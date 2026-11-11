"""Convert a docs/explainers/<x>/<x>_explained.md into a designer-facing PDF (PyMuPDF Story, HTML + CSS).

usage (repo root):
    python docs/tooling/build_explainer_pdf.py docs/explainers/deform/deform_explained.md
        [--out file.pdf] [--footer "Deform, explained - engine V33"] [--eq img/eq_map.png] [--a4]
        [--render-dir docs/tooling/_render/deform_explained]    (also writes one PNG per page, for checking)

Markdown subset handled: headings (first '# ' is the title, later '# ' are sections, '## ' subsections), paragraphs,
bullet / numbered lists (with indented continuation lines), tables, images (alt text becomes the caption),
horizontal rules (dropped; sections already start with a heading), **bold**, *italic*, `code`, <sub>/<sup>,
and a $$ ... $$ display block, which is replaced by a pre-rendered equation image (--eq).
The first page holds title, intro, summary and a contents list; headings are never left at the foot of a page
(a first layout pass finds them, a second pass forces a page break before them).
"""
import argparse
import html
import os
import re
import sys

import pymupdf

ACCENT = "#2a78d6"
INK, INK2, MUTED, GRID = "#1b1b1a", "#52514e", "#9a9893", "#d9d6cf"
TBLROW = {}  # table (chunk) id -> id of its first row
HEADTXT = {}  # heading id -> heading text
ROWPREV = {} # table row id -> id of the row above it (None for the first row)
ORDER, PREVHEAD = [], {} # heading / figure ids in document order; figure id -> id of the heading right above it
FIGS = {} # figure id -> (pixel width, pixel height)
TEXTW = 488 # text width in pt; set from the page size in main()
FONT_DIR = r"C:\Windows\Fonts"
FONTS = {  # family, weight/style -> file
    ("body", "normal", "normal"): "segoeui.ttf", ("body", "bold", "normal"): "segoeuib.ttf",
    ("body", "normal", "italic"): "segoeuii.ttf", ("body", "bold", "italic"): "segoeuiz.ttf",
    ("mono", "normal", "normal"): "consola.ttf", ("mono", "bold", "normal"): "consolab.ttf",
    ("mono", "normal", "italic"): "consolai.ttf", ("mono", "bold", "italic"): "consolaz.ttf",
}


# ------------------------------------------------------------------ inline
def inline(s):
    codes = []

    def keep(m):
        codes.append("<code>%s</code>" % html.escape(m.group(1), quote=False))
        return "\x00%d\x00" % (len(codes) - 1)

    s = re.sub(r"`([^`]+)`", keep, s)
    s = html.escape(s, quote=False)
    s = re.sub(r"&lt;(/?)(sub|sup)&gt;", r"<\1\2>", s)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s, flags=re.S)
    s = re.sub(r"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", r"<i>\1</i>", s, flags=re.S)
    s = re.sub(r"\x00(\d+)\x00", lambda m: codes[int(m.group(1))], s)
    return s


