"""Build the Trim curve + test cases as real features in smallTools' "Trim curve + tests" Part Studio
(created when missing; upsert by name). Fixtures are native features only. Checked by
check_trim_curve_plus_tests.py.

Every case has its own copy of a 3-edge wire (composite curve), offset 1000 mm along X per case:
line (x0, 0)->(x0+200, 0), R100 quarter arc to (x0+300, 100), line up to (x0+300, 300). Length 557.080.
Path arc length s: 0..200 line 1, 200..357.080 arc, 357.080..557.080 line 3.

  T1  split, at points (x0+100, -6) [6 mm off the curve] and (x0+300, 200)  -> 3 wires 100 / 357.080 / 100
  T2  split in place (single wire), same points                             -> 1 wire, 5 edges, 557.080
  T3  trim at point (x0+100, 0)                                             -> 1 wire 457.080
  T4  split, 150 from (x0+100, 0), also at the reference                    -> 3 wires 100 / 150 / 307.080
  T5  split, -50 from (x0+100, 0)                                           -> 2 wires 50 / 507.080
  T6  split, 50 from (x0+100, 0), opposite direction                        -> 2 wires 50 / 507.080
  T7  split at a mate connector at (x0+100, 0)                              -> 2 wires 100 / 457.080
  T8  split, 600 from (x0+100, 0)                                           -> ERROR (runs past the end)
  T9  split, 50 + additional 150 and -80 from (x0+100, 0)                   -> 4 wires 20 / 130 / 100 / 307.080

usage (repo root): PYTHONPATH=. python devtools/onshape/build_trim_curve_plus_tests.py
"""
import copy
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("smallTools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Trim curve + tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "betterCurveTrim"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "betterCurveTrim"][0]

STATE = {"features": None}


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


def mmv(*xs):
    return [x / 1000.0 for x in xs]


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


def sketch_edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def vertices(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % fid


def wire(tag, x0):
    sk = sketch("%s wire sketch: line, R100 arc, line (x0 %d)" % (tag, x0),
                [seg("l1", *mmv(x0, 0, x0 + 200, 0)), arc("a", *mmv(x0 + 200, 100, 100), -math.pi / 2, 0),
                 seg("l2", *mmv(x0 + 300, 100, x0 + 300, 300))])
    return feature("%s 3-edge wire (composite curve)" % tag, "compositeCurve", [q("edges", sketch_edges(sk))])


def points(tag, pts):
    return sketch("%s points %s" % (tag, " ".join("(%g, %g)" % p for p in pts)),
                  [point("p%d" % i, *mmv(*p)) for i, p in enumerate(pts)])


def connector(name, x, y):
    sk = sketch(name + " (sketch)", [point("p", *mmv(x, y))])
    return feature(name, "mateConnector", [
        en("originType", "OriginCreationType", "ON_ENTITY"), q("originQuery", vertices(sk)),
        en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])


def extra_distances(pid, *mm):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid, "items": [
        {"btType": "BTMArrayParameterItem-1843", "parameters": [num("extraDistance", "%g mm" % d)]} for d in mm]}


def trim(name, curves, given=(), enums=None):
    """An instance of Trim curve +; parameters not given take the spec defaults (correction 38)."""
    given = {p["parameterId"]: p for p in [q("curves", curves)] + list(given)}
    enums = enums or {}
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in ("inflectionIndices", "extraDistances"):
            items = given[pid]["items"] if pid in given else []
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid, "items": items})
        elif pid in given:
            params.append(given[pid])
        elif pid in enums:
            e = copy.deepcopy(d)
            e["parameterId"] = pid
            e["value"] = enums[pid]
            params.append(e)
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return feature(name, "betterCurveTrim", params, NS)


SPLIT = {"operation": "SPLIT"}

x0 = 0
w = wire("T1", x0)
p = points("T1", [(x0 + 100, -6), (x0 + 300, 200)])
trim("T1 split at 2 points (one 6 mm off) -> 3 wires 100 / 357.080 / 100", body(w), [q("atPoints", vertices(p))],
     dict(SPLIT, cutBy="AT_POINTS"))

x0 = 1000
w = wire("T2", x0)
p = points("T2", [(x0 + 100, -6), (x0 + 300, 200)])
trim("T2 split in place at 2 points -> 1 wire, 5 edges, 557.080", body(w), [q("atPoints", vertices(p)), b("returnSingleWire", True)],
     dict(SPLIT, cutBy="AT_POINTS"))

x0 = 2000
w = wire("T3", x0)
p = points("T3", [(x0 + 100, 0)])
trim("T3 trim at point -> 1 wire 457.080", body(w), [q("atPoints", vertices(p))], {"operation": "TRIM", "cutBy": "AT_POINTS"})

x0 = 3000
w = wire("T4", x0)
p = points("T4", [(x0 + 100, 0)])
trim("T4 split 150 from point, also at reference -> 3 wires 100 / 150 / 307.080", body(w),
     [q("fromPoint", vertices(p)), num("distance", "150 mm"), b("splitAtReference", True)], dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 4000
w = wire("T5", x0)
p = points("T5", [(x0 + 100, 0)])
trim("T5 split -50 from point -> 2 wires 50 / 507.080", body(w), [q("fromPoint", vertices(p)), num("distance", "-50 mm")],
     dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 5000
w = wire("T6", x0)
p = points("T6", [(x0 + 100, 0)])
trim("T6 split 50 from point, opposite direction -> 2 wires 50 / 507.080", body(w),
     [q("fromPoint", vertices(p)), num("distance", "50 mm"), b("distanceFlip", True)], dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 6000
w = wire("T7", x0)
mc = connector("T7 mate connector at (%d, 0)" % (x0 + 100), x0 + 100, 0)
trim("T7 split at a mate connector -> 2 wires 100 / 457.080", body(w), [q("atPoints", body(mc))], dict(SPLIT, cutBy="AT_POINTS"))

x0 = 8000
w = wire("T9", x0)
p = points("T9", [(x0 + 100, 0)])
trim("T9 split 50, extra 150 and -80 from point -> 4 wires 20 / 130 / 100 / 307.080", body(w),
     [q("fromPoint", vertices(p)), num("distance", "50 mm"), extra_distances("extraDistances", 150, -80)],
     dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))

x0 = 7000
w = wire("T8", x0)
p = points("T8", [(x0 + 100, 0)])
trim("T8 split 600 from point -> ERROR runs past the end", body(w), [q("fromPoint", vertices(p)), num("distance", "600 mm")],
     dict(SPLIT, cutBy="ARC_LENGTH_FROM_POINT"))
