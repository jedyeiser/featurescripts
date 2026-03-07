FeatureScript 2892;

/**
 * Torsional Stiffness (GJ) Calculation for Cross-Sections
 *
 * Implements the thin-plate Saint-Venant formula: GJ = 4 · Σ_e G_e · Iz_e
 * Analytically exact for b/t >> 1; ~10% error for b/t ≈ 7 (underfoot).
 * O(n) — no FEM solve required.
 *
 * Shear modulus per element uses G_torsion back-calculated from the Q matrix:
 * - Balanced laminates (±45°, woven, isotropic): G_torsion = (Q11 - Q12) / 2
 * - Unbalanced (0°-dominant UD): G_torsion = Q66
 *
 * @see GJ_torsional_stiffness_reference.md for mathematical derivation
 */

import(path : "onshape/std/common.fs", version : "2878.0");
// IMPORT: tools/solvers.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");

/**
 * UNIT CONVENTION
 * ===============
 * This module uses plain number calculations with implicit units:
 *
 * Implicit Units:
 * - Coordinates (y, z): meters [m]
 * - Shear modulus (G): pascals [N/m²]
 * - Areas (A): square meters [m²]
 * - Shape gradients (dNdy, dNdz): inverse meters [1/m]
 * - Polar moments (Jp): meters^4 [m⁴]
 * - Stiffness (K): pascals [N/m²]
 * - Loads (f): newtons [N]
 * - Torsional stiffness (GJ): newton-meters² [N·m²]
 *
 * Units are:
 * - STRIPPED at entry: coordinates from sectionPoints, G from qMatrix
 * - IMPLICIT during calculation (documented in comments)
 * - RESTORED at exit: final GJ returned as ValueWithUnits
 */

const MIN_AREA = 1e-12;  // 1 μm² - only filter truly degenerate triangles (implicit m²)

/**
 * Main entry point: Compute torsional stiffness GJ for a cross-section
 *
 * @param section : Cross-section data with bodyData, sectionPoints, frame
 * @param bodies : Array of body material definitions with Q matrices
 * @returns : GJ_eff in N·m² (ValueWithUnits)
 *
 * Process:
 * 1. Extract triangulated mesh from section.bodyData
 * 2. Build G_torsion array (one G per triangle, back-calculated from Q matrix)
 * 3. Compute G-weighted centroid as shear center approximation
 * 4. Integrate GJ = 4·Σ G_e·Iz_e (thin-plate formula, O(n))
 */
export function computeTorsionalStiffness(section is map, bodies is array) returns ValueWithUnits
{
    // Extract global mesh from hierarchical bodyData structure
    var meshData = buildGlobalMesh(section);
    var triangles = meshData.triangles;
    var bodyIndices = meshData.bodyIndices;
    var numNodes = meshData.numNodes;

    // Handle edge cases
    if (size(triangles) == 0)
    {
        return 0 * newton * meter * meter;
    }

    // Extract shear modulus for each triangle from material Q matrices
    var G_elem = extractShearModuli(triangles, bodyIndices, bodies);

    // Count valid G values; used for early return if no structural material exists
    var G_count = 0;
    for (var i = 0; i < size(G_elem); i += 1)
    {
        if (G_elem[i] >= 1e-6)
            G_count += 1;
    }

    // Check if any structural material exists
    if (G_count == 0)
    {
        return 0 * newton * meter * meter;
    }

    // Compute G-weighted centroid as shear center approximation.
    // y, z for FEM must be measured from shear center; using ski base inflates Jp and
    // corrupts ψ, causing the inverted GJ curve.
    var G_A_total = 0.0;
    var Gy_A_total = 0.0;
    var Gz_A_total = 0.0;
    for (var e = 0; e < size(triangles); e += 1)
    {
        var G = G_elem[e];
        if (G < 1e-6) { continue; }
        var tri = triangles[e];
        var pt1 = section.sectionPoints[tri[0]].point2D;
        var pt2 = section.sectionPoints[tri[1]].point2D;
        var pt3 = section.sectionPoints[tri[2]].point2D;
        var y1 = pt1[0] / meter; var z1 = pt1[1] / meter;
        var y2 = pt2[0] / meter; var z2 = pt2[1] / meter;
        var y3 = pt3[0] / meter; var z3 = pt3[1] / meter;
        var shapeData = computeShapeGradients(y1, z1, y2, z2, y3, z3);
        if (shapeData.area < MIN_AREA) { continue; }
        G_A_total += G * shapeData.area;
        Gy_A_total += G * shapeData.area * (y1 + y2 + y3) / 3.0;
        Gz_A_total += G * shapeData.area * (z1 + z2 + z3) / 3.0;
    }
    var y_bar = (G_A_total > 0) ? Gy_A_total / G_A_total : 0.0;
    var z_bar = (G_A_total > 0) ? Gz_A_total / G_A_total : 0.0;

    // Compute GJ using thin-plate formula (O(n), no FEM solve needed)
    var GJ_val = computeGJThinPlate(triangles, G_elem, section.sectionPoints, y_bar, z_bar);

    if (GJ_val <= 0.0)
    {
        return 0 * newton * meter * meter;
    }

    return GJ_val * newton * meter * meter;
}

