FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/path.fs", version : "3095.0");

/**
 * Mate connector tools: shared helpers.
 *
 * References (a vertex, a mate connector, an edge, a face, a body all work as "points" and "directions"), wire paths that
 * tolerate micro-gaps, and arc-length work along a path. Used by Multi mate connector and Mate connector +.
 */

/** The mate connector body inside `q` (a connector may arrive as the body or as its origin vertex), or an empty query. */
export function mcMateConnector(context is Context, q is Query) returns Query
{
    return qBodyType(qUnion([q, qOwnerBody(q)]), BodyType.MATE_CONNECTOR);
}

/** The origin of a mate connector or the point of a vertex. */
export function mcPointOf(context is Context, q is Query, field is string) returns Vector
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("Select a point or mate connector", [field]);
    }
    const mc = mcMateConnector(context, q);
    if (!isQueryEmpty(context, mc))
    {
        return evMateConnector(context, { "mateConnector" : qNthElement(mc, 0) }).origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/**
 * A reference as something evDistance accepts: a point (Vector) for a vertex or mate connector, otherwise the query itself
 * (an edge, a face or a body).
 */
export function mcAsGeometry(context is Context, q is Query, field is string)
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("Select a reference", [field]);
    }
    if (!isQueryEmpty(context, mcMateConnector(context, q)) || !isQueryEmpty(context, qEntityFilter(q, EntityType.VERTEX)))
    {
        return mcPointOf(context, q, field);
    }
    return q;
}

/** The direction of an edge, planar face, cylinder axis, or the Z axis of a mate connector. */
export function mcDirectionOf(context is Context, q is Query, field is string) returns Vector
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("Select a direction", [field]);
    }
    const mc = mcMateConnector(context, q);
    if (!isQueryEmpty(context, mc))
    {
        return evMateConnector(context, { "mateConnector" : qNthElement(mc, 0) }).zAxis;
    }
    const d = extractDirection(context, q);
    if (d == undefined)
    {
        throw regenError("A direction cannot be taken from this selection", [field]);
    }
    return normalize(d);
}

/** The edges of a selection: the edges themselves plus every edge of a selected wire body. */
export function mcEdgesOf(context is Context, q is Query) returns Query
{
    return qUnion([qEntityFilter(q, EntityType.EDGE), qOwnedByBody(qEntityFilter(q, EntityType.BODY), EntityType.EDGE)]);
}

/**
 * One ordered path through the edges. std constructPath refuses edges whose ends miss by more than 1e-8 m and throws a bare
 * string, so it is retried with 1, 10 and 100 um before giving up with a message.
 */
export function mcPath(context is Context, edges is Query, field is string) returns Path
{
    try silent
    {
        return constructPath(context, edges);
    }
    for (var tolerance in [0.000001, 0.00001, 0.0001])
    {
        try silent
        {
            return constructPath(context, edges, { "tolerance" : tolerance * meter }).path;
        }
    }
    throw regenError("The edges of this curve do not form one continuous chain: its " ~ size(evaluateQuery(context, edges))
        ~ " edges do not meet end to end within 0.1 mm, or the curve branches or touches itself.", [field]);
}

/** The path point at a fraction (0 to 1) of its length. */
export function mcPathPoint(context is Context, path is Path, fraction is number) returns Vector
{
    return evPathTangentLines(context, path, [fraction]).tangentLines[0].origin;
}

/**
 * The fraction (0 to 1) of the path closest to `point`: a coarse scan, then a golden-section search around the best sample.
 * `point` should be on or near the curve.
 */
export function mcFractionOfPoint(context is Context, path is Path, point is Vector) returns number
{
    const samples = 200;
    var fractions = [];
    for (var i = 0; i <= samples; i += 1)
    {
        fractions = append(fractions, i / samples);
    }
    const lines = evPathTangentLines(context, path, fractions).tangentLines;
    var best = 0;
    var bestDistance = norm(lines[0].origin - point);
    for (var i = 1; i <= samples; i += 1)
    {
        const d = norm(lines[i].origin - point);
        if (d < bestDistance)
        {
            bestDistance = d;
            best = i;
        }
    }
    var lo = max(0, (best - 1) / samples);
    var hi = min(1, (best + 1) / samples);
    const golden = 0.6180339887498949;
    for (var k = 0; k < 40; k += 1)
    {
        const a = hi - golden * (hi - lo);
        const b = lo + golden * (hi - lo);
        const da = norm(mcPathPoint(context, path, a) - point);
        const db = norm(mcPathPoint(context, path, b) - point);
        if (da < db)
        {
            hi = b;
        }
        else
        {
            lo = a;
        }
    }
    return (lo + hi) / 2;
}
