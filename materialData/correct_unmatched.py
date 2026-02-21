"""
correct_unmatched.py

Applies corrections to unmatched (non-CLT) materials in both Onshape CSVs.
Run populate_onshape_data.py first (for the 47 CLT-matched materials).

Corrections applied
-------------------
Metals
  Steel edges  : nu corrected 0.33 -> 0.29; CTE = 11.5 um/m/K
  Titanal      : E -> 71.7 GPa, rho -> 2810 kg/m3, CTE = 23.6 um/m/K

UHMWPE
  Sintered grades (DS 4001, P-Tex 4504/3000/2100/2000, IS 4800, DS2000):
    rho -> 935 kg/m3, E -> 900 MPa, nu -> 0.46, CTE -> 1.5e-4 K-1
  Extruded grades (F7501, P-Tex 1100/900):
    rho -> 935 kg/m3, E -> 700 MPa, nu -> 0.46, CTE -> 1.5e-4 K-1

ABS
  Sidewall and 3D-print ABS: E -> 2.3 GPa, nu -> 0.38, CTE -> 9e-5 K-1

3D Print
  PP  : rho -> 900, E -> 1.6 GPa, nu -> 0.44, CTE -> 1.5e-4 K-1
  TPU : E -> 150 MPa, nu -> 0.47, CTE -> 1.2e-4 K-1

Wood (unit fix + CTE)
  All 12 wood species: E x 1000 (Pa values were actually MPa — unit labeling error)
  BComp BCore 300 XL, all WBK composite cores: CTE added only
  CTE_x (longitudinal/grain) = 4e-6 K-1, CTE_y (transverse) = 35e-6 K-1

Foam cores (PET structural foam)
  All IsoCore and Struct GRX grades: CTE = 7e-5 K-1 (isotropic)

Ecomass (Halpin-Tsai rule-of-mixtures + Turner CTE)
  Matrix: TPU (rho=1150, E=50 MPa, nu=0.49)
  Filler: SS particles (rho=7900, E=193 GPa, nu=0.30)
  3.0 g/cc: Vf=0.274, E -> 107 MPa, nu -> 0.44, CTE -> 1.71e-5 K-1
  3.4 g/cc: Vf=0.333, E -> 125 MPa, nu -> 0.43, CTE -> 1.70e-5 K-1
  4.5 g/cc: Vf=0.496, E -> 197 MPa, nu -> 0.40, CTE -> 1.70e-5 K-1

For KaiTai CSV, Q11/Q22/Q12/Q66/Q16/Q26 are recomputed from corrected E, nu
(isotropic plane-stress: Q11=Q22=E/(1-nu^2), Q12=nu*Q11, Q66=E/(2*(1+nu)),
 Q16=Q26=0).

Usage
-----
    cd materialData/
    python correct_unmatched.py
"""

import csv
from pathlib import Path

DATA_DIR   = Path(__file__).parent
KAITAI_CSV = DATA_DIR / "KaiTai_ski_snowboard_material_library.csv"
EXPORT_CSV = DATA_DIR / "Kaitai_ski_snowboard_material_library_EXPORT.csv"

# Q16/Q26 values below this (Pa) are CLT floating-point residuals, not real coupling.
# The one meaningful coupling term (C-PLY SP TBX270: ~4.68e8 Pa) is far above this.
_Q_NOISE_THRESHOLD = 1.0  # Pa


def _fmt(x) -> str:
    """Format a number for CSV output (6 significant figures)."""
    if x is None:
        return ""
    f = float(x)
    if f == 0.0:
        return "0"
    return f"{f:.6g}"


def _strip_commas(row: dict) -> dict:
    """Remove commas from all field values (thousands separators, etc.)."""
    return {k: v.replace(",", "") if isinstance(v, str) else v
            for k, v in row.items()}


