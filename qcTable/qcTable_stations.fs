FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
//import qcTable_types
import(path : "ff9221b7148cfda8a449abff", version : "1e352b8a8849d91676fd9521");
//import qcTable_geometry
import(path : "0f9cf9b21a3c654880d3167c", version : "d04be9c925d28ec09fb9b6b5");


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

    // Signed running-surface length. Negative when ACP is at a smaller X than
    // FCP (part modeled in mirrored X orientation) - that orientation is allowed.
    var rsl = acpX - fcpX;

    if (abs(rsl) < GEOM_TOL)
    {
        throw "FCP and ACP cannot be at the same X position";
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
export function extractXPosition(context is Context, query is Query, label is string) returns ValueWithUnits
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

    // Try mate connector (its own body type). A mate connector is a BODY, so
    // this must be checked before the generic BODY (construction plane) branch
    // below, which assumes the body owns faces.
    if (size(evaluateQuery(context, qBodyType(entity, BodyType.MATE_CONNECTOR))) > 0)
    {
        var cs = evMateConnector(context, {"mateConnector" : entity});
        return cs.origin[0];
    }

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

    throw label ~ " must be a vertex, mate connector, plane, or planar face";
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
        x: boundaries.fcp + boundaries.rsl / 2,
        callout: CALLOUT_MRS,
        preferred: true
    });

    stations = append(stations, {
        x: boundaries.fcp + 3 * boundaries.rsl / 4,
        callout: CALLOUT_XS2,
        preferred: true
    });

    stations = append(stations, {
        x: boundaries.acp,
        callout: CALLOUT_ACP,
        preferred: true
    });

    // 2. Add body endpoints if present. Tip is the FCP (forward) side and tail
    //    the ACP (aft) side; when ACP is at smaller X the part is mirrored, so
    //    the tip is the max-X extent and the tail the min-X extent.
    var tailIsMaxX = boundaries.rsl >= 0 * millimeter;

    if (coreExtents != undefined)
    {
        var coreTipX = tailIsMaxX ? coreExtents.minCorner[0] : coreExtents.maxCorner[0];
        var coreTailX = tailIsMaxX ? coreExtents.maxCorner[0] : coreExtents.minCorner[0];

        stations = append(stations, {
            x: coreTipX,
            callout: CALLOUT_CORE_TIP,
            preferred: false
        });

        stations = append(stations, {
            x: coreTailX,
            callout: CALLOUT_CORE_TAIL,
            preferred: false
        });
    }

    if (swExtents != undefined)
    {
        var swTipX = tailIsMaxX ? swExtents.minCorner[0] : swExtents.maxCorner[0];
        var swTailX = tailIsMaxX ? swExtents.maxCorner[0] : swExtents.minCorner[0];

        stations = append(stations, {
            x: swTipX,
            callout: CALLOUT_SW_TIP,
            preferred: false
        });

        stations = append(stations, {
            x: swTailX,
            callout: CALLOUT_SW_TAIL,
            preferred: false
        });
    }

    // 3. Add intermediate stations based on spacing method
    stations = addIntermediateStations(stations, definition, boundaries, coreExtents, swExtents);

    // 3.5 Optionally drop XS-1/MRS/XS-2 that don't land on a spacing station
    if (definition.removeOffGridSections)
    {
        stations = removeOffGridCrossSections(stations);
    }

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
    var startX = 0 * millimeter;
    var endX = 0 * millimeter;

    // Determine measurement range from body extents
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

    // Normalize so lo <= hi regardless of body or FCP/ACP orientation
    var lo = min([startX, endX]);
    var hi = max([startX, endX]);
    var totalLength = hi - lo;

    // MRS (stance midpoint) - orientation independent
    var mrsX = boundaries.fcp + boundaries.rsl / 2;

    if (definition.pointGeneration == POINT_TYPES.STATIC_DISTANCE)
    {
        var spacing = definition.pointDistance;

        // Grid anchor: MRS for the MRS-centered mode, or the tail (aft = ACP
        // side) for the tail mode. Points are then laid out across the whole
        // [lo, hi] range in both directions from the anchor.
        var anchorX = mrsX;
        if (definition.staticStart == START_STATIC_POINTS.TAIL)
        {
            anchorX = (boundaries.rsl >= 0 * millimeter) ? hi : lo;
        }

        var kMin = ceil((lo - anchorX) / spacing);
        var kMax = floor((hi - anchorX) / spacing);

        for (var k = kMin; k <= kMax; k += 1)
        {
            stations = append(stations, {
                "x" : anchorX + k * spacing,
                callout: '',
                preferred: false
            });
        }
    }
    else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_RSL)
    {
        // Spacing derived from the (unsigned) RSL, extended across the whole
        // body. Anchored at MRS so the grid reduces to the legacy result when
        // the stance is centered on the world origin.
        var spacing = abs(boundaries.rsl) / (definition.numEvenPoints - 1);
        var anchorX = mrsX;

        var kMin = ceil((lo - anchorX) / spacing);
        var kMax = floor((hi - anchorX) / spacing);

        for (var k = kMin; k <= kMax; k += 1)
        {
            stations = append(stations, {
                "x" : anchorX + k * spacing,
                callout: '',
                preferred: false
            });
        }
    }
    else if (definition.pointGeneration == POINT_TYPES.EVENLY_DIVIDE_LENGTH)
    {
        // Divide total body length evenly
        var spacing = totalLength / (definition.numEvenPoints - 1);

        for (var i = 0; i < definition.numEvenPoints; i += 1)
        {
            stations = append(stations, {
                "x" : lo + i * spacing,
                callout: '',
                preferred: false
            });
        }
    }

    return stations;
}

