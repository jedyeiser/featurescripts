FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * Continuity level for curve chains.
 */
export enum ChainContinuity
{
    annotation { "Name" : "G0 (Position)" }
    G0,
    annotation { "Name" : "G1 (Tangent)" }
    G1
}

/**
 * Represents a continuous chain of edges with unified parameterization.
 * Internal structure - use builder and accessor functions.
 */
export type CurveChain typecheck canBeCurveChain;

/** @internal */
export predicate canBeCurveChain(value)
{
    value is map;
    value.edges is array;              // Array of edge queries
    value.path is Path;                // Constructed path from edges
    value.continuity is ChainContinuity;
    value.edgeBoundaries is array;     // Normalized arc-length boundaries [0, s1, s2, ..., 1]
    value.totalLength is ValueWithUnits;
    value.numSamples is number;        // Arc-length table resolution
    value.isValid is boolean;
}

/**
 * Construct a curve chain from edges with continuity validation.
 *
 * @param context : Onshape context
 * @param edges : Query for edges to chain (order matters)
 * @param continuity : Required continuity level (G0 or G1)
 * @param options : {
 *                    numSamples: number (default 200) - Path evaluation samples
 *                    tangentTolerance: ValueWithUnits (default 1e-3 rad)
 *                    gapTolerance: ValueWithUnits (default 1e-6 m)
 *                  }
 * @returns CurveChain data structure
 */
export function buildCurveChain(context is Context, edges is Query,
                                continuity is ChainContinuity,
                                options is map) returns CurveChain
{
    // Default options
    const numSamples = options.numSamples ?? 200;
    const tangentTolerance = options.tangentTolerance ?? (1e-3 * radian);
    const gapTolerance = options.gapTolerance ?? (1e-6 * meter);

    // Get edge array
    const edgeArray = evaluateQuery(context, edges);
    if (size(edgeArray) == 0)
        throw regenError("No edges provided for curve chain", ["edges"]);

    // Validate continuity
    const validation = validateChainContinuity(context, edgeArray, continuity, {
        "tangentTolerance" : tangentTolerance,
        "gapTolerance" : gapTolerance
    });

    if (!validation.valid)
    {
        throw regenError(validation.errorMessage, ["edges"]);
    }

    // Build path from edges (use original Query, not array)
    const path = constructPath(context, edges);

    // Compute total length and edge boundaries
    var edgeLengths = [];
    var totalLength = 0 * meter;

    for (var edge in edgeArray)
    {
        const length = evLength(context, {
            "entities" : edge
        });
        edgeLengths = append(edgeLengths, length);
        totalLength += length;
    }

    // Normalize boundaries to [0, 1]
    var edgeBoundaries = [0.0];
    var cumulative = 0 * meter;

    for (var i = 0; i < size(edgeLengths) - 1; i += 1)
    {
        cumulative += edgeLengths[i];
        edgeBoundaries = append(edgeBoundaries, cumulative / totalLength);
    }
    edgeBoundaries = append(edgeBoundaries, 1.0);

    return {
        "edges" : edgeArray,
        "path" : path,
        "continuity" : continuity,
        "edgeBoundaries" : edgeBoundaries,
        "totalLength" : totalLength,
        "numSamples" : numSamples,
        "isValid" : true
    } as CurveChain;
}

/**
 * Validate continuity at chain joints.
 * @internal
 */
