FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

export import(path : "a2665e22c07b7a6929ce4e80", version : "d49252259f7b34bf7da639ec");
import(path : "d009ddf4a8dd9534fc4dc4b5", version : "e11a408e487b65a9b42efac8");
import(path : "6479d7fbd0ec7d11e0ae6c69", version : "4f533950f9fcbe2083572c8b");
// design_map_query_utils: embedVariableMap and the extractable wrappers.
import(path : "2b6b313ac740a0146d5bef7c", version : "6e0b68b1f1ffa8bdf4921850");


/**
 * Offset a set of edges by an amount driven by a second set of edges.
 *
 * The profile curve is read as a function: its X is a coordinate along the edges
 * being offset, its Y is the width offset there, its Z is the height offset.
 *
 * Measure along (what the profile's X axis measures, always from the zero point)
 *   WORLD_X        the offset at a source point is the profile value at that point's world X.
 *   OFFSET_EDGES   the offset at distance c along the edges being offset is the profile
 *                  value at X = c.
 *   REFERENCE_WIRE distance is measured along a separate reference wire, itself offset by
 *                  "Offset delta" -- so an offset stated along the core bottom stays stated
 *                  along the core bottom. Distance along a curve offset by h is s - h *
 *                  theta(s), theta being cumulative turning angle, so no offset curve is
 *                  ever built.
 *
 * Frame (what "width" and "height" mean at a point)
 *   ALONG   the chain's own frame. The width axis is the frame axis with the larger
 *           world-Y component, the height axis the other, each signed positive along
 *           its world direction. Roles are fixed once per chain so they cannot swap
 *           part way along.
 *   WORLD   world X, Y, Z.
 *
 * Continuity
 *   G0 input stays G0: each source edge produces its own output curve. Where the
 *   profile has a slope break, the output is split there and each side takes its own
 *   side's slope, because a discontinuous offset implies a discontinuous result.
 *   Elsewhere end tangents are set from the exact offset tangent rather than from
 *   the fitted points, so a G1 junction stays G1.
 *
 * Output
 *   Offset points are classified geometrically. A run that is straight within
 *   tolerance is emitted as a degree-one curve, which Onshape reads as a line. A run
 *   that is circular within tolerance is emitted through a sketch, so it carries a
 *   real radius. Everything else is fitted. One wire is extracted per G0 path.
 */
annotation { "Feature Type Name" : "Driven edge offset", "Feature Type Description" : "Offset edges by a profile curve" }
export const drivenEdgeOffset = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        offsetMeasurePredicate(definition);

        annotation { "Name" : "Name", "Description" : "Name given to the resulting bodies. Clear it to leave them unnamed." }
        definition.outputName is string;

        offsetReferencePredicate(definition);

        offsetEdgesPredicate(definition);

        annotation { "Name" : "Offset profile", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The edges defining the offset. X maps to position along the offset edges, Y to width offset, Z to height offset" }
        definition.offsetProfile is Query;

        offsetAlignmentPredicate(definition);

        offsetCornersPredicate(definition);

        offsetEndsPredicate(definition);

        offsetZeroPredicate(definition);

        drivenOffsetSpacingPredicate(definition);

        offsetDebugPredicate(definition);
    }
    {
        const result = drivenOffset(context, id, definition);

        debugOutput(context, id + "debug", definition, result.sourceChain, result.profile,
            result.alongRef, result.stations, result.coords, result.upper, result.lower,
            result.placed, result.emitted);

        publishOutputs(context, id, definition, result.emitted);
    });

// ============================================================================
// Callable core
// ============================================================================

/**
 * The whole offset, as a callable.
 *
 * Extracted from the feature body so other features can drive it. driven_offset_surface
 * needs the same offset the feature produces AND the data behind it -- which station a
 * point came from, where the profile ran out, what happened at each corner -- none of
 * which survives a feature invocation.
 *
 * Reporting stays with the feature. debugOutput reads the Debug group's booleans and
 * publishOutputs embeds under the feature's own id; a caller assembling its own geometry
 * has neither, and should not have to fake them.
 *
 * @returns {map} :
 *   "stations"    {array} - every station: origin, tangent, widthAxis, heightAxis, arc,
 *                            linkIndex, edgeIndex, and any junction record.
 *   "coords"      {map}   - profile coordinate and scale per station.
 *   "points"      {array} - the offset point per station, undefined where the profile
 *                            does not reach. That gap is which source edges went unused.
 *   "upper"/"lower" {array} - offset amounts and slopes either side of a profile break.
 *   "runs"        {array} - runs after corner and terminal treatment.
 *   "emitted"     {array} - the runs that became geometry, with kind and radius.
 *   "alongRef"    {map}   - the reference mapping, so a caller can evaluate an offset at
 *                            an intermediate width rather than only at its endpoints.
 *   "sourceChain", "profile", "placed" - as built.
 */
