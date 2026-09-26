FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "7a407d1cf555ba0254c21433");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

/**
 * Pull surface: push and pull a face with a grid of normal handles and hold its edges.
 *
 * The result is a new sheet built from the face's own B-spline control net:
 *   1. The face is read with evApproximateBSplineSurface (rational kept; forceNonRational
 *      creates C0 kinks). Single-span directions below the requested degree are
 *      degree-elevated exactly; a multi-span linear direction asks the kernel for a cubic.
 *   2. The net is clipped to the face's parameter box when the trim loop is that box,
 *      then refined by exact knot insertion (knots are only added, never moved).
 *   3. Control-point rows at each edge of a non-periodic direction are locked:
 *      1 row for G0, 2 for G1, 3 for G2 (more on an unclamped end). Weights never change.
 *      Because an edge curve and its first/second cross derivatives depend only on those
 *      rows, the edges keep position, tangent plane and curvature along their whole length.
 *   4. Every free control point moves along the face normal at its Greville point by a
 *      scalar field D. D is a Catmull-Rom tensor interpolant over the handle grid whose
 *      node values are solved so that the surface moves by the handle's offset at the
 *      handle (linear solve, one unknown per free handle).
 *
 * Handles sit on a uCount x vCount grid of the face's parameter box. A handle is locked
 * (no manipulator) on the grid edges, and one / two rows further in for G1 / G2, so G1
 * needs 5 curves each way and G2 7 before any handle is free.
 * Offsets are stored in definition.mpOffsets (flat, i * vCount + j) and mirrored in the
 * visible activeOffsets list.
 */

// Knot values closer than this fraction of the domain are treated as equal.
const PULL_KNOT_EPS = 1e-9;
// Knot spans per handle interval after refinement.
const PULL_SPANS_PER_HANDLE = 3;
// Largest requested degree honoured by exact elevation.
const PULL_MAX_DEGREE = 5;
// Solved handle values larger than this multiple of the largest offset count as ill-conditioned.
const PULL_SOLVE_LIMIT = 50;

// -- Handle grid ------------------------------------------------------------------

/**
 * Returns true if grid handle (i, j) has no manipulator: the grid edges, plus one
 * (G1) or two (G2) further rows. Unchanged from the original feature so saved offsets
 * keep their meaning.
 */
export function isPointLocked(i is number, j is number, uCount is number, vCount is number, continuity is GeometricContinuity) returns boolean
{
    const boundary = (i == 0 || i == uCount - 1 || j == 0 || j == vCount - 1);
    if (continuity == GeometricContinuity.G0)
    {
        return boundary;
    }
    const g1row = (i == 1 || i == uCount - 2 || j == 1 || j == vCount - 2);
    if (continuity == GeometricContinuity.G1)
    {
        return boundary || g1row;
    }
    const g2row = (i == 2 || i == uCount - 3 || j == 2 || j == vCount - 3);
    return boundary || g1row || g2row;
}

/** Flat offset cache (i * vCount + j) built from a list of { u, v, value } items. */
function flatOffsets(items is array, uCount is number, vCount is number) returns array
{
    var offsets = makeArray(uCount * vCount, { "off" : 0 * meter });
    for (var item in items)
    {
        if (item.u >= 0 && item.u < uCount && item.v >= 0 && item.v < vCount)
        {
            offsets[item.u * vCount + item.v] = { "off" : item.value };
        }
    }
    return offsets;
}

/**
 * Catmull-Rom weights of a uniform grid of `count` nodes at normalized position x in [0, 1].
 * Returns [[node, weight], ...]. End segments use a linearly extrapolated ghost node.
 */
function catmullRomWeights(x is number, count is number) returns array
{
    const xs = min(max(x, 0), 1) * (count - 1);
    if (count == 2)
    {
        return [[0, 1 - xs], [1, xs]];
    }
    const i = min(floor(xs), count - 2);
    const t = xs - i;
    const t2 = t * t;
    const t3 = t2 * t;
    var wm1 = (-t3 + 2 * t2 - t) / 2;
    var w0 = (3 * t3 - 5 * t2 + 2) / 2;
    var w1 = (-3 * t3 + 4 * t2 + t) / 2;
    var w2 = (t3 - t2) / 2;
    var result = [];
    if (i - 1 < 0)
    {
        // ghost d[-1] = 2 d[0] - d[1]
        w0 += 2 * wm1;
        w1 -= wm1;
    }
    else
    {
        result = append(result, [i - 1, wm1]);
    }
    if (i + 2 > count - 1)
    {
        // ghost d[count] = 2 d[count - 1] - d[count - 2]
        w1 += 2 * w2;
        w0 -= w2;
        result = append(result, [i, w0]);
        result = append(result, [i + 1, w1]);
    }
    else
    {
        result = append(result, [i, w0]);
        result = append(result, [i + 1, w1]);
        result = append(result, [i + 2, w2]);
    }
    return result;
}

// -- B-spline basics (P&T ch. 2 / 5) ---------------------------------------------------

function plainArray(values is array) returns array
{
    var out = [];
    for (var x in values)
    {
        out = append(out, x);
    }
    return out;
}

function binomialCoef(n is number, k is number) returns number
{
    var r = 1;
    for (var i = 1; i <= k; i += 1)
    {
        r = r * (n - k + i) / i;
    }
    return r;
}