function validateChainContinuity(context is Context, edgeArray is array,
                                 continuity is ChainContinuity,
                                 options is map) returns map
{
    const tangentTolerance = options.tangentTolerance;
    const gapTolerance = options.gapTolerance;

    if (size(edgeArray) == 1)
    {
        return { "valid" : true };
    }

    // Check each adjacent pair
    for (var i = 0; i < size(edgeArray) - 1; i += 1)
    {
        const edge1 = edgeArray[i];
        const edge2 = edgeArray[i + 1];

        // Get endpoints
        const endPt1 = evEdgeTangentLine(context, {
            "edge" : edge1,
            "parameter" : 1.0
        }).origin;

        const startPt2 = evEdgeTangentLine(context, {
            "edge" : edge2,
            "parameter" : 0.0
        }).origin;

        // Check G0 continuity
        const gap = norm(endPt1 - startPt2);
        if (gap > gapTolerance)
        {
            return {
                "valid" : false,
                "errorMessage" : "Chain edges not G0 continuous at edge " ~ (i + 1) ~ "-" ~ (i + 2) ~
                                ". Gap: " ~ toString(gap)
            };
        }

        // Check G1 continuity if required
        if (continuity == ChainContinuity.G1)
        {
            const tangent1 = evEdgeTangentLine(context, {
                "edge" : edge1,
                "parameter" : 1.0
            }).direction;

            const tangent2 = evEdgeTangentLine(context, {
                "edge" : edge2,
                "parameter" : 0.0
            }).direction;

            // Tangents should be parallel (angle close to 0, not PI)
            const angle = angleBetween(tangent1, tangent2);

            if (angle > tangentTolerance)
            {
                return {
                    "valid" : false,
                    "errorMessage" : "Chain edges not G1 continuous at edge " ~ (i + 1) ~ "-" ~ (i + 2) ~
                                    ". Angle between tangents: " ~ toString(angle * (180 / PI)) ~ "°"
                };
            }
        }
    }

    return { "valid" : true };
}

/**
 * Map chain parameter [0,1] to path distance parameter.
 * Uses arc-length-based mapping via path length.
 */
function chainParamToPathParam(context is Context, chain is CurveChain,
                               chainParam is number) returns number
{
    // Clamp to [0, 1]
    const clampedParam = max(0.0, min(1.0, chainParam));

    // Target arc length
    const targetLength = clampedParam * chain.totalLength;

    // Use evPathLength to find path parameter at this arc length
    // Binary search for the path parameter that gives us targetLength
    var low = 0.0;
    var high = 1.0;
    const tolerance = 1e-6;

    for (var iter = 0; iter < 20; iter += 1)
    {
        const mid = (low + high) / 2;
        const pathLength = evPathLength(context, chain.path, 0.0, mid);

        if (abs(pathLength - targetLength) < tolerance * meter)
            return mid;

        if (pathLength < targetLength)
            low = mid;
        else
            high = mid;
    }

    return (low + high) / 2;
}

/**
 * Evaluate chain at normalized parameter(s).
 *
 * @param context : Onshape context
 * @param chain : Chain to evaluate
 * @param parameters : Array of parameters in [0, 1]
 * @param nDerivatives : Number of derivatives (0 = position only, 1 = add tangents)
 * @returns {
 *            points: array of Vector,
 *            tangents: array of Vector (if nDerivatives >= 1),
 *            derivatives: number
 *          }
 */
export function evaluateChain(context is Context, chain is CurveChain,
                              parameters is array, nDerivatives is number) returns map
{
    var result = {
        "points" : [],
        "derivatives" : nDerivatives
    };

    if (nDerivatives >= 1)
    {
        result.tangents = [];
    }

    // Convert chain parameters to path parameters
    var pathParams = [];
    for (var param in parameters)
    {
        pathParams = append(pathParams, chainParamToPathParam(context, chain, param));
    }

    // Evaluate path at all parameters
    const pathResult = evPathTangentLines(context, chain.path, pathParams);

    result.points = [];
    for (var tangentLine in pathResult.tangentLines)
    {
        result.points = append(result.points, tangentLine.origin);
    }

    if (nDerivatives >= 1)
    {
        result.tangents = [];
        for (var tangentLine in pathResult.tangentLines)
        {
            result.tangents = append(result.tangents, tangentLine.direction);
        }
    }

    return result;
}

