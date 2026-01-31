FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Phase 1 dependencies
import(path : "assertions.fs", version : "");
import(path : "math_utils.fs", version : "");

/**
 * ROOT FINDING & SOLVER METHODS
 * =============================
 *
 * Provides robust numerical methods for finding roots of scalar and
 * vector functions. All bracketed methods guarantee convergence;
 * Newton methods offer faster convergence when derivatives are available.
 *
 * | Method              | Convergence | Needs Derivative | Best For                |
 * |---------------------|-------------|------------------|-------------------------|
 * | solveRootHybrid()   | ~Quadratic  | No               | General 1D root finding |
 * | solveRootBrent()    | Superlinear | No               | Difficult brackets      |
 * | solveRootNewton()   | Quadratic   | Yes              | When derivative cheap   |
 * | newtonND()          | Quadratic   | Yes (Jacobian)   | Multi-dimensional       |
 *
 * WHEN TO USE WHICH:
 * - Default: solveRootHybrid() - robust, fast, no derivatives needed
 * - Flat regions: solveRootBrent() - better when f'(x) near 0 in bracket
 * - High precision: solveRootNewton() - if you have analytical derivative
 * - Surfaces/constraints: newtonND() - intersection, multi-constraint problems
 *
 * CONVERGENCE NOTES:
 * - All methods reach machine precision in 10-20 iterations for typical problems
 * - Bracketed methods (Hybrid, Brent) never fail if bracket is valid
 * - Newton methods may fail without good initial guess
 *
 * TYPICAL TOLERANCES:
 * - Parameter tolerance: 1e-9 to 1e-12 (for curve parameters)
 * - Function tolerance: 1e-10 to 1e-14 (depends on problem scale)
 * - Geometric tolerance: TOLERANCE.zeroLength (~1e-7 meter)
 *
 * @source footprint/fpt_math.fs:94-171
 */

// =============================================================================
// BRACKETING UTILITIES
// =============================================================================

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

// =============================================================================
// BRENT'S METHOD
// =============================================================================

/**
 * Brent's method root solver.
 *
 * Combines inverse quadratic interpolation, secant method, and bisection
 * for reliable and fast convergence. This is the industry standard for
 * bracketed root finding - never slower than bisection, often much faster.
 *
 * Advantages over solveRootHybrid:
 * - Better handling of functions with f'(x) ≈ 0 near the root
 * - Guaranteed superlinear convergence
 * - More robust for ill-conditioned problems
 *
 * @param f {function} : Function to find root of (signature: f(number) returns number)
 * @param a {number} : Left bracket endpoint
 * @param b {number} : Right bracket endpoint
 * @param tol {number} : Tolerance for convergence (on both x and f(x))
 * @param maxIter {number} : Maximum iterations (default 50)
 * @returns {map} : {u: number, f: number, iters: number}
 *
 * @example Solve difficult bracket with flat region:
 *   `solveRootBrent(function(x) { return x*x*x - x - 2; }, 1, 2, 1e-10, 50)`
 *
 * @note Requires sign change: f(a) * f(b) < 0
 * @note Based on Brent (1973) "Algorithms for Minimization without Derivatives"
 */
