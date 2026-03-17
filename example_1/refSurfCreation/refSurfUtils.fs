FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");
import(path : "onshape/std/extend.fs", version : "2909.0");
import(path : "onshape/std/bridgingCurve.fs", version : "2909.0");
import(path : "onshape/std/loft.fs", version : "2909.0");

//import refSurfCore
import(path : "828cc4108f1c8683bc0e59cf", version : "7cd2de6ce6e8a2f5a4f0da71");


// =====================================================================
// PATH UTILITIES
// =====================================================================

/**
 * Like evDistance, but accepts a Path for side0 and/or side1.
 *
 * When a side is a Path, the returned side map contains:
 *   point      (Vector)  : world-space closest point
 *   index      (integer) : index into path.edges of the closest edge
 *   parameter  (number)  : arc-length parameter [0,1] on that edge (local)
 *   pathParam  (number)  : arc-length parameter [0,1] along the full path
 */
export function evDistancePath(context is Context, arg is map) returns map
{
    const side0IsPath = arg.side0 is Path;
    const side1IsPath = arg.side1 is Path;

    if (!side0IsPath && !side1IsPath)
        return evDistance(context, arg);

    if (arg.maximum == true)
        throw regenError("evDistancePath: 'maximum' is not supported when a side is a Path");

    const distArg = mergeMaps(arg, { "arcLengthParameterization" : true });

    const path0 = side0IsPath ? arg.side0 : undefined;
    const path1 = side1IsPath ? arg.side1 : undefined;

    const iCount = side0IsPath ? size(path0.edges) : 1;
    const jCount = side1IsPath ? size(path1.edges) : 1;

    var bestResult = undefined;
    var bestI = 0;
    var bestJ = 0;

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

    var sides = bestResult.sides;

    if (side0IsPath)
        sides[0] = augmentPathSide(context, sides[0], path0, bestI);

    if (side1IsPath)
        sides[1] = augmentPathSide(context, sides[1], path1, bestJ);

    return mergeMaps(bestResult, { "sides" : sides });
}

function augmentPathSide(context is Context, side is map, path is Path, edgeIndex is number) returns map
{
    return mergeMaps(side, {
        "index"     : edgeIndex,
        "pathParam" : computePathParam(context, path, edgeIndex, side.parameter)
    });
}

/**
 * Binary-searches for the path parameter [0,1] at which the wire's world-space
 * X coordinate equals targetX.  Works for any monotone-in-X wire.
 */
export function findPathParamAtX(context is Context, path is Path, targetX is ValueWithUnits) returns number
{
    var endpoints = evPathTangentLines(context, path, [0, 1]);
    var ep0X = endpoints.tangentLines[0].origin[0];
    var ep1X = endpoints.tangentLines[1].origin[0];
    var tLo  = 0.0;
    var tHi  = 1.0;
    for (var iter = 0; iter < 30; iter += 1)
    {
        var tMid = (tLo + tHi) / 2;
        var midX = evPathTangentLines(context, path, [tMid]).tangentLines[0].origin[0];
        if ((midX < targetX) == (ep0X < ep1X))
            tLo = tMid;
        else
            tHi = tMid;
    }
    return (tLo + tHi) / 2;
}

function computePathParam(context is Context, path is Path, edgeIndex is number, localParam is number) returns number
{
    var edgeLengths = makeArray(size(path.edges));
    var totalLength = 0 * meter;
    for (var i = 0; i < size(path.edges); i += 1)
    {
        const len = evLength(context, { "entities" : path.edges[i] });
        edgeLengths[i] = len;
        totalLength += len;
    }

    var cumLength = 0 * meter;
    for (var i = 0; i < edgeIndex; i += 1)
        cumLength += edgeLengths[i];

    const effectiveParam = path.flipped[edgeIndex] ? (1 - localParam) : localParam;

    return (cumLength + effectiveParam * edgeLengths[edgeIndex]) / totalLength;
}


// =====================================================================
// SURFACE / WIRE SETUP
// =====================================================================

/**
 * Extracts top and bottom reference wires from a side surface, builds a
 * reference bottom surface (extrude + split) and fills the top wire.
 * Returns a map with keys: topWire, bottomWire, refBottomSurf, refTopSurf.
 */
export function processSideSurf(context is Context, id is Id, refSheetBody is Query, refWire is Query) returns map
{
    var bodyEdges = qEdgeTopologyFilter(qUnion([qOwnedByBody(refSheetBody, EntityType.EDGE)]), EdgeTopology.ONE_SIDED);

    var retMap = {};

    opExtractWires(context, id + "extractRefWires", {
        "edges" : bodyEdges
    });

    var bodyArray = evaluateQuery(context, qCreatedBy(id + "extractRefWires", EntityType.BODY));
    bodyArray = mapArray(bodyArray, function(x) { return { "query" : x, "box3D" : evBox3d(context, { "topology" : x, "tight" : true }) }; });
    bodyArray = sort(bodyArray, function(a, b) { return a.box3D.minCorner[2] - b.box3D.minCorner[2]; });

    retMap["topWire"]    = bodyArray[1].query;
    retMap["bottomWire"] = bodyArray[0].query;

    setProperty(context, { "entities" : retMap["topWire"],    "propertyType" : PropertyType.NAME, "value" : "refTopWire" });
    setProperty(context, { "entities" : retMap["bottomWire"], "propertyType" : PropertyType.NAME, "value" : "refBottomWire" });

    // Extrude refWire along smallest bounding-box axis, then split with side surface
    var refWireEdges = qOwnedByBody(refWire, EntityType.EDGE);
    var refWireBox   = evBox3d(context, { "topology" : refWire, "tight" : true });
    var refExtents   = refWireBox.maxCorner - refWireBox.minCorner;
    var extrudeDir;
    if (refExtents[0] <= refExtents[1] && refExtents[0] <= refExtents[2])
        extrudeDir = vector(1, 0, 0);
    else if (refExtents[1] <= refExtents[0] && refExtents[1] <= refExtents[2])
        extrudeDir = vector(0, 1, 0);
    else
        extrudeDir = vector(0, 0, 1);

    var sideSurfBox      = evBox3d(context, { "topology" : refSheetBody, "tight" : true });
    var sideSurfHalfExt  = norm(sideSurfBox.maxCorner - sideSurfBox.minCorner) / 2;
    var halfWidth        = max(175 * millimeter, sideSurfHalfExt + 10 * millimeter);

    opExtrude(context, id + "extrudeRefBottom", {
        "entities"   : refWireEdges,
        "direction"  : extrudeDir,
        "endBound"   : BoundingType.BLIND,
        "endDepth"   : halfWidth,
        "startBound" : BoundingType.BLIND,
        "startDepth" : halfWidth
    });

    var extrudedBottomBody = qCreatedBy(id + "extrudeRefBottom", EntityType.BODY);

    opSplitPart(context, id + "splitRefBottom", {
        "targets" : extrudedBottomBody,
        "tool"    : refSheetBody
    });

    // Keep the piece closest to the side surface centroid
    var sideSurfCenter = (sideSurfBox.minCorner + sideSurfBox.maxCorner) / 2;
    var bottomPieces   = evaluateQuery(context, extrudedBottomBody);
    var keepBottomBody = bottomPieces[0];
    var minDist        = evDistance(context, { "side0" : keepBottomBody, "side1" : sideSurfCenter }).distance;
    for (var i = 1; i < size(bottomPieces); i += 1)
    {
        var d = evDistance(context, { "side0" : bottomPieces[i], "side1" : sideSurfCenter }).distance;
        if (d < minDist)
        {
            minDist        = d;
            keepBottomBody = bottomPieces[i];
        }
    }
    var deleteBottomPieces = qSubtraction(extrudedBottomBody, keepBottomBody);
    if (!isQueryEmpty(context, deleteBottomPieces))
        opDeleteBodies(context, id + "deleteBottomTrim", { "entities" : deleteBottomPieces });

    retMap["refBottomSurf"] = extrudedBottomBody;
    setProperty(context, { "entities" : retMap["refBottomSurf"], "propertyType" : PropertyType.NAME,       "value" : "refBottomSurf" });
    setProperty(context, { "entities" : retMap["refBottomSurf"], "propertyType" : PropertyType.APPEARANCE, "value" : color(137/255, 218/255, 211/255) });

    // Fill top wire
    var topRefEdges = qUnion([qOwnedByBody(retMap["topWire"], EntityType.EDGE)]);
    opFillSurface(context, id + "fillRefTop", {
        "edgesG0"       : topRefEdges,
        "edgesG1"       : qNothing(),
        "edgesG2"       : qNothing(),
        "guideVertices" : qNothing()
    });

    retMap["refTopSurf"] = qCreatedBy(id + "fillRefTop", EntityType.BODY);
    setProperty(context, { "entities" : retMap["refTopSurf"], "propertyType" : PropertyType.NAME,       "value" : "refTopSurf" });
    setProperty(context, { "entities" : retMap["refTopSurf"], "propertyType" : PropertyType.APPEARANCE, "value" : color(234/255, 185/255, 125/255) });

    return retMap;
}

