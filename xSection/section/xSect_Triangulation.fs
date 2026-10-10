FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

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
import(path : "c2c3edd39b85fde5e6062533", version : "ba11d48ccdd4952265f29ad5");

// =============================================================================
// CONSTANTS
// =============================================================================

// Note: Using POINT_DEDUP_TOL from xsectUtils for consistency
const POINT_TOLERANCE = POINT_DEDUP_TOL;
// Grid cell size: 10× dedup tolerance provides safety margin for spatial hashing
// Ensures duplicate points within tolerance land in same or adjacent cells
const GRID_CELL_SIZE = 10 * POINT_DEDUP_TOL;  // = 10 micrometers (1e-5 m)

// Loop assembly. Ends of consecutive section edges normally agree to well under a micron;
// LOOP_JOIN_TOL chains them. The kernel can omit a micron-scale edge of a cut (a 5-8 um lip
// on a 0.44 mm laminate, 2026-09-23), leaving a gap no edge closes: open chains whose nearest
// ends are within LOOP_BRIDGE_TOL are joined across it. Both are far below any real feature
// of a ski section.
const LOOP_JOIN_TOL = 2e-5 * meter;
const LOOP_BRIDGE_TOL = 2e-4 * meter;

// Duplicate section edges (a plane on a face boundary returns the edge once per face).
const DUPLICATE_CURVE_TOL = 1e-5 * meter;
const DUPLICATE_SAMPLES = 17;
// Shorter than this a section edge carries no area and only folds the outline.
const DEGENERATE_CURVE_LENGTH = POINT_DEDUP_TOL;
// Vertices of a loop tested against another to decide containment.
const NESTING_SAMPLES = 9;

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
 * @returns {map} : { bodyData, sectionPoints, spatialGrid, diagnostics }
 *   diagnostics (not stored) : { duplicatesRemoved, degenerateRemoved, maxBridge, openChains, zeroArea }
 */
export function processBodyCurves(bodyCurves is array, frame is CoordSystem, sectionPoints is array, spatialGrid is map) returns map
{
    var diagnostics = {
        "duplicatesRemoved" : 0,
        "degenerateRemoved" : 0,
        "maxBridge" : 0 * meter,
        "openChains" : 0,
        "zeroArea" : false
    };

    if (size(bodyCurves) == 0)
    {
        return {
            "bodyData" : {
                "groups" : [],
                "totalSectionProperties" : emptySectionProperties(frame)
            },
            "sectionPoints" : sectionPoints,
            "spatialGrid" : spatialGrid,
            "diagnostics" : diagnostics
        };
    }

    // Step 0: A plane lying on a face boundary returns the edge once per adjacent face, and a
    // cut can return zero-length slivers. Either makes the outline double back on itself.
    const cleaned = removeDuplicateCurves(bodyCurves);
    diagnostics.duplicatesRemoved = cleaned.duplicatesRemoved;
    diagnostics.degenerateRemoved = cleaned.degenerateRemoved;

    // Step 1: Group curves into closed boundaries
    const grouping = groupCurvesIntoBoundaries(cleaned.curves, LOOP_JOIN_TOL);
    var curveGroups = grouping.groups;
    diagnostics.maxBridge = grouping.maxBridge;
    diagnostics.openChains = grouping.openChains;

    // Step 2: Build perimeters and add points to shared storage
    var perimeterData = [];  // array of { pointIndices, points2D }
    var bodyPointIndices = [];

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
            bodyPointIndices = concatenateArrays([bodyPointIndices, result.pointIndices]);
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
    diagnostics.zeroArea = abs(totalProps.area) < 1e-12 * meter * meter;

    // Step 6: Compute bounding box from this body's own points (sectionPoints is shared by
    // every body at the station, so it would include the bodies processed before this one)
    var bodyPoints = [];
    for (var idx in bodyPointIndices)
    {
        bodyPoints = append(bodyPoints, sectionPoints[idx]);
    }
    var boundingBox = computeSectionBoundingBox(bodyPoints);

    return {
        "bodyData" : {
            "groups" : groups,
            "totalSectionProperties" : totalProps,
            "boundingBox" : boundingBox
        },
        "sectionPoints" : sectionPoints,
        "spatialGrid" : spatialGrid,
        "diagnostics" : diagnostics
    };
}

