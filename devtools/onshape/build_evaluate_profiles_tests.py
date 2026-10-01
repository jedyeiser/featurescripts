"""Evaluate profiles on SURFACES: test cases as real features in Curve_tools' "Evaluate profiles tests" Part
Studio (upsert by name). Checked by check_evaluate_profiles_tests.py.

  P1 crowned sheet (arc across y +-50, 3 mm high, 1000 long in X), picked by its FACE, onto Front, All profiles
     -> top at z 3 (the crown line), bottom at z 0 (the edges), middle at z 1.5, each 1000 long
  P2 the same sheet picked as a BODY, Periphery -> one closed loop, 2 x (1000 + 3) = 2006 long
  P3 a flat sheet (z 0) onto Front: edge-on -> ERROR with a message, not the kernel's REGEN_ERROR
  P4 the flat sheet onto Top: Periphery -> the 1000 x 100 rectangle, 2200 long

usage (repo root): PYTHONPATH=. python devtools/onshape/build_evaluate_profiles_tests.py
"""
import json
import math

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature  # noqa: E402  (API budget: one feature-tree GET per studio)

c = OnshapeClient()
DOC = json.load(open("curve_tools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Evaluate profiles tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "evaluate_profiles"][0]
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


_TREE = {}   # feature tree of the test Part Studio, kept in step with each write (API budget)


def upsert(feature):
    r = write_feature(BASE, feature, _TREE, client=c)
    print("%-100s %s" % (feature["name"][:100], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]

def sketch(name, plane, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % plane)], "entities": entities, "constraints": []})


def wire_edges(fid):
    return 'qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE)' % fid


def surface(name, sketch_id, depth, symmetric):
    return upsert({"btType": "BTMFeature-134", "featureType": "extrude", "name": name, "parameters": [
        en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
        q("surfaceEntities", wire_edges(sketch_id)), en("endBound", "BoundingType", "BLIND"), num("depth", "%g mm" % depth), b("symmetric", symmetric)]})


SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"] if x["featureType"] == "evaluateProfiles"][0]


def profiles(name, part, onto, parts, output):
    """Every parameter from the spec's defaults (a partial feature fails its precondition), then ours."""
    given = {p["parameterId"]: p for p in [
        en("profileSource", "ProfileSource", "PART", NS), s("outputName", output), q("profilePart", part),
        en("profileParts", "ProfilePart", parts, NS), q("projectionFace", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % onto),
        en("grouping", "ProfileGrouping", "SINGLE", NS)]}
    params = [given.get(p["parameterId"], dict(p["defaultValue"], parameterId=p["parameterId"]))
              for p in SPEC["parameters"] if isinstance(p.get("defaultValue"), dict)]
    return upsert({"btType": "BTMFeature-134", "featureType": "evaluateProfiles", "name": name, "namespace": NS, "parameters": params})


# crown: circular arc through (-50, 0), (0, 3), (50, 0) on Right (sketch x = world Y, sketch y = world Z)
R = (50.0 ** 2 + 3.0 ** 2) / (2 * 3.0)
cy = 3.0 - R
a0 = math.atan2(-cy, 50.0)
a1 = math.atan2(-cy, -50.0)
arc = {"btType": "BTMSketchCurveSegment-155", "entityId": "a", "startPointId": "a.start", "endPointId": "a.end",
       "startParam": a0, "endParam": a1, "isConstruction": False,
       "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": R / 1000, "xCenter": 0.0, "yCenter": cy / 1000,
                    "xDir": 1.0, "yDir": 0.0, "clockwise": False}}
crown_sketch = sketch("Crown: arc across y -50..50, 3 mm high, on Right", "Right", [arc])
crowned = surface("Crowned sheet, 1000 long in X", crown_sketch, 1000, False)
face = 'qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.FACE)' % crowned
body = 'qCreatedBy(makeId("%s"), EntityType.BODY)' % crowned
profiles("P1 crowned sheet picked by its face, onto Front, All -> top z 3, bottom z 0, middle z 1.5, 1000 long", face, "Front", "ALL", "crown")
profiles("P2 crowned sheet picked as a body, onto Front, Periphery -> 1 closed loop, 2006 long", body, "Front", "PERIPHERY", "crown")

line = {"btType": "BTMSketchCurveSegment-155", "entityId": "l", "startPointId": "l.start", "endPointId": "l.end",
        "startParam": 0.0, "endParam": 0.1, "isConstruction": False,
        "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": -0.05, "pntY": -0.2, "dirX": 1.0, "dirY": 0.0}}
flat_sketch = sketch("Flat: line across y -50..50 at z -200, on Right", "Right", [line])
flat = surface("Flat sheet z -200, 1000 long in X", flat_sketch, 1000, False)
flat_body = 'qCreatedBy(makeId("%s"), EntityType.BODY)' % flat
profiles("P3 flat sheet onto Front: edge-on -> ERROR with a message (no outline area)", flat_body, "Front", "PERIPHERY", "flat")
profiles("P4 flat sheet onto Top, Periphery -> the 1000 x 100 rectangle, 2200 long", flat_body, "Top", "PERIPHERY", "flat")
print("studio", E)


# ---- short-edge merging (the RD 20FOU SW_Fill case): a part whose outline carries a 1.7 um step ----
def profiles_with(name, part, onto, parts, output, extra):
    given = {p["parameterId"]: p for p in [
        en("profileSource", "ProfileSource", "PART", NS), s("outputName", output), q("profilePart", part),
        en("profileParts", "ProfilePart", parts, NS), q("projectionFace", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % onto),
        en("grouping", "ProfileGrouping", "SINGLE", NS)] + extra}
    params = [given.get(p["parameterId"], dict(p["defaultValue"], parameterId=p["parameterId"]))
              for p in SPEC["parameters"] if isinstance(p.get("defaultValue"), dict)]
    return upsert({"btType": "BTMFeature-134", "featureType": "evaluateProfiles", "name": name, "namespace": NS, "parameters": params})


