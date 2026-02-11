FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * XSECTION PROCESSING MODULE
 * ==========================
 *
 * Cross-section processing core logic for the xSection feature.
 *
 * This module handles:
 * - processCrossSections() - Main processing pipeline for all cross-sections
 * - resolveOverrideMaterialData() - User-provided material override resolution
 * - Curve deduplication (getUniqueCurvesOptimized and helpers)
 * - 2D bounding box computation and overlap detection
 *
 * Extracted from xSect.fs to separate processing logic from orchestration.
 */

// IMPORTS - xSectPredicates (for MaterialBehavior and MaterialType enums)
import(path : "17142132b20343b5f125e7e7", version : "131003d40b8c47ecc764fd09");
// IMPORTS - xSectUtils (constants, utilities, polyline projection)
import(path : "c2c3edd39b85fde5e6062533", version : "22e36084c7a3deaba8553abc");
// IMPORTS - xSect_Triangulation (processBodyCurves)
import(path : "08d3a8d4e34a60d45d46e261", version : "d3df284fabacd5135d7b6271");
// IMPORTS - xSectMaterials (buildMaterialLookup, normalizeMaterialName, tryGetKey)
import(path : "f8e590162884d45f56e0a05f", version : "32c54f5061b096612985f073");

// IMPORTS - xSectCLT (isotropicQMatrix, orthotropicQMatrix)
import(path : "74231d1d53f5a117d47d17a9", version : "31cbeb1b629ee01ad23fa333");

// =============================================================================
// OVERLAP DETECTION CONSTANTS
// =============================================================================

/**
 * Overlap percentage threshold for curve deduplication.
 *
 * Two curves are considered to fully overlap if >= 80% of control points
 * from one curve lie on the other. This value balances:
 * - False negatives: Too high (>90%) misses legitimate overlaps from tessellation
 * - False positives: Too low (<70%) merges distinct adjacent curves
 *
 * Chosen at 80% based on typical B-spline approximation quality.
 */
const OVERLAP_PERCENTAGE_THRESHOLD = 0.8;

/**
 * Spatial grid cell size for overlap detection optimization.
 *
 * Curves are grouped into grid cells based on bounding boxes to avoid O(n²)
 * comparisons. Cell size balances:
 * - Too large: many curves per cell, less speedup
 * - Too small: curves span multiple cells, overhead increases
 *
 * Set to 5mm based on typical cross-section dimensions (10-100mm range).
 * Adjacent cell checking ensures no overlaps are missed.
 */
const SPATIAL_GRID_CELL_SIZE = 5 * millimeter;

// =============================================================================
// CURVE OVERLAP DETECTION TYPES
// =============================================================================

/**
 * Overlap detection result types for curve deduplication.
 * Used by getUniqueCurvesOptimized() for composite wire generation.
 */
export enum OverlapType
{
    NONE,               // No overlap detected
    FULL_CONTAINMENT,   // One curve fully contains the other
    PARTIAL_OVERLAP     // Curves partially overlap at endpoints
}

// =============================================================================
// CROSS-SECTION PROCESSING
// =============================================================================

/**
 * Process all cross-sections and extract B-spline curves, points, and properties.
 *
 * Builds the bodies array from definition.bodyArray, resolving material data
 * from the CSV and user overrides here (not in editing logic) because
 * ValueWithUnits maps don't survive definition serialization.
 *
 * @param fcpX : FCP world X coordinate (or undefined for uniform spacing)
 * @param acpX : ACP world X coordinate (or undefined for uniform spacing)
 */
