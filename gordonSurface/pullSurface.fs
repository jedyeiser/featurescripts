FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "7a407d1cf555ba0254c21433");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: pull_surface_icon.svg (feature icon)
IconNamespace::import(path : "a2fbcb115c3ae9d8c34586c8", version : "d4bd951dc528ccff5aec857c");

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
 *
 * "One handle per control point": the net is refined until each non-periodic direction has
 * at least U / V curve count control points; every free control point gets a handle at its
 * Greville point and its offset moves that control point along the normal (no field, no
 * solve). activeOffsets then holds control-point indices (k, l) and is read directly.
 *
 * Closed faces (a full cylinder / revolved face): in a direction where the face meets
 * itself the handles sit at i / count (no duplicate at the seam), none is locked, the field
 * wraps, no edge rows are locked, and the overlapping control rows of a periodic net (or the
 * coincident first / last row of a clamped closed net) move together, so the result stays
 * closed. Poles (sphere cap, cone apex): a collapsed end row the face reaches extends the
 * clip box to the pole; the pole row is an end row, so it is locked like an edge.
 *
 * "Replace face": the pulled sheet replaces the source face in its body (opReplaceFace,
 * sense auto-detected), then the sheet is deleted. A one-face sheet (which opReplaceFace
 * refuses) is deleted instead and the pulled sheet is the result.
 * Periodic directions are refined too (exact periodic knot insertion); their C0 knot joints,
 * the seam included, lock like interior joints.
 */

// Knot values closer than this fraction of the domain are treated as equal.
const PULL_KNOT_EPS = 1e-9;
// Knot spans per handle interval after refinement.
const PULL_SPANS_PER_HANDLE = 3;
// Largest requested degree honoured by exact elevation.
const PULL_MAX_DEGREE = 5;
// Solved handle values larger than this multiple of the largest offset count as ill-conditioned.
const PULL_SOLVE_LIMIT = 50;
// Face parameters are kept this far inside [0, 1] (a cone apex has no tangent plane).
const PULL_PARAM_EPS = 1e-9;
// Two face points closer than this (meters) are the same point (closed direction, pole row).
const PULL_SAME_POINT = 1e-7;
// Homogeneous control points closer than this are the same (periodic overlap, closed seam).
const PULL_SAME_CP = 1e-10;

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

/**
 * isPointLocked with closed directions: a direction in which the face meets itself has no
 * edge, so no handle is locked by it. Open directions lock as isPointLocked.
 */
export function isHandleLocked(i is number, j is number, uCount is number, vCount is number, continuity is GeometricContinuity,
    uClosed is boolean, vClosed is boolean) returns boolean
{
    const reach = (continuity == GeometricContinuity.G0) ? 0 : ((continuity == GeometricContinuity.G1) ? 1 : 2);
    const lockedU = !uClosed && (i <= reach || i >= uCount - 1 - reach);
    const lockedV = !vClosed && (j <= reach || j >= vCount - 1 - reach);
    return lockedU || lockedV;
}

/** Normalized face parameter of handle i of `count`: i / (count - 1), or i / count in a closed direction. */
function gridParam(i is number, count is number, closed is boolean) returns number
{
    const x = closed ? i / count : i / (count - 1);
    return min(max(x, PULL_PARAM_EPS), 1 - PULL_PARAM_EPS);
}

/**
 * [uClosed, vClosed]: whether the face meets itself across its u (v) parameter range, read
 * from face points just inside the parameter box (the same answer in editing logic and body).
 */
export function faceClosedDirs(context is Context, face is Query) returns array
{
    if (isQueryEmpty(context, face))
    {
        return [false, false];
    }
    const e = PULL_PARAM_EPS;
    var ps = [];
    for (var s in [0.25, 0.5, 0.75])
    {
        ps = concatenateArrays([ps, [vector(e, s), vector(1 - e, s), vector(0.5, s), vector(s, e), vector(s, 1 - e), vector(s, 0.5)]]);
    }
    const pl = evFaceTangentPlanes(context, { "face" : face, "parameters" : ps });
    var uClosed = true;
    var vClosed = true;
    for (var k = 0; k < 3; k += 1)
    {
        const b = 6 * k;
        const tol = PULL_SAME_POINT * meter;
        uClosed = uClosed && norm(pl[b].origin - pl[b + 1].origin) < tol && norm(pl[b].origin - pl[b + 2].origin) > tol;
        vClosed = vClosed && norm(pl[b + 3].origin - pl[b + 4].origin) < tol && norm(pl[b + 3].origin - pl[b + 5].origin) > tol;
    }
    return [uClosed, vClosed];
}

