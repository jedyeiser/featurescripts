FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
//import tools/arc_length
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");


// ---------------------------------------------------------------------------
// resolveOffsetCurve
// ---------------------------------------------------------------------------

/**
 * Offsets a reference edge along a face by the given distance and returns the
 * resulting wire body query.
 *
 * Wraps @opOffsetCurveOnFace using GEODESIC offset type so the result stays on
 * the face surface. A distance of zero is valid — the offset wire will coincide
 * with the reference edge, so bridging can start exactly at the reference edge
 * itself.
 *
 * @param context {Context} - active modeling context
 * @param id {Id} - id for this offset operation; use qCreatedBy(id, EntityType.BODY) to retrieve result
 * @param edge {Query} - reference edge to offset from; must lie on `face`
 * @param face {Query} - face on which the offset curve is computed; curve stays on this face
 * @param distance {ValueWithUnits} - offset distance (>= 0, in length units)
 * @param flip {boolean} - if true, offset in the direction opposite to the default normal-based offset direction
 * @returns {Query} - wire body created by the offset operation
 */
export function resolveOffsetCurve(context is Context, id is Id, edge is Query, face is Query,
    distance is ValueWithUnits, flip is boolean) returns Query
{
    @opOffsetCurveOnFace(context, id, {
        "edges" : edge,
        "oppositeDirection" : flip,
        "imprint" : false,
        "extend" : false,
        "distance" : distance,
        "offsetType" : OffsetCurveType.GEODESIC,
        "targets" : face,
        "roundedCorners" : false,
        "displayResults" : true
    });
    return qCreatedBy(id, EntityType.BODY);
}


// ---------------------------------------------------------------------------
// computeAutoFlip
// ---------------------------------------------------------------------------

/**
 * Determines whether side 2's tangent should be flipped so that both approach
 * vectors point toward each other across the bridge gap.
 *
 * Hermite bridging requires T₀ · T₁ < 0 for a smooth transition: the tangents
 * at the two endpoints must oppose each other (one pointing "into" the bridge,
 * the other pointing "out"). If dot(tangent1, tangent2) > 0 the vectors point
 * in the same direction (back-to-back approach), so side 2 must be flipped.
 *
 * @param tangent1 {Vector} - unit tangent direction at side 1 bridge endpoint (unitless)
 * @param tangent2 {Vector} - unit tangent direction at side 2 bridge endpoint (unitless)
 * @returns {map} - { "flip1" : boolean, "flip2" : boolean }
 *                  flip1 is always false (side 1 is taken as the reference);
 *                  flip2 is true when the dot product is positive
 */
export function computeAutoFlip(tangent1 is Vector, tangent2 is Vector) returns map
{
    return {
        "flip1" : false,
        "flip2" : dot(tangent1, tangent2) > 0
    };
}


// ---------------------------------------------------------------------------
// computeEvenlySpacedBridgeParams
// ---------------------------------------------------------------------------

/**
 * Distributes `n` bridge attachment parameters as uniformly as possible along
 * an offset edge, while guaranteeing that all `reservedParams` are included.
 *
 * Reserved params represent mandatory vertex-connecting bridges (e.g. the edge
 * endpoints at 0.0 and 1.0). Additional bridge positions are inserted using a
 * greedy largest-gap algorithm: the largest gap between existing parameters is
 * repeatedly bisected until `n` total params are produced. This minimises the
 * maximum gap between any two consecutive bridges.
 *
 * Parameters are in the edge's arc-length parameterization [0, 1] (caller should
 * use arcLengthParameterization: true when evaluating with evEdgeTangentLines).
 *
 * @param context {Context} - active modeling context (reserved for future use)
 * @param offsetEdge {Query} - the offset wire edge (used for context; not queried here)
 * @param n {number} - total number of bridge parameter positions desired (>= 1)
 * @param reservedParams {array} - parameter values that must be included (array of number in [0,1])
 * @returns {array} - sorted array of n parameter values in [0, 1]
 *
 * Algorithm: greedy largest-gap bisection — O(n·k) where k = size(reservedParams)
 */
