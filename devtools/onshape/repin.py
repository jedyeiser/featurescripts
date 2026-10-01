"""Re-pin same-document imports, and push tabs with a verified write.

Tabs in a document pin each other by ELEMENT microversion: import(path : "<element id>", version : "<microversion>").
Pin chains: push the callee, re-pin its callers to the callee's new microversion, push the callers, and so on.
`pushproject` is unreliable on chains (correction 45: a duplicated block after Onshape re-pinned a tab remotely,
and 30 s client timeouts on writes that still land), so `push` here writes the tab directly and re-reads it.
Cross-document pins: the newer of the local and the live tab's pin wins (the user moves them while versioning).
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
import time

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


XPIN = re.compile(r'import\(path : "([0-9a-f]{24})/([0-9a-f]{24})/([0-9a-f]{24})", version : "[0-9a-f]{24}"\)')
_VERSION_TIMES = {}


def _created(client, did, vid):
    """createdAt of version vid of document did (ISO string), or None when unknown."""
    if did not in _VERSION_TIMES:
        _VERSION_TIMES[did] = {v["id"]: v["createdAt"] for v in client.get("/api/v10/documents/d/%s/versions" % did)}
    return _VERSION_TIMES[did].get(vid)


def adopt_remote_pins(client, local, remote):
    """Each cross-document import in `local` keeps whichever of its own pin and the live tab's pin is the NEWER
    library version (matched by element id).

    The user moves cross-document pins while versioning, so a stale local file must not push an old version back
    (2026-09-26); a deliberate local bump to a newer version must survive (correction 49: the first version of
    this took the live pin unconditionally and reverted a V7 -> V9 bump)."""
    live = {m.group(3): m for m in XPIN.finditer(remote)}

    def pick(m):
        r = live.get(m.group(3))
        if r is None or r.group(0) == m.group(0) or r.group(1) != m.group(1):
            return m.group(0)
        mine, theirs = _created(client, m.group(1), m.group(2)), _created(client, r.group(1), r.group(2))
        if mine is None or theirs is None:
            print("   WARNING: version of %s unknown; kept the local pin" % m.group(3))
            return m.group(0)
        return r.group(0) if theirs > mine else m.group(0)
    return XPIN.sub(pick, local)


def main():
    mode, project, names = sys.argv[1], sys.argv[2], sys.argv[3:]
    if os.path.exists("%s/.document.json" % project):
        doc = json.load(open("%s/.document.json" % project))
        did, wid = doc["document_id"], doc["workspace_id"]
    else:
        # Projects without .document.json (gordonSurface): the document of the project's files in .sync-state.json.
        files = json.load(open(".sync-state.json", encoding="utf-8"))["files"]
        entry = [e for k, e in files.items() if k.replace("\\", "/").startswith(project + "/") and isinstance(e, dict) and e.get("workspace_id")][0]
        did, wid = entry["document_id"], entry["workspace_id"]
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
            before = client.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, eid))["contents"]
            adopted = adopt_remote_pins(client, local, before)
            if adopted != local:
                local = adopted
                open(path, "wb").write(local.encode("utf-8"))
                print("%-20s cross-document pins taken from Onshape" % name)
            ok = False
            remote = before
            if unpinned(before) == unpinned(local):
                ok = True   # already current (API budget: no write, no verify GET)
            timed_out = False
            for attempt in range(0 if ok else 4):
                if attempt == 0 or not timed_out:
                    try:
                        client.update_featurestudio_contents(did, wid, eid, local)
                        timed_out = False
                    except Exception:  # client timeout: the write usually lands anyway; wait and verify, do NOT re-post
                        timed_out = True
                else:
                    time.sleep(10)
                remote = client.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, eid))["contents"]
                if unpinned(remote) == unpinned(local):
                    ok = True
                    break
                if attempt >= 2:
                    timed_out = False   # it never landed: post again
            if ok and lf(remote) != lf(local):
                # Take Onshape's pins back so the next push does not undo them.
                text = remote.replace("\r\n", "\n")
                if "\r\n" in local:
                    text = text.replace("\n", "\r\n")
                open(path, "wb").write(text.encode("utf-8"))
                print("%-20s pins updated from Onshape" % name)
            print("%-20s %s" % (name, "MATCH" if ok else "STILL DIFFERS"))
            failed += not ok
        return 1 if failed else 0
    return 0


if __name__ == "__main__":
    sys.exit(main())
