FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

export import(path : "a2665e22c07b7a6929ce4e80", version : "d87f144b46f7f0bb6b456fc9");

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
        annotation { "Name" : "Measure along", "Default" : MeasureAlong.OFFSET_EDGES, "Description" : "What the offset profile's X axis measures: a world X coordinate, distance along the edges being offset, or distance along a separate reference wire. All three are measured from the zero point.", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
        definition.measureAlong is MeasureAlong;

        annotation { "Name" : "Name", "Description" : "Name given to the resulting bodies. Clear it to leave them unnamed." }
        definition.outputName is string;

        if (definition.measureAlong == MeasureAlong.REFERENCE_WIRE)
        {
            annotation { "Group Name" : "Offset spacing definition", "Collapsed By Default" : false }
            {
                annotation { "Name" : "Reference wire", "Filter" : EntityType.BODY && BodyType.WIRE && ConstructionObject.NO, "MaxNumberOfPicks" : 1, "Description" : "The wire that profile X is measured along" }
                definition.referenceWire is Query;

                annotation { "Name" : "Offset delta", "Description" : "Measure along a curve this far from the selected wire, so an offset stated along the core bottom stays stated along the core bottom" }
                isLength(definition.alongOffsetDelta, OffsetHeightBounds);

                annotation { "Name" : "Flip offset delta", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
                definition.flipAlongOffsetDir is boolean;

                annotation { "Name" : "Hold length in the reference surface", "Default" : false, "Description" : "Project the length direction into the reference surface, so a length offset cannot change a point's height above it. Height is always measured normal to the reference, and width always lies in the surface, with or without this." }
                definition.constrainProfile is boolean;
            }
        }

        annotation { "Name" : "Offset edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
        definition.offsetEdges is Query;

        annotation { "Name" : "Offset profile", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The edges defining the offset. X maps to position along the offset edges, Y to width offset, Z to height offset" }
        definition.offsetProfile is Query;

        annotation { "Name" : "Offset alignment", "Default" : OffsetFrameAlignment.ALONG, "Description" : "How the offset frame is oriented at each point along the offset edges" }
        definition.frameAlignment is OffsetFrameAlignment;

        annotation { "Group Name" : "Corners", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Where the offset gaps", "Default" : CornerGapMode.ARC, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Description" : "A G0 corner in the offset edges separates the two offsets by 2 * width * sin(angle/2) on the outside of the turn. Rounding uses a true circular arc centred on the corner vertex wherever one exists, and an arc-like cubic where it does not." }
            definition.cornerGapMode is CornerGapMode;

            annotation { "Name" : "Where the offset crosses", "Default" : CornerOverlapMode.TRIM, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Description" : "The same corner overlaps on the inside of the turn, by width * tan(angle/2) along each side. Trimming cuts both back to where they actually cross." }
            definition.cornerOverlapMode is CornerOverlapMode;
        }

        annotation { "Name" : "Zero point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.offsetRefPoint is Query;


        annotation { "Group Name" : "Spacing & approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Offset spacing", "Description" : "How many points to evaluate along each offset edge", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : OffsetPointSpacing.CTRL_POINT }
            definition.edgeOffsetSpacingDef is OffsetPointSpacing;

            if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.CTRL_POINT)
            {
                annotation { "Name" : "Control point multiplier" }
                isInteger(definition.ctrlPointMultiplier, CtrlPointMultiplierBounds);
            }
            if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.NUM_POINTS)
            {
                annotation { "Name" : "Points per edge" }
                isInteger(definition.pointsPerEdge, PointsPerEdgeBounds);
            }
            if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.DISTANCE_ALONG)
            {
                annotation { "Name" : "Point spacing" }
                isLength(definition.targetPointSpacing, PointSpacingBounds);
            }

            annotation { "Name" : "Join runs into one wire per link", "Default" : true, "Description" : "Extract the emitted curves into a single wire body per connected chain. Off leaves every run, corner fill and trimmed piece as its own curve body." }
            definition.joinOutput is boolean;

            annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
            {
                offsetApproximationPredicate(definition);
            }
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print from chain", "Default" : false }
            definition.debugPrintFromChain is boolean;

            annotation { "Name" : "Print along chain", "Default" : false }
            definition.debugPrintAlongChain is boolean;

            annotation { "Name" : "Print profile chain", "Default" : false }
            definition.debugPrintProfileChain is boolean;

            annotation { "Name" : "Show offset frames", "Default" : false, "Description" : "Draw the width and height axes at each station" }
            definition.debugShowOffsetFrames is boolean;

            annotation { "Name" : "Show offsets", "Default" : false, "Description" : "Draw each source point to its offset point" }
            definition.debugShowOffsets is boolean;

            annotation { "Name" : "Show reference offset", "Default" : false, "Description" : "Draw the curve the coordinate is actually measured along -- the reference wire moved by the offset delta. Invisible otherwise: it is neither the wire you picked nor anything in the output." }
            definition.debugShowReference is boolean;

            annotation { "Name" : "Visualize continuity", "Default" : false, "Description" : "Mark where the output is split into separate curves" }
            definition.debugVisualizeContinuity is boolean;

            annotation { "Name" : "Print offset table", "Default" : false, "Description" : "Every station's coordinate, offset and resulting point, grouped by source edge" }
            definition.debugPrintOffsetTable is boolean;

            annotation { "Name" : "Print frame table", "Default" : false, "Description" : "Every station's tangent, width axis and height axis, grouped by source edge" }
            definition.debugPrintFrameTable is boolean;
        }
    }
    {
        const zeroPoint = evZeroPoint(context, definition.offsetRefPoint);
        const sourceChain = buildChain(context, definition.offsetEdges, zeroPoint);
        const profile = buildProfile(context, definition.offsetProfile, zeroPoint);

        var alongRef = undefined;
        if (definition.measureAlong == MeasureAlong.REFERENCE_WIRE)
        {
            const delta = definition.flipAlongOffsetDir ? -1 * definition.alongOffsetDelta : definition.alongOffsetDelta;
            alongRef = buildAlongReference(context, definition.referenceWire, zeroPoint, delta);
        }

        const stations = chainStations(context, sourceChain, spacingSettings(definition));
        const coords = stationCoordinates(stations, definition, alongRef, zeroPoint);

        // Two lookups: identical positions, but each side of a profile slope break
        // needs its own slope. Positions are taken from the upper-side pass.
        // Put an exact station on each offset discontinuity before anything is
        // evaluated, so the break lands on the profile's own boundary rather than
        // on whichever sample happened to fall nearest it.
        const split = insertCrossings(context, sourceChain, stations, coords, profile);
        const allCoords = split.coords;

        // Resolve the alignment once and stamp it onto the stations. Everything
        // downstream -- placement, run-end tangents, the frame differences behind
        // them, and both debug tables -- then reads one settled frame instead of
        // rebuilding it two or three times per station.
        const allStations = resolveFrames(split.stations, definition, alongRef);

        const upper = profileAt(profile, allCoords.values, false);
        const lower = profileAt(profile, allCoords.values, true);

        const placed = offsetPoints(allStations, upper, lower, definition, alongRef);
        const points = placed.points;
        const runs = resolveCorners(context, definition, allStations, allCoords, points,
            upper, lower, buildRuns(allStations, upper), alongRef);

        if (size(runs) == 0)
        {
            throw regenError("The offset profile does not reach any of the offset edges.", definition.offsetProfile);
        }

        const emitted = emitRuns(context, id, definition, allStations, allCoords, points, upper, lower, runs, alongRef);

        debugOutput(context, id + "debug", definition, sourceChain, profile, alongRef, allStations, allCoords,
            upper, lower, placed, emitted);
    });

