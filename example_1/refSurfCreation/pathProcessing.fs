FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/5f4337fadd14407df808e982/683d867c35fdab9c98d47556", version : "2799b2b0c9a4314b90f6ba6f");

//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/5f4337fadd14407df808e982/ad98c7f43a25a4c0e8a428e7", version : "1efe2444c4d02973fe212fcc");


// ─── Path processing ──────────────────────────────────────────────────────────

// Corrects startSign mismatches at inter-edge junctions that buildFrenetPath leaves unfixed.
//
// buildFrenetPath propagates startSign across the chain accounting only for intra-edge
// inflections. At junctions where the raw curvature direction is antiparallel between
// adjacent edges, it leaves startSign_{i+1} inconsistent with endSign_i.
//
// We detect this algebraically: the effective sign at the END of edge i is
//   endSign_i = startSign_i * (-1)^(number of inflections in edge i)
// If endSign_i != startSign_{i+1}, the junction is antiparallel and all edges i+1..n-1
// need their startSign flipped.
//
// This approach is robust against inflections near edge endpoints (which made the previous
// eps-based dot-product probe unreliable).
export function fixFrenetPathSigns(frenetPath is map) returns map
{
    var edgeData = frenetPath.edgeData;
    var n = size(edgeData);
    if (n < 2)
    {
        return frenetPath;
    }


    for (var i = 0; i < n - 1; i += 1)
    {
        var inflArcs   = edgeData[i].localInflectionArcs;
        var inflCount  = (inflArcs == undefined) ? 0 : size(inflArcs);
        var endSign    = edgeData[i].startSign * (inflCount % 2 == 1 ? -1 : 1);
        var nextStart  = edgeData[i + 1].startSign;
        var needsFlip  = (endSign != nextStart);

        if (needsFlip)
        {
            for (var j = i + 1; j < n; j += 1)
            {
                edgeData[j] = mergeMaps(edgeData[j], {
                    "startSign": -1 * edgeData[j].startSign
                });
            }
            frenetPath = mergeMaps(frenetPath, { "edgeData": edgeData });
        }
    }

    return frenetPath;
}

/**
 * Returns a frame at the given arc length along the path, handling both BSpline/line and curcular edges
 * @param context {Context} : Context for the document
 * @param frenetPah {map} : frenetPath
 * @param arcLength {ValueWithUnits} : distance along the reference wire
 */
// Returns a frame at the given arc length along the path, handling both BSpline/line edges
// (delegated to getFrameAtArcLength) and circular arc edges (computed natively).
//
// For circular arcs, getFrameAtArcLength fails internally because evApproximateBSplineCurve
// returns a rational NURBS and the arc-length table pipeline does not support weighted
// BSplines.  We bypass it entirely:
//   - position + tangent  via evEdgeTangentLine  (native, works for any edge type)
//   - centripetal normal  via (center - position) (geometric, no BSpline needed)
//   - traversal direction via edgeData.stdDir
//   - sign correction     via edgeData.startSign  (same convention as getFrameAtArcLength)
//
// The returned map has the same shape as getFrameAtArcLength: { frame, sign, edgeIndex }.
export function evalNativeFrame(context is Context, frenetPath is map, arcLength is ValueWithUnits) returns map
{
    var edgeData = frenetPath.edgeData;
    var n        = size(edgeData);

    // Find the edge that contains this arc length
    var ei = n - 1;
    for (var i = 0; i < n - 1; i += 1)
    {
        if (edgeData[i + 1].startArcLength > arcLength)
        {
            ei = i;
            break;
        }
    }
    var ed = edgeData[ei];

    // Check curve type; only circles need special handling.
    // evCurveDefinition uses the field name "curveType" (not "type").
    var curveDef = evCurveDefinition(context, { "edge": ed.query });
    if (curveDef.curveType != CurveType.CIRCLE)
    {
        return getFrameAtArcLength(context, frenetPath, arcLength);
    }

    // ── Circular arc: compute frame directly ──────────────────────────────────

    // Convert local arc length to native edge parameter [0, 1].
    // For a circular arc, the native parameter is proportional to arc length.
    var localArc = arcLength - ed.startArcLength;
    var edgeLen  = ed.length;
    var p        = (edgeLen / meter > 1e-10) ? localArc / edgeLen : 0.0;
    p = ed.stdDir ? p : (1.0 - p);
    p = min(max(p, 0.0), 1.0);

    // Position and tangent from the native edge query
    var tl      = evEdgeTangentLine(context, { "edge": ed.query, "parameter": p });
    var origin  = tl.origin;
    var tangent = ed.stdDir ? tl.direction : -tl.direction;

    // Centripetal normal: direction from the point on the arc toward the circle center.
    // curveDef.coordSystem.origin IS the circle center for CurveType.CIRCLE.
    var center  = curveDef.coordSystem.origin;
    var diff    = center - origin;
    var diffLen = norm(diff);
    var rawNorm = (diffLen / meter > 1e-10) ? diff / diffLen : curveDef.coordSystem.xAxis;

    // Apply startSign (same convention buildFrenetPath uses for BSpline edges)
    var sign  = ed.startSign;
    var xAxis = sign * rawNorm;

    // Re-orthogonalize against tangent — should already be perpendicular for a true circle,
    // but floating-point and edge parameterization can introduce small errors.
    xAxis     = xAxis - tangent * dot(tangent, xAxis);
    var xLen  = norm(xAxis);
    if (xLen > 1e-10)
    {
        xAxis = xAxis / xLen;
    }
    else
    {
        // True degenerate (query point at circle center — geometrically impossible for valid input)
        xAxis = rawNorm;
    }

    return {
        "frame"     : coordSystem(origin, xAxis, tangent),
        "sign"      : sign,
        "edgeIndex" : ei
    };
}

