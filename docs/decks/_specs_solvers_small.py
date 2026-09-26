"""Writes the Solvers and small-tools deck specs (Iterative Solve, Part Volume, Move Along Edge, Trim curve +,
Trim curve). Run from the repo root; then build each with docs/tooling/build_deck.js. See DECK_NOTES.md.
Figures: docs/explainers/solvers/img, docs/explainers/small_tools/img. Dialog shots: docs/decks/_shots_raw/st.json.
"""
import json

SIMG = "docs/explainers/solvers/img/"
TIMG = "docs/explainers/small_tools/img/"
STATUS = "Draft 2026-09-26"
SOLV = "solvers (3cf445f157b6a28aa1d35fcf; named 'Stray_Weight_Concept_1' in Onshape)"
SMALL = "smallTools (14cdaad24a8beab67af40fe4)"


def shot(slug):
    return "docs/decks/%s/shots/dialog.png" % slug


IS = {"slug": "iterative_solve", "title": "Iterative Solve",
      "tagline": "Find the value of one variable that makes a result -- a variable, a mass or a volume -- hit a target, by re-running a list of features; keep only the answer.",
      "document": "solvers", "icon": "icons/final/iterative_solve_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Setting one number so a result comes out right usually means a Pattern 'Reapply features' trick: slow (about 6 % closer per pass) and it keeps every pass. Iterative Solve treats the listed features as a function of the variable, solves for the target and keeps only the accepted trial.",
           "useWhen": ["A dimension must give a target mass or volume", "A variable must make another variable hit a value", "Find the first value that meets a condition"],
           "image": SIMG + "fig01_secant.png", "caption": "Stray_Weight: #flat_length for a target mass -- trial 2 lands on it."},
          {"type": "concept", "title": "Trials",
           "image": SIMG + "fig02_trials.png",
           "points": [{"head": "One trial", "body": "Set the variable, re-run every listed feature under its own id, measure."},
                      {"head": "Keep the answer", "body": "Rejected trials are deleted; the accepted one is kept, the originals deleted, the variable set."},
                      {"head": "No answer", "body": "An error rolls the whole feature back; the originals stay."}]},
          {"type": "concept", "title": "Methods",
           "image": SIMG + "fig01_secant.png",
           "points": [{"head": "From the current value", "body": "Current, then +5 %; secant steps; Brent once the target is bracketed."},
                      {"head": "Both bounds", "body": "A trial at each bound (they must bracket), then Brent."},
                      {"head": "First match", "body": "Even steps from lower to upper; stop at the first value meeting <, <=, > or >=."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("iterative_solve"),
           "params": [["Features to iterate", "The features to re-run (tree order)."],
                      ["Iteration variable", "The name, without #; must exist upstream."],
                      ["Variable type", "Length / Angle / Number."],
                      ["Lower bound", "Search range. Lower = upper by default: an error until you set them."],
                      ["Upper bound", "Upper end of the search range."],
                      ["Method", "Reach target / First value meeting condition."],
                      ["Start from", "Current value (+ First step %, 5) / Both bounds."],
                      ["Result", "Variable / Mass of created solids (+ density) / Volume of created solids; Store result as."],
                      ["Target from variables", "On: target and tolerance variables, re-read each trial. Off: typed target and tolerance."],
                      ["Max trials", "30."],
                      ["Debug", "Watch variables, feature trace, keep failed trial, run once at a value."]]},
          {"type": "outputs",
           "keys": [["#<iteration variable>", "The solution."],
                    ["<Store result as>", "Optional: the mass (g) or volume (mm^3) at the solution."],
                    ["(bodies)", "The accepted trial's bodies."]],
           "messages": [["Info", "#x = ... after N trials; result ...", "Normal."],
                        ["Error", "The target lies beyond the lower / upper bound", "Widen the bounds or change the target."],
                        ["Error", "The bounds do not bracket the target / No value met the condition", ""],
                        ["Error", "The result does not depend on the iteration variable", "Do the listed features read it?"]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Set the tolerances", "body": "Length tolerance defaults to 25 mm and angle tolerance to 0 (exact)."},
              {"kind": "tip", "head": "Probe first", "body": "Debug > Run once at a value builds one trial without searching."},
              {"kind": "limit", "head": "Downstream references", "body": "They bind to <solver>trialN<feature> and break if a later edit accepts a different trial."},
              {"kind": "limit", "head": "Cost", "body": "Every trial re-runs the whole list."}]},
          {"type": "reference", "rows": [["Document", SOLV], ["Tabs", "iterative_solve, solver_core"],
                                         ["Tests", "verified live 2026-09-22 (Stray_Weight, Design_Master derive); no test studio"],
                                         ["Explainer", "docs/explainers/solvers/solvers_explained.md"], ["Related", "Part Volume"]]}]}

