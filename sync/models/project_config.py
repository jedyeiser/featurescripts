"""Configuration models for project-based sync system.

This module defines the new FeatureScriptSettings configuration format that
distinguishes between reference libraries (read-only) and working directories
(bidirectional sync).
"""

from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import json


@dataclass
class ReferenceConfig:
    """Configuration for a read-only reference library from Onshape."""

    name: str
    type: str  # "folder" or "document"
    url: str
    local_path: str
    read_only: bool = True
    auto_update: bool = False
    recursive: bool = True
    last_sync: str | None = None

    # Parsed from URL
    document_id: str | None = None
    workspace_id: str | None = None
    folder_id: str | None = None

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "name": self.name,
            "type": self.type,
            "url": self.url,
            "local_path": self.local_path,
            "read_only": self.read_only,
            "auto_update": self.auto_update,
            "recursive": self.recursive,
            "last_sync": self.last_sync,
            "document_id": self.document_id,
            "workspace_id": self.workspace_id,
            "folder_id": self.folder_id,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "ReferenceConfig":
        """Create from dictionary."""
        return cls(
            name=data["name"],
            type=data["type"],
            url=data["url"],
            local_path=data["local_path"],
            read_only=data.get("read_only", True),
            auto_update=data.get("auto_update", False),
            recursive=data.get("recursive", True),
            last_sync=data.get("last_sync"),
            document_id=data.get("document_id"),
            workspace_id=data.get("workspace_id"),
            folder_id=data.get("folder_id"),
        )

    def update_sync_time(self) -> None:
        """Update last_sync timestamp to now."""
        self.last_sync = datetime.now(timezone.utc).isoformat()


@dataclass
class ProjectConfig:
    """Configuration for a working project with bidirectional sync."""

    name: str
    description: str
    working_directory: str
    onshape_url: str
    references: list[str] = field(default_factory=list)
    last_pull: str | None = None
    last_push: str | None = None

    # Parsed from URL
    document_id: str | None = None
    workspace_id: str | None = None
    folder_id: str | None = None
    recursive: bool = True

    # Element mappings for document-level projects
    metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        result: dict[str, Any] = {
            "name": self.name,
            "description": self.description,
            "working_directory": self.working_directory,
            "onshape_url": self.onshape_url,
            "references": self.references,
            "last_pull": self.last_pull,
            "last_push": self.last_push,
        }

        # Add optional fields
        if self.document_id:
            result["document_id"] = self.document_id
        if self.workspace_id:
            result["workspace_id"] = self.workspace_id
        if self.folder_id:
            result["folder_id"] = self.folder_id
        if not self.recursive:
            result["recursive"] = self.recursive
        if self.metadata:
            result["metadata"] = self.metadata

        return result

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "ProjectConfig":
        """Create from dictionary."""
        return cls(
            name=data["name"],
            description=data.get("description", ""),
            working_directory=data["working_directory"],
            onshape_url=data["onshape_url"],
            references=data.get("references", []),
            last_pull=data.get("last_pull"),
            last_push=data.get("last_push"),
            document_id=data.get("document_id"),
            workspace_id=data.get("workspace_id"),
            folder_id=data.get("folder_id"),
            recursive=data.get("recursive", True),
            metadata=data.get("metadata", {}),
        )

    def update_pull_time(self) -> None:
        """Update last_pull timestamp to now."""
        self.last_pull = datetime.now(timezone.utc).isoformat()

    def update_push_time(self) -> None:
        """Update last_push timestamp to now."""
        self.last_push = datetime.now(timezone.utc).isoformat()


@dataclass
class OnshapeConfig:
    """Onshape API configuration."""

    base_url: str = "https://k2-sports.onshape.com"
    api_version: str = "v10"

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "base_url": self.base_url,
            "api_version": self.api_version,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "OnshapeConfig":
        """Create from dictionary."""
        return cls(
            base_url=data.get("base_url", "https://k2-sports.onshape.com"),
            api_version=data.get("api_version", "v10"),
        )


@dataclass
class FeatureScriptSettings:
    """Root configuration for FeatureScript workspace.

    This is the primary configuration system that tracks projects, references,
    and sync metadata.
    """

    version: str = "1.0"
    onshape: OnshapeConfig = field(default_factory=OnshapeConfig)
    references: list[ReferenceConfig] = field(default_factory=list)
    projects: list[ProjectConfig] = field(default_factory=list)
    sync_metadata: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary for JSON serialization."""
        return {
            "version": self.version,
            "onshape": self.onshape.to_dict(),
            "references": [r.to_dict() for r in self.references],
            "projects": [p.to_dict() for p in self.projects],
            "sync_metadata": self.sync_metadata,
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "FeatureScriptSettings":
        """Create from dictionary."""
        return cls(
            version=data.get("version", "1.0"),
            onshape=OnshapeConfig.from_dict(data.get("onshape", {})),
            references=[ReferenceConfig.from_dict(r) for r in data.get("references", [])],
            projects=[ProjectConfig.from_dict(p) for p in data.get("projects", [])],
            sync_metadata=data.get("sync_metadata", {}),
        )

    @classmethod
    def load(cls, path: Path) -> "FeatureScriptSettings":
        """Load configuration from JSON file."""
        if not path.exists():
            # Return default config if file doesn't exist
            return cls()

        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)

        return cls.from_dict(data)

    def save(self, path: Path) -> None:
        """Save configuration to JSON file."""
        path.parent.mkdir(parents=True, exist_ok=True)

        with open(path, "w", encoding="utf-8") as f:
            json.dump(self.to_dict(), f, indent=2)

    def get_reference(self, name: str) -> ReferenceConfig | None:
        """Get reference by name."""
        for ref in self.references:
            if ref.name == name:
                return ref
        return None

    def get_project(self, name: str) -> ProjectConfig | None:
        """Get project by name."""
        for proj in self.projects:
            if proj.name == name:
                return proj
        return None

    def add_reference(self, ref: ReferenceConfig) -> None:
        """Add a reference to the configuration."""
        # Remove existing reference with same name
        self.references = [r for r in self.references if r.name != ref.name]
        self.references.append(ref)

    def add_project(self, proj: ProjectConfig) -> None:
        """Add a project to the configuration."""
        # Remove existing project with same name
        self.projects = [p for p in self.projects if p.name != proj.name]
        self.projects.append(proj)

    def remove_reference(self, name: str) -> bool:
        """Remove a reference by name. Returns True if found and removed."""
        original_len = len(self.references)
        self.references = [r for r in self.references if r.name != name]
        return len(self.references) < original_len

    def remove_project(self, name: str) -> bool:
        """Remove a project by name. Returns True if found and removed."""
        original_len = len(self.projects)
        self.projects = [p for p in self.projects if p.name != name]
        return len(self.projects) < original_len

    def update_document_cache(self, doc_id: str, name: str, modified_at: str, microversion: str) -> None:
        """Update cached document metadata."""
        if "document_cache" not in self.sync_metadata:
            self.sync_metadata["document_cache"] = {}

        self.sync_metadata["document_cache"][doc_id] = {
            "name": name,
            "modified_at": modified_at,
            "microversion": microversion,
        }

    def get_cached_microversion(self, doc_id: str) -> str | None:
        """Get cached microversion for a document."""
        cache = self.sync_metadata.get("document_cache", {})
        doc_data = cache.get(doc_id)
        return doc_data.get("microversion") if doc_data else None
