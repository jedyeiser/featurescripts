FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import fpt_geometrty (export/import)
export import(path : "67c190b80e8b74dcee72e7ff", version : "89446acd00aa71384f74fd8a");


// NOTE: fpt_math.fs has been deleted - all functions moved to tools/
// IMPORT: tools/assertions.fs (for assertTrue)
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/34fb2c6a3c895cfce6b281f3", version : "18fcdac56b39d12dba5ce285");

// IMPORT: tools/math_utils.fs (for safeSign, clamp01)
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/280a24d76f52bdbf44cd941d", version : "43549bf2d5a2bb2e92fb44bd");

// IMPORT: tools/numerical_integration.fs (for cumTrapz)
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/ef834eed6e0d2df2b34c10eb", version : "7967b0a31f464314605b7b85");

// IMPORT: tools/solvers.fs (for bracketFromSamples, solveRootHybrid)
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/99e84dbe2a4e2350792fa693", version : "91ebe2327e2b0654bb603e52");

// evPathCurvatures() moved to fpt_geometry.fs
//import predicates
import(path : "a54a829744c4e15e8da55e0e", version : "849a0888e10eff97f5f0e84a");

//import arcFit
import(path : "66f4f03cf728e94b8f823585", version : "72150f7fbb1546cf1ee01c88");



IconNamespace::import(path : "bf9ea3f62b60ecdbadaaffd0", version : "4b6968d25725072ecb55c4c1");



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
            annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Forebody contact point - defines which end of the footprint is the forebody for taper angle sign convention" }
            definition.fcpQuery is Query;

            annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1, "Description" : "Aftbody contact point - defines which end of the footprint is the aftbody for taper angle sign convention" }
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
                
                annotation { "Name" : "Group output", "Default" : true, "Description": "When true, uses opExtractWires to group the output of this feature" }
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

        var hasFootprintArcs = false;
        for (var i = 0; i < size(results); i += 1)
        {
            if (results[i].isArc == true)
            {
                hasFootprintArcs = true;
            }
        }

        if (hasFootprintArcs)
        {
            // Arc sections must be emitted as ANALYTIC sketch arcs -- opCreateBSplineCurve can
            // never report a radius, and our designers require a clickable radius on arcs.
            // Transitions stay ordinary splines. (The arc path always produces a single
            // extracted composite wire, so the extractWires/unify toggles do not apply here.)
            emitFootprintWithArcs(context, id, definition, results);
        }
        else if (definition.splineExportType == FootprintSplineExportType.FIT)
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
                
                var strictWire = emitStrictArcWire(context, id + "strictFit", inputNURBS);

                opDeleteBodies(context, id + "deleteFitBSplines", {
                        "entities" : bodyQuery
                });

                // One wire of real arcs and lines already; nothing left to extract.
                fitBodies = [strictWire];
                fitCurves = [qOwnedByBody(strictWire, EntityType.EDGE)];
                bodyQuery = strictWire;
                edgeQuery = qOwnedByBody(strictWire, EntityType.EDGE);
                
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
                    
                    emitStrictArcWire(context, id + "strictAprox", inputNURBS);

                    opDeleteBodies(context, id + "deleteAproxBSplines", {
                            "entities" : qCreatedBy(id + "bSplineFootprint", EntityType.BODY)
                    });

                    
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
                    // Real sketch arcs and lines in one wire (rational NURBS arcs read as splines, correction 39).
                    emitStrictArcWire(context, id + "multiApproxStrict", splines);
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
    
/**
 * Strict output: fit the curves with arcs and lines (Arc fit's library) and emit them as SKETCH arcs and lines
 * extracted into one wire, so Onshape reports real radii. (It used to emit rational quadratic NURBS, which
 * Onshape reports as splines with no radius -- correction 39, footprint test IF7.) Returns the wire body.
 */
