# Helpers exec'd inside ui_driver.py command files (page / shot / out in scope) for the template SET work.
# Sheet mm -> screen px after "Zoom to sheet" at 1600 x 1000 with the right panel closed: every ISO landscape
# sheet fills the same 1239.4 px wide rectangle starting at (200, 961) (A3: 2.951 px/mm, measured 2026-09-29).
import time as _t

DOC = "https://k2-sports.onshape.com/documents/52b5bde03744944bff91bef9/w/81e38c962fe4d7c8250c4fc5/e/"
SHEET_W = {"A4": 297.0, "A3": 420.0, "A2": 594.0}


def mapper(size):
    k = 1239.4 / SHEET_W[size]
    return (lambda x: 200 + k * x), (lambda y: 961 - k * y)


def dframe():
    for _ in range(90):
        fr = [x for x in page.frames if 'drawing' in x.url]
        if fr:
            return fr[0]
        page.wait_for_timeout(1000)
    return None


def vis(text, exact=True, fr=None):
    fr = fr or dframe()
    loc = fr.get_by_text(text, exact=exact)
    for i in range(loc.count()):
        if loc.nth(i).is_visible():
            return loc.nth(i)
    return None


def other_session():
    """True if Onshape shows 'open in another session' (then it is CANCELLED -- never force-open)."""
    for t in ("another session", "another tab", "opened in another"):
        loc = page.get_by_text(t, exact=False)
        if loc.count() and loc.first.is_visible():
            for b in ("Cancel", "Close"):
                bl = page.get_by_text(b, exact=True)
                for i in range(bl.count()):
                    if bl.nth(i).is_visible():
                        bl.nth(i).click()
                        return True
            page.keyboard.press("Escape")
            return True
    return False


def open_tab(eid, wait=14000):
    page.goto(DOC + eid, wait_until="domcontentloaded")
    page.wait_for_timeout(wait)
    if other_session():
        out("OTHER SESSION -- cancelled", eid)
        return False
    f = dframe()
    if f is None:
        out("no drawing frame")
        return False
    page.keyboard.press("Escape")
    zoom_sheet()
    return True


def clear_sel():
    page.keyboard.press("Escape"); page.wait_for_timeout(200)


def set_size(size):
    clear_sel()
    page.mouse.click(1300, 700, button="right"); page.wait_for_timeout(1000)
    vis("Sheet properties...").click(); page.wait_for_timeout(2000)
    f = dframe()
    idx = f.evaluate("() => [...document.querySelectorAll('select')].findIndex(s => [...s.options].some(o => o.text=='A4 Portrait'))")
    f.locator("select").nth(idx).select_option(label=size); page.wait_for_timeout(1000)
    page.mouse.click(296, 90); page.wait_for_timeout(4000)      # green check of Sheet properties
    out("size ->", size)


def lock_formats(locked):
    """Drawing properties > Formats > Lock := Locked / Unlocked (panel closed again afterwards)."""
    f = dframe()
    page.mouse.click(1585, 539); page.wait_for_timeout(2500)
    page.mouse.click(1439, 137); page.wait_for_timeout(1500)     # Formats tab
    val = "Locked" if locked else "Unlocked"
    idx = f.evaluate("() => [...document.querySelectorAll('select')].findIndex(s => s.getBoundingClientRect().width>0 && [...s.options].some(o => o.text=='Locked') && [...s.options].some(o => o.text=='Unlocked'))")
    f.locator("select").nth(idx).select_option(label=val); page.wait_for_timeout(2500)
    page.mouse.click(1215, 539); page.wait_for_timeout(1500)     # close the panel
    out("formats", val)


def move_to_layer(px, py, layer="Title block"):
    f = dframe()
    page.mouse.click(px, py, button="right"); page.wait_for_timeout(1000)
    mv = vis("Move to", fr=f)
    if not mv:
        out("no Move to"); page.keyboard.press("Escape"); return False
    mv.hover(); page.wait_for_timeout(800)
    it = vis(layer, fr=f)
    it.click(timeout=5000); page.wait_for_timeout(1500)
    out("moved to", layer)
    return True


def layer_of(px, py):
    f = dframe()
    clear_sel()
    page.mouse.click(px, py); page.wait_for_timeout(500)
    page.mouse.click(px, py, button="right"); page.wait_for_timeout(900)
    mv = vis("Move to", fr=f)
    r = None
    if mv:
        mv.hover(); page.wait_for_timeout(600)
        r = f.evaluate(r"""() => [...document.querySelectorAll('*')].filter(e=>e.childElementCount==0 && ['Border frame','Border zones','Title block','Drawing'].includes((e.innerText||'').trim()) && e.getBoundingClientRect().width>0 && getComputedStyle(e).color=='rgb(153, 153, 153)').map(e=>(e.innerText||'').trim())""")
    page.keyboard.press("Escape"); page.wait_for_timeout(300)
    return r


def insert_image(blob, x0, y0, x1, y1, X, Y):
    """Insert blob image with corners (x0, y0) top-left and (x1, y1) bottom-right in sheet mm."""
    f = dframe()
    clear_sel()
    page.mouse.click(1255, 59); page.wait_for_timeout(2500)
    box = page.get_by_placeholder("Search files")
    if box.count():
        box.first.fill(blob); page.wait_for_timeout(1500)
    item = None
    for fr in [page] + list(page.frames):
        loc = fr.get_by_text(blob, exact=True)
        for i in range(loc.count()):
            if loc.nth(i).is_visible():
                item = loc.nth(i); break
        if item:
            break
    if not item:
        out("blob not listed", blob); shot("img_list_fail"); page.keyboard.press("Escape"); return False
    item.click(); page.wait_for_timeout(2500)
    page.mouse.move(X(x0), Y(y0), steps=3); page.wait_for_timeout(400)
    page.mouse.click(X(x0), Y(y0)); page.wait_for_timeout(1200)
    page.mouse.move(X(x1), Y(y1), steps=6); page.wait_for_timeout(500)
    page.mouse.click(X(x1), Y(y1)); page.wait_for_timeout(2500)
    clear_sel()
    out("image", blob)
    return True


