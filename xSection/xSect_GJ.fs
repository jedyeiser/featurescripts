FeatureScript 2878;

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
        println("WARNING: No triangles in mesh - GJ = 0");
        return 0 * newton * meter * meter;
    }

    if (numNodes > 2000)
    {
        println("WARNING: Very large mesh (" ~ numNodes ~ " nodes) - check mesh quality");
    }

    // Extract shear modulus for each triangle from material Q matrices
    var G_elem = extractShearModuli(triangles, bodyIndices, bodies);

    // Compute G statistics for diagnostics
    var G_min = 1e99;
    var G_max = 0.0;
    var G_sum = 0.0;
    var G_count = 0;

    for (var i = 0; i < size(G_elem); i += 1)
    {
        var G = G_elem[i];
        if (G >= 1e-6)  // Only count valid G values
        {
            G_min = min(G_min, G);
            G_max = max(G_max, G);
            G_sum += G;
            G_count += 1;
        }
    }

    var G_mean = (G_count > 0) ? (G_sum / G_count) : 0.0;

    println("  Shear modulus G: " ~ G_count ~ "/" ~ size(G_elem) ~ " valid triangles");
    if (G_count > 0)
    {
        println("    G range: [" ~ (G_min / 1e9) ~ ", " ~ (G_max / 1e9) ~ "] GPa");
        println("    G mean: " ~ (G_mean / 1e9) ~ " GPa");
    }

    // Check if any structural material exists
    if (G_count == 0)
    {
        println("WARNING: No structural material found - GJ = 0");
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
    println("  G-weighted centroid: y=" ~ (y_bar * 1000) ~ " mm, z=" ~ (z_bar * 1000) ~ " mm");

    // Compute GJ using thin-plate formula (O(n), no FEM solve needed)
    var GJ_val = computeGJThinPlate(triangles, G_elem, section.sectionPoints, y_bar, z_bar);

    if (GJ_val <= 0.0)
    {
        println("WARNING: GJ = 0 (no valid elements?) - clamping to 0");
        return 0 * newton * meter * meter;
    }

    println("  GJ = " ~ GJ_val ~ " N·m²");
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

        // Iz = ∫y² dA = (A/6) * (y1² + y2² + y3² + y1·y2 + y1·y3 + y2·y3)
        var Iz = (shapeData.area / 6.0) *
                 (y1*y1 + y2*y2 + y3*y3 + y1*y2 + y1*y3 + y2*y3);
        Iz_sum += G * Iz;
    }
    return 4.0 * Iz_sum;  // GJ = 4·Σ G_e·Iz_e, implicit N·m²
}

/**
 * Assemble global FEM system K·ψ = f
 *
 * @param triangles : Array of [i,j,k] node indices
 * @param G_elem : Shear modulus for each triangle (plain numbers, implicit N/m²)
 * @param sectionPoints : Array of {point2D: [y,z], ...}
 * @param numNodes : Total number of nodes
 * @returns : map with { K: array (n×n), f: array (n×1), numNodes: int }
 *
 * Element stiffness (linear triangle):
 *   K_local[a,b] = G * A * (dNdy[a]*dNdy[b] + dNdz[a]*dNdz[b])
 *
 * Element load:
 *   f_local[a] = G * A * (z_c * dNdy[a] - y_c * dNdz[a])
 *
 * where (y_c, z_c) is triangle centroid
 */
