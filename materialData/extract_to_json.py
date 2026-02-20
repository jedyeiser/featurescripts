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
COMPONENT_SIZE      = 30   # columns per component
NUM_COMPONENTS      = 9


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
    'tags':                             'tags',
    'Thck. (mm)':                       'thickness_mm',
    'E0':                               'E0',
    'E0/\u03c1':                        'E0_per_rho',   # E0/ρ
    'Purchased Widths (mm)':            'purchased_widths_mm',
}

# Fixed 0-based offsets within each 30-column component block for the 9 kept fields
COMPONENT_KEPT_OFFSETS: dict[int, str] = {
    0:  'material',
    1:  'orientation',
    2:  'gsm',
    7:  'E1',
    8:  'E2',
    9:  'alpha_1',
    10: 'alpha_2',
    11: 'G12',
    12: 'v12',
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


def build_metadata_headers(ws) -> dict[int, str]:
    """Return a mapping of 1-based column index -> JSON field name for
    the Composite_Calculator metadata section (cols 1–29), built from row-2 labels.
    """
    row2: dict[int, str | None] = {}
    for row in ws.iter_rows(min_row=2, max_row=2, min_col=1,
                            max_col=METADATA_COL_END, values_only=True):
        for col_0based, val in enumerate(row):
            row2[col_0based + 1] = val

    header_map: dict[int, str] = {}
    for col_idx in range(METADATA_COL_START, METADATA_COL_END + 1):
        raw = row2.get(col_idx)
        if raw is None:
            continue
        raw_str = str(raw)
        if raw_str not in METADATA_FIELD_MAP:
            continue  # skip columns not explicitly listed (drops flags, individual tags, etc.)
        header_map[col_idx] = METADATA_FIELD_MAP[raw_str]

    return header_map


# ---------------------------------------------------------------------------
# Extractors
# ---------------------------------------------------------------------------

def extract_isotropic(wb: openpyxl.Workbook) -> list[dict]:
    """Extract 'Raw materials' sheet -> isotropicMaterials.json records.

    Only cols 1–12 (A–L) are read; the sub-label computed section is dropped.
    """
    ws = wb['Raw materials']
    col_map = RAW_MATERIALS_FIELD_MAP
    records = []

    for row in ws.iter_rows(min_row=2, min_col=1, max_col=12, values_only=True):
        name_val = row[0]  # col A
        if name_val is None or (isinstance(name_val, str) and not name_val.strip()):
            continue

        record: dict = {}
        for col_0based, val in enumerate(row):
            col_1based = col_0based + 1
            if col_1based in col_map:
                record[col_map[col_1based]] = val

        records.append(record)

    return clean_nan_values(records)


def extract_orthotropic(wb: openpyxl.Workbook) -> list[dict]:
    """Extract 'Composite_Calculator' sheet -> orthotropicMaterials.json records.

    Row 1: section labels.  Row 2: field labels.  Rows 3+: data.
    Skip rows where col B (vendor) is null.

    Top-level fields come from metadata cols 1–29.  Component data is assembled
    into a 'components' array (up to 9 slots); slots with a null material are
    skipped.  Only the 9 fields in COMPONENT_KEPT_OFFSETS are kept per component.
    The MTL combined section (cols 300–315) is not read.
    """
    ws = wb['Composite_Calculator']
    metadata_map = build_metadata_headers(ws)

    # Pre-read all component rows in one pass up to the last component column
    last_component_col = COMPONENT_COL_START + NUM_COMPONENTS * COMPONENT_SIZE - 1
    records = []

    for row in ws.iter_rows(min_row=3, min_col=METADATA_COL_START,
                            max_col=last_component_col, values_only=True):
        # row[1] == col B (vendor) — skip blank rows
        vendor_val = row[1]
        if vendor_val is None or (isinstance(vendor_val, str) and not vendor_val.strip()):
            continue

        # Pass 1: metadata fields (cols 1–29)
        record: dict = {}
        for col_idx, field_name in metadata_map.items():
            record[field_name] = row[col_idx - 1]  # row is 0-based

        # Pass 2: component slots
        components: list[dict] = []
        for slot in range(NUM_COMPONENTS):
            slot_start_col = COMPONENT_COL_START + slot * COMPONENT_SIZE  # 1-based
            # offset 0 = 'material' — null means no more components
            material_val = row[slot_start_col - 1]
            if material_val is None or (isinstance(material_val, str) and not material_val.strip()):
                break
            comp: dict = {}
            for offset, field_name in COMPONENT_KEPT_OFFSETS.items():
                comp[field_name] = row[slot_start_col - 1 + offset]
            components.append(comp)

        record['components'] = components
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
