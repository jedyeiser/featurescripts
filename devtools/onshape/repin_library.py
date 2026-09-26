"""Re-point every cross-document import of one library document to a new version of it.

A tab imports another document's tab as
    import(path : "<library did>/<version id>/<element id>", version : "<element microversion in that version>");
After the user versions a library, this rewrites those imports in the given importer documents to the new
version (and each element's microversion inside it), writes the tabs directly and verifies them
(devtools/onshape/repin.py semantics). The importers then need versioning themselves.

usage (repo root):
    PYTHONPATH=. python devtools/onshape/repin_library.py <library did> <version id> <importer did> [<importer did> ...] [--dry-run]
    --skip-element <eid>   leave imports of that element alone (e.g. Variable_Tools extract_outputs, frozen at V1)
"""
import argparse
import re
import sys
import time

from sync.core.client import OnshapeClient

PIN = re.compile(r'(import\(path : "[0-9a-f]{24}", version : ")[0-9a-f]{24}("\))')


def unpinned(text):
    return PIN.sub(r"\1\2", text.replace("\r\n", "\n").strip())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("library")
    ap.add_argument("version")
    ap.add_argument("importers", nargs="+")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--skip-element", action="append", default=[])
    a = ap.parse_args()
    c = OnshapeClient()

    elements = {e["id"]: e for e in c.get("/api/v10/documents/d/%s/v/%s/elements" % (a.library, a.version))}
    pattern = re.compile(r'import\(path : "%s/([0-9a-f]{24})/([0-9a-f]{24})", version : "([0-9a-f]{24})"\)' % a.library)

    failed = 0
    for did in a.importers:
        info = c.get("/api/v10/documents/%s" % did)
        wid = info["defaultWorkspace"]["id"]
        for e in c.list_elements(did, wid):
            if e["elementType"] != "FEATURESTUDIO":
                continue
            src = c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, e["id"]))["contents"]
            changes = []

            def swap(m):
                eid = m.group(2)
                if eid in a.skip_element or eid not in elements:
                    return m.group(0)
                new = 'import(path : "%s/%s/%s", version : "%s")' % (a.library, a.version, eid, elements[eid]["microversionId"])
                if new != m.group(0):
                    changes.append(elements[eid]["name"])
                return new

            new = pattern.sub(swap, src)
            if not changes:
                continue
            print("%-28s %-26s -> %s" % (info["name"][:28], e["name"][:26], ", ".join(sorted(set(changes)))))
            if a.dry_run:
                continue
            ok = False
            for _ in range(3):
                try:
                    c.update_featurestudio_contents(did, wid, e["id"], new)
                except Exception:
                    time.sleep(10)
                remote = c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (did, wid, e["id"]))["contents"]
                if unpinned(remote) == unpinned(new):
                    ok = True
                    break
            if not ok:
                print("   WRITE NOT VERIFIED")
                failed += 1
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
