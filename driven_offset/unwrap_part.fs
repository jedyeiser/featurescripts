FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/extendendtype.gen.fs", version : "3070.0");
import(path : "onshape/std/extendsheetshapetype.gen.fs", version : "3070.0");

// IMPORT: edge_offset_utils.fs (same document; the chart: unwrapFast, packChart, referencePointAtArc; export-imports
// curve_core: classifyPoints, emitArcCurve)
export import(path : "a2665e22c07b7a6929ce4e80", version : "19bb4e2dc00faa5df759cf24");

/*
 * Unwrap a SOLID through the unwrap chart (research_unwrap_part.md).
 *
 * The chart maps a wrapped point to (x, y, z): x along the preserved length, y across the reference plane, z off
 * the offset reference. Two facts carry the whole method:
 *
 *   - Over a STRAIGHT span of the reference the map is one rigid motion, so a piece of the part lying over a line
 *     is moved by one opTransform and keeps every native face (arcs, cylinders, B-splines) exactly.
 *   - Over a curved span, a face whose normal has no component across the reference plane (b = 0) maps to an
 *     extrusion along flat Y of one XZ curve (a PROFILE face), and a face whose normal has no component along the
 *     reference normal (c = 0) maps to an extrusion along flat Z of one plan curve (a WALL). Ski parts have no
 *     other faces. Such a piece is REBUILT: one tool sheet per chain of tangent faces, a box split by all tools,
 *     the cells whose interior maps back inside the piece kept and united.
 *
 * So: split the part by planes normal to the reference where it changes between a line and a curve, move the
 * straight pieces, rebuild the curved ones, unite. Walls leaning by up to UNWRAP_PART_RULED_TOL are rebuilt
 * exactly as ruled surfaces, or stood up vertical with squareWalls.
 */

/** |b| (share of the flat normal along flat Y) below this: the face's image is an extrusion along flat Y. */
export const UNWRAP_PART_PROFILE_TOL = 1e-6;

/** |c| (share of the flat normal along flat Z) below this: a vertical wall (it leans by at most this * height). */
export const UNWRAP_PART_WALL_TOL = 1e-4;

/** |c| below this, and not a wall: a leaning wall, rebuilt as a ruled surface (or squared up). Above: not handled. */
export const UNWRAP_PART_RULED_TOL = 0.05;

/** Classification grid per face (G x G parameters). */
export const UNWRAP_PART_GRID = 9;

/** Row sampling along a face's curve direction: spacing (m), minimum and maximum count. Tight corners need 17+.
 * PROFILE and WALL rows feed approximateSpline with exact end tangents; RULED rows are interpolated (opFitSpline)
 * and need the finer spacing. */
export const UNWRAP_PART_ROW_SPACING = 1e-3;
export const UNWRAP_PART_RULED_ROW_SPACING = 5e-4;
export const UNWRAP_PART_ROW_MIN = 17;
export const UNWRAP_PART_ROW_MAX = 200;

/** Rows chain when their ends meet within this (m) and their tangents agree to this dot (0.99999 = 0.26 deg). */
export const UNWRAP_PART_JOIN = 5e-5;
export const UNWRAP_PART_TANGENT_DOT = 0.99999;

/** A chain within this (m) of an earlier one of the same kind is the same tool. */
export const UNWRAP_PART_DUPLICATE = 2e-6;

/** A chain within this (m) of its chord is straight. flatTolerance widens it (snapping near-straight chains). */
export const UNWRAP_PART_STRAIGHT = 1e-8;

/** The cell box is the piece's flat extent grown by this (m) on every side. */
export const UNWRAP_PART_BOX_MARGIN = 0.002;

/** A curved chain within this of a circle is emitted as an exact arc. */
export const UNWRAP_PART_ARC_TOL = 1e-7 * meter;

/** Spline tools are fitted (approximateSpline, degree 3, chord parameters, exact end tangents) to this. */
export const UNWRAP_PART_FIT_TOL = 1e-7 * meter;

/** Control-point cap of a spline tool fit (never reached in practice; approximateFamily warns if it is). */
export const UNWRAP_PART_FIT_MAX_CPS = 500;

/** Consecutive fit points closer than this share of the chain's chord are thinned (approximateSpline wants
 * chord parameters strictly increasing by 1e-6). */
export const UNWRAP_PART_FIT_SEPARATION = 2e-6;

/** A chain end whose extension ray passes within this (m) of another chain's end (same plane) whose normal is parallel
 * to UNWRAP_PART_GRAZE_DOT is fitted with a free end: an exact-tangent extension would graze the neighbour's tool. */
export const UNWRAP_PART_GRAZE_LATERAL = 2e-4;
export const UNWRAP_PART_GRAZE_DOT = 0.9994;

/** A ruling of a RULED tool rising less than this share of its length is degenerate (the face is reported). */
export const UNWRAP_PART_RULING_RISE = 0.5;

/** The counted keys of a report (pieces add them up; maxLean is a maximum). */
export const UNWRAP_PART_COUNT_KEYS = ["cells", "keptCells", "tools", "planeTools", "arcTools", "splineTools", "ruledTools",
        "snappedTools", "squaredWalls"];

/** An empty report: every UNWRAP_PART_COUNT_KEYS entry 0, plus maxLean 0. */
export function emptyPartReport() returns map
{
    var report = { "maxLean" : 0 };
    for (var key in UNWRAP_PART_COUNT_KEYS)
    {
        report[key] = 0;
    }
    return report;
}

// ============================================================================
// Entry point
// ============================================================================

/**
 * Unwrap one solid through a chart.
 *
 * @param chart : unwrapChart(context, W, alignPoint, d).
 * @param cs {CoordSystem} : the flat frame; a wrapped point P lands at toWorld(cs, vector(x, y, z) * meter) with
 *        [x, y, z] = unwrapFast(chart, P, undefined)[0..2] (plain metres) -- same convention as unwrap.fs.
 * @param options {map} : { "squareWalls" : boolean (default false = exact mapped walls), "flatTolerance" :
 *        ValueWithUnits (snap nearly flat mapped faces to planes; default 0.001 mm), "print" : boolean (accepted;
 *        nothing is printed here -- the caller prints "lines") }
 * @returns {map} : { "bodies" : Query (flat solid(s), created under id), "report" : { "pieces", "rigidPieces",
 *        "rebuiltPieces", "cells", "keptCells", "tools", "planeTools", "arcTools", "splineTools", "ruledTools",
 *        "snappedTools", "squaredWalls", "maxLean" }, "lines" : array of strings }
 * Leaves the input part untouched (works on a copy); all temporaries are created under id and deleted.
 */