/** Span index (P&T A2.1); n = number of control points - 1. */
function findSpanIdx(n is number, p is number, u is number, knots is array) returns number
{
    if (u >= knots[n + 1])
    {
        var k = n;
        while (k > p && knots[k] >= knots[n + 1])
        {
            k -= 1;
        }
        return k;
    }
    if (u <= knots[p])
    {
        var k = p;
        while (k < n && knots[k + 1] <= knots[p])
        {
            k += 1;
        }
        return k;
    }
    var low = p;
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

/** Non-zero basis functions N[span - p .. span] at u (P&T A2.2). */
function basisFuns(span is number, u is number, p is number, knots is array) returns array
{
    var basis = makeArray(p + 1, 0);
    basis[0] = 1;
    var left = makeArray(p + 1, 0);
    var right = makeArray(p + 1, 0);
    for (var j = 1; j <= p; j += 1)
    {
        left[j] = u - knots[span + 1 - j];
        right[j] = knots[span + j] - u;
        var saved = 0;
        for (var r = 0; r < j; r += 1)
        {
            const den = right[r + 1] + left[j - r];
            const temp = (den == 0) ? 0 : basis[r] / den;
            basis[r] = saved + right[r + 1] * temp;
            saved = left[j - r] * temp;
        }
        basis[j] = saved;
    }
    return basis;
}

/** Basis functions and their first derivatives at u. */
function basisFunsD1(span is number, u is number, p is number, knots is array) returns map
{
    const basis = basisFuns(span, u, p, knots);
    var dBasis = makeArray(p + 1, 0);
    if (p > 0)
    {
        const lower = basisFuns(span, u, p - 1, knots);
        for (var k = 0; k <= p; k += 1)
        {
            const i = span - p + k;
            var d = 0;
            if (k >= 1)
            {
                const den = knots[i + p] - knots[i];
                if (den > 0)
                {
                    d += p * lower[k - 1] / den;
                }
            }
            if (k <= p - 1)
            {
                const den = knots[i + p + 1] - knots[i + 1];
                if (den > 0)
                {
                    d -= p * lower[k] / den;
                }
            }
            dBasis[k] = d;
        }
    }
    return { "basis" : basis, "dBasis" : dBasis };
}

function knotMultiplicity(knots is array, u is number) returns number
{
    var m = 0;
    for (var x in knots)
    {
        if (x == u)
        {
            m += 1;
        }
    }
    return m;
}

function transposeGrid(grid is array) returns array
{
    const nRows = size(grid);
    const nCols = size(grid[0]);
    var out = makeArray(nCols);
    for (var j = 0; j < nCols; j += 1)
    {
        var col = makeArray(nRows);
        for (var i = 0; i < nRows; i += 1)
        {
            col[i] = grid[i][j];
        }
        out[j] = col;
    }
    return out;
}

function blendRows(rowA is array, rowB is array, alpha is number) returns array
{
    var out = makeArray(size(rowA));
    for (var j = 0; j < size(rowA); j += 1)
    {
        out[j] = alpha * rowA[j] + (1 - alpha) * rowB[j];
    }
    return out;
}

/** Boehm single knot insertion along the first index of a homogeneous grid (P&T A5.1). */
function insertKnotRows(rows is array, knots is array, p is number, u is number) returns map
{
    const n = size(rows) - 1;
    const k = findSpanIdx(n, p, u, knots);
    const s = knotMultiplicity(knots, u);
    if (s >= p)
    {
        return { "rows" : rows, "knots" : knots };
    }
    var out = makeArray(n + 2);
    for (var i = 0; i <= k - p; i += 1)
    {
        out[i] = rows[i];
    }
    for (var i = k - p + 1; i <= k - s; i += 1)
    {
        const alpha = (u - knots[i]) / (knots[i + p] - knots[i]);
        out[i] = blendRows(rows[i], rows[i - 1], alpha);
    }
    for (var i = k - s + 1; i <= n + 1; i += 1)
    {
        out[i] = rows[i - 1];
    }
    var newKnots = [];
    for (var i = 0; i < size(knots); i += 1)
    {
        newKnots = append(newKnots, knots[i]);
        if (i == k)
        {
            newKnots = append(newKnots, u);
        }
    }
    return { "rows" : out, "knots" : newKnots };
}

/** Exact Bezier degree elevation p -> p + t along the first index (single clamped span). */
function elevateBezierRows(rows is array, p is number, t is number) returns array
{
    const q = p + t;
    var out = makeArray(q + 1);
    for (var i = 0; i <= q; i += 1)
    {
        var acc = makeArray(size(rows[0]), vector([0, 0, 0, 0]));
        for (var j = max(0, i - t); j <= min(p, i); j += 1)
        {
            const c = binomialCoef(p, j) * binomialCoef(t, i - j) / binomialCoef(q, i);
            for (var l = 0; l < size(acc); l += 1)
            {
                acc[l] = acc[l] + c * rows[j][l];
            }
        }
        out[i] = acc;
    }
    return out;
}

// -- Homogeneous net ------------------------------------------------------------------
// net = { uDeg, vDeg, uKnots, vKnots, uPer, vPer, rational, hom }
// hom[i][j] = vector(w x, w y, w z, w) in meters (unitless numbers); i runs along u.

function netFromSurface(surf is map) returns map
{
    const nu = size(surf.controlPoints);
    const nv = size(surf.controlPoints[0]);
    const rational = surf.isRational && surf.weights != undefined;
    var hom = makeArray(nu);
    for (var i = 0; i < nu; i += 1)
    {
        var row = makeArray(nv);
        for (var j = 0; j < nv; j += 1)
        {
            const pt = surf.controlPoints[i][j] / meter;
            const w = rational ? surf.weights[i][j] : 1;
            row[j] = vector([w * pt[0], w * pt[1], w * pt[2], w]);
        }
        hom[i] = row;
    }
    return { "uDeg" : surf.uDegree, "vDeg" : surf.vDegree,
            "uKnots" : plainArray(surf.uKnots), "vKnots" : plainArray(surf.vKnots),
            "uPer" : surf.isUPeriodic, "vPer" : surf.isVPeriodic, "rational" : rational, "hom" : hom };
}

function dirDegree(net is map, dirU is boolean) returns number
{
    return dirU ? net.uDeg : net.vDeg;
}

function dirKnots(net is map, dirU is boolean) returns array
{
    return dirU ? net.uKnots : net.vKnots;
}

function dirCount(net is map, dirU is boolean) returns number
{
    return dirU ? size(net.hom) : size(net.hom[0]);
}

function dirPeriodic(net is map, dirU is boolean) returns boolean
{
    return dirU ? net.uPer : net.vPer;
}

/** [lo, hi] parameter domain of one direction. */
function dirDomain(net is map, dirU is boolean) returns array
{
    const knots = dirKnots(net, dirU);
    return [knots[dirDegree(net, dirU)], knots[dirCount(net, dirU)]];
}

function rowsOf(net is map, dirU is boolean) returns array
{
    return dirU ? net.hom : transposeGrid(net.hom);
}

function withRows(net is map, dirU is boolean, rows is array, knots is array, degree is number) returns map
{
    var out = net;
    if (dirU)
    {
        out.hom = rows;
        out.uKnots = knots;
        out.uDeg = degree;
    }
    else
    {
        out.hom = transposeGrid(rows);
        out.vKnots = knots;
        out.vDeg = degree;
    }
    return out;
}

function insertKnotDir(net is map, dirU is boolean, u is number) returns map
{
    const res = insertKnotRows(rowsOf(net, dirU), dirKnots(net, dirU), dirDegree(net, dirU), u);
    return withRows(net, dirU, res.rows, res.knots, dirDegree(net, dirU));
}

/** True when a direction is one clamped Bezier span. */
function isSingleSpan(net is map, dirU is boolean) returns boolean
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    if (dirPeriodic(net, dirU) || dirCount(net, dirU) != p + 1)
    {
        return false;
    }
    for (var i = 0; i <= p; i += 1)
    {
        if (knots[i] != knots[0] || knots[p + 1 + i] != knots[p + 1])
        {
            return false;
        }
    }
    return true;
}

