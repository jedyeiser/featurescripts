import json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="d0950ed72a894fbe35c27d43","55351e2bc9e8b4ec141bf4b6","51c4e18aab64c557561f2b92"
feats={f["name"]:f for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]}
g=feats["Gordon Surface 1"]
for p in g["parameters"]:
    if "queries" in p: print(p["parameterId"], [q.get("deterministicIds") or q.get("queryString","")[:80] for q in p["queries"]][:3])
fid=g["featureId"]
script="""function(context is Context, queries) {
  var out = [];
  for (var f in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.FACE)))
  {
    for (var i = 0; i <= 24; i += 1) { for (var j = 0; j <= 24; j += 1) {
      const pl = evFaceTangentPlane(context, { "face" : f, "parameter" : vector(i / 24, j / 24) });
      out = append(out, "S|" ~ i ~ "|" ~ j ~ "|" ~ toString(pl.origin[0] / millimeter) ~ "," ~ toString(pl.origin[1] / millimeter) ~ "," ~ toString(pl.origin[2] / millimeter));
    } }
  }
  const face = qCreatedBy(makeId("%s"), EntityType.FACE);
  for (var e in evaluateQuery(context, qOwnedByBody(qBodyType(qEverything(EntityType.BODY), BodyType.WIRE), EntityType.EDGE)))
  {
    {
      var onFace = true;
      for (var t in [0.1, 0.35, 0.65, 0.9])
      {
        const pt = evEdgeTangentLine(context, { "edge" : e, "parameter" : t }).origin;
        if (evDistance(context, { "side0" : pt, "side1" : face }).distance > 0.05 * millimeter) { onFace = false; }
      }
      if (!onFace) { continue; }
      var s = "E";
      var ps = []; for (var k = 0; k <= 40; k += 1) { ps = append(ps, k / 40); }
      for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : ps })) { s = s ~ "|" ~ toString(tl.origin[0] / millimeter) ~ "," ~ toString(tl.origin[1] / millimeter) ~ "," ~ toString(tl.origin[2] / millimeter); }
      out = append(out, s);
    }
  }
  return out; }""" % (fid, fid)
r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
vals=re.findall(r'"value":\s*"([SE]\|[^"]*)"', json.dumps(r))
print(len(vals), sum(v.startswith("E") for v in vals))
if not vals: print(json.dumps(r)[:2000])
json.dump(vals, open("docs/explainers/gordon_surface/img/src/data/gordon1.json","w"))
