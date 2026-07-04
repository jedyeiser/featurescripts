FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");

//import qcTable_types
export import(path : "ff9221b7148cfda8a449abff", version : "64db5cfaa75066e3080889bb");

//import qcTable_stations
import(path : "f78f146e807209053299e5a5", version : "49620face679235389eb77e0");

//import qcTable_geometry
import(path : "0f9cf9b21a3c654880d3167c", version : "87e72a91e63f7e10a07d75c7");

// A simple feature that takes up to 10 solid bodies as input along with FCP/ACP references
// User specifies the number of evenly spaced 'sections' to 'measure' between FCP and ACP
// For each part, create a sketch (name the sketch the same as the part name + " QC SKETCH")
// Place vertical construction lines - spaced appropriately - that span the width (y) or height (z) of the part as indicated by the user
// This should be controlled in an editing logic driven array parameter. Users can override sketch names and specify if we should be measuring WIDTH or HEIGHT
// in an enum for each part. WIDTH sketches default to the top plane (can be overriden). HEIGHT sketches default to the Front plane, but can be overriden.
// in the event that a part does not intersect the full span, ignore data with no intersection.

/**
 * Part QC Drawing Sketch Feature
 *
 * For each selected part, builds a construction sketch of the part's measured
 * WIDTH (world Y) or HEIGHT (world Z) at a set of stations evenly spaced from
 * FCP to ACP (inclusive). Each station contributes one construction line
 * spanning the measured extent. Stations where the part has no cross-section
 * are skipped.
 */

// Maximum number of part rows the feature supports.
export const MAX_PARTS = 10;

// ============================================================================
// ARRAY ITEM SPECIFICATION
// ============================================================================

/**
 * One part row: the body to measure, whether to measure width or height, the
 * sketch name, and an optional measurement-plane override.
 */
