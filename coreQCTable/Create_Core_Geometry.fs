FeatureScript 2296;
import(path : "onshape/std/common.fs", version : "2296.0");
import(path : "onshape/std/table.fs", version : "2296.0");

import(path : "onshape/std/geomOperations.fs", version : "2296.0");

export enum POINT_TYPES
{
    annotation { "Name" : "Static Distance Between Points" }
    STATIC_DISTANCE,
    annotation { "Name" : "Evenly Spaced on RSL" }
    EVENLY_DIVIDE_RSL,
    annotation { "Name" : "Evenly Spaced on Core Length" }
    EVENLY_DIVIDE_CORE_LENGTH
}

export enum START_STATIC_POINTS
{
    annotation { "Name" : "Start at Core Tail" }
    CORE_TAIL,
    annotation { "Name" : "Center at MRS" }
    MRS
}

export enum TABLE_ORDER
{
    annotation { "Name" : "Ascending" }
    ASCENDING,
    annotation { "Name" : "Descending" }
    DESCENDING
}

export const pointDistBounds =
{
            (millimeter) : [5, 3 * 25.4, 150]
        } as LengthBoundSpec;

export const rslBounds =
{
            (millimeter) : [100, 1500, 2050]
        } as LengthBoundSpec;

export const pointNumBounds =
{
            (unitless) : [12, 40, 80]
        } as IntegerBoundSpec;

export enum exportUnits
{
    MILLIMETER,
    INCH,
    CENTIMETER
}

export const sigFigBounds = { (unitless) : [1, 3, 5] } as IntegerBoundSpec;

export function elFunction(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    println('definition.pointGeneration is: ' ~ definition.pointGeneration);
    if (definition.pointGeneration == POINT_TYPES.STATIC_DISTANCE)
    {
        //println('Static Distance Selected');
        definition.showPointDist = true;
        definition.showPointCount = false;
    }
    else
    {
        //println('Evenly Divide Selected');
        definition.showPointDist = false;
        definition.showPointCount = true;
    }
    
    if (!isQueryEmpty(context, qHasAttribute(definition.sourceBody, 'RSL')))
    {
        definition.foundRSL = true;
        definition.rslFOUND = getAttribute(context, {
            "entity" : definition.sourceBody,
            "name" : 'RSL'
        });
    }

    else
    {
        definition.foundRSL = false;
    }

    return definition;
}



