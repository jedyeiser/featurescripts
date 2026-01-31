FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");


/**
 * Consolidated constants and enums for footprint features.
 * This file serves as a single source of truth for all configuration values.
 */

// =============================================================================
// SCALING MODES
// =============================================================================

export enum FootprintScaleMode
{
    annotation { "Name" : "Accordion (X only)" }
    ACCORDION,

    annotation { "Name" : "Keep taper angle" }
    KEEP_TAPER,

    annotation { "Name" : "Scale radius" }
    SCALE_RADIUS
}

export enum ContinuityMassageMode
{
    annotation { "Name" : "Adjust tip/tail only" }
    TIP_TAIL_ONLY,

    annotation { "Name" : "Adjust sidecut only" }
    RSL_ONLY,

    annotation { "Name" : "Minimize change (both)" }
    MINIMUM
}

export enum FootprintSymmetryType
{
    SYMMETRIC,
    ASYYMMETRIC
}

// =============================================================================
// GEOMETRY MODES
// =============================================================================

export enum FootprintCurveBuildMode
{
    annotation { "Name" : "PER_REGION" }
    ONE_PER_REGION,

    annotation { "Name" : "PER_EDGE" }
    ONE_PER_EDGE
}

export enum AngleDriver
{
    annotation { "Name" : "Waist location" }
    WAIST,

    annotation { "Name" : "Overall taper angle" }
    TAPER_ANGLE
}

export enum FootprintSplineExportType
{
    annotation { "Name" : "FIT" }
    FIT,

    annotation { "Name" : "APPROX" }
    APPROX
}

export enum RadiusSign
{
    POS,
    NEG
}

// =============================================================================
// BOUNDS
// =============================================================================

export const ScaleRadiusBounds = {(meter) : [5, 18, 60]} as LengthBoundSpec;
export const ScaleWaistBounds = {(millimeter) : [25, 88, 500]} as LengthBoundSpec;

// =============================================================================
// ANALYSIS CONSTANTS
// =============================================================================

export const DEFAULT_ANALYSIS_TOLERANCE = 0.001 * millimeter;
export const CURVE_SPLIT_SAMPLES = 50;
export const ANALYSIS_SAMPLES = 100;
export const ENDPOINT_MATCH_TOLERANCE = 10 * millimeter;
export const Y_ZERO_CROSSING_SAMPLES = 20;

// =============================================================================
// DEFAULTS
// =============================================================================

export const FOOTPRINT_DATA_DEFAULTS = {
    "dimensionStr" : "- - -",
    "foundWaistWidth" : 0 * millimeter,
    "foundWaistLocation" : 0 * millimeter,
    "foundTaperAngle" : 0 * degree,
    "avgRadiusStr" : "-",
    "natRadiusWidestStr" : "-",
    "natRadiusInflectionStr" : "-",
    "fbWidest" : 0 * millimeter,
    "fbInflection" : 0 * millimeter,
    "fbInflectionToWidest" : 0 * millimeter,
    "abWidest" : 0 * millimeter,
    "abInflection" : 0 * millimeter,
    "abInflectionToWidest" : 0 * millimeter,
    "tipLength" : 0 * millimeter,
    "tailLength" : 0 * millimeter
};
