# publish_tools -- "Publish & Drawing tools" document

Onshape doc 73271cfcd708b3f5e3fc315c (wid fea83d30106dba0a4e066761). Test Part Studio: "Part Studio 1"
(bb2cddb24faf53d9d043c38e). Purpose: generated drawing-aid geometry to replace the hand-built
drawing sketches (Part_Buyoff/Base/BF/Core/SW_Sketch) in ski Parts studios -- their breaking is the
pain point. Background research: memory `mbd-drawing-research`.

## Tabs

| tab | element | what |
|---|---|---|
| station_utils.fs | 8a8c023e223cf0814d973a63 | station entry predicate (Point / Along a line / Between two points), resolve, station-set schema `stationSet/1` |
| station_definition.fs | 8b522e33d07f67c10c37ba11 | "Station definition": writes map variable (default `stations`) = resolved stations, plain values only |
| station_geometry.fs | 5d3299cf520723444c27b273 | "Station geometry": per view (plan = datum XY, profile = datum XZ, other = MC XY) outline wires, optional region surface, station lines, datum point, grouped WITH THE PART in an OPEN composite `<prefix> <VIEW>` (excluded from BOM); optional FLAT copy at datum (DXF); station table + per-station queries embedded (extract_outputs V1) |

## Decisions (user, 2026-09-24)

- OPEN composite (part stays its own body; closed would need a duplicate part). Open composites
  can't move, so the datum frame changes numbers (table) and the FLAT copy, not the composite.
- Station inputs are queries (MC, vertex); Line + N: ends always included, measure PERPENDICULAR to
  the line; stations live in a reusable map variable picked by name in publish features, plus
  per-feature extra stations.
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