export function unwrapSolid(context is Context, id is Id, chart is map, cs is CoordSystem, part is Query, options is map) returns map
{
    const settings = {
            "squareWalls" : options.squareWalls == true,
            "flatTolerance" : (options.flatTolerance == undefined) ? 0.001 * millimeter : options.flatTolerance,
            "part" : part
        };
    var report = mergeMaps(emptyPartReport(), { "pieces" : 0, "rigidPieces" : 0, "rebuiltPieces" : 0 });
    var lines = [];
    const inputSolids = evaluateQuery(context, qBodyType(part, BodyType.SOLID));

    opPattern(context, id + "copy", { "entities" : part, "transforms" : [identityTransform()], "instanceNames" : ["copy"] });
    const piecesQ = qBodyType(qUnion([qCreatedBy(id + "copy", EntityType.BODY), qCreatedBy(id + "junction", EntityType.BODY)]),
        BodyType.SOLID);
    if (isQueryEmpty(context, piecesQ))
    {
        throw regenError("Unwrap part: the selection has no solid to unwrap.", part);
    }

    // 1. Split where the reference changes between a line and a curve (planes normal to the reference there).
    const spans = referenceSpans(chart);
    for (var i = 1; i < size(spans); i += 1)
    {
        if (spans[i - 1].straight == spans[i].straight)
        {
            continue;
        }
        const tangent = spans[i - 1].straight ? spans[i - 1].direction : spans[i].direction;
        const pl = plane(spans[i].start, tangent);
        if (planeCutsBody(context, piecesQ, pl))
        {
            opSplitPart(context, id + "junction" + ("j" ~ i), { "targets" : piecesQ, "tool" : pl });
        }
    }

    // 2. Straight pieces move rigidly into the chart frame; curved pieces are rebuilt there.
    const pieces = evaluateQuery(context, piecesQ);
    report.pieces = size(pieces);
    var results = [];
    for (var k = 0; k < size(pieces); k += 1)
    {
        const piece = pieces[k];
        const inside = interiorPoint(context, piece);
        const foot = chartFootAt(chart, inside, undefined);
        if (!chartFootConverged(foot))
        {
            throw regenError("Unwrap part: " ~ partName(context, part) ~ " reaches past the reference's centre of curvature "
                    ~ "(the chart has no foot there).", part);
        }
        const span = spanAt(spans, foot[3]);
        if (span.straight)
        {
            opTransform(context, id + ("rigid" ~ k), { "bodies" : piece, "transform" : rigidTransform(chart, foot, inside, span.direction) });
            results = append(results, piece);
            report.rigidPieces += 1;
            lines = append(lines, "  piece " ~ k ~ ": over a line, moved rigidly ("
                ~ size(evaluateQuery(context, qOwnedByBody(piece, EntityType.FACE))) ~ " faces kept)");
        }
        else
        {
            const rebuilt = rebuildPiece(context, id + ("piece" ~ k), chart, piece, settings);
            opDeleteBodies(context, id + ("deletePiece" ~ k), { "entities" : piece });
            results = append(results, rebuilt.body);
            report.rebuiltPieces += 1;
            for (var key in UNWRAP_PART_COUNT_KEYS)
            {
                report[key] += rebuilt.report[key];
            }
            report.maxLean = max(report.maxLean, rebuilt.report.maxLean);
            lines = append(lines, "  piece " ~ k ~ ": over a curve, rebuilt: " ~ rebuilt.text);
        }
    }

    // 3. Unite, clean up, place in cs.
    if (size(results) > 1)
    {
        try silent
        {
            opBoolean(context, id + "unite", { "tools" : qUnion(results), "operationType" : BooleanOperationType.UNION });
        }
        catch
        {
            throw regenError("Unwrap part: the unwrapped pieces would not unite (they touch in a degenerate way).", part);
        }
    }
    const scrap = qBodyType(qCreatedBy(id, EntityType.BODY), [BodyType.SHEET, BodyType.WIRE, BodyType.POINT]);
    if (!isQueryEmpty(context, scrap))
    {
        opDeleteBodies(context, id + "deleteScrap", { "entities" : scrap });
    }
    const bodies = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID);
    const resultCount = size(evaluateQuery(context, bodies));
    if (resultCount > size(inputSolids))
    {
        throw regenError("Unwrap part: the unwrapped pieces of " ~ partName(context, part) ~ " did not fuse (" ~ resultCount
                ~ " solids from " ~ size(inputSolids) ~ ").", part);
    }
    opTransform(context, id + "place", { "bodies" : bodies, "transform" : toWorld(cs) });

    lines = concatenateArrays([["Unwrap part: " ~ report.pieces ~ " piece(s), " ~ report.rigidPieces ~ " moved rigidly, "
                    ~ report.rebuiltPieces ~ " rebuilt (" ~ report.keptCells ~ "/" ~ report.cells ~ " cells, "
                    ~ report.tools ~ " tools: " ~ report.planeTools ~ " planar, " ~ report.arcTools ~ " arc, " ~ report.splineTools ~ " spline, "
                    ~ report.ruledTools ~ " ruled, " ~ report.snappedTools ~ " snapped flat; "
                    ~ report.squaredWalls ~ " walls squared; worst wall lean " ~ roundToPrecision(report.maxLean, 6) ~ ")"],
            lines]);
    return { "bodies" : bodies, "report" : report, "lines" : lines };
}

// ============================================================================
// The reference's spans and the rigid move
// ============================================================================

/**
 * The reference's edges in chain order with their chart arc range: { "a0", "a1", "straight", "start", "direction" }.
 */
export function referenceSpans(chart is map) returns array
{
    var spans = [];
    for (var link in chart.alongRef.chain.links)
    {
        for (var e in link.edges)
        {
            const f0 = unwrapFast(chart, e.startPoint, undefined);
            const f1 = unwrapFast(chart, e.endPoint, undefined);
            if (!chartFootConverged(f0) || !chartFootConverged(f1))
            {
                throw regenError("Unwrap part: the chart does not find the reference's own edge ends.");
            }
            spans = append(spans, {
                        "a0" : f0[3],
                        "a1" : f1[3],
                        "straight" : e.curveType == CurveType.LINE,
                        "start" : e.startPoint,
                        "direction" : normalize(e.endPoint - e.startPoint)
                    });
        }
    }
    return spans;
}

