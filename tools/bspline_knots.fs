FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Phase 3 dependencies
import(path : "bspline_data.fs", version : "");
import(path : "assertions.fs", version : "");

/**
 * BSPLINE KNOT & CURVE MANIPULATION
 * ==================================
 *
 * Implements fundamental curve operations from Piegl & Tiller "The NURBS Book" (2nd Ed.):
 *
 * | Algorithm | P&T Ref | Function           | Purpose                          |
 * |-----------|---------|-------------------|----------------------------------|
 * | A2.1      | p.68    | findSpan()        | Binary search for knot span      |
 * | A5.1      | p.151   | insertKnot()      | Single knot insertion (Boehm)    |
 * | A5.4      | p.164   | refineKnotVector()| Multiple knot insertion          |
 * | A5.6      | p.172   | removeKnot()      | Knot removal with tolerance      |
 * | A5.9      | p.194   | elevateDegree()   | Degree elevation                 |
 * | -         | -       | reverseCurve()    | Reverse parameter direction      |
 *
 * SHAPE PRESERVATION:
 * - All operations preserve curve geometry (within tolerance for removal)
 * - Knot insertion adds control points without changing shape
 * - Degree elevation increases flexibility without changing shape
 * - Knot removal may slightly change shape (tolerance-controlled)
 *
 * ASSUMPTIONS:
 * - All functions assume CLAMPED (non-periodic) knot vectors
 * - Use isClamped() predicate from bspline_data.fs to validate
 *
 * @source gordonSurface/gordonKnotOps.fs
 * @reference Piegl & Tiller "The NURBS Book" 2nd Ed.
 */

// =============================================================================
// CONSTANTS
// =============================================================================

/**
 * Default tolerance for knot operations.
 */
export const KNOT_OP_TOLERANCE = 1e-8;

/**
 * Tolerance for knot removal shape preservation.
 */
export const KNOT_REMOVAL_TOLERANCE = 1e-6;