export function drivenOffset(context is Context, id is Id, definition is map) returns map
{
    const shared = sharedOffsetContext(context, definition, [definition.offsetProfile], false);

    return offsetFromShared(context, id, definition, shared, 0);
}

/**
 * Everything about an offset that the profile has no say in, computed once.
 *
 * The zero point, the chain, the reference mapping, the stations and their frames depend on
 * the source edges and the reference wire alone. Driving several profiles off the same edges
 * -- which is the whole point of the surface feature -- was rebuilding all of it per profile:
 * the same kernel calls, the same numbers, the same two hundred frames printed twice.
 *
 * Crossings are the one shared thing that is NOT profile-independent, because each profile
 * breaks at its own boundaries. They are inserted here for the UNION of every profile's
 * breaks, so that station i means the same arc length in every profile's tables. What a
 * profile then does with a crossing that is not its own is `matchRuns`:
 *
 *   false - the station is there, the profile reads straight through it, and the runs come
 *           out exactly as they would have with that profile alone. This is what the plain
 *           offset feature wants: no edge split where the offset is smooth.
 *   true  - every profile splits at every crossing, so all of them emit the same run
 *           structure. A loft pairing section against section needs that; without it two
 *           profiles with different breaks give sections with different edge counts and
 *           COLUMNS has nothing to match.
 *
 * @param profileQueries {array} : the profile of each offset, in output order.
 * @param matchRuns {boolean} : whether every profile splits at every profile's breaks.
 */
export function sharedOffsetContext(context is Context, definition is map,
    profileQueries is array, matchRuns is boolean) returns map
{
    const zeroPoint = evZeroPoint(context, definition.offsetRefPoint);
    const sourceChain = buildChain(context, definition.offsetEdges, zeroPoint);

    var alongRef = undefined;
    if (definition.measureAlong == MeasureAlong.REFERENCE_WIRE)
    {
        const delta = definition.flipAlongOffsetDir ? -1 * definition.alongOffsetDelta : definition.alongOffsetDelta;
        alongRef = buildAlongReference(context, definition.referenceWire, zeroPoint, delta);
    }

    const stations = chainStations(context, sourceChain, spacingSettings(definition));
    const coords = stationCoordinates(stations, definition, alongRef, zeroPoint);

    var profiles = [];
    var breaks = [];

    for (var query in profileQueries)
    {
        const profile = buildProfile(context, query, zeroPoint);

        // The two ends of the profile are breaks as much as any interior discontinuity:
        // they are where the offset starts and stops existing. Without a station on them
        // the run begins at the first SAMPLE that happens to fall inside the profile, which
        // is up to one spacing short of the profile's own end -- and on a profile that ramps
        // to zero there, the offset finishes at whatever width that sample had instead of at
        // zero. Measured against a wrapped periphery that is a visible 87 to 480 um of the
        // offset hanging off the surface it was supposed to land on.
        const own = concatenateArrays([discontinuityCoords(profile),
                    [profile.minCoord, profile.maxCoord]]);

        profiles = append(profiles, { "profile" : profile, "breaks" : own });
        breaks = mergeBreaks(breaks, own);
    }

    // Put an exact station on each offset discontinuity before anything is evaluated, so
    // the break lands on the profile's own boundary rather than on whichever sample
    // happened to fall nearest it.
    const split = insertCrossings(context, sourceChain, stations, coords, breaks);

    // Resolve the alignment once and stamp it onto the stations. Everything downstream --
    // placement, run-end tangents, the frame differences behind them, and both debug tables
    // -- then reads one settled frame instead of rebuilding it two or three times per
    // station, and now once rather than once per profile.
    return {
        "zeroPoint" : zeroPoint,
        "sourceChain" : sourceChain,
        "alongRef" : alongRef,
        "stations" : resolveFrames(split.stations, definition, alongRef),
        "coords" : split.coords,
        "profiles" : profiles,
        "matchRuns" : matchRuns
    };
}

/**
 * One profile's offset, over a shared context.
 *
 * @param which {number} : index into `shared.profiles`, matching the order the queries were
 *      handed to sharedOffsetContext.
 */
