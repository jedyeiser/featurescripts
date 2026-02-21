"""
Classical Laminate Theory (CLT) module.

Computes effective laminate properties from per-component JSON records
(as stored in orthotropicMaterials.json).

Unit contract
-------------
- Inputs  : E1, E2, G12 in MPa; thickness in mm; v12 dimensionless
- Outputs : Q*, A in MPa·mm; B in MPa·mm²; D in MPa·mm³; Ex/Ey/Gxy in MPa
- All moduli in this file are in MPa unless explicitly noted.

This module is self-contained (numpy only) so it can be run from the
materialData/ directory without any cross-project imports.
"""

import numpy as np


# ---------------------------------------------------------------------------
# Core ply functions
# ---------------------------------------------------------------------------

def ply_Q(E1: float, E2: float, G12: float, v12: float) -> np.ndarray:
    """
    3×3 reduced stiffness matrix Q for a unidirectional ply.

    Standard plane-stress formulation:
        v21 = v12 * E2/E1
        denom = 1 - v12*v21
        Q11 = E1/denom
        Q22 = E2/denom
        Q12 = v12*E2/denom  (= v21*E1/denom)
        Q66 = G12

    Parameters
    ----------
    E1, E2, G12 : moduli in MPa (or any consistent unit)
    v12         : major Poisson's ratio (dimensionless)

    Returns
    -------
    Q : 3×3 ndarray  [[Q11, Q12, 0], [Q12, Q22, 0], [0, 0, Q66]]
    """
    v21 = v12 * E2 / E1
    denom = 1.0 - v12 * v21
    Q11 = E1 / denom
    Q22 = E2 / denom
    Q12 = v12 * E2 / denom   # = v21 * E1 / denom
    Q66 = G12
    return np.array([[Q11, Q12, 0.0],
                     [Q12, Q22, 0.0],
                     [0.0, 0.0, Q66]], dtype=float)


def rotate_Q(Q: np.ndarray, theta_deg: float) -> np.ndarray:
    """
    Rotate reduced stiffness Q from ply-local to global coordinates.

    Implements the standard CLT transformation (Tsai-Pagano/Jones notation).
    Matches composite_toolkit.py::rotate_reduced_stiffness exactly.

    Parameters
    ----------
    Q         : 3×3 unrotated Q (from ply_Q or rand_Q)
    theta_deg : rotation angle in degrees (ply fibre direction → global x)

    Returns
    -------
    Qbar : 3×3 rotated Q̄  [[Q̄11, Q̄12, Q̄16],
                             [Q̄12, Q̄22, Q̄26],
                             [Q̄16, Q̄26, Q̄66]]
    """
    th = np.deg2rad(theta_deg)
    c, s = np.cos(th), np.sin(th)
    c2, s2 = c * c, s * s
    c4, s4 = c2 * c2, s2 * s2

    Q11, Q12, Q22, Q66 = Q[0, 0], Q[0, 1], Q[1, 1], Q[2, 2]

    Q11b = Q11 * c4 + Q22 * s4 + 2.0 * (Q12 + 2.0 * Q66) * s2 * c2
    Q22b = Q11 * s4 + Q22 * c4 + 2.0 * (Q12 + 2.0 * Q66) * s2 * c2
    Q12b = (Q11 + Q22 - 4.0 * Q66) * s2 * c2 + Q12 * (s4 + c4)
    Q16b = (Q11 - Q12 - 2.0 * Q66) * (c ** 3 * s) - (Q22 - Q12 - 2.0 * Q66) * (c * s ** 3)
    Q26b = (Q11 - Q12 - 2.0 * Q66) * (c * s ** 3) - (Q22 - Q12 - 2.0 * Q66) * (c ** 3 * s)
    Q66b = (Q11 + Q22 - 2.0 * Q12 - 2.0 * Q66) * s2 * c2 + Q66 * (s4 + c4)

    return np.array([[Q11b, Q12b, Q16b],
                     [Q12b, Q22b, Q26b],
                     [Q16b, Q26b, Q66b]], dtype=float)


