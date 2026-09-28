import json, re
from sync.core.client import OnshapeClient
c=OnshapeClient(); D,W,E="73271cfcd708b3f5e3fc315c","fea83d30106dba0a4e066761","bb2cddb24faf53d9d043c38e"
for name, nrm, off in [("TOP_SURFACE","0, 0, 1",53.69),("TOP_SURFACE","0, -1, 0",200),("2D_PERIPHERY","0, 0, 1",25.01),("2D_PERIPHERY","0, -1, 0",200)]:
    s="""function(context is Context, queries) {
      var part = qNothing();
      for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
      { if (getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) == "%s") { part = b; } }
      const id = newId() + "t";
      const n = vector(%s);
      opPlane(context, id + "p", { "plane" : plane(n * %f * millimeter, n, vector(1, 0, 0)), "width" : 20 * meter, "height" : 20 * meter });
      opDropCurve(context, id + "d", { "tools" : qEdgeTopologyFilter(qOwnedByBody(part, EntityType.EDGE), EdgeTopology.LAMINAR),
                                        "targets" : qCreatedBy(id + "p", EntityType.FACE), "projectionType" : ProjectionType.NORMAL_TO_TARGET });
      const es = qCreatedBy(id + "d", EntityType.EDGE);
      const bb = evBox3d(context, { "topology" : es, "tight" : true });
      return ["edges " ~ size(evaluateQuery(context, es)) ~ " bodies " ~ size(evaluateQuery(context, qCreatedBy(id + "d", EntityType.BODY))) ~ " bbox " ~ toString(bb.minCorner / millimeter) ~ toString(bb.maxCorner / millimeter)]; }""" % (name, nrm, off)
    r=c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": s})
    print("%-13s n=(%s)" % (name, nrm), re.findall(r'"value":\s*"(edges[^"]*)"', json.dumps(r)), [x.get("message","")[:100] for x in (r.get("notices") or [])][:2])
