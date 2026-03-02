FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: tools/math_utils.fs (safeSign)
export import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/280a24d76f52bdbf44cd941d", version : "d9e09196718b914b96e84924");

// IMPORT: tools/numerical_integration.fs (cumTrapz)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/ef834eed6e0d2df2b34c10eb", version : "542adae37c1360ee2171b5fd");

// IMPORT: tools/solvers.fs (solveRootHybrid)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");

// IMPORT: fpt_geometry.fs (prepareFootprintCurves, filterAndTrimBSplines, etc.)
export import(path : "67c190b80e8b74dcee72e7ff", version : "513e7245b3d0bbde72fdd7c1");

// IMPORT: fpt_analyze.fs (edgesToBSplines, findWidestPoint, findInflectionPoint, etc.)
export import(path : "71d853c0fd2f10ca3bb20a4b", version : "04b14c75c0ac9cf3de1e6acd");

// IMPORT: arcFit.fs (approximateSplinesWithPolyArcs, primitivesToBSplines)
import(path : "66f4f03cf728e94b8f823585", version : "2397dea14b4c7c8d1013c82a");

// IMPORT: integrateFootprint.fs (forceQuadraticNurbs)
import(path : "5d198387b3966ae60a549555", version : "a3aa09351b1d5b5db386a7d9");

// IMPORT: footprint_math.fs (getBSplineCurvatureAtParam)
import(path : "d3ad341f5b87924b36b5aba8", version : "f95ad68b73bfb5c55d44fff8");





/**
 * =============================================================================
 * SCALE FOOTPRINT FEATURE
 *
 * Takes reference footprint curves and scales them to match a new RSL line.
 * Supports multiple scaling modes:
 *   - ACCORDION: Simple X scaling, optional uniform Y scaling for target width
 *   - KEEP_TAPER: Accordion + rotate about pin point to preserve taper angle
 *   - SCALE_RADIUS: Scale curvature progression to target radius, preserve taper
 *
 * Supports symmetric (mirror +Y to -Y) and asymmetric (independent sides) modes.
 *
 * Width targeting:
 *   - ACCORDION: scales Y uniformly to hit target waist width
 *   - KEEP_TAPER / SCALE_RADIUS: shifts Y to hit target waist width
 *
 * Tip/tail curves are translated and Y-scaled to match new contact widths.
 * Optional G1 continuity repair at contact points (minimum-change control-point method).
 * =============================================================================
 */

// =============================================================================
// ENUMS
// =============================================================================

export enum FootprintScaleMode
{
    annotation { "Name" : "Accordion (X only)" }
    ACCORDION,

    annotation { "Name" : "Keep taper angle" }
    KEEP_TAPER,

    annotation { "Name" : "Scale radius" }
    SCALE_RADIUS
}

// Bounds for sidecut radius input (in meters for better UX)
export const SIDECUT_RADIUS_BOUNDS = {
    (meter): [1, 21, 100]  // [min, default, max]
} as LengthBoundSpec;

export const APPROX_TOLERANCE_BOUNDS = {
    (millimeter): [0.001, 0.001, 10]
} as LengthBoundSpec;

export const MAX_CONTROL_POINTS_BOUNDS = {
    (unitless): [4, 30, 200]
} as IntegerBoundSpec;

export enum ScalePinLocation
{
    annotation { "Name" : "Pin ACP width" }
    PIN_ACP,
    
    annotation { "Name" : "Pin MRS width" }
    PIN_MRS
}

// Always visible so user can choose even if the input data happens to be +Y only.
export enum SymmetryMode
{
    annotation { "Name" : "Symmetric (mirror +Y)" }
    SYMMETRIC,
    
    annotation { "Name" : "Asymmetric (independent sides)" }
    ASYMMETRIC
}

// =============================================================================
// FEATURE DEFINITION
// =============================================================================

