FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

/**
 * Offset+: offset surfaces or curves toward (or away from) a reference, instead of along
 * whichever way a normal or a curve direction happens to point.
 *
 * SURFACE. The built-in offset surface (opExtractSurface with an offset), once per selected
 * body: each body is offset toward the side of it the reference is on -- read as the
 * reference's signed distance to that body -- so bodies of opposite orientation still all
 * go the same way in the model.
 *
 * CURVE. Each chain of the selected curves is sampled, given an offset direction D(s)
 * perpendicular to its tangent by the chosen FRAME, offset by distance * D, fitted per
 * source edge with both end tangents pinned to the true offset tangent, closed at corners,
 * and stitched into one wire:
 *   Transport frame  D is carried along the chain without twist (rotation-minimizing,
 *                    double reflection; exact minimal rotation across a G0 corner). With a
 *                    reference, D starts as the direction from the chain's nearest point
 *                    to the reference, so a 3D chain offsets "toward that" all the way
 *                    round. Planar chains with an in-plane start stay in plane.
 *   Surface normal   D from a surface at each point: along its normal (lift off it) or
 *                    along it (in its tangent plane, perpendicular to the curve). Normals
 *                    are kept continuous along the chain, so the surface's orientation
 *                    does not matter.
 *   Plane            D in the plane perpendicular to a direction (default world Z):
 *                    a plan-view offset of a 3D curve.
 * The reference picks the side (toward it, or away with the box off); without one the box
 * is a plain flip.
 *
 * Corners (G0 junctions): where the two offsets separate the gap is closed by an arc about
 * the corner point with radius = distance (the std round gap fill); where they overlap both
 * are trimmed back to their crossing. Junctions within G1_JUNCTION_ANGLE are smooth: both
 * sides share one averaged end point and tangent, bit for bit, so the wire stitches.
 *
 * Publishes (Extract variables): output (the offset surfaces / wires), and for curves
 * roundedCorners, trimmedCorners, openCorners.
 */

/** What Offset+ offsets. */
export enum OffsetPlusType
{
    annotation { "Name" : "Surface" }
    SURFACE,
    annotation { "Name" : "Curve" }
    CURVE
}

/** How a curve's offset direction is defined along it. */
export enum OffsetFrameMode
{
    annotation { "Name" : "Transport frame" }
    TRANSPORT,
    annotation { "Name" : "Surface normal" }
    SURFACE,
    annotation { "Name" : "Plane" }
    PLANE
}

/** Surface-normal frame: off the surface, or along it. */
export enum SurfaceOffsetDirection
{
    annotation { "Name" : "Along surface normal" }
    NORMAL,
    annotation { "Name" : "Along surface" }
    TANGENT
}

/** Junctions with a tangent break below this are smooth, not corners. */
export const G1_JUNCTION_ANGLE = 0.5 * degree;

/** Normalized parameter step of the helper stations used for end tangents. */
const TANGENT_STEP = 1e-4;

/** Stations closer than this are one point: the step between them is a pure rotation. */
const SAME_POINT = 1e-9;

annotation { "Feature Type Name" : "Offset+",
        "Feature Type Description" : "Offset surfaces, or curves by a transport frame, a surface's normal or a plane, toward the side a reference is on.",
        "Filter Selector" : "allparts" }
