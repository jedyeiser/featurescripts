FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// ============================================================================
// bspline_compat -- exact B-spline algebra for making curves compatible
// ============================================================================
//
// Everything here is exact: a curve that goes in comes out describing the same points to
// the last bit, with a different control net. That is the whole reason this file exists.
// Two sections that share degree and knots define the ruled surface between them as a
// B-spline surface whose control net is their two control polygons -- no loft, no fit, no
// kernel heuristics. When they do not share them, the way to get there is to CHANGE THE
// REPRESENTATION, never the geometry: elevate the lower degree, reparameterize onto a common
// range, join a chain of pieces into one curve, and insert each curve's knots into the other.
//
// Non-rational, clamped, non-periodic curves only. Arcs are rational and are not handled;
// a caller finding one falls back to a loft.
//
// Lifted in simplified form from tools/bspline_knots.fs (insertKnot, Piegl & Tiller A5.1;
// elevateDegree via Bezier decomposition) and tools/curve_operations.fs (joinCurves). Kept
// in this document rather than imported so that the document stays self-contained and
// fscheck can see the calls; see the corrections log.

/** Two knots closer than this are the same knot. */
export const KNOT_TOL = 1e-9;

// ============================================================================
// Construction
// ============================================================================

/**
 * A straight segment as a degree-1 B-spline.
 */
export function lineAsBSpline(start is Vector, end is Vector) returns BSplineCurve
{
    return bSplineCurve({
                "degree" : 1,
                "isPeriodic" : false,
                "controlPoints" : [start, end],
                "knots" : knotArray([0, 0, 1, 1])
            });
}

/**
 * The curve with its parameter run backwards: control points reversed, knots mirrored about
 * the range.
 */
export function reverseBSpline(curve is BSplineCurve) returns BSplineCurve
{
    const first = curve.knots[0];
    const last = curve.knots[size(curve.knots) - 1];

    var knots = [];
    for (var k = size(curve.knots) - 1; k >= 0; k -= 1)
    {
        knots = append(knots, first + last - curve.knots[k]);
    }

    return bSplineCurve({
                "degree" : curve.degree,
                "isPeriodic" : false,
                "controlPoints" : reverse(curve.controlPoints),
                "knots" : knotArray(knots)
            });
}

/**
 * The same curve over the parameter range [a, b]. An affine change of parameter moves no
 * point.
 */
export function reparameterize(curve is BSplineCurve, a is number, b is number) returns BSplineCurve
{
    const first = curve.knots[0];
    const last = curve.knots[size(curve.knots) - 1];
    const scale = (b - a) / (last - first);

    var knots = [];
    for (var knot in curve.knots)
    {
        knots = append(knots, a + (knot - first) * scale);
    }

    return bSplineCurve({
                "degree" : curve.degree,
                "isPeriodic" : false,
                "controlPoints" : curve.controlPoints,
                "knots" : knotArray(knots)
            });
}

// ============================================================================
// Knot insertion
// ============================================================================

/**
 * The span index k with knots[k] <= u < knots[k + 1], clamped to the valid range.
 */
export function findKnotSpan(degree is number, u is number, knots is array) returns number
{
    const n = size(knots) - degree - 2;

    if (u >= knots[n + 1])
    {
        return n;
    }

    var k = degree;
    while (k < n && u >= knots[k + 1])
    {
        k += 1;
    }

    return k;
}

/**
 * The curve with one more knot at u. Boehm's algorithm, Piegl & Tiller A5.1.
 */
export function insertKnotOnce(curve is BSplineCurve, u is number) returns BSplineCurve
{
    const p = curve.degree;
    const points = curve.controlPoints;
    const knots = curve.knots;
    const n = size(points) - 1;
    const k = findKnotSpan(p, u, knots);

    var newPoints = [];
    for (var i = 0; i <= n + 1; i += 1)
    {
        if (i <= k - p)
        {
            newPoints = append(newPoints, points[i]);
        }
        else if (i > k)
        {
            newPoints = append(newPoints, points[i - 1]);
        }
        else
        {
            const denominator = knots[i + p] - knots[i];
            const alpha = (abs(denominator) < KNOT_TOL) ? 0 : (u - knots[i]) / denominator;
            newPoints = append(newPoints, (1 - alpha) * points[i - 1] + alpha * points[i]);
        }
    }

    var newKnots = [];
    for (var i = 0; i <= k; i += 1)
    {
        newKnots = append(newKnots, knots[i]);
    }
    newKnots = append(newKnots, u);
    for (var i = k + 1; i < size(knots); i += 1)
    {
        newKnots = append(newKnots, knots[i]);
    }

    return bSplineCurve({
                "degree" : p,
                "isPeriodic" : false,
                "controlPoints" : newPoints,
                "knots" : knotArray(newKnots)
            });
}

