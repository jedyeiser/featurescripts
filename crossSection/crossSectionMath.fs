FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * CROSS SECTION MATH - Pure Unitless Mathematics
 * ===============================================
 *
 * All functions operate on plain number arrays [x, y, z] -- no ValueWithUnits,
 * no context, no kernel calls. This enables the 6.4x speedup from unitless
 * math and lets us replace evaluateSpline (3.2x win) with our own de Boor.
 *
 * Units are stripped at input boundaries (ev* calls) and re-applied at output
 * boundaries (opCreate*, approximateSpline, debug viz, etc.).
 *
 * Conventions:
 *   - 3D points/vectors: [x, y, z] (array of 3 numbers)
 *   - Planes: { origin: [x,y,z], normal: [x,y,z] }
 *   - B-spline curves: { controlPoints: [[x,y,z],...], knots: [...], degree: n }
 *   - All tolerances are in meters (unitless, but representing meter scale)
 */

// =============================================================================
// CONSTANTS
// =============================================================================

export const MATH_TOL = 1e-10;          // General numerical tolerance
export const GEOM_TOL = 1e-6;           // Geometry tolerance (~1 micron in meters)
export const TANGENT_THRESHOLD = 0.05;  // ~3 degrees from parallel for tangent filtering
export const NEWTON_MAX_ITER = 10;
export const NEWTON_TOL = 1e-8;         // Newton convergence tolerance (meters)

// =============================================================================
// UNITLESS VECTOR OPERATIONS
// =============================================================================
// These replace norm(), dot(), normalize() which require ValueWithUnits.

