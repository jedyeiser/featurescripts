FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3095.0");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: export_tag_icon.svg (feature icon)
IconNamespace::import(path : "f560c2fac5b751264e8c9ac3", version : "0f0021fcd5b08901d6d9ce6e");

// IMPORT: export_tag_icon.svg (feature icon) -- added once the icon tab exists

/** Attribute written on the tagged body (schema exportTag/1). */
export const EXPORT_TAG_ATTRIBUTE = "publishExportTag";
export const EXPORT_TAG_SCHEMA = "exportTag/1";

/** What to do when the stored name no longer matches the body's current name. */
export enum StaleNameAction
{
    annotation { "Name" : "Ignore" }
    IGNORE,
    annotation { "Name" : "Warn" }
    WARN,
    annotation { "Name" : "Error" }
    ERROR
}

/** Released-version prefix RULE; the app resolves the real revision at export time. */
export enum ExportVersionPrefix
{
    annotation { "Name" : "None" }
    NONE,
    annotation { "Name" : "V1, V2, ..." }
    V1,
    annotation { "Name" : "V001, V002, ..." }
    V001,
    annotation { "Name" : "R0, R1, ..." }
    R0,
    annotation { "Name" : "R1, R2, ..." }
    R1
}

/** Where a DXF's faces come from. */
export enum ExportDxfSource
{
    annotation { "Name" : "None" }
    NONE,
    annotation { "Name" : "Projection of the part" }
    PROJECTION,
    annotation { "Name" : "Selected faces" }
    SELECTION
}

/** How the export origin is defined. */
export enum ExportOriginType
{
    annotation { "Name" : "None (leave in place)" }
    NONE,
    annotation { "Name" : "Mate connector" }
    MATE_CONNECTOR,
    annotation { "Name" : "Insert into quadrant" }
    QUADRANT
}

/** A mate connector origin moves the connector's origin to the world origin; optionally its axes onto the world axes. */
export enum ExportOriginAxes
{
    annotation { "Name" : "Origin only (translation)" }
    ORIGIN_ONLY,
    annotation { "Name" : "Origin and axes" }
    ORIGIN_AND_AXES
}

/** Quadrant of the world XY plane the exported part is placed in (counter-clockwise from +X +Y). */
export enum ExportQuadrant
{
    annotation { "Name" : "1st (+X, +Y)" }
    FIRST,
    annotation { "Name" : "2nd (-X, +Y)" }
    SECOND,
    annotation { "Name" : "3rd (-X, -Y)" }
    THIRD,
    annotation { "Name" : "4th (+X, -Y)" }
    FOURTH
}

/** The face outlines are built 20 m wide; the part must lie inside. */
const DXF_PLANE_SIZE = 20 * meter;

/**
 * Export tag: tells the export app what to export for ONE body or ONE composite, and builds the geometry the
 * export needs.
 *
 *   - captures the body's name (Get name button, editing logic -- the feature body cannot read names) and
 *     warns when it goes stale;
 *   - solid formats (STEP / IGES / STL), a drawing flag and the naming rule are only RECORDED;
 *   - the export origin (a mate connector, or a quadrant with buffers) is solved as the minimum transform
 *     and recorded; optionally a shifted COPY of a single body is made (a composite input is taken as
 *     already in place);
 *   - DXF Plan (XY) / Profile (XZ): the part's projection, or selected faces, become surface bodies lying on
 *     the export frame's XY / XZ plane (the "transformed" plane when a shift is recorded), several bodies are
 *     grouped in a composite named for the view;
 *   - everything is written to the attribute publishExportTag (schema exportTag/1) on the tagged body and
 *     to the standard output slot (variable exportTag).
 */
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Export tag",
            "Feature Type Description" : "Records what to export for a body or composite (solid formats, DXF plan / profile, origin shift) and builds the DXF faces.",
            "Editing Logic Function" : "exportTagEditLogic" }
