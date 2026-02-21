FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * GJ ANALYSIS DATA ACCESS MODULE
 * ===============================
 *
 * Helper functions for reading and writing cross-section analysis data
 * from/to attributes for the gjAnalysis feature.
 *
 * Functions:
 * - readXSectAnalysisData: Read cross-section data from xSect feature attribute
 * - updateXSectGJData: Update GJ values in existing attribute
 * - validateSectionData: Check if section has required data for GJ computation
 */

/**
 * Read cross-section analysis data from an xSect feature's stored attribute.
 *
 * @param context {Context}
 * @param xSectFeature {Query} : The xSect feature to read from
 * @returns {map} : { bodies: array, crossSections: array }
 * @throws : Error if attribute not found or data incomplete
 *
 * The returned crossSections array contains section data with:
 * - frame, sectionPoints, bodyData (for GJ computation)
 * - mechanicalProperties.GJ_eff (current GJ value, may be 0)
 */
export function readXSectAnalysisData(context is Context, xSectFeature is Query) returns map
{
    // Convert feature query to attribute ID
    var featureId = try silent(evaluateQuery(context, xSectFeature)[0]);
    if (featureId == undefined)
    {
        throw "Invalid xSect feature - query returned no results";
    }

    var featureKey = toAttributeId(featureId);

    // Read attribute from origin point
    var attributeData = try silent(getAttribute(context, {
        "entity" : qOrigin(EntityType.BODY),
        "name" : "CrossSectionAnalysis"
    }));

    if (attributeData == undefined)
    {
        throw "No CrossSectionAnalysis attribute found on origin - xSect feature may not have run yet";
    }

    // Extract feature-specific data
    var featureData = attributeData[featureKey];
    if (featureData == undefined)
    {
        throw "Feature data not found in attribute - feature ID: " ~ featureKey;
    }

    // Validate required fields
    var details = featureData.details;
    if (details == undefined)
    {
        throw "Missing 'details' field in attribute data";
    }

    if (details.bodies == undefined || size(details.bodies) == 0)
    {
        throw "Missing or empty 'bodies' array in attribute data";
    }

    if (details.crossSections == undefined || size(details.crossSections) == 0)
    {
        throw "Missing or empty 'crossSections' array in attribute data";
    }

    return {
        "bodies" : details.bodies,
        "crossSections" : details.crossSections
    };
}

/**
 * Update GJ values in the stored attribute for an xSect feature.
 *
 * @param context {Context}
 * @param xSectFeature {Query} : The xSect feature to update
 * @param updatedCrossSections {array} : Cross-sections with new GJ_eff values
 *
 * This function:
 * 1. Reads the current attribute (preserving other features' data)
 * 2. Updates only the GJ_eff values in the target feature's cross-sections
 * 3. Writes the merged attribute back
 *
 * Note: Does not throw on write failure - warns instead (GJ was computed successfully)
 */
export function updateXSectGJData(context is Context, xSectFeature is Query, updatedCrossSections is array)
{
    // Convert feature query to attribute ID
    var featureId = try silent(evaluateQuery(context, xSectFeature)[0]);
    if (featureId == undefined)
    {
        println("WARNING: Cannot update attribute - invalid xSect feature");
        return;
    }

    var featureKey = toAttributeId(featureId);

    // Read existing attribute
    var attributeData = try silent(getAttribute(context, {
        "entity" : qOrigin(EntityType.BODY),
        "name" : "CrossSectionAnalysis"
    }));

    if (attributeData == undefined)
    {
        println("WARNING: Cannot update attribute - attribute not found");
        return;
    }

    // Get feature data
    var featureData = attributeData[featureKey];
    if (featureData == undefined || featureData.details == undefined)
    {
        println("WARNING: Cannot update attribute - feature data missing");
        return;
    }

    // Update GJ values in cross-sections
    var existingSections = featureData.details.crossSections;
    if (size(existingSections) != size(updatedCrossSections))
    {
        println("WARNING: Section count mismatch - " ~ size(existingSections) ~
                " existing vs " ~ size(updatedCrossSections) ~ " updated");
    }

    for (var i = 0; i < size(updatedCrossSections); i += 1)
    {
        if (i < size(existingSections) && updatedCrossSections[i].GJ_eff != undefined)
        {
            // Update only GJ_eff field (preserve other data)
            existingSections[i].GJ_eff = updatedCrossSections[i].GJ_eff;
        }
    }

    // Sync GJ values into tableData so the displayed table reflects new values.
    // Table layout: row 0 = header, rows 1+ = data. GJ is column index 3.
    var csTable = featureData.details.crossSections;  // Re-read for table update
    if (featureData.tableData != undefined &&
        featureData.tableData.crossSections != undefined &&
        size(featureData.tableData.crossSections) > 1)
    {
        var tableRows = featureData.tableData.crossSections;
        for (var i = 0; i < size(updatedCrossSections); i += 1)
        {
            var tableRow = i + 1;  // Skip header at index 0
            if (tableRow < size(tableRows) && updatedCrossSections[i].GJ_eff != undefined)
            {
                var GJ_raw = updatedCrossSections[i].GJ_eff / (newton * meter * meter);
                // Round to 0.1 N·m² precision (matches buildTableData rounding in xSectStorage.fs)
                tableRows[tableRow][3] = round(GJ_raw * 10.0) / 10.0;
            }
        }
        featureData.tableData["crossSections"] = tableRows;
        attributeData[featureKey] = featureData;
    }

    // Write updated attribute
    try
    {
        setAttribute(context, {
            "entities" : qOrigin(EntityType.BODY),
            "name" : "CrossSectionAnalysis",
            "attribute" : attributeData
        });

    }
    catch (e)
    {
        println("WARNING: Failed to write updated attribute - " ~ e);
    }
}

/**
 * Validate that a cross-section has the required data for GJ computation.
 *
 * @param section {map} : Cross-section data from attribute
 * @returns {boolean} : true if section can be used for GJ computation
 *
 * Checks for presence of:
 * - sectionPoints (2D point coordinates)
 * - bodyData (triangulation mesh)
 * - frame (coordinate system)
 */
export function validateSectionData(section is map) returns boolean
{
    // Check for required fields
    if (section.sectionPoints == undefined)
    {
        return false;
    }

    if (section.bodyData == undefined)
    {
        return false;
    }

    if (section.frame == undefined)
    {
        return false;
    }

    // Validate bodyData is an array of maps with triangulation data
    // Structure: bodyData[i] = { bodyIdx, groups, totalSectionProperties, boundingBox }
    //            groups[j] = { triangles: [[i1,j1,k1], ...], ... }
    if (!(section.bodyData is array))
    {
        return false;
    }

    if (size(section.bodyData) == 0)
    {
        return false;
    }

    // Check that at least one body has triangles
    var hasTriangles = false;
    for (var bodyEntry in section.bodyData)
    {
        if (bodyEntry.groups != undefined && size(bodyEntry.groups) > 0)
        {
            for (var group in bodyEntry.groups)
            {
                if (group.triangles != undefined && size(group.triangles) > 0)
                {
                    hasTriangles = true;
                    break;
                }
            }
        }
        if (hasTriangles)
        {
            break;
        }
    }

    return hasTriangles;
}