annotation { "Feature Type Name" : "Scale Footprint" }
export const scaleFootprint = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Reference footprint edges", "Filter" : EntityType.EDGE }
        definition.refEdges is Query;
        
        annotation { "Name" : "Reference RSL line", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1,
                     "Description" : "Reference ski length line (RSL). Defines the FCP and ACP of the original ski." }
        definition.refRslEdge is Query;
        
        annotation { "Name" : "New RSL line", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1,
                     "Description" : "New RSL line to scale toward. FCP/ACP positions determine the output ski length." }
        definition.newRslEdge is Query;
        
        // --- Symmetry mode ---
        annotation { "Name" : "Symmetry mode",
                     "Description" : "Symmetric mirrors the +Y sidecut to -Y. Asymmetric scales each edge independently." }
        definition.symmetryMode is SymmetryMode;
        
        // =================== +Y SIDE SETTINGS ===================
        annotation { "Name" : "Scale mode" }
        definition.scaleMode is FootprintScaleMode;
        
        if (definition.scaleMode == FootprintScaleMode.KEEP_TAPER)
        {
            annotation { "Name" : "Pin location",
                         "Description" : "Which width to hold fixed during taper rotation. ACP pins the tail width; MRS pins the mid-stance width." }
            definition.pinLocation is ScalePinLocation;
        }

        if (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
        {
            annotation { "Name" : "Target average radius",
                         "Description" : "Target average sidecut radius. Only used in Scale Radius mode." }
            isLength(definition.targetRadius, SIDECUT_RADIUS_BOUNDS);
        }

        annotation { "Group Name" : "Spline options", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Output curve degree", "Default" : 3 }
            isInteger(definition.outputDegree, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Strict arcs", "Default" : false,
                         "Description" : "When enabled, forces output curve segments to be exact circular arcs." }
            definition.strictArcs is boolean;

            annotation { "Name" : "Approximation tolerance",
                         "Description" : "Maximum deviation between the approximated output B-spline and the target curve. Lower values produce more control points." }
            isLength(definition.approximationTolerance, APPROX_TOLERANCE_BOUNDS);

            annotation { "Name" : "Max control points", "Default" : 30,
                         "Description" : "Upper limit on the number of control points in the approximated output curve." }
            isInteger(definition.maxControlPoints, MAX_CONTROL_POINTS_BOUNDS);
        }

        annotation { "Name" : "Specify target width", "Default" : false }
        definition.specifyWidth is boolean;
        
        if (definition.specifyWidth)
        {
            annotation { "Name" : "Target waist width",
                         "Description" : "Desired total waist width. The sidecut is shifted in Y to match this value." }
            isLength(definition.targetWaistWidth, LENGTH_BOUNDS);
        }
        
        // =================== -Y SIDE SETTINGS (asymmetric only) ===================
        if (definition.symmetryMode == SymmetryMode.ASYMMETRIC)
        {
            annotation { "Name" : "-Y Scale mode" }
            definition.negScaleMode is FootprintScaleMode;
            
            if (definition.negScaleMode == FootprintScaleMode.KEEP_TAPER)
            {
                annotation { "Name" : "-Y Pin location",
                             "Description" : "Which width to hold fixed during taper rotation. ACP pins the tail width; MRS pins the mid-stance width." }
                definition.negPinLocation is ScalePinLocation;
            }

            if (definition.negScaleMode == FootprintScaleMode.SCALE_RADIUS)
            {
                annotation { "Name" : "-Y Target average radius",
                             "Description" : "Target average sidecut radius. Only used in Scale Radius mode." }
                isLength(definition.negTargetRadius, SIDECUT_RADIUS_BOUNDS);
            }

            annotation { "Group Name" : "-Y Spline options", "Collapsed By Default" : true }
            {
                annotation { "Name" : "-Y Output curve degree", "Default" : 3 }
                isInteger(definition.negOutputDegree, POSITIVE_COUNT_BOUNDS);

                annotation { "Name" : "-Y Strict arcs", "Default" : false,
                             "Description" : "When enabled, forces output curve segments to be exact circular arcs." }
                definition.negStrictArcs is boolean;

                annotation { "Name" : "-Y Approximation tolerance",
                             "Description" : "Maximum deviation between the approximated output B-spline and the target curve. Lower values produce more control points." }
                isLength(definition.negApproximationTolerance, APPROX_TOLERANCE_BOUNDS);

                annotation { "Name" : "-Y Max control points", "Default" : 30,
                             "Description" : "Upper limit on the number of control points in the approximated output curve." }
                isInteger(definition.negMaxControlPoints, MAX_CONTROL_POINTS_BOUNDS);
            }

            annotation { "Name" : "-Y Specify target width", "Default" : false }
            definition.negSpecifyWidth is boolean;
            
            if (definition.negSpecifyWidth)
            {
                annotation { "Name" : "-Y Target waist width",
                             "Description" : "Desired total waist width. The sidecut is shifted in Y to match this value." }
                isLength(definition.negTargetWaistWidth, LENGTH_BOUNDS);
            }
        }
        
        // =================== CONTINUITY ===================
        annotation { "Name" : "Enforce tangency at tip (FCP)", "Default" : false,
                     "Description" : "Adjusts the tip curve control point at FCP so the tip is tangent to the sidecut (G1 continuity)." }
        definition.enforceTipTangency is boolean;

        annotation { "Name" : "Enforce tangency at tail (ACP)", "Default" : false,
                     "Description" : "Adjusts the tail curve control point at ACP so the tail is tangent to the sidecut (G1 continuity)." }
        definition.enforceTailTangency is boolean;
        
        // =================== OUTPUT ===================
        annotation { "Name" : "Keep reference curves", "Default" : true }
        definition.keepReference is boolean;
    }
    {
        // =====================================================================
        // STEP 1: Extract RSL data
        // =====================================================================
        var refRslData = extractRslData(context, definition.refRslEdge);
        var refFcp = refRslData.fcp;
        var refAcp = refRslData.acp;
        var refMrs = refRslData.mrs;
        var refLength = abs(refAcp[0] - refFcp[0]);
        
        var newRslData = extractRslData(context, definition.newRslEdge);
        var newFcp = newRslData.fcp;
        var newAcp = newRslData.acp;
        var newMrs = newRslData.mrs;
        var newLength = abs(newAcp[0] - newFcp[0]);

        // =====================================================================
        // STEP 2: Convert reference edges to BSplines
        // =====================================================================
        var tolerance = 0.001 * millimeter;
        var bsplines = edgesToBSplines(context, definition.refEdges, tolerance);
        
        // =====================================================================
        // STEP 3: Categorize curves (+Y / -Y aware)
        // =====================================================================
        var categorized = categorizeCurvesWithSides(context, bsplines, refFcp[0], refAcp[0], tolerance);

        var hasNegData = size(categorized.sidecutNeg) > 0;
        
        // Validate: if ASYMMETRIC chosen but no -Y data, error
        if (definition.symmetryMode == SymmetryMode.ASYMMETRIC && !hasNegData)
        {
            throw regenError("Asymmetric mode selected but no -Y footprint data found in reference edges.");
        }
        
        // =====================================================================
        // STEP 4: Analyze +Y reference sidecut
        // =====================================================================
        var refAnalysisPos = analyzeReferenceSidecut(categorized.sidecutPos,
            refFcp[0], refAcp[0], refMrs[0], tolerance);

        // =====================================================================
        // STEP 5: Scale +Y sidecut
        // =====================================================================
        // Output degree and strict arcs are now always available (not mode-dependent)
        var outputDegree = definition.outputDegree;
        var strictArcs = definition.strictArcs;
        var approxTolerance = definition.approximationTolerance;
        var maxControlPoints = definition.maxControlPoints;

        var scaledPos = scaleSidecut(context, id + "scaledPos", categorized.sidecutPos, refAnalysisPos,
            refFcp[0], refAcp[0], refMrs[0],
            newFcp[0], newAcp[0], newMrs[0],
            definition.scaleMode,
            definition.scaleMode == FootprintScaleMode.KEEP_TAPER ? definition.pinLocation : ScalePinLocation.PIN_ACP,
            definition.scaleMode == FootprintScaleMode.SCALE_RADIUS ? definition.targetRadius : refAnalysisPos.avgRadius,
            definition.specifyWidth,
            definition.specifyWidth ? definition.targetWaistWidth / 2 : refAnalysisPos.waistWidth,
            tolerance,
            outputDegree, strictArcs, approxTolerance, maxControlPoints);

        // =====================================================================
        // STEP 6: Transform +Y tip/tail
        // =====================================================================
        var tipYScale = (abs(refAnalysisPos.fcpWidth) > tolerance) ?
            (scaledPos.fcpWidth / refAnalysisPos.fcpWidth) : 1;
        var tailYScale = (abs(refAnalysisPos.acpWidth) > tolerance) ?
            (scaledPos.acpWidth / refAnalysisPos.acpWidth) : 1;
        
        var transformedTipPos = transformTipTail(categorized.tipPos,
            refFcp[0], newFcp[0], tipYScale, true);
        var transformedTailPos = transformTipTail(categorized.tailPos,
            refAcp[0], newAcp[0], tailYScale, false);

        // =====================================================================
        // STEP 7: Build -Y side
        // =====================================================================
        var scaledNegCurves = [];
        var transformedTipNeg = [];
        var transformedTailNeg = [];
        
        if (definition.symmetryMode == SymmetryMode.SYMMETRIC)
        {
            // Mirror +Y results to -Y by negating Y in control points
            scaledNegCurves = mirrorCurvesY(scaledPos.curves);
            transformedTipNeg = mirrorCurvesY(transformedTipPos);
            transformedTailNeg = mirrorCurvesY(transformedTailPos);
        }
        else // ASYMMETRIC
        {
            // Analyze -Y reference (flip to +Y space, scale, flip back)
            var negFlipped = mirrorCurvesY(categorized.sidecutNeg);
            var refAnalysisNeg = analyzeReferenceSidecut(negFlipped,
                refFcp[0], refAcp[0], refMrs[0], tolerance);
            
            // Scale -Y sidecut using -Y settings
            var negScaleMode = definition.negScaleMode;
            var negPinLocation = (negScaleMode == FootprintScaleMode.KEEP_TAPER) ?
                definition.negPinLocation : ScalePinLocation.PIN_ACP;
            var negTargetRadius = (negScaleMode == FootprintScaleMode.SCALE_RADIUS) ?
                definition.negTargetRadius : refAnalysisNeg.avgRadius;
            var negSpecifyWidth = definition.negSpecifyWidth;
            var negTargetWaistWidth = negSpecifyWidth ?
                definition.negTargetWaistWidth / 2 : refAnalysisNeg.waistWidth;

            // Output degree and strict arcs are now always available (not mode-dependent)
            var negOutputDegree = definition.negOutputDegree;
            var negStrictArcs = definition.negStrictArcs;
            var negApproximationTolerance = definition.negApproximationTolerance;
            var negMaxControlPoints = definition.negMaxControlPoints;

            var scaledNegFlipped = scaleSidecut(context, id + "scaledNeg", negFlipped, refAnalysisNeg,
                refFcp[0], refAcp[0], refMrs[0],
                newFcp[0], newAcp[0], newMrs[0],
                negScaleMode, negPinLocation, negTargetRadius,
                negSpecifyWidth, negTargetWaistWidth, tolerance,
                negOutputDegree, negStrictArcs, negApproximationTolerance, negMaxControlPoints);
            
            // Flip back to -Y space
            scaledNegCurves = mirrorCurvesY(scaledNegFlipped.curves);
            
            // Transform -Y tip/tail
            var negTipYScale = (abs(refAnalysisNeg.fcpWidth) > tolerance) ?
                (scaledNegFlipped.fcpWidth / refAnalysisNeg.fcpWidth) : 1;
            var negTailYScale = (abs(refAnalysisNeg.acpWidth) > tolerance) ?
                (scaledNegFlipped.acpWidth / refAnalysisNeg.acpWidth) : 1;
            
            // Flip -Y tip/tail to +Y, transform, flip back
            var negTipFlipped = mirrorCurvesY(categorized.tipNeg);
            var negTailFlipped = mirrorCurvesY(categorized.tailNeg);
            
            transformedTipNeg = mirrorCurvesY(
                transformTipTail(negTipFlipped, refFcp[0], newFcp[0], negTipYScale, true));
            transformedTailNeg = mirrorCurvesY(
                transformTipTail(negTailFlipped, refAcp[0], newAcp[0], negTailYScale, false));
        }
        
        // =====================================================================
        // STEP 8: Output assembly
        //
        // Create individual BSpline curves, then stitch into wire bodies.
        // CRITICAL: We do SEPARATE opExtractWires for +Y and -Y to prevent
        // them merging into a single closed wire body.  When tip/tail curves
        // meet at Y=0, the +Y and -Y endpoints coincide — a single
        // opExtractWires would stitch everything into one closed loop,
        // which breaks downstream analyzeFootprint (expects open +Y wires).
        // =====================================================================
        var posCurves = concatenateArrays([
            transformedTipPos, scaledPos.curves, transformedTailPos
        ]);
        var negCurves = concatenateArrays([
            transformedTipNeg, scaledNegCurves, transformedTailNeg
        ]);

        // =====================================================================
        // STEP 9: G1 continuity repair at FCP / ACP
        //
        // Operates on the fully assembled curve arrays.  Uses only control
        // point positions — no evaluateSpline calls.  For each junction:
        //   • finds the sidecut curve (its non-junction end is on the interior
        //     side) and reads the tangent from its endpoint control points
        //   • for every tip/tail curve at the same junction, moves the single
        //     adjacent CP to align with the sidecut tangent (minimum change:
        //     only direction changes, distance is preserved)
        // =====================================================================
        if (definition.enforceTipTangency)
        {
            posCurves = fixG1(posCurves, newFcp[0], true,  tolerance);
            negCurves = fixG1(negCurves, newFcp[0], true,  tolerance);
        }
        if (definition.enforceTailTangency)
        {
            posCurves = fixG1(posCurves, newAcp[0], false, tolerance);
            negCurves = fixG1(negCurves, newAcp[0], false, tolerance);
        }

        // --- Create +Y curves and stitch into wire(s) ---
        var posEdgeQueries = [];
        for (var i = 0; i < size(posCurves); i += 1)
        {
            var curveId = id + ("positiveCurve" ~ i);
            opCreateBSplineCurve(context, curveId, {
                "bSplineCurve" : posCurves[i]
            });
            posEdgeQueries = append(posEdgeQueries, qCreatedBy(curveId, EntityType.EDGE));
        }
        
        if (size(posEdgeQueries) > 0)
        {
            opExtractWires(context, id + "extractWiresPos", {
                "edges" : qUnion(posEdgeQueries)
            });
        }
        
        // --- Create -Y curves and stitch into wire(s) ---
        var negEdgeQueries = [];
        for (var i = 0; i < size(negCurves); i += 1)
        {
            var curveId = id + ("negativeCurve" ~ i);
            opCreateBSplineCurve(context, curveId, {
                "bSplineCurve" : negCurves[i]
            });
            negEdgeQueries = append(negEdgeQueries, qCreatedBy(curveId, EntityType.EDGE));
        }
        
        if (size(negEdgeQueries) > 0)
        {
            opExtractWires(context, id + "extractWiresNeg", {
                "edges" : qUnion(negEdgeQueries)
            });
        }
        
        // --- Cleanup: delete original loose curve bodies ---
        var looseBodies = [];
        for (var i = 0; i < size(posCurves); i += 1)
        {
            looseBodies = append(looseBodies, qCreatedBy(id + ("positiveCurve" ~ i), EntityType.BODY));
        }
        for (var i = 0; i < size(negCurves); i += 1)
        {
            looseBodies = append(looseBodies, qCreatedBy(id + ("negativeCurve" ~ i), EntityType.BODY));
        }
        opDeleteBodies(context, id + "deleteLooseCurves", {
            "entities" : qUnion(looseBodies)
        });
        
        // Optionally delete reference curves
        if (!definition.keepReference)
        {
            opDeleteBodies(context, id + "deleteRef", {
                "entities" : qOwnerBody(definition.refEdges)
            });
        }
    });

