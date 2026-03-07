FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * XSECTION MATERIALS MODULE
 * =========================
 *
 * Material data resolution and editing logic helpers for cross-section analysis.
 *
 * This module consolidates material-related logic:
 * - CSV parsing and material lookup (buildMaterialLookup)
 * - Material name normalization for matching
 * - Editing logic helpers (findOldBodyEntry, tryGetKey)
 * - Override material field preservation
 *
 * Extracted from xSect.fs and xSectCLT.fs to centralize material handling.
 */

// =============================================================================
// MATERIAL LIBRARY CSV PARSING
// =============================================================================

/**
 * Build a material lookup map from CSV data loaded via TableData.
 *
 * Parses the material library CSV and returns a map keyed by material Name
 * (string) → materialData map. This is the same materialData format that
 * the CLT assembly expects on each body.
 *
 * CSV Column Layout (from KaiTai_ski_snowboard_material_library.csv):
 *   0: Category
 *   1: Name
 *   2: Density [kg/m³]
 *   3: Poisson's Ratio
 *   4: Young's Modulus [GPa]
 *   5: Q11 [GPa]
 *   6: Q22 [GPa]
 *   7: Q12 [GPa]
 *   8: Q66 [GPa]
 *   9: Q16 [GPa]
 *  10: Q26 [GPa]
 *  11: CTE_x [1/K]
 *  12: CTE_y [1/K]
 *  13: Available dimensions (unused)
 *
 * Q matrix is stored as:
 *   [[Q11, Q12, Q16],
 *    [Q12, Q22, Q26],
 *    [Q16, Q26, Q66]]
 *
 * The lookup map is keyed by exact name match. Names must match exactly as they appear
 * in the CSV and in Onshape's material library (case-sensitive, whitespace-sensitive).
 *
 * Usage in editing logic:
 *   var lookup = buildMaterialLookup(definition.materialCSV.csvData);
 *   var match = lookup[onshapeMaterialName];  // exact match — case and whitespace sensitive
 *
 * @param csvData {array} : Array of row arrays from TableData.csvData.
 *                           Each row is an array of parsed values (numbers/strings).
 * @returns {map} : Normalized material name (string) → {
 *     originalName: string (preserves original casing for display),
 *     category: string,
 *     density: ValueWithUnits (kg/m³),
 *     poissonsRatio: number,
 *     youngsModulus: ValueWithUnits (Pa),
 *     qMatrix: array (3×3, entries in Pa),
 *     cte_x: number (1/K, dimensionless — unit implicit),
 *     cte_y: number (1/K, dimensionless — unit implicit)
 * }
 */
export function buildMaterialLookup(csvData) returns map
{
    if (!(csvData is array))
    {
        return {};
    }

    var lookup = {};

    for (var row in csvData)
    {
        // Skip rows that don't have enough columns or have empty name
        if (size(row) < 11)
            continue;

        var name = row[1];
        if (name == undefined || name == "")
            continue;

        // Skip header row or any row where density isn't numeric
        if (!(row[2] is number))
            continue;

        // Validate Young's modulus is numeric
        if (!(row[4] is number))
            continue;

        // Exact match — names must match CSV character-for-character
        var key = name;

        // Parse numeric values with units
        // csvData from TableData provides numbers directly; we attach units
        // E and Q values are stored in GPa in the CSV to avoid Onshape Table
        // int32 sign-bit corruption that affects values in [1.07, 2.15] GPa when stored as Pa.
        var density = row[2] * kilogram / meter^3;
        var poissonsRatio = row[3];
        var youngsModulus = row[4] * 1e9 * pascal;

        // CSV column order: [Q11, Q22, Q12, Q66, Q16, Q26] at columns 5–10.
        // All Q values are in GPa in the CSV (multiplied by 1e9 here to convert to Pa).
        var Q11 = row[5] * 1e9 * pascal;
        var Q22 = row[6] * 1e9 * pascal;
        var Q12 = row[7] * 1e9 * pascal;
        var Q66 = row[8] * 1e9 * pascal;
        var Q16 = row[9] * 1e9 * pascal;
        var Q26 = row[10] * 1e9 * pascal;

        var cte_x = 0;
        if (size(row) > 11)
        {
            if (row[11] is number)
                cte_x = row[11];
        }
        var cte_y = 0;
        if (size(row) > 12)
        {
            if (row[12] is number)
                cte_y = row[12];
        }

        // Sanity check: Q11 and Q66 must be positive for a physically valid stiffness matrix.
        // Zero or negative values indicate corrupted CSV data (e.g. wrong column order, empty cells).
        if (Q11 <= 0 * pascal || Q66 <= 0 * pascal)
        {
            println("WARNING: xSectMaterials: material '" ~ name ~ "' has Q11=" ~
                    toString(Q11 / (1e9 * pascal)) ~ " GPa, Q66=" ~
                    toString(Q66 / (1e9 * pascal)) ~ " GPa — check CSV data.");
        }

        var qMatrix = [
            [Q11, Q12, Q16],
            [Q12, Q22, Q26],
            [Q16, Q26, Q66]
        ];

        lookup[key] = {
            "originalName" : name,
            "category" : row[0],
            "density" : density,
            "poissonsRatio" : poissonsRatio,
            "youngsModulus" : youngsModulus,
            "qMatrix" : qMatrix,
            "cte_x" : cte_x,
            "cte_y" : cte_y
        };
    }

    return lookup;
}

