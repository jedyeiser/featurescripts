FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

export import(path : "a2665e22c07b7a6929ce4e80", version : "c2271b618a87575d9a9a5e41");

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

                annotation { "Name" : "Length and width along reference", "Default" : false, "Description" : "Also take length and width from the reference wire, so those offsets slide along it. Height is always measured normal to the reference." }
                definition.constrainProfile is boolean;
            }
        }

        annotation { "Name" : "Offset edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO }
        definition.offsetEdges is Query;

        annotation { "Name" : "Offset profile", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The edges defining the offset. X maps to position along the offset edges, Y to width offset, Z to height offset" }
        definition.offsetProfile is Query;

        annotation { "Name" : "Offset alignment", "Default" : OffsetFrameAlignment.ALONG, "Description" : "How the offset frame is oriented at each point along the offset edges" }
        definition.frameAlignment is OffsetFrameAlignment;

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

            annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
            {
                curveApproximationPredicate(definition);
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
        const allStations = split.stations;
        const allCoords = split.coords;

        const upper = profileAt(profile, allCoords.values, false);
        const lower = profileAt(profile, allCoords.values, true);

        const points = offsetPoints(allStations, allCoords, upper, lower, definition, alongRef);
        const runs = buildRuns(allStations, upper);

        if (size(runs) == 0)
        {
            throw regenError("The offset profile does not reach any of the offset edges.", definition.offsetProfile);
        }

        const emitted = emitRuns(context, id, definition, allStations, allCoords, points, upper, lower, runs, alongRef);

        debugOutput(context, definition, sourceChain, profile, alongRef, allStations, allCoords, upper, points, emitted);
    });

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
 * curveApproximationPredicate only defines its fields when "Approximate" is on,
 * so fall back to the standard library's own defaults when it is off. Output
 * still has to be fitted through the offset points either way.
 */
function approximationSettings(definition is map) returns map
{
    if (definition.approximate == true)
    {
        return {
            "approximationDegree" : definition.approximationDegree,
            "approximationTolerance" : definition.approximationTolerance,
            "approximationMaxCPs" : definition.approximationMaxCPs
        };
    }

    // Bounded deliberately. approximateSpline raises its control-point count until
    // tolerance is met, so an unreachable tolerance on a long run would grind all the
    // way to MAX_CONTROL_POINTS (100). 15 matches the standard library's own UI
    // default; a user who needs more can turn Approximate on and raise it.
    return {
        "approximationDegree" : 3,
        "approximationTolerance" : 1e-5 * meter,
        "approximationMaxCPs" : 15
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
 * The frame at one station, honouring the chosen alignment.
 *
 * WORLD replaces the chain frame with the world axes; the curvatures are then
 * measured about those axes, since they drive the arc-length scaling of the offset.
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

    const referenceFrame = referenceFrameAt(alongRef, station.origin[0]);

    // Height is normal to the reference whenever a reference exists. An offset
    // stated as a height above the core bottom has to be measured from the core
    // bottom, not from whichever way the edge being offset happens to lean.
    const heightAxis = referenceFrame.heightAxis;

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

    return mergeMaps(station, {
                "tangent" : tangent,
                "widthAxis" : widthAxis,
                "heightAxis" : heightAxis,
                "curvatureWidth" : station.curvature * dot(station.rawNormal, widthAxis),
                "curvatureHeight" : station.curvature * dot(station.rawNormal, heightAxis)
            });
}

/**
 * World axes, for the WORLD alignment.
 */
function worldFrame(station is map) returns map
{
    const widthAxis = vector(0, 1, 0);
    const heightAxis = vector(0, 0, 1);

    return mergeMaps(station, {
                "tangent" : vector(1, 0, 0),
                "widthAxis" : widthAxis,
                "heightAxis" : heightAxis,
                "curvatureWidth" : station.curvature * dot(station.rawNormal, widthAxis),
                "curvatureHeight" : station.curvature * dot(station.rawNormal, heightAxis)
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
 * Whether the frame borrows anything from the reference. The reference axes rotate
 * along the path, so the closed-form offset tangent does not apply when they do.
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
function offsetPoints(stations is array, coords is map, upper is array, lower is array, definition is map, alongRef) returns array
{
    var points = [];

    for (var i = 0; i < size(stations); i += 1)
    {
        // The left half of a crossing belongs to the profile edge below it.
        const offsets = (stations[i].crossing == "left") ? lower : upper;

        if (offsets[i] == undefined)
        {
            points = append(points, undefined);
            continue;
        }

        const frame = stationFrame(stations[i], definition, alongRef);
        const tangent = offsetTangent(frame,
            { "width" : offsets[i].width, "height" : offsets[i].height },
            { "width" : offsets[i].widthSlope * coords.scales[i], "height" : offsets[i].heightSlope * coords.scales[i] });

        if (tangent.shrink <= 0)
        {
            throw regenError("The offset is larger than the radius of curvature at "
                ~ toString(roundToPrecision(stations[i].arc / millimeter, 1))
                ~ " mm from the zero point, so the result would fold back on itself. Reduce the offset there.");
        }

        points = append(points, frame.origin + offsets[i].width * frame.widthAxis + offsets[i].height * frame.heightAxis);
    }

    return points;
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

    // The curvature magnitude is inherited from the neighbouring station, but it has
    // to be re-resolved against THIS station's axes. Leaving the neighbour's values
    // would feed the fold-back guard and the shrink factor a curvature measured
    // about axes that no longer exist -- and crossings are always run endpoints.
    return mergeMaps(previous, {
                "arc" : arc,
                "origin" : tangentLine.origin,
                "tangent" : tangent,
                "normal" : normal,
                "widthAxis" : axes.widthAxis,
                "heightAxis" : axes.heightAxis,
                "curvatureWidth" : previous.curvature * dot(previous.rawNormal, axes.widthAxis),
                "curvatureHeight" : previous.curvature * dot(previous.rawNormal, axes.heightAxis)
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

        var runPoints = [];
        for (var i = run.start; i <= run.end; i += 1)
        {
            runPoints = append(runPoints, points[i]);
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
                runTangent(stations, coords, upper, definition, alongRef, run.start),
                runTangent(stations, coords, lower, definition, alongRef, run.end),
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
        opExtractWires(context, id + ("wire" ~ key), { "edges" : qUnion(bodiesByLink[key]) });
        created = concatenateArrays([created, bodiesByLink[key]]);
    }

    // The extracted wires are independent copies, so the curves they came from go.
    opDeleteBodies(context, id + "cleanup", { "entities" : qOwnerBody(qUnion(created)) });

    return emitted;
}

/**
 * Exact offset tangent at one end of a run, for the fit to interpolate.
 */
function runTangent(stations is array, coords is map, offsets is array, definition is map, alongRef, index is number)
{
    // In the constrained frame the axes rotate along the path, so the closed-form
    // offset tangent no longer applies. Rather than hand the fit a derivative that
    // is subtly wrong, let it choose its own from the points.
    if (offsets[index] == undefined || usesReferenceFrame(definition, alongRef))
    {
        return undefined;
    }

    const frame = stationFrame(stations[index], definition, alongRef);

    return offsetTangent(frame,
        { "width" : offsets[index].width, "height" : offsets[index].height },
        { "width" : offsets[index].widthSlope * coords.scales[index], "height" : offsets[index].heightSlope * coords.scales[index] }).direction;
}

// ============================================================================
// Debug
// ============================================================================

function debugOutput(context is Context, definition is map, sourceChain is map, profile is map,
    alongRef, stations is array, coords is map, offsets is array, points is array, runs is array)
{
    if (definition.debugPrintFromChain)
    {
        println("offset edges: " ~ toString(size(sourceChain.links)) ~ " link(s), length "
            ~ toString(sourceChain.totalLength) ~ ", zero at " ~ toString(sourceChain.zeroArc));
        println("stations: " ~ toString(size(stations)) ~ ", runs: " ~ toString(size(runs)));
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
        println("reference: length " ~ toString(alongRef.chain.totalLength)
            ~ ", total turning " ~ toString(alongRef.thetas[size(alongRef.thetas) - 1])
            ~ " rad, plane normal " ~ toString(alongRef.planeNormal));
    }

    // Every addDebugLine is a full sketch plus a constraint solve of its own
    // (std/debug.fs:405-417), so drawing one per station at 379 stations costs
    // hundreds of sketch solves. Draw a representative subset instead.
    const stride = debugStride(size(stations));

    if (definition.debugShowOffsetFrames)
    {
        const scale = 5 * millimeter;
        for (var i = 0; i < size(stations); i += stride)
        {
            const frame = stationFrame(stations[i], definition, alongRef);
            addDebugLine(context, frame.origin, frame.origin + scale * frame.widthAxis, DebugColor.GREEN);
            addDebugLine(context, frame.origin, frame.origin + scale * frame.heightAxis, DebugColor.BLUE);
        }
    }

    if (definition.debugShowOffsets)
    {
        for (var i = 0; i < size(stations); i += stride)
        {
            if (points[i] != undefined)
            {
                addDebugLine(context, stations[i].origin, points[i], DebugColor.MAGENTA);
            }
        }
    }

    if (definition.debugPrintOffsetTable)
    {
        printOffsetTable(definition, sourceChain, stations, coords, offsets, points, runs);
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
 * Draw at most DEBUG_MAX_MARKERS frames, however many stations there are.
 */
function debugStride(count is number) returns number
{
    return max(1, ceil(count / DEBUG_MAX_MARKERS));
}

/**
 * Walk the stations one source edge at a time, calling back with the index range.
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
function printOffsetTable(definition is map, sourceChain is map, stations is array,
    coords is map, offsets is array, points is array, runs is array)
{
    println("");
    println("=== offsets: " ~ toString(size(stations)) ~ " stations, "
        ~ toString(size(runs)) ~ " output curve(s) ===");

    for (var block in edgeBlocks(stations))
    {
        println("");
        println(edgeHeading(sourceChain, block) ~ "  ->  " ~ describeRuns(runs, block.first, block.last));
        println("     i        arc      coord   pE      width     height     shrink"
            ~ "          x          y          z");

        for (var i = block.first; i <= block.last; i += 1)
        {
            const offset = offsets[i];
            const point = points[i];

            println(padLeft(toString(i), 6)
                ~ fmtMM(stations[i].arc, 3, 11)
                ~ fmtMM(coords.values[i], 3, 11)
                ~ fmtNum(offset == undefined ? undefined : offset.profileEdge, 0, 5)
                ~ fmtMM(offset == undefined ? undefined : offset.width, 3, 11)
                ~ fmtMM(offset == undefined ? undefined : offset.height, 3, 11)
                ~ fmtNum(shrinkAt(stations[i], offset), 4, 11)
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
 * or a normal that stops being perpendicular to the tangent.
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
        println("     i        arc         tangent x/y/z             width x/y/z"
            ~ "            height x/y/z         kappaW    kappaH     perp");

        for (var i = block.first; i <= block.last; i += 1)
        {
            const frame = stationFrame(stations[i], definition, alongRef);

            // Perpendicularity check: dot(tangent, width) should be zero. A drifting
            // value means the frame is skewing, which bends the offset direction.
            const perp = dot(frame.tangent, frame.widthAxis);

            println(padLeft(toString(i), 6)
                ~ fmtMM(stations[i].arc, 3, 11)
                ~ "  " ~ fmtVec(frame.tangent, 4, 10)
                ~ "  " ~ fmtVec(frame.widthAxis, 4, 10)
                ~ "  " ~ fmtVec(frame.heightAxis, 4, 10)
                ~ fmtNum(frame.curvatureWidth * meter, 4, 10)
                ~ fmtNum(frame.curvatureHeight * meter, 4, 10)
                ~ fmtNum(perp, 6, 11));
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

/**
 * The (1 - w * kappa) factor at a station. Undefined where the profile does not reach.
 */
function shrinkAt(station is map, offset)
{
    if (offset == undefined)
    {
        return undefined;
    }

    return 1 - offset.width * station.curvatureWidth - offset.height * station.curvatureHeight;
}
