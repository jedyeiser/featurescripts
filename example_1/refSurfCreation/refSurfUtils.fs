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
 * Returns true if cross(faceNormal, edgeTangent) points INTO the face and
 * therefore needs to be negated to obtain the outward binormal.
 *
 * A small probe step is taken in the candidate binormal direction.  If the
 * resulting point is still on (or very near) the face, the candidate points
 * inward and must be flipped.  This is geometrically unambiguous and requires
 * no heuristics about face centers or reference normals.
 *
 * Call this once per edge at a representative point (e.g. edge midpoint).
 */
function binormalNeedsFlip(context is Context, face is Query,
    edgePt is Vector, faceNormal is Vector, edgeTangent is Vector) returns boolean
{
    const PROBE = 1e-4 * meter;
    var candidate = cross(faceNormal, edgeTangent);
    var testPt    = edgePt + PROBE * candidate;
    var dist      = evDistance(context, { "side0" : face, "side1" : testPt }).distance;
    return dist < PROBE * 0.5;
}

/**
 * Returns { faceNormal, binormal } at a sample point given a pre-computed flip flag.
 * flip is determined once per edge by binormalNeedsFlip and applied uniformly to
 * all samples on that edge.
 */
function edgeOffsetFrame(faceNormal is Vector, edgeTangent is Vector, flip is boolean) returns map
{
    var binormal = cross(faceNormal, edgeTangent);
    if (flip) { binormal = -1 * binormal; }
    return { "faceNormal" : faceNormal, "binormal" : binormal };
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

        // Determine binormal direction once at edge midpoint.
        var midTL  = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5, "arcLengthParameterization" : true });
        var midUV  = evDistance(context, { "side0" : face, "side1" : midTL.origin }).sides[0].parameter;
        var midN   = evFaceTangentPlane(context, { "face" : face, "parameter" : midUV }).normal;
        var flip   = binormalNeedsFlip(context, face, midTL.origin, midN, midTL.direction);

        var tangentLines = evEdgeTangentLines(context, {
            "edge" : edge, "parameters" : uniformParams(numPts), "arcLengthParameterization" : true
        });

        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt   = tangentLines[i].origin;
            var t        = regionT(edgePt, offsetDef.startFrameOrigin, regionAxis, regionAxisLen2);
            var ptUV     = evDistance(context, { "side0" : face, "side1" : edgePt }).sides[0].parameter;
            var ptNormal = evFaceTangentPlane(context, { "face" : face, "parameter" : ptUV }).normal;
            var fr       = edgeOffsetFrame(ptNormal, tangentLines[i].direction, flip);
            addDebugPoint(context, edgePt + computeOffsetMag(offsetDef, t) * fr.binormal, DebugColor.MAGENTA);
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

        var edgeLen  = evLength(context, { "entities" : edge });
        var arrowLen = edgeLen / numPts;
        var arrowRad = arrowLen * 0.05;

        // Determine binormal direction once at edge midpoint.
        var midTL  = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5, "arcLengthParameterization" : true });
        var midUV  = evDistance(context, { "side0" : face, "side1" : midTL.origin }).sides[0].parameter;
        var midN   = evFaceTangentPlane(context, { "face" : face, "parameter" : midUV }).normal;
        var flip   = binormalNeedsFlip(context, face, midTL.origin, midN, midTL.direction);

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
            var fr          = edgeOffsetFrame(ptNormal, edgeTangent, flip);

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

// Estimates the curvature vector (kappa * normal = dT/ds) at an edge endpoint
// via a forward finite difference on arc-length-parameterized tangents.
// atStart=true: compute at parameter 0; atStart=false: compute at parameter 1.
// Returns a vector with units 1/meter.
function edgeCurvatureVec(context is Context, edge is Query, atStart is boolean) returns Vector
{
    const DP = 0.02;
    var T0;
    var T1;
    if (atStart)
    {
        T0 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.0, "arcLengthParameterization" : true }).direction;
        T1 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : DP,  "arcLengthParameterization" : true }).direction;
    }
    else
    {
        T0 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1.0 - DP, "arcLengthParameterization" : true }).direction;
        T1 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1.0,      "arcLengthParameterization" : true }).direction;
    }
    var edgeLen = evLength(context, { "entities" : edge });
    return (T1 - T0) / (DP * edgeLen);
}

/**
 * Detects G1 and G2 continuity junctions between adjacent edges.
 * G1: turning angle < 0.5 deg at shared endpoint.
 * G2: G1 + curvature vectors agree within G2_REL_TOL relative tolerance.
 *
 * Returns array (one entry per edgeInfo element):
 *   { startDeriv, endDeriv, startCurvVec, endCurvVec }
 * Defined fields indicate a junction of that type.
 * startDeriv/endDeriv: tangent at junction (units: unitless direction).
 * startCurvVec/endCurvVec: curvature vector at G2 junction (units: 1/meter).
 * edgeInfo entries must have: p0, p1, tan0, tan1, edge fields (or be undefined).
 */