export const exportTag = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "Part and name", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Part", "Filter" : (EntityType.BODY && (BodyType.SOLID || BodyType.SHEET)) || BodyType.COMPOSITE, "MaxNumberOfPicks" : 1,
                        "Description" : "One body or one composite. A body inside a composite is exported as itself; tag the composite separately to export the composite." }
            definition.part is Query;

            annotation { "Name" : "Stored name", "Default" : "", "MaxLength" : 256, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            definition.bodyName is string;

            annotation { "Name" : "Name is stale", "Default" : false, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            definition.nameStale is boolean;

            annotation { "Name" : "Get Part Name", "Description" : "Copies the part's current name into the tag." }
            isButton(definition.getName);

            annotation { "Name" : "Export name", "Default" : "", "MaxLength" : 128,
                        "Description" : "Names the exports and the generated bodies. Empty = the stored part name." }
            definition.exportName is string;

            annotation { "Name" : "If the name changed", "Default" : StaleNameAction.WARN, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "The stored name is compared when this dialog is edited, not when the part is renamed elsewhere." }
            definition.staleAction is StaleNameAction;

            annotation { "Name" : "Release prefix", "Default" : ExportVersionPrefix.NONE, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "A rule only: the app adds the released version in this format at export time." }
            definition.versionPrefix is ExportVersionPrefix;
        }

        annotation { "Group Name" : "Export info", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Export solid", "Default" : false }
            definition.exportSolid is boolean;

            if (definition.exportSolid)
            {
                annotation { "Name" : "STEP", "Default" : false }
                definition.stepOut is boolean;

                annotation { "Name" : "IGES", "Default" : false }
                definition.igesOut is boolean;

                annotation { "Name" : "STL", "Default" : false }
                definition.stlOut is boolean;

                annotation { "Name" : "Solid file name", "Default" : "", "MaxLength" : 128,
                            "Description" : "Empty = the export name." }
                definition.solidName is string;
            }

            annotation { "Name" : "Export DXF", "Default" : false }
            definition.exportDxf is boolean;

            if (definition.exportDxf)
            {
                annotation { "Name" : "Plan DXF (XY)", "Default" : ExportDxfSource.NONE, "UIHint" : UIHint.SHOW_LABEL }
                definition.planSource is ExportDxfSource;

                if (definition.planSource == ExportDxfSource.SELECTION)
                {
                    annotation { "Name" : "Plan faces", "Filter" : EntityType.FACE,
                                "Description" : "Faces projected onto the XY plane." }
                    definition.planFaces is Query;
                }

                if (definition.planSource != ExportDxfSource.NONE)
                {
                    annotation { "Name" : "Plan file name", "Default" : "", "MaxLength" : 128, "Description" : "Empty = the app's default." }
                    definition.planName is string;
                }

                annotation { "Name" : "Profile DXF (XZ)", "Default" : ExportDxfSource.NONE, "UIHint" : UIHint.SHOW_LABEL }
                definition.profileSource is ExportDxfSource;

                if (definition.profileSource == ExportDxfSource.SELECTION)
                {
                    annotation { "Name" : "Profile faces", "Filter" : EntityType.FACE,
                                "Description" : "Faces projected onto the XZ plane." }
                    definition.profileFaces is Query;
                }

                if (definition.profileSource != ExportDxfSource.NONE)
                {
                    annotation { "Name" : "Profile file name", "Default" : "", "MaxLength" : 128, "Description" : "Empty = the app's default." }
                    definition.profileName is string;
                }

                if (definition.planSource != ExportDxfSource.NONE && definition.profileSource != ExportDxfSource.NONE)
                {
                    annotation { "Name" : "One composite for plan and profile", "Default" : false,
                                "Description" : "Off: one composite per view (when it has several faces)." }
                    definition.oneDxfComposite is boolean;
                }
            }

            annotation { "Name" : "Export drawing", "Default" : false,
                        "Description" : "Intent only: the part has a drawing to export with it." }
            definition.drawingOut is boolean;

            if (definition.drawingOut)
            {
                annotation { "Name" : "Drawing file name", "Default" : "", "MaxLength" : 128, "Description" : "Empty = the export name." }
                definition.drawingName is string;

                annotation { "Name" : "Station table in the drawing", "Default" : false,
                            "Description" : "The drawing shows the Station table of this part (Station geometry's views). Recorded in the tag with the view filter below." }
                definition.stationTable is boolean;

                if (definition.stationTable)
                {
                    annotation { "Name" : "Views containing", "Default" : "", "MaxLength" : 128,
                                "Description" : "The Station table's 'Views containing' filter. Empty = the export name (Station geometry's name prefix is the part name)." }
                    definition.stationViewFilter is string;
                }
            }
        }

        annotation { "Group Name" : "Export origin", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Origin", "Default" : ExportOriginType.NONE, "UIHint" : UIHint.SHOW_LABEL }
            definition.originType is ExportOriginType;

            if (definition.originType == ExportOriginType.MATE_CONNECTOR)
            {
                annotation { "Name" : "Mate connector or vertex", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1,
                            "Description" : "A mate connector (existing or created here) or a vertex. A vertex moves the origin only." }
                definition.originConnector is Query;

                annotation { "Name" : "Move", "Default" : ExportOriginAxes.ORIGIN_ONLY, "UIHint" : UIHint.SHOW_LABEL }
                definition.originAxes is ExportOriginAxes;
            }

            if (definition.originType == ExportOriginType.QUADRANT)
            {
                annotation { "Name" : "Quadrant", "Default" : ExportQuadrant.FIRST, "UIHint" : UIHint.SHOW_LABEL,
                            "Description" : "Translation only. The part's bounding box is placed in this quadrant of XY, the gap to each axis being the buffer." }
                definition.quadrant is ExportQuadrant;

                annotation { "Name" : "X buffer" }
                isLength(definition.bufferX, { (millimeter) : [0, 10, 100000] } as LengthBoundSpec);

                annotation { "Name" : "Y buffer" }
                isLength(definition.bufferY, { (millimeter) : [0, 10, 100000] } as LengthBoundSpec);

                annotation { "Name" : "Z buffer (lowest point above XY)" }
                isLength(definition.bufferZ, { (millimeter) : [-100000, 0, 100000] } as LengthBoundSpec);
            }

            if (definition.originType != ExportOriginType.NONE)
            {
                annotation { "Name" : "Make a shifted copy", "Default" : false,
                            "Description" : "Adds a copy of the body at the export position (a composite input is taken as already in place)." }
                definition.shiftCopy is boolean;
            }
        }
    }
    {
        if (isQueryEmpty(context, definition.part))
        {
            throw regenError("Select a body or a composite.", ["part"]);
        }
        if (size(evaluateQuery(context, definition.part)) != 1)
        {
            throw regenError("Select exactly one body or composite.", ["part"]);
        }

        const stem = exportStem(definition);
        if (stem == "")
        {
            throw regenError("Press Get Part Name, or enter an export name.", ["exportName"]);
        }
        const isComposite = !isQueryEmpty(context, qBodyType(definition.part, BodyType.COMPOSITE));

        var warnings = [];
        var notes = [];
        if (definition.nameStale && definition.staleAction == StaleNameAction.ERROR)
        {
            throw regenError("The part was renamed since the name was stored. Press Get Part Name.", ["part"]);
        }
        if (definition.nameStale && definition.staleAction == StaleNameAction.WARN)
        {
            warnings = append(warnings, "The part was renamed since the name was stored; Press Get Part Name.");
        }

        // Export origin: the minimum transform, recorded; applied only to a single body copy and to the DXF faces.
        var origin = exportOrigin(context, definition, isComposite);
        if (isComposite && definition.originType != ExportOriginType.NONE)
        {
            notes = append(notes, "A composite is taken as already in place: no shift.");
        }

        var created = [];
        var dxf = {};
        var dxfBodies = {};
        const planSource = definition.exportDxf ? definition.planSource : ExportDxfSource.NONE;
        const profileSource = definition.exportDxf ? definition.profileSource : ExportDxfSource.NONE;
        for (var view in [{ "key" : "plan", "label" : "PLAN", "source" : planSource, "faces" : definition.planFaces,
                    "name" : definition.planName, "normal" : vector(0, 0, 1), "xAxis" : vector(1, 0, 0) },
                { "key" : "profile", "label" : "PROFILE", "source" : profileSource, "faces" : definition.profileFaces,
                    "name" : definition.profileName, "normal" : vector(0, 1, 0), "xAxis" : vector(1, 0, 0) }])
        {
            if (view.source == ExportDxfSource.NONE)
            {
                continue;
            }
            const bodies = buildDxfFaces(context, id + view.key, definition.part, view, origin, stem);
            dxfBodies[view.key] = bodies;
            dxf[view.key] = { "source" : view.source == ExportDxfSource.PROJECTION ? "PROJECTION" : "SELECTION",
                "name" : view.name, "faceCount" : size(evaluateQuery(context, bodies)) };
        }

        // Composites: one per view when it has several faces, or one for both views.
        var composites = [];
        if (definition.oneDxfComposite == true)
        {
            const all = qUnion([viewBodies(dxfBodies, "plan"), viewBodies(dxfBodies, "profile")]);
            composites = append(composites, dxfComposite(context, id + "dxfAll", all, stem ~ " DXF"));
        }
        else
        {
            for (var key in keys(dxfBodies))
            {
                composites = append(composites, dxfComposite(context, id + ("dxf_" ~ key), dxfBodies[key], stem ~ " " ~ (key == "plan" ? "PLAN" : "PROFILE")));
            }
        }
        for (var key in keys(dxfBodies))
        {
            created = append(created, dxfBodies[key]);
        }

        var shiftedCopy = qNothing();
        if (definition.shiftCopy == true && origin.applied)
        {
            opPattern(context, id + "shiftCopy", {
                        "entities" : definition.part,
                        "transforms" : [origin.xf],
                        "instanceNames" : ["shifted"]
                    });
            shiftedCopy = qCreatedBy(id + "shiftCopy", EntityType.BODY);
            setProperty(context, { "entities" : shiftedCopy, "propertyType" : PropertyType.NAME, "value" : stem ~ " EXPORT" });
            created = append(created, shiftedCopy);
        }

        var solidFormats = [];
        if (definition.exportSolid && definition.stepOut)
        {
            solidFormats = append(solidFormats, "STEP");
        }
        if (definition.exportSolid && definition.igesOut)
        {
            solidFormats = append(solidFormats, "IGES");
        }
        if (definition.exportSolid && definition.stlOut)
        {
            solidFormats = append(solidFormats, "STL");
        }
        const stationTable = definition.drawingOut && definition.stationTable;

        const data = {
            "schema" : EXPORT_TAG_SCHEMA,
            "name" : { "stored" : definition.bodyName, "stem" : stem, "versionPrefix" : versionPrefixText(definition.versionPrefix) },
            "isComposite" : isComposite,
            "solid" : { "formats" : solidFormats, "name" : definition.solidName },
            "dxf" : dxf,
            "drawing" : { "export" : definition.drawingOut == true, "name" : definition.drawingName,
                "stationTable" : stationTable, "stationViewFilter" : stationTable ? (definition.stationViewFilter == "" ? stem : definition.stationViewFilter) : "" },
            "origin" : originData(definition, origin),
            "shiftedCopy" : size(evaluateQuery(context, shiftedCopy)) > 0
        };

        setAttribute(context, { "entities" : definition.part, "name" : EXPORT_TAG_ATTRIBUTE, "attribute" : data });

        embedStandardOutputs(context, id, {
                    "output" : qUnion(concatenateArrays([created, composites])),
                    "outputDescription" : "The DXF face bodies / composites and the shifted copy",
                    "inputs" : definition.part,
                    "variables" : {
                        "exportTag" : extractableVariable(data, "What to export for the tagged part (schema exportTag/1): name rule, solid formats, DXF plan / profile, drawing intent, origin transform in mm.")
                    },
                    "queries" : {
                        "exportPlan" : viewBodies(dxfBodies, "plan"),
                        "exportProfile" : viewBodies(dxfBodies, "profile")
                    }
                });

        var summary = "Tag recorded for '" ~ stem ~ "'.";
        if (size(notes) > 0)
        {
            summary = summary ~ " " ~ join(notes, " ");
        }
        if (size(warnings) > 0)
        {
            reportFeatureWarning(context, id, join(warnings, " ") ~ " " ~ summary);
        }
        else
        {
            reportFeatureInfo(context, id, summary);
        }
    }, { "staleAction" : StaleNameAction.WARN, "versionPrefix" : ExportVersionPrefix.NONE,
         "planSource" : ExportDxfSource.NONE, "profileSource" : ExportDxfSource.NONE,
         "originType" : ExportOriginType.NONE, "originAxes" : ExportOriginAxes.ORIGIN_ONLY,
         "quadrant" : ExportQuadrant.FIRST, "bufferX" : 10 * millimeter, "bufferY" : 10 * millimeter, "bufferZ" : 0 * millimeter,
         "bodyName" : "", "nameStale" : false, "exportName" : "", "solidName" : "", "planName" : "", "profileName" : "",
         "drawingName" : "", "oneDxfComposite" : false, "shiftCopy" : false,
         "exportSolid" : false, "exportDxf" : false, "stationTable" : false, "stationViewFilter" : "",
         "stepOut" : false, "igesOut" : false, "stlOut" : false, "drawingOut" : false,
         "planFaces" : qNothing(), "profileFaces" : qNothing(), "originConnector" : qNothing() });

