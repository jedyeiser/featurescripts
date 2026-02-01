FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

//import tools/transition_functions (for TransitionType)
import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/a656fa0d17723f0dafaf8638", version : "56689ead56dff6bcc596641b");

//import tools/printing (for PrintFormat)
export import(path : "b1e8bfe71f67389ca210ed8b/96aed2c3625444f0bea650a0/b02d6a2bac551b24347c983f", version : "c104606e8ffc8e0964404bbc");
// NOTE: GeometricContinuity is NOT imported from tools because tools uses C0/C1/C2 (parametric)
// but Gordon Surface needs G0/G1/G2 (geometric) - defined below
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "2856.0");


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

// TransitionType now imported from tools/transition_functions (above)

export const ScaledCurveParameterBounds = {(unitless): [-.5, 0, .5]} as RealBoundSpec;

export const SampleCountBounds = {(unitless): [5, 15, 200]} as IntegerBoundSpec;
export const FitToleranceBounds = {(millimeter): [1e-5, 1e-3, 10]} as LengthBoundSpec;

// PrintFormat now imported from tools/printing (above)

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

// GeometricContinuity (G0/G1/G2) is now imported from Onshape std library
// Available from onshape/std/common.fs

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