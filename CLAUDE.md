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
driven_offset/ - Driven edge offset / surface (Design_Master document) -- read driven_offset/DOCUMENT_MAP.md first
curve_tools/   - Curve_tools document: curve_core + Clean wire, Map curve, Merge curve, Evaluate profiles
variable_tools/- Variable_tools document: Extract variables + extract_outputs (producer library)
reference_side/- Reference_Side_Features document: Mutual Trim+, Split+, Offset+ (+ test harness)
devtools/onshape/ - eval API runner, feature-insertion helpers, Design_Master regression fingerprint
```

These four documents import each other: curve_core and extract_outputs are pinned by
Onshape VERSION, and only the user creates versions. Import chains, legacy tabs and
re-pin procedure: `driven_offset/DOCUMENT_MAP.md`.

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
2. **Run `python fscheck.py <files>` before pushing** - static checks that catch the
   errors that otherwise cost an Onshape round trip (see below)
3. Push with `python -m sync.main pushproject <project> --files <f.fs> --check` - the
   `--check` reads Onshape's real compile notices back (see below). A new local `.fs`
   file is created in Onshape automatically and placed in the tab folder named after its
   local subdirectory (`proj/sub/x.fs` -> tab folder `sub`; `proj/x.fs` -> root)
4. `python -m sync.main notices <project> --monitor "<Part Studio>"` to regenerate a
   test Part Studio and read runtime errors (with stack traces), `println` output and
   per-feature status
5. Fix issues, update local code
6. Update corrections log with any new issues discovered

### Onshape notices - `pushproject --check` / `notices`

```bash
python -m sync.main login                                   # once; saves browser session
python -m sync.main pushproject driven_offset --files merge_curve.fs --check
python -m sync.main notices driven_offset --monitor "Design Master"
```

Drives a headless browser to the document and scrapes the FeatureScript notices pane,
which the REST API does not expose. Output is `tab:line:col  message` with severity;
`--monitor` adds runtime notices, console output and feature OK/INFO/WARNING/ERROR.
Onshape rates "function not found" a *warning*, so `--check` is strict (fails on
warnings). ~15 s for compile notices, ~35 s with a monitored regen. Details in
`sync/README.md`.

### fscheck.py - pre-push static checker

```bash
python fscheck.py driven_offset/*.fs      # pass ALL tabs of a document together
```

Catches: calls to functions that do not exist, wrong argument counts, duplicate
definitions with the same name and arity ("Multiple visible overloads with identical
signature"), dot access on reserved words (`m.type` must be `m["type"]`), unbalanced
braces, non-ASCII bytes, a BOM. Resolves names against the local `std/` mirror, so
keep that current. Exits non-zero on any finding.

It does NOT type-check, unit-check, or verify enum reachability or runtime behaviour.
Clean means "worth pushing", not "correct".

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
