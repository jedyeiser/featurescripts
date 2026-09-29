# publish_tools -- "Publish & Drawing tools" document

Onshape doc 73271cfcd708b3f5e3fc315c (wid fea83d30106dba0a4e066761). Test Part Studio: "Part Studio 1"
(bb2cddb24faf53d9d043c38e). Purpose: generated drawing-aid geometry to replace the hand-built
drawing sketches (Part_Buyoff/Base/BF/Core/SW_Sketch) in ski Parts studios -- their breaking is the
pain point. Background research: memory `mbd-drawing-research`.

## Tabs

| tab | element | what |
|---|---|---|
| station_utils.fs | 8a8c023e223cf0814d973a63 | station entry predicate (Point / Along a line / Between two points), resolve, station-set schema `stationSet/1` |
| station_definition.fs | 8b522e33d07f67c10c37ba11 | "Station definition": resolved stations (plain values) embedded in its producer slot (key `stationSet`, extract_outputs) and optionally a # variable (default `stations`, empty = none) |
| station_geometry.fs | 5d3299cf520723444c27b273 | "Station geometry": per view (plan = datum XY, profile = datum XZ, other = MC XY) outline wires, optional region surface, station lines, datum point, grouped WITH THE PART in an OPEN composite `<prefix> <VIEW>` (excluded from BOM); optional FLAT copy at datum (DXF); station table + per-station queries embedded (extract_outputs V1) |

## Decisions (user, 2026-09-24)

- OPEN composite (part stays its own body; closed would need a duplicate part). Open composites
  can't move, so the datum frame changes numbers (table) and the FLAT copy, not the composite.
- Station inputs are queries (MC, vertex); Line + N: ends always included, measure PERPENDICULAR to
  the line; plus per-feature extra stations.
- 2026-09-28 (user): publish features PICK the Station definition feature ("Station definition",
  a FeatureList; several are read in order) instead of typing a variable name. The old
  `stationSet` string is kept hidden and read only when nothing is picked (saved features).
  Table rows are in definition order (set rows, then More stations), not sorted by x.
  Reader: the set is at getVariable(toString(featureId)).variable.stationSet (embedStandardOutputs
  nests values under "variable"). Fixture switched to the pick; all three test features INFO,
  table unchanged (4101 widths 116.2/97.95/93.26/105.94/132.08).
- 2026-09-28 icons installed: Station definition = two end points + blue stations
  (icons/final/station_definition_icon.svg), Station geometry = outline + blue station lines.
- Station operation ids = sanitized station names (never list indices) so dimensions don't re-bind.
- Name prefix: auto-filled from part name in editing logic (getProperty throws in the body).

## Test features (Part Studio 1)

Derive_Parts_V1 (all of RD 20TAC 28 Parts @V1; `devtools/onshape/publish_tools_derive.py`), then
`devtools/onshape/publish_tools_fixture.py` upserts:
- "Stations (test)": TAIL/EDA/SPA points, Q between EDA-SPA N=5 (145/515/885/1255/1625), CORE along a Part 7 edge N=4
- "4101 stations (test)": plan + flat copy
- "4501 stations (test)": plan + profile + region (4501 = open composite of core strips; outline gets members)

All regenerate INFO, no notices. Base widths at Q: 116.2/97.9/93.3/105.9/132.1; core thickness 3.16/8.63/12.07/7.70/4.16.

## Next (not done)

1. Drawing of `4101 PLAN`: wire display on; dimension to station-line ends + datum; add/remove a
   station, regen, confirm no re-binding. Ask user: API-created drawing or their template?
2. DXF export of `4101 PLAN FLAT` -- coordinates land at datum?
3. Verify prefix editing logic in the dialog (UI only).
4. Station sets per part family (user to provide). Decide: tangent-graze stations (4101 TAIL = 0.94 mm)
   -> position-only / minimum span.
5. Later: station table on drawing (per-role table or API), query-map keys, drawing builder research.

## Drawings (2026-09-28 demo)

