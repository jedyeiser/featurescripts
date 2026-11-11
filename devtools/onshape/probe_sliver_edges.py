"""For each sliver (area < 0.05 mm2) of a body: its edges (length, ends), how many neighbours, and how far the two LONGEST edges are from each other
(max of the distance from 9 points of one to the other). If that is not about the sliver's width, the two longest edges are not its opposite sides.
Read-only, 1 eval.   PROBE_STUDIO=... python devtools/onshape/probe_sliver_edges.py "<body>" """
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "074a17ad5d8f7e3faa3346b2")
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
            if (area >= 5e-8 * meter ^ 2)
                continue;
            const edges = evaluateQuery(context, qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE));
            const lengths = mapArray(edges, function(e) { return evLength(context, { "entities" : e }) / meter; });
            const order = sort(range(0, size(edges) - 1), function(a, c) { return lengths[c] - lengths[a]; });
            var text = "S sliver face " ~ i ~ " area " ~ roundToPrecision(area / (millimeter ^ 2), 5) ~ " mm2, edges (um): ";
            for (var k in order)
                text = text ~ roundToPrecision(lengths[k] * 1e6, 1) ~ " ";
            if (size(edges) >= 2)
            {
                const e1 = edges[order[0]];
                const e2 = edges[order[1]];
                var worst = 0 * meter;
                var mid = 0 * meter;
                for (var t in [0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1])
                {
                    const p = evEdgeTangentLine(context, { "edge" : e1, "parameter" : t }).origin;
                    const d = evDistance(context, { "side0" : p, "side1" : e2 }).distance;
                    worst = max(worst, d);
                }
                text = text ~ "| longest two edges: max distance between them " ~ roundToPrecision(worst / meter * 1e6, 1) ~ " um";
                const n1 = size(evaluateQuery(context, qSubtraction(qAdjacent(e1, AdjacencyType.EDGE, EntityType.FACE), faces[i])));
                const n2 = size(evaluateQuery(context, qSubtraction(qAdjacent(e2, AdjacencyType.EDGE, EntityType.FACE), faces[i])));
                text = text ~ ", neighbours across them: " ~ n1 ~ " and " ~ n2;
            }
            out = append(out, text);
        }
    }
    return out;
}""".replace("__NAME__", sys.argv[1])
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": "4"})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
