FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/table.fs", version : "2878.0");
import(path : "onshape/std/geomOperations.fs", version : "2878.0");

//import qcTable_types
import(path : "ff9221b7148cfda8a449abff", version : "925c5f3a57e66d7c4a2d6eb9");
//import qcTable_stations
import(path : "ff9221b7148cfda8a449abff", version : "925c5f3a57e66d7c4a2d6eb9");
//import qcTable_geometry
import(path : "ff9221b7148cfda8a449abff", version : "925c5f3a57e66d7c4a2d6eb9");
//import qcTable_merge
import(path : "ff9221b7148cfda8a449abff", version : "925c5f3a57e66d7c4a2d6eb9");

/**
 * QC Table Feature - Main Feature Definition
 *
 * Unified quality control table generation for ski cores and sidewalls.
 * Automatically handles core only, sidewall only, or both with smart station merging.
 */

// ============================================================================
// EDITING LOGIC
// ============================================================================

export function elFunction(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    // Show/hide point generation parameters based on method
    if (definition.pointGeneration == POINT_TYPES.STATIC_DISTANCE)
    {
        definition.showPointDist = true;
        definition.showPointCount = false;
    }
    else
    {
        definition.showPointDist = false;
        definition.showPointCount = true;
    }

    return definition;
}

// ============================================================================
// FEATURE DEFINITION
// ============================================================================

