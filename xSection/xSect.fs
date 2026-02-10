FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");


// xSectPredicates (UI definitions)
export import(path : "17142132b20343b5f125e7e7", version : "20da8dfc09e465299e6d578e");
// xSectUtils (constants, utilities, polyline projection)
import(path : "c2c3edd39b85fde5e6062533", version : "eb21258f6fd7abb6c94d71e8");
// xsectProcessing - UNUSED (experimental overlap detection, kept for reference)
// import(path : "3cb3cff6974529bf6bed096b", version : "e0645029416d1814a0794df3");
// tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
// tools/debug - provides debugControlPolygon
import(path : "b1e8bfe71f67389ca210ed8b/e13e99b75ba5ce6d6380ddd5/8944e3e431de4929b0a28fbc", version : "889ff7e9c358da182dc0bf8a");
// import xSect_Triangulation
import(path : "08d3a8d4e34a60d45d46e261", version : "af1665a8a66963219eacd299");
//import xSectCLT
import(path : "74231d1d53f5a117d47d17a9", version : "ea69733493fc950e2f1573f4");
//import xSectBeamAnalysis
import(path : "ebac109589e3bf405d3f3ae7", version : "e77a468079862e0fd3b26224");

// =============================================================================
// CURVE OVERLAP DETECTION TYPES
// =============================================================================

/**
 * Overlap detection result types for curve deduplication.
 * Used by getUniqueCurvesOptimized() for composite wire generation.
 */
enum OverlapType
{
    NONE,               // No overlap detected
    FULL_CONTAINMENT,   // One curve fully contains the other
    PARTIAL_OVERLAP     // Curves partially overlap at endpoints
}

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
                       isCreating is boolean) returns map
{
    var updatedDef = definition;
    
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
        catch {}
        
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
        catch {}
        
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
            var oldEntry = findOldBodyEntry(context, bodyQ, oldDefinition);
            if (oldEntry != undefined && oldEntry.hasMaterialData == false)
            {
                var oldBehavior = tryGetKey(oldEntry, "materialBehavior");
                if (oldBehavior != undefined)
                    entry.materialBehavior = oldBehavior;
                
                var oldMatType = tryGetKey(oldEntry, "materialType");
                if (oldMatType != undefined)
                    entry.materialType = oldMatType;
                
                var oldName = tryGetKey(oldEntry, "overrideName");
                if (oldName != undefined)
                    entry.overrideName = oldName;
                
                var oldDensity = tryGetKey(oldEntry, "overrideDensity");
                if (oldDensity != undefined)
                    entry.overrideDensity = oldDensity;
                
                var oldE = tryGetKey(oldEntry, "youngsModulus");
                if (oldE != undefined)
                    entry.youngsModulus = oldE;
                
                var oldE1 = tryGetKey(oldEntry, "E1");
                if (oldE1 != undefined)
                    entry.E1 = oldE1;
                
                var oldE2 = tryGetKey(oldEntry, "E2");
                if (oldE2 != undefined)
                    entry.E2 = oldE2;
                
                var oldG12 = tryGetKey(oldEntry, "G12");
                if (oldG12 != undefined)
                    entry.G12 = oldG12;
                
                var oldNu12 = tryGetKey(oldEntry, "nu12");
                if (oldNu12 != undefined)
                    entry.nu12 = oldNu12;
            }
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
// EDITING LOGIC HELPERS
// =============================================================================

/**
 * Find a body's entry in the old definition's bodyArray by query match.
 * Returns the old entry map, or undefined if not found.
 */
function findOldBodyEntry(context is Context, bodyQ is Query, oldDefinition is map)
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
            catch {}
        }
    }
    
    return undefined;
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
        var crossSectionData = processCrossSections(context, id + "process", definition);
        
        // Compute CLT mechanical properties (ABD, EI, neutral axis)
        crossSectionData = computeCLTProperties(crossSectionData);
        
        // Debug: print EI at each section
        for (var i = 0; i < size(crossSectionData.crossSections); i += 1)
        {
            var mp = crossSectionData.crossSections[i].mechanicalProperties;
            println("Section " ~ (i + 1) ~ " | EI=" ~ mp.EI_eff ~ " | NA=" ~ mp.neutralAxisY);
        }
        
        // Build analysis name prefix
        var namePrefix = "";
        try
        {
            if (definition.analysisName != undefined && definition.analysisName != "")
            {
                namePrefix = definition.analysisName;
            }
        }
        catch {}
        
        // --- Create EI curve ---
        createEICurve(context, id + "eiCurve", crossSectionData, namePrefix);
        
        // --- Create neutral axis curve ---
        createNeutralAxisCurve(context, id + "naCurve", crossSectionData, namePrefix);
        
        // --- Beam stiffness analysis ---
        var fcpX = resolveReferencePointX(context, definition.fcpQuery, definition.xSectAlong);
        var acpX = resolveReferencePointX(context, definition.acpQuery, definition.xSectAlong);

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

        // --- Compute total weight and build table data ---
        var totalWeight = 0 * kilogram;
        var beamLength = undefined;
        if (beamAnalysisResults != undefined)
        {
            beamLength = beamAnalysisResults.L;
        }
        else if (size(crossSectionData.crossSections) > 1)
        {
            // Approximate beam length from first to last section
            var firstX = crossSectionData.crossSections[0].frame.origin[0];
            var lastX = crossSectionData.crossSections[size(crossSectionData.crossSections) - 1].frame.origin[0];
            beamLength = abs(lastX - firstX);
        }

        if (beamLength != undefined && beamLength > 0 * meter)
        {
            for (var section in crossSectionData.crossSections)
            {
                var linealDensity = 0 * kilogram / meter;
                for (var contrib in section.mechanicalProperties.bodyContributions)
                {
                    if (contrib.linealDensity != undefined)
                    {
                        linealDensity = linealDensity + contrib.linealDensity;
                    }
                }
                // Approximate: weight = lineal density × section spacing
                totalWeight = totalWeight + linealDensity * (beamLength / size(crossSectionData.crossSections));
            }
        }

        var tableData = buildTableData(crossSectionData.crossSections, beamAnalysisResults, totalWeight);

        // --- Store data on origin for future reuse ---
        try
        {
            storeAnalysisData(context, id, crossSectionData.bodies, crossSectionData, beamAnalysisResults, tableData);

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

        if (definition.createComposites)
        {
            createCompositeWires(context, id, crossSectionData);
        }

        if (definition.debug)
        {
            debugVisualization(context, id, crossSectionData, definition);
        }
    });


