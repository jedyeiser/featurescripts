FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/approximationUtils.fs", version : "2892.0");

//import wrapCurve.fs
import(path : "6863116065bf5063633f30ac", version : "049d7fc5255da2e874b66c81");


/**
 * This is a new and improved version of an old feature. The new and improved version
 * uses functionality built in wrapCurve. This feature is simply an extension of that feature.
 *
 * User provides a selection of source edges. These must be G0 continuous.
 *
 * We will also test to see if the input curves are coplanar. This is dealt with in editing logic,
 * but it's an important aspect to bring up early.
 *
 * If the input curves are coplanar, the user does not need to specify a fromCurves query.
 * If the input curves are coplanar, we assume that the 'fromCurve is just a projection of the toCurve onto the
 * shared plane. We need to develop logic to do this. We need to make sure that we're first projecting each edge, and then only combining edges if they are colinear.
 * Note that this could cause cases where the fromCurve does not extend 'far enough' through our source edges. In this case,
 * use the zAxis (and associated measured coordinate) of the closest available frame to make sure that wrapping geometry is correct.
 * We axckowledge thaat this means any sourceEdges outside of our reference curves will basically be measured in a tranlated frame along the frame z axis.
 * our source edges to have a parameter on the 'theoretical curve'.
 *
 * The user specifies a source reference point. This is an important point as it specifies. how and where geometry might change in a subtle way.
 * This is the point along the fromCurves we reference.
 *
 * The user specifies toCurves - a group of G1 continuous curves (may be lines.arcs) to wrap our source curves around.
 *
 * The user specifies a toCurves reference point.
 *
 * The user supplies a sampling multiplier. WE sample each curve at this multiplier multiplied by the number of its control points. Minimum is 5.
 * User provides standard spline approximation input (tolerance, maxCPs, degree) to which all of our wrapped edges will be apprroximated.
 *
 * The user specifies a 'Primary offset'. We offset our wrapped curve (move normal to the frenet xAxis) by this amount and loft between the two curves.
 * If the user specifies 'secondDirection', we allow the user to specify a different length, which we will offset in the opposite direction.
 *
 * We loft a surface between the outermost curves.
 *
 * If user has 'keep Output curves' selected, an enum pops up KEEP_ALL or KEEP_WRAPPED.
 * If KEEP_ALL : keep the wrapped curve and the first (and if secondOffset and secondOffset  > 0 * millimeter, the second) offset curves. We use opExtractWires
 * to join the wires of each wrapped/offset curve collected in group. Delete the curves we'd previously created - we're now doubling up.
 * If KEEP_WRAPPED is selected, delete the offset curves but extract the wrapped curve as described above.
 *
 */


const ZERO_INCLUSIVE_OFFSET_BOUND = { (millimeter) : [0, 0, 100] } as LengthBoundSpec;

export enum OutputCurveMode
{
    annotation { "Name" : "Keep all" } KEEP_ALL,
    annotation { "Name" : "Keep wrapped only" } KEEP_WRAPPED
}


