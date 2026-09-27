FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "afc5b762b2b4bf44f321ebce");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: move_face_plus_icon.svg (feature icon)
IconNamespace::import(path : "5018685bff9dec813af91051", version : "9aed37e2f23ebf8844ad345c");

/**
 * Move face+: offset faces in place toward (or away from) a reference, instead of along whichever
 * way each face's normal happens to point.
 *
 * The built-in offset face (opOffsetFace, what Move face > Offset runs). For each body owning
 * selected faces, the side is read ONCE -- the reference's signed distance to that body's selected
 * faces at the closest point (Reference_Side sideSign) -- so every face of one surface moves the same
 * way, and surfaces of opposite orientation still all move toward the reference. Faces are then
 * offset in at most two operations: those moving along their normal and those moving against it.
 *
 * A solid face's normal points out of the material, so without a reference the box is a plain
 * grow (on) / shrink (off) -- the native behaviour. A reference that is selected but cannot be read
 * is an error (referenceProbe), never "no reference".
 *
 * Publishes (Extract variables): the standard keys (output = the moved faces) and boundaryEdges.
 */

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Move face+",
        "Feature Type Description" : "Offset faces in place toward the side a reference is on, so faces of surfaces with opposite normals all move the same way.",
        "Filter Selector" : "allparts" }
export const moveFacePlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Faces to move", "Filter" : EntityType.FACE && ConstructionObject.NO && SketchObject.NO }
        definition.faces is Query;

        annotation { "Name" : "Distance", "UIHint" : UIHint.REMEMBER_PREVIOUS_VALUE }
        isLength(definition.distance, NONNEGATIVE_LENGTH_BOUNDS);

        annotation { "Name" : "Side reference", "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Geometry on the side to move toward. Leave empty to move along the face normals (a solid's faces grow)." }
        definition.sideReference is Query;

        annotation { "Name" : "Move toward reference", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION,
                    "Description" : "On: toward the reference. Off: away from it. Without a reference: along the normals (on) or against them (off)." }
        definition.towardReference is boolean;

        annotation { "Name" : "Reapply fillets", "Default" : false,
                    "Description" : "Defillet the moved faces before the offset and fillet them again after (the built-in option)." }
        definition.reFillet is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print details", "Default" : false, "Description" : "The side each body's faces went." }
            definition.debugPrint is boolean;
        }
    }
    {
        const probe = referenceProbe(context, definition.sideReference);
        const faces = qEntityFilter(definition.faces, EntityType.FACE);
        const bodies = evaluateQuery(context, qOwnerBody(faces));
        if (size(bodies) == 0)
        {
            throw regenError(ErrorStringEnum.DIRECT_EDIT_MOVE_FACE_CREATE_SELECT, ["faces"]);
        }
        if (tolerantEquals(definition.distance, 0 * meter))
        {
            reportFeatureInfo(context, id, "Distance is zero: nothing was moved.");
            return;
        }

        var along = [];
        var against = [];
        for (var i = 0; i < size(bodies); i += 1)
        {
            const bodyFaces = qIntersection([faces, qOwnedByBody(bodies[i], EntityType.FACE)]);
            var direction = definition.towardReference ? 1 : -1;
            if (probe != undefined)
            {
                const side = sideSign(context, probe, bodyFaces);
                if (side == 0)
                {
                    throw regenError("The side reference lies on the faces of body " ~ (i + 1) ~ " (or no side of them can be read there); pick something clearly to one side.",
                        ["sideReference"]);
                }
                direction = definition.towardReference ? side : -side;
            }
            if (definition.debugPrint)
            {
                println("[move face+] body " ~ (i + 1) ~ ": " ~ size(evaluateQuery(context, bodyFaces)) ~ " face(s) "
                        ~ (direction > 0 ? "along" : "against") ~ " their normal.");
            }
            if (direction > 0)
            {
                along = append(along, bodyFaces);
            }
            else
            {
                against = append(against, bodyFaces);
            }
        }

        // An offset keeps a face's identity; tracking also follows faces the kernel had to split.
        const held = qUnion(evaluateQuery(context, faces));
        const tracked = startTracking(context, faces);
        if (size(along) > 0)
        {
            opOffsetFace(context, id + "along", { "moveFaces" : qUnion(along), "offsetDistance" : definition.distance, "reFillet" : definition.reFillet });
        }
        if (size(against) > 0)
        {
            opOffsetFace(context, id + "against", { "moveFaces" : qUnion(against), "offsetDistance" : -definition.distance, "reFillet" : definition.reFillet });
        }

        const moved = qEntityFilter(qUnion([held, tracked]), EntityType.FACE);
        embedStandardOutputs(context, id, {
                    "output" : moved,
                    "outputDescription" : "The moved faces",
                    "inputs" : definition.faces,
                    "queries" : {
                        "boundaryEdges" : extractableQuery(qAdjacent(moved, AdjacencyType.EDGE, EntityType.EDGE),
                            "The edges bounding the moved faces.", DebugColor.CYAN)
                    }
                });
    }, {
        "sideReference" : qNothing(),
        "towardReference" : true,
        "reFillet" : false,
        "debugPrint" : false
    });
