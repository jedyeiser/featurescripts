# create_offset_profile -- design (2026-09-24)

A feature in the driven_offset document (BeamBuilder Testbed) that BUILDS an offset profile from a table
of regions or points, for Driven edge offset (DEO) and Driven offset surface (DOS). Borrows the framework of
example_1/refSurfCreation/createCavityDepthProfile.fs (regions, auto-built intersections, Hermite blends) --
re-implemented, not imported.

## Output contract

- Wire bodies in PROFILE COORDINATES: **X = station, Y = width offset, Z = height offset** -- exactly DEO's
  existing "Offset profile" input.
- A **station is a world-X value in profile coordinates.** Where it lands on the part depends on how the
  consumer applies the profile (DEO's world-X or along-reference mapping); the profile does not know.
- The profile is DATA ONLY. Where the offset jumps or is undefined the profile BREAKS: one wire body per
  continuous PIECE. No zero-length connector edges (user decision). Whether / how to join a break is decided
  by the consumer (DEO / DOS), see "Consumers".
- The profile STOPS at the first and last item; nothing is extrapolated.
- Exact geometry: every piece is built from exact polynomial Beziers (no sampling, no approximation
  tolerance). Continuity inside a piece comes from its knot structure and equals what the inputs give.

## Mode: Regions

Each region: start station, end station, start width, end width, start height, end height, shape,
optional start buffer and end buffer (default 0 mm).

- **Shape** (one per region, applied to width and height): LINEAR or SMOOTH.
- **Buffers**: a distance from the region's start (end) toward its centre over which the offset is held
  at the start (end) value. The transition runs between the buffers:

  ```
  u = clamp((x - (xs + b0)) / ((xe - b1) - (xs + b0)), 0, 1)
  v(x) = v0 + (v1 - v0) * S(u)        S = u (LINEAR) or 6u^5 - 15u^4 + 10u^3 (SMOOTH)
  ```

  Validation: b0 + b1 < xe - xs (else regenError on that region's buffer fields).
- Regions are sorted by start station; overlapping regions are an error (named pair).
- **Intersections**: rebuilt by editing logic for each consecutive pair, keyed by the region-name pair so
  user settings survive re-ordering (the example_1 pattern). Each intersection:
  - **Continuous** (default when the regions touch and their end/start values match within tolerance):
    the regions are joined as they are; continuity is whatever their shapes give (see Exactness).
  - **Blend**: a Hermite bridge from `xA_end - dA` to `xB_start + dB`, continuity G0 / G1 / G2 at each side
    (blendHeightAt dispatch from example_1), applied to width and height. Derivatives of the regions at the
    blend ends are ANALYTIC (the region functions are polynomials) -- not finite differences.
    Validation: the blend may not reach past the other end of either region (or into its buffer? -- allowed;
    the buffer is constant, the blend just starts inside it).
  - **Break**: the profile ends at region A and a new piece starts at region B. Automatic when the regions
    touch but values differ and no blend is chosen, or when there is a gap in X between them.

## Mode: Points

Points: station, width, height. Sorted by station.

- **Between consecutive points** (per segment, with an "all segments" default): LINEAR, SMOOTH (smootherstep:
  zero slope and zero curvature at both points, so a chain of SMOOTH segments is G2 and never overshoots the
  point values), or HOLD (keep the first point's value up to the next point, then break).
- **A jump** = two points at the same station with different values -> break there.

## Exactness (both modes)

Every sub-segment is `v(x)` = polynomial of degree <= 5 in x over [xa, xb]. With x linear in the curve
parameter t (x = xa + (xb - xa) t), a degree-p polynomial in t is an exact degree-p Bezier: power
coefficients -> Bernstein coefficients; control points P_i = (xa + (xb - xa) i/p, w_i, h_i). Width and height
channels are elevated to a common degree (5 when any SMOOTH / quintic blend is present).

A PIECE = consecutive sub-segments with no break, joined into one B-spline; interior knot multiplicity from the
actual continuity at each joint:

| joint | continuity | multiplicity (p = 5) |
|---|---|---|
| buffer (constant) -> SMOOTH ramp, SMOOTH -> SMOOTH at a point | C2 (smootherstep has zero 1st and 2nd derivative at its ends) | p - 2 |
| blend with G1 / G2 ends | as chosen | p - 1 / p - 2 |
| LINEAR corners, G0 blends, continuous regions with different slopes | G0 | p |

So "G0 in -> G0 out, G2 blend -> G2 out" holds exactly; no knot removal guesswork.

## Published outputs (Extract variables)

variables: pieceCount, breakCount, breakStations (array of stations), start / end station.
queries: output (all piece wires), piece1..N, startVertex / endVertex (standard ends), breakVertices (the
piece ends at each break). Embedded with embedStandardOutputs (Variable_tools V2 extract_outputs).

## Consumers (DEO / DOS) -- follow-on work

- DEO already builds profile chains from several links sorted by X and tolerates gaps (buildChain). New:
  links that TOUCH at one station with different values, and a per-break choice.
- Editing logic lists the profile's breaks (by station); each: **None** (the offset output breaks too) or
  **Line** (join the two offset points at that station with a straight edge; DOS: a planar patch).
  Blending belongs in the profile (Regions intersections), not in the consumer.

## Test plan

Harness like reference_side_tests (own fixtures, PASS/FAIL console lines):
- FCP/ACP case: tip [x0, FCP] w 0, RSL [FCP, ACP] w 3, tail [ACP, x1] w 0 -> 3 pieces, 2 breaks at FCP / ACP,
  values exact at sampled stations.
- Buffers: SMOOTH region with b0, b1 -> constant over the buffers, C2 at the buffer ends (knot multiplicity 3).
- Blend G2/G2 between two regions -> one piece, curvature continuous across both blend ends.
- Points SMOOTH chain -> one piece, no overshoot (all samples within the min/max of each segment's ends).
- Points jump (duplicate station) -> break.
- Validation errors: overlapping regions, buffers too long, blend reaching past a region.

## 2026-09-25 additions (built)

- **Stations from points**: every station (region start / end, point station) has a Value / Point switch
  (`OffsetStationSource`). Point = a vertex or mate connector pick (filter `EntityType.VERTEX ||
  BodyType.MATE_CONNECTOR`, so a mate connector can be created in the pick field) + a signed distance along +X;
  station = the point's WORLD X + distance (user decision: profiles are defined along X; mirroring DEO's
  measure-along settings rejected). Resolved in the body (missing pick -> error on that field) and in the
  editing logic (writes the resolved value into the hidden value field, so switching back keeps it and sorting /
  intersections use it). Exact on the part only when the consumer measures along World X from x = 0.
- **CONSTANT region shape**: one Width / Height (`constantWidth` / `constantHeight`), no end values, no buffers.
  Default shape stays LINEAR (saved instances unchanged).
- Tests T6 (constant + point + mate connector stations) and T7 (points mode, mate connector station) built by
  `devtools/onshape/build_offset_profile_tests.py`; `check_offset_profile.py` 9/9.

## Open

- One shape per region for width and height (v1). Separate per-channel shapes if needed later.
