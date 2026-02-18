FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/geomOperations.fs", version : "2878.0");
//import table types
import(path : "ff9221b7148cfda8a449abff", version : "2567f5f72109d2808992437d");


/**
 * QC Table Geometry Measurements
 *
 * Core and sidewall measurement functions at specific station locations.
 * Extracted and adapted from Generate_Core_Data and Generate_Sidewall_Data features.
 */

// ============================================================================
// BODY PREPARATION
// ============================================================================

/**
 * Prepare core bodies - handle single body or composite part
 * Composite parts contain multiple bodies but are treated as one unified geometry
 */
export function prepareCoreBodies(context is Context, coreQuery is Query) returns map
{
    var coreBodies = evaluateQuery(context, coreQuery);

    if (size(coreBodies) == 0)
    {
        throw "No core body selected";
    }
    else if (size(coreBodies) == 1)
    {
        // Simple case: single solid body
        return {
            "bodies" : coreQuery,
            "isComposite" : false,
            "bodyCount" : 1
        };
    }
    else
    {
        // Composite part: multiple bodies (for material properties)
        // Treat as one unified geometry
        return {
            "bodies" : qUnion(coreBodies),
            "isComposite" : true,
            "bodyCount" : size(coreBodies)
        };
    }
}

/**
 * Prepare sidewall body - must be single body
 */
export function prepareSidewallBody(context is Context, swQuery is Query) returns map
{
    var swBodies = evaluateQuery(context, swQuery);

    if (size(swBodies) == 0)
    {
        throw "No sidewall body selected";
    }
    else if (size(swBodies) > 1)
    {
        throw "Sidewall must be a single body (not composite)";
    }

    return {
        "bodies" : swQuery,
        "bodyCount" : 1
    };
}

// ============================================================================
// CORE MEASUREMENTS
// ============================================================================

/**
 * Measure core geometry at a specific X station
 * Returns all core measurements: width, thickness, grooved thickness, top width,
 * top angle, base rout dimensions, and Z positions for delta computation
 */
