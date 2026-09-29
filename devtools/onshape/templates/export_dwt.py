"""Export the PART A3 master drawing tab as a .dwt blob tab in the template document (the drawings API
createDrawingAppElement takes a BLOB element as template; Onshape's own dialog can use the drawing tab).
Creates a NEW tab named '<tab>.dwt'; refuses if it already exists unless --replace (then only that .dwt).
usage: PYTHONPATH=. python devtools/onshape/templates/export_dwt.py [--replace]"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, c, elements  # noqa: E402

lay = json.loads((Path(__file__).parent / "part_a3_layout.json").read_text())
name = lay["tab"] + ".dwt"
old = [e for e in elements(TD, TW) if e["name"] == name]
if old and "--replace" not in sys.argv:
    raise SystemExit(f"'{name}' exists; pass --replace")
for e in old:
    c._request("DELETE", f"/api/v6/elements/d/{TD}/w/{TW}/e/{e['id']}")
r = c.post(f"/api/v10/drawings/d/{TD}/w/{TW}/e/{lay['eid']}/translations",
           json_data={"formatName": "DWT", "storeInDocument": True, "destinationName": name})
for _ in range(120):
    s = c.get(f"/api/v10/translations/{r['id']}")
    if s["requestState"] != "ACTIVE":
        break
    time.sleep(2)
print(s["requestState"], s.get("resultElementIds"), s.get("failureReason"))
lay["dwt"] = (s.get("resultElementIds") or [None])[0]
(Path(__file__).parent / "part_a3_layout.json").write_text(json.dumps(lay, indent=1))
