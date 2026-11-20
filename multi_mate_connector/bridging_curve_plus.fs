FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/path.fs", version : "3095.0");
import(path : "onshape/std/bridgingCurve.fs", version : "3095.0");
// IMPORT: mc_core.fs (same document: references, paths, arc length)
import(path : "0a18eeece6adacc759621422", version : "2095707b9424d45d3bf94814");
// IMPORT: bridging_curve_plus_icon.svg (feature icon)
IconNamespace::import(path : "9a0cb4233794456e403f1496", version : "014d2f7de0958ebca210756e");

/**
 * Bridging curve +.
 *
 * Works like Onshape's Bridging curve (a curve between two sides that matches position, tangent or curvature at each end,
 * built with the same control-point solver, with the same highlighted result, control-point display and draggable
 * manipulators), with two improvements:
 *
 *  1. Pick just curves. A side may be edges, a whole wire, a vertex or a mate connector. When a side is edges or a wire the
 *     curve is joined to the END of that wire, and the ends are chosen as the pair that is nearest each other (a wire has
 *     two ends, so there are up to four pairs). The edges of a side must form one chain.
 *  2. The tangent always blends. At the end of a wire the tangent is the direction the wire is already heading as it leaves
 *     the end (outward along the wire), so the bridge continues the wire; it is never reversed into it. The std feature
 *     picks the direction from an end-to-end guess in its dialog only. A mate connector uses its Z axis, turned to point
 *     toward the other side. "Reverse" turns a tangent around on purpose.
 *
 * Curvature continuity uses the curvature of the wire's end edge (a line gives zero curvature, so it behaves as tangent).
 *
 * Edit control points: a magnitude (how far the bridge follows the tangent) and, for curvature, a curvature offset, per
 * side. Each has a draggable manipulator on the control polygon; dragging a magnitude through zero reverses the tangent.
 */

