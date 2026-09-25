FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

import(path : "a2665e22c07b7a6929ce4e80", version : "904e307030d69a3967e84103");

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
    points is array, offsets is array, runs is array, alongRef) returns array
{
    // Survey every corner against the UNTOUCHED runs first. Treating them as we go would
    // not work: a trim moves run indices, so the next iteration's adjacency test and its
    // station-marker lookup would both be reading indices that have already shifted.
    var corners = [];
    var resolved = runs;

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
        if (p1 == undefined || p2 == undefined)
        {
            continue;
        }

        // A corner the offset neither opens nor crosses -- the offset is in the corner's
        // own plane, or zero -- is still a corner: the runs meet at a vertex, and a loft
        // pairing this profile against one whose offset did open it needs to know where.
        if (norm(p2 - p1) < OFFSET_GEOM_TOL)
        {
            resolved[r] = mergeMaps(resolved[r], { "cornerKind" : "closed" });
            continue;
        }

        // Exact offset tangents where they exist; the chord is a safe stand-in where the
        // profile does not reach far enough to define one.
        var t1 = runTangent(stations, coords, offsets, definition, alongRef, prev, prev.end);
        var t2 = runTangent(stations, coords, offsets, definition, alongRef, next, next.start);
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
        // Where the two tangent lines meet. Unclamped: the miter of a corner turning
        // through theta lies gap / (2 sin(theta / 2)) from each end, which for a shallow
        // corner is many gaps out -- twelve at 4.8 degrees -- and a ray clamped to one
        // gap stopped short, leaving a point off both curves that read as a 4.7 degree
        // tangent error at both ends of the run. Below MITER_MIN_TURN the lines are as
        // good as parallel and the midpoint is the corner.
        var meet = 0.5 * (p1 + p2);
        var miss = norm(p2 - p1);
        if (angleBetween(t1, t2) / radian > MITER_MIN_TURN)
        {
            const approach = lineApproach(p1, t1, p2, t2);
            if (approach.s > 0 && approach.t < 0)
            {
                meet = approach.point;
                miss = approach.distance;
            }
        }

        // The miss travels with both runs: how far the shared point lies from where each
        // curve actually goes, which decides whether an end tangent can be prescribed there.
        resolved[index - 1] = mergeMaps(resolved[index - 1], { "endPoint" : meet, "endMiss" : miss });
        resolved[index] = mergeMaps(resolved[index],
            { "startPoint" : meet, "startMiss" : miss, "cornerKind" : "extended" });

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

    resolved[index - 1] = mergeMaps(prev, { "end" : best.i - 1, "endPoint" : best.point, "endMiss" : best.distance });
    resolved[index] = mergeMaps(next,
        { "start" : best.j + 1, "startPoint" : best.point, "startMiss" : best.distance, "cornerKind" : "trimmed" });

    return resolved;
}

/**
 * Cut the loop out of an offset that folded back on itself.
 *
 * offsetPoints leaves a station without a point where the offset exceeds the radius of
 * curvature, so the run breaks on either side of the fold. The two runs either side
 * overlap -- that is what a fold is -- and are trimmed back to where they cross, exactly
 * as an inside corner is; the folded stations between them are gone. A fold at the
 * chain's end has one run only and simply ends the offset early. Every fold is reported
 * as information: the result is the correct offset, and the user may still want to know
 * where their profile was tighter than the geometry.
 */
export function resolveFolds(context is Context, definition is map, stations is array, points is array,
    folded is array, runs is array) returns array
{
    var resolved = runs;
    var notes = [];

    for (var r = size(resolved) - 1; r >= 1; r -= 1)
    {
        const prev = resolved[r - 1];
        const next = resolved[r];

        // A gap between the runs made of folded stations only.
        if (next.start <= prev.end + 1 || prev.linkIndex != next.linkIndex)
        {
            continue;
        }
        var allFolded = true;
        for (var i = prev.end + 1; i < next.start; i += 1)
        {
            if (folded[i] != true)
            {
                allFolded = false;
                break;
            }
        }
        if (!allFolded)
        {
            continue;
        }

        notes = append(notes, fmtMM(stations[prev.end + 1].arc, 1, 0));
        resolved = trimCornerOverlap(mergeMaps(definition, { "cornerOverlapMode" : CornerOverlapMode.TRIM }),
            resolved, r, points);
    }

    // Folds at the chain's ends leave no pair to trim; still worth a word.
    for (var i = 0; i < size(folded); i += 1)
    {
        if (folded[i] == true && (i == 0 || i == size(folded) - 1))
        {
            notes = append(notes, fmtMM(stations[i].arc, 1, 0) ~ " (chain end)");
        }
    }

    if (size(notes) > 0)
    {
        println("NOTE: the offset exceeds the radius of curvature at " ~ join(notes, ", ")
            ~ " mm from the zero point; the folded loop was trimmed there.");
    }

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
    points is array, offsets is array, runs is array, alongRef) returns array
{
    var resolved = runs;

    if (size(resolved) == 0)
    {
        return resolved;
    }

    if (!isQueryEmpty(context, definition.startPlane))
    {
        resolved = terminateEnd(context, definition, stations, coords, points, offsets,
            resolved, alongRef, true);
    }

    if (!isQueryEmpty(context, definition.endPlane))
    {
        resolved = terminateEnd(context, definition, stations, coords, points, offsets,
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
    points is array, offsets is array, runs is array, alongRef,
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

    const natural = runTangent(stations, coords, offsets, definition, alongRef, run, station);

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
    var reachAlong = undefined;

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
        reachAlong = crossing.distance;
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

    // The extension is folded into the run rather than tacked on as a curve of its own:
    // the landing becomes the run's exact end point, the stretch between is sampled along
    // the same osculating arc (or the supplied ray) at the run's own station spacing, and
    // the arrival direction becomes the run's end tangent. The fit then runs through the
    // extension like any other part of the run and lands on the plane exactly -- one
    // curve, one edge, and a section rebuilt from the same points reaches the plane too.
    // Kept as a separate piece it was an extra vertex, an extra edge, an extra face and a
    // G1 joint, and an edge count that changed with the configuration.
    const spacing = norm(points[station] - points[station + inward]);
    const intervals = max(1, round(span / max(spacing, OFFSET_GEOM_TOL)));

    // Outward from the terminal, exclusive of both ends.
    var outwardLead = [];
    for (var k = 1; k < intervals; k += 1)
    {
        outwardLead = append(outwardLead, (supplied != undefined)
                ? terminal + (k / intervals) * (landing - terminal)
                : osculatingAt(terminal, outward, curvature, reachAlong * k / intervals));
    }

    // Stored in the direction of increasing station, with the tangent the fit uses there:
    // travel is inward at the chain start, so the arrival's heading is reversed.
    const folded = atStart
        ? { "startPoint" : landing, "startLead" : reverse(outwardLead), "startArrival" : -arrival }
        : { "endPoint" : landing, "endLead" : outwardLead, "endArrival" : arrival };

    resolved[index] = withTerminalRecord(
        mergeMaps(run, folded),
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
