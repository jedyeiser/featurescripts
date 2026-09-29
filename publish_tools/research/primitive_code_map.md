# Export Primitive -- existing code map (2026-09-28)

Read-only survey of what already exists for each part of `primitive_spec_2026-09-28.md`.
Most of it exists and needs wiring together. Genuinely new: top/bottom scale factors, the radius-plot writer, the
missing baseline measures, and SW rout parameters measured back from a surface.

| Capability | Best existing source | Status | Gap |
|---|---|---|---|
| Footprint from volume | `curve_tools/evaluate_profiles.fs` `partProfiles` L433 (PERIPHERY/TOP/BOTTOM/MIDDLE); wire input `footprint/fpt_analyze.fs` `prepareFootprintCurves` L348, `detectTipTail` L262 | tests P1-P8; footprint suite has no regression tests | world XY, +Y half, world X; `edgesToBSplines` drops analytic arcs; dead `footprintAnalytics.fs` still imported by `getFootprintPoints.fs:4` |
| Radius / inflection / widest / taper | `fpt_analyze.fs` `findWidestPoint` L456, `findWaistPoint` L580, `findInflectionPoint` L697 (in-curve + junction), `computeAverageRadius` L866, `computeTaperAngle` L957 | production | results only reach the dialog through editing logic, so they go stale; natural radii not published; average-radius definition disputed (L/dTheta); `gatherRadiusInformation` L1174 is a stub |
| Baseline analysis | `xSection/beam/analyzeBaseline.fs` `analyzeBaselineGeometry` L437; generator `xSection/features/generateBaseline.fs` | draft docs | values not published; tip/tail block, Tip/Tail_height, FB_Roll, AB_Roll, MCl missing |
| Unwrap, [s,w,h] | `driven_offset/edge_offset_utils.fs` `buildAlongReference` L953, `referenceSurfaceCoords` L1265, `unwrapChart` L1330; feature `driven_offset/unwrap.fs` | tested in Copy 1/2 studios | arc length zeroed at alignment point; unwrap y = -planeNormal |
| Mid-plane section, arc length | `unwrap.fs` `profileFromFace` L1391; `xSection/section/xSectProcessing.fs` L201-227; `std/path.fs` constructPath/evPathLength | production | no top/bottom split of a section loop; per-region length ratio is new |
| EI, deflection, stiffness | `xSection/beam/xSectBeamAnalysis.fs` `computeBeamStiffness` L93 (30 kg, lb/in, mm/30kg), `getEIFromEdges` L373; `updateProfile.fs` (target EI) | production | assumes FCP < ACP -- sort lo/hi at call site |
| SW rout | `example_1/refSurfCreation/createSWRoutSurf.fs` L175-205 (legacy, known bugs); production = Driven edge offset | -- | nothing measures angle / dist above base / step-in back; nearest is `driven_offset/evaluate_offset.fs` |
| Stations / key names | `qcTable/qcTable_stations.fs` `generateStations` L118; `publish_tools/station_utils.fs` | production | no TIP/TAIL/MP; no [s,w,h] |
| Tables / publishing | `publish_tools/station_table.fs`; `qcTable.fs` L584; `variable_tools/extract_outputs.fs` `embedStandardOutputs` L245 | production | the 5 tables + N-point table are new |
| Composite + datum | `publish_tools/station_geometry.fs` `buildView` L241 (opPoint datum, closed WIRES composite) | regen-verified | band stacking offsets new; no MCs in composites (correction 19) |
| Scalar plotted as a wire | `xSection/section/xSectVisualization.fs` `createGenericCurve` L63; radius-plot convention `footprint/integrateFootprint.fs` `curvatureScaleFactors` L43, `convertRadiusScalefactor` L580 | production | no radius-vs-x writer yet |

Radius plot: draw it in integrateFootprint's input format (10 mm Y per 1 m radius, sign from Y, a constant radius
= a horizontal line) so the plot can be fed straight back in.

## Naming / convention conflicts
- Baseline names: generateBaseline `camberHeight/frcpl/arcpl/fcpHeight/acpHeight`; analyzeBaseline `camber_height,
  max_camber_pt, frcp_pt, frcpl, fcph, arcp_pt, arcpl, acph, fb_min_pt, ab_min_pt`; spec `MCh, MCl, FCPh, FRCP,
  FRCPl, ACPh, ARCP, ARCPl, FB_Roll, AB_Roll, Tip_height, Tail_height`.
- FRCP means two things: generateBaseline = FCP +/- frcpl in X (the join point); analyzeBaseline = lowest-Z inflection.
- FRCPL/ARCPL are |dx|, not arc length (conflicts with s).
- XS1/XS2: qcTable "XS-1"/"XS-2" at FCP + rsl/4; spec = MRS +/- RSL/4; drawings use EDA = ACP, SPA = FCP, X from the tail end.
- Tip direction: getFootprintPoints "Tip toward +X" flag; analyzeFootprint uses FCP vs MRS; spec: tip = FCP side.
- FCP < ACP assumptions: computeBeamStiffness, computeCompliance, getEIFromEdges; generateBaseline rounds xFCP/xACP to 0.1 mm.
- Four different FCP/ACP resolvers (resolveReferencePointX, checkInputData, entryPoint, qcTable extractXPosition).

## Proposed module split (`publish_tools/primitive/`)
`primitive_types` (enums, key set, canonical names -> code keys) / `primitive_frame` (one direction-safe resolver,
datum, [s,w,h] zeroed at MRS) / `primitive_profiles` (mid-plane section, top/bottom split, scale factors) /
`primitive_footprint` (fpt_analyze + unwrap + radius-plot writer) / `primitive_baseline` / `primitive_beam` /
`primitive_swrout` / `primitive_output` (stacked closed composite + table attributes + embedStandardOutputs) /
`export_primitive` + `primitive_table`.