export function computeEvenlySpacedBridgeParams(context is Context, offsetEdge is Query,
    n is number, reservedParams is array) returns array
{
    var numReserved = size(reservedParams);

    // Enforce minimum: n must be at least the number of reserved params
    if (n < numReserved)
    {
        n = numReserved;
    }

    // Seed the working list with reserved params plus the endpoints 0.0 and 1.0
    var params = [];
    var hasZero = false;
    var hasOne = false;
    for (var i = 0; i < numReserved; i += 1)
    {
        var p = reservedParams[i];
        if (p < 1e-10)
        {
            hasZero = true;
        }
        if (p > 1.0 - 1e-10)
        {
            hasOne = true;
        }
        params = append(params, p);
    }
    if (!hasZero)
    {
        params = append(params, 0.0);
    }
    if (!hasOne)
    {
        params = append(params, 1.0);
    }

    // Insertion-sort the seed params into ascending order
    params = insertionSort(params);

    // Greedy largest-gap bisection: repeatedly insert the midpoint of the largest gap
    while (size(params) < n)
    {
        // Find index of the largest gap
        var maxGap = -1.0;
        var maxIdx = 0;
        for (var i = 0; i < size(params) - 1; i += 1)
        {
            var gap = params[i + 1] - params[i];
            if (gap > maxGap)
            {
                maxGap = gap;
                maxIdx = i;
            }
        }

        // Insert midpoint of largest gap (build new array — no index mutation)
        var midpoint = (params[maxIdx] + params[maxIdx + 1]) / 2.0;
        var newParams = [];
        for (var i = 0; i <= maxIdx; i += 1)
        {
            newParams = append(newParams, params[i]);
        }
        newParams = append(newParams, midpoint);
        for (var i = maxIdx + 1; i < size(params); i += 1)
        {
            newParams = append(newParams, params[i]);
        }
        params = newParams;
    }

    return params;
}

/**
 * Sorts an array of numbers in ascending order using insertion sort.
 * Internal helper; operates in O(n²) — acceptable for small bridge counts.
 */
function insertionSort(arr is array) returns array
{
    for (var i = 1; i < size(arr); i += 1)
    {
        var key = arr[i];
        // Build a new sorted prefix by scanning backwards
        var newArr = [];
        var inserted = false;
        for (var j = 0; j < i; j += 1)
        {
            if (!inserted && key <= arr[j])
            {
                newArr = append(newArr, key);
                inserted = true;
            }
            newArr = append(newArr, arr[j]);
        }
        if (!inserted)
        {
            newArr = append(newArr, key);
        }
        // Append remaining tail (indices i+1 onward are not yet processed)
        for (var j = i + 1; j < size(arr); j += 1)
        {
            newArr = append(newArr, arr[j]);
        }
        arr = newArr;
    }
    return arr;
}


// ---------------------------------------------------------------------------
// tryFindSharedEdge
// ---------------------------------------------------------------------------

/**
 * Returns the shared edge(s) between two faces, if the faces are adjacent.
 *
 * Uses qIntersection on the sets of edges adjacent to each face. If the faces
 * share one or more edges the result is non-empty; if they are not adjacent the
 * result is an empty query (qNothing equivalent — isQueryEmpty returns true).
 *
 * This is a convenience helper used in editing logic to auto-populate the
 * reference edge selectors when the user picks two adjacent faces. The user
 * can always override the auto-populated value.
 *
 * @param context {Context} - active modeling context
 * @param face1 {Query} - first face
 * @param face2 {Query} - second face
 * @returns {Query} - query resolving to shared edge(s), or empty if not adjacent
 */
export function tryFindSharedEdge(context is Context, face1 is Query, face2 is Query) returns Query
{
    return qIntersection([
        qAdjacent(face1, AdjacencyType.EDGE, EntityType.EDGE),
        qAdjacent(face2, AdjacencyType.EDGE, EntityType.EDGE)
    ]);
}