export function processCrossSections(context is Context, id is Id, definition is map, fcpX, acpX) returns map
{
    var frameData = getCrossSectionFramesAdaptive(context, definition.xSectAlong, definition.numSections, fcpX, acpX);

    // -----------------------------------------------------------------
    // Parse CSV for material resolution (re-parsed here because complex
    // maps with ValueWithUnits don't survive from editing logic)
    // -----------------------------------------------------------------
    var materialLookup = {};
    try
    {
        var csvData = definition.materialCSV.csvData;
        if (csvData is array)
        {
            materialLookup = buildMaterialLookup(csvData);
        }
    }
    catch (e)
    {
        println("WARNING: Failed to parse material CSV - " ~ e);
    }

    // -----------------------------------------------------------------
    // Build top-level bodies array from editing logic's bodyArray,
    // resolving material data and computing volume
    // -----------------------------------------------------------------
    var bodies = [];
    for (var i = 0; i < size(definition.bodyArray); i += 1)
    {
        var bodyDef = definition.bodyArray[i];
        var bodyEntry = {
            "bodyQuery" : bodyDef.bodyQuery,
            "bodyIdx" : i,
            "bodyName" : tryGetKey(bodyDef, "bodyName"),
            "materialName" : tryGetKey(bodyDef, "materialName"),
            "hasMaterialData" : false
        };

        // --- Resolve material data from CSV ---
        var matName = tryGetKey(bodyDef, "materialName");
        if (matName != undefined && matName != "Not assigned")
        {
            var key = normalizeMaterialName(matName);
            var csvMatch = materialLookup[key];
            if (csvMatch != undefined)
            {
                bodyEntry.hasMaterialData = true;
                bodyEntry.materialData = csvMatch;
            }
        }

        // --- If no CSV match, resolve from user overrides ---
        if (!bodyEntry.hasMaterialData)
        {
            var behavior = tryGetKey(bodyDef, "materialBehavior");
            if (behavior == MaterialBehavior.PROVIDE_DATA)
            {
                bodyEntry = resolveOverrideMaterialData(bodyDef, bodyEntry);
            }
        }

        // --- Compute volume ---
        var bodyVolume = 0 * meter^3;
        try
        {
            bodyVolume = evVolume(context, { "entities" : bodyDef.bodyQuery });
        }
        catch (e)
        {
            println("WARNING: Could not compute body volume - " ~ e);
        }
        bodyEntry.volume = bodyVolume;

        bodies = append(bodies, bodyEntry);
    }

    var bodyQueries = mapArray(bodies, function(b) { return b.bodyQuery; });

    // Build body index map for O(1) lookups (Phase 2 optimization)
    var bodyIndexMap = {};
    for (var i = 0; i < size(bodyQueries); i += 1)
    {
        var entities = evaluateQuery(context, bodyQueries[i]);
        if (size(entities) > 0)
        {
            // Use first entity as key (solid bodies typically have single entity)
            bodyIndexMap[toString(entities[0])] = i;
        }
    }

    var crossSections = [];

    println("Processing " ~ size(frameData) ~ " cross-sections...");

    for (var i = 0; i < size(frameData); i += 1)
    {
        // Progress indicator every 10 sections
        if (i % 10 == 0 && i > 0)
        {
            println("  Section " ~ i ~ " / " ~ size(frameData) ~ " (" ~ floor(100.0 * i / size(frameData)) ~ "%)");
        }

        var frame = frameData[i].frame;
        var stationNumber = frameData[i].stationNumber;
        var xSectPlane = plane(frame.origin, frame.zAxis);

        opPlane(context, id + ("plane" ~ i), { "plane" : xSectPlane });
        var planeQ = qCreatedBy(id + ("plane" ~ i), EntityType.FACE);

        var intersectingBodies = evaluateQuery(context, qIntersectsPlane(qUnion(bodyQueries), xSectPlane));

        var wireQueries = [];
        var allBSplines = [];
        var bodyToCurves = {};

        // PHASE A: Intersect all bodies and extract B-splines
        for (var b = 0; b < size(intersectingBodies); b += 1)
        {
            var body = intersectingBodies[b];

            // Use O(1) map lookup instead of O(n) search
            var bodyIdx = bodyIndexMap[toString(body)];
            if (bodyIdx == undefined)
            {
                println("WARNING: Body not found in index map at section " ~ i);
                continue;
            }

            opIntersectFaces(context, id + ("intersect" ~ i ~ "_" ~ b), {
                    "tools" : planeQ,
                    "targets" : body
            });

            wireQueries = append(wireQueries, qCreatedBy(id + ("intersect" ~ i ~ "_" ~ b), EntityType.BODY));

            var edges = evaluateQuery(context, qCreatedBy(id + ("intersect" ~ i ~ "_" ~ b), EntityType.EDGE));
            var bodyCurves = [];

            for (var e = 0; e < size(edges); e += 1)
            {
                var bspline = evApproximateBSplineCurve(context, { "edge" : edges[e] });

                var curveData = {
                    "bSplineCurve" : bspline,
                    "bodyIndices" : [bodyIdx],
                    "bbox2D" : computeCurveBoundingBox2D(bspline, xSectPlane)
                };

                allBSplines = append(allBSplines, curveData);
                bodyCurves = append(bodyCurves, curveData);
            }

            bodyToCurves[bodyIdx] = bodyCurves;
        }

        // PHASE B: Deduplicate if creating composites
        var finalCurves = allBSplines;
        if (definition.createComposites)
        {
            finalCurves = getUniqueCurvesOptimized(allBSplines, OVERLAP_TOL);
        }

        // Strip bbox2D from final output curves (do this early for use in all code paths)
        var outputCurves = mapArray(finalCurves, function(c) {
            return {
                "bSplineCurve" : c.bSplineCurve,
                "bodyIndices" : c.bodyIndices
            };
        });

        // PHASE C: Build bodyData using triangulation module
        var sectionPoints = [];
        var spatialGrid = {};  // Phase 4: Spatial grid for O(1) point deduplication
        var bodyData = [];

        for (var body in intersectingBodies)
        {
            // Use O(1) map lookup instead of O(n) search
            var bodyIdx = bodyIndexMap[toString(body)];
            if (bodyIdx == undefined)
            {
                println("WARNING: Body not found in index map at section " ~ i);
                continue;
            }

            var bodyCurves = bodyToCurves[bodyIdx];
            if (bodyCurves == undefined)
                bodyCurves = [];

            var result = processBodyCurves(bodyCurves, frame, sectionPoints, spatialGrid);
            sectionPoints = result.sectionPoints;
            spatialGrid = result.spatialGrid;

            bodyData = append(bodyData, {
                "bodyIdx" : bodyIdx,
                "groups" : result.bodyData.groups,
                "totalSectionProperties" : result.bodyData.totalSectionProperties,
                "boundingBox" : result.bodyData.boundingBox
            });
        }

        // Aggregate bounding boxes from all bodies at this section
        var overallMinX = undefined;
        var overallMaxX = undefined;
        var overallMinY = undefined;
        var overallMaxY = undefined;

        for (var bodyEntry in bodyData)
        {
            var bbox = bodyEntry.boundingBox;
            if (overallMinX == undefined || bbox.minX < overallMinX) overallMinX = bbox.minX;
            if (overallMaxX == undefined || bbox.maxX > overallMaxX) overallMaxX = bbox.maxX;
            if (overallMinY == undefined || bbox.minY < overallMinY) overallMinY = bbox.minY;
            if (overallMaxY == undefined || bbox.maxY > overallMaxY) overallMaxY = bbox.maxY;
        }

        // Validate that we have valid bounding box data
        if (overallMinX == undefined || overallMaxX == undefined || overallMinY == undefined || overallMaxY == undefined)
        {
            println("WARNING: Section " ~ i ~ " has no valid body data (no intersecting bodies)");
            // Create empty bounding box with zero dimensions
            var sectionBoundingBox = {
                "minX" : 0 * meter,
                "maxX" : 0 * meter,
                "minY" : 0 * meter,
                "maxY" : 0 * meter,
                "width" : 0 * meter,
                "height" : 0 * meter
            };

            // Still append section with empty data for consistency
            crossSections = append(crossSections, {
                "frame" : frame,
                "stationNumber" : stationNumber,
                "sectionPoints" : sectionPoints,
                "bSplineCurves" : outputCurves,
                "bodyData" : bodyData,
                "boundingBox" : sectionBoundingBox
            });
            continue;  // Skip to next section
        }

        var sectionBoundingBox = {
            "minX" : overallMinX,
            "maxX" : overallMaxX,
            "minY" : overallMinY,
            "maxY" : overallMaxY,
            "width" : (overallMaxX - overallMinX),
            "height" : (overallMaxY - overallMinY)
        };

        // Cleanup
        try { opDeleteBodies(context, id + ("deletePlane" ~ i), { "entities" : qCreatedBy(id + ("plane" ~ i), EntityType.BODY) }); }
        catch (e)
        {
            println("WARNING: Could not delete plane at section " ~ i ~ " - " ~ e);
        }
        if (size(wireQueries) > 0)
        {
            try { opDeleteBodies(context, id + ("deleteWires" ~ i), { "entities" : qUnion(wireQueries) }); }
            catch (e)
            {
                println("WARNING: Could not delete wires at section " ~ i ~ " - " ~ e);
            }
        }

        crossSections = append(crossSections, {
            "frame" : frame,
            "stationNumber" : stationNumber,
            "sectionPoints" : sectionPoints,
            "bSplineCurves" : outputCurves,
            "bodyData" : bodyData,
            "boundingBox" : sectionBoundingBox
        });
    }

    println("Cross-section processing complete: " ~ size(crossSections) ~ " sections analyzed");

    return {
        "bodies" : bodies,
        "crossSections" : crossSections
    };
}


