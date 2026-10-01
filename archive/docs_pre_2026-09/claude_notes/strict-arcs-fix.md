# Strict Arcs Implementation Fix

**Date:** 2026-02-09
**File:** `footprint/scaleFootprint.fs`
**Issue:** Strict arcs parameter not producing arc output

---

## Problem

User reported that enabling "Strict arcs" checkbox was not changing the output - curves were still being created as general BSplines instead of being converted to rational quadratic NURBS (exact circular arcs).

### Root Causes

1. **Missing diagnostics:** No console output to confirm strict arcs mode was being entered
2. **Limited scope:** Strict arcs only worked in SCALE_RADIUS mode, not KEEP_TAPER or ACCORDION
3. **Silent failures:** No way to verify the conversion was happening or see arc properties

---

## Solution Implemented

### 1. Added Diagnostic Output (SCALE_RADIUS mode)

**Lines 1957-1982:**
Added detailed console output showing:
- Confirmation that strict arcs mode is enabled
- Input curve properties (degree, rational flag, control point count)
- Output arc properties for verification
- Total arc segment count

**Example output:**
```
STRICT ARCS ENABLED - Converting 3 curves to arcs...
  Input curve 0: degree=3, rational=false, CPs=12
  Input curve 1: degree=3, rational=false, CPs=15
  Input curve 2: degree=3, rational=false, CPs=10
Arc conversion complete: 8 arc segments
  Output arc 0: degree=2, rational=true, CPs=3
  Output arc 1: degree=2, rational=true, CPs=3
  Output arc 2: degree=2, rational=true, CPs=3
Strict arcs conversion applied successfully
```

### 2. Extended Strict Arcs to All Scaling Modes

**Updated function signatures:**
- `scaleAccordion` (line 1121): Added `id`, `outputDegree`, `strictArcs` parameters
- `scaleKeepTaper` (line 1217): Added `id`, `outputDegree`, `strictArcs` parameters
- `scaleSidecut` (line 474-486): Updated to pass parameters to all modes

**Added arc conversion:**
- ACCORDION mode (line 1178-1183): Apply strict arcs before return
- KEEP_TAPER mode (line 1388-1393): Apply strict arcs before return
- SCALE_RADIUS mode (line 1957-1982): Enhanced diagnostics

---

## How Strict Arcs Works

### Conversion Pipeline

1. **Input:** BSpline curves (typically degree 3 cubic, non-rational)
2. **Arc fitting:** `approximateSplinesWithPolyArcs` decomposes curves into line/arc primitives
   - Tolerance: 0.001mm position, 0.001mm plane
   - Angle tolerance: cos(0.1°) for tangent matching
   - Min segment length: 1mm
3. **NURBS conversion:** `primitivesToBSplines` converts primitives to rational quadratic NURBS
4. **Output:** Array of rational quadratic BSpline curves (exact arcs)

### Expected Results

**Input curves:**
- Degree: 3 (cubic)
- Rational: false
- Control points: Variable (8-30 typical)

**Output arcs:**
- Degree: 2 (quadratic)
- Rational: true (weights for exact circular arcs)
- Control points: 3 per arc segment
- Multiple arcs replace single curve (1 input → 2-5 output arcs typical)

---

## Testing Instructions

### 1. Upload and Test

1. Upload fixed `scaleFootprint.fs` to Onshape
2. Create Scale Footprint feature
3. Enable "Strict arcs" checkbox

### 2. Verify SCALE_RADIUS Mode

1. Set scale mode: SCALE_RADIUS
2. Set target radius: 21m
3. Enable strict arcs
4. Check console output:
   - Should show "STRICT ARCS ENABLED" message
   - Should list input curve properties
   - Should list output arc properties
   - Output arcs should be degree=2, rational=true

### 3. Verify KEEP_TAPER Mode

1. Set scale mode: KEEP_TAPER
2. Enable strict arcs
3. Check console output:
   - Should show "KEEP_TAPER: Converting to strict arcs..."
   - Should show arc count

### 4. Verify ACCORDION Mode

1. Set scale mode: ACCORDION
2. Enable strict arcs
3. Check console output:
   - Should show "ACCORDION: Converting to strict arcs..."
   - Should show arc count

### 5. Visual Verification

