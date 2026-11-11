"""Where can a set of sheet bodies leak? For every sheet body in the context at a rollback index: faces, naked edges, and every naked edge shorter than a limit (the
micro-leaks), plus the pairs of naked edges of DIFFERENT bodies that lie within a gap (unsewn contacts between bodies). Read-only, 1 eval.
  PROBE_STUDIO=... PROBE_RB=17 python devtools/onshape/probe_enclose_leak.py [short edge limit mm=2] [contact gap um=200]"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "ce67c6fa6edd49aba4637689")
short = sys.argv[1] if len(sys.argv) > 1 else "2"
gap = sys.argv[2] if len(sys.argv) > 2 else "200"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    const bodies = evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET));
    var names = [];
    var nakedAll = [];
    for (var b in bodies)
    {
        var nm = "?";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        names = append(names, nm);
        const naked = qEdgeTopologyFilter(qOwnedByBody(b, EntityType.EDGE), EdgeTopology.ONE_SIDED);
        nakedAll = append(nakedAll, naked);
        const area = evArea(context, { "entities" : qOwnedByBody(b, EntityType.FACE) }) / (millimeter ^ 2);
        const nakedList = evaluateQuery(context, naked);
        out = append(out, "S BODY " ~ nm ~ ": faces " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) ~ ", area " ~ roundToPrecision(area, 1) ~ " mm2, naked edges " ~ size(nakedList));
        for (var e in nakedList)
        {
            const len = evLength(context, { "entities" : e }) / millimeter;
            if (len < __SHORT__)
            {
                const l = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
                out = append(out, "S   short naked edge " ~ roundToPrecision(len * 1000, 1) ~ " um at (" ~ roundToPrecision(l[0] / millimeter, 2) ~ ", " ~ roundToPrecision(l[1] / millimeter, 2) ~ ", " ~ roundToPrecision(l[2] / millimeter, 2) ~ ")");
            }
        }
    }
    // naked edges of one body that touch (within the gap) a naked edge of ANOTHER body but are not sewn to it
    for (var i = 0; i < size(bodies); i += 1)
        for (var j = i + 1; j < size(bodies); j += 1)
        {
            var count = 0;
            var worst = 0 * meter;
            for (var e in evaluateQuery(context, nakedAll[i]))
            {
                const l = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
                var d = 1 * meter;
                try silent { d = evDistance(context, { "side0" : l, "side1" : nakedAll[j] }).distance; }
                if (d < __GAP__ * 1e-6 * meter)
                {
                    count += 1;
                    worst = max(worst, d);
                }
            }
            if (count > 0)
                out = append(out, "S CONTACT " ~ names[i] ~ " / " ~ names[j] ~ ": " ~ count ~ " naked edges of the first within " ~ __GAP__ ~ " um of the second, worst " ~ roundToPrecision(worst / meter * 1e6, 1) ~ " um");
        }
    return out;
}""".replace("__SHORT__", short).replace("__GAP__", gap)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": os.environ.get("PROBE_RB", "17")})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