// ============================================================================
// Input settings
// ============================================================================

/** Control-point budget for a fitted run. The floor of 4 is a cubic's minimum. */
export const OffsetMaxCPBounds = { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec;

/**
 * The approximation controls this feature actually uses.
 *
 * Replaces std's curveApproximationPredicate, five of whose eight fields were dead or
 * actively misleading here:
 *
 *   "Keep start derivative" / "Keep end derivative" were never read. We compute the exact
 *   offset tangent at every run end ourselves and hand it to the solver as a hard
 *   constraint, so there is nothing for the user to keep or discard.
 *
 *   "Maximum deviation" is declared READ_ONLY by that predicate on the understanding that
 *   the feature writes the measured value back. This one never did, so the field sat
 *   permanently blank.
 *
 *   "Approximate" did not switch approximation on or off. Unchecked, it swapped the
 *   user's three numbers for hard-coded ones and fitted exactly the same runs.
 *
 * Worth knowing while reading these: only freeform runs reach the solver at all. Lines,
 * arcs and corner fills are exact constructions and ignore every field here.
 */
predicate offsetApproximationPredicate(definition is map)
{
    annotation { "Name" : "Target degree", "Description" : "Degree the fit aims for on freeform runs" }
    isInteger(definition.approximationDegree, DEGREE_BOUND);

    annotation { "Name" : "Tolerance", "Description" : "How far a fitted run may sit from the computed offset points" }
    isLength(definition.approximationTolerance, TOLERANCE_BOUND);

    annotation { "Name" : "Maximum control points", "Description" : "Cap on a fitted run. The fit stops as soon as tolerance is met, so this only binds on a run that cannot reach it." }
    isInteger(definition.approximationMaxCPs, OffsetMaxCPBounds);
}

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
 * All three fields are always defined now that offsetApproximationPredicate declares
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
 * Whether length and width are also taken from the reference, rather than only
 * height. The height axis follows the reference either way.
 */
function isConstrained(definition is map, alongRef) returns boolean
{
    return alongRef != undefined && definition.constrainProfile == true;
}

/**
 * Whether this station is placed on the reference surface rather than by a straight
 * step from the source point.
 *
 * The one test that picks the map: surfaceOffset/surfaceOffsetTangent when true,
 * offsetPoints' straight step and offsetTangent when false. The two are tangents to
 * different maps, so placement and direction must agree on this or the fitted end
 * tangent describes a curve the points do not lie on.
 */
function usesReferenceFrame(definition is map, alongRef) returns boolean
{
    return alongRef != undefined && definition.frameAlignment == OffsetFrameAlignment.ALONG;
}

/**
 * Offset position at every station. Stations the profile does not reach get undefined.
 *
 * The (1 - w * kappa) factor is checked here: at or below zero the offset has passed
 * the centre of curvature, and the result would fold back through itself.
 */
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
function buildRuns(stations is array, offsets is array) returns array
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

        if (newEdge || stations[i].crossing == "right")
        {
            runs = closeRun(runs, stations, start, i - 1);
            start = i;
        }
    }

    return closeRun(runs, stations, start, size(stations) - 1);
}

