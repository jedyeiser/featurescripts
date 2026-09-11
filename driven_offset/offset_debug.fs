FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

import(path : "a2665e22c07b7a6929ce4e80", version : "56b6aa6cf26a858344eb3ff0");

/**
 * Reporting for the driven edge offset.
 *
 * Nothing here changes geometry. debugOutput is the single entry point; everything
 * else is reached from it, gated on one of the Debug group's booleans.
 */

export function debugOutput(context is Context, id is Id, definition is map, sourceChain is map, profile is map,
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
        printTerminals(runs);
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

    if (definition.debugShowChainEnds)
    {
        drawChainEnds(context, definition, stations, coords, upper, lower, runs, points, alongRef);
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
 * Mark which end of the chain is the start and which is the end.
 *
 * Both ends of an offset look alike in the graphics, and every input in the Ends group is
 * stated relative to one or the other, so guessing wrong costs a regen to find out. The
 * arrow points OUTWARD -- the direction the offset is heading as it leaves the chain --
 * which is the sense everything downstream uses: an extension travels along it, and a
 * supplied terminal direction is flipped to agree with it.
 *
 * Drawn from the treated runs, so after a terminal trim the arrow sits on the plane rather
 * than where the offset originally stopped.
 */
function drawChainEnds(context is Context, definition is map, stations is array, coords is map,
    upper is array, lower is array, runs is array, points is array, alongRef)
{
    if (size(runs) == 0)
    {
        return;
    }

    const ends = [
            { "run" : runs[0], "atStart" : true },
            { "run" : runs[size(runs) - 1], "atStart" : false }
        ];

    for (var end in ends)
    {
        const run = end.run;
        const station = end.atStart ? run.start : run.end;
        const natural = runTangent(stations, coords, end.atStart ? upper : lower, definition,
                alongRef, run, station);

        if (natural == undefined)
        {
            continue;
        }

        // A trimmed or extended end carries its treated position; otherwise the station is
        // where the offset actually stops.
        var at = points[station];
        if (end.atStart && run.startPoint != undefined)
        {
            at = run.startPoint;
        }
        if (!end.atStart && run.endPoint != undefined)
        {
            at = run.endPoint;
        }

        if (at == undefined)
        {
            continue;
        }

        const outward = (end.atStart ? -1 : 1) * natural;

        addDebugArrow(context, at, at + DEBUG_END_ARROW * outward, DEBUG_END_ARROW_RADIUS,
            end.atStart ? DebugColor.GREEN : DebugColor.RED);

        println("chain " ~ (end.atStart ? "START (green)" : "END   (red)  ")
            ~ " at " ~ fmtMM(at[0], 3, 9) ~ ", " ~ fmtMM(at[1], 3, 9) ~ ", " ~ fmtMM(at[2], 3, 9)
            ~ " mm   heading out " ~ fmtVec(outward, 4, 9));
    }
}

/**
 * What happened at each end of the chain, and whether the source deserved the blame.
 *
 * Squareness is the angle between the SOURCE tangent and the terminal plane normal. Zero
 * means the source ended square and the offset would have landed on the plane by itself;
 * anything large means the endpoint was being dragged along the chain and this feature is
 * covering for a chain that wants fixing.
 *
 * Kink is the angle between the offset's own heading and the direction it was told to
 * arrive at. On a triangular swallowtail this should be near zero, because the V branch the
 * source runs and the direction picked for the apex are the same line. It is not zero when
 * one of the two is wrong.
 */
function printTerminals(runs is array)
{
    if (size(runs) == 0)
    {
        return;
    }

    for (var side in [["terminalStart", "start"], ["terminalEnd", "end"]])
    {
        for (var run in runs)
        {
            const record = run[side[0]];

            if (record == undefined)
            {
                continue;
            }

            var line = "  chain " ~ side[1] ~ ": " ~ record.action
                ~ " " ~ fmtMM(abs(record.distance), 4, 0) ~ " mm"
                ~ ", source " ~ fmtNum(record.squareness / degree, 2, 0) ~ " deg off square";

            if (record.kink != undefined)
            {
                line = line ~ ", arrival kink " ~ fmtNum(record.kink / degree, 2, 0) ~ " deg";
            }

            println(line);
        }
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
