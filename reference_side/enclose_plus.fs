FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: enclose_plus_icon.svg (feature icon)
IconNamespace::import(path : "03964942420afcb28f2079d1", version : "7d6273daa700a3a85e83c6ee");

/**
 * Enclose+ (2026-09-27): Onshape's Enclose -- a solid from the volume that surfaces and caps close
 * off -- with mate connector caps, an inside point, and an optional mirror.
 *
 * How std Enclose decides "inside" (measured): it has no inside. It finds every pocket the inputs
 * close off from the outside and returns them as solids, interior walls dropped (a tube capped at
 * z -20 / +20 with an extra plane at 0 still gives ONE solid). Construction planes are infinite.
 *
 * Enclose+ adds:
 *     Caps          construction planes, planar or other faces, surfaces, and mate connectors (their
 *                   XY plane, infinite like a construction plane).
 *     Inside point  keeps only the solid(s) containing it (a stray pocket the surfaces also close
 *                   off is dropped); an error names a point in no solid (a gap in the surfaces).
 *                   Without one, every solid is kept, as in std Enclose.
 *     Mirror plane  the surfaces are one half: the half is enclosed against the plane, mirrored and
 *                   united -- the union merges faces split by the plane where they lie on one plane
 *                   or cylinder (a mirrored free-form surface keeps a seam edge). A point on the
 *                   mirrored side is mirrored too.
 *
 * Publishes (Extract variables): output (the solid), surfaceFaces, capFaces, seamEdges (edges on
 * the mirror plane after the union).
 */
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Enclose+",
        "Feature Type Description" : "Make a part from the volume surfaces and caps (planes, mate connectors, faces) close off; an inside point picks the region, and an optional mirror plane encloses one half, mirrors it and unites the two.",
        "Filter Selector" : "allparts" }
