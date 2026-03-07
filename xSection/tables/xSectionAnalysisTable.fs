FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/table.fs", version : "2892.0");

// xSectMaterials (for tryGetKey)
import(path : "f8e590162884d45f56e0a05f", version : "c6d4a2440a5dac22f41a1a21");

import(path : "17142132b20343b5f125e7e7", version : "0e6215d723bc0619b74f7dd4");

/**
 * CROSS-SECTION ANALYSIS TABLE MODULE
 * =====================================
 *
 * Renders beam analysis results stored by the xSection feature as Onshape tables.
 *
 * Data source: `CrossSectionAnalysis` attribute on the document origin body.
 * The attribute is a map keyed by feature ID string; each entry contains:
 *   - `tableData.summary`        : array of [label, value] summary rows
 *   - `tableData.crossSections`  : 2D array (header + data rows)
 *   - `tableData.materialTable`  : (optional) material Q-matrix rows
 *   - `tableData.bodyTable`      : (optional) per-body section breakdown
 *
 * Generates up to 4 tables per xSection feature instance:
 *   1. Beam Analysis Summary      — overall stiffness metrics (EI_bar, deflection, etc.)
 *   2. Cross-Section Details      — per-station EI, GJ, NA height, dimensions, density
 *   3. Materials                  — unique material Q-matrix data (if material CSV provided)
 *   4. Body Detail                — per-body geometric/stiffness breakdown (configurable type)
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

            var matTableData = tryGetKey(tableData, "materialTable");
            if (matTableData != undefined)
            {
                allTables = append(allTables, buildRenderedMaterialTable(matTableData, titlePrefix));
            }

            var bodyTableData = tryGetKey(tableData, "bodyTable");
            if (bodyTableData != undefined)
            {
                allTables = append(allTables, buildRenderedBodyTable(bodyTableData, titlePrefix));
            }
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
        tableColumnDefinition("GJ", header[3], TableTextAlignment.RIGHT),
        tableColumnDefinition("naHeight", header[4], TableTextAlignment.RIGHT),
        tableColumnDefinition("naPercentage", header[5], TableTextAlignment.RIGHT),
        tableColumnDefinition("beamWidth", header[6], TableTextAlignment.RIGHT),
        tableColumnDefinition("beamHeight", header[7], TableTextAlignment.RIGHT),
        tableColumnDefinition("linealDensity", header[8], TableTextAlignment.RIGHT)
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
            "GJ" : toString(rowData[3]),
            "naHeight" : toString(rowData[4]),
            "naPercentage" : toString(rowData[5]),
            "beamWidth" : toString(rowData[6]),
            "beamHeight" : toString(rowData[7]),
            "linealDensity" : toString(rowData[8])
        };
        rows = append(rows, tableRow(cellData));
    }

    return table(titlePrefix ~ "Cross-Section Details (" ~ (size(csData) - 1) ~ " sections)", columns, rows);
}

/**
 * Build material table — one row per unique material with full Q-matrix.
 *
 * Columns: ID | Material | Category | Density (kg/m³) | E (GPa) | ν |
 *          Q11 | Q22 | Q12 | Q66 | Q16 | Q26  (all Q in GPa)
 */
