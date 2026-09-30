FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");
// IMPORT: station_utils.fs
export import(path : "8a8c023e223cf0814d973a63", version : "33837d750b6c0df3aee6127e");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

// IMPORT: station_geometry_icon.svg (feature icon)
IconNamespace::import(path : "4f939c6501b1063acaaa9e08", version : "b34c34d69f99fc93661e1b56");

/** How a picked datum places the measuring frame (plan = its XY, profile = its XZ, x along its X). */
export enum StationDatumUse
{
    annotation { "Name" : "World axes (datum is a point only)" }
    ORIGIN,
    annotation { "Name" : "Mate connector's axes" }
    COORDINATE_SYSTEM
}

/**
 * Station geometry: the drawing-aid geometry for one part, generated instead of hand-built
 * sketches.
 *
 * For each view (plan = datum XY, profile = datum XZ, or any mate connector's XY) it projects
 * the part onto the view plane and builds, in that plane:
 *     outline wires   the part's silhouette (outer loops)
 *     outline surface the region inside them (optional)
 *     station lines   one wire per station, spanning the silhouette where the station crosses it
 *     extent points   the outline's two ends along the measuring axis, always (TIP / TAIL when the stations
 *                     include FCP and ACP, else MIN X / MAX X); table rows with x only
 *     datum point     the view origin, for ordinate dimensions
 * and groups them WITH THE PART in an open composite "<prefix> <VIEW>", excluded from the
 * BOM. The part stays its own body; open composites may share it.
 *
 * Stations come from the picked Station definition feature(s) plus any listed here (none is valid: the view
 * then has only its two extent rows). Every station's
 * operation id is its name, so adding or removing a station never re-binds another
 * station's drawing dimensions.
 *
 * Open composites cannot move, so the datum frame changes numbers, not geometry: the
 * published station table is measured from the datum, and "Flat copy at datum" adds a
 * separate copy of the outline surface moved onto world XY with the datum at the origin
 * (for DXF export).
 */

/** Samples per edge when looking for where it crosses a station plane. */
const CROSSING_SAMPLES = 33;
/** Rounds of subdividing a bracketed crossing, and points per round. */
const REFINE_ROUNDS = 4;
const REFINE_POINTS = 9;
/** View planes are this large; the part must project inside. */
const VIEW_PLANE_SIZE = 20 * meter;
/** How far in front of the part (along the view normal) the view geometry is built, so a drawing view shows it. */
const VIEW_CLEARANCE = 0.01 * millimeter;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Station geometry",
            "Feature Type Description" : "Outline, station lines and datum for a part's drawing views, grouped with the part in open composites.",
            "Editing Logic Function" : "stationGeometryEditLogic" }
