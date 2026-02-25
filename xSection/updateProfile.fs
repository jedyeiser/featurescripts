FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSectReferencePoints.fs
import(path : "08fddb59786b6bfee020ee05", version : "ef4a95bef1c88e594c76dedb");
// IMPORT: xSectBeamAnalysis.fs
import(path : "ebac109589e3bf405d3f3ae7", version : "8984e1ab14c99a11a8b53f23");


/**
 * This function is used to make informed decisions about what a ski or
 * snowboard thickness profile SHOULD be to achieve a provided ei proifle.
 *
 * Much data will be given in the form of edges. We can assume that these EI curves will be in the XZ plane and can
 * convert from 'world/geometry space' to get the EI value by converting the world Z value in mm to 1Nm^2
 *
 * Much of the analysis here is done prior with a cross section analysis. This
 * feature relies on significicant functionality built by other features/files.
 *
 * This feature therefore depends on the stability of those outputs.
 *
 * User provides a query for target EI edges.
 *
 * User provides a feature reference to get the ID/Name of significant geometry and material prep work
 *
 * User selects either INHERIT or QUERY to specify if we should treat the EI profile from
 * the feature as our measured EI profile, or if we would like to specify a query for our measured EI profile.
 *
 * User selectes either INHERIT or QUERY to specify if we should treat the thickness profile calculated
 * by the preceeding feature as our measured thickness profile, or if we should provide an edge query
 * for the measured thickness.
 *
 * We now use this data to generate an updated profile guess or estimate to achieve our EI target. We have three basic modes.
 * STD, DELTA, PERCENT that define how we will calculate our updated thickness profile. While it may be STD, these are the methods that
 * will be the most complicated.
 *
 * The delta under consideration here is the difference between our target and measured stiffness profiles at each relevant X location from the
 * preceeding feature.
 *
 * When STD is selected, a boolean appears - scaleDelta. When selected the user can apply a scalefactor delta (1, or 100% is the default).
 * STD is going to use the detailed geometery and material data for our cross sections to hit a 'new' EI curve.
 * In general, we asume the 'new target EI curve' to be equal to the measured EI Profile plus 100% of the delta. If we only want to go half the distance, we can
 * do so by specifying the scalefactor.
 *
 * We can apply a very simple rule to
 * control our solver can alter geometry efficently to solve for a stiffness - if the point is above the neutral axis, it moves up with height change.
 * If a point is below the neutral axis, its height does not change with changes to the profile height.
 *
 * If either DELTA or PERCENT is selected, we're going to rely on some simple calculations. That is that we can fairly reasonably
 * capture how changes to profile affect changes in stiffness by saying EI(x)/b(x) = alpha * t ^ Beta. Solve for these, Our feature uses
 * data from the inherited feature to solve for these.
 *
 * If DELTA is selected, user specifies a percentage of the delta to apply, similar to STD
 * We solve for the new profile height at each cross section by using it's width and finding the t such that
 * the new EI = measuredEI + Delta * scalefactor using our EI/b = alpha * t ^ beta model.
 *
 * Ironically, if PERCENT is selected, we do not provide a percentage.
 * Rather, for each cross section we calculate the percentage difference between the measured and
 * target EI profiles. We specify that t_new^Beta = %change * t_old^Beta.
 *
 * User provides standard spline approximation parameters, and solved profile is approximated to those parameters.
 *
 * User can provide a string to name the output profile.
 *
 *
 */

export enum DataInheretenceType
{
    INHERIT,
    QUERY
}

export enum SolverType
{
    STD,
    DELTA,
    PERCENT
}

/**
 * Sample EI from a set of edges at a given world-X coordinate.
 * Convention: 1 mm world Z = 1 N·m² EI (matches xSectVisualization.fs EI curve scale).
 * Returns undefined if xTarget is outside the range of the sampled edges.
 */
