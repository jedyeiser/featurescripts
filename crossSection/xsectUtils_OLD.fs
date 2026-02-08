FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * XSECT UTILS - Cross Section Utility Functions
 * ==============================================
 * 
 * Reusable helper functions for cross-section analysis:
 * - Body signature extraction (for caching)
 * - Cross-section frame generation
 * - B-spline sampling utilities
 * - Query helpers
 */

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
 * Approximate curve length using control polygon.
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

export function debugControlPolygon(context is Context, curve is BSplineCurve, color is DebugColor)
{
    var cps = curve.controlPoints;
    for (var i = 1; i < size(cps); i += 1)
    {
        addDebugLine(context, cps[i-1], cps[i], color);
    }
}
