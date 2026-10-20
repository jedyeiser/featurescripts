FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/approximationUtils.fs", version : "3083.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");

// curveMappingCore, EXPORT-imported: the dialog's enums (SamplingMode, FrameNormalMode,
// BinormalSource) come from it. wrapCurve is not imported: nothing here uses it, and its own
// export-import of the core could bring a second version of the core into this scope.
// Pinned to the core at curveMapping V28 (adds mapCoordSystems).
// DEVELOPMENT: follows the core tab (adaptive edge fit). Re-pin to the next core version once the user creates it.
export import(path : "683d867c35fdab9c98d47556", version : "");



/**
 * This feature extends the concepts from wrapCurve to deform an entire solid or surface body.
 *
 * Given a source body and two reference edge chains (from/to), every point on the body is
 * mapped from the from-path Frenet frame to the to-path Frenet frame.
 *
 * Debug mode steps through three cumulative sub-operations:
 *   1. createWires  — transform all edges (creates wire bodies)
 *   2. createFaces  — reconstruct all faces using opFillSurface with interior guide points
 *   3. createBodies — union faces into a single body, delete intermediate wire/vertex bodies
 */

export const FaceControlMultiplierBounds = {(unitless) : [2, 3, 10]} as IntegerBoundSpec;

/** Mapped vertices closer than this are one vertex: edges ending at them then share exact end points. */
const VERTEX_MERGE_TOLERANCE = 5e-6 * meter;

/** Maximum wrapped-edge deviation (default 0 = off while the refit cost is tuned; 2 microns is the target): lengths in meter [min, default, max] and the other units. */
const EDGE_ACCURACY_BOUND = { (meter) : [0, 0, 1e-4], (centimeter) : 0, (millimeter) : 0, (inch) : 0, (foot) : 0, (yard) : 0 } as LengthBoundSpec;

/** Mapped positions with the ones that fall within VERTEX_MERGE_TOLERANCE of an earlier one moved onto it. */
function mergeCloseVertices(positions is array) returns array
{
    var merged = positions;
    for (var i = 1; i < size(merged); i += 1)
        for (var j = 0; j < i; j += 1)
            if (norm(merged[i] - merged[j]) < VERTEX_MERGE_TOLERANCE && norm(merged[i] - merged[j]) > 0 * meter)
            {
                merged[i] = merged[j];
                break;
            }
    return merged;
}

/** A mate connector within this distance of a deformed source body is attached to that body's result. */
const MC_ON_BODY_TOLERANCE = 1e-5 * meter;

const NAME_SEPARATOR ="\n"; // also used as a regexp by splitByRegexp

function splitNames(joined is string) returns array
{
    return joined == "" ? [] : splitByRegexp(joined, NAME_SEPARATOR);
}

/**
 * Editing logic: record the source bodies' names (getProperty works here, not in the feature
 * body) so the feature can name each deformed result "<source name><suffix>".
 */
export function deformEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    if (definition.deformMode == DeformMode.FACES)
    {
        definition.sourceNames = "";
        return definition;
    }
    var names = [];
    for (var body in evaluateQuery(context, definition.sourceBody))
    {
        var name = getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME });
        names = append(names, (name is string && name != "") ? name : "Part");
    }
    definition.sourceNames = (size(names) == 0) ? "" : join(names, NAME_SEPARATOR);
    return definition;
}

/**
 * How the faces of the deformed body are rebuilt. A fill builds a face from its wrapped outline alone, so its interior (and
 * its tangency to the neighbours) is whatever the kernel finds smoothest; mapping a face's SURFACE carries the source
 * face's own shape through the deform and trims it to its outline.
 */
export enum FaceBuilder
{
    annotation { "Name" : "Fill (from the wrapped outline)" }
    FILL,
    annotation { "Name" : "Map the surface of long thin faces" }
    MAP_THIN,
    annotation { "Name" : "Map the surface of every face (test)" }
    MAP_ALL
}

/** Whether to deform a whole body or a user-selected set of faces. */
export enum DeformMode
{
    annotation { "Name" : "Bodies" }
    BODIES,
    annotation { "Name" : "Faces" }
    FACES
}


/** Vertices of a face closer than this along the from path lie in one cross-section; the cut across the face runs through them. */
const SPLIT_CLUSTER_GAP = 1e-3 * meter;
/** A cluster of vertices must span at least this much to cut across the face (two vertices a hair apart on one side do not). */
const SPLIT_MIN_SPAN = 1e-3 * meter;
/** A face with no vertex clusters to cut at (only its two ends) is cut in the middle of the gap, when the gap is at least this wide. */
const SPLIT_MIN_GAP = 2e-3 * meter;
/** A face is long and thin enough to be worth measuring when (diagonal of its box)^2 / area exceeds this. */
const THIN_FACE_ASPECT = 80;
/** In the rebuild of cut faces, a piece with (diagonal of its box)^2 / area above this (and an area above GUIDED_FACE_MIN_AREA) is filled with guide points: a fill of a thin piece is flat across its width, and its end curves are too far away to hold the cross-section. */
const GUIDED_FACE_ASPECT = 20;
const GUIDED_FACE_MIN_AREA = 2e-5 * meter ^ 2;     // 20 mm2
/** At most this many thin faces of one body are measured (each measurement maps interior points; the interpreter has a step limit). */
const MAX_MEASURED_FACES = 4;
/** A thin face whose fill is too far from the mapped shape is recut into pieces about this long (cross cuts on a grid along the from path). */
const REFINE_PIECE_LENGTH = 100 * meter;
/** Face accuracy (default 0 = off until the cost is tuned; 0.05 mm is the intended value): lengths in meter [min, default, max] and the other units. */
const FACE_ACCURACY_BOUND = { (meter) : [0, 0, 1e-3], (centimeter) : 0, (millimeter) : 0, (inch) : 0, (foot) : 0, (yard) : 0 } as LengthBoundSpec;
/** At most this many cuts are made across one face. */
const SPLIT_MAX_CUTS = 40;

/**
 * Arc length along a path of a point, extended past either end along the end tangent (projectOntoFrenetPath clamps
 * to the path, but a face can reach well past the end of the reference).
 */
function pathArcOf(context is Context, path is map, point is Vector) returns ValueWithUnits
{
    const arc = projectOntoFrenetPath(path, point, undefined).arcLength;
    const atStart = arc <= 1e-9 * meter;
    const atEnd = arc >= path.totalLength - 1e-9 * meter;
    if (!atStart && !atEnd)
        return arc;
    const endArc = atStart ? 0 * meter : path.totalLength;
    const endFrame = getFrameAtArcLength(context, path, endArc).frame;
    return endArc + dot(point - endFrame.origin, endFrame.zAxis);
}

/**
 * The cuts that divide a face into pieces, found from its own vertices.
 *
 * Vertices that lie in one cross-section (within SPLIT_CLUSTER_GAP of each other along the from path) are joined by a
 * curve across the face (from one side of the face to the other, never along an edge it already has): it starts and ends exactly on existing vertices, so the cut can never leave an edge a few
 * microns long beside a vertex (a plane through one vertex does, and slivers that small make a later union invalid).
 * The two clusters at the ends of the face are not cut. A face with no cluster in between is cut by a plane in the
 * middle of its widest gap instead.
 *
 * @returns {array} of { "points" : array of Vector } (a curve through these vertices) or { "point" : Vector, "arc" : ValueWithUnits } (a plane there)
 */
function faceCuts(context is Context, fromFrenetPath is map, face is Query, refine is boolean) returns array
{
    const points = mapArray(evaluateQuery(context, qAdjacent(face, AdjacencyType.VERTEX, EntityType.VERTEX)),
        function(v) { return evVertexPoint(context, { "vertex" : v }); });
    var items = mapArray(points, function(p) { return { "arc" : pathArcOf(context, fromFrenetPath, p), "point" : p }; });
    items = sort(items, function(a, b) { return (a.arc - b.arc) / meter; });

    var clusters = [];
    var lastArc = undefined;
    for (var item in items)
    {
        if (lastArc == undefined || item.arc - lastArc >= SPLIT_CLUSTER_GAP)
            clusters = append(clusters, [item]);
        else
            clusters[size(clusters) - 1] = append(clusters[size(clusters) - 1], item);
        lastArc = item.arc;
    }

    // Vertices already joined by an edge of the face (a short edge between two vertices of one cross-section) belong
    // together; a cut must not run along such an edge, or it would duplicate it.
    var edgeEnds = [];
    for (var e in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
        edgeEnds = append(edgeEnds, mapArray(evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] }), function(l) { return l.origin; }));

    var cuts = [];
    for (var c = 1; c < size(clusters) - 1 && size(cuts) < SPLIT_MAX_CUTS; c += 1)
    {
        const cluster = clusters[c];
        // Group the cluster's vertices: two are in one group when an edge of the face joins them.
        var group = [];
        for (var i = 0; i < size(cluster); i += 1)
            group = append(group, i);
        for (var ends in edgeEnds)
        {
            var first = undefined;
            var second = undefined;
            for (var i = 0; i < size(cluster); i += 1)
            {
                if (norm(cluster[i].point - ends[0]) < 1e-6 * meter)
                    first = i;
                if (norm(cluster[i].point - ends[1]) < 1e-6 * meter)
                    second = i;
            }
            if (first != undefined && second != undefined && group[first] != group[second])
            {
                const from = group[second];
                for (var i = 0; i < size(cluster); i += 1)
                    if (group[i] == from)
                        group[i] = group[first];
            }
        }
        // The cut joins the nearest pair of vertices from two different groups (a single plane cut when there is only one group).
        var bestPair = undefined;
        var bestDistance = undefined;
        for (var i = 0; i < size(cluster); i += 1)
            for (var j = i + 1; j < size(cluster); j += 1)
            {
                const distance = norm(cluster[i].point - cluster[j].point);
                if (group[i] != group[j] && distance >= SPLIT_MIN_SPAN && (bestDistance == undefined || distance < bestDistance))
                {
                    bestPair = [cluster[i].point, cluster[j].point];
                    bestDistance = distance;
                }
            }
        if (bestPair != undefined)
            cuts = append(cuts, { "points" : bestPair });
        else if (size(cluster) == 1)
            cuts = append(cuts, { "point" : cluster[0].point, "arc" : cluster[0].arc });
    }
    // A face that is being refined (its fill was too far from the mapped shape) also gets cross cuts on a grid along the
    // path, away from its vertices, so that no piece is longer than about REFINE_PIECE_LENGTH: the fill of each piece is then
    // held by the cut curves at both ends, which carry the cross-section of the face.
    if (refine && size(items) > 1)
    {
        const lowest = items[0].arc;
        const highest = items[size(items) - 1].arc;
        const margin = SPLIT_MIN_GAP;
        for (var g = ceil((lowest + margin) / REFINE_PIECE_LENGTH) * REFINE_PIECE_LENGTH; g < highest - margin && size(cuts) < SPLIT_MAX_CUTS; g += REFINE_PIECE_LENGTH)
        {
            var clear = true;
            for (var item in items)
                if (abs(item.arc - g) < SPLIT_MIN_GAP)
                    clear = false;
            if (clear)
                cuts = append(cuts, { "point" : getFrameAtArcLength(context, fromFrenetPath, g).frame.origin, "arc" : g });
        }
    }
    if (size(cuts) == 0 && size(clusters) >= 2)
    {
        var widest = 0;
        for (var c = 1; c < size(clusters); c += 1)
            if (clusters[c][0].arc - clusters[c - 1][size(clusters[c - 1]) - 1].arc > clusters[widest + 1][0].arc - clusters[widest][size(clusters[widest]) - 1].arc)
                widest = c - 1;
        const before = clusters[widest][size(clusters[widest]) - 1].arc;
        const after = clusters[widest + 1][0].arc;
        if (after - before >= SPLIT_MIN_GAP)
        {
            const arc = (before + after) / 2;
            cuts = append(cuts, { "point" : getFrameAtArcLength(context, fromFrenetPath, arc).frame.origin, "arc" : arc });
        }
    }
    return cuts;
}

/**
 * A work copy of `body` in which each face in `faces` is cut across (see faceCuts). A face with a long, many-edged
 * outline (a band running the length of the part, say) can defeat opFillSurface; the pieces between two such cuts
 * are short and have few edges, and fill well.
 *
 * @returns {map} { "body" : the copy (a sheet body made from all of `body`'s faces), or `undefined` when there was nothing to
 *          cut, "ranOps" : whether any modelling operation was run (a failed cut leaves some behind) }
 */
