FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * CROSS-SECTION TRIANGULATION MODULE
 * ===================================
 * 
 * Handles:
 * - Curve grouping into closed boundaries
 * - Nesting detection (holes within perimeters)
 * - Shared point storage with deduplication
 * - Ear clipping triangulation
 * - Recursive section property calculations
 * 
 * Data Structures:
 * 
 * sectionPoints (array) - Shared point storage for entire cross-section:
 *   [{ "point2D": [x,y], "point3D": Vector }, ...]
 * 
 * group (map) - A single closed boundary:
 *   {
 *     "perimeterPointIndices": [int, ...],     // indices into sectionPoints
 *     "triangles": [[i,j,k], ...],             // indices into sectionPoints
 *     "sectionProperties": { area, centroid2D, centroid3D, Ixx, Iyy, Ixy },
 *     "subgroups": [group, ...]                // nested perimeters (holes/islands)
 *   }
 * 
 * bodyData (map) - Per-body data at a cross-section:
 *   {
 *     "bodyIdx": int,
 *     "groups": [group, ...],                  // top-level perimeters
 *     "totalSectionProperties": { ... }        // aggregate with nesting
 *   }
 */

// xSectUtils (provides POINT_DEDUP_TOL constant)
import(path : "c2c3edd39b85fde5e6062533", version : "e28c1b2ccec93ed1e9fe271b");

// =============================================================================
// CONSTANTS
// =============================================================================

// Note: Using POINT_DEDUP_TOL from xsectUtils for consistency
const POINT_TOLERANCE = POINT_DEDUP_TOL;
// Grid cell size: 10× dedup tolerance provides safety margin for spatial hashing
// Ensures duplicate points within tolerance land in same or adjacent cells
const GRID_CELL_SIZE = 10 * POINT_DEDUP_TOL;  // = 10 micrometers (1e-5 m)

// =============================================================================
// SPATIAL GRID HELPERS (Phase 4 Optimization)
// =============================================================================

/**
 * Compute spatial grid key for a 3D point.
 * Quantizes coordinates to grid cells for fast spatial lookup.
 *
 * @param point3D {Vector} : 3D point to hash
 * @param cellSize {ValueWithUnits} : Grid cell size
 * @returns {string} : Grid key "ix,iy,iz"
 */
function computeGridKey(point3D is Vector, cellSize is ValueWithUnits) returns string
{
    var ix = floor(point3D[0] / cellSize);
    var iy = floor(point3D[1] / cellSize);
    var iz = floor(point3D[2] / cellSize);
    return toString(ix) ~ "," ~ toString(iy) ~ "," ~ toString(iz);
}

// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Process curves for a single body at a single cross-section.
 * Groups curves into boundaries, detects nesting, triangulates, computes properties.
 *
 * @param bodyCurves {array} : Array of { bSplineCurve, bodyIndices }
 * @param frame {CoordSystem} : Local coordinate frame
 * @param sectionPoints {array} : Shared point storage (modified in place via return)
 * @param spatialGrid {map} : Spatial grid for O(1) point lookup
 * @returns {map} : { bodyData, sectionPoints, spatialGrid }
 */
export function processBodyCurves(bodyCurves is array, frame is CoordSystem, sectionPoints is array, spatialGrid is map) returns map
{
    if (size(bodyCurves) == 0)
    {
        return {
            "bodyData" : {
                "groups" : [],
                "totalSectionProperties" : emptySectionProperties(frame)
            },
            "sectionPoints" : sectionPoints,
            "spatialGrid" : spatialGrid
        };
    }

    // Step 1: Group curves into closed boundaries
    var curveGroups = groupCurvesIntoBoundaries(bodyCurves, POINT_TOLERANCE);

    // Step 2: Build perimeters and add points to shared storage
    var perimeterData = [];  // array of { pointIndices, points2D }

    for (var curveGroup in curveGroups)
    {
        var result = buildPerimeterFromCurveGroup(curveGroup, frame, sectionPoints, spatialGrid);
        sectionPoints = result.sectionPoints;
        spatialGrid = result.spatialGrid;
        
        if (size(result.pointIndices) >= 3)
        {
            perimeterData = append(perimeterData, {
                "pointIndices" : result.pointIndices,
                "points2D" : result.points2D
            });
        }
    }
    
    // Step 3: Detect nesting (which perimeters contain which)
    var nestedGroups = classifyNesting(perimeterData, sectionPoints, frame);
    
    // Step 4: Triangulate each group and compute properties
    var groups = [];
    for (var nestedGroup in nestedGroups)
    {
        var group = triangulateNestedGroup(nestedGroup, sectionPoints, frame);
        groups = append(groups, group);
    }
    
    // Step 5: Compute total properties for body (recursive with alternating signs)
    var totalProps = emptySectionProperties(frame);
    for (var group in groups)
    {
        var groupProps = computeNestedProperties(group, 0, sectionPoints, frame);
        totalProps = addSectionProperties(totalProps, groupProps);
    }

    // Step 6: Compute bounding box
    var boundingBox = computeSectionBoundingBox(sectionPoints);

    return {
        "bodyData" : {
            "groups" : groups,
            "totalSectionProperties" : totalProps,
            "boundingBox" : boundingBox
        },
        "sectionPoints" : sectionPoints,
        "spatialGrid" : spatialGrid
    };
}

