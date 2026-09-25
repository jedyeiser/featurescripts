FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// IMPORT: driven_edge_offset.fs (same document; export-imports edge_offset_utils -> curve_core)
export import(path : "786f62f4d67ed8d9c7d56d16", version : "");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/f4f872fe20d1498201fed64d/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

/**
 * Evaluate offset: Driven edge offset run backwards. Design: research_evaluate_offset.md.
 *
 * Given the edges an offset is measured FROM (reference edges) and the edges it is measured TO
 * (target edges), build the offset profile -- X station, Y width, Z height, the wire Driven edge
 * offset reads -- that would regenerate the target from the reference with the same settings.
 *
 * Every setting that decides what an offset MEANS is Driven edge offset's own, under the same
 * parameter keys: measure along, offset alignment, zero point, spacing. The stations are the ones
 * Driven edge offset samples, with the same transported frames, so a profile measured here and
 * fed back reproduces the target at those stations up to the fit tolerance.
 *
 * At each station the offset lies in the station's section plane (normal = the frame's length
 * axis: the chain tangent for Along, world X for World). The target is cut by that plane and the
 * cut point read in the frame: width = along the width axis, height = along the height axis.
 * The cut is pure math on the target's B-splines -- a bracket from coarse samples, then a
 * safeguarded Newton, batched per target edge -- so there is no kernel call per station.
 *
 * Every target vertex gets an exact station pair, so a corner in the target is a corner in the
 * profile rather than a rounded sample. The profile BREAKS (one wire per continuous piece) where
 * the target does not reach the section plane, and where two stations at one coordinate see
 * different offsets (a step). It starts and stops where the target does.
 *
 * G0 corners in the reference: outside the turn the forward offset fills the corner, and the fill
 * lies between the two halves' planes where no station looks -- nothing to do. Inside the turn it
 * trims both sides back to where they cross; the stations there cannot see their own (trimmed)
 * target and would cut the OTHER side's, so such cuts are rejected (inCornerOverlap) and the run's
 * fit bridges the gap. The forward offset trims the bridged stretch away again.
 *
 * Not in v1: Measure along = Reference wire (the placement there is a walk in the reference
 * surface, not a plane; its inverse is a different cut -- see the design note).
 */

/** Target samples per control point when bracketing the section planes. */
const EVAL_SAMPLES_PER_CP = 4;
const EVAL_MIN_SAMPLES = 32;
const EVAL_MAX_SAMPLES = 400;

/** Safeguarded Newton passes for the target cut, and for a target vertex's coordinate. */
const EVAL_CUT_ITERATIONS = 8;
const EVAL_VERTEX_ITERATIONS = 4;

/** A target end lying this close to a section plane is on it. */
const EVAL_END_TOL = 1e-5 * meter;

/** Two stations at one coordinate whose offsets differ by more than this are a step. */
const EVAL_STEP_TOL = 1e-5 * meter;

/** Penalty on a cut from the wrong side of a crossing pair: larger than any real offset. */
const EVAL_WRONG_SIDE = 1000 * meter;

/** Tolerance of the non-rational B-spline each target edge is evaluated through. */
const EVAL_TARGET_FIT = 1e-7;

annotation { "Feature Type Name" : "Evaluate offset",
        "Feature Type Description" : "Measure the offset between two chains as an offset profile (X station, Y width, Z height) for Driven edge offset." }
