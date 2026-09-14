FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

export import(path : "a2665e22c07b7a6929ce4e80", version : "d49252259f7b34bf7da639ec");
import(path : "d009ddf4a8dd9534fc4dc4b5", version : "e11a408e487b65a9b42efac8");
import(path : "6479d7fbd0ec7d11e0ae6c69", version : "4f533950f9fcbe2083572c8b");
import(path : "786f62f4d67ed8d9c7d56d16", version : "");

/**
 * This feature extends driven_offset_Profile to create a variety of surfaces from driven offsets. We have three basic modes.
 * For each mode, we can optionally keep offset wires or not
 *
 * definition.surfaceMode - {RULED_OFFSET_PROFILE, CONNECTED_OFFSET, MULTIPROFILE_LOFT}
 *
 * RULED_OFFSET_PROFILE: Options for WIDTH and HEIGHT. If WIDTH is selected, output surface is the width of the offset. If a reference profile is provided, the output width surface shoudl respect that profile (surface may not be linear in cross section)
 *  If HEIGHT is selected, surface should be along the HEIGHt direction of our frames.
 *  Provide second direction boolean, which 'extends' the surface in the opposite direction
 *
 * CONNECTED_OFFSET: Connect the sourceEdges to the offset edges with a loft. Note that not all source edges may be used, and the 'loft' portion of the source edges is just where there is a relevant offset. This may require a change in driven_edge_offset
 *  so it's easier for us to understand if a vertex should be lofted to an arc (G0), visa versa, or if we loose edges due to an offset.
 *
 * MULTIPROFILE_LOFT:
 *  Provide multiple offset profiles, loft between them. Understand vertex connections based on seedEdges verticies - or whatever makes sense
 *
 */

// ============================================================================
// Status
// ============================================================================
//
// Every mode drives drivenOffset once per profile and lofts the results. The seed chain,
// the reference and the frames are still recomputed per profile: sharing them is the next
// piece of work, and it is an optimisation rather than a correctness matter, because the
// station lists only diverge where a profile's own breaks fall.
//
// CONNECTED_OFFSET lofts the whole seed chain to the offset. Trimming the seed to the
// covered span -- and fanning a G0 vertex to the arc that replaced it -- needs the corner
// records that drivenOffset now returns, and is not wired up yet.

// ============================================================================
// Bounds
// ============================================================================

/**
 * How far a ruled surface reaches from the offset curve.
 *
 * Signed: negative rules the other way, which is not the same as "both directions" and is
 * worth having separately.
 */
export const RuledDistanceBounds =
{
    (meter)      : [-10, 0.01, 10],
    (centimeter) : 1,
    (millimeter) : 10,
    (inch)       : 0.5
} as LengthBoundSpec;

/** Sections used across a ruling that has to follow a reference surface. */
export const RuledSectionBounds = { (unitless) : [2, 5, 25] } as IntegerBoundSpec;

// ============================================================================
// Enums
// ============================================================================

/**
 * What the feature builds from the offsets it drives.
 */
export enum SurfaceMode
{
    annotation { "Name" : "Ruled from offset" }
    RULED_OFFSET_PROFILE,
    annotation { "Name" : "Connect source to offset" }
    CONNECTED_OFFSET,
    annotation { "Name" : "Loft between profiles" }
    MULTIPROFILE_LOFT
}

/**
 * Which way a ruled surface leaves the offset curve.
 *
 * The frame's own axes, so both follow the offset rather than a world direction: width lies
 * in the reference surface where there is one, height runs along its normal.
 */
export enum RuledDirection
{
    annotation { "Name" : "Width" }
    WIDTH,
    annotation { "Name" : "Height" }
    HEIGHT
}

// ============================================================================
// Feature
// ============================================================================

annotation { "Feature Type Name" : "Driven offset surface",
        "Feature Type Description" : "Build surfaces from one or more driven offsets of the same edges." }
