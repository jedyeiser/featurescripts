FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * THROWAWAY spike 2 (not a product feature).
 * Spike 1 (2026-11-06): opDeleteFace HEAL and CAP fail on sheet slivers (DIRECT_EDIT_DELETE_FACE_FAILED), VOID works
 * (193 -> 185 faces, naked 54 -> 76); opReplaceFace with oppositeSense=false worked on 3 of 8; opExtractSurface
 * REMOVE_ALL_REDUNDANCY 193 -> 191 faces for free.
 * Spike 2: V4b opReplaceFace with the right oppositeSense (try false, then true); V6 delete one sliver (VOID) then
 * opFillSurface the new hole (G1, then G0), measure deviation, then try to sew it back.
 * try/catch is used ONLY to read each op's failure in a probe (project rule: not in shipped code).
 */
const SLIVER_MM = 0.05;

annotation { "Feature Type Name" : "Clean surfaces spike" }
export const cleanSurfacesSpike = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
    }
    {
        var src = undefined;
        var best = 0;
        for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)))
        {
            const n = size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE)));
            if (n > best)
            {
                best = n;
                src = b;
            }
        }
        println("SPIKE3 source: " ~ stats(context, src));
        opPattern(context, id + "probe", { "entities" : src, "transforms" : [identityTransform()], "instanceNames" : ["probe"] });
        const nSliv = size(findSlivers(context, qCreatedBy(id + "probe", EntityType.BODY)));
        opDeleteBodies(context, id + "probeDel", { "entities" : qCreatedBy(id + "probe", EntityType.BODY) });
        println("slivers " ~ nSliv);

        // spike 4: delete the sliver TOGETHER WITH its neighbour faces, fill the bigger hole, sew, measure.
        for (var k = 0; k < nSliv; k += 1)
        {
            const fid = id + ("c" ~ k);
            opPattern(context, fid, { "entities" : src, "transforms" : [identityTransform()], "instanceNames" : ["C" ~ k] });
            const fb = qCreatedBy(fid, EntityType.BODY);
            const sf = findSlivers(context, fb)[k];
            const info = sliverInfo(context, sf);
            const nbs = evaluateQuery(context, qAdjacent(sf, AdjacencyType.EDGE, EntityType.FACE));
            const region = qUnion([sf, qUnion(nbs)]);
            const pts = regionPoints(context, region);
            const nakedBefore = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(fb, EntityType.EDGE), EdgeTopology.ONE_SIDED));
            const areaBefore = evArea(context, { "entities" : region }) / millimeter ^ 2;
            opDeleteFace(context, fid + "del", { "deleteFaces" : region, "includeFillet" : false, "capVoid" : false, "leaveOpen" : true });
            const hole = qSubtraction(qEdgeTopologyFilter(qOwnedByBody(fb, EntityType.EDGE), EdgeTopology.ONE_SIDED), qUnion(nakedBefore));
            var res = "region " ~ (size(nbs) + 1) ~ " faces A " ~ roundToPrecision(areaBefore, 2) ~ "; hole edges " ~ size(evaluateQuery(context, hole));
            var filled = false;
            for (var mode in ["G1", "G0"])
            {
                if (filled)
                {
                    break;
                }
                try
                {
                    opFillSurface(context, fid + ("fill" ~ mode), mode == "G1" ? { "edgesG1" : hole } : { "edgesG0" : hole });
                    const fill = qCreatedBy(fid + ("fill" ~ mode), EntityType.BODY);
                    filled = true;
                    res = res ~ "; fill " ~ mode ~ " OK, " ~ size(pts) ~ " pts dev " ~ roundToPrecision(maxDist(context, pts, fill) / millimeter, 4) ~ " mm";
                    try
                    {
                        opBoolean(context, fid + "join", { "tools" : qUnion([fill, fb]), "operationType" : BooleanOperationType.UNION });
                        res = res ~ "; sewn: " ~ stats(context, qBodyType(qCreatedBy(fid, EntityType.BODY), BodyType.SHEET));
                    }
                    catch (e)
                    {
                        res = res ~ "; sew FAILED " ~ e.message;
                    }
                }
                catch (e)
                {
                    res = res ~ "; fill " ~ mode ~ " " ~ e.message;
                }
            }
            say("S" ~ k ~ " " ~ info ~ " | CLUSTER " ~ res);
            opDeleteBodies(context, fid + "cleanup", { "entities" : qCreatedBy(fid, EntityType.BODY) });
        }
    });

