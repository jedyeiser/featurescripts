FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// The boolean step's enums (NewBodyOperationType) are parameter types here, so they must be exported (as std Thicken does).
export import(path : "onshape/std/tool.fs", version : "3083.0");
// opExtendSheetBody's enums (the drafted build grows copies of the faces); not part of common.fs
import(path : "onshape/std/extendendtype.gen.fs", version : "3083.0");
import(path : "onshape/std/extendsheetshapetype.gen.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: thicken_plus_icon.svg (feature icon)
IconNamespace::import(path : "b485aa90a94179256b9b5aca", version : "95d6a7e0281857d46f768b63");

/**
 * Thicken+: std Thicken, with the two thicknesses named by a reference instead of by a face normal.
 *
 * "Toward reference" is thickened on the side of each surface the reference is on, "Away from reference" on
 * the other -- decided per surface body (the reference's signed distance to it, as Offset+ and Mutual Trim+),
 * so surfaces of opposite orientation, or ones whose normal flips upstream, still thicken the same way in the
 * model. Without a reference, "toward" is along each face's normal (std Thickness 1).
 *
 * Everything else is std Thicken: opThicken per surface body, then the std boolean step (New / Add / Remove /
 * Intersect, merge scope, merge with all) unchanged.
 *
 * Curvature check (default on): before thickening, each face is sampled for a concave radius smaller than the
 * thickness on that side -- the usual reason a thicken fails -- and the first such place is reported with its
 * location and radius, instead of the kernel's bare failure.
 *
 * Publishes (Extract variables): output (the thickened bodies), towardFaces (the face on the reference side),
 * awayFaces (the face on the other side; on the input surface when Away is 0), sideFaces. Each is tracked
 * through the boolean, so it still resolves when the bodies merge into a part.
 */

/** Which cap faces Thicken+ deletes after the thicken, leaving an open sheet (the periphery surface). */
export enum ThickenPlusDeleteCaps
{
    annotation { "Name" : "None" }
    NONE,
    annotation { "Name" : "Toward reference" }
    TOWARD,
    annotation { "Name" : "Away from reference" }
    AWAY,
    annotation { "Name" : "Both" }
    BOTH
}

/** Where the draft angle is measured from: the surface whose outline does not move. */
export enum ThickenPlusDraftNeutral
{
    annotation { "Name" : "Input face" }
    SEED,
    annotation { "Name" : "Toward reference cap" }
    TOP,
    annotation { "Name" : "Away from reference cap" }
    BOTTOM
}

/** Which of the boundary edges (one-sided for the thickened faces) get the draft. */
export enum ThickenPlusDraftEdges
{
    annotation { "Name" : "All boundary edges" }
    ALL,
    annotation { "Name" : "Only selected edges" }
    SELECTED,
    annotation { "Name" : "All except selected edges" }
    ALL_EXCEPT
}

const THICKEN_DRAFT_BOUNDS = { (degree) : [-89, 0, 89], (radian) : 0 } as AngleBoundSpec;

/** Face samples per parameter direction for the curvature check. */
const CURVATURE_SAMPLES = 9;

/** Samples along each boundary edge for the draft walls, and extension steps past an unlinked end. */
const DRAFT_SAMPLES = 20;
const DRAFT_EXTENSION_STEPS = 4;
/** Two boundary edges meeting at a vertex are one chain (one cutter, no extension) when their tangents agree within 15 degrees. */
const DRAFT_TANGENT_COS = 0.966;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Thicken+",
        "Feature Type Description" : "Thicken surfaces toward and away from a reference, instead of along whichever way their normals point. Otherwise std Thicken, boolean included.",
        "Filter Selector" : "allparts" }
