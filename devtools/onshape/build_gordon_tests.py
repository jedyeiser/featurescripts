"""Build the gordonSurface regression cases (Modify curve end, Scaled Curve, Pull surface) as real features in the
gordonSurface document's "Gordon tools tests" Part Studio (created when missing; upsert by name). Fixtures are
native features only (sketches with lines / arcs / circles / interpolated splines, extrudes). Checked by
check_gordon_tests.py. These are the LEGACY fingerprints taken BEFORE the fixes of
reviews/2026-09-25_tools_review/curves.md (section 4.6); cases that document today's bugs are listed in the
checker's EXPECT_FAIL.

Cases sit 1000 mm apart along X. Names: "<case> <what> -> <expected>"; fixtures are "<case> <role>" (no "->").

Modify curve end (MCE). Curve = sketch spline through (0,0) (50,30) (100,0) (150,-30) (200,0) (+x0), end
tangents along (1,1), TRIMMED at spline parameter SPLINE_TRIM so the fixed end has non-zero curvature (a sketch
spline's own ends are natural: curvature 0). Modified end = (x0+200, 0), To point = (x0+200, 30).
  T01 world G0                         -> both ends exact
  T02 frenet (same geometry)           -> identical to T01 (FRENET == WORLD today; recommended to keep)
  T03 fixed end G1                     -> fixed-end tangent = source tangent
  T04 fixed end G2 best effort         -> fixed-end tangent + curvature = source
  T05 fixed end G2 exact               -> same as T04 (EXACT is a no-op today)
  T06 reference line G1 (45 deg line through To)        -> end tangent along the line
  T07 reference face G1 (cylinder R100 through To)      -> end tangent in the face's tangent plane
  T08 project onto a cylinder R500 under the curve      -> output lies on the face
  T09 reference line stored, 'Endpoint continuity ref?' off -> reference ignored, end = T01's (applied today)
MCE rework (curves.md sec 4.1-4.6; same S curve unless noted; reference cases from x = 20000, wires from 30000, holds
from 40000). "W3" = line (0,0)-(60,0) + arc R40 centre (60,40) to (100,40) + spline to (200,100), all G1; its To
point is (200,130). Mate connectors sit on Front-plane sketch points (Z = the Front normal, X = world X).
  T10 reference arc R25 through To, G2 match          -> end tangent + curvature (40/m) + normal = the arc's
  T11 reference mate connector, G1                    -> end tangent along the connector's Z
  T12 as T11 + Opposite direction                     -> end tangent reversed vs T11 (hook info)
  T13 mate connector, G2 radius 50                    -> k = 20/m, normal toward the connector's X
  T14 reference line, G2 match                        -> end curvature 0 (collinear control points)
  T15 no To point, reference line through the end, G1 -> end stays, tangent along the line
  T16 no To point, reference line 10 mm away, G0      -> end snaps onto the line (info)
  T17 reference line 35 mm away, G1                   -> tangent along the line (info: far reference)
  T20 W3 as a composite wire                          -> one edge, ends exact
  T21 W3 sketch edges picked in reverse order         -> same as T20 (within 2 x splineTol)
  T22 W3 wire, no Modified end                        -> end of the chain moved (info)
  T23 closed chain (rectangle)                        -> ERROR (temporary instance)
  T24 two separate lines                              -> ERROR (temporary instance)
  T30 hold point (150,-30) G1                         -> held part unchanged < 1 um, tangent kept at the hold
  T31 hold point (150,-30) G2                         -> + curvature kept at the hold
  T32 W3 hold point on the line (30,0) G1             -> held part within splineTol of the source
  T33 hold distance 60 mm G1                          -> held part unchanged < 1 um
  T34 hold at a spline knot (100,0) G2                -> held part unchanged, G2 at the hold
  T35 hold point at the fixed end (untrimmed curve)   -> same as no hold (info)
  T36 hold point at the modified end                  -> ERROR
  T37 hold distance 1000 mm (longer than the curve)   -> ERROR
  T38 hold (150,-30) G1 + arc G2 + project on Top     -> held part unchanged, end tangent = arc's (curvature info)
  T40 world G0, embedded keys                         -> movedVertex = 1 vertex at To, holdVertex = the fixed-end vertex
MCE P2 (curves.md sec 3 P2 / 4.3 step 7; S curve at x = 50000..52000):
  T41 world G0 + "Carry offset along the curve"       -> both ends exact, curve differs from T01 (offset transported)
  T42 SMOOTHERSTEP transition                         -> both ends exact, fixed-end tangent error <= T01's (LOGISTIC)
      NEEDS a tools version with SMOOTHERSTEP and modifyCurveEnd's transition_functions import re-pinned to it;
      until then the enum value is not in the feature spec and this instance fails to insert / regenerate.
  T43 hold (150,-30) G2 + Remove hold knot            -> as T31 (held part unchanged, G2 at the hold), 2 fewer control points
Scaled Curve (SC):
  SC0 control: two lines y=0 / y=100, Create curve on   -> midline y=50
  SC1 default instance (Create curve left at default)   -> one wire body (creates nothing today)
  SC2 two splines, degree 5                             -> output degree 5 (hard-coded 3 today)
  SC3 concentric arcs R100 / R200                       -> exact R150 (arcs sampled without forceNonRational today)
Scaled Curve native-path rewrite (2026-09-26):
  SC4 Group 0 line drawn right-to-left, factors -0.5 -> +0.5 -> auto-oriented: the straight diagonal (x0,0)-(x0+200,100)
  SC5 concentric circles R100 / R200 (closed / closed)   -> one closed curve, radius 150
  SC6 circle + line (closed / open)                    -> ERROR
  SC7 two S curves, Sample spacing 60, Minimum samples 5 -> INFO: deviation between samples reported; ends exact
Pull surface (P), offsets written straight into mpOffsets/activeOffsets (editing logic does not run over REST):
  P1 G0 4x4, 90 deg cylinder patch R100, +10 at (1,1)   -> boundary on the source boundary along whole edges
  P2 G1 5x5, same patch, +10 at (2,2)                   -> boundary G0 + normals match along whole edges
  P3 G0 4x4, planar top of a block, +10 at (1,2)        -> boundary exact (control)
  P4 G2 7x7, cylinder patch, +10 at (3,3)               -> boundary G0 + normals match along whole edges
  P5 Replace face on a 90 deg cylinder SHEET, G1 5x5    -> source sheet deleted, the pulled sheet replaces it (1 body), edges on R100 + radial normals
  P6 Replace face on the patch SOLID, G0 4x4            -> still 1 solid, boundary on R100, face pulled
  P7 One handle per control point, G0, 7 CPs, CP (3,3)  -> boundary G0, centre pulled 2..9 mm (CP moves 10, surface ~4.4)
  P8 G1 6x6 full cylinder (closed / periodic)           -> closed sheet with 2 edges, boundary G0 + G1, pulled
  P9 G1 6x6 hemisphere (pole)                           -> pole point unchanged, boundary G0 + G1, pulled
     (a pole that is not a collapsed control row errors "boundary spans no parameter range" -- the designed fallback)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_gordon_tests.py
"""
import copy
import os
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
D = "d0950ed72a894fbe35c27d43"  # gordonSurface has no .document.json
W = c.get(f"/api/v10/documents/{D}")["defaultWorkspace"]["id"]
STUDIO = "Gordon tools tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"


