# merge_curve -- research

Paths are repo-relative. `std/` is the local 2878 mirror. `DEO` = driven_offset/driven_edge_offset.fs,
`utils` = driven_offset/edge_offset_utils.fs. Companion docs: research_map_curve.md,
research_shared_vocabulary.md, research_evaluate_offset.md (other writer).

## 1 Purpose and stub requirements

Stub: driven_offset/merge_curve.fs:4-22. Inputs `seedEdge` (edited) and `mergeEdge` (G0 to seed,
absorbed). Build the std Edit-curve pipeline so the seed's curve becomes seed+merge as one curve.
Four wire-membership scenarios (:12-20): seed on wire, merge not on wire -> stays on seed wire;
seed on wire, merge on other wire -> seed wire; both on same wire -> seed wire; seed not on wire ->
new wire regardless of merge. SINGLE mode of map_curve calls this (research_map_curve.md s2).

## 2 Std surface

| Symbol | Local? | Signature / note |
|---|---|---|
| opEditCurve | yes, `@internal` | std/geomOperations.fs:509 `{ wire : Query, edge : Query }`; std callers add `"showCurves" : true` (std/approximationUtils.fs:202-206) |
| editCurve feature | yes | std/editCurve.fs:58; filter `EDGE \|\| (BODY && WIRE && SketchObject.NO)` :61 |
| approximateResults | yes, exported | std/approximationUtils.fs:174 -- the whole native path per wire |
| makeApproximationTarget | yes, exported | std/approximationUtils.fs:120 `(context, path, keepStart, keepEnd)`; 200 samples via evPathTangentLines :128 |
| approximateSpline | yes | std/splineUtils.fs:73 `{ degree, tolerance, isPeriodic, targets, maxControlPoints }` returns array |
| constructPath | yes | std/path.fs:165 `{ tolerance? }`; throws if not chainable; `.path.closed`, `.path.flipped` |
| opCreateBSplineCurve | yes | std/geomOperations.fs:192 `{ bSplineCurve }`; "must be G1-continuous" :187 |
| opExtractWires | yes | :564 `{ edges }`; fails on crossing / >2 at a point :558; output is an independent copy |
| opDeleteBodies | yes | :406 `{ entities }` |
| opSplineThroughEdges | yes | :1370 `{ edges }`; needs a tangent-continuous chain -- not a G0 merge tool |
| opSplitEdges / opMoveCurveBoundary / opSplitPart | yes | :1392 / :74 / :1524 -- TO_EDGES trimming, not merging |
| qOwnerBody, qBodyType, qOwnedByBody, qSketchFilter | yes | std/query.fs |
| MAX_CONTROL_POINTS, MAX_DEGREE, TOLERANCE_BOUND, DEGREE_BOUND | yes | std/approximationUtils.fs:22-47 |

Do NOT exist (mirror + FsDoc, per raw report 04): `opJoinCurves`, `opMergeCurves`, `opCreateWires`,
`opSplitCurve`, `opTrimCurve`, `joinCurves` (std), `curveEdit`. There is no kernel "join two curves"
primitive; every native route is fit-then-replace.

## 3 How Onshape's own Edit curve / Composite curve do it

Edit curve (std/editCurve.fs): `getQueryToReplace` :284 decides in-place vs copy via
`inputCanBeModified` :258-282 -- in place only if ONE owner body, ALL its edges selected, body is
WIRE, not a sketch body, nothing in-context; otherwise `opExtractWires` on all edges :291-295 and
edit the copy. `getBSplineFromInput` :310: with `approximate` it constructPaths the edges (tol
1e-5 m :318), builds a target :326, approximateSpline :328-334; WITHOUT approximate a multi-edge
selection throws `EDIT_CURVE_MULTIPLE_EDGES` :341-344. So for two edges the ONLY native path is the
approximation. Then opCreateBSplineCurve -> opEditCurve(wire=queryToReplace, edge=new) ->
opDeleteBodies(new) (std/editCurve.fs:231-248; std/approximationUtils.fs:190-208 is the same shape).

Composite curve (std/compositeCurve.fs:12-36): `dissolveWires` -> `opExtractWires` (one wire per
connected group) -> optional `approximateResults` :33-35, which per wire runs constructPath ->
makeApproximationTarget -> approximateSpline -> opCreateBSplineCurve -> opEditCurve -> opDeleteBodies
(std/approximationUtils.fs:174-210). "Composite curve + Approximate" IS a merge of G0 edges into one
spline; merge_curve reproduces it with control over which body survives.