export function solveRootBrent(f, a is number, b is number, tol is number, maxIter is number) returns map
{
    if (maxIter == undefined)
        maxIter = 50;

    var fa = f(a);
    var fb = f(b);

    if (abs(fa) <= tol) return { "u" : a, "f" : fa, "iters" : 0 };
    if (abs(fb) <= tol) return { "u" : b, "f" : fb, "iters" : 0 };

    assertTrue(fa * fb < 0, "solveRootBrent: bracket does not contain a sign change.");

    // Ensure |f(a)| >= |f(b)| (b is the better guess)
    if (abs(fa) < abs(fb))
    {
        var temp = a; a = b; b = temp;
        temp = fa; fa = fb; fb = temp;
    }

    var c = a;      // Previous iterate
    var fc = fa;
    var d = b - a;  // Step size
    var e = d;      // Previous step size

    for (var i = 1; i <= maxIter; i += 1)
    {
        // Ensure |f(a)| >= |f(b)|
        if (abs(fc) < abs(fb))
        {
            a = b; b = c; c = a;
            fa = fb; fb = fc; fc = fa;
        }

        // Convergence check
        var tolAct = 2 * 1e-15 * abs(b) + tol / 2;
        var m = (c - b) / 2;

        if (abs(m) <= tolAct || abs(fb) <= tol)
        {
            return { "u" : b, "f" : fb, "iters" : i };
        }

        // Decide between interpolation and bisection
        if (abs(e) >= tolAct && abs(fa) > abs(fb))
        {
            // Try interpolation
            var s;
            if (abs(a - c) < 1e-15)
            {
                // Linear interpolation (secant)
                s = fb / fa;
                var p = 2 * m * s;
                var q = 1 - s;
                if (q < 0) { p = -p; q = -q; }

                // Accept interpolation if it's within bounds
                if (2 * p < 3 * m * q - abs(tolAct * q) && p < abs(e * q / 2))
                {
                    e = d;
                    d = p / q;
                }
                else
                {
                    // Bisection
                    d = m;
                    e = m;
                }
            }
            else
            {
                // Inverse quadratic interpolation
                var r = fb / fc;
                var s1 = fb / fa;
                var p = s1 * (2 * m * r * (r - s1) - (b - a) * (s1 - 1));
                var q = (r - 1) * (s1 - 1) * (fb / fc - 1);

                if (q < 0) { p = -p; } else { q = -q; }

                // Accept if within bounds
                if (abs(p) < abs(0.5 * e * q) && p > q * (a - b) && p < q * (c - b))
                {
                    e = d;
                    d = p / q;
                }
                else
                {
                    d = m;
                    e = m;
                }
            }
        }
        else
        {
            // Bisection
            d = m;
            e = m;
        }

        // Update a to previous b
        a = b;
        fa = fb;

        // Update b
        if (abs(d) > tolAct)
        {
            b = b + d;
        }
        else
        {
            b = b + (m > 0 ? tolAct : -tolAct);
        }
        fb = f(b);

        // Ensure sign change between b and c
        if ((fb > 0 && fc > 0) || (fb < 0 && fc < 0))
        {
            c = a;
            fc = fa;
            e = b - a;
            d = e;
        }
    }

    return { "u" : b, "f" : fb, "iters" : maxIter };
}

// =============================================================================
// NEWTON-RAPHSON METHODS
// =============================================================================

/**
 * Newton-Raphson root solver (1D).
 *
 * Uses the iteration x_{n+1} = x_n - f(x_n) / f'(x_n) for quadratic
 * convergence when near a simple root. Requires derivative function.
 *
 * Advantages:
 * - Quadratic convergence (doubles correct digits each iteration)
 * - Very fast when derivative is cheap to compute
 *
 * Disadvantages:
 * - Requires derivative function
 * - May diverge without good initial guess
 * - Can fail at roots with multiplicity > 1
 *
 * @param f {function} : Function to find root of
 * @param df {function} : Derivative of f
 * @param x0 {number} : Initial guess
 * @param tol {number} : Convergence tolerance
 * @param maxIter {number} : Maximum iterations (default 30)
 * @returns {map} : {u: number, f: number, iters: number, converged: boolean}
 *
 * @example Solve x² - 2 = 0 (find √2):
 *   `solveRootNewton(
 *       function(x) { return x*x - 2; },
 *       function(x) { return 2*x; },
 *       1.5, 1e-12, 30)`
 *   returns `{u: 1.41421356..., f: ~0, iters: 4, converged: true}`
 *
 * @note Use with good initial guess for reliability
 * @note Falls back gracefully if derivative is zero (halves step)
 */
