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
 * Generate coordinate frames at evenly spaced locations along an edge.
 * 
 * Frames are oriented with:
 * - Origin at the curve point
 * - Z-axis along curve tangent (forced positive X direction)
 * - X and Y axes in the normal plane
 * 
 * @param context {Context}
 * @param edge {Query} : Edge to generate frames along
 * @param numSections {number} : Number of frames (includes endpoints)
 * @returns {array} : Array of CoordSystem frames
 */
export function getCrossSectionFrames(context is Context, edge is Query, numSections is number) returns array
{
    var paramRange = range(0, 1, numSections);
    
    var curvatures = evEdgeCurvatures(context, {
            "edge" : edge,
            "parameters" : paramRange
    });
    
    var frames = mapArray(curvatures, function(c) { return c.frame; });
    
    // Force consistent frame orientation:
    //   zAxis (tangent)  → positive world X (tip to tail)
    //   xAxis (normal)   → positive world Z (thickness, up from base)
    //   yAxis (binormal) → world -Y (width, derived)
    //
    // Assumption: cross-section edge lies in or parallel to the XZ plane.
    var worldThickness = vector(0, 0, 1);
    
    for (var i = 0; i < size(frames); i += 1)
    {
        // Force tangent toward positive X
        var z = frames[i].zAxis;
        if (z[0] < 0)
        {
            z *= -1;
        }
        
        // Project world +Z onto plane perpendicular to tangent
        var rawX = worldThickness - dot(worldThickness, z) * z;
        var xDir = normalize(rawX);
        
        frames[i] = coordSystem(frames[i].origin, xDir, z);
    }
    
    return frames;
}

/**
 * Project a world X coordinate onto an edge parameter using binary search.
 *
 * Handles curved edges gracefully by finding the parameter where the edge's
 * world X coordinate matches the target X value.
 *
 * @param context {Context}
 * @param edge {Query} : Edge to project onto
 * @param targetX {ValueWithUnits} : Target world X coordinate
 * @returns {number} : Parameter [0,1] where edge world X ≈ targetX, or undefined if not found
 */
