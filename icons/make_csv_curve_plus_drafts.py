"""Draft icons for csv curve + (CSV points -> one wire of several curves joined at break points): 20 x 20, Onshape style
(outline #333333, fill #FFFFFF, secondary #999999, accent blue #1651B0, blue "+" badge bottom right).

    python icons/make_csv_curve_plus_drafts.py  ->  icons/drafts/csv_curve_plus/{A_points_joints,B_table_segments,C_bridge_arc,D_break_ticks}.svg
                                                    + review.html (+ icons/final/csv_curve_plus_icon.svg = RECOMMENDED)
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "drafts", "csv_curve_plus")
FINAL = os.path.join(HERE, "final", "csv_curve_plus_icon.svg")
RECOMMENDED = "A_points_joints"
os.makedirs(OUT, exist_ok=True)

DARK, GREY, WHITE, BLUE = "#333333", "#999999", "#FFFFFF", "#1651B0"
PLUS = ('<path d="M11 15h3v-3h2v3h3v2h-3v3h-2v-3h-3v-2z" fill="#FFFFFF" stroke="#FFFFFF" stroke-width="1.6" stroke-linejoin="miter"/>'
        '<path d="M11 15h3v-3h2v3h3v2h-3v3h-2v-3h-3v-2z" fill="#1651B0"/>')


def svg(body, plus=True):
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">%s%s</svg>'
            % (body, PLUS if plus else ""))


def path(d, stroke=DARK, w=1.5, extra=""):
    return '<path d="%s" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"%s/>' % (d, stroke, w, extra)


def dot(x, y, r, fill, stroke="none", w=0):
    s = ' stroke="%s" stroke-width="%g"' % (stroke, w) if stroke != "none" else ""
    return '<circle cx="%g" cy="%g" r="%g" fill="%s"%s/>' % (x, y, r, fill, s)


# A: a row of data points under a curve built from three sections, two blue joint markers
def cand_a():
    b = path("M1.8 13C2.5 8 4.5 4 7 5.5") + path("M7 5.5C9 7 10.5 10 12.5 8.5") + path("M12.5 8.5C14.5 7 16 4.5 18.2 3.5")
    b += "".join(dot(x, 17.6, 1.0, GREY) for x in (2.2, 4.8, 7.4))
    b += dot(7, 5.5, 1.7, BLUE) + dot(12.5, 8.5, 1.7, BLUE)
    return svg(b)


# B: a table (CSV) corner with a segmented curve leaving it, break dots in blue
def cand_b():
    t = ('<rect x="1.5" y="10.5" width="7.5" height="7.5" fill="#FFFFFF" stroke="#999999" stroke-width="1"/>'
         '<path d="M1.5 15.5H9M4.5 10.5V18M6.5 10.5V18" stroke="#999999" stroke-width="0.8" fill="none"/>'
         '<rect x="1.5" y="10.5" width="7.5" height="2.6" fill="#999999"/>')
    c = path("M4.5 9C5.5 4.5 8 2.8 10 3.5") + path("M10 3.5C12 4.2 12.5 7 14.2 6.2") + path("M14.2 6.2C15.8 5.4 16.5 3 18.5 2.5")
    c += dot(10, 3.5, 1.7, BLUE) + dot(14.2, 6.2, 1.7, BLUE)
    return svg(t + c)


# C: two straight pieces whose kink (grey ghost corner) is replaced by a blue bridge arc
def cand_c():
    ghost = path("M7 8.5L10 5.5H12.5", GREY, 1, ' stroke-dasharray="1 1"')
    b = path("M1.8 15L6 9.8") + path("M12.5 5.5H18.2") + path("M6 9.8C7.6 7.8 9 5.5 12.5 5.5", BLUE, 2)
    b += dot(1.8, 15, 1.5, DARK) + dot(18.2, 5.5, 1.5, DARK)
    return svg(ghost + b)


# D: curve in three sections cut by blue break ticks, grey points beneath, no plus badge
def cand_d():
    b = path("M1.5 12C2.5 6 5 4 7 6") + path("M7 6C9 8 10 12 12.5 10") + path("M12.5 10C15 8 16 5 18.5 4")
    b += '<path d="M7 2.2V9.8M12.5 6.2V13.8" stroke="#1651B0" stroke-width="1.6" stroke-linecap="round" fill="none"/>'
    b += "".join(dot(x, 17.4, 1.1, GREY) for x in (2, 5, 8, 11, 14, 17))
    return svg(b, plus=False)


CANDIDATES = {"A_points_joints": cand_a(), "B_table_segments": cand_b(), "C_bridge_arc": cand_c(), "D_break_ticks": cand_d()}

for name, text in CANDIDATES.items():
    with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as f:
        f.write(text)
with open(FINAL, "w", encoding="utf-8") as f:
    f.write(CANDIDATES[RECOMMENDED])


def big(text, n):
    return text.replace('width="20" height="20"', 'width="%d" height="%d"' % (n, n))


def panel(bg, cls):
    cells = "".join('<div class="cell"><div class="row"><span>%s</span><span>%s</span><span>%s</span></div><p>%s</p></div>'
                    % (big(t, 20), big(t, 64), big(t, 160), n) for n, t in CANDIDATES.items())
    return '<div class="panel %s">%s</div>' % (cls, cells)


with open(os.path.join(OUT, "review.html"), "w", encoding="utf-8") as f:
    f.write('<!doctype html><meta charset="utf-8"><title>csv curve + icon candidates</title><style>'
            'body{font:14px sans-serif;margin:0}.panel{padding:16px;display:flex;flex-wrap:wrap;gap:12px 28px}'
            '.light{background:#fff;color:#222}.dark{background:#2b2b2b;color:#ddd}'
            '.row{display:flex;gap:16px;align-items:center}p{margin:4px 0}</style>'
            + panel("#fff", "light") + panel("#2b2b2b", "dark"))
print("wrote", len(CANDIDATES), "candidates to", OUT, "and", FINAL)


