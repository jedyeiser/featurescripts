"""Station table icon drafts (2026-09-28) + review page. usage: python icons/drafts/publish_tools/make_station_table.py"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
G, L, B = "#333333", "#999999", "#1651B0"
HEAD = '<svg xmlns="http://www.w3.org/2000/svg" width="{s}" height="{s}" viewBox="0 0 20 20">'

DRAFTS = {
    # A: a table whose first column is the station ticks (blue), rows of values in grey.
    "A": (f'<rect x="2" y="3" width="16" height="14" rx="1" fill="#FFFFFF" stroke="{G}" stroke-width="1.3"/>'
          f'<path d="M2 7 H18" stroke="{G}" stroke-width="1.3"/>'
          f'<path d="M7.5 3 V17" stroke="{L}" stroke-width="1"/>'
          f'<path d="M4.75 9 V15.5" stroke="{B}" stroke-width="1.6" stroke-linecap="round"/>'
          f'<path d="M9.5 10 H16 M9.5 12.5 H14.5 M9.5 15 H15.5" stroke="{L}" stroke-width="1.2" stroke-linecap="round"/>',
          "Table frame; the station column holds a blue station line, value rows grey."),
    # B: the Station geometry outline (small, top) over a 3-row table; blue station lines drop into the rows.
    "B": (f'<path d="M2 3.5 C5 2.5 7 4.5 10 4.5 C13 4.5 15 2.5 18 3.5 M2 8.5 C5 9.5 7 7.5 10 7.5 C13 7.5 15 9.5 18 8.5" fill="none" stroke="{G}" stroke-width="1.3" stroke-linecap="round"/>'
          f'<path d="M5 3 V9 M10 4.5 V7.5 M15 3 V9" stroke="{B}" stroke-width="1.5" stroke-linecap="round"/>'
          f'<rect x="2" y="11" width="16" height="7" rx="0.8" fill="#FFFFFF" stroke="{G}" stroke-width="1.2"/>'
          f'<path d="M2 14.5 H18 M7.3 11 V18 M12.7 11 V18" stroke="{L}" stroke-width="1"/>',
          "Station geometry glyph (outline + stations) above a small grid: 'the table of those stations'."),
    # C: plain 3x3 grid, header row dark, the three columns headed by blue station ticks.
    "C": (f'<rect x="2" y="2.5" width="16" height="15" rx="1" fill="#FFFFFF" stroke="{G}" stroke-width="1.3"/>'
          f'<rect x="2" y="2.5" width="16" height="4.5" rx="1" fill="{G}"/>'
          f'<path d="M7.3 7 V17.5 M12.7 7 V17.5 M2 10.5 H18 M2 14 H18" stroke="{L}" stroke-width="1"/>'
          f'<path d="M4.65 3.5 V6 M10 3.5 V6 M15.35 3.5 V6" stroke="#FFFFFF" stroke-width="1.4" stroke-linecap="round"/>',
          "Grid with a dark header; white station ticks in the header, one column per value."),
}


def svg(body, s=20):
    return HEAD.format(s=s) + body + "</svg>"


cards = []
for k, (body, desc) in DRAFTS.items():
    open(os.path.join(HERE, f"station_table_{k}.svg"), "w", encoding="ascii").write(svg(body))
    cards.append(f"<div class='card'><div class='lab'>{k}</div><div class='big'>{svg(body, 120)}</div>"
                 f"<div class='sizes'><span>{svg(body, 40)}</span><span>{svg(body)}</span><span class='dark'>{svg(body)}</span></div>"
                 f"<div class='tree'><span class='ic'>{svg(body)}</span>Station table</div><div class='desc'>{desc}</div></div>")
ref = "".join(f"<div class='tree'><span class='ic'>{open(os.path.join(HERE, '..', '..', 'final', n + '_icon.svg')).read()}</span>{t}</div>"
              for n, t in (("station_definition", "Station definition 1"), ("station_geometry", "Station geometry 1")))
page = ("<!doctype html><html><head><meta charset='utf-8'><title>Station table icon</title><style>"
        "body{font-family:Segoe UI,Arial,sans-serif;background:#f6f7f9;margin:24px;color:#222}h1{font-size:20px}h2{font-size:16px;margin:24px 0 8px}"
        ".row{display:flex;gap:16px;flex-wrap:wrap}.card{background:#fff;border:1px solid #ddd;border-radius:8px;padding:14px;width:230px}"
        ".lab{font-weight:700;font-size:18px}.big{display:flex;justify-content:center;padding:10px;border:1px dashed #e3e3e3;border-radius:6px}"
        ".sizes{display:flex;align-items:center;gap:14px;margin:10px 0}.sizes span{display:flex;padding:4px}.dark{background:#2b2d31;border-radius:4px}"
        ".tree{display:flex;align-items:center;gap:6px;font-size:13px;padding:6px 8px;border:1px solid #eee;border-radius:4px;background:#fafafa;margin-bottom:4px;width:200px}"
        ".ic{display:flex}.desc{font-size:12px;color:#555;margin-top:8px}</style></head><body>"
        "<h1>Icon options: Station table</h1><p style='font-size:13px;color:#555'>120 px, 40 px, actual 20 px (light and dark), and as a list row.</p>"
        f"<div class='row'>{''.join(cards)}</div><h2>Next to the chosen Station icons</h2>{ref}</body></html>")
open(os.path.join(HERE, "review_table.html"), "w", encoding="ascii").write(page)
print(os.path.join(HERE, "review_table.html"))
