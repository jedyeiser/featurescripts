FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import xSect_GJ
import(path : "9df6ba3db06d479fabe63c1d", version : "b840272d29f360c74ebcaadc");
// import gjAnalysis
import(path : "d30d288c7bf272efb0957cff", version : "b51de0d6ab7f63c70de6314a");
//import gjDataAccess
import(path : "12c9e75dc2139eb927245033", version : "9ac6c3b7c431a4120610201f");
//import gjPredicates

/**
 * Editing logic for the Solve GJ feature.
 *
 * Hides the `curvePrefix` name field when `createGJCurve` is false (no curve to name).
 *
 * @param context {Context}
 * @param id {Id}
 * @param oldDefinition {map} : Previous definition snapshot
 * @param definition {map} : Current definition. Relevant fields:
 *   - `definition.createGJCurve` {boolean} : Whether to create a GJ visualization curve
 *   - `definition.curvePrefix` {string} : Name prefix for the curve (cleared when curve disabled)
 * @param isCreating {boolean}
 * @param specifiedParameters {map}
 * @returns {map} : Updated definition
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


/**
 * Solve GJ feature.
 *
 * Computes torsional stiffness GJ for each cross-section stored by a selected xSection feature,
 * then writes the results back to the `CrossSectionAnalysis` attribute.
 *
 * Inputs (definition fields):
 *   - `xSectFeature` {FeatureList} : Single xSection feature whose cross-sections to process.
 *     The first key from `keys(definition.xSectFeature)` is used as the attribute lookup key.
 *
 * Preconditions:
 *   - A `CrossSectionAnalysis` attribute must exist on the document origin (written by xSection).
 *   - The referenced feature key must exist in that attribute.
 *
 * Side effects:
 *   - Mutates `CrossSectionAnalysis[featureKey].details.crossSections[i].GJ_eff` for each section.
 *   - Mutates `CrossSectionAnalysis[featureKey].tableData.crossSections[row][3]` (GJ column).
 *   - Re-writes the full attribute to the origin body.
 *
 * Does not create geometry unless `createGJCurve` is true (handled by gjAnalysisEditLogic).
 */
annotation { "Feature Type Name" : "Solve GJ", "Editing Logic Function" : "gjAnalysisEditLogic", "Feature Type Description" : "Takes a cross section/ei feature as input and calculates the torsional stiffness profile of the cross sections. Adds GJ data to the appropriate map on the origin to add the GJ data into the existing EI data" }
export const solveGJ = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Cross section feature", "MaxNumberOfPicks" : 1 }
        definition.xSectFeature is FeatureList;

    }
    {
        var featureKey = keys(definition.xSectFeature)[0][0];
        computeAndStoreGJByFeatureKey(context, id, featureKey, false, "");
    });
