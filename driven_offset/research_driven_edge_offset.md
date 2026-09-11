# driven_edge_offset: publishing its outputs (research, 2026-09-10, rev 2)

> **Decision revised 2026-09-11.** Queries are no longer stored in map variables and the
> Design map feature has been removed. Selections are published as native query variables by
> the **Extract variables** feature (our FS 3070 implementation of Evan Reese's approach, in
> the `design_map` tab); scalars that belong together go in a std Variable holding a map
> literal; an optional manifest records what was published for the API viewer (see
> `extract_variables_schema.md`). Reason: a map-held Query is usable only by our own custom
> features and is a workaround of Onshape's native selection mechanism. Sections below that
> describe Design map, `#core.bottom` query entries, or `designMap/1` are superseded.


Refs: DEO = driven_offset/driven_edge_offset.fs, U = driven_offset/edge_offset_utils.fs,
T = driven_offset/offset_run_treatment.fs, LIB = driven_offset/Reese_IMPORT_THIS.fs,
EV = driven_offset/Reese_Extract_Variables.fs. Std line numbers are the local 2878 mirror.
Consumer side is documented in driven_offset/research_design_map.md (not repeated here).

## 1. Purpose

driven_edge_offset publishes, once per regen, one self-describing map of what it made: the output
wires and per-run edges by symbolic Query, the geometry decided per run (line / arc radius / fitted),
and the inputs it was driven by. Scope: consumers in the SAME Part Studio only. Derive survival and
cross-studio use are out of scope (see "Later, if needed" at the end).

## 2. Reese's pattern, plainly

Producer: at the end of its body a feature builds one map `{ "variable" : {...}, "query" : {...} }`
and calls `embedVariableMap(context, id, m)`, which is one line: `setVariable(context, toString(id),
m as EmbeddedVariables)` (LIB:319). The variable name is the feature's own id rendered by the array
overload of toString (std/string.fs:26-38), e.g. `[ FxxxYYY ]`: unique per instance, not an
identifier, so unreachable from `#` and not a user-facing setup variable. Queries in the map stay
symbolic; the predicate walks the map without evaluating anything (LIB:135, LIB:151-204). Entry
values may be plain or wrapped: `extractableVariable(v, desc)` (LIB:214-237), `extractableQuery(q,
desc, color)` (LIB:246-295); `undefined` entries are rejected (LIB:189), so conditional keys are
added or omitted, never set to undefined.

Consumer: a FeatureList parameter names the producers; for each id it reads
`getVariable(context, toString(featureId))` (EV:294), checks the shape, merges (variable keys
first-wins with a warning, query keys qUnion; EV:283-319) and republishes selected keys.