export const evaluateOffset = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        offsetMeasurePredicate(definition);

        annotation { "Name" : "Name", "Default" : "", "Description" : "Name given to the profile wires. Clear it to leave them unnamed." }
        definition.outputName is string;

        offsetReferencePredicate(definition);

        offsetAlignmentPredicate(definition);

        annotation { "Name" : "Reference edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO,
                    "Description" : "The edges the offset is measured from -- what Driven edge offset would offset." }
        definition.offsetEdges is Query;

        annotation { "Name" : "Target edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO,
                    "Description" : "The edges the offset is measured to." }
        definition.targetEdges is Query;

        offsetZeroPredicate(definition);

        annotation { "Group Name" : "Spacing & approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Offset spacing", "Description" : "How many stations to measure along each reference edge. Use the setting the Driven edge offset will use: the stations, and on a 3D chain the frames, follow it.", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : OffsetPointSpacing.CTRL_POINT }
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
                drivenOffsetApproximationPredicate(definition);
            }
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show measurements", "Default" : false, "Description" : "Draw each measured station to the target point it found" }
            definition.debugShowMeasurements is boolean;

            annotation { "Name" : "Print profile table", "Default" : false, "Description" : "Every station's coordinate, width and height, and where the profile breaks" }
            definition.debugPrintTable is boolean;
        }
    }
    {
        if (definition.measureAlong == MeasureAlong.REFERENCE_WIRE)
        {
            throw regenError("Evaluate offset does not support Measure along = Reference wire yet. Use Offset edges or World X.", ["measureAlong"]);
        }

        const targets = describeTargets(context, definition.targetEdges);
        const base = offsetStationBase(context, definition);
        const framed = offsetStationFrames(base.stations, definition, base.alongRef);

        const vertexCoords = targetVertexCoords(framed, base, definition, targets);
        const resolved = offsetStationsWithBreaks(context, definition, base, vertexCoords);
        const stations = resolved.stations;

        const cuts = cutTargets(stations, targets);
        const rows = measuredRows(stations, resolved.coords, cuts, base.zeroPoint);
        const pieces = buildPieces(stations, rows, targets);

        if (size(pieces) == 0)
        {
            throw regenError("The target edges do not cross the section plane of any reference station.", ["targetEdges"]);
        }

        if (definition.debugPrintTable)
        {
            printTable(stations, rows, pieces, vertexCoords);
        }
        if (definition.debugShowMeasurements)
        {
            drawMeasurements(context, id + "debugMeasure", stations, rows);
        }

        const built = emitPieces(context, id, definition, pieces);

        if (definition.outputName != "")
        {
            setProperty(context, {
                        "entities" : qUnion(built.wires),
                        "propertyType" : PropertyType.NAME,
                        "value" : definition.outputName
                    });
        }

        publishEvaluatedProfile(context, id, definition, built, rows);
    }, {
        "outputName" : "",
        "debugShowMeasurements" : false,
        "debugPrintTable" : false
    });

// ============================================================================
// Target edges
// ============================================================================

/**
 * Every target edge as a non-rational B-spline plus coarse samples.
 *
 * Non-rational because evaluateSpline drops the weights of a rational curve (correction 39):
 * an arc target would be cut on the wrong curve.
 *
 * @returns {array} : per edge { "query", "curve", "params", "points", "start", "end" }
 */
function describeTargets(context is Context, selection is Query) returns array
{
    const edges = evaluateQuery(context, expandEdgeQuery(selection));
    if (size(edges) == 0)
    {
        throw regenError("Select the target edges.", ["targetEdges"]);
    }

    var targets = [];
    for (var edge in edges)
    {
        const curve = evApproximateBSplineCurve(context, {
                    "edge" : edge,
                    "forceNonRational" : true,
                    "tolerance" : EVAL_TARGET_FIT
                });
        const uMin = curve.knots[0];
        const uMax = curve.knots[size(curve.knots) - 1];
        const count = min(EVAL_MAX_SAMPLES, max(EVAL_MIN_SAMPLES, EVAL_SAMPLES_PER_CP * size(curve.controlPoints)));

        var params = [];
        for (var j = 0; j < count; j += 1)
        {
            params = append(params, uMin + (uMax - uMin) * j / (count - 1));
        }
        const points = evaluateSpline({ "spline" : curve, "parameters" : params })[0];

        targets = append(targets, {
                    "query" : edge,
                    "curve" : curve,
                    "params" : params,
                    "points" : points,
                    "start" : points[0],
                    "end" : points[count - 1]
                });
    }
    return targets;
}

/**
 * Signed distance of a point ahead of a station's section plane.
 */
function sectionDistance(station is map, point is Vector) returns ValueWithUnits
{
    return dot(point - station.origin, station.tangent);
}

/**
 * +1 where the chain runs the way the section normal points, -1 where it runs against it (a
 * World frame on a chain heading -X).
 */
function chainSense(station is map) returns number
{
    return (dot(frameVelocity(station), station.tangent) < 0) ? -1 : 1;
}

/**
 * The station whose section plane the point lies at or ahead of, with the next plane ahead of the
 * point: the pair whose planes bracket it. Walked from a start index, in the chain's sense.
 *
 * By PLANES rather than by distance: under the World frame the planes are x = const and the
 * station nearest a point in space can be several stations from the one whose plane passes
 * through it. The two halves of a crossing pair share a plane, and the forward test (>=) steps
 * over them.
 */
