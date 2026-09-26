FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "26943194d596af42a8f67417");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: orient_to_reference_icon.svg (feature icon)
IconNamespace::import(path : "7747b35226d4e052196e1a45", version : "bc84461767230340403b17ad");

/**
 * Orient to reference: turn surfaces so their normals point toward (or away from) a reference.
 *
 * The reference-side features pick a SIDE by geometry, but the surfaces they output keep whatever orientation their
 * inputs had, and the built-ins downstream (Thicken, Offset surface, extrude up to, Move face, Split front / back)
 * still pick their side by the normal. This feature fixes the normal itself: each selected surface body is read
 * against the reference (its signed distance, as in the other reference-side features) and flipped with
 * opFlipOrientation when its normal points the wrong way. Surfaces already facing the right way are left alone.
 *
 * The side is decided per body and read at the body's point nearest the reference -- a local reference near the
 * surface, on the side meant, is best. Solids have no free orientation (their normals always point out) and are
 * refused.
 *
 * Normals are shown while the dialog is open: arrows along each face's normal at a few points, after orientation.
 * Green = the body was already facing the right way, orange = this feature flipped it.
 *
 * Publishes (Extract variables): output (every selected surface, oriented), flipped (the bodies this feature
 * flipped), unchanged (the ones already facing the right way), flippedCount.
 */

/** Points per face parameter direction for the normal arrows. */
const ARROW_GRID = 3;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Orient to reference",
        "Feature Type Description" : "Flip surfaces so their normals point toward (or away from) a reference, so features downstream that follow the normal behave the same whatever happened upstream.",
        "Filter Selector" : "allparts" }
export const orientToReference = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Surfaces to orient",
                    "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO && SketchObject.NO && ModifiableEntityOnly.YES,
                    "Description" : "Surface bodies, or faces of them (a face orients its whole surface body)." }
        definition.surfaces is Query;

        annotation { "Name" : "Reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1,
                    "Description" : "Geometry on the side the normals should face: best a mate connector or vertex near the surfaces." }
        definition.reference is Query;

        annotation { "Name" : "Normals toward reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                    "Description" : "On: every normal points toward the reference. Off: away from it." }
        definition.towardReference is boolean;

        annotation { "Name" : "Show normals", "Default" : true,
                    "Description" : "While editing: arrows along each face's normal after orientation. Green = already right, orange = flipped here." }
        definition.showNormals is boolean;

        if (definition.showNormals)
        {
            annotation { "Name" : "Arrow length" }
            isLength(definition.arrowLength, { (millimeter) : [0.1, 10, 1000] } as LengthBoundSpec);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print sides", "Default" : false, "Description" : "Each surface's signed distance to the reference and whether it was flipped." }
            definition.debugPrint is boolean;
        }
    }
    {
        const faces = facesOf(definition.surfaces);
        const bodies = evaluateQuery(context, qOwnerBody(faces));
        if (size(bodies) == 0)
        {
            throw regenError("Select the surfaces to orient.", ["surfaces"]);
        }
        const solids = qBodyType(qUnion(bodies), BodyType.SOLID);
        if (!isQueryEmpty(context, solids))
        {
            throw regenError("A solid's faces cannot be flipped: its normals always point out of it. Select surfaces.", ["surfaces"], solids);
        }
        const probe = referenceProbe(context, definition.reference);
        if (probe == undefined)
        {
            throw regenError("Select the reference the normals should face.", ["reference"]);
        }

        var flipped = [];
        var unchanged = [];
        for (var i = 0; i < size(bodies); i += 1)
        {
            const bodyFaces = qOwnedByBody(bodies[i], EntityType.FACE);
            const side = sideSign(context, probe, bodyFaces);
            if (side == 0)
            {
                throw regenError("The reference lies on surface " ~ (i + 1) ~ " (or no side of it can be read there); pick something clearly to one side.",
                    ["reference"], bodies[i]);
            }
            // side > 0: the reference is on the side the normal points to already.
            const wrong = (side > 0) != definition.towardReference;
            if (wrong)
            {
                flipped = append(flipped, bodies[i]);
            }
            else
            {
                unchanged = append(unchanged, bodies[i]);
            }
            if (definition.debugPrint)
            {
                println("[orient] surface " ~ (i + 1) ~ ": reference " ~ fmtMM(signedSideOf(context, probe, bodyFaces), 3)
                    ~ " mm along its normal; " ~ (wrong ? "flipped." : "left as it is."));
            }
        }

        if (size(flipped) > 0)
        {
            opFlipOrientation(context, id + "flip", { "bodies" : qUnion(flipped) });
        }

        if (definition.showNormals)
        {
            showNormals(context, qUnion(unchanged), definition.arrowLength, DebugColor.GREEN);
            showNormals(context, qUnion(flipped), definition.arrowLength, DebugColor.ORANGE);
        }

        reportFeatureInfo(context, id, "Flipped " ~ size(flipped) ~ " of " ~ size(bodies) ~ " surface(s); "
            ~ size(unchanged) ~ " already faced " ~ (definition.towardReference ? "toward" : "away from") ~ " the reference.");

        embedStandardOutputs(context, id, {
                    "output" : qUnion(bodies),
                    "outputDescription" : "The selected surfaces, oriented",
                    "inputs" : definition.surfaces,
                    "variables" : {
                        "flippedCount" : extractableVariable(size(flipped), "Surfaces this feature flipped.")
                    },
                    "queries" : {
                        "flipped" : extractableQuery(qUnion(flipped), "The surfaces this feature flipped.", DebugColor.ORANGE),
                        "unchanged" : extractableQuery(qUnion(unchanged), "The surfaces already facing the right way.", DebugColor.GREEN)
                    }
                });
    }, {
        "towardReference" : true,
        "showNormals" : true,
        "arrowLength" : 10 * millimeter,
        "debugPrint" : false
    });

/**
 * An arrow along the normal at an ARROW_GRID x ARROW_GRID grid of points on every face of `bodies` (points that
 * fall outside a trimmed face are skipped).
 */
function showNormals(context is Context, bodies is Query, arrowLength is ValueWithUnits, color is DebugColor)
{
    var parameters = [];
    for (var i = 0; i < ARROW_GRID; i += 1)
    {
        for (var j = 0; j < ARROW_GRID; j += 1)
        {
            parameters = append(parameters, vector((i + 0.5) / ARROW_GRID, (j + 0.5) / ARROW_GRID));
        }
    }
    for (var face in evaluateQuery(context, qOwnedByBody(bodies, EntityType.FACE)))
    {
        for (var tp in evFaceTangentPlanes(context, { "face" : face, "parameters" : parameters }))
        {
            if (evDistance(context, { "side0" : tp.origin, "side1" : face }).distance > 1e-6 * meter)
            {
                continue;
            }
            addDebugArrow(context, tp.origin, tp.origin + tp.normal * arrowLength, arrowLength / 25, color);
        }
    }
}
