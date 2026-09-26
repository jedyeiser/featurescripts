FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: curve_core.fs -- bestSingleArc, arcEndTangents, tangentAngle, emitArcCurve, expandEdgeQuery
export import(path : "02d7784437f621c76397f0d6", version : "71f4e8e4d07b445940796389");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

/**
 * Recognize arcs
 *
 * Finds spline edges that ONE circular arc represents within a tolerance, and optionally
 * rebuilds the input with true (sketch) arcs in their place, so Onshape sees a radius where it
 * saw a spline.
 *
 * Why a single arc and never a biarc: this answers "is this spline really an arc", not "how
 * would arcs approximate it". A biarc can approximate almost anything; one arc is a claim
 * about the shape.
 *
 * The replacement arc passes exactly through the edge's two end points, so the rebuilt wire
 * stays connected. Its end DIRECTIONS are the circle's, not the spline's: where they differ,
 * the joint with a neighbouring edge gains that angle as a kink. "Max end tangent change"
 * bounds it, and every candidate's change is reported, so a near miss is visible.
 *
 * Test per spline edge (samples at RECOGNIZE_SAMPLES arc-length stations, one kernel call):
 *   - open (its ends do not meet) and not straight (chord sagitta above the tolerance: a
 *     straight stretch fits a circle of meaningless radius);
 *   - the circle through both ends and the best interior sample (bestSingleArc) is within the
 *     tolerance of every sample, in the plane and out of it;
 *   - both end tangents within "Max end tangent change" of the circle's.
 *
 * Lines and arcs pass through unchanged and are not counted as candidates.
 *
 * Written 2026-09-25 with the arc / line tangency review (reviews/2026-09-25_arc_line_fitting).
 */

/** Samples per edge: enough that a spline cannot wander off the circle between them unseen. */
export const RECOGNIZE_SAMPLES = 64;

export const RecognizeArcsToleranceBounds = { (millimeter) : [0.00001, 0.01, 10] } as LengthBoundSpec;
export const RecognizeArcsAngleBounds = { (degree) : [0, 0.05, 10], (radian) : 0.001 } as AngleBoundSpec;

annotation { "Feature Type Name" : "Recognize arcs",
        "Feature Type Description" : "Find spline edges that a single circular arc represents within tolerance, report them, and optionally rebuild the wire with true arcs in their place." }
