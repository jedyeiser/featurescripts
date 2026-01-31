# Tools Folder Conversion Notes

This document tracks design decisions, issues, and patterns discovered during the extraction of utilities from `footprint/` and `gordonSurface/` into the `tools/` library.

---

## Context Dependencies

### Functions Requiring Context Parameter

Functions need `context` when they:
1. **Create geometry**: `opFitSpline`, `opPoint`, etc.
2. **Query geometry**: `evDistance`, `evEdgeTangentLine`, `evaluateQuery`, etc.
3. **Perform approximations**: `approximateSpline` (converts exact geometry to BSpline)
4. **Generate IDs**: All geometry operations need unique IDs via `context`

### Pattern for Context Usage
```featurescript
// When context is needed:
export function needsContext(context is Context, id is Id, ...)
{
    opFitSpline(context, id, {...});
}

// When context is NOT needed (pure computation):
export function pureFunction(bspline is BSplineCurve, ...)
{
    // Only operates on data structures
}
```

---

## Unit Handling

### ValueWithUnits Patterns

1. **Tolerances**: Often specified in meters but compared in millimeters
   ```featurescript
   const KNOT_TOLERANCE = 1e-7 * meter;  // Storage
   roundToTolerance(value, KNOT_TOLERANCE / meter)  // Comparison
   ```

2. **Unit-safe operations**:
   - Use `abs()`, `min()`, `max()` which preserve units
   - Multiply/divide by unitless values to preserve units
   - Strip units for trigonometric functions

3. **Array operations with units**:
   ```featurescript
   cumTrapz(x is array, y is array)  // Both can have units
   returns map { cumulative: array, total: ValueWithUnits }
   ```

---

## FeatureScript Limitations

### No Function Closures with Context
Cannot pass context into lambda functions:
```featurescript
// DOESN'T WORK:
const f = function(u) { return evDistance(context, {...}); };

// WORKAROUND: Pre-sample or pass context explicitly
const samples = mapArray(params, function(u) {
    return evDistance(context, {...});
});
```

### Array Immutability
Arrays/maps are passed by value. Modifications require reassignment:
```featurescript
var arr = [1, 2, 3];
arr = append(arr, 4);  // Must reassign
```

### Type System Constraints
- No union types or generics
- Use function overloading for multiple types
- Predicates pattern: `is BSplineCurve`, `is array`, etc.

---

## Algorithm References

### Piegl & Tiller (P&T) "The NURBS Book"

Referenced algorithms:
- **A2.1**: `findSpan` - Binary search for knot span (p. 68)
- **A5.1**: `insertKnot` - Boehm's knot insertion (p. 151)
- **A5.4**: `refineKnotVector` - Insert multiple knots (p. 164)

### Custom Algorithms

1. **Hybrid Root Solver** (`solvers.fs`)
   - Combines secant method (fast) with bisection (robust)
   - Falls back to bisection when secant diverges
   - Source: `footprint/fpt_math.fs:94-171`

2. **Frenet Frame Computation** (`frenet.fs`)
   - Uses Onshape's `evEdgeCurvature` for robust frame
   - Convention: zAxis=tangent, xAxis=normal, yAxis=binormal
   - Handles zero-curvature case (straight segments)
   - Source: `gordonSurface/modifyCurveEnd.fs`

---

## Breaking Changes

### Function Renames
*Track any renamed functions here as extraction proceeds*

### Parameter Changes
*Track any parameter order or type changes*

### Return Type Modifications
*Track any return type changes (e.g., single value → map)*

---

## Import Patterns

### Import Configuration
Import configuration will be managed in feature studios using the tools library. The exact import paths depend on the Onshape environment setup.

### Expected Import Pattern
```featurescript
// In feature studios using tools:
import(path : "tools/assertions.fs", version : "");
import(path : "tools/math_utils.fs", version : "");
// ... etc
```

### Dependency Graph
```
Phase 1 (no dependencies):
  - assertions.fs
  - math_utils.fs
  - debug.fs
  - printing.fs

Phase 2:
  - numerical_integration.fs → assertions
  - solvers.fs → assertions, math_utils
  - transition_functions.fs (no dependencies)

Phase 3:
  - bspline_data.fs (no dependencies)
  - frenet.fs (no dependencies)

Phase 4:
  - bspline_knots.fs → bspline_data
  - bspline_compatibility.fs → bspline_data, bspline_knots
  - bspline_continuity.fs → frenet, bspline_data
  - bspline_special.fs → solvers, transition_functions

Phase 5:
  - bspline_surface_data.fs (no dependencies)
  - bspline_surface_ops.fs → bspline_surface_data, bspline_compatibility
```

