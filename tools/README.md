# Tools Library - Reusable BSpline and Math Utilities

This folder contains extracted, refactored, and documented utility functions from the `footprint/` and `gordonSurface/` feature studios. The code has been organized into domain-specific modules with comprehensive documentation.

## Implementation Status

### ✅ Phase 1: Foundation (COMPLETE)
- **assertions.fs** - Assertion and validation utilities
- **math_utils.fs** - General math utilities (sign, clamp, lerp, remap)
- **debug.fs** - Debug visualization (points, polylines, entities)
- **printing.fs** - Formatted console output for BSplines

### ✅ Phase 2: Core Math (COMPLETE)
- **numerical_integration.fs** - Integration methods (trapz, Simpson, Gaussian quadrature, adaptive)
- **solvers.fs** - Root finding (hybrid, Brent, Newton-Raphson, multi-dimensional)
- **transition_functions.fs** - Blending functions (linear, sinusoidal, logistic)
- **point_projection.fs** - Closest point queries on curves
- **arc_length.fs** - Arc-length parameterization and uniform sampling
- **optimization.fs** - Levenberg-Marquardt, conjugate gradient, gradient descent

### ✅ Phase 3: BSpline Data (COMPLETE)
- **bspline_data.fs** - Data extraction, validation predicates, continuity analysis
  - Predicates: `isClamped()`, `isBezier()`, `hasValidKnotOrder()`, `isValidBSplineStructure()`
  - Accessors: `getDegree()`, `getNumControlPoints()`, `getWeights()`, `isRational()`, `isPeriodic()`
  - Continuity: `getContinuityAtKnot()`, `getMinInteriorContinuity()`, `hasMinContinuity()`
  - Utilities: `estimateCurveLength()`, `checkEndpointConnection()`
- **frenet.fs** - Frenet frame computation and transformations

### ✅ Phase 4: BSpline Operations (MOSTLY COMPLETE)
- **bspline_knots.fs** ✅ - Comprehensive knot and curve operations (P&T algorithms)
  - P&T A2.1: `findSpan()` - Binary knot span search
  - P&T A5.1: `insertKnot()` - Boehm's single knot insertion
  - P&T A5.4: `refineKnotVector()` - Multiple knot insertion
  - P&T A5.6: `removeKnot()` - Knot removal with tolerance
  - P&T A5.9: `elevateDegree()` - Degree elevation
  - Utility: `reverseCurve()`, `makeCurvesCompatible()`
- **bspline_compatibility.fs** ⏳ - (Mostly covered by `makeCurvesCompatible()` in bspline_knots.fs)
- **bspline_continuity.fs** ⏳ - (Mostly covered by continuity functions in bspline_data.fs)
- **bspline_special.fs** ⏳ - NOT YET IMPLEMENTED

### ⏳ Phase 5: Surface Operations (NOT STARTED)
- **bspline_surface_data.fs** ⏳ - NOT YET IMPLEMENTED
- **bspline_surface_ops.fs** ⏳ - NOT YET IMPLEMENTED

## Dependency Graph

```
Phase 1 (no dependencies):
  assertions.fs
  math_utils.fs
  debug.fs
  printing.fs

Phase 2:
  numerical_integration.fs → assertions
  solvers.fs → assertions, math_utils
  transition_functions.fs (no dependencies)
  point_projection.fs → assertions, solvers, bspline_data, math_utils
  arc_length.fs → assertions, math_utils, bspline_data, solvers, numerical_integration
  optimization.fs → assertions, math_utils

Phase 3:
  bspline_data.fs (no dependencies)
  frenet.fs (no dependencies)

Phase 4:
  bspline_knots.fs → bspline_data
  bspline_compatibility.fs → bspline_data, bspline_knots (NOT YET IMPLEMENTED)
  bspline_continuity.fs → frenet, bspline_data (NOT YET IMPLEMENTED)
  bspline_special.fs → solvers, transition_functions (NOT YET IMPLEMENTED)

Phase 5:
  bspline_surface_data.fs (no dependencies) (NOT YET IMPLEMENTED)
  bspline_surface_ops.fs → bspline_surface_data, bspline_compatibility (NOT YET IMPLEMENTED)
```

