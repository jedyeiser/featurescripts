"""Re-pin same-document imports, and push tabs with a verified write.

Tabs in a document pin each other by ELEMENT microversion: import(path : "<element id>", version : "<microversion>").
Pin chains: push the callee, re-pin its callers to the callee's new microversion, push the callers, and so on.
`pushproject` is unreliable on chains (correction 45: a duplicated block after Onshape re-pinned a tab remotely,
and 30 s client timeouts on writes that still land), so `push` here writes the tab directly and re-reads it.
Onshape itself re-pins a written tab's same-document imports to the callees' current microversions; `push`
treats pin-only differences as a match and copies Onshape's pins back into the local file.

usage (repo root):
    PYTHONPATH=. python devtools/onshape/repin.py pin  <project dir> <tab> [<tab> ...]   # re-pin callers of <tab>
    PYTHONPATH=. python devtools/onshape/repin.py push <project dir> <tab> [<tab> ...]   # write + verify <tab>
<tab> is the local file stem (xSect_GJ for xSection/section/xSect_GJ.fs). Element ids come from .sync-state.json.
"""
import glob
import json
import os
import re
import sys

from sync.core.client import OnshapeClient

PIN = re.compile(r'(import\(path : "[0-9a-f]{24}", version : ")[0-9a-f]{24}("\))')


def tabs_of(project):
    """{stem: (local path, element id)} for the project's files, from .sync-state.json."""
    state = json.load(open(".sync-state.json", encoding="utf-8"))
    files = state.get("files", state)
    out = {}
    for key, entry in files.items():
        path = key.replace("\\", "/")
        if isinstance(entry, dict) and path.startswith(project + "/") and path.endswith(".fs") and entry.get("element_id"):
            out[os.path.splitext(os.path.basename(path))[0]] = (path, entry["element_id"])
    return out


def lf(text):
    return text.replace("\r\n", "\n").strip()


def unpinned(text):
    return PIN.sub(r"\1\2", lf(text))


def main():
    mode, project, names = sys.argv[1], sys.argv[2], sys.argv[3:]
    doc = json.load(open("%s/.document.json" % project))
    did, wid = doc["document_id"], doc["workspace_id"]
    client = OnshapeClient()
    tabs = tabs_of(project)
    if mode == "pin":
        elements = {e["id"]: e for e in client.list_elements(did, wid)}
        for name in names:
            eid = tabs[name][1]
            mv = elements[eid]["microversionId"]
            pattern = re.compile(r'(import\(path : "%s", version : ")([0-9a-f]{24})("\))' % eid)
            for path in glob.glob("%s/**/*.fs" % project, recursive=True):
                text = open(path, "rb").read().decode("utf-8")
                new, count = pattern.subn(lambda m: m.group(1) + mv + m.group(3), text)
                if count and new != text:
                    open(path, "wb").write(new.encode("utf-8"))
                    print("%-45s %s -> %s" % (path, name, mv))
    elif mode == "push":
        failed = 0
        for name in names:
            path, eid = tabs[name]
            local = open(path, "rb").read().decode("utf-8")
            ok = False
            for _ in range(3):
                try:
                    client.update_featurestudio_contents(did, wid, eid, local)
                except Exception:  # client timeouts: the write often lands anyway; verify below
                    pass
                remote = client.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, eid))["contents"]
                if unpinned(remote) == unpinned(local):
                    ok = True
                    if lf(remote) != lf(local):
                        # Take Onshape's pins back so the next push does not undo them.
                        text = remote.replace("\r\n", "\n")
                        if "\r\n" in local:
                            text = text.replace("\n", "\r\n")
                        open(path, "wb").write(text.encode("utf-8"))
                        print("%-20s pins updated from Onshape" % name)
                    break
            print("%-20s %s" % (name, "MATCH" if ok else "STILL DIFFERS"))
            failed += not ok
        return 1 if failed else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
