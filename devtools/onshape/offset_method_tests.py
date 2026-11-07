"""Build + check Offset+'s Offset method (Auto / Direct / Thicken, 2026-11-06) and its failure message in the Reference Side
document's "Reference side tests" Part Studio (cases M1-M7, x >= 40000 mm, upsert by name).

  M1  Thicken, sheet at z 50, reference at the centre, toward      -> sheet at z 40, area 10000, no warning
  M2  Thicken, away                                                -> z 60
  M3  Direct, toward                                               -> z 40 (same as M1)
  M4  Auto, toward                                                 -> z 40, status OK (the direct offset worked: no warning)
  M5  Thicken, a cylinder sheet r 50 offset 30 mm toward its axis  -> r 20 (area 2 pi 20 100)
  M6  Direct, the same cylinder, 30 mm                             -> r 20 (equal to M5)
  M7  Auto, the cylinder offset 60 mm toward its axis (> r)        -> ERROR "Offsetting surface 1 by 60 mm failed ..." naming
                                                                      the faces (shown in red in the dialog)

usage (repo root): PYTHONPATH=. FS_SYNC_TIMEOUT=120 python devtools/onshape/offset_method_tests.py
About 25 REST calls (one features GET, one write per feature, one eval, one statuses GET).
"""
import json
import math
import re
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, cached_tree

c = OnshapeClient()
DOC = json.load(open("reference_side/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
ELEMENTS = c.list_elements(D, W)
E = [e["id"] for e in ELEMENTS if e["name"] == "Reference side tests" and e["elementType"] == "PARTSTUDIO"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "offset_plus" and e["elementType"] == "FEATURESTUDIO"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
CACHE = {}
cached_tree(BASE, CACHE, c)


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def upsert(feature):
    r = write_feature(BASE, feature, CACHE, client=c)
    print("%-110s %s" % (feature["name"][:110], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


def mmv(*xs):
    return [x / 1000.0 for x in xs]


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def circle(eid, cx, cy, r):
    return {"btType": "BTMSketchCurve-4", "entityId": eid, "centerId": eid + ".center", "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def point(eid, x, y):
    return {"btType": "BTMSketchPoint-158", "entityId": eid, "isConstruction": False, "x": x, "y": y}


def sketch(name, plane_expr, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
FRONT = 'qCreatedBy(makeId("Front"), EntityType.FACE)'


def edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def vertex(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def surface(name, sketch_id, depth):
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
        q("surfaceEntities", edges(sketch_id)), en("endBound", "BoundingType", "BLIND"), num("depth", "%g mm" % depth), b("symmetric", True)])


def offset(name, surfaces, ref, method, dist, toward=True):
    return feature(name, "offsetPlus", [
        en("offsetType", "OffsetPlusType", "SURFACE", NS), en("offsetMethod", "OffsetMethod", method, NS), q("surfaces", surfaces),
        num("distance", dist), q("sideReference", ref), b("towardReference", toward)], NS)


# ---- fixtures: a flat sheet at z 50 (x0 +- 50, y +- 50) and a cylinder sheet r 50, axis along Z through (x0, 0), z +-50 ----
cases = {}
x0 = 40000
flat_sk = sketch("M fixture flat sheet (sketch) z 50 x %d" % x0, FRONT, [seg("l", *mmv(x0 - 50, 50, x0 + 50, 50))])
flat = surface("M fixture flat sheet z 50, x %d" % x0, flat_sk, 100)
ref_flat = vertex(sketch("M fixture flat reference (%d, 0, 0)" % x0, FRONT, [point("p", *mmv(x0, 0))]))
x1 = 40400
cyl_sk = sketch("M fixture cylinder circle r 50 at (%d, 0)" % x1, TOP, [circle("c", *mmv(x1, 0, 50))])
cyl = surface("M fixture cylinder sheet r 50, z +-50, x %d" % x1, cyl_sk, 100)
ref_cyl = vertex(sketch("M fixture cylinder axis point (%d, 0, 0)" % x1, TOP, [point("p", *mmv(x1, 0))]))

for tag, name, method, surf, ref, dist, toward in [
        ("M1", "M1 Offset+ THICKEN flat sheet z 50 toward the centre 10 mm -> z 40, area 10000", "THICKEN", flat, ref_flat, "10 mm", True),
        ("M2", "M2 Offset+ THICKEN flat sheet z 50 away from the centre 10 mm -> z 60", "THICKEN", flat, ref_flat, "10 mm", False),
        ("M3", "M3 Offset+ DIRECT flat sheet z 50 toward the centre 10 mm -> z 40", "DIRECT", flat, ref_flat, "10 mm", True),
        ("M4", "M4 Offset+ AUTO flat sheet z 50 toward the centre 10 mm -> z 40, no warning", "AUTO", flat, ref_flat, "10 mm", True),
        ("M5", "M5 Offset+ THICKEN cylinder sheet r 50 toward its axis 30 mm -> r 20", "THICKEN", cyl, ref_cyl, "30 mm", True),
        ("M6", "M6 Offset+ DIRECT cylinder sheet r 50 toward its axis 30 mm -> r 20", "DIRECT", cyl, ref_cyl, "30 mm", True),
        ("M7", "M7 Offset+ AUTO cylinder sheet r 50 toward its axis 60 mm (more than r) -> ERROR naming the surface and the faces", "AUTO", cyl, ref_cyl, "60 mm", True)]:
    cases[tag] = offset(name, body(surf), ref, method, dist, toward)

# ---- read back: each case's bodies (box, area) and the status text of every case ----
script = "function(context is Context, queries)\n{\n    var out = [];\n    const mm = function(v) { return roundToPrecision(v / millimeter, 3); };\n"
for tag, fid in cases.items():
    script += ('    {\n        const bodies = evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.BODY));\n'
               '        var line = "%s bodies " ~ size(bodies);\n'
               '        for (var bd in bodies)\n        {\n'
               '            const bb = evBox3d(context, { "topology" : bd, "tight" : true });\n'
               '            line = line ~ " | area " ~ roundToPrecision(evArea(context, { "entities" : qOwnedByBody(bd, EntityType.FACE) }) / (millimeter ^ 2), 1)'
               ' ~ " x " ~ mm(bb.minCorner[0]) ~ ".." ~ mm(bb.maxCorner[0]) ~ " y " ~ mm(bb.minCorner[1]) ~ ".." ~ mm(bb.maxCorner[1]) ~ " z " ~ mm(bb.minCorner[2]) ~ ".." ~ mm(bb.maxCorner[2]);\n'
               '        }\n'
               '        out = append(out, line ~ " | err " ~ toString(getFeatureError(context, makeId("%s"))) ~ " | warn " ~ toString(getFeatureWarning(context, makeId("%s"))));\n'
               '    }\n') % (fid, tag, fid, fid)
script += "    return out;\n}\n"
r = c.post(BASE + "/featurescript", json_data={"script": script})
for n in (r.get("notices") or []):
    if n.get("level") == "ERROR":
        print("NOTICE", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if not v.startswith("BTFSValue"):
        print(v)
