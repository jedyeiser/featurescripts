FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// ProjectionType lives in its own module and common.fs does not re-export it, so it is
// out of scope on a plain common import even though geomOperations documents opDropCurve
// in terms of it.
import(path : "onshape/std/projectiontype.gen.fs", version : "3070.0");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: evaluate_profiles_icon.svg (feature icon)
IconNamespace::import(path : "aca8bd60d103ad58f786f552", version : "c0ce2898b20e07d454e41631");

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

/**
 * How far out of its own plane a run may sit and still count as an arc.
 *
 * Deliberately far tighter than the fit tolerance: radial error is fit scatter, out-of-plane
 * deviation is not. Mirrors ARC_PLANARITY_TOL in edge_offset_utils.fs; this tab imports only
 * std, so it carries its own.
 */
export const ARC_PLANARITY_TOL = 1e-7 * meter;

/** How close two curves must sit to count as the same line or the same circle. */
export const PROFILE_MERGE_TOL = 1e-6 * meter;

/** How parallel two directions must be to count as collinear -- about 0.026 degrees. */
export const PROFILE_MERGE_COS = 0.9999999;

/** Fewest points a detected line or arc must span before it is worth emitting as one. */
export const PROFILE_MIN_SEGMENT = 3;

/** Outline edges shorter than this are merged away (0 = keep every edge). */
export const ProfileMergeBounds = { (millimeter) : [0, 0.01, 10] } as LengthBoundSpec;

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
 * ALL means the three profiles and nothing else -- the sections joining them are dropped.
 * FULL keeps everything: the same three, plus each joining section as its own named body, so
 * the pieces together account for the whole periphery rather than most of it. PERIPHERY is
 * the undivided loop, which is not a profile of anything and so stays its own choice.
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
    ALL,
    annotation { "Name" : "Full" }
    FULL
}

// ============================================================================
// Feature
// ============================================================================

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Evaluate profiles",
        "Feature Type Description" : "Project a chain of edges onto a planar face, trim it where it doubles back, and emit it as a clean wire; or outline a part or surface into top, bottom and middle profiles",
        "Filter Selector" : "allparts" }