/**
 * Drop the XS-1, MRS, and XS-2 cross-section markers unless they coincide
 * (within STATION_MERGE_TOL) with a spacing-generated station. Spacing
 * stations are the only ones carrying an empty callout at this stage.
 * FCP, ACP, and body-endpoint stations are always kept.
 */
function removeOffGridCrossSections(stations is array) returns array
{
    var spacingX = [];
    for (var s in stations)
    {
        if (s.callout == '')
        {
            spacingX = append(spacingX, s.x);
        }
    }

    return filter(stations, function(s)
    {
        var isAddedSection = (s.callout == CALLOUT_XS1 || s.callout == CALLOUT_MRS || s.callout == CALLOUT_XS2);
        if (!isAddedSection)
        {
            return true;
        }

        for (var sx in spacingX)
        {
            if (abs(sx - s.x) < STATION_MERGE_TOL)
            {
                return true;
            }
        }
        return false;
    });
}

/**
 * Apply boundary behavior to filter stations outside FCP/ACP
 */
function applyBoundaryBehavior(
    stations is array,
    boundaries is map,
    behavior is BOUNDARY_BEHAVIOR) returns array
{
    // FCP/ACP may be in either X order; normalize to a low/high interval.
    var loB = min([boundaries.fcp, boundaries.acp]);
    var hiB = max([boundaries.fcp, boundaries.acp]);

    if (behavior == BOUNDARY_BEHAVIOR.IGNORE)
    {
        // Remove all stations outside FCP/ACP
        return filter(stations, function(s)
        {
            return s.x >= loB && s.x <= hiB;
        });
    }
    else if (behavior == BOUNDARY_BEHAVIOR.MINIMAL)
    {
        // Keep ALL stations between FCP and ACP (inclusive)
        var insideBoundary = filter(stations, function(s)
        {
            return s.x >= loB && s.x <= hiB;
        });

        // Add body endpoints that are outside FCP/ACP boundaries
        // These have specific callouts (individual or merged)
        for (var station in stations)
        {
            // Check if station is outside boundaries
            if (station.x < loB || station.x > hiB)
            {
                // Check if this station is a body endpoint
                var isEndpoint = (
                    station.callout == CALLOUT_CORE_TIP ||
                    station.callout == CALLOUT_CORE_TAIL ||
                    station.callout == CALLOUT_SW_TIP ||
                    station.callout == CALLOUT_SW_TAIL
                );

                // Also check for merged callouts like "CORE_TAIL/SW_TAIL"
                if (!isEndpoint && station.callout != '')
                {
                    // Check if callout contains any endpoint markers with '/' delimiter
                    var callout = station.callout;
                    isEndpoint = (
                        (callout == CALLOUT_CORE_TIP ~ "/" ~ CALLOUT_SW_TIP) ||
                        (callout == CALLOUT_SW_TIP ~ "/" ~ CALLOUT_CORE_TIP) ||
                        (callout == CALLOUT_CORE_TAIL ~ "/" ~ CALLOUT_SW_TAIL) ||
                        (callout == CALLOUT_SW_TAIL ~ "/" ~ CALLOUT_CORE_TAIL) ||
                        (callout == CALLOUT_CORE_TIP ~ "/" ~ CALLOUT_CORE_TAIL) ||
                        (callout == CALLOUT_CORE_TAIL ~ "/" ~ CALLOUT_CORE_TIP) ||
                        (callout == CALLOUT_SW_TIP ~ "/" ~ CALLOUT_SW_TAIL) ||
                        (callout == CALLOUT_SW_TAIL ~ "/" ~ CALLOUT_SW_TIP)
                    );
                }

                if (isEndpoint)
                {
                    insideBoundary = append(insideBoundary, station);
                }
            }
        }

        return insideBoundary;
    }
    else  // BOUNDARY_BEHAVIOR.NORMAL
    {
        return stations;
    }
}

/**
 * Merge stations that are within tolerance of each other
 * If core and SW end at the same X, combine into one station.
 * When a cluster contains a preferred (critical) station - FCP, ACP, MRS,
 * XS-1, XS-2 - the merged station keeps that station's exact X so reference
 * points never drift; otherwise the cluster is averaged.
 */