annotation { "Feature Type Name" : "Generate QC Table Data", "Editing Logic Function" : "elFunction" }
export const generateQCData = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // ===== FCP/ACP References =====
        annotation { "Group Name" : "References", "Driving Parameter" : "fcpReference", "Collapsed By Default" : false }
        {
            annotation { "Name" : "FCP Reference", "Filter" : EntityType.VERTEX || EntityType.FACE, "MaxNumberOfPicks" : 1 }
            definition.fcpReference is Query;

            annotation { "Name" : "ACP Reference", "Filter" : EntityType.VERTEX || EntityType.FACE, "MaxNumberOfPicks" : 1 }
            definition.acpReference is Query;
        }

        // ===== Body Selection =====
        annotation { "Group Name" : "Bodies", "Driving Parameter" : "coreBody", "Collapsed By Default" : false }
        {
            annotation {
                "Name" : "Core body or composite part (optional)",
                "Filter" : EntityType.BODY && BodyType.SOLID,
                "Description" : "Select single body or composite part. Leave empty for sidewall only."
            }
            definition.coreBody is Query;

            annotation {
                "Name" : "Sidewall body (optional)",
                "Filter" : EntityType.BODY && BodyType.SOLID,
                "MaxNumberOfPicks" : 1,
                "Description" : "Select single sidewall body. Leave empty for core only."
            }
            definition.sidewallBody is Query;
        }

        // ===== Station Control =====
        annotation { "Group Name" : "Station Control", "Driving Parameter" : "boundaryBehavior", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Outside FCP/ACP", "UIHint" : UIHint.SHOW_LABEL, "Default" : BOUNDARY_BEHAVIOR.NORMAL }
            definition.boundaryBehavior is BOUNDARY_BEHAVIOR;

            annotation { "Name" : "Point Generation Method", "UIHint" : UIHint.SHOW_LABEL, "Default" : POINT_TYPES.EVENLY_DIVIDE_RSL }
            definition.pointGeneration is POINT_TYPES;

            // Hidden control for showing/hiding parameters
            annotation { "Name" : "showPointCount", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.showPointCount is boolean;

            annotation { "Name" : "showPointDist", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.showPointDist is boolean;

            if (definition.showPointCount)
            {
                annotation { "Name" : "Number of Evenly Spaced Points" }
                isInteger(definition.numEvenPoints, pointNumBounds);
            }

            if (definition.showPointDist)
            {
                annotation { "Name" : "Static Point Start Location", "UIHint" : UIHint.SHOW_LABEL }
                definition.staticStart is START_STATIC_POINTS;

                annotation { "Name" : "Distance Between Points" }
                isLength(definition.pointDistance, pointDistBounds);
            }

            annotation { "Name" : "Additional Points (optional)", "Filter" : EntityType.VERTEX }
            definition.addtlPoints is Query;
        }

        // ===== Table Formatting =====
        annotation { "Group Name" : "Table Formatting", "Driving Parameter" : "tableUnits", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Table Order", "UIHint" : UIHint.SHOW_LABEL, "Default" : TABLE_ORDER.DESCENDING }
            definition.tableOrder is TABLE_ORDER;

            annotation { "Name" : "Table Units", "Default" : EXPORT_UNITS.MILLIMETER }
            definition.tableUnits is EXPORT_UNITS;

            annotation { "Name" : "Decimal Precision (sig figs)" }
            isInteger(definition.sigFigs, sigFigBounds);

            annotation { "Name" : "Show Units in Table", "Default" : true }
            definition.showUnits is boolean;
        }

        // ===== Options =====
        annotation { "Group Name" : "Options", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Verbose Debug (show debug entities)", "Default" : false }
            definition.verbose is boolean;

            annotation { "Name" : "Keep Measurement Wires (sidewall)", "Default" : false }
            definition.keepWires is boolean;
        }
    }
    {
        // ===================================================================
        // VALIDATION
        // ===================================================================

        var hasCore = !isQueryEmpty(context, definition.coreBody);
        var hasSW = !isQueryEmpty(context, definition.sidewallBody);

        if (!hasCore && !hasSW)
        {
            throw "Please select at least one body (core or sidewall)";
        }

        // ===================================================================
        // FCP/ACP EXTRACTION
        // ===================================================================

        var boundaries = extractFCPACP(context, definition.fcpReference, definition.acpReference);

        if (definition.verbose)
        {
            println("FCP: " ~ boundaries.fcp);
            println("ACP: " ~ boundaries.acp);
            println("RSL: " ~ boundaries.rsl);
        }

        // ===================================================================
        // BODY PREPARATION
        // ===================================================================

        var coreData = undefined;
        var coreExtents = undefined;

        if (hasCore)
        {
            coreData = prepareCoreBodies(context, definition.coreBody);
            coreExtents = evBox3d(context, {
                topology: coreData.bodies,
                tight: true
            });

            if (definition.verbose)
            {
                println("Core bodies: " ~ coreData.bodyCount ~ (coreData.isComposite ? " (composite)" : ""));
            }
        }

        var swData = undefined;
        var swExtents = undefined;

        if (hasSW)
        {
            swData = prepareSidewallBody(context, definition.sidewallBody);
            swExtents = evBox3d(context, {
                topology: swData.bodies,
                tight: true
            });

            if (definition.verbose)
            {
                println("Sidewall: single body");
            }
        }

        // ===================================================================
        // STATION GENERATION
        // ===================================================================

        var stations = generateStations(
            context,
            definition,
            boundaries,
            coreExtents,
            swExtents
        );

        // Add user-specified additional points
        if (!isQueryEmpty(context, definition.addtlPoints))
        {
            stations = addUserPoints(context, stations, definition.addtlPoints);
            // Re-sort and merge after adding user points
            stations = sort(stations, function(a, b) { return a.x - b.x; });
        }

        if (definition.verbose)
        {
            println("Total stations: " ~ size(stations));
        }

        // ===================================================================
        // CORE MEASUREMENTS
        // ===================================================================

        var coreMeasurements = {};

        if (hasCore)
        {
            for (var i = 0; i < size(stations); i += 1)
            {
                var station = stations[i];

                var measurement = measureCoreAtStation(
                    context,
                    id + ("core" ~ i ~ "_"),
                    coreData,
                    station.x,
                    coreExtents,
                    definition.verbose
                );

                if (measurement != undefined)
                {
                    coreMeasurements[station.x] = measurement;
                }
            }

            if (definition.verbose)
            {
                println("Core measurements: " ~ size(keys(coreMeasurements)));
            }
        }

        // ===================================================================
        // SIDEWALL MEASUREMENTS
        // ===================================================================

        var swMeasurements = {};

        if (hasSW)
        {
            // Setup SW measurement infrastructure
            var swSetup = setupSidewallMeasurement(context, id + "sw_", swData, swExtents);

            // Measure at each station
            for (var station in stations)
            {
                var measurement = measureSidewallAtStation(
                    context,
                    swSetup,
                    station.x,
                    definition.verbose
                );

                if (measurement != undefined)
                {
                    swMeasurements[station.x] = measurement;
                }
            }

            if (definition.verbose)
            {
                println("SW measurements: " ~ size(keys(swMeasurements)));
            }

            // Handle measurement wires
            if (definition.keepWires)
            {
                setProperty(context, {
                    entities: swSetup.centerSplineBottom,
                    propertyType: PropertyType.NAME,
                    value: "SW_BOTTOM_CENTER_WIRE"
                });

                setProperty(context, {
                    entities: swSetup.centerSplineTop,
                    propertyType: PropertyType.NAME,
                    value: "SW_TOP_CENTER_WIRE"
                });
            }
            else
            {
                opDeleteBodies(context, id + "deleteSWWires", {
                    entities: qUnion([swSetup.centerSplineBottom, swSetup.centerSplineTop])
                });
            }
        }

        // ===================================================================
        // MERGE DATA AND COMPUTE DELTAS
        // ===================================================================

        var formatConfig = {
            tableUnits: definition.tableUnits,
            sigFigs: definition.sigFigs,
            showUnits: definition.showUnits
        } as FormatConfig;

        var tableData = mergeStationData(
            stations,
            coreMeasurements,
            swMeasurements,
            boundaries,
            coreExtents,
            swExtents,
            formatConfig
        );

        // Sort by table order
        tableData = sortTableRows(tableData, definition.tableOrder);

        if (definition.verbose)
        {
            println("Table rows: " ~ size(tableData));
        }

        // ===================================================================
        // STORE AS ATTRIBUTE
        // ===================================================================

        setAttribute(context, {
            entities: qOrigin(EntityType.BODY),
            name: "qcTableData",
            attribute: {
                data: tableData,
                hasCore: hasCore,
                hasSW: hasSW,
                tableOrder: definition.tableOrder,
                format: formatConfig
            }
        });

        if (definition.verbose)
        {
            println("QC Table generation complete");
        }
    });

