import json, os, re
from sync.core.client import OnshapeClient
c = OnshapeClient()
D, W = "08e8748f2ef24eea16072b75", "e24fbbae7712ef62dd256c89"
E = os.environ.get("STRESS_STUDIO", "e0595b3c9b7337c1b2d19b25")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "filleted_top_surf (deformed)")
            body = b;
    }
    const face = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))[34];
    const edges = evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE));
    out = append(out, "S face 34: " ~ size(edges) ~ " edges, bbox " ~ toString(evBox3d(context, { "topology" : face }).minCorner / millimeter) ~ " .. " ~ toString(evBox3d(context, { "topology" : face }).maxCorner / millimeter));
    for (var e in edges)
        out = append(out, "S   edge len " ~ roundToPrecision(evLength(context, { "entities" : e }) / millimeter, 2) ~ " mm, adjacent faces " ~ size(evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE))));
    var params = [];
    for (var i = 0; i <= 12; i += 1)
        for (var j = 0; j <= 12; j += 1)
            params = append(params, vector(i / 12, j / 12));
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : params });
    var maxTurn = 0 * degree;
    var worstAt = vector(0, 0);
    for (var i = 0; i <= 12; i += 1)
        for (var j = 0; j <= 12; j += 1)
        {
            const here = planes[i * 13 + j].normal;
            if (i < 12) { const a = angleBetween(here, planes[(i + 1) * 13 + j].normal); if (a > maxTurn) { maxTurn = a; worstAt = vector(i / 12, j / 12); } }
            if (j < 12) { const a = angleBetween(here, planes[i * 13 + j + 1].normal); if (a > maxTurn) { maxTurn = a; worstAt = vector(i / 12, j / 12); } }
        }
    out = append(out, "S largest normal turn between grid neighbours (1/12 of the parameter box): " ~ roundToPrecision(maxTurn / degree, 2) ~ " deg near uv " ~ toString(worstAt));
    var minR = 1e9 * meter;
    var minAt = vector(0, 0);
    try
    {
        const curv = evFaceCurvature(context, { "face" : face, "parameters" : params });
        for (var k = 0; k < size(curv); k += 1)
        {
            const kk = max(abs(curv[k].minCurvature), abs(curv[k].maxCurvature));
            if (kk > 0 / meter && 1 / kk < minR) { minR = 1 / kk; minAt = params[k]; }
        }
        out = append(out, "S smallest radius of curvature on the 13x13 grid: " ~ roundToPrecision(minR / millimeter, 3) ~ " mm near uv " ~ toString(minAt));
    }
    catch (err) { out = append(out, "S curvature: " ~ toString(err)); }
    // self-distance of a thin sheet: offset by tiny amounts
    for (var d in [0.001, 0.01, 0.05])
    {
        var ok = "ok";
        try { opExtractSurface(context, makeId("t" ~ d), { "faces" : face, "offset" : d * millimeter, "useFacesAroundToTrimOffset" : false }); }
        catch (err) { ok = "FAIL"; }
        out = append(out, "S   offset " ~ d ~ " mm alone: " ~ ok);
    }
    return out;
}"""
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
