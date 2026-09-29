"""Parametric sheet layout for the drawing-template masters (A4 / A3 / A1 ...), ASCII only.

Everything is in sheet mm, origin at the sheet's lower-left corner. One function, `sheet_layout(size)`, returns
the frame, the centring marks, the title-block origin and every title-block line / note, the revision-table
anchor and the NOTES label for that sheet size. build_part_a3_master.py (and the later A4 / A1 builders) only
turn this into Onshape annotations; nothing size-specific lives anywhere else.

Rules (publish_tools/research/drawing_style_guide.md):
- ISO 5457 frame: 20 mm left margin, 10 mm on the other sides; centring marks in the margin only.
- NO interior zone lines: the sheet inside the frame is open space (user decision 2026-09-29).
- Title block 180 x 40 in the bottom-right corner, inside the frame (ISO 7200: max 180 wide).
- Revision table (Onshape's own, placed in the UI): fixed corner TOP-RIGHT at the frame's top-right corner, so its
  right edge lines up with the title block's right edge; 180 mm wide; it grows downward.
- NOTES label (2.5 bold, no lines) at the bottom-left, its top aligned with the title block's top.
"""

SHEETS = {  # landscape width x height (ISO 216)
    "A4": (297.0, 210.0),
    "A3": (420.0, 297.0),
    "A2": (594.0, 420.0),
    "A1": (841.0, 594.0),
    "A0": (1189.0, 841.0),
}
MARGIN_L, MARGIN = 20.0, 10.0
TB_W, TB_H = 180.0, 40.0
REV_W = 180.0

LBL, VAL, TTL = 2.5, 3.5, 5.0      # ISO 3098 heights: labels / values / part name + units
LBL_DY = 0.6                       # label top below the cell's top line
VAL_DY = 3.7                       # 3.5 mm value top below the cell's top line (baseline ~1.7 mm above the
                                   # lower line, descenders clear it; value cap top 0.65 below the label baseline)
TTL_DY = 4.4                       # 5 mm bold part name top below the title row's top line

# Title block, relative to its lower-left corner (tx, ty). Columns: logo 0-36; rows (bottom up):
# D 0-9 (drawn / date / size / sheet / rev), C 9-18 (material / weight / scale), description 18-28, title 28-40;
# units / projection cell 144-180 x 0-18.
TB_LINES = [  # (layer, x1, y1, x2, y2, alias)
    ("zones", 0, 40, 180, 40, "tb_top"),    # outer edges not on the frame: 0.35 (Border zones layer)
    ("zones", 0, 0, 0, 40, "tb_left"),
    ("tb", 36, 0, 36, 40, "tb_logo"),
    ("tb", 36, 28, 180, 28, "tb_r1"),
    ("tb", 36, 18, 180, 18, "tb_r2"),
    ("tb", 36, 9, 144, 9, "tb_r3"),
    ("tb", 144, 0, 144, 18, "tb_units"),
    ("tb", 92, 9, 92, 18, "tb_c1"),
    ("tb", 120, 9, 120, 18, "tb_c2"),
    ("tb", 72, 0, 72, 9, "tb_d0"),
    ("tb", 102, 0, 102, 9, "tb_d1"),
    ("tb", 116, 0, 116, 9, "tb_d2"),
    ("tb", 132, 0, 132, 9, "tb_d3"),
]
# (x, row top, label, alias) -- label at (x + 1.5, top - LBL_DY), value at (x + 1.5, top - VAL_DY)
TB_CELLS = [
    (36, 40, "TITLE", "title"),
    (36, 28, "DESCRIPTION", "desc"),
    (36, 18, "MATERIAL", "mat"),
    (92, 18, "WEIGHT", "mass"),
    (120, 18, "SCALE", "scale"),
    (36, 9, "DRAWN BY", "drawn"),
    (72, 9, "DATE", "date"),
    (102, 9, "SIZE", "size"),
    (116, 9, "SHEET", "sheet"),
    (132, 9, "REV", "rev"),
]
# property-linked values: (alias, cell alias, height, bold, wrap width mm, property items)
TB_VALUES = [
    ("v_title", "title", TTL, True, 140, [("sheet", "Name")]),
    ("v_desc", "desc", VAL, False, 140, [("sheet", "Description")]),
    ("v_mat", "mat", VAL, False, 60, [("sheet", "Material")]),
    ("v_mass", "mass", VAL, False, 25, [("sheet", "Mass")]),
    ("v_rev", "rev", VAL, False, 13, [("sheet", "Revision")]),
    ("v_drawn", "drawn", VAL, False, 33, [("drawing", "Drawing drawn by")]),
    ("v_date", "date", VAL, False, 22, [("drawing", "Drawing date drawn")]),
    ("v_scale", "scale", VAL, False, 13, [("drawing", "Sheet scale")]),
    ("v_size", "size", VAL, False, 11, [("drawing", "Sheet size")]),
    ("v_sheet", "sheet", VAL, False, 15, [("drawing", "Sheet number"), ("text", " / "), ("drawing", "Total sheets")]),
]


