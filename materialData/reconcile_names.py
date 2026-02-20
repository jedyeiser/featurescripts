#!/usr/bin/env python
"""
reconcile_names.py — Material name reconciliation tool.

Maps each CSV material name to its JSON record (orthotropic or isotropic).
Produces/updates materialData/name_map.csv.

Columns: csv_name | category | json_file | json_key | match_type | notes

match_type values:
  exact      – identical string match
  normalized – matched after collapsing 'Vendor: Name' → 'Vendor:Name'
  fuzzy      – auto-accepted single candidate with ratio ≥ 0.85
  confirmed  – user confirmed a fuzzy candidate interactively
  none       – no match found (needs research or new material)

Re-runnable: rows already resolved in name_map.csv are preserved and skipped.
Interactive: prompts for ambiguous matches when stdin is a TTY.

Usage:
    python reconcile_names.py
"""

import csv
import difflib
import json
import os
import re
import sys

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

DATA_DIR   = os.path.dirname(os.path.abspath(__file__))
CSV_MAIN   = os.path.join(DATA_DIR, "KaiTai_ski_snowboard_material_library.csv")
CSV_EXPORT = os.path.join(DATA_DIR, "Kaitai_ski_snowboard_material_library_EXPORT.csv")
ORTHO_JSON = os.path.join(DATA_DIR, "orthotropicMaterials.json")
ISO_JSON   = os.path.join(DATA_DIR, "isotropicMaterials.json")
NAME_MAP   = os.path.join(DATA_DIR, "name_map.csv")

FIELDNAMES = ["csv_name", "category", "json_file", "json_key", "match_type", "notes"]

RESOLVED      = {"exact", "normalized", "fuzzy", "confirmed"}
FUZZY_AUTO    = 0.85   # single candidate at or above this → auto-accept as 'fuzzy'
FUZZY_PROMPT  = 0.70   # candidates at or above this → show in interactive prompt


# ---------------------------------------------------------------------------
# Utilities
# ---------------------------------------------------------------------------

def normalize(name: str) -> str:
    """Strip leading/trailing whitespace; collapse 'Vendor: Name' → 'Vendor:Name'."""
    return re.sub(r":\s+", ":", name.strip())


# ---------------------------------------------------------------------------
# Data loading
# ---------------------------------------------------------------------------

