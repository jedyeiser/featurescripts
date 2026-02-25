FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// xSectMaterials (for tryGetKey helper)
import(path : "f8e590162884d45f56e0a05f", version : "e5392c408679921c0a537da3");

// xSectLanguage (translation lookups)
import(path : "a0fab52ee4d0b16ffbc1c603", version : "946ae802f4abcbcd6592484c");

// =============================================================================
// DISPLAY ROUNDING CONSTANTS
// =============================================================================

/**
 * Rounding precision for stiffness values in display tables.
 * Units: lb/in (imperial) and mm/30kg (metric)
 * Value chosen to show meaningful variation while avoiding false precision.
 */
const STIFFNESS_ROUNDING_PRECISION = 0.05;

/**
 * Rounding precision for stiffness in metric units (mm deflection).
 * Units: mm/30kg
 * Slightly coarser than imperial due to typical measurement resolution.
 */
const STIFFNESS_ROUNDING_PRECISION_METRIC = 0.1;

/**
 * Rounding precision for mass/weight display.
 * Units: kg
 * Chosen for typical manufacturing weighing precision (10g resolution).
 */
const WEIGHT_ROUNDING_PRECISION = 0.01;


/**
 * XSECTION STORAGE MODULE
 * ========================
 *
 * Data persistence and table formatting for cross-section analysis.
 *
 * Handles:
 * - Storing analysis results as attributes on origin point
 * - Building formatted table data for export
 * - Value rounding for display
 *
 * Extracted from xSect.fs (lines 795-976) to separate data management from analysis logic.
 */

/**
 * Store analysis data as attribute on origin point.
 *
 * @param context {Context}
 * @param id {Id} : Feature ID
 * @param definition {map} : Feature definition (for feature name)
 * @param bodies {array} : Body configuration data
 * @param crossSectionData {map} : Full cross-section analysis results
 * @param beamAnalysis {map} : Beam stiffness results (or undefined)
 * @param tableData {map} : Formatted table data
 * @param massData {map} : Volume-based mass calculations
 */
export function storeAnalysisData(context is Context, id is Id, definition is map, bodies is array,
                                  crossSectionData is map, beamAnalysis, tableData is map, massData is map)
{
    // Build feature-specific data structure
    var featureKey = toAttributeId(id);

    // Extract body details (including mass from massData)
    var bodyDetails = [];
    for (var i = 0; i < size(bodies); i += 1)
    {
        var bodyEntry = bodies[i];
        var bodyMass = massData.bodyMasses[i];

        var bodyDetail = {
            "bodyIndex" : bodyEntry.bodyIdx,
            "bodyIdx" : bodyEntry.bodyIdx,
            "hasMaterialData" : bodyEntry.hasMaterialData == true,
            "bodyName" : bodyEntry.bodyName,
            "materialName" : bodyEntry.materialName,
            "materialData" : bodyEntry.materialData,
            "volume" : bodyEntry.volume,
            "mass" : bodyMass.mass
        };
        bodyDetails = append(bodyDetails, bodyDetail);
    }

    // Extract cross-section details
    var sectionDetails = [];
    for (var i = 0; i < size(crossSectionData.crossSections); i += 1)
    {
        var section = crossSectionData.crossSections[i];

        // Sum lineal density
        var linealDensity = 0 * kilogram / meter;
        for (var contrib in section.mechanicalProperties.bodyContributions)
        {
            if (contrib.linearDensity != undefined)
            {
                linealDensity = linealDensity + contrib.linearDensity;
            }
        }

        var sectionDetail = {
            "index" : i,
            "stationNumber" : section.stationNumber,
            "xCoord" : section.frame.origin[0],
            "frame" : section.frame,
            "EI_eff" : section.mechanicalProperties.EI_eff,
            "GJ_eff" : section.mechanicalProperties.GJ_eff,
            "neutralAxisY" : section.mechanicalProperties.neutralAxisY,
            "boundingBox" : section.boundingBox,
            "linealDensity" : linealDensity,
            "mechanicalProperties" : {
                "A" : section.mechanicalProperties.A,
                "B" : section.mechanicalProperties.B,
                "D" : section.mechanicalProperties.D
            },
            "sectionPoints" : section.sectionPoints,  // For gjAnalysis feature
            "bodyData" : section.bodyData             // For gjAnalysis feature
        };
        sectionDetails = append(sectionDetails, sectionDetail);
    }

    // Get analysis name (if provided)
    var analysisName = "";
    if (definition.analysisName != undefined && definition.analysisName != "")
    {
        analysisName = definition.analysisName;
    }

    // Build complete data structure
    var analysisData = {
        "analysisName" : analysisName,
        "details" : {
            "bodies" : bodyDetails,
            "crossSections" : sectionDetails,
            "beamAnalysis" : beamAnalysis
        },
        "tableData" : tableData
    };

    try
    {
        // Retrieve existing attribute (if any)
        var existingData = getAttribute(context, {
            "entity" : qOrigin(EntityType.BODY),
            "name" : "CrossSectionAnalysis"
        });

        // Merge with existing data
        if (existingData == undefined)
        {
            existingData = {};
        }
        existingData[featureKey] = analysisData;

        // Store updated attribute
        setAttribute(context, {
            "entities" : qOrigin(EntityType.BODY),
            "name" : "CrossSectionAnalysis",
            "attribute" : existingData
        });

    }
    catch (e)
    {
        println("WARNING: Failed to store analysis data on origin - " ~ e);
    }
}

