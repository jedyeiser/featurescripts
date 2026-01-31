FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// Import constants and predicates

import(path : "a54a829744c4e15e8da55e0e", version : "5a283e0298e3adfbca7a9655");

import(path : "ba0d9a5428fa1db483099bce", version : "44f7eccf75fa18fba43bd3eb");

// Import math utilities
import(path : "b4b27eddd41251b5f56f042b", version : "5ddb5d06cb8b643cd2c5a5a3");
// Import geometry utilities (fpt_analyze)
export import(path : "d1b04ca2346787da6083d7cc", version : "57cafe87c4ce8aed9693754b");
// Import geometry utilities (fpt_geometry)


IconNamespace::import(path : "b550d23fd28bf02e0bca618d", version : "7a66bc39e006a0d82e0d0e4b");



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
 * Width targeting:
 *   - ACCORDION: scales Y uniformly to hit target waist width
 *   - KEEP_TAPER / SCALE_RADIUS: shifts Y to hit target waist width
 *
 * Tip/tail curves are translated and Y-scaled to match new contact widths.
 * Optional G1 continuity repair at contact points.
 * =============================================================================
 */

// =============================================================================
// ENUMS
// =============================================================================

// =============================================================================
// FEATURE DEFINITION
// =============================================================================

annotation { "Feature Type Name" : "Scale Footprint", "Feature Type Description" : "Takes input and output curves/RSL lines and creates an updated footprint", "Icon" : IconNamespace::BLOB_DATA
}
export const scaleFootprint = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Reference footprint edges", "Filter" : EntityType.EDGE }
        definition.refEdges is Query;

        annotation { "Name" : "Reference RSL line", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.refRslEdge is Query;

        annotation { "Name" : "New RSL line", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.newRslEdge is Query;

        annotation { "Name" : "Scale mode" }
        definition.scaleMode is FootprintScaleMode;

        // Pin point selection for KEEP_TAPER and SCALE_RADIUS
        if (definition.scaleMode == FootprintScaleMode.KEEP_TAPER || definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
        {
            annotation { "Name" : "Pin point", "Filter" : EntityType.VERTEX || EntityType.FACE && GeometryType.PLANE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.pinPoint is Query;
        }

        // Target radius only for SCALE_RADIUS
        if (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
        {
            annotation { "Name" : "Target average radius" }
            isLength(definition.targetRadius, ScaleRadiusBounds);
        }

        // Optional target width for all modes
        annotation { "Name" : "Specify target width", "Default" : false }
        definition.specifyWidth is boolean;

        if (definition.specifyWidth)
        {
            annotation { "Name" : "Target waist width" }
            isLength(definition.targetWaistWidth, ScaleWaistBounds);
        }

        annotation { "Name" : "Enforce tip G1 continuity", "Default" : false }
        definition.repairTipContinuity is boolean;

        if (definition.repairTipContinuity)
        {
            annotation { "Name" : "Repair type", "Default" : ContinuityMassageMode.MINIMUM }
            definition.tipRepairType is ContinuityMassageMode;
        }

        annotation { "Name" : "Enforce tail G1 continuity", "Default" : false }
        definition.repairTailContinuity is boolean;

        if (definition.repairTailContinuity)
        {
            annotation { "Name" : "Repair type", "Default" : ContinuityMassageMode.MINIMUM }
            definition.tailRepairType is ContinuityMassageMode;
        }

        annotation { "Group Name" : "Details", "Collapsed By Default" : true }
        {

            annotation { "Name" : "Build Mode", "Default": FootprintCurveBuildMode.ONE_PER_REGION, "Description": "Specifies if we should build one curve per region (taper*, sidecut, taper*) or if we should build one spline per edge in our input query. gaps in X will be interpolated", "UIHint": UIHint.SHOW_LABEL }
            definition.footprintCurveBuildMode is FootprintCurveBuildMode;

            annotation { "Name" : "Unify curves", "Default": false, "Description" : "When true, outputs a single curve rather than multiple curves, no matter what is selected for build mode" }
            definition.unifyCurves is boolean;

            annotation { "Name" : "Strict " ,"Description": "When true, strictly enforces ouput BSplines to be a rational quadratic NURBS arcs or lines" }
            definition.strict is boolean;
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


        annotation { "Name" : "Keep reference curves", "Default" : true, "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.keepReference is boolean;
    }
    {
        // =====================================================================
        // STEP 1: Extract reference RSL data
        // =====================================================================
        var refRslData = extractRslData(context, definition.refRslEdge);
        var refFcp = refRslData.fcp;
        var refAcp = refRslData.acp;
        var refMrs = refRslData.mrs;
        var refLength = abs(refAcp[0] - refFcp[0]);

        println("Reference RSL: FCP=" ~ toString(refFcp) ~ ", ACP=" ~ toString(refAcp) ~ ", MRS=" ~ toString(refMrs));
        println("Reference length: " ~ toString(refLength));

        // =====================================================================
        // STEP 2: Extract new RSL data
        // =====================================================================
        var newRslData = extractRslData(context, definition.newRslEdge);
        var newFcp = newRslData.fcp;
        var newAcp = newRslData.acp;
        var newMrs = newRslData.mrs;
        var newLength = abs(newAcp[0] - newFcp[0]);

        println("New RSL: FCP=" ~ toString(newFcp) ~ ", ACP=" ~ toString(newAcp) ~ ", MRS=" ~ toString(newMrs));
        println("New length: " ~ toString(newLength));

        var xScaleFactor = newLength / refLength;
        println("X scale factor: " ~ toString(xScaleFactor));

        // =====================================================================
        // STEP 3: Convert reference edges to BSplines, filter to positive Y
        // =====================================================================
        var tolerance = DEFAULT_ANALYSIS_TOLERANCE;
        var allBsplines = edgesToBSplines(context, definition.refEdges, tolerance);

        println("Input: " ~ size(allBsplines) ~ " BSplines from edges");

        // Filter to positive Y only (split at Y=0 if needed)
        var bsplines = filterAndTrimBSplines(allBsplines, 0 * meter, tolerance);

        println("After Y>0 filter: " ~ size(bsplines) ~ " BSplines");

        // Categorize curves: tip (x < FCP), sidecut (FCP <= x <= ACP), tail (x > ACP)
        var categorized = categorizeCurves(context, bsplines, refFcp[0], refAcp[0], tolerance);

        println("Categorized: " ~ size(categorized.tipCurves) ~ " tip, " ~
                size(categorized.sidecutCurves) ~ " sidecut, " ~
                size(categorized.tailCurves) ~ " tail");

        // =====================================================================
        // STEP 4: Analyze reference sidecut
        // =====================================================================
        var refAnalysis = analyzeReferenceSidecut(categorized.sidecutCurves,
            refFcp[0], refAcp[0], tolerance);

        println("Reference analysis:");
        println("  Waist: width=" ~ toString(refAnalysis.waistWidth) ~ " at x=" ~ toString(refAnalysis.waistX));
        println("  FCP width=" ~ toString(refAnalysis.fcpWidth) ~ ", ACP width=" ~ toString(refAnalysis.acpWidth));
        println("  Taper angle=" ~ toString(refAnalysis.taperAngle));
        println("  Avg radius=" ~ toString(refAnalysis.avgRadius));

        println("  Inflections found: " ~ refAnalysis.foundInflections ~
        ", FCP inflection X=" ~ toString(refAnalysis.inflectionFcpX) ~
        ", ACP inflection X=" ~ toString(refAnalysis.inflectionAcpX));

        // =====================================================================
        // STEP 5: Extract pin X (for KEEP_TAPER and SCALE_RADIUS)
        // =====================================================================
        var refPinX = undefined;
        var newPinX = undefined;
        var refPinWidth = undefined;

        if (definition.scaleMode == FootprintScaleMode.KEEP_TAPER ||
            definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
        {
            refPinX = extractPinX(context, definition.pinPoint);

            // Scale pin X proportionally to new RSL
            var xScale = newLength / refLength;
            newPinX = newFcp[0] + (refPinX - refFcp[0]) * xScale;

            // Get width at pin location from reference
            refPinWidth = getWidthAtX(refAnalysis.curveData, refPinX, tolerance);

            println("Pin location: refX=" ~ toString(refPinX) ~ ", newX=" ~ toString(newPinX) ~
                    ", refWidth=" ~ toString(refPinWidth));
        }

        // =====================================================================
        // STEP 6: Scale sidecut curves based on mode
        // =====================================================================
        var buildOptions = {
            "buildMode" : definition.footprintCurveBuildMode,
            "unifyCurves" : definition.unifyCurves,
            "strict" : definition.strict,
            "targetDegree" : definition.targetDegree,
            "maxCPs" : definition.maxCPs,
            "approximationTolerance" : definition.approximationTolerance
        };

        var scaledResult;

        if (definition.scaleMode == FootprintScaleMode.ACCORDION)
        {
            scaledResult = scaleAccordion(context, categorized.sidecutCurves, refAnalysis,
                refFcp[0], refAcp[0], newFcp[0], newAcp[0],
                definition.specifyWidth,
                definition.specifyWidth ? definition.targetWaistWidth : refAnalysis.waistWidth,
                tolerance, buildOptions);
        }
        else if (definition.scaleMode == FootprintScaleMode.KEEP_TAPER)
        {
            scaledResult = scaleKeepTaper(context, categorized.sidecutCurves, refAnalysis,
                refFcp[0], refAcp[0],
                newFcp[0], newAcp[0],
                refPinX, newPinX, refPinWidth,
                definition.specifyWidth,
                definition.specifyWidth ? definition.targetWaistWidth : refAnalysis.waistWidth,
                tolerance, buildOptions);
        }
        else if (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
        {
            scaledResult = scaleRadius(context, categorized.sidecutCurves, refAnalysis,
                refFcp[0], refAcp[0],
                newFcp[0], newAcp[0],
                refPinX, newPinX, refPinWidth,
                definition.targetRadius,
                definition.specifyWidth,
                definition.specifyWidth ? definition.targetWaistWidth : refAnalysis.waistWidth,
                tolerance, buildOptions);
        }

        println("Scaled result:");
        println("  FCP width=" ~ toString(scaledResult.fcpWidth) ~ ", ACP width=" ~ toString(scaledResult.acpWidth));
        println("  Waist width=" ~ toString(scaledResult.waistWidth));

        // =====================================================================
        // STEP 7: Transform tip/tail curves
        // =====================================================================
        var tipYScale = (abs(refAnalysis.fcpWidth) > tolerance) ?
            (scaledResult.fcpWidth / refAnalysis.fcpWidth) : 1;
        var tailYScale = (abs(refAnalysis.acpWidth) > tolerance) ?
            (scaledResult.acpWidth / refAnalysis.acpWidth) : 1;

        println("Tip/Tail transform:");
        println("  Input tip curves: " ~ size(categorized.tipCurves));
        println("  Input tail curves: " ~ size(categorized.tailCurves));
        println("  Tip Y scale: " ~ toString(tipYScale) ~ ", Tail Y scale: " ~ toString(tailYScale));

        var transformedTip = transformTipTail(categorized.tipCurves,
            refFcp[0], newFcp[0], tipYScale, true);
        var transformedTail = transformTipTail(categorized.tailCurves,
            refAcp[0], newAcp[0], tailYScale, false);

        println("  Output tip curves: " ~ size(transformedTip));
        println("  Output tail curves: " ~ size(transformedTail));

        // =====================================================================
        // STEP 8: Repair G1 continuity at junctions
        // =====================================================================
        var finalSidecutCurves = scaledResult.curves;

        if (definition.repairTipContinuity && size(transformedTip) > 0)
        {
            var repairResult = repairContinuityAtContact(
                transformedTip, finalSidecutCurves,
                newFcp[0], true, definition.tipRepairType, tolerance);
            transformedTip = repairResult.tipTailCurves;
            finalSidecutCurves = repairResult.sidecutCurves;
            println("Tip continuity repaired using mode: " ~ definition.tipRepairType);
        }

        if (definition.repairTailContinuity && size(transformedTail) > 0)
        {
            var repairResult = repairContinuityAtContact(
                transformedTail, finalSidecutCurves,
                newAcp[0], false, definition.tailRepairType, tolerance);
            transformedTail = repairResult.tipTailCurves;
            finalSidecutCurves = repairResult.sidecutCurves;
            println("Tail continuity repaired using mode: " ~ definition.tailRepairType);
        }

        // =====================================================================
        // STEP 9: Create output curves (positive Y side only for now)
        // =====================================================================
        var allScaledCurves = concatenateArrays([transformedTip, finalSidecutCurves, transformedTail]);

        println("Output: " ~ size(transformedTip) ~ " tip + " ~
                size(finalSidecutCurves) ~ " sidecut + " ~
                size(transformedTail) ~ " tail = " ~ size(allScaledCurves) ~ " total curves (Y>0 only)");

        for (var i = 0; i < size(allScaledCurves); i += 1)
        {
            opCreateBSplineCurve(context, id + ("curve_" ~ i), {
                "bSplineCurve" : allScaledCurves[i]
            });
        }
    });

// =============================================================================
// RSL DATA EXTRACTION
// =============================================================================

/**
 * Extract FCP, ACP, MRS from an RSL edge.
 * Assumes RSL is roughly horizontal, FCP is at more negative X.
 */
function extractRslData(context is Context, rslEdge is Query) returns map
{
    var endpoints = evEdgeTangentLines(context, {
        "edge" : rslEdge,
        "parameters" : [0, 0.5, 1]
    });

    var p0 = endpoints[0].origin;
    var pMid = endpoints[1].origin;
    var p1 = endpoints[2].origin;

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

    var mrs = pMid;

    return {
        "fcp" : fcp,
        "acp" : acp,
        "mrs" : mrs
    };
}

/**
 * Extract X coordinate from pin geometry (vertex, mate connector, or planar face).
 */
function extractPinX(context is Context, pinQuery is Query) returns ValueWithUnits
{
    // Try vertex first
    try silent
    {
        var pt = evVertexPoint(context, { "vertex" : pinQuery });
        return pt[0];
    }

    // Try mate connector
    try silent
    {
        var csys = evMateConnector(context, { "mateConnector" : pinQuery });
        return csys.origin[0];
    }

    // Try planar face (use plane origin)
    try silent
    {
        var plane = evPlane(context, { "face" : pinQuery });
        return plane.origin[0];
    }

    throw regenError("Could not extract X coordinate from pin selection");
}

// =============================================================================
// Y-AXIS FILTERING AND MIRRORING
// =============================================================================

/**
 * Mirror an array of BSpline curves across the Y=0 axis (negate Y coordinates).
 */
function mirrorCurvesAcrossY(curves is array) returns array
{
    var mirrored = [];

    for (var bspline in curves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];

        for (var pt in controlPoints)
        {
            // Negate Y coordinate
            var mirroredPt = vector(pt[0], -pt[1], pt[2]);
            newControlPoints = append(newControlPoints, mirroredPt);
        }

        var mirroredBSpline = bSplineCurve({
            "degree" : bspline.degree,
            "isPeriodic" : bspline.isPeriodic,
            "controlPoints" : newControlPoints,
            "knots" : bspline.knots,
            "weights" : bspline.weights
        });

        mirrored = append(mirrored, mirroredBSpline);
    }

    return mirrored;
}

// =============================================================================
// REFERENCE SIDECUT ANALYSIS
// =============================================================================

/**
 * Analyze reference sidecut curves to extract key dimensions.
 */
function analyzeReferenceSidecut(sidecutCurves is array, fcpX is ValueWithUnits, acpX is ValueWithUnits,
    tolerance is ValueWithUnits) returns map
{
    // Build curve data for sampling (uses exported function from fpt_analyze)
    var curveData = buildCurveDataArray(sidecutCurves);

    // Get widths at key X locations
    var fcpWidth = getWidthAtX(curveData, fcpX, tolerance);
    var acpWidth = getWidthAtX(curveData, acpX, tolerance);

    // Find waist (minimum width) between FCP and ACP
    var config = buildConfig({ "xTolerance" : tolerance });
    var waistResult = findWaistPoint(curveData, fcpX, acpX, config);

    // Compute taper angle from widths at FCP and ACP
    var taperAngle = atan2(fcpWidth - acpWidth, acpX - fcpX);

    // Compute average radius between inflection points (or full range if no inflections)
    var avgRadiusResult = computeAvgRadiusInRange(curveData, fcpX, acpX, tolerance);

    return {
        "fcpWidth" : fcpWidth,
        "acpWidth" : acpWidth,
        "waistWidth" : waistResult.width,
        "waistX" : waistResult.x,
        "taperAngle" : taperAngle,
        "avgRadius" : avgRadiusResult.avgRadius,
        "inflectionFcpX" : avgRadiusResult.inflectionFcpX,
        "inflectionAcpX" : avgRadiusResult.inflectionAcpX,
        "curveData" : curveData
    };
}

/**
 * Get Y value (width) at a specific X coordinate.
 * Uses shared analysis functions from fpt_analyze.fs.
 */
function getWidthAtX(curveData is array, targetX is ValueWithUnits, tolerance is ValueWithUnits) returns ValueWithUnits
{
    for (var cd in curveData)
    {
        if (cd.xMin <= targetX + tolerance && cd.xMax >= targetX - tolerance)
        {
            var param = findParameterAtX(cd.bspline, targetX, tolerance);
            var pt = evaluateSpline({ "spline" : cd.bspline, "parameters" : [param] })[0][0];
            return pt[1];
        }
    }
    return 0 * millimeter;
}

/**
 * Get the actual Y values (widths) at the FCP and ACP ends of sidecut curves.
 * This is more robust than querying at a fixed X coordinate, because curve
 * endpoints may shift slightly after rotation or other transformations.
 *
 * Finds the curve endpoints closest to fcpX and acpX and returns their Y values.
 */
function getSidecutEndpointWidths(sidecutCurves is array, fcpX is ValueWithUnits, acpX is ValueWithUnits,
    tolerance is ValueWithUnits) returns map
{
    var fcpWidth = 0 * meter;
    var acpWidth = 0 * meter;
    var fcpFound = false;
    var acpFound = false;

    // Use a generous tolerance for finding endpoints (10mm)
    var endpointTolerance = ENDPOINT_MATCH_TOLERANCE;

    for (var bspline in sidecutCurves)
    {
        var range = getBSplineParamRange(bspline);
        var startPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMin] })[0][0];
        var endPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMax] })[0][0];

        // Check if start point is near FCP
        if (!fcpFound && abs(startPt[0] - fcpX) < endpointTolerance)
        {
            fcpWidth = startPt[1];
            fcpFound = true;
        }
        // Check if end point is near FCP
        if (!fcpFound && abs(endPt[0] - fcpX) < endpointTolerance)
        {
            fcpWidth = endPt[1];
            fcpFound = true;
        }

        // Check if start point is near ACP
        if (!acpFound && abs(startPt[0] - acpX) < endpointTolerance)
        {
            acpWidth = startPt[1];
            acpFound = true;
        }
        // Check if end point is near ACP
        if (!acpFound && abs(endPt[0] - acpX) < endpointTolerance)
        {
            acpWidth = endPt[1];
            acpFound = true;
        }

        if (fcpFound && acpFound)
            break;
    }

    if (!fcpFound)
        println("  WARNING: Could not find FCP endpoint in sidecut curves");
    if (!acpFound)
        println("  WARNING: Could not find ACP endpoint in sidecut curves");

    return {
        "fcpWidth" : fcpWidth,
        "acpWidth" : acpWidth
    };
}

