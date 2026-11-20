FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/path.fs", version : "3095.0");
export import(path : "onshape/std/bridgingCurve.fs", version : "3095.0");
// IMPORT: mc_core.fs (same document: references, paths, arc length)
import(path : "0a18eeece6adacc759621422", version : "2095707b9424d45d3bf94814");
// IMPORT: bridging_curve_plus_icon.svg (feature icon)
IconNamespace::import(path : "9a0cb4233794456e403f1496", version : "014d2f7de0958ebca210756e");

/**
 * Bridging curve +.
 *
 * Onshape's Bridging curve, unchanged in what you can pick (vertices, mate connectors, interior positions on edges, faces as
 * tangency references, tangency angles, G0 to G3, magnitudes and offsets), plus a "Choose ends" option for sides that are
 * open curves or wires:
 *
 *  - As selected: exactly the standard Bridging curve.
 *  - Nearest pair of wire ends: a side may be edges or a whole wire; the bridge joins the pair of open ends (one per side)
 *    that are nearest each other.
 *  - Ends nearest an attractor point: the bridge joins, on each open side, the end nearest the attractor point (a vertex,
 *    sketch point or mate connector). Put the point near the two ends you want bridged.
 *
 * In the last two modes the feature picks the end vertex and passes the end edge and vertex to the standard Bridging curve,
 * so the curve is built by the same solver. It also points each tangent OUT of its wire (the bridge continues the wire, whichever
 * way the wire was drawn); "Opposite direction" reverses that on purpose. A mate connector's Z axis is turned toward the other
 * side. A side that is a vertex or mate connector is used as is. Interior edge positions are not offered in these two modes.
 */

export enum BcpEndChoice
{
    annotation { "Name" : "As selected (standard)" }
    STANDARD,
    annotation { "Name" : "Nearest pair of wire ends" }
    NEAREST_PAIR,
    annotation { "Name" : "Ends nearest an attractor point" }
    ATTRACTOR
}

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Bridging curve +",
        "Feature Type Description" : "Bridging curve with a choice of which wire ends to bridge. As selected works exactly like Bridging curve (vertices, mate connectors, interior edge positions, faces for tangency, angles, G0 to G3). Nearest pair of wire ends and Ends nearest an attractor point let you pick whole curves or wires and choose the open ends by distance, and always make the bridge continue each wire outward.",
        "Editing Logic Function" : "bridgingCurvePlusEditingLogic",
        "Manipulator Change Function" : "bridgingCurvePlusManipulator",
        "UIHint" : UIHint.NO_PREVIEW_PROVIDED }
