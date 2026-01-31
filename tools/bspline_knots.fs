FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Phase 3 dependencies
import(path : "bspline_data.fs", version : "");

/**
 * BSpline knot manipulation using Piegl & Tiller algorithms.
 *
 * Implements fundamental knot operations from "The NURBS Book" (2nd Ed.):
 * - A2.1: FindSpan - Binary search for knot span
 * - A5.1: CurveKnotIns - Boehm's knot insertion
 * - A5.4: RefineKnotVectCurve - Insert multiple knots
 *
 * These algorithms preserve curve shape exactly while modifying the
 * knot vector and control point structure.
 *
 * @source gordonSurface/gordonKnotOps.fs
 * @reference Piegl & Tiller "The NURBS Book" 2nd Ed.
 */

/**
 * Find knot span index using binary search.
 *
 * Implements P&T Algorithm A2.1 (p.68). Given a parameter u and
 * knot vector, finds the index of the knot span containing u.
 *
 * For a clamped knot vector [0,...,0, u_p+1, ..., u_m-p-1, 1,...,1]
 * with degree p, returns index i where knots[i] <= u < knots[i+1].
 *
 * Special case: If u equals the last knot, returns n (last valid span).
 *
 * @param degree {number} : BSpline degree (p)
 * @param u {number} : Parameter value to locate
 * @param knots {array} : Knot vector (length m+1)
 * @returns {number} : Span index i where knots[i] <= u < knots[i+1]
 *
 * @example For knots=[0,0,0,0.5,1,1,1], degree=2, u=0.3:
 *   `findSpan(2, 0.3, knots)` returns `2` (span [0, 0.5))
 *
 * @reference Piegl & Tiller Algorithm A2.1, p.68
 * @source gordonSurface/gordonKnotOps.fs:434-463
 */
export function findSpan(degree is number, u is number, knots is array) returns number
{
    var n = size(knots) - degree - 2;  // Last valid index for control points

    // Special case: u at end of knot vector
    if (u >= knots[n + 1])
    {
        return n;
    }

    // Binary search
    var low = degree;
    var high = n + 1;
    var mid = floor((low + high) / 2);

    while (u < knots[mid] || u >= knots[mid + 1])
    {
        if (u < knots[mid])
        {
            high = mid;
        }
        else
        {
            low = mid;
        }
        mid = floor((low + high) / 2);
    }

    return mid;
}

/**
 * Insert a single knot into a BSpline curve without changing shape.
 *
 * Implements Boehm's knot insertion algorithm (P&T Algorithm A5.1, p.151).
 * Inserts a knot at parameter knotParam, increasing the number of control
 * points by 1 while preserving the curve geometry exactly.
 *
 * The algorithm uses the knot insertion formula (P&T Eq. 5.15):
 *   α_i = (u_bar - u_i) / (u_{i+p} - u_i)
 *   Q_i = (1 - α_i) * P_{i-1} + α_i * P_i
 *
 * where u_bar is the new knot, p is degree, P are old control points,
 * Q are new control points.
 *
 * @param context {Context} : Onshape context (not used, but kept for API compatibility)
 * @param bSpline {BSplineCurve} : Curve to modify
 * @param knotParam {number} : Parameter value for new knot
 * @returns {BSplineCurve} : New curve with inserted knot
 *
 * @example Insert knot at u=0.5:
 *   `insertKnot(context, myCurve, 0.5)` returns modified curve
 *
 * @note Does not modify input curve - returns new BSplineCurve
 * @note Handles rational curves correctly (weights interpolated same as points)
 *
 * @reference Piegl & Tiller Algorithm A5.1, p.151; Equation 5.15
 * @source gordonSurface/gordonKnotOps.fs:138-239
 */
