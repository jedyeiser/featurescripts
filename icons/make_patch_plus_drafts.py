"""Draft icons for Patch+ (push or pull the region inside a footprint): 20 x 20, Onshape style (outline #333333, fill
#FFFFFF, secondary #999999, accent blue #1651B0, blue "+" badge bottom right).

    python icons/make_patch_plus_drafts.py   ->  icons/drafts/patch_plus/{A_boss,B_pocket,C_boss_draft,D_boss_footprint}.svg + review.html
"""
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "drafts", "patch_plus")
os.makedirs(OUT, exist_ok=True)

DARK, GREY, WHITE, BLUE = "#333333", "#999999", "#FFFFFF", "#1651B0"
PLUS = ('<path d="M11 15h3v-3h2v3h3v2h-3v3h-2v-3h-3v-2z" fill="#FFFFFF" stroke="#FFFFFF" stroke-width="1.6" stroke-linejoin="miter"/>'
        '<path d="M11 15h3v-3h2v3h3v2h-3v3h-2v-3h-3v-2z" fill="#1651B0"/>')


def poly(points, fill, stroke=DARK, width=1, extra=""):
    pts = " ".join("%g,%g" % p for p in points)
    return '<polygon points="%s" fill="%s" stroke="%s" stroke-width="%g" stroke-linejoin="round"%s/>' % (pts, fill, stroke, width, extra)


def rhombus(cx, cy, hw, hh):
    """left, top, right, bottom corners of an isometric diamond centred (cx, cy)."""
    return [(cx - hw, cy), (cx, cy - hh), (cx + hw, cy), (cx, cy + hh)]


def shift(points, dy):
    return [(x, y + dy) for x, y in points]


def svg(body):
    return '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">%s%s</svg>' % (body, PLUS)


BASE = rhombus(9, 10, 8.2, 4.6)          # the surface
CX, CY = 8.6, 9.6


def boss(top_scale=1.0, rise=3.2, hw=3.8, hh=2.3):
    foot = rhombus(CX, CY, hw, hh)                 # where the block stands on the surface
    top = shift(rhombus(CX, CY, hw * top_scale, hh * top_scale), -rise)
    l, t, r, b = foot
    tl, tt, tr, tb = top
    left_wall = [l, b, tb, tl]
    right_wall = [b, r, tr, tb]
    return (poly(BASE, WHITE) + poly(left_wall, GREY) + poly(right_wall, WHITE) + poly(top, WHITE))


def pocket(depth=2.6, hw=3.8, hh=2.3):
    rim = rhombus(CX, CY, hw, hh)
    floor = shift(rim, depth)
    rl, rt, rr, rb = rim
    fl, ft, fr, fb = floor
    back_left = [rl, rt, ft, fl]
    back_right = [rt, rr, fr, ft]
    return poly(BASE, WHITE) + poly(back_left, GREY) + poly(back_right, "#CCCCCC") + poly(floor, WHITE) + poly(rim, "none", width=1)


def footprint_ring(hw=5.6, hh=3.3):
    pts = rhombus(CX, CY + 0.4, hw, hh)
    return poly(pts, "none", stroke=BLUE, width=0.9, extra=' stroke-dasharray="1.3 1.1"')


CANDIDATES = {
    "A_boss": svg(boss()),
    "B_pocket": svg(pocket()),
    "C_boss_draft": svg(boss(top_scale=0.62, rise=3.4, hw=4.2, hh=2.55)),
    "D_boss_footprint": svg(poly(BASE, WHITE) + footprint_ring() + boss().split(poly(BASE, WHITE), 1)[1]),
}

for name, text in CANDIDATES.items():
    with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as f:
        f.write(text)

cells = "".join(
    '<div class="cell"><div class="row"><span class="s20">%s</span><span class="s64">%s</span></div><div class="big">%s</div><p>%s</p></div>'
    % (text, text.replace('width="20" height="20"', 'width="64" height="64"'), text.replace('width="20" height="20"', 'width="160" height="160"'), name)
    for name, text in CANDIDATES.items())
with open(os.path.join(OUT, "review.html"), "w", encoding="utf-8") as f:
    f.write('<!doctype html><meta charset="utf-8"><title>Patch+ icon candidates</title><style>body{font:14px sans-serif;background:#fff;margin:16px}'
            '.cell{display:inline-block;margin:0 20px 20px 0;vertical-align:top}.row{display:flex;gap:16px;align-items:center;margin-bottom:8px}'
            '.big{border:1px solid #ddd;padding:4px;display:inline-block}p{margin:4px 0}</style>' + cells)
print("wrote", len(CANDIDATES), "candidates to", OUT)
