"""(Re)build the demo drawings of Station geometry output in "Publish & Drawing tools" (doc 73271cfc).

Every drawing named here is deleted and rebuilt, so it is safe to rerun; nothing else in the document is touched.

  "4101 PLAN (demo)"            plan view of the open composite "4101 PLAN": width at each Q station and each
                                station's position from the TAIL (datum) end
  "4501 PLAN + PROFILE (demo)"  plan view of "4501 PLAN" (widths) and front view of "4501 PROFILE" (thickness)

How a dimension is attached (learned 2026-09-28):
  * a view shows the composite's station lines only when the view is created with "includeWires": true
    (onshapeCreateViews); setting it afterwards with onshapeEditViews does not re-render the wires;
  * a reference must name the edge by its view "deterministicId" (from .../views/{vid}/jsongeometry); the
    "uniqueId" in the same payload does not reliably resolve (profile dimensions landed on other lines);
  * text positions are sheet millimetres from the lower-left corner; a view "position" is where the centre of
    its geometry goes.
Station lines are found by the bodies' deterministic-id prefix, looked up by body NAME from the parts list.

usage (repo root): PYTHONPATH=. python devtools/onshape/publish_tools_demo_drawings.py
"""
import time

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W, PS = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761", "bb2cddb24faf53d9d043c38e"
FMT1 = {"dimdec": 1, "type": "Onshape::Formatting::Dimension"}
FMT2 = {"dimdec": 2, "type": "Onshape::Formatting::Dimension"}
STATIONS = ["Q_1", "Q_2", "Q_3", "Q_4", "Q_5"]


def modify(eid, requests, desc):
    r = c.post(f"/api/v6/drawings/d/{D}/w/{W}/e/{eid}/modify", json_data={"description": desc, "jsonRequests": requests})
    for _ in range(60):
        s = c.get(f"/api/v6/drawings/modify/status/{r['id']}")
        if s.get("requestState") != "ACTIVE":
            if '"Failed"' in (s.get("output") or ""):
                print("  ", desc, s.get("output"))
            return s
        time.sleep(2)


def parts():
    """{body name: part id} including wires and composites."""
    return {p["name"]: p["partId"] for p in c.get(f"/api/v10/parts/d/{D}/w/{W}/e/{PS}", query_params={"includeWireBodies": "true"})}


def new_drawing(name):
    for e in c.list_elements(D, W):
        if e["name"] == name:
            c._request("DELETE", f"/api/v6/elements/d/{D}/w/{W}/e/{e['id']}")
    return c.post(f"/api/v6/drawings/d/{D}/w/{W}/create", json_data={
        "drawingName": name, "border": True, "titleblock": True, "size": "A3", "units": "millimeter", "standard": "ISO"})["id"]


def add_view(eid, part_id, orientation, x, y):
    s = modify(eid, [{"messageName": "onshapeCreateViews", "formatVersion": "2021-01-01", "views": [{
        "viewType": "TopLevel", "position": {"x": x, "y": y}, "orientation": orientation, "includeWires": True,
        "scale": {"scaleSource": "Custom", "numerator": 1, "denumerator": 5},
        "reference": {"elementId": PS, "idTag": part_id}}]}], "view " + orientation)
    time.sleep(10)
    vid = [r for r in __import__("json").loads(s["output"])["results"]][0]["viewId"]
    return vid, c.get(f"/api/v8/drawings/d/{D}/w/{W}/e/{eid}/views/{vid}/jsongeometry")["bodyData"]


def station_lines(geom, prefix, pid_of):
    """{station: line entity} for the '<prefix> ST <station>' wires in a view's geometry."""
    out = {}
    for st in STATIONS + ["TAIL"]:
        pid = pid_of.get(prefix + " ST " + st)
        if pid is None:
            continue
        stem = pid[:-1]           # part id RNXD -> its edges in the view are RNX...
        for e in geom:
            if e["type"] == "line" and e["deterministicId"].startswith(stem):
                out[st] = e
    return out


def ref(e, vid, where, snap):
    d = e["data"]
    co = d["start"] if where == "start" else d["end"] if where == "end" else [(a + b) / 2 for a, b in zip(d["start"], d["end"])]
    return {"coordinate": co, "type": "Onshape::Reference::Point", "deterministicId": e["deterministicId"], "viewId": vid, "snapPointType": snap}


def p2p(e1, w1, s1, e2, w2, s2, vid, text, fmt):
    return {"type": "Onshape::Dimension::PointToPoint", "pointToPointDimension": {
        "point1": ref(e1, vid, w1, s1), "point2": ref(e2, vid, w2, s2),
        "textPosition": {"coordinate": text + [0], "type": "Onshape::Reference::Point"}, "formatting": fmt}}


def note(text, x=20, y=280):
    return {"type": "Onshape::Note", "note": {"position": {"coordinate": [x, y, 0], "type": "Onshape::Reference::Point"},
                                              "contents": text, "textHeight": 3.5}}


def centre_x(geom):
    xs = [p[0] for e in geom if e["type"] == "line" for p in (e["data"]["start"], e["data"]["end"])]
    return (min(xs) + max(xs)) / 2 * 1000


def sheet_x(xm, cx, ox):
    return cx + (xm - ox) / 5


pid_of = parts()

# ---- 4101 PLAN
eid = new_drawing("4101 PLAN (demo)")
CX, CY = 200.0, 190.0
vid, geom = add_view(eid, pid_of["4101 PLAN"], "top", CX, CY)
lines, ox = station_lines(geom, "4101 PLAN", pid_of), centre_x(geom)
anns = [note("4101 PLAN -- generated by Station geometry (publish_tools). Station lines and datum come from the model, not a sketch.")]
for k, st in enumerate(STATIONS):
    L = lines[st]
    xm = L["data"]["start"][0] * 1000
    anns.append(p2p(L, "start", "ModeStart", L, "end", "ModeEnd", vid, [sheet_x(xm + 45, CX, ox), CY], FMT1))
    anns.append(p2p(lines["TAIL"], "mid", "ModeMid", L, "mid", "ModeMid", vid, [sheet_x(xm / 2, CX, ox), CY + 22 + 8 * k], FMT1))
modify(eid, [{"messageName": "onshapeCreateAnnotations", "formatVersion": "2021-01-01", "annotations": anns}], "4101 dims")
print("4101 PLAN (demo)", eid, "stations found", sorted(lines))

# ---- 4501 PLAN + PROFILE
eid = new_drawing("4501 PLAN + PROFILE (demo)")
anns = [note("4501 core -- PLAN (widths) and PROFILE (thickness) at the same stations, both from Station geometry. No sketches.")]
for comp, orient, cy, text_dy, fmt, dx in (("4501 PLAN", "top", 215.0, 0.0, FMT1, 9), ("4501 PROFILE", "front", 110.0, 16.0, FMT2, 0)):
    vid, geom = add_view(eid, pid_of[comp], orient, CX, cy)
    lines, ox = station_lines(geom, comp, pid_of), centre_x(geom)
    for st in STATIONS:
        L = lines[st]
        xm = L["data"]["start"][0] * 1000
        anns.append(p2p(L, "start", "ModeStart", L, "end", "ModeEnd", vid, [sheet_x(xm, CX, ox) + dx, cy + text_dy], fmt))
    print(" ", comp, "stations found", sorted(lines))
modify(eid, [{"messageName": "onshapeCreateAnnotations", "formatVersion": "2021-01-01", "annotations": anns}], "4501 dims")
print("4501 PLAN + PROFILE (demo)", eid)
