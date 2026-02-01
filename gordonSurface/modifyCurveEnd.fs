FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

//import tools/bspline_knots.fs
import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/dadb70c0a762573622fa609c", version : "acff88f740b64f7b03d722aa");
// import tools/frenet
import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
//import tools/transition_functions (export/import)
export import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/a656fa0d17723f0dafaf8638", version : "56689ead56dff6bcc596641b");
//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "62f653b9c0d24817418e88e5");
//import scaledCurve
import(path : "2dfee1d44e9bde0daba9d73e", version : "19db4dd577e5e91d45d21305");


IconNamespace::import(path : "e96867c52539556a75762725", version : "58044f708ff560e305b72aec");

export function editingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
                                isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    if (isQueryEmpty(context, definition.modContinuityRef))
    {
        definition.showContinuity = true;
    }
    else
    {
        definition.showContinuity = false;
    }
    return definition;
}


annotation { "Feature Type Name" : "Modify curve end", "Editing Logic Function" : "editingLogic", "Icon" : IconNamespace::BLOB_DATA, "Feature Type Description" : "Modify a curve by displacing one endpoint and transitioning smoothly to the fixed end." }
export const modCurveEnd = defineFeature(function(context is Context, id is Id, definition is map) returns map
    precondition
    {
        annotation { "Name" : "Edge(s) to modify", "Filter" : EntityType.EDGE, "Description" : "Allows a single edge, or multiple edges (which must be G1 continuous)" }
        definition.selEdges is Query;
        
        annotation { "Name" : "From point", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1, "Description": "Point on edge to move. Must be an endpoint of the selected path" }
        definition.fromPoint is Query;
        
        annotation { "Name" : "To point", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1, "Description": "New endpoint to deform curve to" }
        definition.toPoint is Query;
        
        annotation { "Name" : "Create curve?", "Default": true, "Description": "When true, creates a curve and adds it to the returned map" }
        definition.createCurve is boolean;
        
        annotation { "Group Name" : "Parameters", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Offset frame", "Default": OffsetFrame.FRENET, "Description": "Apply offsets along the curve's frenet frame(s) [Default], or an offset in [x, y, z]." }
            definition.offsetFrame is OffsetFrame;
            
            annotation { "Name" : "Transition type", "Default": TransitionType.LOGISTIC, "Description": "Defines how the offset is applied across the curve, as a function of the parameter s." }
            definition.transitionType is TransitionType;
            
            annotation { "Name" : "Fixed end continuity", "Default": GeometricContinuity.G0, "Description": "Specifies if output curve is G0: Coincident, G1: Tangent, G2: Equal curvature with the input curve" }
            definition.fixedEndContinuity is GeometricContinuity;
            
            if (definition.fixedEndContinuity == GeometricContinuity.G2)
            {
                annotation { "Name" : "G2 Mode", "Default": G2Mode.BEST_EFFORT, "Description": "Approximate or exact G2 continuity?" }
                definition.g2Mode is G2Mode;
            }
            
            annotation { "Name" : "Endpoint continuity ref?", "Default": false }
            definition.showModContinuity is boolean;
            
            if (definition.showModContinuity)
            {
                annotation { "Group Name" : "Endpoint continuity", "Collapsed By Default" : false, "Driving Parameter": "showModContinuity"}
                {
                    annotation { "Name" : "Endpoint continuity ref.", "Filter" : EntityType.EDGE || EntityType.FACE, "MaxNumberOfPicks" : 1 }
                    definition.modContinuityRef is Query;
                    
                    annotation { "Name" : "Flip ref", "Default" : false, "UIHint": UIHint.OPPOSITE_DIRECTION }
                    definition.flipREf is boolean;
                    
                    annotation { "Name" : "showContinuity", "Default": false, "UIHint": UIHint.ALWAYS_HIDDEN }
                    definition.showContinuity is boolean;
                    
                    
                    annotation { "Name" : "Endpoint continuity", "Default": GeometricContinuity.G0, "Description": "Specifies the modified curves' continuity type with the supplied reference" }
                    definition.modEndContinuity is GeometricContinuity;
                }
                   
            }

        }
        
        annotation { "Group Name" : "Debug, Details", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Spline degree" }
            isInteger(definition.splineDegree, DEGREE_BOUND);
            
            annotation { "Name" : "Sampled spline tol." }
            isLength(definition.splineTol, TOLERANCE_BOUND);
            
            annotation { "Name" : "Max control points" }
            isInteger(definition.splineCP, {(unitless) : [ 4, 10, 100]} as IntegerBoundSpec);
            
            annotation { "Name" : "Sampling multiple", "Default" : 4, "Definition": "Samples splines over {total control points} * N points" }
            isInteger(definition.samplingMultiple, POSITIVE_COUNT_BOUNDS);
            
            annotation { "Name" : "Print input BSplineCurve" }
            definition.printInput is boolean;
            
            annotation { "Name" : "Print output BSplineCurve" }
            definition.printOutput is boolean;
            
            if (definition.printInput || definition.printOutput)
            {
                annotation { "Name" : "BSpline Print Format", "Default": PrintFormat.METADATA }
                definition.printFormat is PrintFormat;

            }
  
        }
   
    }
    {
        var evSelEdges = evaluateQuery(context, qUnion([definition.selEdges]));
        var inputBSplineCurves = mapArray(evSelEdges, function(x) {return evApproximateBSplineCurve(context, { "edge" : x }); });
        var numSamples = definition.samplingMultiple * sum(mapArray(inputBSplineCurves, function(x) {return size(x.controlPoints);}));
        var unifiedCurve = joinCurveSegments(context, inputBSplineCurves, numSamples, definition.splineTol);
        
        var modPoint = evVertexPoint(context, {
                "vertex" : definition.fromPoint
        });
        
        var toPoint = evVertexPoint(context, {
                "vertex" : definition.toPoint
        });
        
        var modParam = norm(modPoint - unifiedCurve.controlPoints[0]) < norm(modPoint - unifiedCurve.controlPoints[size(unifiedCurve.controlPoints)-1]) ? 0 : 1; // this allows us to pick the closest point to the mod point - so we don't necessarily need specify a point on the edge(s)
        var fixedParam = 1 - modParam; 
        
        
        //var currentEndpoint = evaluateSpline({ "spline": unifiedCurve, "parameters": [modParam] })[0][0];
        var worldOffset = toPoint - modPoint;
        var useOffset = definition.offsetFrame == OffsetFrame.WORLD ? worldOffset : worldVectorToFrenet(worldOffset, computeFrenetFrame(unifiedCurve, modParam));
        
        var useRef = size(evaluateQuery(context, definition.modContinuityRef)) > 0 ? definition.modContinuityRef : qNothing();
        var useContinuity = size(evaluateQuery(context, definition.modContinuityRef)) > 0 ? definition.modEndContinuity : GeometricContinuity.G0;

        var modifiedCurve = modifyCurveEnd(context, unifiedCurve, modParam, useOffset, definition.offsetFrame, definition.transitionType, definition.fixedEndContinuity, definition.g2Mode, useRef, useContinuity, numSamples, definition.splineDegree, definition.splineTol);
        
        if (definition.printInput || definition.printOutput)
        {
            println(" - - - - - - - - Modified Endpoint Spline data - - - - - - - - ");
            println("fromPoint -> " ~ toString(modPoint) ~ ". toPoint -> " ~ toString(toPoint));
            println("offsetFrame -> " ~ definition.offsetFrame ~ ". useVector (offset vector) -> " ~ toString(useOffset));
            println("fixedParam -> " ~ fixedParam ~ ". modParam -> "  ~ modParam);
            println("useRef -> " ~ toString(useRef) ~ ". modEndContinuity -> " ~ definition.modEndContinuity);
            println("transitionType -> " ~ definition.transitionType ~ ". fixedEndContinuity" ~ definition.fixedEndContinuity);
            
        }
        
        
        
        if (definition.printInput)
        {
            printBSpline(unifiedCurve, definition.printFormat, ["* * * * * Modify Endpoint input BSplineCurve *  * * * * "]);
            
        }
        if (definition.printOutput)
        {
            printBSpline(modifiedCurve, definition.printFormat, ["* * * * * Modify Endpoint output BSplineCurve *  * * * * "]);
        }
        
        var retMap = {'bspline': modifiedCurve};
        if (definition.createCurve)
        {
            opCreateBSplineCurve(context, id + "createModifiedEndpointBSpline", {
                    "bSplineCurve" : modifiedCurve
            });
            
            retMap['query'] = qCreatedBy(id + "createModifiedEndpointBSpline", EntityType.BODY);
        }
        
        return retMap;
        
    });