def tab(name, feature_type):
    """(namespace, spec) of the custom feature `feature_type` defined in feature studio `name` (correction 38)."""
    t = [e for e in ELEMENTS if e["name"] == name and e["elementType"] == "FEATURESTUDIO"][0]
    specs = c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{t['id']}/featurespecs")["featureSpecs"]
    return "e%s::m%s" % (t["id"], t["microversionId"]), [x for x in specs if x["featureType"] == feature_type][0]


MCE = tab("modifyCurveEnd", "modCurveEnd")
SCALED = tab("scaledCurve", "createScaledCurve")
PULL = tab("pullSurface", "pullSurface")

# Sketch spline parameter where the MCE test curves start (their fixed end). 0 = untrimmed (end curvature 0).
SPLINE_TRIM = 0.15

STATE = {"features": None}


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def integer(pid, n):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": str(n), "isInteger": True}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def upsert(feature):
    if STATE["features"] is None:
        STATE["features"] = c.get(f"{BASE}/features")
    f = STATE["features"]
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    STATE["features"] = None
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


def instance(tab_info, name, given=(), enums=None, arrays=None):
    """A custom feature instance; every spec parameter is sent, those not given take the spec default (correction 38)."""
    ns, spec = tab_info
    given = {p["parameterId"]: p for p in given}
    enums = enums or {}
    arrays = arrays or {}
    params = []
    for p in spec["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in arrays:
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid, "items": arrays[pid]})
        elif pid in given:
            params.append(given[pid])
        elif pid in enums:
            e = copy.deepcopy(d)
            e["parameterId"] = pid
            e["value"] = enums[pid]
            params.append(e)
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return feature(name, spec["featureType"], params, ns)


