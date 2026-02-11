FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

//import curveChain
import(path : "670e82ad72abc97906ec9038", version : "64dc9244c25196d5236b6e33");
//import curveMappingUtils
import(path : "de955d503dbb0ec88622e51b", version : "c9021c710052b752f3eab663");
//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

/**
 * Mapping mode for curve transformation.
 */
export enum MappingMode
{
    annotation { "Name" : "Preserve arc length" }
    LENGTH,
    annotation { "Name" : "Preserve parameter" }
    PARAM
}

/**
 * Alignment mode for reference points.
 */
export enum AlignmentMode
{
    annotation { "Name" : "Automatic (closest point)" }
    AUTO,
    annotation { "Name" : "Manual alignment" }
    MANUAL
}

/**
 * Transform world point to Frenet frame coordinates.
 *
 * @param worldPoint : Point in world coordinates
 * @param frenetResult : EdgeCurvatureResult with frame
 * @returns Local coordinates [tangent, normal, binormal]
 */
function worldPointToFrenet(worldPoint is Vector, frenetResult is EdgeCurvatureResult) returns Vector
{
    var frame = frenetResult.frame;
    const localVector = worldPoint - frame.origin;

    // Handle degenerate case: line with zero curvature (no defined normal/binormal)
    // Check if curvature is effectively zero
    if (abs(frenetResult.curvature) < 1e-10 / meter)
    {
        // Construct arbitrary orthonormal frame from tangent
        const tangent = frame.zAxis;
        const normal = perpendicularVector(tangent);
        frame = coordSystem(frame.origin, normal, tangent);
    }

    // Project onto Frenet frame axes
    // frame.zAxis = tangent direction
    // frame.xAxis = principal normal direction
    // frame.yAxis = binormal direction
    const tangentCoord = dot(localVector, frame.zAxis);
    const normalCoord = dot(localVector, frame.xAxis);
    const binormalCoord = dot(localVector, yAxis(frame));

    return vector(tangentCoord, normalCoord, binormalCoord);
}

/**
 * Transform Frenet frame coordinates to world point.
 *
 * @param localCoords : Local coordinates [tangent, normal, binormal]
 * @param frenetResult : EdgeCurvatureResult with frame
 * @returns Point in world coordinates
 */
function frenetPointToWorld(localCoords is Vector, frenetResult is EdgeCurvatureResult) returns Vector
{
    var frame = frenetResult.frame;

    // Handle degenerate case: line with zero curvature (no defined normal/binormal)
    // Check if curvature is effectively zero
    if (abs(frenetResult.curvature) < 1e-10 / meter)
    {
        // Construct arbitrary orthonormal frame from tangent
        const tangent = frame.zAxis;
        const normal = perpendicularVector(tangent);
        frame = coordSystem(frame.origin, normal, tangent);
    }

    // Reconstruct world point from Frenet coordinates
    return frame.origin +
           localCoords[0] * frame.zAxis +
           localCoords[1] * frame.xAxis +
           localCoords[2] * yAxis(frame);
}

/**
 * Set up curve mapping between fromChain and toChain.
 *
 * @param context : Onshape context
 * @param id : Feature ID for warnings/errors
 * @param fromChain : Source reference chain
 * @param toChain : Target reference chain
 * @param sourcePoint : Alignment point on source geometry
 * @param alignmentMode : AUTO or MANUAL
 * @param options : {
 *                    fromAlignPoint: Vector (required if MANUAL)
 *                    toAlignPoint: Vector (required if MANUAL)
 *                    mappingMode: MappingMode (default LENGTH)
 *                    checkCoplanar: boolean (default true)
 *                    warnNonCoplanar: boolean (default true)
 *                  }
 * @returns {
 *            fromChain: CurveChain,
 *            toChain: CurveChain,
 *            fromRefParam: number,
 *            toRefParam: number,
 *            fromRefArcLength: ValueWithUnits,
 *            toRefArcLength: ValueWithUnits,
 *            mappingMode: MappingMode,
 *            isCoplanar: boolean,
 *            coplanarityAngle: ValueWithUnits (if not coplanar)
 *          }
 */