// =============================================================================
// CROSS-SECTION PROCESSING
// =============================================================================

/**
 * Process all cross-sections and extract B-spline curves, points, and properties.
 *
 * Builds the bodies array from definition.bodyArray, resolving material data
 * from the CSV and user overrides here (not in editing logic) because
 * ValueWithUnits maps don't survive definition serialization.
 */
function processCrossSections(context is Context, id is Id, definition is map) returns map
{
    var frames = getCrossSectionFrames(context, definition.xSectAlong, definition.numSections);
    
    // -----------------------------------------------------------------
    // Parse CSV for material resolution (re-parsed here because complex
    // maps with ValueWithUnits don't survive from editing logic)
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
    catch {}
    
    // -----------------------------------------------------------------
    // Build top-level bodies array from editing logic's bodyArray,
    // resolving material data and computing volume
    // -----------------------------------------------------------------
    var bodies = [];
    for (var i = 0; i < size(definition.bodyArray); i += 1)
    {
        var bodyDef = definition.bodyArray[i];
        var bodyEntry = {
            "bodyQuery" : bodyDef.bodyQuery,
            "bodyIdx" : i,
            "bodyName" : tryGetKey(bodyDef, "bodyName"),
            "materialName" : tryGetKey(bodyDef, "materialName"),
            "hasMaterialData" : false
        };
        
        // --- Resolve material data from CSV ---
        var matName = tryGetKey(bodyDef, "materialName");
        if (matName != undefined && matName != "Not assigned")
        {
            var key = normalizeMaterialName(matName);
            var csvMatch = materialLookup[key];
            if (csvMatch != undefined)
            {
                bodyEntry.hasMaterialData = true;
                bodyEntry.materialData = csvMatch;
            }
        }
        
        // --- If no CSV match, resolve from user overrides ---
        if (!bodyEntry.hasMaterialData)
        {
            var behavior = tryGetKey(bodyDef, "materialBehavior");
            if (behavior == MaterialBehavior.PROVIDE_DATA)
            {
                bodyEntry = resolveOverrideMaterialData(bodyDef, bodyEntry);
            }
        }
        
        // --- Compute volume ---
        var bodyVolume = 0 * meter^3;
        try
        {
            bodyVolume = evVolume(context, { "entities" : bodyDef.bodyQuery });
        }
        catch {}
        bodyEntry.volume = bodyVolume;
        
        bodies = append(bodies, bodyEntry);
    }
    
    var bodyQueries = mapArray(bodies, function(b) { return b.bodyQuery; });

    // Build body index map for O(1) lookups (Phase 2 optimization)
    var bodyIndexMap = {};
    for (var i = 0; i < size(bodyQueries); i += 1)
    {
        var entities = evaluateQuery(context, bodyQueries[i]);
        if (size(entities) > 0)
        {
            // Use first entity as key (solid bodies typically have single entity)
            bodyIndexMap[toString(entities[0])] = i;
        }
    }

    var crossSections = [];

    println("Processing " ~ size(frames) ~ " cross-sections...");

    for (var i = 0; i < size(frames); i += 1)
    {
        // Progress indicator every 10 sections
        if (i % 10 == 0 && i > 0)
        {
            println("  Section " ~ i ~ " / " ~ size(frames) ~ " (" ~ floor(100.0 * i / size(frames)) ~ "%)");
        }

        var frame = frames[i];
        var xSectPlane = plane(frame.origin, frame.zAxis);
        
        opPlane(context, id + ("plane" ~ i), { "plane" : xSectPlane });
        var planeQ = qCreatedBy(id + ("plane" ~ i), EntityType.FACE);
        
        var intersectingBodies = evaluateQuery(context, qIntersectsPlane(qUnion(bodyQueries), xSectPlane));
        
        var wireQueries = [];
        var allBSplines = [];
        var bodyToCurves = {};
        
        // PHASE A: Intersect all bodies and extract B-splines
        for (var b = 0; b < size(intersectingBodies); b += 1)
        {
            var body = intersectingBodies[b];

            // Use O(1) map lookup instead of O(n) search
            var bodyIdx = bodyIndexMap[toString(body)];
            if (bodyIdx == undefined)
            {
                println("WARNING: Body not found in index map at section " ~ i);
                continue;
            }

            opIntersectFaces(context, id + ("intersect" ~ i ~ "_" ~ b), {
                    "tools" : planeQ,
                    "targets" : body
            });
            
            wireQueries = append(wireQueries, qCreatedBy(id + ("intersect" ~ i ~ "_" ~ b), EntityType.BODY));
            
            var edges = evaluateQuery(context, qCreatedBy(id + ("intersect" ~ i ~ "_" ~ b), EntityType.EDGE));
            var bodyCurves = [];
            
            for (var e = 0; e < size(edges); e += 1)
            {
                var bspline = evApproximateBSplineCurve(context, { "edge" : edges[e] });
                
                var curveData = {
                    "bSplineCurve" : bspline,
                    "bodyIndices" : [bodyIdx],
                    "bbox2D" : computeCurveBoundingBox2D(bspline, xSectPlane)
                };
                
                allBSplines = append(allBSplines, curveData);
                bodyCurves = append(bodyCurves, curveData);
            }
            
            bodyToCurves[bodyIdx] = bodyCurves;
        }
        
        // PHASE B: Deduplicate if creating composites
        var finalCurves = allBSplines;
        if (definition.createComposites)
        {
            finalCurves = getUniqueCurvesOptimized(allBSplines, OVERLAP_TOL);
        }
        
        // PHASE C: Build bodyData using triangulation module
        var sectionPoints = [];
        var spatialGrid = {};  // Phase 4: Spatial grid for O(1) point deduplication
        var bodyData = [];

        for (var body in intersectingBodies)
        {
            // Use O(1) map lookup instead of O(n) search
            var bodyIdx = bodyIndexMap[toString(body)];
            if (bodyIdx == undefined)
            {
                println("WARNING: Body not found in index map at section " ~ i);
                continue;
            }

            var bodyCurves = bodyToCurves[bodyIdx];
            if (bodyCurves == undefined)
                bodyCurves = [];

            var result = processBodyCurves(bodyCurves, frame, sectionPoints, spatialGrid);
            sectionPoints = result.sectionPoints;
            spatialGrid = result.spatialGrid;
            
            bodyData = append(bodyData, {
                "bodyIdx" : bodyIdx,
                "groups" : result.bodyData.groups,
                "totalSectionProperties" : result.bodyData.totalSectionProperties,
                "boundingBox" : result.bodyData.boundingBox
            });
        }

        // Aggregate bounding boxes from all bodies at this section
        var overallMinX = undefined;
        var overallMaxX = undefined;
        var overallMinY = undefined;
        var overallMaxY = undefined;

        for (var bodyEntry in bodyData)
        {
            var bbox = bodyEntry.boundingBox;
            if (overallMinX == undefined || bbox.minX < overallMinX) overallMinX = bbox.minX;
            if (overallMaxX == undefined || bbox.maxX > overallMaxX) overallMaxX = bbox.maxX;
            if (overallMinY == undefined || bbox.minY < overallMinY) overallMinY = bbox.minY;
            if (overallMaxY == undefined || bbox.maxY > overallMaxY) overallMaxY = bbox.maxY;
        }

        var sectionBoundingBox = {
            "minX" : overallMinX,
            "maxX" : overallMaxX,
            "minY" : overallMinY,
            "maxY" : overallMaxY,
            "width" : (overallMaxX - overallMinX),
            "height" : (overallMaxY - overallMinY)
        };

        // Strip bbox2D from final output curves
        var outputCurves = mapArray(finalCurves, function(c) {
            return {
                "bSplineCurve" : c.bSplineCurve,
                "bodyIndices" : c.bodyIndices
            };
        });

        // Cleanup
        try { opDeleteBodies(context, id + ("deletePlane" ~ i), { "entities" : qCreatedBy(id + ("plane" ~ i), EntityType.BODY) }); }
        catch {}
        if (size(wireQueries) > 0)
        {
            try { opDeleteBodies(context, id + ("deleteWires" ~ i), { "entities" : qUnion(wireQueries) }); }
            catch {}
        }

        crossSections = append(crossSections, {
            "frame" : frame,
            "sectionPoints" : sectionPoints,
            "bSplineCurves" : outputCurves,
            "bodyData" : bodyData,
            "boundingBox" : sectionBoundingBox
        });
    }

    println("Cross-section processing complete: " ~ size(crossSections) ~ " sections analyzed");

    return {
        "bodies" : bodies,
        "crossSections" : crossSections
    };
}


