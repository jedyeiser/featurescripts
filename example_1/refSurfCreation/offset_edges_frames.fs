FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import CurveWrapping_full/curveMappingCore
import(path : "08e8748f2ef24eea16072b75/fa1a00e26f19c86291f2c55e/683d867c35fdab9c98d47556", version : "3e109f7b7c74c76503dcf207");
//import CurveWrapping_full/Utils
import(path : "08e8748f2ef24eea16072b75/fa1a00e26f19c86291f2c55e/ad98c7f43a25a4c0e8a428e7", version : "223c53d12a83984c4c62e354");

/**
 * Offset edges -- path and frames (split out of offsetEdges.fs 2026-09-26; code unchanged except where noted).
 *
 * The reference path (curveMapping core buildFrenetPath), exact per-edge frames, the per-edge parallel-transport
 * table and the offset point at a path parameter. Every kernel evaluation of a frame goes through
 * [framesAtArcsPinned]: one evEdgeTangentLines call per source edge for any number of points (std's
 * evEdgeTangentLine is that same call with one parameter, so the values are the ones the per-point calls gave).
 */


// --- Exact frames ---------------------------------------------------------------

// The source edge an arc length falls on (pinEdge when given) and the edge parameter to evaluate there.
// Pure -- the lookup frameAtArc always made before its kernel call.
//
// Only origin and tangent (zAxis) of a frame are used: the offset directions come from this feature's own
// parallel-transport table, seeded once at s = 0 by [ptSeedNormal].
//
// (Until 2026-09-23 this read per-edge Frenet normals with sign tracking from the July curveMapping core,
// whose buildFrenetPath threw "Reference edge normals are not coplanar" when three or more consecutive flat
// arcs (sagitta < 0.1% of length, e.g. R 20 m over 100 mm) were treated as lines and the middle one fell back
// to a world-axis normal. The current core carries one normal along the whole path and has no such check.)
// pinEdge: evaluate on that source edge (arcLength clamped to its extent) instead of the edge the arc length
// falls on. A piece of output that ends at a G0 corner must read ITS OWN edge there; the lookup alone returns
// the next edge at the joint (2026-09-25).
export function arcLocation(frenetPath is map, arcLength, pinEdge) returns map
{
    var edgeData = frenetPath.edgeData;
    var s = arcLength;
    if (s < 0 * meter)
    {
        s = 0 * meter;
    }
    if (s > frenetPath.totalLength)
    {
        s = frenetPath.totalLength;
    }

    var lo = 0;
    if (pinEdge != undefined)
    {
        lo = pinEdge;
        s = min(max(s, edgeData[lo].startArcLength), edgeData[lo].startArcLength + edgeData[lo].length);
    }
    else
    {
        var hi = size(edgeData) - 1;
        while (lo < hi)
        {
            var mid = ceil((lo + hi) / 2);
            if (edgeData[mid].startArcLength <= s)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
    }
    var ed = edgeData[lo];

    // Every source edge is evaluated exactly (lines too since 2026-09-25: the core's sampled table blended the
    // two legs' tangents near a G0 corner). The core also treats an edge within 0.1% of straight as its chord
    // (a R 20 m arc over 100 mm qualifies), so the offset of such an arc came out straight -- up to the arc's
    // sagitta off, and collinear, so no arc could be built through it.
    var frac = (ed.length / meter > 1e-12) ? (s - ed.startArcLength) / ed.length : 0;
    frac = min(max(frac, 0), 1);
    return { "edgeIndex" : lo, "parameter" : ed.stdDir ? frac : 1 - frac };
}


// Frames at many arc lengths, pins[i] being the pinEdge of arcLengths[i] (undefined = look the edge up).
// Returns [{ frame, edgeIndex }] in input order, each exactly what frameAtArc returns for the same arguments;
// the kernel calls are grouped: one evEdgeTangentLines per source edge touched (2026-09-26, was one call per
// point).
export function framesAtArcsPinned(context is Context, frenetPath is map, arcLengths is array, pins is array) returns array
{
    var edgeData = frenetPath.edgeData;
    var locs     = [];
    var byEdge   = makeArray(size(edgeData), []);
    for (var i = 0; i < size(arcLengths); i += 1)
    {
        var loc = arcLocation(frenetPath, arcLengths[i], pins[i]);
        locs = append(locs, loc);
        byEdge[loc.edgeIndex] = append(byEdge[loc.edgeIndex], i);
    }

    var out = makeArray(size(arcLengths));
    for (var e = 0; e < size(edgeData); e += 1)
    {
        var idx = byEdge[e];
        if (size(idx) == 0)
        {
            continue;
        }
        var params = [];
        for (var i in idx)
        {
            params = append(params, locs[i].parameter);
        }
        var ed    = edgeData[e];
        var lines = evEdgeTangentLines(context, {
                    "edge"                      : ed.query,
                    "parameters"                : params,
                    "arcLengthParameterization" : true
                });
        for (var k = 0; k < size(idx); k += 1)
        {
            var tangent = ed.stdDir ? lines[k].direction : -lines[k].direction;
            out[idx[k]] = {
                "frame"     : coordSystem(lines[k].origin, perpendicularVector(tangent), tangent),
                "edgeIndex" : e
            };
        }
    }
    return out;
}


// [framesAtArcsPinned] with one pinEdge (or undefined) for every arc length.
export function framesAtArcs(context is Context, frenetPath is map, arcLengths is array, pinEdge) returns array
{
    return framesAtArcsPinned(context, frenetPath, arcLengths, makeArray(size(arcLengths), pinEdge));
}


// A frame at any arc length along the path: { frame (origin, tangent zAxis; xAxis arbitrary), edgeIndex }.
export function frameAtArc(context is Context, frenetPath is map, arcLength, pinEdge) returns map
{
    return framesAtArcs(context, frenetPath, [arcLength], pinEdge)[0];
}


// --- Curve types --------------------------------------------------------------

// The kernel curve type of a source edge (and a circle's centre): the copy processPath cached when asked
// to (curveTypes option), else one evCurveDefinition call -- the call every caller made before 2026-09-26.
export function edgeCurveInfo(context is Context, edgeDat is map) returns map
{
    if (edgeDat.oeCurveType != undefined)
    {
        return { "curveType" : edgeDat.oeCurveType, "center" : edgeDat.oeCircleCenter };
    }
    var curveDef = evCurveDefinition(context, { "edge" : edgeDat.query });
    return {
        "curveType" : curveDef.curveType,
        "center"    : (curveDef.curveType == CurveType.CIRCLE) ? curveDef.coordSystem.origin : undefined
    };
}


// --- Transport seed -------------------------------------------------------------

// True when the old core treated this edge as a line: a real line, or an edge whose control
// polygon deviates from its chord by less than 0.1% of its length.
export function seedTreatsAsLine(context is Context, edgeDat is map) returns boolean
{
    if (edgeCurveInfo(context, edgeDat).curveType == CurveType.LINE)
    {
        return true;
    }
    var cps      = edgeDat.bspline.controlPoints;
    var nCPs     = size(cps);
    var chord    = cps[nCPs - 1] - cps[0];
    var chordLen = norm(chord);
    if (chordLen < 1e-10 * meter)
    {
        return false;
    }
    var chordDir = chord / chordLen;
    var maxDev   = 0 * meter;
    for (var j = 1; j < nCPs - 1; j += 1)
    {
        var diff    = cps[j] - cps[0];
        var lateral = norm(diff - dot(diff, chordDir) * chordDir);
        if (lateral > maxDev)
        {
            maxDev = lateral;
        }
    }
    return maxDev < 0.001 * edgeDat.length;
}


// Curvature normal (toward the centre) at the traversal start of an edge.
export function traversalStartNormal(context is Context, edgeDat is map) returns Vector
{
    return evEdgeCurvature(context, {
                "edge"                      : edgeDat.query,
                "parameter"                 : edgeDat.stdDir ? 0 : 1,
                "arcLengthParameterization" : true
            }).frame.xAxis;
}


// The normal the transport table starts from at s = 0 -- the same choice the old core made
// there, so offsets keep their side on every chain that worked before:
//   - a circular first edge: toward its centre;
//   - a first edge the old core treated as a line: the next edge's curvature normal at its
//     start (when that edge is curved), otherwise a world-axis perpendicular;
//   - any other first edge: its curvature normal at the start.
export function ptSeedNormal(context is Context, frenetPath is map, startFrame is CoordSystem) returns Vector
{
    var edgeData = frenetPath.edgeData;
    var first    = edgeData[0];
    var tangent  = startFrame.zAxis;
    var info     = edgeCurveInfo(context, first);

    var seed;
    if (info.curveType == CurveType.CIRCLE)
    {
        seed = normalize(info.center - startFrame.origin);
    }
    else if (seedTreatsAsLine(context, first))
    {
        if (size(edgeData) > 1 && !seedTreatsAsLine(context, edgeData[1]))
        {
            seed = traversalStartNormal(context, edgeData[1]);
        }
        else
        {
            seed = lineFrenetFrame({ "origin" : startFrame.origin, "direction" : tangent }).xAxis;
        }
    }
    else
    {
        seed = traversalStartNormal(context, first);
    }

    seed = seed - dot(seed, tangent) * tangent;
    if (norm(seed) < 1e-9)
    {
        seed = lineFrenetFrame({ "origin" : startFrame.origin, "direction" : tangent }).xAxis;
    }
    return normalize(seed);
}


// --- Parallel transport table -------------------------------------------------

// Rotates x by the rotation taking unit tangent a to unit tangent b (Rodrigues), then
// re-orthogonalizes it against b.
export function transportStep(x is Vector, a is Vector, b is Vector) returns Vector
{
    var k    = cross(a, b);
    var kLen = norm(k);
    var y    = x;
    if (kLen >= 1e-10)
    {
        var kHat     = k / kLen;
        var cosTheta = dot(a, b);
        y = x * cosTheta + cross(kHat, x) * kLen + kHat * (dot(kHat, x) * (1 - cosTheta));
    }
    y = y - b * dot(b, y);
    var yLen = norm(y);
    return (yLen > 1e-10) ? y / yLen : x;
}


// Parallel transport (Bishop) frame table, ONE ARRAY PER SOURCE EDGE: ptTable[e] = [{ arcLength,
// xAxis }] from the edge's start to its end (both included). Within an edge each row is rotated
// from the previous one by the rotation carrying its tangent to the next; between edges the
// last row of edge e is rotated onto edge e+1's START tangent -- the corner's own rotation at
// a G0 joint -- so rows never mix the two legs of a corner and lookups never interpolate
// across one (2026-09-25; the single table's lerp bent both legs by ~0.6 mm near a corner).
// numSamples is shared out by length, at least 2 rows per edge. The tangents of one edge are read in
// one kernel call (2026-09-26).
export function buildParallelTransportTable(context is Context, frenetPath is map, numSamples is number) returns array
{
    var edgeData    = frenetPath.edgeData;
    var totalLength = frenetPath.totalLength;
    var fr0         = frameAtArc(context, frenetPath, 0 * meter, 0);
    var prevXAxis   = ptSeedNormal(context, frenetPath, fr0.frame);
    var prevTangent = fr0.frame.zAxis;

    var table = [];
    for (var e = 0; e < size(edgeData); e += 1)
    {
        var ed   = edgeData[e];
        var n    = max([2, ceil(numSamples * ed.length / totalLength) + 1]);
        var arcs = [];
        for (var i = 0; i < n; i += 1)
        {
            arcs = append(arcs, ed.startArcLength + ed.length * i / (n - 1));
        }
        var frs  = framesAtArcs(context, frenetPath, arcs, e);
        var rows = [];
        for (var i = 0; i < n; i += 1)
        {
            var tg = frs[i].frame.zAxis;
            prevXAxis   = transportStep(prevXAxis, prevTangent, tg);
            prevTangent = tg;
            rows = append(rows, { "arcLength" : arcs[i], "xAxis" : prevXAxis });
        }
        table = append(table, rows);
    }
    return table;
}


// The transported xAxis at arcLength on the frame's source edge: bracketing rows of that edge's table,
// lerp + renormalize, re-orthogonalized against the exact tangent. Returns the frame map with the
// transported xAxis.
export function transportedFrame(fr is map, ptTable is array, arcLength) returns map
{
    var rows = ptTable[fr.edgeIndex];
    var n    = size(rows);
    var s    = min(max(arcLength, rows[0].arcLength), rows[n - 1].arcLength);

    var lo = 0;
    var hi = n - 1;
    while (hi - lo > 1)
    {
        var mid = floor((lo + hi) / 2);
        if (rows[mid].arcLength <= s)
        {
            lo = mid;
        }
        else
        {
            hi = mid;
        }
    }

    var span    = rows[hi].arcLength - rows[lo].arcLength;
    var alpha   = (span / meter > 1e-12) ? (s - rows[lo].arcLength) / span : 0.0;
    var xLerp   = rows[lo].xAxis * (1 - alpha) + rows[hi].xAxis * alpha;
    var xLen    = norm(xLerp);
    var ptXAxis = (xLen > 1e-10) ? xLerp / xLen : rows[lo].xAxis;

    var tangent = fr.frame.zAxis;
    ptXAxis = ptXAxis - tangent * dot(tangent, ptXAxis);
    var xLen2 = norm(ptXAxis);
    ptXAxis = (xLen2 > 1e-10) ? ptXAxis / xLen2 : fr.frame.xAxis;

    return mergeMaps(fr, { "frame" : coordSystem(fr.frame.origin, ptXAxis, fr.frame.zAxis) });
}


// Transported frames at many arc lengths (pinEdge or undefined for all), in input order.
export function sampleParallelTransportFrames(context is Context, frenetPath is map, ptTable is array,
    arcLengths is array, pinEdge) returns array
{
    var frs = framesAtArcs(context, frenetPath, arcLengths, pinEdge);
    var out = [];
    for (var i = 0; i < size(arcLengths); i += 1)
    {
        out = append(out, transportedFrame(frs[i], ptTable, arcLengths[i]));
    }
    return out;
}


// The transported frame at arcLength on its source edge (pinEdge when given).
export function sampleParallelTransportFrame(context is Context, frenetPath is map, ptTable is array,
    arcLength) returns map
{
    return sampleParallelTransportFrames(context, frenetPath, ptTable, [arcLength], undefined)[0];
}

export function sampleParallelTransportFrame(context is Context, frenetPath is map, ptTable is array,
    arcLength, pinEdge) returns map
{
    return sampleParallelTransportFrames(context, frenetPath, ptTable, [arcLength], pinEdge)[0];
}


// --- Path ---------------------------------------------------------------------

// The reference path: { frenetPath, ptTable, length, refParam }.
// options:
//   transportTable : build the parallel-transport table (the feature body). The editing logic passes false:
//                    it only needs the length and projections, and rebuilt the whole table on every dialog
//                    change before 2026-09-26. ptTable is undefined then.
//   curveTypes     : cache every source edge's curve type in edgeData (oeCurveType / oeCircleCenter), for
//                    callers that ask per piece (Keep as arcs).
export function processPath(context is Context, id is Id, definition is map, options is map) returns map
{
    var edges       = expandEdgeQuery(definition.userSelection);
    var frenetPath  = buildFrenetPath(context, id, edges, definition.flipDirection);
    var edgeData    = frenetPath.edgeData;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var extra = { "exactFrames" : true };   // (never overwrite the core's own isLine: its projection relies on it)
        if (options.curveTypes == true)
        {
            var curveDef = evCurveDefinition(context, { "edge" : edgeData[i].query });
            extra.oeCurveType = curveDef.curveType;
            if (curveDef.curveType == CurveType.CIRCLE)
            {
                extra.oeCircleCenter = curveDef.coordSystem.origin;
            }
        }
        edgeData[i] = mergeMaps(edgeData[i], extra);
    }
    frenetPath      = mergeMaps(frenetPath, { "edgeData" : edgeData });
    var totalLength = frenetPath.totalLength;

    var ptTable = undefined;
    if (options.transportTable == true)
    {
        var numPTSamples = max([100, definition.numRegionPoints * 4]);
        ptTable = buildParallelTransportTable(context, frenetPath, numPTSamples);
    }

    var refPt     = getRefPoint(context, definition.referencePoint);
    var refResult = projectOntoFrenetPath(frenetPath, refPt, undefined);
    var refParam  = refResult.arcLength / totalLength;

    return {
        "frenetPath" : frenetPath,
        "ptTable"    : ptTable,
        "length"     : totalLength,
        "refParam"   : refParam
    };
}


