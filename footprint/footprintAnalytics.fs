FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

export enum searchDerivative
{
    FIRST,
    SECOND
}

export function analyzeFootprint(context is Context, id is Id, fptPath is Path, rslQuery is Query, showCurvature is boolean) returns map
{
    var retMap = {};
    
    var initialPoints = calcPointInfo(context, fptPath, 500, [0, 1]);
    var curvatureScaleFactor = .1;
    
    if (showCurvature)
    {
        for (var pt in initialPoints)
        {
            var startPT = pt.point;
            var endPT = startPT - pt.frame.xAxis*((pt.curvature.value)^(.75) * 1 * meter * curvatureScaleFactor);
            if (pt.curvature < 50/meter)
            {
                try
                {
                    //println('startPT - > ' ~ startPT);
                    //println('endPT -> ' ~ endPT);
                    addDebugLine(context, startPT, endPT, DebugColor.MAGENTA);   
                }   
            }
    
        }   
    }
    
    
    var rslLine = evLine(context, {
                "edge" : rslQuery
        });
        
    var cpLine1 = evEdgeTangentLine(context, {
            "edge" : rslQuery,
            "parameter" : 0
    });
    
    var cpLine2 = evEdgeTangentLine(context, {
            "edge" : rslQuery,
            "parameter" : 1
    });
    
    var pathLength = evPathLength(context, fptPath);
    
    var mrsX = (cpLine1.origin[0] + cpLine2.origin[0])/2;
    var mrsDist = evDistancePath(context, {'side0' : fptPath, 'side1' : vector(mrsX, 0 * millimeter, 0 * millimeter)});
    var mrsPathParam = mrsDist.pathParameter;
    
    //initialPoints = sort(initialPoints, function(a, b) {return a.point[0] - b.point[0];}); // order points in ascending order - more for convention than anything else. 
    initialPoints = filter(initialPoints, function(x) {return x.point[1] > 0 * millimeter;});
    
    var fbPoints = filter(initialPoints, function(x) {return x.point[0] <= mrsX;});
    var abPoints = filter(initialPoints, function(x) {return x.point[0] >= mrsX;});
    
    //fbWidest
    var fbWidestSort = sort(fbPoints, function(a, b) {return b.point[1] - a.point[1];});
    var fbWidestStartParam = fbWidestSort[0].pathParam;
    var fbWidest = newtonRhapsonPath(context, searchDerivative.FIRST, fptPath, [fbWidestStartParam - 0.0025, fbWidestStartParam + 0.0025], 0.001);
    var fbWidestPathParam = fbWidest.pathParam;
    
    addDebugLine(context, fbWidest.point, vector(fbWidest.point[0], -1* fbWidest.point[1], fbWidest.point[2]), DebugColor.CYAN);
    
    
    //fbInflection
    var fbInflectionStarts = findInflectionSearchPoints(context, fbPoints);
    var fbInflectionStart = sort(fbInflectionStarts, function(a, b) {return b.midPoint[0] - a.midPoint[0];})[0];
    
    var fbInflection = newtonRhapsonPath(context, searchDerivative.SECOND, fptPath, fbInflectionStart.pathParamArray, 0.001);
    
    addDebugLine(context, fbInflection.point, vector(fbInflection.point[0], -1* fbInflection.point[1], fbInflection.point[2]), DebugColor.BLUE);
    
    //abWidest
    var abWidestSort = sort(abPoints, function(a, b) {return b.point[1] - a.point[1];});
    var abWidestStartParam = abWidestSort[0].pathParam;
    var abWidest = newtonRhapsonPath(context, searchDerivative.FIRST, fptPath, [abWidestStartParam - 0.0025, abWidestStartParam + 0.0025], 0.001);
    var abWidestPathParam = abWidest.pathParam;
    
    addDebugLine(context, abWidest.point, vector(abWidest.point[0], -1* abWidest.point[1], abWidest.point[2]), DebugColor.CYAN);
    
    //abInflection
    var abInflectionStarts = findInflectionSearchPoints(context, abPoints);
    var abInflectionStart = sort(abInflectionStarts, function(a, b) {return a.midPoint[0] - b.midPoint[0];})[0];
    
    for (var pt in abInflectionStart.points)
    {
        addDebugPoint(context, pt.point, DebugColor.RED);
    }
    
    var abInflection = newtonRhapsonPath(context, searchDerivative.SECOND, fptPath, abInflectionStart.pathParamArray, 0.001);
    
    addDebugLine(context, abInflection.point, vector(abInflection.point[0], -1* abInflection.point[1], abInflection.point[2]), DebugColor.BLUE);
    
    //waist
    var insideWidest = filter(initialPoints, function(x) {return ((x.point[0] > fbInflection.point[0]) && (x.point[0] < abInflection.point[0]));});
    var waistSearchArray = sort(insideWidest, function(a, b) {return a.point[1] - b.point[1];});
    //println(waistSearchArray);
    var waistStartPoint = waistSearchArray[0].pathParam;
    //println('waistStartPoint = ' ~ waistSearchArray[0].point);
    var waistPoint = newtonRhapsonPath(context, searchDerivative.FIRST, fptPath, [waistStartPoint - 0.005, waistStartPoint + 0.005], 0.001);
    
    addDebugLine(context, waistPoint.point, vector(waistPoint.point[0], -1* waistPoint.point[1], waistPoint.point[2]), DebugColor.BLACK);
    
    //radiusPoints
    var radiusPoints = calcPointInfo(context, fptPath, 200, [min(fbInflection.pathParam, abInflection.pathParam), max(fbInflection.pathParam, abInflection.pathParam)]);
    
    //println('fbInflection.point -> ' ~ toString(fbInflection.point));
    //println('abInflection.point -> ' ~ toString(abInflection.point));
    
    radiusPoints = filter(radiusPoints, function(x) {return (x.point[0] > fbInflection.point[0] && x.point[0] < abInflection.point[0]);});
    radiusPoints = filter(radiusPoints, function(x) {return (x.curvature > 0 && isReal(x.curvature.value, POSITIVE_REAL_BOUNDS));});
    
    for (var pt in radiusPoints)
    {
        //println('RADIUS POINT X: RADIUS ' ~ toString(pt.point[0]) ~ ': ' ~ toString(pt.radius));
        addDebugPoint(context, pt.point, DebugColor.MAGENTA);
    }
    
    var radiusArray = mapArray(radiusPoints, function(x) {return x.radius;});
    var avgRadius = average(radiusArray);
    
    //dimension
    var dimensonStr = roundToPrecision(fbWidest.point[1].value*2000, 2) ~ ' - ' ~ roundToPrecision(waistPoint.point[1].value*2000, 2) ~ ' - ' ~ roundToPrecision(abWidest.point[1].value*2000, 2);
    
    //println('dimensionStr = ' ~ dimensonStr);
    //println('waistPoint = ' ~ waistPoint.point);
    
    
    //taperWide
    var taperWideAngle = atan((fbWidest.point[1] - abWidest.point[1])/(fbWidest.point[0] - abWidest.point[0]));
    
    //taperInflection
    var taperInflectionAngle = atan((fbInflection.point[1] - abInflection.point[1])/(fbInflection.point[0] - abInflection.point[0]));
    
    //naturalWide
    var naturalWide = arcThroughPoints(context, fbWidest.point, waistPoint.point, abWidest.point);
    
    //naturalInflection
    var naturalInflection = arcThroughPoints(context, fbInflection.point, waistPoint.point, abInflection.point);
    
    var fcpLine = cpLine1;
    var acpLine = cpLine2;
    
    if (fcpLine.origin[0] > acpLine.origin[0])
    {
        fcpLine = cpLine2;
        acpLine = cpLine1;
    }
    
    var fcpPlane = opPlane(context, id + "planeFCP1", {
            "plane" : plane(fcpLine.origin, vector(1, 0, 0))
    });
    
    var acpPlane = opPlane(context, id + "planeACP2", {
            "plane" : plane(acpLine.origin, vector(1, 0, 0))
    });
    
    var fcpDist = evDistancePath(context, {
            "side0" : qCreatedBy(id + "planeFCP1"),
            "side1" : fptPath
    });
    
    var acpDist = evDistancePath(context, {
            "side0" : qCreatedBy(id + "planeACP2"),
            "side1" : fptPath
    });
    
    var fcpFptLine = evPathTangentLines(context, fptPath, [fcpDist.pathParameter]).tangentLines[0];
    var acpFptLine = evPathTangentLines(context, fptPath, [acpDist.pathParameter]).tangentLines[0];
    
    var fcpAngle = atan(fcpFptLine.direction[1]/fcpFptLine.direction[0]);
    var acpAngle = atan(acpFptLine.direction[1]/acpFptLine.direction[0]);
    
    opDeleteBodies(context, id + "deleteBodies1ContactPlanes", {
            "entities" : qUnion([qCreatedBy(id + "planeFCP1"), qCreatedBy(id + "planeACP2")])
    });
    
    retMap['avgRadiusSTR'] = roundToPrecision(avgRadius.value, 2) ~ ' m';
    retMap['dimSTR'] = dimensonStr;
    retMap['natRadWideSTR'] = roundToPrecision(naturalWide.R.value, 2) ~ ' m';
    retMap['natRadInflectSTR'] = roundToPrecision(naturalInflection.R.value, 2) ~ ' m';
    retMap['taWIDEST'] = roundToPrecision(taperWideAngle.value*180/PI, 2) * degree;
    retMap['taINFLECTION'] = roundToPrecision(taperInflectionAngle.value*180/PI, 2) * degree;
    
    retMap['fcpAngle'] = roundToPrecision(fcpAngle.value*180/PI, 2)*degree;
    retMap['acpAngle'] = roundToPrecision(acpAngle.value*180/PI, 2)*degree;
    retMap['widestFBfromFCP'] = roundToPrecision(fbWidest.point[0].value*1000 - fcpLine.origin[0].value*1000, 3)*millimeter;
    retMap['inflectionFBfromFCP'] = roundToPrecision(fbInflection.point[0].value*1000 - fcpLine.origin[0].value*1000, 3)*millimeter;
    retMap['fbInflectionToWidest'] = roundToPrecision(fbInflection.point[0].value*1000 - fbWidest.point[0].value*1000, 3)*millimeter;
    retMap['widestABfromACP'] = roundToPrecision(abWidest.point[0].value*1000 - acpLine.origin[0].value*1000, 3)*millimeter;
    retMap['inflectionABfromACP'] = roundToPrecision(abInflection.point[0].value*1000 - acpLine.origin[0].value*1000, 3)*millimeter;
    retMap['abInflectionToWidest'] = roundToPrecision(abInflection.point[0].value*1000 - abWidest.point[0].value*1000, 3)*millimeter;
    
    retMap['fbWidest'] = fbWidest.point;
    retMap['abWidest'] = abWidest.point;
    retMap['fbInflection'] = fbInflection.point;
    retMap['abInflection'] = abInflection.point;
    
    return retMap;
}

