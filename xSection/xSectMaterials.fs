FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

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
 *   4: Young's Modulus [Pa]
 *   5: Q11 [Pa]
 *   6: Q22 [Pa]
 *   7: Q12 [Pa]
 *   8: Q66 [Pa]
 *   9: Q16 [Pa]
 *  10: Q26 [Pa]
 *  11: Available dimensions
 *
 * Q matrix is stored as:
 *   [[Q11, Q12, Q16],
 *    [Q12, Q22, Q26],
 *    [Q16, Q26, Q66]]
 *
 * The lookup map is keyed by normalized names (exact match) via the companion
 * function normalizeMaterialName(). Names must match exactly as they appear
 * in the CSV and in Onshape's material library.
 *
 * Usage in editing logic:
 *   var lookup = buildMaterialLookup(definition.materialCSV.csvData);
 *   var key = normalizeMaterialName(onshapeMaterialName);
 *   var match = lookup[key];  // returns materialData or undefined
 *
 * @param csvData {array} : Array of row arrays from TableData.csvData.
 *                           Each row is an array of parsed values (numbers/strings).
 * @returns {map} : Normalized material name (string) → {
 *     originalName: string (preserves original casing for display),
 *     category: string,
 *     density: ValueWithUnits (kg/m³),
 *     poissonsRatio: number,
 *     youngsModulus: ValueWithUnits (Pa),
 *     qMatrix: array (3×3, entries in Pa)
 * }
 */
export function buildMaterialLookup(csvData) returns map
{
    if (!(csvData is array))
    {
        println("WARNING: Material CSV is not an array");
        return {};
    }

    var lookup = {};
    var validRows = 0;
    var skippedRows = 0;

    for (var row in csvData)
    {
        // Skip rows that don't have enough columns or have empty name
        if (size(row) < 11)
        {
            skippedRows += 1;
            continue;
        }

        var name = row[1];
        if (name == undefined || name == "")
        {
            skippedRows += 1;
            continue;
        }

        // Skip header row or any row where density isn't numeric
        if (!(row[2] is number))
        {
            skippedRows += 1;
            continue;
        }

        // Validate Young's modulus is numeric
        if (!(row[4] is number))
        {
            println("WARNING: Skipping material row with invalid Young's modulus: " ~ toString(name));
            skippedRows += 1;
            continue;
        }

        var key = normalizeMaterialName(name);

        // Parse numeric values with units
        // csvData from TableData provides numbers directly; we attach units
        var density = row[2] * kilogram / meter^3;
        var poissonsRatio = row[3];
        var youngsModulus = row[4] * pascal;

        var Q11 = row[5] * pascal;
        var Q22 = row[6] * pascal;
        var Q12 = row[7] * pascal;
        var Q66 = row[8] * pascal;
        var Q16 = row[9] * pascal;
        var Q26 = row[10] * pascal;

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
            "rawRow" : row
        };
        validRows += 1;
    }

    println("Material library: " ~ validRows ~ " materials loaded" ~
            (skippedRows > 0 ? (", " ~ skippedRows ~ " rows skipped") : ""));

    return lookup;
}

/**
 * Normalize a material name for lookup matching.
 * Returns the name unchanged (identity function). Names must match exactly.
 *
 * @param name {string} : Raw material name
 * @returns {string} : Key for lookup (same as input)
 */
export function normalizeMaterialName(name is string) returns string
{
    return name;
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
            catch (e)
            {
                println("WARNING: Query comparison failed - " ~ e);
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
