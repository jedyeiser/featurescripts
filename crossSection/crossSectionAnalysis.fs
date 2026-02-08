FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// CrossSectionMath -- pure unitless math (de Boor, intersections, etc.)
import(path : "4538be7c5b7f28ba40050fad", version : "b75407f5e871ae17169fa4d6");


/**
 * CROSS SECTION ANALYSIS - Geometric Extraction
 * ==============================================
 *
 * Implements the body-first iteration strategy for extracting cross-section
 * intersection curves from solid bodies along an edge path.
 *
 * Architecture:
 * 1. Generate cross-section planes along the edge
 * 2. For each body: find which planes intersect it, preprocess faces & edges
 * 3. For each face x plane: approximate intersection, find exact edge points,
 *    form spans, build final splines
 * 4. Assemble results into analysisMap
 *
 * Kernel calls happen ONLY during preprocessing (ev*, qIntersectsPlane).
 * All per-plane intersection math is pure computation via CrossSectionMath,
 * operating on unitless data for maximum performance.
 *
 * OUTPUT STRUCTURE:
 * {
 *     crossSections: [{
 *         plane: Plane,                        // FS Plane (with units)
 *         frame: CoordSystem,                  // FS CoordSystem (with units)
 *         bodies: [bodyIndex, ...],            // which bodies intersect here
 *         intersectionCurves: [{
 *             BSplineCurve: BSplineCurve,      // with units, for downstream use
 *             bodies: [bodyIndex, ...],
 *             faceIdx: number
 *         }, ...]
 *     }, ...],
 *     bodies: [{
 *         bodyQuery: Query,
 *         bodyIndex: number,
 *         boundingBox: Box3d,
 *         volume: ValueWithUnits,
 *         intersectionPlanes: [planeIndex, ...]
 *     }, ...]
 * }
 */

// =============================================================================
// CONSTANTS
// =============================================================================

// Default B-spline degree for approximate intersection splines.
// Cubic gives a good balance of smoothness and efficiency.
const APPROX_SPLINE_DEGREE = 3;

// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Extract cross-section geometry for all bodies along an edge path.
 *
 * This is the Part 1 workhorse -- it returns intersection curves per
 * cross-section, ready for downstream triangulation, CLT, or debug viz.
 *
 * @param context : Onshape context
 * @param id : Feature ID (for kernel ops)
 * @param edge : Edge query to cross-section along
 * @param bodyQueries : Array of body Query objects (from definition)
 * @param numSections : Number of cross-section planes
 * @returns : analysisMap (see OUTPUT STRUCTURE above)
 */
export function extractCrossSectionGeometry(context is Context, id is Id,
    edge is Query, bodyQueries is array, numSections is number) returns map
{
    // -----------------------------------------------------------------
    // Step 1: Generate cross-section planes along the edge
    // -----------------------------------------------------------------
    var framesAndPlanes = buildCrossSectionPlanes(context, edge, numSections);
    var frames = framesAndPlanes.frames;
    var planes = framesAndPlanes.planes;          // Array of FS Plane objects
    var planesU = framesAndPlanes.planesUnitless;  // Array of { origin, normal } (unitless)

    // Initialize cross-section entries
    var crossSections = makeArray(size(planes));
    for (var cs = 0; cs < size(planes); cs += 1)
    {
        crossSections[cs] = {
            "plane" : planes[cs],
            "frame" : frames[cs],
            "bodies" : [],
            "intersectionCurves" : []
        };
    }

    // -----------------------------------------------------------------
    // Step 2: Body-first iteration
    // -----------------------------------------------------------------
    var bodiesData = makeArray(size(bodyQueries));

    for (var b = 0; b < size(bodyQueries); b += 1)
    {
        var bodyResult = processBody(context, id, b, bodyQueries[b],
            crossSections, planes, planesU);

        bodiesData[b] = bodyResult.bodyData;
        crossSections = bodyResult.crossSections;
    }

    // -----------------------------------------------------------------
    // Step 3: Close intersection loops with gap filling
    // -----------------------------------------------------------------
    crossSections = closeIntersectionLoops(context, crossSections, frames, 0.05 * millimeter);

    // -----------------------------------------------------------------
    // Step 4: Adjacent bodies (stub -- populated during Phase 2 dedup)
    //
    // The adjacentBodies field exists in bodyData so the data structure
    // is ready for dedup. The actual computation is deferred because:
    //   - It's only needed for dedup optimization (Phase 2)
    //   - The right heuristic depends on seeing real duplicate patterns
    //   - Edge endpoint comparison is insufficient (edges can overlap
    //     without sharing endpoints)
    // -----------------------------------------------------------------

    return {
        "crossSections" : crossSections,
        "bodies" : bodiesData
    };
}

// =============================================================================
// PLANE GENERATION
// =============================================================================

/**
 * Generate cross-section frames and planes along an edge.
 *
 * Returns both FS types (for output/kernel use) and unitless versions
 * (for internal math).
 */