/**
 * Builds per-vertex frames for each periphery edge adjacent to a vertex.
 * Returns an array of vertex data maps (one per vertex in frameEdges).
 * Each map has: query, point, adjacentEdges, vertexFrames (map edge->CoordSystem).
 */
export function processFaceFrames(context is Context, id is Id, sheetBody is Query, frameEdges is Query, samplingDef is map, refWireMap is map) returns array
{
    var keyVerticies = qAdjacent(frameEdges, AdjacencyType.VERTEX, EntityType.VERTEX);
    var vertexArray  = evaluateQuery(context, keyVerticies);
    vertexArray = mapArray(vertexArray, function(v) {
        return { "query" : v, "point" : evVertexPoint(context, { "vertex" : v }) };
    });

    for (var i = 0; i < size(vertexArray); i += 1)
    {
        var vertexData = vertexArray[i];
        var framePoint = vertexData.point;

        vertexData["adjacentEdges"] = evaluateQuery(context, qIntersection([
            qAdjacent(vertexData.query, AdjacencyType.VERTEX, EntityType.EDGE),
            frameEdges
        ]));
        if (size(vertexData.adjacentEdges) == 0)
        {
            vertexArray[i] = vertexData;
            continue;
        }

        var faceQ = qIntersection([
            qAdjacent(
                qAdjacent(vertexData.query, AdjacencyType.VERTEX, EntityType.EDGE),
                AdjacencyType.EDGE, EntityType.FACE),
            qOwnedByBody(sheetBody, EntityType.FACE)
        ]);
        var faceArray = evaluateQuery(context, faceQ);
        if (size(faceArray) == 0)
            throw regenError("processFaceFrames: no face found adjacent to vertex");
        faceQ = faceArray[0];

        var faceBox        = evBox3d(context, { "topology" : faceQ, "tight" : true });
        var faceInteriorPt = (faceBox.minCorner + faceBox.maxCorner) / 2;
        var faceCenterUV   = evDistance(context, { "side0" : faceQ, "side1" : faceInteriorPt }).sides[0].parameter;
        var faceRefNormal  = evFaceTangentPlane(context, { "face" : faceQ, "parameter" : faceCenterUV }).normal;
        if (faceRefNormal[2] < 0)
            faceRefNormal = -1 * faceRefNormal;

        var faceUV     = evDistance(context, { "side0" : faceQ, "side1" : framePoint }).sides[0].parameter;
        var faceNormal = evFaceTangentPlane(context, { "face" : faceQ, "parameter" : faceUV }).normal;
        if (dot(faceNormal, faceRefNormal) < 0)
            faceNormal = -1 * faceNormal;

        var vertexFrames = {};
        for (var j = 0; j < size(vertexData.adjacentEdges); j += 1)
        {
            var edge      = vertexData.adjacentEdges[j];
            var startLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0, "arcLengthParameterization" : false });
            var edgeDir   = startLine.direction;
            if (norm(startLine.origin - framePoint) > 1e-5 * meter)
            {
                edgeDir = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1, "arcLengthParameterization" : false }).direction;
            }

            var binormal = cross(faceNormal, edgeDir);
            if (dot(binormal, (faceInteriorPt - framePoint) / meter) > 0)
                binormal = -1 * binormal;

            var frameZAxis  = cross(faceNormal, binormal);
            var vertexFrame = coordSystem(framePoint, faceNormal, frameZAxis);
            vertexFrames    = mergeMaps(vertexFrames, { (edge) : vertexFrame });

            if (samplingDef.showFrames)
            {
                var arrowLen = evLength(context, { "entities" : edge }) / 3;
                var arrowRad = arrowLen * 0.02;
                var xColor   = j == 0 ? DebugColor.RED    : DebugColor.ORANGE;
                var yColor   = j == 0 ? DebugColor.GREEN  : DebugColor.YELLOW;
                var zColor   = j == 0 ? DebugColor.BLUE   : DebugColor.CYAN;
                addDebugArrow(context, framePoint, framePoint + arrowLen * vertexFrame.xAxis,  arrowRad,        xColor);
                addDebugArrow(context, framePoint, framePoint + arrowLen * yAxis(vertexFrame), arrowRad * 0.75, yColor);
                addDebugArrow(context, framePoint, framePoint + arrowLen * vertexFrame.zAxis,  arrowRad * 0.5,  zColor);
            }
        }

        vertexData["vertexFrames"] = vertexFrames;
        vertexArray[i] = vertexData;
    }
    return vertexArray;
}

/**
 * Given a wire body query, returns the plane that contains it.
 * Throws a regen error if the wire is not planar or collinear.
 */
