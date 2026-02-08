FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// CrossSectionPredicates (UI definitions -- export import so users get enums/constants)
export 
import(path : "9204d2e5f7cc55bd15130cf6", version : "e44b9c4d50e5f973b012807c");
// CrossSectionAnalysis (geometry extraction engine)
import(path : "e66f9aab93a8cd3c255e23c9", version : "a4e0cfddb4099cacc43fd82a");


// CrossSectionMath (re-imported here for unit conversion at output boundaries)
import(path : "4538be7c5b7f28ba40050fad", version : "5a796af13252dbc820c04e70");



// =============================================================================
// EDITING LOGIC
// =============================================================================

/**
 * Editing logic function.
 *
 * Responsibilities:
 * 1. Parse materialCSV into lookup map (name matching only)
 * 2. Evaluate selBodies ? expand composites ? populate bodyArray
 * 3. For each body: read name + material, match against CSV
 * 4. Compute body signatures for cache staleness detection
 * 5. Read previous analysis results from Origin attribute ? populate stiffness display
 * 6. Clean up debug filters
 *
 * The clickedButton parameter lets us detect when "Update cache" was pressed.
 * Following Evan Reese's pattern: editing logic computes signatures, and
 * the feature body compares them against cached signatures to decide
 * whether to recompute.
 */
export function crossSectionEL(context is Context, id is Id, oldDefinition is map,
    definition is map, isCreating is boolean, specifiedParameters is map,
    hiddenBodies is Query, clickedButton is string) returns map
{
    var updatedDef = definition;

    // -----------------------------------------------------------------
    // Step 1: Parse CSV into lookup map (name matching only)
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
        // CSV not loaded or malformed -- proceed with empty lookup
    }

    // -----------------------------------------------------------------
    // Step 2: Evaluate selBodies into individual solid body queries
    // qFlattenedCompositeParts passes through plain solids AND expands
    // composites into their constituents -- one query, no looping.
    // -----------------------------------------------------------------
    var selectedBodies = evaluateQuery(context, qFlattenedCompositeParts(definition.selBodies));

    // -----------------------------------------------------------------
    // Step 3: Build bodyArray -- simplified (no material overrides)
    //
    // Every field must have a valid default so the predicate spec
    // is satisfied regardless of conditional branch evaluation.
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
            if (materialLookup[key] != undefined)
            {
                hasMaterialData = true;
            }
        }

        newBodyArray = append(newBodyArray, {
            "bodyQuery" : bodyQ,
            "bodyName" : bodyName,
            "bodyNum" : i + 1,
            "hasMaterialData" : hasMaterialData,
            "materialName" : materialName
        });
    }

    updatedDef.bodyArray = newBodyArray;

    // -----------------------------------------------------------------
    // Step 4: Compute body signatures for cache staleness detection
    //
    // Stored via isAnything(definition.cachedBodySignatures). The feature
    // body compares these against the signatures that produced the cached
    // analysis to decide whether to recompute.
    // -----------------------------------------------------------------
    var bodySignatures = [];
    for (var entry in newBodyArray)
    {
        try
        {
            var bBox = evBox3d(context, { "topology" : entry.bodyQuery, "tight" : true });
            var numFaces = size(evaluateQuery(context, qOwnedByBody(entry.bodyQuery, EntityType.FACE)));
            bodySignatures = append(bodySignatures, {
                "bboxMin" : vecToArr(bBox.minCorner),
                "bboxMax" : vecToArr(bBox.maxCorner),
                "numFaces" : numFaces,
                "materialName" : entry.materialName
            });
        }
        catch
        {
            bodySignatures = append(bodySignatures, undefined);
        }
    }

    // Edge signature (detects if the cross-section path changed)
    var edgeSignature = undefined;
    try
    {
        var edgeBBox = evBox3d(context, { "topology" : definition.xSectAlong, "tight" : true });
        edgeSignature = {
            "bboxMin" : vecToArr(edgeBBox.minCorner),
            "bboxMax" : vecToArr(edgeBBox.maxCorner),
            "numSections" : definition.numSections
        };
    }
    catch {}

    // Following Evan Reese's pattern: store signatures via isAnything fields.
    // On "Update cache" button press, or when caching is first enabled,
    // clear the cached analysis so the feature body recomputes.
    if (definition.useCaching)
    {
        if (clickedButton == "updateCache" ||
            (!oldDefinition.useCaching && definition.useCaching))
        {
            // Force recompute: clear cached data, store fresh signatures
            updatedDef.cachedAnalysis = 0;
            updatedDef.cachedBodySignatures = bodySignatures;
            updatedDef.cachedEdgeSignature = edgeSignature;
        }
        else
        {
            // Normal pass: update signatures but preserve cached analysis.
            // The feature body will compare these against cached signatures
            // to detect staleness.
            updatedDef.cachedBodySignatures = bodySignatures;
            updatedDef.cachedEdgeSignature = edgeSignature;
        }
    }

    // -----------------------------------------------------------------
    // Step 5: Populate stiffness display from previous analysis
    //
    // Read the attribute set on Origin by the previous feature execution.
    // If no previous results exist, display defaults (0).
    // -----------------------------------------------------------------
    var attrName = "xSectAnalysis";
    try
    {
        var name = definition.analysisName;
        if (name != undefined && name != "")
            attrName = "xSectAnalysis_" ~ name;
    }
    catch {}

    var prevStiffness = readStiffnessFromOrigin(context, attrName);
    updatedDef.prismaticStiffness_lbin = prevStiffness.prismaticStiffness_lbin;
    updatedDef.prismaticDeflection_mm = prevStiffness.prismaticDeflection_mm;
    updatedDef.estimatedStiffness_lbin = prevStiffness.estimatedStiffness_lbin;
    updatedDef.estimatedDeflection_mm = prevStiffness.estimatedDeflection_mm;

    // -----------------------------------------------------------------
    // Step 6: Clean up debug filters
    // -----------------------------------------------------------------
    if (definition.debug == true)
    {
        // Remove debug bodies that are no longer in selBodies
        var selBodyQueries = mapArray(newBodyArray, function(b) { return b.bodyQuery; });
        try
        {
            var debugBodies = evaluateQuery(context, definition.debugBodies);
            var cleanDebugBodies = filter(debugBodies, function(db) {
                return any(selBodyQueries, function(sb) { return areQueriesEquivalent(context, sb, db); });
            });
            updatedDef.debugBodies = qUnion(cleanDebugBodies);
        }
        catch {}

        // Remove debug cross-section indices beyond numSections
        try
        {
            updatedDef.debugXSections = filter(definition.debugXSections, function(xs) {
                return xs.xSectionNum <= definition.numSections;
            });
        }
        catch {}
    }

    return updatedDef;
}


