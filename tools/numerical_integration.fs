FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Phase 1 dependencies
import(path : "assertions.fs", version : "");

/**
 * Numerical integration utilities using trapezoidal rule.
 *
 * Provides cumulative and total integration functions that work correctly
 * with both unitless arrays and arrays with ValueWithUnits. Used for
 * arc length calculations, curvature integration, and other numerical tasks.
 *
 * @source footprint/fpt_math.fs:34-87
 */

/**
 * Cumulative trapezoidal integration.
 *
 * Integrates array y with respect to x using the trapezoidal rule,
 * returning both cumulative values at each point and the total integral.
 * Handles unit-valued arrays correctly (e.g., curvature 1/m, slope unitless).
 *
 * The trapezoidal rule approximates the integral as:
 *   ∫y dx ≈ Σ (dx_i * (y_i + y_{i-1}) / 2)
 *
 * @param x {array} : Independent variable array (must be monotonic)
 * @param y {array} : Dependent variable array (can have units)
 * @param initialValue {number|ValueWithUnits} : Starting value for cumulative sum
 * @returns {map} : {cumulative: array, total: number|ValueWithUnits}
 *                  cumulative[i] = integral from x[0] to x[i]
 *                  total = integral from x[0] to x[end]
 *
 * @example Integrate velocity to get position:
 *   `cumTrapz([0, 1, 2], [0*m/s, 5*m/s, 10*m/s], 0*m)` returns
 *   `{cumulative: [0*m, 2.5*m, 10*m], total: 10*m}`
 *
 * @source footprint/fpt_math.fs:39-56
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
 * Simple trapezoidal integration (total only).
 *
 * Convenience function that returns only the total integral value
 * without cumulative intermediate values. More efficient when you
 * only need the final result.
 *
 * @param x {array} : Independent variable array
 * @param y {array} : Dependent variable array
 * @returns {number|ValueWithUnits} : Total integral value
 *
 * @example `trapz([0, 1, 2, 3], [1, 4, 9, 16])` returns approximate integral
 */
export function trapz(x is array, y is array)
{
    assertTrue(size(x) == size(y), "trapz: x and y arrays must be same size.");
    assertTrue(size(x) >= 2, "trapz: arrays must have at least 2 elements for integration.");

    var total = 0 * y[0];  // Initialize with correct units

    for (var i = 1; i < size(y); i += 1)
    {
        var dx = x[i] - x[i - 1];
        total += dx * (y[i] + y[i - 1]) * 0.5;
    }

    return total;
}

/**
 * Moving average smoothing filter.
 *
 * Applies a symmetric moving average window to smooth noisy data.
 * Each output point is the average of nearby input points within the window.
 * Window size is forced to be odd for symmetry. Endpoints use reduced windows.
 *
 * Works with both numeric and unit-valued arrays.
 *
 * @param y {array} : Array to smooth (can contain numbers or ValueWithUnits)
 * @param window {number} : Window size (will be made odd if even, minimum 1)
 * @returns {array} : Smoothed array of same size as input
 *
 * @example Smooth noisy curvature data:
 *   `movingAverage([1, 5, 2, 6, 3], 3)` returns `[2, 2.67, 4.33, 3.67, 4.5]` (approx)
 *
 * @note Window size is forced odd: window=4 becomes window=5
 * @note Endpoints use smaller effective windows (no padding)
 *
 * @source footprint/fpt_math.fs:62-87
 */
export function movingAverage(y is array, window is number) returns array
{
    assertTrue(window >= 1, "movingAverage: window must be >= 1.");
    if (window == 1) return y;

    // Force odd window for symmetry
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