# ---- sketch geometry (metres) ----
def mmv(*xs):
    return [x / 1000.0 for x in xs]


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def arc(eid, cx, cy, r, a0, a1):
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def circle(eid, cx, cy, r):
    return {"btType": "BTMSketchCurve-4", "entityId": eid, "centerId": eid + ".center", "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def point(eid, x, y):
    return {"btType": "BTMSketchPoint-158", "entityId": eid, "isConstruction": False, "x": x, "y": y}


def spline(eid, pts_mm, d0, d1, start_param=0.0):
    """Interpolated sketch spline through pts_mm with end tangents d0 / d1. Format copied from a saved sketch
    (betterMeasure Part Studio 1, read by GET): centripetal parameters, derivative magnitude ~ the chord length,
    handles at P0 + D0 * du0 / 3 and Pn - D1 * du_last / 3. start_param > 0 trims the start like a sketch trim."""
    pts = [mmv(x, y) for x, y in pts_mm]
    chords = [math.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1]) for i in range(len(pts) - 1)]
    roots = [math.sqrt(ch) for ch in chords]
    du0, du1 = roots[0] / sum(roots), roots[-1] / sum(roots)
    size = sum(chords)
    n0, n1 = math.hypot(*d0), math.hypot(*d1)
    D0 = (d0[0] / n0 * size, d0[1] / n0 * size)
    D1 = (d1[0] / n1 * size, d1[1] / n1 * size)
    n = len(pts)
    geometry = {"btType": "BTCurveGeometryInterpolatedSpline-116", "isPeriodic": False, "derivatives": {},
                "interpolationPoints": [v for p in pts for v in p],
                "startDerivativeX": D0[0], "startDerivativeY": D0[1], "endDerivativeX": D1[0], "endDerivativeY": D1[1],
                "startHandleX": pts[0][0] + D0[0] * du0 / 3, "startHandleY": pts[0][1] + D0[1] * du0 / 3,
                "endHandleX": pts[-1][0] - D1[0] * du1 / 3, "endHandleY": pts[-1][1] - D1[1] * du1 / 3}
    flags = [b(".hasHandlesInSketch", True)] + [b(".%d.hasInternalHandle" % i, i in (0, n - 1)) for i in range(n)]
    counts = [{"btType": "BTMParameterQuantity-147", "parameterId": "splinePointParamCount", "expression": "0.0", "value": 0.0, "isInteger": False},
              {"btType": "BTMParameterQuantity-147", "parameterId": "splinePointCount", "expression": "%d.0" % n, "value": float(n), "isInteger": False}]
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": start_param, "endParam": 1.0, "isConstruction": False, "geometry": geometry,
            "internalIds": ["%s.%d.internal" % (eid, i) for i in range(n)] + [eid + ".startHandle", eid + ".endHandle"],
            "parameters": [b("geometryIsPeriodic", False)] + flags + counts}


def sketch(name, plane_expr, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})


def extrude(name, sk, depth_mm):
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"),
        num("depth", "%g mm" % depth_mm), b("symmetric", True)])


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
FRONT = 'qCreatedBy(makeId("Front"), EntityType.FACE)'


def edges(fid):
    return 'qConstructionFilter(qCreatedBy(makeId("%s"), EntityType.EDGE), ConstructionObject.NO)' % fid


def vertex_at(fid, x, y, z=0):
    """One sketch vertex at (x, y, z); qNthElement in case a spline interpolation point coincides with the end."""
    return 'qNthElement(qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(%g, %g, %g) * millimeter), 0)' % (fid, x, y, z)


def cylinder_face(fid):
    return 'qGeometry(qCreatedBy(makeId("%s"), EntityType.FACE), GeometryType.CYLINDER)' % fid


S_PTS = [(0, 0), (50, 30), (100, 0), (150, -30), (200, 0)]


def s_curve(tag, x0, y0=0.0, trim=SPLINE_TRIM):
    return sketch("%s curve" % tag, TOP, [spline("s", [(x0 + x, y0 + y) for x, y in S_PTS], (1, 1), (1, 1), trim)])


