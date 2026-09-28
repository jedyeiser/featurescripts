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