export function solveRootNewton(f, df, x0 is number, tol is number, maxIter is number) returns map
{
    if (maxIter == undefined)
        maxIter = 30;

    var x = x0;
    var fx = f(x);

    for (var i = 1; i <= maxIter; i += 1)
    {
        if (abs(fx) <= tol)
        {
            return { "u" : x, "f" : fx, "iters" : i, "converged" : true };
        }

        var dfx = df(x);

        // Handle zero derivative
        if (abs(dfx) < 1e-15)
        {
            // Perturb slightly
            x = x + tol;
            fx = f(x);
            continue;
        }

        var dx = fx / dfx;
        x = x - dx;
        fx = f(x);

        // Check convergence in x
        if (abs(dx) < tol)
        {
            return { "u" : x, "f" : fx, "iters" : i, "converged" : true };
        }
    }

    return { "u" : x, "f" : fx, "iters" : maxIter, "converged" : false };
}

/**
 * Multi-dimensional Newton solver.
 *
 * Solves F(x) = 0 where x and F are n-dimensional vectors.
 * Uses the iteration: x_{n+1} = x_n - J(x_n)^{-1} * F(x_n)
 * where J is the Jacobian matrix.
 *
 * Applications:
 * - Surface-surface intersection
 * - Constrained point finding
 * - Multi-parameter optimization
 *
 * @param F {function} : Vector function F(x) returns array of n values
 * @param J {function} : Jacobian J(x) returns n×n matrix (array of arrays)
 * @param x0 {array} : Initial guess (array of n numbers)
 * @param tol {number} : Convergence tolerance
 * @param maxIter {number} : Maximum iterations (default 30)
 * @returns {map} : {x: array, f: array, iters: number, converged: boolean}
 *
 * @example Solve system: x² + y² = 1, x - y = 0 (intersection of circle and line):
 *   `newtonND(
 *       function(v) { return [v[0]*v[0] + v[1]*v[1] - 1, v[0] - v[1]]; },
 *       function(v) { return [[2*v[0], 2*v[1]], [1, -1]]; },
 *       [0.5, 0.5], 1e-10, 30)`
 *   returns point on unit circle at 45 degrees
 *
 * @note Requires good initial guess for convergence
 * @note Uses direct solve for 2×2, LU decomposition for larger systems
 */
export function newtonND(F, J, x0 is array, tol is number, maxIter is number) returns map
{
    if (maxIter == undefined)
        maxIter = 30;

    var n = size(x0);
    var x = x0;
    var fx = F(x);

    for (var i = 1; i <= maxIter; i += 1)
    {
        // Check convergence
        var maxF = 0;
        for (var j = 0; j < n; j += 1)
        {
            if (abs(fx[j]) > maxF)
                maxF = abs(fx[j]);
        }

        if (maxF <= tol)
        {
            return { "x" : x, "f" : fx, "iters" : i, "converged" : true };
        }

        // Compute Jacobian
        var Jx = J(x);

        // Solve J * dx = -F for dx
        var dx = solveLinearSystem(Jx, fx, n);

        if (dx == undefined)
        {
            // Singular Jacobian
            return { "x" : x, "f" : fx, "iters" : i, "converged" : false };
        }

        // Update x
        var maxDx = 0;
        for (var j = 0; j < n; j += 1)
        {
            x[j] = x[j] - dx[j];
            if (abs(dx[j]) > maxDx)
                maxDx = abs(dx[j]);
        }

        fx = F(x);

        // Check convergence in x
        if (maxDx < tol)
        {
            return { "x" : x, "f" : fx, "iters" : i, "converged" : true };
        }
    }

    return { "x" : x, "f" : fx, "iters" : maxIter, "converged" : false };
}

/**
 * Solve a linear system A*x = b using Gaussian elimination with partial pivoting.
 *
 * Internal helper for newtonND. Handles small systems efficiently.
 *
 * @param A {array} : n×n coefficient matrix (array of row arrays)
 * @param b {array} : Right-hand side vector
 * @param n {number} : System dimension
 * @returns {array|undefined} : Solution vector, or undefined if singular
 */