// =============================================================================
// CURVE GROUPING
// =============================================================================

/**
 * Group curves into closed boundary loops by matching endpoints.
 *
 * Optimized: Uses boolean marking instead of array removal (O(n) vs O(n²)).
 *
 * @param curves {array} : Array of { bSplineCurve, ... }
 * @param tolerance {ValueWithUnits} : Endpoint matching tolerance
 * @returns {array} : Array of curve groups (each group is array of curves forming closed loop)
 */
function groupCurvesIntoBoundaries(curves is array, tolerance is ValueWithUnits) returns array
{
    if (size(curves) == 0)
        return [];

    var nCurves = size(curves);
    var used = makeArray(nCurves, false);  // Track which curves are already grouped
    var remainingCount = nCurves;
    var groups = [];

    while (remainingCount > 0)
    {
        // Find first unused curve
        var firstIdx = -1;
        for (var i = 0; i < nCurves; i += 1)
        {
            if (!used[i])
            {
                firstIdx = i;
                break;
            }
        }

        if (firstIdx == -1)
            break;  // Should never happen, but safety check

        // Start a new group with first unused curve
        var group = [{ "bSplineCurve" : curves[firstIdx].bSplineCurve, "reversed" : false }];
        used[firstIdx] = true;
        remainingCount -= 1;

        var chainStart = getCurveEndpoint(group[0].bSplineCurve, false);
        var chainEnd = getCurveEndpoint(group[0].bSplineCurve, true);

        // Try to extend the chain
        var changed = true;
        while (changed && remainingCount > 0)
        {
            changed = false;

            for (var i = 0; i < nCurves; i += 1)
            {
                if (used[i])
                    continue;  // Skip already-used curves

                var curve = curves[i];
                var curveStart = getCurveEndpoint(curve.bSplineCurve, false);
                var curveEnd = getCurveEndpoint(curve.bSplineCurve, true);

                // Check if curve connects to chain end
                if (norm(chainEnd - curveStart) < tolerance)
                {
                    group = append(group, { "bSplineCurve" : curve.bSplineCurve, "reversed" : false });
                    chainEnd = curveEnd;
                    used[i] = true;
                    remainingCount -= 1;
                    changed = true;
                    break;
                }
                else if (norm(chainEnd - curveEnd) < tolerance)
                {
                    group = append(group, { "bSplineCurve" : curve.bSplineCurve, "reversed" : true });
                    chainEnd = curveStart;
                    used[i] = true;
                    remainingCount -= 1;
                    changed = true;
                    break;
                }
                // Check if curve connects to chain start
                else if (norm(chainStart - curveEnd) < tolerance)
                {
                    group = insertAtIndex(group, 0, { "bSplineCurve" : curve.bSplineCurve, "reversed" : false });
                    chainStart = curveStart;
                    used[i] = true;
                    remainingCount -= 1;
                    changed = true;
                    break;
                }
                else if (norm(chainStart - curveStart) < tolerance)
                {
                    group = insertAtIndex(group, 0, { "bSplineCurve" : curve.bSplineCurve, "reversed" : true });
                    chainStart = curveEnd;
                    used[i] = true;
                    remainingCount -= 1;
                    changed = true;
                    break;
                }
            }
        }

        // Check if loop is closed
        if (norm(chainStart - chainEnd) < tolerance)
        {
            groups = append(groups, group);
        }
        else
        {
            // Open chain - still add it but note it's not closed
            // This shouldn't happen for valid cross-sections
            groups = append(groups, group);
        }
    }
    /*
    for (var g = 0; g < size(groups); g += 1)
    {
    }
    */
    return groups;
}