function bracketStation(stations is array, point is Vector, start is number) returns number
{
    var k = start;
    while (k + 1 < size(stations) && sectionDistance(stations[k + 1], point) * chainSense(stations[k + 1]) >= 0 * meter)
    {
        k += 1;
    }
    while (k > 0 && sectionDistance(stations[k], point) * chainSense(stations[k]) < 0 * meter)
    {
        k -= 1;
    }
    return k;
}

function nearestStationGlobal(stations is array, point is Vector) returns number
{
    var k = 0;
    var best = norm(point - stations[0].origin);
    for (var i = 1; i < size(stations); i += 1)
    {
        const d = norm(point - stations[i].origin);
        if (d < best)
        {
            best = d;
            k = i;
        }
    }
    return k;
}

// ============================================================================
// Target vertices -> break coordinates
// ============================================================================

/**
 * Profile coordinate of every distinct target vertex that lies over the reference: the
 * coordinate of the section plane passing through it.
 *
 * Solved on the osculating model of the reference at the nearest station -- position, tangent
 * and curvature there -- which needs no kernel call and is exact to third order in the distance
 * from that station (at most half a spacing). The section normal is the chain tangent for Along
 * (turning with curvature) and world X for World (fixed).
 */
function targetVertexCoords(framed is array, base is map, definition is map, targets is array) returns array
{
    var vertices = [];
    for (var target in targets)
    {
        for (var point in [target.start, target.end])
        {
            var seen = false;
            for (var v in vertices)
            {
                if (norm(v - point) < OFFSET_GEOM_TOL)
                {
                    seen = true;
                    break;
                }
            }
            if (!seen)
            {
                vertices = append(vertices, point);
            }
        }
    }

    const world = definition.frameAlignment == OffsetFrameAlignment.WORLD;
    const values = base.coords.values;
    var lowest = values[0];
    var highest = values[0];
    for (var value in values)
    {
        lowest = min(lowest, value);
        highest = max(highest, value);
    }

    var coords = [];
    for (var vertex in vertices)
    {
        // Solve from whichever of the two bracketing stations the vertex's plane is nearer.
        var k = bracketStation(framed, vertex, nearestStationGlobal(framed, vertex));
        if (k + 1 < size(framed)
            && abs(sectionDistance(framed[k + 1], vertex)) < abs(sectionDistance(framed[k], vertex)))
        {
            k += 1;
        }
        const station = framed[k];
        const velocity = frameVelocity(station);
        const curvature = curvatureVector(station);

        // Arc offset ds from station k to the plane through the vertex.
        var ds = 0 * meter;
        for (var step = 0; step < EVAL_VERTEX_ITERATIONS; step += 1)
        {
            const origin = station.origin + ds * velocity + 0.5 * ds * ds * curvature;
            const tangentAt = velocity + ds * curvature;
            const normalAt = world ? station.tangent : normalize(tangentAt);
            const g = dot(vertex - origin, normalAt);
            var slope = -dot(tangentAt, normalAt);
            if (!world)
            {
                slope += dot(vertex - origin, curvature) / norm(tangentAt);
            }
            if (abs(slope) < 1e-9)
            {
                break;
            }
            ds = ds - g / slope;
        }

        var coord;
        if (definition.measureAlong == MeasureAlong.WORLD_X)
        {
            coord = (station.origin + ds * velocity + 0.5 * ds * ds * curvature)[0] - base.zeroPoint[0];
        }
        else
        {
            coord = values[k] + ds;
        }

        // Past either end of the reference nothing is measured; no station to put there.
        if (coord < lowest - OFFSET_GEOM_TOL || coord > highest + OFFSET_GEOM_TOL)
        {
            continue;
        }

        var duplicate = false;
        for (var c in coords)
        {
            if (abs(c - coord) < OFFSET_GEOM_TOL)
            {
                duplicate = true;
                break;
            }
        }
        if (!duplicate)
        {
            coords = append(coords, coord);
        }
    }

    return sort(coords, function(a, b)
        {
            return (a - b) / meter;
        });
}

// ============================================================================
// Cutting the target with each station's section plane
// ============================================================================