// ============================================================================
// TABLE DEFINITION
// ============================================================================

annotation { "Table Type Name" : "QC Table" }
export const qcTable = defineTable(function(context is Context, definition is map) returns Table
    precondition
    {
        // No additional parameters needed
    }
    {
        // Find bodies with qcTableData attribute
        var bodiesWithAttribute = evaluateQuery(context, qHasAttribute("qcTableData"));

        if (size(bodiesWithAttribute) == 0)
        {
            // No data - return empty table
            return table("QC Table (No Data)", [], []);
        }

        // Get attribute data
        var tableAttribute = getAttribute(context, {
            entity: bodiesWithAttribute[0],
            name: "qcTableData"
        });

        var tableData = tableAttribute.data;
        var hasCore = tableAttribute.hasCore;
        var hasSW = tableAttribute.hasSW;

        // Build dynamic column definitions
        var columns = buildColumnDefinitions(hasCore, hasSW);

        // Build table rows
        var rows = [];
        for (var rowData in tableData)
        {
            rows = append(rows, tableRow(rowData));
        }

        // Return table with appropriate title
        var title = "QC Table";
        if (hasCore && hasSW)
        {
            title = "QC Table (Core + Sidewall)";
        }
        else if (hasCore)
        {
            title = "QC Table (Core Only)";
        }
        else if (hasSW)
        {
            title = "QC Table (Sidewall Only)";
        }

        return table(title, columns, rows);
    });