/** The span holding chart arc a; past either end, the end span. */
export function spanAt(spans is array, a is number) returns map
{
    for (var span in spans)
    {
        if (a <= span.a1)
        {
            return span;
        }
    }
    return spans[size(spans) - 1];
}

/**
 * The chart map over a straight span, as a transform into the chart frame: the frame (tangent, -planeNormal,
 * planeNormal x tangent) at `point` lands on the world axes at the point's chart coordinates.
 * @param u : unwrapFast(chart, point, ...) (converged).
 */
export function rigidTransform(chart is map, u is array, point is Vector, tangent is Vector) returns Transform
{
    const normal = normalize(cross(chart.alongRef.planeNormal, tangent));
    return toWorld(coordSystem(vector(u[0], u[1], u[2]) * meter, vector(1, 0, 0), vector(0, 0, 1)))
        * fromWorld(coordSystem(point, tangent, normal));
}

/** True when the plane has corners of the bodies' box on both sides of it. */
export function planeCutsBody(context is Context, bodies is Query, pl is Plane) returns boolean
{
    const bb = evBox3d(context, { "topology" : bodies, "tight" : true });
    var below = false;
    var above = false;
    for (var cx in [bb.minCorner[0], bb.maxCorner[0]])
    {
        for (var cy in [bb.minCorner[1], bb.maxCorner[1]])
        {
            for (var cz in [bb.minCorner[2], bb.maxCorner[2]])
            {
                const side = dot(vector(cx, cy, cz) - pl.origin, pl.normal);
                if (side > 1e-7 * meter)
                {
                    above = true;
                }
                else if (side < -1e-7 * meter)
                {
                    below = true;
                }
            }
        }
    }
    return above && below;
}

/** A point inside a solid: its centroid, or a point just inside one of its faces when the centroid is not. */
export function interiorPoint(context is Context, body is Query) returns Vector
{
    const centroid = evApproximateCentroid(context, { "entities" : body });
    if (!isQueryEmpty(context, qContainsPoint(body, centroid)))
    {
        return centroid;
    }
    for (var face in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)))
    {
        for (var uv in [vector(0.5, 0.5), vector(0.25, 0.25), vector(0.75, 0.75), vector(0.25, 0.75), vector(0.75, 0.25)])
        {
            const tp = evFaceTangentPlane(context, { "face" : face, "parameter" : uv });
            const pt = tp.origin - 1e-6 * meter * tp.normal;
            if (!isQueryEmpty(context, qContainsPoint(body, pt)))
            {
                return pt;
            }
        }
    }
    return centroid;
}

/**
 * unwrapFast with a cold retry: a warm start from a far-away previous foot can fail where a cold seed does not.
 * The caller checks chartFootConverged on the result; a point whose foot is not found is never used as mapped.
 */
export function chartFootAt(chart is map, point is Vector, previous) returns array
{
    const u = unwrapFast(chart, point, previous);
    if (previous == undefined || chartFootConverged(u))
    {
        return u;
    }
    return unwrapFast(chart, point, undefined);
}

/** The name of the first solid of a selection, for messages ("the part" when it has none). */
export function partName(context is Context, part is Query) returns string
{
    const solids = evaluateQuery(context, qBodyType(part, BodyType.SOLID));
    if (size(solids) == 0)
    {
        return "the part";
    }
    return "\"" ~ getProperty(context, { "entity" : solids[0], "propertyType" : PropertyType.NAME }) ~ "\"";
}

/**
 * The unwrapped normal of a face with world normal n at a point whose unwrapFast result is u, in the chart frame
 * and normalised: [a, b, c] = components along flat X, Y, Z. Normals map by the inverse transpose of the chart's
 * Jacobian, diag(scale / (scale - kappa * height), 1, 1) in the frame (t, -planeNormal, planeNormal x t).
 */
export function flatNormal(chart is map, u is array, n is Vector) returns array
{
    const c = chart.packed;
    const nx = c.ny * u[7] - c.nz * u[6];
    const ny = c.nz * u[5] - c.nx * u[7];
    const nz = c.nx * u[6] - c.ny * u[5];
    const a = (n[0] * u[5] + n[1] * u[6] + n[2] * u[7]) * (u[9] - u[8] * u[10]) / u[9];
    const b = -(n[0] * c.nx + n[1] * c.ny + n[2] * c.nz);
    const up = n[0] * nx + n[1] * ny + n[2] * nz;
    const len = sqrt(a * a + b * b + up * up);
    return [a / len, b / len, up / len];
}

// ============================================================================
// Cell rebuild of a curved piece
// ============================================================================

/**
 * Rebuild one piece in the chart frame: classify its faces, sample one row per face, chain and dedupe the rows,
 * build a tool per chain across the piece's flat box (a plane for a straight chain), split the box by every tool,
 * keep the cells that map back inside the piece, unite them.
 * @returns {map} : { "body" : Query, "report", "text" }
 */
