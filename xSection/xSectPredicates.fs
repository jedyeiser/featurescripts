FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import xSectLanguage (export/import)
export import(path : "a0fab52ee4d0b16ffbc1c603", version : "606006d4a22f6a2e649824db");



/**
 * PREDICATES - UI Definitions for EI Cross Section Feature
 * =========================================================
 * 
 * Defines all precondition annotations and UI structure.
 *
 * Body Selection Flow:
 * --------------------
 * 1. User picks bodies via selBodies (solid bodies only, no composites)
 * 2. Editing logic evaluates selBodies, populates bodyArray automatically
 * 3. For each body, editing logic reads the Onshape material name
 *    (stored in materialName -- this is the name from Onshape's material
 *    property, e.g. "Poplar", "HongTex: E-LT-660")
 * 4. Editing logic searches the CSV for a row whose Name column matches
 *    the Onshape material name
 * 5. CSV match found: hasMaterialData = true, full material data (density,
 *    Q matrix, Young's modulus) is populated from the CSV row
 * 6. No match (or no Onshape material assigned): hasMaterialData = false,
 *    override UI appears:
 *      - IGNORE: body participates in geometry but not stiffness
 *      - PROVIDE_DATA -> ISOTROPIC: user gives name, density, E
 *      - PROVIDE_DATA -> ORTHOTROPIC: user gives name, density, E1, E2, G12, nu12
 *
 * Material Data Contract:
 * -----------------------
 * When a CSV match is found, the feature body builds the materialData map
 * directly from CSV values. When the user provides overrides, the feature
 * body builds materialData (with Q matrix, density, Young's modulus) from
 * the raw scalar values stored in the predicate. The predicate stores
 * dimensionless reals labeled in MPa / kg/m3; the feature body attaches
 * units and computes derived quantities.
 */

// =============================================================================
// BOUNDS SPECIFICATIONS
// =============================================================================

export const numXsectionBounds = { (unitless) : [3, 50, 200] } as IntegerBoundSpec;

export const densityBounds = { (unitless) : [1, 1000, 25000] } as RealBoundSpec;

export const modulusMPaBounds = { (unitless) : [0.01, 10000, 1000000] } as RealBoundSpec;

export const poissonBounds = { (unitless) : [0.001, 0.3, 0.5] } as RealBoundSpec;

// =============================================================================
// ENUMS
// =============================================================================

export enum XSectEntityType
{
    FACE,
    WIRE
}

export enum XSectionDebugType
{
    annotation { "Name" : "Edges (Control Polygons)" }
    EDGES,
    annotation { "Name" : "Points" }
    POINTS,
    annotation { "Name" : "Mesh" }
    MESH
}

export enum MaterialBehavior
{
    annotation { "Name" : "Ignore (no stiffness contribution)" }
    IGNORE,
    annotation { "Name" : "Provide material data" }
    PROVIDE_DATA
}

export enum MaterialType
{
    annotation { "Name" : "Isotropic (single E)" }
    ISOTROPIC,
    annotation { "Name" : "Orthotropic (E1, E2, G12, nu12)" }
    ORTHOTROPIC
}

