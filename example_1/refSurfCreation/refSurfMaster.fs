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
export enum IntersectionContinuityType { G0, G1 }

export const REGION_OFFSET_BOUNDS        = { (millimeter) : [-500, 0, 500] } as LengthBoundSpec;
export const REGION_SURFACE_HEIGHT_BOUNDS = { (millimeter) : [0, 1, 500] }  as LengthBoundSpec;
export const INTERSECTION_OFFSET_BOUNDS  = { (millimeter) : [0, 0, 500] }   as LengthBoundSpec;

export function regionExplorerEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    // --- Region defaults ---
    for (var r = 0; r < size(definition.regions); r += 1)
    {
        var region = definition.regions[r];
        definition.regions[r].regionNum = r;
        if (length(region.name) == 0)
        {
            definition.regions[r].name = "Region " ~ r;
        }
    }

    // --- Intersection detection (ALONG_REF regions only) ---
    // Collect sortable region entries
    var sortable = [];
    for (var r = 0; r < size(definition.regions); r += 1)
    {
        var reg = definition.regions[r];
        if (reg.extentDef == RegionExtentDef.ALONG_REF)
        {
            sortable = append(sortable, {
                "name"   : reg.name,
                "startX" : reg.startX,
                "endX"   : reg.endX
            });
        }
    }

    // Sort by startX ascending
    sortable = sort(sortable, function(a, b)
    {
        return (a.startX - b.startX) / meter;
    });

    // Build lookup from existing intersections by name pair, to preserve user data
    var oldIntersections = definition.intersections;
    if (oldIntersections == undefined)
    {
        oldIntersections = [];
    }
    var oldMap = {};
    for (var ix in oldIntersections)
    {
        var key = ix.regionAName ~ "|" ~ ix.regionBName;
        oldMap = mergeMaps(oldMap, {(key) : ix});
    }

    // Walk consecutive sorted pairs and build new intersection array
    var newIntersections = [];
    for (var i = 0; i < size(sortable) - 1; i += 1)
    {
        var regA = sortable[i];
        var regB = sortable[i + 1];
        var gap  = regB.startX - regA.endX;

        var key      = regA.name ~ "|" ~ regB.name;
        var existing = oldMap[key];

        var ix;
        if (existing != undefined)
        {
            // Preserve user data; refresh auto fields
            ix              = existing;
            ix.regionAName  = regA.name;
            ix.regionBName  = regB.name;
            ix.gapDistance  = gap;
        }
        else
        {
            ix = {
                "regionAName"      : regA.name,
                "regionBName"      : regB.name,
                "gapDistance"      : gap,
                "joined"           : false,
                "startContinuity"  : IntersectionContinuityType.G0,
                "endContinuity"    : IntersectionContinuityType.G0,
                "startOffset"      : 0 * millimeter,
                "endOffset"        : 0 * millimeter
            };
        }
        newIntersections = append(newIntersections, ix);
    }

    definition.intersections = newIntersections;
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

        annotation { "Name" : "Return loft surface", "Default" : false }
        definition.returnLoftSurface is boolean;

        if (definition.returnLoftSurface)
        {
            annotation { "Name" : "Wall height" }
            isLength(definition.wallHeight, REGION_SURFACE_HEIGHT_BOUNDS);

            annotation { "Name" : "Second direction", "Default" : false }
            definition.wallSecondDir is boolean;

            if (definition.wallSecondDir)
            {
                annotation { "Name" : "Height (second dir.)" }
                isLength(definition.wallHeight2, REGION_SURFACE_HEIGHT_BOUNDS);
            }
        }

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
                    annotation { "Name" : "Zero slope at start", "Default" : true }
                    region.zeroSlopeAtStart is boolean;
                }
            }


        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection",
                     "Item label template" : "#regionAName -- #regionBName",
                     "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
        definition.intersections is array;
        for (var ix in definition.intersections)
        {
            annotation { "Name" : "Region A", "UIHint" : UIHint.ALWAYS_HIDDEN }
            ix.regionAName is string;

            annotation { "Name" : "Region B", "UIHint" : UIHint.ALWAYS_HIDDEN }
            ix.regionBName is string;

            annotation { "Name" : "Gap distance", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isLength(ix.gapDistance, REGION_OFFSET_BOUNDS);

            annotation { "Name" : "Joined", "Default" : false }
            ix.joined is boolean;

            if (ix.joined)
            {
                annotation { "Name" : "Start continuity", "Default" : IntersectionContinuityType.G0,
                             "UIHint" : UIHint.HORIZONTAL_ENUM }
                ix.startContinuity is IntersectionContinuityType;

                annotation { "Name" : "End continuity", "Default" : IntersectionContinuityType.G0,
                             "UIHint" : UIHint.HORIZONTAL_ENUM }
                ix.endContinuity is IntersectionContinuityType;

                annotation { "Name" : "Start offset" }
                isLength(ix.startOffset, INTERSECTION_OFFSET_BOUNDS);

                annotation { "Name" : "End offset" }
                isLength(ix.endOffset, INTERSECTION_OFFSET_BOUNDS);
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

                annotation { "Name" : "Show offset samples", "Default" : false }
                definition.showOffsetSamples is boolean;

                annotation { "Name" : "Show point frames", "Default" : false }
                definition.showPointFrames is boolean;

                annotation { "Name" : "Show loft points", "Default" : false }
                definition.showLoftPoints is boolean;

            }
            
        }
        
        
        
        
        
    }
    {
        var refGeo = processSideSurf(context, id + "getRefWires", definition.sideSurfBody, definition.refWire);
        
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
        
        // Overlap validation: sort ALONG_REF regions by startX, error if any pair overlaps.
        var regionsForOverlapCheck = [];
        for (var r = 0; r < size(definition.regions); r += 1)
        {
            var reg = definition.regions[r];
            if (reg.extentDef == RegionExtentDef.ALONG_REF)
            {
                regionsForOverlapCheck = append(regionsForOverlapCheck, {
                    "name"   : reg.name,
                    "startX" : reg.startX,
                    "endX"   : reg.endX
                });
            }
        }
        regionsForOverlapCheck = sort(regionsForOverlapCheck, function(a, b)
        {
            return (a.startX - b.startX) / meter;
        });
        for (var i = 0; i < size(regionsForOverlapCheck) - 1; i += 1)
        {
            var gap = regionsForOverlapCheck[i + 1].startX - regionsForOverlapCheck[i].endX;
            if (gap < -1e-6 * meter)
            {
                throw regenError("Regions overlap: \"" ~ regionsForOverlapCheck[i].name ~
                                 "\" and \"" ~ regionsForOverlapCheck[i + 1].name ~ "\"");
            }
        }

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
                    var planeEdges = qUnion([
                        qCoincidesWithPlane(qOwnedByBody(regionCopy, EntityType.EDGE), startPlane),
                        qCoincidesWithPlane(qOwnedByBody(regionCopy, EntityType.EDGE), endPlane)
                    ]);
                    var peripheryEdges = qEdgeTopologyFilter(qSubtraction(qOwnedByBody(regionCopy, EntityType.EDGE), qUnion([splitEdges, planeEdges])), EdgeTopology.ONE_SIDED);

                    // Build offset wires for all offset types via unified BSpline sampling.
                    // Per-sample: offsetPt = edgePt + offsetMag * outwardBinormal
                    if (definition.returnOffsetWires)
                    {
                        var zeroAtStartBuild = (definition.regions[r].offsetType == RegionOffsetType.QUADRATIC) ? definition.regions[r].zeroSlopeAtStart : true;
                        buildVariableOffsetCurves(context, id + ("varOffset" ~ r), regionCopy, peripheryEdges, {
                            "startFrameOrigin" : region.startFrame.origin,
                            "endFrameOrigin"   : region.endFrame.origin,
                            "offsetType"       : definition.regions[r].offsetType,
                            "offset"           : (definition.regions[r].offsetType == RegionOffsetType.CONSTANT) ? definition.regions[r].offset : (0 * millimeter),
                            "startOffset"      : definition.regions[r].startOffset,
                            "endOffset"        : definition.regions[r].endOffset,
                            "zeroSlopeAtStart" : zeroAtStartBuild,
                            "regionName"       : definition.regions[r].name
                        }, definition.samplingDensity, definition.approxDegree, definition.approxTolerance, definition.approxMaxCP);
                    }

                    if (definition.returnLoftSurface)
                    {
                        var zeroAtStartLoft = (definition.regions[r].offsetType == RegionOffsetType.QUADRATIC) ? definition.regions[r].zeroSlopeAtStart : true;
                        buildLoftSurfaces(context, id + ("loftSurf" ~ r), regionCopy, peripheryEdges, {
                            "startFrameOrigin" : region.startFrame.origin,
                            "endFrameOrigin"   : region.endFrame.origin,
                            "offsetType"       : definition.regions[r].offsetType,
                            "offset"           : (definition.regions[r].offsetType == RegionOffsetType.CONSTANT) ? definition.regions[r].offset : (0 * millimeter),
                            "startOffset"      : definition.regions[r].startOffset,
                            "endOffset"        : definition.regions[r].endOffset,
                            "zeroSlopeAtStart" : zeroAtStartLoft,
                            "regionName"       : definition.regions[r].name
                        }, definition.samplingDensity, definition.approxDegree, definition.approxTolerance, definition.approxMaxCP,
                        definition.wallHeight,
                        definition.wallSecondDir,
                        definition.wallSecondDir ? definition.wallHeight2 : (0 * millimeter));
                    }

                    if (definition.debug && (definition.showPointFrames || definition.showLoftPoints))
                    {
                        var zeroAtStartDbg = (definition.regions[r].offsetType == RegionOffsetType.QUADRATIC) ? definition.regions[r].zeroSlopeAtStart : true;
                        var dbgHasLoft     = definition.returnLoftSurface;
                        debugOffsetPoints(context, id + ("debugOffsetPts" ~ r), regionCopy, peripheryEdges, {
                            "startFrameOrigin" : region.startFrame.origin,
                            "endFrameOrigin"   : region.endFrame.origin,
                            "offsetType"       : definition.regions[r].offsetType,
                            "offset"           : (definition.regions[r].offsetType == RegionOffsetType.CONSTANT) ? definition.regions[r].offset : (0 * millimeter),
                            "startOffset"      : definition.regions[r].startOffset,
                            "endOffset"        : definition.regions[r].endOffset,
                            "zeroSlopeAtStart" : zeroAtStartDbg,
                            "showPointFrames"  : definition.showPointFrames,
                            "showLoftPoints"   : definition.showLoftPoints && dbgHasLoft,
                            "wallHeight"       : (definition.showLoftPoints && dbgHasLoft) ? definition.wallHeight : (0 * millimeter),
                            "wallSecondDir"    : definition.showLoftPoints && dbgHasLoft && definition.wallSecondDir,
                            "wallHeight2"      : (definition.showLoftPoints && dbgHasLoft && definition.wallSecondDir) ? definition.wallHeight2 : (0 * millimeter)
                        }, definition.samplingDensity);
                    }

                    if (definition.debug && definition.showOffsetSamples)
                    {
                        var zeroAtStart = (definition.regions[r].offsetType == RegionOffsetType.QUADRATIC) ? definition.regions[r].zeroSlopeAtStart : true;
                        sampleEdgeOffsets(context, id + ("sampleOffsets" ~ r), regionCopy, peripheryEdges, {
                            'startFrameOrigin' : region.startFrame.origin,
                            'endFrameOrigin'   : region.endFrame.origin,
                            'offsetType'       : definition.regions[r].offsetType,
                            'offset'           : (definition.regions[r].offsetType == RegionOffsetType.CONSTANT) ? definition.regions[r].offset : (0 * millimeter),
                            'startOffset'      : definition.regions[r].startOffset,
                            'endOffset'        : definition.regions[r].endOffset,
                            'zeroSlopeAtStart' : zeroAtStart
                        }, definition.samplingDensity);
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

        // Per-face reference normal at the face center — Z-flip applied once, then
        // per-vertex normal is dot-checked for consistency (handles curved surfaces).
        var faceBox = evBox3d(context, {"topology" : faceQ, "tight" : true});
        var faceInteriorPt = (faceBox.minCorner + faceBox.maxCorner) / 2;
        var faceCenterUV = evDistance(context, {"side0" : faceQ, "side1" : faceInteriorPt}).sides[0].parameter;
        var faceRefNormal = evFaceTangentPlane(context, {"face" : faceQ, "parameter" : faceCenterUV}).normal;
        if (faceRefNormal[2] < 0)
        {
            faceRefNormal = -1 * faceRefNormal;
        }

        var faceUV = evDistance(context, {"side0" : faceQ, "side1" : framePoint}).sides[0].parameter;
        var faceNormal = evFaceTangentPlane(context, {"face" : faceQ, "parameter" : faceUV}).normal;
        if (dot(faceNormal, faceRefNormal) < 0)
        {
            faceNormal = -1 * faceNormal;
        }

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
export function processSideSurf(context is Context, id is Id, refSheetBody is Query, refWire is Query) returns map
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

    // bottomSurf — extrude refWire symmetrically in plane normal direction, then split with sideSurf
    // Use smallest bounding box extent as the extrusion direction (wire lies in perpendicular plane)
    var refWireEdges = qOwnedByBody(refWire, EntityType.EDGE);
    var refWireBox = evBox3d(context, {"topology" : refWire, "tight" : true});
    var refExtents = refWireBox.maxCorner - refWireBox.minCorner;
    var extrudeDir;
    if (refExtents[0] <= refExtents[1] && refExtents[0] <= refExtents[2])
    {
        extrudeDir = vector(1, 0, 0);
    }
    else if (refExtents[1] <= refExtents[0] && refExtents[1] <= refExtents[2])
    {
        extrudeDir = vector(0, 1, 0);
    }
    else
    {
        extrudeDir = vector(0, 0, 1);
    }

    // Ensure halfWidth exceeds sideSurf extent in extrusion direction by at least 10mm
    var sideSurfBox = evBox3d(context, {"topology" : refSheetBody, "tight" : true});
    var sideSurfHalfExtent = norm(sideSurfBox.maxCorner - sideSurfBox.minCorner) / 2;
    var halfWidth = max(175 * millimeter, sideSurfHalfExtent + 10 * millimeter);

    opExtrude(context, id + "extrudeRefBottom", {
        "entities"   : refWireEdges,
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


// OFFSET INTERPOLATION HELPERS ************************

export function linearOffset(startOff is ValueWithUnits, endOff is ValueWithUnits, t is number) returns ValueWithUnits
{
    return startOff + (endOff - startOff) * t;
}

export function quadraticOffset(startOff is ValueWithUnits, endOff is ValueWithUnits, t is number, zeroSlopeAtStart is boolean) returns ValueWithUnits
{
    if (zeroSlopeAtStart)
    {
        // f(t) = startOff + (endOff - startOff) * t^2  — zero slope at t=0
        return startOff + (endOff - startOff) * t * t;
    }
    else
    {
        // f(t) = startOff + (endOff - startOff) * (2t - t^2)  — zero slope at t=1
        return startOff + (endOff - startOff) * (2 * t - t * t);
    }
}

export function smoothOffset(startOff is ValueWithUnits, endOff is ValueWithUnits, t is number) returns ValueWithUnits
{
    var s = t * t * (3 - 2 * t);
    return startOff + (endOff - startOff) * s;
}

function computeOffsetMag(offsetDef is map, t is number) returns ValueWithUnits
{
    if (offsetDef.offsetType == RegionOffsetType.CONSTANT)
    {
        return offsetDef.offset;
    }
    else if (offsetDef.offsetType == RegionOffsetType.LINEAR)
    {
        return linearOffset(offsetDef.startOffset, offsetDef.endOffset, t);
    }
    else if (offsetDef.offsetType == RegionOffsetType.QUADRATIC)
    {
        return quadraticOffset(offsetDef.startOffset, offsetDef.endOffset, t, offsetDef.zeroSlopeAtStart);
    }
    else
    {
        return smoothOffset(offsetDef.startOffset, offsetDef.endOffset, t);
    }
}

/**
 * Samples N points per periphery edge and shows each offset point as a magenta debug point.
 * offsetPt = edgePt + offsetMag * outwardBinormal
 *
 * offsetDef fields:
 *   startFrameOrigin  — origin of the region start frame (for t=0 orientation)
 *   offsetType        — RegionOffsetType
 *   offset            — magnitude for CONSTANT
 *   startOffset       — magnitude at t=0 for variable types
 *   endOffset         — magnitude at t=1 for variable types
 *   zeroSlopeAtStart  — (QUADRATIC only) boolean
 */
export function sampleEdgeOffsets(context is Context, id is Id, sheetBody is Query, peripheryEdges is Query, offsetDef is map, numPts is number)
{
    if (numPts < 2)
    {
        numPts = 2;
    }
    var edgeArray = evaluateQuery(context, peripheryEdges);

    // Axis for projecting sample points to a region-wide t in [0,1].
    // Strip meter units so dot products yield plain numbers.
    var regionAxisRaw = offsetDef.endFrameOrigin - offsetDef.startFrameOrigin;
    var regionAxis    = regionAxisRaw / meter;                      // dimensionless
    var regionAxisLen2 = dot(regionAxis, regionAxis);               // scalar

    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];

        // Face adjacent to this edge owned by sheetBody
        var faceQ = qIntersection([
            qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE),
            qOwnedByBody(sheetBody, EntityType.FACE)
        ]);
        var faceArr = evaluateQuery(context, faceQ);
        if (size(faceArr) == 0)
        {
            continue;
        }
        faceQ = faceArr[0];

        // Compute one stable reference normal per face at its center.
        var faceQBB = evBox3d(context, {"topology" : faceQ, "tight" : true});
        var faceQCenterPt = (faceQBB.minCorner + faceQBB.maxCorner) / 2;
        var faceQCenterUV = evDistance(context, {"side0" : faceQ, "side1" : faceQCenterPt}).sides[0].parameter;
        var faceRefNormal = evFaceTangentPlane(context, {"face" : faceQ, "parameter" : faceQCenterUV}).normal;
        if (faceRefNormal[2] < 0)
        {
            faceRefNormal = -1 * faceRefNormal;
        }

        // Build uniform parameter array [0, 1] with numPts samples
        var params = [];
        for (var i = 0; i < numPts; i += 1)
        {
            params = append(params, i / (numPts - 1));
        }

        var tangentLines = evEdgeTangentLines(context, {
            "edge"                   : edge,
            "parameters"             : params,
            "arcLengthParameterization" : true
        });

        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt = tangentLines[i].origin;

            // t = projection of edgePt onto the region start→end axis, clamped to [0,1]
            var disp = (edgePt - offsetDef.startFrameOrigin) / meter;
            var t = dot(disp, regionAxis) / regionAxisLen2;
            if (t < 0) { t = 0; }
            if (t > 1) { t = 1; }

            // Face normal, consistent with per-edge reference
            var ptUV = evDistance(context, {"side0" : faceQ, "side1" : edgePt}).sides[0].parameter;
            var faceNormal = evFaceTangentPlane(context, {"face" : faceQ, "parameter" : ptUV}).normal;
            if (dot(faceNormal, faceRefNormal) < 0)
            {
                faceNormal = -1 * faceNormal;
            }

            // Outward binormal: in-surface, perpendicular to edge, away from face interior
            var edgeTangent = tangentLines[i].direction;
            var binormal = cross(faceNormal, edgeTangent);
            if (dot(binormal, (faceQCenterPt - edgePt) / meter) > 0)
            {
                binormal = -1 * binormal;
            }

            // Lateral offset magnitude along outward binormal (yAxis)
            var offsetMag;
            if (offsetDef.offsetType == RegionOffsetType.CONSTANT)
            {
                offsetMag = offsetDef.offset;
            }
            else if (offsetDef.offsetType == RegionOffsetType.LINEAR)
            {
                offsetMag = linearOffset(offsetDef.startOffset, offsetDef.endOffset, t);
            }
            else if (offsetDef.offsetType == RegionOffsetType.QUADRATIC)
            {
                offsetMag = quadraticOffset(offsetDef.startOffset, offsetDef.endOffset, t, offsetDef.zeroSlopeAtStart);
            }
            else
            {
                offsetMag = smoothOffset(offsetDef.startOffset, offsetDef.endOffset, t);
            }

            var offsetPt = edgePt + offsetMag * binormal;
            addDebugPoint(context, offsetPt, DebugColor.MAGENTA);
        }
    }
}