def _isotropic_Q(E: float, nu: float) -> dict:
    """Plane-stress isotropic stiffness components [same units as E]."""
    denom = 1.0 - nu * nu
    Q11 = E / denom
    Q12 = nu * Q11
    Q66 = E / (2.0 * (1.0 + nu))
    return {"Q11": Q11, "Q22": Q11, "Q12": Q12, "Q66": Q66, "Q16": 0.0, "Q26": 0.0}


# ---------------------------------------------------------------------------
# Correction table
# Each entry: csv_name -> {field: new_value, ...}
# E in Pa, rho in kg/m3, nu dimensionless, CTE in 1/K
# ---------------------------------------------------------------------------

_steel = {
    "Poisson's Ratio": 0.29,
    "CTE_x [1/K]": 1.15e-5,
    "CTE_y [1/K]": 1.15e-5,
}
_UHMW_sint = {
    "Density [kg/m^3]": 935,
    "Young's Modulus [Pa]": 9e8,
    "Poisson's Ratio": 0.46,
    "CTE_x [1/K]": 1.5e-4,
    "CTE_y [1/K]": 1.5e-4,
}
_UHMW_ext = {
    "Density [kg/m^3]": 935,
    "Young's Modulus [Pa]": 7e8,
    "Poisson's Ratio": 0.46,
    "CTE_x [1/K]": 1.5e-4,
    "CTE_y [1/K]": 1.5e-4,
}
_ABS = {
    "Young's Modulus [Pa]": 2.3e9,
    "Poisson's Ratio": 0.38,
    "CTE_x [1/K]": 9e-5,
    "CTE_y [1/K]": 9e-5,
}
_wood_CTE = {
    "CTE_x [1/K]": 4e-6,
    "CTE_y [1/K]": 35e-6,
}
_foam_CTE = {
    "CTE_x [1/K]": 7e-5,
    "CTE_y [1/K]": 7e-5,
}

