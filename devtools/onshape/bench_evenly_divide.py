"""Time the regeneration of Evenly divide surface in the "Divide tests" Part Studio (full surface, symmetry plane = Front).

Each case writes one feature (so Onshape regenerates it) and reports the wall time of that write; the feature is deleted
afterwards. Uses the Derived feature that build_evenly_divide_tests.py made; run that first.

  python devtools/onshape/bench_evenly_divide.py                 # default case list
  BENCH="5:60:1,16:60:1" python ...                              # divisions:sections:refine(1/0)[:tolerance_mm]

usage (repo root): PYTHONPATH=. FS_SYNC_TIMEOUT=180 python devtools/onshape/bench_evenly_divide.py
"""
import json
import os
import re
import time

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, cached_tree

c = OnshapeClient()
D, W = "69ab3696c014c05409202108", "c176011bc60a3f918e673d98"
ELEMENTS = c.list_elements(D, W)
E = [e["id"] for e in ELEMENTS if e["name"] == "Divide tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in ELEMENTS if e["name"] == "evenly_divide_surface" and e["elementType"] == "FEATURESTUDIO"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "evenlyDivideSurface"][0]
_TREE = {}

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var wire = !isQueryEmpty(context, qIntersection([b, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)]));
        var sheet = !isQueryEmpty(context, qIntersection([b, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)]));
        out = append(out, "B " ~ toString(b) ~ " " ~ (wire ? "WIRE" : (sheet ? "SHEET" : "OTHER")) ~ " " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))));
    }
    return out;
}"""
r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
rows = []
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    m = re.search(r"transientId : (\w+) \} (\w+) (\d+)", v)
    if v.startswith("B ") and m:
        rows.append((m.group(1), m.group(2), int(m.group(3))))
full = [i for i, k, n in rows if k == "SHEET" and n == 47][0]
wire = [i for i, k, n in rows if k == "WIRE"][0]


def qids(pid, ids):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": list(ids)}]}


def q(pid, expr):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % expr}]}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


def boolean(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def feature(name, divisions, sections, refine, tol_mm, on_plane=True):
    given = {p["parameterId"]: p for p in [
        qids("surface", [full]), qids("crossWire", [wire]), q("symmetryPlane", 'qCreatedBy(makeId("Front"), EntityType.FACE)'),
        num("divisions", str(divisions), True), num("sections", str(sections), True), boolean("onSymmetryPlane", on_plane),
        boolean("refine", refine), num("surfaceToleranceMm", "%g" % tol_mm)]}
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in given:
            params.append(given[pid])
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return {"btType": "BTMFeature-134", "featureType": "evenlyDivideSurface", "name": name, "namespace": NS, "parameters": params}


cases = []
for item in os.environ.get("BENCH", "5:60:1,5:60:0,16:60:1,16:60:0,5:4:0").split(","):
    t = item.split(":")
    cases.append((int(t[0]), int(t[1]), t[2] == "1", float(t[3]) if len(t) > 3 else 0.01, (t[4] == "1") if len(t) > 4 else True))

print("%-34s %8s  %s" % ("case (divisions:sections:refine:tol)", "seconds", "status"))
for n, s, refine, tol, on_plane in cases:
    name = "BENCH %d:%d:%d:%g:%d" % (n, s, refine, tol, on_plane)
    t0 = time.perf_counter()
    resp = write_feature(BASE, feature(name, n, s, refine, tol, on_plane), _TREE, client=c)
    dt = time.perf_counter() - t0
    print("%-34s %8.1f  %s" % ("%d:%d:%d:%g:%d" % (n, s, refine, tol, on_plane), dt, resp.get("featureState", {}).get("featureStatus")))
    delete_feature(BASE, resp["feature"]["featureId"], _TREE, client=c)