/**
 * Get endpoint of a B-spline curve.
 */
function getCurveEndpoint(curve is BSplineCurve, atEnd is boolean) returns Vector
{
    var cps = curve.controlPoints;
    return atEnd ? cps[size(cps) - 1] : cps[0];
}

// =============================================================================
// PERIMETER BUILDING
// =============================================================================

/**
 * Build ordered perimeter points from a curve group.
 * Adds points to shared storage with deduplication.
 *
 * @param curveGroup {array} : Array of { bSplineCurve, reversed }
 * @param frame {CoordSystem} : Local coordinate frame
 * @param sectionPoints {array} : Shared point storage
 * @param spatialGrid {map} : Spatial grid for O(1) point lookup
 * @returns {map} : { pointIndices, points2D, sectionPoints, spatialGrid }
 */
function buildPerimeterFromCurveGroup(curveGroup is array, frame is CoordSystem, sectionPoints is array, spatialGrid is map) returns map
{
    var pointIndices = [];
    var points2D = [];
    
    for (var i = 0; i < size(curveGroup); i += 1)
    {
        var curveData = curveGroup[i];
        var curve = curveData.bSplineCurve;
        var reversed = curveData.reversed == true;
        
        var curvePoints3D = sampleCurvePoints(curve);
        
        if (reversed)
        {
            curvePoints3D = reverse(curvePoints3D);
        }
        
        // Skip first point if it duplicates last added point
        var startIdx = 0;
        if (size(pointIndices) > 0 && size(curvePoints3D) > 0)
        {
            var lastIdx = pointIndices[size(pointIndices) - 1];
            var lastPt3D = sectionPoints[lastIdx].point3D;
            if (norm(curvePoints3D[0] - lastPt3D) < POINT_TOLERANCE)
            {
                startIdx = 1;
            }
        }
        
        for (var j = startIdx; j < size(curvePoints3D); j += 1)
        {
            var pt3D = curvePoints3D[j];
            var pt2D = worldToFrame2D(pt3D, frame);

            var result = addOrGetPointIndex(sectionPoints, pt2D, pt3D, POINT_TOLERANCE, spatialGrid);
            sectionPoints = result.sectionPoints;
            spatialGrid = result.spatialGrid;

            pointIndices = append(pointIndices, result.index);
            points2D = append(points2D, pt2D);
        }
    }
    
    // Check if last point duplicates first (closed loop)
    if (size(pointIndices) > 1 && pointIndices[size(pointIndices) - 1] == pointIndices[0])
    {
        pointIndices = subArray(pointIndices, 0, size(pointIndices) - 1);
        points2D = subArray(points2D, 0, size(points2D) - 1);
    }
    
    return {
        "pointIndices" : pointIndices,
        "points2D" : points2D,
        "sectionPoints" : sectionPoints,
        "spatialGrid" : spatialGrid
    };
    
}

/**
 * Sample N points on curve (N = number of control points).
 * Endpoints are taken directly; interior points are evaluated.
 */
function sampleCurvePoints(curve is BSplineCurve) returns array
{
    var cps = curve.controlPoints;
    var n = size(cps);
    
    if (n <= 2)
    {
        return cps;
    }
    
    var points = [cps[0]];
    
    var knots = curve.knots;
    var uMin = knots[0];
    var uMax = knots[size(knots) - 1];
    
    var interiorParams = [];
    for (var j = 1; j < n - 1; j += 1)
    {
        interiorParams = append(interiorParams, uMin + (j / (n - 1)) * (uMax - uMin));
    }
    
    var interiorPoints = evaluateSpline({ "spline" : curve, "parameters" : interiorParams })[0];
    points = concatenateArrays(points, interiorPoints);
    
    points = append(points, cps[n - 1]);
    
    return points;
}

// =============================================================================
// SHARED POINT STORAGE
// =============================================================================

/**
 * Add point to shared storage or get existing index if duplicate.
 * Uses spatial grid for O(1) average-case lookup instead of O(n).
 *
 * @param sectionPoints {array} : Shared point storage
 * @param point2D {array} : [x, y] local coordinates
 * @param point3D {Vector} : World coordinates
 * @param tolerance {ValueWithUnits} : Deduplication tolerance
 * @param spatialGrid {map} : Spatial grid map (gridKey -> array of point indices)
 * @returns {map} : { sectionPoints, spatialGrid, index }
 */
