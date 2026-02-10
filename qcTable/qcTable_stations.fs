FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
//import qcTable_types
import(path : "ff9221b7148cfda8a449abff", version : "55fa348279ead98a1ba723b9");


/**
 * QC Table Station Generation
 *
 * Handles generation of measurement stations based on FCP/ACP boundaries,
 * body extents, and user-specified spacing methods.
 */

// ============================================================================
// FCP/ACP EXTRACTION
// ============================================================================

/**
 * Extract FCP and ACP X positions from user queries
 * Handles: vertices, mate connectors, planes, planar faces
 */
export function extractFCPACP(context is Context, fcpQuery is Query, acpQuery is Query) returns map
{
    var fcpX = extractXPosition(context, fcpQuery, "FCP");
    var acpX = extractXPosition(context, acpQuery, "ACP");

    var rsl = acpX - fcpX;

    if (rsl <= 0 * millimeter)
    {
        throw "ACP must be forward of FCP (positive RSL)";
    }

    return {
        fcp: fcpX,
        acp: acpX,
        "rsl" : rsl
    };
}

/**
 * Extract X position from a query (vertex, mate connector, plane, or planar face)
 */
function extractXPosition(context is Context, query is Query, label is string) returns ValueWithUnits
{
    if (isQueryEmpty(context, query))
    {
        throw label ~ " reference is required";
    }

    var entities = evaluateQuery(context, query);

    if (size(entities) == 0)
    {
        throw label ~ " reference is required";
    }

    if (size(entities) > 1)
    {
        throw label ~ " reference must be a single entity";
    }

    var entity = entities[0];
    var entityType = evaluateQuery(context, qEntityFilter(entity, EntityType.VERTEX));

    // Try vertex
    if (size(entityType) > 0)
    {
        var point = evVertexPoint(context, {vertex: entity});
        return point[0];
    }

    // Try face (planar)
    entityType = evaluateQuery(context, qEntityFilter(entity, EntityType.FACE));
    if (size(entityType) > 0)
    {
        var plane = evPlane(context, {face: entity});
        return plane.origin[0];
    }

    // Try body (construction plane)
    entityType = evaluateQuery(context, qEntityFilter(entity, EntityType.BODY));
    if (size(entityType) > 0)
    {
        // Assume it's a construction plane
        var faces = evaluateQuery(context, qOwnedByBody(entity, EntityType.FACE));
        if (size(faces) > 0)
        {
            var plane = evPlane(context, {face: faces[0]});
            return plane.origin[0];
        }
    }

    throw label ~ " must be a vertex, plane, or planar face";
}

// ============================================================================
// STATION GENERATION
// ============================================================================

/**
 * Generate complete station array based on all inputs
 */
export function generateStations(
    context is Context,
    definition is map,
    boundaries is map,
    coreExtents,
    swExtents) returns array
{
    var stations = [];

    // 1. Add critical stations (FCP, XS1, MRS, XS2, ACP)
    stations = append(stations, {
        x: boundaries.fcp,
        callout: CALLOUT_FCP,
        preferred: true
    });

    stations = append(stations, {
        x: boundaries.fcp + boundaries.rsl / 4,
        callout: CALLOUT_XS1,
        preferred: true
    });

    stations = append(stations, {
        x: 0 * millimeter,
        callout: CALLOUT_MRS,
        preferred: true
    });

    stations = append(stations, {
        x: boundaries.rsl / 4,
        callout: CALLOUT_XS2,
        preferred: true
    });

    stations = append(stations, {
        x: boundaries.acp,
        callout: CALLOUT_ACP,
        preferred: true
    });

    // 2. Add body endpoints if present
    if (coreExtents != undefined)
    {
        stations = append(stations, {
            x: coreExtents.minCorner[0],
            callout: CALLOUT_CORE_TIP,
            preferred: false
        });

        stations = append(stations, {
            x: coreExtents.maxCorner[0],
            callout: CALLOUT_CORE_TAIL,
            preferred: false
        });
    }

    if (swExtents != undefined)
    {
        stations = append(stations, {
            x: swExtents.minCorner[0],
            callout: CALLOUT_SW_TIP,
            preferred: false
        });

        stations = append(stations, {
            x: swExtents.maxCorner[0],
            callout: CALLOUT_SW_TAIL,
            preferred: false
        });
    }

    // 3. Add intermediate stations based on spacing method
    stations = addIntermediateStations(stations, definition, boundaries, coreExtents, swExtents);

    // 4. Apply boundary behavior (IGNORE/MINIMAL/NORMAL)
    stations = applyBoundaryBehavior(stations, boundaries, definition.boundaryBehavior);

    // 5. Merge coincident stations
    stations = mergeCoincidentStations(stations);

    // 6. Sort by X position
    stations = sort(stations, function(a, b) { return a.x - b.x; });

    return stations;
}