function sampleEIEdgesAtX(context is Context, edgeQuery is Query, xTarget)
{
    var edges = evaluateQuery(context, edgeQuery);
    if (size(edges) == 0)
    {
        return undefined;
    }

    var SAMPLE_PARAMS = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0];
    var bestLeft  = undefined;
    var bestRight = undefined;

    for (var edge in edges)
    {
        for (var p in SAMPLE_PARAMS)
        {
            var tl = evEdgeTangentLine(context, {
                "edge"                      : edge,
                "parameter"                 : p,
                "arcLengthParameterization" : false
            });
            var sx = tl.origin[0];  // world X, ValueWithUnits (meters)

            if (sx <= xTarget)
            {
                if (bestLeft == undefined || sx > bestLeft["x"])
                {
                    bestLeft = { "x" : sx, "z" : tl.origin[2] };
                }
            }
            else
            {
                if (bestRight == undefined || sx < bestRight["x"])
                {
                    bestRight = { "x" : sx, "z" : tl.origin[2] };
                }
            }
        }
    }

    if (bestLeft == undefined || bestRight == undefined)
    {
        return undefined;
    }

    var x0    = bestLeft["x"]  / (1 * meter);
    var x1    = bestRight["x"] / (1 * meter);
    var xT    = xTarget        / (1 * meter);
    var denom = x1 - x0;
    if (abs(denom) < 1e-12)
    {
        return undefined;
    }

    var frac = (xT - x0) / denom;
    var z0   = bestLeft["z"]  / (1 * millimeter);
    var z1   = bestRight["z"] / (1 * millimeter);

    // 1 mm world Z = 1 N·m² EI
    return (z0 + frac * (z1 - z0)) * (1 * newton * meter * meter);
}

/**
 * Sample 100 parametric points per EI edge, decode EI from world Z (1 mm = 1 N·m²),
 * sort by X, and linearly extrapolate to FCP/ACP boundaries if needed.
 * Identical logic to getEIFromEdges in estimateStiffness.fs.
 */
function getEIFromEdges(context is Context, eiEdges is Query, xFCP is ValueWithUnits, xACP is ValueWithUnits) returns array
{
    var edges = evaluateQuery(context, eiEdges);
    var points = [];
    var numSamples = 100;

    for (var edge in edges)
    {
        for (var i = 0; i < numSamples; i += 1)
        {
            var t = i / (numSamples - 1);
            try
            {
                var tangentLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : t });
                var pt = tangentLine.origin;
                var EI = (pt[2] / millimeter) * newton * meter * meter;
                points = append(points, { "x" : pt[0], "EI" : EI });
            }
            // skip failed evaluations
        }
    }

    if (size(points) < 2)
        return points;

    // Insertion sort by x
    for (var i = 1; i < size(points); i += 1)
    {
        var key = points[i];
        var j = i - 1;
        while (j >= 0 && points[j].x > key.x)
        {
            points[j + 1] = points[j];
            j -= 1;
        }
        points[j + 1] = key;
    }

    var n = size(points);

    // Linear extrapolation at front boundary
    if (points[0].x > xFCP && n >= 2)
    {
        var dx = points[1].x - points[0].x;
        if (abs(dx) > 1e-10 * meter)
        {
            var slope = (points[1].EI - points[0].EI) / dx;
            var extEI = points[0].EI + slope * (xFCP - points[0].x);
            if (extEI < 0 * newton * meter * meter)
                extEI = 0 * newton * meter * meter;
            points = concatenateArrays([[{ "x" : xFCP, "EI" : extEI }], points]);
            n = size(points);
        }
    }

    // Linear extrapolation at rear boundary
    if (points[n - 1].x < xACP && n >= 2)
    {
        var dx2 = points[n - 1].x - points[n - 2].x;
        if (abs(dx2) > 1e-10 * meter)
        {
            var slope2 = (points[n - 1].EI - points[n - 2].EI) / dx2;
            var extEI2 = points[n - 1].EI + slope2 * (xACP - points[n - 1].x);
            if (extEI2 < 0 * newton * meter * meter)
                extEI2 = 0 * newton * meter * meter;
            points = append(points, { "x" : xACP, "EI" : extEI2 });
        }
    }

    return points;
}

/**
 * Compute area, Y-direction centroid, and second moment Iyy about centroid
 * for a single triangle. All inputs are plain numbers (m); outputs are plain
 * numbers (m², m, m⁴).
 *
 * @param y1, y2, y3  Thickness-direction coordinates (m)
 * @param z1, z2, z3  Width-direction coordinates (m)
 * @returns {map} { area (m²), centroid_y (m), Iyy_centroid (m⁴) }
 */
