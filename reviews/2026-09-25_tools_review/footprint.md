# Footprint suite track report

## 1. Bottom line

- **Order of work: tests first, then fixes, then cleanup.** None of the five features has regression tests, and none of them reports anything to the user: `reportFeature*` appears 0 times across all 12 footprint files (I grepped it). The first job is a "Footprint tests" Part Studio with golden captures made from the current code. Several fixes below change output numbers, and without goldens nobody can tell a fix from a regression.
- **Scale Footprint and Arc fit have real correctness bugs, and I confirmed them in the code:**
  - Scale Footprint rebuilds B-splines without their knot vectors (for example `scaleFootprint.fs:1255`, and a comment at `2085` saying so on purpose).
  - `findParamAtX` returns the middle of the curve when it misses its target (`:1209`).
  - The inward search direction is chosen from the sign of X, which assumes the ski is centred on X=0 (`:1582`).
  - Arc fit silently throws away short blocks of curve, which leaves gaps (`arcFit.fs:953`).
  - Arc fit's Tangency tol does nothing: the only code path that reads it (`:1405-1423`) can never run.
- **Analyze footprint is the most useful feature and currently misleads.** Its numbers are only written by editing logic, so they go stale when upstream geometry changes. The Tip length and Tail length rows never appear, because `definition.hasTipTail` is never written (I grepped for it). Publishing its values through variable_tools from the feature body fixes both the staleness and the downstream wiring.
- **About 1,100 lines of dead code or duplication in the libraries, plus about 600 lines duplicated inside `scaleFootprint.fs`.** `footprintAnalytics.fs` can only be deleted after its unused import at `getFootprintPoints.fs:4` is removed (confirmed). The memory note that says nothing imports it is wrong.
- **Icons:** Arc fit and Generate Footprint Points have none. The other three have icons, but only as blob tabs in Onshape, with no SVG source in `icons/`.

## 2. Per-feature verdicts

| Feature | State | Biggest problems | Effort |
|---|---|---|---|
| Analyze footprint (`analyzeFootprint.fs`) | Works, but values go stale | Values written only by editing logic; `hasTipTail` never set, so tip/tail rows are hidden; about 210 dead lines (235-446); tip/tail block duplicated (134-145); "Decription" typo (110); no notices | S |
| Arc fit (`arcFit.fs`, 2042 lines) | Core geometry is sound; parameters and edges are not | Silent minLength drop (953); tanTol, the join logic and planeTol do nothing; bounds defaults (posTol 1e-4 mm) sit at the kernel floor, so every edge subdivides to maximum depth ("runs forever"); no exact Circle/Line fast path; tolerance misses silent; arcs over 180 deg can't be kept whole; no icon | M |
| Generate Footprint Points (`getFootprintPoints.fs`) | Works for the tip-at-lower-X convention | Lower X is always taken as FCP (127-152); "Include Points" does nothing; debug drawing always on; unused footprintAnalytics import; 12 non-ASCII bytes (would crash `install_icons.py`); 21 blocks without braces; no icon | M |
| Integrate footprint (`integrateFootprint.fs`, 530 lines) | Exact-arc path works | Sketch arcs and splines merged in a single `opExtractWires` (502-503), the pattern Scale Footprint found to fail (unresolved); Strict mode outputs rational NURBS with no radius; dead UI (print options, Recalculate, ACP, "Defualt" typo); nested secant solvers in `fpt_geometry` fail to converge without saying so | M |
| Scale Footprint (`scaleFootprint.fs`, 2817 lines) | Accordion and Keep Taper keep arcs; Scale Radius outputs only splines | Knots dropped in 7 places; `findParamAtX` fallback plus a 1 mm curve-selection window; X=0 assumption; boundaries lost when edges aren't sorted by X; roughly 22k spline evaluations in Scale Radius, about 90% wasted; fixG1 runs against arcs that get replaced afterwards; hard-wired to world XY; fails fscheck (63 non-ASCII bytes); about 56 blocks without braces; about 8 helpers duplicated from the libraries | L |
| Libraries (`fpt_analyze`, `fpt_geometry`, `footprint_math`, `predicates`) | Usable | `edgesToBSplines` doesn't pass forceNonRational (`fpt_analyze.fs:77`), so rational arcs are evaluated wrongly (Correction 39); `computeAverageRadius` averages 1/\|k\| per sample and is pulled around by near-inflection samples; exact and linear solvers are duplicate copies with silent non-convergence; overlap check can never fire | M |
| Dead files | Delete | `footprintAnalytics.fs` (remove the import first), `footprint_config.fs`, `Feature_Studio_1.fs` | S |