export function wrapAndLoftEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
  isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    // 1. Test planarity of sourceEdges
    // 2. Update definition.sourceEdgesArePlanar accordingly
    try
    {
        var edgeArray = evaluateQuery(context, definition.sourceEdges);
        if (size(edgeArray) > 0)
        {
            // Collect sample points from all source edges
            var allPoints = [];
            for (var i = 0; i < size(edgeArray); i += 1)
            {
                var tangentLines = evEdgeTangentLines(context, {
                    "edge"       : edgeArray[i],
                    "parameters" : [0, 0.33, 0.67, 1]
                });
                for (var j = 0; j < size(tangentLines); j += 1)
                {
                    allPoints = append(allPoints, tangentLines[j].origin);
                }
            }

            if (size(allPoints) >= 3)
            {
                // Pick three well-separated points to define a candidate plane
                var p0     = allPoints[0];
                var midIdx = floor(size(allPoints) / 2);
                var p1     = allPoints[midIdx];
                var p2     = allPoints[size(allPoints) - 1];

                var v1        = p1 - p0;
                var v2        = p2 - p0;
                var normalVec = cross(v1, v2);

                // norm(normalVec) has units m²; .value gives SI scalar
                if (norm(normalVec).value > 1e-15)
                {
                    var normalDir = normalize(normalVec);  // unitless

                    var maxDev = 0 * meter;
                    for (var k = 0; k < size(allPoints); k += 1)
                    {
                        var dev = abs(dot(allPoints[k] - p0, normalDir));
                        if (dev > maxDev)
                        {
                            maxDev = dev;
                        }
                    }

                    definition.sourceEdgesArePlanar = (maxDev < 0.1 * millimeter);
                }
            }
        }
    }

    return definition;
}

 annotation { "Feature Type Name" : "Wrap and Loft", "Feature Type Description" : "Takes source edges and a wrapping definition to 'loft a surface' from wrapped versions of the source curves.", "Editing Logic Function" : "wrapAndLoftEditingLogic" }
 export const wrapAndLoft = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "Wrap edges", "Filter" : EntityType.EDGE, "Decription" : "Edges that we will wrap and create a loft through"}
         definition.sourceEdges is Query;

         annotation { "Name" : "areSourceEdgesPlanar", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
         definition.sourceEdgesArePlanar is boolean;

         annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            if (!definition.sourceEdgesArePlanar)
            {
                annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference)" }
                definition.fromEdges is Query;
            }

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

        annotation { "Group Name" : "Offset", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Primary offset" }
            isLength(definition.primaryOffset, ZERO_INCLUSIVE_OFFSET_BOUND);

            annotation { "Name" : "Second direction", "Default" : false }
            definition.secondDirection is boolean;

            if (definition.secondDirection)
            {
                annotation { "Name" : "Second offset" }
                isLength(definition.secondOffset, ZERO_INCLUSIVE_OFFSET_BOUND);
            }
        }

        annotation { "Name" : "Keep output curves", "Default" : false }
        definition.keepOutputCurves is boolean;

        if (definition.keepOutputCurves)
        {
            annotation { "Name" : "Output curve mode" }
            definition.outputCurveMode is OutputCurveMode;
        }

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
        // ===== Approximation / sampling options (defaults when showAdvanced is off) =====
        var degree = 3;
        if (definition.approximationDegree != undefined)
        {
            degree = definition.approximationDegree;
        }

        var samplingDensity = 1 * millimeter;
        if (definition.samplingDensity != undefined)
        {
            samplingDensity = definition.samplingDensity;
        }

        // ===== Build to-path =====
        var toFrenetPath = buildFrenetPath(context, id, definition.toEdges, definition.flipTo);

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
        {
            debugDrawFrames(context, toFrenetPath, 10);
        }

        // ===== Build from-path =====
        // Non-planar: use definition.fromEdges directly.
        // Planar: project each toEdge onto the source-edge plane and build fromFrenetPath from those curves.
        var fromFrenetPath     = undefined;
        var projectedBodyQuery = undefined;

        if (definition.sourceEdgesArePlanar)
        {
            // 1. Fit plane to sourceEdges (same sample-point approach as editing logic)
            var srcEdgeArray = evaluateQuery(context, definition.sourceEdges);
            var allSrcPts    = [];
            for (var i = 0; i < size(srcEdgeArray); i += 1)
            {
                var tls = evEdgeTangentLines(context, {
                    "edge"       : srcEdgeArray[i],
                    "parameters" : [0, 0.33, 0.67, 1]
                });
                for (var j = 0; j < size(tls); j += 1)
                {
                    allSrcPts = append(allSrcPts, tls[j].origin);
                }
            }

            var planeOrigin = allSrcPts[0];
            var midSrcIdx   = floor(size(allSrcPts) / 2);
            var planeNormal = normalize(cross(
                allSrcPts[midSrcIdx]             - planeOrigin,
                allSrcPts[size(allSrcPts) - 1]  - planeOrigin
            ));

            // 2. For each toEdge, sample uniformly, project onto source plane, fit a spline
            var toEdgeArray          = evaluateQuery(context, definition.toEdges);
            var projectedEdgeQueries = [];
            var projectedBodies      = [];

            for (var tei = 0; tei < size(toEdgeArray); tei += 1)
            {
                var toBSpline  = evApproximateBSplineCurve(context, { "edge": toEdgeArray[tei] });
                var toArcTable = buildArcLengthTable(toBSpline, 200);
                var nProj      = max([5, ceil(toArcTable.totalLength / samplingDensity) + 1]);
                var toSamples  = uniformArcLengthSamples(toBSpline, nProj, {});

                var projPoints = [];
                for (var k = 0; k < size(toSamples.points); k += 1)
                {
                    var pt   = toSamples.points[k];
                    var dist = dot(pt - planeOrigin, planeNormal);
                    projPoints = append(projPoints, pt - dist * planeNormal);
                }

                var projApproxDef = {
                    "targets"          : [approximationTarget({ "positions": projPoints })],
                    "tolerance"        : definition.approximationTolerance,
                    "maxControlPoints" : definition.approximationMaxCPs,
                    "degree"           : degree
                };
                var projCurve = approximateSpline(context, projApproxDef)[0];

                var projId = id + "projectedFrom" + ("curve" ~ toString(tei));
                opCreateBSplineCurve(context, projId, { "bSplineCurve": projCurve });
                projectedEdgeQueries = append(projectedEdgeQueries, qCreatedBy(projId, EntityType.EDGE));
                projectedBodies      = append(projectedBodies,      qCreatedBy(projId, EntityType.BODY));
            }

            // 3. Build fromFrenetPath from the projected edges
            fromFrenetPath     = buildFrenetPath(context, id, qUnion(projectedEdgeQueries), false);
            projectedBodyQuery = qUnion(projectedBodies);
        }
        else
        {
            // Non-planar: user provides fromEdges explicitly
            fromFrenetPath = buildFrenetPath(context, id, definition.fromEdges, false);
        }

        if (definition.debugFromBSplines)
        {
            var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
            println("From path: " ~ toString(size(fromFrenetPath.edgeData)) ~ " edge(s), totalLength = " ~ toString(fromFrenetPath.totalLength));
            for (var j = 0; j < size(fromFrenetPath.edgeData); j += 1)
            {
                printBSpline(fromFrenetPath.edgeData[j].bspline, fmt, ["  -- From edge " ~ toString(j) ~ " --"]);
            }
        }

        // ===== Resolve reference alignment arc-lengths =====
        var fromRefPt  = getRefPoint(context, definition.fromRef);
        var toRefPt    = getRefPoint(context, definition.toRef);
        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, fromRefPt);
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   toRefPt);

        // ===== Isolated from-line xAxis fix =====
        // Lines adjacent to a curve got a curve-context xAxis in buildFrenetPath step 4.5;
        // isolated lines (no curve neighbor) borrow the to-path normal at the mapped arc-length.
        var fromEdgeData = fromFrenetPath.edgeData;
        for (var i = 0; i < size(fromEdgeData); i += 1)
        {
            var ed = fromEdgeData[i];
            if (!ed.isLine)
            {
                continue;
            }

            var hasCurveCtx = (i > 0 && !fromEdgeData[i - 1].isLine) ||
                              (i + 1 < size(fromEdgeData) && !fromEdgeData[i + 1].isLine);
            if (hasCurveCtx)
            {
                continue;
            }

            var midFromArc = ed.startArcLength + ed.length / 2;
            var midToArc   = toRefArc + (midFromArc - fromRefArc);
            var toXAxis    = getFrameAtArcLength(context, toFrenetPath, midToArc).frame.xAxis;

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
        {
            debugDrawFrames(context, fromFrenetPath, 10);
        }

        // ===== Main wrapping loop =====
        var sourceCurveArray = evaluateQuery(context, definition.sourceEdges);
        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            var srcBSpline = evApproximateBSplineCurve(context, { "edge": sourceCurveArray[i] });

            var srcArcTable = buildArcLengthTable(srcBSpline, 200);
            var numSamples  = max([5, ceil(srcArcTable.totalLength / samplingDensity) + 1]);

            var samples   = uniformArcLengthSamples(srcBSpline, numSamples, {});
            var srcPoints = samples.points;

            if (definition.debugSourceBSplines)
            {
                var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                println("Source curve " ~ toString(i) ~ ": " ~ toString(size(srcPoints)) ~
                        " samples, length = " ~ toString(srcArcTable.totalLength));
                printBSpline(srcBSpline, fmt, ["Source curve " ~ toString(i)]);
            }

            // Map each sampled point through the Frenet frame transformation;
            // record which to-edge each mapped point lands on for span splitting
            var mappedData = [];
            for (var sIdx = 0; sIdx < size(srcPoints); sIdx += 1)
            {
                var pt = srcPoints[sIdx];

                // Project source point onto from-path; get Frenet frame there
                var s_from     = projectOntoFrenetPath(fromFrenetPath, pt);
                var fromResult = getFrameAtArcLength(context, fromFrenetPath, s_from);

                // Express point in from-frame local coordinates
                var localCoords = worldPointToFrenet(pt, fromResult);

                // Linear arc-length mapping from from-path to to-path
                var s_to = toRefArc + (s_from - fromRefArc);

                // Get to-frame at mapped arc-length
                var toResult = getFrameAtArcLength(context, toFrenetPath, s_to);

                // Apply flipToNormal toggle then reconcile normal sign
                var toSign = toResult.sign;
                if (definition.flipToNormal)
                {
                    toSign = -1 * toSign;
                }

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
                    "point"    : frenetPointToWorld(localCoords, toFrameResult),
                    "sFrom"    : s_from
                });
            }

            // Emit one output curve per to-edge span (prevents ringing at line/curve joints)
            var segStartIdx     = 0;
            var segCount        = 0;
            var junctionPt      = undefined;
            var junctionTangent = undefined;

            while (segStartIdx < size(mappedData))
            {
                // Collect the run of consecutive points on the same to-edge
                var currentEdge = mappedData[segStartIdx].edgeIndex;
                var segEndIdx   = segStartIdx;
                while (segEndIdx + 1 < size(mappedData) && mappedData[segEndIdx + 1].edgeIndex == currentEdge)
                {
                    segEndIdx += 1;
                }

                var segPoints = [];

                // Capture carry-over tangent before clearing for this span
                var carryOverTangent = junctionTangent;
                junctionTangent = undefined;

                // Prepend exact junction point carried from end of previous span
                if (junctionPt != undefined)
                {
                    segPoints = append(segPoints, junctionPt);
                }
                junctionPt = undefined;

                for (var k = segStartIdx; k <= segEndIdx; k += 1)
                {
                    segPoints = append(segPoints, mappedData[k].point);
                }

                // Inject exact boundary point at the junction to the next span
                if (segEndIdx + 1 < size(mappedData))
                {
                    var nextEdgeIdx   = mappedData[segEndIdx + 1].edgeIndex;
                    var s_to_boundary = toFrenetPath.edgeData[nextEdgeIdx].startArcLength;

                    // Invert arc-length mapping to get from-path position at boundary
                    var s_from_junction = fromRefArc + (s_to_boundary - toRefArc);
                    if (s_from_junction < 0 * meter)
                    {
                        s_from_junction = 0 * meter;
                    }
                    if (s_from_junction > fromFrenetPath.totalLength)
                    {
                        s_from_junction = fromFrenetPath.totalLength;
                    }

                    // Interpolate source arc-length between bracketing samples
                    var sFrom_k   = mappedData[segEndIdx].sFrom;
                    var sFrom_kp1 = mappedData[segEndIdx + 1].sFrom;
                    var t = (s_from_junction - sFrom_k) / (sFrom_kp1 - sFrom_k);
                    if (t < 0)
                    {
                        t = 0;
                    }
                    if (t > 1)
                    {
                        t = 1;
                    }
                    var s_src_junction = samples.arcLengths[segEndIdx] +
                                         t * (samples.arcLengths[segEndIdx + 1] - samples.arcLengths[segEndIdx]);

                    // Evaluate source curve at junction arc-length
                    var u_junction       = parameterAtArcLength(srcArcTable, s_src_junction);
                    var srcJunctionFrame = computeFrenetFrame(srcBSpline, u_junction);
                    var pt_junction      = srcJunctionFrame.frame.origin;
                    var srcTangent       = srcJunctionFrame.frame.zAxis;

                    // Map through frames with same sign-reconciliation as main loop
                    var fromResult_j  = getFrameAtArcLength(context, fromFrenetPath, s_from_junction);
                    var localCoords_j = worldPointToFrenet(pt_junction, fromResult_j);
                    var toResult_j    = getFrameAtArcLength(context, toFrenetPath, s_to_boundary);
                    var toSign_j      = toResult_j.sign;
                    if (definition.flipToNormal)
                    {
                        toSign_j = -1 * toSign_j;
                    }
                    var toFrameResult_j = toResult_j;
                    if (toSign_j != fromResult_j.sign)
                    {
                        toFrameResult_j = mergeMaps(toResult_j, { "frame":
                            coordSystem(toResult_j.frame.origin, -1 * toResult_j.frame.xAxis, toResult_j.frame.zAxis) });
                    }
                    var junctionWorldPt = frenetPointToWorld(localCoords_j, toFrameResult_j);

                    // Transform source tangent through Frenet frames (direction-only)
                    var fTangential        = dot(srcTangent, fromResult_j.frame.zAxis);
                    var fNormal            = dot(srcTangent, fromResult_j.frame.xAxis);
                    var fBinormal          = dot(srcTangent, yAxis(fromResult_j.frame));
                    var junctionTangentDir = fTangential * toFrameResult_j.frame.zAxis +
                                            fNormal     * toFrameResult_j.frame.xAxis +
                                            fBinormal   * yAxis(toFrameResult_j.frame);

                    segPoints       = append(segPoints, junctionWorldPt);
                    junctionPt      = junctionWorldPt;
                    junctionTangent = junctionTangentDir;
                }

                if (size(segPoints) >= degree + 1)
                {
                    // Scale for derivative constraints: average inter-point spacing
                    var approxScale = norm(segPoints[size(segPoints) - 1] - segPoints[0]) /
                                      max([1, size(segPoints) - 1]);

                    var targetDef = { "positions": segPoints };
                    if (carryOverTangent != undefined)
                    {
                        targetDef = mergeMaps(targetDef, { "startDerivative": carryOverTangent * approxScale });
                    }
                    if (junctionTangent != undefined)
                    {
                        targetDef = mergeMaps(targetDef, { "endDerivative": junctionTangent * approxScale });
                    }

                    var approxDef = {
                        "targets"          : [approximationTarget(targetDef)],
                        "tolerance"        : definition.approximationTolerance,
                        "maxControlPoints" : definition.approximationMaxCPs,
                        "degree"           : degree
                    };
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

        // ===== Cleanup planar projected from-curves =====
        if (projectedBodyQuery != undefined)
        {
            opDeleteBodies(context, id + "cleanupProjected", { "entities" : projectedBodyQuery });
        }

        // Out of scope this session: primaryOffset / secondOffset, lofting, keepOutputCurves / opExtractWires
     });


// ============================================================================
// debugDrawFrames  (internal helper — mirrors the one in wrapCurve.fs)
// ============================================================================
/**
 * Draw Frenet frames at evenly-spaced arc-length positions along a FrenetPath.
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