function buildCrossSectionPlanes(context is Context, edge is Query, numSections is number) returns map
{
    // Evaluate curvature frames at evenly spaced parameters
    var paramRange = range(0, 1, numSections);
    var curvatures = evEdgeCurvatures(context, {
            "edge" : edge,
            "parameters" : paramRange
    });

    var frames = [];
    var planes = [];
    var planesU = [];

    for (var c in curvatures)
    {
        var frame = c.frame;

        // Ensure Z-axis (tangent) points in positive X direction for consistency
        if (frame.zAxis[0] < 0)
        {
            frame.zAxis *= -1;
        }

        var csPlane = plane(frame.origin, frame.zAxis);

        frames = append(frames, frame);
        planes = append(planes, csPlane);
        planesU = append(planesU, {
            "origin" : vecToArr(frame.origin),
            "normal" : dirToArr(frame.zAxis)
        });
    }

    return {
        "frames" : frames,
        "planes" : planes,
        "planesUnitless" : planesU
    };
}

// =============================================================================
// BODY PROCESSING
// =============================================================================

/**
 * Process a single body: find intersecting planes, preprocess geometry,
 * run per-plane intersection, and populate crossSections with results.
 *
 * This is the core of the body-first iteration strategy. By preprocessing
 * faces and edges ONCE per body (not per plane), we avoid redundant kernel
 * calls for bodies that span many cross-section planes.
 */
function processBody(context is Context, id is Id, bodyIndex is number, bodyQuery is Query,
    crossSections is array, planes is array, planesU is array) returns map
{
    // --- Body bounding box (needed for plane filtering AND bodyData) ---
    var boundingBox = evBox3d(context, { "topology" : bodyQuery, "tight" : true });
    var bboxU = {
        "min" : vecToArr(boundingBox.minCorner),
        "max" : vecToArr(boundingBox.maxCorner)
    };

    // --- Find which planes intersect this body (unitless bbox check, no kernel calls) ---
    var intersectionPlanes = findBodyIntersectionPlanes(bboxU, planesU);

    if (size(intersectionPlanes) == 0)
    {
        return {
            "bodyData" : {
                "bodyQuery" : bodyQuery,
                "bodyIndex" : bodyIndex,
                "boundingBox" : undefined,
                "volume" : 0 * meter ^ 3,
                "intersectionPlanes" : [],
                "adjacentBodies" : []
            },
            "crossSections" : crossSections
        };
    }

    // --- Cross-reference: mark each intersected cross-section with this body ---
    for (var csIdx in intersectionPlanes)
    {
        crossSections[csIdx].bodies = append(crossSections[csIdx].bodies, bodyIndex);
    }

    // --- Body-level data ---
    var volume = 0 * meter ^ 3;
    try { volume = evVolume(context, { "entities" : bodyQuery }); }
    catch {}

    // --- Preprocess edges that intersect ANY of our planes (one kernel call) ---
    // Also pre-computes edge x plane intersections for all body-intersecting planes.
    var edgeResult = preprocessBodyEdges(context, bodyQuery, intersectionPlanes, planes, planesU);

    // --- Preprocess faces that intersect ANY of our planes (one kernel call) ---
    // Uses edge?face adjacency from edgeResult to avoid extra ev* calls.
    var faceData = preprocessBodyFaces(context, id, bodyIndex, bodyQuery,
        intersectionPlanes, planes, planesU, edgeResult);

    // --- Per-plane intersection: iterate faces for each plane ---
    crossSections = runPerPlaneIntersections(context, bodyIndex, faceData, edgeResult.edges,
        intersectionPlanes, crossSections, planesU);

    return {
        "bodyData" : {
            "bodyQuery" : bodyQuery,
            "bodyIndex" : bodyIndex,
            "boundingBox" : boundingBox,
            "volume" : volume,
            "intersectionPlanes" : intersectionPlanes,
            "adjacentBodies" : []  // populated during Phase 2 dedup
        },
        "crossSections" : crossSections
    };
}

/**
 * Find which cross-section planes intersect a body's bounding box.
 *
 * Pure unitless math -- no kernel calls. Uses the same bbox-plane test
 * from CrossSectionMath (project bbox half-extent onto plane normal).
 * Conservative: may include planes that clip a bbox corner but miss the
 * actual body, but that's fine -- the per-face bbox check filters those.
 *
 * @param bboxU : Body bbox { min: [x,y,z], max: [x,y,z] } (unitless)
 * @param planesU : Array of { origin, normal } (unitless)
 * @returns : Array of plane indices that could intersect this body.
 */
function findBodyIntersectionPlanes(bboxU is map, planesU is array) returns array
{
    var indices = [];
    for (var p = 0; p < size(planesU); p += 1)
    {
        if (bboxIntersectsPlane(bboxU, planesU[p].origin, planesU[p].normal))
        {
            indices = append(indices, p);
        }
    }
    return indices;
}

// =============================================================================
// EDGE PREPROCESSING
// =============================================================================

