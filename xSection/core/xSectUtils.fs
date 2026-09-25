FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * XSECT UTILS - Cross Section Utility Functions
 * ==============================================
 *
 * Reusable helper functions for cross-section analysis:
 * - Body signature extraction (for caching)
 * - Cross-section frame generation
 * - B-spline sampling utilities
 * - Query helpers
 * - Geometry utilities (polyline projection)
 */

// =============================================================================
// GEOMETRIC TOLERANCE CONSTANTS
// =============================================================================

export const GEOM_TOL = 1e-6 * meter;                    // General geometric tolerance
export const POINT_DEDUP_TOL = 1e-6 * meter;             // Point deduplication threshold
export const PLANE_TOL = 1e-8 * meter;                   // Plane classification tolerance
export const CONTROL_POLY_AREA_TOL = 1e-12 * meter * meter;  // Colinearity check
export const MIN_CURVE_LENGTH = 0.5 * millimeter;        // Minimum curve length for deduplication
export const OVERLAP_TOL = 1e-6 * meter;                 // Curve overlap detection

// =============================================================================
// BODY SIGNATURE FUNCTIONS
// =============================================================================

/**
 * Extract a signature for a body that can be used to detect geometry changes.
 * 
 * Captures:
 * - Tight bounding box (detects move, scale, major reshape)
 * - Face count (detects topology changes)
 * 
 * @param context {Context}
 * @param body {Query} : Single body query
 * @returns {map} : { box3D: Box3d, numFaces: number }
 */
export function getBodySignature(context is Context, body is Query) returns map
{
    var box3D = evBox3d(context, {
            "topology" : body,
            "tight" : true
    });
    
    var numFaces = size(evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)));
    
    return {
        "box3D" : box3D,
        "numFaces" : numFaces
    };
}

/**
 * Compare two body signatures to determine if geometry has changed.
 * 
 * @param sig1 {map} : First signature from getBodySignature
 * @param sig2 {map} : Second signature from getBodySignature
 * @param tolerance {ValueWithUnits} : Position tolerance for bounding box comparison
 * @returns {boolean} : true if signatures match (no change), false if different
 */
export function signaturesMatch(sig1 is map, sig2 is map, tolerance is ValueWithUnits) returns boolean
{
    // Check face count
    if (sig1.numFaces != sig2.numFaces)
        return false;
    
    // Check bounding box corners
    if (norm(sig1.box3D.minCorner - sig2.box3D.minCorner) >= tolerance)
        return false;
    
    if (norm(sig1.box3D.maxCorner - sig2.box3D.maxCorner) >= tolerance)
        return false;
    
    return true;
}

// =============================================================================
// CROSS-SECTION FRAME GENERATION
// =============================================================================

/**
 * Reduce the "cross section along" selection to an edges Query.
 *
 * The selection is a single wire body; its constituent edges are extracted
 * via qOwnedByBody and stitched into a single path by constructPath.
 *
 * @param alongQuery {Query} : The user's "cross section along" wire selection
 * @returns {Query} : Query resolving to the wire's constituent edges
 */
export function edgesFromAlongQuery(alongQuery is Query) returns Query
{
    return qOwnedByBody(alongQuery, EntityType.EDGE);
}

/**
 * Build a single Path from the "cross section along" selection.
 *
 * Extracts the wire body's edges and stitches them into one connected path.
 * All downstream sampling uses whole-path arc-length parameters [0,1] via
 * the native Path eval API.
 *
 * @param context {Context}
 * @param alongQuery {Query} : The user's "cross section along" selection
 * @returns {Path} : Connected path spanning the selected geometry
 */
export function buildCrossSectionPath(context is Context, alongQuery is Query) returns Path
{
    return constructPath(context, edgesFromAlongQuery(alongQuery));
}

/**
 * Orient an array of path tangent lines into cross-section coordinate frames.
 *
 * Each frame is oriented with:
 *   zAxis (tangent)  -> positive world X (tip to tail)
 *   xAxis (normal)   -> positive world Z (thickness, up from base)
 *   yAxis (binormal) -> world -Y (width, derived)
 *
 * Assumption: the cross-section path lies in or parallel to the XZ plane.
 * Only the tangent line's origin and direction are used; curvature is not
 * needed because the normal is rederived from the world thickness axis.
 *
 * @param tangentLines {array} : Line objects ({ origin, direction }) from evPathTangentLines
 * @returns {array} : Array of oriented CoordSystem frames
 */