/**
 * The distinct knot values of a knot vector with their multiplicities, in order.
 *
 * @returns {array} : each { "value", "count" }.
 */
export function knotMultiplicities(knots is array) returns array
{
    var out = [];
    for (var knot in knots)
    {
        const last = size(out) - 1;
        if (last >= 0 && abs(knot - out[last].value) <= KNOT_TOL)
        {
            out[last].count += 1;
        }
        else
        {
            out = append(out, { "value" : knot, "count" : 1 });
        }
    }
    return out;
}

/**
 * The curve refined so that every value in `target` appears with at least the multiplicity
 * given there. Values already present are inserted at their existing exact value, so no
 * near-duplicate knots are created.
 *
 * @param target {array} : { "value", "count" } entries, as knotMultiplicities returns.
 */
export function refineToKnots(curve is BSplineCurve, target is array) returns BSplineCurve
{
    var result = curve;

    for (var entry in target)
    {
        var have = 0;
        var at = entry.value;
        for (var knot in result.knots)
        {
            if (abs(knot - entry.value) <= KNOT_TOL)
            {
                have += 1;
                at = knot;
            }
        }

        for (var i = have; i < entry.count; i += 1)
        {
            result = insertKnotOnce(result, at);
        }
    }

    return result;
}

/**
 * Every curve refined onto the union of all their knot vectors, so that afterwards they
 * share degree, knot vector and control-point count. All must already share degree and
 * parameter range.
 */