/**
 * Decide what happens at every G0 corner between two runs.
 *
 * weldJunctions has already separated the corners from the merely-noisy junctions: a
 * station carrying junctionBreak with welded == false is a corner the source really has.
 * Offsetting one by w does two things at once -- it opens a gap of 2*w*sin(theta/2) on the
 * outside of the turn and drives an overlap of w*tan(theta/2) deep on the inside -- and
 * which one a given corner shows depends only on which side the offset went.
 *
 * The test is whether the next run starts ahead of where the previous one ended, measured
 * along the direction of travel. Ahead means the ends separated: a gap. Behind means they
 * ran past each other: an overlap.
 *
 * Gaps get a filler curve attached to the following run. Overlaps trim both runs back to
 * their crossing and give them a shared exact endpoint, so the wire stays single.
 */
function resolveCorners(context is Context, definition is map, stations is array, coords is map,
    points is array, upper is array, lower is array, runs is array, alongRef) returns array
{
    // Survey every corner against the UNTOUCHED runs first. Treating them as we go would
    // not work: a trim moves run indices, so the next iteration's adjacency test and its
    // station-marker lookup would both be reading indices that have already shifted.
    var corners = [];

    for (var r = 1; r < size(runs); r += 1)
    {
        const prev = runs[r - 1];
        const next = runs[r];

        // Only between runs that actually abut, and only at a vertex the weld declined.
        if (next.start != prev.end + 1 || prev.linkIndex != next.linkIndex)
        {
            continue;
        }

        const marker = stations[next.start];
        if (marker.junctionBreak == undefined || marker.welded == true)
        {
            continue;
        }

        const p1 = points[prev.end];
        const p2 = points[next.start];
        if (p1 == undefined || p2 == undefined || norm(p2 - p1) < OFFSET_GEOM_TOL)
        {
            continue;
        }

        // Exact offset tangents where they exist; the chord is a safe stand-in where the
        // profile does not reach far enough to define one.
        var t1 = runTangent(stations, coords, lower, definition, alongRef, prev, prev.end);
        var t2 = runTangent(stations, coords, upper, definition, alongRef, next, next.start);
        const chordDir = normalize(p2 - p1);
        if (t1 == undefined) { t1 = chordDir; }
        if (t2 == undefined) { t2 = chordDir; }

        corners = append(corners, {
                    "index" : r,
                    "vertex" : marker.origin,
                    "p1" : p1,
                    "t1" : t1,
                    "p2" : p2,
                    "t2" : t2,
                    "isGap" : dot(p2 - p1, t1 + t2) > 0 * meter
                });
    }

    var resolved = runs;

    for (var corner in corners)
    {
        resolved = corner.isGap
            ? fillCornerGap(definition, resolved, corner)
            : trimCornerOverlap(definition, resolved, corner.index, points);
    }

    return resolved;
}

/**
 * Attach a filler to the run that follows an open corner.
 *
 * ARC prefers a true circle centred on the source vertex, which is tangent to both runs
 * by construction, and falls back to an arc-like cubic when the two ends are not
 * co-radial about the vertex. EXTEND runs both sides out to where their tangents meet.
 */