function assembleFEMSystem(triangles is array, G_elem is array, sectionPoints is array, numNodes is number, y_bar is number, z_bar is number) returns map
{
    // Initialize global arrays
    var n = numNodes;
    var K = makeArray(n);
    var f = makeArray(n);

    for (var i = 0; i < n; i += 1)
    {
        K[i] = makeArray(n, 0.0);
        f[i] = 0.0;
    }

    // Loop over all triangles
    var validElements = 0;
    var skippedZeroG = 0;
    var skippedDegenerateArea = 0;

    for (var e = 0; e < size(triangles); e += 1)
    {
        var tri = triangles[e];
        var i1 = tri[0];
        var i2 = tri[1];
        var i3 = tri[2];

        var G = G_elem[e];  // Plain number, implicit N/m²

        // Skip elements with zero stiffness
        if (G < 1e-6)  // Implicit N/m²
        {
            skippedZeroG += 1;
            continue;
        }

        // Get nodal coordinates in local (y,z) frame
        var pt1 = sectionPoints[i1].point2D;
        var pt2 = sectionPoints[i2].point2D;
        var pt3 = sectionPoints[i3].point2D;

        var y1 = pt1[0] / meter - y_bar;
        var z1 = pt1[1] / meter - z_bar;
        var y2 = pt2[0] / meter - y_bar;
        var z2 = pt2[1] / meter - z_bar;
        var y3 = pt3[0] / meter - y_bar;
        var z3 = pt3[1] / meter - z_bar;

        // Compute shape function gradients and area
        var shapeData = computeShapeGradients(y1, z1, y2, z2, y3, z3);
        var dNdy = shapeData.dNdy;
        var dNdz = shapeData.dNdz;
        var A = shapeData.area;

        // Skip degenerate triangles
        if (A < MIN_AREA)
        {
            skippedDegenerateArea += 1;
            continue;
        }

        validElements += 1;

        // Centroid
        var y_c = (y1 + y2 + y3) / 3.0;
        var z_c = (z1 + z2 + z3) / 3.0;

        // Local stiffness and load (3×3 for linear triangle)
        var nodeIndices = [i1, i2, i3];

        for (var a = 0; a < 3; a += 1)
        {
            var ia = nodeIndices[a];

            // Element load vector
            // Dimensional analysis: (N/m²) * m² * m * (1/m) = N
            var f_local = G * A * (z_c * dNdy[a] - y_c * dNdz[a]);  // All plain numbers
            f[ia] += f_local;  // No unit stripping needed

            // Element stiffness matrix
            for (var b = 0; b < 3; b += 1)
            {
                var ib = nodeIndices[b];
                // Dimensional analysis: (N/m²) * m² * (1/m)² = N/m²
                var K_local = G * A * (dNdy[a] * dNdy[b] + dNdz[a] * dNdz[b]);  // All plain numbers
                K[ia][ib] += K_local;  // No unit stripping needed
            }
        }
    }

    println("FEM assembly: " ~ n ~ " nodes, " ~ size(triangles) ~ " triangles");
    println("  Valid elements: " ~ validElements ~ " (" ~
        (100.0 * validElements / size(triangles)) ~ "%)");
    println("  Skipped: " ~ skippedZeroG ~ " (G<1e-6), " ~
        skippedDegenerateArea ~ " (area<1e-12 m²)");

    // Warn if too many degenerate triangles (indicates mesh quality issues)
    if (skippedDegenerateArea > size(triangles) * 0.1)
    {
        var pct = (skippedDegenerateArea * 100.0 / size(triangles));
        println("  WARNING: " ~ skippedDegenerateArea ~ " triangles skipped due to tiny area (" ~
                pct ~ "%) - check mesh quality");
    }

    return {
        "K" : K,
        "f" : f,
        "numNodes" : n
    };
}

/**
 * Apply boundary condition to remove rigid body mode
 *
 * @param K : Global stiffness matrix (n×n)
 * @param f : Global load vector (n×1)
 * @param n : Number of nodes
 * @returns : map with { K, f } (modified in place)
 *
 * Pin first node: ψ[0] = 0
 * - Set K[0,:] = 0, K[:,0] = 0, K[0,0] = 1
 * - Set f[0] = 0
 *
 * This removes the singularity (∇²ψ = 0 has constant solutions)
 */
