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

        // Build tables for ALL analyses in the document
        var allTables = [];
        var featureKeys = keys(allData);

        for (var key in featureKeys)
        {
            var analysisData = allData[key];
            var tableData = analysisData.tableData;
            var analysisName = analysisData.analysisName;

            // Use analysis name in title if provided
            var titlePrefix = "";
            if (analysisName != undefined && analysisName != "")
            {
                titlePrefix = analysisName ~ " - ";
            }

            // Build summary table
            var summaryTable = buildSummaryTable(tableData.summary, titlePrefix);

            // Build cross-section details table
            var detailsTable = buildCrossSectionTable(tableData.crossSections, titlePrefix);

            allTables = append(allTables, summaryTable);
            allTables = append(allTables, detailsTable);
        }

        // Return all tables
        return tableArray(allTables);
    });

/**
 * Build summary table with overall beam stiffness metrics.
 */
function buildSummaryTable(summaryData is array, titlePrefix is string) returns Table
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

    return table(titlePrefix ~ "Beam Analysis Summary", columns, rows);
}

/**
 * Build cross-section details table with per-section properties.
 */
function buildCrossSectionTable(csData is array, titlePrefix is string) returns Table
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

    return table(titlePrefix ~ "Cross-Section Details (" ~ (size(csData) - 1) ~ " sections)", columns, rows);
}
