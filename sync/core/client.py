"""HTTP client wrapper for Onshape API."""

from typing import Any

import requests

from .auth import OnshapeAuth


class OnshapeAPIError(Exception):
    """Exception raised for Onshape API errors."""

    def __init__(self, message: str, status_code: int | None = None, response: Any = None) -> None:
        super().__init__(message)
        self.status_code = status_code
        self.response = response


class OnshapeClient:
    """HTTP client for Onshape REST API with HMAC authentication."""

    # API version prefix
    API_VERSION = "v10"

    def __init__(self, auth: OnshapeAuth | None = None) -> None:
        """Initialize client with authentication.

        Args:
            auth: OnshapeAuth instance (creates one from env if not provided)
        """
        self.auth = auth or OnshapeAuth()
        self.session = requests.Session()

    def _request(
        self,
        method: str,
        path: str,
        query_params: dict[str, str] | None = None,
        json_data: dict[str, Any] | None = None,
        content_type: str = "application/json",
    ) -> dict[str, Any]:
        """Make an authenticated request to the Onshape API.

        Args:
            method: HTTP method
            path: API path (without base URL)
            query_params: Optional query parameters
            json_data: Optional JSON body data
            content_type: Content-Type header

        Returns:
            Parsed JSON response

        Raises:
            OnshapeAPIError: On API errors
        """
        headers = self.auth.get_headers(
            method=method,
            path=path,
            query_params=query_params,
            content_type=content_type,
        )

        url = self.auth.get_full_url(path, query_params)

        try:
            response = self.session.request(
                method=method,
                url=url,
                headers=headers,
                json=json_data if method in ("POST", "PUT", "PATCH") else None,
                timeout=30,
            )

            if response.status_code >= 400:
                error_msg = f"API error {response.status_code}: {response.text[:500]}"
                raise OnshapeAPIError(error_msg, response.status_code, response)

            # Handle empty responses
            if not response.content:
                return {}

            return response.json()  # type: ignore[no-any-return]

        except requests.RequestException as e:
            raise OnshapeAPIError(f"Request failed: {e}") from e

    def get(
        self,
        path: str,
        query_params: dict[str, str] | None = None,
    ) -> dict[str, Any]:
        """Make a GET request."""
        return self._request("GET", path, query_params)

    def post(
        self,
        path: str,
        json_data: dict[str, Any] | None = None,
        query_params: dict[str, str] | None = None,
    ) -> dict[str, Any]:
        """Make a POST request."""
        return self._request("POST", path, query_params, json_data)

    # -------------------------------------------------------------------------
    # Document Operations
    # -------------------------------------------------------------------------

    def list_elements(
        self,
        document_id: str,
        workspace_id: str,
        element_type: str | None = None,
    ) -> list[dict[str, Any]]:
        """List elements in a document workspace.

        Args:
            document_id: Document ID
            workspace_id: Workspace ID
            element_type: Optional filter (e.g., "FEATURESTUDIO")

        Returns:
            List of element metadata dictionaries
        """
        path = f"/api/{self.API_VERSION}/documents/d/{document_id}/w/{workspace_id}/elements"
        query_params = {}
        if element_type:
            query_params["elementType"] = element_type

        response = self.get(path, query_params if query_params else None)

        # Response is a list at the top level
        if isinstance(response, list):
            return response
        return response.get("items", [])

    def get_featurestudio_contents(
        self,
        document_id: str,
        workspace_id: str,
        element_id: str,
    ) -> dict[str, Any]:
        """Get the contents of a Feature Studio.

        Args:
            document_id: Document ID
            workspace_id: Workspace ID
            element_id: Element ID of the Feature Studio

        Returns:
            Dictionary with 'contents' (source code) and metadata
        """
        path = (
            f"/api/{self.API_VERSION}/featurestudios/d/{document_id}"
            f"/w/{workspace_id}/e/{element_id}/featurestudiocontents"
        )
        return self.get(path)

    def update_featurestudio_contents(
        self,
        document_id: str,
        workspace_id: str,
        element_id: str,
        contents: str,
    ) -> dict[str, Any]:
        """Update the contents of a Feature Studio.

        Args:
            document_id: Document ID
            workspace_id: Workspace ID
            element_id: Element ID of the Feature Studio
            contents: New source code contents

        Returns:
            Updated metadata
        """
        path = (
            f"/api/{self.API_VERSION}/featurestudios/d/{document_id}"
            f"/w/{workspace_id}/e/{element_id}/featurestudiocontents"
        )
        return self.post(path, json_data={"contents": contents})

    def get_document_microversion(
        self,
        document_id: str,
        workspace_id: str,
    ) -> str:
        """Get the current microversion of a document workspace.

        Args:
            document_id: Document ID
            workspace_id: Workspace ID

        Returns:
            Microversion string
        """
        path = f"/api/{self.API_VERSION}/documents/d/{document_id}/w/{workspace_id}"
        response = self.get(path)
        return response.get("microversion", "")

    def get_document_info(
        self,
        document_id: str,
    ) -> dict[str, Any]:
        """Get document metadata including default workspace.

        Args:
            document_id: Document ID

        Returns:
            Document metadata including name, defaultWorkspace, etc.
        """
        path = f"/api/{self.API_VERSION}/documents/d/{document_id}"
        return self.get(path)

    def get_default_workspace(self, document_id: str) -> str:
        """Get the default workspace ID for a document.

        Args:
            document_id: Document ID

        Returns:
            Default workspace ID
        """
        doc_info = self.get_document_info(document_id)
        default_ws = doc_info.get("defaultWorkspace", {})
        return default_ws.get("id", "")

    # -------------------------------------------------------------------------
    # Folder Operations
    # -------------------------------------------------------------------------

    def get_folder_contents(
        self,
        folder_id: str,
    ) -> dict[str, Any]:
        """Get contents of a folder using Global Tree Nodes API.

        Args:
            folder_id: Folder ID (nodeId from URL)

        Returns:
            Dictionary with 'items' containing documents and subfolders
        """
        # Use Global Tree Nodes API to list folder contents
        path = f"/api/{self.API_VERSION}/globaltreenodes/folder/{folder_id}"
        response = self.get(path)

        # The response has 'items' at the top level
        # Each item has: id, name, resourceType (document/folder), etc.
        return response

    def list_folder_documents(
        self,
        folder_id: str,
        recursive: bool = False,
    ) -> list[dict[str, Any]]:
        """List all documents in a folder, optionally recursive.

        Args:
            folder_id: Folder ID
            recursive: If True, include documents in subfolders

        Returns:
            List of document metadata dictionaries with keys:
            - id: document ID
            - name: document name
            - folder_path: path within folder hierarchy (if recursive)
        """
        documents: list[dict[str, Any]] = []
        self._collect_folder_documents(folder_id, documents, "", recursive)
        return documents

    def _collect_folder_documents(
        self,
        folder_id: str,
        documents: list[dict[str, Any]],
        current_path: str,
        recursive: bool,
    ) -> None:
        """Recursively collect documents from a folder.

        Args:
            folder_id: Current folder ID
            documents: List to append documents to
            current_path: Current path in hierarchy
            recursive: Whether to recurse into subfolders
        """
        folder_data = self.get_folder_contents(folder_id)

        for item in folder_data.get("items", []):
            item_type = item.get("resourceType", "")
            item_name = item.get("name", "")
            item_id = item.get("id", "")

            if item_type == "document":
                documents.append({
                    "id": item_id,
                    "name": item_name,
                    "folder_path": current_path,
                    "created_at": item.get("createdAt", ""),
                    "modified_at": item.get("modifiedAt", ""),
                    "owner": item.get("owner", {}).get("name", ""),
                })
            elif item_type == "folder" and recursive:
                subfolder_path = f"{current_path}/{item_name}" if current_path else item_name
                self._collect_folder_documents(
                    item_id,
                    documents,
                    subfolder_path,
                    recursive,
                )

    def get_folder_tree(
        self,
        folder_id: str,
        max_depth: int = 10,
    ) -> dict[str, Any]:
        """Get folder structure as a tree.

        Args:
            folder_id: Root folder ID
            max_depth: Maximum recursion depth

        Returns:
            Tree structure with folders and documents
        """
        return self._build_folder_tree(folder_id, 0, max_depth)

    def _build_folder_tree(
        self,
        folder_id: str,
        current_depth: int,
        max_depth: int,
    ) -> dict[str, Any]:
        """Recursively build folder tree.

        Args:
            folder_id: Current folder ID
            current_depth: Current depth in tree
            max_depth: Maximum depth to recurse

        Returns:
            Tree node with children
        """
        folder_data = self.get_folder_contents(folder_id)

        tree: dict[str, Any] = {
            "id": folder_id,
            "name": folder_data.get("name", ""),
            "folders": [],
            "documents": [],
        }

        for item in folder_data.get("items", []):
            item_type = item.get("resourceType", "")

            if item_type == "document":
                tree["documents"].append({
                    "id": item.get("id", ""),
                    "name": item.get("name", ""),
                })
            elif item_type == "folder" and current_depth < max_depth:
                subtree = self._build_folder_tree(
                    item.get("id", ""),
                    current_depth + 1,
                    max_depth,
                )
                subtree["name"] = item.get("name", "")
                tree["folders"].append(subtree)

        return tree

    # -------------------------------------------------------------------------
    # Standard Library Operations
    # -------------------------------------------------------------------------

    def get_std_library_document_id(self) -> str:
        """Get the document ID for the Onshape standard library.

        The std library is a public document that can be discovered via search
        or is well-known. This returns the standard document ID.

        Returns:
            Document ID for Onshape std library
        """
        # The Onshape std library document ID (well-known)
        # This may need to be updated if Onshape changes it
        return "12312312345abcabcabcdeff"  # Placeholder - needs real ID

    # -------------------------------------------------------------------------
    # Health Check
    # -------------------------------------------------------------------------

    def verify_connection(self) -> bool:
        """Verify API connectivity and authentication.

        Returns:
            True if connection successful

        Raises:
            OnshapeAPIError: On connection or auth failure
        """
        # Use a lightweight endpoint to test authentication
        path = f"/api/{self.API_VERSION}/users/sessioninfo"
        response = self.get(path)
        return "id" in response or "email" in response
