# Parameter Visibility Fix - Output Degree & Strict Arcs

**Date:** 2026-02-09
**File:** `footprint/scaleFootprint.fs`
**Issue:** Output curve parameters only visible in SCALE_RADIUS mode

---

## Problem

The "Output curve degree" and "Strict arcs" parameters were only visible when the scale mode was set to SCALE_RADIUS. Users wanted these options available for all scaling modes (KEEP_TAPER and SCALE_RADIUS).

### Previous Behavior

```featurescript
// Lines 115-125: Parameters inside conditional block
if (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
{
    annotation { "Name" : "Target average radius" }
    isLength(definition.targetRadius, SIDECUT_RADIUS_BOUNDS);

    annotation { "Name" : "Output curve degree", "Default" : 3 }
    isInteger(definition.outputDegree, POSITIVE_COUNT_BOUNDS);

    annotation { "Name" : "Strict arcs", "Default" : false }
    definition.strictArcs is boolean;
}
```

**Result:** Parameters only appear in UI when SCALE_RADIUS is selected.

---

## Solution Implemented

### UI Parameter Definitions

**+Y Side (lines 115-125):**
```featurescript
// Target radius still conditional (only needed for SCALE_RADIUS)
if (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS)
{
    annotation { "Name" : "Target average radius" }
    isLength(definition.targetRadius, SIDECUT_RADIUS_BOUNDS);
}

// Output degree and strict arcs now ALWAYS visible
annotation { "Name" : "Output curve degree", "Default" : 3 }
isInteger(definition.outputDegree, POSITIVE_COUNT_BOUNDS);

annotation { "Name" : "Strict arcs", "Default" : false }
definition.strictArcs is boolean;
```

**-Y Side (lines 148-158):**
Same pattern applied for asymmetric mode:
- `-Y Target average radius` remains conditional
- `-Y Output curve degree` always visible
- `-Y Strict arcs` always visible

### Usage Site Simplification

**+Y Side (lines 254-256):**

Before:
```featurescript
var outputDegree = (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS && definition.outputDegree != undefined) ?
    definition.outputDegree : 3;
var strictArcs = (definition.scaleMode == FootprintScaleMode.SCALE_RADIUS) ?
    definition.strictArcs : false;
```

After:
```featurescript
// Output degree and strict arcs are now always available (not mode-dependent)
var outputDegree = definition.outputDegree;
var strictArcs = definition.strictArcs;
```

**-Y Side (lines 321-323):**
Same simplification applied for asymmetric scaling.

---

## Impact

### User Experience

**Before:**
1. User selects "Scale mode" = KEEP_TAPER
2. "Output curve degree" and "Strict arcs" are hidden
3. Code uses hardcoded defaults (degree=3, strictArcs=false)
4. User has no control over output curve quality

**After:**
1. User selects any scale mode (KEEP_TAPER or SCALE_RADIUS)
2. "Output curve degree" and "Strict arcs" are ALWAYS visible
3. User can control output curve characteristics regardless of scaling mode
4. Defaults still apply (degree=3, strictArcs=false) but user can override

### Technical Benefits

1. **Simplified logic:** No conditional checks needed when reading parameters
2. **Consistent API:** Parameters always defined with default values
3. **Better UX:** Users have full control over output quality in all modes
4. **Cleaner code:** Removed `undefined` checks and ternary operators

### Backward Compatibility

✅ **Fully compatible** - The default values match the previous hardcoded fallbacks:
- `outputDegree` defaults to 3 (same as before)
- `strictArcs` defaults to false (same as before)

Existing features will behave identically unless users change the parameters.

---

## What These Parameters Do

### Output Curve Degree

**Purpose:** Controls the polynomial degree of output BSpline curves

**Options:**
- Degree 1: Linear segments (straight lines between control points)
- Degree 2: Quadratic curves (parabolic arcs)
- Degree 3: Cubic curves (smooth, natural curves) - **DEFAULT**
- Higher degrees: More control over shape but heavier computation

**When to change:**
- Use degree 2 for simpler geometry (fewer control points)
- Use degree 3 for smooth, high-quality curves (recommended)
- Higher degrees rarely needed for footprint geometry

### Strict Arcs

**Purpose:** Forces output curves to be **exact circular arcs** (rational quadratic NURBS)

**Options:**
- `false`: Use general BSpline curves (smooth, efficient) - **DEFAULT**
- `true`: Convert to exact circular arcs (perfect arc geometry)

**When to enable:**
- When downstream CAD requires exact arcs
- When geometric precision is critical
- For manufacturing processes that prefer arcs over splines

**Trade-offs:**
- ✅ Exact circular geometry
- ✅ Compatible with arc-only CAD systems
- ❌ More curves needed (can't follow complex shapes with single arc)
- ❌ Slightly larger file size (rational NURBS weights)

---

## Modified Lines Summary

| Lines | Change |
|-------|--------|
| 115-125 | Moved `outputDegree` and `strictArcs` outside SCALE_RADIUS conditional |
| 148-158 | Moved `-Y` versions outside negScaleMode conditional |
| 254-256 | Simplified parameter usage (removed conditionals) |
| 321-323 | Simplified -Y parameter usage (removed conditionals) |

---

## Testing Checklist

- [ ] Upload to Onshape FeatureStudio
- [ ] Create Scale Footprint feature
- [ ] Test KEEP_TAPER mode:
  - [ ] Verify "Output curve degree" is visible
  - [ ] Verify "Strict arcs" is visible
  - [ ] Change degree to 2, verify output curves are quadratic
  - [ ] Enable strict arcs, verify output uses circular arcs
- [ ] Test SCALE_RADIUS mode:
  - [ ] Verify all parameters still work
  - [ ] Verify radius scaling not affected
  - [ ] Test degree and strict arcs combinations
- [ ] Test ASYMMETRIC mode:
  - [ ] Verify -Y parameters visible
  - [ ] Test different settings for +Y and -Y sides

---

## Related Files

- `footprint/scaleFootprint.fs` - Main implementation (MODIFIED)
- `footprint/arcFit.fs` - Arc fitting utilities (referenced by strict arcs mode)
- `.claude/coordinate-space-fix.md` - Related radius scaling fix

---

## Status

✅ **IMPLEMENTED** - Parameters now always visible
⏳ **PENDING** - Testing on Onshape
📝 **DOCUMENTED** - Changes and rationale captured
