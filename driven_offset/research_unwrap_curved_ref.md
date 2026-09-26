# Unwrap PART mode on a reference curved along its whole length (2026-09-25)

Question from the owner: the first big real test will be a baseline with curvature everywhere, the core must not be
moved rigidly, the budget is 0.01 mm, and it should take seconds. What does today's Part mode do there, and how should a
curved-everywhere part be rebuilt?

Everything here ran through the FS eval API in throwaway contexts on "Unwrap_Testing Copy 2" (element 681a5825).
Nothing was pushed, posted or saved. No .fs file in the repo was edited. Scratch: `scratchpad/curvedref/` of session
51e06872 (section 9).

## 0. Summary

| Question | Answer |
|---|---|
| Does today's `unwrapSolid` work with a curved-everywhere reference? | **No, not for the core.** CORE fails with `SPLIT_FAILED` after 14-19 s (both references, exact or squared walls). The base "succeeds" but is **silently wrong** (volume x0.940, 12 mm holes) with exact walls; with squared walls it is right (0.17 um) but takes 10 s and its sidecut spline hits the 500-control-point cap. Sidewall 4103 fails with `SPLIT_FAILED` (10 s). The small parts already over curves (4802, 4803) are unchanged: 2.8 s / 3.9 s, 0.8 um. |
| Are the test parts made of PROFILE faces and chart-normal WALLs over a curved reference? | Profile faces: yes (they are world Y-extrusions, which does not depend on W). Walls: **only approximately**. The test parts were modelled over REF_WIRE's straight middle, so their walls are world-vertical. Over a 4 mm camber they lean by up to 0.0014-0.0016 rad (11 um across a 12 mm core wall). Parts modelled wrapped round the real baseline (as 4802/4803/base are round the tip) would have exact chart-normal walls. |
| "Manufacturing" rebuild (plan outline x Z INTERSECT side profile x Y, per band) | **Works, automatically, and fast.** Prototype "PRISM": two 2D arrangements (side view, plan view) of the exactly mapped face rows; membership of (plan cell, side cell) products by point tests along flat columns; plan cells grouped by their set of solid side cells = the bands; one extrude-intersect per band; union. The CORE comes out as exactly the 3 bands you would draw by hand (inner body, ledge ring, 8 groove strips). |
| PRISM numbers, CORE | 6.1 s (REF_WIRE), 7.0 s (FULL_BASELINE), 7.3 s (4 mm camber). Profile faces <= 0.35 um, walls <= 1.25 um on REF_WIRE, <= 4.7 um on the cambered references (their world-vertical walls squared, lean included), edges 8.29 um at the known tolerant source vertex. Reverse check (result faces mapped back) <= 4.7 um. Boolean difference against the rigid result on REF_WIRE: empty both ways. |
| PRISM numbers, other parts (camber) | 4802 1.4 s (40 um: its 1.2 deg nose corners are squared; the cell method keeps them exact at 0.8 um), 4803 1.0 s (5.3 um at a tight corner with 4 mm rows; 0.84 um with 0.25 mm rows), base 2.5 s (0.53 um), 4103 3.1 s (0.79 um). 4401: refused (faces that are neither profiles nor walls). |
| Face-by-face exact mapping + opEnclose | Not competitive. `opEnclose` is not an arrangement engine (2 crossing sheets + box -> 1 solid, not 4). With each sheet pre-split by all others and only on-boundary pieces kept, it works on the base (0.53 um) and on the core over REF_WIRE (0.44 um, but 251 faces, ~11 s), and fails on 4803/4802 (`SPLIT_FAILED`) and the cambered core (enclose fails). |
| Recommendation | Replace the cell rebuild of curved pieces by PRISM; keep the rigid move for LINE spans; keep the cell rebuild as the fallback for leaning walls that must stay exact. Add adaptive row sampling and a cheap reverse check (section 7). |

## 1. Setup

References (all start at the MC StjLB alignment, d = 0 unless noted, cs = world):