## 3. Prioritized recommendations

**P-1: Safety net. All Onshape writes are user-run.**
- Build a "Footprint tests" Part Studio with these cases:
  - Analyze: AF1-AF8
  - Points: GP1-GP6
  - Scale: SF01-SF15
  - Arc fit: the AF-arc set
  - Integrate: IF1-IF9
- Add a checker script, `devtools/onshape/check_footprint_tests.py` (read-only eval), modelled on `check_move_along_edge_tests.py`.
- Capture goldens from the current code before any change.
- Effort: M (1-2 days), mostly building instances.

**P0: Small, verified correctness fixes (about 350 lines in total).**
1. `fpt_analyze.fs:77`: add `"forceNonRational": true`. One line. It changes numbers only on rational (exact arc) input; confirm with AF6.
2. `analyzeFootprint`:
   - Write `definition.hasTipTail` in the editing logic.
   - Delete the duplicated block at 134-145.
   - Fix the "Decription" typo.
   - Add a `reportFeatureInfo` summary in the body.
   - Size S.
3. `scaleFootprint`:
   - Add one `mapBSplineControlPoints(bspline, fn)` helper that keeps degree, knots and weights, and use it in all 7 rebuild sites.
   - Replace `findParamAtX` with `fpt_analyze.findParameterAtX` and pick the curve by exact containment.
   - Replace `findInflectionInTempCurve` with the library `findInflectionPoint`.
   - Sort boundaries by xMin.
   - Move the reference curvature sampling out of the iteration loop.
   - Add notices, plus a regenError for a zero-length RSL or a footprint not in world XY.
   - About 150 lines. Size M.
4. `arcFit`:
   - Absorb short blocks into a neighbour instead of dropping them.
   - Add an `evCurveDefinition` Circle/Line exact fast path.
   - Set realistic bounds (posTol 0.01 mm, planeTol 0.01 mm, minLength 1 mm) and delete the conflicting "Default" annotations.
   - Collect fit statistics and report them as info; add a warning when segments exceed tolerance.
   - About 130 lines. Size M.
5. `integrateFootprint`: switch `emitFootprintWithArcs` to Scale Footprint's two-stage extract. About 15 lines.
6. `getFootprintPoints`: remove the import at line 4. After that is pushed, the user deletes the `footprintAnalytics` and `Feature_Studio_1` tabs.

**P1: Consolidation (net about -1,500 lines).**
- New `footprint/arc_kernel.fs`, a library with no feature in it:
  - Options map instead of 8 positional arguments.
  - One primitive format, keyed on `kind` rather than `type`.
  - `splinesToArcPrimitives`, `edgesToArcPrimitives`, `primitivesToBSplines` (absorbs `forceQuadraticNurbs`).
  - One `emitAnalyticWire(context, id, plane, items)` using the two-stage extract. It replaces the three emitters: arcFit's `emitSketchFromPrimitives`, integrate's `emitFootprintWithArcs`, and scale's `emitScaledSide`.
  - Scale Footprint's arc-chain solver (`scaleFootprint.fs:2342-2586`) moves here too.
  - Scale Footprint then drops its import of the whole integrateFootprint studio.
- Arc fit:
  - Delete tanTol, `classifyJoinsHardness`, `orderAndOrientBSplines`, the join branch and the planeTol threading, about 120 lines. Implementing real G1 across edges is the alternative; that is a user decision.
  - Unwrap arcs over 180 deg using `thetaMid`.
  - Add `SHOW_LABEL` to the output-type enum.
- Scale Footprint:
  - Delete the local copies of `computeSignedTaper`, `evaluateRadiusBetweenInflections`, `findInflectionInTempCurve`, `buildSingleCurveFromPoints`, `getBSplineParamRange/Bounds` and `buildAnalysisConfig`.
  - Run fixG1 after the arc fit.
  - Parameter UX: `SHOW_LABEL` on the enums, degree bounds [1,3,5], and show degree/tolerance/max-CP options only for Scale Radius or Strict.
  - Braces and ASCII pass until fscheck is clean.
- Libraries:
  - Delete `buildBaseIntegrals`, `signR`, `evPathCurvatures`, the linear waist solver and the `gatherRadiusInformation` chain, plus the unused imports.
  - Merge the widest and waist finders into `findExtremumY`.
  - Replace the duplicated solvers with one generic `solveTheta0` that returns a `converged` flag, and report non-convergence from integrate.
