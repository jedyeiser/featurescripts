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
from .core.inspector import see_response, compare_responses, inspect_document, inspect_folder, inspect_element
from .core.references import ReferenceManager
from .core.working import WorkingDirectoryManager
from .models.config import SyncConfig, FolderConfig, migrate_config_to_project_settings
from .models.project_config import FeatureScriptSettings

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
            console.print(f"  [blue]{f.name}[/blue] -> {f.local_path}")
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


def cmd_inspect(args: argparse.Namespace) -> int:
    """Inspect API responses (debug tool)."""
    import json

    try:
        client = OnshapeClient()
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")
        return 1

    # Parse parameters if provided
    params = None
    if hasattr(args, "params") and args.params:
        try:
            params = json.loads(args.params)
        except json.JSONDecodeError as e:
            console.print(f"[red]Invalid JSON in --params: {e}")
            return 1

    # Handle different inspect subcommands
    if args.inspect_command == "endpoint":
        try:
            see_response(
                endpoint=args.endpoint,
                method=args.method,
                params=params,
                save_to=args.save,
                client=client,
            )
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.inspect_command == "compare":
        try:
            compare_responses(args.file1, args.file2)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.inspect_command == "document":
        try:
            inspect_document(args.doc_id, args.ws_id, client)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.inspect_command == "folder":
        try:
            inspect_folder(args.folder_id, client)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.inspect_command == "element":
        try:
            inspect_element(args.doc_id, args.ws_id, args.elem_id, client)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    return 1


def cmd_migrate(args: argparse.Namespace) -> int:
    """Migrate config.yaml to featurescriptSettings.json."""
    base_dir = get_base_dir()
    config_path = base_dir / "sync" / "config.yaml"
    settings_path = base_dir / "featurescriptSettings.json"

    if not config_path.exists():
        console.print(f"[red]Config not found: {config_path}")
        return 1

    if settings_path.exists() and not args.force:
        console.print(f"[yellow]featurescriptSettings.json already exists!")
        console.print("Use --force to overwrite")
        return 1

    try:
        config = SyncConfig.load(config_path)
        migrate_config_to_project_settings(config, settings_path)
        console.print(f"[green]Migration successful!")
        console.print(f"Settings saved to: {settings_path}")
        return 0
    except Exception as e:
        console.print(f"[red]Migration failed: {e}")
        return 1


def cmd_project(args: argparse.Namespace) -> int:
    """Manage working projects (bidirectional sync)."""
    base_dir = get_base_dir()
    settings_path = base_dir / "featurescriptSettings.json"

    # Load or create settings
    settings = FeatureScriptSettings.load(settings_path)

    try:
        client = OnshapeClient()
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")
        return 1

    work_manager = WorkingDirectoryManager(settings, settings_path, base_dir, client)

    # Handle different project subcommands
    if args.project_command == "add":
        try:
            refs = args.references.split(",") if args.references else []
            work_manager.add_project(
                url=args.url,
                name=args.name,
                description=args.description or "",
                local_path=args.path,
                references=refs,
            )
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.project_command == "list":
        projects = work_manager.list_projects()
        if not projects:
            console.print("[yellow]No projects configured")
            return 0

        table = Table(title="Projects")
        table.add_column("Name", style="cyan")
        table.add_column("Description", style="blue")
        table.add_column("Working Directory", style="green")
        table.add_column("Last Pull")
        table.add_column("Last Push")

        for proj in projects:
            last_pull = proj["last_pull"][:19] if proj["last_pull"] else "[dim]Never"
            last_push = proj["last_push"][:19] if proj["last_push"] else "[dim]Never"
            table.add_row(
                proj["name"],
                proj["description"][:50] + "..." if len(proj["description"]) > 50 else proj["description"],
                proj["working_directory"],
                last_pull,
                last_push,
            )

        console.print(table)
        return 0

    elif args.project_command == "status":
        try:
            work_manager.get_status(args.name)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.project_command == "remove":
        try:
            work_manager.remove_project(args.name, delete_files=args.delete_files)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    return 1


def cmd_get(args: argparse.Namespace) -> int:
    """Pull a working project from Onshape."""
    base_dir = get_base_dir()
    settings_path = base_dir / "featurescriptSettings.json"

    # Load settings
    settings = FeatureScriptSettings.load(settings_path)

    try:
        client = OnshapeClient()
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")
        return 1

    work_manager = WorkingDirectoryManager(settings, settings_path, base_dir, client)

    try:
        result = work_manager.get_working_directory(
            project_name=args.project_name,
            force=args.force,
            dry_run=args.dry_run,
        )
        return 0 if result["success"] else 1
    except Exception as e:
        console.print(f"[red]Error: {e}")
        return 1


