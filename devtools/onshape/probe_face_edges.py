"""Every edge of the face(s) of a body whose area is within a window and whose centroid is near x: edge length (um), end points (mm), mid point, number of faces on
the edge, and the shortest distance from the edge's middle to any OTHER edge of the body (to see whether the neighbour edge is close or absurdly far). Read-only, 1 eval.
  PROBE_STUDIO=... python devtools/onshape/probe_face_edges.py "<body>" xmin xmax areaMin areaMax   (mm / mm2)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "074a17ad5d8f7e3faa3346b2")
name, x0, x1, a0, a1 = sys.argv[1:6]
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm != "__NAME__")
            continue;
        const faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        for (var i = 0; i < size(faces); i += 1)
        {
            const area = evArea(context, { "entities" : faces[i] }) / (millimeter ^ 2);
            if (area < __A0__ || area > __A1__)
                continue;
            const c = evApproximateCentroid(context, { "entities" : faces[i] });
            if (c[0] < __X0__ * millimeter || c[0] > __X1__ * millimeter)
                continue;
            out = append(out, "S FACE " ~ i ~ " area " ~ roundToPrecision(area, 4) ~ " mm2 centroid (" ~ roundToPrecision(c[0] / millimeter, 2) ~ ", " ~ roundToPrecision(c[1] / millimeter, 2) ~ ", " ~ roundToPrecision(c[2] / millimeter, 2) ~ ") type " ~ toString(evSurfaceDefinition(context, { "face" : faces[i], "returnBSplinesAsOther" : true }).surfaceType));
            const edges = evaluateQuery(context, qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE));
            for (var k = 0; k < size(edges); k += 1)
            {
                const l = evEdgeTangentLines(context, { "edge" : edges[k], "parameters" : [0, 0.5, 1] });
                const nf = size(evaluateQuery(context, qAdjacent(edges[k], AdjacencyType.EDGE, EntityType.FACE)));
                const others = qSubtraction(qOwnedByBody(b, EntityType.EDGE), edges[k]);
                const near = evDistance(context, { "side0" : l[1].origin, "side1" : others }).distance;
                out = append(out, "S   edge " ~ k ~ ": len " ~ roundToPrecision(evLength(context, { "entities" : edges[k] }) / millimeter, 4) ~ " mm, (" ~ roundToPrecision(l[0].origin[0] / millimeter, 3) ~ ", " ~ roundToPrecision(l[0].origin[1] / millimeter, 3) ~ ", " ~ roundToPrecision(l[0].origin[2] / millimeter, 3)
                    ~ ") -> (" ~ roundToPrecision(l[2].origin[0] / millimeter, 3) ~ ", " ~ roundToPrecision(l[2].origin[1] / millimeter, 3) ~ ", " ~ roundToPrecision(l[2].origin[2] / millimeter, 3) ~ "), faces on it " ~ nf ~ ", nearest other edge " ~ roundToPrecision(near / millimeter * 1000, 1) ~ " um");
            }
        }
    }
    return out;
}""".replace("__NAME__", name).replace("__X0__", x0).replace("__X1__", x1).replace("__A0__", a0).replace("__A1__", a1)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": "4"})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