export const stationGeometry = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "Part and datum", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Part", "Filter" : (EntityType.BODY && (BodyType.SOLID || BodyType.SHEET)) || BodyType.COMPOSITE, "MaxNumberOfPicks" : 1,
                        "Description" : "The solid, sheet or composite to measure." }
            definition.part is Query;

            annotation { "Name" : "Datum (measuring origin)", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1,
                        "Description" : "Where x = 0. Empty = world origin. With 'World axes' only its position counts: x runs along world X, plan = world XY, profile = world XZ." }
            definition.datum is Query;

            annotation { "Name" : "Datum axes", "Default" : StationDatumUse.ORIGIN,
                        "Description" : "World axes: the datum is a point only (a ski's own mate connectors usually have Z along the ski, which would turn every view). Mate connector's axes: the picked connector's X is the measuring axis and its XY the plan plane." }
            definition.datumUses is StationDatumUse;

            annotation { "Name" : "Name prefix", "Default" : "", "MaxLength" : 128,
                        "Description" : "Starts every body name: <prefix> PLAN, <prefix> PLAN ST MRS, ... Filled with the part's name when the part is picked." }
            definition.prefix is string;
        }

        annotation { "Group Name" : "Views", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Plan view (datum XY)", "Default" : true }
            definition.planView is boolean;

            annotation { "Name" : "Profile view (datum XZ)", "Default" : false }
            definition.profileView is boolean;

            annotation { "Name" : "Custom views", "Item name" : "view", "Item label template" : "#viewName",
                        "Description" : "More views, each on a mate connector's XY plane." }
            definition.otherViews is array;
            for (var view in definition.otherViews)
            {
                annotation { "Name" : "View name", "Default" : "", "MaxLength" : 32,
                            "Description" : "Names the view's bodies (<prefix> <name>), e.g. SIDE or BASE." }
                view.viewName is string;

                annotation { "Name" : "View mate connector", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "Projects onto the connector's XY plane; its X is the measuring axis and its origin the datum." }
                view.viewConnector is Query;
            }
        }

        annotation { "Group Name" : "Stations", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Station definitions",
                        "Description" : "The Station definition feature(s) whose stations this part is measured at. Several are read one after another." }
            definition.stationDefinitions is FeatureList;

            // Before 2026-09-28 the set was named by its variable here; kept hidden so saved features still read it
            // (used only when no Station definition is picked).
            annotation { "Name" : "Station set (variable)", "Default" : "", "MaxLength" : 64, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            definition.stationSet is string;

            annotation { "Name" : "Extra stations (this part only)", "Item name" : "station", "Item label template" : "#stationName",
                        "Description" : "Stations measured on this part only, after the Station definitions' stations." }
            definition.stations is array;
            for (var entry in definition.stations)
            {
                stationEntryPredicate(entry);
            }
        }

        annotation { "Group Name" : "Geometry to create", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Station lines", "Default" : true,
                        "Description" : "One wire per station across the part's outline, to dimension." }
            definition.stationLines is boolean;

            annotation { "Name" : "Outline wires", "Default" : true,
                        "Description" : "The part's outline in each view as wires." }
            definition.outlineWires is boolean;

            annotation { "Name" : "Outline as surface", "Default" : false,
                        "Description" : "The region inside the outline as a flat sheet (shaded in drawings)." }
            definition.outlineSurface is boolean;

            annotation { "Name" : "Datum point", "Default" : true,
                        "Description" : "A point at the datum in each view, for ordinate dimensions." }
            definition.datumPoint is boolean;

            annotation { "Name" : "Flat copy at datum", "Default" : false,
                        "Description" : "A separate copy of the outline surface with the view on world XY and the datum at the origin -- for DXF export." }
            definition.flatCopy is boolean;
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print station table to notices", "Default" : false,
                        "Description" : "Print every view's station rows (x, span, edges) to the FeatureScript notices." }
            definition.printTable is boolean;
        }
    }
    {
        if (isQueryEmpty(context, definition.part))
        {
            throw regenError("Select a part.", ["part"]);
        }

        if (definition.prefix == "")
        {
            throw regenError("Enter a name prefix, e.g. the part number.", ["prefix"]);
        }
        const prefix = definition.prefix;
        const stations = collectStations(context, definition.stationDefinitions, definition.stationSet, definition.stations);
        // Table language from the Station definition; the Station table reads it from the view's attribute.
        definition.tableLanguage = readStationLanguage(context, definition.stationDefinitions);
        const views = viewFrames(context, definition);
        if (size(views) == 0)
        {
            throw regenError("Turn on at least one view.", ["planView"]);
        }

        var outputs = [];
        var queries = {};
        var table = [];
        var missed = [];
        for (var view in views)
        {
            const built = buildView(context, id + view.key, definition, view, stations, prefix);
            outputs = append(outputs, built.output);
            queries = mergeMaps(queries, built.queries);
            table = concatenateArrays([table, built.rows]);
            if (size(built.missed) > 0)
            {
                missed = append(missed, view.label ~ ": " ~ join(built.missed, ", "));
            }
        }

        if (definition.printTable)
        {
            printTable(table);
        }

        embedStandardOutputs(context, id, {
                    "output" : qUnion(outputs),
                    "outputDescription" : "The view composites and flat copies",
                    "inputs" : definition.part,
                    "variables" : {
                        "stationTable" : extractableVariable(table, "One row per view and station: view, id, x, lo, hi, span in mm from the datum; hit false = the station misses the part. Plus two rows per view at the part's ends (extent TIP / TAIL or MIN / MAX): x only."),
                        "stationCount" : extractableVariable(size(stations), "Stations measured in each view.")
                    },
                    "queries" : queries
                });

        const summary = size(stations) ~ " stations and the part's two ends in " ~ size(views) ~ " view(s).";
        if (size(missed) > 0)
        {
            reportFeatureInfo(context, id, summary ~ " Missing the part -- " ~ join(missed, "; "));
        }
        else
        {
            reportFeatureInfo(context, id, summary);
        }
    }, { "datumUses" : StationDatumUse.ORIGIN });