export function measureCoreAtStation(
    context is Context,
    id is Id,
    coreData is map,
    stationX is ValueWithUnits,
    coreExtents is Box3d,
    verbose is boolean) returns map
{
    // Validate input
    if (!isLength(stationX))
    {
        throw "stationX must be a length value";
    }

    // Create measurement plane at station
    var measurePlane = plane(vector(stationX, 0 * millimeter, 0 * millimeter), vector(1, 0, 0));

    // Create the plane body for intersection
    opPlane(context, id + "measurePlane", {
        "plane" : measurePlane
    });
    var planeBody = qCreatedBy(id + "measurePlane", EntityType.BODY);

    // Get all core edges
    var coreEdges = qOwnedByBody(coreData.bodies, EntityType.EDGE);

    // Find intersecting edges
    var intersectEdges = qIntersectsPlane(coreEdges, measurePlane);
    var evIntersectEdges = evaluateQuery(context, intersectEdges);

    // Collect all intersection points
    var allPoints = [];
    for (var edge in evIntersectEdges)
    {
        var pt = evDistance(context, {
            "side0" : measurePlane,
            "side1" : edge
        }).sides[1].point;

        allPoints = append(allPoints, pt);
    }

    allPoints = deduplicate(allPoints);

    if (size(allPoints) == 0)
    {
        // No intersection at this station
        opDeleteBodies(context, id + "deleteMeasurePlane", {"entities" : planeBody});
        return undefined;
    }

    // Find geometric properties
    var maxY = -1000 * millimeter;
    var maxZ = -1000 * millimeter;
    var minZ = 1000 * millimeter;

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

    var coreWidth = 2 * maxY;
    var coreThickness = maxZ - minZ;

    // Find widest and highest points
    var widestPoints = filter(allPoints, function(p)
    {
        return abs(p[1] - maxY) < EDGE_MARGIN;
    });

    var highestPoints = filter(allPoints, function(p)
    {
        return abs(p[2] - maxZ) < EDGE_MARGIN;
    });

    // Check for base rout (widest point not at minimum Z)
    var baseRoutDepth = '';
    var baseRoutWidth = '';

    // Edge case: if no widest points found, skip base rout detection
    if (size(widestPoints) == 0)
    {
        // No distinct widest points - skip base rout detection
    }
    else
    {
        var widestIsLowest = any(widestPoints, function(p)
        {
            return abs(p[2] - minZ) < EDGE_MARGIN;
        });

        if (!widestIsLowest)
        {
            // Base rout exists
            var lowestZ = min(mapArray(widestPoints, function(p) { return p[2]; }));
            baseRoutDepth = lowestZ - minZ;

            // Find inner edge at this Z level
            var atLowestZ = filter(allPoints, function(p)
            {
                return abs(p[2] - lowestZ) < EDGE_MARGIN;
            });

            var innerY = mapArray(atLowestZ, function(p) { return p[1]; });
            innerY = filter(innerY, function(y) { return abs(y - maxY) >= EDGE_MARGIN; });

            if (size(innerY) > 0)
            {
                var nextY = max(innerY);
                var insideEdgeQuery = qContainsPoint(coreEdges, vector(stationX, nextY, lowestZ));

                if (!isQueryEmpty(context, insideEdgeQuery))
                {
                    var brDist = evDistance(context, {
                        "side0" : vector(stationX, maxY, lowestZ),
                        "side1" : insideEdgeQuery
                    });

                    baseRoutWidth = brDist.distance;
                }
            }
        }
    }

    // Check for top edge (widest point not at maximum Z)
    var coreTopWidth = coreWidth;
    var coreTopAngle = '';

    // Edge case: if no widest points found, skip top edge detection
    if (size(widestPoints) == 0 || size(highestPoints) == 0)
    {
        // No distinct widest/highest points - skip top edge detection
    }
    else
    {
        var widestIsHighest = any(widestPoints, function(p)
        {
            return abs(p[2] - maxZ) < EDGE_MARGIN;
        });

        if (!widestIsHighest)
        {
            // Top edge exists
            var zVals = mapArray(widestPoints, function(p) { return p[2]; });
            var highestWidestZ = max(zVals);

            var highestWidest = filter(widestPoints, function(p)
            {
                return abs(p[2] - highestWidestZ) < EDGE_MARGIN;
            })[0];

            var highZYvals = mapArray(highestPoints, function(p) { return p[1]; });
            var widestHighestY = max(highZYvals);

            var widestHighest = filter(highestPoints, function(p)
            {
                return abs(p[1] - widestHighestY) < EDGE_MARGIN;
            })[0];

            coreTopWidth = 2 * widestHighest[1];

            // Calculate top edge angle - check for vertical edge (division by zero)
            var deltaZ = widestHighest[2] - highestWidest[2];
            if (abs(deltaZ.value) < TOLERANCE.zeroLength)
            {
                // Vertical or nearly vertical top edge
                coreTopAngle = 90 * degree;
            }
            else
            {
                var angle = atan((highestWidest[1] - widestHighest[1]) / deltaZ);
                coreTopAngle = round(angle, 0.1 * degree);
            }
        }
    }

    // Get grooved thickness from front plane split
    // Split core to get front plane section
    opPattern(context, id + "copyCoreForGroove", {
        "entities" : coreData.bodies,
        "transforms" : [transform(vector(0 * millimeter, 0 * millimeter, 0 * millimeter))],
        "instanceNames" : ["groove"]
    });

    var copiedCore = qCreatedBy(id + "copyCoreForGroove", EntityType.BODY);

    opSplitPart(context, id + "splitCoreForGroove", {
        "targets" : copiedCore,
        "tool" : qFrontPlane(EntityType.BODY),
        "keepTools" : true,
        "keepType" : SplitOperationKeepType.KEEP_BACK
    });

    var frontPlaneEdges = qCreatedBy(id + "splitCoreForGroove", EntityType.EDGE);
    var frontIntersectEdges = qIntersectsPlane(frontPlaneEdges, measurePlane);
    var evFrontIntersectEdges = evaluateQuery(context, frontIntersectEdges);

    var frontPoints = [];
    for (var edge in evFrontIntersectEdges)
    {
        var pt = evDistance(context, {
            "side0" : measurePlane,
            "side1" : edge
        }).sides[1].point;

        frontPoints = append(frontPoints, pt);
    }

    frontPoints = deduplicate(frontPoints);

    var groovedThickness = coreThickness;
    if (size(frontPoints) > 0)
    {
        var maxZFront = max(mapArray(frontPoints, function(p) { return p[2]; }));
        var minZFront = min(mapArray(frontPoints, function(p) { return p[2]; }));
        groovedThickness = maxZFront - minZFront;
    }

    // Clean up
    opDeleteBodies(context, id + "deleteGrooveCopy", {"entities" : copiedCore});
    opDeleteBodies(context, id + "deleteMeasurePlane", {"entities" : planeBody});

    // Debug visualization
    if (verbose)
    {
        addDebugPoint(context, vector(stationX, maxY, maxZ), DebugColor.RED);
        addDebugPoint(context, vector(stationX, -maxY, maxZ), DebugColor.RED);
        addDebugPoint(context, vector(stationX, 0 * millimeter, minZ), DebugColor.BLUE);
    }

    return {
        "coreWidth" : coreWidth,
        "coreThickness" : coreThickness,
        "groovedThickness" : groovedThickness,
        "coreTopWidth" : coreTopWidth,
        "coreTopAngle" : coreTopAngle,
        "baseRoutDepth" : baseRoutDepth,
        "baseRoutWidth" : baseRoutWidth,
        "coreBottomZ" : minZ,
        "coreTopZ" : maxZ
    };
}

