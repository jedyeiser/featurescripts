FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: mutual_trim_plus_icon.svg (feature icon)
IconNamespace::import(path : "ae64f5f432c178294f9b4d71", version : "80c08feb26502577f86ec001");

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
 *
 * Publishes (Extract variables): output (the trimmed surfaces, merged or not) and
 * trimEdges -- the intersection curve the trim left, i.e. the imprint edges that bound the
 * kept faces, tracked through the merge. That is the edge set a fillet along the trim wants.
 */

const SPLIT_SUFFIX = "split";

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Mutual Trim+",
        "Feature Type Description" : "Mutual trim with the side to keep named by a reference: on each surface the side nearer to (or farther from) the reference is kept, whatever the surfaces' orientations.",
        "Filter Selector" : "allparts" }
export const mutualTrimPlus = defineFeature(function(context is Context, id is Id, definition is map)
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
            publishTrim(context, id, definition, qNothing(), qOwnedByBody(definition.body1, EntityType.FACE), qOwnedByBody(definition.body2, EntityType.FACE));
            return;
        }

        const found = findFacesToDelete(context, id, definition, splitId, splitResult.splittingEdges);
        const facesToDelete = found.faces;
        // Imprint edges already on a sheet's outer boundary BEFORE the delete: the pieces of the
        // perimeter the extended imprint split. They stay one-sided after the delete and are not
        // part of the trim (2026-09-27: a crossing X published the trim edge + 4 perimeter halves).
        const perimeter = qUnion(evaluateQuery(context, qEdgeTopologyFilter(found.imprint, EdgeTopology.ONE_SIDED)));
        if (!isQueryEmpty(context, facesToDelete))
        {
            opDeleteFace(context, id + "deleteFaces", {
                        "deleteFaces" : facesToDelete,
                        "includeFillet" : false,
                        "capVoid" : false,
                        "leaveOpen" : true
                    });
        }

        // The trim boundary: imprint edges left bounding a kept face on one side only. Frozen
        // now and tracked, so the merge below (which fuses the two sides' boundary edges into
        // one) does not lose them.
        const boundary = qUnion(evaluateQuery(context, qSubtraction(qEdgeTopologyFilter(found.imprint, EdgeTopology.ONE_SIDED), perimeter)));
        const trimEdges = qUnion([boundary, startTracking(context, boundary)]);
        const keptFaces1 = frozenAndTracked(context, qOwnedByBody(definition.body1, EntityType.FACE));
        const keptFaces2 = frozenAndTracked(context, qOwnedByBody(definition.body2, EntityType.FACE));

        if (definition.merge)
        {
            callSubfeatureAndProcessStatus(id, opBoolean, context, id + "merge", {
                        "tools" : qUnion([definition.body1, definition.body2]),
                        "operationType" : BooleanOperationType.UNION
                    }, { "propagateErrorDisplay" : true });
        }

        publishTrim(context, id, definition, trimEdges, keptFaces1, keptFaces2);
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
 * Publishes the standard outputs (the trimmed surfaces: body1 carries the merge) plus
 * trimEdges, the intersection curve along which the surfaces were trimmed, and the faces
 * each surface kept (regions: keptFaces1 / keptFaces2).
 */
function publishTrim(context is Context, id is Id, definition is map, trimEdges is Query, keptFaces1 is Query, keptFaces2 is Query)
{
    embedStandardOutputs(context, id, settledOutputs(context, {
                "output" : qUnion([definition.body1, definition.body2]),
                "outputDescription" : "The trimmed surfaces",
                "inputs" : qUnion([definition.body1, definition.body2]),
                "variables" : {
                    "trimEdgeCount" : extractableVariable(size(evaluateQuery(context, trimEdges)),
                            "Edges along the intersection the trim left.")
                },
                "queries" : {
                    "trimEdges" : extractableQuery(trimEdges,
                            "The intersection edges along which the surfaces were trimmed (fillet here).", DebugColor.MAGENTA),
                    "keptFaces1" : extractableQuery(keptFaces1, "The faces the first surface kept (still told apart after the merge).", DebugColor.CYAN),
                    "keptFaces2" : extractableQuery(keptFaces2, "The faces the second surface kept.", DebugColor.YELLOW)
                }
            }));
}

/**
 * The faces to delete on both bodies, and the imprint edges (both bodies) that bound them.
 *
 * The imprint edges -- the split's own edges on each body, two-sided ones only -- bound
 * the two sides of every body. Each body is classified into its two sides; the reference,
 * where there is one, says which side stays.
 */