/**
 * Round a number to specified precision for table display.
 * E.g., roundValue(1.234, 0.05) = 1.25
 * Eliminates floating point artifacts by rounding to 8 decimal places.
 */
export function roundValue(value is number, precision is number) returns number
{
    var rounded = round(value / precision) * precision;
    // Eliminate floating point artifacts
    return round(rounded * 1e8) / 1e8;
}

/**
 * Build formatted table data for export.
 *
 * @param crossSections {array} : Cross-section data with mechanical properties
 * @param beamAnalysis {map} : Beam stiffness results (or undefined if not computed)
 * @param totalWeight {ValueWithUnits} : Total beam weight
 * @param language {LANGUAGE} : Language for table headers and metrics
 * @returns {map} : { summary: array, crossSections: array }
 */
export function buildTableData(crossSections is array, beamAnalysis, totalWeight is ValueWithUnits, language is LANGUAGE) returns map
{
    // Get translation lookups for this language
    var summaryLookup = overallAnalysisTranslationLookup[language];
    var headerLookup = mainTableHeaderTranslationLookup[language];

    // Summary table
    var summaryTable = [];

    if (beamAnalysis != undefined)
    {
        var prisLbIn = roundValue(beamAnalysis.prismaticStiffness_lbin, STIFFNESS_ROUNDING_PRECISION);
        var prisMm = roundValue(beamAnalysis.prismaticStiffness_mm, STIFFNESS_ROUNDING_PRECISION_METRIC);
        var estLbIn = roundValue(beamAnalysis.estimatedStiffness_lbin, STIFFNESS_ROUNDING_PRECISION);
        var estMm = roundValue(beamAnalysis.estimatedStiffness_mm, STIFFNESS_ROUNDING_PRECISION_METRIC);

        summaryTable = append(summaryTable, [summaryLookup["Prismatic stiffness (lb/in)"], prisLbIn]);
        summaryTable = append(summaryTable, [summaryLookup["Prismatic stiffness (mm/30kg)"], prisMm]);
        summaryTable = append(summaryTable, [summaryLookup["Estimated stiffness (lb/in)"], estLbIn]);
        summaryTable = append(summaryTable, [summaryLookup["Estimated stiffness (mm/30kg)"], estMm]);
    }

    var weightKg = roundValue(totalWeight / kilogram, WEIGHT_ROUNDING_PRECISION);
    summaryTable = append(summaryTable, [summaryLookup["Weight (kg)"], weightKg]);

    // Cross-section table header (apply translations)
    var csTable = [
        [
            headerLookup["Station"],
            headerLookup["X"],
            headerLookup["EI"],
            headerLookup["GJ"],
            headerLookup["NA Height"],
            headerLookup["NA Percentage"],
            headerLookup["Beam Width"],
            headerLookup["Beam Height"],
            headerLookup["Lineal Density"]
        ]
    ];

    // Add data rows
    for (var i = 0; i < size(crossSections); i += 1)
    {
        var section = crossSections[i];
        var stationNum = section.stationNumber;  // Extract station number from section data
        var xCoord = section.frame.origin[0] / millimeter;  // World X in mm
        var EI = section.mechanicalProperties.EI_eff / (newton * meter * meter);
        var GJ = section.mechanicalProperties.GJ_eff / (newton * meter * meter);
        var naHeight = section.mechanicalProperties.neutralAxisY / millimeter;

        // NOTE: boundingBox dimensions are in plane-local coordinates (2D cross-section).
        //       boundingBox.width = horizontal extent in plane = BEAM HEIGHT (vertical in world)
        //       boundingBox.height = vertical extent in plane = BEAM WIDTH (horizontal in world)
        //       We swap them here so the table displays physical beam dimensions correctly.
        var beamHeight = section.boundingBox.width / millimeter;   // Vertical dimension (thickness)
        var beamWidth = section.boundingBox.height / millimeter;   // Horizontal dimension (width)

        // Sum lineal density across all bodies
        var linealDensity = 0 * kilogram / meter;
        for (var contrib in section.mechanicalProperties.bodyContributions)
        {
            if (contrib.linearDensity != undefined)
            {
                linealDensity = linealDensity + contrib.linearDensity;
            }
        }
        var linealDensityVal = linealDensity / (kilogram / meter);

        // Apply rounding
        xCoord = roundValue(xCoord, 0.05);           // 0.05mm precision
        EI = roundValue(EI, 0.1);                    // 0.1 N·m² precision
        GJ = roundValue(GJ, 0.1);                    // 0.1 N·m² precision
        naHeight = roundValue(naHeight, 0.05);       // 0.05mm precision
        beamHeight = roundValue(beamHeight, 0.05);   // 0.05mm precision
        beamWidth = roundValue(beamWidth, 0.05);     // 0.05mm precision
        linealDensityVal = roundValue(linealDensityVal, 0.01);  // 0.01 kg/m precision

        // Calculate NA as percentage of beam height (after sign flip and rounding)
        var naPercentage = 0;
        if (beamHeight > 0.05)  // Guard: only compute if beam height > tolerance
        {
            naPercentage = roundValue((naHeight / beamHeight) * 100, 0.1);  // 0.1% precision
        }

        csTable = append(csTable, [stationNum, xCoord, EI, GJ, naHeight, naPercentage, beamWidth, beamHeight, linealDensityVal]);
    }

    return {
        "summary" : summaryTable,
        "crossSections" : csTable
    };
}


