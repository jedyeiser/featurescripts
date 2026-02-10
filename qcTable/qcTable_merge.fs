FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
//import qcTable_types
import(path : "ff9221b7148cfda8a449abff", version : "55fa348279ead98a1ba723b9");


/**
 * QC Table Data Merging
 *
 * Merges core and sidewall measurement data, computes deltas, and formats table rows.
 */

// ============================================================================
// DATA MERGING
// ============================================================================

/**
 * Merge core and sidewall measurements into unified table rows
 * Computes deltas when both measurements are present
 */
export function mergeStationData(
    stations is array,
    coreMeasurements is map,
    swMeasurements is map,
    boundaries is map,
    coreExtents,
    swExtents,
    formatConfig is FormatConfig) returns array
{
    var merged = [];
    var stationNum = 0;

    for (var station in stations)
    {
        var x = station.x;
        var coreData = coreMeasurements[x];
        var swData = swMeasurements[x];

        // Skip if no measurements at this station
        if (coreData == undefined && swData == undefined)
        {
            continue;
        }

        // Build base row
        var row = {
            "station" : stationNum,
            callout: station.callout,
            x_mrs: x,
            x_acp: x - boundaries.acp
        };

        // Add core measurements if present
        if (coreData != undefined)
        {
            row.x_core = coreExtents.maxCorner[0] - x;
            row.core_height = coreData.coreThickness;
            row.coreWidth = coreData.coreWidth;
            row.groovedThickness = coreData.groovedThickness;
            row.topWidth = coreData.coreTopWidth;
            row.topAngle = coreData.coreTopAngle;
            row.baseRoutDepth = coreData.baseRoutDepth;
            row.baseRoutWidth = coreData.baseRoutWidth;
        }

        // Add SW measurements if present
        if (swData != undefined)
        {
            row.x_sw = swExtents.maxCorner[0] - x;
            row.sw_height = swData.swHeight;
        }

        // Compute delta if both present (how much thicker is core than SW?)
        if (coreData != undefined && swData != undefined)
        {
            row.core_sw_delta = coreData.coreThickness - swData.swHeight;
            row.bottom_delta = coreData.coreBottomZ - swData.swBottomZ;
            row.top_delta = coreData.coreTopZ - swData.swTopZ;
        }

        // Format the row
        row = formatTableRow(row, formatConfig);

        merged = append(merged, row);
        stationNum += 1;
    }

    return merged;
}

// ============================================================================
// FORMATTING
// ============================================================================

/**
 * Format a table row with units and sig figs
 * Converts ValueWithUnits to formatted strings
 */
