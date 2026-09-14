"""Pull and push sync operations."""

import json
import shutil
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from ..models.config import (
    DocumentConfig,
    DocumentMetadata,
    FolderConfig,
    SyncConfig,
    sanitize_filename,
)
from .client import OnshapeClient, OnshapeAPIError
from .state import ConflictType, SyncState


@dataclass
class SyncResult:
    """Result of a sync operation."""

    success: bool
    filepath: str
    operation: str  # "pull" or "push"
    message: str
    conflict: bool = False
    skipped: bool = False
    element_id: str = ""
    created: bool = False  # push created the element in Onshape


class SyncOperations:
    """Handles pull and push operations between local files and Onshape."""

    METADATA_FILENAME = ".document.json"

    def __init__(
        self,
        config: SyncConfig,
        client: OnshapeClient | None = None,
        state: SyncState | None = None,
        base_dir: Path | None = None,
    ) -> None:
        """Initialize sync operations.

        Args:
            config: Sync configuration
            client: OnshapeClient (created if not provided)
            state: SyncState manager (created if not provided)
            base_dir: Base directory for sync operations
        """
        self.config = config
        self._client = client
        self.base_dir = base_dir or Path.cwd()

        # Initialize state manager
        state_file = self.base_dir / ".sync-state.json"
        self.state = state or SyncState(state_file)

    @property
    def client(self) -> OnshapeClient:
        """Get or create OnshapeClient."""
        if self._client is None:
            self._client = OnshapeClient()
        return self._client

    def _backup_file(self, filepath: Path) -> Path | None:
        """Create backup of a file before overwriting."""
        if not self.config.settings.backup_on_pull:
            return None

        if not filepath.exists():
            return None

        backup_dir = self.base_dir / self.config.settings.backup_dir
        backup_dir.mkdir(parents=True, exist_ok=True)

        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        backup_name = f"{filepath.stem}_{timestamp}{filepath.suffix}"
        backup_path = backup_dir / backup_name

        shutil.copy2(filepath, backup_path)
        return backup_path

    def _build_onshape_url(self, document_id: str, workspace_id: str, element_id: str = "") -> str:
        """Build a URL to an Onshape document/element."""
        base = self.config.base_url
        url = f"{base}/documents/{document_id}/w/{workspace_id}"
        if element_id:
            url += f"/e/{element_id}"
        return url

    def _save_document_metadata(
        self,
        local_dir: Path,
        document_id: str,
        workspace_id: str,
        document_name: str,
        folder_path: str,
        feature_studios: dict[str, str],
        tab_folders: dict[str, str] | None = None,
    ) -> None:
        """Save .document.json metadata file."""
        metadata = DocumentMetadata(
            document_id=document_id,
            workspace_id=workspace_id,
            document_name=document_name,
            folder_path=folder_path,
            onshape_url=self._build_onshape_url(document_id, workspace_id),
            last_sync=datetime.now(timezone.utc).isoformat(),
            feature_studios=feature_studios,
            tab_folders=tab_folders or {},
        )

        metadata_path = local_dir / self.METADATA_FILENAME
        with open(metadata_path, "w", encoding="utf-8") as f:
            json.dump(metadata.to_dict(), f, indent=2)
            f.write("\n")

    def _update_document_metadata(
        self,
        local_dir: Path,
        feature_studios: dict[str, str] | None = None,
        tab_folders: dict[str, str] | None = None,
    ) -> None:
        """Merge into an existing .document.json (no-op if the file is absent)."""
        meta = self._load_document_metadata(local_dir)
        if meta is None:
            return
        if feature_studios:
            meta.feature_studios.update(feature_studios)
        if tab_folders is not None:
            meta.tab_folders = tab_folders
        meta.last_sync = datetime.now(timezone.utc).isoformat()
        with open(local_dir / self.METADATA_FILENAME, "w", encoding="utf-8") as f:
            json.dump(meta.to_dict(), f, indent=2)
            f.write("\n")

    def _load_document_metadata(self, local_dir: Path) -> DocumentMetadata | None:
        """Load .document.json metadata file if it exists."""
        metadata_path = local_dir / self.METADATA_FILENAME
        if not metadata_path.exists():
            return None

        with open(metadata_path, encoding="utf-8") as f:
            data = json.load(f)
        return DocumentMetadata.from_dict(data)

    # =========================================================================
    # Folder-Based Sync Operations (New)
    # =========================================================================

    def pull_folder(
        self,
        folder_config: FolderConfig,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
        tab_folder: str | None = None,
        save_json: bool = False,
    ) -> list[SyncResult]:
        """Pull all documents from an Onshape folder.

        Each document becomes a local folder containing its Feature Studios.

        Args:
            folder_config: Folder configuration
            dry_run: If True, show what would happen without making changes
            force: If True, overwrite local changes

        Returns:
            List of SyncResults
        """
        results: list[SyncResult] = []
        local_root = self.base_dir / folder_config.local_path

        try:
            # List all documents in the folder (recursively if configured)
            documents = self.client.list_folder_documents(
                folder_id=folder_config.folder_id,
                recursive=folder_config.recursive,
            )

            if not documents:
                results.append(SyncResult(
                    success=True,
                    filepath=folder_config.local_path,
                    operation="pull",
                    message="No documents found in folder",
                    skipped=True,
                ))
                return results

            for doc_info in documents:
                doc_id = doc_info["id"]
                doc_name = doc_info["name"]
                folder_path = doc_info.get("folder_path", "")

                # Check exclude patterns
                relative_path = f"{folder_path}/{doc_name}" if folder_path else doc_name
                if folder_config.should_exclude(relative_path):
                    results.append(SyncResult(
                        success=True,
                        filepath=relative_path,
                        operation="pull",
                        message=f"Excluded by pattern",
                        skipped=True,
                    ))
                    continue

                # Build local path for this document
                doc_folder_name = sanitize_filename(doc_name)
                if folder_path:
                    sanitized_folder_path = "/".join(
                        sanitize_filename(p) for p in folder_path.split("/")
                    )
                    local_doc_dir = local_root / sanitized_folder_path / doc_folder_name
                else:
                    local_doc_dir = local_root / doc_folder_name

                # Pull this document's Feature Studios
                doc_results = self._pull_document_to_folder(
                    document_id=doc_id,
                    document_name=doc_name,
                    folder_path=folder_path,
                    local_dir=local_doc_dir,
                    dry_run=dry_run,
                    force=force,
                    files=files,
                    tab_folder=tab_folder,
                    save_json=save_json,
                )
                results.extend(doc_results)

        except Exception as e:
            results.append(SyncResult(
                success=False,
                filepath=folder_config.local_path,
                operation="pull",
                message=f"Failed to list folder: {e}",
            ))

        return results

    def _pull_document_to_folder(
        self,
        document_id: str,
        document_name: str,
        folder_path: str,
        local_dir: Path,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
        tab_folder: str | None = None,
        save_json: bool = False,
    ) -> list[SyncResult]:
        """Pull a single document's Feature Studios to a local folder.

        Args:
            document_id: Onshape document ID
            document_name: Document name
            folder_path: Path within Onshape folder hierarchy
            local_dir: Local directory for this document
            dry_run: If True, show what would happen
            force: If True, overwrite local changes
            tab_folder: If set, only pull files from this tab folder (by name)
            save_json: If True, save raw elements API response as elements.json

        Returns:
            List of SyncResults
        """
        results: list[SyncResult] = []

        try:
            # Get default workspace for this document
            workspace_id = self.client.get_default_workspace(document_id)
            if not workspace_id:
                workspace_id = self.config.settings.default_workspace

            # Fetch all elements so we can detect tab folders
            all_elements = self.client.list_elements(
                document_id=document_id,
                workspace_id=workspace_id,
            )

            if save_json:
                local_dir.mkdir(parents=True, exist_ok=True)
                json_path = local_dir / "elements.json"
                with open(json_path, "w", encoding="utf-8") as f:
                    json.dump(all_elements, f, indent=2)
                    f.write("\n")

            fs_elements = [e for e in all_elements if e.get("elementType") == "FEATURESTUDIO"]

            # Apply --folder filter: only elements whose local file already lives under local_dir/tab_folder
            if tab_folder:
                folder_path_filter = local_dir / tab_folder
                ext = self.config.settings.file_extension
                tracked_stems = {f.stem for f in folder_path_filter.rglob(f"*{ext}")} if folder_path_filter.exists() else set()
                if not tracked_stems:
                    results.append(SyncResult(
                        success=False,
                        filepath=str(local_dir.relative_to(self.base_dir)),
                        operation="pull",
                        message=f"No local files found under '{tab_folder}' — move files there first, then re-pull",
                    ))
                    return results
                fs_elements = [e for e in fs_elements if sanitize_filename(e.get("name", "")) in tracked_stems]

            if not fs_elements:
                results.append(SyncResult(
                    success=True,
                    filepath=str(local_dir.relative_to(self.base_dir)),
                    operation="pull",
                    message=f"No Feature Studios in document '{document_name}'",
                    skipped=True,
                ))
                return results

            # Filter elements if specific files requested
            if files:
                files_set = {f if f.endswith(".fs") else f"{f}.fs" for f in files}
                available_fs = {f"{e.get('name', '')}.fs" for e in fs_elements if e.get("name", "")}
                fs_elements = [e for e in fs_elements if f"{e.get('name', '')}.fs" in files_set or e.get('name', '') in files_set]
                for req in sorted(files_set):
                    if req not in available_fs:
                        available_str = ", ".join(sorted(available_fs)) or "none"
                        results.append(SyncResult(
                            success=False,
                            filepath=f"{local_dir.relative_to(self.base_dir)}/{req}",
                            operation="pull",
                            message=f"'{req}' not found in Onshape document '{document_name}'. Available Feature Studios: {available_str}",
                        ))

            ext = self.config.settings.file_extension

            if dry_run:
                for element in fs_elements:
                    elem_name = element.get("name", "unnamed")
                    safe_name = sanitize_filename(elem_name)
                    filename = safe_name if safe_name.endswith(ext) else f"{safe_name}{ext}"
                    existing = list(local_dir.rglob(filename)) if local_dir.exists() else []
                    element_dir = existing[0].parent if existing else local_dir
                    results.append(SyncResult(
                        success=True,
                        filepath=str((element_dir / filename).relative_to(self.base_dir)),
                        operation="pull",
                        message=f"[DRY RUN] Would pull {filename}",
                        skipped=True,
                    ))
                return results

            # Create local directory
            local_dir.mkdir(parents=True, exist_ok=True)

            feature_studios: dict[str, str] = {}

            for element in fs_elements:
                element_id = element.get("id", "")
                element_name = element.get("name", "unnamed")

                # Respect user's local folder organization: pull to wherever the file already lives
                safe_name = sanitize_filename(element_name)
                filename = safe_name if safe_name.endswith(ext) else f"{safe_name}{ext}"
                existing = list(local_dir.rglob(filename))
                element_dir = existing[0].parent if existing else local_dir

                result = self._pull_feature_studio(
                    document_id=document_id,
                    workspace_id=workspace_id,
                    element_id=element_id,
                    element_name=element_name,
                    local_dir=element_dir,
                    force=force,
                    # The elements listing already told us each tab's microversion, so an
                    # unchanged tab can be recognised without fetching its contents.
                    remote_microversion=element.get("microversionId", ""),
                )
                results.append(result)

                if result.success:
                    feature_studios[element_name] = element_id

            self._save_document_metadata(
                local_dir=local_dir,
                document_id=document_id,
                workspace_id=workspace_id,
                document_name=document_name,
                folder_path=folder_path,
                feature_studios=feature_studios,
            )

        except Exception as e:
            results.append(SyncResult(
                success=False,
                filepath=str(local_dir),
                operation="pull",
                message=f"Failed to pull document: {e}",
            ))

        return results

    def _pull_feature_studio(
        self,
        document_id: str,
        workspace_id: str,
        element_id: str,
        element_name: str,
        local_dir: Path,
        force: bool = False,
        remote_microversion: str = "",
    ) -> SyncResult:
        """Pull a single Feature Studio to a local file.

        Args:
            document_id: Document ID
            workspace_id: Workspace ID
            element_id: Element ID
            element_name: Element name (becomes filename)
            local_dir: Directory to save file in
            force: If True, overwrite local changes
            remote_microversion: The tab's microversionId from the elements listing.
                When it matches what we recorded last sync and the local file is
                untouched, the tab is skipped without fetching its contents.

        Returns:
            SyncResult
        """
        # Sanitize element name and ensure it has the correct extension
        filename = sanitize_filename(element_name)
        if not filename.endswith(self.config.settings.file_extension):
            filename += self.config.settings.file_extension
        filepath = local_dir / filename
        relative_path = str(filepath.relative_to(self.base_dir))

        # Nothing changed on either side: no request, no write, and it is reported as
        # skipped rather than counted as an update. This is the common case -- most tabs in
        # a document are untouched by any given edit -- and it is what stops a one-file
        # change from reading as "14 updated".
        if not force and remote_microversion:
            file_state = self.state.get_file_state(relative_path)
            if (
                file_state is not None
                and file_state.remote_microversion == remote_microversion
                and filepath.exists()
                and SyncState.hash_file(filepath) == file_state.local_hash
            ):
                return SyncResult(
                    success=True,
                    filepath=relative_path,
                    operation="pull",
                    message=f"Skipped {filename} (unchanged)",
                    skipped=True,
                )

        try:
            # Get remote content
            response = self.client.get_featurestudio_contents(
                document_id=document_id,
                workspace_id=workspace_id,
                element_id=element_id,
            )

            remote_content = response.get("contents", "")
            # The contents endpoint does not return a microversion, so the one from the
            # elements listing is the only real value; falling back to the response kept the
            # recorded microversion permanently empty, which silently disabled both the
            # conflict check and any chance of skipping.
            if not remote_microversion:
                remote_microversion = response.get("microversion", "") or ""

            # Check for conflicts
            if not force:
                conflict = self.state.detect_pull_conflict(
                    relative_path, filepath, remote_microversion
                )

                if conflict.conflict_type == ConflictType.BOTH_CHANGED:
                    return SyncResult(
                        success=False,
                        filepath=relative_path,
                        operation="pull",
                        message=conflict.message,
                        conflict=True,
                    )

            # The microversion moved but the text did not -- a save that changed nothing,
            # or a tab we have never recorded. Record the new microversion so the next sync
            # can skip it outright, but do not rewrite the file or count it as an update.
            local_hash = SyncState.compute_hash(remote_content)
            unchanged = filepath.exists() and SyncState.hash_file(filepath) == local_hash

            if unchanged:
                self.state.update_file_state(
                    filepath=relative_path,
                    local_hash=local_hash,
                    remote_microversion=remote_microversion,
                    element_id=element_id,
                    document_id=document_id,
                    workspace_id=workspace_id,
                )
                self.state.save()
                return SyncResult(
                    success=True,
                    filepath=relative_path,
                    operation="pull",
                    message=f"Skipped {filename} (unchanged)",
                    skipped=True,
                )

            # Backup if needed
            self._backup_file(filepath)

            # Write file with UTF-8 encoding (FeatureScript may contain unicode)
            filepath.write_text(remote_content, encoding="utf-8")

            # Update state
            self.state.update_file_state(
                filepath=relative_path,
                local_hash=local_hash,
                remote_microversion=remote_microversion,
                element_id=element_id,
                document_id=document_id,
                workspace_id=workspace_id,
            )
            self.state.save()

            return SyncResult(
                success=True,
                filepath=relative_path,
                operation="pull",
                message=f"Pulled {filename}",
            )

        except Exception as e:
            return SyncResult(
                success=False,
                filepath=relative_path,
                operation="pull",
                message=f"Failed: {e}",
            )

    def push_folder(
        self,
        folder_config: FolderConfig,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
    ) -> list[SyncResult]:
        """Push local files to an Onshape folder.

        Reads .document.json metadata to map local folders back to Onshape documents.

        Args:
            folder_config: Folder configuration
            dry_run: If True, show what would happen
            force: If True, overwrite remote changes

        Returns:
            List of SyncResults
        """
        results: list[SyncResult] = []
        local_root = self.base_dir / folder_config.local_path

        if not local_root.exists():
            results.append(SyncResult(
                success=False,
                filepath=folder_config.local_path,
                operation="push",
                message=f"Local path not found: {local_root}",
            ))
            return results

        # Find all document folders (directories with .document.json)
        for metadata_file in local_root.rglob(self.METADATA_FILENAME):
            doc_dir = metadata_file.parent

            # Check exclude patterns
            relative_dir = doc_dir.relative_to(local_root)
            if folder_config.should_exclude(str(relative_dir)):
                results.append(SyncResult(
                    success=True,
                    filepath=str(relative_dir),
                    operation="push",
                    message="Excluded by pattern",
                    skipped=True,
                ))
                continue

            doc_results = self._push_document_folder(
                doc_dir=doc_dir,
                dry_run=dry_run,
                force=force,
            )
            results.extend(doc_results)

        return results

    def _push_document_folder(
        self,
        doc_dir: Path,
        dry_run: bool = False,
        force: bool = False,
    ) -> list[SyncResult]:
        """Push a local document folder to Onshape.

        Args:
            doc_dir: Local document directory
            dry_run: If True, show what would happen
            force: If True, overwrite remote changes

        Returns:
            List of SyncResults
        """
        results: list[SyncResult] = []

        # Load metadata
        metadata = self._load_document_metadata(doc_dir)
        if metadata is None:
            results.append(SyncResult(
                success=False,
                filepath=str(doc_dir),
                operation="push",
                message=f"No {self.METADATA_FILENAME} found - cannot push",
            ))
            return results

        # Find all .fs files in this directory
        extension = self.config.settings.file_extension
        for fs_file in doc_dir.glob(f"*{extension}"):
            element_name = fs_file.stem

            # Look up element ID from metadata
            element_id = metadata.feature_studios.get(element_name)
            if not element_id:
                results.append(SyncResult(
                    success=False,
                    filepath=str(fs_file.relative_to(self.base_dir)),
                    operation="push",
                    message=f"No element ID found for {element_name} - pull first",
                ))
                continue

            result = self._push_feature_studio(
                filepath=fs_file,
                document_id=metadata.document_id,
                workspace_id=metadata.workspace_id,
                element_id=element_id,
                dry_run=dry_run,
                force=force,
            )
            results.append(result)

        return results

    def _push_feature_studio(
        self,
        filepath: Path,
        document_id: str,
        workspace_id: str,
        element_id: str,
        dry_run: bool = False,
        force: bool = False,
    ) -> SyncResult:
        """Push a single Feature Studio file to Onshape.

        Args:
            filepath: Local file path
            document_id: Document ID
            workspace_id: Workspace ID
            element_id: Element ID
            dry_run: If True, show what would happen
            force: If True, overwrite remote changes

        Returns:
            SyncResult
        """
        relative_path = str(filepath.relative_to(self.base_dir))

        # Handle dry-run early - no API calls needed
        if dry_run:
            return SyncResult(
                success=True,
                filepath=relative_path,
                operation="push",
                message=f"[DRY RUN] Would push {filepath.name}",
                skipped=True,
            )

        try:
            # Read file content early so we can hash-check before any API calls
            local_content = filepath.read_text(encoding="utf-8")

            # Skip if unchanged since last sync (unless forcing)
            if not force:
                file_state = self.state.get_file_state(relative_path)
                if file_state:
                    current_hash = SyncState.compute_hash(local_content)
                    if current_hash == file_state.local_hash:
                        return SyncResult(
                            success=True,
                            filepath=relative_path,
                            operation="push",
                            message=f"Skipped {filepath.name} (unchanged)",
                            skipped=True,
                        )

            # Get current remote microversion for conflict check (optional)
            remote_microversion = ""
            try:
                remote_microversion = self.client.get_document_microversion(
                    document_id=document_id,
                    workspace_id=workspace_id,
                )
            except OnshapeAPIError:
                # Document microversion endpoint may not be available on all instances
                # Skip conflict detection if this fails
                pass

            # Check for conflicts (only if we got a microversion and not forcing)
            if not force and remote_microversion:
                conflict = self.state.detect_push_conflict(relative_path, remote_microversion)
                if conflict.conflict_type == ConflictType.BOTH_CHANGED:
                    return SyncResult(
                        success=False,
                        filepath=relative_path,
                        operation="push",
                        message=conflict.message,
                        conflict=True,
                    )

            # Push to Onshape
            response = self.client.update_featurestudio_contents(
                document_id=document_id,
                workspace_id=workspace_id,
                element_id=element_id,
                contents=local_content,
            )

            new_microversion = response.get("microversion", "")

            # Update state
            local_hash = SyncState.compute_hash(local_content)
            self.state.update_file_state(
                filepath=relative_path,
                local_hash=local_hash,
                remote_microversion=new_microversion,
                element_id=element_id,
                document_id=document_id,
                workspace_id=workspace_id,
            )
            self.state.save()

            return SyncResult(
                success=True,
                filepath=relative_path,
                operation="push",
                message=f"Pushed {filepath.name}",
            )

        except Exception as e:
            return SyncResult(
                success=False,
                filepath=relative_path,
                operation="push",
                message=f"Failed: {e}",
            )

    # =========================================================================
    # Legacy Document-Based Sync (for backwards compatibility)
    # =========================================================================

    def pull_document(
        self,
        doc_config: DocumentConfig,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
        tab_folder: str | None = None,
        save_json: bool = False,
        folders: bool = False,
    ) -> list[SyncResult]:
        """Pull all Feature Studios from a document (legacy method).

        With folders=True the Onshape tab folders are read via the browser and a
        Feature Studio with no local file yet lands in the matching subdirectory.

        Args:
            doc_config: Document configuration
            dry_run: Show what would happen without making changes
            force: Force pull even if there are local changes
            files: Optional list of specific files to pull (basenames like "file.fs")
            tab_folder: If set, only pull files from this tab folder (by name)
            save_json: If True, save raw elements API response as elements.json
        """
        results: list[SyncResult] = []
        local_dir = self.base_dir / doc_config.local_path

        try:
            # Fetch all elements (no type filter) so we can detect tab folders
            all_elements = self.client.list_elements(
                document_id=doc_config.document_id,
                workspace_id=doc_config.workspace_id,
            )

            if save_json:
                local_dir.mkdir(parents=True, exist_ok=True)
                json_path = local_dir / "elements.json"
                with open(json_path, "w", encoding="utf-8") as f:
                    json.dump(all_elements, f, indent=2)
                    f.write("\n")

            fs_elements = [e for e in all_elements if e.get("elementType") == "FEATURESTUDIO"]

            # Apply --folder filter: only elements whose local file already lives under local_dir/tab_folder
            if tab_folder:
                folder_path = local_dir / tab_folder
                ext = self.config.settings.file_extension
                tracked_stems = {f.stem for f in folder_path.rglob(f"*{ext}")} if folder_path.exists() else set()
                if not tracked_stems:
                    results.append(SyncResult(
                        success=False,
                        filepath=doc_config.local_path,
                        operation="pull",
                        message=f"No local files found under '{tab_folder}' — move files there first, then re-pull",
                    ))
                    return results
                fs_elements = [e for e in fs_elements if sanitize_filename(e.get("name", "")) in tracked_stems]

            # Filter elements if specific files requested
            if files:
                files_set = {f if f.endswith(".fs") else f"{f}.fs" for f in files}
                available_fs = {f"{e.get('name', '')}.fs" for e in fs_elements if e.get("name", "")}
                fs_elements = [e for e in fs_elements if f"{e.get('name', '')}.fs" in files_set or e.get('name', '') in files_set]
                for req in sorted(files_set):
                    if req not in available_fs:
                        available_str = ", ".join(sorted(available_fs)) or "none"
                        results.append(SyncResult(
                            success=False,
                            filepath=f"{doc_config.local_path}/{req}",
                            operation="pull",
                            message=f"'{req}' not found in Onshape document '{doc_config.name}'. Available Feature Studios: {available_str}",
                        ))

            ext = self.config.settings.file_extension

            if dry_run:
                for element in fs_elements:
                    element_name = element.get("name", "unnamed")
                    safe_name = sanitize_filename(element_name)
                    filename = safe_name if safe_name.endswith(ext) else f"{safe_name}{ext}"
                    existing = list(local_dir.rglob(filename)) if local_dir.exists() else []
                    element_dir = existing[0].parent if existing else local_dir
                    results.append(SyncResult(
                        success=True,
                        filepath=str((element_dir / filename).relative_to(self.base_dir)),
                        operation="pull",
                        message=f"[DRY RUN] Would pull {filename}",
                        skipped=True,
                    ))
                return results

            # Create local directory if it doesn't exist
            local_dir.mkdir(parents=True, exist_ok=True)

            tab_folders: dict[str, str] | None = None
            if folders:
                from .tabfolders import read_tab_folders
                try:
                    tab_folders = read_tab_folders(
                        doc_config.document_id, doc_config.workspace_id, fs_elements[0]["id"]
                    )
                except Exception as e:
                    results.append(SyncResult(
                        success=True, filepath=doc_config.local_path, operation="pull", skipped=True,
                        message=f"Could not read Onshape tab folders ({e}); new files go to the project root",
                    ))

            feature_studios: dict[str, str] = {}
            for element in fs_elements:
                element_id = element.get("id", "")
                element_name = element.get("name", "")

                if not element_id or not element_name:
                    results.append(SyncResult(
                        success=False,
                        filepath=doc_config.local_path,
                        operation="pull",
                        message=f"Onshape returned an element with missing id or name (id={element_id!r}, name={element_name!r}) — skipping",
                    ))
                    continue

                # Respect user's local folder organization: pull to wherever the file already
                # lives; a new file mirrors its Onshape tab folder when we know it.
                safe_name = sanitize_filename(element_name)
                filename = safe_name if safe_name.endswith(ext) else f"{safe_name}{ext}"
                existing = list(local_dir.rglob(filename))
                if existing:
                    element_dir = existing[0].parent
                elif tab_folders and tab_folders.get(element_name):
                    element_dir = local_dir / tab_folders[element_name]
                    element_dir.mkdir(parents=True, exist_ok=True)
                else:
                    element_dir = local_dir

                result = self._pull_feature_studio(
                    document_id=doc_config.document_id,
                    workspace_id=doc_config.workspace_id,
                    element_id=element_id,
                    element_name=element_name,
                    local_dir=element_dir,
                    force=force,
                    # As in _pull_document_to_folder: the listing already carries each tab's
                    # microversion, so an untouched tab needs no request.
                    remote_microversion=element.get("microversionId", ""),
                )
                results.append(result)

                if result.success:
                    feature_studios[element_name] = element_id

            if not results and not files:
                results.append(SyncResult(
                    success=True,
                    filepath=doc_config.local_path,
                    operation="pull",
                    message=f"No Feature Studios found in Onshape document '{doc_config.name}'",
                    skipped=True,
                ))

            if tab_folders is None:
                previous = self._load_document_metadata(local_dir)
                tab_folders = previous.tab_folders if previous else {}
            # The name->id map covers every Feature Studio in the document, not
            # just the ones pulled this time (a --files pull must not shrink it).
            all_studios = {
                e["name"]: e["id"]
                for e in all_elements
                if e.get("elementType") == "FEATURESTUDIO" and e.get("name") and e.get("id")
            }
            self._save_document_metadata(
                local_dir=local_dir,
                document_id=doc_config.document_id,
                workspace_id=doc_config.workspace_id,
                document_name=doc_config.name,
                folder_path="",
                feature_studios=all_studios,
                tab_folders=tab_folders,
            )

        except Exception as e:
            results.append(SyncResult(
                success=False,
                filepath=doc_config.local_path,
                operation="pull",
                message=f"Failed to list elements: {e}",
            ))

        return results

    def push_document(
        self,
        doc_config: DocumentConfig,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
        tab_folder: str | None = None,
        create: bool = False,
    ) -> list[SyncResult]:
        """Push all local Feature Studios to a document (legacy method).

        Args:
            doc_config: Document configuration
            dry_run: Show what would happen without making changes
            force: Force push even if there are remote changes
            files: Optional list of specific files to push (basenames like "file.fs")
            tab_folder: If set, only push files from this tab folder (by local dir name)
            create: Create a Feature Studio for any local file with no matching tab
        """
        results: list[SyncResult] = []
        local_dir = self.base_dir / doc_config.local_path

        if not local_dir.exists():
            results.append(SyncResult(
                success=False,
                filepath=doc_config.local_path,
                operation="push",
                message=f"Local directory not found: {local_dir}",
            ))
            return results

        try:
            elements = self.client.list_elements(
                document_id=doc_config.document_id,
                workspace_id=doc_config.workspace_id,
                element_type="FEATURESTUDIO",
            )

            name_to_id = {e.get("name", ""): e.get("id", "") for e in elements}

            extension = self.config.settings.file_extension
            local_files = list(local_dir.rglob(f"*{extension}"))

            # Filter to a local subdirectory if requested
            if tab_folder:
                folder_path = local_dir / tab_folder
                local_files = [f for f in local_files if f.is_relative_to(folder_path)]

            # Filter files if specific files requested
            if files:
                # Normalize file list (ensure .fs extension, handle relative paths)
                files_set = {f if f.endswith(".fs") else f"{f}.fs" for f in files}
                # Also check basenames only (in case full paths were provided)
                files_basenames = {Path(f).name for f in files_set}
                local_files = [f for f in local_files if f.name in files_basenames]

            created: dict[str, str] = {}
            for local_file in local_files:
                element_name = local_file.stem
                element_id = name_to_id.get(element_name)

                if not element_id and create:
                    rel_path = str(local_file.relative_to(self.base_dir))
                    if dry_run:
                        results.append(SyncResult(
                            success=True, filepath=rel_path, operation="push",
                            message=f"[DRY RUN] Would create {element_name} in Onshape", skipped=True,
                        ))
                        continue
                    contents = local_file.read_text(encoding="utf-8")
                    resp = self.client.create_featurestudio(
                        document_id=doc_config.document_id,
                        workspace_id=doc_config.workspace_id,
                        name=element_name,
                        contents=contents,
                    )
                    element_id = resp.get("id", "")
                    if not element_id:
                        results.append(SyncResult(
                            success=False, filepath=rel_path, operation="push",
                            message=f"Onshape returned no element id creating {element_name}: {resp}",
                        ))
                        continue
                    name_to_id[element_name] = element_id
                    created[element_name] = element_id
                    self.state.update_file_state(
                        filepath=rel_path,
                        local_hash=SyncState.compute_hash(contents),
                        remote_microversion=resp.get("microversion", ""),
                        element_id=element_id,
                        document_id=doc_config.document_id,
                        workspace_id=doc_config.workspace_id,
                    )
                    results.append(SyncResult(
                        success=True, filepath=rel_path, operation="push",
                        message=f"Created {element_name} in Onshape ({element_id})",
                        element_id=element_id, created=True,
                    ))
                    continue

                if not element_id:
                    results.append(SyncResult(
                        success=False,
                        filepath=str(local_file),
                        operation="push",
                        message=f"No matching element found for {element_name} (new file? push with --create)",
                    ))
                    continue

                result = self._push_feature_studio(
                    filepath=local_file,
                    document_id=doc_config.document_id,
                    workspace_id=doc_config.workspace_id,
                    element_id=element_id,
                    dry_run=dry_run,
                    force=force,
                )
                result.element_id = element_id
                results.append(result)

            if created:
                self.state.save()
                self._update_document_metadata(local_dir, feature_studios=created)

        except Exception as e:
            results.append(SyncResult(
                success=False,
                filepath=doc_config.local_path,
                operation="push",
                message=f"Failed: {e}",
            ))

        return results

    # =========================================================================
    # High-Level Operations
    # =========================================================================

    def pull_all(
        self,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
        tab_folder: str | None = None,
        save_json: bool = False,
        folders: bool = False,
    ) -> list[SyncResult]:
        """Pull all configured folders and documents.

        Args:
            dry_run: Show what would happen without making changes
            force: Force pull even if there are local changes
            files: Optional list of specific files to pull (basenames like "file.fs")
            tab_folder: If set, only pull files from this tab folder (by name)
            save_json: If True, save raw elements API response as elements.json
        """
        results: list[SyncResult] = []

        # Pull folders (new style)
        for folder_config in self.config.folders:
            folder_results = self.pull_folder(folder_config, dry_run, force, files, tab_folder, save_json)
            results.extend(folder_results)

        # Pull documents (legacy style)
        for doc_config in self.config.documents:
            doc_results = self.pull_document(doc_config, dry_run, force, files, tab_folder, save_json, folders)
            results.extend(doc_results)

        return results

    def push_all(
        self,
        dry_run: bool = False,
        force: bool = False,
        files: list[str] | None = None,
        tab_folder: str | None = None,
        create: bool = False,
    ) -> list[SyncResult]:
        """Push all configured folders and documents.

        Args:
            dry_run: Show what would happen without making changes
            force: Force push even if there are remote changes
            files: Optional list of specific files to push (basenames like "file.fs")
            tab_folder: If set, only push files from this tab folder (by local dir name)
        """
        results: list[SyncResult] = []

        # Push folders (new style)
        for folder_config in self.config.folders:
            folder_results = self.push_folder(folder_config, dry_run, force, files)
            results.extend(folder_results)

        # Push documents (legacy style)
        for doc_config in self.config.documents:
            doc_results = self.push_document(doc_config, dry_run, force, files, tab_folder, create)
            results.extend(doc_results)

        return results

    def get_status(self) -> dict[str, Any]:
        """Get sync status."""
        return self.state.get_status_summary()

    def show_folder_tree(self, folder_config: FolderConfig) -> dict[str, Any]:
        """Get folder tree structure from Onshape."""
        return self.client.get_folder_tree(folder_config.folder_id)
