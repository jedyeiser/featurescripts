"""Derived side of the QVX derive/split test: studio "QVX derive tests" (a Derive of "QVX derive src" must exist;
the user adds it by hand). Appends, after the Derive:
  1. Query variable X, Load from derive feature  -> variables `wires`, `w1`
  2. Split part on wire L1 (y=0) with the Right plane, keep BOTH sides
  3. Split part on wire L2 (y=50) with the Right plane, keep ONE side (mirrors the user's Tail_Split)
Check with check_qvx_split_tests.py. Refuses to run when features after the Derive already exist.

usage (repo root): ONSHAPE_CALL_CAP=6 PYTHONPATH=. python devtools/onshape/build_qvx_split_tests.py
"""
import json

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("query_variable_x/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
els = c.list_elements(D, W)
E = [e for e in els if e["name"] == "QVX derive tests"][0]["id"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in els if e["elementType"] == "FEATURESTUDIO" and e["name"] == "query_variable_x"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])

tree = c.get(f"{BASE}/features")
derived = [f for f in tree["features"] if f.get("featureType") == "importDerived"]
if len(derived) != 1:
    raise SystemExit("expected exactly one Derive in the studio, found %d" % len(derived))
if len(tree["features"]) != 1:
    raise SystemExit("studio already has %d features; not touching it" % len(tree["features"]))
DERIVE = derived[0]["featureId"]


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


post({"btType": "BTMFeature-134", "featureType": "queryVariable", "name": "load from derive", "namespace": NS, "parameters": [
    s("name", ""), s("description", ""), en("selectionType", "SelectionType", "LOAD_FROM_DERIVE", NS), q("selectionQuery"),
    b("addAdditionalQueries", False), arr("additionalQueries", []), arr("filters", []),
    en("filterCombine", "FilterCombine", "ALL", NS), b("debugStages", False), b("specifyOutputType", False),
    b("evaluateOnUse", False), b("showSelection", False), b("saveForDerivedStudios", False),
    {"btType": "BTMParameterFeatureList-1749", "parameterId": "deriveFeature", "featureIds": [DERIVE]}]})

WIRES = "qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)"
RIGHT = 'qCreatedBy(makeId("Right"), EntityType.FACE)'


def split(name, y, both):
    post({"btType": "BTMFeature-134", "featureType": "splitPart", "name": name, "parameters": [
        en("splitType", "SplitType", "PART"),
        q("targets", "qContainsPoint(%s, vector(-50, %s, 0) * millimeter)" % (WIRES, y)),
        q("tool", RIGHT), b("keepTools", False), b("keepBothSides", both), b("keepFront", True)]})


split("split L1 keep both sides", 0, True)
split("split L2 keep one side", 50, False)
