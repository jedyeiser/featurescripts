FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "201c64079ee529cdd6609fe7");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "6ad04ba6be4ab10b452261ce");

// IMPORT: generateBaselineSolver.fs

// IMPORT: analyzeBaseline.fs
import(path : "f0717a1116fee7304957da5b", version : "ab5e058352be20fa5756231b");



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
export const MaxControlPointsBounds = { (unitless) : [4, 50, 500] }    as IntegerBoundSpec;
export const ApproxDegreeBounds     = { (unitless) : [1, 3, 9] }       as IntegerBoundSpec;


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

annotation {
    "Feature Type Name"        : "Generate baseline",
    "Feature Type Description" : "Generates a camber/rocker baseline curve for a ski or snowboard",
    "Editing Logic Function"   : "generateBaselineEditLogic"
}
export const generateBaseline = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Output curve name", "Default" : "Baseline" }
        definition.outputCurveName is string;

        annotation { "Name" : "FCP",
                     "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1 }
        definition.fcpQuery is Query;

        annotation { "Name" : "ACP",
                     "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1 }
        definition.acpQuery is Query;

        annotation { "Name" : "Mount / load point",
                     "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                     "MaxNumberOfPicks" : 1 }
        definition.mountQuery is Query;

        annotation { "Group Name" : "Camber targets", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Camber height (MCH)",
                         "Description" : "Maximum camber height after rocker rotation. 0 = flat ski." }
            isLength(definition.camberHeight, LENGTH_BOUNDS);

            annotation { "Name" : "Forebody rocker length",
                         "Description" : "FCP -> FRCP distance. 0 = no forebody rocker." }
            isLength(definition.frcpl, LENGTH_BOUNDS);

            annotation { "Name" : "Aftbody rocker length",
                         "Description" : "ARCP -> ACP distance. 0 = no aftbody rocker." }
            isLength(definition.arcpl, LENGTH_BOUNDS);

            annotation { "Name" : "FCP height",
                         "Description" : "Normal-distance offset of FCP tip below camber tangent at FRCP." }
            isLength(definition.fcpHeight, LENGTH_BOUNDS);

            annotation { "Name" : "ACP height",
                         "Description" : "Normal-distance offset of ACP tip below camber tangent at ARCP." }
            isLength(definition.acpHeight, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Use EI profile",
                     "Default" : false,
                     "Description" : "When enabled, solve camber pocket using beam bending with the provided EI profile." }
        definition.hasEIProfile is boolean;

        annotation { "Name" : "showEIQuery", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.showEIQuery is boolean;

        if (definition.showEIQuery)
        {
            annotation { "Name" : "EI profile edges",
                         "Filter" : EntityType.EDGE,
                         "Description" : "World Z in mm = EI in Nm^2." }
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

    }
    {
        // ----------------------------------------------------------------
        // 1. Resolve reference X coordinates
        // ----------------------------------------------------------------
        var dummyEdge = qNothing();
        var xFCP   = resolveReferencePointX(context, definition.fcpQuery,   dummyEdge);
        var xACP   = resolveReferencePointX(context, definition.acpQuery,   dummyEdge);
        var xMount = resolveReferencePointX(context, definition.mountQuery,  dummyEdge);

        if (xFCP == undefined || xACP == undefined || xMount == undefined)
        {
            throw regenError("Could not resolve FCP, ACP, or mount point.");
        }
        if (xFCP >= xACP)
        {
            throw regenError("FCP must have a smaller X coordinate than ACP.");
        }
        if (xMount < xFCP || xMount > xACP)
        {
            throw regenError("Mount / load point must lie between FCP and ACP.");
        }

        // ----------------------------------------------------------------
        // 2. Derive FRCP and ARCP
        // ----------------------------------------------------------------
        var xFRCP = xFCP + definition.frcpl;
        var xARCP = xACP - definition.arcpl;

        if (xFRCP >= xARCP)
        {
            throw regenError("Rocker lengths are too large — FRCP must be less than ARCP.");
        }


        // ----------------------------------------------------------------
        // 3. Load EI data
        // ----------------------------------------------------------------
        var eiData = [];
        var hasEI  = false;

        if (definition.hasEIProfile && definition.showEIQuery)
        {
            var edgeCount = size(evaluateQuery(context, definition.eiEdgesQuery));
            if (edgeCount > 0)
            {
                eiData = getEIFromEdges(context, definition.eiEdgesQuery, xFCP, xACP);
                hasEI  = (size(eiData) >= 2);
            }
        }

        // Needed by steps 4, 5, and 6
        var hasForeRocker = definition.frcpl > 0 * meter;
        var hasAftRocker  = definition.arcpl  > 0 * meter;

        // ----------------------------------------------------------------
        // 4-5. MCH bisection + FCPH / ACPH convergence
        //
        // runMCHBisection converges the camber height to MCH_target and
        // applies the global Z-shift.  It accepts fcpH/acpH as VERTICAL
        // tip-height targets (Z above snow after the shift).
        //
        // The FCPH/ACPH loop corrects those effective vertical targets so
        // that the perpendicular distances from the FCP/ACP tips to the
        // FRCP/ARCP tangent lines match the user's spec inputs.
        //
        // Derivation of the perpendicular distance (2D cross product):
        //
        //   Forebody:
        //     FRCP→FCP = (xFCP−xFRCP, Z_FCP−Z_FRCP) = (−frcpl_m, Z_FCP−Z_FRCP)
        //     tangent  = (1, mFore) / ||...||   (mFore > 0, camber rises right)
        //     dist     = |−frcpl_m·mFore − (Z_FCP−Z_FRCP)| / √(1+mFore²)
        //              = (frcpl_m·mFore + Z_FCP − Z_FRCP) / √(1+mFore²)
        //
        //   Aftbody:
        //     ARCP→ACP = (xACP−xARCP, Z_ACP−Z_ARCP) = (+arcpl_m, Z_ACP−Z_ARCP)
        //     tangent  = (1, mAft) / ||...||    (mAft < 0, camber descends right)
        //     dist     = |arcpl_m·mAft − (Z_ACP−Z_ARCP)| / √(1+mAft²)
        //
        // Newton step (relationship is linear in the vertical tip height):
        //   fcpHeightEff_new = fcpHeightEff − err_fcph · √(1+mFore²)
        //   acpHeightEff_new = acpHeightEff − err_acph · √(1+mAft²)
        //
        // Converges in 1-2 iterations for typical rocker angles.
        // ----------------------------------------------------------------
        var FCH_TOL      = 5e-7;   // 0.5 µm in plain meters (< 0.001 mm)
        var fcpHeightEff = definition.fcpHeight;
        var acpHeightEff = definition.acpHeight;
        var MCH_m        = definition.camberHeight / meter;

        var mchResult = runMCHBisection(context, eiData, hasEI,
                                         xFCP, xACP, xFRCP, xARCP, xMount,
                                         fcpHeightEff, acpHeightEff,
                                         definition.frcpl, definition.arcpl,
                                         hasForeRocker, hasAftRocker, MCH_m);
        var finalPts = mchResult.finalPts;

        for (var rocIter = 0; rocIter < 8; rocIter += 1)
        {
            // Bucket finalPts exactly as step 6 does, capturing tip points and
            // the camber subset.  Then fit the same approximateSpline so that the
            // FCPH/ACPH measurement uses the exact camberStartPt / camberStartTan
            // / camberEndPt / camberEndTan that the Bézier rockers will use.
            // This eliminates the systematic offset that arises when measuring
            // slope from the raw solver array vs. the fitted spline endpoint.
            var camberPtsLoop = [];
            var fTipPtLoop    = undefined;
            var aTipPtLoop    = undefined;
            var GEOM_TOL_BKT  = 1e-9 * meter;

            for (var pt in finalPts)
            {
                var inFore = hasForeRocker && (pt.x < xFRCP - GEOM_TOL_BKT);
                var inAft  = hasAftRocker  && (pt.x > xARCP + GEOM_TOL_BKT);
                if (inFore)
                {
                    if (fTipPtLoop == undefined || pt.x < fTipPtLoop.x)
                    {
                        fTipPtLoop = pt;
                    }
                }
                else if (inAft)
                {
                    if (aTipPtLoop == undefined || pt.x > aTipPtLoop.x)
                    {
                        aTipPtLoop = pt;
                    }
                }
                else
                {
                    camberPtsLoop = append(camberPtsLoop, vector(pt.x, 0 * meter, pt.z));
                }
            }

            var fcph_err   = 0.0;
            var acph_err   = 0.0;

            if (size(camberPtsLoop) >= 2)
            {
                var approxLoop = approximateSpline(context, {
                    "degree"           : definition.curveDegree,
                    "tolerance"        : definition.approxTolerance,
                    "isPeriodic"       : false,
                    "targets"          : [{ "positions" : camberPtsLoop }],
                    "maxControlPoints" : definition.maxControlPoints
                });

                var loopSpline = approxLoop[0];
                var nKnots     = size(loopSpline.knots);
                var uStart     = loopSpline.knots[loopSpline.degree];
                var uEnd       = loopSpline.knots[nKnots - 1 - loopSpline.degree];

                var startDerivs = evaluateSpline({
                    "spline"       : loopSpline,
                    "parameters"   : [uStart],
                    "nDerivatives" : 1
                });
                var endDerivs = evaluateSpline({
                    "spline"       : loopSpline,
                    "parameters"   : [uEnd],
                    "nDerivatives" : 1
                });

                var splineStartPt = startDerivs[0][0];  // 3D position at spline start
                var splineStartD1 = startDerivs[1][0];  // first derivative at start
                var splineEndPt   = endDerivs[0][0];    // 3D position at spline end
                var splineEndD1   = endDerivs[1][0];    // first derivative at end

                if (hasForeRocker && fTipPtLoop != undefined)
                {
                    var d1NormFore = norm(splineStartD1);
                    if (d1NormFore > 1e-12 * meter)
                    {
                        // Unit tangent at camber start (same as camberStartTan in step 6)
                        var frcp_dx = splineStartD1[0] / d1NormFore;  // unitless
                        var frcp_dz = splineStartD1[2] / d1NormFore;  // unitless

                        // Perpendicular distance from FCP tip to FRCP tangent line
                        var vec_x  = (fTipPtLoop.x - splineStartPt[0]) / meter;
                        var vec_z  = (fTipPtLoop.z - splineStartPt[2]) / meter;
                        var fcph_m = abs(vec_x * frcp_dz - vec_z * frcp_dx);
                        fcph_err   = fcph_m - definition.fcpHeight / meter;
                        if (abs(fcph_err) > FCH_TOL)
                        {
                            // Newton: d(FCPH)/d(Z_FCP) = frcp_dx  =>  step = err / frcp_dx
                            fcpHeightEff = fcpHeightEff - (fcph_err / frcp_dx) * meter;
                        }
                    }
                }

                if (hasAftRocker && aTipPtLoop != undefined)
                {
                    var d1NormAft = norm(splineEndD1);
                    if (d1NormAft > 1e-12 * meter)
                    {
                        // Unit tangent at camber end (same as camberEndTan in step 6)
                        var arcp_dx = splineEndD1[0] / d1NormAft;  // unitless
                        var arcp_dz = splineEndD1[2] / d1NormAft;  // unitless

                        // Perpendicular distance from ACP tip to ARCP tangent line
                        var vec_x2  = (aTipPtLoop.x - splineEndPt[0]) / meter;
                        var vec_z2  = (aTipPtLoop.z - splineEndPt[2]) / meter;
                        var acph_m  = abs(vec_x2 * arcp_dz - vec_z2 * arcp_dx);
                        acph_err    = acph_m - definition.acpHeight / meter;
                        if (abs(acph_err) > FCH_TOL)
                        {
                            // Newton: d(ACPH)/d(Z_ACP) = arcp_dx  =>  step = err / arcp_dx
                            acpHeightEff = acpHeightEff - (acph_err / arcp_dx) * meter;
                        }
                    }
                }

            }

            if (abs(fcph_err) < FCH_TOL && abs(acph_err) < FCH_TOL)
            {
                break;
            }

            mchResult = runMCHBisection(context, eiData, hasEI,
                                         xFCP, xACP, xFRCP, xARCP, xMount,
                                         fcpHeightEff, acpHeightEff,
                                         definition.frcpl, definition.arcpl,
                                         hasForeRocker, hasAftRocker, MCH_m);
            finalPts = mchResult.finalPts;
        }


        // ----------------------------------------------------------------
        // 6. Fit camber spline separately, then build exact G1 Bézier
        //    rockers tangent to the fitted camber at the junction points.
        //
        //    Rationale: fitting one big spline over all ~300 points then
        //    splitting near FRCP/ARCP shares the approximation budget across
        //    the whole span and, when an EI profile is used, the k=0 clamp
        //    near the EI profile endpoints causes a curvature kink that the
        //    spline fitter absorbs by introducing a spurious inflection in
        //    the camber pocket.  Fitting the camber alone dedicates the full
        //    control-point budget to the camber shape.  The rockers become
        //    exact degree-2 Béziers whose G1 tangent at the junction is
        //    read directly from the fitted camber endpoint.
        // ----------------------------------------------------------------

        try
        {
            // Bucket finalPts into camber vs rocker regions.
            // Camber:     xFRCP ≤ x ≤ xARCP  (includes both junction points)
            // Fore rocker: x < xFRCP          (FCP tip side)
            // Aft rocker:  x > xARCP          (ACP tip side)
            var camberPts     = [];
            var foreRockerPts = [];
            var aftRockerPts  = [];
            var GEOM_TOL_BKT  = 1e-9 * meter;

            for (var pt in finalPts)
            {
                var inFore = hasForeRocker && (pt.x < xFRCP - GEOM_TOL_BKT);
                var inAft  = hasAftRocker  && (pt.x > xARCP + GEOM_TOL_BKT);
                if (inFore)
                {
                    foreRockerPts = append(foreRockerPts, vector(pt.x, 0 * meter, pt.z));
                }
                else if (inAft)
                {
                    aftRockerPts = append(aftRockerPts, vector(pt.x, 0 * meter, pt.z));
                }
                else
                {
                    camberPts = append(camberPts, vector(pt.x, 0 * meter, pt.z));
                }
            }

            if (size(camberPts) < 2)
            {
                throw regenError("Baseline solver produced insufficient camber points.");
            }

            // --- Fit the camber spline (full approximation budget) ---
            var approxCamber = approximateSpline(context, {
                "degree"           : definition.curveDegree,
                "tolerance"        : definition.approxTolerance,
                "isPeriodic"       : false,
                "targets"          : [{ "positions" : camberPts }],
                "maxControlPoints" : definition.maxControlPoints
            });

            opCreateBSplineCurve(context, id + "camber", {
                "bSplineCurve" : approxCamber[0]
            });

            var camberEdges = evaluateQuery(context, qCreatedBy(id + "camber", EntityType.EDGE));
            if (size(camberEdges) == 0)
            {
                throw regenError("Baseline: camber spline body produced no edges.");
            }
            var camberEdge = camberEdges[0];

            // Track which rocker bodies were actually created.
            var hasForeBody = false;
            var hasAftBody  = false;

            // --- Build G1 Bézier forebody rocker ---
            // Degree-2 Bézier: P0 = FCP tip, P1 = G1 control, P2 = fitted camber start.
            //
            //   G1 at P2: Bézier tangent at t=1 = 2*(P2-P1) ∝ camberStartTan
            //   => P1 = P2 - (α/2)*camberStartTan
            //      α  = (P2.x - P0.x) / camberStartTan.x
            //   => P1.x = midpoint(P0.x, P2.x)
            if (hasForeRocker && size(foreRockerPts) >= 1)
            {
                var camberStartCurv = evEdgeCurvatures(context, { "edge" : camberEdge, "parameters" : [0.0] });
                var camberStartPt   = camberStartCurv[0].frame.origin;
                var camberStartTan  = camberStartCurv[0].frame.zAxis;  // normalized tangent, points FCP→ACP

                var fTipPt = foreRockerPts[0];  // lowest-X point = FCP tip
                var dxFore = camberStartPt[0] - fTipPt[0];

                if (abs(camberStartTan[0]) > 0.01)
                {
                    var alphaFore = dxFore / camberStartTan[0];
                    var P1fore = vector(
                        camberStartPt[0] - (alphaFore / 2) * camberStartTan[0],
                        0 * meter,
                        camberStartPt[2] - (alphaFore / 2) * camberStartTan[2]
                    );
                    opCreateBSplineCurve(context, id + "forebody", {
                        "bSplineCurve" : bSplineCurve({
                            "degree"        : 2,
                            "isPeriodic"    : false,
                            "controlPoints" : [fTipPt, P1fore, camberStartPt]
                        })
                    });
                    hasForeBody = true;
                }
                else
                {
                    // Near-vertical junction tangent: approximate through fore points.
                    var forePoints = append(foreRockerPts, camberStartPt);
                    var approxFore = approximateSpline(context, {
                        "degree"           : definition.curveDegree,
                        "tolerance"        : definition.approxTolerance,
                        "isPeriodic"       : false,
                        "targets"          : [{ "positions" : forePoints }],
                        "maxControlPoints" : definition.maxControlPoints
                    });
                    opCreateBSplineCurve(context, id + "forebody", {
                        "bSplineCurve" : approxFore[0]
                    });
                    hasForeBody = true;
                }
            }

            // --- Build G1 Bézier aftbody rocker ---
            // Degree-2 Bézier: P0 = fitted camber end, P1 = G1 control, P2 = ACP tip.
            //
            //   G1 at P0: Bézier tangent at t=0 = 2*(P1-P0) ∝ camberEndTan
            //   => P1 = P0 + (β/2)*camberEndTan
            //      β  = (P2.x - P0.x) / camberEndTan.x
            //   => P1.x = midpoint(P0.x, P2.x)
            if (hasAftRocker && size(aftRockerPts) >= 1)
            {
                var camberEndCurv = evEdgeCurvatures(context, { "edge" : camberEdge, "parameters" : [1.0] });
                var camberEndPt   = camberEndCurv[0].frame.origin;
                var camberEndTan  = camberEndCurv[0].frame.zAxis;  // normalized tangent, points FCP→ACP

                var aTipPt = aftRockerPts[size(aftRockerPts) - 1];  // highest-X point = ACP tip
                var dxAft  = aTipPt[0] - camberEndPt[0];

                if (abs(camberEndTan[0]) > 0.01)
                {
                    var betaAft = dxAft / camberEndTan[0];
                    var P1aft = vector(
                        camberEndPt[0] + (betaAft / 2) * camberEndTan[0],
                        0 * meter,
                        camberEndPt[2] + (betaAft / 2) * camberEndTan[2]
                    );
                    opCreateBSplineCurve(context, id + "aftbody", {
                        "bSplineCurve" : bSplineCurve({
                            "degree"        : 2,
                            "isPeriodic"    : false,
                            "controlPoints" : [camberEndPt, P1aft, aTipPt]
                        })
                    });
                    hasAftBody = true;
                }
                else
                {
                    // Near-vertical junction tangent: approximate through aft points.
                    var aftPoints = concatenateArrays([[camberEndPt], aftRockerPts]);
                    var approxAft = approximateSpline(context, {
                        "degree"           : definition.curveDegree,
                        "tolerance"        : definition.approxTolerance,
                        "isPeriodic"       : false,
                        "targets"          : [{ "positions" : aftPoints }],
                        "maxControlPoints" : definition.maxControlPoints
                    });
                    opCreateBSplineCurve(context, id + "aftbody", {
                        "bSplineCurve" : approxAft[0]
                    });
                    hasAftBody = true;
                }
            }

            // --- Merge all segment edges into a single wire body ---
            var allEdgeQueries = [qCreatedBy(id + "camber", EntityType.EDGE)];
            if (hasForeBody)
            {
                allEdgeQueries = append(allEdgeQueries, qCreatedBy(id + "forebody", EntityType.EDGE));
            }
            if (hasAftBody)
            {
                allEdgeQueries = append(allEdgeQueries, qCreatedBy(id + "aftbody", EntityType.EDGE));
            }

            opExtractWires(context, id + "baseline", {
                "edges" : qUnion(allEdgeQueries)
            });

            // Delete the now-redundant segment bodies.
            var segBodyQueries = [qCreatedBy(id + "camber", EntityType.BODY)];
            if (hasForeBody)
            {
                segBodyQueries = append(segBodyQueries, qCreatedBy(id + "forebody", EntityType.BODY));
            }
            if (hasAftBody)
            {
                segBodyQueries = append(segBodyQueries, qCreatedBy(id + "aftbody", EntityType.BODY));
            }
            opDeleteBodies(context, id + "deleteSegments", {
                "entities" : qUnion(segBodyQueries)
            });

            // Name the wire body if a name was provided.
            if (definition.outputCurveName != "")
            {
                var wireBody = evaluateQuery(context, qCreatedBy(id + "baseline", EntityType.BODY));
                if (size(wireBody) > 0)
                {
                    setProperty(context, {
                        "entities"     : wireBody[0],
                        "propertyType" : PropertyType.NAME,
                        "value"        : definition.outputCurveName
                    });
                }
            }

            // Baseline sketch — analyze the generated curve and output measurement geometry
            if (definition.addBaselineSketch)
            {
                var baselineEdges = qCreatedBy(id + "baseline", EntityType.EDGE);
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
        }
        catch (e)
        {
            throw regenError("Baseline spline fitting failed — see console output.");
        }
    });