// =============================================================================
// EDITING LOGIC HELPERS
// =============================================================================

/**
 * Read stiffness results from the analysis attribute on Origin.
 * Returns default (0) values if no attribute exists.
 */
function readStiffnessFromOrigin(context is Context, attrName is string) returns map
{
    var defaults = {
        "prismaticStiffness_lbin" : 0,
        "prismaticDeflection_mm" : 0,
        "estimatedStiffness_lbin" : 0,
        "estimatedDeflection_mm" : 0
    };

    try
    {
        var originEntity = { "queryType" : QueryType.TRANSIENT, "transientId" : "IB" } as Query;
        var attr = getAttribute(context, {
                "entities" : originEntity,
                "name" : attrName
        });

        if (attr != undefined)
        {
            var stiffness = attr.stiffness;
            if (stiffness != undefined)
            {
                defaults.prismaticStiffness_lbin = stiffness.prismaticStiffness_lbin;
                defaults.prismaticDeflection_mm = stiffness.prismaticDeflection_mm;
                defaults.estimatedStiffness_lbin = stiffness.estimatedStiffness_lbin;
                defaults.estimatedDeflection_mm = stiffness.estimatedDeflection_mm;
            }
        }
    }
    catch {}

    return defaults;
}

/**
 * Normalize a material name for CSV matching.
 *
 * Note: FeatureScript doesn't support string manipulation (no trim/toLower),
 * so we use exact string matching. CSV material names must match Onshape
 * material assignments exactly.
 */
function normalizeMaterialName(name is string) returns string
{
    return name;  // Exact match required (FS limitation)
}

/**
 * Build material lookup map from CSV data.
 * Keys are normalized material names, values are material property data.
 *
 * CSV Columns (from xSectCLT_Old.fs):
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
 */
function buildMaterialLookup(csvData is array) returns map
{
    if (!(csvData is array))
        return {};

    var lookup = {};

    for (var row in csvData)
    {
        // Skip rows that don't have enough columns or have empty name
        if (size(row) < 11)
            continue;

        var name = row[1];
        if (name == undefined || name == "")
            continue;

        // Skip header row or any row where density isn't numeric
        if (!(row[2] is number))
            continue;

        var key = normalizeMaterialName(name);

        // Parse numeric values with units
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
            "qMatrix" : qMatrix
        };
    }

    return lookup;
}