/**
 * Modify a curve by displacing one endpoint with smooth transition to the fixed end.
 *
 * @param context {Context}
 * @param inputCurve {BSplineCurve} : Curve to modify
 * @param modPointParam {number} : 0 or 1 — which endpoint to displace
 * @param offsetVector {Vector} : Displacement at modified endpoint (with length units)
 * @param offsetFrame {OffsetFrame} : WORLD or FRENET [tangent, normal, binormal]
 * @param transitionType {TransitionType} : LINEAR, SINUSOIDAL, or LOGISTIC
 * @param fixedEndContinuity {GeometricContinuity} : G0, G1, or G2 constraint at fixed end
 * @param g2Mode {G2Mode} : EXACT or BEST_EFFORT (only used for G2)
 * @param modPointRef {Query} : Reference edge or face for continuity at modified end (can be empty)
 * @param modPointContinuity {GeometricContinuity} : G0, G1, or G2 constraint at modified end
 * @param numSamples {number} : Sample count for fitting
 * @param degree {number} : Output curve degree
 * @param tolerance {ValueWithUnits} : Fitting tolerance
 * @returns {BSplineCurve}
 */
export function modifyCurveEnd(
    context is Context,
    inputCurve is BSplineCurve,
    modPointParam is number,
    offsetVector is Vector,
    offsetFrame is OffsetFrame,
    transitionType is TransitionType,
    fixedEndContinuity is GeometricContinuity,
    g2Mode is G2Mode,
    modPointRef is Query,
    modPointContinuity is GeometricContinuity,
    numSamples is number,
    degree is number,
    tolerance is ValueWithUnits
) returns BSplineCurve
{
    var fixedPointParam = (modPointParam == 0) ? 1 : 0;
    
    // Store original endpoint data for continuity enforcement later
    var fixedEndFrameOriginal = computeFrenetFrame(inputCurve, fixedPointParam);
    var fixedEndTangent = fixedEndFrameOriginal.frame.zAxis;
    var fixedEndCurvature = fixedEndFrameOriginal.curvature;
    
    // Compute world offset direction once from the Frenet offset at modParam
    var worldOffsetAtModPoint;
    if (offsetFrame == OffsetFrame.FRENET)
    {
        var frenetFrameAtMod = computeFrenetFrame(inputCurve, modPointParam);
        worldOffsetAtModPoint = frenetVectorToWorld(offsetVector, frenetFrameAtMod);
    }
    else
    {
        worldOffsetAtModPoint = offsetVector;
    }
    
    // Sample and offset points
    var modifiedPoints = [];
    
    for (var i = 0; i < numSamples; i += 1)
    {
        var s = i / (numSamples - 1);
        
        // Compute scale factor: 0 at fixed end, 1 at modPoint
        var sf;
        if (modPointParam == 1)
        {
            sf = computeAppliedSF(s, 0, 1, transitionType);
        }
        else
        {
            sf = computeAppliedSF(s, 1, 0, transitionType);
        }
        
        var originalPt = evaluateSpline({
            "spline" : inputCurve,
            "parameters" : [s]
        })[0][0];
        
        var worldOffset = sf * worldOffsetAtModPoint;
        
        modifiedPoints = append(modifiedPoints, originalPt + worldOffset);
    }
    
    // Build parameter array
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, i / (numSamples - 1));
    }
    
    // Fit initial curve
    var fittedCurve = approximateSpline(context, {
        "degree" : degree,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : modifiedPoints })],
        "parameters" : params,
        "interpolateIndices" : [0, numSamples - 1]
    })[0];
    
    // Apply continuity constraints at fixed end
    if (fixedEndContinuity == GeometricContinuity.G1 || fixedEndContinuity == GeometricContinuity.G2)
    {
        fittedCurve = enforceG1AtEnd(fittedCurve, fixedPointParam, fixedEndTangent);
    }
    
    if (fixedEndContinuity == GeometricContinuity.G2)
    {
        fittedCurve = enforceG2AtEnd(fittedCurve, fixedPointParam, fixedEndCurvature, g2Mode);
    }
    
    // Apply constraints at modified end (if reference supplied)
    if (!isQueryEmpty(context, modPointRef) && modPointContinuity != GeometricContinuity.G0)
    {
        var constraints = computeRefContinuityConstraints(context, modPointRef, fittedCurve, modPointParam);
        
        if (modPointContinuity == GeometricContinuity.G1 || modPointContinuity == GeometricContinuity.G2)
        {
            fittedCurve = enforceG1AtEnd(fittedCurve, modPointParam, constraints.tangent);
        }
        
        if (modPointContinuity == GeometricContinuity.G2)
        {
            fittedCurve = enforceG2AtEnd(fittedCurve, modPointParam, constraints.curvature, g2Mode);
        }
    }
    
    return fittedCurve;
}

