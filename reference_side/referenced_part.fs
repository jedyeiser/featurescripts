FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: offset_plus.fs (Offset+, every surface offset)
import(path : "742e5b3f04cc9115de8b6d8a", version : "cb9c79a1a023c8b7935e4cce");
// IMPORT: split_plus.fs (Split+, periphery and cap cuts)
import(path : "637854639ad04840dc6f998d", version : "6828f3e1413feb45737a2464");
// IMPORT: thicken_plus.fs (Thicken+, the Thickened mode)
import(path : "0c55868c5058e59d958b7223", version : "79e13566b2db89cc20dbcbfc");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: referenced_part_icon.svg (feature icon)
IconNamespace::import(path : "385dde1d1db1fd331e975189", version : "c6ae68c4419cca3f9189e4a0");

/**
 * Referenced part (2026-09-28): a part from reference surfaces, every offset and side read from ONE
 * reference point -- the chains done by hand before as Offset+ x N, Mutual Trim+, Enclose+ / Thicken+,
 * Split+ and a mirror.
 *
 * The REFERENCE POINT sets every direction: each offset and the thickness are signed, positive
 * toward the point and negative away from it (0 = the surface as it is). It must not lie on a
 * surface it measures.
 *
 * THICKENED  (base, mats, glass, top sheet): the profile is offset, thickened, then cut by the
 *            offset periphery (the part keeps the point's side of it) and by the caps. Thickening
 *            BEFORE the periphery cut keeps the side walls on the periphery instead of tilted
 *            along the profile's normals. The point lies inside the periphery and on the side the
 *            profile is thickened toward (a negative thickness goes the other way).
 * CONSTRAINED (cores): top, bottom and periphery are offset and enclosed together with the caps
 *            (and the mirror plane); the solid holding the point is kept and cut by the caps, so the
 *            caps always trim -- std Enclose drops the walls between touching pockets, so caps that
 *            only divide a closed volume would otherwise do nothing. The point lies inside the part.
 *
 * Caps (optional, both modes): surfaces or faces (offset with Offset+), or construction planes and
 * mate connectors (infinite planes, moved by the offset). The part keeps the point's side of each.
 *
 * Mirror plane (optional): the surfaces are one half. The half is cut at the plane (the side the
 * surfaces are on is kept -- a point ON the plane is fine), mirrored and united.
 *
 * The inputs are never changed: every offset is a new surface (0 = a copy), deleted at the end.
 *
 * Publishes (Extract variables), every key always present (empty when not applicable): output (the
 * part), profileFaces, oppositeFaces (Thickened: on the offset profile, and the thickness away from
 * it), topFaces, bottomFaces (Constrained), sideFaces (on the offset periphery), cap1Faces, cap2Faces.
 * Faces are sorted by where they lie (mirrored faces by their mirror image), so they survive the
 * splits and the union.
 */

/** How the part is made. */
export enum ReferencedPartType
{
    annotation { "Name" : "Thickened" }
    THICKENED,
    annotation { "Name" : "Constrained" }
    CONSTRAINED
}

/** A thickness: signed, default 1 mm. */
const SIGNED_THICKNESS_BOUNDS = { (millimeter) : [-500000, 1, 500000] } as LengthBoundSpec;

/** A face lies on a boundary within this distance. */
const ON_BOUNDARY_TOL = 1e-5 * meter;

/** A point on the mirror plane is moved this far off it to test which solid holds it. */
const MIRROR_NUDGE = 1e-5 * meter;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Referenced part",
        "Feature Type Description" : "Make a part from reference surfaces, all offsets and sides read from one reference point: Thickened (a profile thickened and cut by a periphery) or Constrained (top, bottom and periphery enclosed); optional caps and mirror plane.",
        "Filter Selector" : "allparts" }
