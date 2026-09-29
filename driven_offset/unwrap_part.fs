FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/extendendtype.gen.fs", version : "3070.0");
import(path : "onshape/std/extendsheetshapetype.gen.fs", version : "3070.0");

// IMPORT: edge_offset_utils.fs (same document; the chart: unwrapFast, packChart, referencePointAtArc; export-imports
// curve_core: classifyPoints, emitArcCurve)
export import(path : "a2665e22c07b7a6929ce4e80", version : "3356bea0c847dcdb94230682");

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
 * straight pieces, rebuild the curved ones, unite.
 *
 * A curved piece is rebuilt by PRISM, the band rebuild (research_unwrap_curved_ref.md 4): every face mapped to a
 * SIDE-view curve (profile faces) and / or a PLAN-view curve (walls); each view's curves cut a flat sheet into
 * cells; (plan cell x Z) INTERSECT (side cell x Y) products tested for membership by the inverse map; plan cells
 * with the same set of solid side cells form a band; each band is one extrude-intersect; bands united. A face that
 * departs from a pure profile / wall by more than the shape tolerance sends the piece to the exact CELL rebuild
 * (rebuildPiece: one tool per chain, a box split by every tool, cells kept by the inverse map; walls leaning by up
 * to UNWRAP_PART_RULED_TOL rebuilt exactly as ruled surfaces, or stood up vertical with squareWalls). Every rebuilt
 * piece is REVERSE CHECKED (result faces mapped back onto the source) before it is accepted.
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
        "snappedTools", "squaredWalls", "approximatedFaces", "fallbackFaces", "bands"];

// ---- PRISM (band rebuild) ----

/** Default shape tolerance: how far a face may depart from a pure side-view extrusion or plan-view wall. */
export const UNWRAP_PART_SHAPE_TOL = 0.005 * millimeter;

/** Classification grid of a PRISM face (G x G parameters, G odd so there is a middle row). */
export const UNWRAP_PART_PRISM_GRID = 5;

/**
 * viewFit: a face whose grid lines make less than this share of their travel along the extrusion direction in both
 * parameter directions (a face facing along it) keeps the old choice of station direction, by spread in the view.
 */
export const UNWRAP_PART_EXTRUSION_SHARE = 0.01;

/** Rows: adaptive (seed every UNWRAP_PART_ROW_SEED_SPACING m, at least UNWRAP_PART_ROW_SEED_MIN, then split every
 * span whose mapped midpoint misses the cubic through its neighbours by more than shapeTolerance *
 * UNWRAP_PART_ROW_REFINE, up to UNWRAP_PART_ROW_PASSES passes), or fixed (UNWRAP_PART_ROW_FIXED_SPACING, at least
 * UNWRAP_PART_ROW_MIN), both capped at UNWRAP_PART_PRISM_ROW_MAX points. */
export const UNWRAP_PART_ADAPTIVE_ROWS = true;
export const UNWRAP_PART_ROW_SEED_SPACING = 8e-3;
export const UNWRAP_PART_ROW_SEED_MIN = 9;
export const UNWRAP_PART_ROW_REFINE = 0.25;
export const UNWRAP_PART_ROW_PASSES = 6;
export const UNWRAP_PART_ROW_FIXED_SPACING = 4e-3;
export const UNWRAP_PART_PRISM_ROW_MAX = 400;

/** "merge" chains are refitted through their exact per-face curves sampled at least this far apart (m). */
export const UNWRAP_PART_MERGE_SPACING = 2.5e-4;

/** A mapped row within shapeTolerance * this of a line / circle is emitted as a line / arc ("keep" faces). */
export const UNWRAP_PART_SHAPE_ARC_SHARE = 0.1;

/** Chain ends meeting end to end are snapped together only when their directions agree to this |cos| (30 deg). */
export const UNWRAP_PART_SNAP_DOT = 0.866;

/** A chain end lying on another chain of its view (within UNWRAP_PART_JOIN) is extended by this (m) so the curves
 * really cross; a free end is extended along its tangent to the box. */
export const UNWRAP_PART_OVERSHOOT = 2e-5;

/** The PRISM box comes from the piece's edges sampled every this (at least 5 points per edge). */
export const UNWRAP_PART_EXTENT_SPACING = 20 * millimeter;

/** Reverse check: result-face parameters mapped back, and the error floor (the limit is max(shapeTolerance, this)). */
export const UNWRAP_PART_REVERSE_PARAMS = [vector(0.125, 0.125), vector(0.125, 0.375), vector(0.125, 0.625), vector(0.125, 0.875),
        vector(0.375, 0.125), vector(0.375, 0.375), vector(0.375, 0.625), vector(0.375, 0.875),
        vector(0.625, 0.125), vector(0.625, 0.375), vector(0.625, 0.625), vector(0.625, 0.875),
        vector(0.875, 0.125), vector(0.875, 0.375), vector(0.875, 0.625), vector(0.875, 0.875)];
export const UNWRAP_PART_REVERSE_FLOOR = 0.01 * millimeter;

/** A face departing from its profile / wall by more than this (m) counts as approximated. */
export const UNWRAP_PART_APPROX_EPS = 1e-7;

/** "Not representable in this view". */
export const UNWRAP_PART_NO_FIT = 1e9;

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
 * @param options {map} : {
 *        "squareWalls" : boolean (default false). PRISM: a leaning wall beyond shapeTolerance is stood up (squared at
 *              mid-height) instead of sending the piece to the exact cell rebuild. Cells: leaning walls squared
 *              instead of ruled.
 *        "faceMode" : "keep" (default: one rebuilt face per source face, a true line / arc where the mapped face
 *              row is one) or "merge" (tangent-connected faces fitted as one spline tool: fewer faces).
 *        "shapeTolerance" : ValueWithUnits (default 0.005 mm): how far a face may depart from a pure side-view
 *              extrusion (profile) or plan-view wall and still be built as its best fit. Beyond it the piece falls
 *              back to the exact cell rebuild.
 *        "flatTolerance" : ValueWithUnits (snap nearly straight chains to lines; default 0.001 mm),
 *        "print" : boolean (accepted; nothing is printed here -- the caller prints "lines") }
 * @returns {map} : { "bodies" : Query (flat solid(s), created under id), "report" : { "pieces", "rigidPieces",
 *        "rebuiltPieces", "prismPieces", "cellPieces", "methods" (per piece: "rigid" / "prism" / "cells"), "bands",
 *        "approximatedFaces", "approximationMax" (ValueWithUnits), "fallbackFaces", "reverseCheckMax"
 *        (ValueWithUnits, rebuilt pieces; rigid pieces are exact), "cells", "keptCells", "tools", "planeTools",
 *        "arcTools", "splineTools", "ruledTools", "snappedTools", "squaredWalls", "maxLean" }, "lines" : array of
 *        strings }
 * Leaves the input part untouched (works on a copy); all temporaries are created under id and deleted.
 * Throws a regenError when a rebuilt piece fails its reverse check (never returns a silently wrong body).
 */
export function unwrapSolid(context is Context, id is Id, chart is map, cs is CoordSystem, part is Query, options is map) returns map
{
    const shapeTolerance = (options.shapeTolerance == undefined) ? UNWRAP_PART_SHAPE_TOL : options.shapeTolerance;
    const settings = {
            "squareWalls" : options.squareWalls == true,
            "flatTolerance" : (options.flatTolerance == undefined) ? 0.001 * millimeter : options.flatTolerance,
            "faceMode" : (options.faceMode == "merge") ? "merge" : "keep",
            "shapeTolerance" : shapeTolerance,
            "arcTolerance" : shapeTolerance * UNWRAP_PART_SHAPE_ARC_SHARE,
            "part" : part,
            "keepFailed" : options.keepFailed == true
        };
    var report = mergeMaps(emptyPartReport(), { "pieces" : 0, "rigidPieces" : 0, "rebuiltPieces" : 0, "prismPieces" : 0,
                "cellPieces" : 0, "methods" : [], "approximationMax" : 0 * meter, "reverseCheckMax" : 0 * meter });
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

    // 2. Straight pieces move rigidly into the chart frame; curved pieces are rebuilt there (PRISM, else cells).
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
            report.methods = append(report.methods, "rigid");
            lines = append(lines, "  piece " ~ k ~ ": over a line, moved rigidly ("
                ~ size(evaluateQuery(context, qOwnedByBody(piece, EntityType.FACE))) ~ " faces kept)");
        }
        else
        {
            const rebuilt = rebuildCurvedPiece(context, id + ("piece" ~ k), chart, piece, settings);
            opDeleteBodies(context, id + ("deletePiece" ~ k), { "entities" : piece });
            results = append(results, rebuilt.body);
            report.rebuiltPieces += 1;
            report.methods = append(report.methods, rebuilt.method);
            report[(rebuilt.method == "prism") ? "prismPieces" : "cellPieces"] += 1;
            for (var key in UNWRAP_PART_COUNT_KEYS)
            {
                report[key] += rebuilt.report[key];
            }
            report.maxLean = max(report.maxLean, rebuilt.report.maxLean);
            report.approximationMax = max(report.approximationMax, rebuilt.report.approximationMax * meter);
            report.reverseCheckMax = max(report.reverseCheckMax, rebuilt.report.reverseCheck * meter);
            lines = concatenateArrays([lines, ["  piece " ~ k ~ ": over a curve, " ~ rebuilt.text], rebuilt.notes]);
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

    var summary = "Unwrap part: " ~ report.pieces ~ " piece(s): " ~ report.rigidPieces ~ " moved rigidly, "
        ~ report.prismPieces ~ " band-rebuilt (" ~ report.bands ~ " bands), " ~ report.cellPieces ~ " cell-rebuilt";
    if (report.rebuiltPieces > 0)
    {
        summary = summary ~ "; reverse check max " ~ roundToPrecision(report.reverseCheckMax / millimeter, 5) ~ " mm; "
            ~ report.approximatedFaces ~ " face(s) approximated (max " ~ roundToPrecision(report.approximationMax / millimeter, 5)
            ~ " mm), " ~ report.fallbackFaces ~ " beyond the shape tolerance; " ~ report.squaredWalls ~ " walls squared";
    }
    lines = concatenateArrays([[summary], lines]);
    return { "bodies" : bodies, "report" : report, "lines" : lines };
}