export function dotU(a is array, b is array) returns number
{
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

export function normSqU(v is array) returns number
{
    return v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
}

export function normU(v is array) returns number
{
    return sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
}

export function normalizeU(v is array) returns array
{
    var len = normU(v);
    if (len < MATH_TOL)
        return [0, 0, 0];
    return [v[0] / len, v[1] / len, v[2] / len];
}

export function subtractU(a is array, b is array) returns array
{
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
}

export function addU(a is array, b is array) returns array
{
    return [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
}

export function scaleU(v is array, s is number) returns array
{
    return [v[0] * s, v[1] * s, v[2] * s];
}

export function lerpU(a is array, b is array, t is number) returns array
{
    return [a[0] + t * (b[0] - a[0]),
            a[1] + t * (b[1] - a[1]),
            a[2] + t * (b[2] - a[2])];
}

/** Average of an array of 3D points. */
export function averageU(points is array) returns array
{
    var n = size(points);
    if (n == 0)
        return [0, 0, 0];
    var sum = [0, 0, 0];
    for (var p in points)
    {
        sum = addU(sum, p);
    }
    return scaleU(sum, 1.0 / n);
}

// =============================================================================
// GEOMETRY VALIDATION UTILITIES
// =============================================================================
// Used to validate B-spline control points before attempting curve creation,
// catching degenerate cases that would cause opCreateBSplineCurve to fail.

/**
 * Cross product of two 3D vectors (unitless).
 * Returns the vector perpendicular to both inputs.
 */
export function crossU(a is array, b is array) returns array
{
    return [a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0]];
}

/**
 * Check if three points are colinear within a tolerance.
 *
 * Uses the parallelogram area test: points are colinear if the area of the
 * triangle they form is near-zero. This is equivalent to checking if the
 * cross product of (p1-p0) and (p2-p0) is near-zero.
 *
 * @param p0, p1, p2 : Three points as [x,y,z] arrays
 * @param tol : Area tolerance (squared length units, e.g., GEOM_TOL^2)
 * @returns : true if points are colinear
 */
export function arePointsColinear(p0 is array, p1 is array, p2 is array, tol is number) returns boolean
{
    var v1 = subtractU(p1, p0);
    var v2 = subtractU(p2, p0);
    var cross = crossU(v1, v2);
    var areaSq = normSqU(cross);
    return areaSq < tol;
}

/**
 * Check if all points in an array are colinear.
 *
 * Tests every triplet of points. If any triplet forms a non-zero triangle,
 * the points are NOT colinear.
 *
 * @param points : Array of [x,y,z] points
 * @param tol : Area tolerance (squared length units)
 * @returns : true if all points lie on a single line
 */
export function areAllPointsColinear(points is array, tol is number) returns boolean
{
    var n = size(points);
    if (n < 3)
        return true;  // 0-2 points are trivially colinear

    // Check every triplet against the first two points
    var p0 = points[0];
    var p1 = points[1];

    for (var i = 2; i < n; i += 1)
    {
        if (!arePointsColinear(p0, p1, points[i], tol))
            return false;
    }

    return true;
}

/**
 * Validate control points before attempting B-spline curve creation.
 *
 * Checks for common degenerate cases that cause opCreateBSplineCurve to fail:
 * - Insufficient points for the degree
 * - Zero-length curve (coincident endpoints)
 * - Colinear points when degree >= 2
 *
 * @param points : Array of [x,y,z] control points (unitless)
 * @param degree : Desired B-spline degree
 * @returns : Map with keys:
 *   - success (boolean): true if points are valid for this degree
 *   - reason (string): Human-readable explanation if invalid
 *   - degenerateCase (string): Classification ("UNDERCONSTRAINED", "ZERO_LENGTH", "COLINEAR", "OK")
 */
export function validateBSplineControlPoints(points is array, degree is number) returns map
{
    var n = size(points);

    // Check 1: Minimum point count
    if (n < 2)
    {
        return {
            "success" : false,
            "reason" : "Need at least 2 points, got " ~ n,
            "degenerateCase" : "UNDERCONSTRAINED"
        };
    }

    // Check 2: Valid degree
    if (degree < 1)
    {
        return {
            "success" : false,
            "reason" : "Degree must be >= 1, got " ~ degree,
            "degenerateCase" : "UNDERCONSTRAINED"
        };
    }

    // Check 3: Sufficient points for degree (need n > degree)
    if (n <= degree)
    {
        return {
            "success" : false,
            "reason" : "Insufficient points for degree: " ~ n ~ " points, degree " ~ degree,
            "degenerateCase" : "UNDERCONSTRAINED"
        };
    }

    // Check 4: Non-zero length (distinct endpoints)
    var startToEnd = subtractU(points[n - 1], points[0]);
    if (normU(startToEnd) < GEOM_TOL)
    {
        return {
            "success" : false,
            "reason" : "Zero-length curve (coincident endpoints)",
            "degenerateCase" : "ZERO_LENGTH"
        };
    }

    // Check 5: For degree >= 2, points must not all be colinear
    // (Linear curves are fine with colinear points)
    if (degree >= 2)
    {
        var colTol = GEOM_TOL * GEOM_TOL;  // Area tolerance: ~1e-12 m²
        if (areAllPointsColinear(points, colTol))
        {
            return {
                "success" : false,
                "reason" : "All points colinear (cannot create degree " ~ degree ~ " curve)",
                "degenerateCase" : "COLINEAR"
            };
        }
    }

    // All checks passed
    return {
        "success" : true,
        "reason" : "Valid",
        "degenerateCase" : "OK"
    };
}

// =============================================================================
// UNIT CONVERSION AT BOUNDARIES
// =============================================================================
// These are the ONLY places units appear in this module.

/** Strip length units from a position Vector ? [x, y, z] in meters. */
export function vecToArr(v is Vector) returns array
{
    return [v[0] / meter, v[1] / meter, v[2] / meter];
}

/** Extract components from a dimensionless direction Vector ? [x, y, z]. */
export function dirToArr(v is Vector) returns array
{
    // Direction vectors (normals, tangents, axis directions) are unitless.
    // Just extract the numeric components -- no unit division needed.
    return [v[0], v[1], v[2]];
}

/** Convert [x, y, z] (meters) ? Vector with units. */
export function arrToVec(a is array) returns Vector
{
    return vector(a[0] * meter, a[1] * meter, a[2] * meter);
}

/** Strip units from BSplineCurve control points ? unitless curve map. */
export function stripCurveUnits(curve is BSplineCurve) returns map
{
    var cps = makeArray(size(curve.controlPoints));
    for (var i = 0; i < size(curve.controlPoints); i += 1)
    {
        cps[i] = vecToArr(curve.controlPoints[i]);
    }
    return {
        "controlPoints" : cps,
        "knots" : curve.knots,
        "degree" : curve.degree
    };
}

/** Strip units from a 2D grid of surface control points ? unitless grid. */
export function stripSurfaceCPUnits(cpGrid is array) returns array
{
    var result = makeArray(size(cpGrid));
    for (var i = 0; i < size(cpGrid); i += 1)
    {
        var row = makeArray(size(cpGrid[i]));
        for (var j = 0; j < size(cpGrid[i]); j += 1)
        {
            row[j] = vecToArr(cpGrid[i][j]);
        }
        result[i] = row;
    }
    return result;
}

// =============================================================================
// B-SPLINE EVALUATION: DE BOOR'S ALGORITHM
// =============================================================================
// Replaces evaluateSpline for all internal use (3.2x faster).

/**
 * Find the knot span index k such that knots[k] <= u < knots[k+1].
 *
 * For a clamped B-spline with n+1 control points and degree p,
 * the valid parameter range is [knots[p], knots[n+1]] and k ? [p, n].
 *
 * @param n : Number of control points minus 1
 * @param degree : B-spline degree
 * @param u : Parameter value
 * @param knots : Flat knot vector (size n + degree + 2)
 */
export function findKnotSpan(n is number, degree is number, u is number, knots is array) returns number
{
    // Special case: u at or beyond the end of the parameter range
    if (u >= knots[n + 1])
        return n;

    // Special case: u at or before the start
    if (u <= knots[degree])
        return degree;

    // Binary search for the span
    var low = degree;
    var high = n + 1;
    var mid = floor((low + high) / 2);

    while (u < knots[mid] || u >= knots[mid + 1])
    {
        if (u < knots[mid])
            high = mid;
        else
            low = mid;
        mid = floor((low + high) / 2);

        // Safety valve against infinite loop
        if (high - low <= 1)
        {
            mid = low;
            break;
        }
    }

    return mid;
}

/**
 * Evaluate a B-spline curve at parameter u using de Boor's algorithm.
 *
 * @param degree : Curve degree
 * @param knots : Flat knot vector
 * @param controlPoints : Array of [x,y,z] (unitless)
 * @param u : Parameter value
 * @returns : [x, y, z] point on curve
 */
export function deBoor(degree is number, knots is array, controlPoints is array, u is number) returns array
{
    var n = size(controlPoints) - 1;
    var k = findKnotSpan(n, degree, u, knots);

    // Copy the affected control points: P[k-p] through P[k]
    var d = makeArray(degree + 1);
    for (var j = 0; j <= degree; j += 1)
    {
        d[j] = controlPoints[k - degree + j];
    }

    // Triangular computation
    for (var r = 1; r <= degree; r += 1)
    {
        for (var j = degree; j >= r; j -= 1)
        {
            var left = k - degree + j;
            var denom = knots[left + degree - r + 1] - knots[left];

            if (abs(denom) < MATH_TOL)
            {
                // Degenerate knot interval -- keep existing point
                continue;
            }

            var alpha = (u - knots[left]) / denom;
            d[j] = lerpU(d[j - 1], d[j], alpha);
        }
    }

    return d[degree];
}

/**
 * Evaluate the first derivative of a B-spline at parameter u.
 *
 * Uses the identity: C'(t) is a degree-(p-1) B-spline with control points
 *   Q[i] = p * (P[i+1] - P[i]) / (knots[i+p+1] - knots[i+1])
 * and knot vector knots[1..m-1] (first and last knot removed).
 */
export function deBoorDerivative(degree is number, knots is array, controlPoints is array, u is number) returns array
{
    if (degree < 1)
        return [0, 0, 0];

    var n = size(controlPoints) - 1;

    // Build derivative control points
    var derivCPs = makeArray(n);
    for (var i = 0; i < n; i += 1)
    {
        var denom = knots[i + degree + 1] - knots[i + 1];
        if (abs(denom) < MATH_TOL)
        {
            derivCPs[i] = [0, 0, 0];
        }
        else
        {
            var diff = subtractU(controlPoints[i + 1], controlPoints[i]);
            derivCPs[i] = scaleU(diff, degree / denom);
        }
    }

    // Derivative knot vector: remove first and last knot
    var derivKnots = subArray(knots, 1, size(knots) - 1);

    return deBoor(degree - 1, derivKnots, derivCPs, u);
}

/**
 * Batch evaluate a B-spline at multiple parameters.
 * More efficient than calling deBoor in a loop when used with sorted params.
 */
export function deBoorBatch(degree is number, knots is array, controlPoints is array, params is array) returns array
{
    var results = makeArray(size(params));
    for (var i = 0; i < size(params); i += 1)
    {
        results[i] = deBoor(degree, knots, controlPoints, params[i]);
    }
    return results;
}

// =============================================================================
// KNOT UTILITIES
// =============================================================================

/**
 * Compute Greville abscissae -- approximate parameter locations for each CP.
 * Greville[i] = (1/p) * sum(knots[i+1] ... knots[i+p])
 *
 * These provide good initial guesses for Newton refinement when finding
 * plane-curve intersections via control polygon sign changes.
 */
export function grevilleAbscissae(knots is array, degree is number, numCPs is number) returns array
{
    var greville = makeArray(numCPs);
    for (var i = 0; i < numCPs; i += 1)
    {
        var sum = 0;
        for (var k = 1; k <= degree; k += 1)
        {
            sum += knots[i + k];
        }
        greville[i] = sum / degree;
    }
    return greville;
}

/**
 * Build a clamped B-spline knot vector from control points using arc-length
 * parameterization.
 *
 * The knot vector places interior knots proportional to accumulated chord
 * length, giving better parameterization for non-uniformly spaced points.
 *
 * @param points : Array of [x,y,z] control points
 * @param degree : Desired B-spline degree (will be clamped to size-1)
 * @returns : Flat knot vector
 */
export function arcLengthKnotVector(points is array, degree is number) returns array
{
    var n = size(points) - 1;  // n+1 control points
    var p = min(degree, n);     // Can't have degree > n

    if (n < 1)
        return [0, 1];

    // Compute chord-length parameters
    var dists = makeArray(n + 1);
    dists[0] = 0;
    var totalDist = 0;
    for (var i = 1; i <= n; i += 1)
    {
        totalDist += normU(subtractU(points[i], points[i - 1]));
        dists[i] = totalDist;
    }

    // Normalize to [0, 1]
    var params = makeArray(n + 1);
    if (totalDist > MATH_TOL)
    {
        for (var i = 0; i <= n; i += 1)
        {
            params[i] = dists[i] / totalDist;
        }
    }
    else
    {
        // Degenerate: all points coincident. Use uniform.
        for (var i = 0; i <= n; i += 1)
        {
            params[i] = (n > 0) ? i / n : 0;
        }
    }

    // Build clamped knot vector: n + p + 2 knots total
    var numKnots = n + p + 2;
    var knots = makeArray(numKnots);

    // First p+1 knots = 0
    for (var i = 0; i <= p; i += 1)
    {
        knots[i] = 0;
    }

    // Interior knots via averaging (de Boor's method)
    // knots[j+p] = (1/p) * sum(params[j] ... params[j+p-1])  for j = 1..n-p
    for (var j = 1; j <= n - p; j += 1)
    {
        var sum = 0;
        for (var i = j; i < j + p; i += 1)
        {
            sum += params[i];
        }
        knots[j + p] = sum / p;
    }

    // Last p+1 knots = 1
    for (var i = n + 1; i < numKnots; i += 1)
    {
        knots[i] = 1;
    }

    return knots;
}

/**
 * Get the parameter range [uMin, uMax] from a knot vector and degree.
 */
export function paramRange(knots is array, degree is number) returns map
{
    return {
        "uMin" : knots[degree],
        "uMax" : knots[size(knots) - degree - 1]
    };
}

// =============================================================================
// PLANE-BSPLINE CURVE INTERSECTION
// =============================================================================
// Strategy Appendix A: Greville abscissae + Newton refinement.

/**
 * Find all intersection points between a plane and a B-spline curve.
 *
 * Approach:
 * 1. Compute signed distance from each CP to the plane
 * 2. Use Greville abscissae as approximate parameter locations
 * 3. Walk CPs, detect sign changes or on-plane points
 * 4. Newton-Raphson refinement for each candidate
 * 5. Deduplicate results
 *
 * @param planeOrigin : [x,y,z] point on plane (unitless meters)
 * @param planeNormal : [x,y,z] unit normal of plane
 * @param controlPoints : Curve CPs (unitless)
 * @param knots : Curve knot vector
 * @param degree : Curve degree
 * @returns : Array of { point: [x,y,z], param: number }
 */
export function getPlaneBSplineIntersections(planeOrigin is array, planeNormal is array,
    controlPoints is array, knots is array, degree is number) returns array
{
    var n = size(controlPoints);
    if (n < 2)
        return [];

    // Step 1: Signed distance from each CP to plane
    var cpDists = makeArray(n);
    for (var i = 0; i < n; i += 1)
    {
        cpDists[i] = dotU(subtractU(controlPoints[i], planeOrigin), planeNormal);
    }

    // Step 2: Greville abscissae for approximate parameter locations
    var greville = grevilleAbscissae(knots, degree, n);

    // Step 3: Find candidate crossings (sign changes or on-plane CPs)
    var candidates = [];
    for (var i = 0; i < n - 1; i += 1)
    {
        if (abs(cpDists[i]) < NEWTON_TOL)
        {
            // CP is on the plane
            candidates = append(candidates, { "paramGuess" : greville[i], "isExact" : true });
        }
        else if (cpDists[i] * cpDists[i + 1] < 0)
        {
            // Sign change -- linear interpolation for initial guess
            var t = cpDists[i] / (cpDists[i] - cpDists[i + 1]);
            var paramGuess = (1 - t) * greville[i] + t * greville[i + 1];
            candidates = append(candidates, { "paramGuess" : paramGuess, "isExact" : false });
        }
    }
    // Check last CP
    if (abs(cpDists[n - 1]) < NEWTON_TOL)
    {
        candidates = append(candidates, { "paramGuess" : greville[n - 1], "isExact" : true });
    }

    // Step 4: Newton-Raphson refinement
    var range = paramRange(knots, degree);
    var results = [];

    for (var c in candidates)
    {
        var param = c.paramGuess;

        if (!c.isExact)
        {
            for (var iter = 0; iter < NEWTON_MAX_ITER; iter += 1)
            {
                var pos = deBoor(degree, knots, controlPoints, param);
                var deriv = deBoorDerivative(degree, knots, controlPoints, param);

                var f = dotU(subtractU(pos, planeOrigin), planeNormal);
                var df = dotU(deriv, planeNormal);

                if (abs(f) < NEWTON_TOL)
                    break;
                if (abs(df) < MATH_TOL)
                    break;  // Tangent to plane -- can't refine further

                param -= f / df;

                // Clamp to valid parameter range
                param = clamp(param, range.uMin, range.uMax);
            }
        }

        var finalPoint = deBoor(degree, knots, controlPoints, param);

        // Verify convergence: point must be near the plane
        if (abs(dotU(subtractU(finalPoint, planeOrigin), planeNormal)) < GEOM_TOL)
        {
            results = append(results, { "point" : finalPoint, "param" : param });
        }
    }

    // Step 5: Deduplicate (adjacent sign changes can converge to same root)
    return deduplicateIntersections(results);
}

/**
 * Remove near-duplicate intersection results by parameter proximity.
 */
function deduplicateIntersections(results is array) returns array
{
    var deduped = [];
    for (var r in results)
    {
        var isDup = false;
        for (var d in deduped)
        {
            if (abs(r.param - d.param) < NEWTON_TOL)
            {
                isDup = true;
                break;
            }
        }
        if (!isDup)
            deduped = append(deduped, r);
    }
    return deduped;
}

// =============================================================================
// PARAMETER ESTIMATION (CLOSEST POINT ON CURVE)
// =============================================================================
// Used in Step 4 to project edge intersection points onto the approximate
// intersection spline, producing fit parameters for span formation.

/**
 * Project a point onto a polyline defined by an ordered array of points.
 * Returns a parameter in [0, 1] where 0 = first point, 1 = last point.
 *
 * This is the fast alternative to estimateParamOnCurve -- no deBoor calls,
 * just linear segment projection. Good enough for sorting edge intersections
 * and selecting interior CPs, which is all we need.
 *
 * @param point : [x,y,z] query point
 * @param polyline : Array of [x,y,z] points (ordered)
 * @returns : Parameter in [0, 1]
 */
export function projectOntoPolyline(point is array, polyline is array) returns number
{
    var n = size(polyline);
    if (n < 2)
        return 0;

    // Compute cumulative arc lengths for parameterization
    var arcLengths = makeArray(n);
    arcLengths[0] = 0;
    for (var i = 1; i < n; i += 1)
    {
        arcLengths[i] = arcLengths[i - 1] + normU(subtractU(polyline[i], polyline[i - 1]));
    }
    var totalLen = arcLengths[n - 1];
    if (totalLen < MATH_TOL)
        return 0;

    // Find closest segment and project onto it
    var bestParam = 0;
    var bestDistSq = 1e30;

    for (var i = 0; i < n - 1; i += 1)
    {
        var seg = subtractU(polyline[i + 1], polyline[i]);
        var segLenSq = normSqU(seg);
        var diff = subtractU(point, polyline[i]);

        // Parameter along this segment [0, 1]
        var t = 0;
        if (segLenSq > MATH_TOL * MATH_TOL)
        {
            t = clamp(dotU(diff, seg) / segLenSq, 0, 1);
        }

        // Distance from point to projected position on segment
        var proj = addU(polyline[i], scaleU(seg, t));
        var distSq = normSqU(subtractU(point, proj));

        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            // Global parameter: arc length to segment start + fraction of segment
            var segLen = arcLengths[i + 1] - arcLengths[i];
            bestParam = (arcLengths[i] + t * segLen) / totalLen;
        }
    }

    return clamp(bestParam, 0, 1);
}

