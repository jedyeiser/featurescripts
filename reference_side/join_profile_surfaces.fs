FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: offset_plus.fs (Offset+, called for every profile)
import(path : "742e5b3f04cc9115de8b6d8a", version : "7a62aeff1d43b811531bb007");
// IMPORT: split_plus.fs (Split+, called for every profile)
import(path : "637854639ad04840dc6f998d", version : "511ae1dfefcb5b639728ab8b");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: join_profile_surfaces_icon.svg (feature icon)
IconNamespace::import(path : "3749310adb8f74e6e862ad8e", version : "0f493347f757f749e45e12b9");

/**
 * Join profile surfaces (2026-09-27): join an inside profile surface to outside profile surfaces
 * across a start and an end split, with a lofted strip at each split -- the steps done by hand
 * before as Offset+ x N, Split+ x N, two lofts and fillets.
 *
 * One INSIDE POINT drives every side decision: it lies inside every profile surface and between
 * the start and end splits.
 *     1. Every profile is offset with Offset+ toward the inside point (positive = toward it,
 *        negative = away, 0 = a copy).
 *     2. The inside offset is split with Split+ at both splits and keeps the INSIDE (between them).
 *        The outside offsets keep the OUTSIDE: one surface split at both (Same outside surface),
 *        or a start surface split at the start and an end surface split at the end (Different).
 *     3. A loft joins each pair of cut edges: inside startCut to outside startCut, and the same at
 *        the end.
 *     4. Merge: everything is united into one surface (always -- joining is the point; a joint that
 *        does not close is an error naming it), then the fillets on the merged joint edges.
 *
 * Publishes (Extract variables): output (the joined surface), insideSurface,
 * startOutside, endOutside, startLoft, endLoft (faces), the four joint edges startInsideEdge,
 * startOutsideEdge, endInsideEdge, endOutsideEdge, and boundaryEdges.
 */

/** Where the outside profiles come from. */
export enum JoinOutsideMode
{
    annotation { "Name" : "Same outside surface" }
    SAME,
    annotation { "Name" : "Different start and end surfaces" }
    DIFFERENT
}

/** Which joint edge of a loft a fillet goes on. */
export enum JoinFilletEdge
{
    annotation { "Name" : "Inside edge" }
    INSIDE,
    annotation { "Name" : "Outside edge" }
    OUTSIDE
}

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Join profile surfaces",
        "Feature Type Description" : "Offset an inside profile and outside profile(s) toward an inside point, cut them at a start and an end split (inside keeps the middle, outside the ends), and join the cuts with lofted strips, optionally filleted.",
        "Filter Selector" : "allparts" }