// ============================================================================
// A curved piece: PRISM, the exact cell rebuild as fallback, the reverse check
// ============================================================================

/**
 * Rebuild one piece lying over a curved span, in the chart frame. PRISM when every face is a profile or wall within
 * the shape tolerance (or a wall to square, with squareWalls) and its result passes the reverse check; otherwise the
 * exact cell rebuild, which must pass the reverse check too. A PRISM attempt runs as a sub-feature and is rolled
 * back when it fails.
 * @returns {map} : { "body" : Query, "method" : "prism" | "cells", "report" (counts, maxLean, approximationMax and
 *      reverseCheck in plain metres), "text", "notes" : array of strings }
 */
export function rebuildCurvedPiece(context is Context, id is Id, chart is map, piece is Query, settings is map) returns map
{
    const analysis = prismAnalysis(context, chart, piece, settings);
    const limit = max(settings.shapeTolerance, UNWRAP_PART_REVERSE_FLOOR).value + analysis.squaredMax;
    var notes = [];
    var reason = undefined;
    if (size(analysis.fallback) == 0)
    {
        const pid = id + "prism";
        startFeature(context, pid, {});
        var built = undefined;
        try silent
        {
            built = prismPiece(context, pid, chart, piece, analysis, settings);
        }
        catch (e)
        {
            reason = "the band rebuild failed (" ~ errorText(e) ~ ")";
        }
        if (built != undefined)
        {
            const check = reverseCheck(context, chart, built.body, piece);
            if (check.distance <= limit)
            {
                endFeature(context, pid);
                var report = mergeMaps(emptyPartReport(), analysis.report);
                report.bands = built.bands;
                report.tools = built.tools;
                report.planeTools = built.counts.lines;
                report.arcTools = built.counts.arcs;
                report.splineTools = built.counts.splines;
                report.snappedTools = built.snapped;
                report.approximationMax = analysis.approximationMax;
                report.reverseCheck = check.distance;
                return { "body" : built.body, "method" : "prism", "report" : report, "notes" : built.notes,
                        "text" : "band rebuild: " ~ built.text ~ "; " ~ analysis.text ~ "; reverse check "
                            ~ roundToPrecision(check.distance * 1000, 5) ~ " mm on " ~ check.samples ~ " samples" };
            }
            reason = "the band rebuild missed the source by " ~ roundToPrecision(check.distance * 1000, 4) ~ " mm at flat "
                ~ flatText(check.where) ~ " (limit " ~ roundToPrecision(limit * 1000, 4) ~ " mm)";
            if (settings.keepFailed)
            {
                println("KEEPFAIL " ~ reason);
                endFeature(context, pid);
                return { "body" : built.body, "method" : "prism", "report" : mergeMaps(emptyPartReport(), { "reverseCheck" : check.distance, "approximationMax" : 0 }), "notes" : [], "text" : "KEPT FAILED " ~ reason };
            }
        }
        abortFeature(context, pid);
    }
    else
    {
        reason = size(analysis.fallback) ~ " face(s) depart from a side-view extrusion and a plan-view wall by more than "
            ~ "the shape tolerance (" ~ roundToPrecision(settings.shapeTolerance / millimeter, 4) ~ " mm; worst "
            ~ ((analysis.fallbackMax >= UNWRAP_PART_NO_FIT) ? "neither" : (roundToPrecision(analysis.fallbackMax * 1000, 4) ~ " mm")) ~ ")";
    }
    notes = append(notes, "    PRISM not used: " ~ reason ~ "; exact cell rebuild instead");

    // The exact fallback.
    var cells = undefined;
    var failure = undefined;
    try silent
    {
        cells = rebuildPiece(context, id + "cells", chart, piece, settings);
    }
    catch (e)
    {
        failure = errorText(e);
    }
    const highlight = (size(analysis.fallback) > 0) ? sourceFaces(context, analysis.fallback, settings.part) : settings.part;
    if (cells == undefined)
    {
        throw regenError("Unwrap part: a curved piece of " ~ partName(context, settings.part) ~ " cannot be unwrapped: " ~ reason
                ~ ", and the exact cell rebuild failed too (" ~ failure ~ ").", highlight);
    }
    const check = reverseCheck(context, chart, cells.body, piece);
    if (check.distance > limit)
    {
        throw regenError("Unwrap part: the rebuilt " ~ partName(context, settings.part) ~ " misses the source by "
                ~ roundToPrecision(check.distance * 1000, 4) ~ " mm (limit " ~ roundToPrecision(limit * 1000, 4) ~ " mm) at flat "
                ~ flatText(check.where) ~ " (reverse check of the cell rebuild, after: " ~ reason ~ ").", highlight);
    }
    var report = cells.report;
    report.fallbackFaces = size(analysis.fallback);
    report.approximatedFaces = 0;
    report.bands = 0;
    report.approximationMax = 0;
    report.reverseCheck = check.distance;
    return { "body" : cells.body, "method" : "cells", "report" : report, "notes" : notes,
            "text" : "cell rebuild: " ~ cells.text ~ "; reverse check " ~ roundToPrecision(check.distance * 1000, 5) ~ " mm on "
                ~ check.samples ~ " samples" };
}

/**
 * Reverse check: points of every face of `body` (flat, chart frame) mapped back with unwrapInverse, and their
 * distance to the source's faces. Catches extra material and holes (a hole adds faces where the source has none).
 * @returns {map} : { "distance" (plain metres, worst), "where" (flat point of the worst, plain metres), "samples" }
 */
export function reverseCheck(context is Context, chart is map, body is Query, source is Query) returns map
{
    const sourceFacesQ = qOwnedByBody(source, EntityType.FACE);
    var worst = 0;
    var where = undefined;
    var samples = 0;
    for (var face in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)))
    {
        for (var tp in evFaceTangentPlanes(context, { "face" : face, "parameters" : UNWRAP_PART_REVERSE_PARAMS,
                        "returnUndefinedOutsideFace" : true }))
        {
            if (tp == undefined)
            {
                continue;
            }
            const q = tp.origin / meter;
            const w = unwrapInverse(chart, q[0], q[1], q[2]);
            const d = evDistance(context, { "side0" : vector(w[0], w[1], w[2]) * meter, "side1" : sourceFacesQ }).distance / meter;
            samples += 1;
            if (d > worst)
            {
                worst = d;
                where = q;
            }
        }
    }
    return { "distance" : worst, "where" : where, "samples" : samples };
}

/** "x, y, z mm" of a flat point in plain metres ("-" when undefined). */
export function flatText(q) returns string
{
    if (q == undefined)
    {
        return "-";
    }
    return roundToPrecision(q[0] * 1000, 2) ~ ", " ~ roundToPrecision(q[1] * 1000, 2) ~ ", " ~ roundToPrecision(q[2] * 1000, 3) ~ " mm";
}

/** The message of a caught error (a regenError's custom message, else its message enum, else the value). */
export function errorText(e) returns string
{
    if (e is map)
    {
        if (e.customMessage != undefined)
        {
            return e.customMessage;
        }
        if (e.message != undefined)
        {
            return toString(e.message);
        }
    }
    return toString(e);
}

/**
 * The source part's faces under the given faces of a working copy (the copy is gone when an error is shown): the
 * source face containing a point of each.
 */
