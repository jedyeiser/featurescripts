# beamBuilder survey (2026-09-28, read-only)

App: `eocProductData/frontend/onshape/beamBuilder` (right-panel React) + `backend/beam_builder` (Django/numpy).
Purpose shift (user): from GENERATOR to VIEWER of ski/snowboard beam design, reading what FeatureScript publishes.

## State
- 4 tabs; only **Setup** is real (SetupTab.tsx: Metadata, Reference Points, Geometry Inputs, Design Inputs,
  Footprint Data, Baseline Data). Layup / Profiles / Output are empty scaffolds.
- Reads Onshape via FS eval only (query variables, body/sketch edges as exact B-splines, variables, MC origins,
  feature list). **No writes.** No Python executables (only Django/numpy endpoints).
- Does NOT read FS tables / attributes / published outputs yet.
- Setup state is in memory only (lost on reload). READMEs are stale (say "placeholder" / "generation").
- Charts are hand-rolled SVG (MiniPlot, CurvePlot, ProfileEndPlot), themes.css, Tailwind v4, lucide.

## Calculations vs FS standard
| Calc | beamBuilder | FS | Verdict |
|---|---|---|---|
| curvature / radius | analytic spline derivs, R = 1/k signed (analysis.py:610-632) | footprint_math.fs | formula MATCH; sign convention differs |
| widest / waist | all local max abs(y), waist = min between them | waist near MRS first, widest each side (fpt_analyze.fs:1016-1057) | DIFFERS (search order) |
| sidecut inflection | first k sign flip walking out from waist, unbounded | bracketed waist..widest, fallback widest | DIFFERS (usually same) |
| average radius | ds-weighted mean of 1/abs(k), inflection..inflection | unweighted mean at 200 x-stations (dx-weighted) | **DIFFERS** (review round2 suggested L/dTheta) |
| natural radius | arc through the 2 stations tangent to the waist line | 3-point circumradius FB-waist-AB | **DIFFERS** |
| taper angle | atan2(abs(yFB)-abs(yAB), abs(dx)) | same | MATCH |
| stiffness / deflection | 3-pt, 30 kg x 9.80665 at MRS, Mohr, Simpson 2001 pts | computeBeamStiffness, trapezoid 200 seg | MATCH method; EI input is an "EI curve in mm", not FS EI data |
| baseline min / camber | chord + max perpendicular distance | analyzeBaseline | MATCH (FS also refines MCl) |
| FRCP / ARCP | lowest-Z inflection per half | inflection nearest the half's minimum toward MRS (2026-09-28) | **STALE** |
| FB/AB roll, MCl | missing | analyzeBaseline outputs | missing |
| CLT EI / layup / mass | none | xSection CLT | none (CLT also in backend/materials/composite_calc.py) |
Also: beamSimulator bending.py uses g = 9.81 (vs 9.80665).

## Radius plot (the good prior work)
- Exact rational B-splines from Onshape (no forceNonRational); arcs also carry exact radius.
- Sample >= 40 pts/edge (~900 total), analytic 1st/2nd derivatives -> k -> R = 1/k (m, signed).
- abs(k) <= 1e-7 /mm (R > 10 km) -> null => line BREAKS instead of spiking; hover shows infinity.
- Inflections: interpolated zero crossings within an edge (flat run -> midpoint) AND between edges (tangent arcs
  of opposite sense); thinned to 20 mm.
- One series per edge, never bridged (a break = a junction).
- Y auto-scaled to the visible X window, clamped to the inner 90th percentile with a clip note; zero line.
- X windows: Full / RSL / Widest / Inflection; stacked width / slope / radius with synced hover; consistent markers
  (FCP/ACP/MRS dotted, inflection cyan dash-dot, widest magenta).
- Sign: baseline normalised (+ rocker, - camber pocket); footprint NOT normalised (depends on chain walk direction).
  FS integrateFootprint: sign from Y side, 10/20/50/100 mm Y per 1 m radius. Needs one contract (sidecut +).
- Tests: backend/beam_builder/test_inflections.py, test_baseline.py, test_placement.py.

## Keep / replace / delete
- KEEP: shell, auth, themes, MiniPlot/CurvePlot + markers, FootprintData/BaselineData layouts, metadata rows,
  DataState provenance, MessageLog; backend k/R sampler + inflection finder + stiffness as cross-checks.
- REPLACE with FS-published values: footprint scalars (fpt_analyze), baseline measures (analyzeBaseline),
  stiffness (computeBeamStiffness), EI (xSection CLT tables), contact points (export query variables).
  The radius SERIES may stay computed from exported curves.
- DELETE/park: Design Inputs, SidewallRoutModal, ProfileEndModal/transition.ts, block checks, profile-tangent,
  selection-pick inputs, GeometryBrowserModal as the main flow, generation-era docs/spec.
- Live mode: nothing yet (microversion captured but unused). New hook: poll the element microversion, refetch
  the FS contract only on change, auto-off after 5 min, opted-in studios only. client.ts says "Never poll"
  (~10k calls/user/yr quota) -> budget decision.
- Risks: numeric drift vs FS (avg/natural radius, FRCP, footprint sign); duplicated messaging.ts (should use
  @shared); duplicated stiffness in beamSimulator; stale READMEs.