/**
 * Collect and preprocess all edges of this body that intersect any cross-section
 * plane. Returns unitless B-spline data for each edge, with summary fields
 * for fast matching (dimension, first/last/average CP).
 *
 * Pre-computes edge x plane intersections for ALL body-intersecting planes
 * via CP polygon walk. This moves the work out of the face x plane inner loop,
 * eliminating redundant walks when multiple faces share an edge.
 *
 * Also returns the union query (allEdgeQ) so preprocessBodyFaces can resolve
 * face-edge adjacency with a single qIntersection per face.
 *
 * Strategy: union all per-plane edge queries into ONE evaluateQuery call,
 * then precompute each edge's B-spline once via qNthElement.
 */
function preprocessBodyEdges(context is Context, bodyQuery is Query,
    intersectionPlanes is array, planes is array, planesU is array) returns map
{
    // Build union query for edges intersecting ANY plane
    var edgePlaneQueries = [];
    var bodyEdgeQ = qOwnedByBody(bodyQuery, EntityType.EDGE);
    for (var p in intersectionPlanes)
    {
        edgePlaneQueries = append(edgePlaneQueries, qIntersectsPlane(bodyEdgeQ, planes[p]));
    }

    var allEdgeQ = qUnion(edgePlaneQueries);
    var numEdges = size(evaluateQuery(context, allEdgeQ));  // one kernel call for count

    // Precompute B-spline + summary for each edge.
    // qNthElement creates a lazy query reference -- avoids materializing
    // all transient queries from evaluateQuery up front.
    var edgeData = makeArray(numEdges);
    for (var e = 0; e < numEdges; e += 1)
    {
        var edgeQ = qNthElement(allEdgeQ, e);
        var bSpline = evApproximateBSplineCurve(context, { "edge" : edgeQ });
        var unitless = stripCurveUnits(bSpline);
        var cps = unitless.controlPoints;
        var n = size(cps);

        // Pre-compute plane intersections for this edge (CP polygon walk).
        // Stored as string-keyed map: planeHits["<planeIndex>"] = [{point, tangent}, ...]
        // Only stores entries for planes that actually intersect this edge.
        var planeHits = {};
        for (var planeIndex in intersectionPlanes)
        {
            var origin = planesU[planeIndex].origin;
            var normal = planesU[planeIndex].normal;
            var hits = [];

            var prevDist = dotU(subtractU(cps[0], origin), normal);

            if (abs(prevDist) < GEOM_TOL)
            {
                var tang = (n > 1) ? subtractU(cps[1], cps[0]) : [0, 0, 0];
                hits = append(hits, { "point" : cps[0], "tangent" : tang });
            }

            for (var i = 1; i < n; i += 1)
            {
                var dist = dotU(subtractU(cps[i], origin), normal);

                if (abs(dist) < GEOM_TOL)
                {
                    var tang = (i < n - 1) ? subtractU(cps[i + 1], cps[i]) : subtractU(cps[i], cps[i - 1]);
                    hits = append(hits, { "point" : cps[i], "tangent" : tang });
                }
                else if (prevDist * dist < 0)
                {
                    var t = prevDist / (prevDist - dist);
                    hits = append(hits, {
                        "point" : lerpU(cps[i - 1], cps[i], t),
                        "tangent" : subtractU(cps[i], cps[i - 1])
                    });
                }

                prevDist = dist;
            }

            if (size(hits) > 0)
            {
                planeHits["" ~ planeIndex] = hits;
            }
        }

        edgeData[e] = {
            "edgeQuery" : edgeQ,
            "controlPoints" : cps,
            "knots" : unitless.knots,
            "degree" : unitless.degree,
            "dimension" : n,
            "firstCP" : cps[0],
            "lastCP" : cps[n - 1],
            "averageCP" : averageU(cps),
            "planeHits" : planeHits
        };
    }

    return {
        "edges" : edgeData,
        "allEdgeQ" : allEdgeQ  // For efficient face-edge adjacency resolution
    };
}

// =============================================================================
// FACE PREPROCESSING
// =============================================================================

/**
 * Collect and preprocess all faces of this body that intersect any cross-section
 * plane. For each face:
 *   - Approximate B-spline surface (one kernel call per face)
 *   - Compute face bounding box from surface CPs
 *   - Determine piercing direction (USE_U vs USE_V)
 *   - Look up adjacent edge indices via single qIntersection per face
 */
