# Trim curve +: distance direction when the reference point is an endpoint, and why a new body appears (brief)

Written 2026-11-12 by the eocProductData session while looking at the user's Part Studio `STR_Tooling_Blank` (document
Tooling_Blanks, `0785635787213e6d725936ab`). Read-only context: it changes no `.fs` file. Labels: **verified** (read in source or from
the document's feature list), **inferred**, **unverified**.

## What the studio uses (verified, from the REST feature list)

Feature `Trim_Ende`, featureType `betterCurveTrim` (Trim curve +): `operation` TRIM, `curves` = query variable `tail_wire`,
`cutBy` ARC_LENGTH_FROM_POINT, `fromPoint` = the vertex of mate connector `EDA`, `distance` = `#endelange + 20mm`, `distanceFlip` false,
`trimKeep` POINT_SIDE. The studio is configured (Spitzeblock / Endeblock configurations) and the wires come from derives and transforms
(`Copy_Tail`, `rotate_tail_block`, ...). The REST feature state of `Trim_Ende` is **ERROR** (the message was not readable from here).
The feature library instance is pinned to `d14cdaad...::vf9223dfa...::e276be4e...::ma24761c...` (not checked against the repo copy).

## 1. Why a trim can produce a NEW body (verified from source)

`betterCurveTrim.fs:356-366`: for TRIM with one cut it first tries `trimWireInPlace` (`curveTrimCore.fs:439`, `opMoveCurveBoundary`),
which keeps the wire body ("references to the wire survive"). It returns `done: false` and the feature falls back to
`splitWireIntoPieces` (`curveTrimCore.fs:390`: `opSplitEdges` + `opExtractWires` per span + `opDeleteBodies` of the source), which makes
NEW bodies and deletes the original, when:
- the cut plane (normal to the path at the cut) crosses the wire more than once (`curveTrimCore.fs:464`, 1 crossing required), or
- the side is ambiguous: both ends are nearly equally far from the cut plane (`CTC_TRIM_SIDE_MARGIN * total`, `:473`), or
- there is more than one cut (inflection mode, SPLIT).
So a new body is the documented fallback, not a malfunction, but it silently changes body identity. Which route ran in this studio
is **unverified** (set `debugCuts` on `Trim_Ende`: `applyCut: n cut(s) -> m piece(s)` prints only on the extract route, `:367`).
Suggest: report which route ran (a feature info message), and consider whether the in-place route can be made to succeed for
the common case (endpoint reference, below), since body identity matters downstream.

## 1a. VERIFIED ROOT CAUSE (2026-11-12, read from the live document; nothing in the document was changed)

Reading each Endeblock option's feature states (REST, 9 calls, other parameters at their defaults): `Trim_Ende` is **ERROR for Endeblock =
"Aufbug 22" (the default) and "FR2"** and OK for the other seven (Aufbug 15/19, 12, 11, K2 Std, FR1, LK2, Aufbug500). With Endeblock = Aufbug 22
selected in the UI, the failing feature is `Trim_Ende` and its message is:

> Trim_Ende [Trim curve +] did not regenerate properly: The distance (165 mm) runs past the end of the curve
> (reference 194.974 mm from its start, curve 194.974 mm long)

That is the error at `betterCurveTrim.fs:548-552` (`f <= 0 || f >= 1`). The reference point (mate connector `EDA`, picked as its vertex) is
EXACTLY at the END of the wire's path (194.974 mm of 194.974 mm, f0 = 1), and the feature measures `distance` in the path direction
(`dir = distanceFlip ? -1 : 1`, `:532`), so the cut lands 165 mm beyond the end. In the other configurations the wire's path runs the other
way, the reference is at path START, and the same settings work. This is the user's "curve direction switching between curves": nothing
about the geometry changed, only the direction `wirePath` assigned. The user states the wire has several edges, no S-curves, no cutting back,
and the point is exactly on the wire's endpoint. **So the endpoint rule in section 2 is the fix**: with f0 at 1, measure toward 0
(cut at 194.974 - 165 = 29.974 mm from the start) and keep the point's side (`POINT_SIDE`: the 165 mm piece at the point's end).
The downstream failure the user also sees (`#tail_proj_dist`: "Select the second entity to measure between. 1 missing selection") is a
consequence: with `Trim_Ende` in ERROR the trimmed wire does not exist for the references after it. Whether the in-place route
(`opMoveCurveBoundary`) or the extract fallback runs once the cut is valid is still **unverified**: check it after the fix
(`debugCuts`, or whether `tail_wire` still resolves after `Trim_Ende`).