- Integrate footprint:
  - Route Strict output through `emitAnalyticWire`.
  - Fix unify+strict so it produces one body.
  - Delete the dead parameters, imports, the no-op `editingLogic` and the unused `samplingDef` keys; fix the typos.
  - Widen the taper bounds to [-1, 0.25, 1] deg.
- `computeAverageRadius`: switch to arc length divided by total turning angle. This changes reported numbers and needs the user's sign-off, with goldens re-baselined deliberately.

**P2: Structure and native replacements.**
- Scale Radius Phase B: move to `fpt_geometry.integrateExact` so it can output arcs.
- Local coordinate frame, so a footprint on any plane or facing either way works.
- One shared `resolveContactPoints` used by all five features (today there are four different FCP conventions).
- `getFootprintPoints`:
  - Cut regions with `opSplitEdges` at arc-length parameters instead of `opSplitPart` with planes (Correction 45).
  - Split the 360-line body into helpers.
  - Add an optional FCP selection.
- `sampleRadiusEdges`: replace one `evDistance` call per sample with an approximated B-spline plus a root solve.

## 4. Designs (condensed; the full versions are in the expert reports)

- **Arc kernel API:**
  - `arcFitOptions(overrides)` with defaults {posTol 0.01 mm, minLength 1 mm, numSamples 16, maxDepth 8, maxFits 5000}.
  - `splinesToArcPrimitives(splines, opts)` returns `{primitives, stats: {nLine, nArc, nPreserved, maxErr, nOverTol, fitsUsed}}`.
  - Primitive format: `{kind: "line"|"arc", start, end, mid, center, radius, normal, maxErr, preserved, curveIndex, u0, u1}`.
  - `emitAnalyticWire` works in two stages: sketch arcs and lines, extract them to a wire, then extract that wire's edges together with the `opCreateBSplineCurve` spline edges into the final wire.
  - The two consumers are pinned at `b515690a...`, so the new API can be built without breaking them; the user re-pins when ready.
- **Knot-preserving transform:** copy degree, knots, weights and isPeriodic; map only the control points. `fixG1` (`scaleFootprint.fs:2305-2309`) already passes knots successfully, so this is proven to work.
- **Target library layout:**
  - `fpt_analyze`: pure B-spline analysis that is safe to call from editing logic (only `ev*`/`evaluateSpline`/`approximateSpline`, per Correction 27).
  - `fpt_geometry`: sampling, the ODE and the solvers only.
  - Neither library imports the other.
- **Contracts that must not change:**
  - `getFootprintPoints` origin attributes `tipEdgePoints`, `rslEdgePoints`, `tailEdgePoints` and `tableFormat`: names, formats, ordering and last-instance-wins behaviour.
  - The `exportUnits` enum must stay defined in `getFootprintPoints.fs`.
  - Analyze footprint's persisted definition keys, and the key names in the `fpt_analyze` return map that editing logic copies by name.

## 5. variable_tools keys (minimal)

Step 0 is moving the footprint document from FeatureScript 2892 to 3070. All 12 footprint files use 2892, which I confirmed; every current importer of `extract_outputs` uses 3070 or later. Whether a 2892 studio can import it is untested, so check with a user-run `pushproject --check` first.

- **Analyze footprint:** 8 variables, published from the body after line 212:
  - `rsl`, `tipWidth`, `waistWidth`, `tailWidth`, `waistLocation`, `taperAngle`, `naturalRadius` (0 when not found), `averageRadius`.
  - Consumers:
    - Scale Footprint "Target average radius" (`:140`) and "Target waist width" (`:168`).
    - Integrate footprint waist width, waist location and taper (`:97/88/93`).
    - Drawing specs (inferred).
  - Caveat: `#averageRadius` only means the same thing as Scale Footprint's target radius once P1 makes Scale Footprint use `computeAverageRadius`. Today it computes its own version.
  - `output` = the analysis sketch; `inputs` = the footprint edges plus the RSL.