annotation { "Feature Type Name" : "Generate Core Data", "Editing Logic Function" : "elFunction" }
export const generateCoreData = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Table Order", "UIHint" : UIHint.SHOW_LABEL, "Default" : TABLE_ORDER.DESCENDING }
        definition.tableOrder is TABLE_ORDER;

        annotation { "Name" : "Point Generation Method", "UIHint" : UIHint.SHOW_LABEL }
        definition.pointGeneration is POINT_TYPES;

        annotation { "Name" : "RSL Attribute on Body", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.foundRSL is boolean;

        if (definition.foundRSL)
        {
            annotation { "Name" : "Found RSL", "UIHint" : UIHint.READ_ONLY }
            isLength(definition.rslFOUND, rslBounds);
        }

        else
        {
            annotation { "Name" : "Input RSL"}
            isLength(definition.rslINPUT, rslBounds);
        }

        annotation { "Name" : "showPointCount", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showPointCount is boolean;

        annotation { "Name" : "showPointDist", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.showPointDist is boolean;


        if (definition.showPointCount)
        {
            annotation { "Name" : "Number of Evenly Spaced Points" }
            isInteger(definition.numEvenPoints, pointNumBounds);
        }

        if (definition.showPointDist)
        {
            annotation { "Name" : "Static Point Distance Location", "UIHint" : UIHint.SHOW_LABEL }
            definition.staticStart is START_STATIC_POINTS;

            annotation { "Name" : "Distance Between Points" }
            isLength(definition.pointDistance, pointDistBounds);

        }

        annotation { "Name" : "Source Body", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
        definition.sourceBody is Query;

        annotation { "Name" : "Additional Points to Add to Table", "Filter" : EntityType.VERTEX }
        definition.addtlPoints is Query;

        annotation { "Group Name" : "Table format", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Table units", "Default" : exportUnits.MILLIMETER }
            definition.tableUnits is exportUnits;

            annotation { "Name" : "Sig figs" }
            isInteger(definition.sigFigs, sigFigBounds);

            annotation { "Name" : "Clear units", "Default" : true }
            definition.remUnits is boolean;
        }

        annotation { "Name" : "Print Debug Statements, Show Debug Entities" }
        definition.verbose is boolean;

    }
    {

        // ############################################ GENERAL SETUP ####################################

        if (definition.foundRSL)
        {
            definition.rsl = definition.rslFOUND;
        }
        else
        {
            definition.rsl = definition.rslINPUT;
        }

        // BUT WHAT IF EL Hasn't run? Need to get Body and Variable RSL Data
        var origRSL = definition.rsl;
        var varRSL = undefined;
        try
        {
            varRSL = getVariable(context, 'RSL');
        }
        var bodyRSL = undefined;
        try
        {
            bodyRSL = getAttribute(context, {
                        "entity" : definition.sourceBody,
                        "name" : 'RSL'
                    });
        }

        if ((!(varRSL is undefined) && !(varRSL == origRSL)) || (!(bodyRSL is undefined) && !(bodyRSL == origRSL)))
        {
            var endSTR = '';
            if (!(bodyRSL is undefined))
            {
                definition.rsl = bodyRSL;
                endSTR = 'BODY RSL';
            }
            else
            {
                definition.rsl = varRSL;
                endSTR = 'VARIABLE RSL';
            }
            var reportStr = 'RSL MISMATCH!!: Function -> ' ~ 1000 * origRSL.value ~ 'mm.';
            if (!(bodyRSL is undefined))
            {
                reportStr = reportStr ~ ' BODY RSL -> ' ~ 1000 * bodyRSL.value ~ 'mm.';
            }
            if (!(varRSL is undefined))
            {
                reportStr = reportStr ~ ' VARIABLE RSL -> ' ~ 1000 * varRSL.value ~ 'mm.';
            }

            reportFeatureInfo(context, id, reportStr ~ ' USING: ' ~ endSTR);
        }


        var coreExtents = evBox3d(context, {
                "topology" : definition.sourceBody,
                "tight" : true
            });


        // Copy and Split Core body to get center area.

        //copy core to split
        opPattern(context, id + "copyCore", {
                    "entities" : definition.sourceBody,
                    "transforms" : [transform(vector(0 * millimeter, 0 * millimeter, 0 * millimeter))],
                    "instanceNames" : ['1']
                });

        var copiedCore = qCreatedBy(id + "copyCore", EntityType.BODY);

        opSplitPart(context, id + "splitCore", {
                    "targets" : qCreatedBy(id + "copyCore", EntityType.BODY),
                    "tool" : qFrontPlane(EntityType.BODY),
                    "keepTools" : true,
                    "keepType" : SplitOperationKeepType.KEEP_BACK
                });

        opFitSpline(context, id + "searchWire", {
                    "points" : [
                        vector(coreExtents.minCorner[0], 0 * millimeter, coreExtents.minCorner[2]),
                        vector(coreExtents.maxCorner[0] + 0 * millimeter, 0 * millimeter, coreExtents.minCorner[2])
                    ]
                });
        var searchWire = qCreatedBy(id + "searchWire", EntityType.BODY);
        var searchWireLen = evLength(context, {
                "entities" : searchWire
            });
        // ############################################ GENERAL SETUP ####################################

        //++++++++++++++++++++++++++++++++++++++++++++++ DEFINE QUERIES FOR WIDTH, HEIGHT, TOP WIDTH, (?BASE ROUT?), (?GROOVE DEPTH?) ++++++++++++++++++++++++++++++++++++++++
        var copiedCoreEdges = qOwnedByBody(copiedCore, EntityType.EDGE);
        //addDebugEntities(context, copiedCoreEdges, DebugColor.RED);

        var frontPlaneEdges = qCreatedBy(id + "splitCore", EntityType.EDGE);
        //addDebugEntities(context, frontPlaneEdges, DebugColor.RED);
        var evFrontEdges = evaluateQuery(context, frontPlaneEdges);
        var edgeVertexX is array = [];
        for (var edgeQ in evFrontEdges)
        {
            var x0 = evVertexPoint(context, {
                    "vertex" : qEdgeVertex(edgeQ, true)
                });

            var x1 = evVertexPoint(context, {
                    "vertex" : qEdgeVertex(edgeQ, false)
                });

            if (!any(edgeVertexX, function(x)
                    {
                        return (abs(x - x0[0]) <= 0.01 * millimeter);
                    }))
            {
                edgeVertexX = append(edgeVertexX, x0[0]);
            }
            if (!any(edgeVertexX, function(x)
                    {
                        return (abs(x - x1[0]) <= 0.01 * millimeter);
                    }))
            {
                edgeVertexX = append(edgeVertexX, x1[0]);
            }
        }
        // if a vertex is within 0.1mm of a core extent, filter it out
        edgeVertexX = filter(edgeVertexX, function(x)
            {
                return ((x >= coreExtents.minCorner[0] + 0.1 * millimeter) && (x <= coreExtents.maxCorner[0] - 0.1 * millimeter));
            });
        edgeVertexX = deduplicate(edgeVertexX);

        //++++++++++++++++++++++++++++++++++++++++++++++ /DEFINE QUERIES FOR WIDTH, HEIGHT, TOP WIDTH, (?BASE ROUT?), (?GROOVE DEPTH?) ++++++++++++++++++++++++++++++++++++++++

        // ********************************************* DEFINE POINTS, CREATE PLANES ***************************************************
        var pointXArray is array = [coreExtents.minCorner[0], -1 * definition.rsl / 2, -1 * definition.rsl / 4, 0 * millimeter, definition.rsl / 4, definition.rsl / 2, coreExtents.maxCorner[0]]; // Array that will hold X values of table points.add Tip, FCP, XS1, MRS, XS2, Tail
        var evalPlanes is array = [];
        pointXArray = concatenateArrays([pointXArray, edgeVertexX]); // add unique verticies from edges on Front Plane
        var addPoints is array = [];
        if (definition.pointGeneration == POINT_TYPES.STATIC_DISTANCE)
        {
            if (definition.staticStart == START_STATIC_POINTS.CORE_TAIL)
            {
                var dummyDivide = (searchWireLen / definition.pointDistance);
                var numPoints = floor(dummyDivide, 1); // keeping the fence/post rule in mind here. The number of points is ACTUALLY numPoints + 1, but we already added a point at the tail, so the 'first point' starts at n = 2.
                //we've already added core tip - need to avoid doing that again (we'll deduplicate later, but it's worth avoiding)
                addPoints = mapArray(range(-1, -1 * numPoints), function(x)
                    {
                        return (x * definition.pointDistance + coreExtents.maxCorner[0]);
                    });

            }
            else if (definition.staticStart == START_STATIC_POINTS.MRS)
            {
                // how many points can we get in the tail region? ASSSUMING THAT MRS IS AT X == 0*millimeter!
                var numTailPoints = coreExtents.maxCorner[0] / definition.pointDistance;
                numTailPoints = floor(numTailPoints, 1);
                var startX = numTailPoints * definition.pointDistance;
                var numTipPoints = abs(coreExtents.minCorner[0] / definition.pointDistance);
                numTipPoints = floor(numTipPoints, 1);
                var totalPoints = numTipPoints + numTailPoints - 1; // subtracting 1 because an MRS Point was counted in both numTipPoints and numTailPoints
                addPoints = mapArray(range(0, -1 * totalPoints), function(x)
                    {
                        return (x * definition.pointDistance + startX);
                    });

            }

        }
        else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_RSL)
        {
            var rslStepDist = definition.rsl / (definition.numEvenPoints - 1);
            var numTailPoints = coreExtents.maxCorner[0] / rslStepDist;
            numTailPoints = floor(numTailPoints, 1);
            var startX = numTailPoints * rslStepDist;
            var numTipPoints = abs(coreExtents.minCorner[0] / rslStepDist);
            numTipPoints = floor(numTipPoints, 1);
            var totalPoints = numTipPoints + numTailPoints - 1;
            addPoints = mapArray(range(0, -1 * totalPoints), function(x)
                {
                    return (x * rslStepDist + startX);
                });

        }
        else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_CORE_LENGTH)
        {
            var coreStepDist = (coreExtents.maxCorner[0] - coreExtents.minCorner[0]) / (definition.numEvenPoints - 1);
            addPoints = mapArray(range(-1, -1 * (definition.numEvenPoints - 2)), function(x)
                {
                    return (x * coreStepDist + coreExtents.maxCorner[0]);
                });
        }


        pointXArray = concatenateArrays([pointXArray, addPoints]);
        // - - - - - - - - - - - Small detour to add points queried to pointXArray
        var evQueried = evaluateQuery(context, definition.addtlPoints);
        for (var ptQ in evQueried)
        {
            var pt = evVertexPoint(context, {
                    "vertex" : ptQ
                });
            pointXArray = append(pointXArray, pt[0]);
        }

        pointXArray = deduplicate(pointXArray);
        pointXArray = sort(pointXArray, function(a, b)
            {
                return a - b;
            });

        // Create Planes
        for (var i = 0; i < size(pointXArray); i += 1)
        {
            opPlane(context, id + ("evalPlane" ~ i), {
                        "plane" : plane(vector(pointXArray[i], 0 * millimeter, 0 * millimeter), vector(1, 0, 0))
                    });
            evalPlanes = append(evalPlanes, qCreatedBy(id + ("evalPlane" ~ i), EntityType.BODY));

        }

        // TEST NEW POINTS
        if (definition.verbose)
        {
            for (var xVal in pointXArray)
            {
                addDebugPoint(context, vector([xVal, 0 * millimeter, 0 * millimeter]), DebugColor.GREEN);
            }

        }
        // END TEST NEW POINTS

        // ********************************************* / DEFINE POINTS, CREATE PLANES *************************************************


        // -------------------------------------------- ITERATE OVER PLANES TO SOLVE FOR GEOMETRY. POPULATE POINTMAP -----------------------------------------------------------
        var calloutMap is map = { coreExtents.minCorner[0] : 'CORE TIP',
            -1 * definition.rsl / 2 : 'FCP',
            -1 * definition.rsl / 4 : 'XS-1',
            0 * millimeter : 'MRS',
            definition.rsl / 4 : 'XS-2',
            definition.rsl / 2 : 'ACP',
            coreExtents.maxCorner[0] : 'CORE TAIL'
        };
        // find frontPlaneEdges with two points at same X Value
        var sameX is array = [];
        for (var edge in evFrontEdges)
        {
            var startPoint = evVertexPoint(context, {
                    "vertex" : qEdgeVertex(edge, true)
                });
            var endPoint = evVertexPoint(context, {
                    "vertex" : qEdgeVertex(edge, false)
                });

            if (abs(startPoint[0] - endPoint[0]) < 0.01 * millimeter && abs(startPoint[2] - endPoint[2]) > 0.01 * millimeter)
            {
                if (abs(startPoint[0] - coreExtents.minCorner[0]) > 0.1 * millimeter && abs(startPoint[0] - coreExtents.maxCorner[0]) > 0.1 * millimeter)
                {
                    if (!any(sameX, function(x)
                            {
                                return (abs(x - startPoint[0]) <= 0.01 * millimeter);
                            }))
                    {
                        sameX = append(sameX, startPoint[0]);
                    }
                }

            }

        }

        // DUPLICATE POINTS SHOULD BE BINDING MATS - IF WITHIN THE SPECIFIED X Range 
        var numDuplicatePoints = size(sameX);
        
        sameX = sort(sameX, function(a, b) { return (a - b);});
        
        sameX = filter(sameX, function(x) {return ((x >= -450 * millimeter) && (x <= 500 * millimeter));});
        
        for (var i = 0; i < size(sameX); i += 1)
        {
            var startNum = i + 1;
            var endNum = 1;
            if (i >= numDuplicatePoints / 2)
            {
                startNum = numDuplicatePoints - (i);
                endNum = 2;
            }
            calloutMap[(sameX[i])] = 'BM ' ~ startNum ~ '-' ~ endNum;            

        }


        var pointMapArray is array = createPointMapArray(context, id, evalPlanes, searchWire, frontPlaneEdges, copiedCoreEdges, calloutMap, coreExtents.maxCorner, definition.verbose); // an array of maps. Each map will be keyed with its 'X value' and relevant table data contained in map.value

        if (definition.tableOrder == TABLE_ORDER.ASCENDING)
        {
            pointMapArray = reverse(pointMapArray);
        }
        
        var formatMap = { 'tableUnits' : definition.tableUnits, 'sigFigs' : definition.sigFigs, 'cleanUnits' : definition.remUnits };
        
        println('formatMap -> ' ~ formatMap);
            

        setAttribute(context, {
                    "entities" : qOrigin(EntityType.BODY),
                    "name" : "coreTableData",
                    "attribute" : { 'data' : pointMapArray, 'tableOrder' : definition.tableOrder, 'format' : { 'tableUnits' : definition.tableUnits, 'sigFigs' : definition.sigFigs, 'cleanUnits' : definition.remUnits } }
                });



        // -------------------------------------------- /ITERATE OVER PLANES TO SOLVE FOR GEOMETRY. POPULATE POINTMAP -----------------------------------------------------------


        opDeleteBodies(context, id + "deletePlanes", {
                    "entities" : qUnion(qUnion(evalPlanes), searchWire, copiedCore)
                });

    });

