## Offset edges (OE) vs Driven edge offset (DEO): should they converge?

### Bottom line
- **One engine: yes. One feature: no.** OE should keep its tab, feature type, parameter ids, zone structure and emitter sub-ids. What should be shared is the math: the profile polynomials, transport frames and exact tangents.
- **I recommend against making OE a thin front end that calls DEO's full pipeline (`planOffset` + `emitOffset`).** It would change output topology and internal op ids (§3). That would probably break downstream picks in every document the moment its OE import is updated.
- **Retiring OE is not justified yet.** Create offset profile (COP) + DEO cannot reproduce four OE features:
  - Quadratic shape
  - Single-region pins that set only one component
  - Region extents taken from a point projected onto the path
  - Flat hold out to the path ends
- **The best long-term route to "one feature for new work":** give DEO a second profile source, "Regions (in dialog)", built on the same shared profile module as COP. OE then becomes legacy, and its fixes come for free through the shared libraries.

Nothing was edited. Scratch: one Python frame calculation only.

---

### 1. Capability matrix (V = verified in code, I = inferred)

| | Offset edges (`example_1/refSurfCreation/offsetEdges.fs`, FS 2909) | Driven edge offset (`driven_offset/driven_edge_offset.fs`, FS 3070) |
|---|---|---|
| Source input | One G1 path via curveMapping `buildFrenetPath`; it throws "Reference edges must be G1 continuous" (`curveMappingCore.fs:197`) (V) | Any chains, several links, G0 corners (`curve_core.fs:488-541`) (V) |
| Station coordinate | Arc length from the reference point's closest-point projection, signed along the traversal, with a Flip direction toggle (`offsetEdges.fs:904-905, 868-870`) (V) | World X, arc along source, or arc along reference wire. Zero = the arc where the chain crosses the zero point's **X** (`curve_core.fs:538, 643-696`). Chain always oriented by ascending X (V) |
| How the profile is specified | In the dialog: Regions (Linear / Quadratic / Smooth, dwell, per-region offset type) + consecutive-pair blends G0-G2; or Single region with pinned points (`:227-407`) (V) | A physical wire: X = station, Y = width, Z = height. Walked as a chain (`edge_offset_utils.fs:553-929`). COP builds that wire exactly (V) |
| Profile derivatives for blends | Finite difference, h = 1e-4 in t (`:1440-1502`) (V) | COP: analytic (`create_offset_profile.fs:518-535, 660-674`). DEO reads slopes from the curve (V) |
| Frames | Own Rodrigues parallel-transport table: max(100, 4 x points) uniform samples, **one unbatched `evEdgeTangentLine` per sample**, lerp between samples. Seeded at **s = 0** by `ptSeedNormal` (`:599-849`) (V) | Minimal-rotation `transportNormal` at each station (batched `evEdgeCurvatures` per edge). Seeded at the **zero station**. Width = the axis with the larger world-Y component, signs fixed by world Y and Z (`curve_core.fs:371-466, 986-1061`). Optional reference-surface and World frames (V) |
| Axis labels | "Normal offset" goes along `yAxis = T x N` and "Binormal offset" along N (`:1516-1517`). The labels are swapped, as the round-1 review found (V) | Width / height, with documented meaning (V) |
| Arcs | "Convert to splines" (the default; everything becomes a spline) or "Keep as arcs": a constant offset over a CIRCLE edge becomes an exact arc, a varying offset becomes a best-fit **biarc** (`:2157-2208`) (V) | Automatic: `classifyPoints` gives a line or arc where the source is a line or circle (gates at `driven_edge_offset.fs:1270-1272`). A varying offset over an arc becomes a spline. No biarc (V) |
| Blends / dwell | Hermite family G0-G2 (`:1600-1680`) and dwell (V) | Whatever the profile wire holds. COP: buffers = dwell, and the **same** lowest-degree Hermite family (`create_offset_profile.fs:625-674`) (V) |
| Flips | Flip direction / flip normal / flip binormal (V) | Signed profile values; alignment enum (V) |
| Where output splits | At every region boundary, every blend boundary and every source edge; never at interior pins (pins go to `interpolateIndices`) (`:2342-2496`) (V) | At every source edge, or only at corners and offset breaks. It splits at profile **kinks and steps** only (`discontinuityCoords`, `:1078-1104`), so a smooth G1/G2 blend joint is not split (V) |
| Output | One `opExtractWires` with id `id+"mergeWires"`; sub-ids `reg_i_j`, `blend_i[_j]`; unnamed (`:2379, 2458, 2507`) (V) | `run<r>`, `fill<r>`, `wire<link>` ids; named; corner arc / extend / trim; folds; terminals (V) |
| Speed | Several hundred unbatched kernel calls: table ≥ 100, 1 per sample point, 2-3 per `zoneTangent`, `evCurveDefinition` per piece. The editing logic rebuilds the table on every dialog change (`:98`). Never measured (I) | About 3-4 calls per edge, batched. 1.41 s with debug on in Design_Master (memory, 09-09; I) |
| Known bugs | Dead station loop (`:947-1010`, never runs); labels swapped; blends keyed by region name; sliver pieces when a region boundary is within ~1 mm of an edge boundary; stale pre-port copy in `pathProcessing.fs` used by 3 features (round 1) | Consumer-side break handling (None / Line) not done; open design question on length/width from the reference (memory) |
| Tests | **None** | Fingerprint baselines of Design_Master (default and complex); Evaluate offset 11/11; COP studio 9/9 (T6 edited by the user, fails by design) |
| variable_tools | None | `publishOutputs` with Variable_tools V1 (`:1350`) |

