"""
pytest test suite for clt.py — Classical Laminate Theory module.

Structure
---------
Unit tests (TestPlyQ, TestRotateQ, TestLaminate)
    Verify mathematical correctness of the CLT primitives.
    These are expected to PASS unconditionally.

Integration tests (test_q11, test_q22, test_q12, test_q66, test_ex, test_nu_xy,
                   test_q16, test_density)
    Compare compute_laminate() output against values stored in
    KaiTai_ski_snowboard_material_library.csv for the 47 matched records
    in name_map.csv (match_type: normalized | confirmed).

    EXPECTED OUTCOME:
    -----------------
    The CSV stores Q-matrix values with the column header "[Pa]".
    CLT computed from JSON component moduli (E1, E2, G12 in MPa) yields
    results in MPa — approximately 1000× larger than the CSV numbers when
    the CSV Pa values are divided by 1e6.

    This reveals a known unit discrepancy: the CSV Q11 ≈ 36 MPa (stored as
    ~36,000,000 Pa) while CLT gives ~35,900 MPa for the same material.
    Factor ≈ 1000.  Both diagnoses are possible:
      (a) The spreadsheet used kPa-scale moduli internally while the JSON
          extracted the raw MPa values, or
      (b) The CSV labels are wrong (should be kPa, not Pa).

    The Q12 sign: CLT gives Q12 > 0 for positive Poisson materials; the CSV
    stores Q12 as a positive number too — no sign flip expected.

    nu_xy and density carry no Pa/MPa ambiguity; tests on those values are
    flagged separately to distinguish unit errors from model errors.

Run commands
------------
    cd materialData/

    # All tests, verbose
    python -m pytest test_clt.py -v

    # Unit tests only (should all pass)
    python -m pytest test_clt.py -v -k "TestPlyQ or TestRotateQ or TestLaminate"

    # Integration tests — surface the unit discrepancy
    python -m pytest test_clt.py -v -k "test_q11 or test_q22 or test_ex"
"""

import csv
import json
import math
from pathlib import Path

import numpy as np
import pytest

from clt import compute_laminate, ply_Q, rand_Q, rotate_Q

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

DATA_DIR = Path(__file__).parent
ORTHO_JSON = DATA_DIR / "orthotropicMaterials.json"
CSV_FILE   = DATA_DIR / "KaiTai_ski_snowboard_material_library.csv"
NAME_MAP   = DATA_DIR / "name_map.csv"

# Relative tolerance used in integration comparisons (5 %)
REL_TOL = 0.05


# ---------------------------------------------------------------------------
# Module-level data loading for parametrize
# (loaded once at collection time; empty list → no integration tests collected)
# ---------------------------------------------------------------------------

def _load_ortho_dict():
    if not ORTHO_JSON.exists():
        return {}
    with open(ORTHO_JSON, encoding="utf-8") as f:
        records = json.load(f)
    return {r["combined_name"]: r for r in records}


