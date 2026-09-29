FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_types.fs
export import(path : "ecde24520874030ab412c981", version : "fe5fa80592a47bebd8342270");

/**
 * Export Primitive -- frames and chains.
 *
 * Everything is measured in the LOCAL frame: the datum (see primitiveDatum; the world origin when none) is moved onto
 * the world origin, so x runs along the datum X, z is the datum Z (up) and y is across. The primitive's working
 * copies (section, outline, baseline) are transformed into that frame; the finished composite is moved back.
 *
 * A chain is an ordered run of edges: { edges, flipped, lengths, starts, total }. Its arc position `a` runs from
 * the first edge's start; the bottom wire's chain runs TAIL -> TIP (towards FCP), so s = a - a(MRS) is the
 * user's s: arc length along the bottom wire, zero at MRS, positive towards FCP.
 */

/** Edge ends closer than this are one vertex when ordering chains (m). */
export const PRIMITIVE_CHAIN_TOLERANCE = 1e-5 * meter;
/** Rows in a chain's x lookup table. */
const CHAIN_TABLE_ROWS = 200;
/** Newton steps when finding the chain point at a given x. */
const CHAIN_NEWTON_STEPS = 5;

/** World point of a vertex, point body or mate connector pick (a connector can arrive as its vertex, correction 44). */
export function primitivePoint(context is Context, pick is Query, label is string, parameter is string) returns Vector
{
    const connectors = evaluateQuery(context, qBodyType(qOwnerBody(pick), BodyType.MATE_CONNECTOR));
    if (size(connectors) > 0)
    {
        return evMateConnector(context, { "mateConnector" : connectors[0] }).origin;
    }
    const vertices = evaluateQuery(context, qEntityFilter(pick, EntityType.VERTEX));
    if (size(vertices) > 0)
    {
        return evVertexPoint(context, { "vertex" : vertices[0] });
    }
    throw regenError("Select a vertex, point or mate connector for " ~ label ~ ".", [parameter]);
}

/** World points of every vertex / point / connector in a multi-pick, in pick order. */
export function primitivePoints(context is Context, picks is Query) returns array
{
    var out = [];
    for (var entity in evaluateQuery(context, picks))
    {
        const connectors = evaluateQuery(context, qBodyType(qOwnerBody(entity), BodyType.MATE_CONNECTOR));
        if (size(connectors) > 0)
        {
            out = append(out, evMateConnector(context, { "mateConnector" : connectors[0] }).origin);
        }
        else if (!isQueryEmpty(context, qEntityFilter(entity, EntityType.VERTEX)))
        {
            out = append(out, evVertexPoint(context, { "vertex" : entity }));
        }
    }
    return out;
}

/**
 * The datum frame; X runs along the ski, Z is up, Y across, and the profile / drawing plane is the datum XZ plane.
 *     nothing picked       the world origin with world axes
 *     ORIGIN               the picked vertex / point / connector origin with WORLD axes: x is measured along world X
 *                          from the datum point (a ski's own connectors often have Z along the ski)
 *     COORDINATE_SYSTEM    the picked mate connector's own frame, axes as they are
 */
export function primitiveDatum(context is Context, datum is Query, uses is PrimitiveDatumUse) returns CoordSystem
{
    if (isQueryEmpty(context, datum))
    {
        return coordSystem(vector(0, 0, 0) * meter, vector(1, 0, 0), vector(0, 0, 1));
    }
    if (uses == PrimitiveDatumUse.ORIGIN)
    {
        return coordSystem(primitivePoint(context, datum, "the datum", "datum"), vector(1, 0, 0), vector(0, 0, 1));
    }
    const connectors = evaluateQuery(context, qBodyType(qOwnerBody(datum), BodyType.MATE_CONNECTOR));
    if (size(connectors) == 0)
    {
        throw regenError("Datum uses Coordinate system: the datum must be a mate connector.", ["datum", "datumUses"]);
    }
    return evMateConnector(context, { "mateConnector" : connectors[0] });
}

/** opTransform, skipped for the identity (datum = world). */
export function primitiveMove(context is Context, id is Id, bodies is Query, xf is Transform, isIdentity is boolean)
{
    if (!isIdentity && !isQueryEmpty(context, bodies))
    {
        opTransform(context, id, { "bodies" : bodies, "transform" : xf });
    }
}

/** { edge, p0, p1, mid } of each edge (ends and midpoint), for ordering and duplicate removal. */
export function primitiveEdgeRecords(context is Context, edges is array) returns array
{
    var out = [];
    for (var edge in edges)
    {
        const tls = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 0.5, 1] });
        out = append(out, { "edge" : edge, "p0" : tls[0].origin, "mid" : tls[1].origin, "p1" : tls[2].origin });
    }
    return out;
}