/**
 * Compute average radius between inflection points.
 * Uses shared analysis functions from fpt_analyze.fs.
 */
function computeAvgRadiusInRange(curveData is array, fcpX is ValueWithUnits, acpX is ValueWithUnits,
    tolerance is ValueWithUnits) returns map
{
    var config = buildConfig({ "xTolerance" : tolerance });

    // Find inflection points on FCP side
    var inflectionFcp = findInflectionPoint(curveData, fcpX,
        (fcpX + acpX) / 2, config);

    // Find inflection points on ACP side
    var inflectionAcp = findInflectionPoint(curveData,
        (fcpX + acpX) / 2, acpX, config);

    var xStart = inflectionFcp.found ? inflectionFcp.x : fcpX;
    var xEnd = inflectionAcp.found ? inflectionAcp.x : acpX;

    // Compute average radius in concave region
    var avgRadiusResult = computeAverageRadius(curveData, xStart, xEnd, config);

    return {
        "avgRadius" : avgRadiusResult.avgRadius,
        "inflectionFcpX" : xStart,
        "inflectionAcpX" : xEnd,
        "foundInflections" : inflectionFcp.found && inflectionAcp.found
    };
}

/**
 * Get curvature at a specific X coordinate.
 * Uses shared analysis functions from fpt_analyze.fs.
 */
