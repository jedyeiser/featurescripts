FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");

//import pathProcessing
import(path : "e9dd34f07820388a202cb620", version : "0837de9a9d2e74ba57457e77");


/**
 * 
 * 
 */


export const WallAngleBounds = {(degree) : [0, 15, 89]} as AngleBoundSpec;
export const PinchRadiusBounds = {(millimeter) : [0.5, 2, 10]} as LengthBoundSpec;
export const TopRadiusBounds = {(millimeter) : [0.5, 3, 20]} as LengthBoundSpec;
export const ContinuityOffsetBounds = {(millimeter) : [0, 5, 50]} as LengthBoundSpec;
export const DebugStepBounds = {(unitless) : [0, 0, 20]} as IntegerBoundSpec;
export const PinchOffsetBounds = {(millimeter) : [-3, 0, 3]} as LengthBoundSpec;
export const PointNumBounds = {(unitless) : [0, 0, 500]} as IntegerBoundSpec;
export const DefaultRegion = {"regionNum" : 0, "startPoint" : "", "endPoint" : "", "transitionType" : RegionTransitionType.SMOOTH, "regionName" : ""};

export const ApproxToleranceBounds = {(millimeter) : [0.001, 0.01, 1]}     as LengthBoundSpec;
export const ApproxDegreeBounds = {(unitless) : [2, 3, 5]} as IntegerBoundSpec;
export const ApproxMaxCPBounds = {(unitless) : [10, 100, 500]} as IntegerBoundSpec;

export const ControlPointMultiplierBounds = {(unitless) : [2, 5, 10]} as IntegerBoundSpec;
export const DistanceSamplingBounds = {(millimeter) : [1, 10, 50]} as LengthBoundSpec;

export enum SamplingType
{
    CONTROL_POINT,
    DISTANCE
}

export enum PointWallAngleDefinition
{
    CONTINUITY,
    RADIUS_ANGLE
}

export enum PointContinuityType
{
    G0,
    G1,
    G2
}

export enum PointDefinitionType
{
    DIST_FROM_REF,
    QUERY
}

export enum RegionTransitionType
{
    LINEAR,
    SMOOTH
}

export enum CDRegionBoundingType
{
    CONTAINED,
    SPLIT, 
    PARTIAL
}


export function topWallGenerationEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    var processedPath = processPath(context, id + "processPath", {
                "userSelection" : definition.refWire,
                "flipDirection" : false,
                "referencePoint" : definition.zeroPoint,
                "numPoints" : 20
        });
        
    //1. Order points as necessary, least to greatest along ref. In our case, we can assume our refWire is in the XZ plane. Least to greatest.
    
    for (var point in definition.wallPoints)
    {
        if (point.locType == PointDefinitionType.QUERY)
        {
            var pointFrame = frameAtQuery(context, processedPath, point.locPoint);
            point.pointFromRef = pointFrame.distFromRef;
        }   
    }
    
    definition.wallPoints = sort(definition.wallPoints, function(a, b) {return a.pointFromRef - b.pointFromRef;});
    
    for (var i = 0; i < size(definition.wallPoints); i += 1)
    {
        definition.wallPoints[i]['pointNum'] = i;
        if (length(definition.wallPoints[i].name) == 0 || startsWith(definition.wallPoints[i].name, "Point #:"))
        {
            definition.wallPoints[i].name = "Point #: " ~ i;   
        }
    }
    
    //3. create regions between each set of points, Region 0 always connects (ordered) point 0 and point 1, etc
    
    var oldRegions = definition.regions;
    var regions = [];
    
    for (var i = 1; i < size(definition.wallPoints); i += 1)
    {
        var startPoint = definition.wallPoints[i-1];
        var endPoint = definition.wallPoints[i];
        var oldRegion = size(oldRegions) > i-1 ? oldRegions[i-1] : DefaultRegion;
        
        oldRegion = mergeMaps(oldRegion, {"regionNum" : i-1}); // update region num
        oldRegion = mergeMaps(oldRegion, {"startPoint" : startPoint.name, "endPoint" : endPoint.name});
        
        if (length(oldRegion.regionName) == 0 || startsWith(oldRegion.regionName, "Region #"))
        {
            oldRegion.regionName = "Region # " ~ (i-1);   
        }
        
        regions = append(regions, oldRegion);
    }
    definition.regions = regions;
    return definition;
}


