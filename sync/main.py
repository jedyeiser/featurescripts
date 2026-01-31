#!/usr/bin/env python3
"""CLI entry point for FeatureScript sync system."""

import argparse
import sys
from pathlib import Path

from rich.console import Console
from rich.table import Table
from rich.tree import Tree

from .core.cache import CacheManager
from .core.client import OnshapeClient, OnshapeAPIError
from .core.operations import SyncOperations
from .models.config import SyncConfig, FolderConfig

console = Console()


def get_base_dir() -> Path:
    """Get the base directory (integrationPrompts)."""
    return Path(__file__).parent.parent


def cmd_verify_auth(args: argparse.Namespace) -> int:
    """Verify API authentication."""
    console.print("Verifying Onshape API credentials...", style="blue")

    try:
        client = OnshapeClient()
        if client.verify_connection():
            console.print("[green]Authentication successful!")
            return 0
    except OnshapeAPIError as e:
        console.print(f"[red]Authentication failed: {e}")
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")

    return 1


def cmd_tree(args: argparse.Namespace) -> int:
    """Show folder tree from Onshape."""
    base_dir = get_base_dir()
    config_path = base_dir / "sync" / "config.yaml"

    if not config_path.exists():
        console.print(f"[red]Config not found: {config_path}")
        return 1

    config = SyncConfig.load(config_path)

    if not config.folders:
        console.print("[yellow]No folders configured in config.yaml")
        return 0

    try:
        client = OnshapeClient()
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")
        return 1

    for folder_config in config.folders:
        console.print(f"\n[bold]Fetching tree for: {folder_config.name}[/bold]")

        try:
            tree_data = client.get_folder_tree(folder_config.folder_id)
            _render_tree(tree_data, folder_config.name)
        except OnshapeAPIError as e:
            console.print(f"[red]Failed to fetch folder: {e}")
            return 1

    return 0


def _render_tree(tree_data: dict, root_name: str) -> None:
    """Render folder tree using Rich."""
    tree = Tree(f"[bold blue]{root_name}[/bold blue]")
    _add_tree_nodes(tree, tree_data)
    console.print(tree)


def _add_tree_nodes(parent: Tree, data: dict) -> None:
    """Recursively add nodes to tree."""
    # Add subfolders
    for folder in data.get("folders", []):
        folder_node = parent.add(f"[blue]{folder['name']}/[/blue]")
        _add_tree_nodes(folder_node, folder)

    # Add documents
    for doc in data.get("documents", []):
        parent.add(f"[green]{doc['name']}[/green]")


def cmd_cache_status(args: argparse.Namespace) -> int:
    """Show cache status."""
    base_dir = get_base_dir()
    std_dir = base_dir / "std"
    cache = CacheManager(std_dir)

    status = cache.status()

    console.print(f"\n[bold]Std Directory:[/bold] {status['std_dir']}")
    console.print(f"[bold]Manifest Version:[/bold] {status['manifest_version']}")
    console.print(f"[bold]Last Updated:[/bold] {status['last_updated'] or 'Never'}")
    console.print(f"[bold]Onshape Std Version:[/bold] {status['onshape_std_version']}")
    console.print(f"[bold]Total Files:[/bold] {status['total_files']}")

    if status["files"]:
        table = Table(title="\nCached Files")
        table.add_column("Filename")
        table.add_column("Exists")
        table.add_column("Microversion")
        table.add_column("Fetched At")

        for f in status["files"]:
            exists = "[green]Yes" if f["exists"] else "[red]No"
            table.add_row(
                f["filename"],
                exists,
                f["microversion"],
                f["fetched_at"] or "Never",
            )

        console.print(table)
    else:
        console.print("\n[yellow]No files in cache manifest.")

    return 0


def cmd_cache_update(args: argparse.Namespace) -> int:
    """Update cached files."""
    base_dir = get_base_dir()
    std_dir = base_dir / "std"
    cache = CacheManager(std_dir)

    filename = args.file if hasattr(args, "file") and args.file else None
    force = args.force if hasattr(args, "force") else False

    if args.all:
        console.print("Updating all cached files...", style="blue")
        results = cache.update(force=force)
    elif filename:
        console.print(f"Updating {filename}...", style="blue")
        results = cache.update(filename=filename, force=force)
    else:
        console.print("[red]Specify a filename or --all")
        return 1

    # Show results
    success = all(results.values())
    for fname, ok in results.items():
        status = "[green]OK" if ok else "[red]FAILED"
        console.print(f"  {fname}: {status}")

    return 0 if success else 1


