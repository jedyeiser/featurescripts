# Cross-section drawings -- findings + recommendation (2026-09-28)

Read-only survey (GETs + DRAWING_JSON translations) of the X-Sect drawings in the four RD docs:
TAC 6212b76f, FOU 9732a1a9, PIR 9329c45a, OEF 76f15b36.

## How it is built today
- ONE template drawing copied into every doc: identical view ids, reference ids and Date drawn (2026-07-09);
  5 A4 sheets SPA / XS1 / MRS / XS2 / EDA.
- Each sheet: a 1:1 "Front" view of the whole ski (mostly off the A4 page -- it only carries the cutting line) +
  a Section view (showCutGeomOnly, scale from the parent view).
- Views reference the open composite "3D_Parts_Composite" from the Parts studio, derived into 3D_Ski
  (`Parts_Derive`), then Mirror 1 / Split 1 / `Simple Body Rename 1`.
- Stations come from the hand-built sketch `X-Section_Sketch` in 3D_Ski: 5 vertical lines with index ids, driven by
  #Tail_Length / #RSL / external refs. Positions: TAC 1625/1255/885/515/145; others 1615/1247.5/880/512.5/145
  (not tied to the Design Master FCP/MRS/ACP mate connectors).
- Dimensions: SPA 136.28 / 131.38 / 6.75 / 1.18; XS1 10.29 / 1.80 / 7.70 / 7.55 x2 / 7.0 deg; MRS 14.66 / 1.20 /
  0.44 / 0.40 / 0.26; XS2 11.22; EDA none. No tables.

## Health
| Doc | Views | Dims | Notes |
|---|---|---|---|
| TAC 6212 | OK | 15/15 attached | title "RD 21SKH 28 177 X-SECT" (stale) |
| FOU 9732 | EDA view errored ("View section cutting line is undefined") | 15/17 dangling | dangling dims still SHOW old values: XS1 angle 173 deg, 4.0 where TAC has 1.8 |
| PIR 9329 | EDA errored | 14/17 dangling | same |
| OEF 76f1 | EDA errored | 14/17 dangling | same |
REST `/views` and `/references` report errorCode 0 everywhere; only DRAWING_JSON shows the dangling state.
All model features regenerate OK -- the breakage is entirely in the drawing layer.

## Causes of brittleness
1. Dimensions attach to section-cut edge ids generated from the derived composite's topology; copy / re-derive /
   layup change / Mirror / Split -> new ids -> the dimensions dangle but keep showing their stale values.
2. The composite's id changes (stored SFXHB vs current SiSHB); it only resolves while Onshape can still match it through derive + Mirror + Split.
3. Stations live in a hand sketch (index ids, external refs); the EDA cutting line fails in 3 of 4 docs.
4. Hard-coded rename string "RD 21SKH 28 177 X-Sect" in all four docs.
5. Template metadata (Drawn by/date) copied verbatim.
6. The off-sheet 1:1 parent view exists only to hold cutting lines; `includeWires:false` on all views.
7. Station values disagree between docs (370 vs 367.5 mm spacing) and aren't linked to FCP/MRS/ACP.

## What already exists
- `xSection/` "EI and Cross Section" (xSect.fs, 35 stations): opPlane + batched opIntersectFaces per body
  (`section/xSectProcessing.fs:187-246`), per-body loops, area/centroid/I, bbox thickness+width, EI, ABD, NA, GJ;
  stored in the `CrossSectionAnalysis` attribute on qOrigin; `createCompositeWires`
  (`materials/xSectComposites.fs:27-71`) builds per-station wire composites but with INDEX-based ids/names.
- `publish_tools/`: stations named by query (station_utils/definition, `stationIdFromName`), closed WIRES composite
  + view composite + table attribute (station_geometry), custom Station table verified on a drawing.

## Recommendation
1. **Stations come from Station definition, not sketches.** FCP / XS1 / MRS / XS2 / ACP (EDA/SPA) resolved from the
   Design Master mate connectors, shared with the Station geometry and Primitive work. That removes cause 7, and cause 3
   at its root.
2. **A "Section geometry" feature** (publish_tools), per named station:
   - cut each layer body with the xSect pipeline (opIntersectFaces + processBodyCurves);
   - build per-layer section faces (named by layer / material), laid flat in a station-local frame
     (w across, h up, centred on the ski centreline) in ONE CLOSED composite `<prefix> SECTION <STATION>`.
     It is closed because it is all generated geometry, so it can be moved and the sections laid out side by side;
   - add named dimension-aid wires/points: overall width, base width, total thickness at centre, core height,
     sidewall faces + angle lines, edge/base thickness -- ids = station name + measurement key (never indices);
   - write a section table (thickness, widths, layer thicknesses, EI, GJ) from data the feature computes itself,
     through the Station table attribute pattern.
   Drawings then use plain views of flat composites: no section views, no off-sheet parent, no cutting lines.