/** activeOffsets with item (i, j) set to value (added, replaced, or removed when zero). */
function setOffsetItem(active, i is number, j is number, value is ValueWithUnits) returns array
{
    var found = false;
    var newActive = [];
    for (var item in (active == undefined ? [] : active))
    {
        if (item.u == i && item.v == j)
        {
            found = true;
            if (value != 0 * meter)
            {
                newActive = append(newActive, { "u" : i, "v" : j, "value" : value });
            }
        }
        else
        {
            newActive = append(newActive, item);
        }
    }
    if (!found && value != 0 * meter)
    {
        newActive = append(newActive, { "u" : i, "v" : j, "value" : value });
    }
    return newActive;
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

/** Catmull-Rom weights of `count` nodes at i / count on a closed (wrapping) direction. */
function catmullRomWeightsPeriodic(x is number, count is number) returns array
{
    const xs = (x - floor(x)) * count;
    const i = min(floor(xs), count - 1);
    const t = xs - i;
    const t2 = t * t;
    const t3 = t2 * t;
    const ws = [(-t3 + 2 * t2 - t) / 2, (3 * t3 - 5 * t2 + 2) / 2, (-3 * t3 + 4 * t2 + t) / 2, (t3 - t2) / 2];
    var result = [];
    for (var a = 0; a < 4; a += 1)
    {
        const node = i - 1 + a;
        result = append(result, [node - floor(node / count) * count, ws[a]]);
    }
    return result;
}

/** Field weights [[free index, weight], ...] of face parameter ab over the free handles. */
function fieldWeights(ab is array, uCount is number, vCount is number, closed is array, freeIndex is array) returns array
{
    const wu = closed[0] ? catmullRomWeightsPeriodic(ab[0], uCount) : catmullRomWeights(ab[0], uCount);
    const wv = closed[1] ? catmullRomWeightsPeriodic(ab[1], vCount) : catmullRomWeights(ab[1], vCount);
    var weights = [];
    for (var wa in wu)
    {
        for (var wb in wv)
        {
            const fi = freeIndex[wa[0] * vCount + wb[0]];
            if (fi >= 0 && wa[1] * wb[1] != 0)
            {
                weights = append(weights, [fi, wa[1] * wb[1]]);
            }
        }
    }
    return weights;
}

// -- B-spline basics (P&T ch. 2 / 5)---------------------------------------------------

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

/**
 * The kernel returns a closed revolved direction (full cylinder, sphere longitude) flagged
 * periodic but in CLAMPED form: end knots of multiplicity p + 1, last row = first row, no
 * p-row overlap (seen live 2026-09-26: 7 rows, knots 0000 .5.5.5 1111, rational semicircles).
 * Such a direction is handled as a clamped closed direction (tied seam row, refinable, seam
 * and joints locked like C0 knot joints); the periodic flag is restored on output (uPerOut).
 */
function normalizeClampedPeriodic(net is map) returns map
{
    var out = net;
    for (var dirU in [true, false])
    {
        if (!dirPeriodic(out, dirU) || isPeriodicOverlapped(out, dirU))
        {
            continue;
        }
        const p = dirDegree(out, dirU);
        const knots = dirKnots(out, dirU);
        const nK = size(knots);
        var clamped = true;
        for (var i = 0; i <= p; i += 1)
        {
            clamped = clamped && knots[i] == knots[0] && knots[nK - 1 - i] == knots[nK - 1];
        }
        const rows = rowsOf(out, dirU);
        if (!clamped || !sameRow(rows[0], rows[size(rows) - 1]))
        {
            continue;
        }
        if (dirU)
        {
            out.uPer = false;
            out.uPerOut = true;
        }
        else
        {
            out.vPer = false;
            out.vPerOut = true;
        }
    }
    return out;
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
    if (dirPeriodic(net, dirU))
    {
        return insertKnotPeriodic(net, dirU, u);
    }
    const res = insertKnotRows(rowsOf(net, dirU), dirKnots(net, dirU), dirDegree(net, dirU), u);
    return withRows(net, dirU, res.rows, res.knots, dirDegree(net, dirU));
}

/** True when a periodic direction is in the overlapped form (rows k and k + nUnique equal for k < p). */
function isPeriodicOverlapped(net is map, dirU is boolean) returns boolean
{
    return dirPeriodic(net, dirU) && uniqueRowCount(net, dirU, false) < dirCount(net, dirU);
}

/**
 * Exact knot insertion of u (in the domain) into a periodic direction, keeping the periodic
 * overlapped form. The padded arrays (knots[i] = T(i - p), row i = unique row i mod nU) are
 * continued p rows / knots to the right so u's span has all its control points, u is inserted
 * once (Boehm), and one period of new unique rows is read where no un-inserted periodic image
 * of u reaches the supports; the padded arrays are then rebuilt from those rows and the new
 * one-period knot list.
 */
function insertKnotPeriodic(net is map, dirU is boolean, u is number) returns map
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    const rows = rowsOf(net, dirU);
    const n = size(rows);
    const nU = n - p;
    const dom = dirDomain(net, dirU);
    const period = dom[1] - dom[0];
    if (!isPeriodicOverlapped(net, dirU) || nU < p + 1 || u < dom[0] || u >= dom[1] || knotMultiplicity(knots, u) >= p)
    {
        return net;
    }
    var extRows = rows;
    for (var i = n; i < n + p; i += 1)
    {
        extRows = append(extRows, rows[i - nU]);
    }
    var extKnots = knots;
    const nK = size(knots);
    for (var i = nK; i < nK + p; i += 1)
    {
        extKnots = append(extKnots, extKnots[i - nU] + period);
    }
    const k = findSpanIdx(size(extRows) - 1, p, u, extKnots);
    const res = insertKnotRows(extRows, extKnots, p, u);
    const nU2 = nU + 1;
    const i0 = max(0, k - nU + 1);
    var uniq = makeArray(nU2);
    for (var i = i0; i <= i0 + nU; i += 1)
    {
        uniq[i - floor(i / nU2) * nU2] = res.rows[i];
    }
    // One period of knots T'(0 .. nU2 - 1) with u placed after equal values (as Boehm places it).
    var tp = [];
    var placed = false;
    for (var j = 0; j < nU; j += 1)
    {
        const t = knots[p + j];
        if (!placed && u < t)
        {
            tp = append(tp, u);
            placed = true;
        }
        tp = append(tp, t);
    }
    if (!placed)
    {
        tp = append(tp, u);
    }
    var newKnots = makeArray(nU2 + 2 * p + 1);
    for (var i = 0; i < size(newKnots); i += 1)
    {
        const j = i - p;
        const q = floor(j / nU2);
        newKnots[i] = tp[j - q * nU2] + q * period;
    }
    var newRows = makeArray(nU2 + p);
    for (var i = 0; i < nU2 + p; i += 1)
    {
        newRows[i] = uniq[i - floor(i / nU2) * nU2];
    }
    return withRows(net, dirU, newRows, newKnots, p);
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

/** The net with its largest span of one direction bisected, or undefined when it has no span. */
function bisectLargestSpan(net is map, dirU is boolean)
{
    if (dirPeriodic(net, dirU) && !isPeriodicOverlapped(net, dirU))
    {
        return undefined;
    }
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    var best = -1;
    var bestLen = 0;
    for (var k = p; k < dirCount(net, dirU); k += 1)
    {
        if (knots[k + 1] - knots[k] > bestLen)
        {
            bestLen = knots[k + 1] - knots[k];
            best = k;
        }
    }
    if (best < 0)
    {
        return undefined;
    }
    return insertKnotDir(net, dirU, (knots[best] + knots[best + 1]) / 2);
}

/** Bisect the largest span until the direction has `target` spans (periodic directions in overlapped form too). */
function refineDir(net is map, dirU is boolean, target is number) returns map
{
    var out = net;
    while (spanCount(out, dirU) < target)
    {
        const next = bisectLargestSpan(out, dirU);
        if (next == undefined)
        {
            break;
        }
        out = next;
    }
    return out;
}

/** Bisect the largest span until the direction has at least `target` control points (periodic: counting the overlap). */
function refineToCount(net is map, dirU is boolean, target is number) returns map
{
    var out = net;
    while (dirCount(out, dirU) < target)
    {
        const next = bisectLargestSpan(out, dirU);
        if (next == undefined)
        {
            break;
        }
        out = next;
    }
    return out;
}

/** True when two homogeneous control rows are the same points and weights. */
function sameRow(rowA is array, rowB is array) returns boolean
{
    for (var j = 0; j < size(rowA); j += 1)
    {
        if (norm(rowA[j] - rowB[j]) > PULL_SAME_CP)
        {
            return false;
        }
    }
    return true;
}

/**
 * Number of independent control rows of a direction. A periodic net repeats its first p rows
 * at the end (rows k and k + result are one row); a clamped direction whose first and last
 * rows coincide (closed -- decided on the net, since face and surface parameters may be
 * swapped, as on a sphere) ties the last row to the first when closedDir. Otherwise every
 * row counts.
 */
function uniqueRowCount(net is map, dirU is boolean, closedDir is boolean) returns number
{
    const n = dirCount(net, dirU);
    const p = dirDegree(net, dirU);
    const rows = rowsOf(net, dirU);
    if (dirPeriodic(net, dirU))
    {
        const nUnique = n - p;
        if (nUnique < 2)
        {
            return n;
        }
        for (var k = 0; k < p; k += 1)
        {
            if (!sameRow(rows[k], rows[k + nUnique]))
            {
                return n;
            }
        }
        return nUnique;
    }
    if (closedDir && sameRow(rows[0], rows[n - 1]))
    {
        return n - 1;
    }
    return n;
}

/** Row k and its repeats (k + nUnique, ...) below n. */
function rowCopies(k is number, nUnique is number, n is number) returns array
{
    var out = [];
    for (var c = k; c < n; c += nUnique)
    {
        out = append(out, c);
    }
    return out;
}

/** The point control row k of a direction collapses to (a pole / apex), or undefined. */
function collapsedRowPoint(net is map, dirU is boolean, k is number)
{
    const row = rowsOf(net, dirU)[k];
    const p0 = vector(row[0][0], row[0][1], row[0][2]) / row[0][3];
    for (var h in row)
    {
        if (norm(vector(h[0], h[1], h[2]) / h[3] - p0) > PULL_SAME_POINT)
        {
            return undefined;
        }
    }
    return p0;
}

/**
 * Row lock mask of one direction. Non-periodic ends lock (order + 1) rows, plus
 * (p + 1 - end multiplicity) on an unclamped end. Interior knots of multiplicity >= p
 * (a C0 joint in parameter) lock the joint row and max(order, 1) rows either side, so
 * a geometric G1 joint stays G1. endLocks false (a closed face's tied seam) skips the ends.
 */
function lockMask(net is map, dirU is boolean, order is number, endLocks is boolean) returns array
{
    const p = dirDegree(net, dirU);
    const knots = dirKnots(net, dirU);
    const n = dirCount(net, dirU) - 1;
    var mask = makeArray(n + 1, false);
    if (!dirPeriodic(net, dirU) && endLocks)
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
    if (!dirPeriodic(net, dirU) && !endLocks)
    {
        // Clamped closed face: the tied seam row is a C0 knot joint like any other.
        for (var k = 0; k <= n; k += 1)
        {
            if (k <= reach || k >= n - reach)
            {
                mask[k] = true;
            }
        }
    }
    if (dirPeriodic(net, dirU))
    {
        // Periodic: every joint in [lo, hi] of the padded knots, the seam included, locks its
        // rows modulo the unique count (and their overlap copies).
        const nU = uniqueRowCount(net, dirU, false);
        const dom = dirDomain(net, dirU);
        var f = 1;
        while (f < size(knots))
        {
            var m = 1;
            while (f + m < size(knots) && knots[f + m] == knots[f])
            {
                m += 1;
            }
            if (m >= p && knots[f] >= dom[0] && knots[f] <= dom[1] && f - 1 <= n)
            {
                for (var k = f - 1 - reach; k <= f + m - p - 1 + reach; k += 1)
                {
                    for (var c = k - floor(k / nU) * nU; c <= n; c += nU)
                    {
                        mask[c] = true;
                    }
                }
            }
            f += m;
        }
        return mask;
    }
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
                "isUPeriodic" : net.uPer || net.uPerOut == true, "isVPeriodic" : net.vPer || net.vPerOut == true,
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

/**
 * Free control points: not locked, independent (below nUniqU / nUniqV) and, on a trimmed
 * face, inside the face. Each is { k, l, s, t (Greville), pt, ab (face parameter), normal }.
 */
function freeCandidates(context is Context, face is Query, net is map, lockU is array, lockV is array, nUniqU is number, nUniqV is number,
    trimmed is boolean, approxTol is number) returns array
{
    const gU = greville(net, true);
    const gV = greville(net, false);
    const isPlanar = !isQueryEmpty(context, qGeometry(face, GeometryType.PLANE));
    var planeFrame = undefined;
    if (isPlanar)
    {
        const fr = evFaceTangentPlanes(context, { "face" : face, "parameters" : [vector(0, 0), vector(1, 0), vector(0, 1)] });
        planeFrame = { "o" : fr[0].origin / meter, "ea" : (fr[1].origin - fr[0].origin) / meter, "eb" : (fr[2].origin - fr[0].origin) / meter };
    }
    const outsideTol = 10 * approxTol + 1e-7;
    var cands = [];
    for (var k = 0; k < nUniqU; k += 1)
    {
        for (var l = 0; l < nUniqV; l += 1)
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
                if (trimmed)
                {
                    inside = evDistance(context, { "side0" : face, "side1" : e.pt * meter }).distance <= outsideTol * meter;
                }
            }
            else
            {
                const dr = evDistance(context, { "side0" : face, "side1" : e.pt * meter });
                const par = dr.sides[0].parameter;
                ab = [par[0], par[1]];
                if (trimmed)
                {
                    inside = dr.distance <= outsideTol * meter;
                }
            }
            if (!inside)
            {
                continue;
            }
            ab = [min(max(ab[0], 0), 1), min(max(ab[1], 0), 1)];
            cands = append(cands, { "k" : k, "l" : l, "s" : gU[k], "t" : gV[l], "pt" : e.pt, "ab" : ab });
        }
    }
    if (size(cands) > 0)
    {
        var candParams = [];
        for (var c in cands)
        {
            candParams = append(candParams, vector(c.ab[0], c.ab[1]));
        }
        const candPlanes = evFaceTangentPlanes(context, { "face" : face, "parameters" : candParams });
        for (var ci = 0; ci < size(cands); ci += 1)
        {
            cands[ci].normal = candPlanes[ci].normal;
        }
    }
    return cands;
}