function getCurvatureAtX(curveData is array, targetX is ValueWithUnits, tolerance is ValueWithUnits)
{
    for (var cd in curveData)
    {
        if (cd.xMin <= targetX + tolerance && cd.xMax >= targetX - tolerance)
        {
            var param = findParameterAtX(cd.bspline, targetX, tolerance);
            var curv = getBSplineCurvatureAtParam(cd.bspline, param);
            return curv.curvatureSigned;
        }
    }
    return 0 / meter;
}
// =============================================================================
// CURVE CATEGORIZATION
// =============================================================================

/**
 * Categorize BSpline curves into tip, sidecut, and tail based on X position.
 *
 * TIP: curve ends at or before FCP (xMax <= fcpX)
 * TAIL: curve starts at or after ACP (xMin >= acpX)
 * SIDECUT: curve is between FCP and ACP
 *
 * Curves that span boundaries are split.
 */
function categorizeCurves(context is Context, bsplines is array, fcpX is ValueWithUnits, acpX is ValueWithUnits,
    tolerance is ValueWithUnits) returns map
{
    var tipCurves = [];
    var sidecutCurves = [];
    var tailCurves = [];

    println("Categorizing " ~ size(bsplines) ~ " curves, FCP X=" ~ toString(fcpX) ~ ", ACP X=" ~ toString(acpX));

    for (var i = 0; i < size(bsplines); i += 1)
    {
        var bspline = bsplines[i];
        var bounds = getBSplineBounds(bspline);
        var xMin = bounds.xMin;
        var xMax = bounds.xMax;

        println("  Curve " ~ i ~ ": xMin=" ~ toString(xMin) ~ ", xMax=" ~ toString(xMax));

        // TIP: curve ends at or before FCP (may touch FCP at its endpoint)
        if (xMax <= fcpX + tolerance)
        {
            println("    -> TIP (ends at or before FCP)");
            tipCurves = append(tipCurves, bspline);
            continue;
        }

        // TAIL: curve starts at or after ACP (may touch ACP at its start)
        if (xMin >= acpX - tolerance)
        {
            println("    -> TAIL (starts at or after ACP)");
            tailCurves = append(tailCurves, bspline);
            continue;
        }

        // SIDECUT: curve is entirely between FCP and ACP
        if (xMin >= fcpX - tolerance && xMax <= acpX + tolerance)
        {
            println("    -> SIDECUT (entirely between FCP and ACP)");
            sidecutCurves = append(sidecutCurves, bspline);
            continue;
        }

        // Curve spans one or both boundaries - need to split
        println("    -> SPANS boundaries, splitting...");
        var splits = splitCurveAtContacts(context, bspline, fcpX, acpX, tolerance);

        if (splits.tipPortion != undefined)
        {
            println("      Split produced TIP portion");
            tipCurves = append(tipCurves, splits.tipPortion);
        }
        if (splits.sidecutPortion != undefined)
        {
            println("      Split produced SIDECUT portion");
            sidecutCurves = append(sidecutCurves, splits.sidecutPortion);
        }
        if (splits.tailPortion != undefined)
        {
            println("      Split produced TAIL portion");
            tailCurves = append(tailCurves, splits.tailPortion);
        }
    }

    return {
        "tipCurves" : tipCurves,
        "sidecutCurves" : sidecutCurves,
        "tailCurves" : tailCurves
    };
}