// =============================================================================
// UNIFIED SIDECUT SCALING DISPATCHER
// =============================================================================

/**
 * Routes to the correct scaling function based on mode.
 * This avoids repeating the mode-switch logic for +Y and -Y sides.
 */
function scaleSidecut(context is Context, id is Id, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits, refMrsX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits, newMrsX is ValueWithUnits,
    scaleMode is FootprintScaleMode, pinLocation is ScalePinLocation,
    targetRadius is ValueWithUnits,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits,
    outputDegree is number, strictArcs is boolean,
    approxTolerance is ValueWithUnits, maxControlPoints is number) returns map
{
    if (scaleMode == FootprintScaleMode.ACCORDION)
    {
        return scaleAccordion(context, id, sidecutCurves, refAnalysis,
            refFcpX, refAcpX, newFcpX, newAcpX,
            specifyWidth, targetWaistWidth, tolerance,
            outputDegree, strictArcs);
    }
    else if (scaleMode == FootprintScaleMode.KEEP_TAPER)
    {
        return scaleKeepTaper(context, id, sidecutCurves, refAnalysis,
            refFcpX, refAcpX, refMrsX,
            newFcpX, newAcpX, newMrsX,
            pinLocation, specifyWidth, targetWaistWidth, tolerance,
            outputDegree, strictArcs);
    }
    else // SCALE_RADIUS
    {
        return scaleRadius(context, id, sidecutCurves, refAnalysis,
            refFcpX, refAcpX, newFcpX, newAcpX,
            targetRadius, specifyWidth, targetWaistWidth, tolerance,
            outputDegree, strictArcs, approxTolerance, maxControlPoints);
    }
}

// =============================================================================
// RSL DATA EXTRACTION
// =============================================================================

/**
 * Extract FCP, ACP, MRS from an RSL edge.
 * FCP is at more negative X. MRS is found as the geometric midpoint in X,
 * not parameter 0.5, to handle non-arc-length parameterized edges.
 */
function extractRslData(context is Context, rslEdge is Query) returns map
{
    // Get endpoints
    var endpoints = evEdgeTangentLines(context, {
        "edge" : rslEdge,
        "parameters" : [0, 1]
    });
    
    var p0 = endpoints[0].origin;
    var p1 = endpoints[1].origin;
    
    // FCP is at more negative X (forebody side)
    var fcp;
    var acp;
    if (p0[0] < p1[0])
    {
        fcp = p0;
        acp = p1;
    }
    else
    {
        fcp = p1;
        acp = p0;
    }
    
    // Find MRS: the point on the RSL edge whose X equals the geometric
    // midpoint of FCP and ACP.  We first scan 51 samples to bracket
    // the parameter, then bisect to within 1e-6 m of the target X.
    var midX = (fcp[0] + acp[0]) / 2;
    var mrsTol = 1e-6 * meter;
    var numSamples = 51;
    var sampleParams = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        sampleParams = append(sampleParams, i / (numSamples - 1));
    }
    
    var sampleTangents = evEdgeTangentLines(context, {
        "edge" : rslEdge,
        "parameters" : sampleParams
    });
    
    // Find the two adjacent samples that bracket midX (sign change in x - midX)
    var bestIdx = 0;
    var bestDist = inf * meter;
    for (var i = 0; i < numSamples; i += 1)
    {
        var dist = abs(sampleTangents[i].origin[0] - midX);
        if (dist < bestDist)
        {
            bestDist = dist;
            bestIdx = i;
        }
    }
    
    // If the closest sample is already within tolerance, use it directly
    var mrs = sampleTangents[bestIdx].origin;
    
    if (bestDist > mrsTol)
    {
        // Bracket: find adjacent pair with sign change in (x - midX)
        var uLo = 0.0;
        var uHi = 1.0;
        var dxAtLo = 0 * meter;
        var foundBracket = false;
        
        for (var i = 0; i < numSamples - 1; i += 1)
        {
            var dx0 = sampleTangents[i].origin[0] - midX;
            var dx1 = sampleTangents[i + 1].origin[0] - midX;
            
            if (dx0 * dx1 < 0 * meter * meter)
            {
                uLo = sampleParams[i];
                uHi = sampleParams[i + 1];
                dxAtLo = dx0;
                foundBracket = true;
                break;
            }
        }
        
        if (foundBracket)
        {
            // Bisect until X is within mrsTol of midX
            for (var iter = 0; iter < 40; iter += 1)
            {
                var uMid = (uLo + uHi) / 2;
                var ptMid = evEdgeTangentLines(context, {
                    "edge" : rslEdge,
                    "parameters" : [uMid]
                })[0].origin;
                
                var dxMid = ptMid[0] - midX;
                
                if (abs(dxMid) < mrsTol)
                {
                    mrs = ptMid;
                    break;
                }
                
                if (dxAtLo * dxMid < 0 * meter * meter)
                {
                    uHi = uMid;
                }
                else
                {
                    uLo = uMid;
                    dxAtLo = dxMid;
                }
                
                mrs = ptMid; // keep best so far
            }
        }
    }
    
    return {
        "fcp" : fcp,
        "acp" : acp,
        "mrs" : mrs
    };
}

// =============================================================================
// ANALYSIS CONFIG & HELPERS
// =============================================================================

/**
 * Build config map for fpt_analyze solver functions.
 * Mirrors fpt_analyze::buildConfig defaults so we can call findWaistPoint,
 * findWidestPoint, findInflectionPoint, computeAverageRadius directly.
 */
function buildAnalysisConfig() returns map
{
    return {
        "tangentSolveTol" : 1e-12,
        "paramBracketSamples" : 50,
        "maxSolverIterations" : 30,
        "xTolerance" : 0.001 * millimeter,
        "yTolerance" : 0.001 * millimeter
    };
}

/**
 * Compute signed taper angle between two points (typically widest points).
 *
 * Convention:
 *   positive = FB-side point has larger Y (tip wider than tail)
 *   negative = AB-side point has larger Y (tail wider than tip)
 *
 * fbPoint/abPoint are the actual curve points (from findWidestPoint),
 * where Y = half-width.  We use the signed delta so that scaleKeepTaper
 * can compute the correct rotation direction.
 *
 * Note: fpt_analyze::computeTaperAngle returns abs(angle) which is fine
 * for reporting but loses the sign we need for rotation.
 */
function computeSignedTaper(fbPoint is Vector, abPoint is Vector) returns ValueWithUnits
{
    var deltaY = fbPoint[1] - abPoint[1];           // positive when FB wider
    var deltaX = abs(abPoint[0] - fbPoint[0]);       // always positive distance
    
    if (deltaX / meter < 1e-12)
        return 0 * degree;
    
    return atan2(deltaY, deltaX);
}

// =============================================================================
// REFERENCE SIDECUT ANALYSIS
// =============================================================================

/**
 * Analyze reference sidecut curves to extract key dimensions.
 *
 * Uses fpt_analyze solver functions for critical-point detection:
 *   - findWaistPoint (solver-refined minimum Y)
 *   - findWidestPoint (solver-refined maximum Y on each side)
 *   - findInflectionPoint (curvature sign change)
 *   - computeAverageRadius (mean radius in sidecut region)
 *
 * Taper angle is measured between widest points (signed), consistent
 * with how analyzeFootprintCurves defines taper.
 *
 * Endpoint widths at FCP/ACP are kept separately — they're needed for
 * tip/tail junction scaling, not taper measurement.
 */
function analyzeReferenceSidecut(sidecutCurves is array, fcpX is ValueWithUnits, acpX is ValueWithUnits,
    mrsX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    var curveData = buildCurveDataArray(sidecutCurves);
    var config = buildAnalysisConfig();
    
    var xLo = min([fcpX, acpX]);
    var xHi = max([fcpX, acpX]);
    
    // --- Endpoint widths (for tip/tail junction scaling) ---
    var fcpWidth = getEndpointWidthAtX(curveData, fcpX).y;
    var acpWidth = getEndpointWidthAtX(curveData, acpX).y;
    var mrsWidth = getWidthAtX(curveData, mrsX, tolerance);
    
    // --- Critical points via fpt_analyze solvers ---
    var waist = findWaistPoint(curveData, xLo, xHi, config);
    if (!waist.found)
    {
        // Fallback: use MRS location as waist
        waist = { "found" : true, "width" : mrsWidth, "x" : mrsX,
                  "point" : vector(mrsX, mrsWidth, 0 * meter) };
    }
    
    // FB widest: between FCP and waist.  AB widest: between waist and ACP.
    // (FCP is at lower X, ACP at higher X in our convention)
    var fbWidest = findWidestPoint(curveData, xLo, waist.x, config);
    var abWidest = findWidestPoint(curveData, waist.x, xHi, config);
    
    if (!fbWidest.found)
    {
        // Fallback: use FCP endpoint as widest
        fbWidest = { "found" : false, "width" : fcpWidth, "x" : fcpX,
                     "point" : vector(fcpX, fcpWidth, 0 * meter) };
    }
    if (!abWidest.found)
    {
        // Fallback: use ACP endpoint as widest
        abWidest = { "found" : false, "width" : acpWidth, "x" : acpX,
                     "point" : vector(acpX, acpWidth, 0 * meter) };
    }
    
    // --- Taper angle between widest points (signed) ---
    // Determine which widest point is on the FB side (closer to FCP)
    var fbPoint;
    var abPoint;
    if (abs(fbWidest.x - fcpX) < abs(abWidest.x - fcpX))
    {
        fbPoint = fbWidest.point;
        abPoint = abWidest.point;
    }
    else
    {
        fbPoint = abWidest.point;
        abPoint = fbWidest.point;
    }
    var taperAngle = computeSignedTaper(fbPoint, abPoint);
    
    // --- Inflection points ---
    var fbInflection = findInflectionPoint(curveData, waist.x, fbWidest.x, true, config);
    var abInflection = findInflectionPoint(curveData, waist.x, abWidest.x, true, config);
    
    // --- Average radius ---
    // Use inflection points as bounds if found, otherwise widest points
    var radiusXMin = fbInflection.found ? fbInflection.x : fbWidest.x;
    var radiusXMax = abInflection.found ? abInflection.x : abWidest.x;
    var avgRadiusResult = computeAverageRadius(curveData,
        min([radiusXMin, radiusXMax]),
        max([radiusXMin, radiusXMax]),
        config);

    var avgRadius = avgRadiusResult.valid ? avgRadiusResult.avgRadius : (1000 * meter);

    return {
        // Endpoint widths (for tip/tail junction scaling)
        "fcpWidth" : fcpWidth,
        "acpWidth" : acpWidth,
        "mrsWidth" : mrsWidth,
        
        // Critical points
        "waistWidth" : waist.width,
        "waistX" : waist.x,
        "fbWidest" : fbWidest,
        "abWidest" : abWidest,
        "fbInflection" : fbInflection,
        "abInflection" : abInflection,
        
        // Taper (signed, between widest points)
        "taperAngle" : taperAngle,
        
        // Radius
        "avgRadius" : avgRadius,
        
        // Raw curve data (for scaleRadius curvature sampling)
        "curveData" : curveData
    };
}

