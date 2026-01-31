"""Configuration and data models for the sync system."""

import fnmatch
import re
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import yaml


def sanitize_filename(name: str) -> str:
    """Sanitize a name for use as a filesystem path.

    Replaces invalid characters with underscores and handles edge cases.
    """
    # Replace characters that are problematic on various filesystems
    sanitized = re.sub(r'[<>:"/\\|?*]', '_', name)
    # Replace multiple underscores/spaces with single underscore
    sanitized = re.sub(r'[_\s]+', '_', sanitized)
    # Remove leading/trailing underscores and spaces
    sanitized = sanitized.strip('_ ')
    # Handle empty result
    return sanitized or "unnamed"


@dataclass
class FolderConfig:
    """Configuration for syncing an Onshape folder hierarchy."""

    name: str  # Human-readable name
    folder_id: str  # Onshape folder ID (from URL nodeId parameter)
    local_path: str  # Local directory to sync to
    recursive: bool = True  # Include subfolders
    exclude: list[str] = field(default_factory=list)  # Glob patterns to exclude

    def should_exclude(self, path: str) -> bool:
        """Check if a path matches any exclude pattern.

        Args:
            path: Relative path to check (e.g., "subfolder/docname")

        Returns:
            True if path should be excluded
        """
        for pattern in self.exclude:
            if fnmatch.fnmatch(path, pattern):
                return True
            # Also check just the name portion
            if fnmatch.fnmatch(path.split('/')[-1], pattern):
                return True
        return False

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "FolderConfig":
        """Create from dictionary."""
        return cls(
            name=data["name"],
            folder_id=data["folder_id"],
            local_path=data["local_path"],
            recursive=data.get("recursive", True),
            exclude=data.get("exclude", []),
        )


@dataclass
class DocumentConfig:
    """Configuration for a single Onshape document to sync (legacy support)."""

    name: str
    document_id: str
    workspace_id: str
    local_path: str

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "DocumentConfig":
        """Create from dictionary."""
        return cls(
            name=data["name"],
            document_id=data["document_id"],
            workspace_id=data["workspace_id"],
            local_path=data["local_path"],
        )


@dataclass
class DocumentMetadata:
    """Metadata stored in .document.json for each synced document folder.

    This provides traceability from local files back to Onshape.
    """

    document_id: str
    workspace_id: str
    document_name: str
    folder_path: str  # Path within Onshape folder hierarchy
    onshape_url: str  # Direct URL to document
    last_sync: str
    feature_studios: dict[str, str] = field(default_factory=dict)  # name -> element_id

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "document_id": self.document_id,
            "workspace_id": self.workspace_id,
            "document_name": self.document_name,
            "folder_path": self.folder_path,
            "onshape_url": self.onshape_url,
            "last_sync": self.last_sync,
            "feature_studios": self.feature_studios,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "DocumentMetadata":
        """Create from dictionary."""
        return cls(
            document_id=data["document_id"],
            workspace_id=data["workspace_id"],
            document_name=data["document_name"],
            folder_path=data.get("folder_path", ""),
            onshape_url=data.get("onshape_url", ""),
            last_sync=data.get("last_sync", ""),
            feature_studios=data.get("feature_studios", {}),
        )

    def update_timestamp(self) -> None:
        """Update last_sync to now."""
        self.last_sync = datetime.now(timezone.utc).isoformat()


@dataclass
class SyncSettings:
    """Sync operation settings."""

    backup_on_pull: bool = True
    backup_dir: str = "./.sync-backups"
    file_extension: str = ".fs"
    verbose: bool = False
    # Default workspace to use when not specified
    default_workspace: str = "main"


