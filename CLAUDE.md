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
std/           - Core geometry, math, and utility modules (33 files)
tools/         - BSpline & geometry utilities (14 files, production-ready)
footprint/     - Footprint geometry analysis feature (archived)
gordonSurface/ - Gordon surface interpolation feature (archived)
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

### Core Tools (Production Ready)
| Module | Purpose |
|--------|---------|
| `tools/bspline_data.fs` | BSpline data extraction, validation, continuity |
| `tools/bspline_knots.fs` | Knot insertion/removal, degree elevation (P&T) |
| `tools/curve_operations.fs` | Split, join, extract curves with continuity |
| `tools/arc_length.fs` | Arc length computation, uniform sampling |
| `tools/frenet.fs` | Frenet frames, coordinate transformations |
| `tools/point_projection.fs` | Point-to-curve projection |
| `tools/solvers.fs` | Root finding (hybrid, Brent, Newton) |
| `tools/optimization.fs` | Gradient descent, LM, conjugate gradient |
| `tools/numerical_integration.fs` | Gaussian quadrature, trapz, Simpson |

### Standard Library
| Module | Purpose |
|--------|---------|
| `std/common.fs` | Master import (re-exports ~50 Onshape modules) |
| `std/evaluate.fs` | ev* functions (evDistance, evCurveTangent, etc.) |
| `std/query.fs` | q* functions (qEverything, qCreatedBy, etc.) |
| `std/splineUtils.fs` | approximateSpline, evaluateSpline |
| `std/transform.fs` | Transform operations |
| `std/vector.fs`, `std/matrix.fs` | Linear algebra |
