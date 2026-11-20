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
 * built with the same control-point solver), with two improvements:
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
        "Feature Type Description" : "Draws a smooth curve between two sides, matching position, tangent or curvature like Bridging curve, but you only need to pick the curves or wires. It joins the nearest pair of wire ends and always blends: the tangent continues the way each wire is already heading, never back into it. Sides can also be vertices or mate connectors." }
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

            annotation { "Name" : "Start magnitude", "Description" : "How far the bridge follows the start tangent before turning, relative to the default (1)." }
            isReal(definition.startMagnitude, POSITIVE_REAL_BOUNDS);
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

            annotation { "Name" : "End magnitude", "Description" : "How far the bridge follows the end tangent before turning, relative to the default (1)." }
            isReal(definition.endMagnitude, POSITIVE_REAL_BOUNDS);
        }
    }
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

        const side1 = bcpSideData(start, definition.startMatch, definition.startReverse, definition.startMagnitude, "startMagnitude", "Start");
        const side2 = bcpSideData(end, definition.endMatch, definition.endReverse, definition.endMagnitude, "endMagnitude", "End");
        const controlPoints = computeBridgingControlPoints(context, side1, side2);
        opCreateBSplineCurve(context, id + "bridge", {
                    "bSplineCurve" : bSplineCurve({ "degree" : size(controlPoints) - 1, "isPeriodic" : false, "controlPoints" : controlPoints })
                });

        reportFeatureInfo(context, id, "Bridged " ~ roundToPrecision(best / millimeter, 3) ~ " mm between the nearest ends"
            ~ " (degree " ~ (size(controlPoints) - 1) ~ ").");
    });

// ---------------------------------------------------------------------------------------------------------------------
// Sides
// ---------------------------------------------------------------------------------------------------------------------

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
function bcpSideData(end is map, match is BcpMatch, reverse, magnitude, scaleName is string, label is string) returns BridgingSideData
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
    }
    return data as BridgingSideData;
}
