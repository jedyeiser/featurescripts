FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// LoftTopology has its own module and common.fs does not reach it -- the same gap as
// ProjectionType and getQueryVariable elsewhere in this document.
import(path : "onshape/std/lofttopology.gen.fs", version : "3070.0");

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
// The seed chain, the reference, the stations and their frames are built once for every
// offset in the array (sharedOffsetContext), with crossings inserted for the union of all
// the profiles' breaks so that station i means the same arc length in all of them.
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

/** Run filter for the surface debug prints; -1 is every run. */
export const DebugRunBounds = { (unitless) : [-1, -1, 200] } as IntegerBoundSpec;

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

        offsetEdgesPredicate(definition);
        offsetZeroPredicate(definition);
        offsetMeasurePredicate(definition);
        offsetReferencePredicate(definition);

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

        if (definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT)
        {
            // There is no "loft the whole wire at once" option any more. Splitting by run
            // is never the wrong thing -- it is what gives each face a surface its own size
            // instead of a window onto one spanning the whole loft -- so it is not a choice,
            // it is just what the feature does. What IS a choice is whether the loft runs
            // straight from profile to profile or curves through them, and that is this.
            annotation { "Name" : "Blend through profiles", "Default" : false, "Description" : "Fit one smooth surface through all the profiles instead of running straight from each to the next. Off gives a ruled patch per adjacent pair, with a crease at every intermediate profile. A loft of three or more profiles can only be smooth, so this is the difference between N-1 straight surfaces and one curved one. No effect with only two offsets." }
            definition.blendThroughProfiles is boolean;
        }

        annotation { "Name" : "Keep offset wires", "Default" : false, "Description" : "Leave the driven offset curves in the result alongside the surfaces." }
        definition.keepWires is boolean;

        offsetAlignmentPredicate(definition);
        offsetCornersPredicate(definition);
        offsetEndsPredicate(definition);
        drivenOffsetSpacingPredicate(definition);
        offsetDebugPredicate(definition);

        annotation { "Group Name" : "Surface debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print surface", "Default" : false, "Description" : "Report what each offset produced and what was handed to the loft: runs, the span chosen, and the point count of every section. A patch the loft refuses is dumped in full: both curves, control point by control point." }
            definition.debugPrintSurface is boolean;

            if (definition.debugPrintSurface)
            {
                annotation { "Name" : "Only run", "Description" : "Limit the section, fit and patch lines to this run. -1 prints every run." }
                isInteger(definition.debugRun, DebugRunBounds);
            }

            annotation { "Name" : "Keep section curves", "Default" : false, "Description" : "Leave the section curves in the result instead of deleting them. The fastest way to see whether the loft was given what you expected." }
            definition.debugKeepSections is boolean;
        }
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

        if (definition.debugPrintSurface)
        {
            printSurfacePlan(context, definition, driven);
        }

        // Each mode hands back the section wires it lofted from. They are the only
        // scaffolding worth ever looking at, and naming them here is what lets the cleanup
        // keep them without also keeping the loose curves they were assembled from.
        var sections = [];

        if (definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT)
        {
            sections = loftAcrossOffsets(context, id, definition, driven);
        }
        else if (definition.surfaceMode == SurfaceMode.RULED_OFFSET_PROFILE)
        {
            for (var i = 0; i < size(driven); i += 1)
            {
                sections = concatenateArrays([sections,
                            ruleFromOffset(context, id + ("ruled" ~ i), definition, driven[i])]);
            }
        }
        else
        {
            for (var i = 0; i < size(driven); i += 1)
            {
                sections = concatenateArrays([sections,
                            connectSourceToOffset(context, id + ("connected" ~ i), definition, driven[i])]);
            }
        }

        if (definition.debugPrintSurface)
        {
            printSurfaceResult(context, id);
        }

        finishSurface(context, id, definition, driven, sections);
    }, {
        // An annotation's "Default" only serves a NEW instance. A feature saved before a
        // parameter existed needs it here, or its next regen fails the precondition -- which
        // is how a debug toggle added mid-session silently does nothing.
        "surfaceMode" : SurfaceMode.RULED_OFFSET_PROFILE,
        "outputName" : "",
        "offsets" : [],
        "ruledDirection" : RuledDirection.WIDTH,
        "ruledBothDirections" : false,
        "ruledSections" : 5,
        "blendThroughProfiles" : false,
        "joinTangentRuns" : false,
        "keepWires" : false,
        "debugPrintSurface" : false,
        "debugRun" : -1,
        "debugKeepSections" : false
    });

// ============================================================================
// Driving the offsets
// ============================================================================

/**
 * Run one driven offset per profile over one shared context, and keep what each produced.
 *
 * Every offset is handed the feature's own definition with only the profile and the name
 * swapped in, so the seed edges, the reference, the frame alignment, the corner and terminal
 * treatment and the spacing are shared by construction rather than by convention. That is
 * the point of the array: a profile is the only thing that differs.
 */
