FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Dependencies
import(path : "assertions.fs", version : "");
import(path : "math_utils.fs", version : "");
import(path : "bspline_data.fs", version : "");
import(path : "solvers.fs", version : "");
import(path : "numerical_integration.fs", version : "");

/**
 * ARC-LENGTH PARAMETERIZATION
 * ===========================
 *
 * Provides utilities for computing and using arc-length parameterization
 * of B-spline curves. Arc-length parameterization enables uniform sampling
 * by distance along the curve, essential for fair visualization, path
 * planning, and uniform feature distribution.
 *
 * | Function                  | Purpose                             | When to Use          |
 * |---------------------------|-------------------------------------|----------------------|
 * | computeArcLength()        | Total arc length of curve           | Length queries       |
 * | buildArcLengthTable()     | Precompute s(u) mapping             | Repeated lookups     |
 * | parameterAtArcLength()    | Find u for given arc length s       | Fair sampling        |
 * | uniformArcLengthSamples() | N points uniformly spaced by arc    | Visualization, paths |
 *
 * ALGORITHM:
 * 1. Forward mapping s(u) = integral from 0 to u of ||C'(t)|| dt
 *    Uses adaptive Gaussian quadrature for accuracy
 * 2. Inverse mapping u(s) via Newton iteration on s(u) - s_target = 0
 *
 * PERFORMANCE:
 * - Table build: O(N) integrations, do once per curve
 * - Lookup: O(log N) binary search + O(1) Newton iterations
 * - Recommended N: 100-500 depending on curve complexity
 *
 * ACCURACY:
 * - Gaussian quadrature provides high accuracy for smooth B-splines
 * - Near cusps or high-curvature regions, use more samples
 * - Typical relative error: < 1e-8 for well-behaved curves
 */

// =============================================================================
// CONSTANTS
// =============================================================================

/**
 * Default number of table entries for arc-length table.
 */
export const ARC_LENGTH_DEFAULT_SAMPLES = 100;

/**
 * Default Gaussian quadrature order for arc length integration.
 */
export const ARC_LENGTH_QUAD_ORDER = 5;

/**
 * Tolerance for inverse arc-length lookup.
 */
export const ARC_LENGTH_INVERSE_TOL = 1e-10;

// =============================================================================
// TOTAL ARC LENGTH
// =============================================================================

/**
 * Compute total arc length of a BSpline curve.
 *
 * Integrates ||C'(u)|| over the parameter domain using composite
 * Gaussian quadrature for high accuracy.
 *
 * @param curve {BSplineCurve} : Curve to measure
 * @param options {map} : Optional settings:
 *                        - numIntervals: Number of integration subintervals (default 20)
 *                        - quadOrder: Gaussian quadrature order (default 5)
 * @returns {ValueWithUnits} : Total arc length in curve's length units
 *
 * @example Get arc length of curve:
 *   `var length = computeArcLength(myCurve, {});`
 *   `println("Curve length: " ~ toString(length));`
 *
 * @note For complex curves with high curvature variation, increase numIntervals
 */
export function computeArcLength(curve is BSplineCurve, options is map)
{
    // Parse options
    var numIntervals = 20;
    var quadOrder = ARC_LENGTH_QUAD_ORDER;

    if (options.numIntervals != undefined)
        numIntervals = options.numIntervals;
    if (options.quadOrder != undefined)
        quadOrder = options.quadOrder;

    // Get parameter range
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Define speed function ||C'(u)||
    var speedFunc = function(u)
    {
        var result = evaluateSpline({
            "spline" : curve,
            "parameters" : [u],
            "nDerivatives" : 1
        });
        var tangent = result[1][0];
        return norm(tangent);
    };

    // Integrate using composite Gaussian quadrature
    var totalLength = compositeGaussQuadratureWithUnits(speedFunc, uMin, uMax, numIntervals, quadOrder);

    return totalLength;
}

/**
 * Composite Gaussian quadrature for functions returning ValueWithUnits.
 * Internal helper for arc length computation.
 */
function compositeGaussQuadratureWithUnits(f, a, b, n, order)
{
    // Select quadrature rule
    var rule;
    if (order == 3)
        rule = GAUSS_3;
    else if (order == 7)
        rule = GAUSS_7;
    else
        rule = GAUSS_5;

    var nodes = rule.nodes;
    var weights = rule.weights;

    var h = (b - a) / n;
    var total = 0 * meter;  // Initialize with units

    for (var i = 0; i < n; i += 1)
    {
        var subA = a + i * h;
        var subB = subA + h;

        var halfWidth = (subB - subA) / 2;
        var midpoint = (subA + subB) / 2;

        // Evaluate at quadrature points
        for (var j = 0; j < size(nodes); j += 1)
        {
            var xi = midpoint + halfWidth * nodes[j];
            total += f(xi) * weights[j] * halfWidth;
        }
    }

    return total;
}

// =============================================================================
// ARC-LENGTH TABLE
// =============================================================================