/**
 * Builds a BSpline offset curve for each periphery edge of a variable-offset region.
 * At junctions between nearly-G1 edges (turning angle < 0.5 deg), enforces G1
 * continuity by constraining the BSpline endpoint derivatives to match the
 * original edge tangent direction.
 * Extracts wire bodies from the created BSpline curves and deletes the raw
 * BSpline bodies. Does not yet generate offset surfaces.
 *
 * offsetDef fields:
 *   startFrameOrigin, endFrameOrigin  -- region extent for t projection
 *   offsetType                        -- RegionOffsetType
 *   offset                            -- ValueWithUnits, magnitude for CONSTANT
 *   startOffset, endOffset            -- ValueWithUnits, magnitudes for variable types
 *   zeroSlopeAtStart                  -- boolean (QUADRATIC only)
 *   regionName                        -- string for wire body naming
 *
 * offsetPt = edgePt + offsetMag * outwardBinormal
 */
export function buildVariableOffsetCurves(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number,
    splineDegree is number, tolerance is ValueWithUnits, maxCP is number)
{
    if (numPts < 2)
    {
        numPts = 2;
    }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    if (size(edgeArray) == 0)
    {
        return;
    }

    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);

    // --- Per-edge: sample offset points and record endpoint geometry ---
    var edgeInfo = [];
    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];

        var tl0 = evEdgeTangentLine(context, {"edge" : edge, "parameter" : 0, "arcLengthParameterization" : false});
        var tl1 = evEdgeTangentLine(context, {"edge" : edge, "parameter" : 1, "arcLengthParameterization" : false});

        var faceArr = evaluateQuery(context, qIntersection([
            qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE),
            qOwnedByBody(sheetBody, EntityType.FACE)
        ]));
        if (size(faceArr) == 0)
        {
            edgeInfo = append(edgeInfo, undefined);
            continue;
        }
        var face = faceArr[0];

        // Compute one stable reference normal per face at its center.
        // All per-point normals are then flipped to match this reference via dot product,
        // ensuring consistency across the whole edge even on curved surfaces.
        var faceBB = evBox3d(context, {"topology" : face, "tight" : true});
        var faceCenterPt = (faceBB.minCorner + faceBB.maxCorner) / 2;
        var faceCenterUV = evDistance(context, {"side0" : face, "side1" : faceCenterPt}).sides[0].parameter;
        var faceRefNormal = evFaceTangentPlane(context, {"face" : face, "parameter" : faceCenterUV}).normal;
        if (faceRefNormal[2] < 0)
        {
            faceRefNormal = -1 * faceRefNormal;
        }

        var params = [];
        for (var i = 0; i < numPts; i += 1)
        {
            params = append(params, i / (numPts - 1));
        }
        var tangentLines = evEdgeTangentLines(context, {
            "edge"                      : edge,
            "parameters"                : params,
            "arcLengthParameterization" : true
        });

        var offPts = [];
        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt = tangentLines[i].origin;

            var disp = (edgePt - offsetDef.startFrameOrigin) / meter;
            var t    = dot(disp, regionAxis) / regionAxisLen2;
            if (t < 0)
            {
                t = 0;
            }
            if (t > 1)
            {
                t = 1;
            }

            var ptUV       = evDistance(context, {"side0" : face, "side1" : edgePt}).sides[0].parameter;
            var faceNormal = evFaceTangentPlane(context, {"face" : face, "parameter" : ptUV}).normal;
            if (dot(faceNormal, faceRefNormal) < 0)
            {
                faceNormal = -1 * faceNormal;
            }

            // Outward binormal: in-surface, perpendicular to edge, away from face interior
            var edgeTangent = tangentLines[i].direction;
            var binormal = cross(faceNormal, edgeTangent);
            if (dot(binormal, (faceCenterPt - edgePt) / meter) > 0)
            {
                binormal = -1 * binormal;
            }

            // Lateral offset magnitude along outward binormal (yAxis)
            var offsetMag;
            if (offsetDef.offsetType == RegionOffsetType.CONSTANT)
            {
                offsetMag = offsetDef.offset;
            }
            else if (offsetDef.offsetType == RegionOffsetType.LINEAR)
            {
                offsetMag = linearOffset(offsetDef.startOffset, offsetDef.endOffset, t);
            }
            else if (offsetDef.offsetType == RegionOffsetType.QUADRATIC)
            {
                offsetMag = quadraticOffset(offsetDef.startOffset, offsetDef.endOffset, t, offsetDef.zeroSlopeAtStart);
            }
            else
            {
                offsetMag = smoothOffset(offsetDef.startOffset, offsetDef.endOffset, t);
            }

            offPts = append(offPts, edgePt + offsetMag * binormal);
        }

        edgeInfo = append(edgeInfo, {
            "edge"         : edge,
            "face"         : face,
            "points"       : offPts,
            "p0"           : tl0.origin,
            "p1"           : tl1.origin,
            "tan0"         : tl0.direction,
            "tan1"         : tl1.direction,
            "faceRefNormal": faceRefNormal
        });
    }

    // --- Identify G1 junctions and assign derivative constraints ---
    // G1 criterion: outgoing-from-vertex directions are anti-parallel within 0.5 degrees.
    // outgoing from V along edge E:
    //   if E.p0 == V: outgoing = tan0  (param increases away from V)
    //   if E.p1 == V: outgoing = -tan1 (param increases into V, so reversed)
    // G1: dot(outgoing_A, outgoing_B) < -cos(0.5 deg)
    // Derivative constraint: natural edge tangent direction at that endpoint.
    const G1_COS  = cos(0.5 * degree);
    const POS_TOL = 1e-6 * meter;

    var derivConstraint = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        derivConstraint = append(derivConstraint, {"startDeriv" : undefined, "endDeriv" : undefined});
    }

    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined)
        {
            continue;
        }
        var eA = edgeInfo[ei];

        for (var ej = ei + 1; ej < size(edgeInfo); ej += 1)
        {
            if (edgeInfo[ej] == undefined)
            {
                continue;
            }
            var eB = edgeInfo[ej];

            // Case: A.p1 at V, B.p0 at V  (outgoing: -tan1_A, tan0_B)
            if (norm(eA.p1 - eB.p0) < POS_TOL)
            {
                if (dot(-1 * eA.tan1, eB.tan0) < -G1_COS)
                {
                    var dcA = derivConstraint[ei];
                    dcA.endDeriv = eA.tan1;
                    derivConstraint[ei] = dcA;
                    var dcB = derivConstraint[ej];
                    dcB.startDeriv = eB.tan0;
                    derivConstraint[ej] = dcB;
                }
            }
            // Case: A.p0 at V, B.p1 at V  (outgoing: tan0_A, -tan1_B)
            else if (norm(eA.p0 - eB.p1) < POS_TOL)
            {
                if (dot(eA.tan0, -1 * eB.tan1) < -G1_COS)
                {
                    var dcA = derivConstraint[ei];
                    dcA.startDeriv = eA.tan0;
                    derivConstraint[ei] = dcA;
                    var dcB = derivConstraint[ej];
                    dcB.endDeriv = eB.tan1;
                    derivConstraint[ej] = dcB;
                }
            }
            // Case: A.p1 at V, B.p1 at V  (outgoing: -tan1_A, -tan1_B)
            else if (norm(eA.p1 - eB.p1) < POS_TOL)
            {
                if (dot(-1 * eA.tan1, -1 * eB.tan1) < -G1_COS)
                {
                    var dcA = derivConstraint[ei];
                    dcA.endDeriv = eA.tan1;
                    derivConstraint[ei] = dcA;
                    var dcB = derivConstraint[ej];
                    dcB.endDeriv = eB.tan1;
                    derivConstraint[ej] = dcB;
                }
            }
            // Case: A.p0 at V, B.p0 at V  (outgoing: tan0_A, tan0_B)
            else if (norm(eA.p0 - eB.p0) < POS_TOL)
            {
                if (dot(eA.tan0, eB.tan0) < -G1_COS)
                {
                    var dcA = derivConstraint[ei];
                    dcA.startDeriv = eA.tan0;
                    derivConstraint[ei] = dcA;
                    var dcB = derivConstraint[ej];
                    dcB.startDeriv = eB.tan0;
                    derivConstraint[ej] = dcB;
                }
            }
        }
    }

    // --- Fit BSpline per edge and extract wire ---
    var wireBodies = [];

    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined)
        {
            continue;
        }
        var ed = edgeInfo[ei];
        var dc = derivConstraint[ei];

        var edgeLen    = evLength(context, {"entities" : ed.edge});
        var derivScale = edgeLen / 3;

        var targetDef = {"positions" : ed.points};
        if (dc.startDeriv != undefined)
        {
            targetDef = mergeMaps(targetDef, {"startDerivative" : dc.startDeriv * derivScale});
        }
        if (dc.endDeriv != undefined)
        {
            targetDef = mergeMaps(targetDef, {"endDerivative" : dc.endDeriv * derivScale});
        }

        try
        {
            var splineResults = approximateSpline(context, {
                "isPeriodic"       : false,
                "degree"           : splineDegree,
                "tolerance"        : tolerance,
                "maxControlPoints" : maxCP,
                "targets"          : [approximationTarget(targetDef)]
            });
            var splineData = splineResults[0];
            opCreateBSplineCurve(context, id + ("offCurve" ~ ei), {"bSplineCurve" : splineData});
            var curveBody = qCreatedBy(id + ("offCurve" ~ ei), EntityType.BODY);

            opExtractWires(context, id + ("offWire" ~ ei), {
                "edges" : qOwnedByBody(curveBody, EntityType.EDGE)
            });
            opDeleteBodies(context, id + ("deleteOffCurve" ~ ei), {"entities" : curveBody});

            var wireBody = qCreatedBy(id + ("offWire" ~ ei), EntityType.BODY);
            wireBodies = append(wireBodies, wireBody);
        }
        catch
        {
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
}

