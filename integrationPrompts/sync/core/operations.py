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
from .client import OnshapeClient
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
        )

        metadata_path = local_dir / self.METADATA_FILENAME
        with open(metadata_path, "w") as f:
            json.dump(metadata.to_dict(), f, indent=2)
            f.write("\n")

    def _load_document_metadata(self, local_dir: Path) -> DocumentMetadata | None:
        """Load .document.json metadata file if it exists."""
        metadata_path = local_dir / self.METADATA_FILENAME
        if not metadata_path.exists():
            return None

        with open(metadata_path) as f:
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
    ) -> list[SyncResult]:
        """Pull a single document's Feature Studios to a local folder.

        Args:
            document_id: Onshape document ID
            document_name: Document name
            folder_path: Path within Onshape folder hierarchy
            local_dir: Local directory for this document
            dry_run: If True, show what would happen
            force: If True, overwrite local changes

        Returns:
            List of SyncResults
        """
        results: list[SyncResult] = []

        try:
            # Get default workspace for this document
            workspace_id = self.client.get_default_workspace(document_id)
            if not workspace_id:
                # Fallback to configured default
                workspace_id = self.config.settings.default_workspace

            # List Feature Studios in this document
            elements = self.client.list_elements(
                document_id=document_id,
                workspace_id=workspace_id,
                element_type="FEATURESTUDIO",
            )

            if not elements:
                results.append(SyncResult(
                    success=True,
                    filepath=str(local_dir.relative_to(self.base_dir)),
                    operation="pull",
                    message=f"No Feature Studios in document '{document_name}'",
                    skipped=True,
                ))
                return results

            if dry_run:
                for element in elements:
                    elem_name = element.get("name", "unnamed")
                    results.append(SyncResult(
                        success=True,
                        filepath=f"{local_dir.relative_to(self.base_dir)}/{elem_name}.fs",
                        operation="pull",
                        message=f"[DRY RUN] Would pull {elem_name}.fs",
                        skipped=True,
                    ))
                return results

            # Create local directory
            local_dir.mkdir(parents=True, exist_ok=True)

            # Track feature studios for metadata
            feature_studios: dict[str, str] = {}

            for element in elements:
                element_id = element.get("id", "")
                element_name = element.get("name", "unnamed")

                result = self._pull_feature_studio(
                    document_id=document_id,
                    workspace_id=workspace_id,
                    element_id=element_id,
                    element_name=element_name,
                    local_dir=local_dir,
                    force=force,
                )
                results.append(result)

                if result.success:
                    feature_studios[element_name] = element_id

            # Save document metadata
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
    ) -> SyncResult:
        """Pull a single Feature Studio to a local file.

        Args:
            document_id: Document ID
            workspace_id: Workspace ID
            element_id: Element ID
            element_name: Element name (becomes filename)
            local_dir: Directory to save file in
            force: If True, overwrite local changes

        Returns:
            SyncResult
        """
        filename = sanitize_filename(element_name) + self.config.settings.file_extension
        filepath = local_dir / filename
        relative_path = str(filepath.relative_to(self.base_dir))

        try:
            # Get remote content
            response = self.client.get_featurestudio_contents(
                document_id=document_id,
                workspace_id=workspace_id,
                element_id=element_id,
            )

            remote_content = response.get("contents", "")
            remote_microversion = response.get("microversion", "")

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

            # Backup if needed
            self._backup_file(filepath)

            # Write file
            filepath.write_text(remote_content)

            # Update state
            local_hash = SyncState.compute_hash(remote_content)
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

        try:
            # Get current remote microversion for conflict check
            remote_microversion = self.client.get_document_microversion(
                document_id=document_id,
                workspace_id=workspace_id,
            )

            # Check for conflicts
            if not force:
                conflict = self.state.detect_push_conflict(relative_path, remote_microversion)
                if conflict.conflict_type == ConflictType.BOTH_CHANGED:
                    return SyncResult(
                        success=False,
                        filepath=relative_path,
                        operation="push",
                        message=conflict.message,
                        conflict=True,
                    )

            local_content = filepath.read_text()

            if dry_run:
                return SyncResult(
                    success=True,
                    filepath=relative_path,
                    operation="push",
                    message=f"[DRY RUN] Would push {filepath.name}",
                    skipped=True,
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
    ) -> list[SyncResult]:
        """Pull all Feature Studios from a document (legacy method)."""
        results: list[SyncResult] = []
        local_dir = self.base_dir / doc_config.local_path

        try:
            elements = self.client.list_elements(
                document_id=doc_config.document_id,
                workspace_id=doc_config.workspace_id,
                element_type="FEATURESTUDIO",
            )

            for element in elements:
                element_id = element.get("id", "")
                element_name = element.get("name", "")

                if not element_id or not element_name:
                    continue

                result = self._pull_feature_studio(
                    document_id=doc_config.document_id,
                    workspace_id=doc_config.workspace_id,
                    element_id=element_id,
                    element_name=element_name,
                    local_dir=local_dir,
                    force=force,
                )
                results.append(result)

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
    ) -> list[SyncResult]:
        """Push all local Feature Studios to a document (legacy method)."""
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
            for local_file in local_dir.glob(f"*{extension}"):
                element_name = local_file.stem
                element_id = name_to_id.get(element_name)

                if not element_id:
                    results.append(SyncResult(
                        success=False,
                        filepath=str(local_file),
                        operation="push",
                        message=f"No matching element found for {element_name}",
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
                results.append(result)

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
    ) -> list[SyncResult]:
        """Pull all configured folders and documents."""
        results: list[SyncResult] = []

        # Pull folders (new style)
        for folder_config in self.config.folders:
            folder_results = self.pull_folder(folder_config, dry_run, force)
            results.extend(folder_results)

        # Pull documents (legacy style)
        for doc_config in self.config.documents:
            doc_results = self.pull_document(doc_config, dry_run, force)
            results.extend(doc_results)

        return results

    def push_all(
        self,
        dry_run: bool = False,
        force: bool = False,
    ) -> list[SyncResult]:
        """Push all configured folders and documents."""
        results: list[SyncResult] = []

        # Push folders (new style)
        for folder_config in self.config.folders:
            folder_results = self.push_folder(folder_config, dry_run, force)
            results.extend(folder_results)

        # Push documents (legacy style)
        for doc_config in self.config.documents:
            doc_results = self.push_document(doc_config, dry_run, force)
            results.extend(doc_results)

        return results

    def get_status(self) -> dict[str, Any]:
        """Get sync status."""
        return self.state.get_status_summary()

    def show_folder_tree(self, folder_config: FolderConfig) -> dict[str, Any]:
        """Get folder tree structure from Onshape."""
        return self.client.get_folder_tree(folder_config.folder_id)
