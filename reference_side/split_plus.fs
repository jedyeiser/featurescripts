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
                        "splitEdges" : extractableQuery(qIntersection([qOwnedByBody(kept, EntityType.EDGE), qCreatedBy(id, EntityType.EDGE)]),
                                "Edges the splits created (the cut on surfaces).", DebugColor.MAGENTA)
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