// =============================================================================
// CURVE CLEANUP
// =============================================================================

/**
 * Removes curves that repeat another curve of the same body, and zero-length slivers.
 *
 * Two curves are the same when their ends coincide (either way round) and points along one
 * lie on the other -- a LINE and a spline copy of the same edge have different control
 * points, so the representation cannot be compared.
 *
 * @returns {map} : { curves, duplicatesRemoved, degenerateRemoved }
 */
function removeDuplicateCurves(curves is array) returns map
{
    var polylines = [];
    for (var c in curves)
    {
        polylines = append(polylines, curvePolyline(c.bSplineCurve, DUPLICATE_SAMPLES));
    }

    var kept = [];
    var keptPolylines = [];
    var duplicates = 0;
    var degenerate = 0;
    for (var i = 0; i < size(curves); i += 1)
    {
        const samples = polylines[i];
        if (polylineLength(samples) < DEGENERATE_CURVE_LENGTH)
        {
            degenerate += 1;
            continue;
        }

        var repeated = false;
        for (var k = 0; k < size(kept); k += 1)
        {
            if (sameCurve(samples, keptPolylines[k]))
            {
                repeated = true;
                break;
            }
        }
        if (repeated)
        {
            duplicates += 1;
            continue;
        }
        kept = append(kept, curves[i]);
        keptPolylines = append(keptPolylines, samples);
    }
    return { "curves" : kept, "duplicatesRemoved" : duplicates, "degenerateRemoved" : degenerate };
}

/**
 * True when two sampled curves trace the same geometry: matching ends, either direction,
 * and every sample of one within tolerance of the other.
 */
function sameCurve(a is array, b is array) returns boolean
{
    const aStart = a[0];
    const aEnd = a[size(a) - 1];
    const bStart = b[0];
    const bEnd = b[size(b) - 1];
    const forward = norm(aStart - bStart) < DUPLICATE_CURVE_TOL && norm(aEnd - bEnd) < DUPLICATE_CURVE_TOL;
    const backward = norm(aStart - bEnd) < DUPLICATE_CURVE_TOL && norm(aEnd - bStart) < DUPLICATE_CURVE_TOL;
    if (!forward && !backward)
    {
        return false;
    }
    for (var p in a)
    {
        if (distanceToPolyline(p, b) > DUPLICATE_CURVE_TOL)
        {
            return false;
        }
    }
    return true;
}

/**
 * `count` points evenly spaced in parameter along a curve, both ends included.
 */
function curvePolyline(curve is BSplineCurve, count is number) returns array
{
    const knots = curve.knots;
    const uMin = knots[0];
    const uMax = knots[size(knots) - 1];
    var params = [];
    for (var j = 0; j < count; j += 1)
    {
        params = append(params, uMin + (uMax - uMin) * j / (count - 1));
    }
    return evaluateSpline({ "spline" : curve, "parameters" : params })[0];
}

function polylineLength(points is array) returns ValueWithUnits
{
    var total = 0 * meter;
    for (var j = 1; j < size(points); j += 1)
    {
        total += norm(points[j] - points[j - 1]);
    }
    return total;
}

function distanceToPolyline(p is Vector, points is array) returns ValueWithUnits
{
    var best = inf * meter;
    for (var j = 1; j < size(points); j += 1)
    {
        const seg = points[j] - points[j - 1];
        const len2 = dot(seg, seg);
        var t = 0;
        if (len2 > 0 * meter * meter)
        {
            t = clamp(dot(p - points[j - 1], seg) / len2, 0, 1);
        }
        const d = norm(p - (points[j - 1] + t * seg));
        if (d < best)
        {
            best = d;
        }
    }
    return best;
}

// =============================================================================
// CURVE GROUPING
// =============================================================================

