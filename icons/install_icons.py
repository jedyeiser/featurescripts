"""Upload the chosen icons into their documents and wire them into the feature files (local edits only).

For each target in icon_targets.py:
  1. prepare the wired file IN MEMORY: `IconNamespace::import(path : "<tab id>", version : "<microversion>");`
     after the file's imports and `"Icon" : IconNamespace::BLOB_DATA` in the feature annotation (skipped if the
     file already has an icon);
  2. only then upload icons/final/<icon>_icon.svg as a tab of the feature's document (skipped if a tab of that
     name exists) and write the file -- as UTF-8 with its original line endings, so a failure can neither leave
     an unused SVG tab behind nor truncate the file (2026-09-25 review, crosscut.md P0-1).
The document comes from the project's .document.json or, when a project has none (gordonSurface), from the
file's entry in .sync-state.json. Targets may use subfolder paths ("features/xSect").
Pushing is done separately (pushproject --check or devtools/onshape/repin.py push) so each document is checked.

usage (repo root): PYTHONPATH=. python icons/install_icons.py [--dry-run]
"""
import json
import os
import re
import sys

import requests

sys.path.insert(0, os.path.dirname(__file__))
from icon_targets import TARGETS  # noqa: E402
from sync.core.client import OnshapeClient  # noqa: E402

c = OnshapeClient()
AUTH = (c.auth.access_key, c.auth.secret_key)
BASE = c.auth.base_url
DRY = "--dry-run" in sys.argv


def document_of(proj, path):
    """(document id, workspace id) for a project file."""
    dj = os.path.join(proj, ".document.json")
    if os.path.exists(dj):
        d = json.load(open(dj))
        return d["document_id"], d["workspace_id"]
    state = json.load(open(".sync-state.json", encoding="utf-8"))
    files = state.get("files", state)
    entry = files.get(path.replace("/", "\\")) or files.get(path)
    if not entry or not entry.get("document_id"):
        raise SystemExit("no .document.json and no .sync-state.json entry for %s" % path)
    return entry["document_id"], entry["workspace_id"]


def find_tab(did, wid, filename):
    tabs = {e["name"]: e for e in c.list_elements(did, wid)}
    return tabs.get(filename)


def upload_tab(did, wid, filename):
    with open(os.path.join("icons", "final", filename), "rb") as fh:
        r = requests.post(f"{BASE}/api/v10/blobelements/d/{did}/w/{wid}", auth=AUTH, timeout=60,
                          files={"file": (filename, fh, "image/svg+xml")},
                          data={"encodedFilename": filename})
    if r.status_code >= 400:
        raise SystemExit("upload of %s failed: %s %s" % (filename, r.status_code, r.text[:300]))
    print("   uploaded", filename)
    return find_tab(did, wid, filename)


def wired_source(src, tab_id, microversion, filename):
    """The file with the icon wired in, or None when it already has one. Raises if it cannot be wired."""
    if "IconNamespace::" in src:
        return None
    eol = "\r\n" if "\r\n" in src else "\n"
    lines = src.replace("\r\n", "\n").split("\n")
    imports = [i for i, l in enumerate(lines) if re.match(r"\s*(export\s+)?import\(path", l)]
    if not imports:
        raise ValueError("no import lines")
    lines.insert(max(imports) + 1, '// IMPORT: %s (feature icon)\nIconNamespace::import(path : "%s", version : "%s");' % (filename, tab_id, microversion))
    text = "\n".join(lines)
    at = text.index('"Feature Type Name"')
    head = text.rindex("annotation", 0, at)
    brace = text.index("{", head)
    text = text[:brace + 1] + ' "Icon" : IconNamespace::BLOB_DATA,' + text[brace + 1:]
    return text.replace("\n", eol)


def main():
    for proj, fs, icon in TARGETS:
        path = os.path.join(proj, fs + ".fs")
        filename = "%s_icon.svg" % icon
        if not os.path.exists(os.path.join("icons", "final", filename)):
            print("%-15s %-25s MISSING icons/final/%s -- skipped" % (proj, fs, filename))
            continue
        src = open(path, "rb").read().decode("utf-8")
        if "IconNamespace::" in src:
            print("%-15s %-25s already has an icon -- left alone" % (proj, fs))
            continue
        did, wid = document_of(proj, path)
        # Dry wiring first (placeholder ids): any failure stops BEFORE anything is uploaded.
        try:
            wired_source(src, "0" * 24, "0" * 24, filename)
        except ValueError as e:
            print("%-15s %-25s cannot wire (%s) -- nothing uploaded" % (proj, fs, e))
            continue
        if any(ord(ch) > 127 for ch in src):
            print("   note: %s has non-ASCII characters (kept as is)" % path)
        if DRY:
            print("%-15s %-25s would upload %s to %s and wire it" % (proj, fs, filename, did))
            continue
        tab = find_tab(did, wid, filename) or upload_tab(did, wid, filename)
        text = wired_source(src, tab["id"], tab["microversionId"], filename)
        open(path, "wb").write(text.encode("utf-8"))
        print("%-15s %-25s icon tab %s  wired" % (proj, fs, tab["id"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
