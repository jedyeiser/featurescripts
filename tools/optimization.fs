FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Dependencies
import(path : "assertions.fs", version : "");
import(path : "math_utils.fs", version : "");

/**
 * OPTIMIZATION METHODS
 * ====================
 *
 * Provides numerical optimization algorithms for curve fitting, fairing,
 * and general minimization problems. These methods are building blocks
 * for more complex geometric operations.
 *
 * | Method                 | Type           | Best For                          |
 * |------------------------|----------------|-----------------------------------|
 * | levenbergMarquardt()   | Least squares  | Curve fitting to point clouds     |
 * | conjugateGradient()    | Unconstrained  | Curve fairing, smoothness opt     |
 * | gradientDescent()      | Unconstrained  | Simple problems, large scale      |
 * | goldenSectionSearch()  | 1D minimization| Line search, scalar optimization  |
 *
 * CURVE FITTING GUIDANCE:
 * - Point cloud to B-spline: Levenberg-Marquardt
 * - Smoothing existing curve: Conjugate Gradient on fairness functional
 * - Simple parameter optimization: Gradient descent with line search
 *
 * CONVERGENCE CHARACTERISTICS:
 * - LM: Quadratic near minimum, linear far from minimum
 * - CG: Superlinear for quadratic objectives, linear otherwise
 * - GD: Linear convergence, depends heavily on step size
 *
 * WHEN TO AVOID:
 * - LM: Very large control point counts (>100) - consider sparse methods
 * - CG: Non-smooth objectives - use gradient descent instead
 * - All: Very high-dimensional problems - consider specialized methods
 */

// =============================================================================
// CONSTANTS
// =============================================================================

/**
 * Default tolerance for optimization convergence.
 */
export const OPTIMIZATION_DEFAULT_TOL = 1e-8;

/**
 * Default maximum iterations.
 */
export const OPTIMIZATION_DEFAULT_MAX_ITER = 100;

/**
 * Initial damping factor for Levenberg-Marquardt.
 */
export const LM_INITIAL_LAMBDA = 0.001;

/**
 * Golden ratio for golden section search.
 */
export const GOLDEN_RATIO = 0.6180339887498949;

// =============================================================================
// GRADIENT DESCENT
// =============================================================================

/**
 * Gradient descent with backtracking line search.
 *
 * Minimizes f(x) using steepest descent with Armijo line search
 * for step size selection. Simple and robust, suitable for
 * general-purpose minimization.
 *
 * @param f {function} : Objective function f(x) returns scalar
 * @param grad {function} : Gradient function grad(x) returns array
 * @param x0 {array} : Initial point (array of numbers)
 * @param options {map} : Optional settings:
 *                        - tol: Convergence tolerance (default 1e-8)
 *                        - maxIter: Maximum iterations (default 100)
 *                        - alpha: Initial step size (default 1.0)
 *                        - beta: Line search shrink factor (default 0.5)
 *                        - c: Armijo constant (default 1e-4)
 * @returns {map} : {
 *                    x: array,           - Optimal point
 *                    f: number,          - Objective value at optimal
 *                    iters: number,      - Iterations used
 *                    converged: boolean  - Whether converged within tolerance
 *                  }
 *
 * @example Minimize quadratic function:
 *   `var result = gradientDescent(
 *       function(x) { return x[0]*x[0] + x[1]*x[1]; },
 *       function(x) { return [2*x[0], 2*x[1]]; },
 *       [1.0, 1.0], {});`
 *   returns x ≈ [0, 0]
 */
export function gradientDescent(f, grad, x0 is array, options is map) returns map
{
    // Parse options
    var tol = OPTIMIZATION_DEFAULT_TOL;
    var maxIter = OPTIMIZATION_DEFAULT_MAX_ITER;
    var alpha = 1.0;
    var beta = 0.5;
    var c = 1e-4;

    if (options.tol != undefined) tol = options.tol;
    if (options.maxIter != undefined) maxIter = options.maxIter;
    if (options.alpha != undefined) alpha = options.alpha;
    if (options.beta != undefined) beta = options.beta;
    if (options.c != undefined) c = options.c;

    var n = size(x0);
    var x = x0;
    var fx = f(x);

    for (var iter = 1; iter <= maxIter; iter += 1)
    {
        var g = grad(x);

        // Check gradient norm for convergence
        var gradNorm = vectorNorm(g);
        if (gradNorm < tol)
        {
            return { "x" : x, "f" : fx, "iters" : iter, "converged" : true };
        }

        // Backtracking line search
        var stepSize = alpha;
        var xNew = vectorSubtract(x, vectorScale(g, stepSize));
        var fNew = f(xNew);

        var descent = vectorDot(g, g);  // g^T * g for steepest descent

        while (fNew > fx - c * stepSize * descent && stepSize > 1e-15)
        {
            stepSize = stepSize * beta;
            xNew = vectorSubtract(x, vectorScale(g, stepSize));
            fNew = f(xNew);
        }

        // Check for sufficient decrease
        if (abs(fNew - fx) < tol * (1 + abs(fx)))
        {
            return { "x" : xNew, "f" : fNew, "iters" : iter, "converged" : true };
        }

        x = xNew;
        fx = fNew;
    }

    return { "x" : x, "f" : fx, "iters" : maxIter, "converged" : false };
}

