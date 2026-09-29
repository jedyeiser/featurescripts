"""Export primitive + Primitive tables icon drafts (2026-09-28) + review page.
usage: python icons/drafts/publish_tools/make_primitive_drafts.py [--png]
--png also renders review_primitive.png (1x) and review_primitive@4x.png via Playwright (Chromium)."""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
G, L, B, W = "#333333", "#999999", "#1651B0", "#FFFFFF"
HEAD = '<svg xmlns="http://www.w3.org/2000/svg" width="{s}" height="{s}" viewBox="0 0 20 20">'


def st(color, w, extra=""):
    return f'fill="none" stroke="{color}" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round"{extra}'


# Reusable band glyphs, each drawn into a horizontal strip [x0, x1] x [y0, y1].
def baseline(x0, x1, y0, y1, w=1.3):
    # flat running length with a short tail rise (left) and a longer tip rise (right)
    rise = min(1.6, y1 - y0)
    yb = y1
    return (f'<path d="M{x0} {yb - rise * 0.6} C{x0 + 0.5} {yb} {x0 + 1} {yb} {x0 + 1.8} {yb} '
            f'H{x1 - 2.6} C{x1 - 1.3} {yb} {x1 - 0.5} {yb - 0.3} {x1} {yb - rise}" {st(G, w)}/>')


def profile(x0, x1, y0, y1, w=1.1):
    # thin ski side view: top + bottom surfaces, thick underfoot, meeting at tail (left) and upturned tip (right)
    yb = y1
    t = min(1.4, (y1 - y0) * 0.5)
    rise = min(1.6, y1 - y0)
    yt = yb - t
    return (f'<path d="M{x0} {yb - rise * 0.6} C{x0 + 0.6} {yt} {x0 + 1.5} {yt} {x0 + 3} {yt} H{x1 - 4} '
            f'C{x1 - 2} {yt} {x1 - 0.8} {yt - 0.2} {x1} {yb - rise} '
            f'C{x1 - 0.5} {yb - 0.3} {x1 - 1.3} {yb} {x1 - 2.6} {yb} H{x0 + 1.8} '
            f'C{x0 + 1} {yb} {x0 + 0.5} {yb} {x0} {yb - rise * 0.6} Z" fill="{W}" stroke="{G}" stroke-width="{w}" stroke-linejoin="round"/>')


def footprint(x0, x1, y0, y1, w=1.3, waist=0.14, color=G, fill=W):
    # plan outline: widest near the tip (right), narrow waist, slightly narrower tail (left)
    h = y1 - y0
    ym = (y0 + y1) / 2
    wt, wb = y0 + h * waist, y1 - h * waist  # waist edges
    tt, tb = y0 + h * 0.06, y1 - h * 0.06  # tail slightly narrower than the tip
    r = min(1.6, h / 2)
    return (f'<path d="M{x0} {ym} C{x0} {tt} {x0 + r * 0.5} {tt} {x0 + 1.5 * r} {tt} '
            f'C{x0 + (x1 - x0) * 0.4} {wt} {x0 + (x1 - x0) * 0.6} {wt} {x1 - 2 * r} {y0} '
            f'C{x1 - r * 0.4} {y0} {x1} {y0 + h * 0.2} {x1} {ym} '
            f'C{x1} {y1 - h * 0.2} {x1 - r * 0.4} {y1} {x1 - 2 * r} {y1} '
            f'C{x0 + (x1 - x0) * 0.6} {wb} {x0 + (x1 - x0) * 0.4} {wb} {x0 + 1.5 * r} {tb} '
            f'C{x0 + r * 0.5} {tb} {x0} {tb} {x0} {ym} Z" fill="{fill}" stroke="{color}" stroke-width="{w}" stroke-linejoin="round"/>')