export function addOrGetPointIndex(sectionPoints is array, point2D is array, point3D is Vector,
                                    tolerance is ValueWithUnits, spatialGrid is map) returns map
{
    // Compute grid key for this point
    var gridKey = computeGridKey(point3D, GRID_CELL_SIZE);

    // Check only points in this grid cell (O(1) average case vs O(n) linear search)
    var cellIndices = spatialGrid[gridKey];
    if (cellIndices == undefined)
        cellIndices = [];

    // Check existing points in cell
    for (var idx in cellIndices)
    {
        var existing = sectionPoints[idx];
        if (norm(existing.point3D - point3D) < tolerance)
        {
            return { "sectionPoints" : sectionPoints, "spatialGrid" : spatialGrid, "index" : idx };
        }
    }

    // Add new point
    var newIndex = size(sectionPoints);
    sectionPoints = append(sectionPoints, {
        "point2D" : point2D,
        "point3D" : point3D
    });

    // Update spatial grid
    cellIndices = append(cellIndices, newIndex);
    spatialGrid[gridKey] = cellIndices;

    return { "sectionPoints" : sectionPoints, "spatialGrid" : spatialGrid, "index" : newIndex };
}

// =============================================================================
// NESTING DETECTION
// =============================================================================

/**
 * Classify perimeters into nested hierarchy (which contains which).
 * 
 * @param perimeterData {array} : Array of { pointIndices, points2D }
 * @param sectionPoints {array} : Shared point storage
 * @param frame {CoordSystem} : Local coordinate frame
 * @returns {array} : Array of nested group structures (top-level only, children in subgroups)
 */
function classifyNesting(perimeterData is array, sectionPoints is array, frame is CoordSystem) returns array
{
    var n = size(perimeterData);
    
    if (n == 0)
        return [];
    
    if (n == 1)
    {
        return [{
            "pointIndices" : perimeterData[0].pointIndices,
            "points2D" : perimeterData[0].points2D,
            "subgroupData" : []
        }];
    }
    
    // Compute centroid for each perimeter
    var centroids = [];
    for (var pData in perimeterData)
    {
        var centroid = computePolygonCentroid2D(pData.points2D);
        centroids = append(centroids, centroid);
    }
    
    // Determine parent for each perimeter (smallest containing perimeter)
    var parents = [];  // index of parent, or -1 if top-level
    var areas = [];
    
    for (var i = 0; i < n; i += 1)
    {
        areas = append(areas, abs(polygonSignedArea2D(perimeterData[i].points2D)));
    }
    
    for (var i = 0; i < n; i += 1)
    {
        var parentIdx = -1;
        var parentArea = inf * meter * meter;
        
        for (var j = 0; j < n; j += 1)
        {
            if (i == j)
                continue;
            
            // Check if centroid of i is inside perimeter j
            if (pointInPolygon2D(centroids[i], perimeterData[j].points2D))
            {
                // j contains i - is it the smallest container?
                if (areas[j] < parentArea)
                {
                    parentIdx = j;
                    parentArea = areas[j];
                }
            }
        }
        
        parents = append(parents, parentIdx);
    }
    
    // Build hierarchy recursively
    return buildNestedHierarchy(perimeterData, parents, -1);
}

/**
 * Build nested hierarchy from parent relationships.
 */
function buildNestedHierarchy(perimeterData is array, parents is array, parentIdx is number) returns array
{
    var result = [];
    
    for (var i = 0; i < size(perimeterData); i += 1)
    {
        if (parents[i] == parentIdx)
        {
            // This perimeter is a direct child of parentIdx
            var subgroupData = buildNestedHierarchy(perimeterData, parents, i);
            
            result = append(result, {
                "pointIndices" : perimeterData[i].pointIndices,
                "points2D" : perimeterData[i].points2D,
                "subgroupData" : subgroupData
            });
        }
    }
    
    return result;
}

/**
 * Compute centroid of 2D polygon.
 */
function computePolygonCentroid2D(points2D is array) returns array
{
    var n = size(points2D);
    if (n == 0)
        return [0 * meter, 0 * meter];
    
    var cx = 0 * meter;
    var cy = 0 * meter;
    
    for (var pt in points2D)
    {
        cx += pt[0];
        cy += pt[1];
    }
    
    return [cx / n, cy / n];
}

/**
 * Test if point is inside polygon using ray casting.
 */
