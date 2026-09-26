FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: clean_wire.fs, merge_curve.fs, map_curve.fs
import(path : "ad14cae77548843ba119071c", version : "231a0f9c912df022ab9860ac");
import(path : "584469527ba0d496604523ec", version : "7364e5d4fa56e0b9f84d2814");
import(path : "83aa07e20d0c8710c97a6cd7", version : "8697a2b8a7c108b18a89e225");

/**
 * Curve tools tests: builds its own wires, runs Clean wire, Merge curve and Map curve on
 * them, and checks the published keys (ends, runs, breaks) against the geometry. PASS / FAIL
 * lines go to the console; the feature shows a warning when anything failed. Cases sit 1 m
 * apart in Y so they cannot interfere.
 */
annotation { "Feature Type Name" : "Curve tools tests", "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const curveToolsTests = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Clean wire", "Default" : true }
        definition.runClean is boolean;
        annotation { "Name" : "Merge curve", "Default" : true }
        definition.runMerge is boolean;
        annotation { "Name" : "Map curve", "Default" : true }
        definition.runMap is boolean;
    }
    {
        var results = [];
        if (definition.runClean)
        {
            results = concatenateArrays([results, cleanTests(context, id + "clean")]);
        }
        if (definition.runMerge)
        {
            results = concatenateArrays([results, mergeTests(context, id + "merge")]);
        }
        if (definition.runMap)
        {
            results = concatenateArrays([results, mapTests(context, id + "map")]);
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
    }, { "runClean" : true, "runMerge" : true, "runMap" : true });

// ============================================================================
// Fixtures and measures
// ============================================================================

function pt(x is number, y is number, z is number) returns Vector
{
    return vector(x, y, z) * millimeter;
}