function applyBoundaryCondition(K is array, f is array, n is number) returns map
{
    // Pin first node to remove rigid body mode
    for (var j = 0; j < n; j += 1)
    {
        K[0][j] = 0.0;
        K[j][0] = 0.0;
    }
    K[0][0] = 1.0;
    f[0] = 0.0;

    // Pin any unused nodes (disconnected from mesh) to prevent singularity
    // These nodes have zero diagonal entries (not part of any triangle)
    var pinnedNodes = 0;
    for (var i = 1; i < n; i += 1)
    {
        if (abs(K[i][i]) < 1e-15)
        {
            // Node i is unused - pin it
            for (var j = 0; j < n; j += 1)
            {
                K[i][j] = 0.0;
                K[j][i] = 0.0;
            }
            K[i][i] = 1.0;
            f[i] = 0.0;
            pinnedNodes += 1;
        }
    }

    var activeNodes = n - 1 - pinnedNodes;  // Subtract node 0 and pinned nodes
    println("  BC applied: " ~ activeNodes ~ " active nodes, " ~
        (pinnedNodes + 1) ~ " pinned (including node 0)");

    return {
        "K" : K,
        "f" : f
    };
}

/**
 * Solve FEM system K·ψ = f for warping function
 *
 * @param K : Stiffness matrix (n×n) with units
 * @param f : Load vector (n×1) with units
 * @param n : Number of nodes
 * @returns : ψ array (dimensionless) or undefined if solver fails
 */
function solveFEMSystem(K is array, f is array, n is number) returns array
{
    // Check matrix diagonal health
    var diagZeros = 0;
    var diagNonZeros = 0;
    var diagMax = 0.0;
    var diagMin = 1e99;

    for (var i = 0; i < n; i += 1)
    {
        var d = abs(K[i][i]);
        if (d < 1e-15)
        {
            diagZeros += 1;
        }
        else
        {
            diagNonZeros += 1;
            if (d > diagMax) diagMax = d;
            if (d < diagMin) diagMin = d;
        }
    }

    println("  Matrix diagonal: " ~ diagNonZeros ~ " non-zero, " ~
        diagZeros ~ " zero entries");
    println("    Diagonal range: [" ~ diagMin ~ ", " ~ diagMax ~ "]");

    if (diagZeros > n / 2)
    {
        println("  WARNING: More than 50% of diagonal is zero - matrix likely singular");
    }

    // Solve system using dense Gaussian elimination
    var psi = solveLinearSystem(K, f, n);

    if (psi == undefined)
    {
        println("ERROR: Linear solver failed - matrix is singular or ill-conditioned");
        return [];  // Return empty array instead of undefined
    }

    return psi;
}

/**
 * Compute GJ from warping function and polar moments
 *
 * @param triangles : Array of [i,j,k] node indices
 * @param G_elem : Shear modulus for each triangle (plain numbers, implicit N/m²)
 * @param psi : Warping function values at nodes (dimensionless)
 * @param sectionPoints : Array of {point2D: [y,z], ...}
 * @returns : GJ in N·m²
 *
 * For each triangle:
 *   Jp_e = polar moment of inertia (exact for triangle)
 *   dpsi_dy, dpsi_dz = constant gradients from nodal ψ
 *   warp_correction = A * (y_c * dpsi_dz - z_c * dpsi_dy)
 *   GJ += G * (Jp_e + warp_correction)
 *
 * Polar moment (exact closed form):
 *   Jp = A/6 * (y1² + y2² + y3² + z1² + z2² + z3² + y1*y2 + y2*y3 + y3*y1 + z1*z2 + z2*z3 + z3*z1)
 */
