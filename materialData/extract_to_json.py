"""Extract CompositeCalculator.xlsx sheets to JSON files.

Sheets:
  'Raw materials'        -> isotropicMaterials.json
  'Composite_Calculator' -> orthotropicMaterials.json

Usage (run from the materialData/ directory):
  python extract_to_json.py --sheet raw_materials
  python extract_to_json.py --sheet composite_calculator
  python extract_to_json.py --sheet all

Optional flags:
  --input PATH       path to the .xlsx file (default: CompositeCalculator.xlsx here)
  --output-dir PATH  where to write JSON files (default: same directory as script)
"""

import argparse
import json
import math
import re
import sys
from pathlib import Path

import openpyxl


# ---------------------------------------------------------------------------
# Composite_Calculator column layout (1-based, matching openpyxl)
#
# Row 1: section-level labels  ("Component 1", "Material combined stiffness matrix", ...)
# Row 2: field-level labels    (actual column names)
# Row 3+: data
#
# Metadata    cols  1 – 29   (A–AC)  29 fields
# Component 1 cols 30 – 59   (AD–BF) }
# Component 2 cols 60 – 89   (BH–CQ) } 30 fields each
# ...                                 }
# Component 9 cols 270– 299           }
# Combined    cols 300 – 315  (KR–LC) 16 fields
# ---------------------------------------------------------------------------
METADATA_COL_START  = 1    # Column A  ('Row')
METADATA_COL_END    = 29   # Column AC ('Purchased Widths (mm)')
COMPONENT_COL_START = 30   # Column AD (Component 1, field 1 = 'Material')
COMPONENT_SIZE      = 30   # columns per component (gained α1, α2, αx, αy, αxy vs old file)
NUM_COMPONENTS      = 9
COMBINED_COL_START  = 300  # Column KR ('MTL.Q̄11')
COMBINED_COL_END    = 315  # Column LC ('MTL.S̄26')


# ---------------------------------------------------------------------------
# Lookup tables
# ---------------------------------------------------------------------------

# Raw materials sheet: 1-based column index -> JSON field name
# Row 1 now has proper headers for cols A–L; col M (13) is empty (omit).
# Cols N–R (14–18) and T–Y (20–25) are a sub-label computed section:
#   row 2 of those cols contains string labels ('v12', 'E1', …) rather than
#   data; subsequent rows have calculated numeric values.
RAW_MATERIALS_FIELD_MAP: dict[int, str] = {
    1:  'name',
    2:  'density_kg_m3',
    3:  'youngs_modulus_mpa',
    4:  'E1',
    5:  'E2',
    6:  'shear_modulus_mpa',
    7:  'poissons_ratio',
    8:  'volumetric_heat_capacity',   # Excel has typo 'volummetric'; fixed here
    9:  'cte_l',
    10: 'cte_t',
    11: 'k_l',
    12: 'k_t',
    # col 13 (M): empty — omit
    14: 'fiber_v12',
    15: 'fiber_E1',
    16: 'fiber_E2',
    17: 'fiber_G12',
    18: 'fiber_Vf',
    # col 19 (S): empty — omit
    20: 'theta_rad',
    21: 'theta_deg',
    22: 'cos_theta',
    23: 'sin_theta',
    24: 'E_theta',
    25: 'E_theta_per_E1',
}

# Composite_Calculator metadata section (row-2 label -> JSON field name)
METADATA_FIELD_MAP: dict[str, str] = {
    'Row':                              'row',
    'Vendor':                           'vendor',
    'num_components':                   'num_components',
    'Material Name':                    'material_name',
    'Combined Name':                    'combined_name',
    'USD/m2':                           'usd_per_m2',
    'Fiber Volume %':                   'fiber_volume_pct',
    'Krenchel efficency factor (\u03b70)': 'krenchel_efficiency_factor',  # η0
    'Fiber Weight (gsm)':               'fiber_weight_gsm',
    'Fiber Volume (m3)':                'fiber_volume_m3',
    'Composite Volume (m3)':            'composite_volume_m3',
    'Resin Volume (m3)':                'resin_volume_m3',
    'Resin Weight':                     'resin_weight',
    'Composite weight':                 'composite_weight',
    'Density':                          'density',
    'has_0':                            'has_0',
    'has_90':                           'has_90',
    'has_45':                           'has_45',
    'has_rand':                         'has_rand',
    'mattTag':                          'matt_tag',
    '+/45Tag':                          'plus_minus_45_tag',
    '0/90Tag':                          'zero_90_tag',
    'uniTag':                           'uni_tag',
    'triaxTag':                         'triax_tag',
    'tags':                             'tags',
    'Thck. (mm)':                       'thickness_mm',
    'E0':                               'E0',
    'E0/\u03c1':                        'E0_per_rho',   # E0/ρ
    'Purchased Widths (mm)':            'purchased_widths_mm',
}