def radius(x0, x1, y0, y1, w=1.3, zero=True, color=B):
    # step chart about a grey zero line
    ym = (y0 + y1) / 2
    n = 5
    dx = (x1 - x0) / n
    lv = [ym + (y1 - ym) * 0.9, y0, ym - (ym - y0) * 0.35, y1, y0 + (ym - y0) * 0.3]
    d = f"M{x0:.2f} {lv[0]:.2f}"
    for i in range(n):
        if i:
            d += f" V{lv[i]:.2f}"
        d += f" H{x0 + dx * (i + 1):.2f}"
    z = f'<path d="M{x0} {ym} H{x1}" {st(L, 0.9)}/>' if zero else ""
    return z + f'<path d="{d}" {st(color, w)}/>'


EXPORT = {
    # A: the composite itself, literal: four bands top to bottom.
    "A": (baseline(1.5, 18.5, 1.8, 4.2)
          + profile(1.5, 18.5, 5.6, 8.4)
          + footprint(1.5, 18.5, 9.8, 14.2, w=1.2)
          + radius(1.5, 18.5, 15.8, 18.6, w=1.2),
          "Literal: the four bands stacked -- baseline, side profile, footprint, blue radius steps."),
    # B: the two most recognisable bands, big: footprint over the blue radius plot.
    "B": (footprint(1.8, 18.2, 2, 10.6, w=1.4, waist=0.22)
          + radius(1.8, 18.2, 13, 18.4, w=1.5),
          "Simplified: large footprint outline over a blue radius step plot."),
    # C: the four bands, shorter, with a blue export arrow leaving to the right.
    "C": (baseline(1.3, 13, 1.8, 4.2, w=1.2)
          + profile(1.3, 13, 5.6, 8.4, w=1.0)
          + footprint(1.3, 13, 9.8, 14.2, w=1.1)
          + radius(1.3, 13, 15.8, 18.6, w=1.1, color=G)
          + f'<path d="M14.6 10 H18.6 M16.6 7.8 L18.8 10 L16.6 12.2" {st(B, 1.6)}/>',
          "Four bands in grey/dark with a blue 'export' arrow pointing out to the right."),
}

TABLES = {
    # A: same family as the chosen Station table (glyph over a grid): radius plot over a grid.
    "A": (radius(2, 18, 2.2, 8.4, w=1.5)
          + f'<rect x="2" y="11" width="16" height="7" rx="0.8" fill="{W}" stroke="{G}" stroke-width="1.2"/>'
          + f'<path d="M2 14.5 H18 M7.3 11 V18 M12.7 11 V18" stroke="{L}" stroke-width="1"/>',
          "Station-table family: blue radius step plot above a small grid."),
    # B: one row per band; the first column holds a mini glyph of that band, values grey.
    "B": (f'<rect x="1.5" y="1.5" width="17" height="17" rx="1" fill="{W}" stroke="{G}" stroke-width="1.2"/>'
          + f'<path d="M8.5 1.5 V18.5 M1.5 5.75 H18.5 M1.5 10 H18.5 M1.5 14.25 H18.5" stroke="{L}" stroke-width="0.8"/>'
          + baseline(2.6, 7.4, 2.6, 4.7, w=0.9)
          + profile(2.6, 7.4, 6.8, 9.0, w=0.8)
          + footprint(2.6, 7.4, 11.1, 13.2, w=0.9, waist=0.25)
          + radius(2.6, 7.4, 15.3, 17.4, w=0.9, zero=False)
          + f'<path d="M10.3 3.6 H16.7 M10.3 7.9 H15.3 M10.3 12.1 H16.2 M10.3 16.4 H14.8" {st(L, 1.1)}/>',
          "Four-row table: each row starts with its band glyph (baseline, profile, footprint, blue radius)."),
    # C: plural 'tables': two stacked tables, the front one carrying the blue radius steps.
    "C": (f'<rect x="5" y="1.5" width="13.5" height="11" rx="0.8" fill="{W}" stroke="{L}" stroke-width="1.1"/>'
          + f'<path d="M5 4.5 H18.5" stroke="{L}" stroke-width="1.1"/>'
          + f'<rect x="1.5" y="6" width="13.5" height="12.5" rx="0.8" fill="{W}" stroke="{G}" stroke-width="1.2"/>'
          + f'<rect x="1.5" y="6" width="13.5" height="3.3" rx="0.8" fill="{G}"/>'
          + f'<path d="M1.5 12.4 H15 M1.5 15.5 H15 M6 9.3 V18.5 M10.5 9.3 V18.5" stroke="{L}" stroke-width="0.8"/>'
          + radius(2.6, 13.9, 10.3, 17.5, w=1.4, zero=False),
          "Two stacked tables (dark header on the front one) with the blue radius steps over its grid."),
}


