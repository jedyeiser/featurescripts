"""Create the throwaway test drawing "Template test 4101 (demo)" in "Publish & Drawing tools" (doc 73271cfc)
FROM the PART A3 master template and add a 1:5 top view of the "4101 PLAN" composite (wires on) in the VIEW zone.
Only the tab with exactly this name is (re)created; the demo drawings are not touched.
usage: PYTHONPATH=. python devtools/onshape/templates/make_test_drawing.py"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import PD, PPS, PW, TD, TW, c, elements, modify  # noqa: E402

NAME = "Template test 4101 (demo)"
tmpl = json.loads((Path(__file__).parent / "part_a3_layout.json").read_text())["dwt"]   # API needs the .dwt blob
for e in elements(PD, PW):
    if e["name"] == NAME:
        c._request("DELETE", f"/api/v6/elements/d/{PD}/w/{PW}/e/{e['id']}")
eid = c.post(f"/api/v6/drawings/d/{PD}/w/{PW}/create", json_data={
    "drawingName": NAME, "templateDocumentId": TD, "templateWorkspaceId": TW, "templateElementId": tmpl})["id"]
print("drawing", eid)
time.sleep(5)
pid = {p["name"]: p["partId"] for p in c.get(f"/api/v10/parts/d/{PD}/w/{PW}/e/{PPS}",
                                             query_params={"includeWireBodies": "true"})}
s = modify(PD, PW, eid, [{"messageName": "onshapeCreateViews", "formatVersion": "2021-01-01", "views": [{
    "viewType": "TopLevel", "position": {"x": 215, "y": 218}, "orientation": "top", "includeWires": True,
    "scale": {"scaleSource": "Custom", "numerator": 1, "denumerator": 5},
    "reference": {"elementId": PPS, "idTag": pid["4101 PLAN"]}}]}], "4101 PLAN view")
print(s.get("output")[:400])
(Path(__file__).parent / "test_drawing.json").write_text(json.dumps({"eid": eid, "name": NAME}))
