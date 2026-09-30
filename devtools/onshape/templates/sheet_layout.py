"""Parametric sheet layout for the drawing-template masters (A4 / A3 / A1 ...), ASCII only.

Everything is in sheet mm, origin at the sheet's lower-left corner. One function, `sheet_layout(size)`, returns
the frame, the centring marks, the title-block origin and every title-block line / note, the revision-table
anchor and the NOTES label for that sheet size. build_part_a3_master.py (and the later A4 / A1 builders) only
turn this into Onshape annotations; nothing size-specific lives anywhere else.

Rules (publish_tools/research/drawing_style_guide.md):
- ISO 5457 frame: 20 mm left margin, 10 mm on the other sides; centring marks in the margin only.
- NO interior zone lines: the sheet inside the frame is open space (user decision 2026-09-29).
- Title block in the bottom-right corner, inside the frame (ISO 7200: max 180 wide): STANDARD profile 180 x 40
  (A3, A2), COMPACT profile 150 x 32 (A4, user decision 2026-09-29) -- same cell structure, smaller text.
- Revision table (Onshape's own, placed in the UI): fixed corner TOP-RIGHT at the frame's top-right corner, so its
  right edge lines up with the title block's right edge; as wide as the title block; it grows downward.
- NOTES label (bold, no lines) at the bottom-left, its top aligned with the title block's top.
"""

SHEETS = {  # landscape width x height (ISO 216)
    "A4": (297.0, 210.0),
    "A3": (420.0, 297.0),
    "A2": (594.0, 420.0),
    "A1": (841.0, 594.0),
    "A0": (1189.0, 841.0),
}
MARGIN_L, MARGIN = 20.0, 10.0

# ---------------------------------------------------------------------------------------------------- profiles
# STANDARD (A3 / A2 and larger): title block 180 x 40, labels 2.5 / values 3.5 / part name 5 bold.
# COMPACT (A4, user decision 2026-09-29): the same cell structure scaled to 150 x 32 so the block takes less of the
# small sheet; labels 1.8 (ISO 3098 minimum, allowed on A4 / A3) / values 2.5 / part name 3.5 bold; revision table
# 150 wide with 1.8 text; NOTES label 1.8 bold. Table defaults (BOM / custom / ...) stay 2.5 on every size.
TB_W, TB_H = 180.0, 40.0          # STANDARD values, kept as module constants for older scripts
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

STANDARD = {
    "name": "standard", "tb_w": TB_W, "tb_h": TB_H, "logo_w": 36.0,
    "lbl": LBL, "val": VAL, "ttl": TTL, "units_h": TTL, "dept_h": LBL, "notes_h": LBL,
    "lbl_dy": LBL_DY, "val_dy": VAL_DY, "ttl_dy": TTL_DY, "pad": 1.5,
    "lines": TB_LINES, "cells": TB_CELLS,
    "wrap": {v[0]: v[4] for v in TB_VALUES},
    # first-angle symbol: (trapezoid left x, scale (1 = 7 mm tall), centre height); units note / label spots
    "proj": (161.0, 1.0, 8.5), "units_note": (146.5, 11.0), "units_label": (145.5, 17.4),
    "dept": (2.6, 11.0), "dept_wrap": False, "notes_dy": 1.2,
    "logo": {"c": (18.0, 26.0), "w_square": 28.0, "w_wide": 32.0},
    "rev": {"width": REV_W, "cols": (18.0, 110.0, 24.0, 28.0), "text": LBL},
}