function orientPathFrames(tangentLines is array) returns array
{
    var worldThickness = vector(0, 0, 1);
    var frames = [];

    for (var i = 0; i < size(tangentLines); i += 1)
    {
        // Force tangent toward positive X
        var z = tangentLines[i].direction;
        if (z[0] < 0)
        {
            z *= -1;
        }

        // Project world +Z onto plane perpendicular to tangent
        var rawX = worldThickness - dot(worldThickness, z) * z;
        var xDir = normalize(rawX);

        frames = append(frames, coordSystem(tangentLines[i].origin, xDir, z));
    }

    return frames;
}

/**
 * Generate coordinate frames at evenly spaced locations along a path.
 *
 * Frames are oriented with:
 * - Origin at the curve point
 * - Z-axis along curve tangent (forced positive X direction)
 * - X and Y axes in the normal plane
 *
 * @param context {Context}
 * @param path {Path} : Path to generate frames along (see buildCrossSectionPath)
 * @param numSections {number} : Number of frames (includes endpoints)
 * @returns {array} : Array of CoordSystem frames
 */
export function getCrossSectionFrames(context is Context, path is Path, numSections is number) returns array
{
    var paramRange = range(0, 1, numSections);

    var result = evPathTangentLines(context, path, paramRange);

    return orientPathFrames(result.tangentLines);
}

/**
 * Project a world X coordinate onto a whole-path parameter using binary search.
 *
 * Handles curved paths gracefully by finding the parameter where the path's
 * world X coordinate matches the target X value. Assumes world X is monotonic
 * along the path (true for a tip-to-tail ski baseline).
 *
 * @param context {Context}
 * @param path {Path} : Path to project onto (see buildCrossSectionPath)
 * @param targetX {ValueWithUnits} : Target world X coordinate
 * @returns {number} : Parameter [0,1] where path world X ~= targetX, or undefined if out of range
 */
export function projectXToPathParameter(context is Context, path is Path, targetX is ValueWithUnits) returns number
{
    const MAX_ITERATIONS = 20;
    const TOLERANCE = 1e-6 * meter;

    var paramMin = 0.0;
    var paramMax = 1.0;

    // Get X coordinates at bounds
    var bounds = evPathTangentLines(context, path, [paramMin, paramMax]);
    var xMin = bounds.tangentLines[0].origin[0];
    var xMax = bounds.tangentLines[1].origin[0];

    // Check if targetX is outside bounds
    if (targetX < min(xMin, xMax) - TOLERANCE || targetX > max(xMin, xMax) + TOLERANCE)
    {
        return undefined;
    }

    // Binary search for parameter
    for (var iter = 0; iter < MAX_ITERATIONS; iter += 1)
    {
        var paramMid = (paramMin + paramMax) / 2.0;

        var mid = evPathTangentLines(context, path, [paramMid]);
        var xMid = mid.tangentLines[0].origin[0];

        // Check convergence
        if (abs(xMid - targetX) < TOLERANCE)
        {
            return paramMid;
        }

        // Update search bounds
        if ((xMid < targetX && xMax > xMin) || (xMid > targetX && xMax < xMin))
        {
            paramMin = paramMid;
        }
        else
        {
            paramMax = paramMid;
        }
    }

    // Return best estimate after max iterations
    return (paramMin + paramMax) / 2.0;
}

/**
 * Generate cross-section frames with FCP/ACP-aware spacing.
 *
 * If both FCP and ACP are defined, cross-section planes are intelligently distributed:
 * - Reference region (FCP to ACP): Evenly spaced with planes at FCP and ACP
 * - Tip region (start to FCP): ≥2 sections, spacing close to reference spacing
 * - Tail region (ACP to end): ≥2 sections, spacing close to reference spacing
 *
 * Fallback: If FCP or ACP undefined → uniform spacing (current behavior)
 *
 * @param context {Context}
 * @param alongQuery {Query} : Wire body to cross-section along (its edges are extracted)
 * @param numSections {number} : Total number of frames requested
 * @param fcpX : FCP world X coordinate (or undefined)
 * @param acpX : ACP world X coordinate (or undefined)
 * @returns {array} : Array of maps [{ "frame": CoordSystem, "stationNumber": number }, ...]
 */
