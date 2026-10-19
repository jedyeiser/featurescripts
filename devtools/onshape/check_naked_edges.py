import json, os, re
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = "e0595b3c9b7337c1b2d19b25"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var bodies = [];
    for (var b in evaluateQuery(context, qCreatedBy(makeId("F0MSpNNTHSSzAXv_0"), EntityType.BODY)))
        if (size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) == 37 && size(evaluateQuery(context, qBodyType(b, BodyType.SHEET))) == 1)
            bodies = append(bodies, ["SRC", b]);
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "filleted_top_surf (deformed)") bodies = append(bodies, ["DEF", b]);
    }
    for (var pr in bodies)
    {
        var rows = [];
        for (var e in evaluateQuery(context, qOwnedByBody(pr[1], EntityType.EDGE)))
        {
            if (size(evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE))) != 1) continue;
            var m = vector(0, 0, 0) * meter;
            var note = "";
            try silent { m = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin; }
            catch { note = "NOTANGENT"; try silent { m = evVertexPoint(context, { "vertex" : evaluateQuery(context, qAdjacent(e, AdjacencyType.VERTEX, EntityType.VERTEX))[0] }); } }
            m = m / millimeter;
            rows = append(rows, { "x" : m[0], "line" : note ~ roundToPrecision(evLength(context, { "entities" : e }) / millimeter, 2) ~ "@(" ~ roundToPrecision(m[0], 0) ~ "," ~ roundToPrecision(m[1], 0) ~ "," ~ roundToPrecision(m[2], 0) ~ ")" });
        }
        rows = sort(rows, function(a, b) { return a.x - b.x; });
        var line = "";
        for (var r in rows) line = line ~ r.line ~ " ";
        out = append(out, "S " ~ pr[0] ~ " naked " ~ size(rows) ~ ": " ~ line);
    }
    return out;
}"""
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