function emitStrictArcWire(context is Context, id is Id, bSplines is array) returns Query
{
    var polyArcs = approximateSplinesWithPolyArcs(bSplines, 1e-3 * millimeter, 1e-3 * millimeter, cos(0.1 * degree), 1 * millimeter, 16, 8, false);
    var xyPlane = plane(vector(0, 0, 0) * meter, vector(0, 0, 1), vector(1, 0, 0));
    emitSketchFromPrimitives(context, id + "sketch", xyPlane, polyArcs.segments);
    opExtractWires(context, id + "wire", { "edges" : qCreatedBy(id + "sketch", EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteSketch", { "entities" : qCreatedBy(id + "sketch", EntityType.BODY) });
    return qCreatedBy(id + "wire", EntityType.BODY);
}

export function forceQuadraticNurbs(context is Context, id is Id, bSplines is array) returns array
{    
    var dotTol = cos(0.1 * degree);
    var polyArcs = approximateSplinesWithPolyArcs(bSplines, 1e-3 * millimeter, 1e-3 * millimeter, dotTol, 1 * millimeter, 16, 8, false);

    var NURBS = primitivesToBSplines(polyArcs.segments);

    return NURBS;

}

/**
 * Unit travel direction of an arc section (exact samples, in order) at its first or last sample, from
 * the circle through its first, middle and last samples; undefined when those are collinear.
 */
function arcSectionTangent(points is array, atEnd is boolean)
{
    const n = size(points);
    const p0 = points[0];
    const pm = points[floor(n / 2)];
    const p1 = points[n - 1];
    const a = pm - p0;
    const b = p1 - p0;
    const axb = cross(a, b);
    if (norm(axb) < 1e-18 * meter * meter)
    {
        return undefined;
    }
    const center = p0 + (dot(a, a) * cross(b, axb) + dot(b, b) * cross(axb, a)) / (2 * dot(axb, axb));
    const at = atEnd ? p1 : p0;
    var tangent = normalize(cross(axb, at - center));
    const ref = atEnd ? (p1 - pm) : (pm - p0);
    if (dot(tangent, ref) < 0 * meter)
    {
        tangent = -tangent;
    }
    return tangent;
}

/**
 * The opFitSpline definition for FIT transition i: its points, and at an end shared with an arc section the
 * ARC's tangent as the end derivative (2026-09-25 arc / line tangency review). An unconstrained fit ended in
 * whatever direction the fitter chose, so every arc <-> transition seam kinked; the arc is exact, so the
 * transition is the side that takes the arc's direction. Magnitude: the transition's chord length, the
 * natural speed for a fit through its points. A seam whose arc tangent and first chord of the transition
 * differ by more than 0.2 rad (the chord is only a rough tangent) is taken for a corner and left free.
 */
function transitionFit(results is array, i is number) returns map
{
    const points = results[i].points;
    const n = size(points);
    var chord = 0 * meter;
    for (var k = 1; k < n; k += 1)
    {
        chord += norm(points[k] - points[k - 1]);
    }

    var fit = { "points" : points };
    if (n < 2 || chord < 1e-9 * meter)
    {
        return fit;
    }

    if (i > 0 && results[i - 1].isArc == true)
    {
        const prev = results[i - 1].points;
        const t = arcSectionTangent(prev, true);
        if (t != undefined && norm(prev[size(prev) - 1] - points[0]) < 1e-6 * meter
            && atan2(norm(cross(t, normalize(points[1] - points[0]))), dot(t, normalize(points[1] - points[0]))) / radian < 0.2)
        {
            fit.startDerivative = t * chord;
        }
    }
    if (i + 1 < size(results) && results[i + 1].isArc == true)
    {
        const next = results[i + 1].points;
        const t = arcSectionTangent(next, false);
        if (t != undefined && norm(next[0] - points[n - 1]) < 1e-6 * meter
            && atan2(norm(cross(t, normalize(points[n - 1] - points[n - 2]))), dot(t, normalize(points[n - 1] - points[n - 2]))) / radian < 0.2)
        {
            fit.endDerivative = t * chord;
        }
    }
    return fit;
}

/**
 * Emit a footprint that contains one or more exact circular-arc sections.
 *
 * Onshape only reports an analytic radius for curves whose kernel geometry IS analytic; a
 * rational-NURBS "circle" from opCreateBSplineCurve always reports as a BSplineCurve (no
 * radius). So arc sections are drawn as 3-point sketch arcs (analytic), transitions as
 * ordinary splines, then a single opExtractWires copies everything into a composite wire
 * (analytic arcs preserved, connected edges merged) and the temporary sketch and spline
 * bodies are deleted -- leaving arcs that show a radius when clicked.
 *
 * results: per-section maps. Arc entries { isArc: true, points: [Vector...] }; transition
 *          entries { isArc: false, bSpline: map, points: [Vector...] }.
 */
function emitFootprintWithArcs(context is Context, id is Id, definition is map, results is array)
{
    var origEdges = [];
    var origBodies = [];

    // 1) Transition (non-arc) sections -> ordinary splines.
    var tIdx = 0;
    for (var i = 0; i < size(results); i += 1)
    {
        var r = results[i];
        if (r.isArc == true)
        {
            continue;
        }

        if (definition.splineExportType == FootprintSplineExportType.FIT)
        {
            opFitSpline(context, id + ("fpTransFit" ~ tIdx), transitionFit(results, i));
            origEdges = append(origEdges, qCreatedBy(id + ("fpTransFit" ~ tIdx), EntityType.EDGE));
            origBodies = append(origBodies, qCreatedBy(id + ("fpTransFit" ~ tIdx), EntityType.BODY));
        }
        else
        {
            opCreateBSplineCurve(context, id + ("fpTrans" ~ tIdx), { "bSplineCurve" : r.bSpline });
            origEdges = append(origEdges, qCreatedBy(id + ("fpTrans" ~ tIdx), EntityType.EDGE));
            origBodies = append(origBodies, qCreatedBy(id + ("fpTrans" ~ tIdx), EntityType.BODY));
        }
        tIdx += 1;
    }

    // 2) Arc sections -> analytic 3-point sketch arcs on the world XY plane. On that plane
    //    sketch (x, y) == world (x, y), so we take the point's x/y components directly.
    var xyPlane = plane(vector(0, 0, 0) * meter, vector(0, 0, 1), vector(1, 0, 0));
    var arcSketchId = id + "fpArcSketch";
    var sk = newSketchOnPlane(context, arcSketchId, { "sketchPlane" : xyPlane });

    var aIdx = 0;
    for (var i = 0; i < size(results); i += 1)
    {
        var r = results[i];
        if (!(r.isArc == true))
        {
            continue;
        }

        var p = r.points;
        var nP = size(p);
        var midI = floor(nP / 2);

        // Samples lie exactly on the true arc; start/mid/end reconstruct it exactly. The
        // middle sample maximizes the sagitta, avoiding a near-collinear (degenerate) arc.
        skArc(sk, "fpArc" ~ aIdx, {
                "start" : vector(p[0][0], p[0][1]),
                "mid" : vector(p[midI][0], p[midI][1]),
                "end" : vector(p[nP - 1][0], p[nP - 1][1])
        });
        aIdx += 1;
    }

    skSolve(sk);

    var sketchEdges = qCreatedBy(arcSketchId, EntityType.EDGE);

    // 3) Copy transitions + analytic arcs into one composite wire, in two stages as scaleFootprint's
    //    emitter: sketch edges first extracted to their own wire (mixing sketch and non-sketch edges in
    //    one opExtractWires can fail with OVERLAPPING_EDGES), then that wire's edges together with the
    //    spline edges. opExtractWires preserves the arcs' analytic type.
    //    (This changed the output edges' ids on 2026-09-25: documents that reference them re-pick or stay
    //    on an older version -- correction 47.)
    var allEdges = qUnion(origEdges);
    var arcWires = qNothing();
    if (!isQueryEmpty(context, sketchEdges))
    {
        opExtractWires(context, id + "fpArcWires", { "edges" : sketchEdges });
        arcWires = qCreatedBy(id + "fpArcWires", EntityType.BODY);
        allEdges = qUnion([allEdges, qOwnedByBody(arcWires, EntityType.EDGE)]);
    }
    opExtractWires(context, id + "fpCompositeWire", { "edges" : allEdges });

    // 4) Delete the temporary spline bodies, the arc wire and the sketch, leaving only the composite wire.
    var toDelete = concatenateArrays([origBodies, [arcWires, qCreatedBy(arcSketchId, EntityType.BODY)]]);
    opDeleteBodies(context, id + "fpDeleteTemp", { "entities" : qUnion(toDelete) });
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

