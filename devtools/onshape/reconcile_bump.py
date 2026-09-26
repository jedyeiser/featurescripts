"""After bump_fs.py moved a document's tabs to a new FeatureScript version, bring the local files in line.

For every local .fs of the project (element ids from .sync-state.json):
  - local == remote apart from the version header / std import versions / same-document pins
        -> "take": the local file is overwritten with Onshape's contents (it was only behind on the bump);
  - otherwise the local file carries unpushed edits
        -> "bump": its header and std import versions are raised to the remote version, ready to push.
Prints what it did per file; pushing the "bump" files is left to devtools/onshape/repin.py push.

usage (repo root): PYTHONPATH=. python devtools/onshape/reconcile_bump.py <project dir> [--dry-run]
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))
from repin import tabs_of  # noqa: E402
from sync.core.client import OnshapeClient

HEADER = re.compile(r"^FeatureScript (\d+);", re.M)
STD = re.compile(r'(import\(path : "onshape/std/[A-Za-z0-9_./]+", version : ")\d+\.0("\))')
PIN = re.compile(r'(import\(path : "[0-9a-f]{24}", version : ")[0-9a-f]{24}("\))')


def neutral(text):
    text = text.replace("\r\n", "\n").strip()
    text = HEADER.sub("FeatureScript N;", text, count=1)
    text = STD.sub(r"\1N\2", text)
    return PIN.sub(r"\1\2", text)


def main():
    project = sys.argv[1]
    dry = "--dry-run" in sys.argv
    doc = json.load(open("%s/.document.json" % project)) if os.path.exists("%s/.document.json" % project) else None
    c = OnshapeClient()
    for name, (path, eid) in sorted(tabs_of(project).items()):
        state = json.load(open(".sync-state.json", encoding="utf-8"))["files"][path.replace("/", "\\")]
        did = state["document_id"]
        wid = state.get("workspace_id") or (doc or {}).get("workspace_id")
        try:
            remote = c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, eid))["contents"]
        except Exception as e:
            print("gone  %-28s (%s -- stale .sync-state entry, local file left alone)" % (name, " ".join(str(e).split())[:40]))
            continue
        if not os.path.exists(path):
            print("nofile %-27s (no local file)" % name)
            continue
        local = open(path, "rb").read().decode("utf-8")
        version = HEADER.search(remote).group(1)
        crlf = "\r\n" in local
        if neutral(local) == neutral(remote):
            action = "take" if local.replace("\r\n", "\n").strip() != remote.replace("\r\n", "\n").strip() else "same"
            if action == "take" and not dry:
                text = remote.replace("\r\n", "\n")
                open(path, "wb").write((text.replace("\n", "\r\n") if crlf else text).encode("utf-8"))
        else:
            action = "bump"
            if not dry:
                text = HEADER.sub("FeatureScript %s;" % version, local, count=1)
                text = STD.sub(lambda m: m.group(1) + version + ".0" + m.group(2), text)
                open(path, "wb").write(text.encode("utf-8"))
        print("%-5s %-28s (remote FS %s)" % (action, name, version))
    return 0


if __name__ == "__main__":
    sys.exit(main())
