"""Cache management for Onshape standard library files."""

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from ..models.config import CacheEntry, CacheManifest
from .client import OnshapeClient


class CacheManager:
    """Manages the local cache of Onshape standard library files."""

    def __init__(
        self,
        std_dir: Path,
        client: OnshapeClient | None = None,
    ) -> None:
        """Initialize cache manager.

        Args:
            std_dir: Path to std library directory (e.g., ./std)
            client: Optional OnshapeClient for fetching (created lazily if needed)
        """
        self.std_dir = Path(std_dir)
        self.manifest_path = self.std_dir / "manifest.json"
        self._client = client
        self._manifest: CacheManifest | None = None

    @property
    def client(self) -> OnshapeClient:
        """Get or create OnshapeClient (lazy initialization)."""
        if self._client is None:
            self._client = OnshapeClient()
        return self._client

    @property
    def manifest(self) -> CacheManifest:
        """Get or load the cache manifest."""
        if self._manifest is None:
            self._manifest = self._load_manifest()
        return self._manifest

    def _load_manifest(self) -> CacheManifest:
        """Load manifest from disk or create empty one."""
        if self.manifest_path.exists():
            with open(self.manifest_path) as f:
                data = json.load(f)
            return CacheManifest.from_dict(data)
        return CacheManifest()

    def _save_manifest(self) -> None:
        """Save manifest to disk."""
        self.std_dir.mkdir(parents=True, exist_ok=True)
        with open(self.manifest_path, "w") as f:
            json.dump(self.manifest.to_dict(), f, indent=2)
            f.write("\n")

    def _get_cache_path(self, filename: str) -> Path:
        """Get the cache file path for a given filename."""
        return self.std_dir / filename

    def is_cached(self, filename: str) -> bool:
        """Check if a file exists in the cache.

        Args:
            filename: Name of the file (e.g., "geometry.fs")

        Returns:
            True if file exists in cache
        """
        cache_path = self._get_cache_path(filename)
        in_manifest = filename in self.manifest.documents
        return cache_path.exists() and in_manifest

    def get_cached(self, filename: str) -> str | None:
        """Get cached file contents without calling API.

        Args:
            filename: Name of the file (e.g., "geometry.fs")

        Returns:
            File contents if cached, None otherwise
        """
        if not self.is_cached(filename):
            return None

        cache_path = self._get_cache_path(filename)
        return cache_path.read_text()

    def resolve_import(
        self,
        import_path: str,
        fetch_if_missing: bool = True,
    ) -> str | None:
        """Resolve an import path to cached content.

        This is the main lookup function implementing the lazy-fetch strategy:
        1. Check if file exists in cache
        2. If YES -> return cached content (no API call)
        3. If NO and fetch_if_missing -> fetch from API, cache, return
        4. If NO and not fetch_if_missing -> return None

        Args:
            import_path: Import path like "std/geometry.fs" or just "geometry.fs"
            fetch_if_missing: Whether to fetch from API if not cached

        Returns:
            File contents or None if not available
        """
        # Normalize path - strip "std/" prefix if present
        filename = import_path
        if filename.startswith("std/"):
            filename = filename[4:]

        # Check cache first
        cached = self.get_cached(filename)
        if cached is not None:
            return cached

        # Fetch if requested
        if fetch_if_missing:
            return self.fetch_and_cache(filename)

        return None

    def fetch_and_cache(
        self,
        filename: str,
        document_id: str | None = None,
        element_id: str | None = None,
        workspace_id: str = "main",
    ) -> str | None:
        """Fetch a file from Onshape and cache it locally.

        Args:
            filename: Name of the file to fetch
            document_id: Onshape document ID (uses std lib if not provided)
            element_id: Element ID within document
            workspace_id: Workspace ID (default "main")

        Returns:
            File contents or None on failure
        """
        # If no IDs provided, need to look up from std library
        # This would require knowing the std lib document structure
        if document_id is None:
            # For now, log that we need the IDs
            print(f"Cannot fetch {filename}: document_id and element_id required")
            return None

        if element_id is None:
            print(f"Cannot fetch {filename}: element_id required")
            return None

        try:
            response = self.client.get_featurestudio_contents(
                document_id=document_id,
                workspace_id=workspace_id,
                element_id=element_id,
            )

            contents = response.get("contents", "")
            microversion = response.get("microversion", "")

            # Cache the file
            cache_path = self._get_cache_path(filename)
            cache_path.write_text(contents)

            # Update manifest
            self.manifest.documents[filename] = CacheEntry(
                document_id=document_id,
                element_id=element_id,
                microversion=microversion,
                fetched_at=datetime.now(timezone.utc).isoformat(),
                onshape_version=self.manifest.onshape_std_version,
            )
            self.manifest.update_timestamp()
            self._save_manifest()

            return contents

        except Exception as e:
            print(f"Failed to fetch {filename}: {e}")
            return None

    def update(
        self,
        filename: str | None = None,
        force: bool = False,
    ) -> dict[str, bool]:
        """Update cached files from Onshape.

        Args:
            filename: Specific file to update, or None for all
            force: Force update even if microversion unchanged

        Returns:
            Dictionary mapping filenames to success status
        """
        results: dict[str, bool] = {}

        if filename:
            files_to_update = [filename] if filename in self.manifest.documents else []
            if not files_to_update:
                print(f"File {filename} not in cache manifest")
                return {filename: False}
        else:
            files_to_update = list(self.manifest.documents.keys())

        for fname in files_to_update:
            entry = self.manifest.documents.get(fname)
            if entry is None:
                results[fname] = False
                continue

            try:
                # Fetch current content
                response = self.client.get_featurestudio_contents(
                    document_id=entry.document_id,
                    workspace_id="main",  # Assuming main workspace for std
                    element_id=entry.element_id,
                )

                current_microversion = response.get("microversion", "")

                # Check if update needed
                if not force and current_microversion == entry.microversion:
                    print(f"{fname}: Already up to date")
                    results[fname] = True
                    continue

                # Update cache
                contents = response.get("contents", "")
                cache_path = self._get_cache_path(fname)
                cache_path.write_text(contents)

                # Update manifest entry
                entry.microversion = current_microversion
                entry.fetched_at = datetime.now(timezone.utc).isoformat()
                self._save_manifest()

                print(f"{fname}: Updated (microversion: {current_microversion[:8]}...)")
                results[fname] = True

            except Exception as e:
                print(f"{fname}: Failed to update - {e}")
                results[fname] = False

        return results

    def status(self) -> dict[str, Any]:
        """Get cache status information.

        Returns:
            Dictionary with cache statistics and file info
        """
        cached_files = []
        for filename, entry in self.manifest.documents.items():
            cache_path = self._get_cache_path(filename)
            cached_files.append({
                "filename": filename,
                "exists": cache_path.exists(),
                "microversion": entry.microversion[:8] + "..." if entry.microversion else "N/A",
                "fetched_at": entry.fetched_at,
            })

        return {
            "std_dir": str(self.std_dir),
            "manifest_version": self.manifest.version,
            "last_updated": self.manifest.last_updated,
            "onshape_std_version": self.manifest.onshape_std_version,
            "total_files": len(self.manifest.documents),
            "files": cached_files,
        }

    def add_to_manifest(
        self,
        filename: str,
        document_id: str,
        element_id: str,
        microversion: str = "",
    ) -> None:
        """Add a file entry to the manifest without fetching.

        Useful for seeding the manifest with known document IDs.

        Args:
            filename: Name of the file
            document_id: Onshape document ID
            element_id: Element ID
            microversion: Optional microversion
        """
        self.manifest.documents[filename] = CacheEntry(
            document_id=document_id,
            element_id=element_id,
            microversion=microversion,
            fetched_at="",  # Not fetched yet
            onshape_version=self.manifest.onshape_std_version,
        )
        self._save_manifest()