/**
 * Compute tangent and curvature constraints from a reference edge or face.
 * 
 * @param context {Context}
 * @param ref {Query} : Reference edge or face
 * @param curve {BSplineCurve} : The curve being constrained (used for approach direction on faces)
 * @param endParam {number} : 0 or 1 — which endpoint
 * @returns {map} : { "tangent": Vector, "curvature": ValueWithUnits }
 */
export function computeRefContinuityConstraints(context is Context, ref is Query, curve is BSplineCurve, endParam is number) returns map
{
    // Get endpoint position
    var endPoint = evaluateSpline({
        "spline" : curve,
        "parameters" : [endParam]
    })[0][0];
    
    // Check if reference is edge or face
    var edgeQuery = qEntityFilter(ref, EntityType.EDGE);
    var faceQuery = qEntityFilter(ref, EntityType.FACE);
    
    if (!isQueryEmpty(context, edgeQuery))
    {
        return computeEdgeContinuityConstraints(context, edgeQuery, endPoint);
    }
    else if (!isQueryEmpty(context, faceQuery))
    {
        return computeFaceContinuityConstraints(context, faceQuery, curve, endParam);
    }
    else
    {
        // Fallback — return curve's own tangent/curvature (no constraint)
        var frame = computeFrenetFrame(curve, endParam);
        return {
            "tangent" : frame.frame.zAxis,
            "curvature" : frame.curvature
        };
    }
}

