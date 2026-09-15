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
            annotation { "Name" : "Loft each segment separately", "Default" : true, "Description" : "Build one loft per matching pair of segments and join them, so each face carries a surface its own size. One loft over the whole profile gives the same shape, but every face is a window onto one surface spanning the lot, and its u/v data runs far past the face." }
            definition.loftPerSegment is boolean;

            if (definition.loftPerSegment && size(definition.offsets) > 2)
            {
                annotation { "Name" : "Blend through profiles", "Default" : false, "Description" : "Fit one smooth surface through all the profiles instead of running straight from each to the next. Off gives a ruled patch per adjacent pair, with a crease at every intermediate profile. A loft of three or more profiles can only be smooth, so this is the difference between N-1 straight surfaces and one curved one." }
                definition.blendThroughProfiles is boolean;
            }
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
            annotation { "Name" : "Print surface", "Default" : false, "Description" : "Report what each offset produced and what was handed to the loft: runs, the span chosen, and the point count of every section." }
            definition.debugPrintSurface is boolean;

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

        if (definition.debugPrintSurface)
        {
            printSurfaceResult(context, id);
        }

        finishSurface(context, id, definition, driven);
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
        "loftPerSegment" : true,
        "blendThroughProfiles" : false,
        "keepWires" : false,
        "debugPrintSurface" : false,
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
                    "debugPrintAlongChain" : definition.debugPrintAlongChain && first,
                    "debugShowReference" : definition.debugShowReference && first,
                    "debugShowOffsetFrames" : definition.debugShowOffsetFrames && first,
                    "debugPrintFrameTable" : definition.debugPrintFrameTable && first
                });

        const result = offsetFromShared(context, offsetId, perOffset, shared, i);

        // The offset's own reporting lives with the feature, not the core, so driving the
        // core directly leaves every toggle in the Debug group wired to nothing unless it
        // is called here. One debug id per offset, or they would collide.
        debugOutput(context, offsetId + "debug", perOffset, result.sourceChain, result.profile,
            result.alongRef, result.stations, result.coords, result.upper, result.lower,
            result.placed, result.emitted);

        driven = append(driven, mergeMaps(result, {
                        "id" : offsetId,
                        "name" : entries[i].offsetName,
                        "wires" : qCreatedBy(offsetId, EntityType.BODY),
                        "edges" : qCreatedBy(offsetId, EntityType.EDGE)
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
function ruleFromOffset(context is Context, id is Id, definition is map, driven is map)
{
    const reach = definition.ruledDistance;
    const steps = usesReferenceFrame(definition, driven.alongRef) ? definition.ruledSections : 2;

    // One surface per stretch. A stepped offset genuinely is several surfaces, and merging
    // them would either bridge the step or drop everything past it.
    const spans = surfaceSpans(driven);

    for (var k = 0; k < size(spans); k += 1)
    {
        ruleOverSpan(context, id + ("span" ~ k), definition, driven, spans[k], reach, steps);
    }
}

/**
 * The ruled surface over one uninterrupted stretch of the offset.
 */
function ruleOverSpan(context is Context, id is Id, definition is map, driven is map,
    span is map, reach is ValueWithUnits, steps is number)
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
    // Indexed by RUN, with a hole where a run fell outside the span or carried no offset,
    // so that two sections built over the same runs can be paired run for run. Packing them
    // down would misalign the moment one section skips a run the other keeps.
    var pieces = [];
    var kept = 0;

    for (var r = 0; r < size(driven.runs); r += 1)
    {
        const run = driven.runs[r];
        const from = max(run.start, span.start);
        const to = min(run.end, span.end);

        var curve = undefined;

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
            var span = 0 * meter;
            for (var k = 1; k < size(distinct); k += 1)
            {
                span += norm(distinct[k] - distinct[k - 1]);
            }

            if (size(distinct) >= 2 && span >= OFFSET_GEOM_TOL)
            {
                curve = curveThrough(context, id + ("piece" ~ r), definition, distinct);
                kept += 1;
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
    const seed = curveThrough(context, id + "seed", definition, points);
    const offsetSide = ruledSection(context, id + "offset", definition, driven, span, 0 * meter).wire;

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
        sections = append(sections, ruledSection(context, id + ("section" ~ i), definition,
                driven[i], longestSpan(context, id, driven[i]), 0 * meter));
    }

    if (definition.loftPerSegment)
    {
        loftColumns(context, id, definition, sections);
        return;
    }

    var wires = [];
    for (var section in sections)
    {
        wires = append(wires, section.wire);
    }

    loftSections(context, id + "loft", wires);
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

            const patchId = id + ("patch" ~ r ~ "_" ~ from);
            opLoft(context, patchId, {
                        "profileSubqueries" : patch,
                        "bodyType" : ToolBodyType.SURFACE
                    });

            sheets = append(sheets, qCreatedBy(patchId, EntityType.BODY));
        }
    }

    if (size(sheets) == 0)
    {
        throw regenError("No run is covered by every offset, so there is nothing to loft between.");
    }

    if (definition.debugPrintSurface)
    {
        println("[surface]     " ~ toString(size(sheets)) ~ " patch loft(s) ("
            ~ toString(size(sections[0].pieces)) ~ " run(s) x "
            ~ toString(lastFrom + 1) ~ " profile span(s))"
            ~ (skipped > 0 ? ", " ~ toString(skipped) ~ " skipped for missing coverage" : ""));
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

/**
 * One curve through a point list, as a body a loft can take.
 */
function curveThrough(context is Context, id is Id, definition is map, points is array) returns Query
{
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

    if (!definition.debugKeepSections && !isQueryEmpty(context, scaffolding))
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
