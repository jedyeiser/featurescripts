FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/f978f8e46256a09d3349266a/683d867c35fdab9c98d47556", version : "b6844b2ef23e11ceb42f1d75");
//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/f978f8e46256a09d3349266a/ad98c7f43a25a4c0e8a428e7", version : "1efe2444c4d02973fe212fcc");

//testMapping
/**
 * This feature inherits much from wrapCurve and createCavityDepthProfile
 * Take input edges and reference point to create a frenetPath
 * Options to flip direction, flip normal, flip binormal of the frenet path
 * user defines region endpoints with a distance along the path (X), or queries. 
 * Each region has a type {LINEAR, QUADRATIC, SMOOTH} and an extent definition type. 
 * User specifies start location, end location (x or query), start normal offset, start binormal offset, end normal offset, end binormal offset
 * Same information is conveyed for endDefinition. 
 * 
 * Provide spline approximation parameters, but make sure to use approximateIndicies correctly. 
 * 
 * Intersections work the same way as createCavityDepthProfile
 * collect all output bodies, use opExtractWires to combine edges, and then delete the original bodies
 */

export enum RegionType
{
    LINEAR,
    QUADRATIC,
    SMOOTH
}

export enum RegionExtentType
{
    QUERY,
    X_EXTENTS
}

export enum QuadraticZeroSlope
{
    AT_START,
    AT_END
}

export const RegionPointsBounds = {(unitless) : [20, 50, 100]} as IntegerBoundSpec;

export function generateCavityDepthProfileEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    return definition;
}
annotation { "Feature Type Name" : "Offset edges", "Feature Type Description" : "Takes input edges, region and intersection information and produces a new wire body with the offsets applied" }
export const offsetEdges = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Reference edges", "Filter" : (EntityType.BODY && BodyType.WIRE) || EntityType.EDGE, "Description" : "Edges to offset from. Must form a G1 continuous path"}
        definition.userSelection is Query;
        
        annotation { "Name" : "Number of evaluation points per region" }
        isInteger(definition.numRegionPoints, RegionPointsBounds);
        
        
        annotation { "Name" : "Regions", "Item name" : "Widget", "Item label template" : "#name" }
        definition.regions is array;
        for (var region in definition.myWidgets)
        {
            annotation { "Name" : "Region type", "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.regionType is RegionType;
            
            annotation { "Name" : "Region name" }
            region.name is string;
            
            annotation { "Name" : "Region extents from" }
            region.extentsFrom is MyEnum;
            
            
            
            
        }
        
        annotation { "Name" : "Intersections", "Item name" : "Widget", "Item label template" : "#myParameter" }
        definition.Intersections is array;
        for (var widget in definition.myWidgets)
        {
            // Nested parameters defined here, as e.g. widget.myParameter
        }
        
        annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
        {
            // insert parameters here
        }
        
        annotation { "Name" : "Debug", "Default" : false }
        definition.debug is boolean;
        
        if (definition.debug)
        {
            annotation { "Group Name" : "Debug", "Collapsed By Default" : false, "Driving Parameter" : "debug" }
            {
                // show frenet frames
                // print region data
            }
        }
        
        
        
        
    }
    {
        //get unique edges from userSelection. Need to extract edges from wires. 
        // evaluate 
    });
