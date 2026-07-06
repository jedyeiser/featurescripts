FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/geomOperations.fs", version : "2878.0");
//import table types
import(path : "ff9221b7148cfda8a449abff", version : "31d87ccebeed70c9ff76b9ed");


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
// TERMINAL-STATION INSET
// ============================================================================

/**
 * Identify a body's two "last" stations: the lowest-X and highest-X stations
 * that fall within the body's X extents. These are the only stations eligible
 * for end insetting. Returns { loX, hiX } as ValueWithUnits, or undefined
 * bounds when no station lands within the body.
 */
export function terminalStationXs(stations is array, extents is Box3d) returns map
{
    var loX = undefined;
    var hiX = undefined;

    for (var s in stations)
    {
        var x = s.x;
        if (x >= extents.minCorner[0] - EDGE_MARGIN && x <= extents.maxCorner[0] + EDGE_MARGIN)
        {
            if (loX == undefined || x < loX)
            {
                loX = x;
            }
            if (hiX == undefined || x > hiX)
            {
                hiX = x;
            }
        }
    }

    return { "loX" : loX, "hiX" : hiX };
}

/**
 * Return the X at which to actually section the body for this station. Only the
 * body's two terminal stations (terminals.loX / terminals.hiX) are moved: the
 * low end inward by +inset, the high end inward by -inset, measured from the
 * body's own end cap. Every other station - and every station when the feature
 * is off (inset <= 0) or the body holds no stations - is returned unchanged.
 *
 * The offset is clamped to half the body length so it can never reach or cross
 * the far end on a very short body.
 */
export function insetTerminalX(
    stationX is ValueWithUnits,
    extents is Box3d,
    inset is ValueWithUnits,
    terminals is map) returns ValueWithUnits
{
    if (inset <= 0 * millimeter || terminals.loX == undefined)
    {
        return stationX;
    }

    var eff = min([inset, (extents.maxCorner[0] - extents.minCorner[0]) / 2]);

    // terminals.loX / hiX are exact copies of station X values, so == is safe.
    if (stationX == terminals.loX)
    {
        return extents.minCorner[0] + eff;
    }
    if (stationX == terminals.hiX)
    {
        return extents.maxCorner[0] - eff;
    }

    return stationX;
}

// ============================================================================
// GENERIC SECTION MEASUREMENT
// ============================================================================

/**
 * Measure the world-axis cross-section extents of a body at a station X.
 *
 * Sections the body with a plane at X (normal +X), collects the boundary
 * intersection points, and returns their world Y/Z bounding extents. Unlike
 * measureCoreAtStation this makes no symmetry assumption (core uses 2*maxY), so
 * it suits arbitrary solid bodies.
 *
 * Returns a map { yMin, yMax, zMin, zMax, width, height } on success, or
 * undefined when the plane finds no intersection (the body does not reach X).
 * Callers treat undefined as "no data at this station". No declared return type
 * so the undefined return is legal.
 */
