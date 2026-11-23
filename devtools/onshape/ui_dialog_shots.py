"""Screenshot the edit dialog of existing features in a Part Studio, read-only (Escape cancels; nothing is changed).

    PYTHONPATH=. python devtools/onshape/ui_dialog_shots.py <out_dir> "<feature name prefix>" ["<prefix>" ...]

Uses the signed-in browser session of sync/core/browser.py (run `python -m sync.main login` once). No REST writes; the
browser session is most likely not counted against the API quota (unverified), the only REST calls are the element list.
Rules (memory playwright-and-live-safety): never run it while the user edits that studio's tabs in another session if
Onshape asks "open in another session" (the script does not click it); no clicks in the viewport.
The studio defaults to "Reference side tests" of reference_side/.document.json; set STUDIO=<name> to use another.
"""
import json
import os
import sys
from pathlib import Path

from sync.core.browser import OnshapeBrowser
from sync.core.client import OnshapeClient

DOC = json.load(open("reference_side/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = os.environ.get("STUDIO", "Reference side tests")
E = {e["name"]: e["id"] for e in OnshapeClient().list_elements(D, W) if e["elementType"] == "PARTSTUDIO"}[STUDIO]
out = Path(sys.argv[1])
out.mkdir(parents=True, exist_ok=True)
with OnshapeBrowser(headless=True) as b:
    page = b.page
    page.set_viewport_size({"width": 1700, "height": 1000})
    b.open_element(D, W, E)
    page.wait_for_timeout(20000)        # a big studio regenerates slowly
    box = page.get_by_placeholder("Filter by name or type")
    for name in sys.argv[2:]:
        box.fill(name)
        page.wait_for_timeout(1500)
        page.get_by_text(name, exact=False).first.dblclick()
        page.wait_for_timeout(6000)
        page.mouse.move(1000, 800)      # no hover tooltip over the dialog
        page.wait_for_timeout(1000)
        b.screenshot(out / ("dialog_%s.png" % name.split(" ")[0]))
        page.keyboard.press("Escape")   # cancel: no change to the feature
        page.wait_for_timeout(1500)
        print("shot", name)
    box.fill("")