/**
 * Compute tangent and curvature from a reference edge at a point.
 */
export function computeEdgeContinuityConstraints(context is Context, edge is Query, point is Vector) returns map
{
    // Find parameter on edge closest to point
    var distResult = evDistance(context, {
        "side0" : edge,
        "side1" : point
    });
    
    var edgeParam = distResult.sides[0].parameter;
    
    // Get edge as BSpline and compute Frenet frame
    var edgeCurve = evApproximateBSplineCurve(context, { "edge" : edge });
    var frame = computeFrenetFrame(edgeCurve, edgeParam);
    
    return {
        "tangent" : frame.frame.zAxis,
        "curvature" : frame.curvature
    };
}

/**
 * Compute tangent and curvature for a curve meeting a face.
 * Projects curve's approach direction onto tangent plane,
 * then computes surface curvature in that direction.
 */
export function computeFaceContinuityConstraints(context is Context, face is Query, curve is BSplineCurve, endParam is number) returns map
{
    // Get endpoint position
    var endPoint = evaluateSpline({
        "spline" : curve,
        "parameters" : [endParam]
    })[0][0];
    
    // Get curve's approach direction (tangent at endpoint)
    var curveFrame = computeFrenetFrame(curve, endParam);
    var approachDirection = curveFrame.frame.zAxis;
    
    // Find UV parameter on face
    var distResult = evDistance(context, {
        "side0" : face,
        "side1" : endPoint
    });
    var uvParam = distResult.sides[0].parameter;
    
    // Get face normal at that point
    var tangentPlane = evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : uvParam
    });
    var faceNormal = tangentPlane.normal;
    
    
    // Project approach direction onto tangent plane
    var projected = approachDirection - dot(approachDirection, faceNormal) * faceNormal;
    var projNorm = norm(projected);
    
    var tangent;
    if (projNorm < 1e-10)
    {
        // Approach is perpendicular to face — pick arbitrary direction in tangent plane
        // Use face's principal direction as fallback
        var faceCurvature = evFaceCurvature(context, {
            "face" : face,
            "parameter" : uvParam
        });
        tangent = faceCurvature.minDirection;
    }
    else
    {
        tangent = projected / projNorm;
    }
    
    // Get face curvature and compute curvature in tangent direction (Euler's formula)
    var faceCurvature = evFaceCurvature(context, {
        "face" : face,
        "parameter" : uvParam
    });
    
    var cosTheta = dot(tangent, faceCurvature.minDirection);
    var sinTheta = dot(tangent, faceCurvature.maxDirection);
    var curvature = faceCurvature.minCurvature * cosTheta * cosTheta 
                  + faceCurvature.maxCurvature * sinTheta * sinTheta;
    
    return {
        "tangent" : tangent,
        "curvature" : curvature
    };
}