/**
 * Get Y value at a curve endpoint nearest the target X.
 *
 * Unlike getWidthAtX (which evaluates at an exact X via bisection),
 * this reads the actual BSpline endpoints.  This is essential after
 * rotation, where the curve endpoint X may have shifted slightly from
 * the nominal FCP/ACP position.
 *
 * Returns a map with "y" and "x" (the actual endpoint position).
 */
function getEndpointWidthAtX(curveData is array, targetX is ValueWithUnits) returns map
{
    var bestDist = inf * meter;
    var bestY = 0 * meter;
    var bestX = targetX;
    
    for (var cd in curveData)
    {
        var bspline = cd.bspline;
        var range = getBSplineParamRange(bspline);
        
        // Evaluate both endpoints
        var pts = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMin, range.uMax] })[0];
        
        for (var pt in pts)
        {
            var dist = abs(pt[0] - targetX);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestY = pt[1];
                bestX = pt[0];
            }
        }
    }
    
    return { "y" : bestY, "x" : bestX };
}

/**
 * Get Y value (width) at a specific X coordinate by evaluating the BSpline.
 * 
 * The containment check uses a wide tolerance (1mm) because transformations
 * like rotation can shift curve X extents beyond the geometry tolerance.
 * The precision comes from findParamAtX, not the containment filter.
 *
 * Best for interior queries (waist, curvature sample points) where the
 * target X is well inside a curve.  For endpoint queries (FCP/ACP width),
 * use getEndpointWidthAtX instead.
 */
function getWidthAtX(curveData is array, targetX is ValueWithUnits, tolerance is ValueWithUnits) returns ValueWithUnits
{
    var searchTol = 1 * millimeter;  // wide filter — just finding the right curve
    for (var cd in curveData)
    {
        if (cd.xMin <= targetX + searchTol && cd.xMax >= targetX - searchTol)
        {
            var param = findParamAtX(cd.bspline, targetX, tolerance);
            var pt = evaluateSpline({ "spline" : cd.bspline, "parameters" : [param] })[0][0];
            return pt[1];
        }
    }
    return 0 * millimeter;
}


/**
 * Get curvature at a specific X coordinate.
 */
function getCurvatureAtX(curveData is array, targetX is ValueWithUnits, tolerance is ValueWithUnits)
{
    var searchTol = 1 * millimeter;
    for (var cd in curveData)
    {
        if (cd.xMin <= targetX + searchTol && cd.xMax >= targetX - searchTol)
        {
            var param = findParamAtX(cd.bspline, targetX, tolerance);
            var curv = getBSplineCurvatureAtParam(cd.bspline, param);
            return curv.curvatureSigned;
        }
    }
    return 0 / meter;
}

// =============================================================================
// CURVE CATEGORIZATION  (+Y / -Y split)
// =============================================================================

/**
 * Categorize BSpline curves into tip/sidecut/tail AND +Y/-Y sides.
 * Curves spanning contact points are split using approximateSpline.
 * Returns separate arrays for each Y side.
 *
 * @returns map with keys: tipPos, sidecutPos, tailPos, tipNeg, sidecutNeg, tailNeg
 */
function categorizeCurvesWithSides(context is Context, bsplines is array, fcpX is ValueWithUnits,
    acpX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    var tipPos = [];
    var sidecutPos = [];
    var tailPos = [];
    var tipNeg = [];
    var sidecutNeg = [];
    var tailNeg = [];
    
    for (var bspline in bsplines)
    {
        var bounds = getBSplineBounds(bspline);
        var xMin = bounds.xMin;
        var xMax = bounds.xMax;
        var yMin = bounds.yMin;
        var yMax = bounds.yMax;
        
        // Determine Y side: +Y if yMin >= -tolerance, -Y if yMax <= tolerance
        // Curves crossing Y=0 within sidecut should not occur (opposite ski sides)
        var isPos = (yMin >= -tolerance);
        var isNeg = (yMax <= tolerance);
        
        // If curve somehow spans both sides, assign to +Y (safe default)
        if (!isPos && !isNeg)
        {
            isPos = true;
        }
        
        // X categorization
        // Key insight: tip/tail curves from integrateFootprint terminate
        // exactly at FCP/ACP.  The tests must use INCLUSIVE bounds at the
        // contact points so these curves aren't silently dropped.
        //
        //   tip:     curve doesn't extend past FCP  →  xMax <= fcpX + tol
        //   tail:    curve doesn't extend past ACP  →  xMin >= acpX - tol
        //   sidecut: curve sits between FCP and ACP →  xMin >= fcpX - tol && xMax <= acpX + tol
        //   spanning: everything else               →  split at contacts
        
        if (xMax <= fcpX + tolerance)
        {
            // Entirely in tip region (extends up to but not past FCP)
            if (isPos) tipPos = append(tipPos, bspline);
            else       tipNeg = append(tipNeg, bspline);
            continue;
        }
        
        if (xMin >= acpX - tolerance)
        {
            // Entirely in tail region (starts at or past ACP)
            if (isPos) tailPos = append(tailPos, bspline);
            else       tailNeg = append(tailNeg, bspline);
            continue;
        }
        
        if (xMin >= fcpX - tolerance && xMax <= acpX + tolerance)
        {
            // Entirely in sidecut region
            if (isPos) sidecutPos = append(sidecutPos, bspline);
            else       sidecutNeg = append(sidecutNeg, bspline);
            continue;
        }
        
        // Curve spans boundaries — split at contact points
        var splits = splitCurveAtContacts(context, bspline, fcpX, acpX, tolerance);
        
        if (splits.tipPortion != undefined)
        {
            if (isPos) tipPos = append(tipPos, splits.tipPortion);
            else       tipNeg = append(tipNeg, splits.tipPortion);
        }
        if (splits.sidecutPortion != undefined)
        {
            if (isPos) sidecutPos = append(sidecutPos, splits.sidecutPortion);
            else       sidecutNeg = append(sidecutNeg, splits.sidecutPortion);
        }
        if (splits.tailPortion != undefined)
        {
            if (isPos) tailPos = append(tailPos, splits.tailPortion);
            else       tailNeg = append(tailNeg, splits.tailPortion);
        }
    }
    
    return {
        "tipPos" : tipPos,
        "sidecutPos" : sidecutPos,
        "tailPos" : tailPos,
        "tipNeg" : tipNeg,
        "sidecutNeg" : sidecutNeg,
        "tailNeg" : tailNeg
    };
}

/**
 * Split a BSpline at FCP and/or ACP X coordinates.
 * Uses approximateSpline for accurate subcurves.
 */
function splitCurveAtContacts(context is Context, bspline is BSplineCurve, fcpX is ValueWithUnits,
    acpX is ValueWithUnits, tolerance is ValueWithUnits) returns map
{
    var bounds = getBSplineBounds(bspline);
    var range = getBSplineParamRange(bspline);
    
    var tipPortion = undefined;
    var sidecutPortion = undefined;
    var tailPortion = undefined;
    
    var fcpParam = undefined;
    var acpParam = undefined;
    
    if (bounds.xMin < fcpX - tolerance && bounds.xMax > fcpX + tolerance)
    {
        fcpParam = findParamAtX(bspline, fcpX, tolerance);
    }
    
    if (bounds.xMin < acpX - tolerance && bounds.xMax > acpX + tolerance)
    {
        acpParam = findParamAtX(bspline, acpX, tolerance);
    }
    
    if (fcpParam != undefined && acpParam != undefined)
    {
        var uFcp = fcpParam;
        var uAcp = acpParam;
        if (uFcp > uAcp)
        {
            var temp = uFcp;
            uFcp = uAcp;
            uAcp = temp;
        }
        
        tipPortion = extractBSplineSubcurve(context, bspline, range.uMin, uFcp, 20);
        sidecutPortion = extractBSplineSubcurve(context, bspline, uFcp, uAcp, 30);
        tailPortion = extractBSplineSubcurve(context, bspline, uAcp, range.uMax, 20);
    }
    else if (fcpParam != undefined)
    {
        var startPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMin] })[0][0];
        if (startPt[0] < fcpX)
        {
            tipPortion = extractBSplineSubcurve(context, bspline, range.uMin, fcpParam, 20);
            sidecutPortion = extractBSplineSubcurve(context, bspline, fcpParam, range.uMax, 30);
        }
        else
        {
            sidecutPortion = extractBSplineSubcurve(context, bspline, range.uMin, fcpParam, 30);
            tipPortion = extractBSplineSubcurve(context, bspline, fcpParam, range.uMax, 20);
        }
    }
    else if (acpParam != undefined)
    {
        var startPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMin] })[0][0];
        if (startPt[0] < acpX)
        {
            sidecutPortion = extractBSplineSubcurve(context, bspline, range.uMin, acpParam, 30);
            tailPortion = extractBSplineSubcurve(context, bspline, acpParam, range.uMax, 20);
        }
        else
        {
            tailPortion = extractBSplineSubcurve(context, bspline, range.uMin, acpParam, 20);
            sidecutPortion = extractBSplineSubcurve(context, bspline, acpParam, range.uMax, 30);
        }
    }
    
    return {
        "tipPortion" : tipPortion,
        "sidecutPortion" : sidecutPortion,
        "tailPortion" : tailPortion
    };
}

