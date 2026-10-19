FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");

/**
 * Test inputs for the Deform feature's stress test (scratch studio only). Builds, around the derived ski surface
 * `filleted_top_surf`, bodies that exercise the face rebuild: a thickened solid, a sheet with a hole, a closed periodic
 * sheet (cylinder wall), a solid cylinder, a sphere and a box. Each body is named `stress_...`.
 */
annotation { "Feature Type Name" : "Stress sources" }
export const stressSources = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
    }
    {
        const ski = getQueryVariable(context, "filleted_top_surf");
        if (size(evaluateQuery(context, ski)) == 0)
            throw regenError("filleted_top_surf not found");
        const skiBox = evBox3d(context, { "topology" : ski });
        const c = (skiBox.minCorner + skiBox.maxCorner) / 2;

        // 1. a solid: the ski surface thickened (solid with the same smooth faces, plus two skins and a rim)
        try
        {
            opThicken(context, id + "thick", { "entities" : ski, "thickness1" : 2 * millimeter, "thickness2" : 0 * millimeter, "keepTools" : true });
            setProperty(context, { "entities" : qCreatedBy(id + "thick", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "stress_thick_solid" });
        }
        catch (e)
        {
            println("thick failed: " ~ toString(e));
        }

        // 2. a sheet with a hole: a copy of the ski surface minus a vertical cylinder
        try
        {
            opPattern(context, id + "copy", { "entities" : ski, "transforms" : [identityTransform()], "instanceNames" : ["holed"] });
            const copy = qCreatedBy(id + "copy", EntityType.BODY);
            const hole = vector(c[0] - 300 * millimeter, c[1], c[2]);
            fCylinder(context, id + "holeTool", { "bottomCenter" : hole - vector(0, 0, 100 * millimeter), "topCenter" : hole + vector(0, 0, 100 * millimeter), "radius" : 12 * millimeter });
            opBoolean(context, id + "hole", { "tools" : qCreatedBy(id + "holeTool", EntityType.BODY), "targets" : copy, "operationType" : BooleanOperationType.SUBTRACTION });
            setProperty(context, { "entities" : qBodyType(qCreatedBy(id + "copy", EntityType.BODY), BodyType.SHEET), "propertyType" : PropertyType.NAME, "value" : "stress_holed_sheet" });
        }
        catch (e)
        {
            println("holed sheet failed: " ~ toString(e));
        }

        // 3. a solid cylinder (periodic wall) and 4. its wall alone as a closed sheet
        const cylinderAt = vector(c[0] + 250 * millimeter, c[1], c[2]);
        try
        {
            fCylinder(context, id + "cyl", { "bottomCenter" : cylinderAt - vector(0, 0, 25 * millimeter), "topCenter" : cylinderAt + vector(0, 0, 25 * millimeter), "radius" : 15 * millimeter });
            setProperty(context, { "entities" : qCreatedBy(id + "cyl", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "stress_cylinder_solid" });
        }
        catch (e)
        {
            println("cylinder failed: " ~ toString(e));
        }
        try
        {
            const wallAt = vector(c[0] - 100 * millimeter, c[1], c[2]);
            fCylinder(context, id + "wallSolid", { "bottomCenter" : wallAt - vector(0, 0, 25 * millimeter), "topCenter" : wallAt + vector(0, 0, 25 * millimeter), "radius" : 15 * millimeter });
            opExtractSurface(context, id + "wall", { "faces" : qGeometry(qOwnedByBody(qCreatedBy(id + "wallSolid", EntityType.BODY), EntityType.FACE), GeometryType.CYLINDER) });
            opDeleteBodies(context, id + "dropWall", { "entities" : qCreatedBy(id + "wallSolid", EntityType.BODY) });
            setProperty(context, { "entities" : qCreatedBy(id + "wall", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "stress_cylinder_wall" });
        }
        catch (e)
        {
            println("cylinder wall failed: " ~ toString(e));
        }

        // 5. a sphere and 6. a box
        try
        {
            fSphere(context, id + "sphere", { "center" : vector(c[0] + 500 * millimeter, c[1], c[2]), "radius" : 20 * millimeter });
            setProperty(context, { "entities" : qCreatedBy(id + "sphere", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "stress_sphere" });
        }
        catch (e)
        {
            println("sphere failed: " ~ toString(e));
        }
        try
        {
            fCuboid(context, id + "box", { "corner1" : vector(c[0] - 610 * millimeter, c[1] - 20 * millimeter, c[2] - 15 * millimeter),
                                          "corner2" : vector(c[0] - 490 * millimeter, c[1] + 20 * millimeter, c[2] + 15 * millimeter) });
            setProperty(context, { "entities" : qCreatedBy(id + "box", EntityType.BODY), "propertyType" : PropertyType.NAME, "value" : "stress_box" });
        }
        catch (e)
        {
            println("box failed: " ~ toString(e));
        }
    });