# COMPACT 150 x 32. Rows (bottom up) D 0-7.2, C 7.2-14.4, description 14.4-22.4, title 22.4-32 (the A3 rows x 0.8);
# columns = the A3 columns x 150/180 rounded to whole mm (logo 30, material 30-77, weight 77-100, scale 100-120,
# units 120-150; drawn 30-60, date 60-85, size 85-97, sheet 97-110, rev 110-120).
# Text: 1.8 label top 0.5 below the line (baseline 2.3 below); 2.5 value top 3.1 below (baseline 1.6 above the
# 7.2 row's lower line); 3.5 bold part name top 3.6 below the title line (baseline 2.5 above its lower line).
COMPACT = {
    "name": "compact", "tb_w": 150.0, "tb_h": 32.0, "logo_w": 30.0,
    "lbl": 1.8, "val": 2.5, "ttl": 3.5, "units_h": 3.5, "dept_h": 1.8, "notes_h": 1.8,
    "lbl_dy": 0.5, "val_dy": 3.1, "ttl_dy": 3.6, "pad": 1.2,
    "lines": [
        ("zones", 0, 32, 150, 32, "tb_top"),
        ("zones", 0, 0, 0, 32, "tb_left"),
        ("tb", 30, 0, 30, 32, "tb_logo"),
        ("tb", 30, 22.4, 150, 22.4, "tb_r1"),
        ("tb", 30, 14.4, 150, 14.4, "tb_r2"),
        ("tb", 30, 7.2, 120, 7.2, "tb_r3"),
        ("tb", 120, 0, 120, 14.4, "tb_units"),
        ("tb", 77, 7.2, 77, 14.4, "tb_c1"),
        ("tb", 100, 7.2, 100, 14.4, "tb_c2"),
        ("tb", 60, 0, 60, 7.2, "tb_d0"),
        ("tb", 85, 0, 85, 7.2, "tb_d1"),
        ("tb", 97, 0, 97, 7.2, "tb_d2"),
        ("tb", 110, 0, 110, 7.2, "tb_d3"),
    ],
    "cells": [
        (30, 32, "TITLE", "title"),
        (30, 22.4, "DESCRIPTION", "desc"),
        (30, 14.4, "MATERIAL", "mat"),
        (77, 14.4, "WEIGHT", "mass"),
        (100, 14.4, "SCALE", "scale"),
        (30, 7.2, "DRAWN BY", "drawn"),
        (60, 7.2, "DATE", "date"),
        (85, 7.2, "SIZE", "size"),
        (97, 7.2, "SHEET", "sheet"),
        (110, 7.2, "REV", "rev"),
    ],
    "wrap": {"v_title": 116, "v_desc": 116, "v_mat": 44, "v_mass": 21, "v_rev": 8, "v_drawn": 27,
             "v_date": 23, "v_scale": 18, "v_size": 10, "v_sheet": 11},
    # symbol x 0.75 (trapezoid 5.25 x 5.25, circles r 2.625 / 1.3125), outer circle 1.55 mm from the right edge
    "proj": (135.7, 0.75, 6.0), "units_note": (121.5, 7.75), "units_label": (121.2, 13.9),
    # department line 1.8 bold, centred in the logo cell; two lines when wider than the cell less 2 mm
    "dept": (None, 7.0), "dept_wrap": True, "notes_dy": 1.0,
    "logo": {"c": (15.0, 20.0), "w_square": 23.0, "w_wide": 26.0},
    "rev": {"width": 150.0, "cols": (15.0, 92.0, 20.0, 23.0), "text": 1.8},
}
PROFILE_BY_SIZE = {"A4": COMPACT}

BOLD_CAP_W = 0.815   # Noto Sans bold caps: advance per character / text height (measured on SKI ENGINEERING)


def profile(size):
    return PROFILE_BY_SIZE.get(size, STANDARD)


def dept_text(p, dept):
    """(content, x, y top) of the department line in the logo cell (relative to the title block)."""
    if not p["dept_wrap"]:
        x, y = p["dept"]
        return dept, x, y
    h, cw = p["dept_h"], p["logo_w"]
    lines = [dept]
    if len(dept) * BOLD_CAP_W * h > cw - 2.0 and " " in dept:
        k = dept.rindex(" ")
        lines = [dept[:k], dept[k + 1:]]
    w = max(len(s) for s in lines) * BOLD_CAP_W * h
    y = p["dept"][1] + (1.8 * h if len(lines) > 1 else 0.0)
    return "\\P".join(lines), max(1.0, (cw - w) / 2.0), y


