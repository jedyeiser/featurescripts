FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: extract_variables_utils.fs
import(path : "a4dcd70ce9ceec588536fb0c", version : "");

/**
 * Variable tools tests: builds its own fixtures and runs Extract variables entry types
 * (resolveEntry) on them against a hand-made source table, measuring each result. PASS /
 * FAIL lines go to the console; the feature shows a warning when anything failed.
 */
annotation { "Feature Type Name" : "Variable tools tests", "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const variableToolsTests = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Region entries", "Default" : true }
        definition.runRegion is boolean;
    }
    {
        var results = [];
        if (definition.runRegion)
        {
            results = concatenateArrays([results, regionTests(context, id + "region")]);
        }

        var failed = [];
        for (var r in results)
        {
            println((r.ok ? "PASS " : "FAIL ") ~ r.name ~ " -- " ~ r.detail);
            if (!r.ok)
            {
                failed = append(failed, r.name);
            }
        }
        if (size(failed) > 0)
        {
            reportFeatureWarning(context, id, size(failed) ~ " of " ~ size(results) ~ " failed: " ~ join(failed, ", "));
        }
        else
        {
            reportFeatureInfo(context, id, "All " ~ size(results) ~ " passed.");
        }
    }, { "runRegion" : true });

function pt(x is number, y is number, z is number) returns Vector
{
    return vector(x, y, z) * millimeter;
}

function result(name is string, ok is boolean, detail is string) returns map
{
    return { "name" : name, "ok" : ok, "detail" : detail };
}

/** A source table as readSources builds it, from { key : query }. */
function table(queries is map) returns map
{
    var keyTable = {};
    for (var entry in queries)
    {
        keyTable[entry.key] = { "key" : entry.key, "source" : 1, "kind" : "query", "value" : entry.value, "description" : "", "implicit" : false };
    }
    return { "keys" : keyTable, "order" : keys(queries), "ambiguous" : {}, "sources" : [] };
}

/** A point marker (a mate connector) to pick as an entry's point. */
function marker(context is Context, id is Id, p is Vector) returns Query
{
    opMateConnector(context, id, { "coordSystem" : coordSystem(p, vector(1, 0, 0), vector(0, 0, 1)), "owner" : qNothing() });
    return qCreatedBy(id, EntityType.BODY);
}

function regionEntry(sourceKey is string, secondKey is string, point is Query, output is ExtractRegionOutput) returns map
{
    return { "x_type" : ExtractEntryType.REGION, "x_sourceKey" : sourceKey, "x_secondKey" : secondKey, "x_point" : point,
            "x_regionOutput" : output, "x_name" : "" };
}

function count(context is Context, q is Query) returns number
{
    return size(evaluateQuery(context, q));
}

function regionTests(context is Context, id is Id) returns array
{
    var out = [];

    // A cube whose top (z 50) and front (y -50) faces are split by planes x -20 and x 20.
    fCuboid(context, id + "cube", { "corner1" : pt(-50, -50, -50), "corner2" : pt(50, 50, 50) });
    const cube = qCreatedBy(id + "cube", EntityType.BODY);
    const faces = qUnion([qContainsPoint(qOwnedByBody(cube, EntityType.FACE), pt(0, 0, 50)),
                qContainsPoint(qOwnedByBody(cube, EntityType.FACE), pt(0, -50, 0))]);
    opPlane(context, id + "p1", { "plane" : plane(pt(-20, 0, 0), vector(1, 0, 0)) });
    opPlane(context, id + "p2", { "plane" : plane(pt(20, 0, 0), vector(1, 0, 0)) });
    opSplitFace(context, id + "split", { "faceTargets" : faces,
                "planeTools" : qUnion([qCreatedBy(id + "p1", EntityType.FACE), qCreatedBy(id + "p2", EntityType.FACE)]) });
    opDeleteBodies(context, id + "deletePlanes", { "entities" : qUnion([qCreatedBy(id + "p1", EntityType.BODY), qCreatedBy(id + "p2", EntityType.BODY)]) });

    const topAndFront = qUnion([qParallelPlanes(qOwnedByBody(cube, EntityType.FACE), vector(0, 0, 1), false),
                qParallelPlanes(qOwnedByBody(cube, EntityType.FACE), vector(0, -1, 0), false)]);
    const cuts = qCreatedBy(id + "split", EntityType.EDGE);
    const available = table({ "topAndFront" : topAndFront, "cuts" : cuts, "cube" : cube });
    const middle = marker(context, id + "mid", pt(0, -10, 50));

    // Bounded by the cuts: the middle strip of the top and of the front.
    {
        const r = resolveEntry(context, available, regionEntry("topAndFront", "cuts", middle, ExtractRegionOutput.FACES));
        const n = r.error == undefined ? count(context, r.value) : -1;
        var ok = n == 2;
        if (ok)
        {
            for (var f in evaluateQuery(context, r.value))
            {
                ok = ok && abs(evApproximateCentroid(context, { "entities" : f })[0]) < 1e-6 * meter;
            }
        }
        out = append(out, result("Region: faces bounded by the cuts", ok,
                    (r.error != undefined ? r.error : n ~ " faces (expected 2, both centred at x 0)")));
    }
    {
        const r = resolveEntry(context, available, regionEntry("topAndFront", "cuts", middle, ExtractRegionOutput.BOUNDARY));
        const n = r.error == undefined ? count(context, r.value) : -1;
        const cutCount = r.error == undefined ? count(context, qIntersection([r.value, cuts])) : -1;
        out = append(out, result("Region: boundary edges", n == 6 && cutCount == 4,
                    (r.error != undefined ? r.error : n ~ " edges (expected 6), of them cuts " ~ cutCount ~ " (expected 4)")));
    }
    {
        const r = resolveEntry(context, available, regionEntry("topAndFront", "cuts", middle, ExtractRegionOutput.BOTH));
        const n = r.error == undefined ? count(context, r.value) : -1;
        out = append(out, result("Region: faces and boundary", n == 8, (r.error != undefined ? r.error : n ~ " entities (expected 8)")));
    }

    // No boundary key: the seed's connected patch within the source faces (top + front, 6).
    {
        const r = resolveEntry(context, available, regionEntry("topAndFront", "", middle, ExtractRegionOutput.FACES));
        const n = r.error == undefined ? count(context, r.value) : -1;
        out = append(out, result("Region: no boundary = connected patch", n == 6, (r.error != undefined ? r.error : n ~ " faces (expected 6)")));
    }

    // A body as the source: its faces; the cuts alone do not close a region on a cube.
    {
        const r = resolveEntry(context, available, regionEntry("cube", "cuts", middle, ExtractRegionOutput.FACES));
        const n = r.error == undefined ? count(context, r.value) : -1;
        out = append(out, result("Region: a body source spreads round the sides", n == 10, (r.error != undefined ? r.error : n ~ " faces (expected all 10)")));
    }

    // Missing point is an error on x_point.
    {
        const r = resolveEntry(context, available, regionEntry("topAndFront", "cuts", qNothing(), ExtractRegionOutput.FACES));
        out = append(out, result("Region: no point is an error", r.error != undefined && r.parameter == "x_point",
                    r.error != undefined ? r.error : "no error"));
    }
    return out;
}
