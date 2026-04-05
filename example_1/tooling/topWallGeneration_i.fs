FeatureScript 2931;
import(path : "onshape/std/common.fs", version : "2931.0");
import(path : "onshape/std/bridgingCurve.fs", version : "2931.0");

// import pathProcessing
import(path : "e9dd34f07820388a202cb620", version : "0837de9a9d2e74ba57457e77");
// IMPORT: tools/bspline_data.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");

// IMPORT: tools/solvers.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");

// --- Constants

const CAVITY_DEPTH_TOL = 0.001 * millimeter;
const MIN_CURVE_CD = 0.05 * millimeter;
const BSEARCH_PARAM_TOL = 1e-9;
const BSEARCH_MAX_ITER = 30;
const YAXIS_PROBE_DIST = 1 * millimeter;
const PINCH_PT_SAMPLES = 50;
const MAX_GREVILLE_SAMPLES = 15;

// --- Bounds

export const WallAngleBounds_i             = {(degree)     : [0, 15, 89]}    as AngleBoundSpec;
export const PinchRadiusBounds_i           = {(millimeter) : [0.5, 2, 10]}   as LengthBoundSpec;
export const TopRadiusBounds_i             = {(millimeter) : [0.5, 3, 20]}   as LengthBoundSpec;
export const ContinuityOffsetBounds_i      = {(millimeter) : [0, 5, 50]}     as LengthBoundSpec;
export const DebugStepBounds_i             = {(unitless)   : [0, 0, 20]}     as IntegerBoundSpec;
export const PinchOffsetBounds_i           = {(millimeter) : [-3, 0, 3]}     as LengthBoundSpec;
export const PointNumBounds_i              = {(unitless)   : [0, 0, 500]}    as IntegerBoundSpec;
export const ApproxToleranceBounds_i       = {(millimeter) : [0.001, 0.01, 1]} as LengthBoundSpec;
export const ApproxDegreeBounds_i          = {(unitless)   : [2, 3, 5]}      as IntegerBoundSpec;
export const ApproxMaxCPBounds_i           = {(unitless)   : [10, 100, 500]} as IntegerBoundSpec;
export const ControlPointMultiplierBounds_i = {(unitless)  : [2, 5, 10]}     as IntegerBoundSpec;
export const DistanceSamplingBounds_i      = {(millimeter) : [1, 10, 50]}    as LengthBoundSpec;

export const DefaultRegion_i = {"regionNum" : 0, "startPoint" : "", "endPoint" : "", "transitionType" : RegionTransitionType_i.SMOOTH, "regionName" : ""};

// --- Enums

export enum SamplingType_i
{
    CONTROL_POINT,
    DISTANCE
}

export enum PointWallAngleDefinition_i
{
    CONTINUITY,
    RADIUS_ANGLE
}

export enum PointContinuityType_i
{
    G0,
    G1,
    G2
}

export enum PointDefinitionType_i
{
    DIST_FROM_REF,
    QUERY
}

export enum RegionTransitionType_i
{
    LINEAR,
    SMOOTH
}

export enum CDRegionBoundingType_i
{
    CONTAINED,
    SPLIT,
    PARTIAL
}

// --- Editing Logic