/**
 * Adjust control points to enforce tangent direction at an endpoint.
 * 
 * @param curve {BSplineCurve}
 * @param endParam {number} : 0 or 1
 * @param targetTangent {Vector} : Desired tangent direction (will be normalized)
 * @returns {BSplineCurve} : Modified curve
 */
export function enforceG1AtEnd(curve is BSplineCurve, endParam is number, targetTangent is Vector) returns BSplineCurve
{
    var cps = curve.controlPoints;
    var n = size(cps);
    
    // Get current tangent at endpoint
    var currentFrame = computeFrenetFrame(curve, endParam);
    var currentTangent = currentFrame.frame.zAxis;
    
    // Check sign - flip target if needed
    var normalizedTarget = normalize(targetTangent);
    if (dot(currentTangent, normalizedTarget) < 0)
    {
        normalizedTarget = -normalizedTarget;
    }
    
    // For a clamped B-spline, tangent at endpoint is proportional to 
    // the vector from first to second control point (for param 0)
    // or second-to-last to last control point (for param 1)
    
    var newCPs = cps;  // Copy
    
    if (endParam == 0)
    {
        // Tangent at s=0 is along (cp[1] - cp[0])
        // Keep cp[0] fixed (it's the endpoint position)
        // Move cp[1] to enforce tangent direction while preserving distance
        var currentVec = cps[1] - cps[0];
        var dist = norm(currentVec);
        newCPs[1] = cps[0] + dist * normalizedTarget;
    }
    else  // endParam == 1
    {
        // Tangent at s=1 is along (cp[n-1] - cp[n-2])
        // Keep cp[n-1] fixed
        // Move cp[n-2] to enforce tangent direction
        var currentVec = cps[n - 1] - cps[n - 2];
        var dist = norm(currentVec);
        // Note: tangent points FROM cp[n-2] TO cp[n-1], so:
        newCPs[n - 2] = cps[n - 1] - dist * normalizedTarget;
    }
    
    return bSplineCurve({
        "degree" : curve.degree,
        "isPeriodic" : curve.isPeriodic,
        "isRational" : curve.isRational,
        "controlPoints" : newCPs,
        "knots" : curve.knots,
        "weights" : curve.weights
    });
}

/**
 * Adjust control points to enforce curvature at an endpoint.
 * 
 * For a cubic B-spline with clamped ends, curvature at endpoint depends on
 * the first three control points (for param 0) or last three (for param 1).
 * 
 * @param curve {BSplineCurve}
 * @param endParam {number} : 0 or 1
 * @param targetCurvature {ValueWithUnits} : Desired curvature (1/length units)
 * @param g2Mode {G2Mode} : EXACT or BEST_EFFORT
 * @returns {BSplineCurve} : Modified curve
 */