function formatTableRow(row is map, formatConfig is FormatConfig) returns map
{
    var scaleFactor = getUnitScaleFactor(formatConfig.tableUnits);
    var suffix = getUnitSuffix(formatConfig.tableUnits, formatConfig.showUnits);

    var formatted = {};

    // Copy non-formatted fields
    formatted.station = row.station;
    formatted.callout = row.callout;

    // Format distance fields
    if (row.x_mrs != undefined)
    {
        formatted.x_mrs = formatValue(row.x_mrs, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.x_acp != undefined)
    {
        formatted.x_acp = formatValue(row.x_acp, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.x_core != undefined)
    {
        formatted.x_core = formatValue(row.x_core, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.x_sw != undefined)
    {
        formatted.x_sw = formatValue(row.x_sw, scaleFactor, formatConfig.sigFigs, suffix);
    }

    // Format core fields
    if (row.core_height != undefined)
    {
        formatted.core_height = formatValue(row.core_height, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.coreWidth != undefined)
    {
        formatted.coreWidth = formatValue(row.coreWidth, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.groovedThickness != undefined)
    {
        formatted.groovedThickness = formatValue(row.groovedThickness, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.topWidth != undefined)
    {
        formatted.topWidth = formatValue(row.topWidth, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.topAngle != undefined)
    {
        // Angle is special - always format as degrees with suffix
        if (row.topAngle is string)
        {
            formatted.topAngle = row.topAngle;  // Empty string
        }
        else
        {
            formatted.topAngle = roundToPrecision(row.topAngle / degree, formatConfig.sigFigs) ~ '\u00B0';
        }
    }

    if (row.baseRoutDepth != undefined)
    {
        if (row.baseRoutDepth is string)
        {
            formatted.baseRoutDepth = row.baseRoutDepth;  // Empty string
        }
        else
        {
            formatted.baseRoutDepth = formatValue(row.baseRoutDepth, scaleFactor, formatConfig.sigFigs, suffix);
        }
    }

    if (row.baseRoutWidth != undefined)
    {
        if (row.baseRoutWidth is string)
        {
            formatted.baseRoutWidth = row.baseRoutWidth;  // Empty string
        }
        else
        {
            formatted.baseRoutWidth = formatValue(row.baseRoutWidth, scaleFactor, formatConfig.sigFigs, suffix);
        }
    }

    // Format SW fields
    if (row.sw_height != undefined)
    {
        formatted.sw_height = formatValue(row.sw_height, scaleFactor, formatConfig.sigFigs, suffix);
    }

    // Format delta fields
    if (row.core_sw_delta != undefined)
    {
        formatted.core_sw_delta = formatValue(row.core_sw_delta, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.bottom_delta != undefined)
    {
        formatted.bottom_delta = formatValue(row.bottom_delta, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.top_delta != undefined)
    {
        formatted.top_delta = formatValue(row.top_delta, scaleFactor, formatConfig.sigFigs, suffix);
    }

    return formatted;
}

/**
 * Format a single value with units
 */
function formatValue(value is ValueWithUnits, scaleFactor is number, sigFigs is number, suffix is string) returns string
{
    var numericValue = value.value * scaleFactor;
    return roundToPrecision(numericValue, sigFigs) ~ suffix;
}

// ============================================================================
// TABLE ARRAY SORTING
// ============================================================================

/**
 * Sort table rows by table order (ascending or descending)
 */
export function sortTableRows(rows is array, tableOrder is TABLE_ORDER) returns array
{
    if (tableOrder == TABLE_ORDER.ASCENDING)
    {
        // Ascending: Tip to Tail (station 0 at tip)
        return rows;  // Already in order
    }
    else
    {
        // Descending: Tail to Tip (station 0 at tail)
        return reverse(rows);
    }
}

// ============================================================================
// COLUMN DEFINITIONS
// ============================================================================

/**
 * Build dynamic column definitions based on what data is present and detail level
 */
export function buildColumnDefinitions(
    hasCore is boolean,
    hasSW is boolean,
    detailLevel is DETAIL_LEVEL) returns array
{
    var columns = [];

    // STANDARD columns (always present)
    columns = append(columns, tableColumnDefinition("callout", "Callout"));
    columns = append(columns, tableColumnDefinition("station", "Station"));
    columns = append(columns, tableColumnDefinition("x_mrs", "X"));

    if (hasCore)
    {
        columns = append(columns, tableColumnDefinition("core_height", "Core Height"));
    }

    if (hasSW)
    {
        columns = append(columns, tableColumnDefinition("sw_height", "SW Height"));
    }

    // Core/SW delta (only if both present)
    if (hasCore && hasSW)
    {
        columns = append(columns, tableColumnDefinition("core_sw_delta", "Core/SW \u0394"));
    }

    // DETAILS columns (additional measurements)
    if (detailLevel == DETAIL_LEVEL.DETAILS)
    {
        // Distance references
        columns = append(columns, tableColumnDefinition("x_acp", "X from ACP"));

        if (hasCore)
        {
            columns = append(columns, tableColumnDefinition("x_core", "X from Core Tail"));
        }

        if (hasSW)
        {
            columns = append(columns, tableColumnDefinition("x_sw", "X from SW Tail"));
        }

        // Core geometry details
        if (hasCore)
        {
            columns = append(columns, tableColumnDefinition("coreWidth", "Core Width"));
            columns = append(columns, tableColumnDefinition("groovedThickness", "Grooved Thickness"));
            columns = append(columns, tableColumnDefinition("topWidth", "Top Width"));
            columns = append(columns, tableColumnDefinition("topAngle", "Top Angle"));
            columns = append(columns, tableColumnDefinition("baseRoutDepth", "BR Depth"));
            columns = append(columns, tableColumnDefinition("baseRoutWidth", "BR Width"));
        }
    }

    return columns;
}