function fillCornerGap(definition is map, runs is array, corner is map) returns array
{
    var resolved = runs;
    const index = corner.index;
    const p1 = corner.p1;
    const p2 = corner.p2;
    const t1 = corner.t1;
    const t2 = corner.t2;

    if (definition.cornerGapMode == CornerGapMode.OPEN)
    {
        return resolved;
    }

    if (definition.cornerGapMode == CornerGapMode.EXTEND)
    {
        // Where the two tangents meet. A chord-length ray reaches the miter for any turn
        // a corner can sensibly have; beyond that the meet is too far out to want.
        const reach = norm(p2 - p1);
        const meet = segmentApproach(p1, p1 + reach * t1, p2, p2 - reach * t2);

        resolved[index - 1] = mergeMaps(resolved[index - 1], { "endPoint" : meet.point });
        resolved[index] = mergeMaps(resolved[index],
            { "startPoint" : meet.point, "cornerKind" : "extended" });

        return resolved;
    }

    const arc = cornerArc(corner.vertex, p1, p2, OFFSET_GEOM_TOL);

    resolved[index] = mergeMaps(resolved[index], {
                "cornerKind" : "gap",
                "fill" : (arc != undefined)
                    ? arc
                    : { "kind" : "spline", "curve" : arcLikeSpline(p1, t1, p2, t2) }
            });

    return resolved;
}

/**
 * Cut both runs back to where they cross on the inside of a corner.
 *
 * Trimming the point lists rather than the emitted geometry keeps everything in the
 * existing architecture: the fit then produces an already-trimmed curve, and both sides
 * are handed the same crossing point so they share an endpoint exactly.
 */
