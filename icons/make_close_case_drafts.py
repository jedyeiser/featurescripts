"""Draft icons for Close case (Case Pattern v2: Define case / Close case / Case pattern) and a review page.

Close case shared Define case's icon (the former Case template's "( ) around a table"), which made the
two ends of the bracket look alike in the tree. Same style as make_case_pattern_drafts.py: 20 x 20,
#333333 outline, #999999 secondary, Onshape blue #1651B0 accent, brackets cut from Onshape's
query-variable glyph.

usage (repo root): python icons/make_close_case_drafts.py -> icons/drafts/case_pattern/close_*.svg + review_close.html
"""
import os
import re

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, "drafts", "case_pattern")
os.makedirs(OUT, exist_ok=True)

DARK, GREY, BLUE = "#333333", "#999999", "#1651B0"

qv = open(os.path.join(HERE, "onshape_reference", "query-variable-button.svg")).read()
qv_path = re.search(r'd="([^"]+)"', qv).group(1)
BRACKETS = qv_path[:qv_path.index("M8.625")]
# Split the "( )" into its two subpaths. The right one starts with a relative "m" after "z", i.e.
# relative to the left subpath's start point (3.1133, 2.5).
LEFT = BRACKETS[:BRACKETS.index("zm") + 1]
right_rel = BRACKETS[BRACKETS.index("zm") + 2:]
NUM = re.compile(r"-?(?:\d+\.?\d*|\.\d+)")
m = NUM.match(right_rel)
dx = float(m.group(0)); rest = right_rel[m.end():]
m2 = NUM.match(rest)
dy = float(m2.group(0)); rest = rest[m2.end():]
RIGHT = "M%.4f %.4f%s" % (3.1133 + dx, 2.5 + dy, rest)


def svg(body):
    return '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">%s</svg>' % body


def left(color=DARK):
    return '<path d="%s" fill="%s"/>' % (LEFT, color)


def right(color=DARK):
    return '<path d="%s" fill="%s"/>' % (RIGHT, color)


def rows(x, w, color, ys=(5.4, 9.1, 12.8), h=1.8):
    return ''.join('<rect x="%.2f" y="%.2f" width="%.2f" height="%.2f" fill="%s"/>' % (x, y, w, h, color) for y in ys)


def arrow_right(x0, x1, y, color=BLUE, width=1.6):
    return ('<path d="M%.2f %.2f H%.2f" stroke="%s" stroke-width="%s" stroke-linecap="round"/>' % (x0, y, x1 - 1.2, color, width)
            + '<path d="M%.2f %.2f L%.2f %.2f L%.2f %.2f Z" fill="%s"/>' % (x1, y, x1 - 2.6, y - 2.0, x1 - 2.6, y + 2.0, color))


DEFINE_NOW = open(os.path.join(HERE, "final", "case_template_icon.svg")).read()
PATTERN_NOW = open(os.path.join(HERE, "final", "case_pattern_icon.svg")).read()

DRAFTS = {}
# A: closing bracket only, dark; the body rows (grey, already defined) end at it -- reads as ")".
DRAFTS["close_A_end_bracket"] = svg(rows(2.2, 9.6, GREY) + right())
# B: both brackets, rows grey, blue outputs arrow leaving through the right bracket.
DRAFTS["close_B_outputs_arrow"] = svg(left(GREY) + right() + rows(5.2, 5.0, DARK) + arrow_right(9.4, 16.2, 10.0))
# C: both brackets, rows grey, blue "end" bar just inside the closing bracket.
DRAFTS["close_C_end_bar"] = svg(left(GREY) + right() + rows(5.4, 6.4, GREY)
                                + '<rect x="13.0" y="4.6" width="1.9" height="10.8" rx="0.4" fill="%s"/>' % BLUE)
# D (pair): Define = "(" + blue input slots with rows; Close = rows + ")" + blue output arrow.
DRAFTS["define_D_open_bracket"] = svg(left() + rows(5.6, 2.4, BLUE) + rows(9.0, 7.6, GREY))
DRAFTS["close_D_close_bracket"] = svg(rows(1.8, 7.0, GREY) + arrow_right(9.6, 15.2, 10.0) + right())

for name, text in DRAFTS.items():
    open(os.path.join(OUT, name + ".svg"), "w", newline="\n").write(text)
open(os.path.join(OUT, "define_now.svg"), "w", newline="\n").write(DEFINE_NOW)
open(os.path.join(OUT, "pattern_now.svg"), "w", newline="\n").write(PATTERN_NOW)

OPTIONS = [
    ("Now (confusing)", "define_now", "define_now", "Close case shares Define case's icon."),
    ("A  End bracket", "define_now", "close_A_end_bracket", "Rows ending at a lone ')': the body closes here."),
    ("B  Outputs arrow", "define_now", "close_B_outputs_arrow", "Brackets with a blue arrow leaving: 'close, publish outputs'."),
    ("C  End bar", "define_now", "close_C_end_bar", "Brackets with a blue end bar: 'end of the repeated features'."),
    ("D  Open / close pair", "define_D_open_bracket", "close_D_close_bracket",
     "Define case becomes '(' + input slots, Close case rows + arrow + ')': the pair reads as ( ... ) down the tree. Changes Define case too."),
]