function samePoint(a is Vector, b is Vector) returns boolean
{
    return norm(a - b) < PRIMITIVE_CHAIN_TOLERANCE;
}

/** Drops edges that repeat another edge (same ends and midpoint): opIntersectFaces returns some twice. */
export function primitiveUniqueRecords(records is array) returns array
{
    var out = [];
    for (var r in records)
    {
        var duplicate = false;
        for (var kept in out)
        {
            if (samePoint(kept.mid, r.mid) &&
                ((samePoint(kept.p0, r.p0) && samePoint(kept.p1, r.p1)) || (samePoint(kept.p0, r.p1) && samePoint(kept.p1, r.p0))))
            {
                duplicate = true;
                break;
            }
        }
        if (!duplicate)
        {
            out = append(out, r);
        }
    }
    return out;
}

/**
 * Orders edge records into connected runs. Returns an array of runs; each run is an array of
 * { edge, flipped, start, end } and a run is closed when its last end meets its first start.
 */
export function primitiveOrderRecords(records is array) returns array
{
    var used = makeArray(size(records), false);
    var runs = [];
    for (var seed = 0; seed < size(records); seed += 1)
    {
        if (used[seed])
        {
            continue;
        }
        used[seed] = true;
        var run = [{ "edge" : records[seed].edge, "flipped" : false, "start" : records[seed].p0, "end" : records[seed].p1 }];
        // Grow forward from the end.
        var grew = true;
        while (grew)
        {
            grew = false;
            const tail = run[size(run) - 1].end;
            if (samePoint(tail, run[0].start) && size(run) > 1)
            {
                break;
            }
            for (var j = 0; j < size(records); j += 1)
            {
                if (used[j])
                {
                    continue;
                }
                if (samePoint(records[j].p0, tail))
                {
                    run = append(run, { "edge" : records[j].edge, "flipped" : false, "start" : records[j].p0, "end" : records[j].p1 });
                }
                else if (samePoint(records[j].p1, tail))
                {
                    run = append(run, { "edge" : records[j].edge, "flipped" : true, "start" : records[j].p1, "end" : records[j].p0 });
                }
                else
                {
                    continue;
                }
                used[j] = true;
                grew = true;
                break;
            }
        }
        // Grow backward from the start (open runs only).
        grew = !samePoint(run[size(run) - 1].end, run[0].start);
        while (grew)
        {
            grew = false;
            const head = run[0].start;
            for (var j = 0; j < size(records); j += 1)
            {
                if (used[j])
                {
                    continue;
                }
                var link = undefined;
                if (samePoint(records[j].p1, head))
                {
                    link = { "edge" : records[j].edge, "flipped" : false, "start" : records[j].p0, "end" : records[j].p1 };
                }
                else if (samePoint(records[j].p0, head))
                {
                    link = { "edge" : records[j].edge, "flipped" : true, "start" : records[j].p1, "end" : records[j].p0 };
                }
                if (link != undefined)
                {
                    run = concatenateArrays([[link], run]);
                    used[j] = true;
                    grew = true;
                    break;
                }
            }
        }
        runs = append(runs, run);
    }
    return runs;
}

/** A run reversed: last edge first, every edge flipped. */
export function primitiveReverseRun(run is array) returns array
{
    var out = [];
    for (var i = size(run) - 1; i >= 0; i -= 1)
    {
        out = append(out, { "edge" : run[i].edge, "flipped" : !run[i].flipped, "start" : run[i].end, "end" : run[i].start });
    }
    return out;
}

/** A chain (with lengths and arc positions) from an ordered run. */
export function primitiveChainFromRun(context is Context, run is array) returns map
{
    var edges = [];
    var flipped = [];
    var lengths = [];
    var starts = [];
    var total = 0 * meter;
    for (var link in run)
    {
        const len = evLength(context, { "entities" : link.edge });
        edges = append(edges, link.edge);
        flipped = append(flipped, link.flipped);
        lengths = append(lengths, len);
        starts = append(starts, total);
        total += len;
    }
    return { "edges" : edges, "flipped" : flipped, "lengths" : lengths, "starts" : starts, "total" : total,
            "startPoint" : run[0].start, "endPoint" : run[size(run) - 1].end };
}

/**
 * A chain of `edges` (one connected open run), oriented so it starts at the end nearer `startNear`.
 */
