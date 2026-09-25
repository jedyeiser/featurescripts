"""Unwrap test cases as real features in driven_offset's unwrap test Part Studio (a copy of the user's
Unwrap_Testing, so every derived body keeps its deterministic id). Upsert by name.

  U1 Wrapped_profile (3 splines) unwrapped over FULL_BASELINE, length along the reference
     -> one wire; the tip/tail stretches come out straight at constant height (the profile sits 5.95 above)
  U2 Topsheet (0.4 mm, draped on the ski top surface) undraped about Wrapped_profile, length along the
     mid-thickness, laid on the plane -> one flat plate 0.4 thick; thickness detected 0.4

Alignment point and unwrapped origin: the derived mate connector at (885, 0, 0), X = +X, Z = +Z.

usage (repo root): PYTHONPATH=. python devtools/onshape/build_unwrap_tests.py
"""
import copy

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
ELEMENTS = c.list_elements(D, W)
E = [e["id"] for e in ELEMENTS if e["name"] in ("Unwrap tests", "Unwrap_Testing Copy 1")][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

# Deterministic ids of the derived bodies / entities (same feature ids as Unwrap_Testing).
WRAPPED_PROFILE = "RjRP"
FULL_BASELINE = "RjRL"
TOPSHEET = "RnRD"
MATE_CONNECTOR = "StjLB"


def tab(name):
    t = [e for e in ELEMENTS if e["name"] == name][0]
    return t, "e%s::m%s" % (t["id"], t["microversionId"])


def qd(pid, *ids):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "deterministicIds": [i]} for i in ids]}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


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


def unwrap(name, given, enums):
    t, ns = tab("unwrap")
    spec = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{t['id']}/featurespecs")["featureSpecs"]
            if x["featureType"] == "unwrap"][0]
    given = {p["parameterId"]: p for p in given}
    params = []
    for p in spec["parameters"]:
        d = p.get("defaultValue")
        if not isinstance(d, dict):
            continue
        pid = p["parameterId"]
        if pid in given:
            params.append(given[pid])
        elif pid in enums:
            e = copy.deepcopy(d)
            e["parameterId"] = pid
            e["value"] = enums[pid]
            params.append(e)
        else:
            params.append(dict(d, parameterId=pid))
    return upsert({"btType": "BTMFeature-134", "featureType": "unwrap", "name": name, "namespace": ns, "parameters": params})


unwrap("U1 Wrapped_profile over FULL_BASELINE (edges, along reference) -> 1 wire, tip/tail at constant height",
       [qd("edges", WRAPPED_PROFILE), qd("reference", FULL_BASELINE), qd("alignPoint", MATE_CONNECTOR), qd("origin", MATE_CONNECTOR),
        b("debugPrintEdges", True)],
       {"unwrapType": "EDGES", "preserveLength": "REFERENCE"})

unwrap("U2 Topsheet undraped about Wrapped_profile (mid-thickness, on plane) -> 1 flat plate 0.4 thick",
       [qd("parts", TOPSHEET), qd("reference", WRAPPED_PROFILE), qd("alignPoint", MATE_CONNECTOR), qd("origin", MATE_CONNECTOR),
        b("debugPrintEdges", True)],
       {"unwrapType": "THICKENED", "preserveLength": "MID_THICKNESS"})
print("studio", E)