function computeGJFromWarping(triangles is array, G_elem is array, psi is array, sectionPoints is array, y_bar is number, z_bar is number) returns ValueWithUnits
{
    var GJ_sum = 0.0;  // Accumulate as plain number, implicit N·m²
    var Jp_total = 0.0;   // Diagnostic: sum of G*Jp_e contributions
    var warp_total = 0.0; // Diagnostic: sum of G*warp_correction contributions
    var Iz_sum = 0.0;  // For thin-plate formula: GJ = 4 * Iz_sum

    // Defensive check: ensure psi array is valid
    if (size(psi) == 0)
    {
        println("WARNING: Empty psi array in computeGJFromWarping - returning GJ = 0");
        return 0 * newton * meter * meter;
    }

    for (var e = 0; e < size(triangles); e += 1)
    {
        var tri = triangles[e];
        var i1 = tri[0];
        var i2 = tri[1];
        var i3 = tri[2];

        var G = G_elem[e];  // Plain number, implicit N/m²

        // Skip elements with zero stiffness
        if (G < 1e-6)  // Implicit N/m²
        {
            continue;
        }

        // Get nodal coordinates
        var pt1 = sectionPoints[i1].point2D;
        var pt2 = sectionPoints[i2].point2D;
        var pt3 = sectionPoints[i3].point2D;

        var y1 = pt1[0] / meter - y_bar;
        var z1 = pt1[1] / meter - z_bar;
        var y2 = pt2[0] / meter - y_bar;
        var z2 = pt2[1] / meter - z_bar;
        var y3 = pt3[0] / meter - y_bar;
        var z3 = pt3[1] / meter - z_bar;

        // Compute shape function gradients and area
        var shapeData = computeShapeGradients(y1, z1, y2, z2, y3, z3);
        var dNdy = shapeData.dNdy;
        var dNdz = shapeData.dNdz;
        var A = shapeData.area;

        // Skip degenerate triangles
        if (A < MIN_AREA)
        {
            continue;
        }

        // Get warping function values at nodes (dimensionless)
        var psi1 = psi[i1];
        var psi2 = psi[i2];
        var psi3 = psi[i3];

        // Compute warping gradients (constant over element)
        var dpsi_dy = psi1 * dNdy[0] + psi2 * dNdy[1] + psi3 * dNdy[2];
        var dpsi_dz = psi1 * dNdz[0] + psi2 * dNdz[1] + psi3 * dNdz[2];

        // Centroid
        var y_c = (y1 + y2 + y3) / 3.0;
        var z_c = (z1 + z2 + z3) / 3.0;

        // Polar moment of inertia (exact closed form for triangle)
        // Jp = Iy + Iz where:
        //   Iy = ∫z² dA = (A/6) * (z1² + z2² + z3² + z1*z2 + z1*z3 + z2*z3)
        //   Iz = ∫y² dA = (A/6) * (y1² + y2² + y3² + y1*y2 + y1*y3 + y2*y3)
        var Iy = (A / 6.0) * (z1*z1 + z2*z2 + z3*z3 + z1*z2 + z1*z3 + z2*z3);
        var Iz = (A / 6.0) * (y1*y1 + y2*y2 + y3*y3 + y1*y2 + y1*y3 + y2*y3);
        var Jp_e = Iy + Iz;

        // Warping correction
        var warp_correction = A * (y_c * dpsi_dz - z_c * dpsi_dy);

        // Accumulate GJ element contribution
        // Dimensional analysis: (N/m²) * m⁴ = N·m²
        Jp_total += G * Jp_e;
        warp_total += G * warp_correction;
        GJ_sum += G * (Jp_e + warp_correction);  // All plain numbers, implicit N·m²
        Iz_sum += G * Iz;
    }

    // Diagnostic: breakdown of Jp vs warping correction
    // After fix, warp_total should be strongly negative for flat/wide sections
    println("    Jp total = " ~ Jp_total ~ " N·m²");
    println("    Warp correction = " ~ warp_total ~ " N·m²");
    println("    J_SV (Jp + warp) = " ~ GJ_sum ~ " N·m²");

    // Use thin-plate formula (GJ = 4*Iz) — analytically exact for b/t >> 1,
    // far more accurate than coarse FEM for ski cross-sections.
    // FEM result (J_SV) kept above as diagnostic to show warping solve comparison.
    var GJ_thin_plate = 4.0 * Iz_sum;
    println("    GJ_thin_plate (4·Iz) = " ~ GJ_thin_plate ~ " N·m²");
    println("    Ratio FEM/thin-plate = " ~ (GJ_sum / GJ_thin_plate));
    return GJ_thin_plate * newton * meter * meter;
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