/**
 * Returns a parallel-transport frame at a signed distance from the path's reference point.
 * Positive = forward along path (tail direction), negative = backward (tip direction).
 * Arc length is clamped to [0, totalLength].
 *
 * @param context {Context}
 * @param processedPath {map} : output of processPath
 * @param distFromRef {ValueWithUnits} : signed distance from reference point (length units)
 * @returns map : { frame {CoordSystem}, arcLength {ValueWithUnits}, sign {number} }
 */
export function frameAtDistFromRef(context is Context, processedPath is map, distFromRef is ValueWithUnits) returns map
{
    var refArcLength    = processedPath.refParam * processedPath.totalLength;
    var targetArcLength = refArcLength + distFromRef;
    targetArcLength     = max(0 * meter, min(targetArcLength, processedPath.totalLength));

    var fr = sampleParallelTransportFrame(context, processedPath.frenetPath, processedPath.ptTable, targetArcLength);
    return mergeMaps(fr, { "arcLength" : targetArcLength });
}

/**
 * Projects a 3D point (vector) onto the path and returns the parallel-transport
 * frame at the nearest point.
 *
 * @param context {Context}
 * @param processedPath {map} : output of processPath
 * @param point {Vector} : 3D point in world coordinates (length units)
 * @returns {{
 *      @field frame {CoordSystem}, 
 *      @field arcLength {ValueWithUnits}, 
 *      @field distFromRef {ValueWithUnits}, 
 *      @field sign {number} 
 * }}
 */
export function frameAtPoint(context is Context, processedPath is map, point is Vector) returns map
{
    var projResult      = projectOntoFrenetPath(processedPath.frenetPath, point, undefined);
    var targetArcLength = projResult.arcLength;
    var refArcLength    = processedPath.refParam * processedPath.totalLength;

    var fr = sampleParallelTransportFrame(context, processedPath.frenetPath, processedPath.ptTable, targetArcLength);
    return mergeMaps(fr, {
        "arcLength"   : targetArcLength,
        "distFromRef" : targetArcLength - refArcLength
    });
}

/**
 * Projects a query (vertex, plane, mate connector) onto the path and returns
 * the parallel-transport frame at the nearest point.
 *
 * @param context {Context}
 * @param processedPath {map} : output of processPath
 * @param locationQuery {Query} : vertex, planar face, or mate connector
 * @returns map : { frame {CoordSystem}, arcLength {ValueWithUnits}, distFromRef {ValueWithUnits}, sign {number} }
 */
export function frameAtQuery(context is Context, processedPath is map, locationQuery is Query) returns map
{
    var pt              = getRefPoint(context, locationQuery);
    var projResult      = projectOntoFrenetPath(processedPath.frenetPath, pt, undefined);
    var targetArcLength = projResult.arcLength;
    var refArcLength    = processedPath.refParam * processedPath.totalLength;

    var fr = sampleParallelTransportFrame(context, processedPath.frenetPath, processedPath.ptTable, targetArcLength);
    return mergeMaps(fr, {
        "arcLength"   : targetArcLength,
        "distFromRef" : targetArcLength - refArcLength
    });
}