/**
 * Split a BSpline at FCP and/or ACP X coordinates.
 */
function splitCurveAtContacts(context is Context, bspline is BSplineCurve, fcpX is ValueWithUnits, acpX is ValueWithUnits,
    tolerance is ValueWithUnits) returns map
{
    var bounds = getBSplineBounds(bspline);
    var range = getBSplineParamRange(bspline);

    var tipPortion = undefined;
    var sidecutPortion = undefined;
    var tailPortion = undefined;

    // Find parameters at FCP and ACP
    var fcpParam = undefined;
    var acpParam = undefined;

    if (bounds.xMin < fcpX - tolerance && bounds.xMax > fcpX + tolerance)
    {
        fcpParam = findParameterAtX(bspline, fcpX, tolerance);
    }

    if (bounds.xMin < acpX - tolerance && bounds.xMax > acpX + tolerance)
    {
        acpParam = findParameterAtX(bspline, acpX, tolerance);
    }

    // Extract portions based on what boundaries we cross
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

// =============================================================================
// ACCORDION SCALING
// =============================================================================

/**
 * Scale sidecut curves using accordion method.
 * X is stretched/compressed to new RSL length.
 * If target width specified, Y is uniformly scaled to achieve it.
 */
function scaleAccordion(context is Context, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits, buildOptions is map) returns map

{
    var refLength = abs(refAcpX - refFcpX);
    var newLength = abs(newAcpX - newFcpX);
    var xScale = newLength / refLength;

    // Y scale: 1.0 unless target width specified
    var yScale = 1.0;
    if (specifyWidth && abs(refAnalysis.waistWidth) > tolerance)
    {
        yScale = targetWaistWidth / refAnalysis.waistWidth;
    }

    println("  Accordion: xScale=" ~ toString(xScale) ~ ", yScale=" ~ toString(yScale));

    var scaledCurves = [];

    for (var bspline in sidecutCurves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];

        for (var pt in controlPoints)
        {
            // Map X from reference to new coordinate system
            var relativeX = pt[0] - refFcpX;
            var newX = newFcpX + relativeX * xScale;

            // Scale Y uniformly
            var newY = pt[1] * yScale;

            var newPt = vector(newX, newY, pt[2]);
            newControlPoints = append(newControlPoints, newPt);
        }

        var newBSpline = bSplineCurve({
            "degree" : bspline.degree,
            "isPeriodic" : bspline.isPeriodic,
            "controlPoints" : newControlPoints,
            "knots" : bspline.knots,
            "weights" : bspline.weights
        });

        scaledCurves = append(scaledCurves, newBSpline);
    }

    // Compute resulting widths from actual curve endpoints
    var endWidths = getSidecutEndpointWidths(scaledCurves, newFcpX, newAcpX, tolerance);
    var scaledCurveData = buildCurveDataArray(scaledCurves);
    var config = buildConfig({ "xTolerance" : tolerance });
    var waistResult = findWaistPoint(scaledCurveData, newFcpX, newAcpX, config);

    return {
        "curves" : scaledCurves,
        "fcpWidth" : endWidths.fcpWidth,
        "acpWidth" : endWidths.acpWidth,
        "waistWidth" : waistResult.width
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
 * 2. Rotate about pin point to restore reference taper angle
 * 3. Optionally shift Y to hit target waist width
 */
function scaleKeepTaper(context is Context, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits,
    refPinX is ValueWithUnits, newPinX is ValueWithUnits, refPinWidth is ValueWithUnits,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits, buildOptions is map) returns map
{
    var refLength = abs(refAcpX - refFcpX);
    var newLength = abs(newAcpX - newFcpX);
    var xScale = newLength / refLength;

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

        accordionedCurves = append(accordionedCurves, bSplineCurve({
            "degree" : bspline.degree,
            "isPeriodic" : bspline.isPeriodic,
            "controlPoints" : newControlPoints,
            "knots" : bspline.knots,
            "weights" : bspline.weights
        }));
    }

    // Get widths after accordion (Y unchanged, so width at newPinX = refPinWidth)
    var accordionedData = buildCurveDataArray(accordionedCurves);
    var accordionedFcpWidth = getWidthAtX(accordionedData, newFcpX, tolerance);
    var accordionedAcpWidth = getWidthAtX(accordionedData, newAcpX, tolerance);

    // Step 2: Compute rotation angle to restore taper
    var refTaper = refAnalysis.taperAngle;

    // Pivot point is at (newPinX, refPinWidth) - the pin width stays fixed
    var pivotX = newPinX;
    var pivotY = refPinWidth;

    // Current taper after accordion (same Y values, new X values)
    var currentTaper = atan2(accordionedFcpWidth - accordionedAcpWidth, newAcpX - newFcpX);

    // Rotation needed: we want final taper = ref taper
    var rotationAngle = refTaper - currentTaper;

    println("  Keep taper: currentTaper=" ~ toString(currentTaper) ~ ", refTaper=" ~ toString(refTaper));
    println("  Rotation angle=" ~ toString(rotationAngle) ~ " about pivot x=" ~ toString(pivotX) ~ ", y=" ~ toString(pivotY));

    // Step 3: Apply rotation about pivot
    var rotatedCurves = [];
    for (var bspline in accordionedCurves)
    {
        var controlPoints = bspline.controlPoints;
        var newControlPoints = [];

        for (var pt in controlPoints)
        {
            // Rotate point about pivot
            var dx = pt[0] - pivotX;
            var dy = pt[1] - pivotY;

            var cosR = cos(rotationAngle);
            var sinR = sin(rotationAngle);

            var newX = pivotX + dx * cosR - dy * sinR;
            var newY = pivotY + dx * sinR + dy * cosR;

            var newPt = vector(newX, newY, pt[2]);
            newControlPoints = append(newControlPoints, newPt);
        }

        rotatedCurves = append(rotatedCurves, bSplineCurve({
            "degree" : bspline.degree,
            "isPeriodic" : bspline.isPeriodic,
            "controlPoints" : newControlPoints,
            "knots" : bspline.knots,
            "weights" : bspline.weights
        }));
    }

    // Step 4: Optionally shift Y to hit target waist width
    var finalCurves = rotatedCurves;
    if (specifyWidth)
    {
        var rotatedData = buildCurveDataArray(rotatedCurves);
        var config = buildConfig({ "xTolerance" : tolerance });
        var currentWaist = findWaistPoint(rotatedData, newFcpX, newAcpX, config);
        var yShift = targetWaistWidth - currentWaist.width;

        println("  Y shift for target width: " ~ toString(yShift));

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

            finalCurves = append(finalCurves, bSplineCurve({
                "degree" : bspline.degree,
                "isPeriodic" : bspline.isPeriodic,
                "controlPoints" : newControlPoints,
                "knots" : bspline.knots,
                "weights" : bspline.weights
            }));
        }
    }

    // Compute final widths from actual curve endpoints
    var endWidths = getSidecutEndpointWidths(finalCurves, newFcpX, newAcpX, tolerance);
    var finalData = buildCurveDataArray(finalCurves);
    var config = buildConfig({ "xTolerance" : tolerance });
    var finalWaist = findWaistPoint(finalData, newFcpX, newAcpX, config);

    return {
        "curves" : finalCurves,
        "fcpWidth" : endWidths.fcpWidth,
        "acpWidth" : endWidths.acpWidth,
        "waistWidth" : finalWaist.width
    };
}

