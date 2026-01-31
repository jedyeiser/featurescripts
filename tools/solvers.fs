FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Phase 1 dependencies
import(path : "assertions.fs", version : "");
import(path : "math_utils.fs", version : "");

/**
 * Numerical root-finding and solver utilities.
 *
 * Provides robust hybrid secant/bisection solver for finding zeros of
 * scalar functions. The hybrid approach combines fast convergence of
 * the secant method with guaranteed convergence of bisection.
 *
 * @source footprint/fpt_math.fs:94-171
 */

/**
 * Find a sign-change bracket from sampled function values.
 *
 * Scans through an array of (u, f) sample points to find the first
 * interval where f changes sign, indicating a root exists in that bracket.
 * Returns immediately if any sample has f exactly equal to zero.
 *
 * @param samples {array} : Array of maps {u: number, f: number}
 *                          Must be sorted by u value
 * @returns {map} : {found: boolean, a: number, b: number, fa: number, fb: number}
 *                  If found=true: [a,b] is bracket with sign change
 *                  If found=false: no sign change detected in samples
 *
 * @example Find bracket for f(x) = x - 5:
 *   `bracketFromSamples([{u:0, f:-5}, {u:3, f:-2}, {u:7, f:2}])`
 *   returns `{found: true, a: 3, b: 7, fa: -2, fb: 2}`
 *
 * @note Returns immediately if f=0 found at any sample point
 * @note Requires at least 2 samples; returns {found: false} otherwise
 *
 * @source footprint/fpt_math.fs:100-115
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
 * Hybrid secant + bisection root solver.
 *
 * Solves f(u) = 0 on interval [a, b] using a hybrid method that combines:
 * - Secant method: Fast quadratic convergence when behaving well
 * - Bisection fallback: Guaranteed convergence when secant diverges
 *
 * Requires initial bracket [a, b] where f(a) and f(b) have opposite signs
 * (unless a==b, indicating exact root already found).
 *
 * Algorithm:
 * 1. Try secant step: u_new = b - f(b) * (b-a) / (f(b)-f(a))
 * 2. If secant step goes outside bracket, use bisection instead
 * 3. Update bracket to maintain sign change
 * 4. Stop when |f(u)| < tol or bracket width < 1e-12
 *
 * @param f {function} : Function to find root of (signature: f(number) returns number)
 * @param a {number} : Left bracket endpoint
 * @param b {number} : Right bracket endpoint
 * @param tol {number} : Function value tolerance (stop when |f| < tol)
 * @param maxIter {number} : Maximum iterations before returning best estimate
 * @returns {map} : {u: number, f: number, iters: number}
 *                  u = root location, f = f(u), iters = iterations used
 *
 * @example Solve x - 5 = 0:
 *   `solveRootHybrid(function(x) { return x - 5; }, 0, 10, 1e-6, 50)`
 *   returns `{u: 5.0, f: ~0, iters: <small number>}`
 *
 * @note Requires sign change: f(a) * f(b) < 0 (unless |f(a)| or |f(b)| already < tol)
 * @note Falls back to bisection when secant diverges or goes out of bounds
 * @note Also stops if bracket width < 1e-12 even if |f| > tol
 *
 * @source footprint/fpt_math.fs:122-171
 */
export function solveRootHybrid(f, a is number, b is number, tol is number, maxIter is number) returns map
{
    var fa = f(a);
    var fb = f(b);

    if (abs(fa) <= tol) return { "u" : a, "f" : fa, "iters" : 0 };
    if (abs(fb) <= tol) return { "u" : b, "f" : fb, "iters" : 0 };

    // Require sign change
    assertTrue(fa * fb < 0, "solveRootHybrid: bracket does not contain a sign change.");

    var lo = a;
    var hi = b;
    var flo = fa;
    var fhi = fb;

    var u = (lo + hi) * 0.5;
    var fu = f(u);

    for (var i = 1; i <= maxIter; i += 1)
    {
        // Secant step
        var denom = (fhi - flo);
        var uSec = (abs(denom) < 1e-18) ? u : (hi - fhi * (hi - lo) / denom);

        // Keep inside bracket; otherwise bisection
        if (uSec <= min([lo, hi]) || uSec >= max([lo, hi]))
            uSec = (lo + hi) * 0.5;

        u = uSec;
        fu = f(u);

        if (abs(fu) <= tol)
            return { "u" : u, "f" : fu, "iters" : i };

        // Update bracket
        if (flo * fu < 0)
        {
            hi = u;
            fhi = fu;
        }
        else
        {
            lo = u;
            flo = fu;
        }

        // Also stop if interval is tiny
        if (abs(hi - lo) <= 1e-12)
            return { "u" : u, "f" : fu, "iters" : i };
    }

    return { "u" : u, "f" : fu, "iters" : maxIter };
}
