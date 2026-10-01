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


# ---------------------------------------------------------------------------------------------------------------------
# API budget (CLAUDE.md "Onshape API budget"): a feature write used to cost a features GET + the POST, and callers
# cleared their cache after every write. write_feature keeps one feature tree per Part Studio in step with each write
# from the write's own response (it carries the new sourceMicroversion, the feature and its status), so N features
# cost N + 1 calls, not 2N. Pass a `cache` dict per Part Studio (or use upsert(), which keeps one per studio).
# ---------------------------------------------------------------------------------------------------------------------

_TREES = {}


def cached_tree(base, cache, client=None):
    """The Part Studio's feature tree (one GET the first time, then kept in step by write_feature)."""
    if cache.get("tree") is None:
        cache["tree"] = (client or c).get(f"{base}/features")
    return cache["tree"]


def write_feature(base, feature, cache, match=None, client=None):
    """Add `feature`, or replace the existing one (same name, or `match(x)` true) in place. `base` is
    /api/v10/partstudios/d/<did>/w/<wid>/e/<eid>. Returns the POST response (its "featureState" is the status of the
    feature just written; other features' statuses in cache["tree"]["featureStates"] may be stale)."""
    client = client or c
    f = cached_tree(base, cache, client)
    existing = [x for x in f["features"] if (match(x) if match else x["name"] == feature["name"])]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = client.post(f"{base}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = client.post(f"{base}/features", body)
    note_write(cache, r, existing[0]["featureId"] if existing else None)
    return r


def note_write(cache, response, replaced_id=None):
    """Bring cache["tree"] up to date from a write's response; drop it (one re-GET later) if the response lacks the fields."""
    f = cache.get("tree")
    new = response.get("feature")
    if f is None:
        return
    if not new or not response.get("sourceMicroversion"):
        cache["tree"] = None
        return
    f["sourceMicroversion"] = response["sourceMicroversion"]
    if replaced_id is None:
        f["features"].append(new)
    else:
        f["features"] = [new if x["featureId"] == replaced_id else x for x in f["features"]]
    f.setdefault("featureStates", {})[new["featureId"]] = response.get("featureState", {})


def feature_status(base, cache, feature_id, client=None):
    """Status of a feature just written: from the write's own response, else one fresh features GET."""
    t = cache.get("tree")
    status = ((t or {}).get("featureStates", {}).get(feature_id) or {}).get("featureStatus")
    if status is None:
        cache["tree"] = None
        status = cached_tree(base, cache, client)["featureStates"][feature_id]["featureStatus"]
    return status


def delete_feature(base, feature_id, cache, client=None):
    """Delete a feature and keep the cached tree in step (the response carries the new sourceMicroversion)."""
    r = (client or c)._request("DELETE", f"{base}/features/featureid/{feature_id}")
    f = cache.get("tree")
    if f is not None:
        if r.get("sourceMicroversion"):
            f["sourceMicroversion"] = r["sourceMicroversion"]
            f["features"] = [x for x in f["features"] if x["featureId"] != feature_id]
            f.get("featureStates", {}).pop(feature_id, None)
        else:
            cache["tree"] = None
    return r


def upsert(did, wid, eid, feature_type, name, params, namespace=""):
    """Add a feature named `name`, or replace the parameters of the existing one with that name."""
    base = f"/api/v10/partstudios/d/{did}/w/{wid}/e/{eid}"
    feature = {"btType": "BTMFeature-134", "featureType": feature_type, "name": name,
               "namespace": namespace, "parameters": params}
    r = write_feature(base, feature, _TREES.setdefault(base, {}))
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
    base = f"/api/v10/partstudios/d/{did}/w/{wid}/e/{eid}"
    return [x for x in cached_tree(base, _TREES.setdefault(base, {}))["features"] if x["name"] == name][0]["featureId"]
