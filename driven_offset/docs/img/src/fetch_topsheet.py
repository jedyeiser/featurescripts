"""Fetch mid-surface sections of the topsheet (read-only FS eval API) into data/topsheet_sections.txt.
usage (repo root): FS_SYNC_TIMEOUT=240 PYTHONPATH=. python driven_offset/docs/img/src/fetch_topsheet.py [step_mm] [chunk]
"""
import os, sys, time
from sync.core.client import OnshapeClient

HERE = os.path.dirname(os.path.abspath(__file__))
D, W, E = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef", "681a5825e353d376c88224eb"  # Unwrap_Testing Copy 2
STEP = float(sys.argv[1]) if len(sys.argv) > 1 else 4.0
CHUNK = int(sys.argv[2]) if len(sys.argv) > 2 else 60
TOTAL = int(1830 / STEP) + 2


def strings(o, found):
    if isinstance(o, dict):
        if isinstance(o.get("value"), str) and "String" in o.get("btType", ""):
            found.append(o["value"])
        for v in o.values():
            strings(v, found)
    elif isinstance(o, list):
        for v in o:
            strings(v, found)
    return found


def run(script):
    c = OnshapeClient()
    for attempt in range(3):
        try:
            r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
            for n in r.get("notices") or []:
                print("  [%s] %s" % (n.get("level"), n.get("message")[:200]))
            return strings(r.get("result"), [])
        except Exception as ex:
            print("  retry:", str(ex)[:200])
            time.sleep(5)
    raise SystemExit("eval failed")


if __name__ == "__main__":
    tmpl = open(os.path.join(HERE, "topsheet_sections.fs.tmpl")).read()
    lines = []
    for i0 in range(0, TOTAL, CHUNK):
        t0 = time.time()
        got = run(tmpl.replace("__I0__", str(i0)).replace("__I1__", str(i0 + CHUNK)).replace("__STEP__", repr(STEP)))
        print("stations %d..%d: %d lines, %.1f s" % (i0, i0 + CHUNK, len(got), time.time() - t0))
        lines += got if i0 == 0 else [x for x in got if not x.startswith("T ")]
    os.makedirs(os.path.join(HERE, "data"), exist_ok=True)
    open(os.path.join(HERE, "data", "topsheet_sections.txt"), "w").write("\n".join(lines) + "\n")
