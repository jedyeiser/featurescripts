FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

export import(path : "d1b04ca2346787da6083d7cc", version : "60f2cd484eff0523847a3bba");
export import(path : "b4b27eddd41251b5f56f042b", version : "a477172449057a1235bdb0f8");
import(path : "a54a829744c4e15e8da55e0e", version : "a429aca66ec4510e66ac2796");
import(path : "acb8ffe3d9c99c3c435f2d91", version : "2bab5ce5d256b75c85b6bbe5");

IconNamespace::import(path : "d351ce8959527c18c8b58a5f", version : "b3cc4f36c103b56147655d00");



export function editingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
                             isCreating is boolean, specifiedParameters is map) returns map
{
    var d = definition;

    return d;
}


export enum curvatureScaleFactors
{
    annotation{"Name" : "10mm [y] = 1m [radius]"}
    TEN,
    annotation{"Name" : "20mm [y] = 1m [radius]"}
    TWENTY,
    annotation{"Name" : "50mm [y] = 1m [radius]"}
    FIFTY,
    annotation{"Name" : "100mm [y] = 1m [radius]"}
    HUNDRED 
}

export const edgeSamplingBounds = {(unitless) : [5, 50, 200]} as IntegerBoundSpec;
export const gapSampleDxBounds = {(millimeter) : [0.5, 5, 10]} as LengthBoundSpec;


export const waistWidthBounds = {(millimeter) : [35, 95, 300]} as LengthBoundSpec;
export const taperAngleBounds = {(degree) : [-0.2, 0.25, .5]} as AngleBoundSpec;

export const numPointBounds = {(unitless) : [10, 50, 200]} as IntegerBoundSpec;

export const waistLocationBounds = {(millimeter) : [-100, 65, 100]} as LengthBoundSpec;

