FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");

/**
 * Test fixture for Join profile surfaces (test-only; devtools/onshape/build_reference_side_tests.py, cases K*).
 *
 * Builds, around x = 30000 mm: an INSIDE sheet (z 0, one face) and an OUTSIDE sheet (z 20) that has a seam at x = 30000
 * with THREE faces to its left (bands at y -50..-17, -17..17, 17..50) and ONE face to its right. A split on that seam
 * therefore cuts the outside profile into three edges and the inside profile into one: the case that made the loft
 * between them three faces instead of one (2026-10-21, "bf_inside_bump" of the TAC design).
 *
 * Output bodies (by feature id): "inside", "outside", point "pt" (inside point at (30050, 0, -10)), plane "seam" (x = 30000).
 */
annotation { "Feature Type Name" : "JPS fixture" }
export const jpsFixture = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
    }
    {
        const x0 = 30000 * millimeter;
        // a sheet = the bottom face of a block, extracted
        const sheetAt = function(name is string, z is ValueWithUnits)
            {
                fCuboid(context, id + (name ~ "Box"), { "corner1" : vector(x0 - 100 * millimeter, -50 * millimeter, z), "corner2" : vector(x0 + 100 * millimeter, 50 * millimeter, z + 10 * millimeter) });
                opExtractSurface(context, id + name, { "faces" : qContainsPoint(qOwnedByBody(qCreatedBy(id + (name ~ "Box"), EntityType.BODY), EntityType.FACE), vector(x0, 0 * meter, z)) });
                opDeleteBodies(context, id + (name ~ "Drop"), { "entities" : qCreatedBy(id + (name ~ "Box"), EntityType.BODY) });
            };
        sheetAt("inside", 0 * millimeter);
        sheetAt("outside", 20 * millimeter);

        // the outside's seam at x0 and the three bands to its left
        opPlane(context, id + "seam", { "plane" : plane(vector(x0, 0 * meter, 0 * meter), vector(1, 0, 0)) });
        opSplitFace(context, id + "splitSeam", { "faceTargets" : qOwnedByBody(qCreatedBy(id + "outside", EntityType.BODY), EntityType.FACE),
                                                  "planeTools" : qCreatedBy(id + "seam", EntityType.FACE) });
        const z = 20 * millimeter;
        for (var k, y in [-17, 17])
        {
            opPolyline(context, id + ("band" ~ k), { "points" : [vector(x0 - 110 * millimeter, y * millimeter, z), vector(x0, y * millimeter, z)] });
        }
        opSplitFace(context, id + "splitBands", { "faceTargets" : qContainsPoint(qOwnedByBody(qCreatedBy(id + "outside", EntityType.BODY), EntityType.FACE), vector(x0 - 50 * millimeter, 0 * meter, z)),
                                                   "edgeTools" : qUnion([qCreatedBy(id + "band0", EntityType.EDGE), qCreatedBy(id + "band1", EntityType.EDGE)]),
                                                   "projectionType" : ProjectionType.NORMAL_TO_TARGET });
        opDeleteBodies(context, id + "dropBands", { "entities" : qUnion([qCreatedBy(id + "band0", EntityType.BODY), qCreatedBy(id + "band1", EntityType.BODY)]) });

        opPoint(context, id + "pt", { "point" : vector(x0 + 50 * millimeter, 0 * meter, -10 * millimeter) });
    });