function elevateDir(net is map, dirU is boolean, target is number) returns map
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    const rows = elevateBezierRows(rowsOf(net, dirU), p, target - p);
    const newKnots = concatenateArrays([makeArray(target + 1, knots[0]), makeArray(target + 1, knots[p + 1])]);
    return withRows(net, dirU, rows, newKnots, target);
}

/** Snap u to an existing knot within eps (so exact box bounds are not inserted twice). */
function snapToKnot(knots is array, u is number, eps is number) returns number
{
    for (var x in knots)
    {
        if (abs(x - u) <= eps)
        {
            return x;
        }
    }
    return u;
}

/** Restrict a non-periodic direction to [a, b] exactly (knot insertion to multiplicity p, then cut). */
function clipDir(net is map, dirU is boolean, a is number, b is number) returns map
{
    const p = dirDegree(net, dirU);
    const dom = dirDomain(net, dirU);
    const eps = PULL_KNOT_EPS * (dom[1] - dom[0]);
    var out = net;
    if (b < dom[1] - eps)
    {
        const bb = snapToKnot(dirKnots(out, dirU), b, eps);
        while (knotMultiplicity(dirKnots(out, dirU), bb) < p)
        {
            out = insertKnotDir(out, dirU, bb);
        }
        const knots = dirKnots(out, dirU);
        var e = 0;
        while (knots[e] != bb)
        {
            e += 1;
        }
        const rows = rowsOf(out, dirU);
        var keptRows = [];
        var keptKnots = [];
        for (var i = 0; i < e; i += 1)
        {
            keptRows = append(keptRows, rows[i]);
            keptKnots = append(keptKnots, knots[i]);
        }
        keptKnots = concatenateArrays([keptKnots, makeArray(p + 1, bb)]);
        out = withRows(out, dirU, keptRows, keptKnots, p);
    }
    if (a > dom[0] + eps)
    {
        const aa = snapToKnot(dirKnots(out, dirU), a, eps);
        while (knotMultiplicity(dirKnots(out, dirU), aa) < p)
        {
            out = insertKnotDir(out, dirU, aa);
        }
        const knots = dirKnots(out, dirU);
        var s = 0;
        while (knots[s] != aa)
        {
            s += 1;
        }
        const rows = rowsOf(out, dirU);
        var keptRows = [];
        for (var i = s - 1; i < size(rows); i += 1)
        {
            keptRows = append(keptRows, rows[i]);
        }
        var keptKnots = makeArray(p + 1, aa);
        for (var i = s + p; i < size(knots); i += 1)
        {
            keptKnots = append(keptKnots, knots[i]);
        }
        out = withRows(out, dirU, keptRows, keptKnots, p);
    }
    return out;
}

/** Number of non-empty knot spans in the domain. */
function spanCount(net is map, dirU is boolean) returns number
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    var c = 0;
    for (var k = p; k < dirCount(net, dirU); k += 1)
    {
        if (knots[k + 1] > knots[k])
        {
            c += 1;
        }
    }
    return c;
}

/** Bisect the largest span until the direction has `target` spans (non-periodic only). */
function refineDir(net is map, dirU is boolean, target is number) returns map
{
    var out = net;
    while (spanCount(out, dirU) < target)
    {
        const p = dirDegree(out, dirU);
        const knots = dirKnots(out, dirU);
        var best = -1;
        var bestLen = 0;
        for (var k = p; k < dirCount(out, dirU); k += 1)
        {
            if (knots[k + 1] - knots[k] > bestLen)
            {
                bestLen = knots[k + 1] - knots[k];
                best = k;
            }
        }
        if (best < 0)
        {
            break;
        }
        out = insertKnotDir(out, dirU, (knots[best] + knots[best + 1]) / 2);
    }
    return out;
}

/**
 * Row lock mask of one direction. Non-periodic ends lock (order + 1) rows, plus
 * (p + 1 - end multiplicity) on an unclamped end. Interior knots of multiplicity >= p
 * (a C0 joint in parameter) lock the joint row and max(order, 1) rows either side, so
 * a geometric G1 joint stays G1.
 */
function lockMask(net is map, dirU is boolean, order is number) returns array
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    const n = dirCount(net, dirU) - 1;
    var mask = makeArray(n + 1, false);
    if (!dirPeriodic(net, dirU))
    {
        var mStart = 0;
        for (var j = p; j >= 0 && knots[j] == knots[p]; j -= 1)
        {
            mStart += 1;
        }
        var mEnd = 0;
        for (var j = n + 1; j < size(knots) && knots[j] == knots[n + 1]; j += 1)
        {
            mEnd += 1;
        }
        const lockStart = order + 1 + (p + 1 - min(mStart, p + 1));
        const lockEnd = order + 1 + (p + 1 - min(mEnd, p + 1));
        for (var k = 0; k <= n; k += 1)
        {
            if (k < lockStart || k > n - lockEnd)
            {
                mask[k] = true;
            }
        }
    }
    const reach = max(order, 1);
    var f = p + 1;
    while (f <= n)
    {
        var m = 1;
        while (f + m <= n && knots[f + m] == knots[f])
        {
            m += 1;
        }
        if (m >= p && knots[f] > knots[p] && knots[f] < knots[n + 1])
        {
            for (var k = f - 1 - reach; k <= f + m - p - 1 + reach; k += 1)
            {
                if (k >= 0 && k <= n)
                {
                    mask[k] = true;
                }
            }
        }
        f += m;
    }
    return mask;
}