CORRECTIONS = {
    # Metals
    "CDW: Steel Edge":         _steel,
    "Freidrich: Steel Edge":   _steel,
    "CDW: Kaltbandstahl C75S": _steel,
    "AMAG: Titanal": {
        "Density [kg/m^3]": 2810,
        "Young's Modulus [Pa]": 7.17e10,
        "CTE_x [1/K]": 2.36e-5,
        "CTE_y [1/K]": 2.36e-5,
    },

    # UHMWPE sintered
    "Crown: DS 4001":       _UHMW_sint,
    "CPS: P-Tex 4504":      _UHMW_sint,
    "CPS: P-Tex 3000":      _UHMW_sint,
    "CPS: P-Tex 2100":      _UHMW_sint,
    "CPS: P-Tex 2000":      _UHMW_sint,
    "Isosport: IS 4800":    _UHMW_sint,
    "Crown: DS2000 UHMWPE": _UHMW_sint,   # sidewall UHMWPE, treated as sintered

    # UHMWPE extruded
    "Crown: F7501":    _UHMW_ext,
    "CPS: P-Tex 1100": _UHMW_ext,
    "CPS: P-Tex 900":  _UHMW_ext,

    # ABS
    "CPS: ABS/TPU Sidewall":       _ABS,
    "IMS Plast: CPAN 125 GT2 ABS": _ABS,
    "3D Printed ABS":               _ABS,

    # 3D Print - PP
    "3D Printed PP": {
        "Density [kg/m^3]": 900,
        "Young's Modulus [Pa]": 1.6e9,
        "Poisson's Ratio": 0.44,
        "CTE_x [1/K]": 1.5e-4,
        "CTE_y [1/K]": 1.5e-4,
    },

    # 3D Print - TPU (flexible filament)
    "3D Printed TPU": {
        "Young's Modulus [Pa]": 1.5e8,
        "Poisson's Ratio": 0.47,
        "CTE_x [1/K]": 1.2e-4,
        "CTE_y [1/K]": 1.2e-4,
    },

    # Wood - unit fix (x1000) + CTE
    "Ash":            {**_wood_CTE, "Young's Modulus [Pa]": 12_310_000 * 1000},
    "Aspen":          {**_wood_CTE, "Young's Modulus [Pa]":  9_750_000 * 1000},
    "Paulownia":      {**_wood_CTE, "Young's Modulus [Pa]":  5_600_000 * 1000},
    "Hard Maple":     {**_wood_CTE, "Young's Modulus [Pa]": 12_620_000 * 1000},
    "Soft Maple":     {**_wood_CTE, "Young's Modulus [Pa]": 11_300_000 * 1000},
    "European Beech": {**_wood_CTE, "Young's Modulus [Pa]": 14_310_000 * 1000},
    "Birch":          {**_wood_CTE, "Young's Modulus [Pa]": 13_860_000 * 1000},
    "Bamboo":         {**_wood_CTE, "Young's Modulus [Pa]": 12_600_000 * 1000},
    "Fir":            {**_wood_CTE, "Young's Modulus [Pa]": 10_240_000 * 1000},
    "Poplar - Light": {**_wood_CTE, "Young's Modulus [Pa]":  7_590_000 * 1000},
    "Poplar":         {**_wood_CTE, "Young's Modulus [Pa]":  9_245_000 * 1000},
    "Poplar - Dense": {**_wood_CTE, "Young's Modulus [Pa]": 10_900_000 * 1000},

    # BComp BCore (E already correct, CTE only)
    "BComp: BCore 300 XL": _wood_CTE,

    # WBK composite cores (CTE only — E values look correct already)
    "WBK # N21 - Offset Stringer (1x): Pal/Map":  _wood_CTE,
    "WBK # N12 - Offset Stringer (2x): Asp/Map":  _wood_CTE,
    "WBK # N3 - Solid: Asp Veneer":               _wood_CTE,
    "WBK # N5 - Alternating: Fir/Asp":            _wood_CTE,
    "WBK # N8 - Double Barrel: Fir/Asp":          _wood_CTE,
    "WBK # N2 - Solid: Asp":                      _wood_CTE,
    "WBK # N4 - Solid: Pal":                      _wood_CTE,
    "WBK # N13 - Offset Stringers (1x): Asp/Map": _wood_CTE,
    "WBK # N14 - Alternating Asp/Pal":            _wood_CTE,

    # Foam cores (PET structural foam, isotropic CTE)
    "Isosport: IsoCore 150":    _foam_CTE,
    "Isosport: IsoCore 250":    _foam_CTE,
    "Armacell: Struct GRX 100": _foam_CTE,
    "Armacell: Struct GRX 115": _foam_CTE,
    "Armacell: Struct GRX 150": _foam_CTE,
    "Armacell: Struct GRX 250": _foam_CTE,

    # Ecomass (Halpin-Tsai rule-of-mixtures, Turner CTE)
    # Matrix: TPU (rho=1150, E=50 MPa, nu=0.49)
    # Filler: SS particles (rho=7900, E=193 GPa, nu=0.30)
    "Ecomass: 4703ZC71 - TPU/SS - 3.0g/cc": {
        "Young's Modulus [Pa]": 1.07e8,   # Vf=0.274, Halpin-Tsai xi=2
        "Poisson's Ratio": 0.44,           # Voigt average
        "CTE_x [1/K]": 1.71e-5,           # Turner rule (SS-dominated)
        "CTE_y [1/K]": 1.71e-5,
    },
    "Ecomass: 4703ZC76 - TPU/SS - 3.4g/cc": {
        "Young's Modulus [Pa]": 1.25e8,
        "Poisson's Ratio": 0.43,
        "CTE_x [1/K]": 1.70e-5,
        "CTE_y [1/K]": 1.70e-5,
    },
    "Ecomass: 4703ZC87 - TPU/SS - 4.5g/cc": {
        "Young's Modulus [Pa]": 1.97e8,
        "Poisson's Ratio": 0.40,
        "CTE_x [1/K]": 1.70e-5,
        "CTE_y [1/K]": 1.70e-5,
    },

    # -----------------------------------------------------------------------
    # Round 2 corrections
    # -----------------------------------------------------------------------

    # --- BASF Elastollan polyether TPU (unfilled) ---
    # Research found these are LOWER than the 300 MPa initial estimate.
    # E-modulus estimates from Shore D hardness interpolation + literature.
    # Densities corrected (confirmed ~1120-1200 kg/m3, not 1370 placeholder).
    # nu ~0.48 for unfilled TPU (near-incompressible elastomer).
    # CTE: unfilled polyether TPU ~ 150-170e-6/K; higher Shore D → slightly lower.
    "BASF: E1160D50 TPU": {       # Elastollan 1160D, Shore D60, polyether
        "Density [kg/m^3]": 1120,
        "Young's Modulus [Pa]": 1.2e8,   # ~120 MPa (Shore D60 estimate)
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.55e-4,
        "CTE_y [1/K]": 1.55e-4,
    },
    "BASF: 1165D50STR TPU": {     # Elastollan ~1165D, Shore D65, polyether
        "Density [kg/m^3]": 1200,
        "Young's Modulus [Pa]": 2.0e8,   # ~200 MPa (Shore D65 estimate)
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.40e-4,
        "CTE_y [1/K]": 1.40e-4,
    },
    "BASF: 1154D50 TPU": {        # Elastollan 1154D, Shore D53-54, polyether
        "Density [kg/m^3]": 1170,
        "Young's Modulus [Pa]": 9.0e7,   # ~90 MPa (Shore D54 estimate)
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.65e-4,
        "CTE_y [1/K]": 1.65e-4,
    },

    # --- Geba Vitane R 3918 (= Desmovit DP R 3918) ---
    # Research confirmed: 20% GF-reinforced polyester TPU (Shore D69).
    # Flexural modulus = 1600 MPa → tensile E estimated ~1100-1400 MPa.
    # Density confirmed 1290 kg/m3 (CSV had 970 — erroneous placeholder).
    # CTE is strongly anisotropic due to glass fiber reinforcement:
    #   parallel to flow/fiber = 7e-6 K-1 (fiber-dominated)
    #   transverse             = 131e-6 K-1 (matrix-dominated)
    # nu ~0.37 for GF-reinforced grade.
    "Geba: TPU 3918": {
        "Density [kg/m^3]": 1290,
        "Young's Modulus [Pa]": 1.25e9,   # midpoint tensile estimate from flexural
        "Poisson's Ratio": 0.37,
        "CTE_x [1/K]": 7e-6,
        "CTE_y [1/K]": 1.31e-4,
    },
    # --- Geba Vitane R 9918 (probable "3919") ---
    # Similar to 3918 but slightly softer (Shore D65, flexural modulus 1500 MPa).
    "Geba: TPU 3919": {
        "Density [kg/m^3]": 1300,
        "Young's Modulus [Pa]": 1.15e9,   # midpoint from 1500 MPa flexural
        "Poisson's Ratio": 0.37,
        "CTE_x [1/K]": 1.03e-5,
        "CTE_y [1/K]": 1.14e-4,
    },
    # --- Desmovit DP R 3918 ---
    # Same material as Geba Vitane R 3918 (sold under BASF/geba dual brand).
    # CSV density=970 was an erroneous placeholder; actual = 1290 kg/m3.
    "Desmovit: DP R 3918": {
        "Density [kg/m^3]": 1290,
        "Young's Modulus [Pa]": 1.25e9,
        "Poisson's Ratio": 0.37,
        "CTE_x [1/K]": 7e-6,
        "CTE_y [1/K]": 1.31e-4,
    },

    # --- Isosport TPU IE 3200 SM 2EV 031 ---
    # No datasheet found. Likely unfilled or lightly filled TPU top-protection layer.
    # Keeping 300 MPa as conservative estimate pending user confirmation.
    "Isosport: TPU IE 3200 SM 2EV 031": {
        "Young's Modulus [Pa]": 3e8,
        "Poisson's Ratio": 0.47,
        "CTE_x [1/K]": 1.2e-4,
        "CTE_y [1/K]": 1.2e-4,
    },

    # --- Isosport ICP 5275 / 5278 (ISOCAP top sheet — ABS/TPU coextrusion) ---
    # Research found ICP 2115 = TPU/ABS bilayer coextrusion, E ~1000-1800 MPa.
    # 5275/5278 are different variants of the same ISOCAP product line.
    # Using 1200 MPa (midpoint); nu and CTE as for ABS-dominant material.
    "Isosport: ICP 5275": {
        "Young's Modulus [Pa]": 1.2e9,
        "Poisson's Ratio": 0.38,
        "CTE_x [1/K]": 9e-5,
        "CTE_y [1/K]": 9e-5,
    },
    "Isosport: ICP 5278": {
        "Young's Modulus [Pa]": 1.2e9,
        "Poisson's Ratio": 0.38,
        "CTE_x [1/K]": 9e-5,
        "CTE_y [1/K]": 9e-5,
    },

    # --- Watom Duraclear / 210 / 211 (clear TPU protective top-sheet film) ---
    # Research confirms: clear flexible TPU film (Shore D50-60), E ~ 80-200 MPa.
    # Using 100 MPa (revised from initial 50 MPa estimate); nu=0.48 for TPU.
    "Watom: Duraclear 117": {
        "Young's Modulus [Pa]": 1e8,
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.6e-4,
        "CTE_y [1/K]": 1.6e-4,
    },
    "Watom: Duraclear 111": {
        "Young's Modulus [Pa]": 1e8,
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.6e-4,
        "CTE_y [1/K]": 1.6e-4,
    },
    "Watom: 210": {
        "Young's Modulus [Pa]": 1e8,
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.6e-4,
        "CTE_y [1/K]": 1.6e-4,
    },
    "Watom: 211": {
        "Young's Modulus [Pa]": 1e8,
        "Poisson's Ratio": 0.48,
        "CTE_x [1/K]": 1.6e-4,
        "CTE_y [1/K]": 1.6e-4,
    },
    # Socrep TPU 511/519 — Shore A70 soft sidewall TPU.
    # Shore A70 ~ E_initial 3-8 MPa; using 5 MPa as central estimate.
    # Nearly incompressible (nu ~ 0.499); high CTE typical for soft elastomers.
    "Socrep: TPU 511/519": {
        "Young's Modulus [Pa]": 5e6,
        "Poisson's Ratio": 0.499,
        "CTE_x [1/K]": 1.8e-4,
        "CTE_y [1/K]": 1.8e-4,
    },

    # Glass fiber fleece / veil (random-fiber, low areal weight)
    # CTE isotropic estimate for random glass-resin veil
    "Nico Fiber":              {"CTE_x [1/K]": 2.5e-5, "CTE_y [1/K]": 2.5e-5},
    "XuRui: G50 Fleece Veil":  {"CTE_x [1/K]": 2.5e-5, "CTE_y [1/K]": 2.5e-5},
    "XuRui:G100 Fleece Veil":  {"CTE_x [1/K]": 2.5e-5, "CTE_y [1/K]": 2.5e-5},

    # PU injection foam (BASF Modipur, rho=600): add CTE
    # E=20.3 MPa kept as-is (may be compressive strength; flagged for review)
    "BASF: Modipur injection foam": {
        "CTE_x [1/K]": 7e-5,
        "CTE_y [1/K]": 7e-5,
    },

    # Binding mat Chomarat Volumat (glass veil, very low E)
    "Chomarat: Volumat: 300T/300": {
        "CTE_x [1/K]": 2.5e-5,
        "CTE_y [1/K]": 2.5e-5,
    },

    # -----------------------------------------------------------------------
    # Round 3 corrections — CTE for remaining laminates and structural materials
    # -----------------------------------------------------------------------

    # Hexcell precure laminates (note: different spelling from Hexcel — 2 L's)
    # E=40.68 GPa, density=1980 — consistent with woven glass/epoxy prepreg.
    # CTE: isotropic woven glass/epoxy estimate (balanced ±45 or 0/90 layup)
    "Hexcell: G-R80 Laminate":  {"CTE_x [1/K]": 8e-6, "CTE_y [1/K]": 8e-6},
    "Hexcell: G-EV R68":        {"CTE_x [1/K]": 8e-6, "CTE_y [1/K]": 8e-6},
    "Hexcell: G-EV 765R":       {"CTE_x [1/K]": 8e-6, "CTE_y [1/K]": 8e-6},
    "Hexcell: G-EV R82":        {"CTE_x [1/K]": 8e-6, "CTE_y [1/K]": 8e-6},

    # Hexcel G-EV 696R (binding mat precure, very low E=31.6 MPa — glass mat)
    "Hexcel: G-EV 696R": {"CTE_x [1/K]": 2e-5, "CTE_y [1/K]": 2e-5},

    # Braid materials (glass fiber)
    # E values (9.65 MPa and 40.68 MPa) may be effective braid stiffness or
    # placeholders — left as-is; CTE added for random/near-random glass fiber.
    "Glass - Braid":             {"CTE_x [1/K]": 2e-5, "CTE_y [1/K]": 2e-5},
    "Glass - Rovings in braid":  {"CTE_x [1/K]": 2e-5, "CTE_y [1/K]": 2e-5},

    # Freudenberg Enkamat 7210 (PU 3D net structure, negligible structural role)
    # E=1000 Pa — essentially a carrier layer, not a structural component.
    "Freudenberg: Enkamat 7210": {"CTE_x [1/K]": 8e-5, "CTE_y [1/K]": 8e-5},

    # HongTex UD glass — E=42.24 GPa plausible for 0-deg UD glass/epoxy
    # Densities (2290, 2955) appear anomalously high — likely data issues.
    # CTE: 0-deg UD glass/epoxy (longitudinal ≈ 6e-6, transverse ≈ 25e-6)
    "HongTex: 9 oz. Uni":  {"CTE_x [1/K]": 6e-6, "CTE_y [1/K]": 25e-6},
    "HongTex: 17 oz. Uni": {"CTE_x [1/K]": 6e-6, "CTE_y [1/K]": 25e-6},

    # Carbon Conversions re-Evo MCF-SB 200gsm (recycled/milled carbon fiber mat)
    # E=21.2 GPa plausible for random-oriented recycled CF mat at moderate Vf.
    # CTE: isotropic estimate for random CF mat
    "Carbon Conversions: re-Evo MCF-SB 200gsm": {
        "CTE_x [1/K]": 1e-5,
        "CTE_y [1/K]": 1e-5,
    },

    # -----------------------------------------------------------------------
    # Round 4 corrections
    # -----------------------------------------------------------------------

    # ITOCHU HS40 carbon rovings — used as 0-deg UD tow/winding reinforcement.
    # E1 assumed ~140 GPa at 60% Vf (HS fiber E_f ~ 230 GPa).
    # Q matrix is orthotropic (E2~9 GPa, G12~5 GPa, nu12=0.30); supplied
    # explicitly to prevent isotropic override in KaiTai CSV.
    # CTE: longitudinal (fiber dir) ~ -1e-6, transverse ~ 25e-6 K-1
    # Note: ITOCHU HS40 F22 (12K, 800 tex) and E13 (6K, 400 tex) differ only
    # in tow size, not fiber type — same modulus assumed for both.
    "ITOCHU: HS40 F22 12K 800 tex Carbon Roving": {
        "Young's Modulus [Pa]": 1.40e11,
        "Poisson's Ratio": 0.30,
        "CTE_x [1/K]": -1e-6,
        "CTE_y [1/K]": 25e-6,
        # Orthotropic Q for 0-deg UD (E1=140 GPa, E2=9 GPa, G12=5 GPa, nu12=0.30)
        "Q11 [Pa]": 1.408e11,
        "Q22 [Pa]": 9.051e9,
        "Q12 [Pa]": 2.715e9,
        "Q66 [Pa]": 5.0e9,
        "Q16 [Pa]": 0,
        "Q26 [Pa]": 0,
    },
    "ITOCHU: HS40 E13 6K 400 tex Carbon Roving": {
        "Young's Modulus [Pa]": 1.40e11,
        "Poisson's Ratio": 0.30,
        "CTE_x [1/K]": -1e-6,
        "CTE_y [1/K]": 25e-6,
        "Q11 [Pa]": 1.408e11,
        "Q22 [Pa]": 9.051e9,
        "Q12 [Pa]": 2.715e9,
        "Q66 [Pa]": 5.0e9,
        "Q16 [Pa]": 0,
        "Q26 [Pa]": 0,
    },

    # Rubber — Semperit Haberkorn (ski edge damp rubber)
    # Actual E ~ 1-6 MPa; placeholder was 344.7 MPa (50 ksi). Use 3 MPa.
    # nu ~ 0.499 (nearly incompressible); CTE ~ 2e-4 K-1 (rubber)
    "Semperit: Haberkorn Rubber": {
        "Young's Modulus [Pa]": 3e6,
        "Poisson's Ratio": 0.499,
        "CTE_x [1/K]": 2e-4,
        "CTE_y [1/K]": 2e-4,
    },

    # Urethane — Argotec (structural urethane interlayer / damping film)
    # Range is wide; using 30 MPa as mid-range estimate for structural urethane.
    # nu ~ 0.45; CTE ~ 1.5e-4 K-1
    "Argotec: Urethane": {
        "Young's Modulus [Pa]": 3e7,
        "Poisson's Ratio": 0.45,
        "CTE_x [1/K]": 1.5e-4,
        "CTE_y [1/K]": 1.5e-4,
    },

    # CPS TP455 — high-density foamed PE (density confirmed 750 kg/m^3)
    # E=750 MPa kept (user estimate); nu updated to 0.40 (PE foam); CTE=1.5e-4
    "CPS: TP455": {
        "Poisson's Ratio": 0.40,
        "CTE_x [1/K]": 1.5e-4,
        "CTE_y [1/K]": 1.5e-4,
    },

    # BComp Amplitex carbon/flax hybrid (balanced woven, 52mm tape)
    # E=48.26 GPa already set; CTE estimate for balanced C/flax woven
    "BComp: Amplitex 115gsm-C 125gsm-F - 52mm": {
        "CTE_x [1/K]": 2e-6,
        "CTE_y [1/K]": 2e-6,
    },
}