annotation { "Feature Type Name" : "Integrate footprint", "Editing Logic Function": "editingLogic", "Icon": IconNamespace::BLOB_DATA, "Feature Type Description" : "Takes a curve, or set of curves, specifying the RADIUS progression through the ski and integrates this curvature profile twice. Either the overall taper angle (widest to widest) or the waist position can be specified. Additional radus scaling options are provided for more flexibility and scaling."}
export const integrateFootprint = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Spline method", "Default" : FootprintSplineExportType.APPROX, "Description" : "Specifies if curves should be generated from approximateSpline or opFitSpline", "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.splineExportType is FootprintSplineExportType;
        
        annotation { "Name" : "Build Mode", "Default": FootprintCurveBuildMode.ONE_PER_REGION, "Description": "Specifies if we should build one curve per region (taper*, sidecut, taper*) or if we should build one spline per edge in our input query. gaps in X will be interpolated", "UIHint": UIHint.SHOW_LABEL }
        definition.footprintCurveBuildMode is FootprintCurveBuildMode;
        
        annotation { "Name" : "Unify curves", "Default": false, "Description" : "When true, outputs a single curve rather than multiple curves, no matter what is selected for build mode" }
        definition.unifyCurves is boolean;
        
        annotation { "Name" : "Strict " ,"Description": "When true, strictly enforces ouput BSplines to be a rational quadratic NURBS arcs or lines" }
        definition.strict is boolean;
        
        annotation { "Name" : "Waist/Taper Angle Calculations", "Default" : AngleDriver.WAIST, 'UIHint' : UIHint.HORIZONTAL_ENUM }
        definition.angleDriver is AngleDriver;
        
        if (definition.angleDriver == AngleDriver.WAIST)
        {
            annotation { "Name" : "Waist location" }
            isLength(definition.waistLocation, waistLocationBounds);
        }
        else if (definition.angleDriver == AngleDriver.TAPER_ANGLE)
        {
            annotation { "Name" : "Overall taper angle" }
            isAngle(definition.taperAngle, taperAngleBounds);
        }
        
        annotation { "Name" : "Waist width" }
        isLength(definition.waistWidth, waistWidthBounds);
          
        annotation { "Name" : "Radius Profile(s)", "Filter" : EntityType.EDGE && ConstructionObject.NO}
        definition.radiusProfiles is Query;
        
        annotation { "Group Name" : "Integration definition", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Y-Axis Scaling", "Defualt" : curvatureScaleFactors.TEN, "UIHint" : UIHint.SHOW_LABEL }
        definition.curvatureScalefactor is curvatureScaleFactors;
        
        }
        
        if (definition.splineExportType == FootprintSplineExportType.APPROX)
        {
            annotation { "Group Name" : "Spline approximation parameters", "Collapsed By Default" : true }
            {
    
                annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
                isInteger(definition.targetDegree, DEGREE_BOUND);
    
                annotation { "Name" : "Maximum control points" }
                isInteger(definition.maxCPs, { (unitless) : [4, 100, 500] } as IntegerBoundSpec);
    
                annotation { "Name" : "Tolerance" }
                isLength(definition.approximationTolerance, TOLERANCE_BOUND);
                
                annotation { "Name" : "isPeriodic", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN }
                definition.isPeriodic is boolean;
                
            }
        }
  
        annotation { "Group Name" : "Debug & details", "Collapsed By Default" : true }
        {
            annotation { "Group Name" : "Details", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Number of samples per edge" }
                isInteger(definition.numSamplesPerEdge, edgeSamplingBounds);
                
                annotation { "Name" : "Group output", "Description": "When true, uses opExtractWires to group the output of this feature" }
                definition.extractWires is boolean; 
                
            }

        }

        annotation { "Name" : "Recalculate?", "Default" : false }
        definition.rebuild is boolean;

        
    }
    
    {
        var cScalefactor = convertRadiusScalefactor(definition);
        
        var integrationDef = {'waistWidth': definition.waistWidth, 'angleDriver': definition.angleDriver, 'solveTol': definition.approximationTolerance, 'curvatureScaleFactor': cScalefactor, 'maxIter': 20};
        if (definition.angleDriver == AngleDriver.WAIST)
        {
            integrationDef['waistLocation'] = definition.waistLocation;
        }
        if (definition.angleDriver == AngleDriver.TAPER_ANGLE)
        {
            integrationDef['taperAngle'] = definition.taperAngle;
            integrationDef["solveTol"] = 1e-3 * degree;
        }
        
        
        var splineDef = {'splineExportType': definition.splineExportType};
        if (definition.splineExportType == FootprintSplineExportType.APPROX)
        {
            splineDef['targetDegree'] = definition.targetDegree;
            splineDef['maxCPs'] = definition.maxCPs;
            splineDef['tolerance'] = definition.approximationTolerance;
            splineDef['isPeriodic'] = false;
        }
        
        var samplingDef = {'numSamplesPerEdge': definition.numSamplesPerEdge, 'gapSampleDx': definition.gapSampleDx, 'epsR': definition.epsR};
        
        var results = generateFootprintFromRadiusEdges(context, id + ("getFootprintFromDef"), definition.radiusProfiles, definition.footprintCurveBuildMode, samplingDef, integrationDef, splineDef);
        
        var inputBox = evBox3d(context, {
                "topology" : qUnion([definition.radiusProfiles]),
                "tight" : true
        });
        
        
        
        if (definition.splineExportType == FootprintSplineExportType.FIT)
        {
            var pointArrays = mapArray(results, function(x) {return x.points;});
            if (definition.unifyCurves)
            {
                pointArrays = [concatenateArrays(pointArrays)];
            }
            
            var fitCurves = [];
            var fitBodies = [];
            
            for (var i = 0; i < size(pointArrays); i += 1)
            {
                opFitSpline(context, id + ("footprintFit" ~ i), {
                        "points" : pointArrays[i]
                });
                
                fitCurves = append(fitCurves, qCreatedBy(id + ("footprintFit" ~ i), EntityType.EDGE));
                fitBodies = append(fitBodies, qCreatedBy(id + ("footprintFit" ~ i), EntityType.BODY));
            }
            
            var bodyQuery = qUnion(fitBodies);
            var edgeQuery = qUnion(fitCurves);
            
            if (definition.strict) // convert to nurbs
            {
                var inputNURBS = mapArray(fitCurves, function(x) {return evApproximateBSplineCurve(context, {
                        "edge" : x
                });});
                
                var solvedArcs = forceQuadraticNurbs(context, id + 'forceFitNURBS', inputNURBS);
                
                opDeleteBodies(context, id + "deleteFitBSplines", {
                        "entities" : bodyQuery
                });
                
                fitCurves = [];
                fitBodies = [];
                
                for (var i = 0; i < size(solvedArcs); i += 1)
                {
                    opCreateBSplineCurve(context, id + ("NurbsFromFit" ~ i), {
                            "bSplineCurve" : solvedArcs[i]
                    });
                    
                    fitBodies = append(fitBodies, qCreatedBy(id + ("NurbsFromFit" ~ i), EntityType.BODY));
                    fitCurves = append(fitCurves, qCreatedBy(id + ("NurbsFromFit" ~ i), EntityType.EDGE));
                }
                
                bodyQuery = qUnion(fitBodies);
                edgeQuery = qUnion(fitCurves);
                
            }
            
            if (size(fitBodies) > 1 && definition.extractWires)
            {
                opExtractWires(context, id + "opExtractFitFootprint", {
                    "edges" : edgeQuery
                });
                
                opDeleteBodies(context, id + "deleteBodies1", {
                        "entities" : bodyQuery
                });   
            }
            
            
        }
        else if(definition.splineExportType == FootprintSplineExportType.APPROX)
        {
            if (definition.unifyCurves)
            {
                
                var pointArrays = mapArray(results, function(x) {return x.points;});
                
                var points = concatenateArrays(pointArrays);
                
                
                var aprxSpline = approximateSpline(context, {
                        "degree" : definition.targetDegree,
                        "tolerance" : definition.approximationTolerance,
                        "maxControlPoints" : definition.maxCPs,
                        "isPeriodic" : false,
                        "targets" : [approximationTarget({ 'positions' : points })], 
                        "interpolateIndices" : [0, size(points)-1]
                });
                
                validateApproximationQuality(aprxSpline[0], inputBox.minCorner[0], inputBox.maxCorner[0], 0.02);
                
                opCreateBSplineCurve(context, id + "bSplineFootprint", {
                        "bSplineCurve" : aprxSpline[0]
                });
                
                if (definition.strict) // convert to nurbs
                {
                    var inputNURBS = mapArray(evaluateQuery(context, qCreatedBy(id + "bSplineFootprint", EntityType.EDGE)), function(x) {return evApproximateBSplineCurve(context, {
                            "edge" : x
                    });});
                    
                    var solvedArcs = forceQuadraticNurbs(context, id + 'forceFitNURBS', inputNURBS);
                    
                    opDeleteBodies(context, id + "deleteAproxBSplines", {
                            "entities" : qCreatedBy(id + "bSplineFootprint", EntityType.BODY)
                    });
                    
                    var nurbsEdges = [];
                    var nurbsBodies = [];
                    
                    for (var i = 0; i < size(solvedArcs); i += 1)
                    {
                        opCreateBSplineCurve(context, id + ("NurbsFromAprox" ~ i), {
                                "bSplineCurve" : solvedArcs[i]
                        });
                        
                        nurbsBodies = append(nurbsBodies, qCreatedBy(id + ("NurbsFromAprox" ~ i), EntityType.BODY));
                        nurbsEdges = append(nurbsEdges, qCreatedBy(id + ("NurbsFromAprox" ~ i), EntityType.EDGE));
                    }
                    
                    
                    
                }
            }
            else
            {
                var splines = mapArray(results, function(x) {return x.bSpline;});
                //splines should be in order, but worth taking the time to enforce it. 
                splines = sort(splines, function(a, b) {return min(mapArray(a.controlPoints, function(p) {return p[0];})) -  min(mapArray(b.controlPoints, function(q) {return q[0];}))  ;});
                
                var splineEdges = [];
                var splineBodies = [];
                
                if (definition.strict)
                {
                    var returnNurbs = forceQuadraticNurbs(context, id + "multiApproxNurbs", splines);
                    
                    for (var i = 0; i < size(returnNurbs); i += 1)
                    {
                        validateApproximationQuality(returnNurbs[i][0], inputBox.minCorner[0], inputBox.maxCorner[0], 0.02);
                        opCreateBSplineCurve(context, id + ("multiApproxNurbs" ~ i ), {
                                "bSplineCurve" : returnNurbs[i]
                        });
                        
                        
                        
                        splineEdges = append(splineEdges, qCreatedBy(id + ("multiApproxNurbs" ~ i ), EntityType.EDGE));
                        splineBodies = append(splineEdges, qCreatedBy(id + ("multiApproxNurbs" ~ i ), EntityType.BODY));
                    }
                }
                else
                {
                    for (var i = 0; i < size(splines); i += 1)
                    {
                        validateApproximationQuality(splines[i], inputBox.minCorner[0], inputBox.maxCorner[0], 0.02);
                        
                        opCreateBSplineCurve(context, id + ("bSplineFootprintSegment" ~ i), {
                                "bSplineCurve" : splines[i]
                        }); 
                        
                        splineEdges = append(splineEdges, qCreatedBy(id + ("bSplineFootprintSegment" ~ i), EntityType.EDGE));
                        splineBodies = append(splineEdges, qCreatedBy(id + ("bSplineFootprintSegment" ~ i), EntityType.BODY));
                        
                    }
                }
                
                
                if (size(splineBodies) > 1 && definition.extractWires)
                {
                    opExtractWires(context, id + "extractBsplineFootprint", {
                            "edges" : qUnion(splineEdges)
                    });
                    
                    opDeleteBodies(context, id + "deleteBSplineFootprints", {
                            "entities" : qUnion(splineBodies)
                    });
                }
            }
            
            
        }
        
    });
    
