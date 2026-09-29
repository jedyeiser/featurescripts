"""Push position changes made in build_part_a3_master.py (VALUES final positions, projection symbol, units note)
to the existing template without rebuilding (UI-made links, layers, logo and revision table stay).
usage: PYTHONPATH=. python devtools/onshape/templates/tweak_part_a3.py"""
import importlib.util
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, modify, pt  # noqa: E402

spec = importlib.util.spec_from_file_location("bpm", str(Path(__file__).parent / "build_part_a3_master.py"))
bpm = importlib.util.module_from_spec(spec); spec.loader.exec_module(bpm)
lay = json.loads((Path(__file__).parent / "part_a3_layout.json").read_text())
ids = lay["ids"]
anns = [{"type": "Onshape::Note", "note": {"logicalId": ids[a][1], "position": pt(x, y)}} for a, x, y, *_r in bpm.VALUES]
for layer, x1, y1, x2, y2, alias in bpm.L:
    if alias.startswith("pj_"):
        anns.append({"type": "Onshape::Line", "line": {"logicalId": ids[alias][1], "startPoint": pt(x1, y1), "endPoint": pt(x2, y2)}})
for layer, x, y, r, alias in bpm.CIRCLES:
    anns.append({"type": "Onshape::Circle", "circle": {"logicalId": ids[alias][1], "center": pt(x, y), "radius": r}})
for layer, x, y, s, h, alias in bpm.N:
    if alias == "st_units":
        anns.append({"type": "Onshape::Note", "note": {"logicalId": ids[alias][1], "position": pt(x, y)}})
s = modify(TD, TW, lay["eid"], [{"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01",
                                 "annotations": anns}], "PART A3 tweak")
print(s.get("output")[:600])