// =============================================================================
// MATERIAL RESOLUTION (FEATURE BODY)
// =============================================================================

/**
 * Build materialData from user-provided override values stored in definition.
 *
 * Called during feature execution (not editing logic) because ValueWithUnits
 * maps don't survive definition serialization.
 *
 * @param bodyDef {map} : The definition bodyArray entry (with override fields)
 * @param bodyEntry {map} : The bodies array entry being built
 * @returns {map} : Updated bodyEntry with materialData attached
 */
export function resolveOverrideMaterialData(bodyDef is map, bodyEntry is map) returns map
{
    var updated = bodyEntry;
    try
    {
        var density = bodyDef.overrideDensity * kilogram / meter^3;
        var name = tryGetKey(bodyDef, "overrideName");
        if (name == undefined)
            name = "User-defined";

        var qMatrix = undefined;
        var youngsModulus = undefined;
        var poissonsRatio = undefined;

        var matType = tryGetKey(bodyDef, "materialType");
        if (matType == MaterialType.ISOTROPIC)
        {
            var E = bodyDef.youngsModulus * megapascal;
            qMatrix = isotropicQMatrix(E, 0.33);
            youngsModulus = E;
            poissonsRatio = 0.33;
        }
        else if (matType == MaterialType.ORTHOTROPIC)
        {
            var E1 = bodyDef.E1 * megapascal;
            var E2 = bodyDef.E2 * megapascal;
            var G12 = bodyDef.G12 * megapascal;
            var nu12 = bodyDef.nu12;
            qMatrix = orthotropicQMatrix(E1, E2, G12, nu12);
            youngsModulus = E1;
            poissonsRatio = nu12;
        }

        if (qMatrix != undefined)
        {
            updated.hasMaterialData = true;
            updated.materialData = {
                "originalName" : name,
                "category" : "User-defined",
                "density" : density,
                "poissonsRatio" : poissonsRatio,
                "youngsModulus" : youngsModulus,
                "qMatrix" : qMatrix
            };
        }
    }
    catch (e)
    {
        println("WARNING: Failed to resolve override material data - " ~ e);
    }
    return updated;
}

