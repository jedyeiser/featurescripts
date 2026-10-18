"""Union experiment on a deform feature in a test studio: put the feature in debug mode (faces only, no union), then in a
throwaway eval try to union the face bodies it made, and report (a) which single pieces, when left out, let the rest union
(b) the edges of those pieces shorter than 0.3 mm.

  EXP_STUDIO=<element id> EXP_FEATURE=<feature name> EXP_FEATURE_ID=<feature id> \
  PYTHONPATH=. python devtools/onshape/exp_deform_union.py [debug|normal]

Defaults: "CNT Testing Copy 1", Deform_after_Splitting_faces. Budget: 1 features GET + 1 POST + 1 eval (mode 'normal' puts the
feature back to a full run and still evaluates).
"""
import json
import os
import re
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature

c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("EXP_STUDIO", "3bc0438ef2ad392db2577808")   # CNT Testing Copy 1
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
FEATURE = os.environ.get("EXP_FEATURE", "Deform_after_Splitting_faces")
FEATURE_ID = os.environ.get("EXP_FEATURE_ID", "F2ZUuqGKma6pMPa_2")
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
    var pieces = evaluateQuery(context, qBodyType(qCreatedBy(makeId("FEATURE_ID"), EntityType.BODY), BodyType.SHEET));
    out = append(out, "U pieces " ~ size(pieces));
    try
    {
        opBoolean(context, makeId("all"), { "tools" : qUnion(pieces), "operationType" : BooleanOperationType.UNION, "keepTools" : true });
        out = append(out, "U all-at-once: OK");
    }
    catch (e)
    {
        out = append(out, "U all-at-once: FAIL " ~ toString(e));
    }
    // leave-one-out: which single piece, when removed, lets the union of the rest succeed?
    var fixes = [];
    for (var i = 0; i < size(pieces); i += 1)
    {
        var rest = [];
        for (var j = 0; j < size(pieces); j += 1)
            if (j != i)
                rest = append(rest, pieces[j]);
        try
        {
            opBoolean(context, makeId("loo" ~ i), { "tools" : qUnion(rest), "operationType" : BooleanOperationType.UNION, "keepTools" : true });
            var bb = evBox3d(context, { "topology" : pieces[i] });
            fixes = append(fixes, i);
            out = append(out, "U without piece " ~ i ~ ": union OK  (area " ~ roundToPrecision(evArea(context, { "entities" : pieces[i] }) / (millimeter ^ 2), 2) ~ " x " ~ roundToPrecision(bb.minCorner[0] / millimeter, 2) ~ ".." ~ roundToPrecision(bb.maxCorner[0] / millimeter, 2) ~ " y " ~ roundToPrecision(bb.minCorner[1] / millimeter, 1) ~ ".." ~ roundToPrecision(bb.maxCorner[1] / millimeter, 1) ~ ")");
            opDeleteBodies(context, makeId("loodel" ~ i), { "entities" : qCreatedBy(makeId("loo" ~ i), EntityType.BODY) });
        }
        catch (e)
        {
        }
    }
    out = append(out, "U pieces whose removal fixes the union: " ~ toString(fixes));
    // short edges of the pieces
    var shorts = [];
    for (var i = 0; i < size(pieces); i += 1)
        for (var e in evaluateQuery(context, qOwnedByBody(pieces[i], EntityType.EDGE)))
        {
            var len = evLength(context, { "entities" : e }) / millimeter;
            if (len < 0.3)
            {
                var p = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin / millimeter;
                shorts = append(shorts, "piece " ~ i ~ " edge " ~ roundToPrecision(len * 1000, 1) ~ " um at x " ~ roundToPrecision(p[0], 2) ~ " y " ~ roundToPrecision(p[1], 1));
            }
        }
    out = append(out, "U short edges (<0.3 mm): " ~ size(shorts));
    for (var s in shorts)
        out = append(out, "U   " ~ s);
    return out;
}""".replace("FEATURE_ID", FEATURE_ID)
res = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
for n in res.get("notices") or []:
    if n.get("level") == "ERROR":
        print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(res.get("result"))):
    if v.startswith("U "):
        print(v)
