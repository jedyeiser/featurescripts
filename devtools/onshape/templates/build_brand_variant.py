"""Build one brand variant of a size master: REST copy of "K2 SKIS - <SIZE> (template)" -> rename -> department
line (API) -> logo swap in the UI (ui_driver.py queue: unlock formats, delete the K2 logo, insert the brand logo
in the zoomed logo cell, move it to the Title block layer, lock) -> PDF/PNG check -> template_set.json.
The .dwt export is a separate step (template_set.py dwt). ASCII only.

usage (repo root): PYTHONPATH=. python devtools/onshape/templates/build_brand_variant.py <queue_dir> <BRAND> <SIZE>
"""
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, elements  # noqa: E402
import template_set as ts  # noqa: E402

Q, BRAND, SIZE = sys.argv[1], sys.argv[2], sys.argv[3]
t0 = time.time()
st = ts.load_set()
masters = {k: v for k, v in st["templates"].items() if v["brand"] == "K2SKIS"}
src = masters["K2SKIS " + SIZE]["drawing"]
name = ts.tab_name(BRAND, SIZE)
key = "%s %s" % (BRAND, SIZE)
have = [e for e in elements(TD, TW) if e["name"] == name]
eid = have[0]["id"] if have else ts.copy(src, name)
print(name, eid, "copied" if not have else "exists")
if ts.BRANDS[BRAND]["dept"] != "SKI ENGINEERING":
    print("dept", ts.set_dept(eid, BRAND, SIZE)[:120])

cmd = Path(Q) / ".." / ("variant_%s_%s.py" % (BRAND, SIZE))
cmd.write_text("\n".join([
    'EID = "%s"' % eid, 'SIZE = "%s"' % SIZE, 'BLOB = "%s"' % ts.logo_blob_name(BRAND),
    "RECT = %r" % (tuple(ts.logo_rect(BRAND, SIZE)),), "OLD = %r" % (tuple(ts.logo_rect("K2SKIS", SIZE)),),
    'exec(open(r"devtools/onshape/templates/ui_set_helpers.py").read())',
    "if not open_tab(EID):",
    "    out('SKIPPED')",
    "else:",
    "    lock_formats(False)",
    "    zoom_sheet()",
    "    X, Y = measured_mapper(SIZE)",
    "    clear_sel(); page.mouse.click(X((OLD[0] + OLD[2]) / 2), Y((OLD[1] + OLD[3]) / 2)); page.wait_for_timeout(700)",
    "    page.keyboard.press('Delete'); page.wait_for_timeout(1500)",
    "    X2, Y2 = logo_cell_mapper(SIZE, X, Y)",
    "    x0, y0, x1, y1 = RECT",
    "    insert_image(BLOB, x0, y0, x1, y1, X2, Y2)",
    "    page.mouse.click(X2((x0 + x1) / 2), Y2((y0 + y1) / 2)); page.wait_for_timeout(500)",
    "    move_to_layer(X2((x0 + x1) / 2), Y2((y0 + y1) / 2), 'Title block')",
    "    out('logo layer', layer_of(X2((x0 + x1) / 2), Y2((y0 + y1) / 2)))",
    "    zoom_sheet()",
    "    lock_formats(True)",
    "    shot('variant_%s_%s')" % (BRAND, SIZE),
]))
r = subprocess.run([sys.executable, "devtools/onshape/templates/uicmd.py", Q, str(cmd), "300"],
                   capture_output=True, text=True)
print(r.stdout.strip())
png = ts.REPO / "publish_tools" / "research" / "img" / "templates" / "set" / ("%s_%s.png" % (ts.BRANDS[BRAND]["tab"].replace(" ", "_"), SIZE))
ts.to_png(eid, png)
st = ts.load_set()
logo = next(e["id"] for e in elements(TD, TW) if e["name"] == ts.logo_blob_name(BRAND))
st["templates"][key] = dict(st["templates"].get(key, {}), brand=BRAND, size=SIZE, name=name, drawing=eid, logo=logo,
                            build_s=round(time.time() - t0))
ts.save_set(st)
print("done", key, round(time.time() - t0), "s", png)