export enum BcpMatch
{
    annotation { "Name" : "Position (G0)" }
    POSITION,
    annotation { "Name" : "Tangent (G1)" }
    TANGENCY,
    annotation { "Name" : "Curvature (G2)" }
    CURVATURE
}

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Bridging curve +",
        "Feature Type Description" : "Draws a smooth curve between two sides, matching position, tangent or curvature like Bridging curve, but you only need to pick the curves or wires. It joins the nearest pair of wire ends and always blends: the tangent continues the way each wire is already heading, never back into it. Sides can also be vertices or mate connectors. Edit control points shows draggable manipulators for the magnitude and curvature of each end.",
        "Manipulator Change Function" : "bridgingCurvePlusManipulator",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const bridgingCurvePlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Start", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "Description" : "A curve or wire (the bridge joins one of its ends), a vertex or a mate connector. Edges must form one chain." }
        definition.startSide is Query;

        annotation { "Name" : "Match", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : BcpMatch.TANGENCY,
                    "Description" : "Position: the bridge only meets the end. Tangent: it leaves the end along the curve's direction. Curvature: it also matches the end's curvature." }
        definition.startMatch is BcpMatch;

        if (definition.startMatch != BcpMatch.POSITION)
        {
            annotation { "Name" : "Reverse tangent", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false,
                        "Description" : "Turn the start tangent around. The default always continues the curve; reversing makes a loop or cusp on purpose." }
            definition.startReverse is boolean;
        }

        annotation { "Name" : "End", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "Description" : "A curve or wire (the bridge joins one of its ends), a vertex or a mate connector. Edges must form one chain." }
        definition.endSide is Query;

        annotation { "Name" : "Match", "Column Name" : "Second match", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : BcpMatch.TANGENCY,
                    "Description" : "Position: the bridge only meets the end. Tangent: it arrives along the curve's direction. Curvature: it also matches the end's curvature." }
        definition.endMatch is BcpMatch;

        if (definition.endMatch != BcpMatch.POSITION)
        {
            annotation { "Name" : "Reverse tangent", "Column Name" : "Second reverse", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false,
                        "Description" : "Turn the end tangent around. The default always continues the curve; reversing makes a loop or cusp on purpose." }
            definition.endReverse is boolean;
        }

        if (definition.startMatch != BcpMatch.POSITION || definition.endMatch != BcpMatch.POSITION)
        {
            annotation { "Name" : "Edit control points", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Default" : false,
                        "Description" : "Show the control points and draggable manipulators for each end's magnitude (and curvature offset)." }
            definition.editControlPoints is boolean;

            if (definition.editControlPoints)
            {
                annotation { "Group Name" : "Edit control points", "Collapsed By Default" : false, "Driving Parameter" : "editControlPoints" }
                {
                    if (definition.startMatch != BcpMatch.POSITION)
                    {
                        annotation { "Name" : "Start magnitude", "Description" : "How far the bridge follows the start tangent before turning, relative to the default (1)." }
                        isReal(definition.startMagnitude, POSITIVE_REAL_BOUNDS);

                        if (definition.startMatch == BcpMatch.CURVATURE)
                        {
                            annotation { "Name" : "Start curvature offset", "Description" : "Moves the start's curvature control point along the tangent, relative to the default (1)." }
                            isReal(definition.startCurvatureOffset, CLAMP_MAGNITUDE_REAL_BOUNDS);
                        }
                    }
                    if (definition.endMatch != BcpMatch.POSITION)
                    {
                        annotation { "Name" : "End magnitude", "Description" : "How far the bridge follows the end tangent before turning, relative to the default (1)." }
                        isReal(definition.endMagnitude, POSITIVE_REAL_BOUNDS);

                        if (definition.endMatch == BcpMatch.CURVATURE)
                        {
                            annotation { "Name" : "End curvature offset", "Description" : "Moves the end's curvature control point along the tangent, relative to the default (1)." }
                            isReal(definition.endCurvatureOffset, CLAMP_MAGNITUDE_REAL_BOUNDS);
                        }
                    }
                }
            }
        }
    }
    {
        const solved = bcpSolve(context, definition, true);
        if (definition.editControlPoints == true && (definition.startMatch != BcpMatch.POSITION || definition.endMatch != BcpMatch.POSITION))
        {
            bcpAddManipulators(context, id, solved.controlPoints, 1, solved.start.degree, definition.startReverse == true);
            bcpAddManipulators(context, id, solved.controlPoints, 2, solved.end.degree, definition.endReverse == true);
            bcpShowControlPoints(context, id, solved.controlPoints);
        }

        opCreateBSplineCurve(context, id + "bridge", {
                    "bSplineCurve" : bSplineCurve({ "degree" : size(solved.controlPoints) - 1, "isPeriodic" : false, "controlPoints" : solved.controlPoints })
                });
        bcpShowWire(context, id, qCreatedBy(id, EntityType.BODY));

        reportFeatureInfo(context, id, "Bridged " ~ roundToPrecision(solved.distance / millimeter, 3) ~ " mm between the nearest ends"
            ~ " (degree " ~ (size(solved.controlPoints) - 1) ~ ").");
    }, { "startMatch" : BcpMatch.TANGENCY, "endMatch" : BcpMatch.TANGENCY, "startReverse" : false, "endReverse" : false, "editControlPoints" : false,
         "startMagnitude" : 1, "endMagnitude" : 1, "startCurvatureOffset" : 1, "endCurvatureOffset" : 1 });

// ---------------------------------------------------------------------------------------------------------------------
// Solving
// ---------------------------------------------------------------------------------------------------------------------

/**
 * Pick the nearest pair of ends, build the two sides' data and solve for the control points.
 * `useScales` false leaves the magnitudes and curvature offsets at their defaults (1): the manipulators are measured
 * against those defaults.
 * @returns {{ @field start {BridgingSideData} @field end {BridgingSideData} @field controlPoints {array} @field distance {ValueWithUnits} }}
 */
function bcpSolve(context is Context, definition is map, useScales is boolean) returns map
{
    const starts = bcpEnds(context, definition.startSide, "startSide");
    const ends = bcpEnds(context, definition.endSide, "endSide");

    // The nearest pair of ends.
    var start = starts[0];
    var end = ends[0];
    var best = norm(start.point - end.point);
    for (var a in starts)
    {
        for (var b in ends)
        {
            const d = norm(a.point - b.point);
            if (d < best)
            {
                best = d;
                start = a;
                end = b;
            }
        }
    }
    if (best < TOLERANCE.zeroLength * meter)
    {
        throw regenError("The two ends are at the same point, so there is nothing to bridge");
    }

    // A mate connector's Z axis has no inherent sense: point it toward the other side, so the bridge blends.
    if (start.mateConnector)
    {
        start.tangent = dot(start.tangent, end.point - start.point) < 0 * meter ? -start.tangent : start.tangent;
    }
    if (end.mateConnector)
    {
        end.tangent = dot(end.tangent, start.point - end.point) < 0 * meter ? -end.tangent : end.tangent;
    }

    const side1 = bcpSideData(start, definition.startMatch, definition.startReverse, useScales ? definition.startMagnitude : undefined,
            useScales ? definition.startCurvatureOffset : undefined, "startMagnitude", "Start");
    const side2 = bcpSideData(end, definition.endMatch, definition.endReverse, useScales ? definition.endMagnitude : undefined,
            useScales ? definition.endCurvatureOffset : undefined, "endMagnitude", "End");
    return { "start" : side1, "end" : side2, "controlPoints" : computeBridgingControlPoints(context, side1, side2), "distance" : best };
}