export function getCrossSectionFramesAdaptive(context is Context, alongQuery is Query,
                                               numSections is number,
                                               fcpX, acpX) returns array
{
    // Build a single path from the selection (single edge, multiple edges, or wire body).
    var path = buildCrossSectionPath(context, alongQuery);

    // Fallback to uniform spacing if either FCP or ACP undefined
    if (fcpX == undefined || acpX == undefined)
    {
        var uniformFrames = getCrossSectionFrames(context, path, numSections);
        var result = [];
        for (var i = 0; i < size(uniformFrames); i += 1)
        {
            result = append(result, {
                "frame" : uniformFrames[i],
                "stationNumber" : i
            });
        }
        return result;
    }

    // Get path world X bounds
    var bounds = evPathTangentLines(context, path, [0.0, 1.0]);

    var xStart = bounds.tangentLines[0].origin[0];
    var xEnd = bounds.tangentLines[1].origin[0];

    var xMin = min(xStart, xEnd);
    var xMax = max(xStart, xEnd);

    // Validate FCP/ACP within bounds
    const BOUND_TOL = 1e-5 * meter;
    if (fcpX < xMin - BOUND_TOL || fcpX > xMax + BOUND_TOL ||
        acpX < xMin - BOUND_TOL || acpX > xMax + BOUND_TOL)
    {
        var uniformFrames = getCrossSectionFrames(context, path, numSections);
        var result = [];
        for (var i = 0; i < size(uniformFrames); i += 1)
        {
            result = append(result, {
                "frame" : uniformFrames[i],
                "stationNumber" : i
            });
        }
        return result;
    }

    // Ensure FCP < ACP for consistent logic
    var fcpXOrdered = min(fcpX, acpX);
    var acpXOrdered = max(fcpX, acpX);

    // Check for degenerate span
    if (abs(acpXOrdered - fcpXOrdered) < 1e-6 * meter)
    {
        var uniformFrames = getCrossSectionFrames(context, path, numSections);
        var result = [];
        for (var i = 0; i < size(uniformFrames); i += 1)
        {
            result = append(result, {
                "frame" : uniformFrames[i],
                "stationNumber" : i
            });
        }
        return result;
    }

    // Compute region lengths (world-X based, independent of edge parameter direction).
    // Tip = portion of the edge below the reference band (low world X);
    // Tail = portion above it (high world X). Because fcpXOrdered/acpXOrdered are the
    // ordered (min/max) boundaries and xMin/xMax are the ordered edge extents, both
    // lengths are always non-negative -- regardless of FCP/ACP selection order or the
    // edge's parameter direction.
    var tipLength = fcpXOrdered - xMin;
    var refLength = acpXOrdered - fcpXOrdered;
    var tailLength = xMax - acpXOrdered;

    // Allocate ALL requested sections to reference region (FCP to ACP)
    var numRefSections = numSections;  // User's requested count goes entirely to reference

    // Compute reference spacing (target for tip/tail sampling)
    var refSpacing = refLength / (numRefSections - 1);

    // Allocate ADDITIONAL bonus sections to tip and tail regions (beyond numSections),
    // targeting refSpacing but capped to avoid excess.
    var numTipSections = 0;
    if (tipLength > refSpacing * 0.5)  // Only add tip if region is significant
    {
        numTipSections = min(ceil(tipLength / refSpacing), 4);  // Cap at 4 to avoid excess
    }
    else if (tipLength > 1e-6 * meter)
    {
        numTipSections = 1;  // Minimal region gets 1 section
    }

    var numTailSections = 0;
    if (tailLength > refSpacing * 0.5)  // Only add tail if region is significant
    {
        numTailSections = min(ceil(tailLength / refSpacing), 4);  // Cap at 4 to avoid excess
    }
    else if (tailLength > 1e-6 * meter)
    {
        numTailSections = 1;  // Minimal region gets 1 section
    }

    // NOTE: tip/tail are bonus sections beyond numSections.
    // Total sections = numTipSections + numRefSections + numTailSections
    //                = (0-4) + numSections + (0-4)

    // Build (worldX, stationNumber) samples in strictly ASCENDING world-X order.
    // Ordering by world X (rather than by edge parameter direction) guarantees a
    // monotonic point list for the downstream visualization spline fit, regardless of
    // how the edge is parameterized or whether FCP/ACP were selected in reverse order.
    // This is the fix for the "no output wires / folded tip-tail" behavior seen when the
    // edge runs high-X -> low-X or when FCP/ACP are reversed.
    var xPositions = [];
    var stationNumbers = [];

    // Tip region: [xMin, fcpXOrdered) -- includes the low edge endpoint, excludes the band.
    // Stations are negative (below reference station 0).
    // The outermost tip/tail stations are inset from the ends: a plane exactly at the end only grazes
    // the geometry and gives a zero-area section (tools review 2026-09-25).
    for (var i = 0; i < numTipSections; i += 1)
    {
        var t = i / numTipSections;  // t in [0, 1): includes xMin, excludes the band boundary
        var tipX = xMin + t * (fcpXOrdered - xMin);
        if (i == 0)
        {
            tipX = xMin + max(0.5 * millimeter, 0.02 * (fcpXOrdered - xMin) / numTipSections);
        }
        xPositions = append(xPositions, tipX);
        stationNumbers = append(stationNumbers, -numTipSections + i);
    }

    // Reference region: [fcpXOrdered, acpXOrdered] inclusive. Stations 0 .. N-1.
    for (var i = 0; i < numRefSections; i += 1)
    {
        var t = i / (numRefSections - 1);
        xPositions = append(xPositions, fcpXOrdered + t * (acpXOrdered - fcpXOrdered));
        stationNumbers = append(stationNumbers, i);
    }

    // Tail region: (acpXOrdered, xMax] -- excludes the band, includes the high edge endpoint.
    // Stations continue above the reference (>= N).
    for (var i = 0; i < numTailSections; i += 1)
    {
        var t = (i + 1) / numTailSections;  // t in (0, 1]: excludes the band boundary, includes xMax
        var tailX = acpXOrdered + t * (xMax - acpXOrdered);
        if (i == numTailSections - 1)
        {
            tailX = xMax - max(0.5 * millimeter, 0.02 * (xMax - acpXOrdered) / numTailSections);
        }
        xPositions = append(xPositions, tailX);
        stationNumbers = append(stationNumbers, numRefSections + i);
    }

    // Convert world-X samples to whole-path parameters, keeping station labels aligned.
    // projectXToPathParameter returns undefined only for out-of-range X; pairing each
    // station number in the same step prevents index drift if any sample is skipped.
    var parameters = [];
    var keptStationNumbers = [];
    for (var i = 0; i < size(xPositions); i += 1)
    {
        var param = projectXToPathParameter(context, path, xPositions[i]);
        if (param != undefined)
        {
            parameters = append(parameters, param);
            keptStationNumbers = append(keptStationNumbers, stationNumbers[i]);
        }
    }

    // Generate frames at computed parameters (same orientation as getCrossSectionFrames)
    var tangents = evPathTangentLines(context, path, parameters);
    var frames = orientPathFrames(tangents.tangentLines);

    // Pair frames with station numbers
    var result = [];
    for (var i = 0; i < size(frames); i += 1)
    {
        result = append(result, {
            "frame" : frames[i],
            "stationNumber" : keptStationNumbers[i]
        });
    }
    return result;
}