/**
 * Compute Frenet frame at chain parameter.
 *
 * @param context : Onshape context
 * @param chain : Chain to evaluate
 * @param parameter : Chain parameter in [0, 1]
 * @returns EdgeCurvatureResult with Frenet frame + curvature
 */
export function getChainFrenetFrame(context is Context, chain is CurveChain,
                                    parameter is number) returns EdgeCurvatureResult
{
    // Map to path parameter
    const pathParam = chainParamToPathParam(context, chain, parameter);

    // Get edge index and local parameter
    const pathResult = evPathTangentLines(context, chain.path, [pathParam]);
    const edgeIndex = pathResult.edgeIndices[0];
    const edge = chain.edges[edgeIndex];

    // Get tangent line on this edge
    const tangentLine = pathResult.tangentLines[0];

    // Compute Frenet frame using edge curvature
    // Find parameter on the edge (approximate from path parameter)
    const edgeStart = chain.edgeBoundaries[edgeIndex];
    const edgeEnd = chain.edgeBoundaries[edgeIndex + 1];
    const edgeSpan = edgeEnd - edgeStart;
    const localParam = (parameter - edgeStart) / edgeSpan;

    // Get curvature at this point
    return evEdgeCurvature(context, {
        "edge" : edge,
        "parameter" : localParam
    });
}

/**
 * Sample chain at uniformly-spaced arc lengths.
 *
 * @param context : Onshape context
 * @param chain : Chain to sample
 * @param numPoints : Number of sample points
 * @returns {
 *            parameters: array,  // Chain parameters [0, ..., 1]
 *            points: array,      // 3D positions
 *            tangents: array,    // Tangent vectors
 *            arcLengths: array   // Arc lengths from start
 *          }
 */
export function sampleChainUniform(context is Context, chain is CurveChain,
                                   numPoints is number) returns map
{
    if (numPoints < 1)
        throw regenError("numPoints must be >= 1");

    // Handle single point case
    if (numPoints == 1)
    {
        const result = evPathTangentLines(context, chain.path, [0.0]);
        return {
            "parameters" : [0.0],
            "points" : [result.tangentLines[0].origin],
            "tangents" : [result.tangentLines[0].direction],
            "arcLengths" : [0 * meter]
        };
    }

    var parameters = [];
    var points = [];
    var tangents = [];
    var arcLengths = [];

    for (var i = 0; i < numPoints; i += 1)
    {
        const param = i / (numPoints - 1);
        const arcLength = param * chain.totalLength;

        parameters = append(parameters, param);
        arcLengths = append(arcLengths, arcLength);
    }

    // Evaluate all at once
    const evalResult = evaluateChain(context, chain, parameters, 1);

    return {
        "parameters" : parameters,
        "points" : evalResult.points,
        "tangents" : evalResult.tangents,
        "arcLengths" : arcLengths
    };
}

/**
 * Find chain parameter at given arc length from chain start.
 *
 * @param chain : Chain
 * @param arcLength : Target arc length
 * @returns Chain parameter in [0, 1]
 */
export function chainParameterAtArcLength(chain is CurveChain,
                                          arcLength is ValueWithUnits)
    returns number
{
    // Clamp arc length to valid range
    const clampedLength = max(0 * meter, min(chain.totalLength, arcLength));

    // Normalize to [0, 1]
    return clampedLength / chain.totalLength;
}

/**
 * Project point onto chain, finding closest point.
 *
 * @param context : Onshape context
 * @param chain : Chain
 * @param point : Query point
 * @param options : Options (currently unused)
 * @returns {
 *            chainParameter: number,      // Parameter on chain [0,1]
 *            edgeIndex: number,          // Which edge in chain
 *            localParameter: number,     // Parameter on that edge
 *            point: Vector,              // Closest point
 *            distance: ValueWithUnits    // Distance to query point
 *          }
 */
