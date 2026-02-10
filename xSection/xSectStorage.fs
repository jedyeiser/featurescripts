FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// xSectMaterials (for tryGetKey helper)
import(path : "f8e590162884d45f56e0a05f", version : "a10b83decf148c418d18a345");


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
            "neutralAxisY" : section.mechanicalProperties.neutralAxisY,
            "boundingBox" : section.boundingBox,
            "linealDensity" : linealDensity,
            "mechanicalProperties" : {
                "A" : section.mechanicalProperties.A,
                "B" : section.mechanicalProperties.B,
                "D" : section.mechanicalProperties.D
            }
        };
        sectionDetails = append(sectionDetails, sectionDetail);
    }

    // Get feature name (if provided)
    var featureName = "";
    if (definition.featureName != undefined && definition.featureName != "")
    {
        featureName = definition.featureName;
    }

    // Build complete data structure
    var analysisData = {
        "featureName" : featureName,
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

        println("Stored analysis data with key: " ~ featureKey);
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
 * @returns {map} : { summary: array, crossSections: array }
 */
export function buildTableData(crossSections is array, beamAnalysis, totalWeight is ValueWithUnits) returns map
{
    // Summary table
    var summaryTable = [];

    if (beamAnalysis != undefined)
    {
        var prisLbIn = roundValue(beamAnalysis.prismaticStiffness_lbin, 0.05);   // 0.05 lb/in
        var prisMm = roundValue(beamAnalysis.prismaticStiffness_mm, 0.1);        // 0.1 mm
        var estLbIn = roundValue(beamAnalysis.estimatedStiffness_lbin, 0.05);    // 0.05 lb/in
        var estMm = roundValue(beamAnalysis.estimatedStiffness_mm, 0.1);         // 0.1 mm

        summaryTable = append(summaryTable, ["Prismatic stiffness (lb/in)", prisLbIn]);
        summaryTable = append(summaryTable, ["Prismatic stiffness (mm/30kg)", prisMm]);
        summaryTable = append(summaryTable, ["Estimated stiffness (lb/in)", estLbIn]);
        summaryTable = append(summaryTable, ["Estimated stiffness (mm/30kg)", estMm]);
    }

    var weightKg = roundValue(totalWeight / kilogram, 0.01);  // 0.01 kg precision
    summaryTable = append(summaryTable, ["Weight (kg)", weightKg]);

    // Cross-section table header
    var csTable = [
        ["Station", "X", "EI", "NA Height", "Beam Width", "Beam Height", "Lineal Density"]
    ];

    // Add data rows
    for (var i = 0; i < size(crossSections); i += 1)
    {
        var section = crossSections[i];
        var stationNum = section.stationNumber;  // Extract station number from section data
        var xCoord = section.frame.origin[0] / millimeter;  // World X in mm
        var EI = section.mechanicalProperties.EI_eff / (newton * meter * meter);
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
        naHeight = roundValue(naHeight, 0.05);       // 0.05mm precision
        beamHeight = roundValue(beamHeight, 0.05);   // 0.05mm precision
        beamWidth = roundValue(beamWidth, 0.05);     // 0.05mm precision
        linealDensityVal = roundValue(linealDensityVal, 0.01);  // 0.01 kg/m precision

        csTable = append(csTable, [stationNum, xCoord, EI, naHeight, beamWidth, beamHeight, linealDensityVal]);
    }

    return {
        "summary" : summaryTable,
        "crossSections" : csTable
    };
}
