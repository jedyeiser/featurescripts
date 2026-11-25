# csv curve + -- design (2026-11-24, decisions agreed with the user)

Document: "csv curve +" (cf2f66d47dfeb06b17bb1b38, workspace 62f98635c58c3cb456044aea, Part Studio element
a788c26f8d31d8837c853c5e; test CSV tab `MK221-5_PROFILE.csv`, element 86fa04969a4984f749b22991).
Template: `std/routingCurve.fs` (CSV path only). Self-contained: no import of curve_tools (fillet_wire) in v1.

## Why
Routing curve's CSV import fits ONE interpolating spline. Near-discontinuities then ring and inflate the control-point
count. New feature: split at break points, approximate each piece, output one wire, optionally bridge the breaks.

## Test data (MK221-5_PROFILE.csv; ski thickness profile, mm)
35 rows, x = 120..1590 uniform (43.235), y = 0, z = 6.09..14.96. Aspect ratio ~160:1, so the kinks are < 1 degree.
THREE breaks (0-based indices 9, 10, 22; x = 509, 552, 1071): the "notch" at 509-552 is a step with two corners, the
9-10 piece is a straight 2-point edge. Breaking only one of 9/10 makes the overshoot ~10x worse (0.009 -> 0.07-0.09 mm).
Second differences: kinks 0.33-0.36e-3, everything else <= 0.13e-3 (robust z >= 7.9 vs <= 2.3).

## Decisions (user)
- Mode: Process button is the default (nothing updates until pressed); an Auto mode is opt-in.
- Breaks: Suggest only (detected breaks shown as markers / filled by a "Suggest breaks" button; nothing applied
  until accepted) PLUS improvement metrics: how much breaking would improve the curve (see Metrics).
- Smoothing at breaks: NO shared tangents (user: supplying tangents to disparate sections recreates the ringing).
  Use a bridging curve (G2) built natively in this document, or the existing Fillet wire feature run afterwards.
- Approximation: force piece END POINTS to interpolate exactly -> no gaps when extracting wires.
- Lives in the csv curve + document.

## Detection (measured offline; scripts in the 2026-11-24 session scratchpad, breaks/ and approx/)
Second difference of slope, robust z-score, O(n), raw mm (never rescale z to a 1:1 aspect: margin collapses):
```
s[i]  = (p[i+1]-p[i]) / (x[i+1]-x[i]);  dd[i] = abs(s[i]-s[i-1]) / (0.5*(x[i+1]-x[i-1]))
sig   = 1.4826*median(abs(dd - median(dd)));  score[i] = (dd[i]-median(dd)) / max(sig, tiny)
break where score > T (default 5, parameter; 3..6 gives exactly {9,10,22}), local maxima, min piece 2 points,
keep adjacent pairs (the notch). Optional stride m for dense/noisy data. No scale-free T exists across densities.
```
Rejected on the data: chord-vs-curve (flags the crest, not the kinks), dR/dP ringing sensitivity (= fourth difference,
costs a spline build per point, worse margin), DP on control-point count (breaks move with tolerance), turning angle with
z scaled by 50+ (margin < 2.4), Hampel (local MAD collapses on smooth data). Greedy energy-gain splitting is a good
VERIFIER (accept a break only if it removes ~25% of the bending-energy proxy), not a finder.

## Metrics to show for "how much could breaking improve the curve"
Cheap, no spline builds: bending-energy proxy (sum of squared second differences) with vs without the suggested
breaks; control-point count single vs piecewise at the chosen tolerance; max deviation. Measured on the test file:
single cubic spline integral of kappa^2 = 38.4e-6 vs 7.3e-6 with 3 breaks (5.3x), curvature total variation 4.7x,
control points 34 -> 18-23. Raw overshoot is small (0.009 mm); the damage is in the comb and the control-point count.

## Approximation (per piece), measured
- Pieces < 5 points: interpolate exactly. Otherwise native approximateSpline (UNVERIFIED live) with
  interpolateIndices [0, n-1], then VERIFY at every data point and every interval midpoint, report max deviation in a
  notice, refit with tol/2 once, else fall back to exact interpolation.
- tol 0.01-0.02 mm: ~21-27 control points vs 38 exact, no visible loss; 0.05-0.1 mm rounds kinks by up to 0.09 mm.
  Warn when tol > ~0.25 x the smallest kink offset (0.62 mm here -> ~0.15 mm).
- Greedy knot insertion is UNSAFE (overshoot to 1.3 mm between data points); Douglas-Peucker overshoots tol.

## Bridge curve at a break (user choice; spec to build)
Per accepted break: choose a setback t each side (default 0.4 x the shorter adjacent piece, user override "Blend
length"; the 43 mm notch piece caps t near 17 mm). Cut each piece at t (position, tangent, curvature taken FROM the
fitted piece, not imposed). Bridge = G2 quintic-Hermite-style spline between the two cut points (opFitSpline with
start/end first and second derivatives, or opCreateBSplineCurve). Removes the corner without forcing data. A true circular
fillet for these sub-1 degree kinks has r = 3-17 m and moves the curve 0.08-0.5 mm (so a blend LENGTH is the
primary control; show the equivalent radius in a notice). Fillet wire remains available as a post-step.

## Architecture (from the routing-curve read; std FS 2878)
- Routing curve builds only from `definition.points`; Process (`isButton(definition.processInputs)`, test only
  `clickedButton`, correction 27) fills it in editing logic (`processCSVData` / `getInputCSVPoints`, dedupe whole rows).
  Body reads `definition.inputData.csvData` only to validate (checkCSVRow). Reading it in the body is fine.
