FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

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


export const RegionNumBounds = {(unitless) : [0, 0, 100]} as IntegerBoundSpec;
export const ApproxToleranceBounds      = {(millimeter) : [0.001, 0.01, 1]} as LengthBoundSpec;
export const ApproxDegreeBounds         = {(unitless) : [2, 3, 5]}        as IntegerBoundSpec;
export const ApproxMaxCPBounds          = {(unitless) : [10, 100, 500]}   as IntegerBoundSpec;
export const SamplingDensityBounds      = {(unitless) : [5, 50, 500]}     as IntegerBoundSpec;

export enum RegionOffsetType { CONSTANT, LINEAR, QUADRATIC, SMOOTH }
export enum IntersectionContinuityType { G0, G1 }

export const REGION_OFFSET_BOUNDS         = { (millimeter) : [-500, 0, 500] } as LengthBoundSpec;
export const REGION_SURFACE_HEIGHT_BOUNDS = { (millimeter) : [0, 20, 100] }   as LengthBoundSpec;
export const INTERSECTION_OFFSET_BOUNDS   = { (millimeter) : [0, 0, 500] }   as LengthBoundSpec;