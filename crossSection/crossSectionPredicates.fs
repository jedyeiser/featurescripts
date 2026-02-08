FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * CROSS SECTION PREDICATES - UI Definitions
 * ==========================================
 *
 * Defines all precondition annotations and UI structure for the
 * Cross Section / EI analysis feature.
 *
 * Body Selection Flow (simplified):
 * ---------------------------------
 * 1. User picks bodies via selBodies (accepts solids and composites)
 * 2. Editing logic evaluates selBodies ? expands composites ? populates bodyArray
 * 3. For each body, EL reads the Onshape material name and searches the CSV
 * 4. CSV match ? hasMaterialData = true (geometry + stiffness)
 *    No match ? hasMaterialData = false (geometry only, excluded from EI)
 *
 * Cache Strategy:
 * ---------------
 * Heavy analysis data (intersection curves, meshes, EI) lives in setVariable.
 * Body signatures (bbox + face count) are stored as undeclared definition fields
 * by editing logic for staleness detection. The feature body checks signatures
 * against cached data to skip recomputation when geometry hasn't changed.
 *
 * The refresh button forces a full recompute regardless of cache validity.
 *
 * Stiffness display fields are populated by editing logic from attributes
 * set on Origin by the previous feature execution.
 */

// =============================================================================
// BOUNDS SPECIFICATIONS
// =============================================================================

export const NUM_XSECTION_BOUNDS = { (unitless) : [3, 50, 200] } as IntegerBoundSpec;

// Stiffness display bounds -- wide enough to show any reasonable result.
// Default 0 signals "not yet computed."
export const STIFFNESS_DISPLAY_BOUNDS = { (unitless) : [-1e6, 0, 1e12] } as RealBoundSpec;

// =============================================================================
// ENUMS
// =============================================================================

export enum XSectionDebugType
{
    annotation { "Name" : "Edges (Control Polygons)" }
    EDGES,
    annotation { "Name" : "Points" }
    POINTS,
    annotation { "Name" : "Mesh" }
    MESH
}

// =============================================================================
// CONSTANTS
// =============================================================================

export const DEBUG_COLOR_SEQUENCE =
[
    DebugColor.RED,
    DebugColor.GREEN,
    DebugColor.BLUE,
    DebugColor.CYAN,
    DebugColor.MAGENTA,
    DebugColor.YELLOW,
    DebugColor.BLACK,
    DebugColor.ORANGE
];

// =============================================================================
// MAIN PRECONDITION
// =============================================================================

