FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

annotation { "Feature Type Name" : "Composite intersection", "Feature Type Description" : "" }
export const compositeIntersect = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        
        annotation { "Name" : "Solid Body", "Filter" : EntityType.BODY && BodyType.SOLID && ConstructionObject.NO && SketchObject.NO }
        definition.solidBody is Query;

        annotation { "Name" : "Composite Part", "Filter" : EntityType.BODY && BodyType.COMPOSITE && ConstructionObject.NO && SketchObject.NO }
        definition.compositePart is Query;
    
    }
    {
        // --- Step 1: Extract the closed surface shell from the solid body ---
        // This gives us a sheet body with outward-pointing normals — the same
        // surface the solid "lives inside of". We use this as the split tool.
        const surfaceId = id + "surface";
        opExtractSurface(context, surfaceId, {
            "faces" : qOwnedByBody(definition.solidBody, EntityType.FACE)
        });
        const surfaceBody = qCreatedBy(surfaceId, EntityType.BODY);

        // --- Step 2: Copy all constituent bodies before any modification ---
        // We do this as a single batch operation before any splits, so all
        // copies are made from the original untouched composite geometry.
        const allCopiesId = id + "allCopies";
        opPattern(context, allCopiesId, {
            "entities"      : qContainedInCompositeParts(definition.compositePart),
            "transforms"    : [identityTransform()],
            "instanceNames" : ["1"]
        });
        const copiedConstituents = evaluateQuery(context, qCreatedBy(allCopiesId, EntityType.BODY));

        if (size(copiedConstituents) == 0)
            throw regenError("Failed to copy composite constituent bodies.");

        var resultBodies = [];

        for (var i = 0; i < size(copiedConstituents); i += 1)
        {
            const copy = copiedConstituents[i];
            const splitId = id + ("split" ~ i);

            // --- Step 3a: Attempt to split the copy with the surface ---
            // opSplitPart throws SPLIT_FAILED if the surface doesn't intersect
            // the body at all (fully inside or fully outside).
            var bodyWasSplit = false;
            try
            {
                opSplitPart(context, splitId, {
                    "targets"   : copy,
                    "tool"      : surfaceBody,
                    "keepTools" : true   // preserve surfaceBody for next iteration
                });
                bodyWasSplit = true;
            }
            catch { }

            if (bodyWasSplit)
            {
                // The surface intersected this body and partitioned it.
                // qSplitBy with backBody=true returns the piece on the negative-normal
                // side of the surface — i.e., inside the closed solid shell.
                const insideBody  = qSplitBy(splitId, EntityType.BODY, true);
                const outsideBody = qSplitBy(splitId, EntityType.BODY, false);

                resultBodies = append(resultBodies, insideBody);
                try(opDeleteBodies(context, id + ("delOut" ~ i), { "entities" : outsideBody }));
            }
            else //body was not split
            {
                // No split: body is either fully inside or fully outside.
                // Disambiguate with a boolean INTERSECT test on throwaway copies.
                // This works cleanly here because we're operating on free bodies,
                // not composite-owned bodies (the root cause of the earlier failures).

                const testCopyId = id + ("testCopy" ~ i);
                opPattern(context, testCopyId, {
                    "entities"      : copy,
                    "transforms"    : [identityTransform()],
                    "instanceNames" : ["1"]
                });
                const testCopy = qCreatedBy(testCopyId, EntityType.BODY);

                const testSolidId = id + ("testSolid" ~ i);
                opPattern(context, testSolidId, {
                    "entities"      : definition.solidBody,
                    "transforms"    : [identityTransform()],
                    "instanceNames" : ["1"]
                });
                const testSolid = qCreatedBy(testSolidId, EntityType.BODY);

                var isFullyInside = false;
                try
                {
                    opBoolean(context, id + ("testBool" ~ i), {
                        "operationType" : BooleanOperationType.INTERSECTION,
                        "targets"       : testCopy,
                        "tools"         : testSolid,
                        "keepTools"     : false   // testSolid consumed on success
                    });
                    isFullyInside = true;
                }
                catch { }

                if (isFullyInside)
                {
                    // testSolid was consumed by the boolean. testCopy is now
                    // the intersection (same geometry as copy, since it's fully inside).
                    // Delete testCopy and keep the original copy.
                    try(opDeleteBodies(context, id + ("delTC" ~ i), { "entities" : testCopy }));
                    resultBodies = append(resultBodies, copy);
                }
                else
                {
                    // Boolean failed — both temp bodies still exist, delete them.
                    // Also delete the original copy — it's fully outside the solid.
                    try(opDeleteBodies(context, id + ("delTC" ~ i),  { "entities" : testCopy }));
                    try(opDeleteBodies(context, id + ("delTS" ~ i),  { "entities" : testSolid }));
                    try(opDeleteBodies(context, id + ("delCopy" ~ i), { "entities" : copy }));
                }
            }
        }

        // --- Step 4: Clean up the trimming surface ---
        try(opDeleteBodies(context, id + "delSurface", { "entities" : surfaceBody }));

        if (size(resultBodies) == 0)
            throw regenError("No constituent bodies intersect the solid body.");

        // --- Step 5: Assemble surviving bodies into a new composite part ---
        var unionQuery = resultBodies[0];
        for (var j = 1; j < size(resultBodies); j += 1)
            unionQuery = qUnion([unionQuery, resultBodies[j]]);

        opCreateCompositePart(context, id + "result", {
            "bodies" : unionQuery,
            "closed" : false
        });
    });
