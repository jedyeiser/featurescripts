FeatureScript 2522;
import(path : "onshape/std/common.fs", version : "2522.0");
import(path : "onshape/std/fillSurface.fs", version : "2522.0");
import(path : "onshape/std/isoparametricCurve.fs", version : "2522.0");
import(path : "onshape/std/math.fs", version : "2522.0");


export const edgeSpacingBounds = { (millimeter) : [0.5, 2, 20] } as LengthBoundSpec;

annotation { "Feature Type Name" : "Deform Geometry", "Feature Type Description" : "" }
export const deformGeometry = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bodies to deform", "Filter" : BodyType.SOLID || BodyType.SHEET}
        definition.deformBodies is Query;

        annotation { "Name" : "From curve", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.fromCurve is Query;

        annotation { "Name" : "To curve", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.toCurve is Query;

        annotation { "Name" : "FlipDirection", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
        definition.flipTo is boolean;

        annotation { "Name" : "Edge point spacing" }
        isLength(definition.edgeSpacing, edgeSpacingBounds);

        annotation { "Group Name" : "Surface Definitions", "Collapsed By Default" : true }
        {
            annotation { "Name" : "UV Curve Spacing" }
            isLength(definition.uvSpacing, edgeSpacingBounds);

        }
        
        annotation { "Name" : "Flip Y", "Default" : false }
        definition.flipY is boolean;
        
        annotation { "Name" : "Keep wires?", "Default" : false }
        definition.keepWires is boolean;
        

    }

    {
        var allBodies = evaluateQuery(context, definition.deformBodies);

        var bodyMap = {};


        for (var b = 0; b < size(allBodies); b += 1)
        {
            var allBodyFaces = evaluateQuery(context, qOwnedByBody(allBodies[b], EntityType.FACE));
            var transformedBodyFaces = [];

            for (var f = 0; f < size(allBodyFaces); f += 1)
            {
                var face = allBodyFaces[f];

                var uIsoCurves = isoparametricCurve(context, id + ('isoParametricU' ~ f), {
                        'face' : face,
                        'directionType' : DirectionType.U_DIRECTION,
                        'equalSpacing' : true,
                        'nCurves' : 7
                    });

                var vIsoCurves = isoparametricCurve(context, id + ('isoParametricV' ~ f), {
                        'face' : face,
                        'directionType' : DirectionType.V_DIRECTION,
                        'equalSpacing' : true,
                        'nCurves' : 7
                    });

                // what are these isoCurves?

                var uCurveArray = evaluateQuery(context, qCreatedBy(id + ('isoParametricU' ~ f), EntityType.BODY));
                var vCurveArray = evaluateQuery(context, qCreatedBy(id + ('isoParametricV' ~ f), EntityType.BODY));

                //println(uCurveArray);

                uCurveArray = filter(uCurveArray, function(x)
                    {
                        return !isQueryEmpty(context, x);
                    });
                vCurveArray = filter(vCurveArray, function(x)
                    {
                        return !isQueryEmpty(context, x);
                    });

                var transformedUCurveArray = [];
                var transformedVCurveArray = [];

                var faceBox = evBox3d(context, {
                        "topology" : face,
                        "tight" : true
                    });


                for (var i = 0; i < size(uCurveArray); i += 1)
                {

                    var edgeLen = evLength(context, {
                            "entities" : uCurveArray[i]
                        });

                    if (edgeLen <= box3dDiagonalLength(faceBox) * 1.5)
                    {
                        var numPoints = floor(edgeLen / definition.edgeSpacing, 1);
                        if (numPoints < 10)
                        {
                            numPoints = 10;
                        }

                        if (numPoints > 50)
                        {
                            numPoints = 50;
                        }

                        transformedUCurveArray = append(transformedUCurveArray, deformCurve(context, id + ('deformUCurve' ~ f ~ i), qOwnedByBody(uCurveArray[i], EntityType.EDGE), numPoints, definition));
                    }
                }

                for (var i = 0; i < size(vCurveArray); i += 1)
                {
                    var edgeLen = evLength(context, {
                            "entities" : vCurveArray[i]
                        });

                    if (edgeLen <= box3dDiagonalLength(faceBox) * 1.5)
                    {
                        var numPoints = floor(edgeLen / definition.edgeSpacing, 1);
                        if (numPoints < 10)
                        {
                            numPoints = 10;
                        }
                        if (numPoints > 50)
                        {
                            numPoints = 50;
                        }

                        transformedVCurveArray = append(transformedVCurveArray, deformCurve(context, id + ('deformVCurve' ~ f ~ i), qOwnedByBody(vCurveArray[i], EntityType.EDGE), numPoints, definition));
                    }
                }


                opDeleteBodies(context, id + ("deleteBodies1" ~ f), {
                            "entities" : qUnion([qUnion(uCurveArray), qUnion(vCurveArray)])
                        });

                // get seed edges
                var seedFaceEdges = evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE));
                var newFaceWires = [];
                var newFaceEdges = [];
                for (var i = 0; i < size(seedFaceEdges); i += 1)
                {
                    var edgeLen = evLength(context, {
                            "entities" : seedFaceEdges[i]
                        });

                    var numPoints = floor(edgeLen / definition.edgeSpacing, 1);
                    if (numPoints < 10)
                    {
                        numPoints = 10;
                    }
                    if (numPoints > 50)
                    {
                        numPoints = 50;
                    }
                    var deformedCurve = deformCurve(context, id + ("getDeformedBoundary" ~ f ~ i), seedFaceEdges[i], numPoints, definition);
                    newFaceWires = append(newFaceWires, deformedCurve);
                    newFaceEdges = append(newFaceEdges, qOwnedByBody(deformedCurve, EntityType.EDGE));
                }

                var transformedSurf = createTransformedSurface(context, id + ("createTransformedSurf" ~ f), transformedUCurveArray, transformedVCurveArray, newFaceEdges);

                opDeleteBodies(context, id + ("curveCleanup" ~ f), {
                            "entities" : qUnion([qUnion(newFaceWires), qUnion(transformedUCurveArray), qUnion(transformedVCurveArray)])
                        });

                if (!isQueryEmpty(context, transformedSurf))
                {
                    transformedBodyFaces = append(transformedBodyFaces, transformedSurf);
                }

            }

            // sew faces together
            joinFaces(context, id + 'JOINFACES', transformedBodyFaces);
        }

    });