export function getWireBodyPlane(context is Context, wireBodyQuery is Query, tolerance is ValueWithUnits) returns Plane
{
    const edges    = evaluateQuery(context, qOwnedByBody(wireBodyQuery, EntityType.EDGE));
    const vertices = evaluateQuery(context, qOwnedByBody(wireBodyQuery, EntityType.VERTEX));

    if (size(edges) == 0)
        throw regenError("Wire body contains no edges.");

    var candidatePlane = undefined;
    for (var edge in edges)
    {
        const curveDef = evCurveDefinition(context, { "edge" : edge, "returnBSplinesAsDerivatives" : false });
        if (!(curveDef is Line))
        {
            try { candidatePlane = evPlanarEdge(context, { "edge" : edge }); }
            catch { throw regenError("Wire body contains a non-planar edge."); }
            break;
        }
    }

    if (candidatePlane == undefined)
    {
        const points = mapArray(vertices, function(v) { return evVertexPoint(context, { "vertex" : v }); });
        const origin = points[0];
        var v1 = undefined;
        for (var i = 1; i < size(points); i += 1)
        {
            if (norm(points[i] - origin) > tolerance)
            {
                v1 = normalize(points[i] - origin);
                break;
            }
        }
        if (v1 == undefined)
            throw regenError("Wire body: all vertices are coincident.");

        var normal = undefined;
        for (var i = 2; i < size(points); i += 1)
        {
            const diff    = points[i] - origin;
            if (norm(diff) <= tolerance) { continue; }
            const crossed = cross(v1, normalize(diff));
            if (norm(crossed) > 1e-6)
            {
                normal = normalize(crossed);
                break;
            }
        }
        if (normal == undefined)
            throw regenError("Wire body is collinear; cannot define a plane.");

        candidatePlane = plane(origin, normal);
    }

    for (var vertex in vertices)
    {
        const pt = evVertexPoint(context, { "vertex" : vertex });
        if (abs(dot(pt - candidatePlane.origin, candidatePlane.normal)) > tolerance)
            throw regenError("Wire body is not planar.");
    }

    return plane(candidatePlane.origin, alignNormalToWorldAxis(candidatePlane.normal));
}

function alignNormalToWorldAxis(normal is Vector) returns Vector
{
    const absX = abs(normal[0]);
    const absY = abs(normal[1]);
    const absZ = abs(normal[2]);

    const dominantPositive = (absY >= absX && absY >= absZ) ? vector(0, 1, 0) :
                             (absX >= absZ)                  ? vector(1, 0, 0) :
                                                               vector(0, 0, 1);

    return (dot(normal, dominantPositive) < 0) ? -normal : normal;
}

/**
 * Returns the highest geometric continuity between two edges that share a vertex.
 */
export function edgeContinuity(context is Context, edgeA is Query, edgeB is Query) returns GeometricContinuity
{
    const POS_TOL    = 1e-8 * meter;
    const G1_COS_TOL = 1e-4;
    const G2_REL_TOL = 1e-3;
    const ZERO_K_TOL = 1e-9 / meter;

    var tA0 = evEdgeTangentLine(context, { "edge" : edgeA, "parameter" : 0.0, "arcLengthParameterization" : false });
    var tA1 = evEdgeTangentLine(context, { "edge" : edgeA, "parameter" : 1.0, "arcLengthParameterization" : false });
    var tB0 = evEdgeTangentLine(context, { "edge" : edgeB, "parameter" : 0.0, "arcLengthParameterization" : false });
    var tB1 = evEdgeTangentLine(context, { "edge" : edgeB, "parameter" : 1.0, "arcLengthParameterization" : false });

    var paramA = 0.0;
    var paramB = 0.0;
    var tA;
    var tB;

    if      (norm(tA1.origin - tB0.origin) < POS_TOL) { paramA = 1.0; paramB = 0.0; tA = tA1; tB = tB0; }
    else if (norm(tA1.origin - tB1.origin) < POS_TOL) { paramA = 1.0; paramB = 1.0; tA = tA1; tB = tB1; }
    else if (norm(tA0.origin - tB0.origin) < POS_TOL) { paramA = 0.0; paramB = 0.0; tA = tA0; tB = tB0; }
    else if (norm(tA0.origin - tB1.origin) < POS_TOL) { paramA = 0.0; paramB = 1.0; tA = tA0; tB = tB1; }
    else { throw regenError("edgeContinuity: edges do not share a vertex"); }

    var outgoingA = (paramA == 0.0) ? tA.direction : -tA.direction;
    var outgoingB = (paramB == 0.0) ? tB.direction : -tB.direction;

    if (dot(outgoingA, outgoingB) > -(1.0 - G1_COS_TOL))
        return GeometricContinuity.G0;

    var cA = evEdgeCurvature(context, { "edge" : edgeA, "parameter" : paramA, "arcLengthParameterization" : false });
    var cB = evEdgeCurvature(context, { "edge" : edgeB, "parameter" : paramB, "arcLengthParameterization" : false });
    var kA = cA.curvature;
    var kB = cB.curvature;

    if (abs(kA) < ZERO_K_TOL && abs(kB) < ZERO_K_TOL)
        return GeometricContinuity.G2;

    if (abs(kA) < ZERO_K_TOL || abs(kB) < ZERO_K_TOL)
        return GeometricContinuity.G1;

    if (abs(kA - kB) / max(abs(kA), abs(kB)) > G2_REL_TOL)
        return GeometricContinuity.G1;

    return GeometricContinuity.G2;
}


// =====================================================================
// OFFSET PROFILE HELPERS
// =====================================================================

export function linearOffset(startOff is ValueWithUnits, endOff is ValueWithUnits, t is number) returns ValueWithUnits
{
    return startOff + (endOff - startOff) * t;
}

export function quadraticOffset(startOff is ValueWithUnits, endOff is ValueWithUnits, t is number, zeroSlopeAtStart is boolean) returns ValueWithUnits
{
    if (zeroSlopeAtStart)
        return startOff + (endOff - startOff) * t * t;
    else
        return startOff + (endOff - startOff) * (2 * t - t * t);
}

export function smoothOffset(startOff is ValueWithUnits, endOff is ValueWithUnits, t is number) returns ValueWithUnits
{
    var s = t * t * (3 - 2 * t);
    return startOff + (endOff - startOff) * s;
}

export function computeOffsetMag(offsetDef is map, t is number) returns ValueWithUnits
{
    if (offsetDef.offsetType == RegionOffsetType.CONSTANT)
        return offsetDef.offset;
    else if (offsetDef.offsetType == RegionOffsetType.LINEAR)
        return linearOffset(offsetDef.startOffset, offsetDef.endOffset, t);
    else if (offsetDef.offsetType == RegionOffsetType.QUADRATIC)
        return quadraticOffset(offsetDef.startOffset, offsetDef.endOffset, t, offsetDef.zeroSlopeAtStart);
    else
        return smoothOffset(offsetDef.startOffset, offsetDef.endOffset, t);
}

/**
 * Evaluates a stable outward face normal and binormal at a point on a face edge.
 * faceRefNormal is used to flip the per-point normal for consistency.
 * faceCenterPt is used to determine the outward direction of the binormal.
 */
