FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/approximationUtils.fs", version : "2878.0");
import(path : "onshape/std/path.fs", version : "2878.0");


//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
//import Utils
import(path : "ad98c7f43a25a4c0e8a428e7", version : "5a7ce93901d19ca9f12bd4ba");
// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");
// IMPORT: tools/frenet.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
// IMPORT: tools/point_projection.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/eb46317a27a44e391e11dfe6", version : "0cea3c8d27e4f7fd660aa69f");
// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");
// IMPORT: curveMappingCore.fs
export import(path : "683d867c35fdab9c98d47556", version : "7010180c5e3be2311ad5359e");



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
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
            definition.fromEdges is Query;

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

        annotation { "Name" : "Source curves",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "Description" : "Curves to map from fromEdge to toEdge. Accepts edges, wire bodies, or composite parts containing wire bodies." }
        definition.sourceCurves is Query;

        annotation { "Group Name" : "Frame orientation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Normal mode", "Default" : FrameNormalMode.FRENET, "UIHint" : UIHint.HORIZONTAL_ENUM, "Description" : "Frenet uses the curvature normal (can flip at inflections on near-flat curves). Binormal builds a flip-free in-plane normal from a supplied plane normal; requires planar, coplanar references." }
            definition.frameNormalMode is FrameNormalMode;

            if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
            {
                annotation { "Name" : "Binormal from", "Default" : BinormalSource.QUERY, "UIHint" : UIHint.HORIZONTAL_ENUM }
                definition.binormalSource is BinormalSource;

                if (definition.binormalSource == BinormalSource.QUERY)
                {
                    annotation { "Name" : "Binormal reference", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Planar face (uses its normal) or mate connector (uses its Z axis) defining the reference-path plane normal." }
                    definition.binormalQuery is Query;
                }
                else
                {
                    annotation { "Name" : "X", "Icon" : Icon.ALONG_X }
                    isReal(definition.binormalX, binormalCompBounds);

                    annotation { "Name" : "Y", "Icon" : Icon.ALONG_Y }
                    isReal(definition.binormalY, binormalCompBoundsY);

                    annotation { "Name" : "Z", "Icon" : Icon.ALONG_Z }
                    isReal(definition.binormalZ, binormalCompBounds);
                }
            }
        }

        annotation { "Group Name" : "Advanced options", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Source sampling mode", "Default" : SamplingMode.CP_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if source curves should be sampled based on length between sampling points, or as an integer multiple of source curve control points" }
            definition.sourceSamplingMode is SamplingMode;

            annotation { "Group Name" : "Sampling options", "Collapsed By Default" : true }
            {
                if (definition.sourceSamplingMode == SamplingMode.CP_BASED)
                {
                    annotation { "Name" : "CP multiplier", "Description" : "Samples per source curve control point" }
                    isInteger(definition.sourceCPMultiplier, cpMultiplierBounds);
                }
                else
                {
                    annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source curves" }
                    isLength(definition.samplingDensity, samplingDensityBounds);
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

            annotation { "Name" : "Show source points",
                        "Description" : "Draw cyan debug points at each sampled source curve position",
                        "Default" : false }
            definition.showSourcePoints is boolean;

            annotation { "Name" : "Show wrapped points",
                        "Description" : "Draw magenta debug points at each mapped output position",
                        "Default" : false }
            definition.showWrappedPoints is boolean;
        }
    }

    {
        // 1. Build FrenetPaths for from/to references
        // Frame orientation: BINORMAL builds a flip-free in-plane normal from a supplied plane
        // normal (the binormal), shared by both paths; FRENET is the legacy curvature normal.
        // The binormal sign is shared, so it does not affect the wrap output — use "Flip normal"
        // to flip the offset side.
        var frameOpts = defaultFrameNormalOptions();
        if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
        {
            var planeRef;
            if (definition.binormalSource == BinormalSource.VECTOR)
            {
                planeRef = resolveBinormalRefFromVector(definition.binormalX, definition.binormalY, definition.binormalZ);
            }
            else
            {
                planeRef = resolveBinormalRefFromQuery(context, definition.binormalQuery);
            }
            frameOpts = planeNormalOptions(planeRef);
        }

        var fromFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.fromEdges), false,             frameOpts);
        var toFrenetPath   = buildFrenetPath(context, id, expandEdgeQuery(definition.toEdges),   definition.flipTo, frameOpts);

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
        {
            debugDrawFrames(context, toFrenetPath, 10);
        }

        // 2. Resolve reference alignment arc-lengths
        var fromRefPt  = getRefPoint(context, definition.fromRef);
        var toRefPt    = getRefPoint(context, definition.toRef);
        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, fromRefPt, undefined).arcLength;
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   toRefPt,   undefined).arcLength;

        // Visualize the frame at the reference points: GREEN = binormal (the supplied plane
        // normal, shared by both paths, shown once), RED = derived in-plane normal
        // (ref x tangent, the offset direction) on each curve.
        if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
        {
            var arrowLen  = 0.05 * meter;
            var arrowRad  = 0.0015 * meter;
            var fromFrame = getFrameAtArcLength(context, fromFrenetPath, fromRefArc).frame;
            // GREEN = binormal (the supplied plane normal); RED = derived in-plane normal
            // (ref x tangent, the offset direction). One of each, at the from reference.
            addDebugArrow(context, fromRefPt, fromRefPt + arrowLen * yAxis(fromFrame), arrowRad, DebugColor.GREEN);
            addDebugArrow(context, fromRefPt, fromRefPt + arrowLen * fromFrame.xAxis,  arrowRad, DebugColor.RED);
        }

        // Fix 1: Bilaterally align isolated line frames between from-path and to-path.
        // Lines adjacent to a curve already got a curve-context xAxis in buildFrenetPath step 4.5;
        // this handles isolated lines on EITHER path (no curve neighbor on its own path) by
        // borrowing the other path's frame at the corresponding arc-length.
        var aligned = alignLineFramesBilateral(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, 0.001);
        fromFrenetPath = aligned.fromFrenetPath;
        toFrenetPath   = aligned.toFrenetPath;

        if (definition.debugShowFromFrames)
        {
            debugDrawFrames(context, fromFrenetPath, 10);
        }

        // 4. For each source curve: sample, map, fit, create
        var allSegEdges      = [];
        var allSegBodies     = [];
        var wrappedBSplines       = [];
        var wrappedIds            = [];
        var allJunctionCurvatures = [];
        var sourceCurveArray = evaluateQuery(context, expandEdgeQuery(definition.sourceCurves));
        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            var srcBSpline = evApproximateBSplineCurve(context, { "edge": sourceCurveArray[i] });
            var approxDegree = definition.keepDegree
                ? max([definition.approximationDegree, srcBSpline.degree])
                : definition.approximationDegree;

            var sampleResult = sampleSourceEdge(context, sourceCurveArray[i], definition.sourceSamplingMode, {
                "samplingDensity"    : definition.samplingDensity,
                "sourceCPMultiplier" : definition.sourceCPMultiplier,
                "srcBSpline"         : srcBSpline
            });
            var srcPoints  = sampleResult.points;
            var numSamples = sampleResult.numSamples;

            if (definition.debugSourceBSplines)
            {
                var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                println("Source curve " ~ toString(i) ~ ": " ~ toString(size(srcPoints)) ~
                        " samples, length = " ~ toString(sampleResult.arcLengths[size(sampleResult.arcLengths) - 1]));
                printBSpline(srcBSpline, fmt, ["Source curve " ~ toString(i)]);
            }

            // Map each sampled point through Frenet frame transformation;
            // record which to-edge each mapped point lands on for span splitting.
            var mappedData = [];
            var projHint   = undefined;
            for (var sIdx = 0; sIdx < size(srcPoints); sIdx += 1)
            {
                var pt = srcPoints[sIdx];
                if (definition.showSourcePoints)
                    addDebugPoint(context, pt, DebugColor.CYAN);

                var r    = mapSinglePoint(context, fromFrenetPath, toFrenetPath,
                    fromRefArc, toRefArc, definition.flipToNormal, pt, projHint);
                projHint = r.hint;

                if (definition.showWrappedPoints)
                    addDebugPoint(context, r.point, DebugColor.MAGENTA);

                if (definition.printWrapDetails)
                {
                    println("pt=" ~ toString(pt) ~ " sFrom=" ~ toString(r.sFrom) ~
                            " offsetDir=" ~ toString(r.offsetDir) ~ " out=" ~ toString(r.point));
                }
                mappedData = append(mappedData, {
                    "edgeIndex": r.edgeIndex,
                    "point"    : r.point,
                    "sFrom"    : r.sFrom
                });
            }

            // Detect reversed source curve parameterization (e.g. mirrored geometry).
            // If sFrom decreases across the samples the source runs opposite to the from-path
            // direction. Reverse mappedData so span detection sees a forward sequence.
            var srcFlipped = mappedData[size(mappedData) - 1].sFrom < mappedData[0].sFrom;
            if (srcFlipped)
            {
                var flippedData = [];
                for (var ri = size(mappedData) - 1; ri >= 0; ri -= 1)
                    flippedData = append(flippedData, mappedData[ri]);
                mappedData = flippedData;
            }

            // Emit one output curve per to-edge span (prevents ringing at line/curve joints)
            var segStartIdx     = 0;
            var segCount        = 0;
            var wrappedIdsBefore  = size(wrappedIds);  // snapshot before this source curve's spans
            var junctionPt        = undefined;  // carry-over exact junction point between spans
            var junctionTangent   = undefined;  // carry-over junction tangent direction in to-space
            var junctionCurvature = undefined;  // carry-over mapped source curvature at span END

            // Pre-constrain first span's start tangent from source edge at parameter 0
            {
                var startParam      = srcFlipped ? 1 : 0;
                var startLine       = evEdgeTangentLines(context, { "edge": sourceCurveArray[i], "parameters": [startParam] })[0];
                var startSrcTangent = srcFlipped ? -1 * startLine.direction : startLine.direction;
                var s_from_0        = mappedData[0].sFrom;
                var fromResult_0    = getFrameAtArcLength(context, fromFrenetPath, s_from_0);
                var s_to_0          = toRefArc + (s_from_0 - fromRefArc);
                var toResult_0      = getFrameAtArcLength(context, toFrenetPath, s_to_0);
                var toSign_0        = definition.flipToNormal ? -1 * toResult_0.sign : toResult_0.sign;
                var toFrameResult_0 = (toSign_0 != fromResult_0.sign)
                    ? mergeMaps(toResult_0, { "frame": coordSystem(toResult_0.frame.origin, -1 * toResult_0.frame.xAxis, toResult_0.frame.zAxis) })
                    : toResult_0;
                junctionTangent = mapEdgeJunctionTangent(startSrcTangent, fromResult_0, toFrameResult_0);
            }

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

                // Capture carry-over tangent from previous span's junction before clearing
                var carryOverTangent   = junctionTangent;
                junctionTangent   = undefined;
                junctionCurvature = undefined;

                // Prepend exact junction point carried over from end of previous span
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
                    // Use the higher-index edge's startArcLength — works for both forward and backward transitions
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
                    // Evaluate exact position and tangent on the source edge at the junction parameter.
                    // Natural parameter for sample k is k/(numSamples-1); interpolate with t.
                    var junctionParam    = (segEndIdx + t) / (numSamples - 1);
                    var srcJunctionParam = srcFlipped ? 1 - junctionParam : junctionParam;
                    var junctionLine     = evEdgeTangentLines(context, {
                        "edge"       : sourceCurveArray[i],
                        "parameters" : [srcJunctionParam]
                    })[0];
                    var pt_junction = junctionLine.origin;
                    var srcTangent  = srcFlipped ? -1 * junctionLine.direction : junctionLine.direction;

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

                    // --- Phase 4: junction-aware oversampling ---
                    // Inject 2 extra mapped points between the last regular sample and the junction.
                    // Provides the spline fitter with better curvature information near the junction.
                    {
                        var nExtra = 2;
                        for (var ex = 1; ex <= nExtra; ex += 1)
                        {
                            var alpha      = ex / (nExtra + 1.0);
                            var extraParam    = segEndIdx / (numSamples - 1) + alpha * (junctionParam - segEndIdx / (numSamples - 1));
                            var srcExtraParam = srcFlipped ? 1 - extraParam : extraParam;
                            var extraLine     = evEdgeTangentLines(context, {
                                "edge"       : sourceCurveArray[i],
                                "parameters" : [srcExtraParam]
                            })[0];
                            var sFrom_extra   = mappedData[segEndIdx].sFrom + alpha * (s_from_junction - mappedData[segEndIdx].sFrom);
                            var fromResult_e  = getFrameAtArcLength(context, fromFrenetPath, sFrom_extra);
                            var localCoords_e = worldPointToFrenet(extraLine.origin, fromResult_e);
                            var s_to_extra    = toRefArc + (sFrom_extra - fromRefArc);
                            var toResult_e    = getFrameAtArcLength(context, toFrenetPath, s_to_extra);
                            var toSign_e      = definition.flipToNormal ? -1 * toResult_e.sign : toResult_e.sign;
                            var toFrameResult_e = (toSign_e != fromResult_e.sign)
                                ? mergeMaps(toResult_e, { "frame": coordSystem(toResult_e.frame.origin, -1 * toResult_e.frame.xAxis, toResult_e.frame.zAxis) })
                                : toResult_e;
                            segPoints = append(segPoints, frenetPointToWorld(localCoords_e, toFrameResult_e));
                        }
                    }

                    // --- Phase 2: compute and map source curvature at junction ---
                    // Finite-difference approximation: (T(t+ε) - T(t-ε)) / arc_length_step
                    {
                        var jEps   = 0.005;
                        var srcJP  = srcFlipped ? 1 - junctionParam : junctionParam;
                        var pJm    = max([0, srcJP - jEps]);
                        var pJp    = min([1, srcJP + jEps]);
                        var kLines = evEdgeTangentLines(context, {
                            "edge"       : sourceCurveArray[i],
                            "parameters" : [pJm, pJp]
                        });
                        var dsJ    = norm(kLines[1].origin - kLines[0].origin);  // chord between evaluation points
                        var deltaT     = kLines[1].direction - kLines[0].direction;
                        if (dsJ > 1e-10 * meter && norm(deltaT) > 1e-10)
                        {
                            var kappaSrc      = deltaT / dsJ;
                            junctionCurvature = mapEdgeJunctionCurvature(kappaSrc, fromResult_j, toFrameResult_j);
                        }
                        else
                        {
                            junctionCurvature = undefined;
                        }
                    }

                    // Append to current span; carry over to next span's start
                    segPoints       = append(segPoints, junctionWorldPt);
                    junctionPt      = junctionWorldPt;
                    junctionTangent = mapEdgeJunctionTangent(srcTangent, fromResult_j, toFrameResult_j);
                }
                else
                {
                    // Last span — constrain end tangent from source edge at parameter 1 (or 0 if flipped)
                    var endParam      = srcFlipped ? 0 : 1;
                    var endLine       = evEdgeTangentLines(context, { "edge": sourceCurveArray[i], "parameters": [endParam] })[0];
                    var endSrcTangent = srcFlipped ? -1 * endLine.direction : endLine.direction;
                    var s_from_end        = mappedData[size(mappedData) - 1].sFrom;
                    var fromResult_end    = getFrameAtArcLength(context, fromFrenetPath, s_from_end);
                    var s_to_end          = toRefArc + (s_from_end - fromRefArc);
                    var toResult_end      = getFrameAtArcLength(context, toFrenetPath, s_to_end);
                    var toSign_end        = definition.flipToNormal ? -1 * toResult_end.sign : toResult_end.sign;
                    var toFrameResult_end = (toSign_end != fromResult_end.sign)
                        ? mergeMaps(toResult_end, { "frame": coordSystem(toResult_end.frame.origin, -1 * toResult_end.frame.xAxis, toResult_end.frame.zAxis) })
                        : toResult_end;
                    junctionTangent = mapEdgeJunctionTangent(endSrcTangent, fromResult_end, toFrameResult_end);
                }

                // Remove near-coincident points to prevent degenerate spline.
                {
                    var minSep  = 1e-6 * meter;
                    var deduped = [segPoints[0]];
                    for (var k = 1; k < size(segPoints); k += 1)
                    {
                        if (norm(segPoints[k] - deduped[size(deduped) - 1]) >= minSep)
                            deduped = append(deduped, segPoints[k]);
                    }
                    segPoints = deduped;
                }

                if (size(segPoints) >= approxDegree + 1)
                {
                    // Scale for derivative constraints: total chord length of this segment.
                    // approximateSpline uses [0,1] parameterization, so the natural derivative
                    // magnitude at an endpoint is ~totalChord (velocity = length/param_range).
                    // Dividing by (n-1) would give per-sample spacing (~218× too small for 218
                    // points on a 0.3m arc), causing startDerivative to force near-zero velocity
                    // and produce a visible bulge of ~0.45mm near the junction.
                    var totalChord = 0 * meter;
                    for (var k = 0; k < size(segPoints) - 1; k += 1)
                    {
                        totalChord += norm(segPoints[k + 1] - segPoints[k]);
                    }
                    var approxScale = totalChord;

                    // Use same pattern as offset curves: derivative constraints as hard solver
                    // constraints (no interpolateIndices), then snap endpoints to exact positions.
                    // enforceEndpointDerivatives had a 20% guard that could silently skip correction;
                    // passing derivatives directly into the solver guarantees tangent continuity.
                    var wrappedTargetDef = { "positions": segPoints };
                    if (carryOverTangent != undefined)
                        wrappedTargetDef = mergeMaps(wrappedTargetDef, { "startDerivative": carryOverTangent * approxScale });
                    if (junctionTangent != undefined)
                        wrappedTargetDef = mergeMaps(wrappedTargetDef, { "endDerivative": junctionTangent * approxScale });
                    var approxDef = {
                        "targets"          : [approximationTarget(wrappedTargetDef)],
                        "tolerance"        : definition.approximationTolerance,
                        "maxControlPoints" : definition.approximationMaxCPs,
                        "degree"           : approxDegree,
                        "isPeriodic"       : false
                    };
                    var mappedCurve = approximateSpline(context, approxDef)[0];
                    // Snap CP[0] and CP[-1] to exact input positions (same as offset curves).
                    {
                        var cps = mappedCurve.controlPoints;
                        var m   = size(cps) - 1;
                        var snapped = [];
                        for (var ci = 0; ci <= m; ci += 1)
                        {
                            if (ci == 0)
                                snapped = append(snapped, segPoints[0]);
                            else if (ci == m)
                                snapped = append(snapped, segPoints[size(segPoints) - 1]);
                            else
                                snapped = append(snapped, cps[ci]);
                        }
                        mappedCurve = mergeMaps(mappedCurve, { "controlPoints": snapped });
                    }

                    if (definition.debugWrappedCurves)
                    {
                        var fmt = definition.debugDetailedBSplines ? PrintFormat.DETAILS : PrintFormat.METADATA;
                        printBSpline(mappedCurve, fmt, ["Wrapped curve " ~ toString(i) ~ "." ~ toString(segCount)]);
                    }

                    var thisSegCount = segCount;
                    segCount += 1;
                    try
                    {
                        var segOpId = id + (toString(i) ~ "_" ~ toString(thisSegCount) ~ "wrappedCurve");
                        opCreateBSplineCurve(context, segOpId, { "bSplineCurve": mappedCurve });
                        wrappedBSplines       = append(wrappedBSplines,       mappedCurve);
                        wrappedIds            = append(wrappedIds,            segOpId);
                        allJunctionCurvatures = append(allJunctionCurvatures, junctionCurvature);
                    }
                    catch (e)
                    {
                        println("ERROR: wrapCurve opCreateBSplineCurve BAD_GEOMETRY - " ~ toString(e));
                        println("  curve i=" ~ toString(i) ~ " seg=" ~ toString(thisSegCount) ~
                                "  segPoints count=" ~ toString(size(segPoints)));
                        for (var di = 0; di < size(segPoints) - 1; di += 1)
                        {
                            if (norm(segPoints[di + 1] - segPoints[di]) > 1e-10 * meter)
                                addDebugLine(context, segPoints[di], segPoints[di + 1], DebugColor.RED);
                        }
                        for (var di = 0; di < size(segPoints); di += 1)
                            addDebugPoint(context, segPoints[di], DebugColor.MAGENTA);
                    }
                }
                else if (definition.debugWrappedCurves)
                {
                    println("  [skipped span " ~ toString(i) ~ "." ~ toString(segCount) ~
                            ": only " ~ toString(size(segPoints)) ~ " points, need " ~ toString(approxDegree + 1) ~ "]");
                }

                segStartIdx = segEndIdx + 1;
            }

            // Accumulate segment edges/bodies for this source curve (only successfully created spans).
            for (var k = wrappedIdsBefore; k < size(wrappedIds); k += 1)
            {
                allSegEdges  = append(allSegEdges,  qCreatedBy(wrappedIds[k], EntityType.EDGE));
                allSegBodies = append(allSegBodies, qCreatedBy(wrappedIds[k], EntityType.BODY));
            }
        }

        // G2 junction smoothing: averages curvature at span junctions and jostles P2/P_{m-2}.
        if (size(wrappedBSplines) >= 2)
        {
            var jostleResult = jostleG2Junctions(context, id, wrappedBSplines, wrappedIds, 1e-6 * meter, allJunctionCurvatures, true);
            allSegEdges  = jostleResult.edgeQueries;
            allSegBodies = jostleResult.bodyQueries;
        }

        // Single opExtractWires for all source curves — all output owned by id + "wire".
        if (size(allSegEdges) > 0)
        {
            opExtractWires(context, id + "wire", { "edges": qUnion(allSegEdges) });
            opDeleteBodies(context, id + "deleteIntermediate", { "entities": qUnion(allSegBodies) });
        }
    });


