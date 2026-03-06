"""Working directory management for bidirectional sync with Onshape.

This module handles syncing of working projects (active development) with
bidirectional sync support - both pull from and push to Onshape.
"""

import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from rich.console import Console
from rich.progress import Progress, SpinnerColumn, TextColumn

from .client import OnshapeClient, OnshapeAPIError
from .git_backup import GitBackup, GitBackupError
from .health_check import HealthChecker, should_run_health_check
from .operations import SyncOperations, SyncResult
from .state import SyncState
from .url_parser import parse_url, OnshapeUrlParseError
from ..models.config import SyncConfig, FolderConfig, DocumentConfig, sanitize_filename
from ..models.project_config import FeatureScriptSettings, ProjectConfig, ReferenceConfig


console = Console()


class WorkingDirectoryManager:
    """Manages working projects with bidirectional sync."""

    def __init__(
        self,
        settings: FeatureScriptSettings,
        settings_path: Path,
        base_dir: Path,
        client: OnshapeClient | None = None,
    ):
        """Initialize the working directory manager.

        Args:
            settings: FeatureScriptSettings configuration
            settings_path: Path to featurescriptSettings.json
            base_dir: Base directory for sync operations
            client: Optional OnshapeClient instance
        """
        self.settings = settings
        self.settings_path = settings_path
        self.base_dir = base_dir
        self.client = client or OnshapeClient()

    def add_project(
        self,
        url: str,
        name: str,
        description: str = "",
        local_path: str | None = None,
        references: list[str] | None = None,
    ) -> ProjectConfig:
        """Add a new working project from an Onshape URL.

        Args:
            url: Onshape URL (document or folder)
            name: Human-readable name for the project
            description: Project description
            local_path: Local path to sync to (defaults to ./projects/{name})
            references: List of reference names this project depends on

        Returns:
            Created ProjectConfig

        Raises:
            OnshapeUrlParseError: If URL is invalid
            OnshapeAPIError: If initial sync fails
        """
        # Parse URL
        parsed = parse_url(url)
        url_type = parsed["type"]

        if url_type not in ("document", "folder", "element"):
            raise OnshapeUrlParseError(f"Project must be a document or folder URL, got: {url_type}")

        # Determine local path
        if local_path is None:
            safe_name = sanitize_filename(name)
            local_path = f"./projects/{safe_name}"

        # Create project config
        proj = ProjectConfig(
            name=name,
            description=description or f"Working project: {name}",
            working_directory=local_path,
            onshape_url=url,
            references=references or [],
            document_id=parsed["document_id"],
            workspace_id=parsed["workspace_id"],
            folder_id=parsed["folder_id"],
            recursive=True,
        )

        # Add to settings
        self.settings.add_project(proj)

        # Perform initial pull
        console.print(f"\n[blue]Performing initial pull for project:[/blue] {name}")
        self.get_working_directory(name, force=True)

        # Save settings
        self.settings.save(self.settings_path)
        console.print(f"[green]Project added:[/green] {name}")

        return proj

    def get_working_directory(
        self,
        project_name: str,
        files: list[str] | None = None,
        force: bool = False,
        dry_run: bool = False,
        auto_backup: bool = True,
        auto_push_backup: bool = False,
    ) -> dict[str, Any]:
        """Pull a working project from Onshape.

        Args:
            project_name: Name of the project
            files: Optional list of specific files to pull (relative to working_directory)
            force: Force pull even if there are local changes
            dry_run: Show what would happen without making changes

        Returns:
            Status report dictionary with keys:
            - success: bool
            - files_updated: int
            - files_skipped: int
            - conflicts: list of conflict descriptions
            - results: list of SyncResult objects

        Raises:
            ValueError: If project not found
            OnshapeAPIError: If sync fails
        """
        proj = self.settings.get_project(project_name)
        if not proj:
            raise ValueError(f"Project not found: {project_name}")

        console.print(f"\n[bold blue]Pulling project:[/bold blue] {proj.name}")

        # Periodic health check
        if should_run_health_check(proj.last_pull, proj.last_push):
            checker = HealthChecker(self.settings, self.base_dir, self.client)
            issues = checker.run_global_checks()
            if issues:
                checker.format_issues(issues)
                if checker.has_blocking_issues(issues):
                    console.print("[red]Health check found blocking issues. Resolve before syncing.[/red]")
                    return {
                        "success": False,
                        "files_updated": 0,
                        "files_skipped": 0,
                        "conflicts": [],
                        "results": [],
                    }

        # Create Git backup before pulling (unless dry-run or disabled)
        if not dry_run and auto_backup:
            try:
                git_backup = GitBackup(self.base_dir)
                backup_result = git_backup.backup_before_sync(
                    operation="pull from Onshape",
                    auto_commit=True,
                    auto_push=auto_push_backup,
                    message=f"Before pulling {proj.name} from Onshape",
                )

                if backup_result["committed"]:
                    console.print("[green]OK[/green] Created Git backup commit")
                    if backup_result["pushed"]:
                        console.print("[green]OK[/green] Pushed backup to remote")
                elif backup_result["had_changes"]:
                    console.print("[yellow]WARNING[/yellow] Had uncommitted changes but backup failed")
            except GitBackupError as e:
                console.print(f"[yellow]⚠[/yellow] Git backup failed: {e}")
                console.print("[yellow]Continuing with pull, but changes are not backed up in Git[/yellow]")

        # Create SyncConfig from ProjectConfig
        sync_config = self._project_to_sync_config(proj)

        # Sanity check: make sure the config has something to pull
        if not sync_config.folders and not sync_config.documents:
            console.print(f"[red]Error: No Onshape source configured for project '{proj.name}'.[/red]")
            console.print(f"  Project has neither a document_id nor a folder_id.")
            console.print(f"  URL: {proj.onshape_url}")
            return {
                "success": False,
                "files_updated": 0,
                "files_skipped": 0,
                "conflicts": [],
                "results": [],
            }

        # Create operations manager
        ops = SyncOperations(sync_config, base_dir=self.base_dir)

        # Check for conflicts before pulling
        conflicts = []
        if not force and not dry_run:
            conflicts = self._check_pull_conflicts(proj, ops)

        if conflicts and not force:
            console.print(f"\n[bold red]Conflicts detected![/bold red]")
            for conflict in conflicts:
                console.print(f"  [red]•[/red] {conflict}")
            console.print("\n[yellow]Use --force to overwrite local changes[/yellow]")

            return {
                "success": False,
                "files_updated": 0,
                "files_skipped": 0,
                "conflicts": conflicts,
                "results": [],
            }

        # Perform pull
        if dry_run:
            console.print("[yellow](DRY RUN - no changes will be made)[/yellow]")

        # Don't use spinner on Windows to avoid unicode issues
        if not dry_run:
            console.print(f"[blue]Downloading files from {proj.name}...[/blue]")
        results = ops.pull_all(dry_run=dry_run, force=force, files=files)

        # Process results
        files_updated = sum(1 for r in results if r.success and not r.skipped)
        files_skipped = sum(1 for r in results if r.skipped)
        failed = [r for r in results if not r.success and not r.conflict]

        # Show results
        for result in results:
            if result.conflict:
                console.print(f"[red]CONFLICT: {result.filepath}[/red]")
                console.print(f"          {result.message}")
            elif not result.success:
                console.print(f"[red]FAILED: {result.filepath}[/red]")
                console.print(f"        {result.message}")
            elif result.skipped:
                # Always show diagnostic skips (no elements found); hide routine up-to-date skips unless verbose
                is_diagnostic = "No Feature Studios" in result.message or "not found" in result.message.lower()
                if is_diagnostic or sync_config.settings.verbose:
                    console.print(f"[dim]{result.message}[/dim]")
            else:
                console.print(f"[green]{result.message}[/green]")

        # Update project metadata if successful
        if not dry_run and files_updated > 0:
            proj.update_pull_time()
            self.settings.save(self.settings_path)

        console.print(f"\n[bold]Summary:[/bold] {files_updated} updated, {files_skipped} skipped, {len(failed)} failed")

        # Completeness check: compare what was requested vs what actually happened
        if not dry_run:
            total_ops = files_updated + files_skipped + len(failed) + len([r for r in results if r.conflict])
            if files is not None:
                n_requested = len(files)
                if files_updated == n_requested:
                    pass  # All good, no need to say anything
                else:
                    console.print(
                        f"\n[bold yellow]Completeness:[/bold yellow] "
                        f"You requested {n_requested} file(s) — "
                        f"{files_updated} downloaded, "
                        f"{files_skipped} skipped (up-to-date), "
                        f"{len(failed)} failed."
                    )
                    if len(failed) > 0:
                        console.print("  Failures are listed above as FAILED. Common causes: file name mismatch, wrong project, or file doesn't exist in Onshape.")
            elif files_updated == 0 and len(failed) == 0 and not any(r.conflict for r in results):
                # Nothing was actually downloaded and nothing hard-failed — explain why
                console.print(
                    f"\n[bold yellow]Completeness:[/bold yellow] "
                    f"0 files were downloaded. Possible reasons:"
                )
                console.print(f"  - The Onshape document has no Feature Studios (only Part Studios, Assemblies, etc.)")
                console.print(f"  - The document ID or workspace ID in the config is stale or wrong")
                console.print(f"  - The API call to list elements returned an empty response")
                console.print(f"  Project URL: {proj.onshape_url}")
                console.print(f"  document_id: {proj.document_id}  workspace_id: {proj.workspace_id}")

        return {
            "success": len(failed) == 0,
            "files_updated": files_updated,
            "files_skipped": files_skipped,
            "conflicts": conflicts,
            "results": results,
        }

    def push_working_directory(
        self,
        project_name: str,
        files: list[str] | None = None,
        force: bool = False,
        dry_run: bool = False,
        auto_backup: bool = True,
        auto_push_backup: bool = False,
    ) -> dict[str, Any]:
        """Push a working project to Onshape.

        Args:
            project_name: Name of the project
            files: Optional list of specific files to push (relative to working_directory)
            force: Force push even if there are remote changes
            dry_run: Show what would happen without making changes

        Returns:
            Status report dictionary with keys:
            - success: bool
            - files_pushed: int
            - files_skipped: int
            - conflicts: list of conflict descriptions
            - results: list of SyncResult objects

        Raises:
            ValueError: If project not found or is read-only
            OnshapeAPIError: If sync fails
        """
        proj = self.settings.get_project(project_name)
        if not proj:
            raise ValueError(f"Project not found: {project_name}")

        # Verify this is not a reference (read-only check)
        from .references import ReferenceManager
        ref_manager = ReferenceManager(self.settings, self.settings_path, self.base_dir, self.client)
        try:
            ref_manager.validate_push_allowed(proj.working_directory)
        except ValueError as e:
            raise ValueError(str(e)) from e

        console.print(f"\n[bold blue]Pushing project:[/bold blue] {proj.name}")

        # Periodic health check
        if should_run_health_check(proj.last_pull, proj.last_push):
            checker = HealthChecker(self.settings, self.base_dir, self.client)
            issues = checker.run_global_checks()
            if issues:
                checker.format_issues(issues)
                if checker.has_blocking_issues(issues):
                    console.print("[red]Health check found blocking issues. Resolve before syncing.[/red]")
                    return {
                        "success": False,
                        "files_pushed": 0,
                        "files_skipped": 0,
                        "conflicts": [],
                        "results": [],
                    }

        # Create Git backup before pushing (unless dry-run or disabled)
        if not dry_run and auto_backup:
            try:
                git_backup = GitBackup(self.base_dir)
                backup_result = git_backup.backup_before_sync(
                    operation="push to Onshape",
                    auto_commit=True,
                    auto_push=auto_push_backup,
                    message=f"Before pushing {proj.name} to Onshape",
                )

                if backup_result["committed"]:
                    console.print("[green]OK[/green] Created Git backup commit")
                    if backup_result["pushed"]:
                        console.print("[green]OK[/green] Pushed backup to remote")
                elif backup_result["had_changes"]:
                    console.print("[yellow]WARNING[/yellow] Had uncommitted changes but backup failed")
            except GitBackupError as e:
                console.print(f"[yellow]⚠[/yellow] Git backup failed: {e}")
                console.print("[yellow]Continuing with push, but changes are not backed up in Git[/yellow]")

        if not dry_run:
            console.print("[bold yellow]WARNING: This will overwrite Feature Studios in Onshape![/bold yellow]")

        # Create SyncConfig from ProjectConfig
        sync_config = self._project_to_sync_config(proj)

        # Create operations manager
        ops = SyncOperations(sync_config, base_dir=self.base_dir)

        # Check for conflicts before pushing
        conflicts = []
        if not force and not dry_run:
            conflicts = self._check_push_conflicts(proj, ops)

        if conflicts and not force:
            console.print(f"\n[bold red]Conflicts detected![/bold red]")
            for conflict in conflicts:
                console.print(f"  [red]•[/red] {conflict}")
            console.print("\n[yellow]Remote has changed since last pull. Use --force to overwrite[/yellow]")

            return {
                "success": False,
                "files_pushed": 0,
                "files_skipped": 0,
                "conflicts": conflicts,
                "results": [],
            }

        # Perform push
        if dry_run:
            console.print("[yellow](DRY RUN - no changes will be made)[/yellow]")

        # Don't use spinner on Windows to avoid unicode issues
        if not dry_run:
            console.print(f"[blue]Uploading files to {proj.name}...[/blue]")
        results = ops.push_all(dry_run=dry_run, force=force, files=files)

        # Process results
        files_pushed = sum(1 for r in results if r.success and not r.skipped)
        files_skipped = sum(1 for r in results if r.skipped)
        failed = [r for r in results if not r.success and not r.conflict]

        # Show results
        for result in results:
            if result.conflict:
                console.print(f"[red]CONFLICT: {result.filepath}[/red]")
                console.print(f"          {result.message}")
            elif not result.success:
                console.print(f"[red]FAILED: {result.filepath}[/red]")
                console.print(f"        {result.message}")
            elif result.skipped:
                if sync_config.settings.verbose:
                    console.print(f"[dim]{result.message}[/dim]")
            else:
                console.print(f"[green]{result.message}[/green]")

        # Update project metadata if successful
        if not dry_run and files_pushed > 0:
            proj.update_push_time()
            self.settings.save(self.settings_path)

        console.print(f"\n[bold]Summary:[/bold] {files_pushed} pushed, {files_skipped} skipped, {len(failed)} failed")

        return {
            "success": len(failed) == 0,
            "files_pushed": files_pushed,
            "files_skipped": files_skipped,
            "conflicts": conflicts,
            "results": results,
        }

    def get_status(self, project_name: str) -> dict[str, Any]:
        """Get sync status for a project.

        Args:
            project_name: Name of the project

        Returns:
            Status dictionary with keys:
            - project: ProjectConfig
            - modified_locally: list of files modified locally
            - modified_remotely: list of files modified remotely
            - in_sync: list of files in sync
            - untracked: list of untracked files

        Raises:
            ValueError: If project not found
        """
        proj = self.settings.get_project(project_name)
        if not proj:
            raise ValueError(f"Project not found: {project_name}")

        console.print(f"\n[bold blue]Status for project:[/bold blue] {proj.name}")

        # Create SyncConfig from ProjectConfig
        sync_config = self._project_to_sync_config(proj)

        # Create operations manager
        ops = SyncOperations(sync_config, base_dir=self.base_dir)

        # Get current state
        state = ops.state

        # Analyze files
        modified_locally = []
        modified_remotely = []
        in_sync = []
        untracked = []

        working_dir = self.base_dir / proj.working_directory
        if working_dir.exists():
            # Check all .fs files in working directory
            for fs_file in working_dir.rglob("*.fs"):
                rel_path = str(fs_file.relative_to(self.base_dir))
                file_state = state.get_file_state(rel_path)

                if file_state:
                    # File is tracked
                    current_hash = ops.state.hash_file(fs_file)
                    if current_hash != file_state.local_hash:
                        modified_locally.append(rel_path)
                    else:
                        # Check if remote has changed (would need microversion check)
                        in_sync.append(rel_path)
                else:
                    # File is not tracked
                    untracked.append(rel_path)

        # Display status
        console.print(f"\n[bold]Project:[/bold] {proj.name}")
        console.print(f"[bold]Working Directory:[/bold] {proj.working_directory}")
        console.print(f"[bold]Last Pull:[/bold] {proj.last_pull[:19] if proj.last_pull else '[dim]Never'}")
        console.print(f"[bold]Last Push:[/bold] {proj.last_push[:19] if proj.last_push else '[dim]Never'}")

        if modified_locally:
            console.print(f"\n[yellow]Modified locally ({len(modified_locally)}):[/yellow]")
            for file in modified_locally:
                console.print(f"  [yellow]M[/yellow] {file}")

        if modified_remotely:
            console.print(f"\n[yellow]Modified remotely ({len(modified_remotely)}):[/yellow]")
            for file in modified_remotely:
                console.print(f"  [yellow]M[/yellow] {file}")

        if untracked:
            console.print(f"\n[dim]Untracked ({len(untracked)}):[/dim]")
            for file in untracked:
                console.print(f"  [dim]?[/dim] {file}")

        if in_sync:
            console.print(f"\n[green]In sync ({len(in_sync)})[/green]")

        return {
            "project": proj,
            "modified_locally": modified_locally,
            "modified_remotely": modified_remotely,
            "in_sync": in_sync,
            "untracked": untracked,
        }

    def list_projects(self) -> list[dict[str, Any]]:
        """List all configured projects.

        Returns:
            List of project info dictionaries
        """
        result: list[dict[str, Any]] = []

        for proj in self.settings.projects:
            info = {
                "name": proj.name,
                "description": proj.description,
                "working_directory": proj.working_directory,
                "onshape_url": proj.onshape_url,
                "last_pull": proj.last_pull,
                "last_push": proj.last_push,
                "references": proj.references,
            }
            result.append(info)

        return result

    def remove_project(self, name: str, delete_files: bool = False) -> bool:
        """Remove a project from configuration.

        Args:
            name: Project name
            delete_files: If True, also delete local files

        Returns:
            True if removed

        Raises:
            ValueError: If project not found
        """
        proj = self.settings.get_project(name)
        if not proj:
            raise ValueError(f"Project not found: {name}")

        # Remove from configuration
        removed = self.settings.remove_project(name)

        if removed:
            # Save settings
            self.settings.save(self.settings_path)

            # Optionally delete files
            if delete_files:
                local_path = self.base_dir / proj.working_directory
                if local_path.exists():
                    import shutil
                    shutil.rmtree(local_path)
                    console.print(f"[yellow]Deleted local files:[/yellow] {local_path}")

            console.print(f"[green]Project removed:[/green] {name}")

        return removed

    def create_file(
        self,
        project_name: str,
        filename: str,
    ) -> dict[str, Any]:
        """Create a new Feature Studio in Onshape and mirror it locally.

        Args:
            project_name: Name of the configured project
            filename: Desired filename (with or without .fs extension)

        Returns:
            Dict with keys: success, filepath, element_id, name
        """
        from .state import SyncState

        proj = self.settings.get_project(project_name)
        if not proj:
            raise ValueError(f"Project not found: {project_name}")

        if not proj.document_id:
            raise ValueError(
                f"Project '{project_name}' has no document_id. "
                "Cannot create a Feature Studio in a folder-based project."
            )

        # Normalize name: Onshape element name has no extension
        if filename.endswith(".fs"):
            element_name = filename[:-3]
            local_filename = filename
        else:
            element_name = filename
            local_filename = f"{filename}.fs"

        ws_id = proj.workspace_id or self.client.get_default_workspace(proj.document_id)

        # Check for name collision in Onshape before creating
        console.print(f"\n[blue]Checking for existing elements in '{proj.name}'...[/blue]")
        try:
            existing = self.client.list_elements(
                document_id=proj.document_id,
                workspace_id=ws_id,
                element_type="FEATURESTUDIO",
            )
            existing_names = {e.get("name", "") for e in existing}
            if element_name in existing_names:
                raise ValueError(
                    f"A Feature Studio named '{element_name}' already exists in '{proj.name}'. "
                    "Choose a different name or pull first to sync it locally."
                )
        except ValueError:
            raise
        except Exception as e:
            console.print(f"[yellow]Warning: could not check for existing elements: {e}[/yellow]")

        # Detect FS version from existing local files, fall back to 2892
        fs_version = _detect_fs_version(self.base_dir / proj.working_directory)

        contents = _featurescript_boilerplate(fs_version)

        console.print(f"[blue]Creating Feature Studio '{element_name}' in Onshape (FeatureScript {fs_version})...[/blue]")

        try:
            response = self.client.create_featurestudio(
                document_id=proj.document_id,
                workspace_id=ws_id,
                name=element_name,
                contents=contents,
            )
        except Exception as e:
            console.print(f"[red]Failed to create Feature Studio in Onshape: {e}[/red]")
            return {"success": False, "error": str(e)}

        element_id = response.get("id", "")
        microversion = response.get("microversion", "")

        if not element_id:
            console.print(f"[red]Onshape did not return an element ID. Response: {response}[/red]")
            return {"success": False, "error": "No element ID in Onshape response"}

        console.print(f"[green]Created in Onshape[/green] (id: {element_id})")

        # Write local file
        local_dir = self.base_dir / proj.working_directory
        local_dir.mkdir(parents=True, exist_ok=True)
        local_path = local_dir / local_filename

        if local_path.exists():
            console.print(f"[yellow]Local file already exists — overwriting: {local_path.relative_to(self.base_dir)}[/yellow]")

        local_path.write_text(contents, encoding="utf-8")
        relative_path = str(local_path.relative_to(self.base_dir))
        console.print(f"[green]Created local file:[/green] {relative_path}")

        # Record in sync state so conflict detection works from the start
        state_file = self.base_dir / ".sync-state.json"
        state = SyncState(state_file)
        state.update_file_state(
            filepath=relative_path,
            local_hash=SyncState.compute_hash(contents),
            remote_microversion=microversion,
            element_id=element_id,
            document_id=proj.document_id,
            workspace_id=ws_id,
        )
        state.save()

        # Update last_pull so health checks know the project is fresh
        proj.update_pull_time()
        self.settings.save(self.settings_path)

        console.print(f"\n[bold green]Done.[/bold green] '{local_filename}' is ready in {proj.working_directory}/")
        return {
            "success": True,
            "filepath": relative_path,
            "element_id": element_id,
            "name": element_name,
        }

    def _project_to_sync_config(self, proj: ProjectConfig) -> SyncConfig:
        """Convert ProjectConfig to SyncConfig for use with SyncOperations.

        Args:
            proj: Project configuration

        Returns:
            SyncConfig instance
        """
        config = SyncConfig(
            base_url=self.settings.onshape.base_url,
            folders=[],
            documents=[],
        )

        if proj.folder_id:
            folder_config = FolderConfig(
                name=proj.name,
                folder_id=proj.folder_id,
                local_path=proj.working_directory,
                recursive=proj.recursive,
            )
            config.folders.append(folder_config)

        elif proj.document_id:
            ws_id = proj.workspace_id or self.client.get_default_workspace(proj.document_id)
            doc_config = DocumentConfig(
                name=proj.name,
                document_id=proj.document_id,
                workspace_id=ws_id,
                local_path=proj.working_directory,
            )
            config.documents.append(doc_config)

        return config

    def _check_pull_conflicts(
        self,
        proj: ProjectConfig,
        ops: SyncOperations,
    ) -> list[str]:
        """Check for conflicts before pulling.

        Args:
            proj: Project configuration
            ops: SyncOperations instance

        Returns:
            List of conflict descriptions
        """
        conflicts = []
        working_dir = self.base_dir / proj.working_directory

        if not working_dir.exists():
            return conflicts  # No conflicts if directory doesn't exist

        # Check all tracked files for local modifications
        for fs_file in working_dir.rglob("*.fs"):
            rel_path = str(fs_file.relative_to(self.base_dir))
            file_state = ops.state.get_file_state(rel_path)

            if file_state:
                current_hash = ops.state.hash_file(fs_file)
                if current_hash != file_state.local_hash:
                    conflicts.append(f"{rel_path} - modified locally since last sync")

        return conflicts

    def _check_push_conflicts(
        self,
        proj: ProjectConfig,
        ops: SyncOperations,
    ) -> list[str]:
        """Check for conflicts before pushing.

        Args:
            proj: Project configuration
            ops: SyncOperations instance

        Returns:
            List of conflict descriptions
        """
        conflicts = []

        # Check if remote has changed since last pull
        # This requires checking microversions for each file
        # For now, we'll implement a simple check based on last_pull timestamp

        # TODO: Implement proper microversion checking
        # This would require querying Onshape for each file's microversion
        # and comparing with cached values

        return conflicts

    def interactive_add_project(
        self,
        url: str,
        name: str,
        description: str | None = None,
        local_path: str | None = None,
        references: list[str] | None = None,
        non_interactive: bool = False,
        verify_exists: bool = True,
    ) -> ProjectConfig:
        """Interactively guide the user through adding a new working project.

        Args:
            url: Onshape URL (document or folder)
            name: Proposed project name
            description: Optional pre-filled description
            local_path: Optional pre-filled local path
            references: Optional pre-filled list of reference names (skips suggestion flow)
            non_interactive: If True, skip all prompts and behave like add_project with checks
            verify_exists: If True, attempt to verify the document exists via API

        Returns:
            Created ProjectConfig

        Raises:
            ValueError: On unresolvable blocking issues in non-interactive mode
            OnshapeUrlParseError: If URL is invalid
        """
        from rich.prompt import Confirm, Prompt
        from rich.panel import Panel
        from .health_check import HealthChecker, IssueSeverity

        # Step 1: Parse URL
        parsed = parse_url(url)
        url_type = parsed["type"]
        if url_type not in ("document", "folder", "element"):
            raise OnshapeUrlParseError(f"Project must be a document or folder URL, got: {url_type}")

        checker = HealthChecker(self.settings, self.base_dir, self.client)

        # Step 2: Resolve name (interactive loop if needed)
        current_name = name
        while True:
            # Build a minimal candidate just to check name
            candidate = ProjectConfig(
                name=current_name,
                description="",
                working_directory=local_path or f"./projects/{sanitize_filename(current_name)}",
                onshape_url=url,
                document_id=parsed["document_id"],
                workspace_id=parsed["workspace_id"],
                folder_id=parsed["folder_id"],
            )
            name_issues = [
                i for i in checker.run_new_project_checks(candidate)
                if i.check_type == "duplicate_name"
            ]
            if not name_issues:
                break

            checker.format_issues(name_issues)
            if non_interactive:
                raise ValueError(f"Project name '{current_name}' conflicts with an existing project or reference.")

            current_name = Prompt.ask(
                "Enter a different name",
                default=current_name + "_2",
            )

        resolved_name = current_name

        # Step 3: Resolve local path
        default_path = f"./projects/{sanitize_filename(resolved_name)}"
        if local_path is None:
            if non_interactive:
                resolved_path = default_path
            else:
                resolved_path = Prompt.ask("Local directory", default=default_path)
        else:
            resolved_path = local_path

        # Step 4: Full candidate health check
        candidate = ProjectConfig(
            name=resolved_name,
            description=description or f"Working project: {resolved_name}",
            working_directory=resolved_path,
            onshape_url=url,
            document_id=parsed["document_id"],
            workspace_id=parsed["workspace_id"],
            folder_id=parsed["folder_id"],
        )
        all_issues = checker.run_new_project_checks(candidate)
        if all_issues:
            checker.format_issues(all_issues)
            if checker.has_blocking_issues(all_issues):
                if non_interactive:
                    raise ValueError("Blocking health check issues found. Resolve before adding project.")
                if not Confirm.ask("Blocking issues found. Continue anyway?", default=False):
                    raise ValueError("Project creation aborted by user.")

        # Step 5: Reference suggestions (interactive only, skip if --references provided)
        if references is not None:
            approved_references = references
        elif non_interactive:
            approved_references = []
        else:
            context = Prompt.ask(
                "Describe what this project does (optional, for reference suggestions)",
                default="",
            )
            if context.strip():
                suggestions = _suggest_references(context, self.settings, self.base_dir)
                approved_references = []
                for ref in suggestions:
                    if Confirm.ask(f"Include reference '{ref.name}'?", default=True):
                        approved_references.append(ref.name)
            else:
                raw = Prompt.ask("Reference names (comma-separated, or blank)", default="")
                approved_references = [r.strip() for r in raw.split(",") if r.strip()]

        # Step 6: Description
        if description is None and not non_interactive:
            description = Prompt.ask(
                "Description (optional)",
                default=f"Working project: {resolved_name}",
            )
        resolved_description = description or f"Working project: {resolved_name}"

        # Step 7: Confirm & create
        if not non_interactive:
            console.print(Panel(
                f"[bold]Name:[/bold] {resolved_name}\n"
                f"[bold]URL:[/bold] {url}\n"
                f"[bold]Local path:[/bold] {resolved_path}\n"
                f"[bold]References:[/bold] {', '.join(approved_references) or '(none)'}\n"
                f"[bold]Description:[/bold] {resolved_description}",
                title="New Project Summary",
                expand=False,
            ))
            if not Confirm.ask("Create this project?", default=True):
                raise ValueError("Project creation aborted by user.")

        # Validate that all named references actually exist in configuration
        if approved_references:
            missing = [r for r in approved_references if not self.settings.get_reference(r)]
            if missing:
                msg = f"References not found in configuration: {', '.join(missing)}"
                if non_interactive:
                    raise ValueError(msg)
                console.print(f"[yellow]WARNING:[/yellow] {msg}")
                if not Confirm.ask("Continue with unresolved references?", default=False):
                    raise ValueError("Project creation aborted by user.")

        # Step 8: Create
        return self.add_project(
            url=url,
            name=resolved_name,
            description=resolved_description,
            local_path=resolved_path,
            references=approved_references,
        )