function edgeOffsetFrame(faceNormalRaw is Vector, faceRefNormal is Vector, edgeTangent is Vector, edgePt is Vector, faceCenterPt is Vector) returns map
{
    var faceNormal = faceNormalRaw;
    if (dot(faceNormal, faceRefNormal) < 0)
        faceNormal = -1 * faceNormal;

    var binormal = cross(faceNormal, edgeTangent);
    if (dot(binormal, (faceCenterPt - edgePt) / meter) > 0)
        binormal = -1 * binormal;

    return { "faceNormal" : faceNormal, "binormal" : binormal };
}

/**
 * Returns { faceRefNormal, faceCenterPt, faceCenterUV } for a face.
 * faceRefNormal is Z-up consistent (flipped if Z < 0).
 */
function faceReferenceFrame(context is Context, face is Query) returns map
{
    var faceBB        = evBox3d(context, { "topology" : face, "tight" : true });
    var faceCenterPt  = (faceBB.minCorner + faceBB.maxCorner) / 2;
    var faceCenterUV  = evDistance(context, { "side0" : face, "side1" : faceCenterPt }).sides[0].parameter;
    var faceRefNormal = evFaceTangentPlane(context, { "face" : face, "parameter" : faceCenterUV }).normal;
    if (faceRefNormal[2] < 0)
        faceRefNormal = -1 * faceRefNormal;
    return { "faceRefNormal" : faceRefNormal, "faceCenterPt" : faceCenterPt };
}

/**
 * Builds uniform parameter array [0, 1/(n-1), ..., 1] of length n.
 */
function uniformParams(n is number) returns array
{
    var params = [];
    for (var i = 0; i < n; i += 1)
        params = append(params, i / (n - 1));
    return params;
}

/**
 * Returns the face adjacent to an edge that is owned by sheetBody, or undefined.
 */
function adjacentFace(context is Context, edge is Query, sheetBody is Query)
{
    var faceArr = evaluateQuery(context, qIntersection([
        qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE),
        qOwnedByBody(sheetBody, EntityType.FACE)
    ]));
    if (size(faceArr) == 0)
        return undefined;
    return faceArr[0];
}

/**
 * Projects edgePt onto the region start->end axis and returns t in [0,1].
 */
function regionT(edgePt is Vector, startOrigin is Vector, regionAxis is Vector, regionAxisLen2 is number) returns number
{
    var disp = (edgePt - startOrigin) / meter;
    var t    = dot(disp, regionAxis) / regionAxisLen2;
    if (t < 0) { t = 0; }
    if (t > 1) { t = 1; }
    return t;
}


// =====================================================================
// OFFSET VISUALIZATION (DEBUG)
// =====================================================================

/**
 * Draws magenta debug points at the offset position for each sample along
 * each periphery edge.  offsetPt = edgePt + offsetMag * outwardBinormal.
 */
export function sampleEdgeOffsets(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number)
{
    if (numPts < 2) { numPts = 2; }
    var edgeArray      = evaluateQuery(context, peripheryEdges);
    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);

    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];
        var face = adjacentFace(context, edge, sheetBody);
        if (face == undefined) { continue; }

        var ref         = faceReferenceFrame(context, face);
        var tangentLines = evEdgeTangentLines(context, {
            "edge" : edge, "parameters" : uniformParams(numPts), "arcLengthParameterization" : true
        });

        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt   = tangentLines[i].origin;
            var t        = regionT(edgePt, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2);
            var ptUV     = evDistance(context, { "side0" : face, "side1" : edgePt }).sides[0].parameter;
            var ptNormal = evFaceTangentPlane(context, { "face" : face, "parameter" : ptUV }).normal;
            var fr       = edgeOffsetFrame(ptNormal, ref.faceRefNormal, tangentLines[i].direction, edgePt, ref.faceCenterPt);

            var offsetPt = edgePt + computeOffsetMag(offsetDef, t) * fr.binormal;
            addDebugPoint(context, offsetPt, DebugColor.MAGENTA);
        }
    }
}

/**
 * Draws debug visualization for offset points:
 *   showPointFrames : RED=normal, BLUE=tangent, GREEN=binormal arrows
 *   showLoftPoints  : CYAN top point, YELLOW bottom point (if wallSecondDir)
 */
export function debugOffsetPoints(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number)
{
    if (numPts < 2) { numPts = 2; }
    var edgeArray      = evaluateQuery(context, peripheryEdges);
    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);

    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];
        var face = adjacentFace(context, edge, sheetBody);
        if (face == undefined) { continue; }

        var ref      = faceReferenceFrame(context, face);
        var edgeLen  = evLength(context, { "entities" : edge });
        var arrowLen = edgeLen / numPts;
        var arrowRad = arrowLen * 0.05;

        var tangentLines = evEdgeTangentLines(context, {
            "edge" : edge, "parameters" : uniformParams(numPts), "arcLengthParameterization" : true
        });

        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt      = tangentLines[i].origin;
            var edgeTangent = tangentLines[i].direction;
            var t           = regionT(edgePt, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2);
            var ptUV        = evDistance(context, { "side0" : face, "side1" : edgePt }).sides[0].parameter;
            var ptNormal    = evFaceTangentPlane(context, { "face" : face, "parameter" : ptUV }).normal;
            var fr          = edgeOffsetFrame(ptNormal, ref.faceRefNormal, edgeTangent, edgePt, ref.faceCenterPt);

            var offsetPt = edgePt + computeOffsetMag(offsetDef, t) * fr.binormal;

            if (offsetDef.showPointFrames)
            {
                addDebugArrow(context, offsetPt, offsetPt + arrowLen * fr.faceNormal, arrowRad,       DebugColor.RED);
                addDebugArrow(context, offsetPt, offsetPt + arrowLen * edgeTangent,   arrowRad * 0.8, DebugColor.BLUE);
                addDebugArrow(context, offsetPt, offsetPt + arrowLen * fr.binormal,   arrowRad * 0.8, DebugColor.GREEN);
            }

            if (offsetDef.showLoftPoints)
            {
                addDebugPoint(context, offsetPt + offsetDef.wallHeight * fr.faceNormal, DebugColor.CYAN);
                if (offsetDef.wallSecondDir == true)
                    addDebugPoint(context, offsetPt - offsetDef.wallHeight2 * fr.faceNormal, DebugColor.YELLOW);
            }
        }
    }
}


// =====================================================================
// OFFSET CURVE BUILDING
// =====================================================================

/**
 * Detects G1 junctions between adjacent edges (turning angle < 0.5 deg).
 * Returns an array of { startDeriv, endDeriv } per entry in edgeInfo.
 * edgeInfo entries must have: p0, p1, tan0, tan1 fields (or be undefined).
 */