---

## Testing Strategy

### Validation Approach
For each phase:
1. Extract function with full documentation
2. Create simple test case (document in this file)
3. Compare with original implementation where applicable
4. Document any differences or edge cases discovered

### Test Case Format
```
## Test: [Function Name]
**Input**: ...
**Expected Output**: ...
**Actual Output**: ...
**Status**: ✓ Pass / ✗ Fail / ⚠ Needs Review
**Notes**: ...
```

---

## Phase 1: Foundation Files ✓ COMPLETE

### assertions.fs ✓
**Status**: Complete
**Source**: `footprint/fpt_math.fs:10-14`
**Functions**:
- `assertTrue(cond, msg)` - General assertion
- `assertPositive(val, msg)` - Validate positive values
- `assertInRange(val, min, max, msg)` - Range validation
- `assertNonEmpty(arr, msg)` - Array validation

### math_utils.fs ✓
**Status**: Complete
**Source**: `footprint/fpt_math.fs:16-28`
**Functions**:
- `safeSign(x, eps)` - Robust sign with tolerance
- `clamp01(u)` - Clamp to [0,1]
- `clamp(val, min, max)` - General clamping
- `lerp(a, b, t)` - Linear interpolation
- `remap(val, inMin, inMax, outMin, outMax)` - Value remapping

### debug.fs ✓
**Status**: Complete
**Source**: `gordonSurface/gordonKnotOps.fs`
**Functions**:
- `addDebugPoints(context, pointArray, debugColor)` - Visualize points
- `showPolyline(context, bspline, debugColor)` - Show control polygon
- `showParamOnCurve(context, curveQuery, param, debugColor)` - Show parameter
- `addDebugEntities(context, query, debugColor)` - Show existing geometry

### printing.fs ✓
**Status**: Complete
**Source**: `gordonSurface/scaledCurve.fs`
**Enums**: `PrintFormat` (METADATA, DETAILS)
**Functions**:
- `printBSpline(curve, format, tags)` - Print curve data
- `printCurveArray(curves, title, format)` - Print multiple curves
- `formatVector(v)` - Pretty-print vectors
- `roundDecimal(value, places)` - Round for display

---

## Phase 2: Core Math Files ✓ COMPLETE

### numerical_integration.fs ✓
**Status**: Complete
**Source**: `footprint/fpt_math.fs:34-87`
**Functions**:
- `cumTrapz(x, y, initialValue)` - Cumulative trapezoidal integration
- `trapz(x, y)` - Simple integration (total only)
- `movingAverage(y, window)` - Smoothing filter

### solvers.fs ✓
**Status**: Complete
**Source**: `footprint/fpt_math.fs:94-171`
**Functions**:
- `bracketFromSamples(samples)` - Find sign-change bracket
- `solveRootHybrid(f, a, b, tol, maxIter)` - Hybrid secant/bisection solver

### transition_functions.fs ✓
**Status**: Complete
**Source**: `gordonSurface/modifyCurveEnd.fs`, `scaledCurve.fs`
**Enums**: `TransitionType` (LINEAR, SINUSOIDAL, LOGISTIC)
**Functions**:
- `linearTransition(t)` - Linear blend
- `sinusoidalTransition(t)` - Smooth S-curve (C1)
- `logisticTransition(t)` - Sigmoid blend (C∞)
- `evaluateTransition(t, type)` - Dispatcher
- `computeAppliedSF(s, sfStart, sfEnd, transitionType)` - Scale factor blend

---

## Phase 3: BSpline Data Files ✓ COMPLETE

### bspline_data.fs ✓
**Status**: Complete
**Source**: `footprint/fpt_analyze.fs`, `gordonSurface/gordonCurveCompatibility.fs`
**Functions**:
- `getBSplineParamRange(bspline)` - Extract parameter bounds
- `getBSplineBounds(bspline)` - Sampling-based bounding box (25 samples)
- `getInteriorKnots(bspline)` - Extract interior knots (excluding clamped ends)
- `getBSplineEndpoints(bspline)` - Get start/end points
- `getBSplineKnotMultiplicities(bspline, tolerance)` - Count knot multiplicities

### frenet.fs ✓
**Status**: Complete
**Source**: `gordonSurface/modifyCurveEnd.fs`
**Constants**: `FRENET_EPSILON = 1e-5`
**Functions**:
- `computeFrenetFrame(curve, s)` - Calculate Frenet frame + curvature
- `frenetVectorToWorld(localVector, frenetResult)` - Transform direction to world
- `worldVectorToFrenet(worldVector, frenetResult)` - Transform direction to Frenet
- `frenetPointToWorld(localPoint, frenetResult)` - Transform point to world
- `worldPointToFrenet(worldPoint, frenetResult)` - Transform point to Frenet

