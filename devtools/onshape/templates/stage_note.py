"""Move one PART A3 value note to a free staging spot (so the UI can open it without hitting a neighbour) or
back to its cell.  usage: PYTHONPATH=. python devtools/onshape/templates/stage_note.py <alias> out|back"""
import importlib.util
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, modify, pt  # noqa: E402

spec = importlib.util.spec_from_file_location("bpm", str(Path(__file__).parent / "build_part_a3_master.py"))
bpm = importlib.util.module_from_spec(spec); spec.loader.exec_module(bpm)
lay = json.loads((Path(__file__).parent / "part_a3_layout.json").read_text())
alias, where = sys.argv[1], sys.argv[2]
x, y = (100.0, 250.0) if where == "out" else next((v[1], v[2]) for v in bpm.VALUES if v[0] == alias)
s = modify(TD, TW, lay["eid"], [{"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01",
                                 "annotations": [{"type": "Onshape::Note", "note": {"logicalId": lay["ids"][alias][1],
                                                                                    "position": pt(x, y)}}]}], "stage " + where)
print(s.get("output"))
