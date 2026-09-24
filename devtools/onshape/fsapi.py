"""Insert / update feature instances in a Part Studio through the REST API.

Parameters are built with the helpers below; queries are FeatureScript query expressions
(stored as queryString "query=<expr>;").
"""
import json

from sync.core.client import OnshapeClient

c = OnshapeClient()


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None,
                         "queryString": "query=%s;" % e} for e in exprs]}


def b(pid, value):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": bool(value)}


def num(pid, expression):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expression, "isInteger": False}


def en(pid, enum_name, value, namespace=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum_name, "value": value, "namespace": namespace}


def s(pid, value):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": value}


def namespace_of(did, wid, studio_name):
    e = [x for x in c.list_elements(did, wid) if x["name"] == studio_name][0]
    return "e%s::m%s" % (e["id"], e["microversionId"])


def features(did, wid, eid):
    return c.get(f"/api/v10/partstudios/d/{did}/w/{wid}/e/{eid}/features")


def upsert(did, wid, eid, feature_type, name, params, namespace=""):
    """Add a feature named `name`, or replace the parameters of the existing one with that name."""
    f = features(did, wid, eid)
    feature = {"btType": "BTMFeature-134", "featureType": feature_type, "name": name,
               "namespace": namespace, "parameters": params}
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    existing = [x for x in f["features"] if x["name"] == name]
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"/api/v10/partstudios/d/{did}/w/{wid}/e/{eid}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"/api/v10/partstudios/d/{did}/w/{wid}/e/{eid}/features", body)
    state = r.get("featureState", {})
    print("%-40s %s" % (name, state.get("featureStatus")))
    return r


def qids(pid, ids):
    """A selection by deterministic (transient) entity ids; Onshape stores it as a persistent query."""
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "",
                         "deterministicIds": list(ids)}]}


def qfeat(pid, feature_ids, entity="FACE"):
    return q(pid, *['qCreatedBy(makeId("%s"), EntityType.%s)' % (f, entity) for f in feature_ids])


def integer(pid, value):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": str(value), "isInteger": True}


def feature_id(did, wid, eid, name):
    return [x for x in features(did, wid, eid)["features"] if x["name"] == name][0]["featureId"]
