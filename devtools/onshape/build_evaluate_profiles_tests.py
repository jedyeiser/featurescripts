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
