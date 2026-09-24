"""Upload the chosen icons into their documents and wire them into the feature files (local edits only).

For each target in icon_targets.py:
  1. upload icons/final/<icon>_icon.svg as a tab of the feature's document (skipped if a tab of that name exists);
  2. add `IconNamespace::import(path : "<tab id>", version : "<microversion>");` after the file's imports and
     `"Icon" : IconNamespace::BLOB_DATA` to the feature annotation (skipped if the file already has an icon).
Pushing is done separately (pushproject --check) so each document is compiled and checked.

usage (repo root): PYTHONPATH=. python icons/install_icons.py
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


def svg_tab(did, wid, filename):
    """Element id and microversion of the SVG tab `filename`, uploading it first if needed."""
    tabs = {e["name"]: e for e in c.list_elements(did, wid)}
    if filename not in tabs:
        with open(os.path.join("icons", "final", filename), "rb") as fh:
            r = requests.post(f"{BASE}/api/v10/blobelements/d/{did}/w/{wid}", auth=AUTH, timeout=60,
                              files={"file": (filename, fh, "image/svg+xml")},
                              data={"encodedFilename": filename})
        if r.status_code >= 400:
            raise SystemExit("upload of %s failed: %s %s" % (filename, r.status_code, r.text[:300]))
        print("   uploaded", filename)
        tabs = {e["name"]: e for e in c.list_elements(did, wid)}
    e = tabs[filename]
    return e["id"], e["microversionId"]


def wire_icon(path, tab_id, microversion, filename):
    src = open(path, encoding="utf-8").read()
    if "IconNamespace::" in src:
        print("   %s already has an icon -- left alone" % path)
        return False
    lines = src.split("\n")
    last_import = max(i for i, l in enumerate(lines) if re.match(r"\s*(export\s+)?import\(path", l))
    lines.insert(last_import + 1, '// IMPORT: %s (feature icon)\nIconNamespace::import(path : "%s", version : "%s");' % (filename, tab_id, microversion))
    src = "\n".join(lines)
    at = src.index('"Feature Type Name"')
    head = src.rindex("annotation", 0, at)
    brace = src.index("{", head)
    src = src[:brace + 1] + ' "Icon" : IconNamespace::BLOB_DATA,' + src[brace + 1:]
    open(path, "w", encoding="ascii", newline="\n").write(src)
    return True


for proj, fs, icon in TARGETS:
    d = json.load(open(os.path.join(proj, ".document.json")))
    filename = "%s_icon.svg" % icon
    tab_id, mv = svg_tab(d["document_id"], d["workspace_id"], filename)
    changed = wire_icon(os.path.join(proj, fs + ".fs"), tab_id, mv, filename)
    print("%-15s %-25s icon tab %s  %s" % (proj, fs, tab_id, "wired" if changed else ""))
