FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// edge_offset_utils: formatting helpers for the console. Nothing geometric is shared.
import(path : "a2665e22c07b7a6929ce4e80", version : "");

/**
 * Mutual Trim+: the standard mutual trim, with the side to keep named by geometry
 * instead of by a flip.
 *
 * The built-in trims two sheets against each other and keeps, on each, whichever side of
 * the split its "keep opposite side" box says -- a choice that flips whenever a surface's
 * orientation changes upstream, which lofts and offsets do freely. Here a REFERENCE (a
 * body, face, edge, vertex or mate connector that lies on the keep side of both surfaces)
 * decides: on each surface the side nearer to the reference is kept ("Keep side nearest
 * reference", on by default), or the farther one with the box off. "Keep the inside" and
 * "keep the outside" are then statements about geometry, stable under any change to the
 * inputs. With no reference the feature is the built-in.
 *
 * Mechanism, unchanged from std/mutualTrim.fs: opSplitFace with mutual imprint splits both
 * sheets along their extended intersection; qSplitBy labels the two sides; a flood fill
 * bounded by the imprint edges partitions each body's faces into two sets; the set to
 * delete goes to opDeleteFace; the survivors are optionally unioned.
 */

const SPLIT_SUFFIX = "split";

/** How much nearer the reference must be to one side than the other before it decides. */
export const KEEP_SIDE_MARGIN = 1e-6 * meter;

annotation { "Feature Type Name" : "Mutual Trim+",
        "Feature Type Description" : "Mutual trim with the side to keep named by a reference: on each surface the side nearer to (or farther from) the reference is kept, whatever the surfaces' orientations.",
        "Filter Selector" : "allparts" }
export const mutualTrimToReference = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "First surface",
                    "Filter" : EntityType.BODY && BodyType.SHEET && ModifiableEntityOnly.YES && SketchObject.NO && ConstructionObject.NO,
                    "MaxNumberOfPicks" : 1 }
        definition.body1 is Query;

        annotation { "Name" : "Keep side nearest reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION, "Description" : "On: keep the side of the first surface nearer to the reference (the inside). Off: keep the farther side (the outside). Either way the choice survives changes to the inputs. Without a reference: the split's own front side on, back side off." }
        definition.keepNear1 is boolean;

        annotation { "Name" : "Second surface",
                    "Filter" : EntityType.BODY && BodyType.SHEET && ModifiableEntityOnly.YES && SketchObject.NO && ConstructionObject.NO,
                    "MaxNumberOfPicks" : 1 }
        definition.body2 is Query;

        annotation { "Name" : "Keep side nearest reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION, "Description" : "On: keep the side of the second surface nearer to the reference. Off: keep the farther side." }
        definition.keepNear2 is boolean;

        annotation { "Name" : "Keep side reference",
                    "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1,
                    "Description" : "Geometry on the keep side of both surfaces. On each surface the side nearer to it is kept, whatever the surfaces' orientations. Leave empty for the built-in behaviour." }
        definition.keepReference is Query;

        annotation { "Name" : "Merge", "Default" : true, "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        definition.merge is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print side distances", "Default" : false, "Description" : "How far the reference is from each side of each surface, and which side was kept." }
            definition.debugPrintSides is boolean;
        }
    }
    {
        checkSelections(context, definition);

        const splitId = id + SPLIT_SUFFIX;
        const splitResult = callSubfeatureAndProcessStatus(id, opSplitFace, context, splitId, {
                    "faceTargets" : qOwnedByBody(definition.body1, EntityType.FACE),
                    "bodyTools" : definition.body2,
                    "keepToolSurfaces" : true,
                    "extendToCompletion" : true,
                    "mutualImprint" : true
                });

        if (getFeatureStatus(context, splitId).statusEnum == ErrorStringEnum.SPLIT_FACE_NO_CHANGE)
        {
            reportFeatureInfo(context, id, ErrorStringEnum.BOOLEAN_UNION_NO_OP);
            return;
        }

        const facesToDelete = findFacesToDelete(context, id, definition, splitId, splitResult.splittingEdges);
        if (!isQueryEmpty(context, facesToDelete))
        {
            opDeleteFace(context, id + "deleteFaces", {
                        "deleteFaces" : facesToDelete,
                        "includeFillet" : false,
                        "capVoid" : false,
                        "leaveOpen" : true
                    });
        }

        if (definition.merge)
        {
            callSubfeatureAndProcessStatus(id, opBoolean, context, id + "merge", {
                        "tools" : qUnion([definition.body1, definition.body2]),
                        "operationType" : BooleanOperationType.UNION
                    }, { "propagateErrorDisplay" : true });
        }
    }, {
        "merge" : true,
        "keepNear1" : true,
        "keepNear2" : true,
        "keepReference" : qNothing(),
        "debugPrintSides" : false
    });