export function forceQuadraticNurbs(context is Context, id is Id, bSplines is array) returns array
{    
    var dotTol = cos(0.1 * degree);
    var polyArcs = approximateSplinesWithPolyArcs(bSplines, 1e-3 * millimeter, 1e-3 * millimeter, dotTol, 1 * millimeter);
    
    var NURBS = primitivesToBSplines(polyArcs.segments);
    
    return NURBS;
    
}

    
export function convertRadiusScalefactor(definition is map) returns number
{
    if (definition.curvatureScalefactor == curvatureScaleFactors.TEN)
    {
        return (1000/10);
    }
    else if (definition.curvatureScalefactor == curvatureScaleFactors.TWENTY)
    {
        return (1000/20);
    }
    else if (definition.curvatureScalefactor == curvatureScaleFactors.FIFTY)
    {
        return (1000/50);
    }
    else if (definition.curvatureScalefactor == curvatureScaleFactors.HUNDRED)
    {
        return (1000/100);
    }
}

/**
 * Validates that a B-spline approximation hasn't produced erratic control points.
 * Checks for: control points outside data bounds, and non-monotonic X progression.
 * 
 * @param bSpline : The approximated B-spline curve
 * @param xMin : Minimum X value of input data
 * @param xMax : Maximum X value of input data
 * @param tolerance : How far outside bounds is acceptable (e.g., 1% of range)
 */
