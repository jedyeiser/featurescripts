FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "d4ef98b7b0acf40b1998b4ed");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "9e8676f449d3bc6ce225a1b8");

// IMPORT: generateBaselineSolver.fs
import(path : "649902142758d832c018a0be", version : "085deabdc3aac82e93a37db7");

// IMPORT: analyzeBaseline.fs
import(path : "f0717a1116fee7304957da5b", version : "5b40b4a82d143f10befe8c4e");

//import export baselineCore
export import(path : "14d1222501acfaf0e2029dac", version : "8111d5991f2b8f3fc012e166");



/**
 * GENERATE BASELINE
 * =================
 *
 * Produces the baseline (camber/rocker) curve for a ski or snowboard.
 *
 * The user provides:
 *   - FCP, ACP, and mount (load) point geometry references
 *   - Camber height (MCH), forebody/aftbody rocker lengths, tip heights
 *   - Optional EI profile edge(s)
 *   - Spline approximation parameters
 *
 * Algorithm:
 *   1. Resolve xFCP, xACP, xMount
 *   2. Derive xFRCP = xFCP + FRCPL,  xARCP = xACP - ARCPL
 *   3. Inner solve (camber pocket + rocker sections) for inner target height H
 *   4. Outer bisection on H until actualCamber = MCH_target ± 0.01 mm
 *   5. Fit approximateSpline through assembled points, create curve body
 *
 * Coordinate convention:
 *   - All geometry in XZ plane (Y = 0)
 *   - X = along-ski axis;  xFCP < xACP
 *   - Z = vertical; positive = up = camber
 */


// =============================================================================
// BOUND CONSTANTS
// =============================================================================

export const ApproxToleranceBounds  = { (meter) : [1e-7, 1e-4, 1e-2] } as LengthBoundSpec;
export const MaxControlPointsBounds = { (unitless) : [4, 15, 50] }    as IntegerBoundSpec;
export const ApproxDegreeBounds     = { (unitless) : [3, 3, 9] }       as IntegerBoundSpec;


// =============================================================================
// EDITING LOGIC
// =============================================================================

export function generateBaselineEditLogic(context is Context, id is Id,
    oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map,
    hiddenBodies is Query, clickedButton is string) returns map
{
    definition.showEIQuery = definition.hasEIProfile;
    return definition;
}


// getEIFromEdges is imported from xSectBeamAnalysis (canonical definition there)

// =============================================================================
// FEATURE DEFINITION
// =============================================================================

annotation {"Feature Type Name"        : "Generate baseline",
    "Feature Type Description" : "Generates a camber/rocker baseline curve for a ski or snowboard",
    "Editing Logic Function"   : "generateBaselineEditLogic" }
