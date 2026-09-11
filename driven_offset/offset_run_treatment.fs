FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

import(path : "a2665e22c07b7a6929ce4e80", version : "56b6aa6cf26a858344eb3ff0");

/**
 * Treatment of the ends and joints of an offset run.
 *
 * Everything here takes the run array produced by buildRuns and returns a modified
 * one: corners where the source turns through a G0 vertex, and terminals where the
 * chain meets a plane it was told to stop on. Both work through the same channel --
 * a run's start / end station plus an exact startPoint / endPoint or a fill curve --
 * so the emitter needs no knowledge of either.
 */

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
export function resolveCorners(context is Context, definition is map, stations is array, coords is map,
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
 * Bring the two ends of the chain onto their terminal planes.
 *
 * Offsetting moves an endpoint off wherever the source ended. At a source end with tangent
 * T the offset lands at w*W + h*H from it, and any part of T that is not square to the end
 * slides that landing ALONG the direction of travel -- which is exactly the 12 um of Y
 * drift we chased at the tail, where T was 1.2 mrad off perpendicular. Declaring the
 * terminating plane is the general fix: it stops the result depending on the source tangent
 * being square, and it is what lets two mirrored halves close into one outline.
 *
 * Which treatment an end needs is never a judgement call. The signed distance from the
 * endpoint to the plane, measured along the direction the offset is heading, says whether
 * it stopped short or ran past. Short extends, past trims.
 *
 * Runs after corners, deliberately: corners are interior to the chain and terminals are its
 * extremities, so corner trimming can never move the station a terminal is measured from.
 */
export function resolveTerminals(context is Context, definition is map, stations is array, coords is map,
    points is array, upper is array, lower is array, runs is array, alongRef) returns array
{
    var resolved = runs;

    if (size(resolved) == 0)
    {
        return resolved;
    }

    if (!isQueryEmpty(context, definition.startPlane))
    {
        resolved = terminateEnd(context, definition, stations, coords, points, upper, lower,
            resolved, alongRef, true);
    }

    if (!isQueryEmpty(context, definition.endPlane))
    {
        resolved = terminateEnd(context, definition, stations, coords, points, upper, lower,
            resolved, alongRef, false);
    }

    return resolved;
}

/**
 * Decide and apply what one end of the chain needs.
 *
 * The first and last runs are the chain's extremities by construction: buildRuns walks
 * stations in order, so runs[0] opens at the lowest station and the last run closes at the
 * highest, whatever breaks the profile put in between.
 */
function terminateEnd(context is Context, definition is map, stations is array, coords is map,
    points is array, upper is array, lower is array, runs is array, alongRef,
    atStart is boolean) returns array
{
    var resolved = runs;

    const index = atStart ? 0 : size(resolved) - 1;
    const run = resolved[index];
    const station = atStart ? run.start : run.end;

    // Three stations are the minimum: the terminal, and two more to read a curvature from.
    if (run.end - run.start < 2 || points[station] == undefined)
    {
        println("WARNING: the " ~ endName(atStart) ~ " run is too short to terminate on a plane.");
        return resolved;
    }

    const pl = planeFromQuery(context, atStart ? definition.startPlane : definition.endPlane);
    const terminal = points[station];

    const natural = runTangent(stations, coords, atStart ? upper : lower, definition, alongRef,
            run, station);

    if (natural == undefined)
    {
        return resolved;
    }

    // Outward, always. At the chain start the offset leaves in the direction of DECREASING
    // station, so its outward heading is the reverse of the one the fit uses there.
    const outward = (atStart ? -1 : 1) * natural;
    const reach = planeCrossingDistance(terminal, outward, pl);

    if (reach == undefined)
    {
        throw regenError("The offset runs parallel to its terminal plane and never reaches it.",
            atStart ? definition.startPlane : definition.endPlane);
    }

    // How square the SOURCE arrives is the number worth reporting: an off-perpendicular
    // source tangent is what moves the endpoint in the first place, so a large value here
    // says to fix the chain rather than to lean harder on this feature.
    const squareness = angleBetween(stations[station].tangent, pl.normal);

    if (abs(reach) < OFFSET_GEOM_TOL)
    {
        resolved[index] = withTerminalRecord(run, atStart,
            { "action" : "already on plane", "distance" : reach, "squareness" : squareness });
        return resolved;
    }

    if (reach < 0 * meter)
    {
        return trimToPlane(resolved, index, points, pl, atStart, squareness);
    }

    return extendToPlane(context, definition, resolved, index, points, pl, terminal, outward,
        atStart, squareness);
}

/**
 * Cut an end back to where it crosses its terminal plane.
 *
 * The crossing replaces the stations it supersedes rather than being added to them, through
 * the same startPoint / endPoint channel a trimmed corner uses, so the emitted curve is
 * already trimmed and lands on the plane exactly.
 *
 * The arrival tangent is left alone. A trim cannot change how the curve arrives -- forcing
 * a slope here would mean deforming offset that the profile actually determined -- so the
 * angle is reported instead of corrected.
 */
function trimToPlane(runs is array, index is number, points is array, pl is Plane,
    atStart is boolean, squareness is ValueWithUnits) returns array
{
    var resolved = runs;
    const run = resolved[index];

    const step = atStart ? 1 : -1;
    const from = atStart ? run.start : run.end;

    var kept = undefined;
    var crossing = undefined;

    for (var i = 0; i < run.end - run.start; i += 1)
    {
        const at = from + i * step;
        const next = at + step;

        if (points[at] == undefined || points[next] == undefined)
        {
            break;
        }

        crossing = planeStraddle(points[at], points[next], pl);

        if (crossing != undefined)
        {
            kept = next;
            break;
        }
    }

    if (kept == undefined)
    {
        println("WARNING: the " ~ endName(atStart) ~ " of the offset lies past its terminal"
            ~ " plane but never crosses back; left untrimmed.");
        return resolved;
    }

    // A trim that leaves nothing to fit would delete more than it repairs.
    if ((atStart ? run.end - kept : kept - run.start) < 1)
    {
        println("WARNING: the " ~ endName(atStart) ~ " of the offset lies past its terminal"
            ~ " plane by more than the run is long; left untrimmed.");
        return resolved;
    }

    resolved[index] = withTerminalRecord(
        mergeMaps(run, atStart
                ? { "start" : kept, "startPoint" : crossing }
                : { "end" : kept, "endPoint" : crossing }),
        atStart,
        {
            "action" : "trimmed",
            "distance" : norm(crossing - points[from]),
            "squareness" : squareness
        });

    return resolved;
}

/**
 * Carry an end forward to its terminal plane.
 *
 * With a direction supplied, that direction fixes both the slope and where to aim: a ray
 * from the last offset point along it. On a triangular swallowtail, where the direction is
 * the V branch the source was already running, the two coincide and the extension is the
 * straight line to the apex that the geometry actually wants.
 *
 * Without one, the run continues along its own osculating circle. A straight tangent
 * extension is the wrong tool at a rockered tip -- it leaves the arc immediately -- and the
 * osculating one costs a four-step Newton solve.
 */
function extendToPlane(context is Context, definition is map, runs is array, index is number,
    points is array, pl is Plane, terminal is Vector, outward is Vector, atStart is boolean,
    squareness is ValueWithUnits) returns array
{
    var resolved = runs;
    const run = resolved[index];

    const station = atStart ? run.start : run.end;
    const inward = atStart ? 1 : -1;

    // Curvature read back off the emitted points, so the extension continues the OFFSET
    // rather than the source it came from; the two differ by a factor of 1 / (1 - w*kappa).
    const curvature = curvatureThrough(points[station + 2 * inward], points[station + inward],
            points[station]);

    const supplied = terminalDirection(context, definition, outward, pl, atStart);

    var landing = undefined;
    var arrival = undefined;

    if (supplied != undefined)
    {
        landing = terminal + planeCrossingDistance(terminal, supplied, pl) * supplied;
        arrival = supplied;
    }
    else
    {
        const crossing = osculatingCrossing(terminal, outward, curvature, pl);

        if (crossing == undefined)
        {
            return resolved;
        }

        landing = crossing.point;
        arrival = crossing.tangent;
    }

    const span = norm(landing - terminal);

    if (!definition.allowExtension)
    {
        println("NOTE: the " ~ endName(atStart) ~ " of the offset stops " ~ fmtMM(span, 4, 0)
            ~ " mm short of its terminal plane; extension is switched off.");
        return resolved;
    }

    const budget = TERMINAL_MAX_EXTENSION * runLength(points, run);

    if (span > budget)
    {
        println("WARNING: the " ~ endName(atStart) ~ " of the offset would extend "
            ~ fmtMM(span, 3, 0) ~ " mm to reach its terminal plane, past the "
            ~ fmtMM(budget, 3, 0) ~ " mm allowed for a run this long; left alone.");
        return resolved;
    }

    // Built outward at both ends. A wire body carries no preferred direction, so reversing
    // it at the start would be ceremony. Where the arrival matches the run's own heading
    // this degenerates to the straight line it should be.
    const extension = arcLikeSpline(terminal, outward, landing, arrival);

    resolved[index] = withTerminalRecord(
        mergeMaps(run, atStart ? { "startExtension" : extension } : { "endExtension" : extension }),
        atStart,
        {
            "action" : (supplied != undefined) ? "extended along direction" : "extended",
            "distance" : span,
            "kink" : angleBetween(outward, arrival),
            "squareness" : squareness
        });

    return resolved;
}

/**
 * The slope an end should have where it meets its plane, or undefined if none was given.
 *
 * The sign comes from the offset's own heading rather than from the selection, so a mate
 * connector axis works whichever way round it was placed. That does mean an arrival more
 * than 90 degrees from the run's heading cannot be asked for -- which would be the offset
 * doubling back on itself at the terminal, and is not a thing anyone wants.
 */
function terminalDirection(context is Context, definition is map, outward is Vector, pl is Plane,
    atStart is boolean)
{
    const query = atStart ? definition.startDirection : definition.endDirection;

    if (isQueryEmpty(context, query))
    {
        return undefined;
    }

    // extractDirection, not evAxis: the filter admits planar faces and mate connectors as
    // well as axes, and only this one reads all three. evAxis alone throws on a face.
    const picked = extractDirection(context, query);

    if (picked == undefined)
    {
        throw regenError("That selection does not define a direction.", query);
    }

    const signed = (dot(picked, outward) < 0) ? -picked : picked;

    if (abs(dot(signed, pl.normal)) < ZERO_DIRECTION)
    {
        throw regenError("The terminal direction lies in the terminal plane, so the offset "
            ~ "never reaches it.", query);
    }

    return signed;
}

/**
 * Record what happened at one end, without disturbing what happened at the other.
 */
function withTerminalRecord(run is map, atStart is boolean, record is map) returns map
{
    return mergeMaps(run, atStart ? { "terminalStart" : record } : { "terminalEnd" : record });
}

/**
 * Chord length of a run, for sizing what counts as a reasonable extension.
 */
function runLength(points is array, run is map) returns ValueWithUnits
{
    var total = 0 * meter;

    for (var i = run.start + 1; i <= run.end; i += 1)
    {
        if (points[i] != undefined && points[i - 1] != undefined)
        {
            total += norm(points[i] - points[i - 1]);
        }
    }

    return total;
}

/**
 * Which end of the chain, for messages.
 */
function endName(atStart is boolean) returns string
{
    return atStart ? "start" : "end";
}
