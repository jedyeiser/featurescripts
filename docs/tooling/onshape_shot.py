"""Read-only Onshape screenshots for the docs, from a shot list.

Opens each Part Studio once, in its own 2x-resolution browser context that reuses the saved login of
`python -m sync.main login`. Per shot it can set a named view, zoom, open ONE feature's dialog (double-click in the
tree, which also shows its preview), capture, then CANCEL it with the dialog's red X -- always, in a finally block:
closing the browser with a dialog open makes Onshape SAVE it, and Escape does not reliably cancel. The workspace
microversion is compared before and after; the run reports CHANGED if anything was written.

Dialog shots can also record where named fields sit (for numbered callouts in the deck): "labels" lists
{"name": parameter name, "find": text shown in the dialog}; the positions are written next to the PNG as
<dialog>.json = {name: [x, y]} in fractions of the image (the left end of the found text, vertically centred).

Never point this at a document someone is editing live (dialogs are visible to collaborators while open).

usage (repo root):  PYTHONPATH=. python docs/tooling/onshape_shot.py docs/decks/<slug>/shots.json

shots.json:
{
  "doc": "...", "ws": "...",
  "shots": [
    {"elem": "...", "feature": "exact feature name" (optional), "view": "iso|top|front|right|bottom" (optional),
     "fit": true (optional), "wait": 20 (optional, s after load),
     "featurePrefix": "T1 Case template" (optional, instead of "feature": match the start of the tree name),
     "filter": "text" (optional: type into the feature-tree filter first, for features far down a long tree),
     "zoom": [x, y, steps] (optional: mouse-wheel zoom toward window pixel x, y; negative = out; view only),
     "dialog": "docs/decks/<slug>/shots/dialog.png"   (optional: crop of the feature dialog),
     "labels": [{"name": "...", "find": "..."}]         (optional, with "dialog"),
     "graphics": "docs/decks/<slug>/shots/x.png"      (optional: crop of the graphics area; right of the dialog
                                                        when one is open; "clip": [x, y, w, h] overrides),
     "full": "..."                                     (optional: the whole window, for debugging)}
  ]
}
"""
import json
import sys
from pathlib import Path

from playwright.sync_api import sync_playwright

from sync.core.browser import base_url, state_file
from sync.core.client import OnshapeClient

VIEW_KEYS = {"iso": "Shift+7", "top": "Shift+5", "front": "Shift+1", "right": "Shift+3", "bottom": "Shift+6"}
# Graphics area of the 1600 x 1000 window, left of the right-hand toolbar, below the header, above the tabs.
GRAPHICS = {"x": 262, "y": 80, "width": 1300, "height": 870}

FIND_JS = """([names]) => {
  const dlg = document.querySelector('.feature-dialog');
  if (!dlg) { return {}; }
  const box = dlg.getBoundingClientRect();
  const out = {};
  const leaves = [...dlg.querySelectorAll('*')].filter(e => e.children.length === 0 && e.getBoundingClientRect().width > 0);
  for (const [name, find] of names) {
    const el = leaves.find(e => e.textContent.trim() === find);
    if (!el) { continue; }
    const r = el.getBoundingClientRect();
    out[name] = [(r.left - box.left) / box.width, (r.top + r.height / 2 - box.top) / box.height];
  }
  return out;
}"""


def microversion(c, d, w):
    return c.get(f"/api/v10/documents/d/{d}/w/{w}/currentmicroversion")["microversion"]


def save(page, out, clip=None, locator=None):
    Path(out).parent.mkdir(parents=True, exist_ok=True)
    if locator is not None:
        locator.screenshot(path=out)
    else:
        page.screenshot(path=out, **({"clip": clip} if clip else {}))
    print("  wrote", out)


def cancel_dialog(p):
    """Cancel an open feature dialog with its red X (header, right end; 452, 93 at 1600 x 1000). True when closed."""
    dlg = p.locator(".feature-dialog").first
    try:
        if not dlg.is_visible():
            return True
        box = dlg.bounding_box()
        x, y = (box["x"] + box["width"] - 14, box["y"] + 13) if box else (452, 93)
        p.mouse.click(x, y)
        p.wait_for_timeout(1500)
        if dlg.is_visible():
            p.mouse.click(452, 93)
            p.wait_for_timeout(1500)
        return not dlg.is_visible()
    except Exception as e:
        print("  cancel_dialog:", e)
        return False