function computeTriangleProperties(y1, y2, y3, z1, z2, z3) returns map
{
    var cross        = (y2 - y1) * (z3 - z1) - (y3 - y1) * (z2 - z1);
    var area         = abs(cross) / 2;
    var centroid_y   = (y1 + y2 + y3) / 3;
    var Iyy_centroid = (area / 18) * (y1*y1 + y2*y2 + y3*y3 - y1*y2 - y1*y3 - y2*y3);
    return {
        "area"         : area,
        "centroid_y"   : centroid_y,
        "Iyy_centroid" : Iyy_centroid
    };
}

/**
 * Recompute effective EI after shifting all section vertices above originalNA_m
 * upward by deltaT_m. Performs full CLT accumulation on the shifted geometry.
 *
 * Point classification (above/below NA) is fixed at the original neutralAxisY
 * throughout all bisection steps. The resulting NA is re-derived from CLT at
 * each evaluation (from the shifted geometry).
 *
 * Formula: EI_eff = D11 - B11² / A11  (same as xSectCLT.fs)
 *
 * @param sectionPoints  Array of { point2D: [y_VWU, z_VWU], ... }
 * @param originalNA_m   Original neutral axis height, plain number (m)
 * @param deltaT_m       Upward shift for vertices above original NA (m)
 * @param bodyData       Array of { bodyIdx, groups: [{ triangles: [[i,j,k],...] }] }
 * @param bodies         Array of body material data indexed by bodyIdx
 * @returns {map} { EI_Nm2 (plain N·m²), NA_m (plain m) } or undefined if degenerate
 */
function computeEIFromShiftedPoints(sectionPoints is array, originalNA_m, deltaT_m,
                                    bodyData is array, bodies is array)
{
    // Build stripped + shifted coordinate arrays (plain numbers, meters)
    var shiftedY = [];
    var shiftedZ = [];
    for (var pt in sectionPoints)
    {
        var y = pt.point2D[0] / meter;
        var z = pt.point2D[1] / meter;
        if (y > originalNA_m)
        {
            y = y + deltaT_m;
        }
        shiftedY = append(shiftedY, y);
        shiftedZ = append(shiftedZ, z);
    }

    // Accumulate CLT A, B, D sums (plain numbers: N, N·m, N·m²)
    var A_sum = 0.0;
    var B_sum = 0.0;
    var D_sum = 0.0;

    for (var entry in bodyData)
    {
        var bodyIdx = entry.bodyIdx;
        var body    = bodies[bodyIdx];
        if (body.hasMaterialData != true)
        {
            continue;
        }

        var Q11 = body.materialData.qMatrix[0][0] / pascal;  // plain N/m²

        // Sum triangle contributions for this body
        var body_area   = 0.0;
        var body_area_y = 0.0;
        var body_Iyy    = 0.0;

        if (entry.groups != undefined)
        {
            for (var group in entry.groups)
            {
                if (group.triangles != undefined)
                {
                    for (var tri in group.triangles)
                    {
                        var vi = tri[0];
                        var vj = tri[1];
                        var vk = tri[2];
                        var props = computeTriangleProperties(
                            shiftedY[vi], shiftedY[vj], shiftedY[vk],
                            shiftedZ[vi], shiftedZ[vj], shiftedZ[vk]);
                        body_area   += props.area;
                        body_area_y += props.area * props.centroid_y;
                        body_Iyy    += props.Iyy_centroid;
                    }
                }
            }
        }

        if (body_area <= 0)
        {
            continue;
        }

        var body_centroid_y = body_area_y / body_area;
        A_sum += Q11 * body_area;
        B_sum += Q11 * body_area * body_centroid_y;
        D_sum += Q11 * (body_Iyy + body_area * body_centroid_y * body_centroid_y);
    }

    if (A_sum <= 0)
    {
        return undefined;
    }

    var NA_m   = B_sum / A_sum;
    var EI_Nm2 = D_sum - (B_sum * B_sum) / A_sum;

    return {
        "EI_Nm2" : EI_Nm2,
        "NA_m"   : NA_m
    };
}