export const referencedPart = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Part type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : ReferencedPartType.THICKENED }
        definition.partType is ReferencedPartType;

        annotation { "Name" : "Reference point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Sets every direction: positive offsets and thickness go toward it, negative away. Thickened: inside the periphery, on the side to thicken. Constrained: inside the part." }
        definition.referencePoint is Query;

        if (definition.partType == ReferencedPartType.THICKENED)
        {
            annotation { "Name" : "Profile", "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO && SketchObject.NO,
                        "Description" : "The surface the part is thickened from." }
            definition.profile is Query;

            annotation { "Name" : "Profile offset", "Description" : "Positive: toward the reference point. 0: the profile itself." }
            isLength(definition.profileOffset, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "Thickness", "Description" : "From the offset profile. Positive: toward the reference point; negative: away from it." }
            isLength(definition.thickness, SIGNED_THICKNESS_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Top", "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO && SketchObject.NO }
            definition.top is Query;

            annotation { "Name" : "Top offset", "Description" : "Positive: toward the reference point." }
            isLength(definition.topOffset, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "Bottom", "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO && SketchObject.NO }
            definition.bottom is Query;

            annotation { "Name" : "Bottom offset", "Description" : "Positive: toward the reference point." }
            isLength(definition.bottomOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }

        annotation { "Name" : "Periphery", "Filter" : (EntityType.FACE || (EntityType.BODY && BodyType.SHEET)) && ConstructionObject.NO && SketchObject.NO,
                    "Description" : "The side wall; the part keeps the reference point's side of it. Optional when thickening." }
        definition.periphery is Query;

        annotation { "Name" : "Periphery offset", "Description" : "Positive: toward the reference point." }
        isLength(definition.peripheryOffset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Cap 1", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Optional end: a surface, a face, a plane or a mate connector (planes and mate connectors are infinite)." }
        definition.cap1 is Query;

        annotation { "Name" : "Cap 1 offset", "Description" : "Positive: toward the reference point." }
        isLength(definition.cap1Offset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Cap 2", "Filter" : (EntityType.BODY && BodyType.SHEET && SketchObject.NO) || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Optional second end." }
        definition.cap2 is Query;

        annotation { "Name" : "Cap 2 offset", "Description" : "Positive: toward the reference point." }
        isLength(definition.cap2Offset, ZERO_DEFAULT_LENGTH_BOUNDS);

        annotation { "Name" : "Mirror plane", "Filter" : EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Optional: the surfaces are one half. The half is made, mirrored and united." }
        definition.mirrorPlane is Query;
    }
    {
        const thickened = definition.partType == ReferencedPartType.THICKENED;
        verifyNonemptyQuery(context, definition, "referencePoint", "Select the reference point.");
        if (thickened)
        {
            verifyNonemptyQuery(context, definition, "profile", "Select the profile surface.");
            if (abs(definition.thickness) < TOLERANCE.zeroLength * meter)
            {
                throw regenError("The thickness is zero.", ["thickness"]);
            }
        }
        else
        {
            verifyNonemptyQuery(context, definition, "top", "Select the top surface.");
            verifyNonemptyQuery(context, definition, "bottom", "Select the bottom surface.");
            verifyNonemptyQuery(context, definition, "periphery", "Select the periphery surface.");
        }
        const point = pointOf(context, definition.referencePoint);
        var mirror = undefined;
        if (!isQueryEmpty(context, definition.mirrorPlane))
        {
            mirror = planeOf(context, definition.mirrorPlane);
            if (mirror == undefined)
            {
                throw regenError("The mirror plane must be a plane, a planar face or a mate connector.", ["mirrorPlane"]);
            }
        }

        // The boundaries, as new surfaces: each face of the part is later sorted onto one of them.
        var boundaries = [];
        const hasPeriphery = !isQueryEmpty(context, definition.periphery);
        const side = hasPeriphery ? offsetSurface(context, id + "peripheryOffset", definition.periphery, definition.peripheryOffset, definition.referencePoint) : undefined;
        if (side != undefined)
        {
            boundaries = append(boundaries, { "key" : "sideFaces", "surface" : side, "distance" : 0 * meter });
        }
        var caps = [];
        for (var cap in [["cap1", "cap1Faces"], ["cap2", "cap2Faces"]])
        {
            if (!isQueryEmpty(context, definition[cap[0]]))
            {
                const tool = capTool(context, id + (cap[0] ~ "Offset"), definition[cap[0]], definition[cap[0] ~ "Offset"], definition.referencePoint, point, cap[0]);
                caps = append(caps, tool.tool);
                boundaries = append(boundaries, { "key" : cap[1], "surface" : tool.tool, "plane" : tool.plane, "distance" : 0 * meter });
            }
        }

        // The part so far, as a HISTORY query: a split's pieces are attributed to the feature that made the
        // original body, and an evaluated query of a split body resolves to nothing afterwards (2026-09-28).
        const solids = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID);
        if (thickened)
        {
            const profile = offsetSurface(context, id + "profileOffset", definition.profile, definition.profileOffset, definition.referencePoint);
            boundaries = concatenateArrays([boundaries, [
                            { "key" : "profileFaces", "surface" : profile, "distance" : 0 * meter },
                            { "key" : "oppositeFaces", "surface" : profile, "distance" : abs(definition.thickness) }]]);
            thickenPlus(context, id + "thicken", {
                        "operationType" : NewBodyOperationType.NEW,
                        "entities" : profile,
                        "sideReference" : definition.referencePoint,
                        "thicknessToward" : definition.thickness > 0 * meter ? definition.thickness : 0 * meter,
                        "thicknessAway" : definition.thickness < 0 * meter ? -definition.thickness : 0 * meter,
                        "swapSides" : false,
                        "keepTools" : true,
                        "checkCurvature" : true,
                        "debugPrint" : false
                    });
            if (side != undefined)
            {
                keepInside(context, id + "peripherySplit", solids, side, qNothing(), definition.referencePoint);
            }
        }
        else
        {
            const top = offsetSurface(context, id + "topOffset", definition.top, definition.topOffset, definition.referencePoint);
            const bottom = offsetSurface(context, id + "bottomOffset", definition.bottom, definition.bottomOffset, definition.referencePoint);
            boundaries = concatenateArrays([boundaries, [
                            { "key" : "topFaces", "surface" : top, "distance" : 0 * meter },
                            { "key" : "bottomFaces", "surface" : bottom, "distance" : 0 * meter }]]);
            enclosedPart(context, id, qUnion(concatenateArrays([[top, bottom, side], caps])), point, mirror);
        }

        // The caps always cut: keep the reference point's side of each.
        if (size(caps) > 0)
        {
            keepInside(context, id + "capSplit", solids, caps[0], size(caps) > 1 ? caps[1] : qNothing(), definition.referencePoint);
        }

        var result = qUnion(evaluateQuery(context, solids));
        if (mirror != undefined)
        {
            result = mirrorAndUnite(context, id, solids, mirror);
        }

        // Faces by the boundary they lie on, read while the boundary surfaces still exist.
        const named = sortFaces(context, result, boundaries, mirror);

        // Every temporary surface and plane goes; only the part stays.
        const temporary = qSubtraction(qCreatedBy(id, EntityType.BODY), result);
        if (!isQueryEmpty(context, temporary))
        {
            opDeleteBodies(context, id + "deleteTemporary", { "entities" : temporary });
        }

        var queries = {};
        for (var key in [["profileFaces", "Thickened: the faces on the offset profile.", DebugColor.GREEN],
                         ["oppositeFaces", "Thickened: the faces the thickness away from the offset profile.", DebugColor.RED],
                         ["topFaces", "Constrained: the faces on the offset top.", DebugColor.RED],
                         ["bottomFaces", "Constrained: the faces on the offset bottom.", DebugColor.GREEN],
                         ["sideFaces", "The faces on the offset periphery.", DebugColor.BLUE],
                         ["cap1Faces", "The faces on cap 1.", DebugColor.MAGENTA],
                         ["cap2Faces", "The faces on cap 2.", DebugColor.MAGENTA]])
        {
            queries[key[0]] = extractableQuery(named[key[0]] == undefined ? qNothing() : qUnion(named[key[0]]), key[1], key[2]);
        }
        const inputs = thickened ? [definition.profile, definition.periphery, definition.cap1, definition.cap2]
                                 : [definition.top, definition.bottom, definition.periphery, definition.cap1, definition.cap2];
        embedStandardOutputs(context, id, settledOutputs(context, {
                    "output" : result,
                    "outputDescription" : "The part",
                    "inputs" : qUnion(inputs),
                    "queries" : queries
                }));
    }, {
        "partType" : ReferencedPartType.THICKENED,
        "profile" : qNothing(),
        "profileOffset" : 0 * meter,
        "thickness" : 1 * millimeter,
        "top" : qNothing(),
        "topOffset" : 0 * meter,
        "bottom" : qNothing(),
        "bottomOffset" : 0 * meter,
        "periphery" : qNothing(),
        "peripheryOffset" : 0 * meter,
        "cap1" : qNothing(),
        "cap1Offset" : 0 * meter,
        "cap2" : qNothing(),
        "cap2Offset" : 0 * meter,
        "mirrorPlane" : qNothing()
    });