export function sourceFaces(context is Context, faces is array, part is Query) returns Query
{
    const partFaces = qOwnedByBody(part, EntityType.FACE);
    var found = [];
    for (var face in faces)
    {
        const tp = evFaceTangentPlane(context, { "face" : face, "parameter" : vector(0.5, 0.5) });
        found = append(found, qContainsPoint(partFaces, tp.origin));
    }
    return qUnion(found);
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

/**
 * How messages name the part. Not its name: getProperty cannot run in a feature body (correction 36) -- it threw
 * inside the error messages that used it, so a refusal surfaced as a getProperty warning and its own text was lost
 * (4103 over FULL_BASELINE, 2026-09-29). The part is highlighted instead.
 */
export function partName(context is Context, part is Query) returns string
{
    return "the part";
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
    return chartExtentSampled(context, chart, piece, part, 2 * millimeter, 9);
}

/** As above, sampling every `spacing` with at least `minimum` points per edge. */
export function chartExtentSampled(context is Context, chart is map, piece is Query, part is Query, spacing is ValueWithUnits,
    minimum is number) returns array
{
    var lo = [1e9, 1e9, 1e9];
    var hi = [-1e9, -1e9, -1e9];
    for (var edge in evaluateQuery(context, qOwnedByBody(piece, EntityType.EDGE)))
    {
        const count = min(max(ceil(evLength(context, { "entities" : edge }) / spacing) + 1, minimum), 200);
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
        chains = append(chains, { "kind" : kind, "pts" : ch.pts, "e0" : ch.e0, "e1" : ch.e1, "faces" : ch.faces, "parts" : ch.parts,
                    "envelope" : ch.envelope == true });
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
    var how = 0;
    if (planarDistance(pts[cn - 1], q[0]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e1, row.e0, endDir, planarDirection(q[0], q[1])))
    {
        joined = { "pts" : concatenateArrays([pts, subArray(q, 1, n)]), "e0" : ch.e0, "e1" : row.e1 };
        how = 1;
    }
    else if (planarDistance(pts[cn - 1], q[n - 1]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e1, row.e1, endDir, planarDirection(q[n - 1], q[n - 2])))
    {
        joined = { "pts" : concatenateArrays([pts, reverse(subArray(q, 0, n - 1))]), "e0" : ch.e0, "e1" : row.e0 };
        how = 2;
    }
    else if (planarDistance(pts[0], q[n - 1]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e0, row.e1, startDir, planarDirection(q[n - 2], q[n - 1])))
    {
        joined = { "pts" : concatenateArrays([subArray(q, 0, n - 1), pts]), "e0" : row.e0, "e1" : ch.e1 };
        how = 3;
    }
    else if (planarDistance(pts[0], q[0]) < UNWRAP_PART_JOIN
        && endsTangent(ch.e0, row.e0, startDir, planarDirection(q[1], q[0])))
    {
        joined = { "pts" : concatenateArrays([reverse(subArray(q, 1, n)), pts]), "e0" : row.e1, "e1" : ch.e1 };
        how = 4;
    }
    if (joined != undefined)
    {
        joined.kind = ch.kind;
        joined.faces = concatenateArrays([ch.faces, row.faces]);
        joined.envelope = ch.envelope == true || row.envelope == true;
        if (ch.parts != undefined && row.parts != undefined)
        {
            // The per-face parts in chain order (PRISM "keep" faces), for the same four cases.
            if (how == 1)
            {
                joined.parts = concatenateArrays([ch.parts, row.parts]);
            }
            else if (how == 2)
            {
                joined.parts = concatenateArrays([ch.parts, reversedParts(row.parts)]);
            }
            else if (how == 3)
            {
                joined.parts = concatenateArrays([row.parts, ch.parts]);
            }
            else
            {
                joined.parts = concatenateArrays([reversedParts(row.parts), ch.parts]);
            }
        }
    }
    return joined;
}

/** Per-face parts of a row or chain run backwards: order reversed, each part's points reversed, its end normals swapped. */
export function reversedParts(parts is array) returns array
{
    var out = [];
    for (var k = size(parts) - 1; k >= 0; k -= 1)
    {
        const part = parts[k];
        out = append(out, { "pts" : reverse(part.pts), "n0" : part.n1, "n1" : part.n0, "face" : part.face });
    }
    return out;
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

// ============================================================================
// PRISM (band rebuild): face analysis
// ============================================================================

/** The PRISM classification grid: G x G face parameters, index i * G + j for parameter (i, j) / (G - 1). */
export function prismGrid() returns array
{
    const G = UNWRAP_PART_PRISM_GRID;
    var grid = [];
    for (var i = 0; i < G; i += 1)
    {
        for (var j = 0; j < G; j += 1)
        {
            grid = append(grid, vector(i / (G - 1), j / (G - 1)));
        }
    }
    return grid;
}

/**
 * Chart points [x, y, z] and flat normals [a, b, c] of a face on the PRISM grid (undefined where there is none).
 * onFace false: every grid point, the surface's extension included; a point the chart cannot map is counted in
 * "misses". onFace true: only the points on the face's trim, and one the chart cannot map is an error on the face.
 * @returns {map} : { "flat", "normals", "misses" }
 */
export function prismFaceSamples(context is Context, chart is map, face is Query, onFace is boolean, part is Query) returns map
{
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : prismGrid(), "returnUndefinedOutsideFace" : onFace });
    var flat = makeArray(size(planes));
    var normals = makeArray(size(planes));
    var misses = 0;
    var previous = undefined;
    for (var k = 0; k < size(planes); k += 1)
    {
        const tp = planes[k];
        if (tp == undefined)
        {
            continue;
        }
        const u = chartFootAt(chart, tp.origin, previous);
        if (!chartFootConverged(u))
        {
            if (onFace)
            {
                throw regenError("Unwrap part: a face of " ~ partName(context, part)
                        ~ " reaches past the reference's centre of curvature (the chart has no foot there).", face);
            }
            misses += 1;
            continue;
        }
        previous = u;
        flat[k] = [u[0], u[1], u[2]];
        normals[k] = flatNormal(chart, u, tp.normal);
    }
    return { "flat" : flat, "normals" : normals, "misses" : misses };
}

/** Grid index of station s along the curve direction and t across it (alongU: the curve runs along parameter u). */
export function gridIndex(s is number, t is number, alongU is boolean) returns number
{
    return alongU ? s * UNWRAP_PART_PRISM_GRID + t : t * UNWRAP_PART_PRISM_GRID + s;
}

/** Longest polyline, in the view plane (x, coordinate ib), of the grid lines running along u (alongU) or v. */
export function gridSpread(flat is array, alongU is boolean, ib is number) returns number
{
    const G = UNWRAP_PART_PRISM_GRID;
    var best = 0;
    for (var t = 0; t < G; t += 1)
    {
        var length = 0;
        var last = undefined;
        for (var s = 0; s < G; s += 1)
        {
            const p = flat[gridIndex(s, t, alongU)];
            if (p == undefined)
            {
                continue;
            }
            if (last != undefined)
            {
                length += sqrt((p[0] - last[0]) ^ 2 + (p[ib] - last[ib]) ^ 2);
            }
            last = p;
        }
        best = max(best, length);
    }
    return best;
}

/**
 * Which share of their travel the grid lines running along u (alongU) or v make along one flat coordinate: the summed
 * |change of that coordinate| over the summed chord length (0 when they do not move).
 */
export function gridExtrusionShare(flat is array, alongU is boolean, coordinate is number) returns number
{
    const G = UNWRAP_PART_PRISM_GRID;
    var along = 0;
    var total = 0;
    for (var t = 0; t < G; t += 1)
    {
        var last = undefined;
        for (var s = 0; s < G; s += 1)
        {
            const p = flat[gridIndex(s, t, alongU)];
            if (p == undefined)
            {
                continue;
            }
            if (last != undefined)
            {
                along += abs(p[coordinate] - last[coordinate]);
                total += sqrt((p[0] - last[0]) ^ 2 + (p[1] - last[1]) ^ 2 + (p[2] - last[2]) ^ 2);
            }
            last = p;
        }
    }
    return (total > 1e-12) ? along / total : 0;
}

/**
 * How far a face departs from a pure extrusion in one view: the side view (ib = 2, the image extruded along flat Y,
 * a PROFILE) or the plan view (ib = 1, extruded along flat Z, a WALL). The curve direction is the grid direction
 * whose travel is least along the extrusion direction (by spread in the view when neither travels along it). At each grid station along it, the points across the face are measured along the view
 * normal of the middle one (so parameter slip along the curve does not count): the best-fit tool passes through the
 * middle of their range, and the face departs from it by half the range. The tool row is the face's middle row
 * shifted by "mids" (interpolated along the row); an envelope row by "outs" (the outermost point of each station).
 * @returns {map} : { "dev" (plain metres; UNWRAP_PART_NO_FIT when the face has no normal in the view plane), "mids",
 *      "outs" (per station, plain metres along the outward view normal), "alongU", "spread" (the curve's length) }
 */
export function viewFit(samples is map, ib is number) returns map
{
    const G = UNWRAP_PART_PRISM_GRID;
    const flat = samples.flat;
    const normals = samples.normals;
    const spreadU = gridSpread(flat, true, ib);
    const spreadV = gridSpread(flat, false, ib);
    // The points across a station must run along the extrusion direction (flat Y for a profile, Z for a wall), so the
    // stations run along the grid direction that moves least in it. Choosing by the spread in the view instead let a
    // small, nearly square face skewed in plan pass as a profile, measured along its height: 4103's tip cap over
    // FULL_BASELINE (flat normal y share 0.29) came out a vertical wall and missed its lean by 0.083 mm.
    const extrusion = (ib == 2) ? 1 : 2;
    const moveU = gridExtrusionShare(flat, true, extrusion);
    const moveV = gridExtrusionShare(flat, false, extrusion);
    const alongU = (max(moveU, moveV) > UNWRAP_PART_EXTRUSION_SHARE) ? (moveV >= moveU) : (spreadU >= spreadV);
    const noFit = { "dev" : UNWRAP_PART_NO_FIT, "mids" : makeArray(G, 0), "outs" : makeArray(G, 0), "alongU" : alongU,
        "spread" : max(spreadU, spreadV) };
    const m = (G - 1) / 2;
    var dev = 0;
    var mids = makeArray(G);
    var outs = makeArray(G);
    var stations = 0;
    for (var s = 0; s < G; s += 1)
    {
        // The defined point across the face nearest its middle.
        var ref = undefined;
        for (var d = 0; d <= m; d += 1)
        {
            for (var t in [m - d, m + d])
            {
                if (ref == undefined && flat[gridIndex(s, t, alongU)] != undefined)
                {
                    ref = gridIndex(s, t, alongU);
                }
            }
        }
        if (ref == undefined)
        {
            continue;
        }
        const nx = normals[ref][0];
        const nc = normals[ref][ib];
        const len = sqrt(nx * nx + nc * nc);
        if (len < 0.5)
        {
            return noFit;
        }
        var count = 0;
        var low = 0;
        var high = 0;
        for (var t = 0; t < G; t += 1)
        {
            const p = flat[gridIndex(s, t, alongU)];
            if (p == undefined)
            {
                continue;
            }
            count += 1;
            const offset = ((p[0] - flat[ref][0]) * nx + (p[ib] - flat[ref][ib]) * nc) / len;
            low = min(low, offset);
            high = max(high, offset);
        }
        if (count >= 2)
        {
            stations += 1;
            dev = max(dev, 0.5 * (high - low));
            mids[s] = 0.5 * (high + low);
            outs[s] = high;
        }
    }
    if (stations == 0)
    {
        return noFit;
    }
    return { "dev" : dev, "mids" : filledStations(mids), "outs" : filledStations(outs), "alongU" : alongU,
            "spread" : max(spreadU, spreadV) };
}

/** Per-station values with the missing ones taken from the nearest station that has one (0 when none has). */
export function filledStations(values is array) returns array
{
    var out = values;
    for (var s = 0; s < size(values); s += 1)
    {
        if (values[s] != undefined)
        {
            continue;
        }
        out[s] = 0;
        for (var d = 1; d < size(values); d += 1)
        {
            if (s - d >= 0 && values[s - d] != undefined)
            {
                out[s] = values[s - d];
                break;
            }
            if (s + d < size(values) && values[s + d] != undefined)
            {
                out[s] = values[s + d];
                break;
            }
        }
    }
    return out;
}

/** A per-station value (stations at s / (G - 1)) interpolated linearly at row fraction f. */
export function stationValue(values is array, f is number) returns number
{
    const x = min(max(f, 0), 1) * (size(values) - 1);
    const s = min(floor(x), size(values) - 2);
    return values[s] + (x - s) * (values[s + 1] - values[s]);
}

/** The unit view-plane normal [x, coordinate ib] at a mapped face point (u: its unwrapFast result, n: world normal). */
export function viewNormal(chart is map, u is array, n is Vector, ib is number) returns array
{
    const fn = flatNormal(chart, u, n);
    const len = sqrt(fn[0] * fn[0] + fn[ib] * fn[ib]);
    return (len > 1e-9) ? [fn[0] / len, fn[ib] / len] : [0, 0];
}

/** Largest |component ib| of the defined flat normals. */
export function normalShareMax(normals is array, ib is number) returns number
{
    var best = 0;
    for (var n in normals)
    {
        if (n != undefined)
        {
            best = max(best, abs(n[ib]));
        }
    }
    return best;
}

/**
 * PRISM's reading of a piece: every face measured as a profile (side view) and as a wall (plan view) on the PRISM
 * grid (re-read on its trim only when the whole grid does not pass), and given a row in each view it fits within the
 * shape tolerance. A steep (x-facing) profile face also gets a plan row at its outward envelope. With squareWalls a
 * wall beyond the tolerance but leaning less than UNWRAP_PART_RULED_TOL is squared. Any other face is a fallback face.
 * @returns {map} : { "rows", "fallback" (faces), "fallbackMax", "squaredMax", "approximationMax" (plain metres),
 *      "report" : { "approximatedFaces", "squaredWalls", "maxLean" }, "text" }
 */
export function prismAnalysis(context is Context, chart is map, piece is Query, settings is map) returns map
{
    const tol = settings.shapeTolerance / meter;
    const refine = tol * UNWRAP_PART_ROW_REFINE;
    var rows = [];
    var fallback = [];
    var fallbackMax = 0;
    var squaredMax = 0;
    var approximationMax = 0;
    var approximated = 0;
    var squared = 0;
    var maxLean = 0;
    var profiles = 0;
    var walls = 0;
    var both = 0;
    var envelopes = 0;
    const faces = evaluateQuery(context, qOwnedByBody(piece, EntityType.FACE));
    for (var face in faces)
    {
        var samples = prismFaceSamples(context, chart, face, false, settings.part);
        var P = viewFit(samples, 2);
        var W = viewFit(samples, 1);
        if (samples.misses > 0 || min(P.dev, W.dev) > tol)
        {
            samples = prismFaceSamples(context, chart, face, true, settings.part);
            P = viewFit(samples, 2);
            W = viewFit(samples, 1);
        }
        const cMax = normalShareMax(samples.normals, 2);
        const profileOK = P.dev <= tol;
        var wallOK = W.dev <= tol;
        var squaredHere = false;
        if (!profileOK && !wallOK && settings.squareWalls && W.dev < UNWRAP_PART_NO_FIT && cMax < UNWRAP_PART_RULED_TOL)
        {
            wallOK = true;
            squaredHere = true;
        }
        if (!profileOK && !wallOK)
        {
            fallback = append(fallback, face);
            fallbackMax = max(fallbackMax, min(P.dev, W.dev));
            continue;
        }
        var dev = 0;
        if (profileOK)
        {
            rows = append(rows, prismRow(context, chart, face, "PROFILE", P, false, refine, settings.part));
            dev = P.dev;
            profiles += 1;
        }
        if (wallOK)
        {
            rows = append(rows, prismRow(context, chart, face, "WALL", W, false, refine, settings.part));
            dev = max(dev, W.dev);
            walls += 1;
            maxLean = max(maxLean, cMax);
            if (profileOK)
            {
                both += 1;
            }
        }
        else if (W.dev < UNWRAP_PART_NO_FIT)
        {
            // A profile face facing along x (its flat normal at least half along x: 4802's tilted nose, 4103's tail
            // cap) may be the only thing closing the plan view there. Its plan row lies on its outward envelope: the
            // side view carves the face exactly, and the plan cell beyond the row holds no material. (A fixed
            // "steep" cutoff missed 4103's tail cap on FULL_BASELINE at c = 0.0502: 5.1 mm of extra material,
            // caught by the reverse check.)
            rows = append(rows, prismRow(context, chart, face, "WALL", W, true, refine, settings.part));
            envelopes += 1;
        }
        if (squaredHere)
        {
            squared += 1;
            squaredMax = max(squaredMax, W.dev);
        }
        if (dev > UNWRAP_PART_APPROX_EPS)
        {
            approximated += 1;
            approximationMax = max(approximationMax, dev);
        }
    }
    const text = size(faces) ~ " faces: " ~ profiles ~ " profile, " ~ walls ~ " wall (" ~ both ~ " both), " ~ envelopes
        ~ " envelope, " ~ approximated ~ " approximated (max " ~ roundToPrecision(approximationMax * 1000, 5) ~ " mm), "
        ~ squared ~ " squared, " ~ size(fallback) ~ " beyond tolerance";
    return { "rows" : rows, "fallback" : fallback, "fallbackMax" : fallbackMax, "squaredMax" : squaredMax,
            "approximationMax" : approximationMax, "text" : text,
            "report" : { "approximatedFaces" : approximated, "squaredWalls" : squared, "maxLean" : maxLean } };
}

/** Face parameters of a row: (f, 0.5) along u, (0.5, f) along v. */
export function rowParameters(fractions is array, alongU is boolean) returns array
{
    var out = [];
    for (var f in fractions)
    {
        out = append(out, alongU ? vector(f, 0.5) : vector(0.5, f));
    }
    return out;
}

/** unwrapFast of a face point, warm-started, retried cold, refused with the face highlighted. */
export function checkedFaceFoot(context is Context, chart is map, point is Vector, previous, face is Query, part is Query) returns array
{
    const u = chartFootAt(chart, point, previous);
    if (!chartFootConverged(u))
    {
        throw regenError("Unwrap part: a face of " ~ partName(context, part)
                ~ " reaches past the reference's centre of curvature (the chart has no foot there).", face);
    }
    return u;
}

/**
 * How far the mapped midpoint of span j misses the cubic Hermite through the span's ends (slopes from the
 * neighbours, one-sided at the ends), in the view plane (x, coordinate ib). Plain metres.
 */
export function rowMidMiss(fs is array, feet is array, j is number, mid is array, ib is number) returns number
{
    const n = size(fs);
    const h = fs[j + 1] - fs[j];
    var miss = 0;
    for (var c in [0, ib])
    {
        const p0 = feet[j][c];
        const p1 = feet[j + 1][c];
        const m0 = (j > 0) ? (feet[j + 1][c] - feet[j - 1][c]) / (fs[j + 1] - fs[j - 1]) : (p1 - p0) / h;
        const m1 = (j + 2 < n) ? (feet[j + 2][c] - feet[j][c]) / (fs[j + 2] - fs[j]) : (p1 - p0) / h;
        miss = max(miss, abs(mid[c] - (0.5 * (p0 + p1) + h / 8 * (m0 - m1))));
    }
    return miss;
}

/**
 * One PRISM row: points along the face's curve direction (fit.alongU) at the middle of the other parameter, mapped
 * to the chart, in the view's plane ((x, z) for a PROFILE, (x, y) for a WALL). Adaptive (UNWRAP_PART_ADAPTIVE_ROWS):
 * a coarse seed, then every span whose mapped midpoint misses the cubic through its neighbours by more than
 * `tolerance` (plain metres) is split. Each point is then moved along its view normal by the face's best-fit shift
 * (fit.mids) or, for an `envelope` row, to the face's outer envelope (fit.outs), interpolated between grid stations.
 * @returns {map} : a row for chainRows: { "kind", "pts", "e0", "e1", "faces", "parts" }
 */
export function prismRow(context is Context, chart is map, face is Query, kind is string, fit is map, envelope is boolean,
    tolerance is number, part is Query) returns map
{
    const ib = (kind == "PROFILE") ? 2 : 1;
    const adaptive = UNWRAP_PART_ADAPTIVE_ROWS;
    var n = adaptive
        ? max(ceil(fit.spread / UNWRAP_PART_ROW_SEED_SPACING) + 1, UNWRAP_PART_ROW_SEED_MIN)
        : max(ceil(fit.spread / UNWRAP_PART_ROW_FIXED_SPACING) + 1, UNWRAP_PART_ROW_MIN);
    n = min(n, UNWRAP_PART_PRISM_ROW_MAX);
    var fs = [];
    for (var k = 0; k < n; k += 1)
    {
        fs = append(fs, k / (n - 1));
    }
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : rowParameters(fs, fit.alongU) });
    var feet = [];
    var normals = [];
    var previous = undefined;
    for (var tp in planes)
    {
        previous = checkedFaceFoot(context, chart, tp.origin, previous, face, part);
        feet = append(feet, previous);
        normals = append(normals, viewNormal(chart, previous, tp.normal, ib));
    }
    const last = size(fs) - 1;
    const nStart = flatNormal(chart, feet[0], planes[0].normal);
    const nEnd = flatNormal(chart, feet[last], planes[last].normal);

    if (adaptive)
    {
        var open = makeArray(last, true);
        for (var pass = 0; pass < UNWRAP_PART_ROW_PASSES; pass += 1)
        {
            var spans = [];
            for (var j = 0; j + 1 < size(fs); j += 1)
            {
                if (open[j])
                {
                    spans = append(spans, j);
                }
            }
            if (size(spans) == 0 || size(fs) + size(spans) > UNWRAP_PART_PRISM_ROW_MAX)
            {
                break;
            }
            var mids = [];
            for (var j in spans)
            {
                mids = append(mids, 0.5 * (fs[j] + fs[j + 1]));
            }
            const midPlanes = evFaceTangentPlanes(context, { "face" : face, "parameters" : rowParameters(mids, fit.alongU) });
            var newFs = [];
            var newFeet = [];
            var newNormals = [];
            var newOpen = [];
            var k = 0;
            for (var j = 0; j < size(fs); j += 1)
            {
                newFs = append(newFs, fs[j]);
                newFeet = append(newFeet, feet[j]);
                newNormals = append(newNormals, normals[j]);
                if (j + 1 == size(fs))
                {
                    break;
                }
                if (k < size(spans) && spans[k] == j)
                {
                    const mid = checkedFaceFoot(context, chart, midPlanes[k].origin, feet[j], face, part);
                    const miss = rowMidMiss(fs, feet, j, mid, ib);
                    k += 1;
                    if (miss > tolerance)
                    {
                        newOpen = append(newOpen, true);
                        newFs = append(newFs, mids[k - 1]);
                        newFeet = append(newFeet, mid);
                        newNormals = append(newNormals, viewNormal(chart, mid, midPlanes[k - 1].normal, ib));
                        newOpen = append(newOpen, true);
                        continue;
                    }
                }
                newOpen = append(newOpen, false);
            }
            fs = newFs;
            feet = newFeet;
            normals = newNormals;
            open = newOpen;
        }
    }

    const n0 = [nStart[0], nStart[ib]];
    const n1 = [nEnd[0], nEnd[ib]];
    // The best-fit (or envelope) shift of each station, along each point's own view normal.
    const shifts = envelope ? fit.outs : fit.mids;
    var pts = [];
    for (var k = 0; k < size(feet); k += 1)
    {
        const d = stationValue(shifts, fs[k]);
        pts = append(pts, [feet[k][0] + d * normals[k][0], feet[k][ib] + d * normals[k][1]]);
    }
    return { "kind" : kind, "pts" : pts, "e0" : { "n" : n0 }, "e1" : { "n" : n1 }, "faces" : [face], "envelope" : envelope,
            "parts" : [{ "pts" : pts, "n0" : n0, "n1" : n1, "face" : face }] };
}

