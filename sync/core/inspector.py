"""API response inspection and debugging tools.

This module provides utilities for inspecting Onshape API responses,
saving them for documentation, and comparing responses.
"""

import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from rich.console import Console
from rich.json import JSON
from rich.panel import Panel
from rich.syntax import Syntax
from rich.table import Table

from .client import OnshapeClient, OnshapeAPIError


console = Console()


def see_response(
    endpoint: str,
    method: str = "GET",
    params: dict[str, Any] | None = None,
    save_to: str | None = None,
    client: OnshapeClient | None = None,
) -> dict[str, Any]:
    """Make an API call and inspect the response.

    Args:
        endpoint: API endpoint (e.g., "/api/v10/users/sessioninfo")
        method: HTTP method (GET, POST, etc.)
        params: Query parameters or request body
        save_to: Optional path to save response
        client: Optional OnshapeClient instance (creates new if None)

    Returns:
        Response data as dictionary

    Raises:
        OnshapeAPIError: If API call fails
    """
    if client is None:
        client = OnshapeClient()

    console.print(f"\n[bold blue]Making {method} request to:[/bold blue] {endpoint}")

    if params:
        console.print("\n[bold]Parameters:[/bold]")
        console.print(JSON(json.dumps(params)))

    try:
        # Make the API call using the client's request method
        if method.upper() == "GET":
            response = client._request(method, endpoint, query_params=params)
        else:
            response = client._request(method, endpoint, json_data=params)

        console.print("\n[bold green]Response received successfully![/bold green]")

        # Display response
        _display_response(response, endpoint)

        # Save if requested
        if save_to:
            save_response(response, save_to, endpoint, method)
            console.print(f"\n[green]Response saved to:[/green] {save_to}")

        return response

    except OnshapeAPIError as e:
        console.print(f"\n[red]API Error:[/red] {e}")
        raise


def _display_response(data: dict[str, Any], endpoint: str) -> None:
    """Display formatted response data."""
    # Extract key metadata if present
    metadata = _extract_metadata(data)

    if metadata:
        console.print("\n[bold]Key Metadata:[/bold]")
        table = Table(show_header=False, box=None)
        table.add_column("Field", style="cyan")
        table.add_column("Value", style="green")

        for key, value in metadata.items():
            table.add_row(key, str(value))

        console.print(table)

    # Display full JSON
    console.print("\n[bold]Full Response:[/bold]")
    json_str = json.dumps(data, indent=2)
    syntax = Syntax(json_str, "json", theme="monokai", line_numbers=False)
    console.print(Panel(syntax, title="JSON Response", expand=False))


def _extract_metadata(data: dict[str, Any]) -> dict[str, Any]:
    """Extract key metadata fields from response."""
    metadata: dict[str, Any] = {}

    # Common fields to extract
    key_fields = [
        "id",
        "name",
        "documentId",
        "workspaceId",
        "elementId",
        "folderId",
        "microversion",
        "modifiedAt",
        "createdAt",
        "type",
    ]

    for field in key_fields:
        if field in data:
            metadata[field] = data[field]

    return metadata


def save_response(
    response: dict[str, Any],
    filepath: str,
    endpoint: str | None = None,
    method: str = "GET",
    format: str = "json",
) -> None:
    """Save API response to file.

    Args:
        response: Response data to save
        filepath: Path to save file
        endpoint: Optional endpoint for metadata
        method: HTTP method for metadata
        format: Output format (json, yaml, or txt)
    """
    path = Path(filepath)
    path.parent.mkdir(parents=True, exist_ok=True)

    # Add metadata
    output = {
        "metadata": {
            "endpoint": endpoint,
            "method": method,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        },
        "response": response,
    }

    if format.lower() == "json":
        with open(path, "w", encoding="utf-8") as f:
            json.dump(output, f, indent=2)
    elif format.lower() == "yaml":
        import yaml
        with open(path, "w", encoding="utf-8") as f:
            yaml.dump(output, f, default_flow_style=False)
    else:
        # Raw text format
        with open(path, "w", encoding="utf-8") as f:
            f.write(json.dumps(output, indent=2))


