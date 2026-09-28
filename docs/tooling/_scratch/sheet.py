import time, json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="9732a1a905d6ac91d8990105","4b70b4378f844dff881b5282","093984ec4a1e35072e737852"
for name, nrm in [("2D_PERIPHERY","0, 0, 1"),("2D_PERIPHERY","0, -1, 0"),("TOP_SURFACE","0, 0, 1"),("SW_SHELF","0, 0, 1")]:
    s="""function(context is Context, queries) {
      var part = qNothing();
      for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
      { if (getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) == "%s") { part = b; } }
      const id = newId() + "t";
      const n = vector(%s);
      opPlane(context, id + "p", { "plane" : plane(vector(0, 0, 0) * meter + n * 0.3 * meter, n, vector(1, 0, 0)), "width" : 20 * meter, "height" : 20 * meter });
      opCreateOutline(context, id + "o", { "tools" : qBodyType(part, [BodyType.SOLID, BodyType.SHEET]), "target" : qCreatedBy(id + "p", EntityType.FACE) });
      return ["faces " ~ size(evaluateQuery(context, qCreatedBy(id + "o", EntityType.FACE))) ~ " edges " ~ size(evaluateQuery(context, qCreatedBy(id + "o", EntityType.EDGE)))]; }""" % (name, nrm)
    r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": s})
    print(name, nrm, re.findall(r'"value":\s*"(faces[^"]*)"', json.dumps(r)), [n.get("message","")[:120] for n in (r.get("notices") or [])][:2])