- Process mode: store `snapshot` (points) + `breaks` + `sig` (fingerprint of CSV+params). Body builds from them; on a
  fingerprint mismatch it reports info "CSV changed - press Process" and keeps the old geometry (does not throw).
- Auto mode: body reads csvData directly; nothing cached. Detection is never cached (O(n) arithmetic, unitless).
- Breaks UX order: (1) break index array + "Suggest breaks" button, (2) click-to-toggle via `togglePointsManipulator`
  (std/manipulator.fs:341; pattern in curve_tools/fillet_wire.fs:134,264), (3) Auto-apply.
- One wire: fit each piece then `opExtractWires` (fillet_wire.fs:749-752; there is no opJoin/opMergeWires). Both
  pieces must end on the identical point value (exact-interpolated ends). Gap tolerance of opExtractWires UNVERIFIED.
  A single spline cannot hold a G0 corner (correction 69).
- Visuals: reuse routing curve's showCSys / control polyline / curve length (setCurveLength). Draw break markers with
  ONE marker set per colour (fillet_wire showCornerPoints: startFeature + opPoint + addDebugEntities + abortFeature),
  NOT one addDebugLine per sample (758 lines cost +2.17 s). Confirmed = magenta, suggested = blue.
- Pitfalls: braces on every if/for; Defaults migrate into saved features; one declaration per param id; inline Filters;
  precondition visibility uses only ==, &&, ||, !; no println without a debug option; dedupe rows before opFitSpline.

## Built 2026-11-24 (csvCurvePlus.fs, ONE tab, pushed to the csv curve + document, compiles clean)
Live-verified in Part Studio 1 on MK221-5_PROFILE.csv (feature "csv curve +" is left in the tree as the test case):
- Picking the table in Process mode fills the cache (first time only); curve length 1470.117 mm shows in the name.
- Suggested breaks = points 10, 11, 23 (1-based), "3.93x smoother" estimate; Suggest breaks fills the list; 4 pieces.
- G1 and G2 bridges: 4 pieces + 3 bridges -> ONE wire (opExtractWires joins them; Curves (1)). Bridges stay within 0.185 mm.
- Width by Distance | Data points (same result at 1 interval); Bridge across short pieces: 50 mm / 2 intervals both give
  3 pieces + 2 G2 bridges ("1 short piece bridged across", within 0.21 mm of the replaced data).
- Approximation at 0.02 mm: 3 pieces approximated, 18 control points, worst deviation 0.0124 mm (interpolateIndices [0,n-1] fine).
- Manipulator draws the clickable data points; data polyline is green.
2026-11-24 later: icon A INSTALLED (tab csv_curve_plus_icon.svg 9de909b3 in the document, wired in csvCurvePlus.fs);
regular labelled enums (no HORIZONTAL_ENUM), a Description on every control, "Detection threshold" renamed "Detection sensitivity"
(robust z-score, plain-language tooltip), tolerance-only approximation (no max control points: approximateSpline does not meet the
tolerance when capped); breaks drawn as MAGENTA tick lines across the curve (suggested = BLUE ticks; single debug points were too
small and hidden under the manipulator dots), bridges CYAN (found again after the join with qClosestTo on the bridge midpoint);
quality metrics (Display > Show quality metrics, default on): this curve vs ONE spline through all the points (built in a scratch
scope): control points, bending energy (integral of kappa^2), curvature variation (total change of signed curvature), peak curvature,
inflections (sign changes of the binormal against the data's Newell plane normal; the data polyline's own count is shown). On the
test CSV with G2 + merge + approximation 0.02 mm: control points 37 -> 30, energy 0.038 -> 0.016 1/m (2.33x), variation 6.553 ->
3.108 (2.11x), peak 0.744 -> 0.353 1/m, inflections 11 -> 7 (the data itself has 7, so the 4 extra are ringing). Corners (G0) are
excluded from energy/variation. Metrics are measured on the loose pieces BEFORE the join, in travel order.
2026-11-25: quality metrics moved OUT of the info message into a read-only "Quality" group (UIHint.READ_ONLY parameters filled
with setFeatureComputedParameter, verified live; shown only with "Show quality metrics"): control points, energy (1/km), variation
(1/km), min radius (mm), inflections, for one spline vs this curve (+ the data's inflections). They are INTEGERS because the dialog
fields are narrow and truncate decimals. "Detection threshold" tooltip rewritten (higher = fewer breaks; no test-file numbers); the
label was "Detection sensitivity" for a while, which was backwards. New Output group: "Wire name" (setProperty PropertyType.NAME on
the joined wire; verified: shows in the Curves list). The info message now only summarises (points, pieces, bridges, deviation, approximation, suggestions).
NOT yet verified: Auto mode, the "CSV changed since Process" notice, other units, clicking a point in the graphics area,
a 2-point/3-point piece under approximation, closed CSVs, big CSVs (cache size), test pair (build_/check_ scripts).
Icon: icons/final/csv_curve_plus_icon.svg (candidate A, drafts in icons/drafts/csv_curve_plus/); NOT installed yet.

## Open / UNVERIFIED (check live in the first spike, within the API budget)
1. opExtractWires with two touching splines from approximateSpline/opFitSpline: single wire? Gap tolerance?
2. native approximateSpline tolerance semantics vs the Python model used for the numbers above.
3. Table parameter: integer-looking CSV cells (the "Table int32 bug"), and how a CSV is attached in REST-built tests.
4. Click-to-toggle manipulator behaviour with a few hundred points.
5. G2 bridge: opFitSpline second-derivative support at both ends; behaviour of a 2-point piece.
6. Columns and units of the user's real CSVs (assumed x,y,z in mm, startColumn 0), typical and maximum row counts,
   closed curves (split at one point and treat as open).
