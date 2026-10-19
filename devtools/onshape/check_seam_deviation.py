import json, os, re
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = "e0595b3c9b7337c1b2d19b25"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var sheets = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        const faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        if (size(faces) != 1) continue;
        const area = evArea(context, { "entities" : faces[0] }) / millimeter ^ 2;
        if (area < 0.5) continue;
        sheets = append(sheets, { "body" : b, "area" : area });
    }
    out = append(out, "S sheets " ~ size(sheets));
    var rows = [];
    for (var i = 0; i < size(sheets); i += 1)
        for (var j = i + 1; j < size(sheets); j += 1)
        {
            if (evDistance(context, { "side0" : sheets[i].body, "side1" : sheets[j].body }).distance > 1e-4 * meter) continue;
            // edges of i that lie on j: all sampled points within 1 mm of j's edges
            for (var e in evaluateQuery(context, qOwnedByBody(sheets[i].body, EntityType.EDGE)))
            {
                const other = qOwnedByBody(sheets[j].body, EntityType.EDGE);
                var worst = 0 * meter;
                var best = 1e9 * meter;
                var len = evLength(context, { "entities" : e });
                try silent
                {
                    for (var l in evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.25, 0.5, 0.75, 1] }))
                    {
                        const d = evDistance(context, { "side0" : l.origin, "side1" : other }).distance;
                        worst = max(worst, d);
                        best = min(best, d);
                    }
                }
                if (worst < 1e-3 * meter && len > 0.5 * millimeter && worst > 2e-6 * meter)
                {
                    const m = evVertexPoint(context, { "vertex" : evaluateQuery(context, qAdjacent(e, AdjacencyType.VERTEX, EntityType.VERTEX))[0] }) / millimeter;
                    rows = append(rows, { "w" : worst / meter * 1e6, "line" : "S seam edge " ~ roundToPrecision(len / millimeter, 1) ~ " mm near (" ~ roundToPrecision(m[0], 0) ~ "," ~ roundToPrecision(m[1], 0) ~ "," ~ roundToPrecision(m[2], 0) ~ "): max distance to neighbour's edge " ~ roundToPrecision(worst / meter * 1e6, 1) ~ " um; areas " ~ roundToPrecision(sheets[i].area, 1) ~ " / " ~ roundToPrecision(sheets[j].area, 1) });
                }
            }
        }
    rows = sort(rows, function(a, b) { return b.w - a.w; });
    for (var k = 0; k < min(size(rows), 25); k += 1) out = append(out, rows[k].line);
    return out;
}"""
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
