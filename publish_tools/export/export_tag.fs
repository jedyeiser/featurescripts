FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3095.0");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: export_tag_icon.svg (feature icon)
IconNamespace::import(path : "f560c2fac5b751264e8c9ac3", version : "0f0021fcd5b08901d6d9ce6e");

// IMPORT: station_geometry.fs (same document, pinned by microversion; re-pin when Station geometry changes)
export import(path : "5d3299cf520723444c27b273", version : "57af218d37a6faa4b493eced");

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

/** How the DXF face pattern is spaced. */
export enum ExportPatternSpacing
{
    annotation { "Name" : "Minimum clearance between faces" }
    CLEARANCE,
    annotation { "Name" : "Pattern distance (pitch)" }
    DISTANCE
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

                if (definition.planSource != ExportDxfSource.NONE || definition.profileSource != ExportDxfSource.NONE)
                {
                    annotation { "Name" : "Pattern the DXF faces", "Default" : false,
                                "Description" : "Creates the patterned copies as real surface bodies in this Part Studio (the DXF faces and their composite), starting at the export origin, so one DXF holds several copies. Only for a single part (a composite is taken as already patterned and gets a warning). Plan: X and Y. Profile: X and Z." }
                    definition.dxfPattern is boolean;

                    if (definition.dxfPattern)
                    {
                        annotation { "Name" : "Spacing", "Default" : ExportPatternSpacing.CLEARANCE, "UIHint" : UIHint.SHOW_LABEL }
                        definition.patternSpacing is ExportPatternSpacing;

                        annotation { "Name" : "Instances in X" }
                        isInteger(definition.patternCountX, { (unitless) : [1, 1, 500] } as IntegerBoundSpec);

                        annotation { "Name" : "Instances in Y" }
                        isInteger(definition.patternCountY, { (unitless) : [1, 1, 500] } as IntegerBoundSpec);

                        // One id per branch (correction 46a), so each label says what the value is.
                        if (definition.patternSpacing == ExportPatternSpacing.CLEARANCE)
                        {
                            annotation { "Name" : "Clearance in X", "Description" : "Gap between neighbouring faces along X." }
                            isLength(definition.clearanceX, { (millimeter) : [0, 10, 100000] } as LengthBoundSpec);

                            annotation { "Name" : "Clearance in Y", "Description" : "Gap between neighbouring faces along Y (Z in the XZ view)." }
                            isLength(definition.clearanceY, { (millimeter) : [0, 10, 100000] } as LengthBoundSpec);
                        }
                        else
                        {
                            annotation { "Name" : "Pattern distance in X", "Description" : "Pitch: start-to-start distance along X. Must not be smaller than the faces' width." }
                            isLength(definition.pitchX, { (millimeter) : [0, 300, 100000] } as LengthBoundSpec);

                            annotation { "Name" : "Pattern distance in Y", "Description" : "Pitch along Y (Z in the XZ view). Must not be smaller than the faces' height." }
                            isLength(definition.pitchY, { (millimeter) : [0, 300, 100000] } as LengthBoundSpec);
                        }
                    }
                }
            }

        }

        // The group's own checkbox is "Export drawing": the part has a drawing to export with it. Everything below
        // (file name, Station table) applies only when it is on.
        annotation { "Group Name" : "Drawing Information", "Driving Parameter" : "drawingOut", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Export drawing", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN,
                        "Description" : "Intent only: the part has a drawing to export with it." }
            definition.drawingOut is boolean;

            annotation { "Name" : "Drawing file name", "Default" : "", "MaxLength" : 128, "Description" : "Empty = the export name." }
            definition.drawingName is string;

            annotation { "Name" : "Station table", "Default" : false,
                        "Description" : "Runs Station geometry for this part, named with the export name, so the drawing's Station table (Views containing = the export name) has its data." }
            definition.stationTable is boolean;

            if (definition.stationTable)
            {
                annotation { "Name" : "Station definitions",
                            "Description" : "The Station definition feature(s) whose stations this part is measured at." }
                definition.stationDefinitions is FeatureList;

                annotation { "Name" : "Datum (measuring origin)", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1,
                            "Description" : "Where x = 0. Empty = world origin." }
                definition.stationDatum is Query;

                annotation { "Name" : "Datum axes", "Default" : StationDatumUse.ORIGIN, "UIHint" : UIHint.SHOW_LABEL }
                definition.stationDatumUses is StationDatumUse;

                annotation { "Name" : "Plan view (datum XY)", "Default" : true }
                definition.stationPlan is boolean;

                annotation { "Name" : "Profile view (datum XZ)", "Default" : false }
                definition.stationProfile is boolean;

                annotation { "Name" : "Build the views at the export position", "Default" : false,
                            "Description" : "Off: the views are built on the picked part where it is (no new part). On: they are built on a shifted copy of the part at the export origin (a new part; single bodies only). Needs an export origin." }
                definition.stationAtExport is boolean;
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
            warnings = append(warnings, "The part was renamed since the name was stored; press Get Part Name.");
        }

        // Export origin: the minimum transform, recorded; applied only to a single body copy and to the DXF faces.
        var origin = exportOrigin(context, definition, isComposite);
        // A composite cannot be copied to the export position: its DXF faces are shifted, a copy / station views on a copy are not.
        const wantsCopy = definition.shiftCopy == true
            || (definition.drawingOut == true && definition.stationTable == true && definition.stationAtExport == true);
        if (isComposite && origin.applied && wantsCopy)
        {
            warnings = append(warnings, "A composite cannot be copied to the export position: the DXF faces are shifted, but the shifted copy and the station views at the export position were NOT built.");
        }
        const copyHere = origin.applied && !isComposite && wantsCopy;
        const stationsOnCopy = origin.applied && !isComposite && definition.drawingOut == true && definition.stationTable == true
            && definition.stationAtExport == true;

        var created = [];
        var dxf = {};
        var dxfBodies = {};
        const planSource = definition.exportDxf ? definition.planSource : ExportDxfSource.NONE;
        const profileSource = definition.exportDxf ? definition.profileSource : ExportDxfSource.NONE;
        // A pattern needs a single part; a composite is taken as already patterned.
        const patternWanted = definition.exportDxf && definition.dxfPattern == true
            && (definition.patternCountX > 1 || definition.patternCountY > 1);
        const pattern = { "applied" : patternWanted && !isComposite, "mode" : definition.patternSpacing,
            "countX" : definition.patternCountX, "countY" : definition.patternCountY,
            "spacingX" : definition.patternSpacing == ExportPatternSpacing.CLEARANCE ? definition.clearanceX : definition.pitchX,
            "spacingY" : definition.patternSpacing == ExportPatternSpacing.CLEARANCE ? definition.clearanceY : definition.pitchY };
        if (patternWanted && isComposite)
        {
            // The requested output is missing, so this is a warning (the feature would otherwise look fine).
            warnings = append(warnings, "A composite is taken as already patterned: the DXF pattern was NOT created.");
        }
        for (var view in [{ "key" : "plan", "label" : "XY", "source" : planSource, "faces" : definition.planFaces,
                    "name" : definition.planName, "normal" : vector(0, 0, 1), "xAxis" : vector(1, 0, 0),
                    "secondAxis" : vector(0, 1, 0), "secondIndex" : 1 },
                { "key" : "profile", "label" : "XZ", "source" : profileSource, "faces" : definition.profileFaces,
                    "name" : definition.profileName, "normal" : vector(0, 1, 0), "xAxis" : vector(1, 0, 0),
                    "secondAxis" : vector(0, 0, 1), "secondIndex" : 2 }])
        {
            if (view.source == ExportDxfSource.NONE)
            {
                continue;
            }
            const built = buildDxfFaces(context, id + view.key, definition.part, view, origin, stem, pattern);
            dxfBodies[view.key] = built.bodies;
            dxf[view.key] = { "source" : view.source == ExportDxfSource.PROJECTION ? "PROJECTION" : "SELECTION",
                "name" : view.name, "faceCount" : size(evaluateQuery(context, built.bodies)), "pattern" : built.pattern,
                "composite" : definition.oneDxfComposite == true ? stem ~ " DXF" : stem ~ " " ~ view.label ~ " DXF" };
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
                composites = append(composites, dxfComposite(context, id + ("dxf_" ~ key), dxfBodies[key], stem ~ " " ~ (key == "plan" ? "XY" : "XZ") ~ " DXF"));
            }
        }
        for (var key in keys(dxfBodies))
        {
            created = append(created, dxfBodies[key]);
        }

        var shiftedCopy = qNothing();
        // Made on request, or for the station views at the export position (born there, nothing left behind in place).
        if (copyHere)
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

        // Station table: run Station geometry for this part, named with the stem, so the drawing's Station table
        // (Views containing = the stem) has its data. The views are tagged composites "<stem> XY" / "<stem> XZ".
        // The Station table belongs to the drawing: it only runs when Export drawing is on.
        const stationTable = definition.drawingOut == true && definition.stationTable == true;
        var layout = {};
        if (stationTable)
        {
            if (!definition.stationPlan && !definition.stationProfile)
            {
                throw regenError("Turn on the plan or the profile view for the Station table.", ["stationPlan"]);
            }
            // With a shift the views are built on the shifted copy, and the datum is moved with it (a mate connector at the
            // transformed datum frame, deleted again after the run).
            var stationPart = definition.part;
            var stationDatum = definition.stationDatum;
            var stationFrame = qNothing();
            if (stationsOnCopy)
            {
                stationPart = shiftedCopy;
                var datumCs = coordSystem(vector(0, 0, 0) * meter, vector(1, 0, 0), vector(0, 0, 1));
                if (!isQueryEmpty(context, definition.stationDatum))
                {
                    const datumConnectors = evaluateQuery(context, qBodyType(qOwnerBody(definition.stationDatum), BodyType.MATE_CONNECTOR));
                    if (size(datumConnectors) > 0)
                    {
                        datumCs = evMateConnector(context, { "mateConnector" : datumConnectors[0] });
                    }
                    else
                    {
                        datumCs = coordSystem(evVertexPoint(context, { "vertex" : definition.stationDatum }), vector(1, 0, 0), vector(0, 0, 1));
                    }
                }
                opMateConnector(context, id + "stationDatum", {
                            "coordSystem" : coordSystem(origin.xf * datumCs.origin, origin.xf.linear * datumCs.xAxis, origin.xf.linear * datumCs.zAxis)
                        });
                stationDatum = qCreatedBy(id + "stationDatum", EntityType.BODY);
                // The Station definition's stations are absolute positions: Station geometry moves them by the image of
                // the world frame under the shift (hidden "Station frame").
                opMateConnector(context, id + "stationFrame", {
                            "coordSystem" : coordSystem(origin.xf * (vector(0, 0, 0) * meter), origin.xf.linear * vector(1, 0, 0), origin.xf.linear * vector(0, 0, 1))
                        });
                stationFrame = qCreatedBy(id + "stationFrame", EntityType.BODY);
            }
            stationGeometry(context, id + "stations", {
                        "part" : stationPart,
                        "datum" : stationDatum,
                        "stationFrame" : stationFrame,
                        "datumUses" : definition.stationDatumUses,
                        "prefix" : stem,
                        "viewNames" : StationViewNames.XY_XZ,
                        "planView" : definition.stationPlan,
                        "profileView" : definition.stationProfile,
                        "profileOutline" : StationProfileOutline.SECTION,
                        "otherViews" : [],
                        "stationDefinitions" : definition.stationDefinitions,
                        "stationSet" : "",
                        "stations" : [],
                        "stationLines" : true,
                        "outlineWires" : false,
                        "outlineSurface" : false,
                        "datumPoint" : true,
                        "flatCopy" : false,
                        "printTable" : false
                    });
            if (stationsOnCopy)
            {
                opDeleteBodies(context, id + "deleteStationDatum", { "entities" : qUnion([stationDatum, stationFrame]) });
            }
            notes = append(notes, "Station table: insert it in the drawing with Views containing = '" ~ stem ~ "'."
                ~ (stationsOnCopy ? " The views are built on the shifted copy." : ""));

            // Drawing layout for the API tool: what to put in each view, by body name (station ids are the body names, so
            // adding a station never re-binds another one). Ordinate dimensions run from the datum point along x; the
            // span (lo / hi) is the width or thickness between a station line's ends. Rows come from the view
            // composite's Station table attribute.
            const sid = id + "stations";
            for (var v in [{ "key" : "plan", "name" : "XY", "on" : definition.stationPlan },
                    { "key" : "profile", "name" : "XZ", "on" : definition.stationProfile }])
            {
                if (!v.on)
                {
                    continue;
                }
                const prefix = stem ~ " " ~ v.name;
                const viewComposite = qBodyType(qCreatedBy(sid + v.key + "composite", EntityType.BODY), BodyType.COMPOSITE);
                var rows = [];
                if (!isQueryEmpty(context, viewComposite))
                {
                    const attr = getAttribute(context, { "entity" : viewComposite, "name" : STATION_TABLE_ATTRIBUTE });
                    if (attr is map && attr.rows is array)
                    {
                        for (var r in attr.rows)
                        {
                            if (r.extent != undefined)
                            {
                                rows = append(rows, { "id" : r.id, "kind" : "EXTENT", "body" : prefix ~ " " ~ r.id, "x" : r.x });
                            }
                            else if (r.hit)
                            {
                                rows = append(rows, { "id" : r.id, "kind" : "STATION", "body" : prefix ~ " ST " ~ r.id,
                                            "x" : r.x, "lo" : r.lo, "hi" : r.hi, "span" : r.span });
                            }
                        }
                    }
                }

                layout[v.name] = { "composite" : prefix, "wires" : prefix ~ " WIRES", "datum" : prefix ~ " DATUM",
                    "builtOnShiftedCopy" : stationsOnCopy, "includeWires" : true, "rows" : rows };
            }
        }

        const data = {
            "schema" : EXPORT_TAG_SCHEMA,
            "name" : { "stored" : definition.bodyName, "stem" : stem, "versionPrefix" : versionPrefixText(definition.versionPrefix) },
            "isComposite" : isComposite,
            "solid" : { "formats" : solidFormats, "name" : definition.solidName },
            "dxf" : dxf,
            "drawing" : { "export" : definition.drawingOut == true, "name" : definition.drawingName,
                "stationTable" : stationTable, "stationViewFilter" : stationTable ? stem : "", "layout" : layout,
                "stationViews" : stationTable ? concatenateArrays([definition.stationPlan ? ["XY"] : [], definition.stationProfile ? ["XZ"] : []]) : [] },
            "origin" : originData(definition, origin),
            "shiftedCopy" : size(evaluateQuery(context, shiftedCopy)) > 0
        };

        if (!isQueryEmpty(context, qHasAttribute(definition.part, EXPORT_TAG_ATTRIBUTE)))
        {
            warnings = append(warnings, "Another Export tag already tags this part; this tag overwrites it.");
        }
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
        for (var key in keys(dxf))
        {
            const faces = dxf[key].faceCount;
            const pat = dxf[key].pattern;
            summary = summary ~ " " ~ (key == "plan" ? "XY" : "XZ") ~ " DXF: " ~ faces ~ " surface bod" ~ (faces == 1 ? "y" : "ies")
                ~ (pat.applied ? " (pattern " ~ pat.countX ~ " x " ~ pat.countY ~ ", pitch " ~ roundMm(pat.pitchXMm * millimeter) ~ " x " ~ roundMm(pat.pitchYMm * millimeter) ~ " mm)" : "") ~ ".";
        }
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
         "dxfPattern" : false, "patternSpacing" : ExportPatternSpacing.CLEARANCE, "patternCountX" : 1, "patternCountY" : 1,
         "clearanceX" : 10 * millimeter, "clearanceY" : 10 * millimeter, "pitchX" : 300 * millimeter, "pitchY" : 300 * millimeter,
         "exportSolid" : false, "exportDxf" : false, "stationTable" : false, "stationPlan" : true, "stationProfile" : false,
         "stationDatum" : qNothing(), "stationDatumUses" : StationDatumUse.ORIGIN, "stationAtExport" : false,
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
 * The minimum transform to the export origin: { applied, xf, kind } where xf moves the part to the export position.
 * It always shifts the DXF faces; a copy of the part (and station views built on it) is made for a single body only.
 */