def compare_responses(file1: str, file2: str) -> None:
    """Compare two saved response files.

    Args:
        file1: Path to first response file
        file2: Path to second response file
    """
    path1 = Path(file1)
    path2 = Path(file2)

    if not path1.exists():
        console.print(f"[red]File not found:[/red] {file1}")
        return

    if not path2.exists():
        console.print(f"[red]File not found:[/red] {file2}")
        return

    with open(path1, "r", encoding="utf-8") as f:
        data1 = json.load(f)

    with open(path2, "r", encoding="utf-8") as f:
        data2 = json.load(f)

    console.print(f"\n[bold]Comparing:[/bold]")
    console.print(f"  File 1: {file1}")
    console.print(f"  File 2: {file2}")

    # Compare metadata
    meta1 = data1.get("metadata", {})
    meta2 = data2.get("metadata", {})

    if meta1 or meta2:
        console.print("\n[bold cyan]Metadata Comparison:[/bold cyan]")
        table = Table()
        table.add_column("Field")
        table.add_column("File 1", style="green")
        table.add_column("File 2", style="blue")

        all_keys = set(meta1.keys()) | set(meta2.keys())
        for key in sorted(all_keys):
            val1 = meta1.get(key, "[dim]N/A[/dim]")
            val2 = meta2.get(key, "[dim]N/A[/dim]")
            table.add_row(key, str(val1), str(val2))

        console.print(table)

    # Compare responses
    resp1 = data1.get("response", {})
    resp2 = data2.get("response", {})

    differences = _find_differences(resp1, resp2)

    if differences:
        console.print(f"\n[bold yellow]Found {len(differences)} differences:[/bold yellow]")
        for diff in differences:
            console.print(f"  [yellow]•[/yellow] {diff}")
    else:
        console.print("\n[bold green]No differences found![/bold green]")


def _find_differences(
    obj1: Any,
    obj2: Any,
    path: str = "root",
) -> list[str]:
    """Recursively find differences between two objects."""
    differences: list[str] = []

    if type(obj1) != type(obj2):
        differences.append(f"{path}: Type mismatch ({type(obj1).__name__} vs {type(obj2).__name__})")
        return differences

    if isinstance(obj1, dict):
        all_keys = set(obj1.keys()) | set(obj2.keys())
        for key in all_keys:
            if key not in obj1:
                differences.append(f"{path}.{key}: Only in second object")
            elif key not in obj2:
                differences.append(f"{path}.{key}: Only in first object")
            else:
                differences.extend(_find_differences(obj1[key], obj2[key], f"{path}.{key}"))

    elif isinstance(obj1, list):
        if len(obj1) != len(obj2):
            differences.append(f"{path}: List length mismatch ({len(obj1)} vs {len(obj2)})")
        else:
            for i, (item1, item2) in enumerate(zip(obj1, obj2)):
                differences.extend(_find_differences(item1, item2, f"{path}[{i}]"))

    else:
        if obj1 != obj2:
            differences.append(f"{path}: {obj1} != {obj2}")

    return differences


def inspect_document(doc_id: str, ws_id: str | None = None, client: OnshapeClient | None = None) -> dict[str, Any]:
    """Inspect a document and show its metadata.

    Args:
        doc_id: Document ID
        ws_id: Optional workspace ID
        client: Optional OnshapeClient instance

    Returns:
        Document data
    """
    if client is None:
        client = OnshapeClient()

    endpoint = f"/api/v10/documents/d/{doc_id}"
    if ws_id:
        endpoint += f"/w/{ws_id}"

    return see_response(endpoint, client=client)


def inspect_folder(folder_id: str, client: OnshapeClient | None = None) -> dict[str, Any]:
    """Inspect a folder and show its contents.

    Args:
        folder_id: Folder ID
        client: Optional OnshapeClient instance

    Returns:
        Folder data
    """
    if client is None:
        client = OnshapeClient()

    endpoint = f"/api/v10/globaltreenodes/folder/{folder_id}"
    return see_response(endpoint, client=client)


def inspect_element(doc_id: str, ws_id: str, elem_id: str, client: OnshapeClient | None = None) -> dict[str, Any]:
    """Inspect a Feature Studio element.

    Args:
        doc_id: Document ID
        ws_id: Workspace ID
        elem_id: Element ID
        client: Optional OnshapeClient instance

    Returns:
        Element data
    """
    if client is None:
        client = OnshapeClient()

    endpoint = f"/api/v10/featurestudios/d/{doc_id}/w/{ws_id}/e/{elem_id}"
    return see_response(endpoint, client=client)