export function offsetFromShared(context is Context, id is Id, definition is map,
    shared is map, which is number) returns map
{
    const entry = shared.profiles[which];
    const profile = entry.profile;
    const allStations = shared.stations;
    const allCoords = shared.coords;
    const alongRef = shared.alongRef;

    // Two lookups: identical positions, but each side of a profile slope break
    // needs its own slope. Positions are taken from the upper-side pass.
    const upper = profileAt(profile, allCoords.values, false);
    const lower = profileAt(profile, allCoords.values, true);

    const placed = offsetPoints(allStations, upper, lower, definition, alongRef);
    const points = placed.points;
    const splits = runSplits(allStations, entry.breaks, shared.matchRuns);
    const cornered = resolveCorners(context, definition, allStations, allCoords, points,
        upper, lower, buildRuns(allStations, upper, splits), alongRef);
    const runs = resolveTerminals(context, definition, allStations, allCoords, points,
        upper, lower, cornered, alongRef);

    if (size(runs) == 0)
    {
        throw regenError("The offset profile does not reach any of the offset edges.", definition.offsetProfile);
    }

    const emitted = emitRuns(context, id, definition, allStations, allCoords, points, upper, lower, runs, alongRef);

    return {
        "sourceChain" : shared.sourceChain,
        "profile" : profile,
        "alongRef" : alongRef,
        "stations" : allStations,
        "coords" : allCoords,
        "upper" : upper,
        "lower" : lower,
        "placed" : placed,
        "points" : points,
        "runs" : runs,
        "emitted" : emitted
    };
}

/**
 * Break coordinates merged into an ascending list, without duplicating a boundary that two
 * profiles share. insertCrossings walks its list in order against ascending station
 * coordinates, so the order here is not cosmetic.
 */
function mergeBreaks(into is array, adding is array) returns array
{
    var merged = into;

    for (var coord in adding)
    {
        var seen = false;
        for (var already in merged)
        {
            if (abs(already - coord) < OFFSET_GEOM_TOL)
            {
                seen = true;
                break;
            }
        }

        if (!seen)
        {
            merged = append(merged, coord);
        }
    }

    return sortMeasures(merged);
}

/**
 * Ascending, by insertion. The lists are a handful of boundaries long.
 */
function sortMeasures(values is array) returns array
{
    var sorted = [];

    for (var value in values)
    {
        var at = size(sorted);
        for (var i = 0; i < size(sorted); i += 1)
        {
            if (value < sorted[i])
            {
                at = i;
                break;
            }
        }

        sorted = concatenateArrays([subArray(sorted, 0, at), [value], subArray(sorted, at, size(sorted))]);
    }

    return sorted;
}

/**
 * Which stations this profile treats as a run boundary.
 *
 * Every inserted crossing carries the coordinate it was inserted for. A profile splits at
 * its own breaks always, and at another profile's breaks only when the caller asked for
 * matching run structure across profiles.
 */
function runSplits(stations is array, breaks is array, matchRuns is boolean) returns array
{
    var flags = [];

    for (var station in stations)
    {
        if (station.crossing != "right")
        {
            flags = append(flags, false);
            continue;
        }

        if (matchRuns || station.crossingAt == undefined)
        {
            flags = append(flags, true);
            continue;
        }

        var mine = false;
        for (var coord in breaks)
        {
            if (abs(coord - station.crossingAt) < OFFSET_GEOM_TOL)
            {
                mine = true;
                break;
            }
        }

        flags = append(flags, mine);
    }

    return flags;
}


// ============================================================================
// Input settings
// ============================================================================


/**
 * Spacing fields, gathered into one map. Only the field matching the chosen mode
 * is read, so the others being undefined is expected.
 */
function spacingSettings(definition is map) returns map
{
    return {
        "mode" : definition.edgeOffsetSpacingDef,
        "pointsPerEdge" : definition.pointsPerEdge,
        "targetSpacing" : definition.targetPointSpacing,
        "ctrlPointMultiplier" : definition.ctrlPointMultiplier
    };
}

/**
 * Fitting settings for freeform output.
 *
 * All three fields are always defined now that drivenOffsetApproximationPredicate declares
 * them unconditionally, so there is nothing left to fall back to. Only freeform runs
 * consume these -- lines, arcs and corner fills are exact constructions.
 */
function approximationSettings(definition is map) returns map
{
    return {
        "approximationDegree" : definition.approximationDegree,
        "approximationTolerance" : definition.approximationTolerance,
        "approximationMaxCPs" : definition.approximationMaxCPs
    };
}

// ============================================================================
// Mapping stations onto profile coordinates
// ============================================================================

/**
 * Profile coordinate for every station, plus d(coordinate)/d(arc length).
 *
 * The scale converts the profile's slopes (per unit coordinate) into slopes per
 * unit arc length, which is what the offset tangent formula needs.
 *
 * @returns {map} : { "values" : array of coordinates, "scales" : array of numbers }
 */
