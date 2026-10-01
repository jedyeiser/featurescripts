# FeatureScript Development Project

Local development environment for Onshape FeatureScript CAD geometry code. Code is written here and tested by copying to Onshape FeatureStudio.

**Target**: the local `std/` mirror is FeatureScript 2878 (`std/featurescriptversionnumber.gen.fs`); `tools/` is written against it.
The active documents (driven_offset, curve_tools, variable_tools, reference_side, publish_tools, curveMapping) are on
FeatureScript 3070; curve_tools/fillet_wire.fs, curveMapping and publish_tools import std 3083. Use the version already
at the top of the file you edit. `fscheck.py` resolves names against the 2878 mirror, so a newer std function shows as
undefined there -- confirm it exists before changing code.

## Critical Rules

1. **Export by default** - Always use `export` for functions, constants, and enums in utility files
2. **No namespace imports** - FeatureScript does NOT support `import ... as namespace` or `::` qualification
3. **Check corrections log** - Before generating code read the top of `.claude/featurescript-corrections.md` (Critical top 10 + Quick index, ~1k tokens), then open only the entries for your topic; do NOT read the whole 2200-line file
4. **Prefer native Onshape** - Use built-in `op*` and `ev*` functions over custom implementations

## Project Structure

```
std/           - Local mirror of Onshape's std library (264 files, FS 2878); fscheck.py resolves names against it
tools/         - BSpline & geometry utilities (13 files, production-ready; no README -- see the table below)
footprint/     - Footprint geometry analysis feature (archived)
gordonSurface/ - Gordon surface interpolation feature (archived)
driven_offset/ - Driven edge offset / surface (Design_Master document) -- read driven_offset/DOCUMENT_MAP.md first
curve_tools/   - Curve_tools document: curve_core + Clean wire, Map curve, Merge curve, Evaluate profiles, fillet_wire
variable_tools/- Variable_tools document: Extract variables + extract_outputs (producer library)
reference_side/- Reference_Side_Features document: Mutual Trim+, Split+, Offset+, Thicken+ (tests in-tree: devtools/onshape/*_reference_side_tests.py)
publish_tools/ - Publish & Drawing tools document: station-geometry / drawing composites (test doc 73271cfc)
devtools/onshape/ - eval API runner, feature-insertion helpers (fsapi.py), build_/check_ live test pairs, repin, Design_Master fingerprint
curveMapping/, curveMapping_public/ - wrapAndLoft, wrapCurve, deform + core (private dev document / public copy)
smallTools/, case_pattern/, xSection/, qcTable/, example_1/ - other documents; see docs/STATUS.md and the memory index
archive/       - superseded docs and dumps (archive/docs_pre_2026-09/README.md); read only for history
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

## Onshape API budget (read before any live call)

The account has a YEARLY REST API quota (enterprise: 10,000 calls per user per year; API-key and OAuth calls
count). 25% was used by 2026-09-30, mostly by agents' live test runs and polling. Rules:
- Static checks first (`fscheck.py`, reading code); live calls only to confirm.
- One FeatureScript eval that returns everything beats many small calls; cache element/part lists in a run.
- Run targeted live checks while iterating; the full live suite once at the end, not after every edit.
- Save API responses as fixtures and test offline; poll with backoff, never tight loops.
- Agents get an explicit live-call budget (default <= 50 per task) and stop and report if they need more.
- The browser tools (`--check`, `notices --monitor`, Playwright) use the web session (most likely not counted,
  unverified) but are heavy -- use them sparingly too. `check_project` still makes a few REST calls per run.

Tooling that enforces this (2026-09-30; only you and the agents use the API, so this is all ours to control):
- **Ledger**: every call through `sync/core/client.py` `OnshapeClient` is logged to `.api-calls.jsonl` (gitignored;
  caller, method, path with ids masked, status). Report: `python -m sync.core.callstats --days 7`. Set
  `ONSHAPE_CALLER=<label>` to name a run. Only 2xx/3xx count against the quota.
- **Cap**: `ONSHAPE_CALL_CAP=<n>` makes a process refuse its (n+1)th call. Set it on every agent-run script
  (analysis: 0; live checks: <= 50). Raise it deliberately, never by removing it.
- **Writing features**: use `devtools/onshape/fsapi.py` `write_feature` / `delete_feature` / `feature_status` (one
  features GET per studio, then the cache follows each write's own response: N features = N + 1 calls, not 2N).
  Never re-GET the feature tree after a write; the POST response already has the status. The `build_*_tests.py`
  scripts all use it; new ones must too.
- **Pushing tabs**: `repin.py push` skips a tab that is already current and, after a client timeout, waits and
  verifies instead of re-posting. `OnshapeClient.get_document_info` is memoised per process.
- **eocProductData**: `backend/onshape_api/call_ledger.py` logs `onshape_call ...` lines on the `onshape.calls`
  logger; translation polling backs off (2 s x1.5, max 10 s).

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

## Live testing (each feature has a build/check pair)

`devtools/onshape/build_<x>_tests.py` inserts real test features into the document's test Part Studio (features named by
case + expected result); `check_<x>_tests.py` regenerates and reads results with one FeatureScript eval per batch.
Run as `PYTHONPATH=. python devtools/onshape/<script>.py` from the repo root (`FS_SYNC_TIMEOUT=120` for heavy studios).
Pairs: arc_tangency(+deo), case_pattern, composite, deo_regions, evaluate_offset, evaluate_profiles, extract_tracking,
fillet_wire (check only), footprint, gordon, move_along_edge, offset_edges, offset_profile, primitive, reference_side,
trim_curve_plus, unwrap(+hole_face, parts), utility, xsection. Build once, check often; both cost API calls -- see
"Onshape API budget" above. Tests live in the feature tree, not in harness tabs (memory: feedback_tests_in_feature_tree).
Cross-document imports are pinned by VERSION and only the user creates versions; to test unversioned library code,
copy the tabs into a scratch document (same-document imports work by microversion) -- ask before creating scratch documents.

## Documentation

- `.claude/agents/featurescript-expert.md` - Full FeatureScript expertise (auto-invoked on .fs files)
- `.claude/featurescript-corrections.md` - Living log of known issues and fixes
- `docs/STATUS.md` - which features have explainers / briefs / decks and their state (generated from docs/status.json)
- `archive/docs_pre_2026-09/README.md` - what was archived and why (old PROJECT.md, February fix notes, old curveMapping design)
- `driven_offset/docs/unwrap_explained.md` - Unwrap / undrape explained end to end (theory, use, code map, tests)

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
