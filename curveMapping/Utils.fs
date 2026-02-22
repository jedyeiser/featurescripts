FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// ============================================================================
// B-Spline Inflection Detection & Finding
// ============================================================================
// Designed to work with the output of evApproximateBSplineCurve, which returns
// a BSplineCurve map with fields: degree, isPeriodic, isRational,
// controlPoints (array of 3D Vector), knots (full knot vector with
// endpoint multiplicities), and optionally weights.
//
// Assumes knot vector convention: size(knots) = size(controlPoints) + degree + 1
//
// NOTE: These functions strip length units from control points internally
// so all numerical comparisons are unitless. The returned parameter values
// are raw parameter values matching the curve's knot domain.
// ============================================================================


// ============================================================================
// HELPER: Strip units from control points for clean numerical work
// ============================================================================
// Why: FeatureScript control points carry length units (meter). Cross products
// produce meter², dots produce meter⁴, and comparing those to 0 or thresholds
// gets messy. Stripping units up front keeps the math clean.
// The inflection parameters we return are unitless regardless.
/*
function stripUnits(controlPoints is array) returns array
{
    var result = makeArray(size(controlPoints));
    for (var i = 0; i < size(controlPoints); i += 1)
        result[i] = vector(controlPoints[i][0] / meter,
                           controlPoints[i][1] / meter,
                           controlPoints[i][2] / meter);
    return result;
}
*/


// ============================================================================
// HELPER: Find knot span index for parameter t
// ============================================================================
// Returns index k such that knots[k] <= t < knots[k+1]
// (with clamping for the upper endpoint)
// This is the standard knot span search for de Boor evaluation.

function findKnotSpan(knots is array, t is number, degree is number, numPoints is number) returns number
{
    // Handle upper endpoint: clamp to last non-degenerate span
    if (t >= knots[numPoints])
        return numPoints - 1;

    // Binary search for the span
    var low = degree;
    var high = numPoints;
    var mid = floor((low + high) / 2);

    while (t < knots[mid] || t >= knots[mid + 1])
    {
        if (t < knots[mid])
            high = mid;
        else
            low = mid;
        mid = floor((low + high) / 2);
    }

    return mid;
}


// ============================================================================
// HELPER: Evaluate a B-spline at parameter t using de Boor's algorithm
// ============================================================================
// Takes a "unitless" bspline map (control points already stripped of units).
// Returns a 3D vector (unitless).
//
// De Boor's algorithm is the B-spline equivalent of de Casteljau's for Bézier
// curves — it's numerically stable and works for any degree.

function evaluateBSplineUnitless(controlPoints is array, knots is array,
                                  degree is number, t is number) returns Vector
{
    const n = size(controlPoints);
    const k = findKnotSpan(knots, t, degree, n);

    // Initialize with the p+1 relevant control points
    var d = makeArray(degree + 1);
    for (var j = 0; j <= degree; j += 1)
        d[j] = controlPoints[k - degree + j];

    // Triangular computation — each round reduces degree by 1
    for (var r = 1; r <= degree; r += 1)
    {
        for (var j = degree; j >= r; j -= 1)
        {
            var idx = k - degree + j;
            var denom = knots[idx + degree - r + 1] - knots[idx];

            // Guard against zero-length knot spans (repeated knots)
            if (abs(denom) < 1e-15)
            {
                // When knot span is zero, the point doesn't move
                continue;
            }

            var alpha = (t - knots[idx]) / denom;
            d[j] = (1 - alpha) * d[j - 1] + alpha * d[j];
        }
    }

    return d[degree];
}


// ============================================================================
// HELPER: Compute the derivative B-spline
// ============================================================================
// The derivative of a degree-p B-spline is a degree-(p-1) B-spline.
// This is exact — no approximation or finite differences.
//
// Math:
//   Q[i] = p * (P[i+1] - P[i]) / (knots[i+p+1] - knots[i+1])
//   New knot vector = original with first and last entries removed
//
// This is fundamental to B-spline theory: differentiating a spline of degree p
// yields a spline of degree p-1, with one fewer control point per span.
// You can chain this to get higher derivatives (we'll call it twice for r'').

function bSplineDerivativeUnitless(controlPoints is array, knots is array,
                                    degree is number) returns map
{
    const n = size(controlPoints);
    const p = degree;

    if (p < 1)
    {
        // Derivative of a degree-0 spline is zero
        return {
            "controlPoints" : [vector(0, 0, 0)],
            "knots" : [knots[0], knots[size(knots) - 1]],
            "degree" : 0
        };
    }

    // Compute derivative control points
    var derivCP = makeArray(n - 1);
    for (var i = 0; i < n - 1; i += 1)
    {
        var denom = knots[i + p + 1] - knots[i + 1];
        if (abs(denom) < 1e-15)
        {
            // Zero-length knot span — derivative is technically infinite here,
            // but we set to zero to avoid blow-up. This happens at C0 joints.
            derivCP[i] = vector(0, 0, 0);
        }
        else
        {
            derivCP[i] = p * (vector(controlPoints[i + 1]) - vector(controlPoints[i])) / denom;
        }
    }

    // Strip first and last knot from the original knot vector
    var derivKnots = makeArray(size(knots) - 2);
    for (var i = 0; i < size(derivKnots); i += 1)
        derivKnots[i] = knots[i + 1];

    return {
        "controlPoints" : derivCP,
        "knots" : derivKnots,
        "degree" : p - 1
    };
}


