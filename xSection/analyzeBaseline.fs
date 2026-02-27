FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * This function takes a query of multiple edges (must be G1 continuous)
 * Takes an FCP location query
 * Takes an ACP location query
 * 
 * Creates a path through query edges
 * Finds curve point at FCP
 * Finds curve point at ACP
 * 
 * MRS = (FCP + ACP)/2
 * 
 * Forebody minimum = point of minimum s of baseline curves starting at MRS and moving towards FCP. Record point(x, y, z)
 * Aftbody minimum = minimum z point of baseline curves starting at MRS and moving towards ACP. Record point (x, y, z). 
 * 
 * baseline_bottom_line = line that connects forebody and aftbody minima. 
 * 
 * Camber height = maximum height of baseline from baseline_bottom_line between forebody and aftbody minima. 
 * 
 * Find rocker points - or the outermost inflection points along our baseline edges. Thes inflection points might be at junctions between edges.
 * 
 * Find tangent lines to baseline at inflection points (FB/Aftbody). Measure minimum distance between these lines and the curve intersection point at FCP/ACP. 
 * These are our FCP/ACP heights. 
 * 
 * Function just runs edting logic. Logic can be used elsewhere. 
 * 
 */ 


export function analyzeBaselineEditLogic(context is Context, id is Id, oldDefinition is map,
   definition is map, isCreating is boolean, specifiedParameters is map, clickedButton is string) returns map
{
    return definition;
}

annotation { "Feature Type Name" : "Analze baseline", 
"Feature Type Description" : "Takes input edge queries, fcp and acp locations and generates ", 
"Editing Logic Function" : "analyzeBaselineEditLogic"}
export const analyzeBaseline = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Baseline edges", "Filter" : EntityType.EDGE, "Description" : "Edges that make up the baseline to be analyzed. must be G1 continuous"}
        definition.baselineEdges is Query;
        
        annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || GeometryType.PLANE || BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.fcpQ is Query;
        
        annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || GeometryType.PLANE || BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.acpQ is Query;
        
        annotation { "Group Name" : "Calculated data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "FB min", "Description" : "String of vector in mm for minimum fb point" }// string vector
            definition.fbMinString is string;
            
            annotation { "Name" : "AB min", "Description" : "String of vector in mm for minimum ab point" }// string vector
            definition.abMinString is string;
            
            annotation { "Name" : "Max camber point", "Description" : "String of vector in mm for max camber point" }// string vector
            definition.maxCamberHeightString is string;
            
            annotation { "Name" : "Camber height" }
            isLength(definition.camberHeight, LENGTH_BOUNDS);
            
            annotation { "Name" : "FRCP", "Description" : "Forebody rocker contact point" } // string vector
            definition.frcp is string;
            
            annotation { "Name" : "FRCPL", "Description" : "Forebody rocker contact point length. Distance between fcp and frcp" }
            isLength(definition.frcpl, LENGTH_BOUNDS);
            
            annotation { "Name" : "FRCP tangent line", "Description" : "Forebody rocker tangent line" } // string description of line (origin on baseline, slope)
            definition.frcpLine is string;
            
            annotation { "Name" : "FCPH", "Description" : "Height of FCP when baseline is weighted. Distance between FCP point and forebody rocker tangent line" }
            isLength(definition.fcph, LENGTH_BOUNDS);
            
            annotation { "Name" : "ARCP", "Description" : "Aftbody rocker contact point" } // string vector
            definition.arcp is string;
            
            annotation { "Name" : "ARCPL", "Description" : "Aftebody rocker contact point length. Distance between fcp and frcp" }
            isLength(definition.arcpl, LENGTH_BOUNDS);
            
            annotation { "Name" : "ARCP tangent line", "Description" : "Aftbody rocker tangent line" } // string description of line (origin on baseline, slope)
            definition.arcpLine is string;
            
            annotation { "Name" : "ACPH", "Description" : "Height of ACP when baseline is weighted. Distance between ACP point and aftbody rocker tangent line" }
            isLength(definition.acph, LENGTH_BOUNDS);
            
            
        }
        
        
        
    }
    {
        // Define the function's action
    });