/**
 * Flatten hierarchical bodyData into single triangle list
 *
 * @param section : Cross-section with bodyData array
 * @returns : map with { triangles, bodyIndices, numNodes }
 *
 * Handles nested structure:
 * - bodyData[i].groups[j].triangles = [[i1,j1,k1], ...]
 * - Each triangle indexed into shared sectionPoints array
 * - Track bodyIdx for material lookup
 */
function buildGlobalMesh(section is map) returns map
{
    var triangles = [];
    var bodyIndices = [];
    var maxNodeIndex = -1;

    // Iterate through all bodies and their groups
    for (var bodyData in section.bodyData)
    {
        var bodyIdx = bodyData.bodyIdx;

        // Each body has groups (perimeters, holes)
        if (bodyData.groups != undefined)
        {
            for (var group in bodyData.groups)
            {
                if (group.triangles != undefined)
                {
                    for (var tri in group.triangles)
                    {
                        triangles = append(triangles, tri);
                        bodyIndices = append(bodyIndices, bodyIdx);

                        // Track max node index
                        maxNodeIndex = max(maxNodeIndex, tri[0]);
                        maxNodeIndex = max(maxNodeIndex, tri[1]);
                        maxNodeIndex = max(maxNodeIndex, tri[2]);
                    }
                }
            }
        }
    }

    var numNodes = maxNodeIndex + 1;

    return {
        "triangles" : triangles,
        "bodyIndices" : bodyIndices,
        "numNodes" : numNodes
    };
}

/**
 * Extract shear modulus G for each triangle from material data
 *
 * @param triangles : Array of [i,j,k] node indices
 * @param bodyIndices : Body index for each triangle
 * @param bodies : Body material definitions with Q matrices
 * @returns : Array of G values (plain numbers, implicit N/m²) - one per triangle
 *
 * Lookup chain: triangle → bodyIdx → Q matrix → G_torsion
 * Q is stored as 3×3: [[Q11, Q12, Q16], [Q12, Q22, Q26], [Q16, Q26, Q66]]
 * - Balanced (±45°, woven, isotropic): G_torsion = (Q11 - Q12) / 2  [back-calc gives true G12]
 * - Unbalanced (0°-dominant UD): G_torsion = Q66  [= G12 on-axis]
 * - Missing material: G = 0 (contributes no stiffness)
 */
function extractShearModuli(triangles is array, bodyIndices is array, bodies is array) returns array
{
    var G_elem = [];

    for (var i = 0; i < size(triangles); i += 1)
    {
        var bodyIdx = bodyIndices[i];
        var G_val = 0.0;  // Plain number, implicit N/m²

        // Find corresponding body material
        for (var body in bodies)
        {
            if (body.bodyIdx == bodyIdx)
            {
                if (body.hasMaterialData &&
                    body.materialData != undefined &&
                    body.materialData.qMatrix != undefined)
                {
                    // Use torsional shear modulus (not Q66 directly):
                    //   Balanced laminates (±45°, woven, isotropic): G_torsion = (Q11 - Q12) / 2
                    //   Unbalanced (0°-dominant UD): G_torsion = Q66
                    // Q = [[Q11, Q12, Q16], [Q12, Q22, Q26], [Q16, Q26, Q66]]
                    var Pa = newton / (meter * meter);
                    var Q11 = body.materialData.qMatrix[0][0] / Pa;
                    var Q12 = body.materialData.qMatrix[0][1] / Pa;
                    var Q22 = body.materialData.qMatrix[1][1] / Pa;
                    var Q66 = body.materialData.qMatrix[2][2] / Pa;
                    var ratio = (Q22 > 1e-6) ? (Q11 / Q22) : 1e9;
                    G_val = (ratio < 3.0) ? ((Q11 - Q12) / 2.0) : Q66;
                }
                break;
            }
        }

        G_elem = append(G_elem, G_val);
    }

    return G_elem;
}

