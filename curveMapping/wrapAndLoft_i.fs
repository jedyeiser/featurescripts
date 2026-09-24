FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");
import(path : "onshape/std/approximationUtils.fs", version : "3008.0");

// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");
// IMPORT: tools/frenet.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
// IMPORT: tools/printing.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");
// IMPORT: curveMappingCore.fs
export import(path : "683d867c35fdab9c98d47556", version : "8462eb38af6e4ab10aabbe0a");

// wrapCurve is deliberately NOT imported: nothing here calls it, and its export-import of the
// core could bring a second version of the core into this scope.



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
const PRIMARY_OFFSET_BOUND        = { (millimeter) : [0, 20, 100] } as LengthBoundSpec;
// A to-edge span whose mapped chord length is below this is treated as a seam
// artifact (endpoint on a reference line/arc joint) and merged into its neighbor,
// which otherwise fits a near-degenerate span with clustered control points.
const CLUSTER_MERGE_MIN_SPAN      = 0.25 * millimeter;

export enum OutputCurveMode
{
    annotation { "Name" : "None" } NONE,
    annotation { "Name" : "Keep wrapped only" } KEEP_WRAPPED,
    annotation { "Name" : "Keep all" } KEEP_ALL
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

        annotation { "Group Name" : "Offset", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Primary offset" }
            isLength(definition.primaryOffset, PRIMARY_OFFSET_BOUND);

            annotation { "Name" : "Flip offset direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipOffset is boolean;

            annotation { "Name" : "Second direction", "Default" : false }
            definition.secondDirection is boolean;

            if (definition.secondDirection)
            {
                annotation { "Name" : "Second offset" }
                isLength(definition.secondOffset, ZERO_INCLUSIVE_OFFSET_BOUND);
            }
        }

        annotation { "Name" : "Output curves", "Default" : OutputCurveMode.NONE }
        definition.outputCurveMode is OutputCurveMode;

        annotation { "Name" : "Exact ruled surface", "Default" : true, "Description" : "Fit each span's wrapped and offset curves together (shared knots) and build the surface as the exact ruled B-spline between them. A source curve with a straight-region span, or a patch the kernel refuses, is lofted as before." }

        definition.exactRuledSurface is boolean;


        annotation { "Group Name" : "Advanced options", "Collapsed By Default" : true }
        {
            
            annotation { "Group Name" : "Sampling options", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Source sampling mode", "Default" : SamplingMode.CP_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if source edges should be sampled based on length between sampling points, or as an integer multiple of the source edge control points" }
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
    
                annotation { "Name" : "Reference sampling mode", "Default" : SamplingMode.CP_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if reference edges  (to/from curves) should be sampled based on length between sampling points, or as an integer multiple of the source edge control points"  }
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

                annotation { "Name" : "Fix control-point clustering", "Default" : true,
                            "Description" : "Merge sliver spans (a source endpoint landing on a reference line/arc seam produces a near-zero-length span with stacked control points). Merges any span shorter than the internal floor into its neighbor." }
                definition.fixClustering is boolean;
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

        // ===== Frame orientation =====
        // BINORMAL builds a flip-free in-plane normal from a supplied plane normal (shared by
        // both paths); FRENET is the legacy curvature normal.
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

        // ===== Build to-path =====
        var toFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.toEdges), definition.flipTo, frameOpts);

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
                var refBSpline;
                if (definition.referenceSamplingMode == SamplingMode.CP_BASED || definition.keepDegree)
                {
                    refBSpline = evApproximateBSplineCurve(context, { "edge": toEdgeArray[tei] });
                }

                var nProj;
                if (definition.referenceSamplingMode == SamplingMode.CP_BASED)
                {
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

                var projApproxDegree = (definition.keepDegree && refBSpline != undefined)
                    ? max([definition.approximationDegree, refBSpline.degree])
                    : definition.approximationDegree;

                var projApproxDef = {
                    "targets"            : [approximationTarget({ "positions": projPoints })],
                    "tolerance"          : definition.approximationTolerance,
                    "maxControlPoints"   : definition.approximationMaxCPs,
                    "degree"             : projApproxDegree,
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
            fromFrenetPath     = buildFrenetPath(context, id, qUnion(projectedEdgeQueries), false, frameOpts);
            projectedBodyQuery = qUnion(projectedBodies);
        }
        else
        {
            // Non-planar: user provides fromEdges explicitly
            fromFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.fromEdges), false, frameOpts);
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

        // ===== Bilateral isolated-line xAxis fix =====
        // Promotes near-linear projected BSplines to line mode and borrows the other path's
        // frame at the corresponding arc-length for isolated lines on EITHER path. Symmetric
        // so that line-on-to-path workflows (e.g. flattening) work as well as line-on-from-path.
        var aligned = alignLineFramesBilateral(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, 0.001);
        fromFrenetPath = aligned.fromFrenetPath;
        toFrenetPath   = aligned.toFrenetPath;

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
        var wrappedBSplines               = [];        var primaryBSplines       = [];   // per fitted span, parallel to wrappedBSplines        var secondaryBSplines     = [];
        var wrappedIds                    = [];
        var allJunctionCurvatures         = [];
        var segCountPerSourceCurve        = [];
        var spanIsFast                    = [];   // per wrapped-span: true if it is a linear fast-path move
        var offsetSign                    = definition.flipOffset ? -1 : 1;   // flips which side the offsets go
        // Cross-curve offset-direction weld: [{point, offsetDir}] for every source-curve
        // endpoint mapped so far. Adjacent source curves share an endpoint; the spine
        // coincides there by construction, but the offset direction is recomputed per
        // curve and can disagree (worst at a to-path line/arc seam), opening a gap on the
        // OFFSET edge scaled by the offset distance. Whichever neighbor is processed first
        // wins; the other adopts its offsetDir so the offset endpoints coincide exactly.
        var junctionOffsetCache           = [];
        var clusteredSpanCount            = 0;    // sliver spans seen when fixClustering is off (for the recommend-the-toggle banner)
        for (var i = 0; i < size(sourceCurveArray); i += 1)
        {
            // Fast path: whole source curve in a doubly-linear region -> the wrap and
            // both offsets are rigid transforms. Move copies of the source instead of
            // sample-and-refit (arcs/lines preserved exactly). Emitted as one span for
            // this source curve; falls through to the normal path below if ineligible.
            if (CM_LINEAR_FASTPATH && pathHasLine(fromFrenetPath) && pathHasLine(toFrenetPath))
            {
                var probePts = mapArray(evEdgeTangentLines(context, {
                    "edge"       : sourceCurveArray[i],
                    "parameters" : [0, 0.25, 0.5, 0.75, 1]
                }), function(x) { return x.origin; });
                var lin = linearRegionMove(context, fromFrenetPath, toFrenetPath,
                    fromRefArc, toRefArc, definition.flipToNormal, probePts);
                if (lin.eligible)
                {
                    var wBefore = size(allWrappedSegQueries);
                    var linId   = id + (toString(i) ~ "lin");
                    var withSecondary = definition.secondDirection && definition.secondOffset > 0 * millimeter;
                    try
                    {
                        // All or nothing, as in the fitted path: make every copy, then record.
                        // Wrapped curve = rigid move of the source.
                        opExtractWires(context, linId + "w", { "edges": sourceCurveArray[i] });
                        opTransform(context, linId + "wx", { "bodies": qCreatedBy(linId + "w", EntityType.BODY), "transform": lin.transform });
                        // Primary offset = wrapped translated by primaryOffset along the constant normal.
                        opExtractWires(context, linId + "p", { "edges": sourceCurveArray[i] });
                        opTransform(context, linId + "px", { "bodies": qCreatedBy(linId + "p", EntityType.BODY),
                                "transform": transform(offsetSign * definition.primaryOffset * lin.offsetDir) * lin.transform });
                        // Secondary offset (opposite side), when enabled.
                        if (withSecondary)
                        {
                            opExtractWires(context, linId + "s", { "edges": sourceCurveArray[i] });
                            opTransform(context, linId + "sx", { "bodies": qCreatedBy(linId + "s", EntityType.BODY),
                                    "transform": transform(-1 * offsetSign * definition.secondOffset * lin.offsetDir) * lin.transform });
                        }

                        allWrappedSegQueries = append(allWrappedSegQueries, qCreatedBy(linId + "w", EntityType.EDGE));
                        allWrappedSegBodies  = append(allWrappedSegBodies,  qCreatedBy(linId + "w", EntityType.BODY));
                        spanIsFast           = append(spanIsFast, true);
                        allPrimaryOffsetSegQueries = append(allPrimaryOffsetSegQueries, qCreatedBy(linId + "p", EntityType.EDGE));
                        allPrimaryOffsetSegBodies  = append(allPrimaryOffsetSegBodies,  qCreatedBy(linId + "p", EntityType.BODY));
                        if (withSecondary)
                        {
                            allSecondaryOffsetSegQueries = append(allSecondaryOffsetSegQueries, qCreatedBy(linId + "s", EntityType.EDGE));
                            allSecondaryOffsetSegBodies  = append(allSecondaryOffsetSegBodies,  qCreatedBy(linId + "s", EntityType.BODY));
                        }

                        // Register both ends in the cross-curve weld, so a fitted neighbour
                        // processed later adopts this curve's (constant) offset direction and
                        // its offset lands on this one's. A neighbour processed EARLIER keeps
                        // its own direction: a rigid copy cannot bend to meet it.
                        for (var pk in [0, size(probePts) - 1])
                        {
                            junctionOffsetCache = append(junctionOffsetCache, {
                                "point"     : lin.transform * probePts[pk],
                                "offsetDir" : lin.offsetDir
                            });
                        }
                    }
                    catch (e)
                    {
                        println("ERROR wrapAndLoft linearFastPath " ~ toString(i) ~ ": " ~ toString(e));
                        const partial = qUnion([qCreatedBy(linId + "w", EntityType.BODY), qCreatedBy(linId + "p", EntityType.BODY),
                                    qCreatedBy(linId + "s", EntityType.BODY)]);
                        if (!isQueryEmpty(context, partial))
                        {
                            opDeleteBodies(context, linId + "discardPartial", { "entities": partial });
                        }
                    }

                    // One entry per source curve (0 if the ops failed) so the loft slicing stays aligned.
                    segCountPerSourceCurve = append(segCountPerSourceCurve, size(allWrappedSegQueries) - wBefore);
                    continue;
                }
            }

            var srcBSpline = (definition.keepDegree || definition.sourceSamplingMode == SamplingMode.CP_BASED)
                ? evApproximateBSplineCurve(context, { "edge": sourceCurveArray[i] })
                : undefined;
            var approxDegree = (definition.keepDegree && srcBSpline != undefined)
                ? max([definition.approximationDegree, srcBSpline.degree])
                : definition.approximationDegree;

            var sampleResult = sampleSourceEdge(context, sourceCurveArray[i], definition.sourceSamplingMode, {
                "samplingDensity"    : samplingDensity,
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
                if (srcBSpline != undefined)
                    printBSpline(srcBSpline, fmt, ["Source curve " ~ toString(i)]);
            }

            // Map each sampled point through the Frenet frame transformation;
            // record which to-edge each mapped point lands on for span splitting.
            var mappedData = [];
            var projHint   = undefined;
            for (var sIdx = 0; sIdx < size(srcPoints); sIdx += 1)
            {
                var r    = mapSinglePoint(context, fromFrenetPath, toFrenetPath,
                    fromRefArc, toRefArc, definition.flipToNormal, srcPoints[sIdx], projHint);
                projHint = r.hint;
                mappedData = append(mappedData, {
                    "edgeIndex" : r.edgeIndex,
                    "fromEdgeIndex" : r.hint.edgeIndex,
                    "point"     : r.point,
                    "sFrom"     : r.sFrom,
                    "offsetDir" : r.offsetDir
                });
            }

            // Detect if source curve is parameterized opposite to the from-path direction
            // (happens with mirrored edges). If so, reverse mappedData and remap parameters.
            var srcFlipped = size(mappedData) > 1 && mappedData[size(mappedData) - 1].sFrom < mappedData[0].sFrom;
            if (srcFlipped)
            {
                var flippedData = [];
                for (var ri = size(mappedData) - 1; ri >= 0; ri -= 1)
                    flippedData = append(flippedData, mappedData[ri]);
                mappedData = flippedData;
            }

            // Weld offset direction at shared source-curve endpoints. Only mappedData[0]
            // and mappedData[-1] are shared with neighbors (interior samples are not). If a
            // neighbor already registered a coincident endpoint, adopt its offsetDir (and
            // exact point) so this curve's offset endpoint lands on the neighbor's -> the
            // loft rib closes. Otherwise register this endpoint as the winner. O(n^2) over
            // endpoints, but n is the source-curve count (tiny). This mirrors the intra-curve
            // junctionOffsetDir carry-over, which resets per source curve and so never
            // spanned across curves.
            if (size(mappedData) > 0)
            {
                var weldIdxs = (size(mappedData) > 1) ? [0, size(mappedData) - 1] : [0];
                for (var ei in weldIdxs)
                {
                    var welded = false;
                    for (var c = 0; c < size(junctionOffsetCache); c += 1)
                    {
                        if (norm(mappedData[ei].point - junctionOffsetCache[c].point) < 1e-6 * meter)
                        {
                            mappedData[ei] = mergeMaps(mappedData[ei], {
                                "offsetDir" : junctionOffsetCache[c].offsetDir,
                                "point"     : junctionOffsetCache[c].point
                            });
                            welded = true;
                            break;
                        }
                    }
                    if (!welded)
                    {
                        junctionOffsetCache = append(junctionOffsetCache, {
                            "point"     : mappedData[ei].point,
                            "offsetDir" : mappedData[ei].offsetDir
                        });
                    }
                }
            }

            // Fix control-point clustering: a source endpoint landing on a to-path line/arc
            // seam yields a sliver to-edge run (a few samples spanning ~microns), which the
            // span fit turns into a near-degenerate curve with stacked control points. Merge
            // any run shorter than CLUSTER_MERGE_MIN_SPAN into its longer neighbor by
            // relabeling its samples' edgeIndex; the existing span loop then groups them as
            // one span. Only edgeIndex changes here -- mapped points and offset directions are
            // untouched, so this is purely a fit-grouping change. Iterates because merging can
            // expose a new sub-floor run.
            if (definition.fixClustering && size(mappedData) > 1)
            {
                var merging = true;
                while (merging)
                {
                    merging = false;
                    // Build contiguous runs of equal edgeIndex with their mapped chord length.
                    var runs = [];
                    var rs = 0;
                    while (rs < size(mappedData))
                    {
                        var re = rs;
                        while (re + 1 < size(mappedData) && mappedData[re + 1].edgeIndex == mappedData[rs].edgeIndex)
                        {
                            re += 1;
                        }
                        var runLen = 0 * meter;
                        for (var k = rs; k < re; k += 1)
                        {
                            runLen += norm(mappedData[k + 1].point - mappedData[k].point);
                        }
                        runs = append(runs, { "start": rs, "end": re, "edge": mappedData[rs].edgeIndex, "len": runLen });
                        rs = re + 1;
                    }
                    if (size(runs) < 2)
                    {
                        break;
                    }
                    // Pick the shortest sub-floor run.
                    var minRun = -1;
                    for (var r = 0; r < size(runs); r += 1)
                    {
                        if (runs[r].len < CLUSTER_MERGE_MIN_SPAN && (minRun == -1 || runs[r].len < runs[minRun].len))
                        {
                            minRun = r;
                        }
                    }
                    if (minRun == -1)
                    {
                        break;
                    }
                    // Merge into the longer neighbor (the only neighbor at an end).
                    var nb = (minRun == 0) ? 1
                        : ((minRun == size(runs) - 1) ? size(runs) - 2
                        : (runs[minRun - 1].len >= runs[minRun + 1].len ? minRun - 1 : minRun + 1));
                    var targetEdge = runs[nb].edge;
                    for (var k = runs[minRun].start; k <= runs[minRun].end; k += 1)
                    {
                        mappedData[k] = mergeMaps(mappedData[k], { "edgeIndex": targetEdge });
                    }
                    merging = true;
                }
            }

            // Detection only (toggle off): count sliver spans so we can recommend enabling
            // the fix. Same cheap run-length scan as the merge, but no relabel and no
            // geometry -- negligible next to the fit/loft ops, so it never bloats the build.
            if (!definition.fixClustering && size(mappedData) > 1)
            {
                var ds = 0;
                while (ds < size(mappedData))
                {
                    var de = ds;
                    while (de + 1 < size(mappedData) && mappedData[de + 1].edgeIndex == mappedData[ds].edgeIndex)
                    {
                        de += 1;
                    }
                    var dLen = 0 * meter;
                    for (var k = ds; k < de; k += 1)
                    {
                        dLen += norm(mappedData[k + 1].point - mappedData[k].point);
                    }
                    if (dLen < CLUSTER_MERGE_MIN_SPAN)
                    {
                        clusteredSpanCount += 1;
                    }
                    ds = de + 1;
                }
            }

            // Emit one output curve per to-edge span (prevents ringing at line/curve joints)
            var segStartIdx               = 0;
            var segCount                  = 0;
            var wrappedCountBefore        = size(allWrappedSegQueries);  // track successful spans
            var junctionPt                = undefined;
            var junctionTangent           = undefined;            var junctionTangentInfo = undefined;  // exact tangent record at the current span's END            var nextStartInfo       = undefined;  // exact tangent record at the next span's START
            var junctionOffsetDir         = undefined;
            var junctionCurvature         = undefined;  // carry-over mapped source curvature at span END

            // Pre-constrain first span's start tangent from source edge at parameter 0
            {
                var startDirection  = srcFlipped ? sampleResult.endTangent : sampleResult.startTangent;   // sampled at parameter 1 / 0
                var startSrcTangent = srcFlipped ? -1 * startDirection : startDirection;
                var s_from_0        = mappedData[0].sFrom;
                var fromResult_0    = getFrameAtArcLength(context, fromFrenetPath, s_from_0);
                var s_to_0          = toRefArc + (s_from_0 - fromRefArc);
                var toResult_0      = getFrameAtArcLength(context, toFrenetPath, s_to_0);
                var toSign_0        = definition.flipToNormal ? -1 * toResult_0.sign : toResult_0.sign;
                var toFrameResult_0 = (toSign_0 != fromResult_0.sign)
                    ? mergeMaps(toResult_0, { "frame": coordSystem(toResult_0.frame.origin, -1 * toResult_0.frame.xAxis, toResult_0.frame.zAxis) })
                    : toResult_0;
                junctionTangentInfo = exactTangentAt(context, fromFrenetPath, toFrenetPath, s_from_0, s_to_0, 0,
                    definition.flipToNormal, startSrcTangent,
                    sampleResult.points[srcFlipped ? size(sampleResult.points) - 1 : 0], mappedData[0].point);
                junctionTangent = junctionTangentInfo.tangent;
            }

            while (segStartIdx < size(mappedData))
            {
                // Collect the run of points the to-path carries smoothly. See
                // pathIsSmoothAcross: with a G1 chain and a transported normal, a split at a
                // tangent-continuous to-edge boundary buys nothing and risks leaving a span
                // on a short edge with too few points to fit.
                var segEndIdx = segStartIdx;
                while (segEndIdx + 1 < size(mappedData)
                       && pathIsSmoothAcross(toFrenetPath, mappedData[segEndIdx].edgeIndex,
                                             mappedData[segEndIdx + 1].edgeIndex)
                       && pathIsSmoothAcross(fromFrenetPath, mappedData[segEndIdx].fromEdgeIndex,
                                             mappedData[segEndIdx + 1].fromEdgeIndex))
                {
                    segEndIdx += 1;
                }

                // A trailing span of one sample cannot be fitted and would be dropped,
                // leaving a gap at the very end of the curve -- the exact failure the
                // top-up exists to prevent, but out of its reach because a single-sample
                // span has no interior gap to subdivide. Absorb it instead.
                if (segEndIdx + 2 == size(mappedData))
                {
                    segEndIdx += 1;
                }

                // The edge the span ENDS on drives the boundary below; a span may cover several.
                var currentEdge = mappedData[segEndIdx].edgeIndex;

                var segPoints     = [];
                var segOffsetDirs = [];

                // Capture carry-over junction data before clearing for this span
                // The next span starts with the AFTER side of the junction (or the curve's start).                var carryOverInfo      = (nextStartInfo != undefined) ? nextStartInfo : junctionTangentInfo;                var carryOverTangent   = (carryOverInfo != undefined) ? carryOverInfo.tangent : junctionTangent;                nextStartInfo          = undefined;                junctionTangentInfo    = undefined;
                var carryOverOffsetDir = junctionOffsetDir;
                junctionTangent   = undefined;
                junctionOffsetDir = undefined;
                junctionCurvature = undefined;

                // Prepend exact junction point carried from end of previous span
                if (junctionPt != undefined)
                {
                    segPoints     = append(segPoints,     junctionPt);
                    segOffsetDirs = append(segOffsetDirs, carryOverOffsetDir);
                }
                junctionPt = undefined;

                // Top a thin span up from its own gaps rather than letting it fall below
                // degree+1 and be discarded. Offset dirs stay in lockstep with the points.
                var spanShortfall = (approxDegree + 1) - (size(segPoints) + segEndIdx - segStartIdx + 1);
                var perGap        = (spanShortfall > 0 && segEndIdx > segStartIdx)
                    ? ceil(spanShortfall / (segEndIdx - segStartIdx))
                    : 0;

                for (var k = segStartIdx; k <= segEndIdx; k += 1)
                {
                    segPoints     = append(segPoints,     mappedData[k].point);
                    segOffsetDirs = append(segOffsetDirs, mappedData[k].offsetDir);

                    if (perGap > 0 && k < segEndIdx)
                    {
                        for (var ex = 1; ex <= perGap; ex += 1)
                        {
                            var extra = mapBetweenSamples(context, definition.flipToNormal,
                                    sourceCurveArray[i], srcFlipped, numSamples,
                                    fromFrenetPath, toFrenetPath, fromRefArc, toRefArc,
                                    mappedData, k, ex / (perGap + 1.0));
                            segPoints     = append(segPoints,     extra.point);
                            segOffsetDirs = append(segOffsetDirs, extra.offsetDir);
                        }
                    }
                }

                // Inject exact boundary point at the junction to the next span
                if (segEndIdx + 1 < size(mappedData))
                {
                    var nextEdgeIdx    = mappedData[segEndIdx + 1].edgeIndex;
                    // The first edge boundary past the current run, in the direction of travel: the
                    // start of currentEdge + 1 going forward, of currentEdge going backward. Same as
                    // max(current, next) for adjacent edges; unlike it, still right after the
                    // clustering fix merges a sliver run and the two edges are no longer adjacent.
                    var boundaryEdgeIdx = (nextEdgeIdx > currentEdge) ? currentEdge + 1 : currentEdge;
                    // The span ended at a to-edge boundary, or else at a from-edge boundary (a corner or                    // curvature jump of the from-path); place the junction at that one.                    var s_to_boundary;                    var s_from_junction;                    if (!pathIsSmoothAcross(toFrenetPath, currentEdge, nextEdgeIdx))                    {                        s_to_boundary   = toFrenetPath.edgeData[boundaryEdgeIdx].startArcLength;                        s_from_junction = fromRefArc + (s_to_boundary - toRefArc);                    }                    else                    {                        var fromK  = mappedData[segEndIdx].fromEdgeIndex;                        var fromK1 = mappedData[segEndIdx + 1].fromEdgeIndex;                        s_from_junction = fromFrenetPath.edgeData[(fromK1 > fromK) ? fromK + 1 : fromK].startArcLength;                        s_to_boundary   = toRefArc + (s_from_junction - fromRefArc);                    }
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
                    // FIX: Use evEdgeTangentLines for exact position and tangent (was: linear
                    // interpolation between samples + finite-difference tangent, which causes G1 error)
                    var junctionParam    = (segEndIdx + t) / (numSamples - 1);
                    var srcJunctionParam = srcFlipped ? 1 - junctionParam : junctionParam;
                    // One batched call: the junction, the 2 oversampling points and the +/- pair of the
                    // curvature finite difference (these were four separate kernel calls).
                    var jEpsB       = 0.005;
                    var extraParams = [];
                    for (var exB = 1; exB <= 2; exB += 1)
                    {
                        var extraParamB = segEndIdx / (numSamples - 1) + (exB / 3.0) * (junctionParam - segEndIdx / (numSamples - 1));
                        extraParams = append(extraParams, srcFlipped ? 1 - extraParamB : extraParamB);
                    }
                    var junctionLines = evEdgeTangentLines(context, {
                        "edge"       : sourceCurveArray[i],
                        "parameters" : concatenateArrays([[srcJunctionParam], extraParams,
                            [max([0, srcJunctionParam - jEpsB]), min([1, srcJunctionParam + jEpsB])]])
                    });
                    var junctionLine     = junctionLines[0];
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
                    var junctionOffDir  = toFrameResult_j.frame.xAxis;

                    // Phase 4: junction-aware oversampling — inject 2 extra mapped points between
                    // the last regular sample and the junction for better curvature fit near junctions.
                    {
                        var nExtra = 2;
                        for (var ex = 1; ex <= nExtra; ex += 1)
                        {
                            var alpha      = ex / (nExtra + 1.0);
                            var extraLine     = junctionLines[ex];   // batched above
                            var sFrom_extra   = mappedData[segEndIdx].sFrom + alpha * (s_from_junction - mappedData[segEndIdx].sFrom);
                            var fromResult_e  = getFrameAtArcLength(context, fromFrenetPath, sFrom_extra);
                            var localCoords_e = worldPointToFrenet(extraLine.origin, fromResult_e);
                            var s_to_extra    = toRefArc + (sFrom_extra - fromRefArc);
                            var toResult_e    = getFrameAtArcLength(context, toFrenetPath, s_to_extra);
                            var toSign_e      = definition.flipToNormal ? -1 * toResult_e.sign : toResult_e.sign;
                            var toFrameResult_e = (toSign_e != fromResult_e.sign)
                                ? mergeMaps(toResult_e, { "frame": coordSystem(toResult_e.frame.origin, -1 * toResult_e.frame.xAxis, toResult_e.frame.zAxis) })
                                : toResult_e;
                            segPoints     = append(segPoints,     frenetPointToWorld(localCoords_e, toFrameResult_e));
                            segOffsetDirs = append(segOffsetDirs, toFrameResult_e.frame.xAxis);
                        }
                    }

                    // Phase 2: compute and map source curvature at junction via finite differences.
                    // kappaSrc = dT/ds ≈ (T(t+ε) - T(t-ε)) / arc_length_step  (units: 1/m)
                    {
                        var kLines = [junctionLines[3], junctionLines[4]];   // batched above
                        var dsJ    = norm(kLines[1].origin - kLines[0].origin);  // chord between evaluation points
                        var deltaT     = kLines[1].direction - kLines[0].direction;
                        if (dsJ > 1e-10 * meter && norm(deltaT) > 1e-10)
                        {
                            var kappaSrc  = deltaT / dsJ;
                            junctionCurvature = mapEdgeJunctionCurvature(kappaSrc, fromResult_j, toFrameResult_j);
                        }
                        else
                        {
                            junctionCurvature = undefined;
                        }
                    }

                    segPoints         = append(segPoints,     junctionWorldPt);
                    segOffsetDirs     = append(segOffsetDirs, junctionOffDir);
                    junctionPt        = junctionWorldPt;
                    // Each side of the junction gets its own frame and curvature (before / after).                    junctionTangentInfo = exactTangentAt(context, fromFrenetPath, toFrenetPath, s_from_junction, s_to_boundary, -1,                        definition.flipToNormal, srcTangent, pt_junction, junctionWorldPt);                    nextStartInfo       = exactTangentAt(context, fromFrenetPath, toFrenetPath, s_from_junction, s_to_boundary, 1,                        definition.flipToNormal, srcTangent, pt_junction, junctionWorldPt);                    junctionTangent     = junctionTangentInfo.tangent;
                    junctionOffsetDir = junctionOffDir;
                }
                else
                {
                    // Last span — constrain end tangent from source edge at parameter 1
                    var endDirection    = srcFlipped ? sampleResult.startTangent : sampleResult.endTangent;   // sampled at parameter 0 / 1
                    var endSrcTangent = srcFlipped ? -1 * endDirection : endDirection;
                    var s_from_end        = mappedData[size(mappedData) - 1].sFrom;
                    var fromResult_end    = getFrameAtArcLength(context, fromFrenetPath, s_from_end);
                    var s_to_end          = toRefArc + (s_from_end - fromRefArc);
                    var toResult_end      = getFrameAtArcLength(context, toFrenetPath, s_to_end);
                    var toSign_end        = definition.flipToNormal ? -1 * toResult_end.sign : toResult_end.sign;
                    var toFrameResult_end = (toSign_end != fromResult_end.sign)
                        ? mergeMaps(toResult_end, { "frame": coordSystem(toResult_end.frame.origin, -1 * toResult_end.frame.xAxis, toResult_end.frame.zAxis) })
                        : toResult_end;
                    junctionTangentInfo = exactTangentAt(context, fromFrenetPath, toFrenetPath, s_from_end, s_to_end, 0,
                        definition.flipToNormal, endSrcTangent,
                        sampleResult.points[srcFlipped ? 0 : size(sampleResult.points) - 1], mappedData[size(mappedData) - 1].point);
                    junctionTangent = junctionTangentInfo.tangent;
                }

                // Remove near-coincident points (segOffsetDirs kept in sync).
                {
                    var minSep      = 1e-6 * meter;
                    var dedupedPts  = [segPoints[0]];
                    var dedupedDirs = [segOffsetDirs[0]];
                    for (var k = 1; k < size(segPoints); k += 1)
                    {
                        if (norm(segPoints[k] - dedupedPts[size(dedupedPts) - 1]) >= minSep)
                        {
                            dedupedPts  = append(dedupedPts,  segPoints[k]);
                            dedupedDirs = append(dedupedDirs, segOffsetDirs[k]);
                        }
                    }
                    segPoints     = dedupedPts;
                    segOffsetDirs = dedupedDirs;
                }

                // Never discard a span. Dropping it opened a gap in the wrapped curve, and
                // did so with no message at all here -- not even a debug-gated one.
                var fitDegree = approxDegree;
                if (size(segPoints) < approxDegree + 1)
                {
                    fitDegree = max([1, size(segPoints) - 1]);
                    println("WARNING: wrapAndLoft span " ~ toString(i) ~ "." ~ toString(segCount)
                        ~ " has " ~ toString(size(segPoints)) ~ " point(s), needs "
                        ~ toString(approxDegree + 1) ~ " for degree " ~ toString(approxDegree)
                        ~ "; fitting at degree " ~ toString(fitDegree) ~ " instead.");
                }

                if (size(segPoints) >= 2)
                {
                    // Scale for derivative constraints: total chord length of this segment.
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
                        "degree"           : fitDegree,
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
                            segPoints[k] + offsetSign * definition.primaryOffset * segOffsetDirs[k]);
                        if (definition.secondDirection && definition.secondOffset > 0 * millimeter)
                        {
                            secondaryOffsetPoints = append(secondaryOffsetPoints,
                                segPoints[k] - offsetSign * definition.secondOffset * segOffsetDirs[k]);
                        }
                    }

                    // Offset-edge gap diagnostic: compare curve i's LAST span primaryOffset[-1]
                    // to curve i+1's FIRST span primaryOffset[0] -- these should now match after
                    // the cross-curve offset-direction weld.
                    if (definition.debugWrappedCurves || definition.debugFromBSplines)
                    {
                        println("  primaryOffset[0]="  ~ toString(primaryOffsetPoints[0]) ~
                                " primaryOffset[-1]=" ~ toString(primaryOffsetPoints[size(primaryOffsetPoints) - 1]));
                        println("  offsetDir[0]="      ~ toString(segOffsetDirs[0]) ~
                                " offsetDir[-1]="      ~ toString(segOffsetDirs[size(segOffsetDirs) - 1]));
                    }

                    // Offset curves don't need exact endpoint interpolation — use unconstrained fit
                    var offsetApproxBase = {
                        "tolerance"        : definition.approximationTolerance,
                        "maxControlPoints" : definition.approximationMaxCPs,
                        "degree"           : fitDegree,
                        "isPeriodic"       : false
                    };

                    // Offset curves need derivative constraints scaled to their own chord, not
                    // the wrapped curve's chord (targetDef carries the wrong approxScale).
                    var primaryOffsetChord = 0 * meter;
                    for (var k = 0; k < size(primaryOffsetPoints) - 1; k += 1)
                        primaryOffsetChord += norm(primaryOffsetPoints[k + 1] - primaryOffsetPoints[k]);
                    var primaryOffsetTargetDef = { "positions": primaryOffsetPoints };
                    if (carryOverTangent != undefined)
                        primaryOffsetTargetDef = mergeMaps(primaryOffsetTargetDef, { "startDerivative": offsetCurveTangent(carryOverInfo, offsetSign * definition.primaryOffset, segOffsetDirs[0]) * primaryOffsetChord });
                    if (junctionTangent != undefined)
                        primaryOffsetTargetDef = mergeMaps(primaryOffsetTargetDef, { "endDerivative": offsetCurveTangent(junctionTangentInfo, offsetSign * definition.primaryOffset, segOffsetDirs[size(segOffsetDirs) - 1]) * primaryOffsetChord });
                    var primaryOffsetApproxDef = mergeMaps(offsetApproxBase, { "targets": [approximationTarget(primaryOffsetTargetDef)] });
                    var coupledFit         = definition.exactRuledSurface == true;
                    var primaryOffsetCurve = coupledFit ? undefined : approximateSpline(context, primaryOffsetApproxDef)[0];
                    // Snap CP[0] and CP[-1] to exact input positions. For a clamped BSpline,
                    // CP[0] = C(t0) and CP[-1] = C(t_end) exactly, so this costs nothing
                    // geometrically but makes boundary edges bit-identical across source curves,
                    // which is required for the surface union to stitch correctly.
                    if (!coupledFit)                    {
                        var cps = primaryOffsetCurve.controlPoints;
                        var m   = size(cps) - 1;
                        var snapped = [];
                        for (var ci = 0; ci <= m; ci += 1)
                        {
                            if (ci == 0)
                                snapped = append(snapped, primaryOffsetPoints[0]);
                            else if (ci == m)
                                snapped = append(snapped, primaryOffsetPoints[size(primaryOffsetPoints) - 1]);
                            else
                                snapped = append(snapped, cps[ci]);
                        }
                        primaryOffsetCurve = mergeMaps(primaryOffsetCurve, { "controlPoints": snapped });
                    }

                    var secondaryOffsetCurve = undefined;
                    if (definition.secondDirection && definition.secondOffset > 0 * millimeter)
                    {
                        var secondaryOffsetChord = 0 * meter;
                        for (var k = 0; k < size(secondaryOffsetPoints) - 1; k += 1)
                            secondaryOffsetChord += norm(secondaryOffsetPoints[k + 1] - secondaryOffsetPoints[k]);
                        var secondaryOffsetTargetDef = { "positions": secondaryOffsetPoints };
                        if (carryOverTangent != undefined)
                            secondaryOffsetTargetDef = mergeMaps(secondaryOffsetTargetDef, { "startDerivative": offsetCurveTangent(carryOverInfo, -1 * offsetSign * definition.secondOffset, segOffsetDirs[0]) * secondaryOffsetChord });
                        if (junctionTangent != undefined)
                            secondaryOffsetTargetDef = mergeMaps(secondaryOffsetTargetDef, { "endDerivative": offsetCurveTangent(junctionTangentInfo, -1 * offsetSign * definition.secondOffset, segOffsetDirs[size(segOffsetDirs) - 1]) * secondaryOffsetChord });
                        var secondaryOffsetApproxDef = mergeMaps(offsetApproxBase, { "targets": [approximationTarget(secondaryOffsetTargetDef)] });
                        secondaryOffsetCurve         = coupledFit ? undefined : approximateSpline(context, secondaryOffsetApproxDef)[0];
                        if (!coupledFit)
                        {
                            var cps = secondaryOffsetCurve.controlPoints;
                            var m   = size(cps) - 1;
                            var snapped = [];
                            for (var ci = 0; ci <= m; ci += 1)
                            {
                                if (ci == 0)
                                    snapped = append(snapped, secondaryOffsetPoints[0]);
                                else if (ci == m)
                                    snapped = append(snapped, secondaryOffsetPoints[size(secondaryOffsetPoints) - 1]);
                                else
                                    snapped = append(snapped, cps[ci]);
                            }
                            secondaryOffsetCurve = mergeMaps(secondaryOffsetCurve, { "controlPoints": snapped });
                        }
                    }

                    // Exact ruled surface: the wrapped and offset curves fitted in ONE call share their knots
                    // exactly (std: multi-target fits are consistently parameterized), so each span\'s surface
                    // can be written as the ruled B-spline between them. Every member has the same point count
                    // and the same derivative constraints present, as std requires.
                    if (coupledFit)
                    {
                        var familyTargets = [approximationTarget(wrappedTargetDef), approximationTarget(primaryOffsetTargetDef)];
                        var withSecond    = definition.secondDirection && definition.secondOffset > 0 * millimeter;
                        if (withSecond)
                        {
                            // Rebuilt here: the separate secondary target is declared inside its own block.
                            var secondChord = 0 * meter;
                            for (var k = 0; k < size(secondaryOffsetPoints) - 1; k += 1)
                            {
                                secondChord += norm(secondaryOffsetPoints[k + 1] - secondaryOffsetPoints[k]);
                            }
                            var secondDef = { "positions": secondaryOffsetPoints };
                            if (carryOverTangent != undefined)
                            {
                                secondDef = mergeMaps(secondDef, { "startDerivative": offsetCurveTangent(carryOverInfo, -1 * offsetSign * definition.secondOffset, segOffsetDirs[0]) * secondChord });
                            }
                            if (junctionTangent != undefined)
                            {
                                secondDef = mergeMaps(secondDef, { "endDerivative": offsetCurveTangent(junctionTangentInfo, -1 * offsetSign * definition.secondOffset, segOffsetDirs[size(segOffsetDirs) - 1]) * secondChord });
                            }
                            familyTargets = append(familyTargets, approximationTarget(secondDef));
                        }
                        var family = approximateSpline(context, mergeMaps(offsetApproxBase, { "targets": familyTargets }));
                        mappedCurve        = snapEndControlPoints(family[0], segPoints[0], segPoints[size(segPoints) - 1]);
                        primaryOffsetCurve = snapEndControlPoints(family[1], primaryOffsetPoints[0], primaryOffsetPoints[size(primaryOffsetPoints) - 1]);
                        if (withSecond)
                        {
                            secondaryOffsetCurve = snapEndControlPoints(family[2], secondaryOffsetPoints[0], secondaryOffsetPoints[size(secondaryOffsetPoints) - 1]);
                        }
                    }
                    
                    // Capture and pre-increment so a failed op can never reuse the same Id
                    var thisSegCount = segCount;
                    segCount += 1;

                    // All or nothing: the span's curves are created first and recorded only once
                    // every one exists. Recording the wrapped curve before its offsets were made
                    // let one failed offset shift every later loft pair out of step.
                    var wrappedId         = id + (toString(i) ~ "_" ~ toString(thisSegCount) ~ "wrappedCurve");
                    var primaryOffsetId   = id + (toString(i) ~ "_" ~ toString(thisSegCount) ~ "primaryOffset");
                    var secondaryOffsetId = id + (toString(i) ~ "_" ~ toString(thisSegCount) ~ "secondaryOffset");
                    try
                    {
                        opCreateBSplineCurve(context, wrappedId, { "bSplineCurve": mappedCurve });
                        opCreateBSplineCurve(context, primaryOffsetId, { "bSplineCurve": primaryOffsetCurve });
                        if (secondaryOffsetCurve != undefined)
                        {
                            opCreateBSplineCurve(context, secondaryOffsetId, { "bSplineCurve": secondaryOffsetCurve });
                        }

                        allWrappedSegQueries = append(allWrappedSegQueries, qCreatedBy(wrappedId, EntityType.EDGE));
                        allWrappedSegBodies  = append(allWrappedSegBodies,  qCreatedBy(wrappedId, EntityType.BODY));
                        spanIsFast           = append(spanIsFast, false);
                        wrappedBSplines       = append(wrappedBSplines,       mappedCurve);                        primaryBSplines       = append(primaryBSplines,       primaryOffsetCurve);                        secondaryBSplines     = append(secondaryBSplines,     secondaryOffsetCurve);
                        wrappedIds            = append(wrappedIds,            wrappedId);
                        allJunctionCurvatures = append(allJunctionCurvatures, junctionCurvature);
                        allPrimaryOffsetSegQueries = append(allPrimaryOffsetSegQueries, qCreatedBy(primaryOffsetId, EntityType.EDGE));
                        allPrimaryOffsetSegBodies  = append(allPrimaryOffsetSegBodies,  qCreatedBy(primaryOffsetId, EntityType.BODY));
                        if (secondaryOffsetCurve != undefined)
                        {
                            allSecondaryOffsetSegQueries = append(allSecondaryOffsetSegQueries, qCreatedBy(secondaryOffsetId, EntityType.EDGE));
                            allSecondaryOffsetSegBodies  = append(allSecondaryOffsetSegBodies,  qCreatedBy(secondaryOffsetId, EntityType.BODY));
                        }
                    }
                    catch (e)
                    {
                        // Remove whatever part of this span was made before the failure.
                        const partial = qUnion([qCreatedBy(wrappedId, EntityType.BODY), qCreatedBy(primaryOffsetId, EntityType.BODY),
                                    qCreatedBy(secondaryOffsetId, EntityType.BODY)]);
                        if (!isQueryEmpty(context, partial))
                        {
                            opDeleteBodies(context, id + (toString(i) ~ "_" ~ toString(thisSegCount) ~ "discardPartial"), { "entities": partial });
                        }
                        println("ERROR: wrapAndLoft opCreateBSplineCurve BAD_GEOMETRY - " ~ e);
                        println("  curve i=" ~ i ~ "  seg=" ~ thisSegCount ~
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
                            if (norm(segPoints[di + 1] - segPoints[di]) > 1e-10 * meter)
                                addDebugLine(context, segPoints[di], segPoints[di + 1], DebugColor.RED);
                        }
                        for (var di = 0; di < size(segPoints); di += 1)
                        {
                            addDebugPoint(context, segPoints[di], DebugColor.MAGENTA);
                        }
                    }
                }
                else
                {
                    // One point cannot be a curve. This is the only remaining way to lose a
                    // span, and it is always reported.
                    println("WARNING: wrapAndLoft span " ~ toString(i) ~ "." ~ toString(segCount)
                        ~ " has only " ~ toString(size(segPoints))
                        ~ " point(s) and cannot be fitted; the output will have a gap here.");
                }

                segStartIdx = segEndIdx + 1;
            }

            // Use actual successful span count, not segCount (which includes failed ops)
            segCountPerSourceCurve = append(segCountPerSourceCurve, size(allWrappedSegQueries) - wrappedCountBefore);
        }

