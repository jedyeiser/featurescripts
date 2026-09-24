FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/f4f872fe20d1498201fed64d/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

/**
 * Fillet wire: rounds chosen G0 corners of a curve (a wire, or a chain of edges) with a radius.
 * Design: research_fillet_wire.md.
 *
 * Corners are the vertices where the tangent turns by more than the corner angle. They are shown as points
 * in the graphics area; click them to choose which to fillet, or use Apply to all. Each chosen corner can
 * override the radius.
 *
 * Tangent (default): an exact circular arc of the radius, tangent to both edges. Only possible where the two
 * edges lie in one plane at the corner; other corners are reported and left sharp.
 * Curvature: a curvature-continuous blend whose average curvature is 1 / radius (the length an arc of that
 * radius would need for the same turn).
 *
 * Output. Wire bodies are edited IN PLACE (default): each wire is split into separate wires at the fillet
 * points, and only the small two-edge corner pieces are changed (opEditCurve) -- everything else keeps its
 * identity. Join into one wire rebuilds a single wire afterwards (a new body). Edges (from sketches or other
 * bodies) always give a new wire: they are copied into one first, then treated the same way.
 */

/** How a fillet meets the edges on either side. */
export enum FilletWireContinuity
{
    annotation { "Name" : "Tangent" }
    TANGENT,
    annotation { "Name" : "Curvature" }
    CURVATURE
}

/** Setback solutions closer than this are converged; positions closer than this coincide. */
const FILLET_TOLERANCE = 1e-7 * meter;

/** Corner angle: default 0.5 degrees. */
export const FILLET_CORNER_ANGLE_BOUNDS = { (degree) : [0.001, 0.5, 90] } as AngleBoundSpec;

annotation { "Feature Type Name" : "Fillet wire",
        "Feature Type Description" : "Round the corners of a wire or a chain of edges: exact arcs (tangent) or curvature-continuous blends.",
        "Manipulator Change Function" : "filletWireManipulatorChange" }