export const generateBaseline = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Output type", "Default" : BaselineCurveOutputType.CURVE_PER_REGION, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.outputType is BaselineCurveOutputType;
        
        annotation { "Name" : "Output curve name", "Default" : "Baseline" }
        definition.outputCurveName is string;

        annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.fcpQuery is Query;

        annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.acpQuery is Query;

        annotation { "Name" : "Mount / load point", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.mountQuery is Query;

        annotation { "Group Name" : "Baseline targets", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Camber height (MCH)", "Description" : "Maximum camber height after rocker rotation. 0 = flat ski." }
            isLength(definition.camberHeight, CamberHeightBounds);

            annotation { "Name" : "Forebody rocker length", "Description" : "FCP -> FRCP distance. 0 = no forebody rocker." }
            isLength(definition.frcpl, RockerLengthBounds);
            
            annotation { "Name" : "Spec forebody minimum" }
            definition.specForebodyMin is boolean;
            
            if (definition.specForebodyMin)
            {
                annotation { "Name" : "Forebody inflection point to minimum point dist" }
                isLength(definition.forebodyMinPointDist, MinPointDistBounds);  
            }

            annotation { "Name" : "Aftbody rocker length","Description" : "ARCP -> ACP distance. 0 = no aftbody rocker." }
            isLength(definition.arcpl, RockerLengthBounds);
            
            annotation { "Name" : "Spec aftbody minimum" }
            definition.specAftbodyMin is boolean;
            
            if (definition.specAftbodyMin)
            {
                annotation { "Name" : "Aftbody inflection point to minimum point dist" }
                isLength(definition.aftbodyMinPointDist, MinPointDistBounds);  
            }

            annotation { "Name" : "FCP height", "Description" : "Normal-distance offset of FCP tip below camber tangent at FRCP." }
            isLength(definition.fcpHeight, RockerHeightBounds);

            annotation { "Name" : "ACP height", "Description" : "Normal-distance offset of ACP tip below camber tangent at ARCP." }
            isLength(definition.acpHeight, RockerHeightBounds);
        }

        annotation { "Name" : "Use EI profile", "Default" : false, "Description" : "When enabled, solve camber pocket using beam bending with the provided EI profile. When false, solve for a simple cubic as the camber pocket" }
        definition.hasEIProfile is boolean;

        annotation { "Name" : "showEIQuery", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.showEIQuery is boolean;

        if (definition.showEIQuery)
        {
            annotation { "Name" : "EI profile edges", "Filter" : EntityType.EDGE, "Description" : "World Z in mm = EI in Nm^2." }
            definition.eiEdgesQuery is Query;
        }

        annotation { "Group Name" : "Spline output", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Approximation tolerance" }
            isLength(definition.approxTolerance, ApproxToleranceBounds);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.maxControlPoints, MaxControlPointsBounds);

            annotation { "Name" : "Curve degree" }
            isInteger(definition.curveDegree, ApproxDegreeBounds);
        }

        annotation { "Name" : "Add baseline sketch" }
        definition.addBaselineSketch is boolean;

        annotation { "Name" : "Create weighted baseline", "Default" : false, "Description" : "Outputs a second curve with zero camber and the same rocker geometry as the primary baseline." }
        definition.createWeightedBaseline is boolean;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print setup" }
            definition.debugPrintSetup is boolean;
            
            annotation { "Name" : "Print solver iterations" }
            definition.debugPrintSolverIterations is boolean;
        }
        

    }
    {
        // ----------------------------------------------------------------
        // 1. Resolve reference X coordinates
        // ----------------------------------------------------------------
        var dummyEdge = qNothing();
        var xFCP = round(resolveReferencePointX(context, definition.fcpQuery,   dummyEdge), 0.1 * millimeter);
        var xACP = round(resolveReferencePointX(context, definition.acpQuery,   dummyEdge), 0.1 * millimeter);
        var xMRS = (xFCP + xACP)/2;
        var xMount = resolveReferencePointX(context, definition.mountQuery,  dummyEdge);
        
        println('xFCP = ' ~ toString(xFCP));
        println('xACP = ' ~ toString(xACP));
        println('xMount = ' ~ toString(xMount));

        if (xFCP == undefined || xACP == undefined || xMount == undefined)
        {
            throw regenError("Could not resolve FCP, ACP, or mount point.");
        }
        
        //println('xFCP -> ' ~ toString(xFCP) ~ '. xACP -> ' ~ toString(xACP) ~ '. xMP -> ' ~ toString(xMount));
        
        if (!((xFCP > xMount && xACP < xMount)  || (xFCP < xMount && xACP > xMount)))
        {
            throw regenError("Mount / load point must lie between FCP and ACP.");
        }
        
        var stdDir = xFCP < xACP;

        // ----------------------------------------------------------------
        // 2. Derive FRCP and ARCP
        // ----------------------------------------------------------------
        var xFRCP = (stdDir) ? xFCP + definition.frcpl : xFCP - definition.frcpl;
        var xARCP = (stdDir) ? xACP - definition.arcpl : xACP + definition.arcpl;


        // ----------------------------------------------------------------
        // 3. Load EI data
        // ----------------------------------------------------------------
        var eiData = []; // {x: (xPoint) , EI: (EI)}
        var hasEI  = false;

        if (definition.hasEIProfile && definition.showEIQuery)
        {
            var edgeCount = size(evaluateQuery(context, definition.eiEdgesQuery));
            if (edgeCount > 0)
            {
                eiData = getEIFromEdges(context, definition.eiEdgesQuery, xFCP, xACP);
                hasEI  = (size(eiData) >= 2);
            }
            else
            {
                throw regenError("Unable to extract edges from provided EI profile");
            }
        }

        // Needed by steps 4, 5, and 6
        var hasForeRocker = definition.frcpl > 0 * meter;
        var hasAftRocker  = definition.arcpl  > 0 * meter;
        
        var iterCamberHeight = definition.camberHeight; // iteration variable
        var camberDelta = 1 * meter; //update this data with each iteration
        
        var camberBSpline = undefined;
        var forebodyRockerBSpline = undefined;
        var aftbodyRockerBSpline = undefined;
        var baselineBSplines =[];

        var lastFbT = 0.5;
        var lastAbT = 0.5;
        var fbDistClamped = false;
        var abDistClamped = false;
        var fbClampMsg = "";
        var abClampMsg = "";
        
        for (var i = 0; i < 20; i += 1) // main solver loop
        {
            println('camber delta at the start of iteration ' ~ i ~ ' = ' ~ toString(camberDelta));
            if (abs(camberDelta) < 0.001 * millimeter)
            {
                break; //last iteration is sufficent
            }
            else
            {
                
                baselineBSplines =[]; // clear array

                var camberPocket = (hasEI) ? solveCamberBeam(eiData, xFRCP, xARCP, xMount, iterCamberHeight/meter) : solveCamberCubic(xFRCP, xARCP, xMount, iterCamberHeight/meter);
                
                var cleanPocket = mapArray(camberPocket, function(x) {return vector(x['x'], 0 * millimeter, x['z']);}); // convert to usable form (vectors)
                
                cleanPocket = sort(cleanPocket, function(a, b) {return a[0] - b[0];}); //sort points in ascending order in X
                
                var approxCamberPocket = approximateSpline(context, {
                        "degree" : definition.curveDegree,
                        "tolerance" : definition.approxTolerance,
                        "maxControlPoints" : definition.maxControlPoints,
                        "isPeriodic" : false,
                        "targets" : [approximationTarget({ 'positions' : cleanPocket })],
                        "interpolateIndices" : [0, size(cleanPocket) -1]
                })[0];
                
                baselineBSplines = append(baselineBSplines, approxCamberPocket);
                camberBSpline = approxCamberPocket;
                
                
                var cpFRCP_data = (stdDir) ? evaluateSpline({ "spline" : approxCamberPocket, "parameters" : [approxCamberPocket.knots[0]], "nDerivatives" : 1}) : evaluateSpline({ "spline" : approxCamberPocket, "parameters" : [approxCamberPocket.knots[size(approxCamberPocket.knots) -1]], "nDerivatives" : 1});
                var cpARCP_data = (stdDir) ? evaluateSpline({ "spline" : approxCamberPocket, "parameters" : [approxCamberPocket.knots[size(approxCamberPocket.knots) -1]], "nDerivatives" : 1}) : evaluateSpline({ "spline" : approxCamberPocket, "parameters" : [approxCamberPocket.knots[0]], "nDerivatives" : 1});
                
                var frcpPoint = cpFRCP_data[0][0];
                var frcpSlope = normalize(cpFRCP_data[1][0]);
                var frcpNormalSlope = vector(frcpSlope[2], 0, -frcpSlope[0]);
                
                var arcpPoint = cpARCP_data[0][0];
                var arcpSlope = normalize(cpARCP_data[1][0]);
                var arcpNormalSlope = vector(arcpSlope[2], 0, -arcpSlope[0]);
                
                //b. if forebody rocker, generate forebody rocker
                if (hasForeRocker)
                {
                    var rockerXDelta = xFCP - frcpPoint[0];

                    var rockerNormalVector = definition.fcpHeight * frcpNormalSlope;
                    if (rockerNormalVector[2] < 0 * millimeter) // flip vector to get correct Z delta
                    {
                        rockerNormalVector = -1 * rockerNormalVector;
                    }
                    
                    var tangent_l = rockerXDelta - rockerNormalVector[0];
                    var tangentPoint = frcpPoint + tangent_l*frcpSlope;
                    var rockerPoint = tangentPoint + rockerNormalVector;
                    
                    var fbTangent = (rockerXDelta < 0 * millimeter ? -1 : 1) * frcpSlope;
                    var t = 0.5;
                    if (definition.specForebodyMin)
                    {
                        var fbRange = getAchievableXDistRange(frcpPoint, rockerPoint, fbTangent);
                        if (definition.forebodyMinPointDist < fbRange.minDist || definition.forebodyMinPointDist > fbRange.maxDist)
                        {
                            fbDistClamped = true;
                            t = (definition.forebodyMinPointDist < fbRange.minDist) ? fbRange.tForMin : fbRange.tForMax;
                            fbClampMsg = "Forebody min point distance "
                                ~ toString(round(definition.forebodyMinPointDist / millimeter))
                                ~ " mm is outside achievable range ["
                                ~ toString(round(fbRange.minDist / millimeter)) ~ ", "
                                ~ toString(round(fbRange.maxDist / millimeter))
                                ~ "] mm. Using closest achievable value.";
                        }
                        else
                        {
                            fbDistClamped = false;
                            t = solveForTension(frcpPoint, rockerPoint, fbTangent, definition.forebodyMinPointDist);
                        }
                    }
                    lastFbT = t;

                    var rockerSpline = quadraticSplineFromTangent(frcpPoint, rockerPoint, fbTangent, t);

                    baselineBSplines = append(baselineBSplines, rockerSpline);
                    forebodyRockerBSpline = rockerSpline;
                }
                //c. if aftbody rocker, generate aftbody rocker
                if (hasAftRocker)
                {
                    var rockerXDelta = xACP - arcpPoint[0];

                    var rockerNormalVector = definition.acpHeight * arcpNormalSlope;
                    if (rockerNormalVector[2] < 0 * millimeter) // flip vector to get correct Z delta
                    {
                        rockerNormalVector = -1 * rockerNormalVector;
                    }
                    
                    var tangent_l = rockerXDelta - rockerNormalVector[0];
                    var tangentPoint = arcpPoint + tangent_l*arcpSlope;
                    var rockerPoint = tangentPoint + rockerNormalVector;
                    
                    var abTangent = (rockerXDelta < 0 * millimeter ? -1 : 1) * arcpSlope;
                    var t = 0.5;
                    if (definition.specAftbodyMin)
                    {
                        var abRange = getAchievableXDistRange(arcpPoint, rockerPoint, abTangent);
                        if (definition.aftbodyMinPointDist < abRange.minDist || definition.aftbodyMinPointDist > abRange.maxDist)
                        {
                            abDistClamped = true;
                            t = (definition.aftbodyMinPointDist < abRange.minDist) ? abRange.tForMin : abRange.tForMax;
                            abClampMsg = "Aftbody min point distance "
                                ~ toString(round(definition.aftbodyMinPointDist / millimeter))
                                ~ " mm is outside achievable range ["
                                ~ toString(round(abRange.minDist / millimeter)) ~ ", "
                                ~ toString(round(abRange.maxDist / millimeter))
                                ~ "] mm. Using closest achievable value.";
                        }
                        else
                        {
                            abDistClamped = false;
                            t = solveForTension(arcpPoint, rockerPoint, abTangent, definition.aftbodyMinPointDist);
                        }
                    }
                    lastAbT = t;

                    var rockerSpline = quadraticSplineFromTangent(arcpPoint, rockerPoint, abTangent, t);

                    baselineBSplines = append(baselineBSplines, rockerSpline);
                    aftbodyRockerBSpline = rockerSpline;
                    
                }
                
                //d. package, translate and shift. 
                var minPoints = findMinZBothSides(baselineBSplines, xMRS, stdDir);
                var translatedCurves = transformCurves(baselineBSplines, minPoints.fbMin, minPoints.abMin);
                
                //2. solve for baseline values
                
                var camber = solveZAtX(translatedCurves, xMount);
                //println('Iteration ' ~ i ~ ' camber = ' ~ camber);
                //3. based on values, update iterCamberHeight
                var camberDiff = definition.camberHeight - camber;
                var pctChange = (iterCamberHeight + camberDiff)/iterCamberHeight;
                /*
                println('********** ITERATION ' ~ i ~ ' ***************');
                println('current -> ' ~ iterCamberHeight);
                println('measured - > ' ~ camber);
                println('delta -> ' ~ camberDiff);
                println('pctChange -> ' ~ pctChange)
                */
                
                iterCamberHeight = iterCamberHeight * pctChange;
                //println('updated -> ' ~ iterCamberHeight);
                
                
                camberDelta = camberDiff;
            }
        }
        
        if (fbDistClamped)
            reportFeatureInfo(context, id + "fbRangeInfo", fbClampMsg);
        if (abDistClamped)
            reportFeatureInfo(context, id + "abRangeInfo", abClampMsg);

        baselineBSplines = [camberBSpline];
        if (hasForeRocker)
        {
            forebodyRockerBSpline = setControlPointX(forebodyRockerBSpline, xFCP); // ensure last point lands on FCP
            baselineBSplines = append(baselineBSplines, forebodyRockerBSpline);
        }
        if (hasAftRocker)
        {
            aftbodyRockerBSpline = setControlPointX(aftbodyRockerBSpline, xACP); // ensure last point lands on ACP
            baselineBSplines = append(baselineBSplines, aftbodyRockerBSpline);
        }

        // Level the output: rotate so the FB/AB contact minima share the same Z (ground plane)
        var outputMinPoints = findMinZBothSides(baselineBSplines, xMRS, stdDir);
        baselineBSplines = transformCurves(baselineBSplines, outputMinPoints.fbMin, outputMinPoints.abMin);

        var curveBodyQ = qNothing();
        
        if (definition.outputType == BaselineCurveOutputType.CURVE_PER_REGION)
        {
            var createdEdges = [];
            var createdBodies = [];
            for (var i = 0; i < size(baselineBSplines); i += 1)
            {
                opCreateBSplineCurve(context, id + ("createSectionBSplines" ~ i), {
                        "bSplineCurve" : baselineBSplines[i]
                });
                
                createdEdges = append(createdEdges, qCreatedBy(id + ("createSectionBSplines" ~ i), EntityType.EDGE));
                createdBodies = append(createdBodies, qCreatedBy(id + ("createSectionBSplines" ~ i), EntityType.BODY));
                
            }
            
            opExtractWires(context, id + "extractBaselineCurves", {
                    "edges" : qUnion(createdEdges)
            });
            
            curveBodyQ = qCreatedBy(id + "extractBaselineCurves", EntityType.BODY);
            
            opDeleteBodies(context, id + "deleteOriginalWires", {
                    "entities" : qUnion(createdBodies)
            });
        }
        else
        {
            var curvePoints = sampleAndSortCurves(baselineBSplines, 20);
            
            var fullSpline = approximateSpline(context, {
                    "degree" : definition.curveDegree,
                    "tolerance" : definition.approxTolerance,
                    "maxControlPoints" : definition.maxControlPoints,
                    "isPeriodic" : false,
                    "targets" : [approximationTarget({ 'positions' : curvePoints })]
            })[0];
            
            opCreateBSplineCurve(context, id + "createFullCurve", {
                    "bSplineCurve" : fullSpline
            });
            
            curveBodyQ = qCreatedBy(id + "createFullCurve", EntityType.BODY);
        }
        
        

        // Name the wire body if a name was provided.
        if (length(definition.outputCurveName) > 0)
        {
            setProperty(context, {
                    "entities" : curveBodyQ,
                    "propertyType" : PropertyType.NAME,
                    "value" : definition.outputCurveName
            });
        }

        // ----------------------------------------------------------------
        // Weighted baseline
        // ----------------------------------------------------------------
        if (definition.createWeightedBaseline)
        {
            // FRCP and ARCP in the transformed coordinate system are the first
            // and last control points of the (clamped, interpolated) camber spline.
            var nCPCamber = size(baselineBSplines[0].controlPoints);
            var wbFrcpPt  = baselineBSplines[0].controlPoints[0];
            var wbArcpPt  = baselineBSplines[0].controlPoints[nCPCamber - 1];

            // Zero-camber section: straight line from FRCP to ARCP
            var wbCamber = bSplineCurve({
                    "degree"        : 1,
                    "dimension"     : 3,
                    "isRational"    : false,
                    "isPeriodic"    : false,
                    "controlPoints" : [wbFrcpPt, wbArcpPt],
                    "knots"         : [0, 0, 1, 1] as KnotArray
            });

            // Assemble: flat camber + rockers rebuilt tangent to the flat baseline
            var flatDir = normalize(wbArcpPt - wbFrcpPt);
            var weightedBSplines = [wbCamber];
            var wbRockerIdx = 1;
            if (hasForeRocker)
            {
                var wbFcpPt = baselineBSplines[wbRockerIdx].controlPoints[2];
                weightedBSplines = append(weightedBSplines, quadraticSplineFromTangent(wbFrcpPt, wbFcpPt, -flatDir, lastFbT));
                wbRockerIdx += 1;
            }
            if (hasAftRocker)
            {
                var wbAcpPt = baselineBSplines[wbRockerIdx].controlPoints[2];
                weightedBSplines = append(weightedBSplines, quadraticSplineFromTangent(wbArcpPt, wbAcpPt, flatDir, lastAbT));
            }

            var wbCreatedEdges  = [];
            var wbCreatedBodies = [];
            for (var i = 0; i < size(weightedBSplines); i += 1)
            {
                opCreateBSplineCurve(context, id + ("createWeightedBSpline" ~ i), {
                        "bSplineCurve" : weightedBSplines[i]
                });
                wbCreatedEdges  = append(wbCreatedEdges,  qCreatedBy(id + ("createWeightedBSpline" ~ i), EntityType.EDGE));
                wbCreatedBodies = append(wbCreatedBodies, qCreatedBy(id + ("createWeightedBSpline" ~ i), EntityType.BODY));
            }

            opExtractWires(context, id + "extractWeightedCurves", {
                    "edges" : qUnion(wbCreatedEdges)
            });

            var wbBodyQ = qCreatedBy(id + "extractWeightedCurves", EntityType.BODY);

            opDeleteBodies(context, id + "deleteOriginalWeightedWires", {
                    "entities" : qUnion(wbCreatedBodies)
            });

            setProperty(context, {
                    "entities"     : wbBodyQ,
                    "propertyType" : PropertyType.NAME,
                    "value"        : (length(definition.outputCurveName) > 0 ? definition.outputCurveName : "Baseline") ~ " (Weighted)"
            });
        }

        // Baseline sketch — analyze the generated curve and output measurement geometry
        if (definition.addBaselineSketch)
        
        {
            var baselineEdges = qUnion([qOwnedByBody(curveBodyQ, EntityType.EDGE)]);
            var result = analyzeBaselineGeometry(context, baselineEdges,
                                                 definition.fcpQuery, definition.acpQuery);
            if (result != undefined)
            {
                var chordDir   = normalize(result.ab_min_pt - result.fb_min_pt);
                var camberDiff = result.max_camber_pt - result.fb_min_pt;
                var camberFoot = result.fb_min_pt + dot(camberDiff, chordDir) * chordDir;

                var fbFoot = undefined;
                if (result.frcp_pt != undefined)
                {
                    var fcpDiff = result.fcp_pt - result.frcp_pt;
                    fbFoot = result.frcp_pt + dot(fcpDiff, result.frcp_dir) * result.frcp_dir;
                }

                var abFoot = undefined;
                if (result.arcp_pt != undefined)
                {
                    var acpDiff = result.acp_pt - result.arcp_pt;
                    abFoot = result.arcp_pt + dot(acpDiff, result.arcp_dir) * result.arcp_dir;
                }

                var sketchPl = plane(vector(0, 0, 0) * meter, vector(0, -1, 0), vector(1, 0, 0));
                var sketch = newSketchOnPlane(context, id + "baselineMeasurementSketch", {
                    "sketchPlane" : sketchPl
                });

                skLineSegment(sketch, "minChord", {
                    "start"        : worldToPlane(sketchPl, result.fb_min_pt),
                    "end"          : worldToPlane(sketchPl, result.ab_min_pt),
                    "construction" : true
                });

                if (result.frcp_pt != undefined && result.arcp_pt != undefined)
                {
                    skLineSegment(sketch, "inflChord", {
                        "start"        : worldToPlane(sketchPl, result.frcp_pt),
                        "end"          : worldToPlane(sketchPl, result.arcp_pt),
                        "construction" : true
                    });
                }

                if (result.frcp_pt != undefined && fbFoot != undefined)
                {
                    skLineSegment(sketch, "fbTangentLeg", {
                        "start"        : worldToPlane(sketchPl, result.frcp_pt),
                        "end"          : worldToPlane(sketchPl, fbFoot),
                        "construction" : true
                    });
                    skLineSegment(sketch, "fbNormalLeg", {
                        "start"        : worldToPlane(sketchPl, result.fcp_pt),
                        "end"          : worldToPlane(sketchPl, fbFoot),
                        "construction" : true
                    });
                }

                if (result.arcp_pt != undefined && abFoot != undefined)
                {
                    skLineSegment(sketch, "abTangentLeg", {
                        "start"        : worldToPlane(sketchPl, result.arcp_pt),
                        "end"          : worldToPlane(sketchPl, abFoot),
                        "construction" : true
                    });
                    skLineSegment(sketch, "abNormalLeg", {
                        "start"        : worldToPlane(sketchPl, result.acp_pt),
                        "end"          : worldToPlane(sketchPl, abFoot),
                        "construction" : true
                    });
                }

                skLineSegment(sketch, "camberNormal", {
                    "start"        : worldToPlane(sketchPl, result.max_camber_pt),
                    "end"          : worldToPlane(sketchPl, camberFoot),
                    "construction" : true
                });

                skSolve(sketch);
            }
            
        }
    
    });