// =============================================================================
// MASS CALCULATION (Volume-Based)
// =============================================================================

/**
 * Compute actual body masses from volumes and material densities.
 *
 * Uses evVolume() results (already stored in bodies array) to compute
 * accurate mass rather than approximating from cross-section samples.
 *
 * @param bodies {array} : Bodies array with volume and materialData
 * @returns {map} : { totalMass, bodyMasses: [{ bodyIdx, bodyName, volume, density, mass, hasMaterial }] }
 */
export function computeActualBodyMasses(bodies is array) returns map
{
    var totalMass = 0 * kilogram;
    var bodyMasses = [];

    for (var body in bodies)
    {
        var mass = 0 * kilogram;
        if (body.hasMaterialData)
        {
            mass = body.volume * body.materialData.density;
        }

        bodyMasses = append(bodyMasses, {
            "bodyIdx" : body.bodyIdx,
            "bodyName" : body.bodyName,
            "volume" : body.volume,
            "density" : body.hasMaterialData ? body.materialData.density : 0 * kilogram / meter^3,
            "mass" : mass,
            "hasMaterial" : body.hasMaterialData
        });

        totalMass = totalMass + mass;
    }

    return { "totalMass" : totalMass, "bodyMasses" : bodyMasses };
}


// =============================================================================
// 2D BOUNDING BOX (Plane-Local Coordinates)
// =============================================================================