/**
 * Create a plane from a cross-section frame.
 *
 * @param frame {CoordSystem} : Frame from getCrossSectionFrames
 * @returns {Plane} : Plane with origin at frame origin, normal along frame Z-axis
 */
export function frameToPlane(frame) returns Plane
{
    return plane(frame.origin, frame.zAxis);
}

// =============================================================================
// B-SPLINE SAMPLING UTILITIES
// =============================================================================

/**
 * Get the parameter range for a B-spline curve.
 * 
 * @param curve {BSplineCurve}
 * @returns {map} : { uMin: number, uMax: number }
 */
export function getBSplineParamRange(curve is BSplineCurve) returns map
{
    var knots = curve.knots;
    return {
        "uMin" : knots[0],
        "uMax" : knots[size(knots) - 1]
    };
}

/**
 * Generate evenly spaced parameters for sampling a B-spline.
 * 
 * @param curve {BSplineCurve}
 * @param numPoints {number} : Number of sample points
 * @returns {array} : Array of parameter values
 */
export function getEvenParams(curve is BSplineCurve, numPoints is number) returns array
{
    var range = getBSplineParamRange(curve);
    var params = [];
    
    for (var i = 0; i < numPoints; i += 1)
    {
        var t = (numPoints == 1) ? 0 : i / (numPoints - 1);
        params = append(params, range.uMin + t * (range.uMax - range.uMin));
    }
    
    return params;
}