/**
 * Fills the name prefix with the part's name when the part is picked, and follows a part
 * change as long as the prefix still is the previous part's name. A typed prefix is kept.
 * (The body cannot read names: getProperty throws during regeneration.)
 */
export function stationGeometryEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    if (isQueryEmpty(context, definition.part))
    {
        return definition;
    }
    const partName = getProperty(context, { "entity" : definition.part, "propertyType" : PropertyType.NAME });
    if (definition.prefix == "")
    {
        definition.prefix = partName;
    }
    else if (oldDefinition.part != undefined && !isQueryEmpty(context, oldDefinition.part)
        && definition.prefix == getProperty(context, { "entity" : oldDefinition.part, "propertyType" : PropertyType.NAME }))
    {
        definition.prefix = partName;
    }
    return definition;
}

/**
 * The views to build: { key, label, cs }. cs X = measuring axis, cs Z = view normal.
 */
function viewFrames(context is Context, definition is map) returns array
{
    var datum = coordSystem(vector(0, 0, 0) * meter, vector(1, 0, 0), vector(0, 0, 1));
    if (!isQueryEmpty(context, definition.datum))
    {
        const connectors = evaluateQuery(context, qBodyType(qOwnerBody(definition.datum), BodyType.MATE_CONNECTOR));
        if (definition.datumUses == StationDatumUse.COORDINATE_SYSTEM)
        {
            if (size(connectors) == 0)
            {
                throw regenError("Datum axes = Mate connector's axes: the datum must be a mate connector.", ["datum", "datumUses"]);
            }
            datum = evMateConnector(context, { "mateConnector" : connectors[0] });
        }
        else
        {
            // World axes: only the datum's position. A ski's own connectors have Z along the ski; using their
            // frame put every station at x = 0 and measured the base thickness as the "width" (2026-09-29).
            const origin = size(connectors) > 0 ? evMateConnector(context, { "mateConnector" : connectors[0] }).origin
                : evVertexPoint(context, { "vertex" : definition.datum });
            datum = coordSystem(origin, vector(1, 0, 0), vector(0, 0, 1));
        }
    }

    var views = [];
    if (definition.planView)
    {
        views = append(views, { "key" : "plan", "label" : "PLAN", "cs" : datum });
    }
    if (definition.profileView)
    {
        // Looking from datum -Y: X stays the measuring axis and datum Z reads as up.
        const yAxis = cross(datum.zAxis, datum.xAxis);
        views = append(views, { "key" : "profile", "label" : "PROFILE", "cs" : coordSystem(datum.origin, datum.xAxis, -yAxis) });
    }
    for (var i = 0; i < size(definition.otherViews); i += 1)
    {
        const v = definition.otherViews[i];
        if (isQueryEmpty(context, v.viewConnector))
        {
            throw regenError("Custom view " ~ (i + 1) ~ ": select the view mate connector.", ["otherViews[" ~ i ~ "].viewConnector"]);
        }
        const label = stationIdFromName(v.viewName);
        if (label == "")
        {
            throw regenError("Custom view " ~ (i + 1) ~ ": enter a view name.", ["otherViews[" ~ i ~ "].viewName"]);
        }
        views = append(views, { "key" : "view_" ~ label, "label" : label,
                    "cs" : evMateConnector(context, { "mateConnector" : v.viewConnector }) });
    }
    return views;
}

/**
 * Builds one view's geometry and composite. Returns { output, queries, rows, missed }.
 */
