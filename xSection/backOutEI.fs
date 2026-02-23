FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: estimateDeflection.fs   (for LoadType, LocationType, RegionType, ApproxDegreeBounds,
//                                   MaxControlPointsBounds, ApproxToleranceBounds when integrating)

/**
 * backOutEI — EI Back-Out Development Workspace
 * ===============================================
 * Contains the EI-back-out / curvature-editing extension for the Estimate Deflection feature.
 * Code lives here while under development; re-integrate into estimateDeflection.fs when ready.
 *
 * Re-integration guide:
 *   1. Import this file from estimateDeflection.fs (add // IMPORT: backOutEI.fs)
 *   2. PRECONDITION: add `definition satisfies backOutEIPredicate;` at the end of the precondition
 *   3. EDITING LOGIC: replace the commented-out /* if (definition.backOutEI) */ block with
 *                     a call to `definition = applyBackOutEIEditLogic(definition, oldDefinition);`
 *                     Also restore the backwards-compat init block above it.
 *   4. FEATURE ANNOTATION: add `"Manipulator Change Function" : "estimateDeflectionManipulatorChange"`
 *   5. FEATURE BODY: un-comment the `/* if (definition.backOutEI && ...) */` block (step 11c)
 */

// =============================================================================
// TYPES AND BOUNDS
// (These are also declared in estimateDeflection.fs — resolve duplicates on integration)
// =============================================================================

export enum RegionType
{
    APPROXIMATE,
    BRIDGING,
    FREE_DRAG
}

export const ApproxToleranceBounds  = { (meter) : [1e-7, 1e-4, 1e-2] } as LengthBoundSpec;
export const MaxControlPointsBounds = { (unitless) : [4, 50, 500] } as IntegerBoundSpec;
export const ApproxDegreeBounds     = { (unitless) : [1, 3, 9] } as IntegerBoundSpec;

// =============================================================================
// PRECONDITION
// Add to estimateDeflection.fs precondition: definition satisfies backOutEIPredicate;
// =============================================================================

export predicate backOutEIPredicate(definition is map)
{
    annotation { "Name" : "Back out EI", "Default" : false }
    definition.backOutEI is boolean;

    // Hidden flat CP storage — unconditional so Onshape always initializes to [] on passive regens
    annotation { "Name" : "allCpX", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "CpX" }
    definition.allCpX is array;
    for (var cpX in definition.allCpX)
    {
        annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 0 }
        isReal(cpX.v, { (unitless) : [-1e6, 0, 1e6] } as RealBoundSpec);
    }

    annotation { "Name" : "allCpZ", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "CpZ" }
    definition.allCpZ is array;
    for (var cpZ in definition.allCpZ)
    {
        annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 0 }
        isReal(cpZ.v, { (unitless) : [-1e6, 0, 1e6] } as RealBoundSpec);
    }

    annotation { "Name" : "cpRegionSizes", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "Sz" }
    definition.cpRegionSizes is array;
    for (var sz in definition.cpRegionSizes)
    {
        annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 0 }
        isInteger(sz.v, { (unitless) : [0, 0, 500] } as IntegerBoundSpec);
    }

    annotation { "Name" : "cpIsInitialized", "UIHint" : UIHint.ALWAYS_HIDDEN, "Item name" : "Init" }
    definition.cpIsInitialized is array;
    for (var init in definition.cpIsInitialized)
    {
        annotation { "Name" : "v", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        init.v is boolean;
    }

    if (definition.backOutEI)
    {
        annotation { "Group Name" : "EI Back-Out", "Driving Parameter" : "backOutEI",
                     "Collapsed By Default" : false }
        {
            annotation { "Name" : "Trim Boundaries", "Item name" : "Boundary",
                         "Show labels" : true, "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
            definition.trimBoundaries is array;
            for (var boundary in definition.trimBoundaries)
            {
                annotation { "Name" : "Label" }
                boundary.boundaryName is string;

                annotation { "Name" : "X" }
                isLength(boundary.boundaryX, LENGTH_BOUNDS);
            }

            annotation { "Name" : "Regions", "Item name" : "Region",
                         "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
            definition.regionParams is array;
            for (var region in definition.regionParams)
            {
                annotation { "Name" : "Type" }
                region.regionType is RegionType;

                if (region.regionType != RegionType.BRIDGING)
                {
                    annotation { "Name" : "Degree" }
                    isInteger(region.approxDegree, ApproxDegreeBounds);

                    annotation { "Name" : "Max CPs" }
                    isInteger(region.maxCP, { (unitless) : [4, 20, 500] } as IntegerBoundSpec);
                }

                if (region.regionType == RegionType.APPROXIMATE)
                {
                    annotation { "Name" : "Tolerance" }
                    isLength(region.regionTolerance, ApproxToleranceBounds);
                }
            }

            annotation { "Name" : "storedScaleK", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : 1e-3 }
            isReal(definition.storedScaleK, { (unitless) : [0, 1e-3, 1e12] } as RealBoundSpec);
        }
    }
}

// =============================================================================
// EDITING LOGIC
// Backwards-compat init (add to top of estimateDeflectionEditLogic):
//   if (definition.backOutEI == undefined)      { definition.backOutEI = false; }
//   if (definition.trimBoundaries == undefined) { definition.trimBoundaries = []; }
//   if (definition.regionParams == undefined)   { definition.regionParams = []; }
//   if (definition.allCpX == undefined)         { definition.allCpX = []; }
//   if (definition.allCpZ == undefined)         { definition.allCpZ = []; }
//   if (definition.cpRegionSizes == undefined)  { definition.cpRegionSizes = []; }
//   if (definition.cpIsInitialized == undefined){ definition.cpIsInitialized = []; }
//   if (definition.storedScaleK == undefined)   { definition.storedScaleK = 1e-3; }
//
// Then replace the commented-out if(backOutEI) block with:
//   definition = applyBackOutEIEditLogic(definition, oldDefinition);
// =============================================================================

export function applyBackOutEIEditLogic(definition is map, oldDefinition is map) returns map
{
    if (!definition.backOutEI) { return definition; }

    // Initialize trimBoundaries if empty or undefined
    if (definition.trimBoundaries == undefined || size(definition.trimBoundaries) == 0)
    {
        var startX = 0 * meter;
        var endX = 1 * meter;
        // Use support positions if both are X_VAL type
        if (definition.support1LocationType == LocationType.X_VAL &&
            definition.support2LocationType == LocationType.X_VAL)
        {
            if (definition.support1X < definition.support2X)
            {
                startX = definition.support1X;
                endX = definition.support2X;
            }
            else
            {
                startX = definition.support2X;
                endX = definition.support1X;
            }
        }
        definition.trimBoundaries = [
            { "boundaryName" : "Start", "boundaryX" : startX },
            { "boundaryName" : "End",   "boundaryX" : endX }
        ];
    }

    // Sort trimBoundaries by boundaryX ascending (insertion sort)
    for (var i = 1; i < size(definition.trimBoundaries); i += 1)
    {
        var key = definition.trimBoundaries[i];
        var j = i - 1;
        while (j >= 0 && definition.trimBoundaries[j].boundaryX > key.boundaryX)
        {
            definition.trimBoundaries[j + 1] = definition.trimBoundaries[j];
            j -= 1;
        }
        definition.trimBoundaries[j + 1] = key;
    }

    // Sync regionParams length to trimBoundaries.length - 1
    var nRegions = size(definition.trimBoundaries) - 1;
    if (nRegions < 0) { nRegions = 0; }
    if (definition.regionParams == undefined) { definition.regionParams = []; }
    var currentSize = size(definition.regionParams);

    // Add default entries for new regions
    for (var k = currentSize; k < nRegions; k += 1)
    {
        definition.regionParams = append(definition.regionParams, {
            "regionType"     : RegionType.APPROXIMATE,
            "approxDegree"   : 3,
            "maxCP"          : 20,
            "regionTolerance": 1e-4 * meter
        });
    }

    // Remove extra entries if boundaries were deleted
    if (currentSize > nRegions)
    {
        var trimmedParams = [];
        for (var k = 0; k < nRegions; k += 1)
        {
            trimmedParams = append(trimmedParams, definition.regionParams[k]);
        }
        definition.regionParams = trimmedParams;
    }

    // Validate: first and last regions cannot be BRIDGING
    var nR = size(definition.regionParams);
    if (nR > 0)
    {
        if (definition.regionParams[0].regionType == RegionType.BRIDGING)
        {
            var r0 = definition.regionParams[0];
            r0.regionType = RegionType.APPROXIMATE;
            definition.regionParams[0] = r0;
            println("Warning: First region cannot be BRIDGING. Reverted to APPROXIMATE.");
        }
        if (nR > 1 && definition.regionParams[nR - 1].regionType == RegionType.BRIDGING)
        {
            var rLast = definition.regionParams[nR - 1];
            rLast.regionType = RegionType.APPROXIMATE;
            definition.regionParams[nR - 1] = rLast;
            println("Warning: Last region cannot be BRIDGING. Reverted to APPROXIMATE.");
        }
    }

    // Reset cpIsInitialized for regions whose fit parameters changed
    var nRCheck = size(definition.regionParams);
    if (size(definition.cpIsInitialized) == nRCheck &&
        oldDefinition.regionParams != undefined &&
        size(oldDefinition.regionParams) == nRCheck)
    {
        for (var k = 0; k < nRCheck; k += 1)
        {
            var oldR = oldDefinition.regionParams[k];
            var newR = definition.regionParams[k];
            if (oldR.approxDegree    != newR.approxDegree   ||
                oldR.maxCP           != newR.maxCP           ||
                oldR.regionTolerance != newR.regionTolerance ||
                oldR.regionType      != newR.regionType)
            {
                definition.cpIsInitialized[k] = { "v" : false };
            }
        }
    }
    // If region count changed, reset all
    if (size(definition.cpIsInitialized) != nRCheck)
    {
        var resetInit = [];
        for (var k = 0; k < nRCheck; k += 1)
        {
            resetInit = append(resetInit, { "v" : false });
        }
        definition.cpIsInitialized = resetInit;
    }

    return definition;
}

// =============================================================================
// MANIPULATOR CHANGE FUNCTION
// Restore in estimateDeflection.fs feature annotation:
//   "Manipulator Change Function" : "estimateDeflectionManipulatorChange"
// =============================================================================

export function estimateDeflectionManipulatorChange(
    context is Context, definition is map, newManipulators is map) returns map
{
    if (!definition.backOutEI)                 { return definition; }
    if (definition.cpRegionSizes == undefined) { return definition; }

    var nReg = size(definition.regionParams);
    var tempCpZ = definition.allCpZ;          // value copy for mutation
    var scaleKVal = definition.storedScaleK;  // pure number = scaleK / (m²)

    var cpFlatOff = 0;
    for (var ri = 0; ri < nReg; ri += 1)
    {
        var sz = definition.cpRegionSizes[ri].v;
        for (var j = 0; j < sz; j += 1)
        {
            var key = "r" ~ toString(ri) ~ "c" ~ toString(j);
            if (newManipulators[key] is map)
            {
                // offset [m] / (scaleKVal [m²/m²] * m²) → kappa [1/m] → cpZ (= kappa * meter, dimensionless)
                var newKappa = newManipulators[key].offset / (scaleKVal * meter * meter);
                tempCpZ[cpFlatOff + j] = { "v" : newKappa * meter };
                // Mark region as initialized so feature body uses stored CPs
                definition.cpIsInitialized[ri] = { "v" : true };
            }
        }
        cpFlatOff = cpFlatOff + sz;
    }
    definition.allCpZ = tempCpZ;
    return definition;
}

// =============================================================================
// FEATURE BODY — STEP 11c
// Paste into estimateDeflection.fs feature body after the scaleK computation,
// replacing the commented-out /* if (definition.backOutEI && ...) */ block.
//
// Variables assumed in scope from the surrounding feature body:
//   definition, N, x_eval, kappa_arr, M_arr, scaleK, xEvalMin, xEvalMax
// =============================================================================

/*  ---- PASTE INTO FEATURE BODY (step 11c) ----

        if (definition.backOutEI && size(definition.trimBoundaries) >= 2)
        {
            // Step A: Resolve & clamp trim boundaries
            var xBounds = [];
            for (var boundary in definition.trimBoundaries)
            {
                var bx = boundary.boundaryX;
                if (bx < xEvalMin) { bx = xEvalMin; }
                if (bx > xEvalMax) { bx = xEvalMax; }
                xBounds = append(xBounds, bx);
            }

            // Step B: Per-region spline fitting or recovery from stored CPs
            var nReg = size(definition.regionParams);
            var newAllCpX = [];
            var newAllCpZ = [];
            var newCpSizes = [];
            var newCpInit = [];

            for (var ri = 0; ri < nReg; ri += 1)
            {
                var region = definition.regionParams[ri];
                var xLo = xBounds[ri];
                var xHi = xBounds[ri + 1];

                var isInit = (ri < size(definition.cpIsInitialized) &&
                              definition.cpIsInitialized[ri].v == true);

                var regionCpX = [];
                var regionCpZ = [];

                if (isInit)
                {
                    // Recover stored CPs from flat arrays
                    var flatOff = 0;
                    for (var k = 0; k < ri; k += 1)
                    {
                        flatOff = flatOff + definition.cpRegionSizes[k].v;
                    }
                    var sz = definition.cpRegionSizes[ri].v;
                    for (var k = 0; k < sz; k += 1)
                    {
                        regionCpX = append(regionCpX, definition.allCpX[flatOff + k].v);
                        regionCpZ = append(regionCpZ, definition.allCpZ[flatOff + k].v);
                    }
                }
                else
                {
                    // Collect kappa sample points in this X range
                    var regionPts = [];
                    for (var j = 0; j < N; j += 1)
                    {
                        if (x_eval[j] >= xLo && x_eval[j] <= xHi)
                        {
                            regionPts = append(regionPts, vector(x_eval[j], 0 * meter, kappa_arr[j] * scaleK));
                        }
                    }

                    if (size(regionPts) >= 2 && region.regionType != RegionType.BRIDGING)
                    {
                        var fitTol = (region.regionType == RegionType.FREE_DRAG)
                                     ? 1e-7 * meter
                                     : region.regionTolerance;
                        var fitResult = approximateSpline(context, {
                            "degree"           : region.approxDegree,
                            "tolerance"        : fitTol,
                            "isPeriodic"       : false,
                            "targets"          : [{ "positions" : regionPts }],
                            "maxControlPoints" : region.maxCP
                        });
                        var spline = fitResult[0];
                        for (var cp in spline.controlPoints)
                        {
                            regionCpX = append(regionCpX, cp[0] / meter);
                            // cpZ = kappa * meter (dimensionless); kappa = cp[2] / scaleK [1/m]
                            regionCpZ = append(regionCpZ, (cp[2] / scaleK) * meter);
                        }
                    }
                }

                // Accumulate into flat arrays
                for (var k = 0; k < size(regionCpX); k += 1)
                {
                    newAllCpX = append(newAllCpX, { "v" : regionCpX[k] });
                    newAllCpZ = append(newAllCpZ, { "v" : regionCpZ[k] });
                }
                newCpSizes = append(newCpSizes, { "v" : size(regionCpX) });
                newCpInit  = append(newCpInit,  { "v" : size(regionCpX) > 0 });
            }

            // Persist fitted CPs to hidden definition parameters
            definition.allCpX         = newAllCpX;
            definition.allCpZ         = newAllCpZ;
            definition.cpRegionSizes  = newCpSizes;
            definition.cpIsInitialized = newCpInit;
            definition.storedScaleK   = scaleK / (meter * meter);

            // Step D: Add manipulators (one per CP, Z-direction drag)
            var allManipulators = {};
            var cpFlatOff = 0;
            for (var ri = 0; ri < nReg; ri += 1)
            {
                var sz = newCpSizes[ri].v;
                for (var j = 0; j < sz; j += 1)
                {
                    var key = "r" ~ toString(ri) ~ "c" ~ toString(j);
                    allManipulators[key] = linearManipulator({
                        "base"      : vector(newAllCpX[cpFlatOff + j].v * meter, 0 * meter, 0 * meter),
                        "direction" : vector(0, 0, 1),
                        "offset"    : newAllCpZ[cpFlatOff + j].v * definition.storedScaleK * meter
                    });
                }
                cpFlatOff = cpFlatOff + sz;
            }
            addManipulators(context, id, allManipulators);

            // Step E: Evaluate kappa_cleaned by linear interpolation over CP polyline per region
            var kappa_cleaned = [];
            for (var j = 0; j < N; j += 1)
            {
                var xi = x_eval[j];
                var kappa_i = kappa_arr[j];   // default: raw kappa
                var foundRegion = false;
                var flatOff2 = 0;
                for (var ri = 0; ri < nReg; ri += 1)
                {
                    var sz2 = newCpSizes[ri].v;
                    if (!foundRegion && xi >= xBounds[ri] && xi <= xBounds[ri + 1] && sz2 >= 2)
                    {
                        var foundSpan = false;
                        for (var k = 0; k < sz2 - 1; k += 1)
                        {
                            if (!foundSpan)
                            {
                                var x0 = newAllCpX[flatOff2 + k].v     * meter;
                                var x1 = newAllCpX[flatOff2 + k + 1].v * meter;
                                if (xi >= x0 && xi <= x1)
                                {
                                    var span = x1 - x0;
                                    if (abs(span) > 1e-12 * meter)
                                    {
                                        var t = (xi - x0) / span;
                                        var z0 = newAllCpZ[flatOff2 + k].v     / meter;  // kappa [1/m]
                                        var z1 = newAllCpZ[flatOff2 + k + 1].v / meter;
                                        kappa_i = z0 + t * (z1 - z0);
                                    }
                                    foundSpan = true;
                                }
                            }
                        }
                        foundRegion = true;
                    }
                    flatOff2 = flatOff2 + sz2;
                }
                kappa_cleaned = append(kappa_cleaned, kappa_i);
            }

            // Step F: EI back-out  EI = M / kappa_cleaned
            var EI_backout = [];
            for (var j = 0; j < N; j += 1)
            {
                var ki = kappa_cleaned[j];
                var EI_j = (abs(ki) > 1e-6 / meter)
                           ? M_arr[j] / ki
                           : 0 * newton * meter * meter;
                EI_backout = append(EI_backout, EI_j);
            }

            // Step G: Output edge — Z = EI [N·m²] encoded as Z [mm], matching selEI format
            var eiPts = [];
            for (var j = 0; j < N; j += 1)
            {
                eiPts = append(eiPts, vector(
                    x_eval[j],
                    0 * meter,
                    (EI_backout[j] / (newton * meter * meter)) * millimeter
                ));
            }
            opFitSpline(context, id + "eiBackout", { "points" : eiPts });
        }

---- END PASTE ----  */