export const filletWire = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Curves", "Filter" : (EntityType.EDGE || (EntityType.BODY && BodyType.WIRE)) && ConstructionObject.NO,
                    "Description" : "Wire bodies (edited in place) or edges (copied into a new wire)." }
        definition.curves is Query;

        annotation { "Name" : "Continuity", "Default" : FilletWireContinuity.TANGENT, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.continuity is FilletWireContinuity;

        annotation { "Name" : "Radius" }
        isLength(definition.radius, BLEND_BOUNDS);

        annotation { "Name" : "Apply to all corners", "Default" : false,
                    "Description" : "Fillet every corner that can be filleted. Off: only the corners clicked in the graphics area." }
        definition.applyToAll is boolean;

        annotation { "Name" : "Corners", "Item name" : "Corner", "Item label template" : "#cornerRadius",
                    "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                    "Description" : "Filled in by clicking corners in the graphics area. Each can override the radius." }
        definition.corners is array;
        for (var corner in definition.corners)
        {
            annotation { "Name" : "Corner", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            corner.vertex is Query;

            annotation { "Name" : "Own radius", "Default" : false }
            corner.overrideRadius is boolean;

            if (corner.overrideRadius)
            {
                annotation { "Name" : "Radius" }
                isLength(corner.cornerRadius, BLEND_BOUNDS);
            }
        }

        annotation { "Name" : "Join into one wire", "Default" : false,
                    "Description" : "Wire bodies only. Off: the wire stays split into pieces (true in-place edit; untouched pieces keep their identity). On: one new wire." }
        definition.joinPieces is boolean;

        annotation { "Group Name" : "Advanced", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Corner angle", "Description" : "A vertex is a corner when the tangent turns by more than this." }
            isAngle(definition.cornerAngle, FILLET_CORNER_ANGLE_BOUNDS);

            annotation { "Name" : "Print corners", "Default" : false }
            definition.debugPrint is boolean;
        }
    }
    {
        const chains = cornerChains(context, definition);
        if (size(chains) == 0)
        {
            throw regenError("Select wire bodies or edges.", ["curves"]);
        }

        // Every corner, its radius, and whether it is chosen.
        var all = [];
        for (var c = 0; c < size(chains); c += 1)
        {
            for (var corner in chains[c].corners)
            {
                all = append(all, corner);
            }
        }
        const picks = pickedCorners(context, definition, all);
        var solved = [];
        var skipped = [];
        for (var i = 0; i < size(all); i += 1)
        {
            if (!picks.chosen[i])
            {
                solved = append(solved, undefined);
                continue;
            }
            const result = solveCorner(context, definition, all[i], picks.radius[i]);
            if (result.error != undefined)
            {
                skipped = append(skipped, "corner at " ~ fmtPoint(all[i].point) ~ ": " ~ result.error);
            }
            solved = append(solved, result.error == undefined ? result : undefined);
        }
        const checked = rejectOverlaps(all, solved, skipped);
        solved = checked.solved;
        skipped = checked.skipped;

        addManipulators(context, id, { "corners" : {
                        "points" : mapArray(all, function(x) { return x.point; }),
                        "selectedIndices" : picks.selected,
                        "suppressedIndices" : [],
                        "manipulatorType" : ManipulatorType.TOGGLE_POINTS } as Manipulator });

        // Build: per chain, a working wire (the input wire, or a copy of the edges), split and edited.
        var outputs = [];
        var filletEdges = [];
        var k = 0;
        for (var c = 0; c < size(chains); c += 1)
        {
            var mine = [];
            for (var j = 0; j < size(chains[c].corners); j += 1)
            {
                if (solved[k] != undefined)
                {
                    mine = append(mine, solved[k]);
                }
                k += 1;
            }
            const built = filletChain(context, id + ("chain" ~ c), definition, chains[c], mine);
            outputs = append(outputs, built.output);
            filletEdges = concatenateArrays([filletEdges, built.fillets]);
        }

        var filleted = 0;
        for (var s in solved)
        {
            if (s != undefined)
            {
                filleted += 1;
            }
        }
        if (size(skipped) > 0)
        {
            reportFeatureWarning(context, id, size(skipped) ~ " corner(s) not filleted: " ~ join(skipped, "; "));
        }
        else if (filleted == 0)
        {
            reportFeatureInfo(context, id, size(all) ~ " corner(s) found. Click them in the graphics area, or use Apply to all corners.");
        }
        if (definition.debugPrint)
        {
            for (var i = 0; i < size(all); i += 1)
            {
                println("[fillet wire] corner " ~ (i + 1) ~ " at " ~ fmtPoint(all[i].point) ~ ": turn " ~ roundToPrecision(all[i].angle / degree, 3)
                    ~ " deg" ~ (solved[i] != undefined ? ", filleted, setbacks " ~ fmtMM(solved[i].dA) ~ " / " ~ fmtMM(solved[i].dB) ~ " mm" : ""));
            }
        }

        embedStandardOutputs(context, id, {
                    "output" : qUnion(outputs),
                    "outputDescription" : "The filleted wires",
                    "inputs" : definition.curves,
                    "variables" : {
                        "cornerCount" : extractableVariable(size(all), "Corners found."),
                        "filletCount" : extractableVariable(filleted, "Corners filleted."),
                        "skippedCount" : extractableVariable(size(skipped), "Chosen corners that could not be filleted.")
                    },
                    "queries" : {
                        "filletEdges" : extractableQuery(qUnion(filletEdges), "The fillet curves.", DebugColor.MAGENTA)
                    }
                });
    }, {
        "continuity" : FilletWireContinuity.TANGENT,
        "applyToAll" : false,
        "corners" : [],
        "joinPieces" : false,
        "cornerAngle" : 0.5 * degree,
        "debugPrint" : false
    });

// ============================================================================
// Manipulator: clicked corners -> the Corners list
// ============================================================================

/**
 * Keeps the Corners list in step with the points clicked: a newly clicked corner is added (its vertex stored
 * robustly), an unclicked one removed; the others keep their radius overrides.
 */
export function filletWireManipulatorChange(context is Context, definition is map, newManipulators is map) returns map
{
    if (newManipulators["corners"] == undefined)
    {
        return definition;
    }
    var all = [];
    for (var chain in cornerChains(context, definition))
    {
        all = concatenateArrays([all, chain.corners]);
    }
    var corners = [];
    for (var index in newManipulators["corners"].selectedIndices)
    {
        if (index >= size(all))
        {
            continue;
        }
        const corner = all[index];
        var entry = undefined;
        for (var existing in definition.corners)
        {
            const at = vertexPoint(context, existing.vertex);
            if (at != undefined && norm(at - corner.point) < 1e-6 * meter)
            {
                entry = existing;
            }
        }
        if (entry == undefined)
        {
            entry = { "vertex" : makeRobustQuery(context, corner.vertex), "overrideRadius" : false, "cornerRadius" : definition.radius };
        }
        corners = append(corners, entry);
    }
    definition.corners = corners;
    return definition;
}

/**
 * Which corners are chosen, and their radii.
 * @returns {map} : { chosen (booleans), radius (lengths), selected (indices for the manipulator) }
 */
function pickedCorners(context is Context, definition is map, all is array) returns map
{
    var chosen = makeArray(size(all), definition.applyToAll);
    var radius = makeArray(size(all), definition.radius);
    var selected = [];
    for (var entry in definition.corners)
    {
        const at = vertexPoint(context, entry.vertex);
        if (at == undefined)
        {
            continue;
        }
        for (var i = 0; i < size(all); i += 1)
        {
            if (norm(all[i].point - at) < 1e-6 * meter)
            {
                chosen[i] = true;
                if (entry.overrideRadius == true)
                {
                    radius[i] = entry.cornerRadius;
                }
            }
        }
    }
    for (var i = 0; i < size(all); i += 1)
    {
        if (chosen[i])
        {
            selected = append(selected, i);
        }
    }
    return { "chosen" : chosen, "radius" : radius, "selected" : selected };
}

function vertexPoint(context is Context, vertex)
{
    if (!(vertex is Query) || isQueryEmpty(context, vertex))
    {
        return undefined;
    }
    return evVertexPoint(context, { "vertex" : vertex });
}

// ============================================================================
// Chains and corners
// ============================================================================

/**
 * The selection as chains: each wire body selected whole is its own chain (edited in place); loose edges are
 * grouped into chains by connectivity (copied into new wires).
 *
 * @returns {array} : of { inPlace, wire (the body when in place), path (Path), corners (array) }
 */
function cornerChains(context is Context, definition is map) returns array
{
    const wires = evaluateQuery(context, qSketchFilter(qBodyType(qEntityFilter(definition.curves, EntityType.BODY), BodyType.WIRE), SketchObject.NO));
    const inWires = qOwnedByBody(qUnion(wires), EntityType.EDGE);
    const looseEdges = qSubtraction(qConstructionFilter(qUnion([qEntityFilter(definition.curves, EntityType.EDGE),
                        qOwnedByBody(qEntityFilter(definition.curves, EntityType.BODY), EntityType.EDGE)]), ConstructionObject.NO), inWires);
    var chains = [];
    for (var wire in wires)
    {
        for (var path in constructPaths(context, qOwnedByBody(wire, EntityType.EDGE), {}))
        {
            chains = append(chains, { "inPlace" : true, "wire" : wire, "path" : path });
        }
    }
    if (!isQueryEmpty(context, looseEdges))
    {
        for (var path in constructPaths(context, looseEdges, {}))
        {
            chains = append(chains, { "inPlace" : false, "wire" : undefined, "path" : path });
        }
    }
    for (var c = 0; c < size(chains); c += 1)
    {
        chains[c].corners = chainCorners(context, chains[c].path, definition.cornerAngle, c);
    }
    return chains;
}

/**
 * The corners of one chain: vertices between consecutive edges (and the seam of a closed chain) where the
 * tangent turns by more than `cornerAngle` (and less than a reversal).
 */
function chainCorners(context is Context, path is Path, cornerAngle is ValueWithUnits, chainIndex is number) returns array
{
    const n = size(path.edges);
    var ends = [];
    var lengths = [];
    for (var i = 0; i < n; i += 1)
    {
        const lines = evEdgeTangentLines(context, { "edge" : path.edges[i], "parameters" : [0, 1] });
        const flipped = path.flipped[i];
        ends = append(ends, {
                    "start" : flipped ? lines[1].origin : lines[0].origin,
                    "end" : flipped ? lines[0].origin : lines[1].origin,
                    "startTangent" : flipped ? -lines[1].direction : lines[0].direction,
                    "endTangent" : flipped ? -lines[0].direction : lines[1].direction
                });
        lengths = append(lengths, evLength(context, { "entities" : path.edges[i] }));
    }
    var corners = [];
    const last = path.closed ? n : n - 1;
    for (var i = 0; i < last; i += 1)
    {
        const j = (i + 1) % n;
        const tA = ends[i].endTangent;
        const tB = ends[j].startTangent;
        const turn = angleBetween(tA, tB);
        if (turn <= cornerAngle || turn >= 179 * degree)
        {
            continue;
        }
        const point = ends[i].end;
        corners = append(corners, {
                    "chain" : chainIndex,
                    "a" : i, "b" : j,
                    "edgeA" : path.edges[i], "flipA" : path.flipped[i], "lengthA" : lengths[i],
                    "edgeB" : path.edges[j], "flipB" : path.flipped[j], "lengthB" : lengths[j],
                    "point" : point, "tA" : tA, "tB" : tB, "angle" : turn,
                    "vertex" : qClosestTo(qAdjacent(path.edges[i], AdjacencyType.VERTEX, EntityType.VERTEX), point)
                });
    }
    return corners;
}

// ============================================================================
// Solving one corner
// ============================================================================

/** Point and travel direction on edge A at distance d back from the corner, and on edge B at d forward. */
function sideA(context is Context, corner is map, d is ValueWithUnits) returns Line
{
    const u = d / corner.lengthA;
    const tl = evEdgeTangentLine(context, { "edge" : corner.edgeA, "parameter" : corner.flipA ? u : 1 - u });
    return line(tl.origin, corner.flipA ? -tl.direction : tl.direction);
}

function sideB(context is Context, corner is map, d is ValueWithUnits) returns Line
{
    const u = d / corner.lengthB;
    const tl = evEdgeTangentLine(context, { "edge" : corner.edgeB, "parameter" : corner.flipB ? 1 - u : u });
    return line(tl.origin, corner.flipB ? -tl.direction : tl.direction);
}

/**
 * The fillet at one corner.
 * @returns {map} : { dA, dB (setbacks along each edge), curve (BSplineCurve), corner } or { error }
 */
function solveCorner(context is Context, definition is map, corner is map, r is ValueWithUnits) returns map
{
    if (definition.continuity == FilletWireContinuity.CURVATURE)
    {
        return { "error" : "Curvature continuity is not built yet" };
    }
    return solveArc(context, corner, r);
}

/**
 * Tangent mode: the circle of radius r tangent to both edges. Its centre is r inward from each edge along the
 * in-plane normal; the setbacks (dA, dB) make those two centres coincide (Gauss-Newton, finite differences,
 * from the straight-edge setback r tan(turn / 2)). No solution with a common plane -> not filleted.
 */
function solveArc(context is Context, corner is map, r is ValueWithUnits) returns map
{
    const n = normalize(cross(corner.tA, corner.tB));
    const centreA = function(d is ValueWithUnits) returns Vector
        {
            const s = sideA(context, corner, d);
            return s.origin + r * normalize(cross(n, s.direction));
        };
    const centreB = function(d is ValueWithUnits) returns Vector
        {
            const s = sideB(context, corner, d);
            return s.origin + r * normalize(cross(n, s.direction));
        };
    var dA = r * tan(corner.angle / 2);
    var dB = dA;
    const h = 1e-6 * meter;
    for (var iteration = 0; iteration < 30; iteration += 1)
    {
        if (dA <= 0 * meter || dB <= 0 * meter || dA >= corner.lengthA || dB >= corner.lengthB)
        {
            return { "error" : "the radius is too large for the edges at this corner" };
        }
        const f = centreA(dA) - centreB(dB);
        if (norm(f) < FILLET_TOLERANCE)
        {
            break;
        }
        const ja = (centreA(dA + h) - centreA(dA)) / h;
        const jb = -(centreB(dB + h) - centreB(dB)) / h;
        // normal equations of the 3 x 2 system [ja jb] [x y]^T = -f
        const a11 = dot(ja, ja);
        const a12 = dot(ja, jb);
        const a22 = dot(jb, jb);
        const b1 = -dot(ja, f);
        const b2 = -dot(jb, f);
        const det = a11 * a22 - a12 * a12;
        if (abs(det) < 1e-20)
        {
            return { "error" : "no circle of this radius touches both edges" };
        }
        dA += (a22 * b1 - a12 * b2) / det;
        dB += (a11 * b2 - a12 * b1) / det;
    }
    const sa = sideA(context, corner, dA);
    const sb = sideB(context, corner, dB);
    const centre = sa.origin + r * normalize(cross(n, sa.direction));
    const mismatch = norm(centre - (sb.origin + r * normalize(cross(n, sb.direction))));
    const offPlane = abs(dot(sb.origin - sa.origin, n));
    if (mismatch > 1e-6 * meter || offPlane > 1e-6 * meter || abs(dot(sa.direction, n)) > 1e-6 || abs(dot(sb.direction, n)) > 1e-6)
    {
        return { "error" : "the edges do not lie in one plane here, so no arc touches both (use Curvature)" };
    }
    // exact arc: rational quadratic through the tangent points, middle control point where the tangent lines meet
    const sweep = angleBetween(sa.direction, sb.direction);
    const reach = r * tan(sweep / 2);
    const curve = bSplineCurve({ "degree" : 2, "isPeriodic" : false,
                "controlPoints" : [sa.origin, sa.origin + reach * sa.direction, sb.origin],
                "weights" : [1, cos(sweep / 2), 1], "knots" : knotArray([0, 1]) });
    return { "dA" : dA, "dB" : dB, "curve" : curve, "corner" : corner, "pointA" : sa, "pointB" : sb };
}

/** A corner whose setback runs into the next corner's is not filleted. Returns the updated { solved, skipped }. */
function rejectOverlaps(all is array, solvedIn is array, skippedIn is array) returns map
{
    var solved = solvedIn;
    var skipped = skippedIn;
    for (var i = 0; i < size(all); i += 1)
    {
        if (solved[i] == undefined)
        {
            continue;
        }
        for (var j = 0; j < size(all); j += 1)
        {
            if (j == i || solved[j] == undefined || all[j].chain != all[i].chain)
            {
                continue;
            }
            // corner i uses edge B = its own b; corner j uses edge A = j.a; same edge -> the setbacks share it
            if (all[i].b == all[j].a && solved[i].dB + solved[j].dA >= all[i].lengthB - FILLET_TOLERANCE)
            {
                skipped = append(skipped, "corners at " ~ fmtPoint(all[i].point) ~ " and " ~ fmtPoint(all[j].point) ~ ": their fillets overlap on the edge between them");
                solved[j] = undefined;
            }
        }
    }
    return { "solved" : solved, "skipped" : skipped };
}

// ============================================================================
// Building
// ============================================================================

/**
 * One chain: its working wire (the selected wire, or a copy of the edges) split into separate wires at every
 * fillet point, each two-edge corner piece replaced by its fillet (opEditCurve); optionally joined again.
 *
 * @returns {map} : { output (Query: the chain's wires), fillets (array of fillet edge queries) }
 */
function filletChain(context is Context, id is Id, definition is map, chain is map, fillets is array) returns map
{
    var wire;
    if (chain.inPlace)
    {
        wire = chain.wire;
    }
    else
    {
        opExtractWires(context, id + "copy", { "edges" : qUnion(chain.path.edges) });
        wire = qCreatedBy(id + "copy", EntityType.BODY);
    }
    if (size(fillets) == 0)
    {
        return { "output" : wire, "fillets" : [] };
    }

    // split into wires at both tangent points of every fillet (tiny trimmed planes: nothing else is cut)
    var pieces = wire;
    var k = 0;
    for (var f in fillets)
    {
        for (var at in [f.pointA, f.pointB])
        {
            const half = min(1 * millimeter, 0.25 * min(f.dA, f.dB));
            const planeId = id + ("cutPlane" ~ k);
            opPlane(context, planeId, { "plane" : plane(at.origin, at.direction), "width" : 2 * half, "height" : 2 * half });
            const splitId = id + ("cut" ~ k);
            opSplitPart(context, splitId, { "targets" : pieces, "tool" : qCreatedBy(planeId, EntityType.FACE), "keepTools" : false, "useTrimmed" : true });
            pieces = qUnion([pieces, qCreatedBy(splitId, EntityType.BODY)]);
            opDeleteBodies(context, id + ("deletePlane" ~ k), { "entities" : qCreatedBy(planeId, EntityType.BODY) });
            k += 1;
        }
    }

    // each corner piece becomes its fillet
    var filletEdges = [];
    for (var i = 0; i < size(fillets); i += 1)
    {
        const f = fillets[i];
        const piece = qOwnerBody(qContainsPoint(qOwnedByBody(pieces, EntityType.VERTEX), f.corner.point));
        if (size(evaluateQuery(context, piece)) != 1)
        {
            throw regenError("Internal: the piece around the corner at " ~ fmtPoint(f.corner.point) ~ " was not isolated.");
        }
        const curveId = id + ("fillet" ~ i);
        opCreateBSplineCurve(context, curveId, { "bSplineCurve" : f.curve });
        opEditCurve(context, id + ("edit" ~ i), { "wire" : piece, "edge" : qCreatedBy(curveId, EntityType.EDGE) });
        opDeleteBodies(context, id + ("deleteFillet" ~ i), { "entities" : qCreatedBy(curveId, EntityType.BODY) });
        filletEdges = append(filletEdges, qClosestTo(qOwnedByBody(pieces, EntityType.EDGE), evaluateSpline({ "spline" : f.curve, "parameters" : [0.5] })[0][0]));
    }

    const all = qUnion(evaluateQuery(context, qBodyType(pieces, BodyType.WIRE)));
    if (!chain.inPlace || definition.joinPieces)
    {
        const fillPoints = mapArray(fillets, function(f) { return evaluateSpline({ "spline" : f.curve, "parameters" : [0.5] })[0][0]; });
        opExtractWires(context, id + "join", { "edges" : qOwnedByBody(all, EntityType.EDGE) });
        opDeleteBodies(context, id + "deletePieces", { "entities" : qSubtraction(all, qCreatedBy(id + "join", EntityType.BODY)) });
        const joined = qCreatedBy(id + "join", EntityType.BODY);
        return { "output" : joined, "fillets" : mapArray(fillPoints, function(p) { return qClosestTo(qOwnedByBody(joined, EntityType.EDGE), p); }) };
    }
    return { "output" : all, "fillets" : filletEdges };
}

// ============================================================================
// Formatting
// ============================================================================

function fmtMM(v is ValueWithUnits) returns string
{
    return toString(roundToPrecision(v / millimeter, 4));
}

function fmtPoint(p is Vector) returns string
{
    return "(" ~ fmtMM(p[0]) ~ ", " ~ fmtMM(p[1]) ~ ", " ~ fmtMM(p[2]) ~ ") mm";
}