export const drivenOffsetSurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Surface", "Default" : SurfaceMode.RULED_OFFSET_PROFILE, "UIHint" : UIHint.SHOW_LABEL }
        definition.surfaceMode is SurfaceMode;

        annotation { "Name" : "Name", "Description" : "Names the surfaces. Each offset's own name, where given, names its wire." }
        definition.outputName is string;

        offsetMeasurePredicate(definition);
        offsetReferencePredicate(definition);
        offsetEdgesPredicate(definition);

        // One array whatever the mode: the seed edges and the reference are shared, and a
        // profile is the only thing that differs between offsets. Loft-between-profiles
        // reads the whole array as one surface; the other two build one surface each.
        annotation { "Name" : "Offsets", "Item name" : "offset", "Item label template" : "#offsetName" }
        definition.offsets is array;
        for (var entry in definition.offsets)
        {
            annotation { "Name" : "Offset profile", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "X maps to position along the offset edges, Y to width offset, Z to height offset." }
            entry.offsetProfile is Query;

            annotation { "Name" : "Name", "Description" : "Names this offset's wire, when wires are kept." }
            entry.offsetName is string;
        }

        if (definition.surfaceMode == SurfaceMode.RULED_OFFSET_PROFILE)
        {
            annotation { "Name" : "Rule along", "Default" : RuledDirection.WIDTH, "UIHint" : UIHint.SHOW_LABEL }
            definition.ruledDirection is RuledDirection;

            annotation { "Name" : "Distance", "Description" : "How far the surface reaches from the offset curve. Negative rules the other way." }
            isLength(definition.ruledDistance, RuledDistanceBounds);

            annotation { "Name" : "Both directions", "Default" : false, "Description" : "Also rule the opposite way, so the surface straddles the offset curve." }
            definition.ruledBothDirections is boolean;

            annotation { "Name" : "Sections across the ruling", "Description" : "Only meaningful with a reference wire, where the ruling follows the reference surface and is not straight. Two sections chord across it." }
            isInteger(definition.ruledSections, RuledSectionBounds);
        }

        annotation { "Name" : "Keep offset wires", "Default" : false, "Description" : "Leave the driven offset curves in the result alongside the surfaces." }
        definition.keepWires is boolean;

        offsetAlignmentPredicate(definition);
        offsetCornersPredicate(definition);
        offsetEndsPredicate(definition);
        offsetZeroPredicate(definition);
        drivenOffsetSpacingPredicate(definition);
        offsetDebugPredicate(definition);
    }
    {
        if (size(definition.offsets) == 0)
        {
            throw regenError("Add at least one offset profile.", ["offsets"]);
        }

        if (definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT && size(definition.offsets) < 2)
        {
            throw regenError("Lofting between profiles needs at least two offsets.", ["offsets"]);
        }

        const driven = driveOffsets(context, id, definition);

        if (definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT)
        {
            loftAcrossOffsets(context, id, definition, driven);
        }
        else if (definition.surfaceMode == SurfaceMode.RULED_OFFSET_PROFILE)
        {
            for (var i = 0; i < size(driven); i += 1)
            {
                ruleFromOffset(context, id + ("ruled" ~ i), definition, driven[i]);
            }
        }
        else
        {
            for (var i = 0; i < size(driven); i += 1)
            {
                connectSourceToOffset(context, id + ("connected" ~ i), definition, driven[i]);
            }
        }

        finishSurface(context, id, definition, driven);
    });

// ============================================================================
// Driving the offsets
// ============================================================================

/**
 * Run one driven offset per profile and keep what each produced.
 *
 * Every offset is handed the feature's own definition with only the profile and the name
 * swapped in, so the seed edges, the reference, the frame alignment, the corner and terminal
 * treatment and the spacing are shared by construction rather than by convention. That is
 * the point of the array: a profile is the only thing that differs.
 *
 * The chain, the stations and the frames are still rebuilt per profile. Hoisting them is
 * worth doing and is not a correctness matter -- what it buys, beyond the kernel calls, is
 * that station indices would then line up across offsets, which is what a loft between them
 * ultimately wants.
 */
