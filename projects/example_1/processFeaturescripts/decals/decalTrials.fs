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
 *
 * Decal storage (from decalUtils.fs internals):
 *   associateDecalAttribute stores an array of DecalData under the named
 *   attribute "decals" on each face via setAttribute/getAttribute.
 *   No public getter exists — must read by name directly.
 *
 * DecalData fields:
 *   .decalId              Id
 *   .imageMappingType     ImageMappingType  (PLANAR | CYLINDRICAL)
 *   .image                ImageData         (.imageWidth, .imageHeight)
 *   .uvTransform          TransformUV       (.linear 2x2 Matrix, .translation Vector)
 *   .planeSystem          CoordSystem       (PLANAR only)
 *   .cylinder             Cylinder          (CYLINDRICAL only)
 *   .cylinderSystem       CoordSystem       (CYLINDRICAL only)
 *
 * UV transform encoding (createUvTransform):
 *   linear = scaleNonuniformly(±1/width_m, ±1/height_m) * rotate(angle)
 *   row norms recover |sx| = 1/width_m, |sy| = 1/height_m
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

            annotation { "Name" : "Debug: probe decal data", "Default" : false }
            definition.debugDecalProbe is boolean;

            if (definition.debugDecalProbe)
            {
                annotation {
                    "Name" : "Face to probe",
                    "Filter" : EntityType.FACE,
                    "MaxNumberOfPicks" : 1
                }
                definition.probeFace is Query;
            }
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

        // ---- debug: generic attributes ----
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

        // ---- debug: decal probe ----
        // Decals are stored under the named attribute "decals" as an array of DecalData.
        // The attribute name is a private constant (DECALS_ATTRIBUTE_NAME) in decalUtils.fs.
        // We read it directly via getAttribute.
        if (definition.showDebug && definition.debugDecalProbe &&
            !isQueryEmpty(context, definition.probeFace))
        {
            const probeEntities = evaluateQuery(context, definition.probeFace);
            const decalArray = getAttribute(context, {
                "entity" : probeEntities[0],
                "name"   : "decals"
            });

            if (decalArray == undefined || size(decalArray) == 0)
            {
                println("=== DECAL PROBE: no decals on selected face ===");
            }
            else
            {
                println("=== DECAL PROBE: " ~ toString(size(decalArray)) ~ " decal(s) found ===");

                for (var i = 0; i < size(decalArray); i += 1)
                {
                    const d = decalArray[i];
                    println("-- Decal [" ~ toString(i) ~ "] --");
                    println("  decalId:          " ~ toString(d.decalId));
                    println("  imageMappingType: " ~ toString(d.imageMappingType));
                    println("  image (full):     " ~ toString(d.image));

                    if (imageDataIsSpecified(d.image))
                    {
                        println("  image size: " ~ toString(d.image.imageWidth) ~
                                " x " ~ toString(d.image.imageHeight) ~ " px");
                    }

                    // Raw UV transform
                    println("  uvTransform.linear:      " ~ toString(d.uvTransform.linear));
                    println("  uvTransform.translation: " ~ toString(d.uvTransform.translation));

                    // Decoded UV transform
                    // UV linear = scaleNonuniformly(±1/w_m, ±1/h_m) * rotate(angle)
                    // Row norms: ||row0|| = 1/width_m, ||row1|| = 1/height_m
                    const m = d.uvTransform.linear;
                    const row0NormSq = m[0][0] ^ 2 + m[0][1] ^ 2;
                    const row1NormSq = m[1][0] ^ 2 + m[1][1] ^ 2;

                    if (row0NormSq > TOLERANCE.zeroLength ^ 2 &&
                        row1NormSq > TOLERANCE.zeroLength ^ 2)
                    {
                        const row0Norm = sqrt(row0NormSq);  // = 1/width_m
                        const row1Norm = sqrt(row1NormSq);  // = 1/height_m
                        const widthMm  = (1.0 / row0Norm) * 1000.0;
                        const heightMm = (1.0 / row1Norm) * 1000.0;

                        // angle: M[0][0] = sx*cos(a), M[0][1] = -sx*sin(a)
                        // → atan2(-M[0][1], M[0][0]) gives angle (assuming no mirror)
                        const angleRad = atan2(-m[0][1], m[0][0]);

                        println("  decoded width:  " ~ toString(widthMm)  ~ " mm");
                        println("  decoded height: " ~ toString(heightMm) ~ " mm");
                        println("  decoded angle:  " ~ toString(angleRad / degree) ~ " deg");
                        println("  mirrorH (approx): " ~ toString(m[0][0] * m[1][1] - m[0][1] * m[1][0] < 0));
                    }

                    // Mapping-type specific data
                    if (d.imageMappingType == ImageMappingType.PLANAR)
                    {
                        println("  planeSystem (full): " ~ toString(d.planeSystem));
                    }
                    else if (d.imageMappingType == ImageMappingType.CYLINDRICAL)
                    {
                        println("  cylinder (full):       " ~ toString(d.cylinder));
                        println("  cylinderSystem (full): " ~ toString(d.cylinderSystem));
                    }
                }
            }
        }
    });