// =============================================================================
// EDITING LOGIC HELPERS
// =============================================================================

/**
 * Find a body's entry in the old definition's bodyArray by query match.
 * Returns the old entry map, or undefined if not found.
 */
export function findOldBodyEntry(context is Context, bodyQ is Query, oldDefinition is map)
{
    var oldArray = tryGetKey(oldDefinition, "bodyArray");
    if (oldArray == undefined)
        return undefined;

    for (var oldEntry in oldArray)
    {
        var oldQ = tryGetKey(oldEntry, "bodyQuery");
        if (oldQ != undefined)
        {
            try
            {
                if (areQueriesEquivalent(context, bodyQ, oldQ))
                    return oldEntry;
            }
            catch
            {
            }
        }
    }

    return undefined;
}

/**
 * Safe map key access.
 */
export function tryGetKey(m is map, key is string)
{
    try { return m[key]; }
    return undefined;
}

/**
 * Preserve user-entered override values from old definition for a body without material data.
 *
 * This refactors the inline preservation logic from elFunc (lines 226-268 in original xSect.fs)
 * into a reusable helper that can be called once per body.
 *
 * @param context {Context}
 * @param bodyQ {Query} : Current body query
 * @param oldDefinition {map} : Previous feature definition
 * @param entry {map} : Current bodyArray entry (with default values)
 * @returns {map} : Updated entry with preserved override values
 */
export function preserveOverrideMaterialFields(context is Context, bodyQ is Query,
                                                oldDefinition is map, entry is map) returns map
{
    var updated = entry;

    var oldEntry = findOldBodyEntry(context, bodyQ, oldDefinition);
    if (oldEntry != undefined && oldEntry.hasMaterialData == false)
    {
        var oldBehavior = tryGetKey(oldEntry, "materialBehavior");
        if (oldBehavior != undefined)
            updated.materialBehavior = oldBehavior;

        var oldMatType = tryGetKey(oldEntry, "materialType");
        if (oldMatType != undefined)
            updated.materialType = oldMatType;

        var oldName = tryGetKey(oldEntry, "overrideName");
        if (oldName != undefined)
            updated.overrideName = oldName;

        var oldDensity = tryGetKey(oldEntry, "overrideDensity");
        if (oldDensity != undefined)
            updated.overrideDensity = oldDensity;

        var oldE = tryGetKey(oldEntry, "youngsModulus");
        if (oldE != undefined)
            updated.youngsModulus = oldE;

        var oldE1 = tryGetKey(oldEntry, "E1");
        if (oldE1 != undefined)
            updated.E1 = oldE1;

        var oldE2 = tryGetKey(oldEntry, "E2");
        if (oldE2 != undefined)
            updated.E2 = oldE2;

        var oldG12 = tryGetKey(oldEntry, "G12");
        if (oldG12 != undefined)
            updated.G12 = oldG12;

        var oldNu12 = tryGetKey(oldEntry, "nu12");
        if (oldNu12 != undefined)
            updated.nu12 = oldNu12;
    }

    return updated;
}
