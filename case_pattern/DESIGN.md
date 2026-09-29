# Case Pattern -- design note

**2026-09-26: v2 BUILT (Define case / Close case / Case pattern) -- section 11 is current. Sections 2-10
describe v1 (Case template + Case pattern); the mechanism notes (frame remapping, outside-list retry,
sketches, limitations 6b) still apply unless section 11 says otherwise.**

Document: case_pattern (2099413dd91f34578b385892 / w a875a90e6a8ebf49cc0eff73).
Status: v1 built and passing tests T1-T5, 2026-09-24 (case_pattern.fs holds both features).

## 1. Idea

"Apply to each" for the feature tree. The user builds a feature chain once against a set of
named query variables (the template, case 1), then re-runs that chain for further cases, each
case rebinding every variable to new geometry and suffixing its outputs with the case name.

Prior art (researched 2026-09-24): Evan Reese's Query Pattern rebinds ONE seed variable per
entity. Nothing public rebinds several variables per iteration (Konst_Sh asked for this in the
Query Pattern thread; IR 6933 "user-defined feature lists" is open since 2017). Closest
analogue outside Onshape is CATIA PowerCopy.

## 2. Mechanism (verified 2026-09-24, correction 41)

    for each case k:
        bind every template name to case k's selections   (setQueryVariable)
        push pattern frame (identity transform) for id + "caseK"
        for each listed feature f:
            f(id + "caseK")                       -- in the frame
            on SELF_INTERSECTING_CURVE_SELECTED only:
                pop frame; f(id + "caseK" + "directI"); push frame
        pop frame; delete unkept bodies; name new bodies
    rebind case 1

What the frame does, measured:
- Remaps FeatureList parameters of listed features onto the case's copies -- a Query Variable
  "created by <listed feature>" follows each case. Clicks and qCreatedBy(makeId(...)) query
  strings do NOT remap; they stay on case 1 (tests T3, T4 are expected ERRORs documenting this).
- Refuses kernel edits of geometry from outside the list (fillet an existing edge, move an
  existing face) with SELF_INTERSECTING_CURVE_SELECTED -- Query Pattern's open Move face bug.
  Those run fine outside the frame under a fresh sub-id, so only that error is retried; any other
  failure stands, because outside the frame an unremapped in-list reference would hit case 1.

User rule: **reference geometry made inside the list through a Query Variable "created by"**, never
by clicking it. The Case template's inputs cover everything outside the list.

## 3. Features (restructured 2026-09-24 per user: cases live in the template)

### 3.1 Case template
| Parameter | Type | Notes |
|---|---|---|
| Case 1 name | string | e.g. "A" |
| Inputs | array { inputName, query "Case 1 selection" } | up to 8; a name may already exist (redefined) |
| Case values | array { valueName, valueKind, typed case-1 field } | up to 4; kinds Length, Angle, Area (mm^2), Volume (mm^3), Number, Text; the template DEFINES #name with case 1's value |
| Input slots | group of read-only strings "Input k: #name" | editing logic fills them, hides unused |
| Further cases | array { rowCaseName, Input 1..8, Value 1..4 } | each slot = read-only label ("#top", "#bossH (length)") + selection / typed field; layout flags (useK, useValueM, vMKind) set by editing logic |
| Print bindings | boolean | println slots + every case's selection counts and values |

Normal run: binds case 1, validates names/values/case names, publishes
`{caseTemplate, caseName, names, queries, valueNames, cases[{caseName, queries}], debug}` under
`toString(id)`. Re-run inside a pattern frame: does nothing.

Parameter ids must be unique across the whole feature, array items included (Onshape:
"Duplicate feature parameter"), hence inputName / valueName / rowCaseName.

### 3.2 Case pattern
| Parameter | Type | Notes |
|---|---|---|
| Case template | FeatureList | exactly one Case template |
| Features to repeat | FeatureList | the body; in-list references via Query Variable "created by" |
| Keep | booleans | Parts, Surfaces, Curves and points, Mate connectors, Planes, Sketches (off) |
| Name separator | string | "_" |
| Template names | hidden string | case 1 body names cached by editing logic |