export function joinFaces(context is Context, id is Id, faceArray is array) returns Query
{
    var refSurf = faceArray[0];
    var remainingSurfs = qSubtraction(qUnion(faceArray), refSurf);
    try
    {
        opBoolean(context, id + "boolean1", {
                    "tools" : qUnion([refSurf, remainingSurfs]),
                    "operationType" : BooleanOperationType.UNION
                });
    }

    return refSurf;
}

export function createTransformedSurface(context is Context, id is Id, uCurves is array, vCurves is array, boundaryEdges is array) returns Query
{
    var retQ = qNothing();

    var intersectionPointMapArray = getUVIntersectionPoints(context, id + ('getIntersectionPoints'), uCurves, vCurves);
    var intersectionPointQueries = mapArray(intersectionPointMapArray, function(x)
    {
        return x.query;
    });

    //addDebugEntities(context, qUnion(uCurves), DebugColor.CYAN);
    //addDebugEntities(context, qUnion(vCurves), DebugColor.MAGENTA);

    try
    {
        //fill(context, id + 'fillSurf', fillDef);


        opFillSurface(context, id + ("fillTransformedSurface"), {
                    "edgesG0" : qUnion(boundaryEdges),
                    "edgesG1" : qUnion([]),
                    "edgesG2" : qUnion([]),
                    "guideVertices" : qUnion(intersectionPointQueries),
                });

        retQ = qCreatedBy(id + ("fillTransformedSurface"), EntityType.BODY);

    }
    catch (error)
    {
        addDebugEntities(context, qUnion(boundaryEdges), DebugColor.BLUE);
    }

    if (size(intersectionPointQueries) > 0)
    {
        opDeleteBodies(context, id + ("deleteIntersectionPoints"), {
                    "entities" : qUnion(intersectionPointQueries)
                });
    }


    return retQ;
}

export function getUVIntersectionPoints(context is Context, id is Id, uCurves is array, vCurves is array) returns array
{
    var retArray = [];

    for (var i = 0; i < size(uCurves); i += 1)
    {
        var uEdge = qOwnedByBody(uCurves[i], EntityType.EDGE);
        for (var j = 0; j < size(vCurves); j += 1)
        {
            var vEdge = qOwnedByBody(vCurves[j], EntityType.EDGE);

            var edgeDist = evDistance(context, {
                    "side0" : uEdge,
                    "side1" : vEdge
                });


            if (edgeDist.distance <= 0.1 * millimeter)
            {
                var uEdgeParam = edgeDist.sides[0].parameter;
                var vEdgeParam = edgeDist.sides[1].parameter;

                if (uEdgeParam > .05 && uEdgeParam < .95 && vEdgeParam > 0.05 && vEdgeParam < .95)
                {
                    opPoint(context, id + ("point" ~ i ~ j), {
                                "point" : edgeDist.sides[0].point
                            });

                    retArray = append(retArray, { 'point' : edgeDist.sides[0].point, 'query' : qCreatedBy(id + ("point" ~ i ~ j), EntityType.VERTEX) });
                    addDebugPoint(context, edgeDist.sides[0].point, DebugColor.GREEN);
                }
            }

        }
    }

    return retArray;
}

