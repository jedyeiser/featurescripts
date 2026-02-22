FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/approximationUtils.fs", version : "2878.0");
import(path : "onshape/std/path.fs", version : "2878.0");


//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
//import Utils
import(path : "ad98c7f43a25a4c0e8a428e7", version : "5a9af7b361c62896e83f949f");
// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");
// IMPORT: tools/frenet.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
// IMPORT: tools/point_projection.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/eb46317a27a44e391e11dfe6", version : "0cea3c8d27e4f7fd660aa69f");
// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");



export const samplingDensityBounds = {(millimeter) : [.1, 1, 10]} as LengthBoundSpec;

annotation { "Feature Type Name" : "Wrap Curve",
             "Feature Type Description" : "Map curves from one reference edge to another using Frenet frame transformations",
             "Filter Selector" : "allparts"
            }
export const wrapCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference)" }
            definition.fromEdges is Query;

            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on from curve" }
            definition.fromRef is Query;
        }

        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference)" }
            definition.toEdges is Query;

            annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipTo is boolean;

            annotation { "Name" : "To reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on to curve" }
            definition.toRef is Query;

            annotation { "Name" : "Flip normal", "Default" : false, "Description" : "When true, flips the frenet frame normal vector on the to chain" }
            definition.flipToNormal is boolean;
        }

        annotation { "Name" : "Source curves",
                    "Filter" : EntityType.EDGE,
                    "Description" : "Curves to map from fromEdge to toEdge" }
        definition.sourceCurves is Query;

        annotation { "Name" : "Advanced options",
                    "Default" : false }
        definition.showAdvanced is boolean;

        if (definition.showAdvanced)
        {
            annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
            isLength(definition.samplingDensity, samplingDensityBounds);

            annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);
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

            annotation { "Name" : "Detailed BSpline output",
                        "Description" : "Print full control points and knot vector (vs. metadata only)",
                        "Default" : false }
            definition.debugDetailedBSplines is boolean;

            annotation { "Name" : "Show from frames",
                        "Description" : "Draw Frenet frame axes along the from reference path",
                        "Default" : false }
            definition.debugShowFromFrames is boolean;

            annotation { "Name" : "Show to frames",
                        "Description" : "Draw Frenet frame axes along the to reference path",
                        "Default" : false }
            definition.debugShowToFrames is boolean;
        }
    }

    {
        // 1. Build FrenetPaths for from/to references
        var fromFrenetPath = buildFrenetPath(context, id, definition.fromEdges, false);
        var toFrenetPath   = buildFrenetPath(context, id, definition.toEdges,   definition.flipTo);

        if (definition.debugFromBSplines)
        {
            var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
            println("From path: " ~ toString(size(fromFrenetPath.edgeData)) ~ " edge(s), totalLength = " ~ toString(fromFrenetPath.totalLength));
            for (var j = 0; j < size(fromFrenetPath.edgeData); j += 1)
            {
                printBSpline(fromFrenetPath.edgeData[j].bspline, fmt, ["  -- From edge " ~ toString(j) ~ " --"]);
            }
        }
        if (definition.debugToBSplines)
        {
            var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
            println("To path: " ~ toString(size(toFrenetPath.edgeData)) ~ " edge(s), totalLength = " ~ toString(toFrenetPath.totalLength));
            for (var j = 0; j < size(toFrenetPath.edgeData); j += 1)
            {
                printBSpline(toFrenetPath.edgeData[j].bspline, fmt, ["  -- To edge " ~ toString(j) ~ " --"]);
            }
        }

        if (definition.debugShowFromFrames)
            debugDrawFrames(context, fromFrenetPath, 10);

        if (definition.debugShowToFrames)
            debugDrawFrames(context, toFrenetPath, 10);

        // 2. Resolve reference alignment arc-lengths
        var fromRefPt  = getRefPoint(context, definition.fromRef);
        var toRefPt    = getRefPoint(context, definition.toRef);
        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, fromRefPt);
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   toRefPt);

        // 3. Approximation options (with defaults for when showAdvanced is false)
        var degree = 3;
        if (definition.approximationDegree != undefined)
            degree = definition.approximationDegree;

        // 4. For each source curve: sample, map, fit, create
        var sourceCurveArray = evaluateQuery(context, definition.sourceCurves);
        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            var srcBSpline = evApproximateBSplineCurve(context, { "edge": sourceCurveArray[i] });

            // Determine number of sample points from sampling density
            var srcArcTable = buildArcLengthTable(srcBSpline, 200);
            var samplingDensity = 1 * millimeter;
            if (definition.samplingDensity != undefined)
                samplingDensity = definition.samplingDensity;
            var numSamples = max([5, ceil(srcArcTable.totalLength / samplingDensity) + 1]);

            // Sample source curve uniformly by arc-length
            var samples   = uniformArcLengthSamples(srcBSpline, numSamples, {});
            var srcPoints = samples.points;

            if (definition.debugSourceBSplines)
            {
                var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                println("Source curve " ~ toString(i) ~ ": " ~ toString(size(srcPoints)) ~
                        " samples, length = " ~ toString(srcArcTable.totalLength));
                printBSpline(srcBSpline, fmt, ["Source curve " ~ toString(i)]);
            }

            // Map each sampled point through Frenet frame transformation;
            // record which to-edge each mapped point lands on for span splitting
            var mappedData = [];
            for (var pt in srcPoints)
            {
                // Project source point onto from-path; get Frenet frame there
                var s_from     = projectOntoFrenetPath(fromFrenetPath, pt);
                var fromResult = getFrameAtArcLength(context, fromFrenetPath, s_from);

                // Express point in from-frame local coordinates [tangent, normal, binormal]
                var localCoords = worldPointToFrenet(pt, fromResult);

                // Linear arc-length mapping from from-path to to-path
                var s_to = toRefArc + (s_from - fromRefArc);

                // Get to-frame at mapped arc-length
                var toResult = getFrameAtArcLength(context, toFrenetPath, s_to);

                // Determine effective to-frame normal sign (apply flipToNormal toggle)
                var toSign = toResult.sign;
                if (definition.flipToNormal)
                    toSign = -1 * toSign;

                // Reconcile normal sign: if from/to normals are on opposite sides, flip to-frame xAxis
                var toFrameResult = toResult;
                if (toSign != fromResult.sign)
                {
                    var flippedFrame = coordSystem(toResult.frame.origin,
                                                   -1 * toResult.frame.xAxis,
                                                   toResult.frame.zAxis);
                    toFrameResult = mergeMaps(toResult, { "frame": flippedFrame });
                }

                mappedData = append(mappedData, {
                    "edgeIndex": toResult.edgeIndex,
                    "point"    : frenetPointToWorld(localCoords, toFrameResult)
                });
            }

            // Emit one output curve per to-edge span (prevents ringing at line/curve joints)
            var segStartIdx = 0;
            var segCount    = 0;

            while (segStartIdx < size(mappedData))
            {
                // Collect the run of consecutive points on the same to-edge
                var currentEdge = mappedData[segStartIdx].edgeIndex;
                var segEndIdx   = segStartIdx;
                while (segEndIdx + 1 < size(mappedData) && mappedData[segEndIdx + 1].edgeIndex == currentEdge)
                    segEndIdx += 1;

                var segPoints = [];
                for (var k = segStartIdx; k <= segEndIdx; k += 1)
                    segPoints = append(segPoints, mappedData[k].point);

                if (size(segPoints) >= degree + 1)
                {
                    var approxDef = {
                        "targets"    : [approximationTarget({'positions' : segPoints})],
                        "tolearance" : definition.approximationTolerance,
                        "maxCPs"     : definition.approximationMaxCPs,
                        "degree"     : degree };
                    var mappedCurve = approximateSpline(context, approxDef)[0];

                    if (definition.debugWrappedCurves)
                    {
                        var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                        printBSpline(mappedCurve, fmt, ["Wrapped curve " ~ toString(i) ~ "." ~ toString(segCount)]);
                    }

                    opCreateBSplineCurve(context, id + (toString(i) ~ "_" ~ toString(segCount) ~ "wrappedCurve"),
                                         { "bSplineCurve": mappedCurve });
                    segCount += 1;
                }

                segStartIdx = segEndIdx + 1;
            }
        }
    });