export const offsetPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Offset type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : OffsetPlusType.SURFACE }
        definition.offsetType is OffsetPlusType;

        if (definition.offsetType == OffsetPlusType.SURFACE)
        {
            annotation { "Name" : "Faces and surfaces to offset",
                        "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO && SketchObject.NO }
            definition.surfaces is Query;
        }
        else
        {
            annotation { "Name" : "Curves and edges to offset",
                        "Filter" : (EntityType.EDGE && ConstructionObject.NO) || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "Connected edges form one chain; each chain becomes one wire." }
            definition.curves is Query;

            annotation { "Name" : "Frame", "Default" : OffsetFrameMode.TRANSPORT,
                        "Description" : "Transport frame: carried along the chain without twist. Surface normal: from a surface at each point. Plane: perpendicular to a direction (a plan-view offset)." }
            definition.frameMode is OffsetFrameMode;

            if (definition.frameMode == OffsetFrameMode.SURFACE)
            {
                annotation { "Name" : "Surface",
                            "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO }
                definition.normalSurface is Query;

                annotation { "Name" : "Direction", "Default" : SurfaceOffsetDirection.NORMAL, "UIHint" : UIHint.SHOW_LABEL }
                definition.surfaceDirection is SurfaceOffsetDirection;
            }

            if (definition.frameMode == OffsetFrameMode.PLANE)
            {
                annotation { "Name" : "Plane normal", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1,
                            "Description" : "The offset lies in planes perpendicular to this. Empty = world Z (a Top-plane offset)." }
                definition.planeNormal is Query;
            }
        }

        annotation { "Name" : "Distance", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        isLength(definition.distance, NONNEGATIVE_LENGTH_BOUNDS);

        annotation { "Name" : "Side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Geometry on the side to offset toward. Leave empty to use the flip alone." }
        definition.sideReference is Query;

        annotation { "Name" : "Offset toward reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                    "Description" : "On: toward the reference. Off: away from it. Without a reference: a plain flip." }
        definition.towardReference is boolean;

        if (definition.offsetType == OffsetPlusType.CURVE)
        {
            annotation { "Group Name" : "Fit", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Samples per edge", "Description" : "At least this many offset stations per source edge; the fit passes within tolerance of every one." }
                isInteger(definition.samplesPerEdge, { (unitless) : [4, 24, 500] } as IntegerBoundSpec);

                annotation { "Name" : "Max station spacing", "Description" : "Long edges get more stations, at most this far apart. Between stations the offset is interpolated, so this bounds the error there." }
                isLength(definition.maxSpacing, { (millimeter) : [0.1, 5, 1000] } as LengthBoundSpec);

                annotation { "Name" : "Fit tolerance" }
                isLength(definition.fitTolerance, { (millimeter) : [0.0001, 0.001, 1] } as LengthBoundSpec);
            }
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print details", "Default" : false, "Description" : "The side each body or chain went, corners and fits." }
            definition.debugPrint is boolean;
        }
    }
    {
        const probe = referenceProbe(context, definition.sideReference);
        if (definition.offsetType == OffsetPlusType.SURFACE)
        {
            offsetSurfaces(context, id, definition, probe);
        }
        else
        {
            offsetCurves(context, id, definition, probe);
        }
    }, {
        "offsetType" : OffsetPlusType.SURFACE,
        "frameMode" : OffsetFrameMode.TRANSPORT,
        "surfaceDirection" : SurfaceOffsetDirection.NORMAL,
        "normalSurface" : qNothing(),
        "planeNormal" : qNothing(),
        "sideReference" : qNothing(),
        "towardReference" : true,
        "samplesPerEdge" : 24,
        "maxSpacing" : 5 * millimeter,
        "fitTolerance" : 0.001 * millimeter,
        "debugPrint" : false
    });

// ============================================================================
// Surfaces
// ============================================================================

/**
 * One opExtractSurface per selected body, each offset toward the side of that body the
 * reference is on (or away from it). Without a reference, all along their normals, or all
 * against them with the box off.
 */
function offsetSurfaces(context is Context, id is Id, definition is map, probe)
{
    const faces = facesOf(definition.surfaces);
    const bodies = evaluateQuery(context, qOwnerBody(faces));
    if (size(bodies) == 0)
    {
        throw regenError(ErrorStringEnum.DIRECT_EDIT_MOVE_FACE_CREATE_SELECT, ["surfaces"]);
    }

    for (var i = 0; i < size(bodies); i += 1)
    {
        const bodyFaces = qIntersection([faces, qOwnedByBody(bodies[i], EntityType.FACE)]);
        var direction = definition.towardReference ? 1 : -1;
        if (probe != undefined)
        {
            const side = sideSign(context, probe, bodyFaces);
            if (side == 0)
            {
                throw regenError("The side reference lies on surface " ~ (i + 1) ~ " (or no side of it can be read there); pick something clearly to one side.",
                    ["sideReference"]);
            }
            direction = definition.towardReference ? side : -side;
        }
        if (definition.debugPrint)
        {
            println("[offset+] surface " ~ (i + 1) ~ ": " ~ (direction > 0 ? "along" : "against") ~ " its normal.");
        }
        opExtractSurface(context, id + ("surface" ~ i), {
                    "faces" : bodyFaces,
                    "offset" : definition.distance * direction,
                    "useFacesAroundToTrimOffset" : false
                });
    }

    embedStandardOutputs(context, id, {
                "output" : qCreatedBy(id, EntityType.BODY),
                "outputDescription" : "The offset surfaces",
                "inputs" : definition.surfaces,
                "variables" : {
                    "roundedCorners" : extractableVariable(0, "Curve offsets only."),
                    "trimmedCorners" : extractableVariable(0, "Curve offsets only."),
                    "openCorners" : extractableVariable(0, "Curve offsets only.")
                },
                "queries" : offsetPlusQueries(context, [], [], [],
                        qEdgeTopologyFilter(qOwnedByBody(qCreatedBy(id, EntityType.BODY), EntityType.EDGE), EdgeTopology.ONE_SIDED))
            });
}

// ============================================================================
// Curves
// ============================================================================

/** Every chain of the selected curves, offset, fitted and stitched into one wire each. */
function offsetCurves(context is Context, id is Id, definition is map, probe)
{
    const edges = qUnion([qEntityFilter(definition.curves, EntityType.EDGE),
                qOwnedByBody(qEntityFilter(definition.curves, EntityType.BODY), EntityType.EDGE)]);
    if (isQueryEmpty(context, edges))
    {
        throw regenError("Select curves or edges to offset.", ["curves"]);
    }
    if (definition.frameMode == OffsetFrameMode.SURFACE && isQueryEmpty(context, definition.normalSurface))
    {
        throw regenError("Select the surface whose normals define the offset.", ["normalSurface"]);
    }

    var axis = vector(0, 0, 1);
    if (definition.frameMode == OffsetFrameMode.PLANE && !isQueryEmpty(context, definition.planeNormal))
    {
        axis = extractDirection(context, definition.planeNormal);
        if (axis == undefined)
        {
            throw regenError("The plane normal gives no direction.", ["planeNormal"]);
        }
    }

    const paths = constructPaths(context, edges, {});
    var counts = { "rounded" : 0, "trimmed" : 0, "open" : 0 };
    var wires = [];
    var cornerArcs = [];
    var sourceStarts = [];
    for (var p = 0; p < size(paths); p += 1)
    {
        const result = offsetPath(context, id + ("path" ~ p), definition, paths[p], probe, axis, p);
        wires = append(wires, result.wire);
        cornerArcs = concatenateArrays([cornerArcs, result.cornerArcs]);
        sourceStarts = append(sourceStarts, result.sourceStart);
        counts.rounded += result.rounded;
        counts.trimmed += result.trimmed;
        counts.open += result.open;
    }

    const output = qUnion(wires);
    if (counts.open > 0)
    {
        reportFeatureWarning(context, id, counts.open ~ " corner(s) could not be closed: the offsets there neither cross nor separate cleanly (a corner sharper than the offset allows, or skew 3D pieces). The wire has a gap at each.");
    }
    else if (counts.rounded + counts.trimmed > 0)
    {
        reportFeatureInfo(context, id, "Corners: " ~ counts.rounded ~ " rounded, " ~ counts.trimmed ~ " trimmed.");
    }

    embedStandardOutputs(context, id, {
                "output" : output,
                "outputDescription" : "The offset wires",
                "inputs" : definition.curves,
                "variables" : {
                    "roundedCorners" : extractableVariable(counts.rounded, "Corners closed with an arc."),
                    "trimmedCorners" : extractableVariable(counts.trimmed, "Corners trimmed back to the crossing."),
                    "openCorners" : extractableVariable(counts.open, "Corners left open.")
                },
                "queries" : offsetPlusQueries(context, wires, sourceStarts, cornerArcs, qNothing())
            });
}

/**
 * The keys Offset+ publishes beyond the standard ones, the same set in both modes (empty
 * where they do not apply): ends of each offset wire (start = the end at the source's
 * start), the corner arcs, and a surface offset's boundary edges.
 */
function offsetPlusQueries(context is Context, wires is array, sourceStarts is array, cornerArcs is array, boundaryEdges is Query) returns map
{
    var chains = [];
    for (var wire in wires)
    {
        chains = append(chains, qOwnedByBody(wire, EntityType.EDGE));
    }
    const ends = size(chains) > 0 ? chainEnds(context, chains, sourceStarts)
        : { "startVertex" : qNothing(), "endVertex" : qNothing(), "startEdge" : qNothing(), "endEdge" : qNothing() };
    return {
            "startVertex" : extractableQuery(ends.startVertex, "Curves: the end of each offset wire where its source starts.", DebugColor.GREEN),
            "endVertex" : extractableQuery(ends.endVertex, "Curves: the other end of each offset wire.", DebugColor.RED),
            "startEdge" : extractableQuery(ends.startEdge, "Curves: the edge at each startVertex.", DebugColor.GREEN),
            "endEdge" : extractableQuery(ends.endEdge, "Curves: the edge at each endVertex.", DebugColor.RED),
            "cornerArcs" : extractableQuery(qUnion(cornerArcs), "Curves: the arcs that round the corners.", DebugColor.MAGENTA),
            "boundaryEdges" : extractableQuery(boundaryEdges, "Surfaces: the open boundary edges of the offset surfaces.", DebugColor.CYAN)
        };
}

/**
 * One chain: stations, offset directions, side, per-edge point runs, corners, fits, wire.
 *
 * @returns {map} : { wire (Query), rounded, trimmed, open, cornerArcs (edges of the wire),
 *      sourceStart (where the source chain starts) }
 */
function offsetPath(context is Context, id is Id, definition is map, path is Path, probe, axis is Vector, index is number) returns map
{
    const sampled = pathStations(context, path, definition.samplesPerEdge, definition.maxSpacing);
    const stations = sampled.stations;
    const nEdges = size(path.edges);

    // Station nearest the reference: where the side is read (and the transport frame starts).
    var refPoint = undefined;
    var k0 = 0;
    if (probe != undefined)
    {
        refPoint = referencePointNear(context, probe, qUnion(path.edges));
        var best = inf * meter;
        for (var k = 0; k < size(stations); k += 1)
        {
            const dist = norm(stations[k].x - refPoint);
            if (!stations[k].helper && dist < best)
            {
                best = dist;
                k0 = k;
            }
        }
    }

    var directions = directionField(context, definition, stations, axis, refPoint, k0);

    var sgn = definition.towardReference ? 1 : -1;
    if (refPoint != undefined)
    {
        const side = dot(refPoint - stations[k0].x, directions[k0]);
        if (abs(side) <= REFERENCE_SIDE_MARGIN)
        {
            throw regenError("The side reference is not to either side of chain " ~ (index + 1) ~ " in this frame (it lies along the curve or on the surface); pick something clearly to one side.",
                ["sideReference"]);
        }
        sgn = ((side > 0 * meter) == definition.towardReference) ? 1 : -1;
    }
    const d = definition.distance * sgn;

    // Per-edge runs of offset points, with their true end tangents.
    var pieces = [];
    for (var j = 0; j < nEdges; j += 1)
    {
        const base = sampled.first[j];
        const last = sampled.last[j];
        var positions = [stations[base].x + d * directions[base]];
        for (var k = base + 3; k <= last - 3; k += 1)
        {
            positions = append(positions, stations[k].x + d * directions[k]);
        }
        positions = append(positions, stations[last].x + d * directions[last]);

        const p0 = stations[base].x + d * directions[base];
        const p1 = stations[base + 1].x + d * directions[base + 1];
        const p2 = stations[base + 2].x + d * directions[base + 2];
        const q0 = stations[last].x + d * directions[last];
        const q1 = stations[last - 1].x + d * directions[last - 1];
        const q2 = stations[last - 2].x + d * directions[last - 2];
        pieces = append(pieces, {
                    "positions" : positions,
                    "startDir" : normalizeOr(4 * p1 - 3 * p0 - p2, stations[base].t),
                    "endDir" : normalizeOr(3 * q0 - 4 * q1 + q2, stations[last].t),
                    "pinStart" : true,
                    "pinEnd" : true,
                    "corner" : stations[base].x,
                    "tStart" : stations[base].t,
                    "tEnd" : stations[last].t
                });
    }

    // Junctions: each piece's end against the next piece's start (and the last against the
    // first on a closed chain).
    var arcsAfter = makeArray(nEdges, []);
    var rounded = 0;
    var trimmed = 0;
    var open = 0;
    const nJoints = path.closed ? nEdges : nEdges - 1;
    for (var j = 0; j < nJoints; j += 1)
    {
        const n = (j + 1) % nEdges;
        const A = pieces[j].positions;
        const B = pieces[n].positions;
        const a = A[size(A) - 1];
        const b = B[0];
        const corner = pieces[n].corner;
        const turn = angleBetween(pieces[j].tEnd, pieces[n].tStart);

        if (turn < G1_JUNCTION_ANGLE || norm(b - a) < SAME_POINT * meter)
        {
            // Smooth: one shared point and one shared tangent on both sides.
            const shared = (a + b) / 2;
            const tangent = normalizeOr(pieces[j].endDir + pieces[n].startDir, pieces[n].tStart);
            pieces[j].positions[size(A) - 1] = shared;
            pieces[n].positions[0] = shared;
            pieces[j].endDir = tangent;
            pieces[n].startDir = tangent;
            continue;
        }

        if (norm(b - a) <= definition.fitTolerance)
        {
            // A real corner whose offsets already meet within the fit tolerance (a kink the
            // offset direction lies along, carried by the transport frame to within microns):
            // close it at one shared point, each side keeping its own tangent. An arc here
            // would be a sliver edge.
            const shared = (a + b) / 2;
            pieces[j].positions[size(A) - 1] = shared;
            pieces[n].positions[0] = shared;
            continue;
        }

        if (dot(b - a, pieces[j].tEnd) > 0 * meter)
        {
            // The offsets separate: round the gap about the corner point.
            const arcs = cornerArcs(corner, a, b);
            if (arcs == undefined)
            {
                open += 1;
                continue;
            }
            arcsAfter[j] = arcs;
            rounded += 1;
            if (definition.debugPrint)
            {
                println("[offset+] chain " ~ (index + 1) ~ " corner " ~ (j + 1) ~ ": rounded, turn "
                    ~ roundToPrecision(turn / degree, 2) ~ " deg, gap " ~ fmtMM(norm(b - a), 4) ~ " mm.");
            }
            continue;
        }

        // The offsets overlap: trim both back to their crossing.
        const crossing = polylineCrossing(A, B);
        if (crossing.distance > max(10 * definition.fitTolerance, 1e-6 * meter) || crossing.ia < 0)
        {
            open += 1;
            if (definition.debugPrint)
            {
                println("[offset+] chain " ~ (index + 1) ~ " corner " ~ (j + 1) ~ ": the offsets do not cross (closest "
                    ~ fmtMM(crossing.distance, 4) ~ " mm); left open.");
            }
            continue;
        }
        var headA = subArray(A, 0, crossing.ia + 1);
        headA = append(headA, crossing.point);
        var tailB = [crossing.point];
        tailB = concatenateArrays([tailB, subArray(B, crossing.ib + 1, size(B))]);
        if (norm(headA[0] - crossing.point) < SAME_POINT * meter || norm(tailB[size(tailB) - 1] - crossing.point) < SAME_POINT * meter)
        {
            open += 1;
            continue;
        }
        pieces[j].positions = headA;
        pieces[j].pinEnd = false;
        pieces[n].positions = tailB;
        pieces[n].pinStart = false;
        trimmed += 1;
        if (definition.debugPrint)
        {
            println("[offset+] chain " ~ (index + 1) ~ " corner " ~ (j + 1) ~ ": trimmed, turn "
                ~ roundToPrecision(turn / degree, 2) ~ " deg.");
        }
    }

    // Fit, create, stitch.
    var bodies = [];
    var arcMiddles = [];
    for (var j = 0; j < nEdges; j += 1)
    {
        const curve = fitPiece(context, definition, pieces[j]);
        const pieceId = id + ("edge" ~ j);
        opCreateBSplineCurve(context, pieceId, { "bSplineCurve" : curve });
        bodies = append(bodies, qCreatedBy(pieceId, EntityType.BODY));
        if (definition.debugPrint)
        {
            println("[offset+] chain " ~ (index + 1) ~ " edge " ~ (j + 1) ~ ": " ~ size(pieces[j].positions) ~ " points, "
                ~ size(curve.controlPoints) ~ " control points.");
        }
        for (var k = 0; k < size(arcsAfter[j]); k += 1)
        {
            const arcId = id + ("arc" ~ j ~ "_" ~ k);
            opCreateBSplineCurve(context, arcId, { "bSplineCurve" : arcsAfter[j][k] });
            bodies = append(bodies, qCreatedBy(arcId, EntityType.BODY));
            arcMiddles = append(arcMiddles, evEdgeTangentLine(context, { "edge" : qCreatedBy(arcId, EntityType.EDGE), "parameter" : 0.5 }).origin);
        }
    }
    const pieceBodies = qUnion(bodies);
    opExtractWires(context, id + "wire", { "edges" : qOwnedByBody(pieceBodies, EntityType.EDGE) });
    opDeleteBodies(context, id + "deletePieces", { "entities" : pieceBodies });

    // The corner arcs as edges of the wire: the wire edge through each arc's middle.
    const wire = qCreatedBy(id + "wire", EntityType.BODY);
    var arcEdges = [];
    for (var middle in arcMiddles)
    {
        arcEdges = append(arcEdges, evaluateQuery(context, qClosestTo(qOwnedByBody(wire, EntityType.EDGE), middle))[0]);
    }
    const first = evEdgeTangentLine(context, { "edge" : path.edges[0], "parameter" : path.flipped[0] ? 1 : 0 }).origin;
    return { "wire" : wire, "rounded" : rounded, "trimmed" : trimmed, "open" : open, "cornerArcs" : arcEdges, "sourceStart" : first };
}

/**
 * Stations along a chain, edge by edge in travel order: per edge the start, two helper
 * stations just after it, n - 1 interior stations, two helpers just before the end, and the
 * end, n being the larger of `count` and length / maxSpacing. Helpers only feed the end
 * tangents. Tangents follow the direction of travel.
 *
 * @returns {map} : { stations, first, last } -- first[j] / last[j] the index of edge j's
 *      start and end station.
 */
function pathStations(context is Context, path is Path, count is number, maxSpacing is ValueWithUnits) returns map
{
    var stations = [];
    var first = [];
    var last = [];
    for (var j = 0; j < size(path.edges); j += 1)
    {
        const n = max(count, ceil(evLength(context, { "entities" : path.edges[j] }) / maxSpacing));
        var ts = [0, TANGENT_STEP, 2 * TANGENT_STEP];
        for (var k = 1; k < n; k += 1)
        {
            ts = append(ts, k / n);
        }
        ts = concatenateArrays([ts, [1 - 2 * TANGENT_STEP, 1 - TANGENT_STEP, 1]]);
        const m = size(ts);

        const flipped = path.flipped[j];
        var parameters = [];
        for (var t in ts)
        {
            parameters = append(parameters, flipped ? 1 - t : t);
        }
        const lines = evEdgeTangentLines(context, { "edge" : path.edges[j], "parameters" : parameters });
        first = append(first, size(stations));
        for (var k = 0; k < m; k += 1)
        {
            stations = append(stations, {
                        "x" : lines[k].origin,
                        "t" : flipped ? -lines[k].direction : lines[k].direction,
                        "helper" : k == 1 || k == 2 || k == m - 2 || k == m - 3
                    });
        }
        last = append(last, size(stations) - 1);
    }
    return { "stations" : stations, "first" : first, "last" : last };
}

/**
 * The unit offset direction at every station, perpendicular to the tangent, with an
 * arbitrary but continuous orientation (the side is chosen afterwards).
 */
function directionField(context is Context, definition is map, stations is array, axis is Vector, refPoint, k0 is number) returns array
{
    const n = size(stations);
    var directions = makeArray(n);

    if (definition.frameMode == OffsetFrameMode.PLANE)
    {
        for (var k = 0; k < n; k += 1)
        {
            const c = cross(axis, stations[k].t);
            directions[k] = norm(c) > 1e-9 ? normalize(c) : (k > 0 ? directions[k - 1] : undefined);
        }
        if (directions[0] == undefined)
        {
            throw regenError("The curve runs along the plane normal at its start; no plane offset exists there.", ["planeNormal"]);
        }
        return directions;
    }

    if (definition.frameMode == OffsetFrameMode.SURFACE)
    {
        const faces = evaluateQuery(context, facesOf(definition.normalSurface));
        var previous = undefined;
        for (var k = 0; k < n; k += 1)
        {
            const foot = evDistance(context, { "side0" : stations[k].x, "side1" : qUnion(faces) });
            var normal = evFaceTangentPlane(context, { "face" : faces[foot.sides[1].index], "parameter" : foot.sides[1].parameter }).normal;
            // Continuous normals, whatever the faces' orientations.
            if (previous != undefined && dot(normal, previous) < 0)
            {
                normal = -normal;
            }
            previous = normal;
            const t = stations[k].t;
            const raw = definition.surfaceDirection == SurfaceOffsetDirection.NORMAL ? normal - dot(normal, t) * t : cross(normal, t);
            if (norm(raw) < 1e-9)
            {
                throw regenError("The curve runs along the surface normal; no offset direction exists there.", ["normalSurface"]);
            }
            directions[k] = normalize(raw);
        }
        return directions;
    }

    // Transport: seed at k0, carry forward and backward.
    directions[k0] = transportSeed(stations, refPoint, k0);
    for (var k = k0 + 1; k < n; k += 1)
    {
        directions[k] = transportStep(directions[k - 1], stations[k - 1], stations[k]);
    }
    for (var k = k0 - 1; k >= 0; k -= 1)
    {
        directions[k] = transportStep(directions[k + 1], stations[k + 1], stations[k]);
    }
    return directions;
}

/**
 * The transport frame's starting direction at station k0: toward the reference when there
 * is one; else, for a planar chain, the in-plane normal; else the turn of the tangent over
 * the first stations; else any perpendicular.
 */
function transportSeed(stations is array, refPoint, k0 is number) returns Vector
{
    const t = stations[k0].t;
    if (refPoint != undefined)
    {
        const v = (refPoint - stations[k0].x) / meter;
        const perpendicular = v - dot(v, t) * t;
        if (norm(perpendicular) < 1e-9)
        {
            throw regenError("The side reference lies along the curve's tangent; it cannot start a transport frame.", ["sideReference"]);
        }
        return normalize(perpendicular);
    }

    const planeNormal = chainPlaneNormal(stations);
    if (planeNormal != undefined)
    {
        return normalize(cross(planeNormal, t));
    }
    for (var k = k0 + 1; k < size(stations); k += 1)
    {
        const turn = stations[k].t - dot(stations[k].t, t) * t;
        if (norm(turn) > 1e-6)
        {
            return normalize(turn);
        }
    }
    const helper = abs(t[2]) < 0.9 ? vector(0, 0, 1) : vector(1, 0, 0);
    return normalize(cross(helper, t));
}

/** The normal of the plane holding every station, or undefined when there is none (3D, or a straight line). */
function chainPlaneNormal(stations is array)
{
    const origin = stations[0].x;
    var far = origin;
    for (var s in stations)
    {
        if (norm(s.x - origin) > norm(far - origin))
        {
            far = s.x;
        }
    }
    const chord = (far - origin) / meter;
    if (norm(chord) < 1e-9)
    {
        return undefined;
    }
    var best = 0;
    var normal = undefined;
    for (var s in stations)
    {
        const c = cross(chord, (s.x - origin) / meter);
        if (norm(c) > best)
        {
            best = norm(c);
            normal = c;
        }
    }
    if (normal == undefined || best < 1e-12)
    {
        return undefined;
    }
    normal = normalize(normal);
    for (var s in stations)
    {
        if (abs(dot((s.x - origin) / meter, normal)) > 1e-7)
        {
            return undefined;
        }
    }
    return normal;
}

/**
 * Carries a direction from one station to the next without twist: the double-reflection
 * rotation-minimizing step (Wang et al. 2008), or, between stations at the same point (a
 * junction), the minimal rotation taking one tangent to the other -- a single reflection
 * there would mirror the direction to the wrong side of a planar corner.
 */
function transportStep(r is Vector, from is map, to is map) returns Vector
{
    const v1 = (to.x - from.x) / meter;
    const c1 = dot(v1, v1);
    var carried;
    if (c1 > SAME_POINT * SAME_POINT)
    {
        const rL = r - (2 / c1) * dot(v1, r) * v1;
        const tL = from.t - (2 / c1) * dot(v1, from.t) * v1;
        const v2 = to.t - tL;
        const c2 = dot(v2, v2);
        carried = c2 > 1e-24 ? rL - (2 / c2) * dot(v2, rL) * v2 : rL;
    }
    else
    {
        carried = rotateMinimal(r, from.t, to.t);
    }
    return normalize(carried - dot(carried, to.t) * to.t);
}

/** r rotated by the rotation that takes t0 to t1 about their common normal (Rodrigues). */
function rotateMinimal(r is Vector, t0 is Vector, t1 is Vector) returns Vector
{
    const axisRaw = cross(t0, t1);
    const s = norm(axisRaw);
    if (s < 1e-12)
    {
        return r;
    }
    const k = axisRaw / s;
    const c = dot(t0, t1);
    return r * c + cross(k, r) * s + k * dot(k, r) * (1 - c);
}

/**
 * Exact circular arcs (rational quadratics) from a to b about center, both at the same
 * distance from it: one arc up to 90 degrees, two halves beyond. Undefined for a turn so
 * close to 180 degrees that the arc's plane is not defined.
 */
function cornerArcs(center is Vector, a is Vector, b is Vector)
{
    const u = a - center;
    const w = b - center;
    const radius = norm(u);
    const cosine = dot(u, w) / (radius * norm(w));
    if (cosine < -0.99999 || radius < SAME_POINT * meter)
    {
        return undefined;
    }
    const sweep = acos(min(1, cosine));
    if (sweep <= 90 * degree)
    {
        return [arcSpan(center, a, b, sweep)];
    }
    const middle = center + radius * normalize(u / radius + w / norm(w));
    return [arcSpan(center, a, middle, sweep / 2), arcSpan(center, middle, b, sweep / 2)];
}

/** One rational quadratic arc from a to b about center, sweeping `sweep` (< 180 degrees). */
function arcSpan(center is Vector, a is Vector, b is Vector, sweep is ValueWithUnits) returns BSplineCurve
{
    const half = cos(sweep / 2);
    const bisector = normalize((a - center) / meter + (b - center) / meter);
    const apex = center + (norm(a - center) / half) * bisector;
    return bSplineCurve({
                "degree" : 2,
                "isPeriodic" : false,
                "controlPoints" : [a, apex, b],
                "weights" : [1, half, 1],
                "knots" : knotArray([0, 0, 0, 1, 1, 1])
            });
}

/**
 * Where two point runs cross: the closest pair of segments, one from each, and the
 * midpoint of their closest points.
 *
 * @returns {map} : { distance, point, ia, ib } -- ia / ib the segment indices (-1 if none).
 */
function polylineCrossing(A is array, B is array) returns map
{
    var best = { "distance" : inf * meter, "point" : A[size(A) - 1], "ia" : -1, "ib" : -1 };
    for (var ia = 0; ia < size(A) - 1; ia += 1)
    {
        for (var ib = 0; ib < size(B) - 1; ib += 1)
        {
            const c = closestOnSegments(A[ia], A[ia + 1], B[ib], B[ib + 1]);
            if (c.distance < best.distance)
            {
                best = { "distance" : c.distance, "point" : (c.p + c.q) / 2, "ia" : ia, "ib" : ib };
            }
        }
    }
    return best;
}

/** Closest points of segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9). */
function closestOnSegments(p1 is Vector, q1 is Vector, p2 is Vector, q2 is Vector) returns map
{
    const d1 = (q1 - p1) / meter;
    const d2 = (q2 - p2) / meter;
    const r = (p1 - p2) / meter;
    const a = dot(d1, d1);
    const e = dot(d2, d2);
    const f = dot(d2, r);
    var s = 0;
    var t = 0;
    if (a > 1e-24 || e > 1e-24)
    {
        if (a <= 1e-24)
        {
            t = clamp01(f / e);
        }
        else
        {
            const c = dot(d1, r);
            if (e <= 1e-24)
            {
                s = clamp01(-c / a);
            }
            else
            {
                const bb = dot(d1, d2);
                const denom = a * e - bb * bb;
                s = denom > 1e-24 ? clamp01((bb * f - c * e) / denom) : 0;
                t = (bb * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = clamp01(-c / a);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = clamp01((bb - c) / a);
                }
            }
        }
    }
    const cp = p1 + (d1 * s) * meter;
    const cq = p2 + (d2 * t) * meter;
    return { "distance" : norm(cp - cq), "p" : cp, "q" : cq };
}

function clamp01(x is number) returns number
{
    return min(1, max(0, x));
}

/** The unit vector along v, or the fallback when v vanishes. */
function normalizeOr(v is Vector, fallback is Vector) returns Vector
{
    const length = norm(v);
    if ((length is ValueWithUnits && length < SAME_POINT * meter) || (length is number && length < SAME_POINT))
    {
        return fallback;
    }
    return v / length;
}

/**
 * The spline through one run of offset points, end tangents pinned where the run ends at
 * its source edge's end (not where a corner trim cut it), end control points set to the
 * exact end points so neighbouring pieces stitch bit for bit.
 */
function fitPiece(context is Context, definition is map, piece is map) returns BSplineCurve
{
    const positions = piece.positions;
    var chord = 0 * meter;
    for (var k = 0; k < size(positions) - 1; k += 1)
    {
        chord += norm(positions[k + 1] - positions[k]);
    }
    var target = { "positions" : positions };
    if (piece.pinStart)
    {
        target.startDerivative = piece.startDir * chord;
    }
    if (piece.pinEnd)
    {
        target.endDerivative = piece.endDir * chord;
    }
    var curve = approximateSpline(context, {
                "degree" : 3,
                "tolerance" : definition.fitTolerance,
                "isPeriodic" : false,
                "targets" : [approximationTarget(target)],
                "suppressInterpolationNotice" : true
            })[0];
    var controlPoints = curve.controlPoints;
    controlPoints[0] = positions[0];
    controlPoints[size(controlPoints) - 1] = positions[size(positions) - 1];
    curve.controlPoints = controlPoints;
    return curve;
}
