FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

/**
 * Split+: the standard part split, with several targets and several tools, and -- when one
 * side is kept -- the side named by a reference instead of by a flip.
 *
 * Every target is split by every tool in turn (opSplitPart, one call per tool, the pieces of
 * one split being the targets of the next). With "Keep both sides" that is the built-in
 * split repeated. With it off, each tool keeps the side of itself the REFERENCE is on
 * ("Keep side nearest reference" on) or the other side (off): what survives is the region
 * on the reference's side of every tool, whatever the tools' orientations.
 *
 * The side is read per tool as the reference's signed distance to that tool (positive on
 * the side its normal points to), and opSplitPart's KEEP_FRONT keeps exactly that side
 * (verified 2026-09-23: a +Z plane tool keeps z > 0 with KEEP_FRONT). Without a reference
 * the box is the built-in's own: on keeps the front of every tool, off the back.
 *
 * Tools: surfaces, faces (construction planes included) and mate connectors (their XY
 * plane). A multi-face surface splits by its faces as they are; "Trim to face boundaries"
 * applies to single faces, as in the built-in.
 *
 * Publishes (Extract variables): output (all resulting pieces), splitFaces (faces the
 * splits created -- the caps on solids), splitEdges (edges they created -- the cut on
 * surfaces), pieceCount.
 */
annotation { "Feature Type Name" : "Split+",
        "Feature Type Description" : "Split parts, surfaces or curves with several tools; when one side is kept, the side is the one a reference is on, whatever the tools' orientations.",
        "Filter Selector" : "allparts" }
export const splitPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Parts, surfaces, or curves to split",
                    "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE) && ModifiableEntityOnly.YES && SketchObject.NO }
        definition.targets is Query;

        annotation { "Name" : "Entities to split with",
                    "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR,
                    "Description" : "Surfaces, faces, planes or mate connectors. Each splits everything the ones before it left, in the order picked." }
        definition.tools is Query;

        annotation { "Name" : "Keep tools", "Default" : false }
        definition.keepTools is boolean;

        annotation { "Name" : "Trim to face boundaries", "Default" : false }
        definition.useTrimmed is boolean;

        annotation { "Name" : "Keep both sides", "Default" : true }
        definition.keepBothSides is boolean;

        if (!definition.keepBothSides)
        {
            annotation { "Name" : "Keep side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "Geometry on the side to keep of every tool. Leave empty for the built-in front / back choice." }
            definition.keepReference is Query;

            annotation { "Name" : "Keep side nearest reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                        "Description" : "On: keep the side of each tool the reference is on. Off: keep the other side. Without a reference: on keeps each tool's front (the side its normal points to), off its back." }
            definition.keepNear is boolean;
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print sides", "Default" : false, "Description" : "Which side of each tool the reference is on, and what was kept." }
            definition.debugPrintSides is boolean;
        }
    }
    {
        verifyNonemptyQuery(context, definition, "targets", ErrorStringEnum.SPLIT_SELECT_TARGETS);
        verifyNonemptyQuery(context, definition, "tools", ErrorStringEnum.SPLIT_SELECT_TOOL);
        if (!isQueryEmpty(context, qIntersection([definition.targets, qOwnerBody(definition.tools)])))
        {
            throw regenError("A tool is also a target; pick it only once.", ["tools"]);
        }

        const probe = definition.keepBothSides ? undefined : referenceProbe(context, definition.keepReference);
        const tools = evaluateQuery(context, definition.tools);

        var tempPlanes = [];
        var resolvedTools = [];
        var pieces = definition.targets;
        for (var i = 0; i < size(tools); i += 1)
        {
            var tool = tools[i];
            const cSys = mateConnectorFrame(context, tool);
            if (cSys != undefined)
            {
                const planeId = id + ("plane" ~ i);
                opPlane(context, planeId, { "plane" : plane(cSys) });
                tool = qCreatedBy(planeId, EntityType.FACE);
                tempPlanes = append(tempPlanes, qCreatedBy(planeId, EntityType.BODY));
            }

            resolvedTools = append(resolvedTools, tool);
            const keepType = keepTypeFor(context, definition, probe, tool, i);
            const splitId = id + ("split" ~ i);
            opSplitPart(context, splitId, {
                        "targets" : pieces,
                        "tool" : tool,
                        "keepTools" : true,
                        "useTrimmed" : definition.useTrimmed,
                        "keepType" : keepType
                    });
            pieces = qUnion([pieces, qCreatedBy(splitId, EntityType.BODY)]);
        }

        // One edge per cut, read while the temporary planes still exist.
        const splitEdges = oneEdgePerCut(context, qIntersection([qOwnedByBody(qUnion(evaluateQuery(context, pieces)), EntityType.EDGE),
                        qCreatedBy(id, EntityType.EDGE)]), resolvedTools);

        if (size(tempPlanes) > 0)
        {
            opDeleteBodies(context, id + "deletePlanes", { "entities" : qUnion(tempPlanes) });
        }
        if (!definition.keepTools)
        {
            const toolBodies = qBodyType(qEntityFilter(definition.tools, EntityType.BODY), BodyType.SHEET);
            if (!isQueryEmpty(context, toolBodies))
            {
                opDeleteBodies(context, id + "deleteTools", { "entities" : toolBodies });
            }
        }

        const kept = qUnion(evaluateQuery(context, pieces));
        if (isQueryEmpty(context, kept))
        {
            throw regenError("Nothing is left: no part of the targets lies on the kept side of every tool.", ["keepReference"]);
        }

        embedStandardOutputs(context, id, {
                    "output" : kept,
                    "outputDescription" : "The split pieces",
                    "inputs" : qUnion([definition.targets, definition.tools]),
                    "variables" : {
                        "pieceCount" : extractableVariable(size(evaluateQuery(context, kept)), "Pieces the split left.")
                    },
                    "queries" : {
                        "splitFaces" : extractableQuery(qIntersection([qOwnedByBody(kept, EntityType.FACE), qCreatedBy(id, EntityType.FACE)]),
                                "Faces the splits created (the caps on solids).", DebugColor.MAGENTA),
                        "splitEdges" : extractableQuery(splitEdges,
                                "The cuts: one edge per cut, the one on the piece in front of its tool (the side the tool's normal points to).",
                                DebugColor.MAGENTA)
                    }
                });
    }, {
        "keepTools" : false,
        "useTrimmed" : false,
        "keepBothSides" : true,
        "keepReference" : qNothing(),
        "keepNear" : true,
        "debugPrintSides" : false
    });