function splitFacesBetweenVertices(context is Context, id is Id, fromFrenetPath is map, body is Query, faces is array, refine is array)
{
    var cutsOfFace = [];
    var anyCut = false;
    for (var f = 0; f < size(faces); f += 1)
    {
        const cuts = faceCuts(context, fromFrenetPath, faces[f], refine[f]);
        cutsOfFace = append(cutsOfFace, cuts);
        anyCut = anyCut || size(cuts) > 0;
    }
    if (!anyCut)
        return { "body" : undefined, "ranOps" : false };

    opExtractSurface(context, id + "copy", { "faces" : qOwnedByBody(body, EntityType.FACE) });
    const copy = qCreatedBy(id + "copy", EntityType.BODY);

    var totalCuts = 0;
    for (var f = 0; f < size(faces); f += 1)
    {
        if (size(cutsOfFace[f]) == 0)
            continue;

        // The face on the copy: the one with the same area and corner. Looked up afresh for every face, because cutting
        // one face changes the edges (and so the handles) of the faces beside it.
        const face = faces[f];
        const area = evArea(context, { "entities" : face });
        const corner = evBox3d(context, { "topology" : face }).minCorner;
        var target = undefined;
        var targetGap = 1e-4 * meter;      // box corners of the same face agree to far better than this
        for (var candidate in evaluateQuery(context, qOwnedByBody(copy, EntityType.FACE)))
        {
            const gap = norm(evBox3d(context, { "topology" : candidate }).minCorner - corner);
            if (gap < targetGap && abs(evArea(context, { "entities" : candidate }) - area) < 1e-2 * area)
            {
                target = candidate;
                targetGap = gap;
            }
        }
        if (target == undefined)
        {
            println("WARNING: a face that failed to fill was not found on the work copy; it is not cut");
            continue;
        }

        // The tools: a curve through each cluster of vertices (projected onto the face, so it ends exactly on them), or a plane.
        var curveBodies = [];
        var planeFaces = [];
        for (var k = 0; k < size(cutsOfFace[f]); k += 1)
        {
            const cut = cutsOfFace[f][k];
            const toolId = id + ("tool" ~ f ~ "_" ~ k);
            if (cut.points != undefined)
            {
                opPolyline(context, toolId, { "points" : cut.points });
                curveBodies = append(curveBodies, qCreatedBy(toolId, EntityType.BODY));
            }
            else
            {
                const frame = getFrameAtArcLength(context, fromFrenetPath, cut.arc).frame;
                opPlane(context, toolId, { "plane" : plane(cut.point, frame.zAxis) });
                planeFaces = append(planeFaces, qCreatedBy(toolId, EntityType.FACE));
            }
        }
        var splitDefinition = {
                "faceTargets" : target,
                "projectionType" : ProjectionType.NORMAL_TO_TARGET,
                "extendToCompletion" : true,
                "keepToolSurfaces" : false
            };
        if (size(curveBodies) > 0)
            splitDefinition.edgeTools = qOwnedByBody(qUnion(curveBodies), EntityType.EDGE);
        if (size(planeFaces) > 0)
            splitDefinition.planeTools = qUnion(planeFaces);
        try
        {
            opSplitFace(context, id + ("split" ~ f), splitDefinition);
            totalCuts += size(cutsOfFace[f]);
        }
        catch (splitErr)
        {
            println("WARNING: could not cut a face that failed to fill: " ~ toString(splitErr) ~ " (" ~ size(curveBodies) ~ " curve cut(s), " ~ size(planeFaces) ~ " plane cut(s))");
        }
        if (size(curveBodies) > 0 || size(planeFaces) > 0)
        {
            try silent
            {
                opDeleteBodies(context, id + ("dropTools" ~ f), { "entities" : qUnion([qUnion(curveBodies), qOwnerBody(qUnion(planeFaces))]) });
            }
        }
    }
    if (totalCuts == 0)
    {
        try silent
        {
            opDeleteBodies(context, id + "dropWork", { "entities" : copy });
        }
        return { "body" : undefined, "ranOps" : true };
    }
    println("Deform: " ~ size(faces) ~ " face(s) failed to fill; retrying with " ~ totalCuts ~ " cut(s) across them");
    return { "body" : copy, "ranOps" : true };
}

/** Two points of a face outline closer than this are the same vertex. */
const OUTLINE_VERTEX_TOLERANCE = 1e-6 * meter;
/** A junction of two outline edges turns by more than this for it to be a corner of the face. */
const OUTLINE_CORNER_ANGLE = 30 * degree;

/**
 * Build a face from its wrapped outline with opBoundarySurface when the outline is four-sided: the outline edges are
 * walked in order, the junctions that turn by more than OUTLINE_CORNER_ANGLE are the four corners, and the two pairs of
 * opposite sides are the profiles. Unlike a fill, the surface takes its shape from all four sides, so the cross-section
 * of a long thin band (carried by its two ends) is kept. Creates the surface as a body under `id`.
 *
 * @returns {boolean} whether the surface was made (false when the outline does not have exactly four corners or the
 *          kernel refused: the caller then fills the outline instead)
 */
function boundarySurfaceFace(context is Context, id is Id, outline is Query) returns boolean
{
    const edges = evaluateQuery(context, outline);
    const n = size(edges);
    if (n < 4)
        return false;
    var ends = [];
    for (var e in edges)
    {
        const l = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] });
        ends = append(ends, { "p0" : l[0].origin, "d0" : l[0].direction, "p1" : l[1].origin, "d1" : l[1].direction });
    }

    // Walk the outline: order[k] is the k-th edge, forward[k] whether it is traversed from its start.
    var order = [0];
    var forward = [true];
    var used = makeArray(n, false);
    used[0] = true;
    var tail = ends[0].p1;
    for (var step = 1; step < n; step += 1)
    {
        var next = -1;
        var nextForward = true;
        for (var k = 0; k < n && next < 0; k += 1)
        {
            if (used[k])
                continue;
            if (norm(ends[k].p0 - tail) < OUTLINE_VERTEX_TOLERANCE)
            {
                next = k;
                nextForward = true;
            }
            else if (norm(ends[k].p1 - tail) < OUTLINE_VERTEX_TOLERANCE)
            {
                next = k;
                nextForward = false;
            }
        }
        if (next < 0)
            return false;
        used[next] = true;
        order = append(order, next);
        forward = append(forward, nextForward);
        tail = nextForward ? ends[next].p1 : ends[next].p0;
    }
    if (norm(tail - ends[0].p0) > OUTLINE_VERTEX_TOLERANCE)
        return false;

    // Corners: the junction at the start of the i-th edge turns sharply.
    var corners = [];
    for (var i = 0; i < n; i += 1)
    {
        const prev = (i + n - 1) % n;
        const dirIn = forward[prev] ? ends[order[prev]].d1 : -ends[order[prev]].d0;
        const dirOut = forward[i] ? ends[order[i]].d0 : -ends[order[i]].d1;
        if (dot(normalize(dirIn), normalize(dirOut)) < cos(OUTLINE_CORNER_ANGLE))
            corners = append(corners, i);
    }
    if (size(corners) != 4)
        return false;

    // The four sides, each a run of edges; a side of several edges becomes one wire body.
    var profiles = [];
    var sideWires = [];
    for (var c = 0; c < 4; c += 1)
    {
        var side = [];
        var i = corners[c];
        const stop = corners[(c + 1) % 4];
        for (var count = 0; count < n; count += 1)
        {
            side = append(side, edges[order[i]]);
            i = (i + 1) % n;
            if (i == stop)
                break;
        }
        if (size(side) == 1)
        {
            profiles = append(profiles, side[0]);
        }
        else
        {
            opExtractWires(context, id + ("side" ~ c), { "edges" : qUnion(side) });
            const wire = qCreatedBy(id + ("side" ~ c), EntityType.BODY);
            sideWires = append(sideWires, wire);
            profiles = append(profiles, wire);
        }
    }

    var made = true;
    try
    {
        opBoundarySurface(context, id + "bsurf", { "uProfileSubqueries" : [profiles[0], profiles[2]],
                                                   "vProfileSubqueries" : [profiles[1], profiles[3]] });
    }
    catch (bsurfErr)
    {
        println("boundary surface refused: " ~ toString(bsurfErr));
        made = false;
    }
    if (size(sideWires) > 0)
    {
        try silent
        {
            opDeleteBodies(context, id + "dropSides", { "entities" : qUnion(sideWires) });
        }
    }
    return made;
}

/** Angle (degrees) between the tangent planes of two faces at a point where they meet (orientation ignored). */
function planeAngleAt(context is Context, faceA is Query, faceB is Query, point is Vector) returns number
{
    const nA = evFaceTangentPlane(context, { "face" : faceA, "parameter" : evDistance(context, { "side0" : point, "side1" : faceA }).sides[1].parameter }).normal;
    const nB = evFaceTangentPlane(context, { "face" : faceB, "parameter" : evDistance(context, { "side0" : point, "side1" : faceB }).sides[1].parameter }).normal;
    return acos(min(1, abs(dot(nA, nB)))) / degree;
}

/**
 * Debug: continuity between faces. Every edge of the source body that joins two faces is classified by the angle between
 * the faces along it (smooth when under 1 degree, as a fillet is to its neighbours); the same place on the deformed result
 * is measured, and edges that were smooth in the source but are not in the result are listed. The wrap is smooth, so a
 * source edge that is tangent must stay tangent.
 */
function reportContinuity(context is Context, source is Query, result is Query, fromFrenetPath is map, toFrenetPath is map, fromRefArc, toRefArc, flipToNormal is boolean)
{
    try
    {
        const resultEdges = qOwnedByBody(result, EntityType.EDGE);
        const resultEdgeList = evaluateQuery(context, resultEdges);
        var rows = [];
        var smooth = 0;
        var unmatched = 0;
        for (var e in evaluateQuery(context, qOwnedByBody(source, EntityType.EDGE)))
        {
            const faces = evaluateQuery(context, qAdjacent(e, AdjacencyType.EDGE, EntityType.FACE));
            if (size(faces) != 2)
                continue;
            const lines = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0.2, 0.5, 0.8] });
            var sourceAngle = 0;
            for (var l in lines)
                sourceAngle = max(sourceAngle, planeAngleAt(context, faces[0], faces[1], l.origin));
            const mapped = mapWorldPoints(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal,
                mapArray(lines, function(l) { return l.origin; }));
            var resultAngle = 0;
            var matched = true;
            for (var p in mapped)
            {
                const nearest = evDistance(context, { "side0" : p, "side1" : resultEdges });
                const resultFaces = nearest.distance < 5e-5 * meter ? evaluateQuery(context, qAdjacent(resultEdgeList[nearest.sides[1].index], AdjacencyType.EDGE, EntityType.FACE)) : [];
                if (size(resultFaces) != 2)
                {
                    matched = false;
                    continue;
                }
                resultAngle = max(resultAngle, planeAngleAt(context, resultFaces[0], resultFaces[1], p));
            }
            if (sourceAngle < 1)
                smooth += 1;
            if (!matched)
                unmatched += 1;
            else
                rows = append(rows, { "source" : sourceAngle, "result" : resultAngle, "length" : evLength(context, { "entities" : e }) / meter * 1e3,
                                      "x" : mapped[1][0] / meter * 1e3, "y" : mapped[1][1] / meter * 1e3, "z" : mapped[1][2] / meter * 1e3 });
        }
        var lost = [];
        for (var r in rows)
            if (r.source < 1 && r.result > 1)
                lost = append(lost, r);
        lost = sort(lost, function(a, b) { return b.result - a.result; });
        println("CONTINUITY " ~ size(rows) ~ " two-face source edges measured (" ~ smooth ~ " smooth in the source, " ~ unmatched ~ " not found in the result): "
            ~ size(lost) ~ " smooth edges are NOT smooth after the deform (over 1 degree)");
        for (var k = 0; k < min(size(lost), 12); k += 1)
            println("CONTINUITY   lost #" ~ k ~ ": " ~ roundToPrecision(lost[k].source, 2) ~ " deg -> " ~ roundToPrecision(lost[k].result, 2) ~ " deg, edge "
                ~ roundToPrecision(lost[k].length, 1) ~ " mm long at (" ~ roundToPrecision(lost[k].x, 0) ~ ", " ~ roundToPrecision(lost[k].y, 0) ~ ", " ~ roundToPrecision(lost[k].z, 0) ~ ") mm");
        var worstSharp = [];
        for (var r in rows)
            if (r.source >= 1 && abs(r.result - r.source) > 2)
                worstSharp = append(worstSharp, r);
        println("CONTINUITY " ~ size(worstSharp) ~ " creased source edges changed their angle by more than 2 degrees");
    }
    catch (continuityErr)
    {
        println("CONTINUITY could not be measured (" ~ toString(continuityErr) ~ ")");
    }
}

/**
 * Debug: how the wrapped edges were fitted: control points per edge, how many needed a refit, and (when an edge accuracy is
 * set) the measured deviation from the exact image of the source edge. Read from what the fit stored: nothing is re-measured.
 */
function reportEdgeFit(edgeMapping is array)
{
    var counts = [];
    var refit = 0;
    var rows = [];
    for (var m in edgeMapping)
    {
        if (m.controlPoints == undefined)
            continue;
        counts = append(counts, m.controlPoints);
        if (m.fitPasses > 1)
            refit += 1;
        if (m.deviation != undefined)
            rows = append(rows, { "dev" : m.deviation / meter * 1e6, "cps" : m.controlPoints, "passes" : m.fitPasses });
    }
    counts = sort(counts, function(a, b) { return a - b; });
    rows = sort(rows, function(a, b) { return b.dev - a.dev; });
    println("EDGEFIT " ~ size(counts) ~ " fitted edges, control points min " ~ (size(counts) > 0 ? counts[0] : 0) ~ " median " ~ (size(counts) > 0 ? counts[floor(size(counts) / 2)] : 0)
        ~ " max " ~ (size(counts) > 0 ? counts[size(counts) - 1] : 0) ~ ", " ~ refit ~ " refitted with a higher limit"
        ~ (size(rows) > 0 ? ", worst measured deviation " ~ roundToPrecision(rows[0].dev, 2) ~ " um" : ""));
    for (var k = 0; k < min(size(rows), 6); k += 1)
        println("EDGEFIT   worst #" ~ k ~ ": " ~ roundToPrecision(rows[k].dev, 2) ~ " um with " ~ rows[k].cps ~ " control points after " ~ rows[k].passes ~ " fit(s)");
}

