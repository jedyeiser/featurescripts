FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/table.fs", version : "2878.0");

/**
 * Cross-Section Analysis Table
 *
 * Displays beam analysis results stored by xSection feature.
 * Reads CrossSectionAnalysis attribute from origin point.
 *
 * Shows two tables:
 * 1. Summary - Overall beam stiffness metrics
 * 2. Cross-Section Details - Per-section geometric and mechanical properties
 */

annotation { "Table Type Name" : "Cross-Section Analysis" }
export const xSectionAnalysisTable = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
        annotation {
            "Name" : "Select Analysis",
            "Description" : "Choose which xSection analysis to display",
            "UIHint" : UIHint.STRING
        }
        isAnything(definition.featureId);
    }
    {
        // Find origin with CrossSectionAnalysis attribute
        var bodiesWithData = evaluateQuery(context, qHasAttribute("CrossSectionAnalysis"));

        if (size(bodiesWithData) == 0)
        {
            var emptyTable = table("Cross-Section Analysis (No Data)", [], []);
            return tableArray([emptyTable]);
        }

        // Read attribute
        var allData = getAttribute(context, {
            "entity" : bodiesWithData[0],
            "name" : "CrossSectionAnalysis"
        });

        if (allData == undefined || size(keys(allData)) == 0)
        {
            var emptyTable = table("Cross-Section Analysis (No Data)", [], []);
            return tableArray([emptyTable]);
        }

        // Get feature keys (all available analyses)
        var featureKeys = keys(allData);

        // Use first available analysis if no specific selection
        var selectedKey = featureKeys[0];
        if (definition.featureId != undefined && definition.featureId != "")
        {
            var requestedKey = toString(definition.featureId);
            if (allData[requestedKey] != undefined)
            {
                selectedKey = requestedKey;
            }
        }

        var analysisData = allData[selectedKey];
        var tableData = analysisData.tableData;

        // Build summary table
        var summaryTable = buildSummaryTable(tableData.summary);

        // Build cross-section details table
        var detailsTable = buildCrossSectionTable(tableData.crossSections);

        // Return both tables
        return tableArray([summaryTable, detailsTable]);
    });

/**
 * Build summary table with overall beam stiffness metrics.
 */
function buildSummaryTable(summaryData is array) returns Table
{
    // Define columns
    var columns = [
        tableColumnDefinition("metric", "Metric"),
        tableColumnDefinition("value", "Value", TableTextAlignment.RIGHT)
    ];

    // Build rows from summary data
    var rows = [];
    for (var rowData in summaryData)
    {
        var cellData = {
            "metric" : rowData[0],  // Label
            "value" : toString(rowData[1])  // Value
        };
        rows = append(rows, tableRow(cellData));
    }

    return table("Beam Analysis Summary", columns, rows);
}

/**
 * Build cross-section details table with per-section properties.
 */
function buildCrossSectionTable(csData is array) returns Table
{
    // First row is header
    var header = csData[0];

    // Define columns from header
    var columns = [
        tableColumnDefinition("section", header[0]),
        tableColumnDefinition("xCoord", header[1], TableTextAlignment.RIGHT),
        tableColumnDefinition("EI", header[2], TableTextAlignment.RIGHT),
        tableColumnDefinition("naHeight", header[3], TableTextAlignment.RIGHT),
        tableColumnDefinition("beamHeight", header[4], TableTextAlignment.RIGHT),
        tableColumnDefinition("beamWidth", header[5], TableTextAlignment.RIGHT),
        tableColumnDefinition("linealDensity", header[6], TableTextAlignment.RIGHT)
    ];

    // Build rows from data (skip header row)
    var rows = [];
    for (var i = 1; i < size(csData); i += 1)
    {
        var rowData = csData[i];
        var cellData = {
            "section" : toString(rowData[0]),
            "xCoord" : toString(rowData[1]),
            "EI" : toString(rowData[2]),
            "naHeight" : toString(rowData[3]),
            "beamHeight" : toString(rowData[4]),
            "beamWidth" : toString(rowData[5]),
            "linealDensity" : toString(rowData[6])
        };
        rows = append(rows, tableRow(cellData));
    }

    return table("Cross-Section Details (" ~ (size(csData) - 1) ~ " sections)", columns, rows);
}
