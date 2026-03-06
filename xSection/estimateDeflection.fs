FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: estimateDeflectionSolver.fs
import(path : "b94e66e5f6d027e5be938121", version : "");

/**
 * ESTIMATE DEFLECTION
 * ===================
 * Enums, bounds, and helper functions live in estimateDeflectionSolver.fs.
 * This file contains the editing logic and feature definition only.
 */

export function estimateDeflectionEditLogic(context is Context, id is Id, oldDefinition is map,
    definition is map, isCreating is boolean, specifiedParameters is map) returns map
{

    /* DISABLED — backOutEI backwards-compat + force-false; restore when re-enabling the feature
    if (definition.backOutEI == undefined)      { definition.backOutEI = false; }
    if (definition.trimBoundaries == undefined) { definition.trimBoundaries = []; }
    if (definition.regionParams == undefined)   { definition.regionParams = []; }
    if (definition.allCpX == undefined)         { definition.allCpX = []; }
    if (definition.allCpZ == undefined)         { definition.allCpZ = []; }
    if (definition.cpRegionSizes == undefined)  { definition.cpRegionSizes = []; }
    if (definition.cpIsInitialized == undefined){ definition.cpIsInitialized = []; }
    if (definition.storedScaleK == undefined)   { definition.storedScaleK = 1e-3; }
    definition.backOutEI = false;
    */

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

    definition.approxNeedsOptions = (definition.curveOutput == CurveOutput.APPROX);

    // --- Back out EI logic --- DISABLED — restore when re-enabling backOutEI feature
    /* if (definition.backOutEI)
    {
        // Initialize trimBoundaries if empty or undefined
        if (definition.trimBoundaries == undefined || size(definition.trimBoundaries) == 0)
        {
            var startX = 0 * meter;
            var endX = 1 * meter;
            // Use support positions if both are X_VAL type
            if (definition.support1LocationType == LocationType.X_VAL &&
                definition.support2LocationType == LocationType.X_VAL)
            {
                if (definition.support1X < definition.support2X)
                {
                    startX = definition.support1X;
                    endX = definition.support2X;
                }
                else
                {
                    startX = definition.support2X;
                    endX = definition.support1X;
                }
            }
            definition.trimBoundaries = [
                { "boundaryName" : "Start", "boundaryX" : startX },
                { "boundaryName" : "End",   "boundaryX" : endX }
            ];
        }

        // Sort trimBoundaries by boundaryX ascending (insertion sort)
        for (var i = 1; i < size(definition.trimBoundaries); i += 1)
        {
            var key = definition.trimBoundaries[i];
            var j = i - 1;
            while (j >= 0 && definition.trimBoundaries[j].boundaryX > key.boundaryX)
            {
                definition.trimBoundaries[j + 1] = definition.trimBoundaries[j];
                j -= 1;
            }
            definition.trimBoundaries[j + 1] = key;
        }

        // Sync regionParams length to trimBoundaries.length - 1
        var nRegions = size(definition.trimBoundaries) - 1;
        if (nRegions < 0)
        {
            nRegions = 0;
        }
        if (definition.regionParams == undefined)
        {
            definition.regionParams = [];
        }
        var currentSize = size(definition.regionParams);

        // Add default entries for new regions
        for (var k = currentSize; k < nRegions; k += 1)
        {
            definition.regionParams = append(definition.regionParams, {
                "regionType"     : RegionType.APPROXIMATE,
                "approxDegree"   : 3,
                "maxCP"          : 20,
                "regionTolerance": 1e-4 * meter
            });
        }

        // Remove extra entries if boundaries were deleted
        if (currentSize > nRegions)
        {
            var trimmedParams = [];
            for (var k = 0; k < nRegions; k += 1)
            {
                trimmedParams = append(trimmedParams, definition.regionParams[k]);
            }
            definition.regionParams = trimmedParams;
        }

        // Validate: first and last regions cannot be BRIDGING (no neighbor on one side)
        var nR = size(definition.regionParams);
        if (nR > 0)
        {
            if (definition.regionParams[0].regionType == RegionType.BRIDGING)
            {
                var r0 = definition.regionParams[0];
                r0.regionType = RegionType.APPROXIMATE;
                definition.regionParams[0] = r0;
            }
            if (nR > 1 && definition.regionParams[nR - 1].regionType == RegionType.BRIDGING)
            {
                var rLast = definition.regionParams[nR - 1];
                rLast.regionType = RegionType.APPROXIMATE;
                definition.regionParams[nR - 1] = rLast;
            }
        }

        // Reset cpIsInitialized for regions whose fit parameters changed
        var nRCheck = size(definition.regionParams);
        if (size(definition.cpIsInitialized) == nRCheck &&
            oldDefinition.regionParams != undefined &&
            size(oldDefinition.regionParams) == nRCheck)
        {
            for (var k = 0; k < nRCheck; k += 1)
            {
                var oldR = oldDefinition.regionParams[k];
                var newR = definition.regionParams[k];
                if (oldR.approxDegree    != newR.approxDegree   ||
                    oldR.maxCP           != newR.maxCP           ||
                    oldR.regionTolerance != newR.regionTolerance ||
                    oldR.regionType      != newR.regionType)
                {
                    definition.cpIsInitialized[k] = { "v" : false };
                }
            }
        }
        // If region count changed, reset all
        if (size(definition.cpIsInitialized) != nRCheck)
        {
            var resetInit = [];
            for (var k = 0; k < nRCheck; k += 1)
            {
                resetInit = append(resetInit, { "v" : false });
            }
            definition.cpIsInitialized = resetInit;
        }
    } */

    return definition;
}


