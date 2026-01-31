FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");



/** =========================
 *  Basic utilities
 *  ========================= */

export function assertTrue(cond is boolean, msg is string)
{
    if (!cond)
        throw regenError(msg);
}

export function safeSign(x is number, eps is number) returns number
{
    if (x > eps) return 1;
    if (x < -eps) return -1;
    return 0;
}

export function clamp01(u is number) returns number
{
    if (u < 0) return 0;
    if (u > 1) return 1;
    return u;
}

/** =========================
 *  Numerical integration
 *  ========================= */

/**
 * Cumulative trapezoidal integration.
 * x and y must be same size. Returns map { cumulative: array, total: ValueWithUnits|number }.
 * Works with y values that have units (e.g. curvature 1/m, slope unitless, etc.).
 */
export function cumTrapz(x is array, y is array, initialValue) returns map
{
    assertTrue(size(x) == size(y), "cumTrapz: x and y arrays must be same size.");
    assertTrue(size(x) >= 1, "cumTrapz: arrays must be non-empty.");

    var cumulative = [initialValue];
    var total = initialValue;

    for (var i = 1; i < size(y); i += 1)
    {
        var dx = x[i] - x[i - 1];
        var trapVal = dx * (y[i] + y[i - 1]) * 0.5;
        total += trapVal;
        cumulative = append(cumulative, total);
    }

    return { "cumulative" : cumulative, "total" : total };
}

/**
 * Optional: lightweight moving average for numeric or unit-valued arrays.
 * window should be odd and >= 1. Endpoints use a smaller effective window.
 */
export function movingAverage(y is array, window is number) returns array
{
    assertTrue(window >= 1, "movingAverage: window must be >= 1.");
    if (window == 1) return y;

    // force odd window for symmetry
    if (window % 2 == 0) window += 1;
    var half = floor(window / 2);

    var out = [];
    for (var i = 0; i < size(y); i += 1)
    {
        var a = max([0, i - half]);
        var b = min([size(y) - 1, i + half]);

        var s = y[a];
        var n = 1;
        for (var j = a + 1; j <= b; j += 1)
        {
            s += y[j];
            n += 1;
        }
        out = append(out, s / n);
    }
    return out;
}



/** =========================
 *  Root finding (hybrid)
 *  ========================= */

/**
 * Find a bracket [a,b] where f changes sign, scanning an array of sample points (u, f).
 * samples: array of maps {u: number, f: number}
 * returns map {found: boolean, a: number, b: number, fa: number, fb: number}
 */
export function bracketFromSamples(samples is array) returns map
{
    if (size(samples) < 2)
        return { "found" : false };

    for (var i = 0; i < size(samples) - 1; i += 1)
    {
        var fa = samples[i].f;
        var fb = samples[i + 1].f;
        if (fa == 0)
            return { "found" : true, "a" : samples[i].u, "b" : samples[i].u, "fa" : fa, "fb" : fa };
        if (fa * fb < 0)
            return { "found" : true, "a" : samples[i].u, "b" : samples[i + 1].u, "fa" : fa, "fb" : fb };
    }
    return { "found" : false };
}

/**
 * Hybrid secant + bisection solver on [a,b] for f(u)=0.
 * Requires an initial sign-change bracket unless a==b (exact root).
 * f is a function(u)->number.
 */
export function solveRootHybrid(f, a is number, b is number, tol is number, maxIter is number) returns map
{
    var fa = f(a);
    var fb = f(b);

    if (abs(fa) <= tol) return { "u" : a, "f" : fa, "iters" : 0 };
    if (abs(fb) <= tol) return { "u" : b, "f" : fb, "iters" : 0 };

    // Require sign change
    assertTrue(fa * fb < 0, "solveRootHybrid: bracket does not contain a sign change.");

    var lo = a; var hi = b;
    var flo = fa; var fhi = fb;

    var u = (lo + hi) * 0.5;
    var fu = f(u);

    for (var i = 1; i <= maxIter; i += 1)
    {
        // secant step
        var denom = (fhi - flo);
        var uSec = (abs(denom) < 1e-18) ? u : (hi - fhi * (hi - lo) / denom);

        // keep inside bracket; otherwise bisection
        if (uSec <= min([lo, hi]) || uSec >= max([lo, hi]))
            uSec = (lo + hi) * 0.5;

        u = uSec;
        fu = f(u);

        if (abs(fu) <= tol)
            return { "u" : u, "f" : fu, "iters" : i };

        // Update bracket
        if (flo * fu < 0)
        {
            hi = u; fhi = fu;
        }
        else
        {
            lo = u; flo = fu;
        }

        // Also stop if interval is tiny
        if (abs(hi - lo) <= 1e-12)
            return { "u" : u, "f" : fu, "iters" : i };
    }

    return { "u" : u, "f" : fu, "iters" : maxIter };
}

