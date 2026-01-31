FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Dependencies
import(path : "assertions.fs", version : "");

/**
 * BSPLINE DATA EXTRACTION & VALIDATION
 * =====================================
 *
 * Provides pure data extraction functions and validation predicates for
 * BSplineCurve structures. These functions do not modify curves, only read
 * and return information about properties, parameters, bounds, and continuity.
 *
 * | Function Category        | Purpose                                    |
 * |--------------------------|--------------------------------------------|
 * | Predicates               | Validate curve properties (clamped, etc.)  |
 * | Accessors                | Get degree, control points, weights, etc.  |
 * | Parameter/Knot Queries   | Parameter range, interior knots, spans     |
 * | Continuity Analysis      | Knot multiplicities, continuity orders     |
 * | Geometric Queries        | Bounds, endpoints, length estimates        |
 *
 * TYPE SAFETY:
 * - All functions validate BSplineCurve input via FeatureScript type system
 * - Predicates can be used for additional runtime validation
 * - Functions document assumptions (e.g., clamped knot vectors)
 *
 * @source footprint/fpt_analyze.fs, gordonSurface/gordonCurveCompatibility.fs
 */

// =============================================================================
// CONSTANTS
// =============================================================================

/**
 * Default tolerance for knot comparisons.
 */
export const KNOT_TOLERANCE = 1e-10;

/**
 * Default number of samples for bounding box estimation.
 */
export const BOUNDS_DEFAULT_SAMPLES = 25;

// =============================================================================
// VALIDATION PREDICATES
// =============================================================================

/**
 * Check if a BSpline curve has a clamped (open) knot vector.
 *
 * A clamped knot vector has the first (degree+1) knots equal to uMin
 * and the last (degree+1) knots equal to uMax. This ensures the curve
 * passes through its first and last control points.
 *
 * Most CAD systems use clamped knot vectors by default. Many algorithms
 * in this library assume clamped knots.
 *
 * @param curve {BSplineCurve} : Curve to check
 * @param tolerance {number} : Tolerance for knot comparison (default KNOT_TOLERANCE)
 * @returns {boolean} : True if knot vector is clamped
 *
 * @example Check before using algorithms that assume clamped knots:
 *   `if (!isClamped(curve)) throw regenError("Curve must be clamped");`
 */
export predicate isClamped(curve is BSplineCurve, tolerance is number)
{
    tolerance > 0;

    var knots = curve.knots;
    var p = curve.degree;
    var n = size(knots);

    // Check first p+1 knots are equal
    var uMin = knots[0];
    for (var i = 1; i <= p; i += 1)
    {
        abs(knots[i] - uMin) < tolerance;
    }

    // Check last p+1 knots are equal
    var uMax = knots[n - 1];
    for (var i = n - p - 1; i < n - 1; i += 1)
    {
        abs(knots[i] - uMax) < tolerance;
    }
}

/**
 * Check if curve is clamped with default tolerance.
 */
export predicate isClamped(curve is BSplineCurve)
{
    isClamped(curve, KNOT_TOLERANCE);
}

/**
 * Check if a BSpline curve is a Bezier curve (no interior knots).
 *
 * A Bezier curve is a special case of BSpline where:
 * - degree = numControlPoints - 1
 * - No interior knots (only clamped endpoint knots)
 *
 * @param curve {BSplineCurve} : Curve to check
 * @returns {boolean} : True if curve is Bezier
 */
export predicate isBezier(curve is BSplineCurve)
{
    curve.degree == size(curve.controlPoints) - 1;
}

/**
 * Check if knot vector is non-decreasing (valid).
 *
 * @param curve {BSplineCurve} : Curve to check
 * @returns {boolean} : True if knots are non-decreasing
 */
export predicate hasValidKnotOrder(curve is BSplineCurve)
{
    var knots = curve.knots;
    for (var i = 1; i < size(knots); i += 1)
    {
        knots[i] >= knots[i - 1];
    }
}

/**
 * Validate BSpline curve structure completeness.
 *
 * Checks that all required fields are present and consistent:
 * - Knot vector size = numControlPoints + degree + 1
 * - Weights array size matches control points (if rational)
 * - Degree >= 1 and <= numControlPoints - 1
 *
 * @param curve {BSplineCurve} : Curve to validate
 * @returns {boolean} : True if structure is valid
 */
