"""Naked (one-sided) edges of sheet bodies whose name contains a substring: count per body, and for each naked edge its length (um), mid point (mm) and the distance (um)
to the nearest OTHER naked edge of the same body. Pairs that are close (< 50 um) are the unsewn seams. Read-only, 1 eval.
  PROBE_STUDIO=... PROBE_RB=7 python devtools/onshape/probe_naked_pairs.py <name substring> [max rows]"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "ce67c6fa6edd49aba4637689")
sub = sys.argv[1]
rows = sys.argv[2] if len(sys.argv) > 2 else "60"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (!(nm is string) || indexOf(nm, "__SUB__") < 0)
            continue;
        const faces = size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE)));
        const naked = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(b, EntityType.EDGE), EdgeTopology.ONE_SIDED));
        out = append(out, "S BODY " ~ nm ~ ": faces " ~ faces ~ ", naked edges " ~ size(naked));
        var shown = 0;
        for (var i = 0; i < size(naked) && shown < __ROWS__; i += 1)
        {
            const l = evEdgeTangentLines(context, { "edge" : naked[i], "parameters" : [0.5] });
            const others = qSubtraction(qEdgeTopologyFilter(qOwnedByBody(b, EntityType.EDGE), EdgeTopology.ONE_SIDED), naked[i]);
            var near = -1;
            try silent { near = evDistance(context, { "side0" : l[0].origin, "side1" : others }).distance / meter * 1e6; }
            const len = evLength(context, { "entities" : naked[i] }) / meter * 1e6;
            if (near >= 0 && near < 50)
            {
                shown += 1;
                out = append(out, "S   naked " ~ i ~ ": len " ~ roundToPrecision(len, 1) ~ " um at (" ~ roundToPrecision(l[0].origin[0] / millimeter, 2) ~ ", " ~ roundToPrecision(l[0].origin[1] / millimeter, 2) ~ ", " ~ roundToPrecision(l[0].origin[2] / millimeter, 2) ~ ") mm, nearest other naked edge " ~ roundToPrecision(near, 2) ~ " um");
            }
        }
    }
    return out;
}""".replace("__SUB__", sub).replace("__ROWS__", rows)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": os.environ.get("PROBE_RB", "7")})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)

