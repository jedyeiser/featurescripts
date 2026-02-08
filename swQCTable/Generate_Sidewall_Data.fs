FeatureScript 2321;
import(path : "onshape/std/common.fs", version : "2321.0");

import(path : "onshape/std/table.fs", version : "2321.0");

import(path : "onshape/std/geomOperations.fs", version : "2321.0");

export enum POINT_TYPES
{
    annotation { "Name" : "Static Distance Between Points" }
    STATIC_DISTANCE,
    annotation { "Name" : "Evenly Spaced on RSL" }
    EVENLY_DIVIDE_RSL,
    annotation { "Name" : "Evenly Spaced on SW Length" }
    EVENLY_DIVIDE_SW_LENGTH
}

export enum START_STATIC_POINTS
{
    annotation { "Name" : "Start at SW Tail" }
    SW_TAIL,
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
    (millimeter) : [5, 3*25.4, 150]
} as LengthBoundSpec;

export const rslBounds = 
{
    (millimeter) : [100, 1500, 2050]
} as LengthBoundSpec;

export const pointNumBounds = 
{
    (unitless): [12, 40, 80]
} as IntegerBoundSpec;

export enum exportUnits
{
    MILLIMETER,
    INCH,
    CENTIMETER
}

export const filterPointdist = {(millimeter) : [0.01, 0.5, 5]} as LengthBoundSpec;

export const sigFigBounds = {(unitless) : [1, 2, 5]} as IntegerBoundSpec;

export function elFunction(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map ) returns map
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
    
        
    var bodyHasRSL = !isQueryEmpty(context, qHasAttribute(definition.sourceBody, 'RSL'));
    
    if (bodyHasRSL)
    {
         var bodyRSL = getAttribute(context, {
            "entity" : definition.sourceBody,
            "name" : 'RSL'
            });
        definition.foundRSL = true;
        definition.rslFOUND = bodyRSL;
    }
    
    else
    {
        definition.foundRSL = false;
    }
   
    return definition;   
}