/**
 * Add intermediate measurement stations based on user spacing method
 */
function addIntermediateStations(
    stations is array,
    definition is map,
    boundaries is map,
    coreExtents,
    swExtents) returns array
{
    var spacing = 100 * millimeter;  // Default (will be overridden)
    var startX = 0 * millimeter;
    var endX = 0 * millimeter;

    // Determine measurement range
    if (coreExtents != undefined && swExtents != undefined)
    {
        // Both present - use combined range
        startX = min([coreExtents.minCorner[0], swExtents.minCorner[0]]);
        endX = max([coreExtents.maxCorner[0], swExtents.maxCorner[0]]);
    }
    else if (coreExtents != undefined)
    {
        // Core only
        startX = coreExtents.minCorner[0];
        endX = coreExtents.maxCorner[0];
    }
    else if (swExtents != undefined)
    {
        // SW only
        startX = swExtents.minCorner[0];
        endX = swExtents.maxCorner[0];
    }
    else
    {
        // Neither - just use FCP/ACP
        startX = boundaries.fcp;
        endX = boundaries.acp;
    }

    var totalLength = endX - startX;

    // Calculate spacing based on method
    if (definition.pointGeneration == POINT_TYPES.STATIC_DISTANCE)
    {
        spacing = definition.pointDistance;

        if (definition.staticStart == START_STATIC_POINTS.MRS)
        {
            // Start from MRS, work outward in both directions
            var numTailPoints = floor(endX / spacing);
            var numTipPoints = floor(abs(startX) / spacing);

            for (var i = -1 * numTipPoints; i <= numTailPoints; i += 1)
            {
                var x = i * spacing;
                if (x >= startX && x <= endX)
                {
                    stations = append(stations, {
                        "x" : x,
                        callout: '',
                        preferred: false
                    });
                }
            }
        }
        else  // START_STATIC_POINTS.TAIL
        {
            // Start from tail, work toward tip
            var numPoints = ceil(totalLength / spacing);

            for (var i = 0; i <= numPoints; i += 1)
            {
                var x = endX - i * spacing;
                if (x >= startX && x <= endX)
                {
                    stations = append(stations, {
                        "x" : x,
                        callout: '',
                        preferred: false
                    });
                }
            }
        }
    }
    else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_RSL)
    {
        // Divide RSL evenly, then extend pattern outside FCP/ACP
        spacing = boundaries.rsl / (definition.numEvenPoints - 1);

        var numTailPoints = floor(endX / spacing);
        var numTipPoints = floor(abs(startX) / spacing);

        for (var i = -1 * numTipPoints; i <= numTailPoints; i += 1)
        {
            var x = i * spacing;
            if (x >= startX && x <= endX)
            {
                stations = append(stations, {
                    "x" : x,
                    callout: '',
                    preferred: false
                });
            }
        }
    }
    else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_LENGTH)
    {
        // Divide total body length evenly
        spacing = totalLength / (definition.numEvenPoints - 1);

        for (var i = 0; i < definition.numEvenPoints; i += 1)
        {
            var x = startX + i * spacing;
            stations = append(stations, {
                "x" : x,
                callout: '',
                preferred: false
            });
        }
    }

    return stations;
}

