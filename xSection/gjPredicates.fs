FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// IMPORT: xSection/gjDataAccess.fs
// IMPORT: xSection/xSect_GJ.fs
// IMPORT: xSection/gjAnalysis.fs

/**
 * GJ ANALYSIS FEATURE DEFINITION
 * ================================
 *
 * Standalone feature for computing torsional stiffness (GJ) from stored
 * cross-section data. Reads data from xSect feature attributes, computes GJ
 * using FEM solver, and updates the attribute with results.
 *
 * Usage:
 * 1. Select any entity (body, face, edge) created by the xSect feature
 * 2. Feature auto-detects which xSect feature created it
 * 3. GJ is computed for all cross-sections and attribute is updated
 *
 * Benefits:
 * - xSect runs faster (no expensive GJ computation)
 * - Users compute GJ only when needed
 * - Clean separation of concerns
 * - Optional visualization curve
 */

annotation {
    "Feature Type Name" : "GJ Analysis",
    "Feature Type Description" : "Compute torsional stiffness (GJ) for cross-sections from xSect feature data",
    "Filter Selector" : "allparts",
    "Editing Logic Function" : "gjAnalysisEditLogic"
}
export const gjAnalysis = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation {
            "Name" : "Entity from xSect feature",
            "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE,
            "MaxNumberOfPicks" : 1,
            "Description" : "Select any entity (composite body, curve, etc.) created by the xSect feature"
        }
        definition.xSectEntity is Query;

        annotation {
            "Name" : "Create GJ visualization curve",
            "Description" : "Create a 3D curve showing GJ variation along the beam",
            "Default" : false
        }
        definition.createGJCurve is boolean;

        annotation {
            "Name" : "Curve name prefix",
            "Description" : "Optional prefix for the GJ curve name (e.g., 'Beam1' → 'Beam1_GJ')",
            "Default" : ""
        }
        definition.curvePrefix is string;
    }
    {
        // Implementation in gjAnalysis.fs
        gjAnalysisMain(context, id, definition);
    });

/**
 * Editing logic for GJ Analysis feature.
 * Controls UI element visibility based on user selections.
 */
export function gjAnalysisEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
                                     isCreating is boolean, specifiedParameters is map) returns map
{
    // Show curve name field only if visualization is enabled
    if (definition.createGJCurve == false)
    {
        definition.curvePrefix = "";
    }

    return definition;
}