export function unifyKnots(curves is array) returns array
{
    var target = [];

    for (var curve in curves)
    {
        for (var entry in knotMultiplicities(curve.knots))
        {
            var found = false;
            for (var t = 0; t < size(target); t += 1)
            {
                if (abs(target[t].value - entry.value) <= KNOT_TOL)
                {
                    target[t].count = max(target[t].count, entry.count);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                target = append(target, entry);
            }
        }
    }

    target = sort(target, function(a, b) { return a.value - b.value; });

    var out = [];
    for (var curve in curves)
    {
        out = append(out, refineToKnots(curve, target));
    }
    return out;
}

// ============================================================================
// Degree elevation
// ============================================================================

/**
 * The curve raised to degree p, by Bezier decomposition: every interior knot is inserted up
 * to multiplicity p, each Bezier piece is elevated in closed form, and the pieces are
 * reassembled with the breakpoints at multiplicity p (of the new degree). Exact. The result
 * carries full-multiplicity interior knots, which describe the same curve with more control
 * points than the minimum; nothing here needs the minimum.
 */
export function elevateToDegree(curve is BSplineCurve, target is number) returns BSplineCurve
{
    var result = curve;
    while (result.degree < target)
    {
        result = elevateByOne(result);
    }
    return result;
}

function elevateByOne(curve is BSplineCurve) returns BSplineCurve
{
    const p = curve.degree;

    // Decompose: interior knots to multiplicity p.
    var bezier = curve;
    for (var entry in knotMultiplicities(curve.knots))
    {
        if (entry.value > curve.knots[0] + KNOT_TOL
            && entry.value < curve.knots[size(curve.knots) - 1] - KNOT_TOL)
        {
            for (var i = entry.count; i < p; i += 1)
            {
                bezier = insertKnotOnce(bezier, entry.value);
            }
        }
    }

    const breakpoints = knotMultiplicities(bezier.knots);
    const segments = size(breakpoints) - 1;
    const q = p + 1;

    var points = [];
    for (var s = 0; s < segments; s += 1)
    {
        // Bezier segment s owns control points [s*p .. s*p + p].
        for (var i = 0; i <= q; i += 1)
        {
            // Skip the shared first point of every segment but the first.
            if (s > 0 && i == 0)
            {
                continue;
            }

            const weightPrev = i / q;
            var point = vector(0, 0, 0) * meter;
            if (i > 0)
            {
                point = point + weightPrev * bezier.controlPoints[s * p + i - 1];
            }
            if (i <= p)
            {
                point = point + (1 - weightPrev) * bezier.controlPoints[s * p + i];
            }
            points = append(points, point);
        }
    }

    var knots = [];
    for (var b = 0; b < size(breakpoints); b += 1)
    {
        const count = (b == 0 || b == size(breakpoints) - 1) ? q + 1 : q;
        for (var i = 0; i < count; i += 1)
        {
            knots = append(knots, breakpoints[b].value);
        }
    }

    return bSplineCurve({
                "degree" : q,
                "isPeriodic" : false,
                "controlPoints" : points,
                "knots" : knotArray(knots)
            });
}

// ============================================================================
// Joining
// ============================================================================

/**
 * Two curves of one degree, the second starting where the first ends and parameterized to
 * continue it, as one curve. The join is a C0 knot of multiplicity `degree`; whatever
 * continuity the pieces really have across it is preserved in the geometry, since neither
 * piece moves. The second's first control point is taken from the first's last, so the join
 * is exact even if the two disagree by rounding.
 */
export function joinC0(a is BSplineCurve, b is BSplineCurve) returns BSplineCurve
{
    const p = a.degree;

    var knots = [];
    for (var i = 0; i < size(a.knots) - 1; i += 1)
    {
        knots = append(knots, a.knots[i]);
    }
    for (var i = p + 1; i < size(b.knots); i += 1)
    {
        knots = append(knots, b.knots[i]);
    }

    var points = a.controlPoints;
    for (var i = 1; i < size(b.controlPoints); i += 1)
    {
        points = append(points, b.controlPoints[i]);
    }

    return bSplineCurve({
                "degree" : p,
                "isPeriodic" : false,
                "controlPoints" : points,
                "knots" : knotArray(knots)
            });
}

/**
 * A chain of pieces, each already oriented to follow the previous, as one curve over [0, 1]
 * with each piece owning the share of the range given by `lengths`.
 *
 * @param pieces {array} : BSplineCurves of one degree, in order.
 * @param lengths {array} : the arc length of each piece.
 */
export function joinChain(pieces is array, lengths is array) returns BSplineCurve
{
    var total = 0 * meter;
    for (var length in lengths)
    {
        total += length;
    }

    var result = undefined;
    var at = 0;
    for (var i = 0; i < size(pieces); i += 1)
    {
        const to = (i == size(pieces) - 1) ? 1 : at + lengths[i] / total;
        const piece = reparameterize(pieces[i], at, to);
        result = (result == undefined) ? piece : joinC0(result, piece);
        at = to;
    }

    return result;
}

// ============================================================================
// Splitting
// ============================================================================

/**
 * The curve cut into pieces at every interior knot of multiplicity `degree` or more -- the
 * places where it is only C0, such as a joinC0 seam. A B-spline surface may not carry such
 * a crease (opCreateBSplineSurface refuses it as BSPLINESURFACE_NOT_G1), so a ruled patch
 * over a creased curve is one surface per piece, meeting along the crease as faces.
 *
 * @returns {array} : BSplineCurves in parameter order, each over its own sub-range.
 */
export function splitAtCreases(curve is BSplineCurve) returns array
{
    var pieces = [];
    var rest = curve;

    while (true)
    {
        const p = rest.degree;
        const knots = rest.knots;
        const first = knots[0];
        const last = knots[size(knots) - 1];

        // The first interior crease, as the index of its first knot.
        var at = -1;
        var value = 0;
        var run = 0;
        for (var i = 0; i < size(knots); i += 1)
        {
            if (knots[i] <= first + KNOT_TOL || knots[i] >= last - KNOT_TOL)
            {
                continue;
            }
            if (i > 0 && abs(knots[i] - knots[i - 1]) <= KNOT_TOL)
            {
                run += 1;
            }
            else
            {
                run = 1;
            }
            if (run >= p)
            {
                at = i - p + 1;
                value = knots[at];
                break;
            }
        }

        if (at < 0)
        {
            return append(pieces, rest);
        }

        // Left of the crease: control points up to the one at the seam, knots up to the
        // crease's copies plus one more to clamp. Right: from the seam point on, with the
        // crease clamped at its start.
        var leftPoints = [];
        for (var i = 0; i < at; i += 1)
        {
            leftPoints = append(leftPoints, rest.controlPoints[i]);
        }
        var leftKnots = [];
        for (var i = 0; i < at + p; i += 1)
        {
            leftKnots = append(leftKnots, knots[i]);
        }
        leftKnots = append(leftKnots, value);

        var rightPoints = [];
        for (var i = at - 1; i < size(rest.controlPoints); i += 1)
        {
            rightPoints = append(rightPoints, rest.controlPoints[i]);
        }
        var rightKnots = [value];
        for (var i = at; i < size(knots); i += 1)
        {
            rightKnots = append(rightKnots, knots[i]);
        }

        pieces = append(pieces, bSplineCurve({
                    "degree" : p,
                    "isPeriodic" : false,
                    "controlPoints" : leftPoints,
                    "knots" : knotArray(leftKnots)
                }));
        rest = bSplineCurve({
                "degree" : p,
                "isPeriodic" : false,
                "controlPoints" : rightPoints,
                "knots" : knotArray(rightKnots)
            });
    }
}