export const recognizeArcs = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges or wires", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO,
                    "Description" : "The edges, or wire bodies, to examine. Only spline edges are candidates; lines and arcs pass through." }
        definition.sourceEdges is Query;

        annotation { "Name" : "Tolerance", "Description" : "Largest distance between a spline and the arc that would replace it." }
        isLength(definition.tolerance, RecognizeArcsToleranceBounds);

        annotation { "Name" : "Max end tangent change",
                    "Description" : "Largest angle between a spline's end direction and the arc's. The replacement adds up to this much kink where the edge meets its neighbour." }
        isAngle(definition.maxTangentChange, RecognizeArcsAngleBounds);

        annotation { "Name" : "Replace with arcs", "Default" : true,
                    "Description" : "Build a copy of the input with true arcs in place of the matching splines. Off: only report and highlight them." }
        definition.replace is boolean;

        if (definition.replace)
        {
            annotation { "Name" : "Name", "Description" : "Names the output wires. Clear it to leave them unnamed." }
            definition.outputName is string;

            annotation { "Name" : "Delete input", "Default" : false,
                        "Description" : "Delete the input wire bodies once the copy is built. Edges selected from a larger body are left alone." }
            definition.deleteInput is boolean;
        }

        annotation { "Name" : "Highlight matches", "Default" : true, "Description" : "Show the matching spline edges in green." }
        definition.highlight is boolean;
    }
    {
        const edges = evaluateQuery(context, expandEdgeQuery(definition.sourceEdges));
        if (size(edges) == 0)
        {
            throw regenError("Select edges or wire bodies.", ["sourceEdges"]);
        }

        var results = [];
        for (var i = 0; i < size(edges); i += 1)
        {
            results = append(results, examineEdge(context, edges[i], definition));
        }

        var matches = [];
        var candidates = 0;
        for (var result in results)
        {
            if (result.candidate)
            {
                candidates += 1;
            }
            if (result.match)
            {
                matches = append(matches, result);
            }
        }

        printReport(results, definition);

        var output = qNothing();
        if (definition.replace)
        {
            output = rebuild(context, id, edges, results, definition);
        }

        if (definition.highlight && size(matches) > 0)
        {
            var matched = [];
            for (var result in matches)
            {
                matched = append(matched, result.edge);
            }
            addDebugEntities(context, qUnion(matched), DebugColor.GREEN);
        }

        reportFeatureInfo(context, id, summaryText(results, size(matches), candidates, definition));

        embedStandardOutputs(context, id, {
                    "output" : output,
                    "outputDescription" : "The input rebuilt with arcs in place of the matching splines",
                    "inputs" : definition.sourceEdges,
                    "variables" : {
                        "recognizedCount" : extractableVariable(size(matches), "Spline edges replaced by (or, report only, matching) a single arc."),
                        "candidateCount" : extractableVariable(candidates, "Spline edges examined.")
                    },
                    "queries" : {
                        "arcEdges" : extractableQuery(qUnion([qGeometry(qOwnedByBody(output, EntityType.EDGE), GeometryType.ARC),
                                    qGeometry(qOwnedByBody(output, EntityType.EDGE), GeometryType.CIRCLE)]),
                            "Every arc edge of the output: the recognized ones and any that were arcs already.", DebugColor.GREEN)
                    }
                });
    }, {
        "replace" : true,
        "outputName" : "",
        "deleteInput" : false,
        "highlight" : true
    });

/**
 * One edge's verdict.
 *
 * @returns {map} : { edge, index, kind ("line" | "arc" | "spline"), candidate, match, reason,
 *      and for splines that produced a circle: arc, deviation, tangentChange, degree, controlPoints }
 */
function examineEdge(context is Context, edge is Query, definition is map) returns map
{
    const shape = evCurveDefinition(context, { "edge" : edge });

    if (shape is Line)
    {
        return { "edge" : edge, "kind" : "line", "candidate" : false, "match" : false };
    }
    if (shape is Circle)
    {
        return { "edge" : edge, "kind" : "arc", "candidate" : false, "match" : false };
    }

    var verdict = { "edge" : edge, "kind" : "spline", "candidate" : true, "match" : false };
    if (shape is BSplineCurve)
    {
        verdict.degree = shape.degree;
        verdict.controlPoints = size(shape.controlPoints);
    }

    var parameters = [];
    for (var k = 0; k <= RECOGNIZE_SAMPLES; k += 1)
    {
        parameters = append(parameters, k / RECOGNIZE_SAMPLES);
    }
    const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : parameters });

    var points = [];
    for (var tl in lines)
    {
        points = append(points, tl.origin);
    }
    const first = points[0];
    const last = points[size(points) - 1];

    if (norm(last - first) < OFFSET_GEOM_TOL)
    {
        verdict.reason = "closed";
        return verdict;
    }

    // Straight within tolerance: any circle through it is as good as any other.
    const chordDir = normalize(last - first);
    var sagitta = 0 * meter;
    for (var point in points)
    {
        const toPoint = point - first;
        sagitta = max(sagitta, norm(toPoint - dot(toPoint, chordDir) * chordDir));
    }
    if (sagitta <= definition.tolerance)
    {
        verdict.reason = "straight within tolerance";
        return verdict;
    }

    const fit = bestSingleArc(points);
    if (fit == undefined)
    {
        verdict.reason = "no circle through the samples";
        return verdict;
    }

    const deviation = sqrt(fit.radialError * fit.radialError + fit.outOfPlane * fit.outOfPlane);
    const ends = arcEndTangents(fit);
    const change = max(tangentAngle(lines[0].direction, ends.start),
            tangentAngle(lines[size(lines) - 1].direction, ends.end)) * radian;

    verdict.arc = fit;
    verdict.deviation = deviation;
    verdict.tangentChange = change;

    if (deviation > definition.tolerance)
    {
        verdict.reason = "off the arc by " ~ fmtMM(deviation, 4, 0) ~ " mm";
    }
    else if (change > definition.maxTangentChange)
    {
        verdict.reason = "end direction changes by " ~ fmtNum(change / degree, 4, 0) ~ " deg";
    }
    else
    {
        verdict.match = true;
    }

    return verdict;
}