export function buildCurveMapping(context is Context, id is Id,
                                  fromChain is CurveChain,
                                  toChain is CurveChain,
                                  sourcePoint is Vector,
                                  alignmentMode is AlignmentMode,
                                  options is map) returns map
{
    const mappingMode = options.mappingMode ?? MappingMode.LENGTH;
    const checkCoplanar = options.checkCoplanar ?? true;
    const warnNonCoplanar = options.warnNonCoplanar ?? true;

    // Determine reference points
    var fromRefParam;
    var toRefParam;

    if (alignmentMode == AlignmentMode.AUTO)
    {
        // Project sourcePoint onto both chains
        const fromProj = projectPointOnChain(context, fromChain, sourcePoint, {});
        const toProj = projectPointOnChain(context, toChain, sourcePoint, {});

        fromRefParam = fromProj.chainParameter;
        toRefParam = toProj.chainParameter;
    }
    else // MANUAL
    {
        if (options.fromAlignPoint == undefined || options.toAlignPoint == undefined)
        {
            throw regenError("Manual alignment requires fromAlignPoint and toAlignPoint",
                           ["fromEdge", "toEdge"]);
        }

        const fromProj = projectPointOnChain(context, fromChain, options.fromAlignPoint, {});
        const toProj = projectPointOnChain(context, toChain, options.toAlignPoint, {});

        fromRefParam = fromProj.chainParameter;
        toRefParam = toProj.chainParameter;
    }

    // Compute arc lengths at reference points
    const fromRefArcLength = fromRefParam * getChainLength(fromChain);
    const toRefArcLength = toRefParam * getChainLength(toChain);

    // Check coplanarity if requested
    var isCoplanar = true;
    var coplanarityAngle = undefined;

    if (checkCoplanar)
    {
        const coplanarCheck = checkCoplanarity(context, fromChain, toChain, {});
        isCoplanar = coplanarCheck.coplanar;

        if (!isCoplanar)
        {
            coplanarityAngle = coplanarCheck.angle;

            if (warnNonCoplanar)
            {
                reportFeatureInfo(context, id,
                    "From and To curves not coplanar (max deviation: " ~
                    toString(coplanarCheck.maxDeviation) ~ "). " ~
                    "Mapping will still work but may produce unexpected results.");
            }
        }
    }

    return {
        "fromChain" : fromChain,
        "toChain" : toChain,
        "fromRefParam" : fromRefParam,
        "toRefParam" : toRefParam,
        "fromRefArcLength" : fromRefArcLength,
        "toRefArcLength" : toRefArcLength,
        "mappingMode" : mappingMode,
        "isCoplanar" : isCoplanar,
        "coplanarityAngle" : coplanarityAngle
    };
}

/**
 * Map a single point from fromChain reference frame to toChain.
 *
 * @param context : Onshape context
 * @param mapping : Result from buildCurveMapping()
 * @param sourcePoint : Point to map (in world coordinates)
 * @returns {
 *            fromParam: number,
 *            fromFrame: EdgeCurvatureResult,
 *            localCoords: Vector,
 *            toParam: number,
 *            toFrame: EdgeCurvatureResult,
 *            mappedPoint: Vector
 *          }
 */
export function mapPointToCurve(context is Context, mapping is map,
                                sourcePoint is Vector) returns map
{
    // 1. Project sourcePoint onto fromChain
    const fromProj = projectPointOnChain(context, mapping.fromChain, sourcePoint, {});
    const fromParam = fromProj.chainParameter;

    // 2. Get Frenet frame at fromParam
    const fromFrame = getChainFrenetFrame(context, mapping.fromChain, fromParam);

    // 3. Transform sourcePoint to Frenet coordinates (CORRECT ORDER)
    const localCoords = worldPointToFrenet(sourcePoint, fromFrame);

    // 4. Compute toParam based on mapping mode
    var toParam;

    if (mapping.mappingMode == MappingMode.LENGTH)
    {
        // Arc-length based mapping
        const fromArcLength = fromParam * getChainLength(mapping.fromChain);
        const deltaArcLength = fromArcLength - mapping.fromRefArcLength;

        const toArcLength = mapping.toRefArcLength + deltaArcLength;
        toParam = chainParameterAtArcLength(mapping.toChain, toArcLength);
    }
    else // PARAM mode
    {
        // Parameter delta preserved
        const deltaParam = fromParam - mapping.fromRefParam;
        toParam = mapping.toRefParam + deltaParam;

        // Clamp to [0, 1]
        toParam = max(0.0, min(1.0, toParam));
    }

    // 5. Get Frenet frame at toParam
    const toFrame = getChainFrenetFrame(context, mapping.toChain, toParam);

    // 6. Transform local coords to world using toFrame (CORRECT ORDER)
    const mappedPoint = frenetPointToWorld(localCoords, toFrame);

    return {
        "fromParam" : fromParam,
        "fromFrame" : fromFrame,
        "localCoords" : localCoords,
        "toParam" : toParam,
        "toFrame" : toFrame,
        "mappedPoint" : mappedPoint
    };
}

