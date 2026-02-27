FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/approximationUtils.fs", version : "2878.0");
import(path : "onshape/std/path.fs", version : "2878.0");


//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
//import Utils
import(path : "ad98c7f43a25a4c0e8a428e7", version : "0420f4e96d9a3fd9e600557a");
// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");
// IMPORT: tools/frenet.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
// IMPORT: tools/point_projection.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/eb46317a27a44e391e11dfe6", version : "0cea3c8d27e4f7fd660aa69f");
// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");
// IMPORT: curveMappingCore.fs
import(path : "683d867c35fdab9c98d47556", version : "851c0c6ec53551f9e1760bf6");




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

            annotation { "Name" : "Print wrap detail",
                        "Description" : "For each sample point print: source point, from-frame (origin/xAxis/zAxis), to-frame (origin/xAxis/zAxis), and mapped output point. Use to verify Frenet mapping vs. approximation error.",
                        "Default" : false }
            definition.printWrapDetails is boolean;

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

        if (definition.debugShowToFrames)
            debugDrawFrames(context, toFrenetPath, 10);

        // 2. Resolve reference alignment arc-lengths
        var fromRefPt  = getRefPoint(context, definition.fromRef);
        var toRefPt    = getRefPoint(context, definition.toRef);
        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, fromRefPt);
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   toRefPt);

        // Fix 1: Align isolated from-line xAxes with to-path normal.
        // Must run after fromRefArc/toRefArc are resolved.
        // Lines adjacent to a curve already got a curve-context xAxis in buildFrenetPath step 4.5;
        // this handles the isolated-line case (no curve neighbor) by borrowing the to-path normal.
        var fromEdgeData = fromFrenetPath.edgeData;
        for (var i = 0; i < size(fromEdgeData); i += 1)
        {
            var ed = fromEdgeData[i];
            if (!ed.isLine)
                continue;

            // Skip lines that already received a curve-context xAxis in step 4.5
            var hasCurveCtx = (i > 0 && !fromEdgeData[i - 1].isLine) ||
                              (i + 1 < size(fromEdgeData) && !fromEdgeData[i + 1].isLine);
            if (hasCurveCtx)
                continue;

            // Map mid-arc of this from-edge to to-path position
            var midFromArc = ed.startArcLength + ed.length / 2;
            var midToArc   = toRefArc + (midFromArc - fromRefArc);
            var toXAxis    = getFrameAtArcLength(context, toFrenetPath, midToArc).frame.xAxis;

            // Project to-xAxis onto the plane perpendicular to the from-edge tangent
            var tangent   = ed.lineFrame.zAxis;
            var perpXAxis = toXAxis - dot(toXAxis, tangent) * tangent;
            if (norm(perpXAxis) > 1e-6)
            {
                fromEdgeData[i] = mergeMaps(ed, {
                    "lineFrame": coordSystem(ed.lineFrame.origin, normalize(perpXAxis), tangent)
                });
            }
        }
        fromFrenetPath = mergeMaps(fromFrenetPath, { "edgeData": fromEdgeData });

        if (definition.debugShowFromFrames)
            debugDrawFrames(context, fromFrenetPath, 10);

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
            for (var sIdx = 0; sIdx < size(srcPoints); sIdx += 1)
            {
                var pt = srcPoints[sIdx];

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

                var toPoint = frenetPointToWorld(localCoords, toFrameResult);
                if (definition.printWrapDetails)
                    println("src=" ~ toString(pt) ~ " s_from=" ~ toString(s_from) ~
                            " | from: orig=" ~ toString(fromResult.frame.origin) ~
                            " x=" ~ toString(fromResult.frame.xAxis) ~
                            " z=" ~ toString(fromResult.frame.zAxis) ~
                            " | s_to=" ~ toString(s_to) ~
                            " to: orig=" ~ toString(toFrameResult.frame.origin) ~
                            " x=" ~ toString(toFrameResult.frame.xAxis) ~
                            " z=" ~ toString(toFrameResult.frame.zAxis) ~
                            " | out=" ~ toString(toPoint));
                mappedData = append(mappedData, {
                    "edgeIndex": toResult.edgeIndex,
                    "point"    : toPoint,
                    "sFrom"    : s_from
                });
            }

            // Emit one output curve per to-edge span (prevents ringing at line/curve joints)
            var segStartIdx = 0;
            var segCount    = 0;
            var junctionPt      = undefined;  // carry-over exact junction point between spans
            var junctionTangent = undefined;  // carry-over junction tangent direction in to-space

            while (segStartIdx < size(mappedData))
            {
                // Collect the run of consecutive points on the same to-edge
                var currentEdge = mappedData[segStartIdx].edgeIndex;
                var segEndIdx   = segStartIdx;
                while (segEndIdx + 1 < size(mappedData) && mappedData[segEndIdx + 1].edgeIndex == currentEdge)
                    segEndIdx += 1;

                var segPoints = [];

                // Capture carry-over tangent from previous span's junction before clearing
                var carryOverTangent = junctionTangent;
                junctionTangent = undefined;

                // Prepend exact junction point carried over from end of previous span
                if (junctionPt != undefined)
                    segPoints = append(segPoints, junctionPt);
                junctionPt = undefined;

                for (var k = segStartIdx; k <= segEndIdx; k += 1)
                    segPoints = append(segPoints, mappedData[k].point);

                // Inject exact boundary point at the junction to the next span
                if (segEndIdx + 1 < size(mappedData))
                {
                    var nextEdgeIdx   = mappedData[segEndIdx + 1].edgeIndex;
                    // Use the higher-index edge's startArcLength — works for both forward and backward transitions
                    var boundaryEdgeIdx = max([currentEdge, nextEdgeIdx]);
                    var s_to_boundary = toFrenetPath.edgeData[boundaryEdgeIdx].startArcLength;

                    // Invert arc-length mapping to get from-path position at boundary
                    var s_from_junction = fromRefArc + (s_to_boundary - toRefArc);
                    if (s_from_junction < 0 * meter)
                        s_from_junction = 0 * meter;
                    if (s_from_junction > fromFrenetPath.totalLength)
                        s_from_junction = fromFrenetPath.totalLength;

                    // Interpolate source arc-length between bracketing samples
                    var sFrom_k   = mappedData[segEndIdx].sFrom;
                    var sFrom_kp1 = mappedData[segEndIdx + 1].sFrom;
                    var t = (s_from_junction - sFrom_k) / (sFrom_kp1 - sFrom_k);
                    if (t < 0) t = 0;
                    if (t > 1) t = 1;
                    var s_src_junction = samples.arcLengths[segEndIdx] +
                                         t * (samples.arcLengths[segEndIdx + 1] - samples.arcLengths[segEndIdx]);

                    // Evaluate source curve at junction arc-length
                    var u_junction       = parameterAtArcLength(srcArcTable, s_src_junction);
                    var srcJunctionFrame = computeFrenetFrame(srcBSpline, u_junction);
                    var pt_junction      = srcJunctionFrame.frame.origin;
                    var srcTangent       = srcJunctionFrame.frame.zAxis;  // unit tangent on source curve

                    // Map through frames with same sign-reconciliation as main loop
                    var fromResult_j  = getFrameAtArcLength(context, fromFrenetPath, s_from_junction);
                    var localCoords_j = worldPointToFrenet(pt_junction, fromResult_j);
                    var toResult_j    = getFrameAtArcLength(context, toFrenetPath, s_to_boundary);
                    var toSign_j      = toResult_j.sign;
                    if (definition.flipToNormal) toSign_j = -1 * toSign_j;
                    var toFrameResult_j = toResult_j;
                    if (toSign_j != fromResult_j.sign)
                    {
                        toFrameResult_j = mergeMaps(toResult_j, { "frame":
                            coordSystem(toResult_j.frame.origin, -1 * toResult_j.frame.xAxis, toResult_j.frame.zAxis) });
                    }
                    var junctionWorldPt = frenetPointToWorld(localCoords_j, toFrameResult_j);

                    // Transform source tangent through Frenet frames (direction-only, no origin offset)
                    // Project world tangent onto from-frame axes, then reconstruct in to-frame
                    var fTangential        = dot(srcTangent, fromResult_j.frame.zAxis);
                    var fNormal            = dot(srcTangent, fromResult_j.frame.xAxis);
                    var fBinormal          = dot(srcTangent, yAxis(fromResult_j.frame));
                    var junctionTangentDir = fTangential * toFrameResult_j.frame.zAxis +
                                            fNormal     * toFrameResult_j.frame.xAxis +
                                            fBinormal   * yAxis(toFrameResult_j.frame);

                    // Append to current span; carry over to next span's start
                    segPoints       = append(segPoints, junctionWorldPt);
                    junctionPt      = junctionWorldPt;
                    junctionTangent = junctionTangentDir;
                }

                if (size(segPoints) >= degree + 1)
                {
                    // Scale for derivative constraints: total chord length of this segment.
                    // approximateSpline uses [0,1] parameterization, so the natural derivative
                    // magnitude at an endpoint is ~totalChord (velocity = length/param_range).
                    // Dividing by (n-1) would give per-sample spacing (~218× too small for 218
                    // points on a 0.3m arc), causing startDerivative to force near-zero velocity
                    // and produce a visible bulge of ~0.45mm near the junction.
                    var totalChord = 0 * meter;
                    for (var k = 0; k < size(segPoints) - 1; k += 1)
                        totalChord += norm(segPoints[k + 1] - segPoints[k]);
                    var approxScale = totalChord;

                    var targetDef = { "positions": segPoints };
                    if (carryOverTangent != undefined)
                        targetDef = mergeMaps(targetDef, { "startDerivative": carryOverTangent * approxScale });
                    if (junctionTangent != undefined)
                        targetDef = mergeMaps(targetDef, { "endDerivative": junctionTangent * approxScale });

                    var approxDef = {
                        "targets"            : [approximationTarget(targetDef)],
                        "tolerance"          : definition.approximationTolerance,
                        "maxControlPoints"   : definition.approximationMaxCPs,
                        "degree"             : degree,
                        "isPeriodic"         : false,
                        "interpolateIndices" : [0, size(segPoints) - 1] };
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
                else if (definition.debugWrappedCurves)
                {
                    println("  [skipped span " ~ toString(i) ~ "." ~ toString(segCount) ~
                            ": only " ~ toString(size(segPoints)) ~ " points, need " ~ toString(degree + 1) ~ "]");
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