/**
 * Stores the part's name when the part is picked or Get name is pressed, and flags a stale name.
 * (The body cannot read names: getProperty throws during regeneration, correction 36.)
 */
export function exportTagEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    if (isQueryEmpty(context, definition.part))
    {
        return definition;
    }
    const current = getProperty(context, { "entity" : definition.part, "propertyType" : PropertyType.NAME });
    if (!(current is string))
    {
        return definition;
    }
    if (clickedButton == "getName" || definition.bodyName == "")
    {
        definition.bodyName = current;
        definition.nameStale = false;
    }
    else
    {
        definition.nameStale = definition.bodyName != current;
    }
    return definition;
}

/** A view's DXF bodies, or nothing when that view has none. */
function viewBodies(bodies is map, key is string) returns Query
{
    return bodies[key] == undefined ? qNothing() : bodies[key];
}

/** The name used for exports and generated bodies. */
function exportStem(definition is map) returns string
{
    if (definition.exportName != "")
    {
        return definition.exportName;
    }
    return definition.bodyName;
}

function versionPrefixText(prefix is ExportVersionPrefix) returns string
{
    if (prefix == ExportVersionPrefix.V1)
    {
        return "V1";
    }
    if (prefix == ExportVersionPrefix.V001)
    {
        return "V001";
    }
    if (prefix == ExportVersionPrefix.R0)
    {
        return "R0";
    }
    if (prefix == ExportVersionPrefix.R1)
    {
        return "R1";
    }
    return "";
}