// =============================================================================
// MATERIAL RESOLUTION (FEATURE BODY)
// =============================================================================

/**
 * Build materialData from user-provided override values stored in definition.
 *
 * Called during feature execution (not editing logic) because ValueWithUnits
 * maps don't survive definition serialization.
 *
 * @param bodyDef {map} : The definition bodyArray entry (with override fields)
 * @param bodyEntry {map} : The bodies array entry being built
 * @returns {map} : Updated bodyEntry with materialData attached
 */
function resolveOverrideMaterialData(bodyDef is map, bodyEntry is map) returns map
{
    var updated = bodyEntry;
    try
    {
        var density = bodyDef.overrideDensity * kilogram / meter^3;
        var name = tryGetKey(bodyDef, "overrideName");
        if (name == undefined)
            name = "User-defined";
        
        var qMatrix = undefined;
        var youngsModulus = undefined;
        var poissonsRatio = undefined;
        
        var matType = tryGetKey(bodyDef, "materialType");
        if (matType == MaterialType.ISOTROPIC)
        {
            var E = bodyDef.youngsModulus * megapascal;
            qMatrix = isotropicQMatrix(E, 0.33);
            youngsModulus = E;
            poissonsRatio = 0.33;
        }
        else if (matType == MaterialType.ORTHOTROPIC)
        {
            var E1 = bodyDef.E1 * megapascal;
            var E2 = bodyDef.E2 * megapascal;
            var G12 = bodyDef.G12 * megapascal;
            var nu12 = bodyDef.nu12;
            qMatrix = orthotropicQMatrix(E1, E2, G12, nu12);
            youngsModulus = E1;
            poissonsRatio = nu12;
        }
        
        if (qMatrix != undefined)
        {
            updated.hasMaterialData = true;
            updated.materialData = {
                "originalName" : name,
                "category" : "User-defined",
                "density" : density,
                "poissonsRatio" : poissonsRatio,
                "youngsModulus" : youngsModulus,
                "qMatrix" : qMatrix
            };
        }
    }
    catch {}
    return updated;
}