annotation { "Feature Type Name" : "Generate Sidewall Table Data", "Editing Logic Function" : "elFunction" }
export const genSWData = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Table order", "UIHint" : UIHint.SHOW_LABEL , "Default" : TABLE_ORDER.DESCENDING}
        definition.tableOrder is TABLE_ORDER;
        
        annotation { "Name" : "Filter points", "Default" : true, "Description" : "Remove any points within a specified distance of eachother. FCP, XS1, MRS, XS2, ACP will always take priority" }
        definition.filterPoints is boolean;
        
        if (definition.filterPoints)
        {
            annotation { "Name" : "Reject points within" }
            isLength(definition.filterDist, filterPointdist);
        }
        
        annotation { "Name" : "Point generation method", "UIHint" : UIHint.SHOW_LABEL }
        definition.pointGeneration is POINT_TYPES;
        
        annotation { "Name" : "RSL Attribute on Body", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.foundRSL is boolean;
        
        if (definition.foundRSL)
        {
            annotation { "Name" : "Found RSL:", "UIHint" : UIHint.READ_ONLY}
            isLength(definition.rslFOUND, rslBounds);  
        }
        
        else
        {
            annotation { "Name" : "Input RSL:"}
            isLength(definition.rslINPUT, rslBounds);   
        }
        
        annotation { "Name" : "showPointCount", "UIHint" : UIHint.ALWAYS_HIDDEN}
        definition.showPointCount is boolean;
        
        annotation { "Name" : "showPointDist", "UIHint" : UIHint.ALWAYS_HIDDEN}
        definition.showPointDist is boolean;
        
        
        if (definition.showPointCount)
        {
            annotation { "Name" : "Number of evenly spaced points" }
            isInteger(definition.numEvenPoints, pointNumBounds);   
        }
        
        if (definition.showPointDist)
        {
            annotation { "Name" : "Static point distance location", "UIHint" : UIHint.SHOW_LABEL }
            definition.staticStart is START_STATIC_POINTS;
            
            annotation { "Name" : "Distance between points" }
            isLength(definition.pointDistance, pointDistBounds);

        }
        
        annotation { "Name" : "Source body", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1 }
        definition.sourceBody is Query;
        
        annotation { "Name" : "AdditionalpPoints to add to table", "Filter" : EntityType.VERTEX}
        definition.addtlPoints is Query;
        
        annotation { "Group Name" : "Table formatting", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Table units", "Default" : exportUnits.MILLIMETER }
            definition.tableUnits is exportUnits;
            
            annotation { "Name" : "Table decimal precision" }
            isInteger(definition.tableSigFigs, sigFigBounds);
            
            annotation { "Name" : "Show units in table", "Default" : true }
            definition.showUnits is boolean;   
        }
        
        annotation { "Name" : "Print debug statements, Show debug entities", "Default" : true}
        definition.verbose is boolean;
        
        annotation { "Name" : "Keep measurement wires", "Default" : true, "Description" : "Measurements are made by re-packaging edges into single wires at the top and bottom of the sidewall. This checkbox allows you to either keep those or remove them" }
        definition.keepWires is boolean;
        
    }
    {
        // Define the function's action
        if (definition.foundRSL)
        {
            definition.rsl = definition.rslFOUND;   
        }
        else
        {
            definition.rsl = definition.rslINPUT;
        }
        
        var bodyExtents = evBox3d(context, {
                "topology" : definition.sourceBody,
                "tight" : true
        });
        
        definition.swExtents = bodyExtents;
        
        var bottomEdges = getBottomEdges(context, id,  '1', definition.sourceBody);
        var topEdges = getTopEdges(context, id + 'topEdges', definition.sourceBody);

        var insidePath = constructPath(context, qUnion(bottomEdges.inside));
        var outsidePath = constructPath(context, qUnion(bottomEdges.outside));
        
        var insideTopPath = constructPath(context, qUnion(topEdges.inside));
        var outsideTopPath = constructPath(context, qUnion(topEdges.outside));
        
        var centerSpline = getCenterSpline(context, id, '2', insidePath, outsidePath);
        
        var centerTopSpline = getCenterSpline(context, id, 'topCenterSpline', insideTopPath, outsideTopPath);
        
        
        var searchLocs = getSearchLocs(context, id + ('getSearchLocs'), definition, centerSpline);
        
        
        var tableMap = generateTableMap(context, id + ('generateTableData'), definition.sourceBody, centerSpline, centerTopSpline, searchLocs, definition.verbose, {'showUnits' : definition.showUnits, 'tableUnits' : definition.tableUnits, 'sigFigs' : definition.tableSigFigs});
        
        setAttribute(context, {
                "entities" : definition.sourceBody,
                "name" : "swTableMap",
                "attribute" : {'direction' : definition.tableOrder, 'tableMap' : tableMap}
        });
        
        if (definition.keepWires)
        {
            setProperty(context, {
                    "entities" : centerSpline,
                    "propertyType" : PropertyType.NAME,
                    "value" : "SW_BOTTOM_CENTER_WIRE"
            });
            setProperty(context, {
                    "entities" : centerTopSpline,
                    "propertyType" : PropertyType.NAME,
                    "value" : "SW_TOP_CENTER_WIRE"
            });
        }
        else
        {
            opDeleteBodies(context, id + "deleteSWWires", {
                    "entities" : qUnion([centerSpline, centerTopSpline])
            });
        }
   
        
    });

/**
 * Return an array of maps specifying locations to place planes at when searching for SW data. 
 * Each map contains a map keys are callout and plane, with standard definitions for both
 * Stations that should be 'preferred' when filtering will contain a key:value pair 
 */