/**
 * The target point in each station's section plane, or undefined where the target does not
 * reach it. Where the plane cuts the target more than once, the cut nearest the station wins.
 *
 * @returns {array} : per station undefined or { "point", "targetIndex" }
 */
function cutTargets(stations is array, targets is array) returns array
{
    var best = makeArray(size(stations));
    var bestDistance = makeArray(size(stations));
    const guards = cornerGuards(stations);

    for (var t = 0; t < size(targets); t += 1)
    {
        const target = targets[t];
        const count = size(target.points);

        // Bracketing station per sample, by continuation along the edge.
        var nearest = [bracketStation(stations, target.points[0], nearestStationGlobal(stations, target.points[0]))];
        for (var j = 1; j < count; j += 1)
        {
            nearest = append(nearest, bracketStation(stations, target.points[j], nearest[j - 1]));
        }

        // Brackets: a sample pair whose ends lie on opposite sides of a station's plane.
        var candidates = [];
        for (var j = 0; j + 1 < count; j += 1)
        {
            const from = max(0, min(nearest[j], nearest[j + 1]) - 2);
            const to = min(size(stations) - 1, max(nearest[j], nearest[j + 1]) + 2);
            for (var k = from; k <= to; k += 1)
            {
                const g0 = sectionDistance(stations[k], target.points[j]);
                const g1 = sectionDistance(stations[k], target.points[j + 1]);
                if ((g0 < 0 * meter && g1 > 0 * meter) || (g0 > 0 * meter && g1 < 0 * meter))
                {
                    candidates = append(candidates, {
                                "station" : k,
                                "lo" : target.params[j],
                                "hi" : target.params[j + 1],
                                "gLo" : g0,
                                "u" : target.params[j] + (target.params[j + 1] - target.params[j]) * g0 / (g0 - g1)
                            });
                }
            }
        }

        // A target end lying in a plane (within EVAL_END_TOL) is a cut there. Sign tests alone
        // miss it whenever the plane passes a hair outside the edge, which is exactly where a
        // station was inserted for that end.
        for (var endIndex in [0, count - 1])
        {
            const k0 = nearest[endIndex];
            for (var k = max(0, k0 - 2); k <= min(size(stations) - 1, k0 + 2); k += 1)
            {
                if (abs(sectionDistance(stations[k], target.points[endIndex])) < EVAL_END_TOL)
                {
                    // Which way the edge runs from this end, in the chain's sense: +1 ahead of
                    // the plane, -1 behind. Where two target edges meet the plane at one
                    // coordinate -- a jump -- this is what tells the halves of the crossing pair
                    // which of the two each belongs to.
                    const inward = sectionDistance(stations[k], target.points[(endIndex == 0) ? 1 : count - 2]);
                    candidates = append(candidates, {
                                "station" : k,
                                "fixed" : true,
                                "u" : target.params[endIndex],
                                "side" : ((inward > 0 * meter) ? 1 : -1) * chainSense(stations[k])
                            });
                }
            }
        }

        const solved = solveCuts(target.curve, stations, candidates);

        for (var c = 0; c < size(candidates); c += 1)
        {
            const k = candidates[c].station;
            if (inCornerOverlap(stations, guards, k, solved[c]))
            {
                continue;
            }

            // The first half of a crossing pair ends the run behind it and wants target that
            // runs back; the second half (the head) wants target that runs on. A cut on the
            // wrong side only wins when there is nothing else.
            const want = (stations[k].crossing == undefined) ? 0 : ((stations[k].crossingHead == true) ? 1 : -1);
            const side = (candidates[c].side == undefined) ? 0 : candidates[c].side;
            const distance = norm(solved[c] - stations[k].origin)
                + ((want != 0 && side != 0 && side != want) ? EVAL_WRONG_SIDE : 0 * meter);
            if (best[k] == undefined || distance < bestDistance[k])
            {
                best[k] = { "point" : solved[c], "targetIndex" : t };
                bestDistance[k] = distance;
            }
        }
    }

    return best;
}

/**
 * For every station, the G0 reference corners either side of it: "next" the index of the half
 * that STARTS the next edge (its plane faces along that edge), "previous" the half that ENDS the
 * previous edge. -1 where there is none. Welded joints are not corners.
 */
