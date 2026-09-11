FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Creates wires (or returns bSpline data) obeying special rules from either a solid body, an edge, or a chain of edges
 *
 * User/caller provides input type {WIRE, PART, EDGES} (query type)
 * User/caller provides output type {WIRE, Data} (query, map)
 * User/caller provides projection planar face
 * User/caller specifies approdimation parameters
 * User/caller specifies if output should be grouped as a single curve, a curve per input curve (exclusing curves that collapse to a point), or an "efficent representation"
 *      Efficent implies look at curvature progressions, degree, weights and control point spacing to evaluate if two curves are good candidates to be merged into a single entity. If so, output has these two curves merged. Note that we may end up wanting to merge multiple curves into a single one.
 *
 * Stop projection where an endpoint of the chain is normal to the reference plane. Note that there may be multiple edges in the chain beyond these tangent points, and we want to ignore all data beyond the first crossing (moving outward from the chain center)
 * If a PART is provided
 *      User/caller specifies what types of data should be returned
 *          Top/Bottom Profiles
 *          Middle Profile
 *          Periphery
 *          All Profiles
 *      The explicit assumption here is that our 'profile direction' here is along, or along-ish the x direction. Users/callers should, however, provide a query or a vector specifying the 'prevailing direction'
 *      We use this prevailing direction to better understand our curves
 *          Top and Bottom profiles will likely be long chains with endpoints near (though not necessarily AT) the extents of our inputs, projected ionto the planar face onto which we're projecting
 *          Top and Bottom profiles will be of similar, though not necessarily equal length
 *          Top and Bottom profiles are joined either by:
 *              A single line with a length sigificantly shorter than either chain
 *              An arc with an arclength significantly shorter than either chain
 *              A b-spline with an arclength significantly shorter than either chain
 *              Intersections between Top/Bottom profiles and the short sections that join them should roughly be strongly perpendicular, or strongly tangent
 *
 *
 */

// ============================================================================
// Status
// ============================================================================
//
// Built: the EDGES / WIRE path -- project a chain onto a planar face, trim it where it
// doubles back, and emit it grouped.
//
// Built: the PART path -- outline the body onto the face, then read the closed periphery
// against the prevailing direction to pull out Top, Bottom and Middle.
//
// One caveat to carry: opCreateOutline is marked @internal in the standard library. It does
// exactly the right thing and there is no supported equivalent, but it is not a documented
// contract. If it ever changes, the fallback is to project every edge of the body and take
// the outer boundary of the result, which is a 2D boolean we would have to write ourselves.

// ============================================================================
// Constants and bounds
// ============================================================================

/**
 * Samples taken along the projected path to find where it doubles back.
 *
 * The reversal is a cusp: the along-direction coordinate stops advancing and turns
 * round. Finding it only needs enough resolution to bracket the turn, because the
 * bracket is then refined by bisection.
 */
export const PROFILE_SCAN_SAMPLES = 400;

/** Bisection steps used to pin a reversal once a sample pair has bracketed it. */
export const PROFILE_REFINE_STEPS = 24;

/** Most comb teeth any debug view will draw. */
export const PROFILE_COMB_TEETH = 120;

/** A direction this short carries no direction. */
export const PROFILE_ZERO_DIRECTION = 1e-9;

/**
 * How square a tangent must be to the prevailing direction to count as running along it.
 *
 * The header's own cue: a top or bottom profile runs strongly tangent to the direction and
 * the pieces joining them run strongly perpendicular. Halfway between the two is the only
 * defensible place to cut, and on a real outline nothing lingers near it.
 */
export const PROFILE_ALONG_COS = 0.7071;

/** An along-run shorter than this fraction of the longest is a joining piece, not a profile. */
export const PROFILE_SHORT_RUN_FRACTION = 0.25;

export const ProfilePointsBounds = { (unitless) : [8, 60, 400] } as IntegerBoundSpec;
export const ProfileDegreeBounds = { (unitless) : [2, 3, 7] } as IntegerBoundSpec;
export const ProfileMaxCPBounds = { (unitless) : [4, 24, 100] } as IntegerBoundSpec;
export const ProfileToleranceBounds =
{
    (meter)      : [1e-8, 1e-5, 1],
    (centimeter) : 1e-3,
    (millimeter) : 1e-2,
    (inch)       : 1e-3
} as LengthBoundSpec;

export const ProfileCombScaleBounds =
{
    (meter)      : [1e-5, 0.02, 10],
    (centimeter) : 2,
    (millimeter) : 20,
    (inch)       : 0.8
} as LengthBoundSpec;

// ============================================================================
// Enums
// ============================================================================

/**
 * How the retained projection is broken into output curves.
 *
 * PER_CURVE keeps what opDropCurve produced, which is the exact projection: projecting a
 * B-spline is an affine map and B-splines are affine invariant, so the dropped edge IS
 * the projected curve, not a fit of it. Any other grouping has to refit, and trades that
 * exactness for a tidier result.
 */
export enum ProfileGrouping
{
    annotation { "Name" : "One curve" }
    SINGLE,
    annotation { "Name" : "One per input curve" }
    PER_CURVE,
    annotation { "Name" : "Efficient" }
    EFFICIENT
}

/**
 * What is being profiled.
 */
