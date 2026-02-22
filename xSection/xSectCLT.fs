FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// xSectMaterials (buildMaterialLookup, normalizeMaterialName)
import(path : "f8e590162884d45f56e0a05f", version : "60e38e7ce6bddaa3e75c6cc3");

// xSect_GJ (computeTorsionalStiffness)
import(path : "9df6ba3db06d479fabe63c1d", version : "01ff18cd88c62b73db496011");


// =============================================================================
// TOLERANCE CONSTANTS
// =============================================================================

/**
 * Area threshold for detecting degenerate polygons.
 * Below this value, treat body cross-section as having zero area.
 */
const ZERO_AREA_TOLERANCE = 1e-15 * meter * meter;

/**
 * Minimum extensional stiffness for valid beam analysis.
 * Guards against division by zero when computing neutral axis and EI_eff.
 * Value chosen to detect numerical issues while allowing very flexible materials.
 */
const MIN_EXTENSIONAL_STIFFNESS = 1e-10 * newton;

/**
 * Warning threshold for suspiciously low extensional stiffness.
 * Alerts user to possible material assignment issues or degenerate geometry.
 * Set higher than MIN_EXTENSIONAL_STIFFNESS to flag edge cases.
 */
const LOW_STIFFNESS_WARNING = 1e-6 * newton;


/**
 * CROSS-SECTION CLT MODULE
 * ========================
 *
 * Computes beam-level mechanical properties at each cross-section using
 * Classical Laminate Theory (CLT) principles applied to discrete body geometry.
 *
 * Theory
 * ------
 * Traditional CLT works with thin plies stacked in Z, giving per-unit-width
 * A, B, D matrices. Here we adapt this to a beam cross-section where each
 * "ply" is an arbitrarily-shaped solid body (core, skin, edge, etc.) with
 * its own area, centroid, and second moment.
 *
 * For each body k with a known reduced stiffness matrix Q_k (3x3):
 *
 *   A_beam[i][j] = Σ_k  Q_k[i][j] * area_k
 *   B_beam[i][j] = Σ_k  Q_k[i][j] * S_k
 *   D_beam[i][j] = Σ_k  Q_k[i][j] * Ixx_ref_k
 *
 * where:
 *   S_k       = area_k * ȳ_k                         (first moment about ref)
 *   Ixx_ref_k = Ixx_centroid_k + area_k * ȳ_k²       (parallel axis theorem)
 *   ȳ_k       = centroid Y in frame coordinates        (distance from ref axis)
 *
 * Reference axis: Y = 0 in the cross-section frame (the plane origin).
 * For a ski, this is typically on the selected edge — i.e., the bottom of the beam.
 *
 * Derived quantities:
 *   Neutral axis:  z_NA   = -B[0][0] / A[0][0]
 *   Effective EI:  EI_eff = D[0][0] - B[0][0]² / A[0][0]
 *
 * Units:
 *   Q matrix entries .... Pa   (N/m²)
 *   A_beam .............. N    (Pa · m²)
 *   B_beam .............. N·m  (Pa · m³)
 *   D_beam .............. N·m² (Pa · m⁴)
 *   EI_eff .............. N·m²
 *   z_NA ................ m
 *   linearDensity ....... kg/m (density · area)
 *
 * Q Matrix Convention:
 *   Stored as 3x3 array-of-arrays:
 *     [[Q11, Q12, Q16],
 *      [Q12, Q22, Q26],
 *      [Q16, Q26, Q66]]
 *
 *   1-direction = longitudinal (along the ski / frame Z-axis)
 *   2-direction = transverse (across the ski / frame X-axis)
 *   6-direction = in-plane shear
 *
 *   For materials with off-axis plies (biax fabrics, etc.), Q16 and Q26 are
 *   non-zero. The CSV stores the already-transformed Q̄ for each material's
 *   as-laid architecture — no further rotation is applied here.
 *
 * Known Limitation:
 *   totalSectionProperties.Ixx for bodies with multiple disconnected perimeters
 *   or holes uses simple summation rather than parallel axis theorem between
 *   groups. This is correct for single-perimeter bodies (the common case in
 *   ski construction) but may be slightly off for complex multi-group bodies.
 */


// =============================================================================
// Q MATRIX CONSTRUCTION
// =============================================================================