export const thickenPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        booleanStepTypePredicate(definition);

        annotation { "Name" : "Faces and surfaces to thicken",
                    "Filter" : (EntityType.FACE || (BodyType.SHEET && EntityType.BODY && SketchObject.NO)) && ConstructionObject.NO }
        definition.entities is Query;

        annotation { "Name" : "Side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1,
                    "Description" : "Geometry on the side to thicken toward: best a mate connector or vertex near the surface, on the side meant. Empty = along the face normals." }
        definition.sideReference is Query;

        annotation { "Name" : "Toward reference", "Description" : "Thickness on the side the reference is on (along the normal without a reference)." }
        isLength(definition.thicknessToward, ZERO_INCLUSIVE_OFFSET_BOUNDS);

        annotation { "Name" : "Away from reference", "Description" : "Thickness on the other side." }
        isLength(definition.thicknessAway, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Swap sides", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                    "Description" : "Exchange the two thicknesses." }
        definition.swapSides is boolean;

        annotation { "Name" : "Keep tools", "Default" : false }
        definition.keepTools is boolean;

        if (definition.operationType == NewBodyOperationType.NEW)
        {
            annotation { "Name" : "Output name", "Description" : "Optional. Names the new bodies; leave empty for the default names." }
            definition.outputName is string;

            annotation { "Name" : "Delete caps", "Default" : ThickenPlusDeleteCaps.NONE,
                        "Description" : "Delete the cap face(s) after the thicken. The result is an open sheet: with both caps gone, the periphery (side faces) of the input, usable for trimming." }
            definition.deleteCaps is ThickenPlusDeleteCaps;
        }

        booleanStepScopePredicate(definition);

        annotation { "Group Name" : "Draft and side faces", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Draft angle",
                        "Description" : "Positive: the walls widen going toward the reference (the part releases toward it). Measured from the surface normal at the boundary. 0 = no draft." }
            isAngle(definition.draftAngle, THICKEN_DRAFT_BOUNDS);

            annotation { "Name" : "Same angle away from reference", "Default" : true,
                        "Description" : "One continuous wall through the neutral surface. Off: give the away side its own angle (same sign convention), which kinks the wall at the neutral surface." }
            definition.shareDraftAngle is boolean;

            if (!definition.shareDraftAngle)
            {
                annotation { "Name" : "Draft angle away from reference" }
                isAngle(definition.draftAngleAway, THICKEN_DRAFT_BOUNDS);
            }

            annotation { "Name" : "Neutral surface", "Description" : "The outline that stays where it is; the walls tilt about it." }
            definition.draftNeutral is ThickenPlusDraftNeutral;

            annotation { "Name" : "Edges to draft" }
            definition.draftEdgeMode is ThickenPlusDraftEdges;

            if (definition.draftEdgeMode != ThickenPlusDraftEdges.ALL)
            {
                annotation { "Name" : "Edges", "Filter" : EntityType.EDGE && ConstructionObject.NO,
                            "Description" : "Edges on the outline of the thickened faces: one-sided with respect to them (an edge shared by two selected faces has no wall)." }
                definition.draftEdges is Query;
            }

            annotation { "Name" : "Side face offset",
                        "Description" : "Move the side faces (all of them, drafted or not) outward (+) or inward (-) by this much, measured at the neutral surface: the same thicken, but the part comes out a little larger or smaller than the face. 0 = off." }
            isLength(definition.sideOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }

        annotation { "Group Name" : "Advanced", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Check curvature", "Default" : true,
                        "Description" : "Before thickening, look for a concave radius smaller than the thickness on its side and report where it is." }
            definition.checkCurvature is boolean;

            annotation { "Name" : "Print details", "Default" : false, "Description" : "The side each surface went and the tightest concave radius on each side." }
            definition.debugPrint is boolean;
        }
    }
    {
        verifyNoMesh(context, definition, "entities");
        const probe = referenceProbe(context, definition.sideReference);
        const toward = definition.swapSides ? definition.thicknessAway : definition.thicknessToward;
        const away = definition.swapSides ? definition.thicknessToward : definition.thicknessAway;
        if (toward + away <= 0 * meter)
        {
            throw regenError("Both thicknesses are zero.", ["thicknessToward"]);
        }

        // Draft: tan of the angle on the toward side and on the away side (the away side's own angle only when not shared).
        const kToward = tan(definition.draftAngle);
        const kAway = definition.shareDraftAngle ? kToward : tan(definition.draftAngleAway);
        // A side face offset takes the same built walls (a constant added to the draft's shift), also without an angle.
        const offsetting = abs(definition.sideOffset) > 1e-9 * meter;
        const angled = abs(kToward) > 1e-9 || abs(kAway) > 1e-9;
        const drafting = angled || offsetting;
        // the edge selection only picks which walls tilt: with no angle it has nothing to pick
        if (angled && definition.draftEdgeMode != ThickenPlusDraftEdges.ALL && isQueryEmpty(context, definition.draftEdges))
        {
            throw regenError("Select the edges to draft (or the edges to leave undrafted).", ["draftEdges"]);
        }

        // One group per owner body: each is thickened on its own side.
        const faces = qUnion([qEntityFilter(definition.entities, EntityType.FACE),
                    qOwnedByBody(qEntityFilter(definition.entities, EntityType.BODY), EntityType.FACE)]);
        const bodies = evaluateQuery(context, qOwnerBody(faces));
        if (size(bodies) == 0)
        {
            throw regenError(ErrorStringEnum.THICKEN_SELECT_ENTITIES, ["entities"]);
        }
        var groups = [];
        for (var i = 0; i < size(bodies); i += 1)
        {
            const groupFaces = qIntersection([faces, qOwnedByBody(bodies[i], EntityType.FACE)]);
            // alongNormal: +1 when the reference is on the side the normal points to
            var alongNormal = 1;
            if (probe != undefined)
            {
                alongNormal = sideSign(context, probe, groupFaces);
                if (alongNormal == 0)
                {
                    throw regenError("The side reference lies on surface " ~ (i + 1) ~ " (or no side of it can be read there); pick something clearly to one side.",
                        ["sideReference"]);
                }
            }
            // the boundary of the face set: edges with exactly one of its faces (topologically one-sided for this feature)
            const oneSided = drafting ? oneSidedEdges(context, groupFaces) : qNothing();
            var drafted = oneSided;
            if (definition.draftEdgeMode == ThickenPlusDraftEdges.SELECTED)
            {
                drafted = qIntersection([oneSided, definition.draftEdges]);
            }
            else if (definition.draftEdgeMode == ThickenPlusDraftEdges.ALL_EXCEPT)
            {
                drafted = qSubtraction(oneSided, definition.draftEdges);
            }
            groups = append(groups, {
                        "faces" : groupFaces,
                        "alongNormal" : alongNormal,
                        // opThicken: thickness1 along the normal, thickness2 against it
                        "thickness1" : alongNormal > 0 ? toward : away,
                        "thickness2" : alongNormal > 0 ? away : toward,
                        "toward" : toward,
                        "away" : away,
                        "oneSided" : oneSided,
                        "drafted" : drafted
                    });
            if (definition.debugPrint)
            {
                println("[thicken+] surface " ~ (i + 1) ~ ": toward the reference is " ~ (alongNormal > 0 ? "along" : "against")
                    ~ " its normal; " ~ fmtMM(toward, 4) ~ " mm toward, " ~ fmtMM(away, 4) ~ " mm away.");
            }
        }

        // Every selected edge must be on the outline of some group's faces.
        if (angled && definition.draftEdgeMode != ThickenPlusDraftEdges.ALL)
        {
            var outlines = [];
            for (var group in groups)
            {
                outlines = append(outlines, group.oneSided);
            }
            const stray = qSubtraction(definition.draftEdges, qUnion(outlines));
            if (!isQueryEmpty(context, stray))
            {
                throw regenError("These edges are not one-sided for the thickened faces (shared by two of them, or not on them at all): only outline edges can be drafted.",
                    ["draftEdges"], stray);
            }
        }

        if (definition.checkCurvature)
        {
            for (var i = 0; i < size(groups); i += 1)
            {
                checkCurvature(context, id, groups[i], i, definition.debugPrint);
            }
        }

        // std Thicken, per group (or the drafted build), then its boolean step unchanged. Returns per group the Toward
        // and Away cap faces of a drafted build (undefined for a plain thicken, whose caps are read from opThicken).
        const remainingTransform = getRemainderPatternTransform(context, { "references" : definition.entities });
        const draftSettings = {
                "kToward" : kToward,
                "kAway" : kAway,
                "neutral" : definition.draftNeutral,
                "offset" : definition.sideOffset,
                "keepTools" : definition.keepTools,
                "debug" : definition.debugPrint
            };
        const thickenAll = function(opId is Id) returns array
            {
                var caps = [];
                for (var i = 0; i < size(groups); i += 1)
                {
                    const groupId = opId + ("thicken" ~ i);
                    if (drafting)
                    {
                        caps = append(caps, draftThicken(context, groupId, groups[i], draftSettings));
                    }
                    else
                    {
                        opThicken(context, groupId, {
                                    "entities" : groups[i].faces,
                                    "thickness1" : groups[i].thickness1,
                                    "thickness2" : groups[i].thickness2,
                                    "keepTools" : definition.keepTools
                                });
                        caps = append(caps, undefined);
                    }
                    transformResultIfNecessary(context, groupId, remainingTransform);
                }
                return caps;
            };
        const built = thickenAll(id);

        // Name the faces before the boolean, tracked through it. opThicken makes its START cap on the thickness1 side
        // (along the normal) and its END cap on the thickness2 side, also when that thickness is 0 (the cap then sits
        // on the input); which of the two is Toward is the surface's side of the reference. Two queries name every
        // face -- classifying them one by one by distance cost about 0.8 s on 160 faces and mislabelled side faces
        // that happened to lie one thickness from the input (2026-10-09).
        const frozen = function(q is Query) returns Query
            {
                return qUnion(evaluateQuery(context, q));
            };
        var towardFaces = [];
        var awayFaces = [];
        var sideFaces = [];
        for (var i = 0; i < size(groups); i += 1)
        {
            const thickenId = id + ("thicken" ~ i);
            var towardCap = qNothing();
            var awayCap = qNothing();
            if (built[i] != undefined)
            {
                // drafted build: the caps were tracked through the splits
                towardCap = frozen(built[i].toward);
                awayCap = frozen(built[i].away);
            }
            else
            {
                const startCap = frozen(qCapEntity(thickenId, CapType.START, EntityType.FACE));
                const endCap = frozen(qCapEntity(thickenId, CapType.END, EntityType.FACE));
                towardCap = groups[i].alongNormal > 0 ? startCap : endCap;
                awayCap = groups[i].alongNormal > 0 ? endCap : startCap;
            }
            towardFaces = append(towardFaces, towardCap);
            awayFaces = append(awayFaces, awayCap);
            sideFaces = append(sideFaces, frozen(qSubtraction(qOwnedByBody(qCreatedBy(thickenId, EntityType.BODY), EntityType.FACE), qUnion([towardCap, awayCap]))));
        }
        const tracked = function(q is Query) returns Query
            {
                return qUnion([q, startTracking(context, q)]);
            };
        const outputs = {
                "toward" : tracked(qUnion(towardFaces)),
                "away" : tracked(qUnion(awayFaces)),
                "side" : tracked(qUnion(sideFaces))
            };
        const created = qCreatedBy(id, EntityType.BODY);
        const output = tracked(created);

        processNewBodyIfNeeded(context, id, definition, thickenAll);

        // Delete the chosen caps (New only). leaveOpen: the solid becomes an open sheet, no healing. The tracked cap
        // queries resolve to nothing afterwards, so the published towardFaces / awayFaces are empty for a deleted cap.
        if (definition.operationType == NewBodyOperationType.NEW && definition.deleteCaps != ThickenPlusDeleteCaps.NONE)
        {
            var capFaces = [];
            if (definition.deleteCaps == ThickenPlusDeleteCaps.TOWARD || definition.deleteCaps == ThickenPlusDeleteCaps.BOTH)
            {
                capFaces = append(capFaces, qEntityFilter(outputs.toward, EntityType.FACE));
            }
            if (definition.deleteCaps == ThickenPlusDeleteCaps.AWAY || definition.deleteCaps == ThickenPlusDeleteCaps.BOTH)
            {
                capFaces = append(capFaces, qEntityFilter(outputs.away, EntityType.FACE));
            }
            const capQuery = qUnion(capFaces);
            if (isQueryEmpty(context, capQuery))
            {
                throw regenError("The cap faces to delete could not be found.", ["deleteCaps"]);
            }
            opDeleteFace(context, id + "deleteCaps", {
                        "deleteFaces" : capQuery,
                        "includeFillet" : false,
                        "capVoid" : false,
                        "leaveOpen" : true
                    });
        }

        // Only new bodies: with Add / Remove / Intersect the result is a part the user already has.
        if (definition.operationType == NewBodyOperationType.NEW && definition.outputName != "")
        {
            setProperty(context, { "entities" : created, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
        }

        embedStandardOutputs(context, id, settledOutputs(context, {
                    // With Remove / Intersect the thickened bodies are consumed by the boolean: the
                    // bodies holding the tracked faces are the parts it changed (2026-09-27 audit).
                    "output" : qUnion([qEntityFilter(output, EntityType.BODY), qOwnerBody(outputs.toward), qOwnerBody(outputs.away), qOwnerBody(outputs.side)]),
                    "outputDescription" : "The thickened bodies (or the parts they merged into)",
                    "inputs" : definition.entities,
                    "queries" : {
                        "towardFaces" : extractableQuery(qEntityFilter(outputs.toward, EntityType.FACE), "The faces on the reference side (Toward reference from the surface).", DebugColor.GREEN),
                        "awayFaces" : extractableQuery(qEntityFilter(outputs.away, EntityType.FACE), "The faces on the other side (on the input surface when Away from reference is 0).", DebugColor.RED),
                        "sideFaces" : extractableQuery(qEntityFilter(outputs.side, EntityType.FACE), "The faces around the edges of the thickened surfaces.", DebugColor.BLUE)
                    }
                }));
    }, {
        "operationType" : NewBodyOperationType.NEW,
        "sideReference" : qNothing(),
        "thicknessAway" : 0 * meter,
        "swapSides" : false,
        "keepTools" : false,
        "outputName" : "",
        "deleteCaps" : ThickenPlusDeleteCaps.NONE,
        "draftAngle" : 0 * degree,
        "shareDraftAngle" : true,
        "draftAngleAway" : 0 * degree,
        "draftNeutral" : ThickenPlusDraftNeutral.SEED,
        "draftEdgeMode" : ThickenPlusDraftEdges.ALL,
        "draftEdges" : qNothing(),
        "sideOffset" : 0 * meter,
        "checkCurvature" : true,
        "debugPrint" : false
    });

/**
 * A thicken of t on one side of a face cannot pass a place where the face is concave toward that side with a
 * radius under t (the offset folds). Each face is sampled on a CURVATURE_SAMPLES grid; the tightest concave
 * radius on each side is compared with that side's thickness, and the first place that fails is reported
 * (shown in red, with its radius) as the feature's error.
 *
 * evFaceCurvature: positive when the centre of curvature is AGAINST the normal. So the side along the normal
 * is concave where the curvature is negative, the other side where it is positive.
 */
function checkCurvature(context is Context, id is Id, group is map, index is number, debugPrint is boolean)
{
    var parameters = [];
    for (var i = 0; i < CURVATURE_SAMPLES; i += 1)
    {
        for (var j = 0; j < CURVATURE_SAMPLES; j += 1)
        {
            parameters = append(parameters, vector((i + 0.5) / CURVATURE_SAMPLES, (j + 0.5) / CURVATURE_SAMPLES));
        }
    }
    var tightest = { "alongNormal" : inf * meter, "againstNormal" : inf * meter };
    for (var face in evaluateQuery(context, group.faces))
    {
        const curvatures = evFaceCurvatures(context, { "face" : face, "parameters" : parameters });
        var planes = undefined;
        for (var k = 0; k < size(parameters); k += 1)
        {
            const c = curvatures[k];
            // concave on the normal side: the most negative curvature; on the other side: the most positive
            for (var side in [{ "key" : "alongNormal", "k" : -c.minCurvature, "t" : group.thickness1 },
                        { "key" : "againstNormal", "k" : c.maxCurvature, "t" : group.thickness2 }])
            {
                if (side.k <= 0 / meter)
                {
                    continue;
                }
                const radius = 1 / side.k;
                // only a place that fails, or (when printing) a new tightest, is worth locating
                if (radius >= side.t && !(debugPrint && radius < tightest[side.key]))
                {
                    continue;
                }
                if (planes == undefined)
                {
                    planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : parameters });
                }
                // grid points can fall outside a trimmed face: only points on the face count
                const at = planes[k].origin;
                if (evDistance(context, { "side0" : at, "side1" : face }).distance > 1e-6 * meter)
                {
                    continue;
                }
                tightest[side.key] = min(tightest[side.key], radius);
                if (radius < side.t)
                {
                    addDebugPoint(context, at, DebugColor.RED);
                    throw regenError("Surface " ~ (index + 1) ~ " is concave with radius " ~ fmtMM(radius, 3) ~ " mm at ("
                        ~ fmtMM(at[0], 2) ~ ", " ~ fmtMM(at[1], 2) ~ ", " ~ fmtMM(at[2], 2) ~ ") mm on the side being thickened by "
                        ~ fmtMM(side.t, 3) ~ " mm: the thicken would fold there. Reduce that thickness below the radius, or swap sides.",
                        ["thicknessToward"]);
                }
            }
        }
    }
    if (debugPrint)
    {
        println("[thicken+] surface " ~ (index + 1) ~ ": tightest concave radius along the normal "
            ~ (tightest.alongNormal < inf * meter ? fmtMM(tightest.alongNormal, 3) ~ " mm" : "none")
            ~ ", against it " ~ (tightest.againstNormal < inf * meter ? fmtMM(tightest.againstNormal, 3) ~ " mm" : "none") ~ ".");
    }
}

