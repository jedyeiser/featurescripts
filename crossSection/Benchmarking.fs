FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/geometry.fs", version : "2878.0");

/**
 * Benchmark Feature: Standard vs @Built-in Function Performance
 *
 * PURPOSE:
 * Measures the performance difference between FeatureScript's standard library
 * wrapper functions (e.g., evDistance) and their underlying kernel-level @built-in
 * counterparts (e.g., @evDistance).
 *
 * WHY @FUNCTIONS ARE FASTER:
 * Standard functions wrap @built-ins with type checking, unit attachment, query
 * casting, and error handling. The @ versions skip all of that and go straight
 * to the C++ kernel. The tradeoff: raw speed for raw results (no units, arrays
 * instead of Vectors, etc.).
 *
 * USAGE:
 * 1. Insert this feature into a Part Studio with existing geometry.
 * 2. Set the iteration count (higher = more stable timing, but slower profiling).
 * 3. Enable the tests you want, select geometry, and regenerate.
 * 4. Check the FeatureScript notices/console for timing output.
 *
 * NOTES ON @FUNCTION SIGNATURES:
 * - evDistance, evPlane, evLength, size, constructPaths: signatures confirmed
 *   via SmartBench / community documentation.
 * - evArea, evSurfaceDefinition, evCurveDefinition, evEdgeTangentLine,
 *   evApproximateBSplineCurve: signatures INFERRED from the std library wrapper
 *   pattern. If any of these fail to compile, check the std source for the
 *   actual @ call and adjust.
 */

// ===========================
//  FEATURE DEFINITION
// ===========================