/** An open polyline in z = 0 as a wire body of its own (sketch lines extracted, sketch deleted). */
function polylineWire(context is Context, id is Id, points is array) returns Query
{
    const sketch = newSketchOnPlane(context, id + "sketch", { "sketchPlane" : plane(pt(0, 0, 0), vector(0, 0, 1)) });
    for (var i = 0; i < size(points) - 1; i += 1)
    {
        skLineSegment(sketch, "line" ~ i, { "start" : vector(points[i][0], points[i][1]) * millimeter,
                    "end" : vector(points[i + 1][0], points[i + 1][1]) * millimeter });
    }
    skSolve(sketch);
    opExtractWires(context, id + "wire", { "edges" : qOwnedByBody(qBodyType(qCreatedBy(id + "sketch", EntityType.BODY), BodyType.WIRE), EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(id + "sketch", EntityType.BODY) });
    return qCreatedBy(id + "wire", EntityType.BODY);
}

function marker(context is Context, id is Id, p is Vector) returns Query
{
    opMateConnector(context, id, { "coordSystem" : coordSystem(p, vector(1, 0, 0), vector(0, 0, 1)), "owner" : qNothing() });
    return qCreatedBy(id, EntityType.BODY);
}

function result(name is string, ok is boolean, detail is string) returns map
{
    return { "name" : name, "ok" : ok, "detail" : detail };
}

function fmtV(p is Vector) returns string
{
    return "(" ~ roundToPrecision(p[0] / millimeter, 3) ~ ", " ~ roundToPrecision(p[1] / millimeter, 3) ~ ", " ~ roundToPrecision(p[2] / millimeter, 3) ~ ")";
}

/** The one vertex of a key, or undefined. */
function vertexAt(context is Context, q is Query)
{
    const list = evaluateQuery(context, q);
    return size(list) == 1 ? evVertexPoint(context, { "vertex" : list[0] }) : undefined;
}

/** "start (x, y, z) end (x, y, z)" and whether both are where expected. */
function checkEnds(context is Context, embedded is map, wantStart is Vector, wantEnd is Vector) returns map
{
    const s = vertexAt(context, embedded.query.startVertex.value);
    const e = vertexAt(context, embedded.query.endVertex.value);
    const startEdges = evaluateQuery(context, embedded.query.startEdge.value);
    const endEdges = evaluateQuery(context, embedded.query.endEdge.value);
    const edgesOk = size(startEdges) == 1 && size(endEdges) == 1
        && !isQueryEmpty(context, qIntersection([qAdjacent(startEdges[0], AdjacencyType.VERTEX, EntityType.VERTEX), embedded.query.startVertex.value]))
        && !isQueryEmpty(context, qIntersection([qAdjacent(endEdges[0], AdjacencyType.VERTEX, EntityType.VERTEX), embedded.query.endVertex.value]));
    const ok = s != undefined && e != undefined && norm(s - wantStart) < 0.01 * millimeter && norm(e - wantEnd) < 0.01 * millimeter && edgesOk;
    return { "ok" : ok, "detail" : "start " ~ (s == undefined ? "none" : fmtV(s)) ~ " (expected " ~ fmtV(wantStart) ~ "), end "
                ~ (e == undefined ? "none" : fmtV(e)) ~ " (expected " ~ fmtV(wantEnd) ~ "), end edges at their vertices " ~ edgesOk };
}

// ============================================================================
// Clean wire
// ============================================================================

function cleanTests(context is Context, id is Id) returns array
{
    var out = [];
    // An open polyline with two sharp corners: ends where the source starts / ends; runs in
    // order from the start; one break between consecutive runs.
    const wire = polylineWire(context, id + "src", [[0, 0], [100, 0], [200, 50], [300, 50]]);
    cleanWire(context, id + "cw", { "sourceEdges" : wire });
    const embedded = getVariable(context, toString(id + "cw"));
    const ends = checkEnds(context, embedded, pt(0, 0, 0), pt(300, 50, 0));
    out = append(out, result("Clean wire: ends", ends.ok, ends.detail));

    const curveCount = embedded.variable.curveCount.value;
    const breaks = size(evaluateQuery(context, embedded.query.breakVertices.value));
    var runsOk = curveCount >= 1;
    var detail = "";
    var previous = embedded.query.startVertex.value;
    for (var k = 1; k <= curveCount; k += 1)
    {
        const run = embedded.query["run" ~ k];
        if (run == undefined)
        {
            runsOk = false;
            detail ~= "run" ~ k ~ " missing; ";
            break;
        }
        // Each run touches the vertex the previous one ended at.
        const touches = !isQueryEmpty(context, qIntersection([qAdjacent(run.value, AdjacencyType.VERTEX, EntityType.VERTEX), previous]));
        runsOk = runsOk && touches;
        previous = qSubtraction(qAdjacent(run.value, AdjacencyType.VERTEX, EntityType.VERTEX), previous);
    }
    runsOk = runsOk && !isQueryEmpty(context, qIntersection([previous, embedded.query.endVertex.value]));
    out = append(out, result("Clean wire: runs in order, breaks between", runsOk && breaks == curveCount - 1,
                detail ~ curveCount ~ " run(s) chained start to end " ~ runsOk ~ ", " ~ breaks ~ " break(s) (expected " ~ (curveCount - 1) ~ ")"));
    return out;
}

// ============================================================================
// Merge curve
// ============================================================================

function mergeTests(context is Context, id is Id) returns array
{
    var out = [];
    // Two lines of one wire, (0, 1000) -> (100, 1000) -> (200, 1050): merged into one spline.
    const wire = polylineWire(context, id + "src", [[0, 1000], [100, 1000], [200, 1050]]);
    const edges = evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE));
    const seed = qClosestTo(qUnion(edges), pt(50, 1000, 0));
    const other = qClosestTo(qUnion(edges), pt(150, 1025, 0));
    mergeCurve(context, id + "mc", {
                "seedEdge" : seed,
                "mergeEdge" : other,
                "approximationDegree" : 3,
                "approximationTolerance" : 1e-5 * meter,
                "approximationMaxCPs" : 30
            });
    const embedded = getVariable(context, toString(id + "mc"));
    const ends = checkEnds(context, embedded, pt(0, 1000, 0), pt(200, 1050, 0));
    const merged = size(evaluateQuery(context, embedded.query.mergedEdge.value));
    out = append(out, result("Merge curve: ends and merged edge", ends.ok && merged == 1, ends.detail ~ ", mergedEdge " ~ merged ~ " (expected 1)"));
    return out;
}

// ============================================================================
// Map curve
// ============================================================================

function mapTests(context is Context, id is Id) returns array
{
    var out = [];
    // To-chain (0, 2000) -> (100, 2000) -> (200, 2050); from-chain x 50..150 at y 2200; zero
    // at x 100. Trim to-edges: arcs 50..150 of the to-chain -> start (50, 2000), end 50 along
    // the diagonal from (100, 2000).
    const toWire = polylineWire(context, id + "to", [[0, 2000], [100, 2000], [200, 2050]]);
    const fromWire = polylineWire(context, id + "from", [[50, 2200], [150, 2200]]);
    const zero = marker(context, id + "zero", pt(100, 2100, 0));
    mapCurve(context, id + "map", {
                "mapMode" : MapMode.TO_EDGES,
                "fromEdges" : fromWire,
                "toEdges" : toWire,
                "refPointMode" : RefPointMode.SHARED,
                "offsetRefPoint" : zero,
                "projectionMode" : ProjectionMode.WORLD_X,
                "flipTo" : false,
                "outputName" : "",
                "debugShowChainEnds" : false,
                "debugPrint" : false
            });
    const embedded = getVariable(context, toString(id + "map"));
    const diagonal = sqrt(100 * 100 + 50 * 50);
    const ends = checkEnds(context, embedded, pt(50, 2000, 0), pt(100 + 50 * 100 / diagonal, 2000 + 50 * 50 / diagonal, 0));
    out = append(out, result("Map curve (trim to-edges): ends", ends.ok, ends.detail));
    return out;
}