function detectContinuityConstraints(context is Context, edgeInfo is array) returns array
{
    const G1_COS     = cos(0.5 * degree);
    const POS_TOL    = 1e-6 * meter;
    const G2_REL_TOL = 0.20;

    var constraints = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        constraints = append(constraints, {
            "startDeriv"   : undefined, "endDeriv"   : undefined,
            "startCurvVec" : undefined, "endCurvVec" : undefined
        });
    }

    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var eA = edgeInfo[ei];
        for (var ej = ei + 1; ej < size(edgeInfo); ej += 1)
        {
            if (edgeInfo[ej] == undefined) { continue; }
            var eB = edgeInfo[ej];

            // Config 1: eA.p1 -- eB.p0
            if (norm(eA.p1 - eB.p0) < POS_TOL && dot(-1 * eA.tan1, eB.tan0) < -G1_COS)
            {
                var cA = constraints[ei]; cA.endDeriv   = eA.tan1; constraints[ei] = cA;
                var cB = constraints[ej]; cB.startDeriv = eB.tan0; constraints[ej] = cB;
                var cvA    = edgeCurvatureVec(context, eA.edge, false);
                var cvB    = edgeCurvatureVec(context, eB.edge, true);
                var cvAMag = norm(cvA);
                var cvBMag = norm(cvB);
                var cvAvg  = cvAMag > cvBMag ? cvAMag : cvBMag;
                if (cvAvg * meter > 1e-4 && norm(cvA - cvB) / cvAvg < G2_REL_TOL)
                {
                    cA = constraints[ei]; cA.endCurvVec   = cvA; constraints[ei] = cA;
                    cB = constraints[ej]; cB.startCurvVec = cvB; constraints[ej] = cB;
                }
            }
            // Config 2: eA.p0 -- eB.p1
            else if (norm(eA.p0 - eB.p1) < POS_TOL && dot(eA.tan0, -1 * eB.tan1) < -G1_COS)
            {
                var cA = constraints[ei]; cA.startDeriv = eA.tan0; constraints[ei] = cA;
                var cB = constraints[ej]; cB.endDeriv   = eB.tan1; constraints[ej] = cB;
                var cvA    = edgeCurvatureVec(context, eA.edge, true);
                var cvB    = edgeCurvatureVec(context, eB.edge, false);
                var cvAMag = norm(cvA);
                var cvBMag = norm(cvB);
                var cvAvg  = cvAMag > cvBMag ? cvAMag : cvBMag;
                if (cvAvg * meter > 1e-4 && norm(cvA - cvB) / cvAvg < G2_REL_TOL)
                {
                    cA = constraints[ei]; cA.startCurvVec = cvA; constraints[ei] = cA;
                    cB = constraints[ej]; cB.endCurvVec   = cvB; constraints[ej] = cB;
                }
            }
            // Config 3: eA.p1 -- eB.p1
            else if (norm(eA.p1 - eB.p1) < POS_TOL && dot(-1 * eA.tan1, -1 * eB.tan1) < -G1_COS)
            {
                var cA = constraints[ei]; cA.endDeriv = eA.tan1; constraints[ei] = cA;
                var cB = constraints[ej]; cB.endDeriv = eB.tan1; constraints[ej] = cB;
                var cvA    = edgeCurvatureVec(context, eA.edge, false);
                var cvB    = edgeCurvatureVec(context, eB.edge, false);
                var cvAMag = norm(cvA);
                var cvBMag = norm(cvB);
                var cvAvg  = cvAMag > cvBMag ? cvAMag : cvBMag;
                if (cvAvg * meter > 1e-4 && norm(cvA - cvB) / cvAvg < G2_REL_TOL)
                {
                    cA = constraints[ei]; cA.endCurvVec = cvA; constraints[ei] = cA;
                    cB = constraints[ej]; cB.endCurvVec = cvB; constraints[ej] = cB;
                }
            }
            // Config 4: eA.p0 -- eB.p0
            else if (norm(eA.p0 - eB.p0) < POS_TOL && dot(eA.tan0, eB.tan0) < -G1_COS)
            {
                var cA = constraints[ei]; cA.startDeriv = eA.tan0; constraints[ei] = cA;
                var cB = constraints[ej]; cB.startDeriv = eB.tan0; constraints[ej] = cB;
                var cvA    = edgeCurvatureVec(context, eA.edge, true);
                var cvB    = edgeCurvatureVec(context, eB.edge, true);
                var cvAMag = norm(cvA);
                var cvBMag = norm(cvB);
                var cvAvg  = cvAMag > cvBMag ? cvAMag : cvBMag;
                if (cvAvg * meter > 1e-4 && norm(cvA - cvB) / cvAvg < G2_REL_TOL)
                {
                    cA = constraints[ei]; cA.startCurvVec = cvA; constraints[ei] = cA;
                    cB = constraints[ej]; cB.startCurvVec = cvB; constraints[ej] = cB;
                }
            }
        }
    }
    return constraints;
}

// Groups edgeInfo entries into connected chains via endpoint matching.
// Returns an array of chains; each chain is an array of { edgeIndex, reversed }.
// reversed=true means traverse that edge p1->p0 in the chain.
function chainEdges(edgeInfo is array) returns array
{
    const POS_TOL = 1e-6 * meter;
    var n       = size(edgeInfo);
    var inChain = [];
    for (var i = 0; i < n; i += 1)
    {
        inChain = append(inChain, false);
    }

    var chains = [];

    for (var si = 0; si < n; si += 1)
    {
        if (inChain[si] || edgeInfo[si] == undefined) { continue; }
        inChain[si] = true;

        // Forward chain: start with si (not reversed), extend from si.p1
        var fwdChain = [{ "edgeIndex" : si, "reversed" : false }];
        var endPt    = edgeInfo[si].p1;
        var growing  = true;
        while (growing)
        {
            growing = false;
            for (var j = 0; j < n; j += 1)
            {
                if (inChain[j] || edgeInfo[j] == undefined) { continue; }
                if (norm(edgeInfo[j].p0 - endPt) < POS_TOL)
                {
                    fwdChain   = append(fwdChain, { "edgeIndex" : j, "reversed" : false });
                    inChain[j] = true;
                    endPt      = edgeInfo[j].p1;
                    growing    = true;
                    break;
                }
                else if (norm(edgeInfo[j].p1 - endPt) < POS_TOL)
                {
                    fwdChain   = append(fwdChain, { "edgeIndex" : j, "reversed" : true });
                    inChain[j] = true;
                    endPt      = edgeInfo[j].p0;
                    growing    = true;
                    break;
                }
            }
        }

        // Backward chain: extend from si.p0
        var bwdChain = [];
        var startPt  = edgeInfo[si].p0;
        var goBack   = true;
        while (goBack)
        {
            goBack = false;
            for (var j = 0; j < n; j += 1)
            {
                if (inChain[j] || edgeInfo[j] == undefined) { continue; }
                if (norm(edgeInfo[j].p1 - startPt) < POS_TOL)
                {
                    bwdChain   = append(bwdChain, { "edgeIndex" : j, "reversed" : false });
                    inChain[j] = true;
                    startPt    = edgeInfo[j].p0;
                    goBack     = true;
                    break;
                }
                else if (norm(edgeInfo[j].p0 - startPt) < POS_TOL)
                {
                    bwdChain   = append(bwdChain, { "edgeIndex" : j, "reversed" : true });
                    inChain[j] = true;
                    startPt    = edgeInfo[j].p1;
                    goBack     = true;
                    break;
                }
            }
        }

        // Full chain: bwdChain in reverse order (traversal flags preserved) + fwdChain
        var fullChain = [];
        for (var bi = size(bwdChain) - 1; bi >= 0; bi -= 1)
        {
            fullChain = append(fullChain, bwdChain[bi]);
        }
        for (var fi = 0; fi < size(fwdChain); fi += 1)
        {
            fullChain = append(fullChain, fwdChain[fi]);
        }

        chains = append(chains, fullChain);
    }

    return chains;
}