function greville(net is map, dirU is boolean) returns array
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    const n = dirCount(net, dirU);
    var g = makeArray(n);
    for (var k = 0; k < n; k += 1)
    {
        var acc = 0;
        for (var i = k + 1; i <= k + p; i += 1)
        {
            acc += knots[i];
        }
        g[k] = acc / p;
    }
    return g;
}

/** Wrap (periodic) or clamp a parameter into the direction's domain. */
function toDomain(net is map, dirU is boolean, u is number) returns number
{
    const dom = dirDomain(net, dirU);
    if (dirPeriodic(net, dirU))
    {
        const period = dom[1] - dom[0];
        return u - floor((u - dom[0]) / period) * period;
    }
    return min(max(u, dom[0]), dom[1]);
}

/** Rational surface point, first partials and the basis data at (s, t). Unitless meters. */
function evalNet(net is map, sIn is number, tIn is number) returns map
{
    const s = toDomain(net, true, sIn);
    const t = toDomain(net, false, tIn);
    const p = net.uDeg;
    const q = net.vDeg;
    const su = findSpanIdx(size(net.hom) - 1, p, s, net.uKnots);
    const sv = findSpanIdx(size(net.hom[0]) - 1, q, t, net.vKnots);
    const bu = basisFunsD1(su, s, p, net.uKnots);
    const bv = basisFunsD1(sv, t, q, net.vKnots);
    var pa = vector(0, 0, 0);
    var pas = vector(0, 0, 0);
    var pat = vector(0, 0, 0);
    var w = 0;
    var ws = 0;
    var wt = 0;
    for (var a = 0; a <= p; a += 1)
    {
        for (var b = 0; b <= q; b += 1)
        {
            const h = net.hom[su - p + a][sv - q + b];
            const xyz = vector(h[0], h[1], h[2]);
            const nn = bu.basis[a] * bv.basis[b];
            const ns = bu.dBasis[a] * bv.basis[b];
            const nt = bu.basis[a] * bv.dBasis[b];
            pa = pa + nn * xyz;
            pas = pas + ns * xyz;
            pat = pat + nt * xyz;
            w += nn * h[3];
            ws += ns * h[3];
            wt += nt * h[3];
        }
    }
    const pt = pa / w;
    return { "pt" : pt, "ds" : (pas - ws * pt) / w, "dt" : (pat - wt * pt) / w,
            "su" : su, "sv" : sv, "bu" : bu.basis, "bv" : bv.basis, "w" : w, "s" : s, "t" : t };
}

/** Closest-point inversion of x (unitless meters) on the net, Gauss-Newton from (s, t). */
function invertOnNet(net is map, x is Vector, s0 is number, t0 is number) returns array
{
    var s = s0;
    var t = t0;
    for (var iter = 0; iter < 25; iter += 1)
    {
        const e = evalNet(net, s, t);
        const r = e.pt - x;
        const a11 = dot(e.ds, e.ds);
        const a12 = dot(e.ds, e.dt);
        const a22 = dot(e.dt, e.dt);
        const det = a11 * a22 - a12 * a12;
        if (det <= 1e-30)
        {
            break;
        }
        const b1 = -dot(e.ds, r);
        const b2 = -dot(e.dt, r);
        const dS = (b1 * a22 - b2 * a12) / det;
        const dT = (a11 * b2 - a12 * b1) / det;
        s = toDomain(net, true, s + dS);
        t = toDomain(net, false, t + dT);
        if (abs(dS) + abs(dT) < 1e-13)
        {
            break;
        }
    }
    return [s, t];
}

/** Exact iso-parametric curve of the net at normalized parameter f of one direction. */
function isoCurve(net is map, fixedU is boolean, f is number) returns BSplineCurve
{
    const dom = dirDomain(net, fixedU);
    const u = dom[0] + f * (dom[1] - dom[0]);
    const p = dirDegree(net, fixedU);
    const span = findSpanIdx(dirCount(net, fixedU) - 1, p, u, dirKnots(net, fixedU));
    const basis = basisFuns(span, u, p, dirKnots(net, fixedU));
    const rows = rowsOf(net, fixedU);
    const nOther = size(rows[0]);
    var pts = makeArray(nOther);
    var wts = makeArray(nOther);
    var rational = false;
    for (var l = 0; l < nOther; l += 1)
    {
        var h = vector([0, 0, 0, 0]);
        for (var a = 0; a <= p; a += 1)
        {
            h = h + basis[a] * rows[span - p + a][l];
        }
        pts[l] = vector(h[0], h[1], h[2]) / h[3] * meter;
        wts[l] = h[3];
        if (abs(h[3] - 1) > 1e-12)
        {
            rational = true;
        }
    }
    return bSplineCurve({ "degree" : dirDegree(net, !fixedU), "isPeriodic" : dirPeriodic(net, !fixedU),
                "controlPoints" : pts, "weights" : rational ? wts : undefined,
                "knots" : knotArray(dirKnots(net, !fixedU)) });
}

function netToSurface(net is map) returns BSplineSurface
{
    const nu = size(net.hom);
    const nv = size(net.hom[0]);
    var pts = makeArray(nu);
    var wts = makeArray(nu);
    for (var i = 0; i < nu; i += 1)
    {
        var rowP = makeArray(nv);
        var rowW = makeArray(nv);
        for (var j = 0; j < nv; j += 1)
        {
            const h = net.hom[i][j];
            rowP[j] = vector(h[0], h[1], h[2]) / h[3] * meter;
            rowW[j] = h[3];
        }
        pts[i] = rowP;
        wts[i] = rowW;
    }
    return bSplineSurface({ "uDegree" : net.uDeg, "vDegree" : net.vDeg,
                "isUPeriodic" : net.uPer, "isVPeriodic" : net.vPer,
                "controlPoints" : controlPointMatrix(pts),
                "weights" : net.rational ? matrix(wts) : undefined,
                "uKnots" : knotArray(net.uKnots), "vKnots" : knotArray(net.vKnots) });
}

