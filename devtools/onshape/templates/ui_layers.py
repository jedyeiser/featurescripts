# Driver command file: puts the PART A3 master's frame / zone / title-block entities on Onshape's format layers
# (right-click > Move to > Border frame | Border zones | Title block). Expects "Zoom to sheet" at 1600 x 1000
# with the right-hand panel closed. STEP selects which group to move ("frame", "zones", "tb").
f = [x for x in page.frames if 'drawing' in x.url][0]
X = lambda x: 200 + 2.951 * x
Y = lambda y: 961 - 2.951 * y
STEP = globals().get("STEP", "frame")
LAYER = {"frame": "Border frame", "zones": "Border zones", "tb": "Title block", "tb2": "Title block"}[STEP]
PICKS = {
    "frame": [(120, 10), (410, 220), (120, 287), (20, 220), (210, 5), (210, 292), (10, 148.5), (415, 148.5)],
    "zones": [(120, 150), (120, 50), (230, 100), (27, 47.5)],
    # leftovers of the window pick: notes wider than the window (title / description) and lines touching its edge
    "tb2": [(268.5, 43.5), (268, 32), (269, 48), (270, 36), (266, 44), (266, 24), (390, 38), (390, 28)],
}
page.mouse.click(X(300), Y(220)); page.wait_for_timeout(500)      # clear selection (empty view zone)
page.keyboard.press("Escape")
if STEP == "tb":
    page.mouse.move(X(229.4), Y(50.6)); page.mouse.down()
    page.mouse.move(X(300), Y(30), steps=5); page.mouse.move(X(411.5), Y(8.8), steps=10); page.mouse.up()
    page.wait_for_timeout(800)
    rc = (X(266), Y(30))
else:
    for x, y in PICKS[STEP]:
        page.mouse.click(X(x), Y(y)); page.wait_for_timeout(400)
    rc = (X(PICKS[STEP][0][0]), Y(PICKS[STEP][0][1]))
shot("layer_sel_" + STEP)
page.mouse.click(rc[0], rc[1], button="right"); page.wait_for_timeout(1000)
mv = f.get_by_text("Move to", exact=True)
vis = [i for i in range(mv.count()) if mv.nth(i).is_visible()]
if not vis:
    out(STEP, "no Move to in menu"); shot("layer_menu_" + STEP)
else:
    mv.nth(vis[0]).hover(); page.wait_for_timeout(800)
    it = f.get_by_text(LAYER, exact=True)
    v2 = [i for i in range(it.count()) if it.nth(i).is_visible()]
    shot("layer_menu_" + STEP)
    it.nth(v2[0]).click(timeout=5000); page.wait_for_timeout(1500)
    out(STEP, "moved to", LAYER)
page.mouse.click(X(300), Y(220)); page.wait_for_timeout(500)