// ============================================================================
// debugDrawFrames  (internal helper)
// ============================================================================

/**
 * Draw Frenet frames at evenly-spaced arc-length positions along a FrenetPath.
 *
 * Uses addDebugArrow directly so arrow length scales with path geometry
 * (1/3 of inter-sample spacing) rather than using a hardcoded 5cm length.
 * Also avoids the console println that debug(context, CoordSystem) emits.
 *
 * Colors: xAxis (normal) = RED, yAxis (binormal) = GREEN, zAxis (tangent) = BLUE
 */
function debugDrawFrames(context is Context, frenetPath is map, numSamples is number)
{
    var totalLength = frenetPath.totalLength;
    var arrowLen    = totalLength / max([1, numSamples - 1]) / 3;
    var arrowRadius = arrowLen * 0.05;

    for (var i = 0; i < numSamples; i += 1)
    {
        var s      = totalLength * i / (numSamples - 1);
        var result = getFrameAtArcLength(context, frenetPath, s);
        var origin = result.frame.origin;

        addDebugArrow(context, origin, origin + arrowLen * result.frame.xAxis,  arrowRadius,           DebugColor.RED);
        addDebugArrow(context, origin, origin + arrowLen * yAxis(result.frame),  arrowRadius * (2 / 3), DebugColor.GREEN);
        addDebugArrow(context, origin, origin + arrowLen * result.frame.zAxis,   arrowRadius * 0.5,     DebugColor.BLUE);
    }
}