| Name | What | Chart self-check (W's own samples, max \|v, h\|) |
|---|---|---|
| REF | REF_WIRE RtjD: R900 arc, line 145..1625, R406 arc, spline. The parts were built over it. | 0 |
| FULL_BASELINE | RjRL, present in Copy 2 as well (same derive): 6 edges, 10-CP spline 195..1495 with ~4 mm camber, deg-2 blends, R406 arc and a spline, but the tip is RAISED (z 18.2 at x 1708.6 vs 8.9 on REF_WIRE). | 0.136 um |
| CAMBER | Built in the eval context: REF_WIRE with its line replaced by `z = 4 mm * sin^2(pi (x - 145) / 1480)`, 75-point `opFitSpline` with horizontal end derivatives (G1 to both arcs). Curvature everywhere, two inflections, max slope 0.0085. Tip and tail are REF_WIRE's own edges, so 4802/4803/base tips stay consistent. | 0.024 um |

Accuracy measures (same as research_unwrap_part.md 8): **forward** = 7x7 on-face samples per source face and 5 per
edge, mapped with `unwrapFast`, `evDistance` to the result faces; **reverse** (new) = 6x6 samples on every result face
mapped back with `unwrapInverse`, `evDistance` to the source faces. The reverse check is needed: extra material is
invisible to the forward check. Times are single sequential eval requests without the checks (API + chart ~0.3 s).
**Never run eval requests in parallel**: a parallel batch made a 2.7 s run take 80 s.

`evVolume` is not a correctness measure here: on the core, PRISM and the rigid result differ by 4.6e-4 in `evVolume`
while their Boolean differences are empty both ways. It is representation noise of extruded-spline bodies.

## 2. Task 1: today's `unwrapSolid` on a curved reference

`unwrap_part.fs` as in the repo today (harness `run.sh` / `test.tmpl`, built from the current files).

| Part | Reference | Result | Time | Cells kept / total | Vol ratio | Worst forward error |
|---|---|---|---|---|---|---|
| CORE Rtjn | CAMBER, exact walls | `SPLIT_FAILED` | 14.2 s | - | - | - |
| CORE | CAMBER, squareWalls | `SPLIT_FAILED` | 19.3 s | - | - | - |
| CORE | FULL_BASELINE, exact / squared | `SPLIT_FAILED` | 14.7 s / ~20 s | - | - | - |
| base RtjP | CAMBER, exact walls | **wrong, no error** | 8.2 s | 24/180 | 0.940 | **12.0 mm** (598 of 2418 samples > 10 um) |
| base | CAMBER, squareWalls | ok | 10.0 s | 11/93 | 1.000189 | 0.165 um; console: sidecut chain of 1251 points hit the 500-CP cap |
| 4103 RtjH | CAMBER | `SPLIT_FAILED` | 10.2 s | - | - | - |
| 4802 Rtj7 | CAMBER (= REF under it) | ok | 2.85 s | 5/108 | 1.012562 | 0.794 um |
| 4803 Rtj3 | CAMBER | ok | 3.9 s | 5/240 | 1.003707 | 0.821 um |
| base / 4802 | FULL_BASELINE | FAIL faces (2 / 4) | 9.8 / 11.1 s | - | - | their tips do not follow FULL_BASELINE's raised tip; not a code problem |

Why the base is wrong with exact walls: its world-vertical sidecut walls lean 0.000275 against the camber, so the long
middle sidecut becomes a RULED chain whose ends meet the tip/tail WALL chains G1. The ruled tool's linear extension
grazes them (the "RULED end" hole in the grazing guard, research_unwrap_part.md 8b), the splits leave cells that
straddle the boundary, and the single centroid test per cell keeps or drops the whole thing. Nothing throws. This is
the strongest argument for a reverse check in production, whatever rebuild is used.

## 3. Face census on the curved references

`faceKind` shares (b = across the reference plane, c = along the chart normal) on a 9x9 grid:

| Part | CAMBER | FULL_BASELINE |
|---|---|---|
| CORE (90 faces) | 20 PROFILE, 42 WALL, 28 LEANING, max c 0.00138 (lean 11.3 um over the 12 mm walls of the tight sidecut blends at x 508-521 / 1095-1108) | 22 PROFILE, 24 WALL, 44 LEANING, max c 0.00155 |
| base (40) | leaning <= 0.000275 (0.3 um over 1.2 mm) | 2 FAIL (tip) |
| 4103 (41) | 20 leaning, max 0.00066 | - |
| 4802 / 4803 | as on REF (nose corners 0.0215 / 0.00015) | 4 FAIL / 10 leaning (tip, tail) |
| 4401 (46) | FAIL: top faces are not exact Y-extrusions (b up to 1.3e-3), lean up to 0.025 | FAIL |

So: profile faces are exact on any reference; walls are chart-normal only if the part was modelled wrapped round
that reference. Squaring a wall at its mid-height costs lean x height / 2: <= 6 um on this core over a 4 mm camber.

## 4. The PRISM rebuild ("manufacturing" rebuild, generalised)

Observation: every face is a PROFILE (image = extrusion along flat Y of an XZ curve) or a WALL (image = extrusion
along flat Z of an XY curve). Extend every mapped face row to a full curve. In the SIDE view the profile curves cut
the XZ plane into **side cells**; in the PLAN view the wall curves cut the XY plane into **plan cells**. Every 3D cell
of the full arrangement is (plan cell x Z) INTERSECT (side cell x Y) (or a component of it), and part membership is
constant on each such product (measured: 5334 point tests on the core, 0 conflicts). So:

```
rows      = classify + one exactly-mapped row per face (unwrap_part.fs faceRow; LEANING -> squared WALL);
            x-facing faces (PROFILE with c < 0.05, or WALL with b < 1e-6) get a row in BOTH views
chains    = chainRows + dedupe (distinctChains, with a bounding-box prefilter)
side      = planar sheet in y = lo - 1 mm, opSplitFace by all PROFILE curves -> side cells
plan      = planar sheet in z = lo - 1 mm, opSplitFace by all WALL curves    -> plan cells
columns   = x-probes (midpoints between chain ends / extension ends / x-extrema, plus every cell's centroid x);
            at each: z-intervals between side-curve crossings, y-intervals between plan-curve crossings;
            locate each interval's cell (bounding boxes, qContainsPoint only when ambiguous);
            one point test per unknown (plan cell, side cell) pair: unwrapInverse + qContainsPoint(part)
groups    = plan cells keyed by their set of inside side cells          (= the bands)
per group = extrude(plan cells, Z) SUBTRACT_COMPLEMENT extrude(side cells, Y)
result    = union of the groups
```

Chain ends: an end lying on another chain of the same view (within 5e-5 m) is extended by an overshoot of 20 um (so
the curves really cross); a free end is extended along its tangent to the box. Straight chains are finite segments
with the same end rule.

What PRISM found by itself:

| Part / reference | Groups (bands) | Side / plan cells |
|---|---|---|
| CORE, any reference | 3: 9 plan cells x 5 side cells (inner body: bottom..top), 2 x 2 (the ledge ring between the lower and upper outlines: ledge..top), 8 x 3 (the 8 groove strips: floor..top) | 6-7 / 21-59 |
| 4802 | 2 (main body; the butt notch at x 750.5) | 3 / 11 |
| 4803 / 4103 | 2 | 3 / 27, 4 / 8 |
| base | 1 (a plate: one plan outline x one side band) | 3 / 2 |

### 4.1 Tested snippets (from `curvedref/prism.tmpl`)

2D arrangement of one view (curves are `emitSplineCurve` fits at 0.1 um with exact end tangents, or lines):

```featurescript
const P3 = function(p) { return profile ? vector(p[0], lo[1] - 0.001, p[1]) * meter : vector(p[0], p[1], lo[2] - 0.001) * meter; };
// per chain: the curve, then each end: overshoot if it lies on another chain of this view, else to the box
const len = onOther ? 2e-5 : reach;
opFitSpline(context, cid + ("ext" ~ k), { "points" : [P3(e.p), P3([e.p[0] + len * e.t[0], e.p[1] + len * e.t[1]])] });
// the sheet: a line along x extruded across the other coordinate, split by every curve at once
opFitSpline(context, aid + "base", { "points" : [P3([x0, c0]), P3([x1, c0])] });
opExtrude(context, aid + "sheet", { "entities" : qCreatedBy(aid + "base", EntityType.EDGE),
        "direction" : profile ? vector(0, 0, 1) : vector(0, 1, 0), "endBound" : BoundingType.BLIND, "endDepth" : (c1 - c0) * meter });
const tools = qSubtraction(qOwnedByBody(qBodyType(qCreatedBy(aid, EntityType.BODY), BodyType.WIRE), EntityType.EDGE),
        qCreatedBy(aid + "base", EntityType.EDGE));
opSplitFace(context, aid + "split", { "faceTargets" : qOwnedByBody(qCreatedBy(aid + "sheet", EntityType.BODY), EntityType.FACE),
        "edgeTools" : tools });
```

Column membership (one point test per unknown pair):

```featurescript
for (var j = 0; j < size(zm); j += 1)              // zm: z-interval midpoints at xp, zc: their side cells
{
    if (memb[pci]["" ~ zc[j]] != undefined) { continue; }
    const w = unwrapInverse(chart, xp, y, zm[j]);   // plain metres, world
    const inside = !isQueryEmpty(context, qContainsPoint(part, vector(w[0], w[1], w[2]) * meter));
    memb[pci]["" ~ zc[j]] = { "i" : zc[j], "inside" : inside };
}
```

One band:

```featurescript
opExtrude(context, gid + "plan", { "entities" : qUnion(g.plan), "direction" : vector(0, 0, 1),
        "endBound" : BoundingType.BLIND, "endDepth" : (hi[2] - lo[2] + 0.004) * meter });
opExtrude(context, gid + "side", { "entities" : qUnion(sideFacesOfGroup), "direction" : vector(0, 1, 0),
        "endBound" : BoundingType.BLIND, "endDepth" : (hi[1] - lo[1] + 0.004) * meter });
// several disjoint plan prisms each intersected with the side prism (INTERSECTION would intersect them all together)
opBoolean(context, gid + "int", { "targets" : qCreatedBy(gid + "plan", EntityType.BODY),
        "tools" : qCreatedBy(gid + "side", EntityType.BODY), "operationType" : BooleanOperationType.SUBTRACT_COMPLEMENT });
```

Other FS facts learned: `evRaycast` costs ~8 ms per ray on the 90-face core versus ~0.45 ms for
`qContainsPoint(body, point)`, so point tests beat rays for parity; `qContainsPoint` over a union of 60 sheet faces
costs ~7 ms, hence the bounding-box prefilter (2081 locates, 680 kernel calls); lambdas capture outer variables by
value (a counter cannot be incremented from inside one); `opExtrude` of edges from several wire bodies at once gives
`EXTRUDE_INVALID_ENTITIES` (extrude one wire at a time); `box` is reserved (`ch.box` does not parse).

## 5. PRISM results

Prototype settings: rows every 4 mm (min 17 per face), 5x5 classification grid, extent from edges every 20 mm,
fits 0.1 um, flatTolerance 0.001 mm, walls squared. Time = request, no checks, sequential.

| Part | Reference | Time | Forward worst: profile / wall / edge (um) | Reverse worst (um) | Notes |
|---|---|---|---|---|---|
| CORE | REF | 6.1 s | 0.06 / 1.25 / 8.29 | 0.07 | 8.29 = the known tolerant source vertex. Boolean diff vs today's rigid result: empty both ways |
| CORE | CAMBER | 7.3-7.5 s | 0.06 / 4.65 / 8.29 | 4.73 | 28 walls squared (lean <= 0.00138) |
| CORE | FULL_BASELINE | 6.9-7.0 s | 0.35 / 3.35 / 8.30 | 3.45 | 44 walls squared (lean <= 0.00155) |
| CORE | CAMBER, d = 7 mm | ~7.5 s | 0.06 / 4.65 / 8.29 | 4.73 | the chart's d is transparent to PRISM |
| 4802 | CAMBER / REF | 1.4 / 1.0 s | 41 / 37 / 44 | 36 | nose corners (1.2 deg lean) squared; = today's squareWalls (43.6 um). Exact with the cell method: 0.79 um |
| 4803 | CAMBER | 1.0 s | 0.14 / 5.3 / 0.84 | 5.9 | 5.3 um = 4 mm rows across the tight corner Stjuk; with 0.25 mm rows 0.84 / 1.42 um (2.3 s) |
| base | CAMBER / REF | 2.5 / 2.0 s | 0.04 / 0.53 / 0.50 | 0.54 | |
| 4103 | CAMBER / REF | 3.1 / 2.6 s | 0.06 / 0.77 / 0.79 | 0.27 | today's code: SPLIT_FAILED on CAMBER |
| 4401 | any | refused | - | - | FAIL faces (b up to 1.3e-3). With the profile tolerance relaxed to 2e-3: REF ok-ish (50 um, lean 0.025 squared), CAMBER **wrong** (vol x1.61), caught by the reverse check (6.9 mm) |

Time split on the CORE over CAMBER (7.3 s): classification + rows 3.1 s, chains + side arrangement 0.6 s, plan
arrangement 0.9 s, membership 1.6 s, bands + union 0.8 s. With 1 mm rows the core takes 13.5 s (long fits); with
0.25 mm rows it would be far slower. Faces out: 39-46 (in 90); the output is clean extrusions (EXTRUDED + PLANE).

### 5.1 Robustness: what failed while building it, and the fix

1. **Membership sampling must cover every x-slab.** First version probed 3 points per plan cell: on 4802 it missed the
   butt region (x 740..750.5) and dropped it (10 mm error). Fix: x-probes at the midpoints between all chain
   end/extension/x-extremum events plus every cell's centroid.
2. **Infinite straight lines graze.** Straight chains first extended to infinity: the tail end-top line (slope 0.008)
   crossed the top profile at a shallow angle 520 mm away, leaving a sliver side cell and 96-204 um errors on the core
   over FULL_BASELINE. Fix: straight chains are finite, with the junction/free end rule.
3. **Squared rows of neighbouring walls do not meet exactly** (mid-height rows of walls leaning differently are ~1 um
   apart), so a junction looked free and was extended across the part. Fix: junction tolerance 5e-5 m
   (= UNWRAP_PART_JOIN), overshoot 20 um.
4. **x-facing faces bound both views.** 4802's tilted nose plane (a profile face with c = 0.0217) existed only in the
   side view, so the plan cell beyond the nose spanned the full width: 17.9 mm of extra material at the nose, found
   only by the reverse check. Fix: faces with b ~ 0 and small c get a row in both views.

Still open:

5. **Leaning walls are squared.** Exact leaning walls cannot be a Z-extrusion. 4802's nose: 40 um. Options in 8.
6. **Non-conforming faces** (4401) are refused, or, if the tolerance is relaxed, can produce garbage silently. The
   membership conflict counter flags it (4802 nose: 7 conflicts, core: 0), and the reverse check catches it.
7. **Uniform row spacing** is both slow (long faces) and too coarse (tight corners). Use the adaptive rule of
   `adaptiveEdgeSamples` (seed coarse, split spans whose midpoint misses the cubic by > tol/4).
8. Tangent run-outs (groove floors onto the bottom) were handled by the 20 um overshoot without trouble here, but the
   arrangement near a tangency is the place to watch.

## 6. Task 3: face-by-face exact mapping + opEnclose

- `opEnclose` does not compute an arrangement: 2 crossing extruded sheets plus a box's faces -> **1** solid (not 4);
  with the box as a body -> `ENCLOSE_NO_REGION`. On the real tool sheets: `BOOLEAN_INVALID` / `ENCLOSE_NO_REGION`.
- Pre-trimmed version (each chain sheet split by all other sheets and extensions, `opSplitFace` with `bodyTools`;
  keep the pieces whose interior maps back onto the source boundary within 2 um; `opEnclose` the kept faces):
  - base over CAMBER: works, 0.53 um (same as PRISM), ~4 s;
  - core over REF: works, 0.44 um reverse, but **251 faces** (every sheet fragmented) and ~11 s;
  - 4803, 4802: `SPLIT_FAILED` (a sheet split by all the others fails where extensions graze);
  - core over CAMBER: the enclose fails.
- It needs every trimmed piece to close watertight and every duplicate piece removed; PRISM needs neither (its
  Booleans are of whole extrusions). Not recommended.

## 7. Recommendation

1. **Rebuild curved pieces with PRISM** (bands of plan x Z INTERSECT side x Y), not the 3D cell split. It is the only
   method that did the core on a curved-everywhere reference, and it is 2-4x faster on every other part. Its output is
   also what a CAM programmer would draw: per band, a flat plan outline and a flat side profile.
2. **Keep the rigid move for LINE spans** (it is free and exact, and it keeps native faces). On a curved-everywhere
   baseline it never triggers, so PRISM does the whole part.
3. **Keep the cell rebuild as the exact fallback** for pieces whose leaning walls exceed a squaring tolerance
   (lean x height / 2 > tol, e.g. 4802's nose), or offer squareWalls (8.1).
4. **Adaptive rows** (5.1 item 7) to reach 1 um at tight corners without the 13 s of uniform 1 mm rows. Expected core
   time with adaptive rows and deduped identical faces (8 groove floors, 16 groove walls): ~4-5 s.
5. **Reverse check always** (a few samples per result face mapped back with `unwrapInverse`, `evDistance` to the
   source faces; ~0.3 s at 3 samples per face). Throw, highlighting the source part, above the tolerance. It catches
   every silent failure seen in this study (base cells 12 mm, 4802 nose 17.9 mm, 4401 6.9 mm).
6. Chart: nothing to change. The packed chart is 0.024 um / 0.136 um on the new references, and d is transparent.

Limits of the recommendation: parts must consist of profile faces and (near-)chart-normal walls; anything else
(4401's twisted tops, fillets between a wall and a profile face, draped tops) is refused. A 3D fillet on a ski part
would need a separate path.

## 8. Decisions for the owner

1. **How are the real parts modelled over the baseline: walls normal to the baseline (wrapped), or world-vertical?**
   Wrapped (like 4802/4803/base at the tip): PRISM is exact. World-vertical (like the test core): each wall leans by
   (plan normal x) x sin(theta); on a 4 mm camber that is <= 0.0016 rad, <= 6 um after squaring on a 12 mm core.
   The flat blank that is CNC-cut square and then bent has chart-normal walls, so squared is arguably the truer
   blank.
2. **Leaning walls that matter (4802's 1.2 deg nose corners): square them (40 um, CNC truth) or keep them exact
   (cell fallback, 0.8 um, +1-2 s)?**
3. **Output topology:** PRISM merges tangent faces into single extrusions (core 90 -> 43 faces; sidecut cylinders
   become one spline surface). Is that acceptable, or should flat lines/arcs be recognised in the band outlines
   (curve_core `classifyPoints`)?
4. **Time vs accuracy knob:** 4 mm rows (core ~7 s, <= 5 um) vs adaptive rows (<= 1 um, expected ~4-5 s after the
   dedupe/adaptive work). I propose adaptive at tol/4 of a 0.005 mm budget.
5. **Refuse or approximate non-conforming faces** (4401)? Approximating needs the reverse check as a hard gate.

## 9. Files

Scratch (session 51e06872, `scratchpad/curvedref/`):

- `prism.tmpl` / `prism_final.tmpl`: the PRISM prototype and its checks (forward, reverse, per-kind worst); switches
  via `run.sh` env: `ST` (stop after stage 1-4), `DBG`, `CC` (test every column: conflict count), `BV` (both views),
  `JT`, `OV`, `PG`, `EN` (the sheet-split + enclose variant of section 6), `SI` (infinite straight lines, the old
  behaviour).
- `test.tmpl`: today's `unwrapSolid` with forward + reverse checks and the CAMBER option (`wdef.inc`).
- `run.sh TAG BODY CHECK SQUARE FLATTOL DOFF WIRE` (WIRE = RjRL, CAMBER or REF); `runf.sh` = same with a tunable copy of
  unwrap_part.fs (`up_fast.tmpl`: `RS` row spacing, `GN` grid, `ES` extent spacing, `PT` profile tolerance).
- `census.body`: face classification on any reference; `enc_test.fs`: the opEnclose arrangement test.
- `out/*.txt`: every run's output quoted above.

## 10. As built (2026-09-25): PRISM in `unwrap_part.fs`

`unwrapSolid(context, id, chart, cs, part, options)` keeps its signature. Straight spans: exact rigid move (unchanged).
Curved pieces: `rebuildCurvedPiece` = `prismAnalysis` -> `prismPiece` (inside `startFeature` / `abortFeature`, so a
failed attempt leaves nothing) -> `reverseCheck`; on refusal or failure, the old cell rebuild (`rebuildPiece`,
unchanged) as the exact fallback, reverse-checked too; if that fails as well, a regenError highlighting the source
faces that forced the fallback. Nothing was pushed; tested through the eval API only (scratch `prism_impl/`:
`t.tmpl`, `run.sh`, `batch.sh`, `summ.py`, `out/p_*.txt`).

Options: `squareWalls` (default false), `faceMode` "keep" (default) / "merge", `shapeTolerance` (default 0.005 mm),
`flatTolerance`, `print`. New report keys: `methods` (per piece: rigid / prism / cells), `prismPieces`, `cellPieces`,
`bands`, `approximatedFaces`, `approximationMax`, `fallbackFaces`, `reverseCheckMax` (ValueWithUnits); old keys kept.

### 10.1 What changed against the prototype (section 4)

1. **Classification by distance, not normal shares.** `viewFit`: on a 5 x 5 grid, per station along the face's curve
   direction, the across-face offsets along the view normal of the middle point; best fit = middle of their range,
   departure = half the range (a linear twist costs half its edge-to-edge). A face within `shapeTolerance` as a
   profile gets a side row, as a wall a plan row (both: x-facing faces). Rows are moved onto the best fit per station
   (interpolated). With `squareWalls`, a wall beyond tolerance leaning < 0.05 is squared (counted; its departure is
   added to the reverse-check limit). Any other face sends the piece to cells (`fallbackFaces`).
2. **Envelope rows.** A profile face with a usable plan normal that is not a wall (4802's 1.2 deg nose, 4103's tail
   cap) gets its plan row on its OUTWARD envelope: the side view carves it exactly and the plan cell beyond holds no
   material. This replaces the prototype's "squared at mid-height" (40 um on 4802). A fixed steepness cutoff
   (c < 0.05) missed 4103's tail cap over FULL_BASELINE (c = 0.0502): 5.1 mm of extra material, caught by the
   reverse check.
3. **"keep" faces**: chains are still built from tangent rows (topology unchanged), but every source face is its own
   edge (`emitChainParts`): a line / arc where the face's row is one within shapeTolerance / 10 (`classifyPoints`),
   else a spline with exact end tangents; joints averaged. Cylinders survive (base over CAMBER: 14 / 14).
4. **"merge"**: one spline per chain, fitted through dense uniform samples of the exact per-face curves
   (`emitMergedChain`). Fitting the raw rows rang across curvature jumps: 10-12 um on 4803's tight corner with
   adaptive rows, 16 um on the core with uniform 4 mm rows (both caught by the reverse check).
5. **Adaptive rows** (seed 8 mm, split spans whose mapped midpoint misses the cubic by > tol / 4): a clear win in keep
   mode. CORE over CAMBER 7.4 s adaptive vs 9.1 s fixed 4 mm at equal accuracy; 4803 0.93 um in 1.2 s (prototype
   5.3 um at 4 mm rows, or 0.84 um in 2.3 s at 0.25 mm rows).
6. **Arrangement robustness** (found on 4401 over CAMBER, whose side view stayed ONE cell): rows run over a face's
   parameter box, so two faces of one profile can overlap by mm in the view and dangle along each other without
   crossing -> `mergeOverlappingChains`; best-fit-shifted ends of near-tangent neighbours part by microns ->
   `snapChainEnds` (near-tangent meetings only; envelope rows never moved); junction test and overshoot grow to
   3 x shapeTolerance.
7. **Reverse check**: 16 points per result face (4 x 4 interior), `unwrapInverse`, `evDistance` to the piece's faces;
   limit max(shapeTolerance, 0.01 mm) + squared departures. 5 points per face missed a 12 um error; 16 cost ~1 s on
   the core.

### 10.2 Results

Eval API, sequential. Times are requests including the built-in reverse check (not the harness checks). keep mode
unless noted. "fwd / rev" = harness forward check (7 x 7 per source face + edges) / dense reverse check (6 x 6 per
result face), worst, um. cyl = cylinder faces out / in the source.

| Part | Reference | tol mm | Method | keep s | merge s | faces keep / merge / src | cyl | fwd / rev um | vol ratio |
|---|---|---|---|---|---|---|---|---|---|
| CORE | REF | 0.005 | prism, prism, rigid | 3.6 | 1.5 | 94 / 94 / 90 | 42 / 40 | 8.29* / 0.22 | 1.000059 |
| CORE | CAMBER | 0.01 | prism (3 bands) | 9.5 | 12.2 | 98 / 47 / 90 | 50 / 40 | 8.29* / 4.46 | 0.99995 |
| CORE | FULL_BASELINE | 0.01 | prism (3 bands) | 9.7 | 12.6 | 92 / 41 / 90 | 48 / 40 | 8.30* / 3.22 | 1.000003 |
| CORE | CAMBER | 0.005 | throws: 4 walls at 5.7 um, cells SPLIT_FAILED | 15.7 | | | | | |
| CORE | FULL_BASELINE | 0.005 + squareWalls | prism | 9.7 | | 92 | 46 / 40 | internal 2.8 | 1.000031 |
| 4802 | REF, CAMBER | 0.005 | cells (2 nose corners at 22 um) | 3.1 | 3.1 | 12 / 12 / 17 | | 0.79 / 0.41 | 1.012562 |
| 4802 | CAMBER | 0.005 + squareWalls | prism | 2.4 | | 20 | | 39.6 / 35.1 | 1.012552 |
| 4803 | REF, CAMBER | 0.005 | prism (2 bands) | 1.2-1.3 | 1.9-2.0 | 18 / 13 / 18 | 4 / 1 | 0.93 / 0.79 | 1.003684 |
| 4803 | FULL_BASELINE | 0.005 | cells (4 faces beyond) | 3.6 | 3.6 | 14 | | 0.42 / 0.11 | 1.002913 |
| base | REF | 0.005 | prism, rigid, prism | 1.5 | 2.6 | 34 / 30 / 40 | 10 / 14 | 0.22 / 0.15 | 1.000206 |
| base | CAMBER | 0.005 | prism (1 band) | 2.5 | 3.8 | 38 / 10 / 40 | 14 / 14 | 0.22 / 0.18 | 1.000233 |
| 4103 | REF | 0.005 | prism, rigid, prism | 1.4 | 2.7 | 36 / 36 / 41 | 16 / 21 | 0.67 / 1.08 | 1.000298 |
| 4103 | CAMBER | 0.005 | prism (2 bands) | 3.3 | 4.4 | 38 / 8 / 41 | 22 / 21 | 0.67 / 1.08 | 1.000354 |
| 4401 | REF | 0.005 | cells, rigid, cells | 2.0 | | 52 / 46 | | internal 0.23 | 1.000041 |
| 4401 | REF | 0.02 | prism, rigid, cells | 1.4 | | 53 | | internal 16.7 | 1.000041 |
| 4401 | CAMBER | 0.005, 0.02 | throws cleanly (2 / 1 end caps beyond; cells refuse the spline tops) | 4.2 | | | | | |
| 4401 | CAMBER | 0.03 | prism | 2.5 | 3.7 | 50 / 12 / 46 | 30 / 16 | 25.0 / 22.7 | 1.000003 |
| 4401 | FULL_BASELINE | 0.03 | prism | 2.4 | 3.8 | 50 / 12 / 46 | 31 / 16 | 29.0 / 29.0 | 1.000056 |
| 4802, base | FULL_BASELINE | 0.005 | throw: 8 faces beyond (their tips do not follow the raised tip, section 2) | 1.6-5.3 | | | | | |
| 4103 | FULL_BASELINE | 0.005 | throws: band rebuild misses by 0.083 mm at its tail, cells refuse 1 face | 8.3 | | | | | |

\* 8.29 um = the known tolerant source vertex (section 5). Cylinder counts above the source's are faces split across
bands, or source extrusions (Onshape type OTHER) whose mapped row is a true arc.

On REF_WIRE the rebuilt ends match today's output within the reverse check (0.15-0.79 um internal; the dense 6 x 6
check gives 1.08 um on 4103); the middle is the same rigid move.

### 10.3 4401: what actually limits it

Its twisted top faces are not the problem: as profiles they depart by at most 2.2 um (CAMBER). The blockers are the
two small x-facing end caps (tip 3 x 3 mm, tail 3 x 4 mm planes), skewed in plan (b 0.017-0.021) AND leaning (c
0.011-0.025): best single-view fit 16.8 um (tip, as a wall) and 24.9 um (tail, as a profile). So 0.02 mm still sends
the piece to cells (which refuse the spline tops: b up to 1.3e-3 > 1e-6); 0.03 mm goes through PRISM with the error
reported (25 um CAMBER, 29 um FULL_BASELINE). PRISM cannot represent a plane skewed in both views better than its best
single view (intersecting the two mid rows is worse).

### 10.4 Open

- 4103 over FULL_BASELINE: 83 um miss at its tail (x 856.8), where the part sits under the raised baseline tip. Caught
  and refused; not diagnosed further.
- CORE keep mode is ~2 s slower since `mergeOverlappingChains` (7.4 -> 9.5 s over CAMBER): the pair test is
  box-prefiltered but still FS-side polyline work.
- unwrap.fs (lead engineer): wire `faceMode` / `shapeTolerance`, remove the "no straight edge" guard.
