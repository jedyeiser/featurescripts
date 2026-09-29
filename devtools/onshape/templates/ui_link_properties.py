# Driver command file (run through ui_driver.py / uicmd.py): links the staged value notes of the PART A3
# master to drawing / sheet-reference properties in the note editor. Expects the drawing open and
# "Zoom to sheet" applied (sheet 420 x 297 mm -> x px = 200 + 2.951 x, y px = 961 - 2.951 y at 1600 x 1000).
import json as _json
import importlib.util as _ilu

_spec = _ilu.spec_from_file_location("bpm", r"devtools/onshape/templates/build_part_a3_master.py")
_bpm = _ilu.module_from_spec(_spec); _spec.loader.exec_module(_bpm)
LAY = _json.loads(open(r"devtools/onshape/templates/part_a3_layout.json").read())
f = [x for x in page.frames if 'drawing' in x.url][0]
X = lambda x: 200 + 2.951 * x
Y = lambda y: 961 - 2.951 * y
ONLY = globals().get("ONLY")


def pick(text):
    loc = f.get_by_text(text, exact=True)
    vis = [i for i in range(loc.count()) if loc.nth(i).is_visible()]
    if not vis:
        return False
    loc.nth(vis[-1]).click(); time.sleep(0.8)
    return True


for k, (alias, _fx, _fy, h, bold, w, parts) in enumerate(_bpm.VALUES):
    if ONLY and alias not in ONLY:
        continue
    sx, sy = LAY["staging"][alias]
    cx, cy = X(sx + 0.5), Y(sy - h / 2)   # left edge -> caret before the first character
    page.mouse.click(1500, 300); time.sleep(0.4)
    page.mouse.move(cx, cy); time.sleep(0.4)
    page.mouse.dblclick(cx, cy); time.sleep(1.8)
    hl = f.locator("input.noteeditor-height").first
    if not hl.is_visible():
        out(alias, "editor did not open"); continue
    hb = hl.bounding_box()
    if abs(float(hl.input_value()) - h) > 1e-3:
        out(alias, "wrong note (height", hl.input_value(), ") - cancel")
        page.mouse.click(hb["x"] + 138, hb["y"] - 21); time.sleep(1); continue
    page.keyboard.press("Home")
    for _ in range(90):
        page.keyboard.press("Delete")
    ok = True
    for kind, item in parts:
        if kind == "text":
            page.keyboard.press("End"); page.keyboard.type(item); time.sleep(0.3)   # End: an inserted field stays selected and typing would replace it
            continue
        page.mouse.click(hb["x"] + (-129 if kind == "sheet" else -172), hb["y"] + 75); time.sleep(1.2)
        if not pick(item):
            ok = False
            out(alias, "property not offered:", item)
            page.keyboard.press("Escape"); time.sleep(0.5)
    shot("link_" + alias)
    if ok:
        page.mouse.click(hb["x"] + 109, hb["y"] - 21)      # green check
        out(alias, "linked")
    else:
        page.mouse.click(hb["x"] + 138, hb["y"] - 21)      # red X, note unchanged
        out(alias, "cancelled")
    time.sleep(1.5)
page.mouse.click(1500, 300)
shot("link_done")