## 1b. Evidence from the failing configuration (earlier look, same document)

In the failing configuration the feature tree reports `1 feature(s) failed to regenerate`, and the failing feature is NOT `Trim_Ende`
but the variable after it: `#tail_proj_dist = ?` [assignVariable, MEASURED DISTANCE, MAXIMUM] with the message **"Select the second entity
to measure between. 1 missing selection."** Its two entities are the vertex of mate connector `EDA` and the query variable `tail_wire`.
`tail_wire` (feature order 13, `queryVariable`) is `selectionType CREATED_BY`, entityType BODY, created by `Copy_Tail`, `evaluateOnUse` false,
i.e. it is resolved to the wire body at index 13, BEFORE `Trim_Ende` (index 20) trims that same wire. Feature order around it:
`Copy_Tail(8) ... #tail_wire(13) rsl_wire(14) EDA(15) ... rotate_tail_block(19) Trim_Ende(20) #tail_proj_dist(21) Bot_Surf(22)`.
**Inferred chain:** in the failing configuration `Trim_Ende` takes the extract-and-delete route (`splitWireIntoPieces`: new wire bodies,
the source deleted), so the body `tail_wire` points at no longer exists and every later reference to it (here the measurement) reports a
missing selection. In the passing configuration the in-place route (`opMoveCurveBoundary`) keeps the body, so the reference survives.
Which of the two conditions made the in-place route return `done: false` (crossings != 1 or ambiguous side) is **unverified**; turn on
`debugCuts` in the failing configuration to see (`applyCut: n cut(s) -> m piece(s)` appears only on the extract route).
**Consequence for the feature:** the same feature changes body identity depending on the geometry and the configuration, which breaks
users' downstream references only sometimes. Prefer ONE consistent behaviour, and when the extract fallback is unavoidable say so
(a feature warning naming "the wire was replaced; earlier references to it are lost") and consider returning the result so that a
`CREATED_BY` variable defined AFTER the trim finds it in both routes. Open question for you: is there any operation that removes part
of a wire body (some edges) while keeping the body's identity, other than `opMoveCurveBoundary`?

## 1c. The user's statement of the problem, and two layers of fix

"The trim doesn't fail, but all features that depend on that body do." The trim is correct; what breaks is every later reference to the
wire it replaced. Two layers, in this order:
1. **Prevent the replacement** (section 2 below, the endpoint rule): make the in-place route succeed as often as possible, since it
   keeps the body and with it every reference.