/**
 * Compute 2D bounding box for a B-spline in plane-local coordinates.
 */
export function computeCurveBoundingBox2D(curve is BSplineCurve, sectionPlane is Plane) returns map
{
    var cps = curve.controlPoints;

    if (size(cps) == 0)
        return { "minX" : 0 * meter, "maxX" : 0 * meter, "minY" : 0 * meter, "maxY" : 0 * meter };

    var pt0_2D = worldToPlane(sectionPlane, cps[0]);
    var minX = pt0_2D[0];
    var maxX = pt0_2D[0];
    var minY = pt0_2D[1];
    var maxY = pt0_2D[1];

    for (var j = 1; j < size(cps); j += 1)
    {
        var pt2D = worldToPlane(sectionPlane, cps[j]);
        minX = min(minX, pt2D[0]);
        maxX = max(maxX, pt2D[0]);
        minY = min(minY, pt2D[1]);
        maxY = max(maxY, pt2D[1]);
    }

    return {
        "minX" : minX,
        "maxX" : maxX,
        "minY" : minY,
        "maxY" : maxY
    };
}

/**
 * Check if two 2D bounding boxes overlap (with tolerance).
 */
export function boundingBoxes2DOverlap(boxA is map, boxB is map, tol is ValueWithUnits) returns boolean
{
    if (boxA.maxX + tol < boxB.minX || boxB.maxX + tol < boxA.minX)
        return false;
    if (boxA.maxY + tol < boxB.minY || boxB.maxY + tol < boxA.minY)
        return false;

    return true;
}

// =============================================================================
// SPATIAL GRID HELPERS
// =============================================================================

/**
 * Compute spatial grid key for a 2D bounding box center.
 *
 * @param bbox {map} : 2D bounding box with minX, maxX, minY, maxY
 * @param cellSize {ValueWithUnits} : Grid cell size
 * @returns {string} : Grid key "ix,iy"
 */
function computeBBoxGridKey(bbox is map, cellSize is ValueWithUnits) returns string
{
    var centerX = (bbox.minX + bbox.maxX) / 2;
    var centerY = (bbox.minY + bbox.maxY) / 2;
    var ix = floor(centerX / cellSize);
    var iy = floor(centerY / cellSize);
    return toString(ix) ~ "," ~ toString(iy);
}

/**
 * Get all grid keys that a bounding box touches (including adjacent cells).
 *
 * Returns the primary cell plus all adjacent cells to ensure overlapping
 * curves in neighboring cells are checked.
 *
 * @param bbox {map} : 2D bounding box
 * @param cellSize {ValueWithUnits} : Grid cell size
 * @returns {array} : Array of grid key strings
 */
function getBBoxGridKeys(bbox is map, cellSize is ValueWithUnits) returns array
{
    var centerX = (bbox.minX + bbox.maxX) / 2;
    var centerY = (bbox.minY + bbox.maxY) / 2;
    var ix = floor(centerX / cellSize);
    var iy = floor(centerY / cellSize);

    // Return primary cell and 8 adjacent cells (3×3 neighborhood)
    // This ensures curves near cell boundaries are checked against neighbors
    var keys = [];
    for (var dx = -1; dx <= 1; dx += 1)
    {
        for (var dy = -1; dy <= 1; dy += 1)
        {
            keys = append(keys, toString(ix + dx) ~ "," ~ toString(iy + dy));
        }
    }
    return keys;
}

/**
 * Build spatial index mapping grid cells to curve indices.
 *
 * @param curves {array} : Array of curves with bbox2D field
 * @param cellSize {ValueWithUnits} : Grid cell size
 * @returns {map} : Map from grid key → array of curve indices
 */
function buildSpatialIndex(curves is array, cellSize is ValueWithUnits) returns map
{
    var spatialIndex = {};

    for (var i = 0; i < size(curves); i += 1)
    {
        var gridKey = computeBBoxGridKey(curves[i].bbox2D, cellSize);

        var cellCurves = spatialIndex[gridKey];
        if (cellCurves == undefined)
            cellCurves = [];

        cellCurves = append(cellCurves, i);
        spatialIndex[gridKey] = cellCurves;
    }

    return spatialIndex;
}