/** At most this many faces per body get their surface mapped with MAP_THIN (every mapped point costs interpreter steps). */
const MAX_MAPPED_FACES = 6;
/** Samples per direction of a mapped surface (at least, at most). */
const MAPPED_SAMPLES_MIN_U = 12;
const MAPPED_SAMPLES_MAX_U = 400;
const MAPPED_SAMPLES_MIN_V = 8;
const MAPPED_SAMPLES_MAX_V = 40;
/** Points mapped for mapped surfaces in one feature (each costs interpreter steps; the budget of a whole feature is about 20-30k). */
const MAPPED_POINT_BUDGET = 20000;
/** How far a mapped surface may lie from the exactly mapped face between its samples (default 2 microns): lengths in meter [min, default, max] and the other units. */
const MAPPED_ACCURACY_BOUND = { (meter) : [1e-7, 2e-6, 1e-4], (centimeter) : 0.0002, (millimeter) : 0.002, (inch) : 0.00008, (foot) : 0.000007, (yard) : 0.0000022 } as LengthBoundSpec;
/** Times the sampling grid of a mapped surface may be refined (each time the directions that miss the accuracy get twice the intervals). */
const MAPPED_REFINE_PASSES = 5;
/** Sample spacing along a mapped surface (the old control, no longer used: the grid follows the measured error). */
const MAPPED_SPACING_BOUND = { (meter) : [1e-3, 6e-3, 5e-2], (centimeter) : 0.6, (millimeter) : 6, (inch) : 0.24, (foot) : 0.02, (yard) : 0.0066 } as LengthBoundSpec;
/** A wrapped edge farther than this from an edge of a built sheet is not matched with it. */
const JOIN_MATCH_GATE = 2e-5 * meter;
/** A sample step of this much in the trim loop (meters of edge) between UV samples. */
const OUTLINE_SAMPLE_SPACING = 0.004 * meter;
/** Most UV samples taken along one outline edge. */
const OUTLINE_MAX_SAMPLES = 300;

/** Values of all n basis functions of a clamped B-spline (degree, knots) at u. */
function basisValues(knots is array, degree is number, n is number, u is number) returns array
{
    var span = degree;
    while (span < n - 1 && u >= knots[span + 1])
        span += 1;
    var left = makeArray(degree + 1, 0);
    var right = makeArray(degree + 1, 0);
    var basis = makeArray(degree + 1, 0);
    basis[0] = 1;
    for (var j = 1; j <= degree; j += 1)
    {
        left[j] = u - knots[span + 1 - j];
        right[j] = knots[span + j] - u;
        var saved = 0;
        for (var r = 0; r < j; r += 1)
        {
            const denominator = right[r + 1] + left[j - r];
            const ratio = denominator == 0 ? 0 : basis[r] / denominator;
            basis[r] = saved + right[r + 1] * ratio;
            saved = left[j - r] * ratio;
        }
        basis[j] = saved;
    }
    var values = makeArray(n, 0);
    for (var i = 0; i <= degree; i += 1)
        values[span - degree + i] = basis[i];
    return values;
}

/** The clamped knot vector of the global interpolation of data at the (increasing, 0..1) parameters (knot averaging). */
function interpolationKnots(parameters is array, degree is number) returns array
{
    const n = size(parameters);
    var knots = makeArray(degree + 1, 0);
    for (var j = 1; j <= n - degree - 1; j += 1)
    {
        var sum = 0;
        for (var i = j; i < j + degree; i += 1)
            sum += parameters[i];
        knots = append(knots, sum / degree);
    }
    return concatenateArrays([knots, makeArray(degree + 1, 1)]);
}

/** The collocation matrix N_k(parameter_i) of the interpolation. */
function collocationMatrix(parameters is array, knots is array, degree is number) returns Matrix
{
    var rows = [];
    for (var u in parameters)
        rows = append(rows, basisValues(knots, degree, size(parameters), u));
    return matrix(rows);
}

/**
 * The UV outline (in the face's normalized parameter space, the one evFaceTangentPlanes uses) of a face with one outer loop, as
 * closed, joined 2D B-spline curves for opCreateBSplineSurface. `undefined` when the face has holes, wraps around its surface
 * (an outline that jumps across the parameter range) or the walk of its edges does not close.
 */
function faceUvOutline(context is Context, face is Query)
{
    const edges = evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE));
    const n = size(edges);
    if (n < 1)
        return undefined;
    var ends = [];
    for (var e in edges)
    {
        const l = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 1] });
        ends = append(ends, [l[0].origin, l[1].origin]);
    }
    var order = [0];
    var forward = [true];
    var used = makeArray(n, false);
    used[0] = true;
    var tail = ends[0][1];
    for (var step = 1; step < n; step += 1)
    {
        var next = -1;
        var nextForward = true;
        for (var k = 0; k < n && next < 0; k += 1)
        {
            if (used[k])
                continue;
            if (norm(ends[k][0] - tail) < OUTLINE_VERTEX_TOLERANCE)
            {
                next = k;
                nextForward = true;
            }
            else if (norm(ends[k][1] - tail) < OUTLINE_VERTEX_TOLERANCE)
            {
                next = k;
                nextForward = false;
            }
        }
        if (next < 0)
            return undefined;          // a second loop (a hole) or an open outline
        used[next] = true;
        order = append(order, next);
        forward = append(forward, nextForward);
        tail = nextForward ? ends[next][1] : ends[next][0];
    }
    if (norm(tail - ends[0][0]) > OUTLINE_VERTEX_TOLERANCE)
        return undefined;

    var curves = [];
    var previousEnd = undefined;
    for (var k = 0; k < n; k += 1)
    {
        const edge = edges[order[k]];
        const length = evLength(context, { "entities" : edge });
        const count = clamp(ceil(length / OUTLINE_SAMPLE_SPACING) + 1, 6, OUTLINE_MAX_SAMPLES);
        var parameters = range(0, 1, count);
        if (!forward[k])
            parameters = reverse(parameters);
        const points = mapArray(evEdgeTangentLines(context, { "edge" : edge, "parameters" : parameters }), function(l) { return l.origin; });
        var uv = [];
        for (var point in points)
        {
            const on = evDistance(context, { "side0" : point, "side1" : face }).sides[1].parameter;
            uv = append(uv, on);
        }
        if (previousEnd != undefined)
            uv[0] = previousEnd;
        for (var j = 1; j < size(uv); j += 1)
            if (norm(uv[j] - uv[j - 1]) > 0.5)
                return undefined;      // the outline runs around the surface: not a single closed loop in this parameter box
        previousEnd = uv[size(uv) - 1];
        // The curve through the samples, fitted in a fake length unit (1 = 1 meter) and turned into a 2D curve.
        var positions = [];
        for (var q in uv)
            positions = append(positions, vector(q[0], q[1], 0) * meter);
        const fitted = approximateSpline(context, { "targets" : [approximationTarget({ "positions" : positions })], "tolerance" : 1e-8,
                                                    "maxControlPoints" : 300, "degree" : 3, "isPeriodic" : false })[0];
        var controlPoints = mapArray(fitted.controlPoints, function(c) { return vector(c[0] / meter, c[1] / meter); });
        controlPoints[0] = uv[0];
        controlPoints[size(controlPoints) - 1] = uv[size(uv) - 1];
        curves = append(curves, bSplineCurve({ "degree" : fitted.degree, "isPeriodic" : false, "controlPoints" : controlPoints, "knots" : fitted.knots }));
    }
    return curves;
}

/** The non-zero basis functions of a clamped B-spline at u: { "first" : index of the first, "values" : degree + 1 values }. */
function basisNonzero(knots is array, degree is number, n is number, u is number) returns map
{
    var span = degree;
    while (span < n - 1 && u >= knots[span + 1])
        span += 1;
    var left = makeArray(degree + 1, 0);
    var right = makeArray(degree + 1, 0);
    var basis = makeArray(degree + 1, 0);
    basis[0] = 1;
    for (var j = 1; j <= degree; j += 1)
    {
        left[j] = u - knots[span + 1 - j];
        right[j] = knots[span + j] - u;
        var saved = 0;
        for (var r = 0; r < j; r += 1)
        {
            const denominator = right[r + 1] + left[j - r];
            const ratio = denominator == 0 ? 0 : basis[r] / denominator;
            basis[r] = saved + right[r + 1] * ratio;
            saved = left[j - r] * ratio;
        }
        basis[j] = saved;
    }
    return { "first" : span - degree, "values" : basis };
}

/** Whether the sample grid has extra samples at the ends (off: the server failed on the clustered grids, to be investigated). */
const USE_END_CLUSTERS = false;

/**
 * The sample parameters of one direction: n evenly spaced over 0..1 and, at each end, three more halving the first interval
 * (1/2, 1/4, 1/8 of it): the parts of a face next to a strongly bending part of the path change fastest there, and a uniform grid
 * is least accurate at the very ends, which is where a neighbouring face has to meet.
 */
function clusteredParameters(n is number) returns array
{
    const step = 1 / (n - 1);
    if (!USE_END_CLUSTERS)
    {
        var uniform = [];
        for (var i = 0; i < n; i += 1)
            uniform = append(uniform, i * step);
        uniform[n - 1] = 1;
        return uniform;
    }
    var parameters = [0];
    for (var k in [1 / 8, 1 / 4, 1 / 2])
        parameters = append(parameters, step * k);
    for (var i = 1; i < n - 1; i += 1)
        parameters = append(parameters, i * step);
    for (var k in [1 / 2, 1 / 4, 1 / 8])
        parameters = append(parameters, 1 - step * k);
    return append(parameters, 1);
}

/** The parameters with a new one halfway between every two neighbours (the old ones keep the even indices). */
function refinedParameters(parameters is array) returns array
{
    var refined = [];
    for (var i = 0; i < size(parameters) - 1; i += 1)
    {
        refined = append(refined, parameters[i]);
        refined = append(refined, (parameters[i] + parameters[i + 1]) / 2);
    }
    return append(refined, parameters[size(parameters) - 1]);
}

/**
 * The exact images of the face sampled on an nU x nV grid of its parameter box. With `previous` (the samples of the grid this one
 * refines: { "nU", "nV", "mapped", "doU", "doV" }, where a direction that was refined has twice the intervals, so the old samples are
 * at the even indices) only the new samples are mapped: refining costs the new points, not the whole grid.
 * @returns {map} { "mapped" : nU x nV array of positions, "points" : how many were mapped }
 */
function mapGridSamples(context is Context, face is Query, us is array, vs is array, previous, fromFrenetPath is map, toFrenetPath is map,
    fromRefArc, toRefArc, flipToNormal is boolean) returns map
{
    const nU = size(us);
    const nV = size(vs);
    var mapped = makeArray(nU);
    var known = makeArray(nU);
    for (var i = 0; i < nU; i += 1)
    {
        mapped[i] = makeArray(nV);
        known[i] = makeArray(nV, false);
    }
    if (previous != undefined)
    {
        for (var i = 0; i < previous.nU; i += 1)
            for (var j = 0; j < previous.nV; j += 1)
            {
                const i2 = previous.doU ? 2 * i : i;
                const j2 = previous.doV ? 2 * j : j;
                mapped[i2][j2] = previous.mapped[i][j];
                known[i2][j2] = true;
            }
    }
    var parameters = [];
    var slots = [];
    for (var j = 0; j < nV; j += 1)
        for (var i = 0; i < nU; i += 1)
            if (!known[i][j])
            {
                parameters = append(parameters, vector(us[i], vs[j]));
                slots = append(slots, [i, j]);
            }
    if (size(slots) == 0)
        return { "mapped" : mapped, "points" : 0 };
    const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : parameters });
    // one chain per v (the slots are grouped by v, u increasing)
    var from = 0;
    while (from < size(slots))
    {
        var to = from;
        while (to + 1 < size(slots) && slots[to + 1][1] == slots[from][1])
            to += 1;
        var chain = [];
        for (var k = from; k <= to; k += 1)
            chain = append(chain, planes[k].origin);
        const images = mapWorldPointChain(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal, chain);
        for (var k = from; k <= to; k += 1)
            mapped[slots[k][0]][slots[k][1]] = images[k - from];
        from = to + 1;
    }
    return { "mapped" : mapped, "points" : size(slots) };
}

/**
 * The interpolating tensor B-spline through the mapped samples (the images of a uniform grid of the face's parameter box).
 * @returns {map} { "us", "vs", "knotsU", "knotsV", "degreeU", "degreeV", "coordinates" (three matrices, meters), "surface" }
 */
function interpolateGrid(mapped is array, us is array, vs is array) returns map
{
    const nU = size(us);
    const nV = size(vs);

    // Interpolate: P = A^-1 Q B^-T, once per coordinate.
    const degreeU = min(3, nU - 1);
    const degreeV = min(3, nV - 1);
    const knotsU = interpolationKnots(us, degreeU);
    const knotsV = interpolationKnots(vs, degreeV);
    const inverseU = inverse(collocationMatrix(us, knotsU, degreeU));
    const inverseVT = transpose(inverse(collocationMatrix(vs, knotsV, degreeV)));
    var coordinates = [];
    for (var c = 0; c < 3; c += 1)
    {
        var rows = [];
        for (var i = 0; i < nU; i += 1)
        {
            var row = [];
            for (var j = 0; j < nV; j += 1)
                row = append(row, mapped[i][j][c] / meter);
            rows = append(rows, row);
        }
        coordinates = append(coordinates, inverseU * matrix(rows) * inverseVT);
    }
    var grid = [];
    for (var i = 0; i < nU; i += 1)
    {
        var row = [];
        for (var j = 0; j < nV; j += 1)
            row = append(row, vector(coordinates[0][i][j], coordinates[1][i][j], coordinates[2][i][j]) * meter);
        grid = append(grid, row);
    }
    const surface = bSplineSurface({ "uDegree" : degreeU, "vDegree" : degreeV, "isUPeriodic" : false, "isVPeriodic" : false,
                                     "controlPoints" : controlPointMatrix(grid), "uKnots" : knotArray(knotsU), "vKnots" : knotArray(knotsV) });
    return { "us" : us, "vs" : vs, "knotsU" : knotsU, "knotsV" : knotsV, "degreeU" : degreeU, "degreeV" : degreeV, "coordinates" : coordinates, "surface" : surface };
}

