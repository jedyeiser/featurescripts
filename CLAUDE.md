# FeatureScript Development Project

Local development environment for Onshape FeatureScript CAD geometry code. Code is written here and tested by copying to Onshape FeatureStudio.

**Target**: FeatureScript 2878 standard

## Critical Rules

1. **Export by default** - Always use `export` for functions, constants, and enums in utility files
2. **No namespace imports** - FeatureScript does NOT support `import ... as namespace` or `::` qualification
3. **Check corrections log** - Read `.claude/featurescript-corrections.md` before generating code
4. **Prefer native Onshape** - Use built-in `op*` and `ev*` functions over custom implementations

## Project Structure

```
std/           - Core geometry, math, and utility modules (22 files)
tools/         - Extracted BSpline utilities (10 files, 67% complete)
footprint/     - Footprint geometry analysis feature (9 files)
gordonSurface/ - Gordon surface interpolation feature (8 files)
```

## Import Pattern

```featurescript
// Correct - no namespace aliasing
import(path : "tools/math_utils.fs", version : "");
import(path : "tools/bspline_data.fs", version : "");

// Functions are directly available (no prefix)
var result = getBSplineParamRange(myCurve);
```

## Key Conventions

- **Units**: All geometric values use `ValueWithUnits` - e.g., `5 * meter`, `90 * degree`
- **Tolerances**: Use `TOLERANCE.zeroLength` (~1e-7m) and `TOLERANCE.zeroAngle` (~1e-7 rad)
- **Vectors**: Position vectors have units; direction vectors are unitless
- **Imports**: Use comment placeholders `// IMPORT: filename.fs` - actual document IDs managed separately

## Development Workflow

1. Write code in `.fs` files locally
2. Copy to Onshape FeatureStudio for testing
3. Fix issues, update local code
4. Update corrections log with any new issues discovered

## Documentation

- `PROJECT.md` - Detailed project overview
- `.claude/agents/featurescript-expert.md` - Full FeatureScript expertise (auto-invoked on .fs files)
- `.claude/featurescript-corrections.md` - Living log of known issues and fixes
- `tools/README.md` - Tools library status and usage

## Quick Reference

| Module | Purpose |
|--------|---------|
| `std/math.fs` | Math utilities, predicates, matrices |
| `std/surfaceGeometry.fs` | Surface creation and manipulation |
| `std/curveGeometry.fs` | Curve operations |
| `std/nurbsUtils.fs` | NURBS utilities |
| `tools/bspline_data.fs` | BSpline analysis (bounds, ranges, knots) |
| `tools/bspline_knots.fs` | Knot insertion (P&T algorithms) |
| `tools/frenet.fs` | Frenet frame computation |