# ------------------------------------------------------------------ blocks
def parse(md, eq_img):
    lines = md.replace("\r\n", "\n").split("\n")
    blocks, i, n = [], 0, len(lines)
    while i < n:
        ln = lines[i]
        if not ln.strip():
            i += 1
            continue
        if ln.strip() == "---":
            blocks.append(("hr",))
            i += 1
            continue
        m = re.match(r"^(#{1,3})\s+(.*)$", ln)
        if m:
            blocks.append(("h", len(m.group(1)), m.group(2).strip()))
            i += 1
            continue
        if ln.strip().startswith("$$"):
            body = [ln.strip()]
            while not (len(body) > 1 or body[0].count("$$") >= 2) or (body[0].count("$$") < 2 and not body[-1].rstrip().endswith("$$")):
                i += 1
                body.append(lines[i].strip())
            blocks.append(("eq", eq_img))
            i += 1
            continue
        m = re.match(r"^!\[(.*?)\]\((.*?)\)\s*$", ln.strip())
        if m:
            blocks.append(("img", m.group(1), m.group(2)))
            i += 1
            continue
        if ln.lstrip().startswith("|"):
            rows = []
            while i < n and lines[i].lstrip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            rows = [r for r in rows if not all(re.fullmatch(r":?-{2,}:?", c) for c in r)]
            blocks.append(("table", rows))
            continue
        m = re.match(r"^(\s*)([-*]|\d+\.)\s+(.*)$", ln)
        if m:
            ordered = m.group(2)[0].isdigit()
            items = []
            while i < n:
                m = re.match(r"^([-*]|\d+\.)\s+(.*)$", lines[i])
                if m:
                    items.append(m.group(2))
                    i += 1
                elif lines[i].startswith("  ") and lines[i].strip() and items:
                    items[-1] += " " + lines[i].strip()
                    i += 1
                else:
                    break
            blocks.append(("ol" if ordered else "ul", items))
            continue
        para = []
        while i < n and lines[i].strip() and not re.match(r"^(#{1,3}\s|---$|!\[|\||\$\$)", lines[i].strip()) \
                and not re.match(r"^([-*]|\d+\.)\s", lines[i]):
            para.append(lines[i].strip())
            i += 1
        blocks.append(("p", " ".join(para)))
    return blocks


def to_html(blocks, mddir, splits=frozenset(), defer=None):
    """Return (html, heading ids). `splits` = row ids before which a table is cut and its header repeated;
    `defer` = {figure id: number of following text blocks to place before the figure}."""
    ORDER.clear()
    PREVHEAD.clear()
    ROWPREV.clear()
    TBLROW.clear()
    HEADTXT.clear()
    defer = defer or {}
    pending = []                      # [figure id, text blocks still to emit before it, html]
    out, hid, fig, tbl = [], 0, 0, 0

    def flush():
        for fid_, _, fh_ in pending:
            ORDER.append(fid_)
            out.append(fh_)
        pending.clear()

    heads = []
    last_head = None
    title_done = False
    toc_done = False
    first_h1_idx = next(k for k, b in enumerate(blocks) if b[0] == "h" and b[1] == 1)
    sections = [(b[2]) for b in blocks if b[0] == "h" and b[1] == 1][1:]
    for k, b in enumerate(blocks):
        t = b[0]
        if t == "img" or (t == "h" and b[1] == 1):
            flush()                    # the next figure or the next numbered section ends any deferral
        if t == "hr":
            if not toc_done and title_done:
                toc_done = True
                out.append('<div class="toc"><p class="tochead">Contents</p><ul>')
                for j, s in enumerate(sections):
                    out.append("<li>%s</li>" % inline(s))
                out.append("</ul></div>")
            continue
        if t == "h":
            level, text = b[1], b[2]
            if level == 1 and not title_done:
                title_done = True
                out.append('<h1 class="title">%s</h1>' % inline(text))
                continue
            hid += 1
            tag = "h2" if level == 1 else "h3"
            heads.append("hd%d" % hid)
            HEADTXT["hd%d" % hid] = text
            ORDER.append("hd%d" % hid)
            if last_head and last_head[1] == k - 1:
                PREVHEAD["hd%d" % hid] = last_head[0]
            last_head = ("hd%d" % hid, k)
            style = ""   # "Under the hood" flows on after section 7 (a forced page break left a page 60% blank)
            out.append('<%s id="hd%d"%s>%s</%s>' % (tag, hid, style, inline(text), tag))
        elif t == "p":
            cls = ""
            if k <= first_h1_idx + 3 and not toc_done:
                cls = ' class="lead"' if b[1].startswith("*") else ' class="summary"'
            out.append("<p%s>%s</p>" % (cls, inline(b[1])))
        elif t in ("ul", "ol"):
            out.append("<%s>%s</%s>" % (t, "".join("<li>%s</li>" % inline(x) for x in b[1]), t))
        elif t == "table":
            rows = b[1]
            tbl += 1
            heads_html = "".join("<th>%s</th>" % inline(c) for c in rows[0])
            head = lambda tid: '<table id="%s"><thead><tr>%s</tr></thead><tbody>' % (tid, heads_html)
            h, prev = head("tb%d_0" % tbl), None
            ORDER.append("tb%d_0" % tbl)
            TBLROW["tb%d_0" % tbl] = "tr%d_0" % tbl
            if last_head and last_head[1] == k - 1:
                PREVHEAD["tb%d_0" % tbl] = last_head[0]
            for ri, r in enumerate(rows[1:]):
                rid = "tr%d_%d" % (tbl, ri)
                if rid in splits:
                    h += "</tbody></table>" + head("tb" + rid)
                    ORDER.append("tb" + rid)
                    TBLROW["tb" + rid] = rid
                h += '<tr id="%s">%s</tr>' % (rid, "".join("<td>%s</td>" % inline(c) for c in r))
                ORDER.append(rid)
                ROWPREV[rid] = prev
                prev = rid
            out.append(h + "</tbody></table>")
        elif t == "img":
            fig += 1
            if last_head and last_head[1] == k - 1:
                PREVHEAD["fig%d" % fig] = last_head[0]
            fw = TEXTW
            try:
                pm = pymupdf.Pixmap(os.path.join(mddir, b[2]))
                fw = int(min(TEXTW, pm.width * 72.0 / (pm.xres or 96)))
                FIGS["fig%d" % fig] = (pm.width, pm.height, fw)
            except Exception:
                pass
            fh_ = ('<div class="fig" id="fig%d"><img width="%d" src="%s"><p class="cap"><b>Figure %d.</b> %s</p></div>'
                   % (fig, fw, html.escape(b[2]), fig, inline(b[1])))
            if defer.get("fig%d" % fig, 0) > 0:
                pending.append(["fig%d" % fig, defer["fig%d" % fig], fh_])
            else:
                ORDER.append("fig%d" % fig)
                out.append(fh_)
        elif t == "eq":
            fw = TEXTW
            try:
                pm = pymupdf.Pixmap(os.path.join(mddir, b[1]))
                fw = int(min(TEXTW, pm.width * 72.0 / (pm.xres or 96)))
            except Exception:
                pass
            out.append('<div class="eq"><img width="%d" src="%s"></div>' % (fw, html.escape(b[1])))
        if t in ("p", "ul", "ol", "table", "eq") and pending:
            for pe in pending:
                pe[1] -= 1
            done = [pe for pe in pending if pe[1] <= 0]
            for pe in done:
                ORDER.append(pe[0])
                out.append(pe[2])
                pending.remove(pe)
    flush()
    return "\n".join(out), heads