// Index of the source edge covering path parameter t (the later edge at a joint).
export function edgeIndexAt(pathInfo is map, t is number) returns number
{
    var edgeData = pathInfo.frenetPath.edgeData;
    var arc = t * pathInfo.length;
    var e = 0;
    for (var i = 1; i < size(edgeData); i += 1)
    {
        if (edgeData[i].startArcLength <= arc)
        {
            e = i;
        }
    }
    return e;
}


// A joint whose tangent turns more than this is a G0 corner (the output splits and is treated there).
export const CORNER_ANGLE = 0.05 * degree;

// The G0 corners of the path: joints where the tangent turns by more than CORNER_ANGLE.
// Each: { edge (index of the edge after the joint), t, vertex, tIn, tOut, angle }.
export function pathCorners(context is Context, pathInfo is map) returns array
{
    var corners  = [];
    var edgeData = pathInfo.frenetPath.edgeData;
    var arcs     = [];
    var pins     = [];
    for (var e = 1; e < size(edgeData); e += 1)
    {
        var s = edgeData[e].startArcLength;
        arcs = concatenateArrays([arcs, [s, s]]);
        pins = concatenateArrays([pins, [e - 1, e]]);
    }
    var frs = framesAtArcsPinned(context, pathInfo.frenetPath, arcs, pins);
    for (var e = 1; e < size(edgeData); e += 1)
    {
        var s    = edgeData[e].startArcLength;
        var fIn  = frs[2 * (e - 1)].frame;
        var fOut = frs[2 * (e - 1) + 1].frame;
        var ang  = angleBetween(fIn.zAxis, fOut.zAxis);
        if (ang > CORNER_ANGLE)
        {
            corners = append(corners, { "edge" : e, "t" : s / pathInfo.length, "vertex" : fOut.origin,
                        "tIn" : fIn.zAxis, "tOut" : fOut.zAxis, "angle" : ang });
        }
    }
    return corners;
}