function cornerGuards(stations is array) returns map
{
    const count = size(stations);
    var next = makeArray(count, -1);
    var previous = makeArray(count, -1);

    var ahead = -1;
    for (var k = count - 1; k >= 0; k -= 1)
    {
        next[k] = ahead;
        if (k > 0 && stations[k].junctionBreak != undefined && stations[k].welded != true)
        {
            ahead = k;
        }
    }

    var behind = -1;
    for (var k = 0; k < count; k += 1)
    {
        previous[k] = behind;
        if (k + 1 < count && stations[k + 1].junctionBreak != undefined && stations[k + 1].welded != true)
        {
            behind = k;
        }
    }

    return { "next" : next, "previous" : previous };
}

/**
 * Whether a cut for station k lies where the NEIGHBOURING edge owns the offset: ahead of the plane
 * that starts the next edge at a G0 corner, or behind the plane that ends the previous one.
 *
 * At an inside corner those half-spaces overlap, and the forward offset trims both sides back to
 * where they cross; a station short of the corner then cuts the OTHER side's target line, a
 * millimetre or more from its own offset. Such a cut is not this station's. Only corners within
 * a few offset lengths are asked: a plane is infinite, and on a chain that turns far round a
 * distant corner's plane says nothing about this station.
 */
function inCornerOverlap(stations is array, guards is map, k is number, point is Vector) returns boolean
{
    const reach = 4 * norm(point - stations[k].origin) + OFFSET_GEOM_TOL;

    const j = guards.next[k];
    if (j >= 0 && norm(stations[j].origin - stations[k].origin) <= reach
        && sectionDistance(stations[j], point) * chainSense(stations[j]) > OFFSET_GEOM_TOL)
    {
        return true;
    }

    const i = guards.previous[k];
    if (i >= 0 && norm(stations[i].origin - stations[k].origin) <= reach
        && sectionDistance(stations[i], point) * chainSense(stations[i]) < -OFFSET_GEOM_TOL)
    {
        return true;
    }

    return false;
}

/**
 * Safeguarded Newton on one target curve, all candidates at once: one evaluateSpline per pass.
 * The residual is the signed distance ahead of the candidate's section plane; a step that
 * leaves the bracket is replaced by bisection.
 *
 * @returns {array} : the cut point per candidate.
 */
function solveCuts(curve is BSplineCurve, stations is array, candidates is array) returns array
{
    if (size(candidates) == 0)
    {
        return [];
    }

    var params = [];
    var los = [];
    var his = [];
    var gLos = [];
    for (var candidate in candidates)
    {
        params = append(params, candidate.u);
        los = append(los, candidate.lo);
        his = append(his, candidate.hi);
        gLos = append(gLos, candidate.gLo);
    }

    for (var iteration = 0; iteration < EVAL_CUT_ITERATIONS; iteration += 1)
    {
        const ev = evaluateSpline({ "spline" : curve, "parameters" : params, "nDerivatives" : 1 });
        for (var i = 0; i < size(params); i += 1)
        {
            if (candidates[i].fixed == true)
            {
                continue;
            }
            const station = stations[candidates[i].station];
            const g = sectionDistance(station, ev[0][i]);

            // Keep the root bracketed: replace the end whose sign g shares.
            if ((g < 0 * meter) == (gLos[i] < 0 * meter))
            {
                los[i] = params[i];
                gLos[i] = g;
            }
            else
            {
                his[i] = params[i];
            }

            const slope = dot(ev[1][i], station.tangent);
            var next = (abs(slope) > 1e-12 * meter) ? params[i] - g / slope : undefined;
            const lower = min(los[i], his[i]);
            const upper = max(los[i], his[i]);
            if (next == undefined || next <= lower || next >= upper)
            {
                next = 0.5 * (los[i] + his[i]);
            }
            params[i] = next;
        }
    }

    return evaluateSpline({ "spline" : curve, "parameters" : params })[0];
}

// ============================================================================
// Rows and pieces
// ============================================================================

/**
 * The measured offset at every station, or undefined where there is none.
 *
 * A cut the Driven edge offset could not reproduce -- one past the reference's own centre of
 * curvature, where the offset folds back -- is dropped, as the forward feature drops it.
 *
 * @returns {array} : per station undefined or { "coord", "width", "height", "point" (profile
 *          space), "target" (world cut point), "targetIndex" }
 */