function exportOrigin(context is Context, definition is map, isComposite is boolean) returns map
{
    var result = { "applied" : false, "xf" : identityTransform(), "kind" : "NONE" };
    if (definition.originType == ExportOriginType.NONE)
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
function buildDxfFaces(context is Context, vid is Id, part is Query, view is map, origin is map, stem is string, pattern is map) returns map
{
    const worldPlane = plane(vector(0, 0, 0) * meter, view.normal, view.xAxis);
    const targetPlane = origin.applied ? inverse(origin.xf) * worldPlane : worldPlane;

    // What is outlined and what already lies flat. A solid is outlined directly. A selected face is copied as a sheet
    // (opCreateOutline refuses faces: INVALID_INPUT, 2026-10-29); a planar face parallel to the plane is its own outline
    // and is only lifted onto the plane, any other face is outlined as a sheet.
    var outlineTools = [];
    var flatSheets = [];
    if (view.source == ExportDxfSource.PROJECTION)
    {
        const solids = qBodyType(memberQuery(part), BodyType.SOLID);
        if (isQueryEmpty(context, solids))
        {
            throw regenError(view.label ~ " projection needs a solid. For a surface, select its faces.", [view.key ~ "Source"]);
        }
        outlineTools = [solids];
    }
    else
    {
        const faces = evaluateQuery(context, view.faces);
        if (size(faces) == 0)
        {
            throw regenError("Select the faces for the " ~ view.label ~ " DXF.", [view.key ~ "Faces"]);
        }
        for (var i = 0; i < size(faces); i += 1)
        {
            const surface = evSurfaceDefinition(context, { "face" : faces[i] });
            const extractId = vid + ("face" ~ i);
            opExtractSurface(context, extractId, { "faces" : faces[i] });
            const sheet = qCreatedBy(extractId, EntityType.BODY);
            if (surface is Plane && abs(abs(dot(surface.normal, targetPlane.normal)) - 1) < 1e-9)
            {
                const lift = targetPlane.normal * dot(targetPlane.origin - surface.origin, targetPlane.normal);
                if (norm(lift) > TOLERANCE.zeroLength * meter)
                {
                    opTransform(context, vid + ("lift" ~ i), { "bodies" : sheet, "transform" : transform(lift) });
                }
                flatSheets = append(flatSheets, sheet);
            }
            else
            {
                outlineTools = append(outlineTools, sheet);
            }
        }
    }

    var regionList = flatSheets;
    if (size(outlineTools) > 0)
    {
        opPlane(context, vid + "target", { "plane" : targetPlane, "width" : DXF_PLANE_SIZE, "height" : DXF_PLANE_SIZE });
        const target = qCreatedBy(vid + "target", EntityType.FACE);
        opCreateOutline(context, vid + "outline", { "tools" : qUnion(outlineTools), "target" : target });
        opDeleteBodies(context, vid + "deleteTarget", { "entities" : qCreatedBy(vid + "target", EntityType.BODY) });
        if (view.source == ExportDxfSource.SELECTION)
        {
            opDeleteBodies(context, vid + "deleteSheets", { "entities" : qUnion(outlineTools) });
        }
        regionList = append(regionList, qCreatedBy(vid + "outline", EntityType.BODY));
    }

    // Overlapping regions (several faces) are merged so no internal lines remain.
    var regions = qUnion(regionList);
    if (size(evaluateQuery(context, regions)) > 1)
    {
        opBoolean(context, vid + "unite", { "tools" : regions, "operationType" : BooleanOperationType.UNION });
        regions = qUnion(regionList);
    }
    if (origin.applied)
    {
        opTransform(context, vid + "move", { "bodies" : regions, "transform" : origin.xf });
    }

    const originals = evaluateQuery(context, qBodyType(regions, BodyType.SHEET));
    var copies = [];
    var patternData = { "applied" : false };
    if (pattern.applied)
    {
        // Pattern from the export origin along the view's X and second axis (plan Y, profile Z). The pitch is the
        // faces' extent plus the clearance, or the given distance, which must not be smaller than the extent.
        const bb = evBox3d(context, { "topology" : qUnion(originals) });
        const extentX = bb.maxCorner[0] - bb.minCorner[0];
        const extentY = bb.maxCorner[view.secondIndex] - bb.minCorner[view.secondIndex];
        const pitchX = pattern.mode == ExportPatternSpacing.CLEARANCE ? extentX + pattern.spacingX : pattern.spacingX;
        const pitchY = pattern.mode == ExportPatternSpacing.CLEARANCE ? extentY + pattern.spacingY : pattern.spacingY;
        if (pattern.countX > 1 && pitchX < extentX - TOLERANCE.zeroLength * meter)
        {
            throw regenError(view.label ~ " pattern distance in X is smaller than the part (" ~ roundMm(extentX) ~ " mm): the copies would overlap.", ["pitchX"]);
        }
        if (pattern.countY > 1 && pitchY < extentY - TOLERANCE.zeroLength * meter)
        {
            throw regenError(view.label ~ " pattern distance in Y is smaller than the part (" ~ roundMm(extentY) ~ " mm): the copies would overlap.", ["pitchY"]);
        }
        var transforms = [];
        var names = [];
        var instances = [{ "n" : 0, "ix" : 0, "iy" : 0, "offsetXMm" : 0, "offsetYMm" : 0 }];
        for (var j = 0; j < pattern.countY; j += 1)
        {
            for (var i = 0; i < pattern.countX; i += 1)
            {
                if (i == 0 && j == 0)
                {
                    continue;
                }
                transforms = append(transforms, transform(view.xAxis * (pitchX * i) + view.secondAxis * (pitchY * j)));
                names = append(names, "p_" ~ i ~ "_" ~ j);
                instances = append(instances, { "n" : size(instances), "ix" : i, "iy" : j,
                            "offsetXMm" : pitchX * i / millimeter, "offsetYMm" : pitchY * j / millimeter });
            }
        }
        opPattern(context, vid + "pattern", { "entities" : qUnion(originals), "transforms" : transforms, "instanceNames" : names });
        copies = evaluateQuery(context, qBodyType(qCreatedBy(vid + "pattern", EntityType.BODY), BodyType.SHEET));
        patternData = { "applied" : true, "instances" : instances, "countX" : pattern.countX, "countY" : pattern.countY,
            "pitchXMm" : pitchX / millimeter, "pitchYMm" : pitchY / millimeter,
            "extentXMm" : extentX / millimeter, "extentYMm" : extentY / millimeter,
            "spacing" : pattern.mode == ExportPatternSpacing.CLEARANCE ? "CLEARANCE" : "DISTANCE" };
    }

    // Originals: "<stem> XY" (or "<stem> XY <k>" when the outline has several regions). Copies: "<stem> XY PATTERN <n>",
    // n counting along X first, then along Y (the attribute lists each instance's indices and offsets).
    const nOriginal = size(originals);
    for (var k = 0; k < nOriginal; k += 1)
    {
        setProperty(context, { "entities" : originals[k], "propertyType" : PropertyType.NAME,
                    "value" : nOriginal == 1 ? stem ~ " " ~ view.label ~ " EXPORT" : stem ~ " " ~ view.label ~ " EXPORT_" ~ (k + 1) });
    }
    for (var m = 0; m < size(copies); m += 1)
    {
        const instance = floor(m / nOriginal) + 1;
        const region = m - (instance - 1) * nOriginal + 1;
        setProperty(context, { "entities" : copies[m], "propertyType" : PropertyType.NAME,
                    "value" : stem ~ " " ~ view.label ~ " EXPORT_PATTERN_" ~ instance ~ (nOriginal > 1 ? "_" ~ region : "") });
    }
    return { "bodies" : qUnion(concatenateArrays([originals, copies])), "pattern" : patternData };
}

function roundMm(length is ValueWithUnits) returns number
{
    return round(length / millimeter * 100) / 100;
}

/** Groups a view's surface bodies (one or several) in a closed composite named "<stem> XY DXF", excluded from the BOM: the one thing to export as that DXF. */
function dxfComposite(context is Context, cid is Id, bodies is Query, name is string) returns Query
{
    if (size(evaluateQuery(context, bodies)) < 1)
    {
        return qNothing();
    }
    opCreateCompositePart(context, cid, { "bodies" : bodies, "closed" : true });
    const composite = qBodyType(qCreatedBy(cid, EntityType.BODY), BodyType.COMPOSITE);
    setProperty(context, { "entities" : composite, "propertyType" : PropertyType.NAME, "value" : name });
    setProperty(context, { "entities" : composite, "propertyType" : PropertyType.EXCLUDE_FROM_BOM, "value" : true });
    return composite;
}
