import json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="9732a1a905d6ac91d8990105","4b70b4378f844dff881b5282","093984ec4a1e35072e737852"
for name, nrm, thick in [("TOP_SURFACE","0, 0, 1",False),("TOP_SURFACE","0, 0, 1",True),("2D_PERIPHERY","0, 0, 1",True),("SW_SHELF","0, 0, 1",False),("SW_SHELF","0, 0, 1",True)]:
    s="""function(context is Context, queries) {
      var part = qNothing();
      for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
      { if (getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) == "%s") { part = b; } }
      const id = newId() + "t";
      var tools = part;
      if (%s)
      {
        opThicken(context, id + "th", { "entities" : qOwnedByBody(part, EntityType.FACE), "thickness1" : 0.01 * millimeter, "thickness2" : 0 * meter });
        tools = qCreatedBy(id + "th", EntityType.BODY);
      }
      const n = vector(%s);
      opPlane(context, id + "p", { "plane" : plane(n * 0.3 * meter, n, vector(1, 0, 0)), "width" : 20 * meter, "height" : 20 * meter });
      opCreateOutline(context, id + "o", { "tools" : tools, "target" : qCreatedBy(id + "p", EntityType.FACE) });
      const f = qCreatedBy(id + "o", EntityType.FACE);
      const bb = evBox3d(context, { "topology" : f, "tight" : true });
      return ["faces " ~ size(evaluateQuery(context, f)) ~ " edges " ~ size(evaluateQuery(context, qCreatedBy(id + "o", EntityType.EDGE))) ~ " bbox " ~ toString(bb.minCorner / millimeter) ~ toString(bb.maxCorner / millimeter)]; }""" % (name, "true" if thick else "false", nrm)
    r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": s})
    print("%-13s thick=%-5s" % (name, thick), re.findall(r'"value":\s*"(faces[^"]*)"', json.dumps(r)), [n.get("message","")[:100] for n in (r.get("notices") or [])][:2])
