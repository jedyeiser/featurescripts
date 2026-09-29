"""Contact sheet of the template set: 3 rows (A4 / A3 / A2) x 6 columns (brands); each cell = the whole sheet plus
a zoom of its title block, from publish_tools/research/img/templates/set/<TAB>_<SIZE>.png (template_set.py png).
usage (repo root): PYTHONPATH=. python devtools/onshape/templates/contact_sheet.py"""
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(Path(__file__).parent))
from template_set import BRANDS, REPO, SIZES, sheet_layout  # noqa: E402

SET = REPO / "publish_tools" / "research" / "img" / "templates" / "set"
OUT = REPO / "publish_tools" / "research" / "img" / "templates" / "template_set.png"
CW, PAD, HEAD = 560, 16, 34
try:
    FONT = ImageFont.truetype("arial.ttf", 20)
except OSError:
    FONT = ImageFont.load_default()

cells = {}
for size in SIZES:
    W, H = sheet_layout(size)["sheet"]
    tx, ty, tw, th = sheet_layout(size)["title_block"]
    for brand, b in BRANDS.items():
        p = SET / ("%s_%s.png" % (b["tab"].replace(" ", "_"), size))
        if not p.exists():
            continue
        im = Image.open(p).convert("RGB")
        k = im.size[0] / W
        whole = im.resize((CW, int(CW * H / W)), Image.LANCZOS)
        tb = im.crop((int((tx - 2) * k), int((H - ty - th - 2) * k), int((tx + tw + 2) * k), int((H - ty + 2) * k)))
        tb = tb.resize((CW, int(CW * tb.size[1] / tb.size[0])), Image.LANCZOS)
        cells[(size, brand)] = (whole, tb)

row_h = max(w.size[1] + t.size[1] for w, t in cells.values()) + HEAD + PAD
sheet = Image.new("RGB", (PAD + len(BRANDS) * (CW + PAD), PAD + len(SIZES) * row_h), "white")
d = ImageDraw.Draw(sheet)
for r, size in enumerate(SIZES):
    for col, brand in enumerate(BRANDS):
        x, y = PAD + col * (CW + PAD), PAD + r * row_h
        d.text((x, y + 6), "%s - %s" % (BRANDS[brand]["tab"], size), fill="black", font=FONT)
        if (size, brand) not in cells:
            d.text((x, y + 60), "(missing)", fill="red", font=FONT)
            continue
        whole, tb = cells[(size, brand)]
        sheet.paste(whole, (x, y + HEAD))
        d.rectangle((x, y + HEAD, x + whole.size[0] - 1, y + HEAD + whole.size[1] - 1), outline=(180, 180, 180))
        sheet.paste(tb, (x, y + HEAD + whole.size[1] + 4))
sheet.save(OUT)
print(OUT, sheet.size, len(cells), "templates")
