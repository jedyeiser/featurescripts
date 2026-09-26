"""Arc / line tangency (2026-09-25 review): test cases as real features in Curve_tools' "Arc tangency tests" Part
Studio (upsert by name). Checked by check_arc_tangency_tests.py.

Fixtures (sketches on Top, mm):
  F1  interpolated spline through 9 points of an R100 quarter circle, exact end tangents
  F2  line -> spline on R200 (30 deg, tangent both ends) -> line, one tangent chain at y 300
  F3  S-curve spline at y 600
  F4  coarse spline: 3 points of an R50 circle over 120 deg, exact end tangents, at y 800
  F5  chain at y 1000: spline A, spline B meeting A at a ~28 deg corner, chords both along +X
  F6  chain at y 1200: spline A, spline C leaving A tangent (G1)
  F7  line y 1400 x 0..300 (map from-chain); F8 spline on R500 at y 1600 (near-circle); F9 sketch arc R500 at y 1800

Cases:
  R1 Recognize arcs on F1, tol 0.01 mm                -> 1 of 1, output 1 edge, an arc R100
  R2 Recognize arcs on F2 (3 edges)                    -> 1 of 1, output 1 wire of 3 edges: line, arc R200, line; joints <= 0.05 deg (the max tangent change)
  R3 Recognize arcs on F3 (S-curve)                    -> 0 of 1, output edge still a spline
  R4 Recognize arcs on F1, report only                 -> no output body
  R5 Recognize arcs on F4, tol 0.01 mm                 -> 0 of 1 (off the arc)
  P12 Evaluate profiles Efficient on F5 (corner 28 deg) -> 2 edges (corner kept; merged before the fix)
  P13 Evaluate profiles Efficient on F6 (tangent)       -> 1 edge (merged)
  M1 Map curve F7 onto F8 (near-circle SPLINE host)     -> 1 edge, NOT an arc (an arc with kinked ends before the fix)
  M2 Map curve F7 onto F9 (true arc host)               -> 1 edge, an arc R500

usage (repo root): PYTHONPATH=. python devtools/onshape/build_arc_tangency_tests.py
"""
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("curve_tools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Arc tangency tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


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


def upsert(feature):
    f = c.get(f"{BASE}/features")
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{BASE}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{BASE}/features", body)
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L / 1000, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0 / 1000, "pntY": y0 / 1000, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def arc(eid, cx, cy, r, a0, a1):
    """Sketch arc on centre (cx, cy) mm, radius r, counter-clockwise from angle a0 to a1 (radians)."""
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r / 1000, "xCenter": cx / 1000, "yCenter": cy / 1000,
                         "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def spline(eid, pts, t0, t1):
    """Interpolated sketch spline through pts (mm) with end tangents t0 / t1 (along the point order), in the format
    Onshape stores (centripetal knots; handle = end point +- derivative * span / 3). From build_footprint_tests.py."""
    t0 = (t0[0] / math.hypot(*t0), t0[1] / math.hypot(*t0))
    t1 = (t1[0] / math.hypot(*t1), t1[1] / math.hypot(*t1))
    P = [(x / 1000, y / 1000) for x, y in pts]
    ch = [math.dist(P[i], P[i + 1]) for i in range(len(P) - 1)]
    qs = [math.sqrt(v) for v in ch]
    du = [v / sum(qs) for v in qs]
    s0, s1 = ch[0] / du[0], ch[-1] / du[-1]
    d0, d1 = (t0[0] * s0, t0[1] * s0), (t1[0] * s1, t1[1] * s1)
    n = len(P)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": 1.0, "isConstruction": False, "centerId": "",
            "internalIds": ["%s.%d.internal" % (eid, i) for i in range(n)] + [eid + ".startHandle", eid + ".endHandle"],
            "geometry": {"btType": "BTCurveGeometryInterpolatedSpline-116", "isPeriodic": False, "derivatives": {},
                         "interpolationPoints": [v for p in P for v in p],
                         "startDerivativeX": d0[0], "startDerivativeY": d0[1], "endDerivativeX": d1[0], "endDerivativeY": d1[1],
                         "startHandleX": P[0][0] + d0[0] * du[0] / 3, "startHandleY": P[0][1] + d0[1] * du[0] / 3,
                         "endHandleX": P[-1][0] - d1[0] * du[-1] / 3, "endHandleY": P[-1][1] - d1[1] * du[-1] / 3},
            "parameters": [b("geometryIsPeriodic", False), b(".hasHandlesInSketch", True)]
            + [b(".%d.hasInternalHandle" % i, i in (0, n - 1)) for i in range(n)]
            + [num("splinePointParamCount", "0.0"), num("splinePointCount", "%d.0" % n)]}


def circle_spline(eid, cx, cy, r, a0, a1, n):
    """Interpolated spline through n points of a circle, counter-clockwise a0 -> a1 (degrees), exact end tangents."""
    angs = [math.radians(a0 + (a1 - a0) * i / (n - 1)) for i in range(n)]
    pts = [(cx + r * math.cos(a), cy + r * math.sin(a)) for a in angs]
    return spline(eid, pts, (-math.sin(angs[0]), math.cos(angs[0])), (-math.sin(angs[-1]), math.cos(angs[-1])))


def edges(fid):
    return 'qConstructionFilter(qCreatedBy(makeId("%s"), EntityType.EDGE), ConstructionObject.NO)' % fid


def spec_of(tab, feature_type):
    t = [e for e in ELEMENTS if e["name"] == tab][0]
    ns = "e%s::m%s" % (t["id"], t["microversionId"])
    spec = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{t['id']}/featurespecs")["featureSpecs"]
            if x["featureType"] == feature_type][0]
    return ns, spec