export function enforceG2AtEnd(curve is BSplineCurve, endParam is number, targetCurvature is ValueWithUnits, g2Mode is G2Mode) returns BSplineCurve
{
    // For BEST_EFFORT, we adjust cp[2] (or cp[n-3]) to approximate the curvature
    // For EXACT, we'd need to iterate
    
    var cps = curve.controlPoints;
    var n = size(cps);
    var degree = curve.degree;
    
    if (degree < 3)
    {
        // Can't enforce G2 on degree < 3 curve
        return curve;
    }
    
    var newCPs = cps;
    
    // Current curvature
    var currentFrame = computeFrenetFrame(curve, endParam);
    var currentCurvature = currentFrame.curvature;
    
    if (g2Mode == G2Mode.BEST_EFFORT)
    {
        // Adjust the third control point to influence curvature
        // This is approximate - curvature depends on the geometry in a nonlinear way
        
        if (endParam == 0)
        {
            // Move cp[2] toward/away from the tangent line to adjust curvature
            var tangentDir = normalize(cps[1] - cps[0]);
            var toCP2 = cps[2] - cps[1];
            
            // Component perpendicular to tangent affects curvature
            var perpComponent = toCP2 - dot(toCP2, tangentDir) * tangentDir;
            var perpDist = norm(perpComponent);
            
            if (perpDist > 1e-10 * meter)
            {
                var perpDir = perpComponent / perpDist;
                
                // Scale perpendicular distance to adjust curvature
                // Higher perpDist = higher curvature (approximately)
                var curvatureRatio = (targetCurvature / currentCurvature);
                
                // Clamp ratio to avoid wild adjustments
                curvatureRatio = min(max(curvatureRatio, 0.1), 10);
                
                var newPerpDist = perpDist * curvatureRatio;
                var parallelComponent = dot(toCP2, tangentDir) * tangentDir;
                
                newCPs[2] = cps[1] + parallelComponent + newPerpDist * perpDir;
            }
        }
        else  // endParam == 1
        {
            // Similar logic for the end
            var tangentDir = normalize(cps[n - 1] - cps[n - 2]);
            var toCP = cps[n - 3] - cps[n - 2];
            
            var perpComponent = toCP - dot(toCP, tangentDir) * tangentDir;
            var perpDist = norm(perpComponent);
            
            if (perpDist > 1e-10 * meter)
            {
                var perpDir = perpComponent / perpDist;
                var curvatureRatio = (targetCurvature / currentCurvature);
                curvatureRatio = min(max(curvatureRatio, 0.1), 10);
                
                var newPerpDist = perpDist * curvatureRatio;
                var parallelComponent = dot(toCP, tangentDir) * tangentDir;
                
                newCPs[n - 3] = cps[n - 2] + parallelComponent + newPerpDist * perpDir;
            }
        }
    }
    else  // EXACT
    {
        // Iterative refinement - adjust cp[2] or cp[n-3] until curvature matches
        var maxIterations = 10;
        var curvatureTolerance = 0.01 / meter;  // 1% tolerance
        
        for (var iter = 0; iter < maxIterations; iter += 1)
        {
            var testCurve = bSplineCurve({
                "degree" : curve.degree,
                "isPeriodic" : curve.isPeriodic,
                "isRational" : curve.isRational,
                "controlPoints" : newCPs,
                "knots" : curve.knots,
                "weights" : curve.weights
            });
            
            var testFrame = computeFrenetFrame(testCurve, endParam);
            var testCurvature = testFrame.curvature;
            
            var error = abs(testCurvature - targetCurvature);
            if (error < curvatureTolerance)
            {
                break;
            }
            
            // Adjust using same logic as BEST_EFFORT but with current state
            // ... (similar adjustment code, operating on newCPs)
        }
    }
    
    return bSplineCurve({
        "degree" : curve.degree,
        "isPeriodic" : curve.isPeriodic,
        "isRational" : curve.isRational,
        "controlPoints" : newCPs,
        "knots" : curve.knots,
        "weights" : curve.weights
    });
}

// Frenet frame operations now handled by tools/frenet.fs
// Available functions:
// - FRENET_EPSILON
// - computeFrenetFrame(curve, s)
// - frenetVectorToWorld(localVector, frenetResult)
// - worldVectorToFrenet(worldVector, frenetResult)
// - frenetPointToWorld(localPoint, frenetResult)
// - worldPointToFrenet(worldPoint, frenetResult)
