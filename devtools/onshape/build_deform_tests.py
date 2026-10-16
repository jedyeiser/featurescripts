"""Build the Deform test cases as real features in the curveMapping document's "Deform tests" Part Studio
(created when missing; upsert by name). Native fixtures only; checked by check_deform_tests.py.

Fixtures (mm, on Top): from path = line (0,0)->(300,0); to path = quarter arc R300 about (0,300), (0,0)->(300,300).
Both references sit at the start vertices, so s_to = s_from and a point (x, y, z) lands at
arc(x) + y*N + z*B with N pointing at the arc centre. Source boxes: A x 100..200, B x 220..260, y -10..10, z 0..10.
Mate connectors: MC_A on box A's corner (100, -10, 10), owned by A; MC_B stand-alone at (150, 0, 0).

  D1  Bodies A+B, connectors MC_A+MC_B -> 2 result solids; MC_A owned by A's result, MC_B stand-alone
  D2  connectors only (no body)         -> both connectors stand-alone
  D3  Body A only                       -> 1 result solid (regression: single-body path)
  D4  Bodies A+B, no connectors         -> 2 result solids, no connector

usage (repo root): PYTHONPATH=. python devtools/onshape/build_deform_tests.py
"""
import json
import math
import os

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, cached_tree  # API budget: one feature-tree GET per studio

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"   # curveMapping (featurescriptSettings.json)
STUDIO = "Deform tests"
ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "deform"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "deform"][0]

_TREE = {}


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
    if os.environ.get("BUILD_ONLY_NEW"):
        have = [x for x in cached_tree(BASE, _TREE, c)["features"] if x["name"] == feature["name"]]
        if have:
            return have[0]["featureId"]
    r = write_feature(BASE, feature, _TREE, client=c)
    print("%-90s %s" % (feature["name"][:90], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


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


def rect(tag, x0, x1, y0, y1):
    pts = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    return [seg("%s%d" % (tag, i), *mmv(*pts[i], *pts[(i + 1) % 4])) for i in range(4)]


TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


def sk_edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def box(name, x0, x1):
    sk = sketch(name + " (sketch)", rect("r", x0, x1, -10, 10))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"), num("depth", "10 mm")])


# Start from an empty studio: upsert-by-name appends renamed features at the END of the tree, behind the cases that need them.
for f in reversed(cached_tree(BASE, _TREE, c)["features"]):
    delete_feature(BASE, f["featureId"], _TREE, client=c)

# ---- fixtures ----
from_sk = sketch("From path: line (0,0)-(300,0)", [seg("l", *mmv(0, 0, 300, 0))])
to_sk = sketch("To path: R300 quarter arc about (0,300)", [arc("a", *mmv(0, 300, 300), -math.pi / 2, 0)])
box_a = box("Box A x100..200", 100, 200)
box_b = box("Box B x220..260", 220, 260)

pt_sk = sketch("MC_B point (150, 0)", [{"btType": "BTMSketchPoint-158", "entityId": "p", "isConstruction": False, "x": 0.150, "y": 0.0}])

mc_a = feature("MC_A box A corner (100,-10,10), owned by A", "mateConnector", [
    en("originType", "OriginCreationType", "ON_ENTITY"),
    q("originQuery", 'qClosestTo(qOwnedByBody(%s, EntityType.VERTEX), vector(100, -10, 10) * millimeter)' % body(box_a)),
    en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", True),
    q("ownerPart", body(box_a))])
mc_b = feature("MC_B stand-alone at (150,0,0)", "mateConnector", [
    en("originType", "OriginCreationType", "ON_ENTITY"),
    q("originQuery", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % pt_sk),
    en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])

FROM_EDGES = sk_edges(from_sk)
TO_EDGES = sk_edges(to_sk)
FROM_REF = 'qClosestTo(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 0, 0) * millimeter)' % from_sk
TO_REF = 'qClosestTo(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(0, 0, 0) * millimeter)' % to_sk


def deform(name, bodies, mcs, given=()):
    """An instance of Deform; parameters not given take the spec defaults."""
    g = {p["parameterId"]: p for p in [
        en("deformMode", "DeformMode", "BODIES", NS),
        q("sourceBody", *bodies), q("mateConnectors", *mcs),
        q("fromEdges", FROM_EDGES), q("fromRef", FROM_REF), q("toEdges", TO_EDGES), q("toRef", TO_REF)] + list(given)}
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in g:
            params.append(g[pid])
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return feature(name, "deform", params, NS)


NAMES = [s("sourceNames", "Box A\nBox B"), s("nameSuffix", " (deformed)")]
deform("D1 boxes A+B, connectors MC_A+MC_B -> 2 solids; MC_A on A's result, MC_B stand-alone",
       [body(box_a), body(box_b)], [body(mc_a), body(mc_b)], NAMES)
deform("D2 connectors only -> 2 stand-alone connectors", [], [body(mc_a), body(mc_b)])
deform("D3 box A only -> 1 solid", [body(box_a)], [], [s("sourceNames", "Box A"), s("nameSuffix", " (deformed)")])
deform("D4 boxes A+B, no connectors -> 2 solids", [body(box_a), body(box_b)], [], NAMES)
