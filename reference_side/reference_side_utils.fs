FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Reference side: the shared rule behind Mutual Trim+, Split+ and Offset+.
 *
 * Every built-in that keeps, removes or offsets toward "one side" names that side by a flip
 * -- a front/back or opposite-direction box read against a surface normal or a curve
 * direction. Those flip whenever an input's orientation changes upstream, which lofts,
 * offsets, mirrors and re-drawn sketches do freely. These features name the side by
 * GEOMETRY instead: a REFERENCE (a body, face, edge, vertex or mate connector) that lies on
 * the side meant. "Toward the reference" / "away from the reference" is then a statement
 * about the model, stable under any change to the inputs.
 *
 * The side of a surface a reference is on is its signed distance to that surface: positive
 * on the side the normal points to at the closest point (correction 28 -- plain distance to
 * two pieces is only a fallback; a leaning piece can reach nearer a point on the other side).
 */

/** How far the reference must be from a surface before its side counts. */
export const REFERENCE_SIDE_MARGIN = 1e-6 * meter;

/**
 * The reference as something evDistance can measure from: the entity itself, or a mate
 * connector's origin (a mate connector is a body with no geometry to measure). Undefined
 * when nothing was picked.
 */
export function referenceProbe(context is Context, reference is Query)
{
    if (isQueryEmpty(context, reference))
    {
        return undefined;
    }
    if (!isQueryEmpty(context, qBodyType(reference, BodyType.MATE_CONNECTOR)))
    {
        return evMateConnector(context, { "mateConnector" : reference }).origin;
    }
    return reference;
}

/** The faces of a query holding sheet bodies, faces, or both. */
export function facesOf(q is Query) returns Query
{
    return qUnion([qEntityFilter(q, EntityType.FACE), qOwnedByBody(qEntityFilter(q, EntityType.BODY), EntityType.FACE)]);
}

/**
 * Signed distance from a probe (a point or an entity) to a surface (faces, or bodies whose
 * faces count), positive on the side the normal points to at the closest point. Undefined
 * where the closest point has no tangent plane.
 */
export function signedSideOf(context is Context, probe, surface is Query)
{
    const faces = evaluateQuery(context, facesOf(surface));
    if (size(faces) == 0)
    {
        return undefined;
    }
    const result = evDistance(context, { "side0" : probe, "side1" : qUnion(faces) });
    const face = faces[result.sides[1].index];

    // evFaceTangentPlane throws at a degenerate parameter (a cone apex, a collapsed edge);
    // that is "no side here", which every caller handles.
    var normal = undefined;
    try silent
    {
        normal = evFaceTangentPlane(context, { "face" : face, "parameter" : result.sides[1].parameter }).normal;
    }
    catch
    {
        return undefined;
    }
    return dot(result.sides[0].point - result.sides[1].point, normal);
}

/**
 * +1 when the probe is on the side a surface's normal points to, -1 on the other side, 0
 * when it lies on the surface (within REFERENCE_SIDE_MARGIN) or no side can be read.
 */
export function sideSign(context is Context, probe, surface is Query) returns number
{
    const s = signedSideOf(context, probe, surface);
    if (s == undefined || abs(s) <= REFERENCE_SIDE_MARGIN)
    {
        return 0;
    }
    return s > 0 * meter ? 1 : -1;
}

/**
 * A point of the reference for direction tests: a mate connector's origin, else the
 * reference's point nearest to `near` (a point, or an entity).
 */
export function referencePointNear(context is Context, probe, near) returns Vector
{
    if (probe is Vector)
    {
        return probe;
    }
    return evDistance(context, { "side0" : probe, "side1" : near }).sides[0].point;
}

/**
 * The END outputs of chains of edges, named the same in every feature: for each open chain
 * in `edges`, its free vertex nearest the matching entry of `startPoints` (where that
 * chain's source starts) is the start, the other free vertex the end; each end's edge
 * goes with it. Closed chains have no ends. `startPoints` holds one point per chain, or
 * one for all.
 *
 * @returns {map} : { startVertex, endVertex, startEdge, endEdge } (queries, unions over chains)
 */
export function chainEnds(context is Context, chains is array, startPoints is array) returns map
{
    var result = { "startVertex" : [], "endVertex" : [], "startEdge" : [], "endEdge" : [] };
    for (var c = 0; c < size(chains); c += 1)
    {
        var uses = {};
        var edgeOf = {};
        var vertexOf = {};
        for (var edge in evaluateQuery(context, chains[c]))
        {
            for (var vertex in evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX)))
            {
                const key = toString(vertex);
                uses[key] = (uses[key] == undefined ? 0 : uses[key]) + 1;
                edgeOf[key] = edge;
                vertexOf[key] = vertex;
            }
        }
        var free = [];
        for (var entry in uses)
        {
            if (entry.value == 1)
            {
                free = append(free, entry.key);
            }
        }
        if (size(free) != 2)
        {
            continue;
        }
        const startPoint = startPoints[min(c, size(startPoints) - 1)];
        const d0 = norm(evVertexPoint(context, { "vertex" : vertexOf[free[0]] }) - startPoint);
        const d1 = norm(evVertexPoint(context, { "vertex" : vertexOf[free[1]] }) - startPoint);
        const s = d0 <= d1 ? free[0] : free[1];
        const e = d0 <= d1 ? free[1] : free[0];
        result.startVertex = append(result.startVertex, vertexOf[s]);
        result.endVertex = append(result.endVertex, vertexOf[e]);
        result.startEdge = append(result.startEdge, edgeOf[s]);
        result.endEdge = append(result.endEdge, edgeOf[e]);
    }
    return {
            "startVertex" : qUnion(result.startVertex),
            "endVertex" : qUnion(result.endVertex),
            "startEdge" : qUnion(result.startEdge),
            "endEdge" : qUnion(result.endEdge)
        };
}

/** A length in mm with `digits` decimals, for the console. */
export function fmtMM(value is ValueWithUnits, digits is number) returns string
{
    return toString(roundToPrecision(value / millimeter, digits));
}