def sheet_layout(size="A3", dept="SKI ENGINEERING"):
    p = profile(size)
    W, H = SHEETS[size]
    x0, y0, x1, y1 = MARGIN_L, MARGIN, W - MARGIN, H - MARGIN
    tw, th = p["tb_w"], p["tb_h"]
    tx, ty = x1 - tw, y0
    lines, circles, notes, values = [], [], [], []
    # frame (Border frame layer, 0.7)
    lines += [("frame", x0, y0, x1, y0, "fr_b"), ("frame", x1, y0, x1, y1, "fr_r"),
              ("frame", x1, y1, x0, y1, "fr_t"), ("frame", x0, y1, x0, y0, "fr_l")]
    cx, cy = W / 2.0, H / 2.0
    lines += [("frame", cx, 0, cx, y0, "cm_b"), ("frame", cx, y1, cx, H, "cm_t"),
              ("frame", 0, cy, x0, cy, "cm_l"), ("frame", x1, cy, W, cy, "cm_r")]
    # title block
    for layer, a, b, c, d, alias in p["lines"]:
        lines.append((layer, tx + a, ty + b, tx + c, ty + d, alias))
    # first-angle projection symbol (ISO 5456-2) in the units cell: trapezoid, short side away from the circles
    L, s, pyr = p["proj"]
    py = ty + pyr
    for a, b, c, d, alias in ((L, -1.75 * s, L, 1.75 * s, "pj_a"), (L, 1.75 * s, L + 7 * s, 3.5 * s, "pj_b"),
                              (L + 7 * s, 3.5 * s, L + 7 * s, -3.5 * s, "pj_c"),
                              (L + 7 * s, -3.5 * s, L, -1.75 * s, "pj_d")):
        lines.append(("tb", tx + a, py + b, tx + c, py + d, alias))
    circles += [("tb", tx + L + 13.5 * s, py, 3.5 * s, "pj_o"), ("tb", tx + L + 13.5 * s, py, 1.75 * s, "pj_i")]
    cells = {}
    pad = p["pad"]
    for x, top, label, alias in p["cells"]:
        cells[alias] = (tx + x, ty + top)
        notes.append(("tb", tx + x + pad, ty + top - p["lbl_dy"], label, p["lbl"], "lb_" + alias, False))
    dtxt, dx, dy = dept_text(p, dept)
    notes.append(("tb", tx + dx, ty + dy, dtxt, p["dept_h"], "lb_dept", True))
    ux, uy = p["units_label"]
    notes.append(("tb", tx + ux, ty + uy, "UNITS / PROJECTION", p["lbl"], "lb_units", False))
    ux, uy = p["units_note"]
    notes.append(("tb", tx + ux, ty + uy, "mm", p["units_h"], "st_units", False))
    notes.append(("zones", x0 + 2.0, ty + th - p["notes_dy"], "NOTES", p["notes_h"], "lb_notes", True))
    for alias, cell, h, bold, _wrap, props in TB_VALUES:
        cxl, top = cells[cell]
        h = p["ttl"] if h == TTL else p["val"]
        values.append((alias, cxl + pad, top - (p["ttl_dy"] if h == p["ttl"] else p["val_dy"]), h, bold,
                       p["wrap"][alias], props))
    rev = p["rev"]
    return {
        "size": size, "profile": p["name"], "sheet": (W, H), "frame": (x0, y0, x1, y1),
        "title_block": (tx, ty, tw, th), "logo_w": p["logo_w"], "logo": p["logo"],
        "revision_table": {"corner": "top-right", "at": (x1, y1), "width": rev["width"], "text": rev["text"],
                           "cols": rev["cols"]},
        "lines": lines, "circles": circles, "notes": notes, "values": values,
        # value-note staging spots (empty paper, spread out) for linking them in the UI one at a time
        "staging": [(x0 + 20.0 + 190.0 * (k % 2), y1 - 12.0 - 22.0 * (k // 2)) for k in range(len(values))],
    }


if __name__ == "__main__":
    import sys
    lay = sheet_layout(sys.argv[1] if len(sys.argv) > 1 else "A3")
    for k in ("size", "profile", "sheet", "frame", "title_block", "revision_table"):
        print(k, lay[k])
    for v in lay["values"]:
        print(v[:4])
