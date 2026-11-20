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

LPTS = [(300, -50), (380, -50), (380, 0), (340, 0), (340, 50), (300, 50)]
L_SK = sketch("F5 L-shape (sketch)", [seg("l%d" % i, LPTS[i][0] / 1000, LPTS[i][1] / 1000, LPTS[(i + 1) % 6][0] / 1000, LPTS[(i + 1) % 6][1] / 1000) for i in range(6)])
LSHAPE = extrude("F5 L-shaped prism 20 high (one concave edge at (340,0))", L_SK, "20 mm")
L_FACES = 'qCreatedBy(makeId("%s"), EntityType.FACE)' % LSHAPE
# the original 16-face fixture only: the L-shape is used by the face convexity tests
SOLID_FACES = "qSubtraction(qBodyType(qEverything(EntityType.FACE), BodyType.SOLID), %s)" % L_FACES
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
            b("fltSizeAllowEqual", False), en("fltConvexity", "EdgeConvexityType", "CONVEX", NS), en("fltEdgeTopology", "EdgeTopology", "ONE_SIDED", NS), flist("fltModifiedBy", []),
            q("fltDistRef"), en("fltDistMode", "DistanceMode", "CLOSEST", NS), {"btType": "BTMParameterQuantity-147", "parameterId": "fltDistCount", "expression": "1", "isInteger": True},
            num("fltDistFrom", "0 mm"), num("fltDistTo", "0 mm"), num("fltDistValue", "0 mm"),
            q("fltWithinRef"), num("fltWithinDist", "0 mm"), en("fltWithinMeasure", "WithinMeasure", "NEAREST", NS), b("fltWithinIncludeRef", False),
            q("fltAdjRef"), en("fltAdjKind", "AdjacencyKind", "EDGE", NS), {"btType": "BTMParameterQuantity-147", "parameterId": "fltAdjSteps", "expression": "1", "isInteger": True}, b("fltAdjIncludeRef", False),
            q("fltAxisRef"), num("fltAxisTol", "0 mm"), num("fltAxisAngleTol", "0 deg"), en("fltFaceConvexity", "FaceConvexity", "CONVEX", NS),
            q("fltSelection")]


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