/**
 * Batch version of mapPointToCurve for performance.
 *
 * @param context : Onshape context
 * @param mapping : Result from buildCurveMapping()
 * @param sourcePoints : Array of points to map
 * @returns Array of mapping results (same structure as mapPointToCurve)
 */
export function mapPointsToCurve(context is Context, mapping is map,
                                 sourcePoints is array) returns array
{
    var results = [];

    for (var point in sourcePoints)
    {
        results = append(results, mapPointToCurve(context, mapping, point));
    }

    return results;
}

/**
 * Map a source curve with automatic segmentation at from/to chain boundaries.
 *
 * CRITICAL: If source curve spans across from/to chain edge boundaries,
 * it will be segmented at those boundaries and each piece mapped independently.
 *
 * @param context : Onshape context
 * @param mapping : Result from buildCurveMapping()
 * @param sourceCurve : Curve to map (BSplineCurve)
 * @param options : {
 *                    numSamplesPerSegment: number (default 25)
 *                    minimalSegmentation: boolean (default false)
 *                  }
 * @returns Array of mapped curve segments: {
 *            curve: BSplineCurve,
 *            fromEdgeIndex: number,
 *            toEdgeIndex: number,
 *            sourceParamRange: [start, end],
 *            continuityBefore: ChainContinuity (if not first segment)
 *          }
 */
export function mapCurveSegmented(context is Context,
                                  mapping is map,
                                  sourceCurve is BSplineCurve,
                                  options is map) returns array
{
    const numSamplesPerSegment = options.numSamplesPerSegment ?? 25;
    const minimalSegmentation = options.minimalSegmentation ?? false;

    if (numSamplesPerSegment < 2)
        throw regenError("numSamplesPerSegment must be at least 2");

    // Get source curve parameter range
    const sourceRange = getBSplineParamRange(sourceCurve);

    // Evaluate endpoints
    const startPoint = evaluateSpline({
        "spline" : sourceCurve,
        "parameters" : [sourceRange.uMin]
    })[0][0];
    const endPoint = evaluateSpline({
        "spline" : sourceCurve,
        "parameters" : [sourceRange.uMax]
    })[0][0];

    // Project endpoints onto fromChain to get span
    const startProj = projectPointOnChain(context, mapping.fromChain, startPoint, {});
    const endProj = projectPointOnChain(context, mapping.fromChain, endPoint, {});

    const spanStart = min(startProj.chainParameter, endProj.chainParameter);
    const spanEnd = max(startProj.chainParameter, endProj.chainParameter);

    // Check for degenerate case
    if (abs(spanEnd - spanStart) < 1e-10)
    {
        throw regenError("Source curve is perpendicular to reference chain - cannot map");
    }

    // Find edge boundaries within span
    const boundaries = mapping.fromChain.edgeBoundaries;
    var segmentPoints = [spanStart];

    for (var boundary in boundaries)
    {
        if (boundary > spanStart && boundary < spanEnd)
        {
            // Check if we should segment at this boundary
            var shouldSegment = true;

            if (minimalSegmentation)
            {
                // Only segment at C0 breaks (G0-only, not G1)
                // Simplified: always segment unless both chains are G1
                if (mapping.fromChain.continuity == ChainContinuity.G1 &&
                    mapping.toChain.continuity == ChainContinuity.G1)
                {
                    shouldSegment = false;
                }
            }

            if (shouldSegment)
            {
                segmentPoints = append(segmentPoints, boundary);
            }
        }
    }
    segmentPoints = append(segmentPoints, spanEnd);

    // Map each segment
    var segments = [];

    for (var i = 0; i < size(segmentPoints) - 1; i += 1)
    {
        const segStart = segmentPoints[i];
        const segEnd = segmentPoints[i + 1];

        // Sample points uniformly within this segment by PROJECTING onto fromChain
        // This is the CORRECT approach, not linear interpolation
        var sampleChainParams = [];
        for (var j = 0; j < numSamplesPerSegment; j += 1)
        {
            const t = j / (numSamplesPerSegment - 1);
            const chainParam = segStart + t * (segEnd - segStart);
            sampleChainParams = append(sampleChainParams, chainParam);
        }

        // Evaluate fromChain at these parameters to get world points
        const chainEval = evaluateChain(context, mapping.fromChain, sampleChainParams, 0);
        const chainPoints = chainEval.points;

        // Project each chain point onto source curve to get CORRECT source parameters
        var sourcePoints = [];
        for (var chainPt in chainPoints)
        {
            // Find closest point on source curve using evDistance
            // Create a temporary vertex at chainPt for evDistance
            // Simplified: sample source curve and find closest
            var bestParam = sourceRange.uMin;
            var bestDist = undefined;

            // Sample source curve to find closest parameter
            for (var k = 0; k <= 20; k += 1)
            {
                const testParam = sourceRange.uMin + (k / 20) * (sourceRange.uMax - sourceRange.uMin);
                const testPt = evaluateSpline({
                    "spline" : sourceCurve,
                    "parameters" : [testParam]
                })[0][0];

                const dist = norm(testPt - chainPt);
                if (bestDist == undefined || dist < bestDist)
                {
                    bestDist = dist;
                    bestParam = testParam;
                }
            }

            // Evaluate source curve at this parameter
            const sourcePt = evaluateSpline({
                "spline" : sourceCurve,
                "parameters" : [bestParam]
            })[0][0];

            sourcePoints = append(sourcePoints, sourcePt);
        }

        // Map all points
        const mappedResults = mapPointsToCurve(context, mapping, sourcePoints);

        var mappedPoints = [];
        for (var result in mappedResults)
        {
            mappedPoints = append(mappedPoints, result.mappedPoint);
        }

        // Detect if mapped points are nearly linear
        const isLinear = detectLinearSegment(mappedPoints, 1e-5 * meter);

        // Choose degree based on linearity (minimum degree 2 for approximateSpline)
        const degree = isLinear ? 2 : 3;

        // Approximate with endpoint interpolation
        const mappedCurve = approximateWithEndpoints(context, mappedPoints, degree, {});

        // Determine edge indices
        const fromEdgeIndex = findEdgeIndexAtParam(mapping.fromChain, segStart);

        // Map segStart through the mapping to get toChain parameter
        const toSegStartParam = mapChainParameter(mapping, segStart);
        const toEdgeIndex = findEdgeIndexAtParam(mapping.toChain, toSegStartParam);

        segments = append(segments, {
            "curve" : mappedCurve,
            "fromEdgeIndex" : fromEdgeIndex,
            "toEdgeIndex" : toEdgeIndex,
            "sourceParamRange" : [segStart, segEnd],
            "continuityBefore" : (i > 0) ? mapping.fromChain.continuity : undefined
        });
    }

    return segments;
}