/**
 * Find parameter where BSpline has X = targetX.
 */
function findParamAtX(bspline is BSplineCurve, targetX is ValueWithUnits, tolerance is ValueWithUnits) returns number
{
    var range = getBSplineParamRange(bspline);
    
    var numSamples = 50;
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, range.uMin + (range.uMax - range.uMin) * i / (numSamples - 1));
    }
    
    var positions = evaluateSpline({ "spline" : bspline, "parameters" : params })[0];
    
    for (var i = 0; i < size(params) - 1; i += 1)
    {
        var x1 = positions[i][0] - targetX;
        var x2 = positions[i + 1][0] - targetX;
        
        if (x1 * x2 < 0 * meter * meter)
        {
            var uLo = params[i];
            var uHi = params[i + 1];
            
            for (var iter = 0; iter < 20; iter += 1)
            {
                var uMid = (uLo + uHi) / 2;
                var ptMid = evaluateSpline({ "spline" : bspline, "parameters" : [uMid] })[0][0];
                var xMid = ptMid[0] - targetX;
                
                if (abs(xMid) < tolerance)
                    return uMid;
                
                if (x1 * xMid < 0 * meter * meter)
                    uHi = uMid;
                else
                {
                    uLo = uMid;
                    x1 = xMid;
                }
            }
            
            return (uLo + uHi) / 2;
        }
    }
    
    return (range.uMin + range.uMax) / 2;
}

// =============================================================================
// ACCORDION SCALING
// =============================================================================

/**
 * Scale sidecut curves using accordion method.
 * X is stretched/compressed to new RSL length.
 * If target width specified, Y is uniformly scaled to achieve it.
 */
function scaleAccordion(context is Context, id is Id, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits,
    outputDegree is number, strictArcs is boolean) returns map
{
    var refLength = abs(refAcpX - refFcpX);
    var newLength = abs(newAcpX - newFcpX);
    var xScale = newLength / refLength;
    
    var yScale = 1.0;
    if (specifyWidth && abs(refAnalysis.waistWidth) > tolerance)
    {
        yScale = targetWaistWidth / refAnalysis.waistWidth;
    }

    var scaledCurves = [];
    
    for (var bspline in sidecutCurves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];
        
        for (var pt in controlPoints)
        {
            var relativeX = pt[0] - refFcpX;
            var newX = newFcpX + relativeX * xScale;
            var newY = pt[1] * yScale;
            
            var newPt = vector(newX, newY, pt[2]);
            newControlPoints = append(newControlPoints, newPt);
        }
        
        // Build BSpline without knots (let them be computed automatically)
        var params = {
            "degree" : bspline.degree,
            "controlPoints" : newControlPoints
        };

        if (bspline.weights != undefined)
            params.weights = bspline.weights;

        if (bspline.isRational != undefined)
            params.isRational = bspline.isRational;

        if (bspline.isPeriodic != undefined)
            params.isPeriodic = bspline.isPeriodic;

        scaledCurves = append(scaledCurves, bSplineCurve(params));
    }
    
    // Compute resulting widths
    var scaledCurveData = buildCurveDataArray(scaledCurves);
    var newFcpWidth = getEndpointWidthAtX(scaledCurveData, newFcpX).y;
    var newAcpWidth = getEndpointWidthAtX(scaledCurveData, newAcpX).y;
    
    var config = buildAnalysisConfig();
    var xLo = min([newFcpX, newAcpX]);
    var xHi = max([newFcpX, newAcpX]);
    var waist = findWaistPoint(scaledCurveData, xLo, xHi, config);
    var waistWidth = waist.found ? waist.width : (newFcpWidth + newAcpWidth) / 2;

    // Apply strict arcs conversion if requested
    if (strictArcs)
    {
        scaledCurves = forceQuadraticNurbs(context, id + "accordionArcs", scaledCurves);
    }

    return {
        "curves" : scaledCurves,
        "fcpWidth" : newFcpWidth,
        "acpWidth" : newAcpWidth,
        "waistWidth" : waistWidth
    };
}

// =============================================================================
// KEEP TAPER SCALING
// =============================================================================

/**
 * Scale sidecut curves while preserving taper angle.
 * 
 * Process:
 * 1. Accordion X to new RSL length
 * 2. Find widest points on accordioned curves
 * 3. Compute taper angle between widest points (signed)
 * 4. Rotate about pin point to restore reference taper angle
 * 5. Optionally shift Y to hit target waist width
 *
 * Taper is measured between widest points, consistent with
 * analyzeFootprintCurves / computeTaperAngle convention.
 *
 * Pin semantics:
 *   PIN_ACP → pivot at ACP X (preserve width at ACP)
 *   PIN_MRS → pivot at MRS X (preserve width at MRS)
 */
function scaleKeepTaper(context is Context, id is Id, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits, refMrsX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits, newMrsX is ValueWithUnits,
    pinLocation is ScalePinLocation,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits,
    outputDegree is number, strictArcs is boolean) returns map
{
    var refLength = abs(refAcpX - refFcpX);
    var newLength = abs(newAcpX - newFcpX);
    var xScale = newLength / refLength;
    var config = buildAnalysisConfig();
    var xLo = min([newFcpX, newAcpX]);
    var xHi = max([newFcpX, newAcpX]);
    
    // Step 1: Accordion X coordinates
    var accordionedCurves = [];
    for (var bspline in sidecutCurves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];
        
        for (var pt in controlPoints)
        {
            var relativeX = pt[0] - refFcpX;
            var newX = newFcpX + relativeX * xScale;
            var newPt = vector(newX, pt[1], pt[2]);
            newControlPoints = append(newControlPoints, newPt);
        }
        
        var params = {
            "degree" : bspline.degree,
            "controlPoints" : newControlPoints
        };

        if (bspline.weights != undefined)
            params.weights = bspline.weights;

        if (bspline.isRational != undefined)
            params.isRational = bspline.isRational;

        if (bspline.isPeriodic != undefined)
            params.isPeriodic = bspline.isPeriodic;

        accordionedCurves = append(accordionedCurves, bSplineCurve(params));
    }
    
    // Step 2: Analyze accordioned curves — find widest points for taper
    var accordionedData = buildCurveDataArray(accordionedCurves);
    
    var accordionedWaist = findWaistPoint(accordionedData, xLo, xHi, config);
    if (!accordionedWaist.found)
    {
        // Fallback: midpoint
        accordionedWaist = { "found" : false, "x" : (xLo + xHi) / 2 };
    }
    
    var accFbWidest = findWidestPoint(accordionedData, xLo, accordionedWaist.x, config);
    var accAbWidest = findWidestPoint(accordionedData, accordionedWaist.x, xHi, config);
    
    // Determine FB/AB by proximity to FCP
    var accFbPoint;
    var accAbPoint;
    if (accFbWidest.found && accAbWidest.found)
    {
        if (abs(accFbWidest.x - newFcpX) < abs(accAbWidest.x - newFcpX))
        {
            accFbPoint = accFbWidest.point;
            accAbPoint = accAbWidest.point;
        }
        else
        {
            accFbPoint = accAbWidest.point;
            accAbPoint = accFbWidest.point;
        }
    }
    else
    {
        // Fallback: use endpoint widths (degenerate case)
        var accFcpW = getEndpointWidthAtX(accordionedData, newFcpX).y;
        var accAcpW = getEndpointWidthAtX(accordionedData, newAcpX).y;
        accFbPoint = vector(newFcpX, accFcpW, 0 * meter);
        accAbPoint = vector(newAcpX, accAcpW, 0 * meter);
    }
    
    var currentTaper = computeSignedTaper(accFbPoint, accAbPoint);
    var refTaper = refAnalysis.taperAngle;  // already signed, between widest points
    // Rotation about ACP pivot: positive R (CCW) moves the FCP-side of the
    // sidecut DOWN, reducing taper; negative R (CW) raises it, increasing taper.
    // To restore refTaper from currentTaper we therefore need:
    //   R = currentTaper - refTaper
    // (not refTaper - currentTaper, which is the opposite direction)
    var rotationAngle = currentTaper - refTaper;

    // Step 3: Determine pivot point
    var pivotX;
    var pivotY;
    if (pinLocation == ScalePinLocation.PIN_ACP)
    {
        pivotX = newAcpX;
        pivotY = getEndpointWidthAtX(accordionedData, newAcpX).y;
    }
    else  // PIN_MRS
    {
        pivotX = newMrsX;
        pivotY = getWidthAtX(accordionedData, newMrsX, tolerance);
    }

    // Step 4: Apply rotation about pivot
    var rotatedCurves = [];
    for (var bspline in accordionedCurves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];
        
        for (var pt in controlPoints)
        {
            var dx = pt[0] - pivotX;
            var dy = pt[1] - pivotY;
            
            var cosR = cos(rotationAngle);
            var sinR = sin(rotationAngle);
            
            var newX = pivotX + dx * cosR - dy * sinR;
            var newY = pivotY + dx * sinR + dy * cosR;
            
            var newPt = vector(newX, newY, pt[2]);
            newControlPoints = append(newControlPoints, newPt);
        }
        
        var params = {
            "degree" : bspline.degree,
            "controlPoints" : newControlPoints
        };

        if (bspline.weights != undefined)
            params.weights = bspline.weights;

        if (bspline.isRational != undefined)
            params.isRational = bspline.isRational;

        if (bspline.isPeriodic != undefined)
            params.isPeriodic = bspline.isPeriodic;

        rotatedCurves = append(rotatedCurves, bSplineCurve(params));
    }
    
    // Step 5: Optionally shift Y to hit target waist width
    var finalCurves = rotatedCurves;
    if (specifyWidth)
    {
        var rotatedData = buildCurveDataArray(rotatedCurves);
        var currentWaist = findWaistPoint(rotatedData, xLo, xHi, config);
        var currentWaistWidth = currentWaist.found ? currentWaist.width :
            getWidthAtX(rotatedData, (xLo + xHi) / 2, tolerance);
        var yShift = targetWaistWidth - currentWaistWidth;

        finalCurves = [];
        for (var bspline in rotatedCurves)
        {
            var controlPoints = bspline.controlPoints;
            var newControlPoints = [];
            
            for (var pt in controlPoints)
            {
                var newPt = vector(pt[0], pt[1] + yShift, pt[2]);
                newControlPoints = append(newControlPoints, newPt);
            }
            
            var params = {
                "degree" : bspline.degree,
                "controlPoints" : newControlPoints
            };

            if (bspline.weights != undefined)
                params.weights = bspline.weights;

            if (bspline.isRational != undefined)
                params.isRational = bspline.isRational;

            if (bspline.isPeriodic != undefined)
                params.isPeriodic = bspline.isPeriodic;

            finalCurves = append(finalCurves, bSplineCurve(params));
        }
    }
    
    // Compute final widths — use endpoint evaluation since rotation shifts X
    var finalData = buildCurveDataArray(finalCurves);
    var finalFcpWidth = getEndpointWidthAtX(finalData, newFcpX).y;
    var finalAcpWidth = getEndpointWidthAtX(finalData, newAcpX).y;
    var finalWaist = findWaistPoint(finalData, xLo, xHi, config);
    var finalWaistWidth = finalWaist.found ? finalWaist.width :
        getWidthAtX(finalData, (xLo + xHi) / 2, tolerance);

    // Apply strict arcs conversion if requested
    if (strictArcs)
    {
        finalCurves = forceQuadraticNurbs(context, id + "taperArcs", finalCurves);
    }

    return {
        "curves" : finalCurves,
        "fcpWidth" : finalFcpWidth,
        "acpWidth" : finalAcpWidth,
        "waistWidth" : finalWaistWidth
    };
}