function buildView(context is Context, vid is Id, definition is map, view is map, stations is array, prefix is string) returns map
{
    // Build on a plane just in FRONT of the part (along the view normal): geometry in the datum plane
    // lies on or behind the part and a drawing view with hidden lines off does not show it (2026-09-28:
    // the 4101 PLAN station lines sat on the part's underside, invisible in a top view). Measurements are
    // in-plane, so the table does not change.
    const extent = evBox3d(context, { "topology" : definition.part, "cSys" : view.cs, "tight" : true });
    const cs = coordSystem(toWorld(view.cs, vector(0 * meter, 0 * meter, extent.maxCorner[2] + VIEW_CLEARANCE)), view.cs.xAxis, view.cs.zAxis);
    const n = cs.zAxis;
    const u = cs.xAxis;
    const viewPlane = plane(cs.origin, n, u);
    const namePrefix = prefix ~ " " ~ view.label;

    // Silhouette on the view plane. The outline refuses a composite, so give it the members.
    //   solid                    -> opCreateOutline (a region)
    //   flat sheet seen face-on  -> the sheet IS its silhouette: copied onto the view plane (a region)
    //   any other sheet          -> its boundary (laminar) edges dropped onto the view plane (edges, no region)
    // opCreateOutline fails on sheets: face-on, edge-on (a wall in plan) and curved sheets close to the view plane
    // (2026-09-28, 2D_PERIPHERY and TOP_SURFACE); the dropped boundary gives the same station spans.
    const tools = evaluateQuery(context, qUnion([qBodyType(definition.part, [BodyType.SOLID, BodyType.SHEET]), qFlattenedCompositeParts(definition.part)]));
    var outlineTools = [];
    var dropTools = [];
    var flatRegions = [];
    for (var i = 0; i < size(tools); i += 1)
    {
        const onPlane = faceOnPoint(context, tools[i], n);
        if (onPlane != undefined)
        {
            opPattern(context, vid + ("faceOn" ~ i), {
                        "entities" : tools[i],
                        "transforms" : [transform(n * dot(cs.origin - onPlane, n))],
                        "instanceNames" : ["faceOn" ~ i]
                    });
            flatRegions = append(flatRegions, qCreatedBy(vid + ("faceOn" ~ i), EntityType.BODY));
        }
        else if (isQueryEmpty(context, qBodyType(tools[i], BodyType.SHEET)))
        {
            outlineTools = append(outlineTools, tools[i]);
        }
        else
        {
            dropTools = append(dropTools, tools[i]);
        }
    }
    var regions = [];
    var dropped = qNothing();
    if (size(outlineTools) > 0 || size(dropTools) > 0)
    {
        opPlane(context, vid + "target", { "plane" : viewPlane, "width" : VIEW_PLANE_SIZE, "height" : VIEW_PLANE_SIZE });
        const target = qCreatedBy(vid + "target", EntityType.FACE);
        if (size(outlineTools) > 0)
        {
            opCreateOutline(context, vid + "outline", { "tools" : qUnion(outlineTools), "target" : target });
            regions = [qCreatedBy(vid + "outline", EntityType.BODY)];
        }
        if (size(dropTools) > 0)
        {
            opDropCurve(context, vid + "drop", {
                        "tools" : qEdgeTopologyFilter(qOwnedByBody(qUnion(dropTools), EntityType.EDGE), EdgeTopology.LAMINAR),
                        "targets" : target,
                        "projectionType" : ProjectionType.NORMAL_TO_TARGET
                    });
            dropped = qCreatedBy(vid + "drop", EntityType.BODY);
        }
        opDeleteBodies(context, vid + "deleteTarget", { "entities" : qCreatedBy(vid + "target", EntityType.BODY) });
    }
    regions = concatenateArrays([regions, flatRegions]);
    if (size(regions) > 1)
    {
        opBoolean(context, vid + "unite", { "tools" : qUnion(regions), "operationType" : BooleanOperationType.UNION });
    }

    const outlineBody = qUnion(regions);
    const outlineFaces = qOwnedByBody(outlineBody, EntityType.FACE);
    const hasRegion = !isQueryEmpty(context, outlineFaces);
    const droppedEdges = qOwnedByBody(dropped, EntityType.EDGE);
    if (!hasRegion && isQueryEmpty(context, droppedEdges))
    {
        throw regenError(view.label ~ ": the part has no outline in this view.", ["part"]);
    }
    const outlineEdges = qUnion([qLoopEdges(outlineFaces), droppedEdges]);

    var members = [];
    var queries = {};
    var rows = [];
    var missed = [];

    for (var s in stations)
    {
        const stationId = vid + ("st_" ~ s.id);
        const key = view.key ~ "_" ~ s.id;
        queries[key] = qNothing();

        const p = project(viewPlane, s.origin);
        var w = cross(n, u);
        if (s.hasDirection)
        {
            const inPlane = s.direction - n * dot(s.direction, n);
            if (norm(inPlane) < 1e-6)
            {
                missed = append(missed, s.id);
                rows = append(rows, missRow(view.key, s.id, dot(p - cs.origin, u)));
                continue;
            }
            w = normalize(cross(n, inPlane));
        }

        const v = cross(n, u);
        var ends = spanAcross(context, outlineEdges, p, w, n, !hasRegion);
        if (ends != undefined && norm(ends[1] - ends[0]) < TOLERANCE.zeroLength * meter)
        {
            // A surface seen edge-on (a wall in plan, a top surface in profile) is one curve here: measure from
            // the datum axis (through the datum along x) to where the station crosses it -- half-width, height.
            ends = fromDatumAxis(ends[0], p, w, cs.origin, v);
        }
        if (ends == undefined)
        {
            missed = append(missed, s.id);
            rows = append(rows, missRow(view.key, s.id, dot(p - cs.origin, u)));
            continue;
        }

        rows = append(rows, {
                    "view" : view.key,
                    "id" : s.id,
                    "x" : dot(p - cs.origin, u) / millimeter,
                    "lo" : dot(ends[0] - cs.origin, v) / millimeter,
                    "hi" : dot(ends[1] - cs.origin, v) / millimeter,
                    "span" : norm(ends[1] - ends[0]) / millimeter,
                    "hit" : true
                });

        if (definition.stationLines)
        {
            opFitSpline(context, stationId, { "points" : ends });
            const wire = qCreatedBy(stationId, EntityType.BODY);
            nameBodies(context, wire, namePrefix ~ " ST " ~ s.id);
            members = append(members, wire);
            queries[key] = wire;
        }
    }

    // The part's ends along the measuring axis, always (2026-09-30, user: "always have extents, even if no other
    // stations are provided"). Position only: a width measured exactly at an end is unstable (4101's tail grazes at
    // 0.94 mm), so the row has no lo / hi / span. A point at each end on the outline, for ordinate dimensions.
    for (var e in viewExtents(context, outlineEdges, cs, stations, definition.tableLanguage))
    {
        // lo / hi / span = 0 (numbers) so a Station table from an older version (which rounds them) still renders
        // instead of failing; the current table shows extent rows as position only.
        rows = append(rows, { "view" : view.key, "id" : e.name, "x" : dot(e.point - cs.origin, u) / millimeter, "hit" : true, "extent" : e.role,
                    "lo" : 0, "hi" : 0, "span" : 0 });
        opPoint(context, vid + ("extent" ~ e.key), { "point" : e.point });
        const pt = qCreatedBy(vid + ("extent" ~ e.key), EntityType.BODY);
        nameBodies(context, pt, namePrefix ~ " " ~ e.name);
        members = append(members, pt);
        queries[view.key ~ "Extent" ~ e.key] = pt;
    }

    // Outline wires from the silhouette's outer loops (holes are not part of it), plus the dropped sheet boundaries
    // (already wires).
    queries[view.key ~ "Outline"] = qNothing();
    if (definition.outlineWires)
    {
        var wireList = [dropped];
        if (hasRegion)
        {
            opExtractWires(context, vid + "outlineWires", { "edges" : qLoopEdges(outlineFaces) });
            wireList = append(wireList, qCreatedBy(vid + "outlineWires", EntityType.BODY));
        }
        const wires = qUnion(wireList);
        nameBodies(context, wires, namePrefix ~ " OUTLINE");
        members = append(members, wires);
        queries[view.key ~ "Outline"] = wires;
    }

    queries[view.key ~ "Datum"] = qNothing();
    if (definition.datumPoint)
    {
        opPoint(context, vid + "datum", { "point" : cs.origin });
        const pt = qCreatedBy(vid + "datum", EntityType.BODY);
        nameBodies(context, pt, namePrefix ~ " DATUM");
        members = append(members, pt);
        queries[view.key ~ "Datum"] = pt;
    }

    queries[view.key ~ "Flat"] = qNothing();
    var output = [];
    if (definition.flatCopy)
    {
        opPattern(context, vid + "flat", {
                    "entities" : qUnion([outlineBody, dropped]),
                    "transforms" : [fromWorld(cs)],
                    "instanceNames" : ["flat"]
                });
        const flat = qCreatedBy(vid + "flat", EntityType.BODY);
        nameBodies(context, flat, namePrefix ~ " FLAT");
        output = append(output, flat);
        queries[view.key ~ "Flat"] = flat;
    }

    // The view's curves (station lines, outline wires) and datum point go into ONE closed composite
    // "<prefix> <VIEW> WIRES", so the parts list shows one entry per view instead of a body per station
    // (2026-09-28, user). Closed hides the members; drawings and queries still reach their edges.
    var viewBodies = [definition.part];
    queries[view.key ~ "Wires"] = qNothing();
    if (size(members) > 0)
    {
        opCreateCompositePart(context, vid + "wires", { "bodies" : qUnion(members), "closed" : true });
        const wiresComposite = qBodyType(qCreatedBy(vid + "wires", EntityType.BODY), BodyType.COMPOSITE);
        nameBodies(context, wiresComposite, namePrefix ~ " WIRES");
        setProperty(context, { "entities" : wiresComposite, "propertyType" : PropertyType.EXCLUDE_FROM_BOM, "value" : true });
        viewBodies = append(viewBodies, wiresComposite);
        queries[view.key ~ "Wires"] = wiresComposite;
    }

    queries[view.key ~ "Surface"] = qNothing();
    // A surface part seen edge-on or curved has no region, only its dropped boundary.
    if (definition.outlineSurface && hasRegion)
    {
        nameBodies(context, outlineBody, namePrefix ~ " REGION");
        viewBodies = append(viewBodies, outlineBody);
        queries[view.key ~ "Surface"] = outlineBody;
    }
    else if (hasRegion)
    {
        opDeleteBodies(context, vid + "deleteOutline", { "entities" : outlineBody });
    }
    if (!definition.outlineWires && !isQueryEmpty(context, dropped))
    {
        opDeleteBodies(context, vid + "deleteDropped", { "entities" : dropped });
    }

    opCreateCompositePart(context, vid + "composite", {
                "bodies" : qUnion(viewBodies),
                "closed" : false
            });
    const composite = qBodyType(qCreatedBy(vid + "composite", EntityType.BODY), BodyType.COMPOSITE);
    nameBodies(context, composite, namePrefix);
    setProperty(context, { "entities" : composite, "propertyType" : PropertyType.EXCLUDE_FROM_BOM, "value" : true });
    // Tag the view composite with its station rows: the "Station table" custom table finds it by this attribute.
    setAttribute(context, { "entities" : composite, "name" : STATION_TABLE_ATTRIBUTE, "attribute" : {
                    "schema" : STATION_TABLE_SCHEMA, "title" : namePrefix, "prefix" : prefix, "view" : view.label, "language" : definition.tableLanguage, "rows" : rows } });
    queries[view.key ~ "Composite"] = composite;
    output = append(output, composite);

    return { "output" : qUnion(output), "queries" : queries, "rows" : rows, "missed" : missed };
}

