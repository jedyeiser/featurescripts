# QC Table Column Reorganization

## Date: 2026-02-09

## Summary

Reorganized QC Table columns with a new two-tier system: STANDARD mode (essential columns) and DETAILS mode (all measurements).

---

## Changes Made

### 1. **Added DETAIL_LEVEL Enum** (qcTable_types.fs)

```featurescript
export enum DETAIL_LEVEL
{
    annotation { "Name" : "Standard (Essential columns only)" }
    STANDARD,
    annotation { "Name" : "Details (All measurements)" }
    DETAILS
}
```

### 2. **Updated FormatConfig Type** (qcTable_types.fs)

Added `detailLevel` field to FormatConfig:
```featurescript
export predicate canBeFormatConfig(value)
{
    value is map;
    value.tableUnits is EXPORT_UNITS;
    value.sigFigs is number;
    value.showUnits is boolean;
    value.detailLevel is DETAIL_LEVEL;  // NEW
}
```

### 3. **Added Core/SW Delta Calculation** (qcTable_merge.fs)

New delta field that combines top and bottom deltas into single thickness comparison:
```featurescript
// Compute delta if both present (how much thicker is core than SW?)
if (coreData != undefined && swData != undefined)
{
    row.core_sw_delta = coreData.coreThickness - swData.swHeight;
    row.bottom_delta = coreData.coreBottomZ - swData.swBottomZ;
    row.top_delta = coreData.coreTopZ - swData.swTopZ;
}
```

**Calculation**: `core_sw_delta = Core Height - SW Height`
- Positive value = core is thicker than SW
- Negative value = SW is thicker than core

### 4. **Reorganized Column Definitions** (qcTable_merge.fs)

Completely rewrote `buildColumnDefinitions()` function:

**New signature**:
```featurescript
export function buildColumnDefinitions(
    hasCore is boolean,
    hasSW is boolean,
    detailLevel is DETAIL_LEVEL) returns array
```

**STANDARD Mode Columns** (always displayed):
1. **Callout** - Station identifier (moved to first position)
2. **Station** - Numeric index
3. **X** - Absolute X position from MRS
4. **Core Height** - Core thickness (if core present)
5. **SW Height** - Sidewall height (if SW present)
6. **Core/SW Δ** - Thickness delta (if both present)

**DETAILS Mode Columns** (additional):
7. **X from ACP** - Distance reference
8. **X from Core Tail** - Distance from core end (if core present)
9. **X from SW Tail** - Distance from SW end (if SW present)
10. **Core Width** - Core width measurement
11. **Grooved Thickness** - Front plane thickness
12. **Top Width** - Top edge width
13. **Top Angle** - Top edge angle
14. **BR Depth** - Base rout depth
15. **BR Width** - Base rout width

### 5. **Updated Main Feature** (qcTable.fs)

**UI Changes**:
- Added `detailLevel` parameter to "Table Formatting" group
- Default: `DETAIL_LEVEL.STANDARD`
- Positioned first in formatting group for visibility

**Data Flow**:
- `detailLevel` added to `formatConfig`
- Stored in table attribute for persistence
- Passed to `buildColumnDefinitions()` in table function

---

## Column Organization Rationale

### STANDARD Mode (6 columns)
**Purpose**: Quick QC checks - shows only essential measurements
- **Identification**: Callout, Station
- **Position**: Single X reference (from MRS)
- **Key Measurements**: Heights and thickness delta

**Use Case**: Daily production QC, quick visual scans

### DETAILS Mode (up to 15 columns)
**Purpose**: Comprehensive analysis with all measurements
- **Distance References**: Multiple coordinate systems (MRS, ACP, body ends)
- **Core Geometry**: Width, grooved thickness, top edge details, base rout

**Use Case**: Detailed analysis, troubleshooting, full documentation

---

## Data Flow

```
User selects DETAIL_LEVEL in UI
    ↓
Stored in formatConfig
    ↓
Passed to mergeStationData() (computes core_sw_delta)
    ↓
Stored in attribute with detailLevel
    ↓
Table retrieves detailLevel from attribute
    ↓
buildColumnDefinitions(hasCore, hasSW, detailLevel)
    ↓
Returns appropriate column set
    ↓
Table displays with correct columns
```

---

## Key Design Decisions

1. **Callout First**: Most important identifier for users - easy to scan
2. **Single X Column in STANDARD**: Simplified - additional references in DETAILS
3. **Combined Delta**: Single `Core/SW Δ` more intuitive than separate top/bottom
4. **Grouped DETAILS Columns**: Distance refs together, geometry details together
5. **Bottom/Top Deltas Removed**: Combined into single delta, old deltas still computed but not displayed

---

## Testing Checklist

- [ ] STANDARD mode shows 6 columns (Core + SW)
- [ ] STANDARD mode shows 4 columns (Core only: Callout, Station, X, Core Height)
- [ ] STANDARD mode shows 4 columns (SW only: Callout, Station, X, SW Height)
- [ ] DETAILS mode shows all 15 columns (Core + SW)
- [ ] Core/SW Δ calculates correctly (Core Height - SW Height)
- [ ] Positive delta when core thicker, negative when SW thicker
- [ ] Column order matches specification
- [ ] Switching between STANDARD/DETAILS updates table correctly
- [ ] All map keys use double quotes (consistent style)

---

## Files Modified

1. **qcTable_types.fs**
   - Added `DETAIL_LEVEL` enum
   - Updated `FormatConfig` type predicate

2. **qcTable_merge.fs**
   - Added `core_sw_delta` calculation
   - Added formatting for `core_sw_delta`
   - Rewrote `buildColumnDefinitions()` function

3. **qcTable.fs**
   - Added `detailLevel` UI parameter
   - Updated `formatConfig` construction
   - Updated attribute storage
   - Updated table function to retrieve and use `detailLevel`
   - Converted all map keys to double quotes

---

## Migration Notes

**Existing tables**: Will default to STANDARD mode on next regeneration. Users can switch to DETAILS to see all columns.

**No data loss**: All measurements still computed, just selectively displayed based on detail level.