/**
 * Builds a lofted surface for each periphery edge of a region.
 *
 * For each edge:
 *   - Samples offset points: offsetPt = edgePt + offsetMag * outwardBinormal
 *   - Creates a "top" profile at offsetPt + wallHeight * faceNormal
 *   - Creates a "bottom" profile at offsetPt - wallHeight2 * faceNormal (if wallSecondDir)
 *     or at offsetPt itself (if not wallSecondDir)
 *   - At shared vertices between adjacent edges, blends the outward binormals into a
 *     bisector direction so adjacent patches share exact corner positions (enabling union)
 *   - Fits BSplines through top and bottom sample arrays, snapping control point
 *     endpoints to the computed corner positions
 *   - Calls opLoft between the top and bottom BSpline per edge
 *   - Unions all patches and names the result
 *
 * offsetDef fields (same as buildVariableOffsetCurves):
 *   startFrameOrigin, endFrameOrigin, offsetType, offset, startOffset, endOffset,
 *   zeroSlopeAtStart, regionName
 */
export function buildLoftSurfaces(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number,
    splineDegree is number, tolerance is ValueWithUnits, maxCP is number,
    wallHeight is ValueWithUnits, wallSecondDir is boolean, wallHeight2 is ValueWithUnits)
{
    if (numPts < 2)
    {
        numPts = 2;
    }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    if (size(edgeArray) == 0)
    {
        return;
    }

    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);
    const POS_TOL      = 1e-6 * meter;

    // --- Phase 1: per-edge data collection ---
    var edgeInfo = [];
    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];

        var faceArr = evaluateQuery(context, qIntersection([
            qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE),
            qOwnedByBody(sheetBody, EntityType.FACE)
        ]));
        if (size(faceArr) == 0)
        {
            edgeInfo = append(edgeInfo, undefined);
            continue;
        }
        var face = faceArr[0];

        var faceBB        = evBox3d(context, {"topology" : face, "tight" : true});
        var faceCenterPt  = (faceBB.minCorner + faceBB.maxCorner) / 2;
        var faceCenterUV  = evDistance(context, {"side0" : face, "side1" : faceCenterPt}).sides[0].parameter;
        var faceRefNormal = evFaceTangentPlane(context, {"face" : face, "parameter" : faceCenterUV}).normal;
        if (faceRefNormal[2] < 0)
        {
            faceRefNormal = -1 * faceRefNormal;
        }

        var tl0 = evEdgeTangentLine(context, {"edge" : edge, "parameter" : 0, "arcLengthParameterization" : false});
        var tl1 = evEdgeTangentLine(context, {"edge" : edge, "parameter" : 1, "arcLengthParameterization" : false});
        var p0  = tl0.origin;
        var p1  = tl1.origin;

        // Face normal and outward binormal at each endpoint
        var uv0 = evDistance(context, {"side0" : face, "side1" : p0}).sides[0].parameter;
        var fn0 = evFaceTangentPlane(context, {"face" : face, "parameter" : uv0}).normal;
        if (dot(fn0, faceRefNormal) < 0) { fn0 = -1 * fn0; }
        var bn0 = cross(fn0, tl0.direction);
        if (dot(bn0, (faceCenterPt - p0) / meter) > 0) { bn0 = -1 * bn0; }

        var uv1 = evDistance(context, {"side0" : face, "side1" : p1}).sides[0].parameter;
        var fn1 = evFaceTangentPlane(context, {"face" : face, "parameter" : uv1}).normal;
        if (dot(fn1, faceRefNormal) < 0) { fn1 = -1 * fn1; }
        var bn1 = cross(fn1, tl1.direction);
        if (dot(bn1, (faceCenterPt - p1) / meter) > 0) { bn1 = -1 * bn1; }

        // t and offsetMag at each endpoint
        var d0  = (p0 - offsetDef.startFrameOrigin) / meter;
        var t0  = dot(d0, regionAxis) / regionAxisLen2;
        if (t0 < 0) { t0 = 0; } if (t0 > 1) { t0 = 1; }
        var d1  = (p1 - offsetDef.startFrameOrigin) / meter;
        var t1  = dot(d1, regionAxis) / regionAxisLen2;
        if (t1 < 0) { t1 = 0; } if (t1 > 1) { t1 = 1; }
        var om0 = computeOffsetMag(offsetDef, t0);
        var om1 = computeOffsetMag(offsetDef, t1);

        // Interior samples
        var params = [];
        for (var i = 0; i < numPts; i += 1)
        {
            params = append(params, i / (numPts - 1));
        }
        var tangentLines = evEdgeTangentLines(context, {
            "edge"                      : edge,
            "parameters"                : params,
            "arcLengthParameterization" : true
        });

        var topPts    = [];
        var bottomPts = [];
        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt      = tangentLines[i].origin;
            var edgeTangent = tangentLines[i].direction;

            var disp = (edgePt - offsetDef.startFrameOrigin) / meter;
            var t    = dot(disp, regionAxis) / regionAxisLen2;
            if (t < 0) { t = 0; } if (t > 1) { t = 1; }

            var ptUV       = evDistance(context, {"side0" : face, "side1" : edgePt}).sides[0].parameter;
            var faceNormal = evFaceTangentPlane(context, {"face" : face, "parameter" : ptUV}).normal;
            if (dot(faceNormal, faceRefNormal) < 0) { faceNormal = -1 * faceNormal; }

            var binormal = cross(faceNormal, edgeTangent);
            if (dot(binormal, (faceCenterPt - edgePt) / meter) > 0) { binormal = -1 * binormal; }

            var offsetMag = computeOffsetMag(offsetDef, t);
            var offsetPt  = edgePt + offsetMag * binormal;

            topPts    = append(topPts,    offsetPt + wallHeight * faceNormal);
            bottomPts = append(bottomPts, wallSecondDir ? offsetPt - wallHeight2 * faceNormal : offsetPt);
        }

        edgeInfo = append(edgeInfo, {
            "edge"         : edge,
            "face"         : face,
            "faceRefNormal": faceRefNormal,
            "faceCenterPt" : faceCenterPt,
            "p0"           : p0,
            "p1"           : p1,
            "tan0"         : tl0.direction,
            "tan1"         : tl1.direction,
            "fn0"          : fn0,
            "fn1"          : fn1,
            "bn0"          : bn0,
            "bn1"          : bn1,
            "om0"          : om0,
            "om1"          : om1,
            "topPts"       : topPts,
            "bottomPts"    : bottomPts
        });
    }

    // --- Phase 2: corner point computation ---
    // At each shared vertex, accumulate outward binormals from all adjacent edges.
    // The bisector of the accumulated binormals gives a consistent outward direction
    // that is identical for both patches sharing that corner, enabling opBoolean UNION.
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var ed = edgeInfo[ei];

        // Corner at p0: accumulate binormals from all other edges sharing this vertex
        var sumBn0 = ed.bn0;
        for (var ej = 0; ej < size(edgeInfo); ej += 1)
        {
            if (ej == ei || edgeInfo[ej] == undefined) { continue; }
            var eo = edgeInfo[ej];
            if (norm(eo.p0 - ed.p0) < POS_TOL)      { sumBn0 = sumBn0 + eo.bn0; }
            else if (norm(eo.p1 - ed.p0) < POS_TOL) { sumBn0 = sumBn0 + eo.bn1; }
        }
        var bisector0  = normalize(sumBn0);
        var cornerOff0 = ed.p0 + ed.om0 * bisector0;
        var ctStart    = cornerOff0 + wallHeight * ed.fn0;
        var cbStart    = wallSecondDir ? cornerOff0 - wallHeight2 * ed.fn0 : cornerOff0;

        // Corner at p1: accumulate binormals from all other edges sharing this vertex
        var sumBn1 = ed.bn1;
        for (var ej = 0; ej < size(edgeInfo); ej += 1)
        {
            if (ej == ei || edgeInfo[ej] == undefined) { continue; }
            var eo = edgeInfo[ej];
            if (norm(eo.p0 - ed.p1) < POS_TOL)      { sumBn1 = sumBn1 + eo.bn0; }
            else if (norm(eo.p1 - ed.p1) < POS_TOL) { sumBn1 = sumBn1 + eo.bn1; }
        }
        var bisector1  = normalize(sumBn1);
        var cornerOff1 = ed.p1 + ed.om1 * bisector1;
        var ctEnd      = cornerOff1 + wallHeight * ed.fn1;
        var cbEnd      = wallSecondDir ? cornerOff1 - wallHeight2 * ed.fn1 : cornerOff1;

        var edCopy        = edgeInfo[ei];
        edCopy.ctStart    = ctStart;
        edCopy.cbStart    = cbStart;
        edCopy.ctEnd      = ctEnd;
        edCopy.cbEnd      = cbEnd;
        edgeInfo[ei]      = edCopy;
    }

    // --- Phase 3: G1 junction detection (same criterion as buildVariableOffsetCurves) ---
    const G1_COS = cos(0.5 * degree);
    var derivConstraint = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        derivConstraint = append(derivConstraint, {"startDeriv" : undefined, "endDeriv" : undefined});
    }
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

    // --- Phase 4: fit BSplines, loft each edge pair, collect patch bodies ---
    var loftBodyQueries = [];
    for (var ei = 0; ei < size(edgeInfo); ei += 1)
    {
        if (edgeInfo[ei] == undefined) { continue; }
        var ed = edgeInfo[ei];
        var dc = derivConstraint[ei];

        var edgeLen    = evLength(context, {"entities" : ed.edge});
        var derivScale = edgeLen / 3;

        // Snap sample endpoints to computed corner positions before fitting
        var topPts    = ed.topPts;
        var bottomPts = ed.bottomPts;
        topPts[0]                    = ed.ctStart;
        topPts[size(topPts) - 1]     = ed.ctEnd;
        bottomPts[0]                 = ed.cbStart;
        bottomPts[size(bottomPts) - 1] = ed.cbEnd;

        // Fit top BSpline
        var topTargetDef = {"positions" : topPts};
        if (dc.startDeriv != undefined) { topTargetDef = mergeMaps(topTargetDef, {"startDerivative" : dc.startDeriv * derivScale}); }
        if (dc.endDeriv   != undefined) { topTargetDef = mergeMaps(topTargetDef, {"endDerivative"   : dc.endDeriv   * derivScale}); }

        var topSpline;
        try
        {
            var topResults = approximateSpline(context, {
                "isPeriodic"       : false,
                "degree"           : splineDegree,
                "tolerance"        : tolerance,
                "maxControlPoints" : maxCP,
                "targets"          : [approximationTarget(topTargetDef)]
            });
            topSpline = topResults[0];
            var topCPs                  = topSpline.controlPoints;
            topCPs[0]                   = ed.ctStart;
            topCPs[size(topCPs) - 1]    = ed.ctEnd;
            topSpline                   = mergeMaps(topSpline, {"controlPoints" : topCPs});
        }
        catch
        {
            continue;
        }

        // Fit bottom BSpline
        var botTargetDef = {"positions" : bottomPts};
        if (dc.startDeriv != undefined) { botTargetDef = mergeMaps(botTargetDef, {"startDerivative" : dc.startDeriv * derivScale}); }
        if (dc.endDeriv   != undefined) { botTargetDef = mergeMaps(botTargetDef, {"endDerivative"   : dc.endDeriv   * derivScale}); }

        var bottomSpline;
        try
        {
            var botResults = approximateSpline(context, {
                "isPeriodic"       : false,
                "degree"           : splineDegree,
                "tolerance"        : tolerance,
                "maxControlPoints" : maxCP,
                "targets"          : [approximationTarget(botTargetDef)]
            });
            bottomSpline = botResults[0];
            var botCPs                  = bottomSpline.controlPoints;
            botCPs[0]                   = ed.cbStart;
            botCPs[size(botCPs) - 1]    = ed.cbEnd;
            bottomSpline                = mergeMaps(bottomSpline, {"controlPoints" : botCPs});
        }
        catch
        {
            continue;
        }

        // Create curve bodies
        var topCurveId    = id + ("loftTopCurve"    ~ ei);
        var bottomCurveId = id + ("loftBottomCurve" ~ ei);
        try
        {
            opCreateBSplineCurve(context, topCurveId,    {"bSplineCurve" : topSpline});
            opCreateBSplineCurve(context, bottomCurveId, {"bSplineCurve" : bottomSpline});
        }
        catch
        {
            continue;
        }

        var topBody    = qCreatedBy(topCurveId,    EntityType.BODY);
        var bottomBody = qCreatedBy(bottomCurveId, EntityType.BODY);

        // Loft between top and bottom profiles
        var loftId = id + ("loftPatch" ~ ei);
        try
        {
            opLoft(context, loftId, {
                "bodyType"          : ToolBodyType.SURFACE,
                "profileSubqueries" : [topBody, bottomBody]
            });
            loftBodyQueries = append(loftBodyQueries, qCreatedBy(loftId, EntityType.BODY));
        }
        catch
        {
        }

        // Clean up intermediate curve bodies regardless of loft success
        try { opDeleteBodies(context, id + ("deleteLoftCurves" ~ ei), {"entities" : qUnion([topBody, bottomBody])}); }
        catch { }
    }

    // --- Phase 5: union all patches into one body ---
    if (size(loftBodyQueries) > 1)
    {
        try
        {
            opBoolean(context, id + "unionLoftPatches", {
                "operationType" : BooleanOperationType.UNION,
                "tools"         : qUnion(loftBodyQueries)
            });
        }
        catch
        {
        }
    }

    // Name the result
    if (size(loftBodyQueries) > 0)
    {
        setProperty(context, {
            "entities"     : qUnion(loftBodyQueries),
            "propertyType" : PropertyType.NAME,
            "value"        : offsetDef.regionName ~ " loft surface"
        });
    }
}

