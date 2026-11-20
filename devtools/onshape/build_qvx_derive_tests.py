"""Source studio of the QVX derive/split test (see docs in the 2026-11-20 session notes).

Builds, in the "QVX derive src" Part Studio of the query_variable_x document:
  - sketch "wires sketch" on Top: L1 (-100,0)-(100,0) mm and L2 (-100,50)-(100,50) mm
  - Query variable X "wires": both sketch edges, Save for derived studios ON
  - Query variable X "w1":    the edge through (-50, 0, 0) mm (L1), Save for derived studios ON

The studio is created by hand/script beforehand and is ours alone. This script refuses to run when the
studio already has features (no deletes, no duplicates).

usage (repo root): ONSHAPE_CALL_CAP=10 PYTHONPATH=. python devtools/onshape/build_qvx_derive_tests.py
"""
import json

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("query_variable_x/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "QVX derive src"

els = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in els if e["elementType"] == "PARTSTUDIO"}
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in els if e["elementType"] == "FEATURESTUDIO" and e["name"] == "query_variable_x"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])

tree = c.get(f"{BASE}/features")
SK = None
KEEP = {}
deleted = False
for f in reversed(tree["features"]):
    if f["name"] in ("wires sketch", "wire L1 (y=0)", "wire L2 (y=50)"):
        KEEP[f["name"]] = f["featureId"]     # reuse sketch and wire bodies
    else:
        c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")   # old tagged variables
        deleted = True
SK = KEEP.get("wires sketch")
if deleted:
    tree = c.get(f"{BASE}/features")


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


def arr(pid, items):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid,
            "items": [{"btType": "BTMArrayParameterItem-1843", "parameters": it} for it in items]}


def post(feature):
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": tree["serializationVersion"], "sourceMicroversion": tree["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    tree["sourceMicroversion"] = r.get("sourceMicroversion", tree["sourceMicroversion"])
    print("%-40s %s" % (feature["name"][:40], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def seg(eid, x0, y0, x1, y1):
    L = ((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def qvx(var, source):
    return post({"btType": "BTMFeature-134", "featureType": "queryVariable", "name": var, "namespace": NS, "parameters": [
        s("name", var), s("description", ""), en("selectionType", "SelectionType", "SELECTION", NS), q("selectionQuery", source),
        b("addAdditionalQueries", False), arr("additionalQueries", []), arr("filters", []),
        en("filterCombine", "FilterCombine", "ALL", NS), b("debugStages", False), b("specifyOutputType", False),
        b("evaluateOnUse", False), b("showSelection", False), b("saveForDerivedStudios", True)]})


if SK is None:
    SK = post({"btType": "BTMSketch-151", "featureType": "newSketch", "name": "wires sketch",
               "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Top"), EntityType.FACE)')],
               "entities": [seg("L1", -0.1, 0.0, 0.1, 0.0), seg("L2", -0.1, 0.05, 0.1, 0.05)], "constraints": []})


def composite(name, x):
    """Wire body from the sketch edge through (x, y?, 0): one composite curve per line."""
    return post({"btType": "BTMFeature-134", "featureType": "compositeCurve", "name": name, "parameters": [
        q("edges", 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.EDGE), vector(%s, 0, 0) * millimeter)' % (SK, x))]})


def composite_y(name, y):
    return post({"btType": "BTMFeature-134", "featureType": "compositeCurve", "name": name, "parameters": [
        q("edges", 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.EDGE), vector(-50, %s, 0) * millimeter)' % (SK, y))]})


W1 = KEEP.get("wire L1 (y=0)") or composite_y("wire L1 (y=0)", 0)
W2 = KEEP.get("wire L2 (y=50)") or composite_y("wire L2 (y=50)", 50)
BODIES1 = 'qCreatedBy(makeId("%s"), EntityType.BODY)' % W1
BODIES2 = 'qCreatedBy(makeId("%s"), EntityType.BODY)' % W2
qvx("wires", "qUnion(%s, %s)" % (BODIES1, BODIES2))
qvx("w1", BODIES1)
print("studio element id", E)
