FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/extend.fs", version : "3083.0");
import(path : "onshape/std/ruledSurface.fs", version : "3083.0");

//import pathProcessing
import(path : "e9dd34f07820388a202cb620", version : "b069b8000abb267262c7f1cf");

//import regionProcessing
import(path : "d1cf8af3d05964b44c3ab4c0", version : "994680b362833bb531363b1b");

//export import refSurfCore
export import(path : "828cc4108f1c8683bc0e59cf", version : "c491879986efec302af2e276");

// IMPORT: refSurfUtils.fs
import(path : "d41884a96244793beb462449", version : "20f7377fd8cb8b11ad2de099");


//Testbed for implementing better and more robust 'region' logic for other tools.

export function variableSurfaceOffsetEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    // --- Region defaults --
    
    if (size(definition.regions) > 0)
    {
        var sortable = [];
        var unsortable = [];
        // Editing logic uses a canonical (unflipped) path so region order/numbering
        // is invariant to flipDirection. The executor still honours flipDirection
        // when building the geometric refPath.
        var processedPath = processPath(context, id + "editingLogic", {'userSelection' : definition.refWire, "flipDirection" : false, "referencePoint" : definition.refPoint, 'numPoints' : max(20, definition.samplingDensity)});

        for (var r = 0; r < size(definition.regions); r += 1)
        {
            var reg = definition.regions[r];
            // Initialize fields added after initial feature creation to prevent precondition failures.
            if (reg.singleCurve  == undefined) { reg = mergeMaps(reg, { "singleCurve"  : false }); }
            if (reg.debugRegion  == undefined) { reg = mergeMaps(reg, { "debugRegion"  : false }); }
            if (reg.extentDef == RegionExtentDef.ALONG_REF && reg.startX != undefined && reg.endX != undefined)
            {
                sortable = append(sortable, reg);
            }
            else if (reg.extentDef == RegionExtentDef.QUERY)
            {
                var extents = queryRegionExtents(context, processedPath, reg.startPoint, reg.endPoint);
                reg.startX = extents.startX;
                reg.endX   = extents.endX;
                sortable = append(sortable, reg);
                
            }
            else
            {
                unsortable = append(unsortable, reg);
            }
        }
        
        sortable = sort(sortable, function(a, b) {return (a.startX + a.endX)/2 - (b.startX + b.endX)/2;});
        var sizeCounter = 0;
        var newRegions = [];
        for (var i = 0; i < size(sortable); i += 1)
        {
            var region = sortable[i];
            region.regionNum = sizeCounter;
            if (length(region.name) == 0 || region.name == undefined)
            {
                region.needsDefaultName = true;   
            }
            else
            {
                region.needsDefaultName = startsWith(region.name, "Region") ? true : false;
            }
            
            if (region.needsDefaultName)
            {
                region.name = "Region " ~ sizeCounter;
            }
            newRegions = append(newRegions, region);
            sizeCounter += 1;
        }
        for (var i = 0; i < size(unsortable); i += 1)
        {
            var region = unsortable[i];
            region.regionNum = sizeCounter;
            if (length(region.name) == 0 || region.name == undefined)
            {
                region.needsDefaultName = true;   
            }
            else
            {
                region.needsDefaultName = false;
            }
            
            if (region.needsDefaultName)
            {
                region.name = "Region " ~ sizeCounter;
            }
            newRegions = append(newRegions, region);
            sizeCounter += 1;
        }
        
        definition.regions = newRegions;
        var oldIntersections = definition.intersections;
        var newIntersections = [];
        for (var i = 0; i < size(newRegions) - 1; i += 1)
        {
            var intersection = (i < size(oldIntersections)) ? oldIntersections[i] : {
                "intersectionNum"       : i,
                "intersectionName"      : "Intersection " ~ (i + 1),
                "needsIntersectionName" : true,
                "join"                  : false,
                "startContinuity"       : IntersectionContinuityType.G0,
                "endContinuity"         : IntersectionContinuityType.G0,
                "joinStartOffset"       : 0 * millimeter,
                "joinEndOffset"         : 0 * millimeter
            };
            intersection.intersectionNum = i;
            intersection.needsIntersectionName = (length(intersection.intersectionName) == 0 || intersection.intersectionName == undefined || startsWith(intersection.intersectionName, "Intersection "));
            if (intersection.needsIntersectionName)
            {
                intersection.intersectionName = "Intersection " ~ (i + 1);
            }
            
            intersection.regionANum = i;
            intersection.regionBNum = i + 1;
            
            intersection.regionAName = newRegions[i].name;
            intersection.regionBName = newRegions[i+1].name;
            
            newIntersections = append(newIntersections, intersection);
        }
        
        definition.intersections = newIntersections;
    }
    else
    {
        definition.intersections =[];
    }

    

    return definition;
}