/** Parameter box [uMin, uMax, vMin, vMax] of the trim loop and whether the loop IS that box. */
function trimBox(curves is array) returns map
{
    var lo = [inf, inf];
    var hi = [-inf, -inf];
    for (var c in curves)
    {
        for (var cp in c.controlPoints)
        {
            for (var d = 0; d < 2; d += 1)
            {
                lo[d] = min(lo[d], cp[d]);
                hi[d] = max(hi[d], cp[d]);
            }
        }
    }
    const eps = [PULL_KNOT_EPS * max(hi[0] - lo[0], 1e-12), PULL_KNOT_EPS * max(hi[1] - lo[1], 1e-12)];
    var isBox = true;
    for (var c in curves)
    {
        var onEdge = [true, true, true, true];
        for (var cp in c.controlPoints)
        {
            onEdge[0] = onEdge[0] && abs(cp[0] - lo[0]) <= eps[0] * 10;
            onEdge[1] = onEdge[1] && abs(cp[0] - hi[0]) <= eps[0] * 10;
            onEdge[2] = onEdge[2] && abs(cp[1] - lo[1]) <= eps[1] * 10;
            onEdge[3] = onEdge[3] && abs(cp[1] - hi[1]) <= eps[1] * 10;
        }
        if (!(onEdge[0] || onEdge[1] || onEdge[2] || onEdge[3]))
        {
            isBox = false;
        }
    }
    return { "lo" : lo, "hi" : hi, "isBox" : isBox };
}

// -- Editing logic -------------------------------------------------------------------------

/**
 * Resets all offsets when the grid size or continuity changes (the old offsets belong
 * to a different handle layout). Otherwise cleans the visible list: drops zero items,
 * items outside the grid (they used to wrap onto another handle) and items on locked
 * handles, and merges duplicates (the later item's value wins, the first position is
 * kept). The hidden flat cache is rebuilt from the clean list.
 *
 * The face is intentionally not compared: query comparison can flip during a
 * manipulator drag and would zero the offsets mid-drag.
 */
export function pullSurfaceEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    const uCount = definition.uCurveCount;
    const vCount = definition.vCurveCount;
    if (definition.uCurveCount != oldDefinition.uCurveCount ||
        definition.vCurveCount != oldDefinition.vCurveCount ||
        definition.continuityType != oldDefinition.continuityType)
    {
        definition.mpOffsets = makeArray(uCount * vCount, { "off" : 0 * meter });
        definition.activeOffsets = [];
        return definition;
    }
    var clean = [];
    var slotOf = {};
    if (definition.activeOffsets != undefined)
    {
        for (var item in definition.activeOffsets)
        {
            if (item.value == 0 * meter)
            {
                continue;
            }
            if (item.u < 0 || item.u >= uCount || item.v < 0 || item.v >= vCount)
            {
                continue;
            }
            if (isPointLocked(item.u, item.v, uCount, vCount, definition.continuityType))
            {
                continue;
            }
            const key = item.u ~ "_" ~ item.v;
            if (slotOf[key] != undefined)
            {
                clean[slotOf[key]] = item;
            }
            else
            {
                slotOf[key] = size(clean);
                clean = append(clean, item);
            }
        }
    }
    definition.activeOffsets = clean;
    definition.mpOffsets = flatOffsets(clean, uCount, vCount);
    return definition;
}

// -- Manipulator change function ---------------------------------------------------------

/**
 * Stores a dragged handle's offset in activeOffsets (added, updated, or removed when
 * dragged back to zero) and rebuilds the flat cache mpOffsets (index i * vCount + j).
 * Dynamic definition keys are rejected by Onshape ("Unknown parameter"), so all offsets
 * live in the declared arrays.
 */
export function pullSurfaceManipulator(context is Context, definition is map, newManipulators is map) returns map
{
    const uCount = definition.uCurveCount;
    const vCount = definition.vCurveCount;
    for (var key, manip in newManipulators)
    {
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                if (("mp_" ~ i ~ "_" ~ j) == key)
                {
                    var active = definition.activeOffsets;
                    if (active == undefined)
                    {
                        active = [];
                    }
                    var found = false;
                    var newActive = [];
                    for (var item in active)
                    {
                        if (item.u == i && item.v == j)
                        {
                            found = true;
                            if (manip.offset != 0 * meter)
                            {
                                newActive = append(newActive, { "u" : i, "v" : j, "value" : manip.offset });
                            }
                        }
                        else
                        {
                            newActive = append(newActive, item);
                        }
                    }
                    if (!found && manip.offset != 0 * meter)
                    {
                        newActive = append(newActive, { "u" : i, "v" : j, "value" : manip.offset });
                    }
                    definition.activeOffsets = newActive;
                }
            }
        }
    }
    definition.mpOffsets = flatOffsets(definition.activeOffsets == undefined ? [] : definition.activeOffsets, uCount, vCount);
    return definition;
}

// -- Feature ----------------------------------------------------------------------------------

annotation { "Feature Type Name" : "Pull surface",
        "Editing Logic Function" : "pullSurfaceEditingLogic",
        "Manipulator Change Function" : "pullSurfaceManipulator",
        "Feature Type Description" : "Creates a copy of a face pushed and pulled along its normal by a grid of handles. The edges keep their position (G0), tangent plane (G1) or curvature (G2) along their whole length; the face's control net is edited, not refitted." }