function solveLinearSystem(A is array, b is array, n is number)
{
    // Special case for 2×2 (most common in curve/surface work)
    if (n == 2)
    {
        var det = A[0][0] * A[1][1] - A[0][1] * A[1][0];
        if (abs(det) < 1e-15)
            return undefined;

        return [
            (A[1][1] * b[0] - A[0][1] * b[1]) / det,
            (A[0][0] * b[1] - A[1][0] * b[0]) / det
        ];
    }

    // General case: Gaussian elimination with partial pivoting
    // Create augmented matrix [A|b]
    var aug = [];
    for (var i = 0; i < n; i += 1)
    {
        var row = [];
        for (var j = 0; j < n; j += 1)
        {
            row = append(row, A[i][j]);
        }
        row = append(row, b[i]);
        aug = append(aug, row);
    }

    // Forward elimination
    for (var col = 0; col < n; col += 1)
    {
        // Find pivot
        var maxVal = abs(aug[col][col]);
        var maxRow = col;
        for (var row = col + 1; row < n; row += 1)
        {
            if (abs(aug[row][col]) > maxVal)
            {
                maxVal = abs(aug[row][col]);
                maxRow = row;
            }
        }

        if (maxVal < 1e-15)
            return undefined;  // Singular

        // Swap rows
        if (maxRow != col)
        {
            var temp = aug[col];
            aug[col] = aug[maxRow];
            aug[maxRow] = temp;
        }

        // Eliminate below
        for (var row = col + 1; row < n; row += 1)
        {
            var factor = aug[row][col] / aug[col][col];
            for (var j = col; j <= n; j += 1)
            {
                aug[row][j] = aug[row][j] - factor * aug[col][j];
            }
        }
    }

    // Back substitution
    var x = [];
    for (var i = 0; i < n; i += 1)
    {
        x = append(x, 0);
    }

    for (var i = n - 1; i >= 0; i -= 1)
    {
        var sum = aug[i][n];
        for (var j = i + 1; j < n; j += 1)
        {
            sum = sum - aug[i][j] * x[j];
        }
        x[i] = sum / aug[i][i];
    }

    return x;
}

// =============================================================================
// UTILITY SOLVERS
// =============================================================================

/**
 * Find all roots in an interval by sampling and refinement.
 *
 * Scans the interval for sign changes, then refines each bracket
 * to find all roots. Useful when multiple roots exist.
 *
 * @param f {function} : Function to find roots of
 * @param a {number} : Left endpoint
 * @param b {number} : Right endpoint
 * @param numSamples {number} : Number of samples for initial scan
 * @param tol {number} : Root tolerance
 * @returns {array} : Array of root locations, sorted
 *
 * @example Find all roots of sin(x) in [0, 10]:
 *   `findAllRoots(function(x) { return sin(x); }, 0, 10, 50, 1e-9)`
 *   returns approximately [0, 3.14159, 6.28318, 9.42478]
 */
export function findAllRoots(f, a is number, b is number, numSamples is number, tol is number) returns array
{
    // Sample the function
    var samples = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var u = a + (b - a) * i / (numSamples - 1);
        samples = append(samples, { "u" : u, "f" : f(u) });
    }

    // Find all sign changes
    var roots = [];
    for (var i = 0; i < numSamples - 1; i += 1)
    {
        var fa = samples[i].f;
        var fb = samples[i + 1].f;

        // Exact root at sample
        if (abs(fa) <= tol)
        {
            // Avoid duplicates
            var isDup = false;
            for (var r in roots)
            {
                if (abs(r - samples[i].u) < tol)
                {
                    isDup = true;
                    break;
                }
            }
            if (!isDup)
                roots = append(roots, samples[i].u);
        }
        // Sign change - refine
        else if (fa * fb < 0)
        {
            var result = solveRootHybrid(f, samples[i].u, samples[i + 1].u, tol, 50);
            roots = append(roots, result.u);
        }
    }

    // Check last sample
    if (abs(samples[numSamples - 1].f) <= tol)
    {
        var isDup = false;
        for (var r in roots)
        {
            if (abs(r - samples[numSamples - 1].u) < tol)
            {
                isDup = true;
                break;
            }
        }
        if (!isDup)
            roots = append(roots, samples[numSamples - 1].u);
    }

    return roots;
}
