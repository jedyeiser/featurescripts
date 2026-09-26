"""One image with every rendered slide of a deck (docs/tooling/_render/<slug>/slide-NN.png), for quick visual QA.
usage: python docs/tooling/contact_sheet.py <slug> [columns]"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw
slug = sys.argv[1]; cols = int(sys.argv[2]) if len(sys.argv) > 2 else 3
files = sorted((Path(__file__).parent / "_render" / slug).glob("slide-*.png"))
w, h = 800, 450
rows = (len(files) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h), "white")
for k, f in enumerate(files):
    im = Image.open(f).convert("RGB").resize((w - 8, h - 8))
    x, y = (k % cols) * w + 4, (k // cols) * h + 4
    sheet.paste(im, (x, y))
    ImageDraw.Draw(sheet).rectangle([x, y, x + w - 9, y + h - 9], outline="#999999")
out = Path(__file__).parent / "_render" / (slug + "_sheet.png")
sheet.save(out); print(out)