// =============================================================================
// MATERIAL TABLE
// =============================================================================

/**
 * Build material table data — one row per unique material in first-seen order.
 *
 * @param bodies {array} : Bodies array from crossSectionData (each entry has
 *                          hasMaterialData, materialData fields).
 * @returns {map} : { "rows": [...] }
 *
 * Each row:
 * {
 *   "id": number,       // 1-indexed, order of first appearance
 *   "name": string,
 *   "category": string,
 *   "density": number,  // kg/m³
 *   "E": number,        // GPa
 *   "nu": number,
 *   "Q11": number,      // GPa
 *   "Q22": number,
 *   "Q12": number,
 *   "Q66": number,
 *   "Q16": number,
 *   "Q26": number,
 * }
 */
export function buildMaterialTableData(bodies is array) returns map
{
    var rows = [];
    var seenNames = [];
    var nextId = 1;

    for (var body in bodies)
    {
        if (body.hasMaterialData != true)
        {
            continue;
        }

        var matData = body.materialData;
        if (matData == undefined)
        {
            continue;
        }

        var name = matData.originalName;
        if (name == undefined)
        {
            name = body.materialName;
        }
        if (name == undefined)
        {
            continue;
        }

        // Deduplicate by name
        if (isIn(name, seenNames))
        {
            continue;
        }
        seenNames = append(seenNames, name);

        // Extract category (fallback to empty string)
        var category = tryGetKey(matData, "category");
        if (category == undefined)
        {
            category = "";
        }

        // Density: strip units, round to 0.1 kg/m³
        var density = matData.density / (kilogram / meter^3);
        density = roundValue(density, 0.1);

        // Young's modulus: convert Pa → GPa, round to 0.001 GPa
        var E_GPa = matData.youngsModulus / (1e9 * pascal);
        E_GPa = roundValue(E_GPa, 0.001);

        // Poisson's ratio
        var nu = roundValue(matData.poissonsRatio, 0.001);

        // Q matrix entries: Pa → GPa, round to 0.001 GPa
        var Q = matData.qMatrix;
        var Q11 = roundValue(Q[0][0] / (1e9 * pascal), 0.001);
        var Q22 = roundValue(Q[1][1] / (1e9 * pascal), 0.001);
        var Q12 = roundValue(Q[0][1] / (1e9 * pascal), 0.001);
        var Q66 = roundValue(Q[2][2] / (1e9 * pascal), 0.001);
        var Q16 = roundValue(Q[0][2] / (1e9 * pascal), 0.001);
        var Q26 = roundValue(Q[1][2] / (1e9 * pascal), 0.001);

        rows = append(rows, {
            "id"       : nextId,
            "name"     : name,
            "category" : category,
            "density"  : density,
            "E"        : E_GPa,
            "nu"       : nu,
            "Q11"      : Q11,
            "Q22"      : Q22,
            "Q12"      : Q12,
            "Q66"      : Q66,
            "Q16"      : Q16,
            "Q26"      : Q26
        });

        nextId += 1;
    }

    return { "rows" : rows };
}


