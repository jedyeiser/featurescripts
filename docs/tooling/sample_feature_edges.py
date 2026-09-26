"""Sample every edge a feature created (read-only eval API) -> JSON {feature name: [{"type", "radius", "body", "pts": [[x, y, z] mm]}]}.
Edges of wire bodies and sketch bodies; bodies of the feature only (qCreatedBy BODY -> owned edges).
usage (repo root): PYTHONPATH=. python docs/tooling/sample_feature_edges.py <did> <wid> <eid> <out.json> "<feature name or prefix>" ...
A name ending in '*' matches the first feature whose name starts with the rest.
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

D, W, E, OUT = sys.argv[1:5]
c = OnshapeClient()
feats = [(f["name"], f["featureId"]) for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]]
res = {}
for want in sys.argv[5:]:
    hit = [f for f in feats if (f[0].startswith(want[:-1]) if want.endswith("*") else f[0] == want)]
    if not hit:
        print("no feature", want)
        continue
    name, fid = hit[0]
    script = """function(context is Context, queries) {
      var out = [];
      var b = 0;
      for (var body in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.BODY)))
      {
      for (var e in evaluateQuery(context, qOwnedByBody(body, EntityType.EDGE)))
      {
        const cd = evCurveDefinition(context, { "edge" : e });
        var s = "E|" ~ cd.curveType ~ "|" ~ (cd.curveType == CurveType.CIRCLE ? toString(cd.radius / millimeter) : "0") ~ "|" ~ b;
        var ps = [];
        for (var i = 0; i <= 60; i += 1) { ps = append(ps, i / 60); }
        for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : ps }))
        {
          s = s ~ "|" ~ toString(tl.origin[0] / millimeter) ~ "," ~ toString(tl.origin[1] / millimeter) ~ "," ~ toString(tl.origin[2] / millimeter);
        }
        out = append(out, s);
      }
      b += 1;
      }
      return out; }""" % fid
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
    edges = []
    for s in re.findall(r'"value":\s*"(E\|[^"]*)"', json.dumps(r)):
        p = s.split("|")
        edges.append({"type": p[1], "radius": float(p[2]), "body": int(p[3]), "pts": [[float(v) for v in q.split(",")] for q in p[4:]]})
    res[want] = edges
    print(name[:50], len(edges), "edges")
json.dump(res, open(OUT, "w"))
