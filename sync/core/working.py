"""Working directory management for bidirectional sync with Onshape.

This module handles syncing of working projects (active development) with
bidirectional sync support - both pull from and push to Onshape.
"""

from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from rich.console import Console
from rich.progress import Progress, SpinnerColumn, TextColumn

from .client import OnshapeClient, OnshapeAPIError
from .operations import SyncOperations, SyncResult
from .state import SyncState
from .url_parser import parse_url, OnshapeUrlParseError
from ..models.config import SyncConfig, FolderConfig, DocumentConfig, sanitize_filename
from ..models.project_config import FeatureScriptSettings, ProjectConfig


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

        if url_type not in ("document", "folder"):
            raise OnshapeUrlParseError(f"Project must be a document or folder, got: {url_type}")

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
        force: bool = False,
        dry_run: bool = False,
    ) -> dict[str, Any]:
        """Pull a working project from Onshape.

        Args:
            project_name: Name of the project
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

        # Create SyncConfig from ProjectConfig
        sync_config = self._project_to_sync_config(proj)

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

        with Progress(
            SpinnerColumn(),
            TextColumn("[progress.description]{task.description}"),
            console=console,
        ) as progress:
            progress.add_task(description=f"Pulling {proj.name}...", total=None)
            results = ops.pull_all(dry_run=dry_run, force=force)

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
                if sync_config.settings.verbose:
                    console.print(f"[dim]{result.message}[/dim]")
            else:
                console.print(f"[green]{result.message}[/green]")

        # Update project metadata if successful
        if not dry_run and files_updated > 0:
            proj.update_pull_time()
            self.settings.save(self.settings_path)

        console.print(f"\n[bold]Summary:[/bold] {files_updated} updated, {files_skipped} skipped, {len(failed)} failed")

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

        # TODO: Implement selective file push if files list is provided
        # For now, push all files

        with Progress(
            SpinnerColumn(),
            TextColumn("[progress.description]{task.description}"),
            console=console,
        ) as progress:
            progress.add_task(description=f"Pushing {proj.name}...", total=None)
            results = ops.push_all(dry_run=dry_run, force=force)

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
                    current_hash = ops._compute_file_hash(fs_file)
                    if current_hash != file_state.hash:
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
                current_hash = ops._compute_file_hash(fs_file)
                if current_hash != file_state.hash:
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