// =============================================================================
// OPTIMIZED UNIQUE CURVE DETECTION
// =============================================================================

/**
 * Get unique curves with optimizations:
 * 1. Spatial binning for O(n×k) instead of O(n²) comparisons
 * 2. 2D bounding box pre-filter
 * 3. Integer body index comparison
 * 4. Minimum curve length protection
 */
export function getUniqueCurvesOptimized(inputCurves is array, tolerance is ValueWithUnits) returns array
{
    if (size(inputCurves) <= 1)
        return inputCurves;

    var tol = tolerance;
    var unique = [];

    // Build spatial index for unique curves (updated as we build the list)
    // Maps grid key → array of indices into unique array
    var spatialIndex = {};

    for (var i = 0; i < size(inputCurves); i += 1)
    {
        var current = inputCurves[i];

        // PROTECTION: Don't dedupe tiny curves
        var curveLength = approximateControlPolygonLength(current.bSplineCurve);
        if (curveLength < MIN_CURVE_LENGTH)
        {
            unique = append(unique, current);
            continue;
        }

        var dominated = false;

        // Get grid cells that current curve might overlap with
        var searchKeys = getBBoxGridKeys(current.bbox2D, SPATIAL_GRID_CELL_SIZE);

        // Build candidate set from spatially nearby curves
        var candidateIndices = [];
        for (var key in searchKeys)
        {
            var cellCurves = spatialIndex[key];
            if (cellCurves != undefined)
            {
                for (var idx in cellCurves)
                {
                    // Avoid duplicates (same curve might be in multiple adjacent cells)
                    if (!isIn(idx, candidateIndices))
                    {
                        candidateIndices = append(candidateIndices, idx);
                    }
                }
            }
        }

        // Check only spatially nearby candidates (typically 5-10 instead of all curves)
        for (var j in candidateIndices)
        {
            var candidate = unique[j];

            // 2D bounding box pre-filter (still useful for curves in same cell)
            if (!boundingBoxes2DOverlap(current.bbox2D, candidate.bbox2D, tol))
                continue;

            // Skip same-body comparisons
            if (curvesShareBodyIndex(current, candidate))
                continue;

            // Check if endpoints touch
            if (!curveEndpointsTouch(current.bSplineCurve, candidate.bSplineCurve, tol))
                continue;

            // Full overlap detection
            var overlapResult = detectCurveOverlap(current.bSplineCurve, candidate.bSplineCurve, tol);

            if (overlapResult.overlapType == OverlapType.NONE)
                continue;

            if (overlapResult.overlapType == OverlapType.FULL_CONTAINMENT)
            {
                if (overlapResult.aContainsB)
                {
                    unique[j] = mergeCurveBodies(current, candidate);
                    dominated = true;
                }
                else
                {
                    unique[j] = mergeCurveBodies(candidate, current);
                    dominated = true;
                }
                break;
            }
            else if (overlapResult.overlapType == OverlapType.PARTIAL_OVERLAP)
            {
                unique[j] = mergeCurveBodies(candidate, current);
                dominated = true;
                break;
            }
        }

        if (!dominated)
        {
            // Add to unique list and update spatial index
            var newIdx = size(unique);
            unique = append(unique, current);

            var gridKey = computeBBoxGridKey(current.bbox2D, SPATIAL_GRID_CELL_SIZE);
            var cellCurves = spatialIndex[gridKey];
            if (cellCurves == undefined)
                cellCurves = [];
            cellCurves = append(cellCurves, newIdx);
            spatialIndex[gridKey] = cellCurves;
        }
    }

    return unique;
}



/**
 * Check if two curves share a body using integer indices.
 */
export function curvesShareBodyIndex(curveA is map, curveB is map) returns boolean
{
    for (var idxA in curveA.bodyIndices)
    {
        for (var idxB in curveB.bodyIndices)
        {
            if (idxA == idxB)
                return true;
        }
    }
    return false;
}

/**
 * Quick check: do curve endpoints touch?
 */