annotation { "Feature Type Name" : "Benchmark @Functions" }
export const benchmarkBuiltins = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Iterations" }
        isInteger(definition.iterations, { (unitless) : [10, 100, 10000] } as IntegerBoundSpec);

        // -------------------------------------------------------
        //  evDistance vs @evDistance
        //  Known: returns raw number in meters, no units.
        //  Caden measured ~2x speedup.
        // -------------------------------------------------------
        annotation { "Group Name" : "evDistance vs @evDistance", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evDistance" }
            definition.testEvDistance is boolean;

            if (definition.testEvDistance)
            {
                annotation { "Name" : "Entity 1", "Filter" : EntityType.FACE || EntityType.EDGE || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
                definition.distEntity1 is Query;

                annotation { "Name" : "Entity 2", "Filter" : EntityType.FACE || EntityType.EDGE || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
                definition.distEntity2 is Query;
            }
        }

        // -------------------------------------------------------
        //  evPlane vs @evPlane
        //  Known: returns plane with no units, vectors as arrays.
        //  Caden measured ~4-5x speedup.
        // -------------------------------------------------------
        annotation { "Group Name" : "evPlane vs @evPlane", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evPlane" }
            definition.testEvPlane is boolean;

            if (definition.testEvPlane)
            {
                annotation { "Name" : "Planar Face", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
                definition.planeFace is Query;
            }
        }

        // -------------------------------------------------------
        //  evLength vs @evLength
        //  Known: returns raw number in meters, no units.
        //  Caden measured ~2-3x speedup.
        // -------------------------------------------------------
        annotation { "Group Name" : "evLength vs @evLength", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evLength" }
            definition.testEvLength is boolean;

            if (definition.testEvLength)
            {
                annotation { "Name" : "Edge", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.lengthEdge is Query;
            }
        }

        // -------------------------------------------------------
        //  evArea vs @evArea
        //  INFERRED: likely returns raw number in m�, no units.
        // -------------------------------------------------------
        annotation { "Group Name" : "evArea vs @evArea", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evArea" }
            definition.testEvArea is boolean;

            if (definition.testEvArea)
            {
                annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
                definition.areaFace is Query;
            }
        }

        // -------------------------------------------------------
        //  size vs @size
        //  Known: ~3-4x speedup per Caden.
        //  Select entities to build an array via evaluateQuery.
        // -------------------------------------------------------
        annotation { "Group Name" : "size vs @size", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test size" }
            definition.testSize is boolean;

            if (definition.testSize)
            {
                annotation { "Name" : "Entities (select many for a meaningful array)", "Filter" : EntityType.FACE || EntityType.EDGE || EntityType.VERTEX }
                definition.sizeEntities is Query;
            }
        }

        // -------------------------------------------------------
        //  evApproximateBSplineSurface vs @evApproximateBSplineSurface
        //  INFERRED: returns raw surface definition map, no units.
        // -------------------------------------------------------
        annotation { "Group Name" : "evApproximateBSplineSurface vs @evApproximateBSplineSurface", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evSurfaceDefinition" }
            definition.testEvSurfaceDef is boolean;

            if (definition.testEvSurfaceDef)
            {
                annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
                definition.surfDefFace is Query;
            }
        }

        // -------------------------------------------------------
        //  evCurveDefinition vs @evCurveDefinition
        //  INFERRED: returns raw curve definition map, no units.
        // -------------------------------------------------------
        annotation { "Group Name" : "evCurveDefinition vs @evCurveDefinition", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evCurveDefinition" }
            definition.testEvCurveDef is boolean;

            if (definition.testEvCurveDef)
            {
                annotation { "Name" : "Edge", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.curveDefEdge is Query;
            }
        }

        // -------------------------------------------------------
        //  evEdgeTangentLine vs @evEdgeTangentLine
        //  INFERRED: returns raw line data (origin + direction as arrays).
        // -------------------------------------------------------
        annotation { "Group Name" : "evEdgeTangentLine vs @evEdgeTangentLine", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evEdgeTangentLine" }
            definition.testEvTangent is boolean;

            if (definition.testEvTangent)
            {
                annotation { "Name" : "Edge", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.tangentEdge is Query;

                annotation { "Name" : "Parameter (0-1 along edge)" }
                isReal(definition.tangentParam, { (unitless) : [0, 0.5, 1] } as RealBoundSpec);
            }
        }

        // -------------------------------------------------------
        //  evApproximateBSplineCurve vs @evApproximateBSplineCurve
        //  INFERRED: returns raw B-spline data (knots, control points
        //  as arrays of raw coordinates, etc.).
        // -------------------------------------------------------
        annotation { "Group Name" : "evApproximateBSplineCurve vs @evApproximateBSplineCurve", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evApproximateBSplineCurve" }
            definition.testEvBSpline is boolean;

            if (definition.testEvBSpline)
            {
                annotation { "Name" : "Edge", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.bsplineEdge is Query;
            }
        }

        // -------------------------------------------------------
        //  constructPath vs @constructPaths
        //  Known: 10-20x speedup per Caden. Note the plural name.
        //  Returns raw path data that needs manual query reconstruction.
        // -------------------------------------------------------
        annotation { "Group Name" : "constructPath vs @constructPaths", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test constructPath" }
            definition.testConstructPath is boolean;

            if (definition.testConstructPath)
            {
                annotation { "Name" : "Edges (select connected edges)", "Filter" : EntityType.EDGE }
                definition.pathEdges is Query;
            }
        }

        // -------------------------------------------------------
        //  approximateSpline vs @approximateSpline
        //  Pure math function: fits a B-spline through an array of points.
        //  No model query involved — wrapper overhead should be
        //  proportionally larger relative to computation.
        // -------------------------------------------------------
        annotation { "Group Name" : "approximateSpline vs @approximateSpline", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test approximateSpline" }
            definition.testApproxSpline is boolean;

            if (definition.testApproxSpline)
            {
                annotation { "Name" : "Edge (to generate test points from)", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.approxSplineEdge is Query;

                annotation { "Name" : "Number of sample points" }
                isInteger(definition.approxSplinePoints, { (unitless) : [5, 20, 100] } as IntegerBoundSpec);
            }
        }

        // -------------------------------------------------------
        //  evaluateSpline vs @evaluateSpline
        //  Pure math function: evaluates a BSplineCurve at a parameter.
        //  Returns a 3D point.
        // -------------------------------------------------------
        annotation { "Group Name" : "evaluateSpline vs @evaluateSpline", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test evaluateSpline" }
            definition.testEvalSpline is boolean;

            if (definition.testEvalSpline)
            {
                annotation { "Name" : "Edge (to get BSplineCurve from)", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.evalSplineEdge is Query;

                annotation { "Name" : "Parameter (0-1)" }
                isReal(definition.evalSplineParam, { (unitless) : [0, 0.5, 1] } as RealBoundSpec);
            }
        }

        // -------------------------------------------------------
        //  Unitless math: norm() with/without ValueWithUnits
        //  Tests whether doing math on raw doubles vs ValueWithUnits
        //  matters in tight loops. We sample points along an edge,
        //  then compute distances between consecutive points both ways:
        //  A) norm(pt1 - pt2) with full ValueWithUnits vectors
        //  B) manual sqrt(dx^2+dy^2+dz^2) on stripped doubles
        //  This isolates the per-operation unit-tracking overhead
        //  that Caden mentions in Tip 4.
        // -------------------------------------------------------
        annotation { "Group Name" : "Unitless math: norm() with/without units", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Test unitless math" }
            definition.testUnitless is boolean;

            if (definition.testUnitless)
            {
                annotation { "Name" : "Edge (to sample points from)", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
                definition.unitlessEdge is Query;

                annotation { "Name" : "Number of point pairs" }
                isInteger(definition.unitlessPoints, { (unitless) : [5, 50, 500] } as IntegerBoundSpec);
            }
        }
    }
    {
        const N = definition.iterations;

        println("");
        println("================================================");
        println("  BENCHMARK: @Function Performance");
        println("  Iterations per test: " ~ N);
        println("================================================");

        if (definition.testEvDistance)
            benchmarkEvDistance(context, definition, N);

        if (definition.testEvPlane)
            benchmarkEvPlane(context, definition, N);

        if (definition.testEvLength)
            benchmarkEvLength(context, definition, N);

        if (definition.testEvArea)
            benchmarkEvArea(context, definition, N);

        if (definition.testSize)
            benchmarkSize(context, definition, N);

        if (definition.testEvSurfaceDef)
            benchmarkEvSurfaceDefinition(context, definition, N);

        if (definition.testEvCurveDef)
            benchmarkEvCurveDefinition(context, definition, N);

        if (definition.testEvTangent)
            benchmarkEvEdgeTangentLine(context, definition, N);

        if (definition.testEvBSpline)
            benchmarkEvApproximateBSplineCurve(context, definition, N);

        if (definition.testConstructPath)
            benchmarkConstructPath(context, definition, N);

        if (definition.testApproxSpline)
            benchmarkApproximateSpline(context, definition, N);

        if (definition.testEvalSpline)
            benchmarkEvaluateSpline(context, definition, N);

        if (definition.testUnitless)
            benchmarkUnitlessMath(context, definition, N);

        println("");
        println("================================================");
        println("  BENCHMARK COMPLETE");
        println("================================================");
    });


// ===========================
//  BENCHMARK FUNCTIONS
// ===========================

/**
 * evDistance vs @evDistance
 *
 * @evDistance skips:
 *   - Query type casting
 *   - Unit attachment to result
 * Returns: raw number in meters
 */
function benchmarkEvDistance(context is Context, definition is map, N is number)
{
    println("");
    println("--- evDistance vs @evDistance ---");

    const arg = {
        "side0" : definition.distEntity1,
        "side1" : definition.distEntity2
    };

    // Standard version
    var stdResult;
    startTimer("evDistance_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evDistance(context, arg);
    }
    printTimer("evDistance_std");

    // Built-in version
    var builtinResult;
    startTimer("evDistance_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evDistance(context, arg);
    }
    printTimer("evDistance_builtin");

    // Print results for type comparison
    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evPlane vs @evPlane
 *
 * @evPlane skips:
 *   - Unit attachment (origin coordinates)
 *   - Vector type wrapping (returns arrays instead of Vector)
 * Returns: map with origin/normal/x as raw number arrays
 */
function benchmarkEvPlane(context is Context, definition is map, N is number)
{
    println("");
    println("--- evPlane vs @evPlane ---");

    const arg = { "face" : definition.planeFace };

    var stdResult;
    startTimer("evPlane_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evPlane(context, arg);
    }
    printTimer("evPlane_std");

    var builtinResult;
    startTimer("evPlane_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evPlane(context, arg);
    }
    printTimer("evPlane_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evLength vs @evLength
 *
 * @evLength skips:
 *   - Unit attachment
 * Returns: raw number in meters
 */
function benchmarkEvLength(context is Context, definition is map, N is number)
{
    println("");
    println("--- evLength vs @evLength ---");

    const arg = { "entities" : definition.lengthEdge };

    var stdResult;
    startTimer("evLength_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evLength(context, arg);
    }
    printTimer("evLength_std");

    var builtinResult;
    startTimer("evLength_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evLength(context, arg);
    }
    printTimer("evLength_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evArea vs @evArea
 *
 * INFERRED SIGNATURE � may need adjustment.
 * Expected: returns raw number in m�
 */
function benchmarkEvArea(context is Context, definition is map, N is number)
{
    println("");
    println("--- evArea vs @evArea [INFERRED] ---");

    const arg = { "entities" : definition.areaFace };

    var stdResult;
    startTimer("evArea_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evArea(context, arg);
    }
    printTimer("evArea_std");

    // NOTE: If this doesn't compile, check the std evArea source for the
    // actual @evArea call signature. The arg key might differ (e.g., "faces").
    var builtinResult;
    startTimer("evArea_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evArea(context, arg);
    }
    printTimer("evArea_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * size vs @size
 *
 * @size skips:
 *   - Type checking overhead on the array
 * Returns: integer count
 *
 * We build the array via evaluateQuery so there's something meaningful to measure.
 * The timing loop only measures size()/@size(), not the query evaluation.
 */
function benchmarkSize(context is Context, definition is map, N is number)
{
    println("");
    println("--- size vs @size ---");

    // Build the array once, outside the timing loop
    const arr = evaluateQuery(context, definition.sizeEntities);
    println("  Array length: " ~ size(arr));

    var stdResult;
    startTimer("size_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = size(arr);
    }
    printTimer("size_std");

    var builtinResult;
    startTimer("size_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @size(arr);
    }
    printTimer("size_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evSurfaceDefinition vs @evSurfaceDefinition
 *
 * INFERRED SIGNATURE � may need adjustment.
 * Expected: returns raw surface geometry map (plane, cylinder, BSpline surface, etc.)
 * without unit attachment or type wrapping.
 */
function benchmarkEvSurfaceDefinition(context is Context, definition is map, N is number)
{
    println("");
    println("--- evApproximateBSplineSurface vs @evApproximateBSplineSurface [INFERRED] ---");

    const arg = { "face" : definition.surfDefFace };

    var stdResult;
    startTimer("evSurfaceDef_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evApproximateBSplineSurface(context, arg);
    }
    printTimer("evSurfaceDef_std");

    var builtinResult;
    startTimer("evSurfaceDef_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evApproximateBSplineSurface(context, arg);
    }
    printTimer("evSurfaceDef_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evCurveDefinition vs @evCurveDefinition
 *
 * INFERRED SIGNATURE � may need adjustment.
 * Expected: returns raw curve geometry (line, circle, BSpline curve, etc.)
 * without unit attachment or type wrapping.
 */
function benchmarkEvCurveDefinition(context is Context, definition is map, N is number)
{
    println("");
    println("--- evCurveDefinition vs @evCurveDefinition [INFERRED] ---");

    const arg = { "edge" : definition.curveDefEdge };

    var stdResult;
    startTimer("evCurveDef_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evCurveDefinition(context, arg);
    }
    printTimer("evCurveDef_std");

    var builtinResult;
    startTimer("evCurveDef_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evCurveDefinition(context, arg);
    }
    printTimer("evCurveDef_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evEdgeTangentLine vs @evEdgeTangentLine
 *
 * INFERRED SIGNATURE � may need adjustment.
 * Expected: returns raw line data with origin and direction as number arrays
 * instead of Vector types, coordinates in meters.
 */
function benchmarkEvEdgeTangentLine(context is Context, definition is map, N is number)
{
    println("");
    println("--- evEdgeTangentLine vs @evEdgeTangentLine [INFERRED] ---");
    
    var testRange = range(0, 1, 50);

    const arg = {
        "edge" : definition.tangentEdge,
        "parameters" : testRange
    };

    var stdResult;
    startTimer("evTangent_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evEdgeTangentLines(context, arg);
    }
    printTimer("evTangent_std");

    var builtinResult;
    startTimer("evTangent_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evEdgeTangentLines(context, arg)[0];
    }
    printTimer("evTangent_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * evApproximateBSplineCurve vs @evApproximateBSplineCurve
 *
 * INFERRED SIGNATURE � may need adjustment.
 * Expected: returns raw B-spline data structure with knot vectors and
 * control points as number arrays, coordinates in meters.
 *
 * This is likely one of the heavier kernel calls, so the wrapper overhead
 * may be a smaller fraction of total time. Still worth measuring.
 */
function benchmarkEvApproximateBSplineCurve(context is Context, definition is map, N is number)
{
    println("");
    println("--- evApproximateBSplineCurve vs @evApproximateBSplineCurve [INFERRED] ---");

    const arg = { "edge" : definition.bsplineEdge };

    var stdResult;
    startTimer("evBSpline_std");
    for (var i = 0; i < N; i += 1)
    {
        stdResult = evApproximateBSplineCurve(context, arg);
    }
    printTimer("evBSpline_std");

    var builtinResult;
    startTimer("evBSpline_builtin");
    for (var i = 0; i < N; i += 1)
    {
        builtinResult = @evApproximateBSplineCurve(context, arg);
    }
    printTimer("evBSpline_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * constructPath vs @constructPaths
 *
 * CONFIRMED by Caden: 10-20x speedup.
 * Note the name difference: singular vs plural.
 * @constructPaths can handle multiple disjoint paths in one call.
 * Returns raw path data � query references need manual reconstruction.
 */
function benchmarkConstructPath(context is Context, definition is map, N is number)
{
    println("");
    println("--- constructPath vs @constructPaths ---");

    // Standard version: constructPath expects all edges to form one continuous path
    var stdResult;
    startTimer("constructPath_std");
    for (var i = 0; i < N; i += 1)
    {

        stdResult = constructPath(context, definition.pathEdges);
        
    }
    printTimer("constructPath_std");

    // Built-in version: @constructPaths handles multiple disjoint paths
    var builtinResult;
    startTimer("constructPaths_builtin");
    for (var i = 0; i < N; i += 1)
    {

        builtinResult = @constructPaths(context, definition.pathEdges, {});
        
    }
    printTimer("constructPaths_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * approximateSpline vs @approximateSpline
 *
 * PURE MATH — no model query. Takes an array of 3D points and fits a
 * B-spline through them. We generate test points by sampling along an
 * edge at uniform parameters.
 *
 * Why this matters: approximateSpline is used heavily when constructing
 * spline curves from sampled data (e.g., cross-section profiles). If the
 * wrapper does meaningful work on the point array (unit validation per
 * point, etc.), the overhead scales with point count.
 *
 * NOTE: @approximateSpline may not exist. If it doesn't compile, that
 * tells us the function is implemented entirely in FeatureScript (no
 * kernel counterpart), which is itself useful information.
 */
function benchmarkApproximateSpline(context is Context, definition is map, N is number)
{
    println("");
    println("--- approximateSpline vs @approximateSpline [INFERRED] ---");

    // Generate test points by sampling along the selected edge
    const numPts = definition.approxSplinePoints;
    var points = [];
    for (var i = 0; i < numPts; i += 1)
    {
        const param = i / (numPts - 1);
        const tangentLine = evEdgeTangentLine(context, {
                "edge" : definition.approxSplineEdge,
                "parameter" : param
            });
        points = append(points, tangentLine.origin);
    }
    println("  Test points generated: " ~ size(points));

    // Standard version
    var stdResult;
    startTimer("approxSpline_std");
    for (var i = 0; i < N; i += 1)
    {


        stdResult = approximateSpline(context, {
                        'degree': 3,
                        'tolerance' : 1e-6 * meter,
                        'isPeriodic' : false,
                        'targets' : [approximationTarget({'positions' : points})]
                        });
                            
        
    }
    printTimer("approxSpline_std");

    // Built-in version
    var builtinResult;
    startTimer("approxSpline_builtin");
    for (var i = 0; i < N; i += 1)
    {

        builtinResult = @approximateSpline(context, {
                        'degree': 3,
                        'tolerance' : 1e-6,
                        'isPeriodic' : false,
                        'targets' : [approximationTarget({'positions' : points})]
                        });
        
    }
    printTimer("approxSpline_builtin");

    println("  std result type:     " ~ stdResult);
    println("  @builtin result type: " ~ builtinResult);
}

/**
 * evaluateSpline vs @evaluateSpline
 *
 * PURE MATH — no model query. Takes a BSplineCurve and a parameter,
 * returns the 3D point at that parameter.
 *
 * We first get a BSplineCurve by approximating the selected edge,
 * then time repeated evaluation at the chosen parameter.
 *
 * Why this matters: if you're evaluating a cached spline at many
 * parameters (e.g., walking along a cross-section profile), this
 * function is in your inner loop.
 */
function benchmarkEvaluateSpline(context is Context, definition is map, N is number)
{
    println("");
    println("--- evaluateSpline vs @evaluateSpline [INFERRED] ---");

    // Get a BSplineCurve to work with
    const spline = evApproximateBSplineCurve(context, {
            "edge" : definition.evalSplineEdge
        });
    const param = range(0, 1, 20);

    println("  Spline degree: " ~ spline.degree ~ ", control points: " ~ size(spline.controlPoints));
    
    const knots = spline.knots;
    const knotMin = knots[0];
    const knotMax = knots[size(knots) - 1];
    const mappedParam = mapArray(param, function(x) {return (knotMin + x * (knotMax - knotMin));});

    // Standard version
    var stdResult;
    startTimer("evalSpline_std");
    for (var i = 0; i < N; i += 1)
    {

        stdResult = evaluateSpline({'spline' : spline, 'parameters' : mappedParam});
        
    }
    printTimer("evalSpline_std");

    // Built-in version
    var builtinResult;
    startTimer("evalSpline_builtin");
    for (var i = 0; i < N; i += 1)
    {

        builtinResult = @evaluateSpline({'spline' : spline, 'parameters' : mappedParam});
        
    }
    printTimer("evalSpline_builtin");

    println("  std result:     " ~ stdResult);
    println("  @builtin result: " ~ builtinResult);
}

/**
 * Unitless math benchmark: norm() with ValueWithUnits vs raw doubles
 *
 * Tests Caden's Tip 4: "Any math operation on a ValueWithUnits takes
 * longer than a primitive value (ie double), as there's always a units
 * translation step."
 *
 * We sample points along an edge, then in a tight loop compute the
 * distance between consecutive point pairs two ways:
 *   A) norm(pt1 - pt2) — using full Vector/ValueWithUnits types
 *   B) sqrt(dx*dx + dy*dy + dz*dz) on stripped doubles
 *
 * This tells us the per-operation cost of carrying units through
 * arithmetic, which matters when you're doing hundreds of distance
 * calculations across cross-sections.
 */
function benchmarkUnitlessMath(context is Context, definition is map, N is number)
{
    println("");
    println("--- Unitless math: norm() with units vs raw doubles ---");

    const numPts = definition.unitlessPoints;

    // Sample points along the edge (with full units)
    var pointsWithUnits = [];
    for (var i = 0; i < numPts; i += 1)
    {
        const param = i / (numPts - 1);
        const tangentLine = evEdgeTangentLine(context, {
                "edge" : definition.unitlessEdge,
                "parameter" : param
            });
        pointsWithUnits = append(pointsWithUnits, tangentLine.origin);
    }

    // Strip units: extract raw doubles in meters
    var pointsRaw = []; // array of [x, y, z] arrays
    for (var i = 0; i < numPts; i += 1)
    {
        const pt = pointsWithUnits[i];
        pointsRaw = append(pointsRaw, [
                pt[0] / meter,
                pt[1] / meter,
                pt[2] / meter
            ]);
    }

    println("  Point pairs: " ~ (numPts - 1));

    // A) With units: norm(pt1 - pt2)
    var totalWithUnits = 0 * meter;
    startTimer("unitful_norm");
    for (var iter = 0; iter < N; iter += 1)
    {
        totalWithUnits = 0 * meter;
        for (var i = 0; i < numPts - 1; i += 1)
        {
            totalWithUnits += norm(pointsWithUnits[i + 1] - pointsWithUnits[i]);
        }
    }
    printTimer("unitful_norm");

    // B) Without units: manual sqrt on raw doubles
    var totalRaw = 0;
    startTimer("unitless_sqrt");
    for (var iter = 0; iter < N; iter += 1)
    {
        totalRaw = 0;
        for (var i = 0; i < numPts - 1; i += 1)
        {
            const dx = pointsRaw[i + 1][0] - pointsRaw[i][0];
            const dy = pointsRaw[i + 1][1] - pointsRaw[i][1];
            const dz = pointsRaw[i + 1][2] - pointsRaw[i][2];
            totalRaw += sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
    printTimer("unitless_sqrt");

    println("  With units total:    " ~ totalWithUnits);
    println("  Raw doubles total:   " ~ totalRaw ~ " (meters)");
}