        // Recommend the fix when it is off and slivers are actually present (default is on,
        // so an untouched feature never sees this).
        if (!definition.fixClustering && clusteredSpanCount > 0)
        {
            reportFeatureInfo(context, id, toString(clusteredSpanCount) ~
                " span(s) have clustered control points (a source endpoint met a reference line/arc seam). " ~
                "Enable 'Fix control-point clustering' to merge them.");
        }

        // No G2 junction smoothing. Span junctions only remain at to-path corners and curvature
        // jumps, from-path corners, and joints between source curves -- all places where the
        // continuity is the geometry's, not something to force. Each side's end tangent is
        // exact instead (mapTangentExact).

        // ===== Per-source-curve loft =====
        // One opLoft per source curve — each uses only its own span queries, preventing
        // opLoft from chaining all spans across all source curves into one huge surface.
        if (size(allWrappedSegQueries) > 0)
        {
            var allLoftBodyQueries = [];
            var segOffset = 0;
            for (var li = 0; li < size(sourceCurveArray); li += 1)
            {
                var iCount = segCountPerSourceCurve[li];
                if (iCount == 0)
                {
                    segOffset += iCount;
                    continue;
                }

                var iWrappedQueries   = [];
                var iPrimaryQueries   = [];
                var iSecondaryQueries = [];
                for (var k = 0; k < iCount; k += 1)
                {
                    var idx = segOffset + k;
                    iWrappedQueries = append(iWrappedQueries, allWrappedSegQueries[idx]);
                    if (idx < size(allPrimaryOffsetSegQueries))
                        iPrimaryQueries = append(iPrimaryQueries, allPrimaryOffsetSegQueries[idx]);
                    if (idx < size(allSecondaryOffsetSegQueries))
                        iSecondaryQueries = append(iSecondaryQueries, allSecondaryOffsetSegQueries[idx]);
                }

                var loftProfile1 = qUnion(iWrappedQueries);
                var loftProfile2 = qUnion(iPrimaryQueries);
                if (definition.secondDirection && size(iSecondaryQueries) > 0)
                {
                    loftProfile1 = qUnion(iPrimaryQueries);
                    loftProfile2 = qUnion(iSecondaryQueries);
                }

                // Exact ruled surface per span, when every span of this curve was fitted as a family.
                var ruledDone = false;
                if (definition.exactRuledSurface == true && size(iWrappedQueries) > 0 && size(iPrimaryQueries) > 0)
                {
                    var allFitted = true;
                    var slowIdx   = [];
                    for (var k = 0; k < iCount; k += 1)
                    {
                        var pos = segOffset + k;
                        if (spanIsFast[pos]) { allFitted = false; break; }
                        var before = 0;
                        for (var q = 0; q < pos; q += 1) { if (spanIsFast[q]) { before += 1; } }
                        slowIdx = append(slowIdx, pos - before);
                    }
                    if (allFitted)
                    {
                        var ruledId = id + ("ruled_" ~ toString(li));
                        ruledDone = true;
                        for (var k = 0; k < iCount && ruledDone; k += 1)
                        {
                            var b = slowIdx[k];
                            var useSecond = definition.secondDirection && b < size(secondaryBSplines) && secondaryBSplines[b] != undefined;
                            var profA = useSecond ? primaryBSplines[b] : wrappedBSplines[b];
                            var profB = useSecond ? secondaryBSplines[b] : primaryBSplines[b];
                            ruledDone = profA != undefined && profB != undefined
                                && ruledSurfaceBetween(context, ruledId + ("s" ~ toString(k)), profA, profB);
                        }
                        if (ruledDone)
                        {
                            allLoftBodyQueries = append(allLoftBodyQueries, qCreatedBy(ruledId, EntityType.BODY));
                        }
                        else if (!isQueryEmpty(context, qCreatedBy(ruledId, EntityType.BODY)))
                        {
                            // A refused patch: remove this curve's partial surfaces and loft it instead.
                            opDeleteBodies(context, ruledId + "discard", { "entities": qCreatedBy(ruledId, EntityType.BODY) });
                        }
                    }
                }

                if (!ruledDone && size(iWrappedQueries) > 0 && size(iPrimaryQueries) > 0)
                {
                    try
                    {
                        opLoft(context, id + ("loft_" ~ toString(li)), {
                            "bodyType"          : ToolBodyType.SURFACE,
                            "profileSubqueries" : [loftProfile1, loftProfile2]
                        });
                        allLoftBodyQueries = append(allLoftBodyQueries,
                            qCreatedBy(id + ("loft_" ~ toString(li)), EntityType.BODY));
                    }
                    catch (e)
                    {
                        println("ERROR: wrapAndLoft loft " ~ toString(li) ~ " failed - " ~ toString(e));
                    }
                }

                segOffset += iCount;
            }

            // Union all per-source-curve surfaces into one body with N faces.
            // Requires boundary edges to be exactly coincident — guaranteed by the
            // endpoint-snapping step on offset curves above.
            if (size(allLoftBodyQueries) > 1)
            {
                try
                {
                    opBoolean(context, id + "unionSurfaces", {
                        "operationType" : BooleanOperationType.UNION,
                        "tools"         : qUnion(allLoftBodyQueries)
                    });
                }
                catch (e)
                {
                    println("Surface union failed (surfaces may not share edges): " ~ toString(e));
                }
            }

            // Wire output: extract only the wires that are kept. (All three used to be
            // extracted and then deleted again in "None", and the offsets in "Keep wrapped".)
            var keepWrapped = definition.outputCurveMode != OutputCurveMode.NONE;
            var keepOffsets = definition.outputCurveMode == OutputCurveMode.KEEP_ALL;
            if (keepWrapped)
            {
                opExtractWires(context, id + "wrappedWires", { "edges": qUnion(allWrappedSegQueries) });
            }
            if (keepOffsets)
            {
                opExtractWires(context, id + "primaryOffsetWires", { "edges": qUnion(allPrimaryOffsetSegQueries) });
                if (size(allSecondaryOffsetSegQueries) > 0)
                {
                    opExtractWires(context, id + "secondaryOffsetWires", { "edges": qUnion(allSecondaryOffsetSegQueries) });
                }
            }

            // Delete original segment bodies -- the extracted wire bodies (if any) are the output
            var allSegBodies = qUnion([qUnion(allWrappedSegBodies), qUnion(allPrimaryOffsetSegBodies)]);
            if (size(allSecondaryOffsetSegBodies) > 0)
            {
                allSegBodies = qUnion([allSegBodies, qUnion(allSecondaryOffsetSegBodies)]);
            }
            opDeleteBodies(context, id + "deleteSegBodies", { "entities": allSegBodies });
        }

        // ===== Cleanup planar projected from-curves =====
        if (projectedBodyQuery != undefined)
        {
            opDeleteBodies(context, id + "cleanupProjected", { "entities" : projectedBodyQuery });
        }
     });