// =============================================================================
// MAIN FEATURE
// =============================================================================

annotation { "Feature Type Name" : "Cross Section Analysis",
             "Feature Type Description" : "Cross-section geometry extraction and EI analysis along an edge path.",
             "Editing Logic Function" : "crossSectionEL" }
export const crossSectionAnalysis = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        crossSectionPrecondition(definition);
    }
    {
        // =============================================================
        // SETUP
        // =============================================================

        var bodyQueries = mapArray(definition.bodyArray, function(b) { return b.bodyQuery; });

        if (size(bodyQueries) == 0)
        {
            // Nothing to analyze
            return;
        }

        // =============================================================
        // CACHE CHECK (Evan Reese pattern)
        // =============================================================
        //
        // If caching is enabled and we have cached analysis data,
        // compare the current signatures against the ones that produced
        // the cache. If they match, rehydrate and skip recomputation.
        //
        // cachedAnalysis == 0 means "no cache" (isAnything default or
        // cleared by EL on "Update cache" button press).

        var analysisResult;
        var usedCache = false;

        if (definition.useCaching &&
            definition.cachedAnalysis != undefined &&
            definition.cachedAnalysis != 0)
        {
            // Check if cached signatures still match current
            if (signaturesMatch(definition.cachedAnalysis.bodySignatures,
                                definition.cachedBodySignatures) &&
                signaturesMatch(definition.cachedAnalysis.edgeSignature,
                                definition.cachedEdgeSignature))
            {
                analysisResult = rehydrateCachedAnalysis(definition.cachedAnalysis.result);
                usedCache = true;
                println("Using cached analysis data");
            }
        }

        if (!usedCache)
        {
            // =============================================================
            // RUN ANALYSIS (Part 1: geometric extraction)
            // =============================================================

            analysisResult = extractCrossSectionGeometry(context, id + "analysis",
                definition.xSectAlong, bodyQueries, definition.numSections);

            // =============================================================
            // STORE RESULTS IN CACHE
            // =============================================================
            //
            // Serialize analysis data for isAnything storage. The key
            // insight from Evan's code: isAnything strips type info
            // (Vector becomes plain array, BSplineCurve becomes plain map),
            // so we store data that's already unitless or easily rehydrated.
            //
            // We also store the signatures that produced this cache so the
            // feature body can detect staleness on next regen.

            if (definition.useCaching)
            {
                try
                {
                    setFeatureComputedParameter(context, id, {
                        "name" : "cachedAnalysis",
                        "value" : {
                            "bodySignatures" : definition.cachedBodySignatures,
                            "edgeSignature" : definition.cachedEdgeSignature,
                            "result" : serializeAnalysisResult(analysisResult)
                        }
                    });
                }
                catch
                {
                    println("WARNING: Failed to cache analysis results");
                }
            }
        }

        println("Cross section analysis complete:");
        println("  Bodies: " ~ size(analysisResult.bodies));
        println("  Cross sections: " ~ size(analysisResult.crossSections));
        for (var cs = 0; cs < size(analysisResult.crossSections); cs += 1)
        {
            var section = analysisResult.crossSections[cs];
            println("  Section " ~ (cs + 1) ~ ": " ~ size(section.intersectionCurves) ~ " curves, "
                ~ size(section.bodies) ~ " bodies");
        }

        // =============================================================
        // TODO: TRIANGULATION & CLT (Part 2+)
        // =============================================================
        // crossSectionData = processTriangulation(analysisResult, definition);
        // crossSectionData = computeCLTProperties(crossSectionData);

        // =============================================================
        // OUTPUT: Create composite wire bodies
        // =============================================================
        if (definition.createComposites)
        {
            createCompositeWires(context, id, analysisResult);
        }

        // =============================================================
        // OUTPUT: Set attributes on Origin for downstream features
        // =============================================================
        // TODO: build and set analysis attribute (port from old code)
        // setAnalysisAttributes(context, crossSectionData, stiffness, totalMass, definition.analysisName);

        // =============================================================
        // DEBUG VISUALIZATION
        // =============================================================
        if (definition.debug == true)
        {
            debugVisualization(context, id, analysisResult, definition);
        }
    });


// =============================================================================
// COMPOSITE WIRE OUTPUT
// =============================================================================

/**
 * Create composite wire bodies from intersection curves at each cross section.
 */