annotation { "Feature Type Name" : "Variable surface offset", "Feature Type Description" : "", "Editing Logic Function" : "variableSurfaceOffsetEditingLogic" }
export const variableSurfaceOffset = defineFeature(function(context is Context, id is Id, definition is map)
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

        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#name", "Collapsed By Default" : true, "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "regionNum", "UIHint" : UIHint.ALWAYS_HIDDEN } // use to default name
            isInteger(region.regionNum, RegionNumBounds);
            
            annotation { "Name" : "Extents from", "Default" : RegionExtentDef.ALONG_REF, "UIHint" : UIHint.HORIZONTAL_ENUM }
            region.extentDef is RegionExtentDef;
            
            annotation { "Name" : "Name" }
            region.name is string;
            
            annotation { "Name" : "needsDefaultName", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            region.needsDefaultName is boolean;
            
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

            annotation { "Name" : "Single curve", "Default" : false }
            region.singleCurve is boolean;

            annotation { "Name" : "Debug this region", "Default" : false }
            region.debugRegion is boolean;

            if (region.debugRegion)
            {
                annotation { "Name" : "Log normals/binormals", "Default" : false }
                region.logNormals is boolean;

                annotation { "Name" : "Log spline metadata", "Default" : false }
                region.logSplineMeta is boolean;

                annotation { "Name" : "Show offset samples", "Default" : false }
                region.showRegionSamples is boolean;

                annotation { "Name" : "Show binormal arrows", "Default" : false }
                region.showRegionBinormals is boolean;
            }

        }

        annotation { "Name" : "Intersections", "Item name" : "Intersection", "Item label template" : "#intersectionName", "Collapsed By Default" : true, "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS] }
        definition.intersections is array;
        for (var ix in definition.intersections)
        {
            annotation { "Name" : "Intersection number", "UIHint" : UIHint.ALWAYS_HIDDEN}
            isInteger(ix.intersectionNum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);

            annotation { "Name" : "Name" }
            ix.intersectionName is string;
            
            annotation { "Name" : "needsDefaultName", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
            ix.needsIntersectionName is boolean;
            
            annotation { "Name" : "RegionANum", "UIHint" : UIHint.ALWAYS_HIDDEN}
            isInteger(ix.regionANum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);
            
            annotation { "Name" : "Region A", "UIHint" : UIHint.READ_ONLY }
            ix.regionAName is string;
            
            annotation { "Name" : "RegionBNum", "UIHint" : UIHint.ALWAYS_HIDDEN}
            isInteger(ix.regionBNum, { (unitless) : [0, 0, 100] } as IntegerBoundSpec);
            
            annotation { "Name" : "Region B", "UIHint" : UIHint.READ_ONLY }
            ix.regionBName is string;

            annotation { "Name" : "Join", "Default" : false }
            ix.join is boolean;

            if (ix.join)
            {
                annotation { "Name" : "Start continuity", "Default" : IntersectionContinuityType.G0, "UIHint" : UIHint.SHOW_LABEL }
                ix.startContinuity is IntersectionContinuityType;

                annotation { "Name" : "End continuity", "Default" : IntersectionContinuityType.G0, "UIHint" : UIHint.SHOW_LABEL }
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
            var startPl = plane(region.startFrame.origin, region.startFrame.zAxis);
            var endPl = plane(region.endFrame.origin, region.endFrame.zAxis);

            var startTrims = qIntersectsPlane(regionCopy, startPl);
            var endTrims = qIntersectsPlane(regionCopy, endPl);
            //split if intersected
            if (!isQueryEmpty(context, startTrims))
            {
                opSplitPart(context, id + ("region" ~ r ~ "startSplit"), {
                        "targets" : qUnion([startTrims]),
                        "tool" : startPl
                });
            }
            if (!isQueryEmpty(context, endTrims))
            {
                opSplitPart(context, id + ("region" ~ r ~ "endSplit"), {
                        "targets" : qUnion([endTrims]),
                        "tool" : endPl
                });
            }
            //delete appropriate body
            var midpoint = (region.startFrame.origin + region.endFrame.origin) / 2;
            var midPl = plane(midpoint, normalize((region.startFrame.origin - region.endFrame.origin) / millimeter));
            var deleteBodies = qSubtraction(regionCopy, qIntersectsPlane(regionCopy, midPl));

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
            var boundaryPlaneEdges = qUnion([
                qCoincidesWithPlane(qOwnedByBody(regionCopy, EntityType.EDGE), startPl),
                qCoincidesWithPlane(qOwnedByBody(regionCopy, EntityType.EDGE), endPl)
            ]);
            var peripheryEdges = qEdgeTopologyFilter(qSubtraction(qOwnedByBody(regionCopy, EntityType.EDGE), qUnion([splitEdges, boundaryPlaneEdges])), EdgeTopology.ONE_SIDED);

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
                "regionName"       : reg.name,
                "singleCurve"      : reg.singleCurve == true
            };

            var isRegionDebug = reg.debugRegion == true;
            var debugDef = {
                "logNormals"    : isRegionDebug && reg.logNormals    == true,
                "logSplineMeta" : isRegionDebug && reg.logSplineMeta == true
            };

            if (definition.returnOffsetWires)
            {
                var wireBodyQ = buildVariableOffsetCurves(context, id + ("varOffset" ~ r), regionCopy, peripheryEdges, offsetDef,
                    definition.samplingDensity, definition.approxDegree, definition.approxTolerance, definition.approxMaxCP,
                    debugDef);
                processedRegions[r] = mergeMaps(processedRegions[r], { "wireBodyQuery" : wireBodyQ });
            }

            if (definition.returnLoftSurface)
            {
                var loftBodyQ = buildLoftSurfaces(context, id + ("loftSurf" ~ r), regionCopy, peripheryEdges, offsetDef,
                    definition.samplingDensity, definition.approxDegree, definition.approxTolerance, definition.approxMaxCP,
                    definition.wallHeight,
                    definition.wallSecondDir,
                    definition.wallSecondDir ? definition.wallHeight2 : (0 * millimeter),
                    debugDef);
                processedRegions[r] = mergeMaps(processedRegions[r], { "loftBodyQuery" : loftBodyQ });
            }

            // Global debug visualizations (all regions)
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

            // Per-region debug visualizations
            if (isRegionDebug && reg.showRegionSamples == true)
            {
                sampleEdgeOffsets(context, id + ("regDbgSamp" ~ r), regionCopy, peripheryEdges, offsetDef, definition.samplingDensity);
            }

            if (isRegionDebug && reg.showRegionBinormals == true)
            {
                debugOffsetPoints(context, id + ("regDbgBin" ~ r), regionCopy, peripheryEdges, mergeMaps(offsetDef, {
                    "showPointFrames" : true,
                    "showLoftPoints"  : false
                }), definition.samplingDensity);
            }

            if (definition.debug && definition.showVertexFrames)
            {
                processFaceFrames(context, id + ("processRef" ~ r ~ "test"), regionCopy, peripheryEdges, {
                    "samplingDensity" : definition.samplingDensity,
                    "samplingType"    : definition.samplingType,
                    "showFrames"      : true
                }, refPath);
            }

            // Reference surface copy has served its purpose -- delete it so it
            // does not participate in the final surface boolean assembly.
            opDeleteBodies(context, id + ("deleteRegionCopy" ~ r), { "entities" : regionCopy });
        }

        // refBottomSurf was only needed as the opPattern source for region copies.
        // Delete it now so it is not included in the final boolean assembly.
        opDeleteBodies(context, id + "deleteRefBottomSurf", { "entities" : refGeo.refBottomSurf });

        if (size(definition.intersections) > 0 &&
            (definition.returnLoftSurface || definition.returnOffsetWires))
        {
            buildIntersectionJoins(context, id, definition, processedRegions);
        }
    });