/** Offset+ of a surface toward the reference point (0 = a copy); the new surface. */
function offsetSurface(context is Context, id is Id, surface is Query, distance is ValueWithUnits, reference is Query) returns Query
{
    offsetPlus(context, id, {
                "offsetType" : OffsetPlusType.SURFACE,
                "surfaces" : surface,
                "distance" : distance,
                "sideReference" : reference,
                "towardReference" : true,
                "debugPrint" : false
            });
    return qCreatedBy(id, EntityType.BODY);
}

/**
 * A cap as a tool: a mate connector or construction plane becomes an infinite plane moved by the offset
 * toward the point; a surface or face is offset with Offset+.
 */
function capTool(context is Context, id is Id, cap is Query, offset is ValueWithUnits, reference is Query, point is Vector, name is string) returns map
{
    var pl = undefined;
    const frame = mateConnectorFrame(context, cap);
    if (frame != undefined)
    {
        pl = plane(frame);
    }
    else if (!isQueryEmpty(context, qConstructionFilter(cap, ConstructionObject.YES)))
    {
        pl = try silent(evPlane(context, { "face" : cap }));
    }
    if (pl == undefined)
    {
        return { "tool" : offsetSurface(context, id, cap, offset, reference), "plane" : undefined };
    }
    const s = dot(point - pl.origin, pl.normal);
    if (abs(s) < ON_BOUNDARY_TOL)
    {
        throw regenError("The reference point lies on " ~ (name == "cap1" ? "cap 1" : "cap 2") ~ "'s plane, so its sides cannot be told apart.", [name]);
    }
    const moved = plane(pl.origin + offset * (s > 0 * meter ? pl.normal : -pl.normal), pl.normal, pl.x);
    opPlane(context, id, { "plane" : moved });
    return { "tool" : qCreatedBy(id, EntityType.FACE), "plane" : moved };
}

