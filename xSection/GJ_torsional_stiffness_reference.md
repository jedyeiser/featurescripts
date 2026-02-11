# Torsional Stiffness (GJ) via Saint-Venant FEM — Implementation Reference

## Coordinate System
- **x** = along the ski (beam axis)
- **y** = width
- **z** = height
- Cross-section lives in the **y-z plane**

## Problem Statement

Compute the Saint-Venant torsional stiffness GJ at each cross-section station along the ski. This is the
torsional analog of the bending stiffness EI we already compute.

Twisting the ski about the x-axis produces shear strains γ_xy and γ_xz (beam axis paired with each cross-
section direction). The Saint-Venant approach solves for the **warping function** ψ(y,z), which describes how
the cross-section deforms out-of-plane under twist.

## Inputs (already available)

- **Triangulated mesh** of each cross-section: nodes (y,z) and triangle connectivity, with material/part
assignment per element
- **Material properties**: shear modulus G for isotropic parts; ABD/Q matrices for composite laminates
- **Part geometry**: top skin, bottom skin, core, edges, base, etc.

## Extracting Shear Modulus Per Element

### Isotropic parts (core, steel edges, base material, etc.)
```
G = E / (2 * (1 + ν))    — or use known G directly from material data
```

### Composite parts — Tier 1 (isotropic approximation, recommended starting point)
```
G_eff = A₆₆ / t_total

where:
  A₆₆ = (3,3) entry of the A matrix (in-plane shear stiffness, N/m)
  t_total = total laminate thickness
```
This is equivalent to the thickness-weighted average of Q̄₆₆ across plies. Assign this single G value per
element and use the isotropic formulation below.

### Composite parts — Tier 2 (anisotropic, optional upgrade)

Each composite element carries TWO shear moduli based on which cross-section direction is in-plane vs through-
thickness for that part:

**Top/bottom skins** (laminate in x-y plane, normal ≈ z):
- G_xy = A₆₆ / t  (in-plane — strong direction)
- G_xz = G_transverse (through-thickness — weak direction)

**Sidewalls** (laminate in x-z plane, normal ≈ y):
- G_xz = A₆₆ / t  (in-plane — strong direction)
- G_xy = G_transverse (through-thickness — weak direction)

**Estimating G_transverse** (not available from CLT/ABD):
- Needs raw ply transverse shear properties: G₁₃, G₂₃
- For UD glass/carbon: G₁₃ ≈ G₁₂; G₂₃ ≈ 0.5 × G₁₂
- For woven glass: G_transverse ≈ 1.5–3 GPa (resin-dominated)
- Effective laminate transverse G uses harmonic mean (series model):
  `1/G_trans = (1/t) * Σ(t_k / G₂₃_k)`

**Expected accuracy difference**: Tier 1 vs Tier 2 is roughly 5–10% for typical ski sections. The bigger
accuracy win comes from using Saint-Venant at all (vs Bredt-Batho), which captures core contribution and thick
edge effects.

## FEM Formulation

### Governing PDE

**Isotropic (Tier 1):**
```
∇·(G ∇ψ) = 0    inside domain
G ∂ψ/∂n = G(z·ny - y·nz)    on boundary (Neumann BC)
```

**Anisotropic (Tier 2):**
```
∂/∂y(G_xy · ∂ψ/∂y) + ∂/∂z(G_xz · ∂ψ/∂z) = 0
```

### Weak form (what we actually solve)

Converts to the linear system **K·ψ = f** where K is the global stiffness matrix and f is the load vector.

### Assembly — Isotropic (Tier 1)

For linear triangles, shape function gradients are constant per element — no Gauss quadrature needed.

```python
# Per element e with nodes (i, j, k):
y1, z1 = nodes[i]
y2, z2 = nodes[j]
y3, z3 = nodes[k]
G = G_elem[e]

# Element area
A = 0.5 * abs((y2 - y1) * (z3 - z1) - (y3 - y1) * (z2 - z1))

# Shape function gradients (constant per element)
inv_2A = 1.0 / (2.0 * A)
dNdy = inv_2A * [z2 - z3, z3 - z1, z1 - z2]    # (3,)
dNdz = inv_2A * [y3 - y2, y1 - y3, y2 - y1]    # (3,)

# Element stiffness (3×3)
K_local[a, b] = G * A * (dNdy[a]*dNdy[b] + dNdz[a]*dNdz[b])

# Element load vector (3,)
# Uses centroid (exact for linear shape functions × linear coordinates)
y_c = (y1 + y2 + y3) / 3.0
z_c = (z1 + z2 + z3) / 3.0
f_local[a] = G * A * (z_c * dNdy[a] - y_c * dNdz[a])

# Scatter into global K and f using DOF indices [i, j, k]
```

### Assembly — Anisotropic (Tier 2)

Only the element stiffness changes:
```python
K_local[a, b] = A * (G_xy * dNdy[a]*dNdy[b] + G_xz * dNdz[a]*dNdz[b])
```
Load vector uses a representative G (e.g., average of G_xy and G_xz, or keep the isotropic load form — the
difference is small).

### Solve

The system is singular (ψ defined up to a constant). Pin one node:
```python
pin = 0    # choice doesn't affect GJ
K[pin, :] = 0
K[:, pin] = 0
K[pin, pin] = 1.0
f[pin] = 0.0

psi = scipy.sparse.linalg.spsolve(K, f)
```

Matrix is symmetric positive definite after pinning — very well-behaved.

## Computing GJ from ψ

### Isotropic (Tier 1)

```python
GJ = 0.0

for each element e with nodes (i, j, k):
    y1, z1 = nodes[i]
    y2, z2 = nodes[j]
    y3, z3 = nodes[k]
    G = G_elem[e]
    A = <element area>

    # Gradient of ψ in this element (constant)
    dpsi_dy = dot(dNdy, [psi[i], psi[j], psi[k]])
    dpsi_dz = dot(dNdz, [psi[i], psi[j], psi[k]])

    # Centroid
    y_c = (y1 + y2 + y3) / 3.0
    z_c = (z1 + z2 + z3) / 3.0

    # Polar moment integral over triangle (exact closed form)
    Iy = (A / 6) * (y1**2 + y2**2 + y3**2 + y1*y2 + y1*y3 + y2*y3)
    Iz = (A / 6) * (z1**2 + z2**2 + z3**2 + z1*z2 + z1*z3 + z2*z3)
    Jp_e = Iy + Iz

    # Warping correction
    warp_correction = A * (y_c * dpsi_dz - z_c * dpsi_dy)

    GJ += G * (Jp_e + warp_correction)
```

Note: The warping correction always REDUCES GJ relative to G*Jp. For a circular section, ψ=0 and GJ = G*Jp
exactly.

## Implementation Notes

1. **Coordinate centering**: y,z should be relative to a reference point (geometric centroid or neutral axis).
Doesn't affect GJ but improves numerical conditioning.

2. **Mesh compatibility**: Triangles from different parts must share nodes along interfaces. If part meshes
are independent, stitch them first so boundary nodes are shared.

3. **Sparse assembly**: Use scipy.sparse (COO format for assembly → convert to CSC for solve). Typical cross-
section = hundreds of triangles, solves in milliseconds.

4. **Per-station computation**: Run this at each cross-section station along the ski to produce a GJ(x)
profile, analogous to the existing EI(x) profile.

5. **Validation idea**: For a rectangular homogeneous section of width b and height h, the exact GJ is known:
   `GJ = G * b * h³ * (1/3 - 0.21*(h/b)*(1 - h⁴/(12*b⁴)))` (for b ≥ h)
   Mesh a rectangle, run the solver, compare.

