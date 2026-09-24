"""Build a preview sheet of every draft/final icon: actual size (20 px), 2x on a toolbar-grey strip, and 64 px.

Writes .playwright-mcp/preview.js -- a Playwright snippet that renders the sheet to .playwright-mcp/icon_preview.png
(the browser tool cannot open file:// URLs, so the SVGs are inlined). Run from the repo root:
    python icons/make_preview.py
"""
import glob
import json
import os
import re


def svg_text(path, size):
    s = open(path, encoding="utf-8", errors="replace").read()
    s = re.sub(r"<\?xml[^>]*\?>|<!DOCTYPE[^>]*>", "", s).strip()
    # force the displayed size, keep the viewBox
    s = re.sub(r'(<svg\b[^>]*?)\swidth="[^"]*"', r"\1", s, count=1)
    s = re.sub(r'(<svg\b[^>]*?)\sheight="[^"]*"', r"\1", s, count=1)
    return s.replace("<svg", '<svg width="%d" height="%d"' % (size, size), 1)


def row(path):
    name = os.path.relpath(path, "icons").replace("\\", "/")[:-4]
    return ('<div class="r"><div class="c">%s</div><div class="c bar">%s</div><div class="c bar">%s</div>'
            '<div class="c">%s</div><div class="n">%s</div></div>'
            % (svg_text(path, 20), svg_text(path, 20), svg_text(path, 40), svg_text(path, 64), name))


files = sorted(glob.glob("icons/final/*.svg")) + sorted(glob.glob("icons/drafts/*.svg")) + sorted(glob.glob("icons/drafts/*/*.svg"))
html = ('<html><head><style>body{font-family:sans-serif;margin:8px;background:#fff}'
        '.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:4px 18px}'
        '.r{display:flex;align-items:center;gap:10px;border-bottom:1px solid #eee;padding:3px 0}'
        '.c{display:flex;align-items:center;justify-content:center;min-width:24px}'
        '.bar{background:#f3f3f3;padding:4px;border-radius:3px}.n{font-size:12px}</style></head><body>'
        '<div class="grid">' + "".join(row(f) for f in files) + '</div></body></html>')
js = ("async (page) => {\n  const p = await page.context().newPage();\n  await p.setViewportSize({ width: 1500, height: 800 });\n"
      "  await p.setContent(%s);\n  await p.screenshot({ path: '.playwright-mcp/icon_preview.png', fullPage: true });\n"
      "  await p.close();\n  return 'ok';\n}\n" % json.dumps(html))
open(".playwright-mcp/preview.js", "w", encoding="utf-8").write(js)
print(len(files), "icons in the sheet")
