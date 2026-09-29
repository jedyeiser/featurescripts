FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * MBD probe (2026-09-29, throwaway): does a custom feature's setDimensionedEntities DISTANCE reach the
 * Inspection table / MBD / drawings when the two faces are not parallel planes?
 *
 * Builds a 100 x 60 mm sheet (flat, cylindrical, extruded spline or double-curved B-spline), thickens it
 * by Thickness, deletes the sheet, then registers Thickness between the two large faces of the part --
 * as two queries, as one face pair, or not at all. With Auto tolerance on (and the user not having
 * made Thickness tolerant) it supplies +/- 0.1 mm itself (the custom-feature route).
 */

export enum MbdProbeShape
{
    annotation { "Name" : "Flat" }
    FLAT,
    annotation { "Name" : "Cylinder" }
    CYLINDER,
    annotation { "Name" : "Extruded spline" }
    EXTRUDED_SPLINE,
    annotation { "Name" : "Freeform (double curved)" }
    FREEFORM
}

export enum MbdProbeRegistration
{
    annotation { "Name" : "Two queries" }
    QUERIES,
    annotation { "Name" : "Face pairs" }
    FACE_PAIRS,
    annotation { "Name" : "None" }
    NONE
}

annotation { "Feature Type Name" : "MBD probe" }
export const mbdProbe = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Shape", "Default" : MbdProbeShape.FLAT }
        definition.shape is MbdProbeShape;

        annotation { "Name" : "Thickness", "UIHint" : UIHint.CAN_BE_TOLERANT }
        isLength(definition.thickness, NONNEGATIVE_LENGTH_BOUNDS);

        annotation { "Name" : "Registration", "Default" : MbdProbeRegistration.QUERIES }
        definition.registration is MbdProbeRegistration;

        annotation { "Name" : "Auto tolerance (+/- 0.1 mm)", "Default" : true }
        definition.autoTolerance is boolean;
    }
    {
        const sheet = probeSheet(context, id + "sheet", definition.shape);
        opThicken(context, id + "thicken", {
                    "entities" : sheet,
                    "thickness1" : definition.thickness,
                    "thickness2" : 0 * meter
                });
        opDeleteBodies(context, id + "deleteSheet", { "entities" : sheet });

        // The two large faces: the thickened copy of the sheet and its offset.
        var faces = evaluateQuery(context, qCreatedBy(id + "thicken", EntityType.FACE));
        faces = sort(faces, function(a, b) { return (evArea(context, { "entities" : b }) - evArea(context, { "entities" : a })).value; });
        const faceA = faces[0];
        const faceB = faces[1];
        const typeA = evSurfaceDefinition(context, { "face" : faceA }).surfaceType;
        const typeB = evSurfaceDefinition(context, { "face" : faceB }).surfaceType;

        var dim = { "parameterId" : "thickness", "dimensionType" : FeatureDimensionType.DISTANCE };
        if (definition.registration == MbdProbeRegistration.QUERIES)
        {
            dim.queries = [faceA, faceB];
        }
        else if (definition.registration == MbdProbeRegistration.FACE_PAIRS)
        {
            dim.facePairs = [[faceA, faceB]];
        }

        const tolerant = getTolerantParameterIds(context, {});
        const userTolerant = tolerant["thickness"] != undefined;
        if (definition.autoTolerance && !userTolerant)
        {
            dim.tolerances = { "toleranceType" : ToleranceType.SYMMETRICAL, "upper" : 0.1 * millimeter, "lower" : 0.1 * millimeter };
            dim.nominal = definition.thickness;
        }

        if (definition.registration != MbdProbeRegistration.NONE)
        {
            setDimensionedEntities(context, dim);
        }

        const message = "MBD probe: " ~ definition.shape ~ " faces " ~ typeA ~ " / " ~ typeB ~ ", registration " ~
            definition.registration ~ ", user tolerant " ~ userTolerant ~ ", auto tolerance " ~ (dim.tolerances != undefined);
        println(message);
        reportFeatureInfo(context, id, message);
    }, { "autoTolerance" : true, "registration" : MbdProbeRegistration.QUERIES });

/** A 100 x 60 mm sheet body at the origin; returns a query for it. */
function probeSheet(context is Context, id is Id, shape is MbdProbeShape) returns Query
{
    if (shape == MbdProbeShape.FREEFORM)
    {
        // 4 x 4 cubic patch, x 0..100, y 0..60, bumps in z.
        const heights = [[0, 4, 2, 0], [3, 12, 9, 2], [1, 8, 14, 5], [0, 3, 6, 1]];
        var points = [];
        for (var i = 0; i < 4; i += 1)
        {
            var row = [];
            for (var j = 0; j < 4; j += 1)
            {
                row = append(row, vector(i * 100 / 3, j * 20, heights[i][j]) * millimeter);
            }
            points = append(points, row);
        }
        opCreateBSplineSurface(context, id + "surface", {
                    "bSplineSurface" : bSplineSurface({
                            "uDegree" : 3, "vDegree" : 3, "isUPeriodic" : false, "isVPeriodic" : false,
                            "controlPoints" : controlPointMatrix(points),
                            "uKnots" : knotArray([0, 0, 0, 0, 1, 1, 1, 1]),
                            "vKnots" : knotArray([0, 0, 0, 0, 1, 1, 1, 1])
                        })
                });
        return qCreatedBy(id + "surface", EntityType.BODY);
    }

    // Profile in the XZ plane (Front), extruded 60 mm along +Y.
    const sketch = newSketchOnPlane(context, id + "sketch", { "sketchPlane" : plane(vector(0, 0, 0) * meter, vector(0, -1, 0), vector(1, 0, 0)) });
    if (shape == MbdProbeShape.FLAT)
    {
        skLineSegment(sketch, "profile", { "start" : vector(0, 0) * millimeter, "end" : vector(100, 0) * millimeter });
    }
    else if (shape == MbdProbeShape.CYLINDER)
    {
        skArc(sketch, "profile", { "start" : vector(0, 0) * millimeter, "mid" : vector(50, 15) * millimeter, "end" : vector(100, 0) * millimeter });
    }
    else
    {
        skFitSpline(sketch, "profile", { "points" : mapArray([vector(0, 0), vector(30, 10), vector(60, 4), vector(100, 14)], function(p) { return p * millimeter; }) });
    }
    skSolve(sketch);
    opExtrude(context, id + "extrude", {
                "entities" : qCreatedBy(id + "sketch", EntityType.EDGE),
                "direction" : vector(0, 1, 0),
                "endBound" : BoundingType.BLIND,
                "endDepth" : 60 * millimeter
            });
    opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(id + "sketch", EntityType.BODY) });
    return qCreatedBy(id + "extrude", EntityType.BODY);
}