def _detect_fs_version(working_dir: Path, default: int = 2892) -> int:
    """Scan existing .fs files in working_dir to detect the FeatureScript version in use."""
    try:
        for fs_file in working_dir.glob("*.fs"):
            first_line = fs_file.read_text(encoding="utf-8", errors="ignore").splitlines()[0]
            if first_line.startswith("FeatureScript "):
                try:
                    return int(first_line.split()[1].rstrip(";"))
                except (IndexError, ValueError):
                    pass
    except Exception:
        pass
    return default


def _featurescript_boilerplate(version: int = 2892) -> str:
    """Return minimal FeatureScript boilerplate for a new file."""
    return f'FeatureScript {version};\nimport(path : "onshape/std/common.fs", version : "{version}.0");\n'



def _suggest_references(
    context: str,
    settings: FeatureScriptSettings,
    base_dir: Path,
    max_suggestions: int = 5,
) -> list[ReferenceConfig]:
    """Suggest references based on a plain-text description of the project.

    Scores references by name token overlap and filesystem file-name overlap
    with the context string, returns top matches (score > 0).
    """
    STOPWORDS = {"the", "a", "an", "is", "for", "of", "with", "and", "or", "to", "in"}

    def tokenize(text: str) -> set[str]:
        tokens = re.split(r'[\s\W]+', text.lower())
        return {t for t in tokens if t and t not in STOPWORDS}

    context_tokens = tokenize(context)

    def camel_split(name: str) -> list[str]:
        expanded = re.sub(r'([A-Z])', r' \1', name)
        return [t.lower() for t in expanded.split() if t]

    scored: list[tuple[int, ReferenceConfig]] = []
    for ref in settings.references:
        score = 0

        # Name match: +2 per name token that appears in context (bidirectional substring)
        name_tokens = set(camel_split(ref.name))
        name_tokens |= {t.lower() for t in ref.name.replace("_", " ").replace("-", " ").split()}
        for nt in name_tokens:
            for ct in context_tokens:
                if nt in ct or ct in nt:
                    score += 2
                    break

        # File scan: +1 per filename token that matches a context token
        ref_dir = base_dir / ref.local_path
        try:
            fs_files = list(ref_dir.glob("*.fs"))
        except OSError:
            fs_files = []

        for fs_file in fs_files:
            file_tokens = tokenize(fs_file.stem)
            for ft in file_tokens:
                for ct in context_tokens:
                    if ft in ct or ct in ft:
                        score += 1
                        break

        score = min(score, 10)
        if score > 0:
            scored.append((score, ref))

    scored.sort(key=lambda x: -x[0])
    return [ref for _, ref in scored[:max_suggestions]]