/**
 * Estimate the parameter of the closest point on a B-spline to a query point.
 *
 * Two-phase approach:
 * 1. Coarse search: sample the curve uniformly, find closest sample
 * 2. Newton refinement: minimize ||C(t) - P||^2 using the foot-point formula
 *
 * The foot-point Newton iteration solves (C(t) - P) * C'(t) = 0:
 *   t_{n+1} = t_n - [(C - P) * C'] / [C' * C' + (C - P) * C'']
 *
 * Since computing C'' adds complexity, we use a simplified update:
 *   t_{n+1} = t_n - [(C - P) * C'] / [C' * C']
 * which still converges when the initial guess is close (which it is
 * after the coarse search).
 *
 * @param point : [x,y,z] query point
 * @param controlPoints : Curve CPs (unitless)
 * @param knots : Curve knot vector
 * @param degree : Curve degree
 * @returns : Parameter value in [uMin, uMax]
 */
export function estimateParamOnCurve(point is array, controlPoints is array,
    knots is array, degree is number) returns number
{
    var range = paramRange(knots, degree);
    var n = size(controlPoints);

    // Phase 1: Coarse search at roughly 2x CP density
    var numSamples = max(2 * n, 10);
    var bestParam = range.uMin;
    var bestDistSq = 1e30;

    for (var i = 0; i < numSamples; i += 1)
    {
        var t = range.uMin + (range.uMax - range.uMin) * i / (numSamples - 1);
        var pos = deBoor(degree, knots, controlPoints, t);
        var distSq = normSqU(subtractU(pos, point));

        if (distSq < bestDistSq)
        {
            bestDistSq = distSq;
            bestParam = t;
        }
    }

    // Phase 2: Newton refinement (simplified foot-point iteration)
    var param = bestParam;
    for (var iter = 0; iter < NEWTON_MAX_ITER; iter += 1)
    {
        var pos = deBoor(degree, knots, controlPoints, param);
        var tang = deBoorDerivative(degree, knots, controlPoints, param);

        var diff = subtractU(pos, point);
        var f = dotU(diff, tang);           // (C - P) * C'
        var df = dotU(tang, tang);           // C' * C' (simplified denominator)

        if (abs(f) < MATH_TOL || df < MATH_TOL)
            break;

        param -= f / df;
        param = clamp(param, range.uMin, range.uMax);
    }

    return param;
}