## Usage

### Import Pattern
```featurescript
// Import tools (exact paths depend on your Onshape document structure)
import(path : "tools/assertions.fs", version : "");
import(path : "tools/math_utils.fs", version : "");
import(path : "tools/bspline_data.fs", version : "");
// etc.
```

### Example: BSpline Analysis
```featurescript
// Get parameter range
var range = getBSplineParamRange(myCurve);
println("Curve parameter range: [" ~ range.uMin ~ ", " ~ range.uMax ~ "]");

// Get bounding box
var bounds = getBSplineBounds(myCurve);
println("X range: [" ~ bounds.xMin ~ ", " ~ bounds.xMax ~ "]");

// Get interior knots
var interior = getInteriorKnots(myCurve);
println("Interior knots: " ~ interior);
```

### Example: Curve Validation
```featurescript
// Check curve properties
if (!isClamped(myCurve))
    throw regenError("Curve must be clamped");

if (!isValidBSplineStructure(myCurve))
    throw regenError("Invalid BSpline structure");

// Check continuity requirements
var minContinuity = getMinInteriorContinuity(myCurve);
println("Minimum continuity: C" ~ minContinuity);

if (hasMinContinuity(myCurve, 1))
    println("Curve is at least C1 continuous");
```

### Example: Knot Operations
```featurescript
// Insert a single knot
var refined = insertKnot(context, myCurve, 0.5);

// Insert multiple knots
var multiRefined = refineKnotVector(context, myCurve, [0.25, 0.5, 0.75]);

// Elevate degree (cubic to quartic)
var elevated = elevateDegree(context, myCurve, 1);

// Remove a knot within tolerance
var result = removeKnot(context, myCurve, 0.5, 1, 1e-4 * meter);
if (result.success)
    myCurve = result.curve;

// Reverse curve direction
var reversed = reverseCurve(myCurve);

// Make two curves compatible for lofting
var compat = makeCurvesCompatible(context, curveA, curveB);
curveA = compat.curveA;
curveB = compat.curveB;
```

### Example: Frenet Frame
```featurescript
// Compute Frenet frame at parameter 0.5
var frame = computeFrenetFrame(myCurve, 0.5);

// Transform offset from Frenet to world
var localOffset = vector(1*mm, 0.5*mm, 0*mm);  // [tangent, normal, binormal]
var worldOffset = frenetVectorToWorld(localOffset, frame);
```

### Example: Debug Visualization
```featurescript
// Show control points
addDebugPoints(context, myCurve.controlPoints, DebugColor.CYAN);

// Show control polygon
showPolyline(context, myCurve, DebugColor.MAGENTA);

// Print curve data
printBSpline(myCurve, PrintFormat.DETAILS, ["=== My Curve ==="]);
```

### Example: Point Projection
```featurescript
// Find closest point on curve to external point
var result = projectPointOnCurve(myCurve, queryPoint, {});
var closestPt = result.point;
var distance = result.distance;
var u = result.parameter;
```

### Example: Arc-Length Parameterization
```featurescript
// Build arc-length table (do once per curve)
var table = buildArcLengthTable(myCurve, 200);
println("Total length: " ~ table.totalLength);

// Get parameter at 50% of arc length (curve midpoint by distance)
var uMid = parameterAtArcLength(table, table.totalLength * 0.5);

// Sample 100 uniformly-spaced points along curve
var samples = uniformArcLengthSamples(myCurve, 100, {});
```

### Example: Numerical Integration
```featurescript
// Gaussian quadrature for smooth function (high accuracy, few evaluations)
var integral = gaussQuadrature(function(x) { return sin(x); }, 0, PI, 5);

// Adaptive quadrature with error control (auto-refines where needed)
var result = adaptiveQuadrature(f, 0, 1, 1e-8, 15);
println("Integral: " ~ result.integral ~ " (error: " ~ result.error ~ ")");
```