// ============================================================================
// SIDEWALL MEASUREMENTS
// ============================================================================

/**
 * Setup sidewall measurement infrastructure
 * Extracts top and bottom edges and creates center splines
 */
export function setupSidewallMeasurement(
    context is Context,
    id is Id,
    swData is map,
    swExtents is Box3d) returns map
{
    // Get bottom edges
    var bottomEdges = getBottomEdges(context, id + "bottom", swData.bodies, swExtents);

    // Get top edges
    var topEdges = getTopEdges(context, id + "top", swData.bodies, swExtents);

    // Create paths from edges
    var insidePath = constructPath(context, qUnion(bottomEdges.inside));
    var outsidePath = constructPath(context, qUnion(bottomEdges.outside));

    var insideTopPath = constructPath(context, qUnion(topEdges.inside));
    var outsideTopPath = constructPath(context, qUnion(topEdges.outside));

    // Create center splines
    var centerSplineBottom = getCenterSpline(context, id + "centerBottom", insidePath, outsidePath);
    var centerSplineTop = getCenterSpline(context, id + "centerTop", insideTopPath, outsideTopPath);

    return {
        "centerSplineBottom" : centerSplineBottom,
        "centerSplineTop" : centerSplineTop
    };
}

/**
 * Measure sidewall at a specific X station
 */
export function measureSidewallAtStation(
    context is Context,
    swSetup is map,
    stationX is ValueWithUnits,
    verbose is boolean) returns map
{
    var measurePlane = plane(vector(stationX, 0 * millimeter, 0 * millimeter), vector(1, 0, 0));

    // Get bottom and top points on center splines
    var bottomPoint = evDistance(context, {
        "side0" : swSetup.centerSplineBottom,
        "side1" : measurePlane
    }).sides[0].point;

    var topPoint = evDistance(context, {
        "side0" : swSetup.centerSplineTop,
        "side1" : measurePlane
    }).sides[0].point;

    var swHeight = topPoint[2] - bottomPoint[2];

    if (verbose)
    {
        addDebugPoint(context, bottomPoint, DebugColor.MAGENTA);
        addDebugPoint(context, topPoint, DebugColor.GREEN);
    }

    return {
        "swHeight" : swHeight,
        "swBottomZ" : bottomPoint[2],
        "swTopZ" : topPoint[2]
    };
}