/** Moves control point (k, l) and its periodic / seam copies by disp (meters) along normal; weights unchanged. */
function shiftControlPoint(net is map, k is number, l is number, nUniqU is number, nUniqV is number, disp is number, normal is Vector) returns map
{
    var out = net;
    for (var kk in rowCopies(k, nUniqU, size(net.hom)))
    {
        for (var ll in rowCopies(l, nUniqV, size(net.hom[0])))
        {
            const h = out.hom[kk][ll];
            const shift = h[3] * disp * normal;
            out.hom[kk][ll] = vector([h[0] + shift[0], h[1] + shift[1], h[2] + shift[2], h[3]]);
        }
    }
    return out;
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
 * manipulator drag and would zero the offsets mid-drag. (Evaluating it for closed
 * directions is fine.) In control-point mode the handle layout depends on the face's net,
 * so only zero / negative items are dropped here; the body ignores the rest (with a note).
 */
export function pullSurfaceEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    const uCount = definition.uCurveCount;
    const vCount = definition.vCurveCount;
    const cpMode = definition.handlePerControlPoint == true;
    if (definition.uCurveCount != oldDefinition.uCurveCount ||
        definition.vCurveCount != oldDefinition.vCurveCount ||
        definition.continuityType != oldDefinition.continuityType ||
        cpMode != (oldDefinition.handlePerControlPoint == true))
    {
        definition.mpOffsets = makeArray(uCount * vCount, { "off" : 0 * meter });
        definition.activeOffsets = [];
        return definition;
    }
    const closed = cpMode ? [false, false] : faceClosedDirs(context, definition.face);
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
            if (item.u < 0 || item.v < 0)
            {
                continue;
            }
            if (!cpMode && (item.u >= uCount || item.v >= vCount))
            {
                continue;
            }
            if (!cpMode && isHandleLocked(item.u, item.v, uCount, vCount, definition.continuityType, closed[0], closed[1]))
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
        // Control-point handles: "cp_<k>_<l>" (net indices, not bounded by the curve counts).
        const cpKey = match(key, "cp_([0-9]+)_([0-9]+)");
        if (cpKey.hasMatch)
        {
            definition.activeOffsets = setOffsetItem(definition.activeOffsets, stringToNumber(cpKey.captures[1]),
                stringToNumber(cpKey.captures[2]), manip.offset);
            continue;
        }
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                if (("mp_" ~ i ~ "_" ~ j) == key)
                {
                    definition.activeOffsets = setOffsetItem(definition.activeOffsets, i, j, manip.offset);
                }
            }
        }
    }
    definition.mpOffsets = flatOffsets(definition.activeOffsets == undefined ? [] : definition.activeOffsets, uCount, vCount);
    return definition;
}

