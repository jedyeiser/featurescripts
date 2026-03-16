FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");

export enum RegionExtentDef
{
    ALONG_REF,
    QUERY
}

export enum SamplingDensityType
{
    NUM_POINTS,
    CTRL_POINT_MULTIPLIER
}


export const RegionNumBounds = {(unitless) : [0, 0, 10]} as IntegerBoundSpec;
export const ApproxToleranceBounds      = {(millimeter) : [0.001, 0.01, 1]} as LengthBoundSpec;
export const ApproxDegreeBounds         = {(unitless) : [2, 3, 5]}        as IntegerBoundSpec;
export const ApproxMaxCPBounds          = {(unitless) : [10, 100, 500]}   as IntegerBoundSpec;
export const SamplingDensityBounds      = {(unitless) : [5, 50, 500]}     as IntegerBoundSpec;