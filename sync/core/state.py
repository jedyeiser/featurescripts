"""Sync state tracking and conflict detection."""

import hashlib
import json
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


@dataclass
class FileState:
    """State information for a single synced file."""

    local_hash: str  # SHA-256 of local file content
    remote_microversion: str  # Onshape microversion at last sync
    last_sync: str  # ISO timestamp of last sync
    element_id: str  # Onshape element ID
    document_id: str  # Onshape document ID
    workspace_id: str  # Onshape workspace ID

    def to_dict(self) -> dict[str, str]:
        """Convert to dictionary."""
        return {
            "local_hash": self.local_hash,
            "remote_microversion": self.remote_microversion,
            "last_sync": self.last_sync,
            "element_id": self.element_id,
            "document_id": self.document_id,
            "workspace_id": self.workspace_id,
        }

    @classmethod
    def from_dict(cls, data: dict[str, str]) -> "FileState":
        """Create from dictionary."""
        return cls(
            local_hash=data.get("local_hash", ""),
            remote_microversion=data.get("remote_microversion", ""),
            last_sync=data.get("last_sync", ""),
            element_id=data.get("element_id", ""),
            document_id=data.get("document_id", ""),
            workspace_id=data.get("workspace_id", ""),
        )


@dataclass
class SyncStateData:
    """Complete sync state for all tracked files."""

    version: str = "1.0"
    files: dict[str, FileState] = field(default_factory=dict)

    def to_dict(self) -> dict[str, Any]:
        """Convert to dictionary."""
        return {
            "version": self.version,
            "files": {k: v.to_dict() for k, v in self.files.items()},
        }

    @classmethod
    def from_dict(cls, data: dict[str, Any]) -> "SyncStateData":
        """Create from dictionary."""
        files = {}
        for filepath, file_data in (data.get("files") or {}).items():
            files[filepath] = FileState.from_dict(file_data)
        return cls(
            version=data.get("version", "1.0"),
            files=files,
        )


class ConflictType:
    """Types of sync conflicts."""

    NONE = "none"
    BOTH_CHANGED = "both_changed"  # Both local and remote changed
    LOCAL_DELETED = "local_deleted"  # Local file deleted, remote changed
    REMOTE_DELETED = "remote_deleted"  # Remote deleted, local changed


@dataclass
class ConflictInfo:
    """Information about a detected conflict."""

    filepath: str
    conflict_type: str
    local_hash: str | None = None
    previous_hash: str | None = None
    remote_microversion: str | None = None
    previous_microversion: str | None = None
    message: str = ""


