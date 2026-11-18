"""Build the Query Variable X tests as real features in the "QVX tests" Part Studio of the query_variable_x
document. The studio is ours alone: every run deletes its features and rebuilds them. Check with
check_qvx_tests.py (expected counts live there).

Fixture (all on Top, millimetres):
  F1 box A  60 x 40 x 20 at x -30..30, y -20..20          F1f fillet r 5 on A's vertical edge at (30, 20)
  F2 box B  20 x 20 x 50 at x 100..120, y -10..10
  F3 cylinder r 10, h 30 at (200, 0)
  F4 sketch arc r 25 centred (25, 100): CCW from (50, 100) to (0, 100) (chord along X, end tangents along Y)
Faces of solids: A 7 (6 + fillet), B 6, cylinder 3 = 16.

Each test is a Query variable X feature that sets variable tN.

usage (repo root): PYTHONPATH=. python devtools/onshape/build_qvx_tests.py
"""
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("query_variable_x/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "QVX tests"

els = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in els if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in els if e["elementType"] == "FEATURESTUDIO" and e["name"] == "query_variable_x"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


def flist(pid, ids):
    return {"btType": "BTMParameterFeatureList-1749", "parameterId": pid, "featureIds": list(ids)}


def arr(pid, items):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid,
            "items": [{"btType": "BTMArrayParameterItem-1843", "parameters": it} for it in items]}


cache = {}
tree = c.get(f"{BASE}/features")
for f in reversed(tree["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")
tree = c.get(f"{BASE}/features")


def post(feature):
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": tree["serializationVersion"], "sourceMicroversion": tree["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    tree["sourceMicroversion"] = r.get("sourceMicroversion", tree["sourceMicroversion"])
    print("%-84s %s" % (feature["name"][:84], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def sketch(name, entities):
    return post({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                 "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Top"), EntityType.FACE)')],
                 "entities": entities, "constraints": []})


def extrude(name, sk, depth):
    return post({"btType": "BTMFeature-134", "featureType": "extrude", "name": name, "parameters": [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"), num("depth", depth)]})


def box(name, x0, y0, x1, y1, h):
    pts = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    sk = sketch(name + " (sketch)", [seg("p%d" % i, pts[i][0] / 1000, pts[i][1] / 1000, pts[(i + 1) % 4][0] / 1000, pts[(i + 1) % 4][1] / 1000) for i in range(4)])
    return extrude(name, sk, h)


A = box("F1 box A (60 x 40 x 20)", -30, -20, 30, 20, "20 mm")
B = box("F2 box B (20 x 20 x 50)", 100, -10, 120, 10, "50 mm")
cyl_sk = sketch("F3 cylinder (sketch)", [{"btType": "BTMSketchCurve-4", "entityId": "c0", "centerId": "c0.center", "isConstruction": False,
                                          "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": 0.01, "xCenter": 0.2, "yCenter": 0.0,
                                                       "xDir": 1.0, "yDir": 0.0, "clockwise": False}}])
CYL = extrude("F3 cylinder r 10 x 30", cyl_sk, "30 mm")
FIL = post({"btType": "BTMFeature-134", "featureType": "fillet", "name": "F1f fillet r 5 on A's edge at (30, 20)", "parameters": [
    q("entities", 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.EDGE), vector(30, 20, 10) * millimeter)' % A), num("radius", "5 mm")]})
ARC = sketch("F4 arc sketch", [{"btType": "BTMSketchCurveSegment-155", "entityId": "arc0", "startPointId": "arc0.start", "endPointId": "arc0.end",
                                "startParam": 0.0, "endParam": math.pi, "isConstruction": False,
                                "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": 0.025, "xCenter": 0.025, "yCenter": 0.1,
                                             "xDir": 1.0, "yDir": 0.0, "clockwise": False}}])

SOLID_FACES = "qBodyType(qEverything(EntityType.FACE), BodyType.SOLID)"
ARC_EDGES = 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % ARC


def top(fid):
    return 'qCapEntity(makeId("%s"), CapType.END, EntityType.FACE)' % fid


def bottom(fid):
    return 'qCapEntity(makeId("%s"), CapType.START, EntityType.FACE)' % fid


def face_at(fid, x, y, z):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.FACE), vector(%s, %s, %s) * millimeter)' % (fid, x, y, z)


Z_REF = top(B)                       # normal +Z
X_REF = face_at(B, 120, 0, 25)       # normal +X
Y_REF = face_at(B, 110, 10, 25)      # normal +Y


def _defaults():
    """Every item parameter, in spec order (the API rejects an item that lacks any), enums in the studio namespace."""
    return [en("fltMode", "FilterMode", "KEEP", NS), en("fltType", "FilterType", "GEOMETRY", NS),
            q("fltDirRef"), en("fltDirRelation", "DirectionRelation", "PARALLEL", NS), num("fltAngleTol", "0 deg"),
            en("fltFaceUse", "FaceDirectionUse", "NORMAL", NS), en("fltCurveMode", "CurveDirectionMode", "CHORD", NS), b("fltProject", False), q("fltProjectPlane"),
            q("fltBetweenA"), q("fltBetweenB"), en("fltBetweenMode", "BetweenMode", "FULLY_BETWEEN", NS), b("fltIncludeBounds", True), b("fltMateAsPlane", False),
            en("fltPosType", "PositionalDirectionalType", "INTERSECTS_PLANE", NS), q("fltPosPlane"), q("fltPosLine"), q("fltPosClosestRef"),
            q("fltPosVertex"), num("fltPosRadius", "0 mm"), q("fltPosDirection"),
            en("fltGeometryType", "GeometryType", "CYLINDER", NS), en("fltEntityType", "EntityType", "FACE", NS),
            en("fltBodyType", "BodyTypeOptions", "SOLID", NS), en("fltSizeType", "SizeComparisonType", "LARGEST", NS), q("fltSizeReference"),
            b("fltSizeAllowEqual", False), en("fltConvexity", "EdgeConvexityType", "CONVEX", NS), en("fltEdgeTopology", "EdgeTopology", "ONE_SIDED", NS), flist("fltModifiedBy", []), q("fltSelection")]


def item(params):
    """A complete array item: the defaults with `params` (by parameterId) replacing theirs."""
    over = {p["parameterId"]: p for p in params}
    return [over.get(d["parameterId"], d) for d in _defaults()]

def direction(ref, relation="PARALLEL", face_use="NORMAL", curve="CHORD", mode="KEEP"):
    return [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "DIRECTION", NS), q("fltDirRef", ref),
            en("fltDirRelation", "DirectionRelation", relation, NS), num("fltAngleTol", "0 deg"),
            en("fltFaceUse", "FaceDirectionUse", face_use, NS), en("fltCurveMode", "CurveDirectionMode", curve, NS), b("fltProject", False)]


def between(a, bb, how, include=True, mode="KEEP"):
    return [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "BETWEEN", NS), q("fltBetweenA", a), q("fltBetweenB", bb),
            en("fltBetweenMode", "BetweenMode", how, NS), b("fltIncludeBounds", include)]