// ============================================================================
// SIDEWALL HELPER FUNCTIONS (from Generate_Sidewall_Data)
// ============================================================================

/**
 * Get bottom edges of sidewall (inside and outside)
 */
function getBottomEdges(context is Context, id is Id, swBody is Query, swExtents is Box3d) returns map
{
    var allEdges = qOwnedByBody(swBody, EntityType.EDGE);

    // Create dummy spline for sweep planes
    opFitSpline(context, id + "dummySpline", {
        "points" : [
            vector(swExtents.minCorner[0] + 3 * millimeter, 0 * millimeter, 0 * millimeter),
            vector(swExtents.maxCorner[0] - 3 * millimeter, 0 * millimeter, 0 * millimeter)
        ]
    });

    var dummySpline = qCreatedBy(id + "dummySpline", EntityType.BODY);
    var insideEdges = [];
    var outsideEdges = [];
    var params = range(0, 1, 10);

    for (var i = 0; i < size(params); i += 1)
    {
        var planeLine = evEdgeTangentLine(context, {
            edge: qOwnedByBody(dummySpline, EntityType.EDGE),
            parameter: params[i]
        });

        opPlane(context, id + (i ~ "plane"), {
            "plane" : plane(planeLine.origin, planeLine.direction)
        });

        var searchPlane = qCreatedBy(id + (i ~ "plane"), EntityType.BODY);
        var evSearchPlane = evPlane(context, {
            face: qOwnedByBody(searchPlane, EntityType.FACE)
        });

        var intersectEdges = qIntersectsPlane(allEdges, evSearchPlane);
        var evIntersectEdges = evaluateQuery(context, intersectEdges);

        var lowest = {"query" : qNothing(), "zVal" : 200 * millimeter, "yVal" : 0 * millimeter};
        var secondLowest = {"query" : qNothing(), "zVal" : 200 * millimeter, "yVal" : 0 * millimeter};

        for (var edge in evIntersectEdges)
        {
            var edgeDist = evDistance(context, {
                side0: edge,
                side1: searchPlane
            });

            var zVal = edgeDist.sides[0].point[2];
            var yVal = edgeDist.sides[0].point[1];

            if (zVal <= lowest.zVal)
            {
                secondLowest = lowest;
                lowest = {"query" : edge, "zVal" : zVal, "yVal" : yVal};
            }
            else if (zVal <= secondLowest.zVal)
            {
                secondLowest = {"query" : edge, "zVal" : zVal, "yVal" : yVal};
            }
        }

        // Inside edge has minimum Y value
        var minY = min([lowest.yVal, secondLowest.yVal]);
        if (lowest.yVal == minY)
        {
            if (!any(insideEdges, function(x) { return x == lowest.query; }))
            {
                insideEdges = append(insideEdges, lowest.query);
                insideEdges = append(insideEdges, qTangentConnectedEdges(lowest.query));
            }
            if (!any(outsideEdges, function(x) { return x == secondLowest.query; }))
            {
                outsideEdges = append(outsideEdges, secondLowest.query);
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(secondLowest.query));
            }
        }
        else
        {
            if (!any(insideEdges, function(x) { return x == secondLowest.query; }))
            {
                insideEdges = append(insideEdges, secondLowest.query);
                insideEdges = append(insideEdges, qTangentConnectedEdges(secondLowest.query));
            }
            if (!any(outsideEdges, function(x) { return x == lowest.query; }))
            {
                outsideEdges = append(outsideEdges, lowest.query);
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(lowest.query));
            }
        }

        opDeleteBodies(context, id + ("deletePlane" ~ i), {"entities" : searchPlane});
    }

    opDeleteBodies(context, id + "deleteDummy", {"entities" : dummySpline});

    insideEdges = evaluateQuery(context, qUnion(insideEdges));
    outsideEdges = evaluateQuery(context, qUnion(outsideEdges));

    return {
        inside: insideEdges,
        outside: outsideEdges
    };
}

