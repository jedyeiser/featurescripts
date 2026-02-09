FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");


/**
 * Shared configuration constants for footprint analysis and processing.
 *
 * This module centralizes default values, tolerances, and bounds used
 * across the footprint feature suite.
 */

/**
 * Default configuration values for footprint analysis algorithms.
 *
 * Used by buildConfig() in fpt_analyze.fs to provide sensible defaults
 * for solver and tolerance parameters.
 */
export const FOOTPRINT_CONFIG_DEFAULTS = {
    "tangentSolveTol" : 1e-12,           // Convergence tolerance for tangent solvers
    "paramBracketSamples" : 50,          // Number of samples for parameter bracketing
    "maxSolverIterations" : 30,          // Maximum iterations for root finding
    "xTolerance" : 0.001 * millimeter,   // X-coordinate tolerance for feature detection
    "yTolerance" : 0.001 * millimeter    // Y-coordinate tolerance for feature detection
};

/**
 * Length bounds for footprint geometry processing.
 *
 * Used in fpt_geometry.fs for sketch extent ranges and initial geometry setup.
 * Range: -3m to 3m covers typical ski/snowboard profiles with margin.
 */
export const FOOTPRINT_LENGTH_BOUNDS = {
    "min" : -3 * meter,
    "max" : 3 * meter
};
