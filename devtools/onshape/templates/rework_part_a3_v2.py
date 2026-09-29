"""v2 rework of the existing PART A3 master (user review 2026-09-29) through the drawings API, no rebuild:
- delete the full-width view-zone line (z_view);
- shorten the two other zone lines to the title block's own top / left edges (z_band -> tb_top, z_split ->
  tb_left; editing keeps their Border zones layer, so no UI layer move is needed);
- move the value notes to the sheet_layout.py positions (VAL_DY: baselines >= 1.7 mm above the cell lines) and
  every static note to its layout position.
UI-made property links, layers, logo and the revision table stay. Updates part_a3_layout.json ids.
usage (repo root): PYTHONPATH=. python devtools/onshape/templates/rework_part_a3_v2.py"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, modify, pt  # noqa: E402
from sheet_layout import sheet_layout  # noqa: E402

P = Path(__file__).parent / "part_a3_layout.json"
lay = json.loads(P.read_text())
ids = lay["ids"]
spec = sheet_layout("A3")
geo = {l[5]: l for l in spec["lines"]}
RENAME = {"z_band": "tb_top", "z_split": "tb_left"}

reqs = []
if "z_view" in ids:
    reqs.append({"messageName": "onshapeDeleteEntities", "formatVersion": "2021-01-01", "entities": [ids["z_view"][1]]})
anns = []
for old, new in RENAME.items():
    if old in ids:
        ids[new] = ids.pop(old)
for alias in ("tb_top", "tb_left"):
    _layer, x1, y1, x2, y2, _a = geo[alias]
    anns.append({"type": "Onshape::Line", "line": {"logicalId": ids[alias][1], "startPoint": pt(x1, y1),
                                                    "endPoint": pt(x2, y2)}})
for alias, x, y, *_r in spec["values"]:
    anns.append({"type": "Onshape::Note", "note": {"logicalId": ids[alias][1], "position": pt(x, y)}})
for _layer, x, y, _s, _h, alias, _b in spec["notes"]:
    anns.append({"type": "Onshape::Note", "note": {"logicalId": ids[alias][1], "position": pt(x, y)}})
reqs.append({"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01", "annotations": anns})
for r in reqs:
    s = modify(TD, TW, lay["eid"], [r], "A3 v2 " + r["messageName"][7:])
    print(r["messageName"], s.get("requestState"), (s.get("output") or "")[:300])
ids.pop("z_view", None)
lay["ids"] = ids
lay["lines"] = spec["lines"]
lay["circles"] = spec["circles"]
lay["notes"] = [(a, b, c, e, f) for a, b, c, _s, e, f, _bold in spec["notes"]]
lay["values"] = [v[:4] for v in spec["values"]]
lay["revision_table"] = spec["revision_table"]
P.write_text(json.dumps(lay, indent=1))
