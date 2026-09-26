"""Sample the edges of named wire bodies (eval API) -> JSON {name: [[ [x,y,z] mm ...] per edge]}.
usage: PYTHONPATH=. python docs/tooling/_scratch/sample_named.py <elem> <out.json> name ...
"""
import json, re, sys
from sync.core.client import OnshapeClient
D, W = "f8deedeb1fbd819a8fa20113", "33c32ba27726cb036d4d912b"
c = OnshapeClient()
E, out, names = sys.argv[1], sys.argv[2], sys.argv[3:]
script = """function(context is Context, queries) {
  var out = [];
  for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)))
  {
    const nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME });
    if (nm == undefined || !isIn(nm, %s)) { continue; }
    for (var e in evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE)))
    {
      var ps = [];
      for (var i = 0; i <= 150; i += 1) { ps = append(ps, i / 150); }
      var s = "E|" ~ nm;
      for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : ps }))
      {
        s = s ~ "|" ~ toString(tl.origin[0] / millimeter) ~ "," ~ toString(tl.origin[2] / millimeter);
      }
      out = append(out, s);
    }
  }
  return out; }""" % json.dumps(names)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
res = {}
for s in re.findall(r'"value":\s*"(E\|[^"]*)"', json.dumps(r)):
    p = s.split("|")
    res.setdefault(p[1], []).append([[float(v) for v in q.split(",")] for q in p[2:]])
for k, v in res.items():
    print(k, len(v), "edges")
if not res:
    print(json.dumps(r)[:1500])
json.dump(res, open(out, "w"))
