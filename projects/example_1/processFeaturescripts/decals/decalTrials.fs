FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

import(path : "onshape/std/decalUtils.fs", version : "2892.0");
import(path : "onshape/std/error.fs", version : "2892.0");
import(path : "onshape/std/imagemappingtype.gen.fs", version : "2892.0");
import(path : "onshape/std/mateConnector.fs", version : "2892.0");
import(path : "onshape/std/topologyUtils.fs", version : "2892.0");

/**
 * Trials to try to align decals with top surfaces. First make flat/cylindrical copy,
 * apply and align decal to simplified surface body,
 * use opReplaceFace to replace the flattened faces with the old faces.
 */

annotation { "Feature Type Name" : "Deform decal", "Feature Type Description" : "" }
export const deformDecal = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Image" }
        definition.image is ImageData;

        annotation { "Name" : "Decal faces", "Filter" : EntityType.FACE }
        definition.decalFaces is Query;

        annotation { "Name" : "Deformed faces", "Filter" : EntityType.FACE }
        definition.deformedFaces is Query;

        // ---- Debug ----
        annotation { "Name" : "Show debug", "Default" : false }
        definition.showDebug is boolean;

        if (definition.showDebug)
        {
            annotation { "Name" : "Debug: image data", "Default" : false }
            definition.debugImage is boolean;

            annotation { "Name" : "Debug: face geometry", "Default" : false }
            definition.debugFaces is boolean;

            annotation { "Name" : "Debug: face attributes", "Default" : false }
            definition.debugAttributes is boolean;
        }
    }
    {
        const decalFaceArray    = evaluateQuery(context, definition.decalFaces);
        const deformedFaceArray = evaluateQuery(context, definition.deformedFaces);

        // ---- debug: image ----
        if (definition.showDebug && definition.debugImage)
        {
            if (imageDataIsSpecified(definition.image))
            {
                println("Image specified: " ~ toString(definition.image.imageWidth) ~
                        " x " ~ toString(definition.image.imageHeight) ~ " px");
            }
            else
            {
                println("Image: NOT SPECIFIED");
            }
        }

        // ---- debug: face geometry ----
        if (definition.showDebug && definition.debugFaces)
        {
            println("Decal faces count: "    ~ toString(size(decalFaceArray)));
            println("Deformed faces count: " ~ toString(size(deformedFaceArray)));

            for (var i = 0; i < size(decalFaceArray); i += 1)
            {
                const tp = evFaceTangentPlane(context, {
                    "face"      : decalFaceArray[i],
                    "parameter" : vector(0.5, 0.5)
                });
                println("  decalFace[" ~ toString(i) ~ "] centroid: " ~ toString(tp.origin));
                println("  decalFace[" ~ toString(i) ~ "] normal:   " ~ toString(tp.normal));
                addDebugPoint(context, tp.origin, DebugColor.GREEN);
                addDebugLine(context,
                    tp.origin,
                    tp.origin + tp.normal * (20 * millimeter),
                    DebugColor.GREEN);
            }

            for (var i = 0; i < size(deformedFaceArray); i += 1)
            {
                const tp = evFaceTangentPlane(context, {
                    "face"      : deformedFaceArray[i],
                    "parameter" : vector(0.5, 0.5)
                });
                println("  deformedFace[" ~ toString(i) ~ "] centroid: " ~ toString(tp.origin));
                println("  deformedFace[" ~ toString(i) ~ "] normal:   " ~ toString(tp.normal));
                addDebugPoint(context, tp.origin, DebugColor.RED);
                addDebugLine(context,
                    tp.origin,
                    tp.origin + tp.normal * (20 * millimeter),
                    DebugColor.RED);
            }
        }

        // ---- debug: attributes ----
        if (definition.showDebug && definition.debugAttributes)
        {
            for (var i = 0; i < size(decalFaceArray); i += 1)
            {
                const attrs = getAttributes(context, { "entities" : decalFaceArray[i] });
                println("decalFace[" ~ toString(i) ~ "] attribute count: " ~ toString(size(attrs)));
                for (var j = 0; j < size(attrs); j += 1)
                {
                    println("  attr[" ~ toString(j) ~ "]: " ~ toString(attrs[j]));
                }
            }

            for (var i = 0; i < size(deformedFaceArray); i += 1)
            {
                const attrs = getAttributes(context, { "entities" : deformedFaceArray[i] });
                println("deformedFace[" ~ toString(i) ~ "] attribute count: " ~ toString(size(attrs)));
                for (var j = 0; j < size(attrs); j += 1)
                {
                    println("  attr[" ~ toString(j) ~ "]: " ~ toString(attrs[j]));
                }
            }
        }
    });
