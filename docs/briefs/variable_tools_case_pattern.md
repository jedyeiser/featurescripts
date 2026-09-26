# Research brief: Variable tools + Case pattern (Explore agent, 2026-09-25)

Source material for docs/explainers/variable_tools and case_pattern. Paths relative to repo root.

## A. Variable tools -- doc a47f90bfa6b17a59e20cebd0 (ws 0bd1fa43...)
Tabs: extract_outputs (3cac74f0..., producer lib), extract_variables_utils (a4dcd70c..., consumer lib, re-exports
extract_outputs), extract_variables (7510fab4..., feature), variable_tools_tests (fdc7cb2a...).
Versions: V1 78504463..., V2 f4f872fe... (Region, Track, modified*, Show). extract_outputs mv b8c80ac... unchanged since V1.

### A1 Extract variables (variable_tools/extract_variables.fs)
Purpose: one per tree block; takes the block's features as Sources and publishes #variables + native query
variables. Replaces piles of hand-made Query Variable features (header :9-32). Port of Evan Reese's Extract
Variables approach (utils:16). vs native: one node for N names; composes selections a native QV can't (region
flood fill, chain end, edges between points, shared edges, largest N).

Concepts:
- Producer/consumer contract: producer ends with embedStandardOutputs -> map stored in hidden variable named
  toString(id) ("[ Fxxx ]", not a valid identifier so never under #) (extract_outputs.fs:8-11, 219-230).
- Any feature is a source: no embed -> implicit output/outputFaces/outputEdges/outputVertices = qCreatedBy (utils:223-232).
- Every source also offers modifiedFaces/Edges/Vertices = entities whose last modifying op belongs to the source;
  the only way to reach what in-place features (Move boundary) changed (utils:234-260).
- Addressing key@n: sources numbered in tree order; key unique -> bare name; shared -> key@1, key@2 and bare errors
  as ambiguous; never merged (utils:132-135, 201-220).
- Hold (default; std makeRobustQueriesBatched -- follows identity-preserving edits) / Track (+startTracking filtered
  to present entity types: split halves, rebuilt edges follow) / Evaluate on use (raw query, re-resolved where used;
  Source key entries only) (utils:779-815).
- Producers recognised by the `extractable` marker field, not type tag (extract_outputs.fs:23-26).
Figures: (a) producers with hidden [Fxxx] maps -> Extract variables (Sources -> entries) -> #prefix_name + QV
dropdown; (b) timeline of an edge through Split then Move boundary: what Held/Tracked/Evaluate-on-use resolve to.

Dialog (extract_variables.fs:40-150): Sources (FeatureList); Prefix (""; joined with _); Add keys from sources
(button); Entries array (label "#x_name #x_sourceKey"); Print keys (off; prints every address
"[query] outputEdges (source 1, implicit) -- desc", utils:317-329); Manifest (""; one map variable
{schema:"extractManifest/2", variables, queries:[names]} for API readers).
Entry: Type = Source key (default) / Filtered / Closest to point / Shared edges / Chain end / Edges between points /
Bridging curve input / Region (utils:28-46). Source key (address, e.g. output, trimEdges, output@2). Second source
key (Shared edges, Region = boundary edges; empty -> whole connected patch). Published name (empty -> address with
@ -> _). Show (off; highlights result in producer debugColor, prints "[show] name: N entities (COLOR)", 247-261).
Entity type Bodies/Faces/Edges(default)/Vertices (Filtered, Closest). Body type Any/Solid/Sheet/Wire, Body name
contains, Keep only the largest + How many 1-1000 (Filtered). Point (vertex/MC) for Closest, Chain end, Bridging,
Edges between, Region. Second point + Other side (closed chain: longer way) for Edges between. Region publish:
Faces / Boundary edges / Faces and boundary edges. Chain end: End Nearest/Farthest; Publish Vertex/Edge/Edge and
vertex. Evaluate on use (Source key only). Track (hidden when Evaluate on use, line 137).
Add keys (editing logic 268-308): appends one Source key entry per address not yet listed, source order; skips
outputFaces, outputVertices, inputs, modifiedFaces, modifiedVertices; modifiedEdges only for non-embedding sources.

Outputs: <prefix>_<name>; query -> publishQueryVariable, value -> setVariable after verifyVariableName. Duplicate
name = regenError (181-184); manifest clash = regenError; QV name used by ordinary variable -> std error (utils:770-777).
Notices, one per regen (218-231): WARNING "Entry k (key): reason." (unknown key, ambiguous (lists addresses), value
not geometry, missing point, closed chain has no end, not one chain, both points same vertex, source has no
faces/edges); INFO "Nothing published yet: press Add keys..."; INFO "Published N name(s); empty at this point in
the tree: a, b." (empty queries still published).

Algorithm: 1 readSources (utils:143-221) sort by tree order, optionalVariable slot, canBeEmbeddedVariables
(extract_outputs 118-144). 2 per source: embedded keys, implicit qCreatedBy for missing standard keys, lazy modified*.
3 addresses. 4 resolveEntry (utils:351-468) via lookupKey (293-305); keyValue resolves modified* by scanning
qEverything lastModifyingOperationId (249-287); composed: filteredEntities (494-527: type, body type, owner-name regex
case-insensitive \Q..\E, largest N by length/area/volume); qClosestTo; shared edges = qIntersection(edgesOf(a),
edgesOf(b)) incl. face edges (483-488); chainEnd (589-629: free vertices used by one edge; nearest/farthest);
Bridging = nearest end edge+vertex one query; edgesBetween (637-704: constructPath, nearest path vertices, shorter
side unless Other side); regionAround (713-744: flood fill from face nearest seed not crossing boundary; boundary =
edges with exactly one adjacent face in region). 5 name, dup check, Show. 6 publish: freeze / Track / raw; composed
always held (466-467). 7 manifest, one notice.

Examples: variable_tools_tests.fs region tests 6/6: 100 mm cube, top+front split by planes x=-20/+20, seed
(0,-10,50), keys topAndFront + cuts -> bounded faces 2 at x 0; boundary 6 edges (4 cuts); both 8; no boundary key ->
patch of 6 faces; body source -> all 10 faces (cuts don't close a region on a cube); no point -> error on x_point.
Tracking tests (build_/check_extract_tracking.py 4/4): 100x50 surface extrude; X1 edge_held/edge_tracked (closest to
x=100); Move boundary +10 mm; X2 modifiedEdges as moved_edges; Ruled surface on edge_tracked. Held follows extend to
x=110 (finding: default freeze already follows an extend); Tracked = exactly moved edge; modifiedEdges = moved edge +
2 lengthened neighbours (3); Ruled regenerates. Examples studio ca7eb26e... box_* entries. Live: Split+ 3 regions ->
pieceCount/output/outputEdges/splitEdges/splitFaces; Curve_tools prefix shelf_ (10 names).

Limits: adding a source that offers an existing key (every source offers output) makes bare `output` ambiguous;
modified* scans every entity (cost) and loses entities later modified; Track picks up derived features' entities;
Evaluate on use over a transient union = the RD 20FOU Ruled-surface failure; Keep only largest ignored for vertices
(utils:520); API array items need every item param (correction 38); QVs not enumerable from FS (correction 18);
instance version bumps via UI Reference manager only.

### A2 Producer library (variable_tools/extract_outputs.fs)
embedStandardOutputs(context, id, {output, outputDescription?, inputs?, variables?, queries?}) (245-278) builds
output (GREEN), outputFaces/Edges/Vertices = qOwnedByBody(output), inputs (BLUE, qNothing if absent), then custom
keys. Lower level embedVariableMap. Descriptors extractableVariable(value, desc), extractableQuery(q, desc[, color]);
predicates 63-102 forbid Query in the variable half. optionalVariable (44-48) because getVariable(..., undefined)
throws (correction 32).
Rules: a key must ALWAYS be present (qNothing/0/"none") or consumers report key not found after an edit (28-29);
modify-only features must embed output; flat query half; seam outputs dedupe coincident edges (Split+ bug).
Taxonomy (regions, seams, ends, correspondence): Offset+ startVertex/endVertex/startEdge/endEdge, cornerArcs,
boundaryEdges; Mutual Trim+ keptFaces1/2, trimEdges; Split+ <region>, <region>Edges, outside/inside, cut |
startCut/endCut | cut1..N, regionCount, pieceCount; Clean wire ends, breakVertices, run1..N; Map/Merge curve ends;
Thicken+ towardFaces/awayFaces/sideFaces.
Producers: merge_curve:357, map_curve:270, fillet_wire:195, evaluate_profiles:295, clean_wire:499,
driven_offset_surface:2679, driven_edge_offset:1387, evaluate_offset:950, create_offset_profile:1087, unwrap:450,
offset_plus:228/296, thicken_plus:188, mutual_trim_plus:140, split_plus:255, station_geometry:143,
betterCurveTrim:662, Move_Along_Edge:233.
Figure: anatomy of one embedded map {variable:{...}, query:{output, outputFaces, ..., inputs, trimEdges}}.

## B. Case pattern (case_pattern/case_pattern.fs; design case_pattern/DESIGN.md)
Doc 2099413dd91f34578b385892 (ws a875a90e..., tab 05ce3846...). Tests: "Case pattern tests" studio via
devtools/onshape/build_case_pattern_tests.py (T1-T5 pass, DESIGN.md:4); templates case_pattern_qv_template.json,
case_pattern_variable_template.json. Icons installed.
Purpose: "apply to each" for the tree: build a chain once against named query variables and #values (case 1), re-run
for more cases each with own selections/values; bodies get the case name suffix (header 9-23). vs native Pattern
"Reapply features" (moves by transform), Evan Reese Query Pattern (rebinds ONE seed variable): rebinds up to 8 QVs +
4 values per case, no transform. Closest analogue CATIA PowerCopy (DESIGN.md:12-15).
Concepts: template -> cases; case 1 = live geometry; rebinding per case, case 1 restored afterwards (877-885);
pattern frame (correction 41): each case inside setFeaturePatternInstanceData(identityTransform()) -- only
FeatureList params remapped onto the case's copies (native QV "created by <listed feature>"); clicks and
qCreatedBy(makeId(...)) stay on case 1. USER RULE: reference in-list geometry via a QV "created by", never by clicking.
Figures: (a) table rows A/B/C x columns #top #rim #bossH #edgeR, chain Boss -> Rim fillet -> #bossEdges -> fillet
drawn once replayed per row; (b) two lanes: clicked reference -> case 1 geometry vs QV created-by -> this case's copy.

B1 Case template dialog (76-434): Case 1 name ("A"); Inputs array (Name = QV name, may exist, redefined; Case 1
selection body/face/edge/vertex/MC); Case values array (Name; Type Length/Angle/Area/Volume/Number/Text; Case 1 value
typed field; Area/Volume plain reals mm2/mm3 (correction 30); bounds +-1e7 mm, +-1e6 deg, +-1e12); Input slots group
(read-only "Input k: #name", showSlotK); Further cases array (Case name; per input read-only label + Selection; per
value label "#name (length)" + typed Value; hidden use2..8, useValue1..4, v1..4Kind); Debug > Print bindings.
Editing logic caseTemplateEditLogic (574-606) fills labels/flags/types.
Outputs: QVs (raw selection, no description, 470), #values (491), hidden signature toString(id) {caseTemplate,
caseName, names, queries, valueNames, values, cases[{caseName, queries, values}], debug} (524-533). Errors: "Name case
1.", "Add at least one input.", max 8 inputs / 4 values, invalid name, "Input name #x is used twice", "Case 1
selection for #x selects nothing", "Name #x is used twice", "Name case k.", "Case name "B" is used twice". No
success notice. Algorithm: return if inside a pattern frame (437); bind inputs; bind values; collect rows; publish
signature; debug print.

B2 Case pattern dialog (697-728): Case template (FeatureList, exactly one); Features to repeat (FeatureList); Keep
group (Parts, Surfaces, Curves and points, Mate connectors, Planes on; Sketches off); Name separator ("_", <=8);
Template names (hidden, cached by casePatternEditLogic 926-945 as "featureIndex\tbodyIndex\tname" because getProperty
throws in regen, correction 36).
Outputs: per case bodies under id+"caseK" filtered by Keep (unkeptBodies 1019-1049), named from matching case-1 body
with its suffix swapped (caseBodyName 1007-1016: Boss_A -> Boss_B; no suffix -> _B appended). Merged-into-existing
bodies are modified, keep names. Notices: errors "Select a Case template." / "Select one Case template." / "Select
the features built on the template's inputs."; INFO "The Case template has no further cases."; WARNING "Not built:
B: input 1 (#top) selects nothing; C: feature 2 failed (...)" or "value m (#x) is not set; edit the Case template";
regenError "No case was built. ..."; INFO sketch caveat; INFO "N bodies kept Onshape's default name: edit this
feature to refresh case 1's names."
Algorithm (729-910): findTemplate (963-985); valuesSortedById; per case: bind (setQueryVariable/setVariable; empty ->
failure, skip), push frame id+"caseK", runListedFeature (1064-1099; only on SELF_INTERSECTING_CURVE_SELECTED pop frame
and retry once under caseK.directI -- lets edits of outside geometry succeed), record new bodies keyed i.j, pop frame,
on failure delete created bodies, delete unkept, rename; restore case 1; notices.
Examples (build_case_pattern_tests.py 1-24, 259-307): blocks A 60x40 rect, B 90x30 at x 200, C pentagon r35 at x 400.
T1 inputs #top #rim; values #bossH A15/B25/C8 mm, #edgeR Number A3/B3/C2; chain Boss (extrude #top by #bossH), Rim
fillet 2 mm (outside geometry -> runs outside frame), QV #bossEdges created by Boss, fillet #bossEdges at #edgeR ->
Boss_B 25 tall R3, Boss_C 8 tall R2, rims filleted. T2 Move face first (Query Pattern's open bug) -> B +5 mm in +X, C's
54 deg face out 5. T3 clicked in-list edge -> ERROR. T4 in-list qCreatedBy(makeId) -> ERROR. T5 in-list via QV
created-by -> posts under B, C, all edges filleted.
Limits (DESIGN.md 6b, meant for docs): sketches re-solved per case (constraints to origin/default planes not
reapplied; references on input geometry stay on case 1; untested live); cases run in order and see earlier results;
failed case can't undo edits to existing geometry; only one refusal code retried (fillet, move face verified; delete
face, chamfer, shell, draft, booleans into existing untested); sheet metal/derived refused in patterns; thin extrude,
cut list differ; unverified: MCs owned by in-list bodies, nested Case patterns; cost (cases+1) x chain; names refresh
only when Case pattern dialog edited; rows update only when template edited; only new bodies renamed; template in the
repeat list does nothing.

## Unclear / contradictory
1. POSSIBLE LATENT BUG: Extract variables Filtered "Body name contains" calls getProperty(NAME) in the feature body
   (utils:511); correction 36 says getProperty throws in regen. Untested.
2. variable_tools/extract_variables_schema.md stale (says design_map.fs / design_map_query_utils.fs; producer table
   2026-09-23; no Track, modified*). DOCUMENT_MAP.md:120 calls it a copy -> drift.
3. Memory says "7 entry types"; code has 8 (Region).
4. Schema says composed types "always frozen"; code allows Track on them.
5. DESIGN.md out of date: per-case values listed as later work (implemented 475-533, 776-788); template-in-list
   decision resolved as separate FeatureList.
6. DESIGN.md 4 says body matching by feature id; code matches by (list index, creation order) i.j; editing logic
   filters sketches but regen j counts all new bodies (828-834 vs 933) -- low risk.
7. Case template stores raw selections (no robust freeze, no description).
8. Unverified: generic setQueryVariable dropdown visibility; VT V3 pending for icons; Design_Master pilot blocks built?
9. variable_tools_tests.fs is still a harness (candidate for in-tree conversion).