export function findInflectionSearchPoints(context is Context, searchPoints is array) returns array
{
    var retArray = [];
    
    var points = sort(searchPoints, function(a, b) {return a.point[0] - b.point[0];});
    
    for (var i = 0; i < size(points) - 1; i += 1)
    {
        var thisXAxis = points[i].frame.xAxis;
        var nextXAxis = points[i+1].frame.xAxis;
        
        if (((thisXAxis[1] > 0) &&(nextXAxis[1] < 0)) || ((thisXAxis[1] < 0) &&(nextXAxis[1] > 0)))
        {
            var retMap = {'pathParamArray' : [points[i].pathParam, points[i+1].pathParam], 'points' : [points[i], points[i+1]], 'midPoint' : (points[i].point + points[i+1].point)/2};
            retArray = append(retArray, retMap);
            //addDebugPoint(context, points[i].point, DebugColor.RED);
            //addDebugPoint(context, points[i+1].point, DebugColor.RED);
        }
    }
    
    return retArray;
}

export function arcThroughPoints(context is Context, p1 is Vector, p2 is Vector, p3 is Vector) returns map
{
    var A = p1[0] * (p2[1] - p3[1]) - p1[1] * (p2[0] - p3[0]) + p2[0] * p3[1] - p3[0] * p2[1];
    var B = (p1[0]^2 + p1[1]^2) * (p3[1] - p2[1]) + (p2[0]^2 + p2[1]^2) * (p1[1] - p3[1]) + (p3[0]^2 + p3[1]^2) * (p2[1] - p1[1]);
    var C = (p1[0]^2 + p1[1]^2) * (p2[0] - p3[0]) + (p2[0]^2 + p2[1]^2) * (p3[0] - p1[0]) + (p3[0]^2 + p3[1]^2) * (p1[0] - p2[0]);
    var D = (p1[0]^2 + p1[1]^2) * (p3[0] * p2[1] - p2[0] * p3[1]) + (p2[0]^2 + p2[1]^2) * (p1[0] * p3[1] - p3[0] * p1[1]) + (p3[0]^2 + p3[1]^2) * (p2[0] * p1[1] - p1[0] * p2[1]);
    //println(B);
    //println(A);
    var xC = -B/(2 * A);
    var yC = -C/(2 * A);
    var R = sqrt((B^2 + C^2 - 4 * A *D) / (4 * A^2) );
    
    return {'xC' : xC, 'yC' : yC, 'R' : R};
}

