"""Union experiment on the "CNT Testing Copy 1" studio: set Deform_after_Splitting_faces to debug (faces only, no union),
then try the union of its face bodies in a throwaway eval (one body at a time, so the failing body is named).

  PYTHONPATH=. python devtools/onshape/exp_deform_union.py [debug|normal]
Budget: 1 features GET + 1 POST + 1 eval.
"""
import json
import re
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature

c = OnshapeClient()
D, W, E = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89", "3bc0438ef2ad392db2577808"   # CNT Testing Copy 1
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
FEATURE = "Deform_after_Splitting_faces"
mode = sys.argv[1] if len(sys.argv) > 1 else "debug"

tree = {}
f = c.get(f"{BASE}/features")
tree["tree"] = f
feature = [x for x in f["features"] if x["name"] == FEATURE][0]
for p in feature["parameters"]:
    if p["parameterId"] == "debug":
        p["value"] = (mode == "debug")
    if p["parameterId"] == "createFaces":
        p["value"] = (mode == "debug")
r = write_feature(BASE, feature, tree, client=c)
print("status:", r.get("featureState", {}).get("featureStatus"))

SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var pieces = evaluateQuery(context, qBodyType(qCreatedBy(makeId("F2ZUuqGKma6pMPa_2"), EntityType.BODY), BodyType.SHEET));
    out = append(out, "U pieces " ~ size(pieces));
    // collisions between pieces whose boxes overlap: report the kind for pairs that are not a clean edge contact
    for (var i = 0; i < size(pieces); i += 1)
    {
        for (var j = i + 1; j < size(pieces); j += 1)
        {
            var bi = evBox3d(context, { "topology" : pieces[i] });
            var bj = evBox3d(context, { "topology" : pieces[j] });
            var tol = 1e-4 * meter;
            var overlap = true;
            for (var k = 0; k < 3; k += 1)
                if (bi.maxCorner[k] < bj.minCorner[k] - tol || bj.maxCorner[k] < bi.minCorner[k] - tol)
                    overlap = false;
            if (!overlap) continue;
            var cs = evCollision(context, { "tools" : pieces[i], "targets" : pieces[j] });
            var kinds = [];
            for (var cc in cs)
                kinds = append(kinds, toString(cc["type"]));
            if (i == 35 || j == 35 || i == 43 || j == 43)
                out = append(out, "U pair " ~ i ~ "," ~ j ~ " : " ~ toString(kinds));
        }
    }    return out;
}"""
res = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
for n in res.get("notices") or []:
    if n.get("level") == "ERROR":
        print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(res.get("result"))):
    if v.startswith("U "):
        print(v)