export function insertKnot(context is Context, bSpline is BSplineCurve, knotParam is number) returns BSplineCurve
{
    var degree = bSpline.degree;
    var oldPoints = bSpline.controlPoints;
    var oldKnots = bSpline.knots;
    var n = size(oldPoints) - 1;

    // Find knot span k where oldKnots[k] <= knotParam < oldKnots[k+1]
    // Using P&T Algorithm A2.1
    var k = degree;
    while (k < size(oldKnots) - degree - 2 && oldKnots[k + 1] <= knotParam)
    {
        k += 1;
    }

    // Compute new control points
    var newPoints = makeArray(n + 2);

    for (var i = 0; i <= n + 1; i += 1)
    {
        if (i <= k - degree)
        {
            // Before insertion - points unchanged
            newPoints[i] = oldPoints[i];
        }
        else if (i > k)
        {
            // After insertion - shift index
            newPoints[i] = oldPoints[i - 1];
        }
        else
        {
            // At insertion - blend using alpha
            var alpha = (knotParam - oldKnots[i]) / (oldKnots[i + degree] - oldKnots[i]);
            newPoints[i] = (1 - alpha) * oldPoints[i - 1] + alpha * oldPoints[i];
        }
    }

    // Insert new knot at position k + 1
    var newKnots = makeArray(size(oldKnots) + 1);
    for (var i = 0; i <= k; i += 1)
    {
        newKnots[i] = oldKnots[i];
    }
    newKnots[k + 1] = knotParam;
    for (var i = k + 1; i < size(oldKnots); i += 1)
    {
        newKnots[i + 1] = oldKnots[i];
    }

    // Compute new weights (for rational curves)
    var newWeights = undefined;
    if (bSpline.isRational && bSpline.weights != undefined)
    {
        var oldWeights = bSpline.weights;
        newWeights = makeArray(n + 2);

        for (var i = 0; i <= n + 1; i += 1)
        {
            if (i <= k - degree)
            {
                newWeights[i] = oldWeights[i];
            }
            else if (i > k)
            {
                newWeights[i] = oldWeights[i - 1];
            }
            else
            {
                var alpha = (knotParam - oldKnots[i]) / (oldKnots[i + degree] - oldKnots[i]);
                newWeights[i] = (1 - alpha) * oldWeights[i - 1] + alpha * oldWeights[i];
            }
        }
    }
    else
    {
        newWeights = makeArray(n + 2, 1.0);
    }

    return {
        "degree" : degree,
        "isPeriodic" : bSpline.isPeriodic,
        "controlPoints" : newPoints,
        "knots" : newKnots,
        "weights" : newWeights,
        "isRational" : bSpline.isRational,
        "dimension" : bSpline.dimension
    } as BSplineCurve;
}

/**
 * Insert multiple knots into a BSpline curve in a single pass.
 *
 * Implements P&T Algorithm A5.4 "RefineKnotVectCurve" (p.164-165).
 * More efficient than repeated single insertions when adding many knots.
 *
 * Algorithm processes knots from right to left, updating control points
 * and knot vector simultaneously. Automatically filters out knots that
 * would exceed maximum multiplicity (degree).
 *
 * @param context {Context} : Onshape context (for API compatibility)
 * @param bSpline {BSplineCurve} : Curve to modify
 * @param knotsToInsert {array} : Sorted array of knot values to insert
 * @returns {BSplineCurve} : New curve with refined knot vector
 *
 * @example Insert knots at multiple locations:
 *   `refineKnotVector(context, curve, [0.25, 0.5, 0.75])`
 *
 * @note Knots are sanitized to prevent exceeding max multiplicity
 * @note Returns input curve unchanged if knotsToInsert is empty
 * @note Does not modify input curve
 *
 * @reference Piegl & Tiller Algorithm A5.4, p.164-165
 * @source gordonSurface/gordonKnotOps.fs:251-369
 */
export function refineKnotVector(context is Context, bSpline is BSplineCurve, knotsToInsert is array) returns BSplineCurve
{
    if (size(knotsToInsert) == 0)
    {
        return bSpline;
    }

    var p = bSpline.degree;
    var U = bSpline.knots;
    var Pw = bSpline.controlPoints;
    var weights = bSpline.weights;
    var isRational = bSpline.isRational && weights != undefined;

    const tolerance = 1e-8;
    var cleanedParams = sanitizeKnotsToInsert(U, knotsToInsert, p, tolerance);

    if (size(cleanedParams) == 0)
    {
        return bSpline;
    }

    var n = size(Pw) - 1;
    var m = size(U) - 1;
    var X = cleanedParams;
    var r = size(X) - 1;

    var newNumCPs = n + r + 2;
    var newNumKnots = m + r + 2;

    // Find spans of first and last knots to insert
    var a = findSpan(p, X[0], U);
    var b = findSpan(p, X[r], U) + 1;

    var Qw = makeArray(newNumCPs);
    var Ubar = makeArray(newNumKnots);
    var newWeights = makeArray(newNumCPs);

    // Copy unchanged points at start
    for (var j = 0; j <= a - p; j += 1)
    {
        Qw[j] = Pw[j];
        newWeights[j] = isRational ? weights[j] : 1;
    }

    // Copy unchanged points at end
    for (var j = b - 1; j <= n; j += 1)
    {
        Qw[j + r + 1] = Pw[j];
        newWeights[j + r + 1] = isRational ? weights[j] : 1.0;
    }

    // Copy unchanged knots at start
    for (var j = 0; j <= a; j += 1)
    {
        Ubar[j] = U[j];
    }

    // Copy unchanged knots at end
    for (var j = b + p; j <= m; j += 1)
    {
        Ubar[j + r + 1] = U[j];
    }

    // Main loop (process from right to left)
    var i = b + p - 1;
    var k = b + p + r;

    for (var j = r; j >= 0; j -= 1)
    {
        while (X[j] <= U[i] && i > a)
        {
            Qw[k - p - 1] = Pw[i - p - 1];
            newWeights[k - p - 1] = isRational ? weights[i - p - 1] : 1;
            Ubar[k] = U[i];
            k -= 1;
            i -= 1;
        }

        Qw[k - p - 1] = Qw[k - p];
        newWeights[k - p - 1] = newWeights[k - p];

        for (var l = 1; l <= p; l += 1)
        {
            var ind = k - p + l;
            var alpha = Ubar[k + l] - X[j];

            if (abs(alpha) < 1e-14)
            {
                Qw[ind - 1] = Qw[ind];
                newWeights[ind - 1] = newWeights[ind];
            }
            else
            {
                alpha = alpha / (Ubar[k + l] - U[i - p + l]);
                Qw[ind - 1] = alpha * Qw[ind - 1] + (1 - alpha) * Qw[ind];
                newWeights[ind - 1] = alpha * newWeights[ind - 1] + (1 - alpha) * newWeights[ind];
            }
        }

        Ubar[k] = X[j];
        k -= 1;
    }

    return {
        "degree" : p,
        "isPeriodic" : bSpline.isPeriodic,
        "controlPoints" : Qw,
        "knots" : Ubar,
        "weights" : newWeights,
        "isRational" : bSpline.isRational,
        "dimension" : bSpline.dimension
    } as BSplineCurve;
}