export const pullSurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1,
                    "Description" : "The face to pull. It is left unchanged; the result is a new surface." }
        definition.face is Query;

        annotation { "Name" : "U curve count", "Description" : "Handles across the face's first parameter direction, edges included." }
        isInteger(definition.uCurveCount, { (unitless) : [2, 4, 20] } as IntegerBoundSpec);

        annotation { "Name" : "V curve count", "Description" : "Handles across the face's second parameter direction, edges included." }
        isInteger(definition.vCurveCount, { (unitless) : [2, 4, 20] } as IntegerBoundSpec);

        annotation { "Name" : "Continuity", "UIHint" : [UIHint.SHOW_LABEL],
                    "Description" : "What the edges keep: position, tangency or curvature. Handles on the edges (and one / two rows in for tangency / curvature) are fixed, so tangency needs 5 curves each way and curvature 7 before a handle is free." }
        definition.continuityType is GeometricContinuity;

        // Live list of non-zero handle offsets, filled when a manipulator is dragged.
        // U and V are grid indices; Offset is along the face normal at the handle.
        annotation { "Name" : "Active offsets", "Item name" : "Offset",
                    "Description" : "Handle offsets. Drag a manipulator to add one; set Offset to 0 or delete the item to remove it." }
        definition.activeOffsets is array;
        for (var activeOffset in definition.activeOffsets)
        {
            annotation { "Name" : "U" }
            isInteger(activeOffset.u, { (unitless) : [0, 0, 19] } as IntegerBoundSpec);

            annotation { "Name" : "V" }
            isInteger(activeOffset.v, { (unitless) : [0, 0, 19] } as IntegerBoundSpec);

            annotation { "Name" : "Offset" }
            isLength(activeOffset.value, { (millimeter) : [-10000, 0, 10000] } as LengthBoundSpec);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show iso-curves", "Description" : "Draw iso-parameter lines of the result." }
            definition.showIsoCurves is boolean;

            annotation { "Name" : "Keep U curves", "Description" : "Create the result's iso-curves at the U grid positions as wires." }
            definition.keepUCurves is boolean;

            annotation { "Name" : "Keep V curves", "Description" : "Create the result's iso-curves at the V grid positions as wires." }
            definition.keepVCurves is boolean;

            annotation { "Name" : "Keep grid points", "Description" : "Create a point at every handle's target position." }
            definition.keepPoints is boolean;

            annotation { "Name" : "Show handle grid", "Description" : "Draw the handle grid at the target positions." }
            definition.showCPPolygons is boolean;

            annotation { "Name" : "Show offset vectors", "Description" : "Draw each non-zero offset (green out, red in)." }
            definition.showOffsetVectors is boolean;

            annotation { "Name" : "Print surface data", "Description" : "Print the control net, locks and the handle solve to the console." }
            definition.printCurveData is boolean;
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Degree", "Description" : "Minimum degree of the result (2 to 5). Exact elevation for single-span directions; a multi-span linear direction becomes cubic." }
            isInteger(definition.curveDegree, SurfDegreeBounds);

            annotation { "Name" : "Tolerance", "Description" : "Accuracy of the face's B-spline representation (read 10x tighter, within 1e-8 m to 1e-4 m)." }
            isLength(definition.fitTolerance, FitToleranceBounds);
        }

        // Hidden flat offset cache (i * vCurveCount + j). Declared so Onshape accepts the
        // manipulator function's writes. Field "off" avoids a duplicate name with activeOffsets.
        annotation { "Name" : "Manipulator offsets", "Item name" : "Offset", "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.mpOffsets is array;
        for (var mpOffset in definition.mpOffsets)
        {
            annotation { "Name" : "Off", "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            isLength(mpOffset.off, { (meter) : [-10, 0, 10] } as LengthBoundSpec);
        }
    }
    {
        const uCount = definition.uCurveCount;
        const vCount = definition.vCurveCount;
        const total = uCount * vCount;
        const order = (definition.continuityType == GeometricContinuity.G0) ? 0 :
            ((definition.continuityType == GeometricContinuity.G1) ? 1 : 2);
        var notes = [];

        // -- Handles: base points and normals on the face's parameter grid --
        var gridParams = [];
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                gridParams = append(gridParams, vector(i / (uCount - 1), j / (vCount - 1)));
            }
        }
        const basePlanes = evFaceTangentPlanes(context, { "face" : definition.face, "parameters" : gridParams });

        const offsets = definition.mpOffsets;
        const haveOffsets = offsets != undefined && size(offsets) == total;
        var handleOffset = makeArray(total, 0 * meter);
        var freeIndex = makeArray(total, -1);
        var freeHandles = [];
        var anyOffset = false;
        var manipMap = {};
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                const flat = i * vCount + j;
                if (isPointLocked(i, j, uCount, vCount, definition.continuityType))
                {
                    continue;
                }
                const off = haveOffsets ? offsets[flat].off : 0 * meter;
                handleOffset[flat] = off;
                freeIndex[flat] = size(freeHandles);
                freeHandles = append(freeHandles, flat);
                if (off != 0 * meter)
                {
                    anyOffset = true;
                }
                manipMap["mp_" ~ i ~ "_" ~ j] = linearManipulator({
                            "base" : basePlanes[flat].origin,
                            "direction" : basePlanes[flat].normal,
                            "offset" : off
                        });
            }
        }
        addManipulators(context, id, manipMap);
        if (size(freeHandles) == 0)
        {
            notes = append(notes, "No handle is free: tangency needs at least 5 U and V curves, curvature at least 7. The surface is an unchanged copy.");
        }

        var targets = makeArray(total);
        for (var flat = 0; flat < total; flat += 1)
        {
            targets[flat] = basePlanes[flat].origin + handleOffset[flat] * basePlanes[flat].normal;
        }

        // -- Source control net --
        const approxTol = min(max(definition.fitTolerance / meter / 10, 1e-8), 1e-4);
        var src = evApproximateBSplineSurface(context, { "face" : definition.face, "tolerance" : approxTol });
        var net = netFromSurface(src.bSplineSurface);
        if ((net.uDeg < 2 && !isSingleSpan(net, true)) || (net.vDeg < 2 && !isSingleSpan(net, false)))
        {
            src = evApproximateBSplineSurface(context, { "face" : definition.face, "tolerance" : approxTol, "forceCubic" : true });
            net = netFromSurface(src.bSplineSurface);
        }
        const targetDeg = min(max(definition.curveDegree, 2), PULL_MAX_DEGREE);
        for (var dirU in [true, false])
        {
            if (dirDegree(net, dirU) < targetDeg && isSingleSpan(net, dirU))
            {
                net = elevateDir(net, dirU, targetDeg);
            }
        }

        // -- Clip to the face's parameter box; keep the trim loop unless it is that box --
        var trims = [];
        const boundary = (src.boundaryBSplineCurves == undefined) ? [] : src.boundaryBSplineCurves;
        if (size(boundary) > 0)
        {
            const tb = trimBox(boundary);
            var needTrims = !tb.isBox;
            for (var d = 0; d < 2; d += 1)
            {
                const dirU = (d == 0);
                const dom = dirDomain(net, dirU);
                const eps = PULL_KNOT_EPS * (dom[1] - dom[0]) * 10;
                const partial = tb.lo[d] > dom[0] + eps || tb.hi[d] < dom[1] - eps;
                if (!partial)
                {
                    continue;
                }
                if (dirPeriodic(net, dirU))
                {
                    needTrims = true;
                }
                else
                {
                    net = clipDir(net, dirU, max(tb.lo[d], dom[0]), min(tb.hi[d], dom[1]));
                }
            }
            if (needTrims)
            {
                trims = boundary;
            }
        }
        if (src.innerLoopBSplineCurves != undefined && size(src.innerLoopBSplineCurves) > 0)
        {
            notes = append(notes, "Holes in the face are not kept; the result covers the outer boundary.");
        }

        var solveInfo = "no offsets";
        if (anyOffset)
        {
            // -- Refine (exact knot insertion) so the handle grid is resolved --
            const spansTarget = max(PULL_SPANS_PER_HANDLE * (max(uCount, vCount) - 1), 2 * (order + 1) + 4);
            for (var dirU in [true, false])
            {
                if (!dirPeriodic(net, dirU))
                {
                    net = refineDir(net, dirU, spansTarget);
                }
            }

            // -- Locked rows and candidate control points --
            const lockU = lockMask(net, true, order);
            const lockV = lockMask(net, false, order);
            const gU = greville(net, true);
            const gV = greville(net, false);
            const nu = size(net.hom);
            const nv = size(net.hom[0]);
            const isPlanar = !isQueryEmpty(context, qGeometry(definition.face, GeometryType.PLANE));
            var planeFrame = undefined;
            if (isPlanar)
            {
                const fr = evFaceTangentPlanes(context, { "face" : definition.face, "parameters" : [vector(0, 0), vector(1, 0), vector(0, 1)] });
                planeFrame = { "o" : fr[0].origin / meter, "ea" : (fr[1].origin - fr[0].origin) / meter, "eb" : (fr[2].origin - fr[0].origin) / meter };
            }
            const outsideTol = 10 * approxTol + 1e-7;
            var cands = [];
            for (var k = 0; k < nu; k += 1)
            {
                for (var l = 0; l < nv; l += 1)
                {
                    if (lockU[k] || lockV[l])
                    {
                        continue;
                    }
                    const e = evalNet(net, gU[k], gV[l]);
                    var ab = undefined;
                    var inside = true;
                    if (isPlanar)
                    {
                        const r = e.pt - planeFrame.o;
                        const a11 = dot(planeFrame.ea, planeFrame.ea);
                        const a12 = dot(planeFrame.ea, planeFrame.eb);
                        const a22 = dot(planeFrame.eb, planeFrame.eb);
                        const b1 = dot(planeFrame.ea, r);
                        const b2 = dot(planeFrame.eb, r);
                        const det = a11 * a22 - a12 * a12;
                        ab = [(b1 * a22 - b2 * a12) / det, (a11 * b2 - a12 * b1) / det];
                        if (size(trims) > 0)
                        {
                            inside = evDistance(context, { "side0" : definition.face, "side1" : e.pt * meter }).distance <= outsideTol * meter;
                        }
                    }
                    else
                    {
                        const dr = evDistance(context, { "side0" : definition.face, "side1" : e.pt * meter });
                        const par = dr.sides[0].parameter;
                        ab = [par[0], par[1]];
                        if (size(trims) > 0)
                        {
                            inside = dr.distance <= outsideTol * meter;
                        }
                    }
                    if (!inside)
                    {
                        continue;
                    }
                    ab = [min(max(ab[0], 0), 1), min(max(ab[1], 0), 1)];
                    // Field weights of this control point over the FREE handles.
                    var weights = [];
                    for (var wa in catmullRomWeights(ab[0], uCount))
                    {
                        for (var wb in catmullRomWeights(ab[1], vCount))
                        {
                            const fi = freeIndex[wa[0] * vCount + wb[0]];
                            if (fi >= 0 && wa[1] * wb[1] != 0)
                            {
                                weights = append(weights, [fi, wa[1] * wb[1]]);
                            }
                        }
                    }
                    cands = append(cands, { "k" : k, "l" : l, "s" : gU[k], "t" : gV[l], "pt" : e.pt, "ab" : ab, "weights" : weights });
                }
            }

            if (size(cands) == 0)
            {
                notes = append(notes, "Every control point is held by the edges; add knots (raise the curve counts) to free some. The surface is an unchanged copy.");
                solveInfo = "no free control points";
            }
            else
            {
                var candParams = [];
                for (var c in cands)
                {
                    candParams = append(candParams, vector(c.ab[0], c.ab[1]));
                }
                const candPlanes = evFaceTangentPlanes(context, { "face" : definition.face, "parameters" : candParams });
                var candIndex = {};
                for (var ci = 0; ci < size(cands); ci += 1)
                {
                    cands[ci].normal = candPlanes[ci].normal;
                    candIndex[cands[ci].k ~ "_" ~ cands[ci].l] = ci;
                }

                // -- Response of each free handle to each free field value --
                const m = size(freeHandles);
                var respRows = makeArray(m);
                var rhs = makeArray(m);
                var dMax = 0;
                for (var h = 0; h < m; h += 1)
                {
                    const flat = freeHandles[h];
                    const x = basePlanes[flat].origin / meter;
                    const nh = basePlanes[flat].normal;
                    // Start guess: nearest Greville point (strided on large nets).
                    const stride = max(1, floor(size(cands) / 400));
                    var best = 0;
                    var bestD = inf;
                    for (var ci = 0; ci < size(cands); ci += stride)
                    {
                        const dd = squaredNorm(cands[ci].pt - x);
                        if (dd < bestD)
                        {
                            bestD = dd;
                            best = ci;
                        }
                    }
                    const st = invertOnNet(net, x, cands[best].s, cands[best].t);
                    const e = evalNet(net, st[0], st[1]);
                    var row = makeArray(m, 0);
                    for (var a = 0; a <= net.uDeg; a += 1)
                    {
                        for (var b = 0; b <= net.vDeg; b += 1)
                        {
                            const k = e.su - net.uDeg + a;
                            const l = e.sv - net.vDeg + b;
                            const ci = candIndex[k ~ "_" ~ l];
                            if (ci == undefined)
                            {
                                continue;
                            }
                            const rb = e.bu[a] * e.bv[b] * net.hom[k][l][3] / e.w;
                            const proj = dot(cands[ci].normal, nh);
                            for (var fw in cands[ci].weights)
                            {
                                row[fw[0]] = row[fw[0]] + rb * proj * fw[1];
                            }
                        }
                    }
                    respRows[h] = row;
                    rhs[h] = [handleOffset[flat] / meter];
                    dMax = max(dMax, abs(handleOffset[flat] / meter));
                }

                var g = makeArray(m, 0);
                const solved = inverse(matrix(respRows)) * matrix(rhs);
                var solveOk = true;
                for (var h = 0; h < m; h += 1)
                {
                    const gh = solved[h][0];
                    if (!(abs(gh) <= PULL_SOLVE_LIMIT * dMax))
                    {
                        solveOk = false;
                    }
                    g[h] = gh;
                }
                if (!solveOk)
                {
                    for (var h = 0; h < m; h += 1)
                    {
                        g[h] = rhs[h][0];
                    }
                    notes = append(notes, "Some handles barely influence the control net (too close to a held edge or outside the face); offsets were applied without exact interpolation. Raise the curve counts or move the offsets inward.");
                }
                solveInfo = (solveOk ? "solved" : "fallback") ~ ", " ~ m ~ " free handles, " ~ size(cands) ~ " free control points";

                // -- Move the free control points along the normal (weights unchanged) --
                for (var c in cands)
                {
                    var disp = 0;
                    for (var fw in c.weights)
                    {
                        disp += fw[1] * g[fw[0]];
                    }
                    if (disp == 0)
                    {
                        continue;
                    }
                    const h = net.hom[c.k][c.l];
                    const shift = h[3] * disp * c.normal;
                    net.hom[c.k][c.l] = vector([h[0] + shift[0], h[1] + shift[1], h[2] + shift[2], h[3]]);
                }
            }

            if (definition.printCurveData)
            {
                var lu = "";
                for (var x in lockU)
                {
                    lu = lu ~ (x ? "L" : ".");
                }
                var lv = "";
                for (var x in lockV)
                {
                    lv = lv ~ (x ? "L" : ".");
                }
                println("[pullSurface] locks u " ~ lu ~ "  v " ~ lv);
            }
        }
        if (size(trims) > 0 && anyOffset)
        {
            notes = append(notes, "The face is trimmed inside its surface: continuity is exact on untrimmed edges only; trimmed edges are held approximately.");
        }

        if (definition.printCurveData)
        {
            println("[pullSurface] degree " ~ net.uDeg ~ " x " ~ net.vDeg ~ ", control points " ~ size(net.hom) ~ " x " ~ size(net.hom[0])
                    ~ ", rational " ~ net.rational ~ ", periodic " ~ net.uPer ~ "/" ~ net.vPer ~ ", trims " ~ size(trims) ~ "; " ~ solveInfo);
            println("[pullSurface] uKnots " ~ toString(net.uKnots));
            println("[pullSurface] vKnots " ~ toString(net.vKnots));
        }

        // -- Result sheet --
        var surfDef = { "bSplineSurface" : netToSurface(net) };
        if (size(trims) > 0)
        {
            surfDef.boundaryBSplineCurves = trims;
        }
        opCreateBSplineSurface(context, id + "pullSurf", surfDef);

        // -- Debug display --
        if (definition.showIsoCurves)
        {
            const newFace = qCreatedBy(id + "pullSurf", EntityType.FACE);
            const segs = 16;
            for (var d = 0; d < 2; d += 1)
            {
                const nLines = (d == 0) ? uCount : vCount;
                for (var i = 0; i < nLines; i += 1)
                {
                    var ps = [];
                    for (var k = 0; k <= segs; k += 1)
                    {
                        ps = append(ps, (d == 0) ? vector(i / (nLines - 1), k / segs) : vector(k / segs, i / (nLines - 1)));
                    }
                    const pl = evFaceTangentPlanes(context, { "face" : newFace, "parameters" : ps });
                    for (var k = 0; k < segs; k += 1)
                    {
                        addDebugLine(context, pl[k].origin, pl[k + 1].origin, (d == 0) ? DebugColor.CYAN : DebugColor.MAGENTA);
                    }
                }
            }
        }
        if (definition.showCPPolygons)
        {
            for (var i = 0; i < uCount; i += 1)
            {
                for (var j = 0; j < vCount; j += 1)
                {
                    addDebugPoint(context, targets[i * vCount + j], DebugColor.BLUE);
                    if (j < vCount - 1)
                    {
                        addDebugLine(context, targets[i * vCount + j], targets[i * vCount + j + 1], DebugColor.CYAN);
                    }
                    if (i < uCount - 1)
                    {
                        addDebugLine(context, targets[i * vCount + j], targets[(i + 1) * vCount + j], DebugColor.MAGENTA);
                    }
                }
            }
        }
        if (definition.showOffsetVectors)
        {
            for (var flat = 0; flat < total; flat += 1)
            {
                if (handleOffset[flat] != 0 * meter)
                {
                    addDebugLine(context, basePlanes[flat].origin, targets[flat],
                        (handleOffset[flat] > 0 * meter) ? DebugColor.GREEN : DebugColor.RED);
                }
            }
        }

        // -- Optional helper bodies (not part of the output) --
        if (definition.keepPoints)
        {
            for (var i = 0; i < uCount; i += 1)
            {
                for (var j = 0; j < vCount; j += 1)
                {
                    opPoint(context, id + ("pt_" ~ i ~ "_" ~ j), { "point" : targets[i * vCount + j] });
                }
            }
        }
        if (definition.keepUCurves)
        {
            for (var i = 0; i < uCount; i += 1)
            {
                opCreateBSplineCurve(context, id + ("keepU_" ~ i), { "bSplineCurve" : isoCurve(net, true, i / (uCount - 1)) });
            }
        }
        if (definition.keepVCurves)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                opCreateBSplineCurve(context, id + ("keepV_" ~ j), { "bSplineCurve" : isoCurve(net, false, j / (vCount - 1)) });
            }
        }

        if (size(notes) > 0)
        {
            var msg = notes[0];
            for (var n = 1; n < size(notes); n += 1)
            {
                msg = msg ~ " " ~ notes[n];
            }
            reportFeatureInfo(context, id, msg);
        }

        embedStandardOutputs(context, id, {
                    "output" : qCreatedBy(id + "pullSurf", EntityType.BODY),
                    "outputDescription" : "The pulled surface",
                    "inputs" : definition.face
                });
    }, { "activeOffsets" : [], "mpOffsets" : [], "showIsoCurves" : false, "keepUCurves" : false, "keepVCurves" : false,
            "keepPoints" : false, "showCPPolygons" : false, "showOffsetVectors" : false, "printCurveData" : false,
            "curveDegree" : 3, "fitTolerance" : 1e-6 * meter });