export function rebuildPiece(context is Context, id is Id, chart is map, piece is Query, settings is map) returns map
{
    var report = emptyPartReport();

    // 1. Classify faces; one row per face.
    const G = UNWRAP_PART_GRID;
    var grid = [];
    for (var i = 0; i < G; i += 1)
    {
        for (var j = 0; j < G; j += 1)
        {
            grid = append(grid, vector(i / (G - 1), j / (G - 1)));
        }
    }
    const extent = chartExtent(context, chart, piece, settings.part);
    var lo = extent[0];
    var hi = extent[1];
    var rows = [];
    var failed = [];
    const faces = evaluateQuery(context, qOwnedByBody(piece, EntityType.FACE));
    for (var face in faces)
    {
        const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : grid });
        var feet = [];
        var flatGrid = [];
        var misses = 0;
        var previous = undefined;
        for (var tp in planes)
        {
            const u = chartFootAt(chart, tp.origin, previous);
            if (chartFootConverged(u))
            {
                previous = u;
                feet = append(feet, u);
                flatGrid = append(flatGrid, [u[0], u[1], u[2]]);
            }
            else
            {
                // Only grid points on the surface's extension can get here; on-face ones throw below.
                misses += 1;
                feet = append(feet, undefined);
                flatGrid = append(flatGrid, undefined);
            }
        }
        // Grid points outside the face's trim lie on its surface's extension, which is at worst less of a profile
        // or wall than the face; so only a face that does not pass on the whole grid (or has an unmapped grid point)
        // is re-read on its trim alone.
        var kind = "FAIL";
        var shares = undefined;
        if (misses == 0)
        {
            shares = normalShares(chart, planes, feet);
            kind = faceKind(shares);
        }
        if (kind != "PROFILE" && kind != "WALL")
        {
            shares = onFaceShares(context, chart, face, grid, settings.part);
            kind = faceKind(shares);
        }
        if (kind == "LEANING")
        {
            kind = settings.squareWalls ? "WALL" : "RULED";
            if (settings.squareWalls)
            {
                report.squaredWalls += 1;
            }
        }
        if (kind != "PROFILE" && kind != "FAIL")
        {
            report.maxLean = max(report.maxLean, shares[1]);
        }
        if (kind == "FAIL")
        {
            failed = append(failed, face);
            continue;
        }
        rows = append(rows, faceRow(context, chart, face, kind, flatGrid, settings.part));
    }
    if (size(failed) > 0)
    {
        throw regenError("Unwrap part: " ~ size(failed) ~ " face(s) of " ~ partName(context, settings.part)
                ~ " over the curved part of the reference are neither extruded across the reference plane nor walls normal "
                ~ "to the reference; they cannot be unwrapped exactly.", qUnion(failed));
    }

    // 2. Chain tangent rows of one kind, drop duplicates.
    var chains = [];
    for (var kind in ["PROFILE", "WALL", "RULED"])
    {
        chains = concatenateArrays([chains, chainRows(rows, kind)]);
    }
    chains = markGrazingEnds(distinctChains(chains, settings.flatTolerance.value));

    // 3. One tool per chain, across the box.
    for (var ax in [0, 1, 2])
    {
        lo[ax] -= UNWRAP_PART_BOX_MARGIN;
        hi[ax] += UNWRAP_PART_BOX_MARGIN;
    }
    const reach = sqrt((hi[0] - lo[0]) ^ 2 + (hi[1] - lo[1]) ^ 2 + (hi[2] - lo[2]) ^ 2) + 0.01;
    var tools = [];
    var sheets = [];
    for (var i = 0; i < size(chains); i += 1)
    {
        const tool = chainTool(context, id + "tool" + ("t" ~ i), chains[i], lo, hi, reach);
        tools = append(tools, tool);
        if (tool.body != undefined)
        {
            sheets = append(sheets, tool.body);
        }
        report[tool.counter] += 1;
        if (chains[i].snapped)
        {
            report.snappedTools += 1;
        }
    }
    report.tools = size(tools);

    // 4. Box, split by every tool (one at a time: one opSplitPart with several tools uses only the first).
    fCuboid(context, id + "box", { "corner1" : vector(lo[0], lo[1], lo[2]) * meter, "corner2" : vector(hi[0], hi[1], hi[2]) * meter });
    const cellsQ = qBodyType(qUnion([qCreatedBy(id + "box", EntityType.BODY), qCreatedBy(id + "split", EntityType.BODY)]), BodyType.SOLID);
    for (var i = 0; i < size(tools); i += 1)
    {
        if (tools[i].plane != undefined)
        {
            opSplitPart(context, id + "split" + ("s" ~ i), { "targets" : cellsQ, "tool" : tools[i].plane });
        }
        else
        {
            opSplitPart(context, id + "split" + ("s" ~ i), { "targets" : cellsQ, "tool" : tools[i].body, "keepTools" : true });
        }
    }

    // 5. Keep the cells whose interior maps back inside the piece (unwrapInverse: the forward map's own tables).
    const cells = evaluateQuery(context, cellsQ);
    var keep = [];
    var drop = [];
    for (var cell in cells)
    {
        const pt = interiorPoint(context, cell) / meter;
        const wrapped = unwrapInverse(chart, pt[0], pt[1], pt[2]);
        if (isQueryEmpty(context, qContainsPoint(piece, vector(wrapped[0], wrapped[1], wrapped[2]) * meter)))
        {
            drop = append(drop, cell);
        }
        else
        {
            keep = append(keep, cell);
        }
    }
    report.cells = size(cells);
    report.keptCells = size(keep);
    if (size(keep) == 0)
    {
        throw regenError("Unwrap part: no cell of a rebuilt piece of " ~ partName(context, settings.part)
                ~ " mapped back inside the part.", settings.part);
    }
    if (size(drop) > 0)
    {
        opDeleteBodies(context, id + "dropCells", { "entities" : qUnion(drop) });
    }
    if (size(sheets) > 0)
    {
        opDeleteBodies(context, id + "dropTools", { "entities" : qUnion(sheets) });
    }
    if (size(keep) > 1)
    {
        try silent
        {
            opBoolean(context, id + "unite", { "tools" : qUnion(keep), "operationType" : BooleanOperationType.UNION });
        }
        catch
        {
            throw regenError("Unwrap part: the rebuilt cells of a curved piece of " ~ partName(context, settings.part)
                    ~ " would not unite (tools touching tangentially, e.g. a groove running out onto a face).", settings.part);
        }
    }

    const text = size(faces) ~ " faces, " ~ size(chains) ~ " tools, " ~ size(keep) ~ "/" ~ size(cells) ~ " cells kept";
    return { "body" : qBodyType(qUnion(keep), BodyType.SOLID), "report" : report, "text" : text };
}

/**
 * The piece's extent in the chart frame, [lo, hi] (plain metres), from its edges sampled every 2 mm (at least 9
 * points each). The faces here are extrusions, whose extremes lie on their boundary edges; the box grows by
 * UNWRAP_PART_BOX_MARGIN anyway. An edge point the chart cannot map is an error on that edge.
 */
export function chartExtent(context is Context, chart is map, piece is Query, part is Query) returns array
{
    var lo = [1e9, 1e9, 1e9];
    var hi = [-1e9, -1e9, -1e9];
    for (var edge in evaluateQuery(context, qOwnedByBody(piece, EntityType.EDGE)))
    {
        const count = min(max(ceil(evLength(context, { "entities" : edge }) / (2 * millimeter)) + 1, 9), 200);
        var parameters = [];
        for (var k = 0; k < count; k += 1)
        {
            parameters = append(parameters, k / (count - 1));
        }
        var previous = undefined;
        for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : parameters, "arcLengthParameterization" : false }))
        {
            const u = chartFootAt(chart, tl.origin, previous);
            if (!chartFootConverged(u))
            {
                throw regenError("Unwrap part: an edge of " ~ partName(context, part)
                        ~ " reaches past the reference's centre of curvature (the chart has no foot there).", edge);
            }
            previous = u;
            for (var ax in [0, 1, 2])
            {
                lo[ax] = min(lo[ax], u[ax]);
                hi[ax] = max(hi[ax], u[ax]);
            }
        }
    }
    return [lo, hi];
}

