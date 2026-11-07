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
        println("SPIKE2 source: " ~ stats(context, src));

        // V4b: opReplaceFace, each sliver onto its largest neighbour, trying both senses
        opPattern(context, id + "V4copy", { "entities" : src, "transforms" : [identityTransform()], "instanceNames" : ["V4b_REPLACE"] });
        const body4 = qCreatedBy(id + "V4copy", EntityType.BODY);
        const sl4 = findSlivers(context, body4);
        var ok4 = 0;
        var fail4 = 0;
        for (var k = 0; k < size(sl4); k += 1)
        {
            const nb = evaluateQuery(context, qAdjacent(sl4[k], AdjacencyType.EDGE, EntityType.FACE));
            var tmpl = undefined;
            var tmplArea = 0 * meter ^ 2;
            for (var f in nb)
            {
                const a = evArea(context, { "entities" : f });
                if (a > tmplArea)
                {
                    tmplArea = a;
                    tmpl = f;
                }
            }
            if (tmpl == undefined)
            {
                fail4 += 1;
                continue;
            }
            const pt = centerOf(context, sl4[k]);
            const dot4 = dot(normalAt(context, sl4[k], pt), normalAt(context, tmpl, pt));
            var done = false;
            for (var sense in [dot4 < 0, dot4 >= 0])
            {
                if (done)
                {
                    break;
                }
                try
                {
                    opReplaceFace(context, id + ("V4rep" ~ k ~ (sense ? "t" : "f")), { "replaceFaces" : sl4[k], "templateFace" : tmpl, "oppositeSense" : sense });
                    done = true;
                    println("V4b sliver " ~ k ~ " ok (oppositeSense " ~ sense ~ ", dot " ~ roundToPrecision(dot4, 3) ~ ")");
                }
                catch (e)
                {
                }
            }
            if (done)
            {
                ok4 += 1;
            }
            else
            {
                fail4 += 1;
                println("V4b sliver " ~ k ~ " FAILED both senses (dot " ~ roundToPrecision(dot4, 3) ~ ")");
            }
        }
        println("V4b_REPLACE ok " ~ ok4 ~ " failed " ~ fail4 ~ ": " ~ stats(context, body4));

        // V6: delete one sliver at a time (VOID), fill the new hole, measure, sew
        opPattern(context, id + "V6copy", { "entities" : src, "transforms" : [identityTransform()], "instanceNames" : ["V6_FILL"] });
        const body6 = qCreatedBy(id + "V6copy", EntityType.BODY);
        for (var h = 0; h < 8; h += 1)
        {
            const slivers = findSlivers(context, body6);
            if (size(slivers) == 0)
            {
                break;
            }
            const nakedBefore = evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(body6, EntityType.EDGE), EdgeTopology.ONE_SIDED));
            opDeleteFace(context, id + ("V6del" ~ h), { "deleteFaces" : slivers[0], "includeFillet" : false, "capVoid" : false, "leaveOpen" : true });
            const holeEdges = qSubtraction(qEdgeTopologyFilter(qOwnedByBody(body6, EntityType.EDGE), EdgeTopology.ONE_SIDED), qUnion(nakedBefore));
            println("V6 hole " ~ h ~ ": new naked edges " ~ size(evaluateQuery(context, holeEdges)));
            var filled = false;
            for (var mode in ["G1", "G0"])
            {
                if (filled)
                {
                    break;
                }
                try
                {
                    opFillSurface(context, id + ("V6fill" ~ h ~ mode), mode == "G1" ? { "edgesG1" : holeEdges } : { "edgesG0" : holeEdges });
                    const fb = qCreatedBy(id + ("V6fill" ~ h ~ mode), EntityType.BODY);
                    println("V6 hole " ~ h ~ " fill " ~ mode ~ " ok, max dev to src " ~ roundToPrecision(maxDev(context, qOwnedByBody(fb, EntityType.FACE), src) / millimeter, 5) ~ " mm");
                    filled = true;
                    try
                    {
                        opBoolean(context, id + ("V6join" ~ h), { "tools" : qUnion([fb, body6]), "operationType" : BooleanOperationType.UNION });
                        println("V6 hole " ~ h ~ " sewn: " ~ stats(context, body6));
                    }
                    catch (e)
                    {
                        println("V6 hole " ~ h ~ " sew FAILED: " ~ e);
                    }
                }
                catch (e)
                {
                    println("V6 hole " ~ h ~ " fill " ~ mode ~ " FAILED: " ~ e);
                }
            }
        }
        println("V6_FILL end: " ~ stats(context, body6));

        opDeleteBodies(context, id + "cleanup", { "entities" : qUnion([body4, qCreatedBy(id + "V6copy", EntityType.BODY)]) });
    });

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