/** The members measured and projected: solids and sheets, composites flattened. */
function memberQuery(part is Query) returns Query
{
    return qUnion([qBodyType(part, [BodyType.SOLID, BodyType.SHEET]), qFlattenedCompositeParts(part)]);
}

/**
 * The minimum transform to the export origin: { applied, xf, kind, frame } where xf moves the part to the export
 * position. A composite is taken as already in place (not applied).
 */
function exportOrigin(context is Context, definition is map, isComposite is boolean) returns map
{
    var result = { "applied" : false, "xf" : identityTransform(), "kind" : "NONE" };
    if (definition.originType == ExportOriginType.NONE || isComposite)
    {
        return result;
    }

    if (definition.originType == ExportOriginType.MATE_CONNECTOR)
    {
        if (isQueryEmpty(context, definition.originConnector))
        {
            throw regenError("Pick the origin mate connector.", ["originConnector"]);
        }
        // An MC pick may arrive as its vertex (correction 44); a real vertex gives the origin only.
        const connectors = evaluateQuery(context, qBodyType(qOwnerBody(definition.originConnector), BodyType.MATE_CONNECTOR));
        if (size(connectors) == 0)
        {
            result.kind = "VERTEX";
            result.xf = transform(-evVertexPoint(context, { "vertex" : definition.originConnector }));
        }
        else
        {
            const cs = evMateConnector(context, { "mateConnector" : connectors[0] });
            result.kind = "MATE_CONNECTOR";
            result.xf = definition.originAxes == ExportOriginAxes.ORIGIN_AND_AXES ? fromWorld(cs) : transform(-cs.origin);
        }
    }
    else
    {
        const bb = evBox3d(context, { "topology" : memberQuery(definition.part), "tight" : true });
        const lo = bb.minCorner;
        const hi = bb.maxCorner;
        var shiftX = definition.bufferX - lo[0];
        var shiftY = definition.bufferY - lo[1];
        if (definition.quadrant == ExportQuadrant.SECOND || definition.quadrant == ExportQuadrant.THIRD)
        {
            shiftX = -definition.bufferX - hi[0];
        }
        if (definition.quadrant == ExportQuadrant.THIRD || definition.quadrant == ExportQuadrant.FOURTH)
        {
            shiftY = -definition.bufferY - hi[1];
        }
        result.kind = "QUADRANT";
        result.xf = transform(vector(shiftX, shiftY, definition.bufferZ - lo[2]));
    }
    result.applied = true;
    return result;
}