export enum BodyTableType
{
    annotation { "Name" : "Basic (EI, area, centroid above NA)" }
    BASIC,
    annotation { "Name" : "EI only (EI contribution + %)" }
    EI_ONLY,
    annotation { "Name" : "Geometry only (area, centroid, I)" }
    GEO_ONLY,
    annotation { "Name" : "Full (area, centroid, I, EI, %)" }
    FULL
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
// MAIN PRECONDITION PREDICATE
// =============================================================================

export predicate eiXSectPrecondition(definition is map)
{
    // -------------------------------------------------------------------------
    // Primary Inputs
    // -------------------------------------------------------------------------
    
    annotation { "Name" : "Edge/wire to cross section along", 
                 "Filter" : EntityType.EDGE, 
                 "MaxNumberOfPicks" : 1, 
                 "Description" : "Edge defining cross-section locations" }
    definition.xSectAlong is Query;
    
    annotation { "Name" : "Analysis name",
                 "Default" : "",
                 "Description" : "Optional name prefix for output curves. Curves named {name}_EI, {name}_neutralAxis." }
    definition.analysisName is string;
    
    annotation { "Name" : "FCP (Front Contact Point)", 
                 "Filter" : (EntityType.VERTEX) || (EntityType.FACE && GeometryType.PLANE) || (BodyType.MATE_CONNECTOR),
                 "MaxNumberOfPicks" : 1,
                 "Description" : "Front support location. Vertex, planar face normal to ski axis, or mate connector." }
    definition.fcpQuery is Query;
    
    annotation { "Name" : "ACP (Aft Contact Point)", 
                 "Filter" : (EntityType.VERTEX) || (EntityType.FACE && GeometryType.PLANE) || (BodyType.MATE_CONNECTOR),
                 "MaxNumberOfPicks" : 1,
                 "Description" : "Rear support location. Vertex, planar face normal to ski axis, or mate connector." }
    definition.acpQuery is Query;
    
    annotation { "Name" : "Cross section bodies", 
                 "Filter" : EntityType.BODY && BodyType.SOLID,
                 "Description" : "Solid bodies to include in analysis" }
    definition.selBodies is Query;
    
    annotation { "Name" : "Material library",
                 "Description" : "CSV with columns: Category, Name, Density, Poisson's Ratio, Young's Modulus, Q11-Q66, CTE_x, CTE_y" }
    definition.materialCSV is TableData;

    annotation { "Name" : "Refresh CSV data", "Description" : "Force re-read of the material library after updating the table element." }
    isButton(definition.refreshCSV);

    annotation { "Name" : "CSV refresh token", "UIHint" : UIHint.ALWAYS_HIDDEN }
    isInteger(definition.csvRefreshToken, { (unitless) : [0, 0, 1000] } as IntegerBoundSpec);

    annotation { "Name" : "Number of cross sections", 
                 "Description" : "Number of evenly spaced locations along selected edge. Includes endpoints." }
    isInteger(definition.numSections, numXsectionBounds);

    // -------------------------------------------------------------------------
    // Body Array (auto-populated by editing logic)
    // -------------------------------------------------------------------------
    
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

            annotation { "Name" : "Body Number", 
                         "UIHint" : UIHint.ALWAYS_HIDDEN }
            isInteger(body.bodyNum, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Has CSV material data", 
                         "Default" : false,
                         "UIHint" : UIHint.ALWAYS_HIDDEN }
            body.hasMaterialData is boolean;

            annotation { "Name" : "Material name",
                         "Description" : "Onshape material name from the body's material property.",
                         "UIHint" : [UIHint.READ_ONLY, UIHint.ALWAYS_HIDDEN] }
            body.materialName is string;

            if (body.hasMaterialData == false)
            {
                annotation { "Name" : "Material behavior",
                             "Default" : MaterialBehavior.IGNORE,
                             "Description" : "How to handle this body's material in the CLT analysis" }
                body.materialBehavior is MaterialBehavior;
                
                if (body.materialBehavior == MaterialBehavior.PROVIDE_DATA)
                {
                    annotation { "Name" : "Material type",
                                 "Default" : MaterialType.ISOTROPIC }
                    body.materialType is MaterialType;
                    
                    annotation { "Name" : "Material name override",
                                 "Description" : "Display name for this material" }
                    body.overrideName is string;
                    
                    annotation { "Name" : "Density (kg/m3)",
                                 "Description" : "Material density" }
                    isReal(body.overrideDensity, densityBounds);
                    
                    if (body.materialType == MaterialType.ISOTROPIC)
                    {
                        annotation { "Name" : "Young's Modulus E (MPa)",
                                     "Description" : "Elastic modulus. Q matrix computed with nu = 0.33" }
                        isReal(body.youngsModulus, modulusMPaBounds);
                    }
                    else
                    {
                        annotation { "Name" : "E1 - Longitudinal modulus (MPa)",
                                     "Description" : "Modulus along the ski (1-direction)" }
                        isReal(body.E1, modulusMPaBounds);
                        
                        annotation { "Name" : "E2 - Transverse modulus (MPa)",
                                     "Description" : "Modulus across the ski (2-direction)" }
                        isReal(body.E2, modulusMPaBounds);
                        
                        annotation { "Name" : "G12 - Shear modulus (MPa)",
                                     "Description" : "In-plane shear modulus" }
                        isReal(body.G12, modulusMPaBounds);
                        
                        annotation { "Name" : "nu12 - Poisson's ratio",
                                     "Description" : "Major Poisson's ratio" }
                        isReal(body.nu12, poissonBounds);
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Output Options
    // -------------------------------------------------------------------------
    
    annotation { "Group Name" : "Output Options", "Collapsed By Default" : true }
    {
        annotation { "Name" : "Create composites", "Default" : true,  "Description" : "Creates a composite part of unique wires for each cross section" }
        definition.createComposites is boolean;
        
        annotation { "Name" : "Language", "Default" : LANGUAGE.ENG, "Description" : "Custom table headers will be in the language selected" }
        definition.tableLanguage is LANGUAGE;

        annotation { "Name" : "Material table",
                     "Default" : false,
                     "Description" : "Include a table of all unique materials with full Q-matrix data." }
        definition.addMaterialTable is boolean;

        annotation { "Name" : "Body detail table",
                     "Default" : false,
                     "Description" : "Include a per-section breakdown of each body's contributions." }
        definition.addBodyTable is boolean;

        if (definition.addBodyTable)
        {
            annotation { "Name" : "Detail level",
                         "Default" : BodyTableType.FULL,
                         "Description" : "Choose which columns to include in the body detail table." }
            definition.bodyTableType is BodyTableType;
        }

    }

    // -------------------------------------------------------------------------
    // Debug Options
    // -------------------------------------------------------------------------
    
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
                         "Default" : true, 
                         "Description" : "When true, displays debug for all cross sections" }
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
                         "Default" : true, 
                         "Description" : "When true, displays debug for all selected bodies" }
            definition.debugAllBodies is boolean;

            if (!definition.debugAllBodies)
            {
                annotation { "Name" : "Debug bodies", 
                             "Filter" : EntityType.BODY,
                             "Description" : "Select specific bodies to debug" }
                definition.debugBodies is Query;
            }
            
            annotation { "Name" : "Print bodies?" }
            definition.printBodyData is boolean;
            
            annotation { "Name" : "Print triangles?" }
            definition.printTriangles is boolean;
        }
    }
}


export predicate AlterMeshingPredicate(definition is map)
{
    annotation { "Name" : "Alter meshing", "Default" : false }
    definition.alterMeshing is boolean;

    if (definition.alterMeshing)
    {
        annotation { "Group Name" : "Meshing parameters", 
                     "Collapsed By Default" : false, 
                     "Driving Parameter" : "alterMeshing" }
        {
            annotation { "Name" : "Maximum segment length", 
                         "Description" : "Maximum arc length between sample points" }
            isLength(definition.perimMaxSegLength, LENGTH_BOUNDS);

            annotation { "Name" : "Insert internal mesh points", "Default" : false }
            definition.insertInternalMeshPoints is boolean;

            if (definition.insertInternalMeshPoints)
            {
                annotation { "Name" : "Element area cutoff (mm^2)", 
                             "Description" : "Minimum triangle area threshold for mesh refinement" }
                isReal(definition.meshElementArea, POSITIVE_REAL_BOUNDS);
            }
        }
    }
}
