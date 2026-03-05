FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/path.fs", version : "2878.0");
import(path : "onshape/std/approximationUtils.fs", version : "2878.0");
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

export const samplingDensityBounds = {(millimeter) : [.1, 1, 10]} as LengthBoundSpec;

export enum SamplingMode
{
    annotation { "Name" : "Length-based" } LENGTH_BASED,
    annotation { "Name" : "Control point-based" } CP_BASED
}

export const cpMultiplierBounds = { (unitless) : [1, 3, 50] } as IntegerBoundSpec;

/**
 * Expands a mixed edge/wire-body/composite selection into a flat edge query.
 * - Direct edges pass through unchanged.
 * - Wire bodies contribute all of their owned edges.
 * - Composite parts contribute edges owned by any wire body they contain.
 */
export function expandEdgeQuery(q is Query) returns Query
{
    var directEdges = qEntityFilter(q, EntityType.EDGE);

    var wireBodies = qBodyType(qEntityFilter(q, EntityType.BODY), BodyType.WIRE);
    var wireEdges = qOwnedByBody(wireBodies, EntityType.EDGE);

    var composites = qBodyType(qEntityFilter(q, EntityType.BODY), BodyType.COMPOSITE);
    var compositeWireEdges = qOwnedByBody(
        qBodyType(qContainedInCompositeParts(composites), BodyType.WIRE),
        EntityType.EDGE);

    return qUnion([directEdges, wireEdges, compositeWireEdges]);
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
            // Guard: skip inflection detection for near-linear BSplines.
            // Nearly-collinear control polygons produce unreliable cross products in
            // bSplineMayHaveInflection, generating spurious sign flips in getFrameAtArcLength.
            // Threshold: max lateral deviation < 0.1% of edge length.
            var cps          = bspline.controlPoints;
            var nCPs         = size(cps);
            var p0           = cps[0];
            var p1           = cps[nCPs - 1];
            var chord        = p1 - p0;
            var chordLen     = norm(chord);
            var isNearLinear = false;
            if (chordLen.value > 1e-10)
            {
                var chordDir = normalize(chord);
                var maxDev   = 0 * meter;
                for (var j = 1; j < nCPs - 1; j += 1)
                {
                    var diff    = cps[j] - p0;
                    var lateral = norm(diff - dot(diff, chordDir) * chordDir);
                    if (lateral > maxDev)  maxDev = lateral;
                }
                isNearLinear = (maxDev < 0.001 * length);
            }

            // Promote near-linear BSplines to line mode so getFrameAtArcLength uses a
            // stable, consistent xAxis instead of computeFrenetFrame's noisy normal.
            // (computeFrenetFrame is unreliable when curvature ≈ 0.)
            if (isNearLinear)
            {
                isLine = true;
                var lineDir = normalize(chord);  // chordLen > 1e-10 guaranteed here
                if (!stdDir)
                    lineDir = -1 * lineDir;
                var origin  = stdDir ? p0 : p1;
                lineFrame   = lineFrenetFrame({ "origin": origin, "direction": lineDir });
                lineStartPt = origin;
            }

            if (!isNearLinear && bSplineMayHaveInflection(bspline))
            {
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

    // 4.5. Post-process: set line xAxis from adjacent curve context
    //      For each line edge, if an adjacent edge is a curve, borrow that curve's
    //      Frenet normal at the shared vertex so the frame is continuous at the junction.
    //      Prefer the NEXT edge (arc after line drives the normal).
    for (var i = 0; i < size(edgeData); i += 1)
    {
        if (!edgeData[i].isLine)
            continue;

        var contextXAxis = undefined;

        // Prefer next edge (line followed by arc → arc drives the normal)
        if (i + 1 < size(edgeData) && !edgeData[i + 1].isLine)
        {
            var nextEd    = edgeData[i + 1];
            // Traversal start of next edge: arcFrac=0 if stdDir, arcFrac=1 if reversed
            var nextArcFrac = nextEd.stdDir ? 0 : 1;
            contextXAxis = evEdgeCurvature(context, {
                "edge"                      : nextEd.query,
                "parameter"                 : nextArcFrac,
                "arcLengthParameterization" : true
            }).frame.xAxis;
        }
        else if (i - 1 >= 0 && !edgeData[i - 1].isLine)
        {
            var prevEd    = edgeData[i - 1];
            // Traversal end of prev edge: arcFrac=1 if stdDir, arcFrac=0 if reversed
            var prevArcFrac = prevEd.stdDir ? 1 : 0;
            contextXAxis = evEdgeCurvature(context, {
                "edge"                      : prevEd.query,
                "parameter"                 : prevArcFrac,
                "arcLengthParameterization" : true
            }).frame.xAxis;
        }
        // else: isolated line or line–line — keep world-axis heuristic

        if (contextXAxis != undefined)
        {
            var lf = edgeData[i].lineFrame;
            // Project contextXAxis onto the plane perpendicular to the line tangent.
            // This ensures exact perpendicularity for coordSystem even when the BSpline
            // tangent at the junction drifts numerically from the line direction.
            var tangent    = lf.zAxis;
            var perpXAxis  = contextXAxis - dot(contextXAxis, tangent) * tangent;
            if (norm(perpXAxis) > 1e-6)
            {
                edgeData[i] = mergeMaps(edgeData[i], {
                    "lineFrame": coordSystem(lf.origin, normalize(perpXAxis), tangent)
                });
            }
            // else: contextXAxis nearly parallel to tangent (degenerate) — keep heuristic
        }
    }

    // 4.6. Validate normal continuity at each junction
    //      Raw xAxis values (before sign correction) must be nearly parallel at each joint.
    //      abs(dot) catches both parallel and antiparallel as valid — sign tracking handles
    //      the antiparallel case downstream.
    var normalContinuityTol = 0.9; // cos(~26°)

    for (var i = 0; i < size(edgeData) - 1; i += 1)
    {
        var xEnd;
        if (edgeData[i].isLine)
        {
            xEnd = edgeData[i].lineFrame.xAxis;
        }
        else
        {
            var edI  = edgeData[i];
            var nKI  = size(edI.bspline.knots);
            var degI = edI.bspline.degree;
            // Traversal end of edge i
            var pI   = edI.stdDir ? edI.bspline.knots[nKI - degI - 1]
                                  : edI.bspline.knots[degI];
            xEnd = computeFrenetFrame(edI.bspline, pI).frame.xAxis;
        }

        var xStart;
        if (edgeData[i + 1].isLine)
        {
            xStart = edgeData[i + 1].lineFrame.xAxis;
        }
        else
        {
            var edJ  = edgeData[i + 1];
            var nKJ  = size(edJ.bspline.knots);
            var degJ = edJ.bspline.degree;
            // Traversal start of edge i+1
            var pJ   = edJ.stdDir ? edJ.bspline.knots[degJ]
                                  : edJ.bspline.knots[nKJ - degJ - 1];
            xStart = computeFrenetFrame(edJ.bspline, pJ).frame.xAxis;
        }

        if (abs(dot(xEnd, xStart)) < normalContinuityTol)
        {
            throw regenError("Reference edge normals are not coplanar at junction " ~ toString(i) ~
                             " — use a planar edge chain.",
                             qUnion([edgeData[i].query, edgeData[i + 1].query]));
        }
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

    // 1. Out-of-bounds: extrapolate tangentially from the boundary frame
    if (arcLength < 0 * meter || arcLength > totalLength)
    {
        var boundaryArc = (arcLength < 0 * meter) ? 0 * meter : totalLength;
        var overflow    = arcLength - boundaryArc;   // negative at start, positive at end
        var boundary    = getFrameAtArcLength(context, frenetPath, boundaryArc);
        var extPos      = boundary.frame.origin + overflow * boundary.frame.zAxis;
        var extFrame    = coordSystem(extPos, boundary.frame.xAxis, boundary.frame.zAxis);
        return mergeMaps(boundary, { "frame": extFrame });
    }

    // 2. Find the edge whose span contains arcLength
    //    (last edge where startArcLength <= arcLength)
    var edgeIdx = 0;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        if (edgeData[i].startArcLength <= arcLength)
            edgeIdx = i;
    }

    var edgeDat = edgeData[edgeIdx];

    // 3. Local arc-length within this edge (from its traversal start)
    var localArc = arcLength - edgeDat.startArcLength;

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
        // 6b. Curved: evaluate exact Frenet frame on actual edge geometry
        // arcFrac maps local traversal arc-length → [0,1] arc-length fraction on edge
        // Clamp to [0,1] to guard against floating-point overshoot at the boundary
        // (e.g. arcLength == totalLength but float subtraction gives localArc = length + eps).
        var arcFrac = localArc.value / edgeDat.length.value;
        if (arcFrac < 0) { arcFrac = 0; }
        if (arcFrac > 1) { arcFrac = 1; }
        if (!edgeDat.stdDir)
            arcFrac = 1 - arcFrac;  // traversal is reversed: start=1, end=0

        var rawResult = evEdgeCurvature(context, {
            "edge"                      : edgeDat.query,
            "parameter"                 : arcFrac,
            "arcLengthParameterization" : true
        });

        if (!edgeDat.stdDir)
        {
            // Flip zAxis so it points in the traversal direction
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
// projectOntoFrenetPath
// ============================================================================

/**
 * Project a point onto a FrenetPath and return the global arc-length position.
 *
 * Tests each edge's BSpline, picks the closest, then converts the BSpline
 * parameter to arc-length accounting for traversal direction.
 *
 * @param frenetPath {map}    - result from buildFrenetPath
 * @param point      {Vector} - query point with units
 * @param hint               - optional map { "edgeIndex", "param" } from previous call;
 *                             when provided, only scans hint edge ± 1 neighbor (warm-start).
 *                             Pass undefined for full scan.
 * @returns {map} :
 *   "arcLength" {ValueWithUnits} - arc-length along the path
 *   "hint"      {map}            - { "edgeIndex", "param" } for next call
 */
export function projectOntoFrenetPath(frenetPath is map, point is Vector, hint) returns map
{
    var edgeData    = frenetPath.edgeData;
    var nEdges      = size(edgeData);
    var bestDist    = inf * meter;
    var bestEdgeIdx = 0;
    var bestParam   = 0;

    if (hint != undefined && hint.edgeIndex >= 0 && hint.edgeIndex < nEdges)
    {
        // Warm-start: only scan hinted edge ± 1 neighbor to handle edge crossings
        var iMin = max([0, hint.edgeIndex - 1]);
        var iMax = min([nEdges - 1, hint.edgeIndex + 1]);
        for (var i = iMin; i <= iMax; i += 1)
        {
            var result = projectPointOnCurve(edgeData[i].bspline, point, {});
            if (result.distance < bestDist)
            {
                bestDist    = result.distance;
                bestEdgeIdx = i;
                bestParam   = result.parameter;
            }
        }
    }
    else
    {
        // Full scan across all edges
        for (var i = 0; i < nEdges; i += 1)
        {
            var result = projectPointOnCurve(edgeData[i].bspline, point, {});
            if (result.distance < bestDist)
            {
                bestDist    = result.distance;
                bestEdgeIdx = i;
                bestParam   = result.parameter;
            }
        }
    }

    var edgeDat = edgeData[bestEdgeIdx];

    // Convert BSpline parameter → arc-length from BSpline uMin
    var physFrac      = arcLengthFraction(edgeDat.arcLengthTable, bestParam);
    var physArcLength = physFrac * edgeDat.length;

    // Convert to local arc from traversal start
    var localArc = edgeDat.stdDir ? physArcLength : (edgeDat.length - physArcLength);

    return {
        "arcLength" : edgeDat.startArcLength + localArc,
        "hint"      : { "edgeIndex": bestEdgeIdx, "param": bestParam }
    };
}


// ============================================================================
// getRefPoint
// ============================================================================

/**
 * Extract a world point from a vertex, mate connector, or planar face query.
 *
 * @param context  {Context}
 * @param refQuery {Query} - vertex, mate connector, or planar face
 * @returns {Vector} - 3D position with units
 */
export function getRefPoint(context is Context, refQuery is Query) returns Vector
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


// ============================================================================
// mapWorldPoints  (public API)
// ============================================================================

/**
 * Map an array of 3D world points from one FrenetPath to another using the
 * same per-point logic as the main wrapCurve mapping loop.
 *
 * @param context        {Context}
 * @param fromFrenetPath {map}     - result from buildFrenetPath for the source path
 * @param toFrenetPath   {map}     - result from buildFrenetPath for the target path
 * @param fromRefArc     {ValueWithUnits} - arc-length of the reference point on the from-path
 * @param toRefArc       {ValueWithUnits} - arc-length of the reference point on the to-path
 * @param flipToNormal   {boolean} - when true, invert the to-path normal sign
 * @param points         {array}   - array of Vector (3D world points with units)
 * @returns {array}                - array of Vector (mapped world points, same length)
 */
export function mapWorldPoints(context is Context,
                               fromFrenetPath is map,
                               toFrenetPath   is map,
                               fromRefArc     is ValueWithUnits,
                               toRefArc       is ValueWithUnits,
                               flipToNormal   is boolean,
                               points         is array) returns array
{
    var result = [];
    for (var i = 0; i < size(points); i += 1)
    {
        var pt = points[i];

        // Project source point onto from-path; get Frenet frame there
        var s_from     = projectOntoFrenetPath(fromFrenetPath, pt, undefined).arcLength;
        var fromResult = getFrameAtArcLength(context, fromFrenetPath, s_from);

        // Express point in from-frame local coordinates [tangent, normal, binormal]
        var localCoords = worldPointToFrenet(pt, fromResult);

        // Linear arc-length mapping from from-path to to-path
        var s_to = toRefArc + (s_from - fromRefArc);

        // Get to-frame at mapped arc-length
        var toResult = getFrameAtArcLength(context, toFrenetPath, s_to);

        // Determine effective to-frame normal sign (apply flipToNormal toggle)
        var toSign = toResult.sign;
        if (flipToNormal)
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

        result = append(result, frenetPointToWorld(localCoords, toFrameResult));
    }
    return result;
}


// ============================================================================
// alignIsolatedLineFrames
// ============================================================================

/**
 * Post-process a FrenetPath: for any isolated line edge (no adjacent curve),
 * borrow the to-path's normal at the corresponding arc position so the frame
 * is aligned across the mapping.
 *
 * Lines adjacent to a curve already received a curve-context xAxis in
 * buildFrenetPath step 4.5; this handles only the isolated-line case.
 *
 * @param context        {Context}
 * @param fromFrenetPath {map} — result from buildFrenetPath (source)
 * @param toFrenetPath   {map} — result from buildFrenetPath (target)
 * @param fromRefArc     {ValueWithUnits}
 * @param toRefArc       {ValueWithUnits}
 * @returns {map} — updated fromFrenetPath with corrected lineFrames
 */
export function alignIsolatedLineFrames(context is Context,
    fromFrenetPath is map, toFrenetPath is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits) returns map
{
    var fromEdgeData = fromFrenetPath.edgeData;
    for (var i = 0; i < size(fromEdgeData); i += 1)
    {
        var ed = fromEdgeData[i];
        if (!ed.isLine) { continue; }

        var hasCurveCtx = (i > 0 && !fromEdgeData[i - 1].isLine) ||
                          (i + 1 < size(fromEdgeData) && !fromEdgeData[i + 1].isLine);
        if (hasCurveCtx) { continue; }

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
    return mergeMaps(fromFrenetPath, { "edgeData": fromEdgeData });
}


// ============================================================================
// debugDrawFrames
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
export function debugDrawFrames(context is Context, frenetPath is map, numSamples is number)
{
    var totalLength = frenetPath.totalLength;
    var arrowLen    = totalLength / max([1, numSamples - 1]) / 3;
    var arrowRadius = arrowLen * 0.05;

    for (var i = 0; i < numSamples; i += 1)
    {
        var s      = totalLength * i / max([1, numSamples - 1]);
        var result = getFrameAtArcLength(context, frenetPath, s);
        var origin = result.frame.origin;

        addDebugArrow(context, origin, origin + arrowLen * result.frame.xAxis,  arrowRadius,           DebugColor.RED);
        addDebugArrow(context, origin, origin + arrowLen * yAxis(result.frame),  arrowRadius * (2 / 3), DebugColor.GREEN);
        addDebugArrow(context, origin, origin + arrowLen * result.frame.zAxis,   arrowRadius * 0.5,     DebugColor.BLUE);
    }
}


// ============================================================================
// transformEdges
// ============================================================================

/**
 * Transforms an array of edges from one Frenet path to another.
 * Returns an array of maps { "sourceEdge": Query, "wrappedEdge": Query, "wrappedBody": Query }.
 *
 * @param context   {Context}
 * @param id        {Id}
 * @param edgeArray {array}  : array of edge Queries to transform
 * @param fromMap   {map}    : result from buildFrenetPath (source reference)
 * @param toMap     {map}    : result from buildFrenetPath (target reference)
 * @param settings  {map}    : {
 *   fromRefArc, toRefArc, flipToNormal,
 *   samplingMode (SamplingMode), samplingDensity (LENGTH_BASED), sourceCPMultiplier (CP_BASED),
 *   approximationDegree, approximationMaxCPs, approximationTolerance
 * }
 * @returns {array} : [{ "sourceEdge": Query, "wrappedEdge": Query, "wrappedBody": Query }, ...]
 */
export function transformEdges(context is Context, id is Id, edgeArray is array, fromMap is map, toMap is map, settings is map) returns array
{
    var result = [];
    for (var i = 0; i < size(edgeArray); i += 1)
    {
        var edge    = edgeArray[i];
        var edgeLen = evLength(context, { "entities": edge });
        var edgeBSpline;
        if (settings.samplingMode == SamplingMode.CP_BASED || settings.keepDegree)
        {
            edgeBSpline = evApproximateBSplineCurve(context, { "edge": edge });
        }

        var numSamples;
        if (settings.samplingMode == SamplingMode.CP_BASED)
        {
            numSamples = max([10, settings.sourceCPMultiplier * size(edgeBSpline.controlPoints)]);
        }
        else
        {
            numSamples = max([5, ceil(edgeLen / settings.samplingDensity) + 1]);
        }

        var srcPoints = mapArray(evEdgeTangentLines(context, {
            "edge"       : edge,
            "parameters" : range(0, 1, numSamples)
        }), function(x) { return x.origin; });

        var mappedPoints = mapWorldPoints(context, fromMap, toMap,
            settings.fromRefArc, settings.toRefArc, settings.flipToNormal, srcPoints);

        // Snap endpoints to pre-computed vertex-mapped positions so that all edges
        // sharing a source vertex produce BSplines with bit-identical endpoints.
        if (settings.vertexMap != undefined)
        {
            var edgeVerts = evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX));
            var nv = size(edgeVerts);
            if (nv == 2)
            {
                var vm0 = settings.vertexMap[toString(edgeVerts[0])];
                var vm1 = settings.vertexMap[toString(edgeVerts[1])];
                if (vm0 != undefined && vm1 != undefined)
                {
                    // Determine which pre-mapped vertex aligns with mappedPoints[0]
                    if (norm(mappedPoints[0] - vm0) <= norm(mappedPoints[0] - vm1))
                    {
                        mappedPoints[0]                    = vm0;
                        mappedPoints[size(mappedPoints) - 1] = vm1;
                    }
                    else
                    {
                        mappedPoints[0]                    = vm1;
                        mappedPoints[size(mappedPoints) - 1] = vm0;
                    }
                }
            }
            else if (nv == 1)
            {
                // Closed edge (full circle etc.) — same vertex at both ends
                var vm = settings.vertexMap[toString(edgeVerts[0])];
                if (vm != undefined)
                {
                    mappedPoints[0]                    = vm;
                    mappedPoints[size(mappedPoints) - 1] = vm;
                }
            }
            // nv == 0: degenerate edge, leave endpoints as-is
        }

        var approxDegree = (settings.keepDegree && edgeBSpline != undefined)
            ? max([settings.approximationDegree, edgeBSpline.degree])
            : settings.approximationDegree;

        var approxDef = {
            "targets"            : [approximationTarget({ "positions": mappedPoints })],
            "tolerance"          : settings.approximationTolerance,
            "maxControlPoints"   : settings.approximationMaxCPs,
            "degree"             : approxDegree,
            "isPeriodic"         : false,
            "interpolateIndices" : [0, size(mappedPoints) - 1]
        };
        var wrappedCurve = approximateSpline(context, approxDef)[0];

        var wrappedId = id + (toString(i) ~ "edge");
        try
        {
            opCreateBSplineCurve(context, wrappedId, { "bSplineCurve": wrappedCurve });
            result = append(result, {
                "sourceEdge" : edge,
                "wrappedEdge": qCreatedBy(wrappedId, EntityType.EDGE),
                "wrappedBody": qCreatedBy(wrappedId, EntityType.BODY)
            });
        }
        catch (e) { println("ERROR transformEdge " ~ i ~ ": " ~ toString(e)); }
    }
    return result;
}


// ============================================================================
// transformFacepoints
// ============================================================================

/**
 * Samples interior points from a face by creating isoparametric curves,
 * maps them through the Frenet transform, and returns the mapped positions
 * for use as guide vertices in opFillSurface.
 *
 * Interior-only sampling (parameters 0 and 1 excluded) avoids duplicating
 * points that are already captured by the transformed boundary edges.
 *
 * @param context      {Context}
 * @param id           {Id}
 * @param face         {Query}  : source face
 * @param uMultiplier  {number} : u iso curve count = uMultiplier * u control-point dimension
 * @param vMultiplier  {number} : v iso curve count = vMultiplier * v control-point dimension
 * @param fromMap      {map}    : result from buildFrenetPath (source reference)
 * @param toMap        {map}    : result from buildFrenetPath (target reference)
 * @param settings     {map}    : { fromRefArc, toRefArc, flipToNormal, samplingDensity, ... }
 * @returns {array} : array of mapped Vector positions (interior guide points)
 */
export function transformFacepoints(context is Context, id is Id, face is Query, uMultiplier is number, vMultiplier is number, fromMap is map, toMap is map, settings is map) returns array
{
    // 1. Get BSpline surface dimensions from the face approximation
    var surfData = evApproximateBSplineSurface(context, { "face": face });
    var bspl     = surfData.bSplineSurface;
    var uDim     = size(bspl.controlPoints);
    var vDim     = size(bspl.controlPoints[0]);
    var nU       = uMultiplier * uDim;
    var nV       = vMultiplier * vDim;

    // 2. Create isoparametric curves on the face
    var isoId  = id + "isoCurves";
    var uNames = [];
    var vNames = [];
    for (var k = 0; k < nU; k += 1) { uNames = append(uNames, "u" ~ k); }
    for (var k = 0; k < nV; k += 1) { vNames = append(vNames, "v" ~ k); }

    opCreateCurvesOnFace(context, isoId, {
        "curveDefinition" : [
            { "face": face, "creationType": FaceCurveCreationType.DIR1_AUTO_SPACED_ISO, "nCurves": nU, "names": uNames },
            { "face": face, "creationType": FaceCurveCreationType.DIR2_AUTO_SPACED_ISO, "nCurves": nV, "names": vNames }
        ]
    });
    var isoBodies = qCreatedBy(isoId, EntityType.BODY);

    // 3. Sample each iso curve at interior parameters, skip boundary endpoints
    var interiorPoints = [];
    var isoEdges = evaluateQuery(context, qCreatedBy(isoId, EntityType.EDGE));
    for (var e in isoEdges)
    {
        var elen  = evLength(context, { "entities": e });
        var nSamp = max([3, ceil(elen / settings.samplingDensity) + 1]);

        // Parameters strictly between 0 and 1 (endpoints lie on boundary edges)
        var params = [];
        for (var k = 1; k < nSamp - 1; k += 1)
        {
            params = append(params, k / (nSamp - 1));
        }
        if (size(params) == 0) { continue; }

        var tangentLines = evEdgeTangentLines(context, { "edge": e, "parameters": params });
        for (var tl in tangentLines)
        {
            interiorPoints = append(interiorPoints, tl.origin);
        }
    }

    // 4. Delete iso curve bodies — they were only needed for sampling
    opDeleteBodies(context, id + "deleteIso", { "entities": isoBodies });

    // 5. Map all collected interior points through the Frenet transform
    if (size(interiorPoints) == 0) { return []; }
    return mapWorldPoints(context, fromMap, toMap,
        settings.fromRefArc, settings.toRefArc, settings.flipToNormal, interiorPoints);
}