/** Grid points inside every face of the region (5 x 5 UV, undefined outside the trim dropped), plus 3 points per edge. */
function regionPoints(context is Context, region is Query) returns array
{
    var params = [];
    for (var i = 1; i < 6; i += 1)
    {
        for (var j = 1; j < 6; j += 1)
        {
            params = append(params, vector(i / 6, j / 6));
        }
    }
    var pts = [];
    for (var f in evaluateQuery(context, region))
    {
        for (var pl in evFaceTangentPlanes(context, { "face" : f, "parameters" : params, "returnUndefinedOutsideFace" : true }))
        {
            if (pl != undefined)
            {
                pts = append(pts, pl.origin);
            }
        }
    }
    return pts;
}
/** println in 60-character pieces (the notices monitor truncates long console lines). */
function say(text is string)
{
    var s = text;
    while (length(s) > 0)
    {
        const n = min(60, length(s));
        println("  ." ~ substring(s, 0, n));
        s = substring(s, n);
    }
}

/** One line about a face:
 width, area, perimeter, whether any of its edges is naked, surface type. */
function sliverInfo(context is Context, face is Query) returns string
{
    const area = evArea(context, { "entities" : face }) / millimeter ^ 2;
    const edges = qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE);
    const per = evLength(context, { "entities" : edges }) / millimeter;
    const naked = size(evaluateQuery(context, qEdgeTopologyFilter(edges, EdgeTopology.ONE_SIDED)));
    return "w " ~ roundToPrecision(2 * area / per, 4) ~ " A " ~ roundToPrecision(area, 4) ~ " P " ~ roundToPrecision(per, 2) ~ " nakedEdges " ~ naked
        ~ " edges " ~ size(evaluateQuery(context, edges)) ~ " " ~ surfType(context, face);
}

function surfType(context is Context, face is Query) returns string
{
    return "" ~ evSurfaceDefinition(context, { "face" : face, "returnBSplinesAsOther" : true }).surfaceType;
}

/** Points on the face's own edges (3 per edge): where the face was, so their distance to the result is the movement. */
function edgePoints(context is Context, face is Query) returns array
{
    var pts = [];
    for (var e in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.5, 1] }))
        {
            pts = append(pts, tl.origin);
        }
    }
    return pts;
}

function maxDist(context is Context, pts is array, target is Query) returns ValueWithUnits
{
    var worst = 0 * meter;
    for (var p in pts)
    {
        const d = evDistance(context, { "side0" : p, "side1" : target }).distance;
        if (d > worst)
        {
            worst = d;
        }
    }
    return worst;
}
function centerOf(context is Context, face is Query) returns Vector
{
    const bb = evBox3d(context, { "topology" : face });
    return (bb.minCorner + bb.maxCorner) / 2;
}

function normalAt(context is Context, face is Query, pt is Vector) returns Vector
{
    const d = evDistance(context, { "side0" : face, "side1" : pt });
    return evFaceTangentPlane(context, { "face" : face, "parameter" : d.sides[0].parameter }).normal;
}

/** Largest distance from a 5 x 5 grid of points of the faces to the source body (sampled, not a proof). */
function maxDev(context is Context, faces is Query, srcBody is Query) returns ValueWithUnits
{
    var worst = 0 * meter;
    var params = [];
    for (var i = 1; i < 6; i += 1)
    {
        for (var j = 1; j < 6; j += 1)
        {
            params = append(params, vector(i / 6, j / 6));
        }
    }
    for (var f in evaluateQuery(context, faces))
    {
        const planes = evFaceTangentPlanes(context, { "face" : f, "parameters" : params, "returnUndefinedOutsideFace" : true });
        for (var p in planes)
        {
            if (p == undefined)
            {
                continue;
            }
            const d = evDistance(context, { "side0" : p.origin, "side1" : srcBody }).distance;
            if (d > worst)
            {
                worst = d;
            }
        }
    }
    return worst;
}

function findSlivers(context is Context, body is Query) returns array
{
    var out = [];
    for (var f in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)))
    {
        const area = evArea(context, { "entities" : f }) / millimeter ^ 2;
        const per = evLength(context, { "entities" : qAdjacent(f, AdjacencyType.EDGE, EntityType.EDGE) }) / millimeter;
        if (2 * area / max(per, 1e-12) < SLIVER_MM)
        {
            out = append(out, f);
        }
    }
    return out;
}

function stats(context is Context, body is Query) returns string
{
    const faces = qOwnedByBody(body, EntityType.FACE);
    const edges = qOwnedByBody(body, EntityType.EDGE);
    const naked = size(evaluateQuery(context, qEdgeTopologyFilter(edges, EdgeTopology.ONE_SIDED)));
    return "faces " ~ size(evaluateQuery(context, faces)) ~ " edges " ~ size(evaluateQuery(context, edges)) ~ " naked " ~ naked
        ~ " area " ~ roundToPrecision(evArea(context, { "entities" : faces }) / millimeter ^ 2, 2) ~ " mm2";
}