/** [max |b|, max |c|] of a face's flat normal over grid planes with their (converged) feet. */
export function normalShares(chart is map, planes is array, feet is array) returns array
{
    var maxB = 0;
    var maxC = 0;
    for (var k = 0; k < size(planes); k += 1)
    {
        const fn = flatNormal(chart, feet[k], planes[k].normal);
        maxB = max(maxB, abs(fn[1]));
        maxC = max(maxC, abs(fn[2]));
    }
    return [maxB, maxC];
}

/**
 * [max |b|, max |c|] over the grid points on the face's trim only (one evFaceTangentPlanes call with
 * returnUndefinedOutsideFace). An on-face point the chart cannot map is an error on the face.
 */
export function onFaceShares(context is Context, chart is map, face is Query, grid is array, part is Query) returns array
{
    var planes = [];
    var feet = [];
    var previous = undefined;
    for (var tp in evFaceTangentPlanes(context, { "face" : face, "parameters" : grid, "returnUndefinedOutsideFace" : true }))
    {
        if (tp == undefined)
        {
            continue;
        }
        const u = chartFootAt(chart, tp.origin, previous);
        if (!chartFootConverged(u))
        {
            throw regenError("Unwrap part: a face of " ~ partName(context, part)
                    ~ " reaches past the reference's centre of curvature (the chart has no foot there).", face);
        }
        previous = u;
        planes = append(planes, tp);
        feet = append(feet, u);
    }
    return normalShares(chart, planes, feet);
}

/** PROFILE (image extruded along flat Y), WALL (vertical), LEANING (a wall leaning < UNWRAP_PART_RULED_TOL), FAIL. */
export function faceKind(shares is array) returns string
{
    if (shares[0] < UNWRAP_PART_PROFILE_TOL && shares[1] >= UNWRAP_PART_PROFILE_TOL)
    {
        return "PROFILE";
    }
    if (shares[1] < UNWRAP_PART_WALL_TOL)
    {
        return "WALL";
    }
    if (shares[1] < UNWRAP_PART_RULED_TOL)
    {
        return "LEANING";
    }
    return "FAIL";
}

/**
 * One row of chart points across a face, along its curve direction (the parameter direction in which the image
 * spreads most in the kind's plane), at mid-parameter of the other. PROFILE rows are (x, z), WALL and RULED rows
 * (x, y); a RULED row also carries the face's bottom and top rows in 3D, packed as [x, y, bottom, top].
 * @param flatGrid : chart points of the classification grid (index i * G + j for parameter (i, j) / (G - 1));
 *      undefined where the chart found no foot.
 * @returns {map} : { "kind", "pts", "e0", "e1", "faces" }: e0 / e1 describe the row's first / last end ("n": the flat
 *      normal there, in the kind's plane).
 */
export function faceRow(context is Context, chart is map, face is Query, kind is string, flatGrid is array, part is Query) returns map
{
    const G = UNWRAP_PART_GRID;
    const ib = (kind == "PROFILE") ? 2 : 1;
    const mid = (G - 1) / 2;
    const spreadU = planarSpread(flatGrid[mid], flatGrid[(G - 1) * G + mid], ib);
    const spreadV = planarSpread(flatGrid[mid * G], flatGrid[mid * G + G - 1], ib);
    if (spreadU < 0 && spreadV < 0)
    {
        throw regenError("Unwrap part: a face of " ~ partName(context, part)
                ~ " reaches past the reference's centre of curvature (the chart has no foot there).", face);
    }
    const alongU = spreadU >= spreadV;
    const spacing = (kind == "RULED") ? UNWRAP_PART_RULED_ROW_SPACING : UNWRAP_PART_ROW_SPACING;
    const count = min(max(ceil(max(spreadU, spreadV) / spacing), UNWRAP_PART_ROW_MIN), UNWRAP_PART_ROW_MAX);

    var middle = [];
    var bottom = [];
    var top = [];
    for (var k = 0; k < count; k += 1)
    {
        const f = k / (count - 1);
        middle = append(middle, alongU ? vector(f, 0.5) : vector(0.5, f));
        bottom = append(bottom, alongU ? vector(f, 0) : vector(0, f));
        top = append(top, alongU ? vector(f, 1) : vector(1, f));
    }
    const mapped = chartRow(context, chart, face, middle, part);
    var row = [];
    for (var p in mapped.pts)
    {
        row = append(row, [p[0], p[ib]]);
    }
    const e0 = { "n" : [mapped.n0[0], mapped.n0[ib]] };
    const e1 = { "n" : [mapped.n1[0], mapped.n1[ib]] };
    if (kind == "RULED")
    {
        const b = chartRow(context, chart, face, bottom, part).pts;
        const t = chartRow(context, chart, face, top, part).pts;
        for (var k = 0; k < count; k += 1)
        {
            row[k] = [row[k][0], row[k][1], vector(b[k][0], b[k][1], b[k][2]), vector(t[k][0], t[k][1], t[k][2])];
        }
    }
    return { "kind" : kind, "pts" : row, "e0" : e0, "e1" : e1, "faces" : [face] };
}

/** Planar spread of two chart points in (x, coordinate ib); -1 when either is undefined. */
export function planarSpread(p, q, ib is number) returns number
{
    if (p == undefined || q == undefined)
    {
        return -1;
    }
    return sqrt((q[0] - p[0]) ^ 2 + (q[ib] - p[ib]) ^ 2);
}

/**
 * Chart points [x, y, z] of a face at the given parameters, warm-started along the row, and the flat normals at
 * the first and last. A point the chart cannot map is an error on the face.
 * @returns {map} : { "pts", "n0", "n1" }
 */
export function chartRow(context is Context, chart is map, face is Query, parameters is array, part is Query) returns map
{
    var out = [];
    var previous = undefined;
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : parameters });
    var n0 = undefined;
    var n1 = undefined;
    for (var k = 0; k < size(planes); k += 1)
    {
        const u = chartFootAt(chart, planes[k].origin, previous);
        if (!chartFootConverged(u))
        {
            throw regenError("Unwrap part: a face of " ~ partName(context, part)
                    ~ " reaches past the reference's centre of curvature (the chart has no foot there).", face);
        }
        previous = u;
        out = append(out, [u[0], u[1], u[2]]);
        if (k == 0)
        {
            n0 = flatNormal(chart, u, planes[k].normal);
        }
        if (k == size(planes) - 1)
        {
            n1 = flatNormal(chart, u, planes[k].normal);
        }
    }
    return { "pts" : out, "n0" : n0, "n1" : n1 };
}

