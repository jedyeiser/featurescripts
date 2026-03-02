FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "b463eaf5c39ae77152ed2484");
//import tools/bspline_knots
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/dadb70c0a762573622fa609c", version : "2267a758e66498ac49f4601e");
//import tools/transition_functions (export.import)
export import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/a656fa0d17723f0dafaf8638", version : "56689ead56dff6bcc596641b");
//import debugTools
import(path : "3f40c735a406f3df927e0b13", version : "ca97f371da515817e2e1c16b");
//import curveOps
import(path : "73de71e75b755f0042e0e6d8", version : "17f13062b9e754fa7ad0d5bf");



annotation { "Feature Type Name" : "Scaled Curve", "Feature Type Description" : "Creates a new BSplineCurve as a scaled combination of the two input curves" }
export const createScaledCurve = defineFeature(function(context is Context, id is Id, definition is map) returns map
    precondition
    {
        annotation { "Name" : "Group 0", "Filter" : EntityType.EDGE}
        definition.group0 is Query;

        annotation { "Name" : "Flip?", "Defualt" : false, "UIHint": UIHint.OPPOSITE_DIRECTION, "Description": "Flip evaluation order of Group0" }
        definition.flip is boolean;

        annotation { "Name" : "Group 1", "Filter" : EntityType.EDGE}
        definition.group1 is Query;


        annotation { "Name" : "Initial curve scalefactor", "Description": "Where scaled curve should sit in [-0.5, 0.5] between Group 0 and Group 1. -.5 -> 100% Group 0. +.5 -> 100% Group 1"}
        isReal(definition.sf0, ScaledCurveParameterBounds);

        annotation { "Name" : "Final curve scalefactor", "Description": "Where scaled curve should sit in [-0.5, 0.5] between Group 0 and Group 1. -.5 -> 100% Group 0. +.5 -> 100% Group 1"}
        isReal(definition.sf1, ScaledCurveParameterBounds);

        annotation { "Name" : "Transition Type", "Default": TransitionType.LINEAR, "Description" : "How to transition from one scalefactor to another along our scaled curve" }
        definition.transitionType is TransitionType;

        annotation { "Name" : "Create curve?", "Default": false, "Description": "When true, creates a curve. Otherwise, just solves for the BSplineCurve" }
        definition.createCurve is boolean;

        if (definition.createCurve)
        {
            annotation { "Name" : "Curve name", "Description": "When not blank, the output wire body will get this name." }
            definition.curveName is string;

        }

        annotation { "Name" : "Project onto surface?" }
        definition.curveOnSurface is boolean;

        if (definition.curveOnSurface)
        {
            annotation { "Name" : "Projection face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
            definition.projectionFace is Query;
        }

        annotation { "Group Name" : "Debug & Details", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show endpoints", "Description": "When true, shows spline startpoints in GREEN and endpoints in RED", "Default": false }
            definition.showEndpoints is boolean;

            annotation { "Name" : "Show curves", "Description": "When true, shows Group 0 in CYAN and Group 1 in MAGENTA", "Default": false }
            definition.showGroups is boolean;

            annotation { "Name" : "Print BSplineCurve data", "Desription": "Prints bspline data for combined input groups and final curve", "Default": false }
            definition.printBsplines is boolean;

            if (definition.printBsplines)
            {
                annotation { "Name" : "Data Depth", "Description" : "Print metadata only, or all details for our Input and output curves" }
                definition.bsplineFormat is PrintFormat;

            }

            annotation { "Group Name" : "Details", "Collapsed By Default" : true }
            {
                annotation { "Group Name" : "Output parameters", "Collapsed By Default" : true }
                {
                    annotation { "Name" : "Scaled samples",  "Description": "Number of samples to use along our output curve. The control point count will be less than or equal to this value" }
                    isInteger(definition.numScaledSamples, SampleCountBounds);

                    annotation { "Name" : "Scaled tolerance", "Description": "Fit tolerance for output curve. Fits sampled data to within this tolerance. Onshape minimum of 1e-8 meter or 1e-5 millimeter"  }
                    isLength(definition.scaledTol, FitToleranceBounds);

                    annotation { "Name" : "Scaled curve degree", "Description": "Degree of scaled curve" }
                    isInteger(definition.scaledDegree, curveDegreeBounds);
                }


                annotation { "Group Name" : "Input parameters", "Collapsed By Default" : true }
                {
                    annotation { "Name" : "Number of samples along Group 0",  "Description": "Number of samples to use along Group 0 when creating a unified BSplineCurve. The control point count will be less than or equal to this value" }
                    isInteger(definition.group0SampleCount, SampleCountBounds);

                    annotation { "Name" : "Group 0 fit tolerance", "Description": "Fit tolerance for Group 0. Onshape minimum of 1e-8 meter or 1e-5 millimeter"  }
                    isLength(definition.group0Tol, FitToleranceBounds);

                    annotation { "Name" : "Number of samples along Group 1", "Description": "Number of samples to use along Group 1 when creating a unified BSplineCurve. The control point count will be less than or equal to this value"  }
                    isInteger(definition.group1SampleCount, SampleCountBounds);

                    annotation { "Name" : "Group 1 fit tolerance", "Description": "Fit tolerance for Group 0. Onshape minimum of 1e-8 meter or 1e-5 millimeter" }
                    isLength(definition.group1Tol, FitToleranceBounds);
                }


            }


        }

    }
    {
        const shiftedScalefactors = [0.5 + definition.sf0, 0.5 + definition.sf1];
        var group0_arr = evaluateQuery(context, qUnion([definition.group0]));
        var group1_arr = evaluateQuery(context, qUnion([definition.group1]));

        var bSpline0_arr = mapArray(group0_arr, function(x) {return evApproximateBSplineCurve(context, { "edge" : x } ); });
        var bSpline1_arr = mapArray(group1_arr, function(x) {return evApproximateBSplineCurve(context, { "edge" : x } ); });

        const curve0 = joinCurveSegments(context, bSpline0_arr, definition.group0SampleCount, definition.group0Tol);
        const curve1 = joinCurveSegments(context, bSpline1_arr, definition.group0SampleCount, definition.group0Tol);

        if (definition.showEndpoints)
        {
            var curve0Points = evaluateSpline({
                    "spline" : curve0,
                    "parameters" : [0, 1]
            });
            var curve1Points = evaluateSpline({
                    "spline" : curve1,
                    "parameters" : [0, 1]
            });

            addDebugPoint(context, curve0Points[0][0], DebugColor.GREEN);
            addDebugPoint(context, curve1Points[0][0], DebugColor.GREEN);
            addDebugPoint(context, curve0Points[0][1], DebugColor.RED);
            addDebugPoint(context, curve1Points[0][1], DebugColor.RED);
        }

        if (definition.showGroups)
        {
            addDebugEntities(context, qUnion(group0_arr), DebugColor.CYAN);
            addDebugEntities(context, qUnion(group1_arr), DebugColor.MAGENTA);
        }

        var retCurve = scaledCurve(context, curve0, curve1, definition.flip, shiftedScalefactors[0], shiftedScalefactors[1], definition.transitionType, definition.numScaledSamples, definition.scaledDegree, definition.scaledTol);

        if (definition.curveOnSurface)
        {
            retCurve = projectCurveOnSurface(context, retCurve, definition.projectionFace, definition.numScaledSamples, definition.scaledTol);
        }

        if (definition.printBsplines)
        {
            println("---------------- BSPLINE DATA ----------------");
            printBSpline(curve0, definition.bsplineFormat, [" - - - - - - Group 0 BSplineCurve - - - - - - "]);
            printBSpline(curve1, definition.bsplineFormat, [" - - - - - - Group 1 BSplineCurve - - - - - - "]);
            printBSpline(retCurve, definition.bsplineFormat, [" - - - - - - Scaled BSplineCurve - - - - - - "]);
            println("---------------- / BSPLINE DATA ----------------");
        }

        var retMap = {"bspline": retCurve};

        if (definition.createCurve)
        {
            opCreateBSplineCurve(context, id + "createScaledBsplineCurve", {
                    "bSplineCurve" : retCurve
            });

            var splineQ = qCreatedBy(id + "createScaledBsplineCurve", EntityType.BODY);
            if (length(definition.curveName) > 0)
            {
                setProperty(context, {
                        "entities" : splineQ,
                        "propertyType" : PropertyType.NAME,
                        "value" : definition.curveName
                });
            }

            retMap['query']= splineQ;
        }


        return retMap;


    });



// Transition functions now handled by tools/transition_functions.fs
// Available functions:
// - computeAppliedSF(s, sfStart, sfEnd, transitionType)
// - linearTransition(t)
// - sinusoidalTransition(t)
// - logisticTransition(t)

/**
 * Create a blended curve between two curves with variable scale factor.
 *
 * @param curve0 {BSplineCurve} : First boundary curve
 * @param curve1 {BSplineCurve} : Second boundary curve
 * @param flip {boolean} : If true, reverse parameterization of curve0
 * @param sf_0 {number} : Scale factor at S=0 (0 = all curve0, 1 = all curve1)
 * @param sf_1 {number} : Scale factor at S=1
 * @param transition {TransitionType} : How scale factor changes
 * @param numSamples {number} : Number of sample points for fitting
 * @param tolerance {ValueWithUnits} : Fitting tolerance for approximateSpline
 */
export function scaledCurve(context is Context, curve0 is BSplineCurve, curve1 is BSplineCurve, flip is boolean, sf_0 is number, sf_1 is number, transition is TransitionType, numSamples is number, degree is number, tolerance is ValueWithUnits) returns BSplineCurve
{
    // Sample both curves and blend
    var blendedPoints = [];

    for (var i = 0; i < numSamples; i += 1)
    {
        var S = i / (numSamples - 1);  // 0 to 1 inclusive

        // Evaluate curve0 (with flip if needed)
        var S0 = flip ? (1 - S) : S;
        var pt0 = evaluateSpline({
            "spline" : curve0,
            "parameters" : [S0]
        })[0][0];

        // Evaluate curve1
        var pt1 = evaluateSpline({
            "spline" : curve1,
            "parameters" : [S]
        })[0][0];

        // Compute blended point
        var sf = computeAppliedSF(S, sf_0, sf_1, transition);
        var blendedPt = (1 - sf) * pt0 + sf * pt1;

        blendedPoints = append(blendedPoints, blendedPt);
    }

    // Fit curve through blended points
    // Use explicit parameters to ensure endpoints are exact
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, i / (numSamples - 1));
    }

    var result = approximateSpline(context, {
        "degree" : 3,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : blendedPoints })],
        "parameters" : params,
        "interpolateIndices" : [0, numSamples - 1]  // Force exact endpoints
    });

    return result[0];
}

// joinCurveSegments, orderCurveSegments, countConnections, projectCurveOnSurface
// → moved to curveOps.fs

// printBSpline, formatVector, roundDecimal
// → moved to debugTools.fs
