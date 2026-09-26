"""repin_library.py with a regression check: fingerprint every studio of each importer before and after.

usage (repo root):
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python devtools/onshape/repin_library_checked.py <library did> <version id> <importer did> [...]
Reports per studio: identical / within noise / CHANGED (with the differing lines). Does not revert.
"""
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(__file__))
from bump_fs import retry, same_within_noise, studios_with_custom_features  # noqa: E402
from fingerprint import fingerprint  # noqa: E402
from sync.core.client import OnshapeClient  # noqa: E402


def main():
    library, version, importers = sys.argv[1], sys.argv[2], sys.argv[3:]
    c = OnshapeClient()
    for did in importers:
        info = c.get("/api/v10/documents/%s" % did)
        wid = info["defaultWorkspace"]["id"]
        studios = studios_with_custom_features(c, did, wid)
        before = {name: retry(lambda: fingerprint(did, wid, eid, None)) for name, eid in studios}
        r = subprocess.run([sys.executable, os.path.join(os.path.dirname(__file__), "repin_library.py"), library, version, did],
                           capture_output=True, text=True)
        print("== %s\n%s" % (info["name"], (r.stdout + r.stderr).strip() or "   (no imports of this library)"))
        for name, eid in studios:
            after = retry(lambda: fingerprint(did, wid, eid, None))
            if after == before[name]:
                print("    %s identical" % name)
            elif same_within_noise(before[name], after):
                print("    %s within noise" % name)
            else:
                print("--- %s CHANGED" % name)
                for line in sorted(set(before[name]) ^ set(after))[:12]:
                    print("   %s %s" % ("-" if line in before[name] else "+", line[:150]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