def rand_Q(E1: float, E2: float, v12: float) -> np.ndarray:
    """
    Isotropic approximation for randomly-oriented plies (Tsai-Pagano).

    Formula
    -------
        E_iso = 3/8 * E1 + 5/8 * E2
        G_iso = E_iso / (2 * (1 + v12))

    NOTE: composite_toolkit.py uses E_iso = max(0.16*E1, E2) instead.
    If integration tests show RAND plies dominate the discrepancy, this
    is a lever to investigate.  For materials where E1==E2 (already
    isotropic), both formulae give the same E_iso.

    Parameters
    ----------
    E1, E2 : longitudinal / transverse moduli in MPa
    v12    : Poisson's ratio used for the isotropic G (minor approximation)

    Returns
    -------
    Q : 3×3 isotropic reduced stiffness (Q16=Q26=0, Q11=Q22)
    """
    E_iso = 3.0 / 8.0 * E1 + 5.0 / 8.0 * E2
    G_iso = E_iso / (2.0 * (1.0 + v12))
    denom = 1.0 - v12 ** 2
    Q11 = E_iso / denom
    Q12 = v12 * E_iso / denom
    Q66 = G_iso
    return np.array([[Q11, Q12, 0.0],
                     [Q12, Q11, 0.0],   # Q22 = Q11 for isotropic
                     [0.0, 0.0, Q66]], dtype=float)


# ---------------------------------------------------------------------------
# Laminate assembly
# ---------------------------------------------------------------------------