// =============================================================================
// SURFACE INTERSECTION HELPERS
// =============================================================================
// These power the per-face-per-plane intersection walk (Strategy Steps 1-5).

/**
 * Compute a bounding box from a 2D grid of surface control points.
 * Returns { min: [x,y,z], max: [x,y,z] }.
 */
export function computeSurfaceBbox(cpGrid is array) returns map
{
    var first = cpGrid[0][0];
    var bboxMin = [first[0], first[1], first[2]];
    var bboxMax = [first[0], first[1], first[2]];

    for (var row in cpGrid)
    {
        for (var pt in row)
        {
            for (var d = 0; d < 3; d += 1)
            {
                if (pt[d] < bboxMin[d]) bboxMin[d] = pt[d];
                if (pt[d] > bboxMax[d]) bboxMax[d] = pt[d];
            }
        }
    }

    return { "min" : bboxMin, "max" : bboxMax };
}

/**
 * Fast bbox-plane rejection test.
 *
 * Projects the bbox extent onto the plane normal. If the center distance
 * exceeds the projected half-extent, the bbox (and thus the face) can't
 * intersect the plane.
 *
 * @returns true if the bbox COULD intersect the plane (conservative)
 */
export function bboxIntersectsPlane(bbox is map, planeOrigin is array, planeNormal is array) returns boolean
{
    var halfExtent = subtractU(bbox.max, bbox.min);
    var radius = 0.5 * (abs(halfExtent[0] * planeNormal[0])
                      + abs(halfExtent[1] * planeNormal[1])
                      + abs(halfExtent[2] * planeNormal[2]));

    var center = scaleU(addU(bbox.min, bbox.max), 0.5);
    var centerDist = dotU(subtractU(center, planeOrigin), planeNormal);

    return abs(centerDist) <= radius + GEOM_TOL;
}

