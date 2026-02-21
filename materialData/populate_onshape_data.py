"""
populate_onshape_data.py

Updates both Onshape CSV files with CLT-computed values for the 47 materials
that are matched in name_map.csv (match_type: normalized | confirmed).

Changes made to matched materials
----------------------------------
  Density [kg/m^3]   ← record['density'] from orthotropicMaterials.json
  Poisson's Ratio    ← CLT nu_xy  (compliance-inversion, dimensionless)
  Young's Modulus    ← CLT Ex [MPa] × 1e6  → Pa
  Q11/Q22/Q12/Q66    ← CLT Q* [MPa] × 1e6  → Pa
  Q16/Q26            ← CLT Q* [MPa] × 1e6  → Pa  (non-zero for unbalanced)

New columns added to BOTH files
---------------------------------
  CTE_x [1/K]        ← effective laminate CTE in 0° direction
  CTE_y [1/K]        ← effective laminate CTE in 90° direction
  (inserted before 'Available dimensions' in the KaiTai CSV; appended in EXPORT)

Unmatched materials
-------------------
  All numeric fields are left unchanged; CTE columns are left empty ('').

Usage
-----
    cd materialData/
    python populate_onshape_data.py
"""

import csv
import json
import sys
from pathlib import Path

from clt import compute_laminate

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

DATA_DIR   = Path(__file__).parent
ORTHO_JSON = DATA_DIR / "orthotropicMaterials.json"
NAME_MAP   = DATA_DIR / "name_map.csv"
KAITAI_CSV = DATA_DIR / "KaiTai_ski_snowboard_material_library.csv"
EXPORT_CSV = DATA_DIR / "Kaitai_ski_snowboard_material_library_EXPORT.csv"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _fmt(x) -> str:
    """Format a number for CSV output (6 significant figures)."""
    if x is None:
        return ""
    f = float(x)
    if f == 0.0:
        return "0"
    # Use scientific notation for very small (CTE) or very large (Pa Q values)
    return f"{f:.6g}"


def _load_ortho() -> dict:
    with open(ORTHO_JSON, encoding="utf-8") as fh:
        records = json.load(fh)
    return {r["combined_name"]: r for r in records}


def _load_name_map() -> dict:
    """Return {csv_name: json_key} for matched entries only."""
    mapping = {}
    with open(NAME_MAP, encoding="utf-8-sig", newline="") as fh:
        for row in csv.DictReader(fh):
            if row["match_type"] in ("normalized", "confirmed"):
                mapping[row["csv_name"]] = row["json_key"]
    return mapping


def _compute_values(record: dict) -> dict:
    """Run CLT on one record; return dict of corrected CSV values."""
    r = compute_laminate(record, theta_deg=0)
    return {
        "density":  _fmt(record["density"]),
        "nu_xy":    _fmt(r["nu_xy"]),
        "Ex_Pa":    _fmt(r["Ex"]  * 1e6),
        "Q11_Pa":   _fmt(r["Q11"] * 1e6),
        "Q22_Pa":   _fmt(r["Q22"] * 1e6),
        "Q12_Pa":   _fmt(r["Q12"] * 1e6),
        "Q66_Pa":   _fmt(r["Q66"] * 1e6),
        "Q16_Pa":   _fmt(r["Q16"] * 1e6),
        "Q26_Pa":   _fmt(r["Q26"] * 1e6),
        "alpha_x":  _fmt(r["alpha_x"]),
        "alpha_y":  _fmt(r["alpha_y"]),
    }


# ---------------------------------------------------------------------------
# KaiTai CSV  (12 columns + 2 new CTE columns)
# ---------------------------------------------------------------------------