def load_csv(path: str) -> list:
    """Return [(name, category), ...] from a material CSV."""
    rows = []
    with open(path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            name = row.get("Name", "").strip()
            cat  = row.get("Category", "").strip()
            if name:
                rows.append((name, cat))
    return rows


def load_json_dicts(ortho_path: str, iso_path: str):
    """Return (ortho_dict, iso_dict) keyed by their name string."""
    with open(ortho_path, encoding="utf-8") as f:
        ortho_list = json.load(f)
    with open(iso_path, encoding="utf-8") as f:
        iso_list = json.load(f)
    ortho_dict = {r["combined_name"]: r for r in ortho_list}
    iso_dict   = {r["name"]:          r for r in iso_list}
    return ortho_dict, iso_dict


def load_name_map(path: str) -> dict:
    """Return {csv_name: row_dict} for an existing name_map.csv, or {}."""
    if not os.path.exists(path):
        return {}
    with open(path, newline="", encoding="utf-8") as f:
        return {row["csv_name"]: row for row in csv.DictReader(f)}


def save_name_map(path: str, rows: list) -> None:
    with open(path, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=FIELDNAMES)
        writer.writeheader()
        writer.writerows(rows)


# ---------------------------------------------------------------------------
# Matching
# ---------------------------------------------------------------------------

def build_norm_index(keys) -> dict:
    """Return {normalize(key): original_key} for a collection of key strings."""
    index = {}
    for k in keys:
        nk = normalize(k)
        if nk not in index:
            index[nk] = k
        # Silently keep first; duplicates after normalization are extremely unlikely
    return index


def fuzzy_candidates(csv_norm: str, norm_index: dict, json_file: str) -> list:
    """
    Return [(ratio, json_file, original_key)] for all keys with ratio ≥ FUZZY_PROMPT,
    sorted descending by ratio.
    """
    results = []
    for norm_key, orig_key in norm_index.items():
        ratio = difflib.SequenceMatcher(None, csv_norm, norm_key).ratio()
        if ratio >= FUZZY_PROMPT:
            results.append((ratio, json_file, orig_key))
    results.sort(key=lambda x: -x[0])
    return results


def infer_notes(csv_name: str, json_file: str, json_key: str) -> str:
    """
    Return a space-joined string of flag tokens for a fuzzy/confirmed match.
    TYPO?    – vendor prefix differs between CSV and JSON key
    ISO_ONLY – matched to isotropicMaterials.json
    """
    flags = []
    csv_vendor = csv_name.split(":")[0].strip().lower()
    jk_vendor  = json_key.split(":")[0].strip().lower()
    if csv_vendor != jk_vendor:
        flags.append("TYPO?")
    if json_file == "isotropicMaterials.json":
        flags.append("ISO_ONLY")
    return " ".join(flags)


def match_name(csv_name: str,
               ortho_dict: dict, iso_dict: dict,
               ortho_norm: dict, iso_norm: dict):
    """
    Attempt to match csv_name against JSON records.

    Returns:
        (match_type, json_file, json_key, notes, needs_prompt, candidates)

    needs_prompt=True means the caller should ask the user interactively.
    candidates is [(ratio, json_file, json_key)].
    """
    # 1. Exact match
    if csv_name in ortho_dict:
        return "exact", "orthotropicMaterials.json", csv_name, "", False, []
    if csv_name in iso_dict:
        return "exact", "isotropicMaterials.json", csv_name, "ISO_ONLY", False, []

    # 2. Normalized match
    csv_norm = normalize(csv_name)
    if csv_norm in ortho_norm:
        return "normalized", "orthotropicMaterials.json", ortho_norm[csv_norm], "", False, []
    if csv_norm in iso_norm:
        return "normalized", "isotropicMaterials.json", iso_norm[csv_norm], "ISO_ONLY", False, []

    # 3. Fuzzy match
    candidates = (
        fuzzy_candidates(csv_norm, ortho_norm, "orthotropicMaterials.json")
        + fuzzy_candidates(csv_norm, iso_norm,   "isotropicMaterials.json")
    )
    candidates.sort(key=lambda x: -x[0])

    if not candidates:
        return "none", "", "", "NEEDS_RESEARCH", False, []

    above = [c for c in candidates if c[0] >= FUZZY_AUTO]

    # Single high-confidence candidate → auto-accept
    if len(above) == 1:
        ratio, jf, jk = above[0]
        notes = infer_notes(csv_name, jf, jk)
        return "fuzzy", jf, jk, notes, False, above

    # Multiple high-confidence OR best is in the ambiguous band → prompt
    if len(above) > 1 or candidates[0][0] >= FUZZY_PROMPT:
        return "prompt", "", "", "", True, candidates

    return "none", "", "", "NEEDS_RESEARCH", False, []


# ---------------------------------------------------------------------------
# Interactive prompt
# ---------------------------------------------------------------------------

def prompt_user(csv_name: str, candidates: list):
    """
    Show top candidates and ask the user to pick one.

    Returns (json_file, json_key, match_type).
    """
    top = candidates[:5]
    print(f'\nNo confident match for: "{csv_name}"')
    print("Candidates:")
    for i, (ratio, jf, jk) in enumerate(top, 1):
        print(f"  [{i}] {jk}  (ratio {ratio:.2f})")
    print("  [0] None of these — mark as unmatched")

    while True:
        try:
            raw = input("Your choice: ").strip()
            n = int(raw)
            if n == 0:
                return "", "", "none"
            if 1 <= n <= len(top):
                _, jf, jk = top[n - 1]
                return jf, jk, "confirmed"
            print(f"  Please enter 0–{len(top)}.")
        except ValueError:
            print("  Please enter a number.")
        except (KeyboardInterrupt, EOFError):
            print()
            return "", "", "none"


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    print("Loading CSVs...")
    csv1 = load_csv(CSV_MAIN)
    csv2 = load_csv(CSV_EXPORT)

    # Merge in CSV order, deduplicating by name; CSV_MAIN takes priority for category
    seen = set()
    all_csv = []
    for name, cat in csv1 + csv2:
        if name not in seen:
            seen.add(name)
            all_csv.append((name, cat))
    print(f"  {len(all_csv)} unique CSV names")

    print("Loading JSON...")
    ortho_dict, iso_dict = load_json_dicts(ORTHO_JSON, ISO_JSON)
    ortho_norm = build_norm_index(ortho_dict)
    iso_norm   = build_norm_index(iso_dict)
    print(f"  {len(ortho_dict)} orthotropic, {len(iso_dict)} isotropic records")

    existing = load_name_map(NAME_MAP)
    print(f"  {len(existing)} rows already in name_map.csv")

    is_tty = sys.stdin.isatty()
    result = []
    counts = {k: 0 for k in ("exact", "normalized", "fuzzy", "confirmed", "none", "skipped")}

    for csv_name, category in all_csv:
        # Preserve already-resolved rows
        if csv_name in existing and existing[csv_name]["match_type"] in RESOLVED:
            result.append(existing[csv_name])
            counts["skipped"] += 1
            continue

        match_type, json_file, json_key, notes, needs_prompt, candidates = match_name(
            csv_name, ortho_dict, iso_dict, ortho_norm, iso_norm
        )

        if needs_prompt:
            if is_tty:
                json_file, json_key, match_type = prompt_user(csv_name, candidates)
                if match_type == "none":
                    notes = "NEEDS_RESEARCH"
                else:
                    notes = infer_notes(csv_name, json_file, json_key)
            else:
                # Non-interactive mode: leave ambiguous rows as none
                match_type, json_file, json_key, notes = "none", "", "", "NEEDS_RESEARCH"

        row = {
            "csv_name":   csv_name,
            "category":   category,
            "json_file":  json_file,
            "json_key":   json_key,
            "match_type": match_type,
            "notes":      notes,
        }
        result.append(row)
        counts[match_type if match_type in counts else "none"] += 1

        # Save immediately after every interactive prompt so progress is not lost
        if needs_prompt and is_tty:
            save_name_map(NAME_MAP, result)

    # Final save
    save_name_map(NAME_MAP, result)

    # Summary
    print("\n--- Summary ---")
    for key in ("exact", "normalized", "fuzzy", "confirmed", "none", "skipped"):
        print(f"  {key:<12} {counts[key]}")
    print(f"\nname_map.csv written ({len(result)} rows):")
    print(f"  {NAME_MAP}")


if __name__ == "__main__":
    main()
