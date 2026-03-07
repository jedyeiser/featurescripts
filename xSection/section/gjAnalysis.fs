FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// IMPORT: xSection/gjDataAccess
import(path : "12c9e75dc2139eb927245033", version : "9ac6c3b7c431a4120610201f");
// IMPORT: xSection/xSect_GJ
import(path : "9df6ba3db06d479fabe63c1d", version : "b840272d29f360c74ebcaadc");


/**
 * GJ ANALYSIS IMPLEMENTATION
 * ===========================
 *
 * Main implementation for gjAnalysis feature. Computes torsional stiffness (GJ)
 * for cross-sections using data stored by the xSect feature.
 *
 * Process:
 * 1. Read cross-section data from attribute (xSect feature's stored data)
 * 2. For each cross-section:
 *    - Validate section has required mesh data
 *    - Call computeTorsionalStiffness() FEM solver
 *    - Store GJ result
 * 3. Update attribute with new GJ values (preserving EI and other data)
 * 4. Optionally create 3D visualization curve showing GJ(x)
 *
 * Error handling pattern:
 * - User-facing errors (bad selection, missing data): throw regenError("...") with entity highlight
 * - Internal assertions (should never happen): plain throw "..." string
 * Only regenError propagates properly to the Onshape UI; plain throws show as generic failures.
 */

/**
 * Compute GJ for all sections of an xSect feature and write results back to the attribute.
 *
 * Absorbs the read → loop → compute → write pattern from the Solve GJ feature body so
 * that the feature definition itself stays thin and this logic is reusable by other callers.
 *
 * @param context {Context}
 * @param id {Id} : Feature ID (used as base for visualization curve sub-ID)
 * @param featureKey {string} : Attribute key for the target xSect feature
 *                              (from `keys(definition.xSectFeature)[0][0]`)
 * @param createVisualization {boolean} : If true, create a GJ visualization spline curve
 * @param curvePrefix {string} : Name prefix for the visualization curve (empty = default name)
 */
export function computeAndStoreGJByFeatureKey(context is Context, id is Id,
                                               featureKey is string,
                                               createVisualization is boolean,
                                               curvePrefix is string)
{
    var xSectData;
    try
    {
        xSectData = readXSectAnalysisDataByKey(context, featureKey);
    }
    catch (e)
    {
        throw regenError("Solve GJ: failed to read xSect data — " ~ e);
    }

    var bodies = xSectData.bodies;
    var crossSections = xSectData.crossSections;
    var numSections = size(crossSections);

    var updatedSections = [];
    for (var i = 0; i < numSections; i += 1)
    {
        var section = crossSections[i];

        if (!validateSectionData(section))
        {
            updatedSections = append(updatedSections, section);
            continue;
        }

        try
        {
            var GJ_eff = computeTorsionalStiffness(section, bodies);
            section.GJ_eff = GJ_eff;
        }
        catch (e)
        {
            // Keep existing GJ value on failure
        }

        updatedSections = append(updatedSections, section);
    }

    updateXSectGJDataByKey(context, featureKey, updatedSections);

    if (createVisualization)
    {
        createGJCurve(context, id + "gjCurve", updatedSections, curvePrefix);
    }
}

/**
 * Main entry point for GJ Analysis feature.
 * Called from gjPredicates.fs feature definition.
 *
 * @param context {Context}
 * @param id {Id} : Feature ID
 * @param definition {map} : Feature parameters from UI
 *   - xSectEntity: Query selecting an entity created by the xSect feature
 *   - createGJCurve: boolean - whether to create visualization
 *   - curvePrefix: string - optional name prefix for curve
 */
export function gjAnalysisMain(context is Context, id is Id, definition is map)
{
    // =========================================================================
    // STEP 0: Identify the xSect feature from the selected entity
    // =========================================================================

    var featureKey = getXSectFeatureFromEntity(context, definition.xSectEntity);

    // =========================================================================
    // STEP 1: Read cross-section data from xSect feature's attribute
    // =========================================================================

    var xSectData;
    try
    {
        xSectData = readXSectAnalysisDataByKey(context, featureKey);
    }
    catch (e)
    {
        throw regenError("Failed to read xSect data: " ~ e, definition.xSectEntity);
    }

    var bodies = xSectData.bodies;
    var crossSections = xSectData.crossSections;
    var numSections = size(crossSections);

    // =========================================================================
    // STEP 2: Compute GJ for each cross-section
    // =========================================================================

    var updatedSections = [];
    var successCount = 0;
    var failCount = 0;
    var skipCount = 0;

    for (var i = 0; i < numSections; i += 1)
    {
        var section = crossSections[i];
        var stationNum = section.stationNumber;

        // Validate section has required data
        if (!validateSectionData(section))
        {
            skipCount += 1;
            updatedSections = append(updatedSections, section);
            continue;
        }

        // Compute GJ using FEM solver
        var GJ_eff = 0 * newton * meter * meter;
        try
        {
            GJ_eff = computeTorsionalStiffness(section, bodies);
            successCount += 1;

            // Update section with new GJ value (only on success)
            section.GJ_eff = GJ_eff;
        }
        catch (e)
        {
            failCount += 1;
            // Keep existing GJ value (don't overwrite with 0)
        }

        updatedSections = append(updatedSections, section);
    }

    // =========================================================================
    // STEP 3: Update attribute with new GJ values
    // =========================================================================

    updateXSectGJDataByKey(context, featureKey, updatedSections);

    // =========================================================================
    // STEP 4: Create visualization curve (if requested)
    // =========================================================================

    if (definition.createGJCurve)
    {
        createGJCurve(context, id + "gjCurve", updatedSections, definition.curvePrefix);
    }
}