/**
 * Determine sample parameters for a B-spline based on definition settings.
 * 
 * Default: sample at control point count (minimal representation)
 * With alterMeshing: sample based on edge length and mesh parameters
 * 
 * @param context {Context}
 * @param curve {BSplineCurve}
 * @param curveLength {ValueWithUnits} : Pre-computed curve length (or undefined to estimate)
 * @param definition {map} : Feature definition with meshing parameters
 * @returns {array} : Array of parameter values for sampling
 */
export function getSampleParams(context is Context, curve is BSplineCurve, curveLength, definition is map) returns array
{
    var numCPs = size(curve.controlPoints);
    
    if (!definition.alterMeshing)
    {
        // Default: control points only
        return getEvenParams(curve, numCPs);
    }
    
    // Estimate length from control polygon if not provided
    var edgeLen = curveLength;
    if (edgeLen == undefined)
    {
        edgeLen = approximateControlPolygonLength(curve);
    }
    
    var numPoints = numCPs;
    
    if (definition.insertInternalMeshPoints)
    {
        // Target equilateral triangle edge length for given area
        var targetLength = sqrt(4 * definition.meshElementArea / 1.73) * millimeter;
        numPoints = max(ceil(edgeLen / targetLength), numCPs);
    }
    else
    {
        numPoints = max(ceil(edgeLen / definition.perimMaxSegLength), numCPs);
    }
    
    return getEvenParams(curve, numPoints);
}


/**
 * Sample a B-spline curve at given parameters.
 * 
 * @param curve {BSplineCurve}
 * @param params {array} : Array of parameter values
 * @returns {array} : Array of 3D points (Vectors)
 */
export function sampleBSplineAtParams(curve is BSplineCurve, params is array) returns array
{
    if (size(params) == 0)
        return [];
    
    var result = evaluateSpline({
            "spline" : curve,
            "parameters" : params
    });
    
    // evaluateSpline returns nested array, extract points
    return result[0];
}

/**
 * Sample a B-spline at its control point parameter locations.
 * 
 * @param curve {BSplineCurve}
 * @returns {array} : Array of 3D points
 */
export function sampleBSplineAtControlPoints(curve is BSplineCurve) returns array
{
    var params = getEvenParams(curve, size(curve.controlPoints));
    return sampleBSplineAtParams(curve, params);
}

// =============================================================================
// QUERY HELPERS
// =============================================================================

/**
 * Check if a body exists in an array of body queries.
 * 
 * @param context {Context}
 * @param body {Query} : Body to search for
 * @param bodyArray {array} : Array of Query objects
 * @returns {boolean} : true if body is in array
 */
export function bodyInArray(context is Context, body is Query, bodyArray is array) returns boolean
{
    return any(bodyArray, function(q) { return areQueriesEquivalent(context, body, q); });
}

/**
 * Find the index of a body in an array.
 * 
 * @param context {Context}
 * @param body {Query} : Body to find
 * @param bodyQueries {array} : Array of Query objects
 * @returns {number} : Index of body, or -1 if not found
 */
export function findBodyIndex(context is Context, body is Query, bodyQueries is array) returns number
{
    for (var i = 0; i < size(bodyQueries); i += 1)
    {
        if (areQueriesEquivalent(context, body, bodyQueries[i]))
            return i;
    }
    return -1;
}

/**
 * Generate an array of integers from start to end (inclusive).
 * 
 * @param start {number} : Starting value
 * @param end {number} : Ending value (inclusive)
 * @returns {array} : Array of integers [start, start+1, ..., end]
 */
export function range(start is number, end is number) returns array
{
    var result = [];
    for (var i = start; i <= end; i += 1)
    {
        result = append(result, i);
    }
    return result;
}

