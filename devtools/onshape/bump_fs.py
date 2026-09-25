"""Bump every Feature Studio of one Onshape document to a FeatureScript version, with a regression check.

Steps: fingerprint every Part Studio that uses custom features (devtools/onshape/fingerprint.py), rewrite each
tab's `FeatureScript N;` header and `onshape/std/...` import versions, write it directly and verify it (Onshape
re-pins same-document imports on write, which is expected), regenerate and compare the fingerprints. If any
studio's fingerprint changes or a tab fails to write, every tab is restored to its original contents.

usage (repo root, Git Bash):
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/bump_fs.py <document id> [--to 3083] [--keep]
--keep leaves the bumped tabs in place even when a fingerprint changes (for reviewing an intended change).
Originals are saved to devtools/onshape/fingerprints/bump_<did>_originals.json.
"""
import argparse
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(__file__))
from fingerprint import fingerprint  # noqa: E402
from sync.core.client import OnshapeClient  # noqa: E402

STORE = os.path.join(os.path.dirname(__file__), "fingerprints")
HEADER = re.compile(r"^FeatureScript \d+;", re.M)
STD = re.compile(r'(import\(path : "onshape/std/[A-Za-z0-9_./]+", version : ")\d+\.0("\))')
PIN = re.compile(r'(import\(path : "[0-9a-f]{24}", version : ")[0-9a-f]{24}("\))')


def unpinned(text):
    return PIN.sub(r"\1\2", text.replace("\r\n", "\n").strip())


def retry(fn, tries=4):
    for k in range(tries):
        try:
            return fn()
        except Exception:
            if k == tries - 1:
                raise
            time.sleep(10)


def studios_with_custom_features(c, did, wid):
    out = []
    for e in c.list_elements(did, wid):
        if e["elementType"] != "PARTSTUDIO":
            continue
        feats = retry(lambda: c.get("/api/v10/partstudios/d/%s/w/%s/e/%s/features" % (did, wid, e["id"])))["features"]
        if any(x.get("namespace", "")[:1] in ("e", "d") for x in feats):
            out.append((e["name"], e["id"]))
    return out


def write_verified(c, did, wid, eid, text):
    for _ in range(3):
        try:
            c.update_featurestudio_contents(did, wid, eid, text)
        except Exception:
            pass
        remote = retry(lambda: c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, eid)))["contents"]
        if unpinned(remote) == unpinned(text):
            return True
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("did")
    ap.add_argument("--to", default="3083")
    ap.add_argument("--keep", action="store_true")
    a = ap.parse_args()
    c = OnshapeClient()
    did = a.did
    wid = c.get("/api/v10/documents/%s" % did)["defaultWorkspace"]["id"]
    name = c.get("/api/v10/documents/%s" % did)["name"]
    print("== %s (%s) -> FeatureScript %s" % (name, did, a.to))

    tabs = [e for e in c.list_elements(did, wid) if e["elementType"] == "FEATURESTUDIO"]
    originals = {}
    for e in tabs:
        originals[e["id"]] = (e["name"], retry(lambda: c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, e["id"])))["contents"])
    todo = {eid: (n, src) for eid, (n, src) in originals.items() if HEADER.search(src) and HEADER.search(src).group(0) != "FeatureScript %s;" % a.to}
    print("tabs: %d, to bump: %d" % (len(tabs), len(todo)))
    if not todo:
        return 0
    json.dump({k: v for k, v in originals.items()}, open(os.path.join(STORE, "bump_%s_originals.json" % did), "w"))

    studios = studios_with_custom_features(c, did, wid)
    before = {}
    for sname, eid in studios:
        before[sname] = retry(lambda: fingerprint(did, wid, eid, None))
    print("fingerprinted studios: %s" % ", ".join(s for s, _ in studios) or "(none)")

    failed = []
    for eid, (tname, src) in todo.items():
        new = HEADER.sub("FeatureScript %s;" % a.to, src, count=1)
        new = STD.sub(lambda m: m.group(1) + a.to + ".0" + m.group(2), new)
        if not write_verified(c, did, wid, eid, new):
            failed.append(tname)
    print("written: %d, failed writes: %s" % (len(todo) - len(failed), failed or "none"))

    changed = []
    for sname, eid in studios:
        after = retry(lambda: fingerprint(did, wid, eid, None))
        if after != before[sname]:
            changed.append(sname)
            print("--- %s CHANGED" % sname)
            b, f = before[sname], after
            for line in sorted(set(b) ^ set(f))[:12]:
                print("   %s %s" % ("-" if line in b else "+", line[:150]))
        else:
            print("    %s identical" % sname)

    if (failed or changed) and not a.keep:
        print("RESTORING originals")
        for eid in todo:
            write_verified(c, did, wid, eid, originals[eid][1])
        return 1
    print("DONE: %s is on FeatureScript %s" % (name, a.to))
    return 0


if __name__ == "__main__":
    sys.exit(main())