- **Generate Footprint Points:** queries `fcpPlane` and `acpPlane` only (`qNothing()` unless "retain planes" is on). Seven features across four folders take a planar FCP/ACP face; I checked their filters. I would **not** publish tip/RSL/tail edges: their consumers are only guessed at. Add them later if asked.
- **Scale Footprint:** query `plusYEdges` = `qCreatedBy(id + "posOut" + "wire", EDGE)`, the open +Y wire that Analyze footprint expects. I dropped `minusYEdges`, which one expert proposed: nothing needs it, and the standard `output` covers both sides.
- **Arc fit:** no keys. Set `output` explicitly to the composite wire (or the sketch, when curves aren't wanted). Otherwise, in BOTH mode, the standard fallback returns the sketch edges and the wire edges together, duplicating each edge. This follows the same bug class as the Split+ fix; not tested live.
- **Integrate footprint:** nothing. The standard fallback is correct, and its solved stats are discarded before the feature sees them (`fpt_geometry.fs:106`) and are pre-refinement anyway.

## 6. Icons

| Feature | Status | Action and glyph concept |
|---|---|---|
| Analyze footprint | Has icon (blob 279bd6d8, `:12/91`), no SVG source | Download the blob into `icons/final/` (read-only GET, needs user OK) |
| Integrate footprint | Has icon (d351ce89, `:30/66`), no SVG source | Same |
| Scale Footprint | Has icon (e81c3eb0, `:28/105`), no SVG source | Same |
| Arc fit | **None** | `arc_fit_icon.svg`: grey (#999) freeform S-curve; over it a chain of three tangent arcs in #333 with white junction dots; the middle arc in #1651B0 with a radius line to its centre |
| Generate Footprint Points | **None** | `footprint_points_icon.svg`: grey centreline, #333 half-sidecut curve rising at tip and tail, FCP/ACP tick marks, 5-7 white sample dots, small #1651B0 table grid in the top-right corner |

- Add both new icons to `icons/icon_targets.py`.
- Remove the non-ASCII characters from `getFootprintPoints` before installing its icon. `install_icons.py` writes the file back as ASCII and raises an error *after* the SVG blob has been uploaded, which leaves a half-installed icon.
- Installing an icon is a live upload, so it needs the user's approval.

## 7. Conflicts, verification, open questions

**Conflicts I resolved:**
- *footprintAnalytics is dead:* the library expert is right. `getFootprintPoints.fs:4` imports element `1f26cb8c`, which `.sync-state.json:5120` maps to that file. The memory note is wrong.
- *scaleFootprint uses `detectWholeCurveArcOrLine`:* two experts independently found this is stale. The code checks `evCurveDefinition is Circle` per edge (279-284).
- *Integrate's mixed extract works / fails:* the two memory notes contradict each other. Left unresolved. I recommend the two-stage extract anyway because it is safe, and a test instance will settle it.
- *Keys:* I cut the integrator's proposal from 5 Generate Footprint Points queries to 2 and from 2 Scale Footprint queries to 1, to match the rule of publishing only what a downstream feature would actually pick up.

**Spot checks I did on the high-severity findings (all confirmed):**
- Scale Footprint knot drop: `:1255-1270` rebuilds with no knots; the comment at `:2085` says so on purpose.
- `findParamAtX`: strict `x1*x2 < 0` test, then falls back to the middle parameter (`:1182`, `:1209`).
- X=0 inward-search assumption: `:1582`.
- Arc fit short-block drop: `:953-962`.
- Arc fit join branch unreachable: segments on different curves always fail the `a.curveIndex1 == b.curveIndex0` test (`:1407`).
- Arc fit bounds defaults: posTol 1e-4 mm, planeTol 1e-5 mm, minLength 10 mm (`:11-13`).
- `hasTipTail` never written to the definition.
- `edgesToBSplines` has no forceNonRational (`fpt_analyze.fs:77-80`), and Correction 39 confirms `evaluateSpline` drops the weights of rational splines. I upgraded this finding from unverified to verified.
- Zero notices anywhere in the suite.

**Still unverified:**
- How large the knot-drop distortion actually is. The code path is confirmed; SF01/SF04 will measure it.
- Whether the arc fit "Default" annotations or the bounds values govern the defaults. My inference is the bounds.
- The micron-scale FCP gap after a Keep Taper rotation.
- Whether a 2892 studio can import the 3070 `extract_outputs`.
- The Arc fit sketch/wire duplicate-edge claim.

**Questions for the user:**
1. Does anything outside this repo read the Generate Footprint Points attributes or table? This decides how strict the contract is.
2. Should footprints facing either way (tip at +X) and footprints on planes other than world XY be supported, or should the feature error out?
3. May the average-radius definition change? It changes reported numbers and Scale Radius results.
4. Arc fit's Tangency tol: implement real G1 across edges, or delete the parameter?
5. Integrate footprint's ACP input: implement it or remove it? And is waist location measured from the ski centre?
6. Should Analyze footprint also publish tip and tail length for drawings?
7. May we GET the three existing icon blobs so the new icons can match them?
8. Is the FS 3070 bump for the footprint document acceptable?

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/footprint/scaleFootprint.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/arcFit.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/fpt_analyze.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/analyzeFootprint.fs
- C:/Users/jed.yeiser/documents/featurescripts/footprint/integrateFootprint.fs