/**
 * A point on the plane of `body` when it is a sheet whose faces all lie in ONE plane facing the view (normal
 * parallel to `n`); undefined otherwise. Such a sheet is its own silhouette in that view.
 */
function faceOnPoint(context is Context, body is Query, n is Vector)
{
    if (isQueryEmpty(context, qBodyType(body, BodyType.SHEET)))
    {
        return undefined;
    }
    var point = undefined;
    for (var face in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)))
    {
        const surface = evSurfaceDefinition(context, { "face" : face });
        if (!(surface is Plane) || abs(abs(dot(surface.normal, n)) - 1) > 1e-9)
        {
            return undefined;
        }
        if (point == undefined)
        {
            point = surface.origin;
        }
        else if (abs(dot(surface.origin - point, n)) > TOLERANCE.zeroLength * meter)
        {
            return undefined;
        }
    }
    return point;
}

/**
 * [axis point, crossing] ordered low to high along `v`, where the axis point is where the station line (through
 * `p` along `w`) meets the datum axis (through `origin`, perpendicular to `v`). Undefined when the line runs along
 * the axis or the crossing lies on it.
 */
function fromDatumAxis(crossing is Vector, p is Vector, w is Vector, origin is Vector, v is Vector)
{
    const wv = dot(w, v);
    if (abs(wv) < 1e-9)
    {
        return undefined;
    }
    const onAxis = p - w * (dot(p - origin, v) / wv);
    if (norm(crossing - onAxis) < TOLERANCE.zeroLength * meter)
    {
        return undefined;
    }
    return dot(crossing - onAxis, v) > 0 * meter ? [onAxis, crossing] : [crossing, onAxis];
}

