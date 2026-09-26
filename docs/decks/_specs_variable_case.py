"""Writes the Extract variables, Case template and Case pattern deck specs. Run from the repo root.
See docs/decks/DECK_NOTES.md.
"""
import json

VT = "docs/explainers/variable_tools/img/"
CP = "docs/explainers/case_pattern/img/"

EV = {"slug": "extract_variables", "title": "Extract variables",
      "tagline": "One feature per block of the tree that turns what its features produced into named variables and query variables -- regions, cuts, chain ends, the largest faces -- instead of a query variable per name.",
      "document": "Variable_tools", "icon": "icons/final/extract_variables_icon.svg", "status": "Draft 2026-09-25",
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A variable-driven model needs dozens of named selections so later features keep working when the model changes. Native query variables give one node per name, picked by hand, and cannot express a region, a chain end or 'the two largest faces'. Extract variables publishes them all from one node, from what the source features published.",
           "useWhen": ["A block of features feeds many later selections", "A selection must be computed (region, chain end, largest N)", "Names must survive changes upstream"],
           "image": "docs/decks/extract_variables/shots/hero.png", "caption": "Examples studio: Show highlights what the entries publish; 'Published 6 name(s).'"},
          {"type": "concept", "title": "Producers and the consumer",
           "image": VT + "fig01_producers_consumer.png",
           "points": [{"head": "Producers", "body": "Our features publish keys (output, regions, cuts, ends ...) in a hidden map; built-ins still offer output and modified* keys."},
                      {"head": "Addresses", "body": "A key offered by one source is its bare name; by several, key@1, key@2 ... (Print keys lists them)."},
                      {"head": "Always present", "body": "A producer's keys exist even when empty, so an entry never breaks after an edit."}]},
          {"type": "concept", "title": "Hold, Track, Evaluate on use",
           "image": VT + "fig02_hold_track_evaluate.png",
           "points": [{"head": "Hold (default)", "body": "What was there, followed through edits that keep it -- verified: follows a 10 mm Move boundary extend."},
                      {"head": "Track", "body": "Also what later splits and rebuilds make of it."},
                      {"head": "Evaluate on use", "body": "Re-resolved wherever used: the most live, and the easiest to get wrong."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": "docs/decks/extract_variables/shots/dialog.png",
           "params": [["Sources", "The features to read (sorted by tree order)."],
                      ["Prefix", "Joined to every name with _ (box -> #box_body)."],
                      ["Add keys from sources", "Button: one entry per address not yet listed."],
                      ["Entries", "Per name: Type, Source key, Published name, Show, Track / Evaluate on use; Filtered adds entity type, body type, name, largest N."],
                      ["Print keys", "List every address with its description."],
                      ["Manifest", "Optional: one map variable listing everything published."]],
           "note": "Entry types: Source key, Filtered, Closest to point, Shared edges, Chain end, Edges between points, Bridging curve input, Region."},
          {"type": "example", "title": "Example: box bands",
           "image": "docs/decks/extract_variables/shots/hero.png",
           "head": "Sources: a Box and a split of its faces into bands; prefix 'box'",
           "body": "Entries publish the body (output@1), the two largest faces (Filtered, keep 2), the edges at a corner (Closest to point), shared edges and two regions -- 6 names, plus a manifest. With Show on, the published body and cuts are highlighted."},
          {"type": "outputs",
           "keys": [["#<prefix>_<name>", "A value entry: an ordinary variable."],
                    ["<prefix>_<name> (query)", "A query entry: a native query variable, in the dropdowns of later features."],
                    ["manifest (optional)", "One map variable listing everything published."]],
           "messages": [["Info", "Published N name(s).", "Empty names are published too, and listed."],
                        ["Info", "Nothing published yet: press Add keys...", ""],
                        ["Warning", "Entry k (key): reason.", "Not published: unknown or ambiguous key (choices listed), a value where geometry was expected, a missing point ..."],
                        ["Error", "a name used twice", "Two entries publish the same name."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Press Add keys, then prune", "body": "It lists everything the sources offer; delete what you do not need and rename the rest."},
              {"kind": "tip", "head": "Prefer Hold", "body": "Use Track for things later split; Evaluate on use only when you know why."},
              {"kind": "limit", "head": "Adding a source", "body": "Every source offers output: a second source makes the bare 'output' ambiguous -- use output@1."}]},
          {"type": "reference", "rows": [
              ["Document", "Variable_tools (a47f90bfa6b17a59e20cebd0), V2"],
              ["Tabs", "extract_variables, extract_variables_utils, extract_outputs (the producer library, pinned by every producer)"],
              ["Tests", "Tracking tests studio (4/4), variable_tools_tests (Region, 6/6)"],
              ["Examples", "Examples studio: Extract variables: box bands"],
              ["Explainer", "docs/explainers/variable_tools/variable_tools_explained.md"],
              ["Related", "Every producer: Split+, Mutual Trim+, Offset+, Thicken+, Orient to reference, Clean wire, Map / Merge curve, ..."]]}]}

CT = {"slug": "case_template", "title": "Case template",
      "tagline": "Name the inputs a chain of features is built on -- selections and values -- for case 1, and list the further cases a Case pattern will re-run the chain for.",
      "document": "Case_Pattern", "icon": "icons/final/case_template_icon.svg", "status": "Draft 2026-09-25",
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "To build the same detail on several places (a boss on each of three blocks, each its own height), build it once against names -- #top, #rim, #bossH -- and let a Case pattern re-run it. The Case template defines those names for case 1 and holds the other cases' selections and values.",
           "useWhen": ["The same feature chain applies to several places", "Each place has its own selections and values", "A transform pattern cannot do it (the places differ)"],
           "image": CP + "fig01_case_table.png", "caption": "The template's case table and the chain built on case A."},
          {"type": "dialogshot", "title": "The dialog", "screenshot": "docs/decks/case_template/shots/dialog.png",
           "params": [["Case 1 name", "The template's own case (default A); its suffix is swapped for each case's name."],
                      ["Inputs", "Up to 8: a query-variable name and case 1's selection."],
                      ["Case values", "Up to 4: a name, a type (Length / Angle / Area / Volume / Number / Text), case 1's value."],
                      ["Input slots", "Read-only: the inputs as the case rows show them."],
                      ["Further cases", "One row per case: its name, a selection per input, a value per value."],
                      ["Debug", "Print bindings."]]},
          {"type": "outputs",
           "keys": [["the input names", "Query variables bound to case 1's selections."],
                    ["the value names", "#variables with case 1's values."],
                    ["(hidden) case signature", "What the Case pattern reads: every case's selections and values."]],
           "messages": [["Error", "Case 1 selection for #x selects nothing", ""],
                        ["Error", "Name #x is used twice / Case name \"B\" is used twice", ""],
                        ["Error", "Add at least one input.", "At most 8 inputs and 4 values."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Build the chain on the names", "body": "Every listed feature should use #top, #bossH ... -- never a clicked case-1 entity."},
              {"kind": "limit", "head": "Rows follow the template", "body": "After adding an input or changing a type, edit the template once so every row gets the slot."}]},
          {"type": "reference", "rows": [
              ["Document", "Case_Pattern (2099413dd91f34578b385892)"], ["Tab", "case_pattern (both features)"],
              ["Tests", "Case pattern tests studio: T1-T5"], ["Explainer", "docs/explainers/case_pattern/case_pattern_explained.md"],
              ["Related", "Case pattern"]]}]}

