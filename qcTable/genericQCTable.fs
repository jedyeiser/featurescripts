FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import qcTable_types
export import(path : "ff9221b7148cfda8a449abff", version : "1e352b8a8849d91676fd9521");

//import qcTable_stations
import(path : "f78f146e807209053299e5a5", version : "b3a06038d37ca234d362d348");

//import qcTable_geometry
import(path : "0f9cf9b21a3c654880d3167c", version : "d04be9c925d28ec09fb9b6b5");

IconNamespace::import(path : "1388cf5d816e3599efec31fa", version : "81e8fdeedc1202b2863f9ddc");


//this is similar to qcTable, but is more general.
//User specifies a single solid body, FCP, ACP, Table Origin (default to world Origin) and number of evenly spaced points between FCP/ACP
//calculate station spacing (may extend beyond FCP/ACP - in which case keep point spacing uniform, but add a station at the last part extent
//In the event a part does not span FCP/ACP, keep station spacing between FCP/ACP, but ignore sections without data, and add a point at part extents
//use similar table formatting to qcTable (Units, keepUnitsInTable(default = false), numDecimals).
//Debug should show measurement points. Table should display X, Width, Height

/**
 * Generic QC Table Feature
 *
 * A simplified, general-purpose sibling of the ski-core QC table. Measures the
 * world Y (width) and Z (height) cross-section extents of one solid body at a
 * set of evenly spaced stations spanning FCP..ACP (extended to the body ends),
 * and stores the result for a companion table.
 */

// ============================================================================
// EDITING LOGIC
// ============================================================================

export function genericElFunction(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    // Show the origin reference picker only when the origin mode needs one.
    if (definition.tableOrigin == TABLE_ORIGIN.QUERY)
    {
        definition.showOriginQuery = true;
    }
    else
    {
        definition.showOriginQuery = false;
    }

    return definition;
}

// ============================================================================
// FEATURE DEFINITION
// ============================================================================