/**
 * Group curves into closed boundary loops by matching endpoints.
 *
 * Pass 1 grows each chain by the NEAREST free endpoint within `tolerance`, at either end, and
 * stops once the chain closes -- first-match chaining could pick the wrong neighbour where
 * loops touch, and kept extending past closure. Pass 2 repairs what the kernel left open:
 * the cut can omit a micron-scale edge (seen: 5 um lips on a 0.44 mm laminate), which splits
 * one loop into open chains. The closest pair of open ends (or a chain's own two ends) is
 * joined repeatedly while the gap is under LOOP_BRIDGE_TOL. A chain still open after that is
 * closed by its chord, as before, and counted.
 *
 * @param curves {array} : Array of { bSplineCurve, ... }
 * @param tolerance {ValueWithUnits} : Endpoint matching tolerance
 * @returns {map} : { groups : array of curve groups (each an array of { bSplineCurve, reversed }),
 *                    maxBridge : largest gap closed in pass 2, openChains : chains left open }
 */
function groupCurvesIntoBoundaries(curves is array, tolerance is ValueWithUnits) returns map
{
    var nCurves = size(curves);
    if (nCurves == 0)
    {
        return { "groups" : [], "maxBridge" : 0 * meter, "openChains" : 0 };
    }

    var starts = [];
    var ends = [];
    for (var c in curves)
    {
        starts = append(starts, getCurveEndpoint(c.bSplineCurve, false));
        ends = append(ends, getCurveEndpoint(c.bSplineCurve, true));
    }

    var used = makeArray(nCurves, false);
    var chains = [];

    // Pass 1: nearest-endpoint chaining
    for (var seed = 0; seed < nCurves; seed += 1)
    {
        if (used[seed])
        {
            continue;
        }
        used[seed] = true;
        var chain = {
            "curves" : [{ "bSplineCurve" : curves[seed].bSplineCurve, "reversed" : false }],
            "start" : starts[seed],
            "end" : ends[seed],
            "closed" : false
        };

        while (true)
        {
            if (norm(chain.start - chain.end) < tolerance)
            {
                chain.closed = true;
                break;
            }
            var best = undefined;
            for (var i = 0; i < nCurves; i += 1)
            {
                if (used[i])
                {
                    continue;
                }
                // [distance, curve, attach at end?, curve reversed?]
                for (var option in [[norm(chain.end - starts[i]), true, false],
                                    [norm(chain.end - ends[i]), true, true],
                                    [norm(chain.start - ends[i]), false, false],
                                    [norm(chain.start - starts[i]), false, true]])
                {
                    if (option[0] < tolerance && (best == undefined || option[0] < best.distance))
                    {
                        best = { "distance" : option[0], "index" : i, "atEnd" : option[1], "reversed" : option[2] };
                    }
                }
            }
            if (best == undefined)
            {
                break;
            }
            used[best.index] = true;
            const piece = { "bSplineCurve" : curves[best.index].bSplineCurve, "reversed" : best.reversed };
            const pieceStart = best.reversed ? ends[best.index] : starts[best.index];
            const pieceEnd = best.reversed ? starts[best.index] : ends[best.index];
            if (best.atEnd)
            {
                chain.curves = append(chain.curves, piece);
                chain.end = pieceEnd;
            }
            else
            {
                chain.curves = concatenateArrays([[piece], chain.curves]);
                chain.start = pieceStart;
            }
        }
        chains = append(chains, chain);
    }

    // Pass 2: bridge the smallest remaining gaps between open chain ends
    var maxBridge = 0 * meter;
    while (true)
    {
        var best = undefined;
        for (var i = 0; i < size(chains); i += 1)
        {
            if (chains[i].closed)
            {
                continue;
            }
            const selfGap = norm(chains[i].start - chains[i].end);
            if (best == undefined || selfGap < best.distance)
            {
                best = { "distance" : selfGap, "i" : i, "j" : -1, "mode" : "self" };
            }
            for (var j = i + 1; j < size(chains); j += 1)
            {
                if (chains[j].closed)
                {
                    continue;
                }
                for (var option in [[norm(chains[i].end - chains[j].start), "endStart"],
                                    [norm(chains[i].end - chains[j].end), "endEnd"],
                                    [norm(chains[i].start - chains[j].end), "startEnd"],
                                    [norm(chains[i].start - chains[j].start), "startStart"]])
                {
                    if (option[0] < best.distance)
                    {
                        best = { "distance" : option[0], "i" : i, "j" : j, "mode" : option[1] };
                    }
                }
            }
        }
        if (best == undefined || best.distance > LOOP_BRIDGE_TOL)
        {
            break;
        }
        if (best.distance > maxBridge)
        {
            maxBridge = best.distance;
        }
        if (best.j == -1)
        {
            chains[best.i].closed = true;
            continue;
        }
        const a = chains[best.i];
        const b = chains[best.j];
        var merged;
        if (best.mode == "endStart")
        {
            merged = joinChains(a, b);
        }
        else if (best.mode == "endEnd")
        {
            merged = joinChains(a, reverseChain(b));
        }
        else if (best.mode == "startEnd")
        {
            merged = joinChains(b, a);
        }
        else
        {
            merged = joinChains(reverseChain(b), a);
        }
        var remaining = [];
        for (var k = 0; k < size(chains); k += 1)
        {
            if (k == best.i)
            {
                remaining = append(remaining, merged);
            }
            else if (k != best.j)
            {
                remaining = append(remaining, chains[k]);
            }
        }
        chains = remaining;
    }

    var groups = [];
    var openChains = 0;
    for (var chain in chains)
    {
        if (!chain.closed)
        {
            openChains += 1;
        }
        groups = append(groups, chain.curves);
    }
    return { "groups" : groups, "maxBridge" : maxBridge, "openChains" : openChains };
}