class SyncState:
    """Manages sync state tracking and conflict detection."""

    def __init__(self, state_file: Path) -> None:
        """Initialize state manager.

        Args:
            state_file: Path to .sync-state.json file
        """
        self.state_file = Path(state_file)
        self._state: SyncStateData | None = None

    @property
    def state(self) -> SyncStateData:
        """Get or load the state data."""
        if self._state is None:
            self._state = self._load_state()
        return self._state

    def _load_state(self) -> SyncStateData:
        """Load state from disk or create empty."""
        if self.state_file.exists():
            with open(self.state_file) as f:
                data = json.load(f)
            return SyncStateData.from_dict(data)
        return SyncStateData()

    def save(self) -> None:
        """Save state to disk."""
        self.state_file.parent.mkdir(parents=True, exist_ok=True)
        with open(self.state_file, "w") as f:
            json.dump(self.state.to_dict(), f, indent=2)
            f.write("\n")

    @staticmethod
    def compute_hash(content: str) -> str:
        """Compute SHA-256 hash of content."""
        return hashlib.sha256(content.encode("utf-8")).hexdigest()

    @staticmethod
    def hash_file(filepath: Path) -> str | None:
        """Compute SHA-256 hash of a file."""
        if not filepath.exists():
            return None
        content = filepath.read_text()
        return SyncState.compute_hash(content)

    def get_file_state(self, filepath: str) -> FileState | None:
        """Get state for a specific file."""
        return self.state.files.get(filepath)

    def update_file_state(
        self,
        filepath: str,
        local_hash: str,
        remote_microversion: str,
        element_id: str,
        document_id: str,
        workspace_id: str,
    ) -> None:
        """Update state after a successful sync operation."""
        self.state.files[filepath] = FileState(
            local_hash=local_hash,
            remote_microversion=remote_microversion,
            last_sync=datetime.now(timezone.utc).isoformat(),
            element_id=element_id,
            document_id=document_id,
            workspace_id=workspace_id,
        )

    def remove_file_state(self, filepath: str) -> None:
        """Remove state for a deleted file."""
        self.state.files.pop(filepath, None)

    def detect_pull_conflict(
        self,
        filepath: str,
        local_path: Path,
        remote_microversion: str,
    ) -> ConflictInfo:
        """Detect conflicts before a pull operation.

        Conflict detection for PULL:
        - If remote changed AND local changed -> CONFLICT
        - If only remote changed -> safe to pull
        - If only local changed -> warn (will overwrite)

        Args:
            filepath: Relative path used as key
            local_path: Actual path to local file
            remote_microversion: Current remote microversion

        Returns:
            ConflictInfo describing any detected conflict
        """
        previous_state = self.get_file_state(filepath)
        current_hash = self.hash_file(local_path)

        # New file - no conflict possible
        if previous_state is None:
            return ConflictInfo(
                filepath=filepath,
                conflict_type=ConflictType.NONE,
                message="New file, safe to pull",
            )

        # Check what changed
        local_changed = current_hash != previous_state.local_hash
        remote_changed = remote_microversion != previous_state.remote_microversion

        # Local file deleted
        if current_hash is None:
            if remote_changed:
                return ConflictInfo(
                    filepath=filepath,
                    conflict_type=ConflictType.LOCAL_DELETED,
                    previous_hash=previous_state.local_hash,
                    remote_microversion=remote_microversion,
                    previous_microversion=previous_state.remote_microversion,
                    message="Local file deleted but remote has changes",
                )
            return ConflictInfo(
                filepath=filepath,
                conflict_type=ConflictType.NONE,
                message="Local deleted, remote unchanged - safe to skip or restore",
            )

        # Both changed - conflict
        if local_changed and remote_changed:
            return ConflictInfo(
                filepath=filepath,
                conflict_type=ConflictType.BOTH_CHANGED,
                local_hash=current_hash,
                previous_hash=previous_state.local_hash,
                remote_microversion=remote_microversion,
                previous_microversion=previous_state.remote_microversion,
                message="Both local and remote have changes - manual resolution required",
            )

        # Only local changed - warn but allow
        if local_changed:
            return ConflictInfo(
                filepath=filepath,
                conflict_type=ConflictType.NONE,
                local_hash=current_hash,
                previous_hash=previous_state.local_hash,
                message="Warning: Local changes will be overwritten",
            )

        # Only remote changed or neither changed - safe
        return ConflictInfo(
            filepath=filepath,
            conflict_type=ConflictType.NONE,
            message="Safe to pull" if remote_changed else "Already in sync",
        )

    def detect_push_conflict(
        self,
        filepath: str,
        remote_microversion: str,
    ) -> ConflictInfo:
        """Detect conflicts before a push operation.

        Conflict detection for PUSH:
        - If remote microversion differs from last sync -> CONFLICT
        - Use --force to override

        Args:
            filepath: Relative path used as key
            remote_microversion: Current remote microversion

        Returns:
            ConflictInfo describing any detected conflict
        """
        previous_state = self.get_file_state(filepath)

        # New file - no conflict
        if previous_state is None:
            return ConflictInfo(
                filepath=filepath,
                conflict_type=ConflictType.NONE,
                message="New file, safe to push",
            )

        # Check if remote changed since last sync
        if remote_microversion != previous_state.remote_microversion:
            return ConflictInfo(
                filepath=filepath,
                conflict_type=ConflictType.BOTH_CHANGED,
                remote_microversion=remote_microversion,
                previous_microversion=previous_state.remote_microversion,
                message="Remote has changed since last sync - use --force to override",
            )

        return ConflictInfo(
            filepath=filepath,
            conflict_type=ConflictType.NONE,
            message="Safe to push",
        )

    def list_tracked_files(self) -> list[str]:
        """Get list of all tracked file paths."""
        return list(self.state.files.keys())

    def get_status_summary(self) -> dict[str, Any]:
        """Get a summary of sync state."""
        return {
            "state_file": str(self.state_file),
            "tracked_files": len(self.state.files),
            "files": [
                {
                    "path": path,
                    "last_sync": state.last_sync,
                    "hash": state.local_hash[:8] + "..." if state.local_hash else "N/A",
                }
                for path, state in self.state.files.items()
            ],
        }