export function newtonRhapsonPath(context is Context, derivative is searchDerivative, searchPath is Path, paramBounds is array, tol is number) returns map // return point map of the same format is calcPointInfo
{
    //var compareTol = tol;
    // seed search values
    var maxParam = max(paramBounds);
    var minParam = min(paramBounds);
    var seedSearch = calcPointInfo(context, searchPath, 200, paramBounds);
    var orderedSeeds =[];
    if (derivative == searchDerivative.FIRST)
    {
        orderedSeeds = sort(seedSearch, function(a, b) {return abs((a.dir[1])/(a.dir[0])) - abs((b.dir[1])/(b.dir[0])) ;});
    }
    else if (derivative == searchDerivative.SECOND)
    {
        orderedSeeds = sort(seedSearch, function(a, b) {return abs(a.curvature) - abs(b.curvature) ;});
    }
    
    var firstGuess = orderedSeeds[0];
    var secondGuess = orderedSeeds[1];
    
    
    //initialize iteration varaibles
    var delta = 0;
    var m = 0;
    var b = 0;
    
    if (derivative == searchDerivative.FIRST)
    {
        delta = firstGuess.dir[1]/firstGuess.dir[0];
        m = (delta - (secondGuess.dir[1]/secondGuess.dir[0]))/(firstGuess.pathParam - secondGuess.pathParam);
        b = delta - m*firstGuess.pathParam;
    }
    else if (derivative == searchDerivative.SECOND)
    {
        delta = firstGuess.curvature.value;
        if (firstGuess.frame.xAxis[1] < 0)
        {
            delta = -1* delta; // if the x axis of the frame has a negative Y component, the curvature is NEGATIVE. the curvature provided by evCurvature is always positive.    
        }
        var secondCurvature = secondGuess.curvature.value;
        if (secondGuess.frame.xAxis[1] < 0)
        {
            secondCurvature = secondCurvature * -1;
        }
        m = (delta - secondCurvature)/(firstGuess.pathParam - secondGuess.pathParam);
        b = delta - m*firstGuess.pathParam;
    }
    
    var closestPoint = firstGuess;
    
    for (var i = 0; i < 15; i += 1) // set 15 iterations MAXIMUM
    {
        /*
        println('iterating NR. Iteration # ' ~ i);
        println('m = ' ~ m);
        println('b = ' ~ b);
        println('delta =' ~ delta);
        */
        if (abs(delta) > tol && (m != 0 && m != inf && b != inf) )
        {
            var nextParam = -b/m;
            
            if (nextParam > maxParam)
            {
                nextParam = maxParam;   
            }
            if (nextParam < minParam)
            {
                nextParam = minParam;
            }
            
            if (nextParam >= 0 && nextParam <= 1)
            {
                var tempMap = {'pathParam' : nextParam};
        
                var pathLines = evPathTangentLines(context, searchPath, [nextParam]);
                var pathPoint = pathLines.tangentLines[0].origin;
                var pointEdge = searchPath.edges[pathLines.edgeIndices[0]];
                tempMap['point'] = pathPoint;
                tempMap['dir'] = pathLines.tangentLines[0].direction;
                tempMap['edgeIndex'] = pathLines.edgeIndices[0];
                tempMap['edgeQuery'] = searchPath.edges[pathLines.edgeIndices[0]];
                
                var pointDist = evDistance(context, {
                        "side0" : pathPoint,
                        "side1" : qUnion(searchPath.edges)
                });
                
                var edgeParameter = pointDist.sides[1].parameter;
                tempMap['edgeParameter'] = edgeParameter;
                
                var edgeCurvature = evEdgeCurvature(context, {
                        "edge" : pointEdge,
                        "parameter" : edgeParameter
                });
                
                tempMap['frame'] = edgeCurvature.frame;
                tempMap['curvature'] = edgeCurvature.curvature;
                
                var pointSlope = tempMap.dir[1]/tempMap.dir[0];
                
                var pointRadius = ((1 + pointSlope^2)^(3/2))/tempMap.curvature;
                tempMap['radius'] = pointRadius;
                
                // update delta, m, b
                
                if (derivative == searchDerivative.FIRST)
                {
                    var newDelta = tempMap.dir[1]/tempMap.dir[0];
                    m = (newDelta - delta)/(nextParam - closestPoint.pathParam);
                    b = newDelta - m * nextParam;
                    delta = newDelta;
                }

                else if (derivative == searchDerivative.SECOND)
                {
                    var newDelta = tempMap.curvature.value;
                    if (tempMap.frame.xAxis[1] < 0)
                    {
                        newDelta = -1* newDelta;
                    }
                    m = (newDelta - delta)/(nextParam - closestPoint.pathParam);
                    b = newDelta - m * nextParam;
                    delta = newDelta;
                }

                closestPoint = tempMap;   
            }
            
            
            
        }   
    }
    
    return closestPoint;
}


