"""Temporarily move the two zone lines that bound the title block (y=50 band line, x=230 split line) out of the
way so a window selection of the title block can start on empty paper; 'restore' puts them back.
usage: PYTHONPATH=. python devtools/onshape/templates/move_zone_lines.py away|restore"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, modify, pt  # noqa: E402

lay = json.loads((Path(__file__).parent / "part_a3_layout.json").read_text())
away = sys.argv[1] == "away"
geo = {"z_band": ((20, 60), (225, 60)) if away else ((20, 50), (410, 50)),
       "z_split": ((220, 60), (220, 150)) if away else ((230, 10), (230, 150))}
anns = [{"type": "Onshape::Line", "line": {"logicalId": lay["ids"][a][1], "startPoint": pt(*s), "endPoint": pt(*e)}}
        for a, (s, e) in geo.items()]
s = modify(TD, TW, lay["eid"], [{"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01",
                                 "annotations": anns}], "zone lines " + sys.argv[1])
print(s.get("output"))
