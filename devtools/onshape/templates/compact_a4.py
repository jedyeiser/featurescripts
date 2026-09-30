"""Rework one existing A4 template tab IN PLACE to the A4 COMPACT profile (sheet_layout.py, user decision
2026-09-29): title block 150 x 32, labels 1.8 / values 2.5 / part name 3.5 bold, revision table 150 wide with 1.8
text, NOTES 1.8 bold. Same tab id; property links, layers and lock are kept. ASCII only.

Steps: (1) API relayout with text heights + department line (template_set.relayout(..., heights=True));
(2) UI through ui_driver.py: unlock formats, Revision tables text 1.8, revision table columns / rows, logo
re-inserted in the 30 mm logo cell (Title block layer), lock; (3) .dwt re-export (replaces '<name>.dwt', new id);
(4) PDF -> PNG in publish_tools/research/img/templates/set/; (5) template_set.json (drawing id unchanged, new dwt).
If Onshape says the tab is open in another session the UI step is CANCELLED (never force-open) and nothing
else runs after the API step -- rerun later (the API step is idempotent).

usage (repo root): PYTHONPATH=. python devtools/onshape/templates/compact_a4.py <queue_dir> <BRAND> [--no-api]
"""
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import template_set as ts  # noqa: E402

Q, BRAND = sys.argv[1], sys.argv[2]
SIZE = "A4"
t0 = time.time()
st = ts.load_set()
key = "%s %s" % (BRAND, SIZE)
eid = st["templates"][key]["drawing"]
name = ts.tab_name(BRAND, SIZE)
if "--no-api" not in sys.argv:
    print("relayout", ts.relayout(eid, SIZE, BRAND, heights=True))
t_api = time.time() - t0

old_c = (297.0 - 10.0 - 180.0 + 18.0, 10.0 + 26.0)   # previous A4 logo centre (STANDARD cell spot)
cmd = Path(Q) / ".." / ("compact_%s_A4.py" % BRAND)
cmd.write_text("\n".join([
    'EID = "%s"' % eid, 'BLOB = "%s"' % ts.logo_blob_name(BRAND),
    "RECT = %r" % (tuple(ts.logo_rect(BRAND, SIZE)),), "OLDC = %r" % (old_c,),
    'exec(open(r"devtools/onshape/templates/ui_set_helpers.py").read())',
    "if not open_tab(EID):",
    "    out('SKIPPED')",
    "else:",
    "    lock_formats(False)",
    "    zoom_sheet()",
    "    revision_text(1.8)",
    "    zoom_sheet()",
    "    out('revision table', compact_revision_table('A4'))",
    "    zoom_sheet()",
    "    X, Y = measured_mapper('A4')",
    "    clear_sel(); page.mouse.click(X(OLDC[0]), Y(OLDC[1])); page.wait_for_timeout(700)",
    "    page.keyboard.press('Delete'); page.wait_for_timeout(1500)",
    "    X2, Y2 = logo_cell_mapper('A4', X, Y)",
    "    x0, y0, x1, y1 = RECT",
    "    insert_image(BLOB, x0, y0, x1, y1, X2, Y2)",
    "    page.mouse.click(X2((x0 + x1) / 2), Y2((y0 + y1) / 2)); page.wait_for_timeout(500)",
    "    move_to_layer(X2((x0 + x1) / 2), Y2((y0 + y1) / 2), 'Title block')",
    "    out('logo layer', layer_of(X2((x0 + x1) / 2), Y2((y0 + y1) / 2)))",
    "    zoom_sheet()",
    "    lock_formats(True)",
    "    shot('compact_%s_A4')" % BRAND,
]))
r = subprocess.run([sys.executable, "devtools/onshape/templates/uicmd.py", Q, str(cmd), "600"],
                   capture_output=True, text=True)
print(r.stdout.strip())
if "SKIPPED" in r.stdout or "TIMEOUT" in r.stdout or "EXC" in r.stdout:
    raise SystemExit("UI step did not complete -- dwt / png not refreshed")
t_ui = time.time() - t0 - t_api
dwt, state = ts.export_dwt(eid, name)
print("dwt", dwt, state)
png = ts.REPO / "publish_tools" / "research" / "img" / "templates" / "set" / (
    "%s_%s.png" % (ts.BRANDS[BRAND]["tab"].replace(" ", "_"), SIZE))
ts.to_png(eid, png)
st = ts.load_set()
st["templates"][key].update(dwt=dwt, compact_s=round(time.time() - t0))
ts.save_set(st)
print("done", key, "api %.0f s, ui %.0f s, total %.0f s" % (t_api, t_ui, time.time() - t0), png)
