FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// The boolean step's enums (NewBodyOperationType) are parameter types here, so they must be exported (as std Thicken does).
export import(path : "onshape/std/tool.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "46dade749f549210ce2b33fc");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
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

/** Face samples per parameter direction for the curvature check. */
const CURVATURE_SAMPLES = 9;

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

        booleanStepScopePredicate(definition);

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
            groups = append(groups, {
                        "faces" : groupFaces,
                        "alongNormal" : alongNormal,
                        // opThicken: thickness1 along the normal, thickness2 against it
                        "thickness1" : alongNormal > 0 ? toward : away,
                        "thickness2" : alongNormal > 0 ? away : toward
                    });
            if (definition.debugPrint)
            {
                println("[thicken+] surface " ~ (i + 1) ~ ": toward the reference is " ~ (alongNormal > 0 ? "along" : "against")
                    ~ " its normal; " ~ fmtMM(toward, 4) ~ " mm toward, " ~ fmtMM(away, 4) ~ " mm away.");
            }
        }

        if (definition.checkCurvature)
        {
            for (var i = 0; i < size(groups); i += 1)
            {
                checkCurvature(context, id, groups[i], i, definition.debugPrint);
            }
        }

        // A copy of each group's faces to measure the result against: opThicken consumes the input surfaces
        // (Keep tools off), and the copies must be gone before the boolean step, which takes every body created
        // under id as a tool.
        for (var i = 0; i < size(groups); i += 1)
        {
            opExtractSurface(context, id + ("measure" ~ i), { "faces" : groups[i].faces });
            groups[i].copy = qCreatedBy(id + ("measure" ~ i), EntityType.FACE);
            // which side of the copy is "toward": the reference's side of it (the copy's own normal need not match)
            groups[i].towardSign = probe != undefined ? sideSign(context, probe, groups[i].copy) : 1;
        }

        // std Thicken, per group, then its boolean step unchanged.
        const remainingTransform = getRemainderPatternTransform(context, { "references" : definition.entities });
        const thickenAll = function(opId is Id)
            {
                for (var i = 0; i < size(groups); i += 1)
                {
                    const groupId = opId + ("thicken" ~ i);
                    opThicken(context, groupId, {
                                "entities" : groups[i].faces,
                                "thickness1" : groups[i].thickness1,
                                "thickness2" : groups[i].thickness2,
                                "keepTools" : definition.keepTools
                            });
                    transformResultIfNecessary(context, groupId, remainingTransform);
                }
            };
        thickenAll(id);

        // Name the faces before the boolean, tracked through it.
        var towardFaces = [];
        var awayFaces = [];
        var sideFaces = [];
        for (var i = 0; i < size(groups); i += 1)
        {
            const named = classifyFaces(context, id + ("thicken" ~ i), groups[i], toward, away);
            towardFaces = append(towardFaces, named.toward);
            awayFaces = append(awayFaces, named.away);
            sideFaces = append(sideFaces, named.side);
            opDeleteBodies(context, id + ("deleteMeasure" ~ i), { "entities" : qOwnerBody(groups[i].copy) });
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

        embedStandardOutputs(context, id, {
                    "output" : qEntityFilter(output, EntityType.BODY),
                    "outputDescription" : "The thickened bodies (or the parts they merged into)",
                    "inputs" : definition.entities,
                    "queries" : {
                        "towardFaces" : extractableQuery(qEntityFilter(outputs.toward, EntityType.FACE), "The faces on the reference side (Toward reference from the surface).", DebugColor.GREEN),
                        "awayFaces" : extractableQuery(qEntityFilter(outputs.away, EntityType.FACE), "The faces on the other side (on the input surface when Away from reference is 0).", DebugColor.RED),
                        "sideFaces" : extractableQuery(qEntityFilter(outputs.side, EntityType.FACE), "The faces around the edges of the thickened surfaces.", DebugColor.BLUE)
                    }
                });
    }, {
        "operationType" : NewBodyOperationType.NEW,
        "sideReference" : qNothing(),
        "thicknessAway" : 0 * meter,
        "swapSides" : false,
        "keepTools" : false,
        "checkCurvature" : true,
        "debugPrint" : false
    });

/**
 * The faces one opThicken made, sorted by where they sit relative to the input surface: at the Toward thickness
 * on the reference side, at the Away thickness on the other, or neither (the side faces). Read at a point on each
 * face (the face's point nearest its centroid), as a signed distance from a copy of the input faces taken before
 * the thicken consumed them.
 */
function classifyFaces(context is Context, thickenId is Id, group is map, toward is ValueWithUnits, away is ValueWithUnits) returns map
{
    var smallest = inf * meter;
    for (var t in [toward, away])
    {
        if (t > 0 * meter)
        {
            smallest = min(smallest, t);
        }
    }
    const tol = min(1e-5 * meter, 0.25 * smallest);
    var result = { "toward" : [], "away" : [], "side" : [] };
    for (var face in evaluateQuery(context, qOwnedByBody(qCreatedBy(thickenId, EntityType.BODY), EntityType.FACE)))
    {
        const centroid = evApproximateCentroid(context, { "entities" : face });
        const onFace = evDistance(context, { "side0" : face, "side1" : centroid }).sides[0].point;
        const s = signedSideOf(context, onFace, group.copy);
        if (s == undefined)
        {
            result.side = append(result.side, face);
            continue;
        }
        // positive toward the reference
        const d = s * group.towardSign;
        if (abs(d - toward) < tol)
        {
            result.toward = append(result.toward, face);
        }
        else if (abs(d + away) < tol)
        {
            result.away = append(result.away, face);
        }
        else
        {
            result.side = append(result.side, face);
        }
    }
    return { "toward" : qUnion(result.toward), "away" : qUnion(result.away), "side" : qUnion(result.side) };
}

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
