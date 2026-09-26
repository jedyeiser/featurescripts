FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

export const CamberHeightBounds = {(millimeter) : [0, 5, 15]} as LengthBoundSpec;
export const RockerHeightBounds = {(millimeter) : [0, 5, 35]} as LengthBoundSpec;
export const RockerLengthBounds ={(millimeter) : [0, 150, 500]} as LengthBoundSpec;
export const MinPointDistBounds = {(millimeter) : [20, 150, 300]} as LengthBoundSpec;

export enum BaselineCurveOutputType
{
    SINGLE_CURVE,
    CURVE_PER_REGION
}
