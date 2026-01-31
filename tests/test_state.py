"""Tests for sync state tracking."""

import tempfile
from pathlib import Path

import pytest

from sync.core.state import ConflictType, FileState, SyncState, SyncStateData


class TestSyncState:
    """Tests for SyncState class."""

    def test_compute_hash(self) -> None:
        content = "Hello, World!"
        hash1 = SyncState.compute_hash(content)
        hash2 = SyncState.compute_hash(content)

        assert hash1 == hash2
        assert len(hash1) == 64  # SHA-256 hex length

    def test_compute_hash_different_content(self) -> None:
        hash1 = SyncState.compute_hash("Hello")
        hash2 = SyncState.compute_hash("World")

        assert hash1 != hash2

    def test_hash_file(self) -> None:
        with tempfile.NamedTemporaryFile(mode="w", suffix=".txt", delete=False) as f:
            f.write("test content")
            filepath = Path(f.name)

        try:
            file_hash = SyncState.hash_file(filepath)
            content_hash = SyncState.compute_hash("test content")
            assert file_hash == content_hash
        finally:
            filepath.unlink()

    def test_hash_file_nonexistent(self) -> None:
        result = SyncState.hash_file(Path("/nonexistent/file.txt"))
        assert result is None

    def test_save_and_load(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            state_file = Path(tmpdir) / ".sync-state.json"
            state = SyncState(state_file)

            state.update_file_state(
                filepath="test.fs",
                local_hash="abc123",
                remote_microversion="mv456",
                element_id="elem789",
                document_id="doc000",
                workspace_id="main",
            )
            state.save()

            # Load in new instance
            state2 = SyncState(state_file)
            file_state = state2.get_file_state("test.fs")

            assert file_state is not None
            assert file_state.local_hash == "abc123"
            assert file_state.remote_microversion == "mv456"


class TestConflictDetection:
    """Tests for conflict detection logic."""

    def test_detect_pull_conflict_new_file(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            state_file = Path(tmpdir) / ".sync-state.json"
            state = SyncState(state_file)

            conflict = state.detect_pull_conflict(
                filepath="new.fs",
                local_path=Path(tmpdir) / "new.fs",
                remote_microversion="mv123",
            )

            assert conflict.conflict_type == ConflictType.NONE
            assert "New file" in conflict.message

    def test_detect_pull_conflict_both_changed(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            state_file = Path(tmpdir) / ".sync-state.json"
            local_file = Path(tmpdir) / "test.fs"

            # Create initial state
            local_file.write_text("original content")
            original_hash = SyncState.compute_hash("original content")

            state = SyncState(state_file)
            state.update_file_state(
                filepath="test.fs",
                local_hash=original_hash,
                remote_microversion="mv_original",
                element_id="elem1",
                document_id="doc1",
                workspace_id="main",
            )
            state.save()

            # Modify local file
            local_file.write_text("modified content")

            # Check conflict with changed remote
            conflict = state.detect_pull_conflict(
                filepath="test.fs",
                local_path=local_file,
                remote_microversion="mv_changed",
            )

            assert conflict.conflict_type == ConflictType.BOTH_CHANGED

    def test_detect_push_conflict_remote_changed(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            state_file = Path(tmpdir) / ".sync-state.json"

            state = SyncState(state_file)
            state.update_file_state(
                filepath="test.fs",
                local_hash="hash123",
                remote_microversion="mv_original",
                element_id="elem1",
                document_id="doc1",
                workspace_id="main",
            )
            state.save()

            # Check conflict with different remote version
            conflict = state.detect_push_conflict(
                filepath="test.fs",
                remote_microversion="mv_changed",
            )

            assert conflict.conflict_type == ConflictType.BOTH_CHANGED
            assert "--force" in conflict.message

    def test_detect_push_no_conflict(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            state_file = Path(tmpdir) / ".sync-state.json"

            state = SyncState(state_file)
            state.update_file_state(
                filepath="test.fs",
                local_hash="hash123",
                remote_microversion="mv_same",
                element_id="elem1",
                document_id="doc1",
                workspace_id="main",
            )
            state.save()

            # Same remote version = no conflict
            conflict = state.detect_push_conflict(
                filepath="test.fs",
                remote_microversion="mv_same",
            )

            assert conflict.conflict_type == ConflictType.NONE