/**
 * Identify the xSect feature from a selected entity.
 *
 * Uses the entity's owner body to identify which xSect feature created it
 * by matching against stored attribute data.
 *
 * @param context {Context}
 * @param entityQuery {Query} : Query for entity created by xSect feature
 * @returns {string} : Attribute key (featureKey) for the xSect feature
 * @throws : Error if feature cannot be identified
 */
function getXSectFeatureFromEntity(context is Context, entityQuery is Query) returns string
{
    // Evaluate the entity query
    var entities = evaluateQuery(context, entityQuery);
    if (size(entities) == 0)
    {
        throw "No entity selected - please select a body, face, or edge created by the xSect feature";
    }

    var entity = entities[0];

    // Get the owner body (works for faces and edges; for bodies, returns itself)
    var ownerBody = qOwnerBody(entity);
    var ownerBodies = evaluateQuery(context, ownerBody);

    if (size(ownerBodies) == 0)
    {
        throw "Could not identify owner body of selected entity";
    }

    // Get the attribute to find all xSect features
    var attributeData = try silent(getAttribute(context, {
        "entity" : qOrigin(EntityType.BODY),
        "name" : "CrossSectionAnalysis"
    }));

    if (attributeData == undefined)
    {
        throw "No CrossSectionAnalysis attribute found - has an xSect feature been run?";
    }

    // If only one xSect feature exists, use it
    var featureKeys = keys(attributeData);
    if (size(featureKeys) == 1)
    {
        return featureKeys[0];
    }

    // Multiple xSect features — cannot determine which one owns this entity
    throw regenError("Multiple xSect features found (" ~ size(featureKeys) ~
          "). Select an entity (face or edge) that belongs to exactly one xSect feature's analysis path.");
}

/**
 * Create 3D visualization curve showing GJ variation along beam.
 *
 * The curve is created in the XZ plane (world coordinates):
 * - X coordinate: section location along beam
 * - Y coordinate: 0 (in XZ plane)
 * - Z coordinate: GJ value scaled to height (1 N·m² = 1mm)
 *
 * This follows the same pattern as EI curve visualization in xSectVisualization.fs.
 *
 * @param context {Context}
 * @param id {Id} : Sub-feature ID for the curve
 * @param sections {array} : Cross-sections with GJ values
 * @param namePrefix {string} : Optional prefix for curve name
 */
function createGJCurve(context is Context, id is Id, sections is array, namePrefix is string)
{
    // Build array of points for spline curve
    var gjPoints = [];

    for (var section in sections)
    {
        // World X coordinate of this cross-section
        var worldX = section.frame.origin[0];

        // GJ value scaled to millimeters (1 N·m² = 1mm height)
        var GJ_val = section.GJ_eff / (newton * meter * meter);
        var gjHeight = GJ_val * millimeter;

        // Point in XZ plane: [worldX, 0, GJ_height]
        var point = vector(worldX, 0 * meter, gjHeight);
        gjPoints = append(gjPoints, point);
    }

    // Validate sufficient points for spline (need at least 2)
    if (size(gjPoints) < 2)
    {
        return;
    }

    // Create spline curve through points
    try
    {
        opFitSpline(context, id, {
            "points" : gjPoints
        });

        // Set curve name (verify body was created first)
        var createdBodies = evaluateQuery(context, qCreatedBy(id, EntityType.BODY));
        if (size(createdBodies) > 0)
        {
            var curveName = (namePrefix != "") ? (namePrefix ~ "_GJ") : "GJ_curve";
            setProperty(context, {
                "entities" : createdBodies[0],
                "propertyType" : PropertyType.NAME,
                "value" : curveName
            });

        }
        else
        {
        }
    }
    catch (e)
    {
    }
}
