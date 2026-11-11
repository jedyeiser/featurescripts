"""Can micro-face clusters be consumed IN THE SOURCE? Read-only (the eval's ops are discarded), 1 eval.
Repeats up to 8 rounds: find the 'micro' faces in the box (area < 0.05 mm2 or an edge < 10 um), take ONE connected group of them, sample points on its
faces, delete the group leaving the hole open, fill the hole from the sheet's new naked edges with those points as guide vertices (G1, then G0),
sew the patch in with COINCIDENT matches, and report faces / naked edges / deviation of the removed faces' samples from the patch.
  ONSHAPE_CALL_CAP=5 PYTHONPATH=. python devtools/onshape/probe_cluster_clean.py "<body>" xmin xmax ymin ymax [guides 1|0]   (env PROBE_STUDIO)"""
import json, os, re, sys
from sync.core.client import OnshapeClient
c = OnshapeClient()
D = os.environ.get("PROBE_DOC", "08e8748f2ef24eea16072b75")
W = os.environ.get("PROBE_WS", "e24fbbae7712ef62dd256c89")
E = os.environ.get("PROBE_STUDIO", "074a17ad5d8f7e3faa3346b2")
name, x0, x1, y0, y1 = sys.argv[1:6]
guides = sys.argv[6] if len(sys.argv) > 6 else "1"
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    var body = undefined;
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
    {
        var nm = "";
        try silent { nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }); }
        if (nm == "__NAME__")
            body = b;
    }
    if (body == undefined)
        return ["S body not found"];
    const inBox = function(p) { return p[0] >= __X0__ * millimeter && p[0] <= __X1__ * millimeter && p[1] >= __Y0__ * millimeter && p[1] <= __Y1__ * millimeter && p[1] < 65 * millimeter; };
    const nakedCount = function() { return size(evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(body, EntityType.EDGE), EdgeTopology.ONE_SIDED))); };
    out = append(out, "S start: " ~ size(evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))) ~ " faces, " ~ nakedCount() ~ " naked edges");
    for (var round = 0; round < 8; round += 1)
    {
        const faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
        var micro = [];
        for (var f in faces)
        {
            if (!inBox(evApproximateCentroid(context, { "entities" : f })))
                continue;
            var isMicro = evArea(context, { "entities" : f }) < 5e-8 * meter ^ 2;
            for (var e in evaluateQuery(context, qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE)))
                if (evLength(context, { "entities" : e }) < 1e-5 * meter)
                    isMicro = true;
            if (isMicro)
                micro = append(micro, f);
        }
        if (size(micro) == 0)
        {
            out = append(out, "S round " ~ round ~ ": no micro faces left");
            break;
        }
        // one connected group (faces that share an edge), grown from the first
        var group = [micro[0]];
        var grew = true;
        while (grew)
        {
            grew = false;
            for (var m in micro)
            {
                var already = false;
                for (var g in group)
                    if (g == m)
                        already = true;
                if (already)
                    continue;
                for (var g in group)
                    if (size(evaluateQuery(context, qIntersection([qAdjacent(g, AdjacencyType.EDGE, EntityType.EDGE), qAdjacent(m, AdjacencyType.EDGE, EntityType.EDGE)]))) > 0)
                    {
                        group = append(group, m);
                        grew = true;
                        break;
                    }
            }
        }
        var samples = [];
        var area0 = 0 * meter ^ 2;
        for (var g in group)
        {
            area0 += evArea(context, { "entities" : g });
            var params = [];
            for (var a = 0; a < 5; a += 1)
                for (var k = 0; k < 5; k += 1)
                    params = append(params, vector(0.1 + 0.2 * a, 0.1 + 0.2 * k));
            for (var pl in evFaceTangentPlanes(context, { "face" : g, "parameters" : params }))
                if (evDistance(context, { "side0" : pl.origin, "side1" : g }).distance < 1e-6 * meter)
                    samples = append(samples, pl.origin);
        }
        const faceCountBefore = size(faces);
        const nakedBefore = nakedCount();
        const tag = "r" ~ round;
        var note = "round " ~ round ~ ": group of " ~ size(group) ~ " micro faces (" ~ roundToPrecision(area0 / (millimeter ^ 2), 2) ~ " mm2, " ~ size(samples) ~ " sample points)";
        try
        {
            opDeleteFace(context, makeId(tag ~ "void"), { "deleteFaces" : qUnion(group), "includeFillet" : false, "capVoid" : false, "leaveOpen" : true });
        }
        catch (e) { out = append(out, "S " ~ note ~ ": delete FAILED " ~ toString(e)); break; }
        var boundary = [];
        for (var e in evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(body, EntityType.EDGE), EdgeTopology.ONE_SIDED)))
            if (inBox(evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin))
                boundary = append(boundary, e);
        note = note ~ "; hole edges " ~ size(boundary);
        var done = false;
        for (var mode in ["G1", "G0"])
        {
            if (done)
                break;
            var pointBodies = [];
            try
            {
                const fid = makeId(tag ~ "fill" ~ mode);
                var useGuides = qNothing();
                if (__GUIDES__ == 1)
                {
                    const step = max(1, floor(size(samples) / 25));
                    for (var s = 0; s < size(samples); s += step)
                    {
                        opPoint(context, makeId(tag ~ mode ~ "pt" ~ s), { "point" : samples[s] });
                        pointBodies = append(pointBodies, qCreatedBy(makeId(tag ~ mode ~ "pt" ~ s), EntityType.BODY));
                    }
                    if (size(pointBodies) > 0)
                        useGuides = qUnion(pointBodies);
                }
                opFillSurface(context, fid, { "edgesG0" : mode == "G0" ? qUnion(boundary) : qNothing(), "edgesG1" : mode == "G1" ? qUnion(boundary) : qNothing(), "edgesG2" : qNothing(), "guideVertices" : useGuides });
                const patchBody = qCreatedBy(fid, EntityType.BODY);
                const patchFace = qCreatedBy(fid, EntityType.FACE);
                var worst = 0 * meter;
                for (var p in samples)
                    worst = max(worst, evDistance(context, { "side0" : p, "side1" : patchFace }).distance);
                var matches = [];
                for (var pe in evaluateQuery(context, qOwnedByBody(patchBody, EntityType.EDGE)))
                {
                    const pm = evEdgeTangentLine(context, { "edge" : pe, "parameter" : 0.5 }).origin;
                    var best = undefined;
                    var bestD = 1e9 * meter;
                    for (var h in boundary)
                    {
                        const d = evDistance(context, { "side0" : pm, "side1" : h }).distance;
                        if (d < bestD) { bestD = d; best = h; }
                    }
                    if (best != undefined && bestD < 1e-4 * meter)
                        matches = append(matches, { "topology1" : best, "topology2" : pe, "matchType" : TopologyMatchType.COINCIDENT });
                }
                opBoolean(context, makeId(tag ~ "join" ~ mode), { "tools" : qUnion([body, patchBody]), "operationType" : BooleanOperationType.UNION, "makeSolid" : false,
                                                                 "allowSheets" : true, "eraseImprintedEdges" : true, "matches" : matches, "recomputeMatches" : true });
                if (size(pointBodies) > 0)
                    try silent { opDeleteBodies(context, makeId(tag ~ mode ~ "dropPts"), { "entities" : qUnion(pointBodies) }); }
                out = append(out, "S " ~ note ~ "; fill " ~ mode ~ " ok, worst deviation of the removed faces " ~ roundToPrecision(worst / meter * 1e6, 1) ~ " um, " ~ size(matches) ~ " matches; faces " ~ faceCountBefore ~ " -> "
                    ~ size(evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))) ~ ", naked edges " ~ nakedBefore ~ " (after the delete) -> " ~ nakedCount());
                done = true;
            }
            catch (e2) { note = note ~ "; fill/join " ~ mode ~ " FAILED " ~ toString(e2); }
        }
        if (!done)
        {
            out = append(out, "S " ~ note);
            break;
        }
    }
    out = append(out, "S end: " ~ size(evaluateQuery(context, qOwnedByBody(body, EntityType.FACE))) ~ " faces, " ~ nakedCount() ~ " naked edges");
    return out;
}""".replace("__NAME__", name).replace("__X0__", x0).replace("__X1__", x1).replace("__Y0__", y0).replace("__Y1__", y1).replace("__GUIDES__", guides)
r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT}, query_params={"rollbackBarIndex": "4"})
for n in r.get("notices") or []:
    if n.get("level") == "ERROR": print("[ERROR]", n.get("message"))
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("S "): print(v)
