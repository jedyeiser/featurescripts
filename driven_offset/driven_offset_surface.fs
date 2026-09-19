FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
// LoftTopology has its own module and common.fs does not reach it -- the same gap as
// ProjectionType and getQueryVariable elsewhere in this document.
import(path : "onshape/std/lofttopology.gen.fs", version : "3070.0");

export import(path : "a2665e22c07b7a6929ce4e80", version : "d49252259f7b34bf7da639ec");
import(path : "d009ddf4a8dd9534fc4dc4b5", version : "e11a408e487b65a9b42efac8");
import(path : "6479d7fbd0ec7d11e0ae6c69", version : "4f533950f9fcbe2083572c8b");
import(path : "786f62f4d67ed8d9c7d56d16", version : "");
// bspline_compat: exact knot/degree/join algebra for the unified patches.
import(path : "6b635e74c92bd23387e850c1", version : "");

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

        // Directly under the measure and reference controls, as in the plain offset.
        offsetAlignmentPredicate(definition);

        // Declared here rather than through offsetBreakPredicate so the description can
        // talk about faces. The default stays "every source edge" on purpose, even though
        // a surface usually wants the other: when a feature type gains a parameter,
        // Onshape writes the ANNOTATION default into every saved feature -- the defineFeature
        // defaults map does not shield them -- and the first push with the other default
        // here silently re-ran SW_Rout_Surface with 3 runs instead of 7, which broke the
        // intersection curve selected off its faces. Choose it per feature.
        annotation { "Name" : "Break output at", "Default" : RunBreakMode.SOURCE_EDGES, "UIHint" : UIHint.SHOW_LABEL, "Description" : "Where the offsets, the sections and therefore the faces are split. Every source edge: one face per source edge, joint for joint. Corners and offset breaks: only where the source turns through a real corner or a profile steps or kinks; tangent-continuous source edges run together into one face. A loft between profiles usually wants the latter." }
        definition.runBreakMode is RunBreakMode;

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

        // A loft between profiles decides its own corner treatment (see driveOffsets), so
        // the group is not offered there; the other two modes rule from one offset and can
        // round or leave a corner as the plain offset does.
        if (definition.surfaceMode != SurfaceMode.MULTIPROFILE_LOFT)
        {
            offsetCornersPredicate(definition);
        }
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
        "runBreakMode" : RunBreakMode.SOURCE_EDGES,
        "keepWires" : false,
        "debugPrintSurface" : false,
        "debugRun" : -1,
        "debugKeepSections" : false
    });

// ============================================================================
// Driving the offsets
// ============================================================================

/**
 * Lay every profile's runs over one shared set of cells.
 *
 * Runs are grouped across profiles by overlap: two runs that share any station are the same
 * cell. That is what pairs them for a loft when the profiles do not all reach the same
 * stretch of the edges -- a profile that stops short has no run in the cells beyond, and
 * pairing by list position would have shifted every later run by one, or run off the end
 * of the shorter list. Overlap rather than exact equality because a corner trim shortens a
 * run in one profile and not another.
 *
 * Every profile splits at every profile's breaks (matchRuns), so a run of one profile that
 * overlaps TWO runs of another is a break the shared context did not see -- a gap or a
 * vertex in a selection that is not one clean wire. That is reported, naming both offsets
 * and the coordinate, rather than left to surface as an index out of bounds downstream.
 *
 * @param names {array} : one display name per plan, for the report.
 * @returns {array} : per plan, an array over the cells of that plan's run or undefined.
 */
