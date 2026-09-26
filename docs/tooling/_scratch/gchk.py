import json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="d0950ed72a894fbe35c27d43","55351e2bc9e8b4ec141bf4b6","1fe953e0696c19de45504f25"
F={f["name"]:f["featureId"] for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]}
def fid(prefix): return [v for k,v in F.items() if k.startswith(prefix)][0]
script="""function(context is Context, queries) {
  var out = [];
  for (var v in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.VERTEX))) { out = append(out, "HOLD|" ~ toString(evVertexPoint(context, { "vertex" : v }) / millimeter)); }
  for (var nm in [["G0", "%s"], ["G1", "%s"], ["OUT", "%s"]])
  {
    for (var e in evaluateQuery(context, qOwnedByBody(qCreatedBy(makeId(nm[1]), EntityType.BODY), EntityType.EDGE)))
    {
      out = append(out, nm[0] ~ "|len " ~ toString(evLength(context, { "entities" : e }) / millimeter));
    }
  }
  return out; }""" % (fid("T30 hold point"), fid("SC3 group 0"), fid("SC3 group 1"), fid("SC3 arcs"))
r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
print(re.findall(r'"value":\s*"((?:HOLD|G0|G1|OUT)\|[^"]*)"', json.dumps(r)))