// Builds a parallel transport (Bishop) frame table along the path.
//
// Starting from the Frenet frame at s=0, each step rotates the previous xAxis by
// the same rotation that carries prevTangent -> currTangent (Rodrigues formula).
// This eliminates the torsion-driven spinning of the Frenet normal while preserving
// tangent continuity — the frame never flips direction on smooth G1 BSpline chains.
//
// Returns an array of { arcLength, xAxis } entries at numSamples uniform positions.
// yAxis = cross(tangent, xAxis) is not stored; it is computed on demand.
export function buildParallelTransportTable(context is Context, frenetPath is map, numSamples is number) returns array
{
    var totalLength = frenetPath.totalLength;
    var fr0         = evalNativeFrame(context, frenetPath, 0 * meter);
    var prevXAxis   = fr0.frame.xAxis;
    var prevTangent = fr0.frame.zAxis;

    var table = [{ "arcLength" : 0 * meter, "xAxis" : prevXAxis }];

    for (var i = 1; i < numSamples; i += 1)
    {
        var s           = totalLength * i / (numSamples - 1);
        var fr          = evalNativeFrame(context, frenetPath, s);
        var currTangent = fr.frame.zAxis;

        // Rodrigues rotation: rotate prevXAxis by the rotation taking prevTangent -> currTangent
        var k    = cross(prevTangent, currTangent);
        var kLen = norm(k);
        var newXAxis = prevXAxis;

        if (kLen >= 1e-10)
        {
            var kHat     = k / kLen;
            var sinTheta = kLen;                              // |cross(a,b)| = sin(angle) for unit vecs
            var cosTheta = dot(prevTangent, currTangent);
            newXAxis = prevXAxis * cosTheta
                + cross(kHat, prevXAxis) * sinTheta
                + kHat * (dot(kHat, prevXAxis) * (1 - cosTheta));
        }

        // Re-orthogonalize against tangent to prevent numerical drift
        newXAxis = newXAxis - currTangent * dot(currTangent, newXAxis);
        var xLen = norm(newXAxis);
        if (xLen > 1e-10)
        {
            newXAxis = newXAxis / xLen;
        }

        table       = append(table, { "arcLength" : s, "xAxis" : newXAxis });
        prevXAxis   = newXAxis;
        prevTangent = currTangent;
    }

    return table;
}


