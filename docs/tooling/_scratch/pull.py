import json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="d0950ed72a894fbe35c27d43","55351e2bc9e8b4ec141bf4b6","1fe953e0696c19de45504f25"
F={f["name"]:f["featureId"] for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]}
def fid(prefix): return [v for k,v in F.items() if k.startswith(prefix)][0]
script="""function(context is Context, queries) {
  var out = [];
  for (var nm in [["SRC", "%s"], ["OUT", "%s"]])
  {
    var k = 0;
    for (var f in evaluateQuery(context, qCreatedBy(makeId(nm[1]), EntityType.FACE)))
    {
      for (var i = 0; i <= 20; i += 1) { for (var j = 0; j <= 20; j += 1) {
        const pl = evFaceTangentPlane(context, { "face" : f, "parameter" : vector(i / 20, j / 20) });
        out = append(out, nm[0] ~ "|" ~ k ~ "|" ~ i ~ "|" ~ j ~ "|" ~ toString(pl.origin[0] / millimeter) ~ "," ~ toString(pl.origin[1] / millimeter) ~ "," ~ toString(pl.origin[2] / millimeter) ~ "|" ~ toString(evDistance(context, { "side0" : pl.origin, "side1" : qCreatedBy(makeId("%s"), EntityType.FACE) }).distance / millimeter));
      } }
      k += 1;
    }
  }
  return out; }""" % (F["P2 patch"], fid("P2 G1"), F["P2 patch"])
r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
v=re.findall(r'"value":\s*"((?:SRC|OUT)\|[^"]*)"', json.dumps(r)); print(len(v))
json.dump(v, open("docs/explainers/gordon_surface/img/src/data/pull_p2.json","w"))