export function deformCurve(context is Context, id is Id, edge is Query, numPoints is number, definition is map) returns Query
{
    // get # control points required. Multiply by 1.5
    var edgeLength = evLength(context, {
            "entities" : edge
        });

    var edgeParameters = range(0, 1, numPoints);
    //println('parameters -> ' ~ edgeParameters);

    var edgePointLines = evEdgeTangentLines(context, {
            "edge" : edge,
            "parameters" : edgeParameters
        });

    var edgeSeedPoints = mapArray(edgePointLines, function(x)
    {
        return x.origin;
    });

    var pointArray = [];

    for (var p = 0; p < numPoints; p += 1)
    {

        pointArray = append(pointArray, pointDeformation(context, edgeSeedPoints[p], definition.fromCurve, definition.toCurve, definition.flipTo));
    }

    var edgePoints = mapArray(pointArray, function(x)
    {
        return x.toPoint;
    });

    var edgeBSpline = approximateSpline(context, {
                "degree" : 3,
                "tolerance" : 1e-5 * meter,
                "isPeriodic" : false,
                //"maxControlPoints" : 10,
                "targets" : [approximationTarget({ 'positions' : edgePoints })]
            })[0];

    opCreateBSplineCurve(context, id + ("bSplineCurve" ~ 'DEFORMED'), {
                "bSplineCurve" : edgeBSpline
            });

    return qCreatedBy(id + ("bSplineCurve" ~ 'DEFORMED'), EntityType.BODY);
}


export function pointDeformation(context is Context, worldPoint is Vector, fromCurve is Query, toCurve is Query, flipTo is boolean) returns map
{
    var retMap = {}; //{'worldPoint', 'fromCS', 'toCS', 'toPoint', 'fromParam', 'toParam'}
    var point = worldPoint;
    var pointFromDist = evDistance(context, {
            "side0" : fromCurve,
            "side1" : point
        });
    var fromParam = pointFromDist.sides[0].parameter;


    var fromRefPoint = pointFromDist.sides[0].point;

    var relativeVector = point - fromRefPoint;

    //create coordinate systems.

    var refFrame = evEdgeCurvature(context, {
                "edge" : fromCurve,
                "parameter" : fromParam
            }).frame;

    var toParm = fromParam;
    if (flipTo)
    {
        toParm = 1 - fromParam;
    }

    var toFrame = evEdgeCurvature(context, {
                "edge" : toCurve,
                "parameter" : toParm
            }).frame;

    var fromCS = coordSystem(refFrame.origin, -1 * refFrame.xAxis, -1 * refFrame.zAxis);
    var toCS = coordSystem(toFrame.origin, toFrame.xAxis, toFrame.zAxis);

    var refVector = fromWorld(fromCS, point);

    // now translate refVector in toCS, convert to world, store point.

    // TESTING

    var sameDirZ = (dot(refFrame.zAxis, toFrame.zAxis) > 0);
    var sameDirX = (dot(refFrame.xAxis, toFrame.xAxis) > 0);
    var sameDirY = (dot(yAxis(refFrame), yAxis(refFrame)) > 0);

    if (sameDirX)
    {
        //addDebugPoint(context, point, DebugColor.RED);
        refVector = vector(-1 * refVector[0], -1 * refVector[1], -1 * refVector[2]);
    }
    else if (!sameDirX)
    {
        refVector = vector(1 * refVector[0], 1 * refVector[1], -1 * refVector[2]);
        //addDebugPoint(context, point, DebugColor.GREEN);
    }
    if (!sameDirZ)
    {
        refVector = vector(1 * refVector[0], 1 * refVector[1], -1 * refVector[2]);
    }

    // END TESTING

    var toVector = toWorld(toCS, refVector);


    //addDebugPoint(context, toVector, DebugColor.RED);

    retMap = { 'worldPoint' : point, 'refVector' : refVector, 'toPoint' : toVector, 'fromCS' : fromCS, 'toCS' : toCS, 'fromParam' : fromParam, 'toParam' : toParm };

    return retMap;

}