export function projectXToEdgeParameter(context is Context, edge is Query, targetX is ValueWithUnits) returns number
{
    const MAX_ITERATIONS = 20;
    const TOLERANCE = 1e-6 * meter;

    var paramMin = 0.0;
    var paramMax = 1.0;

    // Get X coordinates at bounds
    var curvStart = evEdgeCurvatures(context, {
        "edge" : edge,
        "parameters" : [paramMin]
    });
    var curvEnd = evEdgeCurvatures(context, {
        "edge" : edge,
        "parameters" : [paramMax]
    });

    var xMin = curvStart[0].frame.origin[0];
    var xMax = curvEnd[0].frame.origin[0];

    // Check if targetX is outside bounds
    if (targetX < min(xMin, xMax) - TOLERANCE || targetX > max(xMin, xMax) + TOLERANCE)
    {
        return undefined;
    }

    // Binary search for parameter
    for (var iter = 0; iter < MAX_ITERATIONS; iter += 1)
    {
        var paramMid = (paramMin + paramMax) / 2.0;

        var curvMid = evEdgeCurvatures(context, {
            "edge" : edge,
            "parameters" : [paramMid]
        });
        var xMid = curvMid[0].frame.origin[0];

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
 * @param edge {Query} : Edge to generate frames along
 * @param numSections {number} : Total number of frames requested
 * @param fcpX : FCP world X coordinate (or undefined)
 * @param acpX : ACP world X coordinate (or undefined)
 * @returns {array} : Array of maps [{ "frame": CoordSystem, "stationNumber": number }, ...]
 */
export function getCrossSectionFramesAdaptive(context is Context, edge is Query,
                                               numSections is number,
                                               fcpX, acpX) returns array
{
    // Fallback to uniform spacing if either FCP or ACP undefined
    if (fcpX == undefined || acpX == undefined)
    {
        var uniformFrames = getCrossSectionFrames(context, edge, numSections);
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

    // Get edge world X bounds
    var curvStart = evEdgeCurvatures(context, {
        "edge" : edge,
        "parameters" : [0.0]
    });
    var curvEnd = evEdgeCurvatures(context, {
        "edge" : edge,
        "parameters" : [1.0]
    });

    var xStart = curvStart[0].frame.origin[0];
    var xEnd = curvEnd[0].frame.origin[0];

    var xMin = min(xStart, xEnd);
    var xMax = max(xStart, xEnd);

    // Validate FCP/ACP within bounds
    const BOUND_TOL = 1e-5 * meter;
    if (fcpX < xMin - BOUND_TOL || fcpX > xMax + BOUND_TOL ||
        acpX < xMin - BOUND_TOL || acpX > xMax + BOUND_TOL)
    {
        var uniformFrames = getCrossSectionFrames(context, edge, numSections);
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
        var uniformFrames = getCrossSectionFrames(context, edge, numSections);
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

    // Compute region lengths
    var tipLength = abs(fcpXOrdered - xStart);
    var refLength = abs(acpXOrdered - fcpXOrdered);
    var tailLength = abs(xEnd - acpXOrdered);

    // Allocate ALL requested sections to reference region (FCP to ACP)
    var numRefSections = numSections;  // User's requested count goes entirely to reference

    // Compute reference spacing (for use in tip/tail calculations)
    var refSpacing = refLength / (numRefSections - 1);

    // Allocate ADDITIONAL sections to tip and tail regions (beyond requested numSections)
    // Use reference spacing as target, but cap to avoid excessive sections
    var numTipSections = 0;
    if (tipLength > refSpacing * 0.5)  // Only add tip if region is significant
    {
        // Add 2-4 sections depending on region length
        var idealTipCount = ceil(tipLength / refSpacing);
        numTipSections = min(idealTipCount, 4);  // Cap at 4 to avoid excess
    }
    else if (tipLength > 1e-6 * meter)
    {
        numTipSections = 1;  // Minimal region gets 1 section
    }

    var numTailSections = 0;
    if (tailLength > refSpacing * 0.5)  // Only add tail if region is significant
    {
        var idealTailCount = ceil(tailLength / refSpacing);
        numTailSections = min(idealTailCount, 4);  // Cap at 4 to avoid excess
    }
    else if (tailLength > 1e-6 * meter)
    {
        numTailSections = 1;  // Minimal region gets 1 section
    }

    // NOTE: No adjustment needed - tip/tail are bonus sections beyond numSections
    // Total sections = numTipSections + numRefSections + numTailSections
    //                = (0-4) + numSections + (0-4)

    // Determine spatial ordering (does edge go left-to-right or right-to-left?)
    var tipIsLeft = (xStart < xEnd);  // True if edge goes left-to-right

    // Assign station numbers
    var stationNumbers = [];

    // Tip stations
    if (numTipSections > 0)
    {
        if (tipIsLeft)
        {
            // Tip is LEFT of reference → negative stations
            for (var i = 0; i < numTipSections; i += 1)
            {
                stationNumbers = append(stationNumbers, -numTipSections + i);
            }
        }
        else
        {
            // Tip is RIGHT of reference → positive stations >= N
            for (var i = 0; i < numTipSections; i += 1)
            {
                stationNumbers = append(stationNumbers, numRefSections + i);
            }
        }
    }

    // Reference stations (always 0 to N-1)
    for (var i = 0; i < numRefSections; i += 1)
    {
        stationNumbers = append(stationNumbers, i);
    }

    // Tail stations
    if (numTailSections > 0)
    {
        if (tipIsLeft)
        {
            // Tail is RIGHT of reference → positive stations >= N
            for (var i = 0; i < numTailSections; i += 1)
            {
                stationNumbers = append(stationNumbers, numRefSections + i);
            }
        }
        else
        {
            // Tail is LEFT of reference → negative stations
            for (var i = 0; i < numTailSections; i += 1)
            {
                stationNumbers = append(stationNumbers, -numTailSections + i);
            }
        }
    }

    // Generate X positions for all three regions
    var xPositions = [];

    // Tip region (EXCLUDE fcpXOrdered boundary to avoid duplication)
    if (numTipSections > 0)
    {
        for (var i = 0; i < numTipSections; i += 1)
        {
            // Use i/numTipSections to include xStart (t=0) but exclude FCP (t<1)
            // This ensures the edge endpoint is included while avoiding duplication with reference region
            var t = i / numTipSections;
            var x = xStart + t * (fcpXOrdered - xStart);
            xPositions = append(xPositions, x);
        }
    }

    // Reference region (includes FCP and ACP)
    for (var i = 0; i < numRefSections; i += 1)
    {
        var t = i / (numRefSections - 1);
        var x = fcpXOrdered + t * (acpXOrdered - fcpXOrdered);  // Use signed offset for consistency
        xPositions = append(xPositions, x);
    }

    // Tail region (EXCLUDE acpXOrdered boundary to avoid duplication)
    if (numTailSections > 0)
    {
        for (var i = 0; i < numTailSections; i += 1)
        {
            // Use (i+1)/numTailSections to exclude ACP (t>0) but include xEnd (t=1)
            // This ensures the edge endpoint is included while avoiding duplication with reference region
            var t = (i + 1) / numTailSections;
            var x = acpXOrdered + t * (xEnd - acpXOrdered);
            xPositions = append(xPositions, x);
        }
    }

    // Convert X positions to parameters
    var parameters = [];
    for (var x in xPositions)
    {
        var param = projectXToEdgeParameter(context, edge, x);
        if (param != undefined)
        {
            parameters = append(parameters, param);
        }
    }

    // Generate frames at computed parameters
    var curvatures = evEdgeCurvatures(context, {
        "edge" : edge,
        "parameters" : parameters
    });

    var frames = mapArray(curvatures, function(c) { return c.frame; });

    // Force consistent frame orientation (same as getCrossSectionFrames)
    var worldThickness = vector(0, 0, 1);

    for (var i = 0; i < size(frames); i += 1)
    {
        // Force tangent toward positive X
        var z = frames[i].zAxis;
        if (z[0] < 0)
        {
            z *= -1;
        }

        // Project world +Z onto plane perpendicular to tangent
        var rawX = worldThickness - dot(worldThickness, z) * z;
        var xDir = normalize(rawX);

        frames[i] = coordSystem(frames[i].origin, xDir, z);
    }

    // Pair frames with station numbers
    var result = [];
    for (var i = 0; i < size(frames); i += 1)
    {
        result = append(result, {
            "frame" : frames[i],
            "stationNumber" : stationNumbers[i]
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
