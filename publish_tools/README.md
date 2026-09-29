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
| primitive_types | ecde24520874030ab412c981 | enums (PrimitiveSource, PrimitiveDatumUse, PrimitiveRadiusBetween, PrimitiveTableKind), flat-baseline tolerance 0.01 mm, attribute name `publishPrimitive`, schema `primitive/1`, bounds |
| primitive_frame | 5808546b3b3d863d82796d24 | point / datum resolution, edge chains, chain-at-x (Newton), foot, normal crossing, [s, w, h] |
| primitive_profiles | 5865b24d55ff270a56088adf | mid-plane section -> BOTTOM / TOP / TIP END / TAIL END; Table 1 scale factors |
| primitive_footprint | fbc957543e769a649f00c5cc | base periphery, unwrap along s, radius (chain rule, exact), fpt_analyze (footprint V32), radius plot |
| primitive_baseline | b827b10bc0bdc678c2db28cd | analyzeBaselineGeometry (xSection V57) on a local copy + exact FCP/ACP re-measure; Table 5 |
| primitive_output | 6f122edb2547a6a46991d9fd | named points / segments, band stacking, closed composite + attribute |
| export_primitive | 3ce76ee786987ef00917b9b2 | feature "Export primitive"; target EI via xSection V57 xSectBeamAnalysis (getEIFromEdges, computeBeamStiffness) |
| primitive_table | 4e49f8a0b8f0c2bd75c919e7 | custom table "Primitive tables" |

Use: Export primitive -> Volume (the ski solid), FCP, ACP (vertex / point / mate connector), optional MP(s), optional
Datum (empty = world origin, world axes; X along the ski, Z up, profiles in its XZ plane) + Datum uses: ORIGIN
(default; a vertex / point / MC whose ORIGIN moves the frame, axes stay world X/Y/Z, so x is measured along world X
from the datum -- the ski's own connectors have Z along the ski) or COORDINATE_SYSTEM (the MC's own axes, as phase 1),
Target EI (optional EI wire, xSection convention: x = world X, height 1 mm = 1 N*m^2, like EI and Cross Section's EI
curve), Tooling blocks (Tip / Tail block: type a name, or pick the block's wire and editing logic copies its name,
correction 36), Baseline from Volume
(the section's bottom wire) or Input wires (e.g. FULL_BASELINE), Footprint from Volume (the base periphery) or
Input wires (flat FPT_L + FPT_R, taken as already unwrapped and aligned at MRS; or wrapped 3D wires), Average radius
between Contacts / Widest / Inflection (Table 2 average radius only), Data points N (+ force XS1 / MRS / XS2), Layout
(band gap 50 mm, radius plot limit 50 m, tick 10 mm), Query variable (default `primitive`). The name prefix fills from
the volume's name (editing logic).

Output: ONE closed composite `<prefix> PRIMITIVE` (excluded from BOM) in the datum XZ plane, BELOW the part, bands
top to bottom a band gap apart: BASELINE (+ points TIP/TAIL at the baseline's ends, FCP/ACP, and unless the baseline
is flat within the RSL FRCP/ARCP/MCL/FB_MIN/AB_MIN), PROFILE (BOTTOM, TOP, TIP END, TAIL END + points on the bottom
wire, TIP/TAIL at the section's extreme points along X = the bottom wire's ends, TOP FCP/ACP), FOOTPRINT (unwrapped: u = x(MRS) + s along the tip, y drawn
as height; exact arcs kept wherever the bottom is flat; points widest / waist / inflections), RADIUS (10 mm per 1 m,
sidecut +, taper/tip/tail -, breaks where |R| > limit, arcs = horizontal lines, joined across continuous junctions;
REFERENCE line; TICKs at FCP ACP MRS MP XS1 XS2 and FB/AB widest + inflection). Each band has a `<band> DATUM` point
at x = 0 on its reference line. All rows live in the composite's attribute `publishPrimitive` (schema primitive/1)
and in the producer slot (Extract variables keys: primitive, rsl, averageRadius, naturalRadiusWidest,
naturalRadiusInflection, taperAngleWidest, taperAngleInflection, deflection, stiffness (0 = no target EI); queries primitive, baseline, profileBottom,
profileTop, footprint, radius). Tables: add "Primitive tables" (filter "Primitives containing", pick "Table").

Definitions: s = arc length on the bottom wire from MRS, + towards FCP; h along the bottom wire's normal into the
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
Front at z 150 mm = EI 150 N*m^2), and P1..P9 (names carry the expectation; P7 datum = MRS connector with ORIGIN ->
P1 x - 885; P8 target EI + block names -> 132.46 mm/30kg = P L^3 / 48 EI; P9 COORDINATE_SYSTEM on the world-aligned
MC = P5). Build: `PYTHONPATH=. [PRIMITIVE_STUDIO=<name>] python devtools/onshape/build_primitive_tests.py [P1 ...]`;
check: `PYTHONPATH=. FS_SYNC_TIMEOUT=300 python devtools/onshape/check_primitive_tests.py [--dump | --json out |
--before snapshot.json]` -> 59/59 (2026-09-28, run in the copy studio "Primitive tests (agent)" 5c3ac8fb8ec70b1f256c0e97
while the user was working in "Primitive tests"; --before compared P1..P6 with the phase-1 code: unchanged except the
removed rows). avg R 17.0496, natural 17.4824 / 16.1851 m, RSL 1480, FRCPl 130.0, ARCPl 50.0.

Open: SW rout table (Table 4), ISO min thickness, Tip_height / Tail_height definitions, drawing template; the
radius plot and data table use the +y side only; the base periphery misses base faces that do not touch the mid
plane; the Tip / Tail block wire -> name editing logic is untested in the UI (REST inserts skip editing logic);
analyzeBaseline (xSection) takes FCP/ACP from its sample grid unless they are chain ends -- re-measured here, fix
upstream then re-pin.
