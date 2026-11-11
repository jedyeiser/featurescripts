"""Naked (one-sided) edges and faces within a radius of a point, on sheet bodies whose name contains a substring. For each naked edge: length, end points, and the
distance to the nearest OTHER naked edge. Read-only, 1 eval.
  PROBE_STUDIO=... PROBE_RB=7 python devtools/onshape/probe_naked_region.py <name substring> x y z radius   (mm)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "ce67c6fa6edd49aba4637689")
sub, x, y, z, rad = sys.argv[1:6]
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    const centre = vector(__X__, __Y__, __Z__) * millimeter;
    const radius = __R__ * millimeter;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (!(nm is string) || indexOf(nm, "__SUB__") < 0)
            continue;
        const naked = qEdgeTopologyFilter(qOwnedByBody(b, EntityType.EDGE), EdgeTopology.ONE_SIDED);
        const nakedList = evaluateQuery(context, naked);
        out = append(out, "S BODY " ~ nm ~ ": faces " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) ~ ", naked edges " ~ size(nakedList));
        for (var i = 0; i < size(nakedList); i += 1)
        {
            const d = evDistance(context, { "side0" : centre, "side1" : nakedList[i] }).distance;
            if (d > radius)
                continue;
            const l = evEdgeTangentLines(context, { "edge" : nakedList[i], "parameters" : [0, 0.5, 1] });
            var near = -1;
            try silent { near = evDistance(context, { "side0" : l[1].origin, "side1" : qSubtraction(naked, nakedList[i]) }).distance / meter * 1e6; }
            out = append(out, "S   naked " ~ i ~ ": len " ~ roundToPrecision(evLength(context, { "entities" : nakedList[i] }) / millimeter, 4) ~ " mm, (" ~ roundToPrecision(l[0].origin[0] / millimeter, 3) ~ ", " ~ roundToPrecision(l[0].origin[1] / millimeter, 3) ~ ", " ~ roundToPrecision(l[0].origin[2] / millimeter, 3)
                ~ ") -> (" ~ roundToPrecision(l[2].origin[0] / millimeter, 3) ~ ", " ~ roundToPrecision(l[2].origin[1] / millimeter, 3) ~ ", " ~ roundToPrecision(l[2].origin[2] / millimeter, 3) ~ "), nearest other naked edge " ~ roundToPrecision(near, 1) ~ " um");
        }
        const faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        for (var i = 0; i < size(faces); i += 1)
        {
            const d = evDistance(context, { "side0" : centre, "side1" : faces[i] }).distance;
            if (d > radius)
                continue;
            const c = evApproximateCentroid(context, { "entities" : faces[i] });
            out = append(out, "S   face " ~ i ~ ": area " ~ roundToPrecision(evArea(context, { "entities" : faces[i] }) / (millimeter ^ 2), 3) ~ " mm2, centroid (" ~ roundToPrecision(c[0] / millimeter, 2) ~ ", " ~ roundToPrecision(c[1] / millimeter, 2) ~ ", " ~ roundToPrecision(c[2] / millimeter, 2) ~ "), " ~ size(evaluateQuery(context, qAdjacent(faces[i], AdjacencyType.EDGE, EntityType.EDGE))) ~ " edges, distance " ~ roundToPrecision(d / millimeter, 3) ~ " mm");
        }
    }
    return out;
}""".replace("__SUB__", sub).replace("__X__", x).replace("__Y__", y).replace("__Z__", z).replace("__R__", rad)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": os.environ.get("PROBE_RB", "7")})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