// =============================================================================
// SCALE RADIUS HELPERS
// =============================================================================

/**
 * Build a single approximated BSpline from X, Y arrays.
 */
function buildSingleCurveFromPoints(context is Context, xSamples is array,
    ySamples is array) returns BSplineCurve
{
    var points = [];
    for (var i = 0; i < size(xSamples); i += 1)
    {
        points = append(points, vector(xSamples[i], ySamples[i], 0 * millimeter));
    }

    return approximateSpline(context, {
        "degree" : 3,
        "tolerance" : 0.001 * millimeter,
        "maxControlPoints" : 30,
        "targets" : [approximationTarget({ "positions" : points })],
        "interpolateIndices" : [0, size(points) - 1]
    })[0];
}

/**
 * Find inflection point (curvature sign change) in a temporary BSpline curve.
 * Searches from xStart toward the waist (center).
 *
 * @param curve : BSplineCurve - The curve to search
 * @param xStart : ValueWithUnits - Starting X position (FCP or ACP)
 * @param searchInward : boolean - True to search toward waist (smaller |X|)
 * @returns map - {found, x, y, param} or {found: false}
 */
function findInflectionInTempCurve(curve is BSplineCurve, xStart is ValueWithUnits,
    searchInward is boolean) returns map
{
    var numSamples = 50;
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    // Sample the curve with curvature
    var samples = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var u = uMin + (uMax - uMin) * i / (numSamples - 1);
        var curv = getBSplineCurvatureAtParam(curve, u);

        samples = append(samples, {
            "u" : u,
            "x" : curv.point[0],
            "y" : curv.point[1],
            "curvatureSigned" : curv.curvatureSigned,
            "sign" : safeSign(curv.curvatureSigned * meter, 1e-12)
        });
    }

    // Determine search direction
    var searchDir = searchInward ? ((xStart > 0 * meter) ? -1 : 1) : ((xStart > 0 * meter) ? 1 : -1);

    // Sort samples in search direction
    samples = sort(samples, function(a, b)
    {
        if (searchDir > 0)
            return a.x - b.x;  // Increasing X
        else
            return b.x - a.x;  // Decreasing X
    });

    // Find first sample at or past xStart
    var startIdx = 0;
    for (var i = 0; i < size(samples); i += 1)
    {
        if (searchDir > 0 && samples[i].x >= xStart)
        {
            startIdx = i;
            break;
        }
        else if (searchDir < 0 && samples[i].x <= xStart)
        {
            startIdx = i;
            break;
        }
    }

    // Search for sign change
    for (var i = startIdx; i < size(samples) - 1; i += 1)
    {
        var s1 = samples[i];
        var s2 = samples[i + 1];

        if (s1.sign != 0 && s2.sign != 0 && s1.sign != s2.sign)
        {
            // Found inflection! Refine with solver
            var uLo = min([s1.u, s2.u]);
            var uHi = max([s1.u, s2.u]);

            var f = function(u)
            {
                var curv = getBSplineCurvatureAtParam(curve, u);
                return curv.curvatureSigned * meter;
            };

            var result = solveRootHybrid(f, uLo, uHi, 1e-9, 20);
            var finalU = result.u;
            var finalCurv = getBSplineCurvatureAtParam(curve, finalU);

            return {
                "found" : true,
                "x" : finalCurv.point[0],
                "y" : finalCurv.point[1],
                "param" : finalU
            };
        }
    }

    return { "found" : false };
}

/**
 * Evaluate average radius between two X positions on a BSpline curve.
 * Uses the same method as analyzeFootprint's computeAverageRadius().
 *
 * @param curve : BSplineCurve - The curve to evaluate
 * @param xMin, xMax : ValueWithUnits - X bounds for evaluation
 * @returns ValueWithUnits - Average radius (inf if no curvature found)
 */
function evaluateRadiusBetweenInflections(curve is BSplineCurve,
    xMin is ValueWithUnits, xMax is ValueWithUnits) returns ValueWithUnits
{
    var numSamples = 50;
    var range = getBSplineParamRange(curve);
    var uMin = range.uMin;
    var uMax = range.uMax;

    var radiusSum = 0 * meter;
    var count = 0;

    for (var i = 0; i < numSamples; i += 1)
    {
        var u = uMin + (uMax - uMin) * i / (numSamples - 1);
        var curv = getBSplineCurvatureAtParam(curve, u);

        // Check if point is within X bounds
        if (curv.point[0] < xMin || curv.point[0] > xMax)
            continue;

        // Only accumulate non-zero curvature
        if (curv.curvatureMag > 1e-9 / meter)
        {
            radiusSum += 1 / curv.curvatureMag;
            count += 1;
        }
    }

    if (count == 0)
        return inf * meter;  // No curvature found

    return radiusSum / count;
}

/**
 * Find the index in xSamples closest to targetX.
 */
function findClosestIndex(xSamples is array, targetX is ValueWithUnits) returns number
{
    var bestIdx = 0;
    var bestDist = abs(xSamples[0] - targetX);

    for (var i = 1; i < size(xSamples); i += 1)
    {
        var dist = abs(xSamples[i] - targetX);
        if (dist < bestDist)
        {
            bestDist = dist;
            bestIdx = i;
        }
    }

    return bestIdx;
}

// =============================================================================
// SCALE RADIUS  (Iterative radius targeting with curve boundary preservation)
// =============================================================================
// NOTE: forceQuadraticNurbs() is imported from integrateFootprint.fs

/**
 * Scale sidecut curves to achieve target average radius while preserving taper angle.
 *
 * Uses iterative radius targeting:
 * 1. Extract curve boundaries for later splitting
 * 2. Initialize radius scale factor (guess: R_ref / R_target)
 * 3. ITERATE until radius converges:
 *    a. Sample curvature UNIFORMLY across sidecut (maintains continuity)
 *    b. Scale by current factor: k_scaled = k_ref * radiusScaleFactor
 *    c. Map X to new RSL length
 *    d. Integrate: build theta_base and y_base (cumTrapz)
 *    e. Solve theta0 to preserve taper angle
 *    f. Solve y0 to hit target waist width
 *    g. Build temporary curve and EVALUATE actual radius
 *    h. Check convergence: |R_actual - R_target| < tolerance
 *    i. Adjust scale factor: radiusScaleFactor *= (R_actual / R_target)
 * 4. SPLIT converged geometry at original curve boundaries
 * 5. Return array of curves with correct radius and preserved structure
 */
