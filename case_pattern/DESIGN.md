# Case Pattern -- design note

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

## 3. Features

### 3.1 Case template
| Parameter | Type | Notes |
|---|---|---|
| Case name | string | name of case 1, e.g. "Left" |
| Inputs | array of { Name : string, Query : Query } | any entity type incl. mate connectors; a name may already exist (redefined) |

Behaviour:
- Normal run: `setQueryVariable(name, query)` for each row. Publishes its signature (names, case
  name) where Case pattern can read it, keyed by the template's feature id (the FeatureList
  map is keyed by Id, so Case pattern can find it).
- When re-run inside Case pattern (`isInFeaturePattern` + a pending-bindings variable set by
  Case pattern): binds the CURRENT CASE's queries instead of its own. So the template is the
  single binding point and is itself part of the replayed list.
- Later (v1.5): optional proxy ("bridge") geometry per input -- mate connector / extracted
  face or edges -- created inside the replay so sketches can constrain to it (section 5).

### 3.2 Case pattern
| Parameter | Type | Notes |
|---|---|---|
| Features to repeat | FeatureList | must contain exactly one Case template; body = the rest |
| Cases | array of { Case name : string, Input 1..N : Query } | slot k binds the template's k-th name |
| Keep outputs | booleans | Parts, Sheets, Wires, Mate connectors, Planes (construction) |
| Separator | string | default "_" |

Case-row UI: FeatureScript arrays cannot nest and labels cannot be dynamic, so each row has a
fixed number of query slots mapped by ORDER to the template names. Editing logic reads the
template signature (the editing-logic context sits at the feature's position) to set a hidden
input count (hide unused slots) and to fill the row label / a read-only "Inputs: a, b, c" hint.

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