---

### 2. Can one engine serve both?

**Option A: thin front end calling the full DEO engine.**
- The front end would keep OE's dialog, build the region/blend profile in memory, and call `offsetStationBase` → `planOffset` → `emitOffset`.
- **Technically feasible.**
  - `profileAt` only reads `profile.edges[i].curve`, `minCoord` and `maxCoord` (`edge_offset_utils.fs:830-885`). An in-memory `profileFromCurves(curves, zeroX)` constructor is about 30 lines. COP's `bernsteinOf` already produces the exact curves as maps before `opCreateBSplineCurve` (`create_offset_profile.fs:966-1030`).
  - The frame difference is a **constant rotation**, because two parallel-transport fields differ by a constant angle. It can be absorbed by mapping OE's (n, b) channels into (width, height) with a 2x2 matrix computed once. To get the matrix, transport `ptSeedNormal` through DEO's station tangents with the exported `transportedNormals` (`curve_core.fs:404`).
- **Why I still don't recommend it:**
  1. **Topology changes (V):**
     - DEO makes lines and arcs where OE's default makes splines.
     - It splits at dwell and pin kinks where OE does not.
     - It does not split at smooth blend joints where OE does.
     - It has no biarc.
     - DEO's spline emitter has no interior pins (`curve_core.fs:1182`, no `interpolateIndices`).
  2. **Sub-ids change** (`run<r>` / `wire<link>` instead of `reg_i_j` / `mergeWires`). Downstream picks in the many documents would likely stop resolving once their import is updated (I: needs a live check).
  3. **Zero point.** DEO's X-plane zero throws "zero point's X value is not spanned" (`curve_core.fs:695`) where OE clamps a projected point. It also chooses the wrong crossing on a chain that is not monotonic in X.
  4. **Hooks needed.** Keeping parity would take engine hooks for all of the above: shape gates off, a break list, id naming, a zero arc, biarc. That makes DEO's pipeline the place where OE's quirks live.

**Option B (recommended): share libraries, keep two front ends.**
- Move the profile math into a shared module in Curve_tools, so both documents can import it (cross-document, by version). This covers the segment polynomials: linear / quadratic / smootherstep / Hermite blend / hold, analytic value, d/dx and d²/dx², and Bernstein conversion.
- Consumers:
  - COP uses it to build wires.
  - OE uses it to evaluate its zones, replacing `computeOffsetsAt` and `computeOffsetDerivativesAt` (FD).
  - DEO can later use it for an in-dialog profile source.
- OE's frames: replace the table's unbatched calls with batched `evEdgeTangentLines` per edge, then `transportedNormals(tangents, 0, ptSeedNormal(...))`. Keep the seed rule and the table positions, so the offset side and results stay the same.
- Move OE's biarc and arc emitters (`:1867-2147`) into `curve_core`. DEO can then offer "varying offset over an arc → arc pair".
- OE's zones, split points, sub-ids and `mergeWires` stay untouched, so topology is preserved by construction.
- Importing a newer-FS module from OE works today: OE is FS 2909 and imports the core at FS 3008 (V). Whether a 3070 module imports cleanly is untested (I).

**Option C: retire OE (freeze it, add "(legacy)", migrate documents to COP + DEO).**
- Blocked on the four gaps listed in the bottom line.
- COP's "At point" station is world X (memory, 09-25). With DEO measuring "Along source", X is read as **arc length**, so point-picked stations mean something different from OE's projected arc.
- Per-document migration is possible: run Evaluate offset on an existing OE output, which absorbs the frame difference and matched within 5.5 µm in its tests. The profile then becomes sampled rather than editable, and downstream picks must be re-pointed by hand.
- Reasonable as a policy for **new** work, not as a migration of the existing documents.

---

### 3. Numerical parity: where results would differ

