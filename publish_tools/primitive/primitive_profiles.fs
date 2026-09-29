FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "c5a2ade5b038e795de4b90b3");

/**
 * Export Primitive -- the profile: the volume cut by the datum XZ plane (the mid plane), split into
 *     BOTTOM    TAIL -> TIP along the running surface side (TIP / TAIL = the section's extreme points towards
 *               FCP / ACP; MRS -> FCP points to the tip),
 *     TOP       the other side, without its end caps,
 *     TIP END / TAIL END   the end caps: the edges between an extreme point and the last sharp corner within
 *               CAP_REACH of it (a tip that ends in a point has none).
 * Plus the theoretical scale factors (Table 1): region lengths along BOTTOM and along TOP, where a bottom
 * station is carried to the top along the bottom wire's normal.
 */

const SECTION_PLANE_SIZE = 20 * meter;
/** Samples per edge when looking for the section's extreme points. */
const EXTREME_SAMPLES = 33;
/** Refinement rounds (9 points each) of an extreme point. */
const EXTREME_ROUNDS = 7;
/** A corner sharper than this between two top edges may end a cap. */
const CAP_CORNER = 20 * degree;
/** Caps end within this distance (chord) of the extreme point. */
const CAP_REACH = 30 * millimeter;

/**
 * Cuts `volume` with the datum XZ plane and builds the profile wires in the LOCAL frame. Returns
 * { bottom, top, tipEnd, tailEnd (bodies, the caps may be empty), bottomChain, topChain, tip, tail (points),
 *   loops (closed loops found; only the longest is used) }.
 */
export function primitiveProfile(context is Context, id is Id, volume is Query, datum is CoordSystem,
    toLocal is Transform, isIdentity is boolean, dirSign is number, xMrs is ValueWithUnits) returns map
{
    const yAxis = cross(datum.zAxis, datum.xAxis);
    opPlane(context, id + "midPlane", { "plane" : plane(datum.origin, yAxis, datum.xAxis),
                "width" : SECTION_PLANE_SIZE, "height" : SECTION_PLANE_SIZE });
    opIntersectFaces(context, id + "section", { "tools" : qCreatedBy(id + "midPlane", EntityType.FACE), "targets" : volume });
    opDeleteBodies(context, id + "deletePlane", { "entities" : qCreatedBy(id + "midPlane", EntityType.BODY) });
    const sectionBodies = qCreatedBy(id + "section", EntityType.BODY);
    if (isQueryEmpty(context, qOwnedByBody(sectionBodies, EntityType.EDGE)))
    {
        throw regenError("The datum XZ plane (the mid plane) does not cut the volume.", ["volume"]);
    }
    primitiveMove(context, id + "sectionToLocal", sectionBodies, toLocal, isIdentity);

    // One loop, duplicates removed (opIntersectFaces returns some edges twice).
    const loop = sectionLoop(context, sectionBodies);
    const loopCount = loop.count;

    // TIP and TAIL: the loop's extreme points along the tip direction; split an edge where one falls inside it.
    const tipDir = vector(dirSign, 0, 0);
    const tipAt = extremePoint(context, loop.run, tipDir);
    const tailAt = extremePoint(context, loop.run, -tipDir);
    splitAt(context, id + "splitTip", tipAt);
    splitAt(context, id + "splitTail", tailAt);

    // Re-read the loop (split edges replaced by their halves) and cut it at TAIL and TIP.
    var run = (tipAt.interior || tailAt.interior) ? sectionLoop(context, sectionBodies).run : loop.run;
    const tail = tailAt.point;
    const tip = tipAt.point;
    const iTail = startIndex(run, tail);
    run = concatenateArrays([subArray(run, iTail, size(run)), subArray(run, 0, iTail)]);
    const iTip = startIndex(run, tip);
    const sideA = subArray(run, 0, iTip);
    const sideB = primitiveReverseRun(subArray(run, iTip, size(run)));
    if (size(sideA) == 0 || size(sideB) == 0)
    {
        throw regenError("Could not split the profile at the tip and tail.", ["volume"]);
    }

    // The bottom is the side lower at MRS.
    var bottomRun = sideA;
    var upperRun = sideB;
    if (heightAtX(context, sideB, xMrs) < heightAtX(context, sideA, xMrs))
    {
        bottomRun = sideB;
        upperRun = sideA;
    }
    const caps = capSplit(context, upperRun, tail, tip);

    const bottom = extractRun(context, id + "bottom", bottomRun);
    const top = extractRun(context, id + "top", caps.top);
    const tailEnd = extractRun(context, id + "tailEnd", caps.tailCap);
    const tipEnd = extractRun(context, id + "tipEnd", caps.tipCap);
    opDeleteBodies(context, id + "deleteSection", { "entities" : sectionBodies });

    return {
        "bottom" : bottom, "top" : top, "tipEnd" : tipEnd, "tailEnd" : tailEnd,
        "bottomChain" : primitiveChain(context, qOwnedByBody(bottom, EntityType.EDGE), tail, "Profile bottom"),
        "topChain" : primitiveChain(context, qOwnedByBody(top, EntityType.EDGE), tail, "Profile top"),
        "tip" : tip, "tail" : tail, "loops" : loopCount
    };
}