CSS = """
body { font-family: body; font-size: 10.5pt; line-height: 1.4; color: %(ink)s; margin: 0; }
p { margin: 0 0 7pt 0; }
h1.title { font-size: 30pt; line-height: 1.05; color: %(acc)s; margin: 0 0 10pt 0; font-weight: bold; }
h2 { font-size: 17pt; color: %(acc)s; margin: 20pt 0 6pt 0; padding-bottom: 3pt; border-bottom: 1.2pt solid %(acc)s; font-weight: bold; }
h3 { font-size: 12.5pt; color: %(acc)s; margin: 13pt 0 4pt 0; font-weight: bold; }
.lead { font-size: 10pt; color: %(ink2)s; margin-bottom: 18pt; }
.summary { font-size: 12.5pt; line-height: 1.45; margin: 6pt 0 22pt 0; padding: 0 0 0 10pt; border-left: 3pt solid %(acc)s; }
.tochead { font-size: 11pt; font-weight: bold; color: %(acc)s; margin: 0 0 4pt 0; }
.toc ul { margin: 0; padding: 0; list-style-type: none; font-size: 11.5pt; line-height: 1.7; }
ul, ol { margin: 0 0 7pt 0; padding-left: 16pt; }
li { margin: 0 0 3pt 0; }
code { font-family: mono; font-size: 9.2pt; background-color: #f1efe9; }
sub, sup { font-size: 7.5pt; }
table { width: 100%%; border-collapse: collapse; margin: 6pt 0 10pt 0; font-size: 9.2pt; line-height: 1.3; }
th { text-align: left; color: %(acc)s; border-bottom: 1.4pt solid %(acc)s; padding: 3pt 5pt 3pt 0; font-weight: bold; vertical-align: top; }
td { border-bottom: 0.5pt solid %(grid)s; padding: 3pt 6pt 3pt 0; vertical-align: top; }
.fig { margin: 8pt 0 12pt 0; text-align: center; page-break-inside: avoid; }
.cap { font-size: 8.8pt; color: %(ink2)s; margin: 3pt 0 0 0; text-align: left; }
.eq { text-align: center; margin: 4pt 0 8pt 0; }
.pb { page-break-before: always; }
.brk { page-break-before: always; }
""" % dict(ink=INK, ink2=INK2, acc=ACCENT, grid=GRID)