/**
 * Compute signed distance from every surface CP to a plane.
 * Preserves the [i][j] grid shape for iso-curve walking.
 */
export function computeDistanceGrid(cpGrid is array, planeOrigin is array, planeNormal is array) returns array
{
    var numU = size(cpGrid);
    var distGrid = makeArray(numU);
    for (var i = 0; i < numU; i += 1)
    {
        var numV = size(cpGrid[i]);
        var row = makeArray(numV);
        for (var j = 0; j < numV; j += 1)
        {
            row[j] = dotU(subtractU(cpGrid[i][j], planeOrigin), planeNormal);
        }
        distGrid[i] = row;
    }
    return distGrid;
}

/**
 * Determine which parameter direction's iso-curves best "pierce" the plane.
 *
 * Computes average direction of u-curves (rows) and v-curves (columns),
 * then picks whichever aligns more with the plane normal.
 *
 * @returns true for USE_U, false for USE_V
 */
export function determinePiercingDirection(cpGrid is array, planeNormal is array) returns boolean
{
    var numU = size(cpGrid);
    var numV = size(cpGrid[0]);

    // Average direction of u-curves: for each row, direction from first to last CP
    var avgU = [0, 0, 0];
    var countU = 0;
    for (var i = 0; i < numU; i += 1)
    {
        var dir = subtractU(cpGrid[i][numV - 1], cpGrid[i][0]);
        if (normSqU(dir) > MATH_TOL * MATH_TOL)
        {
            avgU = addU(avgU, normalizeU(dir));
            countU += 1;
        }
    }
    if (countU > 0)
        avgU = normalizeU(avgU);

    // Average direction of v-curves: for each column, direction from first to last CP
    var avgV = [0, 0, 0];
    var countV = 0;
    for (var j = 0; j < numV; j += 1)
    {
        var dir = subtractU(cpGrid[numU - 1][j], cpGrid[0][j]);
        if (normSqU(dir) > MATH_TOL * MATH_TOL)
        {
            avgV = addU(avgV, normalizeU(dir));
            countV += 1;
        }
    }
    if (countV > 0)
        avgV = normalizeU(avgV);

    // avgU is actually the V-direction (how surface moves within rows).
    // avgV is actually the U-direction (how surface moves within columns).
    // When the V-direction aligns with the normal, iso-u-curves (constant u,
    // varying v) pierce the plane. walkIsoCurves with useU=false walks those.
    return abs(dotU(planeNormal, avgU)) < abs(dotU(planeNormal, avgV));
}