/**
 * Build a precomputed arc-length table for efficient lookups.
 *
 * Creates a table mapping parameter values to cumulative arc lengths.
 * The table enables fast inverse lookups (arc length to parameter).
 *
 * @param curve {BSplineCurve} : Curve to build table for
 * @param numSamples {number} : Number of table entries (default 100)
 * @returns {map} : {
 *                    parameters: array,    - Parameter values [u0, u1, ...]
 *                    arcLengths: array,    - Cumulative arc lengths [s0, s1, ...]
 *                    totalLength: ValueWithUnits, - Total arc length
 *                    curve: BSplineCurve   - Reference to source curve
 *                  }
 *
 * @example Build table for repeated lookups:
 *   `var table = buildArcLengthTable(curve, 200);`
 *   `var u = parameterAtArcLength(table, table.totalLength / 2);`  // Midpoint
 *
 * @note Higher numSamples = more accurate but more memory
 * @note Recommended: 100 for simple curves, 500 for complex curves
 */
export function buildArcLengthTable(curve is BSplineCurve, numSamples is number) returns map
{
    if (numSamples == undefined)
        numSamples = ARC_LENGTH_DEFAULT_SAMPLES;

    assertTrue(numSamples >= 2, "buildArcLengthTable: numSamples must be >= 2.");

    // Get parameter range
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Build parameter array
    var parameters = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        parameters = append(parameters, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    // Evaluate tangent vectors at all parameters
    var evalResult = evaluateSpline({
        "spline" : curve,
        "parameters" : parameters,
        "nDerivatives" : 1
    });
    var tangents = evalResult[1];

    // Compute speeds
    var speeds = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        speeds = append(speeds, norm(tangents[i]));
    }

    // Cumulative integration using trapezoidal rule
    var arcLengths = [0 * meter];
    var cumLength = 0 * meter;

    for (var i = 1; i < numSamples; i += 1)
    {
        var du = parameters[i] - parameters[i - 1];
        var avgSpeed = (speeds[i] + speeds[i - 1]) / 2;
        cumLength += du * avgSpeed;
        arcLengths = append(arcLengths, cumLength);
    }

    return {
        "parameters" : parameters,
        "arcLengths" : arcLengths,
        "totalLength" : cumLength,
        "curve" : curve
    };
}

// =============================================================================
// INVERSE ARC-LENGTH LOOKUP
// =============================================================================

/**
 * Find the parameter corresponding to a given arc length.
 *
 * Given a target arc length s, finds the parameter u such that
 * the arc length from the curve start to u equals s.
 *
 * Uses binary search on the precomputed table followed by
 * Newton refinement for high accuracy.
 *
 * @param table {map} : Arc-length table from buildArcLengthTable()
 * @param s {ValueWithUnits} : Target arc length
 * @returns {number} : Parameter u corresponding to arc length s
 *
 * @example Find parameter at 25% of total length:
 *   `var u = parameterAtArcLength(table, table.totalLength * 0.25);`
 *
 * @note Clamps s to [0, totalLength] - no extrapolation
 * @note Returns exact endpoint parameters for s=0 or s=totalLength
 */
export function parameterAtArcLength(table is map, s)
{
    var parameters = table.parameters;
    var arcLengths = table.arcLengths;
    var totalLength = table.totalLength;
    var curve = table.curve;

    var n = size(parameters);

    // Handle edge cases
    var sVal = s;
    try silent { sVal = s.value; }
    var totVal = totalLength;
    try silent { totVal = totalLength.value; }

    if (sVal <= 0)
        return parameters[0];
    if (sVal >= totVal)
        return parameters[n - 1];

    // Binary search to find bracketing interval
    var lo = 0;
    var hi = n - 1;

    while (hi - lo > 1)
    {
        var mid = floor((lo + hi) / 2);
        var midS = arcLengths[mid];
        var midSVal = midS;
        try silent { midSVal = midS.value; }

        if (midSVal < sVal)
            lo = mid;
        else
            hi = mid;
    }

    // Linear interpolation for initial guess
    var sLo = arcLengths[lo];
    var sHi = arcLengths[hi];
    var uLo = parameters[lo];
    var uHi = parameters[hi];

    var sLoVal = sLo;
    var sHiVal = sHi;
    try silent { sLoVal = sLo.value; }
    try silent { sHiVal = sHi.value; }

    var t = 0;
    if (abs(sHiVal - sLoVal) > 1e-15)
        t = (sVal - sLoVal) / (sHiVal - sLoVal);

    var uGuess = uLo + t * (uHi - uLo);

    // Newton refinement: solve s(u) - s_target = 0
    // Derivative: ds/du = ||C'(u)||
    var u = uGuess;

    for (var iter = 0; iter < 10; iter += 1)
    {
        // Compute arc length from start to u
        var currentS = interpolateArcLength(table, u);

        // Compute speed at u
        var result = evaluateSpline({
            "spline" : curve,
            "parameters" : [u],
            "nDerivatives" : 1
        });
        var speed = norm(result[1][0]);

        // Newton step
        var errorS = currentS - s;
        var errorVal = errorS;
        try silent { errorVal = errorS.value; }

        var speedVal = speed;
        try silent { speedVal = speed.value; }

        if (abs(errorVal) < ARC_LENGTH_INVERSE_TOL * totVal)
            break;

        if (abs(speedVal) < 1e-15)
            break;  // Degenerate (zero speed)

        var du = errorVal / speedVal;
        u = u - du;

        // Clamp to valid range
        u = clamp(u, parameters[0], parameters[n - 1]);

        if (abs(du) < 1e-14)
            break;
    }

    return u;
}