function stationCoordinates(stations is array, definition is map, alongRef, zeroPoint is Vector) returns map
{
    var values = [];
    var scales = [];

    for (var station in stations)
    {
        if (definition.measureAlong == MeasureAlong.OFFSET_EDGES)
        {
            values = append(values, station.arc);
            scales = append(scales, 1);
        }
        else if (definition.measureAlong == MeasureAlong.WORLD_X)
        {
            values = append(values, station.origin[0] - zeroPoint[0]);
            scales = append(scales, station.tangent[0]);
        }
        else
        {
            const referenceArc = referenceArcAtX(alongRef, station.origin[0]);
            const curvature = interpolate(alongRef.arcs, alongRef.curvatures, referenceArc);

            values = append(values, alongCoordinate(alongRef, referenceArc));

            // d(coord)/d(arc source) = d(coord)/d(arc ref) * d(arc ref)/dx * dx/d(arc source)
            const alongScale = 1 - alongRef.delta * curvature;
            scales = append(scales, alongScale * arcSlopeAt(alongRef, station.origin[0]) * station.tangent[0]);
        }
    }

    return { "values" : values, "scales" : scales };
}

/**
 * d(arc length)/dX on the reference chain, by Hermite lookup on its samples.
 */
function arcSlopeAt(alongRef is map, x is ValueWithUnits) returns number
{
    return interpolate(alongRef.xs, alongRef.arcSlopes, x);
}

/**
 * Apply the chosen alignment to every station, once.
 */
function resolveFrames(stations is array, definition is map, alongRef) returns array
{
    var resolved = [];

    for (var station in stations)
    {
        resolved = append(resolved, stationFrame(station, definition, alongRef));
    }

    return resolved;
}

/**
 * The frame at one station, honouring the chosen alignment.
 *
 * WORLD replaces the chain frame with the world axes; the curvatures are re-resolved
 * about those axes so the fold-back guard measures what it thinks it measures.
 * Source curvature reaches nothing else -- see offsetShrink.
 */
function stationFrame(station is map, definition is map, alongRef) returns map
{
    if (definition.frameAlignment == OffsetFrameAlignment.WORLD)
    {
        return worldFrame(station);
    }

    if (alongRef == undefined)
    {
        // Nothing to be normal to: the chain's own transported frame stands.
        return station;
    }

    // Height is normal to the reference whenever a reference exists. An offset
    // stated as a height above the core bottom has to be measured from the core
    // bottom, not from whichever way the edge being offset happens to lean.
    const heightAxis = referenceHeightAxisAt(alongRef, station.origin[0]);

    // Length always follows the edge being offset, never the reference's own
    // direction. Where the source runs across the reference -- a tip curling round
    // while the reference runs fore-aft -- borrowing the reference's tangent leaves
    // length and width pointing at unrelated things.
    var tangent = station.tangent;

    if (isConstrained(definition, alongRef))
    {
        // Constrained: hold the length direction in the surface, so a length offset
        // cannot change a point's height above the reference either.
        const inSurface = tangent - dot(tangent, heightAxis) * heightAxis;
        if (norm(inSurface) > 1e-9)
        {
            tangent = normalize(inSurface);
        }
    }

    // Width completes the frame: perpendicular to both, so it always lies in the
    // reference surface. Its sign follows the chain's own width axis rather than a
    // world-Y test, which is unstable wherever width runs nearly fore-aft.
    var widthAxis = cross(heightAxis, tangent);
    if (norm(widthAxis) < 1e-9)
    {
        widthAxis = station.widthAxis;
    }
    else
    {
        widthAxis = normalize(widthAxis);
        if (dot(widthAxis, station.widthAxis) < 0)
        {
            widthAxis = -1 * widthAxis;
        }
    }

    const resolved = curvatureOn(station, widthAxis, heightAxis);

    return mergeMaps(station, {
                "tangent" : tangent,
                "widthAxis" : widthAxis,
                "heightAxis" : heightAxis,
                "curvatureWidth" : resolved.curvatureWidth,
                "curvatureHeight" : resolved.curvatureHeight
            });
}

/**
 * World axes, for the WORLD alignment.
 */
function worldFrame(station is map) returns map
{
    const widthAxis = vector(0, 1, 0);
    const heightAxis = vector(0, 0, 1);
    const resolved = curvatureOn(station, widthAxis, heightAxis);

    return mergeMaps(station, {
                "tangent" : vector(1, 0, 0),
                "widthAxis" : widthAxis,
                "heightAxis" : heightAxis,
                "curvatureWidth" : resolved.curvatureWidth,
                "curvatureHeight" : resolved.curvatureHeight
            });
}

/**
 * Offset position at every station, and the fold-back margin each one was tested
 * against. Stations the profile does not reach get undefined for both.
 *
 * The margin is returned rather than recomputed for the debug table, because the two
 * maps are guarded by different quantities: the straight step by offsetShrink, built
 * from source curvature, and the surface-following step by the reference's own
 * (scale - kappa * height). Printing the source one in reference mode showed ~1.0000
 * for ever while the number that could actually fail was never displayed.
 *
 * @returns {map} : { "points" : array, "margins" : array }
 */