// =============================================================================
// CONJUGATE GRADIENT
// =============================================================================

/**
 * Nonlinear conjugate gradient (Polak-Ribiere).
 *
 * Minimizes f(x) using conjugate gradient method with Polak-Ribiere
 * update formula. More efficient than gradient descent for smooth
 * objectives, achieving superlinear convergence.
 *
 * @param f {function} : Objective function f(x) returns scalar
 * @param grad {function} : Gradient function grad(x) returns array
 * @param x0 {array} : Initial point
 * @param options {map} : Optional settings:
 *                        - tol: Convergence tolerance (default 1e-8)
 *                        - maxIter: Maximum iterations (default 100)
 *                        - restartFreq: Restart frequency (default n, dimension)
 * @returns {map} : {x: array, f: number, iters: number, converged: boolean}
 *
 * @example Minimize Rosenbrock function:
 *   `var result = conjugateGradient(rosenbrock, rosenbrockGrad, [-1, 1], {});`
 */
export function conjugateGradient(f, grad, x0 is array, options is map) returns map
{
    // Parse options
    var tol = OPTIMIZATION_DEFAULT_TOL;
    var maxIter = OPTIMIZATION_DEFAULT_MAX_ITER;
    var n = size(x0);
    var restartFreq = n;

    if (options.tol != undefined) tol = options.tol;
    if (options.maxIter != undefined) maxIter = options.maxIter;
    if (options.restartFreq != undefined) restartFreq = options.restartFreq;

    var x = x0;
    var fx = f(x);
    var g = grad(x);
    var d = vectorScale(g, -1);  // Initial direction = -gradient
    var gNormSq = vectorDot(g, g);

    for (var iter = 1; iter <= maxIter; iter += 1)
    {
        // Check convergence
        if (sqrt(gNormSq) < tol)
        {
            return { "x" : x, "f" : fx, "iters" : iter, "converged" : true };
        }

        // Line search along direction d
        var lineResult = lineSearch(f, x, d);
        var alpha = lineResult.alpha;

        // Update position
        var xNew = vectorAdd(x, vectorScale(d, alpha));
        var fNew = f(xNew);

        // Check for sufficient decrease
        if (abs(fNew - fx) < tol * (1 + abs(fx)))
        {
            return { "x" : xNew, "f" : fNew, "iters" : iter, "converged" : true };
        }

        // Compute new gradient
        var gNew = grad(xNew);
        var gNewNormSq = vectorDot(gNew, gNew);

        // Polak-Ribiere beta
        var betaPR = vectorDot(gNew, vectorSubtract(gNew, g)) / gNormSq;

        // Reset to steepest descent if beta < 0 or periodically
        var beta = max([0, betaPR]);
        if (iter % restartFreq == 0)
            beta = 0;

        // Update direction
        d = vectorAdd(vectorScale(gNew, -1), vectorScale(d, beta));

        // Update state
        x = xNew;
        fx = fNew;
        g = gNew;
        gNormSq = gNewNormSq;
    }

    return { "x" : x, "f" : fx, "iters" : maxIter, "converged" : false };
}

// =============================================================================
// LEVENBERG-MARQUARDT
// =============================================================================

/**
 * Levenberg-Marquardt algorithm for nonlinear least squares.
 *
 * Minimizes sum(r_i(x)^2) where r is a vector of residuals.
 * Combines Gauss-Newton and gradient descent, with automatic
 * switching based on convergence behavior.
 *
 * Ideal for curve fitting problems where the objective is
 * sum of squared distances to target points.
 *
 * @param residuals {function} : Residual function r(x) returns array of residuals
 * @param jacobian {function} : Jacobian J(x) returns m×n matrix (m residuals, n params)
 * @param x0 {array} : Initial parameter guess
 * @param options {map} : Optional settings:
 *                        - tol: Convergence tolerance (default 1e-8)
 *                        - maxIter: Maximum iterations (default 100)
 *                        - lambda: Initial damping (default 0.001)
 * @returns {map} : {
 *                    x: array,           - Optimal parameters
 *                    residualNorm: number, - Final residual norm
 *                    iters: number,      - Iterations used
 *                    converged: boolean
 *                  }
 *
 * @example Fit line y = a*x + b to points:
 *   `var result = levenbergMarquardt(
 *       function(p) {  // residuals: predicted - actual
 *           var res = [];
 *           for (var i = 0; i < size(xData); i += 1)
 *               res = append(res, p[0]*xData[i] + p[1] - yData[i]);
 *           return res;
 *       },
 *       function(p) {  // jacobian: d(residual_i)/d(param_j)
 *           var J = [];
 *           for (var i = 0; i < size(xData); i += 1)
 *               J = append(J, [xData[i], 1]);
 *           return J;
 *       },
 *       [0, 0], {});`
 */