function scaleRadius(context is Context, id is Id, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits,
    targetRadius is ValueWithUnits,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits,
    outputDegree is number, strictArcs is boolean,
    approxTolerance is ValueWithUnits, maxControlPoints is number) returns map
{
    var refLength = abs(refAcpX - refFcpX);
    var newLength = abs(newAcpX - newFcpX);
    var xScale = newLength / refLength;

    var refCurveData = refAnalysis.curveData;

    // Extract curve boundaries for later splitting
    var curveBoundaries = [];
    for (var curveIdx = 1; curveIdx < size(refCurveData); curveIdx += 1)
    {
        var cd = refCurveData[curveIdx];
        var boundaryX = cd.xMin;

        // Only include boundaries within sidecut region
        if (boundaryX >= min([refFcpX, refAcpX]) && boundaryX <= max([refFcpX, refAcpX]))
        {
            curveBoundaries = append(curveBoundaries, boundaryX);
        }
    }

    // CRITICAL: Sort boundaries in ascending X order for splitting algorithm
    curveBoundaries = sort(curveBoundaries, function(a, b) { return a - b; });

    // Initial guess for radius scale factor (start at 1.0 to bootstrap)
    var radiusScaleFactor = 1.0;
    var maxIterations = 10;
    var radiusTolerance = 0.01 * meter;  // 1cm tolerance (tightened to compensate for splitting error)

    var finalY = [];
    var newXSamples = [];
    var converged = false;

    // Declare outside loop so they're accessible after loop ends
    var minY = inf * meter;
    var y0 = 0 * meter;

    // Store reference curvature (unscaled) for selective scaling
    var kReference = [];

    // Inflection bounds (will be updated each iteration)
    var inflectionXMin = newFcpX;  // Default to full sidecut (NEW space)
    var inflectionXMax = newAcpX;
    var refInflectionXMin = refFcpX;  // Default to full sidecut (REFERENCE space)
    var refInflectionXMax = refAcpX;

    // ITERATION LOOP: Adjust scale factor until radius matches target
    for (var iteration = 0; iteration < maxIterations; iteration += 1)
    {
        // Sample curvature UNIFORMLY across sidecut (maintains continuity)
        // Scale sample count with number of curves to ensure small curves get adequate resolution
        var numSamples = max([100, size(curveBoundaries) * 30]);
        var refXSamples = [];
        var kSamples = [];

        for (var i = 0; i < numSamples; i += 1)
        {
            var t = i / (numSamples - 1);
            var x = refFcpX + t * (refAcpX - refFcpX);
            refXSamples = append(refXSamples, x);

            var k = getCurvatureAtX(refCurveData, x, tolerance);
            kSamples = append(kSamples, k);
        }

        // Store reference curvature on first iteration
        if (iteration == 0)
        {
            kReference = kSamples;
        }

        // Scale curvature SELECTIVELY
        // - Between inflections: apply radiusScaleFactor
        // - Outside inflections (taper): keep original
        //
        // CRITICAL: Use REFERENCE space coordinates consistently!
        // - kReference was sampled at refXSamples (reference space)
        // - inflectionXMin/Max are in NEW space
        // - Must transform inflection bounds to reference space for comparison
        var scaledK = [];

        // Transform inflection bounds from NEW space to REFERENCE space
        // Formula: refX = refFcpX + (newX - newFcpX) / xScale
        var xScale = (newAcpX - newFcpX) / (refAcpX - refFcpX);
        refInflectionXMin = refFcpX + (inflectionXMin - newFcpX) / xScale;
        refInflectionXMax = refFcpX + (inflectionXMax - newFcpX) / xScale;

        for (var i = 0; i < size(kReference); i += 1)
        {
            // Use REFERENCE X coordinate (matching how kReference was sampled)
            var xRef = refXSamples[i];
            var k = kReference[i];

            // Check if this sample is between inflections (sidecut region) in REFERENCE space
            if (xRef >= refInflectionXMin && xRef <= refInflectionXMax)
            {
                // Scale sidecut curvature
                scaledK = append(scaledK, k * radiusScaleFactor);
            }
            else
            {
                // Keep taper curvature unchanged
                scaledK = append(scaledK, k);
            }
        }

        // Map X to new RSL length
        newXSamples = [];
        for (var refX in refXSamples)
        {
            var relativeX = refX - refFcpX;
            var newX = newFcpX + relativeX * xScale;
            newXSamples = append(newXSamples, newX);
        }

        // Integrate to build base geometry
        // Type contract (matching buildBaseIntegrals):
        //   base.x  = VWU (length)
        //   base.k  = plain number (for solver seed)
        //   base.yP = plain number (dimensionless slope)
        //   base.y  = VWU (length)
        var thetaBase = cumTrapz(newXSamples, scaledK, 0).cumulative;
        var yBase = cumTrapz(newXSamples, thetaBase, 0 * millimeter).cumulative;
        var kPlain = mapArray(scaledK, function(r) { return r * meter; });

        var base = {
            "x" : newXSamples,
            "k" : kPlain,
            "yP" : thetaBase,
            "y" : yBase
        };

        // Solve for theta0 to preserve taper angle
        var integrationDef = {
            "angleDriver" : AngleDriver.TAPER_ANGLE,
            "taperAngle" : refAnalysis.taperAngle
        };

        var theta0 = solveTheta0ForDriver(base, integrationDef, 0.0001 * degree, 50);

        // Evaluate Y and find waist
        var yFinal = evalY(base, theta0, 0 * meter);

        // Update minY for this iteration
        minY = inf * meter;
        for (var i = 0; i < size(yFinal); i += 1)
        {
            if (yFinal[i] < minY)
            {
                minY = yFinal[i];
            }
        }

        // Update y0 for this iteration
        y0 = targetWaistWidth - minY;

        // Apply y0 shift
        finalY = [];
        for (var i = 0; i < size(yFinal); i += 1)
        {
            finalY = append(finalY, yFinal[i] + y0);
        }

        // Build temporary curve for radius evaluation
        var tempCurve = buildSingleCurveFromPoints(context, newXSamples, finalY);

        // Find inflection points (search inward from FCP and ACP toward waist)
        var fbInflection = findInflectionInTempCurve(tempCurve, newFcpX, true);
        var abInflection = findInflectionInTempCurve(tempCurve, newAcpX, true);

        // Determine evaluation bounds (between inflections if found, else full sidecut)
        var evalXMin = fbInflection.found ? fbInflection.x : newFcpX;
        var evalXMax = abInflection.found ? abInflection.x : newAcpX;

        // UPDATE inflection bounds for next iteration
        inflectionXMin = evalXMin;
        inflectionXMax = evalXMax;

        // EVALUATE ACTUAL RADIUS between inflection points (like analyzeFootprint)
        var actualRadius = evaluateRadiusBetweenInflections(tempCurve, evalXMin, evalXMax);

        // Check for invalid radius (straight line or evaluation failure)
        if (actualRadius == inf * meter || actualRadius <= 0 * meter)
        {
            break;
        }

        var error = actualRadius - targetRadius;

        // Check convergence
        if (abs(error) < radiusTolerance)
        {
            converged = true;
            break;
        }

        // Adjust scale factor for next iteration
        // If actual > target, we need MORE curvature (higher k), so HIGHER scale
        radiusScaleFactor = radiusScaleFactor * (actualRadius / targetRadius);
    }

    // SPLIT converged geometry at original curve boundaries
    var outputCurves = [];
    var segmentStartIdx = 0;

    // Map reference boundaries to new coordinate space
    var mappedBoundaries = [];
    for (var boundaryX in curveBoundaries)
    {
        var newBoundaryX = newFcpX + (boundaryX - refFcpX) * xScale;
        mappedBoundaries = append(mappedBoundaries, newBoundaryX);
    }

    for (var boundaryIdx = 0; boundaryIdx < size(mappedBoundaries); boundaryIdx += 1)
    {
        var boundaryX = mappedBoundaries[boundaryIdx];
        var splitIdx = findClosestIndex(newXSamples, boundaryX);

        // Build segment from start to split point (INCLUSIVE)
        var segmentPoints = [];
        for (var i = segmentStartIdx; i <= splitIdx; i += 1)
        {
            segmentPoints = append(segmentPoints,
                vector(newXSamples[i], finalY[i], 0 * millimeter));
        }

        // Create curve if segment has enough points
        if (size(segmentPoints) >= 2)
        {
            var segmentCurve = approximateSpline(context, {
                "degree" : outputDegree,
                "tolerance" : approxTolerance,
                "maxControlPoints" : maxControlPoints,
                "targets" : [approximationTarget({ "positions" : segmentPoints })],
                "interpolateIndices" : [0, size(segmentPoints) - 1]
            })[0];

            outputCurves = append(outputCurves, segmentCurve);
        }

        // CRITICAL: Share boundary point for G0 continuity
        // Next segment starts at splitIdx (NOT splitIdx + 1)
        segmentStartIdx = splitIdx;
    }

    // Last segment (from last boundary to end)
    var segmentPoints = [];
    for (var i = segmentStartIdx; i < size(newXSamples); i += 1)
    {
        segmentPoints = append(segmentPoints,
            vector(newXSamples[i], finalY[i], 0 * millimeter));
    }

    if (size(segmentPoints) >= 2)
    {
        var segmentCurve = approximateSpline(context, {
            "degree" : outputDegree,
            "tolerance" : approxTolerance,
            "maxControlPoints" : maxControlPoints,
            "targets" : [approximationTarget({ "positions" : segmentPoints })],
            "interpolateIndices" : [0, size(segmentPoints) - 1]
        })[0];

        outputCurves = append(outputCurves, segmentCurve);
    }

    // NOTE: Post-process simplification was tested but caused catastrophic radius error
    // (16.06m vs 21m target). Control point count is less important than accuracy.
    // To reduce CPs, adjust the original approximation parameters instead.

    // OPTIONAL: Convert to strict arcs if requested
    if (strictArcs)
    {
        var arcCurves = forceQuadraticNurbs(context, id + "strictArcs", outputCurves);
        outputCurves = arcCurves;
    }

    // NOTE: To validate final radius accuracy, run analyzeFootprint on the output curves
    // The tighter iteration tolerance (1cm) and improved approximation quality should
    // reduce the radius error from ~0.63m to < 0.1m

    // Get final widths
    var finalFcpWidth = finalY[0];
    var finalAcpWidth = finalY[size(finalY) - 1];
    var finalWaistWidth = minY + y0;

    return {
        "curves" : outputCurves,
        "fcpWidth" : finalFcpWidth,
        "acpWidth" : finalAcpWidth,
        "waistWidth" : finalWaistWidth
    };
}

// =============================================================================
// TIP/TAIL TRANSFORMATION
// =============================================================================

/**
 * Transform tip or tail curves: translate X, scale Y.
 */