/** The section's longest closed loop, duplicates removed: { run, count }. */
function sectionLoop(context is Context, sectionBodies is Query) returns map
{
    const records = primitiveUniqueRecords(primitiveEdgeRecords(context, evaluateQuery(context, qOwnedByBody(sectionBodies, EntityType.EDGE))));
    return longestLoop(context, primitiveOrderRecords(records));
}

/** The closed run with the largest x extent; { run, count } where count = closed runs found. */
function longestLoop(context is Context, runs is array) returns map
{
    var best = undefined;
    var bestSpan = -1 * meter;
    var count = 0;
    for (var run in runs)
    {
        if (norm(run[size(run) - 1].end - run[0].start) > PRIMITIVE_CHAIN_TOLERANCE)
        {
            continue;
        }
        count += 1;
        var lo = run[0].start[0];
        var hi = lo;
        for (var link in run)
        {
            lo = min(lo, link.end[0]);
            hi = max(hi, link.end[0]);
        }
        if (hi - lo > bestSpan)
        {
            bestSpan = hi - lo;
            best = run;
        }
    }
    if (best == undefined)
    {
        throw regenError("The mid-plane section of the volume has no closed loop.", ["volume"]);
    }
    return { "run" : best, "count" : count };
}

/** The loop point farthest along `dir`: { edge, t (edge parameter), point, interior (false = at a vertex) }. */
function extremePoint(context is Context, run is array, dir is Vector) returns map
{
    var best = undefined;
    for (var link in run)
    {
        var ts = [];
        for (var i = 0; i < EXTREME_SAMPLES; i += 1)
        {
            ts = append(ts, i / (EXTREME_SAMPLES - 1));
        }
        const tls = evEdgeTangentLines(context, { "edge" : link.edge, "parameters" : ts });
        for (var i = 0; i < EXTREME_SAMPLES; i += 1)
        {
            const f = dot(tls[i].origin, dir);
            if (best == undefined || f > best.f)
            {
                best = { "edge" : link.edge, "t" : ts[i], "f" : f, "point" : tls[i].origin };
            }
        }
    }
    var lo = max(0, best.t - 1 / (EXTREME_SAMPLES - 1));
    var hi = min(1, best.t + 1 / (EXTREME_SAMPLES - 1));
    for (var round = 0; round < EXTREME_ROUNDS; round += 1)
    {
        var ts = [];
        for (var k = 0; k < 9; k += 1)
        {
            ts = append(ts, lo + (hi - lo) * k / 8);
        }
        const tls = evEdgeTangentLines(context, { "edge" : best.edge, "parameters" : ts });
        var kBest = 0;
        for (var k = 1; k < 9; k += 1)
        {
            if (dot(tls[k].origin, dir) > dot(tls[kBest].origin, dir))
            {
                kBest = k;
            }
        }
        best.t = ts[kBest];
        best.point = tls[kBest].origin;
        const step = (hi - lo) / 8;
        lo = max(0, best.t - step);
        hi = min(1, best.t + step);
    }
    const len = evLength(context, { "entities" : best.edge });
    best.interior = best.t * len > PRIMITIVE_CHAIN_TOLERANCE && (1 - best.t) * len > PRIMITIVE_CHAIN_TOLERANCE;
    if (!best.interior)
    {
        // Snap to the vertex.
        best.point = evEdgeTangentLine(context, { "edge" : best.edge, "parameter" : best.t < 0.5 ? 0 : 1 }).origin;
    }
    return best;
}

/** Splits the edge at an interior extreme point (correction 45: split at a parameter, not with a plane). */
function splitAt(context is Context, id is Id, at is map)
{
    if (at.interior)
    {
        opSplitEdges(context, id, { "edges" : at.edge, "parameters" : [[at.t]] });
    }
}

/** Index of the link that starts at `point`. */
function startIndex(run is array, point is Vector) returns number
{
    var best = 0;
    for (var i = 1; i < size(run); i += 1)
    {
        if (norm(run[i].start - point) < norm(run[best].start - point))
        {
            best = i;
        }
    }
    return best;
}

/** z where the run passes x (sampled; enough to tell the two sides apart). */
function heightAtX(context is Context, run is array, x is ValueWithUnits) returns ValueWithUnits
{
    var bestD = undefined;
    var z = 0 * meter;
    for (var link in run)
    {
        var ts = [];
        for (var i = 0; i <= 32; i += 1)
        {
            ts = append(ts, i / 32);
        }
        for (var tl in evEdgeTangentLines(context, { "edge" : link.edge, "parameters" : ts }))
        {
            const d = abs(tl.origin[0] - x);
            if (bestD == undefined || d < bestD)
            {
                bestD = d;
                z = tl.origin[2];
            }
        }
    }
    return z;
}