// =============================================================================
// MANIPULATOR CHANGE FUNCTION — DISABLED (backOutEI feature trimmed out)
// =============================================================================
/* DISABLED — re-enable when restoring backOutEI / CP drag manipulators
export function estimateDeflectionManipulatorChange(
    context is Context, definition is map, newManipulators is map) returns map
{
    if (!definition.backOutEI)                   { return definition; }
    if (definition.cpRegionSizes == undefined)   { return definition; }

    var nReg = size(definition.regionParams);
    var tempCpZ = definition.allCpZ;          // value copy for mutation
    var scaleKVal = definition.storedScaleK;  // pure number = scaleK / (m²)

    var cpFlatOff = 0;
    for (var ri = 0; ri < nReg; ri += 1)
    {
        var sz = definition.cpRegionSizes[ri].v;
        for (var j = 0; j < sz; j += 1)
        {
            var key = "r" ~ toString(ri) ~ "c" ~ toString(j);
            if (newManipulators[key] is map)
            {
                // offset [m] / (scaleKVal [m²/m²] * m²) → kappa [1/m] → cpZ (= kappa * meter, dimensionless)
                var newKappa = newManipulators[key].offset / (scaleKVal * meter * meter);
                tempCpZ[cpFlatOff + j] = { "v" : newKappa * meter };
                // Mark region as initialized so feature body uses stored CPs
                definition.cpIsInitialized[ri] = { "v" : true };
            }
        }
        cpFlatOff = cpFlatOff + sz;
    }
    definition.allCpZ = tempCpZ;
    return definition;
}
*/