export function mergeCoincidentStations(stations is array) returns array
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
        var preferredX = undefined;

        for (var s in group)
        {
            if (s.callout != '')
            {
                callouts = append(callouts, s.callout);
            }
            if (s.preferred)
            {
                isPreferred = true;
                // Snap to the first preferred station encountered in the cluster
                if (preferredX == undefined)
                {
                    preferredX = s.x;
                }
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

        // Position: snap to the preferred station if the cluster has one so
        // critical references never move; otherwise use the cluster average.
        var mergedX = preferredX;
        if (mergedX == undefined)
        {
            var avgX = 0 * millimeter;
            for (var s in group)
            {
                avgX = avgX + s.x;
            }
            mergedX = avgX / size(group);
        }

        merged = append(merged, {
            x: mergedX,
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

/**
 * Relocate each enabled body's outermost (terminal) station to a phantom X: beyond
 * the body extents (OFFSET mode) or onto a picked reference (QUERY mode). The whole
 * row moves - the terminal station's world X is replaced and its already-measured
 * core/sidewall values are carried along (repeat-terminal semantics; no new rows,
 * no re-sectioning). Call this AFTER measurement so the values are available.
 *
 * The core is at least as long as the sidewall, so the core's terminals are
 * core-only rows (clean). A sidewall terminal may also carry core data - the whole
 * shared row moves, which is the accepted behavior.
 *
 * Returns { stations, core, sw }: the updated stations array and measurement maps.
 */
export function relocatePhantomEnds(
    context is Context,
    stations is array,
    coreMeasurements is map,
    swMeasurements is map,
    definition is map,
    coreExtents,
    swExtents) returns map
{
    var bundle = { "stations" : stations, "core" : coreMeasurements, "sw" : swMeasurements };

    if (coreExtents != undefined && definition.corePhantomMode != PHANTOM_MODE.OFF)
    {
        bundle = relocateBodyEnds(context, bundle, definition.corePhantomMode,
            definition.corePhantomOffset, definition.corePhantomStart, definition.corePhantomEnd, coreExtents);
    }
    if (swExtents != undefined && definition.swPhantomMode != PHANTOM_MODE.OFF)
    {
        bundle = relocateBodyEnds(context, bundle, definition.swPhantomMode,
            definition.swPhantomOffset, definition.swPhantomStart, definition.swPhantomEnd, swExtents);
    }

    return bundle;
}

// Relocate one body's lo/hi terminal stations. `offset`/`startQuery`/`endQuery`
// are only read in the branch that uses them (the others may be undefined). In
// QUERY mode the start pick moves the low-X terminal, the end pick the high-X one.
// World-X convention on purpose (phantom start = low-X end, end = high-X end), not tip/tail.
function relocateBodyEnds(context is Context, bundle is map, mode, offset, startQuery, endQuery, extents is Box3d) returns map
{
    var terminals = terminalStationXs(bundle.stations, extents);
    if (terminals.loX == undefined)
    {
        return bundle;   // no stations fall within this body
    }

    var loTarget = undefined;
    var hiTarget = undefined;
    if (mode == PHANTOM_MODE.OFFSET)
    {
        loTarget = extents.minCorner[0] - offset;
        hiTarget = extents.maxCorner[0] + offset;
    }
    else if (mode == PHANTOM_MODE.QUERY)
    {
        if (!isQueryEmpty(context, startQuery))
        {
            loTarget = extractXPosition(context, startQuery, "Phantom start");
        }
        if (!isQueryEmpty(context, endQuery))
        {
            hiTarget = extractXPosition(context, endQuery, "Phantom end");
        }
    }

    if (loTarget != undefined)
    {
        bundle = relocateStation(bundle, terminals.loX, loTarget);
    }
    if (hiTarget != undefined)
    {
        bundle = relocateStation(bundle, terminals.hiX, hiTarget);
    }

    return bundle;
}

// Move the (untagged) station whose x == oldX to newX and carry its measurements.
// The `moved` tag keeps terminalStationXs and a later pass from re-selecting it.
function relocateStation(bundle is map, oldX is ValueWithUnits, newX is ValueWithUnits) returns map
{
    var stations = bundle.stations;
    for (var i = 0; i < size(stations); i += 1)
    {
        if (stations[i].x == oldX && stations[i].phantomBody == undefined)
        {
            stations[i] = mergeMaps(stations[i], { "x" : newX, "phantomBody" : "moved" });
            break;
        }
    }

    var core = bundle.core;
    var sw = bundle.sw;
    if (core[oldX] != undefined)
    {
        core[newX] = core[oldX];
    }
    if (sw[oldX] != undefined)
    {
        sw[newX] = sw[oldX];
    }

    return { "stations" : stations, "core" : core, "sw" : sw };
}