/**
 * Constrained: the kernel enclose of every boundary (and the mirror plane), then the solid holding the
 * point. A point on the mirror plane is tested just off it on both sides; a point on the mirrored side is
 * mirrored back.
 */
function enclosedPart(context is Context, id is Id, boundaries is Query, point is Vector, mirror)
{
    var entities = [boundaries];
    var candidates = [point];
    if (mirror != undefined)
    {
        opPlane(context, id + "mirrorBoundary", { "plane" : mirror });
        entities = append(entities, qCreatedBy(id + "mirrorBoundary", EntityType.FACE));
        candidates = [point, point + MIRROR_NUDGE * mirror.normal, point - MIRROR_NUDGE * mirror.normal, mirrorPoint(point, mirror)];
    }
    var failed = false;
    try silent
    {
        opEnclose(context, id + "enclose", { "entities" : qUnion(entities) });
    }
    catch
    {
        failed = true;
    }
    if (failed)
    {
        throw regenError("The top, bottom, periphery and caps do not close off any volume: look for a gap between them, or a missing cap.", ["periphery"]);
    }
    const solids = evaluateQuery(context, qCreatedBy(id + "enclose", EntityType.BODY));
    var holding = [];
    for (var candidate in candidates)
    {
        holding = evaluateQuery(context, qContainsPoint(qUnion(solids), candidate));
        if (size(holding) > 0)
        {
            break;
        }
    }
    if (size(holding) == 0)
    {
        throw regenError("The reference point is not inside the enclosed volume (" ~ size(solids) ~ " solid(s) found): it must lie inside the part.", ["referencePoint"]);
    }
    const others = qSubtraction(qUnion(solids), qUnion(holding));
    if (!isQueryEmpty(context, others))
    {
        opDeleteBodies(context, id + "deleteOthers", { "entities" : others });
    }
}

/** Split+ of the solids (a history query) by one or two tools, keeping the reference point's side. */
function keepInside(context is Context, id is Id, solids is Query, startTool is Query, endTool is Query, reference is Query)
{
    splitPlus(context, id, {
                "splitType" : SplitPlusType.PART,
                "targets" : solids,
                "startTool" : startTool,
                "endTool" : endTool,
                "insideReference" : reference,
                "keep" : SplitPlusKeep.INSIDE,
                "useTrimmed" : false,
                "keepTools" : true,
                "debugPrintSides" : false
            });
}