predicate isPartSpec(part is map)
{
    annotation { "Name" : "Body", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
    part.body is Query;

    annotation { "Name" : "Measure", "UIHint" : UIHint.SHOW_LABEL, "Default" : MEASURE_AXIS.WIDTH }
    part.axis is MEASURE_AXIS;

    annotation { "Name" : "Sketch name" }
    part.sketchName is string;

    annotation { "Name" : "Override measurement plane", "Default" : false }
    part.overridePlane is boolean;

    if (part.overridePlane)
    {
        annotation { "Name" : "Plane", "Filter" : GeometryType.PLANE || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        part.planeRef is Query;
    }
}

// ============================================================================
// EDITING LOGIC
// ============================================================================

export function partSketchElFunction(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    // Default each part's sketch name to "<part name> QC SKETCH" while the user
    // has left it blank. Once the user types a name it is left untouched.
    for (var i = 0; i < size(definition.parts); i += 1)
    {
        var part = definition.parts[i];

        var nameBlank = (part.sketchName == undefined || part.sketchName == "");
        if (nameBlank && !isQueryEmpty(context, part.body))
        {
            var partName = getProperty(context, {
                "entity" : part.body,
                "propertyType" : PropertyType.NAME
            });
            definition.parts[i].sketchName = partName ~ " QC SKETCH";
        }
    }

    return definition;
}

// ============================================================================
// FEATURE DEFINITION
// ============================================================================

annotation { "Feature Type Name" : "Part QC Sketch", "Editing Logic Function" : "partSketchElFunction", "Description" : "Builds a construction sketch of each part's width (Y) or height (Z) at evenly spaced stations between FCP and ACP." }
export const generatePartQCSketch = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // ===== References =====
        annotation { "Group Name" : "References", "Collapsed By Default" : false }
        {
            annotation { "Name" : "FCP Reference", "Filter" : EntityType.VERTEX || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.fcpReference is Query;

            annotation { "Name" : "ACP Reference", "Filter" : EntityType.VERTEX || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.acpReference is Query;

            annotation { "Name" : "Number of Sections (incl. FCP & ACP)" }
            isInteger(definition.numSections, sectionCountBounds);
        }

        // ===== Parts =====
        annotation { "Group Name" : "Parts", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Parts", "Item name" : "part", "Item label template" : "#sketchName", "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
            definition.parts is array;
            for (var part in definition.parts)
            {
                isPartSpec(part);
            }
        }
    }
    {
        // ===================================================================
        // VALIDATION
        // ===================================================================

        if (size(definition.parts) == 0)
        {
            throw "Add at least one part";
        }

        if (size(definition.parts) > MAX_PARTS)
        {
            throw "This feature supports at most " ~ MAX_PARTS ~ " parts";
        }

        // ===================================================================
        // STATIONS (FCP..ACP inclusive, N evenly spaced)
        // ===================================================================

        var boundaries = extractFCPACP(context, definition.fcpReference, definition.acpReference);

        var n = definition.numSections;
        var span = boundaries.acp - boundaries.fcp;

        var stationXs = [];
        for (var i = 0; i < n; i += 1)
        {
            stationXs = append(stationXs, boundaries.fcp + (span * i) / (n - 1));
        }

        // ===================================================================
        // PER-PART SKETCHES
        // ===================================================================

        for (var p = 0; p < size(definition.parts); p += 1)
        {
            var part = definition.parts[p];

            // Skip empty rows rather than erroring on a partially filled array.
            if (isQueryEmpty(context, part.body))
            {
                continue;
            }

            buildPartQCSketch(context, id + ("part" ~ p), part, stationXs);
        }
    });

// ============================================================================
// SKETCH CONSTRUCTION
// ============================================================================

/**
 * Build one part's QC sketch: a construction line at each station spanning the
 * measured extent. WIDTH draws on a Top-oriented plane (line spans world Y);
 * HEIGHT draws on a Front-oriented plane (line spans world Z). An override
 * plane only shifts the projection offset; the measured span stays world Y/Z.
 */
function buildPartQCSketch(context is Context, id is Id, part is map, stationXs is array)
{
    var isWidth = (part.axis == MEASURE_AXIS.WIDTH);

    // Projection offset from an optional override plane. WIDTH projects onto a
    // constant-Z plane, HEIGHT onto a constant-Y plane; default offset is 0.
    var offset = 0 * meter;
    if (part.overridePlane && !isQueryEmpty(context, part.planeRef))
    {
        var refOrigin = planeRefOrigin(context, part.planeRef);
        offset = isWidth ? refOrigin[2] : refOrigin[1];
    }

    // Plane axes chosen so a 2D point (x, v) maps to (x, v, offset) for WIDTH
    // and (x, offset, v) for HEIGHT - i.e. v is exactly the world Y or Z value.
    var originPt = isWidth ? vector(0 * meter, 0 * meter, offset) : vector(0 * meter, offset, 0 * meter);
    var normal = isWidth ? vector(0, 0, 1) : vector(0, -1, 0);
    var sketchPlane = plane(originPt, normal, vector(1, 0, 0));

    var sk = newSketchOnPlane(context, id + "sketch", {
        "sketchPlane" : sketchPlane
    });

    var drawn = 0;
    for (var i = 0; i < size(stationXs); i += 1)
    {
        var x = stationXs[i];
        var m = measureSectionExtents(context, part.body, x);

        // No cross-section at this station: ignore (part does not span here).
        if (m == undefined)
        {
            continue;
        }

        var vLo = isWidth ? m.yMin : m.zMin;
        var vHi = isWidth ? m.yMax : m.zMax;

        skLineSegment(sk, "line" ~ i, {
            "start" : vector(x, vLo),
            "end" : vector(x, vHi),
            "construction" : true
        });

        drawn += 1;
    }

    skSolve(sk);

    // Name the resulting sketch geometry when we drew anything.
    if (drawn > 0 && part.sketchName != undefined && part.sketchName != "")
    {
        var sketchBodies = qCreatedBy(id + "sketch", EntityType.BODY);
        if (!isQueryEmpty(context, sketchBodies))
        {
            setProperty(context, {
                "entities" : sketchBodies,
                "propertyType" : PropertyType.NAME,
                "value" : part.sketchName
            });
        }
    }
}

/**
 * Origin of a plane/planar-face/mate-connector reference.
 */
function planeRefOrigin(context is Context, query is Query) returns Vector
{
    if (size(evaluateQuery(context, qBodyType(query, BodyType.MATE_CONNECTOR))) > 0)
    {
        return evMateConnector(context, {"mateConnector" : query}).origin;
    }

    if (size(evaluateQuery(context, qEntityFilter(query, EntityType.FACE))) > 0)
    {
        return evPlane(context, {"face" : query}).origin;
    }

    // Fallback: a construction-plane body owns a single planar face.
    return evPlane(context, {"face" : qOwnedByBody(query, EntityType.FACE)}).origin;
}