// ============================================================================
// Which faces go
// ============================================================================

/**
 * The faces to delete on both bodies.
 *
 * The imprint edges -- the split's own edges on each body, two-sided ones only -- bound
 * the two sides of every body. Each body is classified into its two sides; the reference,
 * where there is one, says which side stays.
 */
function findFacesToDelete(context is Context, id is Id, definition is map, splitId is Id, splittingEdges is array) returns Query
{
    var base = qCreatedBy(id, EntityType.EDGE);
    for (var edge in splittingEdges)
    {
        base = qUnion([base, edge]);
    }
    base = qUnion([base, qSplitBy(splitId, EntityType.EDGE, false), qSplitBy(splitId, EntityType.EDGE, true)]);

    const imprint1 = qIntersection([base, qOwnedByBody(definition.body1, EntityType.EDGE)])->qEdgeTopologyFilter(EdgeTopology.TWO_SIDED);
    const imprint2 = qIntersection([base, qOwnedByBody(definition.body2, EntityType.EDGE)])->qEdgeTopologyFilter(EdgeTopology.TWO_SIDED);
    if (isQueryEmpty(context, imprint1) || isQueryEmpty(context, imprint2))
    {
        return qNothing();
    }

    const reference = keepReferenceSide(context, definition);

    const delete1 = sideToDelete(context, definition, "first", qOwnedByBody(definition.body1, EntityType.FACE),
        imprint1, definition.keepNear1, splitId, reference);
    const delete2 = sideToDelete(context, definition, "second", qOwnedByBody(definition.body2, EntityType.FACE),
        imprint2, definition.keepNear2, splitId, reference);

    return qUnion([delete1, delete2]);
}

/**
 * The reference as something evDistance can measure from: the entity itself, or a mate
 * connector's origin. Undefined when none was picked.
 */
function keepReferenceSide(context is Context, definition is map)
{
    if (isQueryEmpty(context, definition.keepReference))
    {
        return undefined;
    }

    if (!isQueryEmpty(context, qBodyType(definition.keepReference, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : definition.keepReference }).origin;
    }

    return definition.keepReference;
}

/**
 * One body's faces to delete.
 *
 * With a reference, `keepNear` on keeps the side nearer to it and off the farther side:
 * "keep the inside" / "keep the outside", stated in geometry, so the choice holds when
 * the inputs change. A reference that cannot tell the two sides apart is an error, not a
 * guess. Without a reference this is the built-in: on keeps the split's front side, off
 * its back side.
 */
function sideToDelete(context is Context, definition is map, label is string, faces is Query, imprint is Query,
    keepNear is boolean, splitId is Id, reference) returns Query
{
    const sides = classifySides(context, faces, imprint, splitId);

    if (reference == undefined)
    {
        return keepNear ? sides.back : sides.front;
    }

    const toFront = evDistance(context, { "side0" : reference, "side1" : sides.front }).distance;
    const toBack = evDistance(context, { "side0" : reference, "side1" : sides.back }).distance;

    if (definition.debugPrintSides)
    {
        println("[trim] " ~ label ~ " surface: reference is " ~ fmtMM(toFront, 4, 0) ~ " mm from one side and "
            ~ fmtMM(toBack, 4, 0) ~ " mm from the other; keeping the " ~ (keepNear ? "nearer" : "farther") ~ " side.");
    }

    if (abs(toFront - toBack) < KEEP_SIDE_MARGIN)
    {
        throw regenError("The keep side reference is as close to one side of the " ~ label
            ~ " surface as to the other (" ~ fmtMM(toFront, 4, 0) ~ " mm); pick something clearly on the side to keep.",
            ["keepReference"]);
    }

    const nearer = (toFront < toBack) ? sides.front : sides.back;
    const farther = (toFront < toBack) ? sides.back : sides.front;
    return keepNear ? farther : nearer;
}