/**
 * The half (solids: a history query, so it holds the split's pieces) is cut at the mirror plane (its side is
 * where its centroid is, so a point on the plane does not matter), mirrored and united with the mirror copy.
 */
function mirrorAndUnite(context is Context, id is Id, solids is Query, mirror is Plane) returns Query
{
    const s = dot(evApproximateCentroid(context, { "entities" : solids }) - mirror.origin, mirror.normal);
    if (abs(s) < ON_BOUNDARY_TOL)
    {
        throw regenError("The part is centred on the mirror plane: give one half of the surfaces, or remove the mirror plane.", ["mirrorPlane"]);
    }
    opPlane(context, id + "mirrorCut", { "plane" : mirror });
    opSplitPart(context, id + "mirrorSplit", {
                "targets" : solids,
                "tool" : qCreatedBy(id + "mirrorCut", EntityType.FACE),
                "keepTools" : true,
                "keepType" : SplitOperationKeepType.KEEP_ALL
            });
    var kept = [];
    var dropped = [];
    for (var piece in evaluateQuery(context, solids))
    {
        const ps = dot(evApproximateCentroid(context, { "entities" : piece }) - mirror.origin, mirror.normal);
        if (ps * s > 0 * meter * meter)
        {
            kept = append(kept, piece);
        }
        else
        {
            dropped = append(dropped, piece);
        }
    }
    if (size(dropped) > 0)
    {
        opDeleteBodies(context, id + "deleteOtherHalf", { "entities" : qUnion(dropped) });
    }
    opPattern(context, id + "mirror", {
                "entities" : qUnion(kept),
                "transforms" : [mirrorAcross(mirror)],
                "instanceNames" : ["mirror"]
            });
    const both = qUnion([qUnion(kept), qCreatedBy(id + "mirror", EntityType.BODY)]);
    opBoolean(context, id + "unite", { "tools" : both, "operationType" : BooleanOperationType.UNION });
    return qUnion(evaluateQuery(context, both));
}

/**
 * Each face of the part on the first boundary it lies on: at the boundary's distance from its surface (or
 * plane) within ON_BOUNDARY_TOL, tested at the face's point nearest its centroid and, with a mirror, at that
 * point's mirror image. Returns key -> array of faces.
 */
function sortFaces(context is Context, part is Query, boundaries is array, mirror) returns map
{
    var named = {};
    for (var face in evaluateQuery(context, qOwnedByBody(part, EntityType.FACE)))
    {
        const centroid = evApproximateCentroid(context, { "entities" : face });
        const at = evDistance(context, { "side0" : face, "side1" : centroid }).sides[0].point;
        const points = mirror == undefined ? [at] : [at, mirrorPoint(at, mirror)];
        for (var boundary in boundaries)
        {
            var on = false;
            for (var p in points)
            {
                const d = boundary.plane != undefined ? abs(dot(p - boundary.plane.origin, boundary.plane.normal))
                                                      : evDistance(context, { "side0" : p, "side1" : boundary.surface }).distance;
                if (abs(d - boundary.distance) < ON_BOUNDARY_TOL)
                {
                    on = true;
                    break;
                }
            }
            if (on)
            {
                named[boundary.key] = append(named[boundary.key] == undefined ? [] : named[boundary.key], face);
                break;
            }
        }
    }
    return named;
}

/** The reference point as a position: a vertex, or a mate connector's origin. */
function pointOf(context is Context, q is Query) returns Vector
{
    const frame = mateConnectorFrame(context, q);
    if (frame != undefined)
    {
        return frame.origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/** A mate connector's frame, or undefined for anything else. */
function mateConnectorFrame(context is Context, q is Query)
{
    if (isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
    {
        return undefined;
    }
    return evMateConnector(context, { "mateConnector" : q });
}

/** The Plane of a construction plane, planar face or mate connector (its XY plane); undefined otherwise. */
function planeOf(context is Context, q is Query)
{
    const frame = mateConnectorFrame(context, q);
    if (frame != undefined)
    {
        return plane(frame);
    }
    return try silent(evPlane(context, { "face" : q }));
}

/** A point reflected across a plane. */
function mirrorPoint(point is Vector, mirror is Plane) returns Vector
{
    return point - 2 * dot(point - mirror.origin, mirror.normal) * mirror.normal;
}