function alignRuns(plans is array, names is array, coords is map) returns array
{
    var items = [];
    for (var k = 0; k < size(plans); k += 1)
    {
        for (var run in plans[k].runs)
        {
            items = append(items, { "profile" : k, "run" : run });
        }
    }

    items = sort(items, function(a, b) { return a.run.start - b.run.start; });

    // Each cell: the stations it covers across every member, and one run per profile.
    var cellSpans = [];
    var members = [];

    for (var item in items)
    {
        const last = size(cellSpans) - 1;

        if (last >= 0 && item.run.start <= cellSpans[last].end)
        {
            if (members[last][item.profile] != undefined)
            {
                const other = members[last][item.profile];
                var against = "another offset";
                for (var j = 0; j < size(plans); j += 1)
                {
                    if (j != item.profile && members[last][j] != undefined)
                    {
                        against = "'" ~ names[j] ~ "'";
                        break;
                    }
                }

                throw regenError("'" ~ names[item.profile] ~ "' breaks at "
                    ~ toString(roundToPrecision(coords.values[item.run.start] / millimeter, 3))
                    ~ " mm along the edges, inside a run of " ~ against ~ " (stations "
                    ~ toString(other.start) ~ " to " ~ toString(item.run.end) ~ "). Every offset has to break where the others do; "
                    ~ "check that '" ~ names[item.profile] ~ "' is one clean wire with no vertex or gap there.",
                    ["offsets"]);
            }

            cellSpans[last].end = max(cellSpans[last].end, item.run.end);
            members[last][item.profile] = item.run;
        }
        else
        {
            cellSpans = append(cellSpans, { "start" : item.run.start, "end" : item.run.end });
            var slots = makeArray(size(plans), undefined);
            slots[item.profile] = item.run;
            members = append(members, slots);
        }
    }

    var cells = [];
    for (var k = 0; k < size(plans); k += 1)
    {
        var row = [];
        for (var c = 0; c < size(members); c += 1)
        {
            row = append(row, members[c][k]);
        }
        cells = append(cells, row);
    }

    return cells;
}

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
    // A loft between profiles pairs edge i of one offset with edge i of the next, so every
    // offset has to come out with the same edges: the shared crossings and the shared
    // junction flags already give them identical runs, and the corner treatment must not
    // add or remove any. Of the corner modes only two keep the count -- extending a gap to
    // the miter and trimming an overlap to the crossing, each of which leaves one exact
    // corner point on both adjoining runs. An arc filler is an extra edge on the side that
    // gaps with no counterpart on the side that overlaps (the same corner does one or the
    // other depending on which side of the source the profile is), and an open corner or a
    // kept crossing has no shared point for the patches to meet on. So a paired loft uses
    // the miter and the trim whatever the dialog holds, and the Corners group is not shown
    // for it. The patch boundary at a corner is then the ruling between the two offsets'
    // corner points, and the union closes across it.
    const paired = definition.surfaceMode == SurfaceMode.MULTIPROFILE_LOFT;
    const cornerTreatment = paired
        ? { "cornerGapMode" : CornerGapMode.EXTEND, "cornerOverlapMode" : CornerOverlapMode.TRIM }
        : {};

    var plans = [];
    for (var i = 0; i < size(entries); i += 1)
    {
        plans = append(plans, planOffset(context, mergeMaps(definition, mergeMaps(cornerTreatment, {
                            "offsetProfile" : entries[i].offsetProfile
                        })), shared, i));
    }

    // Lay every profile's runs over one set of cells, so that cell r is the same stretch of
    // the edges in all of them and a profile that stops short simply has no run there.
    var names = [];
    for (var i = 0; i < size(entries); i += 1)
    {
        names = append(names, entries[i].offsetName == "" ? "offset " ~ toString(i + 1) : entries[i].offsetName);
    }

    // Only the loft between profiles pairs sections, so only it needs the alignment; the
    // other two modes rule from each offset alone, and their profiles are free to break
    // where they like.
    var cells = [];
    if (paired)
    {
        cells = alignRuns(plans, names, shared.coords);
    }
    else
    {
        for (var plan in plans)
        {
            cells = append(cells, plan.runs);
        }
    }

    if (definition.joinTangentRuns && !paired)
    {
        for (var k = 0; k < size(cells); k += 1)
        {
            cells[k] = applyRunMerges(cells[k], tangentRunMerges([plans[k].points], cells[k],
                    shared.stations, definition.approximationTolerance));
        }
    }

    if (definition.joinTangentRuns && paired)
    {
        // One walk over the shared cells, testing every profile's points at each step, so
        // the merges come out identical for all of them by construction rather than by
        // intersecting decisions taken separately.
        var pointsPerProfile = [];
        for (var plan in plans)
        {
            pointsPerProfile = append(pointsPerProfile, plan.points);
        }

        const common = alignedRunMerges(pointsPerProfile, cells, shared.stations,
            definition.approximationTolerance);

        var mergedCells = [];
        for (var k = 0; k < size(cells); k += 1)
        {
            mergedCells = append(mergedCells, applyRunMerges(cells[k], common));
        }
        cells = mergedCells;

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

    // Each profile's own runs are the cells it reaches; the aligned layout rides along for
    // the loft to pair sections over.
    var laid = [];
    for (var k = 0; k < size(plans); k += 1)
    {
        laid = append(laid, mergeMaps(plans[k], { "runs" : nonEmpty(cells[k]), "cells" : cells[k] }));
    }
    plans = laid;

    var driven = [];

    for (var i = 0; i < size(entries); i += 1)
    {
        const offsetId = id + ("offset" ~ i);

        // Four of the Debug toggles report things that are now genuinely shared -- the
        // reference, the frames, the frame table. Leaving them on for every offset printed
        // the same two hundred frames once per profile, which is what made the duplication
        // visible in the first place. Report them for the first offset only.
        const first = (i == 0);
        const perOffset = mergeMaps(definition, mergeMaps(cornerTreatment, {
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
                }));

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

    // Over the aligned cells, not this profile's own run list, so index r is the same
    // stretch of the edges in every section. A cell this profile does not reach is a hole.
    for (var r = 0; r < size(driven.cells); r += 1)
    {
        const run = driven.cells[r];
        var plan = undefined;

        const from = (run == undefined) ? 0 : max(run.start, span.start);
        const to = (run == undefined) ? -1 : min(run.end, span.end);

        if (run != undefined && to - from >= 1)
        {
            // Through the same points the offset wire is emitted through, exact corner and
            // terminal points included. Built from the stations alone, a section stopped at
            // the last station a trimmed run kept -- up to one spacing short of the crossing
            // the wire itself reaches -- so the patches either side of every corner missed
            // each other and the union left them as separate bodies.
            const points = sectionPoints(definition, driven, run, from, to, reach);
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
                const gates = sourceShapeGates(driven.stations, from, to);

                plan = {
                        "from" : from,
                        "to" : to,
                        "sampled" : to - from + 1,
                        "points" : distinct,
                        "runSpan" : runSpan,
                        "gates" : gates,
                        "shape" : classifyPoints(distinct, definition.approximationTolerance, gates.allowArc, gates.allowLine),
                        "startDerivative" : runTangent(driven.stations, driven.coords, driven.sided,
                                definition, driven.alongRef, run, from, displacement),
                        "endDerivative" : runTangent(driven.stations, driven.coords, driven.sided,
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
                    ~ "  arc/line " ~ toString(plan.gates.allowArc) ~ "/" ~ toString(plan.gates.allowLine)
                    ~ "  -> " ~ plan.shape.kind
                    ~ "  tangents " ~ toString(plan.startDerivative != undefined)
                    ~ "/" ~ toString(plan.endDerivative != undefined)
                    ~ "  coupled " ~ toString(fitted != undefined)
                    ~ tangentAgreement(plan));
            }

            curve = emitShape(context, id + ("piece" ~ r), definition, plan.shape, plan.points,
                plan.startDerivative, plan.endDerivative, fitted, reportsRun(definition, r));
            kept += 1;

            // With one run singled out, show the curve as emitted: what the loft is given.
            if (definition.debugPrintSurface && definition.debugRun == r)
            {
                printCurveDump(context, "  emitted run " ~ toString(r), curve);
            }
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
 *
 * Judged on the offset AMOUNTS, not the points. A crossing snapped onto a G0 source vertex
 * has its two halves on different points -- the corner gap or overlap the corner treatment
 * closes -- while the offset itself reads the same value either side of the break, and a
 * surface runs straight through a treated corner.
 */
function isOffsetStep(driven is map, index is number) returns boolean
{
    if (driven.stations[index].crossingHead != true)
    {
        return false;
    }

    const here = driven.sided[index];
    const there = driven.sided[index - 1];

    if (here == undefined || there == undefined)
    {
        return here != there;
    }

    return abs(here.width - there.width) > TOLERANCE.zeroLength * meter
        || abs(here.height - there.height) > TOLERANCE.zeroLength * meter;
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
    const amounts = driven.sided[index];

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
 * A run's point list stepped sideways by the section's reach.
 *
 * The stations from `from` to `to` are displaced through displacedPoint as before. An exact
 * start or end point -- a corner or terminal point runPointList puts on the list where the
 * range reaches the run's own end -- has no station of its own, so it takes the step of the
 * station beside it, carried over as a translation. At zero reach, which is every loft
 * between profiles, nothing moves and the exact point is exact.
 */
function sectionPoints(definition is map, driven is map, run is map, from is number, to is number,
    reach is ValueWithUnits) returns array
{
    var points = [];

    if (from == run.start && run.startPoint != undefined)
    {
        points = append(points, run.startPoint + sectionStep(definition, driven, from, reach));
    }

    for (var i = from; i <= to; i += 1)
    {
        const at = displacedPoint(definition, driven, i, reach);

        if (at != undefined)
        {
            points = append(points, at);
        }
    }

    if (to == run.end && run.endPoint != undefined)
    {
        points = append(points, run.endPoint + sectionStep(definition, driven, to, reach));
    }

    return points;
}

/**
 * How far and which way one station's point moves under a section's reach.
 */
function sectionStep(definition is map, driven is map, index is number, reach is ValueWithUnits) returns Vector
{
    const stepped = displacedPoint(definition, driven, index, reach);

    if (stepped == undefined || driven.points[index] == undefined)
    {
        return vector(0, 0, 0) * meter;
    }

    return stepped - driven.points[index];
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
        sourceShapeGates(driven.stations, span.start, span.end), undefined, undefined);
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
 * Which profiles are lofted to which, cell by cell, and over which cells each such pairing
 * runs.
 *
 * Profiles are allowed to differ in extent, so a cell may hold any subset of them. The
 * patches are built between the profiles that are THERE: adjacent present profiles pair
 * up, bridging across any that stop short, so a short middle profile hands the outer two
 * to each other. Blending takes every present profile in the cell at once. A cell only one
 * profile reaches has nothing to loft to, and is left to the caller to count.
 *
 * @returns {map} :
 *      cells {array} : per cell, the member tuples lofted there (arrays of profile indices).
 *      stretches {map} : keyed by `toString(members)`, then by cell: `{ first, last }`, the
 *          contiguous range of cells that pairing covers around that cell. A pairing's
 *          terminal patches are where a member's overhang gets absorbed.
 */
function patchPairings(definition is map, sections is array) returns map
{
    var cells = [];

    for (var r = 0; r < size(sections[0].pieces); r += 1)
    {
        var present = [];
        for (var i = 0; i < size(sections); i += 1)
        {
            if (sections[i].pieces[r] != undefined)
            {
                present = append(present, i);
            }
        }

        var tuples = [];
        if (size(present) >= 2)
        {
            if (definition.blendThroughProfiles)
            {
                tuples = [present];
            }
            else
            {
                for (var k = 0; k + 1 < size(present); k += 1)
                {
                    tuples = append(tuples, [present[k], present[k + 1]]);
                }
            }
        }

        cells = append(cells, tuples);
    }

    // Contiguous runs of cells per tuple. A tuple absent from a cell and back again later
    // starts a new stretch; the gap between is a hole, not an overhang.
    var stretches = {};
    var open = {};

    for (var r = 0; r < size(cells); r += 1)
    {
        var seen = {};
        for (var members in cells[r])
        {
            const key = toString(members);
            seen[key] = true;

            if (open[key] == undefined)
            {
                open[key] = { "first" : r, "last" : r };
            }
            else
            {
                open[key].last = r;
            }
        }

        for (var key, range in open)
        {
            if (seen[key] != true)
            {
                stretches = recordStretch(stretches, key, range);
                open[key] = undefined;
            }
        }

        var still = {};
        for (var key, range in open)
        {
            if (range != undefined)
            {
                still[key] = range;
            }
        }
        open = still;
    }

    for (var key, range in open)
    {
        stretches = recordStretch(stretches, key, range);
    }

    return { "cells" : cells, "stretches" : stretches };
}

function recordStretch(stretches is map, key is string, range is map) returns map
{
    var byCell = (stretches[key] == undefined) ? {} : stretches[key];
    for (var r = range.first; r <= range.last; r += 1)
    {
        byCell[r] = range;
    }
    stretches[key] = byCell;
    return stretches;
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

    var sheets = [];
    var skipped = 0;
    var refused = [];

    // Which profiles pair up in each cell, then the cells each pairing covers, so that a
    // pairing's terminal patches know where its coverage ends.
    const cellCount = size(sections[0].pieces);
    const pairings = patchPairings(definition, sections);

    for (var r = 0; r < cellCount; r += 1)
    {
        if (size(pairings.cells[r]) == 0)
        {
            skipped += 1;
        }

        for (var members in pairings.cells[r])
        {
            const from = members[0];
            const stretch = pairings.stretches[toString(members)][r];

            // One query per member. In the interior of a pairing's coverage that is the
            // member's piece for this cell. At either end of the coverage a member that
            // reaches further than its partners carries its overhang along: every piece
            // of its own beyond the last shared cell, contiguously, joined to the terminal
            // piece. The loft then runs from the shared cell out to the member's real end,
            // the way a loft of the whole wires would -- but the stretch it takes to get
            // there is confined to this one patch, and every interior patch keeps the exact
            // station-for-station pairing.
            var patch = [];
            var chains = [];
            for (var i in members)
            {
                var before = [];
                if (r == stretch.first)
                {
                    for (var c = r - 1; c >= 0 && sections[i].pieces[c] != undefined; c -= 1)
                    {
                        before = append(before, sections[i].pieces[c]);
                    }
                }

                var after = [];
                if (r == stretch.last)
                {
                    for (var c = r + 1; c < cellCount && sections[i].pieces[c] != undefined; c += 1)
                    {
                        after = append(after, sections[i].pieces[c]);
                    }
                }

                // In order along the edges, for the exact construction; as one query, for
                // the loft and the reports.
                const chain = concatenateArrays([reverse(before), [sections[i].pieces[r]], after]);
                chains = append(chains, chain);
                patch = append(patch, size(chain) == 1 ? chain[0] : qUnion(chain));
            }

            // Printed BEFORE the loft, so the last line standing names the patch that
            // failed. opLoft reports LOFT_FAILED without saying which of its profiles it
            // could not use, and a patch is one run by one profile pair, so that is
            // exactly the pair of curves worth naming.
            // What the kernel holds for each section, read once: the construction is chosen
            // on it and the report names the choice.
            var curves = [];
            for (var section in patch)
            {
                const edges = evaluateQuery(context, section);
                curves = append(curves, size(edges) == 1
                        ? evCurveDefinition(context, { "edge" : edges[0] })
                        : undefined);
            }

            if (reportsRun(definition, r))
            {
                var report = "[patch]     run " ~ toString(r) ~ "  profiles " ~ toString(members)
                    ~ "  sections " ~ toString(size(patch))
                    ~ "  " ~ (sharedParameterization(curves) ? "ruled"
                        : (compatibleChains(context, chains) ? "unified" : "loft"));

                // The pair as opLoft sees it. Printed for the patches that succeed as well
                // as the one that throws, because the useful reading is the comparison: a
                // failing patch beside its near-identical neighbour that built.
                if (size(patch) == 2 && curves[0] != undefined && curves[1] != undefined)
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

            // A pair of B-spline sections sharing degree and knots -- which is what the
            // coupled fit produces -- is built directly as the B-spline surface whose
            // control net is their two control polygons. That surface IS the ruled patch
            // between them; there is nothing for a loft to work out. Every other pair
            // (lines, arcs, sections fitted separately) goes to opLoft, which builds them
            // without trouble.
            //
            // opLoft is not used for the coupled pairs because it refuses some of them.
            // Reproduced outside the feature on one pair: two 6-CP cubics, same knots, 5.8
            // mm apart, tangents agreeing, no cusp or reversal -- LOFT_FAILED. Rounding the
            // knots to 1e-5, or either curve's control points to 1 um, and the same pair
            // lofts; shifting the knots by 1e-9 and it still fails. A kernel path with no
            // outside characterisation, and no reason to go near it when the surface can be
            // written down. See correction 22.
            var built = ruledPatch(context, patchId, curves)
                || unifiedPatch(context, patchId, chains);
            var loftError = undefined;

            if (!built)
            {
                // One refusal costs one face, not the surface. The hole marks exactly
                // where the kernel objected, and the refusal is reported, not swallowed.
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
            }

            // Neither op returns anything, so the body it should have made is the only
            // honest test of whether it ran.
            if (size(evaluateQuery(context, qCreatedBy(patchId, EntityType.BODY))) == 0)
            {
                refused = append(refused, toString(r) ~ ":" ~ toString(members));

                // The refusal is the trigger, not a toggle: this is the one patch worth
                // seeing in full, and it is only ever a handful of curves.
                if (reportsRun(definition, r))
                {
                    println("[refused]   run " ~ toString(r) ~ "  profiles " ~ toString(members)
                        ~ "  by " ~ (built ? "opCreateBSplineSurface" : "opLoft")
                        ~ "  error " ~ toString(loftError));

                    for (var k = 0; k < size(patch); k += 1)
                    {
                        printCurveDump(context, "  profile " ~ toString(members[k]), patch[k]);
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
            ~ " patches, so the surface has a hole at run:profiles " ~ join(refused, ", ")
            ~ ". Every other patch built.");
    }

    if (definition.debugPrintSurface)
    {
        println("[surface]     " ~ toString(size(sheets)) ~ " patch loft(s) over "
            ~ toString(size(sections[0].pieces)) ~ " run(s), " ~ toString(size(sections)) ~ " profile(s)"
            ~ (skipped > 0 ? ", " ~ toString(skipped) ~ " run(s) reached by fewer than two profiles" : "")
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

        // Which of the shared cells this offset reaches, so a hole in the loft can be read
        // straight off the plan: "-" is a cell another offset covers and this one does not.
        var covered = "";
        for (var cell in offset.cells)
        {
            covered = covered ~ (cell == undefined ? "-" : "#");
        }

        println("[surface]   offset " ~ toString(i) ~ " '" ~ offset.name ~ "': "
            ~ toString(size(offset.runs)) ~ " run(s) over " ~ toString(size(offset.cells))
            ~ " cell(s) [" ~ covered ~ "] -> "
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
/**
 * Whether every piece of every chain is a curve the exact construction can take: a line, or
 * a non-rational, non-periodic B-spline. Arcs are rational and go to the loft.
 */
function compatibleChains(context is Context, chains is array) returns boolean
{
    if (size(chains) != 2)
    {
        return false;
    }

    for (var chain in chains)
    {
        for (var piece in chain)
        {
            const edges = evaluateQuery(context, piece);
            if (size(edges) != 1)
            {
                return false;
            }

            const curve = evCurveDefinition(context, { "edge" : edges[0] });
            if (!(curve is Line) && !(curve is BSplineCurve && curve.weights == undefined && !curve.isPeriodic))
            {
                return false;
            }
        }
    }

    return true;
}

/**
 * The ruled patch between two members whose pieces do NOT share a parameterization -- a
 * terminal piece with its overhang chained on, a line against a fit, two fits with their own
 * knots -- built exactly, by changing representation and never geometry:
 *
 *   1. every piece as a B-spline (a line is a degree-1 one), oriented to run along the
 *      edges, the two members running the same way;
 *   2. every piece raised to the highest degree present;
 *   3. each member's pieces joined into one curve over [0, 1], each piece owning its share
 *      of the range by arc length -- the one CHOICE in the construction, and the same one a
 *      loft makes: matching fractions of length face each other across the patch;
 *   4. each curve's knots inserted into the other, so both share one knot vector;
 *   5. the surface whose control net is the two control polygons.
 *
 * opLoft was doing this by fitting: 38 to 80 control points across a 48 mm patch, and the
 * kink where the overhang joins smeared into a curvature ripple. Here the joint stays what it
 * is, a crease, and the net is ten or so points.
 *
 * @returns {boolean} : false when a piece cannot be taken, and nothing was built.
 */
function unifiedPatch(context is Context, id is Id, chains is array) returns boolean
{
    if (!compatibleChains(context, chains))
    {
        return false;
    }

    var members = [];
    var degree = 1;

    for (var chain in chains)
    {
        var pieces = [];
        var lengths = [];

        for (var piece in chain)
        {
            const edge = evaluateQuery(context, piece)[0];
            const curve = evCurveDefinition(context, { "edge" : edge });

            if (curve is Line)
            {
                const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
                pieces = append(pieces, lineAsBSpline(ends[0].origin, ends[1].origin));
            }
            else
            {
                pieces = append(pieces, curve);
                degree = max(degree, curve.degree);
            }

            lengths = append(lengths, evLength(context, { "entities" : edge }));
        }

        members = append(members, { "pieces" : orientChain(pieces), "lengths" : lengths });
    }

    // Both members running the same way, or the rulings cross.
    if (dot(chainChord(members[0].pieces), chainChord(members[1].pieces)) < 0)
    {
        var flipped = [];
        for (var k = size(members[1].pieces) - 1; k >= 0; k -= 1)
        {
            flipped = append(flipped, reverseBSpline(members[1].pieces[k]));
        }
        members[1] = { "pieces" : flipped, "lengths" : reverse(members[1].lengths) };
    }

    var curves = [];
    for (var member in members)
    {
        var raised = [];
        for (var piece in member.pieces)
        {
            raised = append(raised, elevateToDegree(piece, degree));
        }
        curves = append(curves, joinChain(raised, member.lengths));
    }

    curves = unifyKnots(curves);

    // A joint in either chain is a crease, and the surface may not carry one: one surface
    // per stretch between creases, meeting as faces. Both curves share the knot vector, so
    // they split at the same places into the same number of pieces.
    const piecesA = splitAtCreases(curves[0]);
    const piecesB = splitAtCreases(curves[1]);

    for (var k = 0; k < size(piecesA); k += 1)
    {
        var grid = [];
        for (var i = 0; i < size(piecesA[k].controlPoints); i += 1)
        {
            grid = append(grid, [piecesA[k].controlPoints[i], piecesB[k].controlPoints[i]]);
        }

        opCreateBSplineSurface(context, id + ("seg" ~ k), {
                    "bSplineSurface" : bSplineSurface({
                            "uDegree" : degree,
                            "vDegree" : 1,
                            "isUPeriodic" : false,
                            "isVPeriodic" : false,
                            "controlPoints" : controlPointMatrix(grid),
                            "uKnots" : piecesA[k].knots,
                            "vKnots" : knotArray([0, 0, 1, 1])
                        })
                });
    }

    return true;
}

/**
 * The pieces of a chain each turned to follow the one before. The kernel hands back an
 * edge's curve in whichever direction it holds it, so every piece is checked against its
 * neighbour by endpoint distance and reversed where it runs the wrong way.
 */
function orientChain(pieces is array) returns array
{
    if (size(pieces) < 2)
    {
        return pieces;
    }

    var out = [];
    for (var k = 0; k < size(pieces); k += 1)
    {
        const piece = pieces[k];
        const start = piece.controlPoints[0];
        const end = piece.controlPoints[size(piece.controlPoints) - 1];

        var forward = true;
        if (k == 0)
        {
            const next = pieces[1];
            const nextStart = next.controlPoints[0];
            const nextEnd = next.controlPoints[size(next.controlPoints) - 1];
            const fromEnd = min(norm(end - nextStart), norm(end - nextEnd));
            const fromStart = min(norm(start - nextStart), norm(start - nextEnd));
            forward = fromEnd <= fromStart;
        }
        else
        {
            const previous = out[k - 1];
            const previousEnd = previous.controlPoints[size(previous.controlPoints) - 1];
            forward = norm(start - previousEnd) <= norm(end - previousEnd);
        }

        out = append(out, forward ? piece : reverseBSpline(piece));
    }

    return out;
}

function chainChord(pieces is array) returns Vector
{
    const last = pieces[size(pieces) - 1];
    return last.controlPoints[size(last.controlPoints) - 1] - pieces[0].controlPoints[0];
}

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
 * Whether a patch's sections are B-splines of one degree over one knot vector, so that
 * the surface between them can be written down rather than lofted.
 *
 * Exact equality on the knots, not tolerant: the coupled fit hands every section the same
 * vector bit for bit, and two vectors that merely resemble each other describe two
 * parameterizations, between which a loft is the right tool after all.
 */
function sharedParameterization(curves is array) returns boolean
{
    if (size(curves) != 2)
    {
        return false;
    }

    const a = curves[0];
    const b = curves[1];

    return a != undefined && b != undefined
        && a is BSplineCurve && b is BSplineCurve
        && a.degree == b.degree
        && !a.isPeriodic && !b.isPeriodic
        && a.weights == undefined && b.weights == undefined
        && size(a.controlPoints) == size(b.controlPoints)
        && a.knots == b.knots;
}

/**
 * Build the ruled patch between two sections that share a parameterization as the
 * B-spline surface they define: u runs along the sections with their degree and knots,
 * v runs straight across at degree 1, and the control net is the two control polygons
 * side by side. The sections are the surface's v = 0 and v = 1 boundaries exactly.
 *
 * @param curves {array} : the sections' evCurveDefinition results.
 * @returns {boolean} : false when the pair does not qualify, and nothing was built.
 */
function ruledPatch(context is Context, id is Id, curves is array) returns boolean
{
    if (!sharedParameterization(curves))
    {
        return false;
    }

    const a = curves[0];
    var b = curves[1];

    // The two polygons must run the same way, or the rulings cross. Reversing a B-spline
    // is the control points backwards and the knots mirrored about the parameter range.
    const last = size(a.controlPoints) - 1;
    const chordA = a.controlPoints[last] - a.controlPoints[0];
    const chordB = b.controlPoints[last] - b.controlPoints[0];
    if (dot(chordA, chordB) < 0)
    {
        const first = b.knots[0];
        const final = b.knots[size(b.knots) - 1];
        var knots = [];
        for (var k = size(b.knots) - 1; k >= 0; k -= 1)
        {
            knots = append(knots, first + final - b.knots[k]);
        }
        b = mergeMaps(b, { "controlPoints" : reverse(b.controlPoints), "knots" : knotArray(knots) }) as BSplineCurve;
    }

    var grid = [];
    for (var i = 0; i <= last; i += 1)
    {
        grid = append(grid, [a.controlPoints[i], b.controlPoints[i]]);
    }

    opCreateBSplineSurface(context, id, {
                "bSplineSurface" : bSplineSurface({
                        "uDegree" : a.degree,
                        "vDegree" : 1,
                        "isUPeriodic" : false,
                        "isVPeriodic" : false,
                        "controlPoints" : controlPointMatrix(grid),
                        "uKnots" : a.knots,
                        "vKnots" : knotArray([0, 0, 1, 1])
                    })
            });

    return true;
}

/**
 * How far the end tangents handed to the fit are from the direction of the points they are
 * meant to match, in degrees: the first two and last two distinct points. A fit given a
 * tangent that disagrees with its own points hooks to satisfy both.
 */
function tangentAgreement(plan is map) returns string
{
    const n = size(plan.points);
    if (n < 2)
    {
        return "";
    }

    // The first and last sample gaps, since the fitter sizes its end derivatives from them.
    var text = "  gaps " ~ toString(roundToPrecision(norm(plan.points[1] - plan.points[0]) / millimeter, 3))
        ~ "/" ~ toString(roundToPrecision(norm(plan.points[n - 1] - plan.points[n - 2]) / millimeter, 3)) ~ " mm";
    if (plan.startDerivative != undefined)
    {
        const chord = normalize(plan.points[1] - plan.points[0]);
        text = text ~ "  startDev " ~ toString(roundToPrecision(
                acos(clamp(dot(normalize(plan.startDerivative), chord), -1, 1)) / degree, 3)) ~ " deg";
    }
    if (plan.endDerivative != undefined)
    {
        const chord = normalize(plan.points[n - 1] - plan.points[n - 2]);
        text = text ~ "  endDev " ~ toString(roundToPrecision(
                acos(clamp(dot(normalize(plan.endDerivative), chord), -1, 1)) / degree, 3)) ~ " deg";
    }
    return text;
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
    gates is map, startDerivative, endDerivative) returns Query
{
    return emitShape(context, id, definition,
        classifyPoints(points, definition.approximationTolerance, gates.allowArc, gates.allowLine),
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