/**
 * A body's faces partitioned into the two sides of the split, by flood fill from the
 * split's own labelling, bounded by the imprint edges. Verbatim the built-in's
 * classification, returning both sets instead of one.
 *
 * @returns {map} : { "front", "back" } face queries.
 */
function classifySides(context is Context, faces is Query, imprint is Query, splitId is Id) returns map
{
    const nFaces = size(evaluateQuery(context, faces));
    var front = qIntersection([qSplitBy(splitId, EntityType.FACE, true), faces]);
    var back = qIntersection([qSplitBy(splitId, EntityType.FACE, false), faces]);

    for (var i = 0; i < nFaces; i += 1)
    {
        front = floodFill(context, front, imprint);
        back = floodFill(context, back, imprint);
        const classified = qUnion([front, back]);
        var left = qSubtraction(faces, classified);
        if (isQueryEmpty(context, left))
        {
            break;
        }

        const edgesToCheck = qIntersection([qAdjacent(left, AdjacencyType.EDGE, EntityType.EDGE),
                    qAdjacent(classified, AdjacencyType.EDGE, EntityType.EDGE), imprint]);
        if (isQueryEmpty(context, edgesToCheck))
        {
            throw regenError(ErrorStringEnum.MUTUAL_TRIM_GENERIC_ERROR);
        }

        const grown = addAdjacentToClassification(context, edgesToCheck, front, back);
        front = grown.front;
        back = grown.back;
        left = qSubtraction(left, front);
        if (isQueryEmpty(context, left))
        {
            break;
        }
    }

    return { "front" : front, "back" : back };
}

function floodFill(context is Context, seedFaces is Query, boundaryEdges is Query) returns Query
{
    const allBodyFaces = seedFaces->qOwnerBody()->qOwnedByBody(EntityType.FACE);
    const nMaxIterations = size(evaluateQuery(context, allBodyFaces));
    var result = seedFaces;
    var frontier = seedFaces;
    for (var i = 0; i < nMaxIterations; i += 1)
    {
        const facesToAdd = frontier->qAdjacent(AdjacencyType.EDGE, EntityType.EDGE)->qSubtraction(boundaryEdges)->qAdjacent(AdjacencyType.EDGE, EntityType.FACE);
        frontier = qSubtraction(facesToAdd, result);
        result = qUnion([result, facesToAdd]);
        if (isQueryEmpty(context, frontier))
        {
            break;
        }
    }
    return result;
}

/**
 * For each imprint edge between a classified and an unclassified face, put the
 * unclassified face on the other side.
 */
function addAdjacentToClassification(context is Context, edges is Query, front is Query, back is Query) returns map
{
    var frontFaces = evaluateQuery(context, front);
    var backFaces = evaluateQuery(context, back);
    for (var edge in evaluateQuery(context, edges))
    {
        const edgeFaces = evaluateQuery(context, edge->qAdjacent(AdjacencyType.EDGE, EntityType.FACE));
        if (size(edgeFaces) < 2)
        {
            continue;
        }
        for (var i = 0; i < 2; i += 1)
        {
            if (!isQueryEmpty(context, qIntersection([edgeFaces[i], qUnion(frontFaces)])))
            {
                backFaces = append(backFaces, edgeFaces[1 - i]);
                break;
            }
            else if (!isQueryEmpty(context, qIntersection([edgeFaces[i], qUnion(backFaces)])))
            {
                frontFaces = append(frontFaces, edgeFaces[1 - i]);
                break;
            }
        }
    }
    return { "front" : qUnion(frontFaces), "back" : qUnion(backFaces) };
}

function checkSelections(context is Context, definition is map)
{
    verifyNonemptyQuery(context, definition, "body1", ErrorStringEnum.MUTUAL_TRIM_SURFACE_NOT_SELECTED);
    verifyNonemptyQuery(context, definition, "body2", ErrorStringEnum.MUTUAL_TRIM_SURFACE_NOT_SELECTED);
    verify(!isQueryEmpty(context, qSubtraction(definition.body1, definition.body2)), ErrorStringEnum.MUTUAL_TRIM_SAME_SURFACE_USED,
        { "faultyParameters" : ["body1", "body2"] });
}
