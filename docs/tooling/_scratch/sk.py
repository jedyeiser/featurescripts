import json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="9732a1a905d6ac91d8990105","4b70b4378f844dff881b5282","093984ec4a1e35072e737852"
r=json.load(open("docs/tooling/_scratch/parts_features.json"))
F={f["name"]:f["featureId"] for f in r["features"]}
for name in ("Part_Buyoff_Sketch","Base_Sketch","BF_Sketch","Core_Sketch","SW_Sketch"):
    script="""function(context is Context, queries) {
      var out = [];
      const pl = evOwnerSketchPlane(context, { "entity" : qNthElement(qCreatedBy(makeId("%s"), EntityType.EDGE), 0) });
      out = append(out, "PLANE|" ~ toString(pl.origin / millimeter) ~ "|" ~ toString(pl.normal));
      for (var e in evaluateQuery(context, qCreatedBy(makeId("%s"), EntityType.EDGE)))
      {
        const a = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0 }).origin;
        const b = evEdgeTangentLine(context, { "edge" : e, "parameter" : 1 }).origin;
        const cd = evCurveDefinition(context, { "edge" : e });
        out = append(out, "E|" ~ cd.curveType ~ "|" ~ toString(a / millimeter) ~ "|" ~ toString(b / millimeter));
      }
      return out; }""" % (F[name], F[name])
    res=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": script})
    vals=re.findall(r'"value":\s*"((?:PLANE|E)\|[^"]*)"', json.dumps(res))
    print("==",name, len(vals)-1 if vals else 0)
    for v in vals[:12]: print("  ",v[:150])
    if not vals: print(json.dumps(res.get("notices"))[:400])