Values: before each case `#name` = that row's typed value; an unset slot (editing logic never ran)
fails that case by name; case 1's values are restored afterwards with the query bindings.
Area/volume are isReal in mm^2/mm^3 (isArea/isVolume are not dialog types, correction 30).

## 4. Outputs and naming
- Case k outputs = bodies created under `id + caseKey` that still exist when the case ends,
  filtered by the Keep toggles. Bodies merged into existing parts are "modified", not created,
  and keep their names.
- Name = the matching TEMPLATE body's name with case 1's suffix stripped and case k's added:
  `Rib_Left` -> `Rib_Right`. Matching: replayed feature id `<id>.<case>.<inst>.<templateFeatureId>`
  -> template feature id -> bodies that feature created; tie-break by position when a feature
  makes several bodies.
- Mate connectors and planes are bodies; same rule.

## 5. Sketches
Sketches are re-solved per case. Per Onshape's pattern help, with Reapply features:
- constraints/dimensions to the origin or Top/Front/Right are NOT reapplied -> those entities
  keep their case-1 position;
- other external references are re-evaluated per instance -> references to geometry created
  INSIDE the list follow the case; references picked directly on template input geometry
  resolve to the same template geometry (constrained, but to the wrong thing).

Rule for users: build sketches on geometry DERIVED from the inputs inside the list (mate
connector / plane on `#face`, Extract of `#face` edges). Case pattern cannot see sketch
constraints from FeatureScript, so it emits one generic `reportFeatureInfo` when the list
contains a sketch (`containsSketch`).