def main(listing):
    spec = json.loads(Path(listing).read_text())
    d, w = spec["doc"], spec["ws"]
    c = OnshapeClient()
    before = microversion(c, d, w)
    sf = state_file()
    with sync_playwright() as pw:
        browser = pw.chromium.launch(headless=True, args=["--disable-blink-features=AutomationControlled"])
        context = browser.new_context(storage_state=str(sf) if sf.exists() else None,
                                      viewport={"width": 1600, "height": 1000}, device_scale_factor=2)
        p = context.new_page()
        current = None
        try:
            for shot in spec["shots"]:
                if shot["elem"] != current:
                    p.goto(f"{base_url()}/documents/{d}/w/{w}/e/{shot['elem']}", wait_until="domcontentloaded")
                    p.wait_for_timeout(shot.get("wait", 20) * 1000)
                    current = shot["elem"]
                p.mouse.move(GRAPHICS["x"] + 650, GRAPHICS["y"] + 435)   # hover only: keys need the graphics area
                if shot.get("view"):
                    p.keyboard.press(VIEW_KEYS[shot["view"]])
                    p.wait_for_timeout(1500)
                if shot.get("fit"):
                    p.keyboard.press("f")
                    p.wait_for_timeout(1500)
                if shot.get("zoom"):
                    zx, zy, steps = shot["zoom"]
                    p.mouse.move(zx, zy)
                    for _ in range(abs(steps)):
                        p.mouse.wheel(0, -120 if steps > 0 else 120)
                        p.wait_for_timeout(120)
                    p.wait_for_timeout(1500)
                opened = False
                if shot.get("feature") or shot.get("featurePrefix"):
                    if shot.get("filter"):
                        # long trees only render what is on screen: narrow the tree with its filter box (view only)
                        box = p.get_by_placeholder("Filter by name or type").first
                        try:
                            box.wait_for(state="visible", timeout=90000)
                        except Exception:
                            save(p, "docs/tooling/_render/filter_box.FAILED.png")
                            print("  no tree filter box -- see docs/tooling/_render/filter_box.FAILED.png")
                            raise
                        box.fill(shot["filter"])
                        p.wait_for_timeout(2000)
                    if shot.get("featurePrefix"):
                        # names with #variables display differently in the tree: match the start of the name
                        import re as _re
                        p.get_by_text(_re.compile("^" + _re.escape(shot["featurePrefix"]))).first.dblclick()
                    else:
                        p.get_by_text(shot["feature"], exact=True).first.dblclick()
                    try:
                        p.locator(".feature-dialog").first.wait_for(state="visible", timeout=shot.get("dialogWait", 20) * 1000)
                    except Exception:
                        # leave evidence, skip this shot, keep the batch going
                        dbg = str(Path(shot.get("dialog") or shot.get("graphics") or "docs/decks/_shots_raw/x.png").with_suffix(".FAILED.png"))
                        save(p, dbg)
                        print("  dialog did not open -- skipped; see", dbg)
                        cancel_dialog(p)
                        continue
                    p.mouse.move(1500, 900)   # off the tree, so its tooltip closes
                    p.wait_for_timeout(shot.get("settle", 5) * 1000)
                    opened = True
                if shot.get("dialog"):
                    save(p, shot["dialog"], locator=p.locator(".feature-dialog").first)
                    if shot.get("labels"):
                        found = p.evaluate(FIND_JS, [[[l["name"], l["find"]] for l in shot["labels"]]])
                        missing = [l["name"] for l in shot["labels"] if l["name"] not in found]
                        Path(shot["dialog"]).with_suffix(".json").write_text(json.dumps(found, indent=1))
                        print("  labels found %d/%d%s" % (len(found), len(shot["labels"]),
                                                         (" -- missing: " + ", ".join(missing)) if missing else ""))
                if shot.get("graphics"):
                    clip = dict(zip(("x", "y", "width", "height"), shot["clip"])) if shot.get("clip") else GRAPHICS
                    if opened and not shot.get("clip"):
                        clip = dict(GRAPHICS, x=472, width=GRAPHICS["x"] + GRAPHICS["width"] - 472)   # right of the dialog
                    save(p, shot["graphics"], clip=clip)
                if shot.get("full"):
                    save(p, shot["full"])
                if opened and not cancel_dialog(p):
                    print("  WARNING: dialog did not close")
        finally:
            # never leave a dialog open when the browser closes (Onshape would save it)
            if not cancel_dialog(p):
                print("  WARNING: a dialog may still be open -- check the document")
        context.close()
        browser.close()
    after = microversion(c, d, w)
    print("microversion", "UNCHANGED" if before == after else f"CHANGED {before} -> {after}")


if __name__ == "__main__":
    main(sys.argv[1])
