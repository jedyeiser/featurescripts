FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * This tool/these tools are used to estimate beam deflections using
 * Euler Bernouli beam bending equations. User supplies stiffness curve, context info
 * and array of load/support conditions. Function calculates deflection and creates a curve matching that deflection. 
 * 
 * User provides a query for EI edges. Additional EI can be added with an additional EI query. 
 * User specifies if the analysis should have an applied moment, and if so, where that moment should be applied. 
 * User specifies number of evaluation points
 * User specifies total applied load. 
 * User specifies applied load ratio (load 1/ load 2)
 * User specifies two applied load shapes. 
 * User specifies up to three load shapes. These are assumed to be the forebody support, the aftbody support, and the center support. 
 * If three support shapes are specified, the user must specify what percentage of the support force goes on the center load. 
 * Total loads/supports are solved to provide shear and moment estimates. 
 * 
 * We can combine the moment with the EI at each X point and integrate twice. 
 * WE enforce the boundary condition that the deflection at the outermost SUPPORTS is zero. 
 * 
 * Each load has a shape, a center and a width (except a point load). 
 * Constant - constant load throughout the width
 * Linear - Triangular load with maximum at the center of the span
 * Quadratic - Quadratically shaped load with maximum at the center (zero slope)
 * Quintic - Qunitic approximation of a gaussian curve. Easy to solve for with repeated roots. 
 * Logistic - Two mirrored logistic functions with maximum at the center
 * 
 * calculate the deflection of the beam, and create an approximated curve of the deflected curve. Provide standard curve approximating
 * inputs
 */
 
 export enum LoadType
 {
     POINT,
     CONSTANT,
     LINEAR,
     QUADRATIC,
     QUINTIC,
     LOGISTIC
 }
 
 export enum LocationType
 {
     QUERY,
     X_VAL
 }
 
 export const LoadBalanceBounds = {(unitless) : [0.001, .75, .999]} as RealBoundSpec;
 export const EvaluationPointBounds = {(unitless) : [20, 100, 500]} as IntegerBoundSpec;
 
 annotation { "Feature Type Name" : "Estimate Deflection",
             "Editing Logic Function" : "estimateDeflectionEditLogic",
             "Feature Type Description" : "Estimates beam deflection given an EI profile and loading conditions" }
 export const estimateDeflection = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "EI Edges", "Filter" : EntityType.EDGE, "Description" : "Dedges defining EI profile" }
         definition.selEI is Query;
         
         annotation { "Name" : "Number of evaluation points" }
         isInteger(definition.numEvalPoints, EvaluationPointBounds);
         
         
         annotation { "Name" : "Add plate/mounting conditions?", "Default" : false, "Description" : "When true, allows users to add an additional query for plate stiffness as well as apply a mounting reaction moment" }
         definition.addPlateConditions is boolean;
         
         if (definition.addPlateConditions)
         {
             annotation { "Group Name" : "Plate conditions", "Collapsed By Default" : false, "Driving Parameter" : "addPlateConditions" }
             {
                 annotation { "Name" : "Plate stiffness", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                 definition.plateStiffness is Query;
                 
                 annotation { "Name" : "Reaction moment", "Description" : "Moment from forward pressure in Nm", "Default" : 0}
                 isReal(definition.reactionMoment, POSITIVE_REAL_BOUNDS);
                 
                 annotation { "Name" : "Moment x location", "Description" : "We assume that EI profile is in XZ plane. This is the location of the reaction moment in X", "Default" : 0 * meter }
                 isLength(definition.reactionMomentX, LENGTH_BOUNDS);
             }
             
         }
         
         annotation { "Group Name" : "Applied load data", "Collapsed By Default" : false }
         {
             annotation { "Name" : "Applied load balance", "Description" : "The percentage of the applied load borne by the first Applied load. When only one applied load is provided, this defaults to 1" }
             isReal(definition.appliedLoadBalance, LoadBalanceBounds);
             
             annotation { "Group Name" : "Applied load 1", "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Applied load 1 Location Type" }
                 definition.applied1LocationType is LocationType;
                                     
                annotation { "Name" : "Applied load 1 load shape", "Description" : "Shape of the applied load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                definition.applied1LoadShape is LoadType;
                
                annotation { "Name" : "applied1NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.applied1NeedsWidth is boolean;
                
                if (definition.applied1NeedsWidth)
                {
                    annotation { "Name" : "Applied load 1 width" }
                    isLength(definition.applied1Width, LENGTH_BOUNDS);
                }
                 
                 annotation { "Name" : "applied1IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                 definition.applied1IsQuery is boolean;
                 
                 if (definition.applied1IsQuery)
                 {
                     annotation { "Name" : "Applied load 1 query", "Description" : "Location of center of applied 1 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                     definition.applied1Query is Query;
                 }
                 else
                 {
                     annotation { "Name" : "Applied load 1 x location", "Description" : "X location of first applied load center", "Default" : 0 * meter }
                     isLength(definition.applied1X, LENGTH_BOUNDS);
                 }
                 
             }
             
             annotation { "Name" : "Second applied load?", "Default" : false }
             definition.secondApplied is boolean;
             
             if (definition.secondApplied)
             {
                 annotation { "Group Name" : "Applied load 2", "Collapsed By Default" : false }
                 {
                     annotation { "Name" : "Applied load 2 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                     definition.applied2LocationType is LocationType;
                     
                     annotation { "Name" : "Applied load 2 shape", "Description" : "Shape of the applied load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                     definition.applied2LoadShape is LoadType;
                     
                     annotation { "Name" : "applied2NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                     definition.applied2NeedsWidth is boolean;
                     
                     if (definition.applied2NeedsWidth)
                     {
                         annotation { "Name" : "Applied load 2 width" }
                         isLength(definition.applied2Width, LENGTH_BOUNDS);
                     }
                     
                     annotation { "Name" : "applied2IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                     definition.applied2IsQuery is boolean;
                     
                     if (definition.applied2IsQuery)
                     {
                         annotation { "Name" : "Applied load 2 query", "Description" : "Location of center of applied load 2", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                         definition.applied2Query is Query;
                     }
                     else
                     {
                         annotation { "Name" : "Applied load 2 x location", "Description" : "X location of second applied load center", "Default" : 0 * meter }
                         isLength(definition.applied2X, LENGTH_BOUNDS);
                     }
                     
                 }
             }
             
             
             
         }
         
         annotation { "Group Name" : "Support load data", "Collapsed By Default" : false }
         {
            annotation { "Name" : "Add third support load", "Default" : false, "Description" : "When true, allows the addition of a third support load. A ratio of support loads between the 'standard 2' and the thrid needs to be established" }
            definition.addThirdSupport is boolean;
            
            if (definition.addThirdSupport)
            {
                annotation { "Name" : "Support load balance", "Description" : "The percentage of the support load borne by the third support load." }
                isReal(definition.supportLoadBalance, LoadBalanceBounds);
            }
            
            annotation { "Group Name" : "Support load 1", "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Support load 1 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                 definition.support1LocationType is LocationType;
                                     
                annotation { "Name" : "Support load 1 load shape", "Description" : "Shape of the support load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                definition.support1LoadShape is LoadType;
                
                annotation { "Name" : "support1NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.support1NeedsWidth is boolean;
                
                if (definition.support1NeedsWidth)
                {
                    annotation { "Name" : "Support load 1 width" }
                    isLength(definition.support1Width, LENGTH_BOUNDS);
                }
                 
                 annotation { "Name" : "support1IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                 definition.support1IsQuery is boolean;
                 
                 if (definition.support1IsQuery)
                 {
                     annotation { "Name" : "Support load 1 query", "Description" : "Location of center of support 1 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                     definition.support1Query is Query;
                 }
                 else
                 {
                     annotation { "Name" : "Support load 1 x location", "Description" : "X location of first support load center", "Default" : 0 * meter }
                     isLength(definition.support1X, LENGTH_BOUNDS);
                 }
                 
             }
             
             annotation { "Group Name" : "Support load 2", "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Support load 2 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                 definition.support2LocationType is LocationType;
                                     
                annotation { "Name" : "Support load 2 load shape", "Description" : "Shape of the support load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                definition.support2LoadShape is LoadType;
                
                annotation { "Name" : "support2NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.support2NeedsWidth is boolean;
                
                if (definition.support2NeedsWidth)
                {
                    annotation { "Name" : "Support load 2 width" }
                    isLength(definition.support2Width, LENGTH_BOUNDS);
                }
                 
                 annotation { "Name" : "support2IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility. 
                 definition.support2IsQuery is boolean;
                 
                 if (definition.support2IsQuery)
                 {
                     annotation { "Name" : "Support load 2 query", "Description" : "Location of center of support 2 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                     definition.support2Query is Query;
                 }
                 else
                 {
                     annotation { "Name" : "Support load 2 x location", "Description" : "X location of second support load center", "Default" : 0 * meter }
                     isLength(definition.support2X, LENGTH_BOUNDS);
                 }
                 
             }
             
             if (definition.addThirdSupport)
             {
                annotation { "Group Name" : "Support load 3", "Collapsed By Default" : false }
                {
                    annotation { "Name" : "Support load 3 Location Type", "UIHint" : UIHint.SHOW_LABEL }
                    definition.support3LocationType is LocationType;
                                         
                    annotation { "Name" : "Support load 3 load shape", "Description" : "Shape of the support load" , "Default" : LoadType.POINT, "UIHint" : UIHint.SHOW_LABEL  }
                    definition.support3LoadShape is LoadType;
                    
                    annotation { "Name" : "support3NeedsWidth", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                    definition.support3NeedsWidth is boolean;
                    
                    if (definition.support3NeedsWidth)
                    {
                        annotation { "Name" : "Support load 3 width" }
                        isLength(definition.support3Width, LENGTH_BOUNDS);
                    }

                     annotation { "Name" : "support3IsQuery", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN } // toggles query, x location visibility.
                     definition.support3IsQuery is boolean;

                     if (definition.support3IsQuery)
                     {
                         annotation { "Name" : "Support load 3 query", "Description" : "Location of center of support 3 load", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                         definition.support3Query is Query;
                     }
                     else
                     {
                         annotation { "Name" : "Support load 3 x location", "Description" : "X location of third support load center", "Default" : 0 * meter }
                         isLength(definition.support3X, LENGTH_BOUNDS);
                     }
                     
                 }   
             }
            
            
            
         }
         
         
         
         
         
     }
     {
         // 1. Check to make sure EI curves are continuous
         // 2. Solve for load areas based on geometry, moment balance, etc. 
         // 3. Solve for shear, moment profile
         // 4. solve for change in curvature. Integrate twice. Apply boundary conditions such that 'outside' supports (the last point to get a support load, not the center of the last load) have zero deflection. 
         // 5. return curve
     });
 
 
export function estimateDeflectionEditLogic(context is Context, id is Id, oldDefinition is map,
    definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    debug("estimateDeflectionEditLogic called");
    debug(definition.applied1LocationType);
    debug(definition.applied1IsQuery);

    // Applied load 1 (always visible)
    definition.applied1NeedsWidth = (definition.applied1LoadShape != LoadType.POINT);
    definition.applied1IsQuery    = (definition.applied1LocationType == LocationType.QUERY);

    // Applied load 2 (shown when secondApplied == true)
    definition.applied2NeedsWidth = (definition.applied2LoadShape != LoadType.POINT);
    definition.applied2IsQuery    = (definition.applied2LocationType == LocationType.QUERY);

    // Support load 1 (always visible)
    definition.support1NeedsWidth = (definition.support1LoadShape != LoadType.POINT);
    definition.support1IsQuery    = (definition.support1LocationType == LocationType.QUERY);

    // Support load 2 (always visible)
    definition.support2NeedsWidth = (definition.support2LoadShape != LoadType.POINT);
    definition.support2IsQuery    = (definition.support2LocationType == LocationType.QUERY);

    // Support load 3 (shown when addThirdSupport == true)
    definition.support3NeedsWidth = (definition.support3LoadShape != LoadType.POINT);
    definition.support3IsQuery    = (definition.support3LocationType == LocationType.QUERY);

    return definition;
}