def custom(tab, feature_type, name, given_list):
    """Every parameter from the spec's defaults (a partial feature fails its precondition), then ours."""
    ns, spec = spec_of(tab, feature_type)
    given = {p["parameterId"]: p for p in given_list}
    params = [given.get(p["parameterId"], dict(p["defaultValue"], parameterId=p["parameterId"]))
              for p in spec["parameters"] if isinstance(p.get("defaultValue"), dict)]
    return upsert({"btType": "BTMFeature-134", "featureType": feature_type, "name": name, "namespace": ns, "parameters": params})


# ---- fixtures ------------------------------------------------------------------------------------------------
f1 = sketch("F1 spline through 9 points of an R100 quarter circle", [circle_spline("s", 0, 0, 100, 0, 90, 9)])

a30 = math.radians(-60)
p_end = (200 * math.cos(a30), 500 + 200 * math.sin(a30))
t_end = (-math.sin(a30), math.cos(a30))
f2 = sketch("F2 line, spline on R200 (30 deg), line: one tangent chain at y 300", [
    seg("a", -100, 300, 0, 300),
    circle_spline("s", 0, 500, 200, -90, -60, 7),
    seg("b", p_end[0], p_end[1], p_end[0] + 100 * t_end[0], p_end[1] + 100 * t_end[1])])

f3 = sketch("F3 S-curve spline at y 600", [spline("s", [(0, 600), (50, 620), (100, 600), (150, 580), (200, 600)], (1, 0.4), (1, 0.4))])
f4 = sketch("F4 coarse spline: 3 points of R50 over 120 deg at y 800", [circle_spline("s", 0, 800, 50, -150, -30, 3)])

f5 = sketch("F5 splines A and B meeting at a 28 deg corner, chords along +X, y 1000", [
    spline("a", [(0, 1000), (100, 1010), (200, 1000)], (1, 0.2), (1, -0.2)),
    spline("b", [(200, 1000), (300, 1010), (400, 1000)], (1, 0.3), (1, -0.3))])
f6 = sketch("F6 splines A and C meeting tangent, y 1200", [
    spline("a", [(0, 1200), (100, 1210), (200, 1200)], (1, 0.2), (1, -0.2)),
    spline("c", [(200, 1200), (300, 1180), (400, 1190)], (1, -0.2), (1, 0.2))])

f7 = sketch("F7 line y 1400, x 0..300 (map from-chain)", [seg("l", 0, 1400, 300, 1400)])
# Near-circle spline host and the true arc: both R500, starting at (0, y) heading +X (centre above), 60 deg.
f8 = sketch("F8 spline on R500 at y 1600 (near-circle host)", [circle_spline("s", 0, 2100, 500, -90, -30, 9)])
f9 = sketch("F9 sketch arc R500 at y 1800", [arc("c", 0, 2300, 500, math.radians(-90), math.radians(-30))])

# ---- Recognize arcs ------------------------------------------------------------------------------------------
RA = ("recognize_arcs", "recognizeArcs")


def recognize(name, fid, tol_mm, replace, output):
    return custom(*RA, name, [q("sourceEdges", edges(fid)), num("tolerance", "%g mm" % tol_mm), b("replace", replace),
                              s("outputName", output)])


recognize("R1 F1, tol 0.01 -> 1 of 1, output 1 edge, arc R100", f1, 0.01, True, "R1")
recognize("R2 F2 chain, tol 0.01 -> 1 of 1, 1 wire: line, arc R200, line, joints <= 0.05 deg (the max tangent change)", f2, 0.01, True, "R2")
recognize("R3 F3 S-curve -> 0 of 1, output edge still a spline", f3, 0.01, True, "R3")
recognize("R4 F1 report only -> no output body", f1, 0.01, False, "R4")
recognize("R5 F4 coarse, tol 0.01 -> 0 of 1 (off the arc)", f4, 0.01, True, "R5")

# ---- Evaluate profiles, Efficient ------------------------------------------------------------------------------
EP = ("evaluate_profiles", "evaluateProfiles")
ns_ep, _ = spec_of(*EP)


def efficient(name, fid, output):
    return custom(*EP, name, [en("profileSource", "ProfileSource", "EDGES", ns_ep), s("outputName", output),
                              q("profileEdges", edges(fid)), q("projectionFace", TOP),
                              en("grouping", "ProfileGrouping", "EFFICIENT", ns_ep)])


efficient("P12 F5 Efficient, 28 deg corner, chords agree -> 2 edges (corner kept)", f5, "P12")
efficient("P13 F6 Efficient, tangent joint -> 1 edge (merged)", f6, "P13")

# ---- Map curve ---------------------------------------------------------------------------------------------------
MC = ("map_curve", "mapCurve")
ns_mc, _ = spec_of(*MC)
zero = 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 1.4, 0) * meter)' % f7


def mapped(name, host, output):
    return custom(*MC, name, [en("mapMode", "MapMode", "FROM_EDGES", ns_mc), q("fromEdges", edges(f7)), q("toEdges", edges(host)),
                              en("refPointMode", "RefPointMode", "SHARED", ns_mc), q("offsetRefPoint", zero), s("outputName", output)])


mapped("M1 F7 onto F8 (near-circle spline host) -> 1 edge, NOT an arc", f8, "M1")
mapped("M2 F7 onto F9 (true arc host) -> 1 edge, arc R500", f9, "M2")
print("studio", E)
