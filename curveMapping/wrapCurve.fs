FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

//import curveChain(export import)
export import(path : "670e82ad72abc97906ec9038", version : "9b4465a71fe746c08e1290d5");
//import curveMappingUtils(export import)
export import(path : "de955d503dbb0ec88622e51b", version : "7ad38baccd6573d3fb22f005");
//import curveMappingCore (export import)
export import(path : "683d867c35fdab9c98d47556", version : "8018560f72f4525f39044cef");

//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

annotation { "Feature Type Name" : "Wrap Curve",
             "Feature Type Description" : "Map curves from one reference edge to another using Frenet frame transformations",
             "Filter Selector" : "allparts",
             "Editing Logic Function" : "wrapCurveEditLogic" }
export const wrapCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference)" }
        definition.fromEdge is Query;

        annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference)" }
        definition.toEdge is Query;

        annotation { "Name" : "Source curves",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 50,
                    "Description" : "Curves to map from fromEdge to toEdge" }
        definition.sourceCurves is Query;

        annotation { "Name" : "Alignment point",
                    "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1,
                    "Description" : "Point to align mapping (defaults to curve midpoints)" }
        definition.alignmentPoint is Query;

        annotation { "Name" : "Chain continuity" }
        definition.chainContinuity is ChainContinuity;

        annotation { "Name" : "Mapping mode" }
        definition.mappingMode is MappingMode;

        annotation { "Name" : "Advanced options",
                    "Default" : false }
        definition.showAdvanced is boolean;

        if (definition.showAdvanced)
        {
            annotation { "Name" : "Samples per segment",
                        "Description" : "Number of points to sample for each curve segment" }
            isInteger(definition.numSamples, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Minimal segmentation",
                        "Description" : "Only segment at C0 breaks (not G1)",
                        "Default" : false }
            definition.minimalSegmentation is boolean;
        }

        annotation { "Group Name" : "Debug Options",
                    "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print from BSplines",
                        "Description" : "Print BSpline data from 'from' reference chain",
                        "Default" : false }
            definition.debugFromBSplines is boolean;

            annotation { "Name" : "Print to BSplines",
                        "Description" : "Print BSpline data from 'to' reference chain",
                        "Default" : false }
            definition.debugToBSplines is boolean;

            annotation { "Name" : "Print source BSplines",
                        "Description" : "Print BSpline data from source curves",
                        "Default" : false }
            definition.debugSourceBSplines is boolean;

            annotation { "Name" : "Print wrapped BSplines",
                        "Description" : "Print BSpline data from wrapped output curves",
                        "Default" : false }
            definition.debugWrappedCurves is boolean;
        }
    }
    {
        // Default values
        const numSamples = definition.showAdvanced ? definition.numSamples : 25;
        const minimalSegmentation = definition.showAdvanced ? definition.minimalSegmentation : false;

        // Step 1: Build curve chains with continuity validation
        var fromChain;
        var toChain;

        try
        {
            fromChain = buildCurveChain(context, definition.fromEdge,
                                       definition.chainContinuity, {});
        }
        catch
        {
            throw regenError("From edge chain error", ["fromEdge"]);
        }

        try
        {
            toChain = buildCurveChain(context, definition.toEdge,
                                     definition.chainContinuity, {});
        }
        catch
        {
            throw regenError("To edge chain error", ["toEdge"]);
        }

        // Debug: Print from chain BSplines
        if (definition.debugFromBSplines)
        {
            println("\n=== FROM REFERENCE CHAIN ===");
            const fromEdges = evaluateQuery(context, definition.fromEdge);
            for (var i = 0; i < size(fromEdges); i += 1)
            {
                try
                {
                    const bspline = evApproximateBSplineCurve(context, {
                        "edge" : fromEdges[i]
                    });
                    println("FROM Edge #" ~ (i + 1));
                    printBSplineInfo(bspline);
                }
                catch
                {
                    println("FROM Edge #" ~ (i + 1) ~ ": Could not convert to BSpline");
                }
            }
        }

        // Debug: Print to chain BSplines
        if (definition.debugToBSplines)
        {
            println("\n=== TO REFERENCE CHAIN ===");
            const toEdges = evaluateQuery(context, definition.toEdge);
            for (var i = 0; i < size(toEdges); i += 1)
            {
                try
                {
                    const bspline = evApproximateBSplineCurve(context, {
                        "edge" : toEdges[i]
                    });
                    println("TO Edge #" ~ (i + 1));
                    printBSplineInfo(bspline);
                }
                catch
                {
                    println("TO Edge #" ~ (i + 1) ~ ": Could not convert to BSpline");
                }
            }
        }

        // Step 2: Determine alignment point
        var alignmentPoint;
        if (size(evaluateQuery(context, definition.alignmentPoint)) > 0)
        {
            // User specified alignment point
            const alignVertex = evaluateQuery(context, definition.alignmentPoint)[0];

            // Try as mate connector first (using try silent pattern)
            const mateConnectorResult = try silent(evMateConnector(context, {
                "mateConnector" : alignVertex
            }));

            if (mateConnectorResult != undefined)
            {
                alignmentPoint = mateConnectorResult.origin;
            }
            else
            {
                // Must be a vertex
                alignmentPoint = evVertexPoint(context, {
                    "vertex" : alignVertex
                });
            }
        }
        else
        {
            // Default: use midpoint of fromChain
            const midEval = evaluateChain(context, fromChain, [0.5], 0);
            alignmentPoint = midEval.points[0];
        }

        // Step 3: Build mapping
        const mapping = buildCurveMapping(context, id, fromChain, toChain,
                                         alignmentPoint, AlignmentMode.AUTO,
                                         { "mappingMode" : definition.mappingMode });

        // Step 4: Process each source curve
        const sourceCurveArray = evaluateQuery(context, definition.sourceCurves);

        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            const sourceEdge = sourceCurveArray[i];

            // Get BSpline representation of source curve
            var sourceBSpline;
            try
            {
                sourceBSpline = evApproximateBSplineCurve(context, {
                    "edge" : sourceEdge
                });
            }
            catch (error)
            {
                reportFeatureWarning(context, id,
                    "Could not convert edge " ~ (i + 1) ~ " to BSpline: " ~ toString(error));
                continue;
            }

            // Debug: Print source curve BSpline
            if (definition.debugSourceBSplines)
            {
                println("\n=== SOURCE CURVE #" ~ (i + 1) ~ " ===");
                printBSplineInfo(sourceBSpline);
            }

            // Map curve with segmentation
            var mappedSegments;
            try
            {
                mappedSegments = mapCurveSegmented(context, mapping, sourceBSpline, {
                    "numSamplesPerSegment" : numSamples,
                    "minimalSegmentation" : minimalSegmentation
                });
            }
            catch (error)
            {
                reportFeatureWarning(context, id,
                    "Could not map curve " ~ (i + 1) ~ ": " ~ toString(error));
                continue;
            }

            // Join segments
            const joinedCurves = joinMappedSegments(context, mappedSegments, {
                "keepSeparateAtG0" : true
            });

            // Debug: Print wrapped curves
            if (definition.debugWrappedCurves)
            {
                println("\n=== WRAPPED CURVES FROM SOURCE #" ~ (i + 1) ~ " ===");
                for (var k = 0; k < size(joinedCurves); k += 1)
                {
                    println("Wrapped Curve #" ~ (i + 1) ~ "." ~ (k + 1));
                    printBSplineInfo(joinedCurves[k]);
                }
            }

            // Create geometry from mapped curves
            for (var j = 0; j < size(joinedCurves); j += 1)
            {
                try
                {
                    opCreateBSplineCurve(context, id + ("curve" ~ i ~ "_" ~ j), {
                        "bSplineCurve" : joinedCurves[j]
                    });
                }
                catch (error)
                {
                    reportFeatureWarning(context, id,
                        "Could not create mapped curve " ~ (i + 1) ~ "." ~ (j + 1) ~ ": " ~ toString(error));
                }
            }
        }
    }, {
        chainContinuity : ChainContinuity.G0,
        mappingMode : MappingMode.LENGTH,
        showAdvanced : false,
        numSamples : 25,
        minimalSegmentation : false,
        debugFromBSplines : false,
        debugToBSplines : false,
        debugSourceBSplines : false,
        debugWrappedCurves : false
    });