def font_css():
    css = ""
    for (fam, wt, st), fn in FONTS.items():
        if os.path.exists(os.path.join(FONT_DIR, fn)):
            css += "@font-face { font-family: %s; font-weight: %s; font-style: %s; src: url(%s); }\n" % (fam, wt, st, fn)
    return css


def make_html(body, breaks):
    html_doc = "<html><body>%s</body></html>" % body
    for h in breaks:
        if h.startswith("fig"):
            html_doc = html_doc.replace('class="fig" id="%s"' % h, 'class="fig brk" id="%s"' % h, 1)
        elif h.startswith("tb"):
            html_doc = html_doc.replace('<table id="%s"' % h, '<div class="brk"></div><table id="%s"' % h, 1)
        else:
            html_doc = html_doc.replace('id="%s"' % h, 'id="%s" class="brk"' % h, 1)
    return html_doc


def layout(html_doc, arch, mediabox, where, tmp):
    """Lay out and write tmp; return (page count, {element id: (page index, rect)})."""
    pages_pos = {}
    story = pymupdf.Story(html=html_doc, user_css=font_css() + CSS, em=10.5, archive=arch)
    writer = pymupdf.DocumentWriter(tmp)
    more, pg = 1, 0
    while more:
        dev = writer.begin_page(mediabox)
        more, _ = story.place(where)
        cur = pg

        def rec(p):
            if p.id and (p.open_close & 1):
                pages_pos[p.id] = (cur, pymupdf.Rect(p.rect))

        story.element_positions(rec)
        story.draw(dev)
        writer.end_page()
        pg += 1
    writer.close()
    return pg, pages_pos


def first_violation(pages_pos, where, breaks, splits, defer):
    """First layout problem in document order, or None. Applies split / defer in place, returns 'split'/'defer'
    for those, or the id before which a page break is to be forced."""
    for hid_ in ORDER:
        if hid_ not in pages_pos or hid_ in breaks:
            continue
        r = pages_pos[hid_][1]
        if hid_.startswith("tb"):
            rw = TBLROW.get(hid_)
            if rw in pages_pos and pages_pos[rw][0] > pages_pos[hid_][0] and r.y0 > where.y0 + 5:
                pv = PREVHEAD.get(hid_)
                return pv if pv and pv not in breaks and pv in pages_pos and pages_pos[pv][1].y0 > where.y0 + 5 else hid_
            continue
        if hid_.startswith("tr"):
            pv = ROWPREV.get(hid_)
            if pv and pv in pages_pos and pages_pos[hid_][0] > pages_pos[pv][0] and hid_ not in splits:
                splits.add(hid_)       # this row starts a new page: cut the table here, repeat the header
                return "split"
            continue
        if r.y0 <= where.y0 + 5:
            continue
        if hid_.startswith("fig"):
            fw, fh, fdisp = FIGS[hid_]
            if r.height < fdisp * fh / fw + 12 or r.y1 > where.y1 - 40:
                if defer.get(hid_, 0) < 2 and hid_ not in PREVHEAD:
                    defer[hid_] = defer.get(hid_, 0) + 1     # let following text fill the gap, place the figure after it
                    return "defer"
                pv = PREVHEAD.get(hid_)
                return pv if pv not in breaks and pv in pages_pos and pages_pos[pv][1].y0 > where.y0 + 5 else hid_
        elif r.y1 > where.y1 - 75:
            pv = PREVHEAD.get(hid_)
            return pv if pv and pv not in breaks and pv in pages_pos and pages_pos[pv][1].y0 > where.y0 + 5 else hid_
    return None