export function levenbergMarquardt(residuals, jacobian, x0 is array, options is map) returns map
{
    // Parse options
    var tol = OPTIMIZATION_DEFAULT_TOL;
    var maxIter = OPTIMIZATION_DEFAULT_MAX_ITER;
    var lambda = LM_INITIAL_LAMBDA;

    if (options.tol != undefined) tol = options.tol;
    if (options.maxIter != undefined) maxIter = options.maxIter;
    if (options.lambda != undefined) lambda = options.lambda;

    var n = size(x0);
    var x = x0;
    var r = residuals(x);
    var m = size(r);

    var cost = sumOfSquares(r);

    for (var iter = 1; iter <= maxIter; iter += 1)
    {
        // Compute Jacobian
        var J = jacobian(x);

        // Compute J^T * J and J^T * r
        var JtJ = matrixMultiplyTransposeLeft(J, J);
        var Jtr = matrixVectorMultiplyTranspose(J, r);

        // Add damping: (J^T*J + lambda*diag(J^T*J)) * delta = -J^T*r
        var A = [];
        for (var i = 0; i < n; i += 1)
        {
            var row = [];
            for (var j = 0; j < n; j += 1)
            {
                if (i == j)
                    row = append(row, JtJ[i][j] * (1 + lambda));
                else
                    row = append(row, JtJ[i][j]);
            }
            A = append(A, row);
        }

        // Solve for step
        var negJtr = vectorScale(Jtr, -1);
        var delta = solveLinearSystemLM(A, negJtr, n);

        if (delta == undefined)
        {
            // Singular - increase damping
            lambda = lambda * 10;
            continue;
        }

        // Evaluate new point
        var xNew = vectorAdd(x, delta);
        var rNew = residuals(xNew);
        var costNew = sumOfSquares(rNew);

        // Check for improvement
        if (costNew < cost)
        {
            // Accept step, decrease damping
            x = xNew;
            r = rNew;
            cost = costNew;
            lambda = lambda / 3;

            // Check convergence
            if (sqrt(cost / m) < tol)
            {
                return {
                    "x" : x,
                    "residualNorm" : sqrt(cost),
                    "iters" : iter,
                    "converged" : true
                };
            }

            // Check step size
            if (vectorNorm(delta) < tol * (1 + vectorNorm(x)))
            {
                return {
                    "x" : x,
                    "residualNorm" : sqrt(cost),
                    "iters" : iter,
                    "converged" : true
                };
            }
        }
        else
        {
            // Reject step, increase damping
            lambda = lambda * 10;

            // Check for excessive damping
            if (lambda > 1e10)
            {
                return {
                    "x" : x,
                    "residualNorm" : sqrt(cost),
                    "iters" : iter,
                    "converged" : false
                };
            }
        }
    }

    return {
        "x" : x,
        "residualNorm" : sqrt(cost),
        "iters" : maxIter,
        "converged" : false
    };
}

// =============================================================================
// LINE SEARCH
// =============================================================================

/**
 * Golden section search for 1D minimization.
 *
 * Finds the minimum of f(x) in interval [a, b] using golden section
 * search. Converges linearly with ratio 0.618.
 *
 * @param f {function} : Function to minimize
 * @param a {number} : Left endpoint
 * @param b {number} : Right endpoint
 * @param tol {number} : Tolerance for interval width
 * @returns {map} : {x: number, f: number, iters: number}
 *
 * @example Find minimum of parabola:
 *   `goldenSectionSearch(function(x) { return (x-3)*(x-3); }, 0, 5, 1e-6)`
 *   returns x ≈ 3
 */
export function goldenSectionSearch(f, a is number, b is number, tol is number) returns map
{
    var phi = GOLDEN_RATIO;

    var x1 = b - phi * (b - a);
    var x2 = a + phi * (b - a);
    var f1 = f(x1);
    var f2 = f(x2);

    var iters = 0;

    while (abs(b - a) > tol)
    {
        iters += 1;

        if (f1 < f2)
        {
            b = x2;
            x2 = x1;
            f2 = f1;
            x1 = b - phi * (b - a);
            f1 = f(x1);
        }
        else
        {
            a = x1;
            x1 = x2;
            f1 = f2;
            x2 = a + phi * (b - a);
            f2 = f(x2);
        }
    }

    var xMin = (a + b) / 2;
    return { "x" : xMin, "f" : f(xMin), "iters" : iters };
}