export const joinProfileSurfaces = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Start split", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.startSplit is Query;

        annotation { "Name" : "End split", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.endSplit is Query;

        annotation { "Name" : "Inside point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "A point inside every profile surface and between the start and end splits. Positive offsets move toward it." }
        definition.insidePoint is Query;

        annotation { "Name" : "Inside profile", "Filter" : EntityType.BODY && BodyType.SHEET && SketchObject.NO && ConstructionObject.NO, "MaxNumberOfPicks" : 1 }
        definition.insideProfile is Query;

        annotation { "Name" : "Inside offset", "Description" : "Positive: toward the inside point. 0: a copy." }
        isLength(definition.insideOffset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Outside profiles", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinOutsideMode.SAME }
        definition.outsideMode is JoinOutsideMode;

        if (definition.outsideMode == JoinOutsideMode.SAME)
        {
            annotation { "Name" : "Outside profile", "Filter" : EntityType.BODY && BodyType.SHEET && SketchObject.NO && ConstructionObject.NO, "MaxNumberOfPicks" : 1 }
            definition.outsideProfile is Query;

            annotation { "Name" : "Outside offset", "Description" : "Positive: toward the inside point. 0: a copy." }
            isLength(definition.outsideOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Start outside profile", "Filter" : EntityType.BODY && BodyType.SHEET && SketchObject.NO && ConstructionObject.NO, "MaxNumberOfPicks" : 1 }
            definition.startProfile is Query;

            annotation { "Name" : "Start outside offset", "Description" : "Positive: toward the inside point. 0: a copy." }
            isLength(definition.startOffset, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "End outside profile", "Filter" : EntityType.BODY && BodyType.SHEET && SketchObject.NO && ConstructionObject.NO, "MaxNumberOfPicks" : 1 }
            definition.endProfile is Query;

            annotation { "Name" : "End outside offset", "Description" : "Positive: toward the inside point. 0: a copy." }
            isLength(definition.endOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }

        annotation { "Name" : "Start fillet", "Default" : false }
        definition.startFillet is boolean;
        if (definition.startFillet)
        {
            annotation { "Name" : "Start fillet radius" }
            isLength(definition.startRadius, BLEND_BOUNDS);
            annotation { "Name" : "Start fillet edge", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinFilletEdge.INSIDE }
            definition.startFilletEdge is JoinFilletEdge;
            annotation { "Name" : "Keep opposite start edge", "Default" : false,
                        "Description" : "Protect the loft's other joint edge from the fillet (edge overflow on, that edge kept)." }
            definition.startKeepOpposite is boolean;
        }

        annotation { "Name" : "End fillet", "Default" : false }
        definition.endFillet is boolean;
        if (definition.endFillet)
        {
            annotation { "Name" : "End fillet radius" }
            isLength(definition.endRadius, BLEND_BOUNDS);
            annotation { "Name" : "End fillet edge", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinFilletEdge.INSIDE }
            definition.endFilletEdge is JoinFilletEdge;
            annotation { "Name" : "Keep opposite end edge", "Default" : false,
                        "Description" : "Protect the loft's other joint edge from the fillet (edge overflow on, that edge kept)." }
            definition.endKeepOpposite is boolean;
        }

        annotation { "Name" : "Keep input surfaces", "Default" : true, "Description" : "Off: delete the profile surfaces this feature used." }
        definition.keepInputs is boolean;
    }
    {
        verifyNonemptyQuery(context, definition, "startSplit", "Select the start split.");
        verifyNonemptyQuery(context, definition, "endSplit", "Select the end split.");
        verifyNonemptyQuery(context, definition, "insidePoint", "Select the inside point.");
        verifyNonemptyQuery(context, definition, "insideProfile", "Select the inside profile surface.");
        const same = definition.outsideMode == JoinOutsideMode.SAME;
        if (same)
        {
            verifyNonemptyQuery(context, definition, "outsideProfile", "Select the outside profile surface.");
        }
        else
        {
            verifyNonemptyQuery(context, definition, "startProfile", "Select the start outside profile surface.");
            verifyNonemptyQuery(context, definition, "endProfile", "Select the end outside profile surface.");
        }
        const inputs = same ? [definition.insideProfile, definition.outsideProfile]
                            : [definition.insideProfile, definition.startProfile, definition.endProfile];

        // 1. Offsets (Offset+: positive toward the inside point, 0 a copy).
        const inside = offsetProfile(context, id + "insideOffset", definition.insideProfile, definition.insideOffset, definition.insidePoint);

        // 2. Splits (Split+ with the inside point; the split tools are the user's and are kept).
        const insideSplit = splitProfile(context, id + "insideSplit", inside, definition.startSplit, definition.endSplit, definition.insidePoint, SplitPlusKeep.INSIDE);
        const insideStart = insideSplit.startCut;
        const insideEnd = insideSplit.endCut;
        const insideFaces = frozenAndTracked(context, qOwnedByBody(insideSplit.inside, EntityType.FACE));

        var outsideStart;
        var outsideEnd;
        var startOutsideFaces;
        var endOutsideFaces;
        if (same)
        {
            const outside = offsetProfile(context, id + "outsideOffset", definition.outsideProfile, definition.outsideOffset, definition.insidePoint);
            const outsideSplit = splitProfile(context, id + "outsideSplit", outside, definition.startSplit, definition.endSplit, definition.insidePoint, SplitPlusKeep.OUTSIDE);
            outsideStart = outsideSplit.startCut;
            outsideEnd = outsideSplit.endCut;
            startOutsideFaces = frozenAndTracked(context, qOwnedByBody(outsideSplit.start, EntityType.FACE));
            endOutsideFaces = frozenAndTracked(context, qOwnedByBody(outsideSplit.end, EntityType.FACE));
        }
        else
        {
            // One tool each: its cut is Split+'s startCut, the piece beyond it Split+'s start region.
            const startOut = offsetProfile(context, id + "startOffset", definition.startProfile, definition.startOffset, definition.insidePoint);
            const startSplit = splitProfile(context, id + "startSplit", startOut, definition.startSplit, qNothing(), definition.insidePoint, SplitPlusKeep.OUTSIDE);
            const endOut = offsetProfile(context, id + "endOffset", definition.endProfile, definition.endOffset, definition.insidePoint);
            const endSplit = splitProfile(context, id + "endSplit", endOut, definition.endSplit, qNothing(), definition.insidePoint, SplitPlusKeep.OUTSIDE);
            outsideStart = startSplit.startCut;
            outsideEnd = endSplit.startCut;
            startOutsideFaces = frozenAndTracked(context, qOwnedByBody(startSplit.start, EntityType.FACE));
            endOutsideFaces = frozenAndTracked(context, qOwnedByBody(endSplit.start, EntityType.FACE));
        }
        verifyCut(context, insideStart, "start", "inside");
        verifyCut(context, insideEnd, "end", "inside");
        verifyCut(context, outsideStart, "start", "outside");
        verifyCut(context, outsideEnd, "end", "outside");

        // 3. Lofts between the paired cut edges.
        opLoft(context, id + "startLoft", { "profileSubqueries" : [insideStart, outsideStart], "bodyType" : ToolBodyType.SURFACE });
        opLoft(context, id + "endLoft", { "profileSubqueries" : [insideEnd, outsideEnd], "bodyType" : ToolBodyType.SURFACE });
        const startLoftFaces = frozenAndTracked(context, qCreatedBy(id + "startLoft", EntityType.FACE));
        const endLoftFaces = frozenAndTracked(context, qCreatedBy(id + "endLoft", EntityType.FACE));
        const joints = {
                "startInside" : frozenAndTracked(context, insideStart),
                "startOutside" : frozenAndTracked(context, outsideStart),
                "endInside" : frozenAndTracked(context, insideEnd),
                "endOutside" : frozenAndTracked(context, outsideEnd)
            };

        // Every body this feature made (the offsets' kept pieces and the lofts).
        const pieces = qUnion([qOwnerBody(insideFaces), qOwnerBody(startOutsideFaces), qOwnerBody(endOutsideFaces),
                    qCreatedBy(id + "startLoft", EntityType.BODY), qCreatedBy(id + "endLoft", EntityType.BODY)]);

        // 4. Merge (always: joining is the point), then the fillets on the merged joint edges.
        var mergeFailed = false;
        try silent
        {
            opBoolean(context, id + "merge", { "tools" : qUnion(evaluateQuery(context, pieces)), "operationType" : BooleanOperationType.UNION });
        }
        catch
        {
            mergeFailed = true;
        }
        const result = qUnion(evaluateQuery(context, qOwnerBody(qUnion([insideFaces, startOutsideFaces, endOutsideFaces, startLoftFaces, endLoftFaces]))));
        const jointEdge = function(key is string) returns Query
            {
                return qIntersection([qEntityFilter(joints[key], EntityType.EDGE), qOwnedByBody(result, EntityType.EDGE)]);
            };
        // Every joint must have closed: its edge now shared by the loft and the surface it joins.
        var openJoints = [];
        for (var joint in [["startInside", "start loft to the inside surface"], ["startOutside", "start loft to the outside surface"],
                           ["endInside", "end loft to the inside surface"], ["endOutside", "end loft to the outside surface"]])
        {
            if (isQueryEmpty(context, qEdgeTopologyFilter(jointEdge(joint[0]), EdgeTopology.TWO_SIDED)))
            {
                openJoints = append(openJoints, joint[1]);
            }
        }
        if (mergeFailed || size(evaluateQuery(context, result)) != 1 || size(openJoints) > 0)
        {
            throw regenError("The pieces did not join into one surface" ~ (size(openJoints) > 0 ? ": open at the " ~ join(openJoints, ", ") : "")
                    ~ ". A loft edge does not meet its cut edge -- check that the profiles reach the splits cleanly.", ["startSplit"]);
        }
        if (definition.startFillet)
        {
            filletJoint(context, id + "startFillet", jointEdge("startInside"), jointEdge("startOutside"), definition.startFilletEdge,
                definition.startRadius, definition.startKeepOpposite, "start");
        }
        if (definition.endFillet)
        {
            filletJoint(context, id + "endFillet", jointEdge("endInside"), jointEdge("endOutside"), definition.endFilletEdge,
                definition.endRadius, definition.endKeepOpposite, "end");
        }

        if (!definition.keepInputs)
        {
            const inputBodies = qConstructionFilter(qBodyType(qUnion(inputs), BodyType.SHEET), ConstructionObject.NO);
            if (!isQueryEmpty(context, inputBodies))
            {
                opDeleteBodies(context, id + "deleteInputs", { "entities" : inputBodies });
            }
        }

        const faceKey = function(q is Query, description is string) returns map
            {
                return extractableQuery(qIntersection([qEntityFilter(q, EntityType.FACE), qOwnedByBody(result, EntityType.FACE)]), description, DebugColor.CYAN);
            };
        embedStandardOutputs(context, id, {
                    "output" : result,
                    "outputDescription" : "The joined surface",
                    "inputs" : qUnion(concatenateArrays([inputs, [definition.startSplit, definition.endSplit]])),
                    "queries" : {
                        "insideSurface" : faceKey(insideFaces, "Faces from the inside profile (between the splits)."),
                        "startOutside" : faceKey(startOutsideFaces, "Faces from the outside profile beyond the start split."),
                        "endOutside" : faceKey(endOutsideFaces, "Faces from the outside profile beyond the end split."),
                        "startLoft" : faceKey(startLoftFaces, "The lofted strip at the start split."),
                        "endLoft" : faceKey(endLoftFaces, "The lofted strip at the end split."),
                        "startInsideEdge" : extractableQuery(jointEdge("startInside"), "The start loft's joint with the inside surface.", DebugColor.MAGENTA),
                        "startOutsideEdge" : extractableQuery(jointEdge("startOutside"), "The start loft's joint with the outside surface.", DebugColor.MAGENTA),
                        "endInsideEdge" : extractableQuery(jointEdge("endInside"), "The end loft's joint with the inside surface.", DebugColor.MAGENTA),
                        "endOutsideEdge" : extractableQuery(jointEdge("endOutside"), "The end loft's joint with the outside surface.", DebugColor.MAGENTA),
                        "boundaryEdges" : extractableQuery(qEdgeTopologyFilter(qOwnedByBody(result, EntityType.EDGE), EdgeTopology.ONE_SIDED),
                            "The open edges of the result.", DebugColor.CYAN)
                    }
                });
    }, {
        "insideOffset" : 0 * meter,
        "outsideMode" : JoinOutsideMode.SAME,
        "outsideProfile" : qNothing(),
        "outsideOffset" : 0 * meter,
        "startProfile" : qNothing(),
        "startOffset" : 0 * meter,
        "endProfile" : qNothing(),
        "endOffset" : 0 * meter,
        "startFillet" : false,
        "endFillet" : false,
        "keepInputs" : true
    });

/** Offset+ of one profile toward the inside point; the offset surface (a copy at 0). */
function offsetProfile(context is Context, id is Id, profile is Query, distance is ValueWithUnits, insidePoint is Query) returns Query
{
    offsetPlus(context, id, {
                "offsetType" : OffsetPlusType.SURFACE,
                "surfaces" : profile,
                "distance" : distance,
                "sideReference" : insidePoint,
                "towardReference" : true,
                "debugPrint" : false
            });
    return qCreatedBy(id, EntityType.BODY);
}

/** Split+ of one offset surface; its published keys as queries (inside, start, end, startCut, endCut). */
function splitProfile(context is Context, id is Id, target is Query, startTool is Query, endTool is Query, insidePoint is Query, keep is SplitPlusKeep) returns map
{
    splitPlus(context, id, {
                "splitType" : SplitPlusType.PART,
                "targets" : target,
                "startTool" : startTool,
                "endTool" : endTool,
                "insideReference" : insidePoint,
                "keep" : keep,
                "useTrimmed" : false,
                "keepTools" : true,
                "debugPrintSides" : false
            });
    const out = getVariable(context, toString(id));
    var result = {};
    for (var key in ["inside", "start", "end", "startCut", "endCut"])
    {
        result[key] = out.query[key].value;
    }
    return result;
}

/** A cut the join needs must exist. */
function verifyCut(context is Context, cut is Query, which is string, side is string)
{
    if (isQueryEmpty(context, cut))
    {
        throw regenError("The " ~ side ~ " profile is not cut by the " ~ which ~ " split: it does not reach the split, or the inside point is on the wrong side.",
            [which == "start" ? "startSplit" : "endSplit"]);
    }
}

/** Fillet one joint edge; optionally protect the loft's other joint edge. */
function filletJoint(context is Context, id is Id, insideEdge is Query, outsideEdge is Query, which is JoinFilletEdge,
    radius is ValueWithUnits, keepOpposite is boolean, where is string)
{
    const edge = which == JoinFilletEdge.INSIDE ? insideEdge : outsideEdge;
    const opposite = which == JoinFilletEdge.INSIDE ? outsideEdge : insideEdge;
    if (isQueryEmpty(context, edge))
    {
        throw regenError("The " ~ where ~ " joint edge to fillet was not found after the merge.", [where ~ "Fillet"]);
    }
    var fillet = { "entities" : edge, "radius" : radius };
    if (keepOpposite)
    {
        fillet.allowEdgeOverflow = true;
        fillet.keepEdges = opposite;
    }
    opFillet(context, id, fillet);
}

/** The entities of a query now, followed through later edits. */
function frozenAndTracked(context is Context, q is Query) returns Query
{
    return qUnion([qUnion(evaluateQuery(context, q)), startTracking(context, q)]);
}
