FeatureScript 2878;

/**
 * Torsional Stiffness (GJ) Calculation for Cross-Sections
 *
 * Implements Saint-Venant FEM method to compute effective torsional stiffness.
 * Uses linear triangular elements with isotropic shear modulus (Tier 1 approach).
 *
 * Mathematical Background:
 * - Governing equation: ∇·(G ∇ψ) = 0 (interior)
 * - Boundary condition: G ∂ψ/∂n = G(z·ny - y·nz)
 * - FEM discretization: K·ψ = f
 * - GJ integration: GJ = Σ G_e * (Jp_e + warp_correction)
 *
 * @see GJ_torsional_stiffness_reference.md for mathematical derivation
 */

import(path : "onshape/std/common.fs", version : "2878.0");
// IMPORT: tools/solvers.fs
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");


const MIN_AREA = 1e-12 * meter * meter;

/**
 * Main entry point: Compute torsional stiffness GJ for a cross-section
 *
 * @param section : Cross-section data with bodyData, sectionPoints, frame
 * @param bodies : Array of body material definitions with Q matrices
 * @returns : GJ_eff in N·m² (ValueWithUnits)
 *
 * Process:
 * 1. Extract triangulated mesh from section.bodyData
 * 2. Build shear modulus array (one G per triangle from Q66)
 * 3. Assemble global FEM system (K·ψ = f)
 * 4. Apply boundary condition (pin one node)
 * 5. Solve for warping function ψ
 * 6. Integrate GJ from ψ gradients and polar moments
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
        println("ERROR: Mesh too large (" ~ numNodes ~ " nodes) - skipping GJ computation");
        return 0 * newton * meter * meter;
    }

    if (numNodes > 500)
    {
        println("WARNING: Large mesh (" ~ numNodes ~ " nodes) - GJ computation may be slow");
    }

    // Extract shear modulus for each triangle from material Q matrices
    var G_elem = extractShearModuli(triangles, bodyIndices, bodies);

    // Check if any structural material exists
    var totalG = 0;
    for (var i = 0; i < size(G_elem); i += 1)
    {
        totalG += G_elem[i] / (newton / (meter * meter));
    }
    if (totalG < 1e-6) // Essentially zero
    {
        println("WARNING: No structural material found - GJ = 0");
        return 0 * newton * meter * meter;
    }

    // Assemble global FEM system K·ψ = f
    var femSystem = assembleFEMSystem(triangles, G_elem, section.sectionPoints, numNodes);

    // Apply boundary condition to remove rigid body mode (pin one node)
    femSystem = applyBoundaryCondition(femSystem.K, femSystem.f, numNodes);

    // Solve linear system for warping function ψ
    var psi = solveFEMSystem(femSystem.K, femSystem.f, numNodes);

    if (psi == undefined)
    {
        println("ERROR: FEM system failed to solve - returning GJ = 0");
        return 0 * newton * meter * meter;
    }

    // Integrate GJ from warping function and polar moments
    var GJ = computeGJFromWarping(triangles, G_elem, psi, section.sectionPoints);

    // Validate result
    if (GJ < 0 * newton * meter * meter)
    {
        println("WARNING: Negative GJ detected (" ~ GJ ~ ") - clamping to 0 (numerical issue)");
        GJ = 0 * newton * meter * meter;
    }

    return GJ;
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
 * @returns : Array of G values (ValueWithUnits) - one per triangle
 *
 * Lookup chain: triangle → bodyIdx → Q matrix → Q[2][2] (Q66)
 * Q is stored as 3×3: [[Q11, Q12, Q16], [Q12, Q22, Q26], [Q16, Q26, Q66]]
 * - Isotropic: Q66 = E/(2*(1+ν))
 * - Orthotropic: Q66 = G12
 * - Missing material: G = 0 (contributes no stiffness)
 */
function extractShearModuli(triangles is array, bodyIndices is array, bodies is array) returns array
{
    var G_elem = [];

    for (var i = 0; i < size(triangles); i += 1)
    {
        var bodyIdx = bodyIndices[i];
        var G = 0 * newton / (meter * meter);

        // Find corresponding body material
        for (var body in bodies)
        {
            if (body.bodyIdx == bodyIdx)
            {
                if (body.hasMaterialData &&
                    body.materialData != undefined &&
                    body.materialData.qMatrix != undefined)
                {
                    // Q66 is shear modulus - stored at [2][2] in 3×3 matrix
                    // Q = [[Q11, Q12, Q16], [Q12, Q22, Q26], [Q16, Q26, Q66]]
                    G = body.materialData.qMatrix[2][2];
                }
                break;
            }
        }

        G_elem = append(G_elem, G);
    }

    return G_elem;
}

