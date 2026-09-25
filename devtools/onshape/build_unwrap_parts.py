"""Unwrap every production part of Unwrap_Testing's "Derived 1" onto the Top plane, as real features in
"Unwrap_Testing Copy 2" (upsert by name). Plates first; the core, core extensions, sidewalls and 4401s wait for
the part mode (research_unwrap_part.md).

usage (repo root): PYTHONPATH=. python devtools/onshape/build_unwrap_parts.py
"""
import copy

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "f61d2c000ab2d1240776342e", "5b11f323ab31b04cba8b36ef"
ELEMENTS = c.list_elements(D, W)
E = "681a5825e353d376c88224eb"   # "Unwrap_Testing Copy 2" (copyelement of Unwrap_Testing incl. the CORE boolean)
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

# Deterministic ids of the derived bodies / entities (same feature ids as Unwrap_Testing).
WRAPPED_PROFILE = "RjRP"
FULL_BASELINE = "RjRL"
TOPSHEET = "RnRD"
MATE_CONNECTOR = "StjLB"
FRONT_PLANE = "JCC"


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



def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


REF_WIRE = "RtjD"
TOP_PLANE = "JDC"
MC = "StjLB"

# Plates spanning the alignment station: target = the section of their own mid-surface by the Front plane.
ONLY = __import__("os").environ.get("ONLY")
for name, body in [("4101 base", "RtjP"), ("4305", "RtjT"), ("6005", "Rtjf"), ("4310", "Rtjj"), ("Topsheet", "RnRD")]:
    if ONLY and ONLY != body:
        continue
    unwrap("Unwrap %s (plate, own section)" % name,
           [qd("parts", body), qd("profileFace", "JCC"), qd("alignPoint", MC), qd("origin", TOP_PLANE), b("debugPrintEdges", body == "RtjP")],
           {"unwrapType": "THICKENED", "targetFrom": "FACE"})

# Tip / tail pieces that do not reach the alignment station: REF_WIRE, offset to their mid-height.
for name, body, offset in [("Tip-Mat", "RtjX", "1.88 mm"), ("Tail-Mat", "Rtjb", "1.8 mm"),
                           ("Tip-Shear", "Rtj/", "1.96 mm"), ("Tail-Shear", "StjDB", "1.9 mm")]:
    if ONLY and ONLY != body:
        continue
    unwrap("Unwrap %s (plate, REF_WIRE offset %s)" % (name, offset),
           [qd("parts", body), qd("reference", REF_WIRE), num("targetOffset", offset), qd("alignPoint", MC), qd("origin", TOP_PLANE)],
           {"unwrapType": "THICKENED", "targetFrom": "WIRE"})
print("studio", E)