3. **Tradeoff:** native section views give automatic hatching; generated faces in a normal view do not hatch.
   A shaded view coloured by layer appearance is arguably better for layups. Decide per drawing consumer.
4. **The drawing builder (API) becomes realistic:** with name-based ids, a script can build or repair the X-Sect
   drawing in every doc (views with includeWires:true, dimensions by deterministicId, text from table data).
   It also needs the read-only audit (DRAWING_JSON isDangling) -- the only way to see the dangling dimensions.
5. **An assembly is not needed** for full-ski sections; the composite-per-station approach covers it.
6. **Quick fixes for existing docs:** replace the literal rename with the #Part_Prefix pattern used in Parts; treat
   the FOU/PIR/OEF X-Sect drawings as WRONG (dangling dims display TAC-era values) until rebuilt.

## User decisions (2026-09-28)
- Sections come from the Part Studio with the real layer parts (like 3D_Ski); the 3D part is to be the source of truth.
- No dimensions: per-station composites (shaded-view option) + one table per section:
  X, s, ski_thck, core_thck, sw_height (sidewall height in the section), sw_width (raw sidewall material width),
  sw_left_in_ski (sidewall width remaining at the TOP of the SW rout -- the minimum, since rout angle > 0 and a
  positive step-in cut into the ski), cavity_depth.
- Sheet 1 summary: full ski plan + profile + table (RSL, SW rout specs, est. weight, est. stiffness, est. deflection,
  total reinforcement thickness, total exposed layers, minimum SW left in ski, ISO min-thickness locations (solved)).

## Open before building
- Spike S1: do name-based ids survive a document copy / re-derive in a drawing (build a section composite in
  73271cfc, dimension it via API, add a station, regen, copy workspace, audit)?
- Hatching vs shaded layers; which dimensions per station are canonical (list above is TAC's).
- Section of the flat Parts composite (3D_Parts_Composite) vs the 3D_Ski (mirrored/split) body -- which is truth?

## Proposal v2 (2026-09-29, after reviewing RD 20ONE's X-Sect)
Reference: RD 20ONE 28 178 (did 0ce37e0a...), drawing "RD 20ONE 28 178_X-Sect" = 5 A4 sheets at 1:1 (SPA, XS1, MRS,
"A - A" (= XS2, inconsistent name), EDA), native section views of composite "Ski_Parts_3D" in 3D_Ski, hand dimensions
(total / core / sidewall heights, top + base widths, SW angle 15 deg, step 4). 3D_Ski holds real layer parts with
materials: base 4101_3D (P-Tex), edges 4103_L/R, core strips "Part 1..13" (Poplar / Beech), glass 4305_3D / 4310_3D,
topsheet 6005_3D, sidewalls 4401_L/R (ABS), shear rubbers, 4802/4803; stations today from the hand sketch
"X-Sect-Sketch"; "EI and Cross Section 1" already cuts the same parts.

1. "Section geometry" feature (publish_tools, runs in the 3D studio): stations from a Station definition (SPA/XS1/MRS/
   XS2/EDA = FCP/XS1/MRS/XS2/ACP). Per station: cut the layer parts, one flat FACE per part (keeps the part's material
   colour), laid out in a stacked column (all stations on one sheet), station label as text geometry. One closed
   composite per station + one for the set.
2. ONE section table, a row per station: x, s, ski_thck, core_thck, sw_height, sw_width, sw_left_in_ski, cavity_depth
   (+ width at base / top, optionally EI / GJ at the station). Layer ROLES by explicit picks (Base, Core, Sidewall,
   Topsheet ...) -- names/materials vary ("Part 7"), picks are robust.
3. Drawing: one A3 at 1:1 (sections ~130 x 15 mm each, 5 stack easily) + the table; shaded view shows the layup by
   colour (test whether drawing SHADED views keep face colours -- wires are black, faces may not be). No hand dims.
   Optional A4-per-station set for the shop.
4. Summary sheet (earlier decision): plan + profile + summary table.
Open: role picks vs material rules; cavity_depth definition; station names shown (SPA/EDA vs FCP/ACP); one sheet vs
per-station sheets; include EI/GJ per station?