annotation { "Feature Type Name" : "Generic QC Table", "Editing Logic Function" : "genericElFunction", "Icon" : IconNamespace::BLOB_DATA , "Description" : "Measures width (Y) and height (Z) of a single solid body at evenly spaced stations between FCP and ACP and outputs a QC table." }
export const generateGenericQCData = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Optional table title. When non-blank, overrides the auto title.
        annotation { "Name" : "Name" }
        definition.tableName is string;

        // ===== References =====
        annotation { "Group Name" : "References", "Collapsed By Default" : false }
        {
            annotation { "Name" : "FCP Reference", "Filter" : EntityType.VERTEX || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.fcpReference is Query;

            annotation { "Name" : "ACP Reference", "Filter" : EntityType.VERTEX || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.acpReference is Query;

            annotation { "Name" : "Table Origin", "UIHint" : UIHint.SHOW_LABEL, "Default" : TABLE_ORIGIN.ORIGIN }
            definition.tableOrigin is TABLE_ORIGIN;

            annotation { "Name" : "showOriginQuery", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.showOriginQuery is boolean;

            if (definition.showOriginQuery)
            {
                annotation { "Name" : "Table Origin Reference", "Filter" : EntityType.VERTEX || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.originReference is Query;
            }
        }

        // ===== Body =====
        annotation { "Group Name" : "Body", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Solid body", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
            definition.body is Query;
        }

        // ===== Station Control =====
        annotation { "Group Name" : "Station Control", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Number of Evenly Spaced Points" }
            isInteger(definition.numPoints, sectionCountBounds);

            annotation { "Name" : "Station 0 Reference", "Filter" : EntityType.VERTEX || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.station0Ref is Query;

            annotation { "Name" : "Station Numbering", "UIHint" : UIHint.SHOW_LABEL, "Default" : STATION_DIRECTION.INCREASE_WITH_X }
            definition.stationDirection is STATION_DIRECTION;
        }

        // ===== Table Formatting =====
        annotation { "Group Name" : "Table Formatting", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Table Order", "UIHint" : UIHint.SHOW_LABEL, "Default" : TABLE_ORDER.DESCENDING,
                         "Description" : "Ascending / Descending world X (not tip/tail)." }
            definition.tableOrder is TABLE_ORDER;

            annotation { "Name" : "Table Units", "Default" : EXPORT_UNITS.MILLIMETER }
            definition.tableUnits is EXPORT_UNITS;

            annotation { "Name" : "Keep Units in Table", "Default" : false }
            definition.keepUnits is boolean;

            annotation { "Name" : "Decimal Precision (sig figs)" }
            isInteger(definition.numDecimals, sigFigBounds);
        }

        // ===== Options =====
        annotation { "Group Name" : "Options", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Debug (show measurement points)", "Default" : false }
            definition.debug is boolean;
        }
    }
    {
        // ===================================================================
        // VALIDATION
        // ===================================================================

        if (isQueryEmpty(context, definition.body))
        {
            throw "Please select a solid body";
        }

        // ===================================================================
        // REFERENCES
        // ===================================================================

        var boundaries = extractFCPACP(context, definition.fcpReference, definition.acpReference);

        var tableOriginX = 0 * millimeter;
        if (definition.tableOrigin == TABLE_ORIGIN.QUERY)
        {
            tableOriginX = extractXPosition(context, definition.originReference, "Table Origin");
        }

        // Station 0 datum (required). Numbering counts outward from the station
        // nearest this X position.
        var station0X = extractXPosition(context, definition.station0Ref, "Station 0 Reference");

        var bodyExtents = evBox3d(context, {
            "topology" : definition.body,
            "tight" : true
        });

        // ===================================================================
        // STATIONS
        // ===================================================================

        var stations = generateGenericStations(context, boundaries, bodyExtents, definition.numPoints);

        // ===================================================================
        // MEASURE + FORMAT
        // ===================================================================

        var scale = getUnitScaleFactor(definition.tableUnits);
        var suffix = getUnitSuffix(definition.tableUnits, definition.keepUnits);
        var decimals = definition.numDecimals;

        var rows = [];
        for (var station in stations)
        {
            var m = measureSectionExtents(context, definition.body, station.x);

            // No cross-section here: the body does not reach this station. Drop it.
            if (m == undefined)
            {
                continue;
            }

            rows = append(rows, {
                "x" : roundToPrecision((station.x - tableOriginX).value * scale, decimals) ~ suffix,
                "width" : roundToPrecision(m.width.value * scale, decimals) ~ suffix,
                "height" : roundToPrecision(m.height.value * scale, decimals) ~ suffix,
                "sortX" : station.x
            });

            if (definition.debug)
            {
                // Green: section-center marker (measurement axis location).
                var midY = (m.yMin + m.yMax) / 2;
                var midZ = (m.zMin + m.zMax) / 2;
                addDebugPoint(context, vector(station.x, midY, midZ), DebugColor.GREEN);

                // Cyan: the two points that define the measured width (Y extents).
                addDebugPoint(context, m.yMinPoint, DebugColor.CYAN);
                addDebugPoint(context, m.yMaxPoint, DebugColor.CYAN);

                // Magenta: the two points that define the measured height (Z extents).
                addDebugPoint(context, m.zMinPoint, DebugColor.MAGENTA);
                addDebugPoint(context, m.zMaxPoint, DebugColor.MAGENTA);
            }
        }

        // Order by world X ascending so station numbering is independent of the
        // display order chosen below.
        rows = sort(rows, function(a, b)
        {
            return a.sortX - b.sortX;
        });

        // Station 0 = the row nearest the Station 0 reference. Numbers then count
        // outward, increasing or decreasing with world X per the chosen mode.
        var k0 = 0;
        if (size(rows) > 0)
        {
            var bestDist = abs(rows[0].sortX - station0X);
            for (var i = 1; i < size(rows); i += 1)
            {
                var d = abs(rows[i].sortX - station0X);
                if (d < bestDist)
                {
                    bestDist = d;
                    k0 = i;
                }
            }
        }

        for (var i = 0; i < size(rows); i += 1)
        {
            var num = (definition.stationDirection == STATION_DIRECTION.INCREASE_WITH_X) ? (i - k0) : (k0 - i);
            rows[i].station = num ~ "";
        }

        // DESCENDING = descending world X (rows are in ascending world X here); no tip/tail logic.
        if (definition.tableOrder == TABLE_ORDER.DESCENDING)
        {
            rows = reverse(rows);
        }

        // Strip the sort helper key before storing.
        var tableRows = [];
        for (var r in rows)
        {
            tableRows = append(tableRows, {
                "station" : r.station,
                "x" : r.x,
                "width" : r.width,
                "height" : r.height
            });
        }

        // ===================================================================
        // STORE AS ATTRIBUTE
        // ===================================================================

        setAttribute(context, {
            "entities" : qOrigin(EntityType.BODY),
            "name" : "genericQCTableData",
            "attribute" : {
                "data" : tableRows,
                "tableName" : definition.tableName
            }
        });
    });

// ============================================================================
// STATION GENERATION
// ============================================================================

/**
 * Build the station list: a uniform grid with spacing |RSL|/(N-1) anchored at
 * FCP, extended in both directions to cover the body's X extent, plus exact
 * stations at the body ends. Coincident stations are merged. Stations that fall
 * outside the body produce no cross-section and are dropped during measurement.
 */
function generateGenericStations(context is Context, boundaries is map, bodyExtents is Box3d, n is number) returns array
{
    var s = abs(boundaries.rsl) / (n - 1);
    var anchor = boundaries.fcp;

    var lo = min([boundaries.fcp, boundaries.acp, bodyExtents.minCorner[0]]);
    var hi = max([boundaries.fcp, boundaries.acp, bodyExtents.maxCorner[0]]);

    var kMin = ceil((lo - anchor) / s);
    var kMax = floor((hi - anchor) / s);

    var stations = [];
    for (var k = kMin; k <= kMax; k += 1)
    {
        stations = append(stations, {
            "x" : anchor + k * s,
            "callout" : "",
            "preferred" : false
        });
    }

    // Exact part extents so the table always brackets the body ends even when
    // they fall between grid stations.
    stations = append(stations, {
        "x" : bodyExtents.minCorner[0],
        "callout" : "",
        "preferred" : false
    });
    stations = append(stations, {
        "x" : bodyExtents.maxCorner[0],
        "callout" : "",
        "preferred" : false
    });

    // Collapse duplicates (an extent point landing on a grid station).
    stations = mergeCoincidentStations(stations);

    return stations;
}

// ============================================================================
// TABLE DEFINITION
// ============================================================================

annotation { "Table Type Name" : "Generic QC Table", "Icon" : IconNamespace::BLOB_DATA }
export const genericQCTable = defineTable(function(context is Context, definition is map) returns Table
    precondition
    {
        // No additional parameters needed
    }
    {
        var withAttr = evaluateQuery(context, qHasAttribute("genericQCTableData"));

        if (size(withAttr) == 0)
        {
            return table("Generic QC Table (No Data)", [], []);
        }

        var attr = getAttribute(context, {
            "entity" : withAttr[0],
            "name" : "genericQCTableData"
        });

        var columns = [
            tableColumnDefinition("station", "Station #"),
            tableColumnDefinition("x", "X"),
            tableColumnDefinition("width", "Width"),
            tableColumnDefinition("height", "Height")
        ];

        var rows = [];
        for (var rowData in attr.data)
        {
            rows = append(rows, tableRow(rowData));
        }

        var title = "Generic QC Table";
        if (attr.tableName != undefined && attr.tableName != "")
        {
            title = attr.tableName;
        }

        return table(title, columns, rows);
    });
