"""Writes the Case pattern family deck spec (v3: Define case, Case, Close case). Run from the repo root.
One family deck, not one per feature: the three features are one construct (a function's signature, its calls,
and the call site that runs them), live in one tab and are never used apart. See docs/decks/DECK_NOTES.md.
Replaces the v2 decks "Case template" (retired) and "Case pattern" (this slug, rewritten) on 2026-09-29.
"""
import json

CP = "docs/explainers/case_pattern/img/"
SH = "docs/decks/case_pattern/shots/"
ICON = "icons/final/"

SPEC = {
    "slug": "case_pattern", "title": "Case pattern",
    "tagline": "Build a chain of features once, against named inputs, then run it again per case -- each case with its own selections and values. Three features: Define case, Case, Close case.",
    "document": "Case_Pattern", "icon": ICON + "case_icon.svg", "status": "Draft 2026-09-29 (v3)",
    "slides": [
        {"type": "why", "title": "What it does",
         "problem": "'Apply to each' for the feature tree. Build a detail once for case A against names (#top, #bossH); each further case gives its own geometry and values, and the chain is run again for it. Nothing is moved by a transform, so the places may differ in shape.",
         "useWhen": ["The same feature chain applies to several places", "Each place has its own selections and dimensions",
                     "The chain edits existing geometry (fillet, move face) as well as making new"],
         "image": SH + "t1_iso.png", "caption": "Test T1: one chain (boss, rim fillet, boss-edge fillet) built for case A and run again for B and C."},
        {"type": "concept", "title": "Three features, like a function",
         "image": CP + "fig03_tree_v3.png",
         "points": [{"head": "Define case = the signature", "body": "Names the inputs and values, with case 1's selections. The features after it (the body) use those names."},
                    {"head": "Case = one call's arguments", "body": "One feature per further case: a selection per input, a value per value. Runs nothing itself."},
                    {"head": "Close case = runs the calls", "body": "Lists the body and the Cases, runs the body once per Case, publishes each case's outputs."}]},
        {"type": "concept", "title": "What the body may point at",
         "image": CP + "fig04_references_v3.png",
         "points": [{"head": "The one rule", "body": "Geometry the body itself made is referenced through a query variable ('created by' a body feature). A click stays on case A's copy."},
                    {"head": "New in v3", "body": "Geometry from before the body may be clicked directly (a side reference, an 'up to' face): every case uses the same one."},
                    {"head": "Values", "body": "Numbers and lengths are not geometry: plain variables from anywhere earlier work. Put one in Define case only if it changes per case."}]},
        {"type": "dialogshot", "title": "Define case", "icon": ICON + "define_case_icon.svg", "screenshot": SH + "dialog_define.png",
         "params": [["Case 1 name", "The case the tree itself builds (letters, digits, _; starts with a letter)."],
                    ["Inputs", "Up to 8 query variables that change per case: a name and case 1's selection."],
                    ["Shared references", "Optional: one selection for every case. Clicking the geometry in the body does the same; this is one place to change it."],
                    ["Values", "Up to 6 variables that change per case: name, type (Length, Angle, Area, Volume, Number, Integer, Text, Boolean), case 1's value."]],
         "note": "Also sets #caseName and #caseIndex (1 for case 1); every case updates them."},
        {"type": "dialogshot", "title": "Case", "icon": ICON + "case_icon.svg", "screenshot": SH + "dialog_case.png",
         "params": [["Define case", "The Define case this case belongs to. Picking it lays out the slots."],
                    ["Update from Define case", "Lays the slots out again after inputs or values changed in the Define case; selections and values are kept by name."],
                    ["Case name", "Unique among the Define case's cases; also the output prefix (#B_...)."],
                    ["Input slots", "A selection for each input (labelled #top, #rim ...)."],
                    ["Value slots", "A value for each value. New slots start with case 1's value (a copy, not a link)."]],
         "note": "Place each Case after the body and before the Close case that runs it. A missing selection or value is an error on the Case."},
        {"type": "dialogshot", "title": "Close case", "icon": ICON + "close_case_icon.svg", "screenshot": SH + "dialog_close.png",
         "positions": {"Keep": [0.018, 0.795]},
         "params": [["Define case", "The Define case this body belongs to."],
                    ["Features to repeat", "The body's features, in tree order."],
                    ["Cases", "The Case features to run. May be empty: then it only publishes case 1's outputs."],
                    ["Outputs", "Per output: a Name and the query variable the body sets (its name as plain text, no #). Evaluate on use / Track downstream changes as in Extract variables."],
                    ["Keep", "Which new bodies each case keeps: parts, surfaces, curves and points, mate connectors, planes on; sketches off."],
                    ["Name parts after outputs", "On: a part an output points to is named like the output's variable (B_rib)."],
                    ["Print bindings", "Print each case's selections and values to the notices."]]},
        {"type": "example", "title": "Example: bosses on three blocks (T1)", "icon": ICON + "close_case_icon.svg",
         "image": SH + "t1_iso.png",
         "head": "Define case A: #top, #rim; #bossH, #edgeR. Body: Boss (extrude #top by #bossH), Rim fillet on #rim, #bossEdges = edges created by Boss, fillet #bossEdges at #edgeR, #bossFaces = faces created by Boss.",
         "body": "Case B (25 mm, 3) and Case C (8 mm, 2); one Close case with output bossFaces. Result: three bosses at their own heights, every rim and boss edge filleted; parts A_bossFaces, B_bossFaces, C_bossFaces and the query variables #A_bossFaces, #B_bossFaces, #C_bossFaces."},
        {"type": "example", "title": "Example: a reference clicked from before the body (T14)", "icon": ICON + "close_case_icon.svg",
         "image": SH + "t14_iso.png",
         "head": "The boss is extruded 'up to' the top of an outside tower, clicked directly in the extrude; the body also fillets the case's block rim and the boss edges.",
         "body": "Case B's boss stops at the same tower top as case A's (z 20..60); B's rim and boss edges are filleted. All three kinds of reference in one case: clicked outside geometry, an edit of geometry from before the body, a query variable made in the body. In v2 the clicked face was lost."},
        {"type": "example", "title": "Example: closing again (T7)", "icon": ICON + "close_case_icon.svg",
         "image": SH + "t7_iso.png",
         "head": "A first Close case runs Case B (a stud on block B). Case D selects the top of B's stud; a second Close case, on the same Define case with the body picked again, runs it.",
         "body": "D's stud sits on B's (z 30..40); #A_stud, #B_stud and #D_stud each hold one body. Use this when a case must select geometry an earlier case made."},
        {"type": "outputs", "keysTitle": "Variables",
         "keys": [["#<case>_<output>", "One query variable per output per case, case 1 included (#A_bossFaces, #B_bossFaces ...). Fixed when the case closes unless Evaluate on use."],
                  ["inputs, values", "Define case binds them to case 1; after the Close case later features see case 1 again."],
                  ["#caseName, #caseIndex", "The running case's name and number (case 1 = 1); back to case 1 after the Close case."],
                  ["other body variables", "Keep the LAST case's value after the Close case."]],
         "messages": [["Warning", "Not built: B: repeated feature 3 failed (...)", "The other cases are still built; Onshape's own reason is quoted."],
                      ["Error", "No case was built.", "Every listed case failed."],
                      ["Error", "#h has no value: click Update from Define case.", "On a Case: the Define case changed (T11 shows the error)."],
                      ["Info", "#B_x is not case B's geometry: make #x a query variable 'created by' a repeated feature", "The output was not remade per case."],
                      ["Info", "Sketches are re-solved per case ...", "Constraints to the origin / default planes keep case 1's position."]]},
        {"type": "tips", "items": [
            {"kind": "tip", "head": "Few, short local names", "body": "Query variables made in the body are locals: name them by role (_body, _edges, _out) and reuse them in every template. A name can never be deleted from the list."},
            {"kind": "tip", "head": "Outputs only for what is used later", "body": "Each output makes one name per case. Type output names as plain text: a leading # turns the field into a reference."},
            {"kind": "tip", "head": "Switch features per case", "body": "A Boolean value suppresses a feature by expression (#pin), re-checked per case (T6). Numeric fields take expressions: #flag ? 3 mm : 0 mm."},
            {"kind": "limit", "head": "Order and undo", "body": "Cases run in order and see earlier results; a failed case cannot undo its edits to existing geometry (only its new bodies are removed)."},
            {"kind": "limit", "head": "Tested edits", "body": "Fillet, Move face, Move face+ on geometry from before the body; chamfer, shell, draft, booleans into existing parts are untested. No sheet metal or derived features."},
            {"kind": "limit", "head": "Cost and dialog", "body": "Every rebuild runs the body once per case. In a NEW Case the grey slot names stay blank until Update from Define case is pressed once."}]},
        {"type": "reference", "rows": [
            ["Document", "Case_Pattern (2099413dd91f34578b385892)"],
            ["Tab", "case_pattern (Define case, Case, Close case)"],
            ["Tests", "Case pattern tests studio: T1-T14 (T3, T11 must error), 53/53 checks pass 2026-09-29 -- devtools/onshape/build_case_pattern_tests.py, check_case_pattern_tests.py"],
            ["Explainer", "docs/explainers/case_pattern/case_pattern_explained.md"],
            ["Design", "case_pattern/DESIGN.md sections 11-12; corrections log 41, 50, 60"],
            ["From v2", "Case pattern features are gone: each case row becomes a Case feature listed in the Close case; outputs are typed as query-variable names."]]}]}

if __name__ == "__main__":
    json.dump(SPEC, open("docs/decks/case_pattern/spec.json", "w"), indent=2)
    print("wrote case_pattern")