/**
 * Compute GJ using the thin-plate Saint-Venant formula: GJ = 4 · Σ_e G_e · Iz_e
 *
 * Analytically exact for b/t >> 1; ~10% error for b/t ≈ 7 (underfoot).
 * O(n) — does not require FEM solve.
 *
 * @param triangles : Array of [i,j,k] node indices
 * @param G_elem : Torsional shear modulus per triangle (plain numbers, implicit N/m²)
 * @param sectionPoints : Array of {point2D: [y,z], ...}
 * @param y_bar : G-weighted centroid y coordinate (plain number, implicit m)
 * @param z_bar : G-weighted centroid z coordinate (plain number, implicit m)
 * @returns : GJ as plain number (implicit N·m²)
 */
function computeGJThinPlate(triangles is array, G_elem is array,
                             sectionPoints is array, y_bar is number, z_bar is number) returns number
{
    var Iz_sum = 0.0;
    for (var e = 0; e < size(triangles); e += 1)
    {
        var G = G_elem[e];
        if (G < 1e-6) { continue; }

        var tri = triangles[e];
        var pt1 = sectionPoints[tri[0]].point2D;
        var pt2 = sectionPoints[tri[1]].point2D;
        var pt3 = sectionPoints[tri[2]].point2D;

        var y1 = pt1[0] / meter - y_bar;
        var y2 = pt2[0] / meter - y_bar;
        var y3 = pt3[0] / meter - y_bar;
        var z1 = pt1[1] / meter - z_bar;
        var z2 = pt2[1] / meter - z_bar;
        var z3 = pt3[1] / meter - z_bar;

        var shapeData = computeShapeGradients(y1, z1, y2, z2, y3, z3);
        if (shapeData.area < MIN_AREA) { continue; }

        // Exact closed form for second moment of area of triangle about centroid:
        // Iz = ∫y² dA = (A/6) * (y1² + y2² + y3² + y1·y2 + y1·y3 + y2·y3)
        // See e.g. Pilkey, "Analysis and Design of Elastic Beams" (Wiley, 2002)
        var Iz = (shapeData.area / 6.0) *
                 (y1*y1 + y2*y2 + y3*y3 + y1*y2 + y1*y3 + y2*y3);
        Iz_sum += G * Iz;
    }
    return 4.0 * Iz_sum;  // GJ = 4·Σ G_e·Iz_e, implicit N·m²
}

/**
 * Compute constant shape function gradients for linear triangle
 *
 * @param y1, z1, y2, z2, y3, z3 : Nodal coordinates (plain numbers, implicit meters)
 * @returns : map with { dNdy: [3], dNdz: [3], area: number }
 *
 * Linear shape functions:
 *   N1 = (a1 + b1*y + c1*z) / (2*A)
 *   N2 = (a2 + b2*y + c2*z) / (2*A)
 *   N3 = (a3 + b3*y + c3*z) / (2*A)
 *
 * Gradients (constant over element):
 *   dN1/dy = b1/(2*A) = (z2 - z3)/(2*A)
 *   dN1/dz = c1/(2*A) = (y3 - y2)/(2*A)
 *   etc.
 *
 * Area (signed):
 *   2*A = (y2-y1)*(z3-z1) - (y3-y1)*(z2-z1)
 */
export function computeShapeGradients(y1 is number, z1 is number,
                                       y2 is number, z2 is number,
                                       y3 is number, z3 is number) returns map
{
    // Compute twice the signed area
    var twoA = (y2 - y1) * (z3 - z1) - (y3 - y1) * (z2 - z1);

    // Area (take absolute value)
    var A = abs(twoA) / 2.0;

    // Check for degenerate triangle before dividing by twoA
    if (A < MIN_AREA)
    {
        // Return zero gradients for degenerate triangles
        return {
            "dNdy" : [0.0, 0.0, 0.0],
            "dNdz" : [0.0, 0.0, 0.0],
            "area" : A
        };
    }

    // Shape function gradients (dimensionless / meter)
    var dNdy = makeArray(3);
    var dNdz = makeArray(3);

    dNdy[0] = (z2 - z3) / twoA;
    dNdy[1] = (z3 - z1) / twoA;
    dNdy[2] = (z1 - z2) / twoA;

    dNdz[0] = (y3 - y2) / twoA;
    dNdz[1] = (y1 - y3) / twoA;
    dNdz[2] = (y2 - y1) / twoA;

    return {
        "dNdy" : dNdy,
        "dNdz" : dNdz,
        "area" : A
    };
}