// --- Offset points ------------------------------------------------------------

/**
 * The 3D world points at path parameters ts with the given offsets ({ normalOff, binormalOff } per t), frames
 * read on pathInfo.pinEdge when set. The "normal" offset moves along yAxis(frame) = T x N (out of the plane of
 * a planar path), the "binormal" offset along the transported normal xAxis. One kernel call per source edge.
 */
export function computeOffsetPoints(context is Context, pathInfo is map, definition is map,
    ts is array, offsets is array) returns array
{
    var arcs = [];
    for (var t in ts)
    {
        arcs = append(arcs, t * pathInfo.length);
    }
    var frs = sampleParallelTransportFrames(context, pathInfo.frenetPath, pathInfo.ptTable, arcs, pathInfo.pinEdge);
    var pts = [];
    for (var i = 0; i < size(ts); i += 1)
    {
        var fr   = frs[i];
        // Empirically: yAxis(frame) = visual normal direction, xAxis = visual binormal direction
        var nDir = definition.flipNormal   ? -yAxis(fr.frame) : yAxis(fr.frame);
        var bDir = definition.flipBinormal ? -fr.frame.xAxis  : fr.frame.xAxis;
        pts = append(pts, fr.frame.origin + offsets[i].normalOff * nDir + offsets[i].binormalOff * bDir);
    }
    return pts;
}
