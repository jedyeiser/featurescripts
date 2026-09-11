# driven_edge_offset: publishing its outputs (research, 2026-09-10)

Refs: DEO = driven_offset/driven_edge_offset.fs, U = driven_offset/edge_offset_utils.fs,
T = driven_offset/offset_run_treatment.fs, LIB = driven_offset/Reese_IMPORT_THIS.fs,
EV = driven_offset/Reese_Extract_Variables.fs. Std line numbers are the local 2878 mirror.

## 1. Purpose

driven_edge_offset should publish, once per regen, a self-describing record of what it made:
which bodies/edges are the output (by stable identity), the geometry it decided per run (line /
arc radius / fitted), and the inputs it was driven by. Consumers: later custom features in the
same studio, derived studios, an external tool via evalFeatureScript, and a future solid-making
feature that turns the record into true PMI.

## 2. What Reese's Extract Variables does

Contract (producer): one ordinary context variable named `toString(id)` holding
`{ "variable": {key: value | {value, description}}, "query": {key: Query | {value, description, debugColor}} }`
cast `as EmbeddedVariables` (LIB:142-177, LIB:313-320). Queries are stored symbolically; nothing
touches geometry (LIB:135). Consumer (EV): a FeatureList parameter names the sources (EV:43-44);
for each id it does `getVariable(context, toString(featureId))`, gates on `is EmbeddedVariables`
(EV:292-296), merges (ordinary keys first-wins with warning, query keys qUnion, EV:283-319), then
re-publishes: ordinary via setVariable, queries via `setQueryVariable(name, desc,
qUnion(evaluateQuery(context, q)))` (EV:422-424). "Print available variables" lists
`[Variable] key -- desc` (EV:387-402). Bulk-add buttons in editing logic fill the key arrays
(EV:244-269).

