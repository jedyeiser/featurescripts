"""Build the Deform stress-test studio's features (the scratch duplicate of "CNT Testing Copy 1" in the curveMapping document).

  ONSHAPE_CALL_CAP=40 PYTHONPATH=. python devtools/onshape/build_deform_stress.py [builder=MAP_ALL] [only=thick,holed]

Removes the sandbox's trailing user features from THIS duplicate (never from the sandbox), adds the "Stress sources" feature
(curveMapping/stress_sources.fs) and one Deform per stress body, each cloned from test_Deform. The acceptance checks
(offset, thicken, body count, free edges, edge angles) are in check_deform_stress.py.
Cost: 1 features GET + 1 POST per feature written / deleted.
"""
import copy
import os
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, namespace_of

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("STRESS_STUDIO", "e0595b3c9b7337c1b2d19b25")
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

args = dict(a.split("=", 1) for a in sys.argv[1:])
builder = args.get("builder", "MAP_ALL")
only = args["only"].split(",") if "only" in args else None
run = args.get("run", "").split(",")      # the stress deforms left active (the studio's regen time is the limit: a write is refused, 409, when it is too long)

# case name -> (sub id under the Stress sources feature, entity filter, body name)
CASES = {
    "thick":  ("thick",  None,    "stress_thick_solid"),
    "holed":  ("copy",   None,    "stress_holed_sheet"),
    "cyl":    ("cyl",    None,    "stress_cylinder_solid"),
    "wall":   ("wall",   None,    "stress_cylinder_wall"),
    "sphere": ("sphere", None,    "stress_sphere"),
    "box":    ("box",    None,    "stress_box"),
}

cache = {}
tree = c.get(f"{BASE}/features")
cache["tree"] = tree
by_name = {x["name"]: x for x in tree["features"]}

# 1. clear the sandbox's trailing user features from this duplicate
for x in list(tree["features"]):
    drop = x["name"] in ("Offset+ 1", "split_faces_before", "Deform_after_Splitting_faces", "Thicken 1")
    if args.get("fresh") and (x["name"] == "Stress sources" or x["name"].startswith("stress_")):
        drop = True            # delete-and-add instead of replacing in place (a replace of the sources has returned 409)
    if x["featureType"] == "queryVariable" and any(p.get("value") == "deformed_surfs" for p in x["parameters"]):
        drop = True
    if drop:
        delete_feature(BASE, x["featureId"], cache, client=c)
        print("deleted", x["name"])

# 1b. the baseline test_Deform is suppressed: it costs regen time and the stress cases are what matter
for x in cache["tree"]["features"]:
    if x["name"] == "test_Deform" and not x.get("suppressed"):
        x["suppressed"] = True
        write_feature(BASE, x, cache, client=c)
        print("suppressed test_Deform")

# 2. the sources
ns = namespace_of(D, W, "stress_sources")
feature = {"btType": "BTMFeature-134", "featureType": "stressSources", "name": "Stress sources", "namespace": ns, "parameters": []}
r = write_feature(BASE, feature, cache, client=c)      # replaced in place (rebinds to the pushed tab's microversion)
sources_id = r["feature"]["featureId"]
print("Stress sources:", r.get("featureState", {}).get("featureStatus"), sources_id)

# 3. one deform per body
deform_ns = namespace_of(D, W, "deform")
template = [x for x in cache["tree"]["features"] if x["name"] == "test_Deform"][0]
template = copy.deepcopy(template)
for key, (sub, kind, body_name) in CASES.items():
    if only and key not in only:
        continue
    f = copy.deepcopy(template)
    f.pop("featureId", None)
    f["name"] = "stress_" + key
    f["suppressed"] = key not in run
    f["namespace"] = deform_ns          # bound to the pushed deform tab's current microversion
    for p in f["parameters"]:
        if p.get("namespace"):
            p["namespace"] = deform_ns
    expr = 'qCreatedBy(makeId("%s") + "%s", EntityType.BODY)' % (sources_id, sub)
    if kind:
        expr = "qBodyType(%s, BodyType.%s)" % (expr, kind)
    for p in f["parameters"]:
        if p["parameterId"] == "sourceBody":
            p["queries"] = [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % expr}]
        elif p["parameterId"] == "sourceNames":
            p["value"] = body_name
        elif p["parameterId"] == "faceBuilder":
            p["value"] = builder
    r = write_feature(BASE, f, cache, client=c)
    st = r.get("featureState", {})
    print("%-18s %s" % (f["name"], st.get("featureStatus")))