// =============================================================================
// SCALE RADIUS
// =============================================================================

/**
 * Scale sidecut curves to achieve target average radius while preserving taper angle.
 *
 * Process:
 * 1. Sample curvature progression from reference curves
 * 2. Scale curvature to achieve target average radius (k_new = k_ref * R_ref/R_target)
 * 3. Map X to new RSL length
 * 4. Integrate scaled curvature to get ?_base
 * 5. Solve for ?0 to preserve taper angle
 * 6. Integrate ? to get Y values
 * 7. Solve for y0 based on pin width (or target waist width if specified)
 */
function scaleRadius(context is Context, sidecutCurves is array, refAnalysis is map,
    refFcpX is ValueWithUnits, refAcpX is ValueWithUnits,
    newFcpX is ValueWithUnits, newAcpX is ValueWithUnits,
    refPinX is ValueWithUnits, newPinX is ValueWithUnits, refPinWidth is ValueWithUnits,
    targetRadius is ValueWithUnits,
    specifyWidth is boolean, targetWaistWidth is ValueWithUnits,
    tolerance is ValueWithUnits, buildOptions is map) returns map
{
    var refLength = abs(refAcpX - refFcpX);
    var newLength = abs(newAcpX - newFcpX);

    // Sample curvature from reference
    var numSamples = ANALYSIS_SAMPLES;
    var refXSamples = [];
    var kSamples = [];

    for (var i = 0; i < numSamples; i += 1)
    {
        var t = i / (numSamples - 1);
        var x = refFcpX + t * (refAcpX - refFcpX);
        refXSamples = append(refXSamples, x);

        var k = getCurvatureAtX(refAnalysis.curveData, x, tolerance);
        kSamples = append(kSamples, k);
    }

    // Scale curvature to achieve target radius
    // k_new = k_ref * (R_ref / R_target)
    var radiusScale = refAnalysis.avgRadius / targetRadius;

    println("  Scale radius: refAvgRadius=" ~ toString(refAnalysis.avgRadius) ~
            ", targetRadius=" ~ toString(targetRadius) ~ ", scale=" ~ toString(radiusScale));

    var scaledK = [];
    for (var k in kSamples)
    {
        scaledK = append(scaledK, k * radiusScale);
    }

    // Map X to new RSL length
    var newXSamples = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var t = i / (numSamples - 1);
        var newX = newFcpX + t * (newAcpX - newFcpX);
        newXSamples = append(newXSamples, newX);
    }

    // Integrate curvature to get ?_base: ?_base = ?k dx
    var thetaBase = cumTrapz(newXSamples, scaledK, 0).cumulative;

    // Build base map for solver
    // Integrate ?_base to get y_base (small angle approximation: tan(?) � ?)
    var yBase = cumTrapz(newXSamples, thetaBase, 0 * millimeter).cumulative;

    var base = {
        "x" : newXSamples,
        "k" : thetaBase,  // This is actually cumulative theta from curvature
        "y" : yBase
    };

    // Solve for ?0 to preserve taper angle
    var integrationDef = {
        "angleDriver" : AngleDriver.TAPER_ANGLE,
        "taperAngle" : refAnalysis.taperAngle
    };

    var theta0 = solveTheta0ForDriver(base, integrationDef, 0.0001 * degree, 50);

    println("  Solved theta0=" ~ toString(theta0));

    // Evaluate final Y values (before vertical positioning)
    var yFinal = evalY(base, theta0, 0 * millimeter);

    // Find waist (minimum Y) - needed for both positioning methods
    var minY = inf * meter;
    for (var i = 0; i < size(yFinal); i += 1)
    {
        if (yFinal[i] < minY)
        {
            minY = yFinal[i];
        }
    }

    // Determine y0 based on pin or target waist width
    var y0;
    if (specifyWidth)
    {
        // Position so waist hits target width
        y0 = targetWaistWidth - minY;
        println("  Positioning by target waist: minY=" ~ toString(minY) ~ ", y0=" ~ toString(y0));
    }
    else
    {
        // Position so that width at pin location matches reference pin width
        // Find Y at newPinX by interpolation
        var pinIdx = 0;
        for (var i = 0; i < size(newXSamples) - 1; i += 1)
        {
            if (newXSamples[i] <= newPinX && newXSamples[i + 1] >= newPinX)
            {
                pinIdx = i;
                break;
            }
        }

        // Linear interpolation to get Y at newPinX
        var t = (newPinX - newXSamples[pinIdx]) / (newXSamples[pinIdx + 1] - newXSamples[pinIdx]);
        var yAtPin = yFinal[pinIdx] + t * (yFinal[pinIdx + 1] - yFinal[pinIdx]);

        y0 = refPinWidth - yAtPin;
        println("  Positioning by pin: yAtPin=" ~ toString(yAtPin) ~ ", refPinWidth=" ~ toString(refPinWidth) ~ ", y0=" ~ toString(y0));
    }

    // Apply y0 shift
    var finalY = [];
    for (var y in yFinal)
    {
        finalY = append(finalY, y + y0);
    }

    // Build points for new curve
    var newPoints = [];
    for (var i = 0; i < size(newXSamples); i += 1)
    {
        newPoints = append(newPoints, vector(newXSamples[i], finalY[i], 0 * millimeter));
    }

    // Create approximating BSpline through points
    var newCurve = approximateSpline(context, {
        "degree" : 3,
        "tolerance" : 1e-5,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : newPoints })]
    })[0];

    // Get final widths
    var finalFcpWidth = finalY[0];
    var finalAcpWidth = finalY[size(finalY) - 1];
    var finalWaistWidth = minY + y0;

    return {
        "curves" : [newCurve],
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
    yScale, isTip is boolean) returns array
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

        var newBSpline = bSplineCurve({
            "degree" : bspline.degree,
            "isPeriodic" : bspline.isPeriodic,
            "controlPoints" : newControlPoints,
            "knots" : bspline.knots,
            "weights" : bspline.weights
        });

        transformedCurves = append(transformedCurves, newBSpline);
    }

    return transformedCurves;
}

