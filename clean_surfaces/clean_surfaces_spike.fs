FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * THROWAWAY spike (not a product feature): which native ops can remove sliver faces / merge faces on a sheet body?
 * Each variant runs on its own copy, prints stats, and the copies are deleted at the end.
 * try/catch is used here ONLY to read each op's failure message in a probe (project rule: not in shipped code).
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
        println("SPIKE source: " ~ stats(context, src));

        // V1 HEAL, V2 CAP, V3 VOID
        const names = ["V1_HEAL", "V2_CAP", "V3_VOID"];
        for (var i = 0; i < 3; i += 1)
        {
            const vid = id + names[i];
            opPattern(context, vid + "copy", { "entities" : src, "transforms" : [identityTransform()], "instanceNames" : [names[i]] });
            const body = qCreatedBy(vid + "copy", EntityType.BODY);
            const slivers = findSlivers(context, body);
            println(names[i] ~ " slivers found " ~ size(slivers));
            try
            {
                opDeleteFace(context, vid + "del", { "deleteFaces" : qUnion(slivers), "includeFillet" : false,
                            "capVoid" : i == 1, "leaveOpen" : i == 2 });
                println(names[i] ~ " OK: " ~ stats(context, body));
            }
            catch (e)
            {
                println(names[i] ~ " FAILED: " ~ e);
            }
        }

        // V4 opReplaceFace: each sliver replaced by its largest neighbour face
        opPattern(context, id + "V4copy", { "entities" : src, "transforms" : [identityTransform()], "instanceNames" : ["V4_REPLACE"] });
        const body4 = qCreatedBy(id + "V4copy", EntityType.BODY);
        const sl4 = findSlivers(context, body4);
        var okCount = 0;
        var failCount = 0;
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
                failCount += 1;
                continue;
            }
            try
            {
                opReplaceFace(context, id + "V4rep" + k, { "replaceFaces" : sl4[k], "templateFace" : tmpl, "oppositeSense" : false });
                okCount += 1;
            }
            catch (e)
            {
                failCount += 1;
                println("V4 sliver " ~ k ~ " FAILED: " ~ e);
            }
        }
        println("V4_REPLACE ok " ~ okCount ~ " failed " ~ failCount ~ ": " ~ stats(context, body4));

        // V5 opExtractSurface with redundancy removal (no sliver work): how many faces merge for free?
        const rts = [ExtractSurfaceRedundancyType.REMOVE_ALL_REDUNDANCY, ExtractSurfaceRedundancyType.REMOVE_ADDED_REDUNDANCY];
        for (var j = 0; j < 2; j += 1)
        {
            try
            {
                opExtractSurface(context, id + ("V5ex" ~ j), { "faces" : qOwnedByBody(src, EntityType.FACE), "tangentPropagation" : false,
                            "redundancyType" : rts[j] });
                println("V5_EXTRACT " ~ ["ALL", "ADDED"][j] ~ ": " ~ stats(context, qCreatedBy(id + ("V5ex" ~ j), EntityType.BODY)));
            }
            catch (e)
            {
                println("V5_EXTRACT " ~ ["ALL", "ADDED"][j] ~ " FAILED: " ~ e);
            }
        }

        opDeleteBodies(context, id + "cleanup", { "entities" : qUnion([qCreatedBy(id + "V1_HEALcopy", EntityType.BODY),
                            qCreatedBy(id + "V2_CAPcopy", EntityType.BODY), qCreatedBy(id + "V3_VOIDcopy", EntityType.BODY),
                            qCreatedBy(id + "V4copy", EntityType.BODY), qCreatedBy(id + "V5ex0", EntityType.BODY),
                            qCreatedBy(id + "V5ex1", EntityType.BODY)]) });
    });

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