/**
 * Build a Q matrix for an isotropic material.
 *
 * For an isotropic material, there is one independent elastic modulus (E)
 * and one Poisson's ratio (ν). The shear modulus is G = E / (2(1+ν)).
 *
 * The plane-stress reduced stiffness matrix is:
 *   Q11 = Q22 = E / (1 - ν²)
 *   Q12 = ν · E / (1 - ν²)
 *   Q66 = G = E / (2(1 + ν))
 *   Q16 = Q26 = 0
 *
 * @param E {ValueWithUnits} : Young's modulus (Pa)
 * @param nu {number} : Poisson's ratio (dimensionless)
 * @returns {array} : 3x3 Q matrix with entries in Pa
 */
export function isotropicQMatrix(E is ValueWithUnits, nu is number) returns array
{
    var denom = 1 - nu * nu;
    var Q11 = E / denom;
    var Q12 = nu * E / denom;
    var Q66 = E / (2 * (1 + nu));
    var zero = 0 * pascal;

    return [
        [Q11,  Q12,  zero],
        [Q12,  Q11,  zero],
        [zero, zero, Q66]
    ];
}

/**
 * Build a Q matrix for an orthotropic material.
 *
 * An orthotropic ply has four independent constants: E1, E2, G12, ν12.
 * The minor Poisson's ratio ν21 is derived from reciprocity: ν21 = ν12 · E2/E1.
 *
 * The plane-stress reduced stiffness matrix is:
 *   Q11 = E1 / (1 - ν12·ν21)
 *   Q22 = E2 / (1 - ν12·ν21)
 *   Q12 = ν12 · E2 / (1 - ν12·ν21)
 *   Q66 = G12
 *   Q16 = Q26 = 0
 *
 * Note: Q16 = Q26 = 0 because this is the ON-AXIS stiffness matrix. If the
 * material has off-axis plies, the transformed Q̄ (with non-zero Q16, Q26)
 * should be provided directly from the material library, not computed here.
 *
 * @param E1 {ValueWithUnits} : Longitudinal modulus (Pa)
 * @param E2 {ValueWithUnits} : Transverse modulus (Pa)
 * @param G12 {ValueWithUnits} : In-plane shear modulus (Pa)
 * @param nu12 {number} : Major Poisson's ratio (dimensionless)
 * @returns {array} : 3x3 Q matrix with entries in Pa
 */
export function orthotropicQMatrix(E1 is ValueWithUnits, E2 is ValueWithUnits,
                                    G12 is ValueWithUnits, nu12 is number) returns array
{
    var nu21 = nu12 * E2 / E1;
    var denom = 1 - nu12 * nu21;
    var Q11 = E1 / denom;
    var Q22 = E2 / denom;
    var Q12 = nu12 * E2 / denom;
    var zero = 0 * pascal;

    return [
        [Q11,  Q12,  zero],
        [Q12,  Q22,  zero],
        [zero, zero, G12]
    ];
}


// =============================================================================
// 3×3 MATRIX UTILITIES
// =============================================================================
// These operate on array-of-arrays: [[row0], [row1], [row2]]
// Each entry is a ValueWithUnits.

/**
 * Create a 3×3 matrix filled with a zero value.
 * The zero value establishes the dimensional units for the matrix.
 *
 * Example: zeroMatrix3x3(0 * newton) → 3×3 of zeros in Newtons.
 *
 * @param zeroVal {ValueWithUnits} : Zero with desired units
 * @returns {array} : 3×3 matrix
 */
export function zeroMatrix3x3(zeroVal) returns array
{
    return [
        [zeroVal, zeroVal, zeroVal],
        [zeroVal, zeroVal, zeroVal],
        [zeroVal, zeroVal, zeroVal]
    ];
}

/**
 * Element-wise addition of two 3×3 matrices.
 * Both matrices must have compatible units.
 */
export function addMatrix3x3(a is array, b is array) returns array
{
    return [
        [a[0][0] + b[0][0], a[0][1] + b[0][1], a[0][2] + b[0][2]],
        [a[1][0] + b[1][0], a[1][1] + b[1][1], a[1][2] + b[1][2]],
        [a[2][0] + b[2][0], a[2][1] + b[2][1], a[2][2] + b[2][2]]
    ];
}

/**
 * Multiply every entry of a 3×3 matrix by a scalar.
 * The scalar may carry units — the result inherits the product units.
 *
 * Used in ABD assembly: scaleMatrix3x3(Q_in_Pa, area_in_m2) → matrix in N.
 */