function offsetPoints(stations is array, upper is array, lower is array, definition is map, alongRef) returns map
{
    var points = [];
    var margins = [];

    for (var i = 0; i < size(stations); i += 1)
    {
        // The left half of a crossing belongs to the profile edge below it.
        const offsets = (stations[i].crossing == "left") ? lower : upper;

        if (offsets[i] == undefined)
        {
            points = append(points, undefined);
            margins = append(margins, undefined);
            continue;
        }

        const frame = stations[i];

        // Only the fold-back test is wanted here. The direction is a run-end
        // question, asked separately where the frame's turn rates are available.
        const sourceMargin = offsetShrink(frame, offsets[i]);
        if (sourceMargin <= 0)
        {
            throw regenError("The offset is larger than the radius of curvature at "
                ~ toString(roundToPrecision(stations[i].arc / millimeter, 1))
                ~ " mm from the zero point, so the result would fold back on itself. Reduce the offset there.");
        }

        if (usesReferenceFrame(definition, alongRef))
        {
            const placed = surfaceOffset(alongRef, frame, offsets[i]);

            // The source curve's own fold-back is caught above. This is the other
            // one: a height offset that reaches the centre of curvature of the
            // reference itself. offsetShrink cannot see it, because it is built
            // from the SOURCE curvature and a straight source edge over a curved
            // reference leaves it sitting at exactly 1.
            const surfaceMargin = placed.surf.scale - placed.surf.curvature * placed.height;
            if (surfaceMargin <= 0)
            {
                throw regenError("The height offset reaches the centre of curvature of the reference at "
                    ~ toString(roundToPrecision(stations[i].arc / millimeter, 1))
                    ~ " mm from the zero point, so the result would fold back on itself. Reduce the height offset there.");
            }

            points = append(points, placed.point);
            margins = append(margins, surfaceMargin);
            continue;
        }

        points = append(points, frame.origin + offsets[i].width * frame.widthAxis + offsets[i].height * frame.heightAxis);
        margins = append(margins, sourceMargin);
    }

    return { "points" : points, "margins" : margins };
}

// ============================================================================
// Runs
// ============================================================================

/**
 * Group stations into runs, each of which becomes one output curve.
 *
 * A run breaks at a source edge boundary, so G0 input stays G0; where the profile
 * stops providing data; and at an offset discontinuity, which arrives as a pair of
 * stations sharing one coordinate. The pair's left half ends a run and its right
 * half starts the next, so a step in the offset produces a step in the output and
 * a kink produces a kink -- rather than one curve smoothed through the break.
 *
 * @returns {array} : each { "start", "end", "linkIndex" }, inclusive indices.
 */
function buildRuns(stations is array, offsets is array, splits is array) returns array
{
    var runs = [];
    var start = undefined;

    for (var i = 0; i < size(stations); i += 1)
    {
        if (offsets[i] == undefined)
        {
            runs = closeRun(runs, stations, start, i - 1);
            start = undefined;
            continue;
        }

        if (start == undefined)
        {
            start = i;
            continue;
        }

        const newEdge = (stations[i].linkIndex != stations[start].linkIndex
                || stations[i].edgeIndex != stations[start].edgeIndex);

        if (newEdge || splits[i])
        {
            runs = closeRun(runs, stations, start, i - 1);
            start = i;
        }
    }

    return closeRun(runs, stations, start, size(stations) - 1);
}

/**
 * Append a run if it holds at least two stations.
 */
function closeRun(runs is array, stations is array, start, end is number) returns array
{
    if (start == undefined || end - start < 1)
    {
        return runs;
    }

    return append(runs, { "start" : start, "end" : end, "linkIndex" : stations[start].linkIndex });
}

// ============================================================================
// Offset discontinuities
// ============================================================================

/**
 * Insert a pair of stations at every coordinate where the offset actually breaks.
 *
 * Crossing into a new profile edge is not by itself a discontinuity -- profile
 * edges usually join smoothly, and splitting at every one of them would litter the
 * output with needless seams. Only a step in the offset value or a break in its
 * slope earns a split.
 *
 * The inserted pair shares one coordinate: the left station reads the profile edge
 * below the boundary, the right station the one above. Where the offset only kinks
 * they land on the same point and the output stays connected; where it steps they
 * separate by exactly the step.
 */