export function validateApproximationQuality(bSpline is BSplineCurve, xMin is ValueWithUnits, xMax is ValueWithUnits, toleranceFraction is number)
{
    var cps = bSpline.controlPoints;
    var xRange = xMax - xMin;
    var margin = xRange * toleranceFraction;
    
    var outOfBoundsCount = 0;
    var nonMonotonicCount = 0;
    
    for (var i = 0; i < size(cps); i += 1)
    {
        var x = cps[i][0];
        
        // Check bounds (allow small margin for numerical reasons)
        if (x < xMin - margin || x > xMax + margin)
        {
            outOfBoundsCount += 1;
        }
        
        // Check monotonicity (skip first point)
        // Allow small backwards steps at endpoints due to interpolation constraints
        if (i > 0 && i < size(cps) - 1)
        {
            var prevX = cps[i - 1][0];
            var nextX = cps[i + 1][0];
            
            // Flag if this point breaks the general trend significantly
            if ((x < prevX - margin * 0.5) && (x < nextX - margin * 0.5))
            {
                nonMonotonicCount += 1;
            }
            if ((x > prevX + margin * 0.5) && (x > nextX + margin * 0.5))
            {
                nonMonotonicCount += 1;
            }
        }
    }
    
    if (outOfBoundsCount > 0 || nonMonotonicCount > 0)
    {
        var msg = "Spline approximation quality issue: ";
        if (outOfBoundsCount > 0)
        {
            msg ~= outOfBoundsCount ~ " control point(s) outside data bounds. ";
        }
        if (nonMonotonicCount > 0)
        {
            msg ~= nonMonotonicCount ~ " control point(s) oscillating. ";
        }
        msg ~= "Try increasing the approximation tolerance or reducing max control points.";
        
        throw regenError(msg);
    }
}