export function primitiveChain(context is Context, edges is Query, startNear is Vector, label is string) returns map
{
    const runs = primitiveOrderRecords(primitiveEdgeRecords(context, evaluateQuery(context, edges)));
    if (size(runs) != 1)
    {
        throw regenError(label ~ ": the edges must form one connected chain (found " ~ size(runs) ~ " pieces).");
    }
    var run = runs[0];
    if (norm(run[size(run) - 1].end - startNear) < norm(run[0].start - startNear))
    {
        run = primitiveReverseRun(run);
    }
    return primitiveChainFromRun(context, run);
}

/** The edge index and the edge's own arc-length parameter at arc position `a` (clamped to the chain). */
function chainLocate(chain is map, a is ValueWithUnits) returns map
{
    var i = 0;
    for (var k = 0; k < size(chain.edges); k += 1)
    {
        if (chain.starts[k] <= a)
        {
            i = k;
        }
    }
    var t = (a - chain.starts[i]) / chain.lengths[i];
    t = max(0, min(1, t));
    return { "i" : i, "t" : chain.flipped[i] ? 1 - t : t };
}

/**
 * Chain points at arc positions `arcs` (outside the chain: its end). Each result:
 * { a, point, tangent (unit, along the chain), curvature (1/length, >= 0), normal (Frenet principal normal) }.
 */
export function primitiveChainEvaluate(context is Context, chain is map, arcs is array) returns array
{
    var byEdge = {};
    for (var j = 0; j < size(arcs); j += 1)
    {
        const loc = chainLocate(chain, arcs[j]);
        var entry = byEdge[loc.i];
        if (entry == undefined)
        {
            entry = { "rows" : [], "ts" : [] };
        }
        entry.rows = append(entry.rows, j);
        entry.ts = append(entry.ts, loc.t);
        byEdge[loc.i] = entry;
    }
    var out = makeArray(size(arcs));
    for (var entry in byEdge)
    {
        const i = entry.key;
        const results = evEdgeCurvatures(context, { "edge" : chain.edges[i], "parameters" : entry.value.ts });
        for (var k = 0; k < size(results); k += 1)
        {
            const r = results[k];
            const row = entry.value.rows[k];
            out[row] = { "a" : arcs[row], "point" : r.frame.origin,
                    "tangent" : chain.flipped[i] ? -r.frame.zAxis : r.frame.zAxis,
                    "curvature" : r.curvature, "normal" : r.frame.xAxis };
        }
    }
    return out;
}

/** x of CHAIN_TABLE_ROWS + 1 evenly spaced chain points, the start for chain-at-x searches. */
export function primitiveChainTable(context is Context, chain is map) returns map
{
    var arcs = [];
    for (var i = 0; i <= CHAIN_TABLE_ROWS; i += 1)
    {
        arcs = append(arcs, chain.total * i / CHAIN_TABLE_ROWS);
    }
    var xs = [];
    for (var r in primitiveChainEvaluate(context, chain, arcs))
    {
        xs = append(xs, r.point[0]);
    }
    return { "a" : arcs, "x" : xs };
}

/**
 * The chain point at each local x in `xs` (Newton on x from the lookup table). Beyond the chain's ends the arc
 * position keeps growing along the end tangent (linear extension), so `a` stays meaningful for points just past
 * an end; `point` is then the end point.
 */
export function primitiveChainAtX(context is Context, chain is map, lookup is map, xs is array) returns array
{
    const n = size(lookup.x);
    var arcs = [];
    for (var x in xs)
    {
        var guess = undefined;
        for (var j = 0; j < n - 1; j += 1)
        {
            const d0 = lookup.x[j] - x;
            const d1 = lookup.x[j + 1] - x;
            if ((d0 <= 0 * meter && d1 >= 0 * meter) || (d0 >= 0 * meter && d1 <= 0 * meter))
            {
                const span = lookup.x[j + 1] - lookup.x[j];
                const f = abs(span) > 1e-12 * meter ? (x - lookup.x[j]) / span : 0;
                guess = lookup.a[j] + (lookup.a[j + 1] - lookup.a[j]) * f;
                break;
            }
        }
        if (guess == undefined)
        {
            guess = abs(lookup.x[0] - x) < abs(lookup.x[n - 1] - x) ? lookup.a[0] : lookup.a[n - 1];
        }
        arcs = append(arcs, guess);
    }
    if (size(arcs) == 0)
    {
        return [];
    }
    var results = [];
    for (var step = 0; step < CHAIN_NEWTON_STEPS; step += 1)
    {
        results = primitiveChainEvaluate(context, chain, arcs);
        for (var j = 0; j < size(arcs); j += 1)
        {
            const tx = results[j].tangent[0];
            if (abs(tx) > 1e-9)
            {
                arcs[j] = arcs[j] + (xs[j] - results[j].point[0]) / tx;
            }
        }
    }
    results = primitiveChainEvaluate(context, chain, arcs);
    for (var j = 0; j < size(arcs); j += 1)
    {
        results[j].a = arcs[j];
    }
    return results;
}

