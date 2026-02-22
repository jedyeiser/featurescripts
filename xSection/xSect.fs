FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// xSectPredicates (UI definitions)
export import(path : "17142132b20343b5f125e7e7", version : "050844b1699edc3d7d5cd7ac");

// xSectUtils (constants, utilities, polyline projection)
import(path : "c2c3edd39b85fde5e6062533", version : "a7fe1c7d5205fb83d436bead");

// xSectCLT (CLT computations)
import(path : "74231d1d53f5a117d47d17a9", version : "465228769219dc9b9826a990");

// xSectBeamAnalysis (beam stiffness computations)
import(path : "ebac109589e3bf405d3f3ae7", version : "ac8c3132c76d69fd74f330d7");

//import xSectMatrials
import(path : "f8e590162884d45f56e0a05f", version : "3e555e4ae5c7fc9267f971fe");
//import xSectProcessing
import(path : "3cb3cff6974529bf6bed096b", version : "ba5016a06f4fc45afb33e35e");
//import xSectVisualization
import(path : "19991d0446ad0551339572d9", version : "a69b37b988d93cf6c8dd5b3b");
//import xSectStorage
import(path : "a2f2ae10eb446d33ccd47bb9", version : "4ab65d23e2f41af46a45f6e5");
//import xSectComposites
import(path : "8c01f1526e7b93cc89fe9811", version : "7382221ecebdd63d150bbe25");
//import xSectDebug
import(path : "4973f90e73d48ab3578831f0", version : "2fd76b5882144d86b9965fc9");
//import xSectReferencePoints
import(path : "08fddb59786b6bfee020ee05", version : "8e39305a0b7508392ef7f14e");
// xSect_GJ (torsional stiffness)
import(path : "9df6ba3db06d479fabe63c1d", version : "0e68885e3b5e21a4c36925e7");

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
 * Editing logic function.
 *
 * Responsibilities:
 * 1. Parse materialCSV into a lookup map (for name matching only)
 * 2. Evaluate selBodies -> populate bodyArray (one entry per solid body)
 * 3. For each body:
 *    a. Read Onshape part name -> bodyName
 *    b. Read Onshape material name -> materialName
 *    c. Match materialName against CSV lookup -> set hasMaterialData
 * 4. Preserve user-entered override values across edits
 *
 * NOTE: Only predicate-declared fields are stored on bodyArray entries.
 * Material data (Q matrices, density with units, etc.) is resolved in the
 * feature body via processCrossSections, because complex ValueWithUnits
 * maps don't survive definition serialization.
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
        else
        {
        }
    }
    catch (e)
    {
        // Proceed with empty lookup
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
        catch (e)
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
        catch (e)
        {
        }

        // --- Match against CSV ---
        var hasMaterialData = false;

        if (materialName != "Not assigned")
        {
            var key = normalizeMaterialName(materialName);
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
            println("WARNING: GJ inline computation failed - " ~ e);
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
        try
        {
            if (definition.analysisName != undefined && definition.analysisName != "")
            {
                namePrefix = definition.analysisName;
            }
        }
        catch (e)
        {
            println("WARNING: Could not read analysis name - " ~ e);
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

            if (definition.debug)
            {
                println("Analysis complete. Feature ID: " ~ toAttributeId(id));
            }
        }
        catch (e)
        {
            println("WARNING: Failed to store analysis data on origin: " ~ e);
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