export function measureSectionExtents(
    context is Context,
    bodyQuery is Query,
    stationX is ValueWithUnits)
{
    if (!isLength(stationX))
    {
        throw "stationX must be a length value";
    }

    var sectionPlane = plane(vector(stationX, 0 * millimeter, 0 * millimeter), vector(1, 0, 0));

    var bodyEdges = qOwnedByBody(bodyQuery, EntityType.EDGE);
    var crossing = evaluateQuery(context, qIntersectsPlane(bodyEdges, sectionPlane));

    if (size(crossing) == 0)
    {
        return undefined;
    }

    var points = [];
    for (var edge in crossing)
    {
        var pt = evDistance(context, {
            "side0" : sectionPlane,
            "side1" : edge
        }).sides[1].point;

        points = append(points, pt);
    }

    points = deduplicate(points);

    if (size(points) == 0)
    {
        return undefined;
    }

    var yMin = points[0][1];
    var yMax = points[0][1];
    var zMin = points[0][2];
    var zMax = points[0][2];

    // Track the actual boundary points achieving each extent, so callers can
    // mark exactly where width (Y) and height (Z) are measured.
    var yMinPoint = points[0];
    var yMaxPoint = points[0];
    var zMinPoint = points[0];
    var zMaxPoint = points[0];

    for (var pt in points)
    {
        if (pt[1] < yMin)
        {
            yMin = pt[1];
            yMinPoint = pt;
        }
        if (pt[1] > yMax)
        {
            yMax = pt[1];
            yMaxPoint = pt;
        }
        if (pt[2] < zMin)
        {
            zMin = pt[2];
            zMinPoint = pt;
        }
        if (pt[2] > zMax)
        {
            zMax = pt[2];
            zMaxPoint = pt;
        }
    }

    return {
        "yMin" : yMin,
        "yMax" : yMax,
        "zMin" : zMin,
        "zMax" : zMax,
        "width" : yMax - yMin,
        "height" : zMax - zMin,
        "yMinPoint" : yMinPoint,
        "yMaxPoint" : yMaxPoint,
        "zMinPoint" : zMinPoint,
        "zMaxPoint" : zMaxPoint
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
    verbose is boolean,
    showDebug is boolean)
{
    // NOTE: no declared return type. This function returns a measurement map on
    // success but returns undefined when a station plane finds no core
    // intersection. A declared "returns map" makes the undefined return illegal
    // and throws "Function with a return type returned undefined." Callers treat
    // undefined as "no data at this station".
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
        if (verbose)
        {
            println("CORE MISS @ X = " ~ stationX
                ~ " : plane intersects 0 core edges from "
                ~ size(evIntersectEdges) ~ " candidate edge(s) - core does not reach this X.");
        }
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
    if (showDebug)
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
 * Measure sidewall at a specific X station.
 *
 * Sections the sidewall body fresh at this station (plane normal +X) and reads
 * the intersection points directly, exactly like measureCoreAtStation does for
 * the core. This replaced an earlier approach that pre-classified top/bottom and
 * inside/outside edge sets from only ~11 sample planes inset 3 mm from each end:
 * any station landing on a sidewall edge segment those samples never captured
 * returned no data even though the body clearly reached that X. Fresh sectioning
 * removes that failure mode entirely.
 *
 * The section of a thin sidewall is a near-parallelogram with two top corners
 * (inside/outside) and two bottom corners. Splitting the points at mid-Z and
 * averaging each half reproduces the old mid-thickness top and bottom centers.
 *
 * No declared return type: returns undefined when the plane finds no section
 * (the body does not reach this X - legitimate for stations past the sidewall
 * when the core is longer). Callers treat undefined as "no data at this station".
 */
export function measureSidewallAtStation(
    context is Context,
    swData is map,
    stationX is ValueWithUnits,
    swExtents is Box3d,
    verbose is boolean,
    showDebug is boolean)
{
    var measurePlane = plane(vector(stationX, 0 * millimeter, 0 * millimeter), vector(1, 0, 0));

    var swEdges = qOwnedByBody(swData.bodies, EntityType.EDGE);
    var crossing = evaluateQuery(context, qIntersectsPlane(swEdges, measurePlane));

    if (size(crossing) == 0)
    {
        if (verbose)
        {
            var withinExtents = stationX >= swExtents.minCorner[0] - EDGE_MARGIN
                && stationX <= swExtents.maxCorner[0] + EDGE_MARGIN;
            println("SW MISS @ X = " ~ stationX
                ~ " : plane intersects 0 sidewall edges."
                ~ " Within SW extents [" ~ swExtents.minCorner[0] ~ ", " ~ swExtents.maxCorner[0] ~ "]? " ~ withinExtents
                ~ (withinExtents ? "  <-- UNEXPECTED: body reaches this X" : "  (expected: past sidewall end)"));
        }
        return undefined;
    }

    var points = [];
    for (var edge in crossing)
    {
        var pt = evDistance(context, {
            "side0" : measurePlane,
            "side1" : edge
        }).sides[1].point;
        points = append(points, pt);
    }
    points = deduplicate(points);

    if (size(points) < 2)
    {
        if (verbose)
        {
            println("SW MISS @ X = " ~ stationX
                ~ " : only " ~ size(points) ~ " section point(s) after dedup from "
                ~ size(crossing) ~ " crossing edge(s) - section too degenerate to measure.");
        }
        return undefined;
    }

    // Top/bottom split by mid-Z, then average each half so the centers sit at
    // mid-thickness (inside/outside corners averaged), matching the old result.
    var maxZ = points[0][2];
    var minZ = points[0][2];
    for (var pt in points)
    {
        if (pt[2] > maxZ)
        {
            maxZ = pt[2];
        }
        if (pt[2] < minZ)
        {
            minZ = pt[2];
        }
    }

    var midZ = (maxZ + minZ) / 2;
    var topPoints = filter(points, function(p) { return p[2] >= midZ; });
    var bottomPoints = filter(points, function(p) { return p[2] < midZ; });

    var topPoint = average(topPoints);
    var bottomPoint = average(bottomPoints);

    var swHeight = topPoint[2] - bottomPoint[2];

    if (showDebug)
    {
        addDebugPoint(context, bottomPoint, DebugColor.MAGENTA);
        addDebugPoint(context, topPoint, DebugColor.GREEN);
    }

    return {
        "swHeight" : swHeight,
        "swBottomZ" : bottomPoint[2],
        "swTopZ" : topPoint[2],
        "bottomCenter" : bottomPoint,
        "topCenter" : topPoint
    };
}