def fill(name, wire_feature):
    spec = [x for x in c.get(f"{BASE}/featurespecs")["featureSpecs"] if x["featureType"] == "fill"][0]
    item_defaults = [p["defaultValue"] for p in [x for x in spec["parameters"] if x["parameterId"] == "edges"][0]["parameters"]]
    given = {"entities": q("entities", 'qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE)' % wire_feature),
             "continuity": en("continuity", "GeometricContinuity", "G0")}
    item = {"btType": "BTMArrayParameterItem-1843", "parameters": [dict(given.get(d["parameterId"], d)) for d in item_defaults]}
    return upsert({"btType": "BTMFeature-134", "featureType": "fill", "name": name, "parameters": [
        en("surfaceOperationType", "NewSurfaceOperationType", "NEW"), {"btType": "BTMParameterArray-2025", "parameterId": "edges", "items": [item]}]})


STEP = 0.0017   # mm
pts = [(0, 400), (100, 400), (100, 420), (60, 420), (60, 420 + STEP), (0, 420 + STEP)]
segs = []
for i in range(len(pts)):
    x0, z0 = pts[i]
    x1, z1 = pts[(i + 1) % len(pts)]
    L = math.hypot(x1 - x0, z1 - z0)
    segs.append({"btType": "BTMSketchCurveSegment-155", "entityId": "s%d" % i, "startPointId": "s%d.start" % i, "endPointId": "s%d.end" % i,
                 "startParam": 0.0, "endParam": L / 1000, "isConstruction": False,
                 "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0 / 1000, "pntY": z0 / 1000, "dirX": (x1 - x0) / L, "dirY": (z1 - z0) / L}})
step_sketch = sketch("Step: 100 x 20 outline on Front with a 0.0017 mm step at x 60", "Front", segs)
step_part = upsert({"btType": "BTMFeature-134", "featureType": "extrude", "name": "Step part (extruded 50 symmetric)", "parameters": [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
    q("entities", 'qSketchRegion(makeId("%s"))' % step_sketch), en("endBound", "BoundingType", "BLIND"), num("depth", "50 mm"), b("symmetric", True)]})
part_q = 'qCreatedBy(makeId("%s"), EntityType.BODY)' % step_part
p5 = profiles_with("P5 step part, Periphery, merge < 0.01 mm (default) -> 5 edges, the step merged, notice", part_q, "Front", "PERIPHERY", "step", [])
fill("P6 Fill on P5's periphery -> OK, area 2000.0935", p5)
p7 = profiles_with("P7 step part, Periphery, merge 0 -> 6 edges, the 0.0017 mm step kept", part_q, "Front", "PERIPHERY", "step raw",
                   [num("mergeShorter", "0 mm")])
fill("P8 Fill on P7's periphery -> ERROR (the kernel refuses the 0.0017 mm edge)", p7)


# ---- Edges mode Output (2026-09-25): One curve / One per input curve / Efficient -------------------------------
# A chain on Top at y 600..: two collinear lines (0 > 200 > 400), a slanted line to (700, 700), then an R100 arc
# tangent to it that turns back past +X. Projected onto Top it is itself; the scan cuts the arc where it turns back.
#   P9  One curve              -> 1 edge (a fit)
#   P10 One per input curve    -> 4 edges: line, line, line, arc R100 (exact, the arc cut at the reversal)
#   P11 Efficient              -> 3 edges: the collinear pair merged into one 400 mm line, the slanted line, the arc R100
def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L / 1000, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0 / 1000, "pntY": y0 / 1000, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


th = math.atan2(100, 300)
cx, cy_ = 700 - 100 * math.sin(th), 700 + 100 * math.cos(th)
hook = {"btType": "BTMSketchCurveSegment-155", "entityId": "h", "startPointId": "h.start", "endPointId": "h.end",
        "startParam": th - math.pi / 2, "endParam": th - math.pi / 2 + math.radians(200), "isConstruction": False,
        "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": 0.1, "xCenter": cx / 1000, "yCenter": cy_ / 1000,
                     "xDir": 1.0, "yDir": 0.0, "clockwise": False}}
chain = sketch("Chain on Top: lines 0>200>400 (collinear), line to (700, 700), R100 hook turning back", "Top",
               [seg("a", 0, 600, 200, 600), seg("b", 200, 600, 400, 600), seg("c", 400, 600, 700, 700), hook])


def profiles_edges(name, grouping, output):
    given = {p["parameterId"]: p for p in [
        en("profileSource", "ProfileSource", "EDGES", NS), s("outputName", output),
        q("profileEdges", 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % chain),
        q("projectionFace", 'qCreatedBy(makeId("Top"), EntityType.FACE)'), en("grouping", "ProfileGrouping", grouping, NS)]}
    params = [given.get(p["parameterId"], dict(p["defaultValue"], parameterId=p["parameterId"]))
              for p in SPEC["parameters"] if isinstance(p.get("defaultValue"), dict)]
    return upsert({"btType": "BTMFeature-134", "featureType": "evaluateProfiles", "name": name, "namespace": NS, "parameters": params})


profiles_edges("P9 chain, One curve -> 1 edge", "SINGLE", "chain single")
profiles_edges("P10 chain, One per input curve -> 4 edges, exact lines + arc R100 cut at the reversal", "PER_CURVE", "chain per curve")
profiles_edges("P11 chain, Efficient -> 3 edges: collinear lines merged (400 long), line, arc R100", "EFFICIENT", "chain efficient")
