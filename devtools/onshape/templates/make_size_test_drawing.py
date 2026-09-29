"""Create (or rebuild in place) the per-size template test drawing in "Publish & Drawing tools" (doc 73271cfc):
"Template test A4 (demo)" / "Template test A2 (demo)" FROM that size's K2 SKIS .dwt, with a top view of the
"4101 PLAN" composite (wires on) and a front view of the real part "RD 20TAC 28 178 4101" (the sheet reference
needs a part WITH a view; set it afterwards with ui_set_sheet_reference.py). A2 also gets the Export primitive
composite "P1 TAC PRIMITIVE" from "Primitive tests" (was "Primitive tests (agent)") at 1:5 to check that it fits. Only the tab with exactly
this name is touched. The A3 test stays "Template test 4101 (demo)" (make_test_drawing.py).
usage: PYTHONPATH=. python devtools/onshape/templates/make_size_test_drawing.py A4|A2"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import PD, PPS, PW, TD, TW, c, elements, modify  # noqa: E402

SIZE = sys.argv[1]
NAME = "Template test %s (demo)" % SIZE
PRIM_PS, PRIM = "5c3ac8fb8ec70b1f256c0e97", "P1 TAC PRIMITIVE"
st = json.loads((Path(__file__).parent / "template_set.json").read_text())
tmpl = st["templates"]["K2SKIS " + SIZE]["dwt"]
for e in elements(PD, PW):
    if e["name"] == NAME:
        c._request("DELETE", f"/api/v6/elements/d/{PD}/w/{PW}/e/{e['id']}")
eid = c.post(f"/api/v6/drawings/d/{PD}/w/{PW}/create", json_data={
    "drawingName": NAME, "templateDocumentId": TD, "templateWorkspaceId": TW, "templateElementId": tmpl})["id"]
print("drawing", eid)
time.sleep(5)


def pids(ps):
    return {p["name"]: p["partId"] for p in c.get(f"/api/v10/parts/d/{PD}/w/{PW}/e/{ps}",
                                                  query_params={"includeWireBodies": "true", "includeCompositeParts": "true"})}


pid = pids(PPS)
den = 10 if SIZE == "A4" else 5
# (x, y) view centres in sheet mm: open area left of the revision table / above the title block
spots = {"A4": {"plan": (150, 160), "part": (150, 110)},
         "A2": {"plan": (215, 385), "part": (215, 345), "prim": (232, 185)}}[SIZE]
views = [("plan", PPS, pid["4101 PLAN"], "top", True), ("part", PPS, pid["RD 20TAC 28 178 4101"], "front", False)]
if SIZE == "A2":
    views.append(("prim", PRIM_PS, pids(PRIM_PS)[PRIM], "front", True))
for key, ps, part, orient, wires in views:
    x, y = spots[key]
    v = {"viewType": "TopLevel", "position": {"x": x, "y": y}, "orientation": orient,
         "scale": {"scaleSource": "Custom", "numerator": 1, "denumerator": den},
         "reference": {"elementId": ps, "idTag": part}}
    if wires:
        v["includeWires"] = True
    s = modify(PD, PW, eid, [{"messageName": "onshapeCreateViews", "formatVersion": "2021-01-01", "views": [v]}], key)
    print(key, (s.get("output") or "")[:200])
p = Path(__file__).parent / "test_drawing.json"
d = json.loads(p.read_text()) if p.read_text().strip() else {}
d = d if "sizes" in d else {"A3": d, "sizes": True}
d[SIZE] = {"eid": eid, "name": NAME}
p.write_text(json.dumps(d, indent=1))
