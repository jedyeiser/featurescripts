"""Build the Multi mate connector tests as real features in the "MMC tests" Part Studio of the multi_mate_connector document
(created when missing; every run deletes its features and rebuilds them). Check with check_mmc_tests.py.

Fixture (Top, millimetres, walls stand up in Z):
  W1  surface wall along the line (0,0) -> (200,0), 100 high
  W2  surface wall along the line (400,0) -> (200,0), 100 high   (drawn the other way, so its normal is the OPPOSITE of W1's)
  CURVE  line (10,10) -> (390,10) on Top: 10 mm in front of both walls, running past the seam at x = 200
  REF  a point at (200,500)

M1  5 connectors, both ends, consistent normals       -> x 10 105 200 295 390; all Z parallel to Y; consecutive Z dot > 0
M2  same, exactly as the faces                        -> the fixture's normals really flip: some consecutive Z dot < 0
M3  4 connectors, centered, consistent                -> x 57.5 152.5 247.5 342.5
M4  5 connectors, toward a reference point            -> every Z has +Y component (the point is at y = 500)
M5  5 connectors, consistent, Flip on                 -> every Z is the opposite of M1's
M6  5 connectors, fixed direction (Top plane normal)  -> every Z = (0, 0, 1)
M7  5 connectors, consistent, reverse curve           -> first connector at x = 390

usage (repo root): PYTHONPATH=. python devtools/onshape/build_mmc_tests.py
"""
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("multi_mate_connector/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "MMC tests"

els = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in els if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in els if e["elementType"] == "FEATURESTUDIO" and e["name"] == "multi_mate_connector"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


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
    print("%-88s %s" % (feature["name"][:88], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def point(eid, x, y):
    return {"btType": "BTMSketchPoint-158", "entityId": eid, "isConstruction": False, "x": x, "y": y}


def sketch(name, entities):
    return post({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                 "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Top"), EntityType.FACE)')], "entities": entities, "constraints": []})


def mm(*xs):
    return [x / 1000.0 for x in xs]


def wall(name, x0, y0, x1, y1):
    sk = sketch(name + " (sketch)", [seg("l", *mm(x0, y0, x1, y1))])
    return post({"btType": "BTMFeature-134", "featureType": "extrude", "name": name, "parameters": [
        en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("operationType", "NewBodyOperationType", "NEW"),
        q("surfaceEntities", 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % sk), en("endBound", "BoundingType", "BLIND"), num("depth", "100 mm")]})


W1 = wall("W1 wall (0,0)-(200,0)", 0, 0, 200, 0)
W2 = wall("W2 wall (400,0)-(200,0), opposite normal", 400, 0, 200, 0)
CURVE_SK = sketch("CURVE line (10,10)-(390,10)", [seg("c", *mm(10, 10, 390, 10))])
REF_SK = sketch("REF point (200,500)", [point("p", *mm(200, 500))])

CURVE = 'qCreatedBy(makeId("%s"), EntityType.EDGE)' % CURVE_SK
WALLS = 'qUnion([qCreatedBy(makeId("%s"), EntityType.FACE), qCreatedBy(makeId("%s"), EntityType.FACE)])' % (W1, W2)
REF = 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % REF_SK
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


def mmc(name, count=5, spacing="INCLUDE_ENDS", correction="CONSISTENT", source="SURFACE", reverse=False, flip=False):
    params = [q("curves", CURVE), num("count", str(count), True), en("spacingMode", "MmcSpacing", spacing, NS), b("reverseCurve", reverse),
              en("normalSource", "MmcNormalSource", source, NS)]
    if source == "SURFACE":
        params += [q("normalFaces", WALLS), en("normalCorrection", "MmcNormalCorrection", correction, NS)]
        if correction == "TOWARD_POINT":
            params.append(q("referencePoint", REF))
    else:
        params.append(q("zDirection", TOP))
    params.append(b("flipNormals", flip))
    return post({"btType": "BTMFeature-134", "featureType": "multiMateConnector", "name": name, "namespace": NS, "parameters": params})


mmc("M1 5 both ends, consistent -> x 10 105 200 295 390, Z parallel to Y, no sign jumps")
mmc("M2 5 both ends, exactly as the faces -> the normals flip at the seam", correction="AS_FACES")
mmc("M3 4 centered, consistent -> x 57.5 152.5 247.5 342.5", count=4, spacing="CENTERED")
mmc("M4 5 toward a reference point at y = 500 -> every Z has +Y", correction="TOWARD_POINT")
mmc("M5 5 consistent, Flip -> every Z opposite to M1", flip=True)
mmc("M6 5 fixed direction (Top normal) -> every Z = (0,0,1)", source="DIRECTION")
mmc("M7 5 consistent, reverse curve -> first connector at x = 390", reverse=True)