export function updateProfileEditLogic(context is Context, id is Id, oldDefinition is map,
   definition is map, isCreating is boolean, specifiedParameters is map, clickedButton is string) returns map
{
    println("updateProfileEditLogic called");

    // Sync mode-visibility flags
    definition.showCalcs     = (definition.solverType == SolverType.DELTA ||
                                definition.solverType == SolverType.PERCENT);
    definition.showStdOptions = (definition.solverType == SolverType.STD);

    // Sync query-gate booleans
    definition.provideMeasuredEI        = (definition.measuredEIType        == DataInheretenceType.QUERY);
    definition.provideMeasuredThickness = (definition.measuredThicknessType == DataInheretenceType.QUERY);

    // Compute alpha/beta when DELTA or PERCENT and a feature has been selected
    if (definition.showCalcs && size(keys(definition.xSectFeature)) > 0)
    {
        try
        {
            var oldID = keys(definition.xSectFeature)[0][0];
            var allEIData = getAttribute(context, {
                "entity" : qOrigin(EntityType.BODY),
                "name"   : "CrossSectionAnalysis"
            });
            var crossSections = allEIData[oldID].details.crossSections;

            // Log-linear regression: log(EI/b) = log(alpha) + beta * log(t)
            var sumX  = 0.0;
            var sumY  = 0.0;
            var sumXX = 0.0;
            var sumXY = 0.0;
            var n = 0;
            for (var cs in crossSections)
            {
                // MEMORY: boundingBox.width = beam height = thickness; .height = beam width
                var t_m = cs.boundingBox.width  / (1 * meter);
                var b_m = cs.boundingBox.height / (1 * meter);
                var EI  = cs.EI_eff / (1 * newton * meter * meter);
                if (t_m > 0 && b_m > 0 && EI > 0)
                {
                    var lx = log(t_m);
                    var ly = log(EI / b_m);
                    sumX  += lx;
                    sumY  += ly;
                    sumXX += lx * lx;
                    sumXY += lx * ly;
                    n += 1;
                }
            }
            if (n >= 2)
            {
                var denom = n * sumXX - sumX * sumX;
                if (abs(denom) > 1e-12)
                {
                    var beta  = (n * sumXY - sumX * sumY) / denom;
                    var alpha = exp((sumY - beta * sumX) / n);
                    definition.calcBeta  = beta;
                    definition.calcAlpha = alpha;
                    
                    println('alpha -> ' ~ alpha);
                    println('beta -> ' ~ beta);
                }
            }
        }
        // try with no catch silently swallows errors (attribute not yet written, etc.)
    }

    if (clickedButton == "recalculate")
    {
        println('buttonClicked');
    }

    // Check FCP/ACP validity and compute stiffness estimates
    definition.stiffnessDataAvailable = false;
    try
    {
        var fcpEntities = evaluateQuery(context, definition.fcpQuery);
        var acpEntities = evaluateQuery(context, definition.acpQuery);

        if (size(fcpEntities) > 0 && size(acpEntities) > 0)
        {
            var xFCP = resolveReferencePointX(context, definition.fcpQuery, definition.targetEIQuery);
            var xACP = resolveReferencePointX(context, definition.acpQuery, definition.targetEIQuery);

            if (xFCP != undefined && xACP != undefined && xFCP < xACP)
            {
                var eiData = getEIFromEdges(context, definition.targetEIQuery, xFCP, xACP);
                if (size(eiData) >= 2)
                {
                    var result = computeBeamStiffness(eiData, xFCP, xACP);
                    definition.prismaticlb = result.prismaticStiffness_lbin;
                    definition.prismaticmm = result.prismaticStiffness_mm * millimeter;
                    definition.estimatedlb = result.estimatedStiffness_lbin;
                    definition.estimatedmm = result.estimatedStiffness_mm * millimeter;
                    definition.stiffnessDataAvailable = true;
                }
            }
        }
    }
    // try with no catch: errors leave stiffnessDataAvailable = false

    return definition;
}


export const DeltaPercentBounds = {(unitless) : [0.001, 1, 1]} as RealBoundSpec;

export const ALPHA_DISPLAY_BOUNDS = { (unitless) : [0, 1e9, 1e15] } as RealBoundSpec;

annotation {
    "Feature Type Name" : "Update profile",
    "Feature Type Description" : "Uses input data to make informed decisions about what the thickness profile of a ski/snowboard should be to achieve an EI Target profile",
    "Editing Logic Function" : "updateProfileEditLogic"
    }