export function planarDistance(p is array, q is array) returns number
{
    return sqrt((p[0] - q[0]) ^ 2 + (p[1] - q[1]) ^ 2);
}

export function planarDirection(p is array, q is array) returns array
{
    const l = planarDistance(p, q);
    return [(q[0] - p[0]) / l, (q[1] - p[1]) / l];
}

export function planarDot(a is array, b is array) returns number
{
    return a[0] * b[0] + a[1] * b[1];
}

/**
 * Chain the rows of one kind whose ends meet with tangents agreeing (tangent-continuous neighbours extended
 * separately give near-coincident tools and sliver cells). A chain's points run in one direction; e0 / e1 describe
 * its first / last end (faceRow) and faces are its source faces.
 */
export function chainRows(rows is array, kind is string) returns array
{
    var pool = [];
    for (var r in rows)
    {
        if (r.kind == kind)
        {
            pool = append(pool, r);
        }
    }
    var used = makeArray(size(pool), false);
    var chains = [];
    for (var i = 0; i < size(pool); i += 1)
    {
        if (used[i])
        {
            continue;
        }
        used[i] = true;
        var ch = pool[i];
        var grew = true;
        while (grew)
        {
            grew = false;
            for (var j = 0; j < size(pool); j += 1)
            {
                if (used[j])
                {
                    continue;
                }
                const joined = joinRows(ch, pool[j]);
                if (joined != undefined)
                {
                    ch = joined;
                    used[j] = true;
                    grew = true;
                }
            }
        }
        chains = append(chains, { "kind" : kind, "pts" : ch.pts, "e0" : ch.e0, "e1" : ch.e1, "faces" : ch.faces });
    }
    return chains;
}

/**
 * Whether two meeting ends continue each other tangentially: their exact flat normals (e.n) parallel to
 * UNWRAP_PART_TANGENT_DOT and their chords running on (not folding back). Without normals, the chords alone must
 * agree to UNWRAP_PART_TANGENT_DOT (chords at a tight corner differ by spacing x curvature, so normals are preferred).
 */
export function endsTangent(a, b, dirA is array, dirB is array) returns boolean
{
    const na = (a == undefined) ? undefined : a.n;
    const nb = (b == undefined) ? undefined : b.n;
    if (na == undefined || nb == undefined)
    {
        return planarDot(dirA, dirB) > UNWRAP_PART_TANGENT_DOT;
    }
    const la = sqrt(na[0] * na[0] + na[1] * na[1]);
    const lb = sqrt(nb[0] * nb[0] + nb[1] * nb[1]);
    if (la < 0.5 || lb < 0.5)
    {
        return planarDot(dirA, dirB) > UNWRAP_PART_TANGENT_DOT;
    }
    return planarDot(dirA, dirB) > 0 && abs(planarDot(na, nb)) / (la * lb) > UNWRAP_PART_TANGENT_DOT;
}

/**
 * Row or chain q appended to chain ch when an end of q meets an end of ch within UNWRAP_PART_JOIN with tangents
 * agreeing to UNWRAP_PART_TANGENT_DOT (q reversed as needed; the result runs in ch's direction), else undefined.
 * The result carries ch's kind, both rows' faces, and the ends (e0 / e1) that remain ends.
 */
export function joinRows(ch is map, row is map)
{
    const q = row.pts;
    const n = size(q);
    const pts = ch.pts;
    const cn = size(pts);
    const endDir = planarDirection(pts[cn - 2], pts[cn - 1]);
    const startDir = planarDirection(pts[0], pts[1]);
    var joined = undefined;
    if (planarDistance(pts[cn - 1], q[0]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e1, row.e0, endDir, planarDirection(q[0], q[1])))
    {
        joined = { "pts" : concatenateArrays([pts, subArray(q, 1, n)]), "e0" : ch.e0, "e1" : row.e1 };
    }
    else if (planarDistance(pts[cn - 1], q[n - 1]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e1, row.e1, endDir, planarDirection(q[n - 1], q[n - 2])))
    {
        joined = { "pts" : concatenateArrays([pts, reverse(subArray(q, 0, n - 1))]), "e0" : ch.e0, "e1" : row.e0 };
    }
    else if (planarDistance(pts[0], q[n - 1]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e0, row.e1, startDir, planarDirection(q[n - 2], q[n - 1])))
    {
        joined = { "pts" : concatenateArrays([subArray(q, 0, n - 1), pts]), "e0" : row.e0, "e1" : ch.e1 };
    }
    else if (planarDistance(pts[0], q[0]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e0, row.e0, startDir, planarDirection(q[1], q[0])))
    {
        joined = { "pts" : concatenateArrays([reverse(subArray(q, 1, n)), pts]), "e0" : row.e1, "e1" : ch.e1 };
    }
    if (joined != undefined)
    {
        joined.kind = ch.kind;
        joined.faces = concatenateArrays([ch.faces, row.faces]);
    }
    return joined;
}

/** Signed distance of p from the line through a and b (planar). */
export function lineOffset(a is array, b is array, p is array) returns number
{
    const d = planarDirection(a, b);
    return -(p[0] - a[0]) * d[1] + (p[1] - a[1]) * d[0];
}

/** Distance of p from a polyline (planar). */
export function polylineDistance(pts is array, p is array) returns number
{
    var best = 1e9;
    for (var k = 0; k + 1 < size(pts); k += 1)
    {
        const a = pts[k];
        const b = pts[k + 1];
        const abx = b[0] - a[0];
        const aby = b[1] - a[1];
        const l2 = abx * abx + aby * aby;
        var f = (l2 > 0) ? ((p[0] - a[0]) * abx + (p[1] - a[1]) * aby) / l2 : 0;
        f = min(max(f, 0), 1);
        best = min(best, planarDistance(p, [a[0] + f * abx, a[1] + f * aby]));
    }
    return best;
}

/**
 * Mark straight chains (within flatTolerance of their chord; the line is then moved to the middle of the band so
 * the error is at most half of it) and drop chains lying on an earlier one of the same kind.
 * Adds "straight", "snapped", "lineStart", "lineEnd" to each chain.
 */