/**
 * Merge two knot vectors, taking maximum multiplicities.
 *
 * Combines two knot vectors by including all unique knots with
 * multiplicity equal to the maximum multiplicity from either vector.
 * Uses tolerance-based comparison for identifying equal knots.
 *
 * @param knotsA {array} : First knot vector
 * @param knotsB {array} : Second knot vector
 * @param tolerance {number} : Tolerance for knot equality
 * @returns {array} : Merged knot vector (sorted)
 *
 * @example Merge [0,0,0.5,1,1] and [0,0,0.5,0.5,1,1]:
 *   returns [0,0,0.5,0.5,1,1] (max multiplicities)
 */
export function mergeKnotVectors(knotsA is array, knotsB is array, tolerance is number) returns array
{
    var multsA = {};
    var multsB = {};

    // Count multiplicities in A
    for (var knot in knotsA)
    {
        var key = roundToTolerance(knot, tolerance);
        multsA[key] = (multsA[key] == undefined) ? 1 : multsA[key] + 1;
    }

    // Count multiplicities in B
    for (var knot in knotsB)
    {
        var key = roundToTolerance(knot, tolerance);
        multsB[key] = (multsB[key] == undefined) ? 1 : multsB[key] + 1;
    }

    // Merge with max multiplicities
    var merged = [];
    var allKeys = {};
    for (var key in multsA)
        allKeys[key] = true;
    for (var key in multsB)
        allKeys[key] = true;

    for (var key in allKeys)
    {
        var countA = (multsA[key] == undefined) ? 0 : multsA[key];
        var countB = (multsB[key] == undefined) ? 0 : multsB[key];
        var maxCount = max([countA, countB]);

        for (var i = 0; i < maxCount; i += 1)
        {
            merged = append(merged, key);
        }
    }

    return sort(merged, function(a, b) { return a - b; });
}

/**
 * Filter and validate knots before insertion.
 *
 * Removes duplicates and knots that would exceed maximum multiplicity (degree).
 * Ensures knot vector remains valid after insertion.
 *
 * @param existingKnots {array} : Current knot vector
 * @param knotsToInsert {array} : Candidate knots to insert
 * @param degree {number} : Curve degree
 * @param tolerance {number} : Tolerance for knot comparison
 * @returns {array} : Filtered, sorted array of valid knots to insert
 *
 * @note Skips knots that would create multiplicity > degree
 * @note Returns empty array if all knots are invalid
 *
 * @source gordonSurface/gordonKnotOps.fs:376-422
 */
export function sanitizeKnotsToInsert(existingKnots is array, knotsToInsert is array,
                                       degree is number, tolerance is number) returns array
{
    if (size(knotsToInsert) == 0)
    {
        return [];
    }

    // Sort ascending
    var sorted = sort(knotsToInsert, function(a, b) { return a - b; });

    // Count multiplicities in existing knot vector
    var multiplicities = {};
    for (var knot in existingKnots)
    {
        var key = roundToTolerance(knot, tolerance);
        multiplicities[key] = (multiplicities[key] == undefined) ? 1 : multiplicities[key] + 1;
    }

    // Filter: keep only knots that won't exceed max multiplicity
    var valid = [];
    for (var knot in sorted)
    {
        var key = roundToTolerance(knot, tolerance);
        var currentMult = (multiplicities[key] == undefined) ? 0 : multiplicities[key];

        if (currentMult < degree)  // Can still insert
        {
            valid = append(valid, knot);
            multiplicities[key] = currentMult + 1;
        }
    }

    return valid;
}

/**
 * Round value to tolerance for knot comparison.
 *
 * Helper function for tolerance-based knot equality testing.
 *
 * @param value {number} : Value to round
 * @param tolerance {number} : Rounding tolerance
 * @returns {number} : Rounded value
 *
 * @example `roundToTolerance(0.50000001, 1e-6)` returns `0.5`
 *
 * @source gordonSurface/gordonKnotOps.fs:424-427
 */
export function roundToTolerance(value is number, tolerance is number) returns number
{
    return round(value / tolerance) * tolerance;
}