export function scaleMatrix3x3(m is array, scalar) returns array
{
    return [
        [m[0][0] * scalar, m[0][1] * scalar, m[0][2] * scalar],
        [m[1][0] * scalar, m[1][1] * scalar, m[1][2] * scalar],
        [m[2][0] * scalar, m[2][1] * scalar, m[2][2] * scalar]
    ];
}


// =============================================================================
// MAIN ENTRY POINT
// =============================================================================

/**
 * Compute CLT mechanical properties for all cross-sections.
 *
 * Takes the full crossSectionData map (as returned by processCrossSections)
 * and returns an augmented copy with a mechanicalProperties map added to
 * each cross-section.
 *
 * Expected input structure:
 * {
 *     bodies: [{
 *         bodyIdx, bodyName,
 *         hasMaterialData: boolean,
 *         materialData?: {
 *             density: ValueWithUnits (kg/m³),
 *             youngsModulus: ValueWithUnits (Pa),
 *             qMatrix: [[Q11,Q12,Q16],[Q12,Q22,Q26],[Q16,Q26,Q66]] (Pa)
 *         }
 *     }, ...],
 *     crossSections: [{
 *         frame: CoordSystem,
 *         sectionPoints: [{point2D, point3D}, ...],
 *         bodyData: [{
 *             bodyIdx: number,
 *             totalSectionProperties: { area, centroid2D, centroid3D, Ixx, Iyy, Ixy }
 *         }, ...]
 *     }, ...]
 * }
 *
 * Output adds to each crossSection:
 *     mechanicalProperties: {
 *         A: [[3×3]] in N,
 *         B: [[3×3]] in N·m,
 *         D: [[3×3]] in N·m²,
 *         neutralAxisY: ValueWithUnits (m),
 *         EI_eff: ValueWithUnits (N·m²),
 *         sectionWidth: ValueWithUnits (m),
 *         sectionHeight: ValueWithUnits (m),
 *         bodyContributions: [{
 *             bodyIdx: number,
 *             linearDensity: ValueWithUnits (kg/m),
 *             hasMaterial: boolean
 *         }, ...]
 *     }
 *
 * @param data {map} : crossSectionData from processCrossSections
 * @returns {map} : Augmented copy with mechanicalProperties on each section
 */
export function computeCLTProperties(data is map) returns map
{
    var bodies = data.bodies;
    var updatedSections = [];

    for (var section in data.crossSections)
    {
        var mechProps = assembleSectionMechanics(section, bodies);

        // Add geometric bounds (reuse pre-computed bounding box from section data)
        mechProps.sectionWidth = section.boundingBox.width;
        mechProps.sectionHeight = section.boundingBox.height;

        var updatedSection = section;
        updatedSection.mechanicalProperties = mechProps;

        updatedSections = append(updatedSections, updatedSection);
    }

    var result = data;
    result.crossSections = updatedSections;
    return result;
}


// =============================================================================
// PER-SECTION ABD ASSEMBLY
// =============================================================================

/**
 * Assemble beam-level A, B, D matrices and extract derived properties
 * for a single cross-section.
 *
 * Walk through every body present at this section. For bodies with material
 * data, accumulate their contribution to the 3×3 A, B, D matrices using the
 * full Q matrix (all 9 entries, respecting coupling terms Q16/Q26).
 *
 * Bodies flagged IGNORE (hasMaterialData == false) are skipped in the
 * stiffness assembly — they still appear in the geometry but contribute
 * zero to structural properties.
 *
 * @param section {map} : Single crossSection entry (frame, bodyData, etc.)
 * @param bodies {array} : Top-level bodies array with material data
 * @returns {map} : { A, B, D, neutralAxisY, EI_eff, bodyContributions }
 */
