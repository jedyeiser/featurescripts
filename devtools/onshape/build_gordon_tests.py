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
Scaled Curve (SC):
  SC0 control: two lines y=0 / y=100, Create curve on   -> midline y=50
  SC1 default instance (Create curve left at default)   -> one wire body (creates nothing today)
  SC2 two splines, degree 5                             -> output degree 5 (hard-coded 3 today)
  SC3 concentric arcs R100 / R200                       -> exact R150 (arcs sampled without forceNonRational today)
Pull surface (P), offsets written straight into mpOffsets/activeOffsets (editing logic does not run over REST):
  P1 G0 4x4, 90 deg cylinder patch R100, +10 at (1,1)   -> boundary on the source boundary along whole edges
  P2 G1 5x5, same patch, +10 at (2,2)                   -> boundary G0 + normals match along whole edges
  P3 G0 4x4, planar top of a block, +10 at (1,2)        -> boundary exact (control)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_gordon_tests.py
"""
import copy
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


# ---- Pull surface ----
def pull(tag, text, face_expr, n, continuity, offsets):
    """offsets {(i, j): mm}; written into both the hidden flat cache mpOffsets (what the body reads) and the
    visible activeOffsets list, as the editing logic / manipulator would."""
    mp = [{"btType": "BTMArrayParameterItem-1843", "parameters": [num("off", "%g mm" % offsets.get((k // n, k % n), 0))]}
          for k in range(n * n)]
    active = [{"btType": "BTMArrayParameterItem-1843", "parameters": [integer("u", i), integer("v", j), num("value", "%g mm" % v)]}
              for (i, j), v in sorted(offsets.items())]
    return instance(PULL, "%s %s" % (tag, text), [q("face", face_expr), integer("uCurveCount", n), integer("vCurveCount", n)],
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

print("studio", E)