def sheet_layout(size="A3", dept="SKI ENGINEERING"):
    W, H = SHEETS[size]
    x0, y0, x1, y1 = MARGIN_L, MARGIN, W - MARGIN, H - MARGIN
    tx, ty = x1 - TB_W, y0
    lines, circles, notes, values = [], [], [], []
    # frame (Border frame layer, 0.7)
    lines += [("frame", x0, y0, x1, y0, "fr_b"), ("frame", x1, y0, x1, y1, "fr_r"),
              ("frame", x1, y1, x0, y1, "fr_t"), ("frame", x0, y1, x0, y0, "fr_l")]
    cx, cy = W / 2.0, H / 2.0
    lines += [("frame", cx, 0, cx, y0, "cm_b"), ("frame", cx, y1, cx, H, "cm_t"),
              ("frame", 0, cy, x0, cy, "cm_l"), ("frame", x1, cy, W, cy, "cm_r")]
    # title block
    for layer, a, b, c, d, alias in TB_LINES:
        lines.append((layer, tx + a, ty + b, tx + c, ty + d, alias))
    # first-angle projection symbol (ISO 5456-2) in the units cell: trapezoid, short side away from the circles
    py = ty + 8.5
    for a, b, c, d, alias in ((161, -1.75, 161, 1.75, "pj_a"), (161, 1.75, 168, 3.5, "pj_b"),
                              (168, 3.5, 168, -3.5, "pj_c"), (168, -3.5, 161, -1.75, "pj_d")):
        lines.append(("tb", tx + a, py + b, tx + c, py + d, alias))
    circles += [("tb", tx + 174.5, py, 3.5, "pj_o"), ("tb", tx + 174.5, py, 1.75, "pj_i")]
    cells = {}
    for x, top, label, alias in TB_CELLS:
        cells[alias] = (tx + x, ty + top)
        notes.append(("tb", tx + x + 1.5, ty + top - LBL_DY, label, LBL, "lb_" + alias, False))
    notes.append(("tb", tx + 2.6, ty + 11.0, dept, LBL, "lb_dept", True))
    notes.append(("tb", tx + 145.5, ty + 17.4, "UNITS / PROJECTION", LBL, "lb_units", False))
    notes.append(("tb", tx + 146.5, ty + 11.0, "mm", TTL, "st_units", False))
    notes.append(("zones", x0 + 2.0, ty + TB_H - 1.2, "NOTES", LBL, "lb_notes", True))
    for alias, cell, h, bold, wrap, props in TB_VALUES:
        cxl, top = cells[cell]
        values.append((alias, cxl + 1.5, top - (TTL_DY if h == TTL else VAL_DY), h, bold, wrap, props))
    return {
        "size": size, "sheet": (W, H), "frame": (x0, y0, x1, y1),
        "title_block": (tx, ty, TB_W, TB_H),
        "revision_table": {"corner": "top-right", "at": (x1, y1), "width": REV_W, "text": LBL},
        "lines": lines, "circles": circles, "notes": notes, "values": values,
        # value-note staging spots (empty paper, spread out) for linking them in the UI one at a time
        "staging": [(x0 + 20.0 + 190.0 * (k % 2), y1 - 12.0 - 22.0 * (k // 2)) for k in range(len(values))],
    }


if __name__ == "__main__":
    import sys
    lay = sheet_layout(sys.argv[1] if len(sys.argv) > 1 else "A3")
    for k in ("size", "sheet", "frame", "title_block", "revision_table"):
        print(k, lay[k])
    for v in lay["values"]:
        print(v[:4])
