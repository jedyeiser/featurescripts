FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/approximationUtils.fs", version : "2892.0");

// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");
// IMPORT: tools/frenet.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");
// IMPORT: curveMappingCore.fs
export import(path : "683d867c35fdab9c98d47556", version : "7010180c5e3be2311ad5359e");

//import wrapCurve.fs
import(path : "6863116065bf5063633f30ac", version : "e692ba3166b7cd2cbc1a7da2");



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
        var edgeArray = evaluateQuery(context, expandEdgeQuery(definition.sourceEdges));
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
         annotation { "Name" : "Wrap edges",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "Description" : "Edges to wrap and create a loft through. Accepts edges, wire bodies, or composite parts containing wire bodies." }
         definition.sourceEdges is Query;

         annotation { "Name" : "areSourceEdgesPlanar", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
         definition.sourceEdgesArePlanar is boolean;

         annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            if (!definition.sourceEdgesArePlanar)
            {
                annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
                definition.fromEdges is Query;
            }

            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on from curve" }
            definition.fromRef is Query;
        }

        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
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

        annotation { "Group Name" : "Advanced options", "Collapsed By Default" : true }
        {
            
            annotation { "Group Name" : "Sampling options", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Source sampling mode", "Default" : SamplingMode.LENGTH_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if source edges should be sampled based on length between sampling points, or as an integer multiple of the source edge control points" }
                definition.sourceSamplingMode is SamplingMode;
                
                if (definition.sourceSamplingMode == SamplingMode.CP_BASED)
                {
                    annotation { "Name" : "Source CP multiplier", "Description" : "Samples per source edge control point" }
                    isInteger(definition.sourceCPMultiplier, cpMultiplierBounds);
                }
                else
                {
                    annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
                    isLength(definition.samplingDensity, samplingDensityBounds);
                }
    
                annotation { "Name" : "Reference sampling mode", "Default" : SamplingMode.LENGTH_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if reference edges  (to/from curves) should be sampled based on length between sampling points, or as an integer multiple of the source edge control points"  }
                definition.referenceSamplingMode is SamplingMode;
    
                if (definition.referenceSamplingMode == SamplingMode.CP_BASED)
                {
                    annotation { "Name" : "Reference CP multiplier", "Description" : "Samples per reference edge control point" }
                    isInteger(definition.referenceCPMultiplier, cpMultiplierBounds);
                }
                else
                {
                    annotation { "Name" : "Reference sampling density", "Description" : "Distance between sample points along reference edges" }
                    isLength(definition.referenceSamplingDensity, samplingDensityBounds);
                }
            }
            
            annotation { "Group Name" : "Spline approximation options", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
                isInteger(definition.approximationDegree, DEGREE_BOUND);

                annotation { "Name" : "Keep source degree", "Default" : false, "Description" : "When true, uses the maximum of the source curve degree and the target degree" }
                definition.keepDegree is boolean;

                annotation { "Name" : "Maximum control points" }
                isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

                annotation { "Name" : "Tolerance" }
                isLength(definition.approximationTolerance, TOLERANCE_BOUND);
            }
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
        // ===== Approximation / sampling options =====
        var samplingDensity = definition.samplingDensity;
        var referenceSamplingDensity = definition.referenceSamplingDensity;

        // ===== Build to-path =====
        var toFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.toEdges), definition.flipTo);

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
            var srcEdgeArray = evaluateQuery(context, expandEdgeQuery(definition.sourceEdges));
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
            var toEdgeArray          = evaluateQuery(context, expandEdgeQuery(definition.toEdges));
            var projectedEdgeQueries = [];
            var projectedBodies      = [];

            for (var tei = 0; tei < size(toEdgeArray); tei += 1)
            {
                var nProj;
                if (definition.referenceSamplingMode == SamplingMode.CP_BASED)
                {
                    var refBSpline = evApproximateBSplineCurve(context, { "edge": toEdgeArray[tei] });
                    nProj = max([10, definition.referenceCPMultiplier * size(refBSpline.controlPoints)]);
                }
                else
                {
                    var edgeLen = evLength(context, { "entities": toEdgeArray[tei] });
                    nProj = max([10, ceil(edgeLen / referenceSamplingDensity) + 1]);
                }
                var toSamples_origins = mapArray(evEdgeTangentLines(context, {
                    "edge"       : toEdgeArray[tei],
                    "parameters" : range(0, 1, nProj)
                }), function(x) { return x.origin; });

                var projPoints = [];
                for (var k = 0; k < size(toSamples_origins); k += 1)
                {
                    var pt   = toSamples_origins[k];
                    var dist = dot(pt - planeOrigin, planeNormal);
                    projPoints = append(projPoints, pt - dist * planeNormal);
                }

                var projApproxDef = {
                    "targets"            : [approximationTarget({ "positions": projPoints })],
                    "tolerance"          : definition.approximationTolerance,
                    "maxControlPoints"   : definition.approximationMaxCPs,
                    "degree"             : degree,
                    "isPeriodic"         : false,
                    "interpolateIndices" : [0, size(projPoints) - 1]
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
            fromFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.fromEdges), false);
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
        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, fromRefPt, undefined).arcLength;
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   toRefPt,   undefined).arcLength;

        // ===== Isolated from-line xAxis fix =====
        // Lines adjacent to a curve got a curve-context xAxis in buildFrenetPath step 4.5;
        // isolated lines (no curve neighbor) borrow the to-path normal at the mapped arc-length.
        //
        // In the planar case, near-linear to-paths produce near-linear projected BSplines that
        // buildFrenetPath classifies as BSplines (not CurveType.LINE), giving arbitrary xAxes.
        // Pass 1 promotes such effectively-linear BSplines to line mode so pass 2 can fix them.
        var fromEdgeData = fromFrenetPath.edgeData;

        // Pass 1 — promote effectively-linear BSplines to line mode
        for (var i = 0; i < size(fromEdgeData); i += 1)
        {
            var ed = fromEdgeData[i];
            if (ed.isLine)  continue;  // already exact line, skip

            var cps     = ed.bspline.controlPoints;
            var n       = size(cps);
            var p0      = cps[0];
            var p1      = cps[n - 1];
            var chord    = p1 - p0;
            var chordLen = norm(chord);

            if (chordLen.value < 1e-10)  continue;  // degenerate edge, leave alone

            var chordDir = normalize(chord);
            var maxDev   = 0 * meter;
            for (var j = 1; j < n - 1; j += 1)
            {
                var diff    = cps[j] - p0;
                var lateral = norm(diff - dot(diff, chordDir) * chordDir);
                if (lateral > maxDev)  maxDev = lateral;
            }

            if (maxDev >= 0.001 * ed.length)  continue;  // well-curved, leave alone

            // Promote to line mode
            var traversalStartPt = ed.stdDir ? cps[0]     : cps[n - 1];
            var traversalEndPt   = ed.stdDir ? cps[n - 1] : cps[0];
            var tangent          = normalize(traversalEndPt - traversalStartPt);

            // Placeholder xAxis — perpendicular to tangent, overwritten in pass 2
            var refVec    = (abs(dot(tangent, vector(1, 0, 0))) < 0.9) ? vector(1, 0, 0) : vector(0, 1, 0);
            var tempXAxis = normalize(refVec - dot(refVec, tangent) * tangent);

            fromEdgeData[i] = mergeMaps(ed, {
                "isLine"              : true,
                "lineStartPt"         : traversalStartPt,
                "lineFrame"           : coordSystem(traversalStartPt, tempXAxis, tangent),
                "localInflectionArcs" : []   // clear spurious inflections from near-linear BSpline
            });

            if (definition.debugFromBSplines)
            {
                println("  Pass1: promoted edge " ~ toString(i) ~
                        " length=" ~ toString(ed.length) ~
                        " maxDev=" ~ toString(maxDev) ~
                        " tangent=" ~ toString(tangent));
            }
        }

        // Pass 2 — borrow xAxis from to-path for all isolated lines
        // (exact lines from buildFrenetPath + newly-promoted lines from pass 1)
        for (var i = 0; i < size(fromEdgeData); i += 1)
        {
            var ed = fromEdgeData[i];
            if (!ed.isLine)  continue;

            var hasCurveCtx = (i > 0 && !fromEdgeData[i - 1].isLine) ||
                              (i + 1 < size(fromEdgeData) && !fromEdgeData[i + 1].isLine);
            if (hasCurveCtx)  continue;

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
        var sourceCurveArray              = evaluateQuery(context, expandEdgeQuery(definition.sourceEdges));
        var allWrappedSegQueries          = [];
        var allPrimaryOffsetSegQueries    = [];
        var allSecondaryOffsetSegQueries  = [];
        var allWrappedSegBodies           = [];
        var allPrimaryOffsetSegBodies     = [];
        var allSecondaryOffsetSegBodies   = [];
        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            var srcLen     = evLength(context, { "entities": sourceCurveArray[i] });
            var srcBSpline = evApproximateBSplineCurve(context, { "edge": sourceCurveArray[i] });
            var degree = definition.keepDegree
                ? max([definition.approximationDegree, srcBSpline.degree])
                : definition.approximationDegree;

            var numSamples;
            if (definition.sourceSamplingMode == SamplingMode.CP_BASED)
            {
                numSamples = max([10, definition.sourceCPMultiplier * size(srcBSpline.controlPoints)]);
            }
            else
            {
                numSamples = max([10, ceil(srcLen / samplingDensity) + 1]);
            }

            var srcPoints = mapArray(evEdgeTangentLines(context, {
                "edge"       : sourceCurveArray[i],
                "parameters" : range(0, 1, numSamples)
            }), function(x) { return x.origin; });

            // Build chord-based arc-length array
            var srcArcLengths = [0 * meter];
            for (var k = 1; k < size(srcPoints); k += 1)
            {
                srcArcLengths = append(srcArcLengths, srcArcLengths[k - 1] + norm(srcPoints[k] - srcPoints[k - 1]));
            }

            if (definition.debugSourceBSplines)
            {
                var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                println("Source curve " ~ toString(i) ~ ": " ~ toString(size(srcPoints)) ~
                        " samples, length = " ~ toString(srcLen));
                printBSpline(srcBSpline, fmt, ["Source curve " ~ toString(i)]);
            }

            // Map each sampled point through the Frenet frame transformation;
            // record which to-edge each mapped point lands on for span splitting
            var mappedData = [];
            for (var sIdx = 0; sIdx < size(srcPoints); sIdx += 1)
            {
                var pt = srcPoints[sIdx];

                // Project source point onto from-path; get Frenet frame there
                var s_from     = projectOntoFrenetPath(fromFrenetPath, pt, undefined).arcLength;
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
                    "edgeIndex" : toResult.edgeIndex,
                    "point"     : toFrameResult.frame.origin + toFrameResult.frame.xAxis * localCoords[1] + yAxis(toFrameResult.frame) * localCoords[2] + toFrameResult.frame.zAxis * localCoords[0],
                    "sFrom"     : s_from,
                    "offsetDir" : toFrameResult.frame.xAxis  // to-frame normal = loft thickness direction (~worldZ for XZ-curved to-paths)
                });
            }

            // Emit one output curve per to-edge span (prevents ringing at line/curve joints)
            var segStartIdx               = 0;
            var segCount                  = 0;
            var junctionPt                = undefined;
            var junctionTangent           = undefined;
            var junctionOffsetDir         = undefined;

            while (segStartIdx < size(mappedData))
            {
                // Collect the run of consecutive points on the same to-edge
                var currentEdge = mappedData[segStartIdx].edgeIndex;
                var segEndIdx   = segStartIdx;
                while (segEndIdx + 1 < size(mappedData) && mappedData[segEndIdx + 1].edgeIndex == currentEdge)
                {
                    segEndIdx += 1;
                }

                var segPoints     = [];
                var segOffsetDirs = [];

                // Capture carry-over junction data before clearing for this span
                var carryOverTangent   = junctionTangent;
                var carryOverOffsetDir = junctionOffsetDir;
                junctionTangent  = undefined;
                junctionOffsetDir = undefined;

                // Prepend exact junction point carried from end of previous span
                if (junctionPt != undefined)
                {
                    segPoints     = append(segPoints,     junctionPt);
                    segOffsetDirs = append(segOffsetDirs, carryOverOffsetDir);
                }
                junctionPt = undefined;

                for (var k = segStartIdx; k <= segEndIdx; k += 1)
                {
                    segPoints     = append(segPoints,     mappedData[k].point);
                    segOffsetDirs = append(segOffsetDirs, mappedData[k].offsetDir);
                }

                // Inject exact boundary point at the junction to the next span
                if (segEndIdx + 1 < size(mappedData))
                {
                    var nextEdgeIdx    = mappedData[segEndIdx + 1].edgeIndex;
                    var boundaryEdgeIdx = max([currentEdge, nextEdgeIdx]);
                    var s_to_boundary = toFrenetPath.edgeData[boundaryEdgeIdx].startArcLength;

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
                    var pt_junction = srcPoints[segEndIdx] + t * (srcPoints[segEndIdx + 1] - srcPoints[segEndIdx]);
                    var srcTangent  = normalize(srcPoints[segEndIdx + 1] - srcPoints[segEndIdx]);

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

                    var junctionOffDir = toFrameResult_j.frame.xAxis;
                    segPoints         = append(segPoints,     junctionWorldPt);
                    segOffsetDirs     = append(segOffsetDirs, junctionOffDir);
                    junctionPt        = junctionWorldPt;
                    junctionTangent   = normalize(junctionTangentDir);
                    junctionOffsetDir = junctionOffDir;
                }

                if (size(segPoints) >= degree + 1)
                {
                    // Scale for derivative constraints: total chord length of this segment.
                    var totalChord = 0 * meter;
                    for (var k = 0; k < size(segPoints) - 1; k += 1)
                    {
                        totalChord += norm(segPoints[k + 1] - segPoints[k]);
                    }
                    var approxScale = totalChord;

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
                        "targets"            : [approximationTarget(targetDef)],
                        "tolerance"          : definition.approximationTolerance,
                        "maxControlPoints"   : definition.approximationMaxCPs,
                        "degree"             : degree,
                        "isPeriodic"         : false,
                        "interpolateIndices" : [0, size(segPoints) - 1]
                    };
                    var mappedCurve = approximateSpline(context, approxDef)[0];

                    if (definition.debugWrappedCurves || definition.debugFromBSplines)
                    {
                        var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                        printBSpline(mappedCurve, fmt, ["Wrapped curve " ~ toString(i) ~ "." ~ toString(segCount)]);
                        println("  segPoints[0]=" ~ toString(segPoints[0]) ~
                                " segPoints[-1]=" ~ toString(segPoints[size(segPoints) - 1]) ~
                                " count=" ~ toString(size(segPoints)));
                    }

                    // Build offset point arrays using per-point to-frame xAxis (Frenet normal) as offset direction.
                    // Each segPoints[k] was placed by a specific to-frame; offsetting along that frame's
                    // xAxis (perpendicular to the path tangent, in the plane of curvature) is the correct loft direction.
                    var primaryOffsetPoints   = [];
                    var secondaryOffsetPoints = [];
                    for (var k = 0; k < size(segPoints); k += 1)
                    {
                        primaryOffsetPoints = append(primaryOffsetPoints,
                            segPoints[k] + definition.primaryOffset * segOffsetDirs[k]);
                        if (definition.secondDirection && definition.secondOffset > 0 * millimeter)
                        {
                            secondaryOffsetPoints = append(secondaryOffsetPoints,
                                segPoints[k] - definition.secondOffset * segOffsetDirs[k]);
                        }
                    }

                    // Offset curves don't need exact endpoint interpolation — use unconstrained fit
                    var offsetApproxBase = {
                        "tolerance"        : definition.approximationTolerance,
                        "maxControlPoints" : definition.approximationMaxCPs,
                        "degree"           : degree
                    };

                    var primaryOffsetTargetDef = mergeMaps(targetDef, { "positions": primaryOffsetPoints });
                    var primaryOffsetApproxDef = mergeMaps(offsetApproxBase, { "targets": [approximationTarget(primaryOffsetTargetDef)] });
                    var primaryOffsetCurve     = approximateSpline(context, primaryOffsetApproxDef)[0];

                    var secondaryOffsetCurve = undefined;
                    if (definition.secondDirection && definition.secondOffset > 0 * millimeter)
                    {
                        var secondaryOffsetTargetDef = mergeMaps(targetDef, { "positions": secondaryOffsetPoints });
                        var secondaryOffsetApproxDef = mergeMaps(offsetApproxBase, { "targets": [approximationTarget(secondaryOffsetTargetDef)] });
                        secondaryOffsetCurve         = approximateSpline(context, secondaryOffsetApproxDef)[0];
                    }

                    try
                    {
                        var wrappedId = id + (toString(i) ~ "_" ~ toString(segCount) ~ "wrappedCurve");
                        opCreateBSplineCurve(context, wrappedId, { "bSplineCurve": mappedCurve });
                        allWrappedSegQueries = append(allWrappedSegQueries, qCreatedBy(wrappedId, EntityType.EDGE));
                        allWrappedSegBodies  = append(allWrappedSegBodies,  qCreatedBy(wrappedId, EntityType.BODY));

                        var primaryOffsetId = id + (toString(i) ~ "_" ~ toString(segCount) ~ "primaryOffset");
                        opCreateBSplineCurve(context, primaryOffsetId, { "bSplineCurve": primaryOffsetCurve });
                        allPrimaryOffsetSegQueries = append(allPrimaryOffsetSegQueries, qCreatedBy(primaryOffsetId, EntityType.EDGE));
                        allPrimaryOffsetSegBodies  = append(allPrimaryOffsetSegBodies,  qCreatedBy(primaryOffsetId, EntityType.BODY));

                        if (secondaryOffsetCurve != undefined)
                        {
                            var secondaryOffsetId = id + (toString(i) ~ "_" ~ toString(segCount) ~ "secondaryOffset");
                            opCreateBSplineCurve(context, secondaryOffsetId, { "bSplineCurve": secondaryOffsetCurve });
                            allSecondaryOffsetSegQueries = append(allSecondaryOffsetSegQueries, qCreatedBy(secondaryOffsetId, EntityType.EDGE));
                            allSecondaryOffsetSegBodies  = append(allSecondaryOffsetSegBodies,  qCreatedBy(secondaryOffsetId, EntityType.BODY));
                        }

                        segCount += 1;
                    }
                    catch (e)
                    {
                        println("ERROR: wrapAndLoft opCreateBSplineCurve BAD_GEOMETRY - " ~ e);
                        println("  curve i=" ~ i ~ "  seg=" ~ segCount ~
                                "  segPoints count=" ~ size(segPoints));
                        println("  approxScale=" ~ toString(approxScale / millimeter) ~ " mm");
                        println("  carryOverTangent defined=" ~ (carryOverTangent != undefined));
                        println("  junctionTangent  defined=" ~ (junctionTangent  != undefined));

                        // Per-point coordinates
                        for (var di = 0; di < size(segPoints); di += 1)
                        {
                            var dpt = segPoints[di];
                            println("  [" ~ di ~ "] X=" ~ toString(dpt[0] / millimeter) ~
                                    " mm  Y=" ~ toString(dpt[1] / millimeter) ~
                                    " mm  Z=" ~ toString(dpt[2] / millimeter) ~ " mm");
                        }

                        // Debug geometry: polyline + points
                        for (var di = 0; di < size(segPoints) - 1; di += 1)
                        {
                            addDebugLine(context, segPoints[di], segPoints[di + 1], DebugColor.RED);
                        }
                        for (var di = 0; di < size(segPoints); di += 1)
                        {
                            addDebugPoint(context, segPoints[di], DebugColor.MAGENTA);
                        }
                    }
                }

                segStartIdx = segEndIdx + 1;
            }

        }

        // ===== Single loft across all source edges =====
        if (size(allWrappedSegQueries) > 0)
        {
            // Outermost pair: no secondDirection → [wrapped, primary]; secondDirection → [primary, secondary]
            var loftProfile1 = qUnion(allWrappedSegQueries);
            var loftProfile2 = qUnion(allPrimaryOffsetSegQueries);
            if (definition.secondDirection && size(allSecondaryOffsetSegQueries) > 0)
            {
                loftProfile1 = qUnion(allPrimaryOffsetSegQueries);
                loftProfile2 = qUnion(allSecondaryOffsetSegQueries);
            }

            try
            {
                opLoft(context, id + "loft", {
                    "bodyType"          : ToolBodyType.SURFACE,
                    "profileSubqueries" : [loftProfile1, loftProfile2]
                });
            }
            catch (e)
            {
                println("ERROR: wrapAndLoft loft failed - " ~ toString(e));
            }

            // Collect multi-span segments into wire bodies (one wire per connected run)
            var wrappedWireId = id + "wrappedWires";
            opExtractWires(context, wrappedWireId, { "edges": qUnion(allWrappedSegQueries) });
            var wrappedWireBodies = qCreatedBy(wrappedWireId, EntityType.BODY);

            var primaryWireId = id + "primaryOffsetWires";
            opExtractWires(context, primaryWireId, { "edges": qUnion(allPrimaryOffsetSegQueries) });
            var primaryWireBodies = qCreatedBy(primaryWireId, EntityType.BODY);

            var secondaryWireBodies = undefined;
            if (size(allSecondaryOffsetSegQueries) > 0)
            {
                var secondaryWireId = id + "secondaryOffsetWires";
                opExtractWires(context, secondaryWireId, { "edges": qUnion(allSecondaryOffsetSegQueries) });
                secondaryWireBodies = qCreatedBy(secondaryWireId, EntityType.BODY);
            }

            // Delete original segment bodies — wire bodies are the output
            var allSegBodies = qUnion([qUnion(allWrappedSegBodies), qUnion(allPrimaryOffsetSegBodies)]);
            if (size(allSecondaryOffsetSegBodies) > 0)
            {
                allSegBodies = qUnion([allSegBodies, qUnion(allSecondaryOffsetSegBodies)]);
            }
            opDeleteBodies(context, id + "deleteSegBodies", { "entities": allSegBodies });

            // Curve output cleanup
            if (!definition.keepOutputCurves)
            {
                var wiresToDelete = [wrappedWireBodies, primaryWireBodies];
                if (secondaryWireBodies != undefined)
                {
                    wiresToDelete = append(wiresToDelete, secondaryWireBodies);
                }
                opDeleteBodies(context, id + "deleteWires", { "entities": qUnion(wiresToDelete) });
            }
            else if (definition.outputCurveMode == OutputCurveMode.KEEP_WRAPPED)
            {
                var offsetWiresToDelete = [primaryWireBodies];
                if (secondaryWireBodies != undefined)
                {
                    offsetWiresToDelete = append(offsetWiresToDelete, secondaryWireBodies);
                }
                opDeleteBodies(context, id + "deleteOffsetWires", { "entities": qUnion(offsetWiresToDelete) });
            }
            // OutputCurveMode.KEEP_ALL: keep all wire bodies
        }

        // ===== Cleanup planar projected from-curves =====
        if (projectedBodyQuery != undefined)
        {
            opDeleteBodies(context, id + "cleanupProjected", { "entities" : projectedBodyQuery });
        }
     });


