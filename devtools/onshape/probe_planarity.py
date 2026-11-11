"""Which faces of a studio's bodies are (nearly) planar, and what surface type does the kernel call them? Read-only, 1 eval.
For every face: surfaceType, area, and the largest distance of a 5x5 grid of its points from the plane through its centre
(normal = the face's tangent-plane normal at the centre). Prints the nearly planar faces (< 100 um) and a count per type.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_planarity.py [rollbackBarIndex]
  env PROBE_DOC / PROBE_WS / PROBE_STUDIO (default: the SWCNT_Tests studio in the curveMapping doc)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "0dd2e23b1a37d950f29b0479")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var typeCount = {};
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        const faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        out = append(out, "S body '" ~ nm ~ "' " ~ (size(evaluateQuery(context, qBodyType(b, BodyType.SOLID))) > 0 ? "solid" : "sheet") ~ ", " ~ size(faces) ~ " faces");
        for (var i = 0; i < size(faces); i += 1)
        {
            var kind = "?";
            try silent { kind = toString(evSurfaceDefinition(context, { "face" : faces[i], "returnBSplinesAsOther" : true }).surfaceType); }
            typeCount[kind] = (typeCount[kind] == undefined ? 0 : typeCount[kind]) + 1;
            var dev = undefined;
            try silent
            {
                var params = [];
                for (var a = 0; a <= 4; a += 1)
                    for (var k = 0; k <= 4; k += 1)
                        params = append(params, vector(a / 4, k / 4));
                const planes = evFaceTangentPlanes(context, { "face" : faces[i], "parameters" : params });
                const centre = planes[12];
                dev = 0 * meter;
                for (var pl in planes)
                    dev = max(dev, abs(dot(pl.origin - centre.origin, centre.normal)));
            }
            if (dev != undefined && dev < 100e-6 * meter)
                out = append(out, "S   face " ~ i ~ " " ~ kind ~ " area " ~ roundToPrecision(evArea(context, { "entities" : faces[i] }) / (millimeter ^ 2), 1) ~ " mm2, deviation from its plane " ~ roundToPrecision(dev / meter * 1e6, 2) ~ " um");
        }
    }
    out = append(out, "S types " ~ toString(typeCount));
    return out;
}"""
qp = {"rollbackBarIndex": sys.argv[1]} if len(sys.argv) > 1 else None
path = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript"
r = c.post(path, json_data={"script": SCRIPT}, query_params=qp)
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
