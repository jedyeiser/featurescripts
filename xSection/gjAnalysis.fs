FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// IMPORT: xSection/gjDataAccess.fs
// IMPORT: xSection/xSect_GJ.fs

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
 */

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

    var xSectFeature = getXSectFeatureFromEntity(context, definition.xSectEntity);

    // =========================================================================
    // STEP 1: Read cross-section data from xSect feature's attribute
    // =========================================================================

    var xSectData;
    try
    {
        xSectData = readXSectAnalysisData(context, xSectFeature);
    }
    catch (e)
    {
        throw regenError("Failed to read xSect data: " ~ e, definition.xSectEntity);
    }

    var bodies = xSectData.bodies;
    var crossSections = xSectData.crossSections;
    var numSections = size(crossSections);

    println("=== GJ Analysis Started ===");
    println("Processing " ~ numSections ~ " cross-sections");

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
            println("WARNING: Section " ~ i ~ " (station " ~ stationNum ~
                    ") missing mesh data - GJ = 0");
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

            var GJ_val = GJ_eff / (newton * meter * meter);
            println("  Section " ~ i ~ " (station " ~ stationNum ~ "): GJ = " ~ GJ_val ~ " N·m²");

            // Update section with new GJ value (only on success)
            section.GJ_eff = GJ_eff;
        }
        catch (e)
        {
            println("WARNING: GJ computation failed for section " ~ i ~
                    " (station " ~ stationNum ~ ") - " ~ e);
            failCount += 1;
            // Keep existing GJ value (don't overwrite with 0)
        }

        updatedSections = append(updatedSections, section);
    }

    // =========================================================================
    // STEP 3: Update attribute with new GJ values
    // =========================================================================

    updateXSectGJData(context, xSectFeature, updatedSections);

    // =========================================================================
    // STEP 4: Create visualization curve (if requested)
    // =========================================================================

    if (definition.createGJCurve)
    {
        createGJCurve(context, id + "gjCurve", updatedSections, definition.curvePrefix);
    }

    // =========================================================================
    // Summary
    // =========================================================================

    println("=== GJ Analysis Complete ===");
    println("  Success: " ~ successCount ~ " sections");
    if (failCount > 0)
    {
        println("  Failed:  " ~ failCount ~ " sections");
    }
    if (skipCount > 0)
    {
        println("  Skipped: " ~ skipCount ~ " sections (missing data)");
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
 * @returns {Query} : Query for the xSect feature ID
 * @throws : Error if feature cannot be identified
 */
function getXSectFeatureFromEntity(context is Context, entityQuery is Query) returns Query
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
        println("Auto-detected xSect feature: " ~ featureKeys[0]);
        return qFeature(featureKeys[0]);
    }

    // Multiple xSect features - try to match based on entity
    // For now, throw an error asking user to specify
    throw "Multiple xSect features found (" ~ size(featureKeys) ~
          "). Please manually identify which feature created this entity.\n" ~
          "Available features: " ~ featureKeys;
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
        println("WARNING: Insufficient points for GJ curve (" ~ size(gjPoints) ~ " points) - need at least 2");
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

            println("Created GJ visualization curve: " ~ curveName);
        }
        else
        {
            println("WARNING: opFitSpline succeeded but no body was created");
        }
    }
    catch (e)
    {
        println("WARNING: Failed to create GJ curve - " ~ e);
    }
}