/**
 * The outline's two ends along the measuring axis (cs X): [{ key "Min" / "Max", role, name, point }], low x first.
 * point = the outline point furthest along -X / +X (on the view plane). role TIP / TAIL when the stations include
 * FCP and ACP at different x (TIP = the end on FCP's side), else MIN / MAX. name in the Station definition's
 * language; the Station table renders the role in its own.
 */
function viewExtents(context is Context, outlineEdges is Query, cs is CoordSystem, stations is array, language is string) returns array
{
    const u = cs.xAxis;
    var ends = [];
    for (var sign in [-1, 1])
    {
        const far = plane(cs.origin + u * (sign * VIEW_PLANE_SIZE), u);
        ends = append(ends, evDistance(context, { "side0" : outlineEdges, "side1" : far }).sides[0].point);
    }

    var xFcp = undefined;
    var xAcp = undefined;
    for (var s in stations)
    {
        if (s.id == "FCP")
        {
            xFcp = dot(s.origin - cs.origin, u);
        }
        else if (s.id == "ACP")
        {
            xAcp = dot(s.origin - cs.origin, u);
        }
    }
    var roles = ["MIN", "MAX"];
    if (xFcp != undefined && xAcp != undefined && abs(xFcp - xAcp) > TOLERANCE.zeroLength * meter)
    {
        roles = xFcp > xAcp ? ["TAIL", "TIP"] : ["TIP", "TAIL"];
    }
    return [
            { "key" : "Min", "role" : roles[0], "name" : stationExtentName(roles[0], language), "point" : ends[0] },
            { "key" : "Max", "role" : roles[1], "name" : stationExtentName(roles[1], language), "point" : ends[1] }
        ];
}