// -- Feature ----------------------------------------------------------------------------------

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Pull surface",
        "Editing Logic Function" : "pullSurfaceEditingLogic",
        "Manipulator Change Function" : "pullSurfaceManipulator",
        "Feature Type Description" : "Creates a copy of a face pushed and pulled along its normal by a grid of handles (or one handle per control point), or replaces the face in its body. The edges keep their position (G0), tangent plane (G1) or curvature (G2) along their whole length; the face's control net is edited, not refitted." }
export const pullSurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1,
                    "Description" : "The face to pull. It is left unchanged (the result is a new surface) unless Replace face is on." }
        definition.face is Query;

        annotation { "Name" : "Replace face", "Default" : false,
                    "Description" : "Put the pulled surface in place of the face in its own body (solid or surface) instead of creating a new surface. A solid stays solid when the kernel can re-fit the neighbouring faces; a one-face surface is replaced by the pulled surface as a whole (new body)." }
        definition.replaceFace is boolean;

        annotation { "Name" : "U curve count", "Description" : "Handles across the face's first parameter direction, edges included. With one handle per control point: the minimum number of control points in that direction." }
        isInteger(definition.uCurveCount, { (unitless) : [2, 4, 20] } as IntegerBoundSpec);

        annotation { "Name" : "V curve count", "Description" : "Handles across the face's second parameter direction, edges included. With one handle per control point: the minimum number of control points in that direction." }
        isInteger(definition.vCurveCount, { (unitless) : [2, 4, 20] } as IntegerBoundSpec);

        annotation { "Name" : "Continuity", "UIHint" : [UIHint.SHOW_LABEL],
                    "Description" : "What the edges keep: position, tangency or curvature. Handles on the edges (and one / two rows in for tangency / curvature) are fixed, so tangency needs 5 curves each way and curvature 7 before a handle is free. A closed direction (full cylinder) has no edge and fixes nothing; a pole is fixed like an edge." }
        definition.continuityType is GeometricContinuity;

        annotation { "Name" : "One handle per control point", "Default" : false,
                    "Description" : "A handle at every free control point (at its Greville point) instead of the U / V grid; an offset moves that control point along the normal. U / V curve count set the minimum number of control points (periodic directions keep theirs)." }
        definition.handlePerControlPoint is boolean;

        // Live list of non-zero handle offsets, filled when a manipulator is dragged.
        // U and V are grid indices (control-point indices in control-point mode); Offset is along the face normal.
        annotation { "Name" : "Active offsets", "Item name" : "Offset",
                    "Description" : "Handle offsets. Drag a manipulator to add one; set Offset to 0 or delete the item to remove it." }
        definition.activeOffsets is array;
        for (var activeOffset in definition.activeOffsets)
        {
            annotation { "Name" : "U" }
            isInteger(activeOffset.u, { (unitless) : [0, 0, 999] } as IntegerBoundSpec);

            annotation { "Name" : "V" }
            isInteger(activeOffset.v, { (unitless) : [0, 0, 999] } as IntegerBoundSpec);

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
        const cpMode = definition.handlePerControlPoint;
        var notes = [];

        if (definition.replaceFace)
        {
            if (!isQueryEmpty(context, qSketchFilter(definition.face, SketchObject.YES)) ||
                isQueryEmpty(context, qModifiableEntityFilter(definition.face)))
            {
                throw regenError("Replace face cannot modify this face (a sketch region, or a face this feature may not change). Turn Replace face off to create a new surface.",
                    ["replaceFace"], definition.face);
            }
            if (isQueryEmpty(context, qBodyType(qOwnerBody(definition.face), [BodyType.SOLID, BodyType.SHEET])))
            {
                throw regenError("Replace face needs a face of a solid or a surface body.", ["face"], definition.face);
            }
        }

        // Directions in which the face meets itself (full cylinder / revolved face).
        const closed = faceClosedDirs(context, definition.face);

        // -- Grid handles: base points and normals on the face's parameter grid --
        var basePlanes = [];
        var handleOffset = makeArray(total, 0 * meter);
        var freeIndex = makeArray(total, -1);
        var freeHandles = [];
        var anyOffset = false;
        var targets = [];
        if (!cpMode)
        {
            var gridParams = [];
            for (var i = 0; i < uCount; i += 1)
            {
                for (var j = 0; j < vCount; j += 1)
                {
                    gridParams = append(gridParams, vector(gridParam(i, uCount, closed[0]), gridParam(j, vCount, closed[1])));
                }
            }
            basePlanes = evFaceTangentPlanes(context, { "face" : definition.face, "parameters" : gridParams });

            const offsets = definition.mpOffsets;
            const haveOffsets = offsets != undefined && size(offsets) == total;
            var manipMap = {};
            for (var i = 0; i < uCount; i += 1)
            {
                for (var j = 0; j < vCount; j += 1)
                {
                    const flat = i * vCount + j;
                    if (isHandleLocked(i, j, uCount, vCount, definition.continuityType, closed[0], closed[1]))
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

            targets = makeArray(total);
            for (var flat = 0; flat < total; flat += 1)
            {
                targets[flat] = basePlanes[flat].origin + handleOffset[flat] * basePlanes[flat].normal;
            }
        }

        // -- Source control net --
        const approxTol = min(max(definition.fitTolerance / meter / 10, 1e-8), 1e-4);
        var src = evApproximateBSplineSurface(context, { "face" : definition.face, "tolerance" : approxTol });
        var net = normalizeClampedPeriodic(netFromSurface(src.bSplineSurface));
        if ((net.uDeg < 2 && !isSingleSpan(net, true)) || (net.vDeg < 2 && !isSingleSpan(net, false)))
        {
            src = evApproximateBSplineSurface(context, { "face" : definition.face, "tolerance" : approxTol, "forceCubic" : true });
            net = normalizeClampedPeriodic(netFromSurface(src.bSplineSurface));
        }
        const targetDeg = min(max(definition.curveDegree, 2), PULL_MAX_DEGREE);
        for (var dirU in [true, false])
        {
            if (dirDegree(net, dirU) < targetDeg && isSingleSpan(net, dirU))
            {
                net = elevateDir(net, dirU, targetDeg);
            }
        }

        // -- Poles: a collapsed end row that the face reaches (sphere cap, cone apex) --
        // The face's boundary never reaches a pole, so the clip box is extended to it.
        var poles = [[false, false], [false, false]];
        for (var d = 0; d < 2; d += 1)
        {
            const dirU = (d == 0);
            if (dirPeriodic(net, dirU))
            {
                continue;
            }
            for (var endIdx = 0; endIdx < 2; endIdx += 1)
            {
                const pp = collapsedRowPoint(net, dirU, (endIdx == 0) ? 0 : dirCount(net, dirU) - 1);
                if (pp != undefined && evDistance(context, { "side0" : definition.face, "side1" : pp * meter }).distance < 10 * PULL_SAME_POINT * meter)
                {
                    poles[d][endIdx] = true;
                }
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
                const lo = poles[d][0] ? dom[0] : max(tb.lo[d], dom[0]);
                const hi = poles[d][1] ? dom[1] : min(tb.hi[d], dom[1]);
                if (hi - lo <= eps)
                {
                    throw regenError("Pull surface cannot read this face: its boundary spans no parameter range in one direction (a pole or degenerate face it cannot resolve).",
                        ["face"], definition.face);
                }
                const partial = lo > dom[0] + eps || hi < dom[1] - eps;
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
                    net = clipDir(net, dirU, lo, hi);
                }
            }
            if (needTrims)
            {
                trims = boundary;
            }
        }
        if (!definition.replaceFace && src.innerLoopBSplineCurves != undefined && size(src.innerLoopBSplineCurves) > 0)
        {
            notes = append(notes, "Holes in the face are not kept; the result covers the outer boundary.");
        }

        var solveInfo = "no offsets";
        var lockU = undefined;
        var lockV = undefined;
        var cpHandles = [];
        if (cpMode)
        {
            // -- One handle per free control point; its offset moves that control point --
            for (var dirU in [true, false])
            {
                // A periodic direction counts its unique control points (the overlap adds p).
                const wanted = dirU ? uCount : vCount;
                net = refineToCount(net, dirU, dirPeriodic(net, dirU) ? wanted + dirDegree(net, dirU) : wanted);
            }
            const nUniqU = uniqueRowCount(net, true, true);
            const nUniqV = uniqueRowCount(net, false, true);
            lockU = lockMask(net, true, order, nUniqU == size(net.hom));
            lockV = lockMask(net, false, order, nUniqV == size(net.hom[0]));
            const cands = freeCandidates(context, definition.face, net, lockU, lockV, nUniqU, nUniqV, size(trims) > 0, approxTol);

            var offsetOf = {};
            for (var item in (definition.activeOffsets == undefined ? [] : definition.activeOffsets))
            {
                offsetOf[item.u ~ "_" ~ item.v] = item.value;
            }
            var manipMap = {};
            var used = 0;
            for (var c in cands)
            {
                const key = c.k ~ "_" ~ c.l;
                const off = (offsetOf[key] == undefined) ? 0 * meter : offsetOf[key];
                manipMap["cp_" ~ key] = linearManipulator({ "base" : c.pt * meter, "direction" : c.normal, "offset" : off });
                cpHandles = append(cpHandles, { "k" : c.k, "l" : c.l, "base" : c.pt * meter, "normal" : c.normal, "off" : off });
                if (off != 0 * meter)
                {
                    used += 1;
                    anyOffset = true;
                    net = shiftControlPoint(net, c.k, c.l, nUniqU, nUniqV, off / meter, c.normal);
                }
            }
            addManipulators(context, id, manipMap);
            var requested = 0;
            for (var key, value in offsetOf)
            {
                if (value != 0 * meter)
                {
                    requested += 1;
                }
            }
            if (size(cands) == 0)
            {
                notes = append(notes, "No control point is free: raise U / V curve count (tangency needs 5, curvature 7). The surface is an unchanged copy.");
            }
            if (requested > used)
            {
                notes = append(notes, (requested - used) ~ " offsets are not on a free control point and were ignored (the face, curve counts or continuity changed the net).");
            }
            solveInfo = "control-point mode, " ~ size(cands) ~ " free control points, " ~ used ~ " moved";
        }
        else if (anyOffset)
        {
            // -- Refine (exact knot insertion) so the handle grid is resolved --
            const spansTarget = max(PULL_SPANS_PER_HANDLE * (max(uCount, vCount) - 1), 2 * (order + 1) + 4);
            for (var dirU in [true, false])
            {
                net = refineDir(net, dirU, spansTarget);
            }

            // -- Locked rows and candidate control points --
            const nUniqU = uniqueRowCount(net, true, true);
            const nUniqV = uniqueRowCount(net, false, true);
            lockU = lockMask(net, true, order, nUniqU == size(net.hom));
            lockV = lockMask(net, false, order, nUniqV == size(net.hom[0]));
            const nu = size(net.hom);
            const nv = size(net.hom[0]);
            var cands = freeCandidates(context, definition.face, net, lockU, lockV, nUniqU, nUniqV, size(trims) > 0, approxTol);

            if (size(cands) == 0)
            {
                notes = append(notes, "Every control point is held by the edges; add knots (raise the curve counts) to free some. The surface is an unchanged copy.");
                solveInfo = "no free control points";
            }
            else
            {
                // Field weights of each control point over the FREE handles; periodic / seam
                // copies of a control point share its candidate.
                var candIndex = {};
                for (var ci = 0; ci < size(cands); ci += 1)
                {
                    cands[ci].weights = fieldWeights(cands[ci].ab, uCount, vCount, closed, freeIndex);
                    for (var kk in rowCopies(cands[ci].k, nUniqU, nu))
                    {
                        for (var ll in rowCopies(cands[ci].l, nUniqV, nv))
                        {
                            candIndex[kk ~ "_" ~ ll] = ci;
                        }
                    }
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
                // A handle with no (or no finite) response makes the matrix singular: fall back
                // instead of inverting (matrixInverse fails on NaN).
                var solveOk = true;
                for (var h = 0; h < m; h += 1)
                {
                    var rowMax = 0;
                    for (var x in respRows[h])
                    {
                        if (!(abs(x) < inf))
                        {
                            rowMax = -1;
                            break;
                        }
                        rowMax = max(rowMax, abs(x));
                    }
                    if (!(rowMax > 1e-9))
                    {
                        solveOk = false;
                    }
                }
                if (solveOk)
                {
                    // Near-singular (two handles with the same response): condition check by SVD.
                    const sv = svd(matrix(respRows)).s;
                    var sMin = inf;
                    var sMax = 0;
                    for (var h = 0; h < m; h += 1)
                    {
                        sMin = min(sMin, abs(sv[h][h]));
                        sMax = max(sMax, abs(sv[h][h]));
                    }
                    solveOk = sMin > 1e-10 * sMax;
                }
                if (solveOk)
                {
                    const solved = inverse(matrix(respRows)) * matrix(rhs);
                    for (var h = 0; h < m; h += 1)
                    {
                        const gh = solved[h][0];
                        if (!(abs(gh) <= PULL_SOLVE_LIMIT * dMax))
                        {
                            solveOk = false;
                        }
                        g[h] = gh;
                    }
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

                // -- Move the free control points (and their copies) along the normal --
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
                    net = shiftControlPoint(net, c.k, c.l, nUniqU, nUniqV, disp, c.normal);
                }
            }
        }
        if (definition.printCurveData && lockU != undefined)
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
            println("[pullSurface] locks u " ~ lu ~ "  v " ~ lv ~ "; closed " ~ closed[0] ~ "/" ~ closed[1]
                    ~ "; poles u " ~ poles[0][0] ~ "/" ~ poles[0][1] ~ " v " ~ poles[1][0] ~ "/" ~ poles[1][1]);
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
        // Control-point handles by "k_l", for the debug grid.
        var cpTarget = {};
        for (var hd in cpHandles)
        {
            cpTarget[hd.k ~ "_" ~ hd.l] = hd.base + hd.off * hd.normal;
        }
        if (definition.showCPPolygons)
        {
            if (!cpMode)
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
            else
            {
                for (var hd in cpHandles)
                {
                    const tp = cpTarget[hd.k ~ "_" ~ hd.l];
                    addDebugPoint(context, tp, DebugColor.BLUE);
                    const nextL = cpTarget[hd.k ~ "_" ~ (hd.l + 1)];
                    if (nextL != undefined)
                    {
                        addDebugLine(context, tp, nextL, DebugColor.CYAN);
                    }
                    const nextK = cpTarget[(hd.k + 1) ~ "_" ~ hd.l];
                    if (nextK != undefined)
                    {
                        addDebugLine(context, tp, nextK, DebugColor.MAGENTA);
                    }
                }
            }
        }
        if (definition.showOffsetVectors)
        {
            if (!cpMode)
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
            else
            {
                for (var hd in cpHandles)
                {
                    if (hd.off != 0 * meter)
                    {
                        addDebugLine(context, hd.base, hd.base + hd.off * hd.normal, (hd.off > 0 * meter) ? DebugColor.GREEN : DebugColor.RED);
                    }
                }
            }
        }

        // -- Optional helper bodies (not part of the output) --
        if (definition.keepPoints)
        {
            if (!cpMode)
            {
                for (var i = 0; i < uCount; i += 1)
                {
                    for (var j = 0; j < vCount; j += 1)
                    {
                        opPoint(context, id + ("pt_" ~ i ~ "_" ~ j), { "point" : targets[i * vCount + j] });
                    }
                }
            }
            else
            {
                for (var hd in cpHandles)
                {
                    opPoint(context, id + ("pt_" ~ hd.k ~ "_" ~ hd.l), { "point" : cpTarget[hd.k ~ "_" ~ hd.l] });
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

        // -- Replace face: the pulled sheet takes the face's place in its body, then goes --
        var output = qCreatedBy(id + "pullSurf", EntityType.BODY);
        var outputDescription = "The pulled surface";
        const sourceBody = qOwnerBody(definition.face);
        const singleFaceSheet = definition.replaceFace && !isQueryEmpty(context, qBodyType(sourceBody, BodyType.SHEET)) &&
            size(evaluateQuery(context, qOwnedByBody(sourceBody, EntityType.FACE))) == 1;
        if (singleFaceSheet)
        {
            // The kernel refuses opReplaceFace on a one-face sheet (DIRECT_EDIT_REPLACE_FACE_FAILED):
            // replacing its only face IS replacing the body, so the source sheet is deleted and
            // the pulled sheet is the result (new body identity; downstream picks re-pick).
            opDeleteBodies(context, id + "deleteSource", { "entities" : sourceBody });
            outputDescription = "The pulled surface, replacing the source sheet";
        }
        else if (definition.replaceFace)
        {
            const templateFace = qCreatedBy(id + "pullSurf", EntityType.FACE);
            // Sense auto-detected (corrections log: opReplaceFace oppositeSense).
            const srcPlane = evFaceTangentPlane(context, { "face" : definition.face, "parameter" : vector(0.5, 0.5) });
            const near = evDistance(context, { "side0" : templateFace, "side1" : srcPlane.origin });
            const tplPlane = evFaceTangentPlane(context, { "face" : templateFace, "parameter" : near.sides[0].parameter });
            opReplaceFace(context, id + "replaceFace", {
                        "replaceFaces" : definition.face,
                        "templateFace" : templateFace,
                        "oppositeSense" : dot(srcPlane.normal, tplPlane.normal) < 0
                    });
            opDeleteBodies(context, id + "deleteTemplate", { "entities" : qCreatedBy(id + "pullSurf", EntityType.BODY) });
            output = qOwnerBody(definition.face);
            outputDescription = "The body whose face was replaced by the pulled surface";
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
                    "output" : output,
                    "outputDescription" : outputDescription,
                    "inputs" : definition.face
                });
    }, { "activeOffsets" : [], "mpOffsets" : [], "showIsoCurves" : false, "keepUCurves" : false, "keepVCurves" : false,
            "keepPoints" : false, "showCPPolygons" : false, "showOffsetVectors" : false, "printCurveData" : false,
            "curveDegree" : 3, "fitTolerance" : 1e-6 * meter, "replaceFace" : false, "handlePerControlPoint" : false });
