FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * XSECTION VISUALIZATION MODULE
 * ==============================
 *
 * Creates visualization curves for cross-section analysis results:
 * - EI (bending stiffness) curve in XZ plane
 * - Neutral axis curve following beam profile
 *
 * Extracted from xSect.fs (lines 1066-1159) to separate visualization from analysis logic.
 */

/**
 * Create visualization curves for cross-section analysis.
 *
 * Wrapper function that creates both EI and neutral axis curves.
 *
 * @param context {Context}
 * @param id {Id} : Base feature ID
 * @param crossSectionData {map} : Full analysis data with mechanicalProperties
 * @param namePrefix {string} : Optional prefix for curve names (from analysisName)
 */
export function createVisualizationCurves(context is Context, id is Id,
                                          crossSectionData is map, namePrefix is string)
{
    createEICurve(context, id + "eiCurve", crossSectionData, namePrefix);
    createNeutralAxisCurve(context, id + "naCurve", crossSectionData, namePrefix);
    createLinealDensityCurve(context, id + "ldCurve", crossSectionData, namePrefix);
}

// =============================================================================
// GENERIC CURVE CREATION HELPER
// =============================================================================

/**
 * Generic helper for creating a spline curve from points and naming it.
 *
 * Handles the common pattern for all visualization curves:
 * 1. Call opFitSpline() with points
 * 2. Build curve name (with optional prefix)
 * 3. Set name property on created body
 * 4. Catch errors and log warnings
 *
 * @param context {Context}
 * @param id {Id}
 * @param points {array} : Array of Vector points for spline
 * @param baseName {string} : Base curve name (e.g., "EI_curve")
 * @param prefixedName {string} : Name to use with prefix (e.g., "EI")
 * @param namePrefix {string} : Optional prefix from analysisName
 */
function createGenericCurve(context is Context, id is Id, points is array,
                            baseName is string, prefixedName is string, namePrefix is string)
{
    if (size(points) < 2)
        return;

    try
    {
        opFitSpline(context, id, {
                "points" : points
        });

        var curveName = baseName;
        if (namePrefix != "")
        {
            curveName = namePrefix ~ "_" ~ prefixedName;
        }

        var createdBodies = evaluateQuery(context, qCreatedBy(id, EntityType.BODY));
        if (size(createdBodies) > 0)
        {
            setProperty(context, {
                    "entities" : createdBodies[0],
                    "propertyType" : PropertyType.NAME,
                    "value" : curveName
            });
        }
    }
    catch (e)
    {
        println("WARNING: Failed to create " ~ baseName ~ " - " ~ e);
    }
}

// =============================================================================
// SPECIALIZED CURVE CREATION FUNCTIONS
// =============================================================================

/**
 * Create an EI visualization curve in the XZ plane.
 *
 * Scale: 1mm of curve height in Z = 1 N*m^2 of bending stiffness.
 * X position = world X of each cross-section origin.
 * Z position = EI_value * millimeter.
 * Y position = 0 (lives in XZ plane).
 *
 * @param context {Context}
 * @param id {Id}
 * @param crossSectionData {map} : Full data with mechanicalProperties
 * @param namePrefix {string} : Analysis name prefix for curve naming
 */
export function createEICurve(context is Context, id is Id, crossSectionData is map, namePrefix is string)
{
    var sections = crossSectionData.crossSections;
    var points = [];

    for (var section in sections)
    {
        var worldX = section.frame.origin[0];
        var EI_eff = section.mechanicalProperties.EI_eff;

        // Scale: 1 N*m^2 = 1mm of height in world Z
        var zHeight = (EI_eff / (newton * meter * meter)) * millimeter;

        points = append(points, vector(worldX, 0 * meter, zHeight));
    }

    createGenericCurve(context, id, points, "EI_curve", "EI", namePrefix);
}

/**
 * Create a neutral axis curve that follows the base edge profile
 * offset by the neutral axis height in the frame normal direction.
 *
 * For each cross-section:
 *   NA_point = frame.origin + |neutralAxisY| * frame.xAxis
 *
 * The offset is along frame.xAxis (thickness direction, normal to base edge).
 * neutralAxisY is negative in our convention (NA above base = negative from
 * -B/A formula), so we negate it for a positive offset upward.
 *
 * @param context {Context}
 * @param id {Id}
 * @param crossSectionData {map} : Full data with mechanicalProperties
 * @param namePrefix {string} : Analysis name prefix for curve naming
 */
export function createNeutralAxisCurve(context is Context, id is Id, crossSectionData is map, namePrefix is string)
{
    var sections = crossSectionData.crossSections;
    var points = [];

    for (var section in sections)
    {
        var origin = section.frame.origin;
        var xDir = section.frame.xAxis;
        var naHeight = section.mechanicalProperties.neutralAxisY;

        // Negate: neutralAxisY is negative (above base), offset is positive upward
        var naPoint = origin + (-naHeight) * xDir;

        points = append(points, naPoint);
    }

    createGenericCurve(context, id, points, "neutral_axis", "neutralAxis", namePrefix);
}

/**
 * Create a lineal density visualization curve in the XZ plane.
 *
 * Scale: 1 kg/m of lineal density = 100mm of curve height in Z.
 * X position = world X of each cross-section origin.
 * Z position = lineal_density * 100mm.
 * Y position = 0 (lives in XZ plane).
 *
 * @param context {Context}
 * @param id {Id}
 * @param crossSectionData {map} : Full data with mechanicalProperties
 * @param namePrefix {string} : Analysis name prefix for curve naming
 */
export function createLinealDensityCurve(context is Context, id is Id,
                                          crossSectionData is map, namePrefix is string)
{
    var sections = crossSectionData.crossSections;
    var points = [];

    for (var section in sections)
    {
        var worldX = section.frame.origin[0];

        // Sum lineal density from all body contributions
        var linealDensity = 0 * kilogram / meter;
        for (var contrib in section.mechanicalProperties.bodyContributions)
        {
            if (contrib.linearDensity != undefined)
            {
                linealDensity = linealDensity + contrib.linearDensity;
            }
        }

        // Scale: 1 kg/m = 100mm of height in world Z
        var zHeight = (linealDensity / (kilogram / meter)) * 100 * millimeter;

        points = append(points, vector(worldX, 0 * meter, zHeight));
    }

    createGenericCurve(context, id, points, "linealDensity_curve", "linealDensity", namePrefix);
}