// =============================================================================
// CONTINUITY REPAIR
// =============================================================================

/**
 * Repair G1 continuity at a contact point between tip/tail and sidecut curves.
 *
 * Modes:
 *   TIP_TAIL_ONLY: Only adjust tip/tail control points
 *   RSL_ONLY: Only adjust sidecut control points
 *   MINIMUM: Adjust both to minimize overall change
 *
 * Returns map with:
 *   tipTailCurves: adjusted tip/tail curves
 *   sidecutCurves: adjusted sidecut curves
 */
function repairContinuityAtContact(tipTailCurves is array, sidecutCurves is array,
    contactX is ValueWithUnits, isTip is boolean, mode is ContinuityMassageMode,
    tolerance is ValueWithUnits) returns map
{
    // Find the curves that connect at this contact point
    var tipTailCurve = undefined;
    var tipTailIndex = -1;
    var tipTailConnectsAtStart = false;

    var sidecutCurve = undefined;
    var sidecutIndex = -1;
    var sidecutConnectsAtStart = false;

    // Find tip/tail curve at contact
    for (var i = 0; i < size(tipTailCurves); i += 1)
    {
        var bspline = tipTailCurves[i];
        var bounds = getBSplineBounds(bspline);
        var range = getBSplineParamRange(bspline);

        var connectsHere = isTip ?
            (abs(bounds.xMax - contactX) < tolerance) :
            (abs(bounds.xMin - contactX) < tolerance);

        if (connectsHere)
        {
            tipTailCurve = bspline;
            tipTailIndex = i;

            var startPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMin] })[0][0];
            var endPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMax] })[0][0];
            tipTailConnectsAtStart = (abs(startPt[0] - contactX) < abs(endPt[0] - contactX));
            break;
        }
    }

    // Find sidecut curve at contact
    for (var i = 0; i < size(sidecutCurves); i += 1)
    {
        var bspline = sidecutCurves[i];
        var bounds = getBSplineBounds(bspline);
        var range = getBSplineParamRange(bspline);

        var connectsHere = isTip ?
            (abs(bounds.xMin - contactX) < tolerance) :
            (abs(bounds.xMax - contactX) < tolerance);

        if (connectsHere)
        {
            sidecutCurve = bspline;
            sidecutIndex = i;

            var startPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMin] })[0][0];
            var endPt = evaluateSpline({ "spline" : bspline, "parameters" : [range.uMax] })[0][0];
            sidecutConnectsAtStart = (abs(startPt[0] - contactX) < abs(endPt[0] - contactX));
            break;
        }
    }

    // If we can't find both curves, return unchanged
    if (tipTailCurve == undefined || sidecutCurve == undefined)
    {
        println("  Warning: Could not find curves at contact X=" ~ toString(contactX));
        return {
            "tipTailCurves" : tipTailCurves,
            "sidecutCurves" : sidecutCurves
        };
    }

    // Get current tangents at the junction
    var tipTailTangent = getEndTangent(tipTailCurve, tipTailConnectsAtStart);
    var sidecutTangent = getEndTangent(sidecutCurve, sidecutConnectsAtStart);

    // Compute target tangent based on mode
    var targetTangent;
    if (mode == ContinuityMassageMode.TIP_TAIL_ONLY)
    {
        // Target is the sidecut tangent (tip/tail adjusts to match)
        targetTangent = sidecutConnectsAtStart ? sidecutTangent : -sidecutTangent;
    }
    else if (mode == ContinuityMassageMode.RSL_ONLY)
    {
        // Target is the tip/tail tangent (sidecut adjusts to match)
        targetTangent = tipTailConnectsAtStart ? -tipTailTangent : tipTailTangent;
    }
    else  // MINIMUM
    {
        // Average the tangents (both adjust halfway)
        var t1 = tipTailConnectsAtStart ? -tipTailTangent : tipTailTangent;
        var t2 = sidecutConnectsAtStart ? sidecutTangent : -sidecutTangent;
        targetTangent = normalize(t1 + t2);
    }

    println("  Contact repair at X=" ~ toString(contactX) ~ " mode=" ~ mode);

    // Apply repairs based on mode
    var newTipTailCurves = tipTailCurves;
    var newSidecutCurves = sidecutCurves;

    if (mode == ContinuityMassageMode.TIP_TAIL_ONLY || mode == ContinuityMassageMode.MINIMUM)
    {
        var adjustedTipTail = adjustCurveEndTangent(tipTailCurve, tipTailConnectsAtStart,
            tipTailConnectsAtStart ? -targetTangent : targetTangent);
        newTipTailCurves = replaceArrayElement(tipTailCurves, tipTailIndex, adjustedTipTail);
    }

    if (mode == ContinuityMassageMode.RSL_ONLY || mode == ContinuityMassageMode.MINIMUM)
    {
        var adjustedSidecut = adjustCurveEndTangent(sidecutCurve, sidecutConnectsAtStart,
            sidecutConnectsAtStart ? targetTangent : -targetTangent);
        newSidecutCurves = replaceArrayElement(sidecutCurves, sidecutIndex, adjustedSidecut);
    }

    return {
        "tipTailCurves" : newTipTailCurves,
        "sidecutCurves" : newSidecutCurves
    };
}

