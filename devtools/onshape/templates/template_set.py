"""The drawing-template SET (6 brands x A4 / A3 / A2) in "Ski Drawing Templates" (doc 52b5bde0), ASCII only.

Route (2026-09-29): every template is a REST element copy of the approved K2 SKIS A3 master, so the UI-made parts
(property links, layers, drawing properties, revision table, lock) come along unchanged. Per copy:
- size change: Sheet properties > Size in the UI, then `relayout()` moves every API annotation (same logicalIds
  as the master) to its sheet_layout.py position for the new size; revision table + logo are moved in the UI;
- brand change: `set_dept()` rewrites the department line; the logo image is swapped in the UI.
Records ids in template_set.json.

usage (repo root, PYTHONPATH=.):
  python devtools/onshape/templates/template_set.py copy <src eid> "<new name>"
  python devtools/onshape/templates/template_set.py relayout <eid> <size>
  python devtools/onshape/templates/template_set.py dept <eid> <brand key>
  python devtools/onshape/templates/template_set.py dwt <eid>
  python devtools/onshape/templates/template_set.py png <eid> <out.png>
"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from _common import TD, TW, c, elements, modify, pt  # noqa: E402
from sheet_layout import SHEETS, sheet_layout  # noqa: E402

HERE = Path(__file__).parent
REPO = HERE.parents[2]
SET_JSON = HERE / "template_set.json"
IDS = json.loads((HERE / "part_a3_layout.json").read_text())["ids"]   # logicalIds, identical in every copy
MASTER_A3 = "acf93b94bedc5f7e268b0f56"
FONT = "Noto Sans"
NAME_PROP = "57f3fb8efa3416c06701d60d"

# brand key -> tab prefix, logo png, department line, tab folder. Visible brand names with an umlaut are written
# "Völkl" (escape keeps this file ASCII); tab names stay ASCII.
BRANDS = {
    "K2SKIS": {"tab": "K2 SKIS", "png": "k2_skis_drawing.png", "dept": "SKI ENGINEERING", "folder": "K2 SKIS"},
    "K2SNOW": {"tab": "K2 SNOWBOARDING", "png": "k2_snowboards_drawing.png", "dept": "SNOWBOARD ENGINEERING",
               "folder": "K2 SNOWBOARDING"},
    "RIDE": {"tab": "RIDE", "png": "ride_drawing.png", "dept": "SNOWBOARD ENGINEERING", "folder": "RIDE"},
    "VOLKL": {"tab": "VOLKL", "png": "volkl_drawing.png", "dept": "SKI ENGINEERING", "folder": "VOLKL",
              "display": "Völkl"},
    "LINE": {"tab": "LINE", "png": "line_drawing.png", "dept": "SKI ENGINEERING", "folder": "LINE"},
    "EOC": {"tab": "EOC", "png": "eoc_drawing.png", "dept": "ENGINEERING", "folder": "EOC",
            "display": "Elevate Outdoor Collective"},
}
SIZES = ("A4", "A3", "A2")


def tab_name(brand, size):
    return "%s - %s (template)" % (BRANDS[brand]["tab"], size)


def logo_blob_name(brand):
    return "%s logo (drawing).png" % BRANDS[brand]["tab"]


def logo_rect(brand, size):
    """Logo placement in sheet mm: (x_left, y_top, x_right, y_bottom). Logo cell = title block x 0-36; the image
    is centred at (18, 26) above the department line. Squarish logos 28 wide (the approved K2 master); wide logos
    (aspect > 2) scaled to the cell width less 2 mm each side (32 mm), aspect kept."""
    from PIL import Image
    w_px, h_px = Image.open(REPO / "icons" / "brands" / BRANDS[brand]["png"]).size
    a = w_px / float(h_px)
    w = 28.0 if a <= 2.0 else 32.0
    h = w / a
    tx, ty, _w, _h = sheet_layout(size)["title_block"]
    cx, cy = tx + 18.0, ty + 26.0
    return (cx - w / 2, cy + h / 2, cx + w / 2, cy - h / 2)


def load_set():
    return json.loads(SET_JSON.read_text()) if SET_JSON.exists() else {"doc": TD, "workspace": TW, "templates": {}}


def save_set(s):
    SET_JSON.write_text(json.dumps(s, indent=1))


def rename(eid, name):
    c.post(f"/api/v6/metadata/d/{TD}/w/{TW}/e/{eid}",
           json_data={"properties": [{"propertyId": NAME_PROP, "value": name}]})


def copy(src, name):
    if any(e["name"] == name for e in elements(TD, TW)):
        raise SystemExit(f"'{name}' exists")
    r = c.post(f"/api/v6/elements/copyelement/{TD}/workspace/{TW}", json_data={
        "documentIdSource": TD, "workspaceIdSource": TW, "elementIdSource": src, "anchorElementId": src,
        "isWholePartStudio": False})
    rename(r["id"], name)
    return r["id"]


def txt(s, bold=False):
    return "{\\f%s|b%d|i0|c0|p0;%s}" % (FONT, 1 if bold else 0, s)


def relayout(eid, size):
    """Move every API-made annotation of a copied master to its sheet_layout(size) position (edits keep the
    property links and layers; they work on locked format layers)."""
    spec = sheet_layout(size)
    anns = []
    for _layer, x1, y1, x2, y2, alias in spec["lines"]:
        anns.append({"type": "Onshape::Line", "line": {"logicalId": IDS[alias][1], "startPoint": pt(x1, y1),
                                                        "endPoint": pt(x2, y2)}})
    for _layer, x, y, r, alias in spec["circles"]:
        anns.append({"type": "Onshape::Circle", "circle": {"logicalId": IDS[alias][1], "center": pt(x, y),
                                                            "radius": r}})
    for _layer, x, y, _s, _h, alias, _b in spec["notes"]:
        anns.append({"type": "Onshape::Note", "note": {"logicalId": IDS[alias][1], "position": pt(x, y)}})
    for alias, x, y, *_r in spec["values"]:
        anns.append({"type": "Onshape::Note", "note": {"logicalId": IDS[alias][1], "position": pt(x, y)}})
    s = modify(TD, TW, eid, [{"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01",
                              "annotations": anns}], "relayout " + size)
    res = json.loads(s["output"])["results"]
    bad = [r for r in res if r.get("status") != "OK"]
    return len(res), bad


def set_dept(eid, brand, size):
    spec = sheet_layout(size, BRANDS[brand]["dept"])
    n = next(n for n in spec["notes"] if n[5] == "lb_dept")
    _layer, x, y, s, h, alias, bold = n
    s2 = modify(TD, TW, eid, [{"messageName": "onshapeEditAnnotations", "formatVersion": "2021-01-01",
                               "annotations": [{"type": "Onshape::Note", "note": {
                                   "logicalId": IDS[alias][1], "position": pt(x, y), "contents": txt(s, bold),
                                   "textHeight": h}}]}], "dept " + brand)
    return s2.get("output")


def export_dwt(eid, name):
    """(Re)export '<name>.dwt' as a blob tab; replaces only a .dwt of exactly that name."""
    dname = name + ".dwt"
    for e in elements(TD, TW):
        if e["name"] == dname:
            c._request("DELETE", f"/api/v6/elements/d/{TD}/w/{TW}/e/{e['id']}")
    r = c.post(f"/api/v10/drawings/d/{TD}/w/{TW}/e/{eid}/translations",
               json_data={"formatName": "DWT", "storeInDocument": True, "destinationName": dname})
    for _ in range(120):
        s = c.get(f"/api/v10/translations/{r['id']}")
        if s["requestState"] != "ACTIVE":
            break
        time.sleep(2)
    return (s.get("resultElementIds") or [None])[0], s["requestState"]


def to_png(eid, png, dpi=150, did=TD, wid=TW):
    from export_drawing import export
    import pypdfium2 as pdfium
    pdf = str(png)[:-4] + ".pdf"
    export(did, wid, eid, "PDF", pdf)
    pdfium.PdfDocument(pdf)[0].render(scale=dpi / 72).to_pil().save(png)
    return png


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "copy":
        print(copy(sys.argv[2], sys.argv[3]))
    elif cmd == "relayout":
        print(relayout(sys.argv[2], sys.argv[3]))
    elif cmd == "dept":
        print(set_dept(sys.argv[2], sys.argv[3], sys.argv[4]))
    elif cmd == "dwt":
        name = next(e["name"] for e in elements(TD, TW) if e["id"] == sys.argv[2])
        print(export_dwt(sys.argv[2], name))
    elif cmd == "png":
        print(to_png(sys.argv[2], sys.argv[3]))
    elif cmd == "rename":
        rename(sys.argv[2], sys.argv[3])


# ---------------------------------------------------------------- size change repair
# Sheet properties > Size regenerates the Border frame and Border zones format layers: our frame, centring marks,
# title-block top / left edges and the NOTES label are replaced by Onshape's own 4-line border, and the
# Title block layer content is shifted. rebuild_format_layers() deletes the generated border, recreates those
# 11 items (on the Drawing layer; ui moves them to their layers) and records their new ids per size.
FORMAT_ALIASES = ("fr_b", "fr_r", "fr_t", "fr_l", "cm_b", "cm_t", "cm_l", "cm_r", "tb_top", "tb_left", "lb_notes")


def size_ids(size):
    """logicalIds for a size master and its copies (the A3 ids with that size's recreated format items)."""
    ids = dict(IDS)
    p = HERE / ("format_ids_%s.json" % size.lower())
    if p.exists():
        ids.update(json.loads(p.read_text()))
    return ids


def rebuild_format_layers(eid, size):
    from export_drawing import export
    tmp = HERE / "__pycache__" / "_dj.json"
    export(TD, TW, eid, "DRAWING_JSON", str(tmp))
    d = json.loads(tmp.read_text())
    known = {v[1] for v in IDS.values()}
    extra = []
    for a in d["sheets"][0]["annotations"]:
        t = a["type"].split("::")[1].lower()
        lid = a[t].get("logicalId")
        if lid not in known:
            extra.append(lid)
    if extra:
        modify(TD, TW, eid, [{"messageName": "onshapeDeleteEntities", "formatVersion": "2021-01-01",
                              "entities": extra}], "delete generated border")
    spec = sheet_layout(size)
    anns, order = [], []
    for _layer, x1, y1, x2, y2, alias in spec["lines"]:
        if alias in FORMAT_ALIASES:
            anns.append({"type": "Onshape::Line", "line": {"startPoint": pt(x1, y1), "endPoint": pt(x2, y2)}})
            order.append(alias)
    for _layer, x, y, s, h, alias, bold in spec["notes"]:
        if alias in FORMAT_ALIASES:
            anns.append({"type": "Onshape::Note", "note": {"position": pt(x, y), "contents": txt(s, bold),
                                                            "textHeight": h}})
            order.append(alias)
    s = modify(TD, TW, eid, [{"messageName": "onshapeCreateAnnotations", "formatVersion": "2021-01-01",
                              "annotations": anns}], "recreate frame / zones")
    res = json.loads(s["output"])["results"]
    new = {a: [("frame" if a.startswith(("fr_", "cm_")) else "zones"), r.get("logicalId")] for a, r in zip(order, res)}
    (HERE / ("format_ids_%s.json" % size.lower())).write_text(json.dumps(new, indent=1))
    return extra, new
