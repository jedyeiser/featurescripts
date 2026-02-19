FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// IMPORT: ExportCurveCore.fs
export import(path : "666228ba3514cc062764888b", version : "9a1363cd20be83b8573cf508");


/**
 * ________________ EXPORT CURVE ____________________
 * Takes an edge query (can be multiple, but must be G0 Continuious),
 * a sectionAlong enum {CHAIN, QUERY, WORLD} which dictates how we will generate points
 * in our export table, and user input about table export format (units, sig figs,
 * clean (no units), number of points). Samples along the input edges based on user
 * inputs and exports a custom table with evenly spaced point data along the edge(s),
 * though what 'evenly spaced' means depends on user input.
 *
 * If sectionAlong == AlongType.CHAIN, section N evenly spaced points (in distance)
 * along the chain. In the event that our edges have G1 continuity, this would be equivelant to
 * a parameter range of range(0, 1, N) over the path that composes the edges.
 *
 * If sectionAlong == AlongType.QUERY, the user will need to provide a query to define our spacing.
 * If the query is an edge, it must be a line. Our evaluation planes will be normal to this line. We then create N evenly spaced planes
 * that bound our edges correctly, and evaluate the intersection of our plane and one of the edges.
 * If the query is a face, it must be a planar face. The normal of this face is analogous to the tangent of the line above.
 * If sectionAlong == AlongType.WORLD, the user must select an alongAxis enum that specifies if the cross section planes
 * should be normal to worldX, worldY or worldZ. In this case, alongAxis.worldX would mean evenly spaced points in X.
 *
 *
 */


// =============================================================================
// EDITING LOGIC FUNCTION
// =============================================================================

export function elFunction(context is Context, id is Id, oldDef is map, def is map,
                            isCreating is boolean, specifiedParams is map) returns map
{
    def.showGeomQuery = (def.alongType == AlongType.QUERY);
    def.showWorldAxis = (def.alongType == AlongType.WORLD);
    return def;
}


// =============================================================================
// FEATURE DEFINITION
// =============================================================================

annotation { "Feature Type Name" : "Export Curve",
             "Editing Logic Function" : "elFunction" }
export const exportCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Edge chain selection
        annotation { "Name" : "Edges", "Filter" : EntityType.EDGE }
        definition.edgeQuery is Query;

        // Spacing type
        annotation { "Name" : "Section Along", "Default" : AlongType.CHAIN }
        definition.alongType is AlongType;

        // Hidden visibility toggles (set by elFunction)
        annotation { "Name" : "showGeomQuery", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showGeomQuery is boolean;

        annotation { "Name" : "showWorldAxis", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showWorldAxis is boolean;

        // Conditional: reference geometry for QUERY mode
        if (definition.showGeomQuery)
        {
            annotation { "Name" : "Reference Geometry",
                         "Filter" : EntityType.EDGE || EntityType.FACE,
                         "MaxNumberOfPicks" : 1 }
            definition.geomQuery is Query;
        }

        // Conditional: world axis for WORLD mode
        if (definition.showWorldAxis)
        {
            annotation { "Name" : "World Axis", "Default" : AlongAxis.WORLD_X }
            definition.alongAxis is AlongAxis;
        }

        // Output settings
        annotation { "Name" : "Number of Points" }
        isInteger(definition.numPoints, NUM_POINTS_BOUNDS);

        annotation { "Name" : "Export Units", "Default" : ExportUnits.MILLIMETER }
        definition.tableUnits is ExportUnits;

        annotation { "Name" : "Significant Figures" }
        isInteger(definition.sigFigs, SIG_FIGS_BOUNDS);

        annotation { "Name" : "Show Units in Table", "Default" : true }
        definition.showUnits is boolean;

        annotation { "Name" : "Include Parameters", "Default" : false }
        definition.addParameters is boolean;

        annotation { "Name" : "Include Slopes (Tangent / Normal)", "Default" : false }
        definition.addSlopes is boolean;
    }
    {
        // Build format config
        var formatConfig = {
            "tableUnits"    : definition.tableUnits,
            "sigFigs"       : definition.sigFigs,
            "showUnits"     : definition.showUnits,
            "addParameters" : definition.addParameters,
            "addSlopes"     : definition.addSlopes
        };

        // Collect and order edges into a chain
        var chainResult = collectAndOrderEdges(context, definition.edgeQuery);
        var orderedCurves = chainResult.curves;
        var chainStart    = chainResult.chainStart;

        // Sample based on mode
        var samples;

        if (definition.alongType == AlongType.CHAIN)
        {
            var chainCurve = assembleCurveChain(context, orderedCurves);
            samples = sampleChain(chainCurve, definition.numPoints, definition.addSlopes);
        }
        else if (definition.alongType == AlongType.QUERY)
        {
            var direction = getQueryDirection(context, definition.geomQuery);
            samples = sampleByPlanes(context, orderedCurves, direction,
                                     definition.numPoints, chainStart, definition.addSlopes);
        }
        else
        {
            // WORLD mode
            var direction = getAxisDirection(definition.alongAxis);
            samples = sampleByPlanes(context, orderedCurves, direction,
                                     definition.numPoints, chainStart, definition.addSlopes);
        }

        // Build row data
        var rows = buildTableRows(samples, formatConfig);

        // Store attribute on a stable anchor entity
        setAttribute(context, {
            "entities"  : qOrigin(EntityType.BODY),
            "name"      : "curveExportData",
            "attribute" : {
                "rows"         : rows,
                "formatConfig" : formatConfig
            }
        });
    });


// =============================================================================
// TABLE DEFINITION
// =============================================================================

annotation { "Table Type Name" : "Curve Export Table" }
export const curveExportTable = defineTable(function(context is Context, definition is map) returns Table
    precondition
    {
    }
    {
        // Look for attribute on any entity
        var anchors = evaluateQuery(context, qHasAttribute("curveExportData"));

        if (size(anchors) == 0)
        {
            return table("Curve Export (No Data)", [], []);
        }

        var attr = getAttribute(context, {
            "entity" : anchors[0],
            "name"   : "curveExportData"
        });

        var fc = attr.formatConfig;

        // Build column definitions
        var cols = [
            tableColumnDefinition("n",   "N"),
            tableColumnDefinition("x",   "X"),
            tableColumnDefinition("y",   "Y"),
            tableColumnDefinition("z",   "Z"),
            tableColumnDefinition("csv", "X, Y, Z")
        ];

        if (fc.addParameters)
        {
            cols = append(cols, tableColumnDefinition("param",     "Param"));
            cols = append(cols, tableColumnDefinition("arclength", "Arc Length"));
        }

        if (fc.addSlopes)
        {
            cols = concatenateArrays([cols, [
                tableColumnDefinition("tx", "TX"),
                tableColumnDefinition("ty", "TY"),
                tableColumnDefinition("tz", "TZ"),
                tableColumnDefinition("nx", "NX"),
                tableColumnDefinition("ny", "NY"),
                tableColumnDefinition("nz", "NZ")
            ]]);
        }

        // Build rows
        var rows = [];
        for (var r in attr.rows)
        {
            rows = append(rows, tableRow(r));
        }

        return table("Curve Export", cols, rows);
    });
