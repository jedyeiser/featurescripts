FeatureScript 2909;
import(path : "onshape/std/common.fs", version : "2909.0");
import(path : "onshape/std/extend.fs", version : "2909.0");
import(path : "onshape/std/ruledSurface.fs", version : "2909.0");

//import pathProcessing
import(path : "e9dd34f07820388a202cb620", version : "c1ce0c99dcc25d3d7e2b0bb7");

//import regionProcessing
import(path : "d1cf8af3d05964b44c3ab4c0", version : "0cfec2c414434510e6694ac4");

//export import refSurfCore
export import(path : "828cc4108f1c8683bc0e59cf", version : "decdb33da99d8a1fe538479c");

//Testbed for implementing better and more robust 'region' logic for other tools.

export enum RegionOffsetType { CONSTANT, LINEAR, QUADRATIC, SMOOTH }

export const REGION_OFFSET_BOUNDS = { (millimeter) : [-500, 0, 500] } as LengthBoundSpec;
export const REGION_SURFACE_HEIGHT_BOUNDS = { (millimeter) : [0, 1, 500] } as LengthBoundSpec;

export function regionExplorerEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    for (var r = 0; r < size(definition.regions); r += 1)
    {
        var region = definition.regions[r];
        definition.regions[r].regionNum = r;
        if (length(region.name) == 0)
        {
            definition.regions[r].name = "Region " ~ r;
        }
    }
    return definition;
}