def _load_csv_dict():
    if not CSV_FILE.exists():
        return {}
    result = {}
    with open(CSV_FILE, encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            result[row["Name"]] = row
    return result


def _load_matched_pairs():
    """
    Return list of (csv_name, record, csv_row) for matched materials.

    Filters name_map.csv to match_type in {normalized, confirmed} and
    requires both sides of the match to be present in their respective files.
    """
    if not NAME_MAP.exists():
        return []
    ortho = _load_ortho_dict()
    csv_d = _load_csv_dict()
    pairs = []
    with open(NAME_MAP, encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row["match_type"] not in ("normalized", "confirmed"):
                continue
            csv_name = row["csv_name"]
            json_key = row["json_key"]
            if csv_name in csv_d and json_key in ortho:
                pairs.append((csv_name, ortho[json_key], csv_d[csv_name]))
    return pairs


MATCHED_PAIRS = _load_matched_pairs()
_PAIR_IDS = [p[0] for p in MATCHED_PAIRS]


# ---------------------------------------------------------------------------
# Session-level fixtures (for non-parametrized tests or direct access)
# ---------------------------------------------------------------------------

@pytest.fixture(scope="session")
def ortho_dict():
    return _load_ortho_dict()


@pytest.fixture(scope="session")
def csv_dict():
    return _load_csv_dict()


@pytest.fixture(scope="session")
def matched():
    return MATCHED_PAIRS


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _rel_diff(a: float, b: float) -> float:
    """Relative difference |a-b| / max(|b|, 1e-10)."""
    return abs(a - b) / max(abs(b), 1e-10)


def _csv_float(row: dict, col: str) -> float:
    return float(row[col])


# ===========================================================================
# UNIT TESTS — mathematical correctness of CLT primitives
# ===========================================================================

class TestPlyQ:
    """ply_Q: reduced stiffness matrix from engineering constants."""

    def test_isotropic_limit(self):
        """When E1=E2=E and G12=E/(2(1+v)), Q is fully isotropic."""
        E, v = 70_000.0, 0.30
        G = E / (2.0 * (1.0 + v))
        Q = ply_Q(E, E, G, v)
        expected_Q11 = E / (1.0 - v ** 2)
        expected_Q12 = v * E / (1.0 - v ** 2)
        expected_Q66 = G
        assert math.isclose(Q[0, 0], expected_Q11, rel_tol=1e-10), (
            f"Q11={Q[0,0]:.4f}  expected={expected_Q11:.4f}"
        )
        assert math.isclose(Q[1, 1], expected_Q11, rel_tol=1e-10), "Q22 != Q11 for isotropic"
        assert math.isclose(Q[0, 1], expected_Q12, rel_tol=1e-10), (
            f"Q12={Q[0,1]:.4f}  expected={expected_Q12:.4f}"
        )
        assert math.isclose(Q[2, 2], expected_Q66, rel_tol=1e-10), (
            f"Q66={Q[2,2]:.4f}  expected={expected_Q66:.4f}"
        )
        assert Q[0, 2] == 0.0 and Q[1, 2] == 0.0, "Off-diagonal shear should be zero"

    def test_known_values(self):
        """Spot-check against hand-computed values for a typical glass UD ply."""
        E1, E2, G12, v12 = 50_000.0, 8_000.0, 3_000.0, 0.30
        v21 = v12 * E2 / E1              # = 0.048
        denom = 1.0 - v12 * v21          # = 1 - 0.3*0.048 = 0.9856
        expected = {
            "Q11": E1 / denom,
            "Q22": E2 / denom,
            "Q12": v12 * E2 / denom,
            "Q66": G12,
        }
        Q = ply_Q(E1, E2, G12, v12)
        for key, (i, j) in [("Q11", (0, 0)), ("Q22", (1, 1)),
                              ("Q12", (0, 1)), ("Q66", (2, 2))]:
            assert math.isclose(Q[i, j], expected[key], rel_tol=1e-10), (
                f"{key}: computed={Q[i,j]:.4f}  expected={expected[key]:.4f}"
            )

    def test_symmetry(self):
        """Q matrix must be symmetric."""
        Q = ply_Q(50_000, 8_000, 3_000, 0.30)
        np.testing.assert_array_equal(Q, Q.T)

    def test_positive_definite(self):
        """Q must have all positive eigenvalues (positive definiteness)."""
        Q = ply_Q(50_000, 8_000, 3_000, 0.30)
        eigs = np.linalg.eigvalsh(Q)
        assert np.all(eigs > 0), f"Negative eigenvalue(s): {eigs}"


class TestRotateQ:
    """rotate_Q: standard CLT Q̄ transformation."""

    @pytest.fixture
    def sample_Q(self):
        return ply_Q(50_000, 8_000, 3_000, 0.30)

    def test_zero_angle_identity(self, sample_Q):
        """rotate_Q(Q, 0°) should return Q unchanged (machine precision)."""
        Qbar = rotate_Q(sample_Q, 0.0)
        np.testing.assert_allclose(Qbar, sample_Q, rtol=1e-12,
                                   err_msg="rotate_Q at 0° not identity")

    def test_90deg_swaps_Q11_Q22(self, sample_Q):
        """At 90°, Q̄11↔Q22 and Q̄22↔Q11; Q̄12 and Q̄66 unchanged."""
        Qbar = rotate_Q(sample_Q, 90.0)
        assert math.isclose(Qbar[0, 0], sample_Q[1, 1], rel_tol=1e-10), (
            f"Q̄11(90°)={Qbar[0,0]:.4f} != Q22={sample_Q[1,1]:.4f}"
        )
        assert math.isclose(Qbar[1, 1], sample_Q[0, 0], rel_tol=1e-10), (
            f"Q̄22(90°)={Qbar[1,1]:.4f} != Q11={sample_Q[0,0]:.4f}"
        )
        assert math.isclose(Qbar[0, 1], sample_Q[0, 1], rel_tol=1e-10), "Q12 changed at 90°"
        assert math.isclose(Qbar[2, 2], sample_Q[2, 2], rel_tol=1e-10), "Q66 changed at 90°"

    def test_90deg_no_shear_coupling(self, sample_Q):
        """At 90°, Q̄16 and Q̄26 must be zero."""
        Qbar = rotate_Q(sample_Q, 90.0)
        assert abs(Qbar[0, 2]) < 1e-8, f"Q̄16 at 90° = {Qbar[0,2]}"
        assert abs(Qbar[1, 2]) < 1e-8, f"Q̄26 at 90° = {Qbar[1,2]}"

    def test_180deg_returns_to_original(self, sample_Q):
        """At 180°, Q̄ must equal Q (180° rotation is a symmetry of Q).

        atol=1e-8 handles floating-point noise in the Q16/Q26 entries
        which land at ~1e-12 (compared to an exact 0 desired value).
        """
        Qbar = rotate_Q(sample_Q, 180.0)
        np.testing.assert_allclose(Qbar, sample_Q, rtol=1e-10, atol=1e-8,
                                   err_msg="rotate_Q at 180° not identity")

    def test_pm45_balanced_Q16_cancels(self, sample_Q):
        """For a balanced ±45 pair, Q̄16(+45) + Q̄16(−45) = 0."""
        Q16_pos = rotate_Q(sample_Q, +45.0)[0, 2]
        Q16_neg = rotate_Q(sample_Q, -45.0)[0, 2]
        assert abs(Q16_pos + Q16_neg) < 1e-8, (
            f"Q̄16(+45)={Q16_pos:.4f}  Q̄16(-45)={Q16_neg:.4f}  sum={Q16_pos+Q16_neg:.2e}"
        )

    def test_symmetry_preserved(self, sample_Q):
        """Rotated Q̄ must remain symmetric."""
        for angle in [15, 30, 45, 60, 75]:
            Qbar = rotate_Q(sample_Q, angle)
            np.testing.assert_allclose(Qbar, Qbar.T, atol=1e-10,
                                       err_msg=f"Asymmetry at {angle}°")

    def test_energy_invariant(self, sample_Q):
        """
        The double-contraction Q:Q (Frobenius norm) is NOT generally invariant
        under rotation, but the trace of Q*inv(Q)=3 (identity) must hold.
        We verify that Q̄ remains invertible and physically meaningful.
        """
        Qbar = rotate_Q(sample_Q, 37.0)
        eigs = np.linalg.eigvalsh(Qbar)
        assert np.all(eigs > 0), f"Rotated Q lost positive definiteness: {eigs}"


class TestLaminate:
    """compute_laminate: integration over ply stack."""

    def _single_ply_record(self, E1=50_000, E2=8_000, G12=3_000, v12=0.30,
                            orientation=0, gsm=300, thickness_mm=1.0):
        """Helper to build a minimal single-ply record dict."""
        return {
            "fiber_weight_gsm": gsm,
            "thickness_mm": thickness_mm,
            "components": [{
                "gsm": gsm,
                "E1": E1,
                "E2": E2,
                "G12": G12,
                "v12": v12,
                "orientation": orientation,
            }],
        }

    def test_single_ud_ply_passthrough(self):
        """
        A laminate with one 0° ply should yield Q* = Q_ply (within float eps).
        Thickness fraction = 1.0, so A = Q*h, Q* = A/h = Q.
        """
        E1, E2, G12, v12 = 50_000.0, 8_000.0, 3_000.0, 0.30
        rec = self._single_ply_record(E1=E1, E2=E2, G12=G12, v12=v12,
                                      orientation=0, gsm=300, thickness_mm=2.0)
        result = compute_laminate(rec, theta_deg=0)
        Q_expected = ply_Q(E1, E2, G12, v12)
        assert math.isclose(result["Q11"], Q_expected[0, 0], rel_tol=1e-10), (
            f"Q11: {result['Q11']:.4f} vs {Q_expected[0,0]:.4f}"
        )
        assert math.isclose(result["Q22"], Q_expected[1, 1], rel_tol=1e-10)
        assert math.isclose(result["Q12"], Q_expected[0, 1], rel_tol=1e-10)
        assert math.isclose(result["Q66"], Q_expected[2, 2], rel_tol=1e-10)
        assert abs(result["Q16"]) < 1e-8, f"Q16 nonzero for 0° ply: {result['Q16']}"
        assert abs(result["Q26"]) < 1e-8, f"Q26 nonzero for 0° ply: {result['Q26']}"

    def test_thickness_is_conserved(self):
        """Sum of ply thicknesses must equal record['thickness_mm']."""
        rec = {
            "fiber_weight_gsm": 700,
            "thickness_mm": 0.85,
            "components": [
                {"gsm": 400, "E1": 47000, "E2": 8500, "G12": 3200, "v12": 0.28, "orientation": 0},
                {"gsm": 150, "E1": 47000, "E2": 8500, "G12": 3200, "v12": 0.28, "orientation": 45},
                {"gsm": 150, "E1": 47000, "E2": 8500, "G12": 3200, "v12": 0.28, "orientation": -45},
            ],
        }
        result = compute_laminate(rec)
        assert math.isclose(result["h_mm"], 0.85, rel_tol=1e-10), (
            f"h_mm={result['h_mm']:.6f}  expected=0.85"
        )

    def test_balanced_laminate_no_shear_coupling(self):
        """A symmetric ±45 balanced laminate must give Q*16 = Q*26 ≈ 0."""
        rec = {
            "fiber_weight_gsm": 300,
            "thickness_mm": 1.0,
            "components": [
                {"gsm": 150, "E1": 47000, "E2": 8500, "G12": 3200, "v12": 0.28, "orientation": 45},
                {"gsm": 150, "E1": 47000, "E2": 8500, "G12": 3200, "v12": 0.28, "orientation": -45},
            ],
        }
        result = compute_laminate(rec)
        assert abs(result["Q16"]) < 1e-6, f"Q*16={result['Q16']:.2e} for ±45 laminate"
        assert abs(result["Q26"]) < 1e-6, f"Q*26={result['Q26']:.2e} for ±45 laminate"

    def test_quasi_isotropic_approx(self):
        """
        A 0°/±45°/90° quasi-isotropic laminate should have Q*11 ≈ Q*22 and Q*16 ≈ 0.
        Exact QI condition: Q11=Q22, Q12+2*Q66=Q11/2 ... we test approximate equality.
        """
        E1, E2, G12, v12 = 140_000.0, 10_000.0, 5_000.0, 0.30
        gsm_each = 200
        rec = {
            "fiber_weight_gsm": 4 * gsm_each,
            "thickness_mm": 1.0,
            "components": [
                {"gsm": gsm_each, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": 0},
                {"gsm": gsm_each, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": 45},
                {"gsm": gsm_each, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": -45},
                {"gsm": gsm_each, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": 90},
            ],
        }
        result = compute_laminate(rec)
        # Q11 and Q22 should be equal (quasi-isotropic symmetry)
        assert math.isclose(result["Q11"], result["Q22"], rel_tol=1e-6), (
            f"Q*11={result['Q11']:.2f}  Q*22={result['Q22']:.2f}"
        )
        assert abs(result["Q16"]) < 1.0, f"Q*16={result['Q16']:.4f}  expected ≈0"

    def test_rand_ply_gives_isotropic_Q(self):
        """A RAND-only laminate should yield Q*11 = Q*22 and Q*16 = 0."""
        rec = {
            "fiber_weight_gsm": 100,
            "thickness_mm": 0.5,
            "components": [
                {"gsm": 100, "E1": 7500, "E2": 7500, "G12": 500, "v12": 0.28, "orientation": "RAND"},
            ],
        }
        result = compute_laminate(rec)
        assert math.isclose(result["Q11"], result["Q22"], rel_tol=1e-10), (
            f"RAND Q*11={result['Q11']:.4f}  Q*22={result['Q22']:.4f}"
        )
        assert abs(result["Q16"]) < 1e-8
        assert abs(result["Q26"]) < 1e-8

    def test_b_matrix_zero_for_symmetric_layup(self):
        """
        For a symmetric layup (ply k mirrored by ply n-k+1 with same properties),
        the B coupling matrix must be ≈ 0.
        """
        E1, E2, G12, v12 = 50_000.0, 8_000.0, 3_000.0, 0.30
        gsm = 200
        rec = {
            "fiber_weight_gsm": 3 * gsm,
            "thickness_mm": 1.0,
            "components": [
                {"gsm": gsm, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": 0},
                {"gsm": gsm, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": 90},
                {"gsm": gsm, "E1": E1, "E2": E2, "G12": G12, "v12": v12, "orientation": 0},
            ],
        }
        result = compute_laminate(rec)
        np.testing.assert_allclose(result["B"], 0.0, atol=1e-6,
                                   err_msg="B not zero for symmetric layup")

    def test_engineering_constants_self_consistent(self):
        """
        Ex, Ey, Gxy derived from Q* must satisfy round-trip:
        Q*[0,0] = Ex / (1 - nu_xy * nu_yx) in isotropic case.
        Here we check Ex > 0, Ey > 0, Gxy > 0, and 0 < |nu_xy| < 1.
        """
        rec = {
            "fiber_weight_gsm": 500,
            "thickness_mm": 0.8,
            "components": [
                {"gsm": 300, "E1": 50000, "E2": 8000, "G12": 3000, "v12": 0.30, "orientation": 0},
                {"gsm": 100, "E1": 50000, "E2": 8000, "G12": 3000, "v12": 0.30, "orientation": 45},
                {"gsm": 100, "E1": 50000, "E2": 8000, "G12": 3000, "v12": 0.30, "orientation": -45},
            ],
        }
        r = compute_laminate(rec)
        assert r["Ex"] > 0,  f"Ex={r['Ex']:.2f} <= 0"
        assert r["Ey"] > 0,  f"Ey={r['Ey']:.2f} <= 0"
        assert r["Gxy"] > 0, f"Gxy={r['Gxy']:.2f} <= 0"
        assert abs(r["nu_xy"]) < 1.0, f"|nu_xy|={abs(r['nu_xy']):.4f} >= 1"


# ===========================================================================
# INTEGRATION TESTS — compare against CSV values
#
# EXPECTED: Q11/Q22/Q12/Q66 tests will FAIL with a ratio ≈ 1000.
#   Computed values (MPa) vs CSV values (Pa/1e6 = MPa-equivalent):
#   factor ~1000 reveals the unit discrepancy between JSON component
#   moduli (MPa) and CSV Q-matrix values (which appear to be in kPa-scale).
#
# EXPECTED: nu_xy and density tests may pass or show a different discrepancy
#   (no Pa/MPa conversion involved, so any mismatch is a model difference).
# ===========================================================================

def _integration_skip(pairs):
    if not pairs:
        return pytest.mark.skip(reason=(
            "No matched pairs found.  Ensure orthotropicMaterials.json, "
            "KaiTai_ski_snowboard_material_library.csv, and name_map.csv "
            "are in the same directory as test_clt.py."
        ))
    return pytest.mark.parametrize("csv_name,record,csv_row", pairs,
                                   ids=[p[0] for p in pairs])


_integ = _integration_skip(MATCHED_PAIRS)


@_integ
def test_q11(csv_name, record, csv_row):
    """Q11 computed (MPa) vs CSV Q11 (Pa converted to MPa)."""
    result = compute_laminate(record)
    computed = result["Q11"]
    csv_mpa = _csv_float(csv_row, "Q11 [Pa]") / 1e6
    ratio = computed / csv_mpa if abs(csv_mpa) > 1e-12 else float("inf")
    assert _rel_diff(computed, csv_mpa) < REL_TOL, (
        f"\n  material : {csv_name}"
        f"\n  COMPUTED Q11 = {computed:.1f} MPa"
        f"\n  CSV Q11      = {csv_mpa:.6f} MPa  (raw Pa: {csv_mpa*1e6:.2f})"
        f"\n  ratio        = {ratio:.0f}  ← expected ~1000 if unit discrepancy"
    )


@_integ
def test_q22(csv_name, record, csv_row):
    """Q22 computed (MPa) vs CSV Q22 (Pa converted to MPa)."""
    result = compute_laminate(record)
    computed = result["Q22"]
    csv_mpa = _csv_float(csv_row, "Q22 [Pa]") / 1e6
    ratio = computed / csv_mpa if abs(csv_mpa) > 1e-12 else float("inf")
    assert _rel_diff(computed, csv_mpa) < REL_TOL, (
        f"\n  material : {csv_name}"
        f"\n  COMPUTED Q22 = {computed:.1f} MPa"
        f"\n  CSV Q22      = {csv_mpa:.6f} MPa  (raw Pa: {csv_mpa*1e6:.2f})"
        f"\n  ratio        = {ratio:.0f}"
    )


@_integ
def test_q12(csv_name, record, csv_row):
    """Q12 computed (MPa) vs CSV Q12 (Pa converted to MPa)."""
    result = compute_laminate(record)
    computed = result["Q12"]
    csv_mpa = _csv_float(csv_row, "Q12 [Pa]") / 1e6
    ratio = computed / csv_mpa if abs(csv_mpa) > 1e-12 else float("inf")
    assert _rel_diff(computed, csv_mpa) < REL_TOL, (
        f"\n  material : {csv_name}"
        f"\n  COMPUTED Q12 = {computed:.1f} MPa"
        f"\n  CSV Q12      = {csv_mpa:.6f} MPa  (raw Pa: {csv_mpa*1e6:.2f})"
        f"\n  ratio        = {ratio:.0f}"
    )


@_integ
def test_q66(csv_name, record, csv_row):
    """Q66 computed (MPa) vs CSV Q66 (Pa converted to MPa)."""
    result = compute_laminate(record)
    computed = result["Q66"]
    csv_mpa = _csv_float(csv_row, "Q66 [Pa]") / 1e6
    ratio = computed / csv_mpa if abs(csv_mpa) > 1e-12 else float("inf")
    assert _rel_diff(computed, csv_mpa) < REL_TOL, (
        f"\n  material : {csv_name}"
        f"\n  COMPUTED Q66 = {computed:.1f} MPa"
        f"\n  CSV Q66      = {csv_mpa:.6f} MPa  (raw Pa: {csv_mpa*1e6:.2f})"
        f"\n  ratio        = {ratio:.0f}"
    )


@_integ
def test_ex(csv_name, record, csv_row):
    """Ex computed (MPa) vs CSV Young's Modulus (Pa converted to MPa)."""
    result = compute_laminate(record)
    computed = result["Ex"]
    csv_mpa = _csv_float(csv_row, "Young's Modulus [Pa]") / 1e6
    ratio = computed / csv_mpa if abs(csv_mpa) > 1e-12 else float("inf")
    assert _rel_diff(computed, csv_mpa) < REL_TOL, (
        f"\n  material : {csv_name}"
        f"\n  COMPUTED Ex  = {computed:.1f} MPa"
        f"\n  CSV E        = {csv_mpa:.6f} MPa  (raw Pa: {csv_mpa*1e6:.2f})"
        f"\n  ratio        = {ratio:.0f}"
    )


@_integ
def test_nu_xy(csv_name, record, csv_row):
    """
    nu_xy is dimensionless — no unit conversion.

    If this test fails the error is a model difference (ply stack vs
    CSV Poisson's ratio definition), NOT a unit discrepancy.
    """
    result = compute_laminate(record)
    computed = result["nu_xy"]
    csv_nu = _csv_float(csv_row, "Poisson's Ratio")
    assert _rel_diff(computed, csv_nu) < REL_TOL, (
        f"\n  material    : {csv_name}"
        f"\n  COMPUTED nu = {computed:.6f}"
        f"\n  CSV nu      = {csv_nu:.6f}"
        f"\n  NOTE: mismatch here is a model difference, not a unit error"
    )


@_integ
def test_density(csv_name, record, csv_row):
    """
    Density [kg/m³] is a passthrough — no CLT computation needed.

    Checks that record['density'] is consistent with CSV Density.
    A mismatch here indicates a data extraction error rather than a CLT issue.
    """
    computed = float(record["density"])
    csv_rho = _csv_float(csv_row, "Density [kg/m^3]")
    assert _rel_diff(computed, csv_rho) < REL_TOL, (
        f"\n  material        : {csv_name}"
        f"\n  JSON density    = {computed:.2f} kg/m³"
        f"\n  CSV density     = {csv_rho:.2f} kg/m³"
    )


def _has_significant_q16(csv_row, threshold_pa=1000.0):
    """Return True if CSV Q16 or Q26 is meaningfully non-zero."""
    q16 = abs(_csv_float(csv_row, "Q16 [Pa]"))
    q26 = abs(_csv_float(csv_row, "Q26 [Pa]"))
    return q16 > threshold_pa or q26 > threshold_pa


# Q16/Q26 integration test — only runs for materials with non-trivial values
_q16_pairs = [(n, r, c) for n, r, c in MATCHED_PAIRS if _has_significant_q16(c)]
_q16_integ = _integration_skip(_q16_pairs) if _q16_pairs else pytest.mark.skip(
    reason="No materials with significant Q16 in matched set"
)


@_q16_integ
def test_q16(csv_name, record, csv_row):
    """
    Q16 for off-axis / unbalanced materials (Pa → MPa).

    Materials with non-zero Q16 in the CSV include some carbon biaxial
    and speciality laminates.  Most symmetric/balanced laminates give Q16≈0.
    """
    result = compute_laminate(record)
    computed = result["Q16"]
    csv_mpa = _csv_float(csv_row, "Q16 [Pa]") / 1e6
    ratio = (computed / csv_mpa) if abs(csv_mpa) > 1e-12 else float("inf")
    assert _rel_diff(computed, csv_mpa) < REL_TOL, (
        f"\n  material : {csv_name}"
        f"\n  COMPUTED Q16 = {computed:.4f} MPa"
        f"\n  CSV Q16      = {csv_mpa:.9f} MPa  (raw Pa: {csv_mpa*1e6:.4f})"
        f"\n  ratio        = {ratio:.0f}"
    )