function measuredRows(stations is array, coords is map, cuts is array, zeroPoint is Vector) returns array
{
    var rows = [];
    var folded = 0;
    for (var k = 0; k < size(stations); k += 1)
    {
        if (cuts[k] == undefined)
        {
            rows = append(rows, undefined);
            continue;
        }

        const station = stations[k];
        const toTarget = cuts[k].point - station.origin;
        const offsets = {
                "width" : dot(toTarget, station.widthAxis),
                "height" : dot(toTarget, station.heightAxis)
            };

        if (offsetShrink(station, offsets) <= 0)
        {
            folded += 1;
            rows = append(rows, undefined);
            continue;
        }

        const coord = coords.values[k];
        rows = append(rows, {
                    "coord" : coord,
                    "width" : offsets.width,
                    "height" : offsets.height,
                    "point" : vector(zeroPoint[0] + coord, offsets.width, offsets.height),
                    "target" : cuts[k].point,
                    "targetIndex" : cuts[k].targetIndex
                });
    }

    if (folded > 0)
    {
        println("NOTE: evaluate offset: " ~ folded ~ " station(s) dropped -- the target lies past the reference's centre of curvature there, where Driven edge offset folds.");
    }
    return rows;
}

/**
 * Group the rows into pieces (continuous wires) of runs (one curve each).
 *
 * A gap the target runs on unbroken across is bridged by the run's own fit; any other gap breaks
 * the profile. A run ends at every inserted crossing (a target vertex, so a target corner is a
 * profile corner), where two stations at one coordinate disagree, where the target edge changes
 * without a crossing, and where the coordinate turns back. A piece ends where the next run does
 * not start on the point the last one ended on. The two halves of a welded reference joint agree
 * and read straight through.
 *
 * @returns {array} : pieces, each an array of runs, each an array of profile points.
 */
function buildPieces(stations is array, rows is array, targets is array) returns array
{
    var pieces = [];
    var piece = [];
    var run = [];
    var direction = 0;
    var previous = undefined;
    var gap = false;

    for (var k = 0; k < size(rows); k += 1)
    {
        const row = rows[k];
        if (row == undefined)
        {
            gap = previous != undefined;
            continue;
        }

        if (gap)
        {
            gap = false;

            if (targetsConnected(targets, previous.targetIndex, row.targetIndex))
            {
                // The target runs on unbroken across stations it could not be measured at -- an
                // inside corner the forward offset trimmed, or stations dropped as folded. The
                // offset there is whatever the forward feature cuts away again, so the run just
                // carries on: its fit bridges the gap with the curvature either side, where a
                // straight bridge measured 0.07 mm off at a trimmed corner.
                if (norm(row.point - run[size(run) - 1]) >= EVAL_STEP_TOL
                    && abs(row.coord - previous.coord) >= OFFSET_GEOM_TOL)
                {
                    run = append(run, row.point);
                }
                direction = 0;
                previous = row;
                continue;
            }

            piece = closeRunInto(piece, run);
            pieces = closePieceInto(pieces, piece);
            piece = [];
            previous = undefined;
        }

        if (previous == undefined)
        {
            run = [row.point];
            direction = 0;
            previous = row;
            continue;
        }

        const step = row.coord - previous.coord;
        const sameCoord = abs(step) < OFFSET_GEOM_TOL;
        const samePoint = norm(row.point - previous.point) < EVAL_STEP_TOL;
        const crossing = stations[k].crossingHead == true;

        if (sameCoord && samePoint && !crossing && stations[k].welded == true)
        {
            // Second half of a welded reference joint: nothing new here.
            previous = row;
            continue;
        }

        if (sameCoord)
        {
            // Two stations at one coordinate: a crossing pair or the halves of a reference
            // corner. Agreeing, they are a G0 joint -- the next run starts exactly where this
            // one ended. Disagreeing, the offset steps and the profile breaks.
            piece = closeRunInto(piece, run);
            if (samePoint)
            {
                run = [run[size(run) - 1]];
            }
            else
            {
                pieces = closePieceInto(pieces, piece);
                piece = [];
                run = [row.point];
            }
            direction = 0;
            previous = row;
            continue;
        }

        const newSign = (step > 0 * meter) ? 1 : -1;
        const turned = direction != 0 && newSign != direction;
        const targetChanged = row.targetIndex != previous.targetIndex;

        if (crossing || turned || targetChanged)
        {
            // A joint between stations: the new run starts on the last point of this one.
            piece = closeRunInto(piece, run);
            run = [run[size(run) - 1], row.point];
            direction = newSign;
            previous = row;
            continue;
        }

        run = append(run, row.point);
        direction = newSign;
        previous = row;
    }

    piece = closeRunInto(piece, run);
    return closePieceInto(pieces, piece);
}