def zoom_sheet(tries=4):
    """Context menu > Zoom to sheet, verified: the white sheet must then sit inside the canvas."""
    from PIL import Image as _I
    import io as _io
    for t in range(tries):
        clear_sel()
        page.wait_for_timeout(600)
        page.mouse.click(700, 300 + 60 * t, button="right"); page.wait_for_timeout(1200)
        z = vis("Zoom to sheet")
        if z:
            z.click(); page.wait_for_timeout(2500)
        else:
            page.keyboard.press("Escape")
        im = _I.open(_io.BytesIO(page.screenshot())).convert("RGB")
        if im.getpixel((62, 450)) != (255, 255, 255) and im.getpixel((1555, 450)) != (255, 255, 255):
            return True
    out("zoom_sheet FAILED")
    return False


def _layout(size):
    import sys as _s
    if "devtools/onshape/templates" not in _s.path:
        _s.path.insert(0, "devtools/onshape/templates")
    from sheet_layout import sheet_layout as _sl
    return _sl(size)


def format_picks(size):
    """Click points (sheet mm) on the recreated frame / centring marks (-> Border frame) and the title-block top /
    left edges + NOTES label (-> Border zones)."""
    L = _layout(size)
    x0, y0, x1, y1 = L["frame"]
    W, H = L["sheet"]
    tx, ty, tw, th = L["title_block"]
    frame = [(x0 + 30, y0), (x1, y0 + 60), (x0 + 30, y1), (x0, y0 + 60),
             (W / 2, y0 / 2), (W / 2, (y1 + H) / 2), (x0 / 2, H / 2), ((x1 + W) / 2, H / 2)]
    zones = [(tx + 110, ty + th), (tx, ty + 30), (x0 + 5.5, ty + th - 2.4)]
    return frame, zones


def pick_group(points, layer, X, Y):
    clear_sel()
    for x, y in points:
        page.mouse.click(X(x), Y(y)); page.wait_for_timeout(350)
    shot("grp_" + layer.replace(" ", "_"))
    ok = move_to_layer(X(points[0][0]), Y(points[0][1]), layer)
    clear_sel()
    return ok


def measured_mapper(size):
    """Sheet mm -> px measured from a screenshot: the sheet is the pure-white rectangle in the canvas."""
    from PIL import Image as _I
    import io as _io
    im = _I.open(_io.BytesIO(page.screenshot())).convert("RGB")
    W = SHEET_W[size]
    y_mid = 450
    row = [im.getpixel((x, y_mid)) for x in range(60, 1560)]
    xs = [60 + i for i, p in enumerate(row) if p == (255, 255, 255)]
    left, right = xs[0], xs[-1]
    col = [im.getpixel((left + 200, y)) for y in range(78, 975)]
    ys = [78 + i for i, p in enumerate(col) if p == (255, 255, 255)]
    top, bottom = ys[0], ys[-1]
    k = (right - left + 1) / W
    out("sheet px", left, right, top, bottom, "k=%.4f" % k, "h/k=%.1f" % ((bottom - top + 1) / k))
    return (lambda x: left + k * x), (lambda y: bottom + 1 - k * y)


def logo_cell_mapper(size, X, Y, wheel=6):
    """Zoom in on the (empty) logo cell and return an exact mm -> px mapping measured from the screen: the
    cell's left edge (title-block x 0) and logo divider (x 36) are the first two columns with a long vertical
    dark run, the title block's top edge (y 50) the first dark pixel above the cell middle. At this zoom the image
    clicks stay clear of the line snapping that grabbed the corner at small scales."""
    from PIL import Image as _I
    import io as _io
    L = _layout(size)
    tx, ty, _w, th = L["title_block"]
    page.mouse.move(X(tx + 18.0), Y(ty + 30.0)); page.wait_for_timeout(300)
    for _ in range(wheel):
        page.mouse.wheel(0, -120); page.wait_for_timeout(250)
    page.wait_for_timeout(1500)
    im = _I.open(_io.BytesIO(page.screenshot())).convert("L")
    Wd, Hd = im.size
    px = im.load()
    cols = []
    for x in range(60, 1560):
        run = best = 0
        for y in range(80, 972):
            if px[x, y] < 110:
                run += 1
                best = max(best, run)
            else:
                run = 0
        if best > 120:
            cols.append(x)
    groups = []
    for x in cols:
        if groups and x - groups[-1][-1] <= 2:
            groups[-1].append(x)
        else:
            groups.append([x])
    cen = [sum(g) / len(g) for g in groups]
    if len(cen) < 2:
        out("cell lines not found", cen); return None
    a, b = cen[0], cen[1]
    k = (b - a) / 36.0
    xm = int((a + b) / 2)
    top = next(y for y in range(80, 972) if px[xm, y] < 110)
    out("cell mapper k=%.3f left %.1f div %.1f top %d" % (k, a, b, top))
    return (lambda x: a + k * (x - tx)), (lambda y: top + k * (ty + th - y))