export function isValidBSplineStructure(curve is BSplineCurve) returns boolean
{
    var degree = curve.degree;
    var numCP = size(curve.controlPoints);
    var numKnots = size(curve.knots);

    // Basic degree constraints
    if (degree < 1)
        return false;
    if (degree > numCP - 1)
        return false;

    // Knot vector size: m = n + p + 1 where n = numCP - 1, p = degree
    // So numKnots = numCP + degree
    var expectedKnots = numCP + degree + 1;
    if (numKnots != expectedKnots)
        return false;

    // Check knot order
    for (var i = 1; i < numKnots; i += 1)
    {
        if (curve.knots[i] < curve.knots[i - 1])
            return false;
    }

    // Check weights for rational curves
    if (curve.isRational)
    {
        if (curve.weights == undefined)
            return false;
        if (size(curve.weights) != numCP)
            return false;
        // All weights should be positive
        for (var w in curve.weights)
        {
            if (w <= 0)
                return false;
        }
    }

    return true;
}

// =============================================================================
// SIMPLE ACCESSORS
// =============================================================================

/**
 * Get the degree of a BSpline curve.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {number} : Polynomial degree (1=linear, 2=quadratic, 3=cubic, etc.)
 */
export function getDegree(curve is BSplineCurve) returns number
{
    return curve.degree;
}

/**
 * Get the number of control points.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {number} : Number of control points
 */
export function getNumControlPoints(curve is BSplineCurve) returns number
{
    return size(curve.controlPoints);
}

/**
 * Get the control points array.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {array} : Array of control point Vectors (with length units)
 */
export function getControlPoints(curve is BSplineCurve) returns array
{
    return curve.controlPoints;
}

/**
 * Get the knot vector.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {array} : Knot vector (unitless array of non-decreasing values)
 */
export function getKnotVector(curve is BSplineCurve) returns array
{
    return curve.knots;
}

/**
 * Get the weights array for rational curves.
 *
 * For non-rational curves, returns array of 1.0 values.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {array} : Weights array (same length as control points)
 */
export function getWeights(curve is BSplineCurve) returns array
{
    if (curve.isRational && curve.weights != undefined)
    {
        return curve.weights;
    }
    // Return uniform weights for non-rational curves
    return makeArray(size(curve.controlPoints), 1.0);
}

/**
 * Check if curve is rational (NURBS vs B-spline).
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {boolean} : True if curve uses rational weights
 */
export function isRational(curve is BSplineCurve) returns boolean
{
    return curve.isRational == true;
}

/**
 * Check if curve is periodic (closed with continuous derivatives).
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {boolean} : True if curve is periodic
 */
export function isPeriodic(curve is BSplineCurve) returns boolean
{
    return curve.isPeriodic == true;
}

/**
 * Get curve dimension (2D or 3D).
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {number} : 2 or 3
 */
export function getDimension(curve is BSplineCurve) returns number
{
    return curve.dimension;
}

// =============================================================================
// PARAMETER & KNOT QUERIES
// =============================================================================

/**
 * Get parameter range from BSpline knot vector.
 *
 * For clamped BSplines (standard), returns [knots[0], knots[last]].
 * This is the valid parameter domain for curve evaluation.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {map} : {uMin: number, uMax: number}
 *
 * @example `getBSplineParamRange(myCurve)` returns `{uMin: 0, uMax: 1}`
 *
 * @source footprint/fpt_analyze.fs:71-78
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
 * Get interior knots (excluding clamped endpoints).
 *
 * For a clamped BSpline of degree p, the first (p+1) and last (p+1)
 * knots are repeated at the endpoints. Interior knots are those between.
 * Returns empty array for Bezier curves (no interior knots).
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {array} : Interior knot values (may include repeated values)
 *
 * @example Cubic BSpline with knots [0,0,0,0, 0.5, 1,1,1,1]:
 *   `getInteriorKnots(curve)` returns `[0.5]`
 *
 * @note For Bezier curves (degree = numCP - 1), returns []
 * @note Assumes clamped knot vector
 *
 * @source gordonSurface/gordonCurveCompatibility.fs:154-174
 */
export function getInteriorKnots(curve is BSplineCurve) returns array
{
    var degree = curve.degree;
    var knots = curve.knots;

    var startIndex = degree + 1;
    var endIndex = size(knots) - degree - 1;

    if (endIndex <= startIndex)
    {
        return [];  // Bezier curve, no interior knots
    }

    var interior = [];
    for (var i = startIndex; i < endIndex; i += 1)
    {
        interior = append(interior, knots[i]);
    }

    return interior;
}

