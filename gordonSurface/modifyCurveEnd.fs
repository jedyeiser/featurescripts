FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import tools/bspline_knots.fs
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");
// import tools/frenet
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a19a275a032ee47f4dbcc83c", version : "65e923a8d375058271c92fbc");
//import tools/transition_functions (export/import)
export import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a656fa0d17723f0dafaf8638", version : "56689ead56dff6bcc596641b");
//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "b463eaf5c39ae77152ed2484");
//import scaledCurve
import(path : "2dfee1d44e9bde0daba9d73e", version : "c685211f07cd7af59f25cb37");
//import debugTools
import(path : "3f40c735a406f3df927e0b13", version : "ca97f371da515817e2e1c16b");

//import continuityTools
import(path : "6db2a56b5418f71818d7a607", version : "f5e90edbacec0ec0ff42136f");
//import curveOps
import(path : "73de71e75b755f0042e0e6d8", version : "17f13062b9e754fa7ad0d5bf");



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

        annotation { "Name" : "Project onto surface?" }
        definition.curveOnSurface is boolean;

        if (definition.curveOnSurface)
        {
            annotation { "Name" : "Projection face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
            definition.projectionFace is Query;
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

        var modifiedCurve = modifyCurveEnd(context, unifiedCurve, modParam, useOffset, definition.offsetFrame, definition.transitionType, definition.fixedEndContinuity, definition.g2Mode, useRef, definition.modEndContinuity, numSamples, definition.splineDegree, definition.splineTol);

        if (definition.curveOnSurface)
        {
            modifiedCurve = projectCurveOnSurface(context, modifiedCurve, definition.projectionFace, numSamples, definition.splineTol);
        }

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

// computeRefContinuityConstraints, computeEdgeContinuityConstraints,
// computeFaceContinuityConstraints, enforceG1AtEnd, enforceG2AtEnd
// → moved to continuityTools.fs

// Frenet frame operations now handled by tools/frenet.fs
// Available functions:
// - FRENET_EPSILON
// - computeFrenetFrame(curve, s)
// - frenetVectorToWorld(localVector, frenetResult)
// - worldVectorToFrenet(worldVector, frenetResult)
// - frenetPointToWorld(localPoint, frenetResult)
// - worldPointToFrenet(worldPoint, frenetResult)
