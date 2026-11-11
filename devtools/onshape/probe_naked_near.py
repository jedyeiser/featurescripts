"""Every naked edge of one body within R mm of a point: exact end points (mm), length (um), the face it belongs to. Read-only, 1 eval.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_naked_near.py <rollbackBarIndex> "<body name>" x y z [radius_mm]
  e.g. ... 4 "filleted_top_surf (deformed)" -596 53 -3 40"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "0dd2e23b1a37d950f29b0479")
rb, name, x, y, z = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5]
radius = sys.argv[6] if len(sys.argv) > 6 else "40"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    const centre = vector(__X__, __Y__, __Z__) * millimeter;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm != "__NAME__")
            continue;
        for (var e in evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(b, EntityType.EDGE), EdgeTopology.ONE_SIDED)))
        {
            const l = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.5, 1] });
            if (norm(l[1].origin - centre) > __R__ * millimeter)
                continue;
            const f = qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE);
            out = append(out, "S naked edge len " ~ roundToPrecision(evLength(context, { "entities" : e }) / millimeter * 1000, 1) ~ " um, from (" ~ roundToPrecision(l[0].origin[0] / millimeter, 4) ~ ", " ~ roundToPrecision(l[0].origin[1] / millimeter, 4) ~ ", " ~ roundToPrecision(l[0].origin[2] / millimeter, 4)
                ~ ") to (" ~ roundToPrecision(l[2].origin[0] / millimeter, 4) ~ ", " ~ roundToPrecision(l[2].origin[1] / millimeter, 4) ~ ", " ~ roundToPrecision(l[2].origin[2] / millimeter, 4) ~ "), face "
                ~ toString(evSurfaceDefinition(context, { "face" : f, "returnBSplinesAsOther" : true }).surfaceType) ~ " " ~ roundToPrecision(evArea(context, { "entities" : f }) / (millimeter ^ 2), 2) ~ " mm2");
        }
    }
    return out;
}""".replace("__X__", x).replace("__Y__", y).replace("__Z__", z).replace("__R__", radius).replace("__NAME__", name)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": rb})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
