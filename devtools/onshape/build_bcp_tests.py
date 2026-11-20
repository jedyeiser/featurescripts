"""Build the Bridging curve + tests as real features in the "BCP tests" Part Studio of the multi_mate_connector document
(created when missing; every run deletes its features and rebuilds them). Check with check_bcp_tests.py.

Fixture (Top, millimetres). Curves are sketch lines, an arc, a 3-edge wire and a fit spline (a B-spline):
  A    line (0,0) -> (100,0)            heading +X; its end (100,0) is nearest B's start
  B    line (200,50) -> (300,50)        B1: drawn this way; its near end is (200,50), outward direction -X
  Brev line (300,100) -> (200,100)      the same kind of curve drawn the other way (a different row, y = 100)
  ARC  quarter circle centre (200,200) r 50 from 270 to 360 deg: from (200,150) to (250,200), end heading +Y
  W3   three-edge wire (400,0) -> (450,0) -> (450,50) -> (500,50)
  SP   fit spline through (400,200) (450,230) (500,200) (550,230)     (a B-spline edge)
  MC   a Mate connector + at (100,300) with Z = world X (a mate connector whose Z points +X)

B1  A -> B, tangent both         -> ends (100,0) (200,50); starts heading +X, arrives heading +X (an S that blends)
B2  A -> Brev, tangent both      -> the same, whichever way Brev was drawn: ends (100,0) (200,100)
B3  A -> B, position only        -> a straight line (100,0) -> (200,50)
B4  A -> ARC, tangent both       -> ends (100,0)... nearest of ARC's ends: (200,150); arrives heading along the arc's outward direction
B5  A -> B, curvature both       -> degree 5, same end points as B1, starts and arrives heading +X
B6  MC -> B, tangent both        -> starts at the connector (100,300), heading +X (its Z), ends at (200,50) arriving heading +X
B7  A -> W3 (3-edge wire)        -> nearest end of the wire is (400,0): ends (100,0) (400,0)
B8  A -> SP (B-spline)           -> nearest end of the spline is (400,200): ends (100,0) (400,200)
B9  vertex start, tangent        -> ERROR (a vertex has no tangent)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_bcp_tests.py
"""
import copy
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("multi_mate_connector/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "BCP tests"

els = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in els if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TABS = {e["name"]: e for e in els if e["elementType"] == "FEATURESTUDIO"}
NS_B = "e%s::m%s" % (TABS["bridging_curve_plus"]["id"], TABS["bridging_curve_plus"]["microversionId"])
NS_M = "e%s::m%s" % (TABS["mate_connector_plus"]["id"], TABS["mate_connector_plus"]["microversionId"])
SPECS = {x.get("featureType"): x for x in c.get(f"{BASE}/featurespecs")["featureSpecs"]}


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


for f in reversed(c.get(f"{BASE}/features")["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")
tree = c.get(f"{BASE}/features")


def post(feature):
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": tree["serializationVersion"], "sourceMicroversion": tree["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    tree["sourceMicroversion"] = r.get("sourceMicroversion", tree["sourceMicroversion"])
    print("%-92s %s" % (feature["name"][:92], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def arc(eid, cx, cy, r, a0, a1):
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def point(eid, x, y):
    return {"btType": "BTMSketchPoint-158", "entityId": eid, "isConstruction": False, "x": x, "y": y}


def mm(*xs):
    return [x / 1000.0 for x in xs]


def sketch(name, entities):
    return post({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                 "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Top"), EntityType.FACE)')], "entities": entities, "constraints": []})


def edges_of(sk):
    return 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % sk


A = sketch("A line (0,0)-(100,0)", [seg("a", *mm(0, 0, 100, 0))])
B = sketch("B line (200,50)-(300,50)", [seg("b", *mm(200, 50, 300, 50))])
BREV = sketch("Brev line (300,100)-(200,100), drawn the other way", [seg("r", *mm(300, 100, 200, 100))])
ARC = sketch("ARC centre (200,200) r 50, 270 to 360 deg", [arc("c", *mm(200, 200, 50), 1.5 * math.pi, 2 * math.pi)])
W3 = sketch("W3 wire (400,0)-(450,0)-(450,50)-(500,50)", [seg("w1", *mm(400, 0, 450, 0)), seg("w2", *mm(450, 0, 450, 50)), seg("w3", *mm(450, 50, 500, 50))])
SPP = sketch("SP points (400,200) (450,230) (500,200) (550,230)", [point("p%d" % i, *mm(x, y)) for i, (x, y) in enumerate([(400, 200), (450, 230), (500, 200), (550, 230)])])
VTX = sketch("V point (50,300)", [point("v", *mm(50, 300))])
MCPT = sketch("MC point (100,300)", [point("m", *mm(100, 300))])

# the fit spline, from the spec defaults (every parameter must be present)
spec = SPECS["fitSpline"]
fit = []
for p in spec["parameters"]:
    pid = p["parameterId"]
    if pid == "vertices":
        fit.append(q("vertices", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % SPP))
    elif isinstance(p.get("defaultValue"), dict):
        fit.append(dict(p["defaultValue"], parameterId=pid))
SP = post({"btType": "BTMFeature-134", "featureType": "fitSpline", "name": "SP fit spline (a B-spline)", "parameters": fit})

# a mate connector with Z = world X, made by Mate connector +
MC = post({"btType": "BTMFeature-134", "featureType": "mateConnectorPlus", "name": "MC Mate connector + at (100,300), Z = +X", "namespace": NS_M, "parameters": [
    en("mode", "McpMode", "NEW", NS_M), en("originType", "McpOriginType", "POINT", NS_M),
    q("originPoint", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % MCPT),
    en("zType", "McpAxisType", "WORLD", NS_M), en("zWorld", "McpWorldAxis", "X_AXIS", NS_M), b("zFlip", False),
    en("xType", "McpXAxisType", "AUTOMATIC", NS_M), b("xFlip", False),
    num("modOffsetX", "0 mm"), num("modOffsetY", "0 mm"), num("modOffsetZ", "0 mm"), num("modRotX", "0 deg"), num("modRotY", "0 deg"), num("modRotZ", "0 deg")]})


ATT1 = sketch("ATT1 point (-50,0)", [point("t", *mm(-50, 0))])
ATT2 = sketch("ATT2 point (350,50)", [point("t", *mm(350, 50))])
ATT3 = sketch("ATT3 point (550,50)", [point("t", *mm(550, 50))])


def verts(sk):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % sk


def bcp(name, side1, side2, m1="TANGENCY", m2="TANGENCY", choose="NEAREST_PAIR", attractor=None, edit=None, flip1=False):
    """choose: STANDARD / NEAREST_PAIR / ATTRACTOR. edit: None, or {"side1Magnitude": 2, ...} (the Edit control points group)."""
    p = [en("endChoice", "BcpEndChoice", choose, NS_B)]
    if choose == "ATTRACTOR" and attractor is not None:
        p.append(q("attractor", attractor))
    p += [q("side1", side1), en("match1", "BridgingCurveMatchType", m1, NS_B)]
    if m1 != "POSITION":
        p.append(b("flip1", flip1))
    p += [q("side2", side2), en("match2", "BridgingCurveMatchType", m2, NS_B)]
    if m2 != "POSITION":
        p.append(b("flip2", False))
    if m1 != "POSITION" or m2 != "POSITION":
        p.append(en("method", "BridgingCurveMethod", "CONTROL_POINTS", NS_B))
        p.append(b("editControlPoints", bool(edit)))
        if edit:
            if m1 != "POSITION":
                p.append(num("side1Magnitude", str(edit.get("side1Magnitude", 1))))
                if m1 == "CURVATURE":
                    p.append(num("side1CurvatureOffset", str(edit.get("side1CurvatureOffset", 1))))
            if m2 != "POSITION":
                p.append(num("side2Magnitude", str(edit.get("side2Magnitude", 1))))
                if m2 == "CURVATURE":
                    p.append(num("side2CurvatureOffset", str(edit.get("side2CurvatureOffset", 1))))
    return post({"btType": "BTMFeature-134", "featureType": "bridgingCurvePlus", "name": name, "namespace": NS_B, "parameters": p})

bcp("B1 A to B, tangent both -> S curve (100,0)-(200,50), heading +X at both ends", edges_of(A), edges_of(B))
bcp("B2 A to Brev (drawn the other way), tangent both -> (100,0)-(200,100)", edges_of(A), edges_of(BREV))
bcp("B3 A to B, position only -> a straight line (100,0)-(200,50)", edges_of(A), edges_of(B), "POSITION", "POSITION")
bcp("B4 A to ARC, tangent both -> nearest ARC end (200,150)", edges_of(A), edges_of(ARC))
bcp("B5 A to B, curvature both -> degree 5, same ends as B1", edges_of(A), edges_of(B), "CURVATURE", "CURVATURE")
bcp("B6 mate connector to B, tangent both -> from (100,300) heading +X", 'qCreatedBy(makeId("%s"), EntityType.BODY)' % MC, edges_of(B))
bcp("B7 A to W3 (3-edge wire), tangent both -> nearest end (400,0)", edges_of(A), edges_of(W3))
bcp("B8 A to SP (B-spline), tangent both -> nearest end (400,200)", edges_of(A), edges_of(SP))
bcp("B9 vertex to B, tangent -> ERROR (a lone point has no edge to take a tangent from)", verts(VTX), edges_of(B))
bcp("B10 A to B, tangent both, Edit control points: magnitudes 2 and 0.5 -> same ends and headings as B1, a different shape", edges_of(A), edges_of(B), edit={"side1Magnitude": 2, "side2Magnitude": 0.5})
bcp("B11 A to B, start tangent reversed -> starts heading -X (a deliberate loop)", edges_of(A), edges_of(B), flip1=True)
bcp("B12 A to B, curvature both, curvature offsets 0.5 -> degree 5, same ends", edges_of(A), edges_of(B), "CURVATURE", "CURVATURE", edit={"side1CurvatureOffset": 0.5, "side2CurvatureOffset": 0.5})
# attractor mode: each open side joins at the end nearest the attractor
bcp("B13 attractor (350,50), A to B -> ends (100,0) (300,50); B's end (300,50) leaves +X so the bridge arrives heading -X", edges_of(A), edges_of(B), choose="ATTRACTOR", attractor=verts(ATT2))
bcp("B14 attractor (-50,0), A to B -> ends (0,0) (200,50); starts heading -X (out of A's start), arrives heading +X", edges_of(A), edges_of(B), choose="ATTRACTOR", attractor=verts(ATT1))
bcp("B15 attractor (550,50), A to W3 (3-edge wire) -> ends (100,0) (500,50); arrives heading -X", edges_of(A), edges_of(W3), choose="ATTRACTOR", attractor=verts(ATT3))
bcp("B16 standard mode, vertex (100,0) to vertex (200,50), position -> a straight line", 'qContainsPoint(%s, vector(0.1, 0, 0) * meter)' % verts(A), 'qContainsPoint(%s, vector(0.2, 0.05, 0) * meter)' % verts(B), "POSITION", "POSITION", choose="STANDARD")
bcp("B17 attractor mode with no attractor picked -> ERROR", edges_of(A), edges_of(B), choose="ATTRACTOR")