# Per-component field suffix (row-2 label -> suffix appended to 'component_N_')
# Unicode: Δ=U+0394, θ=U+03B8, α=U+03B1, combining macron=U+0304
COMPONENT_FIELD_MAP: dict[str, str] = {
    'Material':           'material',
    'Orientation':        'orientation',
    'gsm':                'gsm',
    'Fiber V (m3)':       'fiber_volume_m3',
    'Comp. Vol':          'composite_volume_m3',
    'Vol. Frac':          'volume_fraction',
    'Comp. Thck':         'thickness_mm',
    'E1':                 'E1',
    'E2':                 'E2',
    '\u03b11':            'alpha_1',       # α1
    '\u03b12':            'alpha_2',       # α2
    'G12':                'G12',
    'v12':                'v12',
    'v21':                'v21',
    '\u0394':             'delta',         # Δ
    'Q11':                'Q_11',
    'Q22':                'Q_22',
    'Q12':                'Q_12',
    'Q66':                'Q_66',
    'm = cos(\u03b8)':    'm_cos_theta',   # m = cos(θ)
    'n = sin(\u03b8)':    'n_sin_theta',   # n = sin(θ)
    'Q\u030411':          'Q_bar_11',      # Q̄11
    'Q\u030422':          'Q_bar_22',
    'Q\u030412':          'Q_bar_12',
    'Q\u030466':          'Q_bar_66',
    'Q\u030416':          'Q_bar_16',
    'Q\u030426':          'Q_bar_26',
    '\u03b1x':            'alpha_x',       # αx
    '\u03b1y':            'alpha_y',       # αy
    '\u03b1xy':           'alpha_xy',      # αxy
}

# Combined-laminate section (row-2 label -> JSON field name)
MTL_FIELD_MAP: dict[str, str] = {
    'MTL.Q\u030411': 'mtl_Q_bar_11',
    'MTL.Q\u030422': 'mtl_Q_bar_22',
    'MTL.Q\u030412': 'mtl_Q_bar_12',
    'MTL.Q\u030466': 'mtl_Q_bar_66',
    'MTL.Q\u030416': 'mtl_Q_bar_16',
    'MTL.Q\u030426': 'mtl_Q_bar_26',
    '\u03b1x':        'mtl_alpha_x',      # αx (MTL combined CTE)
    '\u03b1y':        'mtl_alpha_y',
    '\u03b1xy':       'mtl_alpha_xy',
    'det(Q)':         'det_Q',
    'MTL.S\u030411': 'mtl_S_bar_11',
    'MTL.S\u030422': 'mtl_S_bar_22',
    'MTL.S\u030412': 'mtl_S_bar_12',
    'MTL.S\u030466': 'mtl_S_bar_66',
    'MTL.S\u030416': 'mtl_S_bar_16',
    'MTL.S\u030426': 'mtl_S_bar_26',
}


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def clean_nan_values(data, blank_replacement=None):
    """Recursively replace None and float NaN with blank_replacement."""
    if isinstance(data, dict):
        return {k: clean_nan_values(v, blank_replacement) for k, v in data.items()}
    if isinstance(data, list):
        return [clean_nan_values(v, blank_replacement) for v in data]
    if data is None:
        return blank_replacement
    if isinstance(data, float) and math.isnan(data):
        return blank_replacement
    return data


def sanitize_name(raw: str) -> str:
    """Fallback: convert a raw header to ASCII snake_case.

    Used when a label is not found in one of the hardcoded lookup tables.
    """
    if not raw:
        return ''
    s = str(raw).strip()
    s = s.replace('Q\u0304', 'Q_bar')
    s = s.replace('S\u0304', 'S_bar')
    s = s.replace('\u0394', 'delta')
    s = s.replace('\u03b1', 'alpha')
    s = s.replace('m = cos(\u03b8)', 'm_cos_theta')
    s = s.replace('n = sin(\u03b8)', 'n_sin_theta')
    s = s.replace('MTL.', 'mtl_')
    s = s.replace('det(Q)', 'det_Q')
    s = s.replace('%', 'pct')
    s = s.replace('/', '_per_')
    s = re.sub(r'[^a-zA-Z0-9]+', '_', s)
    s = re.sub(r'_+', '_', s).strip('_')
    return s.lower()


def build_composite_headers(ws) -> dict[int, str]:
    """Return a mapping of 1-based column index -> JSON field name for
    Composite_Calculator, built from the row-2 field labels.
    """
    row2: dict[int, str | None] = {}
    for row in ws.iter_rows(min_row=2, max_row=2, min_col=1,
                            max_col=COMBINED_COL_END, values_only=True):
        for col_0based, val in enumerate(row):
            row2[col_0based + 1] = val

    header_map: dict[int, str] = {}

    # Metadata (cols 1–29)
    for col_idx in range(METADATA_COL_START, METADATA_COL_END + 1):
        raw = row2.get(col_idx)
        if raw is None:
            continue
        raw_str = str(raw)
        header_map[col_idx] = METADATA_FIELD_MAP.get(raw_str, sanitize_name(raw_str))

    # Component sections (9 × 30 columns starting at col 30)
    for comp_num in range(1, NUM_COMPONENTS + 1):
        start = COMPONENT_COL_START + (comp_num - 1) * COMPONENT_SIZE
        for offset in range(COMPONENT_SIZE):
            col_idx = start + offset
            raw = row2.get(col_idx)
            if raw is None:
                continue
            raw_str = str(raw)
            suffix = COMPONENT_FIELD_MAP.get(raw_str, sanitize_name(raw_str))
            header_map[col_idx] = f'component_{comp_num}_{suffix}'

    # Combined-laminate section (cols 300–315)
    for col_idx in range(COMBINED_COL_START, COMBINED_COL_END + 1):
        raw = row2.get(col_idx)
        if raw is None:
            continue
        raw_str = str(raw)
        header_map[col_idx] = MTL_FIELD_MAP.get(raw_str, sanitize_name(raw_str))

    return header_map