export function calcPointInfo(context is Context, fptPath is Path, numPoints is number, paramRange is array) returns array
{
    var pathParams = range(paramRange[0], paramRange[1], numPoints);
    
    // the approach here is far from the most computationally efficent, but what it lacks in elegance, it compensates for in simplicity. 
    // this is certainly an area to investigat if future performance improvements are needed, though I suspect it's going to run quickly enough
    // that performance improvements will not be warranted. 
    
    var retArray = [];
    
    for (var i = 0; i < size(pathParams); i += 1)
    {
        var pathParam = pathParams[i];
        var tempMap = {'pathParam' : pathParam};
        
        var pathLines = evPathTangentLines(context, fptPath, [pathParam]);
        var pathPoint = pathLines.tangentLines[0].origin;
        var pointEdge = fptPath.edges[pathLines.edgeIndices[0]];
        tempMap['point'] = pathPoint;
        tempMap['dir'] = pathLines.tangentLines[0].direction;
        tempMap['edgeIndex'] = pathLines.edgeIndices[0];
        tempMap['edgeQuery'] = fptPath.edges[pathLines.edgeIndices[0]];
        
        var pointDist = evDistance(context, {
                "side0" : pathPoint,
                "side1" : qUnion(fptPath.edges)
        });
        
        var edgeParameter = pointDist.sides[1].parameter;
        tempMap['edgeParameter'] = edgeParameter;
        
        var edgeCurvature = evEdgeCurvature(context, {
                "edge" : pointEdge,
                "parameter" : edgeParameter
        });
        
        tempMap['frame'] = edgeCurvature.frame;
        tempMap['curvature'] = edgeCurvature.curvature;
        
        var pointSlope = tempMap.dir[1]/tempMap.dir[0];
        
        var pointRadius = ((1 + pointSlope^2)^(3/2))/tempMap.curvature;
        tempMap['radius'] = pointRadius;
        
        retArray = append(retArray, tempMap);
    }
    
    return retArray;
}