function preprocessBodyFaces(context is Context, id is Id, bodyIndex is number, bodyQuery is Query,
    intersectionPlanes is array, planes is array, planesU is array, bodyEdgeResult is map) returns array
{
    // Build union query for faces intersecting ANY plane
    var facePlaneQueries = [];
    var bodyFaceQ = qOwnedByBody(bodyQuery, EntityType.FACE);
    for (var p in intersectionPlanes)
    {
        facePlaneQueries = append(facePlaneQueries, qIntersectsPlane(bodyFaceQ, planes[p]));
    }

    var allFaceQ = qUnion(facePlaneQueries);
    var numFaces = size(evaluateQuery(context, allFaceQ));  // one kernel call for count
    var faceData = [];

    for (var f = 0; f < numFaces; f += 1)
    {
        var faceQ = qNthElement(allFaceQ, f);

        // Approximate B-spline surface (kernel call -- one per face)
        var bSplineSurface = evApproximateBSplineSurface(context, { "face" : faceQ }).bSplineSurface;
        var cpGrid = stripSurfaceCPUnits(bSplineSurface.controlPoints);

        // Bounding box from surface CPs (for per-plane early exit)
        var bbox = computeSurfaceBbox(cpGrid);

        // Determine piercing direction using first intersecting plane as reference.
        // For cross sections along a single spine, plane normals are similar enough
        // that this choice generalizes across all planes for this face.
        var refPlane = planesU[intersectionPlanes[0]];
        var useU = determinePiercingDirection(cpGrid, refPlane.normal);

        // Intersection dimension: # of CPs per iso-curve in the piercing direction
        var intersectionDimension = useU ? size(cpGrid) : size(cpGrid[0]);

        // --- Adjacent edge indices: ONE query per face ---
        // Intersect face's edges with the body's intersection edge set.
        // Resolves all matches in a single evaluateQuery call.
        var adjacentEdgeIndices = findEdgeIndicesForFace(context, faceQ,
            bodyEdgeResult.allEdgeQ, bodyEdgeResult.edges);

        faceData = append(faceData, {
            "faceIdx" : f,
            "faceQuery" : faceQ,
            "cpGrid" : cpGrid,
            "bbox" : bbox,
            "useU" : useU,
            "intersectionDimension" : intersectionDimension,
            "adjacentEdgeIndices" : adjacentEdgeIndices
        });
    }

    return faceData;
}

/**
 * Find which body intersection edge indices are adjacent to a given face.
 *
 * One evaluateQuery per face: get the face's edges, intersect with the body's
 * intersection edge union, evaluate once. Then match resolved queries back to
 * edgeData indices via areQueriesEquivalent.
 */
function findEdgeIndicesForFace(context is Context, faceQuery is Query,
    allEdgeQ is Query, bodyEdges is array) returns array
{
    var faceEdgesQ = qAdjacent(faceQuery, AdjacencyType.EDGE, EntityType.EDGE);
    var matchedEdges = evaluateQuery(context, qIntersection(faceEdgesQ, allEdgeQ));

    var indices = [];
    for (var m = 0; m < size(matchedEdges); m += 1)
    {
        for (var e = 0; e < size(bodyEdges); e += 1)
        {
            if (areQueriesEquivalent(context, matchedEdges[m], bodyEdges[e].edgeQuery))
            {
                indices = append(indices, e);
                break;
            }
        }
    }
    return indices;
}

// =============================================================================
// PER-PLANE INTERSECTION
// =============================================================================

/**
 * For each face of this body, iterate through intersecting planes and extract
 * intersection curves. This is the inner loop that benefits most from unitless
 * math -- all the heavy computation here uses CrossSectionMath functions.
 *
 * For each face x plane combination:
 *   Step 1: Approximate intersection from surface CP grid
 *   Step 2: Look up pre-computed edge-plane intersections
 *   Step 3: Filter tangent/corner intersections
 *   Step 4: Estimate fit parameters and form spans
 *   Step 5: Build final splines, snap exit point to real edge via @evDistance
 */