export const updateProfile = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
       annotation { "Name" : "Solver type", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : SolverType.STD }
       definition.solverType is SolverType;

       annotation { "Name" : "Output curve name", "Default" : "Updated thickness" }
       definition.outputCurveName is string;

       annotation { "Name" : "Cross section feature", "MaxNumberOfPicks" : 1 }
       definition.xSectFeature is FeatureList;

       annotation { "Name" : "Target EI profile", "Filter" : EntityType.EDGE, "Description" : "Edges representing the target EI profile. World Z in mm = EI in N*m^2" }
       definition.targetEIQuery is Query;

       annotation { "Name" : "Measured EI from", "Default" : DataInheretenceType.INHERIT, "Decription" : "Specifies if we should treat our modeled EI as our measured EI" , "UIHint" : UIHint.SHOW_LABEL }
       definition.measuredEIType is DataInheretenceType;

       annotation { "Name" : "provideMeasuredEI", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
       definition.provideMeasuredEI is boolean;

       if (definition.provideMeasuredEI)
       {
           annotation { "Name" : "Measured EI edges", "Filter" : EntityType.EDGE}
           definition.measuredEIQuery is Query;
       }

       annotation { "Name" : "Measured thickness from", "Default" : DataInheretenceType.INHERIT, "UIHint" : UIHint.SHOW_LABEL, "Decription" : "Specifies if we should assume the measured ski had the correct (theoretical) profile, or if we're going to provide a measured profile" }
       definition.measuredThicknessType is DataInheretenceType;

       annotation { "Name" : "provideMeasuredThickness", "UIHint" : UIHint.ALWAYS_HIDDEN }
       definition.provideMeasuredThickness is boolean;

       if (definition.provideMeasuredThickness)
       {
           annotation { "Name" : "Measured thickness edges", "Filter" : EntityType.EDGE}
           definition.measuredThicknessQuery is Query;
       }

       annotation { "Name" : "showStdOptions", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
       definition.showStdOptions is boolean;

       if (definition.showStdOptions)
       {
           annotation { "Name" : "Scale delta", "Default" : false,
                        "Description" : "Apply less than 100% of the EI delta (1.0 = full correction)" }
           definition.scaleDelta is boolean;

           if (definition.scaleDelta)
           {
               annotation { "Name" : "Delta scale factor" }
               isReal(definition.deltaScaleFactor, DeltaPercentBounds);
           }
       }

       annotation { "Name" : "showCalcs", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false, "Descrption" : "true when definition.solverType == solverType.DELTA or solverType.PERCENT" }
       definition.showCalcs is boolean;

       if (definition.showCalcs)
       {
           annotation { "Name" : "Override percentage delta" }
           definition.overridePercentageDelta is boolean;

           if (definition.overridePercentageDelta) // no matter what the applyDeltaPercentage is, if overridePercentageDelta is false, use 1 as the applyPercentageDelta.
           {
               annotation { "Name" : "Apply percentage to delta" }
               isReal(definition.applyDeltaPercentage, DeltaPercentBounds);
           }
       }

       annotation { "Name" : "FCP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
       definition.fcpQuery is Query;

       annotation { "Name" : "ACP", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.VERTEX) || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
       definition.acpQuery is Query;

       annotation { "Name" : "stiffnessAvailable", "Default" : false, "UIHint" : UIHint.ALWAYS_HIDDEN, "Description" : "When true, show calculated stiffness data. gets triggered to true in editing logic when both definition.acpQuery and definition.fcpQuery are valid" }
       definition.stiffnessDataAvailable is boolean;

       if (definition.stiffnessDataAvailable)
       {
           annotation { "Group Name" : "Stiffness estimates", "Collapsed By Default" : true }
           {
               annotation { "Name" : "Prismatic stiffness (lb/in)", "UIHint" : UIHint.READ_ONLY }
               isReal(definition.prismaticlb, POSITIVE_REAL_BOUNDS);

               annotation { "Name" : "Prismatic stiffness (mm/30kg)", "UIHint" : UIHint.READ_ONLY }
               isLength(definition.prismaticmm, LENGTH_BOUNDS);

               annotation { "Name" : "Estimated stiffness (lb/in)", "UIHint" : UIHint.READ_ONLY }
               isReal(definition.estimatedlb, POSITIVE_REAL_BOUNDS);

               annotation { "Name" : "Estimated stiffness (mm/30kg)", "UIHint" : UIHint.READ_ONLY }
               isLength(definition.estimatedmm, LENGTH_BOUNDS);

           }
       }

       if (definition.showCalcs)
       {
           annotation { "Group Name" : "Width, thickness, stiffness data", "Collapsed By Default" : true }
           {
               annotation { "Name" : "Alpha", "Description" : "coefficent in EI/b = alpha * t ^ beta", "UIHint" : UIHint.READ_ONLY }
               isReal(definition.calcAlpha, ALPHA_DISPLAY_BOUNDS);

               annotation { "Name" : "Beta", "Description" : "exponent in EI/b = alpha * t ^ beta", "UIHint" : UIHint.READ_ONLY }
               isReal(definition.calcBeta, POSITIVE_REAL_BOUNDS);

           }

       }


       annotation { "Name" : "Recalculate" }
       isButton(definition.recalculate);

    }
    {
        // --- 1. Read cross-section data from the inherited feature ---
        var oldID = keys(definition.xSectFeature)[0][0];

        var allEIData = getAttribute(context, {
            "entity" : qOrigin(EntityType.BODY),
            "name"   : "CrossSectionAnalysis"
        });

        var crossSectionData    = allEIData[oldID];
        var crossSectionDetails = crossSectionData.details;
        var crossSections       = crossSectionDetails.crossSections;
        var bodies              = crossSectionDetails.bodies;

        // Always recompute alpha/beta from current data (do not rely on stale definition values)
        var liveAlpha = definition.calcAlpha;  // fallback to stored
        var liveBeta  = definition.calcBeta;

        if (definition.solverType != SolverType.STD)
        {
            var sumX  = 0.0; var sumY  = 0.0;
            var sumXX = 0.0; var sumXY = 0.0;
            var n = 0;
            for (var cs in crossSections)
            {
                var t_m = cs.boundingBox.width  / (1 * meter);
                var b_m = cs.boundingBox.height / (1 * meter);
                var EI  = cs.EI_eff / (1 * newton * meter * meter);
                if (t_m > 0 && b_m > 0 && EI > 0)
                {
                    var lx = log(t_m);
                    var ly = log(EI / b_m);
                    sumX  += lx;  sumY  += ly;
                    sumXX += lx * lx;  sumXY += lx * ly;
                    n += 1;
                }
            }
            if (n >= 2)
            {
                var denom = n * sumXX - sumX * sumX;
                if (abs(denom) > 1e-12)
                {
                    liveBeta  = (n * sumXY - sumX * sumY) / denom;
                    liveAlpha = exp((sumY - liveBeta * sumX) / n);
                }
            }
            println("updateProfile: liveAlpha=" ~ liveAlpha ~ " liveBeta=" ~ liveBeta ~ " (n=" ~ n ~ ")");
        }

        // Unit factor: 1 N·m²
        var EI_UNIT = 1 * newton * meter * meter;

        // --- 2–7. Build output points, one per cross-section station ---
        var outputPoints = [];
        var tableRows    = [];

        for (var cs in crossSections)
        {
            var xCoord = cs.xCoord;  // ValueWithUnits (meters)

            // Sample target EI at this station's world-X coordinate
            var targetEI = sampleEIEdgesAtX(context, definition.targetEIQuery, xCoord);
            if (targetEI == undefined)
            {
                continue;  // station outside target curve range — skip
            }

            // Get measured EI: inherit from cross-section or sample from query
            var measuredEI = cs.EI_eff;  // ValueWithUnits N·m²
            if (definition.measuredEIType == DataInheretenceType.QUERY)
            {
                var sampledMeasured = sampleEIEdgesAtX(context, definition.measuredEIQuery, xCoord);
                if (sampledMeasured != undefined)
                {
                    measuredEI = sampledMeasured;
                }
            }

            // MEMORY: boundingBox.width = beam height = THICKNESS; .height = beam width = WIDTH
            var t_old = cs.boundingBox.width;   // ValueWithUnits m (thickness)
            var b     = cs.boundingBox.height;  // ValueWithUnits m (width)

            // Strip units for arithmetic
            var t_old_m = t_old    / (1 * meter);
            var b_m     = b        / (1 * meter);
            var measEI  = measuredEI / EI_UNIT;
            var targEI  = targetEI   / EI_UNIT;

            // Guard against degenerate cross-sections
            if (t_old_m <= 0 || b_m <= 0 || measEI <= 0)
            {
                continue;
            }

            // Compute new thickness based on solver type
            var t_new_m  = t_old_m;  // default: no change
            var solvedEI = measEI;   // default: unchanged

            if (definition.solverType == SolverType.STD)
            {
                // STD: full CLT bisection solve.
                // Find deltaT such that shifting all mesh vertices above the original
                // neutral axis by deltaT yields the target effective EI.
                var STD_MIN_THICKNESS_M = 0.002;  // 2 mm physical lower bound
                var STD_MAX_DELTA_M     = t_old_m; // allow up to doubling total thickness
                var STD_BISECT_TOL      = 1e-4;    // N·m² convergence tolerance
                var STD_MAX_ITER        = 60;

                // scaleDelta: scale the EI target delta (same pattern as DELTA solver)
                var scaleFactor = 1.0;
                if (definition.scaleDelta == true)
                {
                    scaleFactor = definition.deltaScaleFactor;
                }
                var effectiveTargEI = measEI + scaleFactor * (targEI - measEI);

                // Bisection bounds on deltaT (m): negative drives down to min thickness
                var deltaT_min = STD_MIN_THICKNESS_M - t_old_m;
                var deltaT_max = STD_MAX_DELTA_M;

                // Extract section mesh data for CLT recompute
                var originalNA_m    = cs.neutralAxisY / meter;
                var csSectionPoints = cs.sectionPoints;
                var csBodyData      = cs.bodyData;

                // Evaluate EI at the two bisection bounds
                var result_min = computeEIFromShiftedPoints(csSectionPoints, originalNA_m, deltaT_min, csBodyData, bodies);
                var result_max = computeEIFromShiftedPoints(csSectionPoints, originalNA_m, deltaT_max, csBodyData, bodies);

                var EI_min = measEI;
                if (result_min != undefined)
                {
                    EI_min = result_min.EI_Nm2;
                }
                var EI_max = measEI;
                if (result_max != undefined)
                {
                    EI_max = result_max.EI_Nm2;
                }

                var deltaT_final = 0.0;

                if (effectiveTargEI <= EI_min)
                {
                    // Target below minimum-thickness EI — clamp to lower bound
                    deltaT_final = deltaT_min;
                    solvedEI     = EI_min;
                    println("WARNING STD [" ~ toString(xCoord / millimeter) ~ "mm]: target EI below min-thickness bound, clamping to EI=" ~ EI_min ~ " N·m²");
                }
                else if (effectiveTargEI >= EI_max)
                {
                    // Target above maximum-delta EI — clamp to upper bound
                    deltaT_final = deltaT_max;
                    solvedEI     = EI_max;
                    println("WARNING STD [" ~ toString(xCoord / millimeter) ~ "mm]: target EI above max-delta bound, clamping to EI=" ~ EI_max ~ " N·m²");
                }
                else
                {
                    // Bisect to find the deltaT that yields effectiveTargEI
                    var lo     = deltaT_min;
                    var hi     = deltaT_max;
                    var mid    = (lo + hi) / 2;
                    var EI_mid = (EI_min + EI_max) / 2;  // initial estimate
                    var iter   = 0;

                    while (iter < STD_MAX_ITER)
                    {
                        mid = (lo + hi) / 2;
                        var r_mid = computeEIFromShiftedPoints(csSectionPoints, originalNA_m, mid, csBodyData, bodies);
                        if (r_mid != undefined)
                        {
                            EI_mid = r_mid.EI_Nm2;
                        }
                        if (abs(EI_mid - effectiveTargEI) < STD_BISECT_TOL || abs(hi - lo) < 1e-9)
                        {
                            break;
                        }
                        if (EI_mid < effectiveTargEI)
                        {
                            lo = mid;
                        }
                        else
                        {
                            hi = mid;
                        }
                        iter += 1;
                    }

                    deltaT_final = mid;
                    solvedEI     = EI_mid;
                }

                t_new_m = t_old_m + deltaT_final;
            }
            else if (definition.solverType == SolverType.DELTA)
            {
                // DELTA: newEI = measuredEI + scaleFactor * delta
                // Solve for t: EI/b = alpha * t^beta  →  t = (newEI/(alpha*b))^(1/beta)
                var scaleFactor = 1.0;
                if (definition.overridePercentageDelta == true)
                {
                    scaleFactor = definition.applyDeltaPercentage;
                }
                var newEI = measEI + scaleFactor * (targEI - measEI);
                solvedEI = newEI;
                if (newEI > 0 && liveAlpha > 0 && abs(liveBeta) > 1e-6)
                {
                    var eiPerB = newEI / (liveAlpha * b_m);
                    if (eiPerB > 0)
                    {
                        t_new_m = eiPerB ^ (1.0 / liveBeta);
                    }
                }
            }
            else // SolverType.PERCENT
            {
                // PERCENT: pctChange = targetEI / measuredEI
                // t_new^beta = pctChange * t_old^beta  →  t_new = (pctChange * t_old^beta)^(1/beta)
                var newEI = targEI;  // pctChange * measEI = targEI algebraically
                solvedEI = newEI;
                if (measEI > 0 && t_old_m > 0 && abs(liveBeta) > 1e-6)
                {
                    var pctChange = targEI / measEI;
                    var betaInv   = 1.0 / liveBeta;
                    t_new_m = (pctChange * (t_old_m ^ liveBeta)) ^ betaInv;
                }
            }

            // Guard: t_new must be physically plausible (0.1mm to 200mm)
            var T_MIN_M = 0.0001;
            var T_MAX_M = 0.200;
            if (t_new_m < T_MIN_M || t_new_m > T_MAX_M)
            {
                println("WARNING [" ~ toString(xCoord / millimeter) ~ "mm]: t_new_m=" ~ t_new_m ~ " out of range, clamping to t_old_m=" ~ t_old_m);
                t_new_m = t_old_m;
            }

            tableRows = append(tableRows, {
                "xMm"     : round(xCoord / (1 * millimeter) * 10) / 10,
                "tMeasMm" : round(t_old_m * 1000 * 100) / 100,
                "EIMeas"  : round(measEI * 100) / 100,
                "tUpdMm"  : round(t_new_m * 1000 * 100) / 100,
                "EIUpd"   : round(solvedEI * 100) / 100
            });

            // Build 3D output point: world X = xCoord, Y = 0, Z = new thickness
            outputPoints = append(outputPoints, vector(xCoord, 0 * meter, t_new_m * meter));
        }

        // --- 8. Fit spline through output points and name the resulting body ---
        if (size(outputPoints) >= 2)
        {
            try
            {
                opFitSpline(context, id + "thicknessProfile", {
                    "points" : outputPoints
                });

                var createdBodies = evaluateQuery(context, qCreatedBy(id + "thicknessProfile", EntityType.BODY));
                if (size(createdBodies) > 0)
                {
                    setProperty(context, {
                        "entities"     : createdBodies[0],
                        "propertyType" : PropertyType.NAME,
                        "value"        : definition.outputCurveName
                    });
                }
            }
            catch (e)
            {
                println("ERROR: updateProfile opFitSpline INVALID_RESULT - " ~ e);
                println("  outputPoints count = " ~ size(outputPoints));

                // Print each point (X in mm, Z in mm — Y is always 0)
                for (var i = 0; i < size(outputPoints); i += 1)
                {
                    var pt = outputPoints[i];
                    var x_mm = toString(pt[0] / millimeter);
                    var z_mm = toString(pt[2] / millimeter);
                    println("  [" ~ i ~ "] X=" ~ x_mm ~ " mm  Z=" ~ z_mm ~ " mm");
                }

                // Draw debug polyline so we can see the point sequence in-canvas
                for (var i = 0; i < size(outputPoints) - 1; i += 1)
                {
                    addDebugLine(context, outputPoints[i], outputPoints[i + 1], DebugColor.RED);
                }

                // Draw a point at each candidate location
                for (var i = 0; i < size(outputPoints); i += 1)
                {
                    addDebugPoint(context, outputPoints[i], DebugColor.MAGENTA);
                }
            }
        }

        // --- 9. Persist per-station results as profileUpdates attribute ---
        var existingUpdates = getAttribute(context, {
            "entity" : qOrigin(EntityType.BODY),
            "name"   : "profileUpdates"
        });
        if (existingUpdates == undefined)
            existingUpdates = {};

        existingUpdates[toAttributeId(id)] = {
            "updateName" : definition.outputCurveName,
            "tableData"  : tableRows
        };

        setAttribute(context, {
            "entities"  : qOrigin(EntityType.BODY),
            "name"      : "profileUpdates",
            "attribute" : existingUpdates
        });
    });