def svg(body, s=20):
    return HEAD.format(s=s) + body + "</svg>"


def card(k, body, desc, label):
    return (f"<div class='card'><div class='lab'>{k}</div><div class='big'>{svg(body, 120)}</div>"
            f"<div class='sizes'><span>{svg(body, 40)}</span><span>{svg(body)}</span><span class='dark'>{svg(body)}</span></div>"
            f"<div class='tree'><span class='ic'>{svg(body)}</span>{label}</div><div class='desc'>{desc}</div></div>")


sections = []
for prefix, label, drafts in (("export_primitive", "Export primitive 1", EXPORT), ("primitive_tables", "Primitive tables", TABLES)):
    cards = []
    for k, (body, desc) in drafts.items():
        open(os.path.join(HERE, f"{prefix}_{k}.svg"), "w", encoding="ascii").write(svg(body))
        cards.append(card(k, body, desc, label))
    sections.append(f"<h2>{label.rstrip(' 1')}</h2><div class='row'>{''.join(cards)}</div>")

ref = "".join(f"<div class='tree'><span class='ic'>{open(os.path.join(HERE, '..', '..', 'final', n + '_icon.svg')).read()}</span>{t}</div>"
              for n, t in (("station_definition", "Station definition 1"), ("station_geometry", "Station geometry 1"),
                           ("station_table", "Station table")))
page = ("<!doctype html><html><head><meta charset='utf-8'><title>Primitive icons</title><style>"
        "body{font-family:Segoe UI,Arial,sans-serif;background:#f6f7f9;margin:24px;color:#222;width:860px}h1{font-size:20px}h2{font-size:16px;margin:24px 0 8px}"
        ".row{display:flex;gap:16px;flex-wrap:wrap}.card{background:#fff;border:1px solid #ddd;border-radius:8px;padding:14px;width:230px}"
        ".lab{font-weight:700;font-size:18px}.big{display:flex;justify-content:center;padding:10px;border:1px dashed #e3e3e3;border-radius:6px}"
        ".sizes{display:flex;align-items:center;gap:14px;margin:10px 0}.sizes span{display:flex;padding:4px}.dark{background:#2b2d31;border-radius:4px}"
        ".tree{display:flex;align-items:center;gap:6px;font-size:13px;padding:6px 8px;border:1px solid #eee;border-radius:4px;background:#fafafa;margin-bottom:4px;width:200px}"
        ".ic{display:flex}.desc{font-size:12px;color:#555;margin-top:8px}</style></head><body>"
        "<h1>Icon options: Export primitive + Primitive tables</h1><p style='font-size:13px;color:#555'>120 px, 40 px, actual 20 px (light and dark), and as a list row.</p>"
        f"{''.join(sections)}<h2>Next to the installed Station icons</h2>{ref}</body></html>")
html = os.path.join(HERE, "review_primitive.html")
open(html, "w", encoding="ascii").write(page)
print(html)

if "--png" in sys.argv:
    from playwright.sync_api import sync_playwright
    with sync_playwright() as p:
        br = p.chromium.launch()
        for scale, name in ((1, "review_primitive.png"), (4, "review_primitive@4x.png")):
            pg = br.new_page(viewport={"width": 910, "height": 600}, device_scale_factor=scale)
            pg.goto("file:///" + html.replace("\\", "/"))
            pg.screenshot(path=os.path.join(HERE, name), full_page=True)
            print(os.path.join(HERE, name))
        br.close()