function runPerPlaneIntersections(context is Context, bodyIndex is number,
    faceData is array, bodyEdgeData is array,
    intersectionPlanes is array, crossSections is array, planesU is array) returns array
{
    for (var f = 0; f < size(faceData); f += 1)
    {
        var face = faceData[f];

        for (var planeIndex in intersectionPlanes)
        {
            var planeU = planesU[planeIndex];

            // --- EARLY EXIT: bbox-plane rejection ---
            if (!bboxIntersectsPlane(face.bbox, planeU.origin, planeU.normal))
                continue;

            // =============================================================
            // STEP 1: Approximate intersection from surface CP grid
            // =============================================================
            var distGrid = computeDistanceGrid(face.cpGrid, planeU.origin, planeU.normal);
            var approxCPs = walkIsoCurves(face.cpGrid, distGrid, face.useU);

            // Need at least 2 points for a meaningful intersection
            if (size(approxCPs) < 2)
                continue;

            // Build approximate intersection spline (unitless)
            var approxDegree = min(APPROX_SPLINE_DEGREE, size(approxCPs) - 1);
            var approxKnots = arcLengthKnotVector(approxCPs, approxDegree);

            // =============================================================
            // STEP 2: Look up pre-computed edge-plane intersections
            // =============================================================
            var planeKey = "" ~ planeIndex;
            var faceEdgeIntersections = [];
            for (var edgeIdx in face.adjacentEdgeIndices)
            {
                var hits = bodyEdgeData[edgeIdx].planeHits[planeKey];
                if (hits != undefined)
                {
                    for (var hit in hits)
                    {
                        faceEdgeIntersections = append(faceEdgeIntersections, {
                            "point" : hit.point,
                            "edgeIdx" : edgeIdx,
                            "tangent" : hit.tangent
                        });
                    }
                }
            }

            // =============================================================
            // STEP 3: Merge corners + filter tangent touches
            // =============================================================
            var merged = mergeNearDuplicates(faceEdgeIntersections);
            if (size(merged) % 2 != 0)
            {
                merged = filterTangentIntersections(merged, planeU.normal);
            }

            // Still odd or < 2? Skip this face for this plane.
            if (size(merged) < 2 || size(merged) % 2 != 0)
                continue;

            // =============================================================
            // STEP 4: Estimate fit parameters and form entry/exit spans
            //
            // Uses cheap polyline projection on approxCPs instead of expensive
            // Newton foot-point iteration. We only need approximate parameters
            // for sorting and CP selection, not exact curve parameters.
            // =============================================================
            for (var mi = 0; mi < size(merged); mi += 1)
            {
                var fitParam = projectOntoPolyline(merged[mi].point, approxCPs);
                merged[mi] = mergeMaps(merged[mi], { "fitParam" : fitParam });
            }

            // Sort by fitParam, then pair into spans: (0,1), (2,3), ...
            merged = sortArray(merged, function(a, b) { return a.fitParam - b.fitParam; });

            // =============================================================
            // STEP 5: Build final splines from exact endpoints + sampled interior
            //
            // Exit point is snapped to the real edge via @evDistance for
            // accurate endpoint positioning (important for Phase 2 stitching).
            // =============================================================
            for (var sp = 0; sp < size(merged) - 1; sp += 2)
            {
                var startPoint = merged[sp].point;
                var endPoint = merged[sp + 1].point;
                var startParam = merged[sp].fitParam;
                var endParam = merged[sp + 1].fitParam;

                // Snap exit point to real edge geometry
                var endEdgeIdx = merged[sp + 1].edgeIdx;
                try
                {
                    var distResult = evDistance(context, {
                        "side0" : arrToVec(endPoint),
                        "side1" : bodyEdgeData[endEdgeIdx].edgeQuery
                    });
                    endPoint = vecToArr(distResult.sides[1].point);
                }
                catch
                {
                    // Fall back to CP polygon approximation if evDistance fails
                }

                var finalCurve = buildFinalSpline(startPoint, endPoint,
                    startParam, endParam, approxCPs, approxKnots, approxDegree,
                    face.intersectionDimension);

                if (finalCurve != undefined)
                {
                    crossSections[planeIndex].intersectionCurves = append(
                        crossSections[planeIndex].intersectionCurves, {
                            "BSplineCurve" : finalCurve,
                            "bodies" : [bodyIndex],
                            "faceIdx" : f
                        });
                }
            }
        }
    }

    return crossSections;
}

/**
 * Build the final intersection spline for one span (entry/exit pair).
 *
 * Uses exact edge intersection points as endpoints. Instead of re-sampling
 * the approximate spline via deBoor (expensive at scale), we select the
 * approximate CPs that fall between the entry/exit parameters and bracket
 * them with the exact endpoints.
 *
 * This eliminates thousands of deBoor calls that were hitting FS's step limit.
 *
 * This is one of the OUTPUT BOUNDARIES where we convert back to units.
 */
function buildFinalSpline(
    startPoint is array, endPoint is array,
    startParam is number, endParam is number,
    approxCPs is array, approxKnots is array, approxDegree is number,
    intersectionDimension is number)
{
    // Find approxCPs whose arc-length parameter falls between start and end.
    // approxCPs are already ordered intersection points from walkIsoCurves.
    // We use Greville abscissae to estimate each CP's parameter.
    var numCPs = size(approxCPs);
    var cpParams = grevilleAbscissae(approxKnots, approxDegree, numCPs);

    var interiorPoints = [];
    for (var i = 0; i < numCPs; i += 1)
    {
        if (cpParams[i] > startParam + GEOM_TOL && cpParams[i] < endParam - GEOM_TOL)
        {
            interiorPoints = append(interiorPoints, approxCPs[i]);
        }
    }

    // Assemble: [exact start] + [interior approx CPs] + [exact end]
    var allPointsU = concatenateArrays([[startPoint], interiorPoints, [endPoint]]);

    // Need at least degree+1 points
    var degree = min(APPROX_SPLINE_DEGREE, size(allPointsU) - 1);
    if (degree < 1)
        return undefined;

    // Build knot vector from arc-length parameterization (unitless)
    var knots = arcLengthKnotVector(allPointsU, degree);

    // --- OUTPUT BOUNDARY: convert control points to Vectors with units ---
    var cpsWithUnits = makeArray(size(allPointsU));
    for (var i = 0; i < size(allPointsU); i += 1)
    {
        cpsWithUnits[i] = arrToVec(allPointsU[i]);
    }

    try
    {
        return bSplineCurve({
                "degree" : degree,
                "isPeriodic" : false,
                "controlPoints" : cpsWithUnits,
                "knots" : knots as KnotArray
        });
    }
    catch
    {
        return undefined;
    }
}