# ---- Modify curve end ----
def mce_case(tag, x0, text, enums=None, given=(), ref=None):
    curve = s_curve(tag, x0)
    to = sketch("%s to point" % tag, TOP, [point("p", *mmv(x0 + 200, 30))])
    base = [q("selEdges", edges(curve)), q("fromPoint", vertex_at(curve, x0 + 200, 0)),
            q("toPoint", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % to), b("createCurve", True)]
    if ref is not None:
        base.append(q("modContinuityRef", ref))
    e = {"offsetFrame": "WORLD"}
    e.update(enums or {})
    return instance(MCE, "%s %s" % (tag, text), base + list(given), e)


def ref_line(tag, x0):
    return sketch("%s reference line" % tag, TOP, [seg("l", *mmv(x0 + 180, 10, x0 + 220, 50))])


mce_case("T01", 0, "world G0 -> both ends exact")
mce_case("T02", 1000, "frenet G0 -> identical to T01", {"offsetFrame": "FRENET"})
mce_case("T03", 2000, "fixed end G1 -> fixed tangent = source", {"fixedEndContinuity": "G1"})
mce_case("T04", 3000, "fixed end G2 best effort -> fixed tangent + curvature = source",
         {"fixedEndContinuity": "G2", "g2Mode": "BEST_EFFORT"})
mce_case("T05", 4000, "fixed end G2 exact -> fixed tangent + curvature = source",
         {"fixedEndContinuity": "G2", "g2Mode": "EXACT"})
l6 = ref_line("T06", 5000)
mce_case("T06", 5000, "reference line G1 -> end tangent along the 45 deg line",
         {"modEndContinuity": "G1"}, [b("showModContinuity", True)], edges(l6))
c7 = sketch("T07 reference cylinder (sketch)", TOP, [circle("c", *mmv(6000 + 300, 30, 100))])
f7 = extrude("T07 reference cylinder", c7, 40)
mce_case("T07", 6000, "reference face G1 -> end tangent in the cylinder's tangent plane",
         {"modEndContinuity": "G1"}, [b("showModContinuity", True)], cylinder_face(f7))
# Front plane: sketch x = world X, sketch y = world Z. R500 cylinder along Y, top at z = -20 under the curve.
c8 = sketch("T08 projection cylinder (sketch)", FRONT, [circle("c", *mmv(7000 + 100, -520, 500))])
f8 = extrude("T08 projection cylinder", c8, 300)
mce_case("T08", 7000, "project onto the R500 cylinder -> curve on the face", given=[b("curveOnSurface", True), q("projectionFace", cylinder_face(f8))])
l9 = ref_line("T09", 8000)
mce_case("T09", 8000, "reference line stored, checkbox off -> ignored, end tangent = T01",
         {"modEndContinuity": "G1"}, [b("showModContinuity", False)], edges(l9))


# ---- Modify curve end: rework cases (reference, wire, hold) ----
def mce_run(tag, text, sel, frm, to, enums=None, given=(), ref=None):
    """An MCE instance with explicit selections; frm / to None = left empty."""
    base = [q("selEdges", sel), q("fromPoint", *([frm] if frm else [])), q("toPoint", *([to] if to else []))]
    if ref is not None:
        base.append(q("modContinuityRef", ref))
    e = {"offsetFrame": "WORLD"}
    e.update(enums or {})
    return instance(MCE, "%s %s" % (tag, text), base + list(given), e)


def point_sketch(name, x, y):
    return sketch(name, TOP, [point("p", *mmv(x, y))])


