"""Cost model of the weld prototype's canonical-edge step on a body: faces, edges, edge lengths, and how many exact map evaluations canonicalEdgeCurve would spend
(samples = clamp(ceil(length / 1 mm) + 1, 8, 400) per edge of at least 0.05 mm). Read-only, 1 eval.
  PROBE_STUDIO=... PROBE_RB=2 python devtools/onshape/probe_edge_cost.py name1 [name2 ...]"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "ce67c6fa6edd49aba4637689")
names = sys.argv[1:]
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    const wanted = __NAMES__;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        out = append(out, "S BODY " ~ nm);
        if (indexOf(wanted, nm) < 0)
            continue;
        const faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        const edges = evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE));
        var total = 0 * meter;
        var samples = 0;
        var long = 0;
        var tiny = 0;
        var longest = 0 * meter;
        for (var e in edges)
        {
            const l = evLength(context, { "entities" : e });
            total += l;
            longest = max(longest, l);
            if (l < 5e-5 * meter)
            {
                tiny += 1;
                continue;
            }
            long += 1;
            samples += clamp(ceil(l / (1e-3 * meter)) + 1, 8, 400);
        }
        out = append(out, "S " ~ nm ~ ": faces " ~ size(faces) ~ ", edges " ~ size(edges) ~ " (" ~ long ~ " mapped, " ~ tiny ~ " under 0.05 mm), total length " ~ roundToPrecision(total / meter, 3) ~ " m, longest " ~ roundToPrecision(longest / meter, 3)
            ~ " m, canonical samples " ~ samples ~ " (each is one exact map evaluation)");
    }
    return out;
}""".replace("__NAMES__", json.dumps(names))
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": os.environ.get("PROBE_RB", "2")})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
