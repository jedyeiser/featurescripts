FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * PLANE INTERSECTION MODULE
 * =========================
 * 
 * Computes B-spline intersection curves between planes and bodies
 * WITHOUT using opIntersectFaces. Suitable for editing logic.
 * 
 * Pipeline:
 * 1. Preprocess bodies - cache edge/face B-spline data
 * 2. Per plane - find edge crossings, generate interior curves
 * 
 * Interior curves are represented as line segments (degree-1 B-splines)
 * connecting crossing points. This is simpler and faster than full
 * surface-plane intersection, and accurate enough for most analysis.
 */

// =============================================================================
// CONSTANTS
// =============================================================================

const PLANE_TOLERANCE = 1e-8 * meter;
const PARAM_TOLERANCE = 1e-10;
const EDGE_SAMPLES = 5;  // Initial samples for crossing detection

enum PlaneClassification { ABOVE, ON, BELOW }

// =============================================================================
// PREPROCESSING: Build body cache (call ONCE)
// =============================================================================

/**
 * Preprocess all bodies for fast plane intersection.
 * Call this ONCE before processing multiple cross-sections.
 */
export function preprocessBodiesForIntersection(context is Context, bodies is array) returns map
{
    var bodyDataArray = [];
    
    for (var bodyIdx = 0; bodyIdx < size(bodies); bodyIdx += 1)
    {
        var body = bodies[bodyIdx];
        bodyDataArray = append(bodyDataArray, preprocessSingleBody(context, body, bodyIdx));
    }
    
    return {
        "bodies" : bodies,
        "bodyData" : bodyDataArray
    };
}

/**
 * Preprocess a single body: extract faces, edges, curves.
 * All maps use integer indices as keys.
 */
function preprocessSingleBody(context is Context, body is Query, bodyIdx is number) returns map
{
    var faces = evaluateQuery(context, qOwnedByBody(body, EntityType.FACE));
    var edges = evaluateQuery(context, qOwnedByBody(body, EntityType.EDGE));
    
    // Build adjacency using integer indices
    var faceToEdgeIndices = {};
    var edgeToFaceIndices = {};
    
    for (var faceIdx = 0; faceIdx < size(faces); faceIdx += 1)
    {
        faceToEdgeIndices[faceIdx] = [];
    }
    
    for (var edgeIdx = 0; edgeIdx < size(edges); edgeIdx += 1)
    {
        var edge = edges[edgeIdx];
        var adjFaces = evaluateQuery(context, qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE));
        
        var adjFaceIndices = [];
        for (var adjFace in adjFaces)
        {
            for (var faceIdx = 0; faceIdx < size(faces); faceIdx += 1)
            {
                if (areQueriesEquivalent(context, adjFace, faces[faceIdx]))
                {
                    adjFaceIndices = append(adjFaceIndices, faceIdx);
                    faceToEdgeIndices[faceIdx] = append(faceToEdgeIndices[faceIdx], edgeIdx);
                    break;
                }
            }
        }
        edgeToFaceIndices[edgeIdx] = adjFaceIndices;
    }
    
    // Cache edge curves (3D) - keyed by edgeIdx
    var edgeCurves = {};
    for (var edgeIdx = 0; edgeIdx < size(edges); edgeIdx += 1)
    {
        edgeCurves[edgeIdx] = evApproximateBSplineCurve(context, { "edge" : edges[edgeIdx] });
    }
    
    return {
        "body" : body,
        "bodyIdx" : bodyIdx,
        "faces" : faces,
        "edges" : edges,
        "faceToEdgeIndices" : faceToEdgeIndices,
        "edgeToFaceIndices" : edgeToFaceIndices,
        "edgeCurves" : edgeCurves
    };
}

// =============================================================================
// MAIN ENTRY: Intersect plane with preprocessed bodies
// =============================================================================

/**
 * Compute intersection curves between a plane and all preprocessed bodies.
 * Replaces opIntersectFaces.
 */
export function intersectPlaneWithBodies(context is Context, preprocessedData is map, sectionPlane is Plane) returns array
{
    var allCurves = [];
    var planeNormal = sectionPlane.normal;
    var planeOrigin = sectionPlane.origin;
    
    for (var bodyIdx = 0; bodyIdx < size(preprocessedData.bodies); bodyIdx += 1)
    {
        var bodyData = preprocessedData.bodyData[bodyIdx];
        var bodyCurves = intersectPlaneWithBody(bodyData, planeNormal, planeOrigin);
        
        // Tag curves with body info
        for (var curve in bodyCurves)
        {
            allCurves = append(allCurves, {
                "BSplineCurve" : curve,
                "bodies" : [bodyData.body],
                "bodyIndices" : [bodyIdx]
            });
        }
    }
    
    return allCurves;
}