## 4 The native path (D1)

1. Resolve `edges = qUnion([seedEdge, mergeEdge])`; classify wire membership (s5).
2. `path = constructPath(context, edges, { "tolerance" : 1e-5 * meter }).path` -- doubles as the G0
   test (throws if not chainable) and yields orientation.
3. `target = makeApproximationTarget(context, path, keepStart, keepEnd)`.
4. `bspline = approximateSpline(...)[0]` with the shared approximation predicate fields.
5. `opCreateBSplineCurve(id + "new", { "bSplineCurve" : bspline })`.
6. Pick / make the target wire (s5). `opEditCurve(id + "edit", { "wire" : target, "edge" :
   qCreatedBy(id + "new", EntityType.EDGE), "showCurves" : true })`.
7. `opDeleteBodies` the temp curve (and, scenario 2, the emptied merge wire).
8. `nameOutput` (DEO:919, to be exported) on the surviving wire.

```
const edges = qUnion([definition.seedEdge, definition.mergeEdge]);
var path;
try silent
{
    path = constructPath(context, edges, { "tolerance" : 1e-5 * meter }).path;
}
catch (error)
{
    throw regenError("Seed and merge edges must meet end to end (G0).", ["mergeEdge"], edges);
}
const target = makeApproximationTarget(context, path, definition.keepStartDerivative, definition.keepEndDerivative);
const bspline = approximateSpline(context, {
            "degree" : definition.approximationDegree,
            "tolerance" : definition.approximationTolerance,
            "isPeriodic" : path.closed,
            "targets" : [target],
            "maxControlPoints" : definition.approximationMaxCPs
        })[0];
opCreateBSplineCurve(context, id + "new", { "bSplineCurve" : bspline });
const targetWire = resolveTargetWire(context, id, definition);   // s5
opEditCurve(context, id + "edit", {
            "wire" : targetWire,
            "edge" : qCreatedBy(id + "new", EntityType.EDGE),
            "showCurves" : true });
opDeleteBodies(context, id + "cleanup", { "entities" : qCreatedBy(id + "new", EntityType.BODY) });
```

The `try silent` is the one std uses for the same call (std/editCurve.fs:314-323); it converts a
kernel error into a user-facing message, not defensive scaffolding.

opEditCurve is `@internal` (std/geomOperations.fs:502). The project already depends on internal
`opCreateOutline` (driven_offset/evaluate_profiles.fs:49-53 records the caveat); record the same
caveat in merge_curve.fs. Fallback if it ever breaks: opExtractWires(new curve), delete the seed
wire, copy the name -- identity lost but geometry preserved.

## 5 The four wire-membership scenarios

Membership tests (std/editCurve.fs:277-278): on wire = `!isQueryEmpty(qBodyType(qOwnerBody(e),
BodyType.WIRE))`; sketch excluded by `qSketchFilter(..., SketchObject.YES)`. Same body =
`size(evaluateQuery(qOwnerBody(edges))) == 1`. Seed wire's edge set = `qOwnedByBody(W, EDGE)`.

| seed | merge | action | body identity |
|---|---|---|---|
| on wire W, W = {seed} | not on a wire (solid/sheet/sketch edge) | opEditCurve(wire=W); merge untouched | W keeps id and name |
| on wire W, W = {seed} | on separate wire V | opEditCurve(wire=W); if V = {merge} opDeleteBodies(V), else leave V | W keeps id; V gone or shrunk (UNVERIFIED: opEditCurve leaves V intact -- expect yes, it reads geometry only) |
| on wire W | on same W | if W = {seed, merge} opEditCurve(wire=W). Else opExtractWires(W's other edges) + opExtractWires(seed U merge), opDeleteBodies(W), edit the second copy, copy name | first case keeps id; second gets a NEW body id -- "stays part of seed wire" is nominal only |
| not on a wire | anything | opExtractWires(seed U merge) -> opEditCurve(wire = that) (= std/editCurve.fs:291-295) | new wire body; merge's owner untouched |

Rows 1-2 with W having extra edges beyond seed reduce to row 3's second case. opEditCurve replaces
the WHOLE wire body's curve (std only calls it on single-edge or freshly extracted wires,
std/approximationUtils.fs:202); it cannot splice one edge inside a multi-edge wire. Row-3 second
case is therefore the only honest treatment; report it with reportFeatureInfo.