@dataclass
class SyncConfig:
    """Main configuration for the sync system.

    Supports both:
    - folders: New folder-based sync (recommended)
    - documents: Legacy single-document sync (for backwards compatibility)
    """

    folders: list[FolderConfig] = field(default_factory=list)
    documents: list[DocumentConfig] = field(default_factory=list)  # Legacy
    settings: SyncSettings = field(default_factory=SyncSettings)
    # Base URL for this Onshape instance
    base_url: str = "https://cad.onshape.com"

    @classmethod
    def load(cls, config_path: Path) -> "SyncConfig":
        """Load configuration from YAML file."""
        with open(config_path) as f:
            data = yaml.safe_load(f) or {}

        # Load folder configs (new style)
        folders = []
        for folder_data in data.get("folders") or []:
            folders.append(FolderConfig.from_dict(folder_data))

        # Load document configs (legacy style)
        documents = []
        for doc_data in data.get("documents") or []:
            documents.append(DocumentConfig.from_dict(doc_data))

        # Load settings
        settings_data = data.get("settings", {})
        settings = SyncSettings(
            backup_on_pull=settings_data.get("backup_on_pull", True),
            backup_dir=settings_data.get("backup_dir", "./.sync-backups"),
            file_extension=settings_data.get("file_extension", ".fs"),
            verbose=settings_data.get("verbose", False),
            default_workspace=settings_data.get("default_workspace", "main"),
        )

        base_url = data.get("base_url", "https://cad.onshape.com")

        return cls(
            folders=folders,
            documents=documents,
            settings=settings,
            base_url=base_url,
        )

    def save(self, config_path: Path) -> None:
        """Save configuration to YAML file."""
        data: dict[str, Any] = {}

        if self.folders:
            data["folders"] = [
                {
                    "name": f.name,
                    "folder_id": f.folder_id,
                    "local_path": f.local_path,
                    "recursive": f.recursive,
                    "exclude": f.exclude,
                }
                for f in self.folders
            ]

        if self.documents:
            data["documents"] = [
                {
                    "name": d.name,
                    "document_id": d.document_id,
                    "workspace_id": d.workspace_id,
                    "local_path": d.local_path,
                }
                for d in self.documents
            ]

        data["base_url"] = self.base_url

        data["settings"] = {
            "backup_on_pull": self.settings.backup_on_pull,
            "backup_dir": self.settings.backup_dir,
            "file_extension": self.settings.file_extension,
            "verbose": self.settings.verbose,
            "default_workspace": self.settings.default_workspace,
        }

        with open(config_path, "w") as f:
            yaml.dump(data, f, default_flow_style=False, sort_keys=False)


# ---------------------------------------------------------------------------
# Cache models (unchanged from before)
# ---------------------------------------------------------------------------

@dataclass
class CacheEntry:
    """A single cached document entry."""

    document_id: str
    element_id: str
    microversion: str
    fetched_at: str
    onshape_version: str

    def to_dict(self) -> dict[str, str]:
        """Convert to dictionary for JSON serialization."""
        return {
            "document_id": self.document_id,
            "element_id": self.element_id,
            "microversion": self.microversion,
            "fetched_at": self.fetched_at,
            "onshape_version": self.onshape_version,
        }

    @classmethod
    def from_dict(cls, data: dict[str, str]) -> "CacheEntry":
        """Create from dictionary."""
        return cls(
            document_id=data["document_id"],
            element_id=data["element_id"],
            microversion=data["microversion"],
            fetched_at=data["fetched_at"],
            onshape_version=data["onshape_version"],
        )


@dataclass
class CacheManifest:
    """Manifest tracking all cached standard library files."""

    version: str = "1.0"
    last_updated: str | None = None
    onshape_std_version: str = "2878"
    documents: dict[str, CacheEntry] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "version": self.version,
            "last_updated": self.last_updated,
            "onshape_std_version": self.onshape_std_version,
            "documents": {k: v.to_dict() for k, v in self.documents.items()},
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "CacheManifest":
        """Create from dictionary."""
        documents = {}
        for name, entry_data in (data.get("documents") or {}).items():
            documents[name] = CacheEntry.from_dict(entry_data)

        return cls(
            version=data.get("version", "1.0"),
            last_updated=data.get("last_updated"),
            onshape_std_version=data.get("onshape_std_version", "2878"),
            documents=documents,
        )

    def update_timestamp(self) -> None:
        """Update the last_updated timestamp to now."""
        self.last_updated = datetime.now(timezone.utc).isoformat()
