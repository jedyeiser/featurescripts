FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import xSect_GJ
import(path : "9df6ba3db06d479fabe63c1d", version : "85b2b777fcad63294d446295");
// import gjAnalysis
import(path : "d30d288c7bf272efb0957cff", version : "66d41e7cc5fa381e0beff893");
//import gjDataAccess
import(path : "12c9e75dc2139eb927245033", version : "245034f4d110f01b5a55c774");
//import gjPredicates

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
 * Creates no geometry. EI and Cross Section now computes GJ itself with the same solver, so this
 * feature only re-runs that step on the stored sections.
 */
annotation { "Feature Type Name" : "Solve GJ", "Feature Type Description" : "Takes a cross section/ei feature as input and calculates the torsional stiffness profile of the cross sections. Adds GJ data to the appropriate map on the origin to add the GJ data into the existing EI data" }
export const solveGJ = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Cross section feature", "MaxNumberOfPicks" : 1 }
        definition.xSectFeature is FeatureList;

    }
    {
        if (size(definition.xSectFeature) == 0)
        {
            throw regenError("Select the cross section feature.", ["xSectFeature"]);
        }
        var featureKey = toAttributeId(keys(definition.xSectFeature)[0]);
        computeAndStoreGJByFeatureKey(context, id, featureKey, false, "");
    });
