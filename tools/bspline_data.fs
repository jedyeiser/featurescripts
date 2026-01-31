FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * BSpline data extraction utilities.
 *
 * Provides pure data extraction functions for BSplineCurve structures.
 * These functions do not modify curves, only read and return information
 * about parameter ranges, bounds, knots, and geometric properties.
 *
 * @source footprint/fpt_analyze.fs, gordonSurface/gordonCurveCompatibility.fs
 */

/**
 * Get parameter range from BSpline knot vector.
 *
 * For clamped BSplines (standard), returns [knots[0], knots[last]].
 * This is the valid parameter domain for curve evaluation.
 *
 * @param bspline {BSplineCurve} : Curve to query
 * @returns {map} : {uMin: number, uMax: number}
 *
 * @example `getBSplineParamRange(myCurve)` returns `{uMin: 0, uMax: 1}`
 *
 * @source footprint/fpt_analyze.fs:71-78
 */
export function getBSplineParamRange(bspline is BSplineCurve) returns map
{
    var knots = bspline.knots;
    return {
        "uMin" : knots[0],
        "uMax" : knots[size(knots) - 1]
    };
}

/**
 * Get axis-aligned bounding box of BSpline by sampling.
 *
 * Samples the curve at 25 uniformly-spaced parameters and computes
 * min/max extents in X and Y. For precise bounds of high-curvature
 * curves, consider increasing sample count.
 *
 * @param bspline {BSplineCurve} : Curve to bound
 * @returns {map} : {xMin, xMax, yMin, yMax} in curve's units
 *
 * @example `getBSplineBounds(curve)` returns bounding box
 *
 * @note Uses 25 samples - approximation only, not exact bounds
 * @note For exact bounds, would need to find derivative zeros
 *
 * @source footprint/fpt_analyze.fs:84-119
 */
export function getBSplineBounds(bspline is BSplineCurve) returns map
{
    var range = getBSplineParamRange(bspline);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var numSamples = 25;
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, uMin + (uMax - uMin) * i / (numSamples - 1));
    }

    var result = evaluateSpline({ "spline" : bspline, "parameters" : params });
    var positions = result[0];

    var xMin = inf * meter;
    var xMax = -inf * meter;
    var yMin = inf * meter;
    var yMax = -inf * meter;

    for (var pt in positions)
    {
        xMin = min([xMin, pt[0]]);
        xMax = max([xMax, pt[0]]);
        yMin = min([yMin, pt[1]]);
        yMax = max([yMax, pt[1]]);
    }

    return {
        "xMin" : xMin,
        "xMax" : xMax,
        "yMin" : yMin,
        "yMax" : yMax
    };
}

/**
 * Get interior knots (excluding clamped endpoints).
 *
 * For a clamped BSpline of degree p, the first (p+1) and last (p+1)
 * knots are repeated at the endpoints. Interior knots are those between.
 * Returns empty array for Bezier curves (no interior knots).
 *
 * @param bspline {BSplineCurve} : Curve to query
 * @returns {array} : Interior knot values (may include repeated values)
 *
 * @example Cubic BSpline with knots [0,0,0,0, 0.5, 1,1,1,1]:
 *   `getInteriorKnots(curve)` returns `[0.5]`
 *
 * @note For Bezier curves (degree = numCP - 1), returns []
 *
 * @source gordonSurface/gordonCurveCompatibility.fs:154-174
 */
export function getInteriorKnots(bspline is BSplineCurve) returns array
{
    var degree = bspline.degree;
    var knots = bspline.knots;

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
 * Get BSpline curve endpoints.
 *
 * Evaluates the curve at its parameter extrema to return the
 * start and end points as Vectors.
 *
 * @param bspline {BSplineCurve} : Curve to query
 * @returns {map} : {start: Vector, end: Vector}
 *
 * @example `getBSplineEndpoints(curve)` returns start/end points
 */
export function getBSplineEndpoints(bspline is BSplineCurve) returns map
{
    var range = getBSplineParamRange(bspline);
    var result = evaluateSpline({
        "spline" : bspline,
        "parameters" : [range.uMin, range.uMax]
    });

    return {
        "start" : result[0][0],
        "end" : result[0][1]
    };
}

/**
 * Get knot multiplicity map.
 *
 * Counts how many times each unique knot value appears in the knot vector.
 * Useful for continuity analysis (multiplicity = degree means C0, etc.).
 *
 * @param bspline {BSplineCurve} : Curve to analyze
 * @param tolerance {number} : Tolerance for considering knots equal (optional, default 1e-12)
 * @returns {map} : Map from rounded knot value to count
 *
 * @example Knot vector [0,0,0,0, 0.5, 0.5, 1,1,1,1]:
 *   `getBSplineKnotMultiplicities(curve)` returns `{0: 4, 0.5: 2, 1: 4}`
 *
 * @note Uses tolerance-based rounding to group nearly-equal knots
 */
export function getBSplineKnotMultiplicities(bspline is BSplineCurve, tolerance is number) returns map
{
    var knots = bspline.knots;
    if (tolerance == undefined)
        tolerance = 1e-12;

    var mults = {};
    for (var knot in knots)
    {
        var key = round(knot / tolerance) * tolerance;
        if (mults[key] == undefined)
        {
            mults[key] = 1;
        }
        else
        {
            mults[key] += 1;
        }
    }

    return mults;
}