/**
 * Walk iso-curves in the piercing direction to find approximate intersection
 * control points where the surface crosses the plane.
 *
 * For each iso-curve, finds the first sign change in the distance grid and
 * linearly interpolates between bracketing CPs. This yields one point per
 * iso-curve, naturally ordered, with no deduplication needed.
 *
 * @param cpGrid : Surface control point grid (unitless)
 * @param distGrid : Signed distances to plane (same shape as cpGrid)
 * @param useU : true to walk u-curves (piercing direction is U)
 * @returns : Ordered array of [x,y,z] approximate intersection control points
 */
export function walkIsoCurves(cpGrid is array, distGrid is array, useU is boolean) returns array
{
    var numU = size(cpGrid);
    var numV = size(cpGrid[0]);
    var ctrlPoints = [];

    if (useU)
    {
        // Piercing direction is U: walk each v-index (j), scanning across u-indices (i)
        for (var j = 0; j < numV; j += 1)
        {
            var found = false;
            for (var i = 0; i < numU - 1; i += 1)
            {
                var d0 = distGrid[i][j];
                var d1 = distGrid[i + 1][j];

                if (abs(d0) < GEOM_TOL)
                {
                    ctrlPoints = append(ctrlPoints, cpGrid[i][j]);
                    found = true;
                    break;
                }
                if (d0 * d1 < 0)
                {
                    var t = d0 / (d0 - d1);
                    ctrlPoints = append(ctrlPoints, lerpU(cpGrid[i][j], cpGrid[i + 1][j], t));
                    found = true;
                    break;
                }
            }
            // Check last CP in this iso-curve
            if (!found && abs(distGrid[numU - 1][j]) < GEOM_TOL)
            {
                ctrlPoints = append(ctrlPoints, cpGrid[numU - 1][j]);
            }
        }
    }
    else
    {
        // Piercing direction is V: walk each u-index (i), scanning across v-indices (j)
        for (var i = 0; i < numU; i += 1)
        {
            var found = false;
            for (var j = 0; j < numV - 1; j += 1)
            {
                var d0 = distGrid[i][j];
                var d1 = distGrid[i][j + 1];

                if (abs(d0) < GEOM_TOL)
                {
                    ctrlPoints = append(ctrlPoints, cpGrid[i][j]);
                    found = true;
                    break;
                }
                if (d0 * d1 < 0)
                {
                    var t = d0 / (d0 - d1);
                    ctrlPoints = append(ctrlPoints, lerpU(cpGrid[i][j], cpGrid[i][j + 1], t));
                    found = true;
                    break;
                }
            }
            if (!found && abs(distGrid[i][numV - 1]) < GEOM_TOL)
            {
                ctrlPoints = append(ctrlPoints, cpGrid[i][numV - 1]);
            }
        }
    }

    return ctrlPoints;
}

