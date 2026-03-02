FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import fpt_geometrty (export/import)
export import(path : "67c190b80e8b74dcee72e7ff", version : "744aeeb44e5f29d0ed224e4d");


// NOTE: fpt_math.fs has been deleted - all functions moved to tools/
// IMPORT: tools/assertions.fs (for assertTrue)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/34fb2c6a3c895cfce6b281f3", version : "bd6a4d5a47ec29178af978cf");

// IMPORT: tools/math_utils.fs (for safeSign, clamp01)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/280a24d76f52bdbf44cd941d", version : "d9e09196718b914b96e84924");

// IMPORT: tools/numerical_integration.fs (for cumTrapz)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/ef834eed6e0d2df2b34c10eb", version : "542adae37c1360ee2171b5fd");

// IMPORT: tools/solvers.fs (for bracketFromSamples, solveRootHybrid)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");

// evPathCurvatures() moved to fpt_geometry.fs
//import predicates
import(path : "a54a829744c4e15e8da55e0e", version : "c022e44160d7c658a2f46c9a");

//import arcFit
import(path : "66f4f03cf728e94b8f823585", version : "bbc62220b7aaab0b5264db49");



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

        annotation { "Group Name" : "Contact points", "Collapsed By Default" : false }
        {
            annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Forebody contact point — defines which end of the footprint is the forebody for taper angle sign convention" }
            definition.fcpQuery is Query;

            annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Aftbody contact point — defines which end of the footprint is the aftbody for taper angle sign convention" }
            definition.acpQuery is Query;
        }

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
            
            annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
            {
                annotation { "Name" : "Print input metadata" }
                definition.printInputMetadata is boolean;
                
                annotation { "Name" : "Print output metadata" }
                definition.printOutputMetadata is boolean;
                
            }

        }

        
        annotation { "Name" : "Recalculate?", "Default" : false }
        definition.rebuild is boolean;

    }
    {
        var cScalefactor = convertRadiusScalefactor(definition);
        
        var integrationDef = {'waistWidth': definition.waistWidth, 'angleDriver': definition.angleDriver, 'solveTol': definition.approximationTolerance, 'curvatureScaleFactor': cScalefactor, 'maxIter': 20};

        // If FCP is provided, extract its X coordinate so the solver can correctly label
        // forebody/aftbody regardless of which direction the radius profiles run along X.
        if (!isQueryEmpty(context, definition.fcpQuery))
        {
            var fcpBox = evBox3d(context, { "topology" : definition.fcpQuery, "tight" : true });
            integrationDef['fcpX'] = (fcpBox.minCorner[0] + fcpBox.maxCorner[0]) / 2;
        }
        if (definition.angleDriver == AngleDriver.WAIST)
        {
            integrationDef['waistLocation'] = definition.waistLocation;                                                                                                                                                               
            integrationDef["solveTol"] = 0.1 * millimeter;  
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
        
        if (definition.printInputMetadata)
        {
            var inputBox = evBox3d(context, {
                    "topology" : qUnion([definition.radiusProfiles]),
                    "tight" : true
            });

            var numEdges = size(evaluateQuery(context, qUnion([definition.radiusProfiles])));
        }
        
        var results = generateFootprintFromRadiusEdges(context, id + ("getFootprintFromDef"), definition.radiusProfiles, definition.footprintCurveBuildMode, samplingDef, integrationDef, splineDef);
        
        if (definition.splineExportType == FootprintSplineExportType.FIT)
        {
            
            var pointArrays = mapArray(results, function(x) {return x.points;});
            if (definition.unifyCurves)
            {
                pointArrays = [concatenateArrays(pointArrays)];
            }
            
            if (definition.printOutputMetadata)
            {
                var printPoints = concatenateArrays(pointArrays);
                var xVals = mapArray(printPoints, function(x) {return x[0];});
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
            
            if (definition.printOutputMetadata)
            {
                var outputSplines = mapArray(results, function(x) {return x.bSpline;});
                outputSplines = sort(outputSplines, function(a, b) {return min(mapArray(a.controlPoints, function(p) {return p[0];})) -  min(mapArray(b.controlPoints, function(q) {return q[0];}))  ;});
                var controlPoints = concatenateArrays(mapArray(outputSplines, function(x) {return x.controlPoints;}));
                var controlX = mapArray(controlPoints, function(x) {return x[0];});
            }
            
            if (definition.unifyCurves)
            {
                var points = concatenateArrays(mapArray(results, function(x) {return x.points;}));
                var aprxSpline = approximateSpline(context, {
                        "degree" : definition.targetDegree,
                        "tolerance" : definition.approximationTolerance,
                        "maxControlPoints" : definition.maxCPs,
                        "isPeriodic" : false,
                        "targets" : [approximationTarget({ 'positions' : points })]
                });
                
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
                        opCreateBSplineCurve(context, id + ("multiApproxNurbs" ~ i ), {
                                "bSplineCurve" : returnNurbs[i]
                        });
                        
                        splineEdges = append(splineEdges, qCreatedBy(id + ("multiApproxNurbs" ~ i ), EntityType.EDGE));
                        splineBodies = append(splineBodies, qCreatedBy(id + ("multiApproxNurbs" ~ i ), EntityType.BODY));
                    }
                }
                else
                {
                    for (var i = 0; i < size(splines); i += 1)
                    {
                        opCreateBSplineCurve(context, id + ("bSplineFootprintSegment" ~ i), {
                                "bSplineCurve" : splines[i]
                        }); 
                        
                        splineEdges = append(splineEdges, qCreatedBy(id + ("bSplineFootprintSegment" ~ i), EntityType.EDGE));
                        splineBodies = append(splineBodies, qCreatedBy(id + ("bSplineFootprintSegment" ~ i), EntityType.BODY));
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