# ---------------------------------------------------------------------------
# Extractors
# ---------------------------------------------------------------------------

def extract_isotropic(wb: openpyxl.Workbook) -> list[dict]:
    """Extract 'Raw materials' sheet -> isotropicMaterials.json records.

    Row 1 has proper headers for cols A–L.
    Row 2 is the first data row (Fiberglass Tow), but cols N–R and T–Y in
    that row contain sub-label strings ('v12', 'E1', …) rather than data;
    those string values are treated as null for that row only.
    """
    ws = wb['Raw materials']
    col_map = RAW_MATERIALS_FIELD_MAP
    # Cols that contain sub-label strings in row 2 (but numeric data in others)
    sublabel_cols = frozenset({14, 15, 16, 17, 18, 20, 21, 22, 23, 24, 25})
    records = []

    for row in ws.iter_rows(min_row=2, min_col=1, max_col=25, values_only=True):
        name_val = row[0]  # col A
        if name_val is None or (isinstance(name_val, str) and not name_val.strip()):
            continue

        record: dict = {}
        for col_0based, val in enumerate(row):
            col_1based = col_0based + 1
            if col_1based not in col_map:
                continue  # omit cols 13 (M) and 19 (S)
            if col_1based in sublabel_cols and isinstance(val, str):
                val = None  # row-2 sub-label text, not numeric data
            record[col_map[col_1based]] = val

        records.append(record)

    return clean_nan_values(records)


def extract_orthotropic(wb: openpyxl.Workbook) -> list[dict]:
    """Extract 'Composite_Calculator' sheet -> orthotropicMaterials.json records.

    Row 1: section labels.  Row 2: field labels.  Rows 3+: data.
    Skip rows where col B (vendor) is null.
    """
    ws = wb['Composite_Calculator']
    col_map = build_composite_headers(ws)
    records = []

    for row in ws.iter_rows(min_row=3, min_col=METADATA_COL_START,
                            max_col=COMBINED_COL_END, values_only=True):
        # row[1] == col B (vendor) — skip blank rows
        vendor_val = row[1]
        if vendor_val is None or (isinstance(vendor_val, str) and not vendor_val.strip()):
            continue

        record: dict = {}
        for col_0based, val in enumerate(row):
            col_1based = col_0based + METADATA_COL_START
            if col_1based in col_map:
                record[col_map[col_1based]] = val

        records.append(record)

    return clean_nan_values(records)


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main() -> None:
    parser = argparse.ArgumentParser(
        description='Extract CompositeCalculator.xlsx sheets to JSON.'
    )
    parser.add_argument(
        '--sheet',
        choices=['raw_materials', 'composite_calculator', 'all'],
        default='all',
        help='Which sheet to extract (default: all)',
    )
    parser.add_argument(
        '--input',
        dest='input_path',
        default=None,
        help='Path to the .xlsx file (default: CompositeCalculator.xlsx in this directory)',
    )
    parser.add_argument(
        '--output-dir',
        dest='output_dir',
        default=None,
        help='Output directory for JSON files (default: same directory as this script)',
    )
    args = parser.parse_args()

    script_dir = Path(__file__).parent

    if args.input_path:
        xlsx_path = Path(args.input_path)
    else:
        candidates = sorted(script_dir.glob('*.xlsx'))
        if not candidates:
            print('ERROR: No .xlsx file found in materialData/', file=sys.stderr)
            sys.exit(1)
        xlsx_path = next(
            (f for f in candidates if 'CompositeCalculator' in f.name), candidates[0]
        )

    output_dir = Path(args.output_dir) if args.output_dir else script_dir
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f'Reading: {xlsx_path}')
    wb = openpyxl.load_workbook(str(xlsx_path), read_only=True, data_only=True)

    if args.sheet in ('raw_materials', 'all'):
        print('Extracting Raw materials ...')
        data = extract_isotropic(wb)
        out_path = output_dir / 'isotropicMaterials.json'
        with open(out_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
        print(f'  -> {out_path}  ({len(data)} records)')

    if args.sheet in ('composite_calculator', 'all'):
        print('Extracting Composite_Calculator ...')
        data = extract_orthotropic(wb)
        out_path = output_dir / 'orthotropicMaterials.json'
        with open(out_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
        print(f'  -> {out_path}  ({len(data)} records)')

    wb.close()
    print('Done.')


if __name__ == '__main__':
    main()
