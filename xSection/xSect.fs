FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// xSectPredicates (UI definitions)
export import(path : "17142132b20343b5f125e7e7", version : "915cfab04b2ac30586bf3b10");

// xSectUtils (constants, utilities, polyline projection)
import(path : "c2c3edd39b85fde5e6062533", version : "715a10837362eb3958ac7af2");

// xSectCLT (CLT computations)
import(path : "74231d1d53f5a117d47d17a9", version : "6e4f0f6d6fb192e03179b3af");

// xSectBeamAnalysis (beam stiffness computations)
import(path : "ebac109589e3bf405d3f3ae7", version : "50d8ae4bba946bad63f2fa21");

//import xSectMatrials
import(path : "f8e590162884d45f56e0a05f", version : "b68b3e44226906853f6724cf");
//import xSectProcessing
import(path : "3cb3cff6974529bf6bed096b", version : "4650c27d886bec47e4a2c0c2");
//import xSectVisualization
import(path : "19991d0446ad0551339572d9", version : "c2c975a84aa2a0b9e7b2739b");
//import xSectStorage
import(path : "a2f2ae10eb446d33ccd47bb9", version : "8cc1d233ed2e2a29968f2bf4");
//import xSectComposites
import(path : "8c01f1526e7b93cc89fe9811", version : "63bec95540302c55419de58a");
//import xSectDebug
import(path : "4973f90e73d48ab3578831f0", version : "f211e9dbed8c286bb70f05fb");
//import xSectReferencePoints
import(path : "08fddb59786b6bfee020ee05", version : "11686088c6c8185a95ce8d18");

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
        println("CSV refresh requested — re-reading material library (token=" ~ toString(updatedDef.csvRefreshToken) ~ ")");
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
            println("WARNING: Material CSV not loaded or invalid format");
        }
    }
    catch (e)
    {
        println("ERROR parsing material CSV: " ~ e);
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

    println("Material lookup: " ~ size(materialLookup) ~ " entries");

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
            println("WARNING: Could not read body name - " ~ e);
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
            println("WARNING: Could not read material property - " ~ e);
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
                var r = csvMatch.rawRow;
                println("  CSV match [" ~ materialName ~ "]:");
                println("    [0]Cat="     ~ toString(r[0])  ~
                        " [1]Name="      ~ toString(r[1])  ~
                        " [2]Density="   ~ toString(r[2])  ~
                        " [3]nu="        ~ toString(r[3])  ~
                        " [4]E="         ~ toString(r[4])  ~
                        " [5]Q11="       ~ toString(r[5])  ~
                        " [6]Q22="       ~ toString(r[6])  ~
                        " [7]Q12="       ~ toString(r[7])  ~
                        " [8]Q66="       ~ toString(r[8])  ~
                        " [9]Q16="       ~ toString(r[9])  ~
                        " [10]Q26="      ~ toString(r[10]) ~
                        " [11]CTE_x="    ~ (size(r) > 11 ? toString(r[11]) : "n/a") ~
                        " [12]CTE_y="    ~ (size(r) > 12 ? toString(r[12]) : "n/a") ~
                        " [13]dims(len)=" ~ (size(r) > 13 ? toString(size(r[13])) : "n/a"));
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

    println("Bodies found: " ~ size(newBodyArray));
    for (var b in newBodyArray)
    {
        println("  " ~ b.bodyName ~ " | " ~ b.materialName ~ " | matched=" ~ b.hasMaterialData);
    }

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

        if (fcpX != undefined && acpX != undefined)
        {
            println("FCP/ACP detected - using adaptive spacing:");
            println("  FCP X: " ~ (fcpX / millimeter) ~ " mm");
            println("  ACP X: " ~ (acpX / millimeter) ~ " mm");
            println("  Span: " ~ (abs(acpX - fcpX) / millimeter) ~ " mm");
        }

        // -----------------------------------------------------------------
        // Step 2: Process all cross-sections (intersect, triangulate)
        // -----------------------------------------------------------------
        var crossSectionData = processCrossSections(context, id + "process", definition, fcpX, acpX);

        // -----------------------------------------------------------------
        // Step 3: Compute CLT mechanical properties (ABD, EI, neutral axis)
        // -----------------------------------------------------------------
        crossSectionData = computeCLTProperties(crossSectionData);

        // -----------------------------------------------------------------
        // Step 4: Compute actual body masses (volume-based)
        // -----------------------------------------------------------------
        var massData = computeActualBodyMasses(crossSectionData.bodies);

        // Debug: print EI at each section
        for (var i = 0; i < size(crossSectionData.crossSections); i += 1)
        {
            var section = crossSectionData.crossSections[i];
            var mp = section.mechanicalProperties;
            println("Station " ~ section.stationNumber ~ " | EI=" ~ mp.EI_eff ~ " | NA=" ~ mp.neutralAxisY);
        }

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

            println("EI_bar = " ~ stiffness.EI_bar);
            println("Span L = " ~ stiffness.L);
            println("Prismatic: " ~ stiffness.prismaticStiffness_lbin ~ " lb/in, " ~ stiffness.prismaticStiffness_mm ~ " mm");
            println("Estimated: " ~ stiffness.estimatedStiffness_lbin ~ " lb/in, " ~ stiffness.estimatedStiffness_mm ~ " mm");
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

        // Debug: Print mass analysis
        println("═══════════════════════════════════════");
        println("  MASS ANALYSIS");
        println("═══════════════════════════════════════");
        println("Total mass (volume-based): " ~ (totalWeight / kilogram) ~ " kg");
        println("");
        for (var bodyMass in massData.bodyMasses)
        {
            if (bodyMass.hasMaterial)
            {
                println("  " ~ bodyMass.bodyName ~ ":");
                println("    Volume:  " ~ (bodyMass.volume / (meter^3)) ~ " m³");
                println("    Density: " ~ (bodyMass.density / (kilogram / meter^3)) ~ " kg/m³");
                println("    Mass:    " ~ (bodyMass.mass / kilogram) ~ " kg");
            }
        }
        println("═══════════════════════════════════════");

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

            println("");
            println("═══════════════════════════════════════");
            println("  Analysis complete!");
            println("  Insert 'Cross-Section Analysis' table to view results.");
            println("  Feature ID: " ~ toAttributeId(id));
            println("═══════════════════════════════════════");
            println("");
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
