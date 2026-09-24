"""Build the Extract variables tracking / modified-key cases as real features in Variable_tools'
"Tracking tests" Part Studio (upsert by name). Checked by check_extract_tracking.py.

Setup: a 100 x 50 mm surface (line on Top extruded 50 mm in z), then
  X1 Extract variables before the move: its right edge (closest to a point at x 100, z 25),
     published held (edge_held) and tracked (edge_tracked)
  Move boundary: the held edge, 10 mm
  X2 Extract variables after the move: source Move boundary, modifiedEdges -> moved_edges
  R1 Ruled surface on the tracked variable (the RD 20FOU case: must regenerate)

usage (repo root): PYTHONPATH=. python devtools/onshape/build_extract_tracking_tests.py
"""
import json
import os
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("variable_tools/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Tracking tests"
studios = {e["name"]: e["id"] for e in c.list_elements(D, W) if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in c.list_elements(D, W) if e["name"] == "extract_variables"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
spec = [s for s in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if s["featureType"] == "extractVariables"][0]
ENTRY_DEFAULTS = [p["defaultValue"] for p in [x for x in spec["parameters"] if x["parameterId"] == "extractEntries"][0]["parameters"]]


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
    print("%-80s %s" % (feature["name"], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def sketch(name, plane_expr, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})


def entry(etype, key, name, track=False, point=None):
    given = {"x_type": en("x_type", "ExtractEntryType", etype, NS), "x_sourceKey": s("x_sourceKey", key),
             "x_name": s("x_name", name), "x_show": b("x_show", True), "x_track": b("x_track", track)}
    if point is not None:
        given["x_point"] = q("x_point", point)
    return {"btType": "BTMArrayParameterItem-1843",
            "parameters": [json.loads(json.dumps(given.get(d["parameterId"], d))) for d in ENTRY_DEFAULTS]}


def extract(name, sources, entries):
    return upsert({"btType": "BTMFeature-134", "featureType": "extractVariables", "name": name, "namespace": NS, "parameters": [
        {"btType": "BTMParameterFeatureList-1749", "parameterId": "sources", "featureIds": sources},
        s("prefix", ""), {"btType": "BTMParameterArray-2025", "parameterId": "extractEntries", "items": entries},
        b("printKeys", True), s("manifest", "")]})


def from_template(kind, name, queries, overrides):
    """A std feature copied from a saved template, with its query parameters replaced."""
    here = os.path.dirname(os.path.abspath(__file__))
    t = json.load(open(os.path.join(here, "extract_tracking_templates.json")))[kind]
    params = []
    for p in t["parameters"]:
        if p["parameterId"] in queries:
            params.append(q(p["parameterId"], queries[p["parameterId"]]))
        elif p["parameterId"] in overrides:
            params.append(overrides[p["parameterId"]])
        elif p["btType"].startswith("BTMParameterQueryList"):
            params.append(q(p["parameterId"]))
        else:
            params.append(p)
    return upsert({"btType": "BTMFeature-134", "featureType": t["featureType"], "name": name, "parameters": params})


line = {"btType": "BTMSketchCurveSegment-155", "entityId": "l0", "startPointId": "l0.start", "endPointId": "l0.end",
        "startParam": 0.0, "endParam": 0.1, "isConstruction": False,
        "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": 0.0, "pntY": 0.0, "dirX": 1.0, "dirY": 0.0}}
s1 = sketch("Sketch: line 0,0 > 100,0 on Top", 'qCreatedBy(makeId("Top"), EntityType.FACE)', [line])
ext = upsert({"btType": "BTMFeature-134", "featureType": "extrude", "name": "Surface 100 x 50 (extrude 50 mm in z)", "parameters": [
    en("bodyType", "ExtendedToolBodyType", "SURFACE"), en("surfaceOperationType", "NewSurfaceOperationType", "NEW"),
    q("surfaceEntities", 'qConstructionFilter(qCreatedBy(makeId("%s"), EntityType.EDGE), ConstructionObject.NO)' % s1),
    en("endBound", "BoundingType", "BLIND"), num("depth", "50 mm")]})
pt = {"btType": "BTMSketchPoint-158", "entityId": "p0", "isConstruction": False, "x": 0.1, "y": 0.025}
s2 = sketch("Sketch: point x 100, z 25 on Front (picks the right edge)", 'qCreatedBy(makeId("Front"), EntityType.FACE)', [pt])
point = 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % s2
x1 = extract("X1 Extract before the move: right edge held (edge_held) and tracked (edge_tracked)", [ext], [
    entry("CLOSEST", "outputEdges", "edge_held", False, point),
    entry("CLOSEST", "outputEdges", "edge_tracked", True, point)])
mb = from_template("extend", "Move boundary: edge_held, 10 mm", {"entities": 'getQueryVariable(context, "edge_held")'},
                   {"extendDistance": num("extendDistance", "10 mm")})
x2 = extract("X2 Extract after the move: source Move boundary, modifiedEdges -> moved_edges", [mb], [
    entry("SOURCE_KEY", "modifiedEdges", "moved_edges")])
from_template("ruled", "R1 Ruled surface on edge_tracked -> regenerates (the RD 20FOU case)",
              {"edges": 'getQueryVariable(context, "edge_tracked")'}, {"distance": num("distance", "20 mm"),
               "surfaceOperationType": en("surfaceOperationType", "NewSurfaceOperationType", "NEW")})
print("studio", E)