function detectG1Junctions(edgeInfo is array) returns array
{
    const G1_COS  = cos(0.5 * degree);
    const POS_TOL = 1e-6 * meter;

    var derivConstraint = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
        derivConstraint = append(derivConstraint, { "startDeriv" : undefined, "endDeriv" : undefined });

    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var eA = edgeInfo[ei];
        for (var ej = ei + 1; ej < size(edgeInfo); ej += 1)
        {
            if (edgeInfo[ej] == undefined) { continue; }
            var eB = edgeInfo[ej];

            if (norm(eA.p1 - eB.p0) < POS_TOL && dot(-1 * eA.tan1, eB.tan0) < -G1_COS)
            {
                var dcA = derivConstraint[ei]; dcA.endDeriv   = eA.tan1; derivConstraint[ei] = dcA;
                var dcB = derivConstraint[ej]; dcB.startDeriv = eB.tan0; derivConstraint[ej] = dcB;
            }
            else if (norm(eA.p0 - eB.p1) < POS_TOL && dot(eA.tan0, -1 * eB.tan1) < -G1_COS)
            {
                var dcA = derivConstraint[ei]; dcA.startDeriv = eA.tan0; derivConstraint[ei] = dcA;
                var dcB = derivConstraint[ej]; dcB.endDeriv   = eB.tan1; derivConstraint[ej] = dcB;
            }
            else if (norm(eA.p1 - eB.p1) < POS_TOL && dot(-1 * eA.tan1, -1 * eB.tan1) < -G1_COS)
            {
                var dcA = derivConstraint[ei]; dcA.endDeriv = eA.tan1; derivConstraint[ei] = dcA;
                var dcB = derivConstraint[ej]; dcB.endDeriv = eB.tan1; derivConstraint[ej] = dcB;
            }
            else if (norm(eA.p0 - eB.p0) < POS_TOL && dot(eA.tan0, eB.tan0) < -G1_COS)
            {
                var dcA = derivConstraint[ei]; dcA.startDeriv = eA.tan0; derivConstraint[ei] = dcA;
                var dcB = derivConstraint[ej]; dcB.startDeriv = eB.tan0; derivConstraint[ej] = dcB;
            }
        }
    }
    return derivConstraint;
}

/**
 * Builds a BSpline offset wire for each periphery edge.
 * offsetPt = edgePt + offsetMag * outwardBinormal
 *
 * offsetDef fields: startFrameOrigin, endFrameOrigin, offsetType, offset,
 *   startOffset, endOffset, zeroSlopeAtStart, regionName
 */
export function buildVariableOffsetCurves(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number,
    splineDegree is number, tolerance is ValueWithUnits, maxCP is number) returns Query
{
    if (numPts < 2) { numPts = 2; }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    if (size(edgeArray) == 0) { return; }

    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);

    var edgeInfo = [];
    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];
        var face = adjacentFace(context, edge, sheetBody);
        if (face == undefined) { edgeInfo = append(edgeInfo, undefined); continue; }

        var tl0 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0, "arcLengthParameterization" : false });
        var tl1 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1, "arcLengthParameterization" : false });
        var ref = faceReferenceFrame(context, face);

        var tangentLines = evEdgeTangentLines(context, {
            "edge" : edge, "parameters" : uniformParams(numPts), "arcLengthParameterization" : true
        });

        var offPts = [];
        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt   = tangentLines[i].origin;
            var t        = regionT(edgePt, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2);
            var ptUV     = evDistance(context, { "side0" : face, "side1" : edgePt }).sides[0].parameter;
            var ptNormal = evFaceTangentPlane(context, { "face" : face, "parameter" : ptUV }).normal;
            var fr       = edgeOffsetFrame(ptNormal, ref.faceRefNormal, tangentLines[i].direction, edgePt, ref.faceCenterPt);
            offPts = append(offPts, edgePt + computeOffsetMag(offsetDef, t) * fr.binormal);
        }

        edgeInfo = append(edgeInfo, {
            "edge"  : edge, "face"  : face, "points" : offPts,
            "p0"    : tl0.origin, "p1"    : tl1.origin,
            "tan0"  : tl0.direction, "tan1"  : tl1.direction,
            "faceRefNormal" : ref.faceRefNormal
        });
    }

    var derivConstraint = detectG1Junctions(edgeInfo);

    var wireBodies = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var ed = edgeInfo[ei];
        var dc = derivConstraint[ei];

        var derivScale   = evLength(context, { "entities" : ed.edge }) / 3;
        var targetDef    = { "positions" : ed.points };
        if (dc.startDeriv != undefined) { targetDef = mergeMaps(targetDef, { "startDerivative" : dc.startDeriv * derivScale }); }
        if (dc.endDeriv   != undefined) { targetDef = mergeMaps(targetDef, { "endDerivative"   : dc.endDeriv   * derivScale }); }

        try
        {
            var splineData = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree,
                "tolerance" : tolerance, "maxControlPoints" : maxCP,
                "targets" : [approximationTarget(targetDef)]
            })[0];
            opCreateBSplineCurve(context, id + ("offCurve" ~ ei), { "bSplineCurve" : splineData });
            var curveBody = qCreatedBy(id + ("offCurve" ~ ei), EntityType.BODY);
            opExtractWires(context, id + ("offWire" ~ ei), { "edges" : qOwnedByBody(curveBody, EntityType.EDGE) });
            opDeleteBodies(context, id + ("deleteOffCurve" ~ ei), { "entities" : curveBody });
            wireBodies = append(wireBodies, qCreatedBy(id + ("offWire" ~ ei), EntityType.BODY));
        }
        catch { }
    }

    for (var wi = 0; wi < size(wireBodies); wi += 1)
    {
        setProperty(context, {
            "entities"     : wireBodies[wi],
            "propertyType" : PropertyType.NAME,
            "value"        : offsetDef.regionName ~ " offset wire"
        });
    }
    return size(wireBodies) > 0 ? qUnion(wireBodies) : qNothing();
}


// =====================================================================
// LOFT SURFACE BUILDING
// =====================================================================

/**
 * Builds a lofted wall surface per periphery edge.
 * Top profile   = offsetPt + wallHeight  * faceNormal
 * Bottom profile = offsetPt - wallHeight2 * faceNormal  (or offsetPt if !wallSecondDir)
 * Corner bisector snapping ensures adjacent patches share exact corner positions.
 *
 * offsetDef fields: startFrameOrigin, endFrameOrigin, offsetType, offset,
 *   startOffset, endOffset, zeroSlopeAtStart, regionName
 */