`devtools/onshape/publish_tools_demo_drawings.py` rebuilds "4101 PLAN (demo)" (widths + positions from the TAIL
end) and "4501 PLAN + PROFILE (demo)" (widths + thickness) through the Drawings API. Findings:
- Station geometry now builds each view's geometry 0.01 mm IN FRONT of the part along the view normal; in the
  datum plane the lines lay on the part's underside and a top view hid them.
- A view shows the composite's wires only if created with `"includeWires": true` (onshapeCreateViews);
  onshapeEditViews sets the flag but does not re-render.
- Dimension references must use the view edge's `deterministicId` (jsongeometry); `uniqueId` resolved to other
  lines in the profile view. Text positions are sheet mm from the lower-left corner.
- Point-to-point between line midpoints gives station positions only because both midpoints lie on y = 0 here;
  asymmetric parts need ordinate dimensions (not tried yet).
- In the UI: Insert view -> pick the composite `<prefix> PLAN` -> view properties: show wires -> dimension the
  station lines (ends for widths, lines/datum for positions).
- 2026-09-28 (user): the view's curves (station lines, outline wires) and datum point are grouped in ONE CLOSED
  composite `<prefix> <VIEW> WIRES` (excluded from BOM); the open view composite `<prefix> <VIEW>` = part +
  WIRES (+ REGION sheet). Parts list for 4101: PLAN, PLAN WIRES, PLAN FLAT (was ~16 bodies). Query key
  `<view>Wires` added. Demo drawings kept their references (views error-free, wires still rendered).

## Station table (2026-09-28)

`station_table.fs` (tab 07f13dc8a33435e0fb1c5a7f): custom table "Station table". Station geometry tags each view
composite with attribute `publishStationTable` ({schema stationTable/1, title, prefix, view, rows}); the table finds
them with qHasAttribute and returns one table per view, rows sorted by x (Station | x from datum | Width /
Thickness / Span | From | To). Parameter "Views containing" (case-sensitive) filters by view name -- needed in
drawings, because inserting a custom table brings every table it returns. Verified: Part Studio table panel, and
drawing "4101 PLAN + table (demo)" (Custom table > Part Studio 1 > Station table, Views containing = 4101 PLAN).

### Surface parts (2026-09-28)
Station geometry accepts sheet bodies (and composites holding them). Per body:
* solid -> `opCreateOutline` region (as before);
* flat sheet seen face-on -> the sheet is copied onto the view plane (a region);
* any other sheet (a wall seen edge-on, a curved top surface) -> its boundary (laminar) edges are dropped onto
  the view plane with `opDropCurve` NORMAL_TO_TARGET. `opCreateOutline` refuses these (REGEN_ERROR).
Dropped boundaries are edges only: no REGION body, and they join `<prefix> <VIEW> OUTLINE`.
When the part has no region and a station crosses it once (the surface is edge-on: 2D_PERIPHERY in plan,
TOP_SURFACE in profile), the station is measured from the datum axis to the crossing: half-width or height.
Fixture: "2D_PERIPHERY stations (test)" (plan half-widths 48.7..68.1) and "TOP_SURFACE stations (test)"
(profile heights 5.75..14.7; plan 72.5 everywhere because that sheet is an untrimmed rectangle in y).

### Station table icon (2026-09-28)
User picked draft B (Station geometry outline + stations over a small grid; drafts icons/drafts/publish_tools/station_table_*.svg). Tab station_table_icon.svg 87946e777d7b592c8d681392, wired with "Icon" on the "Table Type Name" annotation (install_icons.py only wires "Feature Type Name", so it was wired by hand).

### Table cleanup, numbering, language (2026-09-28)
* Table: columns Station | x (mm) | Width / Thickness / Span, all centred. "Show edge positions" (default off) adds
  Lower / Upper edge (mm): where the station line starts and ends, across the view from the datum axis.
* Station entries: an Along a line / Between two points entry with an EMPTY name numbers its stations 1 .. N (no
  prefix); "First number" (default 1, so saved features keep their ids) starts the numbering anywhere, e.g. 0.
  A Point entry still needs a name.
