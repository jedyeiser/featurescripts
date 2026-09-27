FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Transition and blending functions for smooth parameter transitions.
 *
 * Monotonic functions mapping [0, 1] -> [0, 1] with f(0) = 0 and f(1) = 1 and different
 * smoothness at the ends. Used for blending scale factors, applying smooth offsets, and
 * creating smooth transitions in curve modifications.
 *
 * @source gordonSurface/modifyCurveEnd.fs, gordonSurface/scaledCurve.fs
 */

/**
 * Transition function types, by how smoothly they start and stop (derivatives at t = 0 and t = 1):
 *
 * - LINEAR: f' = 1 at both ends (a corner where it meets a constant).
 * - SINUSOIDAL: f' = 0 at both ends; f'' = +-pi^2/2 at the ends (not zero).
 * - LOGISTIC: steep sigmoid (k = 10); f'(0) = f'(1) = 0.067 (small, NOT zero).
 * - SMOOTHERSTEP: 6t^5 - 15t^4 + 10t^3; f' = f'' = 0 at both ends (C2 against a constant).
 *
 * New members go at the END (saved features store the member name; order only affects the dropdown).
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
    LOGISTIC,
    annotation { "Name" : "Smootherstep" }
    SMOOTHERSTEP
}

/**
 * Linear transition: f(t) = t
 *
 * Constant rate of change (f'(t) = 1 everywhere), so it leaves a slope corner where it meets
 * a constant (for example the unchanged part of a curve).
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
 * Sinusoidal transition: f(t) = (1 - cos(pi t)) / 2
 *
 * Eases in and out: f'(0) = f'(1) = 0. The second derivative is pi^2/2 at t = 0 and -pi^2/2
 * at t = 1, so it is C1 (not C2) against a constant.
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
 * Logistic (sigmoid) transition: s(t) = 1 / (1 + e^(-k (t - 0.5))) with k = 10, rescaled so
 * f(0) = 0 and f(1) = 1: f(t) = (s(t) - s(0)) / (s(1) - s(0)).
 *
 * Infinitely differentiable inside [0, 1], and steeper in the middle than SINUSOIDAL. Its end
 * derivatives do NOT vanish: f'(0) = f'(1) = k s(0) (1 - s(0)) / (s(1) - s(0)) = 0.067, and
 * f''(0) = +0.66, f''(1) = -0.66 (all small, none zero). Use SMOOTHERSTEP when the ends must be
 * flat to second order.
 *
 * Properties: f(0) = 0, f(0.5) = 0.5, f(1) = 1 (exactly, by the rescaling).
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
 * Smootherstep transition (Perlin): f(t) = 6t^5 - 15t^4 + 10t^3 = t^3 (t (6t - 15) + 10)
 *
 * The lowest-degree polynomial with f(0) = 0, f(1) = 1 and f' = f'' = 0 at BOTH ends:
 *   f'(t)  = 30 t^2 (t - 1)^2
 *   f''(t) = 60 t (t - 1) (2t - 1)
 * so it meets a constant with C2 continuity at either end. Symmetric: f(1 - t) = 1 - f(t),
 * f(0.5) = 0.5, peak slope f'(0.5) = 1.875.
 *
 * @param t {number} : Parameter in [0, 1] (clamped)
 * @returns {number} : Blend value in [0, 1]
 *
 * @example `smootherstepTransition(0.5)` returns `0.5`
 */
export function smootherstepTransition(t is number) returns number
{
    const x = clamp(t, 0, 1);
    return x * x * x * (x * (6 * x - 15) + 10);
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
    {
        return linearTransition(t);
    }
    else if (transitionType == TransitionType.SINUSOIDAL)
    {
        return sinusoidalTransition(t);
    }
    else if (transitionType == TransitionType.LOGISTIC)
    {
        return logisticTransition(t);
    }
    else if (transitionType == TransitionType.SMOOTHERSTEP)
    {
        return smootherstepTransition(t);
    }

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