def closest(ref, mode="KEEP"):
    return [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "POSITION", NS),
            en("fltPosType", "PositionalDirectionalType", "CLOSEST_TO", NS), q("fltPosClosestRef", ref)]


def geometry(kind, mode="KEEP"):
    return [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "GEOMETRY", NS), en("fltGeometryType", "GeometryType", kind, NS)]


def modified_by(fids, mode="KEEP"):
    return [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "LAST_MODIFIED_BY", NS), flist("fltModifiedBy", fids)]


def qvx(var, label, source, *filters):
    post({"btType": "BTMFeature-134", "featureType": "queryVariable", "name": "%s %s" % (var, label), "namespace": NS, "parameters": [
        s("name", var), s("description", ""), en("selectionType", "SelectionType", "SELECTION", NS), q("selectionQuery", source),
        arr("filters", [item(f) for f in filters]), b("debugStages", False), b("specifyOutputType", False),
        b("evaluateOnUse", False), b("showSelection", False), b("saveForDerivedStudios", False)]})


qvx("t1", "Direction parallel +Z -> 6 (top and bottom of A, B, cylinder)", SOLID_FACES, direction(Z_REF))
qvx("t2", "Direction perpendicular to Z -> 8 (planar sides of A and B)", SOLID_FACES, direction(Z_REF, "PERPENDICULAR"))
qvx("t3", "Direction parallel X (B's +X face) -> 4", SOLID_FACES, direction(X_REF))
qvx("t4", "Reject direction parallel Z -> 10", SOLID_FACES, direction(Z_REF, mode="REJECT"))
qvx("t5", "Faces use AXIS parallel Z -> 2 (cylinder side, fillet)", SOLID_FACES, direction(Z_REF, face_use="AXIS"))
qvx("t6", "Geometry CYLINDER keep -> 2", SOLID_FACES, geometry("CYLINDER"))
qvx("t7", "Between A top and B top, fully, bounds in -> 3", SOLID_FACES, between(top(A), top(B), "FULLY_BETWEEN"))
qvx("t8", "Between A top and B top, fully, bounds out -> 1", SOLID_FACES, between(top(A), top(B), "FULLY_BETWEEN", include=False))
qvx("t9", "Between A top and B top, overlaps, bounds in -> 13", SOLID_FACES, between(top(A), top(B), "OVERLAPS"))
qvx("t10", "Between A top and B top, center, bounds in -> 7", SOLID_FACES, between(top(A), top(B), "CENTER_BETWEEN"))
qvx("t11", "Closest to body B -> 6 (A's +X, top, bottom and the cylinder's 3 faces tie at 70 mm)", SOLID_FACES,
    closest('qOwnerBody(%s)' % face_at(B, 120, 0, 25)))
qvx("t12", "Closest to cylinder top face -> 1 (cylinder side)", SOLID_FACES, closest(top(CYL)))
qvx("t13", "Last modified by fillet -> 5 (fillet face; +X, +Y, top, bottom trimmed)", SOLID_FACES, modified_by([FIL]))
qvx("t14", "Arc edge, direction parallel Y by START tangent -> 1", ARC_EDGES, direction(Y_REF, curve="START_TANGENT"))
qvx("t15", "Arc edge, direction parallel Y by END tangent -> 1", ARC_EDGES, direction(Y_REF, curve="END_TANGENT"))
qvx("t16", "Arc edge, direction parallel Y by CHORD -> 0", ARC_EDGES, direction(Y_REF, curve="CHORD"))
qvx("t17", "Arc edge, direction parallel X by CHORD -> 1", ARC_EDGES, direction(X_REF, curve="CHORD"))
qvx("t18", "Chain: parallel Z then between A top and B top -> 3", SOLID_FACES, direction(Z_REF), between(top(A), top(B), "FULLY_BETWEEN"))