PV = {"slug": "part_volume", "title": "Part Volume",
      "tagline": "Write one solid's volume to a variable, as a plain number in mm^3 -- typically the result Iterative Solve drives.",
      "document": "solvers", "icon": "icons/final/part_volume_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Iterative Solve and later features read variables. Part Volume turns one part's volume into one: evaluated precisely, stored as a unitless number of mm^3, and printed to the console.",
           "useWhen": ["Drive Iterative Solve by a part's volume", "Show a volume in a variable or table"],
           "image": SIMG + "fig01_secant.png", "caption": "Its value can be Iterative Solve's result variable."},
          {"type": "dialog", "title": "The dialog",
           "params": [["Part", "One solid."], ["Variable Name", "The name to set (not checked: an empty name is not caught)."]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Unitless", "body": "The value is a number of mm^3, not a volume with units."},
              {"kind": "limit", "head": "One part", "body": "Single solid only; no description or messages."}]},
          {"type": "reference", "rows": [["Document", SOLV], ["Tab", "Part_Vol"], ["Tests", "none"],
                                         ["Explainer", "docs/explainers/solvers/solvers_explained.md, section 2.2"], ["Related", "Iterative Solve"]]}]}

MAE = {"slug": "move_along_edge", "title": "Move Along Edge",
       "tagline": "Move or copy bodies, mate connectors, sketch edges or points along an edge, chain or wire -- by a distance along it, or to the point nearest a target.",
       "document": "smallTools", "icon": "icons/final/move_along_edge_icon.svg", "status": STATUS,
       "slides": [
           {"type": "why", "title": "What it does",
            "problem": "Native Transform cannot follow a curve by arc length, and Curve pattern spaces instances evenly. Move Along Edge moves (or copies) things a set distance along a path, or to where the path is nearest a target, turning them with the path if asked.",
            "useWhen": ["Place a part at a station along a curve", "Copy a detail to several named stations", "Put a point where a curve meets another"],
            "image": TIMG + "fig02_nearest_copies.png", "caption": "M15: moved nearest target 1; copies nearest target 2 and at 50 mm."},
           {"type": "concept", "title": "Orientation",
            "image": TIMG + "fig01_orientation.png",
            "points": [{"head": "Tangent only", "body": "Default: the entity turns with the path's tangent."},
                       {"head": "Transported", "body": "No twist: a rotation-minimising frame (M8 stays on top)."},
                       {"head": "Frenet", "body": "Follows the curvature normal: flips at inflections (M7 ends below), arbitrary on lines."}]},
           {"type": "dialogshot", "title": "The dialog", "screenshot": shot("move_along_edge"),
            "params": [["Entities to move", "Bodies, mate connectors, sketch edges, sketch vertices."],
                       ["Edges or wire to move along", "The path; positive = the first edge's own direction."],
                       ["Provide Reference Point", "+ vertex: use it instead of the centroid / connector origin."],
                       ["Move to", "Distance (signed, 25 mm; Flip direction or the arrow) / Nearest point to a target."],
                       ["Apply", "Move and rotate / Translate only / Rotate only."],
                       ["Orientation", "Tangent only / Transported (no twist) / Curve normal (Frenet)."],
                       ["Copy Bodies?", "Copy instead of move."],
                       ["Naming", "New name / Prefix / Suffix + Name text."],
                       ["Mate connectors as points", "Output points instead of moving connectors."],
                       ["Additional copies", "More copies from the same start, each by distance or target, each named."]]},
           {"type": "example", "title": "Example: nearest point and copies (M15)",
            "image": TIMG + "fig02_nearest_copies.png",
            "head": "Main move nearest (250, 60); copies nearest (320, -40) and at 50 mm",
            "body": "The cube lands at x = 250, copies at 320 and 50. Also tested: a closed loop wraps (M6), moving back past the start extends the path (M9), rotate only turns in place (M18)."},
           {"type": "outputs",
            "keys": [["output / inputs", "The moved entities (or copies) / the sources."],
                     ["initial_copy", "The first copy (Copy on)."],
                     ["copy_1 ... copy_n", "The additional copies."]],
            "messages": [["Info", "rotate only on points / mate connectors cannot be named", ""],
                         ["Warning", "source names missing", "Edit the feature once to refresh the names."]]},
           {"type": "tips", "items": [
               {"kind": "tip", "head": "Moves keep identity", "body": "A moved body is the same body: downstream references survive."},
               {"kind": "limit", "head": "Nearest ignores Flip", "body": "And names refresh only on edit."},
               {"kind": "limit", "head": "Frenet on lines", "body": "Undefined normal: use Transported."}]},
           {"type": "reference", "rows": [["Document", SMALL], ["Tab", "Move_Along_Edge"],
                                          ["Tests", "Move along edge tests: M1-M20 + M3b (all pass)"],
                                          ["Explainer", "docs/explainers/small_tools/small_tools_explained.md, section 1"],
                                          ["Related", "Trim curve +; Extract variables (copy_n keys)"]]}]}