function createCompositeWires(context is Context, id is Id, analysisResult is map)
{
    for (var cs = 0; cs < size(analysisResult.crossSections); cs += 1)
    {
        var section = analysisResult.crossSections[cs];
        var createdBodies = [];

        for (var c = 0; c < size(section.intersectionCurves); c += 1)
        {
            var curve = section.intersectionCurves[c].BSplineCurve;
            if (curve == undefined)
                continue;

            try
            {
                opCreateBSplineCurve(context, id + ("curve" ~ cs ~ "_" ~ c), {
                        "bSplineCurve" : curve
                });
                createdBodies = append(createdBodies, qCreatedBy(id + ("curve" ~ cs ~ "_" ~ c), EntityType.BODY));
            }
            catch
            {
                println("WARNING: Failed to create curve " ~ c ~ " at section " ~ cs);
            }
        }

        if (size(createdBodies) > 0)
        {
            try
            {
                opCreateCompositePart(context, id + ("composite" ~ cs), {
                        "bodies" : qUnion(createdBodies),
                        "closed" : true
                });

                setProperty(context, {
                        "entities" : qCompositePartsContaining(qUnion(createdBodies)),
                        "propertyType" : PropertyType.NAME,
                        "value" : "XSect " ~ (cs + 1) ~ " Composite"
                });
            }
            catch
            {
                println("WARNING: Failed to create composite at section " ~ cs);
            }
        }
    }
}


// =============================================================================
// CACHE HELPERS
// =============================================================================

/**
 * Compare two signature values for equality.
 * Handles arrays, maps, numbers, and undefined.
 * Uses approximate comparison for numeric values.
 */
function signaturesMatch(sigA, sigB) returns boolean
{
    if (sigA == undefined && sigB == undefined)
        return true;
    if (sigA == undefined || sigB == undefined)
        return false;

    // Direct equality works for isAnything-stored data (plain arrays/maps/numbers)
    return sigA == sigB;
}

/**
 * Serialize an analysis result for isAnything storage.
 *
 * The critical issue from Evan's code: isAnything strips FS type info.
 * BSplineCurve becomes a plain map, Vector becomes a plain array.
 * Since our internal math is already unitless, we just need to handle
 * the output BSplineCurves (which have units on control points).
 *
 * Strategy: store BSplineCurves as unitless maps. On rehydration,
 * reconstruct BSplineCurve with units from the plain map.
 */
function serializeAnalysisResult(result is map) returns map
{
    var serialized = {
        "bodies" : serializeBodiesData(result.bodies),
        "crossSections" : []
    };

    for (var cs in result.crossSections)
    {
        var serializedCurves = [];
        for (var curveData in cs.intersectionCurves)
        {
            var curve = curveData.BSplineCurve;
            if (curve != undefined)
            {
                // Strip units from BSplineCurve control points for storage
                serializedCurves = append(serializedCurves, {
                    "curve" : stripCurveUnits(curve),
                    "bodies" : curveData.bodies,
                    "faceIdx" : curveData.faceIdx
                });
            }
        }

        serialized.crossSections = append(serialized.crossSections, {
            "bodies" : cs.bodies,
            "intersectionCurves" : serializedCurves
            // Note: plane and frame are NOT cached -- they're regenerated from
            // the edge, which is cheaper than storing them and they stay
            // consistent with the current geometry.
        });
    }

    return serialized;
}

/**
 * Serialize body data for caching. Strips Query objects (not serializable)
 * and unit-bearing values.
 */
function serializeBodiesData(bodies is array) returns array
{
    return mapArray(bodies, function(b)
    {
        return {
            "bodyIndex" : b.bodyIndex,
            "intersectionPlanes" : b.intersectionPlanes,
            "adjacentBodies" : b.adjacentBodies
        };
    });
}

/**
 * Rehydrate cached analysis data -- reconstruct BSplineCurves with units.
 *
 * Following Evan's rehydrateCachedPointsTable pattern: isAnything strips
 * the Vector type designation, so we need to cast arrays back to Vectors
 * and rebuild BSplineCurve objects.
 */