/**
 * Apply boundary behavior to filter stations outside FCP/ACP
 */
function applyBoundaryBehavior(
    stations is array,
    boundaries is map,
    behavior is BOUNDARY_BEHAVIOR) returns array
{
    if (behavior == BOUNDARY_BEHAVIOR.IGNORE)
    {
        // Remove all stations outside FCP/ACP
        return filter(stations, function(s)
        {
            return s.x >= boundaries.fcp && s.x <= boundaries.acp;
        });
    }
    else if (behavior == BOUNDARY_BEHAVIOR.MINIMAL)
    {
        // Keep preferred stations + one tip/tail per body type
        var filtered = filter(stations, function(s) { return s.preferred; });

        // Add core tips if present
        var coreTips = filter(stations, function(s)
        {
            return s.callout == CALLOUT_CORE_TIP || s.callout == CALLOUT_CORE_TAIL;
        });

        for (var tip in coreTips)
        {
            filtered = append(filtered, tip);
        }

        // Add SW tips if present
        var swTips = filter(stations, function(s)
        {
            return s.callout == CALLOUT_SW_TIP || s.callout == CALLOUT_SW_TAIL;
        });

        for (var tip in swTips)
        {
            filtered = append(filtered, tip);
        }

        return filtered;
    }
    else  // BOUNDARY_BEHAVIOR.NORMAL
    {
        return stations;
    }
}

/**
 * Merge stations that are within tolerance of each other
 * If core and SW end at the same X, combine into one station
 */
function mergeCoincidentStations(stations is array) returns array
{
    if (size(stations) == 0)
    {
        return stations;
    }

    // Sort by X first
    stations = sort(stations, function(a, b) { return a.x - b.x; });

    var merged = [];
    var i = 0;

    while (i < size(stations))
    {
        var current = stations[i];
        var group = [current];

        // Find all stations within tolerance
        for (var j = i + 1; j < size(stations); j += 1)
        {
            if (abs(stations[j].x - current.x) < STATION_MERGE_TOL)
            {
                group = append(group, stations[j]);
            }
            else
            {
                break;  // No more in this group
            }
        }

        // Merge callouts from group
        var callouts = [];
        var isPreferred = false;

        for (var s in group)
        {
            if (s.callout != '')
            {
                callouts = append(callouts, s.callout);
            }
            if (s.preferred)
            {
                isPreferred = true;
            }
        }

        // Build merged callout string
        var mergedCallout = '';
        for (var k = 0; k < size(callouts); k += 1)
        {
            if (k > 0)
            {
                mergedCallout = mergedCallout ~ '/';
            }
            mergedCallout = mergedCallout ~ callouts[k];
        }

        // Use average X position
        var avgX = 0 * millimeter;
        for (var s in group)
        {
            avgX = avgX + s.x;
        }
        avgX = avgX / size(group);

        merged = append(merged, {
            x: avgX,
            callout: mergedCallout,
            preferred: isPreferred
        });

        i += size(group);
    }

    return merged;
}

// ============================================================================
// ADDITIONAL POINTS
// ============================================================================

/**
 * Add user-specified vertex points to station array
 */
export function addUserPoints(
    context is Context,
    stations is array,
    pointsQuery is Query) returns array
{
    if (isQueryEmpty(context, pointsQuery))
    {
        return stations;
    }

    var vertices = evaluateQuery(context, pointsQuery);

    for (var vertex in vertices)
    {
        var point = evVertexPoint(context, {"vertex" : vertex});

        stations = append(stations, {
            x: point[0],
            callout: '',
            preferred: false
        });
    }

    return stations;
}