def cmd_cache_add(args: argparse.Namespace) -> int:
    """Add a file to the cache manifest."""
    base_dir = get_base_dir()
    std_dir = base_dir / "std"
    cache = CacheManager(std_dir)

    cache.add_to_manifest(
        filename=args.filename,
        document_id=args.document_id,
        element_id=args.element_id,
    )

    console.print(f"[green]Added {args.filename} to manifest")
    return 0


def cmd_pull(args: argparse.Namespace) -> int:
    """Pull files from Onshape."""
    base_dir = get_base_dir()
    config_path = base_dir / "sync" / "config.yaml"

    if not config_path.exists():
        console.print(f"[red]Config not found: {config_path}")
        return 1

    config = SyncConfig.load(config_path)

    if not config.folders and not config.documents:
        console.print("[yellow]No folders or documents configured in config.yaml")
        console.print("Add a folder to sync:")
        console.print("  folders:")
        console.print('    - name: "My Folder"')
        console.print('      folder_id: "your_folder_id_here"')
        console.print('      local_path: "./featurescripts/myfolder"')
        return 0

    ops = SyncOperations(config, base_dir=base_dir)

    console.print("Pulling from Onshape...", style="blue")
    if args.dry_run:
        console.print("[yellow](DRY RUN - no changes will be made)")
    if args.force:
        console.print("[yellow](FORCE - will overwrite local changes)")

    results = ops.pull_all(dry_run=args.dry_run, force=args.force)

    # Show results
    success_count = sum(1 for r in results if r.success and not r.skipped)
    skipped_count = sum(1 for r in results if r.skipped)
    conflict_count = sum(1 for r in results if r.conflict)
    failed_count = sum(1 for r in results if not r.success and not r.conflict)

    for result in results:
        if result.conflict:
            console.print(f"[red]CONFLICT: {result.filepath}")
            console.print(f"          {result.message}")
        elif not result.success:
            console.print(f"[red]FAILED: {result.filepath}")
            console.print(f"        {result.message}")
        elif result.skipped:
            console.print(f"[dim]{result.message}[/dim]")
        else:
            console.print(f"[green]{result.message}")

    console.print(f"\n[bold]Summary:[/bold] {success_count} pulled, {skipped_count} skipped, {conflict_count} conflicts, {failed_count} failed")

    return 0 if conflict_count == 0 and failed_count == 0 else 1


def cmd_push(args: argparse.Namespace) -> int:
    """Push files to Onshape."""
    base_dir = get_base_dir()
    config_path = base_dir / "sync" / "config.yaml"

    if not config_path.exists():
        console.print(f"[red]Config not found: {config_path}")
        return 1

    config = SyncConfig.load(config_path)

    if not config.folders and not config.documents:
        console.print("[yellow]No folders or documents configured in config.yaml")
        return 0

    ops = SyncOperations(config, base_dir=base_dir)

    console.print("Pushing to Onshape...", style="blue")
    if args.dry_run:
        console.print("[yellow](DRY RUN - no changes will be made)")
    if args.force:
        console.print("[yellow](FORCE - will overwrite remote changes)")

    # Extra warning for non-dry-run push
    if not args.dry_run:
        console.print("\n[bold yellow]WARNING: This will overwrite Feature Studios in Onshape![/bold yellow]")
        console.print("Make sure you have pulled recently to avoid conflicts.\n")

    results = ops.push_all(dry_run=args.dry_run, force=args.force)

    # Show results
    success_count = sum(1 for r in results if r.success and not r.skipped)
    skipped_count = sum(1 for r in results if r.skipped)
    conflict_count = sum(1 for r in results if r.conflict)
    failed_count = sum(1 for r in results if not r.success and not r.conflict)

    for result in results:
        if result.conflict:
            console.print(f"[red]CONFLICT: {result.filepath}")
            console.print(f"          {result.message}")
        elif not result.success:
            console.print(f"[red]FAILED: {result.filepath}")
            console.print(f"        {result.message}")
        elif result.skipped:
            console.print(f"[dim]{result.message}[/dim]")
        else:
            console.print(f"[green]{result.message}")

    console.print(f"\n[bold]Summary:[/bold] {success_count} pushed, {skipped_count} skipped, {conflict_count} conflicts, {failed_count} failed")

    return 0 if conflict_count == 0 and failed_count == 0 else 1