// =============================================================================
// REFERENCE POINT RESOLUTION
// =============================================================================

/**
 * Store analysis data as attribute on origin point.
 *
 * @param context {Context}
 * @param id {Id} : Feature ID
 * @param bodies {array} : Body configuration data
 * @param crossSectionData {map} : Full cross-section analysis results
 * @param beamAnalysis {map} : Beam stiffness results (or undefined)
 * @param tableData {map} : Formatted table data
 */
function storeAnalysisData(context is Context, id is Id, bodies is array,
                           crossSectionData is map, beamAnalysis, tableData is map)
{
    // Build feature-specific data structure
    var featureKey = toAttributeId(id);

    // Extract body details
    var bodyDetails = [];
    for (var bodyEntry in bodies)
    {
        var bodyDetail = {
            "bodyIndex" : bodyEntry.bodyIndex,
            "bodyName" : bodyEntry.bodyName,
            "materialName" : bodyEntry.materialName,
            "materialData" : bodyEntry.materialData,
            "volume" : bodyEntry.volume
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
            if (contrib.linealDensity != undefined)
            {
                linealDensity = linealDensity + contrib.linealDensity;
            }
        }

        var sectionDetail = {
            "index" : i,
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

    // Build complete data structure
    var analysisData = {
        "details" : {
            "bodies" : bodyDetails,
            "crossSections" : sectionDetails,
            "beamAnalysis" : beamAnalysis
        },
        "tableData" : tableData
    };

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

/**
 * Build formatted table data for export.
 *
 * @param crossSections {array} : Cross-section data with mechanical properties
 * @param beamAnalysis {map} : Beam stiffness results (or undefined if not computed)
 * @param totalWeight {ValueWithUnits} : Total beam weight
 * @returns {map} : { summary: array, crossSections: array }
 */
function buildTableData(crossSections is array, beamAnalysis, totalWeight is ValueWithUnits) returns map
{
    // Summary table
    var summaryTable = [];

    if (beamAnalysis != undefined)
    {
        summaryTable = append(summaryTable, ["Prismatic stiffness (lb/in)", beamAnalysis.prismaticStiffness_lbin]);
        summaryTable = append(summaryTable, ["Prismatic stiffness (mm/30kg)", beamAnalysis.prismaticStiffness_mm]);
        summaryTable = append(summaryTable, ["Estimated stiffness (lb/in)", beamAnalysis.estimatedStiffness_lbin]);
        summaryTable = append(summaryTable, ["Estimated stiffness (mm/30kg)", beamAnalysis.estimatedStiffness_mm]);
    }

    summaryTable = append(summaryTable, ["Weight (kg)", totalWeight / kilogram]);

    // Cross-section table header
    var csTable = [
        ["Section #", "X Coord (mm)", "EI (N·m²)", "NA Height (mm)", "Beam Height (mm)", "Beam Width (mm)", "Lineal Density (kg/m)"]
    ];

    // Add data rows
    for (var i = 0; i < size(crossSections); i += 1)
    {
        var section = crossSections[i];
        var xCoord = section.frame.origin[0] / millimeter;  // World X in mm
        var EI = section.mechanicalProperties.EI_eff / (newton * meter * meter);
        var naHeight = section.mechanicalProperties.neutralAxisY / millimeter;
        var beamHeight = section.boundingBox.height / millimeter;
        var beamWidth = section.boundingBox.width / millimeter;

        // Sum lineal density across all bodies
        var linealDensity = 0 * kilogram / meter;
        for (var contrib in section.mechanicalProperties.bodyContributions)
        {
            if (contrib.linealDensity != undefined)
            {
                linealDensity = linealDensity + contrib.linealDensity;
            }
        }
        var linealDensityVal = linealDensity / (kilogram / meter);

        csTable = append(csTable, [i, xCoord, EI, naHeight, beamHeight, beamWidth, linealDensityVal]);
    }

    return {
        "summary" : summaryTable,
        "crossSections" : csTable
    };
}

/**
 * Resolve a reference point query (FCP or ACP) to a world X coordinate.
 *
 * Accepts:
 *   - Vertex: projects vertex position onto world X
 *   - Mate connector: projects mate connector origin onto world X
 *   - Planar face: validates normal is parallel to world X (no Y component),
 *     then uses the plane origin X coordinate
 *
 * @param context {Context}
 * @param refQuery {Query} : The FCP or ACP query
 * @param edgeQuery {Query} : The cross-section edge (for future plane intersection)
 * @returns : World X coordinate (ValueWithUnits), or undefined if unresolvable
 */
function resolveReferencePointX(context is Context, refQuery is Query, edgeQuery is Query)
{
    var entities = evaluateQuery(context, refQuery);
    if (size(entities) == 0)
        return undefined;
    
    // --- Try as vertex ---
    try
    {
        var vertexEntities = evaluateQuery(context, qEntityFilter(refQuery, EntityType.VERTEX));
        if (size(vertexEntities) > 0)
        {
            var pos = evVertexPoint(context, { "vertex" : vertexEntities[0] });
            return pos[0];
        }
    }
    catch {}
    
    // --- Try as mate connector ---
    try
    {
        var mateEntities = evaluateQuery(context, qBodyType(refQuery, BodyType.MATE_CONNECTOR));
        if (size(mateEntities) > 0)
        {
            var csys = evMateConnector(context, { "mateConnector" : mateEntities[0] });
            return csys.origin[0];
        }
    }
    catch {}
    
    // --- Try as planar face ---
    try
    {
        var faceEntities = evaluateQuery(context, qGeometry(refQuery, GeometryType.PLANE));
        if (size(faceEntities) > 0)
        {
            var facePlane = evPlane(context, { "face" : faceEntities[0] });
            
            // Validate: plane normal must be parallel to world X (no Y component)
            if (abs(facePlane.normal[1]) > 0.01)
            {
                throw regenError("FCP/ACP plane must be normal to the ski axis (world X). This plane has a Y component in its normal.");
            }
            
            return facePlane.origin[0];
        }
    }
    catch (e)
    {
        if (e is map && tryGetKey(e, "message") != undefined)
            throw e;  // Re-throw our validation error
    }
    
    return undefined;
}


// =============================================================================
// CURVE CREATION
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
function createEICurve(context is Context, id is Id, crossSectionData is map, namePrefix is string)
{
    var sections = crossSectionData.crossSections;
    if (size(sections) < 2)
        return;
    
    var points = [];
    for (var section in sections)
    {
        var worldX = section.frame.origin[0];
        var EI_eff = section.mechanicalProperties.EI_eff;
        
        // Scale: 1 N*m^2 = 1mm of height in world Z
        var zHeight = (EI_eff / (newton * meter * meter)) * millimeter;
        
        points = append(points, vector(worldX, 0 * meter, zHeight));
    }
    
    opFitSpline(context, id, {
            "points" : points
    });
    
    var curveName = "EI_curve";
    if (namePrefix != "")
    {
        curveName = namePrefix ~ "_EI";
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
function createNeutralAxisCurve(context is Context, id is Id, crossSectionData is map, namePrefix is string)
{
    var sections = crossSectionData.crossSections;
    if (size(sections) < 2)
        return;
    
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
    
    opFitSpline(context, id, {
            "points" : points
    });
    
    var curveName = "neutral_axis";
    if (namePrefix != "")
    {
        curveName = namePrefix ~ "_neutralAxis";
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


// =============================================================================
// 2D BOUNDING BOX (Plane-Local Coordinates)
// =============================================================================

/**
 * Compute 2D bounding box for a B-spline in plane-local coordinates.
 */
function computeCurveBoundingBox2D(curve is BSplineCurve, sectionPlane is Plane) returns map
{
    var cps = curve.controlPoints;
    
    if (size(cps) == 0)
        return { "minX" : 0 * meter, "maxX" : 0 * meter, "minY" : 0 * meter, "maxY" : 0 * meter };
    
    var pt0_2D = worldToPlane(sectionPlane, cps[0]);
    var minX = pt0_2D[0];
    var maxX = pt0_2D[0];
    var minY = pt0_2D[1];
    var maxY = pt0_2D[1];
    
    for (var j = 1; j < size(cps); j += 1)
    {
        var pt2D = worldToPlane(sectionPlane, cps[j]);
        minX = min(minX, pt2D[0]);
        maxX = max(maxX, pt2D[0]);
        minY = min(minY, pt2D[1]);
        maxY = max(maxY, pt2D[1]);
    }
    
    return {
        "minX" : minX,
        "maxX" : maxX,
        "minY" : minY,
        "maxY" : maxY
    };
}

/**
 * Check if two 2D bounding boxes overlap (with tolerance).
 */
function boundingBoxes2DOverlap(boxA is map, boxB is map, tol is ValueWithUnits) returns boolean
{
    if (boxA.maxX + tol < boxB.minX || boxB.maxX + tol < boxA.minX)
        return false;
    if (boxA.maxY + tol < boxB.minY || boxB.maxY + tol < boxA.minY)
        return false;
    
    return true;
}

// =============================================================================
// HELPER FUNCTIONS
// =============================================================================


/**
 * Safe map key access.
 */
function tryGetKey(m is map, key is string)
{
    try { return m[key]; }
    return undefined;
}

// =============================================================================
// OPTIMIZED UNIQUE CURVE DETECTION
// =============================================================================

/**
 * Get unique curves with optimizations:
 * 1. 2D bounding box pre-filter
 * 2. Integer body index comparison
 * 3. Minimum curve length protection
 */
function getUniqueCurvesOptimized(inputCurves is array, tolerance is ValueWithUnits) returns array
{
    if (size(inputCurves) <= 1)
        return inputCurves;
    
    var tol = tolerance;
    var unique = [];
    
    for (var i = 0; i < size(inputCurves); i += 1)
    {
        var current = inputCurves[i];
        
        // PROTECTION: Don't dedupe tiny curves
        var curveLength = approximateControlPolygonLength(current.bSplineCurve);
        if (curveLength < MIN_CURVE_LENGTH)
        {
            unique = append(unique, current);
            continue;
        }
        
        var dominated = false;
        
        for (var j = 0; j < size(unique); j += 1)
        {
            var candidate = unique[j];
            
            // 2D bounding box pre-filter
            if (!boundingBoxes2DOverlap(current.bbox2D, candidate.bbox2D, tol))
                continue;
            
            // Skip same-body comparisons
            if (curvesShareBodyIndex(current, candidate))
                continue;
            
            // Check if endpoints touch
            if (!curveEndpointsTouch(current.bSplineCurve, candidate.bSplineCurve, tol))
                continue;
            
            // Full overlap detection
            var overlapResult = detectCurveOverlap(current.bSplineCurve, candidate.bSplineCurve, tol);
            
            if (overlapResult.overlapType == OverlapType.NONE)
                continue;
            
            if (overlapResult.overlapType == OverlapType.FULL_CONTAINMENT)
            {
                if (overlapResult.aContainsB)
                {
                    unique[j] = mergeCurveBodies(current, candidate);
                    dominated = true;
                }
                else
                {
                    unique[j] = mergeCurveBodies(candidate, current);
                    dominated = true;
                }
                break;
            }
            else if (overlapResult.overlapType == OverlapType.PARTIAL_OVERLAP)
            {
                unique[j] = mergeCurveBodies(candidate, current);
                dominated = true;
                break;
            }
        }
        
        if (!dominated)
        {
            unique = append(unique, current);
        }
    }
    
    return unique;
}



/**
 * Check if two curves share a body using integer indices.
 */
function curvesShareBodyIndex(curveA is map, curveB is map) returns boolean
{
    for (var idxA in curveA.bodyIndices)
    {
        for (var idxB in curveB.bodyIndices)
        {
            if (idxA == idxB)
                return true;
        }
    }
    return false;
}

/**
 * Quick check: do curve endpoints touch?
 */
function curveEndpointsTouch(curveA is BSplineCurve, curveB is BSplineCurve, tol is ValueWithUnits) returns boolean
{
    var cpA = curveA.controlPoints;
    var cpB = curveB.controlPoints;
    
    if (size(cpA) == 0 || size(cpB) == 0)
        return false;
    
    var endpointsA = [cpA[0], cpA[size(cpA) - 1]];
    var endpointsB = [cpB[0], cpB[size(cpB) - 1]];
    
    for (var ptA in endpointsA)
    {
        for (var ptB in endpointsB)
        {
            if (norm(ptA - ptB) < tol)
                return true;
        }
    }
    
    for (var ptA in endpointsA)
    {
        var closest = closestPointOnPolyline(ptA, cpB);
        if (closest.distance < tol)
            return true;
    }
    
    return false;
}

/**
 * Detect overlap between two curves with early-abort optimization.
 *
 * Checks control point proximity to classify overlap:
 * - FULL_CONTAINMENT: >=80% of points from one curve lie on the other
 * - PARTIAL_OVERLAP: >=2 points match but not full containment
 * - NONE: <2 points match or early-abort triggered
 *
 * Early-abort logic:
 * - Success: Stop once 80% threshold reached (no need to check remaining points)
 * - Failure: Abort if first 3 points show no match (curves likely don't overlap)
 */
function detectCurveOverlap(curveA is BSplineCurve, curveB is BSplineCurve, tol is ValueWithUnits) returns map
{
    var cpA = curveA.controlPoints;
    var cpB = curveB.controlPoints;
    var nA = size(cpA);
    var nB = size(cpB);

    // Count how many points from A are on B
    var aOnB = 0;
    var minNeededForA = ceil(nA * 0.8);  // 80% threshold

    for (var i = 0; i < nA; i += 1)
    {
        var pt = cpA[i];
        var closest = closestPointOnPolyline(pt, cpB);
        if (closest.distance < tol)
            aOnB += 1;

        // Early success - if we've reached 80%, stop checking
        if (aOnB >= minNeededForA)
            break;

        // Early failure - if we've checked 3 points and none match, abort
        if (i >= 2 && aOnB == 0)
        {
            return { "overlapType" : OverlapType.NONE, "aContainsB" : false };
        }
    }

    // Count how many points from B are on A (similar optimization)
    var bOnA = 0;
    var minNeededForB = ceil(nB * 0.8);

    for (var i = 0; i < nB; i += 1)
    {
        var pt = cpB[i];
        var closest = closestPointOnPolyline(pt, cpA);
        if (closest.distance < tol)
            bOnA += 1;

        // Early success
        if (bOnA >= minNeededForB)
            break;

        // Early failure
        if (i >= 2 && bOnA == 0)
        {
            return { "overlapType" : OverlapType.NONE, "aContainsB" : false };
        }
    }

    // Classification logic (unchanged)
    var aFullyOnB = (aOnB >= nA * 0.8);
    var bFullyOnA = (bOnA >= nB * 0.8);

    if (aFullyOnB && bFullyOnA)
    {
        return { "overlapType" : OverlapType.FULL_CONTAINMENT, "aContainsB" : (nA >= nB) };
    }
    else if (aFullyOnB)
    {
        return { "overlapType" : OverlapType.FULL_CONTAINMENT, "aContainsB" : false };
    }
    else if (bFullyOnA)
    {
        return { "overlapType" : OverlapType.FULL_CONTAINMENT, "aContainsB" : true };
    }
    else if (aOnB >= 2 || bOnA >= 2)
    {
        return { "overlapType" : OverlapType.PARTIAL_OVERLAP, "aContainsB" : false };
    }

    return { "overlapType" : OverlapType.NONE, "aContainsB" : false };
}

/**
 * Merge body ownership from two curves.
 */
function mergeCurveBodies(keeper is map, donor is map) returns map
{
    var mergedIndices = keeper.bodyIndices;
    
    for (var idx in donor.bodyIndices)
    {
        if (!isIn(idx, mergedIndices))
        {
            mergedIndices = append(mergedIndices, idx);
        }
    }
    
    return {
        "bSplineCurve" : keeper.bSplineCurve,
        "bodyIndices" : mergedIndices,
        "bbox2D" : keeper.bbox2D
    };
}

// =============================================================================
// GEOMETRY CREATION
// =============================================================================
// NOTE: closestPointOnPolyline() and closestPointOnSegment() now imported from xsectUtils

/**
 * Create composite wire bodies from cross-section data.
 */
function createCompositeWires(context is Context, id is Id, data is map)
{
    for (var i = 0; i < size(data.crossSections); i += 1)
    {
        var section = data.crossSections[i];
        var createdBodies = [];
        
        for (var s = 0; s < size(section.bSplineCurves); s += 1)
        {
            var curve = section.bSplineCurves[s].bSplineCurve;
            
            opCreateBSplineCurve(context, id + ("curve" ~ i ~ "_" ~ s), {
                    "bSplineCurve" : curve
            });
            
            createdBodies = append(createdBodies, qCreatedBy(id + ("curve" ~ i ~ "_" ~ s), EntityType.BODY));
        }
        
        if (size(createdBodies) > 0)
        {
            opCreateCompositePart(context, id + ("composite" ~ i), {
                    "bodies" : qUnion(createdBodies),
                    "closed" : true
            });
            
            setProperty(context, {
                    "entities" : qCompositePartsContaining(qUnion(createdBodies)),
                    "propertyType" : PropertyType.NAME,
                    "value" : "XSect " ~ i ~ " Composite"
            });
        }
    }
}

// =============================================================================
// DEBUG VISUALIZATION
// =============================================================================

/**
 * Render debug visualization for cross-sections.
 */
function debugVisualization(context is Context, id is Id, data is map, definition is map)
{
    var sectionIndices = [];
    if (definition.debugAllXSections)
    {
        for (var i = 0; i < size(data.crossSections); i += 1)
            sectionIndices = append(sectionIndices, i);
    }
    else
    {
        sectionIndices = mapArray(definition.debugXSections, function(x) { return x.xSectionNum - 1; });
    }
    
    // Get selected debug body indices (only if not debugging all bodies)
    var debugBodyIndices = [];
    if (!definition.debugAllBodies)
    {
        var debugBodiesQuery = evaluateQuery(context, definition.debugBodies);
        for (var i = 0; i < size(data.bodies); i += 1)
        {
            for (var debugBody in debugBodiesQuery)
            {
                if (areQueriesEquivalent(context, data.bodies[i].bodyQuery, debugBody))
                {
                    debugBodyIndices = append(debugBodyIndices, i);
                    break;
                }
            }
        }
    }
    
    for (var idx in sectionIndices)
    {
        if (idx < 0 || idx >= size(data.crossSections))
            continue;
        
        var section = data.crossSections[idx];
        var sectionPoints = section.sectionPoints;
        
        if (definition.printBodyData)
        {
            println("=== Cross Section " ~ (idx + 1) ~ " ===");
            println("Total section points: " ~ size(sectionPoints));
        }
        
        for (var bodyInfo in section.bodyData)
        {
            var bodyIdx = bodyInfo.bodyIdx;
            
            // Skip if filtering bodies and this one isn't selected
            if (!definition.debugAllBodies && !isIn(bodyIdx, debugBodyIndices))
                continue;
            
            var color = DEBUG_COLOR_SEQUENCE[bodyIdx % size(DEBUG_COLOR_SEQUENCE)];
            
            if (definition.printBodyData)
            {
                println("--- Body " ~ bodyIdx ~ " ---");
                println("  Groups: " ~ size(bodyInfo.groups));
                printGroupData(bodyInfo.groups, sectionPoints, 1, definition.printTriangles);
            }
            
            if (definition.debugType == XSectionDebugType.EDGES)
            {
                var bodyCurves = filter(section.bSplineCurves, function(c) {
                    return isIn(bodyIdx, c.bodyIndices);
                });
                for (var curveData in bodyCurves)
                {
                    debugControlPolygon(context, curveData.bSplineCurve, color);
                }
            }
            else if (definition.debugType == XSectionDebugType.POINTS)
            {
                debugGroupPoints(context, bodyInfo.groups, sectionPoints, color);
            }
            else if (definition.debugType == XSectionDebugType.MESH)
            {
                debugGroupMesh(context, bodyInfo.groups, sectionPoints, color);
            }
        }
    }
}

/**
 * Recursively draw perimeter points from groups and subgroups.
 */
function debugGroupPoints(context is Context, groups is array, sectionPoints is array, color is DebugColor)
{
    for (var group in groups)
    {
        for (var idx in group.perimeterPointIndices)
        {
            addDebugPoint(context, sectionPoints[idx].point3D, color);
        }
        debugGroupPoints(context, group.subgroups, sectionPoints, color);
    }
}

/**
 * Recursively draw triangle mesh from groups and subgroups.
 */
function debugGroupMesh(context is Context, groups is array, sectionPoints is array, color is DebugColor)
{
    for (var group in groups)
    {
        for (var tri in group.triangles)
        {
            var p0 = sectionPoints[tri[0]].point3D;
            var p1 = sectionPoints[tri[1]].point3D;
            var p2 = sectionPoints[tri[2]].point3D;
            
            if(norm(p1 - p0) > 1e-6 * meter)
            {
                addDebugLine(context, p0, p1, color);
            }
            
            if(norm(p2 - p1) > 1e-6 * meter)
            {
                addDebugLine(context, p1, p2, color);
            }
                
            if(norm(p0 - p2) > 1e-6 * meter)
            {
                addDebugLine(context, p2, p0, color);
            }
        }
        debugGroupMesh(context, group.subgroups, sectionPoints, color);
    }
}

/**
 * Recursively print group data for debugging.
 */
function printGroupData(groups is array, sectionPoints is array, depth is number, printTriangles is boolean)
{
    var indent = "";
    for (var d = 0; d < depth; d += 1)
        indent = indent ~ "  ";
    
    for (var g = 0; g < size(groups); g += 1)
    {
        var group = groups[g];
        println(indent ~ "Group " ~ g ~ ":");
        println(indent ~ "  Perimeter points: " ~ size(group.perimeterPointIndices));
        println(indent ~ "  Triangles: " ~ size(group.triangles));
        println(indent ~ "  Area: " ~ group.sectionProperties.area);
        println(indent ~ "  Subgroups: " ~ size(group.subgroups));
        
        println(indent ~ "  Perimeter coordinates (2D):");
        for (var i = 0; i < size(group.perimeterPointIndices); i += 1)
        {
            var ptIdx = group.perimeterPointIndices[i];
            var pt2D = sectionPoints[ptIdx].point2D;
            println(indent ~ "    [" ~ i ~ "] idx=" ~ ptIdx ~ " X=" ~ pt2D[0] ~ " Y=" ~ pt2D[1]);
        }
        
        if (printTriangles)
        {
            println(indent ~ "  Triangle indices:");
            for (var t = 0; t < size(group.triangles); t += 1)
            {
                var tri = group.triangles[t];
                var p0 = sectionPoints[tri[0]].point2D;
                var p1 = sectionPoints[tri[1]].point2D;
                var p2 = sectionPoints[tri[2]].point2D;
                println(indent ~ "    [" ~ t ~ "] " ~ tri[0] ~ " -> " ~ tri[1] ~ " -> " ~ tri[2]);
                println(indent ~ "      (" ~ p0[0] ~ ", " ~ p0[1] ~ ")");
                println(indent ~ "      (" ~ p1[0] ~ ", " ~ p1[1] ~ ")");
                println(indent ~ "      (" ~ p2[0] ~ ", " ~ p2[1] ~ ")");
            }
        }
        
        if (size(group.subgroups) > 0)
        {
            printGroupData(group.subgroups, sectionPoints, depth + 1, printTriangles);
        }
    }
}