/** Extending distanceResult for paths to include a path parameter
 * 
 **/
 
export type PathDistanceResult typecheck canBePathDistanceResult;

predicate canBePathDistanceResult(value)
{
    value is map;
    isLength(value.distance);
    value.sides is array;
    size(value.sides) == 2;
    value.pathParameter is number;
    for (var sideResult in value.sides)
    {
        sideResult is map;
        isNonNegativeInteger(sideResult.index); // Index into either input array or results of input query evaluation
        is3dLengthVector(sideResult.point);
        // The parameter is either one number (for a curve) or an array of two (for a surface) or a 2D length vector (for a plane). For bodies or points, the parameter is 0.
        // For lines, the parameter is a length representing the distance along the direction.
        if (!(sideResult.parameter is number || isLength(sideResult.parameter) || is2dPoint(sideResult.parameter)))
        {
            sideResult.parameter is Vector || sideResult.parameter is MeshFaceParameter;
            size(sideResult.parameter) == 2;
            sideResult.parameter[0] is number;
            sideResult.parameter[1] is number;
        }
        
    }
}

export function evDistancePath(context is Context, definition is map) returns PathDistanceResult
{
    
    if (definition.side0 is Path)
    {
        var result = calcEvDistancePath(context, {'path' : definition.side0, 'otherSide' : definition.side1});
        return result;  
    }
    
    else if (definition.side1 is Path)
    {
        var result = calcEvDistancePath(context, {'path' : definition.side1, 'otherSide' : definition.side0});
        var resultSides = result.sides;
        result.sides = [resultSides[1], resultSides[0]];
        return result; 
    }
}