function driveOffsets(context is Context, id is Id, definition is map) returns array
{
    var driven = [];

    for (var i = 0; i < size(definition.offsets); i += 1)
    {
        const entry = definition.offsets[i];

        if (isQueryEmpty(context, entry.offsetProfile))
        {
            continue;
        }

        const offsetId = id + ("offset" ~ i);
        const perOffset = mergeMaps(definition, {
                    "offsetProfile" : entry.offsetProfile,
                    "outputName" : entry.offsetName
                });

        const result = drivenOffset(context, offsetId, perOffset);

        driven = append(driven, mergeMaps(result, {
                        "id" : offsetId,
                        "name" : entry.offsetName,
                        "wires" : qCreatedBy(offsetId, EntityType.BODY),
                        "edges" : qCreatedBy(offsetId, EntityType.EDGE)
                    }));
    }

    if (size(driven) == 0)
    {
        throw regenError("None of the offset profiles produced a curve.", ["offsets"]);
    }

    return driven;
}

// ============================================================================
// Ruled from the offset
// ============================================================================

/**
 * Sweep the offset curve sideways and loft the result.
 *
 * The second curve is the offset displaced by a fixed distance along the frame's own width
 * or height axis -- not a world direction, so it follows the offset round every turn.
 *
 * With a reference wire the ruling is NOT straight: width lies in the reference surface, so
 * a step of w + d lands wherever that surface has got to, which is why the cross-section can
 * bow. Intermediate sections are what stop the loft chording across that bow; without a
 * reference they would be collinear and are skipped.
 */
function ruleFromOffset(context is Context, id is Id, definition is map, driven is map)
{
    const reach = definition.ruledDistance;
    const steps = usesReferenceFrame(definition, driven.alongRef) ? definition.ruledSections : 2;

    var sections = [ruledSection(context, id + "base", definition, driven, 0 * meter)];

    for (var s = 1; s < steps; s += 1)
    {
        sections = append(sections,
            ruledSection(context, id + ("out" ~ s), definition, driven, reach * s / (steps - 1)));
    }

    if (definition.ruledBothDirections)
    {
        // Prepended rather than appended: a loft reads its sections in order, and the far
        // side of the offset belongs at the far end of that order, not after it.
        var back = [];
        for (var s = steps - 1; s >= 1; s -= 1)
        {
            back = append(back,
                ruledSection(context, id + ("back" ~ s), definition, driven, -reach * s / (steps - 1)));
        }
        sections = concatenateArrays([back, sections]);
    }

    loftSections(context, id + "loft", sections);
}

/**
 * One section of a ruled surface: the offset curve stepped sideways by `reach`.
 *
 * A step of zero is the offset itself, rebuilt here rather than reused. Reusing the emitted
 * wire would be cheaper but it is split into runs at every profile break and every corner,
 * and a loft wants one curve per section.
 */
function ruledSection(context is Context, id is Id, definition is map, driven is map,
    reach is ValueWithUnits) returns Query
{
    var points = [];

    for (var i = 0; i < size(driven.stations); i += 1)
    {
        const at = displacedPoint(definition, driven, i, reach);

        if (at != undefined)
        {
            points = append(points, at);
        }
    }

    if (size(points) < 2)
    {
        throw regenError("The offset does not cover enough of the edges to rule a surface from.");
    }

    return curveThrough(context, id, definition, points);
}

/**
 * Where one station's offset point goes when stepped sideways.
 *
 * Undefined where the profile never reached -- those stations have no offset to step from,
 * and they are exactly the source edges the header says may go unused.
 */
function displacedPoint(definition is map, driven is map, index is number, reach is ValueWithUnits)
{
    const at = driven.points[index];
    const amounts = driven.upper[index];

    if (at == undefined || amounts == undefined)
    {
        return undefined;
    }

    if (reach == 0 * meter)
    {
        return at;
    }

    const frame = driven.stations[index];
    const alongWidth = definition.ruledDirection == RuledDirection.WIDTH;

    // In the reference surface the step is a geodesic, not a straight line, so it has to go
    // through the same placement the offset itself used rather than being added to the
    // finished point.
    if (usesReferenceFrame(definition, driven.alongRef))
    {
        return surfaceOffset(driven.alongRef, frame, {
                        "width" : amounts.width + (alongWidth ? reach : 0 * meter),
                        "height" : amounts.height + (alongWidth ? 0 * meter : reach)
                    }).point;
    }

    return at + reach * (alongWidth ? frame.widthAxis : frame.heightAxis);
}

