FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * Assertion and validation utilities for runtime checks.
 *
 * These functions provide clean error handling by throwing regenError
 * with descriptive messages when validation fails. Use them to validate
 * preconditions and invariants throughout the codebase.
 *
 * @source footprint/fpt_math.fs:10-14
 */

/**
 * Assert that a condition is true, throwing regenError if false.
 *
 * Use this for general assertions where you need custom validation logic.
 * Throws a regeneration error with the provided message if the condition fails.
 *
 * @param cond {boolean} : Condition to check
 * @param msg {string} : Error message to display if assertion fails
 *
 * @example `assertTrue(size(arr) > 0, "Array must not be empty")` throws if arr is empty
 *
 * @source footprint/fpt_math.fs:10-14
 */
export function assertTrue(cond is boolean, msg is string)
{
    if (!cond)
        throw regenError(msg);
}

/**
 * Assert that a value is positive (> 0).
 *
 * Works with both plain numbers and ValueWithUnits.
 *
 * @param val {number|ValueWithUnits} : Value to check
 * @param msg {string} : Error message if validation fails
 *
 * @example `assertPositive(radius, "Radius must be positive")`
 */
export function assertPositive(val, msg is string)
{
    var value = val;
    try silent { value = val.value; }

    if (value <= 0)
        throw regenError(msg);
}

/**
 * Assert that a value is within a specified range [min, max].
 *
 * Works with both plain numbers and ValueWithUnits.
 *
 * @param val {number|ValueWithUnits} : Value to check
 * @param minVal {number|ValueWithUnits} : Minimum allowed value (inclusive)
 * @param maxVal {number|ValueWithUnits} : Maximum allowed value (inclusive)
 * @param msg {string} : Error message if validation fails
 *
 * @example `assertInRange(param, 0, 1, "Parameter must be in [0,1]")`
 */
export function assertInRange(val, minVal, maxVal, msg is string)
{
    var value = val;
    var min = minVal;
    var max = maxVal;

    try silent { value = val.value; }
    try silent { min = minVal.value; }
    try silent { max = maxVal.value; }

    if (value < min || value > max)
        throw regenError(msg);
}

/**
 * Assert that an array is non-empty.
 *
 * Useful for validating function inputs that require at least one element.
 *
 * @param arr {array} : Array to check
 * @param msg {string} : Error message if validation fails
 *
 * @example `assertNonEmpty(controlPoints, "Control points array cannot be empty")`
 */
export function assertNonEmpty(arr is array, msg is string)
{
    if (size(arr) == 0)
        throw regenError(msg);
}
