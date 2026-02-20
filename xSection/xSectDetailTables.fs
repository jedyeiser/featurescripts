FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// IMPORT: xSectMaterials.fs
// IMPORT: xSectStorage.fs

/**
 * XSECTION DETAIL TABLES MODULE
 * ==============================
 *
 * Builds optional debug/export table data for the xSection analysis feature.
 *
 * Provides two exported functions:
 *   - buildMaterialTableData()  : One row per unique material, full Q-matrix.
 *   - buildBodyTableData()      : One row per cross-section, per-body breakdown.
 *
 * These are pure data transformation functions — no geometry operations.
 * Results are stored in tableData["materialTable"] and tableData["bodyTable"]
 * by xSect.fs, then rendered by xSectionAnalysisTable.fs.
 */

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
 *   naHeight_m = -(section.mechanicalProperties.neutralAxisY)  // positive = above base
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

        // NA height: neutralAxisY is negative when above base (by FeatureScript convention)
        // naHeight_m = positive physical height above base
        var naHeight_m = -(mp.neutralAxisY / meter);

        // Build lookup of bodyData by bodyIdx for this section
        var bodyDataByIdx = {};
        for (var bd in section.bodyData)
        {
            bodyDataByIdx[toString(bd.bodyIdx)] = bd;
        }

        // --- Pass 1: compute EI_body for each body, accumulate EI_sum ---
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

            var body = bodies[k];
            if (body.hasMaterialData != true)
            {
                // Body present geometrically but no material
                bodyResults = append(bodyResults, { "noMaterial" : true });
                continue;
            }

            var props = bdMap.totalSectionProperties;
            var area = props.area;

            // Skip degenerate geometry
            if (area < 1e-12 * meter * meter)
            {
                bodyResults = append(bodyResults, { "noMaterial" : true });
                continue;
            }

            var centroid2D = props.centroid2D;
            var yBar_k = centroid2D[0];        // height direction (frame X = world +Z = thickness)
            var Iyy_centroid = props.Iyy;      // second moment about bending axis at centroid

            // Parallel-axis shift from centroid to neutral axis
            var d_k = yBar_k / meter - naHeight_m;
            var I_k_NA = Iyy_centroid / (meter^4) + (area / (meter * meter)) * d_k * d_k;

            // Q11 in Pa (strip units)
            var Q11_k = body.materialData.qMatrix[0][0] / pascal;

            var EI_body_k = Q11_k * I_k_NA;   // N·m²
            EI_sum_val = EI_sum_val + EI_body_k;

            bodyResults = append(bodyResults, {
                "area"         : area,
                "centroid_y"   : yBar_k,
                "Iyy_centroid" : Iyy_centroid,
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

            if (res.noMaterial == true)
            {
                finalBodyResults = append(finalBodyResults, { "noMaterial" : true });
                continue;
            }

            var area_mm2 = roundValue(res.area / (millimeter * millimeter), 0.01);
            var centroid_mm = roundValue(res.centroid_y / millimeter, 0.01);
            var I_centroid_mm4 = roundValue(res.Iyy_centroid / (millimeter^4), 0.1);
            var EI_body = roundValue(res.EI_body_raw, 0.01);

            var pct = 0;
            if (EI_sum_val > 0)
            {
                pct = roundValue(res.EI_body_raw / EI_sum_val * 100, 0.1);
            }

            finalBodyResults = append(finalBodyResults, {
                "area_mm2"       : area_mm2,
                "centroid_mm"    : centroid_mm,
                "I_centroid_mm4" : I_centroid_mm4,
                "EI_body"        : EI_body,
                "pct"            : pct,
                "noMaterial"     : false
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
