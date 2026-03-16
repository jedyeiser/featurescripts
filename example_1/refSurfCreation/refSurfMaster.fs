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

// IMPORT: refSurfUtils.fs

//Testbed for implementing better and more robust 'region' logic for other tools.

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
    // Wrapped in try/catch so editing logic always returns a valid definition
    // even if detection fails (ensures definition.intersections is always an array).
    try
    {
        // Collect sortable region entries
        var sortable = [];
        for (var r = 0; r < size(definition.regions); r += 1)
        {
            var reg = definition.regions[r];
            if (reg.extentDef == RegionExtentDef.ALONG_REF &&
                reg.startX != undefined && reg.endX != undefined)
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

            var ixEntry;
            if (existing != undefined)
            {
                // Preserve user data; refresh auto fields only
                ixEntry                  = existing;
                ixEntry.intersectionNum  = i;
                ixEntry.regionAName      = regA.name;
                ixEntry.regionBName      = regB.name;
            }
            else
            {
                ixEntry = {
                    "intersectionNum"  : i,
                    "name"             : regA.name ~ " / " ~ regB.name,
                    "regionAName"      : regA.name,
                    "regionBName"      : regB.name,
                    "join"             : false,
                    "startContinuity"  : IntersectionContinuityType.G0,
                    "endContinuity"    : IntersectionContinuityType.G0,
                    "joinStartOffset"  : 0 * millimeter,
                    "joinEndOffset"    : 0 * millimeter
                };
            }
            newIntersections = append(newIntersections, ixEntry);
        }

        definition.intersections = newIntersections;
    }
    catch
    {
        // Ensure intersections is always a valid array even if detection fails
        if (definition.intersections == undefined)
        {
            definition.intersections = [];
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

        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#name", "Collapsed By Default" : true }
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
                     "Item label template" : "#name",
                     "Collapsed By Default" : true,
                     "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
        definition.intersections is array;
        for (var ix in definition.intersections)
        {
            annotation { "Name" : "Intersection number", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(ix.intersectionNum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);

            annotation { "Name" : "Name" }
            ix.name is string;

            annotation { "Name" : "Region A", "UIHint" : UIHint.READ_ONLY }
            ix.regionAName is string;

            annotation { "Name" : "Region B", "UIHint" : UIHint.READ_ONLY }
            ix.regionBName is string;

            annotation { "Name" : "Join", "Default" : false }
            ix.join is boolean;

            if (ix.join)
            {
                annotation { "Name" : "Start continuity", "Default" : IntersectionContinuityType.G0 }
                ix.startContinuity is IntersectionContinuityType;

                annotation { "Name" : "End continuity", "Default" : IntersectionContinuityType.G0 }
                ix.endContinuity is IntersectionContinuityType;

                annotation { "Name" : "Start offset" }
                isLength(ix.joinStartOffset, INTERSECTION_OFFSET_BOUNDS);

                annotation { "Name" : "End offset" }
                isLength(ix.joinEndOffset, INTERSECTION_OFFSET_BOUNDS);
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
            var midpoint = (region.startFrame.origin + region.endFrame.origin) / 2;
            var midpointPlane = plane(midpoint, normalize((region.startFrame.origin - region.endFrame.origin) / millimeter));
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

            var reg = definition.regions[r];
            var isConstant = reg.offsetType == RegionOffsetType.CONSTANT;
            var zeroAtStart = (reg.offsetType == RegionOffsetType.QUADRATIC) ? reg.zeroSlopeAtStart : true;
            var offsetDef = {
                "startFrameOrigin" : region.startFrame.origin,
                "endFrameOrigin"   : region.endFrame.origin,
                "offsetType"       : reg.offsetType,
                "offset"           : isConstant ? reg.offset : (0 * millimeter),
                "startOffset"      : reg.startOffset,
                "endOffset"        : reg.endOffset,
                "zeroSlopeAtStart" : zeroAtStart,
                "regionName"       : reg.name
            };

            if (definition.returnOffsetWires)
            {
                buildVariableOffsetCurves(context, id + ("varOffset" ~ r), regionCopy, peripheryEdges, offsetDef,
                    definition.samplingDensity, definition.approxDegree, definition.approxTolerance, definition.approxMaxCP);
            }

            if (definition.returnLoftSurface)
            {
                buildLoftSurfaces(context, id + ("loftSurf" ~ r), regionCopy, peripheryEdges, offsetDef,
                    definition.samplingDensity, definition.approxDegree, definition.approxTolerance, definition.approxMaxCP,
                    definition.wallHeight,
                    definition.wallSecondDir,
                    definition.wallSecondDir ? definition.wallHeight2 : (0 * millimeter));
            }

            if (definition.debug && (definition.showPointFrames || definition.showLoftPoints))
            {
                var dbgHasLoft = definition.returnLoftSurface;
                debugOffsetPoints(context, id + ("debugOffsetPts" ~ r), regionCopy, peripheryEdges, mergeMaps(offsetDef, {
                    "showPointFrames" : definition.showPointFrames,
                    "showLoftPoints"  : definition.showLoftPoints && dbgHasLoft,
                    "wallHeight"      : (definition.showLoftPoints && dbgHasLoft) ? definition.wallHeight : (0 * millimeter),
                    "wallSecondDir"   : definition.showLoftPoints && dbgHasLoft && definition.wallSecondDir,
                    "wallHeight2"     : (definition.showLoftPoints && dbgHasLoft && definition.wallSecondDir) ? definition.wallHeight2 : (0 * millimeter)
                }), definition.samplingDensity);
            }

            if (definition.debug && definition.showOffsetSamples)
            {
                sampleEdgeOffsets(context, id + ("sampleOffsets" ~ r), regionCopy, peripheryEdges, offsetDef, definition.samplingDensity);
            }

            var faceFrames = processFaceFrames(context, id + ("processRef" ~ r ~ "test"), regionCopy, peripheryEdges, {
                "samplingDensity" : definition.samplingDensity,
                "samplingType"    : definition.samplingType,
                "showFrames"      : definition.debug && definition.showVertexFrames
            }, refPath);
        }
    });
