"""Reference library management for read-only syncing from Onshape.

This module handles syncing of reference libraries (e.g., standard library,
corporate standards) from Onshape to local directories. Reference libraries
are read-only and cannot be pushed back to Onshape.
"""

from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from rich.console import Console
from rich.progress import Progress, SpinnerColumn, TextColumn

from .client import OnshapeClient, OnshapeAPIError
from .operations import SyncOperations
from .url_parser import parse_url, OnshapeUrlParseError
from ..models.config import SyncConfig, FolderConfig, DocumentConfig
from ..models.project_config import FeatureScriptSettings, ReferenceConfig


console = Console()


class ReferenceManager:
    """Manages read-only reference libraries synced from Onshape."""

    def __init__(
        self,
        settings: FeatureScriptSettings,
        settings_path: Path,
        base_dir: Path,
        client: OnshapeClient | None = None,
    ):
        """Initialize the reference manager.

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

    def add_reference(
        self,
        url: str,
        name: str,
        local_path: str | None = None,
        auto_update: bool = False,
        recursive: bool = True,
    ) -> ReferenceConfig:
        """Add a new reference library from an Onshape URL.

        Args:
            url: Onshape URL (document or folder)
            name: Human-readable name for the reference
            local_path: Local path to sync to (defaults to ./references/{name})
            auto_update: Whether to auto-update this reference
            recursive: For folders, whether to include subfolders

        Returns:
            Created ReferenceConfig

        Raises:
            OnshapeUrlParseError: If URL is invalid
            OnshapeAPIError: If initial sync fails
        """
        # Parse URL
        parsed = parse_url(url)
        url_type = parsed["type"]

        if url_type not in ("document", "folder"):
            raise OnshapeUrlParseError(f"Reference must be a document or folder, got: {url_type}")

        # Determine local path
        if local_path is None:
            # Sanitize name for filesystem
            from ..models.config import sanitize_filename
            safe_name = sanitize_filename(name)
            local_path = f"./references/{safe_name}"

        # Create reference config
        ref = ReferenceConfig(
            name=name,
            type=url_type,  # type: ignore
            url=url,
            local_path=local_path,
            read_only=True,
            auto_update=auto_update,
            recursive=recursive,
            document_id=parsed["document_id"],
            workspace_id=parsed["workspace_id"],
            folder_id=parsed["folder_id"],
        )

        # Add to settings
        self.settings.add_reference(ref)

        # Perform initial sync
        console.print(f"\n[blue]Performing initial sync for reference:[/blue] {name}")
        self.update_single_reference(name, force=True)

        # Save settings
        self.settings.save(self.settings_path)
        console.print(f"[green]Reference added:[/green] {name}")

        return ref

    def update_references(
        self,
        force: bool = False,
        check_only: bool = False,
    ) -> dict[str, bool]:
        """Update all configured references (smart sync).

        Args:
            force: Force update even if auto_update is False
            check_only: Only check for updates, don't download

        Returns:
            Dictionary mapping reference name to success status
        """
        results: dict[str, bool] = {}

        if not self.settings.references:
            console.print("[yellow]No references configured")
            return results

        for ref in self.settings.references:
            # Skip if auto_update is False and not forced
            if not ref.auto_update and not force:
                console.print(f"[dim]Skipping {ref.name} (auto_update=false, use --force to update)[/dim]")
                results[ref.name] = True
                continue

            try:
                success = self._update_reference(ref, check_only=check_only)
                results[ref.name] = success
            except Exception as e:
                console.print(f"[red]Failed to update {ref.name}:[/red] {e}")
                results[ref.name] = False

        return results

    def update_single_reference(
        self,
        name: str,
        force: bool = False,
    ) -> bool:
        """Update a specific reference by name.

        Args:
            name: Reference name
            force: Force update regardless of change detection

        Returns:
            True if successful

        Raises:
            ValueError: If reference not found
        """
        ref = self.settings.get_reference(name)
        if not ref:
            raise ValueError(f"Reference not found: {name}")

        return self._update_reference(ref, force=force)

    def _update_reference(
        self,
        ref: ReferenceConfig,
        force: bool = False,
        check_only: bool = False,
    ) -> bool:
        """Update a reference library (internal implementation).

        Args:
            ref: Reference configuration
            force: Force update without checking changes
            check_only: Only check if update needed

        Returns:
            True if successful
        """
        console.print(f"\n[bold blue]Checking reference:[/bold blue] {ref.name}")

        # Check if remote has changed
        needs_update = force
        if not force:
            needs_update = self._check_needs_update(ref)

        if not needs_update:
            console.print(f"[green]Up to date:[/green] {ref.name}")
            return True

        if check_only:
            console.print(f"[yellow]Update available:[/yellow] {ref.name}")
            return True

        # Perform sync using existing SyncOperations
        console.print(f"[blue]Syncing reference:[/blue] {ref.name}")

        # Create temporary SyncConfig from ReferenceConfig
        sync_config = self._reference_to_sync_config(ref)

        ops = SyncOperations(sync_config, base_dir=self.base_dir)

        with Progress(
            SpinnerColumn(),
            TextColumn("[progress.description]{task.description}"),
            console=console,
        ) as progress:
            progress.add_task(description=f"Pulling {ref.name}...", total=None)
            results = ops.pull_all(dry_run=False, force=False)

        # Check results
        success_count = sum(1 for r in results if r.success and not r.skipped)
        failed_count = sum(1 for r in results if not r.success)

        if failed_count > 0:
            console.print(f"[red]Failed to sync {failed_count} files from {ref.name}")
            return False

        # Update last_sync timestamp
        ref.update_sync_time()
        self.settings.save(self.settings_path)

        console.print(f"[green]Successfully synced {success_count} files from {ref.name}")
        return True

    def _check_needs_update(self, ref: ReferenceConfig) -> bool:
        """Check if a reference needs updating.

        Args:
            ref: Reference configuration

        Returns:
            True if update needed
        """
        # If never synced, needs update
        if not ref.last_sync:
            return True

        # For document references, check microversion
        if ref.type == "document" and ref.document_id:
            try:
                ws_id = ref.workspace_id or self.client.get_default_workspace(ref.document_id)
                remote_mv = self.client.get_document_microversion(ref.document_id, ws_id)
                cached_mv = self.settings.get_cached_microversion(ref.document_id)

                if cached_mv and remote_mv != cached_mv:
                    console.print(f"[yellow]Remote microversion changed:[/yellow] {cached_mv} → {remote_mv}")
                    return True

                return cached_mv is None  # No cached version, assume needs update

            except OnshapeAPIError as e:
                console.print(f"[yellow]Warning: Could not check remote version:[/yellow] {e}")
                return False

        # For folders, we don't have a good way to check without listing all documents
        # Default to updating if it's been more than 1 day
        if ref.last_sync:
            try:
                last_sync_dt = datetime.fromisoformat(ref.last_sync)
                age = datetime.now(timezone.utc) - last_sync_dt
                if age.days >= 1:
                    console.print(f"[yellow]Last sync was {age.days} day(s) ago")
                    return True
            except ValueError:
                pass

        return False

    def _reference_to_sync_config(self, ref: ReferenceConfig) -> SyncConfig:
        """Convert ReferenceConfig to SyncConfig for use with SyncOperations.

        Args:
            ref: Reference configuration

        Returns:
            SyncConfig instance
        """
        config = SyncConfig(
            base_url=self.settings.onshape.base_url,
            folders=[],
            documents=[],
        )

        if ref.type == "folder" and ref.folder_id:
            folder_config = FolderConfig(
                name=ref.name,
                folder_id=ref.folder_id,
                local_path=ref.local_path,
                recursive=ref.recursive,
            )
            config.folders.append(folder_config)

        elif ref.type == "document" and ref.document_id:
            ws_id = ref.workspace_id or self.client.get_default_workspace(ref.document_id)
            doc_config = DocumentConfig(
                name=ref.name,
                document_id=ref.document_id,
                workspace_id=ws_id,
                local_path=ref.local_path,
            )
            config.documents.append(doc_config)

        return config

    def list_references(self) -> list[dict[str, Any]]:
        """List all configured references with their status.

        Returns:
            List of reference info dictionaries
        """
        result: list[dict[str, Any]] = []

        for ref in self.settings.references:
            info = {
                "name": ref.name,
                "type": ref.type,
                "url": ref.url,
                "local_path": ref.local_path,
                "auto_update": ref.auto_update,
                "last_sync": ref.last_sync,
                "read_only": ref.read_only,
            }
            result.append(info)

        return result

    def remove_reference(self, name: str, delete_files: bool = False) -> bool:
        """Remove a reference from configuration.

        Args:
            name: Reference name
            delete_files: If True, also delete local files

        Returns:
            True if removed

        Raises:
            ValueError: If reference not found
        """
        ref = self.settings.get_reference(name)
        if not ref:
            raise ValueError(f"Reference not found: {name}")

        # Remove from configuration
        removed = self.settings.remove_reference(name)

        if removed:
            # Save settings
            self.settings.save(self.settings_path)

            # Optionally delete files
            if delete_files:
                local_path = self.base_dir / ref.local_path
                if local_path.exists():
                    import shutil
                    shutil.rmtree(local_path)
                    console.print(f"[yellow]Deleted local files:[/yellow] {local_path}")

            console.print(f"[green]Reference removed:[/green] {name}")

        return removed

    def validate_push_allowed(self, local_path: str) -> None:
        """Validate that a push operation is not targeting a reference directory.

        Args:
            local_path: Path being pushed

        Raises:
            ValueError: If path is a reference directory (read-only)
        """
        # Check if local_path is within any reference directory
        path_obj = Path(local_path).resolve()

        for ref in self.settings.references:
            ref_path = (self.base_dir / ref.local_path).resolve()
            try:
                # Check if path is same or child of reference path
                path_obj.relative_to(ref_path)
                raise ValueError(
                    f"Cannot push to reference directory '{ref.name}' - references are read-only. "
                    f"If you need to modify these files, create a working project instead."
                )
            except ValueError:
                # Not a child of this reference, continue checking
                pass