/**
 * Builds a BSpline offset wire for each periphery edge.
 * offsetPt = edgePt + offsetMag * outwardBinormal
 *
 * All offset types — including CONSTANT — use the same sampling pipeline so that
 * direction is always determined by a geometric probe (binormalNeedsFlip).
 * This avoids the direction ambiguity of @opOffsetCurveOnFace, whose oppositeDirection
 * flag depends on face orientation x edge orientation rather than user sign convention.
 *
 * offsetDef fields: startFrameOrigin, endFrameOrigin, offsetType, offset,
 *   startOffset, endOffset, zeroSlopeAtStart, regionName
 */
export function buildVariableOffsetCurves(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number,
    splineDegree is number, tolerance is ValueWithUnits, maxCP is number,
    debugDef is map) returns Query
{
    // All offset types use the sampling pipeline.
    if (numPts < 2) { numPts = 2; }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    if (size(edgeArray) == 0) { return qNothing(); }

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

        // Log source edge BSpline metadata
        if (debugDef.logSplineMeta)
        {
            var curveDef = evCurveDefinition(context, { "edge" : edge });
            var edgeLen  = evLength(context, { "entities" : edge });
            println("  src edge " ~ ei ~ ": type=" ~ curveDef.curveType ~ " len=" ~ edgeLen / meter ~ "m");
            if (curveDef.curveType == CurveType.CIRCLE)
            {
                println("    circle radius=" ~ curveDef.radius / meter ~ "m");
            }
            else if (curveDef.curveType == CurveType.SPLINE)
            {
                var bs = evApproximateBSplineCurve(context, { "edge" : edge });
                println("    bspline deg=" ~ bs.degree ~ " CPs=" ~ size(bs.controlPoints) ~ " knots=" ~ size(bs.knots));
            }
            else
            {
                println("    type=" ~ curveDef.curveType);
            }
        }

        // Determine binormal flip direction once at edge midpoint.
        var midTL  = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5, "arcLengthParameterization" : true });
        var midUV  = evDistance(context, { "side0" : face, "side1" : midTL.origin }).sides[0].parameter;
        var midN   = evFaceTangentPlane(context, { "face" : face, "parameter" : midUV }).normal;
        var flip   = binormalNeedsFlip(context, face, midTL.origin, midN, midTL.direction);

        if (debugDef.logNormals)
        {
            println("  edge " ~ ei ~ ": midNormal=" ~ midN ~ " flip=" ~ flip);
        }

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
            var fr       = edgeOffsetFrame(ptNormal, tangentLines[i].direction, flip);
            if (debugDef.logNormals)
            {
                println("    samp " ~ i ~ " t=" ~ t ~ " mag=" ~ computeOffsetMag(offsetDef, t) / meter ~ "m binormal=" ~ fr.binormal);
            }
            offPts = append(offPts, edgePt + computeOffsetMag(offsetDef, t) * fr.binormal);
        }

        edgeInfo = append(edgeInfo, {
            "edge"  : edge, "face"  : face, "points" : offPts,
            "p0"    : tl0.origin, "p1"    : tl1.origin,
            "tan0"  : tl0.direction, "tan1"  : tl1.direction
        });
    }

    var wireBodies = [];

    if (offsetDef.singleCurve == true)
    {
        // Single-curve mode: chain all connected edges and fit one spline per chain.
        var chains = chainEdges(edgeInfo);
        for (var ci = 0; ci < size(chains); ci += 1)
        {
            var chain    = chains[ci];
            var chainPts = [];
            for (var li = 0; li < size(chain); li += 1)
            {
                var link = chain[li];
                if (edgeInfo[link.edgeIndex] == undefined) { continue; }
                var pts = edgeInfo[link.edgeIndex].points;
                if (link.reversed)
                {
                    var endIdx = (li == 0) ? size(pts) - 1 : size(pts) - 2;
                    for (var pi = endIdx; pi >= 0; pi -= 1)
                    {
                        chainPts = append(chainPts, pts[pi]);
                    }
                }
                else
                {
                    var startIdx = (li == 0) ? 0 : 1;
                    for (var pi = startIdx; pi < size(pts); pi += 1)
                    {
                        chainPts = append(chainPts, pts[pi]);
                    }
                }
            }
            if (size(chainPts) < 2) { continue; }
            if (debugDef.logSplineMeta)
            {
                println("  single-curve chain " ~ ci ~ ": " ~ size(chain) ~ " edges, " ~ size(chainPts) ~ " pts");
            }
            var splineData = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree,
                "tolerance" : tolerance, "maxControlPoints" : maxCP,
                "targets" : [approximationTarget({ "positions" : chainPts })]
            })[0];
            if (debugDef.logSplineMeta)
            {
                println("    chain " ~ ci ~ " spline: deg=" ~ splineData.degree ~ " CPs=" ~ size(splineData.controlPoints));
            }
            opCreateBSplineCurve(context, id + ("singleCurve" ~ ci), { "bSplineCurve" : splineData });
            var curveBody = qCreatedBy(id + ("singleCurve" ~ ci), EntityType.BODY);
            opExtractWires(context, id + ("singleWire" ~ ci), { "edges" : qOwnedByBody(curveBody, EntityType.EDGE) });
            opDeleteBodies(context, id + ("deleteSingleCurve" ~ ci), { "entities" : curveBody });
            wireBodies = append(wireBodies, qCreatedBy(id + ("singleWire" ~ ci), EntityType.BODY));
        }
    }
    else
    {
        // Per-edge mode: fit one spline per edge with G1/G2 junction constraints.
        var constraints = detectContinuityConstraints(context, edgeInfo);
        for (var ei = 0; ei < size(edgeInfo); ei += 1)
        {
            if (edgeInfo[ei] == undefined) { continue; }
            var ed = edgeInfo[ei];
            var dc = constraints[ei];

            var derivScale = evLength(context, { "entities" : ed.edge }) / 3;
            var targetDef  = { "positions" : ed.points };
            if (dc.startDeriv   != undefined) { targetDef = mergeMaps(targetDef, { "startDerivative"       : dc.startDeriv   * derivScale }); }
            if (dc.endDeriv     != undefined) { targetDef = mergeMaps(targetDef, { "endDerivative"         : dc.endDeriv     * derivScale }); }
            if (dc.startCurvVec != undefined) { targetDef = mergeMaps(targetDef, { "startSecondDerivative" : dc.startCurvVec * derivScale * derivScale }); }
            if (dc.endCurvVec   != undefined) { targetDef = mergeMaps(targetDef, { "endSecondDerivative"   : dc.endCurvVec   * derivScale * derivScale }); }

            var splineData = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree,
                "tolerance" : tolerance, "maxControlPoints" : maxCP,
                "targets" : [approximationTarget(targetDef)]
            })[0];
            if (debugDef.logSplineMeta)
            {
                println("  offset spline edge " ~ ei ~ ": deg=" ~ splineData.degree ~
                    " CPs=" ~ size(splineData.controlPoints) ~ " knots=" ~ size(splineData.knots));
                if (dc.startDeriv   != undefined) { println("    startDeriv constraint applied"); }
                if (dc.endDeriv     != undefined) { println("    endDeriv constraint applied"); }
                if (dc.startCurvVec != undefined) { println("    startG2 constraint applied"); }
                if (dc.endCurvVec   != undefined) { println("    endG2 constraint applied"); }
            }
            opCreateBSplineCurve(context, id + ("offCurve" ~ ei), { "bSplineCurve" : splineData });
            var curveBody = qCreatedBy(id + ("offCurve" ~ ei), EntityType.BODY);
            opExtractWires(context, id + ("offWire" ~ ei), { "edges" : qOwnedByBody(curveBody, EntityType.EDGE) });
            opDeleteBodies(context, id + ("deleteOffCurve" ~ ei), { "entities" : curveBody });
            wireBodies = append(wireBodies, qCreatedBy(id + ("offWire" ~ ei), EntityType.BODY));
        }
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
    wallHeight is ValueWithUnits, wallSecondDir is boolean, wallHeight2 is ValueWithUnits,
    debugDef is map) returns Query
{
    if (numPts < 2) { numPts = 2; }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    if (size(edgeArray) == 0) { return qNothing(); }

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

        var tl0 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0, "arcLengthParameterization" : false });
        var tl1 = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1, "arcLengthParameterization" : false });

        // Log source edge BSpline metadata
        if (debugDef.logSplineMeta)
        {
            var curveDef = evCurveDefinition(context, { "edge" : edge });
            var edgeLen  = evLength(context, { "entities" : edge });
            println("  src edge " ~ ei ~ ": type=" ~ curveDef.curveType ~ " len=" ~ edgeLen / meter ~ "m");
            if (curveDef.curveType == CurveType.CIRCLE)
            {
                println("    circle radius=" ~ curveDef.radius / meter ~ "m");
            }
            else if (curveDef.curveType == CurveType.SPLINE)
            {
                var bs = evApproximateBSplineCurve(context, { "edge" : edge });
                println("    bspline deg=" ~ bs.degree ~ " CPs=" ~ size(bs.controlPoints) ~ " knots=" ~ size(bs.knots));
            }
            else
            {
                println("    type=" ~ curveDef.curveType);
            }
        }

        // Determine binormal flip direction once at edge midpoint.
        var midTL  = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0.5, "arcLengthParameterization" : true });
        var midUV  = evDistance(context, { "side0" : face, "side1" : midTL.origin }).sides[0].parameter;
        var midN   = evFaceTangentPlane(context, { "face" : face, "parameter" : midUV }).normal;
        var flip   = binormalNeedsFlip(context, face, midTL.origin, midN, midTL.direction);

        if (debugDef.logNormals)
        {
            println("  edge " ~ ei ~ ": midNormal=" ~ midN ~ " flip=" ~ flip);
        }

        var uv0 = evDistance(context, { "side0" : face, "side1" : tl0.origin }).sides[0].parameter;
        var fr0 = edgeOffsetFrame(evFaceTangentPlane(context, { "face" : face, "parameter" : uv0 }).normal, tl0.direction, flip);

        var uv1 = evDistance(context, { "side0" : face, "side1" : tl1.origin }).sides[0].parameter;
        var fr1 = edgeOffsetFrame(evFaceTangentPlane(context, { "face" : face, "parameter" : uv1 }).normal, tl1.direction, flip);

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
            var fr          = edgeOffsetFrame(ptNormal, tangentLines[i].direction, flip);
            if (debugDef.logNormals)
            {
                println("    loft samp " ~ i ~ " t=" ~ t ~ " binormal=" ~ fr.binormal);
            }
            var offsetPt    = edgePt + computeOffsetMag(offsetDef, t) * fr.binormal;
            topPts    = append(topPts,    offsetPt + wallHeight * fr.faceNormal);
            bottomPts = append(bottomPts, wallSecondDir ? offsetPt - wallHeight2 * fr.faceNormal : offsetPt);
        }

        edgeInfo = append(edgeInfo, {
            "edge" : edge, "face" : face,
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

    // --- Phase 3+4: fit BSplines, loft, collect patches ---
    var loftBodyQueries = [];

    if (offsetDef.singleCurve == true)
    {
        // Single-curve mode: chain all connected edges, concatenate top/bottom points,
        // fit one spline pair per chain, produce one loft surface per chain.
        var chains = chainEdges(edgeInfo);
        for (var ci = 0; ci < size(chains); ci += 1)
        {
            var chain    = chains[ci];
            var chainTop = [];
            var chainBot = [];
            for (var li = 0; li < size(chain); li += 1)
            {
                var link = chain[li];
                if (edgeInfo[link.edgeIndex] == undefined) { continue; }
                var ed = edgeInfo[link.edgeIndex];

                // Apply corner snapping to this edge's raw point arrays.
                var topPts    = ed.topPts;
                var bottomPts = ed.bottomPts;
                topPts[0]                      = ed.ctStart;
                topPts[size(topPts) - 1]       = ed.ctEnd;
                bottomPts[0]                   = ed.cbStart;
                bottomPts[size(bottomPts) - 1] = ed.cbEnd;

                if (link.reversed)
                {
                    // Traverse p1->p0: read arrays in reverse.
                    // For li>0 the junction point is topPts[size-1]; skip it.
                    var startPi = (li == 0) ? size(topPts) - 1 : size(topPts) - 2;
                    for (var pi = startPi; pi >= 0; pi -= 1)
                    {
                        chainTop = append(chainTop, topPts[pi]);
                        chainBot = append(chainBot, bottomPts[pi]);
                    }
                }
                else
                {
                    // Traverse p0->p1: read arrays forward.
                    // For li>0 the junction point is topPts[0]; skip it.
                    var startIdx = (li == 0) ? 0 : 1;
                    for (var pi = startIdx; pi < size(topPts); pi += 1)
                    {
                        chainTop = append(chainTop, topPts[pi]);
                        chainBot = append(chainBot, bottomPts[pi]);
                    }
                }
            }
            if (size(chainTop) < 2) { continue; }

            var topSpline = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree, "tolerance" : tolerance,
                "maxControlPoints" : maxCP, "targets" : [approximationTarget({ "positions" : chainTop })]
            })[0];
            var topCPs               = topSpline.controlPoints;
            topCPs[0]                = chainTop[0];
            topCPs[size(topCPs) - 1] = chainTop[size(chainTop) - 1];
            topSpline                = mergeMaps(topSpline, { "controlPoints" : topCPs });

            var bottomSpline = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree, "tolerance" : tolerance,
                "maxControlPoints" : maxCP, "targets" : [approximationTarget({ "positions" : chainBot })]
            })[0];
            var botCPs               = bottomSpline.controlPoints;
            botCPs[0]                = chainBot[0];
            botCPs[size(botCPs) - 1] = chainBot[size(chainBot) - 1];
            bottomSpline             = mergeMaps(bottomSpline, { "controlPoints" : botCPs });

            var topCurveId    = id + ("loftTopChain"    ~ ci);
            var bottomCurveId = id + ("loftBottomChain" ~ ci);
            opCreateBSplineCurve(context, topCurveId,    { "bSplineCurve" : topSpline    });
            opCreateBSplineCurve(context, bottomCurveId, { "bSplineCurve" : bottomSpline });

            var topCurveBody    = qCreatedBy(topCurveId,    EntityType.BODY);
            var bottomCurveBody = qCreatedBy(bottomCurveId, EntityType.BODY);

            var topWireId    = id + ("loftTopChainWire"    ~ ci);
            var bottomWireId = id + ("loftBottomChainWire" ~ ci);
            opExtractWires(context, topWireId,    { "edges" : qOwnedByBody(topCurveBody,    EntityType.EDGE) });
            opExtractWires(context, bottomWireId, { "edges" : qOwnedByBody(bottomCurveBody, EntityType.EDGE) });
            opDeleteBodies(context, id + ("deleteLoftChainCurves" ~ ci), { "entities" : qUnion([topCurveBody, bottomCurveBody]) });

            var topWireBody    = qCreatedBy(topWireId,    EntityType.BODY);
            var bottomWireBody = qCreatedBy(bottomWireId, EntityType.BODY);

            var loftId = id + ("loftChain" ~ ci);
            opLoft(context, loftId, {
                "bodyType"          : ToolBodyType.SURFACE,
                "profileSubqueries" : [topWireBody, bottomWireBody]
            });
            loftBodyQueries = append(loftBodyQueries, qCreatedBy(loftId, EntityType.BODY));
            opDeleteBodies(context, id + ("deleteLoftChainWires" ~ ci), { "entities" : qUnion([topWireBody, bottomWireBody]) });
        }
    }
    else
    {
        // Per-edge mode: one loft patch per edge with G1/G2 junction constraints.
        var constraints = detectContinuityConstraints(context, edgeInfo);
        for (var ei = 0; ei < size(edgeInfo); ei += 1)
        {
            if (edgeInfo[ei] == undefined) { continue; }
            var ed = edgeInfo[ei];
            var dc = constraints[ei];

            var derivScale = evLength(context, { "entities" : ed.edge }) / 3;

            var topPts    = ed.topPts;
            var bottomPts = ed.bottomPts;
            topPts[0]                      = ed.ctStart;
            topPts[size(topPts) - 1]       = ed.ctEnd;
            bottomPts[0]                   = ed.cbStart;
            bottomPts[size(bottomPts) - 1] = ed.cbEnd;

            var topTargetDef = { "positions" : topPts };
            var botTargetDef = { "positions" : bottomPts };
            if (dc.startDeriv   != undefined)
            {
                topTargetDef = mergeMaps(topTargetDef, { "startDerivative" : dc.startDeriv * derivScale });
                botTargetDef = mergeMaps(botTargetDef, { "startDerivative" : dc.startDeriv * derivScale });
            }
            if (dc.endDeriv     != undefined)
            {
                topTargetDef = mergeMaps(topTargetDef, { "endDerivative" : dc.endDeriv * derivScale });
                botTargetDef = mergeMaps(botTargetDef, { "endDerivative" : dc.endDeriv * derivScale });
            }
            if (dc.startCurvVec != undefined)
            {
                topTargetDef = mergeMaps(topTargetDef, { "startSecondDerivative" : dc.startCurvVec * derivScale * derivScale });
                botTargetDef = mergeMaps(botTargetDef, { "startSecondDerivative" : dc.startCurvVec * derivScale * derivScale });
            }
            if (dc.endCurvVec   != undefined)
            {
                topTargetDef = mergeMaps(topTargetDef, { "endSecondDerivative" : dc.endCurvVec * derivScale * derivScale });
                botTargetDef = mergeMaps(botTargetDef, { "endSecondDerivative" : dc.endCurvVec * derivScale * derivScale });
            }

            var topSpline = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree, "tolerance" : tolerance,
                "maxControlPoints" : maxCP, "targets" : [approximationTarget(topTargetDef)]
            })[0];
            var topCPs               = topSpline.controlPoints;
            topCPs[0]                = ed.ctStart;
            topCPs[size(topCPs) - 1] = ed.ctEnd;
            topSpline                = mergeMaps(topSpline, { "controlPoints" : topCPs });

            var bottomSpline = approximateSpline(context, {
                "isPeriodic" : false, "degree" : splineDegree, "tolerance" : tolerance,
                "maxControlPoints" : maxCP, "targets" : [approximationTarget(botTargetDef)]
            })[0];
            var botCPs                = bottomSpline.controlPoints;
            botCPs[0]                 = ed.cbStart;
            botCPs[size(botCPs) - 1]  = ed.cbEnd;
            bottomSpline              = mergeMaps(bottomSpline, { "controlPoints" : botCPs });

            var topCurveId    = id + ("loftTopCurve"    ~ ei);
            var bottomCurveId = id + ("loftBottomCurve" ~ ei);
            opCreateBSplineCurve(context, topCurveId,    { "bSplineCurve" : topSpline    });
            opCreateBSplineCurve(context, bottomCurveId, { "bSplineCurve" : bottomSpline });

            var topCurveBody    = qCreatedBy(topCurveId,    EntityType.BODY);
            var bottomCurveBody = qCreatedBy(bottomCurveId, EntityType.BODY);

            var topWireId    = id + ("loftTopWire"    ~ ei);
            var bottomWireId = id + ("loftBottomWire" ~ ei);
            opExtractWires(context, topWireId,    { "edges" : qOwnedByBody(topCurveBody,    EntityType.EDGE) });
            opExtractWires(context, bottomWireId, { "edges" : qOwnedByBody(bottomCurveBody, EntityType.EDGE) });
            opDeleteBodies(context, id + ("deleteLoftCurves" ~ ei), { "entities" : qUnion([topCurveBody, bottomCurveBody]) });

            var topWireBody    = qCreatedBy(topWireId,    EntityType.BODY);
            var bottomWireBody = qCreatedBy(bottomWireId, EntityType.BODY);

            var loftId = id + ("loftPatch" ~ ei);
            opLoft(context, loftId, {
                "bodyType"          : ToolBodyType.SURFACE,
                "profileSubqueries" : [topWireBody, bottomWireBody]
            });
            loftBodyQueries = append(loftBodyQueries, qCreatedBy(loftId, EntityType.BODY));
            opDeleteBodies(context, id + ("deleteLoftWires" ~ ei), { "entities" : qUnion([topWireBody, bottomWireBody]) });
        }
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

