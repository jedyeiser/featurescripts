"""Draft icons for the v3 Case feature (Define case / Case / Close case) and a preview sheet.

Same style as make_close_case_drafts.py: 20 x 20, #333333 outline, #999999 secondary, Onshape blue #1651B0 accent.
Define case = '{' + slot rows (the signature), Close case = rows + arrow + '}' (runs it). A Case is ONE call's
arguments, so the drafts show one set of values inside the call's ( ) brackets, or a slot row with a '+'.

usage (repo root): python icons/make_case_drafts.py -> icons/drafts/case_pattern/case_*.svg + case_preview.png
"""
import os
import re

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, "drafts", "case_pattern")
os.makedirs(OUT, exist_ok=True)
DARK, GREY, BLUE = "#333333", "#999999", "#1651B0"

qv = open(os.path.join(HERE, "onshape_reference", "query-variable-button.svg")).read()
BRACKETS = re.search(r'd="([^"]+)"', qv).group(1)
BRACKETS = BRACKETS[:BRACKETS.index("M8.625")]


def svg(body):
    return '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">%s</svg>' % body


def parens(color=DARK):
    return '<path d="%s" fill="%s"/>' % (BRACKETS, color)


def rect(x, y, w, h, color):
    return '<rect x="%.2f" y="%.2f" width="%.2f" height="%.2f" fill="%s"/>' % (x, y, w, h, color)


def plus(cx, cy, r=3.1):
    """Onshape-style blue plus badge on a white disc."""
    return ('<circle cx="%.2f" cy="%.2f" r="%.2f" fill="#FFFFFF"/>' % (cx, cy, r + 0.7)
            + rect(cx - r, cy - 0.8, 2 * r, 1.6, BLUE) + rect(cx - 0.8, cy - r, 1.6, 2 * r, BLUE))


ROWS = (5.4, 9.1, 12.8)
DRAFTS = {
    # A: the call's ( ) around one case's arguments: blue keys, dark values (Define case shows grey = case 1 defaults).
    "case_A_call_args": svg(parens() + ''.join(rect(5.6, y, 2.4, 1.8, BLUE) + rect(9.0, y, 5.4, 1.8, DARK) for y in ROWS)),
    # B: ( ) around ONE highlighted row among grey ones: one case of several.
    "case_B_one_of_many": svg(parens() + rect(5.6, 5.4, 8.8, 1.8, GREY) + rect(5.6, 9.1, 2.4, 1.8, BLUE)
                              + rect(9.0, 9.1, 5.4, 1.8, DARK) + rect(5.6, 12.8, 8.8, 1.8, GREY)),
    # C: Define case's slot rows (no brace) + blue plus badge: "add a case".
    "case_C_rows_plus": svg(''.join(rect(2.4, y, 2.4, 1.8, BLUE) + rect(5.8, y, 8.4, 1.8, GREY) for y in ROWS) + plus(15.0, 14.6)),
    # D: ( ) around a single blue value bar: one argument set, simplest read at 20 px.
    "case_D_single": svg(parens() + rect(5.6, 9.1, 2.4, 1.8, BLUE) + rect(9.0, 9.1, 5.4, 1.8, DARK)),
}
for name, text in DRAFTS.items():
    open(os.path.join(OUT, name + ".svg"), "w", newline="\n").write(text)

# Preview: each draft beside Define case and Close case, as a mini feature tree, at 20 px and 48 px.
final = os.path.join(HERE, "final")
row = lambda icon, label: ('<div class="tl"><img src="file:///%s" width="20">%s</div>' % (icon.replace("\\", "/"), label))
html = ['<html><body style="font-family:Segoe UI,Arial;margin:12px"><div style="display:flex;gap:18px">']
for name in DRAFTS:
    p = os.path.abspath(os.path.join(OUT, name + ".svg"))
    html.append('<div style="border:1px solid #ccc;padding:8px;width:210px"><b style="font-size:12px">%s</b><br>'
                '<img src="file:///%s" width="48" style="margin:6px 0"><div style="font-size:13px">' % (name, p.replace("\\", "/"))
                + row(os.path.abspath(os.path.join(final, "define_case_icon.svg")), "Define_Core_Bottom")
                + row(p, "Case core_bot_2d") + row(p, "Case core_top_2d")
                + row(os.path.abspath(os.path.join(final, "close_case_icon.svg")), "Close_Core_Bottom") + '</div></div>')
html.append('</div></body></html>')
page = os.path.abspath(os.path.join(OUT, "case_preview.html"))
open(page, "w", newline="\n").write("\n".join(html).replace('class="tl"', 'style="display:flex;align-items:center;gap:8px;padding:3px 4px"'))

from playwright.sync_api import sync_playwright
with sync_playwright() as pw:
    b = pw.chromium.launch()
    pg = b.new_page(viewport={"width": 960, "height": 230}, device_scale_factor=2)
    pg.goto("file:///" + page.replace("\\", "/"))
    pg.screenshot(path=os.path.join(OUT, "case_preview.png"))
    b.close()
print("wrote", os.path.join(OUT, "case_preview.png"))
