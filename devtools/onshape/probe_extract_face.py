"""Does opExtractSurface work on a face next to a naked edge of the deformed lid body? Tries the call shapes sewSeams uses. Read-only (the eval context is discarded), 1 eval.
  PROBE_STUDIO=... PROBE_RB=7 python devtools/onshape/probe_extract_face.py"""
import json, os, re
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "ce67c6fa6edd49aba4637689")
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (!(nm is string) || indexOf(nm, "weighted_top_surf (deformed)") < 0)
            continue;
        if (size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) < 50)
            continue;
        const naked = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(b, EntityType.EDGE), EdgeTopology.ONE_SIDED));
        out = append(out, "S naked edges " ~ size(naked));
        const faces = qAdjacent(naked[0], AdjacencyType.EDGE, EntityType.FACE);
        out = append(out, "S faces next to naked edge 0: " ~ size(evaluateQuery(context, faces)) ~ ", area mm2 " ~ roundToPrecision(evArea(context, { "entities" : faces }) / (millimeter ^ 2), 3));
        try
        {
            opExtractSurface(context, makeId("probeA"), { "faces" : faces });
            out = append(out, "S A (faces only): ok");
        }
        catch (e)
        {
            out = append(out, "S A (faces only): " ~ toString(e));
        }
        try
        {
            opExtractSurface(context, makeId("probeB"), { "faces" : faces, "tangentPropagation" : false });
            out = append(out, "S B (tangentPropagation false): ok");
        }
        catch (e)
        {
            out = append(out, "S B (tangentPropagation false): " ~ toString(e));
        }
        try
        {
            opExtractSurface(context, makeId("probeC"), { "faces" : qUnion([faces]) });
            out = append(out, "S C (qUnion): ok");
        }
        catch (e)
        {
            out = append(out, "S C (qUnion): " ~ toString(e));
        }
        var tried = 0;
        for (var i = 0; i < size(naked) && tried < 6; i += 1)
        {
            const len = evLength(context, { "entities" : naked[i] });
            if (len < 3e-4 * meter)
                continue;
            const mid = evEdgeTangentLine(context, { "edge" : naked[i], "parameter" : 0.5 }).origin;
            const others = qSubtraction(qUnion(naked), naked[i]);
            const near = evDistance(context, { "side0" : mid, "side1" : others });
            if (near.distance > 5e-5 * meter)
                continue;
            tried += 1;
            const fa = qAdjacent(naked[i], AdjacencyType.EDGE, EntityType.FACE);
            const fb = qAdjacent(qNthElement(others, near.sides[1].index), AdjacencyType.EDGE, EntityType.FACE);
            const aa = evArea(context, { "entities" : fa });
            const ab = evArea(context, { "entities" : fb });
            const small = aa <= ab ? fa : fb;
            try
            {
                opExtractSurface(context, makeId("probeD" ~ i), { "faces" : small, "tangentPropagation" : false });
                out = append(out, "S D edge " ~ i ~ " len " ~ roundToPrecision(len / millimeter, 2) ~ " mm, small face " ~ roundToPrecision(min(aa, ab) / (millimeter ^ 2), 3) ~ " mm2: ok");
            }
            catch (e)
            {
                out = append(out, "S D edge " ~ i ~ " len " ~ roundToPrecision(len / millimeter, 2) ~ " mm, small face " ~ roundToPrecision(min(aa, ab) / (millimeter ^ 2), 3) ~ " mm2: " ~ toString(e));
            }
        }
        break;
    }
    return out;
}"""
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": os.environ.get("PROBE_RB", "7")})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)