/**
 * Splits the upper side (TAIL -> TIP) into { tailCap, top, tipCap }: a cap runs from an extreme point to the
 * farthest sharp corner (> CAP_CORNER) within CAP_REACH of it.
 */
function capSplit(context is Context, upper is array, tail is Vector, tip is Vector) returns map
{
    const n = size(upper);
    var first = 0;       // first top link
    var last = n - 1;    // last top link
    for (var j = 0; j < n - 1; j += 1)
    {
        const out = linkTangent(context, upper[j], false);
        const into = linkTangent(context, upper[j + 1], true);
        const corner = angleBetween(out, into) > CAP_CORNER;
        const vertex = upper[j].end;
        if (corner && norm(vertex - tail) <= CAP_REACH && j + 1 > first && j + 1 <= n - 1)
        {
            first = j + 1;
        }
    }
    for (var j = n - 2; j >= 0; j -= 1)
    {
        const out = linkTangent(context, upper[j], false);
        const into = linkTangent(context, upper[j + 1], true);
        const corner = angleBetween(out, into) > CAP_CORNER;
        const vertex = upper[j].end;
        if (corner && norm(vertex - tip) <= CAP_REACH && j < last && j >= first)
        {
            last = j;
        }
    }
    return { "tailCap" : subArray(upper, 0, first), "top" : subArray(upper, first, last + 1), "tipCap" : subArray(upper, last + 1, n) };
}

/** Unit tangent of a link along the run at its start (atStart) or end. */
function linkTangent(context is Context, link is map, atStart is boolean) returns Vector
{
    var t = atStart != link.flipped ? 0 : 1;
    const tl = evEdgeTangentLine(context, { "edge" : link.edge, "parameter" : t });
    return link.flipped ? -tl.direction : tl.direction;
}

/** A wire body of the run's edges (qNothing() for an empty run). */
function extractRun(context is Context, id is Id, run is array) returns Query
{
    if (size(run) == 0)
    {
        return qNothing();
    }
    var edges = [];
    for (var link in run)
    {
        edges = append(edges, link.edge);
    }
    opExtractWires(context, id, { "edges" : qUnion(edges) });
    return qCreatedBy(id, EntityType.BODY);
}

/**
 * Table 1: { rows : [{ key, name, bottom, top, ratio }] (mm, mm, %), topFcp, topAcp (points or undefined) }.
 * Bottom lengths run along the bottom wire between FCP / ACP (their feet) and its ends; top lengths along the top
 * wire between the normal projections of those bottom stations and the top wire's ends.
 */
export function primitiveScaleFactors(context is Context, frame is map, topChain is map, aFcp is ValueWithUnits, aAcp is ValueWithUnits) returns map
{
    const bottom = frame.chain;
    const atStations = primitiveChainEvaluate(context, bottom, [aFcp, aAcp]);
    const topFcp = primitiveChainCrossing(context, topChain, atStations[0].point, primitiveUp(frame, atStations[0].tangent));
    const topAcp = primitiveChainCrossing(context, topChain, atStations[1].point, primitiveUp(frame, atStations[1].tangent));

    // The bottom chain runs TAIL -> TIP (a = 0 at TAIL); so does the top chain.
    const tipBottom = bottom.total - aFcp;
    const rsBottom = abs(aFcp - aAcp);
    const tailBottom = aAcp;
    var tipTop = undefined;
    var rsTop = undefined;
    var tailTop = undefined;
    if (topFcp != undefined)
    {
        tipTop = topChain.total - topFcp.a;
    }
    if (topAcp != undefined)
    {
        tailTop = topAcp.a;
    }
    if (topFcp != undefined && topAcp != undefined)
    {
        rsTop = abs(topFcp.a - topAcp.a);
    }
    return {
        "rows" : [
            scaleRow("tip", "Tip length", tipBottom, tipTop),
            scaleRow("runningSurface", "Running surface length", rsBottom, rsTop),
            scaleRow("tail", "Tail length", tailBottom, tailTop)
        ],
        "topFcp" : topFcp == undefined ? undefined : topFcp.point,
        "topAcp" : topAcp == undefined ? undefined : topAcp.point
    };
}

function scaleRow(key is string, name is string, bottom is ValueWithUnits, top) returns map
{
    if (top == undefined)
    {
        return { "key" : key, "name" : name, "bottom" : primitiveMM(bottom), "top" : PRIMITIVE_NOT_FOUND, "ratio" : PRIMITIVE_NOT_FOUND };
    }
    return { "key" : key, "name" : name, "bottom" : primitiveMM(bottom), "top" : primitiveMM(top),
            "ratio" : bottom > 1e-9 * meter ? primitiveRound(top / bottom * 100, 4) : PRIMITIVE_NOT_FOUND };
}