/**
 * Get unique interior knots (no duplicates).
 *
 * Returns each unique interior knot value once, useful for
 * iterating over knot spans.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @param tolerance {number} : Tolerance for considering knots equal (default KNOT_TOLERANCE)
 * @returns {array} : Unique interior knot values, sorted
 */
export function getUniqueInteriorKnots(curve is BSplineCurve, tolerance is number) returns array
{
    if (tolerance == undefined)
        tolerance = KNOT_TOLERANCE;

    var interior = getInteriorKnots(curve);
    if (size(interior) == 0)
        return [];

    var unique = [interior[0]];
    for (var i = 1; i < size(interior); i += 1)
    {
        var lastUnique = unique[size(unique) - 1];
        if (abs(interior[i] - lastUnique) > tolerance)
        {
            unique = append(unique, interior[i]);
        }
    }

    return unique;
}

/**
 * Get the number of knot spans (Bezier segments).
 *
 * A BSpline with k unique interior knots has k+1 spans.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {number} : Number of Bezier segments
 */
export function getNumSpans(curve is BSplineCurve) returns number
{
    return size(getUniqueInteriorKnots(curve, KNOT_TOLERANCE)) + 1;
}

// =============================================================================
// KNOT MULTIPLICITY & CONTINUITY ANALYSIS
// =============================================================================

/**
 * Get knot multiplicity map.
 *
 * Counts how many times each unique knot value appears in the knot vector.
 * Useful for continuity analysis (multiplicity m at knot means C^(p-m) continuity).
 *
 * @param curve {BSplineCurve} : Curve to analyze
 * @param tolerance {number} : Tolerance for considering knots equal (default KNOT_TOLERANCE)
 * @returns {map} : Map from knot value to multiplicity count
 *
 * @example Knot vector [0,0,0,0, 0.5, 0.5, 1,1,1,1]:
 *   `getBSplineKnotMultiplicities(curve)` returns `{0: 4, 0.5: 2, 1: 4}`
 *
 * @note Uses tolerance-based grouping for nearly-equal knots
 */
export function getBSplineKnotMultiplicities(curve is BSplineCurve, tolerance is number) returns map
{
    if (tolerance == undefined)
        tolerance = KNOT_TOLERANCE;

    var knots = curve.knots;
    var mults = {};
    var uniqueKnots = [];  // Track unique knots in order

    for (var knot in knots)
    {
        // Find if this knot matches an existing unique knot
        var foundKey = undefined;
        for (var existingKnot in uniqueKnots)
        {
            if (abs(knot - existingKnot) < tolerance)
            {
                foundKey = existingKnot;
                break;
            }
        }

        if (foundKey != undefined)
        {
            mults[foundKey] = mults[foundKey] + 1;
        }
        else
        {
            uniqueKnots = append(uniqueKnots, knot);
            mults[knot] = 1;
        }
    }

    return mults;
}

/**
 * Get knot multiplicity map with default tolerance.
 */
export function getBSplineKnotMultiplicities(curve is BSplineCurve) returns map
{
    return getBSplineKnotMultiplicities(curve, KNOT_TOLERANCE);
}

/**
 * Get multiplicity of a specific knot value.
 *
 * @param curve {BSplineCurve} : Curve to analyze
 * @param knotValue {number} : Knot value to look up
 * @param tolerance {number} : Tolerance for knot comparison (default KNOT_TOLERANCE)
 * @returns {number} : Multiplicity (0 if knot not found)
 */
export function getKnotMultiplicity(curve is BSplineCurve, knotValue is number, tolerance is number) returns number
{
    if (tolerance == undefined)
        tolerance = KNOT_TOLERANCE;

    var count = 0;
    for (var knot in curve.knots)
    {
        if (abs(knot - knotValue) < tolerance)
        {
            count += 1;
        }
    }
    return count;
}

/**
 * Get knot with maximum multiplicity.
 *
 * Useful for finding the location of lowest continuity.
 * Excludes endpoint knots (which are typically clamped with multiplicity = degree+1).
 *
 * @param curve {BSplineCurve} : Curve to analyze
 * @param tolerance {number} : Tolerance for knot comparison (default KNOT_TOLERANCE)
 * @returns {map} : {knot: number, multiplicity: number} or undefined if no interior knots
 */