export function buildLoftSurfaces(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number,
    splineDegree is number, tolerance is ValueWithUnits, maxCP is number,
    wallHeight is ValueWithUnits, wallSecondDir is boolean, wallHeight2 is ValueWithUnits) returns Query
{
    if (numPts < 2) { numPts = 2; }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    if (size(edgeArray) == 0) { return; }

    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);
    const POS_TOL      = 1e-6 * meter;

    // --- Phase 1: per-edge data collection ---
    var edgeInfo = [];
    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];
        var face = adjacentFace(context, edge, sheetBody);
        if (face == undefined) { edgeInfo = append(edgeInfo, undefined); continue; }

        var ref = faceReferenceFrame(context, face);
        var tl0 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0, "arcLengthParameterization" : false });
        var tl1 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1, "arcLengthParameterization" : false });

        var uv0 = evDistance(context, { "side0" : face, "side1" : tl0.origin }).sides[0].parameter;
        var fr0 = edgeOffsetFrame(evFaceTangentPlane(context, { "face" : face, "parameter" : uv0 }).normal, ref.faceRefNormal, tl0.direction, tl0.origin, ref.faceCenterPt);

        var uv1 = evDistance(context, { "side0" : face, "side1" : tl1.origin }).sides[0].parameter;
        var fr1 = edgeOffsetFrame(evFaceTangentPlane(context, { "face" : face, "parameter" : uv1 }).normal, ref.faceRefNormal, tl1.direction, tl1.origin, ref.faceCenterPt);

        var om0 = computeOffsetMag(offsetDef, regionT(tl0.origin, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2));
        var om1 = computeOffsetMag(offsetDef, regionT(tl1.origin, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2));

        var tangentLines = evEdgeTangentLines(context, {
            "edge" : edge, "parameters" : uniformParams(numPts), "arcLengthParameterization" : true
        });

        var topPts    = [];
        var bottomPts = [];
        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt      = tangentLines[i].origin;
            var t           = regionT(edgePt, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2);
            var ptUV        = evDistance(context, { "side0" : face, "side1" : edgePt }).sides[0].parameter;
            var ptNormal    = evFaceTangentPlane(context, { "face" : face, "parameter" : ptUV }).normal;
            var fr          = edgeOffsetFrame(ptNormal, ref.faceRefNormal, tangentLines[i].direction, edgePt, ref.faceCenterPt);
            var offsetPt    = edgePt + computeOffsetMag(offsetDef, t) * fr.binormal;
            topPts    = append(topPts,    offsetPt + wallHeight * fr.faceNormal);
            bottomPts = append(bottomPts, wallSecondDir ? offsetPt - wallHeight2 * fr.faceNormal : offsetPt);
        }

        edgeInfo = append(edgeInfo, {
            "edge" : edge, "face" : face,
            "faceRefNormal" : ref.faceRefNormal, "faceCenterPt" : ref.faceCenterPt,
            "p0" : tl0.origin, "p1" : tl1.origin,
            "tan0" : tl0.direction, "tan1" : tl1.direction,
            "fn0" : fr0.faceNormal, "fn1" : fr1.faceNormal,
            "bn0" : fr0.binormal,   "bn1" : fr1.binormal,
            "om0" : om0, "om1" : om1,
            "topPts" : topPts, "bottomPts" : bottomPts
        });
    }

    // --- Phase 2: corner bisector computation ---
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var ed = edgeInfo[ei];

        var sumBn0 = ed.bn0;
        for (var ej = 0; ej < size(edgeInfo); ej += 1)
        {
            if (ej == ei || edgeInfo[ej] == undefined) { continue; }
            var eo = edgeInfo[ej];
            if      (norm(eo.p0 - ed.p0) < POS_TOL) { sumBn0 = sumBn0 + eo.bn0; }
            else if (norm(eo.p1 - ed.p0) < POS_TOL) { sumBn0 = sumBn0 + eo.bn1; }
        }
        var cornerOff0 = ed.p0 + ed.om0 * normalize(sumBn0);

        var sumBn1 = ed.bn1;
        for (var ej = 0; ej < size(edgeInfo); ej += 1)
        {
            if (ej == ei || edgeInfo[ej] == undefined) { continue; }
            var eo = edgeInfo[ej];
            if      (norm(eo.p0 - ed.p1) < POS_TOL) { sumBn1 = sumBn1 + eo.bn0; }
            else if (norm(eo.p1 - ed.p1) < POS_TOL) { sumBn1 = sumBn1 + eo.bn1; }
        }
        var cornerOff1 = ed.p1 + ed.om1 * normalize(sumBn1);

        var edCopy     = edgeInfo[ei];
        edCopy.ctStart = cornerOff0 + wallHeight * ed.fn0;
        edCopy.cbStart = wallSecondDir ? cornerOff0 - wallHeight2 * ed.fn0 : cornerOff0;
        edCopy.ctEnd   = cornerOff1 + wallHeight * ed.fn1;
        edCopy.cbEnd   = wallSecondDir ? cornerOff1 - wallHeight2 * ed.fn1 : cornerOff1;
        edgeInfo[ei]   = edCopy;
    }

    // --- Phase 3: G1 junction detection ---
    var derivConstraint = detectG1Junctions(edgeInfo);

    // --- Phase 4: fit BSplines, loft, collect patches ---
    var loftBodyQueries = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var ed = edgeInfo[ei];
        var dc = derivConstraint[ei];

        var derivScale = evLength(context, { "entities" : ed.edge }) / 3;

        var topPts    = ed.topPts;
        var bottomPts = ed.bottomPts;
        topPts[0]                      = ed.ctStart;
        topPts[size(topPts) - 1]       = ed.ctEnd;
        bottomPts[0]                   = ed.cbStart;
        bottomPts[size(bottomPts) - 1] = ed.cbEnd;

        var topTargetDef = { "positions" : topPts };
        var botTargetDef = { "positions" : bottomPts };
        if (dc.startDeriv != undefined)
        {
            topTargetDef = mergeMaps(topTargetDef, { "startDerivative" : dc.startDeriv * derivScale });
            botTargetDef = mergeMaps(botTargetDef, { "startDerivative" : dc.startDeriv * derivScale });
        }
        if (dc.endDeriv != undefined)
        {
            topTargetDef = mergeMaps(topTargetDef, { "endDerivative" : dc.endDeriv * derivScale });
            botTargetDef = mergeMaps(botTargetDef, { "endDerivative" : dc.endDeriv * derivScale });
        }

        var topSpline;
        var bottomSpline;
        try
        {
            topSpline = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree, "tolerance" : tolerance,
                "maxControlPoints" : maxCP, "targets" : [approximationTarget(topTargetDef)]
            })[0];
            var topCPs              = topSpline.controlPoints;
            topCPs[0]               = ed.ctStart;
            topCPs[size(topCPs) - 1] = ed.ctEnd;
            topSpline               = mergeMaps(topSpline, { "controlPoints" : topCPs });
        }
        catch { continue; }

        try
        {
            bottomSpline = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree, "tolerance" : tolerance,
                "maxControlPoints" : maxCP, "targets" : [approximationTarget(botTargetDef)]
            })[0];
            var botCPs                = bottomSpline.controlPoints;
            botCPs[0]                 = ed.cbStart;
            botCPs[size(botCPs) - 1]  = ed.cbEnd;
            bottomSpline              = mergeMaps(bottomSpline, { "controlPoints" : botCPs });
        }
        catch { continue; }

        var topCurveId    = id + ("loftTopCurve"    ~ ei);
        var bottomCurveId = id + ("loftBottomCurve" ~ ei);
        try { opCreateBSplineCurve(context, topCurveId,    { "bSplineCurve" : topSpline    }); }
        catch { continue; }
        try { opCreateBSplineCurve(context, bottomCurveId, { "bSplineCurve" : bottomSpline }); }
        catch { continue; }

        var topBody    = qCreatedBy(topCurveId,    EntityType.BODY);
        var bottomBody = qCreatedBy(bottomCurveId, EntityType.BODY);

        var loftId = id + ("loftPatch" ~ ei);
        try
        {
            opLoft(context, loftId, {
                "bodyType"          : ToolBodyType.SURFACE,
                "profileSubqueries" : [topBody, bottomBody]
            });
            loftBodyQueries = append(loftBodyQueries, qCreatedBy(loftId, EntityType.BODY));
        }
        catch { }

        try { opDeleteBodies(context, id + ("deleteLoftCurves" ~ ei), { "entities" : qUnion([topBody, bottomBody]) }); }
        catch { }
    }

    if (size(loftBodyQueries) > 1)
    {
        try
        {
            opBoolean(context, id + "unionLoftPatches", {
                "operationType" : BooleanOperationType.UNION,
                "tools"         : qUnion(loftBodyQueries)
            });
        }
        catch { }
    }

    if (size(loftBodyQueries) > 0)
    {
        setProperty(context, {
            "entities"     : qUnion(loftBodyQueries),
            "propertyType" : PropertyType.NAME,
            "value"        : offsetDef.regionName ~ " loft surface"
        });
    }
    return size(loftBodyQueries) > 0 ? qUnion(loftBodyQueries) : qNothing();
}