// ============================================================================
// MAIN: Quick check — does the control polygon suggest a possible inflection?
// ============================================================================
// Uses the variation-diminishing property of B-splines: the curve cannot
// oscillate more than its control polygon. If all consecutive cross products
// of the polygon legs point in the same direction, the curve is convex and
// cannot have an inflection.
//
// Returns true  = inflection is POSSIBLE (further investigation needed)
// Returns false = NO inflection exists (guaranteed by V-D property)
//
// Cost: O(n) where n = number of control points. Essentially free.

export function bSplineMayHaveInflection(bSpline is map) returns boolean
{
    var cp = stripUnits(bSpline.controlPoints);
    const n = size(cp);

    // Need at least 3 control points to have curvature at all
    if (n < 3)
        return false;

    // Compute polygon legs
    var legs = makeArray(n - 1);
    for (var i = 0; i < n - 1; i += 1)
        legs[i] = vector(cp[i + 1]) - vector(cp[i]);

    // Compute cross products of consecutive legs
    // Each cross product indicates the local turning direction of the polygon.
    // Think of it as the "binormal" at each interior control point.
    var crosses = makeArray(n - 2);
    for (var i = 0; i < n - 2; i += 1)
        crosses[i] = cross(legs[i], legs[i + 1]);

    // Find the first non-degenerate cross product to use as reference
    // (degenerate = collinear legs, where cross product ≈ zero)
    var refIdx = -1;
    for (var i = 0; i < size(crosses); i += 1)
    {
        if (squaredNorm(crosses[i]) > 1e-20)
        {
            refIdx = i;
            break;
        }
    }

    if (refIdx == -1)
    {
        // All legs are collinear — the entire polygon is straight.
        // A straight B-spline has zero curvature everywhere, which is
        // technically not an inflection (no sign change). Return false.
        return false;
    }

    var refCross = crosses[refIdx];

    // Check if any cross product points in the opposite direction.
    // A negative dot product means the polygon turns the "other way"
    // at that vertex — i.e., a potential inflection in the curve.
    for (var i = 0; i < size(crosses); i += 1)
    {
        if (i == refIdx)
            continue;

        // Skip degenerate (collinear) vertices — they don't indicate direction
        if (squaredNorm(crosses[i]) < 1e-20)
            continue;

        if (dot(crosses[i], refCross) < 0)
            return true;
    }

    return false;
}


// ============================================================================
// MAIN: Find all inflection point parameters on the B-spline
// ============================================================================
// Strategy:
//   1. Compute r'(t) and r''(t) as analytical derivative B-splines
//   2. Sample the cross product c(t) = r'(t) × r''(t) along the curve
//   3. Detect where the osculating plane flips: consecutive c vectors pointing
//      in opposite directions (negative dot product) → true inflection
//   4. Bisect each candidate interval to refine the parameter
//
// Why dot-product sign change works for 3D:
//   At an inflection, curvature passes through zero AND the osculating plane
//   reverses. The cross product r' × r'' is the binormal direction scaled by
//   curvature; when it flips direction, that's a true inflection. Comparing
//   consecutive cross products via dot product converts this into a scalar
//   sign-change problem that bisection can solve.
//
// Parameters:
//   bSpline     — BSplineCurve from evApproximateBSplineCurve
//   numSamples  — number of sample points (higher = less chance of missing
//                 closely-spaced inflections; 10-20 per knot span is safe)
//   tolerance   — bisection convergence tolerance on the parameter