def tree(define, close):
    def line(icon, label, ref=False):
        src = ("../../onshape_reference/%s.svg" % icon) if ref else (icon + ".svg")
        return '<div class="tl"><img src="%s" width="20" height="20"><span>%s</span></div>' % (src, label)
    return ('<div class="tree">' + line("fillet-button", "Block A", True) + line(define, "Define case 1")
            + line("fillet-button", "Boss", True) + line("fillet-button", "Boss edges", True)
            + line("query-variable-button", "#bossFaces", True) + line(close, "Close case 1")
            + line("pattern_now", "Case pattern B") + line("pattern_now", "Case pattern C") + '</div>')


html = ['<!doctype html><html><head><meta charset="utf-8"><title>Close case icons</title><style>',
        'body{font-family:Segoe UI,sans-serif;margin:24px;color:#333}h1{font-size:20px}',
        '.opts{display:flex;flex-wrap:wrap;gap:18px}.opt{border:1px solid #e4e4e4;border-radius:6px;padding:10px;width:230px}',
        '.opt h3{font-size:13px;margin:0 0 4px}.opt p{font-size:12px;color:#666;min-height:48px;margin:0 0 8px}',
        '.big{display:flex;gap:12px;align-items:center;margin-bottom:8px}',
        '.tree{background:#fff;border:1px solid #ddd;font-size:13px}.tl{display:flex;align-items:center;gap:8px;padding:3px 8px;border-bottom:1px solid #f0f0f0}',
        '</style></head><body><h1>Close case icon options</h1><div class="opts">']
for label, d, cl, note in OPTIONS:
    html.append('<div class="opt"><h3>%s</h3><p>%s</p><div class="big"><img src="%s.svg" width="48"><img src="%s.svg" width="48"></div>%s</div>'
                % (label, note, d, cl, tree(d, cl)))
html.append('</div></body></html>')
open(os.path.join(OUT, "review_close.html"), "w", newline="\n").write("\n".join(html))
print("RIGHT starts", RIGHT[:40])
print("wrote", os.path.join(OUT, "review_close.html"))


# ---------------- 2026-09-26: curly-brace pair (user: "case:{ (var), (var) }") ----------------
def brace(open_brace=True, color=DARK, width=1.55):
    """A curly brace drawn as a stroke, matching the query-variable brackets' height (y 2.5..17.5)."""
    if open_brace:
        d = "M5.2 2.6 C3.2 2.6 3.4 4.2 3.4 6.2 C3.4 8.4 3.0 9.6 1.6 10 C3.0 10.4 3.4 11.6 3.4 13.8 C3.4 15.8 3.2 17.4 5.2 17.4"
    else:
        d = "M14.8 2.6 C16.8 2.6 16.6 4.2 16.6 6.2 C16.6 8.4 17.0 9.6 18.4 10 C17.0 10.4 16.6 11.6 16.6 13.8 C16.6 15.8 16.8 17.4 14.8 17.4"
    return '<path d="%s" fill="none" stroke="%s" stroke-width="%s" stroke-linecap="round" stroke-linejoin="round"/>' % (d, color, width)


BRACE = {
    "define_E_open_brace": svg(brace(True) + rows(6.2, 2.4, BLUE) + rows(9.6, 7.4, GREY)),
    "close_E_close_brace": svg(rows(2.0, 6.6, GREY) + arrow_right(9.4, 14.6, 10.0) + brace(False)),
    # F: Define shows its inputs as little query-variable "( )" pills -- literally { (var) (var) (var) }.
    "define_F_brace_vars": svg(brace(True) + ''.join(
        '<path d="M7.4 %.2f a1.5 1.5 0 0 0 0 2.4 M10.6 %.2f a1.5 1.5 0 0 1 0 2.4" fill="none" stroke="%s" stroke-width="1.1" stroke-linecap="round"/>'
        '<rect x="8.3" y="%.2f" width="1.4" height="1.2" fill="%s"/>' % (y, y, BLUE, y + 0.6, BLUE) for y in (4.0, 8.8, 13.6))
        + rows(12.4, 4.4, GREY, ys=(4.6, 9.4, 14.2), h=1.2)),
}
for name, text in BRACE.items():
    open(os.path.join(OUT, name + ".svg"), "w", newline="\n").write(text)

OPTIONS += [
    ("E  Brace pair { ... }", "define_E_open_brace", "close_E_close_brace",
     "Define '{' + input slots, Close rows + arrow + '}'. Case pattern keeps ( ) = the call: define { body } -> case(B)."),
    ("F  { (var) (var) (var) } + E close", "define_F_brace_vars", "close_E_close_brace",
     "Define shows its inputs as little ( ) query-variable pills inside the brace. Busier at 20 px."),
    ("D  (for comparison) ( ... )", "define_D_open_bracket", "close_D_close_bracket", "The parenthesis pair from before."),
]
html = [h for h in html if not h.startswith('<div class="opt">') and h != '</div></body></html>']
for label, d, cl, note in OPTIONS[5:]:
    html.append('<div class="opt"><h3>%s</h3><p>%s</p><div class="big"><img src="%s.svg" width="48"><img src="%s.svg" width="48"></div>%s</div>'
                % (label, note, d, cl, tree(d, cl)))
html.append('</div></body></html>')
open(os.path.join(OUT, "review_braces.html"), "w", newline="\n").write("\n".join(html))
print("wrote review_braces.html")
