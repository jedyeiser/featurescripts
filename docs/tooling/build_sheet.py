"""Build a 1-2 page feature sheet PDF from an HTML source (pymupdf Story; no extra deps).

Usage (repo root):  python docs/tooling/build_sheet.py docs/sheets/case_pattern/case_pattern_sheet.html
Writes <same name>.pdf next to the html. Image src paths are relative to the repo root.
Page breaks: put class="pb" on an element.
"""
import sys
from pathlib import Path
import pymupdf

CSS = """
body { font-family: sans-serif; font-size: 8.2pt; color: #1b1f24; line-height: 1.18; }
h1 { font-size: 20pt; color: #0b4f8a; margin: 0 0 2pt 0; }
h2 { font-size: 11pt; color: #0b4f8a; margin: 6pt 0 2pt 0; border-bottom: 1px solid #b8c7d6; }
.pb { page-break-before: always; }
.sub { color: #55606b; font-size: 8pt; margin: 0 0 6pt 0; }
.lead { font-size: 10pt; margin: 4pt 0 6pt 0; }
.cap { font-size: 7.5pt; color: #55606b; margin: 1pt 0 4pt 0; }
.note { font-size: 8pt; color: #3b4651; margin: 2pt 0; }
.foot { font-size: 7.5pt; color: #55606b; margin-top: 8pt; }
code { font-family: monospace; font-size: 8pt; color: #0b4f8a; }
table { width: 100%; border-collapse: collapse; }
th { color: #0b4f8a; border-bottom: 2px solid #0b4f8a; text-align: left; padding: 2pt 4pt; font-size: 8pt; }
td { padding: 2pt 4pt; border-bottom: 1px solid #dde3e9; vertical-align: top; }
td.k { font-weight: bold; }
td.g { color: #1d6b3a; width: 50%; }
td.r { color: #8a3416; width: 50%; }
table.shots td { border: none; text-align: center; }
ol { margin: 2pt 0 2pt 12pt; padding: 0; }
"""


def build(html_path: Path) -> Path:
    root = Path(__file__).resolve().parents[2]
    html = html_path.read_text(encoding="utf-8")
    out = html_path.with_suffix(".pdf")
    story = pymupdf.Story(html=html, user_css=CSS, archive=pymupdf.Archive(str(root)))
    writer = pymupdf.DocumentWriter(str(out))
    mediabox = pymupdf.paper_rect("letter")
    where = mediabox + (40, 28, -40, -26)
    more = True
    while more:
        dev = writer.begin_page(mediabox)
        more, _ = story.place(where)
        story.draw(dev)
        writer.end_page()
    writer.close()
    return out


if __name__ == "__main__":
    p = build(Path(sys.argv[1]).resolve())
    print(p, len(pymupdf.open(str(p))), "pages")
