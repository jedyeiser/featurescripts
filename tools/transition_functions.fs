FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * Transition and blending functions for smooth parameter transitions.
 *
 * Provides various monotonic functions mapping [0,1] → [0,1] with different
 * smoothness properties. Used for blending scale factors, applying smooth
 * offsets, and creating smooth transitions in curve modifications.
 *
 * @source gordonSurface/modifyCurveEnd.fs, gordonSurface/scaledCurve.fs
 */

/**
 * Transition function types with different smoothness properties.
 *
 * - LINEAR: C^∞ but has corner (discontinuous derivative) at endpoints when chained
 * - SINUSOIDAL: C^1 continuous (smooth first derivative)
 * - LOGISTIC: C^∞ smooth sigmoid (smooth to all orders)
 *
 * @source gordonSurface/constEnums.fs:20-28
 */
export enum TransitionType
{
    annotation { "Name" : "Linear" }
    LINEAR,
    annotation { "Name" : "Sinusoidal" }
    SINUSOIDAL,
    annotation { "Name" : "Logistic" }
    LOGISTIC
}

/**
 * Linear transition: f(t) = t
 *
 * Simplest transition with constant rate of change.
 * Derivative is constant (f'(t) = 1 everywhere).
 *
 * Use when: Simple linear blending is sufficient
 *
 * Smoothness: C^∞ in interior, but creates corner when used
 *             at boundaries between segments
 *
 * @param t {number} : Parameter in [0, 1]
 * @returns {number} : t (identity function)
 *
 * @example `linearTransition(0.5)` returns `0.5`
 */
export function linearTransition(t is number) returns number
{
    return t;
}

/**
 * Sinusoidal transition: f(t) = (1 - cos(πt)) / 2
 *
 * Smooth S-curve with zero derivatives at endpoints (f'(0) = f'(1) = 0).
 * Creates smooth blending that eases in and out.
 *
 * Use when: Need smooth blend with zero derivative at ends (G1 continuity)
 *
 * Smoothness: C^1 continuous (smooth first derivative)
 * Properties: f(0) = 0, f(1) = 1, f'(0) = 0, f'(1) = 0
 *
 * @param t {number} : Parameter in [0, 1]
 * @returns {number} : Sinusoidal blend value in [0, 1]
 *
 * @example `sinusoidalTransition(0)` returns `0`
 * @example `sinusoidalTransition(0.5)` returns `0.5`
 * @example `sinusoidalTransition(1)` returns `1`
 */
export function sinusoidalTransition(t is number) returns number
{
    return (1 - cos(t * PI * radian)) / 2;
}

/**
 * Logistic (sigmoid) transition: f(t) = 1 / (1 + e^(-k(t-0.5)))
 *
 * Smooth S-curve using logistic function, scaled/shifted to map [0,1] → [0,1].
 * Smoother than sinusoidal, with all derivatives continuous.
 * Uses steepness parameter k=10 for good transition characteristics.
 *
 * Use when: Need maximum smoothness (G^∞ continuity)
 *
 * Smoothness: C^∞ continuous (infinitely differentiable)
 * Properties: f(0) ≈ 0, f(0.5) = 0.5, f(1) ≈ 1
 *             All derivatives vanish at endpoints
 *
 * @param t {number} : Parameter in [0, 1]
 * @returns {number} : Sigmoid blend value in [0, 1]
 *
 * @example `logisticTransition(0.5)` returns `0.5`
 *
 * @note Uses k=10 for steepness; could be parameterized if needed
 */
export function logisticTransition(t is number) returns number
{
    const k = 10;  // Steepness parameter
    var shifted = k * (t - 0.5);
    var sigmoid = 1 / (1 + exp(-shifted));

    // Scale/shift to ensure f(0)=0, f(1)=1
    var f0 = 1 / (1 + exp(k * 0.5));
    var f1 = 1 / (1 + exp(-k * 0.5));

    return (sigmoid - f0) / (f1 - f0);
}

/**
 * Evaluate transition function by type.
 *
 * Dispatcher function that calls the appropriate transition function
 * based on the enum type. Useful when transition type is a parameter.
 *
 * @param t {number} : Parameter in [0, 1]
 * @param transitionType {TransitionType} : Which transition function to use
 * @returns {number} : Transition value in [0, 1]
 *
 * @example `evaluateTransition(0.5, TransitionType.SINUSOIDAL)` returns `0.5`
 */
export function evaluateTransition(t is number, transitionType is TransitionType) returns number
{
    if (transitionType == TransitionType.LINEAR)
        return linearTransition(t);
    else if (transitionType == TransitionType.SINUSOIDAL)
        return sinusoidalTransition(t);
    else if (transitionType == TransitionType.LOGISTIC)
        return logisticTransition(t);

    // Default to linear if unknown type
    return linearTransition(t);
}

/**
 * Compute blended scale factor using transition function.
 *
 * Blends smoothly between two scale factors (sfStart, sfEnd) using
 * parameter s in [0,1] and the specified transition type.
 *
 * Formula: sf(s) = sfStart + (sfEnd - sfStart) * transition(s)
 *
 * This is used extensively in curve blending and modification operations
 * to create smooth variations in scale factors along a curve.
 *
 * @param s {number} : Parameter in [0, 1]
 * @param sfStart {number} : Scale factor at s=0
 * @param sfEnd {number} : Scale factor at s=1
 * @param transitionType {TransitionType} : Transition function to use
 * @returns {number} : Blended scale factor
 *
 * @example Blend from 0.5 to 1.0 with sinusoidal transition:
 *   `computeAppliedSF(0, 0.5, 1.0, TransitionType.SINUSOIDAL)` returns `0.5`
 *   `computeAppliedSF(0.5, 0.5, 1.0, TransitionType.SINUSOIDAL)` returns `0.75`
 *   `computeAppliedSF(1, 0.5, 1.0, TransitionType.SINUSOIDAL)` returns `1.0`
 *
 * @source gordonSurface/modifyCurveEnd.fs, gordonSurface/scaledCurve.fs (pattern)
 */
export function computeAppliedSF(s is number, sfStart is number, sfEnd is number,
                                  transitionType is TransitionType) returns number
{
    var blendFactor = evaluateTransition(s, transitionType);
    return sfStart + (sfEnd - sfStart) * blendFactor;
}