// Helper functions (interpEI, sampleEdgesForEI, resolveQueryX, loadIntensityAt,
// findNearestIndex) — defined in estimateDeflectionSolver.fs


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

         annotation { "Name" : "Debug mode", "Default" : false,
                      "Description" : "Draws shear/moment diagrams and load arrows for diagnosis (visible while editing only)" }
         definition.debugMode is boolean;

         annotation { "Name" : "Print load summary", "Default" : false,
                      "Description" : "Prints reactions, load catalogue, equilibrium check, and M boundary values to the FeatureScript console on each regen" }
         definition.printLoadSummary is boolean;


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
             annotation { "Name" : "Applied load" , "Description" : "Total applied load in N" }
             isReal(definition.appliedLoad, AppliedLoadBounds);
             
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

         annotation { "Group Name" : "Output Options", "Collapsed By Default" : false }
         {
             annotation { "Name" : "Output span" }
             definition.outputSpan is OutputSpan;

             annotation { "Name" : "Curve output type" }
             definition.curveOutput is CurveOutput;

             annotation { "Name" : "approxNeedsOptions", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
             definition.approxNeedsOptions is boolean;

             if (definition.approxNeedsOptions)
             {
                 annotation { "Group Name" : "Approximation Options", "Collapsed By Default" : false,
                              "Driving Parameter" : "approxNeedsOptions" }
                 {
                     annotation { "Name" : "Approximation tolerance",
                                  "Description" : "Maximum deviation of approximated curve from computed points" }
                     isLength(definition.approxTolerance, ApproxToleranceBounds);

                     annotation { "Name" : "Maximum control points" }
                     isInteger(definition.maxControlPoints, MaxControlPointsBounds);

                     annotation { "Name" : "Curve degree" }
                     isInteger(definition.curveDegree, ApproxDegreeBounds);
                 }
             }
         }

         /* DISABLED — backOutEI + CP storage arrays removed from precondition; re-enable to restore
         annotation { "Name" : "Back out EI", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
         definition.backOutEI is boolean;

         // Hidden flat CP storage — unconditional so Onshape always initializes to [] on passive regens
         annotation { "Name" : "allCpX", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "CpX" }
         definition.allCpX is array;
         for (var cpX in definition.allCpX)
         {
             annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 0 }
             isReal(cpX.v, { (unitless) : [-1e6, 0, 1e6] } as RealBoundSpec);
         }

         annotation { "Name" : "allCpZ", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "CpZ" }
         definition.allCpZ is array;
         for (var cpZ in definition.allCpZ)
         {
             annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 0 }
             isReal(cpZ.v, { (unitless) : [-1e6, 0, 1e6] } as RealBoundSpec);
         }

         annotation { "Name" : "cpRegionSizes", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "Sz" }
         definition.cpRegionSizes is array;
         for (var sz in definition.cpRegionSizes)
         {
             annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 0 }
             isInteger(sz.v, { (unitless) : [0, 0, 500] } as IntegerBoundSpec);
         }

         annotation { "Name" : "cpIsInitialized", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "Init" }
         definition.cpIsInitialized is array;
         for (var init in definition.cpIsInitialized)
         {
             annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
             init.v is boolean;
         }
         */ /* ALSO DISABLED — EI back-out UI temporarily commented out; re-enable with backOutEI visible to restore
         if (definition.backOutEI)
         {
             annotation { "Group Name" : "EI Back-Out", "Driving Parameter" : "backOutEI",
                          "Collapsed By Default" : false }
             {
                 annotation { "Name" : "Trim Boundaries", "Item name" : "Boundary",
                              "Show labels" : true, "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
                 definition.trimBoundaries is array;
                 for (var boundary in definition.trimBoundaries)
                 {
                     annotation { "Name" : "Label" }
                     boundary.boundaryName is string;

                     annotation { "Name" : "X" }
                     isLength(boundary.boundaryX, LENGTH_BOUNDS);
                 }

                 annotation { "Name" : "Regions", "Item name" : "Region",
                              "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
                 definition.regionParams is array;
                 for (var region in definition.regionParams)
                 {
                     annotation { "Name" : "Type" }
                     region.regionType is RegionType;

                     if (region.regionType != RegionType.BRIDGING)
                     {
                         annotation { "Name" : "Degree" }
                         isInteger(region.approxDegree, ApproxDegreeBounds);

                         annotation { "Name" : "Max CPs" }
                         isInteger(region.maxCP, { (unitless) : [4, 20, 500] } as IntegerBoundSpec);
                     }

                     if (region.regionType == RegionType.APPROXIMATE)
                     {
                         annotation { "Name" : "Tolerance" }
                         isLength(region.regionTolerance, ApproxToleranceBounds);
                     }
                 }

                 annotation { "Name" : "storedScaleK", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 1e-3 }
                 isReal(definition.storedScaleK, { (unitless) : [0, 1e-3, 1e12] } as RealBoundSpec);
             }
         }
         */
     }
     {
        // --- 1. Extract EI data from selected edges ---
        var eiData = sampleEdgesForEI(context, definition.selEI, definition.numEvalPoints);
        if (size(eiData) < 2)
        {
            throw regenError("Need at least 2 EI sample points. Check EI edge selection.");
        }

        // Trim leading zero-EI samples (shovel tip)
        while (size(eiData) > 2 && eiData[0].EI <= 0 * newton * meter * meter)
        {
            var trimmed = [];
            for (var k = 1; k < size(eiData); k += 1)
            {
                trimmed = append(trimmed, eiData[k]);
            }
            eiData = trimmed;
        }
        // Trim trailing zero-EI samples (ski tip)
        while (size(eiData) > 2 && eiData[size(eiData) - 1].EI <= 0 * newton * meter * meter)
        {
            var trimmed = [];
            for (var k = 0; k < size(eiData) - 1; k += 1)
            {
                trimmed = append(trimmed, eiData[k]);
            }
            eiData = trimmed;
        }
        if (size(eiData) < 2)
        {
            throw regenError("EI profile has no valid (non-zero) data after trimming. Check EI edge selection.");
        }

        var N = definition.numEvalPoints;
        var xMin = eiData[0].x;
        var xMax = eiData[size(eiData) - 1].x;

        // --- 1b. Sample plate stiffness (if enabled) ---
        var plateData = [];
        if (definition.addPlateConditions)
        {
            plateData = sampleEdgesForEI(context, definition.plateStiffness, definition.numEvalPoints);
        }

        // --- 2. Resolve support X positions ---
        var xs1;
        if (definition.support1IsQuery)
        {
            xs1 = resolveQueryX(context, definition.support1Query);
        }
        else
        {
            xs1 = definition.support1X;
        }

        var xs2;
        if (definition.support2IsQuery)
        {
            xs2 = resolveQueryX(context, definition.support2Query);
        }
        else
        {
            xs2 = definition.support2X;
        }

        if (abs(xs2 - xs1) < 1e-6 * meter)
        {
            throw regenError("Support 1 and Support 2 must be at different X positions.");
        }

        var xs3 = 0 * meter;
        if (definition.addThirdSupport)
        {
            if (definition.support3IsQuery)
            {
                xs3 = resolveQueryX(context, definition.support3Query);
            }
            else
            {
                xs3 = definition.support3X;
            }
        }

        // --- 3. Compute evaluation span ---
        var xEvalMin = xMin;
        var xEvalMax = xMax;
        if (definition.outputSpan == OutputSpan.SUPPORT_SPAN)
        {
            xEvalMin = xs1;
            xEvalMax = xs1;

            if (definition.support1LoadShape != LoadType.POINT)
            {
                var hw1 = definition.support1Width / 2;
                if (xs1 - hw1 < xEvalMin) { xEvalMin = xs1 - hw1; }
                if (xs1 + hw1 > xEvalMax) { xEvalMax = xs1 + hw1; }
            }

            if (xs2 < xEvalMin) { xEvalMin = xs2; }
            if (xs2 > xEvalMax) { xEvalMax = xs2; }
            if (definition.support2LoadShape != LoadType.POINT)
            {
                var hw2 = definition.support2Width / 2;
                if (xs2 - hw2 < xEvalMin) { xEvalMin = xs2 - hw2; }
                if (xs2 + hw2 > xEvalMax) { xEvalMax = xs2 + hw2; }
            }

            if (definition.addThirdSupport)
            {
                if (xs3 < xEvalMin) { xEvalMin = xs3; }
                if (xs3 > xEvalMax) { xEvalMax = xs3; }
                if (definition.support3LoadShape != LoadType.POINT)
                {
                    var hw3 = definition.support3Width / 2;
                    if (xs3 - hw3 < xEvalMin) { xEvalMin = xs3 - hw3; }
                    if (xs3 + hw3 > xEvalMax) { xEvalMax = xs3 + hw3; }
                }
            }

            // Clamp to EI data bounds
            if (xEvalMin < xMin) { xEvalMin = xMin; }
            if (xEvalMax > xMax) { xEvalMax = xMax; }
        }
        if (xEvalMax - xEvalMin < 1e-6 * meter)
        {
            throw regenError("Evaluation span is zero. Check support positions and output span setting.");
        }

        // --- 4. Build uniform eval grid ---
        var dx = (xEvalMax - xEvalMin) / (N - 1);
        var x_eval = [];
        for (var i = 0; i < N; i += 1)
        {
            x_eval = append(x_eval, xEvalMin + i * dx);
        }

        // --- 5. Resolve applied load X positions ---
        var x1;
        if (definition.applied1IsQuery)
        {
            x1 = resolveQueryX(context, definition.applied1Query);
        }
        else
        {
            x1 = definition.applied1X;
        }

        var x2 = 0 * meter;
        if (definition.secondApplied)
        {
            if (definition.applied2IsQuery)
            {
                x2 = resolveQueryX(context, definition.applied2Query);
            }
            else
            {
                x2 = definition.applied2X;
            }
        }

        // --- 6. Compute reactions by moment balance ---
        var F_total = definition.appliedLoad * newton;
        var F1;
        var F2;
        if (definition.secondApplied)
        {
            F1 = F_total * definition.appliedLoadBalance;
            F2 = F_total * (1 - definition.appliedLoadBalance);
        }
        else
        {
            F1 = F_total;
            F2 = 0 * newton;
        }

        var R1;
        var R2;
        var R3 = 0 * newton;
        var M0 = (definition.addPlateConditions ? definition.reactionMoment : 0) * newton * meter;
        if (definition.addThirdSupport)
        {
            R3 = F_total * definition.supportLoadBalance;
            var remainder = F_total - R3;
            var momentArm = F1 * (x1 - xs1) + F2 * (x2 - xs1) - R3 * (xs3 - xs1) + M0;
            R2 = momentArm / (xs2 - xs1);
            R1 = remainder - R2;
        }
        else
        {
            var momentArm = F1 * (x1 - xs1) + F2 * (x2 - xs1) + M0;
            R2 = momentArm / (xs2 - xs1);
            R1 = F_total - R2;
        }

        // --- 7. Classify loads as distributed or point loads ---
        // distLoads: { "center", "force", "shape", "width", "sign" }  sign: +1=upward, -1=downward
        // pointLoads: { "x", "force" }  force: positive=upward
        var distLoads = [];
        var pointLoads = [];

        // Support 1
        if (definition.support1LoadShape == LoadType.POINT)
        {
            pointLoads = append(pointLoads, { "x" : xs1, "force" : R1, "label" : "R1" });
        }
        else
        {
            distLoads = append(distLoads, { "center" : xs1, "force" : R1,
                "shape" : definition.support1LoadShape, "width" : definition.support1Width, "sign" : 1, "label" : "R1" });
        }

        // Support 2
        if (definition.support2LoadShape == LoadType.POINT)
        {
            pointLoads = append(pointLoads, { "x" : xs2, "force" : R2, "label" : "R2" });
        }
        else
        {
            distLoads = append(distLoads, { "center" : xs2, "force" : R2,
                "shape" : definition.support2LoadShape, "width" : definition.support2Width, "sign" : 1, "label" : "R2" });
        }

        // Support 3 (optional)
        if (definition.addThirdSupport)
        {
            if (definition.support3LoadShape == LoadType.POINT)
            {
                pointLoads = append(pointLoads, { "x" : xs3, "force" : R3, "label" : "R3" });
            }
            else
            {
                distLoads = append(distLoads, { "center" : xs3, "force" : R3,
                    "shape" : definition.support3LoadShape, "width" : definition.support3Width, "sign" : 1, "label" : "R3" });
            }
        }

        // Applied load 1
        if (definition.applied1LoadShape == LoadType.POINT)
        {
            pointLoads = append(pointLoads, { "x" : x1, "force" : -F1, "label" : "F1" });
        }
        else
        {
            distLoads = append(distLoads, { "center" : x1, "force" : F1,
                "shape" : definition.applied1LoadShape, "width" : definition.applied1Width, "sign" : -1, "label" : "F1" });
        }

        // Applied load 2 (optional)
        if (definition.secondApplied)
        {
            if (definition.applied2LoadShape == LoadType.POINT)
            {
                pointLoads = append(pointLoads, { "x" : x2, "force" : -F2, "label" : "F2" });
            }
            else
            {
                distLoads = append(distLoads, { "center" : x2, "force" : F2,
                    "shape" : definition.applied2LoadShape, "width" : definition.applied2Width, "sign" : -1, "label" : "F2" });
            }
        }

        // Compute the span covered by all loads (used to mask M/V beyond load extents)
        var xAffectedMin = xMax;
        var xAffectedMax = xMin;
        for (var dl in distLoads)
        {
            var hw = dl.width / 2;
            if (dl.center - hw < xAffectedMin) { xAffectedMin = dl.center - hw; }
            if (dl.center + hw > xAffectedMax) { xAffectedMax = dl.center + hw; }
        }
        for (var pl in pointLoads)
        {
            if (pl.x < xAffectedMin) { xAffectedMin = pl.x; }
            if (pl.x > xAffectedMax) { xAffectedMax = pl.x; }
        }

        // --- 7b. Print load summary (debug) ---
        if (definition.printLoadSummary)
        {
            if (definition.addThirdSupport)
            {
            }
            if (definition.addThirdSupport)
            {
            }
            if (definition.secondApplied)
            {
            }
            var netForce = R1 + R2 + R3 - F1 - F2;
            for (var dl in distLoads)
            {
            }
            for (var pl in pointLoads)
            {
            }
        }

        // --- 8. Build distributed net load q_net at each eval point ---
        var q_net = [];
        for (var i = 0; i < N; i += 1)
        {
            var q = 0 * newton / meter;
            for (var dl in distLoads)
            {
                q = q + dl.sign * loadIntensityAt(x_eval[i], dl.center, dl.force, dl.shape, dl.width);
            }
            q_net = append(q_net, q);
        }

        // --- 9. Build shear V(x) by trapezoidal integration, then add point load jumps ---
        var V_arr = [];
        V_arr = append(V_arr, 0 * newton);
        for (var i = 1; i < N; i += 1)
        {
            var v_new = V_arr[i - 1] + (q_net[i - 1] + q_net[i]) / 2 * dx;
            V_arr = append(V_arr, v_new);
        }
        // Point load jumps: upward force (+) raises V for all x >= load location
        for (var pl in pointLoads)
        {
            for (var i = 0; i < N; i += 1)
            {
                if (x_eval[i] >= pl.x)
                {
                    V_arr[i] = V_arr[i] + pl.force;
                }
            }
        }

        // --- 10. Build moment M(x) by trapezoidal integration of V ---
        var M_arr = [];
        M_arr = append(M_arr, 0 * newton * meter);
        for (var i = 1; i < N; i += 1)
        {
            var m_new = M_arr[i - 1] + (V_arr[i - 1] + V_arr[i]) / 2 * dx;
            M_arr = append(M_arr, m_new);
        }

        // --- 10a. Inject concentrated reaction moment (if enabled) ---
        if (definition.addPlateConditions && definition.reactionMoment != 0)
        {
            var iM = findNearestIndex(x_eval, definition.reactionMomentX);
            for (var i = iM; i < N; i += 1)
            {
                M_arr[i] = M_arr[i] + definition.reactionMoment * newton * meter;
            }
        }

        // --- 10b. Enforce M = 0 at outer support positions (simply-supported BCs) ---
        // Trapezoidal integration starts M[0] = 0 at xEvalMin, not at xs1.
        // Any non-zero q_net between xEvalMin and xs1 (e.g. distributed support straddle)
        // leaves M(xs1) != 0.  Subtract the line through M(xs1) and M(xs2) to zero both.
        var i1 = findNearestIndex(x_eval, xs1);
        var i2 = findNearestIndex(x_eval, xs2);
        var xSpan12 = x_eval[i2] - x_eval[i1];
        if (abs(xSpan12) < 1e-9 * meter)
        {
            throw regenError("Support positions map to the same eval grid node. Increase numEvalPoints or move supports.");
        }
        var M_raw1  = M_arr[i1];
        var M_raw2  = M_arr[i2];
        var M_slope = (M_arr[i2] - M_arr[i1]) / xSpan12;
        for (var i = 0; i < N; i += 1)
        {
            M_arr[i] = M_arr[i] - (M_raw1 + M_slope * (x_eval[i] - x_eval[i1]));
        }
        if (definition.printLoadSummary)
        {
        }

        // Zero M and V outside the load-affected span (physically correct: V=0, M=0 beyond all loads)
        for (var i = 0; i < N; i += 1)
        {
            if (x_eval[i] < xAffectedMin || x_eval[i] > xAffectedMax)
            {
                V_arr[i] = 0 * newton;
                M_arr[i] = 0 * newton * meter;
            }
        }

        // --- 11. Integrate curvature twice to get raw deflection + apply BCs ---
        // kappa = M / EI  [1/m];  theta = integral(kappa dx) [rad];  delta = integral(theta dx) [m]
        var EI_min = 1e-3 * newton * meter * meter;  // 0.001 N*m^2 — physical floor for any ski section
        var kappa_arr = [];
        for (var i = 0; i < N; i += 1)
        {
            var EI_i = interpEI(eiData, x_eval[i]);

            // Add plate stiffness contribution if within plate x-range
            if (definition.addPlateConditions && size(plateData) > 0)
            {
                var xpMin = plateData[0].x;
                var xpMax = plateData[size(plateData) - 1].x;
                if (x_eval[i] >= xpMin && x_eval[i] <= xpMax)
                {
                    EI_i = EI_i + interpEI(plateData, x_eval[i]);
                }
            }

            if (EI_i < EI_min)
            {
                EI_i = EI_min;
            }
            kappa_arr = append(kappa_arr, M_arr[i] / EI_i);
        }

        // Integrate kappa [1/m] * dx [m] -> dimensionless slope theta
        var theta_arr = [];
        theta_arr = append(theta_arr, kappa_arr[0] * (0 * meter)); // 0 with correct dimensionless type
        for (var i = 1; i < N; i += 1)
        {
            var th_new = theta_arr[i - 1] + (kappa_arr[i - 1] + kappa_arr[i]) / 2 * dx;
            theta_arr = append(theta_arr, th_new);
        }

        // Integrate theta * dx [m] -> deflection [m]
        var delta_arr = [];
        delta_arr = append(delta_arr, 0 * meter);
        for (var i = 1; i < N; i += 1)
        {
            var d_new = delta_arr[i - 1] + (theta_arr[i - 1] + theta_arr[i]) / 2 * dx;
            delta_arr = append(delta_arr, d_new);
        }

        // Apply zero-deflection BCs at outer supports (part of step 11)
        // delta_corrected(x) = delta_raw(x) + C1*(x - x_eval[0]) + C2
        // Enforces delta_corrected = 0 at xs1 and xs2.
        // i1, i2, xSpan12 computed and validated in step 10b above.
        var C1 = -(delta_arr[i2] - delta_arr[i1]) / xSpan12;
        var C2 = -(delta_arr[i1] + C1 * (x_eval[i1] - x_eval[0]));

        var deflection = [];
        for (var i = 0; i < N; i += 1)
        {
            deflection = append(deflection, delta_arr[i] + C1 * (x_eval[i] - x_eval[0]) + C2);
        }

        // Compute scaleK for kappa visualization (used by debugMode CYAN line and backOutEI manipulators)
        var maxAbsKappa = 1e-9 / meter;
        for (var i = 0; i < N; i += 1)
        {
            if (abs(kappa_arr[i]) > maxAbsKappa) { maxAbsKappa = abs(kappa_arr[i]); }
        }
        var scaleK = 0.001 * meter * meter;
        while (maxAbsKappa * scaleK < 0.1 * meter && scaleK < 1e7 * meter * meter)
        {
            scaleK = scaleK * 10;
        }

        // --- 11b. Debug visualization (shear/moment diagrams + load arrows) ---
        if (definition.debugMode)
        {
            var vizSpan = xEvalMax - xEvalMin;

            // Find max |V|, |M| for scaling
            var maxAbsV     = 1e-9 * newton;
            var maxAbsM     = 1e-9 * newton * meter;
            for (var i = 0; i < N; i += 1)
            {
                if (abs(V_arr[i])     > maxAbsV)     { maxAbsV     = abs(V_arr[i]);     }
                if (abs(M_arr[i])     > maxAbsM)     { maxAbsM     = abs(M_arr[i]);     }
            }
            var scaleV = vizSpan * 0.2 / maxAbsV;   // [m / N]
            var scaleM = vizSpan * 0.2 / maxAbsM;   // [m / (N·m)]
            var scaleF = vizSpan * 0.2 / F_total;   // [m / N]
            // scaleK computed above (shared with backOutEI)

            // Zero reference line (BLACK)
            addDebugLine(context,
                vector(xEvalMin, 0 * meter, 0 * meter),
                vector(xEvalMax, 0 * meter, 0 * meter),
                DebugColor.BLACK);

            // Shear diagram polyline (BLUE)
            for (var i = 0; i < N - 1; i += 1)
            {
                addDebugLine(context,
                    vector(x_eval[i],     0 * meter, V_arr[i]     * scaleV),
                    vector(x_eval[i + 1], 0 * meter, V_arr[i + 1] * scaleV),
                    DebugColor.BLUE);
            }

            // Moment diagram polyline (MAGENTA)
            for (var i = 0; i < N - 1; i += 1)
            {
                addDebugLine(context,
                    vector(x_eval[i],     0 * meter, M_arr[i]     * scaleM),
                    vector(x_eval[i + 1], 0 * meter, M_arr[i + 1] * scaleM),
                    DebugColor.MAGENTA);
            }

            // Curvature diagram polyline (CYAN)
            for (var i = 0; i < N - 1; i += 1)
            {
                addDebugLine(context,
                    vector(x_eval[i],     0 * meter, kappa_arr[i]     * scaleK),
                    vector(x_eval[i + 1], 0 * meter, kappa_arr[i + 1] * scaleK),
                    DebugColor.CYAN);
            }

            // Support reaction arrows: GREEN (all supports)
            addDebugLine(context,
                vector(xs1, 0 * meter, 0 * meter),
                vector(xs1, 0 * meter, R1 * scaleF),
                DebugColor.GREEN);

            addDebugLine(context,
                vector(xs2, 0 * meter, 0 * meter),
                vector(xs2, 0 * meter, R2 * scaleF),
                DebugColor.GREEN);

            if (definition.addThirdSupport)
            {
                addDebugLine(context,
                    vector(xs3, 0 * meter, 0 * meter),
                    vector(xs3, 0 * meter, R3 * scaleF),
                    DebugColor.GREEN);
            }

            // Applied load arrows: RED (all applied loads)
            addDebugLine(context,
                vector(x1, 0 * meter, 0 * meter),
                vector(x1, 0 * meter, -F1 * scaleF),
                DebugColor.RED);

            if (definition.secondApplied)
            {
                addDebugLine(context,
                    vector(x2, 0 * meter, 0 * meter),
                    vector(x2, 0 * meter, -F2 * scaleF),
                    DebugColor.RED);
            }
        }

        // --- 11c. backOutEI: fit kappa regions, add manipulators, output EI edge ---
        // DISABLED — re-enable by removing the /* */ wrapping and making backOutEI visible
        /* if (definition.backOutEI && size(definition.trimBoundaries) >= 2)
        {
            // Step A: Resolve & clamp trim boundaries
            var xBounds = [];
            for (var boundary in definition.trimBoundaries)
            {
                var bx = boundary.boundaryX;
                if (bx < xEvalMin) { bx = xEvalMin; }
                if (bx > xEvalMax) { bx = xEvalMax; }
                xBounds = append(xBounds, bx);
            }

            // Step B: Per-region spline fitting or recovery from stored CPs
            var nReg = size(definition.regionParams);
            var newAllCpX = [];
            var newAllCpZ = [];
            var newCpSizes = [];
            var newCpInit = [];

            for (var ri = 0; ri < nReg; ri += 1)
            {
                var region = definition.regionParams[ri];
                var xLo = xBounds[ri];
                var xHi = xBounds[ri + 1];

                var isInit = (ri < size(definition.cpIsInitialized) &&
                              definition.cpIsInitialized[ri].v == true);

                var regionCpX = [];
                var regionCpZ = [];

                if (isInit)
                {
                    // Recover stored CPs from flat arrays
                    var flatOff = 0;
                    for (var k = 0; k < ri; k += 1)
                    {
                        flatOff = flatOff + definition.cpRegionSizes[k].v;
                    }
                    var sz = definition.cpRegionSizes[ri].v;
                    for (var k = 0; k < sz; k += 1)
                    {
                        regionCpX = append(regionCpX, definition.allCpX[flatOff + k].v);
                        regionCpZ = append(regionCpZ, definition.allCpZ[flatOff + k].v);
                    }
                }
                else
                {
                    // Collect kappa sample points in this X range
                    var regionPts = [];
                    for (var j = 0; j < N; j += 1)
                    {
                        if (x_eval[j] >= xLo && x_eval[j] <= xHi)
                        {
                            regionPts = append(regionPts, vector(x_eval[j], 0 * meter, kappa_arr[j] * scaleK));
                        }
                    }

                    if (size(regionPts) >= 2 && region.regionType != RegionType.BRIDGING)
                    {
                        var fitTol = (region.regionType == RegionType.FREE_DRAG)
                                     ? 1e-7 * meter
                                     : region.regionTolerance;
                        var fitResult = approximateSpline(context, {
                            "degree"           : region.approxDegree,
                            "tolerance"        : fitTol,
                            "isPeriodic"       : false,
                            "targets"          : [{ "positions" : regionPts }],
                            "maxControlPoints" : region.maxCP
                        });
                        var spline = fitResult[0];
                        for (var cp in spline.controlPoints)
                        {
                            regionCpX = append(regionCpX, cp[0] / meter);
                            // cpZ = kappa * meter (dimensionless); kappa = cp[2] / scaleK [1/m]
                            regionCpZ = append(regionCpZ, (cp[2] / scaleK) * meter);
                        }
                    }
                }

                // Accumulate into flat arrays
                for (var k = 0; k < size(regionCpX); k += 1)
                {
                    newAllCpX = append(newAllCpX, { "v" : regionCpX[k] });
                    newAllCpZ = append(newAllCpZ, { "v" : regionCpZ[k] });
                }
                newCpSizes = append(newCpSizes, { "v" : size(regionCpX) });
                newCpInit  = append(newCpInit,  { "v" : size(regionCpX) > 0 });
            }

            // Persist fitted CPs to hidden definition parameters
            definition.allCpX         = newAllCpX;
            definition.allCpZ         = newAllCpZ;
            definition.cpRegionSizes  = newCpSizes;
            definition.cpIsInitialized = newCpInit;
            definition.storedScaleK   = scaleK / (meter * meter);

            // Step D: Add manipulators (one per CP, Z-direction drag)
            var allManipulators = {};
            var cpFlatOff = 0;
            for (var ri = 0; ri < nReg; ri += 1)
            {
                var sz = newCpSizes[ri].v;
                for (var j = 0; j < sz; j += 1)
                {
                    var key = "r" ~ toString(ri) ~ "c" ~ toString(j);
                    allManipulators[key] = linearManipulator({
                        "base"      : vector(newAllCpX[cpFlatOff + j].v * meter, 0 * meter, 0 * meter),
                        "direction" : vector(0, 0, 1),
                        "offset"    : newAllCpZ[cpFlatOff + j].v * definition.storedScaleK * meter
                    });
                }
                cpFlatOff = cpFlatOff + sz;
            }
            addManipulators(context, id, allManipulators);

            // Step E: Evaluate kappa_cleaned by linear interpolation over CP polyline per region
            var kappa_cleaned = [];
            for (var j = 0; j < N; j += 1)
            {
                var xi = x_eval[j];
                var kappa_i = kappa_arr[j];   // default: raw kappa
                var foundRegion = false;
                var flatOff2 = 0;
                for (var ri = 0; ri < nReg; ri += 1)
                {
                    var sz2 = newCpSizes[ri].v;
                    if (!foundRegion && xi >= xBounds[ri] && xi <= xBounds[ri + 1] && sz2 >= 2)
                    {
                        var foundSpan = false;
                        for (var k = 0; k < sz2 - 1; k += 1)
                        {
                            if (!foundSpan)
                            {
                                var x0 = newAllCpX[flatOff2 + k].v     * meter;
                                var x1 = newAllCpX[flatOff2 + k + 1].v * meter;
                                if (xi >= x0 && xi <= x1)
                                {
                                    var span = x1 - x0;
                                    if (abs(span) > 1e-12 * meter)
                                    {
                                        var t = (xi - x0) / span;
                                        var z0 = newAllCpZ[flatOff2 + k].v     / meter;  // kappa [1/m]
                                        var z1 = newAllCpZ[flatOff2 + k + 1].v / meter;
                                        kappa_i = z0 + t * (z1 - z0);
                                    }
                                    foundSpan = true;
                                }
                            }
                        }
                        foundRegion = true;
                    }
                    flatOff2 = flatOff2 + sz2;
                }
                kappa_cleaned = append(kappa_cleaned, kappa_i);
            }

            // Step F: EI back-out  EI = M / kappa_cleaned
            var EI_backout = [];
            for (var j = 0; j < N; j += 1)
            {
                var ki = kappa_cleaned[j];
                var EI_j = (abs(ki) > 1e-6 / meter)
                           ? M_arr[j] / ki
                           : 0 * newton * meter * meter;
                EI_backout = append(EI_backout, EI_j);
            }

            // Step G: Output edge — Z = EI [N·m²] encoded as Z [mm], matching selEI format
            var eiPts = [];
            for (var j = 0; j < N; j += 1)
            {
                eiPts = append(eiPts, vector(
                    x_eval[j],
                    0 * meter,
                    (EI_backout[j] / (newton * meter * meter)) * millimeter
                ));
            }
            opFitSpline(context, id + "eiBackout", { "points" : eiPts });
        } */ // END DISABLED backOutEI block

        // --- 12. Create deflection spline curve (XZ plane, Z = deflection) ---
        var pts = [];
        for (var i = 0; i < N; i += 1)
        {
            pts = append(pts, vector(x_eval[i], 0 * meter, deflection[i]));
        }

        if (definition.curveOutput == CurveOutput.APPROX)
        {
            var approxResult = approximateSpline(context, {
                "degree"           : definition.curveDegree,
                "tolerance"        : definition.approxTolerance,
                "isPeriodic"       : false,
                "targets"          : [{ "positions" : pts }],
                "maxControlPoints" : definition.maxControlPoints
            });
            opCreateBSplineCurve(context, id + "deflCurve", { "bSplineCurve" : approxResult[0] });
        }
        else
        {
            opFitSpline(context, id + "deflCurve", { "points" : pts });
        }

        setProperty(context, {
            "entities"     : qCreatedBy(id + "deflCurve", EntityType.BODY),
            "propertyType" : PropertyType.NAME,
            "value"        : "deflection_curve"
        });
     });
 