export function projectPointOnChain(context is Context, chain is CurveChain,
                                    point is Vector, options is map) returns map
{
    // Project onto each edge, find closest
    var bestResult = undefined;
    var bestDistance = undefined;
    var bestEdgeIndex = -1;

    for (var i = 0; i < size(chain.edges); i += 1)
    {
        const edge = chain.edges[i];

        // Use evDistance to find closest point on edge
        const distResult = evDistance(context, {
            "side0" : edge,
            "side1" : point
        });

        if (bestDistance == undefined || distResult.distance < bestDistance)
        {
            bestDistance = distResult.distance;
            bestResult = distResult;
            bestEdgeIndex = i;
        }
    }

    // Get parameter on the edge
    const edgeTangent = evEdgeTangentLine(context, {
        "edge" : chain.edges[bestEdgeIndex],
        "parameter" : 0.5
    });

    // Approximate parameter by projecting onto edge
    // Use edge endpoints to estimate parameter
    const edgeStart = evEdgeTangentLine(context, {
        "edge" : chain.edges[bestEdgeIndex],
        "parameter" : 0.0
    }).origin;

    const edgeEnd = evEdgeTangentLine(context, {
        "edge" : chain.edges[bestEdgeIndex],
        "parameter" : 1.0
    }).origin;

    const edgeVector = edgeEnd - edgeStart;
    const toPoint = bestResult.sides[0].point - edgeStart;
    const edgeLength = norm(edgeVector);

    var localParam = 0.5; // Default to midpoint
    if (edgeLength > TOLERANCE.zeroLength)
    {
        localParam = dot(toPoint, edgeVector) / (edgeLength * edgeLength);
        localParam = max(0.0, min(1.0, localParam));
    }

    // Convert local parameter to chain parameter
    const edgeStartParam = chain.edgeBoundaries[bestEdgeIndex];
    const edgeEndParam = chain.edgeBoundaries[bestEdgeIndex + 1];
    const chainParam = edgeStartParam + localParam * (edgeEndParam - edgeStartParam);

    return {
        "chainParameter" : chainParam,
        "edgeIndex" : bestEdgeIndex,
        "localParameter" : localParam,
        "point" : bestResult.sides[0].point,
        "distance" : bestDistance
    };
}

/**
 * Get total arc length of chain.
 */
export function getChainLength(chain is CurveChain) returns ValueWithUnits
{
    return chain.totalLength;
}

/**
 * Visualize chain with debug graphics.
 *
 * @param context : Onshape context
 * @param chain : Chain to visualize
 * @param options : {
 *                    showJoints: boolean (default true)
 *                    showTangents: boolean (default true)
 *                  }
 */
export function debugChain(context is Context, chain is CurveChain,
                          options is map)
{
    const showJoints = options.showJoints ?? true;
    const showTangents = options.showTangents ?? true;

    // Draw tangent arrows at start and end
    if (showTangents)
    {
        const startFrame = getChainFrenetFrame(context, chain, 0.0);
        const endFrame = getChainFrenetFrame(context, chain, 1.0);

        const arrowLength = chain.totalLength * 0.1;

        debug(context, startFrame.frame.origin, DebugColor.GREEN);
        debug(context, startFrame.frame.origin + startFrame.frame.zAxis * arrowLength, DebugColor.GREEN);

        debug(context, endFrame.frame.origin, DebugColor.BLUE);
        debug(context, endFrame.frame.origin + endFrame.frame.zAxis * arrowLength, DebugColor.BLUE);
    }

    // Show joint locations
    if (showJoints && size(chain.edgeBoundaries) > 2)
    {
        for (var i = 1; i < size(chain.edgeBoundaries) - 1; i += 1)
        {
            const param = chain.edgeBoundaries[i];
            const frame = getChainFrenetFrame(context, chain, param);

            // Green sphere for G1, yellow for G0
            const jointColor = (chain.continuity == ChainContinuity.G1) ?
                              DebugColor.GREEN : DebugColor.YELLOW;

            debug(context, frame.frame.origin, jointColor);
        }
    }
}