// ============================================================================
// Source to offset
// ============================================================================

/**
 * Loft the edges being offset to the offset itself.
 *
 * Distinct from ruling even though both span the same gap: ruling steps a fixed distance
 * from the offset, while this lands on the seed exactly. They agree only when the offset
 * width is constant and the ruled distance matches it.
 */
function connectSourceToOffset(context is Context, id is Id, definition is map, driven is map)
{
    var points = [];

    for (var i = 0; i < size(driven.stations); i += 1)
    {
        if (driven.points[i] != undefined)
        {
            points = append(points, driven.stations[i].origin);
        }
    }

    if (size(points) < 2)
    {
        throw regenError("The offset does not cover enough of the edges to connect to.");
    }

    // The seed is rebuilt from the stations that actually carry an offset rather than
    // selected wholesale, which is what keeps an uncovered edge out of the loft.
    const seed = curveThrough(context, id + "seed", definition, points);
    const offsetSide = ruledSection(context, id + "offset", definition, driven, 0 * meter);

    loftSections(context, id + "loft", [seed, offsetSide]);
}

// ============================================================================
// Between profiles
// ============================================================================

/**
 * Loft across every offset in array order.
 *
 * Array order, not sorted by size: a deliberately non-monotonic sequence of profiles is a
 * legitimate thing to want, and sorting would quietly rewrite it.
 */
function loftAcrossOffsets(context is Context, id is Id, definition is map, driven is array)
{
    var sections = [];

    for (var i = 0; i < size(driven); i += 1)
    {
        sections = append(sections,
            ruledSection(context, id + ("section" ~ i), definition, driven[i], 0 * meter));
    }

    loftSections(context, id + "loft", sections);
}

// ============================================================================
// Shared
// ============================================================================

/**
 * One curve through a point list, as a body a loft can take.
 */
function curveThrough(context is Context, id is Id, definition is map, points is array) returns Query
{
    var chord = 0 * meter;
    for (var i = 1; i < size(points); i += 1)
    {
        chord += norm(points[i] - points[i - 1]);
    }

    const fitted = approximateSpline(context, {
                "degree" : definition.approximationDegree,
                "tolerance" : definition.approximationTolerance,
                "isPeriodic" : false,
                "maxControlPoints" : definition.approximationMaxCPs,
                "targets" : [approximationTarget({ "positions" : points })]
            })[0];

    opCreateBSplineCurve(context, id, { "bSplineCurve" : fitted });

    return qCreatedBy(id, EntityType.EDGE);
}

/**
 * Loft a list of section curves into one surface.
 */
function loftSections(context is Context, id is Id, sections is array)
{
    if (size(sections) < 2)
    {
        return;
    }

    opLoft(context, id, {
                "profileSubqueries" : sections,
                "bodyType" : ToolBodyType.SURFACE
            });
}

/**
 * Name the surfaces and clear away whatever the user did not ask to keep.
 *
 * The section curves are scaffolding either way: they are rebuilt from the stations, not the
 * emitted offset, so keeping them would leave a second copy of every curve beside the wires
 * the offsets already produced.
 */
function finishSurface(context is Context, id is Id, definition is map, driven is array)
{
    var wires = [];
    for (var offset in driven)
    {
        wires = append(wires, offset.wires);
    }

    const surfaces = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SHEET);
    const scaffolding = qSubtraction(qCreatedBy(id, EntityType.BODY),
            qUnion([surfaces, qUnion(wires)]));

    if (!isQueryEmpty(context, scaffolding))
    {
        opDeleteBodies(context, id + "scaffolding", { "entities" : scaffolding });
    }

    if (!definition.keepWires && !isQueryEmpty(context, qUnion(wires)))
    {
        opDeleteBodies(context, id + "wires", { "entities" : qUnion(wires) });
    }

    if (definition.outputName != "" && !isQueryEmpty(context, surfaces))
    {
        setProperty(context, {
                    "entities" : surfaces,
                    "propertyType" : PropertyType.NAME,
                    "value" : definition.outputName
                });
    }
}