// ============================================================================
// buildFrenetPath
// ============================================================================

/**
 * Preprocess a G1-continuous edge chain into a queryable FrenetPath structure.
 *
 * Validates G1 continuity via constructPath, optionally reverses the chain,
 * and computes per-edge metadata: arc-length tables, inflection arc-lengths,
 * and cumulative normal sign.
 *
 * @param context   {Context}
 * @param id        {Id}
 * @param sourceEdges {Query}  : edges forming a single G1-continuous chain
 * @param flipRef   {boolean} : if true, reverse the chain direction
 * @returns {map} :
 *   "path"        {Path}            - ordered Path from constructPath
 *   "totalLength" {ValueWithUnits}  - total arc-length of the chain
 *   "edgeData"    {array}           - per-edge maps (index 0 = chain start)
 *
 * Each edgeData entry contains:
 *   query, bspline, stdDir, isLine, length,
 *   arcLengthTable, localInflectionArcs,
 *   lineFrame (lines only), lineStartPt (lines only),
 *   startArcLength, startSign
 */
export function buildFrenetPath(context is Context, id is Id, sourceEdges is Query, flipRef is boolean) returns map
{
    // 1. Validate G1 continuity and get ordered path
    var path;
    try
    {
        path = constructPath(context, sourceEdges);
    }
    catch (error)
    {
        throw regenError("Reference edges must be G1 continuous", sourceEdges);
    }

    // 2. Optionally reverse traversal direction
    if (flipRef)
        path = reverse(path);

    // 3. Build per-edge data
    var edgeData = [];
    var nEdges   = size(path.edges);

    for (var i = 0; i < nEdges; i += 1)
    {
        var edge    = path.edges[i];
        var flipped = path.flipped[i];
        var stdDir  = !flipped;  // true = traverse param 0→1

        var bspline  = evApproximateBSplineCurve(context, { "edge": edge });
        var length   = evLength(context, { "entities": edge });
        var curveDef = evCurveDefinition(context, { "edge": edge });
        var isLine   = (curveDef.curveType == CurveType.LINE);

        // Always build arc-length table — used for both projection and frame lookup
        var arcLengthTable = buildArcLengthTable(bspline, 100);

        var localInflectionArcs = [];
        var lineFrame           = undefined;
        var lineStartPt         = undefined;

        if (isLine)
        {
            // Build Frenet frame for the line, direction-corrected for traversal
            var lineDir = curveDef.direction;
            if (!stdDir)
                lineDir = -1 * lineDir;

            lineFrame = lineFrenetFrame({ "origin": curveDef.origin, "direction": lineDir });

            // Store traversal-start position for position interpolation in getFrameAtArcLength
            var endLines = evEdgeTangentLines(context, { "edge": edge, "parameters": [0, 1] });
            lineStartPt  = stdDir ? endLines[0].origin : endLines[1].origin;
        }
        else
        {
            // Detect inflection points and record them as local arc-length positions
            if (bSplineMayHaveInflection(bspline))
            {
                var nCPs           = size(bspline.controlPoints);
                var rawInflections = findBSplineInflections(bspline, 4 * nCPs, 1e-4);

                for (var u_inf in rawInflections)
                {
                    // Convert BSpline parameter to arc-length fraction, then to physical arc-length
                    var physFrac      = arcLengthFraction(arcLengthTable, u_inf);
                    var physArcLength = physFrac * length;  // arc from BSpline param=uMin

                    // Convert to local arc (from traversal start)
                    var localArc = stdDir ? physArcLength : (length - physArcLength);
                    localInflectionArcs = append(localInflectionArcs, localArc);
                }

                // Sort ascending by local arc-length
                localInflectionArcs = sort(localInflectionArcs, function(a, b)
                {
                    return (a - b) / meter;
                });
            }
        }

        edgeData = append(edgeData, {
            "query"              : edge,
            "bspline"            : bspline,
            "stdDir"             : stdDir,
            "isLine"             : isLine,
            "length"             : length,
            "arcLengthTable"     : arcLengthTable,
            "localInflectionArcs": localInflectionArcs,
            "lineFrame"          : lineFrame,
            "lineStartPt"        : lineStartPt
        });
    }

    // 4. Propagate cumulative startArcLength and startSign across edges
    var runningArc  = 0 * meter;
    var runningSign = 1;

    for (var i = 0; i < size(edgeData); i += 1)
    {
        edgeData[i] = mergeMaps(edgeData[i], {
            "startArcLength": runningArc,
            "startSign"     : runningSign
        });

        runningArc += edgeData[i].length;

        // Each inflection flips the sign; an even count is a net identity
        if (size(edgeData[i].localInflectionArcs) % 2 == 1)
            runningSign = -1 * runningSign;
    }

    // 5. Compute total length by summing edges (avoids dependency on evPathLength)
    var totalLength = 0 * meter;
    for (var ed in edgeData)
        totalLength += ed.length;

    return {
        "path"       : path,
        "totalLength": totalLength,
        "edgeData"   : edgeData
    };
}