// =====================================================================
// INTERSECTION JOINING
// =====================================================================

/**
 * Post-processes joined intersections: trims loft surfaces and offset wires
 * back by joinStartOffset/joinEndOffset, then bridges the gap with a loft
 * surface or bridging spline.  Call this after the region loop.
 */
export function buildIntersectionJoins(context is Context, id is Id,
        definition is map, processedRegions is array)
{
    for (var i = 0; i < size(definition.intersections); i += 1)
    {
        var ix = definition.intersections[i];
        if (!ix.join)
        {
            continue;
        }
        var rA = ix.regionANum;
        var rB = ix.regionBNum;
        if (rA == undefined || rB == undefined || rA >= size(processedRegions) || rB >= size(processedRegions))
        {
            continue;
        }

        var regionA    = processedRegions[rA];
        var regionB = processedRegions[rB];
        
        var rAContinuity  = ix.startContinuity;      // IntersectionContinuityType
        var rBContinuity  = ix.endContinuity;        // IntersectionContinuityType
        var rAOffset      = ix.joinStartOffset;      // Length (how far to trim into region A)
        var rBOffset      = ix.joinEndOffset;        // Length (how far to trim into region B)
        
        var boundaryPl = plane(regionA.endFrame.origin, regionA.endFrame.zAxis);
        
        
        if (definition.returnLoftSurface)
        {
            var rASurf        = isQueryEmpty(context, regionA.loftBodyQuery) ? qNothing() : regionA.loftBodyQuery;   // Query | undefined
            var rBSurf        = isQueryEmpty(context, regionB.loftBodyQuery) ? qNothing() : regionB.loftBodyQuery;   // Query | undefined
            
            //addDebugEntities(context, qEdgeTopologyFilter(qOwnedByBody(rBSurf, EntityType.EDGE), EdgeTopology.ONE_SIDED), DebugColor.MAGENTA);
            
            var rAEdges = qEdgeTopologyFilter(qOwnedByBody(rASurf, EntityType.EDGE), EdgeTopology.ONE_SIDED);
            var rAVerticies = qUnion([qEdgeVertex(rAEdges, true), qEdgeVertex(rAEdges, false)]);
            
            var rASearchVerticies = evaluateQuery(context, rAVerticies);
            rASearchVerticies = mapArray(rASearchVerticies, function(x) {return evVertexPoint(context, {"vertex" : x});});
            rASearchVerticies = filter(rASearchVerticies, function(x) {return abs(dot((x - boundaryPl.origin), boundaryPl.normal)) < 0.1 * millimeter;});
            
            for (var pt = 0; pt < size(rASearchVerticies); pt += 1)
            {
                addDebugPoint(context, rASearchVerticies[pt], DebugColor.RED);
            }
            
            rASearchVerticies = deduplicate(rASearchVerticies);
            
            var rAPairs = greedyClosestPairs(rASearchVerticies);
            //println('pointPairs -> ' ~ rAPairs);
            
           var rALoftEdgeArr = [];
           for (var pr = 0; pr < size(rAPairs); pr += 1)
           {
                var pointPair = rAPairs[pr];
                var q1 = qContainsPoint(rAEdges, pointPair[0]);
                var q2 = qContainsPoint(rAEdges, pointPair[1]);
                rALoftEdgeArr = append(rALoftEdgeArr, qIntersection([q1, q2]));
           }
           
           var rALoftEdges = qUnion(rALoftEdgeArr);
           //addDebugEntities(context, rALoftEdges, DebugColor.CYAN);
           const trackerA = startTracking(context, rALoftEdges);
           
           if (rAOffset > 0 * millimeter)
           {
                definition.entities is Query;
                extendSurface(context, id + ('extendIntersection' ~ i ~ 'regionA'), {
                    "entities" : rALoftEdges,
                    "endCondition" : ExtendBoundingType.BLIND,
                    "oppositeDirection" : true,
                    "extendDistance" : rAOffset
                    });

            }
            var newEdgeQ = qUnion([qCreatedBy(id + ('extendIntersection' ~ i ~ 'regionA'), EntityType.EDGE)]);
            addDebugEntities(context, trackerA, DebugColor.CYAN);
           
            //Now B
            
            var rBEdges = qEdgeTopologyFilter(qOwnedByBody(rBSurf, EntityType.EDGE), EdgeTopology.ONE_SIDED);
            var rBVerticies = qUnion([qEdgeVertex(rBEdges, true), qEdgeVertex(rBEdges, false)]);
            
            var rBSearchVerticies = evaluateQuery(context, rBVerticies);
            rBSearchVerticies = mapArray(rBSearchVerticies, function(x) {return evVertexPoint(context, {"vertex" : x});});
            rBSearchVerticies = filter(rBSearchVerticies, function(x) {return abs(dot((x - boundaryPl.origin), boundaryPl.normal)) < 0.1 * millimeter;});
            
            for (var pt = 0; pt < size(rBSearchVerticies); pt += 1)
            {
                addDebugPoint(context, rBSearchVerticies[pt], DebugColor.BLUE);
            }
            
            rBSearchVerticies = deduplicate(rBSearchVerticies);
            
            var rBPairs = greedyClosestPairs(rBSearchVerticies);
            //println('pointPairs -> ' ~ rBPairs);
            
           var rBLoftEdgeArr = [];
           for (var pr = 0; pr < size(rBPairs); pr += 1)
           {
                var pointPair = rBPairs[pr];
                var q1 = qContainsPoint(rBEdges, pointPair[0]);
                var q2 = qContainsPoint(rBEdges, pointPair[1]);
                rBLoftEdgeArr = append(rBLoftEdgeArr, qIntersection([q1, q2]));
           }
           
           var rBLoftEdges = qUnion(rBLoftEdgeArr);
           //addDebugEntities(context, rALoftEdges, DebugColor.CYAN);
           const trackerB = startTracking(context, rBLoftEdges);
           
           if (rBOffset > 0 * millimeter)
           {
                definition.entities is Query;
                extendSurface(context, id + ('extendIntersection' ~ i ~ 'regionB'), {
                    "entities" : rBLoftEdges,
                    "endCondition" : ExtendBoundingType.BLIND,
                    "oppositeDirection" : true,
                    "extendDistance" : rBOffset
                    });

            }
            var newBEdgeQ = qUnion([qCreatedBy(id + ('extendIntersection' ~ i ~ 'regionB'), EntityType.EDGE)]);
            addDebugEntities(context, trackerB, DebugColor.MAGENTA);
            
            var bodyAEdges = evaluateQuery(context, trackerA);
            var bodyBEdges = evaluateQuery(context, trackerB);
            
            for (var b = 0; b < size(bodyAEdges); b += 1)
            {
                var midpoint = evEdgeTangentLine(context, {
                        "edge" : bodyAEdges[b],
                        "parameter" : 0.5
                }).origin;
                
                var bEdge = qClosestTo(qUnion(bodyBEdges), midpoint);
                
                var faceA = qIntersection(qAdjacent(bodyAEdges[b], AdjacencyType.EDGE, EntityType.FACE), qOwnedByBody(rASurf, EntityType.FACE));
                var faceB = qIntersection(qAdjacent(bEdge, AdjacencyType.EDGE, EntityType.FACE), qOwnedByBody(rBSurf, EntityType.FACE));
                
                var loftedSurface = surfaceLofter(context, id + ("loftRegion"~i~"edge"~b), bodyAEdges[b], faceA, bEdge, faceB, rAContinuity, rBContinuity);
            }
            
        
        }
        
        if (definition.returnOffsetWires)
        {
            var wireA = isQueryEmpty(context, regionA.wireBodyQuery) ? qNothing() : regionA.wireBodyQuery;   // Query | undefined
            var wireB = isQueryEmpty(context, regionB.wireBodyQuery) ? qNothing() : regionB.wireBodyQuery;   // Query | undefined
            
            var wireAVerticies = qOwnedByBody(wireA, EntityType.VERTEX);
            var wireBVerticies  = qOwnedByBody(wireB, EntityType.VERTEX);
            
            wireAVerticies = evaluateQuery(context, wireAVerticies);
            wireBVerticies = evaluateQuery(context, wireBVerticies);
            wireAVerticies  = mapArray(wireAVerticies, function(x) {return evVertexPoint(context, {"vertex" : x});});
            wireBVerticies  = mapArray(wireBVerticies, function(x) {return evVertexPoint(context, {"vertex" : x});});
            wireAVerticies = filter(wireAVerticies, function(x) {return abs(dot(x - boundaryPl.origin, boundaryPl.normal)) < 0.1 * millimeter;});
            wireBVerticies = filter(wireBVerticies, function(x) {return abs(dot(x - boundaryPl.origin, boundaryPl.normal)) < 0.1 * millimeter;});
            
            for (var pt = 0; pt < size(wireAVerticies); pt += 1)
            {
                var wireAPointQ = qClosestTo(qOwnedByBody(wireA, EntityType.VERTEX), wireAVerticies[pt]);
                var wireBPointQ = qClosestTo(qOwnedByBody(wireB, EntityType.VERTEX), wireAVerticies[pt]);
                var wireAEdge = qClosestTo(qOwnedByBody(wireA, EntityType.EDGE), wireAVerticies[pt]);
                var wireBEdge = qClosestTo(qOwnedByBody(wireB, EntityType.EDGE), wireAVerticies[pt]);
                
                bridgingCurve(context, id + ("bridgingCurveRegion" ~ i ~ "num" ~ pt), {
                    'side1' : qUnion([wireAPointQ, wireAEdge]),
                    'match1' : (rAContinuity == IntersectionContinuityType.G1) ? BridgingCurveMatchType.TANGENCY : BridgingCurveMatchType.POSITION,
                    'flip1' : (rAContinuity == IntersectionContinuityType.G1) ? false : undefined,
                    'side2' : qUnion([wireBPointQ, wireBEdge]),
                    'match2' : (rBContinuity == IntersectionContinuityType.G1) ? BridgingCurveMatchType.TANGENCY : BridgingCurveMatchType.POSITION,
                    'flip2' : (rBContinuity == IntersectionContinuityType.G1) ? false : undefined,
                    });
                    
                var createdWire = qCreatedBy(id + ("bridgingCurveRegion" ~ i ~ "num" ~ pt), EntityType.BODY);
            }
            
            
        }
        
    }
}

