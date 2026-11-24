"""Update the Import 1 feature of the import_cleaner 'lab_enclose' studio with a file, through Onshape's own Update action (Playwright).

    PYTHONPATH=. python import_cleaner/ui_update_import.py <file.step>

Right-clicks Import 1, clicks Update..., answers the file chooser with the file, then waits (REST polling, a few calls) until the
Part Studio's microversion has changed and stayed put. Then run snapshot_ids.py to record the identities. Uses the signed-in browser
session (python -m sync.main login); a visible window.
"""
import os
import re
import sys
import time
from pathlib import Path

from sync.core.browser import OnshapeBrowser
from sync.core.client import OnshapeClient

D, W, E = "ad26c2fa8bc9abaf250c5e8a", "d713b8e6302cdb9bc9ad6308", "21efb3b681d28f70f0d1d256"
path = Path(sys.argv[1]).resolve()
c = OnshapeClient()


def microversion():
    return c.get("/api/v10/partstudios/d/%s/w/%s/e/%s/features" % (D, W, E)).get("sourceMicroversion")


before = microversion()
print("microversion before:", before, "| file:", path.name, "%.1f MB" % (path.stat().st_size / 1e6))
with OnshapeBrowser(headless=False) as b:
    page = b.page
    page.set_viewport_size({"width": 1700, "height": 1000})
    b.open_element(D, W, E)
    page.wait_for_timeout(12000)
    page.get_by_text("Import 1", exact=True).first.click(button="right")
    page.wait_for_timeout(800)
    with page.expect_file_chooser(timeout=20000) as chooser:
        page.locator("text=/^Update/ >> visible=true").first.click()
    chooser.value.set_files(str(path))
    print("file given to the Update action; waiting for Onshape ...")
    stable = 0
    last = before
    for i in range(40):
        page.wait_for_timeout(10000)
        now = microversion()
        if now != before and now == last:
            stable += 1
        else:
            stable = 0
        last = now
        print("  poll %d: microversion %s%s" % (i + 1, now, " (changed)" if now != before else ""))
        if now != before and stable >= 2:
            break
    page.wait_for_timeout(5000)
    b.screenshot(Path(__file__).parent / "snapshots" / ("after_" + path.stem + ".png"))
print("done; microversion after:", last)