/**
 * Whether two target edges are one edge or meet end to end.
 */
function targetsConnected(targets is array, a is number, b is number) returns boolean
{
    if (a == b)
    {
        return true;
    }
    for (var p in [targets[a].start, targets[a].end])
    {
        for (var q in [targets[b].start, targets[b].end])
        {
            if (norm(p - q) < EVAL_STEP_TOL)
            {
                return true;
            }
        }
    }
    return false;
}

function closeRunInto(piece is array, run is array) returns array
{
    if (size(run) < 2)
    {
        return piece;
    }
    var span = 0 * meter;
    for (var i = 1; i < size(run); i += 1)
    {
        span += norm(run[i] - run[i - 1]);
    }
    if (span < OFFSET_GEOM_TOL)
    {
        return piece;
    }
    return append(piece, run);
}

function closePieceInto(pieces is array, piece is array) returns array
{
    return (size(piece) == 0) ? pieces : append(pieces, piece);
}

// ============================================================================
// Output
// ============================================================================

/**
 * One curve per run -- a line where the run is straight within tolerance, a fit otherwise --
 * and one wire per piece.
 *
 * Arcs are not offered: a profile's axes carry different quantities, and a run that happens to
 * sit within tolerance of a circle in them is not an arc of anything.
 *
 * @returns {map} : { "wires" : array of Query, "ends" : array of { "start", "end" } per piece }
 */
function emitPieces(context is Context, id is Id, definition is map, pieces is array) returns map
{
    const approximation = {
            "approximationDegree" : definition.approximationDegree,
            "approximationTolerance" : definition.approximationTolerance,
            "approximationMaxCPs" : definition.approximationMaxCPs
        };

    var wires = [];
    var ends = [];
    for (var p = 0; p < size(pieces); p += 1)
    {
        const pieceId = id + ("piece" ~ p);
        var curves = [];
        for (var r = 0; r < size(pieces[p]); r += 1)
        {
            const points = pieces[p][r];
            const runId = pieceId + ("run" ~ r);
            const shape = (size(points) < 3)
                ? { "kind" : "line", "start" : points[0], "end" : points[size(points) - 1] }
                : classifyPoints(points, approximation.approximationTolerance, false);
            if (shape.kind == "line")
            {
                emitLineCurve(context, runId, points[0], points[size(points) - 1]);
            }
            else
            {
                emitSplineCurve(context, runId, points, undefined, undefined, approximation);
            }
            curves = append(curves, qCreatedBy(runId, EntityType.BODY));
        }

        const curveBodies = qUnion(curves);
        opExtractWires(context, pieceId + "wire", { "edges" : qOwnedByBody(curveBodies, EntityType.EDGE) });
        opDeleteBodies(context, pieceId + "deleteRuns", { "entities" : curveBodies });

        const firstRun = pieces[p][0];
        const lastRun = pieces[p][size(pieces[p]) - 1];
        wires = append(wires, qCreatedBy(pieceId + "wire", EntityType.BODY));
        ends = append(ends, { "start" : firstRun[0], "end" : lastRun[size(lastRun) - 1] });
    }

    return { "wires" : wires, "ends" : ends };
}

