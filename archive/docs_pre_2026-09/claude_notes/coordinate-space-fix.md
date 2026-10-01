# Coordinate Space Mismatch Fix - SCALE_RADIUS Mode

**Date:** 2026-02-09
**File:** `footprint/scaleFootprint.fs`
**Lines Modified:** 1705-1708, 1738-1771, 1848-1850

---

## Problem Summary

The SCALE_RADIUS implementation had a **catastrophic coordinate space mismatch** causing 23% radius error (4.9m):

- **Input reference:** 14.93m radius
- **Target:** 21m
- **Iteration converged to:** 20.99m ✅ (perfect!)
- **Final output measured:** 16.09m ❌ (23% error!)

The iteration algorithm was working perfectly, but the final geometry was corrupted.

---

## Root Cause

The selective curvature scaling algorithm operated in **two different coordinate spaces** without proper transformation:

### Before Fix (BROKEN)

```featurescript
// Lines 1722-1729: Sample curvature in REFERENCE coordinate space
for (var i = 0; i < numSamples; i += 1)
{
    var t = i / (numSamples - 1);
    var x = refFcpX + t * (refAcpX - refFcpX);  // ← REFERENCE X coords
    refXSamples = append(refXSamples, x);

    var k = getCurvatureAtX(refCurveData, x, tolerance);
    kSamples = append(kSamples, k);  // ← Curvature indexed by REFERENCE space
}

// Lines 1740-1756: Scale using NEW coordinate space (WRONG!)
for (var i = 0; i < size(kReference); i += 1)
{
    var x = newFcpX + (i / (size(kReference) - 1)) * (newAcpX - newFcpX);  // ← NEW X coords
    var k = kReference[i];  // ← Curvature from REFERENCE sampling

    // Check if in sidecut region using NEW coordinates
    if (x >= inflectionXMin && x <= inflectionXMax)  // ← Bounds in NEW space
    {
        scaledK = append(scaledK, k * radiusScaleFactor);  // ← Scaling wrong values!
    }
}
```

### Why This Failed

1. **Curvature sampled** at uniform intervals in **REFERENCE space** (refFcpX to refAcpX)
2. **Scaling decision** made using **NEW space** coordinates (newFcpX to newAcpX)
3. **Array index `i`** maps to **different X locations** in the two spaces
4. Inflection bounds (found in NEW space) compared against NEW coordinates
5. **Result:** Wrong curvature values get scaled, geometry is corrupted

**Example of the mismatch:**
- Reference: FCP=-0.8m, ACP=0.8m (length=1.6m)
- New (scaled): FCP=-0.675m, ACP=0.675m (length=1.35m) — 15.6% shorter
- Inflection bounds in NEW space: -0.477m to 0.490m
- When checking `if (x >= -0.477m)` with NEW coordinate `x`...
- ...we select `kReference[i]` which was sampled at a **different** reference space x!
- The curvature at NEW x=-0.477m gets mapped to REFERENCE x≈-0.565m
- **Wrong region** of curvature gets scaled!

---

## Solution Implemented

### After Fix (CORRECT)

```featurescript
// Lines 1707-1708: Declare inflection bounds in both spaces
var refInflectionXMin = refFcpX;  // Default to full sidecut (REFERENCE space)
var refInflectionXMax = refAcpX;

// Lines 1748-1771: Use REFERENCE space consistently
// Transform inflection bounds from NEW space to REFERENCE space
var xScale = (newAcpX - newFcpX) / (refAcpX - refFcpX);
refInflectionXMin = refFcpX + (inflectionXMin - newFcpX) / xScale;
refInflectionXMax = refFcpX + (inflectionXMax - newFcpX) / xScale;

for (var i = 0; i < size(kReference); i += 1)
{
    // Use REFERENCE X coordinate (matching how kReference was sampled)
    var xRef = refXSamples[i];  // ← Use reference space directly
    var k = kReference[i];

    // Check if in sidecut region using REFERENCE space
    if (xRef >= refInflectionXMin && xRef <= refInflectionXMax)  // ← Same space!
    {
        scaledK = append(scaledK, k * radiusScaleFactor);  // ← Correct values
    }
    else
    {
        scaledK = append(scaledK, k);
    }
}
```

### Key Changes

1. **Declare transformed bounds** at iteration loop scope (lines 1707-1708)
2. **Transform inflection bounds** from NEW space to REFERENCE space (lines 1751-1752)
3. **Use reference coordinates** directly from `refXSamples[i]` (line 1757)
4. **Compare in same space:** reference X vs reference inflection bounds (line 1761)
5. **Add diagnostic output** to verify transformation (lines 1848-1850)

---

## Expected Results

### Before Fix
- Iteration converges to: 20.99m ✅
- Final output measures: 16.09m ❌
- Error: 4.9m (23%)

### After Fix (Expected)
- Iteration converges to: ~21.00m ✅
- Final output measures: ~20.7m ✅
- Error: ~0.3m (1.4%)

The remaining ~0.3m error is the inherent **splitting error** we've accepted as fundamental to the algorithm (from splitting continuous curvature into discrete curve segments).

---

## Verification Steps

1. Upload fixed code to Onshape
2. Run **Scale Footprint** feature (target=21m)
3. Check console output:
   - Iteration should converge to ~21.00m
   - Reference inflection bounds should be shown in both spaces
4. Run **Analyze Footprint** on output
5. Verify `avgRadius` ≈ 20.5-21.5m (within 0.5m tolerance)
6. Check inflection point stability (should match between iteration and output)

---

## Lessons Learned

### Coordinate Space Discipline

When working with multiple coordinate systems:
1. **Always document** which space each variable lives in
2. **Transform explicitly** when crossing boundaries
3. **Use consistent space** for related operations (sampling + filtering)
4. **Add assertions** or debug output to verify transformations

### The Bug Was Subtle Because

- Visual quality remained good (smooth curves, no kinks)
- Iteration algorithm converged correctly (error appeared elsewhere)
- The code "made sense" at first glance (indexing by `i` seems natural)
- Only numerical measurements revealed the error

### Prevention

- Comment coordinate spaces in variable names or declarations
- Test with extreme scale factors (2x, 0.5x) to amplify misalignments
- Add diagnostic output showing coordinates in multiple spaces
- Verify transformations with known test cases

---

## Related Files

- `footprint/scaleFootprint.fs` - Main implementation (FIXED)
- `.claude/qc-table-column-reorganization.md` - Related documentation
- `MEMORY.md` - Lessons learned archive

---

## Status

✅ **FIXED** - Coordinate space mismatch resolved
⏳ **PENDING** - Verification testing on Onshape
📝 **DOCUMENTED** - Fix rationale and expected results captured