// =============================================================================
// BODY DETAIL TABLE
// =============================================================================

/**
 * Build body detail table data — one row per cross-section, per-body breakdown.
 *
 * For each section, uses the parallel-axis theorem to compute each body's
 * contribution to EI, then sums them to produce EI_sum. This gives a simple
 * parallel-axis estimate alongside the CLT-based EI_calc for comparison.
 *
 * Parallel-axis formula per body k:
 *   naHeight_m = section.mechanicalProperties.neutralAxisY     // positive = above base
 *   d_k        = centroid2D[0] - naHeight_m                    // signed distance, centroid to NA
 *   I_k_NA     = Iyy_centroid + area * d_k²
 *   EI_body_k  = Q11_k * I_k_NA                               // Q11 in Pa, result in N·m²
 *   EI_sum     = Σ EI_body_k (bodies with material only)
 *
 * @param crossSections {array} : crossSectionData.crossSections
 * @param bodies {array}        : crossSectionData.bodies
 * @returns {map} : { "bodyNames": [...], "rows": [...] }
 *
 * Per-body result (rows[i].bodies[k]):
 *   undefined                     → body not present at this section
 *   { "noMaterial": true }        → body present but lacks material
 *   { "area_mm2", "centroid_mm", "I_centroid_mm4", "EI_body", "pct",
 *     "noMaterial": false }        → full data
 */