export function topWallGeneration_iEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenBodies is Query) returns map
{
    var processedPath = processPath(context, id + "processPath", {
        "userSelection"  : definition.refWire,
        "flipDirection"  : false,
        "referencePoint" : definition.zeroPoint,
        "numPoints"      : 20
    });

    for (var point in definition.wallPoints)
    {
        if (point.locType == PointDefinitionType_i.QUERY)
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

    var oldRegions = definition.regions;
    var regions    = [];

    for (var i = 1; i < size(definition.wallPoints); i += 1)
    {
        var startPoint = definition.wallPoints[i - 1];
        var endPoint   = definition.wallPoints[i];
        var oldRegion  = size(oldRegions) > i - 1 ? oldRegions[i - 1] : DefaultRegion_i;

        oldRegion = mergeMaps(oldRegion, {"regionNum" : i - 1});
        oldRegion = mergeMaps(oldRegion, {"startPoint" : startPoint.name, "endPoint" : endPoint.name});

        if (length(oldRegion.regionName) == 0 || startsWith(oldRegion.regionName, "Region #"))
        {
            oldRegion.regionName = "Region # " ~ (i - 1);
        }

        regions = append(regions, oldRegion);
    }
    definition.regions = regions;

    var stepNames = [
        "0: Setup geometry",
        "1: Preprocess points",
        "2: Preprocess regions",
        "3: Greville frames",
        "4: Zero crossings",
        "5: MinCD transitions",
        "6: Build curves"
    ];
    var stepIdx = definition.debugStepNum;
    definition.debugStepDesc = (stepIdx >= 0 && stepIdx < size(stepNames)) ? stepNames[stepIdx] : "";

    return definition;
}

// --- Feature

annotation { "Feature Type Name" : "Top Wall Generation I", "Feature Type Description" : "Improved top wall surface generation using parallel transport frames and Greville sampling", "Editing Logic Function" : "topWallGeneration_iEditingLogic" }
export const topWallGeneration_i = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Wall definition type", "Default" : PointWallAngleDefinition_i.RADIUS_ANGLE, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.definitionType is PointWallAngleDefinition_i;

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

        annotation { "Name" : "Footprint surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.footprintSurf is Query;

        annotation { "Name" : "Bottom surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1 }
        definition.bottomSurf is Query;

        annotation { "Name" : "Wall points", "Item name" : "Wall point", "Item label template" : "#name", "UIHint" : [UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.PREVENT_ARRAY_REORDER] }
        definition.wallPoints is array;
        for (var wallPoint in definition.wallPoints)
        {
            annotation { "Name" : "Location type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : PointDefinitionType_i.DIST_FROM_REF }
            wallPoint.locType is PointDefinitionType_i;

            annotation { "Name" : "pointNumber", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(wallPoint.pointNum, PointNumBounds_i);

            if (wallPoint.locType == PointDefinitionType_i.DIST_FROM_REF)
            {
                annotation { "Name" : "Point from ref" }
                isLength(wallPoint.pointFromRef, LENGTH_BOUNDS);
            }
            else if (wallPoint.locType == PointDefinitionType_i.QUERY)
            {
                annotation { "Name" : "Location point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                wallPoint.locPoint is Query;
            }

            annotation { "Name" : "Name" }
            wallPoint.name is string;

            annotation { "Name" : "Pinch offset" }
            isLength(wallPoint.pinchOffset, PinchOffsetBounds_i);

            if (definition.definitionType == PointWallAngleDefinition_i.RADIUS_ANGLE)
            {
                annotation { "Name" : "Pinch radius" }
                isLength(wallPoint.pinchRadius, PinchRadiusBounds_i);

                annotation { "Name" : "Top radius" }
                isLength(wallPoint.topRadius, TopRadiusBounds_i);

                annotation { "Name" : "Wall angle" }
                isAngle(wallPoint.wallAngle, WallAngleBounds_i);
            }
            else if (definition.definitionType == PointWallAngleDefinition_i.CONTINUITY)
            {
                annotation { "Name" : "Continuity at pinch", "Default" : PointContinuityType_i.G1 }
                wallPoint.pinchContinuity is PointContinuityType_i;

                annotation { "Name" : "Continuity at top", "Default" : PointContinuityType_i.G1 }
                wallPoint.topContinuity is PointContinuityType_i;

                annotation { "Name" : "Offset" }
                isLength(wallPoint.offset, ContinuityOffsetBounds_i);
            }
        }

        annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#regionName", "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS] }
        definition.regions is array;
        for (var region in definition.regions)
        {
            annotation { "Name" : "regionNum", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(region.regionNum, PointNumBounds_i);

            annotation { "Name" : "Region Name" }
            region.regionName is string;

            annotation { "Name" : "Start point" }
            region.startPoint is string;

            annotation { "Name" : "End point" }
            region.endPoint is string;

            annotation { "Name" : "Transition type", "Default" : RegionTransitionType_i.SMOOTH, "UIHint" : UIHint.SHOW_LABEL }
            region.transitionType is RegionTransitionType_i;
        }

        annotation { "Group Name" : "Sampling & Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approximationTol, ApproxToleranceBounds_i);

            annotation { "Name" : "Max control points" }
            isInteger(definition.maxCPs, ApproxMaxCPBounds_i);

            annotation { "Name" : "Keep degree?", "Default" : true }
            definition.keepDegree is boolean;

            if (!definition.keepDegree)
            {
                annotation { "Name" : "Degree" }
                isInteger(definition.approxDegree, ApproxDegreeBounds_i);
            }
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Step through?", "Default" : false }
            definition.debugStepThrough is boolean;

            if (definition.debugStepThrough)
            {
                annotation { "Name" : "Debug step" }
                isInteger(definition.debugStepNum, DebugStepBounds_i);

                annotation { "Name" : "Step", "UIHint" : UIHint.READ_ONLY }
                definition.debugStepDesc is string;
            }

            annotation { "Name" : "Show ref frames" }
            definition.showRefFrames is boolean;

            annotation { "Name" : "Show point frames" }
            definition.showPointFrames is boolean;

            annotation { "Name" : "Show curve points", "Default" : false }
            definition.showCurvePoints is boolean;

            if (definition.showCurvePoints)
            {
                annotation { "Name" : "Pinch (green)", "Default" : true }
                definition.showPinchPts is boolean;

                annotation { "Name" : "Top (magenta)", "Default" : true }
                definition.showTopPts is boolean;

                annotation { "Name" : "Mid (blue)", "Default" : false }
                definition.showMidPts is boolean;

                annotation { "Name" : "Top edge bottom (cyan)", "Default" : false }
                definition.showTopEdgeBottomPts is boolean;

                annotation { "Name" : "Pinch top (red)", "Default" : false }
                definition.showPinchTopPts is boolean;
            }

            annotation { "Name" : "Show sample frames", "Default" : false }
            definition.showSampleFrames is boolean;

            annotation { "Name" : "Log sampling", "Default" : false }
            definition.logSampling is boolean;
        }
    }
    {
        // --- Step 0: Setup geometry
        var processedPath = processPath(context, id + "processPath", {
            "userSelection"  : definition.refWire,
            "flipDirection"  : false,
            "referencePoint" : definition.zeroPoint,
            "numPoints"      : 20
        });

        if (definition.showRefFrames)
        {
            showRefFrames(context, processedPath, false, false);
        }

        var geomSetup = setupGeometry(context, id + "setup", definition);
        var pinchWire = geomSetup.pinchWire;
        var cdSurfs   = geomSetup.cdSurfs;
        var topCopy   = geomSetup.topCopy;
        var cdCopy    = geomSetup.cdCopy;

        if (definition.debugStepThrough && definition.debugStepNum == 0)
        {
            return;
        }

        // --- Step 1: Preprocess points
        const preprocessedPoints = preprocessPoints_i(context, definition.wallPoints, processedPath, pinchWire);
        definition.wallPoints = preprocessedPoints;

        if (definition.showPointFrames)
        {
            for (var point in preprocessedPoints)
            {
                var arrowDist = 20 * millimeter;
                var refFrame  = point.pointRefFrame.frame;
                addDebugArrow(context, refFrame.origin, refFrame.origin + yAxis(refFrame) * arrowDist, 3 * millimeter, DebugColor.GREEN);
                addDebugArrow(context, refFrame.origin, refFrame.origin + refFrame.xAxis * arrowDist, 3 * millimeter, DebugColor.RED);
                addDebugArrow(context, refFrame.origin, refFrame.origin + refFrame.zAxis * arrowDist, 3 * millimeter, DebugColor.BLUE);
            }
        }

        if (definition.debugStepThrough && definition.debugStepNum == 1)
        {
            return;
        }

        // --- Step 2: Build pinch PT table (once) + preprocess regions
        const processedPinchWire = processPinchWire(context, id + "pinchPT", pinchWire, processedPath, PINCH_PT_SAMPLES);

        var approxDef = {
            "tolerance"       : definition.approximationTol,
            "maxControlPoints" : definition.maxCPs,
            "degree"          : definition.keepDegree ? 3 : definition.approxDegree
        };

        const preprocessedRegions = preprocessRegions_i(context, id + "regions", processedPath, preprocessedPoints, definition.regions, pinchWire, processedPinchWire);

        if (definition.debugStepThrough && definition.debugStepNum == 2)
        {
            return;
        }

        // --- Steps 3-6: Per-region processing
        for (var ri = 0; ri < size(preprocessedRegions); ri += 1)
        {
            var regionData  = preprocessedRegions[ri];
            var startPoint  = regionData.startPoint;
            var endPoint    = regionData.endPoint;
            var transitionMap = regionData.transitionMap;
            var regionWire  = regionData.regionWire;

            var regionWireBodies = evaluateQuery(context, regionWire);

            for (var b = 0; b < size(regionWireBodies); b += 1)
            {
                var wireBody  = regionWireBodies[b];
                var bodyEdges = evaluateQuery(context, qOwnedByBody(wireBody, EntityType.EDGE));

                for (var e = 0; e < size(bodyEdges); e += 1)
                {
                    var edge     = bodyEdges[e];
                    var bsCurve  = evApproximateBSplineCurve(context, { "edge" : edge });

                    // Skip only LINEAR (degree-1) edges that lie in a boundary plane.
                    // Curved edges can never be colinear with a plane, so the filter
                    // is restricted to degree-1 edges to avoid discarding valid curved edges.
                    if (getDegree(bsCurve) == 1 &&
                        (edgeLiesInPlane(context, edge, startPoint.pointRefPlane) ||
                         edgeLiesInPlane(context, edge, endPoint.pointRefPlane)))
                    {
                        continue;
                    }

                    // Step 3: Build edge point maps via Greville sampling
                    var edgeResult = processRegionEdge(context, id + ("edge_" ~ ri ~ "_" ~ b ~ "_" ~ e),
                        edge, bsCurve, processedPinchWire, cdSurfs, topCopy, cdCopy,
                        startPoint, endPoint, transitionMap, definition.definitionType);

                    if (definition.logSampling)
                    {
                        println("edge r" ~ ri ~ " b" ~ b ~ " e" ~ e ~ ": nPts=" ~ size(edgeResult.edgePointMaps));
                        for (var ptMap in edgeResult.edgePointMaps)
                        {
                            println("  sp=" ~ ptMap.spanParam ~ " cd=" ~ ptMap.cavityDepth / millimeter ~ "mm");
                        }
                    }

                    if (definition.showSampleFrames)
                    {
                        var arrowLen = 5 * millimeter;
                        for (var ptMap in edgeResult.edgePointMaps)
                        {
                            var o = ptMap.origin;
                            addDebugArrow(context, o, o + ptMap.xAxis * arrowLen,  1 * millimeter, DebugColor.RED);
                            addDebugArrow(context, o, o + ptMap.yAxis * arrowLen,  1 * millimeter, DebugColor.GREEN);
                            addDebugArrow(context, o, o + ptMap.zAxis * arrowLen,  1 * millimeter, DebugColor.BLUE);
                        }
                    }

                    if (definition.debugStepThrough && definition.debugStepNum == 3)
                    {
                        continue;
                    }

                    // Step 4: Filter zero cavity depth, insert zero crossings
                    var subRegions = filterAndInsertZeroCrossings(context,
                        edgeResult.edgePointMaps, edge, edgeResult.bsCurve,
                        processedPinchWire, cdSurfs, topCopy, cdCopy,
                        startPoint, endPoint, transitionMap, definition.definitionType);

                    if (definition.logSampling)
                    {
                        println("  subRegions=" ~ size(subRegions));
                        for (var sr = 0; sr < size(subRegions); sr += 1)
                        {
                            println("    sr" ~ sr ~ ": nPts=" ~ size(subRegions[sr]));
                            for (var ptMap in subRegions[sr])
                            {
                                var cp = ptMap.curvePoints;
                                if (cp != undefined && cp.pinchPoint != undefined && cp.topPoint != undefined)
                                {
                                    println("      sp=" ~ ptMap.spanParam
                                        ~ " pinch=" ~ cp.pinchPoint / millimeter
                                        ~ " top=" ~ cp.topPoint / millimeter);
                                }
                            }
                        }
                    }

                    if (definition.debugStepThrough && definition.debugStepNum == 4)
                    {
                        continue;
                    }

                    // Step 5: Insert minCD transitions (RADIUS_ANGLE only)
                    if (definition.definitionType == PointWallAngleDefinition_i.RADIUS_ANGLE)
                    {
                        var augmentedSubRegions = [];
                        for (var sr = 0; sr < size(subRegions); sr += 1)
                        {
                            augmentedSubRegions = append(augmentedSubRegions,
                                insertMinCDTransitions(context, subRegions[sr], edge, edgeResult.bsCurve,
                                    processedPinchWire, cdSurfs, topCopy, cdCopy, startPoint, endPoint, transitionMap));
                        }
                        subRegions = augmentedSubRegions;
                    }

                    // Debug: draw computed curve points
                    if (definition.showCurvePoints)
                    {
                        for (var sr = 0; sr < size(subRegions); sr += 1)
                        {
                            for (var ptMap in subRegions[sr])
                            {
                                var cp = ptMap.curvePoints;
                                if (definition.showPinchPts && cp.pinchPoint != undefined)
                                {
                                    addDebugPoint(context, cp.pinchPoint, DebugColor.GREEN);
                                }
                                if (definition.showTopPts && cp.topPoint != undefined)
                                {
                                    addDebugPoint(context, cp.topPoint, DebugColor.MAGENTA);
                                }
                                if (definition.showMidPts && cp.midPoint != undefined)
                                {
                                    addDebugPoint(context, cp.midPoint, DebugColor.BLUE);
                                }
                                if (definition.showTopEdgeBottomPts && cp.topEdgeBottom != undefined)
                                {
                                    addDebugPoint(context, cp.topEdgeBottom, DebugColor.CYAN);
                                }
                                if (definition.showPinchTopPts && cp.pinchTop != undefined)
                                {
                                    addDebugPoint(context, cp.pinchTop, DebugColor.RED);
                                }
                            }
                        }
                    }

                    if (definition.debugStepThrough && definition.debugStepNum == 5)
                    {
                        continue;
                    }

                    // Step 6: Build curves for each sub-region
                    for (var sr = 0; sr < size(subRegions); sr += 1)
                    {
                        if (size(subRegions[sr]) < 2)
                        {
                            continue;
                        }

                        // Sort sub-region by spanParam (insertion sort).
                        // Edges traversed in reverse produce decreasing spanParams;
                        // sorting ensures approximateSpline receives monotonic parameters.
                        var sortedSR = subRegions[sr];
                        for (var si = 1; si < size(sortedSR); si += 1)
                        {
                            var key = sortedSR[si];
                            var ji  = si - 1;
                            while (ji >= 0 && sortedSR[ji].spanParam > key.spanParam)
                            {
                                sortedSR[ji + 1] = sortedSR[ji];
                                ji -= 1;
                            }
                            sortedSR[ji + 1] = key;
                        }

                        // Deduplicate points with identical spanParams.
                        // Duplicate spanParams cause approximateSpline to receive non-strictly-monotonic
                        // parameters, which causes opCreateBSplineCurve BAD_GEOMETRY.
                        var dedupedSR = [sortedSR[0]];
                        for (var di = 1; di < size(sortedSR); di += 1)
                        {
                            if (sortedSR[di].spanParam - dedupedSR[size(dedupedSR) - 1].spanParam > 1e-9)
                            {
                                dedupedSR = append(dedupedSR, sortedSR[di]);
                            }
                        }
                        sortedSR = dedupedSR;

                        if (size(sortedSR) < approxDef.degree + 1)
                        {
                            continue;
                        }

                        // Skip sub-regions where max cavity depth is negligible.
                        // Avoids degenerate (zero-length) splines from opCreateBSplineCurve.
                        var maxCD = 0 * millimeter;
                        for (var ptMap in sortedSR)
                        {
                            if (ptMap.cavityDepth > maxCD) { maxCD = ptMap.cavityDepth; }
                        }
                        if (maxCD < MIN_CURVE_CD)
                        {
                            continue;
                        }

                        buildCurveCollectionsForSubRegion(context,
                            id + ("curves_" ~ ri ~ "_" ~ b ~ "_" ~ e ~ "_" ~ sr),
                            sortedSR, definition.definitionType, approxDef);
                    }
                }
            }
        }

        // Step 7: Guide curves + surface creation -- TODO (stub)
        // Awaiting confirmation of loft strategy before implementing.
    });


// --- Step 0: setupGeometry

function setupGeometry(context is Context, id is Id, definition is map) returns map
{
    const cdCopy     = copyBody(context, id + "copyCD", definition.cdProfile, "CD_COPY");
    const swRoutCopy = copyBody(context, id + "copySWRout", definition.swRoutSurf, "SW-ROUT_COPY");
    const topCopy    = copyBody(context, id + "copyTop", definition.topSurf, "TOP_COPY");

    opIntersectFaces(context, id + "initialPinchWire", {
        "tools"   : qOwnedByBody(cdCopy, EntityType.FACE),
        "targets" : qOwnedByBody(swRoutCopy, EntityType.FACE)
    });

    opExtractWires(context, id + "extractPinchWire", {
        "edges" : qCreatedBy(id + "initialPinchWire", EntityType.EDGE)
    });

    opDeleteBodies(context, id + "deleteSeedPinchWires", {
        "entities" : qCreatedBy(id + "initialPinchWire", EntityType.BODY)
    });

    const pinchWire = qCreatedBy(id + "extractPinchWire", EntityType.BODY);
    setProperty(context, {
        "entities"     : pinchWire,
        "propertyType" : PropertyType.NAME,
        "value"        : "PINCH_WIRE"
    });

    var cdSurfsBase = splitCDSurfs_i(context, id + "splitCDSurfs", cdCopy, swRoutCopy);
    const cdSurfs = mergeMaps(cdSurfsBase, { "footprint" : definition.footprintSurf, "bottom" : definition.bottomSurf });

    return {
        "pinchWire"   : pinchWire,
        "cdSurfs"     : cdSurfs,
        "topCopy"     : topCopy,
        "cdCopy"      : cdCopy,
        "swRoutCopy"  : swRoutCopy
    };
}


// --- Step 1: preprocessPoints_i

function preprocessPoints_i(context is Context, pointArray is array, processedPath is map, pinchWire is Query) returns array
{
    var updatedPoints = [];
    for (var point in pointArray)
    {
        var ptRefFrame = (point.locType == PointDefinitionType_i.DIST_FROM_REF)
            ? frameAtDistFromRef(context, processedPath, point.pointFromRef)
            : frameAtQuery(context, processedPath, point.locPoint);

        // Snap origin to pinch wire if it is not already on it.
        var origin = ptRefFrame.frame.origin;
        var snapDist = evDistance(context, {
            "side0" : pinchWire,
            "side1" : origin
        });

        if (snapDist.distance > CAVITY_DEPTH_TOL)
        {
            origin     = snapDist.sides[0].point;
            ptRefFrame = frameAtPoint(context, processedPath, origin);
        }

        point['pointRefFrame'] = ptRefFrame;
        point['pointRefPlane'] = plane(ptRefFrame.frame.origin, ptRefFrame.frame.zAxis);
        point['arcLength']     = ptRefFrame.arcLength;

        updatedPoints = append(updatedPoints, point);
    }
    return updatedPoints;
}


// --- Step 2: preprocessRegions_i

function preprocessRegions_i(context is Context, id is Id, processedPath is map, preprocessedPoints is array, regions is array, pinchWire is Query, processedPinchWire is map) returns array
{
    var processedRegions = [];

    for (var i = 0; i < size(regions); i += 1)
    {
        var region     = regions[i];
        var startPoint = preprocessedPoints[i];
        var endPoint   = preprocessedPoints[i + 1];

        var transitionMap = {
            "transitionType" : region.transitionType
        };

        opPattern(context, id + ("copyPinch_" ~ i), {
            "entities"      : pinchWire,
            "transforms"    : [identityTransform()],
            "instanceNames" : ["1"]
        });

        var regionWire = qCreatedBy(id + ("copyPinch_" ~ i), EntityType.BODY);
        setProperty(context, {
            "entities"     : regionWire,
            "propertyType" : PropertyType.NAME,
            "value"        : region.regionName ~ " PINCH_WIRE"
        });

        if (wireGenuinelyCrossesPlane(context, regionWire, startPoint.pointRefPlane))
        {
            opSplitPart(context, id + ("splitStart_" ~ i), {
                "targets" : regionWire,
                "tool"    : startPoint.pointRefPlane
            });

            var splitTrue   = qSplitBy(id + ("splitStart_" ~ i), EntityType.BODY, true);
            var splitFalse  = qSplitBy(id + ("splitStart_" ~ i), EntityType.BODY, false);
            var trueBox     = evBox3d(context, { "topology" : splitTrue,  "tight" : true });
            var falseBox    = evBox3d(context, { "topology" : splitFalse, "tight" : true });
            var trueMid     = (trueBox.minCorner  + trueBox.maxCorner)  / 2;
            var falseMid    = (falseBox.minCorner + falseBox.maxCorner) / 2;
            var trueDist    = norm(trueMid  - endPoint.pointRefPlane.origin);
            var falseDist   = norm(falseMid - endPoint.pointRefPlane.origin);
            var deleteSide  = (trueDist > falseDist) ? splitTrue : splitFalse;

            opDeleteBodies(context, id + ("deleteStart_" ~ i), { "entities" : deleteSide });
        }

        if (wireGenuinelyCrossesPlane(context, regionWire, endPoint.pointRefPlane))
        {
            opSplitPart(context, id + ("splitEnd_" ~ i), {
                "targets" : regionWire,
                "tool"    : endPoint.pointRefPlane
            });

            var splitTrue   = qSplitBy(id + ("splitEnd_" ~ i), EntityType.BODY, true);
            var splitFalse  = qSplitBy(id + ("splitEnd_" ~ i), EntityType.BODY, false);
            var trueBox     = evBox3d(context, { "topology" : splitTrue,  "tight" : true });
            var falseBox    = evBox3d(context, { "topology" : splitFalse, "tight" : true });
            var trueMid     = (trueBox.minCorner  + trueBox.maxCorner)  / 2;
            var falseMid    = (falseBox.minCorner + falseBox.maxCorner) / 2;
            var trueDist    = norm(trueMid  - startPoint.pointRefPlane.origin);
            var falseDist   = norm(falseMid - startPoint.pointRefPlane.origin);
            var deleteSide  = (trueDist > falseDist) ? splitTrue : splitFalse;

            opDeleteBodies(context, id + ("deleteEnd_" ~ i), { "entities" : deleteSide });
        }

        processedRegions = append(processedRegions, {
            "region"        : region,
            "startPoint"    : startPoint,
            "endPoint"      : endPoint,
            "transitionMap" : transitionMap,
            "regionWire"    : regionWire
        });
    }

    return processedRegions;
}


// --- Step 3 helpers: Greville params

// Returns { native: array, normalized: array } of Greville abscissae.
// native[] are in [uMin, uMax]; normalized[] are in [0, 1] for evEdgeTangentLines.
function computeGrevilleParams(bsCurve is BSplineCurve) returns map
{
    var knots     = getKnotVector(bsCurve);
    var degree    = getDegree(bsCurve);
    var paramRange = getBSplineParamRange(bsCurve);
    var uMin      = paramRange.uMin;
    var uMax      = paramRange.uMax;
    var span      = uMax - uMin;
    var n         = size(knots) - degree - 1; // number of control points

    var nativeParams     = [];
    var normalizedParams = [];

    for (var i = 0; i < n; i += 1)
    {
        var g = 0.0;
        for (var k = 1; k <= degree; k += 1)
        {
            g = g + knots[i + k];
        }
        g = g / degree;
        nativeParams     = append(nativeParams, g);
        normalizedParams = append(normalizedParams, (span > 1e-12) ? (g - uMin) / span : 0.0);
    }

    // Downsample if too many control points — keeps endpoints, uniform spacing between.
    if (n > MAX_GREVILLE_SAMPLES)
    {
        var sampledNative     = [];
        var sampledNormalized = [];
        for (var s = 0; s < MAX_GREVILLE_SAMPLES; s += 1)
        {
            var idx = round((s / (MAX_GREVILLE_SAMPLES - 1)) * (n - 1));
            sampledNative     = append(sampledNative,     nativeParams[idx]);
            sampledNormalized = append(sampledNormalized, normalizedParams[idx]);
        }
        nativeParams     = sampledNative;
        normalizedParams = sampledNormalized;
    }

    return { "native" : nativeParams, "normalized" : normalizedParams };
}


// Returns true if all sampled points on edge lie within PLANE_TOL of testPlane.
// Used to skip edges that are colinear with (lie in) a region boundary plane.
function edgeLiesInPlane(context is Context, edge is Query, testPlane is Plane) returns boolean
{
    const PLANE_TOL    = 1e-6 * meter;
    const sampleParams = [0.0, 0.25, 0.5, 0.75, 1.0];

    const tangentLines = evEdgeTangentLines(context, {
        "edge"       : edge,
        "parameters" : sampleParams
    });

    for (var tl in tangentLines)
    {
        if (abs(dot(tl.origin - testPlane.origin, testPlane.normal)) > PLANE_TOL)
        {
            return false;
        }
    }
    return true;
}


// Builds one edgePointMap at a native parameter u along edge.
function buildSingleEdgePointMap(context is Context, edge is Query, nativeParam is number,
    normalizedParam is number, bsCurve is BSplineCurve, processedPinchWire is map, cdSurfs is map,
    topSurf is Query, cdSurf is Query, startPoint is map, endPoint is map, transitionMap is map,
    definitionType) returns map
{
    var tl = evEdgeTangentLine(context, {
        "edge"                     : edge,
        "parameter"                : normalizedParam,
        "arcLengthParameterization" : false
    });

    var origin = tl.origin;
    var arcLen = arcLengthOnPinchWire(processedPinchWire.ptTable, origin);

    // xAxis: face normal of the bottom surface at the closest point to origin, flipped to +Z.
    var closestBotFace  = qClosestTo(qOwnedByBody(cdSurfs.bottom, EntityType.FACE), origin);
    var botFaceDist     = evDistance(context, { "side0" : closestBotFace, "side1" : origin });
    var botTangentPlane = evFaceTangentPlane(context, { "face" : closestBotFace, "parameter" : botFaceDist.sides[0].parameter });
    var xRaw            = botTangentPlane.normal;
    var xAx             = (dot(xRaw, vector(0, 0, 1)) >= 0) ? xRaw : -xRaw;

    // Cavity depth: cast ray along xAx to topSurf.
    var topHits     = evRaycast(context, { "ray" : line(origin, xAx), "entities" : topSurf, "closest" : true });
    var cavityDepth = (size(topHits) > 0) ? norm(topHits[0].intersection - origin) : 0 * millimeter;

    // yAxis: nearest footprint face normal, projected perpendicular to xAxis.
    // Polarity check (probe toward cdSurfs.inside) ensures yAxis points outward.
    // qClosestTo resolves to the specific closest face so evFaceTangentPlane gets a valid Query.
    // Footprint faces are planar (vertical extrusion) so normal is constant; UV (0.5, 0.5) is fine.
    var fpFaces        = qOwnedByBody(cdSurfs.footprint, EntityType.FACE);
    var closestFpFace  = qClosestTo(fpFaces, origin);
    var fpFaceDist     = evDistance(context, { "side0" : closestFpFace, "side1" : origin });
    var fpTangentPlane = evFaceTangentPlane(context, { "face" : closestFpFace, "parameter" : fpFaceDist.sides[0].parameter });
    var fpNormalRaw    = fpTangentPlane.normal;
    var probeOut = origin + fpNormalRaw * YAXIS_PROBE_DIST;
    var probeIn  = origin - fpNormalRaw * YAXIS_PROBE_DIST;
    var dOut = evDistance(context, { "side0" : cdSurfs.inside, "side1" : probeOut }).distance;
    var dIn  = evDistance(context, { "side0" : cdSurfs.inside, "side1" : probeIn  }).distance;
    var fpNormal = (dOut > dIn) ? fpNormalRaw : -fpNormalRaw;
    var yRaw = fpNormal - xAx * dot(xAx, fpNormal);
    var yLen = norm(yRaw);
    var yAx  = (yLen > 1e-10) ? yRaw / yLen : cross(xAx, vector(0, 1, 0));

    // zAxis: cross(xAxis, yAxis) — automatically perpendicular to both.
    var zAx = cross(xAx, yAx);

    var pointFrame = coordSystem(origin, xAx, zAx);

    // spanParam: project startPoint and endPoint onto the pinch wire to get arc lengths
    // in the same coordinate system as arcLen. This fixes the coordinate mismatch that
    // caused spanParam to clamp to 1 for all regions except the one near the zero point.
    var startArcLen = arcLengthOnPinchWire(processedPinchWire.ptTable, startPoint.pointRefFrame.frame.origin);
    var endArcLen   = arcLengthOnPinchWire(processedPinchWire.ptTable, endPoint.pointRefFrame.frame.origin);
    var regionSpan  = endArcLen - startArcLen;
    var rawSpan     = (abs(regionSpan) / meter > 1e-12) ? ((arcLen - startArcLen) / regionSpan) : 0.0;
    var spanParam   = max(0.0, min(1.0, rawSpan));

    var curvePoints = solveCapWallPoints(context, origin, xAx, yAx, pointFrame,
        spanParam, cavityDepth, startPoint, endPoint, transitionMap, topSurf, cdSurf, definitionType);

    return {
        "nativeParam"  : nativeParam,
        "normalizedParam" : normalizedParam,
        "arcLength"    : arcLen,
        "origin"       : origin,
        "xAxis"        : xAx,
        "yAxis"        : yAx,
        "zAxis"        : zAx,
        "pointFrame"   : pointFrame,
        "cavityDepth"  : cavityDepth,
        "spanParam"    : spanParam,
        "curvePoints"  : curvePoints,
        "pointType"    : "NORMAL"
    };
}


// Builds all edge point maps for one edge using Greville sampling.
function buildEdgePointMaps(context is Context, edge is Query, bsCurve is BSplineCurve,
    grevilleParams is map, processedPinchWire is map, cdSurfs is map,
    topSurf is Query, cdSurf is Query, startPoint is map, endPoint is map, transitionMap is map,
    definitionType) returns array
{
    var nativeParams     = grevilleParams.native;
    var normalizedParams = grevilleParams.normalized;
    var maps = [];

    for (var j = 0; j < size(nativeParams); j += 1)
    {
        maps = append(maps, buildSingleEdgePointMap(context, edge, nativeParams[j], normalizedParams[j],
            bsCurve, processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap, definitionType));
    }

    return maps;
}


// Orchestrates edge processing: Greville params -> point maps.
// bsCurve is pre-computed by the caller (avoids a duplicate evApproximateBSplineCurve call).
function processRegionEdge(context is Context, id is Id, edge is Query, bsCurve is BSplineCurve,
    processedPinchWire is map, cdSurfs is map, topSurf is Query, cdSurf is Query,
    startPoint is map, endPoint is map, transitionMap is map, definitionType) returns map
{
    var grevilleParams = computeGrevilleParams(bsCurve);
    var edgePointMaps  = buildEdgePointMaps(context, edge, bsCurve, grevilleParams,
        processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap, definitionType);

    return {
        "edge"           : edge,
        "bsCurve"        : bsCurve,
        "grevilleParams" : grevilleParams,
        "edgePointMaps"  : edgePointMaps
    };
}


// Solves pinch and top curve points (and RADIUS_ANGLE intermediate points) at one sample.
function solveCapWallPoints(context is Context, origin is Vector, xAx is Vector, yAx is Vector,
    pointFrame is CoordSystem, spanParam is number, cavityDepth is ValueWithUnits,
    startPoint is map, endPoint is map, transitionMap is map,
    topSurf is Query, cdSurf is Query, definitionType) returns map
{
    var transType = transitionMap.transitionType;

    var pinchOffsetVal = profileValueAt_i(spanParam, startPoint.pinchOffset, endPoint.pinchOffset, transType);
    var rawPinchPoint  = origin + yAx * pinchOffsetVal;
    var pinchPoint     = evDistance(context, { "side0" : cdSurf, "side1" : rawPinchPoint }).sides[0].point;

    var offset = (definitionType == "CONTINUITY")
        ? profileValueAt_i(spanParam, startPoint.offset, endPoint.offset, transType)
        : calcCapWallOffset_i(cavityDepth,
            profileValueAt_i(spanParam, startPoint.pinchRadius, endPoint.pinchRadius, transType),
            profileValueAt_i(spanParam, startPoint.topRadius, endPoint.topRadius, transType),
            profileValueAt_i(spanParam, startPoint.wallAngle, endPoint.wallAngle, transType));

    var offsetPoint = origin + yAx * offset;
    // Project offsetPoint onto topSurf along xAx via raycast.
    // evRaycast avoids the distant-point issue that evDistance(surface, line) has when the line misses.
    // Fallback to nearest-point projection if the ray misses (e.g. offset puts point outside topSurf).
    var topPointHits = evRaycast(context, { "ray" : line(offsetPoint, xAx), "entities" : topSurf, "closest" : true });
    var topPoint = (size(topPointHits) > 0)
        ? topPointHits[0].intersection
        : evDistance(context, { "side0" : topSurf, "side1" : offsetPoint }).sides[0].point;

    var retMap = {
        "pinchPoint" : pinchPoint,
        "topPoint"   : topPoint
    };

    if (definitionType == "RADIUS_ANGLE")
    {
        var connectionMap = {
            "pinchRadius"   : profileValueAt_i(spanParam, startPoint.pinchRadius, endPoint.pinchRadius, transType),
            "topEdgeRadius" : profileValueAt_i(spanParam, startPoint.topRadius, endPoint.topRadius, transType),
            "capWallAngle"  : profileValueAt_i(spanParam, startPoint.wallAngle, endPoint.wallAngle, transType),
            "pointFrame"    : pointFrame
        };
        var connectedPoints = connectCapWallPoints_i(connectionMap, pinchPoint, topPoint, cavityDepth);
        retMap = mergeMaps(retMap, connectedPoints);
    }

    return retMap;
}


// --- Step 4: Zero crossing detection

// Evaluates cavity depth at a native parameter u along edge.
function evaluateCavityDepth(context is Context, edge is Query, nativeParam is number, uMin is number, uMax is number, topSurf is Query) returns ValueWithUnits
{
    var span      = uMax - uMin;
    var normParam = (span > 1e-12) ? (nativeParam - uMin) / span : 0.0;

    var tl = evEdgeTangentLine(context, {
        "edge"                     : edge,
        "parameter"                : normParam,
        "arcLengthParameterization" : false
    });

    var worldZ  = vector(0, 0, 1);
    var topHits = evRaycast(context, { "ray" : line(tl.origin, worldZ), "entities" : topSurf, "closest" : true });

    if (size(topHits) > 0)
    {
        return norm(topHits[0].intersection - tl.origin);
    }

    return 0 * millimeter;
}


// Finds the zero crossing between highPoint (positive CD) and lowPoint (zero CD).
// Returns a complete edgePointMap at the crossing.
function findZeroCrossing(context is Context, highPoint is map, lowPoint is map, edge is Query,
    bsCurve is BSplineCurve, processedPinchWire is map, cdSurfs is map, topSurf is Query, cdSurf is Query,
    startPoint is map, endPoint is map, transitionMap is map, definitionType) returns map
{
    var paramRange = getBSplineParamRange(bsCurve);
    var uMin = paramRange.uMin;
    var uMax = paramRange.uMax;

    var f = function(u)
    {
        return (evaluateCavityDepth(context, edge, u, uMin, uMax, topSurf) - CAVITY_DEPTH_TOL) / millimeter;
    };

    var fa = f(highPoint.nativeParam);
    var fb = f(lowPoint.nativeParam);
    var u;
    if (fa * fb >= 0)
    {
        u = (abs(fa) < abs(fb)) ? highPoint.nativeParam : lowPoint.nativeParam;
    }
    else
    {
        var result = solveRootHybrid(f, highPoint.nativeParam, lowPoint.nativeParam, BSEARCH_PARAM_TOL, BSEARCH_MAX_ITER);
        u = result.u;
    }
    var span      = uMax - uMin;
    var normParam = (span > 1e-12) ? (u - uMin) / span : 0.0;

    var ptMap = buildSingleEdgePointMap(context, edge, u, normParam, bsCurve,
        processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap, definitionType);

    return mergeMaps(ptMap, {
        "cavityDepth" : 0 * millimeter,
        "pointType"   : "ZERO_CROSSING"
    });
}


// Splits edgePointMaps into sub-regions of positive CD.
// Each sub-region is padded at its boundaries with a ZERO_CROSSING point
// solved exactly at the CD=0 transition.
function filterAndInsertZeroCrossings(context is Context, edgePointMaps is array, edge is Query,
    bsCurve is BSplineCurve, processedPinchWire is map, cdSurfs is map, topSurf is Query, cdSurf is Query,
    startPoint is map, endPoint is map, transitionMap is map, definitionType) returns array
{
    var subRegions = [];
    var curRegion  = [];

    for (var k = 0; k < size(edgePointMaps); k += 1)
    {
        var ptMap = edgePointMaps[k];

        if (ptMap.cavityDepth > CAVITY_DEPTH_TOL)
        {
            // Entering a positive-CD run: insert leading zero crossing if previous point was zero.
            if (size(curRegion) == 0 && k > 0 && edgePointMaps[k - 1].cavityDepth <= CAVITY_DEPTH_TOL)
            {
                var zc = findZeroCrossing(context, ptMap, edgePointMaps[k - 1], edge, bsCurve,
                    processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap, definitionType);
                curRegion = append(curRegion, zc);
            }
            curRegion = append(curRegion, ptMap);
        }
        else
        {
            if (size(curRegion) > 0)
            {
                // Leaving a positive-CD run: insert trailing zero crossing.
                var lastHigh = curRegion[size(curRegion) - 1];
                var zc = findZeroCrossing(context, lastHigh, ptMap, edge, bsCurve,
                    processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap, definitionType);
                curRegion  = append(curRegion, zc);
                subRegions = append(subRegions, curRegion);
                curRegion  = [];
            }
        }
    }

    if (size(curRegion) > 0)
    {
        subRegions = append(subRegions, curRegion);
    }

    return subRegions;
}


// --- Step 5: minCD transition detection

function calcMinCD_i(pinchRadius is ValueWithUnits, topRadius is ValueWithUnits, wallAngle is ValueWithUnits) returns ValueWithUnits
{
    return (1 - cos(90 * degree - wallAngle)) * (pinchRadius + topRadius);
}


function findMinCDTransition(context is Context, pointA is map, pointB is map, edge is Query,
    bsCurve is BSplineCurve, processedPinchWire is map, cdSurfs is map, topSurf is Query, cdSurf is Query,
    startPoint is map, endPoint is map, transitionMap is map) returns map
{
    var paramRange = getBSplineParamRange(bsCurve);
    var uMin = paramRange.uMin;
    var uMax = paramRange.uMax;
    var span = uMax - uMin;

    var f = function(u)
    {
        var cd       = evaluateCavityDepth(context, edge, u, uMin, uMax, topSurf);
        var normParam = (span > 1e-12) ? (u - uMin) / span : 0.0;

        // Approximate spanParam at u
        var tl = evEdgeTangentLine(context, {
            "edge"                     : edge,
            "parameter"                : normParam,
            "arcLengthParameterization" : false
        });
        var arcLen      = arcLengthOnPinchWire(processedPinchWire.ptTable, tl.origin);
        var startArcLen = startPoint.arcLength;
        var endArcLen   = endPoint.arcLength;
        var regionSpan  = endArcLen - startArcLen;
        var spanParam   = (regionSpan / meter > 1e-12) ? ((arcLen - startArcLen) / regionSpan) : 0.0;

        var transType   = transitionMap.transitionType;
        var minCD = calcMinCD_i(
            profileValueAt_i(spanParam, startPoint.pinchRadius, endPoint.pinchRadius, transType),
            profileValueAt_i(spanParam, startPoint.topRadius,   endPoint.topRadius,   transType),
            profileValueAt_i(spanParam, startPoint.wallAngle,   endPoint.wallAngle,   transType)
        );

        return (cd - minCD) / millimeter;
    };

    var result    = solveRootHybrid(f, pointA.nativeParam, pointB.nativeParam, BSEARCH_PARAM_TOL, BSEARCH_MAX_ITER);
    var u         = result.u;
    var normParam = (span > 1e-12) ? (u - uMin) / span : 0.0;

    var ptMap = buildSingleEdgePointMap(context, edge, u, normParam, bsCurve,
        processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap, "RADIUS_ANGLE");

    // At the transition, midPoint == pinchFilletTop == topFilletBottom.
    // buildSingleEdgePointMap already sets curvePoints correctly via solveCapWallPoints at cd==minCD.
    ptMap = mergeMaps(ptMap, { "pointType" : "MIN_CD_TRANSITION" });

    return ptMap;
}


function insertMinCDTransitions(context is Context, subRegion is array, edge is Query,
    bsCurve is BSplineCurve, processedPinchWire is map, cdSurfs is map, topSurf is Query, cdSurf is Query,
    startPoint is map, endPoint is map, transitionMap is map) returns array
{
    if (size(subRegion) < 2)
    {
        return subRegion;
    }

    var result = [];

    for (var k = 0; k < size(subRegion) - 1; k += 1)
    {
        var ptA = subRegion[k];
        var ptB = subRegion[k + 1];
        result  = append(result, ptA);

        // Check if cd-minCD changes sign between ptA and ptB.
        // Both must have RADIUS_ANGLE curvePoints; check presence of midPoint vs pinchTop.
        var aHasMid   = ptA.curvePoints.midPoint  != undefined;
        var bHasMid   = ptB.curvePoints.midPoint  != undefined;
        var aHasWall  = ptA.curvePoints.pinchTop   != undefined;
        var bHasWall  = ptB.curvePoints.pinchTop   != undefined;

        var signChange = (aHasMid && bHasWall) || (aHasWall && bHasMid);

        if (signChange)
        {
            var transPoint = findMinCDTransition(context, ptA, ptB, edge, bsCurve,
                processedPinchWire, cdSurfs, topSurf, cdSurf, startPoint, endPoint, transitionMap);
            result = append(result, transPoint);
        }
    }

    result = append(result, subRegion[size(subRegion) - 1]);

    return result;
}


// --- Step 6: Build curves

function buildCurveCollectionsForSubRegion(context is Context, id is Id, subRegion is array,
    definitionType, approxDef is map) returns map
{
    var spanParams        = [];
    var pinchPts          = [];
    var topPts            = [];
    var midPts            = [];
    var pinchFilletTopPts = [];
    var topFilletBottomPts = [];

    // allHaveMid: true only when every point in the sub-region has a midPoint (pure fillet-only section).
    // allHaveWall: true only when every point has pinchTop/topEdgeBottom (pure wall+fillet section).
    // Mixed sub-regions (containing a MIN_CD_TRANSITION) only get pinchCurve + topCurve.
    var allHaveMid  = true;
    var allHaveWall = true;

    var lastSpanParam = -1.0;
    for (var ptMap in subRegion)
    {
        var sp = ptMap.spanParam;
        if (sp - lastSpanParam < 1e-6)
        {
            // Too close to previous — skip to prevent approximateSpline parameter error.
            if (definitionType == "RADIUS_ANGLE")
            {
                if (ptMap.curvePoints.midPoint == undefined) { allHaveMid = false; }
                if (ptMap.curvePoints.pinchTop == undefined) { allHaveWall = false; }
            }
            continue;
        }
        lastSpanParam = sp;

        spanParams = append(spanParams, sp);
        pinchPts   = append(pinchPts,   ptMap.curvePoints.pinchPoint);
        topPts     = append(topPts,     ptMap.curvePoints.topPoint);

        if (definitionType == "RADIUS_ANGLE")
        {
            if (ptMap.curvePoints.midPoint != undefined)
            {
                midPts = append(midPts, ptMap.curvePoints.midPoint);
            }
            else
            {
                allHaveMid = false;
            }

            if (ptMap.curvePoints.pinchTop != undefined)
            {
                pinchFilletTopPts  = append(pinchFilletTopPts,  ptMap.curvePoints.pinchTop);
                topFilletBottomPts = append(topFilletBottomPts, ptMap.curvePoints.topEdgeBottom);
            }
            else
            {
                allHaveWall = false;
            }
        }
    }

    // Build targets array for a single approximateSpline call.
    var targets = [
        approximationTarget({ "positions" : pinchPts }),
        approximationTarget({ "positions" : topPts })
    ];

    if (definitionType == "RADIUS_ANGLE")
    {
        if (allHaveMid)
        {
            targets = append(targets, approximationTarget({ "positions" : midPts }));
        }
        if (allHaveWall)
        {
            targets = append(targets, approximationTarget({ "positions" : pinchFilletTopPts }));
            targets = append(targets, approximationTarget({ "positions" : topFilletBottomPts }));
        }
    }

    if (size(spanParams) < approxDef.degree + 1)
    {
        return {};
    }

    var approxResult = approximateSpline(context, {
        "degree"            : approxDef.degree,
        "tolerance"         : approxDef.tolerance,
        "isPeriodic"        : false,
        "maxControlPoints"  : approxDef.maxControlPoints,
        "targets"           : targets,
        "parameters"        : spanParams
    });

    var curveMap = {};

    var pinchCurveBS = approxResult[0];
    var topCurveBS   = approxResult[1];

    curveMap["pinchCurve"] = createWireForCurve(context, id + "pinchCurve", pinchCurveBS, "PINCH_CURVE");
    curveMap["topCurve"]   = createWireForCurve(context, id + "topCurve",   topCurveBS,   "TOP_CURVE");

    if (definitionType == "RADIUS_ANGLE")
    {
        var targetIdx = 2;
        if (allHaveMid)
        {
            curveMap["midCurve"] = createWireForCurve(context, id + "midCurve", approxResult[targetIdx], "MID_CURVE");
            targetIdx = targetIdx + 1;
        }
        if (allHaveWall)
        {
            curveMap["pinchFilletTopCurve"]   = createWireForCurve(context, id + "pinchFilletTopCurve",   approxResult[targetIdx],     "PINCH_FILLET_TOP_CURVE");
            curveMap["topFilletBottomCurve"]  = createWireForCurve(context, id + "topFilletBottomCurve",  approxResult[targetIdx + 1], "TOP_FILLET_BOTTOM_CURVE");
        }
    }

    return curveMap;
}


function createWireForCurve(context is Context, id is Id, bsCurveData is BSplineCurve, name is string) returns Query
{
    opCreateBSplineCurve(context, id, { "bSplineCurve" : bsCurveData });
    setProperty(context, {
        "entities"     : qCreatedBy(id, EntityType.BODY),
        "propertyType" : PropertyType.NAME,
        "value"        : name
    });
    return qCreatedBy(id, EntityType.BODY);
}


// --- Shared utilities

function copyBody(context is Context, id is Id, body is Query, name is string) returns Query
{
    opPattern(context, id + "copyBody", {
        "entities"      : body,
        "transforms"    : [identityTransform()],
        "instanceNames" : ["foo"]
    });
    setProperty(context, {
        "entities"     : qCreatedBy(id + "copyBody", EntityType.BODY),
        "propertyType" : PropertyType.NAME,
        "value"        : name
    });
    return qCreatedBy(id + "copyBody", EntityType.BODY);
}


function splitCDSurfs_i(context is Context, id is Id, cdCopy is Query, swSurf is Query) returns map
{
    opPattern(context, id + "copyCDSurftrim", {
        "entities"      : cdCopy,
        "transforms"    : [identityTransform()],
        "instanceNames" : ["1"]
    });

    opSplitPart(context, id + "splitCDWithSW", {
        "targets"   : qCreatedBy(id + "copyCDSurftrim", EntityType.BODY),
        "tool"      : swSurf,
        "keepTools" : true
    });

    var splitTrue  = qSplitBy(id + "splitCDWithSW", EntityType.BODY, true);
    var splitFalse = qSplitBy(id + "splitCDWithSW", EntityType.BODY, false);
    var trueBox    = evBox3d(context, { "topology" : splitTrue,  "tight" : true });
    var falseBox   = evBox3d(context, { "topology" : splitFalse, "tight" : true });

    var insideTrue = norm(trueBox.maxCorner - trueBox.minCorner) < norm(falseBox.maxCorner - falseBox.minCorner);

    if (insideTrue)
    {
        setProperty(context, { "entities" : splitTrue,  "propertyType" : PropertyType.NAME, "value" : "INSIDE_CD_SURF"  });
        setProperty(context, { "entities" : splitFalse, "propertyType" : PropertyType.NAME, "value" : "OUTSIDE_CD_SURF" });
        setProperty(context, { "entities" : splitTrue,  "propertyType" : PropertyType.APPEARANCE, "value" : color(195/255, 139/255, 205/255) });
        setProperty(context, { "entities" : splitFalse, "propertyType" : PropertyType.APPEARANCE, "value" : color(245/255, 213/255, 120/255) });
        return { "inside" : splitTrue, "outside" : splitFalse };
    }
    else
    {
        setProperty(context, { "entities" : splitFalse, "propertyType" : PropertyType.NAME, "value" : "INSIDE_CD_SURF"  });
        setProperty(context, { "entities" : splitTrue,  "propertyType" : PropertyType.NAME, "value" : "OUTSIDE_CD_SURF" });
        setProperty(context, { "entities" : splitFalse, "propertyType" : PropertyType.APPEARANCE, "value" : color(195/255, 139/255, 205/255) });
        setProperty(context, { "entities" : splitTrue,  "propertyType" : PropertyType.APPEARANCE, "value" : color(245/255, 213/255, 120/255) });
        return { "inside" : splitFalse, "outside" : splitTrue };
    }
}


/**
 * Linear or smootherstep interpolation between startVal and endVal at normalized t in [0,1].
 */
function profileValueAt_i(t is number, startVal, endVal, regionType) returns ValueWithUnits
{
    var s;
    if (regionType == "LINEAR" || regionType == RegionTransitionType_i.LINEAR)
    {
        s = t;
    }
    else
    {
        s = t * t * t * (10 + t * (6 * t - 15));
    }
    return startVal + (endVal - startVal) * s;
}


function calcCapWallOffset_i(cavityDepth is ValueWithUnits, pinchRadius is ValueWithUnits, topEdgeRadius is ValueWithUnits, capWallAngle is ValueWithUnits) returns ValueWithUnits
{
    var minCD = (1 - cos(90 * degree - capWallAngle)) * (pinchRadius + topEdgeRadius);
    if (cavityDepth <= minCD)
    {
        var thetaOverride = 90 * degree - acos(1 - (cavityDepth / (pinchRadius + topEdgeRadius)));
        return sin(90 * degree - thetaOverride) * (pinchRadius + topEdgeRadius);
    }
    else
    {
        var wallCD = cavityDepth - minCD;
        return sin(90 * degree - capWallAngle) * (pinchRadius + topEdgeRadius) + wallCD * tan(capWallAngle);
    }
}


// Solves RADIUS_ANGLE intermediate points from pinchPoint and topPoint.
// Returns map with either { midPoint } or { pinchTop, topEdgeBottom }.
function connectCapWallPoints_i(connectionMap is map, pinchPoint is Vector, topPoint is Vector, cavityDepth is ValueWithUnits) returns map
{
    var minCD = (1 - cos(90 * degree - connectionMap.capWallAngle)) * (connectionMap.pinchRadius + connectionMap.topEdgeRadius);

    if (cavityDepth <= minCD)
    {
        var thetaOverride = 90 * degree - acos(1 - (cavityDepth / (connectionMap.pinchRadius + connectionMap.topEdgeRadius)));
        var vertOffset    = (1 - cos(thetaOverride)) * connectionMap.pinchRadius;
        var sideOffset    = sin(90 * degree - thetaOverride) * connectionMap.pinchRadius;
        return { "midPoint" : pinchPoint + vertOffset * connectionMap.pointFrame.xAxis + sideOffset * yAxis(connectionMap.pointFrame) };
    }
    else
    {
        var pinchTopVertOffset   = (1 - cos(connectionMap.capWallAngle)) * connectionMap.pinchRadius;
        var pinchTopSideOffset   = sin(90 * degree - connectionMap.capWallAngle) * connectionMap.pinchRadius;
        var wallCD               = cavityDepth - minCD;
        var topEdgeBottomVertOffset = wallCD;
        var topEdgeBottomSideOffset = tan(connectionMap.capWallAngle) * wallCD;
        return {
            "pinchTop"       : pinchPoint + pinchTopVertOffset * connectionMap.pointFrame.xAxis + pinchTopSideOffset * yAxis(connectionMap.pointFrame),
            "topEdgeBottom"  : pinchPoint + (topEdgeBottomVertOffset + pinchTopVertOffset) * connectionMap.pointFrame.xAxis + (topEdgeBottomSideOffset + pinchTopSideOffset) * yAxis(connectionMap.pointFrame)
        };
    }
}


export function wireGenuinelyCrossesPlane(context is Context, wire is Query, testPlane is Plane) returns boolean
{
    const PLANE_TOL   = 1e-6 * meter;
    const edgeQuery   = qOwnedByBody(wire, EntityType.EDGE);
    const edges       = evaluateQuery(context, edgeQuery);
    const sampleParams = [0.0, 0.25, 0.5, 0.75, 1.0];

    if (size(edges) == 0)
    {
        return false;
    }

    for (var edge in edges)
    {
        const tangentLines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : sampleParams });
        var hasPos = false;
        var hasNeg = false;
        var allInPlane = true;

        for (var tLine in tangentLines)
        {
            const signedDist = dot(tLine.origin - testPlane.origin, testPlane.normal);
            if (abs(signedDist) > PLANE_TOL)
            {
                allInPlane = false;
                if (signedDist > 0 * meter)
                {
                    hasPos = true;
                }
                else
                {
                    hasNeg = true;
                }
            }
        }

        if (allInPlane)
        {
            continue;
        }
        if (hasPos && hasNeg)
        {
            return true;
        }
    }

    return false;
}
