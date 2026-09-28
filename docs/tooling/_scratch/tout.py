import time, json, re, sys
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="9732a1a905d6ac91d8990105","4b70b4378f844dff881b5282","093984ec4a1e35072e737852"
tmpl="""function(context is Context, queries) {
  var part = qNothing();
  for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
  {
    if (getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) == "RD 20FOU 28 178 4501") { part = b; }
  }
  const tools = qUnion([qBodyType(part, [BodyType.SOLID, BodyType.SHEET]), qFlattenedCompositeParts(part)]);
  const id = newId() + "t";
  %s
  return ["done " ~ size(evaluateQuery(context, qCreatedBy(id + "o", EntityType.EDGE)))]; }"""
prof='opPlane(context, id + "p", { "plane" : plane(vector(0, -0.2, 0) * meter, vector(0, -1, 0), vector(1, 0, 0)), "width" : 20 * meter, "height" : 20 * meter }); opCreateOutline(context, id + "o", { "tools" : tools, "target" : qCreatedBy(id + "p", EntityType.FACE) });'
cases={
 "profile outline + wires": prof + ' opExtractWires(context, id + "w", { "edges" : qLoopEdges(qCreatedBy(id + "o", EntityType.FACE)) });',
 "profile outline + 35 plane queries": prof + ' for (var k = 0; k < 35; k += 1) { evaluateQuery(context, qIntersectsPlane(qLoopEdges(qCreatedBy(id + "o", EntityType.FACE)), plane(vector(0.145 + k * 0.0435, 0, 0) * meter, vector(1, 0, 0)))); }',
}
_old={
 "baseline (find part only)": "",
 "plan outline": 'opPlane(context, id + "p", { "plane" : plane(vector(0, 0, 0.1) * meter, vector(0, 0, 1), vector(1, 0, 0)), "width" : 20 * meter, "height" : 20 * meter }); opCreateOutline(context, id + "o", { "tools" : tools, "target" : qCreatedBy(id + "p", EntityType.FACE) });',
 "profile outline": 'opPlane(context, id + "p", { "plane" : plane(vector(0, -0.2, 0) * meter, vector(0, -1, 0), vector(1, 0, 0)), "width" : 20 * meter, "height" : 20 * meter }); opCreateOutline(context, id + "o", { "tools" : tools, "target" : qCreatedBy(id + "p", EntityType.FACE) });',
}
for name, body in cases.items():
    t=time.time()
    r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": tmpl % body})
    print("%-26s %.1f s" % (name, time.time()-t), re.findall(r'"value":\s*"(done[^"]*)"', json.dumps(r)), [n.get("message","")[:80] for n in (r.get("notices") or [])][:2])