/**
 * The ends a side offers, each as { point, tangent (outward continuation, or undefined), frame (curvature there, or
 * undefined), mateConnector }. A wire offers its two ends; a vertex or mate connector offers itself.
 */
function bcpEnds(context is Context, side is Query, field is string) returns array
{
    if (isQueryEmpty(context, side))
    {
        throw regenError("Select the curves, vertex or mate connector for this side", [field]);
    }
    const mc = mcMateConnector(context, side);
    if (!isQueryEmpty(context, mc))
    {
        const cs = evMateConnector(context, { "mateConnector" : qNthElement(mc, 0) });
        return [{ "point" : cs.origin, "tangent" : cs.zAxis, "frame" : undefined, "mateConnector" : true }];
    }
    const vertices = qEntityFilter(side, EntityType.VERTEX);
    if (!isQueryEmpty(context, vertices) && isQueryEmpty(context, mcEdgesOf(context, side)))
    {
        return [{ "point" : evVertexPoint(context, { "vertex" : qNthElement(vertices, 0) }), "tangent" : undefined, "frame" : undefined, "mateConnector" : false }];
    }

    const path = mcPath(context, mcEdgesOf(context, side), field);
    if (path.closed)
    {
        throw regenError("A closed curve has no ends to bridge from", [field]);
    }
    const lines = evPathTangentLines(context, path, [0, 1]).tangentLines;
    const lastIndex = size(path.edges) - 1;
    // The parameter of the path's first and last point on their own edges (an edge may run against the path).
    const startFrame = evEdgeCurvature(context, { "edge" : path.edges[0], "parameter" : path.flipped[0] ? 1 : 0 });
    const endFrame = evEdgeCurvature(context, { "edge" : path.edges[lastIndex], "parameter" : path.flipped[lastIndex] ? 0 : 1 });
    return [
            { "point" : lines[0].origin, "tangent" : -normalize(lines[0].direction), "frame" : startFrame, "mateConnector" : false },
            { "point" : lines[1].origin, "tangent" : normalize(lines[1].direction), "frame" : endFrame, "mateConnector" : false }
        ];
}

/** The solver's description of one side. */
function bcpSideData(end is map, match is BcpMatch, reverse, magnitude, curvatureOffset, scaleName is string, label is string) returns BridgingSideData
{
    var data = { "position" : end.point, "speedScaleName" : scaleName };
    data.degree = match == BcpMatch.POSITION ? 0 : (match == BcpMatch.TANGENCY ? 1 : 2);
    if (data.degree >= 1)
    {
        if (end.tangent == undefined)
        {
            throw regenError(label ~ ": a vertex has no tangent. Use Position, or pick a curve or mate connector.");
        }
        data.tangent = reverse == true ? -end.tangent : end.tangent;
        data.speedScale = magnitude == undefined ? 1 : magnitude;
    }
    if (data.degree >= 2)
    {
        if (end.frame == undefined)
        {
            throw regenError(label ~ ": curvature needs a curve. A mate connector or vertex has none, so use Tangent or Position.");
        }
        data.curvature = end.frame.curvature;
        data.curvatureDirection = curvatureFrameNormal(end.frame);
        data.curvatureOffsetScale = curvatureOffset == undefined ? 1 : curvatureOffset;
    }
    return data as BridgingSideData;
}

// ---------------------------------------------------------------------------------------------------------------------
// Manipulators and visualization (the same as Bridging curve)
// ---------------------------------------------------------------------------------------------------------------------

function bcpMagnitudeKey(side is number) returns string
{
    return (side == 1 ? "Start" : "End") ~ " magnitude";
}