// Looks up the parallel transport xAxis at the given arc length via binary search + lerp,
// then returns a frame map in the same format as getFrameAtArcLength but with the PT
// xAxis substituted.  Origin and tangent (zAxis) are exact from getFrameAtArcLength.
export function sampleParallelTransportFrame(context is Context, frenetPath is map, ptTable is array,
    arcLength) returns map
{
    var n  = size(ptTable);
    var fr = evalNativeFrame(context, frenetPath, arcLength);

    if (n == 0)
    {
        return fr;
    }

    // Binary search for the bracketing interval
    var lo = 0;
    var hi = n - 1;
    while (hi - lo > 1)
    {
        var mid = floor((lo + hi) / 2);
        if (ptTable[mid].arcLength <= arcLength)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    // Lerp + renormalize between lo and hi
    var span    = ptTable[hi].arcLength - ptTable[lo].arcLength;
    var alpha   = (span / meter > 1e-12) ? (arcLength - ptTable[lo].arcLength) / span : 0.0;
    var x0      = ptTable[lo].xAxis;
    var x1      = ptTable[hi].xAxis;
    var xLerp   = x0 * (1 - alpha) + x1 * alpha;
    var xLen    = norm(xLerp);
    var ptXAxis = (xLen > 1e-10) ? xLerp / xLen : x0;

    // Re-orthogonalize against the exact tangent at this arc length.
    // The interpolated xAxis may have drifted slightly off the perpendicular plane.
    var tangent = fr.frame.zAxis;
    ptXAxis = ptXAxis - tangent * dot(tangent, ptXAxis);
    var xLen2 = norm(ptXAxis);
    if (xLen2 > 1e-10)
    {
        ptXAxis = ptXAxis / xLen2;
    }
    else
    {
        // Degenerate: interpolated PT direction is nearly parallel to the tangent.
        // Fall back to the raw Frenet xAxis, which is guaranteed perpendicular.
        ptXAxis = fr.frame.xAxis;
    }

    return mergeMaps(fr, { "frame" : coordSystem(fr.frame.origin, ptXAxis, fr.frame.zAxis) });
}

/**
 * Processes definition into a frenetPath
 * @param context {Context}
 * @param id {Id} : id
 * @param definition {{
 *      @field userSelection {Query} : Edges or wire queries
 *      @field flipDirection {boolean} : flip primary direction
 *      @field referencePoint {Query} : reference point along path
 *      @field numPoints {number} : number of points per region
 * }}
 */
export function processPath(context is Context, id is Id, definition is map) returns map
{
    var edges       = expandEdgeQuery(definition.userSelection);
    var frenetPath  = buildFrenetPath(context, id, edges, definition.flipDirection);
    frenetPath      = fixFrenetPathSigns(frenetPath);
    var totalLength = frenetPath.totalLength;

    var numPTSamples = definition.numPoints;
    var ptTable      = buildParallelTransportTable(context, frenetPath, numPTSamples);

    var refPt     = getRefPoint(context, definition.referencePoint);
    var refResult = projectOntoFrenetPath(frenetPath, refPt, undefined);
    var refParam  = refResult.arcLength / totalLength;

    return {
        "frenetPath" : frenetPath,
        "ptTable"    : ptTable,
        "totalLength"     : totalLength,
        "refParam"   : refParam
    };
}

// Builds a parallel transport table seeded from a caller-supplied xAxis rather than
// the Frenet normal. Useful for G0 wires where the Frenet normal is unreliable at
// kink junctions. The seed is projected perpendicular to the wire tangent at s=0
// before the first entry is stored.
//
// Algorithm is identical to buildParallelTransportTable; only the initial xAxis differs.
export function buildPinchWireTransportTable(context is Context, frenetPath is map,
    seedXAxis is Vector, numSamples is number) returns array
{
    var totalLength = frenetPath.totalLength;
    var fr0         = evalNativeFrame(context, frenetPath, 0 * meter);
    var prevTangent = fr0.frame.zAxis;

    // Project seed perpendicular to the wire tangent at s=0, fallback to Frenet normal
    var prevXAxis = seedXAxis - prevTangent * dot(prevTangent, seedXAxis);
    var xLen      = norm(prevXAxis);
    if (xLen > 1e-10)
    {
        prevXAxis = prevXAxis / xLen;
    }
    else
    {
        prevXAxis = fr0.frame.xAxis;
    }

    var table = [{ "arcLength" : 0 * meter, "xAxis" : prevXAxis }];

    for (var i = 1; i < numSamples; i += 1)
    {
        var s           = totalLength * i / (numSamples - 1);
        var fr          = evalNativeFrame(context, frenetPath, s);
        var currTangent = fr.frame.zAxis;

        var k    = cross(prevTangent, currTangent);
        var kLen = norm(k);
        var newXAxis = prevXAxis;

        if (kLen >= 1e-10)
        {
            var kHat     = k / kLen;
            var sinTheta = kLen;
            var cosTheta = dot(prevTangent, currTangent);
            newXAxis = prevXAxis * cosTheta
                + cross(kHat, prevXAxis) * sinTheta
                + kHat * (dot(kHat, prevXAxis) * (1 - cosTheta));
        }

        newXAxis = newXAxis - currTangent * dot(currTangent, newXAxis);
        var xLen2 = norm(newXAxis);
        if (xLen2 > 1e-10)
        {
            newXAxis = newXAxis / xLen2;
        }

        table       = append(table, { "arcLength" : s, "xAxis" : newXAxis });
        prevXAxis   = newXAxis;
        prevTangent = currTangent;
    }

    return table;
}

/**
 * Builds a processed path for a pinch wire — a wire that may have G0 (kink) junctions
 * between edges. The parallel transport xAxis is seeded from the ref wire's frame at
 * the pinch wire's start point, so "up" remains consistent with the ski coordinate system.
 *
 * fixFrenetPathSigns is intentionally skipped: its sign-flip correction assumes G1
 * continuity, which does not hold at G0 junctions.
 *
 * Returns a map with the same shape as processPath — compatible with
 * sampleParallelTransportFrame, projectOntoFrenetPath, etc.
 *
 * @param context {Context}
 * @param id {Id}
 * @param pinchWireBody {Query} : the pinch wire body
 * @param processedRefPath {map} : output of processPath for the reference wire
 * @param numSamples {number} : number of PT table entries
 */
export function processPinchWire(context is Context, id is Id, pinchWireBody is Query,
    processedRefPath is map, numSamples is number) returns map
{
    var edges      = expandEdgeQuery(pinchWireBody);
    var frenetPath = buildFrenetPath(context, id, edges, false);
    // Intentionally skip fixFrenetPathSigns — G0 junctions break its continuity assumption
    var totalLength = frenetPath.totalLength;

    // Seed xAxis from the ref wire frame at the pinch wire's start point
    var fr0        = evalNativeFrame(context, frenetPath, 0 * meter);
    var refFrame   = frameAtPoint(context, processedRefPath, fr0.frame.origin);
    var seedXAxis  = refFrame.frame.xAxis;

    var ptTable = buildPinchWireTransportTable(context, frenetPath, seedXAxis, numSamples);

    return {
        "frenetPath"  : frenetPath,
        "ptTable"     : ptTable,
        "totalLength" : totalLength
    };
}

/**
 * Projects two point queries onto processedPath and returns their equivalent
 * startX / endX — signed arc-length distances from the reference point,
 * matching the coordinate used by ALONG_REF regions.
 */
export function queryRegionExtents(context is Context, processedPath is map,
        startQuery is Query, endQuery is Query) returns map
{
    var pt0 = getRefPoint(context, startQuery);
    var pt1 = getRefPoint(context, endQuery);
    var r0 = projectOntoFrenetPath(processedPath.frenetPath, pt0, undefined);
    var r1 = projectOntoFrenetPath(processedPath.frenetPath, pt1, undefined);
    var refArcLength = processedPath.refParam * processedPath.totalLength;
    return {
        "startX" : min(r0.arcLength, r1.arcLength) - refArcLength,
        "endX"   : max(r0.arcLength, r1.arcLength) - refArcLength
    };
}


export function showRefFrames(context is Context, refPath is map, flipNormal is boolean, flipBinormal is boolean)
{

    // Draw frames with flipNormal/flipBinormal applied so arrows match
    // the actual offset directions (RED = normal offset dir, GREEN = binormal offset dir, BLUE = tangent)
    var numF   = 20;
    var len    = refPath.totalLength;
    var aLen   = len / max([1, numF - 1]) / 3;
    var aRad   = aLen * 0.05;
    for (var fi = 0; fi < numF; fi += 1)
    {
        var s   = len * fi / max([1, numF - 1]);
        var fr  = sampleParallelTransportFrame(context, refPath.frenetPath, refPath.ptTable, s);
        var org = fr.frame.origin;
        var nD  = flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
        var bD  = flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;
        addDebugArrow(context, org, org + aLen * nD,              aRad,          DebugColor.GREEN);
        addDebugArrow(context, org, org + aLen * bD,              aRad * (2/3),  DebugColor.RED);
        addDebugArrow(context, org, org + aLen * fr.frame.zAxis,  aRad * 0.5,   DebugColor.BLUE);
    }

}

export function showTransportFrames(context is Context, frame is CoordSystem, xAxisColor is DebugColor, yAxisColor is DebugColor, zAxisColor is DebugColor, len is ValueWithUnits)
{

    // Draw frames with flipNormal/flipBinormal applied so arrows match
    // the actual offset directions (RED = normal offset dir, GREEN = binormal offset dir, BLUE = tangent)
    var numF   = 20;
    
    var aLen   = len / max([1, numF - 1]) / 3;
    var aRad   = aLen * 0.05;
    for (var fi = 0; fi < numF; fi += 1)
    {
        var s   = len * fi / max([1, numF - 1]);
        var org = frame.origin;
        var nD  = yAxis(frame);
        var bD  = frame.xAxis;
        addDebugArrow(context, org, org + aLen * nD,              aRad,          yAxisColor);
        addDebugArrow(context, org, org + aLen * bD,              aRad * (2/3),  xAxisColor);
        addDebugArrow(context, org, org + aLen * frame.zAxis,  aRad * 0.5,   zAxisColor);
    }

}