// =============================================================================
// ARRAY UTILITIES
// =============================================================================

/**
 * Remove element at index from array.
 * 
 * @param arr {array} : Source array
 * @param index {number} : Index to remove
 * @returns {array} : New array without element at index
 */
export function removeArrayIndex(arr is array, index is number) returns array
{
    var result = [];
    for (var i = 0; i < size(arr); i += 1)
    {
        if (i != index)
        {
            result = append(result, arr[i]);
        }
    }
    return result;
}

/**
 * Approximate arc length of a B-spline by summing control polygon segment lengths.
 *
 * The control polygon length is an upper bound on the actual arc length and
 * converges to it for well-distributed control points. Useful for mesh sizing
 * estimates when an exact arc-length computation is not required.
 *
 * @param curve {BSplineCurve}
 * @returns {ValueWithUnits} : Sum of consecutive control-point distances (same units as control points)
 */
export function approximateControlPolygonLength(curve is BSplineCurve) returns ValueWithUnits
{
    var cps = curve.controlPoints;
    var length = 0 * meter;
    for (var i = 0; i < size(cps) - 1; i += 1)
    {
        length += norm(cps[i + 1] - cps[i]);
    }
    return length;
}

/**
 * Draw B-spline control polygon as debug lines. Debug-only; has no effect in production builds.
 *
 * @param context {Context}
 * @param curve {BSplineCurve} : Curve whose control polygon to visualize
 * @param color {DebugColor} : Debug line color
 */
export function debugControlPolygon(context is Context, curve is BSplineCurve, color is DebugColor)
{
    var cps = curve.controlPoints;
    for (var i = 1; i < size(cps); i += 1)
    {
        addDebugLine(context, cps[i-1], cps[i], color);
    }
}

// =============================================================================
// POLYLINE PROJECTION UTILITIES
// =============================================================================

/**
 * Find closest point on a polyline (control polygon) to a given point.
 *
 * @param pt {Vector} : Query point
 * @param polyline {array} : Array of Vectors defining polyline vertices
 * @returns {map} : { distance, segmentIdx, t, closestPt }
 *     - distance: closest distance to polyline
 *     - segmentIdx: index of closest segment (0 to n-2)
 *     - t: parameter along that segment [0,1]
 *     - closestPt: closest point on polyline
 */
export function closestPointOnPolyline(pt is Vector, polyline is array) returns map
{
    var bestDist = undefined;
    var bestSeg = 0;
    var bestT = 0;
    var bestPt = polyline[0];

    for (var i = 0; i < size(polyline) - 1; i += 1)
    {
        var segStart = polyline[i];
        var segEnd = polyline[i + 1];
        var result = closestPointOnSegment(pt, segStart, segEnd);

        if (bestDist == undefined || result.distance < bestDist)
        {
            bestDist = result.distance;
            bestSeg = i;
            bestT = result.t;
            bestPt = result.closestPt;
        }
    }

    return {
        "distance" : bestDist,
        "segmentIdx" : bestSeg,
        "t" : bestT,
        "closestPt" : bestPt
    };
}

/**
 * Find closest point on a line segment to a given point.
 *
 * @param pt {Vector} : Query point
 * @param segStart {Vector} : Segment start
 * @param segEnd {Vector} : Segment end
 * @returns {map} : { distance, t, closestPt }
 *     - distance: distance from pt to closest point
 *     - t: parameter along segment [0,1]
 *     - closestPt: closest point on segment
 */
export function closestPointOnSegment(pt is Vector, segStart is Vector, segEnd is Vector) returns map
{
    var segVec = segEnd - segStart;
    var segLenSq = dot(segVec, segVec);

    if (segLenSq < 1e-20 * meter * meter)
    {
        // Degenerate segment
        return {
            "distance" : norm(pt - segStart),
            "t" : 0,
            "closestPt" : segStart
        };
    }

    // Project pt onto line, clamp to segment
    var t = dot(pt - segStart, segVec) / segLenSq;
    t = max(0, min(1, t));  // clamp to [0,1]

    var closestPt = segStart + t * segVec;
    var distance = norm(pt - closestPt);

    return {
        "distance" : distance,
        "t" : t,
        "closestPt" : closestPt
    };
}
