"""Tests for data models."""

import json
import tempfile
from pathlib import Path

import pytest

from sync.models.config import (
    CacheEntry,
    CacheManifest,
    DocumentConfig,
    SyncConfig,
    SyncSettings,
)


class TestCacheEntry:
    """Tests for CacheEntry model."""

    def test_to_dict(self) -> None:
        entry = CacheEntry(
            document_id="doc123",
            element_id="elem456",
            microversion="mv789",
            fetched_at="2026-01-31T12:00:00Z",
            onshape_version="2878",
        )

        result = entry.to_dict()

        assert result["document_id"] == "doc123"
        assert result["element_id"] == "elem456"
        assert result["microversion"] == "mv789"
        assert result["fetched_at"] == "2026-01-31T12:00:00Z"
        assert result["onshape_version"] == "2878"

    def test_from_dict(self) -> None:
        data = {
            "document_id": "doc123",
            "element_id": "elem456",
            "microversion": "mv789",
            "fetched_at": "2026-01-31T12:00:00Z",
            "onshape_version": "2878",
        }

        entry = CacheEntry.from_dict(data)

        assert entry.document_id == "doc123"
        assert entry.element_id == "elem456"
        assert entry.microversion == "mv789"


class TestCacheManifest:
    """Tests for CacheManifest model."""

    def test_empty_manifest(self) -> None:
        manifest = CacheManifest()

        assert manifest.version == "1.0"
        assert manifest.last_updated is None
        assert manifest.documents == {}

    def test_to_dict_roundtrip(self) -> None:
        manifest = CacheManifest(
            version="1.0",
            last_updated="2026-01-31T12:00:00Z",
            onshape_std_version="2878",
            documents={
                "test.fs": CacheEntry(
                    document_id="doc123",
                    element_id="elem456",
                    microversion="mv789",
                    fetched_at="2026-01-31T12:00:00Z",
                    onshape_version="2878",
                )
            },
        )

        data = manifest.to_dict()
        restored = CacheManifest.from_dict(data)

        assert restored.version == manifest.version
        assert restored.last_updated == manifest.last_updated
        assert "test.fs" in restored.documents
        assert restored.documents["test.fs"].document_id == "doc123"

    def test_update_timestamp(self) -> None:
        manifest = CacheManifest()
        assert manifest.last_updated is None

        manifest.update_timestamp()

        assert manifest.last_updated is not None
        assert "T" in manifest.last_updated  # ISO format


class TestSyncConfig:
    """Tests for SyncConfig model."""

    def test_load_empty_config(self) -> None:
        with tempfile.NamedTemporaryFile(mode="w", suffix=".yaml", delete=False) as f:
            f.write("documents: []\nsettings: {}\n")
            config_path = Path(f.name)

        try:
            config = SyncConfig.load(config_path)
            assert config.documents == []
            assert config.settings.backup_on_pull is True
        finally:
            config_path.unlink()

    def test_load_with_documents(self) -> None:
        yaml_content = """
documents:
  - name: "Test Doc"
    document_id: "abc123"
    workspace_id: "main"
    local_path: "./test"

settings:
  backup_on_pull: false
  verbose: true
"""
        with tempfile.NamedTemporaryFile(mode="w", suffix=".yaml", delete=False) as f:
            f.write(yaml_content)
            config_path = Path(f.name)

        try:
            config = SyncConfig.load(config_path)
            assert len(config.documents) == 1
            assert config.documents[0].name == "Test Doc"
            assert config.documents[0].document_id == "abc123"
            assert config.settings.backup_on_pull is False
            assert config.settings.verbose is True
        finally:
            config_path.unlink()