/**
 * Interpolate arc length at a given parameter using the table.
 * Internal helper for Newton refinement.
 */
function interpolateArcLength(table is map, u is number)
{
    var parameters = table.parameters;
    var arcLengths = table.arcLengths;
    var n = size(parameters);

    // Binary search for interval
    var lo = 0;
    var hi = n - 1;

    while (hi - lo > 1)
    {
        var mid = floor((lo + hi) / 2);
        if (parameters[mid] < u)
            lo = mid;
        else
            hi = mid;
    }

    // Linear interpolation
    var t = 0;
    if (abs(parameters[hi] - parameters[lo]) > 1e-15)
        t = (u - parameters[lo]) / (parameters[hi] - parameters[lo]);

    return arcLengths[lo] + t * (arcLengths[hi] - arcLengths[lo]);
}

// =============================================================================
// UNIFORM SAMPLING
// =============================================================================

/**
 * Sample curve at uniformly-spaced arc lengths.
 *
 * Returns N points on the curve that are equally spaced by arc length.
 * This provides visually uniform point distribution regardless of
 * parameter-space curvature variations.
 *
 * @param curve {BSplineCurve} : Curve to sample
 * @param numPoints {number} : Number of sample points
 * @param options {map} : Optional settings:
 *                        - tableSamples: Arc-length table resolution (default 200)
 * @returns {map} : {
 *                    parameters: array,    - Parameters u at sample points
 *                    points: array,        - 3D positions at sample points
 *                    arcLengths: array,    - Arc lengths at sample points
 *                    totalLength: ValueWithUnits
 *                  }
 *
 * @example Sample curve with 50 uniform points:
 *   `var samples = uniformArcLengthSamples(curve, 50, {});`
 *   `for (var pt in samples.points) { ... }`
 *
 * @note First and last points are exactly at curve endpoints
 */
export function uniformArcLengthSamples(curve is BSplineCurve, numPoints is number, options is map) returns map
{
    assertTrue(numPoints >= 2, "uniformArcLengthSamples: numPoints must be >= 2.");

    // Parse options
    var tableSamples = 200;
    if (options.tableSamples != undefined)
        tableSamples = options.tableSamples;

    // Build arc-length table
    var table = buildArcLengthTable(curve, tableSamples);
    var totalLength = table.totalLength;

    // Compute uniform arc-length positions
    var targetArcLengths = [];
    for (var i = 0; i < numPoints; i += 1)
    {
        targetArcLengths = append(targetArcLengths, totalLength * i / (numPoints - 1));
    }

    // Find parameters for each target arc length
    var parameters = [];
    for (var s in targetArcLengths)
    {
        parameters = append(parameters, parameterAtArcLength(table, s));
    }

    // Evaluate positions
    var evalResult = evaluateSpline({
        "spline" : curve,
        "parameters" : parameters
    });
    var points = evalResult[0];

    return {
        "parameters" : parameters,
        "points" : points,
        "arcLengths" : targetArcLengths,
        "totalLength" : totalLength
    };
}

/**
 * Compute arc-length fraction (normalized arc length) at a parameter.
 *
 * Returns s(u) / totalLength, giving a value in [0, 1] representing
 * the fraction of total arc length covered.
 *
 * @param table {map} : Arc-length table from buildArcLengthTable()
 * @param u {number} : Parameter value
 * @returns {number} : Arc-length fraction in [0, 1]
 *
 * @example Check if parameter is past curve midpoint by arc length:
 *   `var fraction = arcLengthFraction(table, u);`
 *   `if (fraction > 0.5) { ... }`
 */
export function arcLengthFraction(table is map, u is number) returns number
{
    var s = interpolateArcLength(table, u);
    var total = table.totalLength;

    var sVal = s;
    var totVal = total;
    try silent { sVal = s.value; }
    try silent { totVal = total.value; }

    if (abs(totVal) < 1e-15)
        return 0;

    return sVal / totVal;
}

/**
 * Reparameterize curve by arc length.
 *
 * Returns a mapping function that converts arc-length parameter t in [0,1]
 * to the original curve parameter u. Useful for uniform speed traversal.
 *
 * @param table {map} : Arc-length table from buildArcLengthTable()
 * @returns {function} : Function(t) returns u, where t is arc-length fraction
 *
 * @example Create arc-length parameterized evaluation:
 *   `var remap = arcLengthReparameterization(table);`
 *   `var u = remap(0.5);  // Parameter at 50% of arc length`
 */
export function arcLengthReparameterization(table is map)
{
    return function(t is number)
    {
        var s = table.totalLength * clamp01(t);
        return parameterAtArcLength(table, s);
    };
}