export function getMaxInteriorKnotMultiplicity(curve is BSplineCurve, tolerance is number) returns map
{
    if (tolerance == undefined)
        tolerance = KNOT_TOLERANCE;

    var interior = getInteriorKnots(curve);
    if (size(interior) == 0)
        return undefined;

    // Count multiplicities of interior knots
    var maxKnot = interior[0];
    var maxMult = 1;
    var currentKnot = interior[0];
    var currentMult = 1;

    for (var i = 1; i < size(interior); i += 1)
    {
        if (abs(interior[i] - currentKnot) < tolerance)
        {
            currentMult += 1;
        }
        else
        {
            if (currentMult > maxMult)
            {
                maxMult = currentMult;
                maxKnot = currentKnot;
            }
            currentKnot = interior[i];
            currentMult = 1;
        }
    }

    // Check last run
    if (currentMult > maxMult)
    {
        maxMult = currentMult;
        maxKnot = currentKnot;
    }

    return { "knot" : maxKnot, "multiplicity" : maxMult };
}

/**
 * Get continuity order at a specific knot.
 *
 * Continuity at a knot is: C^k where k = degree - multiplicity
 * - k = degree means C^degree (smooth, knot has multiplicity 1)
 * - k = 0 means C^0 (position continuous only, knot has multiplicity = degree)
 * - k < 0 means discontinuous (knot has multiplicity > degree, which is invalid)
 *
 * @param curve {BSplineCurve} : Curve to analyze
 * @param knotValue {number} : Knot value to check
 * @param tolerance {number} : Tolerance for knot comparison (default KNOT_TOLERANCE)
 * @returns {number} : Continuity order (C^k), or -1 if knot not found
 *
 * @example For cubic (degree=3) curve:
 *   - Multiplicity 1 at knot 0.5 → C^2 continuity (returns 2)
 *   - Multiplicity 2 at knot 0.5 → C^1 continuity (returns 1)
 *   - Multiplicity 3 at knot 0.5 → C^0 continuity (returns 0)
 */
export function getContinuityAtKnot(curve is BSplineCurve, knotValue is number, tolerance is number) returns number
{
    if (tolerance == undefined)
        tolerance = KNOT_TOLERANCE;

    var multiplicity = getKnotMultiplicity(curve, knotValue, tolerance);

    if (multiplicity == 0)
        return -1;  // Knot not found

    return curve.degree - multiplicity;
}

/**
 * Get minimum continuity order across all interior knots.
 *
 * Returns the lowest continuity in the curve's interior.
 * Useful for checking if a curve meets smoothness requirements.
 *
 * @param curve {BSplineCurve} : Curve to analyze
 * @param tolerance {number} : Tolerance for knot comparison (default KNOT_TOLERANCE)
 * @returns {number} : Minimum C^k order, or curve.degree if no interior knots (Bezier)
 *
 * @example Check if curve is at least C^1:
 *   `if (getMinInteriorContinuity(curve) >= 1) { ... }`
 */
export function getMinInteriorContinuity(curve is BSplineCurve, tolerance is number) returns number
{
    if (tolerance == undefined)
        tolerance = KNOT_TOLERANCE;

    var maxMultResult = getMaxInteriorKnotMultiplicity(curve, tolerance);

    if (maxMultResult == undefined)
    {
        // No interior knots (Bezier) - infinitely smooth
        return curve.degree;
    }

    return curve.degree - maxMultResult.multiplicity;
}

/**
 * Check if curve has at least C^k continuity everywhere.
 *
 * @param curve {BSplineCurve} : Curve to check
 * @param k {number} : Required continuity order
 * @returns {boolean} : True if curve is at least C^k continuous
 *
 * @example Check for G2 compatibility (need at least C^1):
 *   `if (hasMinContinuity(curve, 1)) { ... }`
 */
export function hasMinContinuity(curve is BSplineCurve, k is number) returns boolean
{
    return getMinInteriorContinuity(curve, KNOT_TOLERANCE) >= k;
}

// =============================================================================
// GEOMETRIC QUERIES
// =============================================================================

/**
 * Get BSpline curve endpoints.
 *
 * Evaluates the curve at its parameter extrema to return the
 * start and end points as Vectors.
 *
 * @param curve {BSplineCurve} : Curve to query
 * @returns {map} : {start: Vector, end: Vector}
 *
 * @example `getBSplineEndpoints(curve)` returns start/end points
 */
export function getBSplineEndpoints(curve is BSplineCurve) returns map
{
    var range = getBSplineParamRange(curve);
    var result = evaluateSpline({
        "spline" : curve,
        "parameters" : [range.uMin, range.uMax]
    });

    return {
        "start" : result[0][0],
        "end" : result[0][1]
    };
}

