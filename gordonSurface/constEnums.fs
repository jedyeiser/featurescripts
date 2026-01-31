FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

export const paramBounds = {(unitless) : [0, 0.5, 1]} as RealBoundSpec;

export enum curveCreationMethod
{
    annotation {"Name" : "Sample points along curves"}
    SAMPLE,
    annotation {"Name" : "Enforce compatibility"}
    UNIFY,
    annotation {"Name" : "Implied from surfaces"}
    IMPLIED,
}

export const interiorCurveCountBounds = {(unitless) : [ 1, 3, 15]} as IntegerBoundSpec;
export const sampleNumberBounds = {(unitless) : [5, 20, 100]} as IntegerBoundSpec;
export const curveDegreeBounds = {(unitless) : [2, 3, 10]} as IntegerBoundSpec;

export enum TransitionType
{
    annotation { "Name" : "Linear" }
    LINEAR,
    annotation { "Name" : "Sinusoidal" }
    SINUSOIDAL,
    annotation { "Name" : "Logistic" }
    LOGISTIC
}

export const ScaledCurveParameterBounds = {(unitless): [-.5, 0, .5]} as RealBoundSpec;

export const SampleCountBounds = {(unitless): [5, 15, 200]} as IntegerBoundSpec;
export const FitToleranceBounds = {(millimeter): [1e-5, 1e-3, 10]} as LengthBoundSpec;

export enum PrintFormat
{
    annotation { "Name" : "Metadata" }
    METADATA,
    annotation { "Name" : "Details" }
    DETAILS
}

export enum BlendMode
{
    annotation { "Name" : "Linear blend" }
    LINEAR_BLEND,
    annotation { "Name" : "Cross sample" }
    CROSS_SAMPLE,
    annotation { "Name" : "Auto (guaranteed intersections)" }
    AUTO
}

export enum OffsetFrame
{
    annotation { "Name" : "World" }
    WORLD,
    annotation { "Name" : "Frenet" }
    FRENET
}

export enum ContinuityType
{
    annotation { "Name" : "G0 (Position)" }
    G0,
    annotation { "Name" : "G1 (Tangent)" }
    G1,
    annotation { "Name" : "G2 (Curvature)" }
    G2
}

export enum G2Mode
{
    annotation { "Name" : "Exact" }
    EXACT,
    annotation { "Name" : "Best effort" }
    BEST_EFFORT
}

export enum VParamMode
{
    annotation { "Name" : "Uniform" }
    UNIFORM,
    
    annotation { "Name" : "Chord length" }
    CHORD_LENGTH
}

export const SurfDegreeBounds = {(unitless) : [2, 3, 15]} as IntegerBoundSpec;

export enum CleanupMode { AUTO, MANUAL }