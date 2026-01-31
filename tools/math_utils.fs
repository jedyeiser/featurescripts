FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * General math utilities for numerical operations.
 *
 * Provides safe, robust mathematical operations that handle edge cases
 * and work correctly with ValueWithUnits. These utilities are used
 * throughout the codebase for consistent numerical behavior.
 *
 * @source footprint/fpt_math.fs:16-28
 */

/**
 * Robust sign function with epsilon tolerance.
 *
 * Returns -1, 0, or 1 based on the sign of x, with a tolerance
 * for near-zero values to avoid noise in numerical calculations.
 *
 * @param x {number} : Value to check
 * @param eps {number} : Epsilon tolerance for zero detection
 * @returns {number} : -1 if x < -eps, +1 if x > eps, 0 otherwise
 *
 * @example `safeSign(-0.001, 0.01)` returns `0` (within tolerance)
 * @example `safeSign(5.2, 1e-9)` returns `1`
 * @example `safeSign(-3.7, 1e-9)` returns `-1`
 *
 * @source footprint/fpt_math.fs:16-21
 */
export function safeSign(x is number, eps is number) returns number
{
    if (x > eps) return 1;
    if (x < -eps) return -1;
    return 0;
}

/**
 * Clamp a value to the range [0, 1].
 *
 * Useful for normalizing parameters and ensuring values stay within
 * valid unit interval bounds.
 *
 * @param u {number} : Value to clamp
 * @returns {number} : u clamped to [0, 1]
 *
 * @example `clamp01(-0.5)` returns `0`
 * @example `clamp01(0.7)` returns `0.7`
 * @example `clamp01(1.5)` returns `1`
 *
 * @source footprint/fpt_math.fs:23-28
 */
export function clamp01(u is number) returns number
{
    if (u < 0) return 0;
    if (u > 1) return 1;
    return u;
}

/**
 * Clamp a value to a specified range [minVal, maxVal].
 *
 * Generalized clamping function that works with any numeric range.
 * Supports both plain numbers and ValueWithUnits.
 *
 * @param val {number|ValueWithUnits} : Value to clamp
 * @param minVal {number|ValueWithUnits} : Minimum value
 * @param maxVal {number|ValueWithUnits} : Maximum value
 * @returns {number|ValueWithUnits} : val clamped to [minVal, maxVal]
 *
 * @example `clamp(5, 0, 3)` returns `3`
 * @example `clamp(-2, 0, 10)` returns `0`
 * @example `clamp(1.5 * meter, 0 * meter, 3 * meter)` returns `1.5 * meter`
 */
export function clamp(val, minVal, maxVal)
{
    if (val < minVal) return minVal;
    if (val > maxVal) return maxVal;
    return val;
}

/**
 * Linear interpolation between two values.
 *
 * Computes a + (b - a) * t. Works with numbers, ValueWithUnits, and Vectors.
 * When t=0 returns a, when t=1 returns b.
 *
 * @param a {number|ValueWithUnits|Vector} : Start value
 * @param b {number|ValueWithUnits|Vector} : End value
 * @param t {number} : Interpolation parameter (typically in [0,1])
 * @returns {number|ValueWithUnits|Vector} : Interpolated value
 *
 * @example `lerp(0, 10, 0.5)` returns `5`
 * @example `lerp(1*meter, 3*meter, 0.25)` returns `1.5*meter`
 */
export function lerp(a, b, t is number)
{
    return a + (b - a) * t;
}

/**
 * Remap a value from one range to another.
 *
 * Maps val from [inMin, inMax] to [outMin, outMax] linearly.
 * Useful for converting between different parameter spaces.
 *
 * @param val {number|ValueWithUnits} : Value to remap
 * @param inMin {number|ValueWithUnits} : Input range minimum
 * @param inMax {number|ValueWithUnits} : Input range maximum
 * @param outMin {number|ValueWithUnits} : Output range minimum
 * @param outMax {number|ValueWithUnits} : Output range maximum
 * @returns {number|ValueWithUnits} : Remapped value
 *
 * @example `remap(5, 0, 10, 0, 100)` returns `50`
 * @example `remap(0.5, 0, 1, -1, 1)` returns `0`
 */
export function remap(val, inMin, inMax, outMin, outMax)
{
    var t = (val - inMin) / (inMax - inMin);
    return lerp(outMin, outMax, t);
}