// =============================================================================
// SORTING HELPER
// =============================================================================
// FeatureScript's sort expects a comparison function returning -1, 0, or 1.
// Our caller passes a subtraction-based comparator.

/**
 * Sort an array using a comparison function.
 * Comparison function should return negative if a < b, positive if a > b, 0 if equal.
 * Uses insertion sort -- efficient for the small arrays we deal with (typically < 20 elements).
 */
function sortArray(arr is array, compareFn) returns array
{
    var sorted = arr;
    for (var i = 1; i < size(sorted); i += 1)
    {
        var key = sorted[i];
        var j = i - 1;
        while (j >= 0 && compareFn(sorted[j], key) > 0)
        {
            sorted[j + 1] = sorted[j];
            j -= 1;
        }
        sorted[j + 1] = key;
    }
    return sorted;
}

// =============================================================================
// LOOP CLOSURE AND GAP FILLING
// =============================================================================

/**
 * Close intersection loops by chaining curves and filling gaps.
 *
 * For each plane, chains curves into loops (handling multiple disconnected
 * components and nested holes), then fixes small gaps by adjusting endpoints
 * and large gaps by inserting linear filler curves.
 *
 * @param context : Onshape context (needed for evDistance)
 * @param crossSections : Array of cross-section data
 * @param frames : Array of coordinate frames (for nesting detection)
 * @param gapTolerance : Maximum gap size to fix (typically 0.05mm)
 * @returns : Modified crossSections with closed loops
 */
function closeIntersectionLoops(context is Context, crossSections is array,
    frames is array, gapTolerance is ValueWithUnits) returns array
{
    var gapTolU = gapTolerance / meter;  // Convert to unitless

    for (var planeIdx = 0; planeIdx < size(crossSections); planeIdx += 1)
    {
        var cs = crossSections[planeIdx];

        if (size(cs.intersectionCurves) == 0)
            continue;

        // Group curves by body
        var bodyGroups = {};
        for (var c = 0; c < size(cs.intersectionCurves); c += 1)
        {
            var curve = cs.intersectionCurves[c];
            var bodyIdx = curve.bodies[0];  // Each curve belongs to one body
            var key = "" ~ bodyIdx;

            if (bodyGroups[key] == undefined)
                bodyGroups[key] = [];

            bodyGroups[key] = append(bodyGroups[key], {
                "curveIdx" : c,
                "curve" : curve.BSplineCurve,
                "faceIdx" : curve.faceIdx,
                "bodies" : curve.bodies
            });
        }

        // Process each body's curves
        var newCurves = [];
        for (var key, curveSet in bodyGroups)
        {
            // Step 1: Chain curves into boundaries
            var boundaries = chainCurvesIntoBoundaries(curveSet, gapTolU);

            // Step 2: Fix gaps in each boundary
            for (var b = 0; b < size(boundaries); b += 1)
            {
                var boundary = boundaries[b];
                var gaps = detectGaps(boundary.chain, curveSet, gapTolU);

                // Apply fixes
                adjustEndpointsToMeet(boundary.chain, curveSet, gaps, gapTolU);
                var updatedChain = insertLinearFillers(boundary.chain, curveSet, gaps, gapTolU);
                boundaries[b].chain = updatedChain.chain;
                curveSet = updatedChain.curves;
            }

            // Step 3: Collect fixed curves into output
            for (var b = 0; b < size(boundaries); b += 1)
            {
                for (var chainEntry in boundaries[b].chain)
                {
                    var curveData = curveSet[chainEntry.curveIdx];
                    var finalCurve = chainEntry.isReversed
                        ? reverseBSplineCurve(curveData.curve)
                        : curveData.curve;

                    newCurves = append(newCurves, {
                        "BSplineCurve" : finalCurve,
                        "bodies" : curveData.bodies,
                        "faceIdx" : chainEntry.isFiller ? -1 : curveData.faceIdx
                    });
                }
            }
        }

        crossSections[planeIdx].intersectionCurves = newCurves;
    }

    return crossSections;
}

/**
 * Chain unordered curves into closed (or near-closed) loops using
 * four-way endpoint matching (adapted from xSectRef algorithm).
 *
 * @param curves : Array of curve data with .curve field
 * @param tolerance : Gap tolerance for endpoint matching (unitless)
 * @returns : Array of boundaries, each with { chain, chainStart, chainEnd, closureGap, isClosed }
 */