/** The point of a mappedGrid surface at (u, v), from its non-zero basis functions only. */
function mappedGridPoint(grid is map, u is number, v is number) returns Vector
{
    const bu = basisNonzero(grid.knotsU, grid.degreeU, size(grid.us), u);
    const bv = basisNonzero(grid.knotsV, grid.degreeV, size(grid.vs), v);
    var sum = [0, 0, 0];
    for (var a = 0; a <= grid.degreeU; a += 1)
        for (var b = 0; b <= grid.degreeV; b += 1)
        {
            const w = bu.values[a] * bv.values[b];
            for (var c = 0; c < 3; c += 1)
                sum[c] += w * grid.coordinates[c][bu.first + a][bv.first + b];
        }
    return vector(sum[0], sum[1], sum[2]) * meter;
}

/**
 * How far the interpolated surface is from the exactly mapped face BETWEEN its samples, where a cubic interpolation is least
 * accurate: probes at interval midpoints along u (on a few v sample lines), along v (on a few u sample lines) and at cell
 * centres. Returns { "u", "v", "centre" } (worst distances) and "points" (the mappings spent).
 */
function mappedGridError(context is Context, face is Query, grid is map, fromFrenetPath is map, toFrenetPath is map,
    fromRefArc, toRefArc, flipToNormal is boolean) returns map
{
    const nU = size(grid.us);
    const nV = size(grid.vs);
    var uIntervals = [];
    const uProbeCount = nU > 40 ? 8 : 4;
    var uProbeIndices = [];
    for (var q = 0; q < uProbeCount; q += 1)
        uProbeIndices = append(uProbeIndices, floor(q * (nU - 2) / (uProbeCount - 1)));
    for (var k in uProbeIndices)
        if (k >= 0 && k <= nU - 2 && indexOf(uIntervals, k) < 0)
            uIntervals = append(uIntervals, k);
    var vIntervals = [];
    for (var k in [0, nV - 2])
        if (k >= 0 && k <= nV - 2 && indexOf(vIntervals, k) < 0)
            vIntervals = append(vIntervals, k);
    var vLines = [];
    for (var k in [0, floor((nV - 1) / 2), nV - 1])
        if (indexOf(vLines, k) < 0)
            vLines = append(vLines, k);

    // each chain: one v, increasing u
    var chains = [];
    for (var kv in vLines)
    {
        var chain = [];
        for (var ku in uIntervals)
            chain = append(chain, { "u" : (grid.us[ku] + grid.us[ku + 1]) / 2, "v" : grid.vs[kv], "kind" : "u" });
        chains = append(chains, chain);
    }
    for (var kv in vIntervals)
    {
        var onSamples = [];
        var atCentres = [];
        for (var ku in uIntervals)
        {
            onSamples = append(onSamples, { "u" : grid.us[ku], "v" : (grid.vs[kv] + grid.vs[kv + 1]) / 2, "kind" : "v" });
            atCentres = append(atCentres, { "u" : (grid.us[ku] + grid.us[ku + 1]) / 2, "v" : (grid.vs[kv] + grid.vs[kv + 1]) / 2, "kind" : "centre" });
        }
        chains = append(chains, onSamples);
        chains = append(chains, atCentres);
    }
    var worst = { "u" : 0 * meter, "v" : 0 * meter, "centre" : 0 * meter };
    var points = 0;
    for (var chain in chains)
    {
        const planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : mapArray(chain, function(q) { return vector(q.u, q.v); }) });
        const images = mapWorldPointChain(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal,
            mapArray(planes, function(pl) { return pl.origin; }));
        points += size(chain);
        for (var k = 0; k < size(chain); k += 1)
            worst[chain[k].kind] = max(worst[chain[k].kind], norm(images[k] - mappedGridPoint(grid, chain[k].u, chain[k].v)));
    }
    return { "u" : worst.u, "v" : worst.v, "centre" : worst.centre, "points" : points };
}

/** Whether the refinement also probes the face's own edges (off: on the ski they never changed a grid and cost budget the long faces need). */
const USE_BOUNDARY_PROBES = false;

/**
 * Probes on the face's own edges, which is what neighbouring faces must meet: for a few points on every edge, the parameter
 * position on the face and the exactly mapped image. The interpolation probes sit between samples on a few lines of the grid and can
 * miss the boundary of a face whose outline is a curve in parameter space.
 * @returns {array} [{ "u", "v", "image" }, ...]
 */
function boundaryProbes(context is Context, face is Query, fromFrenetPath is map, toFrenetPath is map, fromRefArc, toRefArc, flipToNormal is boolean) returns array
{
    var probes = [];
    const edges = evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE));
    const parameters = size(edges) > 8 ? [0.5] : [0.2, 0.5, 0.8];
    for (var edge in edges)
    {
        try silent
        {
            const onEdge = mapArray(evEdgeTangentLines(context, { "edge" : edge, "parameters" : parameters }), function(l) { return l.origin; });
            const images = mapWorldPointChain(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal, onEdge);
            for (var k = 0; k < size(onEdge); k += 1)
            {
                const uv = evDistance(context, { "side0" : onEdge[k], "side1" : face }).sides[1].parameter;
                probes = append(probes, { "u" : uv[0], "v" : uv[1], "image" : images[k] });
            }
        }
    }
    return probes;
}

/** The worst distance from the exactly mapped boundary probes to the interpolated surface. */
function boundaryError(grid is map, probes is array) returns ValueWithUnits
{
    var worst = 0 * meter;
    for (var probe in probes)
        worst = max(worst, norm(probe.image - mappedGridPoint(grid, clamp(probe.u, 0, 1), clamp(probe.v, 0, 1))));
    return worst;
}

/**
 * Build the deformed face by mapping the SURFACE of the source face: the face is sampled on a grid of its own parameter box, every
 * sample is mapped exactly, the mapped samples are interpolated by a cubic tensor B-spline surface, and the surface is trimmed with
 * the face's own outline. Interior shape and tangency are carried through the deform (the surface is mapped, not rebuilt from its
 * boundary).
 *
 * The grid is not fixed: it starts at twice the control net of the source face's own approximation, the surface is compared with
 * exactly mapped probe points between its samples (mappedGridError), and the directions that miss `accuracy` get twice the
 * intervals, up to MAPPED_REFINE_PASSES times, the sample caps and the point budget. A face the map barely bends stays coarse (few
 * control points); a face near a strongly bending part of the path gets the density it needs, so neighbouring faces' edges meet.
 *
 * @returns {number} the number of points mapped when the sheet was made under `id`; 0, having left nothing, when the face does not qualify
 */
function mappedSurfaceFace(context is Context, id is Id, face is Query, fromFrenetPath is map, toFrenetPath is map,
    fromRefArc, toRefArc, flipToNormal is boolean, accuracy is ValueWithUnits, pointBudget is number) returns number
{
    try
    {
        const approximation = evApproximateBSplineSurface(context, { "face" : face, "tolerance" : 1e-6 });
        const net = approximation.bSplineSurface.controlPoints;
        var nU = clamp(size(net), MAPPED_SAMPLES_MIN_U, MAPPED_SAMPLES_MAX_U);
        var nV = clamp(size(net[0]), MAPPED_SAMPLES_MIN_V, MAPPED_SAMPLES_MAX_V);
        // A face that does not fit the budget at its natural size is sampled more coarsely rather than not at all (a fill cannot
        // rebuild a long thin face); the probes then say how far from the exact map it is.
        if (nU * nV > pointBudget)
            nU = max(MAPPED_SAMPLES_MIN_U, floor(pointBudget / nV));
        if (nU * nV > pointBudget)
            return 0;
        var us = clusteredParameters(nU);
        var vs = clusteredParameters(nV);
        nU = size(us);
        nV = size(vs);
        if (nU * nV > pointBudget)
            return 0;
        var samples = mapGridSamples(context, face, us, vs, undefined, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal);
        var spent = samples.points;
        var grid = interpolateGrid(samples.mapped, us, vs);
        const probes = USE_BOUNDARY_PROBES ? boundaryProbes(context, face, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal) : [];
        spent += size(probes);
        // physical length of the face along u and v: where the boundary misses, the direction with the longer sample spacing is refined
        const extent = evFaceTangentPlanes(context, { "face" : face, "parameters" : [vector(0, 0.5), vector(1, 0.5), vector(0.5, 0), vector(0.5, 1)] });
        const lengthU = norm(extent[1].origin - extent[0].origin);
        const lengthV = norm(extent[3].origin - extent[2].origin);
        for (var pass = 0; pass < MAPPED_REFINE_PASSES; pass += 1)
        {
            const error = mappedGridError(context, face, grid, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal);
            spent += error.points;
            const edgeMiss = boundaryError(grid, probes) > accuracy;
            const uLonger = lengthU / (nU - 1) >= lengthV / (nV - 1);
            const refineU = (error.u > accuracy || error.centre > accuracy || (edgeMiss && uLonger)) && 2 * nU - 1 <= MAPPED_SAMPLES_MAX_U + 6;
            const refineV = (error.v > accuracy || error.centre > accuracy || (edgeMiss && !uLonger)) && 2 * nV - 1 <= MAPPED_SAMPLES_MAX_V + 6;
            if (!refineU && !refineV)
                break;
            const nextU = refineU ? 2 * nU - 1 : nU;
            const nextV = refineV ? 2 * nV - 1 : nV;
            // only the new samples are mapped: the grid's points are all but the old ones
            if (spent + nextU * nextV - nU * nV > pointBudget)
                break;
            const nextUs = refineU ? refinedParameters(us) : us;
            const nextVs = refineV ? refinedParameters(vs) : vs;
            samples = mapGridSamples(context, face, nextUs, nextVs, { "nU" : nU, "nV" : nV, "mapped" : samples.mapped, "doU" : refineU, "doV" : refineV },
                fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, flipToNormal);
            spent += samples.points;
            us = nextUs;
            vs = nextVs;
            nU = nextU;
            nV = nextV;
            grid = interpolateGrid(samples.mapped, us, vs);
        }

        const outline = faceUvOutline(context, face);
        if (outline == undefined)
            return 0;
        opCreateBSplineSurface(context, id + "mapped", { "bSplineSurface" : grid.surface, "boundaryBSplineCurves" : outline });
        if (spent > 1500)
            println("MAPPED grid " ~ nU ~ " x " ~ nV ~ ", " ~ spent ~ " points mapped (budget left " ~ (pointBudget - spent) ~ ")");
        return spent;
    }
    catch (mappedErr)
    {
        var faceNote = "";
        try silent
        {
            const surfaceKind = evSurfaceDefinition(context, { "face" : face }).surfaceType;
            const outlineEdges = evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE));
            var closedEdges = 0;
            var vertexless = 0;
            for (var oe in outlineEdges)
            {
                const ov = size(evaluateQuery(context, qAdjacent(oe, AdjacencyType.VERTEX, EntityType.VERTEX)));
                if (ov < 2)
                    closedEdges += 1;
                if (ov == 0)
                    vertexless += 1;
            }
            faceNote = " | " ~ toString(surfaceKind) ~ ", " ~ size(outlineEdges) ~ " edges (" ~ closedEdges ~ " closed, " ~ vertexless ~ " without a vertex), area "
                ~ roundToPrecision(evArea(context, { "entities" : face }) / (millimeter ^ 2), 1) ~ " mm2";
        }
        println("mapped surface refused: " ~ toString(mappedErr) ~ faceNote);
        try silent
        {
            opDeleteBodies(context, id + "dropMapped", { "entities" : qCreatedBy(id, EntityType.BODY) });
        }
        return 0;
    }
}

/** The edge of `edges` nearest to a point, and how far it is. */
function nearestEdgeTo(context is Context, point is Vector, edges is Query) returns map
{
    const nearest = evDistance(context, { "side0" : point, "side1" : edges });
    return { "edge" : evaluateQuery(context, edges)[nearest.sides[1].index], "distance" : nearest.distance };
}

/** Whether the union is given vertex-to-vertex matches as well as edge matches (off while it is checked that the kernel accepts them). */
const USE_VERTEX_MATCHES = false;

/**
 * Vertex matches for two edges that are to be welded: each end vertex of the first with the vertex of the second that lies at
 * it (the faces' corners are the images of one source vertex, but each face's own surface puts its corner a fraction of a micron
 * off; unmatched, the union leaves a micro edge between the two corners).
 */
function vertexMatches(context is Context, edgeA is Query, edgeB is Query, gate is ValueWithUnits) returns array
{
    if (!USE_VERTEX_MATCHES)
        return [];
    var found = [];
    const verticesB = evaluateQuery(context, qAdjacent(edgeB, AdjacencyType.VERTEX, EntityType.VERTEX));
    for (var va in evaluateQuery(context, qAdjacent(edgeA, AdjacencyType.VERTEX, EntityType.VERTEX)))
    {
        const pa = evVertexPoint(context, { "vertex" : va });
        for (var vb in verticesB)
        {
            if (norm(evVertexPoint(context, { "vertex" : vb }) - pa) < gate)
            {
                found = append(found, { "topology1" : va, "topology2" : vb, "matchType" : TopologyMatchType.COINCIDENT });
                break;
            }
        }
    }
    return found;
}

