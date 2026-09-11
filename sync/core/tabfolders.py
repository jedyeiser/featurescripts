"""Keep Onshape tab folders in step with local subdirectories.

Rule: a local file at <project>/<sub>/<name>.fs lives in the Onshape tab folder
<sub>; a file at <project>/<name>.fs lives at the document root. One level only.
The public API cannot see or move tab folders, so this drives the browser.
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

from .browser import OnshapeBrowser, TabBar


def desired_folder(local_dir: Path, fs_file: Path) -> str:
    """Tab folder a local file should live in ('' = root)."""
    rel = fs_file.resolve().relative_to(local_dir.resolve())
    return rel.parts[0] if len(rel.parts) > 1 else ""


def _element_folders(structure: dict[str, dict[str, str]]) -> dict[str, str]:
    """Invert {folder: {id: name}} into {id: folder}."""
    return {eid: folder for folder, members in structure.items() for eid in members}


def read_tab_folders(
    document_id: str,
    workspace_id: str,
    open_element_id: str,
    headless: bool = True,
) -> dict[str, str]:
    """{element_name: folder_name} for every tab inside a first-level folder."""
    with OnshapeBrowser(headless=headless) as b:
        b.open_element(document_id, workspace_id, open_element_id)
        bar = TabBar(b)
        bar.wait_ready()
        structure = bar.read_structure()
    return {name: folder for folder, members in structure.items() if folder for name in members.values()}


def sync_tab_folders(
    document_id: str,
    workspace_id: str,
    open_element_id: str,
    placements: dict[str, str],
    headless: bool = True,
) -> tuple[list[str], dict[str, str]]:
    """Move tabs so each element_id in `placements` sits in its folder ('' = root).

    Creates missing folders. Returns (human-readable actions, {element_name: folder}
    for the whole document afterwards).
    """
    actions: list[str] = []
    with OnshapeBrowser(headless=headless) as b:
        b.open_element(document_id, workspace_id, open_element_id)
        bar = TabBar(b)
        bar.wait_ready()
        structure = bar.read_structure()
        current = _element_folders(structure)
        names = {eid: name for members in structure.values() for eid, name in members.items()}

        for eid, want in placements.items():
            if eid not in current:
                actions.append(f"{eid}: not visible in tab bar (nested folder?) - skipped")
                continue
            have = current[eid]
            if have == want:
                continue
            name = names[eid]
            if have:
                bar.open_folder(have)
                bar.move_to_parent(eid)
                bar.go_home()
            if want:
                if want not in structure:
                    bar.create_folder(want)
                    structure[want] = {}
                    actions.append(f"created tab folder '{want}'")
                bar.drag_into_folder(eid, want)
            structure[have].pop(eid, None)
            structure[want][eid] = name
            current[eid] = want
            actions.append(f"{name}: '{have or '/'}' -> '{want or '/'}'")

    tab_folders = {name: folder for folder, members in structure.items() if folder for name in members.values()}
    return actions, tab_folders
