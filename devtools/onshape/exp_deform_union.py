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
    var pieces = evaluateQuery(context, qBodyType(qCreatedBy(makeId("FEATURE_ID") + "SUBID", EntityType.BODY), BodyType.SHEET));
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
    var total = 0 * meter ^ 2;
    for (var i = 0; i < size(pieces); i += 1)
        total += evArea(context, { "entities" : pieces[i] });
    out = append(out, "U sum of piece areas: " ~ roundToPrecision(total / (millimeter ^ 2), 0) ~ " mm2 over " ~ size(pieces) ~ " pieces");
    // two-piece unions of all touching pairs were fine; try the unions of every window of 3 consecutive (x-sorted) pieces
    var bodiesByX = [];
    for (var i = 0; i < size(pieces); i += 1)
        bodiesByX = append(bodiesByX, { "i" : i, "x" : evBox3d(context, { "topology" : pieces[i] }).minCorner[0] / meter });
    bodiesByX = sort(bodiesByX, function(a, b) { return a.x - b.x; });
    var windowFails = [];
    for (var w = 0; w + 8 <= size(bodiesByX); w += 4)
    {
        var q = [];
        for (var m = w; m < w + 8; m += 1)
            q = append(q, pieces[bodiesByX[m].i]);
        try
        {
            opBoolean(context, makeId("win" ~ w), { "tools" : qUnion(q), "operationType" : BooleanOperationType.UNION, "keepTools" : true });
            opDeleteBodies(context, makeId("windel" ~ w), { "entities" : qCreatedBy(makeId("win" ~ w), EntityType.BODY) });
        }
        catch (e)
        {
            var ids = [];
            for (var m = w; m < w + 8; m += 1)
                ids = append(ids, bodiesByX[m].i);
            windowFails = append(windowFails, "window at x " ~ roundToPrecision(bodiesByX[w].x * 1000, 0) ~ " pieces " ~ toString(ids) ~ ": " ~ toString(e));
        }
    }
    out = append(out, "U 8-piece windows failing: " ~ size(windowFails));
    for (var s in windowFails)
        out = append(out, "U   " ~ s);    // short edges of the pieces
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
}""".replace("FEATURE_ID", FEATURE_ID).replace("SUBID", os.environ.get("EXP_SUB", "unit0retry"))
res = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
for n in res.get("notices") or []:
    if n.get("level") == "ERROR":
        print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(res.get("result"))):
    if v.startswith("U "):
        print(v)