function driveOffsets(context is Context, id is Id, definition is map) returns array
{
    var queries = [];
    var entries = [];

    for (var entry in definition.offsets)
    {
        if (isQueryEmpty(context, entry.offsetProfile))
        {
            continue;
        }

        queries = append(queries, entry.offsetProfile);
        entries = append(entries, entry);
    }

    if (size(queries) == 0)
    {
        throw regenError("None of the offset profiles produced a curve.", ["offsets"]);
    }

    // The edges, the reference and every frame are the same for all of them, so they are
    // built once here rather than once per profile. What this buys beyond the kernel calls
    // is that station i is the same arc length in every offset -- which is what a loft
    // between two of them needs before it can pair anything up.
    //
    // Only a loft between profiles asks for matching run structure. The other two modes
    // rule from a single offset, and forcing a split there would put an edge in the result
    // where the offset is smooth for no gain.
    const shared = sharedOffsetContext(context, definition, queries,
        definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT);

    // Plan every profile before committing any of it. Joining tangent runs changes the run
    // structure, and a loft pairs section against section by run index -- so the decision has
    // to be taken once, over all the profiles, and applied to all of them. Deciding per
    // profile would desynchronise them exactly the way differing profile breaks did.
    var plans = [];
    for (var i = 0; i < size(entries); i += 1)
    {
        plans = append(plans, planOffset(context, mergeMaps(definition, {
                            "offsetProfile" : entries[i].offsetProfile
                        }), shared, i));
    }

    if (definition.joinTangentRuns)
    {
        // One walk over the shared run structure, testing every profile's points at each
        // step, so the merges come out identical for all of them by construction rather than
        // by intersecting decisions taken separately.
        var pointsPerProfile = [];
        for (var plan in plans)
        {
            pointsPerProfile = append(pointsPerProfile, plan.points);
        }

        const common = tangentRunMerges(pointsPerProfile, plans[0].runs, shared.stations,
            definition.approximationTolerance);

        var merged = [];
        for (var plan in plans)
        {
            merged = append(merged, withMergedRuns(plan, common));
        }
        plans = merged;

        if (definition.debugPrintSurface)
        {
            var dissolved = 0;
            for (var m in common)
            {
                if (m) { dissolved += 1; }
            }
            println("[surface] tangent joins: " ~ toString(dissolved) ~ " of "
                ~ toString(size(common)) ~ " run boundary(ies) dissolved in every profile");
        }
    }

    var driven = [];

    for (var i = 0; i < size(entries); i += 1)
    {
        const offsetId = id + ("offset" ~ i);

        // Four of the Debug toggles report things that are now genuinely shared -- the
        // reference, the frames, the frame table. Leaving them on for every offset printed
        // the same two hundred frames once per profile, which is what made the duplication
        // visible in the first place. Report them for the first offset only.
        const first = (i == 0);
        const perOffset = mergeMaps(definition, {
                    "offsetProfile" : entries[i].offsetProfile,
                    "outputName" : entries[i].offsetName,

                    // Forced, not inherited. joinOutput reaches this feature through
                    // drivenOffsetSpacingPredicate, where it sits in a collapsed group and
                    // reads as a sampling option -- nothing about it says it decides whether
                    // an offset comes out as one wire or as one body per run, fill and
                    // extension. An offset here is a named thing the user asked for, so it
                    // is one wire, and the toggle does not get a vote.
                    "joinOutput" : true,
                    "debugPrintAlongChain" : definition.debugPrintAlongChain && first,
                    "debugShowReference" : definition.debugShowReference && first,
                    "debugShowOffsetFrames" : definition.debugShowOffsetFrames && first,
                    "debugPrintFrameTable" : definition.debugPrintFrameTable && first
                });

        // Emitting the offset wire is the most expensive thing this feature does that is not
        // a loft: per run a curve, and for every arc run a whole sketch -- newSketchOnPlane,
        // skArc, skSolve, opExtractWires, opDeleteBodies, five kernel ops including a
        // constraint solve. And no loft consumes it. loftColumns builds from sections[i].pieces
        // and the other two modes from ruledSection's wire; all three rebuild their curves
        // from the stations. So unless the wire is being kept, or a debug toggle reads the
        // emitted runs, the whole thing is built and then deleted again by finishSurface.
        const needsEmit = definition.keepWires
            || definition.debugPrintFromChain
            || definition.debugPrintOffsetTable
            || definition.debugVisualizeContinuity
            || definition.debugShowChainEnds;

        const result = needsEmit
            ? emitOffset(context, offsetId, perOffset, shared, plans[i])
            : mergeMaps(plans[i], {
                        "sourceChain" : shared.sourceChain,
                        "alongRef" : shared.alongRef,
                        "stations" : shared.stations,
                        "coords" : shared.coords,
                        "emitted" : []
                    });

        // The offset's own reporting lives with the feature, not the core, so driving the
        // core directly leaves every toggle in the Debug group wired to nothing unless it
        // is called here. One debug id per offset, or they would collide.
        debugOutput(context, offsetId + "debug", perOffset, result.sourceChain, result.profile,
            result.alongRef, result.stations, result.coords, result.upper, result.lower,
            result.placed, result.emitted);

        driven = append(driven, mergeMaps(result, {
                        "id" : offsetId,
                        "name" : entries[i].offsetName,
                        "wires" : qCreatedBy(offsetId, EntityType.BODY)
                    }));
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
function ruleFromOffset(context is Context, id is Id, definition is map, driven is map) returns array
{
    const reach = definition.ruledDistance;
    const steps = usesReferenceFrame(definition, driven.alongRef) ? definition.ruledSections : 2;

    // One surface per stretch. A stepped offset genuinely is several surfaces, and merging
    // them would either bridge the step or drop everything past it.
    const spans = surfaceSpans(driven);

    var wires = [];

    for (var k = 0; k < size(spans); k += 1)
    {
        wires = concatenateArrays([wires,
                    ruleOverSpan(context, id + ("span" ~ k), definition, driven, spans[k], reach, steps)]);
    }

    return wires;
}

/**
 * The ruled surface over one uninterrupted stretch of the offset.
 */
function ruleOverSpan(context is Context, id is Id, definition is map, driven is map,
    span is map, reach is ValueWithUnits, steps is number) returns array
{
    var sections = [ruledSection(context, id + "base", definition, driven, span, 0 * meter).wire];

    for (var s = 1; s < steps; s += 1)
    {
        sections = append(sections,
            ruledSection(context, id + ("out" ~ s), definition, driven, span, reach * s / (steps - 1)).wire);
    }

    if (definition.ruledBothDirections)
    {
        // Prepended rather than appended: a loft reads its sections in order, and the far
        // side of the offset belongs at the far end of that order, not after it.
        var back = [];
        for (var s = steps - 1; s >= 1; s -= 1)
        {
            back = append(back,
                ruledSection(context, id + ("back" ~ s), definition, driven, span, -reach * s / (steps - 1)).wire);
        }
        sections = concatenateArrays([back, sections]);
    }

    loftSections(context, id + "loft", sections);

    return sections;
}

/**
 * One section of a ruled surface: the offset stepped sideways by `reach`.
 *
 * One curve per RUN, gathered into a single wire -- not one curve over the whole stretch.
 * Fitting the stretch as a single spline smooths straight through every G0 corner the source
 * had, and leaves the loft with one edge per side and nothing to pair up. Runs already break
 * exactly where the source edges do, so fitting them separately keeps those breaks and gives
 * both sections the same edge count, which is what lets the loft match them face for face.
 *
 * A step of zero is the offset itself, rebuilt here rather than reused. Reusing the emitted
 * wire would keep its exact lines and arcs, but the displaced section has to be built this
 * way regardless, and two sections built differently would not correspond.
 */
function ruledSection(context is Context, id is Id, definition is map, driven is map,
    span is map, reach is ValueWithUnits) returns map
{
    return emitSection(context, id, definition, sectionPlan(definition, driven, span, reach),
        reach, undefined);
}

/**
 * Everything a section settles before any geometry exists.
 *
 * Split out from the emit for the same reason planOffset was split out of emitOffset: a
 * caller driving several profiles has to be able to look at all of their runs before any of
 * them is committed. Fitting a run needs the point lists of EVERY profile at that run, so
 * the decision cannot be made one section at a time -- see coupledFits.
 *
 * @returns {array} : run-indexed, with a hole where a run fell outside the span, carried no
 *      offset, or collapsed to nothing. The holes are kept so two sections built over the
 *      same runs still pair run for run.
 */
function sectionPlan(definition is map, driven is map, span is map, reach is ValueWithUnits) returns array
{
    var plans = [];

    for (var r = 0; r < size(driven.runs); r += 1)
    {
        const run = driven.runs[r];
        const from = max(run.start, span.start);
        const to = min(run.end, span.end);

        var plan = undefined;

        if (to - from >= 1)
        {
            var points = [];
            for (var i = from; i <= to; i += 1)
            {
                const at = displacedPoint(definition, driven, i, reach);

                if (at != undefined)
                {
                    points = append(points, at);
                }
            }

            const distinct = withoutRepeats(points);

            // The same guard emitRuns applies, for the same reason: a run collapses to
            // nothing when a profile break lands on a source edge boundary, and a curve
            // through a sub-micron span is not geometry. Dropping it here keeps it out of
            // the loft; the neighbouring columns cover the position.
            // Named runSpan, not span: the argument of that name is the stretch this section
            // is clipped to, and this is a length.
            var runSpan = 0 * meter;
            for (var k = 1; k < size(distinct); k += 1)
            {
                runSpan += norm(distinct[k] - distinct[k - 1]);
            }

            if (size(distinct) >= 2 && runSpan >= OFFSET_GEOM_TOL)
            {
                // The end tangents, displaced by the same reach that placed the points, so
                // every section is fitted under the same constraints the offset itself was.
                // The tangent is not the slope: a constant reach has zero derivative, so the
                // displaced tangent is the offset tangent re-evaluated at the larger amount,
                // and runTangent takes it from whichever map placed the points.
                const displacement = sectionDisplacement(definition, reach);
                const allowArc = sourceAllowsArc(driven.stations, from, to);

                plan = {
                        "from" : from,
                        "to" : to,
                        "sampled" : size(points),
                        "points" : distinct,
                        "runSpan" : runSpan,
                        "allowArc" : allowArc,
                        "shape" : classifyPoints(distinct, definition.approximationTolerance, allowArc),
                        "startDerivative" : runTangent(driven.stations, driven.coords, driven.upper,
                                definition, driven.alongRef, run, from, displacement),
                        "endDerivative" : runTangent(driven.stations, driven.coords, driven.lower,
                                definition, driven.alongRef, run, to, displacement)
                    };
            }
        }

        plans = append(plans, plan);
    }

    return plans;
}

/**
 * One fit per profile for every run that can take one, computed a run at a time.
 *
 * This is the point of the plan/emit split. Fitting each section on its own lets
 * approximateSpline choose each curve knots independently, and opLoft refuses some pairs of
 * independently-parameterized curves while accepting others that measure identically --
 * same separation the whole way, tangents in agreement, no cusp or reversal in either, same
 * degree, same control-point count. Confirmed against the kernel: a loft built by hand
 * between two such curves fails the same way, and the same feature builds cleanly when the
 * source classifies as lines and arcs so no fitting happens at all. Handing a run to the
 * fitter once, with every profile as a target, makes the family share a parameterization by
 * construction instead of hoping the fitter lands on compatible knots twice.
 *
 * @param plans {array} : one sectionPlan per profile, all over the same runs.
 * @returns {array} : fits[profile][run], undefined where the run is not coupled and the
 *      section emits it on its own.
 */
function coupledFits(context is Context, definition is map, plans is array) returns array
{
    const runs = size(plans[0]);

    var fits = [];
    for (var i = 0; i < size(plans); i += 1)
    {
        var empty = [];
        for (var r = 0; r < runs; r += 1)
        {
            empty = append(empty, undefined);
        }

        fits = append(fits, empty);
    }

    for (var r = 0; r < runs; r += 1)
    {
        if (!coupleable(plans, r))
        {
            continue;
        }

        var members = [];
        for (var i = 0; i < size(plans); i += 1)
        {
            members = append(members, {
                        "points" : plans[i][r].points,
                        "startDerivative" : plans[i][r].startDerivative,
                        "endDerivative" : plans[i][r].endDerivative
                    });
        }

        const curves = approximateFamily(context, members, fitSettings(definition));

        for (var i = 0; i < size(plans); i += 1)
        {
            var row = fits[i];
            row[r] = curves[i];
            fits[i] = row;
        }
    }

    return fits;
}

/**
 * Whether one run can be fitted as a single family across every profile.
 *
 * Three things have to hold and none can be assumed. std requires every target to carry the
 * same number of positions and corresponding derivative information. withoutRepeats can drop
 * a different number of points from different profiles, because the profiles place their
 * points differently. And a run that classifies as a line or an arc keeps its analytic
 * emitter -- those are exact, so they have no parameterization to disagree about, and
 * routing them through the fitter is the thing the classification exists to avoid.
 */
function coupleable(plans is array, r is number) returns boolean
{
    const first = plans[0][r];

    if (first == undefined || first.shape.kind != "freeform")
    {
        return false;
    }

    for (var i = 1; i < size(plans); i += 1)
    {
        const plan = plans[i][r];

        if (plan == undefined
            || plan.shape.kind != "freeform"
            || size(plan.points) != size(first.points)
            || (plan.startDerivative == undefined) != (first.startDerivative == undefined)
            || (plan.endDerivative == undefined) != (first.endDerivative == undefined))
        {
            return false;
        }
    }

    return true;
}

/**
 * Turn one section plan into geometry.
 *
 * @param fits {array} : run-indexed curves already fitted as a family, or undefined to let
 *      each run fit on its own.
 */
function emitSection(context is Context, id is Id, definition is map, plans is array,
    reach is ValueWithUnits, fits) returns map
{
    var pieces = [];
    var kept = 0;

    for (var r = 0; r < size(plans); r += 1)
    {
        const plan = plans[r];
        var curve = undefined;

        if (plan != undefined)
        {
            const fitted = (fits == undefined) ? undefined : fits[r];

            // Printed BEFORE the emit, so if a fit is ever rejected again the last line
            // standing names the run it came from.
            if (reportsRun(definition, r))
            {
                println("[section]   reach " ~ toString(roundToPrecision(reach / millimeter, 4))
                    ~ " mm  run " ~ toString(r)
                    ~ " [" ~ toString(plan.from) ~ ".." ~ toString(plan.to) ~ "]"
                    ~ "  pts " ~ toString(plan.sampled) ~ " distinct " ~ toString(size(plan.points))
                    ~ "  span " ~ toString(roundToPrecision(plan.runSpan / millimeter, 4)) ~ " mm"
                    ~ "  allowArc " ~ toString(plan.allowArc)
                    ~ "  -> " ~ plan.shape.kind
                    ~ "  tangents " ~ toString(plan.startDerivative != undefined)
                    ~ "/" ~ toString(plan.endDerivative != undefined)
                    ~ "  coupled " ~ toString(fitted != undefined));
            }

            curve = emitShape(context, id + ("piece" ~ r), definition, plan.shape, plan.points,
                plan.startDerivative, plan.endDerivative, fitted, reportsRun(definition, r));
            kept += 1;
        }

        pieces = append(pieces, curve);
    }

    if (kept == 0)
    {
        throw regenError("The offset does not cover enough of the edges to build a section from.");
    }

    // One wire, many edges. opExtractWires chains the pieces where they meet, which they do
    // because adjacent runs share a station -- and a trimmed corner shares an exact point.
    const wireId = id + "wire";
    opExtractWires(context, wireId, { "edges" : qUnion(nonEmpty(pieces)) });

    const wires = qCreatedBy(wireId, EntityType.BODY);

    if (definition.debugPrintSurface)
    {
        println("[surface]     section reach " ~ toString(reach) ~ ": "
            ~ toString(kept) ~ " run curve(s) -> "
            ~ toString(size(evaluateQuery(context, wires))) ~ " wire body(ies), "
            ~ toString(size(evaluateQuery(context, qCreatedBy(wireId, EntityType.EDGE)))) ~ " edge(s)");
    }

    return { "wire" : wires, "pieces" : pieces };
}

/**
 * The entries of a run-indexed array that are actually there.
 */
function nonEmpty(entries is array) returns array
{
    var present = [];

    for (var entry in entries)
    {
        if (entry != undefined)
        {
            present = append(present, entry);
        }
    }

    return present;
}

/**
 * The stretches of the offset a surface can be built over.
 *
 * Emphatically NOT the runs. buildRuns splits at every source edge boundary so that G0 input
 * stays G0 in the emitted curves -- a chain of six edges gives six runs even when the offset
 * across them is perfectly continuous. Building over one run covers a sixth of the chain,
 * which is what made the surface come out short.
 *
 * Only two things actually interrupt a surface: a station with no offset at all, and a real
 * step. A step is where insertCrossings put a pair at a profile discontinuity AND the two
 * halves landed on different points -- where the offset merely kinks they coincide, and the
 * surface should run straight through.
 */
function surfaceSpans(driven is map) returns array
{
    var spans = [];
    var from = undefined;

    for (var i = 0; i < size(driven.points); i += 1)
    {
        if (driven.points[i] == undefined)
        {
            spans = closeSpan(spans, from, i - 1);
            from = undefined;
            continue;
        }

        if (from == undefined)
        {
            from = i;
            continue;
        }

        if (isOffsetStep(driven, i))
        {
            spans = closeSpan(spans, from, i - 1);
            from = i;
        }
    }

    return closeSpan(spans, from, size(driven.points) - 1);
}

/**
 * Whether the offset jumps between this station and the one before it.
 */
function isOffsetStep(driven is map, index is number) returns boolean
{
    if (driven.stations[index].crossing != "right")
    {
        return false;
    }

    return norm(driven.points[index] - driven.points[index - 1]) > TOLERANCE.zeroLength * meter;
}

/**
 * Keep a span if it holds enough stations to fit a curve through.
 */
function closeSpan(spans is array, from, end is number) returns array
{
    if (from == undefined || end - from < 1)
    {
        return spans;
    }

    return append(spans, { "start" : from, "end" : end });
}

/**
 * The single stretch used where only one makes sense.
 */
function longestSpan(context is Context, id is Id, driven is map) returns map
{
    const spans = surfaceSpans(driven);

    if (size(spans) == 0)
    {
        throw regenError("The offset profile does not reach the edges anywhere.");
    }

    var best = spans[0];
    for (var span in spans)
    {
        if (span.end - span.start > best.end - best.start)
        {
            best = span;
        }
    }

    if (size(spans) > 1)
    {
        reportFeatureInfo(context, id, "The offset steps into " ~ toString(size(spans))
            ~ " separate stretches; this mode used the longest.");
    }

    return best;
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
    const displacement = sectionDisplacement(definition, reach);

    // In the reference surface the step is a geodesic, not a straight line, so it has to go
    // through the same placement the offset itself used rather than being added to the
    // finished point.
    if (usesReferenceFrame(definition, driven.alongRef))
    {
        return surfaceOffset(driven.alongRef, frame, {
                        "width" : amounts.width + displacement.width,
                        "height" : amounts.height + displacement.height
                    }).point;
    }

    return at + displacement.width * frame.widthAxis + displacement.height * frame.heightAxis;
}

/**
 * The constant added to a station's offset amounts to place a ruled section.
 *
 * One description of where a section is, shared by the point and by the tangent. They are
 * tangents to the same map only if they are displaced the same way, and a section fitted
 * with an end tangent belonging to a different displacement describes a curve its own
 * points do not lie on.
 */
function sectionDisplacement(definition is map, reach is ValueWithUnits) returns map
{
    if (definition.ruledDirection == RuledDirection.WIDTH)
    {
        return { "width" : reach, "height" : 0 * meter };
    }

    return { "width" : 0 * meter, "height" : reach };
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
function connectSourceToOffset(context is Context, id is Id, definition is map, driven is map) returns array
{
    const span = longestSpan(context, id, driven);

    var points = [];

    for (var i = span.start; i <= span.end; i += 1)
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

    // The seed is rebuilt over the same run the offset side uses rather than selected
    // wholesale, which is what keeps an uncovered edge out of the loft.
    // The seed runs along the source edges, not the offset, so the offset's end slopes do
    // not describe it. Unconstrained, as it was.
    const seed = curveThrough(context, id + "seed", definition, points,
        sourceAllowsArc(driven.stations, span.start, span.end), undefined, undefined);
    const offsetSide = ruledSection(context, id + "offset", definition, driven, span, 0 * meter).wire;

    loftSections(context, id + "loft", [seed, offsetSide]);

    return [seed, offsetSide];
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
function loftAcrossOffsets(context is Context, id is Id, definition is map, driven is array) returns array
{
    // Every profile is planned before any of them is emitted, so that a run can be fitted
    // across all of them at once. Fitting each section on its own is what left corresponding
    // curves with independently chosen knot vectors, which opLoft rejects for some pairs and
    // accepts for others that measure identically. See coupledFits.
    var plans = [];

    for (var i = 0; i < size(driven); i += 1)
    {
        plans = append(plans, sectionPlan(definition, driven[i],
                longestSpan(context, id, driven[i]), 0 * meter));
    }

    const fits = coupledFits(context, definition, plans);

    var sections = [];

    for (var i = 0; i < size(plans); i += 1)
    {
        sections = append(sections, emitSection(context, id + ("section" ~ i), definition,
                plans[i], 0 * meter, fits[i]));
    }

    var wires = [];
    for (var section in sections)
    {
        wires = append(wires, section.wire);
    }

    loftColumns(context, id, definition, sections);

    return wires;
}

/**
 * One loft per patch, joined into a single sheet.
 *
 * A patch is one RUN by one pair of adjacent PROFILES, and both halves of that are
 * deliberate.
 *
 * Splitting by run is what gives a face a surface its own size. LoftTopology.COLUMNS gives
 * the right face COUNT, but every one of those faces is a window onto a single underlying
 * surface spanning the whole loft -- which is why the u/v data of a face ran far past the
 * face itself. No loft option trims that: trimProfiles and trimGuidesByProfiles bound a loft
 * by its GUIDES, and there are none here.
 *
 * Splitting by adjacent pair is what keeps the surface straight from one profile to the next.
 * opLoft has no linear-interpolation option -- the interpolation degree follows from the
 * section count, and three or more sections are always fitted with a smooth curve through
 * them. Two sections are ruled by construction. So N profiles wanted as N-1 straight
 * stretches genuinely are N-1 surfaces, and asking one loft for them will always round the
 * corner at every intermediate profile.
 *
 * Together they make every patch a two-section, one-column loft: a single ruled face whose
 * surface covers exactly its own footprint. LoftTopology stops mattering -- there is only
 * ever one column -- and the profile boundaries become real creases instead of a blend.
 *
 * The sheets are unioned rather than left loose, and the imprinted edges are kept: the patch
 * boundaries are the source edge boundaries and the profiles themselves, and erasing them
 * would merge the faces back into the one face all of this was chosen to avoid.
 */
function loftColumns(context is Context, id is Id, definition is map, sections is array)
{
    if (size(sections) < 2)
    {
        return;
    }

    // Blending reads every profile at once, so it cannot be built patch by patch along the
    // loft direction. It still splits by run, which costs nothing and keeps the face sizes.
    const lastFrom = definition.blendThroughProfiles ? 0 : size(sections) - 2;

    var sheets = [];
    var skipped = 0;
    var refused = [];

    for (var r = 0; r < size(sections[0].pieces); r += 1)
    {
        for (var from = 0; from <= lastFrom; from += 1)
        {
            const span = definition.blendThroughProfiles
                ? sections
                : [sections[from], sections[from + 1]];

            var patch = [];
            for (var section in span)
            {
                if (section.pieces[r] != undefined)
                {
                    patch = append(patch, section.pieces[r]);
                }
            }

            // A run one section covers and another does not cannot be lofted across: there
            // is no facing curve. The neighbouring patches still build, so the surface comes
            // out with a hole rather than not at all, and the count is reported.
            if (size(patch) < size(span))
            {
                skipped += 1;
                continue;
            }

            // Printed BEFORE the loft, so the last line standing names the patch that
            // failed. opLoft reports LOFT_FAILED without saying which of its profiles it
            // could not use, and a patch is one run by one profile pair, so that is
            // exactly the pair of curves worth naming.
            if (reportsRun(definition, r))
            {
                var report = "[patch]     run " ~ toString(r) ~ "  from profile " ~ toString(from)
                    ~ "  sections " ~ toString(size(patch));

                // The pair as opLoft sees it. Printed for the patches that succeed as well
                // as the one that throws, because the useful reading is the comparison: a
                // failing patch beside its near-identical neighbour that built.
                if (size(patch) == 2)
                {
                    const walkA = curveWalk(context, patch[0], PATCH_WALK_SAMPLES);
                    const walkB = curveWalk(context, patch[1], PATCH_WALK_SAMPLES);

                    var minSeparation = norm(walkA.points[0] - walkB.points[0]);
                    for (var k = 1; k < PATCH_WALK_SAMPLES; k += 1)
                    {
                        minSeparation = min(minSeparation, norm(walkA.points[k] - walkB.points[k]));
                    }

                    // dir is the dot of the two chord directions: +1 is the pair running the
                    // same way, -1 is a reversed pair, which a loft will not take and which
                    // every other number here is blind to. ends is the endpoint gap the loft
                    // sees, start to start and end to end.
                    report = report
                        ~ "  dir " ~ toString(roundToPrecision(dot(walkA.along, walkB.along), 5))
                        ~ "  ends " ~ toString(roundToPrecision(norm(walkA.points[0] - walkB.points[0]) / millimeter, 4))
                        ~ "/" ~ toString(roundToPrecision(norm(walkA.points[PATCH_WALK_SAMPLES - 1] - walkB.points[PATCH_WALK_SAMPLES - 1]) / millimeter, 4))
                        ~ "  minSep " ~ toString(roundToPrecision(minSeparation / millimeter, 4))
                        ~ "  turnA " ~ toString(roundToPrecision(walkA.minTurn, 5))
                        ~ "  turnB " ~ toString(roundToPrecision(walkB.minTurn, 5))
                        ~ "  stepA " ~ toString(roundToPrecision(walkA.minAdvance / millimeter, 5))
                        ~ "  stepB " ~ toString(roundToPrecision(walkB.minAdvance / millimeter, 5));
                }

                println(report);
            }

            const patchId = id + ("patch" ~ r ~ "_" ~ from);

            // One refusal costs one face, not the surface.
            //
            // opLoft rejects some patches whose curves measure identical to a neighbouring
            // patch it accepts: same 5.8 mm separation the whole way, tangent directions in
            // agreement, no reversal or cusp in either curve, one edge each, fits of the
            // same degree, control-point count and knot vector. Nothing available here
            // distinguishes them, and LOFT_FAILED names neither the profile nor the reason.
            //
            // Letting that kill the feature loses thirteen good faces to one the kernel
            // will not build. The hole marks exactly where it objected, which is worth more
            // than a dead feature, and the refusal is reported rather than swallowed --
            // this is a way to keep working on it, not a fix.
            var loftError = undefined;
            try silent
            {
                opLoft(context, patchId, {
                            "profileSubqueries" : patch,
                            "bodyType" : ToolBodyType.SURFACE
                        });
            }
            catch (error)
            {
                loftError = error;
            }

            // opLoft returns nothing, so the body it should have made is the only honest
            // test of whether it ran.
            if (size(evaluateQuery(context, qCreatedBy(patchId, EntityType.BODY))) == 0)
            {
                refused = append(refused, toString(r) ~ ":" ~ toString(from));

                // The refusal is the trigger, not a toggle: this is the one patch worth
                // seeing in full, and it is only ever a handful of curves.
                if (reportsRun(definition, r))
                {
                    println("[refused]   run " ~ toString(r) ~ "  from profile " ~ toString(from)
                        ~ "  error " ~ toString(loftError));

                    for (var k = 0; k < size(patch); k += 1)
                    {
                        printCurveDump(context, "  profile " ~ toString(from + k), patch[k]);
                    }

                    // The same pair, rebuilt from its own printed definition, lofts when
                    // built alone in a fresh context. So the objection is not to the
                    // geometry but to the state of these two edges, and only a retry in
                    // THIS context can say which. Each retry is deleted on the spot so it
                    // never reaches the output.
                    // Established so far on this pair: rebuilt exact FAILS, swapped FAILS,
                    // shifted 1 m FAILS, CPs rounded to 10 nm FAILS, CPs rounded to 1 um
                    // lofts. The same CPs rounded to 10 nm loft outside the feature, where
                    // the KNOTS were rounded too. These narrow down which quantity it is.
                    const retries = [
                            { "name" : "knots rounded 1e-5        ", "decimals" : -1, "knotDecimals" : 5, "profiles" : [0, 1], "axes" : [true, true, true], "which" : "all" },
                            { "name" : "CPs 100 nm                ", "decimals" : 4, "knotDecimals" : -1, "profiles" : [0, 1], "axes" : [true, true, true], "which" : "all" },
                            { "name" : "CPs 1 um, profile 0 only  ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [0], "axes" : [true, true, true], "which" : "all" },
                            { "name" : "CPs 1 um, profile 1 only  ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [1], "axes" : [true, true, true], "which" : "all" },
                            { "name" : "CPs 1 um, X only          ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [0, 1], "axes" : [true, false, false], "which" : "all" },
                            { "name" : "CPs 1 um, Y only          ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [0, 1], "axes" : [false, true, false], "which" : "all" },
                            { "name" : "CPs 1 um, Z only          ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [0, 1], "axes" : [false, false, true], "which" : "all" },
                            { "name" : "CPs 1 um, ends only       ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [0, 1], "axes" : [true, true, true], "which" : "ends" },
                            { "name" : "CPs 1 um, interior only   ", "decimals" : 3, "knotDecimals" : -1, "profiles" : [0, 1], "axes" : [true, true, true], "which" : "interior" }
                        ];

                    for (var t = 0; t < size(retries); t += 1)
                    {
                        println("  retry " ~ retries[t].name ~ ": "
                            ~ loftRetry(context, patchId + ("retry" ~ t), patch, retries[t]));
                    }
                }

                continue;
            }

            sheets = append(sheets, qCreatedBy(patchId, EntityType.BODY));
        }
    }

    if (size(sheets) == 0)
    {
        if (size(refused) > 0)
        {
            throw regenError("Every patch was refused by the loft, so no surface was built.");
        }

        throw regenError("No run is covered by every offset, so there is nothing to loft between.");
    }

    // A hole in the surface is missing output, so it is a warning and not an info: the
    // result is not what was asked for, and it is not safe to leave that to the console.
    if (size(refused) > 0)
    {
        reportFeatureWarning(context, id, "The loft refused " ~ toString(size(refused))
            ~ " of " ~ toString(size(refused) + size(sheets))
            ~ " patches, so the surface has a hole at run:profile " ~ join(refused, ", ")
            ~ ". Every other patch built.");
    }

    if (definition.debugPrintSurface)
    {
        println("[surface]     " ~ toString(size(sheets)) ~ " patch loft(s) ("
            ~ toString(size(sections[0].pieces)) ~ " run(s) x "
            ~ toString(lastFrom + 1) ~ " profile span(s))"
            ~ (skipped > 0 ? ", " ~ toString(skipped) ~ " skipped for missing coverage" : "")
            ~ (size(refused) > 0 ? ", " ~ toString(size(refused)) ~ " refused by the loft: "
                    ~ join(refused, ", ") : ""));
    }

    if (size(sheets) > 1)
    {
        opBoolean(context, id + "join", {
                    "tools" : qUnion(sheets),
                    "operationType" : BooleanOperationType.UNION,
                    "eraseImprintedEdges" : false
                });
    }
}

// ============================================================================
// Debug
// ============================================================================

/**
 * What each offset produced, before anything is lofted.
 *
 * The section point count is the number to read: a loft that collapses usually got sections
 * of unequal length, or one section that doubled back. Both show here before opLoft is
 * reached, which is earlier than the error does.
 */
function printSurfacePlan(context is Context, definition is map, driven is array)
{
    println("");
    println("========== driven offset surface ==========");
    println("[surface] mode: " ~ surfaceModeName(definition.surfaceMode)
        ~ ", offsets driven: " ~ toString(size(driven)));
    println("[surface] shared chain/reference/frames: " ~ toString(size(driven[0].stations))
        ~ " stations built once for all " ~ toString(size(driven)) ~ " offset(s)"
        ~ (definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT
                ? ", run structure matched across them" : ""));

    for (var i = 0; i < size(driven); i += 1)
    {
        const offset = driven[i];
        const spans = surfaceSpans(offset);

        println("[surface]   offset " ~ toString(i) ~ " '" ~ offset.name ~ "': "
            ~ toString(size(offset.runs)) ~ " run(s) -> "
            ~ toString(size(spans)) ~ " surface stretch(es)");

        for (var span in spans)
        {
            println("[surface]     stretch " ~ toString(span.start) ~ "-" ~ toString(span.end)
                ~ "  (" ~ toString(span.end - span.start + 1) ~ " stations)");
        }
    }
}

/**
 * What actually came out.
 */
function printSurfaceResult(context is Context, id is Id)
{
    const sheets = evaluateQuery(context, qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SHEET));
    const faces = evaluateQuery(context, qCreatedBy(id, EntityType.FACE));

    println("[surface] result: " ~ toString(size(sheets)) ~ " sheet body(ies), "
        ~ toString(size(faces)) ~ " face(s)");
    println("========== end driven offset surface ==========");
    println("");
}

/**
 * The chosen mode, for the log.
 */
function surfaceModeName(mode is SurfaceMode) returns string
{
    if (mode == SurfaceMode.RULED_OFFSET_PROFILE)
    {
        return "ruled from offset";
    }
    if (mode == SurfaceMode.CONNECTED_OFFSET)
    {
        return "connect source to offset";
    }

    return "loft between profiles";
}

// ============================================================================
// Shared
// ============================================================================

/** How many places along a section curve the patch diagnostic looks at. */
const PATCH_WALK_SAMPLES = 9;

/**
 * How a section curve behaves between its ends.
 *
 * The endpoint measures cannot see a fit that loops or cusps in its interior, and that is
 * the one shape that fails a loft while every endpoint number still reads healthy. Two
 * things give it away: `minTurn`, the smallest dot product between consecutive sample
 * tangents, which drops toward or below zero where the curve turns back on itself; and
 * `minAdvance`, the smallest step along the curve's own chord, which goes negative where
 * it actually reverses.
 */
function curveWalk(context is Context, edge is Query, samples is number) returns map
{
    var points = [];
    var directions = [];

    for (var k = 0; k < samples; k += 1)
    {
        const at = evEdgeTangentLine(context, { "edge" : edge, "parameter" : k / (samples - 1) });

        points = append(points, at.origin);
        directions = append(directions, at.direction);
    }

    const chord = points[samples - 1] - points[0];
    const along = (norm(chord) < TOLERANCE.zeroLength * meter) ? vector(1, 0, 0) : normalize(chord);

    var minTurn = 1;
    var minAdvance = norm(chord);

    for (var k = 1; k < samples; k += 1)
    {
        minTurn = min(minTurn, dot(directions[k], directions[k - 1]));
        minAdvance = min(minAdvance, dot(points[k] - points[k - 1], along));
    }

    return { "points" : points, "along" : along, "minTurn" : minTurn, "minAdvance" : minAdvance };
}

/**
 * Print one edge the way the kernel holds it: the curve definition, control point by control
 * point, so the pair a loft refused can be rebuilt and bisected outside the feature.
 */
function printCurveDump(context is Context, label is string, edge is Query)
{
    const edges = evaluateQuery(context, edge);
    if (size(edges) != 1)
    {
        println(label ~ ": " ~ toString(size(edges)) ~ " edge(s), expected 1");
        return;
    }

    const curve = evCurveDefinition(context, { "edge" : edges[0] });
    const length = evLength(context, { "entities" : edges[0] });
    const ends = evEdgeTangentLines(context, { "edge" : edges[0], "parameters" : [0, 1] });

    println(label ~ ": length " ~ toString(roundToPrecision(length / millimeter, 4)) ~ " mm"
        ~ "  start " ~ pointText(ends[0].origin) ~ " dir " ~ dirText(ends[0].direction)
        ~ "  end " ~ pointText(ends[1].origin) ~ " dir " ~ dirText(ends[1].direction));

    if (curve is BSplineCurve)
    {
        println("    bspline degree " ~ toString(curve.degree)
            ~ "  periodic " ~ toString(curve.isPeriodic)
            ~ "  CPs " ~ toString(size(curve.controlPoints))
            ~ "  knots " ~ toString(size(curve.knots)) ~ " " ~ toString(curve.knots)
            ~ "  weights " ~ (curve.weights == undefined ? "none" : toString(curve.weights)));

        for (var k = 0; k < size(curve.controlPoints); k += 1)
        {
            println("    cp " ~ toString(k) ~ "  " ~ pointText(curve.controlPoints[k]));
        }
    }
    else if (curve is Line)
    {
        println("    line origin " ~ pointText(curve.origin) ~ " dir " ~ dirText(curve.direction));
    }
    else if (curve is Circle)
    {
        println("    circle radius " ~ toString(roundToPrecision(curve.radius / millimeter, 4))
            ~ " mm  center " ~ pointText(curve.coordSystem.origin)
            ~ " normal " ~ dirText(curve.coordSystem.zAxis));
    }
    else
    {
        println("    " ~ toString(curve));
    }
}

/**
 * Loft a pair once more under a scratch id, say whether the kernel took it, and delete
 * whatever was made. Diagnostic only; nothing survives the call.
 *
 * Every retry rebuilds fresh curves from each edge's own evCurveDefinition, perturbed as the
 * options say, so that what the kernel objects to can be narrowed down one quantity at a time.
 *
 * @param options {map} :
 *      decimals {number} : round control points to this many decimal places in mm; -1 exact.
 *      knotDecimals {number} : round the knots to this many decimal places; -1 exact.
 *      profiles {array} : indices of the profiles whose control points are rounded.
 *      axes {array} : [x, y, z] booleans, which coordinates are rounded.
 *      which {string} : "all", "ends" or "interior" control points.
 */
function loftRetry(context is Context, id is Id, profiles is array, options is map) returns string
{
    var subqueries = [];
    var status = "";

    for (var k = 0; k < size(profiles); k += 1)
    {
        const curve = evCurveDefinition(context, { "edge" : profiles[k] });
        const curveId = id + ("curve" ~ k);

        if (curve is BSplineCurve)
        {
            const roundThis = isIn(k, options.profiles) && options.decimals >= 0;
            const last = size(curve.controlPoints) - 1;

            var controlPoints = [];
            for (var c = 0; c <= last; c += 1)
            {
                var point = curve.controlPoints[c];
                const isEnd = (c == 0 || c == last);
                const inScope = options.which == "all"
                    || (options.which == "ends" && isEnd)
                    || (options.which == "interior" && !isEnd);

                if (roundThis && inScope)
                {
                    var rounded = [];
                    for (var a = 0; a < 3; a += 1)
                    {
                        rounded = append(rounded, options.axes[a]
                                ? roundToPrecision(point[a] / millimeter, options.decimals)
                                : point[a] / millimeter);
                    }
                    point = vector(rounded) * millimeter;
                }
                controlPoints = append(controlPoints, point);
            }

            var knots = curve.knots;
            if (options.knotDecimals >= 0)
            {
                var roundedKnots = [];
                for (var knot in curve.knots)
                {
                    roundedKnots = append(roundedKnots, roundToPrecision(knot, options.knotDecimals));
                }
                knots = knotArray(roundedKnots);
            }

            opCreateBSplineCurve(context, curveId, {
                        "bSplineCurve" : mergeMaps(curve, { "controlPoints" : controlPoints, "knots" : knots }) as BSplineCurve
                    });
        }
        else
        {
            status = "not retried, profile " ~ toString(k) ~ " is not a B-spline";
        }

        subqueries = append(subqueries, qCreatedBy(curveId, EntityType.EDGE));
    }

    if (status == "")
    {
        try silent
        {
            opLoft(context, id + "loft", {
                        "profileSubqueries" : subqueries,
                        "bodyType" : ToolBodyType.SURFACE
                    });
        }
        catch (error)
        {
            status = "FAILED " ~ toString(error);
        }

        if (status == "")
        {
            status = (size(evaluateQuery(context, qCreatedBy(id + "loft", EntityType.BODY))) > 0)
                ? "OK" : "no body";
        }
    }

    if (size(evaluateQuery(context, qCreatedBy(id, EntityType.BODY))) > 0)
    {
        opDeleteBodies(context, id + "delete", { "entities" : qCreatedBy(id, EntityType.BODY) });
    }

    return status;
}

function pointText(point is Vector) returns string
{
    // Ten places in millimetres: enough to rebuild the curve bit for bit outside the feature.
    return "(" ~ toString(roundToPrecision(point[0] / millimeter, 10))
        ~ ", " ~ toString(roundToPrecision(point[1] / millimeter, 10))
        ~ ", " ~ toString(roundToPrecision(point[2] / millimeter, 10)) ~ ")";
}

function dirText(direction is Vector) returns string
{
    return "(" ~ toString(roundToPrecision(direction[0], 5))
        ~ ", " ~ toString(roundToPrecision(direction[1], 5))
        ~ ", " ~ toString(roundToPrecision(direction[2], 5)) ~ ")";
}

/**
 * One curve through a point list, as a body a loft can take.
 */
function curveThrough(context is Context, id is Id, definition is map, points is array,
    allowArc is boolean, startDerivative, endDerivative) returns Query
{
    return emitShape(context, id, definition,
        classifyPoints(points, definition.approximationTolerance, allowArc),
        points, startDerivative, endDerivative, undefined, definition.debugPrintSurface);
}

/**
 * The approximation controls, as emitSplineCurve and approximateFamily want them.
 *
 * Named for the fit rather than the feature because driven_edge_offset already has an
 * approximationSettings of its own, and two functions of the same name and arity visible at
 * once is an identical-signature clash.
 */
function fitSettings(definition is map) returns map
{
    return fitSettings(definition, definition.debugPrintSurface);
}

/**
 * @param report {boolean} : whether emitFittedCurve prints the fit. The run filter decides
 *      this per run; the plain flag is for callers with no run in hand.
 */
function fitSettings(definition is map, report is boolean) returns map
{
    return {
            "approximationDegree" : definition.approximationDegree,
            "approximationTolerance" : definition.approximationTolerance,
            "approximationMaxCPs" : definition.approximationMaxCPs,
            "debugFit" : report
        };
}

/**
 * Whether the surface debug prints cover run r: the print flag, narrowed by "Only run".
 */
function reportsRun(definition is map, r is number) returns boolean
{
    return definition.debugPrintSurface && (definition.debugRun < 0 || definition.debugRun == r);
}

/**
 * Emit a run as the thing it was classified to be.
 *
 * Classified, not fitted -- the same decision emitRuns makes about the very same points.
 *
 * Fitting everything with approximateSpline reproduces the positions to tolerance and wrecks
 * the curvature: a constant-radius arc sampled and re-approximated as a degree-3 spline comes
 * back with curvature swinging over 100% along its length, and the loft faithfully carries
 * that into the face. Measured against a constant-curvature input the offset wire held 0%
 * spread on every run while the section built from the same points held 142% on the first and
 * 129% on the last.
 *
 * classifyPoints already knows when a run is a line or an arc, and emitLineCurve and
 * emitArcCurve make the exact thing rather than an approximation of it. Only genuinely
 * freeform runs reach the fitter.
 *
 * @param fitted : a curve already fitted as part of a family, or undefined to fit here. It
 *      is only ever supplied for a freeform run -- a line and an arc are exact, so there is
 *      nothing for a family fit to make consistent.
 * @param report {boolean} : print the fit, where there is one.
 */
function emitShape(context is Context, id is Id, definition is map, shape is map, points is array,
    startDerivative, endDerivative, fitted, report is boolean) returns Query
{
    if (shape.kind == "line")
    {
        emitLineCurve(context, id, shape.start, shape.end);
    }
    else if (shape.kind == "arc")
    {
        emitArcCurve(context, id, shape);
    }
    else if (fitted != undefined)
    {
        emitFittedCurve(context, id, fitted, points, fitSettings(definition, report));
    }
    else
    {
        // The end slopes, where the caller has them. A freeform offset is exactly the case
        // this matters for: the fit is what determines the shape, and an unconstrained fit
        // leaves the ends free to bulge away from the run it is supposed to continue.
        emitSplineCurve(context, id, points, startDerivative, endDerivative, fitSettings(definition, report));
    }

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

    // COLUMNS, not the MINIMAL default: minimal merges every matching pair of profile
    // segments into one face, which throws away exactly the G0 structure the sections were
    // built to carry. One face per segment pair keeps a source edge boundary visible as a
    // face boundary.
    opLoft(context, id, {
                "profileSubqueries" : sections,
                "bodyType" : ToolBodyType.SURFACE,
                "loftTopology" : LoftTopology.COLUMNS
            });
}

/**
 * Name the surfaces and clear away whatever the user did not ask to keep.
 *
 * The section curves are scaffolding either way: they are rebuilt from the stations, not the
 * emitted offset, so keeping them would leave a second copy of every curve beside the wires
 * the offsets already produced.
 */
function finishSurface(context is Context, id is Id, definition is map, driven is array,
    sections is array)
{
    var wires = [];
    for (var offset in driven)
    {
        wires = append(wires, offset.wires);
    }

    const surfaces = qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.SHEET);

    // One rule, stated once: the surfaces always stay, the offset wires stay if asked for,
    // the section wires stay if asked for, and everything else this feature made goes.
    //
    // "Everything else" is doing real work. A section is assembled from one loose curve per
    // run and then extracted into a wire, and the loose curves have to outlive the extraction
    // because the per-column lofts are built from them individually. So they are still lying
    // around at this point, and a rule phrased as "keep the sections" written as anything
    // broader than the wires themselves keeps all of them too -- which is a body per profile
    // edge sitting beside the assembled wire, twice over.
    var keep = [surfaces];

    if (definition.keepWires)
    {
        keep = append(keep, qUnion(wires));
    }
    if (definition.debugKeepSections)
    {
        keep = append(keep, qOwnerBody(qUnion(sections)));
    }

    const remove = qSubtraction(qCreatedBy(id, EntityType.BODY), qUnion(keep));

    if (definition.debugPrintSurface)
    {
        println("[surface] cleanup: "
            ~ toString(size(evaluateQuery(context, qUnion(wires)))) ~ " offset wire(s) "
            ~ (definition.keepWires ? "kept" : "deleted") ~ ", "
            ~ toString(size(evaluateQuery(context, qOwnerBody(qUnion(sections))))) ~ " section wire(s) "
            ~ (definition.debugKeepSections ? "kept" : "deleted") ~ ", "
            ~ toString(size(evaluateQuery(context, remove))) ~ " body(ies) removed, "
            ~ toString(size(evaluateQuery(context, surfaces))) ~ " surface(s) kept");
    }

    if (!isQueryEmpty(context, remove))
    {
        opDeleteBodies(context, id + "cleanup", { "entities" : remove });
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
