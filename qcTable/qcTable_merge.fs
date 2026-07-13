FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
//import qcTable_types
import(path : "ff9221b7148cfda8a449abff", version : "31d87ccebeed70c9ff76b9ed");


/**
 * QC Table Data Merging
 *
 * Merges core and sidewall measurement data, computes deltas, and formats table rows.
 */

// Placeholder shown when a core measurement was expected but could not be taken.
export const NO_DATA = "No Data";

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
    tableOriginX is ValueWithUnits,
    swFromEndX,
    formatConfig is FormatConfig,
    stationNumbering is map) returns array
{
    var merged = [];

    // World X of each built row, parallel to `merged`, used to assign station
    // numbers after all rows are known.
    var rowXs = [];

    // ACP at larger X means the aft (tail) end is the max-X extent; otherwise
    // the part is mirrored and the tail is the min-X extent.
    var acpAtLargerX = boundaries.rsl >= 0 * millimeter;

    for (var station in stations)
    {
        var x = station.x;
        var coreData = coreMeasurements[x];
        var swData = swMeasurements[x];

        // A station is "core-expected" when it falls within the core's X extents
        // but produced no measurement. Cores have data throughout FCP/ACP, so a
        // missing measurement here is anomalous and should read "No Data" rather
        // than appear blank. Stations beyond the core (SW-only) stay blank.
        var coreExpected = coreExtents != undefined
            && x >= coreExtents.minCorner[0] - EDGE_MARGIN
            && x <= coreExtents.maxCorner[0] + EDGE_MARGIN;

        // Skip only if there is genuinely nothing to report at this station
        if (coreData == undefined && swData == undefined && !coreExpected)
        {
            continue;
        }

        // Build base row. "X" is reported relative to the chosen table origin.
        // The station number is a placeholder here; it is assigned after the
        // loop once the full set of displayed rows is known.
        var row = {
            "station" : 0,
            callout: station.callout,
            x_mrs: x - tableOriginX,
            x_acp: x - boundaries.acp
        };

        // Add core measurements if present
        if (coreData != undefined)
        {
            var coreTailX = acpAtLargerX ? coreExtents.maxCorner[0] : coreExtents.minCorner[0];
            row.x_core = abs(x - coreTailX);
            row.core_height = coreData.coreThickness;
            row.coreWidth = coreData.coreWidth;
            row.groovedThickness = coreData.groovedThickness;
            row.topWidth = coreData.coreTopWidth;
            row.topAngle = coreData.coreTopAngle;
            row.baseRoutDepth = coreData.baseRoutDepth;
            row.baseRoutWidth = coreData.baseRoutWidth;
        }
        else if (coreExpected)
        {
            // Core should have intersected here but the measurement failed.
            // x_core is purely geometric, so keep it; mark the measured
            // quantities as "No Data".
            var coreTailX = acpAtLargerX ? coreExtents.maxCorner[0] : coreExtents.minCorner[0];
            row.x_core = abs(x - coreTailX);
            row.core_height = NO_DATA;
            row.coreWidth = NO_DATA;
            row.groovedThickness = NO_DATA;
            row.topWidth = NO_DATA;
            row.topAngle = NO_DATA;
            row.baseRoutDepth = NO_DATA;
            row.baseRoutWidth = NO_DATA;
        }

        // Add SW measurements if present
        if (swData != undefined)
        {
            var swTailX = acpAtLargerX ? swExtents.maxCorner[0] : swExtents.minCorner[0];
            row.x_sw = abs(x - swTailX);
            row.sw_height = swData.swHeight;

            // Distance along X from a user-picked reference to this station. Cores and
            // sidewalls are programmed from their own (often shifted) endpoints; the
            // core's end is served by the primary X column, so this is SW-specific.
            if (swFromEndX != undefined)
            {
                row.sw_from_end = abs(x - swFromEndX);
            }
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
        rowXs = append(rowXs, x);
    }

    // Assign station numbers now that the displayed rows are known.
    merged = assignStationNumbers(merged, rowXs, stationNumbering, boundaries);

    return merged;
}

/**
 * Assign the "station" number to each row. Rows arrive in ascending world-X
 * order, so a row's index is its ascending rank.
 *
 * DEFAULT: 0-based, counting from the FCP/tip end toward the ACP/tail end. This
 *          is oriented by the FCP->ACP direction (sign of rsl), NOT by world X.
 *          The legacy code numbered from the min-X row, which only matched the
 *          tip when FCP happened to sit at a lesser X than ACP; parts modeled
 *          with FCP at the greater X then numbered backwards (station 0 landing
 *          on the tail). Anchoring to FCP keeps numbering consistent whichever
 *          way the part is modeled.
 * CUSTOM:  the row nearest stationNumbering.station0X is 0; numbers then count
 *          outward, increasing or decreasing with world X per the direction.
 */
function assignStationNumbers(rows is array, rowXs is array, stationNumbering is map, boundaries is map) returns array
{
    if (stationNumbering.mode == STATION_NUMBERING.CUSTOM && size(rows) > 0)
    {
        var station0X = stationNumbering.station0X;

        // Nearest row to the reference (ties resolve toward lower X).
        var k0 = 0;
        var bestDist = abs(rowXs[0] - station0X);
        for (var i = 1; i < size(rowXs); i += 1)
        {
            var d = abs(rowXs[i] - station0X);
            if (d < bestDist)
            {
                bestDist = d;
                k0 = i;
            }
        }

        for (var i = 0; i < size(rows); i += 1)
        {
            rows[i].station = (stationNumbering.direction == STATION_DIRECTION.INCREASE_WITH_X) ? (i - k0) : (k0 - i);
        }
    }
    else
    {
        // Station 0 at the FCP/tip end. When FCP is at the lesser X (rsl >= 0)
        // that is the min-X row (ascending index); when the part is mirrored
        // (FCP at the greater X) it is the max-X row, so count down from the end.
        var fcpAtMinX = boundaries.rsl >= 0 * millimeter;
        var n = size(rows);
        for (var i = 0; i < n; i += 1)
        {
            rows[i].station = fcpAtMinX ? i : (n - 1 - i);
        }
    }

    return rows;
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

    if (row.sw_from_end != undefined)
    {
        formatted.sw_from_end = formatValue(row.sw_from_end, scaleFactor, formatConfig.sigFigs, suffix);
    }

    // Format core fields. These can hold the NO_DATA string when a core
    // measurement was expected but failed, so pass strings through unchanged.
    if (row.core_height != undefined)
    {
        formatted.core_height = formatField(row.core_height, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.coreWidth != undefined)
    {
        formatted.coreWidth = formatField(row.coreWidth, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.groovedThickness != undefined)
    {
        formatted.groovedThickness = formatField(row.groovedThickness, scaleFactor, formatConfig.sigFigs, suffix);
    }

    if (row.topWidth != undefined)
    {
        formatted.topWidth = formatField(row.topWidth, scaleFactor, formatConfig.sigFigs, suffix);
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
 * Format a field that is either a measured value or a placeholder string
 * (e.g. NO_DATA). Strings pass through unchanged; values are formatted.
 */
function formatField(value, scaleFactor is number, sigFigs is number, suffix is string) returns string
{
    if (value is string)
    {
        return value;
    }
    return formatValue(value, scaleFactor, sigFigs, suffix);
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
export function sortTableRows(rows is array, tableOrder is TABLE_ORDER, boundaries is map) returns array
{
    // Rows arrive in ascending world-X order. Tip is the FCP side: when the
    // part is mirrored (ACP at smaller X) ascending world-X runs tail -> tip,
    // so the reversal logic flips.
    var mirrored = boundaries.rsl < 0 * millimeter;

    var reverseNeeded = (tableOrder == TABLE_ORDER.ASCENDING) ? mirrored : !mirrored;

    if (reverseNeeded)
    {
        // Tail to Tip (station 0 at tail) or mirrored Tip to Tail
        return reverse(rows);
    }

    // Tip to Tail (station 0 at tip)
    return rows;
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
    detailLevel is DETAIL_LEVEL,
    language is LANGUAGE,
    hasSwFromEnd is boolean) returns array
{
    var columns = [];

    // STANDARD columns (always present)
    columns = append(columns, tableColumnDefinition("callout", translateColumnName("Callout", language)));
    columns = append(columns, tableColumnDefinition("station", translateColumnName("Station", language)));
    columns = append(columns, tableColumnDefinition("x_mrs", translateColumnName("X", language)));

    if (hasCore)
    {
        columns = append(columns, tableColumnDefinition("core_height", translateColumnName("Core Height", language)));
        columns = append(columns, tableColumnDefinition("coreWidth", translateColumnName("Core Width", language)));
    }

    if (hasSW)
    {
        columns = append(columns, tableColumnDefinition("sw_height", translateColumnName("SW Height", language)));
    }

    // SW From-End distance (shown at all detail levels when a reference was picked).
    if (hasSW && hasSwFromEnd)
    {
        columns = append(columns, tableColumnDefinition("sw_from_end", translateColumnName("SW From End", language)));
    }

    // Core/SW delta (only if both present)
    if (hasCore && hasSW)
    {
        columns = append(columns, tableColumnDefinition("core_sw_delta", translateColumnName("Core/SW \u0394", language)));
    }

    // DETAILS columns (additional measurements)
    if (detailLevel == DETAIL_LEVEL.DETAILS)
    {
        // Distance references
        columns = append(columns, tableColumnDefinition("x_acp", translateColumnName("X from ACP", language)));

        if (hasCore)
        {
            columns = append(columns, tableColumnDefinition("x_core", translateColumnName("X from Core Tail", language)));
        }

        if (hasSW)
        {
            columns = append(columns, tableColumnDefinition("x_sw", translateColumnName("X from SW Tail", language)));
        }

        // Core geometry details
        if (hasCore)
        {
            columns = append(columns, tableColumnDefinition("groovedThickness", translateColumnName("Grooved Thickness", language)));
            columns = append(columns, tableColumnDefinition("topWidth", translateColumnName("Top Width", language)));
            columns = append(columns, tableColumnDefinition("topAngle", translateColumnName("Top Angle", language)));
            columns = append(columns, tableColumnDefinition("baseRoutDepth", translateColumnName("BR Depth", language)));
            columns = append(columns, tableColumnDefinition("baseRoutWidth", translateColumnName("BR Width", language)));
        }
    }

    return columns;
}