export function createPointMapArray(context is Context, id is Id, evalPlanes is array, searchWire is Query, frontPlaneEdges is Query, coreEdges is Query, calloutMap is map, coreTail is Vector, isVerbose is boolean) returns array
{

    var retArray is array = [];
    var calloutPoints = keys(calloutMap);


    for (var tPlane in evalPlanes)
    {
        var pln = evPlane(context, {
                "face" : qOwnedByBody(tPlane, EntityType.FACE)
            });
        var xVal = pln.origin[0];

        var tempMap is map = {};
        if (any(calloutPoints, function(x)
            {
                return x == xVal;
            }))
        {
            tempMap['callout'] = calloutMap[xVal];
        }
        else
        {
            tempMap['callout'] = '';
        }

        tempMap['mrsDist'] = xVal;
        tempMap['tailDist'] = coreTail[0] - xVal;
        
        println('Looking at the plane with origin at: ' ~ toString(pln.origin));

        var allCoreEdgeIntersections = qIntersectsPlane(coreEdges, pln);
        var allFrontEdgeIntersections = qIntersectsPlane(frontPlaneEdges, pln);
        var evAllCoreEdgeIntersections = evaluateQuery(context, allCoreEdgeIntersections);
        var evAllFrontEdgeIntersections = evaluateQuery(context, allFrontEdgeIntersections);
        var allCoreIntersectionPoints is array = [];
        var allFrontIntersectionPoints is array = [];

        var bottomPoint = evDistance(context, {
                    "side0" : pln,
                    "side1" : searchWire
                }).sides[0].point;

        for (var edge in evAllCoreEdgeIntersections)
        {
            var pt = evDistance(context, {
                        "side0" : pln,
                        "side1" : edge
                    }).sides[1].point;

            allCoreIntersectionPoints = append(allCoreIntersectionPoints, pt);
        }

        for (var edge in evAllFrontEdgeIntersections)
        {
            var pt = evDistance(context, {
                        "side0" : pln,
                        "side1" : edge
                    }).sides[1].point;

            allFrontIntersectionPoints = append(allFrontIntersectionPoints, pt);
        }

        allCoreIntersectionPoints = deduplicate(allCoreIntersectionPoints);
        allFrontIntersectionPoints = deduplicate(allFrontIntersectionPoints);

        var allPoints = concatenateArrays([allCoreIntersectionPoints, allFrontIntersectionPoints]);
        allPoints = deduplicate(allPoints);
        
        
        var maxY = 0 * millimeter;
        var maxZ = 0 * millimeter;
        var minZ = 10 * millimeter;
        var maxFrontZ = 0 * millimeter;
        var minFrontZ = 10 * millimeter;

        for (var pt in allPoints)
        {
            if (pt[1] > maxY)
            {
                maxY = pt[1];
            }
            if (pt[2] > maxZ)
            {
                maxZ = pt[2];
            }
            if (pt[2] < minZ)
            {
                minZ = pt[2];
            }

        }

        tempMap['coreWidth'] = 2 * maxY;
        tempMap['coreTickness'] = maxZ - bottomPoint[2];
        if (isVerbose)
        {
            addDebugPoint(context, vector(xVal, maxY, maxZ), DebugColor.BLACK);
            addDebugPoint(context, vector(xVal, -1 * maxY, maxZ), DebugColor.BLACK);
        }

        // if the lowest point with the max Y value does not have the minimum Z Value, than there is a baserout.
        var widestPoints = filter(allPoints, function(x)
        {
            return abs(x[1] - maxY) <= 0.01 * millimeter;
        });
        var highestPoints = filter(allPoints, function(x)
        {
            return abs(x[2] - maxZ) <= 0.01 * millimeter;
        });


        if (!any(widestPoints, function(x)
                {
                    return abs(x[2] - minZ) <= 0.01 * millimeter;
                })) // there are no widest points at minZ. We have a base-rout!
        {
            // find the lowest outside point
            println('There should be a baserout');
            var lowestZ = min(mapArray(widestPoints, function(x)
                {
                    return x[2];
                }));

            tempMap['baseRoutDepth'] = lowestZ - bottomPoint[2];

            var lowestWidest = filter(widestPoints, function(x)
            {
                return abs(x[2] - lowestZ) <= 0.01 * millimeter;
            });
            // find next narrowest point at this Z Level
            var atWidestLowestZ = filter(allPoints, function(x)
            {
                return abs(x[2] - lowestZ) <= 0.01 * millimeter;
            }); // all points with Z Value of lowestWidest
            var nextWidestY = mapArray(atWidestLowestZ, function(x)
            {
                return x[1];
            }); // get Y values of atWidestLowestZ
            nextWidestY = filter(nextWidestY, function(x)
                {
                    return abs(x - maxY) >= 0.01 * millimeter;
                }); // remove the any point with a Y value equal to maxY from the array
            nextWidestY = max(nextWidestY);
            // find edge that contains this point

                var insideEdgeQuery = qContainsPoint(coreEdges, vector(xVal, nextWidestY, lowestZ));
                // evDistance between outside point and edge.

                var brPT = evDistance(context, {
                        "side0" : vector(xVal, maxY, lowestZ),
                        "side1" : insideEdgeQuery
                    });

                if (isVerbose)
                {
                    addDebugPoint(context, brPT.sides[1].point, DebugColor.BLUE);
                    addDebugPoint(context, vector(brPT.sides[1].point[0], -1 * brPT.sides[1].point[1], brPT.sides[1].point[2]), DebugColor.BLUE);
                    //addDebugEntities(context, insideEdgeQuery, DebugColor.RED);

                }

                tempMap['baseRoutWidth'] = brPT.distance;
                tempMap['baseRoutDepth'] = lowestZ - bottomPoint[2];
            

        }
        else
        {
            tempMap['baseRoutWidth'] = '';
            tempMap['baseRoutDepth'] = '';
        }

        // do any of the widest points have the maximum Z height? If no there is a core top edge.
        if (!any(widestPoints, function(x)
                {
                    return abs(x[2] - maxZ) <= 0.01 * millimeter;
                }))
        {
            var zVals = mapArray(widestPoints, function(x)
            {
                return x[2];
            });
            var highestWidestZ = max(zVals);
            var highestWidest = filter(widestPoints, function(x)
                {
                    return (abs(x[2] - highestWidestZ) <= 0.01 * millimeter);
                })[0];

            var highZYvals = mapArray(highestPoints, function(x)
            {
                return x[1];
            });
            var widestHighestY = max(highZYvals);
            var widestHighest = filter(highestPoints, function(x)
                {
                    return abs(x[1] - widestHighestY) <= 0.01 * millimeter;
                })[0];

            tempMap['coreTopWidth'] = 2 * widestHighest[1];

            if (isVerbose)
            {
                addDebugPoint(context, highestWidest, DebugColor.RED);
                addDebugPoint(context, widestHighest, DebugColor.RED);

                addDebugPoint(context, vector(highestWidest[0], -1 * highestWidest[1], highestWidest[2]), DebugColor.RED);
                addDebugPoint(context, vector(widestHighest[0], -1 * widestHighest[1], widestHighest[2]), DebugColor.RED);
            }

            var coreTopAngle = atan((highestWidest[1] - widestHighest[1]) / (widestHighest[2] - highestWidest[2]));
            tempMap['coreTopAngle'] = round(coreTopAngle, 1 * degree);

        }
        else
        {
            tempMap['coreTopWidth'] = 2 * maxY;
            tempMap['coreTopAngle'] = '';
        }

        // What about the points on the front plane

        var planeFrontIntersections = evaluateQuery(context, qIntersectsPlane(frontPlaneEdges, pln));

        var maxZFrontPT = max(mapArray(allFrontIntersectionPoints, function(x)
            {
                return x[2];
            }));
        var minZFrontPT = min(mapArray(allFrontIntersectionPoints, function(x)
            {
                return x[2];
            }));

        tempMap['groovedThickness'] = maxZFrontPT - minZFrontPT;
        if (isVerbose)
        {
            addDebugPoint(context, vector(xVal, 0 * millimeter, maxZFrontPT), DebugColor.YELLOW);
            addDebugPoint(context, vector(xVal, 0 * millimeter, minZFrontPT), DebugColor.YELLOW);
        }

        var tempRetMap is map = {};
        tempRetMap = insertIntoMapOfArrays(tempRetMap, xVal, tempMap);

        // do we need an additional point?
        // find the lowest Z value of frontIntersectionPoints that is not equal to maxZFront or minZFront
        var frontZs = mapArray(allFrontIntersectionPoints, function(x)
        {
            return x[2];
        });

        var nextPT = filter(frontZs, function(x)
        {
            return (abs(x - maxZFrontPT) >= 0.05 * millimeter && abs(x - minZFrontPT) >= 0.05 * millimeter);
        });
        if (size(nextPT) > 1)
        {
            var ptZ = min(nextPT);
            var copiedRetMap = tempMap;
            copiedRetMap['groovedThickness'] = ptZ - minZFrontPT;
            if (abs(copiedRetMap['groovedThickness'] - tempMap['groovedThickness']) >= 0.01 * millimeter) // was getting duplicate points at pointed tail.
            {
                tempRetMap = insertIntoMapOfArrays(tempRetMap, xVal, copiedRetMap); // I still am.
            }

        }
        retArray = append(retArray, tempRetMap);
    }

    return retArray;
}

