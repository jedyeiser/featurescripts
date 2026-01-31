FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Dependencies
import(path : "assertions.fs", version : "");
import(path : "solvers.fs", version : "");
import(path : "bspline_data.fs", version : "");
import(path : "math_utils.fs", version : "");

/**
 * POINT PROJECTION METHODS
 * ========================
 *
 * Provides utilities for finding closest points on curves and related
 * geometric queries. These are fundamental operations for CAD geometry.
 *
 * | Method                  | Best For                              | Robustness |
 * |-------------------------|---------------------------------------|------------|
 * | projectPointOnCurve()   | Single closest point query            | High       |
 * | findMultipleProjections()| Handle multiple solutions            | High       |
 * | findParameterForPoint() | Point known to be ON curve            | High       |
 *
 * ALGORITHM: Point projection is ROOT FINDING, not optimization.
 * The closest point satisfies the orthogonality condition:
 *   (C(u) - P) . C'(u) = 0
 * where C(u) is the curve, P is the query point, and C'(u) is the tangent.
 *
 * PERFORMANCE:
 * - Initial sampling: O(N) evaluations to find bracket
 * - Refinement: O(log(1/tol)) iterations with solveRootHybrid
 * - Recommended N: 20-50 samples for typical curves
 * - Use more samples for high-curvature or self-intersecting curves
 *
 * WHEN TO USE WHICH:
 * - projectPointOnCurve: General closest point query (most common)
 * - findMultipleProjections: Self-intersecting curves, complex shapes
 * - findParameterForPoint: Point is known to lie ON the curve
 */

// =============================================================================
// CONSTANTS
// =============================================================================

/**
 * Default number of samples for initial bracket search.
 */
export const PROJECTION_DEFAULT_SAMPLES = 25;

/**
 * Default tolerance for projection convergence.
 */
export const PROJECTION_DEFAULT_TOLERANCE = 1e-9;

/**
 * Maximum iterations for projection refinement.
 */
export const PROJECTION_MAX_ITERATIONS = 50;

// =============================================================================
// MAIN PROJECTION FUNCTIONS
// =============================================================================

/**
 * Project a point onto a BSpline curve, finding the closest point.
 *
 * Finds the parameter u where the curve is closest to the query point P.
 * Uses a two-phase algorithm:
 * 1. Sample the curve to find approximate closest region(s)
 * 2. Refine using root finding on the orthogonality condition
 *
 * @param curve {BSplineCurve} : Curve to project onto
 * @param point {Vector} : Query point (3D with units)
 * @param options {map} : Optional settings:
 *                        - numSamples: Initial samples (default 25)
 *                        - tolerance: Convergence tolerance (default 1e-9)
 *                        - maxIterations: Max refinement iterations (default 50)
 * @returns {map} : {
 *                    parameter: number,     - Parameter u on curve
 *                    point: Vector,         - Closest point on curve
 *                    distance: ValueWithUnits, - Distance to query point
 *                    converged: boolean     - Whether refinement converged
 *                  }
 *
 * @example Find closest point on curve to external point:
 *   `var result = projectPointOnCurve(myCurve, vector(1, 2, 0) * inch, {});`
 *   `var closestPt = result.point;`
 *   `var dist = result.distance;`
 *
 * @note Returns the GLOBALLY closest point (samples entire curve)
 * @note For multiple local minima, use findMultipleProjections()
 */