function trimCornerOverlap(definition is map, runs is array, index is number, points is array) returns array
{
    var resolved = runs;

    if (definition.cornerOverlapMode == CornerOverlapMode.KEEP)
    {
        return resolved;
    }

    const prev = resolved[index - 1];
    const next = resolved[index];

    const backTo = max(prev.start + 1, prev.end - CORNER_TRIM_WINDOW);
    const forwardTo = min(next.end - 1, next.start + CORNER_TRIM_WINDOW);

    var best = undefined;

    for (var i = prev.end; i > backTo; i -= 1)
    {
        for (var j = next.start; j < forwardTo; j += 1)
        {
            if (points[i - 1] == undefined || points[i] == undefined
                || points[j] == undefined || points[j + 1] == undefined)
            {
                continue;
            }

            const approach = segmentApproach(points[i - 1], points[i], points[j], points[j + 1]);
            if (best == undefined || approach.distance < best.distance)
            {
                best = mergeMaps(approach, { "i" : i, "j" : j });
            }
        }
    }

    // Both sides must keep at least one station, or there is nothing left to fit and the
    // trim would delete more than it repairs.
    if (best == undefined || best.i - 1 < prev.start || best.j + 1 > next.end)
    {
        println("WARNING: corner at station " ~ toString(next.start)
            ~ " overlaps further than either run is long; left untrimmed.");
        return resolved;
    }

    resolved[index - 1] = mergeMaps(prev, { "end" : best.i - 1, "endPoint" : best.point });
    resolved[index] = mergeMaps(next,
        { "start" : best.j + 1, "startPoint" : best.point, "cornerKind" : "trimmed" });

    return resolved;
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
function insertCrossings(context is Context, chain is map, stations is array, coords is map, profile is map) returns map
{
    const breaks = discontinuityCoords(profile);
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

            outStations = append(outStations, mergeMaps(crossing, { "crossing" : "left" }));
            outStations = append(outStations, mergeMaps(crossing, { "crossing" : "right" }));
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

/**
 * Exact offset tangent at one end of a run, for the fit to interpolate.
 *
 * Everything needed is determined at a run end: the source curve has a tangent and
 * a curvature there, the profile has a value and a slope there, and the frame has a
 * rate of turn there. Only that turn used to be missing, so reference-driven frames
 * were handed back undefined rather than a tangent that quietly ignored it, and the
 * fit was left to guess from the point cloud. frameRates supplies it, so every run
 * end gets its exact tangent -- which is what holds a junction G1, and what pins the
 * last curve of a chain onto the mirror plane at the tip.
 */
function runTangent(stations is array, coords is map, offsets is array, definition is map, alongRef,
    run is map, index is number)
{
    if (offsets[index] == undefined)
    {
        return undefined;
    }

    const frame = stations[index];
    const amounts = { "width" : offsets[index].width, "height" : offsets[index].height };
    const slopes = {
            "width" : offsets[index].widthSlope * coords.scales[index],
            "height" : offsets[index].heightSlope * coords.scales[index]
        };
    const rates = frameRates(stations, run, index);

    // The two are tangents to different maps, so which one applies follows exactly
    // the same test that decides which map placed the points.
    if (usesReferenceFrame(definition, alongRef))
    {
        return surfaceOffsetTangent(alongRef, frame, amounts, slopes, rates).direction;
    }

    return offsetTangent(frame, amounts, slopes, rates);
}

/**
 * How fast the offset frame's axes turn at one station, per unit length.
 *
 * Differenced from neighbouring stations rather than derived in closed form. The
 * frame is a composition of the source curve, a reference lookup and a projection;
 * differencing it is exact for whatever that composition turns out to be, costs no
 * kernel calls, assumes nothing about the axes being mutually perpendicular, and
 * stays correct if any part of the composition changes later.
 *
 * Samples are taken inwards from the run end, so they never cross an edge junction
 * or a profile break into a frame belonging to the other side. Second order where
 * the run has three stations to work with, first order where it has only two.
 *
 * @returns {map} : { "width" : dW/ds, "height" : dH/ds }
 */
function frameRates(stations is array, run is map, index is number) returns map
{
    const still = { "width" : vector(0, 0, 0) / meter, "height" : vector(0, 0, 0) / meter };
    const step = (index == run.end) ? -1 : 1;
    const one = index + step;

    if (one < run.start || one > run.end)
    {
        return still;
    }

    const here = stations[index];
    const near = stations[one];
    const h1 = stations[one].arc - stations[index].arc;

    if (abs(h1) < TOLERANCE.zeroLength * meter)
    {
        return still;
    }

    const secant = {
            "width" : (near.widthAxis - here.widthAxis) / h1,
            "height" : (near.heightAxis - here.heightAxis) / h1
        };

    const two = index + 2 * step;
    if (two < run.start || two > run.end)
    {
        return secant;
    }

    const far = stations[two];
    const h2 = stations[two].arc - stations[index].arc;

    if (abs(h2) < TOLERANCE.zeroLength * meter || abs(h2 - h1) < TOLERANCE.zeroLength * meter)
    {
        return secant;
    }

    // Three-point one-sided derivative on uneven spacing, applied to each axis.
    // Stations inside one edge are evenly spaced in arc length, but an inserted
    // crossing can break that. Written on differences from this station because the
    // stencil's own coefficient for it is identically zero.
    const c1 = h2 / (h1 * (h2 - h1));
    const c2 = h1 / (h2 * (h2 - h1));

    return {
        "width" : c1 * (near.widthAxis - here.widthAxis) - c2 * (far.widthAxis - here.widthAxis),
        "height" : c1 * (near.heightAxis - here.heightAxis) - c2 * (far.heightAxis - here.heightAxis)
    };
}

// ============================================================================
// Debug
// ============================================================================

function debugOutput(context is Context, id is Id, definition is map, sourceChain is map, profile is map,
    alongRef, stations is array, coords is map, upper is array, lower is array, placed is map, runs is array)
{
    const points = placed.points;

    if (definition.debugPrintFromChain)
    {
        println("offset edges: " ~ toString(size(sourceChain.links)) ~ " link(s), length "
            ~ toString(sourceChain.totalLength) ~ ", zero at " ~ toString(sourceChain.zeroArc));
        println("stations: " ~ toString(size(stations)) ~ ", runs: " ~ toString(size(runs)));
        printJunctions(stations);
        printCorners(runs, stations);
    }

    if (definition.debugPrintProfileChain)
    {
        println("profile: " ~ toString(size(profile.edges)) ~ " usable edge(s), "
            ~ toString(size(profile.steps)) ~ " vertical step(s), coordinate range "
            ~ fmtMM(profile.minCoord, 3, 0) ~ " to " ~ fmtMM(profile.maxCoord, 3, 0) ~ " mm");

        for (var i = 0; i < size(profile.edges); i += 1)
        {
            println("  edge " ~ toString(i) ~ ": " ~ fmtMM(profile.edges[i].minCoord, 3, 12)
                ~ " to" ~ fmtMM(profile.edges[i].maxCoord, 3, 12) ~ " mm");
        }
        for (var step in profile.steps)
        {
            println("  step at " ~ fmtMM(step, 3, 0) ~ " mm (vertical edge, offset jumps here)");
        }
        for (var back in profile.doublesBack)
        {
            println("  note: profile edge " ~ toString(back.edge) ~ " re-covers "
                ~ fmtMM(back.overlap, 3, 0) ~ " mm already covered. Two offsets exist there; "
                ~ "the one that applies is chosen by walking the profile in order.");
        }
    }

    if (definition.debugPrintAlongChain && alongRef != undefined)
    {
        const turning = alongRef.thetas[size(alongRef.thetas) - 1];

        println("reference: length " ~ toString(alongRef.chain.totalLength)
            ~ ", total turning " ~ toString(turning)
            ~ " rad, plane normal " ~ toString(alongRef.planeNormal));

        // delta earns its keep only through the turning: the coordinate it produces
        // differs from the wire's own arc length by exactly delta * theta, which is
        // zero wherever the reference is straight. Printing that product says at a
        // glance whether a delta can possibly be doing anything here.
        // thetas is a bare number, not an angle with units: it accumulates
        // curvature * arc, which cancels to unitless. Do not divide it by radian.
        println("  offset delta " ~ fmtMM(alongRef.delta, 3, 0) ~ " mm  ->  coordinate shifted "
            ~ fmtMM(-alongRef.delta * turning, 3, 0) ~ " mm at the far end");
    }

    // The curve the coordinate is actually measured along, whenever that is not the
    // selected wire itself. delta never moves an offset point -- it only re-indexes
    // the profile -- so drawing the offset curve is the only way to confirm from the
    // graphics that it landed where it was meant to.
    if (definition.debugShowReference && alongRef != undefined)
    {
        drawReferenceOffset(context, id + "debugReference", alongRef);
    }

    // Markers are batched into one feature per colour by drawDebugSegments now, but
    // a marker at every station is unreadable long before it is slow. Draw a
    // representative subset.
    const stride = debugStride(size(stations));

    if (definition.debugShowOffsetFrames)
    {
        var widthAxes = [];
        var heightAxes = [];
        for (var i = 0; i < size(stations); i += stride)
        {
            const frame = stations[i];
            widthAxes = append(widthAxes,
                { "start" : frame.origin, "end" : frame.origin + DEBUG_AXIS_LENGTH * frame.widthAxis });
            heightAxes = append(heightAxes,
                { "start" : frame.origin, "end" : frame.origin + DEBUG_AXIS_LENGTH * frame.heightAxis });
        }
        drawDebugSegments(context, id + "debugWidthAxes", widthAxes, DebugColor.GREEN);
        drawDebugSegments(context, id + "debugHeightAxes", heightAxes, DebugColor.BLUE);
    }

    if (definition.debugShowOffsets)
    {
        var reach = [];
        for (var i = 0; i < size(stations); i += stride)
        {
            if (points[i] != undefined)
            {
                reach = append(reach, { "start" : stations[i].origin, "end" : points[i] });
            }
        }
        drawDebugSegments(context, id + "debugOffsets", reach, DebugColor.MAGENTA);
    }

    if (definition.debugPrintOffsetTable)
    {
        printOffsetTable(sourceChain, stations, coords, upper, lower, placed, runs);
    }

    if (definition.debugPrintFrameTable)
    {
        printFrameTable(definition, sourceChain, stations, alongRef);
    }

    if (definition.debugVisualizeContinuity)
    {
        for (var run in runs)
        {
            addDebugPoint(context, points[run.start], DebugColor.RED);
            addDebugPoint(context, points[run.end], DebugColor.YELLOW);
        }
    }
}

/**
 * Draw the delta-offset reference as a magenta polyline.
 *
 * Drawn whenever the delta is nonzero, without a debug flag of its own: a delta is
 * a deliberate act, and the curve it names is invisible otherwise -- it is neither
 * the wire the user picked nor anything that appears in the output.
 *
 * Strided like the frame markers: a polyline needs only enough points to show where
 * it sits. The last sample is always included, so it reaches the end of the
 * reference rather than stopping wherever the stride happened to land.
 */
function drawReferenceOffset(context is Context, id is Id, alongRef is map)
{
    const points = alongRef.points;
    const count = size(points);
    const stride = debugStride(count);
    var segments = [];
    var previous = points[0];

    for (var i = stride; i < count; i += stride)
    {
        segments = append(segments, { "start" : previous, "end" : points[i] });
        previous = points[i];
    }

    // Always finish on the last sample, so the polyline reaches the end of the
    // reference rather than stopping at whatever the stride last landed on.
    segments = append(segments, { "start" : previous, "end" : points[count - 1] });

    drawDebugSegments(context, id, segments, DebugColor.MAGENTA);
}

/**
 * What each G0 corner turned into.
 *
 * A corner shows as a gap or an overlap depending only on which side the offset went, so
 * one chain routinely produces both. This says which, and what was done about it.
 */
function printCorners(runs is array, stations is array)
{
    for (var r = 1; r < size(runs); r += 1)
    {
        const run = runs[r];
        if (run.cornerKind == undefined)
        {
            continue;
        }

        var what = "overlap trimmed to the crossing";
        if (run.cornerKind == "extended")
        {
            what = "gap extended to a sharp corner";
        }
        else if (run.cornerKind == "gap")
        {
            what = (run.fill.kind == "arc")
                ? ("gap rounded, arc R=" ~ fmtMM(run.fill.radius, 3, 0) ~ " mm")
                : "gap filled with an arc-like cubic";
        }

        println("  corner before station " ~ toString(run.start) ~ ": " ~ what);
    }
}

/**
 * What was decided at each shared vertex between two source edges.
 *
 * A break well under G1_JUNCTION_ANGLE that is still listed as a corner, or a real
 * corner listed as welded, means the threshold wants moving. A welded junction is
 * the only reason two adjacent output curves can be relied on to share an endpoint.
 */
function printJunctions(stations is array)
{
    for (var i = 0; i < size(stations); i += 1)
    {
        if (stations[i].junctionBreak == undefined)
        {
            continue;
        }

        println("  junction at station " ~ toString(i)
            ~ ": tangent break " ~ fmtNum(stations[i].junctionBreak / radian / 1e-3, 3, 0) ~ " mrad"
            ~ ", vertex gap " ~ fmtNum(stations[i].junctionGap / (1e-6 * meter), 3, 0) ~ " um  ->  "
            ~ (stations[i].welded ? "welded" : "left open"));
    }
}

/**
 * Draw many debug segments for the cost of one feature.
 *
 * std/debug.fs:429 wraps each addDebugLine in its own startFeature + newSketchOnPlane
 * + skLineSegment + skSolve + abortFeature. At this feature's stride that is a couple
 * of hundred constraint solves per regen, and it was the largest single cost here.
 *
 * addDebugEntities takes a Query, so the whole set can be built under one throwaway
 * id as degree-one B-spline curves -- no sketch and no solver at all -- and handed
 * over in one call. abortFeature then rolls the curves back while the debug entities
 * survive, which is the same contract addDebugLine itself relies on.
 *
 * @param segments {array} : each { "start" : Vector, "end" : Vector }.
 */
function drawDebugSegments(context is Context, id is Id, segments is array, color is DebugColor)
{
    var drawn = 0;

    startFeature(context, id, {});
    for (var i = 0; i < size(segments); i += 1)
    {
        // A zero-length segment has no direction for opCreateBSplineCurve to use.
        if (norm(segments[i].end - segments[i].start) > OFFSET_GEOM_TOL)
        {
            emitLineCurve(context, id + ("seg" ~ i), segments[i].start, segments[i].end);
            drawn += 1;
        }
    }
    if (drawn > 0)
    {
        addDebugEntities(context, qCreatedBy(id, EntityType.EDGE), color);
    }
    abortFeature(context, id);
}

/**
 * Draw at most DEBUG_MAX_MARKERS frames, however many stations there are.
 */
function debugStride(count is number) returns number
{
    return max(1, ceil(count / DEBUG_MAX_MARKERS));
}

/**
 * Split the stations into one index range per source edge.
 * Stations are already ordered, so a change of edge is just a change of index.
 */
function edgeBlocks(stations is array) returns array
{
    var blocks = [];
    var index = 0;

    while (index < size(stations))
    {
        var last = index;
        while (last + 1 < size(stations)
            && stations[last + 1].linkIndex == stations[index].linkIndex
            && stations[last + 1].edgeIndex == stations[index].edgeIndex)
        {
            last += 1;
        }

        blocks = append(blocks, {
                    "first" : index,
                    "last" : last,
                    "linkIndex" : stations[index].linkIndex,
                    "edgeIndex" : stations[index].edgeIndex
                });
        index = last + 1;
    }

    return blocks;
}

/**
 * Header line naming one source edge.
 */
function edgeHeading(sourceChain is map, block is map) returns string
{
    const edgeData = sourceChain.links[block.linkIndex].edges[block.edgeIndex];

    return "--- link " ~ toString(block.linkIndex) ~ " edge " ~ toString(block.edgeIndex)
        ~ "  " ~ toString(edgeData.curveType)
        ~ (edgeData.flipped ? " (reversed)" : "")
        ~ "  length " ~ fmtMM(edgeData.length, 3, 0) ~ " mm"
        ~ "  stations " ~ toString(block.last - block.first + 1);
}

/**
 * Print every station grouped by the source edge it came from.
 *
 * Columns are arc length from the zero point, the profile coordinate that arc
 * mapped to, the width and height offsets read there, the (1 - w * kappa) factor
 * (which approaches zero as the offset approaches the centre of curvature), and
 * the resulting point. Stations the profile does not reach print as "--".
 */
function printOffsetTable(sourceChain is map, stations is array, coords is map,
    upper is array, lower is array, placed is map, runs is array)
{
    println("");
    println("=== offsets: " ~ toString(size(stations)) ~ " stations, "
        ~ toString(size(runs)) ~ " output curve(s) ===");

    for (var block in edgeBlocks(stations))
    {
        println("");
        println(edgeHeading(sourceChain, block) ~ "  ->  " ~ describeRuns(runs, block.first, block.last));
        println("     i        arc      coord   pE      width     height     margin"
            ~ "          x          y          z");

        for (var i = block.first; i <= block.last; i += 1)
        {
            // The side offsetPoints actually used. Printing "upper" unconditionally
            // described one side of a crossing while its x/y/z came from the other,
            // which is precisely the row you read first when a step looks wrong.
            const offset = (stations[i].crossing == "left") ? lower[i] : upper[i];
            const point = placed.points[i];

            println(padLeft(toString(i), 6)
                ~ fmtMM(stations[i].arc, 3, 11)
                ~ fmtMM(coords.values[i], 3, 11)
                ~ fmtNum(offset == undefined ? undefined : offset.profileEdge, 0, 5)
                ~ fmtMM(offset == undefined ? undefined : offset.width, 3, 11)
                ~ fmtMM(offset == undefined ? undefined : offset.height, 3, 11)
                ~ fmtNum(placed.margins[i], 4, 11)
                ~ fmtMM(point == undefined ? undefined : point[0], 3, 11)
                ~ fmtMM(point == undefined ? undefined : point[1], 3, 11)
                ~ fmtMM(point == undefined ? undefined : point[2], 3, 11));
        }
    }

    println("");
}

/**
 * Print the frame at every station, grouped by source edge.
 *
 * The axes shown are the ones offsets are actually applied along, so this is the
 * table to read when a result leans the wrong way. Watch for the tangent flipping
 * sign inside one edge, the width and height columns swapping roles between edges,
 * or the T.H column drifting off zero.
 */
function printFrameTable(definition is map, sourceChain is map, stations is array, alongRef)
{
    println("");
    println("=== frames: alignment " ~ toString(definition.frameAlignment) ~ " ===");
    if (usesReferenceFrame(definition, alongRef))
    {
        println("height axis: normal to the reference wire; length along the offset edges"
            ~ (isConstrained(definition, alongRef) ? ", projected into the reference surface" : "")
            ~ "; width perpendicular to both");
    }
    else if (definition.frameAlignment == OffsetFrameAlignment.ALONG)
    {
        println("height axis: from the transported frame (no reference wire supplied)");
    }

    if (size(stations) > 0 && stations[0].roles != undefined)
    {
        const roles = stations[0].roles;
        println("roles fixed at the zero station: width is "
            ~ (roles.widthIsNormal ? "the curvature normal" : "the binormal")
            ~ ", width sign " ~ toString(roles.widthSign)
            ~ ", height sign " ~ toString(roles.heightSign));
    }

    if (alongRef != undefined)
    {
        println("reference plane normal: " ~ fmtVec(alongRef.planeNormal, 4, 10));
    }

    for (var block in edgeBlocks(stations))
    {
        println("");
        println(edgeHeading(sourceChain, block));
        // Widths must track the row below exactly: 6 + 11 + 3 * (2 + 3 * 10) + 10 + 10 + 11.
        println("     i        arc"
            ~ padLeft("tangent x/y/z", 32)
            ~ padLeft("width x/y/z", 32)
            ~ padLeft("height x/y/z", 32)
            ~ padLeft("kappaW", 10)
            ~ padLeft("kappaH", 10)
            ~ padLeft("T.H", 11));

        for (var i = block.first; i <= block.last; i += 1)
        {
            const frame = stations[i];

            // dot(T, W) is identically zero by construction -- W is built as
            // normalize(cross(H, T)) -- so printing it proves nothing. dot(T, H) is
            // the one that can drift: the length axis is only projected into the
            // surface when "hold length in the reference surface" is on, and an
            // unprojected T against a reference-derived H is exactly the
            // non-orthonormality that once cost 4 degrees of end tangent.
            const skew = dot(frame.tangent, frame.heightAxis);

            println(padLeft(toString(i), 6)
                ~ fmtMM(stations[i].arc, 3, 11)
                ~ "  " ~ fmtVec(frame.tangent, 4, 10)
                ~ "  " ~ fmtVec(frame.widthAxis, 4, 10)
                ~ "  " ~ fmtVec(frame.heightAxis, 4, 10)
                ~ fmtNum(frame.curvatureWidth * meter, 4, 10)
                ~ fmtNum(frame.curvatureHeight * meter, 4, 10)
                ~ fmtNum(skew, 6, 11));
        }
    }

    println("");
}

/**
 * The output curves covering a station range, as "[0..11] arc R=345.200".
 */
function describeRuns(runs is array, first is number, last is number) returns string
{
    var parts = [];

    for (var run in runs)
    {
        if (run.end < first || run.start > last)
        {
            continue;
        }

        var text = "[" ~ toString(run.start) ~ ".." ~ toString(run.end) ~ "] " ~ run.kind;
        if (run.radius != undefined)
        {
            text = text ~ " R=" ~ fmtMM(run.radius, 3, 0);
        }
        parts = append(parts, text);
    }

    return (size(parts) == 0) ? "no output (profile does not reach)" : join(parts, " | ");
}