export function getSearchLocs(context is Context, id is Id, definition is map, centerSpline is Query) returns array
{
    var retArray = [];
    //add in the stupid/easy stuff first. These are the stations 
    retArray = append(retArray, {'callout' : 'FCP', 'plane' : plane(vector(-1*definition.rsl/2, 0 * millimeter, 0 * millimeter), vector(1, 0, 0)), 'preferred' : true});
    retArray = append(retArray, {'callout' : 'XS_1', 'plane' : plane(vector(-1*definition.rsl/4, 0 * millimeter, 0 * millimeter), vector(1, 0, 0)), 'preferred' : true});
    retArray = append(retArray, {'callout' : 'MRS', 'plane' : plane(vector(0 * millimeter, 0 * millimeter, 0 * millimeter), vector(1, 0, 0)), 'preferred' : true});
    retArray = append(retArray, {'callout' : 'XS_2', 'plane' : plane(vector(definition.rsl/4, 0 * millimeter, 0 * millimeter), vector(1, 0, 0)), 'preferred' : true});
    retArray = append(retArray, {'callout' : 'ACP', 'plane' : plane(vector(definition.rsl/2, 0 * millimeter, 0 * millimeter), vector(1, 0, 0)), 'preferred' : true});
    
    var splineBox = evBox3d(context, {
            "topology" : centerSpline,
            "tight" : true
    });
    
    var swLen = splineBox.maxCorner[0] - splineBox.minCorner[0];
    
    retArray = append(retArray, {'callout' : 'SW_END_TIP', 'plane' : plane(splineBox.minCorner, vector(1, 0, 0)), 'preferred' : false});
    retArray = append(retArray, {'callout' : 'SW_END_TAIL', 'plane' : plane(splineBox.maxCorner, vector(1, 0, 0)), 'preferred' : false});
    
    var stationSpacing = 1000 * millimeter; //this is just to initialize the variable - we're not actually spacing station 1m apart!
    
    if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_SW_LENGTH)
    {
        stationSpacing = swLen/definition.numEvenPoints;
    }
    else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_RSL)
    {
        stationSpacing = definition.rsl/definition.numEvenPoints;
    }
    else
    {
        stationSpacing = definition.pointDistance;
    }
    
    var numStations = ceil(swLen/stationSpacing);
    //println('numStations = ' ~ numStations);
    var startPoint = floor(splineBox.maxCorner[0]/stationSpacing)*stationSpacing; //This should put the FIRST point at the maximum point (inside the sidewall) that will have a point fall at MRS. SHOULD. 
    
    if ((definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_SW_LENGTH) || (definition.pointGeneration == POINT_TYPES.STATIC_DISTANCE && definition.staticStart == START_STATIC_POINTS.SW_TAIL))
    {
        startPoint = splineBox.maxCorner[0];
    }
    
    for (var i = 0; i < numStations; i += 1)
    {
        retArray = append(retArray, {'callout' : '', 'plane' : plane(vector(startPoint - i * stationSpacing, 0 * millimeter, 0 * millimeter), vector(1, 0, 0)), 'preferred' : false});
    }
    
    var cleanArray = [];
    
    for (var i = 0; i < size(retArray); i += 1)
    {
        var pointMap = retArray[i];
        if (pointMap.plane.origin[0] >= splineBox.minCorner[0] && pointMap.plane.origin[0] <= splineBox.maxCorner[0]) // only keep points within the sidewall.
        {
            if (definition.filterPoints)
            {
                if (!any(cleanArray, function(x) {return abs(pointMap.plane.origin[0] - x.plane.origin[0]) <= definition.filterDist;})) // no points in the cleaned array are within the user specified filter distance. Add the point!
                {
                    cleanArray = append(cleanArray, pointMap);
                }
                else
                {
                    if (pointMap.preferred) // the only instance where we'd want to REPLACE a point that's currently in the table is if the current point is a preffered point
                    {
                        cleanArray = filter(cleanArray, function(x) {return abs(pointMap.plane.origin[0] - x.plane.origin[0]) >= definition.filterDist;});
                        cleanArray = append(cleanArray, pointMap);
                    }
                }
            }
            else
            {
                cleanArray = append(cleanArray, pointMap);
            }
        }
        
    }
    
    cleanArray = sort(cleanArray, function(a, b) {return a.plane.origin[0] - b.plane.origin[0];});
    
    return cleanArray;
}

    
annotation { "Table Type Name" : "Sidewall Table" }
export const swTable = defineTable(function(context is Context, definition is map) returns TableArray
    precondition
    {
        // Define the parameters of the table type
    }
    {
        var targetBodies = evaluateQuery(context, qHasAttribute('swTableMap'));
        var tables = [];
        
        for (var swBody in targetBodies)
        {
            
       
            var fullTableMap = getAttribute(context, {
                    "entity" : swBody,
                    "name" : 'swTableMap'
            });
            
            var direction = fullTableMap.direction;
            var tableMap = fullTableMap.tableMap;
            var partName = fullTableMap.partName;
            
            
            var columnDefinitions = [
                tableColumnDefinition("callout", "Callout"),
                tableColumnDefinition("mrsDist", "Dist. from MRS"),
                tableColumnDefinition("tailDist", "Dist. from Tail"),
                tableColumnDefinition("swHeight", "Sidewall Height"),
                ];
                
            var rows = [];
            
            var sortedKeys = [];
            if (direction == TABLE_ORDER.ASCENDING)
            {
                sortedKeys = sort(keys(tableMap), function(a, b) {return (b - a);});   
            }
            else
            {
                sortedKeys = sort(keys(tableMap), function(a, b) {return (a - b);});
            }
            
            // Compute the table
            
            for (var xKey in sortedKeys)
            {
                var keyRow = tableMap[(xKey)];

                rows = append(rows, tableRow({"callout" : keyRow.callout, "mrsDist" : keyRow.xVal, "tailDist": keyRow.xFromTail, "swHeight" : keyRow.swHeight}));   
                
                
            }
            tables = append(tables, table((" Sidewall Table"), columnDefinitions, rows));
        }
        
        return tableArray(tables);
    });

    
    