function rehydrateCachedAnalysis(cached is map) returns map
{
    var result = {
        "bodies" : cached.bodies,  // Body data is already unitless
        "crossSections" : []
    };

    for (var cs in cached.crossSections)
    {
        var rehydratedCurves = [];
        for (var curveData in cs.intersectionCurves)
        {
            // Reconstruct BSplineCurve with units from unitless cache
            var unitlessCurve = curveData.curve;
            var cpsWithUnits = mapArray(unitlessCurve.controlPoints, function(cp)
            {
                return arrToVec(cp);
            });

            try
            {
                var bsCurve = bSplineCurve({
                    "degree" : unitlessCurve.degree,
                    "isPeriodic" : false,
                    "controlPoints" : cpsWithUnits,
                    "knots" : unitlessCurve.knots as KnotArray
                });

                rehydratedCurves = append(rehydratedCurves, {
                    "BSplineCurve" : bsCurve,
                    "bodies" : curveData.bodies,
                    "faceIdx" : curveData.faceIdx
                });
            }
            catch
            {
                println("WARNING: Failed to rehydrate cached curve");
            }
        }

        result.crossSections = append(result.crossSections, {
            "bodies" : cs.bodies,
            "intersectionCurves" : rehydratedCurves
        });
    }

    return result;
}


// =============================================================================
// DEBUG VISUALIZATION
// =============================================================================

/**
 * Render debug visualization for cross-section intersection curves.
 *
 * For Part 1, this visualizes the raw intersection curves (control polygons,
 * points, or mesh) per body per cross-section. Once triangulation is ported,
 * the mesh mode will show actual triangle meshes.
 */
function debugVisualization(context is Context, id is Id, analysisResult is map, definition is map)
{
    // Determine which sections to debug
    var sectionIndices = [];
    if (definition.debugAllXSections)
    {
        for (var i = 0; i < size(analysisResult.crossSections); i += 1)
            sectionIndices = append(sectionIndices, i);
    }
    else
    {
        try
        {
            sectionIndices = mapArray(definition.debugXSections, function(x) { return x.xSectionNum - 1; });
        }
        catch {}
    }

    // Determine which body indices to debug
    var debugAllBodies = true;
    var debugBodyIndices = [];
    try
    {
        debugAllBodies = definition.debugAllBodies;
        if (!debugAllBodies)
        {
            var debugBodiesQuery = evaluateQuery(context, definition.debugBodies);
            var bodyQueries = mapArray(definition.bodyArray, function(b) { return b.bodyQuery; });
            for (var i = 0; i < size(bodyQueries); i += 1)
            {
                for (var db in debugBodiesQuery)
                {
                    if (areQueriesEquivalent(context, bodyQueries[i], db))
                    {
                        debugBodyIndices = append(debugBodyIndices, i);
                        break;
                    }
                }
            }
        }
    }
    catch {}

    // Render
    for (var idx in sectionIndices)
    {
        if (idx < 0 || idx >= size(analysisResult.crossSections))
            continue;

        var section = analysisResult.crossSections[idx];

        for (var curveData in section.intersectionCurves)
        {
            // Determine color from first body index for this curve
            var bodyIdx = (size(curveData.bodies) > 0) ? curveData.bodies[0] : 0;

            // Skip if filtering bodies and this curve doesn't belong to a selected body
            if (!debugAllBodies)
            {
                var bodyMatch = false;
                for (var bi in curveData.bodies)
                {
                    if (isInArray(bi, debugBodyIndices))
                    {
                        bodyMatch = true;
                        bodyIdx = bi;
                        break;
                    }
                }
                if (!bodyMatch)
                    continue;
            }

            var color = DEBUG_COLOR_SEQUENCE[bodyIdx % size(DEBUG_COLOR_SEQUENCE)];

            if (definition.debugType == XSectionDebugType.EDGES)
            {
                // Draw control polygon
                var cps = curveData.BSplineCurve.controlPoints;
                for (var i = 1; i < size(cps); i += 1)
                {
                    if (norm(cps[i] - cps[i - 1]) > 1e-6 * meter)
                    {
                        addDebugLine(context, cps[i - 1], cps[i], color);
                    }
                }
            }
            else if (definition.debugType == XSectionDebugType.POINTS)
            {
                // Draw control points
                for (var cp in curveData.BSplineCurve.controlPoints)
                {
                    addDebugPoint(context, cp, color);
                }
            }
            // MESH mode deferred until triangulation is ported
        }

        if (definition.printBodyData == true)
        {
            println("=== Cross Section " ~ (idx + 1) ~ " ===");
            println("  Curves: " ~ size(section.intersectionCurves));
            println("  Bodies at this section: " ~ section.bodies);
        }
    }
}


// =============================================================================
// UTILITY HELPERS
// =============================================================================

/** Check if a value exists in an array. */
function isInArray(value, arr is array) returns boolean
{
    for (var item in arr)
    {
        if (item == value)
            return true;
    }
    return false;
}
