"""Build the PART A3 drawing template master in "Ski Drawing Templates" (doc 52b5bde0, K2 enterprise).

Creates (or rebuilds) ONLY the tab named TAB below; no other tab is touched. Geometry follows
publish_tools/research/drawing_templates_plan.md sections 4-5 and drawing_style_guide.md.

What the API can do (done here): A3 / ISO / mm / first-angle drawing without Onshape's border and title block;
lines, circles and notes (MText formatting codes: {\\fNoto Sans|b1|i0|c0|p0;...} bold, \\P new line).
What it cannot (done in the UI afterwards, see ui_part_a3_master.py): line weights (format layers), property-
linked notes, the logo image, drawing properties, the revision table, locking the format layers.

usage (repo root): PYTHONPATH=. python devtools/onshape/templates/build_part_a3_master.py [--rebuild] [--brand K2] [--size A3|A4|A1]
       (phase 1: value notes only) -> ui_part_a3_master.py links them -> ... --phase rest (phase 2)
Prints the element id and writes the layer assignment to templates/part_a3_layout.json.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, c, elements, modify, pt  # noqa: E402

BRANDS = {"K2": {"tab": "K2 SKIS - PART %s (template)", "dept": "SKI ENGINEERING"}}
BRAND = sys.argv[sys.argv.index("--brand") + 1] if "--brand" in sys.argv else "K2"
TAB = BRANDS[BRAND]["tab"] % (sys.argv[sys.argv.index("--size") + 1] if "--size" in sys.argv else "A3")

FONT = "Noto Sans"


def txt(s, bold=False):
    return "{\\f%s|b%d|i0|c0|p0;%s}" % (FONT, 1 if bold else 0, s)


# ------------------------------------------------------------------ geometry: devtools/onshape/templates/sheet_layout.py
# (parametric in the sheet size; this script builds the A3 master, --size A4 / A1 for the next masters)
from sheet_layout import sheet_layout  # noqa: E402

SIZE = sys.argv[sys.argv.index("--size") + 1] if "--size" in sys.argv else "A3"
LAYOUT_SPEC = sheet_layout(SIZE, BRANDS[BRAND]["dept"])
L = LAYOUT_SPEC["lines"]          # (layer, x1, y1, x2, y2, alias)
CIRCLES = LAYOUT_SPEC["circles"]  # (layer, x, y, r, alias)
N = [(layer, x, y, txt(s, bold), h, alias) for layer, x, y, s, h, alias, bold in LAYOUT_SPEC["notes"]]
# property-linked value notes: (alias, final x, final ytop, height, bold, wrap width mm, property items).
# Phase "values" creates them alone at staging spots so the UI can link each one without hitting a neighbour;
# phase "rest" moves them into their cells (a position-only onshapeEditAnnotations keeps the property link) and
# adds everything else. The placeholder length fixes the note's wrap width (Onshape keeps the created width).
VALUES = LAYOUT_SPEC["values"]


def staging(k):
    return LAYOUT_SPEC["staging"][k]


def annotations():
    out = []
    for layer, x1, y1, x2, y2, alias in L:
        out.append({"type": "Onshape::Line", "line": {"startPoint": pt(x1, y1), "endPoint": pt(x2, y2),
                                                        "logicalId": alias}})
    for layer, x, y, r, alias in CIRCLES:
        out.append({"type": "Onshape::Circle", "circle": {"center": pt(x, y), "radius": r, "logicalId": alias}})
    for layer, x, y, s, h, alias in N:
        out.append({"type": "Onshape::Note", "note": {"position": pt(x, y), "contents": s, "textHeight": h,
                                                        "logicalId": alias}})
    return out


LAYOUT = Path(__file__).parent / ("part_%s_layout.json" % SIZE.lower())


def phase_values():
    existing = [e for e in elements(TD, TW) if e["name"] == TAB]
    if existing and "--rebuild" not in sys.argv:
        raise SystemExit(f"'{TAB}' exists ({existing[0]['id']}); pass --rebuild to delete and rebuild it")
    for e in existing:
        c._request("DELETE", f"/api/v6/elements/d/{TD}/w/{TW}/e/{e['id']}")
    eid = c.post(f"/api/v6/drawings/d/{TD}/w/{TW}/create", json_data={
        "drawingName": TAB, "border": False, "titleblock": False, "size": SIZE, "units": "MILLIMETER",
        "standard": "ISO", "projection": "First", "decimalSeparator": "PERIOD", "views": "zero"})["id"]
    anns = []
    for k, (alias, _x, _y, h, bold, w, _p) in enumerate(VALUES):
        x, y = staging(k)
        n = max(2, int(w / (0.72 * h)))
        anns.append({"type": "Onshape::Note", "note": {"position": pt(x, y), "contents": txt("X" * n, bold),
                                                         "textHeight": h}})
    s = modify(TD, TW, eid, [{"messageName": "onshapeCreateAnnotations", "formatVersion": "2021-01-01",
                              "annotations": anns}], "PART A3 value notes")
    res = json.loads(s["output"])["results"]
    ids = {v[0]: ["tb", r.get("logicalId")] for v, r in zip(VALUES, res)}
    LAYOUT.write_text(json.dumps({"eid": eid, "tab": TAB, "ids": ids,
                                  "staging": {v[0]: staging(k) for k, v in enumerate(VALUES)}}, indent=1))
    print(TAB, eid, "value notes", len(res))


def phase_rest():
    lay = json.loads(LAYOUT.read_text())
    eid, ids = lay["eid"], lay["ids"]
    moves = [{"type": "Onshape::Note", "note": {"logicalId": ids[a][1], "position": pt(x, y)}}
             for a, x, y, *_r in VALUES]
    s = modify(TD, TW, eid, [{"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01",
                              "annotations": moves}], "PART A3 move values")
    print("moved", s.get("requestState"))
    old = [v[1] for a, v in ids.items() if not a.startswith("v_") and v[1]]
    if old:   # rerun: replace the geometry and static notes of the previous phase-2 run
        modify(TD, TW, eid, [{"messageName": "onshapeDeleteEntities", "formatVersion": "2021-01-01",
                              "entities": old}], "PART A3 clear geometry")
        ids = {a: v for a, v in ids.items() if a.startswith("v_")}
    anns = annotations()
    s = modify(TD, TW, eid, [{"messageName": "onshapeCreateAnnotations", "formatVersion": "2021-01-01",
                              "annotations": anns}], "PART A3 master geometry")
    res = json.loads(s["output"])["results"]
    k = 0
    for layer, *_rest, alias in L:
        ids[alias] = (layer, res[k].get("logicalId")); k += 1
    for layer, *_rest, alias in CIRCLES:
        ids[alias] = (layer, res[k].get("logicalId")); k += 1
    for layer, *_rest, alias in N:
        ids[alias] = (layer, res[k].get("logicalId")); k += 1
    lay.update({"ids": ids, "lines": L, "circles": CIRCLES,
                "notes": [(a, b, cc, e, f) for a, b, cc, _s, e, f in N]})
    LAYOUT.write_text(json.dumps(lay, indent=1))
    print("geometry", len(res), "failed", len([r for r in res if r.get("status") != "OK"]))


if __name__ == "__main__":
    if "--phase" in sys.argv and sys.argv[sys.argv.index("--phase") + 1] == "rest":
        phase_rest()
    else:
        phase_values()