/**
 * The edges on the outline of a face set: those with exactly one of the faces on them. For a face in the middle of a
 * part these are all its edges (two-sided in the part, but one-sided for this feature); between two selected faces an
 * edge is not on the outline and has no wall.
 */
function oneSidedEdges(context is Context, faces is Query) returns Query
{
    var outline = [];
    for (var edge in evaluateQuery(context, qAdjacent(faces, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        if (size(evaluateQuery(context, qIntersection([qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE), faces]))) == 1)
        {
            outline = append(outline, edge);
        }
    }
    return qUnion(outline);
}

/**
 * The drafted thicken of one group of faces. Every connected piece of the faces is copied as a sheet and built by
 * draftSheet; the input surfaces go with keepTools off, as with opThicken. Returns the Toward and Away cap queries
 * (tracked through the splits).
 */
function draftThicken(context is Context, groupId is Id, group is map, draft is map) returns map
{
    opExtractSurface(context, groupId + "seed", { "faces" : group.faces, "tangentPropagation" : false });
    const seedBodies = qCreatedBy(groupId + "seed", EntityType.BODY);
    const seeds = evaluateQuery(context, seedBodies);
    if (size(seeds) == 0)
    {
        throw regenError("The faces could not be copied for the draft.", ["entities"]);
    }
    var toward = [];
    var away = [];
    for (var s = 0; s < size(seeds); s += 1)
    {
        const caps = draftSheet(context, groupId + ("sheet" ~ s), seeds[s], group, draft);
        toward = append(toward, caps.toward);
        away = append(away, caps.away);
    }
    opDeleteBodies(context, groupId + "deleteSeeds", { "entities" : seedBodies });
    if (!draft.keepTools)
    {
        // like opThicken: a sheet body goes when all its faces were thickened
        var spent = [];
        for (var sheet in evaluateQuery(context, qBodyType(qOwnerBody(group.faces), BodyType.SHEET)))
        {
            if (isQueryEmpty(context, qSubtraction(qOwnedByBody(sheet, EntityType.FACE), group.faces)))
            {
                spent = append(spent, sheet);
            }
        }
        if (size(spent) > 0)
        {
            opDeleteBodies(context, groupId + "deleteInputs", { "entities" : qUnion(spent) });
        }
    }
    return { "toward" : qUnion(toward), "away" : qUnion(away) };
}

/**
 * One connected sheet of the seed faces, thickened with drafted walls.
 *
 * The slab is the std thicken of a copy of the seed grown by a margin on every side. Each outline edge gets a ruled
 * wall: at signed height h from the neutral surface (along the normal toward the reference) the outline moves outward
 * along the surface conormal by h * tan(angle), so the wall tilts about the neutral surface; with two angles the wall
 * kinks there. The walls are lofts between sampled copies of the outline, overextended in height past the slab and, at
 * ends that do not continue smoothly into another outline edge, along the surface past the corner (points projected
 * onto a second, bigger copy of the seed) so they cross the whole slab. The slab is split by the walls of one smooth
 * chain at a time and the piece holding the seed is kept: this is right where the outline is convex, and a concave
 * corner (or an edge that continues smoothly into an edge with another draft) is reported instead.
 */
function draftSheet(context is Context, id is Id, seed is Query, group is map, draft is map) returns map
{
    const T = group.toward;
    const A = group.away;
    const total = T + A;
    const kMax = max(abs(draft.kToward), abs(draft.kAway));
    // the slab outgrows the walls' shift on every side; the walls reach past its outline by a multiple of that margin
    const margin = 2 * total * kMax + 2 * abs(draft.offset) + 1 * millimeter;
    const reach = 8 * margin + 5 * millimeter;
    const mm = millimeter;

    const seedFaces = qOwnedByBody(seed, EntityType.FACE);
    const seedFaceList = evaluateQuery(context, seedFaces);
    const modelFaceList = evaluateQuery(context, group.faces);
    const centroid = evApproximateCentroid(context, { "entities" : seedFaces });
    const dm = evDistance(context, { "side0" : centroid, "side1" : seedFaces });
    const mid = dm.sides[1].point;
    const seedNormalMid = evFaceTangentPlane(context, { "face" : seedFaceList[dm.sides[1].index], "parameter" : dm.sides[1].parameter }).normal;
    const dmm = evDistance(context, { "side0" : mid, "side1" : group.faces });
    const modelNormalMid = evFaceTangentPlane(context, { "face" : modelFaceList[dmm.sides[1].index], "parameter" : dmm.sides[1].parameter }).normal;
    // a seed normal times this points toward the reference
    const towardSign = (dot(seedNormalMid, modelNormalMid) >= 0 ? 1 : -1) * group.alongNormal;

    // a copy of the seed grown by dist on every outline edge
    const grownCopy = function(tag is string, dist is ValueWithUnits) returns Query
        {
            opExtractSurface(context, id + ("copy" ~ tag), { "faces" : seedFaces, "tangentPropagation" : false });
            const copy = qCreatedBy(id + ("copy" ~ tag), EntityType.BODY);
            opExtendSheetBody(context, id + ("extend" ~ tag), {
                        "entities" : qEdgeTopologyFilter(qOwnedByBody(copy, EntityType.EDGE), EdgeTopology.LAMINAR),
                        "tangentPropagation" : false,
                        "endCondition" : ExtendEndType.EXTEND_BLIND,
                        "extendDistance" : dist,
                        "extensionShape" : ExtendSheetShapeType.LINEAR
                    });
            return copy;
        };
    const smallCopy = grownCopy("S", margin);
    const bigCopy = grownCopy("B", 1.5 * reach);
    const smallFaces = qOwnedByBody(smallCopy, EntityType.FACE);
    const bigFaces = qOwnedByBody(bigCopy, EntityType.FACE);
    const bigFaceList = evaluateQuery(context, bigFaces);

    // the slab: opThicken of the small copy; START is its thickness1 side, along that copy's normal
    const dSmall = evDistance(context, { "side0" : mid, "side1" : smallFaces });
    const smallNormalMid = evFaceTangentPlane(context, { "face" : evaluateQuery(context, smallFaces)[dSmall.sides[1].index], "parameter" : dSmall.sides[1].parameter }).normal;
    const alongSmall = (dot(smallNormalMid, modelNormalMid) >= 0 ? 1 : -1) * group.alongNormal;
    opThicken(context, id + "slab", {
                "entities" : smallFaces,
                "thickness1" : alongSmall > 0 ? T : A,
                "thickness2" : alongSmall > 0 ? A : T,
                "keepTools" : false
            });
    const tracked = function(q is Query) returns Query
        {
            return qUnion([q, startTracking(context, q)]);
        };
    const slab = tracked(qCreatedBy(id + "slab", EntityType.BODY));

    // heights from the seed along the toward normal: the neutral surface, and the walls' extent (past the slab)
    const hN = draft.neutral == ThickenPlusDraftNeutral.TOP ? T : (draft.neutral == ThickenPlusDraftNeutral.BOTTOM ? -A : 0 * meter);
    const hHi = T + 0.5 * total + 1 * mm;
    const hLo = -(A + 0.5 * total + 1 * mm);
    // a point strictly inside the slab above the seed, to tell the piece to keep
    const pin = mid + seedNormalMid * towardSign * ((T - A) / 2);

    // ---- the outline edges, sampled ----
    var edges = [];
    for (var edge in evaluateQuery(context, qEdgeTopologyFilter(qOwnedByBody(seed, EntityType.EDGE), EdgeTopology.LAMINAR)))
    {
        var params = [];
        for (var i = 0; i <= DRAFT_SAMPLES; i += 1)
        {
            params = append(params, i / DRAFT_SAMPLES);
        }
        const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : params });
        var ps = [];
        var ns = [];
        var ts = [];
        // the seed face under each sample, then the face normals there: one evFaceTangentPlanes call per face, not per sample
        var hits = [];
        for (var i = 0; i <= DRAFT_SAMPLES; i += 1)
        {
            hits = append(hits, evDistance(context, { "side0" : lines[i].origin, "side1" : seedFaces }).sides[1]);
        }
        var sampleNormals = makeArray(DRAFT_SAMPLES + 1);
        for (var fi = 0; fi < size(seedFaceList); fi += 1)
        {
            var onFace = [];
            var onFaceParameters = [];
            for (var i = 0; i <= DRAFT_SAMPLES; i += 1)
            {
                if (hits[i].index == fi)
                {
                    onFace = append(onFace, i);
                    onFaceParameters = append(onFaceParameters, hits[i].parameter);
                }
            }
            if (size(onFace) > 0)
            {
                const planes = evFaceTangentPlanes(context, { "face" : seedFaceList[fi], "parameters" : onFaceParameters });
                for (var n = 0; n < size(onFace); n += 1)
                {
                    sampleNormals[onFace[n]] = planes[n].normal;
                }
            }
        }
        for (var i = 0; i <= DRAFT_SAMPLES; i += 1)
        {
            ps = append(ps, lines[i].origin);
            ns = append(ns, sampleNormals[i] * towardSign);
            ts = append(ts, lines[i].direction);
        }
        const closed = norm(ps[DRAFT_SAMPLES] - ps[0]) < 1e-6 * meter;
        if (closed)
        {
            ps[DRAFT_SAMPLES] = ps[0];
        }
        // the outward conormal: along tangent x normal, turned round when a step that way stays on the face
        const im = floor(DRAFT_SAMPLES / 2);
        const guess = normalize(cross(ts[im], ns[im]));
        const stepOut = evDistance(context, { "side0" : ps[im] + guess * 0.5 * mm, "side1" : seedFaces }).distance;
        const stepIn = evDistance(context, { "side0" : ps[im] - guess * 0.5 * mm, "side1" : seedFaces }).distance;
        var cs = [];
        for (var i = 0; i <= DRAFT_SAMPLES; i += 1)
        {
            cs = append(cs, normalize(cross(ts[i], ns[i])) * (stepOut < stepIn ? -1 : 1));
        }
        const isDrafted = !isQueryEmpty(context, group.drafted) && evDistance(context, { "side0" : ps[im], "side1" : group.drafted }).distance < 1e-6 * meter;
        edges = append(edges, {
                    "edge" : edge, "ps" : ps, "ns" : ns, "ts" : ts, "cs" : cs, "closed" : closed,
                    "kT" : isDrafted ? draft.kToward : 0, "kA" : isDrafted ? draft.kAway : 0
                });
    }
    const nE = size(edges);
    if (nE == 0)
    {
        throw regenError("The faces have no outline edge to build a draft on.", ["entities"]);
    }

    // ---- smooth chains: edges that meet tangentially with the same draft share one cutter and are not extended there ----
    var label = [];
    var startLinked = [];
    var endLinked = [];
    for (var i = 0; i < nE; i += 1)
    {
        label = append(label, i);
        startLinked = append(startLinked, edges[i].closed);
        endLinked = append(endLinked, edges[i].closed);
    }
    for (var i = 0; i < nE; i += 1)
    {
        if (edges[i].closed)
        {
            continue;
        }
        for (var a = 0; a < 2; a += 1)
        {
            const pa = a == 0 ? edges[i].ps[0] : edges[i].ps[DRAFT_SAMPLES];
            const ua = a == 0 ? -edges[i].ts[0] : edges[i].ts[DRAFT_SAMPLES];
            var partners = [];
            for (var j = 0; j < nE; j += 1)
            {
                if (j == i || edges[j].closed)
                {
                    continue;
                }
                for (var b = 0; b < 2; b += 1)
                {
                    if (norm(pa - (b == 0 ? edges[j].ps[0] : edges[j].ps[DRAFT_SAMPLES])) < 1e-6 * meter)
                    {
                        partners = append(partners, { "j" : j, "b" : b, "u" : b == 0 ? -edges[j].ts[0] : edges[j].ts[DRAFT_SAMPLES] });
                    }
                }
            }
            if (draft.debug)
            {
                println("[thicken+] draft: edge " ~ i ~ " end " ~ a ~ ": " ~ size(partners) ~ " partner(s)"
                    ~ (size(partners) == 1 ? ", tangent dot " ~ roundToPrecision(dot(ua, partners[0].u), 4) ~ ", same draft " ~ (edges[partners[0].j].kT == edges[i].kT && edges[partners[0].j].kA == edges[i].kA) : ""));
            }
            if (size(partners) == 1 && dot(ua, partners[0].u) < -DRAFT_TANGENT_COS
                && edges[partners[0].j].kT == edges[i].kT && edges[partners[0].j].kA == edges[i].kA)
            {
                if (a == 0)
                {
                    startLinked[i] = true;
                }
                else
                {
                    endLinked[i] = true;
                }
                // A bend (the faces fold, or the outline turns a little): both walls share one end curve, on the mitre of
                // the two normals and of the two conormals, so the chain's cutter is continuous through the bend.
                const pj = partners[0].j;
                const ia = a == 0 ? 0 : DRAFT_SAMPLES;
                const ib = partners[0].b == 0 ? 0 : DRAFT_SAMPLES;
                const nm = normalize(edges[i].ns[ia] + edges[pj].ns[ib]);
                const cm = normalize(edges[i].cs[ia] + edges[pj].cs[ib]);
                var ei = edges[i];
                ei.ns[ia] = nm / dot(nm, edges[i].ns[ia]);
                ei.cs[ia] = cm / dot(cm, edges[i].cs[ia]);
                edges[i] = ei;
                var ej = edges[pj];
                ej.ns[ib] = nm / dot(nm, edges[pj].ns[ib]);
                ej.cs[ib] = cm / dot(cm, edges[pj].cs[ib]);
                edges[pj] = ej;
                const from = label[partners[0].j];
                const to = label[i];
                for (var k = 0; k < nE; k += 1)
                {
                    if (label[k] == from)
                    {
                        label[k] = to;
                    }
                }
            }
        }
    }

    // a point of the big copy near q, with its toward normal and a conormal carried over from the edge's end
    const onBig = function(q is Vector, conormal is Vector) returns map
        {
            const db = evDistance(context, { "side0" : q, "side1" : bigFaces });
            const nb = evFaceTangentPlane(context, { "face" : bigFaceList[db.sides[1].index], "parameter" : db.sides[1].parameter }).normal * towardSign;
            return { "p" : db.sides[1].point, "n" : nb, "c" : normalize(conormal - nb * dot(conormal, nb)) };
        };
    // An extension past the end of edge `index` that comes back onto the face is a concave corner, or runs along an edge
    // with another draft (marked). Along the outline of a neighbour with the same draft it is fine: a bend of the outline,
    // as at the fold between two faces, whose wall the neighbour's own wall continues.
    const checkOutside = function(p is Vector, index is number)
        {
            if (evDistance(context, { "side0" : p, "side1" : seedFaces }).distance < 1e-6 * meter)
            {
                var neighbours = [];
                for (var j = 0; j < nE; j += 1)
                {
                    if (j != index && edges[j].kT == edges[index].kT && edges[j].kA == edges[index].kA)
                    {
                        neighbours = append(neighbours, edges[j].edge);
                    }
                }
                if (size(neighbours) > 0 && evDistance(context, { "side0" : p, "side1" : qUnion(neighbours) }).distance < 1e-6 * meter)
                {
                    return;
                }
                addDebugPoint(context, p, DebugColor.RED);
                throw regenError("The draft cannot be built at a concave corner, or where an outline edge continues smoothly into an edge with a different draft (marked): draft the whole smooth chain together, and leave concave corners undrafted by drafting only convex ones.",
                    ["draftAngle"]);
            }
        };

    // ---- one ruled wall per edge (two with separate angles), grouped by chain ----
    var toolsByChain = [];
    for (var i = 0; i < nE; i += 1)
    {
        toolsByChain = append(toolsByChain, []);
    }
    for (var i = 0; i < nE; i += 1)
    {
        const e = edges[i];
        var pts = [];
        var nrm = [];
        var con = [];
        if (!startLinked[i])
        {
            for (var j = DRAFT_EXTENSION_STEPS; j >= 1; j -= 1)
            {
                const t = onBig(e.ps[0] - e.ts[0] * (reach * j / DRAFT_EXTENSION_STEPS), e.cs[0]);
                checkOutside(t.p, i);
                pts = append(pts, t.p);
                nrm = append(nrm, t.n);
                con = append(con, t.c);
            }
        }
        for (var k = 0; k <= DRAFT_SAMPLES; k += 1)
        {
            pts = append(pts, e.ps[k]);
            nrm = append(nrm, e.ns[k]);
            con = append(con, e.cs[k]);
        }
        if (!endLinked[i])
        {
            for (var j = 1; j <= DRAFT_EXTENSION_STEPS; j += 1)
            {
                const t = onBig(e.ps[DRAFT_SAMPLES] + e.ts[DRAFT_SAMPLES] * (reach * j / DRAFT_EXTENSION_STEPS), e.cs[DRAFT_SAMPLES]);
                checkOutside(t.p, i);
                pts = append(pts, t.p);
                nrm = append(nrm, t.n);
                con = append(con, t.c);
            }
        }

        const wid = id + ("wall" ~ i);
        const levels = e.kT != e.kA ? [hLo, hN, hHi] : [hLo, hHi];
        var curves = [];
        for (var l = 0; l < size(levels); l += 1)
        {
            const shift = draft.offset + (levels[l] >= hN ? (levels[l] - hN) * e.kT : (levels[l] - hN) * e.kA);
            var through = [];
            for (var k = 0; k < size(pts); k += 1)
            {
                through = append(through, pts[k] + nrm[k] * levels[l] + con[k] * shift);
            }
            opFitSpline(context, wid + ("curve" ~ l), { "points" : through });
            curves = append(curves, qCreatedBy(wid + ("curve" ~ l), EntityType.EDGE));
        }
        var halves = [];
        for (var l = 0; l < size(levels) - 1; l += 1)
        {
            opLoft(context, wid + ("loft" ~ l), {
                        "profileSubqueries" : [curves[l], curves[l + 1]],
                        "bodyType" : ToolBodyType.SURFACE
                    });
            halves = append(halves, qBodyType(qCreatedBy(wid + ("loft" ~ l), EntityType.BODY), BodyType.SHEET));
        }
        if (size(halves) > 1)
        {
            // a kinked wall: the two halves share the kink curve; joined into one sheet they cut as one surface
            // (the union keeps the identity of one half, so follow both through it)
            const followed = tracked(qUnion(halves));
            opBoolean(context, wid + "join", { "tools" : qUnion(halves), "operationType" : BooleanOperationType.UNION });
            halves = [qBodyType(followed, BodyType.SHEET)];
        }
        toolsByChain[label[i]] = append(toolsByChain[label[i]], halves[0]);
        var wires = [];
        for (var l = 0; l < size(levels); l += 1)
        {
            wires = append(wires, qCreatedBy(wid + ("curve" ~ l), EntityType.BODY));
        }
        opDeleteBodies(context, wid + "deleteCurves", { "entities" : qUnion(wires) });
        if (draft.debug)
        {
            println("[thicken+] draft: edge " ~ i ~ " of " ~ nE ~ " chain " ~ label[i] ~ (e.closed ? " closed" : "") ~ ", tan angle toward "
                ~ roundToPrecision(e.kT, 4) ~ " away " ~ roundToPrecision(e.kA, 4) ~ ", " ~ size(levels) - 1 ~ " wall(s)");
        }
    }

    // ---- split the slab by each chain's cutter, keeping the piece that holds the seed ----
    for (var c = 0; c < nE; c += 1)
    {
        if (size(toolsByChain[c]) == 0)
        {
            continue;
        }
        // a chain of several walls cuts as one surface: joined into one sheet (the union keeps one sheet's identity, so follow them)
        var cutter = qUnion(toolsByChain[c]);
        if (size(toolsByChain[c]) > 1)
        {
            const followedWalls = tracked(cutter);
            opBoolean(context, id + ("join" ~ c), { "tools" : cutter, "operationType" : BooleanOperationType.UNION });
            cutter = qBodyType(followedWalls, BodyType.SHEET);
        }
        opSplitPart(context, id + ("split" ~ c), {
                    "targets" : qBodyType(slab, BodyType.SOLID),
                    "tool" : cutter,
                    "keepTools" : false
                });
        const pieces = qBodyType(slab, BodyType.SOLID);
        if (isQueryEmpty(context, pieces))
        {
            throw regenError("The draft split lost the thickened body.", ["draftAngle"]);
        }
        const dropped = qSubtraction(pieces, qContainsPoint(pieces, pin));
        if (!isQueryEmpty(context, dropped))
        {
            opDeleteBodies(context, id + ("drop" ~ c), { "entities" : dropped });
        }
    }

    // ---- check: one solid, with the walls enclosing the face outline ----
    const result = qBodyType(slab, BodyType.SOLID);
    if (size(evaluateQuery(context, result)) != 1)
    {
        throw regenError("The draft cut the thickened body into pieces; reduce the angle or the thickness.", ["draftAngle"]);
    }
    const h0 = (T - A) / 2;
    for (var e in edges)
    {
        for (var k in [floor(DRAFT_SAMPLES / 4), floor(DRAFT_SAMPLES / 2), floor(3 * DRAFT_SAMPLES / 4)])
        {
            const shift0 = draft.offset + (h0 >= hN ? (h0 - hN) * e.kT : (h0 - hN) * e.kA);
            const probe = e.ps[k] + e.ns[k] * h0 + e.cs[k] * (shift0 - 0.25 * mm);
            if (isQueryEmpty(context, qContainsPoint(result, probe)))
            {
                addDebugPoint(context, probe, DebugColor.RED);
                throw regenError("The drafted walls do not enclose the face here (marked): the outline is concave, or the walls cross each other. Try a smaller angle.",
                    ["draftAngle"]);
            }
        }
    }

    // ---- the caps: faces lying on the offset surfaces. Tracking a cap through the splits and joins also picks up the faces
    // the walls cut, so the caps are read from the geometry: all of a cap's points sit at the signed distance +toward
    // (Toward cap) or -away (Away cap) from the grown seed, along the toward normal; a wall's points do not.
    var towardCaps = [];
    var awayCaps = [];
    for (var face in evaluateQuery(context, qOwnedByBody(result, EntityType.FACE)))
    {
        var towardHits = 0;
        var awayHits = 0;
        const capProbes = evFaceTangentPlanes(context, { "face" : face, "parameters" : [vector(0.5, 0.5), vector(0.3, 0.7), vector(0.7, 0.3)] });
        for (var probe in capProbes)
        {
            const p = probe.origin;
            const dc = evDistance(context, { "side0" : p, "side1" : bigFaces });
            const nc = evFaceTangentPlane(context, { "face" : bigFaceList[dc.sides[1].index], "parameter" : dc.sides[1].parameter }).normal * towardSign;
            const signed = dot(p - dc.sides[1].point, nc);
            if (abs(signed - T) < 1e-5 * meter)
            {
                towardHits += 1;
            }
            if (abs(signed + A) < 1e-5 * meter)
            {
                awayHits += 1;
            }
        }
        if (towardHits == 3)
        {
            towardCaps = append(towardCaps, face);
        }
        else if (awayHits == 3)
        {
            awayCaps = append(awayCaps, face);
        }
    }

    opDeleteBodies(context, id + "deleteBig", { "entities" : bigCopy });
    const leftover = qCreatedBy(id + "copyS", EntityType.BODY);
    if (!isQueryEmpty(context, leftover))
    {
        opDeleteBodies(context, id + "deleteSmall", { "entities" : leftover });
    }
    return { "toward" : qUnion(towardCaps), "away" : qUnion(awayCaps) };
}