TCP = {"slug": "trim_curve_plus", "title": "Trim curve +",
       "tagline": "Trim or split wires at exact arc-length positions: at points, signed distances from a point, even divisions, or inflections.",
       "document": "smallTools", "icon": "smallTools/trimCurvePlus_A.svg", "status": STATUS,
       "slides": [
           {"type": "why", "title": "What it does",
            "problem": "Native Trim curve trims up to one entity. Trim curve + reads each wire -- multi-edge too -- as one path and cuts it where you say, exactly, splitting into pieces or trimming away one side.",
            "useWhen": ["Split a profile at stations", "Cut a set distance from a point", "Divide evenly, or split at inflections"],
            "image": TIMG + "fig03_trim_curve_plus.png", "caption": "T1 at points, T9 by distances."},
           {"type": "dialogshot", "title": "The dialog", "screenshot": shot("trim_curve_plus"),
            "params": [["Operation", "Split (default) / Trim."],
                       ["Curves to adjust", "Wire bodies (not sketch)."],
                       ["Cut location", "At points / Up to entity / Distance from point / Even division / At inflections."],
                       ["Cut points", "(At points) off-curve picks are projected (dashed preview)."],
                       ["Return single wire", "(Split) one wire split in place; off = separate wires, source deleted."],
                       ["Keep opposite side", "(Trim) keep the other piece (default: end side)."],
                       ["Debug", "Print inflection solve / cuts."]]},
           {"type": "dialog", "title": "Cut locations",
            "params": [["At points", "Cut points (vertices or mate connectors)."],
                       ["Up to entity", "Where the curve meets it (nearest point per edge)."],
                       ["Distance from point", "From point + signed Distance (Opposite direction); Split: Additional distances, Also split at reference point."],
                       ["Even division", "Between two points (segments; also cuts at both) / Every distance from point (spacing, count)."],
                       ["At inflections", "Click the red dots, or the one nearest each end (one planar, single-edge curve)."]]},
           {"type": "example", "title": "Examples: T1 and T9",
            "image": TIMG + "fig03_trim_curve_plus.png",
            "head": "T1: at 2 points (one 6 mm off) -> 100 / 357.08 / 100. T9: 50, extra 150 and -80 -> 20 / 130 / 100 / 307.08",
            "body": "Also tested: split in place (T2, 5 edges), trim (T3), a mate connector point (T7), and 600 mm past the end -> error (T8)."},
           {"type": "outputs",
            "keys": [["output / inputs", "The result / the source."],
                     ["cut_1 ... cut_n, cutVertices", "The vertices at the cuts (2 per cut when split into separate wires)."],
                     ["piece_1 ... piece_m", "The pieces (bodies, or edges when split in place)."],
                     ["cutCount", "Variable: the number of cuts."]],
            "messages": [["Error", "runs past the end of the curve", "Distance mode."],
                         ["Error", "no valid cut / not planar / no inflections", ""]]},
           {"type": "tips", "items": [
               {"kind": "limit", "head": "Separate pieces", "body": "The source wire is deleted: downstream references to it break. Use Return single wire to keep it."},
               {"kind": "limit", "head": "Trim", "body": "Uses only the first cut."},
               {"kind": "limit", "head": "Directions", "body": "Distance: positive = path direction. Spacing: away from the midpoint by default."}]},
           {"type": "reference", "rows": [["Document", SMALL], ["Tabs", "betterCurveTrim, curveTrimCore"],
                                          ["Tests", "Trim curve + tests: T1-T9 (all pass)"],
                                          ["Explainer", "docs/explainers/small_tools/small_tools_explained.md, section 2"],
                                          ["Related", "Move Along Edge; native Trim curve"]]}]}

TC = {"slug": "trim_curve", "title": "Trim curve (reference copy)",
      "tagline": "A verbatim copy of Onshape's own Trim curve (Move curve boundary), kept to compare with Trim curve +. Use the native feature.",
      "document": "smallTools", "icon": "docs/decks/_icons/trim_curve_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it is",
           "problem": "OS_Trim.fs is PTC's MIT-licensed source of native Trim curve with only the version bumped. It exists so Trim curve + can be compared with it; nothing imports it.",
           "useWhen": ["Never in models: use the native Trim curve", "Reading how the native feature works"],
           "image": TIMG + "fig03_trim_curve_plus.png", "caption": "What Trim curve + adds: cuts at exact positions."},
          {"type": "dialog", "title": "The dialog",
           "params": [["Move curve boundary type", "Trim / Extend."],
                      ["Curves to adjust", "Non-sketch wires."],
                      ["End condition", "(Extend) Blind + distance, or Up to entity; then extension shape."],
                      ["Up to entity", "(Trim) where to trim."],
                      ["Help point", "Picks the end / side."],
                      ["Opposite direction", "Flip."]]},
          {"type": "reference", "rows": [["Document", SMALL], ["Tab", "OS_Trim"], ["Tests", "none"],
                                         ["Explainer", "docs/explainers/small_tools/small_tools_explained.md, section 3"],
                                         ["Related", "Trim curve +"]]}]}

for spec in (IS, PV, MAE, TCP, TC):
    json.dump(spec, open("docs/decks/%s/spec.json" % spec["slug"], "w"), indent=2)
    print("wrote", spec["slug"])