function insertCrossings(context is Context, chain is map, stations is array, coords is map, breaks is array) returns map
{
    if (size(breaks) == 0 || size(stations) < 2)
    {
        return { "stations" : stations, "coords" : coords };
    }

    var outStations = [];
    var values = [];
    var scales = [];
    var next = 0;

    for (var i = 0; i < size(stations); i += 1)
    {
        while (i > 0 && next < size(breaks)
            && breaks[next] > coords.values[i - 1] && breaks[next] <= coords.values[i])
        {
            const arc = arcAtCoord(stations, coords, i, breaks[next]);
            const crossing = crossingStation(context, chain, stations, i, arc);

            // The coordinate travels with the station so that a profile can later tell its
            // own break from one inserted for a different profile.
            outStations = append(outStations, mergeMaps(crossing, { "crossing" : "left", "crossingAt" : breaks[next] }));
            outStations = append(outStations, mergeMaps(crossing, { "crossing" : "right", "crossingAt" : breaks[next] }));
            values = append(values, breaks[next]);
            values = append(values, breaks[next]);
            scales = append(scales, coords.scales[i]);
            scales = append(scales, coords.scales[i]);
            next += 1;
        }

        outStations = append(outStations, stations[i]);
        values = append(values, coords.values[i]);
        scales = append(scales, coords.scales[i]);
    }

    return { "stations" : outStations, "coords" : { "values" : values, "scales" : scales } };
}

/**
 * Profile boundaries where the offset genuinely breaks, in ascending order.
 */
function discontinuityCoords(profile is map) returns array
{
    var found = [];

    for (var boundary in profileBoundaries(profile))
    {
        const left = profileAt(profile, [boundary], true)[0];
        const right = profileAt(profile, [boundary], false)[0];

        if (left == undefined || right == undefined)
        {
            continue;
        }

        const steps = abs(left.width - right.width) > OFFSET_GEOM_TOL
            || abs(left.height - right.height) > OFFSET_GEOM_TOL;
        const kinks = abs(left.widthSlope - right.widthSlope) > 1e-6
            || abs(left.heightSlope - right.heightSlope) > 1e-6;

        if (steps || kinks)
        {
            found = append(found, boundary);
        }
    }

    return found;
}

/**
 * Arc length at a coordinate, by cubic Hermite between the two stations that
 * bracket it. The coordinate scales are d(coord)/d(arc), so their reciprocals are
 * exactly the slopes this inversion needs.
 */
function arcAtCoord(stations is array, coords is map, index is number, coord is ValueWithUnits) returns ValueWithUnits
{
    const scaleBefore = coords.scales[index - 1];
    const scaleAfter = coords.scales[index];

    if (abs(scaleBefore) < 1e-9 || abs(scaleAfter) < 1e-9)
    {
        const span = coords.values[index] - coords.values[index - 1];
        const fraction = (abs(span / meter) < 1e-12) ? 0 : (coord - coords.values[index - 1]) / span;

        return stations[index - 1].arc + fraction * (stations[index].arc - stations[index - 1].arc);
    }

    return hermiteAt([coords.values[index - 1], coords.values[index]],
        [stations[index - 1].arc, stations[index].arc],
        [1 / scaleBefore, 1 / scaleAfter], coord);
}

/**
 * A station at an arbitrary arc length, for a discontinuity that falls between
 * samples. Costs one kernel call; the frame is transported from the station before
 * it so it joins the same roll-free field as the rest.
 */
function crossingStation(context is Context, chain is map, stations is array, index is number, arc is ValueWithUnits) returns map
{
    const previous = stations[index - 1];
    const located = edgeAtArc(chain, arc);
    const tangentLine = evEdgeTangentLines(context, {
                "edge" : located.edgeData.query,
                "parameters" : [edgeParam(located.edgeData, located.fraction)]
            })[0];

    const tangent = located.edgeData.flipped ? -1 * tangentLine.direction : tangentLine.direction;
    const normal = transportNormal(previous.normal, previous.tangent, tangent);
    const axes = offsetAxes(tangent, normal, previous.roles);

    // The curvature vector is inherited from the neighbouring station, but it has to
    // be re-resolved against THIS station's axes. Leaving the neighbour's components
    // would feed the fold-back guard a curvature measured about axes that no longer
    // exist -- and crossings are always run endpoints.
    const resolved = curvatureOn(previous, axes.widthAxis, axes.heightAxis);

    return mergeMaps(previous, {
                "arc" : arc,
                // An inserted crossing is not a vertex between two source edges, so
                // it must not inherit the neighbour's junction record -- that was
                // reporting one welded junction three times.
                "junctionBreak" : undefined,
                "welded" : undefined,
                "origin" : tangentLine.origin,
                "tangent" : tangent,
                "normal" : normal,
                "widthAxis" : axes.widthAxis,
                "heightAxis" : axes.heightAxis,
                "curvatureWidth" : resolved.curvatureWidth,
                "curvatureHeight" : resolved.curvatureHeight
            });
}