export const evaluateProfiles = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Profile from", "Default" : ProfileSource.EDGES, "UIHint" : UIHint.SHOW_LABEL }
        definition.profileSource is ProfileSource;

        annotation { "Name" : "Name", "Description" : "Name given to the result. A part yields several curves, so each takes this name with its role appended -- \"core top\", \"core bottom\". Clear it to leave them unnamed." }
        definition.outputName is string;

        if (definition.profileSource == ProfileSource.EDGES)
        {
            annotation { "Name" : "Edges to project", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
            definition.profileEdges is Query;
        }
        else
        {
            annotation { "Name" : "Part or surface to outline",
                        "Filter" : (EntityType.BODY && (BodyType.SOLID || BodyType.SHEET)) || (EntityType.FACE && BodyType.SHEET && ConstructionObject.NO && SketchObject.NO),
                        "MaxNumberOfPicks" : 1,
                        "Description" : "A part, or a surface (click any of its faces, or pick it from the Surfaces list): its silhouette on the projection face." }
            definition.profilePart is Query;

            annotation { "Name" : "Return", "Default" : ProfilePart.ALL, "UIHint" : UIHint.SHOW_LABEL }
            definition.profileParts is ProfilePart;

            annotation { "Name" : "Merge edges shorter than",
                        "Description" : "Outline edges shorter than this (slivers the part carries, which Fill and other surface features refuse) are collapsed to their midpoint, their neighbours rebuilt to meet there: lines stay lines, arcs stay arcs. Each merge is reported. 0 = keep every edge." }
            isLength(definition.mergeShorter, ProfileMergeBounds);
        }

        annotation { "Name" : "Project onto", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.projectionFace is Query;

        // Not needed for the periphery: it is emitted as outlined, never split into profiles.
        if (definition.profileSource == ProfileSource.EDGES || definition.profileParts != ProfilePart.PERIPHERY)
        {
            annotation { "Name" : "Prevailing direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.prevailingDirection is Query;

            annotation { "Name" : "Flip prevailing direction", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
            definition.flipPrevailing is boolean;
        }

        annotation { "Name" : "Output", "Default" : ProfileGrouping.SINGLE, "UIHint" : UIHint.SHOW_LABEL }
        definition.grouping is ProfileGrouping;

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

            annotation { "Name" : "Print curve detail", "Default" : false, "Description" : "Every periphery edge in a table: which profile it landed in, which way it runs, how long it is, and what it actually is -- line, arc with radius, or bspline with degree and control point count. Costs one kernel call per edge, so it is separate from the scan rather than part of it." }
            definition.debugPrintCurves is boolean;

            annotation { "Name" : "Show profiles", "Default" : false, "Description" : "Colour the result: top green, bottom red, middle blue, and the sections joining them magenta." }
            definition.debugShowProfiles is boolean;
        }
    }
    {
        if (definition.debugPrintScan || definition.debugPrintCurves)
        {
            println("");
            println("========== evaluate profiles: start ==========");
        }

        const result = projectedProfile(context, id, definition);
        publishProfiles(context, id, definition, result);

        if (definition.debugPrintScan)
        {
            printProfileScan(result);
        }
        if (definition.debugPrintScan || definition.debugPrintCurves)
        {
            println("========== evaluate profiles: end ============");
            println("");
        }
    });

/**
 * Extract variables: the standard outputs plus one key per named profile -- top, bottom,
 * middle, periphery, connectors -- each always present and empty when not asked for, and
 * the projected length.
 */
function publishProfiles(context is Context, id is Id, definition is map, result is map)
{
    const output = qCreatedBy(id, EntityType.BODY);
    const top = qCreatedBy(id + "top", EntityType.BODY);
    const bottom = qCreatedBy(id + "bottom", EntityType.BODY);
    const middle = qCreatedBy(id + "middle", EntityType.BODY);
    const periphery = qCreatedBy(id + "periphery", EntityType.BODY);
    const named = qUnion([top, bottom, middle, periphery, qCreatedBy(id + "profile", EntityType.BODY)]);
    embedStandardOutputs(context, id, {
                "output" : output,
                "outputDescription" : "The profile wires",
                "inputs" : definition.profileSource == ProfileSource.PART ? definition.profilePart : definition.profileEdges,
                "variables" : {
                    "length" : extractableVariable(result.length, "Length of the retained projection (the periphery for a part)."),
                    "trimmedStart" : extractableVariable(result.trimmed.atStart, "The projection doubled back and was cut at its start."),
                    "trimmedEnd" : extractableVariable(result.trimmed.atEnd, "The projection doubled back and was cut at its end.")
                },
                "queries" : {
                    "top" : extractableQuery(top, "Top profile wire (Part)."),
                    "bottom" : extractableQuery(bottom, "Bottom profile wire (Part)."),
                    "middle" : extractableQuery(middle, "Middle profile wire (Part)."),
                    "periphery" : extractableQuery(periphery, "Undivided periphery wire (Part, Return = Periphery)."),
                    "connectors" : extractableQuery(qSubtraction(output, named), "Sections joining top and bottom (Part, Return = Full).")
                }
            });
}

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

    if (definition.debugPrintScan)
    {
        println("[profiles] projecting onto plane: origin "
            ~ toString(roundToPrecision(plane.origin[0] / millimeter, 3)) ~ ", "
            ~ toString(roundToPrecision(plane.origin[1] / millimeter, 3)) ~ ", "
            ~ toString(roundToPrecision(plane.origin[2] / millimeter, 3))
            ~ " mm   normal " ~ headingText(plane.normal)
            ~ "   in-plane x " ~ headingText(plane.x));
    }

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
    catch
    {
        throw regenError("The projected edges do not form a single connected chain.",
            definition.profileEdges);
    }

    const ends = evPathTangentLines(context, path, [0, 1]).tangentLines;
    const chord = ends[1].origin - ends[0].origin;

    // Reduced to a direction here rather than inside prevailingDirection. The two callers
    // supply different kinds of thing -- a chain's chord carries length, a picked axis and
    // a bounding-box axis do not -- and no single tolerance can be written that means the
    // same for both. A projection that closes on itself has no chord at all, so its own
    // start tangent stands in.
    const chordDirection = (norm(chord) < TOLERANCE.zeroLength * meter)
        ? ends[0].direction
        : normalize(chord);

    const heading = prevailingDirection(context, definition, plane, chordDirection);
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
    // Every step below reports before the next one runs, so when this path dies the last
    // line printed names the step that died. Without that the only signal is a throw from
    // inside a std call, which says nothing about how far the outline got.
    const verbose = definition.debugPrintScan;

    traceStep(verbose, "outlining the part onto the face");

    // A surface picked by one of its faces stands for its whole body.
    const part = qUnion([qEntityFilter(definition.profilePart, EntityType.BODY),
                qOwnerBody(qEntityFilter(definition.profilePart, EntityType.FACE))]);
    if (!isQueryEmpty(context, qBodyType(part, BodyType.SHEET)) && surfaceIsEdgeOn(context, part, plane))
    {
        // The kernel's own failure here is a bare REGEN_ERROR.
        throw regenError("The surface is edge-on to the projection face, so its outline has no area. Project its edges instead "
            ~ "(Profile from Edges), or pick a face it is not edge-on to.", ["profilePart"], part);
    }

    const outlineId = id + "outline";
    opCreateOutline(context, outlineId, {
                "tools" : part,
                "target" : definition.projectionFace
            });

    // The outline comes back as sheet bodies, so the periphery is their boundary. qLoopEdges
    // seeded with faces returns the OUTER loops only, which drops any through-hole of the
    // part -- a hole is not part of the silhouette.
    const outlineFaces = qCreatedBy(outlineId, EntityType.FACE);
    if (isQueryEmpty(context, outlineFaces))
    {
        throw regenError("The part produced no outline on that face.", part);
    }

    if (verbose)
    {
        println("[profiles] outline produced " ~ toString(size(evaluateQuery(context, outlineFaces)))
            ~ " face(s); extracting their outer loops");
    }

    const scratchId = id + "outlineWires";
    opExtractWires(context, scratchId, { "edges" : qLoopEdges(outlineFaces) });

    // A part whose silhouette falls into disjoint pieces outlines as several loops. The
    // profile is the largest of them; the rest are separate silhouettes with their own top
    // and bottom, and averaging across them would be meaningless.
    if (verbose)
    {
        println("[profiles] loops extracted: "
            ~ toString(size(evaluateQuery(context, qCreatedBy(scratchId, EntityType.BODY))))
            ~ " wire body(s); picking the longest");
    }

    const merged = mergeShortEdges(context, id + "merge", longestWire(context, qCreatedBy(scratchId, EntityType.BODY)),
            definition.mergeShorter, plane);
    if (size(merged.merged) > 0)
    {
        var where = [];
        for (var m in merged.merged)
        {
            where = append(where, toString(roundToPrecision(m.length / millimeter, 5)) ~ " mm at (" ~ toString(roundToPrecision(m.at[0] / millimeter, 3))
                ~ ", " ~ toString(roundToPrecision(m.at[1] / millimeter, 3)) ~ ", " ~ toString(roundToPrecision(m.at[2] / millimeter, 3)) ~ ")");
        }
        reportFeatureInfo(context, id, "Merged " ~ size(merged.merged) ~ " outline edge(s) shorter than "
            ~ toString(roundToPrecision(definition.mergeShorter / millimeter, 5)) ~ " mm: " ~ join(where, "; ") ~ ".");
    }
    const loop = merged.loop;
    const loopEdges = qOwnedByBody(loop, EntityType.EDGE);

    if (verbose)
    {
        println("[profiles] longest loop has " ~ toString(size(evaluateQuery(context, loopEdges)))
            ~ " edge(s); ordering them into a path");
    }

    const path = constructPath(context, loopEdges);
    const peripheryLength = evPathLength(context, path);

    traceStep(verbose, "path built: closed = " ~ toString(path.closed)
        ~ ", length " ~ toString(peripheryLength));

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

        // Periphery is the one mode that cannot fail on the decomposition, which makes it
        // the right place to REPORT on it. Running the survey here gets the same diagnosis
        // out of a regen that succeeds, instead of only out of one that throws.
        if (verbose || definition.debugPrintCurves)
        {
            const surveyHeading = resolveHeading(context, definition, plane, loopEdges);
            const surveyEdges = peripheryEdges(context, path, surveyHeading);
            const surveyNamed = pickProfiles(groupEdges(surveyEdges, path.closed), surveyEdges,
                    upwardAcross(plane, surveyHeading), verbose);

            if (definition.debugPrintCurves)
            {
                printPeripheryCurves(context, surveyEdges, surveyNamed);
            }
        }
    }
    else
    {
        // Only now: classifying throws when the outline will not separate, and asking for
        // the periphery alone should not be able to fail on a decomposition it never wanted.
        const heading = resolveHeading(context, definition, plane, loopEdges);

        traceStep(verbose, "heading " ~ headingText(heading) ~ "; scanning the loop for runs");

        const across = upwardAcross(plane, heading);
        const edgeData = peripheryEdges(context, path, heading);

        traceStep(verbose, "read " ~ toString(size(edgeData)) ~ " periphery edges; grouping them");

        const named = pickProfiles(groupEdges(edgeData, path.closed), edgeData, across, verbose);

        traceStep(verbose, "top and bottom identified; sampling and fitting");

        const wantsTop = wantedProfile(definition, ProfilePart.TOP);
        const wantsBottom = wantedProfile(definition, ProfilePart.BOTTOM);
        const wantsMiddle = wantedProfile(definition, ProfilePart.MIDDLE);

        // Sampled once and shared: the middle is built from the same points the top and
        // bottom are fitted from, so re-sampling would cost a kernel call to reproduce them.
        const top = (wantsTop || wantsMiddle) ? sampleGroup(context, edgeData, named.top, definition, plane) : [];
        const bottom = (wantsBottom || wantsMiddle) ? sampleGroup(context, edgeData, named.bottom, definition, plane) : [];

        if (wantsTop)
        {
            curves = concatenateArrays([curves, emitFromEdges(context, id + "top", definition,
                            edgeData, named.top, top, plane, "top")]);
            samples = concatenateArrays([samples, top]);
            profiles["top"] = named.top;
        }

        if (wantsBottom)
        {
            curves = concatenateArrays([curves, emitFromEdges(context, id + "bottom", definition,
                            edgeData, named.bottom, bottom, plane, "bottom")]);
            samples = concatenateArrays([samples, bottom]);
            profiles["bottom"] = named.bottom;
        }

        profiles["connectors"] = named.connectors;
        profiles["edgeData"] = edgeData;

        // Emitted verbatim, like any other periphery edge: a joining section is outline the
        // kernel already produced, and refitting it would only round its corners off.
        if (definition.profileParts == ProfilePart.FULL)
        {
            for (var c = 0; c < size(named.connectors); c += 1)
            {
                const joinerName = "connector " ~ toString(c + 1);
                gatherIntoWire(context, id + ("connector" ~ c), definition,
                    liveEdges(edgeData, named.connectors[c]), joinerName);
                profiles[joinerName] = named.connectors[c].length;
            }
        }

        if (definition.debugPrintCurves)
        {
            printPeripheryCurves(context, edgeData, named);
        }

        if (wantsMiddle)
        {
            const middle = middleProfile(definition, plane, heading, top, bottom);
            if (size(middle) >= 2)
            {
                curves = concatenateArrays([curves, emitConstructed(context, id + "middle",
                                definition, middle, plane, "middle")]);
                samples = concatenateArrays([samples, middle]);
                profiles["middle"] = { "stations" : size(middle) };
            }
        }
    }

    if (definition.debugShowProfiles)
    {
        showProfiles(context, id, definition, path, plane, profiles);
    }

    // Last, because path and loopEdges both point into the scratch wire.
    opDeleteBodies(context, id + "outlineCleanup", {
                "entities" : qUnion([qCreatedBy(outlineId, EntityType.BODY),
                            qCreatedBy(scratchId, EntityType.BODY), qCreatedBy(id + "merge", EntityType.BODY)])
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
 * The outline loop with every edge shorter than `limit` merged away. A run of consecutive short edges collapses to
 * the midpoint between its two ends; the edge before it now ends there and the edge after it starts there. Lines
 * are rebuilt as lines and arcs as arcs (through their old middle) in a sketch on the projection plane; anything
 * else as a B-spline with its end control point moved (a clamped spline ends on its end control point).
 * The loop is unchanged when nothing is short, or when fewer than two edges would remain.
 *
 * @returns {map} : { loop (Query: the wire body to use), merged (array of { length, at }) }
 */
function mergeShortEdges(context is Context, id is Id, loop is Query, limit is ValueWithUnits, plane is Plane) returns map
{
    const unchanged = { "loop" : loop, "merged" : [] };
    if (limit <= 0 * meter)
    {
        return unchanged;
    }
    const path = constructPath(context, qOwnedByBody(loop, EntityType.EDGE));
    const n = size(path.edges);
    var isShort = makeArray(n, false);
    var lengths = makeArray(n);
    var starts = makeArray(n);
    var ends = makeArray(n);
    var shortCount = 0;
    var firstLong = -1;
    for (var i = 0; i < n; i += 1)
    {
        lengths[i] = evLength(context, { "entities" : path.edges[i] });
        isShort[i] = lengths[i] < limit;
        const lines = evEdgeTangentLines(context, { "edge" : path.edges[i], "parameters" : path.flipped[i] ? [1, 0] : [0, 1] });
        starts[i] = lines[0].origin;
        ends[i] = lines[1].origin;
        if (isShort[i])
        {
            shortCount += 1;
        }
        else if (firstLong < 0)
        {
            firstLong = i;
        }
    }
    if (shortCount == 0 || n - shortCount < 2)
    {
        return unchanged;
    }

    // Runs of short edges, scanned from just after a long edge so that on a closed loop no run wraps the scan.
    var newStart = starts;
    var newEnd = ends;
    var changed = makeArray(n, false);
    var merged = [];
    const offset = path.closed ? firstLong + 1 : 0;
    var p = 0;
    while (p < n)
    {
        if (!isShort[(offset + p) % n])
        {
            p += 1;
            continue;
        }
        var run = [];
        var runLength = 0 * meter;
        while (p < n && isShort[(offset + p) % n])
        {
            run = append(run, (offset + p) % n);
            runLength += lengths[(offset + p) % n];
            p += 1;
        }
        const first = run[0];
        const last = run[size(run) - 1];
        const before = (path.closed || first > 0) ? (first - 1 + n) % n : -1;
        const after = (path.closed || last < n - 1) ? (last + 1) % n : -1;
        const at = before < 0 ? starts[first] : (after < 0 ? ends[last] : (starts[first] + ends[last]) / 2);
        if (before >= 0)
        {
            newEnd[before] = at;
            changed[before] = true;
        }
        if (after >= 0)
        {
            newStart[after] = at;
            changed[after] = true;
        }
        merged = append(merged, { "length" : runLength, "at" : at });
    }

    // Rebuild the changed edges; keep the rest as they are.
    var kept = [];
    var sketch = undefined;
    const sketchId = id + "sketch";
    const at2d = function(pnt is Vector) returns Vector
        {
            const local = worldToPlane(plane, pnt);
            return vector(local[0], local[1]);
        };
    for (var i = 0; i < n; i += 1)
    {
        if (isShort[i])
        {
            continue;
        }
        if (!changed[i])
        {
            kept = append(kept, path.edges[i]);
            continue;
        }
        const def = evCurveDefinition(context, { "edge" : path.edges[i] });
        if (def is Line || def is Circle)
        {
            if (sketch == undefined)
            {
                sketch = newSketchOnPlane(context, sketchId, { "sketchPlane" : plane });
            }
            if (def is Line)
            {
                skLineSegment(sketch, "line" ~ i, { "start" : at2d(newStart[i]), "end" : at2d(newEnd[i]) });
            }
            else
            {
                skArc(sketch, "arc" ~ i, { "start" : at2d(newStart[i]),
                            "mid" : at2d(evEdgeTangentLine(context, { "edge" : path.edges[i], "parameter" : 0.5 }).origin),
                            "end" : at2d(newEnd[i]) });
            }
            continue;
        }
        var curve = evApproximateBSplineCurve(context, { "edge" : path.edges[i] });
        var cps = curve.controlPoints;
        const lastCp = size(cps) - 1;
        // which end of the spline is the edge's start in travel order
        const startIsFirst = norm(cps[0] - starts[i]) <= norm(cps[lastCp] - starts[i]);
        const iStart = startIsFirst ? 0 : lastCp;
        const iEnd = startIsFirst ? lastCp : 0;
        if (norm(cps[iStart] - starts[i]) > TOLERANCE.zeroLength * meter || norm(cps[iEnd] - ends[i]) > TOLERANCE.zeroLength * meter)
        {
            throw regenError("Merging a short outline edge: its neighbouring spline does not end on its control points. Set Merge edges shorter than to 0.",
                ["mergeShorter"]);
        }
        cps[iStart] = newStart[i];
        cps[iEnd] = newEnd[i];
        curve.controlPoints = cps;
        opCreateBSplineCurve(context, id + ("spline" ~ i), { "bSplineCurve" : curve });
    }
    if (sketch != undefined)
    {
        skSolve(sketch);
    }
    const rebuilt = qUnion([qOwnedByBody(qBodyType(qCreatedBy(sketchId, EntityType.BODY), BodyType.WIRE), EntityType.EDGE),
                qCreatedBy(id, EntityType.EDGE)]);
    opExtractWires(context, id + "wire", { "edges" : qUnion([qUnion(kept), qConstructionFilter(rebuilt, ConstructionObject.NO)]) });
    const wire = qCreatedBy(id + "wire", EntityType.BODY);
    opDeleteBodies(context, id + "scaffold", { "entities" : qSubtraction(qCreatedBy(id, EntityType.BODY), wire) });
    if (size(evaluateQuery(context, wire)) != 1)
    {
        throw regenError("Merging short outline edges left the loop in " ~ size(evaluateQuery(context, wire)) ~ " pieces. Set Merge edges shorter than to 0.",
            ["mergeShorter"]);
    }
    return { "loop" : wire, "merged" : merged };
}

/**
 * True when every face of a surface is edge-on to the plane -- its normal perpendicular to the plane's normal at
 * every sample -- so its outline there is a curve with no area. Sampled on a 5 x 5 grid per face; one sample
 * that faces the plane is enough to say no.
 */
function surfaceIsEdgeOn(context is Context, sheet is Query, plane is Plane) returns boolean
{
    var parameters = [];
    for (var i = 0; i < 5; i += 1)
    {
        for (var j = 0; j < 5; j += 1)
        {
            parameters = append(parameters, vector((i + 0.5) / 5, (j + 0.5) / 5));
        }
    }
    for (var face in evaluateQuery(context, qOwnedByBody(sheet, EntityType.FACE)))
    {
        for (var tangentPlane in evFaceTangentPlanes(context, { "face" : face, "parameters" : parameters }))
        {
            if (abs(dot(tangentPlane.normal, plane.normal)) > 1e-6)
            {
                return false;
            }
        }
    }
    return true;
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
    return definition.profileParts == part
        || definition.profileParts == ProfilePart.ALL
        || definition.profileParts == ProfilePart.FULL;
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
 * The in-plane axis that separates top from bottom, oriented so that top means top.
 *
 * cross(normal, heading) alone is not enough: on the Front plane, with the heading along X,
 * it points along -Z, so the higher curve gets the lower coordinate and the two profiles
 * come out labelled backwards. Agreeing with world up fixes the case that matters and
 * leaves a deterministic answer for the rest.
 */
function upwardAcross(plane is Plane, heading is Vector) returns Vector
{
    const across = normalize(cross(plane.normal, heading));

    // On a horizontal face there is no "up" in the plane, so Y then X stand in, in that
    // order -- the same order a plan view would read as up the page.
    for (var reference in [vector(0, 0, 1), vector(0, 1, 0), vector(1, 0, 0)])
    {
        const agreement = dot(across, reference);

        if (abs(agreement) > PROFILE_ALONG_COS / 2)
        {
            return (agreement < 0) ? -across : across;
        }
    }

    return across;
}

/**
 * The prevailing direction, computing the fallback only when nothing was picked.
 *
 * Lazily, because the fallback reads a bounding box in the face's frame and that is one of
 * the more fragile things here. Evaluating it eagerly as a call argument -- which is what
 * this replaced -- let a bad box break a run that had supplied a perfectly good direction.
 */
function resolveHeading(context is Context, definition is map, plane is Plane,
    loopEdges is Query) returns Vector
{
    var fallback = undefined;

    if (isQueryEmpty(context, definition.prevailingDirection))
    {
        fallback = longestExtent(context, loopEdges, plane);
    }

    return prevailingDirection(context, definition, plane, fallback);
}

/**
 * A direction, short enough to sit in an error message.
 */
function headingText(heading is Vector) returns string
{
    return "[" ~ toString(roundToPrecision(heading[0], 4))
        ~ ", " ~ toString(roundToPrecision(heading[1], 4))
        ~ ", " ~ toString(roundToPrecision(heading[2], 4)) ~ "]";
}

/**
 * One step of the part path, reported before the next one runs.
 */
function traceStep(verbose is boolean, message is string)
{
    if (verbose)
    {
        println("[profiles] " ~ message);
    }
}

/**
 * Read every edge of the periphery once.
 *
 * Edges, not sampled parameters, are the right unit for this. A ski tail is a flat face at
 * constant X, so along it every sample ties for "furthest along the heading" and an extreme
 * lands arbitrarily in the middle of the face -- which put half the tail on the top profile
 * and half on the bottom, and left no joining section to find. The tail is one edge. Treat
 * it as one and it becomes a joiner by construction.
 *
 * Direction comes from the chord rather than a tangent: a tangent read anywhere on a curved
 * edge describes that point, while the chord describes the edge, which is what is being
 * classified. Chord length stands in for arc length for the same reason and at no extra
 * cost -- a joiner is short by either measure.
 */
function peripheryEdges(context is Context, path is Path, heading is Vector) returns array
{
    var out = [];

    for (var i = 0; i < size(path.edges); i += 1)
    {
        const flipped = path.flipped[i];
        const ends = evEdgeTangentLines(context, {
                    "edge" : path.edges[i],
                    "parameters" : flipped ? [1, 0] : [0, 1]
                });

        const chord = ends[1].origin - ends[0].origin;
        const reach = norm(chord);
        const direction = (reach < TOLERANCE.zeroLength * meter)
            ? (flipped ? -ends[0].direction : ends[0].direction)
            : chord / reach;

        out = append(out, {
                    "index" : i,
                    "edge" : path.edges[i],
                    "flipped" : flipped,
                    "from" : ends[0].origin,
                    "to" : ends[1].origin,
                    "direction" : direction,
                    "length" : reach,
                    "along" : abs(dot(direction, heading)) >= PROFILE_ALONG_COS
                });
    }

    return out;
}

/**
 * Gather consecutive edges that agree into groups, closing the group across the seam.
 *
 * The seam is wherever constructPath happened to start, which is arbitrary and never
 * meaningful; a profile split across it would read as two half-length runs and could lose
 * the length test.
 */
function groupEdges(edgeData is array, closed is boolean) returns array
{
    if (size(edgeData) == 0)
    {
        return [];
    }

    var groups = [];
    var members = [0];

    for (var i = 1; i < size(edgeData); i += 1)
    {
        if (edgeData[i].along == edgeData[i - 1].along)
        {
            members = append(members, i);
            continue;
        }

        groups = append(groups, makeGroup(edgeData, members));
        members = [i];
    }
    groups = append(groups, makeGroup(edgeData, members));

    const last = size(groups) - 1;
    if (closed && last > 0 && groups[0].along == groups[last].along)
    {
        groups = append(subArray(groups, 1, last),
            makeGroup(edgeData, concatenateArrays([groups[last].members, groups[0].members])));
    }

    return groups;
}

/**
 * One group, with the chord length that decides whether it is a profile or a joiner.
 */
function makeGroup(edgeData is array, members is array) returns map
{
    var total = 0 * meter;
    for (var m in members)
    {
        total += edgeData[m].length;
    }

    return { "members" : members, "along" : edgeData[members[0]].along, "length" : total };
}

/**
 * The two longest groups running along the heading are the profiles; everything else joins.
 *
 * Nothing is discarded. A group that runs along the heading but is too short to be a profile
 * is still part of the outline, so it is reported as a joiner rather than dropped -- which
 * is what makes the magenta in the debug view trustworthy.
 */
function pickProfiles(groups is array, edgeData is array, across is Vector,
    verbose is boolean) returns map
{
    var best = undefined;
    var second = undefined;

    for (var g = 0; g < size(groups); g += 1)
    {
        if (!groups[g].along)
        {
            continue;
        }
        if (best == undefined || groups[g].length > groups[best].length)
        {
            second = best;
            best = g;
        }
        else if (second == undefined || groups[g].length > groups[second].length)
        {
            second = g;
        }
    }

    if (verbose)
    {
        printGroupSurvey(groups, edgeData, best, second);
    }

    if (second == undefined)
    {
        throw regenError("The outline does not separate into a top and a bottom profile: "
            ~ toString(size(groups)) ~ " group(s) of edges, and fewer than two of them run "
            ~ "along the prevailing direction. The print log lists every group.");
    }

    const bestHeight = groupHeight(edgeData, groups[best], across);
    const secondHeight = groupHeight(edgeData, groups[second], across);
    const topIndex = (bestHeight >= secondHeight) ? best : second;
    const bottomIndex = (bestHeight >= secondHeight) ? second : best;

    var connectors = [];
    for (var g = 0; g < size(groups); g += 1)
    {
        if (g != topIndex && g != bottomIndex)
        {
            connectors = append(connectors, groups[g]);
        }
    }

    return { "top" : groups[topIndex], "bottom" : groups[bottomIndex], "connectors" : connectors };
}

/**
 * Mean position of a group across the heading, which is what tells top from bottom.
 */
function groupHeight(edgeData is array, group is map, across is Vector) returns ValueWithUnits
{
    var total = 0 * meter;

    for (var m in group.members)
    {
        total += 0.5 * (dot(edgeData[m].from, across) + dot(edgeData[m].to, across));
    }

    return total / size(group.members);
}

/**
 * Sample a group of edges end to end.
 *
 * Each edge is sampled in its own arc-length parameter and the results concatenated, so
 * there is no path parameter to map and no seam to wrap around. Points are shared at the
 * joins, so the duplicate is dropped as it appears.
 */
function sampleGroup(context is Context, edgeData is array, group is map, definition is map,
    plane is Plane) returns array
{
    const budget = definition.samplesPerCurve;

    var points = [];
    var tangents = [];

    for (var m in group.members)
    {
        const data = edgeData[m];
        const share = (group.length > TOLERANCE.zeroLength * meter)
            ? ceil(budget * data.length / group.length)
            : 2;
        const count = max(2, share);

        var parameters = [];
        for (var i = 0; i < count; i += 1)
        {
            const t = i / (count - 1);
            parameters = append(parameters, data.flipped ? 1 - t : t);
        }

        const sampled = evEdgeTangentLines(context, { "edge" : data.edge, "parameters" : parameters });

        for (var i = 0; i < count; i += 1)
        {
            const at = sampled[i].origin;

            if (size(points) > 0 && norm(at - points[size(points) - 1]) < TOLERANCE.zeroLength * meter)
            {
                continue;
            }

            points = append(points, at);
            tangents = append(tangents, data.flipped ? -sampled[i].direction : sampled[i].direction);
        }
    }

    var samples = [];
    for (var i = 0; i < size(points); i += 1)
    {
        samples = append(samples, {
                    "point" : points[i],
                    "tangent" : tangents[i],
                    "normal" : cross(plane.normal, tangents[i]),
                    "curvature" : 0 / meter,
                    "startsGroup" : (i == 0)
                });
    }

    return samples;
}

/**
 * Every group the scan found, and which two became the profiles.
 */
function printGroupSurvey(groups is array, edgeData is array, best, second)
{
    println("[profiles] edge groups: " ~ toString(size(groups))
        ~ " (a group runs \"along\" when |chord . heading| >= " ~ toString(PROFILE_ALONG_COS) ~ ")");

    for (var g = 0; g < size(groups); g += 1)
    {
        var role = "joiner";
        if (g == best)
        {
            role = "PROFILE (longest)";
        }
        else if (g == second)
        {
            role = "PROFILE (second)";
        }

        println("[profiles]   group " ~ toString(g) ~ ": "
            ~ (groups[g].along ? "along " : "across")
            ~ "  " ~ toString(size(groups[g].members)) ~ " edge(s)"
            ~ "  " ~ fmtLength(groups[g].length) ~ " mm   " ~ role);
    }
}

/**
 * What every periphery edge is, and which profile it ended up in.
 *
 * The whole point of always building the periphery first is that this table exists: if the
 * decomposition puts an edge somewhere surprising, the edge itself is right here to look at.
 */
function printPeripheryCurves(context is Context, edgeData is array, named is map)
{
    var role = {};
    for (var m in named.top.members)
    {
        role[m] = "top";
    }
    for (var m in named.bottom.members)
    {
        role[m] = "bottom";
    }
    for (var connector in named.connectors)
    {
        for (var m in connector.members)
        {
            role[m] = "joiner";
        }
    }

    println("[profiles] periphery edges:");

    for (var i = 0; i < size(edgeData); i += 1)
    {
        const data = edgeData[i];
        println("[profiles]   " ~ padLeft(toString(i), 3) ~ "  "
            ~ padLeft((role[i] == undefined) ? "-" : role[i], 7)
            ~ "  " ~ (data.along ? "along " : "across")
            ~ "  " ~ fmtLength(data.length) ~ " mm"
            ~ "  " ~ describeCurve(context, data.edge));
    }
}

/**
 * A curve, named by what it actually is.
 */
function describeCurve(context is Context, edge is Query) returns string
{
    const definition = evCurveDefinition(context, { "edge" : edge });

    if (definition is Line)
    {
        return "line";
    }
    if (definition is Circle)
    {
        return "arc R=" ~ fmtLength(definition.radius) ~ " mm";
    }
    if (definition is Ellipse)
    {
        return "ellipse";
    }
    if (definition is BSplineCurve)
    {
        return "bspline degree " ~ toString(definition.degree)
            ~ ", " ~ toString(size(definition.controlPoints)) ~ " control points"
            ~ (definition.weights == undefined ? "" : ", rational");
    }

    return "other";
}

/**
 * Right-align a short string. This tab is standalone, so it carries its own.
 */
function padLeft(value is string, width is number) returns string
{
    var out = value;

    while (length(out) < width)
    {
        out = " " ~ out;
    }

    return out;
}

/**
 * A length in millimetres, three decimals, for the log.
 */
function fmtLength(value is ValueWithUnits) returns string
{
    return toString(roundToPrecision(value / millimeter, 3));
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

// ============================================================================
// Grouping
// ============================================================================

/**
 * Emit a profile that came from real edges, as the chosen grouping asks.
 *
 * The three modes trade exactness against tidiness, and the trade is real. A single fit
 * through a profile that is mostly straight has to oscillate: the straight part carries no
 * curvature to spend, so the solver distributes its error there. No tolerance or control
 * point budget fixes that -- a straight run wants to be a line.
 */
function emitFromEdges(context is Context, id is Id, definition is map, edgeData is array,
    group is map, samples is array, plane is Plane, label is string) returns array
{
    var curves = [];
    var pieces = [];

    if (definition.grouping == ProfileGrouping.PER_CURVE)
    {
        pieces = liveEdges(edgeData, group);
    }
    else if (definition.grouping == ProfileGrouping.EFFICIENT)
    {
        const built = buildMergedRuns(context, id, definition, edgeData, group, plane);
        curves = built.curves;
        pieces = built.pieces;
    }
    else
    {
        curves = append(curves, fitSamples(context, id + "fit", definition, samples));
        pieces = [qCreatedBy(id + "fit", EntityType.EDGE)];
    }

    gatherIntoWire(context, id, definition, pieces, label);

    return curves;
}

/**
 * Collect whatever a profile was built from into exactly one wire body.
 *
 * The grouping decides how many CURVES a profile is made of, never how many bodies it comes
 * out as: a profile is one thing, and splitting it across bodies makes it harder to select
 * and harder to use downstream. So every mode ends here, and the pieces that fed the extract
 * are deleted behind it.
 */
function gatherIntoWire(context is Context, id is Id, definition is map, pieces is array,
    label is string)
{
    if (size(pieces) == 0)
    {
        return;
    }

    const wireId = id + "wire";
    opExtractWires(context, wireId, { "edges" : qUnion(pieces) });

    // Anything built under this profile that is not the wire was scaffolding for it. The
    // extract took independent copies, so the originals have no further use. Edges read
    // straight off the outline are not caught here -- they are not created under this id,
    // and the outline is cleaned up separately.
    const scaffolding = qSubtraction(qCreatedBy(id, EntityType.BODY),
            qCreatedBy(wireId, EntityType.BODY));

    if (!isQueryEmpty(context, scaffolding))
    {
        opDeleteBodies(context, id + "scaffolding", { "entities" : scaffolding });
    }

    nameProfile(context, qCreatedBy(wireId, EntityType.BODY),
        suffixedName(definition.outputName, label));
}

/**
 * A profile's edges, minus any that collapsed to a point.
 *
 * The header asks for those to be excluded: they carry no shape, and extracting one would
 * only produce a degenerate edge in the result.
 */
function liveEdges(edgeData is array, group is map) returns array
{
    var kept = [];

    for (var m in group.members)
    {
        if (edgeData[m].length > TOLERANCE.zeroLength * meter)
        {
            kept = append(kept, edgeData[m].edge);
        }
    }

    return kept;
}

/**
 * Merge what genuinely belongs together, and emit the rest as it stands.
 *
 * The merge rule is deliberately narrow. Lines merge only with collinear lines and arcs only
 * with co-circular arcs, so both stay exact; everything else merges across tangent-continuous
 * junctions and is refitted. A line is NOT absorbed into a curved neighbour even when the
 * junction is smooth -- doing so would throw away the one representation that cannot ring,
 * which is the whole reason this mode exists. A G0 corner never merges either: a real corner
 * is information, and rounding it off is a silent lie.
 */
function buildMergedRuns(context is Context, id is Id, definition is map, edgeData is array,
    group is map, plane is Plane) returns map
{
    const runs = mergeRuns(context, edgeData, group);
    var curves = [];
    var pieces = [];

    for (var r = 0; r < size(runs); r += 1)
    {
        const run = runs[r];
        const runId = id + ("run" ~ r);

        if (size(run.members) == 1 || run.kind == "arc")
        {
            // One edge, or a co-circular pair we would only be rebuilding: passing the
            // original through is exact, and reconstructing it is not.
            pieces = concatenateArrays([pieces, liveEdges(edgeData, makeGroup(edgeData, run.members))]);
        }
        else if (run.kind == "line")
        {
            const first = edgeData[run.members[0]];
            const last = edgeData[run.members[size(run.members) - 1]];
            curves = append(curves, straightCurve(context, runId, first.from, last.to));
            pieces = append(pieces, qCreatedBy(runId, EntityType.EDGE));
        }
        else
        {
            curves = append(curves, fitSamples(context, runId, definition,
                    sampleGroup(context, edgeData, makeGroup(edgeData, run.members), definition, plane)));
            pieces = append(pieces, qCreatedBy(runId, EntityType.EDGE));
        }
    }

    return { "curves" : curves, "pieces" : pieces };
}

/**
 * Group a profile's edges into runs that can each become one curve.
 */
function mergeRuns(context is Context, edgeData is array, group is map) returns array
{
    var runs = [];
    var members = [];
    var kind = "freeform";
    var anchor = undefined;

    for (var m in group.members)
    {
        const shape = evCurveDefinition(context, { "edge" : edgeData[m].edge });
        const thisKind = (shape is Line) ? "line" : ((shape is Circle) ? "arc" : "freeform");

        var joins = false;

        if (size(members) > 0)
        {
            if (kind == "line" && thisKind == "line")
            {
                joins = sameLine(anchor, shape);
            }
            else if (kind == "arc" && thisKind == "arc")
            {
                joins = sameCircle(anchor, shape);
            }
            else if (kind == "freeform" && thisKind == "freeform")
            {
                joins = smoothAcross(edgeData, members[size(members) - 1], m);
            }
        }

        if (size(members) == 0 || joins)
        {
            members = append(members, m);
            if (size(members) == 1)
            {
                kind = thisKind;
                anchor = shape;
            }
            continue;
        }

        runs = append(runs, { "members" : members, "kind" : kind });
        members = [m];
        kind = thisKind;
        anchor = shape;
    }

    if (size(members) > 0)
    {
        runs = append(runs, { "members" : members, "kind" : kind });
    }

    return runs;
}

/**
 * Whether two consecutive edges meet smoothly enough to be one curve.
 */
function smoothAcross(edgeData is array, before is number, after is number) returns boolean
{
    return dot(edgeData[before].direction, edgeData[after].direction) >= PROFILE_ALONG_COS;
}

/**
 * Whether two lines lie on the same infinite line.
 */
function sameLine(a, b) returns boolean
{
    if (!(a is Line) || !(b is Line))
    {
        return false;
    }

    if (abs(dot(a.direction, b.direction)) < PROFILE_MERGE_COS)
    {
        return false;
    }

    const offset = b.origin - a.origin;

    return norm(offset - dot(offset, a.direction) * a.direction) < PROFILE_MERGE_TOL;
}

/**
 * Whether two arcs lie on the same circle.
 */
function sameCircle(a, b) returns boolean
{
    if (!(a is Circle) || !(b is Circle))
    {
        return false;
    }

    return abs(a.radius - b.radius) < PROFILE_MERGE_TOL
        && norm(a.coordSystem.origin - b.coordSystem.origin) < PROFILE_MERGE_TOL
        && abs(dot(a.coordSystem.zAxis, b.coordSystem.zAxis)) >= PROFILE_MERGE_COS;
}

/**
 * Emit a profile that has no source edges, as the chosen grouping asks.
 *
 * The middle is built rather than read -- it is the average of top and bottom at matching
 * stations -- so there are no edges to copy or merge. Both non-single modes therefore fall
 * back on reading the shape out of the points: a straight stretch becomes a line, a circular
 * one an arc, and the rest is fitted. That matters here for the same reason it matters on the
 * bottom profile, because the middle of a ski is straight over exactly the same span.
 */
function emitConstructed(context is Context, id is Id, definition is map, samples is array,
    plane is Plane, label is string) returns array
{
    var curves = [];
    var pieces = [];

    if (definition.grouping == ProfileGrouping.SINGLE)
    {
        curves = append(curves, fitSamples(context, id + "fit", definition, samples));
        pieces = [qCreatedBy(id + "fit", EntityType.EDGE)];
    }
    else
    {
        var points = [];
        for (var sample in samples)
        {
            points = append(points, sample.point);
        }

        const runs = segmentPoints(points, definition.fitTolerance);

        for (var r = 0; r < size(runs); r += 1)
        {
            const run = runs[r];
            const runId = id + ("seg" ~ r);

            if (run.kind == "line")
            {
                curves = append(curves, straightCurve(context, runId, points[run.from], points[run.to]));
            }
            else if (run.kind == "arc")
            {
                arcThroughPoints(context, runId, points, run);
            }
            else
            {
                curves = append(curves, fitSamples(context, runId, definition,
                        subArray(samples, run.from, run.to + 1)));
            }

            pieces = append(pieces, qCreatedBy(runId, EntityType.EDGE));
        }
    }

    gatherIntoWire(context, id, definition, pieces, label);

    return curves;
}

/**
 * Read a point list as a sequence of straight, circular and freeform stretches.
 *
 * Greedy and longest-first at each position: a line is tried, then an arc, and whichever
 * reaches further wins. Points that support neither accumulate into a freeform stretch until
 * one becomes available again, so a curve that is straight in the middle and shaped at both
 * ends comes apart exactly where it should.
 */
function segmentPoints(points is array, tolerance is ValueWithUnits) returns array
{
    const n = size(points);
    var runs = [];
    var from = 0;
    var freeformStart = undefined;

    while (from < n - 1)
    {
        const lineEnd = extendLine(points, from, tolerance);
        const arcEnd = extendArc(points, from, tolerance);
        const best = max(lineEnd, arcEnd);

        if (best - from >= PROFILE_MIN_SEGMENT)
        {
            if (freeformStart != undefined)
            {
                runs = append(runs, { "from" : freeformStart, "to" : from, "kind" : "freeform" });
                freeformStart = undefined;
            }

            runs = append(runs, {
                        "from" : from,
                        "to" : best,
                        "kind" : (lineEnd >= arcEnd) ? "line" : "arc"
                    });
            from = best;
            continue;
        }

        if (freeformStart == undefined)
        {
            freeformStart = from;
        }
        from += 1;
    }

    if (freeformStart != undefined)
    {
        runs = append(runs, { "from" : freeformStart, "to" : n - 1, "kind" : "freeform" });
    }

    return runs;
}

/**
 * Furthest index whose points all still lie on the line from `from`.
 */
function extendLine(points is array, from is number, tolerance is ValueWithUnits) returns number
{
    var best = from + 1;
    var to = from + 2;

    while (to < size(points) && onLine(points, from, to, tolerance))
    {
        best = to;
        to += 1;
    }

    return best;
}

function onLine(points is array, from is number, to is number, tolerance is ValueWithUnits) returns boolean
{
    const axis = points[to] - points[from];
    const reach = norm(axis);

    if (reach < tolerance)
    {
        return false;
    }

    const direction = axis / reach;

    for (var i = from + 1; i < to; i += 1)
    {
        const offset = points[i] - points[from];

        if (norm(offset - dot(offset, direction) * direction) > tolerance)
        {
            return false;
        }
    }

    return true;
}

/**
 * Furthest index whose points all still lie on the circle through `from`, its midpoint and it.
 */
function extendArc(points is array, from is number, tolerance is ValueWithUnits) returns number
{
    var best = from + 2;
    var to = from + 3;

    while (to < size(points) && onArc(points, from, to, tolerance))
    {
        best = to;
        to += 1;
    }

    return (best > from + 2) ? best : from + 1;
}

/**
 * Circle through three points, or undefined when they are collinear.
 */
function circleThroughPoints(p0 is Vector, p1 is Vector, p2 is Vector)
{
    const a = p1 - p0;
    const b = p2 - p0;
    const axb = cross(a, b);

    if (norm(axb) < PROFILE_MERGE_TOL * norm(a))
    {
        return undefined;
    }

    const toCenter = (dot(a, a) * cross(b, axb) + dot(b, b) * cross(axb, a)) / (2 * dot(axb, axb));

    return {
        "center" : p0 + toCenter,
        "radius" : norm(toCenter),
        "normal" : normalize(axb)
    };
}

function onArc(points is array, from is number, to is number, tolerance is ValueWithUnits) returns boolean
{
    const circleData = circleThroughPoints(points[from], points[floor((from + to) / 2)], points[to]);

    if (circleData == undefined)
    {
        return false;
    }

    // Planarity and radial fit are asked separately, on separate budgets. A circle is planar
    // by definition, so a real arc's points are planar to numerical noise; a systematic bow
    // means the run is not an arc however small the bow is. Measured as one combined distance
    // -- which is what this did -- a curve wrapped onto a gently curved surface reads as
    // circular, because the bow hides inside the radial allowance.
    for (var i = from + 1; i < to; i += 1)
    {
        const toPoint = points[i] - circleData.center;
        const outOfPlane = abs(dot(toPoint, circleData.normal));
        const radial = abs(norm(toPoint - dot(toPoint, circleData.normal) * circleData.normal)
                - circleData.radius);

        if (radial > tolerance || outOfPlane > ARC_PLANARITY_TOL)
        {
            return false;
        }
    }

    return true;
}


/**
 * A true arc through a detected circular stretch, built on a sketch so it is a real arc
 * rather than a spline that resembles one.
 */
function arcThroughPoints(context is Context, id is Id, points is array, run is map)
{
    const middle = floor((run.from + run.to) / 2);
    const circleData = circleThroughPoints(points[run.from], points[middle], points[run.to]);

    if (circleData == undefined)
    {
        return;
    }

    const sketchId = id + "arcSketch";
    const sketchPl = plane(circleData.center, circleData.normal,
            normalize(points[run.from] - circleData.center));
    const sk = newSketchOnPlane(context, sketchId, { "sketchPlane" : sketchPl });

    skArc(sk, "arc", {
                "start" : worldToPlane(sketchPl, points[run.from]),
                "mid" : worldToPlane(sketchPl, points[middle]),
                "end" : worldToPlane(sketchPl, points[run.to])
            });
    skSolve(sk);

    opExtractWires(context, id + "wire", { "edges" : qCreatedBy(sketchId, EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(sketchId, EntityType.BODY) });
}

/**
 * An exact straight curve between two points.
 */
function straightCurve(context is Context, id is Id, from is Vector, to is Vector) returns BSplineCurve
{
    const curve = bSplineCurve({
                "degree" : 1,
                "isPeriodic" : false,
                "controlPoints" : [from, to]
            });

    opCreateBSplineCurve(context, id, { "bSplineCurve" : curve });

    return curve;
}

/**
 * Fit and emit one profile.
 */
function fitSamples(context is Context, id is Id, definition is map, samples is array) returns BSplineCurve
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
function prevailingDirection(context is Context, definition is map, plane is Plane, fallback) returns Vector
{
    var heading = fallback;

    if (!isQueryEmpty(context, definition.prevailingDirection))
    {
        // extractDirection, not evAxis: the filter admits planar faces and mate connectors
        // as well as axes, and evAxis alone throws on a face.
        heading = extractDirection(context, definition.prevailingDirection);

        if (heading == undefined)
        {
            throw regenError("That selection does not define a direction.",
                definition.prevailingDirection);
        }
    }

    if (definition.debugPrintScan)
    {
        println("[profiles] prevailing direction " ~ headingText(heading) ~ " ("
            ~ (isQueryEmpty(context, definition.prevailingDirection)
                ? "no selection, taken from the outline's longest in-plane extent"
                : "from the selected entity")
            ~ (definition.flipPrevailing ? ", flipped)" : ")"));
    }

    // Only the in-plane part means anything: a component along the plane normal cannot
    // distinguish advancing from doubling back, because the projection has removed it.
    heading = heading - dot(heading, plane.normal) * plane.normal;

    // Unitless throughout: every caller now hands in a direction, never a displacement.
    if (norm(heading) < PROFILE_ZERO_DIRECTION)
    {
        throw regenError("The prevailing direction lies along the projection direction, so it "
            ~ "cannot say which way the profile advances. Its in-plane part measured "
            ~ toString(roundToPrecision(norm(heading), 9))
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

    // Both ends refined in one interleaved walk. The bisections are sequential and cannot be
    // collapsed, but they are independent of each other and share path, heading and forward,
    // so the two midpoints can go to the kernel together -- 48 single-parameter calls become
    // 24 two-parameter ones whenever both ends are trimmed, which is the usual case.
    const refined = refineReversals(context, path, heading, forward,
        trimmedStart ? [parameters[startIndex], parameters[startIndex + 1]] : undefined,
        trimmedEnd ? [parameters[endIndex - 1], parameters[endIndex]] : undefined);

    const start = trimmedStart ? refined[0] : 0;
    const end = trimmedEnd ? refined[1] : 1;

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
function refineReversals(context is Context, path is Path, heading is Vector, forward is number,
    startBracket, endBracket) returns array
{
    var a = [startBracket == undefined ? 0 : startBracket[0], endBracket == undefined ? 0 : endBracket[0]];
    var b = [startBracket == undefined ? 0 : startBracket[1], endBracket == undefined ? 0 : endBracket[1]];

    var live = [];
    if (startBracket != undefined) { live = append(live, 0); }
    if (endBracket != undefined) { live = append(live, 1); }

    for (var i = 0; i < PROFILE_REFINE_STEPS; i += 1)
    {
        var mids = [];
        for (var e in live)
        {
            mids = append(mids, 0.5 * (a[e] + b[e]));
        }

        if (size(mids) == 0)
        {
            break;
        }

        const lines = evPathTangentLines(context, path, mids).tangentLines;

        for (var k = 0; k < size(live); k += 1)
        {
            const e = live[k];
            const rate = forward * dot(lines[k].direction, heading);

            if (rate > 0)
            {
                a[e] = mids[k];
            }
            else
            {
                b[e] = mids[k];
            }
        }
    }

    // Return the side that still advances, so the retained span never includes the cusp.
    return [
            (startBracket == undefined || startBracket[0] < startBracket[1]) ? a[0] : b[0],
            (endBracket == undefined || endBracket[0] < endBracket[1]) ? a[1] : b[1]
        ];
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
 * Colour the result so the decomposition can be read at a glance.
 *
 * Top green, bottom red, middle blue, and the sections joining them magenta. The three
 * profiles are already bodies, so they only need colouring; the joiners are not emitted as
 * output, so they are drawn here as throwaway polylines and aborted with the rest.
 *
 * A joiner that comes back empty is not a fault -- it means that end of the outline turns
 * through a corner, and the two profiles meet there with nothing in between.
 */
function showProfiles(context is Context, id is Id, definition is map, path is Path,
    plane is Plane, profiles is map)
{
    paintProfile(context, id + "top", DebugColor.GREEN);
    paintProfile(context, id + "bottom", DebugColor.RED);
    paintProfile(context, id + "middle", DebugColor.BLUE);

    const connectors = profiles["connectors"];

    if (connectors == undefined || size(connectors) == 0)
    {
        return;
    }

    const drawId = id + "joiners";
    startFeature(context, drawId, {});
    var drawn = 0;

    for (var c = 0; c < size(connectors); c += 1)
    {
        const samples = sampleGroup(context, profiles["edgeData"], connectors[c], definition, plane);

        var points = [];
        for (var sample in samples)
        {
            if (size(points) == 0 || norm(sample.point - points[size(points) - 1]) > TOLERANCE.zeroLength * meter)
            {
                points = append(points, sample.point);
            }
        }

        if (size(points) < 2)
        {
            continue;
        }

        opCreateBSplineCurve(context, drawId + ("joiner" ~ c), {
                    "bSplineCurve" : bSplineCurve({
                                "degree" : 1,
                                "isPeriodic" : false,
                                "controlPoints" : points
                            })
                });
        drawn += 1;
    }

    if (drawn > 0)
    {
        addDebugEntities(context, qCreatedBy(drawId, EntityType.EDGE), DebugColor.MAGENTA);
    }
    abortFeature(context, drawId);
}

/**
 * Colour one emitted profile, if it was asked for and therefore exists.
 */
function paintProfile(context is Context, id is Id, color is DebugColor)
{
    const edges = qCreatedBy(id, EntityType.EDGE);

    if (!isQueryEmpty(context, edges))
    {
        addDebugEntities(context, edges, color);
    }
}

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