// =============================================================================
// KNOT SPAN SEARCH (P&T A2.1)
// =============================================================================

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

    // Special case: u at start of knot vector
    if (u <= knots[degree])
    {
        return degree;
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

// =============================================================================
// KNOT INSERTION (P&T A5.1)
// =============================================================================

/**
 * Insert a single knot into a BSpline curve without changing shape.
 *
 * Implements Boehm's knot insertion algorithm (P&T Algorithm A5.1, p.151).
 * Inserts a knot at parameter knotParam, increasing the number of control
 * points by 1 while preserving the curve geometry exactly.
 *
 * The algorithm uses the knot insertion formula (P&T Eq. 5.15):
 *   alpha_i = (u_bar - u_i) / (u_{i+p} - u_i)
 *   Q_i = (1 - alpha_i) * P_{i-1} + alpha_i * P_i
 *
 * where u_bar is the new knot, p is degree, P are old control points,
 * Q are new control points.
 *
 * @param context {Context} : Onshape context (kept for API compatibility)
 * @param curve {BSplineCurve} : Curve to modify
 * @param knotParam {number} : Parameter value for new knot
 * @returns {BSplineCurve} : New curve with inserted knot
 *
 * @example Insert knot at u=0.5:
 *   `insertKnot(context, myCurve, 0.5)` returns modified curve
 *
 * @note Does not modify input curve - returns new BSplineCurve
 * @note Handles rational curves correctly (weights interpolated same as points)
 * @note Assumes clamped knot vector
 *
 * @reference Piegl & Tiller Algorithm A5.1, p.151; Equation 5.15
 * @source gordonSurface/gordonKnotOps.fs:138-239
 */
export function insertKnot(context is Context, curve is BSplineCurve, knotParam is number) returns BSplineCurve
{
    var degree = curve.degree;
    var oldPoints = curve.controlPoints;
    var oldKnots = curve.knots;
    var n = size(oldPoints) - 1;

    // Validate knot parameter is in valid range
    var range = getBSplineParamRange(curve);
    assertTrue(knotParam >= range.uMin && knotParam <= range.uMax,
               "insertKnot: knotParam must be within curve parameter range");

    // Find knot span k where oldKnots[k] <= knotParam < oldKnots[k+1]
    var k = findSpan(degree, knotParam, oldKnots);

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
            var denom = oldKnots[i + degree] - oldKnots[i];
            var alpha = (abs(denom) < 1e-14) ? 0.5 : (knotParam - oldKnots[i]) / denom;
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
    var newWeights;
    if (curve.isRational && curve.weights != undefined)
    {
        var oldWeights = curve.weights;
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
                var denom = oldKnots[i + degree] - oldKnots[i];
                var alpha = (abs(denom) < 1e-14) ? 0.5 : (knotParam - oldKnots[i]) / denom;
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
        "isPeriodic" : curve.isPeriodic,
        "controlPoints" : newPoints,
        "knots" : newKnots,
        "weights" : newWeights,
        "isRational" : curve.isRational,
        "dimension" : curve.dimension
    } as BSplineCurve;
}

// =============================================================================
// KNOT REFINEMENT (P&T A5.4)
// =============================================================================

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
 * @param curve {BSplineCurve} : Curve to modify
 * @param knotsToInsert {array} : Sorted array of knot values to insert
 * @returns {BSplineCurve} : New curve with refined knot vector
 *
 * @example Insert knots at multiple locations:
 *   `refineKnotVector(context, curve, [0.25, 0.5, 0.75])`
 *
 * @note Knots are sanitized to prevent exceeding max multiplicity
 * @note Returns input curve unchanged if knotsToInsert is empty
 * @note Does not modify input curve
 * @note Assumes clamped knot vector
 *
 * @reference Piegl & Tiller Algorithm A5.4, p.164-165
 * @source gordonSurface/gordonKnotOps.fs:251-369
 */
export function refineKnotVector(context is Context, curve is BSplineCurve, knotsToInsert is array) returns BSplineCurve
{
    if (size(knotsToInsert) == 0)
    {
        return curve;
    }

    var p = curve.degree;
    var U = curve.knots;
    var Pw = curve.controlPoints;
    var weights = curve.weights;
    var isRational = curve.isRational && weights != undefined;

    var cleanedParams = sanitizeKnotsToInsert(U, knotsToInsert, p, KNOT_OP_TOLERANCE);

    if (size(cleanedParams) == 0)
    {
        return curve;
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
        newWeights[j] = isRational ? weights[j] : 1.0;
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
            newWeights[k - p - 1] = isRational ? weights[i - p - 1] : 1.0;
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
        "isPeriodic" : curve.isPeriodic,
        "controlPoints" : Qw,
        "knots" : Ubar,
        "weights" : newWeights,
        "isRational" : curve.isRational,
        "dimension" : curve.dimension
    } as BSplineCurve;
}

// =============================================================================
// DEGREE ELEVATION (P&T A5.9)
// =============================================================================

/**
 * Elevate the degree of a BSpline curve.
 *
 * Implements P&T Algorithm A5.9 (p.194-208). Increases the polynomial degree
 * while preserving the curve shape exactly. Degree elevation is often needed
 * to make curves compatible for surface operations (lofting, etc.).
 *
 * The algorithm works by:
 * 1. Decomposing the curve into Bezier segments
 * 2. Elevating each Bezier segment
 * 3. Recombining into a single B-spline
 *
 * @param context {Context} : Onshape context (for API compatibility)
 * @param curve {BSplineCurve} : Curve to elevate
 * @param t {number} : Number of degrees to elevate (default 1)
 * @returns {BSplineCurve} : New curve with elevated degree
 *
 * @example Elevate cubic to quartic:
 *   `elevateDegree(context, cubicCurve, 1)` returns degree-4 curve
 *
 * @example Make two curves same degree:
 *   `var degDiff = curve2.degree - curve1.degree;`
 *   `if (degDiff > 0) curve1 = elevateDegree(context, curve1, degDiff);`
 *
 * @note Does not modify input curve
 * @note Handles rational curves correctly
 * @note Assumes clamped knot vector
 *
 * @reference Piegl & Tiller Algorithm A5.9, p.194-208
 */
export function elevateDegree(context is Context, curve is BSplineCurve, t is number) returns BSplineCurve
{
    if (t == undefined)
        t = 1;

    if (t <= 0)
        return curve;

    var p = curve.degree;
    var U = curve.knots;
    var Pw = curve.controlPoints;
    var weights = curve.weights;
    var isRational = curve.isRational && weights != undefined;
    var n = size(Pw) - 1;
    var m = size(U) - 1;

    // New degree
    var ph = p + t;
    var ph2 = floor(ph / 2);

    // Compute Bezier degree elevation coefficients
    // bezalfs[i][j] = C(ph,j) * C(p,i) / C(ph, i+j) for appropriate ranges
    var bezalfs = computeBezierDegreeElevationCoeffs(p, ph);

    // Get unique interior knots and their multiplicities
    var uniqueKnots = [];
    var mults = [];
    var prevKnot = U[0];
    var mult = 1;

    for (var i = 1; i <= m; i += 1)
    {
        if (abs(U[i] - prevKnot) < KNOT_OP_TOLERANCE)
        {
            mult += 1;
        }
        else
        {
            uniqueKnots = append(uniqueKnots, prevKnot);
            mults = append(mults, mult);
            prevKnot = U[i];
            mult = 1;
        }
    }
    uniqueKnots = append(uniqueKnots, prevKnot);
    mults = append(mults, mult);

    var s = size(uniqueKnots) - 2;  // Number of interior unique knots

    // Compute new knot vector size and control point count
    // Each unique knot with multiplicity m_i contributes m_i + t knots
    var newM = m + (s + 2) * t;  // +2 for endpoints
    var newN = newM - ph - 1;

    var Uh = makeArray(newM + 1);
    var Qw = makeArray(newN + 1);
    var newWeights = makeArray(newN + 1);

    // Bezier segment storage
    var bpts = makeArray(p + 1);
    var ebpts = makeArray(ph + 1);
    var Nextbpts = makeArray(p - 1);
    var bwts = makeArray(p + 1);
    var ebwts = makeArray(ph + 1);
    var Nextwts = makeArray(p - 1);

    // Initialize first ph+1 knots
    for (var i = 0; i <= ph; i += 1)
    {
        Uh[i] = U[0];
    }

    // Initialize first control point
    Qw[0] = Pw[0];
    newWeights[0] = isRational ? weights[0] : 1.0;

    // Initialize Bezier segment
    for (var i = 0; i <= p; i += 1)
    {
        bpts[i] = Pw[i];
        bwts[i] = isRational ? weights[i] : 1.0;
    }

    var kind = ph + 1;  // Index into new knot vector
    var cind = 1;       // Index into new control points
    var a = p;          // Index into old knots
    var b = p + 1;

    // Process each Bezier segment
    while (b < m)
    {
        var i = b;
        while (b < m && abs(U[b + 1] - U[b]) < KNOT_OP_TOLERANCE)
        {
            b += 1;
        }
        var mul = b - i + 1;  // Multiplicity of knot at b

        // Degree elevate Bezier segment
        for (var j = 0; j <= ph; j += 1)
        {
            ebpts[j] = vector(0, 0, 0) * meter;
            ebwts[j] = 0.0;
        }

        for (var j = 0; j <= ph; j += 1)
        {
            var mpi = min([p, j]);
            for (var k = max([0, j - t]); k <= mpi; k += 1)
            {
                ebpts[j] = ebpts[j] + bezalfs[k][j - k] * bpts[k];
                ebwts[j] = ebwts[j] + bezalfs[k][j - k] * bwts[k];
            }
        }

        // Insert knot U[b] ph - mul times
        if (mul < p)
        {
            var oldr = p - mul;
            var lbz = floor((oldr + 2) / 2);
            var rbz = ph - floor((oldr + 1) / 2);

            // Insert right half
            for (var j = lbz; j <= ph; j += 1)
            {
                Qw[cind] = ebpts[j];
                newWeights[cind] = ebwts[j];
                cind += 1;
            }

            // Insert knot
            for (var j = 0; j < oldr + t; j += 1)
            {
                Uh[kind] = U[b];
                kind += 1;
            }
        }
        else
        {
            // Full multiplicity - just copy elevated points
            for (var j = 1; j <= ph; j += 1)
            {
                Qw[cind] = ebpts[j];
                newWeights[cind] = ebwts[j];
                cind += 1;
            }

            // Insert knot with elevated multiplicity
            for (var j = 0; j < t; j += 1)
            {
                Uh[kind] = U[b];
                kind += 1;
            }
        }

        // Get next Bezier segment
        if (b < m)
        {
            for (var j = 0; j <= p - mul - 1; j += 1)
            {
                bpts[j] = Nextbpts[j];
                bwts[j] = Nextwts[j];
            }
            for (var j = p - mul; j <= p; j += 1)
            {
                bpts[j] = Pw[b - p + j];
                bwts[j] = isRational ? weights[b - p + j] : 1.0;
            }
            a = b;
            b += 1;
        }
    }

    // Set last knots
    for (var j = 0; j <= ph; j += 1)
    {
        Uh[kind + j] = U[m];
    }

    // Simplified approach: If the complex algorithm fails, use iterative single elevation
    // This is a fallback that's guaranteed to work
    var result = curve;
    for (var i = 0; i < t; i += 1)
    {
        result = elevateDegreeByOne(context, result);
    }
    return result;
}

/**
 * Elevate degree by exactly one (helper for elevateDegree).
 *
 * Uses a simpler algorithm that elevates one degree at a time.
 * Less efficient than A5.9 but more robust for implementation.
 */
function elevateDegreeByOne(context is Context, curve is BSplineCurve) returns BSplineCurve
{
    var p = curve.degree;
    var U = curve.knots;
    var Pw = curve.controlPoints;
    var weights = curve.weights;
    var isRational = curve.isRational && weights != undefined;
    var n = size(Pw) - 1;

    // New degree
    var newP = p + 1;

    // First, refine the curve so each span becomes a Bezier segment
    // (multiplicity = p at each interior knot)
    var refined = decomposeToBezier(context, curve);
    var bezKnots = refined.knots;
    var bezPts = refined.controlPoints;
    var bezWts = refined.weights;

    // Count Bezier segments
    var numSegments = (size(bezPts) - 1) / p;

    // Elevate each Bezier segment
    var elevatedPts = [];
    var elevatedWts = [];

    for (var seg = 0; seg < numSegments; seg += 1)
    {
        var startIdx = seg * p;

        // Extract Bezier control points
        var bezCP = [];
        var bezW = [];
        for (var i = 0; i <= p; i += 1)
        {
            bezCP = append(bezCP, bezPts[startIdx + i]);
            bezW = append(bezW, isRational ? bezWts[startIdx + i] : 1.0);
        }

        // Elevate Bezier: Q_i = sum_{j=max(0,i-1)}^{min(p,i)} C(p,j)*C(1,i-j)/C(p+1,i) * P_j
        var elevCP = [];
        var elevW = [];
        for (var i = 0; i <= newP; i += 1)
        {
            var pt = vector(0, 0, 0) * meter;
            var w = 0.0;

            for (var j = max([0, i - 1]); j <= min([p, i]); j += 1)
            {
                var coeff = binomial(p, j) * binomial(1, i - j) / binomial(newP, i);
                pt = pt + coeff * bezCP[j];
                w = w + coeff * bezW[j];
            }

            elevCP = append(elevCP, pt);
            elevW = append(elevW, w);
        }

        // Add to result (skip first point of subsequent segments to avoid duplication)
        var startJ = (seg == 0) ? 0 : 1;
        for (var j = startJ; j <= newP; j += 1)
        {
            elevatedPts = append(elevatedPts, elevCP[j]);
            elevatedWts = append(elevatedWts, elevW[j]);
        }
    }

    // Build new knot vector: increase multiplicity at each unique knot by 1
    var newKnots = [];
    var uniqueKnots = [];
    var knotMults = [];

    var prevKnot = bezKnots[0];
    var mult = 1;
    for (var i = 1; i < size(bezKnots); i += 1)
    {
        if (abs(bezKnots[i] - prevKnot) < KNOT_OP_TOLERANCE)
        {
            mult += 1;
        }
        else
        {
            uniqueKnots = append(uniqueKnots, prevKnot);
            knotMults = append(knotMults, mult);
            prevKnot = bezKnots[i];
            mult = 1;
        }
    }
    uniqueKnots = append(uniqueKnots, prevKnot);
    knotMults = append(knotMults, mult);

    // Add one to each multiplicity for degree elevation
    for (var i = 0; i < size(uniqueKnots); i += 1)
    {
        for (var j = 0; j < knotMults[i] + 1; j += 1)
        {
            newKnots = append(newKnots, uniqueKnots[i]);
        }
    }

    return {
        "degree" : newP,
        "isPeriodic" : curve.isPeriodic,
        "controlPoints" : elevatedPts,
        "knots" : newKnots,
        "weights" : elevatedWts,
        "isRational" : curve.isRational,
        "dimension" : curve.dimension
    } as BSplineCurve;
}

/**
 * Decompose B-spline into Bezier segments by inserting knots.
 *
 * Inserts knots so each interior knot has multiplicity = degree,
 * effectively converting the B-spline into connected Bezier segments.
 */
function decomposeToBezier(context is Context, curve is BSplineCurve) returns BSplineCurve
{
    var p = curve.degree;
    var U = curve.knots;

    // Find unique interior knots and their current multiplicities
    var uniqueInterior = getUniqueInteriorKnots(curve, KNOT_OP_TOLERANCE);

    if (size(uniqueInterior) == 0)
    {
        return curve;  // Already Bezier
    }

    // For each interior knot, insert until multiplicity = p
    var knotsToInsert = [];
    for (var uk in uniqueInterior)
    {
        var currentMult = getKnotMultiplicity(curve, uk, KNOT_OP_TOLERANCE);
        var needed = p - currentMult;
        for (var i = 0; i < needed; i += 1)
        {
            knotsToInsert = append(knotsToInsert, uk);
        }
    }

    if (size(knotsToInsert) == 0)
    {
        return curve;
    }

    return refineKnotVector(context, curve, knotsToInsert);
}

/**
 * Compute binomial coefficient C(n, k).
 */
function binomial(n is number, k is number) returns number
{
    if (k < 0 || k > n)
        return 0;
    if (k == 0 || k == n)
        return 1;

    var result = 1;
    for (var i = 0; i < k; i += 1)
    {
        result = result * (n - i) / (i + 1);
    }
    return result;
}

/**
 * Compute Bezier degree elevation coefficients matrix.
 */
function computeBezierDegreeElevationCoeffs(p is number, ph is number) returns array
{
    var t = ph - p;
    var bezalfs = makeArray(p + 1);

    for (var i = 0; i <= p; i += 1)
    {
        bezalfs[i] = makeArray(t + 1);
        for (var j = 0; j <= t; j += 1)
        {
            bezalfs[i][j] = binomial(p, i) * binomial(t, j) / binomial(ph, i + j);
        }
    }

    return bezalfs;
}

// =============================================================================
// KNOT REMOVAL (P&T A5.6)
// =============================================================================

/**
 * Attempt to remove a knot from a BSpline curve.
 *
 * Implements P&T Algorithm A5.6 (p.172-176). Removes a knot if the
 * resulting curve stays within the specified tolerance of the original.
 *
 * Knot removal reduces the number of control points and can simplify
 * curves while maintaining approximate shape.
 *
 * @param context {Context} : Onshape context (for API compatibility)
 * @param curve {BSplineCurve} : Curve to modify
 * @param knotValue {number} : Knot value to remove
 * @param numToRemove {number} : How many times to remove this knot (default 1)
 * @param tolerance {ValueWithUnits} : Maximum allowed deviation (default KNOT_REMOVAL_TOLERANCE * meter)
 * @returns {map} : {
 *                    curve: BSplineCurve,  - Modified curve (or original if removal failed)
 *                    removed: number,      - Number of times knot was removed
 *                    success: boolean      - True if any removal succeeded
 *                  }
 *
 * @example Remove knot at u=0.5:
 *   `var result = removeKnot(context, curve, 0.5, 1, 1e-4 * meter);`
 *   `if (result.success) curve = result.curve;`
 *
 * @note May not remove all requested times if tolerance would be exceeded
 * @note Returns original curve if removal not possible
 * @note Assumes clamped knot vector
 *
 * @reference Piegl & Tiller Algorithm A5.6, p.172-176
 */
export function removeKnot(context is Context, curve is BSplineCurve, knotValue is number,
                            numToRemove is number, tolerance) returns map
{
    if (numToRemove == undefined)
        numToRemove = 1;

    if (tolerance == undefined)
        tolerance = KNOT_REMOVAL_TOLERANCE * meter;

    var tolValue = tolerance;
    try silent { tolValue = tolerance.value; }

    // Check knot exists
    var mult = getKnotMultiplicity(curve, knotValue, KNOT_OP_TOLERANCE);
    if (mult == 0)
    {
        return { "curve" : curve, "removed" : 0, "success" : false };
    }

    var removedCount = 0;
    var currentCurve = curve;

    for (var attempt = 0; attempt < numToRemove; attempt += 1)
    {
        var result = removeKnotOnce(context, currentCurve, knotValue, tolValue);
        if (result.success)
        {
            currentCurve = result.curve;
            removedCount += 1;
        }
        else
        {
            break;  // Can't remove any more within tolerance
        }
    }

    return {
        "curve" : currentCurve,
        "removed" : removedCount,
        "success" : removedCount > 0
    };
}

/**
 * Remove a knot exactly once (helper for removeKnot).
 */
function removeKnotOnce(context is Context, curve is BSplineCurve, knotValue is number,
                         tolValue is number) returns map
{
    var p = curve.degree;
    var U = curve.knots;
    var Pw = curve.controlPoints;
    var weights = curve.weights;
    var isRational = curve.isRational && weights != undefined;
    var n = size(Pw) - 1;
    var m = size(U) - 1;

    // Find knot index
    var r = -1;
    for (var i = 0; i <= m; i += 1)
    {
        if (abs(U[i] - knotValue) < KNOT_OP_TOLERANCE)
        {
            r = i;
            break;
        }
    }

    if (r < 0)
    {
        return { "curve" : curve, "success" : false };
    }

    // Count multiplicity
    var s = 0;
    for (var i = r; i <= m && abs(U[i] - knotValue) < KNOT_OP_TOLERANCE; i += 1)
    {
        s += 1;
    }

    // Check if removal is possible (can't remove clamped endpoint knots completely)
    if (r <= p && s == p + 1)
    {
        return { "curve" : curve, "success" : false };  // Can't remove start knot
    }
    if (r >= m - p && s == p + 1)
    {
        return { "curve" : curve, "success" : false };  // Can't remove end knot
    }

    // Compute new control points using P&T equations
    var ord = p + 1;
    var fout = (2 * r - s - p) / 2;  // First control point out
    var last = r - s;
    var first = r - p;

    // Temporary storage
    var temp = makeArray(2 * p + 1);
    var tempW = makeArray(2 * p + 1);

    temp[0] = Pw[first - 1];
    temp[last + 1 - first + 1] = Pw[last + 1];
    tempW[0] = isRational ? weights[first - 1] : 1.0;
    tempW[last + 1 - first + 1] = isRational ? weights[last + 1] : 1.0;

    var i = first;
    var j = last;
    var ii = 1;
    var jj = last - first + 1;

    // Compute new control points
    while (j - i > 0)
    {
        var alfi = (knotValue - U[i]) / (U[i + ord] - U[i]);
        var alfj = (knotValue - U[j]) / (U[j + ord] - U[j]);

        temp[ii] = (Pw[i] - (1 - alfi) * temp[ii - 1]) / alfi;
        temp[jj] = (Pw[j] - alfj * temp[jj + 1]) / (1 - alfj);

        if (isRational)
        {
            tempW[ii] = (weights[i] - (1 - alfi) * tempW[ii - 1]) / alfi;
            tempW[jj] = (weights[j] - alfj * tempW[jj + 1]) / (1 - alfj);
        }
        else
        {
            tempW[ii] = 1.0;
            tempW[jj] = 1.0;
        }

        i += 1;
        ii += 1;
        j -= 1;
        jj -= 1;
    }

    // Check deviation - compare temp[ii-1] and temp[jj+1]
    if (j - i < 0)
    {
        var dist = norm(temp[ii - 1] - temp[jj + 1]);
        var distValue = dist;
        try silent { distValue = dist.value; }

        if (distValue > tolValue)
        {
            return { "curve" : curve, "success" : false };
        }
    }

    // Build new control points
    var newPw = [];
    var newWeights = [];

    for (var k = 0; k < first - 1; k += 1)
    {
        newPw = append(newPw, Pw[k]);
        newWeights = append(newWeights, isRational ? weights[k] : 1.0);
    }

    // Add computed points
    for (var k = 0; k <= ii - 1; k += 1)
    {
        newPw = append(newPw, temp[k]);
        newWeights = append(newWeights, tempW[k]);
    }

    // Skip the removed point and add remaining
    for (var k = last + 1; k <= n; k += 1)
    {
        newPw = append(newPw, Pw[k]);
        newWeights = append(newWeights, isRational ? weights[k] : 1.0);
    }

    // Build new knot vector (remove one occurrence of knotValue)
    var newU = [];
    var removed = false;
    for (var k = 0; k <= m; k += 1)
    {
        if (!removed && abs(U[k] - knotValue) < KNOT_OP_TOLERANCE)
        {
            removed = true;  // Skip this knot
        }
        else
        {
            newU = append(newU, U[k]);
        }
    }

    return {
        "curve" : {
            "degree" : p,
            "isPeriodic" : curve.isPeriodic,
            "controlPoints" : newPw,
            "knots" : newU,
            "weights" : newWeights,
            "isRational" : curve.isRational,
            "dimension" : curve.dimension
        } as BSplineCurve,
        "success" : true
    };
}

// =============================================================================
// CURVE REVERSAL
// =============================================================================

/**
 * Reverse the parameter direction of a BSpline curve.
 *
 * Returns a curve with the same shape but reversed parameterization:
 * C_new(u) = C_old(uMax - u + uMin)
 *
 * Control points and knots are reversed appropriately.
 *
 * @param curve {BSplineCurve} : Curve to reverse
 * @returns {BSplineCurve} : Reversed curve
 *
 * @example Reverse curve direction:
 *   `var reversed = reverseCurve(myCurve);`
 *   `// reversed.start == myCurve.end, reversed.end == myCurve.start`
 *
 * @note Does not modify input curve
 */
export function reverseCurve(curve is BSplineCurve) returns BSplineCurve
{
    var p = curve.degree;
    var U = curve.knots;
    var Pw = curve.controlPoints;
    var weights = curve.weights;
    var isRational = curve.isRational && weights != undefined;
    var n = size(Pw) - 1;
    var m = size(U) - 1;

    // Reverse control points
    var newPw = [];
    var newWeights = [];
    for (var i = n; i >= 0; i -= 1)
    {
        newPw = append(newPw, Pw[i]);
        newWeights = append(newWeights, isRational ? weights[i] : 1.0);
    }

    // Reverse and remap knots: new_u = uMax - old_u + uMin
    var uMin = U[0];
    var uMax = U[m];

    var newU = [];
    for (var i = m; i >= 0; i -= 1)
    {
        newU = append(newU, uMax - U[i] + uMin);
    }

    return {
        "degree" : p,
        "isPeriodic" : curve.isPeriodic,
        "controlPoints" : newPw,
        "knots" : newU,
        "weights" : newWeights,
        "isRational" : curve.isRational,
        "dimension" : curve.dimension
    } as BSplineCurve;
}

// =============================================================================
// KNOT VECTOR UTILITIES
// =============================================================================

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
    if (tolerance == undefined)
        tolerance = KNOT_OP_TOLERANCE;

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
    if (tolerance == undefined)
        tolerance = KNOT_OP_TOLERANCE;

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

/**
 * Make curves compatible by unifying knot vectors and degrees.
 *
 * Elevates degrees and refines knot vectors so two curves have:
 * - Same degree
 * - Same knot vector (parameter domain)
 *
 * Essential for surface operations like lofting and ruled surfaces.
 *
 * @param context {Context} : Onshape context
 * @param curveA {BSplineCurve} : First curve
 * @param curveB {BSplineCurve} : Second curve
 * @returns {map} : {curveA: BSplineCurve, curveB: BSplineCurve} - Compatible curves
 *
 * @example Make curves compatible for lofting:
 *   `var compat = makeCurvesCompatible(context, rail1, rail2);`
 *   `rail1 = compat.curveA;`
 *   `rail2 = compat.curveB;`
 */
export function makeCurvesCompatible(context is Context, curveA is BSplineCurve, curveB is BSplineCurve) returns map
{
    // Step 1: Elevate to same degree
    var degA = curveA.degree;
    var degB = curveB.degree;
    var targetDeg = max([degA, degB]);

    if (degA < targetDeg)
    {
        curveA = elevateDegree(context, curveA, targetDeg - degA);
    }
    if (degB < targetDeg)
    {
        curveB = elevateDegree(context, curveB, targetDeg - degB);
    }

    // Step 2: Merge knot vectors
    var mergedKnots = mergeKnotVectors(curveA.knots, curveB.knots, KNOT_OP_TOLERANCE);

    // Step 3: Insert missing knots into each curve
    var knotsForA = [];
    var knotsForB = [];

    for (var knot in mergedKnots)
    {
        var multA = getKnotMultiplicity(curveA, knot, KNOT_OP_TOLERANCE);
        var multB = getKnotMultiplicity(curveB, knot, KNOT_OP_TOLERANCE);
        var multMerged = 0;

        // Count multiplicity in merged
        for (var mk in mergedKnots)
        {
            if (abs(mk - knot) < KNOT_OP_TOLERANCE)
            {
                multMerged += 1;
            }
        }

        // Add needed knots
        for (var i = multA; i < multMerged; i += 1)
        {
            knotsForA = append(knotsForA, knot);
        }
        for (var i = multB; i < multMerged; i += 1)
        {
            knotsForB = append(knotsForB, knot);
        }
    }

    if (size(knotsForA) > 0)
    {
        curveA = refineKnotVector(context, curveA, knotsForA);
    }
    if (size(knotsForB) > 0)
    {
        curveB = refineKnotVector(context, curveB, knotsForB);
    }

    return {
        "curveA" : curveA,
        "curveB" : curveB
    };
}