function buildRenderedMaterialTable(matTableData is map, titlePrefix is string) returns Table
{
    var columns = [
        tableColumnDefinition("mat_id",      "ID",              TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_name",    "Material"),
        tableColumnDefinition("mat_cat",     "Category"),
        tableColumnDefinition("mat_density", "Density (kg/m³)", TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_E",       "E (GPa)",         TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_nu",      "ν",               TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_Q11",     "Q11 (GPa)",       TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_Q22",     "Q22 (GPa)",       TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_Q12",     "Q12 (GPa)",       TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_Q66",     "Q66 (GPa)",       TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_Q16",     "Q16 (GPa)",       TableTextAlignment.RIGHT),
        tableColumnDefinition("mat_Q26",     "Q26 (GPa)",       TableTextAlignment.RIGHT)
    ];

    var rows = [];
    for (var rowData in matTableData.rows)
    {
        var cellData = {
            "mat_id"      : toString(rowData.id),
            "mat_name"    : toString(rowData.name),
            "mat_cat"     : toString(rowData.category),
            "mat_density" : toString(rowData.density),
            "mat_E"       : toString(rowData.E),
            "mat_nu"      : toString(rowData.nu),
            "mat_Q11"     : toString(rowData.Q11),
            "mat_Q22"     : toString(rowData.Q22),
            "mat_Q12"     : toString(rowData.Q12),
            "mat_Q66"     : toString(rowData.Q66),
            "mat_Q16"     : toString(rowData.Q16),
            "mat_Q26"     : toString(rowData.Q26)
        };
        rows = append(rows, tableRow(cellData));
    }

    return table(titlePrefix ~ "Materials (" ~ toString(size(matTableData.rows)) ~ " unique)", columns, rows);
}

/**
 * Build body detail table — one row per section, dynamic columns per body.
 *
 * Column layout varies by bodyTableType:
 *   Fixed prefix (always):  Station | X (mm) | EI_calc (N·m²) | NA Height (mm)
 *   FULL:     per body → Area | Centroid | I | EI | %   + EI_sum
 *   GEO_ONLY: per body → Area | Centroid | I            (no EI_sum)
 *   EI_ONLY:  per body → EI | %                         + EI_sum
 *   BASIC:    per body → Area | Centroid above NA | EI | % + EI_sum
 */
function buildRenderedBodyTable(bodyTableData is map, titlePrefix is string) returns Table
{
    var bodyNames = bodyTableData.bodyNames;
    var numBodies = size(bodyNames);

    // Read bodyTableType; fall back to FULL for backwards compatibility
    var bodyTableType = tryGetKey(bodyTableData, "bodyTableType");
    if (bodyTableType == undefined)
    {
        bodyTableType = BodyTableType.FULL;
    }

    // Fixed prefix columns (always present)
    var columns = [
        tableColumnDefinition("bdt_station", "Station",          TableTextAlignment.RIGHT),
        tableColumnDefinition("bdt_x",       "X (mm)",           TableTextAlignment.RIGHT),
        tableColumnDefinition("bdt_EI_calc", "EI_calc (N·m²)",   TableTextAlignment.RIGHT),
        tableColumnDefinition("bdt_NA",      "NA Height (mm)",    TableTextAlignment.RIGHT)
    ];

    // Per-body columns — vary by type
    for (var k = 0; k < numBodies; k += 1)
    {
        var bName = bodyNames[k];
        var kStr = toString(k);

        if (bodyTableType == BodyTableType.FULL)
        {
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_area", bName ~ " Area (mm²)",    TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_ctr",  bName ~ " Centroid (mm)", TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_I",    bName ~ " I (mm⁴)",       TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_EI",   bName ~ " EI (N·m²)",     TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_pct",  bName ~ " %",             TableTextAlignment.RIGHT));
        }
        else if (bodyTableType == BodyTableType.GEO_ONLY)
        {
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_area", bName ~ " Area (mm²)",    TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_ctr",  bName ~ " Centroid (mm)", TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_I",    bName ~ " I (mm⁴)",       TableTextAlignment.RIGHT));
        }
        else if (bodyTableType == BodyTableType.EI_ONLY)
        {
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_EI",   bName ~ " EI (N·m²)",     TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_pct",  bName ~ " %",             TableTextAlignment.RIGHT));
        }
        else if (bodyTableType == BodyTableType.BASIC)
        {
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_area",   bName ~ " Area (mm²)",            TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_ctr_na", bName ~ " Centroid above NA (mm)", TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_EI",     bName ~ " EI (N·m²)",             TableTextAlignment.RIGHT));
            columns = append(columns, tableColumnDefinition("b" ~ kStr ~ "_pct",    bName ~ " %",                     TableTextAlignment.RIGHT));
        }
    }

    // Fixed suffix: EI_sum for all types except GEO_ONLY
    if (bodyTableType != BodyTableType.GEO_ONLY)
    {
        columns = append(columns, tableColumnDefinition("bdt_EI_sum", "EI_sum (N·m²)", TableTextAlignment.RIGHT));
    }

    // Build rows
    var rows = [];
    for (var rowData in bodyTableData.rows)
    {
        var cellData = {
            "bdt_station" : toString(rowData.station),
            "bdt_x"       : toString(rowData.x_mm),
            "bdt_EI_calc" : toString(rowData.EI_calc),
            "bdt_NA"      : toString(rowData.NA_height_mm)
        };

        var bodyList = rowData.bodies;
        for (var k = 0; k < numBodies; k += 1)
        {
            var kStr = toString(k);
            var bData = bodyList[k];

            if (bodyTableType == BodyTableType.FULL)
            {
                var areaStr = "";
                var ctrStr  = "";
                var iStr    = "";
                var eiStr   = "";
                var pctStr  = "";
                if (bData != undefined && bData.noMaterial == false)
                {
                    areaStr = toString(bData.area_mm2);
                    ctrStr  = toString(bData.centroid_mm);
                    iStr    = toString(bData.I_centroid_mm4);
                    eiStr   = toString(bData.EI_body);
                    pctStr  = toString(bData.pct);
                }
                cellData["b" ~ kStr ~ "_area"] = areaStr;
                cellData["b" ~ kStr ~ "_ctr"]  = ctrStr;
                cellData["b" ~ kStr ~ "_I"]    = iStr;
                cellData["b" ~ kStr ~ "_EI"]   = eiStr;
                cellData["b" ~ kStr ~ "_pct"]  = pctStr;
            }
            else if (bodyTableType == BodyTableType.GEO_ONLY)
            {
                var areaStr = "";
                var ctrStr  = "";
                var iStr    = "";
                if (bData != undefined)
                {
                    areaStr = toString(bData.area_mm2);
                    ctrStr  = toString(bData.centroid_mm);
                    iStr    = toString(bData.I_centroid_mm4);
                }
                cellData["b" ~ kStr ~ "_area"] = areaStr;
                cellData["b" ~ kStr ~ "_ctr"]  = ctrStr;
                cellData["b" ~ kStr ~ "_I"]    = iStr;
            }
            else if (bodyTableType == BodyTableType.EI_ONLY)
            {
                var eiStr  = "";
                var pctStr = "";
                if (bData != undefined && bData.noMaterial == false)
                {
                    eiStr  = toString(bData.EI_body);
                    pctStr = toString(bData.pct);
                }
                cellData["b" ~ kStr ~ "_EI"]  = eiStr;
                cellData["b" ~ kStr ~ "_pct"] = pctStr;
            }
            else if (bodyTableType == BodyTableType.BASIC)
            {
                var areaStr  = "";
                var ctrNaStr = "";
                var eiStr    = "";
                var pctStr   = "";
                if (bData != undefined && bData.noMaterial == false)
                {
                    areaStr  = toString(bData.area_mm2);
                    ctrNaStr = toString(bData.centroid_above_na_mm);
                    eiStr    = toString(bData.EI_body);
                    pctStr   = toString(bData.pct);
                }
                cellData["b" ~ kStr ~ "_area"]   = areaStr;
                cellData["b" ~ kStr ~ "_ctr_na"] = ctrNaStr;
                cellData["b" ~ kStr ~ "_EI"]     = eiStr;
                cellData["b" ~ kStr ~ "_pct"]    = pctStr;
            }
        }

        if (bodyTableType != BodyTableType.GEO_ONLY)
        {
            cellData["bdt_EI_sum"] = toString(rowData.EI_sum);
        }
        rows = append(rows, tableRow(cellData));
    }

    return table(titlePrefix ~ "Body Detail (" ~ toString(size(bodyTableData.rows)) ~ " sections, " ~ toString(numBodies) ~ " bodies)", columns, rows);
}