def cmd_push_new(args: argparse.Namespace) -> int:
    """Push a working project to Onshape."""
    base_dir = get_base_dir()
    settings_path = base_dir / "featurescriptSettings.json"

    # Load settings
    settings = FeatureScriptSettings.load(settings_path)

    try:
        client = OnshapeClient()
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")
        return 1

    work_manager = WorkingDirectoryManager(settings, settings_path, base_dir, client)

    try:
        result = work_manager.push_working_directory(
            project_name=args.project_name,
            files=args.files if hasattr(args, "files") else None,
            force=args.force,
            dry_run=args.dry_run,
        )
        return 0 if result["success"] else 1
    except Exception as e:
        console.print(f"[red]Error: {e}")
        return 1


def cmd_reference(args: argparse.Namespace) -> int:
    """Manage references (read-only libraries from Onshape)."""
    base_dir = get_base_dir()
    settings_path = base_dir / "featurescriptSettings.json"

    # Load or create settings
    settings = FeatureScriptSettings.load(settings_path)

    try:
        client = OnshapeClient()
    except ValueError as e:
        console.print(f"[red]Configuration error: {e}")
        return 1

    ref_manager = ReferenceManager(settings, settings_path, base_dir, client)

    # Handle different reference subcommands
    if args.reference_command == "add":
        try:
            ref_manager.add_reference(
                url=args.url,
                name=args.name,
                local_path=args.path,
                auto_update=args.auto_update,
                recursive=not args.no_recursive,
            )
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    elif args.reference_command == "list":
        references = ref_manager.list_references()
        if not references:
            console.print("[yellow]No references configured")
            return 0

        table = Table(title="References")
        table.add_column("Name", style="cyan")
        table.add_column("Type", style="blue")
        table.add_column("Local Path", style="green")
        table.add_column("Auto Update", style="yellow")
        table.add_column("Last Sync")

        for ref in references:
            auto_update = "[green]Yes" if ref["auto_update"] else "[dim]No"
            last_sync = ref["last_sync"][:19] if ref["last_sync"] else "[dim]Never"
            table.add_row(
                ref["name"],
                ref["type"],
                ref["local_path"],
                auto_update,
                last_sync,
            )

        console.print(table)
        return 0

    elif args.reference_command == "update":
        if args.name:
            # Update single reference
            try:
                success = ref_manager.update_single_reference(args.name, force=args.force)
                return 0 if success else 1
            except Exception as e:
                console.print(f"[red]Error: {e}")
                return 1
        else:
            # Update all references
            results = ref_manager.update_references(force=args.force, check_only=args.check)
            success_count = sum(1 for s in results.values() if s)
            failed_count = len(results) - success_count

            console.print(f"\n[bold]Summary:[/bold] {success_count} successful, {failed_count} failed")
            return 0 if failed_count == 0 else 1

    elif args.reference_command == "remove":
        try:
            ref_manager.remove_reference(args.name, delete_files=args.delete_files)
            return 0
        except Exception as e:
            console.print(f"[red]Error: {e}")
            return 1

    return 1


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

    # inspect command
    inspect_parser = subparsers.add_parser("inspect", help="Inspect API responses (debug tool)")
    inspect_subparsers = inspect_parser.add_subparsers(dest="inspect_command")

    # inspect endpoint
    inspect_endpoint = inspect_subparsers.add_parser("endpoint", help="Inspect arbitrary endpoint")
    inspect_endpoint.add_argument("endpoint", help="API endpoint (e.g., /api/v10/users/sessioninfo)")
    inspect_endpoint.add_argument("--method", default="GET", help="HTTP method (default: GET)")
    inspect_endpoint.add_argument("--params", help="JSON parameters")
    inspect_endpoint.add_argument("--save", help="Save response to file")

    # inspect compare
    inspect_compare = inspect_subparsers.add_parser("compare", help="Compare two saved responses")
    inspect_compare.add_argument("file1", help="First response file")
    inspect_compare.add_argument("file2", help="Second response file")

    # inspect document
    inspect_doc = inspect_subparsers.add_parser("document", help="Inspect document metadata")
    inspect_doc.add_argument("doc_id", help="Document ID")
    inspect_doc.add_argument("--ws-id", help="Workspace ID")

    # inspect folder
    inspect_folder_cmd = inspect_subparsers.add_parser("folder", help="Inspect folder contents")
    inspect_folder_cmd.add_argument("folder_id", help="Folder ID")

    # inspect element
    inspect_elem = inspect_subparsers.add_parser("element", help="Inspect Feature Studio element")
    inspect_elem.add_argument("doc_id", help="Document ID")
    inspect_elem.add_argument("ws_id", help="Workspace ID")
    inspect_elem.add_argument("elem_id", help="Element ID")

    # migrate command
    migrate_parser = subparsers.add_parser("migrate", help="Migrate config.yaml to featurescriptSettings.json")
    migrate_parser.add_argument("--force", action="store_true", help="Overwrite existing settings file")

    # project commands
    project_parser = subparsers.add_parser("project", help="Manage working projects (bidirectional)")
    project_subparsers = project_parser.add_subparsers(dest="project_command")

    # project add
    project_add = project_subparsers.add_parser("add", help="Add a new working project")
    project_add.add_argument("url", help="Onshape URL (document or folder)")
    project_add.add_argument("name", help="Human-readable name for the project")
    project_add.add_argument("--description", help="Project description")
    project_add.add_argument("--path", help="Local path (default: ./projects/{name})")
    project_add.add_argument("--references", help="Comma-separated list of reference names")

    # project list
    project_subparsers.add_parser("list", help="List all configured projects")

    # project status
    project_status = project_subparsers.add_parser("status", help="Show project sync status")
    project_status.add_argument("name", help="Project name")

    # project remove
    project_remove = project_subparsers.add_parser("remove", help="Remove a project")
    project_remove.add_argument("name", help="Project name")
    project_remove.add_argument("--delete-files", action="store_true", help="Also delete local files")

    # get command (pull working project)
    get_parser = subparsers.add_parser("get", help="Pull a working project from Onshape")
    get_parser.add_argument("project_name", help="Project name")
    get_parser.add_argument("--force", action="store_true", help="Overwrite local changes")
    get_parser.add_argument("--dry-run", action="store_true", help="Show what would happen")

    # push command (new style for working projects)
    push_new_parser = subparsers.add_parser("pushproject", help="Push a working project to Onshape")
    push_new_parser.add_argument("project_name", help="Project name")
    push_new_parser.add_argument("--files", nargs="+", help="Specific files to push")
    push_new_parser.add_argument("--force", action="store_true", help="Overwrite remote changes")
    push_new_parser.add_argument("--dry-run", action="store_true", help="Show what would happen")

    # reference commands
    reference_parser = subparsers.add_parser("reference", help="Manage reference libraries (read-only)")
    reference_subparsers = reference_parser.add_subparsers(dest="reference_command")

    # reference add
    reference_add = reference_subparsers.add_parser("add", help="Add a new reference library")
    reference_add.add_argument("url", help="Onshape URL (document or folder)")
    reference_add.add_argument("name", help="Human-readable name for the reference")
    reference_add.add_argument("--path", help="Local path (default: ./references/{name})")
    reference_add.add_argument("--auto-update", action="store_true", help="Enable automatic updates")
    reference_add.add_argument("--no-recursive", action="store_true", help="Don't sync subfolders")

    # reference list
    reference_subparsers.add_parser("list", help="List all configured references")

    # reference update
    reference_update = reference_subparsers.add_parser("update", help="Update references from Onshape")
    reference_update.add_argument("name", nargs="?", help="Specific reference to update")
    reference_update.add_argument("--force", action="store_true", help="Force update even if auto_update=false")
    reference_update.add_argument("--check", action="store_true", help="Only check for updates, don't download")

    # reference remove
    reference_remove = reference_subparsers.add_parser("remove", help="Remove a reference")
    reference_remove.add_argument("name", help="Reference name")
    reference_remove.add_argument("--delete-files", action="store_true", help="Also delete local files")

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
    elif args.command == "inspect":
        if args.inspect_command:
            return cmd_inspect(args)
        else:
            inspect_parser.print_help()
            return 1
    elif args.command == "migrate":
        return cmd_migrate(args)
    elif args.command == "project":
        if args.project_command:
            return cmd_project(args)
        else:
            project_parser.print_help()
            return 1
    elif args.command == "get":
        return cmd_get(args)
    elif args.command == "pushproject":
        return cmd_push_new(args)
    elif args.command == "reference":
        if args.reference_command:
            return cmd_reference(args)
        else:
            reference_parser.print_help()
            return 1
    else:
        parser.print_help()
        return 1


if __name__ == "__main__":
    sys.exit(main())
