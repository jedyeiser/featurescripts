"""For faces that are flat to < 1 um but still typed B-spline: how far do their boundary edges (5 samples each) lie from the face's plane? 1 eval, read-only.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_flat_edges.py [rollbackBarIndex]"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "0dd2e23b1a37d950f29b0479")
BODY = os.environ.get("PROBE_BODY", "filleted_top_surf (deformed)")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "__BODY__")
            body = b;
    }
    const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
    for (var i = 0; i < size(faces); i += 1)
    {
        try silent
        {
            var params = [];
            for (var a = 0; a <= 4; a += 1)
                for (var k = 0; k <= 4; k += 1)
                    params = append(params, vector(a / 4, k / 4));
            const planes = evFaceTangentPlanes(context, { "face" : faces[i], "parameters" : params });
            var dev = 0 * meter;
            for (var pl in planes)
                dev = max(dev, abs(dot(pl.origin - planes[12].origin, planes[12].normal)));
            if (dev > 1e-6 * meter || evArea(context, { "entities" : faces[i] }) < 100 * millimeter ^ 2)
                continue;
            var edgeDev = 0 * meter;
            var n = 0;
            for (var e in evaluateQuery(context, qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE)))
            {
                n += 1;
                for (var l in evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.25, 0.5, 0.75, 1] }))
                    edgeDev = max(edgeDev, abs(dot(l.origin - planes[12].origin, planes[12].normal)));
            }
            out = append(out, "S face " ~ i ~ " " ~ toString(evSurfaceDefinition(context, { "face" : faces[i], "returnBSplinesAsOther" : true }).surfaceType) ~ " area "
                ~ roundToPrecision(evArea(context, { "entities" : faces[i] }) / (millimeter ^ 2), 1) ~ " interior dev " ~ roundToPrecision(dev / meter * 1e6, 2)
                ~ " um, " ~ n ~ " edges, boundary dev " ~ roundToPrecision(edgeDev / meter * 1e6, 2) ~ " um");
        }
    }
    return out;
}""".replace("__BODY__", BODY)
qp = {"rollbackBarIndex": sys.argv[1]} if len(sys.argv) > 1 else None
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params=qp)
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