**Frame Convention**: zAxis=tangent, xAxis=normal, yAxis=binormal

---

## Phase 4: BSpline Operations (PARTIAL - 1/4 complete)

### bspline_knots.fs ✓
**Status**: Complete
**Source**: `gordonSurface/gordonKnotOps.fs`
**References**: Piegl & Tiller Algorithms A2.1, A5.1, A5.4
**Functions**:
- `findSpan(degree, u, knots)` - P&T A2.1: Binary search for knot span
- `insertKnot(context, bSpline, knotParam)` - P&T A5.1: Boehm's knot insertion
- `refineKnotVector(context, bSpline, knotsToInsert)` - P&T A5.4: Multi-knot insertion
- `mergeKnotVectors(knotsA, knotsB, tolerance)` - Merge with max multiplicities
- `sanitizeKnotsToInsert(existingKnots, knotsToInsert, degree, tolerance)` - Filter invalid knots
- `roundToTolerance(value, tolerance)` - Helper for knot comparison

**Notes**:
- All algorithms preserve curve shape exactly
- Handles rational curves correctly (weights interpolated same as control points)
- Uses tolerance-based comparisons (default 1e-8)
- Automatically prevents knot multiplicity > degree

### bspline_compatibility.fs ⏳
**Status**: NOT YET IMPLEMENTED
**Source**: `gordonSurface/gordonCurveCompatibility.fs`
**Planned Functions**:
- `makeCurvesCompatible(context, id, curves)` - Make curves compatible for lofting
- `compatibilityReport(curves)` - Diagnostic utility
- `allEqual(arr)` - Helper for checking consistency

### bspline_continuity.fs ⏳
**Status**: NOT YET IMPLEMENTED
**Source**: `gordonSurface/modifyCurveEnd.fs`
**Planned Enums**: `ContinuityType` (G0, G1, G2), `G2Mode` (EXACT, BEST_EFFORT)
**Planned Functions**:
- `enforceG1AtEnd(curve, endParam, targetTangent)` - Adjust control points for G1
- `enforceG2AtEnd(curve, endParam, targetCurvature, g2Mode)` - G2 continuity
- `computeRefContinuityConstraints(context, ref, curve, endParam)`
- `computeEdgeContinuityConstraints(context, edge, point)`
- `computeFaceContinuityConstraints(context, face, curve, endParam)`

### bspline_special.fs ⏳
**Status**: NOT YET IMPLEMENTED
**Source**: Multiple files (`fpt_analyze.fs`, `modifyCurveEnd.fs`, `scaledCurve.fs`)
**Planned Functions**:
- `joinCurveSegments(context, curveArray, numSamples, tolerance)` - Join segments into single curve
- `extractBSplineSubcurve(context, bspline, uStart, uEnd, numPoints)` - Extract portion of curve
- `findBSplineYCrossings(bspline, yTarget, tolerance)` - Find Y=const crossings
- `scaledCurve(context, curve0, curve1, flip, sf0, sf1, transitionType, numSamples, degree, tol)` - Blend curves

---

## Phase 5: Surface Operations (NOT STARTED)

### bspline_surface_data.fs ⏳
**Status**: NOT YET IMPLEMENTED
**Source**: `gordonSurface/gordonSurface.fs`
**Planned Functions**:
- `extractSurfaceRow(surface, rowIndex)` - Get control points for row
- `extractSurfaceColumn(surface, colIndex)` - Get control points for column
- `extractSurfaceRowWeights(surface, rowIndex)`
- `extractSurfaceColumnWeights(surface, colIndex)`
- `surfaceRowToCurve(surface, rowIndex)` - Convert row to BSplineCurve
- `surfaceColumnToCurve(surface, colIndex)` - Convert column to BSplineCurve
- `getWeight(surface, i, j)` - Extract weight at index

### bspline_surface_ops.fs ⏳
**Status**: NOT YET IMPLEMENTED
**Source**: `gordonSurface/gordonSurface.fs`, `simplifySurface.fs`
**Planned Functions**:
- `transposeSurface(surface)` - Swap U/V directions
- `elevateSurfaceUDegree(surface, newDegree)`
- `elevateSurfaceVDegree(surface, newDegree)`
- `createSkinningSurface(context, curves, ...)` - Skin through curves
- `createTensorProductSurface(context, uCurves, vCurves, ...)`

---

## Discovered Issues

*Track any unexpected issues, workarounds, or design decisions here*

---

## Future Enhancements

*Ideas for improvements or additional utilities to add*