* Language: Station definition "Language" (English / Deutsch) is stored in the set ("language" : "en" / "de"),
  copied by Station geometry into the view attribute; Station table "Language" = As station definition (default) /
  English / Deutsch. German headings: Station | x (mm) | Breite / Dicke / Abmessung (mm) | Untere / Obere Kante (mm);
  misses "verfehlt das Teil"; title "<view> Stationen".
* Fixture: "Numbered 0-4 DE (test)" + "4101N numbered DE (test)" -> stations 0..4, language de (verified by eval).
* The test studio's Station table is added from Publish & Drawing tools V2: its panel shows the new table only
  after a version + Update table.

## Export Primitive (phase 1 + 2026-09-28 decisions)

Tabs in tab folder `primitive` (local `publish_tools/primitive/`):

| tab | element | what |
|---|---|---|
| primitive_types | ecde24520874030ab412c981 | enums (PrimitiveSource, PrimitiveDatumUse, PrimitivePlotRegion, PrimitivePlot, PrimitiveTableKind), flat-baseline tolerance 0.01 mm, attribute name `publishPrimitive`, schema `primitive/1`, bounds, reserved key names |
| primitive_frame | 5808546b3b3d863d82796d24 | point / datum resolution, edge chains, chain-at-x (Newton), foot, normal crossing, [s, w, h] |
| primitive_profiles | 5865b24d55ff270a56088adf | mid-plane section -> BOTTOM / TOP / TIP END / TAIL END; Table 1 scale factors |
| primitive_footprint | fbc957543e769a649f00c5cc | base periphery, unwrap along s (centreline + one end section where cut back, 2026-09-29), radius + signed curvature (chain rule, exact), fpt_analyze (footprint V32), plot runs / region + axis clip / junctions |
| primitive_baseline | b827b10bc0bdc678c2db28cd | analyzeBaselineGeometry (xSection V58: FCP/ACP exact, workaround removed 2026-09-29) on a local copy; Table 5 |
| primitive_output | 6f122edb2547a6a46991d9fd | named points / segments, band stacking, closed composite + attribute |
| export_primitive | 3ce76ee786987ef00917b9b2 | feature "Export primitive"; target EI via xSection V58 xSectBeamAnalysis (getEIFromEdges, computeBeamStiffness) |
| primitive_table | 4e49f8a0b8f0c2bd75c919e7 | custom table "Primitive tables" |

