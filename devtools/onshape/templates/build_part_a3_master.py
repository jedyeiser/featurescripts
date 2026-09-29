"""Build the PART A3 drawing template master in "Ski Drawing Templates" (doc 52b5bde0, K2 enterprise).

Creates (or rebuilds) ONLY the tab named TAB below; no other tab is touched. Geometry follows
publish_tools/research/drawing_templates_plan.md sections 4-5 and drawing_style_guide.md.

What the API can do (done here): A3 / ISO / mm / first-angle drawing without Onshape's border and title block;
lines, circles and notes (MText formatting codes: {\\fNoto Sans|b1|i0|c0|p0;...} bold, \\P new line).
What it cannot (done in the UI afterwards, see ui_part_a3_master.py): line weights (format layers), property-
linked notes, the logo image, drawing properties, the revision table, locking the format layers.

usage (repo root): PYTHONPATH=. python devtools/onshape/templates/build_part_a3_master.py [--rebuild] [--brand K2]
       (phase 1: value notes only) -> ui_part_a3_master.py links them -> ... --phase rest (phase 2)
Prints the element id and writes the layer assignment to templates/part_a3_layout.json.
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, c, elements, modify, pt  # noqa: E402

BRANDS = {"K2": {"tab": "K2 SKIS - PART A3 (template)", "dept": "SKI ENGINEERING"}}
BRAND = sys.argv[sys.argv.index("--brand") + 1] if "--brand" in sys.argv else "K2"
TAB = BRANDS[BRAND]["tab"]

FONT = "Noto Sans"


def txt(s, bold=False):
    return "{\\f%s|b%d|i0|c0|p0;%s}" % (FONT, 1 if bold else 0, s)


# ------------------------------------------------------------------ geometry (sheet mm, origin lower-left)
L = []   # (layer, x1, y1, x2, y2, alias)


def line(layer, x1, y1, x2, y2, alias):
    L.append((layer, x1, y1, x2, y2, alias))


# ISO 5457 frame: 20 left, 10 elsewhere -> 0.7 mm (Border frame layer)
line("frame", 20, 10, 410, 10, "fr_b")
line("frame", 410, 10, 410, 287, "fr_r")
line("frame", 410, 287, 20, 287, "fr_t")
line("frame", 20, 287, 20, 10, "fr_l")
# centring marks, in the margin only (sheet 420 x 297)
line("frame", 210, 0, 210, 10, "cm_b")
line("frame", 210, 287, 210, 297, "cm_t")
line("frame", 0, 148.5, 20, 148.5, "cm_l")
line("frame", 410, 148.5, 420, 148.5, "cm_r")
# zone structure 0.35 (Border zones layer): views above y150, tables|revisions y50-150, notes|title block y10-50
line("zones", 20, 150, 410, 150, "z_view")
line("zones", 20, 50, 410, 50, "z_band")
line("zones", 230, 10, 230, 150, "z_split")
# title block cells 0.25 (Title block layer). Block x230-410, y10-50: logo column x230-266;
# rows: title y38-50, description y28-38, row C y19-28, row D y10-19; units/projection cell x374-410, y10-28
line("tb", 266, 10, 266, 50, "tb_logo")
line("tb", 266, 38, 410, 38, "tb_r1")
line("tb", 266, 28, 410, 28, "tb_r2")
line("tb", 266, 19, 374, 19, "tb_r3")
line("tb", 374, 10, 374, 28, "tb_units")
line("tb", 322, 19, 322, 28, "tb_c1")
line("tb", 350, 19, 350, 28, "tb_c2")
for i, x in enumerate((302, 332, 346, 362)):
    line("tb", x, 10, x, 19, "tb_d%d" % i)
# first-angle projection symbol (ISO 5456-2): trapezoid, short side AWAY from the circles
cy = 18.5
line("tb", 391, cy - 1.75, 391, cy + 1.75, "pj_a")
line("tb", 391, cy + 1.75, 398, cy + 3.5, "pj_b")
line("tb", 398, cy + 3.5, 398, cy - 3.5, "pj_c")
line("tb", 398, cy - 3.5, 391, cy - 1.75, "pj_d")
CIRCLES = [("tb", 404.5, cy, 3.5, "pj_o"), ("tb", 404.5, cy, 1.75, "pj_i")]

# ------------------------------------------------------------------ notes (position = top-left of the text)
LBL, VAL, TTL = 2.5, 3.5, 5.0
N = []   # (layer, x, ytop, contents, height, alias)


def note(layer, x, y, s, h, alias, bold=False):
    N.append((layer, x, y, txt(s, bold), h, alias))


def cell(x, ytop, label, alias, value="", vbold=False, vh=VAL):
    note("tb", x + 1.5, ytop - 0.9, label, LBL, "lb_" + alias)
    if value:
        note("tb", x + 1.5, ytop - 4.2, value, vh, "v_" + alias, vbold)


note("tb", 232.6, 21.0, BRANDS[BRAND]["dept"], LBL, "lb_dept", bold=True)
cell(266, 50, "TITLE", "title")
cell(266, 38, "DESCRIPTION", "desc")
cell(266, 28, "MATERIAL", "mat")
cell(322, 28, "WEIGHT", "mass")
cell(350, 28, "SCALE", "scale")
cell(266, 19, "DRAWN BY", "drawn")
cell(302, 19, "DATE", "date")
cell(332, 19, "SIZE", "size")
cell(346, 19, "SHEET", "sheet")
cell(362, 19, "REV", "rev")
note("tb", 375.5, 27.1, "UNITS / PROJECTION", LBL, "lb_units")
note("tb", 375.8, 21.0, "mm", TTL, "st_units")
note("zones", 22, 48.8, "NOTES", LBL, "lb_notes", bold=True)

# Property-linked value notes. Phase "values" creates them alone, spread out over the empty view zone, so the UI
# can link each one without hitting a neighbour; phase "rest" moves them into their cells (a position-only
# onshapeEditAnnotations keeps the property link) and adds everything else. The placeholder length fixes the
# note's wrap width (Onshape keeps the created width), so it is sized to the cell.
# (alias, final x, final ytop, height, bold, wrap width mm, property source, property items)
VALUES = [
    ("v_title", 267.5, 45.6, TTL, True, 140, [("sheet", "Name")]),
    ("v_desc", 267.5, 33.6, VAL, False, 140, [("sheet", "Description")]),
    ("v_mat", 267.5, 24.2, VAL, False, 60, [("sheet", "Material")]),
    ("v_mass", 323.5, 24.2, VAL, False, 25, [("sheet", "Mass")]),
    ("v_rev", 363.5, 15.2, VAL, False, 13, [("sheet", "Revision")]),
    ("v_drawn", 267.5, 15.2, VAL, False, 33, [("drawing", "Drawing drawn by")]),
    ("v_date", 303.5, 15.2, VAL, False, 22, [("drawing", "Drawing date drawn")]),
    ("v_scale", 351.5, 24.2, VAL, False, 13, [("drawing", "Sheet scale")]),
    ("v_size", 333.5, 15.2, VAL, False, 11, [("drawing", "Sheet size")]),
    ("v_sheet", 347.5, 15.2, VAL, False, 15, [("drawing", "Sheet number"), ("text", " / "), ("drawing", "Total sheets")]),
]


def staging(k):
    return 40.0 + 190.0 * (k % 2), 275.0 - 22.0 * (k // 2)


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


LAYOUT = Path(__file__).parent / "part_a3_layout.json"


def phase_values():
    existing = [e for e in elements(TD, TW) if e["name"] == TAB]
    if existing and "--rebuild" not in sys.argv:
        raise SystemExit(f"'{TAB}' exists ({existing[0]['id']}); pass --rebuild to delete and rebuild it")
    for e in existing:
        c._request("DELETE", f"/api/v6/elements/d/{TD}/w/{TW}/e/{e['id']}")
    eid = c.post(f"/api/v6/drawings/d/{TD}/w/{TW}/create", json_data={
        "drawingName": TAB, "border": False, "titleblock": False, "size": "A3", "units": "MILLIMETER",
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