/**
 * Intersect plane with a single preprocessed body.
 * No context calls - pure math on cached data.
 */
function intersectPlaneWithBody(bodyData is map, planeNormal is Vector, planeOrigin is Vector) returns array
{
    // Step 1: Classify all edges (keyed by edgeIdx)
    var edgeResults = {};
    for (var edgeIdx = 0; edgeIdx < size(bodyData.edges); edgeIdx += 1)
    {
        var curve = bodyData.edgeCurves[edgeIdx];
        edgeResults[edgeIdx] = classifyEdgeAgainstPlane(curve, planeNormal, planeOrigin);
    }
    
    // Step 2: Process each face
    var outputCurves = [];
    
    for (var faceIdx = 0; faceIdx < size(bodyData.faces); faceIdx += 1)
    {
        var faceEdgeIndices = bodyData.faceToEdgeIndices[faceIdx];
        var faceCurves = processFaceIntersection(faceEdgeIndices, edgeResults, planeNormal, planeOrigin);
        outputCurves = concatenateArrays(outputCurves, faceCurves);
    }
    
    return outputCurves;
}

// =============================================================================
// EDGE CLASSIFICATION
// =============================================================================

/**
 * Classify an edge against the plane: coplanar, crossing, or no intersection.
 * No context calls - works entirely on cached B-spline data.
 */
function classifyEdgeAgainstPlane(curve is BSplineCurve, planeNormal is Vector, planeOrigin is Vector) returns map
{
    var knots = curve.knots;
    var uMin = knots[0];
    var uMax = knots[size(knots) - 1];
    
    var params = [];
    for (var i = 0; i < EDGE_SAMPLES; i += 1)
    {
        var t = i / (EDGE_SAMPLES - 1);
        params = append(params, uMin + t * (uMax - uMin));
    }
    
    var points = evaluateSpline({ "spline" : curve, "parameters" : params })[0];
    
    var samples = [];
    for (var i = 0; i < size(params); i += 1)
    {
        samples = append(samples, {
            "param" : params[i],
            "point3D" : points[i],
            "classification" : classifyPointToPlane(points[i], planeNormal, planeOrigin)
        });
    }
    
    // Check if coplanar
    if (isAllOn(samples))
    {
        return { 
            "edgeStatus" : "COPLANAR", 
            "curve" : curve
        };
    }
    
    // Find crossings
    var crossings = findEdgeCrossings(curve, samples, planeNormal, planeOrigin);
    
    if (size(crossings) == 0)
    {
        return { "edgeStatus" : "NONE" };
    }
    
    return {
        "edgeStatus" : "CROSSING",
        "crossings" : crossings
    };
}

/**
 * Classify a point relative to the plane.
 */
function classifyPointToPlane(point is Vector, planeNormal is Vector, planeOrigin is Vector) returns PlaneClassification
{
    var dist = dot(point - planeOrigin, planeNormal);
    
    if (dist > PLANE_TOLERANCE)
    {
        return PlaneClassification.ABOVE;
    }
    else if (dist < -PLANE_TOLERANCE)
    {
        return PlaneClassification.BELOW;
    }
    else
    {
        return PlaneClassification.ON;
    }
}

/**
 * Check if all samples are ON the plane.
 */
function isAllOn(samples is array) returns boolean
{
    for (var sample in samples)
    {
        if (sample.classification != PlaneClassification.ON)
        {
            return false;
        }
    }
    return true;
}

/**
 * Find crossing points along an edge.
 */
function findEdgeCrossings(curve is BSplineCurve, samples is array,
                           planeNormal is Vector, planeOrigin is Vector) returns array
{
    var crossings = [];
    
    for (var i = 0; i < size(samples) - 1; i += 1)
    {
        var current = samples[i];
        var next = samples[i + 1];
        
        var crossingStatus = detectCrossingStatus(current, next);
        
        if (crossingStatus == "REAL")
        {
            var refined = refineCrossing(curve, current.param, next.param, planeNormal, planeOrigin);
            crossings = append(crossings, refined);
        }
        else if (crossingStatus == "NEXT_IS_ON")
        {
            crossings = append(crossings, {
                "param" : next.param,
                "point3D" : next.point3D
            });
        }
    }
    
    return crossings;
}