def update_kaitai(mapping: dict, ortho: dict) -> int:
    # --- read ---
    with open(KAITAI_CSV, encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        orig_fields = list(reader.fieldnames)
        rows = [dict(r) for r in reader]

    # Guard against running the script twice (don't add CTE columns again)
    cte_cols = ["CTE_x [1/K]", "CTE_y [1/K]"]
    avail_col = "Available dimensions"

    if cte_cols[0] in orig_fields:
        new_fields = orig_fields                        # already there
    elif avail_col in orig_fields:
        idx = orig_fields.index(avail_col)
        new_fields = orig_fields[:idx] + cte_cols + orig_fields[idx:]
    else:
        new_fields = orig_fields + cte_cols

    # --- update rows ---
    updated = 0
    for row in rows:
        name = row.get("Name", "").strip()
        if not name:
            row.setdefault("CTE_x [1/K]", "")
            row.setdefault("CTE_y [1/K]", "")
            continue

        if name in mapping and mapping[name] in ortho:
            v = _compute_values(ortho[mapping[name]])
            row["Density [kg/m^3]"]      = v["density"]
            row["Poisson's Ratio"]        = v["nu_xy"]
            row["Young's Modulus [Pa]"]   = v["Ex_Pa"]
            row["Q11 [Pa]"]               = v["Q11_Pa"]
            row["Q22 [Pa]"]               = v["Q22_Pa"]
            row["Q12 [Pa]"]               = v["Q12_Pa"]
            row["Q66 [Pa]"]               = v["Q66_Pa"]
            row["Q16 [Pa]"]               = v["Q16_Pa"]
            row["Q26 [Pa]"]               = v["Q26_Pa"]
            row["CTE_x [1/K]"]            = v["alpha_x"]
            row["CTE_y [1/K]"]            = v["alpha_y"]
            updated += 1
            print(f"  [KaiTai] {name}")
            print(f"           Q11={v['Q11_Pa']} Pa | Ex={v['Ex_Pa']} Pa"
                  f" | nu={v['nu_xy']} | rho={v['density']}")
            print(f"           CTE_x={v['alpha_x']} | CTE_y={v['alpha_y']}")
        else:
            row.setdefault("CTE_x [1/K]", "")
            row.setdefault("CTE_y [1/K]", "")

    # --- write ---
    with open(KAITAI_CSV, "w", encoding="utf-8", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=new_fields,
                                extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)

    return updated


# ---------------------------------------------------------------------------
# EXPORT CSV  (5 columns + 2 new CTE columns)
# ---------------------------------------------------------------------------

def update_export(mapping: dict, ortho: dict) -> int:
    # --- read ---
    with open(EXPORT_CSV, encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        orig_fields = list(reader.fieldnames)
        rows = [dict(r) for r in reader]

    cte_cols = ["CTE_x [1/K]", "CTE_y [1/K]"]
    if cte_cols[0] in orig_fields:
        new_fields = orig_fields
    else:
        new_fields = orig_fields + cte_cols

    # --- update rows ---
    updated = 0
    for row in rows:
        name = row.get("Name", "").strip()
        if not name:
            row.setdefault("CTE_x [1/K]", "")
            row.setdefault("CTE_y [1/K]", "")
            continue

        if name in mapping and mapping[name] in ortho:
            v = _compute_values(ortho[mapping[name]])
            row["Density [kg/m^3]"]     = v["density"]
            row["Poisson's Ratio"]       = v["nu_xy"]
            row["Young's Modulus [Pa]"]  = v["Ex_Pa"]
            row["CTE_x [1/K]"]           = v["alpha_x"]
            row["CTE_y [1/K]"]           = v["alpha_y"]
            updated += 1
        else:
            row.setdefault("CTE_x [1/K]", "")
            row.setdefault("CTE_y [1/K]", "")

    # --- write ---
    with open(EXPORT_CSV, "w", encoding="utf-8", newline="") as fh:
        writer = csv.DictWriter(fh, fieldnames=new_fields,
                                extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)

    return updated


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main():
    print("Loading orthotropicMaterials.json …")
    ortho = _load_ortho()
    print(f"  {len(ortho)} records loaded.")

    print("Loading name_map.csv …")
    mapping = _load_name_map()
    print(f"  {len(mapping)} matched materials.")

    print("\n-- KaiTai CSV --")
    n_kaitai = update_kaitai(mapping, ortho)
    print(f"\n  -> {n_kaitai} rows updated in KaiTai CSV.")

    print("\n-- EXPORT CSV --")
    n_export = update_export(mapping, ortho)
    print(f"\n  -> {n_export} rows updated in EXPORT CSV.")

    print("\nDone.  CTE_x / CTE_y columns added to both files.")
    print("Unmatched materials left unchanged (empty CTE cells).")


if __name__ == "__main__":
    main()