/**
 * Get axis-aligned bounding box of BSpline by sampling.
 *
 * Samples the curve at uniformly-spaced parameters and computes
 * min/max extents. For precise bounds of high-curvature curves,
 * increase numSamples.
 *
 * @param curve {BSplineCurve} : Curve to bound
 * @param numSamples {number} : Number of sample points (default 25)
 * @returns {map} : {xMin, xMax, yMin, yMax, zMin, zMax} in curve's units
 *
 * @example `getBSplineBounds(curve)` returns bounding box
 *
 * @note This is an approximation - does not find exact extrema
 * @note For exact bounds, would need to find derivative zeros
 *
 * @source footprint/fpt_analyze.fs:84-119
 */
export function getBSplineBounds(curve is BSplineCurve, numSamples is number) returns map
{
    if (numSamples == undefined)
        numSamples = BOUNDS_DEFAULT_SAMPLES;

    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var result = evaluateSpline({ "spline" : curve, "parameters" : params });
    var positions = result[0];

    if (size(positions) == 0)
    {
        throw regenError("getBSplineBounds: Failed to evaluate curve");
    }

    // Initialize with first point
    var firstPt = positions[0];
    var xMin = firstPt[0];
    var xMax = firstPt[0];
    var yMin = firstPt[1];
    var yMax = firstPt[1];
    var zMin = firstPt[2];
    var zMax = firstPt[2];

    for (var i = 1; i < size(positions); i += 1)
    {
        var pt = positions[i];
        xMin = min([xMin, pt[0]]);
        xMax = max([xMax, pt[0]]);
        yMin = min([yMin, pt[1]]);
        yMax = max([yMax, pt[1]]);
        zMin = min([zMin, pt[2]]);
        zMax = max([zMax, pt[2]]);
    }

    return {
        "xMin" : xMin,
        "xMax" : xMax,
        "yMin" : yMin,
        "yMax" : yMax,
        "zMin" : zMin,
        "zMax" : zMax
    };
}

/**
 * Get bounding box with default sample count.
 */
export function getBSplineBounds(curve is BSplineCurve) returns map
{
    return getBSplineBounds(curve, BOUNDS_DEFAULT_SAMPLES);
}

/**
 * Estimate curve length by sampling.
 *
 * Computes approximate arc length by summing chord lengths between
 * sample points. For accurate arc length, use arc_length.fs functions.
 *
 * @param curve {BSplineCurve} : Curve to measure
 * @param numSamples {number} : Number of sample points (default 50)
 * @returns {ValueWithUnits} : Approximate arc length
 *
 * @note This is a chord-length approximation (underestimates true length)
 * @note For accurate arc length, use buildArcLengthTable() from arc_length.fs
 */
export function estimateCurveLength(curve is BSplineCurve, numSamples is number)
{
    if (numSamples == undefined)
        numSamples = 50;

    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var result = evaluateSpline({ "spline" : curve, "parameters" : params });
    var positions = result[0];

    var totalLength = 0 * meter;
    for (var i = 1; i < size(positions); i += 1)
    {
        totalLength += norm(positions[i] - positions[i - 1]);
    }

    return totalLength;
}

/**
 * Check if two curves have matching endpoints (within tolerance).
 *
 * Useful for checking curve connectivity before joining operations.
 *
 * @param curveA {BSplineCurve} : First curve
 * @param curveB {BSplineCurve} : Second curve
 * @param tolerance {ValueWithUnits} : Distance tolerance
 * @returns {map} : {
 *                    connected: boolean,
 *                    connectionType: string ("A_END_B_START", "A_END_B_END", etc.)
 *                  }
 */
export function checkEndpointConnection(curveA is BSplineCurve, curveB is BSplineCurve, tolerance) returns map
{
    var endpointsA = getBSplineEndpoints(curveA);
    var endpointsB = getBSplineEndpoints(curveB);

    var tolValue = tolerance;
    try silent { tolValue = tolerance.value; }

    // Check all four combinations
    if (norm(endpointsA.end - endpointsB.start) < tolerance)
    {
        return { "connected" : true, "connectionType" : "A_END_B_START" };
    }
    if (norm(endpointsA.end - endpointsB.end) < tolerance)
    {
        return { "connected" : true, "connectionType" : "A_END_B_END" };
    }
    if (norm(endpointsA.start - endpointsB.start) < tolerance)
    {
        return { "connected" : true, "connectionType" : "A_START_B_START" };
    }
    if (norm(endpointsA.start - endpointsB.end) < tolerance)
    {
        return { "connected" : true, "connectionType" : "A_START_B_END" };
    }

    return { "connected" : false, "connectionType" : "NONE" };
}