function transformTipTail(curves is array, refContactX is ValueWithUnits, newContactX is ValueWithUnits,
    yScale is number, isTip is boolean) returns array
{
    var xTranslation = newContactX - refContactX;
    var transformedCurves = [];
    
    for (var bspline in curves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];
        
        for (var pt in controlPoints)
        {
            var newX = pt[0] + xTranslation;
            var newY = pt[1] * yScale;

            var newPt = vector(newX, newY, pt[2]);
            newControlPoints = append(newControlPoints, newPt);
        }

        // Build parameter map - let bSplineCurve compute knots automatically
        var params = {
            "degree" : bspline.degree,
            "controlPoints" : newControlPoints
        };

        if (bspline.weights != undefined)
            params.weights = bspline.weights;

        if (bspline.isRational != undefined)
            params.isRational = bspline.isRational;

        if (bspline.isPeriodic != undefined)
            params.isPeriodic = bspline.isPeriodic;

        // NOTE: Not passing knots - let bSplineCurve compute them

        transformedCurves = append(transformedCurves, bSplineCurve(params));
    }
    
    return transformedCurves;
}

// =============================================================================
// MIRRORING UTILITY
// =============================================================================

/**
 * Mirror BSpline curves across Y=0 by negating the Y component of
 * every control point. Preserves knots, weights, degree — pure math,
 * no context operation needed.
 */
function mirrorCurvesY(curves is array) returns array
{
    var mirrored = [];

    for (var bspline in curves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];

        for (var pt in controlPoints)
        {
            newControlPoints = append(newControlPoints, vector(pt[0], -pt[1], pt[2]));
        }

        // Build parameter map - let bSplineCurve compute knots automatically
        // (Passing knots directly can fail if they're not in KnotArray format)
        var params = {
            "degree" : bspline.degree,
            "controlPoints" : newControlPoints
        };

        // Only add weights and isRational for rational curves (like arcs)
        if (bspline.weights != undefined)
            params.weights = bspline.weights;

        if (bspline.isRational != undefined)
            params.isRational = bspline.isRational;

        if (bspline.isPeriodic != undefined)
            params.isPeriodic = bspline.isPeriodic;

        // NOTE: Deliberately NOT passing knots - let bSplineCurve compute them
        // This avoids KnotArray format issues with arc-converted curves

        mirrored = append(mirrored, bSplineCurve(params));
    }

    return mirrored;
}

// =============================================================================
// G1 CONTINUITY REPAIR  (minimum-change, control-point only)
// =============================================================================

/**
 * Enforce G1 continuity at a contact-point junction (FCP or ACP) across
 * a fully assembled curve array (tip + sidecut + tail).
 *
 * Identification uses only control point X positions:
 *   - A curve touching the junction whose OTHER endpoint is on the interior
 *     side (X > contactX for FCP, X < contactX for ACP) is the sidecut.
 *   - All other curves touching the junction are tip/tail and get repaired.
 *
 * Repair: for each tip/tail curve, the single adjacent CP is moved so that
 *   cp_adjacent = cp_junction - scDir * dist
 * where scDir is the sidecut unit tangent INTO the interior and dist is the
 * original distance.  Only direction changes — minimum possible modification.
 *
 * @param curves   - assembled BSplineCurve array
 * @param contactX - X coordinate of the junction (newFcp[0] or newAcp[0])
 * @param isTip    - true for FCP junction, false for ACP junction
 * @param tol      - position tolerance for endpoint matching
 */
function fixG1(curves is array, contactX is ValueWithUnits,
    isTip is boolean, tol is ValueWithUnits) returns array
{
    // Tip/tail endpoints are always at contactX (they come from transformTipTail,
    // which is a pure X-translation + Y-scale).  Sidecut endpoints may have moved
    // off contactX after scaleKeepTaper's rotation step, so we locate the junction
    // position from a tip/tail endpoint first, then find the sidecut by 3D proximity.

    // ---- Pass 0: find junction position from a tip/tail curve endpoint ----
    // Tip/tail:   endpoint X == contactX  AND  other endpoint X is on exterior side
    //   FCP (isTip=true):  tip other end has X < contactX
    //   ACP (isTip=false): tail other end has X > contactX
    var junctionPos = undefined;

    for (var idx = 0; idx < size(curves); idx += 1)
    {
        var cps = curves[idx].controlPoints;
        var n   = size(cps);

        var jIdx = -1;
        if (abs(cps[0][0] - contactX) < tol)         jIdx = 0;
        else if (abs(cps[n - 1][0] - contactX) < tol) jIdx = n - 1;
        if (jIdx < 0) continue;

        var otherX = cps[(jIdx == 0) ? n - 1 : 0][0];
        var isEndCurve;
        if (isTip)  isEndCurve = (otherX < contactX);
        else        isEndCurve = (otherX > contactX);
        if (!isEndCurve) continue;

        junctionPos = cps[jIdx];
        break;
    }

    if (junctionPos == undefined) return curves;

    // ---- Pass 1: find sidecut tangent by 3D proximity to junctionPos ----
    // Use a generous spatial tolerance (1 mm) — the sidecut endpoint may have
    // moved after rotation but should still be near the tip/tail endpoint.
    var proximityTol = 1 * millimeter;
    var scDir = undefined;

    for (var idx = 0; idx < size(curves); idx += 1)
    {
        var cps = curves[idx].controlPoints;
        var n   = size(cps);

        var jIdx = -1;
        var aIdx = -1;
        if (norm(cps[0] - junctionPos) < proximityTol)
        {
            jIdx = 0;
            aIdx = 1;
        }
        else if (norm(cps[n - 1] - junctionPos) < proximityTol)
        {
            jIdx = n - 1;
            aIdx = n - 2;
        }
        if (jIdx < 0) continue;

        // Sidecut: other endpoint is on the interior side of the junction.
        // Compare against the actual (possibly rotated) junction endpoint X,
        // not contactX, so this works even after scaleKeepTaper's rotation.
        //   FCP (isTip=true):  sidecut interior → otherX > junction X
        //   ACP (isTip=false): sidecut interior → otherX < junction X
        var otherX = cps[(jIdx == 0) ? n - 1 : 0][0];
        var isSidecut;
        if (isTip)  isSidecut = (otherX > cps[jIdx][0]);
        else        isSidecut = (otherX < cps[jIdx][0]);

        if (!isSidecut) continue;

        // Also verify this curve's junction end is NOT at the tip/tail side
        // (exclude curves whose other end is also near contactX — degenerate case)
        var otherNearContact = abs(cps[(jIdx == 0) ? n - 1 : 0][0] - contactX) < tol;
        if (otherNearContact) continue;

        var delta = cps[aIdx] - cps[jIdx];
        var dLen  = norm(delta);
        if (dLen < tol) continue;

        scDir = normalize(delta);
        if (isTip  && scDir[0] < 0) scDir = -scDir;
        if (!isTip && scDir[0] > 0) scDir = -scDir;
        break;
    }

    if (scDir == undefined) return curves;

    // ---- Pass 2: repair tip/tail curves at the junction ----
    var result = [];

    for (var idx = 0; idx < size(curves); idx += 1)
    {
        var ec  = curves[idx];
        var cps = ec.controlPoints;
        var n   = size(cps);

        var jIdx = -1;
        var aIdx = -1;
        if (abs(cps[0][0] - contactX) < tol)
        {
            jIdx = 0;
            aIdx = 1;
        }
        else if (abs(cps[n - 1][0] - contactX) < tol)
        {
            jIdx = n - 1;
            aIdx = n - 2;
        }

        var needsRepair = false;
        if (jIdx >= 0 && ec.degree >= 2 && n >= 3)
        {
            var otherX = cps[(jIdx == 0) ? n - 1 : 0][0];
            if (isTip)  needsRepair = (otherX < contactX);
            else        needsRepair = (otherX > contactX);
        }

        if (!needsRepair)
        {
            result = append(result, ec);
            continue;
        }

        var jPt    = cps[jIdx];
        var aPt    = cps[aIdx];
        var dist   = norm(aPt - jPt);
        var newAPt = jPt - scDir * dist;

        var newCPs = [];
        for (var i = 0; i < n; i += 1)
        {
            if (i == aIdx)
                newCPs = append(newCPs, newAPt);
            else
                newCPs = append(newCPs, cps[i]);
        }

        var p = {
            "degree"        : ec.degree,
            "controlPoints" : newCPs,
            "knots"         : ec.knots
        };
        if (ec.weights    != undefined) p.weights    = ec.weights;
        if (ec.isRational != undefined) p.isRational = ec.isRational;
        if (ec.isPeriodic != undefined) p.isPeriodic = ec.isPeriodic;

        result = append(result, bSplineCurve(p));
    }

    return result;
}

// =============================================================================
// BSPLINE UTILITIES
// =============================================================================

/**
 * Get parameter range from knot vector.
 */
function getBSplineParamRange(bspline is BSplineCurve) returns map
{
    var knots = bspline.knots;
    return {
        "uMin" : knots[0],
        "uMax" : knots[size(knots) - 1]
    };
}

/**
 * Get bounding box by sampling.
 */
function getBSplineBounds(bspline is BSplineCurve) returns map
{
    var range = getBSplineParamRange(bspline);
    
    var numSamples = 25;
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, range.uMin + (range.uMax - range.uMin) * i / (numSamples - 1));
    }
    
    var result = evaluateSpline({ "spline" : bspline, "parameters" : params });
    var positions = result[0];
    
    var xMin = inf * meter;
    var xMax = -inf * meter;
    var yMin = inf * meter;
    var yMax = -inf * meter;
    
    for (var pt in positions)
    {
        xMin = min([xMin, pt[0]]);
        xMax = max([xMax, pt[0]]);
        yMin = min([yMin, pt[1]]);
        yMax = max([yMax, pt[1]]);
    }
    
    return {
        "xMin" : xMin,
        "xMax" : xMax,
        "yMin" : yMin,
        "yMax" : yMax
    };
}

// NOTE: extractBSplineSubcurve() is imported from fpt_analyze.fs
// NOTE: getBSplineCurvatureAtParam() is imported from footprint_math.fs