/**
 * Chain `a` followed by chain `b` (a's end meets b's start).
 */
function joinChains(a is map, b is map) returns map
{
    return {
        "curves" : concatenateArrays([a.curves, b.curves]),
        "start" : a.start,
        "end" : b.end,
        "closed" : false
    };
}

/**
 * The same chain traversed the other way.
 */
function reverseChain(chain is map) returns map
{
    var curves = [];
    for (var i = size(chain.curves) - 1; i >= 0; i -= 1)
    {
        curves = append(curves, { "bSplineCurve" : chain.curves[i].bSplineCurve, "reversed" : !(chain.curves[i].reversed == true) });
    }
    return { "curves" : curves, "start" : chain.end, "end" : chain.start, "closed" : chain.closed };
}

/**
 * Get endpoint of a B-spline curve.
 */
function getCurveEndpoint(curve is BSplineCurve, atEnd is boolean) returns Vector
{
    // Evaluated, not the first/last control point: those are only on the curve for a clamped
    // (non-periodic) B-spline; a full circle comes back periodic (tools review 2026-09-25).
    var domain = curveDomain(curve);
    return evaluateSpline({ "spline" : curve, "parameters" : [atEnd ? domain[1] : domain[0]] })[0][0];
}

// Parameter domain [knots[p], knots[last - p]]: for a clamped curve the same as the first/last knot,
// for a periodic curve the actual range of the curve.
function curveDomain(curve is BSplineCurve) returns array
{
    var knots = curve.knots;
    var p = curve.degree;
    return [knots[p], knots[size(knots) - 1 - p]];
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
// Maximum turning between consecutive perimeter samples of a curved edge.
const SAMPLE_TURN = 5 * degree;

function sampleCurvePoints(curve is BSplineCurve) returns array
{
    var cps = curve.controlPoints;
    var n = size(cps);
    
    if (n <= 2)
    {
        return cps;
    }
    
    var domain = curveDomain(curve);
    var uMin = domain[0];
    var uMax = domain[1];

    // Samples follow the curve's turning, not its control-point count: one per SAMPLE_TURN of the control
    // polygon's total turning (a straight edge keeps its two endpoints; a full circle gets ~72 points, an
    // area error of ~0.1% instead of several % from a 7-point polygon).
    var turning = 0;
    for (var j = 1; j < n - 1; j += 1)
    {
        var a = cps[j] - cps[j - 1];
        var b = cps[j + 1] - cps[j];
        if (norm(a) > TOLERANCE.zeroLength * meter && norm(b) > TOLERANCE.zeroLength * meter)
        {
            turning += angleBetween(a, b) / radian;
        }
    }
    var numSamples = max(n, ceil(turning / (SAMPLE_TURN / radian)) + 1);

    // All samples evaluated, ends included (the first/last control points are not on a periodic curve).
    var params = [];
    for (var j = 0; j < numSamples; j += 1)
    {
        params = append(params, uMin + (j / (numSamples - 1)) * (uMax - uMin));
    }
    return evaluateSpline({ "spline" : curve, "parameters" : params })[0];
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
    
    // Determine parent for each perimeter (smallest containing perimeter)
    var parents = [];  // index of parent, or -1 if top-level
    var areas = [];    // plain numbers (m^2), as are the polygon coordinates below
    var polyX = [];
    var polyY = [];

    for (var i = 0; i < n; i += 1)
    {
        var xs = [];
        var ys = [];
        for (var p in perimeterData[i].points2D)
        {
            xs = append(xs, p[0] / meter);
            ys = append(ys, p[1] / meter);
        }
        polyX = append(polyX, xs);
        polyY = append(polyY, ys);
        areas = append(areas, abs(polygonSignedAreaXY(xs, ys)));
    }

    for (var i = 0; i < n; i += 1)
    {
        var parentIdx = -1;
        var parentArea = inf;

        for (var j = 0; j < n; j += 1)
        {
            // A container is strictly larger than what it contains. Without this two near-equal
            // loops (the open halves of one broken outline) each took the other as parent, no
            // loop was top-level, and the body silently lost all its area.
            if (i == j || areas[j] <= areas[i])
            {
                continue;
            }

            // i is inside j when most of i's vertices are. A vertex-average test point can fall
            // outside a thin U- or crescent-shaped loop altogether.
            if (mostlyInside(polyX[i], polyY[i], polyX[j], polyY[j]))
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
 * Build nested hierarchy from flat parent-index array (recursive).
 *
 * @param perimeterData {array} : Flat array of { pointIndices, points2D }
 * @param parents {array} : For each perimeter i, parents[i] = index of its immediate parent (-1 = top-level)
 * @param parentIdx {number} : Current parent to collect children for (-1 on initial call)
 * @returns {array} : Array of nodes at this nesting depth, each:
 *   { pointIndices: [int,...], points2D: [[x,y],...], subgroupData: [...recursive...] }
 *   Top-level call returns only the root perimeters; children are in subgroupData.
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
 * True when more than half of up to NESTING_SAMPLES vertices of `inner`, evenly spread, lie
 * inside `outer`. Vertices shared by touching loops sit on the boundary and can go either
 * way, so a majority decides rather than any one point.
 */
function mostlyInside(innerX is array, innerY is array, outerX is array, outerY is array) returns boolean
{
    const n = size(innerX);
    const count = min(n, NESTING_SAMPLES);
    var inside = 0;
    for (var s = 0; s < count; s += 1)
    {
        const k = floor(s * n / count);
        if (pointInPolygonXY(innerX[k], innerY[k], outerX, outerY))
        {
            inside += 1;
        }
    }
    return 2 * inside > count;
}

/**
 * Test if point (px, py) is inside the polygon (xs, ys) using ray casting. Plain numbers (meters).
 */
function pointInPolygonXY(px is number, py is number, xs is array, ys is array) returns boolean
{
    const n = size(xs);
    if (n < 3)
        return false;

    var inside = false;

    var j = n - 1;
    for (var i = 0; i < n; i += 1)
    {
        const xi = xs[i];
        const yi = ys[i];
        const xj = xs[j];
        const yj = ys[j];
        
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
    
    // Triangulate this perimeter (the triangles are output for GJ and the stored map)
    var triangles = earClipTriangulate(points2D, pointIndices);

    // Section properties come from the outline itself, exactly, so they do not depend on the
    // triangulation succeeding on a thin or self-touching loop
    var sectionProperties = computeSectionPropertiesFromPolygon(points2D, frame);
    
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
 * The loops run on plain numbers (meters): a ValueWithUnits operation builds and typechecks a map,
 * which is far more expensive than a number operation and is what made big sections hit the
 * interpreter step limit. The units are stripped once on entry; the result is indices only.
 *
 * @param points2D {array} : Local 2D coordinates
 * @param pointIndices {array} : Indices into sectionPoints
 * @returns {array} : Array of [i, j, k] triangles (indices into sectionPoints)
 */
export function earClipTriangulate(points2D is array, pointIndices is array) returns array
{
    const n = size(points2D);

    if (n < 3)
        return [];

    if (n == 3)
        return [[pointIndices[0], pointIndices[1], pointIndices[2]]];

    var xs = [];
    var ys = [];
    for (var p in points2D)
    {
        xs = append(xs, p[0] / meter);
        ys = append(ys, p[1] / meter);
    }

    // Working polygon: local indices into points2D/pointIndices
    var localIndices = [];
    for (var i = 0; i < n; i += 1)
    {
        localIndices = append(localIndices, i);
    }

    // Reflex vertices (local ids) of the working polygon. Only a reflex vertex can lie inside an ear
    // triangle of a simple polygon, so the contains-a-vertex test needs only these. Clipping an ear
    // changes the angle of its two neighbours only, so the list is updated there.
    var reflexList = reflexVertices(xs, ys, localIndices);

    var triangles = [];
    var scanStart = 0;   // resume where the last ear was clipped: a clip only changes its neighbours

    // Each pass scans the remaining polygon for a valid ear (a convex vertex whose triangle contains
    // no other reflex vertex), emits it and removes the tip, until 3 vertices remain. If a full scan
    // finds no ear the polygon is collinear (fan it) or numerically ill-conditioned (clip the most
    // convex vertex anyway), so the loop always makes progress.
    while (size(localIndices) > 3)
    {
        var earFound = false;
        const numLocal = size(localIndices);

        for (var step = 0; step < numLocal; step += 1)
        {
            const i = (scanStart + step) % numLocal;
            const localPrev = localIndices[(i - 1 + numLocal) % numLocal];
            const localCurr = localIndices[i];
            const localNext = localIndices[(i + 1) % numLocal];

            const ax = xs[localPrev];
            const ay = ys[localPrev];
            const bx = xs[localCurr];
            const by = ys[localCurr];
            const cx = xs[localNext];
            const cy = ys[localNext];

            // Convex? (near-zero counts as convex: collinear ears are valid)
            if ((bx - ax) * (cy - by) - (by - ay) * (cx - bx) < -1e-12)
                continue;

            // Does the triangle contain another reflex vertex?
            var blocked = false;
            for (var r in reflexList)
            {
                if (r == localPrev || r == localCurr || r == localNext)
                    continue;
                if (pointInTriangleXY(xs[r], ys[r], ax, ay, bx, by, cx, cy))
                {
                    blocked = true;
                    break;
                }
            }
            if (blocked)
                continue;

            triangles = append(triangles, [pointIndices[localPrev], pointIndices[localCurr], pointIndices[localNext]]);
            localIndices = removeIndex(localIndices, i);
            reflexList = updateReflexAfterClip(reflexList, localIndices, xs, ys, localPrev, localCurr, localNext, i);
            scanStart = i > 0 ? i - 1 : 0;
            earFound = true;
            break;
        }

        if (earFound)
            continue;

        // No clean ear. Collinear remainder: fan it.
        var allCollinear = true;
        var forced = 0;
        var forcedCross = -inf;
        for (var i = 0; i < numLocal; i += 1)
        {
            const cross = turnAt(xs, ys, localIndices, i);
            if (abs(cross) > 1e-12)
                allCollinear = false;
            if (cross > forcedCross)
            {
                forcedCross = cross;
                forced = i;
            }
        }

        if (allCollinear)
        {
            while (size(localIndices) > 2)
            {
                triangles = append(triangles, [pointIndices[localIndices[0]], pointIndices[localIndices[1]], pointIndices[localIndices[2]]]);
                localIndices = removeIndex(localIndices, 1);
            }
            break;
        }

        // Numerically ill-conditioned (collinear runs, touching vertices): clip the most convex vertex
        // anyway rather than dropping the rest of the polygon, so the triangulation still covers the
        // whole loop. Section properties come from the outline, so this only affects the mesh.
        triangles = append(triangles, [
                pointIndices[localIndices[(forced - 1 + numLocal) % numLocal]],
                pointIndices[localIndices[forced]],
                pointIndices[localIndices[(forced + 1) % numLocal]]
            ]);
        localIndices = removeIndex(localIndices, forced);
        reflexList = reflexVertices(xs, ys, localIndices);
        scanStart = 0;
    }

    // Final triangle
    if (size(localIndices) == 3)
    {
        triangles = append(triangles, [pointIndices[localIndices[0]], pointIndices[localIndices[1]], pointIndices[localIndices[2]]]);
    }

    return triangles;
}

/** Cross product of the turn at position `pos` of the working polygon (> 0 = left turn = convex for CCW). */
function turnAt(xs is array, ys is array, localIndices is array, pos is number) returns number
{
    const m = size(localIndices);
    const a = localIndices[(pos - 1 + m) % m];
    const b = localIndices[pos];
    const c = localIndices[(pos + 1) % m];
    return (xs[b] - xs[a]) * (ys[c] - ys[b]) - (ys[b] - ys[a]) * (xs[c] - xs[b]);
}

/** Local ids of the reflex vertices of the working polygon. */
function reflexVertices(xs is array, ys is array, localIndices is array) returns array
{
    var reflex = [];
    for (var i = 0; i < size(localIndices); i += 1)
    {
        if (turnAt(xs, ys, localIndices, i) < -1e-12)
        {
            reflex = append(reflex, localIndices[i]);
        }
    }
    return reflex;
}

/**
 * The reflex list after the ear tip `localCurr` (position `removedPos`) was removed from the working
 * polygon. Only its two neighbours get new neighbours, so only their reflex state is recomputed.
 * `localIndices` is the working polygon AFTER the removal.
 */
function updateReflexAfterClip(reflexList is array, localIndices is array, xs is array, ys is array,
        localPrev is number, localCurr is number, localNext is number, removedPos is number) returns array
{
    const m = size(localIndices);
    var updated = filter(reflexList, function(r) { return r != localCurr && r != localPrev && r != localNext; });
    for (var pos in [(removedPos - 1 + m) % m, removedPos % m])
    {
        if (turnAt(xs, ys, localIndices, pos) < -1e-12)
        {
            updated = append(updated, localIndices[pos]);
        }
    }
    return updated;
}

/**
 * Point-in-triangle test using barycentric coordinates, on plain numbers (meters).
 * True iff (px, py) is strictly inside triangle abc (u > tol, v > tol, u + v < 1 - tol);
 * degenerate triangles (zero denominator) return false.
 */
function pointInTriangleXY(px is number, py is number, ax is number, ay is number, bx is number, by is number,
        cx is number, cy is number) returns boolean
{
    const v0x = cx - ax;
    const v0y = cy - ay;
    const v1x = bx - ax;
    const v1y = by - ay;
    const v2x = px - ax;
    const v2y = py - ay;

    const dot00 = v0x * v0x + v0y * v0y;
    const dot01 = v0x * v1x + v0y * v1y;
    const dot02 = v0x * v2x + v0y * v2y;
    const dot11 = v1x * v1x + v1y * v1y;
    const dot12 = v1x * v2x + v1y * v2y;

    const denom = dot00 * dot11 - dot01 * dot01;
    if (abs(denom) < 1e-20)
        return false;

    const invDenom = 1 / denom;
    const u = (dot11 * dot02 - dot01 * dot12) * invDenom;
    const v = (dot00 * dot12 - dot01 * dot02) * invDenom;

    const tol = 1e-9;
    return (u > tol) && (v > tol) && (u + v < 1 - tol);
}

// =============================================================================
// SECTION PROPERTIES
// =============================================================================


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
    
    // Each part's moments are about its OWN centroid; moved to the combined centroid by the
    // parallel axis theorem. Areas carry their sign (a hole is negative), which is what makes
    // the same formula subtract a hole correctly.
    var Ixx = a.Ixx + b.Ixx;
    var Iyy = a.Iyy + b.Iyy;
    var Ixy = a.Ixy + b.Ixy;
    if (abs(totalArea) > 1e-20 * meter * meter)
    {
        for (var part in [a, b])
        {
            const dx = part.centroid2D[0] - centroid2D[0];
            const dy = part.centroid2D[1] - centroid2D[1];
            Ixx += part.area * dy * dy;
            Iyy += part.area * dx * dx;
            Ixy += part.area * dx * dy;
        }
    }

    return {
        "area" : totalArea,
        "centroid2D" : centroid2D,
        "centroid3D" : centroid3D,
        "Ixx" : Ixx,
        "Iyy" : Iyy,
        "Ixy" : Ixy
    };
}

/**
 * Exact section properties of a simple polygon from its outline (Green's theorem), about the
 * polygon's own centroid, in the convention the rest of this module uses:
 * Ixx = integral of y^2, Iyy = integral of x^2, Ixy = integral of xy, frame 2D coordinates.
 * O(n) and independent of any triangulation. Coordinates are taken relative to the first
 * vertex so the sums do not cancel.
 */
function computeSectionPropertiesFromPolygon(points2D is array, frame is CoordSystem) returns map
{
    const n = size(points2D);
    if (n < 3)
    {
        return emptySectionProperties(frame);
    }
    // Plain numbers (meters) in the loop: unit arithmetic is far costlier. Units are put back on the result.
    const ox = points2D[0][0] / meter;
    const oy = points2D[0][1] / meter;
    var px = [];
    var py = [];
    for (var p in points2D)
    {
        px = append(px, p[0] / meter - ox);
        py = append(py, p[1] / meter - oy);
    }

    var a2 = 0;       // twice the signed area
    var sx = 0;       // 6 A cx
    var sy = 0;       // 6 A cy
    var sxx = 0;      // 12 * integral x^2
    var syy = 0;      // 12 * integral y^2
    var sxy = 0;      // 24 * integral xy
    for (var i = 0; i < n; i += 1)
    {
        const j = (i + 1) % n;
        const xi = px[i];
        const yi = py[i];
        const xj = px[j];
        const yj = py[j];
        const c = xi * yj - xj * yi;
        a2 += c;
        sx += (xi + xj) * c;
        sy += (yi + yj) * c;
        sxx += (xi * xi + xi * xj + xj * xj) * c;
        syy += (yi * yi + yi * yj + yj * yj) * c;
        sxy += (xi * yj + 2 * xi * yi + 2 * xj * yj + xj * yi) * c;
    }

    var area = a2 / 2;
    if (abs(area) < 1e-20)
    {
        return emptySectionProperties(frame);
    }
    const cxLocal = sx / (3 * a2);
    const cyLocal = sy / (3 * a2);

    // Moments about the local origin, then shifted to the centroid
    var Iyy = sxx / 12 - area * cxLocal * cxLocal;
    var Ixx = syy / 12 - area * cyLocal * cyLocal;
    var Ixy = sxy / 24 - area * cxLocal * cyLocal;

    // Orientation-independent, as the triangle version (whose triangle areas are unsigned)
    if (area < 0)
    {
        area = -area;
        Ixx = -Ixx;
        Iyy = -Iyy;
        Ixy = -Ixy;
    }

    const centroid2D = [(cxLocal + ox) * meter, (cyLocal + oy) * meter];
    return {
        "area" : area * meter^2,
        "centroid2D" : centroid2D,
        "centroid3D" : frame2DToWorld(centroid2D, frame),
        "Ixx" : Ixx * meter^4,
        "Iyy" : Iyy * meter^4,
        "Ixy" : Ixy * meter^4
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
            "minX" : 0 * meter,
            "maxX" : 0 * meter,
            "minY" : 0 * meter,
            "maxY" : 0 * meter,
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
    var xs = [];
    var ys = [];
    for (var p in points2D)
    {
        xs = append(xs, p[0] / meter);
        ys = append(ys, p[1] / meter);
    }
    return polygonSignedAreaXY(xs, ys) * meter * meter;
}

/**
 * Signed area (m^2, plain number) of the polygon with coordinates xs, ys in meters (positive = CCW).
 */
function polygonSignedAreaXY(xs is array, ys is array) returns number
{
    const n = size(xs);
    var area = 0;
    for (var i = 0; i < n; i += 1)
    {
        const j = (i + 1) % n;
        area += xs[i] * ys[j] - xs[j] * ys[i];
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
    return concatenateArrays([subArray(arr, 0, index), subArray(arr, index + 1, size(arr))]);
}
