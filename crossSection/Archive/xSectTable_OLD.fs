FeatureScript 2878;

import(path : "onshape/std/table.fs", version : "2878.0");
import(path : "onshape/std/attributes.fs", version : "2878.0");


/**
 * EI CROSS-SECTION TABLE
 * ======================
 *
 * Reads the "EIData" attribute on Origin (set by EI Cross Section features).
 * Produces a summary + section table pair for every analysis found.
 * No parameters needed -- it shows everything automatically.
 *
 * All data is pre-formatted by the feature. This table just reads and displays.
 */


annotation { "Table Type Name" : "EI Cross-Section Analysis" }
export const eiXSectTable = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
    }
    {
        var eiData = loadEIData(context);

        if (eiData == undefined)
        {
            // Return a single empty table so the table type still registers
            return tableArray([emptyTable()]);
        }

        var keys = sg(eiData, "_keys");
        if (keys == undefined)
        {
            return tableArray([emptyTable()]);
        }

        // Build a summary + section table for each analysis
        var tables = [];
        for (var key in keys)
        {
            var data = sg(eiData, key);
            if (data != undefined)
            {
                tables = append(tables, summaryTable(data));
                tables = append(tables, sectionTable(data));
            }
        }

        if (size(tables) == 0)
        {
            return tableArray([emptyTable()]);
        }

        return tableArray(tables);
    });


// =============================================================================
// DATA LOADING
// =============================================================================

function loadEIData(context is Context)
{
    var originEntity = { "queryType" : QueryType.TRANSIENT, "transientId" : "IB" } as Query;

    try
    {
        return getAttribute(context, { "entity" : originEntity, "name" : "EIData" });
    }
    catch
    {
        return undefined;
    }
}


// =============================================================================
// TABLES
// =============================================================================

function emptyTable() returns Table
{
    return table("No EI Data",
        [tableColumnDefinition("info", "Info")],
        [tableRow({ "info" : "Run an EI Cross Section feature first" })]);
}

function summaryTable(data is map) returns Table
{
    var cols = [
        tableColumnDefinition("metric", "Metric"),
        tableColumnDefinition("value", "Value")
    ];

    var rows = [];
    var summary = sg(data, "summary");
    if (summary != undefined)
    {
        for (var row in summary)
        {
            rows = append(rows, tableRow({
                "metric" : sg(row, "metric"),
                "value" : sg(row, "value")
            }));
        }
    }

    var name = sg(data, "analysisName");
    if (name == undefined)
    {
        name = "Analysis";
    }

    return table(name ~ " - Stiffness", cols, rows);
}

function sectionTable(data is map) returns Table
{
    var cols = [
        tableColumnDefinition("x", "X (mm)"),
        tableColumnDefinition("origin", "Plane Origin (mm)"),
        tableColumnDefinition("normal", "Plane Normal"),
        tableColumnDefinition("ei", "EI (N*m2)"),
        tableColumnDefinition("na", "NA (mm)"),
        tableColumnDefinition("ld", "Lineal Density (kg/m)"),
        tableColumnDefinition("width", "Width (mm)"),
        tableColumnDefinition("height", "Height (mm)")
    ];

    var rows = [];
    var sections = sg(data, "sections");
    if (sections != undefined)
    {
        for (var s in sections)
        {
            rows = append(rows, tableRow({
                "x" : sg(s, "x"),
                "origin" : sg(s, "origin"),
                "normal" : sg(s, "normal"),
                "ei" : sg(s, "EI"),
                "na" : sg(s, "na"),
                "ld" : sg(s, "ld"),
                "width" : sg(s, "width"),
                "height" : sg(s, "height")
            }));
        }
    }

    var name = sg(data, "analysisName");
    if (name == undefined)
    {
        name = "Analysis";
    }

    return table(name ~ " - Sections", cols, rows);
}


// =============================================================================
// SAFE KEY ACCESS
// =============================================================================

function sg(m, key is string)
{
    try { return m[key]; }
    return undefined;
}