/** The matches without repeats (a corner is shared by several edges). */
function uniqueMatches(matches is array) returns array
{
    var seen = {};
    var result = [];
    for (var m in matches)
    {
        const key = toString(m.topology1) ~ "|" ~ toString(m.topology2);
        if (seen[key] != undefined)
            continue;
        seen[key] = true;
        result = append(result, m);
    }
    return result;
}

/**
 * The edge matches for the union of the rebuilt faces. Every source edge between two faces became one wrapped edge, and each
 * of the two rebuilt faces has an edge that follows it; those two edges are meant to coincide but, being the boundaries of
 * two separately built surfaces, can be a fraction of a micron apart, which the kernel will not weld on its own. Telling it
 * which edges belong together (the way the standard Fill feature joins its surface) lets it weld them.
 */
function weldMatches(context is Context, edgeMapping is array, fillBodies is map) returns array
{
    var matches = [];
    for (var m in edgeMapping)
    {
        const adjacent = evaluateQuery(context, qAdjacent(m.sourceEdge, AdjacencyType.EDGE, EntityType.FACE));
        if (size(adjacent) != 2)
            continue;
        const bodyA = fillBodies[toString(adjacent[0])];
        const bodyB = fillBodies[toString(adjacent[1])];
        if (bodyA == undefined || bodyB == undefined)
            continue;
        const middle = evEdgeTangentLine(context, { "edge" : m.wrappedEdge, "parameter" : 0.5 }).origin;
        const a = nearestEdgeTo(context, middle, qOwnedByBody(bodyA, EntityType.EDGE));
        const b = nearestEdgeTo(context, middle, qOwnedByBody(bodyB, EntityType.EDGE));
        if (a.distance < 5e-5 * meter && b.distance < 5e-5 * meter)
        {
            matches = append(matches, { "topology1" : a.edge, "topology2" : b.edge, "matchType" : TopologyMatchType.COINCIDENT });
            matches = concatenateArrays([matches, vertexMatches(context, a.edge, b.edge, 5e-5 * meter)]);
        }
    }
    return uniqueMatches(matches);
}

/** The adjacency of the rebuilt faces for incrementalJoin: for every source edge between two faces, a point on its wrapped edge and the two faces. */
function joinLinks(context is Context, edgeMapping is array, fillBodies is map) returns array
{
    var links = [];
    for (var m in edgeMapping)
    {
        const adjacent = evaluateQuery(context, qAdjacent(m.sourceEdge, AdjacencyType.EDGE, EntityType.FACE));
        if (size(adjacent) != 2)
            continue;
        const ka = toString(adjacent[0]);
        const kb = toString(adjacent[1]);
        if (fillBodies[ka] == undefined || fillBodies[kb] == undefined || ka == kb)
            continue;
        links = append(links, { "middle" : evEdgeTangentLine(context, { "edge" : m.wrappedEdge, "parameter" : 0.5 }).origin, "a" : ka, "b" : kb });
    }
    return links;
}

/**
 * The faces joined one at a time, breadth first from the largest, each join in a try of its own, with the edge matches found
 * against the bodies as they are at that step (`links` from joinLinks). A face the kernel refuses stays a separate sheet and
 * the others go on, so what can be joined is joined and the refusals are named.
 *
 * @returns {map} { "joined" : faces in the main body, "left" : faces left as separate sheets }
 */
function incrementalJoin(context is Context, id is Id, links is array, fillBodies is map, makeSolid is boolean) returns map
{
    var current = fillBodies;
    var seed = undefined;
    var seedArea = 0 * meter ^ 2;
    for (var entry in fillBodies)
    {
        const area = evArea(context, { "entities" : entry.value });
        if (seed == undefined || area > seedArea)
        {
            seed = entry.key;
            seedArea = area;
        }
    }
    if (seed == undefined)
        return { "joined" : 0, "left" : 0 };
    var done = { (seed) : true };
    var refused = {};
    var step = 0;
    var progress = true;
    while (progress)
    {
        progress = false;
        for (var entry in fillBodies)
        {
            const key = entry.key;
            if (done[key] != undefined || refused[key] != undefined)
                continue;
            var matches = [];
            var touching = false;
            for (var link in links)
            {
                var other = undefined;
                if (link.a == key && done[link.b] != undefined)
                    other = link.b;
                else if (link.b == key && done[link.a] != undefined)
                    other = link.a;
                if (other == undefined)
                    continue;
                touching = true;
                const a = nearestEdgeTo(context, link.middle, qOwnedByBody(current[other], EntityType.EDGE));
                const b = nearestEdgeTo(context, link.middle, qOwnedByBody(current[key], EntityType.EDGE));
                if (a.distance < JOIN_MATCH_GATE && b.distance < JOIN_MATCH_GATE)
                {
                    matches = append(matches, { "topology1" : a.edge, "topology2" : b.edge, "matchType" : TopologyMatchType.COINCIDENT });
                    matches = concatenateArrays([matches, vertexMatches(context, a.edge, b.edge, JOIN_MATCH_GATE)]);
                }
            }
            if (!touching)
                continue;
            step += 1;
            const joinId = id + ("join" ~ step);
            try
            {
                opBoolean(context, joinId, { "tools" : qUnion([current[seed], current[key]]), "operationType" : BooleanOperationType.UNION,
                                             "makeSolid" : false, "allowSheets" : true, "eraseImprintedEdges" : true,
                                             "matches" : uniqueMatches(matches), "recomputeMatches" : true });
                const merged = qBodyType(qCreatedBy(joinId, EntityType.BODY), BodyType.SHEET);
                for (var member in done)
                    current[member.key] = merged;
                current[key] = merged;
                done[key] = true;
                progress = true;
            }
            catch (joinErr)
            {
                refused[key] = true;
                println("join refused a face: " ~ toString(joinErr) ~ " (" ~ size(matches) ~ " edge matches)");
            }
        }
    }
    var left = 0;
    for (var entry in fillBodies)
        if (done[entry.key] == undefined)
            left += 1;
    return { "joined" : size(done), "left" : left };
}

/** Whether (diagonal of the face's box)^2 / its area is above `aspect`. */
function isAspectAbove(context is Context, face is Query, aspect is number) returns boolean
{
    const faceBox = evBox3d(context, { "topology" : face });
    const diagonal = norm(faceBox.maxCorner - faceBox.minCorner);
    return diagonal * diagonal > aspect * evArea(context, { "entities" : face });
}

/** Whether a face is long and thin enough that a fill of its outline may flatten it (see THIN_FACE_ASPECT). */
function isThinAndLong(context is Context, face is Query) returns boolean
{
    const faceBox = evBox3d(context, { "topology" : face });
    const diagonal = norm(faceBox.maxCorner - faceBox.minCorner);
    return diagonal * diagonal > THIN_FACE_ASPECT * evArea(context, { "entities" : face });
}

/** The largest distance from the exactly mapped interior points of a source face to the rebuilt face. */
function faceMaxDeviation(context is Context, id is Id, face is Query, filled is Query,
    fromFrenetPath is map, toFrenetPath is map, settings is map) returns ValueWithUnits
{
    var worst = 0 * meter;
    for (var p in transformFacepoints(context, id, face, 2, 2, fromFrenetPath, toFrenetPath, settings))
        worst = max(worst, evDistance(context, { "side0" : p, "side1" : filled }).distance);
    return worst;
}

/**
 * Debug: how far a rebuilt face lies from where the wrap says it should be. Interior points of the source face are
 * mapped exactly (as the guide points are), and the distance from each to the rebuilt face is printed.
 */