annotation { "Feature Type Name" : "Region explorer", "Feature Type Description" : "" }
export const regionExplorer = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Side surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.sideSurfBody is Query;
        
        annotation { "Name" : "Ref. wire", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1 }
        definition.refWire is Query;
        
        annotation { "Name" : "flipDir", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipDirection is boolean;
        
        annotation { "Name" : "Ref. point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
        definition.refPoint is Query;

        annotation { "Name" : "Return offset wires", "Default" : false }
        definition.returnOffsetWires is boolean;

        annotation { "Name" : "Return offset surfaces", "Default" : false }
        definition.returnOffsetSurfaces is boolean;

        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#name" }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "regionNUm", "UIHint" : UIHint.ALWAYS_HIDDEN } // use to default name
            isInteger(region.regionNum, RegionNumBounds);
            
            annotation { "Name" : "Extents from", "Default" : RegionExtentDef.ALONG_REF, "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.extentDef is RegionExtentDef;
            
            annotation { "Name" : "Name" }
            region.name is string;
            
            
            if (region.extentDef == RegionExtentDef.ALONG_REF)
            {
                annotation { "Name" : "Start x" }
                isLength(region.startX, LENGTH_BOUNDS);
                
                annotation { "Name" : "End x" }
                isLength(region.endX, LENGTH_BOUNDS);
            }
            else if (region.extentDef == RegionExtentDef.QUERY)
            {
                annotation { "Name" : "Ref. point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                region.startPoint is Query;
        
                annotation { "Name" : "Ref. point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                region.endPoint is Query;
            }
            
            annotation { "Name" : "Offset type", "Default" : RegionOffsetType.CONSTANT, "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.offsetType is RegionOffsetType;

            if (region.offsetType == RegionOffsetType.CONSTANT)
            {
                annotation { "Name" : "Offset" }
                isLength(region.offset, REGION_OFFSET_BOUNDS);
            }
            else
            {
                annotation { "Name" : "Start offset" }
                isLength(region.startOffset, REGION_OFFSET_BOUNDS);

                annotation { "Name" : "End offset" }
                isLength(region.endOffset, REGION_OFFSET_BOUNDS);

                if (region.offsetType == RegionOffsetType.QUADRATIC)
                {
                    annotation { "Name" : "Zero slope at start", "Default" : true, "UIHint" : UIHint.OPPOSITE_DIRECTION }
                    region.zeroSlopeAtStart is boolean;
                }
            }

            if (definition.returnOffsetSurfaces && region.offsetType == RegionOffsetType.CONSTANT)
            {
                annotation { "Name" : "Surface height" }
                isLength(region.surfaceHeight, REGION_SURFACE_HEIGHT_BOUNDS);
            }

        }
        
        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Sampling density", "Description" : "Points sampled per region (and per blend)" }
            isInteger(definition.samplingDensity, SamplingDensityBounds);
            
            annotation { "Name" : "Sampling type", "Default" : SamplingDensityType.NUM_POINTS }
            definition.samplingType is SamplingDensityType;
            

            annotation { "Name" : "Spline degree" }
            isInteger(definition.approxDegree, ApproxDegreeBounds);

            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ApproxToleranceBounds);

            annotation { "Name" : "Max control points" }
            isInteger(definition.approxMaxCP, ApproxMaxCPBounds);
        }
        
        annotation { "Name" : "Debug", "Default" : false }
        definition.debug is boolean;
        
        if (definition.debug)
        {
            annotation { "Group Name" : "Debug", "Collapsed By Default" : false, "Driving Parameter" : "debug" }
            {
                
                annotation { "Name" : "Step through?", "Default" : false }
                definition.stepThrough is boolean;
                
                if (definition.stepThrough)
                {
                    annotation { "Group Name" : "Step through", "Collapsed By Default" : false, "Driving Parameter" : "stepThrough"}
                    {
                        annotation { "Name" : "Step 0 - Setup", "Default" : false }
                        definition.step0 is boolean;
                        
                        annotation { "Name" : "Step 1 - Divide into regions", "Default" : false }
                        definition.step1 is boolean;
                        
                        annotation { "Name" : "Step 2 - Wires, transport frames", "Default" : false }
                        definition.step2 is boolean;
                        
                    }
                    
                }
                
                annotation { "Name" : "Show ref frames", "Default" : false }
                definition.showRefFrames is boolean;
                
                annotation { "Name" : "Show vertex frames" }
                definition.showVertexFrames is boolean;
                
            }
            
        }
        
        
        
        
        
    }
    {
        var refGeo = processSideSurf(context, id + "getRefWires", definition.sideSurfBody);
        
        var refPath = processPath(context, id, {'userSelection' : definition.refWire, "flipDirection" : definition.flipDirection, "referencePoint" : definition.refPoint, 'numPoints' : max(20, definition.samplingDensity)});
        
        if (definition.debug && definition.showRefFrames)
        {
            showRefFrames(context, refPath, false, false);
        }
        
        /*
        println('refPath -> ' ~ refPath);
        
        var testFrames = processFaceFrames(context, id + "testProcessFrames", refGeo.refBottomSurf, qUnion([qOwnedByBody(refGeo.refBottomSurf, EntityType.EDGE)]), {
            'samplingType' : definition.samplingDensityType, 
            'samplingNumber' : definition.samplingDensity
            }, {});
        */
        
        //region processing logic
        var processRegionsBool = true;
        
        if (processRegionsBool) //only don't fire if we're not debugging and don't want to show step 1. 
            {
                var processedRegions = processRegions(context, {
                    'regions' : definition.regions,
                    }, 
                    refPath);
                
                for (var r = 0; r < size(processedRegions); r += 1)
                {
                    var region = processedRegions[r];
                    //copy bottom surf
                    opPattern(context, id + ("region" ~ r ~ "refCopy"), {
                            "entities" : refGeo.refBottomSurf,
                            "transforms" : [identityTransform()],
                            "instanceNames" : ['1']
                    });
                    
                    var regionCopy = qCreatedBy(id + ("region" ~ r ~ "refCopy"), EntityType.BODY);
                    setProperty(context, {
                            "entities" : regionCopy,
                            "propertyType" : PropertyType.NAME,
                            "value" : "region" ~ r ~ "copy"
                    });
                    //create planes on ref wire
                    var startPlane = plane(region.startFrame.origin, region.startFrame.zAxis);
                    var endPlane = plane(region.endFrame.origin, region.endFrame.zAxis);
                    
                    var startTrims = qIntersectsPlane(regionCopy, startPlane);
                    var endTrims = qIntersectsPlane(regionCopy, endPlane);
                    //split if intersected
                    if (!isQueryEmpty(context, startTrims))
                    {
                        opSplitPart(context, id + ("region" ~ r ~ "startSplit"), {
                                "targets" : qUnion([startTrims]),
                                "tool" : startPlane
                        });
                    }
                    if (!isQueryEmpty(context, endTrims))
                    {
                        opSplitPart(context, id + ("region" ~ r ~ "endSplit"), {
                                "targets" : qUnion([endTrims]),
                                "tool" : endPlane
                        });
                    }
                    //delete appropriate body
                    var splitBodies = evaluateQuery(context, regionCopy);
                    var midpoint = (region.startFrame.origin + region.endFrame.origin)/2;
                    var midpointPlane = plane (midpoint, normalize((region.startFrame.origin - region.endFrame.origin)/millimeter));
                    var deleteBodies = qSubtraction(regionCopy, qIntersectsPlane(regionCopy, midpointPlane));
                    
                    opDeleteBodies(context, id + ("deleteTrimmedBits" ~ r), {
                            "entities" : qUnion([deleteBodies])
                    });
                    
                    //rename
                    setProperty(context, {
                            "entities" : regionCopy,
                            "propertyType" : PropertyType.NAME,
                            "value" : region.name ~ " refSurf"
                    });
                    
                    var splitEdges = qUnion([qCreatedBy(id + ("region" ~ r ~ "startSplit"), EntityType.EDGE), qCreatedBy(id + ("region" ~ r ~ "endSplit"), EntityType.EDGE)]);
                    var peripheryEdges = qUnion([qSubtraction(qOwnedByBody(regionCopy, EntityType.EDGE), splitEdges)]);

                    if (definition.regions[r].offsetType == RegionOffsetType.CONSTANT)
                    {
                        var offsetDist = definition.regions[r].offset;
                        if (offsetDist != 0 * meter)
                        {
                            extendSurface(context, id + ("regionOffset" ~ r), {
                                "entities"               : peripheryEdges,
                                "endCondition"           : ExtendBoundingType.BLIND,
                                "oppositeDirection"      : offsetDist < 0 * meter,
                                "extendDistance"         : abs(offsetDist),
                                "tangentPropagation"     : false,
                                "maintainCurvature"      : false,
                                "hasOffset"              : false,
                                "offsetOppositeDirection": false,
                                "offset"                 : 0 * meter
                            });
                        }

                        // Periphery edges after offset (boundary may have changed)
                        var postOffsetPeriphery = qSubtraction(qOwnedByBody(regionCopy, EntityType.EDGE), splitEdges);

                        if (definition.returnOffsetWires)
                        {
                            opPattern(context, id + ("regionWireCopy" ~ r), {
                                "entities"      : regionCopy,
                                "transforms"    : [identityTransform()],
                                "instanceNames" : ["wire"]
                            });
                            var wireCopyBody = qCreatedBy(id + ("regionWireCopy" ~ r), EntityType.BODY);
                            opDeleteFace(context, id + ("regionWireFace" ~ r), {
                                "deleteFaces"   : qOwnedByBody(wireCopyBody, EntityType.FACE),
                                "includeFillet" : false,
                                "capVoid"       : false,
                                "leaveOpen"     : true
                            });
                            setProperty(context, {
                                "entities"     : wireCopyBody,
                                "propertyType" : PropertyType.NAME,
                                "value"        : definition.regions[r].name ~ " offset wire"
                            });
                        }

                        if (definition.returnOffsetSurfaces)
                        {
                            var faceArr = evaluateQuery(context, qOwnedByBody(regionCopy, EntityType.FACE));
                            if (size(faceArr) > 0)
                            {
                                var faceBox = evBox3d(context, {"topology" : faceArr[0], "tight" : true});
                                var faceCenterPt = (faceBox.minCorner + faceBox.maxCorner) / 2;
                                var faceUV = evDistance(context, {"side0" : faceArr[0], "side1" : faceCenterPt}).sides[0].parameter;
                                var faceNormal = evFaceTangentPlane(context, {"face" : faceArr[0], "parameter" : faceUV}).normal;
                                if (faceNormal[2] < 0)
                                {
                                    faceNormal = -1 * faceNormal;
                                }
                                opRuledSurface(context, id + ("regionRuledSurface" ~ r), {
                                    "path"             : postOffsetPeriphery,
                                    "ruledSurfaceType" : RuledSurfaceType.ALIGNED_WITH_VECTOR,
                                    "ruledDirection"   : faceNormal,
                                    "width"            : definition.regions[r].surfaceHeight,
                                    "angle"            : 0
                                });
                                setProperty(context, {
                                    "entities"     : qCreatedBy(id + ("regionRuledSurface" ~ r), EntityType.BODY),
                                    "propertyType" : PropertyType.NAME,
                                    "value"        : definition.regions[r].name ~ " offset surface"
                                });
                            }
                        }
                    }

                    //addDebugEntities(context, peripheryEdges, DebugColor.CYAN);
                    
                    var faceFrames = processFaceFrames(context, id + ("processRef" ~ r ~"test"), regionCopy, peripheryEdges, {'samplingDensity': definition.samplingDensity, 'samplingType' : definition.samplingType, 'showFrames' : (definition.debug && definition.showVertexFrames)}, refPath);
                }
            }
            
        
        
        
        
    });
    
    
/**
 * Takes edges and a face query. Creates offset frames/definition around those edges. 
 */
 
export function processFaceFrames(context is Context, id is Id, sheetBody is Query, frameEdges is Query, samplingDef is map, refWireMap is map) returns array
{
    var keyVerticies = qAdjacent(frameEdges, AdjacencyType.VERTEX, EntityType.VERTEX);
    var vertexArray = evaluateQuery(context, keyVerticies);
    vertexArray = mapArray(vertexArray, function(v) {
        return {'query' : v, 'point' : evVertexPoint(context, {"vertex" : v})};
    });

    for (var i = 0; i < size(vertexArray); i += 1)
    {
        var vertexData = vertexArray[i];
        var framePoint = vertexData.point;

        // Periphery edges at this vertex only (filter out split edges)
        vertexData['adjacentEdges'] = evaluateQuery(context, qIntersection([
            qAdjacent(vertexData.query, AdjacencyType.VERTEX, EntityType.EDGE),
            frameEdges
        ]));
        if (size(vertexData.adjacentEdges) == 0)
        {
            vertexArray[i] = vertexData;
            continue;
        }

        // Face on sheetBody adjacent to this vertex
        var faceQ = qIntersection([
            qAdjacent(
                qAdjacent(vertexData.query, AdjacencyType.VERTEX, EntityType.EDGE),
                AdjacencyType.EDGE, EntityType.FACE),
            qOwnedByBody(sheetBody, EntityType.FACE)
        ]);
        var faceArray = evaluateQuery(context, faceQ);
        if (size(faceArray) == 0)
        {
            throw regenError("processFaceFrames: no face found adjacent to vertex");
        }
        faceQ = faceArray[0];

        // Face normal at vertex: one evDistance for UV, one evFaceTangentPlane
        var faceUV = evDistance(context, {"side0" : faceQ, "side1" : framePoint}).sides[0].parameter;
        var faceNormal = evFaceTangentPlane(context, {"face" : faceQ, "parameter" : faceUV}).normal;
        if (faceNormal[2] < 0)
        {
            faceNormal = -1 * faceNormal;
        }

        // Face interior reference point — one evBox3d replaces two evDistance probe calls per edge
        var faceBox = evBox3d(context, {"topology" : faceQ, "tight" : true});
        var faceInteriorPt = (faceBox.minCorner + faceBox.maxCorner) / 2;

        // Build one frame per adjacent periphery edge
        var vertexFrames = {};
        for (var j = 0; j < size(vertexData.adjacentEdges); j += 1)
        {
            var edge = vertexData.adjacentEdges[j];

            // Edge tangent at the endpoint that lies at this vertex.
            // Two cheap evEdgeTangentLine calls replace one evDistance + one evEdgeTangentLine.
            var startLine = evEdgeTangentLine(context, {
                "edge" : edge, "parameter" : 0, "arcLengthParameterization" : false
            });
            var edgeDir = startLine.direction;
            if (norm(startLine.origin - framePoint) > 1e-5 * meter)
            {
                edgeDir = evEdgeTangentLine(context, {
                    "edge" : edge, "parameter" : 1, "arcLengthParameterization" : false
                }).direction;
            }

            // Outward binormal: in-surface, perpendicular to edge, away from face interior.
            // Sign check with dot product — no kernel calls needed.
            var binormal = cross(faceNormal, edgeDir);
            if (dot(binormal, (faceInteriorPt - framePoint) / meter) > 0)
            {
                binormal = -1 * binormal;
            }

            // Frame: xAxis = faceNormal, zAxis = along edge, yAxis = outward binormal (derived)
            // yAxis(frame) = cross(zAxis, xAxis) = cross(cross(N,B), N) = B  (since B perp N)
            var frameZAxis = cross(faceNormal, binormal);
            var vertexFrame = coordSystem(framePoint, faceNormal, frameZAxis);
            vertexFrames = mergeMaps(vertexFrames, {(edge) : vertexFrame});

            if (samplingDef.showFrames)
            {
                var arrowLen = evLength(context, {"entities" : edge}) / 3;
                var arrowRad = arrowLen * 0.02;
                var xColor = j == 0 ? DebugColor.RED : DebugColor.ORANGE;
                var yColor = j == 0 ? DebugColor.GREEN : DebugColor.YELLOW;
                var zColor = j == 0 ? DebugColor.BLUE : DebugColor.CYAN;
                // RED/ORANGE  = xAxis = face normal (normal to surface and edge)
                // GREEN/YELLOW = yAxis = outward binormal (away from surface interior)
                // BLUE/CYAN   = zAxis = edge tangent
                addDebugArrow(context, framePoint, framePoint + arrowLen * vertexFrame.xAxis,  arrowRad,         xColor);
                addDebugArrow(context, framePoint, framePoint + arrowLen * yAxis(vertexFrame), arrowRad * 0.75, yColor);
                addDebugArrow(context, framePoint, framePoint + arrowLen * vertexFrame.zAxis,  arrowRad * 0.5,  zColor);
            }
        }

        vertexData['vertexFrames'] = vertexFrames;
        vertexArray[i] = vertexData;
    }
    return vertexArray;
}
    
/**
 * takes a surface body. returns a wire body for any closed loop of one sided edges along with information
 * that could be used to select these wire bodies. 
 */
export function processSideSurf(context is Context, id is Id, refSheetBody is Query) returns map
{
    var bodyEdges = qEdgeTopologyFilter(qUnion([qOwnedByBody(refSheetBody, EntityType.EDGE)]), EdgeTopology.ONE_SIDED);
    //addDebugEntities(context, bodyEdges, DebugColor.CYAN);
    
    var retMap = {};
    
    opExtractWires(context, id + "extractRefWires", {
            "edges" : bodyEdges
    });
    
    var bodyArray = evaluateQuery(context, qCreatedBy(id + "extractRefWires", EntityType.BODY));
    bodyArray = mapArray(bodyArray, function(x) {return {'query' : x, 'box3D' : evBox3d(context, {"topology" : x, "tight" : true})};});
    bodyArray = sort(bodyArray, function(a, b) {return a.box3D.minCorner[2] - b.box3D.minCorner[2];}); // sort by z-height
    
    retMap['topWire'] = bodyArray[1].query;
    retMap['bottomWire'] = bodyArray[0].query;
    
    setProperty(context, {
            "entities" : retMap['topWire'],
            "propertyType" : PropertyType.NAME,
            "value" : "refTopWire"
    });
    
    setProperty(context, {
            "entities" : retMap['bottomWire'],
            "propertyType" : PropertyType.NAME,
            "value" : "refBottomWire"
    });
    
    var botRefEdges = qUnion([qOwnedByBody(retMap.bottomWire, EntityType.EDGE)]);

    //addDebugEntities(context, botRefEdges, DebugColor.MAGENTA);

    // bottomSurf — extrude wire symmetrically in plane normal direction, then split with sideSurf
    var botWirePlane = getWireBodyPlane(context, retMap.bottomWire, 1e-6 * meter);
    var extrudeDir = botWirePlane.normal;

    // Ensure halfWidth exceeds sideSurf Y-extent by at least 10mm
    var sideSurfBox = evBox3d(context, {"topology" : refSheetBody, "tight" : true});
    var sideSurfHalfExtent = norm(sideSurfBox.maxCorner - sideSurfBox.minCorner) / 2;
    var halfWidth = max(175 * millimeter, sideSurfHalfExtent + 10 * millimeter);

    opExtrude(context, id + "extrudeRefBottom", {
        "entities"   : botRefEdges,
        "direction"  : extrudeDir,
        "endBound"   : BoundingType.BLIND,
        "endDepth"   : halfWidth,
        "startBound" : BoundingType.BLIND,
        "startDepth" : halfWidth
    });

    var extrudedBottomBody = qCreatedBy(id + "extrudeRefBottom", EntityType.BODY);

    // Split with the side surface
    opSplitPart(context, id + "splitRefBottom", {
        "targets" : extrudedBottomBody,
        "tool"    : refSheetBody
    });

    // Keep the piece closest to the side surface centroid (the inner bottom piece)
    var sideSurfCenter = (sideSurfBox.minCorner + sideSurfBox.maxCorner) / 2;
    var bottomPieces = evaluateQuery(context, extrudedBottomBody);
    var keepBottomBody = bottomPieces[0];
    var minDist = evDistance(context, {"side0" : keepBottomBody, "side1" : sideSurfCenter}).distance;
    for (var i = 1; i < size(bottomPieces); i += 1)
    {
        var d = evDistance(context, {"side0" : bottomPieces[i], "side1" : sideSurfCenter}).distance;
        if (d < minDist)
        {
            minDist = d;
            keepBottomBody = bottomPieces[i];
        }
    }
    var deleteBottomPieces = qSubtraction(extrudedBottomBody, keepBottomBody);
    if (!isQueryEmpty(context, deleteBottomPieces))
    {
        opDeleteBodies(context, id + "deleteBottomTrim", {"entities" : deleteBottomPieces});
    }

    retMap['refBottomSurf'] = extrudedBottomBody;
    setProperty(context, {
            "entities" : retMap['refBottomSurf'],
            "propertyType" : PropertyType.NAME,
            "value" : "refBottomSurf"
    });
    setProperty(context, {
            "entities" : retMap['refBottomSurf'],
            "propertyType" : PropertyType.APPEARANCE,
            "value" : color(137/255, 218/255, 211/255)
    });
    
    //topSurf
    var topRefEdges = qUnion([qOwnedByBody(retMap.topWire, EntityType.EDGE)]);
    opFillSurface(context, id + "fillRefTop", {
            "edgesG0" : topRefEdges,
            "edgesG1" : qNothing(),
            "edgesG2" : qNothing(),
            "guideVertices" : qNothing()
    });
    
    retMap['refTopSurf'] = qCreatedBy(id + "fillRefTop", EntityType.BODY);
    setProperty(context, {
            "entities" : retMap['refTopSurf'],
            "propertyType" : PropertyType.NAME,
            "value" : "refTopSurf"
    });
    setProperty(context, {
            "entities" : retMap['refTopSurf'],
            "propertyType" : PropertyType.APPEARANCE,
            "value" : color(234/255, 185/255, 125/255)
    });
    
    
    
    return retMap;
}

// WIRE PROCESSING TOOLS *******************************

/**
 * Given a query for a wire body, returns the plane that contains it.
 * Throws a regen error if the wire is not planar or collinear.
 *
 * @param wireBodyQuery {Query}       - must evaluate to a single wire body
 * @param tolerance {ValueWithUnits}  - planarity tolerance, e.g. 1e-6 * meter
 */
export function getWireBodyPlane(context is Context, wireBodyQuery is Query, tolerance is ValueWithUnits) returns Plane
{
    const edges = evaluateQuery(context, qOwnedByBody(wireBodyQuery, EntityType.EDGE));
    if (size(edges) == 0)
        throw regenError("Wire body contains no edges.");

    const vertices = evaluateQuery(context, qOwnedByBody(wireBodyQuery, EntityType.VERTEX));

    // --- 1. Try to get the plane directly from the first non-linear edge ---
    var candidatePlane = undefined;
    for (var edge in edges)
    {
        const curveDef = evCurveDefinition(context, { "edge" : edge, "returnBSplinesAsDerivatives" : false });
        if (!(curveDef is Line))
        {
            try
            {
                candidatePlane = evPlanarEdge(context, { "edge" : edge });
            }
            catch
            {
                throw regenError("Wire body contains a non-planar edge.");
            }
            break;
        }
    }

    // --- 2. Fallback: all-line wire — find plane from vertices via cross product ---
    if (candidatePlane == undefined)
    {
        const points = mapArray(vertices, function(v) {
            return evVertexPoint(context, { "vertex" : v });
        });

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
            const diff = points[i] - origin;
            if (norm(diff) <= tolerance)
                continue;
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

    // --- 3. Verify all vertices lie on the candidate plane ---
    for (var vertex in vertices)
    {
        const pt = evVertexPoint(context, { "vertex" : vertex });
        if (abs(dot(pt - candidatePlane.origin, candidatePlane.normal)) > tolerance)
            throw regenError("Wire body is not planar.");
    }

    // --- 4. Orient normal toward nearest positive world axis (Y preferred) ---
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
 * Auto-detects which endpoints are shared.
 * Returns "G2", "G1", "G0", or "NONE" (no shared vertex found).
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
    else throw regenError("edgeContinuity: edges do not share a vertex");

    // G0 confirmed — shared vertex found.

    var outgoingA = (paramA == 0.0) ? tA.direction : -tA.direction;
    var outgoingB = (paramB == 0.0) ? tB.direction : -tB.direction;

    if (dot(outgoingA, outgoingB) > -(1.0 - G1_COS_TOL))
        return GeometricContinuity.G0;  // ← was "G0"

    // G1 confirmed.

    var cA = evEdgeCurvature(context, { "edge" : edgeA, "parameter" : paramA, "arcLengthParameterization" : false });
    var cB = evEdgeCurvature(context, { "edge" : edgeB, "parameter" : paramB, "arcLengthParameterization" : false });
    var kA = cA.curvature;
    var kB = cB.curvature;

    if (abs(kA) < ZERO_K_TOL && abs(kB) < ZERO_K_TOL)
        return GeometricContinuity.G2;

    if (abs(kA) < ZERO_K_TOL || abs(kB) < ZERO_K_TOL)
        return GeometricContinuity.G1;

    if (abs(kA - kB) / max(abs(kA), abs(kB)) > G2_REL_TOL)
        return GeometricContinuity.G1;  // ← was "G1"

    return GeometricContinuity.G2;  // ← was GeometricContinuity.G0 — the silent bug
}