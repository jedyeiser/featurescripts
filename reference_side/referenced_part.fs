FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: reference_side_utils.fs
import(path : "9aebe5ead538258b285aec19", version : "51d2aab9634029141009803d");
// IMPORT: offset_plus.fs (Offset+, every surface offset)
import(path : "742e5b3f04cc9115de8b6d8a", version : "a70e95e0c3507503d79c7fe9");
// IMPORT: split_plus.fs (Split+, periphery and cap cuts)
import(path : "637854639ad04840dc6f998d", version : "f803257d22d5a7ba34163ec7");
// IMPORT: thicken_plus.fs (Thicken+, the Thickened mode)
import(path : "0c55868c5058e59d958b7223", version : "67f2adc834b29839d2ba5e44");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
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
 * surfaces are on is kept -- a point ON the plane is fine), mirrored and united. With a profile offset the
 * thickened layer is built from the unoffset profile (see thickenLayer) so its faces meet the plane exactly.
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

/** A half that reaches less than this past the mirror plane (box padding included) is not cut at the plane. */
const MIRROR_CUT_REACH = 1e-6 * meter;

/** A piece of the mirror cut smaller than this fraction of the largest piece is a sliver, not part of the part. */
const SLIVER_VOLUME_RATIO = 1e-4;

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

        annotation { "Name" : "Output name", "Description" : "Optional. Names the part; leave empty for the default name." }
        definition.outputName is string;

        annotation { "Name" : "Print diagnostics", "Default" : false,
                    "Description" : "Prints step markers and measurements to the notices console: the profile's edges and the part against the mirror plane. For tracking down a failed boolean." }
        definition.debugPrint is boolean;
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

        // Constrained: the top and bottom offsets are made BEFORE the periphery and caps. opEnclose is sensitive to the order
        // the boundary bodies were created in: on the real core it fails (BOOLEAN_INVALID) in every one of the 8 orders that
        // make the periphery before both top and bottom, and succeeds in all 16 others; the order the entities are passed
        // in does not matter (2026-10-09).
        var top = undefined;
        var bottom = undefined;
        if (!thickened)
        {
            top = offsetSurface(context, id + "topOffset", definition.top, definition.topOffset, definition.referencePoint);
            bottom = offsetSurface(context, id + "bottomOffset", definition.bottom, definition.bottomOffset, definition.referencePoint);
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
        // The sheet mirror (see mirroredProfile) cuts ONE full-width solid, so a cap or the periphery must act the same on
        // both halves: a plane that contains the mirror plane's normal, or a surface that spans the plane. Anything else
        // is one-sided and keeps the older route (cut the half, mirror the solid, unite).
        var symmetricTools = true;
        for (var cap in [["cap1", "cap1Faces"], ["cap2", "cap2Faces"]])
        {
            if (!isQueryEmpty(context, definition[cap[0]]))
            {
                const tool = capTool(context, id + (cap[0] ~ "Offset"), definition[cap[0]], definition[cap[0] ~ "Offset"], definition.referencePoint, point, cap[0]);
                caps = append(caps, tool.tool);
                boundaries = append(boundaries, { "key" : cap[1], "surface" : tool.tool, "plane" : tool.plane, "distance" : 0 * meter });
                if (mirror != undefined)
                {
                    const symmetric = tool.plane != undefined ? abs(dot(tool.plane.normal, mirror.normal)) < 1e-6 : spansPlane(context, tool.tool, mirror);
                    symmetricTools = symmetricTools && symmetric;
                    if (definition.debugPrint)
                    {
                        debugStep(context, cap[0] ~ ": " ~ (tool.plane != undefined ? "plane" : "surface") ~ ", " ~ (symmetric ? "symmetric about the mirror plane" : "one-sided"));
                    }
                }
            }
        }
        if (mirror != undefined && side != undefined)
        {
            const sideSymmetric = spansPlane(context, side, mirror);
            symmetricTools = symmetricTools && sideSymmetric;
            if (definition.debugPrint)
            {
                debugStep(context, "periphery: " ~ (sideSymmetric ? "spans the mirror plane" : "one-sided"));
            }
        }
        // Thickened with a mirror plane and symmetric tools: mirror and merge the PROFILE sheet, then thicken once.
        const sheetMirror = thickened && mirror != undefined && symmetricTools;

        // The part so far, as a HISTORY query: a split's pieces are attributed to the feature that made the
        // original body, and an evaluated query of a split body resolves to nothing afterwards (2026-09-28).
        const solids = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SOLID);
        if (definition.debugPrint)
        {
            debugStep(context, "start: " ~ (thickened ? "thickened" : "constrained") ~ ", mirror " ~ (mirror != undefined)
                ~ ", caps " ~ size(caps) ~ ", periphery " ~ hasPeriphery);
            if (thickened && mirror != undefined)
            {
                debugProfileEdges(context, definition.profile, mirror);
            }
        }
        if (thickened)
        {
            // Offset 0 is the profile itself: no copy (about 0.7 s on a 76-face profile). It is only measured against and
            // thickened with the tools kept, so the input is never changed.
            const profile = abs(definition.profileOffset) < TOLERANCE.zeroLength * meter ? definition.profile
                : offsetSurface(context, id + "profileOffset", definition.profile, definition.profileOffset, definition.referencePoint);
            boundaries = concatenateArrays([boundaries, [
                            { "key" : "profileFaces", "surface" : profile, "distance" : 0 * meter },
                            { "key" : "oppositeFaces", "surface" : profile, "distance" : abs(definition.thickness) }]]);
            if (sheetMirror)
            {
                // The profile and its mirror image are joined into ONE sheet first (sheets merge where solids that meet in a
                // face do not: top sheet, 2026-11-12), then that sheet is thickened as a layer built from the ORIGINAL profile.
                const full = mirroredProfile(context, id + "mirrorProfile", definition.profile, mirror, definition.referencePoint, definition.debugPrint);
                thickenLayer(context, id, full, definition.referencePoint,
                    min(definition.profileOffset, definition.profileOffset + definition.thickness),
                    max(definition.profileOffset, definition.profileOffset + definition.thickness));
                const thickenedSolids = size(evaluateQuery(context, solids));
                if (definition.debugPrint)
                {
                    debugStep(context, "sheet mirror: thickened, solids " ~ thickenedSolids);
                }
                if (thickenedSolids != 1)
                {
                    throw regenError("The profile and its mirror image did not join into one surface at the mirror plane (" ~ thickenedSolids
                        ~ " parts after thickening). A gap wider than about 1 micron between the profile's edge and the plane stops them joining.", ["mirrorPlane"]);
                }
            }
            else if (mirror != undefined && abs(definition.profileOffset) > TOLERANCE.zeroLength * meter)
            {
                // The profile offset is not applied to the surface here; see thickenLayer.
                thickenLayer(context, id, definition.profile, definition.referencePoint,
                    min(definition.profileOffset, definition.profileOffset + definition.thickness),
                    max(definition.profileOffset, definition.profileOffset + definition.thickness));
            }
            else
            {
                thickenSides(context, id + "thicken", profile, definition.referencePoint,
                    definition.thickness > 0 * meter ? definition.thickness : 0 * meter,
                    definition.thickness < 0 * meter ? -definition.thickness : 0 * meter);
            }
            if (side != undefined)
            {
                keepInside(context, id + "peripherySplit", solids, side, qNothing(), definition.referencePoint);
            }
        }
        else
        {
            boundaries = concatenateArrays([boundaries, [
                            { "key" : "topFaces", "surface" : top, "distance" : 0 * meter },
                            { "key" : "bottomFaces", "surface" : bottom, "distance" : 0 * meter }]]);
            enclosedPart(context, id, qUnion(concatenateArrays([[top, bottom, side], caps])), point, mirror);
        }

        // The caps always cut: keep the reference point's side of each.
        if (size(caps) > 0)
        {
            if (definition.debugPrint)
            {
                debugStep(context, "before cap split");
                debugBodiesAgainstPlane(context, "thickened part", solids, mirror);
            }
            keepInside(context, id + "capSplit", solids, caps[0], size(caps) > 1 ? caps[1] : qNothing(), definition.referencePoint);
        }

        var result = qUnion(evaluateQuery(context, solids));
        if (mirror != undefined && !sheetMirror)
        {
            result = mirrorAndUnite(context, id, solids, mirror, definition.debugPrint);
        }

        // Faces by the boundary they lie on, read while the boundary surfaces still exist.
        const named = sortFaces(context, result, boundaries, mirror);

        // Every temporary surface and plane goes; only the part stays.
        const temporary = qSubtraction(qCreatedBy(id, EntityType.BODY), result);
        if (!isQueryEmpty(context, temporary))
        {
            opDeleteBodies(context, id + "deleteTemporary", { "entities" : temporary });
        }

        if (definition.outputName != "")
        {
            setProperty(context, { "entities" : result, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
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
        "mirrorPlane" : qNothing(),
        "outputName" : "",
        "debugPrint" : false
    });

/** Thicken+ of a surface into a new solid: `toward` on the reference point's side, `away` on the other. */
function thickenSides(context is Context, id is Id, surface is Query, reference is Query, toward is ValueWithUnits, away is ValueWithUnits)
{
    thickenPlus(context, id, {
                "operationType" : NewBodyOperationType.NEW,
                "entities" : surface,
                "sideReference" : reference,
                "thicknessToward" : toward,
                "thicknessAway" : away,
                "swapSides" : false,
                "keepTools" : true,
                "checkCurvature" : true,
                "debugPrint" : false
            });
}

/**
 * Thickened with a mirror plane and a profile offset: the layer from `lo` to `hi` (signed distances from the
 * profile, positive toward the reference point), built from the ORIGINAL profile instead of from an offset copy.
 *
 * The union with the mirror image needs the half's faces to meet the mirror plane exactly. An offset surface's
 * edges at the plane stop a fraction of a micron short of it (0.3 um on a 76-face profile), and the union then
 * fails with BOOLEAN_NON_MANIFOLD_RESULT -- for any positive offset, however small (2026-10-08). The original
 * profile's edges lie in the plane. A layer that spans the profile (lo <= 0 <= hi) is one thicken; a layer wholly
 * on one side is the thicken out to its far face less the thicken out to its near face.
 */
function thickenLayer(context is Context, id is Id, profile is Query, reference is Query, lo is ValueWithUnits, hi is ValueWithUnits)
{
    thickenSides(context, id + "thicken", profile, reference, max(hi, 0 * meter), max(-lo, 0 * meter));
    if (lo > 0 * meter || hi < 0 * meter)
    {
        thickenSides(context, id + "thickenNear", profile, reference, max(lo, 0 * meter), max(-hi, 0 * meter));
        opBoolean(context, id + "layer", {
                    "targets" : qCreatedBy(id + "thicken", EntityType.BODY),
                    "tools" : qCreatedBy(id + "thickenNear", EntityType.BODY),
                    "operationType" : BooleanOperationType.SUBTRACTION,
                    "keepTools" : false
                });
    }
}

/**
 * The profile and its mirror image as ONE sheet. A copy of the profile (Offset+ distance 0, so the input is never changed),
 * its mirror image, and the union of the two: the sheets sew where they meet at the mirror plane (gaps up to about 1 um,
 * correction 73), which is what the manual Mirror (Add) does. Uniting the THICKENED halves instead fails with
 * BOOLEAN_INVALID on the top sheet (the kernel will not merge the plane faces, whose outlines carry micron-scale detail).
 * Returns the joined sheet; thickenLayer then makes the part from it.
 */
function mirroredProfile(context is Context, id is Id, profile is Query, mirror is Plane, reference is Query, debugPrint is boolean) returns Query
{
    const half = offsetSurface(context, id + "copy", profile, 0 * meter, reference);
    opPattern(context, id + "mirror", {
                "entities" : half,
                "transforms" : [mirrorAcross(mirror)],
                "instanceNames" : ["mirror"]
            });
    const image = qCreatedBy(id + "mirror", EntityType.BODY);
    if (debugPrint)
    {
        debugStep(context, "sheet mirror: profile copy and mirror image made; joining");
    }
    opBoolean(context, id + "join", { "tools" : qUnion([half, image]), "operationType" : BooleanOperationType.UNION });
    return qUnion(evaluateQuery(context, half));
}

/** True when a surface reaches more than MIRROR_CUT_REACH to both sides of the plane (it spans the full width). */
function spansPlane(context is Context, tool is Query, mirror is Plane) returns boolean
{
    var lo = 1 * meter;
    var hi = -1 * meter;
    for (var body in evaluateQuery(context, tool))
    {
        const bx = evBox3d(context, { "topology" : body });
        for (var cx in [bx.minCorner[0], bx.maxCorner[0]])
        {
            for (var cy in [bx.minCorner[1], bx.maxCorner[1]])
            {
                for (var cz in [bx.minCorner[2], bx.maxCorner[2]])
                {
                    const d = dot(vector(cx, cy, cz) - mirror.origin, mirror.normal);
                    lo = min(lo, d);
                    hi = max(hi, d);
                }
            }
        }
    }
    return lo < -MIRROR_CUT_REACH && hi > MIRROR_CUT_REACH;
}

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
function mirrorAndUnite(context is Context, id is Id, solids is Query, mirror is Plane, debugPrint is boolean) returns Query
{
    if (debugPrint)
    {
        debugStep(context, "mirror: before the cut at the plane");
        debugBodiesAgainstPlane(context, "half", solids, mirror);
    }
    const s = dot(evApproximateCentroid(context, { "entities" : solids }) - mirror.origin, mirror.normal);
    if (abs(s) < ON_BOUNDARY_TOL)
    {
        throw regenError("The part is centred on the mirror plane: give one half of the surfaces, or remove the mirror plane.", ["mirrorPlane"]);
    }
    // The cut is only for a half that really reaches across the plane. A half that already lies on one side (its box, which
    // the kernel pads by about 0.1 um, reaches less than MIRROR_CUT_REACH past the plane) is mirrored as it is: cutting it
    // leaves slivers and plane faces with micron-scale outlines that the union then refuses (BOOLEAN_INVALID, top sheet,
    // 2026-11-12: 13 profile edges cross the plane by a fraction of a micron).
    const sideSign = s > 0 * meter ? 1 : -1;
    var reach = -1 * meter;
    for (var body in evaluateQuery(context, solids))
    {
        const bx = evBox3d(context, { "topology" : body });
        for (var cx in [bx.minCorner[0], bx.maxCorner[0]])
        {
            for (var cy in [bx.minCorner[1], bx.maxCorner[1]])
            {
                for (var cz in [bx.minCorner[2], bx.maxCorner[2]])
                {
                    reach = max(reach, -sideSign * dot(vector(cx, cy, cz) - mirror.origin, mirror.normal));
                }
            }
        }
    }
    const cutNeeded = reach > MIRROR_CUT_REACH;
    if (debugPrint)
    {
        debugStep(context, "mirror: the half reaches " ~ roundTo(reach / meter * 1e6, 3) ~ " um past the plane; " ~ (cutNeeded ? "cutting it" : "no cut needed"));
    }
    if (cutNeeded)
    {
        opPlane(context, id + "mirrorCut", { "plane" : mirror });
        opSplitPart(context, id + "mirrorSplit", {
                    "targets" : solids,
                    "tool" : qCreatedBy(id + "mirrorCut", EntityType.FACE),
                    "keepTools" : true,
                    "keepType" : SplitOperationKeepType.KEEP_ALL
                });
    }
    if (debugPrint)
    {
        debugStep(context, "mirror: after the cut, pieces " ~ size(evaluateQuery(context, solids)));
        debugBodiesAgainstPlane(context, "pieces", solids, mirror);
    }
    // The cut can leave slivers thinner than a micron in the plane (a thicken's end wall leans off it by about 0.06 um,
    // and profile edges that cross the plane by a fraction of a micron make wedges beyond it). A sliver's centroid is
    // meaningless, so the side test alone keeps one of them, and the union then fails with BOOLEAN_INVALID (measured
    // 2026-11-12, top sheet: 3 pieces, 2 slivers of about 1e-14 m^3 against 4e-5 m^3). Pieces are told apart by VOLUME first.
    const pieces = evaluateQuery(context, solids);
    var volumes = [];
    var biggest = 0 * meter * meter * meter;
    for (var piece in pieces)
    {
        const volume = try silent(evVolume(context, { "entities" : piece, "accuracy" : VolumeAccuracy.HIGH }));
        volumes = append(volumes, volume);
        if (volume != undefined && volume > biggest)
        {
            biggest = volume;
        }
    }
    var kept = [];
    var dropped = [];
    for (var i = 0; i < size(pieces); i += 1)
    {
        const piece = pieces[i];
        if (debugPrint)
        {
            debugStep(context, "piece " ~ i ~ " volume " ~ (volumes[i] == undefined ? "unreadable" : roundTo(volumes[i] / (meter * meter * meter) * 1e9, 6) ~ " mm^3")
                ~ (volumes[i] != undefined && volumes[i] < SLIVER_VOLUME_RATIO * biggest ? " (sliver: dropped)" : ""));
        }
        if (volumes[i] == undefined || volumes[i] < SLIVER_VOLUME_RATIO * biggest)
        {
            dropped = append(dropped, piece);
            continue;
        }
        const centroid = try silent(evApproximateCentroid(context, { "entities" : piece }));
        if (centroid == undefined)
        {
            dropped = append(dropped, piece);
            continue;
        }
        const ps = dot(centroid - mirror.origin, mirror.normal);
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
    if (debugPrint)
    {
        debugStep(context, "mirror: kept " ~ size(kept) ~ ", dropped " ~ size(dropped) ~ "; before the union");
        debugBodiesAgainstPlane(context, "union inputs (half and mirror)", both, mirror);
    }
    // Two bodies that meet in a face: the kernel can refuse the plain union of both as tools (BOOLEAN_INVALID, seen on the
    // top sheet, 2026-11-12, with the slivers already gone). A boolean with the half as target and the mirror image as
    // tool takes another route, so it is the second try.
    var united = false;
    try silent
    {
        opBoolean(context, id + "unite", { "tools" : both, "operationType" : BooleanOperationType.UNION });
        united = true;
    }
    if (!united)
    {
        if (debugPrint)
        {
            debugStep(context, "union with both as tools failed; trying the half as target and the mirror as tool");
        }
        try silent
        {
            opBoolean(context, id + "uniteTargeted", {
                        "targets" : qUnion(kept),
                        "tools" : qCreatedBy(id + "mirror", EntityType.BODY),
                        "operationType" : BooleanOperationType.UNION
                    });
            united = true;
        }
    }
    if (!united)
    {
        throw regenError("The half and its mirror image could not be united at the mirror plane. Switch on Print diagnostics to see the pieces.", ["mirrorPlane"]);
    }
    if (debugPrint)
    {
        debugStep(context, "mirror: united");
    }
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

/** Debug (Print diagnostics): one step marker. The last marker before an error names the step that failed. */
function debugStep(context is Context, label is string)
{
    // fscheck: allow-println every caller is gated by Print diagnostics (definition.debugPrint)
    println("[referencedPart] " ~ label);
}

/** Signed distance of a point from a plane, in micrometres (plain number). */
function microns(point is Vector, mirror is Plane) returns number
{
    return dot(point - mirror.origin, mirror.normal) / meter * 1e6;
}

/**
 * Debug (Print diagnostics): how the profile's edges sit against the mirror plane. Edges within 1 mm of the plane are
 * sampled at five parameters; prints how many are near, the worst deviation (micrometres) and how many cross the plane.
 */
function debugProfileEdges(context is Context, profile is Query, mirror is Plane)
{
    const edges = evaluateQuery(context, qUnion([qOwnedByBody(qEntityFilter(profile, EntityType.BODY), EntityType.EDGE),
                    qAdjacent(qEntityFilter(profile, EntityType.FACE), AdjacencyType.EDGE, EntityType.EDGE)]));
    var near = 0;
    var crossing = 0;
    var worst = 0;
    var lines = [];
    for (var edge in edges)
    {
        var samples = [];
        for (var t in [0, 0.25, 0.5, 0.75, 1])
        {
            samples = append(samples, microns(evEdgeTangentLine(context, { "edge" : edge, "parameter" : t }).origin, mirror));
        }
        const lo = min(samples);
        const hi = max(samples);
        if (min(abs(lo), abs(hi)) < 1000 || lo * hi < 0)
        {
            near += 1;
            const dev = max(abs(lo), abs(hi));
            if (lo * hi < 0)
            {
                crossing += 1;
            }
            if (dev > worst)
            {
                worst = dev;
            }
            if (size(lines) < 8)
            {
                lines = append(lines, "  edge " ~ near ~ ": " ~ roundTo(lo, 3) ~ " .. " ~ roundTo(hi, 3) ~ " um");
            }
        }
    }
    // fscheck: allow-println every caller is gated by Print diagnostics (definition.debugPrint)
    println("[referencedPart] profile edges " ~ size(edges) ~ ", near the mirror plane (<1 mm) " ~ near ~ ", crossing it " ~ crossing
        ~ ", worst distance " ~ roundTo(worst, 3) ~ " um");
    for (var line in lines)
    {
        // fscheck: allow-println gated by the callers (Print diagnostics)
        println(line);
    }
}

/**
 * Debug (Print diagnostics): each body's extent across the mirror plane (box corners, micrometres) and its planar faces
 * that lie within 1 mm of the plane: their offset from it and tilt against it.
 */
function debugBodiesAgainstPlane(context is Context, label is string, bodies is Query, mirror is Plane)
{
    for (var body in evaluateQuery(context, bodies))
    {
        const bx = evBox3d(context, { "topology" : body });
        var lo = undefined;
        var hi = undefined;
        for (var cx in [bx.minCorner[0], bx.maxCorner[0]])
        {
            for (var cy in [bx.minCorner[1], bx.maxCorner[1]])
            {
                for (var cz in [bx.minCorner[2], bx.maxCorner[2]])
                {
                    const d = microns(vector(cx, cy, cz), mirror);
                    lo = lo == undefined ? d : min(lo, d);
                    hi = hi == undefined ? d : max(hi, d);
                }
            }
        }
        // fscheck: allow-println every caller is gated by Print diagnostics (definition.debugPrint)
        println("[referencedPart] " ~ label ~ ": box spans " ~ roundTo(lo, 1) ~ " .. " ~ roundTo(hi, 1) ~ " um across the plane");
        var shown = 0;
        for (var face in evaluateQuery(context, qOwnedByBody(body, EntityType.FACE)))
        {
            const pl = try silent(evPlane(context, { "face" : face }));
            if (pl == undefined)
            {
                continue;
            }
            const tilt = acos(min(1, abs(dot(pl.normal, mirror.normal)))) / radian * 1e6;
            const off = microns(pl.origin, mirror);
            if (tilt < 1000 && abs(off) < 1000 && shown < 6)
            {
                shown += 1;
                // fscheck: allow-println gated by the callers (Print diagnostics)
                const area = evArea(context, { "entities" : face }) / (meter * meter) * 1e6;
                const edgeCount = size(evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)));
                // fscheck: allow-println gated by the callers (Print diagnostics)
                println("  planar face near the plane: offset " ~ roundTo(off, 3) ~ " um, tilt " ~ roundTo(tilt, 3) ~ " urad, area "
                    ~ roundTo(area, 3) ~ " mm^2, edges " ~ edgeCount);
            }
        }
    }
}

/** Rounds to a number of decimals, for printing. */
function roundTo(value is number, decimals is number) returns number
{
    const scale = 10 ^ decimals;
    return round(value * scale) / scale;
}

/** A point reflected across a plane. */
function mirrorPoint(point is Vector, mirror is Plane) returns Vector
{
    return point - 2 * dot(point - mirror.origin, mirror.normal) * mirror.normal;
}