/**
 * Quick check: does an edge's control polygon cross the plane?
 *
 * Returns true if CPs span both sides of (or touch) the plane,
 * meaning the edge could intersect it.
 */
export function edgeCrossesPlane(edgeCPs is array, planeOrigin is array, planeNormal is array) returns boolean
{
    var hasPositive = false;
    var hasNegative = false;
    var hasOnPlane = false;

    for (var cp in edgeCPs)
    {
        var d = dotU(subtractU(cp, planeOrigin), planeNormal);
        if (abs(d) < GEOM_TOL)
            hasOnPlane = true;
        else if (d > 0)
            hasPositive = true;
        else
            hasNegative = true;
    }

    // Needs at least one side + (other side or on-plane)
    if (hasPositive && (hasNegative || hasOnPlane))
        return true;
    if (hasNegative && hasOnPlane)
        return true;

    return false;
}

/**
 * Merge near-duplicate intersection points (corner intersections where
 * two edges meet at a vertex both report the same crossing).
 *
 * @param intersections : Array of { point, edgeIdx, tangent, ... }
 * @returns : Deduplicated array
 */
export function mergeNearDuplicates(intersections is array) returns array
{
    var merged = [];
    for (var pt in intersections)
    {
        var isDuplicate = false;
        for (var existing in merged)
        {
            if (normU(subtractU(existing.point, pt.point)) < GEOM_TOL * 10)  // Slightly larger tol for corner merging
            {
                isDuplicate = true;
                break;
            }
        }
        if (!isDuplicate)
            merged = append(merged, pt);
    }
    return merged;
}