function bcpCurvatureKey(side is number) returns string
{
    return (side == 1 ? "Start" : "End") ~ " curvature offset";
}

/** The control point of the curvature manipulator with zero offset: the third point's projection along the tangent. */
function bcpCurvatureBase(direction is Vector, controlPoints is array) returns Vector
{
    return controlPoints[2] - direction * dot(controlPoints[2] - controlPoints[1], direction);
}

function bcpAddManipulators(context is Context, id is Id, controlPoints is array, side is number, degree is number, flip is boolean)
{
    if (degree == 0)
    {
        return;
    }
    const points = side == 2 ? reverse(controlPoints) : controlPoints;
    const direction = normalize(points[1] - points[0]);
    const sign = flip ? -1 : 1;
    addManipulators(context, id, {
                (bcpMagnitudeKey(side)) : linearManipulator({
                            "base" : points[0],
                            "direction" : direction * sign,
                            "offset" : norm(points[1] - points[0]) * sign,
                            "primaryParameterId" : (side == 1 ? "startMagnitude" : "endMagnitude")
                        })
            });
    if (degree == 1)
    {
        return;
    }
    const base = bcpCurvatureBase(direction, points);
    addManipulators(context, id, {
                (bcpCurvatureKey(side)) : linearManipulator({
                            "base" : base,
                            "direction" : direction,
                            "offset" : dot(direction, points[2] - base),
                            "style" : ManipulatorStyleEnum.SECONDARY,
                            "primaryParameterId" : (side == 1 ? "startCurvatureOffset" : "endCurvatureOffset")
                        })
            });
}

/**
 * @internal
 * The manipulator change function of Bridging curve +: turns a drag into a magnitude (a drag through zero reverses the
 * tangent) or a curvature offset.
 */
export function bridgingCurvePlusManipulator(context is Context, definition is map, newManipulators is map) returns map
{
    // A curvature offset is measured with the magnitudes as they are; a magnitude is measured against the default one.
    const curvatureMoved = newManipulators[bcpCurvatureKey(1)] is map || newManipulators[bcpCurvatureKey(2)] is map;
    var controlPoints = bcpSolve(context, definition, curvatureMoved).controlPoints;
    for (var side in [1, 2])
    {
        if (side == 2)
        {
            controlPoints = reverse(controlPoints);
        }
        const name = side == 1 ? "start" : "end";

        const magnitude = newManipulators[bcpMagnitudeKey(side)];
        if (magnitude is map)
        {
            definition[name ~ "Magnitude"] = abs(magnitude.offset) / norm(controlPoints[1] - controlPoints[0]);
            definition[name ~ "Reverse"] = magnitude.offset < 0 * meter;
        }
        const curvature = newManipulators[bcpCurvatureKey(side)];
        if (curvature is map)
        {
            const direction = normalize(controlPoints[1] - controlPoints[0]);
            definition[name ~ "CurvatureOffset"] = curvature.offset / dot(direction, controlPoints[2] - bcpCurvatureBase(direction, controlPoints));
        }
    }
    return definition;
}

/** Show the control polygon (magenta points and lines) while the dialog is open. */
function bcpShowControlPoints(context is Context, id is Id, controlPoints is array)
{
    if (!isTopLevelId(id))
    {
        return;
    }
    const controlId = id + "controlPoints";
    startFeature(context, controlId, {});
    try
    {
        opPoint(context, controlId + 0 + "point", { "point" : controlPoints[0] });
        for (var i = 1; i < size(controlPoints); i += 1)
        {
            opPoint(context, controlId + i + "point", { "point" : controlPoints[i] });
            opFitSpline(context, controlId + i + "line", { "points" : [controlPoints[i - 1], controlPoints[i]] });
        }
        const edges = qCreatedBy(controlId, EntityType.EDGE);
        const vertices = qCreatedBy(controlId, EntityType.VERTEX)->qBodyType(BodyType.POINT);
        addDebugEntities(context, qUnion([vertices, edges]), DebugColor.MAGENTA);
    }
    abortFeature(context, controlId);
}

/** Highlight the result curve in magenta while the dialog is open. */
function bcpShowWire(context is Context, id is Id, wire is Query)
{
    if (!isTopLevelId(id))
    {
        return;
    }
    const showId = id + "showWire";
    startFeature(context, showId, {});
    try
    {
        addDebugEntities(context, wire, DebugColor.MAGENTA);
    }
    abortFeature(context, showId);
}