/** Plain numbers for the attribute: the matrix (3x3, unitless), translation (mm) and the world axes after the move. */
function originData(definition is map, origin is map) returns map
{
    const lin = origin.xf.linear;
    const t = origin.xf.translation / millimeter;
    return {
        "kind" : origin.kind,
        "applied" : origin.applied,
        "matrix" : [[lin[0][0], lin[0][1], lin[0][2]], [lin[1][0], lin[1][1], lin[1][2]], [lin[2][0], lin[2][1], lin[2][2]]],
        "translationMm" : [t[0], t[1], t[2]],
        "quadrant" : definition.originType == ExportOriginType.QUADRANT ? toString(definition.quadrant) : "",
        "bufferMm" : definition.originType == ExportOriginType.QUADRANT
            ? [definition.bufferX / millimeter, definition.bufferY / millimeter, definition.bufferZ / millimeter] : []
    };
}

/**
 * Surface bodies lying on the export frame's plane for one view (plan XY / profile XZ): the faces' outline is
 * built on the plane that the recorded transform carries onto the world plane, then moved by it, so the faces
 * land on the world plane at the export position.
 * Returns a Query of the (named) bodies.
 */
function buildDxfFaces(context is Context, vid is Id, part is Query, view is map, origin is map, stem is string) returns Query
{
    var tools = qNothing();
    if (view.source == ExportDxfSource.PROJECTION)
    {
        tools = qBodyType(memberQuery(part), BodyType.SOLID);
        if (isQueryEmpty(context, tools))
        {
            throw regenError(view.label ~ " projection needs a solid. For a surface, select its faces.", [view.key ~ "Source"]);
        }
    }
    else
    {
        tools = view.faces;
        if (isQueryEmpty(context, tools))
        {
            throw regenError("Select the faces for the " ~ view.label ~ " DXF.", [view.key ~ "Faces"]);
        }
    }

    const worldPlane = plane(vector(0, 0, 0) * meter, view.normal, view.xAxis);
    const targetPlane = origin.applied ? inverse(origin.xf) * worldPlane : worldPlane;
    opPlane(context, vid + "target", { "plane" : targetPlane, "width" : DXF_PLANE_SIZE, "height" : DXF_PLANE_SIZE });
    const target = qCreatedBy(vid + "target", EntityType.FACE);
    opCreateOutline(context, vid + "outline", { "tools" : tools, "target" : target });
    opDeleteBodies(context, vid + "deleteTarget", { "entities" : qCreatedBy(vid + "target", EntityType.BODY) });

    // Overlapping regions (several faces) are merged so no internal lines remain.
    var regions = qCreatedBy(vid + "outline", EntityType.BODY);
    if (size(evaluateQuery(context, regions)) > 1)
    {
        opBoolean(context, vid + "unite", { "tools" : regions, "operationType" : BooleanOperationType.UNION });
        regions = qUnion([regions]);
    }
    if (origin.applied)
    {
        opTransform(context, vid + "move", { "bodies" : regions, "transform" : origin.xf });
    }

    const bodies = evaluateQuery(context, qBodyType(regions, BodyType.SHEET));
    for (var i = 0; i < size(bodies); i += 1)
    {
        setProperty(context, { "entities" : bodies[i], "propertyType" : PropertyType.NAME,
                    "value" : size(bodies) == 1 ? stem ~ " " ~ view.label : stem ~ " " ~ view.label ~ " " ~ (i + 1) });
    }
    return qUnion(bodies);
}

/** Groups several surface bodies in a closed composite named for the view, excluded from the BOM. A single body is left alone. */
function dxfComposite(context is Context, cid is Id, bodies is Query, name is string) returns Query
{
    if (size(evaluateQuery(context, bodies)) < 2)
    {
        return qNothing();
    }
    opCreateCompositePart(context, cid, { "bodies" : bodies, "closed" : true });
    const composite = qBodyType(qCreatedBy(cid, EntityType.BODY), BodyType.COMPOSITE);
    setProperty(context, { "entities" : composite, "propertyType" : PropertyType.NAME, "value" : name });
    setProperty(context, { "entities" : composite, "propertyType" : PropertyType.EXCLUDE_FROM_BOM, "value" : true });
    return composite;
}