/**
 * The chain edge containing an arc length, and how far along it that arc falls.
 * Station arcs are measured from the zero point, edge arcs from the chain start.
 */
function edgeAtArc(chain is map, arc is ValueWithUnits) returns map
{
    const absolute = arc + chain.zeroArc;
    var last = undefined;

    for (var link in chain.links)
    {
        for (var edgeData in link.edges)
        {
            last = edgeData;
            if (absolute >= edgeData.startArc && absolute <= edgeData.startArc + edgeData.length)
            {
                return {
                    "edgeData" : edgeData,
                    "fraction" : clamp((absolute - edgeData.startArc) / edgeData.length, 0, 1)
                };
            }
        }
    }

    return { "edgeData" : last, "fraction" : 1 };
}

// ============================================================================
// Output
// ============================================================================

/**
 * Emit one curve per run, then extract one wire per G0 path.
 */
function emitRuns(context is Context, id is Id, definition is map, stations is array,
    coords is map, points is array, upper is array, lower is array, runs is array, alongRef) returns array
{
    const approximation = approximationSettings(definition);
    var bodiesByLink = {};
    var emitted = [];

    for (var r = 0; r < size(runs); r += 1)
    {
        const run = runs[r];
        const runId = id + ("run" ~ r);

        // A trimmed run carries the exact crossing point in place of the stations it
        // gave up, so both sides of a trimmed corner end on the same coordinates.
        var runPoints = [];
        if (run.startPoint != undefined)
        {
            runPoints = append(runPoints, run.startPoint);
        }
        for (var i = run.start; i <= run.end; i += 1)
        {
            runPoints = append(runPoints, points[i]);
        }
        if (run.endPoint != undefined)
        {
            runPoints = append(runPoints, run.endPoint);
        }
        runPoints = withoutRepeats(runPoints);

        // A corner filler belongs to the run it leads into, and joins the same wire.
        if (run.fill != undefined)
        {
            const fillId = id + ("fill" ~ r);
            if (run.fill.kind == "arc")
            {
                emitArcCurve(context, fillId, run.fill);
            }
            else
            {
                opCreateBSplineCurve(context, fillId, { "bSplineCurve" : run.fill.curve });
            }

            const fillKey = "link" ~ run.linkIndex;
            bodiesByLink[fillKey] = append(bodiesByLink[fillKey] == undefined ? [] : bodiesByLink[fillKey],
                qCreatedBy(fillId, EntityType.EDGE));
        }

        // A terminal extension stays its own curve rather than joining the fit. It is
        // geometry we fabricated past where the source data stops, and the run's tolerance
        // should describe the offset, not the piece invented to reach a plane.
        for (var side in ["startExtension", "endExtension"])
        {
            if (run[side] == undefined)
            {
                continue;
            }

            const extensionId = id + (side ~ r);
            opCreateBSplineCurve(context, extensionId, { "bSplineCurve" : run[side] });

            const extensionKey = "link" ~ run.linkIndex;
            bodiesByLink[extensionKey] = append(
                bodiesByLink[extensionKey] == undefined ? [] : bodiesByLink[extensionKey],
                qCreatedBy(extensionId, EntityType.EDGE));
        }

        // A run can collapse to a point when a profile break falls exactly on a source
        // edge boundary: the crossing pair lands immediately before the junction's left
        // station, so the run between them spans no arc at all. classifyPoints would call
        // that a "line" with start == end, and a zero-length degree-one curve is not
        // geometry. The corner fill and the neighbouring runs cover the position, so
        // dropping it loses nothing.
        var runSpan = 0 * meter;
        for (var i = 1; i < size(runPoints); i += 1)
        {
            runSpan += norm(runPoints[i] - runPoints[i - 1]);
        }
        if (size(runPoints) < 2 || runSpan < OFFSET_GEOM_TOL)
        {
            println("NOTE: run " ~ toString(r) ~ " spans "
                ~ fmtMM(runSpan, 6, 0) ~ " mm and was not emitted; a profile break"
                ~ " coincides with a source edge boundary there.");
            continue;
        }

        const shape = classifyPoints(runPoints, approximation.approximationTolerance);

        if (shape.kind == "line")
        {
            emitLineCurve(context, runId, shape.start, shape.end);
        }
        else if (shape.kind == "arc")
        {
            emitArcCurve(context, runId, shape);
        }
        else
        {
            // A run starts on the upper side of any slope break and ends on the lower
            // side of the next one, so each end takes the slope that actually applies.
            emitSplineCurve(context, runId, runPoints,
                runTangent(stations, coords, upper, definition, alongRef, run, run.start),
                runTangent(stations, coords, lower, definition, alongRef, run, run.end),
                approximation);
        }

        emitted = append(emitted, mergeMaps(run, {
                        "kind" : shape.kind,
                        "radius" : (shape.kind == "arc") ? shape.radius : undefined
                    }));

        const key = "link" ~ run.linkIndex;
        bodiesByLink[key] = append(bodiesByLink[key] == undefined ? [] : bodiesByLink[key],
            qCreatedBy(runId, EntityType.EDGE));
    }

    var created = [];
    for (var key in keys(bodiesByLink))
    {
        created = concatenateArrays([created, bodiesByLink[key]]);
    }

    if (size(created) == 0)
    {
        return emitted;
    }

    if (definition.joinOutput)
    {
        var wires = [];
        for (var key in keys(bodiesByLink))
        {
            const wireId = id + ("wire" ~ key);
            opExtractWires(context, wireId, { "edges" : qUnion(bodiesByLink[key]) });
            wires = append(wires, qCreatedBy(wireId, EntityType.BODY));
        }

        // The extracted wires are independent copies, so the curves they came from go.
        opDeleteBodies(context, id + "cleanup", { "entities" : qOwnerBody(qUnion(created)) });
        nameOutput(context, qUnion(wires), definition.outputName);
    }
    else
    {
        nameOutput(context, qOwnerBody(qUnion(created)), definition.outputName);
    }

    return emitted;
}