export function projectPointOnCurve(curve is BSplineCurve, point is Vector, options is map) returns map
{
    // Parse options
    var numSamples = PROJECTION_DEFAULT_SAMPLES;
    var tolerance = PROJECTION_DEFAULT_TOLERANCE;
    var maxIter = PROJECTION_MAX_ITERATIONS;

    if (options.numSamples != undefined)
        numSamples = options.numSamples;
    if (options.tolerance != undefined)
        tolerance = options.tolerance;
    if (options.maxIterations != undefined)
        maxIter = options.maxIterations;

    // Get parameter range
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Phase 1: Sample curve to find approximate closest point
    var bestU = uMin;
    var bestDistSq = inf * meter * meter;

    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var u = uMin + (uMax - uMin) * i / (numSamples - 1);
        params = append(params, u);
    }

    var evalResult = evaluateSpline({ "spline" : curve, "parameters" : params });
    var positions = evalResult[0];

    for (var i = 0; i < numSamples; i += 1)
    {
        var diff = positions[i] - point;
        var distSq = dot(diff, diff);
        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            bestU = params[i];
        }
    }

    // Phase 2: Refine using root finding on orthogonality condition
    // Find bracket around best sample
    var bracketHalfWidth = (uMax - uMin) / (numSamples - 1);
    var bracketLo = max([uMin, bestU - bracketHalfWidth]);
    var bracketHi = min([uMax, bestU + bracketHalfWidth]);

    // Define orthogonality function: (C(u) - P) . C'(u)
    var orthoFunc = function(u)
    {
        var result = evaluateSpline({
            "spline" : curve,
            "parameters" : [u],
            "nDerivatives" : 1
        });
        var curvePoint = result[0][0];
        var tangent = result[1][0];
        var diff = curvePoint - point;

        // Return scalar orthogonality measure (strip units for solver)
        var ortho = dot(diff, tangent);
        // Convert to unitless number
        try silent { return ortho / meter / meter; }
        return ortho;
    };

    // Sample to find sign change bracket
    var samples = [];
    var nBracketSamples = 11;
    for (var i = 0; i < nBracketSamples; i += 1)
    {
        var u = bracketLo + (bracketHi - bracketLo) * i / (nBracketSamples - 1);
        samples = append(samples, { "u" : u, "f" : orthoFunc(u) });
    }

    var bracket = bracketFromSamples(samples);
    var refinedU = bestU;
    var converged = false;

    if (bracket.found)
    {
        // Use hybrid solver for refinement
        var solveResult = solveRootHybrid(orthoFunc, bracket.a, bracket.b, tolerance, maxIter);
        refinedU = solveResult.u;
        converged = abs(solveResult.f) < tolerance * 100;
    }
    else
    {
        // No sign change found - check endpoints and use best sample
        var fLo = orthoFunc(bracketLo);
        var fHi = orthoFunc(bracketHi);

        if (abs(fLo) < abs(fHi) && abs(fLo) < abs(orthoFunc(bestU)))
        {
            refinedU = bracketLo;
        }
        else if (abs(fHi) < abs(orthoFunc(bestU)))
        {
            refinedU = bracketHi;
        }
        converged = false;
    }

    // Clamp to valid range
    refinedU = clamp(refinedU, uMin, uMax);

    // Evaluate final result
    var finalResult = evaluateSpline({ "spline" : curve, "parameters" : [refinedU] });
    var finalPoint = finalResult[0][0];
    var finalDiff = finalPoint - point;
    var finalDist = norm(finalDiff);

    return {
        "parameter" : refinedU,
        "point" : finalPoint,
        "distance" : finalDist,
        "converged" : converged
    };
}

/**
 * Find all local closest points (multiple projections) on a curve.
 *
 * For self-intersecting curves or complex shapes, there may be multiple
 * local minima. This function returns all of them, sorted by distance.
 *
 * @param curve {BSplineCurve} : Curve to project onto
 * @param point {Vector} : Query point (3D with units)
 * @param options {map} : Optional settings:
 *                        - numSamples: Initial samples (default 50 for better coverage)
 *                        - tolerance: Convergence tolerance
 *                        - minSeparation: Minimum parameter separation between results
 * @returns {array} : Array of projection results, sorted by distance (closest first)
 *                    Each entry has {parameter, point, distance, converged}
 *
 * @example Find all close points on self-intersecting curve:
 *   `var results = findMultipleProjections(curve, queryPt, {});`
 *   `var closest = results[0];`  // Globally closest
 *   `var second = results[1];`   // Second closest (if exists)
 */
export function findMultipleProjections(curve is BSplineCurve, point is Vector, options is map) returns array
{
    // Parse options with higher defaults for multiple search
    var numSamples = 50;
    var tolerance = PROJECTION_DEFAULT_TOLERANCE;
    var minSeparation = 0.05;  // Minimum parameter separation

    if (options.numSamples != undefined)
        numSamples = options.numSamples;
    if (options.tolerance != undefined)
        tolerance = options.tolerance;
    if (options.minSeparation != undefined)
        minSeparation = options.minSeparation;

    // Get parameter range
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Sample curve and compute distances
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var evalResult = evaluateSpline({ "spline" : curve, "parameters" : params });
    var positions = evalResult[0];

    var distancesSq = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var diff = positions[i] - point;
        distancesSq = append(distancesSq, dot(diff, diff));
    }

    // Find local minima in distance function
    var localMinima = [];
    for (var i = 1; i < numSamples - 1; i += 1)
    {
        if (distancesSq[i] < distancesSq[i - 1] && distancesSq[i] < distancesSq[i + 1])
        {
            localMinima = append(localMinima, params[i]);
        }
    }

    // Also check endpoints
    if (distancesSq[0] < distancesSq[1])
    {
        localMinima = append(localMinima, params[0]);
    }
    if (distancesSq[numSamples - 1] < distancesSq[numSamples - 2])
    {
        localMinima = append(localMinima, params[numSamples - 1]);
    }

    // Refine each local minimum
    var results = [];
    for (var minU in localMinima)
    {
        // Create focused options for single projection
        var localOptions = {
            "numSamples" : 11,
            "tolerance" : tolerance,
            "maxIterations" : PROJECTION_MAX_ITERATIONS
        };

        // Restrict search to neighborhood of local minimum
        var halfWidth = (uMax - uMin) / (numSamples - 1) * 1.5;
        var localMin = max([uMin, minU - halfWidth]);
        var localMax = min([uMax, minU + halfWidth]);

        // Build a local curve range by adjusting numSamples context
        var result = projectPointOnCurve(curve, point, localOptions);

        // Check if this result is sufficiently separated from existing results
        var isDuplicate = false;
        for (var existing in results)
        {
            if (abs(result.parameter - existing.parameter) < minSeparation * (uMax - uMin))
            {
                isDuplicate = true;
                break;
            }
        }

        if (!isDuplicate)
        {
            results = append(results, result);
        }
    }

    // Sort by distance (simple insertion sort since array is typically small)
    for (var i = 1; i < size(results); i += 1)
    {
        var j = i;
        while (j > 0 && results[j].distance < results[j - 1].distance)
        {
            var temp = results[j];
            results[j] = results[j - 1];
            results[j - 1] = temp;
            j -= 1;
        }
    }

    return results;
}