function reportFaceAccuracy(context is Context, id is Id, face is Query, filled is Query, label is string,
    fromFrenetPath is map, toFrenetPath is map, settings is map)
{
    try
    {
        const points = transformFacepoints(context, id, face, 3, 3, fromFrenetPath, toFrenetPath, settings);
        var worst = 0 * meter;
        var sum = 0 * meter;
        for (var p in points)
        {
            const d = evDistance(context, { "side0" : p, "side1" : filled }).distance;
            worst = max(worst, d);
            sum += d;
        }
        // The boundary: exactly mapped points of every source edge against the rebuilt face's own edges (what a neighbour has to meet).
        var boundaryWorst = 0 * meter;
        var boundaryNote = "";
        const rebuiltEdges = qAdjacent(filled, AdjacencyType.EDGE, EntityType.EDGE);
        for (var sourceEdge in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
        {
            try silent
            {
                const reportParameters = [0.05, 0.15, 0.25, 0.35, 0.45, 0.55, 0.65, 0.75, 0.85, 0.95];
                const onEdge = mapArray(evEdgeTangentLines(context, { "edge" : sourceEdge, "parameters" : reportParameters }), function(x) { return x.origin; });
                const images = mapWorldPointChain(context, fromFrenetPath, toFrenetPath, settings.fromRefArc, settings.toRefArc, settings.flipToNormal, onEdge);
                var edgeWorst = 0 * meter;
                var worstAt = 0;
                for (var k = 0; k < size(images); k += 1)
                {
                    const gap = evDistance(context, { "side0" : images[k], "side1" : rebuiltEdges }).distance;
                    if (gap > edgeWorst)
                    {
                        edgeWorst = gap;
                        worstAt = reportParameters[k];
                    }
                }
                if (edgeWorst > boundaryWorst)
                {
                    boundaryWorst = edgeWorst;
                    const at = onEdge[round(worstAt * 10 - 0.5)] / millimeter;
                    boundaryNote = " (source edge " ~ roundToPrecision(evLength(context, { "entities" : sourceEdge }) / millimeter, 1) ~ " mm, worst at " ~ worstAt ~ " of it, source (" ~ roundToPrecision(at[0], 0) ~ "," ~ roundToPrecision(at[1], 0) ~ "," ~ roundToPrecision(at[2], 0) ~ "))";
                }
            }
        }
        const surfaceKind = evSurfaceDefinition(context, { "face" : face }).surfaceType;
        println("ACCURACY " ~ label ~ " " ~ toString(surfaceKind) ~ ": boundary max " ~ roundToPrecision(boundaryWorst / meter * 1e6, 1) ~ " um" ~ boundaryNote ~ "; " ~ size(points) ~ " pts, max " ~ roundToPrecision(worst / meter * 1e6, 1) ~ " um, mean "
            ~ roundToPrecision(size(points) == 0 ? 0 : sum / size(points) / meter * 1e6, 1) ~ " um, area "
            ~ roundToPrecision(evArea(context, { "entities" : face }) / (meter ^ 2) * 1e6, 1) ~ " mm2");
    }
    catch (accuracyErr)
    {
        println("ACCURACY " ~ label ~ ": could not be measured (" ~ toString(accuracyErr) ~ ")");
    }
}

annotation { "Feature Type Name" : "Deform", "Editing Logic Function" : "deformEditLogic","Feature Type Description" : "Takes a solid or surface body, from edges and to edges as input. Wraps the body from the from edges to the to edges" }
export const deform = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.deformMode is DeformMode;

        if (definition.deformMode != DeformMode.FACES)
        {
            annotation { "Name" : "Source bodies", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET), "Description" : "Bodies to deform. Each body is deformed separately and produces its own result." }
            definition.sourceBody is Query;
        }
        else
        {
            annotation { "Name" : "Source faces", "Filter" : EntityType.FACE, "Description" : "Faces to deform. Adjacent selected faces will be recombined into sheet bodies." }
            definition.sourceFaces is Query;
        }

        annotation { "Name" : "Mate connectors to deform", "Filter" : BodyType.MATE_CONNECTOR, "Description" : "Mate connectors carried through the same deform: each lands where it would end up if it were part of the source body, with its orientation rotated to match. A connector lying on a deformed source body (within 0.01 mm) is attached to that body's result; any other stays stand-alone." }
        definition.mateConnectors is Query;

        annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "From edge(s)",
                "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                "MaxNumberOfPicks" : 10,
                "Description" : "Reference edge(s) to map from (source reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
            definition.fromEdges is Query;

            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on from curve" }
            definition.fromRef is Query;
        }

        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.COMPOSITE),
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference). Accepts edges, wire bodies, or composite parts containing wire bodies." }
            definition.toEdges is Query;

            annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipTo is boolean;

            annotation { "Name" : "To reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on to curve" }
            definition.toRef is Query;

            annotation { "Name" : "Flip normal", "Default" : false, "Description" : "When true, flips the frenet frame normal vector on the to chain" }
            definition.flipToNormal is boolean;
        }

        annotation { "Group Name" : "Frame orientation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Normal mode", "Default" : FrameNormalMode.FRENET, "UIHint" : UIHint.HORIZONTAL_ENUM, "Description" : "Frenet transports one normal along the whole reference; flip-free, and works on any G1 chain. Binormal builds an in-plane normal from a supplied plane normal; requires planar, coplanar references." }
            definition.frameNormalMode is FrameNormalMode;

            if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
            {
                annotation { "Name" : "Binormal from", "Default" : BinormalSource.QUERY, "UIHint" : UIHint.HORIZONTAL_ENUM }
                definition.binormalSource is BinormalSource;

                if (definition.binormalSource == BinormalSource.QUERY)
                {
                    annotation { "Name" : "Binormal reference", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Planar face (uses its normal) or mate connector (uses its Z axis) defining the reference-path plane normal." }
                    definition.binormalQuery is Query;
                }
                else
                {
                    annotation { "Name" : "X", "Icon" : Icon.ALONG_X }
                    isReal(definition.binormalX, binormalCompBounds);

                    annotation { "Name" : "Y", "Icon" : Icon.ALONG_Y }
                    isReal(definition.binormalY, binormalCompBoundsY);

                    annotation { "Name" : "Z", "Icon" : Icon.ALONG_Z }
                    isReal(definition.binormalZ, binormalCompBounds);
                }
            }
        }

        annotation { "Group Name" : "Setup", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Source sampling mode", "Default" : SamplingMode.CP_BASED, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Specifies if source edges should be sampled based on length between sampling points, or as an integer multiple of the edge control points" }
            definition.sourceSamplingMode is SamplingMode;

            if (definition.sourceSamplingMode == SamplingMode.CP_BASED)
            {
                annotation { "Name" : "Source CP multiplier", "Description" : "Samples per source edge control point" }
                isInteger(definition.sourceCPMultiplier, cpMultiplierBounds);
            }
            else
            {
                annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
                isLength(definition.samplingDensity, samplingDensityBounds);
            }

            annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Keep source degree", "Default" : false, "Description" : "When true, uses the maximum of the source edge degree and the target degree" }
            definition.keepDegree is boolean;

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);

            annotation { "Name" : "Face accuracy", "Description" : "A long thin face whose rebuilt surface lies farther than this from the exactly mapped shape of the source face (a fill flattens such faces) is cut into shorter pieces and rebuilt. 0 turns the check off." }
            isLength(definition.faceAccuracy, FACE_ACCURACY_BOUND);

            annotation { "Name" : "Edge accuracy", "Description" : "Each wrapped edge is checked against the exact image of its source edge and refitted with more samples and control points until it follows it this closely. The maximum control points is where the fitting starts. 0 turns the check off (a single fit)." }
            isLength(definition.edgeAccuracy, EDGE_ACCURACY_BOUND);

            annotation { "Name" : "Face builder", "Default" : FaceBuilder.FILL, "Description" : "How faces are rebuilt after their outline is wrapped. Fill builds a face from its outline alone; mapping a face's surface carries the source face's own shape and tangency through the deform (every mapped point costs interpreter steps, so only long thin faces are mapped by default)." }
            definition.faceBuilder is FaceBuilder;

            annotation { "Name" : "Mapped surface accuracy", "Description" : "When a face's surface is mapped, the sampling grid is refined until the surface lies this close to the exactly mapped face between its samples (each sample costs interpreter steps; neighbouring faces' edges meet to about twice this)." }
            isLength(definition.mappedAccuracy, MAPPED_ACCURACY_BOUND);

            annotation { "Name" : "Mapped sample spacing (unused)", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isLength(definition.mappedSpacing, MAPPED_SPACING_BOUND);

            annotation { "Name" : "Use face guide points", "Default" : false, "Description" : "Sample interior points from each face and map them as guide vertices for opFillSurface. Improves shape accuracy on curved faces at the cost of extra computation." }
            definition.useFacePoints is boolean;
            
            if (definition.useFacePoints)
            {
                annotation { "Name" : "uSampling multiplier", "Description" : "Number of u iso curves = N * u control-point count", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Driving Parameter" : "useFacePoints" }
                isInteger(definition.faceUsamplingMultiplier, FaceControlMultiplierBounds);

                annotation { "Name" : "vSampling multiplier", "Description" : "Number of v iso curves = N * v control-point count", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Driving Parameter" : "useFacePoints" }
                isInteger(definition.faceVsamplingMultiplier, FaceControlMultiplierBounds);
            }

            annotation { "Name" : "Keep wires", "Default" : false, "Description" : "Retain wrapped edge bodies after reconstruction. Merged into one wire body if possible, otherwise a composite part." }
            definition.keepWires is boolean;
        }

        annotation { "Group Name" : "Naming", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Name suffix", "Default" : " (deformed)", "Description" : "Appended to each source body's name to name its deformed result. Blank leaves results unnamed." }
            definition.nameSuffix is string;

            // Filled by deformEditLogic: source body names, in selection order, joined by NAME_SEPARATOR.
            annotation { "Name" : "Source names", "UIHint" : UIHint.ALWAYS_HIDDEN }
            definition.sourceNames is string;
        }

        annotation { "Name" : "Debug", "Default" : false }
        definition.debug is boolean;
        
        if (definition.debug)
        {
            annotation { "Group Name" : "Debug", "Collapsed By Default" : false, "Driving Parameter" : "debug" }
            {
                annotation { "Name" : "Create wires", "Default" : false }
                definition.createWires is boolean;
    
                annotation { "Name" : "Create faces", "Default" : false }
                definition.createFaces is boolean;
    
                annotation { "Name" : "Create bodies", "Default" : false }
                definition.createBodies is boolean;
    
                annotation { "Name" : "Boundary surface faces (test)", "Description" : "Build four-sided faces with a boundary surface from their four wrapped sides instead of a fill (keeps the cross-section of long thin faces)", "Default" : false }
                definition.useBoundarySurface is boolean;

                annotation { "Name" : "Report face accuracy", "Description" : "Print, for every rebuilt face, how far it lies from the mapped interior points of its source face (notices console)", "Default" : false }
                definition.reportAccuracy is boolean;

                annotation { "Name" : "Show from frames", "Description" : "Draw Frenet frame axes along the from reference path", "Default" : false }
                definition.debugShowFromFrames is boolean;
    
                annotation { "Name" : "Show to frames", "Description" : "Draw Frenet frame axes along the to reference path", "Default" : false }
                definition.debugShowToFrames is boolean;
            }
        }

    }
    {
        // --- Frame orientation ---
        // BINORMAL builds a flip-free in-plane normal from a supplied plane normal (shared by
        // both paths); FRENET is the legacy curvature normal.
        var frameOpts = defaultFrameNormalOptions();
        if (definition.frameNormalMode == FrameNormalMode.BINORMAL)
        {
            var planeRef;
            if (definition.binormalSource == BinormalSource.VECTOR)
            {
                planeRef = resolveBinormalRefFromVector(definition.binormalX, definition.binormalY, definition.binormalZ);
            }
            else
            {
                planeRef = resolveBinormalRefFromQuery(context, definition.binormalQuery);
            }
            frameOpts = planeNormalOptions(planeRef);
        }

        // --- Setup (always runs) ---
        var toFrenetPath   = buildFrenetPath(context, id, expandEdgeQuery(definition.toEdges),   definition.flipTo, frameOpts);
        var fromFrenetPath = buildFrenetPath(context, id, expandEdgeQuery(definition.fromEdges), false,             frameOpts);

        var fromRefArc = projectOntoFrenetPath(fromFrenetPath, getRefPoint(context, definition.fromRef), undefined).arcLength;
        var toRefArc   = projectOntoFrenetPath(toFrenetPath,   getRefPoint(context, definition.toRef),   undefined).arcLength;

        // Bilaterally align isolated line frames so from/to xAxes match regardless
        // of which path the line lives on (e.g. flattening: line is on the to-path).
        var aligned = alignLineFramesBilateral(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, 0.001);
        fromFrenetPath = aligned.fromFrenetPath;
        toFrenetPath   = aligned.toFrenetPath;

        if (definition.debugShowFromFrames)
            debugDrawFrames(context, fromFrenetPath, 10);
        if (definition.debugShowToFrames)
            debugDrawFrames(context, toFrenetPath, 10);

        var isFacesMode = definition.deformMode == DeformMode.FACES;

        if ((isFacesMode ? isQueryEmpty(context, definition.sourceFaces) : isQueryEmpty(context, definition.sourceBody))
            && isQueryEmpty(context, definition.mateConnectors))
        {
            throw regenError(isFacesMode ? "Select faces or mate connectors to deform." : "Select bodies or mate connectors to deform.",
                [isFacesMode ? "sourceFaces" : "sourceBody", "mateConnectors"]);
        }

        // One deform per source body (FACES mode is a single unit). Each unit works under its
        // own id so the ops, qCreatedBy lookups and the union in step 3 never mix bodies.
        var unitBodies = [];
        if (isFacesMode)
        {
            if (!isQueryEmpty(context, definition.sourceFaces))
                unitBodies = [undefined];
        }
        else
        {
            unitBodies = evaluateQuery(context, definition.sourceBody);
        }

        // toString(source body) -> query for its deformed result body; lets mate connectors
        // find the body they now belong to.
        var unitResults = {};

        for (var unitIdx = 0; unitIdx < size(unitBodies); unitIdx += 1)
        {
            var unitBody = unitBodies[unitIdx];
            var uid      = id + ("unit" ~ unitIdx);
            // The body whose faces are rebuilt: the source itself, or on the second attempt a copy in which the
            // faces that failed to fill are cut into pieces. The faces that failed are listed in failedFaces.
            var geomBody    = unitBody;
            var joinId      = uid;       // ids of the final join (step 3); differs from uid when work ops had to run in between
            var failedFaces = [];
            var poorFaces   = [];        // thin faces whose fill is too far from the mapped shape (rebuilt in pieces)
            var measuredFaces = 0;
            var mappedFaces   = 0;
            var mappedPointCount = 0;
            // Steps, not points, are the limit (a feature gets about 20-30k mapped points in all, edges and the rest included): a unit with more faces gets a smaller share.
            const pointBudget = clamp(MAPPED_POINT_BUDGET * 40 / max(1, size(evaluateQuery(context, qOwnedByBody(unitBody, EntityType.FACE)))), 4000, MAPPED_POINT_BUDGET);
            const workId    = id + ("work" ~ unitIdx);
            const workId2   = id + ("work" ~ unitIdx ~ "b");
            var unionFailed  = false;    // the final union of this attempt was refused
            var refinedUsed  = false;    // this attempt's pieces include grid cuts (a plain recut is the fallback)
            var firstCutFaces = [];      // the faces the first attempt cut, for the plain recut

            for (var attempt = 0; attempt < 3; attempt += 1)
            {
                failedFaces = [];
                poorFaces   = [];
                measuredFaces = 0;
                mappedFaces   = 0;
                mappedPointCount = 0;
                unionFailed = false;
                if (attempt >= 1)
                    uid = id + ("unit" ~ unitIdx ~ "retry" ~ (attempt > 1 ? toString(attempt) : ""));
                joinId = uid;


                // Pre-transform all body vertices in one batch so every edge uses the same
                // canonical mapped position for each shared vertex.  This eliminates the
                // floating-point gap that arises when two adjacent edges sample the same
                // source vertex independently and get slightly different float values.
                var allVerts = evaluateQuery(context, isFacesMode
                    ? qAdjacent(definition.sourceFaces, AdjacencyType.VERTEX, EntityType.VERTEX)
                    : qOwnedByBody(geomBody, EntityType.VERTEX));
                var vertSrcPositions = mapArray(allVerts, function(v) {
                    return evVertexPoint(context, { "vertex": v });
                });
                var vertMappedPositions = mapWorldPoints(context, fromFrenetPath, toFrenetPath,
                    fromRefArc, toRefArc, definition.flipToNormal, vertSrcPositions);
                vertMappedPositions = mergeCloseVertices(vertMappedPositions);
                var vertexMap = {};
                for (var vIdx = 0; vIdx < size(allVerts); vIdx += 1)
                {
                    vertexMap[toString(allVerts[vIdx])] = vertMappedPositions[vIdx];
                }

                var settings = {
                    "fromRefArc"             : fromRefArc,
                    "toRefArc"               : toRefArc,
                    "flipToNormal"           : definition.flipToNormal,
                    "samplingMode"           : definition.sourceSamplingMode,
                    "samplingDensity"        : definition.samplingDensity,
                    "sourceCPMultiplier"     : definition.sourceCPMultiplier,
                    "keepDegree"             : definition.keepDegree,
                    "approximationDegree"    : definition.approximationDegree,
                    "approximationMaxCPs"    : definition.approximationMaxCPs,
                    "approximationTolerance" : definition.approximationTolerance,
                    "edgeAccuracy"           : definition.edgeAccuracy,
                    "vertexMergeTolerance"   : VERTEX_MERGE_TOLERANCE,
                    "vertexMap"              : vertexMap
                };

                // Accumulators declared before conditional blocks so all steps share scope
                var edgeMapping = [];
                var fillBodies = {};         // toString(source face) -> the sheet that was built for it

                // Cumulative debug conditions: each step implies all prior steps ran
                var runWires  = !definition.debug || definition.createWires  || definition.createFaces || definition.createBodies;
                var runFaces  = !definition.debug || definition.createFaces  || definition.createBodies;
                var runBodies = !definition.debug || definition.createBodies;

                // --- Step 1: Transform edges ---
                if (runWires)
                {
                    var allEdges = evaluateQuery(context, isFacesMode
                        ? qAdjacent(definition.sourceFaces, AdjacencyType.EDGE, EntityType.EDGE)
                        : qOwnedByBody(geomBody, EntityType.EDGE));
                    edgeMapping  = transformEdges(context, uid + "wires", allEdges, fromFrenetPath, toFrenetPath, settings);
                if (definition.reportAccuracy)
                    reportEdgeFit(edgeMapping);
                }

                // --- Step 2: Reconstruct faces ---
                if (runFaces)
                {
                    var allFaces = evaluateQuery(context, isFacesMode
                        ? definition.sourceFaces
                        : qOwnedByBody(geomBody, EntityType.FACE));

                    // Build O(1) lookup: toString(sourceEdge) → wrappedEdge.
                    // toString on a transient query gives a stable unique string per entity,
                    // which is more reliable than == comparison across different evaluateQuery call sites.
                    var edgeLookupMap = {};
                    for (var m in edgeMapping)
                    {
                        edgeLookupMap[toString(m.sourceEdge)] = m.wrappedEdge;
                    }

                    // Pre-evaluate boundary edges for every face once — avoids repeated qAdjacent
                    // kernel calls inside the loop.
                    var faceBoundaryEdgeSets = [];
                    for (var fIdx = 0; fIdx < size(allFaces); fIdx += 1)
                    {
                        faceBoundaryEdgeSets = append(faceBoundaryEdgeSets,
                            evaluateQuery(context, qAdjacent(allFaces[fIdx], AdjacencyType.EDGE, EntityType.EDGE)));
                    }

                    for (var fIdx = 0; fIdx < size(allFaces); fIdx += 1)
                    {
                        var face      = allFaces[fIdx];
                        var faceEdges = faceBoundaryEdgeSets[fIdx];

                        // Gather wrapped counterparts via O(1) map lookup.
                        // Deduplicate by toString(wrappedEdge) to guard against opExtractWires
                        // OVERLAPPING_EDGES when the same wrapped edge resolves for two source keys.
                        var wrappedBoundaryEdgeQueries = [];
                        var seenWrappedKeys = {};
                        for (var eIdx = 0; eIdx < size(faceEdges); eIdx += 1)
                        {
                            var wrappedEdge = edgeLookupMap[toString(faceEdges[eIdx])];
                            if (wrappedEdge != undefined)
                            {
                                var wk = toString(wrappedEdge);
                                if (seenWrappedKeys[wk] == undefined)
                                {
                                    seenWrappedKeys[wk] = true;
                                    wrappedBoundaryEdgeQueries = append(wrappedBoundaryEdgeQueries, wrappedEdge);
                                }
                                // else: duplicate wrapped edge — skip to avoid OVERLAPPING_EDGES
                            }
                            else
                            {
                                println("WARNING: face " ~ fIdx ~ " boundary edge " ~ eIdx ~
                                        " has no wrapped counterpart — it likely failed in transformEdges");
                            }
                        }

                        // Each wrapped edge lives in its own wire body — topologically disconnected
                        // from its neighbours even when endpoints are geometrically coincident.
                        // opFillSurface needs a closed, topologically-connected boundary, so we
                        // stitch the edges into a proper wire first with opExtractWires.
                        if (size(wrappedBoundaryEdgeQueries) == 0)
                        {
                            println("WARNING: face " ~ fIdx ~ " has no wrapped boundary edges — skipping fill");
                            continue;
                        }

                        var extractId = uid + ("bdry" ~ fIdx);
                        try
                        {
                            opExtractWires(context, extractId, { "edges": qUnion(wrappedBoundaryEdgeQueries) });
                        }
                        catch (extractErr)
                        {
                            println("ERROR stitching boundary for face " ~ fIdx ~ ": " ~ toString(extractErr));
                            failedFaces = append(failedFaces, face);

                            // Cyan = undeformed source boundary edges
                            for (var seq in faceEdges)
                                addDebugEntities(context, seq, DebugColor.CYAN);
                            // Red = wrapped edges that failed to stitch
                            for (var beq in wrappedBoundaryEdgeQueries)
                                addDebugEntities(context, beq, DebugColor.RED);

                            // ── Source edges with expected mapped positions ──────────────────────
                            println("  face " ~ fIdx ~ ": " ~ size(faceEdges) ~ " src edges / " ~
                                    size(wrappedBoundaryEdgeQueries) ~ " wrapped:");
                            for (var dIdx = 0; dIdx < size(faceEdges); dIdx += 1)
                            {
                                var srcPts = evEdgeTangentLines(context, {
                                    "edge": faceEdges[dIdx], "parameters": [0, 1]
                                });
                                var expPts = mapWorldPoints(context, fromFrenetPath, toFrenetPath,
                                    settings.fromRefArc, settings.toRefArc, settings.flipToNormal,
                                    [srcPts[0].origin, srcPts[1].origin]);
                                println("  src " ~ dIdx ~ ": " ~ toString(srcPts[0].origin) ~
                                        " → " ~ toString(srcPts[1].origin));
                                println("  exp " ~ dIdx ~ ": " ~ toString(expPts[0]) ~
                                        " → " ~ toString(expPts[1]));
                            }

                            // ── Actual wrapped edge endpoints ────────────────────────────────────
                            var wrappedPtPairs = [];
                            for (var wIdx = 0; wIdx < size(wrappedBoundaryEdgeQueries); wIdx += 1)
                            {
                                var wPts = evEdgeTangentLines(context, {
                                    "edge": wrappedBoundaryEdgeQueries[wIdx], "parameters": [0, 1]
                                });
                                wrappedPtPairs = append(wrappedPtPairs, wPts);
                                // If counts match, also print the lookup error vs expected
                                if (size(faceEdges) == size(wrappedBoundaryEdgeQueries))
                                {
                                    var srcP = evEdgeTangentLines(context, {
                                        "edge": faceEdges[wIdx], "parameters": [0, 1]
                                    });
                                    var expP = mapWorldPoints(context, fromFrenetPath, toFrenetPath,
                                        settings.fromRefArc, settings.toRefArc, settings.flipToNormal,
                                        [srcP[0].origin, srcP[1].origin]);
                                    var errS = norm(expP[0] - wPts[0].origin);
                                    var errE = norm(expP[1] - wPts[1].origin);
                                    println("  wrapped " ~ wIdx ~ ": " ~ toString(wPts[0].origin) ~
                                            " → " ~ toString(wPts[1].origin) ~
                                            "  lookupErr start=" ~ toString(errS) ~
                                            " end=" ~ toString(errE));
                                }
                                else
                                {
                                    println("  wrapped " ~ wIdx ~ ": " ~ toString(wPts[0].origin) ~
                                            " → " ~ toString(wPts[1].origin));
                                }
                            }

                            // ── Open endpoint analysis ───────────────────────────────────────────
                            // Report any wrapped endpoint that has no matching endpoint within 1e-5 m.
                            var ETOL = 1e-5 * meter;
                            for (var wIdx = 0; wIdx < size(wrappedPtPairs); wIdx += 1)
                            {
                                for (var ep = 0; ep < 2; ep += 1)
                                {
                                    var pos = wrappedPtPairs[wIdx][ep].origin;
                                    var bestGap = 1e10 * meter;
                                    var bestJ   = -1;
                                    for (var jIdx = 0; jIdx < size(wrappedPtPairs); jIdx += 1)
                                    {
                                        if (jIdx == wIdx) { continue; }
                                        for (var jp = 0; jp < 2; jp += 1)
                                        {
                                            var d = norm(pos - wrappedPtPairs[jIdx][jp].origin);
                                            if (d < bestGap) { bestGap = d; bestJ = jIdx; }
                                        }
                                    }
                                    if (bestGap > ETOL)
                                    {
                                        println("  OPEN END: wrapped[" ~ wIdx ~ "]" ~
                                                (ep == 0 ? ".start" : ".end") ~
                                                " nearest=wrapped[" ~ bestJ ~ "] gap=" ~ toString(bestGap));
                                    }
                                }
                            }

                            continue;
                        }

                        var bdryEdges = qCreatedBy(extractId, EntityType.EDGE);
                        var bdryBody  = qCreatedBy(extractId, EntityType.BODY);

                        // Generate mapped interior guide points for this face.
                        // Wrapped boundary edges only constrain the perimeter; guide vertices
                        // pull the fill surface interior toward the correct deformed shape.
                        // Gated by definition.useFacePoints — skipped by default because
                        // opCreateCurvesOnFace adds significant computation per face.
                        var guideVerticesQuery = qNothing();
                        var guideVtxDeleteId   = undefined;
                        // Declared outside the try so a failure part-way through the loop can remove the
                        // points already made (they used to be left behind as stray point bodies).
                        var faceGuideVtxQueries = [];
                        // Guide points for the user's option, or for the long thin pieces of a cut face (when the face accuracy is on).
                        const guided = definition.useFacePoints ||
                            (attempt >= 1 && definition.faceAccuracy > 0 * meter && !isFacesMode && evArea(context, { "entities" : face }) > GUIDED_FACE_MIN_AREA &&
                             isAspectAbove(context, face, GUIDED_FACE_ASPECT));
                        if (guided) try
                        {
                            var guidePoints = transformFacepoints(
                                context, uid + ("faceGuides" ~ fIdx), face,
                                definition.faceUsamplingMultiplier, definition.faceVsamplingMultiplier,
                                fromFrenetPath, toFrenetPath, settings);

                            for (var gIdx = 0; gIdx < size(guidePoints); gIdx += 1)
                            {
                                var vtxId = uid + ("guideVtx" ~ fIdx ~ "_" ~ gIdx);
                                opPoint(context, vtxId, { "point": guidePoints[gIdx] });
                                faceGuideVtxQueries = append(faceGuideVtxQueries,
                                    qCreatedBy(vtxId, EntityType.BODY));
                            }
                            if (size(faceGuideVtxQueries) > 0)
                            {
                                guideVerticesQuery = qUnion(faceGuideVtxQueries);
                                guideVtxDeleteId   = uid + ("deleteGuideVtx" ~ fIdx);
                            }
                        }
                        catch (guideErr)
                        {
                            println("WARNING: guide point generation failed for face " ~ fIdx ~
                                    ": " ~ toString(guideErr));
                            if (size(faceGuideVtxQueries) > 0)
                            {
                                opDeleteBodies(context, uid + ("discardGuideVtx" ~ fIdx), { "entities": qUnion(faceGuideVtxQueries) });
                            }
                        }

                        var fillId = uid + ("fill" ~ fIdx);
                        var builder = "fill";
                        try
                        {
                            const mapThis = !isFacesMode && (definition.faceBuilder == FaceBuilder.MAP_ALL ||
                                (definition.faceBuilder == FaceBuilder.MAP_THIN && mappedFaces < MAX_MAPPED_FACES && isThinAndLong(context, face)));
                            var pointsMapped = 0;
                            if (mapThis)
                                pointsMapped = mappedSurfaceFace(context, fillId, face, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, definition.flipToNormal,
                                    definition.mappedAccuracy, pointBudget - mappedPointCount);
                            if (pointsMapped > 0)
                            {
                                builder = "mapped";
                                mappedFaces += 1;
                                mappedPointCount += pointsMapped;
                            }
                            else if (definition.useBoundarySurface && boundarySurfaceFace(context, fillId, bdryEdges))
                            {
                                builder = "bsurf";
                            }
                            else
                            {
                                opFillSurface(context, fillId, {
                                    "edgesG0"      : bdryEdges,
                                    "edgesG1"      : qNothing(),
                                    "edgesG2"      : qNothing(),
                                    "guideVertices": guideVerticesQuery
                                });
                            }
                            fillBodies[toString(face)] = qBodyType(qCreatedBy(fillId, EntityType.BODY), BodyType.SHEET);
                            if (definition.reportAccuracy)
                            {
                                reportFaceAccuracy(context, uid + ("accuracy" ~ fIdx), face, qOwnedByBody(qBodyType(qCreatedBy(fillId, EntityType.BODY), BodyType.SHEET), EntityType.FACE),
                                    "face " ~ fIdx ~ (attempt == 1 ? " (piece)" : "") ~ " [" ~ builder ~ "]", fromFrenetPath, toFrenetPath, settings);
                            }
                            if (attempt == 0 && !isFacesMode && definition.faceAccuracy > 0 * meter && measuredFaces < MAX_MEASURED_FACES && isThinAndLong(context, face))
                            {
                                measuredFaces += 1;
                                const deviation = faceMaxDeviation(context, uid + ("poor" ~ fIdx), face,
                                    qOwnedByBody(qBodyType(qCreatedBy(fillId, EntityType.BODY), BodyType.SHEET), EntityType.FACE), fromFrenetPath, toFrenetPath, settings);
                                if (deviation > definition.faceAccuracy)
                                {
                                    poorFaces = append(poorFaces, face);
                                    println("Deform: face " ~ fIdx ~ " is " ~ roundToPrecision(deviation / meter * 1e6, 0) ~ " um from the mapped shape; it will be rebuilt in shorter pieces");
                                }
                            }
                            // Fill succeeded — cleanup intermediates
                            opDeleteBodies(context, uid + ("deleteBdry" ~ fIdx), { "entities": bdryBody });
                            if (guideVtxDeleteId != undefined)
                                opDeleteBodies(context, guideVtxDeleteId, { "entities": guideVerticesQuery });
                        }
                        catch (fillErr)
                        {
                            println("ERROR fill face " ~ fIdx ~ ": " ~ toString(fillErr));
                            failedFaces = append(failedFaces, face);
                            // Cyan = undeformed source boundary, Red = stitched wrapped boundary
                            for (var seq in faceEdges)
                                addDebugEntities(context, seq, DebugColor.CYAN);
                            addDebugEntities(context, bdryEdges, DebugColor.RED);

                            // ── Stitched edge endpoints + open endpoint analysis ─────────────────
                            var stitchedList = evaluateQuery(context, bdryEdges);
                            println("  stitched boundary: " ~ size(stitchedList) ~ " edges");
                            var stPtPairs = [];
                            for (var sIdx = 0; sIdx < size(stitchedList); sIdx += 1)
                            {
                                var sPts = evEdgeTangentLines(context, {
                                    "edge": stitchedList[sIdx], "parameters": [0, 1]
                                });
                                stPtPairs = append(stPtPairs, sPts);
                                println("  stitched[" ~ sIdx ~ "]: " ~ toString(sPts[0].origin) ~
                                        " → " ~ toString(sPts[1].origin));
                            }
                            var ETOL = 1e-5 * meter;
                            for (var sIdx = 0; sIdx < size(stPtPairs); sIdx += 1)
                            {
                                for (var ep = 0; ep < 2; ep += 1)
                                {
                                    var pos = stPtPairs[sIdx][ep].origin;
                                    var bestGap = 1e10 * meter;
                                    var bestJ   = -1;
                                    for (var jIdx = 0; jIdx < size(stPtPairs); jIdx += 1)
                                    {
                                        if (jIdx == sIdx) { continue; }
                                        for (var jp = 0; jp < 2; jp += 1)
                                        {
                                            var d = norm(pos - stPtPairs[jIdx][jp].origin);
                                            if (d < bestGap) { bestGap = d; bestJ = jIdx; }
                                        }
                                    }
                                    if (bestGap > ETOL)
                                    {
                                        println("  OPEN END: stitched[" ~ sIdx ~ "]" ~
                                                (ep == 0 ? ".start" : ".end") ~
                                                " nearest=stitched[" ~ bestJ ~ "] gap=" ~ toString(bestGap));
                                    }
                                }
                            }

                            opDeleteBodies(context, uid + ("deleteBdry" ~ fIdx), { "entities": bdryBody });
                            if (guideVtxDeleteId != undefined)
                                opDeleteBodies(context, guideVtxDeleteId, { "entities": guideVerticesQuery });
                        }
                    }
                }

                // Faces that would not fill: rebuild them from pieces, but only when there is something to cut
                // (otherwise keep what this attempt built).
                if (attempt == 0 && (size(failedFaces) > 0 || size(poorFaces) > 0) && !isFacesMode)
                {
                    var refine = [];
                    // A failed face that is long and thin is cut across on the grid as well: its fill would be flat across the width.
                    for (var failed in failedFaces)
                        refine = append(refine, definition.faceAccuracy > 0 * meter && isThinAndLong(context, failed));
                    for (var poor in poorFaces)
                        refine = append(refine, true);
                    const cut = splitFacesBetweenVertices(context, workId, fromFrenetPath, unitBody, concatenateArrays([failedFaces, poorFaces]), refine);
                    const cutBody = cut.body;
                    if (cutBody == undefined && cut.ranOps)
                    {
                        // The failed cut left operations of its own in the history: the ids of this attempt cannot be
                        // used again (an id prefix must stay contiguous), so the join takes a prefix of its own.
                        joinId = id + ("join" ~ unitIdx);
                    }
                    if (cutBody != undefined)
                    {
                        firstCutFaces = concatenateArrays([failedFaces, poorFaces]);
                        for (var flag in refine)
                            if (flag)
                                refinedUsed = true;
                        try
                        {
                            opDeleteBodies(context, id + ("discard" ~ unitIdx), { "entities" : qCreatedBy(uid, EntityType.BODY) });
                        }
                        catch (discardErr)
                        {
                            println("WARNING: could not discard the first attempt: " ~ toString(discardErr));
                        }
                        geomBody = cutBody;
                        continue;
                    }
                }

                // --- Step 3: Combine into one body, clean up intermediates ---
                if (runBodies)
                {
                    // The edges that must weld, found while the wrapped edges still exist.
                    var matches = [];
                    var links = [];
                    try
                    {
                        matches = weldMatches(context, edgeMapping, fillBodies);
                        links = joinLinks(context, edgeMapping, fillBodies);
                    }
                    catch (matchesErr)
                    {
                        println("WARNING: the edge matches for the union could not be found: " ~ toString(matchesErr));
                    }
                    // Wire cleanup first — so qCreatedBy(uid) only sees face surface bodies
                    // by the time we boolean.
                    if (size(edgeMapping) > 0)
                    {
                        var allWrappedEdgeQueries = mapArray(edgeMapping, function(m) { return m.wrappedEdge; });
                        var allWrappedBodyQueries = mapArray(edgeMapping, function(m) { return m.wrappedBody; });

                        if (definition.keepWires)
                        {
                            // Try to stitch all wrapped edges into clean wire bodies.
                            // This will fail with NON_MANIFOLD whenever 3+ faces share a vertex
                            // (which is normal for any solid) — fall back to a composite part
                            // of the raw per-edge wire bodies in that case.
                            try
                            {
                                opExtractWires(context, joinId + "keepWires", { "edges": qUnion(allWrappedEdgeQueries) });
                                opDeleteBodies(context, joinId + "deleteWireIntermediates", { "entities": qUnion(allWrappedBodyQueries) });
                                opCreateCompositePart(context, joinId + "compositePart", {
                                    "bodies" : qCreatedBy(joinId + "keepWires", EntityType.BODY),
                                    "closed" : true
                                });
                            }
                            catch
                            {
                                opCreateCompositePart(context, joinId + "compositePart", {
                                    "bodies" : qUnion(allWrappedBodyQueries),
                                    "closed" : true
                                });
                            }
                        }
                        else
                        {
                            opDeleteBodies(context, joinId + "deleteWires", { "entities": qUnion(allWrappedBodyQueries) });
                        }
                    }

                    // Boolean all reconstructed face surface bodies.
                    // Use qCreatedBy(uid) filtered to SHEET — this is equivalent to what
                    // the user confirmed works in manual Onshape testing.  Wire bodies
                    // have already been deleted above, so only fill surfaces remain.
                    // In FACES mode the result is always one or more sheet bodies — no makeSolid.
                    var inputIsSolid = !isFacesMode &&
                        !isQueryEmpty(context, qBodyType(unitBody, BodyType.SOLID));
                    var surfaceBodies     = qBodyType(qCreatedBy(uid, EntityType.BODY), BodyType.SHEET);
                    var surfaceBodiesList = evaluateQuery(context, surfaceBodies);
                    if (size(surfaceBodiesList) > 1)
                    {
                        var joined = false;
                        if (size(matches) > 0)
                        {
                            try
                            {
                                opBoolean(context, joinId + "unionMatched", {
                                    "tools"               : surfaceBodies,
                                    "operationType"       : BooleanOperationType.UNION,
                                    "makeSolid"           : inputIsSolid,
                                    "allowSheets"         : true,
                                    "eraseImprintedEdges" : true,
                                    "matches"             : matches,
                                    "recomputeMatches"    : true
                                });
                                joined = true;
                            }
                            catch (matchedErr)
                            {
                                println("WARNING: the union with edge matches was refused (" ~ toString(matchedErr) ~ "); trying a plain union");
                            }
                        }
                        try
                        {
                            if (!joined)
                            {
                                opBoolean(context, joinId + "unionFaces", {
                                    "tools"        : surfaceBodies,
                                    "operationType": BooleanOperationType.UNION,
                                    "makeSolid"    : inputIsSolid,
                                });
                            }
                        }
                        catch (unionErr)
                        {
                            // Every face was rebuilt; the kernel would not join them (the edges of neighbouring faces
                            // do not agree closely enough, which a higher control-point limit usually cures).
                            // Keep the faces as separate sheets rather than losing the whole result.
                            println("WARNING: the " ~ size(surfaceBodiesList) ~ " rebuilt faces could not be joined at once: " ~ toString(unionErr) ~ "; joining them one at a time");
                            unionFailed = true;
                            if (size(links) > 0)
                            {
                                try
                                {
                                    const stepwise = incrementalJoin(context, joinId + "one", links, fillBodies, inputIsSolid);
                                    println("Deform: " ~ stepwise.joined ~ " faces joined into one body, " ~ stepwise.left ~ " left as separate sheets");
                                    unionFailed = stepwise.left > 0;
                                }
                                catch (stepwiseErr)
                                {
                                    println("WARNING: joining one at a time failed: " ~ toString(stepwiseErr));
                                }
                            }
                        }
                    }
                    else if (size(surfaceBodiesList) == 1 && inputIsSolid)
                    {
                        // Single surface body (all fills already merged) — attempt to close into solid.
                        try
                        {
                            opBoolean(context, joinId + "closeSolid", {
                                "tools"        : surfaceBodies,
                                "operationType": BooleanOperationType.UNION,
                                "makeSolid"    : true,
                            });
                        }
                        catch (solidErr)
                        {
                            println("WARNING: could not close surface into solid: " ~ toString(solidErr));
                        }
                    }
                }
                // The pieces with grid cuts would not join: redo the cuts without the grid (the variant that joined), keeping
                // these pieces until the new copy exists.
                if (attempt == 1 && unionFailed && refinedUsed)
                {
                    var plain = [];
                    for (var cutFace in firstCutFaces)
                        plain = append(plain, false);
                    const second = splitFacesBetweenVertices(context, workId2, fromFrenetPath, unitBody, firstCutFaces, plain);
                    if (second.body != undefined)
                    {
                        println("Deform: the refined pieces could not be joined; recutting without the grid");
                        try silent
                        {
                            opDeleteBodies(context, id + ("dropRefined" ~ unitIdx), { "entities" : qCreatedBy(uid, EntityType.BODY) });
                        }
                        try silent
                        {
                            opDeleteBodies(context, id + ("dropFirstWork" ~ unitIdx), { "entities" : geomBody });
                        }
                        geomBody = second.body;
                        refinedUsed = false;
                        continue;
                    }
                }
                break;
            }
            if (unionFailed)
            {
                reportFeatureWarning(context, id, "The rebuilt faces of a body could not be joined into one body and are left as separate sheets. Raising the maximum control points usually fixes this.");
            }
            if (geomBody != unitBody)
            {
                try silent
                {
                    opDeleteBodies(context, id + ("dropWork" ~ unitIdx), { "entities" : geomBody });
                }
            }
            if (size(failedFaces) > 0)
            {
                reportFeatureWarning(context, id, size(failedFaces) ~ " face(s) of a source body could not be rebuilt and are missing from the result (see the notices).");
            }

            // Record this unit's deformed body and name it. Source names cannot be read here
            // (getProperty is unavailable on the current context), so the name comes from the dialog.
            if (!isFacesMode)
            {
                var unitResult = qUnion([qBodyType(qCreatedBy(uid, EntityType.BODY), BodyType.SOLID),
                                         qBodyType(qCreatedBy(uid, EntityType.BODY), BodyType.SHEET),
                                         qBodyType(qCreatedBy(joinId, EntityType.BODY), BodyType.SOLID),
                                         qBodyType(qCreatedBy(joinId, EntityType.BODY), BodyType.SHEET)]);
                unitResults[toString(unitBody)] = unitResult;
                if (definition.reportAccuracy && !isQueryEmpty(context, unitResult))
                    reportContinuity(context, unitBody, unitResult, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, definition.flipToNormal);

                // Names come from deformEditLogic (getProperty is not allowed in a feature body);
                // only trust them when the count still matches the selection.
                var sourceNameList = splitNames(definition.sourceNames);
                if (definition.nameSuffix != "" && size(sourceNameList) == size(unitBodies) && !isQueryEmpty(context, unitResult))
                {
                    try silent
                    {
                        setProperty(context, {
                            "entities"     : unitResult,
                            "propertyType" : PropertyType.NAME,
                            "value"        : sourceNameList[unitIdx] ~ definition.nameSuffix
                        });
                    }
                }
            }
        }

        // --- Mate connectors: where do they end up under the same deform? ---
        if (!isQueryEmpty(context, definition.mateConnectors))
        {
            var mcQueries = evaluateQuery(context, definition.mateConnectors);
            var mcSystems = mapArray(mcQueries, function(mc) {
                return evMateConnector(context, { "mateConnector" : mc });
            });
            var mcMapped = mapCoordSystems(context, fromFrenetPath, toFrenetPath,
                fromRefArc, toRefArc, definition.flipToNormal, mcSystems);

            for (var mcIdx = 0; mcIdx < size(mcQueries); mcIdx += 1)
            {
                var mcId  = id + ("mateConnector" ~ mcIdx);
                var mcDef = { "coordSystem" : mcMapped[mcIdx] };

                // A connector on a deformed body goes onto that body's result; anything else stays stand-alone.
                // FeatureScript cannot read which body owns a mate connector (qOwnerBody returns the
                // connector itself), so a connector counts as belonging to the source body it lies on.
                for (var srcBody in unitBodies)
                {
                    var ownerResult = unitResults[toString(srcBody)];
                    if (ownerResult != undefined && size(evaluateQuery(context, ownerResult)) == 1 &&
                        evDistance(context, { "side0" : mcSystems[mcIdx].origin, "side1" : srcBody }).distance < MC_ON_BODY_TOLERANCE)
                    {
                        mcDef.owner = ownerResult;
                        break;
                    }
                }

                opMateConnector(context, mcId, mcDef);
            }
        }

    }, {
        // Saved features predate these parameters: keep their behaviour (no connectors, no naming).
        "mateConnectors" : qNothing(),
        "reportAccuracy" : false,
        "faceBuilder" : FaceBuilder.FILL,
        "mappedSpacing" : 6e-3 * meter,
        "mappedAccuracy" : 2e-6 * meter,
        "edgeAccuracy" : 0 * meter,
        "faceAccuracy" : 0 * meter,
        "useBoundarySurface" : false,
        "nameSuffix"     : "",
        "sourceNames"    : ""
    });
// regen marker: 1