/**
 * Combines results from evPathTangentLines and evEdgeCurvature. Provides curvature data for paths.
 *
 * - Uses evPathTangentLines for path-aligned tangent directions (already accounts for path.flipped)
 * - Uses evDistance(edge, point) to recover the closest edge parameter
 * - Computes SIGNED curvature for a planar XY footprint using binormal vs world +Z
 * - Ensures sign is consistent with PATH traversal direction (via dot(tEdge, tPath))
 */
export function evPathCurvatures(context is Context, path is Path, params is array) returns map
{
    var retMap = {
        "edges" : path.edges,
        "flipped" : path.flipped,
        "closed" : path.closed,
        "evalParams" : params
    };

    var pathTangentLines = evPathTangentLines(context, path, params);
    var tangentLines = pathTangentLines.tangentLines;
    var edgeIndices = pathTangentLines.edgeIndices;

    var points = [];
    var tangents = [];
    var edgeParams = [];

    // Raw curvature results (magnitude + frame)
    var curvatures = [];

    // Signed curvature (ValueWithUnits) and sign scalar (unitless)
    var curvatureSigned = [];
    var curvatureSign = [];
    var curvatureMag = [];

    // World plane normal (XY footprint)
    var worldZ = vector(0, 0, 1);

    for (var i = 0; i < size(params); i += 1)
    {
        var tangentLine = tangentLines[i];
        var edge = path.edges[edgeIndices[i]];

        var p = tangentLine.origin;
        var tPath = normalize(tangentLine.direction); // path-aligned by evPathTangentLines

        points = append(points, p);
        tangents = append(tangents, tPath);

        // Closest parameter on the underlying edge
        var pointDist = evDistance(context, {
            "side0" : edge,
            "side1" : p
        });
        var uEdge = pointDist.sides[0].parameter;
        edgeParams = append(edgeParams, uEdge);

        // Curvature magnitude + Frenet frame at that edge parameter
        var cr = evEdgeCurvature(context, {
            "edge" : edge,
            "parameter" : uEdge
        });
        curvatures = append(curvatures, cr);

        // --- Signed curvature logic (planar) ---
        // Onshape EdgeCurvatureResult frame:
        //   zAxis = tangent, xAxis = normal, yAxis = binormal
        var b = normalize(cr.frame.yAxis); // binormal

        // Base sign from binormal relative to world +Z
        var sgn = safeSign(dot(b, worldZ), 1e-9);

        // Ensure sign is consistent with PATH traversal direction:
        // If edge's parameterization tangent opposes the path tangent, reverse sign.
        var tEdge = normalize(evEdgeTangentLine(context, { "edge" : edge, "parameter" : uEdge }).direction);
        if (dot(tEdge, tPath) < 0)
        {
            sgn = -sgn;
        }

        // Optional guard near inflection (avoid noisy sign when curvature ~ 0)
        var kMag = cr.curvature;
        var kSigned;
        if (abs(kMag.value) < 1e-12)
        {
            sgn = 0;
            kSigned = 0 * kMag;
        }
        else
        {
            kSigned = (sgn == 0) ? (0 * kMag) : (sgn * kMag);
        }
        
        curvatureMag = append(curvatureMag, kMag);
        curvatureSign = append(curvatureSign, sgn);
        curvatureSigned = append(curvatureSigned, kSigned);
    }

    retMap["points"] = points;
    retMap["tangents"] = tangents;
    retMap["edgeParams"] = edgeParams;
    retMap["curvatures"] = curvatures;

    retMap["edgeIndices"] = edgeIndices;
    retMap["curvatureSign"] = curvatureSign;
    retMap["curvatureSigned"] = curvatureSigned;
    retMap["curvatureMag"] = curvatureMag;

    return retMap;
}