/**
 * The input again, with each matching spline replaced by its arc. The other edges are copied
 * as they are, so the wire keeps its topology apart from the curve type of the replaced edges.
 */
function rebuild(context is Context, id is Id, edges is array, results is array, definition is map) returns Query
{
    var members = [];
    var scaffolding = [];

    for (var i = 0; i < size(results); i += 1)
    {
        if (results[i].match)
        {
            const arcId = id + ("arc" ~ i);
            emitArcCurve(context, arcId, results[i].arc);
            members = append(members, qCreatedBy(arcId, EntityType.EDGE));
            scaffolding = append(scaffolding, qCreatedBy(arcId, EntityType.BODY));
        }
        else
        {
            members = append(members, edges[i]);
        }
    }

    opExtractWires(context, id + "wire", { "edges" : qUnion(members) });
    const output = qCreatedBy(id + "wire", EntityType.BODY);

    if (size(scaffolding) > 0)
    {
        opDeleteBodies(context, id + "cleanup", { "entities" : qUnion(scaffolding) });
    }

    if (definition.outputName != "")
    {
        setProperty(context, { "entities" : output, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
    }

    if (definition.deleteInput)
    {
        const inputBodies = qBodyType(qEntityFilter(definition.sourceEdges, EntityType.BODY), BodyType.WIRE);
        if (!isQueryEmpty(context, inputBodies))
        {
            opDeleteBodies(context, id + "deleteInput", { "entities" : inputBodies });
        }
    }

    return output;
}

function summaryText(results is array, matched is number, candidates is number, definition is map) returns string
{
    var text = toString(matched) ~ " of " ~ toString(candidates) ~ " spline edges are single arcs within "
        ~ fmtMM(definition.tolerance, 4, 0) ~ " mm and " ~ fmtNum(definition.maxTangentChange / degree, 3, 0) ~ " deg";

    var radii = "";
    for (var result in results)
    {
        if (result.match)
        {
            radii = radii ~ ((radii == "") ? "" : ", ") ~ "R " ~ fmtMM(result.arc.radius, 3, 0);
        }
    }

    return (radii == "") ? text ~ "." : text ~ ": " ~ radii ~ ".";
}

function printReport(results is array, definition is map)
{
    println("Recognize arcs: tolerance " ~ fmtMM(definition.tolerance, 4, 0) ~ " mm, max end tangent change "
            ~ fmtNum(definition.maxTangentChange / degree, 4, 0) ~ " deg");

    for (var i = 0; i < size(results); i += 1)
    {
        const result = results[i];
        var row = "  edge " ~ padLeft(toString(i), 3) ~ "  " ~ result.kind;

        if (result.degree != undefined)
        {
            row = row ~ " (deg " ~ result.degree ~ ", " ~ result.controlPoints ~ " CPs)";
        }
        if (result.arc != undefined)
        {
            row = row ~ "  R " ~ fmtMM(result.arc.radius, 3, 0) ~ " mm"
                ~ "  dev " ~ fmtMM(result.deviation, 5, 0) ~ " mm"
                ~ "  tangent " ~ fmtNum(result.tangentChange / degree, 5, 0) ~ " deg";
        }
        if (result.match)
        {
            row = row ~ "  -> ARC";
        }
        else if (result.reason != undefined)
        {
            row = row ~ "  -> kept (" ~ result.reason ~ ")";
        }

        println(row);
    }
}