def _apply_to_row(row: dict, corr: dict, has_Q_cols: bool) -> None:
    """Mutate row in place; recompute Q (isotropic) if columns exist.

    If the corrections dict already supplies Q11 [Pa] explicitly (orthotropic
    materials), skip the isotropic Q recompute and use those values directly.
    """
    for field, val in corr.items():
        row[field] = _fmt(val)

    if has_Q_cols and "Q11 [Pa]" in row:
        # If Q values were set explicitly in corrections, skip recompute
        if "Q11 [Pa]" in corr:
            return
        try:
            E  = float(row["Young's Modulus [Pa]"])
            nu = float(row["Poisson's Ratio"])
        except (ValueError, KeyError):
            return
        Q = _isotropic_Q(E, nu)
        row["Q11 [Pa]"] = _fmt(Q["Q11"])
        row["Q22 [Pa]"] = _fmt(Q["Q22"])
        row["Q12 [Pa]"] = _fmt(Q["Q12"])
        row["Q66 [Pa]"] = _fmt(Q["Q66"])
        row["Q16 [Pa]"] = _fmt(Q["Q16"])
        row["Q26 [Pa]"] = _fmt(Q["Q26"])


def _update_csv(path: Path, corrections: dict, has_Q: bool,
                exclude_cols: tuple = ()) -> list:
    with open(path, encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        fieldnames = [c for c in reader.fieldnames if c not in exclude_cols]
        rows = [dict(r) for r in reader]

    changed = []
    for row in rows:
        name = row.get("Name", "").strip()
        if name in corrections:
            _apply_to_row(row, corrections[name], has_Q_cols=has_Q)
            changed.append(name)

        # Zero out Q16/Q26 floating-point noise (KaiTai CSV only).
        # Applies to every row so CLT residuals (~1e-9 Pa) are suppressed.
        # Values >= _Q_NOISE_THRESHOLD (e.g. the 4.68e8 Pa coupling term in
        # C-PLY SP TBX270) are preserved unchanged.
        if has_Q:
            for col in ("Q16 [Pa]", "Q26 [Pa]"):
                val_str = row.get(col, "")
                if val_str:
                    try:
                        if abs(float(val_str)) < _Q_NOISE_THRESHOLD:
                            row[col] = "0"
                    except ValueError:
                        pass

    with open(path, "w", encoding="utf-8", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(_strip_commas(row) for row in rows)

    return changed


def main():
    n = len(CORRECTIONS)
    print(f"Applying corrections for {n} materials\n")

    print("-- KaiTai CSV (with Q recompute) --")
    changed_k = _update_csv(KAITAI_CSV, CORRECTIONS, has_Q=True)
    for name in changed_k:
        print(f"  [KaiTai] {name}")
    print(f"\n  -> {len(changed_k)} rows updated")

    print("\n-- EXPORT CSV --")
    changed_e = _update_csv(EXPORT_CSV, CORRECTIONS, has_Q=False,
                            exclude_cols=("CTE_x [1/K]", "CTE_y [1/K]"))
    for name in changed_e:
        print(f"  [EXPORT] {name}")
    print(f"\n  -> {len(changed_e)} rows updated")

    # Report any corrections that were not found in either CSV
    not_found = set(CORRECTIONS) - set(changed_k) - set(changed_e)
    if not_found:
        print("\nWARNING - these names were not found in either CSV:")
        for name in sorted(not_found):
            print(f"  {name}")

    print("\nDone.")
    print("\nMaterials NOT yet corrected (flagged for further research):")
    pending = [
        "Watom: Duraclear 117/111/210/211    (PVC or PU top sheet — need grade confirmation)",
        "Isosport: ICP 5275/5278             (top sheet material — need type)",
        "Isosport: TPU IE 3200 SM 2EV 031   (need hardness / Shore D)",
        "Geba: TPU 3918/3919                 (need hardness / Shore D)",
        "BASF: E1160D50 / 1165D50STR / 1154D50 TPU  (Shore D50; E ~300 MPa pending confirm)",
        "Desmovit: DP R 3918                 (density 970 seems low for solid TPU)",
        "Socrep: TPU 511/519                 (E=20 MPa; nu=0.33 should probably be 0.47)",
        "Semperit: Haberkorn Rubber          (flagged for research)",
        "Argotec: Urethane                   (flagged for research)",
        "CPS: TP455                          (density 750 — foam or low-fill plastic?)",
        "BASF: Modipur injection foam        (E=20 MPa at rho=600 may be strength, not modulus)",
        "Nico Fiber / XuRui Fleece           (glass veil — E values look plausible, CTE needed)",
        "Glass - Braid / Glass Rovings in braid  (effective laminate values unclear)",
        "Hexcell precure laminates (G-R80, G-EV R68/765R/R82)  (no JSON match; CLT data needed)",
        "Carbon Conversions: re-Evo MCF-SB 200gsm  (no CLT data)",
        "ITOCHU: HS40 carbon rovings         (E=68.95 GPa seems low for HS40 CF tow)",
        "BComp: Amplitex 115gsm carbon/flax  (hybrid; CLT calc needed)",
        "Freudenberg: Enkamat 7210           (structural contribution negligible — leave as-is)",
        "Hexcel: G-EV 696R / Chomarat: Volumat  (binding mat — structural role is minimal)",
    ]
    for item in pending:
        print(f"  - {item}")


if __name__ == "__main__":
    main()