function findFacesToDelete(context is Context, id is Id, definition is map, splitId is Id, splittingEdges is array) returns map
{
    var base = qCreatedBy(id, EntityType.EDGE);
    for (var edge in splittingEdges)
    {
        base = qUnion([base, edge]);
    }
    base = qUnion([base, qSplitBy(splitId, EntityType.EDGE, false), qSplitBy(splitId, EntityType.EDGE, true)]);

    // Unfiltered: after the delete the trim boundary is ONE-sided, so the caller filters
    // this set itself (the two-sided filter below would drop exactly the edges it wants).
    const imprintAll = qIntersection([base, qUnion([qOwnedByBody(definition.body1, EntityType.EDGE), qOwnedByBody(definition.body2, EntityType.EDGE)])]);
    const imprint1 = qIntersection([base, qOwnedByBody(definition.body1, EntityType.EDGE)])->qEdgeTopologyFilter(EdgeTopology.TWO_SIDED);
    const imprint2 = qIntersection([base, qOwnedByBody(definition.body2, EntityType.EDGE)])->qEdgeTopologyFilter(EdgeTopology.TWO_SIDED);
    if (isQueryEmpty(context, imprint1) || isQueryEmpty(context, imprint2))
    {
        return { "faces" : qNothing(), "imprint" : imprintAll };
    }

    const reference = referenceProbe(context, definition.keepReference);

    const faces1 = qOwnedByBody(definition.body1, EntityType.FACE);
    const faces2 = qOwnedByBody(definition.body2, EntityType.FACE);
    const delete1 = sideToDelete(context, definition, "first", faces1, imprint1, definition.keepNear1, splitId, reference, faces2);
    const delete2 = sideToDelete(context, definition, "second", faces2, imprint2, definition.keepNear2, splitId, reference, faces1);

    return { "faces" : qUnion([delete1, delete2]), "imprint" : imprintAll };
}

/**
 * One body's faces to delete.
 *
 * With a reference, `keepNear` on keeps the side the reference is on and off the other:
 * "keep the inside" / "keep the outside", stated in geometry, so the choice holds when
 * the inputs change. "The side the reference is on" is decided against the SPLITTER --
 * the other surface, extended: the reference and each piece of this surface get a signed
 * distance to it, and the piece on the reference's side is the near one. Plain distance
 * to the pieces is only the fallback: a leaning wall's upper half can reach closer to a
 * point below the cut than the lower half does, and distance then picks the wrong side.
 * Without a reference this is the built-in: on keeps the split's front side, off its back.
 */
function sideToDelete(context is Context, definition is map, label is string, faces is Query, imprint is Query,
    keepNear is boolean, splitId is Id, reference, splitter is Query) returns Query
{
    const sides = classifySides(context, faces, imprint, splitId);

    if (reference == undefined)
    {
        return keepNear ? sides.back : sides.front;
    }

    var near = undefined;
    var far = undefined;
    var how = "";

    const refSide = signedSideOf(context, reference, splitter);
    const frontSide = signedSideOf(context, evApproximateCentroid(context, { "entities" : sides.front }), splitter);
    const backSide = signedSideOf(context, evApproximateCentroid(context, { "entities" : sides.back }), splitter);

    if (refSide != undefined && frontSide != undefined && backSide != undefined
        && abs(refSide) > REFERENCE_SIDE_MARGIN && frontSide * backSide < 0 * meter * meter)
    {
        const refOnFront = (refSide > 0 * meter) == (frontSide > 0 * meter);
        near = refOnFront ? sides.front : sides.back;
        far = refOnFront ? sides.back : sides.front;
        how = "by side of the other surface (reference " ~ fmtMM(refSide, 3) ~ " mm, pieces "
            ~ fmtMM(frontSide, 3) ~ " / " ~ fmtMM(backSide, 3) ~ " mm)";
    }
    else
    {
        const toFront = evDistance(context, { "side0" : reference, "side1" : sides.front }).distance;
        const toBack = evDistance(context, { "side0" : reference, "side1" : sides.back }).distance;
        if (abs(toFront - toBack) < REFERENCE_SIDE_MARGIN)
        {
            throw regenError("The keep side reference cannot be placed on either side of the " ~ label
                ~ " surface; pick something clearly on the side to keep.", ["keepReference"]);
        }
        near = (toFront < toBack) ? sides.front : sides.back;
        far = (toFront < toBack) ? sides.back : sides.front;
        how = "by distance (" ~ fmtMM(toFront, 3) ~ " / " ~ fmtMM(toBack, 3) ~ " mm; the pieces did not straddle the other surface)";
    }

    if (definition.debugPrintSides)
    {
        println("[trim] " ~ label ~ " surface: keeping the " ~ (keepNear ? "reference's" : "opposite") ~ " side, decided " ~ how ~ ".");
    }

    return keepNear ? far : near;
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

/** The entities of a query now, and whatever they become later in this feature (a merge). */
function frozenAndTracked(context is Context, q is Query) returns Query
{
    const now = qUnion(evaluateQuery(context, q));
    return qUnion([now, startTracking(context, now)]);
}