export function findBSplineInflections(bSpline is map, numSamples is number,
                                 tolerance is number) returns array
{
    const cp = stripUnits(bSpline.controlPoints);
    const knots = bSpline.knots;
    const degree = bSpline.degree;

    // Compute analytical first derivative B-spline
    const d1 = bSplineDerivativeUnitless(cp, knots, degree);

    // Compute analytical second derivative B-spline (derivative of derivative)
    const d2 = bSplineDerivativeUnitless(d1.controlPoints, d1.knots, d1.degree);

    // Parameter domain (from the clamped knot endpoints)
    const tMin = knots[degree];
    const tMax = knots[size(knots) - degree - 1];
    const dt = (tMax - tMin) / numSamples;

    // ---- Phase 1: Sample cross products along the curve ----
    var samples = makeArray(numSamples + 1);
    for (var i = 0; i <= numSamples; i += 1)
    {
        const t = min(tMin + i * dt, tMax);
        const r1 = evaluateBSplineUnitless(d1.controlPoints, d1.knots, d1.degree, t);
        const r2 = evaluateBSplineUnitless(d2.controlPoints, d2.knots, d2.degree, t);
        samples[i] = {
            "t" : t,
            "crossVec" : cross(r1, r2)
        };
    }

    // ---- Phase 2: Find intervals where osculating plane flips ----
    var inflections = [];

    for (var i = 0; i < numSamples; i += 1)
    {
        const c0 = samples[i].crossVec;
        const c1 = samples[i + 1].crossVec;

        // Skip if either cross product is degenerate (near-zero curvature
        // at the sample point itself — could be near an inflection but
        // we need a non-degenerate neighbor to define the flip direction)
        if (squaredNorm(c0) < 1e-20 || squaredNorm(c1) < 1e-20)
        {
            // One of the sample points is very close to zero curvature.
            // Could be an inflection — add the midpoint as candidate and
            // let bisection refine it.
            if (squaredNorm(c0) < 1e-20 && squaredNorm(c1) < 1e-20)
                continue; // Both near-zero, likely a straight segment

            // Use the near-zero sample's parameter as a candidate
            var tCandidate = squaredNorm(c0) < 1e-20
                ? samples[i].t
                : samples[i + 1].t;
            inflections = append(inflections, tCandidate);
            continue;
        }

        // The key test: if the dot product is negative, the binormal has
        // flipped direction → inflection exists in this interval
        if (dot(c0, c1) < 0)
        {
            // ---- Phase 3: Bisect to refine ----
            // We bisect on f(t) = dot(r'(t) × r''(t), ref)
            // where ref is chosen to make f change sign across the interval.
            // Using c0 as the reference direction: f(tLeft) > 0, f(tRight) < 0.

            var tLeft = samples[i].t;
            var tRight = samples[i + 1].t;
            var refDir = c0;

            // We already know the signs:
            // dot(c0, refDir) > 0 (it's dot with itself)
            // dot(c1, refDir) < 0 (from the check above)

            while ((tRight - tLeft) > tolerance)
            {
                var tMid = (tLeft + tRight) / 2.0;
                var r1Mid = evaluateBSplineUnitless(d1.controlPoints, d1.knots, d1.degree, tMid);
                var r2Mid = evaluateBSplineUnitless(d2.controlPoints, d2.knots, d2.degree, tMid);
                var cMid = cross(r1Mid, r2Mid);

                if (dot(cMid, refDir) > 0)
                    tLeft = tMid;
                else
                    tRight = tMid;
            }

            inflections = append(inflections, (tLeft + tRight) / 2.0);
        }
    }

    return inflections;
}

// ============================================================================
// Best-fit Frenet frame for a line, aligned to world coordinate system
// ============================================================================
// Problem: A line has a defined tangent but no curvature, so the Frenet
// normal is undefined. We need to pick a normal that is "closest to the
// world coordinate system" — i.e., we project world axes onto the plane
// perpendicular to the tangent and pick the one that survives best.
//
// Method: Projection rejection. Given tangent t and reference vector ref:
//     n = ref - (ref · t) * t
// This strips out the component along t, leaving only the part
// perpendicular to the tangent. The longer the residual, the better
// that world axis "fits" as a normal.
//
// Returns a FeatureScript coordSystem where:
//     zAxis = tangent (line direction, normalized)
//     xAxis = best-fit normal (closest to a world axis)
//     yAxis = derived (zAxis × xAxis) internally by coordSystem
// ============================================================================

export function lineFrenetFrame(line is map) returns CoordSystem
{
    const tangent = normalize(line.direction);

    // Try each world axis as a candidate normal reference.
    // We want the one that is LEAST parallel to the tangent,
    // because that one loses the least length after projection
    // and produces the most stable normal.
    //
    // |t × ref| = sin(angle between them), so the largest cross
    // product magnitude means the most perpendicular reference.
    const worldAxes = [vector(1, 0, 0), vector(0, 1, 0), vector(0, 0, 1)];

    var bestRef = worldAxes[0];
    var bestSinSq = 0;

    for (var i = 0; i < 3; i += 1)
    {
        var c = cross(tangent, worldAxes[i]);
        var sinSq = squaredNorm(c);

        if (sinSq > bestSinSq)
        {
            bestSinSq = sinSq;
            bestRef = worldAxes[i];
        }
    }

    // Project-reject: remove the tangent component from the best reference
    const normal = normalize(bestRef - dot(bestRef, tangent) * tangent);

    return coordSystem(line.origin, normal, tangent);
}