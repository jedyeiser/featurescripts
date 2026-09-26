import json, re, sys
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="2143812a99089658c704f0bc","4ea63c7ef14610f212917c42","0ee366a19b18c78097222cd0"
F=[(f["name"],f["featureId"]) for f in c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")["features"]]
print([n for n,_ in F])
def fid(p): return [i for n,i in F if n.startswith(p)][0]
out={}
for key in ["F1","F4","R1 ","R5 "]:
    script="""function(context is Context, queries) {
      var out = [];
      for (var e in evaluateQuery(context, qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE)))
      {
        const cd = evCurveDefinition(context, { "edge" : e });
        var s = "E|" ~ cd.curveType;
        for (var i = 0; i <= 80; i += 1)
        {
          const k = evEdgeCurvature(context, { "edge" : e, "parameter" : i / 80 });
          s = s ~ "|" ~ toString(k.frame.origin[0] / millimeter) ~ "," ~ toString(k.frame.origin[1] / millimeter) ~ "," ~ toString(k.curvature * millimeter);
        }
        out = append(out, s);
      }
      return out; }""" % fid(key)
    r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
    out[key.strip()]=re.findall(r'"value":\s*"(E\|[^"]*)"', json.dumps(r))
    print(key, len(out[key.strip()]))
json.dump(out, open("docs/explainers/curve_tools/img/src/data_recognize.json","w"))