/**
 * Editing logic to provide better defaults and validation.
 */
export function wrapCurveEditLogic(context is Context, id is Id, oldDefinition is map,
                                   definition is map, isCreating is boolean) returns map
{
    // Auto-select chain continuity based on from/to edges
    if (oldDefinition.fromEdge != definition.fromEdge ||
        oldDefinition.toEdge != definition.toEdge)
    {
        // Could analyze edges and auto-detect continuity
        // For now, keep user selection
    }

    return definition;
}

/**
 * Print detailed BSpline information for debugging.
 * Uses bspline_data.fs accessor functions.
 */
function printBSplineInfo(bspline is BSplineCurve)
{
    const degree = getDegree(bspline);
    const numCPs = getNumControlPoints(bspline);
    const knots = getKnotVector(bspline);
    const cps = getControlPoints(bspline);
    const paramRange = getBSplineParamRange(bspline);
    const endpoints = getBSplineEndpoints(bspline);

    println("  Degree: " ~ degree);
    println("  Control Points: " ~ numCPs);
    println("  Rational: " ~ isRational(bspline));
    println("  Periodic: " ~ isPeriodic(bspline));
    println("  Dimension: " ~ getDimension(bspline));
    println("  Param Range: [" ~ paramRange.uMin ~ ", " ~ paramRange.uMax ~ "]");
    println("  Knot Vector: " ~ knots);

    // Print endpoints
    println("  Start Point: [" ~
            toString(endpoints.start[0] / meter) ~ ", " ~
            toString(endpoints.start[1] / meter) ~ ", " ~
            toString(endpoints.start[2] / meter) ~ "] m");
    println("  End Point: [" ~
            toString(endpoints.end[0] / meter) ~ ", " ~
            toString(endpoints.end[1] / meter) ~ ", " ~
            toString(endpoints.end[2] / meter) ~ "] m");

    // Print control points (only first/last to avoid clutter)
    println("  First CP: [" ~
            toString(cps[0][0] / meter) ~ ", " ~
            toString(cps[0][1] / meter) ~ ", " ~
            toString(cps[0][2] / meter) ~ "] m");
    println("  Last CP: [" ~
            toString(cps[numCPs - 1][0] / meter) ~ ", " ~
            toString(cps[numCPs - 1][1] / meter) ~ ", " ~
            toString(cps[numCPs - 1][2] / meter) ~ "] m");

    // Print ALL control points for detailed debugging
    println("  All Control Points:");
    for (var i = 0; i < numCPs; i += 1)
    {
        println("    CP[" ~ i ~ "]: [" ~
                toString(cps[i][0] / meter) ~ ", " ~
                toString(cps[i][1] / meter) ~ ", " ~
                toString(cps[i][2] / meter) ~ "] m");
    }

    // Print continuity info if multiple spans
    const numSpans = getNumSpans(bspline);
    if (numSpans > 1)
    {
        const minContinuity = getMinInteriorContinuity(bspline, KNOT_TOLERANCE);
        println("  Spans: " ~ numSpans);
        println("  Min Interior Continuity: C" ~ minContinuity);
    }
}
