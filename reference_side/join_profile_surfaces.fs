FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// LoftEndDerivativeType and createProfileConditions (G1 / G2 connections) are not part of common.fs
import(path : "onshape/std/loft.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: offset_plus.fs (Offset+, called for every profile)
import(path : "742e5b3f04cc9115de8b6d8a", version : "a70e95e0c3507503d79c7fe9");
// IMPORT: split_plus.fs (Split+, called for every profile)
import(path : "637854639ad04840dc6f998d", version : "f803257d22d5a7ba34163ec7");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
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
 * Either split alone is enough (2026-10-08): the inside profile keeps the inside point's side of
 * it, the outside profile the other side, and one loft joins them. The keys of the missing side
 * (endOutside, endLoft, the end joint edges, or the start ones) are then empty. Fillets and
 * "different" outside profiles are needed only for the splits given.
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

/** How the cut edges of the inside and the outside surface are joined at a split. */
export enum JoinConnection
{
    annotation { "Name" : "Ruled (G0)" }
    RULED,
    annotation { "Name" : "Tangent (G1)" }
    TANGENT,
    annotation { "Name" : "Curvature (G2)" }
    CURVATURE,
    annotation { "Name" : "Fillets and a straight section" }
    FILLETS
}

/** The tangent magnitude of a G1 / G2 connection, relative to the kernel's default. */
const JOIN_MAGNITUDE_BOUNDS = { (unitless) : [0.05, 1, 10] } as RealBoundSpec;

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
        annotation { "Name" : "Start split", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Give a start split, an end split, or both. With one split there is one transition." }
        definition.startSplit is Query;

        annotation { "Name" : "End split", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Give a start split, an end split, or both. With one split there is one transition." }
        definition.endSplit is Query;

        annotation { "Name" : "Inside point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "A point inside every profile surface, on the side of the split(s) the inside profile keeps: between the start and end splits, or beyond the only split. Positive offsets move toward it." }
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

        annotation { "Group Name" : "Start connection", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Start inside trim", "Description" : "Moves the inside surface's cut away from the start split, toward the inside point (measured normal to a planar split, or offset from a split surface). 0: the cut is on the split. Not negative." }
            isLength(definition.startInsideTrim, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "Start outside trim", "Description" : "Moves the outside surface's cut away from the start split, away from the inside point. 0: the cut is on the split. Not negative." }
            isLength(definition.startOutsideTrim, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "Start connection", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinConnection.RULED }
            definition.startConnection is JoinConnection;

            if (definition.startConnection == JoinConnection.TANGENT || definition.startConnection == JoinConnection.CURVATURE)
            {
                annotation { "Name" : "Start magnitude", "Description" : "How far the connection keeps to the surface tangent before turning; 1 is the kernel default." }
                isReal(definition.startMagnitude, JOIN_MAGNITUDE_BOUNDS);
            }
            else if (definition.startConnection == JoinConnection.FILLETS)
            {
                annotation { "Name" : "Start inside radius", "Description" : "The arc that meets the inside surface. 0: none. Needs a gap (a trim)." }
                isLength(definition.startInsideRadius, ZERO_DEFAULT_LENGTH_BOUNDS);
                annotation { "Name" : "Start outside radius", "Description" : "The arc that meets the outside surface. 0: none." }
                isLength(definition.startOutsideRadius, ZERO_DEFAULT_LENGTH_BOUNDS);
            }
        }
        if (definition.startConnection == JoinConnection.RULED)
        {
            annotation { "Name" : "Start fillet", "Default" : false }
            definition.startFillet is boolean;
        }
        if (definition.startConnection == JoinConnection.RULED && definition.startFillet)
        {
            annotation { "Name" : "Start fillet radius" }
            isLength(definition.startRadius, BLEND_BOUNDS);
            annotation { "Name" : "Start fillet edge", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinFilletEdge.INSIDE }
            definition.startFilletEdge is JoinFilletEdge;
            annotation { "Name" : "Keep opposite start edge", "Default" : false,
                        "Description" : "Protect the loft's other joint edge from the fillet (edge overflow on, that edge kept)." }
            definition.startKeepOpposite is boolean;
        }

        annotation { "Group Name" : "End connection", "Collapsed By Default" : true }
        {
            annotation { "Name" : "End inside trim", "Description" : "Moves the inside surface's cut away from the end split, toward the inside point (measured normal to a planar split, or offset from a split surface). 0: the cut is on the split. Not negative." }
            isLength(definition.endInsideTrim, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "End outside trim", "Description" : "Moves the outside surface's cut away from the end split, away from the inside point. 0: the cut is on the split. Not negative." }
            isLength(definition.endOutsideTrim, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "End connection", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinConnection.RULED }
            definition.endConnection is JoinConnection;

            if (definition.endConnection == JoinConnection.TANGENT || definition.endConnection == JoinConnection.CURVATURE)
            {
                annotation { "Name" : "End magnitude", "Description" : "How far the connection keeps to the surface tangent before turning; 1 is the kernel default." }
                isReal(definition.endMagnitude, JOIN_MAGNITUDE_BOUNDS);
            }
            else if (definition.endConnection == JoinConnection.FILLETS)
            {
                annotation { "Name" : "End inside radius", "Description" : "The arc that meets the inside surface. 0: none. Needs a gap (a trim)." }
                isLength(definition.endInsideRadius, ZERO_DEFAULT_LENGTH_BOUNDS);
                annotation { "Name" : "End outside radius", "Description" : "The arc that meets the outside surface. 0: none." }
                isLength(definition.endOutsideRadius, ZERO_DEFAULT_LENGTH_BOUNDS);
            }
        }
        if (definition.endConnection == JoinConnection.RULED)
        {
            annotation { "Name" : "End fillet", "Default" : false }
            definition.endFillet is boolean;
        }
        if (definition.endConnection == JoinConnection.RULED && definition.endFillet)
        {
            annotation { "Name" : "End fillet radius" }
            isLength(definition.endRadius, BLEND_BOUNDS);
            annotation { "Name" : "End fillet edge", "UIHint" : [UIHint.SHOW_LABEL], "Default" : JoinFilletEdge.INSIDE }
            definition.endFilletEdge is JoinFilletEdge;
            annotation { "Name" : "Keep opposite end edge", "Default" : false,
                        "Description" : "Protect the loft's other joint edge from the fillet (edge overflow on, that edge kept)." }
            definition.endKeepOpposite is boolean;
        }

        annotation { "Name" : "Output name", "Description" : "Optional. Names the joined surface; leave empty for the default name." }
        definition.outputName is string;

        annotation { "Name" : "Keep input surfaces", "Default" : true, "Description" : "Off: delete the profile surfaces this feature used." }
        definition.keepInputs is boolean;
    }
    {
        // Either split alone is a valid join (one transition); both give a transition at each end.
        const hasStart = !isQueryEmpty(context, definition.startSplit);
        const hasEnd = !isQueryEmpty(context, definition.endSplit);
        if (!hasStart && !hasEnd)
        {
            throw regenError("Select a start split, an end split, or both.", ["startSplit"]);
        }
        verifyNonemptyQuery(context, definition, "insidePoint", "Select the inside point.");
        verifyNonemptyQuery(context, definition, "insideProfile", "Select the inside profile surface.");
        const startRuled = definition.startConnection == JoinConnection.RULED;
        const endRuled = definition.endConnection == JoinConnection.RULED;
        if (definition.startFillet && startRuled && !hasStart)
        {
            throw regenError("The start fillet needs a start split.", ["startFillet"]);
        }
        if (definition.endFillet && endRuled && !hasEnd)
        {
            throw regenError("The end fillet needs an end split.", ["endFillet"]);
        }
        for (var endInfo in [["start", hasStart], ["end", hasEnd]])
        {
            const trims = [definition[endInfo[0] ~ "InsideTrim"], definition[endInfo[0] ~ "OutsideTrim"]];
            const connection = definition[endInfo[0] ~ "Connection"];
            if (trims[0] < 0 * meter || trims[1] < 0 * meter)
            {
                throw regenError("The " ~ endInfo[0] ~ " trims cannot be negative.", [endInfo[0] ~ "InsideTrim", endInfo[0] ~ "OutsideTrim"]);
            }
            if (!endInfo[1] && (connection != JoinConnection.RULED || trims[0] > 0 * meter || trims[1] > 0 * meter))
            {
                throw regenError("The " ~ endInfo[0] ~ " trims and connection need a " ~ endInfo[0] ~ " split.", [endInfo[0] ~ "Connection"]);
            }
            if (connection == JoinConnection.FILLETS && definition[endInfo[0] ~ "InsideRadius"] <= 0 * meter && definition[endInfo[0] ~ "OutsideRadius"] <= 0 * meter)
            {
                throw regenError("Give the " ~ endInfo[0] ~ " connection an inside radius, an outside radius, or both.", [endInfo[0] ~ "InsideRadius"]);
            }
        }
        const same = definition.outsideMode == JoinOutsideMode.SAME;
        var inputs = [definition.insideProfile];
        if (same)
        {
            verifyNonemptyQuery(context, definition, "outsideProfile", "Select the outside profile surface.");
            inputs = append(inputs, definition.outsideProfile);
        }
        else
        {
            if (hasStart)
            {
                verifyNonemptyQuery(context, definition, "startProfile", "Select the start outside profile surface.");
                inputs = append(inputs, definition.startProfile);
            }
            if (hasEnd)
            {
                verifyNonemptyQuery(context, definition, "endProfile", "Select the end outside profile surface.");
                inputs = append(inputs, definition.endProfile);
            }
        }
        // Each side cuts at its own tool: the split itself, or (a trim > 0) a copy of it moved normal to a plane / offset from a
        // surface, the inside one toward the inside point and the outside one away from it. A trim of 0 uses the user's tool as it is.
        const startInsideTool = hasStart ? trimmedTool(context, id + "startInsideTool", definition.startSplit, definition.startInsideTrim, definition.insidePoint, true) : qNothing();
        const startOutsideTool = hasStart ? trimmedTool(context, id + "startOutsideTool", definition.startSplit, definition.startOutsideTrim, definition.insidePoint, false) : qNothing();
        const endInsideTool = hasEnd ? trimmedTool(context, id + "endInsideTool", definition.endSplit, definition.endInsideTrim, definition.insidePoint, true) : qNothing();
        const endOutsideTool = hasEnd ? trimmedTool(context, id + "endOutsideTool", definition.endSplit, definition.endOutsideTrim, definition.insidePoint, false) : qNothing();
        // With one split it is the only tool: Split+ names its cut "startCut" whichever end it is.
        const firstInsideTool = hasStart ? startInsideTool : endInsideTool;
        const secondInsideTool = (hasStart && hasEnd) ? endInsideTool : qNothing();
        const firstOutsideTool = hasStart ? startOutsideTool : endOutsideTool;
        const secondOutsideTool = (hasStart && hasEnd) ? endOutsideTool : qNothing();

        // 1. Offsets (Offset+: positive toward the inside point, 0 a copy).
        const inside = offsetProfile(context, id + "insideOffset", definition.insideProfile, definition.insideOffset, definition.insidePoint);

        // 2. Splits (Split+ with the inside point; the split tools are the user's and are kept).
        const insideSplit = splitProfileAtSeams(context, id + "insideSplit", inside, firstInsideTool, secondInsideTool, definition.insidePoint, SplitPlusKeep.INSIDE);
        var insideStart = qNothing();
        var insideEnd = qNothing();
        if (hasStart && hasEnd)
        {
            insideStart = insideSplit.startCut;
            insideEnd = insideSplit.endCut;
        }
        else if (hasStart)
        {
            insideStart = insideSplit.startCut;
        }
        else
        {
            insideEnd = insideSplit.startCut;
        }
        const insideFaces = frozenAndTracked(context, qOwnedByBody(insideSplit.inside, EntityType.FACE));

        var outsideStart = qNothing();
        var outsideEnd = qNothing();
        var startOutsideFaces = qNothing();
        var endOutsideFaces = qNothing();
        if (same)
        {
            const outside = offsetProfile(context, id + "outsideOffset", definition.outsideProfile, definition.outsideOffset, definition.insidePoint);
            const outsideSplit = splitProfileAtSeams(context, id + "outsideSplit", outside, firstOutsideTool, secondOutsideTool, definition.insidePoint, SplitPlusKeep.OUTSIDE);
            if (hasStart && hasEnd)
            {
                outsideStart = outsideSplit.startCut;
                outsideEnd = outsideSplit.endCut;
                startOutsideFaces = frozenAndTracked(context, qOwnedByBody(outsideSplit.start, EntityType.FACE));
                endOutsideFaces = frozenAndTracked(context, qOwnedByBody(outsideSplit.end, EntityType.FACE));
            }
            else if (hasStart)
            {
                outsideStart = outsideSplit.startCut;
                startOutsideFaces = frozenAndTracked(context, qOwnedByBody(outsideSplit.start, EntityType.FACE));
            }
            else
            {
                outsideEnd = outsideSplit.startCut;
                endOutsideFaces = frozenAndTracked(context, qOwnedByBody(outsideSplit.start, EntityType.FACE));
            }
        }
        else
        {
            // One tool each: its cut is Split+'s startCut, the piece beyond it Split+'s start region.
            if (hasStart)
            {
                const startOut = offsetProfile(context, id + "startOffset", definition.startProfile, definition.startOffset, definition.insidePoint);
                const startSplit = splitProfileAtSeams(context, id + "startSplit", startOut, startOutsideTool, qNothing(), definition.insidePoint, SplitPlusKeep.OUTSIDE);
                outsideStart = startSplit.startCut;
                startOutsideFaces = frozenAndTracked(context, qOwnedByBody(startSplit.start, EntityType.FACE));
            }
            if (hasEnd)
            {
                const endOut = offsetProfile(context, id + "endOffset", definition.endProfile, definition.endOffset, definition.insidePoint);
                const endSplit = splitProfileAtSeams(context, id + "endSplit", endOut, endOutsideTool, qNothing(), definition.insidePoint, SplitPlusKeep.OUTSIDE);
                outsideEnd = endSplit.startCut;
                endOutsideFaces = frozenAndTracked(context, qOwnedByBody(endSplit.start, EntityType.FACE));
            }
        }
        if (hasStart)
        {
            verifyCut(context, insideStart, "start", "inside");
            verifyCut(context, outsideStart, "start", "outside");
            insideStart = withoutSlivers(context, insideStart);
            outsideStart = withoutSlivers(context, outsideStart);
        }
        if (hasEnd)
        {
            verifyCut(context, insideEnd, "end", "inside");
            verifyCut(context, outsideEnd, "end", "outside");
            insideEnd = withoutSlivers(context, insideEnd);
            outsideEnd = withoutSlivers(context, outsideEnd);
        }

        // The fillets must fit in the gap (checked before anything is built), and the gap is published.
        var startGap = 0 * meter;
        var endGap = 0 * meter;
        if (hasStart)
        {
            startGap = connectionGap(context, insideStart, outsideStart);
            if (definition.startConnection == JoinConnection.FILLETS)
            {
                checkFilletsFit(context, insideStart, outsideStart, definition.startInsideRadius, definition.startOutsideRadius, "start");
            }
        }
        if (hasEnd)
        {
            endGap = connectionGap(context, insideEnd, outsideEnd);
            if (definition.endConnection == JoinConnection.FILLETS)
            {
                checkFilletsFit(context, insideEnd, outsideEnd, definition.endInsideRadius, definition.endOutsideRadius, "end");
            }
        }

        // 3. A loft between the paired cut edges at each split given (ruled, or matching the tangent / curvature of the two surfaces).
        var startLoftFaces = qNothing();
        var endLoftFaces = qNothing();
        var joints = { "startInside" : qNothing(), "startOutside" : qNothing(), "endInside" : qNothing(), "endOutside" : qNothing() };
        if (hasStart)
        {
            opLoft(context, id + "startLoft", connectionLoft(context, insideStart, outsideStart, definition.startConnection, definition.startMagnitude));
            startLoftFaces = frozenAndTracked(context, qCreatedBy(id + "startLoft", EntityType.FACE));
            joints.startInside = frozenAndTracked(context, insideStart);
            joints.startOutside = frozenAndTracked(context, outsideStart);
        }
        if (hasEnd)
        {
            opLoft(context, id + "endLoft", connectionLoft(context, insideEnd, outsideEnd, definition.endConnection, definition.endMagnitude));
            endLoftFaces = frozenAndTracked(context, qCreatedBy(id + "endLoft", EntityType.FACE));
            joints.endInside = frozenAndTracked(context, insideEnd);
            joints.endOutside = frozenAndTracked(context, outsideEnd);
        }

        // Every body this feature made (the offsets' kept pieces and the lofts).
        const pieces = qUnion([qOwnerBody(insideFaces), qOwnerBody(startOutsideFaces), qOwnerBody(endOutsideFaces),
                    qCreatedBy(id + "startLoft", EntityType.BODY), qCreatedBy(id + "endLoft", EntityType.BODY)]);
        // 4. Merge (always: joining is the point), then the fillets on the merged joint edges.
        var mergeFailed = false;
        var mergeError = "";
        try silent
        {
            opBoolean(context, id + "merge", { "tools" : qUnion(evaluateQuery(context, pieces)), "operationType" : BooleanOperationType.UNION });
        }
        catch (error)
        {
            mergeFailed = true;
            mergeError = toString(error);
        }
        const result = qUnion(evaluateQuery(context, qOwnerBody(qUnion([insideFaces, startOutsideFaces, endOutsideFaces, startLoftFaces, endLoftFaces]))));
        const jointEdge = function(key is string) returns Query
            {
                return qIntersection([qEntityFilter(joints[key], EntityType.EDGE), qOwnedByBody(result, EntityType.EDGE)]);
            };
        // Every joint must have closed: its edge now shared by the loft and the surface it joins.
        var openJoints = [];
        for (var joint in [["startInside", "start loft to the inside surface", hasStart], ["startOutside", "start loft to the outside surface", hasStart],
                           ["endInside", "end loft to the inside surface", hasEnd], ["endOutside", "end loft to the outside surface", hasEnd]])
        {
            if (joint[2] && isQueryEmpty(context, qEdgeTopologyFilter(jointEdge(joint[0]), EdgeTopology.TWO_SIDED)))
            {
                openJoints = append(openJoints, joint[1]);
            }
        }
        if (mergeFailed || size(evaluateQuery(context, result)) != 1 || size(openJoints) > 0)
        {
            throw regenError("The pieces did not join into one surface" ~ (size(openJoints) > 0 ? ": open at the " ~ join(openJoints, ", ") : "")
                    ~ (mergeFailed ? " (the merge failed: " ~ mergeError ~ ")" : "")
                    ~ ". A loft edge does not meet its cut edge -- check that the profiles reach the splits cleanly.", ["startSplit"]);
        }
        if (definition.startFillet && startRuled)
        {
            filletJoint(context, id + "startFillet", jointEdge("startInside"), jointEdge("startOutside"), definition.startFilletEdge,
                definition.startRadius, definition.startKeepOpposite, "start");
        }
        if (definition.endFillet && endRuled)
        {
            filletJoint(context, id + "endFillet", jointEdge("endInside"), jointEdge("endOutside"), definition.endFilletEdge,
                definition.endRadius, definition.endKeepOpposite, "end");
        }
        // Fillets and a straight section: an arc at each end of the strip (tangent to the surface and to the strip)
        for (var endInfo in [["start", hasStart], ["end", hasEnd]])
        {
            if (endInfo[1] && definition[endInfo[0] ~ "Connection"] == JoinConnection.FILLETS)
            {
                const radiusInside = definition[endInfo[0] ~ "InsideRadius"];
                const radiusOutside = definition[endInfo[0] ~ "OutsideRadius"];
                if (radiusInside > 0 * meter)
                {
                    opFillet(context, id + (endInfo[0] ~ "FilletInside"), { "entities" : jointEdge(endInfo[0] ~ "Inside"), "radius" : radiusInside });
                }
                if (radiusOutside > 0 * meter)
                {
                    opFillet(context, id + (endInfo[0] ~ "FilletOutside"), { "entities" : jointEdge(endInfo[0] ~ "Outside"), "radius" : radiusOutside });
                }
            }
        }

        if (!definition.keepInputs)
        {
            const inputBodies = qConstructionFilter(qBodyType(qUnion(inputs), BodyType.SHEET), ConstructionObject.NO);
            if (!isQueryEmpty(context, inputBodies))
            {
                opDeleteBodies(context, id + "deleteInputs", { "entities" : inputBodies });
            }
        }

        if (definition.outputName != "")
        {
            setProperty(context, { "entities" : result, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
        }

        // every fillet face made at each end: the old single fillet (startFillet / endFillet) and the two arcs of the new connection
        const startFilletFaces = qUnion([qCreatedBy(id + "startFillet", EntityType.FACE), qCreatedBy(id + "startFilletInside", EntityType.FACE), qCreatedBy(id + "startFilletOutside", EntityType.FACE)]);
        const endFilletFaces = qUnion([qCreatedBy(id + "endFillet", EntityType.FACE), qCreatedBy(id + "endFilletInside", EntityType.FACE), qCreatedBy(id + "endFilletOutside", EntityType.FACE)]);
        const faceKey = function(q is Query, description is string) returns map
            {
                return extractableQuery(qIntersection([qEntityFilter(q, EntityType.FACE), qOwnedByBody(result, EntityType.FACE)]), description, DebugColor.CYAN);
            };
        embedStandardOutputs(context, id, settledOutputs(context, {
                    "output" : result,
                    "outputDescription" : "The joined surface",
                    "inputs" : qUnion(concatenateArrays([inputs, [definition.startSplit, definition.endSplit]])),
                    "variables" : {
                        "startGap" : extractableVariable(startGap, "The shortest distance between the start cuts (the width of the start connection before fillets)."),
                        "endGap" : extractableVariable(endGap, "The shortest distance between the end cuts.")
                    },
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
                        "startConnectionFaces" : faceKey(qUnion([startLoftFaces, startFilletFaces]), "Every face that bridges the inside to the outside surface at the start (the strip and its fillet arcs)."),
                        "endConnectionFaces" : faceKey(qUnion([endLoftFaces, endFilletFaces]), "Every face that bridges the inside to the outside surface at the end (the strip and its fillet arcs)."),
                        "startFilletFaces" : faceKey(startFilletFaces, "The fillet faces at the start split (the fillet, or the arcs of a fillets-and-straight connection)."),
                        "endFilletFaces" : faceKey(endFilletFaces, "The fillet faces at the end split."),
                        "startFilletInside" : faceKey(qCreatedBy(id + "startFilletInside", EntityType.FACE), "The arc that meets the inside surface at the start (fillets and straight section)."),
                        "startFilletOutside" : faceKey(qCreatedBy(id + "startFilletOutside", EntityType.FACE), "The arc that meets the outside surface at the start (fillets and straight section)."),
                        "endFilletInside" : faceKey(qCreatedBy(id + "endFilletInside", EntityType.FACE), "The arc that meets the inside surface at the end."),
                        "endFilletOutside" : faceKey(qCreatedBy(id + "endFilletOutside", EntityType.FACE), "The arc that meets the outside surface at the end."),
                        "boundaryEdges" : extractableQuery(qEdgeTopologyFilter(qOwnedByBody(result, EntityType.EDGE), EdgeTopology.ONE_SIDED),
                            "The open edges of the result.", DebugColor.CYAN)
                    }
                }));
    }, {
        "insideOffset" : 0 * meter,
        "outsideMode" : JoinOutsideMode.SAME,
        "outsideProfile" : qNothing(),
        "outsideOffset" : 0 * meter,
        "startProfile" : qNothing(),
        "startOffset" : 0 * meter,
        "endProfile" : qNothing(),
        "endOffset" : 0 * meter,
        "outputName" : "",
        "startFillet" : false,
        "endFillet" : false,
        "startInsideTrim" : 0 * meter, "startOutsideTrim" : 0 * meter, "startConnection" : JoinConnection.RULED, "startMagnitude" : 1,
        "startInsideRadius" : 0 * meter, "startOutsideRadius" : 0 * meter,
        "endInsideTrim" : 0 * meter, "endOutsideTrim" : 0 * meter, "endConnection" : JoinConnection.RULED, "endMagnitude" : 1,
        "endInsideRadius" : 0 * meter, "endOutsideRadius" : 0 * meter,
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

/**
 * A split tool moved for a trim: the user's tool when the distance is 0; otherwise a copy moved by `distance` normal to a plane
 * (a mate connector's XY plane, a planar face) or offset from a surface, toward the inside point (towardInside) or away from it.
 */
function trimmedTool(context is Context, id is Id, tool is Query, distance is ValueWithUnits, insidePoint is Query, towardInside is boolean) returns Query
{
    if (abs(distance) < 1e-9 * meter)
    {
        return tool;
    }
    const point = insidePointOf(context, insidePoint);
    const cutPlane = toolPlane(context, tool);
    if (cutPlane != undefined)
    {
        // +1: the inside point is on the plane's normal side
        const toward = dot(point - cutPlane.origin, cutPlane.normal) >= 0 * meter ? 1 : -1;
        const shifted = plane(cutPlane.origin + cutPlane.normal * ((towardInside ? toward : -toward) * distance), cutPlane.normal, cutPlane.x);
        opPlane(context, id, { "plane" : shifted });
        return qCreatedBy(id, EntityType.BODY);
    }
    const faces = qUnion([qEntityFilter(tool, EntityType.FACE), qOwnedByBody(qEntityFilter(tool, EntityType.BODY), EntityType.FACE)]);
    const faceList = evaluateQuery(context, faces);
    if (size(faceList) == 0)
    {
        throw regenError("A trim needs a split that is a surface, a face, a plane or a mate connector.", ["startSplit", "endSplit"]);
    }
    const near = evDistance(context, { "side0" : point, "side1" : faces });
    const normal = evFaceTangentPlane(context, { "face" : faceList[near.sides[1].index], "parameter" : near.sides[1].parameter }).normal;
    const toward = dot(point - near.sides[1].point, normal) >= 0 * meter ? 1 : -1;
    opExtractSurface(context, id, { "faces" : faces, "offset" : (towardInside ? toward : -toward) * distance, "tangentPropagation" : false });
    return qCreatedBy(id, EntityType.BODY);
}

/** The shortest distance between the two cut edges of a connection. */
function connectionGap(context is Context, insideCut is Query, outsideCut is Query) returns ValueWithUnits
{
    return evDistance(context, { "side0" : insideCut, "side1" : outsideCut }).distance;
}

/** The profiles and end conditions of the loft that joins two cuts: ruled, or matching the tangent / curvature of the surfaces. */
function connectionLoft(context is Context, insideCut is Query, outsideCut is Query, connection is JoinConnection, magnitude is number) returns map
{
    var loftDefinition = { "profileSubqueries" : [insideCut, outsideCut], "bodyType" : ToolBodyType.SURFACE };
    if (connection == JoinConnection.TANGENT || connection == JoinConnection.CURVATURE)
    {
        const condition = connection == JoinConnection.TANGENT ? LoftEndDerivativeType.MATCH_TANGENT : LoftEndDerivativeType.MATCH_CURVATURE;
        loftDefinition.derivativeInfo = [
                createProfileConditions(context, condition, insideCut, 0, magnitude, qNothing(), qNothing()),
                createProfileConditions(context, condition, outsideCut, 1, magnitude, qNothing(), qNothing())
            ];
    }
    return loftDefinition;
}

/**
 * The direction pointing into the face that carries a cut edge, at a point of that edge (tangent to the face, normal to the edge).
 */
function intoFace(context is Context, cut is Query, point is Vector) returns Vector
{
    const face = qAdjacent(cut, AdjacencyType.EDGE, EntityType.FACE);
    const nearest = evDistance(context, { "side0" : point, "side1" : cut });
    const edgeLine = evEdgeTangentLine(context, { "edge" : evaluateQuery(context, cut)[nearest.sides[1].index], "parameter" : nearest.sides[1].parameter });
    const onFace = evDistance(context, { "side0" : point, "side1" : face });
    const normal = evFaceTangentPlane(context, { "face" : face, "parameter" : onFace.sides[1].parameter }).normal;
    const across = normalize(cross(normal, edgeLine.direction));
    // the sign that stays on the face
    const a = evDistance(context, { "side0" : point + across * 0.2 * millimeter, "side1" : face }).distance;
    const b = evDistance(context, { "side0" : point - across * 0.2 * millimeter, "side1" : face }).distance;
    return a <= b ? across : -across;
}

/**
 * Fillets and a straight section: the arc at each end of the strip has tangent length R tan(turn / 2), where the turn is the
 * angle between the surface and the strip. The two must fit in the strip. At five points along the cut the turns are measured; if
 * they do not fit, the error gives the largest radii (both scaled alike) that do.
 */
function checkFilletsFit(context is Context, insideCut is Query, outsideCut is Query, radiusInside is ValueWithUnits, radiusOutside is ValueWithUnits, where is string)
{
    var scale = inf;
    var narrowest = inf * meter;
    var samples = [];
    for (var cutEdge in evaluateQuery(context, insideCut))
    {
        samples = concatenateArrays([samples, evEdgeTangentLines(context, { "edge" : cutEdge, "parameters" : [0, 0.25, 0.5, 0.75, 1] })]);
    }
    for (var line in samples)
    {
        const across = evDistance(context, { "side0" : line.origin, "side1" : outsideCut });
        const chord = across.sides[1].point - line.origin;
        const gap = norm(chord);
        narrowest = min(narrowest, gap);
        if (gap < 1e-6 * meter)
        {
            throw regenError("The " ~ where ~ " connection has no gap for fillets and a straight section: give the inside trim or the outside trim.", [where ~ "InsideTrim"]);
        }
        const heading = chord / gap;
        // the inside surface arrives at the strip heading away from itself; the outside surface leaves it heading into itself
        const turnInside = acos(clamp(dot(-intoFace(context, insideCut, line.origin), heading), -1, 1));
        const turnOutside = acos(clamp(dot(heading, intoFace(context, outsideCut, across.sides[1].point)), -1, 1));
        const need = (radiusInside > 0 * meter ? radiusInside * tan(turnInside / 2) : 0 * meter)
            + (radiusOutside > 0 * meter ? radiusOutside * tan(turnOutside / 2) : 0 * meter);
        if (need > 1e-9 * meter)
        {
            scale = min(scale, gap / need);
        }
    }
    if (scale < 1)
    {
        throw regenError("The " ~ where ~ " fillets do not fit in the strip (it is " ~ roundToPrecision(narrowest / millimeter, 2)
                ~ " mm wide at its narrowest). Largest radii that fit: inside " ~ roundToPrecision(radiusInside * scale / millimeter, 2)
                ~ " mm, outside " ~ roundToPrecision(radiusOutside * scale / millimeter, 2) ~ " mm. Or widen the gap with a trim.", [where ~ "InsideRadius"]);
    }
}
/** The entities of a query now, followed through later edits. */
function frozenAndTracked(context is Context, q is Query) returns Query
{
    return qUnion([qUnion(evaluateQuery(context, q)), startTracking(context, q)]);
}

/**
 * A split tool this close to an edge of a profile counts as lying ON it. Onshape's modelling tolerance
 * is 0.01 mm; a cut closer than that to a seam leaves sliver faces (2026-10-06: a mate connector 0.0025 mm
 * and 0.0083 mm off the inside profile's seams cut slivers of 0.04 and 0.15 mm2) or, exactly on a seam,
 * creates no cut edge at all, and the joints then never close.
 */
const SEAM_TOLERANCE = 0.05 * millimeter;

/**
 * splitProfile, except where a split tool lies on an edge the profile already has (the bump stations of a
 * profile built in pieces are such seams). Cutting there makes no edge, or a sliver, so the seam itself is
 * the cut: every face is placed on its side of the tools, each kept region is extracted as its own sheet body,
 * and the cut is that body's one-sided edge in the tool. A split that is not on a seam goes through the
 * ordinary opSplitPart. Returns the same keys as splitProfile (inside, start, end, startCut, endCut).
 *
 * Seam handling needs every tool to be a plane (mate connector, planar face or plane): a tool of another shape on a
 * seam is an error that names it.
 */
function splitProfileAtSeams(context is Context, id is Id, target is Query, startTool is Query, endTool is Query,
    insidePoint is Query, keep is SplitPlusKeep) returns map
{
    const tools = isQueryEmpty(context, endTool) ? [startTool] : [startTool, endTool];
    const edges = evaluateQuery(context, qOwnedByBody(target, EntityType.EDGE));
    var planes = [];
    var onSeam = [];
    for (var tool in tools)
    {
        const cutPlane = toolPlane(context, tool);
        var found = false;
        for (var edge in edges)
        {
            if (edgeLiesOnTool(context, edge, tool, cutPlane))
            {
                found = true;
                break;
            }
        }
        planes = append(planes, cutPlane);
        onSeam = append(onSeam, found);
    }
    if (!isIn(true, onSeam))
    {
        return splitProfile(context, id, target, startTool, endTool, insidePoint, keep);
    }
    for (var cutPlane in planes)
    {
        if (cutPlane == undefined)
        {
            throw regenError("A split lies on an existing edge of a profile, and it is not a plane or mate connector, so it cannot be applied there. "
                ~ "Move the split off the edge (more than " ~ roundToPrecision(SEAM_TOLERANCE / millimeter, 2) ~ " mm) or use a mate connector or plane.", ["startSplit", "endSplit"]);
        }
    }

    // Which side of each tool is inside.
    const point = insidePointOf(context, insidePoint);
    var insideSigns = [];
    for (var cutPlane in planes)
    {
        insideSigns = append(insideSigns, sideOfPlane(point, cutPlane));
    }

    // Splits only where the tool is not on a seam; every piece is kept.
    var pieces = qUnion([target, startTracking(context, target)]);
    var tempPlanes = [];
    for (var i = 0; i < size(tools); i += 1)
    {
        if (onSeam[i])
        {
            continue;
        }
        const planeId = id + ("plane" ~ i);
        opPlane(context, planeId, { "plane" : planes[i] });
        tempPlanes = append(tempPlanes, qCreatedBy(planeId, EntityType.BODY));
        const splitId = id + ("split" ~ i);
        opSplitPart(context, splitId, {
                    "targets" : pieces,
                    "tool" : qCreatedBy(planeId, EntityType.FACE),
                    "keepTools" : true,
                    "keepType" : SplitOperationKeepType.KEEP_ALL
                });
        pieces = qUnion([pieces, qCreatedBy(splitId, EntityType.BODY)]);
    }

    // Every face in its region: inside, beyond the start tool, beyond the end tool (beyond both: dropped).
    const pieceBodies = qUnion(evaluateQuery(context, pieces));
    var regionFaces = { "inside" : [], "start" : [], "end" : [] };
    for (var face in evaluateQuery(context, qOwnedByBody(pieceBodies, EntityType.FACE)))
    {
        const centroid = evApproximateCentroid(context, { "entities" : face });
        const onFace = evDistance(context, { "side0" : centroid, "side1" : face }).sides[1].point;
        var beyond = [];
        for (var i = 0; i < size(planes); i += 1)
        {
            beyond = append(beyond, sideOfPlane(onFace, planes[i]) != insideSigns[i]);
        }
        if (size(planes) == 2 && beyond[0] && beyond[1])
        {
            continue;
        }
        const region = beyond[0] ? "start" : ((size(planes) == 2 && beyond[1]) ? "end" : "inside");
        regionFaces[region] = append(regionFaces[region], face);
    }

    // Each kept region becomes its own sheet body.
    var result = { "inside" : qNothing(), "start" : qNothing(), "end" : qNothing(), "startCut" : qNothing(), "endCut" : qNothing() };
    for (var region in ["inside", "start", "end"])
    {
        const wanted = region == "inside" ? keep != SplitPlusKeep.OUTSIDE : keep != SplitPlusKeep.INSIDE;
        if (!wanted || size(regionFaces[region]) == 0)
        {
            continue;
        }
        const extractId = id + ("extract" ~ region);
        opExtractSurface(context, extractId, { "faces" : qUnion(regionFaces[region]), "tangentPropagation" : false });
        result[region] = qCreatedBy(extractId, EntityType.BODY);
    }

    // The cut of each tool: the kept piece's one-sided edge(s) in the tool (the inside piece, or the outside one when keeping outside).
    for (var k = 0; k < size(tools); k += 1)
    {
        const cutBody = keep == SplitPlusKeep.OUTSIDE ? result[k == 0 ? "start" : "end"] : result.inside;
        var cutEdges = [];
        for (var edge in evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(cutBody, EntityType.EDGE), EdgeTopology.ONE_SIDED)))
        {
            if (edgeLiesOnTool(context, edge, tools[k], planes[k]))
            {
                cutEdges = append(cutEdges, edge);
            }
        }
        result[k == 0 ? "startCut" : "endCut"] = qUnion(cutEdges);
    }

    opDeleteBodies(context, id + "deletePieces", { "entities" : qUnion(concatenateArrays([evaluateQuery(context, pieceBodies), tempPlanes])) });
    return result;
}

/** The plane a split tool cuts on: a mate connector's XY plane, or one planar face / plane. undefined for any other tool. */
function toolPlane(context is Context, tool is Query)
{
    if (!isQueryEmpty(context, qBodyType(tool, BodyType.MATE_CONNECTOR)))
    {
        return plane(evMateConnector(context, { "mateConnector" : tool }));
    }
    const faces = evaluateQuery(context, qUnion([qEntityFilter(tool, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(tool, EntityType.BODY), EntityType.FACE)]));
    if (size(faces) != 1)
    {
        return undefined;
    }
    var result = undefined;
    try silent
    {
        result = evPlane(context, { "face" : faces[0] });
    }
    return result;
}

/** Distance from a point to a tool: to its plane when it has one, else to the tool itself. */
function distanceToTool(context is Context, point is Vector, tool is Query, cutPlane) returns ValueWithUnits
{
    if (cutPlane != undefined)
    {
        return abs(dot(point - cutPlane.origin, cutPlane.normal));
    }
    return evDistance(context, { "side0" : point, "side1" : tool }).distance;
}

/**
 * An edge lies on a tool when five points along it are all within SEAM_TOLERANCE of it AND, for a plane tool, it runs along the
 * plane: a micro edge only 0.006 mm long that crosses the plane has all its points within the tolerance too, but it is a piece of
 * the profile's boundary, not a cut (2026-10-21: two such edges, each side of a seam 0.006 mm off the split, were taken for cut
 * edges, and a loft from three edges to three made three faces, two of them slivers, where one was meant).
 */
function edgeLiesOnTool(context is Context, edge is Query, tool is Query, cutPlane) returns boolean
{
    const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 0.25, 0.5, 0.75, 1] });
    for (var line in lines)
    {
        if (distanceToTool(context, line.origin, tool, cutPlane) > SEAM_TOLERANCE)
        {
            return false;
        }
    }
    if (cutPlane != undefined)
    {
        const across = abs(dot(lines[size(lines) - 1].origin - lines[0].origin, cutPlane.normal));
        const along = norm(lines[size(lines) - 1].origin - lines[0].origin);
        // mostly across the plane (and not a closed edge, whose ends meet): it crosses the tool instead of lying on it
        if (along > 1e-9 * meter && across > 0.5 * along)
        {
            return false;
        }
    }
    return true;
}