/**
 * The cut edges with each coincident pair reduced to one. Keeping both sides leaves every cut
 * as two edges on top of each other, one per piece, and anything built on both -- an
 * extrude, a fillet -- fails on the overlap. Of a pair, the edge kept is the one on the
 * piece in front of the tool that made it (the side the tool's normal points to).
 */
function oneEdgePerCut(context is Context, edges is Query, tools is array) returns Query
{
    const all = evaluateQuery(context, edges);
    var middles = [];
    var lengths = [];
    for (var e in all)
    {
        middles = append(middles, evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 }).origin);
        lengths = append(lengths, evLength(context, { "entities" : e }));
    }

    var used = makeArray(size(all), false);
    var result = [];
    for (var i = 0; i < size(all); i += 1)
    {
        if (used[i])
        {
            continue;
        }
        var group = [i];
        for (var j = i + 1; j < size(all); j += 1)
        {
            if (!used[j] && abs(lengths[i] - lengths[j]) < REFERENCE_SIDE_MARGIN && norm(middles[i] - middles[j]) < REFERENCE_SIDE_MARGIN)
            {
                group = append(group, j);
                used[j] = true;
            }
        }
        if (size(group) == 1)
        {
            result = append(result, all[i]);
            continue;
        }

        const tool = nearestTool(context, middles[i], tools);
        var chosen = all[group[0]];
        for (var g in group)
        {
            if (edgeSide(context, all[g], tool) > 0 * meter)
            {
                chosen = all[g];
                break;
            }
        }
        result = append(result, chosen);
    }
    return qUnion(result);
}

/** The tool nearest a point. */
function nearestTool(context is Context, point is Vector, tools is array) returns Query
{
    var best = tools[0];
    var bestDistance = inf * meter;
    for (var tool in tools)
    {
        const d = evDistance(context, { "side0" : point, "side1" : tool }).distance;
        if (d < bestDistance)
        {
            bestDistance = d;
            best = tool;
        }
    }
    return best;
}

/**
 * Signed distance to the tool of the piece an edge bounds, read on its adjacent faces (a
 * point on each face; the largest reading wins, since a cap face lies on the tool itself).
 */
function edgeSide(context is Context, edge is Query, tool is Query) returns ValueWithUnits
{
    var best = 0 * meter;
    for (var face in evaluateQuery(context, qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE)))
    {
        const onFace = evDistance(context, { "side0" : evApproximateCentroid(context, { "entities" : face }), "side1" : face }).sides[1].point;
        const side = signedSideOf(context, onFace, tool);
        if (side != undefined && abs(side) > abs(best))
        {
            best = side;
        }
    }
    return best;
}

/** A mate connector's frame, or undefined for anything else. */
function mateConnectorFrame(context is Context, tool is Query)
{
    if (isQueryEmpty(context, qBodyType(tool, BodyType.MATE_CONNECTOR)))
    {
        return undefined;
    }
    return evMateConnector(context, { "mateConnector" : tool });
}

/**
 * The opSplitPart keep type for one tool. KEEP_FRONT keeps the side the tool's normal points
 * to, so with a reference the kept side is the reference's side (or the other one).
 */
function keepTypeFor(context is Context, definition is map, probe, tool is Query, index is number) returns SplitOperationKeepType
{
    if (definition.keepBothSides)
    {
        return SplitOperationKeepType.KEEP_ALL;
    }
    if (probe == undefined)
    {
        return definition.keepNear ? SplitOperationKeepType.KEEP_FRONT : SplitOperationKeepType.KEEP_BACK;
    }

    const side = sideSign(context, probe, tool);
    if (side == 0)
    {
        throw regenError("The keep side reference lies on tool " ~ (index + 1) ~ " (or no side of it can be read there); pick something clearly on the side to keep.",
            ["keepReference"]);
    }
    const keepFront = (side > 0) == definition.keepNear;
    if (definition.debugPrintSides)
    {
        println("[split+] tool " ~ (index + 1) ~ ": reference " ~ fmtMM(signedSideOf(context, probe, tool), 3)
            ~ " mm from it, on its " ~ (side > 0 ? "front" : "back") ~ "; keeping the " ~ (keepFront ? "front" : "back") ~ ".");
    }
    return keepFront ? SplitOperationKeepType.KEEP_FRONT : SplitOperationKeepType.KEEP_BACK;
}