/** Row / body name of an extent role (TIP, TAIL, MIN, MAX) in "en" or "de" (station_table.fs WORDS has the same names). */
function stationExtentName(role is string, language is string) returns string
{
    const names = {
            "en" : { "TIP" : "TIP", "TAIL" : "TAIL", "MIN" : "MIN X", "MAX" : "MAX X" },
            "de" : { "TIP" : "SPITZE", "TAIL" : "ENDE", "MIN" : "X MIN", "MAX" : "X MAX" }
        };
    return names[language == "de" ? "de" : "en"][role];
}

function missRow(viewKey is string, stationId is string, x is ValueWithUnits) returns map
{
    return { "view" : viewKey, "id" : stationId, "x" : x / millimeter, "lo" : 0, "hi" : 0, "span" : 0, "hit" : false };
}

function nameBodies(context is Context, bodies is Query, name is string)
{
    setProperty(context, { "entities" : bodies, "propertyType" : PropertyType.NAME, "value" : name });
}

/**
 * Where the line through `p` along `w` (in the view plane, normal `n`) enters and leaves the
 * silhouette: the extreme crossings of the outline edges with the plane through that line.
 * Returns [low end, high end] along w, or undefined when the line misses the part.
 */
// With `allowPoint` a single crossing (a surface seen edge-on) returns [crossing, crossing].
function spanAcross(context is Context, outlineEdges is Query, p is Vector, w is Vector, n is Vector, allowPoint is boolean)
{
    const sp = plane(p, normalize(cross(w, n)));
    var lo = undefined;
    var hi = undefined;
    for (var edge in evaluateQuery(context, qIntersectsPlane(outlineEdges, sp)))
    {
        for (var c in edgePlaneCrossings(context, edge, sp))
        {
            const t = dot(c - p, w);
            if (lo == undefined || t < lo)
            {
                lo = t;
            }
            if (hi == undefined || t > hi)
            {
                hi = t;
            }
        }
    }
    if (lo == undefined || (!allowPoint && hi - lo < TOLERANCE.zeroLength * meter))
    {
        return undefined;
    }
    return [p + w * lo, p + w * hi];
}