// Returns all ONE_SIDED edges of bodies whose base endpoint (parameter 0.0)
// AND midpoint (parameter 0.5) both lie within 1 mm of pl.
// Checking pt0 alone admits rail/side edges that terminate at the plane but
// run away from it.  Adding the midpoint check filters those out while still
// accepting true cap edges (which lie in the plane throughout their length),
// including the tall-wall case where pt1 may deviate from the plane.
function edgesNearPlane(context is Context, bodies is Query, pl is Plane) returns array
{
    const TOL = 1e-3 * meter;
    var candidates = evaluateQuery(context, qEdgeTopologyFilter(
            qOwnedByBody(bodies, EntityType.EDGE), EdgeTopology.ONE_SIDED));
    var result = [];
    for (var e in candidates)
    {
        var pt0  = evEdgeTangentLine(context, {
            "edge" : e, "parameter" : 0.0, "arcLengthParameterization" : true
        }).origin;
        var ptMid = evEdgeTangentLine(context, {
            "edge" : e, "parameter" : 0.5, "arcLengthParameterization" : true
        }).origin;
        if (abs(dot(pt0  - pl.origin, pl.normal)) < TOL &&
            abs(dot(ptMid - pl.origin, pl.normal)) < TOL)
        {
            result = append(result, e);
        }
    }
    return result;
}

