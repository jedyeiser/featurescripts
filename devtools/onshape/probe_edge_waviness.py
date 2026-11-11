"""How wavy are the edges? For every edge longer than 0.5 mm of the named body (within an x window): the largest distance of 25 points of the edge from the straight chord between its
ends (um). A straight edge reads ~0. Prints the edges above a threshold, worst first, and the count above it. Read-only, 1 eval.
  PROBE_STUDIO=... python devtools/onshape/probe_edge_waviness.py "<body>" xmin xmax threshold_um"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "074a17ad5d8f7e3faa3346b2")
name, x0, x1, thr = sys.argv[1:5]
SCRIPT = """function(context is Context, queries)
{
    var found = [];
    var total = 0;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm != "__NAME__")
            continue;
        for (var e in evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE)))
        {
            const len = evLength(context, { "entities" : e });
            if (len < 0.5 * millimeter)
                continue;
            var params = [];
            for (var k = 0; k <= 24; k += 1)
                params = append(params, k / 24);
            const l = evEdgeTangentLines(context, { "edge" : e, "parameters" : params });
            const a = l[0].origin;
            const z = l[24].origin;
            const m = l[12].origin;
            if (m[0] < __X0__ * millimeter || m[0] > __X1__ * millimeter)
                continue;
            const dir = normalize(z - a);
            var worst = 0 * meter;
            for (var q in l)
            {
                const v = q.origin - a;
                worst = max(worst, norm(v - dot(v, dir) * dir));
            }
            total += 1;
            if (worst > __T__ * 1e-6 * meter)
                found = append(found, { "w" : worst / meter * 1e6, "text" : "len " ~ roundToPrecision(len / millimeter, 2) ~ " mm, (" ~ roundToPrecision(a[0] / millimeter, 2) ~ ", " ~ roundToPrecision(a[1] / millimeter, 2) ~ ", " ~ roundToPrecision(a[2] / millimeter, 2) ~ ") -> (" ~ roundToPrecision(z[0] / millimeter, 2) ~ ", " ~ roundToPrecision(z[1] / millimeter, 2) ~ ", " ~ roundToPrecision(z[2] / millimeter, 2) ~ ")" });
        }
    }
    found = sort(found, function(p, q) { return q.w - p.w; });
    var out = ["S edges longer than 0.5 mm in the window: " ~ total ~ "; above __T__ um from their chord: " ~ size(found)];
    for (var i = 0; i < min(25, size(found)); i += 1)
        out = append(out, "S   " ~ roundToPrecision(found[i].w, 1) ~ " um  " ~ found[i].text);
    return out;
}""".replace("__NAME__", name).replace("__X0__", x0).replace("__X1__", x1).replace("__T__", thr)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": "4"})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
