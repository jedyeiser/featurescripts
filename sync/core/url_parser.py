"""URL parsing utilities for Onshape URLs.

This module provides functions to parse and construct Onshape URLs,
extracting document IDs, workspace IDs, element IDs, and folder IDs.
"""

import re
from typing import Literal
from urllib.parse import urlparse, parse_qs


UrlType = Literal["document", "folder", "element"]


class OnshapeUrlParseError(Exception):
    """Raised when URL parsing fails."""
    pass


def parse_url(url: str) -> dict[str, str | None]:
    """Parse an Onshape URL into its components.

    Supported formats:
    - https://cad.onshape.com/documents/d/{docId}/w/{wsId}/e/{elemId}
    - https://cad.onshape.com/documents/d/{docId}/w/{wsId}
    - https://cad.onshape.com/documents/d/{docId}
    - https://cad.onshape.com/documents/folder/{folderId}
    - https://k2-sports.onshape.com/... (custom domain)

    Args:
        url: Onshape URL to parse

    Returns:
        Dictionary with keys: base_url, type, document_id, workspace_id,
        element_id, folder_id (values are None if not present)

    Raises:
        OnshapeUrlParseError: If URL format is invalid
    """
    parsed = urlparse(url)

    if not parsed.scheme or not parsed.netloc:
        raise OnshapeUrlParseError(f"Invalid URL format: {url}")

    base_url = f"{parsed.scheme}://{parsed.netloc}"
    path = parsed.path

    result: dict[str, str | None] = {
        "base_url": base_url,
        "type": None,
        "document_id": None,
        "workspace_id": None,
        "element_id": None,
        "folder_id": None,
    }

    # Check for folder URL (match alphanumeric IDs, not just hex)
    folder_match = re.search(r'/documents/folder/([a-zA-Z0-9_-]+)', path)
    if folder_match:
        result["type"] = "folder"
        result["folder_id"] = folder_match.group(1)
        return result

    # Check for document URL (match alphanumeric IDs, not just hex)
    doc_match = re.search(r'/documents/d/([a-zA-Z0-9_-]+)', path)
    if doc_match:
        result["document_id"] = doc_match.group(1)

        # Check for workspace (match alphanumeric IDs)
        ws_match = re.search(r'/w/([a-zA-Z0-9_-]+)', path)
        if ws_match:
            result["workspace_id"] = ws_match.group(1)

        # Check for element (match alphanumeric IDs)
        elem_match = re.search(r'/e/([a-zA-Z0-9_-]+)', path)
        if elem_match:
            result["element_id"] = elem_match.group(1)
            result["type"] = "element"
        else:
            result["type"] = "document"

        return result

    raise OnshapeUrlParseError(f"Unable to parse Onshape URL: {url}")


def get_url_type(url: str) -> UrlType:
    """Determine the type of Onshape URL.

    Args:
        url: Onshape URL

    Returns:
        "document", "folder", or "element"

    Raises:
        OnshapeUrlParseError: If URL format is invalid
    """
    parsed = parse_url(url)
    url_type = parsed.get("type")
    if url_type not in ("document", "folder", "element"):
        raise OnshapeUrlParseError(f"Unknown URL type: {url_type}")
    return url_type  # type: ignore


def build_url(
    base: str,
    doc_id: str | None = None,
    ws_id: str | None = None,
    elem_id: str | None = None,
    folder_id: str | None = None,
) -> str:
    """Construct an Onshape URL from components.

    Args:
        base: Base URL (e.g., "https://cad.onshape.com")
        doc_id: Document ID
        ws_id: Workspace ID
        elem_id: Element ID
        folder_id: Folder ID

    Returns:
        Constructed URL

    Raises:
        OnshapeUrlParseError: If required components are missing
    """
    # Remove trailing slash from base
    base = base.rstrip('/')

    if folder_id:
        return f"{base}/documents/folder/{folder_id}"

    if doc_id:
        url = f"{base}/documents/d/{doc_id}"
        if ws_id:
            url += f"/w/{ws_id}"
            if elem_id:
                url += f"/e/{elem_id}"
        return url

    raise OnshapeUrlParseError("Must provide either folder_id or doc_id")


def extract_document_info(url: str) -> tuple[str, str | None]:
    """Extract document ID and workspace ID from URL.

    Args:
        url: Onshape URL

    Returns:
        Tuple of (document_id, workspace_id)

    Raises:
        OnshapeUrlParseError: If URL is not a document URL
    """
    parsed = parse_url(url)
    if parsed["type"] not in ("document", "element"):
        raise OnshapeUrlParseError(f"URL is not a document: {url}")

    doc_id = parsed["document_id"]
    if not doc_id:
        raise OnshapeUrlParseError(f"No document ID found in URL: {url}")

    return doc_id, parsed["workspace_id"]


def extract_folder_id(url: str) -> str:
    """Extract folder ID from URL.

    Args:
        url: Onshape URL

    Returns:
        Folder ID

    Raises:
        OnshapeUrlParseError: If URL is not a folder URL
    """
    parsed = parse_url(url)
    if parsed["type"] != "folder":
        raise OnshapeUrlParseError(f"URL is not a folder: {url}")

    folder_id = parsed["folder_id"]
    if not folder_id:
        raise OnshapeUrlParseError(f"No folder ID found in URL: {url}")

    return folder_id


def normalize_url(url: str) -> str:
    """Normalize an Onshape URL to canonical form.

    Args:
        url: Onshape URL

    Returns:
        Normalized URL

    Raises:
        OnshapeUrlParseError: If URL format is invalid
    """
    parsed = parse_url(url)
    return build_url(
        base=parsed["base_url"] or "https://cad.onshape.com",
        doc_id=parsed["document_id"],
        ws_id=parsed["workspace_id"],
        elem_id=parsed["element_id"],
        folder_id=parsed["folder_id"],
    )
