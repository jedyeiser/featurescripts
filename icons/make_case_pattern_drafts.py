"""Draft icons for Case template / Case pattern (case_pattern document) and a review page.

Style (feature-icons memory, correction 35): 20 x 20 viewBox, outline #333333, fill #FFFFFF,
secondary #999999, accent Onshape blue #1651B0. The "( )" brackets are cut from Onshape's own
query-variable-button glyph so the family reads as query-variable tools.

usage (repo root): python icons/make_case_pattern_drafts.py  -> icons/drafts/case_pattern/*.svg + review.html
"""
import os
import re

OUT = os.path.join(os.path.dirname(__file__), "drafts", "case_pattern")
os.makedirs(OUT, exist_ok=True)

DARK, GREY, WHITE, BLUE = "#333333", "#999999", "#FFFFFF", "#1651B0"

qv = open(os.path.join(os.path.dirname(__file__), "onshape_reference", "query-variable-button.svg")).read()
qv_path = re.search(r'd="([^"]+)"', qv).group(1)
BRACKETS = qv_path[:qv_path.index("M8.625")]  # the two "( )" subpaths


def svg(body):
    return '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">%s</svg>' % body


def brackets(color=DARK):
    return '<path d="%s" fill="%s"/>' % (BRACKETS, color)


def loop_arrow(cx, cy, r, color=BLUE, width=1.4):
    """A circular arrow, 300 deg clockwise from the top, head at the end."""
    import math
    a0, a1 = math.radians(-60), math.radians(240)
    x0, y0 = cx + r * math.cos(a0), cy + r * math.sin(a0)
    x1, y1 = cx + r * math.cos(a1), cy + r * math.sin(a1)
    arc = '<path d="M%.2f %.2f A%.2f %.2f 0 1 1 %.2f %.2f" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (
        x0, y0, r, r, x1, y1, color, width)
    # head at (x1, y1), tangent direction of a clockwise arc (in SVG y-down coords) at angle a1
    tx, ty = -math.sin(a1), math.cos(a1)
    nx, ny = -ty, tx
    h = 2.6
    p1 = (x1 + tx * h * 0.9, y1 + ty * h * 0.9)
    p2 = (x1 + nx * h * 0.75 - tx * h * 0.3, y1 + ny * h * 0.75 - ty * h * 0.3)
    p3 = (x1 - nx * h * 0.75 - tx * h * 0.3, y1 - ny * h * 0.75 - ty * h * 0.3)
    head = '<path d="M%.2f %.2f L%.2f %.2f L%.2f %.2f Z" fill="%s"/>' % (p1 + p2 + p3 + (color,))
    return arc + head


DRAFTS = {}

# ---------------- Case pattern ----------------
# A: same steps, different shapes -- seed square (dark) and two dissimilar cases (grey), each with the
#    same blue feature dot.
DRAFTS["pattern_A_dissimilar_shapes"] = svg(
    '<rect x="1.25" y="1.25" width="8" height="8" rx="0.8" fill="%s" stroke="%s" stroke-width="1.3"/>' % (WHITE, DARK)
    + '<circle cx="5.25" cy="5.25" r="1.7" fill="%s"/>' % BLUE
    + '<circle cx="15" cy="5.25" r="3.9" fill="%s" stroke="%s" stroke-width="1.3"/>' % (WHITE, GREY)
    + '<circle cx="15" cy="5.25" r="1.7" fill="%s"/>' % BLUE
    + '<path d="M5.5 18.6 L14.5 18.6 L10 11.4 Z" fill="%s" stroke="%s" stroke-width="1.3" stroke-linejoin="round"/>' % (WHITE, GREY)
    + '<circle cx="10" cy="16" r="1.5" fill="%s"/>' % BLUE)

# B: loop over a feature list -- three feature rows and a blue repeat arrow.
DRAFTS["pattern_B_feature_list_loop"] = svg(
    ''.join('<rect x="1.25" y="%.2f" width="9.5" height="3" rx="0.6" fill="%s" stroke="%s" stroke-width="1.2"/>' % (y, WHITE, DARK)
            for y in (3.5, 8.5, 13.5))
    + ''.join('<rect x="2.6" y="%.2f" width="1.6" height="1.2" fill="%s"/>' % (y + 0.9, GREY) for y in (3.5, 8.5, 13.5))
    + loop_arrow(15.4, 10, 3.4))