export predicate crossSectionPrecondition(definition is map)
{
    // =====================================================================
    // PRIMARY INPUTS
    // =====================================================================

    annotation { "Name" : "Edge to cross-section along",
                 "Filter" : EntityType.EDGE,
                 "MaxNumberOfPicks" : 1,
                 "Description" : "Edge defining cross-section plane locations" }
    definition.xSectAlong is Query;

    annotation { "Name" : "Analysis name",
                 "Default" : "",
                 "Description" : "Optional prefix for output curves and attributes" }
    definition.analysisName is string;

    annotation { "Name" : "FCP (Front Contact Point)",
                 "Filter" : (EntityType.VERTEX) || (EntityType.FACE && GeometryType.PLANE) || (BodyType.MATE_CONNECTOR),
                 "MaxNumberOfPicks" : 1,
                 "Description" : "Front support location for stiffness calculation" }
    definition.fcpQuery is Query;

    annotation { "Name" : "ACP (Aft Contact Point)",
                 "Filter" : (EntityType.VERTEX) || (EntityType.FACE && GeometryType.PLANE) || (BodyType.MATE_CONNECTOR),
                 "MaxNumberOfPicks" : 1,
                 "Description" : "Rear support location for stiffness calculation" }
    definition.acpQuery is Query;

    annotation { "Name" : "Cross section bodies",
                 "Filter" : EntityType.BODY || BodyType.COMPOSITE,
                 "Description" : "Bodies and/or composite parts to include in analysis" }
    definition.selBodies is Query;

    annotation { "Name" : "Material library",
                 "Description" : "CSV with columns: Category, Name, Density, Poisson's Ratio, Young's Modulus, Q11-Q66" }
    definition.materialCSV is TableData;

    annotation { "Name" : "Number of cross sections",
                 "Description" : "Evenly spaced locations along selected edge (includes endpoints)" }
    isInteger(definition.numSections, NUM_XSECTION_BOUNDS);

    // =====================================================================
    // BODIES & MATERIALS (auto-populated by editing logic)
    // =====================================================================
    //
    // No user-editable material overrides. If a body's Onshape material name
    // doesn't match the CSV, it still participates in geometry (cross-section
    // curves, meshing) but is excluded from stiffness/EI calculations.

    annotation { "Group Name" : "Bodies & Materials", "Collapsed By Default" : false }
    {
        annotation { "Name" : "Bodies",
                     "Item name" : "Body",
                     "Item label template" : "#bodyName (#materialName)",
                     "UIHint" : UIHint.PREVENT_ARRAY_REORDER }
        definition.bodyArray is array;

        for (var body in definition.bodyArray)
        {
            annotation { "Name" : "Body",
                         "Filter" : EntityType.BODY && BodyType.SOLID,
                         "MaxNumberOfPicks" : 1,
                         "UIHint" : UIHint.ALWAYS_HIDDEN }
            body.bodyQuery is Query;

            annotation { "Name" : "Body name",
                         "UIHint" : UIHint.READ_ONLY }
            body.bodyName is string;

            annotation { "Name" : "Body number",
                         "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(body.bodyNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Has CSV material data",
                         "Default" : false,
                         "UIHint" : UIHint.ALWAYS_HIDDEN }
            body.hasMaterialData is boolean;

            annotation { "Name" : "Material name",
                         "UIHint" : UIHint.ALWAYS_HIDDEN }
            body.materialName is string;
        }
    }

    // =====================================================================
    // STIFFNESS ESTIMATES (read-only, populated from previous analysis)
    // =====================================================================

    annotation { "Group Name" : "Stiffness Estimates", "Collapsed By Default" : false }
    {
        annotation { "Name" : "Prismatic stiffness (lb/in)",
                     "UIHint" : UIHint.READ_ONLY }
        isReal(definition.prismaticStiffness_lbin, STIFFNESS_DISPLAY_BOUNDS);

        annotation { "Name" : "Prismatic deflection (mm @ 30kg)",
                     "UIHint" : UIHint.READ_ONLY }
        isReal(definition.prismaticDeflection_mm, STIFFNESS_DISPLAY_BOUNDS);

        annotation { "Name" : "Estimated stiffness (lb/in)",
                     "UIHint" : UIHint.READ_ONLY }
        isReal(definition.estimatedStiffness_lbin, STIFFNESS_DISPLAY_BOUNDS);

        annotation { "Name" : "Estimated deflection (mm @ 30kg)",
                     "UIHint" : UIHint.READ_ONLY }
        isReal(definition.estimatedDeflection_mm, STIFFNESS_DISPLAY_BOUNDS);
    }

    // =====================================================================
    // CACHE CONTROL
    // =====================================================================

    annotation { "Name" : "Use caching",
                 "Default" : true,
                 "Description" : "When enabled, analysis results are cached and reused if geometry hasn't changed" }
    definition.useCaching is boolean;

    if (definition.useCaching)
    {
        annotation { "Group Name" : "Caching", "Driving Parameter" : "useCaching", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Update cache",
                         "Description" : "Force a full recomputation of all analysis data" }
            isButton(definition.updateCache);

            annotation { "Name" : "Cached analysis data", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isAnything(definition.cachedAnalysis);

            annotation { "Name" : "Cached body signatures", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isAnything(definition.cachedBodySignatures);

            annotation { "Name" : "Cached edge signature", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isAnything(definition.cachedEdgeSignature);
        }
    }

    // =====================================================================
    // OUTPUT OPTIONS
    // =====================================================================

    annotation { "Name" : "Create composites",
                 "Default" : true,
                 "Description" : "Create composite wire bodies for each cross section" }
    definition.createComposites is boolean;

    // =====================================================================
    // DEBUG
    // =====================================================================

    annotation { "Name" : "Debug", "Default" : false }
    definition.debug is boolean;

    if (definition.debug)
    {
        annotation { "Group Name" : "Debug options",
                     "Driving Parameter" : "debug",
                     "Collapsed By Default" : false }
        {
            annotation { "Name" : "Debug type", "Default" : XSectionDebugType.EDGES }
            definition.debugType is XSectionDebugType;

            annotation { "Name" : "All cross-sections",
                         "Default" : true }
            definition.debugAllXSections is boolean;

            if (!definition.debugAllXSections)
            {
                annotation { "Name" : "Debug cross-sections",
                             "Item name" : "Cross-section",
                             "Item label template" : "Section #xSectionNum" }
                definition.debugXSections is array;

                for (var xSection in definition.debugXSections)
                {
                    annotation { "Name" : "Section number" }
                    isInteger(xSection.xSectionNum, POSITIVE_COUNT_BOUNDS);
                }
            }

            annotation { "Name" : "All bodies",
                         "Default" : true }
            definition.debugAllBodies is boolean;

            if (!definition.debugAllBodies)
            {
                annotation { "Name" : "Debug bodies",
                             "Filter" : EntityType.BODY }
                definition.debugBodies is Query;
            }

            annotation { "Name" : "Print body data?" }
            definition.printBodyData is boolean;

            annotation { "Name" : "Print triangles?" }
            definition.printTriangles is boolean;
        }
    }
}