// ============================================================================
// getFrameAtArcLength
// ============================================================================

/**
 * Return a globally consistent Frenet frame at any arc-length along the path.
 *
 * Handles multi-edge chains, non-standard traversal (stdDir=false), and
 * inflection points. The normal (xAxis) sign is tracked cumulatively so it
 * never discontinuously flips across the entire chain.
 *
 * @param context    {Context}
 * @param frenetPath {map}           - result from buildFrenetPath
 * @param arcLength  {ValueWithUnits}- global arc-length position (clamped)
 * @returns {map} :
 *   "frame"     {CoordSystem} - zAxis=tangent, xAxis=sign-corrected normal
 *   "sign"      {number}      - current normal sign (+1 or -1)
 *   "edgeIndex" {number}      - index of the edge containing this position
 */
export function getFrameAtArcLength(context is Context, frenetPath is map, arcLength) returns map
{
    var edgeData    = frenetPath.edgeData;
    var totalLength = frenetPath.totalLength;

    // 1. Clamp arc-length to valid range
    var clampedArc = arcLength;
    if (arcLength < 0 * meter)     { clampedArc = 0 * meter; }
    else if (arcLength > totalLength) { clampedArc = totalLength; }

    // 2. Find the edge whose span contains clampedArc
    //    (last edge where startArcLength <= clampedArc)
    var edgeIdx = 0;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        if (edgeData[i].startArcLength <= clampedArc)
            edgeIdx = i;
    }

    var edgeDat = edgeData[edgeIdx];

    // 3. Local arc-length within this edge (from its traversal start)
    var localArc = clampedArc - edgeDat.startArcLength;

    // 4. Count inflections we have passed (localInflectionArcs <= localArc)
    var inflectionsBefore = 0;
    for (var infArc in edgeDat.localInflectionArcs)
    {
        if (infArc <= localArc)
            inflectionsBefore += 1;
    }

    // 5. Effective normal sign at this position
    var sign = edgeDat.startSign;
    if (inflectionsBefore % 2 == 1)
        sign = -1 * sign;

    var frame;

    if (edgeDat.isLine)
    {
        // 6a. Line: interpolate position along traversal direction
        var position = edgeDat.lineStartPt + localArc * edgeDat.lineFrame.zAxis;
        frame = coordSystem(position, sign * edgeDat.lineFrame.xAxis, edgeDat.lineFrame.zAxis);
    }
    else
    {
        // 6b. Curved: convert local arc-length to BSpline parameter
        var u;
        if (edgeDat.stdDir)
        {
            u = parameterAtArcLength(edgeDat.arcLengthTable, localArc);
        }
        else
        {
            // Traversal is param 1→0; localArc=0 corresponds to uMax
            u = parameterAtArcLength(edgeDat.arcLengthTable, edgeDat.length - localArc);
        }

        var rawResult = computeFrenetFrame(edgeDat.bspline, u);

        if (!edgeDat.stdDir)
        {
            // Flip zAxis so it points in the traversal direction (param 1→0)
            frame = coordSystem(rawResult.frame.origin,
                                rawResult.frame.xAxis,
                                -1 * rawResult.frame.zAxis);
        }
        else
        {
            frame = rawResult.frame;
        }

        // Apply cumulative normal sign correction to xAxis
        frame = coordSystem(frame.origin, sign * frame.xAxis, frame.zAxis);
    }

    return {
        "frame"    : frame,
        "sign"     : sign,
        "edgeIndex": edgeIdx
    };
}