def compute_laminate(record: dict, theta_deg: float = 0) -> dict:
    """
    Compute effective laminate properties from a JSON record.

    Thickness allocation
    --------------------
        t_k [mm] = (gsm_k / record['fiber_weight_gsm']) * record['thickness_mm']

    This distributes total composite thickness proportionally to fiber weight,
    which is exact when all fibers have the same density and gives a good
    approximation for mixed glass/carbon laminates.

    Stacking order
    --------------
    Components are processed in their JSON list order, bottom-to-top, with
    the laminate mid-plane at z=0.

    Boundary condition
    ------------------
    Q* = A / h  (kappa=0, curvature constrained).  This is appropriate for
    comparing to effective stiffness values derived from a symmetric laminate
    assumption (which is how the CompositeCalculator.xlsx operates).

    Parameters
    ----------
    record    : one entry from orthotropicMaterials.json
    theta_deg : global coordinate rotation applied to all ply orientations
                (default 0 — fibre reference frame)

    Returns
    -------
    dict with keys:
        Q11, Q22, Q12, Q66, Q16, Q26  — Q* = A/h  [MPa]
        Ex, Ey, Gxy                    — engineering constants [MPa]
        nu_xy                          — effective Poisson's ratio [–]
        h_mm                           — total laminate thickness [mm]
        A                              — in-plane stiffness matrix  [MPa·mm]
        B                              — coupling stiffness matrix   [MPa·mm²]
        D                              — bending stiffness matrix    [MPa·mm³]
        alpha_x                        — effective CTE, global x-direction [1/K]
        alpha_y                        — effective CTE, global y-direction [1/K]
                                         (valid for symmetric laminates; B≈0)
    """
    components = record['components']
    fiber_weight_gsm = float(record['fiber_weight_gsm'])
    thickness_mm = float(record['thickness_mm'])

    # Per-ply thickness allocation (proportional to fiber weight)
    thicknesses = [
        (float(comp['gsm']) / fiber_weight_gsm) * thickness_mm
        for comp in components
    ]

    h = sum(thicknesses)  # Should equal record['thickness_mm'] within float rounding

    # z-coordinates: laminate centred at mid-plane z=0
    z_bot = -0.5 * h
    z_edges = [z_bot]
    for t_k in thicknesses:
        z_edges.append(z_edges[-1] + t_k)

    # Accumulate ABD matrices and thermal force resultant N_T
    A   = np.zeros((3, 3), dtype=float)
    B   = np.zeros((3, 3), dtype=float)
    D   = np.zeros((3, 3), dtype=float)
    N_T = np.zeros(3,      dtype=float)   # thermal force per unit ΔT [MPa·mm/K]

    for i, comp in enumerate(components):
        ori   = comp['orientation']
        E1    = float(comp['E1'])
        E2    = float(comp['E2'])
        G12   = float(comp['G12'])
        v12   = float(comp['v12'])
        # CTE components; default to 0 if absent (no thermal contribution)
        a1    = float(comp.get('alpha_1', 0.0))   # longitudinal CTE [1/K]
        a2    = float(comp.get('alpha_2', 0.0))   # transverse CTE   [1/K]

        if isinstance(ori, str):
            # Non-numeric orientation (RAND, etc.) → isotropic approximation
            Qbar = rand_Q(E1, E2, v12)
            # 2-D random CTE: isotropic average  α_iso = (α1+α2)/2
            a_iso   = (a1 + a2) / 2.0
            alpha_bar = np.array([a_iso, a_iso, 0.0])
        else:
            # Standard directional ply: rotate by (orientation - theta_deg)
            phi    = float(ori) - theta_deg
            Q_local = ply_Q(E1, E2, G12, v12)
            Qbar   = rotate_Q(Q_local, phi)
            # Rotate CTE from ply-local to global (standard transformation)
            pr = np.deg2rad(phi)
            cp, sp = np.cos(pr), np.sin(pr)
            alpha_bar = np.array([
                a1 * cp**2 + a2 * sp**2,
                a1 * sp**2 + a2 * cp**2,
                2.0 * (a1 - a2) * cp * sp,   # engineering shear CTE
            ])

        z_km1, z_k = z_edges[i], z_edges[i + 1]
        t_k = z_k - z_km1
        A   += Qbar * t_k
        B   += 0.5  * Qbar * (z_k**2 - z_km1**2)
        D   += (1.0 / 3.0) * Qbar * (z_k**3 - z_km1**3)
        N_T += Qbar @ alpha_bar * t_k

    # Effective Q* = A/h  (kappa=0)
    Qstar = A / h

    # Engineering constants from compliance S* = Q*^-1
    try:
        Sstar = np.linalg.inv(Qstar)
    except np.linalg.LinAlgError:
        Sstar = np.linalg.pinv(Qstar)

    Ex    = 1.0 / Sstar[0, 0]
    Ey    = 1.0 / Sstar[1, 1]
    Gxy   = 1.0 / Sstar[2, 2]
    nu_xy = -Sstar[0, 1] / Sstar[0, 0]

    # Effective laminate CTE: {α*} = A⁻¹ · N_T
    # Valid for symmetric laminates (B≈0). For asymmetric stacks this is an
    # approximation; the full result requires the coupled ABD system.
    try:
        Ainv = np.linalg.inv(A)
    except np.linalg.LinAlgError:
        Ainv = np.linalg.pinv(A)
    alpha_eff = Ainv @ N_T
    alpha_x   = float(alpha_eff[0])
    alpha_y   = float(alpha_eff[1])

    return {
        'Q11':     Qstar[0, 0],
        'Q22':     Qstar[1, 1],
        'Q12':     Qstar[0, 1],
        'Q66':     Qstar[2, 2],
        'Q16':     Qstar[0, 2],
        'Q26':     Qstar[1, 2],
        'Ex':      Ex,
        'Ey':      Ey,
        'Gxy':     Gxy,
        'nu_xy':   nu_xy,
        'h_mm':    h,
        'A':       A,
        'B':       B,
        'D':       D,
        'alpha_x': alpha_x,   # effective in-plane CTE, x-direction  [1/K]
        'alpha_y': alpha_y,   # effective in-plane CTE, y-direction  [1/K]
    }
