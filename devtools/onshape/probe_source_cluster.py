"""Small faces of a body near a region: index, type, area, edge count, shortest edge (um), centroid (mm). Read-only, 1 eval.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_source_cluster.py "<body>" xmin xmax ymin ymax maxArea_mm2   (env PROBE_STUDIO)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "074a17ad5d8f7e3faa3346b2")
name, x0, x1, y0, y1, amax = sys.argv[1:7]
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
            const area = evArea(context, { "entities" : faces[i] });
            if (area > __A__ * millimeter ^ 2)
                continue;
            const c = evApproximateCentroid(context, { "entities" : faces[i] });
            if (c[0] < __X0__ * millimeter || c[0] > __X1__ * millimeter || c[1] < __Y0__ * millimeter || c[1] > __Y1__ * millimeter)
                continue;
            var minLen = 1e9 * meter;
            const edges = evaluateQuery(context, qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE));
            for (var e in edges)
                minLen = min(minLen, evLength(context, { "entities" : e }));
            out = append(out, "S face " ~ i ~ " " ~ toString(evSurfaceDefinition(context, { "face" : faces[i], "returnBSplinesAsOther" : true }).surfaceType) ~ " area " ~ roundToPrecision(area / (millimeter ^ 2), 4) ~ " mm2, "
                ~ size(edges) ~ " edges, shortest " ~ roundToPrecision(minLen / millimeter * 1000, 1) ~ " um, centroid (" ~ roundToPrecision(c[0] / millimeter, 2) ~ ", " ~ roundToPrecision(c[1] / millimeter, 2) ~ ", " ~ roundToPrecision(c[2] / millimeter, 2) ~ ")");
        }
    }
    return out;
}""".replace("__NAME__", name).replace("__A__", amax).replace("__X0__", x0).replace("__X1__", x1).replace("__Y0__", y0).replace("__Y1__", y1)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)