/**
 * Assemble global FEM system K·ψ = f
 *
 * @param triangles : Array of [i,j,k] node indices
 * @param G_elem : Shear modulus for each triangle
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
function assembleFEMSystem(triangles is array, G_elem is array, sectionPoints is array, numNodes is number) returns map
{
    // Initialize global arrays
    var n = numNodes;
    var K = makeArray(n);
    var f = makeArray(n);

    for (var i = 0; i < n; i += 1)
    {
        K[i] = makeArray(n, 0.0 * newton / (meter * meter));
        f[i] = 0.0 * newton;
    }

    // Loop over all triangles
    for (var e = 0; e < size(triangles); e += 1)
    {
        var tri = triangles[e];
        var i1 = tri[0];
        var i2 = tri[1];
        var i3 = tri[2];

        var G = G_elem[e];

        // Skip elements with zero stiffness
        if (G < 1e-6 * newton / (meter * meter))
        {
            continue;
        }

        // Get nodal coordinates in local (y,z) frame
        var pt1 = sectionPoints[i1].point2D;
        var pt2 = sectionPoints[i2].point2D;
        var pt3 = sectionPoints[i3].point2D;

        var y1 = pt1[0];
        var z1 = pt1[1];
        var y2 = pt2[0];
        var z2 = pt2[1];
        var y3 = pt3[0];
        var z3 = pt3[1];

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

        // Centroid
        var y_c = (y1 + y2 + y3) / 3.0;
        var z_c = (z1 + z2 + z3) / 3.0;

        // Local stiffness and load (3×3 for linear triangle)
        var nodeIndices = [i1, i2, i3];

        for (var a = 0; a < 3; a += 1)
        {
            var ia = nodeIndices[a];

            // Element load vector
            var f_local = G * A * (z_c * dNdy[a] - y_c * dNdz[a]);
            f[ia] += f_local;

            // Element stiffness matrix
            for (var b = 0; b < 3; b += 1)
            {
                var ib = nodeIndices[b];
                var K_local = G * A * (dNdy[a] * dNdy[b] + dNdz[a] * dNdz[b]);
                K[ia][ib] += K_local;
            }
        }
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
    // Pin first node
    for (var j = 0; j < n; j += 1)
    {
        K[0][j] = 0.0;
        K[j][0] = 0.0;
    }
    K[0][0] = 1.0;
    f[0] = 0.0;

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
 *
 * Strips units for solveLinearSystem() call (expects plain numbers)
 * Returns dimensionless ψ values
 */
function solveFEMSystem(K is array, f is array, n is number) returns array
{
    // Strip units from K and f for solver
    // K has units: G * A * (dN/dy)² = (N/m²) * m² * (1/m²) = N/m²
    // f has units: G * A * distance * (dN/dy) = (N/m²) * m² * m * (1/m) = N
    var K_plain = makeArray(n);
    var f_plain = makeArray(n);

    var K_unit = newton / (meter * meter);  // N/m²
    var f_unit = newton;                     // N

    for (var i = 0; i < n; i += 1)
    {
        K_plain[i] = makeArray(n);
        for (var j = 0; j < n; j += 1)
        {
            K_plain[i][j] = K[i][j] / K_unit;
        }
        f_plain[i] = f[i] / f_unit;
    }

    // Solve using dense Gaussian elimination (returns undefined if singular)
    var psi = solveLinearSystem(K_plain, f_plain, n);

    if (psi == undefined)
    {
        println("ERROR: Linear solver failed - matrix is singular or ill-conditioned");
    }

    return psi;
}

/**
 * Compute GJ from warping function and polar moments
 *
 * @param triangles : Array of [i,j,k] node indices
 * @param G_elem : Shear modulus for each triangle
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
function computeGJFromWarping(triangles is array, G_elem is array, psi is array, sectionPoints is array) returns ValueWithUnits
{
    var GJ = 0 * newton * meter * meter;

    for (var e = 0; e < size(triangles); e += 1)
    {
        var tri = triangles[e];
        var i1 = tri[0];
        var i2 = tri[1];
        var i3 = tri[2];

        var G = G_elem[e];

        // Skip elements with zero stiffness
        if (G < 1e-6 * newton / (meter * meter))
        {
            continue;
        }

        // Get nodal coordinates
        var pt1 = sectionPoints[i1].point2D;
        var pt2 = sectionPoints[i2].point2D;
        var pt3 = sectionPoints[i3].point2D;

        var y1 = pt1[0];
        var z1 = pt1[1];
        var y2 = pt2[0];
        var z2 = pt2[1];
        var y3 = pt3[0];
        var z3 = pt3[1];

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

        // Accumulate GJ
        GJ += G * (Jp_e + warp_correction);
    }

    return GJ;
}

/**
 * Compute constant shape function gradients for linear triangle
 *
 * @param y1, z1, y2, z2, y3, z3 : Nodal coordinates (ValueWithUnits)
 * @returns : map with { dNdy: [3], dNdz: [3], area: ValueWithUnits }
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
export function computeShapeGradients(y1 is ValueWithUnits, z1 is ValueWithUnits,
                                       y2 is ValueWithUnits, z2 is ValueWithUnits,
                                       y3 is ValueWithUnits, z3 is ValueWithUnits) returns map
{
    // Compute twice the signed area
    var twoA = (y2 - y1) * (z3 - z1) - (y3 - y1) * (z2 - z1);

    // Area (take absolute value)
    var A = abs(twoA) / 2.0;

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