/**
 * A cut's edges without its slivers: edges shorter than twice the seam tolerance are dropped so that each loft profile is the
 * cut itself, not the cut plus the bits of the profile's boundary that happen to lie within the tolerance of the tool. (A loft
 * pairs the edges of its two profiles one to one: an extra micro edge becomes a sliver face.) The cut as it was when nothing
 * longer is left.
 */
function withoutSlivers(context is Context, cut is Query) returns Query
{
    var kept = [];
    for (var edge in evaluateQuery(context, cut))
    {
        if (evLength(context, { "entities" : edge }) > 2 * SEAM_TOLERANCE)
        {
            kept = append(kept, edge);
        }
    }
    return size(kept) == 0 ? cut : qUnion(kept);
}

/** Which side of a plane a point is on: 1 in front, -1 behind (and on it). */
function sideOfPlane(point is Vector, cutPlane is Plane) returns number
{
    return dot(point - cutPlane.origin, cutPlane.normal) > 0 * meter ? 1 : -1;
}

/** The inside point's position: a mate connector's origin, or a vertex. */
function insidePointOf(context is Context, reference is Query) returns Vector
{
    if (!isQueryEmpty(context, qBodyType(reference, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : reference }).origin;
    }
    return evVertexPoint(context, { "vertex" : qEntityFilter(reference, EntityType.VERTEX) });
}