# '(regex with one group ending right before the number, heading text it points at)'
PAGE_REFS = [(r"(glossary on page )\d+", "Glossary"), (r"(appendix[^()]*\(page )\d+", "Advanced inputs")]


def patch_refs(blocks, pages_pos):
    """Rewrite 'page N' cross references in paragraphs to the real page of the heading they point at."""
    pageof = {}
    for hid_, txt in HEADTXT.items():
        if hid_ in pages_pos:
            pageof[txt] = pages_pos[hid_][0] + 1
    out, changed = [], False
    for b in blocks:
        if b[0] == "p":
            t = b[1]
            for rx, head in PAGE_REFS:
                pg = next((p for tx, p in pageof.items() if tx.startswith(head)), None)
                if pg is not None:
                    t = re.sub(rx, lambda m: m.group(1) + str(pg), t)
            if t != b[1]:
                changed = True
            b = ("p", t)
        out.append(b)
    return out, changed


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("md")
    ap.add_argument("--out")
    ap.add_argument("--footer", default="Deform, explained - engine V33")
    ap.add_argument("--eq", default="img/eq_map.png")
    ap.add_argument("--a4", action="store_true")
    ap.add_argument("--render-dir")
    a = ap.parse_args()
    md_path = os.path.abspath(a.md)
    mddir = os.path.dirname(md_path)
    out = os.path.abspath(a.out or os.path.splitext(md_path)[0] + ".pdf")
    with open(md_path, encoding="utf-8") as f:
        md = f.read()
    global TEXTW
    arch = pymupdf.Archive()
    arch.add(pymupdf.Archive(FONT_DIR))
    arch.add(pymupdf.Archive(mddir))
    mediabox = pymupdf.paper_rect("a4" if a.a4 else "letter")
    TEXTW = int(mediabox.width - 144)
    where = pymupdf.Rect(72, 58, mediabox.x1 - 72, mediabox.y1 - 66)
    blocks = parse(md, a.eq)
    breaks, splits, defer = set(), set(), {}
    tmp = out + ".tmp.pdf"
    for outer in range(4):                      # outer: settle the layout, then fix 'page N' references, repeat
        for it in range(80):
            body, heads = to_html(blocks, mddir, frozenset(splits), defer)
            npages, pages_pos = layout(make_html(body, breaks), arch, mediabox, where, tmp)
            bad = first_violation(pages_pos, where, breaks, splits, defer)
            if os.environ.get("EXPLAINER_DEBUG"):
                print(outer, it, bad)
            if not bad:
                break
            if bad not in ("split", "defer"):
                breaks.add(bad)
        blocks, changed = patch_refs(blocks, pages_pos)
        if not changed:
            break
    # footer
    doc = pymupdf.open(tmp)
    for i, page in enumerate(doc):
        y = mediabox.y1 - 38
        page.draw_line((72, y - 12), (mediabox.x1 - 72, y - 12), color=(0.85, 0.84, 0.81), width=0.5)
        page.insert_text((72, y), a.footer, fontname="helv", fontsize=8.5, color=(0.32, 0.32, 0.3))
        num = str(i + 1)
        w = pymupdf.get_text_length(num, fontname="helv", fontsize=8.5)
        page.insert_text((mediabox.x1 - 72 - w, y), num, fontname="helv", fontsize=8.5, color=(0.32, 0.32, 0.3))
    doc.set_metadata({"title": os.path.basename(md_path), "author": ""})
    doc.save(out, garbage=3, deflate=True)
    if a.render_dir:
        os.makedirs(a.render_dir, exist_ok=True)
        for i, page in enumerate(doc):
            page.get_pixmap(dpi=80).save(os.path.join(a.render_dir, "p%02d.png" % (i + 1)))
    n = len(doc)
    doc.close()
    os.remove(tmp)
    print("wrote %s (%d pages); forced breaks before: %s; table splits: %s; deferred figures: %s"
          % (out, n, sorted(breaks), sorted(splits), defer))


if __name__ == "__main__":
    main()