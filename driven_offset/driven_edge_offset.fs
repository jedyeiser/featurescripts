FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

export import(path : "a2665e22c07b7a6929ce4e80", version : "56b6aa6cf26a858344eb3ff0");
import(path : "d009ddf4a8dd9534fc4dc4b5", version : "cae3c4ee90fa3138a2c17652");
import(path : "6479d7fbd0ec7d11e0ae6c69", version : "cff9d5ef7f399cfb49e0ddc5");


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
            annotation { "Name" : "Where the offset gaps", "Default" : CornerGapMode.ARC, "UIHint" : UIHint.SHOW_LABEL, "Description" : "A G0 corner in the offset edges separates the two offsets by 2 * width * sin(angle/2) on the outside of the turn. Rounding uses a true circular arc centred on the corner vertex wherever one exists, and an arc-like cubic where it does not." }
            definition.cornerGapMode is CornerGapMode;

            annotation { "Name" : "Where the offset crosses", "Default" : CornerOverlapMode.TRIM, "UIHint" : UIHint.SHOW_LABEL, "Description" : "The same corner overlaps on the inside of the turn, by width * tan(angle/2) along each side. Trimming cuts both back to where they actually cross." }
            definition.cornerOverlapMode is CornerOverlapMode;
        }

        annotation { "Group Name" : "Ends", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Start plane", "Filter" : BodyType.MATE_CONNECTOR || (EntityType.FACE && GeometryType.PLANE), "MaxNumberOfPicks" : 1, "Description" : "Terminate the start of the offset on this plane. Offsetting moves an endpoint off wherever the source ended, by however far the source tangent is from square; this puts it back on a plane you choose. The offset is trimmed if it runs past and extended if it stops short." }
            definition.startPlane is Query;

            annotation { "Name" : "Start direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Optional. The slope the offset should have where it meets the start plane, for ends that arrive oblique -- a triangular swallowtail meeting the centreline, say. Left empty, the offset continues along its own curvature. Which way round the selection points does not matter." }
            definition.startDirection is Query;

            annotation { "Name" : "End plane", "Filter" : BodyType.MATE_CONNECTOR || (EntityType.FACE && GeometryType.PLANE), "MaxNumberOfPicks" : 1, "Description" : "Terminate the end of the offset on this plane. Independent of the start plane: a chain from FCP to ACP ends on two planes at different orientations." }
            definition.endPlane is Query;

            annotation { "Name" : "End direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Optional. As the start direction, for the other end." }
            definition.endDirection is Query;

            annotation { "Name" : "Allow extension", "Default" : true, "Description" : "Off, an offset that stops short of its terminal plane is left alone rather than extended. Extension fabricates geometry past where the source data stops, which is not always wanted." }
            definition.allowExtension is boolean;
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

            annotation { "Name" : "Show chain ends", "Default" : false, "Description" : "Arrow at each end of the chain pointing the way the offset is heading there: green for the start, red for the end. This is the sense the Ends group works in -- an extension travels along the arrow, and a terminal direction is flipped to agree with it." }
            definition.debugShowChainEnds is boolean;

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
        const cornered = resolveCorners(context, definition, allStations, allCoords, points,
            upper, lower, buildRuns(allStations, upper), alongRef);
        const runs = resolveTerminals(context, definition, allStations, allCoords, points,
            upper, lower, cornered, alongRef);

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
