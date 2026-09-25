FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/extendendtype.gen.fs", version : "3070.0");
import(path : "onshape/std/extendsheetshapetype.gen.fs", version : "3070.0");

// IMPORT: edge_offset_utils.fs (same document; the chart: unwrapFast, packChart, referencePointAtArc; export-imports
// curve_core: classifyPoints, emitArcCurve)
export import(path : "a2665e22c07b7a6929ce4e80", version : "941e620c8511448a358a762b");

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

/** Row sampling along a face's curve direction: spacing (m), minimum and maximum count. Tight corners need 17+. */
export const UNWRAP_PART_ROW_SPACING = 5e-4;
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

/** Grid samples farther than this from their face (outside its trim) are not classified. */
export const UNWRAP_PART_ON_FACE = 1e-8 * meter;

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
    var report = { "pieces" : 0, "rigidPieces" : 0, "rebuiltPieces" : 0, "cells" : 0, "keptCells" : 0, "tools" : 0,
            "planeTools" : 0, "arcTools" : 0, "splineTools" : 0, "ruledTools" : 0, "snappedTools" : 0, "squaredWalls" : 0,
            "maxLean" : 0 };
    var lines = [];

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
        const span = spanAt(spans, unwrapFast(chart, inside, undefined)[3]);
        if (span.straight)
        {
            opTransform(context, id + ("rigid" ~ k), { "bodies" : piece, "transform" : rigidTransform(chart, inside, span.direction) });
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
            for (var key in ["cells", "keptCells", "tools", "planeTools", "arcTools", "splineTools", "ruledTools", "snappedTools", "squaredWalls"])
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
            spans = append(spans, {
                        "a0" : unwrapFast(chart, e.startPoint, undefined)[3],
                        "a1" : unwrapFast(chart, e.endPoint, undefined)[3],
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
 */
export function rigidTransform(chart is map, point is Vector, tangent is Vector) returns Transform
{
    const u = unwrapFast(chart, point, undefined);
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
 * The wrapped point at chart coordinates (x, y, z), plain metres: inverse of unwrapFast. x = arc - delta * theta(arc)
 * is solved for arc by Newton (slope 1 - delta * kappa); with delta = 0 it is one step.
 */
export function unflatPoint(chart is map, x is number, y is number, z is number) returns Vector
{
    const alongRef = chart.alongRef;
    const delta = alongRef.delta.value;
    const target = x + chart.alignX;
    var a = target;
    for (var step = 0; step < 20 && delta != 0; step += 1)
    {
        const arc = a * meter;
        const residual = target - (a - delta * interpolate(alongRef.arcs, alongRef.thetas, arc));
        if (abs(residual) < 1e-12)
        {
            break;
        }
        a = a + residual / (1 - delta * interpolate(alongRef.arcs, alongRef.curvatures, arc).value);
    }
    const basis = referenceBasisAtArc(alongRef, a * meter);
    return referencePointAtArc(alongRef, a * meter) + ((chart.alignV - y) * meter) * alongRef.planeNormal
        + ((z + chart.alignHeight) * meter) * basis.normal;
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
 * build a tool sheet per chain across the piece's flat box, split the box by every tool, keep the cells that map
 * back inside the piece, unite them.
 * @returns {map} : { "body" : Query, "report", "text" }
 */
export function rebuildPiece(context is Context, id is Id, chart is map, piece is Query, settings is map) returns map
{
    var report = { "cells" : 0, "keptCells" : 0, "tools" : 0, "planeTools" : 0, "arcTools" : 0, "splineTools" : 0, "ruledTools" : 0,
            "snappedTools" : 0, "squaredWalls" : 0, "maxLean" : 0 };

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
    const extent = chartExtent(context, chart, piece);
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
        var previous = undefined;
        for (var tp in planes)
        {
            const u = unwrapFast(chart, tp.origin, previous);
            previous = u;
            feet = append(feet, u);
            flatGrid = append(flatGrid, [u[0], u[1], u[2]]);
        }
        // Grid points outside the face's trim lie on its surface's extension, which is at worst less of a profile
        // or wall than the face; so only a face that does not pass on the whole grid is re-read on its trim alone.
        var shares = normalShares(context, chart, face, planes, feet, false);
        var kind = faceKind(shares);
        if (kind != "PROFILE" && kind != "WALL")
        {
            shares = normalShares(context, chart, face, planes, feet, true);
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
        rows = append(rows, faceRow(context, chart, face, kind, flatGrid));
    }
    if (size(failed) > 0)
    {
        throw regenError("Unwrap part: " ~ size(failed) ~ " face(s) over the curved part of the reference are neither "
                ~ "extruded across the reference plane nor walls normal to the reference; they cannot be unwrapped exactly.",
            settings.part);
    }

    // 2. Chain tangent rows of one kind, drop duplicates.
    var chains = [];
    for (var kind in ["PROFILE", "WALL", "RULED"])
    {
        chains = concatenateArrays([chains, chainRows(rows, kind)]);
    }
    chains = distinctChains(chains, settings.flatTolerance.value);

    // 3. One tool sheet per chain, across the box.
    for (var ax in [0, 1, 2])
    {
        lo[ax] -= UNWRAP_PART_BOX_MARGIN;
        hi[ax] += UNWRAP_PART_BOX_MARGIN;
    }
    const reach = sqrt((hi[0] - lo[0]) ^ 2 + (hi[1] - lo[1]) ^ 2 + (hi[2] - lo[2]) ^ 2) + 0.01;
    var tools = [];
    for (var i = 0; i < size(chains); i += 1)
    {
        const tool = chainTool(context, id + "tool" + ("t" ~ i), chains[i], lo, hi, reach);
        tools = append(tools, tool.body);
        report[tool.counter] += 1;
        if (chains[i].snapped)
        {
            report.snappedTools += 1;
        }
    }
    report.tools = size(tools);

    // 4. Box, split by every tool.
    fCuboid(context, id + "box", { "corner1" : vector(lo[0], lo[1], lo[2]) * meter, "corner2" : vector(hi[0], hi[1], hi[2]) * meter });
    const cellsQ = qBodyType(qUnion([qCreatedBy(id + "box", EntityType.BODY), qCreatedBy(id + "split", EntityType.BODY)]), BodyType.SOLID);
    for (var i = 0; i < size(tools); i += 1)
    {
        opSplitPart(context, id + "split" + ("s" ~ i), { "targets" : cellsQ, "tool" : tools[i], "keepTools" : true });
    }

    // 5. Keep the cells whose interior maps back inside the piece.
    const cells = evaluateQuery(context, cellsQ);
    var keep = [];
    var drop = [];
    for (var cell in cells)
    {
        const pt = interiorPoint(context, cell) / meter;
        if (isQueryEmpty(context, qContainsPoint(piece, unflatPoint(chart, pt[0], pt[1], pt[2]))))
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
        throw regenError("Unwrap part: no cell of a rebuilt piece mapped back inside the part.", settings.part);
    }
    if (size(drop) > 0)
    {
        opDeleteBodies(context, id + "dropCells", { "entities" : qUnion(drop) });
    }
    opDeleteBodies(context, id + "dropTools", { "entities" : qUnion(tools) });
    if (size(keep) > 1)
    {
        try silent
        {
            opBoolean(context, id + "unite", { "tools" : qUnion(keep), "operationType" : BooleanOperationType.UNION });
        }
        catch
        {
            throw regenError("Unwrap part: the rebuilt cells of a curved piece would not unite "
                    ~ "(tools touching tangentially, e.g. a groove running out onto a face).", settings.part);
        }
    }

    const text = size(faces) ~ " faces, " ~ size(chains) ~ " tools, " ~ size(keep) ~ "/" ~ size(cells) ~ " cells kept";
    return { "body" : qBodyType(qUnion(keep), BodyType.SOLID), "report" : report, "text" : text };
}

/**
 * The piece's extent in the chart frame, [lo, hi] (plain metres), from its edges sampled every 2 mm (at least 9
 * points each). The faces here are extrusions, whose extremes lie on their boundary edges; the box grows by
 * UNWRAP_PART_BOX_MARGIN anyway.
 */
export function chartExtent(context is Context, chart is map, piece is Query) returns array
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
        for (var tl in evEdgeTangentLines(context, { "edge" : edge, "parameters" : parameters }))
        {
            const u = unwrapFast(chart, tl.origin, previous);
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

/**
 * [max |b|, max |c|] of a face's flat normal over its classification grid; onFaceOnly skips grid points outside
 * the face's trim (one evDistance each).
 */
export function normalShares(context is Context, chart is map, face is Query, planes is array, feet is array,
    onFaceOnly is boolean) returns array
{
    var maxB = 0;
    var maxC = 0;
    for (var k = 0; k < size(planes); k += 1)
    {
        if (onFaceOnly && evDistance(context, { "side0" : planes[k].origin, "side1" : face }).distance > UNWRAP_PART_ON_FACE)
        {
            continue;
        }
        const fn = flatNormal(chart, feet[k], planes[k].normal);
        maxB = max(maxB, abs(fn[1]));
        maxC = max(maxC, abs(fn[2]));
    }
    return [maxB, maxC];
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
 * @param flatGrid : chart points of the classification grid (index i * G + j for parameter (i, j) / (G - 1)).
 */
export function faceRow(context is Context, chart is map, face is Query, kind is string, flatGrid is array) returns map
{
    const G = UNWRAP_PART_GRID;
    const ib = (kind == "PROFILE") ? 2 : 1;
    const mid = (G - 1) / 2;
    const u0 = flatGrid[mid];
    const u1 = flatGrid[(G - 1) * G + mid];
    const v0 = flatGrid[mid * G];
    const v1 = flatGrid[mid * G + G - 1];
    const spreadU = sqrt((u1[0] - u0[0]) ^ 2 + (u1[ib] - u0[ib]) ^ 2);
    const spreadV = sqrt((v1[0] - v0[0]) ^ 2 + (v1[ib] - v0[ib]) ^ 2);
    const alongU = spreadU >= spreadV;
    const count = min(max(ceil(max(spreadU, spreadV) / UNWRAP_PART_ROW_SPACING), UNWRAP_PART_ROW_MIN), UNWRAP_PART_ROW_MAX);

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
    const pts = chartPoints(context, chart, face, middle);
    var row = [];
    for (var p in pts)
    {
        row = append(row, [p[0], p[ib]]);
    }
    if (kind == "RULED")
    {
        const b = chartPoints(context, chart, face, bottom);
        const t = chartPoints(context, chart, face, top);
        for (var k = 0; k < count; k += 1)
        {
            row[k] = [row[k][0], row[k][1], vector(b[k][0], b[k][1], b[k][2]), vector(t[k][0], t[k][1], t[k][2])];
        }
    }
    return { "kind" : kind, "pts" : row };
}

/** Chart points [x, y, z] of a face at the given parameters, warm-started along the row. */
export function chartPoints(context is Context, chart is map, face is Query, parameters is array) returns array
{
    var out = [];
    var previous = undefined;
    for (var tp in evFaceTangentPlanes(context, { "face" : face, "parameters" : parameters }))
    {
        const u = unwrapFast(chart, tp.origin, previous);
        previous = u;
        out = append(out, [u[0], u[1], u[2]]);
    }
    return out;
}

function planarDistance(p is array, q is array) returns number
{
    return sqrt((p[0] - q[0]) ^ 2 + (p[1] - q[1]) ^ 2);
}

function planarDirection(p is array, q is array) returns array
{
    const l = planarDistance(p, q);
    return [(q[0] - p[0]) / l, (q[1] - p[1]) / l];
}

function planarDot(a is array, b is array) returns number
{
    return a[0] * b[0] + a[1] * b[1];
}

/**
 * Chain the rows of one kind whose ends meet with tangents agreeing (tangent-continuous neighbours extended
 * separately give near-coincident tools and sliver cells). A chain's points run in one direction.
 */
export function chainRows(rows is array, kind is string) returns array
{
    var pool = [];
    for (var r in rows)
    {
        if (r.kind == kind)
        {
            pool = append(pool, r.pts);
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
                const q = pool[j];
                const n = size(q);
                const cn = size(ch);
                const endDir = planarDirection(ch[cn - 2], ch[cn - 1]);
                const startDir = planarDirection(ch[0], ch[1]);
                var joined = undefined;
                if (planarDistance(ch[cn - 1], q[0]) < UNWRAP_PART_JOIN
                    && planarDot(endDir, planarDirection(q[0], q[1])) > UNWRAP_PART_TANGENT_DOT)
                {
                    joined = concatenateArrays([ch, subArray(q, 1, n)]);
                }
                else if (planarDistance(ch[cn - 1], q[n - 1]) < UNWRAP_PART_JOIN
                    && planarDot(endDir, planarDirection(q[n - 1], q[n - 2])) > UNWRAP_PART_TANGENT_DOT)
                {
                    joined = concatenateArrays([ch, reverse(subArray(q, 0, n - 1))]);
                }
                else if (planarDistance(ch[0], q[n - 1]) < UNWRAP_PART_JOIN
                    && planarDot(startDir, planarDirection(q[n - 2], q[n - 1])) > UNWRAP_PART_TANGENT_DOT)
                {
                    joined = concatenateArrays([subArray(q, 0, n - 1), ch]);
                }
                else if (planarDistance(ch[0], q[0]) < UNWRAP_PART_JOIN
                    && planarDot(startDir, planarDirection(q[1], q[0])) > UNWRAP_PART_TANGENT_DOT)
                {
                    joined = concatenateArrays([reverse(subArray(q, 1, n)), ch]);
                }
                if (joined != undefined)
                {
                    ch = joined;
                    used[j] = true;
                    grew = true;
                }
            }
        }
        chains = append(chains, { "kind" : kind, "pts" : ch });
    }
    return chains;
}

/** Signed distance of p from the line through a and b (planar). */
function lineOffset(a is array, b is array, p is array) returns number
{
    const d = planarDirection(a, b);
    return -(p[0] - a[0]) * d[1] + (p[1] - a[1]) * d[0];
}

/** Distance of p from a polyline (planar). */
function polylineDistance(pts is array, p is array) returns number
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

/** A chain point placed just outside the box: PROFILE (x, z) at y below the box, WALL (x, y) at z below it. */
function toolPoint(p is array, profile is boolean, lo is array) returns Vector
{
    return (profile ? vector(p[0], lo[1] - 0.001, p[1]) : vector(p[0], p[1], lo[2] - 0.001)) * meter;
}

/**
 * The tool sheet of one chain, crossing the whole box. PROFILE: the (x, z) curve just outside the box's -Y face,
 * extruded along Y; WALL: the (x, y) curve under the box, extruded along Z; RULED: the bottom and top rows pushed
 * along their rulings to below and above the box, lofted. Then only the side edges (the rulings) are extended by
 * `reach`: extending every edge fails on curved chains, and a tool that does not cross the box does not split it.
 * @returns {map} : { "body" : Query, "counter" : report key }
 */
export function chainTool(context is Context, id is Id, chain is map, lo is array, hi is array, reach is number) returns map
{
    const sheet = id + "sheet";
    var counter = "splineTools";
    var extrusion = undefined;
    if (chain.kind == "RULED")
    {
        var below = [];
        var above = [];
        for (var p in chain.pts)
        {
            const B = p[2];
            const u = p[3] - B;
            below = append(below, (B + ((lo[2] - 0.001) - B[2]) / u[2] * u) * meter);
            above = append(above, (B + ((hi[2] + 0.001) - B[2]) / u[2] * u) * meter);
        }
        opFitSpline(context, id + "below", { "points" : below });
        opFitSpline(context, id + "above", { "points" : above });
        opLoft(context, sheet, { "profileSubqueries" : [qCreatedBy(id + "below", EntityType.EDGE), qCreatedBy(id + "above", EntityType.EDGE)],
                    "bodyType" : ToolBodyType.SURFACE });
        counter = "ruledTools";
    }
    else
    {
        const profile = chain.kind == "PROFILE";
        const curve = id + "curve";
        counter = "splineTools";
        if (chain.straight)
        {
            emitLineCurve(context, curve, toolPoint(chain.lineStart, profile, lo), toolPoint(chain.lineEnd, profile, lo));
            counter = "planeTools";
        }
        else
        {
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
                opFitSpline(context, curve, { "points" : points });
            }
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
        const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
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