function chainCurvesIntoBoundaries(curves is array, tolerance is number) returns array
{
    var numCurves = size(curves);
    var used = makeArray(numCurves);
    for (var i = 0; i < numCurves; i += 1)
        used[i] = false;

    var boundaries = [];

    while (true)
    {
        // Find first unused curve
        var startIdx = -1;
        for (var i = 0; i < numCurves; i += 1)
        {
            if (!used[i])
            {
                startIdx = i;
                break;
            }
        }

        if (startIdx == -1)
            break;  // All curves processed

        // Start new boundary chain
        used[startIdx] = true;
        var chain = [{ "curveIdx" : startIdx, "isReversed" : false, "isFiller" : false }];

        var chainStart = getCurveEndpoint(curves[startIdx].curve, true, false);
        var chainEnd = getCurveEndpoint(curves[startIdx].curve, false, false);

        var changed = true;
        while (changed)
        {
            changed = false;

            for (var i = 0; i < numCurves; i += 1)
            {
                if (used[i])
                    continue;

                var curveStart = getCurveEndpoint(curves[i].curve, true, false);
                var curveEnd = getCurveEndpoint(curves[i].curve, false, false);

                // Four-way matching: try all orientations and positions
                var matched = false;

                // Case 1: Append forward (curve.start connects to chainEnd)
                if (normU(subtractU(curveStart, chainEnd)) < tolerance)
                {
                    chain = append(chain, { "curveIdx" : i, "isReversed" : false, "isFiller" : false });
                    chainEnd = curveEnd;
                    matched = true;
                }
                // Case 2: Append reversed (curve.end connects to chainEnd)
                else if (normU(subtractU(curveEnd, chainEnd)) < tolerance)
                {
                    chain = append(chain, { "curveIdx" : i, "isReversed" : true, "isFiller" : false });
                    chainEnd = curveStart;
                    matched = true;
                }
                // Case 3: Prepend forward (curve.end connects to chainStart)
                else if (normU(subtractU(curveEnd, chainStart)) < tolerance)
                {
                    chain = concatenateArrays([[{ "curveIdx" : i, "isReversed" : false, "isFiller" : false }], chain]);
                    chainStart = curveStart;
                    matched = true;
                }
                // Case 4: Prepend reversed (curve.start connects to chainStart)
                else if (normU(subtractU(curveStart, chainStart)) < tolerance)
                {
                    chain = concatenateArrays([[{ "curveIdx" : i, "isReversed" : true, "isFiller" : false }], chain]);
                    chainStart = curveEnd;
                    matched = true;
                }

                if (matched)
                {
                    used[i] = true;
                    changed = true;
                    break;  // Restart search with extended chain
                }
            }
        }

        // Check if loop closed
        var closureGap = normU(subtractU(chainStart, chainEnd));
        var isClosed = closureGap < tolerance;

        boundaries = append(boundaries, {
            "chain" : chain,
            "chainStart" : chainStart,
            "chainEnd" : chainEnd,
            "closureGap" : closureGap,
            "isClosed" : isClosed
        });
    }

    return boundaries;
}

/**
 * Get endpoint of a BSpline curve (start or end).
 * For non-periodic curves, endpoints are the first/last control points.
 *
 * @param curve : BSplineCurve
 * @param isStart : true for start point, false for end point
 * @param isReversed : if true, swap start/end
 * @returns : [x, y, z] in unitless meters
 */
function getCurveEndpoint(curve is BSplineCurve, isStart is boolean, isReversed is boolean) returns array
{
    var wantStart = isReversed ? !isStart : isStart;
    var idx = wantStart ? 0 : (size(curve.controlPoints) - 1);
    return vecToArr(curve.controlPoints[idx]);
}

/**
 * Detect gaps between adjacent curves in a chain.
 *
 * @param chain : Ordered array of { curveIdx, isReversed }
 * @param curves : Array of curve data
 * @param tolerance : Minimum gap size to report (unitless)
 * @returns : Array of gaps with { afterIdx, beforeIdx, distance, point1, point2 }
 */
function detectGaps(chain is array, curves is array, tolerance is number) returns array
{
    var gaps = [];

    for (var i = 0; i < size(chain); i += 1)
    {
        var currentEntry = chain[i];
        var nextEntry = chain[(i + 1) % size(chain)];

        var currentCurve = curves[currentEntry.curveIdx].curve;
        var nextCurve = curves[nextEntry.curveIdx].curve;

        var currentEnd = getCurveEndpoint(currentCurve, false, currentEntry.isReversed);
        var nextStart = getCurveEndpoint(nextCurve, true, nextEntry.isReversed);

        var gapDistance = normU(subtractU(currentEnd, nextStart));

        if (gapDistance > GEOM_TOL)  // Larger than floating-point noise
        {
            gaps = append(gaps, {
                "afterIdx" : i,
                "beforeIdx" : (i + 1) % size(chain),
                "distance" : gapDistance,
                "point1" : currentEnd,
                "point2" : nextStart
            });
        }
    }

    return gaps;
}

/**
 * Adjust curve endpoints to close small gaps by rebuilding curves
 * with modified control points meeting at the midpoint.
 *
 * @param chain : Chain of curves
 * @param curves : Array of curve data (modified in place)
 * @param gaps : Array of gap data
 * @param tolerance : Maximum gap size to fix (unitless)
 */