function pointInPolygon2D(point is array, polygon is array) returns boolean
{
    var n = size(polygon);
    if (n < 3)
        return false;
    
    var px = point[0];
    var py = point[1];
    var inside = false;
    
    var j = n - 1;
    for (var i = 0; i < n; i += 1)
    {
        var xi = polygon[i][0];
        var yi = polygon[i][1];
        var xj = polygon[j][0];
        var yj = polygon[j][1];
        
        // Check if ray from point crosses edge
        if (((yi > py) != (yj > py)) && (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
        {
            inside = !inside;
        }
        
        j = i;
    }
    
    return inside;
}

// =============================================================================
// TRIANGULATION
// =============================================================================

/**
 * Triangulate a nested group structure.
 */
function triangulateNestedGroup(nestedGroup is map, sectionPoints is array, frame is CoordSystem) returns map
{
    var pointIndices = nestedGroup.pointIndices;
    var points2D = nestedGroup.points2D;
    
    // Handle degenerate perimeters
    if (size(pointIndices) < 3)
    {
        // Still process subgroups even if this perimeter is degenerate
        var subgroups = [];
        for (var subgroupData in nestedGroup.subgroupData)
        {
            var subgroup = triangulateNestedGroup(subgroupData, sectionPoints, frame);
            subgroups = append(subgroups, subgroup);
        }
        
        return {
            "perimeterPointIndices" : pointIndices,
            "triangles" : [],
            "sectionProperties" : emptySectionProperties(frame),
            "subgroups" : subgroups
        };
    }
    
    // Ensure CCW winding for outer perimeter
    if (polygonSignedArea2D(points2D) < 0 * meter * meter)
    {
        pointIndices = reverse(pointIndices);
        points2D = reverse(points2D);
    }
    
    // Triangulate this perimeter
    var triangles = earClipTriangulate(points2D, pointIndices);
    
    // Compute section properties for this perimeter only
    var sectionProperties = computeSectionPropertiesFromTriangles(triangles, sectionPoints, frame);
    
    // Process subgroups recursively
    var subgroups = [];
    for (var subgroupData in nestedGroup.subgroupData)
    {
        var subgroup = triangulateNestedGroup(subgroupData, sectionPoints, frame);
        subgroups = append(subgroups, subgroup);
    }
    
    return {
        "perimeterPointIndices" : pointIndices,
        "triangles" : triangles,
        "sectionProperties" : sectionProperties,
        "subgroups" : subgroups
    };
}

/**
 * Ear clipping triangulation.
 * 
 * @param points2D {array} : Local 2D coordinates
 * @param pointIndices {array} : Indices into sectionPoints
 * @returns {array} : Array of [i, j, k] triangles (indices into sectionPoints)
 */
export function earClipTriangulate(points2D is array, pointIndices is array) returns array
{
    var n = size(points2D);
    
    if (n < 3)
        return [];
    
    if (n == 3)
        return [[pointIndices[0], pointIndices[1], pointIndices[2]]];
    
    // Store original polygon for diagonal check
    var originalPolygon = points2D;
    
    // Build working index list (local indices into points2D/pointIndices)
    var localIndices = [];
    for (var i = 0; i < n; i += 1)
    {
        localIndices = append(localIndices, i);
    }
    
    var triangles = [];
    
    while (size(localIndices) > 3)
    {
        var earFound = false;
        var numLocal = size(localIndices);
        
        for (var i = 0; i < numLocal; i += 1)
        {
            var iPrev = (i - 1 + numLocal) % numLocal;
            var iNext = (i + 1) % numLocal;
            
            var localPrev = localIndices[iPrev];
            var localCurr = localIndices[i];
            var localNext = localIndices[iNext];
            
            var pPrev = points2D[localPrev];
            var pCurr = points2D[localCurr];
            var pNext = points2D[localNext];
            
            // Check 1: Is vertex convex?
            if (!isConvexVertex2D(pPrev, pCurr, pNext))
                continue;
            
            // Check 2: Does triangle contain any other vertex?
            if (triangleContainsAnyVertex(pPrev, pCurr, pNext, points2D, localIndices, i))
                continue;
            
            // Check 3: Is diagonal inside the original polygon?
            if (!isDiagonalInsidePolygon(pPrev, pNext, originalPolygon))
                continue;
            
            // Valid ear - clip it
            triangles = append(triangles, [
                pointIndices[localPrev],
                pointIndices[localCurr],
                pointIndices[localNext]
            ]);
            localIndices = removeIndex(localIndices, i);
            earFound = true;
            break;
        }
        
        if (!earFound)
        {
            // Check if remaining vertices are all collinear (degenerate case)
            var allCollinear = true;
            var collinearTol = 1e-12 * meter * meter;
            
            for (var i = 0; i < numLocal; i += 1)
            {
                var iPrev = (i - 1 + numLocal) % numLocal;
                var iNext = (i + 1) % numLocal;
                var pPrev = points2D[localIndices[iPrev]];
                var pCurr = points2D[localIndices[i]];
                var pNext = points2D[localIndices[iNext]];
                
                var cross = (pCurr[0] - pPrev[0]) * (pNext[1] - pCurr[1]) - (pCurr[1] - pPrev[1]) * (pNext[0] - pCurr[0]);
                
                if (abs(cross) > collinearTol)
                {
                    allCollinear = false;
                    break;
                }
            }
            
            if (allCollinear)
            {
                // Degenerate case: fan triangulate remaining vertices
                while (size(localIndices) > 2)
                {
                    triangles = append(triangles, [
                        pointIndices[localIndices[0]],
                        pointIndices[localIndices[1]],
                        pointIndices[localIndices[2]]
                    ]);
                    localIndices = removeIndex(localIndices, 1);
                }
                break;
            }
            else
            {
                break;
            }
        }
    }
    
    // Final triangle
    if (size(localIndices) == 3)
    {
        triangles = append(triangles, [
            pointIndices[localIndices[0]],
            pointIndices[localIndices[1]],
            pointIndices[localIndices[2]]
        ]);
    }
    
    // Debug output
    if (size(triangles) > 0)
    {
        var expected = size(points2D) - 2;
        if (size(triangles) != expected)
        {
        }
    }
    
    return triangles;
}


/**
 * Check if vertex forms convex angle (CCW winding).
 */
function isConvexVertex2D(pA is array, pB is array, pC is array) returns boolean
{
    var cross = (pB[0] - pA[0]) * (pC[1] - pB[1]) - (pB[1] - pA[1]) * (pC[0] - pB[0]);
    // Allow near-zero (collinear points are valid ears)
    return cross >= -1e-12 * meter * meter;
}

/**
 * Check if triangle contains any other vertex from the working set.
 */
function triangleContainsAnyVertex(pA is array, pB is array, pC is array, 
                                    points2D is array, localIndices is array, skipLocalIdx is number) returns boolean
{
    var numLocal = size(localIndices);
    var iPrev = (skipLocalIdx - 1 + numLocal) % numLocal;
    var iNext = (skipLocalIdx + 1) % numLocal;
    
    for (var j = 0; j < numLocal; j += 1)
    {
        if (j == iPrev || j == skipLocalIdx || j == iNext)
            continue;
        
        var testPt = points2D[localIndices[j]];
        
        if (pointInTriangle2D(testPt, pA, pB, pC))
            return true;
    }
    
    return false;
}

/**
 * Point in triangle test using barycentric coordinates.
 */
function pointInTriangle2D(p is array, a is array, b is array, c is array) returns boolean
{
    var v0x = c[0] - a[0];
    var v0y = c[1] - a[1];
    var v1x = b[0] - a[0];
    var v1y = b[1] - a[1];
    var v2x = p[0] - a[0];
    var v2y = p[1] - a[1];
    
    var dot00 = v0x * v0x + v0y * v0y;
    var dot01 = v0x * v1x + v0y * v1y;
    var dot02 = v0x * v2x + v0y * v2y;
    var dot11 = v1x * v1x + v1y * v1y;
    var dot12 = v1x * v2x + v1y * v2y;
    
    var denom = dot00 * dot11 - dot01 * dot01;
    
    if (abs(denom) < 1e-20 * meter^4)
        return false;
    
    var invDenom = 1 / denom;
    var u = (dot11 * dot02 - dot01 * dot12) * invDenom;
    var v = (dot00 * dot12 - dot01 * dot02) * invDenom;
    
    var tol = 1e-9;
    return (u > tol) && (v > tol) && (u + v < 1 - tol);
}

// =============================================================================
// SECTION PROPERTIES
// =============================================================================

/**
 * Compute section properties from triangles.
 */
function computeSectionPropertiesFromTriangles(triangles is array, sectionPoints is array, frame is CoordSystem) returns map
{
    if (size(triangles) == 0)
        return emptySectionProperties(frame);
    
    var totalArea = 0 * meter * meter;
    var sumCx = 0 * meter^3;
    var sumCy = 0 * meter^3;
    
    // First pass: area and centroid
    for (var tri in triangles)
    {
        var a = sectionPoints[tri[0]].point2D;
        var b = sectionPoints[tri[1]].point2D;
        var c = sectionPoints[tri[2]].point2D;
        
        var triArea = triangleArea2D(a, b, c);
        var triCx = (a[0] + b[0] + c[0]) / 3;
        var triCy = (a[1] + b[1] + c[1]) / 3;
        
        totalArea += triArea;
        sumCx += triArea * triCx;
        sumCy += triArea * triCy;
    }
    
    if (abs(totalArea) < 1e-20 * meter * meter)
        return emptySectionProperties(frame);
    
    var centroidX = sumCx / totalArea;
    var centroidY = sumCy / totalArea;
    
    // Second pass: moments about centroid
    var Ixx = 0 * meter^4;
    var Iyy = 0 * meter^4;
    var Ixy = 0 * meter^4;
    
    for (var tri in triangles)
    {
        var a = sectionPoints[tri[0]].point2D;
        var b = sectionPoints[tri[1]].point2D;
        var c = sectionPoints[tri[2]].point2D;
        
        var ax = a[0] - centroidX;
        var ay = a[1] - centroidY;
        var bx = b[0] - centroidX;
        var by = b[1] - centroidY;
        var cx = c[0] - centroidX;
        var cy = c[1] - centroidY;
        
        var triArea = triangleArea2D(a, b, c);
        
        Ixx += (triArea / 6) * (ay*ay + ay*by + by*by + ay*cy + by*cy + cy*cy);
        Iyy += (triArea / 6) * (ax*ax + ax*bx + bx*bx + ax*cx + bx*cx + cx*cx);
        Ixy += (triArea / 12) * (ax*(2*ay + by + cy) + bx*(ay + 2*by + cy) + cx*(ay + by + 2*cy));
    }
    
    var centroid3D = frame2DToWorld([centroidX, centroidY], frame);
    
    return {
        "area" : abs(totalArea),
        "centroid2D" : [centroidX, centroidY],
        "centroid3D" : centroid3D,
        "Ixx" : abs(Ixx),
        "Iyy" : abs(Iyy),
        "Ixy" : Ixy
    };
}

/**
 * Compute nested section properties with alternating signs.
 * Level 0 (outer): +
 * Level 1 (holes): -
 * Level 2 (islands in holes): +
 * etc.
 */
export function computeNestedProperties(group is map, depth is number, sectionPoints is array, frame is CoordSystem) returns map
{
    var sign = (depth % 2 == 0) ? 1 : -1;
    
    var result = scaleSectionProperties(group.sectionProperties, sign);
    
    for (var subgroup in group.subgroups)
    {
        var subProps = computeNestedProperties(subgroup, depth + 1, sectionPoints, frame);
        result = addSectionProperties(result, subProps);
    }
    
    return result;
}

/**
 * Scale section properties by a factor.
 */
function scaleSectionProperties(props is map, factor is number) returns map
{
    return {
        "area" : props.area * factor,
        "centroid2D" : props.centroid2D,
        "centroid3D" : props.centroid3D,
        "Ixx" : props.Ixx * factor,
        "Iyy" : props.Iyy * factor,
        "Ixy" : props.Ixy * factor
    };
}

/**
 * Add two section properties together.
 * Note: Centroid calculation is simplified (uses first non-zero centroid).
 * For accurate combined centroid, use area-weighted average.
 */
function addSectionProperties(a is map, b is map) returns map
{
    var totalArea = a.area + b.area;
    
    // Area-weighted centroid
    var centroid2D = a.centroid2D;
    var centroid3D = a.centroid3D;
    
    if (abs(totalArea) > 1e-20 * meter * meter)
    {
        var cx = (a.area * a.centroid2D[0] + b.area * b.centroid2D[0]) / totalArea;
        var cy = (a.area * a.centroid2D[1] + b.area * b.centroid2D[1]) / totalArea;
        centroid2D = [cx, cy];
        
        // Note: centroid3D should be recalculated from frame if needed
        // For now, use weighted average of 3D centroids
        centroid3D = (a.area / totalArea) * a.centroid3D + (b.area / totalArea) * b.centroid3D;
    }
    
    return {
        "area" : totalArea,
        "centroid2D" : centroid2D,
        "centroid3D" : centroid3D,
        "Ixx" : a.Ixx + b.Ixx,
        "Iyy" : a.Iyy + b.Iyy,
        "Ixy" : a.Ixy + b.Ixy
    };
}

/**
 * Empty section properties.
 */
export function emptySectionProperties(frame is CoordSystem) returns map
{
    return {
        "area" : 0 * meter * meter,
        "centroid2D" : [0 * meter, 0 * meter],
        "centroid3D" : frame.origin,
        "Ixx" : 0 * meter^4,
        "Iyy" : 0 * meter^4,
        "Ixy" : 0 * meter^4
    };
}

/**
 * Compute 2D bounding box from section points.
 *
 * @param points {array} : Array of {point2D, point3D} maps
 * @returns {map} : { minX, maxX, minY, maxY, width, height }
 */
export function computeSectionBoundingBox(points is array) returns map
{
    if (size(points) == 0)
    {
        return {
            "minX" : 0,
            "maxX" : 0,
            "minY" : 0,
            "maxY" : 0,
            "width" : 0 * meter,
            "height" : 0 * meter
        };
    }

    var minX = points[0].point2D[0];
    var maxX = points[0].point2D[0];
    var minY = points[0].point2D[1];
    var maxY = points[0].point2D[1];

    for (var pt in points)
    {
        var x = pt.point2D[0];
        var y = pt.point2D[1];

        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
    }

    return {
        "minX" : minX,
        "maxX" : maxX,
        "minY" : minY,
        "maxY" : maxY,
        "width" : (maxX - minX),
        "height" : (maxY - minY)
    };
}

// =============================================================================
// GEOMETRY UTILITIES
// =============================================================================

/**
 * Triangle area from 2D vertices.
 */
export function triangleArea2D(a is array, b is array, c is array) returns ValueWithUnits
{
    return abs((b[0] - a[0]) * (c[1] - a[1]) - (c[0] - a[0]) * (b[1] - a[1])) / 2;
}

/**
 * Signed area of polygon (positive = CCW).
 */
export function polygonSignedArea2D(points2D is array) returns ValueWithUnits
{
    var n = size(points2D);
    var area = 0 * meter * meter;
    
    for (var i = 0; i < n; i += 1)
    {
        var j = (i + 1) % n;
        area += points2D[i][0] * points2D[j][1];
        area -= points2D[j][0] * points2D[i][1];
    }
    
    return area / 2;
}

// =============================================================================
// COORDINATE FRAME UTILITIES
// =============================================================================

/**
 * Convert 3D world point to 2D local frame coordinates.
 */
export function worldToFrame2D(point3D is Vector, frame is CoordSystem) returns array
{
    var local = point3D - frame.origin;
    var x = dot(local, frame.xAxis);
    var y = dot(local, yAxis(frame));
    return [x, y];
}

/**
 * Convert 2D local frame coordinates to 3D world point.
 */
export function frame2DToWorld(point2D is array, frame is CoordSystem) returns Vector
{
    return frame.origin + point2D[0] * frame.xAxis + point2D[1] * yAxis(frame);
}

// =============================================================================
// ARRAY HELPERS
// =============================================================================

/**
 * Remove element at index from array.
 */
function removeIndex(arr is array, index is number) returns array
{
    var result = [];
    for (var i = 0; i < size(arr); i += 1)
    {
        if (i != index)
            result = append(result, arr[i]);
    }
    return result;
}

/**
 * Insert element at index in array.
 */
function insertAtIndex(arr is array, index is number, element) returns array
{
    var result = [];
    for (var i = 0; i < size(arr); i += 1)
    {
        if (i == index)
            result = append(result, element);
        result = append(result, arr[i]);
    }
    if (index >= size(arr))
        result = append(result, element);
    return result;
}

/**
 * Check if an ear's diagonal lies inside the polygon.
 * The diagonal is the edge from pPrev to pNext.
 */
function isDiagonalInsidePolygon(pPrev is array, pNext is array, originalPolygon is array) returns boolean
{
    // Midpoint of the diagonal
    var midX = (pPrev[0] + pNext[0]) / 2;
    var midY = (pPrev[1] + pNext[1]) / 2;
    var midpoint = [midX, midY];
    
    return pointInPolygon2D(midpoint, originalPolygon);
}