function surfaceLofter(context is Context, id is Id, edgeA is Query, faceA is Query, edgeB is Query, faceB is Query, rAContinuity is IntersectionContinuityType, rBContinuity is IntersectionContinuityType) returns Query
{
    
 var loftDef = {
     'bodyType' : ExtendedToolBodyType.SURFACE,
     'surfaceOperationType' : NewSurfaceOperationType.NEW,
     'wireProfilesArray' : [{ 'wireProfileEntities' : qUnion([edgeA]) },{ 'wireProfileEntities' : qUnion([edgeB]) }],
     'startCondition' : (rAContinuity == IntersectionContinuityType.G0) ? LoftEndDerivativeType.DEFAULT : LoftEndDerivativeType.MATCH_TANGENT,
     'startMagnitude' : (rAContinuity == IntersectionContinuityType.G1) ? 1 : undefined,
     'adjacentFacesStart' : faceA,
     'endCondition' : (rBContinuity == IntersectionContinuityType.G0) ? LoftEndDerivativeType.DEFAULT : LoftEndDerivativeType.MATCH_TANGENT,
     'endMagnitude' : (rBContinuity == IntersectionContinuityType.G1) ? 1 : undefined,
     'adjacentFacesEnd' : faceB,
     'trimProfiles' : false, 
     'makePeriodic' : false,
     'showIsocurves' : false,
     'defaultSurfaceScope' : true,
     'addSections' : false,
     'addGuides' : false
 };
 
 try
 {
     loft(context, id + ("loftOperation"), loftDef);
 }
 
 
 return qCreatedBy(id + ("loftOperation"), EntityType.BODY);
}


/**
 * Pairs points by greedily matching the globally closest remaining pair.
 * Returns an array of 2-element arrays: [[p1, p2], [p3, p4], ...]
 *
 * @param points {array} - array of Vector (3D points)
 */
function greedyClosestPairs(points is array) returns array
{
    
    if (size(points) % 2 != 0)
    {
        throw regenError("greedyClosestPairs: point count must be even, got " ~ toString(size(points)));
    }
    
    var remaining = points;
    var pairs = [];

    while (size(remaining) > 0)
    {
        var bestDist = undefined;
        var bestI = 0;
        var bestJ = 1;

        for (var i = 0; i < size(remaining) - 1; i += 1)
        {
            for (var j = i + 1; j < size(remaining); j += 1)
            {
                const d = norm(remaining[i] - remaining[j]);
                if (bestDist == undefined || d < bestDist)
                {
                    bestDist = d;
                    bestI = i;
                    bestJ = j;
                }
            }
        }

        pairs = append(pairs, [remaining[bestI], remaining[bestJ]]);
        remaining = removeElementAt(remaining, bestJ);
        remaining = removeElementAt(remaining, bestI);
    }

    return pairs;
}