/**
 * Splits body at boundPlane, keeps the piece whose bounding-box center is
 * closest to probePoint, deletes the other.  Returns the kept body query.
 * If the plane does not intersect the body, returns body unchanged.
 */
function splitAndKeepLocal(context is Context, id is Id, body is Query,
        boundPlane is Plane, probePoint is Vector) returns Query
{
    opPlane(context, id + "pl", { "plane" : boundPlane });
    var planeQ  = qCreatedBy(id + "pl", EntityType.BODY);
    var splitOk = false;
    try
    {
        opSplitPart(context, id + "split", {
            "targets"   : body,
            "tool"      : planeQ,
            "keepTools" : false
        });
        splitOk = true;
    }
    catch {}
    try silent(opDeleteBodies(context, id + "delPl", { "entities" : planeQ }));

    if (!splitOk)
    {
        return body;
    }

    var pieceA = qSplitBy(id + "split", EntityType.BODY, false);
    var pieceB = qSplitBy(id + "split", EntityType.BODY, true);
    var bbA    = evBox3d(context, { "topology" : pieceA, "tight" : true });
    var bbB    = evBox3d(context, { "topology" : pieceB, "tight" : true });
    var cA     = (bbA.minCorner + bbA.maxCorner) / 2;
    var cB     = (bbB.minCorner + bbB.maxCorner) / 2;

    if (norm(cA - probePoint) <= norm(cB - probePoint))
    {
        opDeleteBodies(context, id + "del", { "entities" : pieceB });
        return pieceA;
    }
    else
    {
        opDeleteBodies(context, id + "del", { "entities" : pieceA });
        return pieceB;
    }
}