Verified std facts: (1) type tags are bound to a declaration in one module of the import graph, so
`is EmbeddedVariables` across different library elements is unverifiable
(https://cad.onshape.com/FsDoc/relational.html; raw 01 follow-up 1); (2) common.fs does not export
queryVariable.fs (std/geometry.fs:76 does), so setQueryVariable needs an explicit import
(std/queryVariable.fs:554-557); (3) @setVariable accepts any name string; only the Variable feature
validates (std/variable.fs:798-805; https://forum.onshape.com/discussion/17100/...).

Kept: one map per feature under toString(id); symbolic Queries; descriptors (they feed "print keys").
Fixed in our port (R3): broken element-id import (EV:8); missing icon import (EV:38); 3044 -> 3070;
explicit `import(path : "onshape/std/queryVariable.fs", version : "3070.0")` wherever
setQueryVariable is called. The port lives in driven_offset/design_map_query_utils.fs; Reese_* and
query_varialble_ref.fs are deleted afterwards.
Dropped: the typed-map gate (consumer checks `canBeEmbeddedVariables` only; the cast at LIB:319 is
kept for typecheck diagnostics and never relied on); EV's `qUnion(evaluateQuery(...))` freeze
(EV:422-423) in favour of `qUnion(makeRobustQueriesBatched(...))` (std/feature.fs:643-651, the std
Query Variable pattern at std/queryVariable.fs:405); EV as a separate feature (Design map absorbs it).

## 3. MBD in Onshape

What exists (GA Feb 2026, 1.211): annotation toolbar, Inspection panel, feature/hole/axis/thickness
dims, datums, GTOL frames, weld symbols; auto-carried into drawing views; STEP AP242 PMI export.
- https://cad.onshape.com/help/Content/PartStudio/model_based_definition.htm --
  "MBD annotations must always be associated with faces. Edges and vertices cannot currently be
  referenced." Derived parts: MBD read-only.
- https://cad.onshape.com/help/Content/PartStudio/inspection_table.htm (CSV export only)
- https://cad.onshape.com/help/Content/Home/tolerance_options.htm -- "Tolerances are available in
  FeatureScript, and can be used in custom features."
- https://www.onshape.com/en/resource-center/what-is-new/onshape-labs-featurescript-co-complete-persistent-center-mass-model-based-definition-mbd
- https://forum.onshape.com/discussion/31466/improvements-to-onshape-august-7-2026

What FS can write: only a tolerant feature dimension, via `setDimensionedEntities(context,
{parameterId, queries | facePairs, dimensionType, tolerances?: ToleranceInfo, nominal?, ...})`
(std/feature.fs:1115, doc 1051-1114). Queries must resolve to faces (std/feature.fs:1060).
Companion: UIHint.CAN_BE_TOLERANT (std/uihint.gen.fs:110), getTolerantParameterIds
(std/feature.fs:1155), ToleranceInfo {toleranceType, upper, lower, precision, toleranceFitInfo}
(std/toleranceTypes.fs:132). What FS cannot write: datums, GTOL frames, notes, surface finish,
weld symbols -- no std function exists in 2878 or the live 3070 FsDoc; GTolConstraintType is an
unused @internal enum (std/gtolconstrainttype.gen.fs:9). No REST endpoint reads PMI/tolerances.

| Carrier | Write from FS | Read from FS | Read via REST | Survives derive | Drawing/UI |
|---|---|---|---|---|---|
| (a) True PMI | tolerant feature dims only, faces | getTolerantParameterIds (own params) | none (UI CSV, AP242) | yes, read-only | yes |
| (b) FS attributes | any value; body/face/edge/vertex (std/attributes.fs:72) | any later feature (std/attributes.fs:105-152) | evalFeatureScript + getAttributes (forum 6912) | yes; split/pattern/union copy them (std/attributes.fs:38-43); derive strips only SM attrs (std/derive.fs:138-145) | no |
| (c) Properties | bodies; NAME on wires works (forum 24715) | not in own context (std/properties.fs:71-76) | /metadata/.../e/{eid}/p | yes | NAME/PART_NUMBER |
| (d) Variables / QVars | any value (std/context.fs:260-279) | getVariable | evalFeatureScript | no | Variables table |

Consequence: DEO's outputs are wire bodies, so true PMI is impossible today. Export data
PMI-shaped (nominal + ToleranceInfo-like) so a downstream feature that thickens or sweeps the wire
into a solid can call setDimensionedEntities on the resulting faces. DEO itself never calls it.

## 4. Export inventory

"Map half" = where the item lands in the embedded map: variable half, query half, or not exported.

| # | Item | Type | Where | Map half | Notes |
|---|---|---|---|---|---|
| 1 | All output bodies | Query | qCreatedBy(id, BODY); wires DEO:896-899, unjoined DEO:907 | query `output` | the identity every consumer starts from |
| 2 | Wire per G0 path | Query | id + "wire" ~ "link" ~ k, DEO:896 | query `link<k>` | join mode only (DEO:891); wires local to emitRuns today |
| 3 | Per-run curve edge | Query | id + "run" ~ r, DEO:776 | query `run<r>` | join mode: qCreatedBy evaluates empty after DEO:897/902; re-base with qClosestTo on wire edges |
| 4 | Corner fill edge | Query + map | id + "fill" ~ r, DEO:797; fill data T:133-137 | query `fill<r>`; variable runs[].fill | payload {kind arc/spline, radius?} |
| 5 | Terminal extension edges | Query | id + side ~ r, DEO:822 | query `startExtension<r>` / `endExtension<r>` | fabricated geometry; keyed so consumers can exclude it |
| 6 | Run classification | string line/arc/freeform | emitted[r].kind, DEO:871 | variable runs[] | |
| 7 | Arc-run radius | ValueWithUnits | DEO:872 (center/normal dropped) | variable runs[] | PMI-shaped {nominal, tolerance}; emitRuns must keep center/normal |
| 8 | Line-run endpoints | Vector x2 | classifyPoints U:380 shape.start/end | variable runs[] | |
| 9 | Run arc-length range | ValueWithUnits x2 | stations[run.start/end].arc, signed from zero point (U:1253) | variable runs[] | export this convention; chain-start arc derivable via zeroArc |
| 10 | Run exact endpoints | Vector x2 | run.startPoint/endPoint T:125,195 | variable runs[] | undefined -> points[run.start/end] |
| 11 | Corner treatment | string gap/extended/trimmed | T:125,133,195 | variable runs[] | |
| 12 | Terminal records | map {action, distance, squareness, kink?} | T:510 | variable runs[] | strings + ValueWithUnits only |
| 13 | Zero point | Vector | DEO:177, evZeroPoint U:219 | variable `zeroPoint`; query `zeroPointEntity` | input entity DEO:110, symbolic |
| 14 | Measure-along, frame alignment | enum -> string | DEO:50, DEO:80 | variable | store toString(enum); FS consumers compare via the enum, not the string |
| 15 | Chain length, zero arc, link count | ValueWithUnits, ValueWithUnits, number | buildChain U:722-779 | variable | |
| 16 | Reference wire + delta + plane normal | Query, ValueWithUnits, Vector | DEO:60, DEO:185; U:1700 | query `referenceWire`; variable `offsetDelta`, `referencePlaneNormal` | REFERENCE_WIRE mode only; keys omitted otherwise |
| 17 | Profile break coordinates, range | array ValueWithUnits; {min, max} | discontinuityCoords DEO:640; profile.minCoord/maxCoord | variable `profileBreaks`, `profileRange` | breaks already computed at DEO:601; reuse, do not recompute |
| 18 | Output name | string | DEO:53 | variable `outputName` | |
| 19 | Inputs (offsetEdges, offsetProfile) | Query | DEO:74, DEO:77 | query, symbolic | they belong to other features; never re-based |
| -- | Stations/frames table | array of maps, up to 200/edge (U:112) | U:985-988 | not exported | bulk; downstream re-evaluates frames from the published wire |
| -- | Profile samples, placed points, margins | arrays | DEO:205-206, DEO:463-519 | not exported | intermediate; reconstructible from output |
| -- | alongRef tables (arcs, thetas, xs, points) | arrays | U:1791-1802 | not exported | internal to the s - h*theta mapping; expose only delta, planeNormal and referenceWire |
| -- | Run end tangents | Vector | U:2727-2751 | not exported | evCurveTangent on the published edge is the truth |

## 5. Concrete design

Embedded map, stored by `embedVariableMap(context, id, {...})` from design_map_query_utils.fs:
- variable half: `outputName`, `zeroPoint`, `measureAlong`, `frameAlignment` (toString of the
  enums), `chainLength`, `zeroArc`, `linkCount`, `profileRange {min, max}`, `profileBreaks`,
  `runs` (array of run payloads), and when alongRef != undefined `offsetDelta`, `referencePlaneNormal`.
- run payload (one per emitted run, index = r): `{kind, link, radius:{nominal, dimensionType:"RADIUS",
  tolerance:{toleranceType:"NONE"}}?, center?, normal?, start, end, arcStart, arcEnd, cornerKind,
  fill:{kind, radius?}?, terminalStart?, terminalEnd?}`. Strings, numbers, ValueWithUnits, Vectors
  and nested maps/arrays only; no Queries (the predicate does not inspect nesting, LIB:187-204, so a
  nested Query would pass and then be useless) and no enums.
- query half: `output` = qCreatedBy(id, BODY), `link<k>` (join mode), `run<r>`, `fill<r>`?,
  `startExtension<r>`?, `endExtension<r>`?, `offsetEdges`, `offsetProfile`, `zeroPointEntity`,
  `referenceWire`?. Per-run keys are flat because the query half cannot hold arrays of Queries
  (LIB:202). Conditional keys are omitted, never undefined (LIB:189).
- No attributes, no setQueryVariable, no mapName parameter in DEO. DEO gains one import (the
  design_map_query_utils tab) and zero kernel calls.

Where: one call after emitRuns (DEO:220) and before debugOutput (DEO:222). This is after
opExtractWires (DEO:897) and opDeleteBodies (DEO:902), which matters for the queries: in join mode
`qCreatedBy(id + ("run" ~ r), EDGE)` evaluates empty after extraction, so each run's edge is
re-based as `qClosestTo(qCreatedBy(wireId, EntityType.EDGE), midPoint)` with midPoint the run's
middle sample (wireId = id + ("wire" ~ "link" ~ linkIndex)); runs trimmed to two or three stations
(T:161-162) still have a distinct middle point. Non-join mode uses qCreatedBy unchanged.

emitRuns must return `{ "runs" : emitted, "links" : { "link<k>" : qCreatedBy(wireId, BODY) } }`
instead of `emitted` alone (DEO:910), each run carrying `edge` (the re-based Query), `midPoint`,
`center`/`normal` for arcs (dropped today at DEO:870-873), and `shape.start/end` for lines.
debugOutput reads `result.runs`. Nothing else in DEO changes.

```
// DEO body, after emitRuns and before debugOutput
function embedOutput(context is Context, id is Id, definition is map, sourceChain is map,
    alongRef, zeroPoint is Vector, result is map)
{
    var variableHalf = {
        "outputName" : definition.outputName,
        "zeroPoint" : extractableVariable(zeroPoint, "Zero point, world coordinates"),
        "chainLength" : extractableVariable(sourceChain.totalLength, "Summed length of the offset edges"),
        "zeroArc" : sourceChain.zeroArc, "linkCount" : size(sourceChain.links),
        "measureAlong" : toString(definition.measureAlong), "runs" : [] };
    var queryHalf = {
        "output" : extractableQuery(qCreatedBy(id, EntityType.BODY), "All output wires", DebugColor.BLUE),
        "offsetEdges" : definition.offsetEdges, "zeroPointEntity" : definition.offsetRefPoint };
    for (var r = 0; r < size(result.runs); r += 1)
    {
        variableHalf.runs = append(variableHalf.runs, runPayload(result.runs[r]));
        queryHalf["run" ~ r] = result.runs[r].edge;
    }
    if (alongRef != undefined)
    {
        variableHalf.offsetDelta = alongRef.delta;
        queryHalf.referenceWire = definition.referenceWire;
    }
    embedVariableMap(context, id, { "variable" : variableHalf, "query" : queryHalf });
}
```

## 6. How it is consumed

Design map (research_design_map.md) picks DEO instances through a FeatureList, reads
`getVariable(context, toString(featureId))`, and republishes chosen keys into one ordinary map
variable `#<mapName>` plus optional query variables `<mapName>_<key>` frozen with
`qUnion(makeRobustQueriesBatched(context, q))`. From there:
- expression fields: `#deo.chainLength`, `#deo.runs[1].radius.nominal`;
- a custom feature that must stay symbolic: `getVariable(context, "deo").run0` (Query as stored);
- native Sweep/Loft: query variable `deo_output` from the Variables dropdown;
- external tool, same studio: evalFeatureScript returning `getVariable(context, "deo")`
  (UNVERIFIED: JSON shape of ValueWithUnits / Vector in the response).
Design map must sit after every producer it lists; a suppressed producer is skipped with a warning.

## 7. Cost

Producer: no kernel calls; one predicate walk plus one setVariable per regen. Freezing (if any) is
paid once in Design map, not in DEO. Keep the map to scalars and per-run summaries (tens of KB, not
the 200-station-per-edge tables): every consumer copies the whole map on read.

## 8. Tests to run in Onshape

1. Publish from one DEO; open the Variables table. Retires: `[ Fxxx ]` is not shown (LIB:299-301 claim).
2. Consumer (Design map) that checks `canBeEmbeddedVariables` only, no `is` test, accepts the map and lists its keys. Retires: predicate-only gate.
3. Join mode, 3-run chain: republish `run1`; `size(evaluateQuery(context, #run1))` -> 1 and it is the middle edge. Retires: qClosestTo re-basing and opExtractWires keeping runs as separate edges.
4. Same, non-join mode: `run1` resolves via qCreatedBy -> 1 edge. Retires: dual-path query choice.
5. Two DEO instances both picked: query keys union (`output` -> both wire sets), variable keys first-wins with a warning naming the key. Retires: merge semantics.
6. Suppress one picked producer: Design map regenerates with a clean warning, no error. Retires: suppressed-source handling.
7. Freeze `deo_output` with makeRobustQueriesBatched, sweep a solid from it, then fillet the solid: the query variable still resolves. Retires: robust freeze vs EV's transient evaluateQuery.
8. Sweep from `deo_output` picked in the native Sweep dialog. Retires: setQueryVariable import and picker visibility.
9. Feature-list performance readout before and after embedding on the 1.41 s test doc. Retires: cost claim in section 7.
10. Optional: setProperty DESCRIPTION on the wire, then GET /metadata/.../e/{eid}/p. Only if the API path is ever wanted.

## 9. Open decisions

1. Descriptors vs plain values: wrap the 5-6 keys a user would read in the printed list (recommended; plain values everywhere else, arrays never wrapped) vs wrap everything.
2. Export stations at all: no (recommended); frames are re-evaluated from the published wire.
3. Per-run keys vs runs[] only: both (recommended; `run<r>` queries are the only way to get a per-run Query out, `runs[]` carries the payload) vs runs[] alone.
4. Also call embedFeatureDefinition for DEO's inputs: no (recommended; a second call replaces the first, LIB:50-52; the inputs that matter are already in the query half).
5. Delete Reese_* and query_varialble_ref.fs: after test 2 passes with the port (recommended) vs now.

## Later, if needed

Derive survival and cross-studio reads would need attributes on the output entities (std/attributes.fs:38-43,
std/derive.fs:138-145) and a loader on the far side; the map-of-attributes schema from rev 1 of this
doc is in git history. Not planned.