Use: Export primitive (dialog groups, 2026-09-28: Inputs (open), Sources, Key locations, Plot, Stiffness, Tooling blocks,
Data table, Output; parameter ids unchanged) -> Volume (the ski solid), FCP, ACP (vertex / point / mate connector), optional MP(s), optional
Datum (empty = world origin, world axes; X along the ski, Z up, profiles in its XZ plane) + Datum uses: ORIGIN
(default; a vertex / point / MC whose ORIGIN moves the frame, axes stay world X/Y/Z, so x is measured along world X
from the datum -- the ski's own connectors have Z along the ski) or COORDINATE_SYSTEM (the MC's own axes, as phase 1),
Target EI (optional EI wire, xSection convention: x = world X, height 1 mm = 1 N*m^2, like EI and Cross Section's EI
curve), Tooling blocks (per block "name from" TYPED (default; shows only the name field) or WIRE (shows only the wire
pick; editing logic copies the wire's name into the hidden tipBlockWireName / tailBlockWireName, correction 36; no
wire = no row)), Baseline from Volume
(the section's bottom wire) or Input wires (e.g. FULL_BASELINE), Footprint from Volume (the base periphery) or
Input wires (flat FPT_L + FPT_R, taken as already unwrapped and aligned at MRS; or wrapped 3D wires), Extra key points
(array `extraPoints`: keyName + keyPoint (vertex / point / MC) + showKeyLine; name -> op-id key via primitiveKey, an
empty, reserved (FCP ACP MRS MP.. XS1 XS2 TIP TAIL, footprint point names) or duplicate key is a regenError on that
item), Data points N (+ "Force key locations", id `forceStations`: rows at XS1 / MRS / XS2 and every extra key point
inside the RSL), Station numbers (default on: # column on Key locations and Data), Plot (`plotMode` RADIUS default /
CURVATURE), Plot region (`plotRegion`, FULL default / RSL (value id CONTACTS) / WIDEST / INFLECTION: the x-range of the
radius / curvature band -- plot, reference, axes, key and junction ticks), Curvature scale (5 mm per 0.01 1/m), Layout
(band gap 50 mm, radius plot limit 50 m, tick 10 mm, EI scale 2 N*m^2 per mm, Dashed grid default OFF, Key lines
default OFF, Junction ticks default ON, Labels default ON + Text height 20 mm), Query variable (default `primitive`).
The average radius is ALWAYS taken between the inflection points (2026-09-28; the old "Average radius between"
parameter `radiusBetween` is gone -- its saved value is ignored, so saved features keep the full radius plot). The name prefix
fills from the volume's name (editing logic). Icons: feature = icons/final/export_primitive_icon.svg (tab
b9dc4aaf067afeb58293caed), table = primitive_tables_icon.svg (tab eb32ed1a7e9ecf0a7ef61a7c, wired by hand on "Table
Type Name").

Output: ONE closed composite `<prefix> PRIMITIVE` (excluded from BOM) in the datum XZ plane, BELOW the part, bands
top to bottom a band gap apart: EI (only with a Target EI: the EI wire's edges sampled + fitted at EI / EI scale, x
local; `EI REFERENCE` zero line; title EI (N*m + a raised 0.6-height 2 + ) via primitiveLabel's `^2` markup; `EI AXIS` / `EI TICK +50 TIP` / `EI LABEL` / `EI GRID` every 50 N*m^2, like the radius
frame, numbers without "+"; title "EI (Nm" + raised 2 + ")"; purple), BASELINE (+ points TIP/TAIL at the baseline's ends, FCP/ACP, and unless the baseline
is flat within the RSL FRCP/ARCP/MCL/FB_MIN/AB_MIN), PROFILE (BOTTOM, TOP, TIP END, TAIL END + points on the bottom
wire, TIP/TAIL at the section's extreme points along X = the bottom wire's ends, TOP FCP/ACP), FOOTPRINT (unwrapped: u = x(MRS) + s along the BASE at the point's own y (see Unwrap below), y drawn
as height; exact arcs kept wherever the base is flat; points widest / waist / inflections), RADIUS (10 mm per 1 m,
sidecut +, taper/tip/tail -, breaks where |R| > limit, arcs = horizontal lines, joined across continuous junctions;
or with Plot CURVATURE the band CURVATURE (1/m): signed curvature, same signs, one run through sign changes, broken only
at edge junctions where the curvature jumps (arcs), levels in 0.01 1/m (step 1-2-5 for <= 20 levels), numbers with 2
decimals every multiple that keeps them 1.25 label heights apart (default: 0 and 0.05, no "+"), names `CURVATURE TICK
+0.01 TIP` / `CURVATURE LABEL +0.05`; clipped runs shorter than 0.1 mm dropped (TAC: the tip arc starts exactly at
the inflection, curvature jumps +0.047 -> -0.98);
REFERENCE line; TICKs at FCP ACP MRS MP XS1 XS2, extra key points and FB/AB widest + inflection (inside the plot region);
`FOOTPRINT JUNCTION` / `RADIUS JUNCTION` 3 mm ticks at every +y footprint edge junction (op ids from u in um); `KEY
LINE <name>` dashed verticals (4 / 4 mm, opPattern) over all bands at FCP MP(s) MRS ACP + extra points with Show key line; scale frame: `RADIUS AXIS TIP` / `TAIL`
at the band's two x ends over every 10 m level covering the plot (outward, capped at the limit), 3 mm `RADIUS TICK +10
TIP` ticks outward at each level on both axes, optional `RADIUS GRID -20` dashed lines (4 mm dash / 4 mm gap, one
opPattern per level) at every level but 0). Each band has a `<band> DATUM` point at x = 0 on its reference line.
Labels (outline text as wires: sketch text -> opExtractWires of its REGION edges, the text's own curves overlap):
`<band> TITLE` (BASELINE / PROFILE / FOOTPRINT / RADIUS (m)) right-aligned in one column left of everything, centred on
each band's reference line; `RADIUS LABEL +10` numbers (0.6 x text height) left of the low-x axis. Colours
(APPEARANCE, kept by wires and points): baseline blue, profile green, footprint orange, radius plot red, reference /
ticks / frame / grid / numbers / datum points grey, titles in their band's colour. All rows live in the composite's attribute `publishPrimitive` (schema primitive/1)
and in the producer slot (Extract variables keys: primitive, rsl, averageRadius, naturalRadiusWidest,
naturalRadiusInflection, taperAngleWidest, taperAngleInflection, deflection, stiffness (0 = no target EI); queries primitive, baseline, profileBottom,
profileTop, footprint, radius or curvature). Tables: add "Primitive tables" (filter "Primitives containing", pick "Table"; the data table is "6 Data (RSL)", 4 stays reserved for the SW rout table).

Definitions: x from the datum; s = distance along the bottom wire from the datum, same direction as x (signed arc
length, zero at the bottom-wire point at x = 0, ds/dx > 0 whichever way the tip points; past an end of the wire it runs
on along that end's tangent; MCl's s is measured the same way, not taken from analyzeBaseline; 2026-09-28 user rule);
Key locations and Data rows sorted by x ascending, `station` 0.. from the lowest x (stored always, # column with
"Station numbers"); h along the bottom wire's normal into the
ski; XS1 / XS2 halfway FCP..MRS / MRS..ACP in x; RSL = |x(ACP) - x(FCP)| in the datum; ski_thck = normal thickness;
baseline_height = baseline above the straight line through its FCP / ACP points; Table 1 top lengths run between
the bottom stations carried to the top along the normal.

Rows (2026-09-28 decisions): only rows with data are stored and shown, and a table without rows is not returned.
Table 2 deflection (mm/30kg, Mohr integral of the EI wire, rollers FCP/ACP, 30 kg at MRS) and stiffness (lb/in, load
for 1 in) only with a Target EI; Table 5 Tip / Tail block only when named (Tip_height / Tail_height NOT defined yet,
left out); FCPh .. ACPh only when the baseline is analysed and not flat: a baseline within 0.01 mm of the FCP-ACP
chord over the RSL (e.g. a flat Design Master volume) gets an INFO note "the baseline is flat within the RSL" and no
min / FRCP / ARCP / MCL points; radii only where found.

Tests: Part Studio "Primitive tests" (cbf60b1202cbc555d4e752d6): Derive_DM_V1 (RD 20TAC Design Master @ V1: VOLUME,
FULL_BASELINE, REF_WIRE, FPT_L/R + mate connectors), Mirror_TAC (tip -X), Datum_x500_z10, EI_const_150 (sketch line on
Front at z 150 mm = EI 150 N*m^2), and P1..P10 (names carry the expectation; P7 datum = MRS connector with ORIGIN ->
P1 x - 885, s(MRS) = 0; P8 target EI + block names -> 132.46 mm/30kg = P L^3 / 48 EI, EI band; P9 COORDINATE_SYSTEM on
the world-aligned MC = P5; P10 = P1 with the dashed grid on, labels off). Build: `PYTHONPATH=. [PRIMITIVE_STUDIO=<name>] python devtools/onshape/build_primitive_tests.py [P1 ...]`;
check: `PYTHONPATH=. FS_SYNC_TIMEOUT=300 python devtools/onshape/check_primitive_tests.py [--dump | --json out |
--before snapshot.json] [--unsuppress] [--keep P1,P8]` (many Export primitive instances make a studio slow: after
checking, `--keep` suppresses every case but those) -> all pass (2026-09-28 evening, see the session report; first
59/59 run in the copy studio "Primitive tests (agent)" 5c3ac8fb8ec70b1f256c0e97
while the user was working in "Primitive tests"; --before compared P1..P6 with the phase-1 code: unchanged except the
removed rows). avg R 17.0496, natural 17.4824 / 16.1851 m, RSL 1480, FRCPl 130.0, ARCPl 50.0.
2026-09-28 (b): P6 = plot region RSL (avg R now = P1); P11 extra key points (MP connector "FB_Mass_location" with key
line, Datum_x500_z10 "Mass AB") + key lines + region WIDEST -> 23 data rows; P12 Plot CURVATURE + region INFLECTION;
P13 duplicate names "FB mass" / "FB_mass" -> ERROR. The checker reads plot / reference / tick x-ranges (member
extents) and glyph-loop counts (EI "150" = 4 loops, no "+"). 100/100 in the agent studio; --before vs the morning
snapshot: tables identical except P6's average radius. Speed-ups (chain-at-x bisection + per-point Newton exit,
baseline-from-volume reuses the bottom frame's chain / lookup, data-table footprint crossings batched): P1 alone
7.7 s -> 3.0 s.

### Unwrap through base sections (2026-09-28, the user's tail bite)
Bug: the footprint mapped each periphery point x -> s along the mid-plane BOTTOM wire at the same x and clamped at its
end, so a bite cut into the tail ("Primitive tests" Sketch 1 / Extrude 1: R 79.12 mm circle at (-31.43, 3.38) mm, REMOVE
through all) collapsed into a vertical line; the vertical bite wall also made the TAIL extreme point a tie, the bottom
wire climbed the wall and the wall counted as a base face. Fix (option 2 of the brief, all in primitive_footprint /
primitive_profiles; no new import):
* the base FACES (only) are cut by planes y = 0, +-0.45 and +-0.9 of the periphery's largest |y| (one opIntersectFaces);
  every run of a level is a section, aligned at x(MRS) (u = x(MRS) there) or, when it misses the waist or is split by a
  bite, to the nearest-in-y section it overlaps in x;
* each sample maps by its FOOT in XZ (nearest point: Newton on a cubic-Hermite table in plain numbers, then one exact
  kernel correction; exact without the kernel over line edges) on the levels just below / above its y, u interpolated
  in y; radius / curvature use the same map's derivatives (header of primitive_footprint.fs);
* points no section reaches get sections at their own y (a periphery point lies on the base, so that section reaches
  it; at most 24 extra levels); tangent extrapolation of the nearest section only beyond all base geometry;
* profile extreme points: ties within 1e-10 m go to the LOWER point (a vertical end wall ends the bottom at its foot).
On a base extruded along y the unwrap is an isometry: the attribute's `footprint.unwrap` records sections, exact /
fitted edges, sourceLength / unwrappedLength (mm) and baseArea (mm^2). P14 (bite on a copy-in-place of the volume,
FULL_BASELINE, footprint from VOLUME): length 3693.7816 vs 3693.7817 mm, area 192315.4 vs 192315.4 mm^2, footprint
on the centreline ends at u(TAIL) 47.42 (the bite's depth), only the bite adds corners (73 / 71 deg), radius band
beyond the bite = P1 within 0.016 mm plot height, no spikes through it, data rows / radius metadata = P3. P1-P6
tables identical to before at 1e-6 mm. 117/117 in "Primitive tests (agent)" (left with P1 + P8 active; the bite
fixture features stay, P14 suppressed). Regen (REST re-post, wall): P1 3.02 -> 3.55 s, P14 4.09 s. Option 1
(driven_offset V17 unwrapChart) not used: its chart is ONE reference wire extruded along y, so it needs the same base
sections to reach a bite (the centreline wire ends there) and would add a cross-document pin chain
(edge_offset_utils -> curve_core).

Open: SW rout table (Table 4), ISO min thickness, Tip_height / Tail_height definitions, drawing template; the
radius plot and data table use the +y side only; the base periphery misses base faces that do not touch the mid
plane; a wrapped (3D) INPUT footprint is mapped on the bottom wire alone (untested); the Tip / Tail block WIRE -> name editing logic is untested in the UI (REST inserts skip editing logic);
drawing views render every wire BLACK (tested 2026-09-28: APPEARANCE colours show only in the Part Studio; PDF export
had no colour); with a COORDINATE_SYSTEM datum the EI band maps world x through the datum's x axis only;
analyzeBaseline (xSection) FCP/ACP sample-grid snap: FIXED upstream, re-pinned to V58 2026-09-29 (workaround gone).

### Pass 5 (2026-09-29): V58 re-pin, dialog audit, fixed chart axes, simpler unwrap
* xSection V58 (ad6a3958dd2e8873f6dd0b0d; analyzeBaseline tab microversion d8b52bb8 = the exact-FCP/ACP fix) pinned in
  primitive_baseline and export_primitive; the exactContacts / distanceToLine re-measure is gone. Tables unchanged at
  1e-6 mm (FRCPl 130, ARCPl 50).
* UI audit items 1-37 (research/ui_audit_2026-09-28.md, marked there): labels, dropdowns instead of HORIZONTAL_ENUM
  (Baseline / Footprint source, Plot type, Tip / Tail block name source), enum names, groups (Export primitive:
  "Baseline and footprint source", EI settings under Stiffness, row settings under Data table, "Tooling blocks
  (Table 5)"; Station geometry: Part and datum / Views / Stations / Geometry to create / Debug), descriptions. Ids
  unchanged. Station definition "Also store as # variable (optional)" default "" for NEW features (saved ones keep
  "stations", verified on the fixture); Custom view name default "" (empty = regenError); station entry "Point / first
  point" (two-branch declaration is refused, correction 46); table "6 Data (FCP to ACP)".
* Fixed chart axes: the radius / curvature band's axes, reference line, grid and numbers span the FULL footprint (u) and
  a fixed range -- radius -"Radius axis min (m)" (radiusAxisLow, 10) .. +Max radius (50 m); curvature -"Curvature axis
  min" (0.02 1/m) .. +"Max curvature" (0.1 1/m), "Curvature plot scale" default 50 mm per 0.01 1/m (was 5). Plot x-range
  cuts only the plotted data (plus key-location / junction ticks); data outside the axis breaks the line (Liang-Barsky
  clip, primitiveClipPolyline). EI band: frame over the volume's x extent, 0 .. "EI axis max" (450 N*m^2). Band sizes
  no longer depend on the data: P1 / P6 / P11 / P15 (any region) stack identically. attribute bands: frameFrom/To,
  axisFrom/To, radiusLimit, radiusAxisMin | maxCurvature / curvatureAxisMin, eiAxisMax, eiFrameFrom/To.
  Visible change on existing features: radius axis now to +50 m (was up to the data, 30 m on TAC): the radius band is
  200 mm taller and sits 200 mm lower; with a target EI the EI band runs to 450 (was 150 on P8): everything below it
  moves down 150 mm. TAC's full-ski curvature: the tip / tail arcs (-0.98 1/m) are cut, only the sidecut shows.
* Correction 62: a NEW length parameter's default migrates into saved features in mm (10 m -> 10 mm), so the radius axis
  min is a plain number in m under a new id.
* Unwrap simplified (user: the base is developable): points map on the centreline bottom wire; where it is cut back, ONE
  base section per end at the y where the base reaches furthest; no y interpolation. Every table value of P1-P16
  identical to before (stored 1e-4 mm); P14 2 sections (was 20). Regen: P1 3.71 -> 3.23 s, P14 4.24 -> 3.19 s.
* Tests: P15 (radius, region INFLECTION), P16 (curvature, region FULL), SD1 (new Station definition, spec default);
  P12 rebuilt at the new 50 mm default. 135 / 135 in "Primitive tests (agent)" (incl. --before P1-P6 vs the session
  start); after the unwrap change 129 / 129 + every value = the pre-change snapshot. Left with P1 + P8 active, SD1 kept.