export function curveEndpointsTouch(curveA is BSplineCurve, curveB is BSplineCurve, tol is ValueWithUnits) returns boolean
{
    var cpA = curveA.controlPoints;
    var cpB = curveB.controlPoints;

    if (size(cpA) == 0 || size(cpB) == 0)
        return false;

    var endpointsA = [cpA[0], cpA[size(cpA) - 1]];
    var endpointsB = [cpB[0], cpB[size(cpB) - 1]];

    for (var ptA in endpointsA)
    {
        for (var ptB in endpointsB)
        {
            if (norm(ptA - ptB) < tol)
                return true;
        }
    }

    for (var ptA in endpointsA)
    {
        var closest = closestPointOnPolyline(ptA, cpB);
        if (closest.distance < tol)
            return true;
    }

    return false;
}

/**
 * Detect overlap between two curves with early-abort optimization.
 *
 * Checks control point proximity to classify overlap:
 * - FULL_CONTAINMENT: >=80% of points from one curve lie on the other
 * - PARTIAL_OVERLAP: >=2 points match but not full containment
 * - NONE: <2 points match or early-abort triggered
 *
 * Early-abort logic:
 * - Success: Stop once 80% threshold reached (no need to check remaining points)
 * - Failure: Abort if first 3 points show no match (curves likely don't overlap)
 */
export function detectCurveOverlap(curveA is BSplineCurve, curveB is BSplineCurve, tol is ValueWithUnits) returns map
{
    var cpA = curveA.controlPoints;
    var cpB = curveB.controlPoints;
    var nA = size(cpA);
    var nB = size(cpB);

    // Count how many points from A are on B
    var aOnB = 0;
    var minNeededForA = ceil(nA * OVERLAP_PERCENTAGE_THRESHOLD);

    for (var i = 0; i < nA; i += 1)
    {
        var pt = cpA[i];
        var closest = closestPointOnPolyline(pt, cpB);
        if (closest.distance < tol)
            aOnB += 1;

        // Early success - if we've reached 80%, stop checking
        if (aOnB >= minNeededForA)
            break;

        // Early failure - if we've checked 3 points and none match, abort
        if (i >= 2 && aOnB == 0)
        {
            return { "overlapType" : OverlapType.NONE, "aContainsB" : false };
        }
    }

    // Count how many points from B are on A (similar optimization)
    var bOnA = 0;
    var minNeededForB = ceil(nB * OVERLAP_PERCENTAGE_THRESHOLD);

    for (var i = 0; i < nB; i += 1)
    {
        var pt = cpB[i];
        var closest = closestPointOnPolyline(pt, cpA);
        if (closest.distance < tol)
            bOnA += 1;

        // Early success
        if (bOnA >= minNeededForB)
            break;

        // Early failure
        if (i >= 2 && bOnA == 0)
        {
            return { "overlapType" : OverlapType.NONE, "aContainsB" : false };
        }
    }

    // Classification logic (unchanged)
    var aFullyOnB = (aOnB >= nA * OVERLAP_PERCENTAGE_THRESHOLD);
    var bFullyOnA = (bOnA >= nB * OVERLAP_PERCENTAGE_THRESHOLD);

    if (aFullyOnB && bFullyOnA)
    {
        return { "overlapType" : OverlapType.FULL_CONTAINMENT, "aContainsB" : (nA >= nB) };
    }
    else if (aFullyOnB)
    {
        return { "overlapType" : OverlapType.FULL_CONTAINMENT, "aContainsB" : false };
    }
    else if (bFullyOnA)
    {
        return { "overlapType" : OverlapType.FULL_CONTAINMENT, "aContainsB" : true };
    }
    else if (aOnB >= 2 || bOnA >= 2)
    {
        return { "overlapType" : OverlapType.PARTIAL_OVERLAP, "aContainsB" : false };
    }

    return { "overlapType" : OverlapType.NONE, "aContainsB" : false };
}

/**
 * Merge body ownership from two curves.
 */
export function mergeCurveBodies(keeper is map, donor is map) returns map
{
    var mergedIndices = keeper.bodyIndices;

    for (var idx in donor.bodyIndices)
    {
        if (!isIn(idx, mergedIndices))
        {
            mergedIndices = append(mergedIndices, idx);
        }
    }

    return {
        "bSplineCurve" : keeper.bSplineCurve,
        "bodyIndices" : mergedIndices,
        "bbox2D" : keeper.bbox2D
    };
}