## 6. Failure reporting
- Per-case failures are collected, never swallowed (Query Pattern's bare `try` hides them).
- Some cases failed -> `reportFeatureWarning` naming them (requested output missing).
- All failed -> `regenError`.
- Sketches present -> `reportFeatureInfo` (section 5).

## 6b. Limitations (user-facing; carry into the tool's documentation)

Rules
- Reference geometry the repeated features create through a Query Variable ("created by", or
  "tangent connected" / "bounded faces" after a Split, which creates no faces). Clicks and
  qCreatedBy(makeId(...)) queries stay on case 1 (tests T3, T4).
- Sketches are re-solved per case, but dimensions/constraints to the origin or Top/Front/Right
  are not reapplied (entities keep case 1's position), and references picked directly on input
  geometry stay on case 1. Build sketches on geometry derived from the inputs. (Untested live.)

Behaviour to expect
- Cases run in order and see earlier cases' results. If case B merges or consumes geometry case C
  selects, C can fail or resolve differently.
- A failed case deletes the bodies it created but cannot undo edits it already made to existing
  geometry (e.g. a fillet on an existing block that succeeded before a later feature failed).
- Edits to geometry that existed before the repeated features are refused inside the pattern
  frame (SELF_INTERSECTING_CURVE_SELECTED) and retried outside it. Verified for fillet and move
  face; delete face, chamfer, shell, draft, booleans into existing parts are untested -- a
  different refusal code is not retried and fails that case with its message.
- Sheet metal and derived features are refused by Onshape's feature-pattern machinery; some
  features (thin extrude, cut list) behave differently in a pattern. Expect per-case failures.
- Mate connectors owned by a body made in the repeated features: whether they follow each case's
  copy is untested (likely needs the owner through a Query Variable).
- Cost: every rebuild runs the repeated features (cases + 1) times.
- Part names: case 1's names are cached when the Case pattern dialog is edited; rename case 1's
  parts, then edit the Case pattern, for the case names to follow. Only new bodies are renamed.
- Case rows are laid out by the Case template's editing logic: after adding an input or value,
  or changing a value's type, edit the template so every row gets the slot.
- Nested Case patterns: not supported yet (section 9).

## 7. Risks
1. DONE: Move face first (T2) works via the outside-frame retry.
2. DONE: template re-run inside the replay skips (isInFeaturePattern) -- T1-T5.
3. Mate connector owned by a body made inside the list attaches to each case's copy.
   Owner is a query -> probably needs a Query Variable "created by" to follow. Test.
4. ANSWERED: getProperty throws during regen (correction 36). Template names are cached by the
   editing logic, so they refresh only when the Case pattern dialog is edited.
7. Delete face, chamfer, shell, boolean on outside geometry: do they refuse with the same code?
5. Sketch on a mate connector on `#face`, dimensioned to an Extract of `#face` edges, follows
   two differently shaped faces.
6. Case k sees case k-1's results (sequential) -- document it, test a boolean-ADD body.

## 8. Tests
Feature-tree tests in a "Case pattern tests" Part Studio: real instances named by case +
expected result, checked from devtools (no harness tabs).

## 9. Later / out of scope for v1
- API add-on: tree-context-menu action "Explode case k to features" (writes native features
  with re-pointed references; fully adaptive sketches, sheet metal, per-case edits).
  Note: an API app cannot add a feature-tree item or run at regen -- FS is the only live option.
- Per-case values (non-query variables) -- user asked 2026-09-24; see proposal in chat.
- `#caseName` string variable inside the body.
- Cross-document PowerCopy.

## 10. Open decisions (user)
- Max input slots per case (proposal: 8).
- Template in the Features-to-repeat list (proposal) vs a separate Template parameter.
- Should Case pattern append case 1's suffix to template parts that lack it?
- Separator default "_"?

## 11. v2 (agreed and BUILT 2026-09-26; tests T1-T7 30/30 via check_case_pattern_tests.py)

User found v1 clunky: cases live in the template, which sits ABOVE the body, so case geometry is
picked rolled back, a case can never select geometry made later, and rows go stale when inputs change.
Consulted three read-only expert reviews (CAD prior art, FS feasibility, power-user UX); all agreed with
the user's proposal. Prior art: PowerCopy / UDF / Library Feature are all "define once, insert per
instance"; tables of instances only where variation is numbers on fixed geometry.

### Three features (names agreed 2026-09-26)
    Define case     declares names, kinds, case-1 selections/values; binds case 1
      ...features to repeat (= the template)...
    Close case      lists the features to repeat ONCE, Keep toggles, separator; publishes them
    Case pattern    picks the Close case; case name; slots laid out by ITS OWN editing logic from
                    the published signature; optional More cases array
Everything between Define case and Close case is the template.

Mental model (user, 2026-09-26) -- a function definition and its calls:
- Define case = the signature. Its declared names are the ONLY names a Case pattern can rebind.
- Features between = the body. Variables they define are locals, recomputed every replay, never slots.
- Close case = end of the definition (points back to its Define case, lists the body).
- Case pattern = a call with new arguments (one slot per declared parameter).
- Variables defined BEFORE Define case are globals: every case reads the same value.
Boolean parameters are typed expressions (true / false / #other / !#flag), not checkboxes.

### Decisions
- Migration: not a concern (clean break; rebuild the T1-T5 test studio).
- Multiple cases per Case pattern: supported (array), one case is the normal use.
- Booleans: Boolean kind; used natively (features CAN be suppressed by a boolean variable -- user
  correction) and in expressions. No custom skip-list unless spike S3 shows suppression is not
  re-evaluated during replay.
- Integer kind; `#caseName` / `#caseIndex` variables.
- Slots bound by NAME, not position; a Define case change reports "edit this case"; a missing value
  falls back to case 1's with an info notice. (No per-value override toggles: unchanging values are
  ordinary variables outside the template.)
- Case sub-id from the case name, not the row index (correction 47).
- One-case-per-entity: out of scope -- use Derek Van Allen's Amalgamate.
- Outputs: a Query Variable feature inside the body already follows each case ("latest case wins"
  until the next Case pattern). Optional extra: publish suffixed copies (`#rib_R`) of variables the
  body set, found by a before/after getAllVariables diff (spike S4), for features needing several cases.
- Rule to document: after a Case pattern, INPUTS are back on case 1 but variables the BODY set hold
  the last case's.
- Better errors: name the failing feature by name; detect body features clicked to case 1's geometry.

### Spike results (2026-09-26, "Case pattern v2 spikes" studio, correction 50)
- S1 as designed FAILS: FeatureList functions cannot be stored with setVariable ("Execution error").
  S1 via route B PASSES: Case pattern selects the Close case (FeatureList) and calls it in the frame;
  Close case, seeing isInFeaturePattern, replays its own list. QV "created by" followed the replay (B's
  boss filleted, A's untouched); a sketch in the list rebuilt. => Close case holds the list; Case pattern
  picks the Close case. Close case still publishes the signature (names/kinds/count) through a variable.
- S4 FAILS: getAllVariables omits query variables -> no automatic `#rib_R` copies. If wanted later,
  outputs must be declared by name.
- S2 PASSES: Case pattern editing logic read the Close case's published variable (and other variables)
  -- editing logic only runs on a parameter CHANGE in the dialog, not on open or REST insert.
- S3 PASSES: suppression by expression IS re-evaluated per replay. Case 1 #flag false -> post suppressed
  under A; replay with #flag true -> post built under B. Suppressed features stay in the FeatureList.
  REST format: feature.suppressionState = BTMSuppressionStateExpression-1811 with value
  BTMParameterQuantity-147 expression "#flag" (parameterId "feature-suppression-state").

### Spikes (spike_v2.fs, throwaway tab: "Close case (test)", "Case pattern (test)")
- S1 feature functions published through setVariable, read back and replayed in the frame: does a
  Query Variable "created by" in the body still follow the replay?
- S2 getVariable/getAllVariables in editing logic sees earlier features' variables.
- S3 body feature suppressed by #flag: re-evaluated per replay, always skipped, or always run? Is a
  suppressed feature still in the FeatureList?
- S4 getAllVariables lists query variables; before/after diff finds variables the body set.

### Final mechanism (built)
- Case pattern binds a case (inputs + values + #caseName/#caseIndex), publishes CASE_REPLAY_KEY, and
  calls the Close case's function under id + "case_<name>" (no frame). A second call (mode "outputs")
  reads the outputs after the replay (query parameters resolve at call time).
- Close case, seeing CASE_REPLAY_KEY: sorts its list by original feature id (keys arrive prefixed
  outside a frame), pushes the frame on its OWN id, runs every feature with that id, and pops/retries
  outside the frame on SELF_INTERSECTING_CURVE_SELECTED (runListedFeature, as v1). Records body origins.
- Case pattern then deletes failed/unkept bodies, names bodies from the Close case's cached names,
  publishes #<case>_<output> (freeze / track / on use), restores case 1's inputs and #caseName/#caseIndex.
- Why this shape: correction 50 (every dead end measured).

### Tests (devtools/onshape/build_case_pattern_tests.py + check_case_pattern_tests.py, 30/30)
T1 values + outside-list fillet + outputs; T2 move face first, two cases in one pattern; T3 clicked
in-list reference -> ERROR (expected); T6 boolean suppression per case; T7 chained cases + outputs.
UI (2026-09-26, Playwright): picking a Close case adds the first row, laid out by name with case 1's
values (Boolean shown as the expression "true").

### Part naming option (2026-09-26, user's RD 20FOU 28 doc)
Close case "Name parts with the case name" (default ON = old behaviour; correction 25 migrates the default
into saved features). OFF leaves names to the repeated features -- e.g. a rename feature using #caseName,
which the suffix rule would otherwise overwrite (it produced sw_inside_surf_3d_sw_inside_2d). Outputs are
unaffected (queries, not names); they are named #<case>_<output>, so renaming a CASE renames its outputs.
Test T8 (on -> Stud_B, off -> default name kept).

### Update from Define case button (2026-09-26)
Editing logic does not run when a dialog is only opened, so after adding/removing/retyping a Define case
input or value the Case pattern needs a change to re-lay its rows: the "Update from Define case" button
(isButton, correction 27). Tested live: new input inserted FIRST in T7's Define case -> button -> saved rows
have in1Key extraFace (empty), in2Key face7 with B's selection kept (bind by name works). Quirk: the open
dialog does not repaint read-only label strings changed by editing logic in existing array rows -- the
stored labels are right and show on reopening.

### Outside references in the frame (2026-09-26, test T9)
Offset+ in the RD doc flipped sides for some cases: its side reference was a click on the MRS mate connector
(outside the repeated features); inside the frame that query is "out of pattern scope" and read as empty,
so Offset+ fell back to surface normals. Fixed: inputs are bound as entities resolved outside the frame;
Reference_Side errors when a picked reference resolves to nothing (correction 50). Rule for users: route
every outside reference through a Define case input. T9a (click inside Offset+) documents the old failure
(pinned to Reference_Side V-1458547, still flips), T9b (reference via input) passes.

### Shared references; no value fallback (2026-09-26)
Define case "Shared references" (name + selection): geometry from before the Define case used by every case.
Case pattern resolves them once OUTSIDE the frame and binds them before each case (a query variable defined
earlier is refused inside the frame just like a click). No per-case slot. Test T10 (Offset+ side reference as a
shared reference, two differently oriented cases). The regen fallback "missing value -> case 1's value + note" is
gone: a missing value fails that case ("#x has no value (click Update...)"), test T11. New rows are still
PRE-FILLED with case 1's values by the editing logic (visible, editable). Plain value variables from before the
Define case need no section: only geometry queries are refused inside the frame.
Explainer rewritten for v2 (docs/explainers/case_pattern/case_pattern_explained.md).

### Open
- Not yet exercised: mate-connector outputs, sketches in the body, evaluate-on-use / track outputs.
- Feature names containing "#name" display as "?" in the tree (Onshape treats # in names specially).
- docs/explainers/case_pattern/case_pattern_explained.md still describes v1.
- The toolbar/search also offers "Case pattern" from Case_Pattern V1 (older version) -- pick the
  workspace one, or version the document and update the toolbar.

### Part naming = output naming (2026-09-27, user decision)
Replaced the copy-and-swap scheme (case 1's names cached by Close case editing logic, case name swapped as a
suffix / prefix -- stale when parts were renamed, produced "Surface 28_bf_inside_2d", a second naming rule) with
"Name parts after outputs" (param id nameParts kept): the bodies an output points to are named exactly like the
output's variable, `<case>_<output>` (`_2`, `_3` for further bodies), case 1 included (named at the Close case).
No editing logic on Close case any more; separator and templateNames parameters removed. Naming pattern agreed:
case names carry the variant (`bf_inside_3d`), output names the kind (`bump`, `surf`), published and part names
read `<component>_<qualifier>_<2d|3d>_<kind>`; locals `_role`; no `-`, no leading `#` in text fields.


### Outside references clicked in the body -- cause found (2026-09-29, correction 60)
Case pattern calls the Close case with NO frame, and that call drops every reference the listed features clicked on
geometry from before the body (they resolve empty; not a platform limit -- native Feature pattern keeps them). A frame
around the Close case call as well as the Close case's own frame (nested) keeps them, but then the step-out-of-frame
retry for edits of outside geometry fails (the outer frame cannot be left). Spike rows O1-O4, N1-N6 in the
"Case pattern outside-ref spikes" studio (throwaway; spike_outside.fs tab). Proposed: run each case nested first;
if a feature refuses with SELF_INTERSECTING, roll the attempt back and rerun the case the current way. Not built.