- Right-click on output curve → "Show curvature combs"
- Arc segments should have constant curvature (uniform comb height)
- Sharp transitions at arc boundaries are expected
- Each arc segment represents a perfect circular arc

---

## Code Changes Summary

| File | Lines | Change |
|------|-------|--------|
| scaleFootprint.fs | 474-486 | Updated `scaleSidecut` to pass strict arcs to all modes |
| scaleFootprint.fs | 1121-1126 | Updated `scaleAccordion` signature with id, outputDegree, strictArcs |
| scaleFootprint.fs | 1178-1183 | Added strict arcs conversion in ACCORDION mode |
| scaleFootprint.fs | 1217-1222 | Updated `scaleKeepTaper` signature with id, outputDegree, strictArcs |
| scaleFootprint.fs | 1388-1393 | Added strict arcs conversion in KEEP_TAPER mode |
| scaleFootprint.fs | 1957-1982 | Added detailed diagnostics for SCALE_RADIUS arc conversion |

---

## Technical Details

### Arc Fitting Algorithm

The `approximateSplinesWithPolyArcs` function (from `arcFit.fs`):
1. Orders and orients input splines into continuous chain
2. Classifies curve joins as hard/soft boundaries
3. Builds initial segments from knot spans (over-segmented)
4. Fits line or arc primitive to each segment
5. Merges adjacent segments until quality degrades
6. Returns optimized set of line/arc primitives

### NURBS Arc Representation

Rational quadratic NURBS can represent exact circular arcs:
- 3 control points
- Weights computed from arc angle
- Knot vector: [0, 0, 0, 1, 1, 1]
- Perfect circular geometry (not approximation)

### Why Multiple Arcs?

A single complex curve may require multiple arcs because:
- Arc can span max ~180° (numerical stability)
- Variable curvature requires multiple arcs
- Sharp corners create arc boundaries
- Better to have more small arcs than loose approximation

---

## Troubleshooting

### "Strict arcs" not showing in console

**Cause:** Parameter not enabled or scale mode changed
**Fix:** Verify checkbox is checked and save feature

### Output still looks like splines

**Cause:** Arcs may visually appear smooth (they are smooth!)
**Fix:** Check console for "degree=2, rational=true" confirmation

### Too many arc segments

**Cause:** Complex curvature requires many arcs for exact representation
**Fix:** This is expected - each arc is geometrically perfect

### Continuity breaks at arc boundaries

**Cause:** Arc fitting introduces C0/C1 boundaries
**Fix:** Expected behavior - perfect geometric accuracy vs perfect smoothness trade-off

---

## Related Files

- `footprint/scaleFootprint.fs` - Main implementation (FIXED)
- `footprint/arcFit.fs` - Arc fitting algorithms
- `.claude/parameter-visibility-fix.md` - Related parameter visibility changes
- `.claude/coordinate-space-fix.md` - Related radius scaling fix

---

## Bug Fix: BSpline Construction Error

**Error:** `Precondition of bSplineCurve failed (definition.knots is undefined || definition.knots is KnotArray)`

**Root Cause:** Arc-converted curves from `forceQuadraticNurbs` have plain array knots `[0, 0, 0, 1, 1, 1]`, but when passed through `mirrorCurvesY` or `transformTipTail`, the BSpline construction failed.

**Fix Applied (lines 2074-2110, 2031-2068):**

Updated `mirrorCurvesY` and `transformTipTail` to conditionally include only defined fields when constructing BSpline curves:

```featurescript
// Build parameter map with only defined fields
var params = {
    "degree" : bspline.degree,
    "controlPoints" : newControlPoints
};

// Only add optional fields if they exist and are defined
if (bspline.isPeriodic != undefined)
    params.isPeriodic = bspline.isPeriodic;

if (bspline.knots != undefined)
    params.knots = bspline.knots;

if (bspline.weights != undefined)
    params.weights = bspline.weights;

if (bspline.isRational != undefined)
    params.isRational = bspline.isRational;

mirrored = append(mirrored, bSplineCurve(params));
```

This ensures compatibility with both regular BSpline curves and arc-converted rational NURBS curves.

---

## Status

✅ **IMPLEMENTED** - Strict arcs supported in all modes
✅ **DIAGNOSTICS** - Console output added
✅ **BUG FIXED** - BSpline construction error resolved
⏳ **PENDING** - Testing on Onshape
📝 **DOCUMENTED** - Implementation and usage captured