/**
 * Filter tangent intersections from a list of edge-plane crossings.
 *
 * When the count is odd (indicating a tangent touch), removes crossings
 * where the edge direction is nearly parallel to the plane.
 *
 * Uses the tangent stored on each intersection result (from CP polygon
 * segment direction) rather than calling deBoorDerivative.
 *
 * @param intersections : Array of { point, edgeIdx, tangent }
 * @param planeNormal : Plane normal (unitless)
 * @returns : Filtered array (even count, or empty if degenerate)
 */
export function filterTangentIntersections(intersections is array,
    planeNormal is array) returns array
{
    if (size(intersections) % 2 == 0)
        return intersections;  // Already even -- no filtering needed

    var filtered = [];
    for (var pt in intersections)
    {
        var tangLenSq = normSqU(pt.tangent);
        if (tangLenSq < MATH_TOL * MATH_TOL)
        {
            filtered = append(filtered, pt);  // Keep degenerate -- can't determine angle
            continue;
        }
        var tangentNorm = scaleU(pt.tangent, 1 / sqrt(tangLenSq));
        var crossingAngle = abs(dotU(tangentNorm, planeNormal));

        if (crossingAngle > TANGENT_THRESHOLD)
        {
            filtered = append(filtered, pt);
        }
        // else: tangent touch, discard
    }

    return filtered;
}