export function distinctChains(chains is array, flatTolerance is number) returns array
{
    const tol = max(flatTolerance, UNWRAP_PART_STRAIGHT);
    var kept = [];
    for (var ch in chains)
    {
        const pts = ch.pts;
        const n = size(pts);
        var c = ch;
        c.straight = false;
        c.snapped = false;
        if (c.kind != "RULED")
        {
            var lowest = 0;
            var highest = 0;
            for (var p in pts)
            {
                const off = lineOffset(pts[0], pts[n - 1], p);
                lowest = min(lowest, off);
                highest = max(highest, off);
            }
            if (highest - lowest <= tol)
            {
                const shift = 0.5 * (highest + lowest);
                const d = planarDirection(pts[0], pts[n - 1]);
                c.straight = true;
                c.snapped = highest - lowest > UNWRAP_PART_STRAIGHT;
                c.lineStart = [pts[0][0] - shift * d[1], pts[0][1] + shift * d[0]];
                c.lineEnd = [pts[n - 1][0] - shift * d[1], pts[n - 1][1] + shift * d[0]];
            }
        }
        var duplicate = false;
        for (var k in kept)
        {
            if (k.kind != c.kind)
            {
                continue;
            }
            var all = true;
            for (var p in [pts[0], pts[n - 1], pts[floor(n / 2)]])
            {
                const dd = k.straight ? abs(lineOffset(k.lineStart, k.lineEnd, p)) : polylineDistance(k.pts, p);
                if (dd > UNWRAP_PART_DUPLICATE)
                {
                    all = false;
                }
            }
            if (all)
            {
                duplicate = true;
                break;
            }
        }
        if (!duplicate)
        {
            kept = append(kept, c);
        }
    }
    return kept;
}

/**
 * Marks free0 / free1 on each chain: true when the end's extension ray (from the end, along its outward tangent)
 * passes within UNWRAP_PART_GRAZE_LATERAL of an end of another chain in the same plane (PROFILE with PROFILE; WALL
 * with WALL and RULED, all (x, y)), or of an x = const plane of the other kind, whose flat normal is parallel to
 * UNWRAP_PART_GRAZE_DOT. Such an end runs on
 * near-tangent into a neighbour it was not chained with (a blend into a straight wall a face away, a squared wall
 * beside a vertical one), and its tool extended along the exact tangent would cross the neighbour's at a grazing
 * angle (measured: SPLIT_FAILED on 4802 with squareWalls). A free end lets the fit choose its tangent.
 */
export function markGrazingEnds(chains is array) returns array
{
    var out = chains;
    for (var i = 0; i < size(chains); i += 1)
    {
        const pi = chains[i].pts;
        const n = size(pi);
        const ends = [{ "p" : pi[0], "e" : chains[i].e0, "t" : planarDirection(pi[1], pi[0]) },
                { "p" : pi[n - 1], "e" : chains[i].e1, "t" : planarDirection(pi[n - 2], pi[n - 1]) }];
        var free = [false, false];
        for (var j = 0; j < size(chains); j += 1)
        {
            if (j == i)
            {
                continue;
            }
            const pj = chains[j].pts;
            var others = [{ "p" : pj[0], "e" : chains[j].e0 }, { "p" : pj[size(pj) - 1], "e" : chains[j].e1 }];
            if ((chains[i].kind == "PROFILE") != (chains[j].kind == "PROFILE"))
            {
                // A chain of the other plane reaches this one only as an x = const plane (a straight chain whose
                // normal lies along x): its trace here is the line x = its mean x, normal along x.
                const nj = chains[j].e0.n;
                if (!chains[j].straight || nj == undefined || abs(nj[1]) > 0.05 * abs(nj[0]))
                {
                    continue;
                }
                const xj = 0.5 * (pj[0][0] + pj[size(pj) - 1][0]);
                others = [];
                for (var k in [0, 1])
                {
                    others = append(others, { "p" : [xj, ends[k].p[1]], "e" : { "n" : [nj[0], 0] } });
                }
            }
            for (var other in others)
            {
                for (var k in [0, 1])
                {
                    const d = [other.p[0] - ends[k].p[0], other.p[1] - ends[k].p[1]];
                    const ahead = planarDot(d, ends[k].t);
                    const lateral = abs(d[0] * ends[k].t[1] - d[1] * ends[k].t[0]);
                    if (ahead > -UNWRAP_PART_GRAZE_LATERAL && lateral < UNWRAP_PART_GRAZE_LATERAL
                        && normalsParallel(ends[k].e, other.e, UNWRAP_PART_GRAZE_DOT))
                    {
                        free[k] = true;
                    }
                }
            }
        }
        out[i].free0 = free[0];
        out[i].free1 = free[1];
    }
    return out;
}

/** Whether two ends' flat normals (e.n, 2D) are parallel (either sense) to `limit`; false when either is missing. */
export function normalsParallel(a, b, limit is number) returns boolean
{
    if (a == undefined || b == undefined || a.n == undefined || b.n == undefined)
    {
        return false;
    }
    const la = sqrt(a.n[0] * a.n[0] + a.n[1] * a.n[1]);
    const lb = sqrt(b.n[0] * b.n[0] + b.n[1] * b.n[1]);
    if (la < 0.5 || lb < 0.5)
    {
        return false;
    }
    return abs(planarDot(a.n, b.n)) / (la * lb) > limit;
}

/** A chain point placed just outside the box: PROFILE (x, z) at y below the box, WALL (x, y) at z below it. */
export function toolPoint(p is array, profile is boolean, lo is array) returns Vector
{
    return (profile ? vector(p[0], lo[1] - 0.001, p[1]) : vector(p[0], p[1], lo[2] - 0.001)) * meter;
}

/**
 * The unit tangent at a chain end in its plane: normal to the flat normal n (2D), pointing from `from` to `to`.
 * Undefined when n has no length in the plane.
 */
export function endTangent(n, from is array, to is array)
{
    if (n == undefined)
    {
        return undefined;
    }
    const len = sqrt(n[0] * n[0] + n[1] * n[1]);
    if (len < 0.5)
    {
        return undefined;
    }
    const t = [-n[1] / len, n[0] / len];
    return (planarDot(t, [to[0] - from[0], to[1] - from[1]]) < 0) ? [-t[0], -t[1]] : t;
}

/** The fitter settings of a spline tool (curve_core's approximation map). */
export function fitApproximation() returns map
{
    return { "approximationDegree" : 3, "approximationTolerance" : UNWRAP_PART_FIT_TOL,
            "approximationMaxCPs" : UNWRAP_PART_FIT_MAX_CPS };
}

/**
 * Indices of a family of equally long point lists (Vectors with units) to fit: the first, the last, and every
 * point whose averaged chord fraction is UNWRAP_PART_FIT_SEPARATION past the previous kept one and short of the
 * last (approximateSpline wants strictly increasing parameters).
 */