# C: stacked case cards, front card dark with blue fold corner.
DRAFTS["pattern_C_case_cards"] = svg(
    '<rect x="7.5" y="1.25" width="11" height="12" rx="0.8" fill="%s" stroke="%s" stroke-width="1.2"/>' % (WHITE, GREY)
    + '<rect x="4.5" y="4.25" width="11" height="12" rx="0.8" fill="%s" stroke="%s" stroke-width="1.2"/>' % (WHITE, GREY)
    + '<path d="M1.5 8.05 a.8 .8 0 0 1 .8 -.8 H9.7 L12.5 10.05 V18 a.8 .8 0 0 1 -.8 .8 H2.3 a.8 .8 0 0 1 -.8 -.8 Z" fill="%s" stroke="%s" stroke-width="1.3" stroke-linejoin="round"/>' % (WHITE, DARK)
    + '<path d="M9.7 7.25 V10.05 H12.5 Z" fill="%s"/>' % BLUE
    + '<path d="M3.6 12.5 H9.5 M3.6 15.2 H8" stroke="%s" stroke-width="1.2" stroke-linecap="round"/>' % DARK)

# D: query-variable brackets around a blue repeat arrow.
DRAFTS["pattern_D_brackets_loop"] = svg(brackets() + loop_arrow(10, 10.2, 4.1, width=1.6))

# E: seed -> dissimilar copies with a blue arrow (the pattern read left to right).
DRAFTS["pattern_E_seed_arrow"] = svg(
    '<rect x="1.25" y="6" width="7" height="7" rx="0.7" fill="%s" stroke="%s" stroke-width="1.3"/>' % (WHITE, DARK)
    + '<path d="M9.8 9.5 H13" stroke="%s" stroke-width="1.5" stroke-linecap="round"/>' % BLUE
    + '<path d="M13.6 9.5 L11.6 7.7 L11.6 11.3 Z" fill="%s"/>' % BLUE
    + '<circle cx="16.6" cy="4.4" r="2.6" fill="%s" stroke="%s" stroke-width="1.2"/>' % (WHITE, GREY)
    + '<path d="M14 18.6 L19.2 18.6 L16.6 13.8 Z" fill="%s" stroke="%s" stroke-width="1.2" stroke-linejoin="round"/>' % (WHITE, GREY))

# ---------------- Case template ----------------
# A: query-variable brackets around a table of named inputs (blue name column).
DRAFTS["template_A_brackets_table"] = svg(
    brackets()
    + ''.join('<rect x="5.6" y="%.2f" width="2.4" height="1.8" fill="%s"/>' % (y, BLUE) for y in (5.4, 9.1, 12.8))
    + ''.join('<rect x="9" y="%.2f" width="5.4" height="1.8" fill="%s"/>' % (y, DARK) for y in (5.4, 9.1, 12.8)))

# B: a sheet with case tabs on top (first tab blue = case 1) and rows below.
DRAFTS["template_B_case_tabs"] = svg(
    '<rect x="1.5" y="1.5" width="4.8" height="3.2" rx="0.6" fill="%s"/>' % BLUE
    + '<rect x="7.2" y="1.5" width="4.8" height="3.2" rx="0.6" fill="%s" stroke="%s" stroke-width="1"/>' % (WHITE, GREY)
    + '<rect x="12.9" y="1.5" width="4.8" height="3.2" rx="0.6" fill="%s" stroke="%s" stroke-width="1"/>' % (WHITE, GREY)
    + '<rect x="1.25" y="4.6" width="17.5" height="14" rx="0.8" fill="%s" stroke="%s" stroke-width="1.3"/>' % (WHITE, DARK)
    + '<path d="M4 9 H16 M4 12.3 H16 M4 15.6 H11" stroke="%s" stroke-width="1.3" stroke-linecap="round"/>' % DARK)

# C: a document of input slots -- blue slot dots with lines (the signature list).
DRAFTS["template_C_slot_list"] = svg(
    '<path d="M3 1.25 H13 L17 5.25 V18 a.75 .75 0 0 1 -.75 .75 H3 a.75 .75 0 0 1 -.75 -.75 V2 a.75 .75 0 0 1 .75 -.75 Z" fill="%s" stroke="%s" stroke-width="1.3" stroke-linejoin="round"/>' % (WHITE, DARK)
    + '<path d="M13 1.25 V5.25 H17" fill="none" stroke="%s" stroke-width="1.1" stroke-linejoin="round"/>' % DARK
    + ''.join('<circle cx="5.6" cy="%.2f" r="1.35" fill="%s"/>' % (y, BLUE) for y in (8, 11.5, 15))
    + ''.join('<path d="M8.4 %.2f H14.2" stroke="%s" stroke-width="1.3" stroke-linecap="round"/>' % (y, DARK) for y in (8, 11.5, 15)))

for name, text in DRAFTS.items():
    open(os.path.join(OUT, name + ".svg"), "w", newline="\n").write(text)

