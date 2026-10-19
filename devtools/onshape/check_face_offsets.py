"""Which faces of a deformed body break an offset, and are any normals flipped across an edge.
  ONSHAPE_CALL_CAP=3 PYTHONPATH=. python devtools/onshape/check_face_offsets.py [name suffix]  (1 eval)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("STRESS_STUDIO", "e0595b3c9b7337c1b2d19b25")
TARGET = sys.argv[1] if len(sys.argv) > 1 else "filleted_top_surf (deformed)"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "%s")
            body = b;
    }
    const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
    var flips = 0;
    var flipNote = [];
    for (var e in evaluateQuery(context, qOwnedByBody(body, EntityType.EDGE)))
    {
        const adj = evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE));
        if (size(adj) != 2) continue;
        const l = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 });
        const nA = evFaceTangentPlane(context, { "face" : adj[0], "parameter" : evDistance(context, { "side0" : l.origin, "side1" : adj[0] }).sides[1].parameter }).normal;
        const nB = evFaceTangentPlane(context, { "face" : adj[1], "parameter" : evDistance(context, { "side0" : l.origin, "side1" : adj[1] }).sides[1].parameter }).normal;
        if (dot(nA, nB) < 0)
        {
            flips += 1;
            const m = l.origin / millimeter;
            flipNote = append(flipNote, "(" ~ roundToPrecision(m[0], 0) ~ "," ~ roundToPrecision(m[1], 0) ~ "," ~ roundToPrecision(m[2], 0) ~ ")");
        }
    }
    out = append(out, "S flipped-normal edges: " ~ flips ~ " " ~ toString(flipNote));
    var bad = [];
    for (var i = 0; i < size(faces); i += 1)
    {
        var ok = true;
        try { opExtractSurface(context, makeId("f" ~ i), { "faces" : faces[i], "offset" : 0.2 * millimeter, "useFacesAroundToTrimOffset" : false }); }
        catch (err) { ok = false; }
        if (!ok)
        {
            const bx = evBox3d(context, { "topology" : faces[i] });
            const m = (bx.minCorner + bx.maxCorner) / 2 / millimeter;
            bad = append(bad, "face " ~ i ~ " area " ~ roundToPrecision(evArea(context, { "entities" : faces[i] }) / millimeter ^ 2, 1) ~ " mm2 at (" ~ roundToPrecision(m[0], 0) ~ "," ~ roundToPrecision(m[1], 0) ~ "," ~ roundToPrecision(m[2], 0) ~ ") " ~ toString(evSurfaceDefinition(context, { "face" : faces[i] }).surfaceType));
        }
    }
    out = append(out, "S faces that fail to offset alone: " ~ size(bad) ~ " of " ~ size(faces));
    for (var b in bad) out = append(out, "S   " ~ b);
    return out;
}""" % TARGET
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