/**
 * Detect crossing status between two samples.
 */
function detectCrossingStatus(current is map, next is map) returns string
{
    var c = current.classification;
    var n = next.classification;
    
    if ((c == PlaneClassification.ABOVE && n == PlaneClassification.BELOW) ||
        (c == PlaneClassification.BELOW && n == PlaneClassification.ABOVE))
    {
        return "REAL";
    }
    
    if (c != PlaneClassification.ON && n == PlaneClassification.ON)
    {
        return "NEXT_IS_ON";
    }
    
    return "NONE";
}

/**
 * Binary search to refine crossing location.
 */
function refineCrossing(curve is BSplineCurve, tLow is number, tHigh is number,
                        planeNormal is Vector, planeOrigin is Vector) returns map
{
    const MAX_ITERATIONS = 20;
    
    var lowPt = evaluateSpline({ "spline" : curve, "parameters" : [tLow] })[0][0];
    var lowClass = classifyPointToPlane(lowPt, planeNormal, planeOrigin);
    
    for (var i = 0; i < MAX_ITERATIONS; i += 1)
    {
        if (tHigh - tLow < PARAM_TOLERANCE)
        {
            break;
        }
        
        var tMid = (tLow + tHigh) / 2;
        var midPt = evaluateSpline({ "spline" : curve, "parameters" : [tMid] })[0][0];
        var midClass = classifyPointToPlane(midPt, planeNormal, planeOrigin);
        
        if (midClass == PlaneClassification.ON)
        {
            return { "param" : tMid, "point3D" : midPt };
        }
        
        if (lowClass != midClass)
        {
            tHigh = tMid;
        }
        else
        {
            tLow = tMid;
        }
    }
    
    var finalT = (tLow + tHigh) / 2;
    var finalPt = evaluateSpline({ "spline" : curve, "parameters" : [finalT] })[0][0];
    
    return { "param" : finalT, "point3D" : finalPt };
}

// =============================================================================
// FACE INTERSECTION PROCESSING
// =============================================================================

/**
 * Process a face's intersection with the plane.
 * Returns array of B-spline curves.
 * 
 * Coplanar edges are output directly.
 * Crossing pairs are connected with line segments.
 */
function processFaceIntersection(faceEdgeIndices is array, edgeResults is map,
                                  planeNormal is Vector, planeOrigin is Vector) returns array
{
    var coplanarCurves = [];
    var crossings = [];
    
    for (var edgeIdx in faceEdgeIndices)
    {
        var result = edgeResults[edgeIdx];
        
        if (result == undefined)
        {
            continue;
        }
        
        if (result.edgeStatus == "COPLANAR")
        {
            coplanarCurves = append(coplanarCurves, result.curve);
        }
        else if (result.edgeStatus == "CROSSING")
        {
            for (var crossing in result.crossings)
            {
                crossings = append(crossings, {
                    "point3D" : crossing.point3D,
                    "edgeIdx" : edgeIdx
                });
            }
        }
    }
    
    var outputCurves = [];
    
    // Coplanar edges are direct output (exact curves)
    for (var curve in coplanarCurves)
    {
        outputCurves = append(outputCurves, curve);
    }
    
    // Connect crossing pairs with line segments
    if (size(crossings) >= 2)
    {
        var lineCurves = generateLineSegments(crossings);
        outputCurves = concatenateArrays(outputCurves, lineCurves);
    }
    
    return outputCurves;
}

// =============================================================================
// LINE SEGMENT GENERATION
// =============================================================================

/**
 * Generate line segments connecting crossing pairs.
 * Simple and fast - connects 0->1, 2->3, etc.
 */
function generateLineSegments(crossings is array) returns array
{
    if (size(crossings) < 2)
    {
        return [];
    }
    
    var curves = [];
    
    // Simple pairing: connect crossing 0->1, 2->3, etc.
    for (var i = 0; i + 1 < size(crossings); i += 2)
    {
        var entry = crossings[i];
        var exit = crossings[i + 1];
        
        // Degree-1 B-spline = line segment
        var lineCurve = bSplineCurve({
            "degree" : 1,
            "isPeriodic" : false,
            "controlPoints" : [entry.point3D, exit.point3D],
            "knots" : [0, 0, 1, 1] as KnotArray
        });
        
        curves = append(curves, lineCurve);
    }
    
    return curves;
}
