FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "d4ef98b7b0acf40b1998b4ed");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "7e4fdcd1cd16322867bb23fc");

// IMPORT: generateBaselineSolver.fs
import(path : "649902142758d832c018a0be", version : "bf935e5ab4c5f802d1acf5fb");

// IMPORT: analyzeBaseline.fs
import(path : "f0717a1116fee7304957da5b", version : "5b40b4a82d143f10befe8c4e");



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
        // 4-5. Solve baseline geometry
        // ----------------------------------------------------------------
        var MCH_m    = definition.camberHeight / meter;
        var finalPts = buildBaseline(context, eiData, hasEI,
                                     xFCP, xACP, xFRCP, xARCP, xMount,
                                     definition.fcpHeight, definition.acpHeight,
                                     definition.frcpl, definition.arcpl,
                                     MCH_m);


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