annotation { "Table Type Name" : "Core Data Table" }
export const coreTable = defineTable(function(context is Context, definition is map) returns Table
    precondition
    {
        // Define the parameters of the table type
    }
    {
        var columnDefinitions = [
            tableColumnDefinition("callout", "Callout"),
            tableColumnDefinition("mrsDist", "Dist. from MRS"),
            tableColumnDefinition("tailDist", "Dist. from Tail"),
            tableColumnDefinition("coreWidth", "Core Width"),
            tableColumnDefinition("coreThickness", "Full Core Thickness"),
            tableColumnDefinition("groovedThickness", "Grooved Thickness"),
            tableColumnDefinition("topEdgeWidth", "Top Width"),
            tableColumnDefinition("baseRoutDepth", "Base Rout Depth"),
            tableColumnDefinition("baseRoutWidth", "Base Rout Width"),
            tableColumnDefinition("topAngle", "Top Edge Angle")
        ];

        var tableRows = [];

        var bodiesWithAttribute = evaluateQuery(context, qHasAttribute("coreTableData"));

        const tableData = getAttribute(context, {
                    "entity" : bodiesWithAttribute[0],
                    "name" : "coreTableData"
                });

        var suffixStr = '';
        var scaleFactor = 1;

        const format = tableData.format;

        if (format.tableUnits == exportUnits.MILLIMETER)
        {
            scaleFactor = 1000;
            suffixStr = ' mm';
        }
        if (format.tableUnits == exportUnits.INCH)
        {
            scaleFactor = 1000 / 25.4;
            suffixStr = ' in';
        }
        if (format.tableUnits == exportUnits.CENTIMETER)
        {
            scaleFactor = 100;
            suffixStr = ' cm';
        }
        
        if (format.cleanUnits)
        {
            suffixStr = '';   
        }

        var tableArray = tableData['data'];
        var tableOrder = tableData['tableOrder'];

        for (var tablePoint in tableArray)
        {
            var points = values(tablePoint)[0];
            points = deduplicate(points); /// This really shouldn't be necessary, but Were getting some phanotom dupicated points on an imported .igs file.

            if (size(points) > 1) // if there is more than one point in the array
            {
                points = sort(points, function(x, y)
                    {
                        return (y['groovedThickness'] - x['groovedThickness']);
                    }); // now sorted in descending order of grooved thickness
                if (keys(tablePoint)[0] < 0 * millimeter)
                {
                    points = reverse(points);
                }
                if (tableOrder == TABLE_ORDER.DESCENDING)
                {
                    points = reverse(points);
                }

            }

            for (var point in points)

            {
                var newTableRow = { 
                    "callout" : point['callout'], 
                    "mrsDist" : roundToPrecision(point['mrsDist'].value * scaleFactor, format.sigFigs) ~ suffixStr, 
                    "tailDist" : roundToPrecision(point['tailDist'].value * scaleFactor, format.sigFigs) ~ suffixStr, 
                    "coreWidth" : roundToPrecision(point['coreWidth'].value * scaleFactor, format.sigFigs) ~ suffixStr,
                    "coreThickness" : roundToPrecision(point['coreTickness'].value * scaleFactor, format.sigFigs) ~ suffixStr,
                    "topEdgeWidth" : roundToPrecision(point['coreTopWidth'].value * scaleFactor, format.sigFigs) ~ suffixStr,
                    //"baseRoutDepth" : roundToPrecision(point["baseRoutDepth"].value * scaleFactor, format.sigFigs) ~ suffixStr, 
                    //"baseRoutWidth" : roundToPrecision(point["baseRoutWidth"].value * scaleFactor, format.sigFigs) ~ suffixStr, 
                    "groovedThickness" : roundToPrecision(point["groovedThickness"].value * scaleFactor, format.sigFigs) ~ suffixStr, 
                    "topAngle" : point["coreTopAngle"] 
                    };
                    
                if (isLength(point["baseRoutDepth"])) // in the event that the table row is blank, very bad things happen. 
                {
                    newTableRow["baseRoutDepth"] = roundToPrecision(point["baseRoutDepth"].value * scaleFactor, format.sigFigs) ~ suffixStr;  
                }
                if (isLength(point["baseRoutWidth"])) // in the event that the table row is blank, very bad things happen. 
                {
                    newTableRow["baseRoutWidth"] = roundToPrecision(point["baseRoutWidth"].value * scaleFactor, format.sigFigs) ~ suffixStr;  
                }
                    
                tableRows = append(tableRows, tableRow(newTableRow));
            }

        }
        return table("Core Points", columnDefinitions, tableRows);
    });