export const bridgingCurvePlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Choose ends", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : BcpEndChoice.STANDARD,
                    "Description" : "As selected: like Bridging curve. Nearest pair: for curves or wires, join the nearest open ends. Attractor: on each curve or wire, join the end nearest the attractor point." }
        definition.endChoice is BcpEndChoice;

        if (definition.endChoice == BcpEndChoice.ATTRACTOR)
        {
            annotation { "Name" : "Attractor point", "Description" : "A vertex, sketch point or mate connector. Each open curve or wire is joined at the end nearest this point.",
                        "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.attractor is Query;
        }

        annotation { "Name" : "Preselection", "UIHint" : UIHint.ALWAYS_HIDDEN, "Filter" : EntityType.EDGE || EntityType.VERTEX || (EntityType.FACE && ConstructionObject.NO) || BodyType.MATE_CONNECTOR }
        definition.preselectedEntities is Query;

        annotation { "Name" : "Start", "Filter" : EntityType.EDGE || EntityType.VERTEX || (EntityType.FACE && ConstructionObject.NO) || BodyType.MATE_CONNECTOR || (EntityType.BODY && BodyType.WIRE),
                    "Description" : "Vertex, edge, face or mate connector. With Choose ends set to nearest pair or attractor: the curves or wire (edges must form one chain)." }
        definition.side1 is Query;

        annotation { "Name" : "Match", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Default" : BridgingCurveMatchType.TANGENCY }
        definition.match1 is BridgingCurveMatchType;

        if (definition.match1 != BridgingCurveMatchType.POSITION)
        {
            annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flip1 is boolean;
        }

        annotation { "Name" : "End", "Filter" : EntityType.EDGE || EntityType.VERTEX || (EntityType.FACE && ConstructionObject.NO) || BodyType.MATE_CONNECTOR || (EntityType.BODY && BodyType.WIRE),
                    "Description" : "Vertex, edge, face or mate connector. With Choose ends set to nearest pair or attractor: the curves or wire (edges must form one chain)." }
        definition.side2 is Query;

        annotation { "Name" : "Match", "Column Name" : "Second match", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE, "Default" : BridgingCurveMatchType.TANGENCY }
        definition.match2 is BridgingCurveMatchType;

        if (definition.match2 != BridgingCurveMatchType.POSITION)
        {
            annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flip2 is boolean;
        }

        if (definition.match1 != BridgingCurveMatchType.POSITION || definition.match2 != BridgingCurveMatchType.POSITION)
        {
            if (definition.match1 != BridgingCurveMatchType.G3 && definition.match2 != BridgingCurveMatchType.G3)
            {
                annotation { "Name" : "Method", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : BridgingCurveMethod.CONTROL_POINTS }
                definition.method is BridgingCurveMethod;
            }
            if (methodIsControlPointsPlus(definition))
            {
                annotation { "Name" : "Edit control points", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
                definition.editControlPoints is boolean;
                if (definition.editControlPoints)
                {
                    annotation { "Group Name" : "Edit control points", "Collapsed By Default" : false, "Driving Parameter" : "editControlPoints" }
                    {
                        if (definition.match1 != BridgingCurveMatchType.POSITION)
                        {
                            annotation { "Name" : "Start magnitude" }
                            isReal(definition.side1Magnitude, POSITIVE_REAL_BOUNDS);
                            if (definition.match1 == BridgingCurveMatchType.CURVATURE || definition.match1 == BridgingCurveMatchType.G3)
                            {
                                annotation { "Name" : "Start curvature offset" }
                                isReal(definition.side1CurvatureOffset, CLAMP_MAGNITUDE_REAL_BOUNDS);
                            }
                            if (definition.match1 == BridgingCurveMatchType.G3)
                            {
                                annotation { "Name" : "Start flow offset" }
                                isReal(definition.side1G3Offset, CLAMP_MAGNITUDE_REAL_BOUNDS);
                            }
                        }
                        if (definition.match2 != BridgingCurveMatchType.POSITION)
                        {
                            annotation { "Name" : "End magnitude" }
                            isReal(definition.side2Magnitude, POSITIVE_REAL_BOUNDS);
                            if (definition.match2 == BridgingCurveMatchType.CURVATURE || definition.match2 == BridgingCurveMatchType.G3)
                            {
                                annotation { "Name" : "End curvature offset" }
                                isReal(definition.side2CurvatureOffset, CLAMP_MAGNITUDE_REAL_BOUNDS);
                            }
                            if (definition.match2 == BridgingCurveMatchType.G3)
                            {
                                annotation { "Name" : "End flow offset" }
                                isReal(definition.side2G3Offset, CLAMP_MAGNITUDE_REAL_BOUNDS);
                            }
                        }
                    }
                }
            }
            else
            {
                annotation { "Name" : "Magnitude" }
                isReal(definition.magnitude, POSITIVE_REAL_BOUNDS);

                if (definition.match1 != BridgingCurveMatchType.POSITION && definition.match2 != BridgingCurveMatchType.POSITION)
                {
                    annotation { "Name" : "Bias" }
                    isReal(definition.bias, BIAS_BOUNDS);
                }
            }
        }

        annotation { "Name" : "side1HasVertex", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.side1HasVertex is boolean;
        annotation { "Name" : "side2HasVertex", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.side2HasVertex is boolean;

        if (definition.endChoice == BcpEndChoice.STANDARD && (!definition.side1HasVertex || !definition.side2HasVertex))
        {
            annotation { "Name" : "Edit edge positions", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            definition.editEdgePositions is boolean;
            if (definition.editEdgePositions)
            {
                annotation { "Group Name" : "Edge positions", "Collapsed By Default" : false, "Driving Parameter" : "editEdgePositions" }
                {
                    if (!definition.side1HasVertex)
                    {
                        annotation { "Name" : "Start edge position" }
                        isReal(definition.startEdgeParameter, EDGE_PARAMETER_BOUNDS);
                    }
                    if (!definition.side2HasVertex)
                    {
                        annotation { "Name" : "End edge position" }
                        isReal(definition.endEdgeParameter, EDGE_PARAMETER_BOUNDS);
                    }
                }
            }
        }

        annotation { "Name" : "side1HasFace", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.side1HasFace is boolean;
        annotation { "Name" : "side2HasFace", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.side2HasFace is boolean;

        if ((definition.side1HasFace || definition.side2HasFace) && (definition.match1 != BridgingCurveMatchType.POSITION || definition.match2 != BridgingCurveMatchType.POSITION))
        {
            annotation { "Name" : "Edit tangency angles", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
            definition.editTangencyAngles is boolean;
            if (definition.editTangencyAngles)
            {
                annotation { "Group Name" : "Edit angles", "Collapsed By Default" : false, "Driving Parameter" : "editTangencyAngles" }
                {
                    if (definition.match1 != BridgingCurveMatchType.POSITION && definition.side1HasFace)
                    {
                        annotation { "Name" : "Start angle" }
                        isAngle(definition.startAngle, ANGLE_180_MINUS_180_BOUNDS);
                    }
                    if (definition.match2 != BridgingCurveMatchType.POSITION && definition.side2HasFace)
                    {
                        annotation { "Name" : "End angle" }
                        isAngle(definition.endAngle, ANGLE_180_MINUS_180_BOUNDS);
                    }
                }
            }
        }
    }
    {
        var passed = definition;
        if (definition.endChoice != BcpEndChoice.STANDARD)
        {
            passed = bcpResolve(context, definition).definition;
        }

        // The standard feature builds the curve (and shows its own errors on side1 / side2 / the matching parameters).
        bridgingCurve(context, id + "bridge", passed);
        bcpShowWire(context, id, qCreatedBy(id + "bridge", EntityType.BODY));
    }, {
            "endChoice" : BcpEndChoice.STANDARD,
            "match1" : BridgingCurveMatchType.TANGENCY,
            "match2" : BridgingCurveMatchType.TANGENCY,
            "flip1" : false,
            "flip2" : false,
            "method" : BridgingCurveMethod.CONTROL_POINTS,
            "preselectedEntities" : qNothing(),
            "side1HasVertex" : false,
            "side2HasVertex" : false,
            "side1HasFace" : true,
            "side2HasFace" : true,
            "editEdgePositions" : false,
            "editTangencyAngles" : false
        });

/** Same rule as std's methodIsControlPoints (which is not exported). */
predicate methodIsControlPointsPlus(definition is map)
{
    definition.method == BridgingCurveMethod.CONTROL_POINTS
        || definition.match1 == BridgingCurveMatchType.G3
        || definition.match2 == BridgingCurveMatchType.G3;
}

// ---------------------------------------------------------------------------------------------------------------------
// Choosing the ends
// ---------------------------------------------------------------------------------------------------------------------

/**
 * What a side offers: { kind : "wire" | "vertex" | "mate connector", ends : array, faces : Query }.
 * Each end is { point, and for a wire: outward (the direction leaving the wire), edge (the end edge), vertex (its end vertex);
 * for a mate connector: zAxis }. A vertex or mate connector offers just itself; a wire offers its two ends.
 */
function bcpSideInfo(context is Context, side is Query, field is string) returns map
{
    if (isQueryEmpty(context, side))
    {
        throw regenError("Select the curves, vertex or mate connector for this side", [field]);
    }
    const mc = mcMateConnector(context, side);
    if (!isQueryEmpty(context, mc))
    {
        const cs = evMateConnector(context, { "mateConnector" : qNthElement(mc, 0) });
        return { "kind" : "mate connector", "ends" : [{ "point" : cs.origin, "zAxis" : cs.zAxis }], "faces" : qNothing() };
    }
    const vertices = qEntityFilter(side, EntityType.VERTEX);
    if (!isQueryEmpty(context, vertices))
    {
        return { "kind" : "vertex", "ends" : [{ "point" : evVertexPoint(context, { "vertex" : qNthElement(vertices, 0) }) }], "faces" : qNothing() };
    }
    const edges = mcEdgesOf(context, side);
    if (isQueryEmpty(context, edges))
    {
        throw regenError("Select curves, a wire, a vertex or a mate connector for this side", [field]);
    }
    const path = mcPath(context, edges, field);
    if (path.closed)
    {
        throw regenError("A closed curve has no ends to bridge from", [field]);
    }
    const lines = evPathTangentLines(context, path, [0, 1]).tangentLines;
    const lastIndex = size(path.edges) - 1;
    return { "kind" : "wire", "faces" : qEntityFilter(side, EntityType.FACE),
            "ends" : [
                    bcpWireEnd(path.edges[0], lines[0].origin, -normalize(lines[0].direction)),
                    bcpWireEnd(path.edges[lastIndex], lines[1].origin, normalize(lines[1].direction))
                ] };
}

function bcpWireEnd(edge is Query, point is Vector, outward is Vector) returns map
{
    return { "point" : point, "outward" : outward, "edge" : edge, "vertex" : qClosestTo(qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX), point) };
}

/** The end of `ends` nearest `target`. */
function bcpNearestEnd(ends is array, target is Vector) returns map
{
    var best = ends[0];
    var bestDistance = norm(best.point - target);
    for (var candidate in ends)
    {
        const d = norm(candidate.point - target);
        if (d < bestDistance)
        {
            best = candidate;
            bestDistance = d;
        }
    }
    return best;
}

/**
 * Turn the sides into what the standard Bridging curve wants: for each open wire side the chosen end edge and end vertex
 * (plus any face picked for tangency), with "Opposite direction" adjusted so the tangent leaves the wire.
 * @returns {{ @field definition {map} }}
 */
function bcpResolve(context is Context, definition is map) returns map
{
    const info = [bcpSideInfo(context, definition.side1, "side1"), bcpSideInfo(context, definition.side2, "side2")];

    var picked = [info[0].ends[0], info[1].ends[0]];
    if (definition.endChoice == BcpEndChoice.ATTRACTOR)
    {
        if (isQueryEmpty(context, definition.attractor))
        {
            throw regenError("Select the attractor point", ["attractor"]);
        }
        const target = mcPointOf(context, definition.attractor, "attractor");
        picked = [bcpNearestEnd(info[0].ends, target), bcpNearestEnd(info[1].ends, target)];
    }
    else
    {
        var bestDistance = undefined;
        for (var a in info[0].ends)
        {
            for (var b in info[1].ends)
            {
                const d = norm(a.point - b.point);
                if (bestDistance == undefined || d < bestDistance)
                {
                    bestDistance = d;
                    picked = [a, b];
                }
            }
        }
    }

    var out = definition;
    out.editEdgePositions = false;
    for (var i = 0; i < 2; i += 1)
    {
        const n = i + 1;
        const here = picked[i];
        const there = picked[1 - i];
        const faces = info[i].kind == "wire" ? info[i].faces : qEntityFilter(definition["side" ~ n], EntityType.FACE);
        var autoFlip = false;
        if (definition["match" ~ n] != BridgingCurveMatchType.POSITION)
        {
            if (info[i].kind == "wire" && isQueryEmpty(context, faces))
            {
                // std's tangent follows the edge's own direction; reverse it when that points into the wire.
                const parameter = evDistance(context, { "side0" : here.edge, "side1" : here.point }).sides[0].parameter;
                const native = evEdgeTangentLine(context, { "edge" : here.edge, "parameter" : parameter }).direction;
                autoFlip = dot(native, here.outward) < 0;
            }
            else if (info[i].kind == "mate connector")
            {
                autoFlip = dot(here.zAxis, there.point - here.point) < 0 * meter;
            }
        }
        if (info[i].kind == "wire")
        {
            out["side" ~ n] = qUnion([here.edge, here.vertex, faces]);
        }
        out["flip" ~ n] = definition["flip" ~ n] != autoFlip;
        out["side" ~ n ~ "HasVertex"] = true;
        out["side" ~ n ~ "HasFace"] = !isQueryEmpty(context, faces);
    }
    return { "definition" : out };
}

// ---------------------------------------------------------------------------------------------------------------------
// Editing logic and manipulators: the standard ones, on the resolved sides
// ---------------------------------------------------------------------------------------------------------------------

/**
 * @internal
 * The editing logic of Bridging curve +: the standard Bridging curve's.
 */
export function bridgingCurvePlusEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    return bridgingCurveEditingLogic(context, id, oldDefinition, definition, isCreating, specifiedParameters, hiddenBodies);
}

/**
 * @internal
 * The manipulator change function of Bridging curve +: runs the standard one on the resolved sides and copies the
 * manipulated values back (the automatic flip is taken out of "Opposite direction" again).
 */
export function bridgingCurvePlusManipulator(context is Context, definition is map, newManipulators is map) returns map
{
    var resolved = definition;
    if (definition.endChoice != BcpEndChoice.STANDARD)
    {
        resolved = bcpResolve(context, definition).definition;
    }
    const changed = bridgingCurveManipulator(context, resolved, newManipulators);
    for (var key in ["side1Magnitude", "side2Magnitude", "side1CurvatureOffset", "side2CurvatureOffset", "side1G3Offset", "side2G3Offset",
            "startEdgeParameter", "endEdgeParameter", "startAngle", "endAngle"])
    {
        if (changed[key] != undefined)
        {
            definition[key] = changed[key];
        }
    }
    for (var n in [1, 2])
    {
        const key = "flip" ~ n;
        if (changed[key] != resolved[key])
        {
            // the manipulator reversed the direction: reverse the user's choice too
            definition[key] = !(definition[key] == true);
        }
    }
    return definition;
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
