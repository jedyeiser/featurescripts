FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// xSectPredicates (UI definitions)
export import(path : "17142132b20343b5f125e7e7", version : "0e6215d723bc0619b74f7dd4");

// xSectUtils (constants, utilities, polyline projection)
import(path : "c2c3edd39b85fde5e6062533", version : "33bc110a345c59dd98776a00");

// xSectCLT (CLT computations)
import(path : "74231d1d53f5a117d47d17a9", version : "8bd88ef6a6d18e7e44aef45e");

// xSectBeamAnalysis (beam stiffness computations)
import(path : "ebac109589e3bf405d3f3ae7", version : "7e4fdcd1cd16322867bb23fc");

//import xSectMatrials
import(path : "f8e590162884d45f56e0a05f", version : "c6d4a2440a5dac22f41a1a21");
//import xSectProcessing
import(path : "3cb3cff6974529bf6bed096b", version : "f177f840c28fbae5ca95968f");
//import xSectVisualization
import(path : "19991d0446ad0551339572d9", version : "669e52dc9601ee7fc48b4439");
//import xSectStorage
import(path : "a2f2ae10eb446d33ccd47bb9", version : "01a7fa259eda96a0c933e2b9");
//import xSectComposites
import(path : "8c01f1526e7b93cc89fe9811", version : "b9f252a6fad41c8dd9770cb0");
//import xSectDebug
import(path : "4973f90e73d48ab3578831f0", version : "b611b06e41a74f0fdccc4cb7");
//import xSectReferencePoints
import(path : "08fddb59786b6bfee020ee05", version : "d4ef98b7b0acf40b1998b4ed");
// xSect_GJ (torsional stiffness)
import(path : "9df6ba3db06d479fabe63c1d", version : "dfcffe2529d8e0ac4d2add13");

// =============================================================================
// FEATURE DEFINITION
// =============================================================================

/**
 * EI AND CROSS SECTION FEATURE
 * =============================
 *
 * Generates cross-section analysis data for multiple solid bodies along an edge path,
 * then computes beam-level mechanical properties (ABD matrices, EI, neutral axis)
 * using Classical Laminate Theory.
 *
 * Data Structure:
 * {
 *     bodies: [{
 *         bodyQuery, bodyIdx, bodyName, materialName,
 *         hasMaterialData: boolean,
 *         materialData?: {
 *             originalName, category,
 *             density (kg/m3),
 *             poissonsRatio,
 *             youngsModulus (Pa),
 *             qMatrix: [[Q11,Q12,Q16],[Q12,Q22,Q26],[Q16,Q26,Q66]] (Pa)
 *         },
 *         volume (m3)
 *     }, ...],
 *     crossSections: [{
 *         frame: CoordSystem,
 *         sectionPoints: [{ point2D, point3D }, ...],
 *         bSplineCurves: [{ bSplineCurve, bodyIndices }, ...],
 *         bodyData: [{
 *             bodyIdx: number,
 *             groups: [{
 *                 perimeterPointIndices: [int, ...],
 *                 triangles: [[i,j,k], ...],
 *                 sectionProperties: { area, centroid2D, centroid3D, Ixx, Iyy, Ixy },
 *                 subgroups: [...]
 *             }, ...],
 *             totalSectionProperties: { ... }
 *         }, ...],
 *         mechanicalProperties: {
 *             A: [[3x3]] (N),
 *             B: [[3x3]] (N*m),
 *             D: [[3x3]] (N*m2),
 *             neutralAxisY (m),
 *             EI_eff (N*m2),
 *             sectionWidth (m),
 *             sectionHeight (m),
 *             bodyContributions: [{
 *                 bodyIdx, linearDensity (kg/m), hasMaterial
 *             }, ...]
 *         }
 *     }, ...]
 * }
 */


// =============================================================================
// EDITING LOGIC
// =============================================================================

/**
 * Editing logic for the xSection (EI and Cross Section) feature.
 *
 * Runs on every UI interaction to rebuild the `bodyArray` and update material-match status.
 * Only predicate-declared fields are stored on bodyArray entries — complex maps with
 * ValueWithUnits (Q matrices, density) are NOT serialized here; they are resolved at
 * regen time inside the feature body via `processCrossSections`.
 *
 * @param context {Context}
 * @param id {Id}
 * @param oldDefinition {map} : Previous definition snapshot (used to preserve user overrides)
 * @param definition {map} : Current definition map. Relevant fields read/written:
 *   - `definition.selBodies` {Query} : User-selected solid bodies to analyze
 *   - `definition.materialCSV` {map} : CSV blob with `.csvData` array (material database)
 *   - `definition.bodyArray` {array} : Rebuilt each call — array of per-body entries:
 *       { bodyQuery, bodyName, bodyNum, hasMaterialData, materialName,
 *         materialBehavior, materialType, overrideName, overrideDensity,
 *         youngsModulus, E1, E2, G12, nu12 }
 *   - `definition.csvRefreshToken` {number} : Bumped when "Refresh CSV" button is clicked
 *   - `definition.debugBodies` {Query} : Filtered to only include bodies still in selBodies
 * @param isCreating {boolean} : True on first creation (unused currently)
 * @param specifiedParameters {map} : Fields explicitly set by the user this interaction
 * @param hiddenBodies {Query} : Bodies hidden by the feature (unused currently)
 * @param clickedButton {string} : ID of clicked button, e.g. "refreshCSV"
 * @returns {map} : Updated definition map
 */