# ---------------- review page ----------------
PAIRS = [("template_A_brackets_table", "pattern_D_brackets_loop", "Query-variable family"),
         ("template_B_case_tabs", "pattern_C_case_cards", "Cases-as-sheets family"),
         ("template_C_slot_list", "pattern_B_feature_list_loop", "List family"),
         ("template_B_case_tabs", "pattern_A_dissimilar_shapes", "Tabs + dissimilar shapes"),
         ("template_A_brackets_table", "pattern_E_seed_arrow", "Brackets + seed arrow")]
REFS = ["query-variable-button", "variable-table-button", "assign-variable-button", "function-button", "transform-button", "fillet-button"]

NOTES = {
    "pattern_A_dissimilar_shapes": "Same blue feature applied to three different shapes: the literal pitch of Case Pattern.",
    "pattern_B_feature_list_loop": "Three feature-list rows with a repeat arrow: 're-run these features'.",
    "pattern_C_case_cards": "Stack of case sheets, front one active (blue fold).",
    "pattern_D_brackets_loop": "Onshape's query-variable ( ) with a repeat arrow inside: 'repeat over query variables'.",
    "pattern_E_seed_arrow": "Seed shape -> arrow -> dissimilar copies (reads like a pattern).",
    "template_A_brackets_table": "Onshape's query-variable ( ) around a name/value table: 'a set of query variables'.",
    "template_B_case_tabs": "A sheet with case tabs; tab 1 (blue) is case 1.",
    "template_C_slot_list": "Document of input slots: blue slot markers + rows.",
}


def cell(src, size, bg=None):
    img = '<img src="%s" width="%d" height="%d">' % (src, size, size)
    if bg:
        img = '<div style="background:%s;padding:8px;border-radius:4px;display:inline-block">%s</div>' % (bg, img)
    return img


def row(name):
    src = name + ".svg"
    return ('<div class="opt"><div class="name">%s</div><div class="note">%s</div><div class="sizes">%s%s%s%s</div></div>'
            % (name, NOTES[name], cell(src, 20), cell(src, 20, "#f3f3f3"), cell(src, 40, "#f3f3f3"), cell(src, 128)))


def tree_mock(t, p):
    """How the pair reads in a feature list."""
    def line(icon, label):
        return '<div class="tl"><img src="%s" width="20" height="20"><span>%s</span></div>' % (icon, label)
    return ('<div class="tree">' + line("../../onshape_reference/fillet-button.svg", "Block A")
            + line(t + ".svg", "Case template 1") + line("../../onshape_reference/query-variable-button.svg", "#bossEdges")
            + line("../../onshape_reference/fillet-button.svg", "Boss edges") + line(p + ".svg", "Case pattern 1") + '</div>')


html = ['<!doctype html><html><head><meta charset="utf-8"><title>Case Pattern icons</title><style>',
        'body{font-family:Segoe UI,sans-serif;margin:24px;color:#333;max-width:1100px}h1{font-size:20px}h2{font-size:16px;margin-top:32px}',
        '.opt{display:flex;align-items:center;gap:18px;margin:10px 0;padding:10px;border:1px solid #e4e4e4;border-radius:6px}',
        '.name{width:230px;font-weight:600;font-size:13px}.note{width:330px;font-size:12px;color:#666}.sizes{display:flex;align-items:center;gap:18px}',
        '.pairs{display:flex;flex-wrap:wrap;gap:18px}.pair{border:1px solid #e4e4e4;border-radius:6px;padding:10px;width:200px}',
        '.pair h3{font-size:13px;margin:0 0 8px}.tree{background:#fff;border:1px solid #ddd;font-size:13px}',
        '.tl{display:flex;align-items:center;gap:8px;padding:3px 8px;border-bottom:1px solid #f0f0f0}',
        '.refs{display:flex;gap:20px;align-items:center}.refs div{text-align:center;font-size:11px;color:#777}</style></head><body>',
        '<h1>Case Pattern icon drafts</h1><p>20 px = actual feature-list size; grey = toolbar; then 2x and 128 px. '
        'Onshape reference glyphs for style comparison:</p><div class="refs">']
for r in REFS:
    html.append('<div><img src="../../onshape_reference/%s.svg" width="20" height="20"><br>%s</div>' % (r, r.replace("-button", "")))
html.append('</div><h2>Case pattern</h2>')
html += [row(n) for n in DRAFTS if n.startswith("pattern_")]
html.append('<h2>Case template</h2>')
html += [row(n) for n in DRAFTS if n.startswith("template_")]
html.append('<h2>Pairs in a feature list</h2><div class="pairs">')
for t, p, label in PAIRS:
    html.append('<div class="pair"><h3>%s</h3>%s</div>' % (label, tree_mock(t, p)))
html.append('</div></body></html>')
open(os.path.join(OUT, "review.html"), "w", newline="\n").write("\n".join(html))
print("wrote", len(DRAFTS), "drafts and", os.path.join(OUT, "review.html"))