### Example: Root Finding
```featurescript
// Brent's method for difficult brackets
var root = solveRootBrent(function(x) { return x*x*x - 2*x - 5; }, 2, 3, 1e-10, 50);

// Newton's method when derivative is available
var root = solveRootNewton(
    function(x) { return x*x - 2; },
    function(x) { return 2*x; },
    1.5, 1e-12, 30);

// Multi-dimensional Newton for systems
var result = newtonND(F, J, [0.5, 0.5], 1e-10, 30);
```

### Example: Optimization
```featurescript
// Levenberg-Marquardt for curve fitting
var result = levenbergMarquardt(residuals, jacobian, initialParams, {});
var fittedParams = result.x;

// Conjugate gradient for smoothing
var result = conjugateGradient(fairnessObjective, fairnessGradient, x0, {});
```

## Key Features

### Piegl & Tiller Algorithms
- **findSpan** (A2.1) - Binary search for knot span
- **insertKnot** (A5.1) - Boehm's knot insertion
- **refineKnotVector** (A5.4) - Multi-knot insertion
- **removeKnot** (A5.6) - Knot removal with tolerance control
- **elevateDegree** (A5.9) - Degree elevation for curve compatibility

All algorithms preserve curve shape exactly (within tolerance for removal) and are documented with P&T references.

### Robust Numerical Methods
- Multiple root solvers: Hybrid, Brent, Newton-Raphson, multi-dimensional Newton
- Integration methods: Trapezoidal, Simpson, Gaussian quadrature, adaptive
- Point projection using orthogonality root finding
- Arc-length parameterization with efficient inverse lookup
- Optimization: Levenberg-Marquardt, conjugate gradient, gradient descent
- Epsilon-based tolerances for numerical stability

### Frenet Frame Support
- Handles degenerate cases (zero curvature, straight lines)
- Full bidirectional transformations (Frenet ↔ World)
- Consistent frame convention: zAxis=tangent, xAxis=normal, yAxis=binormal

## Design Principles

1. **No Code Duplication** - Single source of truth for all algorithms
2. **Pure Functions** - Most functions are context-free (pure data operations)
3. **Unit Safety** - Correctly handles ValueWithUnits throughout
4. **Comprehensive Documentation** - Every function has detailed docs with examples
5. **Algorithm References** - P&T algorithm numbers and page references included
6. **Source Traceability** - Every function documents its original source file

## Testing & Validation

Each function should be validated by:
1. Comparing output with original implementation
2. Testing edge cases (empty arrays, zero curvature, etc.)
3. Verifying unit handling (meters vs millimeters)
4. Checking tolerance behavior

See `conversionNotes.md` for detailed implementation notes.

## Remaining Work

### Priority 1: Complete Phase 4
- **bspline_compatibility.fs** - Curve degree elevation, knot unification
- **bspline_continuity.fs** - G1/G2 continuity enforcement
- **bspline_special.fs** - Joining segments, subcurves, Y-crossings, scaled curves

### Priority 2: Implement Phase 5
- **bspline_surface_data.fs** - Surface row/column extraction
- **bspline_surface_ops.fs** - Transpose, degree elevation, skinning

## References

1. **Piegl, L. & Tiller, W.** - "The NURBS Book" 2nd Ed. (1997)
   - Primary reference for knot algorithms
2. **Original Sources**:
   - `footprint/fpt_math.fs` - Math utilities and solvers
   - `footprint/fpt_analyze.fs` - BSpline analysis functions
   - `gordonSurface/gordonKnotOps.fs` - P&T knot algorithms
   - `gordonSurface/modifyCurveEnd.fs` - Frenet frames, continuity
   - `gordonSurface/gordonCurveCompatibility.fs` - Curve compatibility
   - `gordonSurface/scaledCurve.fs` - Printing, transition functions

## Contributing

When adding new utilities:
1. Place in appropriate phase/domain file
2. Include full documentation with examples
3. Document source file and line numbers
4. Add algorithm references where applicable
5. Update this README and conversionNotes.md
6. Test thoroughly before committing