function publishEvaluatedProfile(context is Context, id is Id, definition is map, built is map, rows is array)
{
    const wires = built.wires;
    const ends = built.ends;

    var measured = 0;
    for (var row in rows)
    {
        if (row != undefined)
        {
            measured += 1;
        }
    }

    var breakStations = [];
    var breakVertices = [];
    const vertexAt = function(wire is Query, point is Vector) returns Query
        {
            return qClosestTo(qOwnedByBody(wire, EntityType.VERTEX), point);
        };
    for (var k = 0; k + 1 < size(ends); k += 1)
    {
        breakStations = append(breakStations, 0.5 * (ends[k].end[0] + ends[k + 1].start[0]));
        breakVertices = append(breakVertices, vertexAt(wires[k], ends[k].end));
        breakVertices = append(breakVertices, vertexAt(wires[k + 1], ends[k + 1].start));
    }

    const first = ends[0];
    const last = ends[size(ends) - 1];
    var queries = {
        "startVertex" : extractableQuery(vertexAt(wires[0], first.start), "Where the profile starts (first reference station measured).", DebugColor.GREEN),
        "endVertex" : extractableQuery(vertexAt(wires[size(wires) - 1], last.end), "Where the profile ends (last reference station measured).", DebugColor.RED),
        "breakVertices" : extractableQuery(qUnion(breakVertices), "The piece ends on both sides of every break.", DebugColor.MAGENTA)
    };
    for (var k = 0; k < size(wires); k += 1)
    {
        queries["piece" ~ (k + 1)] = extractableQuery(wires[k], "Piece " ~ (k + 1) ~ " of the profile, in reference-chain order.", DebugColor.CYAN);
    }

    embedStandardOutputs(context, id, {
                "output" : qUnion(wires),
                "outputDescription" : "The measured offset profile pieces (X station, Y width, Z height)",
                "inputs" : qUnion([definition.offsetEdges, definition.targetEdges]),
                "variables" : {
                    "pieceCount" : extractableVariable(size(wires), "Continuous pieces of the profile."),
                    "breakCount" : extractableVariable(size(wires) - 1, "Breaks between pieces."),
                    "breakStations" : extractableVariable(breakStations, "Station of each break (the middle of a jump or a gap)."),
                    "startStation" : extractableVariable(first.start[0], "Station where the profile starts."),
                    "endStation" : extractableVariable(last.end[0], "Station where the profile ends."),
                    "measuredStations" : extractableVariable(measured, "Reference stations at which the target was found."),
                    "stationCount" : extractableVariable(size(rows), "Reference stations sampled.")
                },
                "queries" : queries
            });
}

// ============================================================================
// Debug
// ============================================================================

function printTable(stations is array, rows is array, pieces is array, vertexCoords is array)
{
    println("[evaluate offset] " ~ size(stations) ~ " stations, " ~ size(pieces) ~ " piece(s), "
        ~ size(vertexCoords) ~ " target vertex station(s)");
    for (var c in vertexCoords)
    {
        println("    target vertex at coord " ~ fmtMM(c, 4, 0) ~ " mm");
    }
    println("     k    coord mm    width mm   height mm  target  flags");
    for (var k = 0; k < size(stations); k += 1)
    {
        var flags = "";
        if (stations[k].crossing != undefined)
        {
            flags = flags ~ " crossing-" ~ stations[k].crossing;
        }
        if (stations[k].junctionBreak != undefined)
        {
            flags = flags ~ " junction";
        }
        if (stations[k].welded == true)
        {
            flags = flags ~ " welded";
        }
        if (rows[k] == undefined)
        {
            println(padLeft(toString(k), 6) ~ "   (no cut)" ~ flags);
            continue;
        }
        println(padLeft(toString(k), 6) ~ fmtMM(rows[k].coord, 4, 12) ~ fmtMM(rows[k].width, 4, 12)
            ~ fmtMM(rows[k].height, 4, 12) ~ padLeft(toString(rows[k].targetIndex), 8) ~ " " ~ flags);
    }
    for (var p = 0; p < size(pieces); p += 1)
    {
        println("    piece " ~ (p + 1) ~ ": " ~ size(pieces[p]) ~ " run(s)");
    }
}

/**
 * Station -> cut segments, at most DEBUG_MAX_MARKERS of them, in one debug scope.
 */
function drawMeasurements(context is Context, id is Id, stations is array, rows is array)
{
    var measured = [];
    for (var k = 0; k < size(rows); k += 1)
    {
        if (rows[k] != undefined)
        {
            measured = append(measured, k);
        }
    }
    const stride = max(1, ceil(size(measured) / DEBUG_MAX_MARKERS));

    var drawn = 0;
    startFeature(context, id, {});
    for (var i = 0; i < size(measured); i += stride)
    {
        const k = measured[i];
        if (norm(rows[k].target - stations[k].origin) > OFFSET_GEOM_TOL)
        {
            emitLineCurve(context, id + ("segment" ~ i), stations[k].origin, rows[k].target);
            drawn += 1;
        }
    }
    if (drawn > 0)
    {
        addDebugEntities(context, qCreatedBy(id, EntityType.EDGE), DebugColor.MAGENTA);
    }
    abortFeature(context, id);
}