## 6 G0 adjacency test options

| Option | Cost | Verdict |
|---|---|---|
| constructPath(seed U merge) throws | 1 kernel call, needed anyway for the target | use; error message = the G0 requirement |
| qVertexAdjacent(seed, EDGE) contains merge | 1 query | only for shared topology (same body); misses coincident-but-separate bodies |
| evEdgeTangentLines(e, [0,1]) origins within OFFSET_GEOM_TOL (utils:44; pattern utils:847) | 2 calls | already paid if buildChain describes the edges; gives WHICH ends meet, which the path also gives via `.flipped` |

## 7 Custom exact join: deferred

tools/curve_operations.fs:369 `joinCurves(context, A, B, ContinuityType, options)` (enum :59 C0/C1/C2)
would keep line+spline seams exact. Deferred because:

1. Seam knot. It keeps A's full clamped end (p+1 copies of the seam knot) and skips only B's first
   p+1 knots (:490-491), so the seam has multiplicity p+1 and knot count m = n + p + 2 -- one knot too
   many for the CPs concatenated at :473-482 (derived from the code, not run). A p+1 seam is a
   parametric break; opCreateBSplineCurve demands G1 (std/geomOperations.fs:187), so a true G0 corner
   is rejected outright and a smooth seam still needs `removeKnot` (tools/bspline_knots.fs:666) down
   to multiplicity p (C0) or p-1 (C1). C2 is only "approximate" (:467-471).
2. Weights. `isRational` and `weights` come from compatA only (:498, :515-516); `makeCurvesCompatible`
   (tools/bspline_knots.fs:1086) elevates degree and merges knots but does not homogenise
   rational vs non-rational. A rational arc joined to a non-rational line drops the arc's weights.

Also cross-document: utils:7 forbids importing tools/ into this document. Revisit only if seam
exactness is a hard requirement AND both inputs are non-rational.

## 8 Arc / line type loss and the warning

The native path is a fit (MAX_CONTROL_POINTS 100, MAX_DEGREE 15, std/approximationUtils.fs:22-26).
A line+arc merge becomes one NURBS; the radius no longer reads in Onshape. DEO keeps arcs real only
by sketching them (utils:2518-2533 emitArcCurve) and lines as degree-1 splines (utils:2500). So:
when `evCurveDefinition(edge, simplify true)` of either input is a `Circle` (curveType CIRCLE in
utils:848-856 describeEdges), `reportFeatureWarning("Merging a circular arc produces a spline; the
radius is not preserved.")`. Do not refuse; SINGLE mode of map_curve legitimately merges arcs.
Two collinear lines: same warning class (a degree-1 two-span result is fine but detect and report).

## 9 Risks and Onshape tests

| Risk | Test in Onshape |
|---|---|
| opEditCurve with a multi-edge wire target and a multi-edge source curve | scenario 3 first case; expect success (composite-curve Approximate does exactly this) |
| opEditCurve target wire on a sketch body | must be rejected before the call (std filter :61); test the error path |
| Internal op removed/changed in a future std | pin the std version in the tab header; keep fallback of s4 |
| opDeleteBodies(V) when V has other edges | do not delete; verify V survives with merge edge intact |
| approximateSpline exceeds maxControlPoints | reportFeatureWarning with deviation (evMaxPathDeviation, std/approximationUtils.fs:193-197 pattern) |
| Name/property survival across opEditCurve | check PropertyType.NAME still set on W after edit |
| Closed result (`path.closed`) | isPeriodic true path; test a two-arc circle |

## 10 Open decisions

1. Expose keepStart/keepEndDerivative? Recommend yes, both default true (std default is a fit
   without them; ends drifting is the usual complaint).
2. Scenario 3 second case: refuse instead of re-extracting? Recommend re-extract + info message; a
   refusal forces the user to split the wire by hand.
3. Deviation display (approximationShowDeviation): defer; debug-group boolean only.
4. Exact join: keep deferred (s7). Reopen only with a ski use case where a spline seam fails QC.
5. Parameter names: `seedEdge`, `mergeEdge`, `outputName`, approximation predicate from the
   shared vocabulary (research_shared_vocabulary.md s5).