/**
 * Map a chain parameter from fromChain to toChain.
 * @internal
 */
function mapChainParameter(mapping is map, fromParam is number) returns number
{
    if (mapping.mappingMode == MappingMode.LENGTH)
    {
        const fromArcLength = fromParam * getChainLength(mapping.fromChain);
        const deltaArcLength = fromArcLength - mapping.fromRefArcLength;
        const toArcLength = mapping.toRefArcLength + deltaArcLength;
        return chainParameterAtArcLength(mapping.toChain, toArcLength);
    }
    else // PARAM mode
    {
        const deltaParam = fromParam - mapping.fromRefParam;
        var toParam = mapping.toRefParam + deltaParam;
        toParam = max(0.0, min(1.0, toParam));
        return toParam;
    }
}

/**
 * Find which edge contains a given chain parameter.
 * @internal
 */
function findEdgeIndexAtParam(chain is CurveChain, parameter is number) returns number
{
    const boundaries = chain.edgeBoundaries;

    for (var i = 0; i < size(boundaries) - 1; i += 1)
    {
        if (parameter >= boundaries[i] && parameter <= boundaries[i + 1])
        {
            return i;
        }
    }

    // Fallback: return last edge
    return size(boundaries) - 2;
}

/**
 * Join mapped curve segments with appropriate continuity.
 *
 * @param context : Onshape context
 * @param segments : From mapCurveSegmented()
 * @param options : {
 *                    keepSeparateAtG0: boolean (default true)
 *                  }
 * @returns Array of joined curves (may be fewer than input segments)
 */
export function joinMappedSegments(context is Context,
                                   segments is array,
                                   options is map) returns array
{
    const keepSeparateAtG0 = options.keepSeparateAtG0 ?? true;

    if (size(segments) <= 1)
    {
        // Nothing to join
        var curves = [];
        for (var seg in segments)
        {
            curves = append(curves, seg.curve);
        }
        return curves;
    }

    // Group segments by continuity
    var groups = [];
    var currentGroup = [segments[0].curve];

    for (var i = 1; i < size(segments); i += 1)
    {
        const seg = segments[i];
        const continuity = seg.continuityBefore;

        if (continuity != undefined && continuity == ChainContinuity.G1)
        {
            // Can join with C1
            currentGroup = append(currentGroup, seg.curve);
        }
        else if (continuity != undefined && continuity == ChainContinuity.G0 && !keepSeparateAtG0)
        {
            // Join with C0
            currentGroup = append(currentGroup, seg.curve);
        }
        else
        {
            // Start new group
            groups = append(groups, currentGroup);
            currentGroup = [seg.curve];
        }
    }
    groups = append(groups, currentGroup);

    // Join each group (simplified: return individual curves for now)
    var result = [];
    for (var group in groups)
    {
        for (var curve in group)
        {
            result = append(result, curve);
        }
    }

    return result;
}