/**
 * Post-processes joined intersections: trims loft surfaces and offset wires
 * back by joinStartOffset/joinEndOffset (measured as arc-length along the ref
 * wire), then bridges the gap with a loft surface or bridging spline.
 * Trim planes are placed normal to the ref wire at the offset position, not
 * as Euclidean offsets of the boundary plane.  When an offset is zero no
 * splitting is performed.  Call this after the region loop.
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
        if (rA == undefined || rB == undefined ||
                rA >= size(processedRegions) || rB >= size(processedRegions))
        {
            continue;
        }

        var regionA      = processedRegions[rA];
        var regionB      = processedRegions[rB];
        var rAContinuity = ix.startContinuity;
        var rBContinuity = ix.endContinuity;
        var rAOffset     = ix.joinStartOffset;
        var rBOffset     = ix.joinEndOffset;

        // Boundary plane — normal to ref wire at regionA's end frame.
        var boundaryPl = plane(regionA.endFrame.origin, regionA.endFrame.zAxis);

        if (definition.returnLoftSurface)
        {
            var rASurf = isQueryEmpty(context, regionA.loftBodyQuery) ? qNothing() : regionA.loftBodyQuery;
            var rBSurf = isQueryEmpty(context, regionB.loftBodyQuery) ? qNothing() : regionB.loftBodyQuery;
            if (isQueryEmpty(context, rASurf) || isQueryEmpty(context, rBSurf))
            {
                continue;
            }

            // Trim region A back from the boundary by rAOffset along the ref wire.
            // Plane position is interpolated between the region's end and start frames,
            // so it remains normal to the wire (not a Euclidean offset of boundaryPl).
            var capPlaneA = boundaryPl;
            if (rAOffset > 0 * millimeter)
            {
                var rALen       = regionA.length;
                var rAFrac      = (rALen > 0 * meter) ? min(rAOffset / rALen, 1) : 0;
                var trimOriginA = regionA.endFrame.origin +
                        rAFrac * (regionA.startFrame.origin - regionA.endFrame.origin);
                var trimNormA   = normalize((1 - rAFrac) * regionA.endFrame.zAxis +
                        rAFrac * regionA.startFrame.zAxis);
                var trimPlA     = plane(trimOriginA, trimNormA);
                var probeA      = (regionA.startFrame.origin + trimOriginA) / 2;
                rASurf          = splitAndKeepLocal(context, id + ("trimA" ~ i),
                        rASurf, trimPlA, probeA);
                capPlaneA       = trimPlA;
            }

            // Trim region B forward from the boundary by rBOffset along the ref wire.
            var capPlaneB = boundaryPl;
            if (rBOffset > 0 * millimeter)
            {
                var rBLen       = regionB.length;
                var rBFrac      = (rBLen > 0 * meter) ? min(rBOffset / rBLen, 1) : 0;
                var trimOriginB = regionB.startFrame.origin +
                        rBFrac * (regionB.endFrame.origin - regionB.startFrame.origin);
                var trimNormB   = normalize(rBFrac * regionB.endFrame.zAxis +
                        (1 - rBFrac) * regionB.startFrame.zAxis);
                var trimPlB     = plane(trimOriginB, trimNormB);
                var probeB      = (trimOriginB + regionB.endFrame.origin) / 2;
                rBSurf          = splitAndKeepLocal(context, id + ("trimB" ~ i),
                        rBSurf, trimPlB, probeB);
                capPlaneB       = trimPlB;
            }

            // Find cap edges at each region's (possibly trimmed) end plane.
            var rACapEdges = edgesNearPlane(context, rASurf, capPlaneA);
            var rBCapEdges = edgesNearPlane(context, rBSurf, capPlaneB);
            if (size(rACapEdges) == 0 || size(rBCapEdges) == 0)
            {
                continue;
            }

            // Bridge each A cap edge to its closest B cap edge.
            for (var b = 0; b < size(rACapEdges); b += 1)
            {
                var midA  = evEdgeTangentLine(context, {
                    "edge" : rACapEdges[b], "parameter" : 0.5
                }).origin;
                var bEdge = qClosestTo(qUnion(rBCapEdges), midA);
                var midB  = evEdgeTangentLine(context, {
                    "edge" : bEdge, "parameter" : 0.5
                }).origin;

                if (norm(midA - midB) < 0.01 * millimeter)
                {
                    continue;
                }

                var faceA = qIntersection([
                    qAdjacent(rACapEdges[b], AdjacencyType.EDGE, EntityType.FACE),
                    qOwnedByBody(rASurf, EntityType.FACE)
                ]);
                var faceB = qIntersection([
                    qAdjacent(bEdge, AdjacencyType.EDGE, EntityType.FACE),
                    qOwnedByBody(rBSurf, EntityType.FACE)
                ]);

                try
                {
                    surfaceLofter(context, id + ("loftIx" ~ i ~ "e" ~ b),
                            rACapEdges[b], faceA, bEdge, faceB, rAContinuity, rBContinuity);
                }
                catch {}
            }
        }

        if (definition.returnOffsetWires)
        {
            var wireA = isQueryEmpty(context, regionA.wireBodyQuery) ? qNothing() : regionA.wireBodyQuery;
            var wireB = isQueryEmpty(context, regionB.wireBodyQuery) ? qNothing() : regionB.wireBodyQuery;
            if (isQueryEmpty(context, wireA) || isQueryEmpty(context, wireB))
            {
                continue;
            }

            // Trim region A's wire back from the boundary by rAOffset (same
            // interpolated-frame logic as the surface section).
            var wireCapPlaneA = boundaryPl;
            if (rAOffset > 0 * millimeter)
            {
                var rALen       = regionA.length;
                var rAFrac      = (rALen > 0 * meter) ? min(rAOffset / rALen, 1) : 0;
                var trimOriginA = regionA.endFrame.origin +
                        rAFrac * (regionA.startFrame.origin - regionA.endFrame.origin);
                var trimNormA   = normalize((1 - rAFrac) * regionA.endFrame.zAxis +
                        rAFrac * regionA.startFrame.zAxis);
                var trimPlA     = plane(trimOriginA, trimNormA);
                var probeA      = (regionA.startFrame.origin + trimOriginA) / 2;
                wireA           = splitAndKeepLocal(context, id + ("trimWireA" ~ i),
                        wireA, trimPlA, probeA);
                wireCapPlaneA   = trimPlA;
            }

            // Trim region B's wire forward from the boundary by rBOffset.
            var wireCapPlaneB = boundaryPl;
            if (rBOffset > 0 * millimeter)
            {
                var rBLen       = regionB.length;
                var rBFrac      = (rBLen > 0 * meter) ? min(rBOffset / rBLen, 1) : 0;
                var trimOriginB = regionB.startFrame.origin +
                        rBFrac * (regionB.endFrame.origin - regionB.startFrame.origin);
                var trimNormB   = normalize(rBFrac * regionB.endFrame.zAxis +
                        (1 - rBFrac) * regionB.startFrame.zAxis);
                var trimPlB     = plane(trimOriginB, trimNormB);
                var probeB      = (trimOriginB + regionB.endFrame.origin) / 2;
                wireB           = splitAndKeepLocal(context, id + ("trimWireB" ~ i),
                        wireB, trimPlB, probeB);
                wireCapPlaneB   = trimPlB;
            }

            // Find wire endpoints near each region's (possibly trimmed) cap plane.
            var wireAVerts = evaluateQuery(context, qOwnedByBody(wireA, EntityType.VERTEX));
            var wireBVerts = evaluateQuery(context, qOwnedByBody(wireB, EntityType.VERTEX));

            wireAVerts = filter(wireAVerts, function(v)
            {
                return abs(dot(evVertexPoint(context, { "vertex" : v }) - wireCapPlaneA.origin,
                               wireCapPlaneA.normal)) < 1e-3 * meter;
            });
            wireBVerts = filter(wireBVerts, function(v)
            {
                return abs(dot(evVertexPoint(context, { "vertex" : v }) - wireCapPlaneB.origin,
                               wireCapPlaneB.normal)) < 1e-3 * meter;
            });

            for (var a = 0; a < size(wireAVerts); a += 1)
            {
                var ptA = evVertexPoint(context, { "vertex" : wireAVerts[a] });

                var bestBVert = wireAVerts[a];
                var bestDist  = undefined;
                for (var bb = 0; bb < size(wireBVerts); bb += 1)
                {
                    var ptB = evVertexPoint(context, { "vertex" : wireBVerts[bb] });
                    var d   = norm(ptA - ptB);
                    if (bestDist == undefined || d < bestDist)
                    {
                        bestDist  = d;
                        bestBVert = wireBVerts[bb];
                    }
                }

                if (bestDist != undefined && bestDist < 0.01 * millimeter)
                {
                    continue;
                }

                var wireAEdgeQ = qClosestTo(qOwnedByBody(wireA, EntityType.EDGE), ptA);
                var ptBVec     = evVertexPoint(context, { "vertex" : bestBVert });
                var wireBEdgeQ = qClosestTo(qOwnedByBody(wireB, EntityType.EDGE), ptBVec);

                // Bridge direction: from A vertex toward B vertex.
                var bridgeDir = normalize((ptBVec - ptA) / millimeter);

                // In FS >= V2744_BRIDGING_CURVE_FLIP, parameter-based auto-flip is
                // disabled and qUnion([vertex, edge]) also disables editing-logic flip.
                // Compute flip explicitly: flip if edge tangent at the vertex is
                // anti-parallel to bridgeDir (i.e. points away from the gap).
                var match1 = (rAContinuity == IntersectionContinuityType.G1) ?
                        BridgingCurveMatchType.TANGENCY : BridgingCurveMatchType.POSITION;
                var match2 = (rBContinuity == IntersectionContinuityType.G1) ?
                        BridgingCurveMatchType.TANGENCY : BridgingCurveMatchType.POSITION;

                var flip1 = false;
                var flip2 = false;
                if (match1 == BridgingCurveMatchType.TANGENCY)
                {
                    var paramA = evDistance(context, {
                            "side0" : wireAEdgeQ,
                            "side1" : ptA
                    }).sides[0].parameter;
                    var tangA = evEdgeTangentLine(context, {
                            "edge"      : wireAEdgeQ,
                            "parameter" : paramA
                    }).direction;
                    flip1 = dot(tangA, bridgeDir) < 0;
                }
                if (match2 == BridgingCurveMatchType.TANGENCY)
                {
                    var paramB = evDistance(context, {
                            "side0" : wireBEdgeQ,
                            "side1" : ptBVec
                    }).sides[0].parameter;
                    var tangB = evEdgeTangentLine(context, {
                            "edge"      : wireBEdgeQ,
                            "parameter" : paramB
                    }).direction;
                    flip2 = dot(tangB, bridgeDir) > 0;
                }

                bridgingCurve(context, id + ("bridgeIx" ~ i ~ "v" ~ a), {
                    "side1"             : qUnion([wireAVerts[a], wireAEdgeQ]),
                    "match1"            : match1,
                    "flip1"             : flip1,
                    "side2"             : qUnion([bestBVert, wireBEdgeQ]),
                    "match2"            : match2,
                    "flip2"             : flip2,
                    "method"            : BridgingCurveMethod.CONTROL_POINTS,
                    "editControlPoints" : false
                });
            }
        }
    }
}


function surfaceLofter(context is Context, id is Id,
        edgeA is Query, faceA is Query, edgeB is Query, faceB is Query,
        rAContinuity is IntersectionContinuityType, rBContinuity is IntersectionContinuityType)
{
    // Only apply MATCH_TANGENT when the adjacent face is confirmed non-empty.
    // If the face is missing, loft throws LOFT_NO_FACE_FOR_START/END_CLAMP even
    // when auto-detect is attempted — fall back to G0 for that end instead.
    var useG1A = (rAContinuity == IntersectionContinuityType.G1) && !isQueryEmpty(context, faceA);
    var useG1B = (rBContinuity == IntersectionContinuityType.G1) && !isQueryEmpty(context, faceB);

    var loftDef = {
        "bodyType"               : ExtendedToolBodyType.SURFACE,
        "surfaceOperationType"   : NewSurfaceOperationType.NEW,
        "wireProfilesArray"      : [
            { "wireProfileEntities" : qUnion([edgeA]) },
            { "wireProfileEntities" : qUnion([edgeB]) }
        ],
        "startCondition"         : useG1A ? LoftEndDerivativeType.MATCH_TANGENT : LoftEndDerivativeType.DEFAULT,
        "endCondition"           : useG1B ? LoftEndDerivativeType.MATCH_TANGENT : LoftEndDerivativeType.DEFAULT,
        "trimProfiles"           : false,
        "makePeriodic"           : false,
        "showIsocurves"          : false,
        "defaultSurfaceScope"    : true,
        "addSections"            : false,
        "addGuides"              : false
    };

    if (useG1A)
    {
        loftDef = mergeMaps(loftDef, { "adjacentFacesStart" : faceA, "startMagnitude" : 1.0 });
    }
    if (useG1B)
    {
        loftDef = mergeMaps(loftDef, { "adjacentFacesEnd" : faceB, "endMagnitude" : 1.0 });
    }

    // Try the requested continuity first; if that fails, retry as G0.
    var succeeded = false;
    try
    {
        loft(context, id, loftDef);
        succeeded = true;
    }
    catch {}

    if (!succeeded && (useG1A || useG1B))
    {
        var g0Def = mergeMaps(loftDef, {
            "startCondition" : LoftEndDerivativeType.DEFAULT,
            "endCondition"   : LoftEndDerivativeType.DEFAULT
        });
        loft(context, id + "g0", g0Def);
    }
}