def qvx(var, label, source, *filters, combine="ALL"):
    post({"btType": "BTMFeature-134", "featureType": "queryVariable", "name": "%s %s" % (var, label), "namespace": NS, "parameters": [
        s("name", var), s("description", ""), en("selectionType", "SelectionType", "SELECTION", NS), q("selectionQuery", source),
        b("addAdditionalQueries", False), arr("additionalQueries", []), arr("filters", [item(f) for f in filters]), en("filterCombine", "FilterCombine", combine, NS), b("debugStages", False), b("specifyOutputType", False),
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
qvx("t19", "Position coincides with ANY of two planes (B top, cylinder top) -> 2", SOLID_FACES,
    [en("fltMode", "FilterMode", "KEEP", NS), en("fltType", "FilterType", "POSITION", NS),
     en("fltPosType", "PositionalDirectionalType", "COINCIDES_WITH_PLANE", NS), q("fltPosPlane", top(B), top(CYL))])
qvx("t20", "Combine ANY: keep direction parallel Z, keep geometry CYLINDER -> 8", SOLID_FACES, direction(Z_REF), geometry("CYLINDER"), combine="ANY")
qvx("t21", "Combine ANY, then reject CYLINDER -> 6", SOLID_FACES, direction(Z_REF), geometry("CYLINDER"), geometry("CYLINDER", mode="REJECT"), combine="ANY")

def flt(kind, **kw):
    return [en("fltMode", "FilterMode", kw.pop("mode", "KEEP"), NS), en("fltType", "FilterType", kind, NS)] + list(kw.values())


def dist(ref, how, mode="KEEP", count=None, value=None, value2=None):
    p = [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "DISTANCE", NS), q("fltDistRef", ref), en("fltDistMode", "DistanceMode", how, NS)]
    if count is not None:
        p.append({"btType": "BTMParameterQuantity-147", "parameterId": "fltDistCount", "expression": str(count), "isInteger": True})
    if how == "BETWEEN_DISTANCES":
        p += [num("fltDistFrom", value), num("fltDistTo", value2)]
    elif value is not None:
        p.append(num("fltDistValue", value))
    return p


def adjacent(ref, kind="EDGE", steps=1, include=False):
    return [en("fltMode", "FilterMode", "KEEP", NS), en("fltType", "FilterType", "ADJACENT", NS), q("fltAdjRef", ref), en("fltAdjKind", "AdjacencyKind", kind, NS),
            {"btType": "BTMParameterQuantity-147", "parameterId": "fltAdjSteps", "expression": str(steps), "isInteger": True}, b("fltAdjIncludeRef", include)]


def coaxial(ref):
    return [en("fltMode", "FilterMode", "KEEP", NS), en("fltType", "FilterType", "COAXIAL", NS), q("fltAxisRef", ref), num("fltAxisTol", "0 mm"), num("fltAxisAngleTol", "0 deg")]


def face_convexity(kind):
    return [en("fltMode", "FilterMode", "KEEP", NS), en("fltType", "FilterType", "FACE_CONVEXITY", NS), en("fltFaceConvexity", "FaceConvexity", kind, NS)]


# Distances from B's +X face (x = 120): A's -X face is 150 away, A's -Y and +Y faces sqrt(90^2+10^2) = 90.55, the cylinder's 3 faces 70,
# B's -X face 20, B's other four faces touch it (0).
qvx("t22", "Distance furthest 1 from B +X face -> 1 (A's -X face, 150 mm)", SOLID_FACES, dist(X_REF, "FURTHEST", count=1))
qvx("t23", "Distance furthest 2 from B +X face -> 2 (A's -X at 150, then A's +Y at 95.5: it ends at x = 25 where the fillet starts)", SOLID_FACES, dist(X_REF, "FURTHEST", count=2))
qvx("t24", "Distance nearer than 25 mm from B +X face -> 5 (B's other five faces)", SOLID_FACES, dist(X_REF, "NEARER_THAN", value="25 mm"))
qvx("t25", "Distance farther than 100 mm from B +X face -> 1 (A's -X)", SOLID_FACES, dist(X_REF, "FARTHER_THAN", value="100 mm"))
qvx("t26", "Distance between 60 and 80 mm from B +X face -> 3 (the cylinder's faces at 70)", SOLID_FACES, dist(X_REF, "BETWEEN_DISTANCES", value="60 mm", value2="80 mm"))
qvx("t27", "Reject distance nearer than 25 mm from B +X face -> 11 (16 minus the 5 matches; the reference face itself is not a match, so it stays)", SOLID_FACES, dist(X_REF, "NEARER_THAN", mode="REJECT", value="25 mm"))
qvx("t28", "Adjacent (share an edge) to A's top face -> 5 (-X, +X, -Y, +Y, fillet)", SOLID_FACES, adjacent(top(A)))
qvx("t29", "Adjacent to A's top face, include the reference -> 6", SOLID_FACES, adjacent(top(A), include=True))
qvx("t30", "Adjacent to A's top face, 2 steps -> 6 (the five plus A's bottom)", SOLID_FACES, adjacent(top(A), steps=2))
qvx("t31", "Adjacent (share a vertex) to A's top face -> 5", SOLID_FACES, adjacent(top(A), kind="VERTEX"))
qvx("t32", "Coaxial with the cylinder's top circular edge -> 1 (its side face)", SOLID_FACES, coaxial('qContainsPoint(qEverything(EntityType.EDGE), vector(210, 0, 30) * millimeter)'))
qvx("t33", "Coaxial with the fillet face -> 0 (nothing else shares its axis)", SOLID_FACES, coaxial('qGeometry(qCreatedBy(makeId("%s"), EntityType.FACE), GeometryType.CYLINDER)' % FIL))
def within(refs, value, measure="NEAREST", mode="KEEP", include=False):
    return [en("fltMode", "FilterMode", mode, NS), en("fltType", "FilterType", "WITHIN", NS), q("fltWithinRef", *refs), num("fltWithinDist", value),
            en("fltWithinMeasure", "WithinMeasure", measure, NS), b("fltWithinIncludeRef", include)]


# B is x 100..120, y -10..10, z 0..50; A is x -30..30, y -20..20, z 0..20. B's five other faces are 0 (touching) to 20 (-X face) from the +X face.
A_NEG_X = face_at(A, -30, 0, 10)     # A's -X face
qvx("t39", "Within 25 mm (nearest) of B +X face -> 5 (B's other five faces; same as Distance nearer than)", SOLID_FACES, within([X_REF], "25 mm"))
qvx("t40", "Within 25 mm of B +X face, include the references -> 6", SOLID_FACES, within([X_REF], "25 mm", include=True))
qvx("t41", "Within 15 mm (nearest) of B +X face -> 4 (the four faces touching it; the -X face is 20 away)", SOLID_FACES, within([X_REF], "15 mm"))
qvx("t42", "Within 15 mm (center) of B +X face -> 4 (centroids of the +-Y and +-Z faces are 10 away; the -X face centroid is 20)", SOLID_FACES, within([X_REF], "15 mm", measure="CENTER"))
qvx("t43", "Within 15 mm (whole entity) of B +X face -> 0 (every B face has a vertex at x = 100, 20 away)", SOLID_FACES, within([X_REF], "15 mm", measure="WHOLE"))
qvx("t44", "Within 25 mm (whole entity) of B +X face -> 5", SOLID_FACES, within([X_REF], "25 mm", measure="WHOLE"))
qvx("t45", "Within 0 mm of B +X face OR A -X face -> 8 (the four B faces touching the +X face plus A's -Y, +Y, top, bottom)", SOLID_FACES, within([X_REF, A_NEG_X], "0 mm"))
qvx("t46", "Reject within 25 mm of B +X face -> 11 (16 minus 5; the reference stays)", SOLID_FACES, within([X_REF], "25 mm", mode="REJECT"))
qvx("t34", "Face convexity CONVEX on the L-shape -> 6 (all but the two faces at the concave edge)", L_FACES, face_convexity("CONVEX"))
qvx("t35", "Face convexity HAS_CONCAVE on the L-shape -> 2", L_FACES, face_convexity("HAS_CONCAVE"))
qvx("t36", "Face convexity MIXED on the L-shape -> 2", L_FACES, face_convexity("MIXED"))
qvx("t37", "Face convexity CONCAVE on the L-shape -> 0", L_FACES, face_convexity("CONCAVE"))
qvx("t38", "Face convexity CONVEX on the 16-face fixture -> 16 (every sharp edge is convex)", SOLID_FACES, face_convexity("CONVEX"))