export function generateTableMap(context is Context, id is Id, sourceBody is Query, centerSpline is Query, centerTopSpline is Query, searchLocs is array, verbose is boolean, formatting is map) returns map
{
    var splineExtents = evBox3d(context, {
            "topology" : centerSpline,
            "tight" : true
    });
    
    //setup formatting variables
    var suffixStr = '';
    var scaleFactor = 1;

    if (formatting.tableUnits == exportUnits.MILLIMETER)
    {
        suffixStr = ' mm';
        scaleFactor = 1000;
    }
    if (formatting.tableUnits == exportUnits.INCH)
    {
        suffixStr = ' in';
        scaleFactor = 1000/25.4;
    }
    if (formatting.tableUnits == exportUnits.CENTIMETER)
    {
        suffixStr = ' cm';
        scaleFactor = 100;
    }
    if (!formatting.showUnits)
    {
        suffixStr='';   
    }
        
    
    // <END> formatting variables
    
    var retMap = {};
    
    for (var i = 0; i < size(searchLocs); i += 1)
    {
        var searchLoc = searchLocs[i];
        var intersectEdges = qIntersectsPlane(qOwnedByBody(sourceBody, EntityType.EDGE), searchLoc.plane);
        var evIntersectEdges = evaluateQuery(context, intersectEdges);
        
        var bottomPoint = evDistance(context, {
                "side0" : centerSpline,
                "side1" : searchLoc.plane
        }).sides[0].point;
        
        var topPoint = evDistance(context, {
                "side0" : centerTopSpline,
                "side1" : searchLoc.plane
        }).sides[0].point;
        
        var swHeight = topPoint[2] - bottomPoint[2];
        var xVal = searchLoc.plane.origin[0];
        var xFromTail = splineExtents.maxCorner[0] - searchLoc.plane.origin[0];
        
        
        retMap[(xVal)] = {'xVal' : roundToPrecision(xVal.value*scaleFactor, formatting.sigFigs)~suffixStr, 'swHeight' : roundToPrecision(swHeight.value*scaleFactor, formatting.sigFigs) ~ suffixStr, 'callout': searchLoc.callout, 'xFromTail' : roundToPrecision(xFromTail.value*scaleFactor, formatting.sigFigs) ~ suffixStr};
        
        if (verbose)
        {
            addDebugPoint(context, topPoint, DebugColor.GREEN);
            addDebugPoint(context, bottomPoint, DebugColor.MAGENTA);
        }
    }
    
    return retMap;
}
    

    
export function getCenterSpline(context is Context, id is Id, idStr is string, insidePath is Path, outsidePath is Path) returns Query
{
    var retQ = qNothing();
    
    var splinePoints = [];
    
    for (var param in range(0, 1, 50))
    {
        var insidePoint = evPathTangentLines(context, insidePath, [param]).tangentLines[0].origin;
        var outsidePoint = evPathTangentLines(context, outsidePath, [param]).tangentLines[0].origin;
        
        var pointDist = evDistance(context, {
                "side0" : insidePoint,
                "side1" : outsidePoint
        });
        
        var flipOutside = (pointDist.distance > 15 * millimeter);
        
        if (flipOutside)
        {
            outsidePoint = evPathTangentLines(context, outsidePath, [1 - param]).tangentLines[0].origin;
        }
        
        var centerPoint = average([insidePoint, outsidePoint]);
        splinePoints = append(splinePoints, centerPoint);
        
    }
    
    //println('splinePoints: ' ~ toString(splinePoints));
    
    opFitSpline(context, id + ("fitCenterSpline" ~ idStr), {
            "points" : splinePoints
    });
    
    retQ = qCreatedBy(id + ("fitCenterSpline" ~ idStr), EntityType.BODY);
    
    return retQ;
}