/**
 * Backtracking line search with Armijo condition.
 *
 * Finds step size alpha such that f(x + alpha*d) satisfies
 * sufficient decrease condition.
 *
 * @param f {function} : Objective function
 * @param x {array} : Current point
 * @param d {array} : Search direction
 * @returns {map} : {alpha: number, f: number}
 */
function lineSearch(f, x is array, d is array) returns map
{
    var alpha = 1.0;
    var beta = 0.5;
    var c = 1e-4;

    var fx = f(x);
    var xNew = vectorAdd(x, vectorScale(d, alpha));
    var fNew = f(xNew);

    // Compute directional derivative approximation
    var eps = 1e-8;
    var fEps = f(vectorAdd(x, vectorScale(d, eps)));
    var dirDeriv = (fEps - fx) / eps;

    var maxBacktrack = 30;
    for (var i = 0; i < maxBacktrack; i += 1)
    {
        if (fNew <= fx + c * alpha * dirDeriv || alpha < 1e-15)
            break;

        alpha = alpha * beta;
        xNew = vectorAdd(x, vectorScale(d, alpha));
        fNew = f(xNew);
    }

    return { "alpha" : alpha, "f" : fNew };
}

// =============================================================================
// VECTOR AND MATRIX UTILITIES
// =============================================================================

/**
 * Compute vector norm (Euclidean).
 */
function vectorNorm(v is array) returns number
{
    var sum = 0;
    for (var i = 0; i < size(v); i += 1)
    {
        sum += v[i] * v[i];
    }
    return sqrt(sum);
}

/**
 * Compute dot product of two vectors.
 */
function vectorDot(a is array, b is array) returns number
{
    var sum = 0;
    for (var i = 0; i < size(a); i += 1)
    {
        sum += a[i] * b[i];
    }
    return sum;
}

/**
 * Add two vectors.
 */
function vectorAdd(a is array, b is array) returns array
{
    var result = [];
    for (var i = 0; i < size(a); i += 1)
    {
        result = append(result, a[i] + b[i]);
    }
    return result;
}

/**
 * Subtract two vectors (a - b).
 */
function vectorSubtract(a is array, b is array) returns array
{
    var result = [];
    for (var i = 0; i < size(a); i += 1)
    {
        result = append(result, a[i] - b[i]);
    }
    return result;
}

/**
 * Scale vector by scalar.
 */
function vectorScale(v is array, s is number) returns array
{
    var result = [];
    for (var i = 0; i < size(v); i += 1)
    {
        result = append(result, v[i] * s);
    }
    return result;
}

/**
 * Compute sum of squares of array elements.
 */
function sumOfSquares(v is array) returns number
{
    var sum = 0;
    for (var i = 0; i < size(v); i += 1)
    {
        sum += v[i] * v[i];
    }
    return sum;
}

/**
 * Multiply J^T * J where J is m×n matrix.
 */
function matrixMultiplyTransposeLeft(J is array, K is array) returns array
{
    var m = size(J);
    var n = size(J[0]);

    var result = [];
    for (var i = 0; i < n; i += 1)
    {
        var row = [];
        for (var j = 0; j < n; j += 1)
        {
            var sum = 0;
            for (var k = 0; k < m; k += 1)
            {
                sum += J[k][i] * K[k][j];
            }
            row = append(row, sum);
        }
        result = append(result, row);
    }
    return result;
}

/**
 * Multiply J^T * v where J is m×n matrix and v is m-vector.
 */
function matrixVectorMultiplyTranspose(J is array, v is array) returns array
{
    var m = size(J);
    var n = size(J[0]);

    var result = [];
    for (var i = 0; i < n; i += 1)
    {
        var sum = 0;
        for (var k = 0; k < m; k += 1)
        {
            sum += J[k][i] * v[k];
        }
        result = append(result, sum);
    }
    return result;
}

/**
 * Solve linear system A*x = b using Gaussian elimination.
 * Returns undefined if singular.
 */
function solveLinearSystemLM(A is array, b is array, n is number)
{
    // Create augmented matrix
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

    // Forward elimination with partial pivoting
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
            return undefined;

        // Swap rows
        if (maxRow != col)
        {
            var temp = aug[col];
            aug[col] = aug[maxRow];
            aug[maxRow] = temp;
        }

        // Eliminate
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