def vertex_of(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def body_of(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def connector(name, x, z):
    """Mate connector on a Front-plane sketch point at world (x, 0, z)."""
    sk = sketch(name + " (sketch)", FRONT, [point("p", *mmv(x, z))])
    return feature(name, "mateConnector", [
        en("originType", "OriginCreationType", "ON_ENTITY"), q("originQuery", vertex_of(sk)),
        en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])


def s_case(tag, x0, trim=SPLINE_TRIM, to_xy=(200, 30)):
    """The S curve plus its 'to point' fixture (the expected end position, whether or not it is passed)."""
    curve = s_curve(tag, x0, 0.0, trim)
    to = point_sketch("%s to point" % tag, x0 + to_xy[0], to_xy[1])
    return curve, to


def w3(tag, x0):
    """Line + arc + spline, G1 at both joins, from (x0, 0) to (x0 + 200, 100)."""
    return sketch("%s curve" % tag, TOP, [
        seg("l", *mmv(x0, 0, x0 + 60, 0)),
        arc("a", *mmv(x0 + 60, 40, 40), -math.pi / 2, 0.0),
        spline("s", [(x0 + 100, 40), (x0 + 120, 90), (x0 + 160, 110), (x0 + 200, 100)], (0, 1), (1, -0.5))])


REF_G1 = {"modEndContinuity": "G1"}
REF_G2 = {"modEndContinuity": "G2"}
REF_ON = [b("showModContinuity", True)]
MANY_CP = [integer("splineCP", 50)]

# Reference cases
x = 20000
cv, t = s_case("T10", x)
a10 = sketch("T10 reference arc", TOP, [arc("a", *mmv(x + 200, 55, 25), math.radians(-150), math.radians(-30))])
mce_run("T10", "reference arc R25 G2 -> end tangent, curvature 40/m and normal = the arc's", edges(cv), vertex_at(cv, x + 200, 0),
        vertex_of(t), REF_G2, REF_ON, edges(a10))

x = 21000
cv, t = s_case("T11", x)
mc11 = connector("T11 reference connector", x + 200, 0)
mce_run("T11", "reference mate connector G1 -> end tangent along its Z", edges(cv), vertex_at(cv, x + 200, 0), vertex_of(t),
        REF_G1, REF_ON, body_of(mc11))

x = 22000
cv, t = s_case("T12", x)
mc12 = connector("T12 reference connector", x + 200, 0)
mce_run("T12", "mate connector G1, Opposite direction -> end tangent reversed vs T11", edges(cv), vertex_at(cv, x + 200, 0),
        vertex_of(t), REF_G1, REF_ON + [b("flipRef", True)], body_of(mc12))

x = 23000
cv, t = s_case("T13", x)
mc13 = connector("T13 reference connector", x + 200, 0)
mce_run("T13", "mate connector G2 radius 50 -> k 20/m toward its X", edges(cv), vertex_at(cv, x + 200, 0), vertex_of(t),
        dict(REF_G2, modCurvatureMode="RADIUS"), REF_ON + [num("modEndRadius", "50 mm")], body_of(mc13))

x = 24000
cv, t = s_case("T14", x)
l14 = sketch("T14 reference line", TOP, [seg("l", *mmv(x + 180, 10, x + 220, 50))])
mce_run("T14", "reference line G2 -> end curvature 0", edges(cv), vertex_at(cv, x + 200, 0), vertex_of(t), REF_G2, REF_ON, edges(l14))

x = 25000
cv, t = s_case("T15", x, to_xy=(200, 0))
l15 = sketch("T15 reference line", TOP, [seg("l", *mmv(x + 180, -20, x + 220, 20))])
mce_run("T15", "no To point, reference line through the end G1 -> end stays, tangent along the line", edges(cv),
        vertex_at(cv, x + 200, 0), None, REF_G1, REF_ON, edges(l15))

x = 26000
cv, t = s_case("T16", x, to_xy=(210, 0))
l16 = sketch("T16 reference line", TOP, [seg("l", *mmv(x + 210, -20, x + 210, 20))])
mce_run("T16", "no To point, reference line 10 mm away G0 -> end snaps onto the line (info)", edges(cv),
        vertex_at(cv, x + 200, 0), None, None, REF_ON, edges(l16))

x = 27000
cv, t = s_case("T17", x)
l17 = sketch("T17 reference line", TOP, [seg("l", *mmv(x + 230, 10, x + 270, 50))])
mce_run("T17", "reference line 35 mm away G1 -> tangent along the line (info)", edges(cv), vertex_at(cv, x + 200, 0),
        vertex_of(t), REF_G1, REF_ON, edges(l17))

# Wire cases
x = 30000
cv = w3("T20", x)
t = point_sketch("T20 to point", x + 200, 130)
wire20 = feature("T20 wire", "compositeCurve", [q("edges", edges(cv))])
mce_run("T20", "W3 as a wire -> one edge, ends exact", body_of(wire20), vertex_at(cv, x + 200, 100), vertex_of(t), given=MANY_CP)

x = 31000
cv = w3("T21", x)
t = point_sketch("T21 to point", x + 200, 130)
mce_run("T21", "W3 edges in reverse order -> same as T20",
        "qUnion([%s])" % ", ".join("qNthElement(%s, %d)" % (edges(cv), i) for i in (2, 1, 0)),
        vertex_at(cv, x + 200, 100), vertex_of(t), given=MANY_CP)

x = 32000
cv = w3("T22", x)
t = point_sketch("T22 to point", x + 200, 130)
wire22 = feature("T22 wire", "compositeCurve", [q("edges", edges(cv))])
mce_run("T22", "W3 wire, no Modified end -> chain end moved (info)", body_of(wire22), None, vertex_of(t), given=MANY_CP)

x = 33000
cv = sketch("T23 curve", TOP, [seg("a", *mmv(x, 0, x + 100, 0)), seg("b", *mmv(x + 100, 0, x + 100, 50)),
                              seg("c", *mmv(x + 100, 50, x, 50)), seg("d", *mmv(x, 50, x, 0))])
t = point_sketch("T23 to point", x + 100, 80)
mce_run("T23", "closed chain -> ERROR", edges(cv), vertex_at(cv, x + 100, 50), vertex_of(t))

x = 34000
cv = sketch("T24 curve", TOP, [seg("a", *mmv(x, 0, x + 100, 0)), seg("b", *mmv(x, 50, x + 100, 50))])
t = point_sketch("T24 to point", x + 100, 80)
mce_run("T24", "two separate lines -> ERROR", edges(cv), vertex_at(cv, x + 100, 50), vertex_of(t))

# Hold cases
HOLD_ON = [b("useHold", True)]
HOLD_G1 = {"fixedEndContinuity": "G1"}
HOLD_G2 = {"fixedEndContinuity": "G2"}


def hold_case(tag, x0, text, hold_xy=None, distance=None, enums=None, given=(), trim=SPLINE_TRIM, ref=None):
    cv, t = s_case(tag, x0, trim)
    extra = list(HOLD_ON) + list(given)
    e = dict(enums or {})
    if hold_xy is not None:
        h = point_sketch("%s hold point" % tag, x0 + hold_xy[0], hold_xy[1])
        extra.append(q("holdPoint", vertex_of(h)))
    if distance is not None:
        e["holdMode"] = "DISTANCE"
        extra.append(num("holdDistance", "%g mm" % distance))
    return mce_run(tag, text, edges(cv), vertex_at(cv, x0 + 200, 0), vertex_of(t), e, extra, ref)


hold_case("T30", 40000, "hold point G1 -> held part unchanged, tangent kept at the hold", (150, -30), enums=HOLD_G1)
hold_case("T31", 41000, "hold point G2 -> held part unchanged, curvature kept at the hold", (150, -30), enums=HOLD_G2)

x = 42000
cv = w3("T32", x)
t = point_sketch("T32 to point", x + 200, 130)
h = point_sketch("T32 hold point", x + 30, 0)
mce_run("T32", "W3 hold on the line G1 -> held part within splineTol", edges(cv), vertex_at(cv, x + 200, 100), vertex_of(t),
        HOLD_G1, HOLD_ON + MANY_CP + [q("holdPoint", vertex_of(h))])

hold_case("T33", 43000, "hold distance 60 G1 -> held part unchanged", distance=60, enums=HOLD_G1)
hold_case("T34", 44000, "hold at a spline knot G2 -> held part unchanged, G2 at the hold", (100, 0), enums=HOLD_G2)
hold_case("T35", 45000, "hold at the fixed end -> same as no hold (info)", (0, 0), enums=HOLD_G1, trim=0.0)
hold_case("T36", 46000, "hold at the modified end -> ERROR", (200, 0), enums=HOLD_G1)
hold_case("T37", 47000, "hold distance 1000 -> ERROR", distance=1000, enums=HOLD_G1)

x = 48000
a38 = sketch("T38 reference arc", TOP, [arc("a", *mmv(x + 200, 55, 25), math.radians(-150), math.radians(-30))])
hold_case("T38", x, "hold G1 + arc G2 + project on Top -> held part unchanged, end tangent = arc's (info)", (150, -30),
          enums=dict(HOLD_G1, **REF_G2), given=REF_ON + [b("curveOnSurface", True), q("projectionFace", TOP)], ref=edges(a38))

x = 49000
cv, t = s_case("T40", x)
mce_run("T40", "world G0, embedded keys -> movedVertex at To, holdVertex at the fixed end", edges(cv), vertex_at(cv, x + 200, 0),
        vertex_of(t))

# MCE P2: transport offset, SMOOTHERSTEP, hold-knot removal
mce_case("T41", 50000, "transport offset -> ends exact, differs from T01", given=[b("transportOffset", True)])
# T42 needs the tools version with SMOOTHERSTEP (re-pin transition_functions first); set GORDON_T42=1 to build it.
if os.environ.get("GORDON_T42"):
    mce_case("T42", 51000, "smootherstep transition -> ends exact, fixed-end tangent no worse than T01", {"transitionType": "SMOOTHERSTEP"})
hold_case("T43", 52000, "hold G2 + remove hold knot -> held part unchanged, 2 fewer control points than T31", (150, -30),
          enums=HOLD_G2, given=[b("removeHoldKnot", True)])


# ---- Scaled Curve ----
def scaled(tag, text, g0, g1, given=()):
    return instance(SCALED, "%s %s" % (tag, text), [q("group0", edges(g0)), q("group1", edges(g1))] + list(given))


g0 = sketch("SC0 group 0", TOP, [seg("l", *mmv(10000, 0, 10200, 0))])
g1 = sketch("SC0 group 1", TOP, [seg("l", *mmv(10000, 100, 10200, 100))])
scaled("SC0", "two lines, Create curve on -> midline y = 50", g0, g1, [b("createCurve", True)])

g0 = sketch("SC1 group 0", TOP, [seg("l", *mmv(11000, 0, 11200, 0))])
g1 = sketch("SC1 group 1", TOP, [seg("l", *mmv(11000, 100, 11200, 100))])
scaled("SC1", "default instance -> one wire body", g0, g1)

g0 = s_curve("SC2 group 0", 12000, 0, 0.0)
g1 = s_curve("SC2 group 1", 12000, 100, 0.0)
scaled("SC2", "two splines, degree 5 -> output degree 5", g0, g1, [b("createCurve", True), integer("scaledDegree", 5)])

g0 = sketch("SC3 group 0", TOP, [arc("a", *mmv(13000, 0, 100), 0, math.pi / 2)])
g1 = sketch("SC3 group 1", TOP, [arc("a", *mmv(13000, 0, 200), 0, math.pi / 2)])
scaled("SC3", "arcs R100 / R200 -> exact R150", g0, g1, [b("createCurve", True)])

# Native-path rewrite cases
g0 = sketch("SC4 group 0", TOP, [seg("l", *mmv(14200, 0, 14000, 0))])
g1 = sketch("SC4 group 1", TOP, [seg("l", *mmv(14000, 100, 14200, 100))])
scaled("SC4", "reversed Group 0, factors -0.5 to 0.5 -> auto-oriented straight diagonal", g0, g1, [num("sf0", "-0.5"), num("sf1", "0.5")])

g0 = sketch("SC5 group 0", TOP, [circle("c", *mmv(18000, 0, 100))])
g1 = sketch("SC5 group 1", TOP, [circle("c", *mmv(18000, 0, 200))])
scaled("SC5", "circles R100 / R200 closed -> closed R150", g0, g1)

g0 = sketch("SC6 group 0", TOP, [circle("c", *mmv(19000, 0, 100))])
g1 = sketch("SC6 group 1", TOP, [seg("l", *mmv(18800, 300, 19200, 300))])
scaled("SC6", "closed circle with open line -> ERROR", g0, g1)

g0 = s_curve("SC7 group 0", 53000, 0, 0.0)
g1 = s_curve("SC7 group 1", 53000, 100, 0.0)
scaled("SC7", "S curves, spacing 60, 5 samples -> INFO deviation between samples", g0, g1,
       [num("sampleSpacing", "60 mm"), integer("numScaledSamples", 5)])


# ---- Pull surface ----
def pull(tag, text, face_expr, n, continuity, offsets, given=()):
    """offsets {(i, j): mm}; written into both the hidden flat cache mpOffsets (what the body reads) and the
    visible activeOffsets list, as the editing logic / manipulator would. In control-point mode (i, j) are
    control-point indices and only activeOffsets is read. given: extra parameters (replaceFace, ...)."""
    mp = [{"btType": "BTMArrayParameterItem-1843", "parameters": [num("off", "%g mm" % offsets.get((k // n, k % n), 0))]}
          for k in range(n * n)]
    active = [{"btType": "BTMArrayParameterItem-1843", "parameters": [integer("u", i), integer("v", j), num("value", "%g mm" % v)]}
              for (i, j), v in sorted(offsets.items())]
    return instance(PULL, "%s %s" % (tag, text), [q("face", face_expr), integer("uCurveCount", n), integer("vCurveCount", n)] + list(given),
                    {"continuityType": continuity}, {"mpOffsets": mp, "activeOffsets": active})


def cylinder_patch(tag, x0):
    """R100 arc from -45 to +45 deg about (x0, 0) closed by its chord, extruded 100 symmetric: a 90 deg cylinder patch."""
    r = 100.0
    a = math.pi / 4
    p0 = (x0 + r * math.cos(-a), r * math.sin(-a))
    p1 = (x0 + r * math.cos(a), r * math.sin(a))
    sk = sketch("%s patch (sketch)" % tag, TOP, [arc("a", *mmv(x0, 0, r), -a, a), seg("l", *mmv(p1[0], p1[1], p0[0], p0[1]))])
    return cylinder_face(extrude("%s patch" % tag, sk, 100))


pull("P1", "G0 4x4 cylinder patch, +10 at (1,1) -> boundary on the source boundary", cylinder_patch("P1", 15000), 4, "G0", {(1, 1): 10})
pull("P2", "G1 5x5 cylinder patch, +10 at (2,2) -> boundary G0 + normals along whole edges", cylinder_patch("P2", 16000), 5, "G1", {(2, 2): 10})
sk3 = sketch("P3 block (sketch)", TOP, [seg("a", *mmv(16950, -50, 17050, -50)), seg("b", *mmv(17050, -50, 17050, 50)),
                                        seg("c", *mmv(17050, 50, 16950, 50)), seg("d", *mmv(16950, 50, 16950, -50))])
f3 = extrude("P3 block", sk3, 20)
pull("P3", "G0 4x4 planar top face, +10 at (1,2) -> boundary exact (control)",
     'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.FACE), vector(17000, 0, 10) * millimeter)' % f3, 4, "G0", {(1, 2): 10})

# Pull surface P2 upgrades (curves.md P2): G2, replace face, one handle per control point, closed face, pole.
pull("P4", "G2 7x7 cylinder patch, +10 at (3,3) -> boundary G0 + normals along whole edges", cylinder_patch("P4", 60000), 7, "G2",
     {(3, 3): 10})

sk5 = sketch("P5 sheet (sketch)", TOP, [arc("a", *mmv(61000, 0, 100), -math.pi / 4, math.pi / 4)])
f5 = feature("P5 sheet", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SURFACE"), q("surfaceEntities", edges(sk5)),
    en("endBound", "BoundingType", "BLIND"), num("depth", "100 mm"), b("symmetric", True)])
pull("P5", "replace face on a sheet, G1 5x5, +10 at (2,2) -> sheet keeps 1 face, edges G0 + G1, face pulled",
     cylinder_face(f5), 5, "G1", {(2, 2): 10}, [b("replaceFace", True)])

pull("P6", "replace face on a solid, G0 4x4, +10 at (1,1) -> still 1 solid, boundary G0, face pulled",
     cylinder_patch("P6", 62000), 4, "G0", {(1, 1): 10}, [b("replaceFace", True)])

pull("P7", "one handle per control point, G0 7 CPs, +10 at CP (3,3) -> boundary G0, centre pulled 2..9 mm",
     cylinder_patch("P7", 63000), 7, "G0", {(3, 3): 10}, [b("handlePerControlPoint", True)])

sk8 = sketch("P8 cylinder (sketch)", TOP, [circle("c", *mmv(64000, 0, 100))])
f8 = extrude("P8 cylinder", sk8, 100)
pull("P8", "G1 6x6 full cylinder (closed), +10 at (2,2) -> closed sheet (2 edges), boundary G0 + G1",
     cylinder_face(f8), 6, "G1", {(2, 2): 10})

# Hemisphere: quarter disc on Front (sketch y = world Z) revolved about the line x = 65000; pole at (65000, 0, 100).
sk9 = sketch("P9 hemisphere (sketch)", FRONT, [arc("a", *mmv(65000, 0, 100), 0, math.pi / 2),
                                               seg("l1", *mmv(65000, 100, 65000, 0)), seg("l2", *mmv(65000, 0, 65100, 0))])
ax9 = sketch("P9 axis", FRONT, [seg("l", *mmv(65000, 0, 65000, 100))])
f9 = feature("P9 hemisphere", "revolve", [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
    q("entities", 'qSketchRegion(makeId("%s"))' % sk9), q("axis", edges(ax9)), b("fullRevolve", True)])
pull("P9", "G1 6x6 hemisphere (pole), +10 at (2,2) -> pole fixed, boundary G0 + G1",
     'qGeometry(qCreatedBy(makeId("%s"), EntityType.FACE), GeometryType.SPHERE)' % f9, 6, "G1", {(2, 2): 10})

print("studio", E)