export function getBottomEdges(context is Context, id is Id, idStr is string, sourceBody is Query) returns map
{
    var retMap = {};
    
    var allEdges = qOwnedByBody(sourceBody, EntityType.EDGE);
    
    // evaluate bounding box. Make a sweep with X planes to find lowestEdges
    var boundingBox = evBox3d(context, {
            "topology" : sourceBody,
            "tight" : true
    });
    
    // make spline shorter than box so that we filter out transfer edges. 
    opFitSpline(context, id + ("evalPlaneFitSpline" ~ idStr), {
            "points" : [
                vector( boundingBox.minCorner[0] + 3*millimeter,  0*millimeter,  0*millimeter) ,
                vector( boundingBox.maxCorner[0] - 3*millimeter,  0*millimeter,  0*millimeter) ,

            ]
    });
    
    var dummySpline = qCreatedBy(id + ("evalPlaneFitSpline" ~ idStr), EntityType.BODY);
    var insideEdges = [];
    var outsideEdges = [];
    var params = range(0, 1, 10);
    
    for (var i = 0; i < size(params); i += 1)
    {
        var planeLine = evEdgeTangentLine(context, {
                "edge" : qOwnedByBody(dummySpline, EntityType.EDGE),
                "parameter" : params[i]
        });
        
        opPlane(context, id + ( i ~ "searchPlane" ~ idStr), {
                "plane" : plane(planeLine.origin, planeLine.direction)
        });
        
        var searchPlane = qCreatedBy(id + ( i ~ "searchPlane" ~ idStr), EntityType.BODY);
        var evSearchPlane = evPlane(context, {
                "face" : qOwnedByBody(searchPlane, EntityType.FACE)
        });
        
        var intersectEdges = qIntersectsPlane(allEdges, evSearchPlane);
        var evIntersectEdges = evaluateQuery(context, intersectEdges);
        
        var lowest = {'query' : qNothing(), 'zVal' : 200*millimeter, 'yVal' : 0 * millimeter};
        var secondLowest = {'query' : qNothing(), 'zVal' : 200*millimeter, 'yVal' : 0*millimeter};
        
        
        for (var edge in evIntersectEdges)
        {
            var edgeDist = evDistance(context, {
                    "side0" : edge,
                    "side1" : searchPlane
            });
            
            var zVal = edgeDist.sides[0].point[2];
            var yVal = edgeDist.sides[0].point[1];
            
            if (zVal <= lowest.zVal)
            {
                secondLowest = lowest;
                lowest = {'query' : edge, 'zVal' : zVal, 'yVal' : yVal};
                
            }
            else if (zVal <= secondLowest.zVal)
            {
                secondLowest = {'query' : edge, 'zVal' : zVal, 'yVal' : yVal};
                
            }
            
        }
        
        var minY = min([lowest.yVal, secondLowest.yVal]);
        if (lowest.yVal == minY)
        {
            //println('inside lowest.minY');
            if (!any(insideEdges, function(x) {return x == lowest.query;}))
            {
                insideEdges = append(insideEdges, lowest.query) ;
                insideEdges = append(insideEdges, qTangentConnectedEdges(lowest.query));
            } 
            if (!any(outsideEdges, function(x) {return x == secondLowest.query;}))
            {
                outsideEdges = append(outsideEdges, secondLowest.query) ;
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(secondLowest.query));
            } 
        }
        else
        {
            //println('inside secondLowest.minY');
            if (!any(insideEdges, function(x) {return x == secondLowest.query;}))
            {
                insideEdges = append(insideEdges, secondLowest.query) ;
                insideEdges = append(insideEdges, qTangentConnectedEdges(secondLowest.query));
            } 
            if (!any(outsideEdges, function(x) {return x == lowest.query;}))
            {
                outsideEdges = append(outsideEdges, lowest.query) ;
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(lowest.query));
            } 
            
        }
        
        opDeleteBodies(context, id + ("deleteSearchPlane" ~ i), {
                "entities" : searchPlane
        });
        
    }
    

    opDeleteBodies(context, id + ("deleteBodies1" ~ idStr), {
            "entities" : qUnion([dummySpline])
    });
    
    insideEdges = evaluateQuery(context, qUnion(insideEdges));
    outsideEdges = evaluateQuery(context, qUnion(outsideEdges));
    
    retMap['inside'] = insideEdges;
    retMap['outside'] = outsideEdges;
    
    
    return retMap;
}