/**
 * Expose this feature's results to a later Extract variables feature.
 *
 * Two kinds of thing go out. The queries are what another feature would otherwise have to
 * pick in the viewport -- published as query variables they appear in any selection dropdown
 * and survive a regeneration that moves the geometry, which a viewport pick does not.
 *
 * The variables are measurements this feature already computes and, until now, only ever
 * printed. Squareness in particular is the number that diagnosed the tail drift: as a
 * variable a QC feature can assert on it, where a println can only be read by a person.
 *
 * Costs one setVariable and no kernel calls, so it runs unconditionally rather than behind
 * a toggle -- the slot is keyed by feature id and is not reachable from `#`, so it adds
 * nothing a user has to look at.
 */
function publishOutputs(context is Context, id is Id, definition is map, emitted is array)
{
    var variables = {
            "curveCount" : extractableVariable(size(emitted),
                    "Number of curves this offset emitted.")
        };

    var radii = [];

    for (var run in emitted)
    {
        if (run.fill != undefined && run.fill.kind == "arc")
        {
            radii = append(radii, run.fill.radius);
        }

        // Both ends can land on the same run when the whole chain is one run, so these are
        // read independently rather than as an either/or.
        if (run.terminalStart != undefined)
        {
            variables = withTerminalVariables(variables, "start", run.terminalStart);
        }
        if (run.terminalEnd != undefined)
        {
            variables = withTerminalVariables(variables, "end", run.terminalEnd);
        }
    }

    variables["cornerArcCount"] = extractableVariable(size(radii),
        "G0 corners that were rounded with a true arc.");

    if (size(radii) > 0)
    {
        variables["cornerArcRadii"] = extractableVariable(radii,
            "Radius of each rounded corner, in chain order.");
    }

    embedVariableMap(context, id, {
                "variable" : variables,
                "query" : {
                    "offsetBodies" : extractableQuery(qCreatedBy(id, EntityType.BODY),
                            "Bodies produced by this offset.", DebugColor.GREEN),
                    "offsetEdges" : extractableQuery(qCreatedBy(id, EntityType.EDGE),
                            "Edges of the offset result.", DebugColor.GREEN),
                    "sourceEdges" : extractableQuery(definition.offsetEdges,
                            "The edges this offset was taken from.", DebugColor.BLUE)
                }
            });
}

/**
 * Fold one end's terminal record into the published variables.
 */
function withTerminalVariables(variables is map, side is string, record is map) returns map
{
    var out = variables;

    out[side ~ "Action"] = extractableVariable(record.action,
        "What happened where the offset met its " ~ side ~ " plane.");
    out[side ~ "Distance"] = extractableVariable(abs(record.distance),
        "How far the " ~ side ~ " of the offset was trimmed or extended.");
    out[side ~ "Squareness"] = extractableVariable(record.squareness,
        "Angle between the source tangent and the " ~ side ~ " plane normal. Zero means the "
        ~ "source ended square and the offset would have landed on the plane unaided.");

    if (record.kink != undefined)
    {
        out[side ~ "Kink"] = extractableVariable(record.kink,
            "Angle between the offset's own heading and the direction it was told to arrive at.");
    }

    return out;
}

/**
 * Name the bodies this feature produced.
 *
 * An empty name is a deliberate choice, not a missing value: setProperty would happily
 * write "" and leave the bodies looking unnamed but shadowed, so skip it instead.
 */
function nameOutput(context is Context, bodies is Query, name is string)
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