export const enclosePlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Surfaces", "Filter" : ((EntityType.BODY && BodyType.SHEET) || EntityType.FACE) && SketchObject.NO && ConstructionObject.NO,
                    "Description" : "The surfaces bounding the part (top, bottom, sides...)." }
        definition.surfaces is Query;

        annotation { "Name" : "Caps", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR,
                    "Description" : "Optional ends: planes and mate connectors (infinite), faces or surfaces." }
        definition.caps is Query;

        annotation { "Name" : "Inside point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "A point inside the part. Keeps only the solid containing it. Empty: every enclosed solid is kept." }
        definition.insidePoint is Query;

        annotation { "Name" : "Mirror plane", "Filter" : EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Optional: the surfaces are one half. It is enclosed against this plane, mirrored and united." }
        definition.mirrorPlane is Query;

        annotation { "Name" : "Keep tools", "Default" : false, "Description" : "Keep the input surfaces (planes and mate connectors are never deleted)." }
        definition.keepTools is boolean;
    }
    {
        verifyNonemptyQuery(context, definition, "surfaces", "Select the surfaces that bound the part.");
        const surfaceFaces = facesOf(definition.surfaces);

        // Caps: mate connectors become (infinite) planes; everything else is used as picked.
        var tempPlanes = [];
        var caps = [];
        var capPlanes = [];
        var k = 0;
        for (var cap in evaluateQuery(context, definition.caps))
        {
            const frame = mateConnectorFrame(context, cap);
            if (frame != undefined)
            {
                const planeId = id + ("capPlane" ~ k);
                opPlane(context, planeId, { "plane" : plane(frame) });
                tempPlanes = append(tempPlanes, qCreatedBy(planeId, EntityType.BODY));
                caps = append(caps, qCreatedBy(planeId, EntityType.FACE));
                capPlanes = append(capPlanes, plane(frame));
            }
            else
            {
                caps = append(caps, cap);
                const pl = try silent(evPlane(context, { "face" : cap }));
                if (pl != undefined && isQueryEmpty(context, qEntityFilter(cap, EntityType.BODY)))
                {
                    capPlanes = append(capPlanes, pl);
                }
            }
            k += 1;
        }

        // Mirror: enclose one half against the plane.
        var mirror = undefined;
        var boundaries = concatenateArrays([[definition.surfaces], caps]);
        if (!isQueryEmpty(context, definition.mirrorPlane))
        {
            mirror = mirrorPlaneOf(context, definition.mirrorPlane);
            if (mirror == undefined)
            {
                throw regenError("The mirror plane must be a plane, a planar face or a mate connector.", ["mirrorPlane"]);
            }
            opPlane(context, id + "mirrorPlane", { "plane" : mirror });
            tempPlanes = append(tempPlanes, qCreatedBy(id + "mirrorPlane", EntityType.BODY));
            boundaries = append(boundaries, qCreatedBy(id + "mirrorPlane", EntityType.FACE));
        }

        const trackedCaps = startTracking(context, qUnion(caps));
        var failure = undefined;
        try silent
        {
            opEnclose(context, id + "enclose", { "entities" : qUnion(boundaries) });
        }
        catch
        {
            failure = "The surfaces and caps do not close off any volume: look for a gap, or a missing cap.";
        }
        if (failure != undefined)
        {
            throw regenError(failure, ["surfaces"]);
        }
        const solids = evaluateQuery(context, qCreatedBy(id + "enclose", EntityType.BODY));

        // Keep the solid(s) containing the inside point.
        var kept = solids;
        const point = insidePointOf(context, definition.insidePoint);
        if (point != undefined)
        {
            var holding = evaluateQuery(context, qContainsPoint(qUnion(solids), point));
            if (size(holding) == 0 && mirror != undefined)
            {
                holding = evaluateQuery(context, qContainsPoint(qUnion(solids), mirrorPoint(point, mirror)));
            }
            if (size(holding) == 0)
            {
                throw regenError("The inside point is not in any enclosed solid (" ~ size(solids) ~ " found): the surfaces leave a gap around it, or it lies outside the part.",
                    ["insidePoint"]);
            }
            kept = holding;
            const dropped = qSubtraction(qUnion(solids), qUnion(holding));
            if (!isQueryEmpty(context, dropped))
            {
                opDeleteBodies(context, id + "deleteOthers", { "entities" : dropped });
                reportFeatureInfo(context, id, (size(solids) - size(holding)) ~ " other enclosed solid(s) did not contain the inside point and were removed.");
            }
        }

        // Mirror the half and unite.
        var result = qUnion(kept);
        var seamEdges = qNothing();
        if (mirror != undefined)
        {
            opPattern(context, id + "mirror", {
                        "entities" : result,
                        "transforms" : [mirrorAcross(mirror)],
                        "instanceNames" : ["mirror"]
                    });
            const both = qUnion([result, qCreatedBy(id + "mirror", EntityType.BODY)]);
            opBoolean(context, id + "unite", { "tools" : both, "operationType" : BooleanOperationType.UNION });
            result = qUnion(evaluateQuery(context, both));
            seamEdges = qCoincidesWithPlane(qOwnedByBody(result, EntityType.EDGE), mirror);
        }

        if (size(tempPlanes) > 0)
        {
            opDeleteBodies(context, id + "deletePlanes", { "entities" : qUnion(tempPlanes) });
        }
        if (!definition.keepTools)
        {
            const inputSheets = qConstructionFilter(qBodyType(qUnion([qOwnerBody(surfaceFaces), qOwnerBody(facesOf(definition.caps)),
                                    qEntityFilter(definition.caps, EntityType.BODY)]), BodyType.SHEET), ConstructionObject.NO);
            if (!isQueryEmpty(context, inputSheets))
            {
                opDeleteBodies(context, id + "deleteTools", { "entities" : inputSheets });
            }
        }

        // Faces: those on a cap plane or grown from a cap are cap faces; the rest come from the surfaces.
        var capFaceList = [qEntityFilter(qUnion([trackedCaps]), EntityType.FACE)];
        for (var pl in capPlanes)
        {
            capFaceList = append(capFaceList, qCoincidesWithPlane(qOwnedByBody(result, EntityType.FACE), pl));
            if (mirror != undefined)
            {
                capFaceList = append(capFaceList, qCoincidesWithPlane(qOwnedByBody(result, EntityType.FACE), mirrorPlaneAcross(pl, mirror)));
            }
        }
        const capFaces = qIntersection([qUnion(capFaceList), qOwnedByBody(result, EntityType.FACE)]);
        const bodyFaces = qSubtraction(qOwnedByBody(result, EntityType.FACE), capFaces);

        embedStandardOutputs(context, id, settledOutputs(context, {
                    "output" : result,
                    "outputDescription" : "The enclosed part",
                    "inputs" : qUnion([definition.surfaces, definition.caps]),
                    "queries" : {
                        "surfaceFaces" : extractableQuery(bodyFaces, "The part's faces that come from the surfaces.", DebugColor.CYAN),
                        "capFaces" : extractableQuery(capFaces, "The part's faces on the caps.", DebugColor.MAGENTA),
                        "seamEdges" : extractableQuery(seamEdges, "Edges on the mirror plane after the union (empty without a mirror, or where faces merged).", DebugColor.RED)
                    }
                }));
    }, {
        "caps" : qNothing(),
        "insidePoint" : qNothing(),
        "mirrorPlane" : qNothing(),
        "keepTools" : false
    });

/** A mate connector's frame, or undefined for anything else. */
function mateConnectorFrame(context is Context, q is Query)
{
    if (isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
    {
        return undefined;
    }
    return evMateConnector(context, { "mateConnector" : q });
}

/** The mirror Plane of a construction plane, planar face or mate connector (its XY plane). */
function mirrorPlaneOf(context is Context, q is Query)
{
    const frame = mateConnectorFrame(context, q);
    if (frame != undefined)
    {
        return plane(frame);
    }
    return try silent(evPlane(context, { "face" : q }));
}

/** The inside point as a position: a vertex, or a mate connector's origin. */
function insidePointOf(context is Context, q is Query)
{
    if (isQueryEmpty(context, q))
    {
        return undefined;
    }
    const frame = mateConnectorFrame(context, q);
    if (frame != undefined)
    {
        return frame.origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/** A point reflected across a plane. */
function mirrorPoint(point is Vector, mirror is Plane) returns Vector
{
    return point - 2 * dot(point - mirror.origin, mirror.normal) * mirror.normal;
}

/** A plane reflected across a mirror plane. */
function mirrorPlaneAcross(pl is Plane, mirror is Plane) returns Plane
{
    const n = pl.normal - 2 * dot(pl.normal, mirror.normal) * mirror.normal;
    return plane(mirrorPoint(pl.origin, mirror), n);
}