export function getTopEdges(context is Context, id is Id, sourceBody is Query) returns map
{
    var retMap = {};
    
    var allEdges = qOwnedByBody(sourceBody, EntityType.EDGE);
    
    // evaluate bounding box. Make a sweep with X planes to find lowestEdges
    var boundingBox = evBox3d(context, {
            "topology" : sourceBody,
            "tight" : true
    });
    
    // make spline shorter than box so that we filter out transfer edges. 
    opFitSpline(context, id + ("evalPlaneFitSpline"), {
            "points" : [
                vector( boundingBox.minCorner[0] + 3*millimeter,  0*millimeter,  0*millimeter) ,
                vector( boundingBox.maxCorner[0] - 3*millimeter,  0*millimeter,  0*millimeter) ,

            ]
    });
    
    var dummySpline = qCreatedBy(id + ("evalPlaneFitSpline"), EntityType.BODY);
    var insideEdges = [];
    var outsideEdges = [];
    var params = range(0, 1, 10);
    
    for (var i = 0; i < size(params); i += 1)
    {
        var planeLine = evEdgeTangentLine(context, {
                "edge" : qOwnedByBody(dummySpline, EntityType.EDGE),
                "parameter" : params[i]
        });
        
        opPlane(context, id + ( i ~ "searchPlane"), {
                "plane" : plane(planeLine.origin, planeLine.direction)
        });
        
        var searchPlane = qCreatedBy(id + ( i ~ "searchPlane"), EntityType.BODY);
        var evSearchPlane = evPlane(context, {
                "face" : qOwnedByBody(searchPlane, EntityType.FACE)
        });
        
        var intersectEdges = qIntersectsPlane(allEdges, evSearchPlane);
        var evIntersectEdges = evaluateQuery(context, intersectEdges);
        
        var highest = {'query' : qNothing(), 'zVal' : -200*millimeter, 'yVal' : 0 * millimeter};
        var secondHighest = {'query' : qNothing(), 'zVal' :-200*millimeter, 'yVal' : 0*millimeter};
        
        
        for (var edge in evIntersectEdges)
        {
            var edgeDist = evDistance(context, {
                    "side0" : edge,
                    "side1" : searchPlane
            });
            
            var zVal = edgeDist.sides[0].point[2];
            var yVal = edgeDist.sides[0].point[1];
            
            if (zVal >= highest.zVal)
            {
                secondHighest = highest;
                highest = {'query' : edge, 'zVal' : zVal, 'yVal' : yVal};
                
            }
            else if (zVal >= secondHighest.zVal)
            {
                secondHighest = {'query' : edge, 'zVal' : zVal, 'yVal' : yVal};
            }
        }
        
        var minY = min([highest.yVal, secondHighest.yVal]);
        if (highest.yVal == minY)
        {
            //println('inside lowest.minY');
            if (!any(insideEdges, function(x) {return x == highest.query;}))
            {
                insideEdges = append(insideEdges, highest.query) ;
                insideEdges = append(insideEdges, qTangentConnectedEdges(highest.query));
            } 
            if (!any(outsideEdges, function(x) {return x == secondHighest.query;}))
            {
                outsideEdges = append(outsideEdges, secondHighest.query) ;
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(secondHighest.query));
            } 
        }
        else
        {
            //println('inside secondLowest.minY');
            if (!any(insideEdges, function(x) {return x == secondHighest.query;}))
            {
                insideEdges = append(insideEdges, secondHighest.query) ;
                insideEdges = append(insideEdges, qTangentConnectedEdges(secondHighest.query));
            } 
            if (!any(outsideEdges, function(x) {return x == highest.query;}))
            {
                outsideEdges = append(outsideEdges, highest.query) ;
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(highest.query));
            } 
            
        }
        
        opDeleteBodies(context, id + ("deleteSearchPlane" ~ i), {
                "entities" : searchPlane
        });
        
    }
    

    opDeleteBodies(context, id + ("deleteBodies1"), {
            "entities" : qUnion([dummySpline])
    });
    
    insideEdges = evaluateQuery(context, qUnion(insideEdges));
    outsideEdges = evaluateQuery(context, qUnion(outsideEdges));
    
    retMap['inside'] = insideEdges;
    retMap['outside'] = outsideEdges;
    
    return retMap;
}