/**
 * Check if mapped points are nearly linear.
 *
 * @param points : Mapped point array
 * @param tolerance : Linearity tolerance (default 1e-5 m)
 * @returns True if points lie on a line within tolerance
 */
function detectLinearSegment(points is array, tolerance is ValueWithUnits)
    returns boolean
{
    if (size(points) < 3)
        return true;

    // Fit line through points using least squares
    const direction = normalize(points[size(points) - 1] - points[0]);
    const origin = points[0];

    // Measure max perpendicular distance from line
    var maxDeviation = 0 * meter;

    for (var point in points)
    {
        const toPoint = point - origin;
        const projectedDistance = dot(toPoint, direction);
        const projectedPoint = origin + projectedDistance * direction;
        const deviation = norm(point - projectedPoint);

        if (deviation > maxDeviation)
        {
            maxDeviation = deviation;
        }
    }

    return maxDeviation < tolerance;
}

/**
 * Check Frenet frame orientation consistency between curves.
 *
 * @param fromFrame : Frame on fromChain
 * @param toFrame : Frame on toChain
 * @returns {
 *            consistent: boolean,
 *            normalFlip: boolean,
 *            binormalFlip: boolean,
 *            maxAngle: ValueWithUnits
 *          }
 */
function checkFrameConsistency(fromFrame is EdgeCurvatureResult,
                               toFrame is EdgeCurvatureResult) returns map
{
    // Check if normals point in broadly same direction
    const normalDot = dot(fromFrame.frame.xAxis, toFrame.frame.xAxis);
    const binormalDot = dot(yAxis(fromFrame.frame), yAxis(toFrame.frame));

    const normalFlip = (normalDot < 0);
    const binormalFlip = (binormalDot < 0);

    // Compute max angle deviation
    const normalAngle = angleBetween(fromFrame.frame.xAxis, toFrame.frame.xAxis);
    const binormalAngle = angleBetween(yAxis(fromFrame.frame), yAxis(toFrame.frame));
    const maxAngle = max(normalAngle, binormalAngle);

    const consistent = !normalFlip && !binormalFlip;

    return {
        "consistent" : consistent,
        "normalFlip" : normalFlip,
        "binormalFlip" : binormalFlip,
        "maxAngle" : maxAngle
    };
}

/**
 * Adjust toFrame orientation to match fromFrame (if needed).
 *
 * @param fromFrame : Reference frame
 * @param toFrame : Frame to adjust
 * @param consistency : Result from checkFrameConsistency
 * @returns Adjusted EdgeCurvatureResult
 */
function adjustFrameOrientation(fromFrame is EdgeCurvatureResult,
                                toFrame is EdgeCurvatureResult,
                                consistency is map) returns EdgeCurvatureResult
{
    if (!consistency.normalFlip && !consistency.binormalFlip)
    {
        // No adjustment needed
        return toFrame;
    }

    // Need to reconstruct frame with flipped axes
    // coordSystem(origin, xAxis, zAxis) - derives yAxis = cross(zAxis, xAxis)
    var newXAxis = consistency.normalFlip ? -toFrame.frame.xAxis : toFrame.frame.xAxis;
    var newZAxis = toFrame.frame.zAxis; // Tangent direction never flips

    // If binormal flips, we need to flip the derived yAxis
    // Since yAxis = cross(zAxis, xAxis), flipping yAxis means negating xAxis
    if (consistency.binormalFlip && !consistency.normalFlip)
    {
        newXAxis = -newXAxis;
    }
    else if (consistency.binormalFlip && consistency.normalFlip)
    {
        // Both flip: xAxis already flipped, need to unflip for yAxis calculation
        newXAxis = toFrame.frame.xAxis;
    }

    const newFrame = coordSystem(toFrame.frame.origin, newXAxis, newZAxis);

    // Reconstruct EdgeCurvatureResult with new frame
    return {
        "frame" : newFrame,
        "curvature" : toFrame.curvature,
        "torsion" : toFrame.torsion
    };
}
