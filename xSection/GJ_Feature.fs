FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

//import xSect_GJ
import(path : "9df6ba3db06d479fabe63c1d", version : "01ff18cd88c62b73db496011");
// import gjAnalysis
import(path : "d30d288c7bf272efb0957cff", version : "82afb1c5966d181fa0227c45");
//import gjDataAccess
import(path : "12c9e75dc2139eb927245033", version : "009f2fb3b7652346f43dcb9c");
//import gjPredicates

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


annotation { "Feature Type Name" : "Solve GJ", "Editing Logic Function" : "gjAnalysisEditLogic", "Feature Type Description" : "Takes a cross section/ei feature as input and calculates the torsional stiffness profile of the cross sections. Adds GJ data to the appropriate map on the origin to add the GJ data into the existing EI data" }
export const solveGJ = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Cross section feature", "MaxNumberOfPicks" : 1 }
        definition.xSectFeature is FeatureList;

    }
    {
        var oldID = keys(definition.xSectFeature)[0][0];

        var allEIData = getAttribute(context, {
                "entity" : qOrigin(EntityType.BODY),
                "name" : "CrossSectionAnalysis"
        });

        var crossSectionData = allEIData[(oldID)];
        var crossSectionDetails = crossSectionData.details;
        var crossSectionTableData = crossSectionData.tableData;

        var bodies = crossSectionDetails.bodies;
        var crossSections = crossSectionDetails.crossSections;
        var numSections = size(crossSections);

        var successCount = 0;
        var failCount = 0;
        var skipCount = 0;

        // Extract table rows once for mutation
        var tableRows = crossSectionTableData.crossSections;

        for (var i = 0; i < numSections; i += 1)
        {
            var section = crossSections[i];
            var stationNum = section.stationNumber;

            if (!validateSectionData(section))
            {
                println("WARNING: Section " ~ i ~ " (station " ~ stationNum ~ ") missing mesh data - skipped");
                skipCount += 1;
                continue;
            }

            try
            {
                var GJ_eff = computeTorsionalStiffness(section, bodies);
                var GJ_val = GJ_eff / (newton * meter * meter);

                // Update section GJ_eff
                crossSections[i].GJ_eff = GJ_eff;

                // Update table: row i+1 (skip header), column 3 (GJ)
                var tableRow = i + 1;
                if (tableRow < size(tableRows))
                {
                    tableRows[tableRow][3] = round(GJ_val * 10.0) / 10.0;
                }

                successCount += 1;
            }
            catch (e)
            {
                println("WARNING: GJ computation failed for section " ~ i ~
                        " (station " ~ stationNum ~ ") - " ~ e);
                failCount += 1;
            }
        }

        // Explicit reassignment chain for FeatureScript value-copy semantics
        crossSectionTableData["crossSections"] = tableRows;
        crossSectionDetails["crossSections"] = crossSections;
        crossSectionData["details"] = crossSectionDetails;
        crossSectionData["tableData"] = crossSectionTableData;
        allEIData[oldID] = crossSectionData;

        setAttribute(context, {
            "entities" : qOrigin(EntityType.BODY),
            "name" : "CrossSectionAnalysis",
            "attribute" : allEIData
        });

    });