/**
 * Every point where `edge` crosses `sp`. evDistance returns one point per edge; an outline
 * edge round a tip can cross a station twice, so sample the edge, bracket each sign change
 * and refine it. An edge lying in the plane contributes its ends.
 */
function edgePlaneCrossings(context is Context, edge is Query, sp is Plane) returns array
{
    const tol = TOLERANCE.zeroLength * meter;
    var ts = [];
    for (var i = 0; i < CROSSING_SAMPLES; i += 1)
    {
        ts = append(ts, i / (CROSSING_SAMPLES - 1));
    }
    const pts = sampleEdge(context, edge, ts);
    var ds = [];
    var onPlane = true;
    for (var pt in pts)
    {
        const d = dot(pt - sp.origin, sp.normal);
        ds = append(ds, d);
        if (abs(d) > tol)
        {
            onPlane = false;
        }
    }
    if (onPlane)
    {
        return [pts[0], pts[size(pts) - 1]];
    }

    var out = [];
    for (var i = 0; i < CROSSING_SAMPLES; i += 1)
    {
        if (abs(ds[i]) <= tol)
        {
            out = append(out, pts[i]);
        }
        else if (i + 1 < CROSSING_SAMPLES && abs(ds[i + 1]) > tol && (ds[i] > 0 * meter) != (ds[i + 1] > 0 * meter))
        {
            out = append(out, refineCrossing(context, edge, sp, ts[i], ts[i + 1], ds[i]));
        }
    }
    return out;
}

function refineCrossing(context is Context, edge is Query, sp is Plane, t0 is number, t1 is number, d0 is ValueWithUnits) returns Vector
{
    var a = t0;
    var b = t1;
    var da = d0;
    for (var round = 0; round < REFINE_ROUNDS; round += 1)
    {
        var ts = [];
        for (var k = 0; k < REFINE_POINTS; k += 1)
        {
            ts = append(ts, a + (b - a) * k / (REFINE_POINTS - 1));
        }
        const pts = sampleEdge(context, edge, ts);
        var prevT = ts[0];
        var prevD = da;
        for (var k = 1; k < REFINE_POINTS; k += 1)
        {
            const d = dot(pts[k] - sp.origin, sp.normal);
            if (abs(d) <= TOLERANCE.zeroLength * meter)
            {
                return pts[k];
            }
            if ((d > 0 * meter) != (prevD > 0 * meter))
            {
                a = prevT;
                b = ts[k];
                da = prevD;
                break;
            }
            prevT = ts[k];
            prevD = d;
        }
    }
    const ends = sampleEdge(context, edge, [a, b]);
    const d1 = dot(ends[1] - sp.origin, sp.normal);
    const f = da / (da - d1);
    return ends[0] + (ends[1] - ends[0]) * f;
}

function sampleEdge(context is Context, edge is Query, ts is array) returns array
{
    var pts = [];
    for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : ts, "arcLengthParameterization" : false }))
    {
        pts = append(pts, tl.origin);
    }
    return pts;
}

function printTable(table is array)
{
    println("[stations] view | id | x | lo | hi | span (mm from datum)");
    for (var r in table)
    {
        if (r.extent != undefined)
        {
            println("[stations] " ~ r.view ~ " | " ~ r.id ~ " | " ~ roundTo(r.x) ~ " | extent (position only)");
        }
        else if (r.hit)
        {
            println("[stations] " ~ r.view ~ " | " ~ r.id ~ " | " ~ roundTo(r.x) ~ " | " ~ roundTo(r.lo) ~ " | " ~ roundTo(r.hi) ~ " | " ~ roundTo(r.span));
        }
        else
        {
            println("[stations] " ~ r.view ~ " | " ~ r.id ~ " | " ~ roundTo(r.x) ~ " | misses the part");
        }
    }
}

function roundTo(value is number) returns number
{
    return round(value * 1000) / 1000;
}
