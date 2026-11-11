"""Why will a surface not enclose? For each sheet body with >= 4 faces: its naked edges (length, midpoint) and the pairs of adjacent faces that are
coplanar (tangent planes at the shared edge midpoint within 0.05 deg and 20 um apart) but still separate faces. Read-only, 1 eval.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_enclose.py [rollbackBarIndex]   (default: end of the tree)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "0dd2e23b1a37d950f29b0479")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        const faces = evaluateQuery(context, qOwnedByBody(b, EntityType.FACE));
        if (size(faces) < 4)
            continue;
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        var naked = [];
        var split = [];
        for (var e in evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE)))
        {
            const adj = evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE));
            const mid = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin;
            const len = evLength(context, { "entities" : e });
            if (size(adj) == 1)
                naked = append(naked, "len " ~ roundToPrecision(len / millimeter, 3) ~ " mm at (" ~ roundToPrecision(mid[0] / millimeter, 1) ~ ", " ~ roundToPrecision(mid[1] / millimeter, 1) ~ ", " ~ roundToPrecision(mid[2] / millimeter, 1) ~ ")");
            else if (size(adj) == 2)
            {
                try silent
                {
                    const pa = evFaceTangentPlane(context, { "face" : adj[0], "parameter" : evDistance(context, { "side0" : mid, "side1" : adj[0] }).sides[1].parameter });
                    const pb = evFaceTangentPlane(context, { "face" : adj[1], "parameter" : evDistance(context, { "side0" : mid, "side1" : adj[1] }).sides[1].parameter });
                    const angle = acos(min(1, abs(dot(pa.normal, pb.normal)))) / degree;
                    if (angle < 0.05 && len > 0.5 * millimeter)
                    {
                        const ta = toString(evSurfaceDefinition(context, { "face" : adj[0], "returnBSplinesAsOther" : true }).surfaceType);
                        const tb = toString(evSurfaceDefinition(context, { "face" : adj[1], "returnBSplinesAsOther" : true }).surfaceType);
                        split = append(split, "edge " ~ roundToPrecision(len / millimeter, 1) ~ " mm at (" ~ roundToPrecision(mid[0] / millimeter, 0) ~ ", " ~ roundToPrecision(mid[1] / millimeter, 0) ~ ", " ~ roundToPrecision(mid[2] / millimeter, 0) ~ ") between "
                            ~ ta ~ " " ~ roundToPrecision(evArea(context, { "entities" : adj[0] }) / (millimeter ^ 2), 0) ~ " mm2 and " ~ tb ~ " " ~ roundToPrecision(evArea(context, { "entities" : adj[1] }) / (millimeter ^ 2), 0) ~ " mm2, angle " ~ roundToPrecision(angle, 4) ~ " deg");
                    }
                }
            }
        }
        out = append(out, "S body '" ~ nm ~ "' " ~ size(faces) ~ " faces, naked edges " ~ size(naked) ~ ", unmerged tangent-flat seams " ~ size(split));
        for (var i = 0; i < min(12, size(naked)); i += 1)
            out = append(out, "S   naked " ~ naked[i]);
        for (var i = 0; i < min(12, size(split)); i += 1)
            out = append(out, "S   flat seam " ~ split[i]);
    }
    return out;
}"""
qp = {"rollbackBarIndex": sys.argv[1]} if len(sys.argv) > 1 else None
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params=qp)
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
