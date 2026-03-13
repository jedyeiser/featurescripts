FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * Like evDistance, but accepts a Path for side0 and/or side1.
 *
 * When a side is a Path, the returned side map contains:
 *   point      (Vector)  : world-space closest point, as with evDistance
 *   index      (integer) : index into path.edges of the closest edge
 *   parameter  (number)  : arc-length parameter [0,1] on that edge (local)
 *   pathParam  (number)  : arc-length parameter [0,1] along the full path
 *
 * Notes:
 *   - 'maximum' is not supported when either side is a Path.
 *   - arcLengthParameterization is forced to true internally for path sides.
 *     The returned 'parameter' and 'pathParam' are always arc-length based.
 */
export function evDistancePath(context is Context, arg is map) returns map
{
    const side0IsPath = arg.side0 is Path;
    const side1IsPath = arg.side1 is Path;

    // No paths: just delegate directly
    if (!side0IsPath && !side1IsPath)
        return evDistance(context, arg);

    if (arg.maximum == true)
        throw regenError("evDistancePath: 'maximum' is not supported when a side is a Path");

    // Force arc-length parameterization so pathParam computation is valid
    const distArg = mergeMaps(arg, { "arcLengthParameterization" : true });

    const path0 = side0IsPath ? arg.side0 : undefined;
    const path1 = side1IsPath ? arg.side1 : undefined;

    const iCount = side0IsPath ? size(path0.edges) : 1;
    const jCount = side1IsPath ? size(path1.edges) : 1;

    var bestResult = undefined;
    var bestI = 0;
    var bestJ = 0;

    // Iterate edge pairs, find minimum distance
    for (var i = 0; i < iCount; i += 1)
    {
        for (var j = 0; j < jCount; j += 1)
        {
            var callArg = distArg;
            if (side0IsPath)
                callArg.side0 = path0.edges[i];
            if (side1IsPath)
                callArg.side1 = path1.edges[j];

            const result = evDistance(context, callArg);

            if (bestResult == undefined || result.distance < bestResult.distance)
            {
                bestResult = result;
                bestI = i;
                bestJ = j;
            }
        }
    }

    // Augment path sides with index and pathParam
    var sides = bestResult.sides;

    if (side0IsPath)
        sides[0] = augmentPathSide(context, sides[0], path0, bestI);

    if (side1IsPath)
        sides[1] = augmentPathSide(context, sides[1], path1, bestJ);

    return mergeMaps(bestResult, { "sides" : sides });
}

/**
 * Adds 'index' and 'pathParam' to a side result map for a path side.
 */
function augmentPathSide(context is Context, side is map, path is Path, edgeIndex is number) returns map
{
    return mergeMaps(side, {
        "index"     : edgeIndex,
        "pathParam" : computePathParam(context, path, edgeIndex, side.parameter)
    });
}

/**
 * Computes the arc-length normalized [0,1] path parameter for a point
 * located at 'localParam' on path.edges[edgeIndex].
 *
 * Accounts for path.flipped so the parameter always increases
 * in the direction of path traversal.
 */
function computePathParam(context is Context, path is Path, edgeIndex is number, localParam is number) returns number
{
    // Collect edge lengths
    var edgeLengths = makeArray(size(path.edges));
    var totalLength = 0 * meter;
    for (var i = 0; i < size(path.edges); i += 1)
    {
        const len = evPathLength(context, path);
        edgeLengths[i] = len;
        totalLength += len;
    }

    // Cumulative length up to (but not including) the winning edge
    var cumLength = 0 * meter;
    for (var i = 0; i < edgeIndex; i += 1)
        cumLength += edgeLengths[i];

    // If the edge is flipped in the path, the edge's natural parameter
    // runs opposite to the path direction — invert it
    const effectiveParam = path.flipped[edgeIndex] ? (1 - localParam) : localParam;

    return (cumLength + effectiveParam * edgeLengths[edgeIndex]) / totalLength;
}