// ============================================================================
// projectOntoFrenetPath  (internal helper)
// ============================================================================

/**
 * Project a point onto a FrenetPath and return the global arc-length position.
 *
 * Tests each edge's BSpline, picks the closest, then converts the BSpline
 * parameter to arc-length accounting for traversal direction.
 *
 * @param frenetPath {map}    - result from buildFrenetPath
 * @param point      {Vector} - query point with units
 * @returns {ValueWithUnits}  - arc-length along the path
 */
function projectOntoFrenetPath(frenetPath is map, point is Vector)
{
    var edgeData    = frenetPath.edgeData;
    var bestDist    = inf * meter;
    var bestEdgeIdx = 0;
    var bestParam   = 0;

    for (var i = 0; i < size(edgeData); i += 1)
    {
        var result = projectPointOnCurve(edgeData[i].bspline, point, {});
        if (result.distance < bestDist)
        {
            bestDist    = result.distance;
            bestEdgeIdx = i;
            bestParam   = result.parameter;
        }
    }

    var edgeDat = edgeData[bestEdgeIdx];

    // Convert BSpline parameter → arc-length from BSpline uMin
    var physFrac      = arcLengthFraction(edgeDat.arcLengthTable, bestParam);
    var physArcLength = physFrac * edgeDat.length;

    // Convert to local arc from traversal start
    var localArc = edgeDat.stdDir ? physArcLength : (edgeDat.length - physArcLength);

    return edgeDat.startArcLength + localArc;
}


// ============================================================================
// getRefPoint  (internal helper)
// ============================================================================

/**
 * Extract a world point from a vertex, mate connector, or planar face query.
 *
 * @param context  {Context}
 * @param refQuery {Query} - vertex, mate connector, or planar face
 * @returns {Vector} - 3D position with units
 */
function getRefPoint(context is Context, refQuery is Query) returns Vector
{
    var pt = undefined;

    try silent { pt = evVertexPoint(context, { "vertex": refQuery }); }
    if (pt != undefined) return pt;

    try silent { pt = evMateConnector(context, { "mateConnector": refQuery }).origin; }
    if (pt != undefined) return pt;

    try silent { pt = evPlane(context, { "face": refQuery }).origin; }
    if (pt != undefined) return pt;

    throw regenError("Cannot evaluate reference point from selection");
}