export function calcEvDistancePath(context is Context, measureMap is map) returns PathDistanceResult
{
    var refPath = measureMap.path;
    var otherSide = measureMap.otherSide;
    var pathEdgeQuery = qUnion(refPath.edges);
    var pathLength = evPathLength(context, refPath);
    var stdDistance = evDistance(context, {
            "side0" : qUnion(refPath.edges),
            "side1" : otherSide
    });
    
    var edgeIndex = stdDistance.sides[0].index;
    var edgeParameter = stdDistance.sides[0].parameter;
    var closestEdge = qNthElement(qUnion(refPath.edges), edgeIndex);
    var edgeLength = evLength(context, {
            "entities" : closestEdge
    });
    
    var pathLenUpToEdge = 0 * millimeter;
    if (edgeIndex > 0)
    {
        for (var i = 0; i < edgeIndex; i += 1)
        {
         pathLenUpToEdge += evLength(context, {
                 "entities" : qNthElement(pathEdgeQuery, i)
         });   
        }
    }
    
    var paramUpToEdge = pathLenUpToEdge/pathLength;
    var paramAtEnd = (edgeLength + pathLenUpToEdge)/pathLength;
    var pathParamDelta = paramAtEnd - paramUpToEdge;
    var pathParm = paramUpToEdge + pathParamDelta*edgeParameter;
    if (refPath.flipped[edgeIndex])
    {
        pathParm = paramUpToEdge + pathParamDelta*(1-edgeParameter);
    }
    
    const pathDistResult = {
        'distance' : stdDistance.distance,
        'sides' : stdDistance.sides,
        'pathParameter' : pathParm
        } as PathDistanceResult;
        
    return pathDistResult;  
}