export function separatedIndices(members is array) returns array
{
    const n = size(members[0]);
    var fractions = makeArray(n, 0);
    for (var pts in members)
    {
        var chords = [0];
        for (var i = 1; i < n; i += 1)
        {
            chords = append(chords, chords[i - 1] + norm(pts[i] - pts[i - 1]) / meter);
        }
        const total = chords[n - 1];
        for (var i = 0; i < n; i += 1)
        {
            fractions[i] += ((total > 0) ? chords[i] / total : i / (n - 1)) / size(members);
        }
    }
    var keep = [0];
    for (var i = 1; i < n - 1; i += 1)
    {
        if (fractions[i] - fractions[keep[size(keep) - 1]] >= UNWRAP_PART_FIT_SEPARATION
            && fractions[n - 1] - fractions[i] >= UNWRAP_PART_FIT_SEPARATION)
        {
            keep = append(keep, i);
        }
    }
    return append(keep, n - 1);
}

/** The entries of an array at the given indices. */
export function pickIndices(values is array, indices is array) returns array
{
    var out = [];
    for (var i in indices)
    {
        out = append(out, values[i]);
    }
    return out;
}

/**
 * The tool of one chain, crossing the whole box. A straight PROFILE or WALL chain is a Plane (opSplitPart takes
 * one). Otherwise PROFILE: the (x, z) curve just outside the box's -Y face, extruded along Y; WALL: the (x, y)
 * curve under the box, extruded along Z; RULED: the bottom and top rows pushed along their rulings to below and
 * above the box, interpolated and lofted. PROFILE and WALL curves are exact arcs when the chain is one, else
 * approximateSpline fits (UNWRAP_PART_FIT_TOL, exact end tangents from the flat normals). Then only the
 * sheet's side edges (the rulings) are extended by `reach`: extending every edge fails on curved chains, and a tool
 * that does not cross the box does not split it.
 * @returns {map} : { "plane" : Plane (straight chains) or "body" : Query (a sheet), "counter" : report key }
 */
export function chainTool(context is Context, id is Id, chain is map, lo is array, hi is array, reach is number) returns map
{
    const sheet = id + "sheet";
    var counter = "splineTools";
    var extrusion = undefined;
    const approximation = fitApproximation();
    if (chain.kind == "RULED")
    {
        var below = [];
        var above = [];
        for (var p in chain.pts)
        {
            const B = p[2];
            const u = p[3] - B;
            if (norm(u) < 1e-9 || abs(u[2]) < UNWRAP_PART_RULING_RISE * norm(u))
            {
                throw regenError("Unwrap part: a leaning wall has a degenerate ruling at flat x = "
                        ~ roundToPrecision(B[0] * 1000, 3) ~ " mm (its bottom and top rows do not rise across it).",
                    qUnion(chain.faces));
            }
            below = append(below, (B + ((lo[2] - 0.001) - B[2]) / u[2] * u) * meter);
            above = append(above, (B + ((hi[2] + 0.001) - B[2]) / u[2] * u) * meter);
        }
        // Interpolated, not approximateSpline: a family fit lofts unreliably (correction 22), and written down as a
        // ruled B-spline surface it needs exact end tangents, whose linear extensions graze a tangent neighbour
        // tool (measured: SPLIT_FAILED on 4802 / 4803, 3-6 um slivers at the 4803 tip). See research note 8b.
        opFitSpline(context, id + "below", { "points" : below });
        opFitSpline(context, id + "above", { "points" : above });
        opLoft(context, sheet, { "profileSubqueries" : [qCreatedBy(id + "below", EntityType.EDGE), qCreatedBy(id + "above", EntityType.EDGE)],
                    "bodyType" : ToolBodyType.SURFACE });
        counter = "ruledTools";
    }
    else
    {
        const profile = chain.kind == "PROFILE";
        if (chain.straight)
        {
            const d = planarDirection(chain.lineStart, chain.lineEnd);
            const normal = profile ? vector(-d[1], 0, d[0]) : vector(-d[1], d[0], 0);
            return { "plane" : plane(toolPoint(chain.lineStart, profile, lo), normal), "counter" : "planeTools" };
        }
        const curve = id + "curve";
        var points = [];
        for (var p in chain.pts)
        {
            points = append(points, toolPoint(p, profile, lo));
        }
        const shape = classifyPoints(points, UNWRAP_PART_ARC_TOL, true, false);
        if (shape.kind == "arc")
        {
            emitArcCurve(context, curve, shape);
            counter = "arcTools";
        }
        else
        {
            const n = size(chain.pts);
            const t0 = chain.free0 ? undefined : endTangent(chain.e0.n, chain.pts[0], chain.pts[1]);
            const t1 = chain.free1 ? undefined : endTangent(chain.e1.n, chain.pts[n - 2], chain.pts[n - 1]);
            const start = (t0 == undefined) ? undefined : (profile ? vector(t0[0], 0, t0[1]) : vector(t0[0], t0[1], 0));
            const end = (t1 == undefined) ? undefined : (profile ? vector(t1[0], 0, t1[1]) : vector(t1[0], t1[1], 0));
            emitSplineCurve(context, curve, pickIndices(points, separatedIndices([points])), start, end, approximation);
        }
        extrusion = profile ? vector(0, 1, 0) : vector(0, 0, 1);
        const depth = profile ? (hi[1] - lo[1] + 0.002) : (hi[2] - lo[2] + 0.002);
        opExtrude(context, sheet, { "entities" : qCreatedBy(curve, EntityType.EDGE), "direction" : extrusion,
                    "endBound" : BoundingType.BLIND, "endDepth" : depth * meter });
    }

    const body = qCreatedBy(sheet, EntityType.BODY);
    var sides = [];
    for (var edge in evaluateQuery(context, qOwnedByBody(body, EntityType.EDGE)))
    {
        const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1], "arcLengthParameterization" : false });
        const along = ends[1].origin - ends[0].origin;
        const isSide = (extrusion == undefined)
            ? abs(along[2]) > 1e-3 * meter
            : norm(along) > 1e-9 * meter && abs(dot(normalize(along), extrusion)) > 0.999999;
        if (isSide)
        {
            sides = append(sides, edge);
        }
    }
    opExtendSheetBody(context, id + "extend", { "entities" : qUnion(sides), "tangentPropagation" : false,
                "endCondition" : ExtendEndType.EXTEND_BLIND, "extendDistance" : reach * meter,
                "extensionShape" : ExtendSheetShapeType.LINEAR });
    return { "body" : body, "counter" : counter };
}
