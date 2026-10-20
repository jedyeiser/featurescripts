"""Set the stress studio's test_Deform to deform TWO source bodies (the user's Lid_Geo_Deform case: filleted_top_surf + periphery_surf),
write it (rebound to the pushed deform tab) and report the result bodies.

  ONSHAPE_CALL_CAP=20 PYTHONPATH=. python devtools/onshape/exp_deform_two_bodies.py [guide=1] [builder=MAP_ALL] [bodies=filleted_top_surf,periphery_surf]
"""
import json
import re
import sys
import time

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, namespace_of

c = OnshapeClient()
D, W, E = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89", "e0595b3c9b7337c1b2d19b25"
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
args = dict(a.split("=", 1) for a in sys.argv[1:])
bodies = args.get("bodies", "filleted_top_surf,periphery_surf").split(",")

tree = {"tree": c.get(f"{BASE}/features")}
feature = [x for x in tree["tree"]["features"] if x["name"] == "test_Deform"][0]
ns = namespace_of(D, W, "deform")
feature["namespace"] = ns
feature["suppressed"] = False
for p in feature["parameters"]:
    if p.get("namespace"):
        p["namespace"] = ns
    if p["parameterId"] == "sourceBody":
        p["queries"] = [{"btType": "BTMIndividualParametricQuery-3477", "queryVariableName": b, "queryStatement": None,
                         "queryString": 'query = getQueryVariable(context, "%s");' % b} for b in bodies]
    elif p["parameterId"] == "sourceNames":
        p["value"] = "\n".join(bodies)
    elif p["parameterId"] == "useFacePoints":
        p["value"] = args.get("guide") == "1"
    elif p["parameterId"] == "faceBuilder":
        p["value"] = args.get("builder", "MAP_ALL")
t0 = time.perf_counter()
try:
    r = write_feature(BASE, feature, tree, client=c)
    print("write+regen seconds: %.1f" % (time.perf_counter() - t0))
    print("status:", r.get("featureState", {}).get("featureStatus"))
except Exception as ex:
    print("write FAILED after %.1f s: %s" % (time.perf_counter() - t0, str(ex)[:200].replace("\n", " ")))
    sys.exit(1)

S = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm is string && match(nm, ".*\\\\(deformed\\\\)").hasMatch)
            out = append(out, "R " ~ nm ~ " | faces " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) ~ " | " ~ (size(evaluateQuery(context, qBodyType(b, BodyType.SOLID))) > 0 ? "solid" : "sheet"));
    }
    return out;
}"""
res = c.post(f"{BASE}/featurescript", json_data={"script": S})
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(res.get("result"))):
    if v.startswith("R "):
        print(v)