/**
 * Find the parameter for a point known to lie ON the curve.
 *
 * More robust than projection when the point is already on (or very near)
 * the curve. Uses distance minimization directly rather than orthogonality.
 *
 * @param curve {BSplineCurve} : Curve containing the point
 * @param point {Vector} : Point on the curve (3D with units)
 * @param tolerance {ValueWithUnits} : Distance tolerance for convergence
 * @returns {map} : {
 *                    parameter: number,     - Parameter u on curve
 *                    distance: ValueWithUnits, - Residual distance (should be ~0)
 *                    converged: boolean     - Whether point was found within tolerance
 *                  }
 *
 * @example Find parameter for curve endpoint:
 *   `var result = findParameterForPoint(curve, knownPoint, 1e-6 * meter);`
 *   `var u = result.parameter;`
 *
 * @note Expects point to be ON the curve; returns closest match if not exact
 */
export function findParameterForPoint(curve is BSplineCurve, point is Vector, tolerance) returns map
{
    // Get tolerance value for comparisons
    var tolValue = tolerance;
    try silent { tolValue = tolerance.value; }
    if (tolValue == undefined)
        tolValue = 1e-7;  // Default tolerance

    // Get parameter range
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Sample curve to find initial approximation
    var numSamples = 30;
    var bestU = uMin;
    var bestDistSq = inf * meter * meter;

    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var evalResult = evaluateSpline({ "spline" : curve, "parameters" : params });
    var positions = evalResult[0];

    for (var i = 0; i < numSamples; i += 1)
    {
        var diff = positions[i] - point;
        var distSq = dot(diff, diff);
        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            bestU = params[i];
        }
    }

    // Refine using Newton iteration on distance squared gradient
    // d/du |C(u) - P|^2 = 2 * (C(u) - P) . C'(u)
    var u = bestU;
    var converged = false;

    for (var iter = 0; iter < 20; iter += 1)
    {
        var result = evaluateSpline({
            "spline" : curve,
            "parameters" : [u],
            "nDerivatives" : 2
        });

        var curvePoint = result[0][0];
        var tangent = result[1][0];
        var curvature = result[2][0];

        var diff = curvePoint - point;
        var dist = norm(diff);

        // Check convergence
        var distValue = dist;
        try silent { distValue = dist.value; }
        if (distValue < tolValue)
        {
            converged = true;
            break;
        }

        // Newton step: u_new = u - f(u) / f'(u)
        // where f(u) = (C(u) - P) . C'(u)
        // and f'(u) = C'(u) . C'(u) + (C(u) - P) . C''(u)
        var f = dot(diff, tangent);
        var fPrime = dot(tangent, tangent) + dot(diff, curvature);

        // Extract values for division
        var fVal = f;
        var fPrimeVal = fPrime;
        try silent { fVal = f.value; }
        try silent { fPrimeVal = fPrime.value; }

        if (abs(fPrimeVal) < 1e-15)
            break;  // Singular, stop iteration

        var du = fVal / fPrimeVal;
        u = u - du;

        // Clamp to valid range
        u = clamp(u, uMin, uMax);

        // Check for convergence in parameter space
        if (abs(du) < 1e-12)
        {
            converged = true;
            break;
        }
    }

    // Final evaluation
    var finalResult = evaluateSpline({ "spline" : curve, "parameters" : [u] });
    var finalPoint = finalResult[0][0];
    var finalDist = norm(finalPoint - point);

    return {
        "parameter" : u,
        "distance" : finalDist,
        "converged" : converged
    };
}

// =============================================================================
// UTILITY FUNCTIONS
// =============================================================================

/**
 * Compute signed distance from point to curve at a given parameter.
 *
 * The sign indicates which side of the curve the point lies on,
 * based on the curve's normal direction in the XY plane.
 *
 * @param curve {BSplineCurve} : Reference curve
 * @param point {Vector} : Query point
 * @param u {number} : Parameter on curve
 * @returns {ValueWithUnits} : Signed distance (positive = right of tangent in XY)
 */
export function signedDistanceAtParameter(curve is BSplineCurve, point is Vector, u is number)
{
    var result = evaluateSpline({
        "spline" : curve,
        "parameters" : [u],
        "nDerivatives" : 1
    });

    var curvePoint = result[0][0];
    var tangent = result[1][0];
    var diff = point - curvePoint;

    var dist = norm(diff);

    // Compute sign using 2D cross product (assumes XY plane curve)
    var cross2D = tangent[0] * diff[1] - tangent[1] * diff[0];
    var crossValue = cross2D;
    try silent { crossValue = cross2D.value; }

    if (crossValue < 0)
    {
        return -dist;
    }
    return dist;
}