/**
 * Debug visualization for variable-offset points.
 *
 * offsetDef fields (in addition to the standard offset fields):
 *   showPointFrames  -- draw a frame at each offset point
 *                       RED   = face normal
 *                       BLUE  = edge tangent
 *                       GREEN = binormal (cross of normal x tangent)
 *   showLoftPoints   -- draw points further offset by wallHeight (CYAN)
 *                       and wallHeight2 in the opposite direction (YELLOW) if wallSecondDir
 *   wallHeight       -- ValueWithUnits, distance for loft point (dir 1)
 *   wallSecondDir    -- boolean
 *   wallHeight2      -- ValueWithUnits, distance for loft point (dir 2)
 */
export function debugOffsetPoints(context is Context, id is Id, sheetBody is Query,
    peripheryEdges is Query, offsetDef is map, numPts is number)
{
    if (numPts < 2)
    {
        numPts = 2;
    }
    var edgeArray = evaluateQuery(context, peripheryEdges);
    var regionAxis     = (offsetDef.endFrameOrigin - offsetDef.startFrameOrigin) / meter;
    var regionAxisLen2 = dot(regionAxis, regionAxis);

    for (var ei = 0; ei < size(edgeArray); ei += 1)
    {
        var edge = edgeArray[ei];

        var faceArr = evaluateQuery(context, qIntersection([
            qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE),
            qOwnedByBody(sheetBody, EntityType.FACE)
        ]));
        if (size(faceArr) == 0)
        {
            continue;
        }
        var face = faceArr[0];

        var faceBB = evBox3d(context, {"topology" : face, "tight" : true});
        var faceCenterPt = (faceBB.minCorner + faceBB.maxCorner) / 2;
        var faceCenterUV = evDistance(context, {"side0" : face, "side1" : faceCenterPt}).sides[0].parameter;
        var faceRefNormal = evFaceTangentPlane(context, {"face" : face, "parameter" : faceCenterUV}).normal;
        if (faceRefNormal[2] < 0)
        {
            faceRefNormal = -1 * faceRefNormal;
        }

        var edgeLen  = evLength(context, {"entities" : edge});
        var arrowLen = edgeLen / numPts;
        var arrowRad = arrowLen * 0.05;

        var params = [];
        for (var i = 0; i < numPts; i += 1)
        {
            params = append(params, i / (numPts - 1));
        }
        var tangentLines = evEdgeTangentLines(context, {
            "edge"                      : edge,
            "parameters"                : params,
            "arcLengthParameterization" : true
        });

        for (var i = 0; i < size(tangentLines); i += 1)
        {
            var edgePt      = tangentLines[i].origin;
            var edgeTangent = tangentLines[i].direction;

            var disp = (edgePt - offsetDef.startFrameOrigin) / meter;
            var t    = dot(disp, regionAxis) / regionAxisLen2;
            if (t < 0) { t = 0; }
            if (t > 1) { t = 1; }

            var ptUV       = evDistance(context, {"side0" : face, "side1" : edgePt}).sides[0].parameter;
            var faceNormal = evFaceTangentPlane(context, {"face" : face, "parameter" : ptUV}).normal;
            if (dot(faceNormal, faceRefNormal) < 0)
            {
                faceNormal = -1 * faceNormal;
            }

            var binormal = cross(faceNormal, edgeTangent);
            if (dot(binormal, (faceCenterPt - edgePt) / meter) > 0)
            {
                binormal = -1 * binormal;
            }

            var offsetMag;
            if (offsetDef.offsetType == RegionOffsetType.CONSTANT)
            {
                offsetMag = offsetDef.offset;
            }
            else if (offsetDef.offsetType == RegionOffsetType.LINEAR)
            {
                offsetMag = linearOffset(offsetDef.startOffset, offsetDef.endOffset, t);
            }
            else if (offsetDef.offsetType == RegionOffsetType.QUADRATIC)
            {
                offsetMag = quadraticOffset(offsetDef.startOffset, offsetDef.endOffset, t, offsetDef.zeroSlopeAtStart);
            }
            else
            {
                offsetMag = smoothOffset(offsetDef.startOffset, offsetDef.endOffset, t);
            }

            var offsetPt = edgePt + offsetMag * binormal;

            if (offsetDef.showPointFrames)
            {
                addDebugArrow(context, offsetPt, offsetPt + arrowLen * faceNormal,  arrowRad,        DebugColor.RED);
                addDebugArrow(context, offsetPt, offsetPt + arrowLen * edgeTangent, arrowRad * 0.8,  DebugColor.BLUE);
                addDebugArrow(context, offsetPt, offsetPt + arrowLen * binormal,    arrowRad * 0.8,  DebugColor.GREEN);
            }

            if (offsetDef.showLoftPoints)
            {
                addDebugPoint(context, offsetPt + offsetDef.wallHeight * faceNormal, DebugColor.CYAN);
                if (offsetDef.wallSecondDir == true)
                {
                    addDebugPoint(context, offsetPt - offsetDef.wallHeight2 * faceNormal, DebugColor.YELLOW);
                }
            }
        }
    }
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