- **Frames on planar chains:** both transports keep the in-plane normal exactly. The only difference is sign or axis role, which the 2x2 map absorbs. Difference: 0 up to rounding (V by construction).
- **Frames on 3D chains.** Python check: one-turn helix, R100, pitch 50, L = 630 mm.
  - OE seeded at s = 0 vs DEO seeded mid-chain: **14.28°** constant roll, matching τ·L/2 exactly. At a 10 mm offset that moves points by 2·10·sin(7.14°) = **2.49 mm** unless the channel map is applied.
  - After the map, the remaining difference is discretization. OE's 100-sample lerp table is 0.021° from exact Bishop (**3.7 µm** at 10 mm). At n = 25, 50 and 200 samples it is 63 µm, 15 µm and 0.9 µm.
  - DEO evaluates at the station itself, with no lerp, so it should be at or below OE's error (I).
- **Station zero:** identical when the reference point lies on a chain that is monotonic in X. It differs, or DEO throws, when the point is off the chain, beyond its end, or the chain doubles back in X.
- **Arcs:** OE "Convert to splines" gives splines within the 0.01 mm default tolerance; DEO gives exact arcs and lines, so the curve type changes. OE "Keep as arcs" with a varying offset gives a biarc, 0.0075 mm from the true offset on New_Test (memory); DEO gives a spline.
- **Blends:** same polynomial family (unique interpolant), so the only difference is OE's FD derivatives. Order-h² error centrally; first order at a one-sided kink (I: well below 1 µm for typical values).
- **Fits:** different sample sets. OE uses 20 points per region by default (`:61`); DEO uses station spacing. Expect differences at the approximation-tolerance level, about 10 µm or less (I).

**Parity test set.** Reuse OE1-OE17 from `utility.md` §4, in one studio with instances placed 1000 mm apart. For each case, compare:
- max point deviation at 200 arc-length samples
- edge count
- curve type per edge
- end tangents (degrees)
- the ids of the output wire's edges (the reference-survival check)

Add these cases:
- **OE18:** 3D helix with a varying offset (the frame roll check; must be ≤ 5 µm after the map).
- **OE19:** reference point off the chain / beyond its end.
- **OE20:** Single-region pin that sets only one component.
- **OE21:** region extent from two projected points on a curved chain.
- **OE22:** a downstream feature (e.g. an Extrude edge or a sweep) that picks OE output edges. Run it before and after the change to prove references survive.

Pass criteria:
- Option B: identical topology and ids, max deviation ≤ 1 µm on planar cases and ≤ 5 µm on 3D.
- Option A: would fail topology on OE1-3, OE5-8 (I).

---

### 4. Plan

| Phase | Work | Effort |
|---|---|---|
| **P0** | OE test studio OE1-OE22 + `build_/check_offset_edges_tests.py`; fingerprint baseline of Create_SW_Shelf (doc 9732a1a9). Round-1 P0 item 1. Needs example_1 versioned (f352eb88 + later). | M, ~2 d |
| **P1** | Shared profile module in Curve_tools (from COP §segments + OE quadratic / per-component pins / offset-type gating / hold to ends). Re-point COP onto it; COP tests must stay green. | M, ~2 d |
| **P2** | OE evaluates through P1 (analytic derivatives); batched tangents + `transportedNormals` with the `ptSeedNormal` seed. Delete the dead station loop and the FD code. Zones, ids and emitter unchanged. Parity against P0 baselines. | M, ~2 d |
| **P3** | Move biarc / arc emitters into `curve_core`. Optional DEO option for varying offsets over arcs (default off, per correction 25). | S-M, ~1 d |
| **P4 (optional, the convergence step)** | DEO "Profile source: Wire / Regions" using P1 in memory (`profileFromCurves`), with COP's region UI predicates. Then new documents use DEO only, and OE gets "(legacy)" in its name while staying maintained through P1/P2 libraries. | M-L, 3-4 d |
| **P5** | `pathProcessing.fs` (3 features on the old core): re-pin or retire (round 1, P2 item 13). | M |

Order: P0 → P1 → P2. P3 and P4 are independent after P1.

---

### Decisions for the user
1. **Topology:** must updating an OE import leave edge counts, curve types and ids unchanged? If yes, rule out Option A.
2. **New work:** should OE be frozen for new designs (P4 + "(legacy)"), or stay first-class?
3. **Arc default for new work:** DEO's automatic lines/arcs, or OE-style "splines unless asked"?
4. **What "At point" means in COP and in a future DEO Regions source:** world X, or arc length projected onto the source (OE's meaning)?
5. **Display fix for OE's swapped Normal/Binormal labels:** wording only; parameter ids stay.
6. **Versions:** example_1 and Curve_tools when P1/P2 land. The user creates versions.

### Critical Files for Implementation
- C:/Users/jed.yeiser/documents/featurescripts/example_1/refSurfCreation/offsetEdges.fs
- C:/Users/jed.yeiser/documents/featurescripts/driven_offset/create_offset_profile.fs
- C:/Users/jed.yeiser/documents/featurescripts/curve_tools/curve_core.fs
- C:/Users/jed.yeiser/documents/featurescripts/driven_offset/edge_offset_utils.fs
- C:/Users/jed.yeiser/documents/featurescripts/driven_offset/driven_edge_offset.fs
