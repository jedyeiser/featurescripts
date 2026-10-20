"""Add a wrapCurve test over a CLOSED ring wire to the Deform stress studio (the scratch duplicate of "CNT Testing Copy 1").

  ONSHAPE_CALL_CAP=30 PYTHONPATH=. python devtools/onshape/build_wrap_stress.py

The wrapCurve definition is cloned from "Wrap Curve 1" in the user's "Test" studio (read only); the from/to path queries come from
test_Deform in the stress studio (same paths), and the source curve is the "ring" wire made by the Stress sources feature
(a full circle as one curve: a clamped open spline closed on itself is refused by the kernel, BAD_GEOMETRY). The feature is
written suppressed unless run=1 (the studio's regen time is limited: see build_deform_stress.py).
Cost: about 6 calls.
"""
import copy
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, namespace_of

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
TEST_STUDIO = "75f77ff5a77da7d50bfefe0c"          # the user's Test studio: template only, never written
E = "e0595b3c9b7337c1b2d19b25"
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
args = dict(a.split("=", 1) for a in sys.argv[1:])
run = args.get("run") == "1"
kind = args.get("kind", "wrapCurve")          # wrapCurve | wrapAndLoft
studio = {"wrapCurve": "75f77ff5a77da7d50bfefe0c", "wrapAndLoft": "75f77ff5a77da7d50bfefe0c"}[kind]

template = [x for x in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{studio}/features")["features"] if x["featureType"] == kind][0]
cache = {"tree": c.get(f"{BASE}/features")}
tree = cache["tree"]
deform = [x for x in tree["features"] if x["name"] == "test_Deform"][0]
sources = [x for x in tree["features"] if x["featureType"] == "stressSources"][0]

f = copy.deepcopy(template)
f.pop("featureId", None)
f["name"] = "stress_%s_ring" % kind
f["suppressed"] = not run
ns = namespace_of(D, W, "wrapCurve" if kind == "wrapCurve" else "wrapAndLoft_i")
f["namespace"] = ns
paths = {p["parameterId"]: p for p in deform["parameters"] if p["parameterId"] in ("fromEdges", "fromRef", "toEdges", "toRef")}
for p in f["parameters"]:
    if p["parameterId"] in paths:
        p["queries"] = copy.deepcopy(paths[p["parameterId"]]["queries"])
    elif p["parameterId"] in ("sourceCurves", "sourceEdges"):
        p["queries"] = [{"btType": "BTMIndividualQuery-138", "queryStatement": None,
                         "queryString": 'query=qCreatedBy(makeId("%s") + "ring", EntityType.EDGE);' % sources["featureId"]}]
    elif p["parameterId"].startswith("debug") or p["parameterId"] in ("showWrappedPoints", "showSourcePoints", "printWrapDetails"):
        p["value"] = False
    if kind == "wrapAndLoft" and p["parameterId"] == "primaryOffset":
        p["expression"] = "5 mm"           # a loft needs distinct rails
    if kind == "wrapAndLoft" and p["parameterId"] == "outputCurveMode" and args.get("keepall"):
        p["value"] = "KEEP_ALL"
    if kind == "wrapAndLoft" and p["parameterId"] == "secondDirection":
        p["value"] = False
    if p.get("namespace"):
        p["namespace"] = ns
r = write_feature(BASE, f, cache, client=c)
print(f["name"], r.get("featureState", {}).get("featureStatus"), "suppressed =", f["suppressed"])