export function elFunc(context is Context, id is Id, oldDefinition is map, definition is map,
                       isCreating is boolean, specifiedParameters is map, hiddenBodies is Query,
                       clickedButton is string) returns map
{
    var updatedDef = definition;

    // Handle CSV refresh button
    if (clickedButton == "refreshCSV")
    {
        var currentToken = tryGetKey(definition, "csvRefreshToken");
        if (currentToken == undefined || !(currentToken is number))
            currentToken = 0;
        updatedDef.csvRefreshToken = currentToken + 1;
    }

    // -----------------------------------------------------------------
    // Step 1: Parse CSV into lookup map (for name matching)
    // -----------------------------------------------------------------
    var materialLookup = {};
    try
    {
        var csvData = definition.materialCSV.csvData;
        if (csvData is array)
        {
            materialLookup = buildMaterialLookup(csvData);
        }
    }
    catch
    {
        // Proceed with empty lookup — no CSV loaded or field not yet set
    }

    // -----------------------------------------------------------------
    // Step 2: Evaluate selBodies into individual body queries
    // -----------------------------------------------------------------
    var selectedBodies = evaluateQuery(context, definition.selBodies);

    // -----------------------------------------------------------------
    // Step 3: Build bodyArray from selected bodies
    //
    // Only predicate-declared fields go into the entry.
    // We rebuild the array each time, but preserve user-entered override
    // values by matching against the old definition's bodyArray.
    // -----------------------------------------------------------------
    var newBodyArray = [];

    for (var i = 0; i < size(selectedBodies); i += 1)
    {
        var bodyQ = selectedBodies[i];

        // --- Read Onshape part name ---
        var bodyName = "Unnamed";
        try
        {
            bodyName = getProperty(context, {
                    "entity" : bodyQ,
                    "propertyType" : PropertyType.NAME
            });
        }
        catch
        {
        }

        // --- Read Onshape material name ---
        var materialName = "Not assigned";
        try
        {
            var onshapeMaterial = getProperty(context, {
                    "entity" : bodyQ,
                    "propertyType" : PropertyType.MATERIAL
            });
            if (onshapeMaterial != undefined && onshapeMaterial['name'] != undefined)
            {
                materialName = onshapeMaterial['name'];
            }
        }
        catch
        {
            // No material assigned — materialName stays "Not assigned"
        }

        // --- Match against CSV ---
        var hasMaterialData = false;

        if (materialName != "Not assigned")
        {
            // Exact match — names must match CSV character-for-character
            var key = materialName;
            var csvMatch = materialLookup[key];
            if (csvMatch != undefined)
            {
                hasMaterialData = true;
            }
        }

        // --- Build the array entry ---
        //
        // Always populate ALL fields with valid defaults so the predicate
        // spec is satisfied regardless of which conditional branches it
        // evaluates. Hidden/conditional fields just won't be shown in UI
        // but FeatureScript still validates they satisfy bounds.
        //
        var entry = {
            "bodyQuery" : bodyQ,
            "bodyName" : bodyName,
            "bodyNum" : i + 1,
            "hasMaterialData" : hasMaterialData,
            "materialName" : materialName,
            // Override fields — always present with valid defaults
            "materialBehavior" : MaterialBehavior.IGNORE,
            "materialType" : MaterialType.ISOTROPIC,
            "overrideName" : "Default",
            "overrideDensity" : 1000,
            "youngsModulus" : 10000,
            "E1" : 10000,
            "E2" : 10000,
            "G12" : 3000,
            "nu12" : 0.3
        };

        // --- Preserve user-entered override values from old definition ---
        if (!hasMaterialData)
        {
            entry = preserveOverrideMaterialFields(context, bodyQ, oldDefinition, entry);
        }

        newBodyArray = append(newBodyArray, entry);
    }

    updatedDef.bodyArray = newBodyArray;

    // -----------------------------------------------------------------
    // Step 4: Clean up debug filters
    // -----------------------------------------------------------------
    var selBodies = mapArray(newBodyArray, function(b) { return b.bodyQuery; });

    var debugBodies = evaluateQuery(context, definition.debugBodies);
    var cleanDebugBodies = filter(debugBodies, function(db) {
        return any(selBodies, function(sb) { return areQueriesEquivalent(context, sb, db); });
    });
    updatedDef.debugBodies = qUnion(cleanDebugBodies);

    var updatedXSections = filter(definition.debugXSections, function(xs) {
        return xs.xSectionNum <= definition.numSections;
    });
    updatedDef.debugXSections = updatedXSections;

    return updatedDef;
}