def cmd_status(args: argparse.Namespace) -> int:
    """Show sync status."""
    base_dir = get_base_dir()
    config_path = base_dir / "sync" / "config.yaml"

    config = SyncConfig.load(config_path) if config_path.exists() else SyncConfig()

    # Show configured folders
    console.print("\n[bold]Configured Folders:[/bold]")
    if config.folders:
        for f in config.folders:
            console.print(f"  [blue]{f.name}[/blue] → {f.local_path}")
    else:
        console.print("  [dim]None[/dim]")

    # Show sync state
    ops = SyncOperations(config, base_dir=base_dir)
    status = ops.get_status()

    console.print(f"\n[bold]Tracked Files:[/bold] {status['tracked_files']}")

    if status["files"]:
        table = Table()
        table.add_column("Path")
        table.add_column("Last Sync")
        table.add_column("Hash")

        for f in status["files"]:
            table.add_row(f["path"], f["last_sync"][:19] if f["last_sync"] else "Never", f["hash"])

        console.print(table)
    else:
        console.print("[dim]No files tracked yet. Run 'pull' to start syncing.[/dim]")

    return 0


def main() -> int:
    """Main CLI entry point."""
    parser = argparse.ArgumentParser(
        prog="featurescript-sync",
        description="Sync FeatureScript files between local repo and Onshape",
    )
    subparsers = parser.add_subparsers(dest="command", help="Command to run")

    # verify-auth command
    subparsers.add_parser("verify-auth", help="Verify API authentication")

    # tree command
    subparsers.add_parser("tree", help="Show folder structure from Onshape")

    # cache commands
    cache_parser = subparsers.add_parser("cache", help="Cache management")
    cache_subparsers = cache_parser.add_subparsers(dest="cache_command")

    cache_subparsers.add_parser("status", help="Show cache status")

    cache_update = cache_subparsers.add_parser("update", help="Update cached files")
    cache_update.add_argument("file", nargs="?", help="Specific file to update")
    cache_update.add_argument("--all", action="store_true", help="Update all files")
    cache_update.add_argument("--force", action="store_true", help="Force update")

    cache_add = cache_subparsers.add_parser("add", help="Add file to manifest")
    cache_add.add_argument("filename", help="Filename (e.g., geometry.fs)")
    cache_add.add_argument("--document-id", required=True, help="Onshape document ID")
    cache_add.add_argument("--element-id", required=True, help="Onshape element ID")

    # pull command
    pull_parser = subparsers.add_parser("pull", help="Pull files from Onshape")
    pull_parser.add_argument("--dry-run", action="store_true", help="Show what would happen")
    pull_parser.add_argument("--force", action="store_true", help="Overwrite local changes")

    # push command
    push_parser = subparsers.add_parser("push", help="Push files to Onshape")
    push_parser.add_argument("--dry-run", action="store_true", help="Show what would happen")
    push_parser.add_argument("--force", action="store_true", help="Overwrite remote changes")

    # status command
    subparsers.add_parser("status", help="Show sync status")

    args = parser.parse_args()

    if args.command == "verify-auth":
        return cmd_verify_auth(args)
    elif args.command == "tree":
        return cmd_tree(args)
    elif args.command == "cache":
        if args.cache_command == "status":
            return cmd_cache_status(args)
        elif args.cache_command == "update":
            return cmd_cache_update(args)
        elif args.cache_command == "add":
            return cmd_cache_add(args)
        else:
            cache_parser.print_help()
            return 1
    elif args.command == "pull":
        return cmd_pull(args)
    elif args.command == "push":
        return cmd_push(args)
    elif args.command == "status":
        return cmd_status(args)
    else:
        parser.print_help()
        return 1


if __name__ == "__main__":
    sys.exit(main())