/**
 * Get top edges of sidewall (inside and outside)
 */
function getTopEdges(context is Context, id is Id, swBody is Query, swExtents is Box3d) returns map
{
    var allEdges = qOwnedByBody(swBody, EntityType.EDGE);

    // Create dummy spline for sweep planes
    opFitSpline(context, id + "dummySpline", {
        points: [
            vector(swExtents.minCorner[0] + 3 * millimeter, 0 * millimeter, 0 * millimeter),
            vector(swExtents.maxCorner[0] - 3 * millimeter, 0 * millimeter, 0 * millimeter)
        ]
    });

    var dummySpline = qCreatedBy(id + "dummySpline", EntityType.BODY);
    var insideEdges = [];
    var outsideEdges = [];
    var params = range(0, 1, 10);

    for (var i = 0; i < size(params); i += 1)
    {
        var planeLine = evEdgeTangentLine(context, {
            "edge" : qOwnedByBody(dummySpline, EntityType.EDGE),
            "parameter" : params[i]
        });

        opPlane(context, id + (i ~ "plane"), {
            "plane" : plane(planeLine.origin, planeLine.direction)
        });

        var searchPlane = qCreatedBy(id + (i ~ "plane"), EntityType.BODY);
        var evSearchPlane = evPlane(context, {
            face: qOwnedByBody(searchPlane, EntityType.FACE)
        });

        var intersectEdges = qIntersectsPlane(allEdges, evSearchPlane);
        var evIntersectEdges = evaluateQuery(context, intersectEdges);

        var highest = {"query" : qNothing(), "zVal" : -200 * millimeter, "yVal" : 0 * millimeter};
        var secondHighest = {"query" : qNothing(), "zVal" : -200 * millimeter, "yVal" : 0 * millimeter};

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
                highest = {"query" : edge, "zVal" : zVal, "yVal" : yVal};
            }
            else if (zVal >= secondHighest.zVal)
            {
                secondHighest = {"query" : edge, "zVal" : zVal, "yVal" : yVal};
            }
        }

        // Inside edge has minimum Y value
        var minY = min([highest.yVal, secondHighest.yVal]);
        if (highest.yVal == minY)
        {
            if (!any(insideEdges, function(x) { return x == highest.query; }))
            {
                insideEdges = append(insideEdges, highest.query);
                insideEdges = append(insideEdges, qTangentConnectedEdges(highest.query));
            }
            if (!any(outsideEdges, function(x) { return x == secondHighest.query; }))
            {
                outsideEdges = append(outsideEdges, secondHighest.query);
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(secondHighest.query));
            }
        }
        else
        {
            if (!any(insideEdges, function(x) { return x == secondHighest.query; }))
            {
                insideEdges = append(insideEdges, secondHighest.query);
                insideEdges = append(insideEdges, qTangentConnectedEdges(secondHighest.query));
            }
            if (!any(outsideEdges, function(x) { return x == highest.query; }))
            {
                outsideEdges = append(outsideEdges, highest.query);
                outsideEdges = append(outsideEdges, qTangentConnectedEdges(highest.query));
            }
        }

        opDeleteBodies(context, id + ("deletePlane" ~ i), {"entities" : searchPlane});
    }

    opDeleteBodies(context, id + "deleteDummy", {"entities" : dummySpline});

    insideEdges = evaluateQuery(context, qUnion(insideEdges));
    outsideEdges = evaluateQuery(context, qUnion(outsideEdges));

    return {
        inside: insideEdges,
        outside: outsideEdges
    };
}

/**
 * Create center spline by averaging inside and outside paths
 */
function getCenterSpline(context is Context, id is Id, insidePath is Path, outsidePath is Path) returns Query
{
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

    opFitSpline(context, id + "centerSpline", {
        "points" : splinePoints
    });

    return qCreatedBy(id + "centerSpline", EntityType.BODY);
}