/**
 * Get the tangent vector at the start or end of a BSpline.
 */
function getEndTangent(bspline is BSplineCurve, atStart is boolean) returns Vector
{
    var range = getBSplineParamRange(bspline);
    var param = atStart ? range.uMin : range.uMax;
    var result = evaluateSpline({ "spline" : bspline, "parameters" : [param], "nDerivatives" : 1 });
    return normalize(result[1][0]);
}

/**
 * Adjust a curve's control point to achieve a target tangent at one end.
 * Preserves the endpoint position, only moves the adjacent control point.
 */
function adjustCurveEndTangent(bspline is BSplineCurve, atStart is boolean, targetTangent is Vector) returns BSplineCurve
{
    var controlPoints = bspline.controlPoints;
    var newControlPoints = [];

    // Copy all control points
    for (var i = 0; i < size(controlPoints); i += 1)
    {
        newControlPoints = append(newControlPoints, controlPoints[i]);
    }

    if (bspline.degree >= 2 && size(controlPoints) >= 3)
    {
        if (atStart)
        {
            var cp0 = controlPoints[0];
            var cp1 = controlPoints[1];
            var dist = norm(cp1 - cp0);
            var newCp1 = cp0 + targetTangent * dist;
            newControlPoints[1] = newCp1;
        }
        else
        {
            var n = size(controlPoints);
            var cpLast = controlPoints[n - 1];
            var cpPrev = controlPoints[n - 2];
            var dist = norm(cpLast - cpPrev);
            var newCpPrev = cpLast + targetTangent * dist;
            newControlPoints[n - 2] = newCpPrev;
        }
    }

    return bSplineCurve({
        "degree" : bspline.degree,
        "isPeriodic" : bspline.isPeriodic,
        "controlPoints" : newControlPoints,
        "knots" : bspline.knots,
        "weights" : bspline.weights
    });
}

/**
 * Replace an element in an array at a specific index.
 */
function replaceArrayElement(arr is array, index is number, newElement) returns array
{
    var result = [];
    for (var i = 0; i < size(arr); i += 1)
    {
        if (i == index)
            result = append(result, newElement);
        else
            result = append(result, arr[i]);
    }
    return result;
}