Verified std facts (2878 mirror):
- Type tags are bound to a declaration in a module of the import graph, not to a name
  (https://cad.onshape.com/FsDoc/relational.html). Producer and consumer importing LIB from
  different element ids may not share the `EmbeddedVariables` tag -> silent "no compatible
  feature". EV:8 imports element 3c37750af0cf716cb0ede1e0; the local LIB is 909cf7b14bf562d9c989f0b1.
- common.fs does not export queryVariable.fs (std/common.fs has no such line; std/geometry.fs:76
  does). setQueryVariable needs an explicit import (std/queryVariable.fs:554-557).
- @setVariable accepts any name string; the identifier regex lives only in the Variable feature
  (std/variable.fs:798-805) and checkQueryVariableName (std/queryVariable.fs:519-532)
  (https://forum.onshape.com/discussion/17100/...). Non-identifier names are unreachable from `#`.

Not adopted verbatim, because:
1. Typed map + `is` gate = tag-identity risk across elements/versions (above; raw 01 limitation 2).
2. FeatureList discovery: EV must follow all sources; suppressed or reordered sources vanish silently
   into one generic warning (raw 01 limitations 7, 10).
3. Context-variable carrier: nothing crosses a derive (raw 01 limitation 6); the `[ Fxxx ]` variable
   is a persisting context variable anyone can read or overwrite (limitation 5).
4. Consumer evaluates every published query every regen into transient, non-robust queries;
   evaluate-on-use is hard-wired off (EV:175, EV:202; limitation 8).
5. No name validation, ordinary/query namespace collisions unchecked, FS 3044 files with a broken
   import and a missing icon (limitations 1-5).

Kept: a producer publishes one plain self-describing map of ordinary values and Queries with
descriptions; a Query in a map stays symbolic until a consumer freezes it; "print available keys" UX.

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

Carrier per D2: A = attribute on output entity, M = ordinary map variable, Q = query variable.

| # | Item | Type | Where | Carrier | Notes |
|---|---|---|---|---|---|
| 1 | All output bodies | Query | qCreatedBy(id, BODY); wires DEO:896-899, unjoined DEO:907 | A `output` + M `output` + Q (opt) | the identity every consumer starts from |
| 2 | Wire per G0 path | Query | id + "wire" ~ "link" ~ k, DEO:896 | A `link<k>` + M `link<k>` | join mode only (DEO:891); wires local to emitRuns today |
| 3 | Per-run curve edge | Query | id + "run" ~ r, DEO:776 | A `run<r>` + M `run<r>` | join mode: qCreatedBy evaluates empty after DEO:897/902; re-base with qClosestTo on wire edges |
| 4 | Corner fill edge | Query + map | id + "fill" ~ r, DEO:797; fill data T:133-137 | A `fill<r>` | payload {kind arc/spline, radius?} |
| 5 | Terminal extension edges | Query | id + side ~ r, DEO:822 | A `startExtension<r>` / `endExtension<r>` | fabricated geometry; tag so consumers can exclude it |
| 6 | Run classification | string line/arc/freeform | emitted[r].kind, DEO:871 | A run payload + M runs[] | |
| 7 | Arc-run radius | ValueWithUnits | DEO:872 (center/normal dropped) | A run payload + M runs[] | PMI-shaped {nominal, tolerance}; emitRuns must keep center/normal |
| 8 | Line-run endpoints | Vector x2 | classifyPoints U:380 shape.start/end | A run payload | |
| 9 | Run arc-length range | ValueWithUnits x2 | stations[run.start/end].arc, signed from zero point (U:1253) | A run payload + M runs[] | export this convention; chain-start arc derivable via zeroArc |
| 10 | Run exact endpoints | Vector x2 | run.startPoint/endPoint T:125,195 | A run payload | undefined -> points[run.start/end] |
| 11 | Corner treatment | string gap/extended/trimmed | T:125,133,195 | A run payload | |
| 12 | Terminal records | map {action, distance, squareness, kink?} | T:510 | A run payload | strings + ValueWithUnits only |
| 13 | Zero point | Vector | DEO:177, evZeroPoint U:219 | M `zeroPoint` + A body payload | input entity also as M `zeroPointEntity` (symbolic Query) |
| 14 | Measure-along, frame alignment | enum -> string | DEO:50, DEO:80 | M + A body payload | store toString(enum); FS consumers compare via the enum, not the string |
| 15 | Chain length, zero arc, link count | ValueWithUnits, ValueWithUnits, number | buildChain U:729-777 | M + A body payload | |
| 16 | Reference wire + delta | Query, ValueWithUnits | DEO:60, DEO:185; U:1800 | M `referenceWire`, M `offsetDelta` | REFERENCE_WIRE mode only; keys omitted otherwise |
| 17 | Profile break coordinates | array ValueWithUnits | discontinuityCoords DEO:640 | M `profileBreaks` | already computed for insertCrossings; reuse, do not recompute |
| 18 | Output name | string | DEO:53 | M `outputName` + A body payload | |
| 19 | Inputs (offsetEdges, offsetProfile) | Query | DEO:74, DEO:77 | M symbolic | never attribute-tagged: they belong to other features |
| -- | Stations/frames table | array of maps, up to 200/edge (U:112) | U:985-988 | not exported | bulk; downstream re-evaluates frames from the published wire |
| -- | Profile samples, placed points, margins | arrays | DEO:205-206, DEO:463-519 | not exported | intermediate; reconstructible from output |
| -- | alongRef tables (arcs, thetas, xs, points) | arrays | U:1791-1802 | not exported | internal to the s - h*theta mapping; expose only delta and referenceWire |
| -- | Run end tangents | Vector | U:2727-2751 | not exported | evCurveTangent on the published edge is the truth |

## 5. Concrete design

Attribute schema (identity carrier, one attribute per key, name-per-key so boolean merges keep
all of them, std/attributes.fs:41-43):
- name: `"dm:" ~ map ~ ":" ~ key`; keys as in the table: `output`, `link<k>`, `run<r>`, `fill<r>`,
  `startExtension<r>`, `endExtension<r>`.
- value: `{ "map": map, "key": key, "v": 1, "data": <payload map> }`. No type tag.
- body payload (`output`, `link<k>`): {outputName, linkIndex, zeroPoint, measureAlong,
  frameAlignment, chainLength, zeroArc, offsetDelta?, runIndices:[r...]}.
- run payload (`run<r>`): {kind, radius:{nominal, dimensionType:"RADIUS", tolerance:{toleranceType:"NONE"}}?,
  center?, normal?, start, end, arcStart, arcEnd, cornerKind, terminalStart?, terminalEnd?}.
  Values are strings, numbers, ValueWithUnits and Vectors only -- never Queries (meaningless in
  another context) and never enums (compare-by-reference does not survive JSON).

Map variable (in-studio scalars and autocomplete): `setVariable(context, map, m, desc)` with
`m = { "schema": "deo/1", "outputName", "zeroPoint", "measureAlong", "frameAlignment",
"chainLength", "zeroArc", "linkCount", "profileBreaks", "runs": [run payloads],
"output": qCreatedBy(id, BODY), "link<k>": Query, "run<r>": Query, "offsetEdges", "offsetProfile",
"zeroPointEntity", "referenceWire"?, "offsetDelta"? }`. Queries stay symbolic; a consumer freezes
them with evaluateQuery or makeRobustQueriesBatched (std/feature.fs:643) when it needs to.
Conditional keys are omitted, never undefined.

Query variable: only `map ~ "_output"` = qCreatedBy(id, BODY), so a native Sweep/Loft can pick
the wires from the Variables dropdown. Gated by a boolean parameter, default false. Needs
`import(path : "onshape/std/queryVariable.fs", version : "3070.0")` in DEO. No per-run QVs.

Map name: a new `definition.mapName is string` parameter, validated with
verifyVariableNameIsValid (std/variable.fs:798, exported via std/common.fs:63). Empty = publish
nothing. It is the only key of the attribute names, so it must be an identifier.

Where the calls go: a new `publishOutput(context, id, definition, result, sourceChain, alongRef,
zeroPoint)` called after emitRuns (DEO:220) and before debugOutput (DEO:222). This is after
opExtractWires (DEO:897) and opDeleteBodies (DEO:902), which is mandatory: any attribute set on a
pre-extraction curve body dies with it at DEO:902. In join mode `qCreatedBy(id + ("run" ~ r),
EDGE)` evaluates empty after extraction, so per-run edges are re-based as
`qClosestTo(qCreatedBy(wireId, EDGE), midPoint)` with midPoint the run's middle sample. Runs
collapsed to two or three stations by corner trimming (T:161-162) still have a distinct middle
point; test 1 checks that opExtractWires preserves the edge split. In non-join mode the
qCreatedBy(id + ("run" ~ r), EDGE) query is used unchanged.

emitRuns must additionally return (today it returns only `emitted`, DEO:910): `{ "runs": emitted,
"links": { "link<k>": qCreatedBy(wireId, BODY) } }`, with each emitted run carrying
`edge` (the re-based Query), `midPoint`, `center`/`normal` for arcs (currently dropped at DEO:870),
and `shape.start/end` for lines. Nothing else in DEO changes; debugOutput reads `result.runs`.

```
function publishOutput(context is Context, id is Id, definition is map, result is map,
    sourceChain is map, alongRef, zeroPoint is Vector)
{
    const map = definition.mapName;
    if (map == "")
    {
        return;
    }
    verifyVariableNameIsValid(map, "mapName");
    var m = { "schema" : "deo/1", "outputName" : definition.outputName,
        "zeroPoint" : zeroPoint, "chainLength" : sourceChain.totalLength,
        "output" : qCreatedBy(id, EntityType.BODY), "runs" : [] };
    tagEntities(context, map, "output", qCreatedBy(id, EntityType.BODY), bodyPayload(definition, sourceChain, alongRef, zeroPoint));
    for (var r = 0; r < size(result.runs); r += 1)
    {
        const payload = runPayload(result.runs[r]);
        m.runs = append(m.runs, payload);
        m["run" ~ r] = result.runs[r].edge;
        tagEntities(context, map, "run" ~ r, result.runs[r].edge, payload);
    }
    setVariable(context, map, m, "driven_edge_offset output map");
}

function tagEntities(context is Context, map is string, key is string, entities is Query, data is map)
{
    setAttribute(context, { "entities" : entities, "name" : "dm:" ~ map ~ ":" ~ key,
        "attribute" : { "map" : map, "key" : key, "v" : 1, "data" : data } });
}
```

Duplicate map names (two DEO instances, or a Design map feature using the same name): before
writing, `evaluateQuery(context, qHasAttribute("dm:" ~ map ~ ":output"))` non-empty means another
feature owns the name; throw regenError. One extra attribute-filter query; loud, not silent.

## 6. External API path

evalFeatureScript (POST /partstudios/d/{did}/{wvm}/{wvmid}/e/{eid}/featurescript) with a script
that returns `getAttributes(context, {entities: qHasAttribute("dm:core:run0"), name: "dm:core:run0"})`
for geometry-bound data and `getVariable(context, "core")` for the map; the Onshape-recommended
route for attributes (https://forum.onshape.com/discussion/6912/...). Correlate entities with
tessellation/bodydetails ids via evaluateQuery transient ids in the same script. NAME already goes
through setProperty (DEO:919-931) and is returned by /metadata/d/{did}/{wvm}/{wvmid}/e/{eid}/p; a
one-line DESCRIPTION summary could join it. UNVERIFIED: wire bodies appear in /metadata and accept
DESCRIPTION; ValueWithUnits/Vector JSON shape from evalFeatureScript; named attributes on wire
bodies survive derive (documented for entities generally, std/attributes.fs:38-43; wires untested).

## 7. Cost

setAttribute and setVariable are context writes, not geometry ops; each setAttribute resolves its
entity query (an attribute-filter-class resolution, the same cost class std sheet metal pays every
regen, std/frameAttributes.fs). No explicit evaluateQuery is needed at publish time except the one
duplicate-name guard. Added per regen: 1 + L + R + F + E attribute writes (links, runs, fills,
extensions), one setVariable, optionally one setQueryVariable. Against the measured 1.41 s
(memory: driven-offset.md) this is noise. Consumers re-running on unrelated edits is tree-order
behaviour that already exists (https://cad.onshape.com/FsDoc/debugging-in-feature-studios.html).

## 8. Tests to run in Onshape

1. Join mode, 3-run chain: publish; later feature prints size(evaluateQuery(qHasAttribute("dm:m:run1"))) -> 1 and it is the middle edge. Retires: qClosestTo re-basing and opExtractWires keeping runs as separate edges.
2. Same, non-join mode: dm:m:run1 resolves via qCreatedBy(id + "run1", EDGE) -> 1 body. Retires: dual-path query choice.
3. Derive the output wire into a fresh studio; print the same count -> 1, and getAllAttributes on the derived edge shows the payload. Retires: attribute survival on wires through derive (UNVERIFIED in section 6).
4. Downstream custom feature: getVariable(context, "m").output resolves the wires; #m.chainLength works in an expression field. Retires: symbolic Query in a plain map, no type tag.
5. Enable the query-variable toggle; open Sweep, pick m_output from the Variables dropdown -> wire selected. Retires: setQueryVariable import and picker visibility.
6. evalFeatureScript returning getAttributes + getVariable; inspect JSON for a ValueWithUnits and a Vector. Retires: API serialization shape.
7. setProperty DESCRIPTION on the wire, then GET /metadata/.../e/{eid}/p. Retires: wire bodies in /metadata.
8. Two DEO instances with mapName "m": second must regenError with the duplicate-name message. Retires: silent overwrite.
9. Feature-list performance readout before and after publishing on the 1.41 s test doc. Retires: cost claim in section 7.
10. Boolean/split a solid swept from the tagged wire; check dm:* attributes on resulting faces/edges. Retires: whether the future solid-making feature can find its radius payload to call setDimensionedEntities.

## 9. Open decisions

1. Map name source: new `mapName` parameter (recommended; explicit, validated, empty = off) vs deriving from outputName (not an identifier in practice).
2. Query variable publication: boolean default off, `<map>_output` only (recommended) vs always publish vs per-link QVs.
3. Payload duplication: run payloads in both edge attributes and the map variable via one builder (recommended; attributes are truth, map is convenience) vs attributes only.
4. Arc-length convention: signed from zero point, matching profile X (recommended; zeroArc exported so chain-start arc is derivable) vs from chain start.
5. Duplicate map name: regenError (recommended, loud) vs warning with last-writer-wins.
6. DESCRIPTION property summary on wires: add if test 7 passes (recommended) vs skip.
7. Tagging fills and extensions: tag both (recommended; consumers must be able to exclude fabricated geometry) vs runs only.