annotation { "Feature Type Name" : "Top Wall Generation", "Feature Type Description" : "Combines input geometry to create a top wall surfae, and/or it's constituents", "Editing Logic Function" : "topWallGenerationEditingLogic" }
export const topWallGeneration = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Wall definition type", "Default" : PointWallAngleDefinition.RADIUS_ANGLE, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.definitionType is PointWallAngleDefinition;
            
        annotation { "Name" : "Ref. Wire", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1 }
        definition.refWire is Query;
        
        annotation { "Name" : "Zero point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.zeroPoint is Query;
        
        annotation { "Name" : "Top surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.topSurf is Query;
        
        annotation { "Name" : "Cavity depth profile", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.cdProfile is Query;
        
        annotation { "Name" : "Sidewall rout surf", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.swRoutSurf is Query;
        
        
        annotation { "Name" : "Wall points", "Item name" : "Wall point", "Item label template" : "#name", "UIHint" : [UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.PREVENT_ARRAY_REORDER] }
        definition.wallPoints is array;
        for (var wallPoint in definition.wallPoints)
        {
            annotation { "Name" : "My Enum", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : PointDefinitionType.DIST_FROM_REF }
            wallPoint.locType is PointDefinitionType;
            
            annotation { "Name" : "pointNumber", "UIHint" : UIHint.ALWAYS_HIDDEN } //gets set by editing logic. lowest number (0) has lowest position along ref wire (generally, lowest x)
            isInteger(wallPoint.pointNum, PointNumBounds);
            
            if (wallPoint.locType == PointDefinitionType.DIST_FROM_REF)
            {
                annotation { "Name" : "Point from ref" }
                isLength(wallPoint.pointFromRef, LENGTH_BOUNDS);
                   
            }
            
            else if (wallPoint.locType == PointDefinitionType.QUERY)
            {
                annotation { "Name" : "Location point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                wallPoint.locPoint is Query;
            }
            
            annotation { "Name" : "Name" } // "Point #" set in UI if nothing is set
            wallPoint.name is string;
            
            annotation { "Name" : "Pinch offset" } // offset from theoretical to move the start of our wall/fillets in. Positive is towards the inside of the ski. 
            isLength(wallPoint.pinchOffset, PinchOffsetBounds);
            
            
            if (definition.definitionType == PointWallAngleDefinition.RADIUS_ANGLE)
            {
                annotation { "Name" : "Pinch radius" }
                isLength(wallPoint.pinchRadius, PinchRadiusBounds);
                
                annotation { "Name" : "Pinch radius" }
                isLength(wallPoint.topRadius, TopRadiusBounds);
                
                annotation { "Name" : "Wall angle" }
                isAngle(wallPoint.wallAngle, WallAngleBounds);
                
            }
            else if (definition.definitionType == PointWallAngleDefinition.CONTINUITY)
            {
                annotation { "Name" : "Continuity at pinch", "Default" : PointContinuityType.G1 }
                wallPoint.pinchContinuity is PointContinuityType;
                
                annotation { "Name" : "Continuity at top", "Default" : PointContinuityType.G1 }
                wallPoint.topContinuity is PointContinuityType;
                
                annotation { "Name" : "Offset" }
                isLength(wallPoint.offset, ContinuityOffsetBounds);
            }
            
            
        }
        
        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#regionName", "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS] }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "regionNum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, PointNumBounds);
            
            annotation { "Name" : "Region Name" }
            region.regionName is string;
            
            annotation { "Name" : "Start point" }
            region.startPoint is string;
            
            annotation { "Name" : "End point" }
            region.endPoint is string;
            
            annotation { "Name" : "Transition type", "Default" : RegionTransitionType.SMOOTH, "UIHint" : UIHint.SHOW_LABEL }
            region.transitionType is RegionTransitionType;
  
        }
        
        annotation { "Group Name" : "Sampling & Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling type", "UIHint" : UIHint.SHOW_LABEL, "Default" : SamplingType.CONTROL_POINT }
            definition.samplingType is SamplingType;
            
            if (definition.samplingType == SamplingType.CONTROL_POINT)
            {
                annotation { "Name" : "Control point multiplier" }
                isInteger(definition.controlPointMultiplier, ControlPointMultiplierBounds);
                
            }
            else if (definition.samplingType == SamplingType.DISTANCE)
            {
                annotation { "Name" : "Sample spacing" }
                isLength(definition.sampleSpacing, DistanceSamplingBounds);
                
            }
            
            annotation { "Name" : "Keep degree?", "Default" : true }
            definition.keepDegree is boolean;
            
            if (!definition.keepDegree)
            {
                annotation { "Name" : "Degree" }
                isInteger(definition.approxDegree, ApproxDegreeBounds);
                
            }
            
            annotation { "Name" : "Approximation tol" }
            isLength(definition.approximationTol, ApproxToleranceBounds);
            
            annotation { "Name" : "Max control points" }
            isInteger(definition.maxCPs, ApproxMaxCPBounds);
            
        }
        


        annotation { "Group Name" : "Debug", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Step through?", "Default" : false }
            definition.debugStepThrough is boolean;
            
            if (definition.debugStepThrough)
            {
                annotation { "Name" : "Debug step" }
                isInteger(definition.debugStepNum, DebugStepBounds);
                   
            }
            
            annotation { "Name" : "Print ref wire data" }
            definition.printRefCurveData is boolean;
            
            annotation { "Name" : "Show ref Frames" }
            definition.showRefFrames is boolean;
            
            annotation { "Name" : "Show point frames" }
            definition.showPointFrames is boolean;
            
            
        }
  
        
    }
    {
        // Define the function's action
        
        var processedPath = processPath(context, id + "processPath", {
                "userSelection" : definition.refWire,
                "flipDirection" : false,
                "referencePoint" : definition.zeroPoint,
                "numPoints" : 20
        });
        
        if (definition.printRefCurveData)
        {
            println('*** REF CURVE DATA ****');
            println('refParam -> ' ~ processedPath.refParam);
            //println('keys(frenetPath) -> ' ~ keys(processedPath.frenetPath));
            var paramLine = evPathTangentLines(context, processedPath.frenetPath.path, [processedPath.refParam]).tangentLines[0];
            println('refOrigin -> ' ~ toString(paramLine.origin));
        }
        
        if (definition.showRefFrames)
        {
            //println('keys(processedPath) -> ' ~ keys(processedPath));
            showRefFrames(context, processedPath, false, false);
        }
        
        const cdCopy = copyBody(context, id + 'copyCD', definition.cdProfile, "CD_COPY");
        const swRoutCopy = copyBody(context, id + 'copySWRout', definition.swRoutSurf, "SW-ROUT_COPY");
        const topCopy = copyBody(context, id + 'topCopy', definition.topSurf, "TOP_COPY");
        
        opIntersectFaces(context, id + "initialPinchWire", {
                "tools" : qUnion([qOwnedByBody(cdCopy, EntityType.FACE)]),
                "targets" : qUnion([qOwnedByBody(swRoutCopy, EntityType.FACE)])
        });
        
        opExtractWires(context, id + "extractPinchWire", {
                "edges" : qUnion([qCreatedBy(id + "initialPinchWire", EntityType.EDGE)])
        });
        
        opDeleteBodies(context, id + "deleteSeedPinchWires", {
                "entities" : qUnion([qCreatedBy(id + "initialPinchWire", EntityType.BODY)])
        });
        
        const pinchWire = qCreatedBy(id + "extractPinchWire", EntityType.BODY);
        setProperty(context, {
                "entities" : pinchWire,
                "propertyType" : PropertyType.NAME,
                "value" : "PINCH_WIRE"
        });
        
        // setup reference surfaces
        
        var cdSurfs = splitCDSurfs(context, id + "splitCDSurfs", cdCopy, swRoutCopy);
        
        // preprocess points
        const preprocessedPoints = preprocessPoints(context, definition.wallPoints, processedPath);
        
        definition.wallPoints = preprocessedPoints;
        
        if (definition.showPointFrames)
        {
            for (var point in preprocessedPoints)
            {
                var arrowDist = 20 * millimeter;
                //println('keys(point) - > ' ~ keys(point));
                //println('keys(point.pointRefFrame) -> ' ~ keys(point.pointRefFrame));
                var refFrame = point.pointRefFrame.frame;
                addDebugArrow(context, refFrame.origin, refFrame.origin + yAxis(refFrame) * arrowDist, 3 * millimeter, DebugColor.GREEN);
                addDebugArrow(context, refFrame.origin, refFrame.origin + refFrame.xAxis * arrowDist,  3 * millimeter,  DebugColor.RED);
                addDebugArrow(context, refFrame.origin, refFrame.origin + refFrame.zAxis * arrowDist,  3 * millimeter,   DebugColor.BLUE);
            }
        }
        
        var samplingDef = {
            "samplingType" : definition.samplingType,
            "keepDegree" : definition.keepDegree,
            "approximationTol" : definition.approximationTol,
            "maxCPs" : definition.maxCPs
        };
        
        if (!definition.keepDegree)
        {
            samplingDef['approxDegree'] = definition.approxDegree;
        }
        if (definition.samplingType == SamplingType.CONTROL_POINT)
        {
            samplingDef['controlPointMultiplier'] = definition.controlPointMultiplier;
        }
        else if (definition.samplingType == SamplingType.DISTANCE)
        {
            samplingDef["sampleSpacing"] = definition.sampleSpacing;
        }
        
        
        const processedRegions = processRegions(context, id + "preprocessRegions", processedPath, preprocessedPoints, definition.regions, cdSurfs, topCopy, cdCopy, pinchWire, samplingDef, definition.definitionType);
        // preprocess regions
        
    });

export function processRegions(context is Context, id is Id, processedPath is map, preprocessedPoints is array, regions is array, cdSurfs is map, topSurf is Query, cdSurf is Query, pinchWire is Query, samplingDef is map, definitionType is PointWallAngleDefinition) returns array
{
    var processedRegions = [];
    
    for (var i = 0; i < size(regions); i += 1)
    {
        var region = regions[i];
        var startPoint = preprocessedPoints[i];
        var endPoint = preprocessedPoints[i+1]; // depending quite a bit on demensions being correct here
        
        var transitionMap = {
            'transitionType' : region.transitionType, 
            'definitionType' : definitionType,
        };
        
        if (definitionType == PointWallAngleDefinition.CONTINUITY)
        {
            transitionMap['startPinchContinuity'] = startPoint.pinchContinuity;
            transitionMap['endPinchContinuity'] = endPoint.pinchContinuity;
            transitionMap['startTopContinuity'] = startPoint.topContinuity;
            transitionMap['endTopContinuity'] = endPoint.topContinuity;

        }
        
        opPattern(context, id + ("copyPinchForRegion" ~ i), {
                "entities" : pinchWire,
                "transforms" : [identityTransform()],
                "instanceNames" : ['1']
        });
        
        var regionWire = qCreatedBy(id + ("copyPinchForRegion" ~ i), EntityType.BODY);
        
         setProperty(context, {
                    "entities" : regionWire,
                    "propertyType" : PropertyType.NAME,
                    "value" : region.regionName ~ " PINCH_WIRE"
            });
        
        if (wireGenuinelyCrossesPlane(context, regionWire, startPoint.pointRefPlane))
        {
            opSplitPart(context, id + ("splitRefPinchWireStart" ~ i) , {
                    "targets" : regionWire,
                    "tool" : startPoint.pointRefPlane
            });
            
            var splitTrue = qSplitBy(id + ("splitRefPinchWireStart" ~ i), EntityType.BODY, true);
            var splitFalse = qSplitBy(id + ("splitRefPinchWireStart" ~ i), EntityType.BODY, false);
            
            var trueBox = evBox3d(context, {
                    "topology" : splitTrue,
                    "tight" : true
            });
            
            var falseBox = evBox3d(context, {
                    "topology" : splitFalse,
                    "tight" : true
            });
            
            var trueMidpoint = (trueBox.minCorner + trueBox.maxCorner)/2;
            var falseMidpoint = (falseBox.minCorner + falseBox.maxCorner)/2;
            
            var trueDist = norm(trueMidpoint - endPoint.pointRefPlane.origin);
            var falseDist = norm(falseMidpoint - endPoint.pointRefPlane.origin);
            
            var deleteSide = (trueDist > falseDist) ? splitTrue : splitFalse;
            
            opDeleteBodies(context, id + ("deleteRefPinchWireStart" ~ i), {
                    "entities" : deleteSide
            });
            
            
        }
        
        if (wireGenuinelyCrossesPlane(context, regionWire, endPoint.pointRefPlane))
        {
            opSplitPart(context, id + ("splitRefPinchWireEnd" ~ i) , {
                    "targets" : regionWire,
                    "tool" : endPoint.pointRefPlane
            });
            
            var splitTrue = qSplitBy(id + ("splitRefPinchWireEnd" ~ i), EntityType.BODY, true);
            var splitFalse = qSplitBy(id + ("splitRefPinchWireEnd" ~ i), EntityType.BODY, false);
            
            var trueBox = evBox3d(context, {
                    "topology" : splitTrue,
                    "tight" : true
            });
            
            var falseBox = evBox3d(context, {
                    "topology" : splitFalse,
                    "tight" : true
            });
            
            var trueMidpoint = (trueBox.minCorner + trueBox.maxCorner)/2;
            var falseMidpoint = (falseBox.minCorner + falseBox.maxCorner)/2;
            
            var trueDist = norm(trueMidpoint - startPoint.pointRefPlane.origin);
            var falseDist = norm(falseMidpoint - startPoint.pointRefPlane.origin);
            
            var deleteSide = (trueDist > falseDist) ? splitTrue : splitFalse;
            
            opDeleteBodies(context, id + ("deleteRefPinchWireEnd" ~ i), {
                    "entities" : deleteSide
            });
            
        }
        
        // do we have distinct bodies?
        var regionWireBodies = evaluateQuery(context, regionWire);
        
        region['buildMaps'] = []; //array of maps with pinch edge as keys. value is map with keys 'pinch', 'top', and optionally 'middle', 'wallTop', 'wallBottom'. will add startProfiles and endprofiels later
        
        for (var b = 0; b < size(regionWireBodies); b += 1)
        {
            var wireBody = regionWireBodies[b];
            var bodyEdges = evaluateQuery(context, qUnion([qOwnedByBody(wireBody, EntityType.EDGE)]));
            
            for (var e = 0; e < size(bodyEdges); e += 1)
            {
                var edgeBuildMap = {};

                var numPoints = 20;
                if (samplingDef.samplingType == SamplingType.CONTROL_POINT)
                {
                    var edgeBSplineCurve = evApproximateBSplineCurve(context, {
                            "edge" : bodyEdges[e]
                    });
                    
                    numPoints = size(edgeBSplineCurve.controlPoints) * samplingDef.controlPointMultiplier;
                }
                else if (samplingDef.samplingType == SamplingType.DISTANCE)
                {
                    var edgeLength = evLength(context, {
                            "entities" : bodyEdges[e]
                    });
                    
                    numPoints = max([ceil(edgeLength/samplingDef.sampleSpacing), 10]);
                }
                
                var paramRange = range(0, 1, numPoints);
                var edgePointLines = evEdgeTangentLines(context, {
                        "edge" : bodyEdges[e],
                        "parameters" : paramRange
                });
                //var edgePointMaps = mapArray(edgeLines, function(x) {return {'point' : x.origin, 'zAxis' : x.direction, 'refFrame' : frameAtPoint(context, processedPath, x.origin)};});
                ///edgePointMaps = mapArray(edgePointMaps, function(x) {return mergeMaps(x, {'distFromRef' : x.refFrame.distFromRef, 'xAxis' : x.refFrame.frame.xAxis});});
                
                var edgePointMaps = [];
                
                for (var j = 0; j < size(paramRange); j += 1)
                {
                    var edgePointMap = {'edgeParam' : paramRange[j], 'origin' : edgePointLines[j].origin, 'zAxis' : edgePointLines[j].direction, 'refFrame' : frameAtPoint(context, processedPath, edgePointLines[j].origin)};
                    edgePointMap = mergeMaps(edgePointMap, {'distFromRef' : edgePointMap.refFrame.distFromRef, 'xAxis' : edgePointMap.refFrame.frame.xAxis});
                    
                    var pointYAxis = cross(edgePointMap.zAxis, edgePointMap.xAxis);
                    var forwardStep = edgePointMap.origin + pointYAxis * 1 * millimeter;
                    
                    var insideSurfDist = evDistance(context, {
                            "side0" : cdSurfs['inside'],
                            "side1" : forwardStep
                    });
                    
                    var outsideSurfDist = evDistance(context, {
                            "side0" : cdSurfs['outside'],
                            "side1" : forwardStep
                    });
                    
                    edgePointMap['yAxis'] = (insideSurfDist.distance < outsideSurfDist.distance) ? pointYAxis : -1 * pointYAxis;
                    edgePointMap['zAxis'] = cross(edgePointMap.xAxis, edgePointMap.yAxis);
                    edgePointMap['pointFrame'] = coordSystem(edgePointMap.origin, edgePointMap.xAxis, edgePointMap.zAxis);
                    
                    var topDist = evDistance(context, {
                            "side0" : topSurf,
                            "side1" : line(edgePointMap.origin, edgePointMap.xAxis)
                    });
                    
                    var cdDist = evDistance(context, {
                            "side0" : cdSurf,
                            "side1" : line(edgePointMap.origin, edgePointMap.xAxis)
                    });
                    
                    edgePointMap['cavityDepth'] = norm(topDist.sides[0].point - cdDist.sides[0].point);
                    edgePointMaps = append(edgePointMaps, edgePointMap);
                }
                
                var edgeRegions = [];
                var curRegion = [];
                
                for (var k = 0; k < size(edgePointMaps); k += 1)
                {
                    if (edgePointMaps[k].cavityDepth > 0.001 * millimeter) // only take points with positive cavity depths
                    {
                        if (k > 0) //check to see if previosu point is 0
                        {
                            if (edgePointMaps[k-1].cavityDepth < 0.001 * millimeter)
                            {
                                   curRegion = append(curRegion, findEdgeZeroPoint(context, edgePointMaps[k], edgePointMaps[k-1], bodyEdges[e], topSurf, cdSurf, processedPath, cdSurfs));
                            }
                            else
                            {
                                curRegion = append(curRegion, edgePointMaps[k]);
                            }
                        }
                        if (k < size(edgePointMaps) - 1) //check to see if next point is 0
                        {
                            if (edgePointMaps[k+1].cavityDepth < 0.001 * millimeter)
                            {
                                curRegion = append(curRegion, findEdgeZeroPoint(context, edgePointMaps[k], edgePointMaps[k+1], bodyEdges[e], topSurf, cdSurf, processedPath, cdSurfs));
                            } 
                            else
                            {
                                curRegion = append(curRegion, edgePointMaps[k]);
                            }
                        }
                    }
                    else
                    {
                        if (size(curRegion) > 0)
                        {
                            edgeRegions = append(edgeRegions, curRegion);
                            curRegion=[];
                        }
                    }
                }
                
                if (size(curRegion) > 0)
                {
                    edgeRegions = append(edgeRegions, curRegion);
                }
                
                var regionCurveArray = [];
                
                for (var w = 0; w < size(edgeRegions); w += 1)
                {
                    
                    var regionPointMaps = edgeRegions[w];
                    
                    for (var t = 0; t < size(regionPointMaps); t += 1)
                    {
                        // first get offset pinch point
                        var curvePointMap = {};
                        var edgePointMap = regionPointMaps[t];
                        var regionSpan = endPoint.pointFromRef - startPoint.pointFromRef;
                        var curSpan = edgePointMap.distFromRef - startPoint.pointFromRef;
                        var spanParam = curSpan/regionSpan;
                        
                        var rawPinchPoint = edgePointMap.origin + edgePointMap.yAxis * profileValueAt(spanParam, startPoint.pinchOffset, endPoint.pinchOffset, transitionMap.transitionType);
                        
                        
                        var offset = (transitionMap.definitionType == PointWallAngleDefinition.CONTINUITY) ? 
                        profileValueAt(spanParam, startPoint.offset, endPoint.offset, transitionMap.transitionType) : calcCapWallOffset(context, edgePointMap.cavityDepth, profileValueAt(spanParam, startPoint.pinchRadius, endPoint.pinchRadius, transitionMap.transitionType), profileValueAt(spanParam, startPoint.topRadius, endPoint.topRadius, transitionMap.transitionType), profileValueAt(spanParam, startPoint.wallAngle, endPoint.wallAngle, transitionMap.transitionType));
                        
                        var offsetPoint = edgePointMap.origin + edgePointMap.yAxis * offset;
                        var offsetPointFrame = frameAtPoint(context, processedPath, offsetPoint);
                        var offsetLine = line(offsetPoint, offsetPointFrame.frame.xAxis);
                        var topPoint = evDistance(context, {
                                "side0" : topSurf,
                                "side1" : offsetLine
                        }).sides[0].point;
                        
                        var pinchPoint = evDistance(context, {
                                "side0" : cdSurf,
                                "side1" : rawPinchPoint
                        }).sides[0].point;
                        
                        //AddPointstoCurveCollections
                        edgePointMap['curvePointMap'] = {};
                        edgePointMap.curvePointMap['pinchCurve'] = pinchPoint;
                        edgePointMap.curvePointMap['topCurve'] = topPoint;
                        
                        if (transitionMap.definitionType == PointWallAngleDefinition.RADIUS_ANGLE)
                        {
                            var connectionMap = {'definitionType' : transitionMap.definitionType};
                            connectionMap['pinchRadius'] = profileValueAt(spanParam, startPoint.pinchRadius, endPoint.pinchRadius, transitionMap.transitionType);
                            connectionMap['topEdgeRadius'] = profileValueAt(spanParam, startPoint.topRadius, endPoint.topRadius, transitionMap.transitionType);
                            connectionMap['capWallAngle'] = profileValueAt(spanParam, startPoint.wallAngle, endPoint.wallAngle, transitionMap.transitionType);
                            connectionMap['pointFrame'] = edgePointMap.pointFrame;
                            
                            var connectedPoints = connectCapWallPoints(context, connectionMap, pinchPoint, topPoint, edgePointMap.cavityDepth, cdSurf, topSurf);
                            edgePointMap.curvePointMap = mergeMaps(edgePointMap.curvePointMap, connectedPoints);
                        }
                        
                        regionPointMaps[t] = edgePointMap;
                    }
                }
            }
            
        }
        
        
        
    }
    
    return processedRegions;
}

function connectCapWallPoints(context is Context, connectionMap is map, pinchPoint is Vector, topPoint is Vector, cavityDepth is ValueWithUnits, cdSurf is Query, topSurf is Query) returns map
{
    var retMap = {};
    var minCD = (1 - cos(90 * degree - connectionMap.capWallAngle)) * (connectionMap.pinchRadius + connectionMap.topEdgeRadius);
    
    if (cavityDepth <= minCD)
    {
        var thetaOverride = 90 * degree - acos(1 - (cavityDepth/ (connectionMap.pinchRadius + connectionMap.topEdgeRadius)));
        var vertOffset = (1 - cos(thetaOverride)) * connectionMap.pinchRadius;
        var sideOffset = sin(90 * degree - thetaOverride) * connectionMap.pinchRadius;
        retMap['midPoint'] = pinchPoint + vertOffset * connectionMap.pointFrame.xAxis + sideOffset * yAxis(connectionMap.pointFrame);
    }
    else
    {
        var pinchTopVertOffset = (1 - cos(connectionMap.capWallAngle)) * connectionMap.pinchRadius;
        var pinchTopSideOffset = sin(90 * degree - connectionMap.capWallAngle) * connectionMap.pinchRadius;
        
        var wallCD = cavityDepth - minCD;
        
        var topEdgeBottomVertOffset =  wallCD;
        var topEdgeBottomSideOffset =  tan(connectionMap.capWallAngle) * wallCD;
        
        retMap['pinchTop'] = pinchPoint + pinchTopVertOffset * connectionMap.pointFrame.xAxis + pinchTopSideOffset * yAxis(connectionMap.pointFrame);
        retMap['topEdgeBottom'] = pinchPoint + (topEdgeBottomVertOffset + pinchTopVertOffset) * connectionMap.pointFrame.xAxis + (topEdgeBottomSideOffset + pinchTopSideOffset) * yAxis(connectionMap.pointFrame);
    }
    
    return retMap;
}

function calcCapWallOffset(context is Context, cavityDepth is ValueWithUnits, pinchRadius is ValueWithUnits, topEdgeRadius is ValueWithUnits, capWallAngle is ValueWithUnits) returns ValueWithUnits
{
    var minCD = (1 - cos(90 * degree - capWallAngle)) * (pinchRadius + topEdgeRadius);
    
    if (cavityDepth <= minCD) //no line/wall
    {
        var thetaOverride = 90 * degree - acos(1 - (cavityDepth/ (pinchRadius + topEdgeRadius)));
        return sin(90 * degree - thetaOverride) * (pinchRadius + topEdgeRadius);
    }
    else // gretaer than minCD
    {
        var wallCD = cavityDepth - minCD;
        return sin(90 * degree - capWallAngle) * (pinchRadius + topEdgeRadius) + wallCD * tan(capWallAngle);
    }
}

function findEdgeZeroPoint(context, startHighPoint is map, startLowPoint is map, edge is Query, topSurf is Query, cdSurf is Query, processedPath is map, cdSurfs is map) returns map
{
    var modifyPoint = startHighPoint;
    var highParam = startHighPoint.edgeParam;
    var lowParam = startLowPoint.edgeParam;
    var iterations = 0;
    var distStep = 1 * meter;

    while(distStep > 0.001 * millimeter)
    {
        if (iterations >= 20)
        {
            break;
        }
        else
        {
            iterations += 1;
            var newParam = (highParam + lowParam)/2;
            distStep = abs(highParam - newParam);
            
            var edgeParamLine = evEdgeTangentLine(context, {
                    "edge" : edge,
                    "parameter" : newParam
            });
            
            var edgeParamFrame = frameAtPoint(context, processedPath, edgeParamLine.origin);
            var measureLine = line(edgeParamLine.origin, edgeParamFrame.frame.xAxis);
            
            var cdDist = evDistance(context, {
                    "side0" : cdSurf,
                    "side1" : measureLine
            });
            
            var topDist = evDistance(context, {
                    "side0" : topSurf,
                    "side1" : measureLine
            });
            
            var paramCD = norm(cdDist.sides[0].point - topDist.sides[0].point);
            if (paramCD > 0.001 * millimeter)
            {
                highParam = newParam;
            }
            else
            {
                lowParam = newParam;
            }
        }
    }
    
    var newParam = (highParam + lowParam)/2;
    var edgeParamLine = evEdgeTangentLine(context, {
                    "edge" : edge,
                    "parameter" : newParam
        });
    
    var edgeParamFrame = frameAtPoint(context, processedPath, edgeParamLine.origin);
    var measureLine = line(edgeParamLine.origin, edgeParamFrame.frame.xAxis);
    
    var cdDist = evDistance(context, {
            "side0" : cdSurf,
            "side1" : measureLine
    });
    
    var topDist = evDistance(context, {
            "side0" : topSurf,
            "side1" : measureLine
    });
    
    var paramCD = norm(cdDist.sides[0].point - topDist.sides[0].point);
    
    var updatedEdgePointMap = mergeMaps(modifyPoint, {'edgeParameter' : newParam, 'origin' : edgeParamLine.origin, 'zAxis' : edgeParamLine.direction, 'xAxis' : edgeParamFrame.frame.xAxis, 'cavityDepth' : 0 * millimeter});
    
    var tempYAxis = cross(updatedEdgePointMap.zAxis, updatedEdgePointMap.xAxis);
    var forwardStep = updatedEdgePointMap.origin + tempYAxis * 1 * millimeter;
    
    var insideDist = evDistance(context, {
            "side0" : cdSurfs.inside,
            "side1" : forwardStep
    });
    var outsideDist = evDistance(context, {
            "side0" : cdSurfs.outside,
            "side1" : forwardStep
    });
    
    updatedEdgePointMap['yAxis'] = (insideDist.distance < outsideDist.distance) ? tempYAxis : -1 * tempYAxis;
    updatedEdgePointMap['zAxis'] = cross(updatedEdgePointMap['xAxis'], updatedEdgePointMap['yAxis']);
    updatedEdgePointMap['pointFrame'] = coordSystem(updatedEdgePointMap.origin, updatedEdgePointMap.xAxis, updatedEdgePointMap.zAxis);
    
    
    return updatedEdgePointMap;
}

/**
 * Returns the interpolated offset at normalized position t ∈ [0,1] within a region.
 *   LINEAR    : linear ramp
 *   QUADRATIC : true quadratic — one endpoint has zero slope
 *   SMOOTH    : smootherstep (6t⁵ − 15t⁴ + 10t³) — C2 at both endpoints
 */
function profileValueAt(t is number, startVal is ValueWithUnits, endVal is ValueWithUnits,
    regionType is RegionTransitionType) returns ValueWithUnits
{
    var s;
    if (regionType == RegionTransitionType.LINEAR)
    {
        s = t;
    }
    
    else // SMOOTH
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return startVal + (endVal - startVal) * s;
}


/**
 * Returns true if the given wire body has at least one edge that genuinely
 * crosses the given plane — i.e., the edge has points on both sides of the
 * plane, not merely touching or being fully contained within it.
 *
 * Returns false if:
 *   - No edges intersect the plane, OR
 *   - All intersecting edges are fully contained in the plane.
 *
 * @param wire   : Query  — a query resolving to a single wire body
 * @param testPlane : Plane — the plane to test against
 */
export function wireGenuinelyCrossesPlane(context is Context, wire is Query, testPlane is Plane) returns boolean
{
    // Planarity tolerance: a point within this distance is considered "in the plane"
    const PLANE_TOL = 1e-6 * meter;

    // Collect all edges belonging to the wire body
    const edgeQuery = qOwnedByBody(wire, EntityType.EDGE);
    const edges = evaluateQuery(context, edgeQuery);

    if (size(edges) == 0)
        return false;

    // Arc-length parameterization: t=0 is start, t=0.5 is midpoint, t=1 is end.
    // 5 samples is sufficient for line segments; curved edges may need more — see note below.
    const sampleParams = [0.0, 0.25, 0.5, 0.75, 1.0];

    for (var edge in edges)
    {
        const tangentLines = evEdgeTangentLines(context, {
            "edge"       : edge,
            "parameters" : sampleParams
            // arcLengthParameterization defaults to true — t=0.5 is geometric midpoint
        });

        var hasPositiveSide = false;
        var hasNegativeSide = false;
        var allInPlane      = true;

        for (var tLine in tangentLines)
        {
            // tLine.origin is the sample point on the edge
            const signedDist = dot(tLine.origin - testPlane.origin, testPlane.normal);

            if (abs(signedDist) > PLANE_TOL)
            {
                allInPlane = false;
                if (signedDist > 0 * meter)
                    hasPositiveSide = true;
                else
                    hasNegativeSide = true;
            }
        }

        // Fully contained in plane - not a genuine crossing, continue to next edge
        if (allInPlane)
            continue;

        // Samples on BOTH sides - genuine crossing found, short-circuit
        if (hasPositiveSide && hasNegativeSide)
            return true;
    }

    // No genuine crossing found across any edge
    return false;
}

    
function splitCDSurfs(context is Context, id is Id, cdCopy is Query, swSurf is Query) returns map
{
    opPattern(context, id + "copyCDSurftrim", {
            "entities" : cdCopy,
            "transforms" : [identityTransform()],
            "instanceNames" : ['1']
    });
    
    opSplitPart(context, id + "splitCDWithSW", {
            "targets" : qCreatedBy(id + "copyCDSurftrim", EntityType.BODY),
            "tool" : swSurf,
            "keepTools" : true
    });
    
    var splitTrue = qSplitBy(id + "splitCDWithSW", EntityType.BODY, true);
    var splitFalse = qSplitBy(id + "splitCDWithSW", EntityType.BODY, false);
    
    var trueBox = evBox3d(context, {
            "topology" : splitTrue,
            "tight" : true
    });
    
    var falseBox = evBox3d(context, {
            "topology" : splitFalse,
            "tight" : true
    });
    
    var insideTrue = norm(trueBox.maxCorner - trueBox.minCorner) < norm(falseBox.maxCorner - falseBox.minCorner);
    var retMap = {};
    
    if (insideTrue)
    {
        retMap = {'inside' : splitTrue, 'outside' : splitFalse};
        
        setProperty(context, {
                "entities" : splitTrue,
                "propertyType" : PropertyType.NAME,
                "value" : "INSIDE_CD_SURF"
        });
        
        setProperty(context, {
                "entities" : splitFalse,
                "propertyType" : PropertyType.NAME,
                "value" : "OUTSIDE_CD_SURF"
        });
        
         setProperty(context, {
                "entities" : splitTrue,
                "propertyType" : PropertyType.APPEARANCE,
                "value" : color(195/255, 139/255, 205/255)
        });
        
        setProperty(context, {
                "entities" : splitFalse,
                "propertyType" : PropertyType.APPEARANCE,
                "value" : color(245/255, 213/255, 120/255)
        });
    }
    else
    {
        retMap = {'inside' : splitFalse, 'outside' : splitTrue};
        
        setProperty(context, {
                "entities" : splitFalse,
                "propertyType" : PropertyType.NAME,
                "value" : "INSIDE_CD_SURF"
        });
        
        setProperty(context, {
                "entities" : splitTrue,
                "propertyType" : PropertyType.NAME,
                "value" : "OUTSIDE_CD_SURF"
        });
        
         setProperty(context, {
                "entities" : splitFalse,
                "propertyType" : PropertyType.APPEARANCE,
                "value" : color(195/255, 139/255, 205/255)
        });
        
        setProperty(context, {
                "entities" : splitTrue,
                "propertyType" : PropertyType.APPEARANCE,
                "value" : color(245/255, 213/255, 120/255)
        });
    }
    
    return retMap;
}

    
    
function preprocessPoints(context is Context, pointArray is array, processedPath is map) returns array
{
    //updates fields in points array to include pertinatnt data bout where these points are on our ref wire(s) (pinch, ref)
    var updatedPoints = [];
    for (var point in pointArray)
    {
   
        point['pointRefFrame'] = (point.locType == PointDefinitionType.DIST_FROM_REF) ? frameAtDistFromRef(context, processedPath, point.pointFromRef) : frameAtQuery(context, processedPath, point.locPoint);
        
        point['pointRefPlane'] = plane(point['pointRefFrame'].frame.origin, point['pointRefFrame'].frame.zAxis);
        
        updatedPoints = append(updatedPoints, point);
    }
    return updatedPoints;
}

function copyBody(context is Context, id is Id, body is Query, name is string) returns Query
{
    opPattern(context, id + "copyBody", {
            "entities" : body,
            "transforms" : [identityTransform()],
            "instanceNames" : ['foo']
    });
    
    setProperty(context, {
            "entities" : qCreatedBy(id + "copyBody", EntityType.BODY),
            "propertyType" : PropertyType.NAME,
            "value" : name
    });
    
    return qCreatedBy(id + "copyBody", EntityType.BODY);
}