CPD = {"slug": "case_pattern", "title": "Case pattern",
       "tagline": "Re-run a chain of features for every case of a Case template -- each case with its own selections and values -- and name the new bodies by case.",
       "document": "Case_Pattern", "icon": "icons/final/case_pattern_icon.svg", "status": "Draft 2026-09-25",
       "slides": [
           {"type": "why", "title": "What it does",
            "problem": "'Apply to each' for the feature tree. Onshape's Pattern moves copies by a transform; Evan Reese's Query Pattern rebinds one seed. Case pattern rebinds up to 8 query variables and 4 values per case and re-runs the listed features for each case.",
            "useWhen": ["A detail repeats on places that differ in shape", "Each place needs its own dimensions", "The chain edits existing geometry (fillets, move face) as well as making new"],
            "image": "docs/decks/case_pattern/shots/t1_side.png", "caption": "T1: the boss chain on three blocks, each at its own height and radius."},
           {"type": "concept", "title": "The one rule",
            "image": CP + "fig02_references_in_list.png",
            "points": [{"head": "Inside the list", "body": "Geometry one listed feature made, used by another, must come through a Query variable 'created by' that feature."},
                       {"head": "Why", "body": "Each case runs in a pattern frame that remaps those references onto the case's copies; clicks stay on case 1."},
                       {"head": "Outside the list", "body": "References to existing geometry are fine: they are rebound through the template's names."}]},
           {"type": "dialogshot", "title": "The dialog", "screenshot": "docs/decks/case_pattern/shots/dialog.png",
            "params": [["Case template", "Exactly one Case template."],
                       ["Features to repeat", "The chain built on the template's names."],
                       ["Keep", "Which new bodies to keep per case (sketches off by default)."],
                       ["Name separator", "Between name and case (default _): Boss_A -> Boss_B."]]},
           {"type": "example", "title": "Example: bosses on three blocks (T1)",
            "image": "docs/decks/case_pattern/shots/t1_side.png",
            "head": "Chain: Boss (extrude #top by #bossH), rim fillet 2 mm, #bossEdges = edges created by Boss, fillet #bossEdges at #edgeR",
            "body": "Case A: 15 mm, R3 (the template). Case B: 25 mm, R3. Case C (pentagon): 8 mm, R2. New bodies are named Boss_<case>. (In the studio case B is currently named 'Foobar!'.)"},
           {"type": "outputs",
            "keys": [["output", "The bodies each case created (filtered by Keep), named <name>_<case>."]],
            "messages": [["Warning", "Not built: B: input 1 (#top) selects nothing; C: feature 2 failed (...)", "The other cases are still built."],
                         ["Error", "No case was built.", ""],
                         ["Info", "N bodies kept Onshape's default name", "Edit the Case pattern once to refresh case 1's names."]]},
           {"type": "tips", "items": [
               {"kind": "limit", "head": "Sketches", "body": "Re-solved per case, but references to input geometry stay on case 1. Build sketches on geometry derived from the inputs."},
               {"kind": "limit", "head": "Order matters", "body": "Cases run in order and see earlier results; a failed case cannot undo edits to existing geometry."},
               {"kind": "limit", "head": "Cost", "body": "(cases + 1) x the chain on every rebuild."},
               {"kind": "tip", "head": "Verified", "body": "Fillet and Move face on existing geometry, new extrudes, native query variables (T1-T5)."}]},
           {"type": "reference", "rows": [
               ["Document", "Case_Pattern (2099413dd91f34578b385892)"], ["Tab", "case_pattern (both features)"],
               ["Tests", "Case pattern tests studio: T1-T5 (T3, T4 must error)  -  devtools/onshape/build_case_pattern_tests.py"],
               ["Explainer", "docs/explainers/case_pattern/case_pattern_explained.md"], ["Related", "Case template; Query variable"]]}]}

for spec in (EV, CT, CPD):
    json.dump(spec, open("docs/decks/%s/spec.json" % spec["slug"], "w"), indent=2)
    print("wrote", spec["slug"])