function assembleSectionMechanics(section is map, bodies is array) returns map
{
    // Initialize 3×3 ABD with correct units:
    //   A: Q(Pa) × area(m²)   = N
    //   B: Q(Pa) × S(m³)      = N·m
    //   D: Q(Pa) × Ixx(m⁴)    = N·m²
    var A = zeroMatrix3x3(0 * newton);
    var B = zeroMatrix3x3(0 * newton * meter);
    var D = zeroMatrix3x3(0 * newton * meter * meter);

    var bodyContributions = [];

    for (var bodyInfo in section.bodyData)
    {
        var bodyIdx = bodyInfo.bodyIdx;
        var body = bodies[bodyIdx];

        // -----------------------------------------------------------------
        // Skip bodies without material data (user chose IGNORE, or no
        // material assigned and no override provided).
        // -----------------------------------------------------------------
        if (body.hasMaterialData != true)
        {
            bodyContributions = append(bodyContributions, {
                "bodyIdx" : bodyIdx,
                "linearDensity" : 0 * kilogram / meter,
                "hasMaterial" : false
            });
            continue;
        }

        var Q = body.materialData.qMatrix;
        var density = body.materialData.density;
        var props = bodyInfo.totalSectionProperties;

        var area_k = props.area;
        var yBar_k = props.centroid2D[0];       // height above base (frame X = world +Z = thickness)
        var Ixx_centroid_k = props.Iyy;         // second moment about width axis (bending stiffness)

        // Skip bodies with effectively zero area (degenerate geometry)
        if (abs(area_k) < ZERO_AREA_TOLERANCE)
        {
            bodyContributions = append(bodyContributions, {
                "bodyIdx" : bodyIdx,
                "linearDensity" : 0 * kilogram / meter,
                "hasMaterial" : true
            });
            continue;
        }

        // -----------------------------------------------------------------
        // Parallel axis theorem: shift Ixx from body centroid to reference (Y=0)
        //
        //   Ixx_ref = Ixx_centroid + A · ȳ²
        //
        // This is the key step that lets us combine bodies at different
        // heights into a coherent beam-level D matrix.
        // -----------------------------------------------------------------
        var Ixx_ref_k = Ixx_centroid_k + area_k * yBar_k * yBar_k;

        // First moment of area about reference axis
        var S_k = area_k * yBar_k;

        // -----------------------------------------------------------------
        // Accumulate into beam-level ABD using full 3×3 Q matrix.
        //
        // Every Q[i][j] entry participates — this preserves extension-bending
        // coupling (via B), bend-twist coupling (via Q16/Q26 terms in D),
        // and all cross-coupling effects.
        // -----------------------------------------------------------------
        A = addMatrix3x3(A, scaleMatrix3x3(Q, area_k));
        B = addMatrix3x3(B, scaleMatrix3x3(Q, S_k));
        D = addMatrix3x3(D, scaleMatrix3x3(Q, Ixx_ref_k));

        // Per-body linear mass density: mass per unit length along the ski
        var linearDensity = density * area_k;

        bodyContributions = append(bodyContributions, {
            "bodyIdx" : bodyIdx,
            "linearDensity" : linearDensity,
            "hasMaterial" : true
        });
    }

    // =====================================================================
    // Extract scalar results from assembled matrices
    // =====================================================================

    var neutralAxisY = 0 * meter;
    var EI_eff = 0 * newton * meter * meter;

    // Guard against zero extensional stiffness (e.g. all bodies are IGNORE)
    if (abs(A[0][0]) > MIN_EXTENSIONAL_STIFFNESS)
    {
        // Neutral axis: the Y location where axial strain is zero under
        // pure bending. Derived from the condition B_eff = 0 when the
        // reference is shifted to the neutral axis.
        neutralAxisY = -B[0][0] / A[0][0];

        // Effective bending stiffness: accounts for extension-bending
        // coupling that "steals" some apparent stiffness when the layup
        // is asymmetric about the neutral axis.
        //
        //   EI_eff = D11 - B11² / A11
        //
        // For a symmetric layup (B = 0), this reduces to D11.
        EI_eff = D[0][0] - (B[0][0] * B[0][0]) / A[0][0];

        // Warn if stiffness is suspiciously low
        if (abs(A[0][0]) < LOW_STIFFNESS_WARNING)
        {
            println("WARNING: Section has very low extensional stiffness (A11 = " ~ A[0][0] ~ ")");
        }
    }
    else
    {
        println("WARNING: Zero extensional stiffness detected - all bodies may be set to IGNORE");
    }

    // =====================================================================
    // Torsional stiffness (GJ) computation moved to separate gjAnalysis feature
    // =====================================================================
    // GJ computation is expensive and not always needed. Users can run the
    // gjAnalysis feature separately to compute GJ on-demand.
    var GJ_eff = 0 * newton * meter * meter;

    return {
        "A" : A,
        "B" : B,
        "D" : D,
        "neutralAxisY" : neutralAxisY,
        "EI_eff" : EI_eff,
        "GJ_eff" : GJ_eff,
        "bodyContributions" : bodyContributions
    };
}


// NOTE: buildMaterialLookup() and normalizeMaterialName() have been moved to xSectMaterials.fs
// and are imported above. This keeps CLT module focused on mechanical calculations.
