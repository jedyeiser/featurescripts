"""Sample every edge created by the named features (eval API) -> JSON {feature: [[ [x,y,z]mm ... ] per edge]}.
usage: PYTHONPATH=. python docs/tooling/_scratch/sample_edges.py <elem> <out.json> "<feature name>" ...
"""
import json, re, sys
from sync.core.client import OnshapeClient
D, W = "f8deedeb1fbd819a8fa20113", "33c32ba27726cb036d4d912b"
c = OnshapeClient()
E, out = sys.argv[1], sys.argv[2]
feats = {f["name"]: f["featureId"] for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]}
res = {}
for name in sys.argv[3:]:
    fid = feats[name]
    script = """function(context is Context, queries) {
      var out = [];
      for (var e in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.EDGE)))
      {
        var ps = [];
        for (var i = 0; i <= 120; i += 1) { ps = append(ps, i / 120); }
        var s = "E";
        for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : ps }))
        {
          s = s ~ "|" ~ toString(tl.origin[0] / millimeter) ~ "," ~ toString(tl.origin[1] / millimeter) ~ "," ~ toString(tl.origin[2] / millimeter);
        }
        out = append(out, s);
      }
      return out; }""" % fid
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
    edges = []
    for s in re.findall(r'"value":\s*"(E\|[^"]*)"', json.dumps(r)):
        edges.append([[float(v) for v in p.split(",")] for p in s.split("|")[1:]])
    res[name] = edges
    print(name, len(edges), "edges")
json.dump(res, open(out, "w"))