export function buildBodyTableData(crossSections is array, bodies is array) returns map
{
    // Build ordered body name list (indexed by bodyIdx = array index in bodies)
    var bodyNames = [];
    for (var body in bodies)
    {
        bodyNames = append(bodyNames, body.bodyName);
    }

    var numBodies = size(bodies);
    var rows = [];

    for (var section in crossSections)
    {
        var mp = section.mechanicalProperties;

        // NA height: neutralAxisY is positive when above base
        var naHeight_m = mp.neutralAxisY / meter;

        // Build lookup of bodyData by bodyIdx for this section
        var bodyDataByIdx = {};
        for (var bd in section.bodyData)
        {
            bodyDataByIdx[toString(bd.bodyIdx)] = bd;
        }

        // --- Pass 1: compute geometry and EI_body for each body, accumulate EI_sum ---
        var bodyResults = [];
        var EI_sum_val = 0;

        for (var k = 0; k < numBodies; k += 1)
        {
            var bdMap = bodyDataByIdx[toString(k)];

            if (bdMap == undefined)
            {
                // Body not present at this section
                bodyResults = append(bodyResults, undefined);
                continue;
            }

            var props = bdMap.totalSectionProperties;
            var area = props.area;

            // Skip degenerate geometry
            if (area < 1e-12 * meter * meter)
            {
                bodyResults = append(bodyResults, undefined);
                continue;
            }

            var centroid2D = props.centroid2D;
            var yBar_k = centroid2D[0];        // height direction (frame X = world +Z = thickness)
            var Iyy_centroid = props.Iyy;      // second moment about bending axis at centroid

            // Parallel-axis shift from centroid to neutral axis
            var d_k = yBar_k / meter - naHeight_m;

            var body = bodies[k];
            var hasMat = body.hasMaterialData == true;

            if (!hasMat)
            {
                // Body present geometrically but no material — store geometry only
                bodyResults = append(bodyResults, {
                    "area"         : area,
                    "centroid_y"   : yBar_k,
                    "Iyy_centroid" : Iyy_centroid,
                    "d_k"          : d_k,
                    "noMaterial"   : true
                });
                continue;
            }

            var I_k_NA = Iyy_centroid / (meter^4) + (area / (meter * meter)) * d_k * d_k;

            // Q11 in Pa (strip units)
            var Q11_k = body.materialData.qMatrix[0][0] / pascal;

            var EI_body_k = Q11_k * I_k_NA;   // N·m²
            EI_sum_val = EI_sum_val + EI_body_k;

            bodyResults = append(bodyResults, {
                "area"         : area,
                "centroid_y"   : yBar_k,
                "Iyy_centroid" : Iyy_centroid,
                "d_k"          : d_k,
                "EI_body_raw"  : EI_body_k,
                "noMaterial"   : false
            });
        }

        // --- Pass 2: assign percentages, convert units ---
        var finalBodyResults = [];
        for (var k = 0; k < numBodies; k += 1)
        {
            var res = bodyResults[k];

            if (res == undefined)
            {
                finalBodyResults = append(finalBodyResults, undefined);
                continue;
            }

            var area_mm2 = roundValue(res.area / (millimeter * millimeter), 0.01);
            var centroid_mm = roundValue(res.centroid_y / millimeter, 0.01);
            var I_centroid_mm4 = roundValue(res.Iyy_centroid / (millimeter^4), 0.1);
            var centroid_above_na_mm = roundValue(res.d_k * 1000, 0.01);

            if (res.noMaterial == true)
            {
                finalBodyResults = append(finalBodyResults, {
                    "area_mm2"             : area_mm2,
                    "centroid_mm"          : centroid_mm,
                    "I_centroid_mm4"       : I_centroid_mm4,
                    "centroid_above_na_mm" : centroid_above_na_mm,
                    "noMaterial"           : true
                });
                continue;
            }

            var EI_body = roundValue(res.EI_body_raw, 0.01);

            var pct = 0;
            if (EI_sum_val > 0)
            {
                pct = roundValue(res.EI_body_raw / EI_sum_val * 100, 0.1);
            }

            finalBodyResults = append(finalBodyResults, {
                "area_mm2"             : area_mm2,
                "centroid_mm"          : centroid_mm,
                "I_centroid_mm4"       : I_centroid_mm4,
                "centroid_above_na_mm" : centroid_above_na_mm,
                "EI_body"              : EI_body,
                "pct"                  : pct,
                "noMaterial"           : false
            });
        }

        // --- Build row ---
        var EI_calc_val = roundValue(mp.EI_eff / (newton * meter * meter), 0.1);
        var NA_height_mm = roundValue(naHeight_m * 1000, 0.05);
        var x_mm = roundValue(section.frame.origin[0] / millimeter, 0.05);
        var EI_sum_rounded = roundValue(EI_sum_val, 0.01);

        rows = append(rows, {
            "station"      : section.stationNumber,
            "x_mm"         : x_mm,
            "EI_calc"      : EI_calc_val,
            "NA_height_mm" : NA_height_mm,
            "bodies"       : finalBodyResults,
            "EI_sum"       : EI_sum_rounded
        });
    }

    return {
        "bodyNames" : bodyNames,
        "rows"      : rows
    };
}