/** The chain point closest to `point`, with its arc position. */
export function primitiveChainFoot(context is Context, chain is map, point is Vector) returns map
{
    const d = evDistance(context, { "side0" : point, "side1" : qUnion(chain.edges) });
    const i = d.sides[1].index;
    const t = d.sides[1].parameter;
    const a = chain.starts[i] + (chain.flipped[i] ? 1 - t : t) * chain.lengths[i];
    return primitiveChainEvaluate(context, chain, [a])[0];
}

/**
 * Where the line through `origin` along `direction` meets the chain: { point, a }, or undefined when it misses.
 */
export function primitiveChainCrossing(context is Context, chain is map, origin is Vector, direction is Vector)
{
    const d = evDistance(context, { "side0" : line(origin, direction), "side1" : qUnion(chain.edges) });
    if (d.distance > 1e-6 * meter)
    {
        return undefined;
    }
    const i = d.sides[1].index;
    const t = d.sides[1].parameter;
    return { "point" : d.sides[1].point, "a" : chain.starts[i] + (chain.flipped[i] ? 1 - t : t) * chain.lengths[i] };
}

/**
 * The measuring frame on the bottom wire: its chain (TAIL -> TIP) and lookup table, where MRS sits on it (aMrs),
 * where x = 0 (the datum) sits on it (aZero; beyond an end of the wire the arc position runs on along that end's
 * tangent), the tip direction (dirSign = +1 when FCP has the larger x) and upSign, which turns cross(tangent, y)
 * into the normal pointing into the ski.
 */
export function primitiveFrame(context is Context, chain is map, mrs is Vector, dirSign is number) returns map
{
    const lookup = primitiveChainTable(context, chain);
    const at = primitiveChainAtX(context, chain, lookup, [mrs[0], 0 * meter]);
    const n0 = cross(at[0].tangent, vector(0, 1, 0));
    return { "chain" : chain, "lookup" : lookup, "aMrs" : at[0].a, "aZero" : at[1].a, "xMrs" : mrs[0], "dirSign" : dirSign,
            "upSign" : n0[2] >= 0 ? 1 : -1 };
}

/** The in-plane normal into the ski at a bottom point with unit `tangent`. */
export function primitiveUp(frame is map, tangent is Vector) returns Vector
{
    const n = cross(tangent, vector(0, 1, 0)) * frame.upSign;
    return norm(n) > 1e-12 ? normalize(n) : vector(0, 0, 1);
}

/**
 * s of a bottom arc position: the signed arc length along the bottom wire from the point at x = 0 (the datum),
 * increasing with x (ds/dx > 0) whichever way the tip points. The chain runs TAIL -> TIP, i.e. with x when dirSign > 0.
 */
export function primitiveS(frame is map, a is ValueWithUnits) returns ValueWithUnits
{
    return frame.dirSign * (a - frame.aZero);
}

/** s (as primitiveS) of an unwrapped length coordinate u (primitiveU). */
export function primitiveSFromU(frame is map, u is ValueWithUnits) returns ValueWithUnits
{
    return u - frame.xMrs + frame.dirSign * (frame.aMrs - frame.aZero);
}

/** The unwrapped length coordinate: x of MRS plus s towards FCP (u = x wherever the bottom wire is flat and straight). */
export function primitiveU(frame is map, a is ValueWithUnits) returns ValueWithUnits
{
    return frame.xMrs + frame.dirSign * (a - frame.aMrs);
}

/**
 * [x, y, z] and [s, w, h] of a local point: s at its foot on the bottom wire, w across (y), h along the bottom
 * wire's normal into the ski.
 */
export function primitiveLocate(context is Context, frame is map, point is Vector) returns map
{
    const foot = primitiveChainFoot(context, frame.chain, point);
    const n = primitiveUp(frame, foot.tangent);
    return { "x" : point[0], "y" : point[1], "z" : point[2], "s" : primitiveS(frame, foot.a), "w" : point[1],
            "h" : dot(point - foot.point, n), "foot" : foot.point, "a" : foot.a };
}