// =============================================================================
// MAIN FEATURE
// =============================================================================

annotation { "Feature Type Name" : "EI and Cross Section",
             "Feature Type Description" : "Generates cross-section analysis data for solid bodies along an edge path.",
             "Editing Logic Function" : "elFunc" }
export const eiXSect = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        eiXSectPrecondition(definition);
    }
    {
        // -----------------------------------------------------------------
        // Step 1: Resolve reference points (FCP/ACP) early for adaptive spacing
        // -----------------------------------------------------------------
        var fcpX = resolveReferencePointX(context, definition.fcpQuery, definition.xSectAlong);
        var acpX = resolveReferencePointX(context, definition.acpQuery, definition.xSectAlong);

        // -----------------------------------------------------------------
        // Step 2: Process all cross-sections (intersect, triangulate)
        // -----------------------------------------------------------------
        var crossSectionData = processCrossSections(context, id + "process", definition, fcpX, acpX);

        // -----------------------------------------------------------------
        // Step 3: Compute CLT mechanical properties (ABD, EI, neutral axis)
        // -----------------------------------------------------------------
        crossSectionData = computeCLTProperties(crossSectionData);

        // -----------------------------------------------------------------
        // Step 3b: Compute torsional stiffness (GJ) inline
        // -----------------------------------------------------------------
        try
        {
            for (var i = 0; i < size(crossSectionData.crossSections); i += 1)
            {
                var section = crossSectionData.crossSections[i];
                var gjValue = computeTorsionalStiffness(section, crossSectionData.bodies);
                crossSectionData.crossSections[i].mechanicalProperties.GJ_eff = gjValue;
            }
        }
        catch (e)
        {
            println("xSect: GJ computation failed — " ~ toString(e));
        }

        // -----------------------------------------------------------------
        // Step 4: Compute actual body masses (volume-based)
        // -----------------------------------------------------------------
        var massData = computeActualBodyMasses(crossSectionData.bodies);

        // -----------------------------------------------------------------
        // Step 5: Compute beam stiffness (if FCP/ACP defined)
        // -----------------------------------------------------------------
        var beamAnalysisResults = undefined;
        if (fcpX != undefined && acpX != undefined)
        {
            var xFCP = min(fcpX, acpX);
            var xACP = max(fcpX, acpX);

            var eiData = extractEIData(crossSectionData);
            var stiffness = computeBeamStiffness(eiData, xFCP, xACP);
            beamAnalysisResults = stiffness;
        }

        // -----------------------------------------------------------------
        // Step 6: Create visualization curves (EI, neutral axis)
        // -----------------------------------------------------------------
        var namePrefix = "";
        var analysisName = tryGetKey(definition, "analysisName");
        if (analysisName != undefined && analysisName != "")
        {
            namePrefix = analysisName;
        }

        createVisualizationCurves(context, id, crossSectionData, namePrefix);

        // -----------------------------------------------------------------
        // Step 7: Compute total weight and build table data
        // -----------------------------------------------------------------
        var totalWeight = massData.totalMass;

        // Extract language preference (default to English)
        var language = LANGUAGE.ENG;
        if (definition.tableLanguage != undefined)
        {
            language = definition.tableLanguage;
        }

        var tableData = buildTableData(crossSectionData.crossSections, beamAnalysisResults, totalWeight, language);

        if (definition.addMaterialTable == true)
        {
            var matTableData = buildMaterialTableData(crossSectionData.bodies);
            tableData["materialTable"] = matTableData;
        }

        if (definition.addBodyTable == true)
        {
            var bodyTableData = buildBodyTableData(crossSectionData.crossSections, crossSectionData.bodies);
            var btt = tryGetKey(definition, "bodyTableType");
            if (btt == undefined)
            {
                btt = BodyTableType.FULL;
            }
            bodyTableData["bodyTableType"] = btt;
            tableData["bodyTable"] = bodyTableData;
        }

        // -----------------------------------------------------------------
        // Step 8: Store analysis data as attribute on origin
        // -----------------------------------------------------------------
        try
        {
            storeAnalysisData(context, id, definition, crossSectionData.bodies, crossSectionData, beamAnalysisResults, tableData, massData);

        }
        catch (e)
        {
            println("xSect: storeAnalysisData failed — " ~ toString(e));
        }

        // -----------------------------------------------------------------
        // Step 9: Optionally create composite wires
        // -----------------------------------------------------------------
        if (definition.createComposites)
        {
            createCompositeWires(context, id, crossSectionData);
        }

        // -----------------------------------------------------------------
        // Step 10: Optionally create debug visualization
        // -----------------------------------------------------------------
        if (definition.debug)
        {
            debugVisualization(context, id, crossSectionData, definition);
        }
    });