function adjustEndpointsToMeet(chain is array, curves is array, gaps is array, tolerance is number)
{
    for (var gap in gaps)
    {
        if (gap.distance >= tolerance)
            continue;  // Only fix small gaps

        var midpoint = scaleU(addU(gap.point1, gap.point2), 0.5);

        var afterEntry = chain[gap.afterIdx];
        var beforeEntry = chain[gap.beforeIdx];

        // Rebuild curve1 with modified endpoint
        var curve1 = curves[afterEntry.curveIdx].curve;
        var endpointIdx1 = afterEntry.isReversed ? 0 : (size(curve1.controlPoints) - 1);
        curves[afterEntry.curveIdx].curve = rebuildCurveWithNewEndpoint(curve1, endpointIdx1, midpoint);

        // Rebuild curve2 with modified startpoint
        var curve2 = curves[beforeEntry.curveIdx].curve;
        var endpointIdx2 = beforeEntry.isReversed ? (size(curve2.controlPoints) - 1) : 0;
        curves[beforeEntry.curveIdx].curve = rebuildCurveWithNewEndpoint(curve2, endpointIdx2, midpoint);
    }
}

/**
 * Rebuild a BSpline curve with one control point modified.
 *
 * @param curve : Original BSplineCurve
 * @param endpointIndex : Index of control point to modify
 * @param newPoint : New position [x, y, z] (unitless)
 * @returns : New BSplineCurve with modified control point
 */
function rebuildCurveWithNewEndpoint(curve is BSplineCurve, endpointIndex is number,
    newPoint is array) returns BSplineCurve
{
    var modifiedCPs = makeArray(size(curve.controlPoints));
    for (var i = 0; i < size(curve.controlPoints); i += 1)
    {
        if (i == endpointIndex)
            modifiedCPs[i] = arrToVec(newPoint);
        else
            modifiedCPs[i] = curve.controlPoints[i];
    }

    return bSplineCurve({
        "degree" : curve.degree,
        "isPeriodic" : false,
        "controlPoints" : modifiedCPs,
        "knots" : curve.knots
    });
}

/**
 * Insert linear B-spline fillers for large gaps.
 *
 * Creates new curve entries for gaps that exceed the tolerance and inserts
 * them into the chain at the appropriate positions.
 *
 * @param chain : Chain of curves
 * @param curves : Array of curve data
 * @param gaps : Array of gap data
 * @param tolerance : Minimum gap size to fill (unitless)
 * @returns : Map with updated { chain, curves }
 */
function insertLinearFillers(chain is array, curves is array, gaps is array, tolerance is number) returns map
{
    var updatedChain = chain;
    var updatedCurves = curves;

    // Process gaps in reverse order so indices remain valid
    var sortedGaps = sortArray(gaps, function(a, b) { return b.afterIdx - a.afterIdx; });

    for (var gap in sortedGaps)
    {
        if (gap.distance < tolerance)
            continue;  // Only fill large gaps

        // Create linear B-spline from point1 to point2
        var fillerCurve = bSplineCurve({
            "degree" : 1,
            "isPeriodic" : false,
            "controlPoints" : [arrToVec(gap.point1), arrToVec(gap.point2)],
            "knots" : [0, 0, 1, 1] as KnotArray
        });

        // Get bodies from the curve after this gap
        var afterCurve = updatedCurves[updatedChain[gap.afterIdx].curveIdx];

        // Add filler to curves array
        var fillerIdx = size(updatedCurves);
        updatedCurves = append(updatedCurves, {
            "curve" : fillerCurve,
            "faceIdx" : -1,
            "bodies" : afterCurve.bodies
        });

        // Insert into chain after the gap
        var newEntry = { "curveIdx" : fillerIdx, "isReversed" : false, "isFiller" : true };
        var insertPos = gap.afterIdx + 1;

        var newChain = makeArray(size(updatedChain) + 1);
        for (var i = 0; i < insertPos; i += 1)
            newChain[i] = updatedChain[i];
        newChain[insertPos] = newEntry;
        for (var i = insertPos; i < size(updatedChain); i += 1)
            newChain[i + 1] = updatedChain[i];

        updatedChain = newChain;
    }

    return {
        "chain" : updatedChain,
        "curves" : updatedCurves
    };
}

/**
 * Reverse a BSpline curve (reverse control points and reflect knots).
 *
 * @param curve : Original BSplineCurve
 * @returns : Reversed BSplineCurve
 */
function reverseBSplineCurve(curve is BSplineCurve) returns BSplineCurve
{
    var n = size(curve.controlPoints);
    var reversedCPs = makeArray(n);
    for (var i = 0; i < n; i += 1)
        reversedCPs[i] = curve.controlPoints[n - 1 - i];

    // Reflect knots: knots'[i] = knotMax - knots[n-i]
    var knotMax = curve.knots[size(curve.knots) - 1];
    var reversedKnots = makeArray(size(curve.knots));
    for (var i = 0; i < size(curve.knots); i += 1)
        reversedKnots[i] = knotMax - curve.knots[size(curve.knots) - 1 - i];

    return bSplineCurve({
        "degree" : curve.degree,
        "isPeriodic" : false,
        "controlPoints" : reversedCPs,
        "knots" : reversedKnots as KnotArray
    });
}