2. **When the extract fallback is unavoidable, keep the IDENTITY.** Same mechanism as `bodyIdentity.fs` (`opNameEntity` / `qNamed`:
   "This name becomes its identity. qNamed(name) will resolve to this entity or its modifications", `std/nameEntity.fs`): name the kept
   piece with the identity the source wire carried, so references that resolve through `qNamed` follow. Constraints to investigate:
   (a) references created BEFORE the trim (the `tail_wire` query variable at order 13) can only follow if the source already carried a
   name at that time, so the wire needs its identity assigned early (right after `Copy_Tail`), e.g. with `bodyIdentity` / Assign Identity;
   (b) whether FeatureScript can READ an entity's existing `opNameEntity` name (to copy it), or the feature needs a `Keep identity`
   string parameter naming it (parameters are frozen once published: use `extraOptions`/a hidden field);
   (c) a feature-level WARNING when the fallback ran ("the wire was replaced; earlier references to it are lost unless it carries an
   identity"), so the cause is visible instead of a downstream "missing selection".

## 1d. Other reasons the in-place route is refused (verified in source), and ways around them

The endpoint rule fixes DIRECTION and the "ambiguous side" gate (the kept side follows from which end the point is). It does NOT touch
the other gate: `trimWireInPlace` cuts with `opPlane(... width/height = 2 * wp.total)` (`curveTrimCore.fs:495-496`), a plane about twice the
wire's length, normal to the path at the cut; it then requires that plane to cross the whole wire (400 samples, `:445-466`) exactly ONCE.
A wire that turns back through the plane anywhere (a hook or S-curve turning more than ~90 degrees past the cut, which is exactly the
kept tail piece here) crosses twice and the feature extracts new bodies instead. That is independent of the reference point, the end,
and `distanceFlip`. (The side-ambiguity gate compares the two ends' distances ALONG THE PLANE NORMAL, not arc length, within 1% of the
wire length: `:471-476`.) Two ways to avoid depending on the crossing count:
- Use a SMALL cutting plane (a few mm, sized from the local scale) so it can only meet the wire near the cut. Unverified: whether
  `opMoveCurveBoundary` trims to the finite face or to its underlying infinite surface.
- Trim to a VERTEX: `opMoveCurveBoundary`'s `trimTo` filter accepts `EntityType.VERTEX`, mate connectors, edges, faces and non-sketch
  bodies (`std/moveCurveBoundary.fs:42,51`). Split the edge in place at the cut with `opSplitEdges` (it adds a vertex and keeps ONE wire
  body: `curveTrimCore.fs:336-357`), then trim to that vertex: no plane, no crossing test. Unverified: trimming to a vertex that lies
  on the wire itself.
Not measured: the geometry of the failing wire (the REST edge/body endpoints return nothing for wire bodies); `debugCuts` in the failing
configuration will say which gate refused.

## 2. The direction problem and the endpoint rule (the user's point)

In ARC_LENGTH_FROM_POINT mode the distance is measured along the PATH direction: `dir = distanceFlip ? -1 : 1`
(`betterCurveTrim.fs:532`), `f = fractionAtSignedDistance(wp, f0, distance, dir)` (`:547`), and it throws "runs past the end of the
curve" when `f <= 0 || f >= 1` (`:548-552`). `wp = wirePath(context, wire)` orders and directs the wire's edges; that direction is NOT
tied to the reference point. So the SAME `distanceFlip` cuts toward the far end on one curve and past the end (an error) on another,
or on the same curve in a different configuration, when the wire's path direction flips. The `keepEnd` logic then follows the same
direction (`:305-313`). **inferred**: this is the user's "curve direction switching between curves", and the likely cause of the
ERROR state above (reference point at one end, direction pointing off the curve).

**Proposed rule:** after `f0 = fractionOfPoint(...)`, if the reference point is an ENDPOINT of the wire (f0 within a tolerance of 0 or 1,
or the point coincides with a path end vertex), the direction is trivial: measure INTO the wire, from that end, whatever the path
direction is: `dir = (f0 near 0) ? +1 : -1`. `distanceFlip` should then have no effect (or be hidden/greyed with a note when the point is
an endpoint), `trimKeep` POINT_SIDE keeps the span between the endpoint and the cut, FAR_SIDE keeps the rest, and the in-place trim's
side (`keepEnd`) follows from which end the point is, not from `distanceFlip`. For an interior point keep today's behaviour
(`distanceFlip` chooses the direction along the path) but say which way the path runs (the feature already marks the positive
direction: `markPositiveDirection`).
Also check the case where the point is on a closed or self-touching wire (an "endpoint" is ambiguous): fall back to today's rule.

## 3. Tests to run (a document, not code)

For each of two wires drawn in OPPOSITE directions (and the same wire with `Reverse`): reference point at the start vertex and at the end
vertex; `distance` a few mm and most of the length; `trimKeep` both settings; with and without `distanceFlip`. Record: which end is cut,
the kept length, whether the body kept its identity (the in-place route) or was replaced, and any error. After the change the result
must be the same for both directions of the same geometry.

## Out of scope

Everything else in the studio; the deform backend. Do not edit the user's Part Studio.