// ============================================================================
// PRISM: the two views, membership, bands
// ============================================================================

/** [xmin, xmax, cmin, cmax] of 2D points. */
export function chainBox(pts is array) returns array
{
    var b = [1e9, -1e9, 1e9, -1e9];
    for (var p in pts)
    {
        b = [min(b[0], p[0]), max(b[1], p[0]), min(b[2], p[1]), max(b[3], p[1])];
    }
    return b;
}

/** Whether a 2D point lies within tol of a chain (its line when straight, else its polyline; box prefilter). */
export function nearChain(ch is map, p is array, tol is number) returns boolean
{
    if (ch.straight)
    {
        return polylineDistance([ch.lineStart, ch.lineEnd], p) < tol;
    }
    const b = ch.bbx;
    if (p[0] < b[0] - tol || p[0] > b[1] + tol || p[1] < b[2] - tol || p[1] > b[3] + tol)
    {
        return false;
    }
    return polylineDistance(ch.pts, p) < tol;
}

/**
 * distinctChains for one view, with a bounding box per chain ("bbx") to prefilter the duplicate test: marks straight
 * chains (lineStart / lineEnd moved to the middle of their band) and drops chains lying on an earlier one.
 */
export function distinctViewChains(chains is array, flatTolerance is number) returns array
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
        c.bbx = chainBox(pts);
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
        var duplicate = false;
        for (var k in kept)
        {
            var all = true;
            for (var p in [pts[0], pts[n - 1], pts[floor(n / 2)]])
            {
                if (!nearChain(k, p, UNWRAP_PART_DUPLICATE))
                {
                    all = false;
                    break;
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

/** A chain run backwards: points, ends and per-face parts. */
export function reversedChain(chain is map) returns map
{
    var ch = chain;
    ch.pts = reverse(chain.pts);
    ch.e0 = chain.e1;
    ch.e1 = chain.e0;
    if (chain.parts != undefined)
    {
        ch.parts = reversedParts(chain.parts);
    }
    return ch;
}

/**
 * Chains of one view that OVERLAP (a's last end lies on b, b's first end lies on a, and neither lies wholly on the
 * other) joined into one: a up to where b begins, then b. Rows run over their face's parameter box, which can reach
 * past the face's trim, so two faces of one smooth profile can overlap by millimetres in the view (4401's top: 1.3
 * mm). Left apart, each end dangles along the other curve without crossing it and the outline leaks through the
 * micron-thin gap between them (measured: 4401's side view stayed one cell).
 */
export function mergeOverlappingChains(chains is array, tol is number) returns array
{
    var pool = chains;
    var merged = true;
    while (merged)
    {
        merged = false;
        var boxes = [];
        for (var ch in pool)
        {
            boxes = append(boxes, chainBox(ch.pts));
        }
        for (var i = 0; i < size(pool) && !merged; i += 1)
        {
            for (var j = 0; j < size(pool) && !merged; j += 1)
            {
                if (i == j || !boxesOverlap(boxes[i], boxes[j], tol))
                {
                    continue;
                }
                for (var flips in [[false, false], [false, true], [true, false], [true, true]])
                {
                    const a = flips[0] ? reversedChain(pool[i]) : pool[i];
                    const b = flips[1] ? reversedChain(pool[j]) : pool[j];
                    const aFirst = a.pts[0];
                    const aLast = a.pts[size(a.pts) - 1];
                    const bFirst = b.pts[0];
                    const bLast = b.pts[size(b.pts) - 1];
                    if (planarDistance(aLast, bFirst) > tol && nearBoxedPolyline(b.pts, boxes[j], aLast, tol)
                        && nearBoxedPolyline(a.pts, boxes[i], bFirst, tol) && !nearBoxedPolyline(b.pts, boxes[j], aFirst, tol)
                        && !nearBoxedPolyline(a.pts, boxes[i], bLast, tol))
                    {
                        const joined = overlapJoin(a, b);
                        var next = [];
                        for (var k = 0; k < size(pool); k += 1)
                        {
                            if (k == i)
                            {
                                next = append(next, joined);
                            }
                            else if (k != j)
                            {
                                next = append(next, pool[k]);
                            }
                        }
                        pool = next;
                        merged = true;
                        break;
                    }
                }
            }
        }
    }
    return pool;
}

/** Whether two [xmin, xmax, cmin, cmax] boxes overlap within tol. */
export function boxesOverlap(a is array, b is array, tol is number) returns boolean
{
    return a[0] <= b[1] + tol && b[0] <= a[1] + tol && a[2] <= b[3] + tol && b[2] <= a[3] + tol;
}

/** Whether p lies within tol of a polyline whose box is given. */
export function nearBoxedPolyline(pts is array, bx is array, p is array, tol is number) returns boolean
{
    if (p[0] < bx[0] - tol || p[0] > bx[1] + tol || p[1] < bx[2] - tol || p[1] > bx[3] + tol)
    {
        return false;
    }
    return polylineDistance(pts, p) < tol;
}

/**
 * a (up to the point nearest b's first point) followed by b, for mergeOverlappingChains. The per-face parts follow:
 * a's parts past the cut are dropped, the cut one ends at b's first point.
 */
export function overlapJoin(a is map, b is map) returns map
{
    const cut = b.pts[0];
    // The segment of a nearest the cut: a keeps its points up to that segment's start.
    var best = 1e9;
    var m = 0;
    for (var k = 0; k + 1 < size(a.pts); k += 1)
    {
        const d = polylineDistance([a.pts[k], a.pts[k + 1]], cut);
        if (d < best)
        {
            best = d;
            m = k;
        }
    }
    var pts = subArray(a.pts, 0, m + 1);
    if (planarDistance(pts[m], cut) < 1e-9)
    {
        pts = subArray(pts, 0, m);
    }
    var joined = { "kind" : a.kind, "pts" : concatenateArrays([pts, b.pts]), "e0" : a.e0, "e1" : b.e1,
        "faces" : concatenateArrays([a.faces, b.faces]), "envelope" : a.envelope == true || b.envelope == true };
    if (a.parts != undefined && b.parts != undefined)
    {
        // Part q of a covers chain points start .. start + size - 1 (consecutive parts share their joint point).
        var parts = [];
        var start = 0;
        for (var part in a.parts)
        {
            const count = size(part.pts);
            if (start + count - 1 <= size(pts) - 1)
            {
                parts = append(parts, part);
            }
            else
            {
                var kept = subArray(part.pts, 0, max(1, size(pts) - start));
                kept = append(kept, cut);
                parts = append(parts, { "pts" : kept, "n0" : part.n0, "n1" : b.parts[0].n0, "face" : part.face });
                break;
            }
            start += count - 1;
        }
        if (size(parts) == size(a.parts))
        {
            // The cut fell on a's last point: its last part ends at the cut.
            var last = parts[size(parts) - 1];
            last.pts[size(last.pts) - 1] = cut;
            parts[size(parts) - 1] = last;
        }
        joined.parts = concatenateArrays([parts, b.parts]);
    }
    return joined;
}

/**
 * The curves of one chain in "keep" mode: one edge per source face, meeting at the averaged joints; a line or an arc
 * where the face's row is one within arcTolerance (so planar and cylindrical faces come out planar and cylindrical),
 * else a spline with exact end tangents.
 * @returns {map} : counts { "lines", "arcs", "splines" } updated
 */
export function emitChainParts(context is Context, id is Id, ch is map, profile is boolean, lo is array, arcTolerance is ValueWithUnits,
    counts is map) returns map
{
    var result = counts;
    const parts = ch.parts;
    const np = size(parts);
    var joints = [parts[0].pts[0]];
    for (var q = 1; q < np; q += 1)
    {
        const a = parts[q - 1].pts[size(parts[q - 1].pts) - 1];
        const b = parts[q].pts[0];
        joints = append(joints, [0.5 * (a[0] + b[0]), 0.5 * (a[1] + b[1])]);
    }
    joints = append(joints, parts[np - 1].pts[size(parts[np - 1].pts) - 1]);
    for (var q = 0; q < np; q += 1)
    {
        var pts = parts[q].pts;
        const n = size(pts);
        pts[0] = joints[q];
        pts[n - 1] = joints[q + 1];
        var points = [];
        for (var p in pts)
        {
            points = append(points, toolPoint(p, profile, lo));
        }
        const pid = id + ("p" ~ q);
        const shape = classifyPoints(points, arcTolerance, true, true);
        if (shape.kind == "line")
        {
            emitLineCurve(context, pid, points[0], points[n - 1]);
            result.lines += 1;
        }
        else if (shape.kind == "arc")
        {
            emitArcCurve(context, pid, shape);
            result.arcs += 1;
        }
        else
        {
            const t0 = endTangent(parts[q].n0, pts[0], pts[1]);
            const t1 = endTangent(parts[q].n1, pts[n - 2], pts[n - 1]);
            const start = (t0 == undefined) ? undefined : (profile ? vector(t0[0], 0, t0[1]) : vector(t0[0], t0[1], 0));
            const end = (t1 == undefined) ? undefined : (profile ? vector(t1[0], 0, t1[1]) : vector(t1[0], t1[1], 0));
            emitSplineCurve(context, pid, pickIndices(points, separatedIndices([points])), start, end, fitApproximation());
            result.splines += 1;
        }
    }
    return result;
}

/**
 * The curve of one chain in "merge" mode: ONE spline through the whole chain. Fitting it straight through the rows
 * rings where the faces meet with a curvature jump (a line into a tight arc): 12 um on 4803's corner between adaptive
 * samples, 16 um at the CORE's tip with uniform 4 mm ones. So the exact per-face curves are built first
 * (emitChainParts), sampled uniformly by arc length (at least UNWRAP_PART_MERGE_SPACING apart, at most
 * UNWRAP_PART_PRISM_ROW_MAX points), fitted with the chain's end tangents, and deleted.
 */
export function emitMergedChain(context is Context, id is Id, ch is map, profile is boolean, lo is array, arcTolerance is ValueWithUnits,
    start, end)
{
    const exactId = id + "exact";
    emitChainParts(context, exactId, ch, profile, lo, arcTolerance, { "lines" : 0, "arcs" : 0, "splines" : 0 });
    var edges = [];
    var lengths = [];
    var total = 0;
    for (var q = 0; q < size(ch.parts); q += 1)
    {
        const edge = qCreatedBy(exactId + ("p" ~ q), EntityType.EDGE);
        const length = evLength(context, { "entities" : edge }) / meter;
        edges = append(edges, edge);
        lengths = append(lengths, length);
        total += length;
    }
    const spacing = max(total / (UNWRAP_PART_PRISM_ROW_MAX - 1), UNWRAP_PART_MERGE_SPACING);
    var points = [];
    for (var q = 0; q < size(edges); q += 1)
    {
        const count = max(3, ceil(lengths[q] / spacing) + 1);
        var parameters = [];
        for (var k = 0; k < count; k += 1)
        {
            parameters = append(parameters, k / (count - 1));
        }
        var run = [];
        for (var tl in evEdgeTangentLines(context, { "edge" : edges[q], "parameters" : parameters }))
        {
            run = append(run, tl.origin);
        }
        // Each edge in chain order: its start is the part's first point.
        const first = toolPoint(ch.parts[q].pts[0], profile, lo);
        if (norm(run[count - 1] - first) < norm(run[0] - first))
        {
            run = reverse(run);
        }
        points = concatenateArrays([points, (q == 0) ? run : subArray(run, 1, count)]);
    }
    opDeleteBodies(context, id + "deleteExact", { "entities" : qCreatedBy(exactId, EntityType.BODY) });
    emitSplineCurve(context, id + "fit", pickIndices(points, separatedIndices([points])), start, end, fitApproximation());
}

/**
 * Chain ends meeting end to end (within `tolerance`) moved onto their common average point. Rows shifted to their
 * best fit no longer share the source edge's point exactly; where two such chains meet at a slight crease their short
 * overshoots run almost parallel and never cross, leaving the outline open by microns (measured: 4401's side view
 * stayed one cell). Snapped, they share a vertex. Only near-tangent meetings (|cos| > UNWRAP_PART_SNAP_DOT) are
 * snapped: at an angle the overshoots cross anyway, and moving a best-fit end would tilt its row (4401's tail cap:
 * +11 um).
 */
export function snapChainEnds(chains is array, tolerance is number) returns array
{
    var ends = [];
    for (var i = 0; i < size(chains); i += 1)
    {
        if (chains[i].envelope == true)
        {
            // Envelope rows lie off their face on purpose and meet their neighbours at an angle: never moved.
            continue;
        }
        const pts = chains[i].pts;
        const n = size(pts);
        ends = append(ends, { "chain" : i, "last" : false, "p" : chains[i].straight ? chains[i].lineStart : pts[0],
                    "t" : planarDirection(pts[0], pts[1]) });
        ends = append(ends, { "chain" : i, "last" : true, "p" : chains[i].straight ? chains[i].lineEnd : pts[n - 1],
                    "t" : planarDirection(pts[n - 2], pts[n - 1]) });
    }
    var out = chains;
    var done = makeArray(size(ends), false);
    for (var a = 0; a < size(ends); a += 1)
    {
        if (done[a])
        {
            continue;
        }
        var cluster = [a];
        for (var b = a + 1; b < size(ends); b += 1)
        {
            if (!done[b] && ends[b].chain != ends[a].chain && planarDistance(ends[a].p, ends[b].p) < tolerance
                && abs(planarDot(ends[a].t, ends[b].t)) > UNWRAP_PART_SNAP_DOT)
            {
                cluster = append(cluster, b);
            }
        }
        if (size(cluster) < 2)
        {
            continue;
        }
        var sx = 0;
        var sy = 0;
        for (var k in cluster)
        {
            sx += ends[k].p[0];
            sy += ends[k].p[1];
            done[k] = true;
        }
        const p = [sx / size(cluster), sy / size(cluster)];
        for (var k in cluster)
        {
            out[ends[k].chain] = movedChainEnd(out[ends[k].chain], ends[k].last, p);
        }
    }
    return out;
}

/** A chain with its first (last = false) or last end moved to p: points, line ends and per-face parts alike. */
export function movedChainEnd(chain is map, last is boolean, p is array) returns map
{
    var ch = chain;
    const n = size(ch.pts);
    ch.pts[last ? n - 1 : 0] = p;
    if (ch.straight)
    {
        if (last)
        {
            ch.lineEnd = p;
        }
        else
        {
            ch.lineStart = p;
        }
    }
    if (ch.parts != undefined)
    {
        const q = last ? size(ch.parts) - 1 : 0;
        var part = ch.parts[q];
        part.pts[last ? size(part.pts) - 1 : 0] = p;
        ch.parts[q] = part;
    }
    return ch;
}

/**
 * The 2D arrangement of one view: a flat sheet just outside the box (side view: y = lo[1] - 1 mm, coordinates (x, z);
 * plan view: z = lo[2] - 1 mm, coordinates (x, y)) split by the curves of every chain and their extensions. Each end
 * lying on another chain of the view (within UNWRAP_PART_JOIN, or 3 x shapeTolerance when larger) is extended by
 * UNWRAP_PART_OVERSHOOT (or 3 x shapeTolerance) so the curves really cross; a free end is extended along its tangent
 * to the box. Straight chains are finite segments.
 * @returns {map} : { "chains" (with "ext": the extensions), "faces" (the cells), "counts" }
 */
export function prismArrangement(context is Context, id is Id, viewChains is array, profile is boolean, lo is array, hi is array,
    settings is map) returns map
{
    const reach = sqrt((hi[0] - lo[0]) ^ 2 + (hi[1] - lo[1]) ^ 2 + (hi[2] - lo[2]) ^ 2) + 0.01;
    const ib = profile ? 2 : 1;
    const keep = settings.faceMode == "keep";
    // A best-fit row is shifted by up to the shape tolerance, so two neighbours' ends can part by twice that: the
    // junction test and the overshoot grow with it (measured: 4401's tail cap, shifted 25 um, left its outline open).
    const junction = max(UNWRAP_PART_JOIN, 3 * settings.shapeTolerance / meter);
    const overshoot = max(UNWRAP_PART_OVERSHOOT, 3 * settings.shapeTolerance / meter);
    const chains = snapChainEnds(viewChains, junction);
    var out = [];
    var counts = { "lines" : 0, "arcs" : 0, "splines" : 0 };
    for (var i = 0; i < size(chains); i += 1)
    {
        var ch = chains[i];
        ch.ext = [];
        const cid = id + ("c" ~ i);
        var t0 = undefined;
        var t1 = undefined;
        var p0 = ch.pts[0];
        var p1 = ch.pts[size(ch.pts) - 1];
        if (ch.straight)
        {
            const d = planarDirection(ch.lineStart, ch.lineEnd);
            p0 = ch.lineStart;
            p1 = ch.lineEnd;
            t0 = d;
            t1 = d;
            emitLineCurve(context, cid + "line", toolPoint(p0, profile, lo), toolPoint(p1, profile, lo));
            counts.lines += 1;
        }
        else
        {
            const n = size(ch.pts);
            t0 = endTangent(ch.e0.n, ch.pts[0], ch.pts[1]);
            t1 = endTangent(ch.e1.n, ch.pts[n - 2], ch.pts[n - 1]);
            if (keep && ch.parts != undefined)
            {
                counts = emitChainParts(context, cid + "part", ch, profile, lo, settings.arcTolerance, counts);
            }
            else
            {
                const start = (t0 == undefined) ? undefined : (profile ? vector(t0[0], 0, t0[1]) : vector(t0[0], t0[1], 0));
                const end = (t1 == undefined) ? undefined : (profile ? vector(t1[0], 0, t1[1]) : vector(t1[0], t1[1], 0));
                emitMergedChain(context, cid + "merged", ch, profile, lo, settings.arcTolerance, start, end);
                counts.splines += 1;
            }
            if (t0 == undefined)
            {
                t0 = planarDirection(ch.pts[0], ch.pts[1]);
            }
            if (t1 == undefined)
            {
                t1 = planarDirection(ch.pts[n - 2], ch.pts[n - 1]);
            }
        }
        const ends = [{ "p" : p0, "t" : [-t0[0], -t0[1]] }, { "p" : p1, "t" : t1 }];
        for (var k in [0, 1])
        {
            var onOther = false;
            for (var j = 0; j < size(chains); j += 1)
            {
                if (j != i && nearChain(chains[j], ends[k].p, junction))
                {
                    onOther = true;
                    break;
                }
            }
            const len = onOther ? overshoot : reach;
            const e = ends[k];
            emitLineCurve(context, cid + ("ext" ~ k), toolPoint(e.p, profile, lo), toolPoint([e.p[0] + len * e.t[0], e.p[1] + len * e.t[1]], profile, lo));
            ch.ext = append(ch.ext, { "p" : e.p, "t" : e.t, "len" : len });
        }
        out = append(out, ch);
    }
    // The sheet: a line along x extruded across the other coordinate, split by every curve at once.
    const x0 = lo[0] - 0.001;
    const x1 = hi[0] + 0.001;
    const c0 = lo[ib] - 0.001;
    const c1 = hi[ib] + 0.001;
    emitLineCurve(context, id + "base", toolPoint([x0, c0], profile, lo), toolPoint([x1, c0], profile, lo));
    opExtrude(context, id + "sheet", { "entities" : qCreatedBy(id + "base", EntityType.EDGE), "direction" : profile ? vector(0, 0, 1) : vector(0, 1, 0),
                "endBound" : BoundingType.BLIND, "endDepth" : (c1 - c0) * meter });
    const tools = qSubtraction(qOwnedByBody(qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.WIRE), EntityType.EDGE),
        qCreatedBy(id + "base", EntityType.EDGE));
    const sheet = qCreatedBy(id + "sheet", EntityType.BODY);
    opSplitFace(context, id + "split", { "faceTargets" : qOwnedByBody(sheet, EntityType.FACE), "edgeTools" : tools });
    return { "chains" : out, "faces" : evaluateQuery(context, qOwnedByBody(sheet, EntityType.FACE)), "counts" : counts };
}

/** Crossings (the other coordinate) of the line x = xp with a chain: its polyline (or line) and its extensions. */
export function chainCrossings(ch is map, xp is number) returns array
{
    var cs = [];
    const pts = ch.straight ? [ch.lineStart, ch.lineEnd] : ch.pts;
    for (var k = 0; k + 1 < size(pts); k += 1)
    {
        const a = pts[k];
        const b = pts[k + 1];
        if ((a[0] - xp) * (b[0] - xp) <= 0 && abs(b[0] - a[0]) > 1e-12)
        {
            cs = append(cs, a[1] + (xp - a[0]) / (b[0] - a[0]) * (b[1] - a[1]));
        }
    }
    for (var e in ch.ext)
    {
        if (abs(e.t[0]) > 1e-12)
        {
            const f = (xp - e.p[0]) / e.t[0];
            if (f > 0 && f <= e.len)
            {
                cs = append(cs, e.p[1] + f * e.t[1]);
            }
        }
    }
    return cs;
}

/** [xmin, xmax, cmin, cmax] (plain metres) of each cell, c = coordinate ib. */
export function cellBoxes(context is Context, faces is array, ib is number) returns array
{
    var boxes = [];
    for (var f in faces)
    {
        const bb = evBox3d(context, { "topology" : f, "tight" : true });
        boxes = append(boxes, [bb.minCorner[0] / meter, bb.maxCorner[0] / meter, bb.minCorner[ib] / meter, bb.maxCorner[ib] / meter]);
    }
    return boxes;
}

/** The cell holding a sheet point (box prefilter, qContainsPoint only when ambiguous), or undefined. */
export function locateCell(context is Context, faces is array, boxes is array, px is number, pc is number, point is Vector)
{
    var candidates = [];
    for (var k = 0; k < size(faces); k += 1)
    {
        const b = boxes[k];
        if (px >= b[0] - 1e-7 && px <= b[1] + 1e-7 && pc >= b[2] - 1e-7 && pc <= b[3] + 1e-7)
        {
            candidates = append(candidates, k);
        }
    }
    if (size(candidates) == 1)
    {
        return candidates[0];
    }
    for (var k in candidates)
    {
        if (!isQueryEmpty(context, qContainsPoint(faces[k], point)))
        {
            return k;
        }
    }
    return undefined;
}

/**
 * Which (plan cell, side cell) products are part of the piece. Columns at x-probes (midpoints between all chain
 * end / extension / x-extremum events, plus every cell's centroid x): at each, the side curves' crossings give
 * z-intervals (each in one side cell) and the plan curves' crossings y-intervals (each in one plan cell); every
 * product not yet known is decided by one point test (unwrapInverse + qContainsPoint on the piece). A product is
 * inside when any of its tests is.
 * @returns {map} : { "memb" (per plan cell: map side-cell key -> { "i", "inside" }), "probes", "tests" }
 */
export function prismMembership(context is Context, chart is map, piece is Query, side is map, plan is map, lo is array, hi is array) returns map
{
    const sideBoxes = cellBoxes(context, side.faces, 2);
    const planBoxes = cellBoxes(context, plan.faces, 1);
    var events = [lo[0], hi[0]];
    for (var ch in concatenateArrays([side.chains, plan.chains]))
    {
        const cp = ch.straight ? [ch.lineStart, ch.lineEnd] : ch.pts;
        const n = size(cp);
        events = append(events, cp[0][0]);
        events = append(events, cp[n - 1][0]);
        for (var k = 1; k + 1 < n; k += 1)
        {
            if ((cp[k][0] - cp[k - 1][0]) * (cp[k + 1][0] - cp[k][0]) < 0)
            {
                events = append(events, cp[k][0]);
            }
        }
        for (var e in ch.ext)
        {
            events = append(events, e.p[0] + e.len * e.t[0]);
        }
    }
    events = sort(events, function(a, b) { return a - b; });
    var probes = [];
    for (var k = 0; k + 1 < size(events); k += 1)
    {
        if (events[k + 1] - events[k] > 2e-6 && events[k] >= lo[0] - 1e-9 && events[k + 1] <= hi[0] + 1e-9)
        {
            probes = append(probes, 0.5 * (events[k] + events[k + 1]));
        }
    }
    for (var f in concatenateArrays([side.faces, plan.faces]))
    {
        probes = append(probes, (evApproximateCentroid(context, { "entities" : f }) / meter)[0]);
    }
    var memb = makeArray(size(plan.faces), {});
    var tests = 0;
    for (var xp in probes)
    {
        var zs = [lo[2] - 0.0005, hi[2] + 0.0005];
        for (var ch in side.chains)
        {
            zs = concatenateArrays([zs, chainCrossings(ch, xp)]);
        }
        zs = sort(zs, function(a, b) { return a - b; });
        var zm = [];
        var zc = [];
        for (var k = 0; k + 1 < size(zs); k += 1)
        {
            if (zs[k + 1] - zs[k] < 2e-6)
            {
                continue;
            }
            const z = 0.5 * (zs[k] + zs[k + 1]);
            if (z < lo[2] || z > hi[2])
            {
                continue;
            }
            const si = locateCell(context, side.faces, sideBoxes, xp, z, vector(xp, lo[1] - 0.001, z) * meter);
            if (si != undefined)
            {
                zm = append(zm, z);
                zc = append(zc, si);
            }
        }
        var ys = [lo[1] - 0.0005, hi[1] + 0.0005];
        for (var ch in plan.chains)
        {
            ys = concatenateArrays([ys, chainCrossings(ch, xp)]);
        }
        ys = sort(ys, function(a, b) { return a - b; });
        for (var k = 0; k + 1 < size(ys); k += 1)
        {
            if (ys[k + 1] - ys[k] < 2e-6)
            {
                continue;
            }
            const y = 0.5 * (ys[k] + ys[k + 1]);
            if (y < lo[1] || y > hi[1])
            {
                continue;
            }
            const pci = locateCell(context, plan.faces, planBoxes, xp, y, vector(xp, y, lo[2] - 0.001) * meter);
            if (pci == undefined)
            {
                continue;
            }
            var m = memb[pci];
            var changed = false;
            for (var j = 0; j < size(zm); j += 1)
            {
                const key = "" ~ zc[j];
                if (m[key] != undefined)
                {
                    continue;
                }
                const w = unwrapInverse(chart, xp, y, zm[j]);
                m[key] = { "i" : zc[j], "inside" : !isQueryEmpty(context, qContainsPoint(piece, vector(w[0], w[1], w[2]) * meter)) };
                tests += 1;
                changed = true;
            }
            if (changed)
            {
                memb[pci] = m;
            }
        }
    }
    return { "memb" : memb, "probes" : size(probes), "tests" : tests };
}

/**
 * PRISM, the band rebuild of one piece (research_unwrap_curved_ref.md 4): the analysis rows chained per view and
 * deduplicated, the two arrangements, membership, plan cells grouped by their set of solid side cells (the bands),
 * one (plan cells x Z) INTERSECT (side cells x Y) per band, bands united. Temporaries are deleted.
 * @returns {map} : { "body" : Query (one solid), "bands", "tools", "counts", "snapped", "text", "notes" }
 */
export function prismPiece(context is Context, id is Id, chart is map, piece is Query, analysis is map, settings is map) returns map
{
    const extent = chartExtentSampled(context, chart, piece, settings.part, UNWRAP_PART_EXTENT_SPACING, 5);
    var lo = extent[0];
    var hi = extent[1];
    for (var ax in [0, 1, 2])
    {
        lo[ax] -= UNWRAP_PART_BOX_MARGIN;
        hi[ax] += UNWRAP_PART_BOX_MARGIN;
    }
    const flatTol = settings.flatTolerance / meter;
    const pch = distinctViewChains(mergeOverlappingChains(chainRows(analysis.rows, "PROFILE"), UNWRAP_PART_DUPLICATE), flatTol);
    const wch = distinctViewChains(mergeOverlappingChains(chainRows(analysis.rows, "WALL"), UNWRAP_PART_DUPLICATE), flatTol);
    var snapped = 0;
    for (var ch in concatenateArrays([pch, wch]))
    {
        if (ch.snapped)
        {
            snapped += 1;
        }
    }
    const side = prismArrangement(context, id + "side", pch, true, lo, hi, settings);
    const plan = prismArrangement(context, id + "plan", wch, false, lo, hi, settings);
    const membership = prismMembership(context, chart, piece, side, plan, lo, hi);

    // Bands: plan cells with the same set of solid side cells.
    var groups = {};
    for (var pci = 0; pci < size(plan.faces); pci += 1)
    {
        var inside = [];
        for (var entry in membership.memb[pci])
        {
            if (entry.value.inside)
            {
                inside = append(inside, entry.value.i);
            }
        }
        if (size(inside) == 0)
        {
            continue;
        }
        inside = sort(inside, function(a, b) { return a - b; });
        const key = toString(inside);
        if (groups[key] == undefined)
        {
            groups[key] = { "side" : inside, "plan" : [] };
        }
        groups[key].plan = append(groups[key].plan, plan.faces[pci]);
    }
    var results = [];
    var notes = [];
    var gi = 0;
    for (var entry in groups)
    {
        const g = entry.value;
        var sideFaces = [];
        for (var k in g.side)
        {
            sideFaces = append(sideFaces, side.faces[k]);
        }
        const gid = id + ("band" ~ gi);
        opExtrude(context, gid + "plan", { "entities" : qUnion(g.plan), "direction" : vector(0, 0, 1),
                    "endBound" : BoundingType.BLIND, "endDepth" : (hi[2] - lo[2] + 0.004) * meter });
        opExtrude(context, gid + "side", { "entities" : qUnion(sideFaces), "direction" : vector(0, 1, 0),
                    "endBound" : BoundingType.BLIND, "endDepth" : (hi[1] - lo[1] + 0.004) * meter });
        // Several disjoint plan prisms, each intersected with the side prism (INTERSECTION would intersect them all).
        opBoolean(context, gid + "intersect", { "targets" : qCreatedBy(gid + "plan", EntityType.BODY),
                    "tools" : qCreatedBy(gid + "side", EntityType.BODY), "operationType" : BooleanOperationType.SUBTRACT_COMPLEMENT });
        results = append(results, qCreatedBy(gid + "plan", EntityType.BODY));
        notes = append(notes, "    band " ~ gi ~ ": " ~ size(g.plan) ~ " plan cell(s) x " ~ size(g.side) ~ " side cell(s)");
        gi += 1;
    }
    if (size(results) == 0)
    {
        throw regenError("no plan cell holds material");
    }
    if (size(results) > 1)
    {
        opBoolean(context, id + "unite", { "tools" : qUnion(results), "operationType" : BooleanOperationType.UNION });
    }
    const scrap = qBodyType(qCreatedBy(id, EntityType.BODY), [BodyType.SHEET, BodyType.WIRE, BodyType.POINT]);
    if (!isQueryEmpty(context, scrap))
    {
        opDeleteBodies(context, id + "clean", { "entities" : scrap });
    }
    const body = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID);
    const count = size(evaluateQuery(context, body));
    if (count != 1)
    {
        throw regenError("the bands gave " ~ count ~ " solids, not one");
    }
    const counts = { "lines" : side.counts.lines + plan.counts.lines, "arcs" : side.counts.arcs + plan.counts.arcs,
        "splines" : side.counts.splines + plan.counts.splines };
    const text = size(groups) ~ " band(s); side view " ~ size(pch) ~ " chains -> " ~ size(side.faces) ~ " cells, plan view "
        ~ size(wch) ~ " chains -> " ~ size(plan.faces) ~ " cells; curves " ~ counts.lines ~ " line, " ~ counts.arcs ~ " arc, "
        ~ counts.splines ~ " spline; " ~ membership.probes ~ " probes, " ~ membership.tests ~ " point tests";
    return { "body" : body, "bands" : size(groups), "tools" : size(pch) + size(wch), "counts" : counts, "snapped" : snapped,
            "text" : text, "notes" : notes };
}