export enum ProfileSource
{
    annotation { "Name" : "Edges" }
    EDGES,
    annotation { "Name" : "Part" }
    PART
}

/**
 * Which of a part's profiles to return.
 *
 * Outlining a body onto a plane gives one closed periphery. Read against a prevailing
 * direction it separates into two long chains running ALONG that direction -- the top and
 * bottom profiles -- and short pieces running ACROSS it that join their ends. Middle is
 * derived rather than found: the average of top and bottom at each station along the
 * direction.
 *
 * ALL means all three profiles. The periphery is the undivided loop and is not a profile of
 * anything, so it stays its own choice.
 */
export enum ProfilePart
{
    annotation { "Name" : "Top" }
    TOP,
    annotation { "Name" : "Bottom" }
    BOTTOM,
    annotation { "Name" : "Middle" }
    MIDDLE,
    annotation { "Name" : "Periphery" }
    PERIPHERY,
    annotation { "Name" : "All profiles" }
    ALL
}

// ============================================================================
// Feature
// ============================================================================

annotation { "Feature Type Name" : "Evaluate profiles",
        "Feature Type Description" : "Project a chain of edges onto a planar face, trim it where it doubles back, and emit it as a clean wire" }
export const evaluateProfiles = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Profile from", "Default" : ProfileSource.EDGES, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
        definition.profileSource is ProfileSource;

        if (definition.profileSource == ProfileSource.EDGES)
        {
            annotation { "Name" : "Edges to project", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
            definition.profileEdges is Query;
        }
        else
        {
            annotation { "Name" : "Part to outline", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET), "MaxNumberOfPicks" : 1 }
            definition.profilePart is Query;

            annotation { "Name" : "Return", "Default" : ProfilePart.ALL, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
            definition.profileParts is ProfilePart;
        }

        annotation { "Name" : "Project onto", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.projectionFace is Query;

        annotation { "Name" : "Prevailing direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
        definition.prevailingDirection is Query;

        annotation { "Name" : "Flip prevailing direction", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipPrevailing is boolean;

        annotation { "Name" : "Output", "Default" : ProfileGrouping.SINGLE, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.grouping is ProfileGrouping;

        annotation { "Name" : "Name", "Description" : "Name given to the resulting wire. Clear it to leave it unnamed." }
        definition.outputName is string;

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Samples per curve", "Description" : "Points taken along each retained span before fitting" }
            isInteger(definition.samplesPerCurve, ProfilePointsBounds);

            annotation { "Name" : "Target degree" }
            isInteger(definition.fitDegree, ProfileDegreeBounds);

            annotation { "Name" : "Tolerance" }
            isLength(definition.fitTolerance, ProfileToleranceBounds);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.fitMaxCPs, ProfileMaxCPBounds);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print scan", "Default" : false, "Description" : "Report the projected length, where it was trimmed and why" }
            definition.debugPrintScan is boolean;

            annotation { "Name" : "Show curvature comb", "Default" : false }
            definition.debugShowComb is boolean;

            annotation { "Name" : "Comb scale" }
            isLength(definition.combScale, ProfileCombScaleBounds);
        }
    }
    {
        const result = projectedProfile(context, id, definition);

        if (definition.debugPrintScan)
        {
            printProfileScan(result);
        }
        if (definition.debugShowComb)
        {
            drawCurvatureComb(context, id + "comb", result.samples, definition.combScale);
        }
    });

// ============================================================================
// Reusable core
// ============================================================================

/**
 * Project a chain onto a planar face, trim it where it doubles back, and emit it.
 *
 * Exported so the other features in this document can take the projected profile without
 * going through the feature UI. The returned map carries the sampled geometry as well as
 * the queries, because a caller that wants to reason about the profile -- evaluate_offset
 * measuring against it, say -- needs the points, not just a body to select.
 *
 * @returns {map} :
 *   "curves"   {array}  - the emitted BSplineCurves, in order along the chain
 *   "samples"  {array}  - each { "point", "tangent", "curvature" } along the retained span
 *   "span"     {map}    - { "start", "end" } path parameters retained, in [0, 1]
 *   "trimmed"  {map}    - { "atStart", "atEnd" } booleans: did a reversal cut that end
 *   "length"   {ValueWithUnits} - arc length of the retained projection
 *   "profiles" {map}    - PART only: what each named profile turned out to be. Empty for EDGES.
 */
export function projectedProfile(context is Context, id is Id, definition is map) returns map
{
    const plane = evPlane(context, { "face" : definition.projectionFace });

    if (definition.profileSource == ProfileSource.PART)
    {
        return partProfiles(context, id, definition, plane);
    }

    // NORMAL_TO_TARGET on a planar face is projection along the plane normal, which is
    // what "project onto this plane" means. Naming the direction explicitly would say the
    // same thing and then have to be kept in step with the face.
    const dropId = id + "drop";
    opDropCurve(context, dropId, {
                "tools" : definition.profileEdges,
                "targets" : definition.projectionFace,
                "projectionType" : ProjectionType.NORMAL_TO_TARGET
            });

    const dropped = qCreatedBy(dropId, EntityType.EDGE);
    if (isQueryEmpty(context, dropped))
    {
        throw regenError("Nothing projected onto the face.", definition.profileEdges);
    }

    var path;
    try
    {
        path = constructPath(context, dropped);
    }
    catch (error)
    {
        throw regenError("The projected edges do not form a single connected chain.",
            definition.profileEdges);
    }

    const ends = evPathTangentLines(context, path, [0, 1]).tangentLines;
    const heading = prevailingDirection(context, definition, plane, ends[1].origin - ends[0].origin);
    const scan = scanForReversals(context, path, heading);

    const samples = sampleSpan(context, path, scan.start, scan.end, definition, plane);
    const curves = emitGrouping(context, id, definition, path, scan, samples);

    opDeleteBodies(context, id + "dropCleanup", { "entities" : qOwnerBody(dropped) });

    nameProfile(context, qCreatedBy(id + "profile", EntityType.BODY), definition.outputName);

    return {
        "curves" : curves,
        "samples" : samples,
        "span" : { "start" : scan.start, "end" : scan.end },
        "trimmed" : { "atStart" : scan.trimmedStart, "atEnd" : scan.trimmedEnd },
        "length" : scan.retainedLength,
        "profiles" : {}
    };
}

// ============================================================================
// Part outlines
// ============================================================================

/**
 * Outline a body on the face and split the periphery into the requested profiles.
 *
 * The split follows the two cues in this file's header and nothing else: a top or bottom
 * profile runs strongly TANGENT to the prevailing direction and is LONG; the pieces joining
 * them run strongly PERPENDICULAR and are SHORT. Tangent alignment finds the boundaries and
 * length then tells the profiles from the joins.
 */
function partProfiles(context is Context, id is Id, definition is map, plane is Plane) returns map
{
    const outlineId = id + "outline";
    opCreateOutline(context, outlineId, {
                "tools" : definition.profilePart,
                "target" : definition.projectionFace
            });

    // The outline comes back as sheet bodies, so the periphery is their boundary. qLoopEdges
    // seeded with faces returns the OUTER loops only, which drops any through-hole of the
    // part -- a hole is not part of the silhouette.
    const outlineFaces = qCreatedBy(outlineId, EntityType.FACE);
    if (isQueryEmpty(context, outlineFaces))
    {
        throw regenError("The part produced no outline on that face.", definition.profilePart);
    }

    const scratchId = id + "outlineWires";
    opExtractWires(context, scratchId, { "edges" : qLoopEdges(outlineFaces) });

    // A part whose silhouette falls into disjoint pieces outlines as several loops. The
    // profile is the largest of them; the rest are separate silhouettes with their own top
    // and bottom, and averaging across them would be meaningless.
    const loop = longestWire(context, qCreatedBy(scratchId, EntityType.BODY));
    const loopEdges = qOwnedByBody(loop, EntityType.EDGE);
    const path = constructPath(context, loopEdges);

    const peripheryLength = evPathLength(context, path);

    var curves = [];
    var samples = [];
    var profiles = {};

    if (definition.profileParts == ProfilePart.PERIPHERY)
    {
        // Emitted as it came. Outlining already produced the exact boundary, and refitting a
        // closed loop would only round its corners off.
        opExtractWires(context, id + "periphery", { "edges" : loopEdges });
        nameProfile(context, qCreatedBy(id + "periphery", EntityType.BODY),
            suffixedName(definition.outputName, "periphery"));
        profiles["periphery"] = peripheryLength;
    }
    else
    {
        // Only now: classifying throws when the outline will not separate, and asking for
        // the periphery alone should not be able to fail on a decomposition it never wanted.
        const heading = prevailingDirection(context, definition, plane, longestExtent(context, loopEdges, plane));
        const across = normalize(cross(plane.normal, heading));
        const named = classifyRuns(context, path, splitByAlignment(context, path, heading),
            heading, across, definition.debugPrintScan);

        const wantsTop = wantedProfile(definition, ProfilePart.TOP);
        const wantsBottom = wantedProfile(definition, ProfilePart.BOTTOM);
        const wantsMiddle = wantedProfile(definition, ProfilePart.MIDDLE);

        // Sampled once and shared: the middle is built from the same points the top and
        // bottom are fitted from, so re-sampling would cost a kernel call to reproduce them.
        const top = (wantsTop || wantsMiddle) ? samplePeriphery(context, path, named.top, definition, plane) : [];
        const bottom = (wantsBottom || wantsMiddle) ? samplePeriphery(context, path, named.bottom, definition, plane) : [];

        if (wantsTop)
        {
            curves = append(curves, emitProfile(context, id + "top", definition, top, "top"));
            samples = concatenateArrays([samples, top]);
            profiles["top"] = named.top;
        }

        if (wantsBottom)
        {
            curves = append(curves, emitProfile(context, id + "bottom", definition, bottom, "bottom"));
            samples = concatenateArrays([samples, bottom]);
            profiles["bottom"] = named.bottom;
        }

        if (wantsMiddle)
        {
            const middle = middleProfile(definition, plane, heading, top, bottom);
            if (size(middle) >= 2)
            {
                curves = append(curves, emitProfile(context, id + "middle", definition, middle, "middle"));
                samples = concatenateArrays([samples, middle]);
                profiles["middle"] = { "stations" : size(middle) };
            }
        }
    }

    // Last, because path and loopEdges both point into the scratch wire.
    opDeleteBodies(context, id + "outlineCleanup", {
                "entities" : qUnion([qCreatedBy(outlineId, EntityType.BODY),
                            qCreatedBy(scratchId, EntityType.BODY)])
            });

    return {
        "curves" : curves,
        "samples" : samples,
        "span" : { "start" : 0, "end" : 1 },
        "trimmed" : { "atStart" : false, "atEnd" : false },
        "length" : peripheryLength,
        "profiles" : profiles
    };
}

/**
 * The loop's longer in-plane axis, as a default prevailing direction.
 *
 * Measured in the face's own frame so the two extents are directly comparable. This is only
 * a guess -- it cannot tell a wide part from a tall one that happens to be drawn sideways --
 * which is why the direction input stays available and overrides it.
 */
function longestExtent(context is Context, edges is Query, plane is Plane) returns Vector
{
    const bounds = evBox3d(context, {
                "topology" : edges,
                "cSys" : planeToCSys(plane),
                "tight" : true
            });

    const extents = bounds.maxCorner - bounds.minCorner;

    return (extents[0] >= extents[1]) ? plane.x : cross(plane.normal, plane.x);
}

/**
 * Whether a profile was asked for. ALL covers the three profiles, not the periphery.
 */
function wantedProfile(definition is map, part is ProfilePart) returns boolean
{
    return definition.profileParts == part || definition.profileParts == ProfilePart.ALL;
}

/**
 * The longest of the wire bodies the outline produced.
 */
function longestWire(context is Context, wires is Query) returns Query
{
    const bodies = evaluateQuery(context, wires);
    if (size(bodies) == 0)
    {
        throw regenError("Outlining the part produced no closed loop on that face.");
    }

    var best = bodies[0];
    var bestLength = evLength(context, { "entities" : bodies[0] });

    for (var i = 1; i < size(bodies); i += 1)
    {
        const length = evLength(context, { "entities" : bodies[i] });
        if (length > bestLength)
        {
            best = bodies[i];
            bestLength = length;
        }
    }

    return best;
}

/**
 * Cut the loop wherever its tangent stops running along the prevailing direction.
 *
 * Returns spans of path parameter, each tagged with whether it runs along the heading.
 * Scanning rather than solving is right here: on a real outline the boundary between along
 * and across is usually a corner, so there is nothing smooth to bisect, and the scan already
 * resolves far finer than the shortest joining piece.
 *
 * The loop is closed, so a profile straddling the seam at parameter 0 arrives as two runs.
 * They are stitched back together at the end, which is why a run's end may exceed 1.
 */
function splitByAlignment(context is Context, path is Path, heading is Vector) returns array
{
    var parameters = [];
    for (var i = 0; i < PROFILE_SCAN_SAMPLES; i += 1)
    {
        parameters = append(parameters, i / (PROFILE_SCAN_SAMPLES - 1));
    }

    const lines = evPathTangentLines(context, path, parameters).tangentLines;

    // Absolute value, not signed: a closed loop traverses its top and its bottom in opposite
    // senses, and both are running along the heading.
    var runs = [];
    var runStart = 0;
    var runAlong = abs(dot(lines[0].direction, heading)) >= PROFILE_ALONG_COS;

    for (var i = 1; i < PROFILE_SCAN_SAMPLES; i += 1)
    {
        const along = abs(dot(lines[i].direction, heading)) >= PROFILE_ALONG_COS;
        if (along != runAlong)
        {
            runs = append(runs, { "start" : parameters[runStart], "end" : parameters[i - 1], "along" : runAlong });
            runStart = i;
            runAlong = along;
        }
    }
    runs = append(runs, { "start" : parameters[runStart], "end" : 1, "along" : runAlong });

    const last = size(runs) - 1;
    if (path.closed && last > 0 && runs[0].along == runs[last].along)
    {
        // Rejoin across the seam. The wrapped run reaches past 1 and is unwrapped at sampling
        // time; left as two it would halve a profile and could lose it to the length test.
        runs = append(subArray(runs, 1, last), {
                    "start" : runs[last].start,
                    "end" : runs[0].end + 1,
                    "along" : runs[0].along
                });
    }

    return runs;
}

/**
 * Pick the top and bottom profiles out of the runs.
 *
 * Of the runs that go along the heading, the long ones are profiles and the rest are joins.
 * That is the header's "significantly shorter", made explicit as a fraction of the longest.
 * Top against bottom is then simply which side of each other they sit on, measured across
 * the heading -- their own mean positions separate them, where the outline's centroid would
 * be thrown off by an asymmetric periphery.
 */
function classifyRuns(context is Context, path is Path, runs is array, heading is Vector,
    across is Vector, verbose is boolean) returns map
{
    var longest = 0;
    for (var run in runs)
    {
        if (run.along && run.end - run.start > longest)
        {
            longest = run.end - run.start;
        }
    }

    var measured = [];
    for (var run in runs)
    {
        if (!run.along || run.end - run.start < PROFILE_SHORT_RUN_FRACTION * longest)
        {
            continue;
        }

        const mid = evPathTangentLines(context, path,
                [wrapParameter(0.5 * (run.start + run.end))]).tangentLines[0].origin;
        measured = append(measured, mergeMaps(run, { "height" : dot(mid, across) }));
    }

    if (verbose || size(measured) < 2)
    {
        printRunSurvey(runs, heading, longest, size(measured));
    }

    if (size(measured) < 2)
    {
        throw regenError("The outline does not separate into a top and a bottom profile: "
            ~ toString(size(measured)) ~ " of " ~ toString(size(runs))
            ~ " runs qualified as a profile. The print log lists every run the scan found.");
    }

    var high = measured[0];
    var low = measured[0];
    for (var m in measured)
    {
        if (m.height > high.height)
        {
            high = m;
        }
        if (m.height < low.height)
        {
            low = m;
        }
    }

    return { "top" : high, "bottom" : low, "candidates" : measured };
}

/**
 * Every run the alignment scan found, and why each did or did not qualify.
 *
 * Printed whenever the split fails, because the throw happens long before the debug output
 * would otherwise run, and the run table is the only thing that distinguishes the three
 * ways this goes wrong: a heading pointing the wrong way (nothing reads as "along"), a
 * threshold set badly for this outline (everything reads as one or the other), or a profile
 * arriving in pieces (several short along-runs where one long one was expected).
 */
function printRunSurvey(runs is array, heading is Vector, longest is number, qualified is number)
{
    println("profile split: heading " ~ toString(roundToPrecision(heading[0], 4))
        ~ ", " ~ toString(roundToPrecision(heading[1], 4))
        ~ ", " ~ toString(roundToPrecision(heading[2], 4))
        ~ " -- " ~ toString(size(runs)) ~ " runs, " ~ toString(qualified) ~ " qualified as profiles");
    println("  a run is \"along\" when |tangent . heading| >= " ~ toString(PROFILE_ALONG_COS)
        ~ ", and a profile when its span >= " ~ toString(PROFILE_SHORT_RUN_FRACTION)
        ~ " x the longest along-run (" ~ toString(roundToPrecision(longest, 4)) ~ ")");

    for (var i = 0; i < size(runs); i += 1)
    {
        const run = runs[i];
        println("  run " ~ toString(i) ~ ": " ~ (run.along ? "along " : "across")
            ~ "  " ~ toString(roundToPrecision(run.start, 4))
            ~ " -> " ~ toString(roundToPrecision(run.end, 4))
            ~ "   span " ~ toString(roundToPrecision(run.end - run.start, 4)));
    }
}

/**
 * Sample one run of the periphery.
 *
 * Separate from sampleSpan because a run may straddle the seam and so reach past parameter
 * 1. Curvature is differenced from the tangents, as there: evPathTangentLines parameterises
 * by arc length, so dT/ds falls out of neighbouring samples for no extra kernel call.
 */
function samplePeriphery(context is Context, path is Path, run is map, definition is map,
    plane is Plane) returns array
{
    const count = definition.samplesPerCurve;
    const step = (run.end - run.start) * evPathLength(context, path) / (count - 1);

    var parameters = [];
    for (var i = 0; i < count; i += 1)
    {
        parameters = append(parameters, wrapParameter(run.start + (run.end - run.start) * i / (count - 1)));
    }

    const lines = evPathTangentLines(context, path, parameters).tangentLines;

    var samples = [];
    for (var i = 0; i < count; i += 1)
    {
        const before = lines[max(0, i - 1)].direction;
        const after = lines[min(count - 1, i + 1)].direction;
        const spanCount = min(count - 1, i + 1) - max(0, i - 1);
        const curvature = (spanCount > 0 && step > 0 * meter)
            ? norm(after - before) / (spanCount * step)
            : 0 / meter;

        samples = append(samples, {
                    "point" : lines[i].origin,
                    "tangent" : lines[i].direction,
                    "normal" : cross(plane.normal, lines[i].direction),
                    "curvature" : curvature,
                    "startsGroup" : (i == 0)
                });
    }

    return samples;
}

/**
 * Bring a seam-crossing path parameter back into range.
 *
 * A single subtraction rather than a modulo, deliberately: parameters here never reach 2,
 * and it leaves an unwrapped run's final 1.0 as 1.0. Wrapping that to 0 would send the last
 * sample of every untouched run back to the seam.
 */
function wrapParameter(parameter is number) returns number
{
    return (parameter > 1) ? parameter - 1 : parameter;
}

/**
 * The curve midway between top and bottom.
 *
 * Averaged at matching STATIONS along the heading, not at matching parameters. The two
 * profiles are different lengths and differently parameterised, so pairing by parameter
 * would pull the middle toward whichever is sampled denser. Both are single valued along the
 * heading by construction -- that is what being an along-run means -- so a station picks out
 * one point on each without ambiguity.
 *
 * Only the overlap of their two extents is covered: beyond it there is no second curve to
 * average with, and extrapolating one of them would be inventing geometry.
 */
function middleProfile(definition is map, plane is Plane, heading is Vector,
    top is array, bottom is array) returns array
{
    const count = definition.samplesPerCurve;

    const topRange = alongRange(top, heading);
    const bottomRange = alongRange(bottom, heading);
    const lo = max(topRange.lo, bottomRange.lo);
    const hi = min(topRange.hi, bottomRange.hi);

    if (hi - lo < TOLERANCE.zeroLength * meter)
    {
        println("WARNING: the top and bottom profiles do not overlap along the prevailing "
            ~ "direction, so there is no middle profile to build.");
        return [];
    }

    var points = [];
    for (var i = 0; i < count; i += 1)
    {
        const station = lo + (hi - lo) * i / (count - 1);
        points = append(points, 0.5 * (pointAtStation(top, heading, station)
                    + pointAtStation(bottom, heading, station)));
    }

    return samplesFromPoints(points, plane);
}

/**
 * Extent of a sampled profile along the prevailing direction.
 */
function alongRange(samples is array, heading is Vector) returns map
{
    var lo = dot(samples[0].point, heading);
    var hi = lo;

    for (var sample in samples)
    {
        const u = dot(sample.point, heading);
        if (u < lo)
        {
            lo = u;
        }
        if (u > hi)
        {
            hi = u;
        }
    }

    return { "lo" : lo, "hi" : hi };
}

/**
 * The point of a sampled profile at a given station along the heading.
 *
 * Linear between the bracketing samples. The profile is monotone along the heading, so a
 * forward scan finds the bracket; at these sample counts a search structure would cost more
 * than it saves.
 */
function pointAtStation(samples is array, heading is Vector, station is ValueWithUnits) returns Vector
{
    const count = size(samples);
    const ascending = dot(samples[count - 1].point, heading) >= dot(samples[0].point, heading);

    for (var i = 0; i < count - 1; i += 1)
    {
        const a = dot(samples[i].point, heading);
        const b = dot(samples[i + 1].point, heading);
        const brackets = ascending ? (station >= a && station <= b) : (station <= a && station >= b);

        if (brackets)
        {
            const span = b - a;
            const t = (abs(span) < TOLERANCE.zeroLength * meter) ? 0 : (station - a) / span;
            return samples[i].point + t * (samples[i + 1].point - samples[i].point);
        }
    }

    // Only reachable at the very ends, where rounding can put the station a hair outside.
    return ((station > dot(samples[0].point, heading)) == ascending)
        ? samples[count - 1].point
        : samples[0].point;
}

/**
 * Wrap a bare point list as samples, differencing tangents from the points.
 *
 * The middle profile is constructed rather than evaluated, so there is no path to ask for
 * its derivatives; differencing is the only source, and it is what the fit needs for its end
 * conditions.
 */
function samplesFromPoints(points is array, plane is Plane) returns array
{
    const count = size(points);
    var samples = [];

    for (var i = 0; i < count; i += 1)
    {
        const before = points[max(0, i - 1)];
        const after = points[min(count - 1, i + 1)];
        const step = after - before;
        const tangent = (norm(step) < TOLERANCE.zeroLength * meter)
            ? vector(1, 0, 0)
            : normalize(step);

        samples = append(samples, {
                    "point" : points[i],
                    "tangent" : tangent,
                    "normal" : cross(plane.normal, tangent),
                    "curvature" : 0 / meter,
                    "startsGroup" : (i == 0)
                });
    }

    return samples;
}

/**
 * Fit and emit one profile.
 */
function emitProfile(context is Context, id is Id, definition is map, samples is array,
    label is string) returns BSplineCurve
{
    var points = [];
    var chord = 0 * meter;
    for (var sample in samples)
    {
        if (size(points) > 0)
        {
            chord += norm(sample.point - points[size(points) - 1]);
        }
        points = append(points, sample.point);
    }

    const fitted = approximateSpline(context, {
                "degree" : definition.fitDegree,
                "tolerance" : definition.fitTolerance,
                "isPeriodic" : false,
                "maxControlPoints" : definition.fitMaxCPs,
                "targets" : [approximationTarget({
                                "positions" : points,
                                "startDerivative" : samples[0].tangent * chord,
                                "endDerivative" : samples[size(samples) - 1].tangent * chord
                            })]
            })[0];

    opCreateBSplineCurve(context, id, { "bSplineCurve" : fitted });
    nameProfile(context, qCreatedBy(id, EntityType.BODY), suffixedName(definition.outputName, label));

    return fitted;
}

/**
 * A profile's name. Several curves come out of one part, so they cannot all share one.
 */
function suffixedName(name is string, label is string) returns string
{
    return (name == "") ? "" : (name ~ " " ~ label);
}

/**
 * The direction the profile is understood to run along.
 *
 * Everything is stated relative to this: for a chain, a reversal is the along-heading
 * coordinate turning round; for a part, a top or bottom profile is one that runs along it.
 *
 * The fallback comes from the caller because the two cases have nothing sensible in common.
 * An open chain's own chord is the obvious guess. A closed loop's chord is zero, so it needs
 * its longest in-plane extent instead.
 */
function prevailingDirection(context is Context, definition is map, plane is Plane, fallback is Vector) returns Vector
{
    var heading = fallback;

    if (!isQueryEmpty(context, definition.prevailingDirection))
    {
        heading = evAxis(context, { "axis" : definition.prevailingDirection }).direction;
    }

    // Only the in-plane part means anything: a component along the plane normal cannot
    // distinguish advancing from doubling back, because the projection has removed it.
    heading = heading - dot(heading, plane.normal) * plane.normal;

    if (norm(heading) < PROFILE_ZERO_DIRECTION * meter)
    {
        throw regenError("The prevailing direction lies along the projection direction, so it "
            ~ "cannot say which way the profile advances. Its in-plane part measured "
            ~ toString(roundToPrecision(norm(heading) / meter, 9))
            ~ " -- picking the face you are projecting onto does this.",
            definition.prevailingDirection);
    }

    return (definition.flipPrevailing ? -1 : 1) * normalize(heading);
}

/**
 * Find where the projection doubles back, working outward from the middle.
 *
 * The cusp is where the source tangent stands normal to the plane: the projected tangent
 * vanishes there and the curve turns round. Rather than hunt for a zero tangent, which is
 * exactly where the tangent direction stops being defined, this watches the along-heading
 * COORDINATE and finds where it stops advancing -- the same event, detected on a quantity
 * that stays well behaved through it.
 *
 * Outward from the middle is the rule because a chain may fold more than once; only the
 * first fold on each side bounds the usable profile, and everything past it is discarded
 * whether or not it later turns back the right way.
 */
function scanForReversals(context is Context, path is Path, heading is Vector) returns map
{
    var parameters = [];
    for (var i = 0; i < PROFILE_SCAN_SAMPLES; i += 1)
    {
        parameters = append(parameters, i / (PROFILE_SCAN_SAMPLES - 1));
    }

    const lines = evPathTangentLines(context, path, parameters).tangentLines;

    var advance = [];
    for (var i = 0; i < PROFILE_SCAN_SAMPLES; i += 1)
    {
        advance = append(advance, dot(lines[i].direction, heading));
    }

    const middle = floor((PROFILE_SCAN_SAMPLES - 1) / 2);

    // Which way is "forward" is decided at the middle, not assumed from the heading: a
    // chain traversed against the heading is still a perfectly good profile.
    const forward = (advance[middle] >= 0) ? 1 : -1;

    var startIndex = 0;
    var trimmedStart = false;
    for (var i = middle; i > 0; i -= 1)
    {
        if (forward * advance[i - 1] <= 0)
        {
            startIndex = i - 1;
            trimmedStart = true;
            break;
        }
    }

    var endIndex = PROFILE_SCAN_SAMPLES - 1;
    var trimmedEnd = false;
    for (var i = middle; i < PROFILE_SCAN_SAMPLES - 1; i += 1)
    {
        if (forward * advance[i + 1] <= 0)
        {
            endIndex = i + 1;
            trimmedEnd = true;
            break;
        }
    }

    const start = trimmedStart
        ? refineReversal(context, path, heading, forward, parameters[startIndex], parameters[startIndex + 1])
        : 0;
    const end = trimmedEnd
        ? refineReversal(context, path, heading, forward, parameters[endIndex - 1], parameters[endIndex])
        : 1;

    return {
        "start" : start,
        "end" : end,
        "trimmedStart" : trimmedStart,
        "trimmedEnd" : trimmedEnd,
        "forward" : forward,
        "retainedLength" : (end - start) * evPathLength(context, path)
    };
}

/**
 * Bisect a bracketed reversal.
 *
 * The bracket already has the along-heading rate positive at one end and non-positive at
 * the other, so plain bisection converges without needing a derivative -- and a
 * derivative is the one thing not to ask for here, since it is going through zero.
 */
function refineReversal(context is Context, path is Path, heading is Vector, forward is number,
    lo is number, hi is number) returns number
{
    var a = lo;
    var b = hi;

    for (var i = 0; i < PROFILE_REFINE_STEPS; i += 1)
    {
        const mid = 0.5 * (a + b);
        const rate = forward * dot(evPathTangentLines(context, path, [mid]).tangentLines[0].direction, heading);

        if (rate > 0)
        {
            a = mid;
        }
        else
        {
            b = mid;
        }
    }

    // Return the side that still advances, so the retained span never includes the cusp.
    return (lo < hi) ? a : b;
}

/**
 * Sample the retained span, carrying enough per point for a caller to reason about it.
 *
 * Curvature is differenced from the tangents rather than evaluated: evPathTangentLines
 * gives an arc-length parameterisation, so dT/ds falls straight out of neighbouring
 * samples and costs no extra kernel call.
 */
function sampleSpan(context is Context, path is Path, start is number, end is number,
    definition is map, plane is Plane) returns array
{
    const count = definition.samplesPerCurve;
    const length = evPathLength(context, path);

    var parameters = [];
    for (var i = 0; i < count; i += 1)
    {
        parameters = append(parameters, start + (end - start) * i / (count - 1));
    }

    const lines = evPathTangentLines(context, path, parameters).tangentLines;
    const step = (end - start) * length / (count - 1);

    var samples = [];
    for (var i = 0; i < count; i += 1)
    {
        // One-sided at the ends, central in between.
        const before = lines[max(0, i - 1)].direction;
        const after = lines[min(count - 1, i + 1)].direction;
        const spanCount = min(count - 1, i + 1) - max(0, i - 1);
        const curvature = (spanCount > 0 && step > 0 * meter)
            ? norm(after - before) / (spanCount * step)
            : 0 / meter;

        samples = append(samples, {
                    "point" : lines[i].origin,
                    "tangent" : lines[i].direction,
                    "normal" : cross(plane.normal, lines[i].direction),
                    "curvature" : curvature
                });
    }

    return samples;
}

/**
 * Emit the retained projection, grouped as asked.
 *
 * PER_CURVE is the only grouping that does not refit. The dropped edges already ARE the
 * projection exactly, so where no trim touched them the honest thing is to keep them.
 */
function emitGrouping(context is Context, id is Id, definition is map, path is Path,
    scan is map, samples is array) returns array
{
    var points = [];
    for (var sample in samples)
    {
        points = append(points, sample.point);
    }

    // SINGLE and EFFICIENT both fit one curve through the retained span for now. EFFICIENT
    // is where the merge test described in the header goes -- comparing curvature
    // progression, degree, weights and control-point spacing across a junction to decide
    // whether two spans want to be one. Until that exists it must not silently behave like
    // PER_CURVE, so it fits as one and says so.
    const fitted = approximateSpline(context, {
                "degree" : definition.fitDegree,
                "tolerance" : definition.fitTolerance,
                "isPeriodic" : false,
                "maxControlPoints" : definition.fitMaxCPs,
                "targets" : [approximationTarget({
                                "positions" : points,
                                "startDerivative" : samples[0].tangent * scan.retainedLength,
                                "endDerivative" : samples[size(samples) - 1].tangent * scan.retainedLength
                            })]
            });

    opCreateBSplineCurve(context, id + "profile", { "bSplineCurve" : fitted[0] });

    return fitted;
}

/**
 * Name the emitted wire. An empty name is a choice, not a missing value.
 */
function nameProfile(context is Context, bodies is Query, name is string)
{
    if (name == "")
    {
        return;
    }

    setProperty(context, {
                "entities" : bodies,
                "propertyType" : PropertyType.NAME,
                "value" : name
            });
}

// ============================================================================
// Debug
// ============================================================================

/**
 * What the scan found, and what it did about it.
 */
function printProfileScan(result is map)
{
    println("projected profile: retained " ~ toString(result.length)
        ~ " over path parameters " ~ toString(roundToPrecision(result.span.start, 6))
        ~ " to " ~ toString(roundToPrecision(result.span.end, 6)));
    println("  trimmed at start: " ~ toString(result.trimmed.atStart)
        ~ ", trimmed at end: " ~ toString(result.trimmed.atEnd));
    println("  samples: " ~ toString(size(result.samples))
        ~ ", output curves: " ~ toString(size(result.curves)));

    if (!result.trimmed.atStart && !result.trimmed.atEnd)
    {
        println("  the projection never doubles back; the whole chain is usable.");
    }

    for (var key in keys(result.profiles))
    {
        println("  " ~ key ~ ": " ~ toString(result.profiles[key]));
    }
}

/**
 * Curvature comb over the retained span.
 *
 * Teeth point along the in-plane normal and scale with curvature, so a kink reads as a
 * spike and a fair curve reads as a smooth envelope. Batched into one feature: each
 * addDebugLine is otherwise a sketch and a constraint solve of its own.
 *
 * The samples may cover several profiles end to end, so the envelope is broken wherever one
 * profile stops and the next begins.
 */
function drawCurvatureComb(context is Context, id is Id, samples is array, scale is ValueWithUnits)
{
    const count = size(samples);
    if (count == 0)
    {
        return;
    }

    var peak = 0 / meter;
    for (var sample in samples)
    {
        if (sample.curvature > peak)
        {
            peak = sample.curvature;
        }
    }
    if (peak * meter < PROFILE_ZERO_DIRECTION)
    {
        return;
    }

    const stride = max(1, ceil(count / PROFILE_COMB_TEETH));

    startFeature(context, id, {});
    var previousTip = undefined;
    var drawn = 0;
    var lastIndex = -1;

    for (var i = 0; i < count; i += stride)
    {
        // Every skipped sample counts, not just the drawn one: a stride that steps over a
        // profile boundary would otherwise carry the envelope straight across it.
        for (var j = lastIndex + 1; j <= i; j += 1)
        {
            if (samples[j].startsGroup == true)
            {
                previousTip = undefined;
            }
        }
        lastIndex = i;

        const tip = samples[i].point + (scale * samples[i].curvature / peak) * samples[i].normal;

        if (norm(tip - samples[i].point) > TOLERANCE.zeroLength * meter)
        {
            opCreateBSplineCurve(context, id + ("tooth" ~ i), {
                        "bSplineCurve" : bSplineCurve({
                                    "degree" : 1,
                                    "isPeriodic" : false,
                                    "controlPoints" : [samples[i].point, tip]
                                })
                    });
            drawn += 1;
        }

        // The envelope is what makes a comb readable; the teeth alone are just hair.
        if (previousTip != undefined && norm(tip - previousTip) > TOLERANCE.zeroLength * meter)
        {
            opCreateBSplineCurve(context, id + ("env" ~ i), {
                        "bSplineCurve" : bSplineCurve({
                                    "degree" : 1,
                                    "isPeriodic" : false,
                                    "controlPoints" : [previousTip, tip]
                                })
                    });
            drawn += 1;
        }
        previousTip = tip;
    }

    if (drawn > 0)
    {
        addDebugEntities(context, qCreatedBy(id, EntityType.EDGE), DebugColor.MAGENTA);
    }
    abortFeature(context, id);
}
