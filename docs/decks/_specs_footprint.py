"""Writes the footprint deck specs (Integrate footprint, Analyze footprint, Scale Footprint, Arc fit, Generate
Footprint Points). Run from the repo root; then build each with docs/tooling/build_deck.js. See DECK_NOTES.md.
Figures: docs/explainers/footprint/img (img/src/figs.py). Dialog shots: docs/decks/_shots_raw/fp.json.
"""
import json

IMG = "docs/explainers/footprint/img/"
STATUS = "Draft 2026-09-26"
EXPL = "docs/explainers/footprint/footprint_explained.md, section "
TESTS = "Footprint tests studio (build_ / check_footprint_tests.py): "
RELATED = "Integrate footprint, Analyze footprint, Scale Footprint, Arc fit, Generate Footprint Points"


def ref(tab, tests, sec):
    return [["Document", "footprint (52724f3a857fa52d3ecceb77)"], ["Tab", tab], ["Tests", TESTS + tests],
            ["Explainer", EXPL + sec], ["Related", RELATED]]


def shot(slug):
    return "docs/decks/%s/shots/dialog.png" % slug


VOCAB = {"type": "concept", "title": "The words",
         "image": IMG + "fig01_vocabulary.png",
         "points": [{"head": "Contacts and RSL", "body": "FCP / ACP where the ski meets the snow; RSL between them, MRS its middle."},
                    {"head": "Waist, widest, inflection", "body": "Narrowest point; widest point of each half; where the curvature changes sign."},
                    {"head": "Taper", "body": "Angle of the line joining the widest points; positive when the tip is wider."}]}

IF = {"slug": "integrate_footprint", "title": "Integrate footprint",
      "tagline": "Design a sidecut from its radius: draw the radius along the ski as sketch lines, get the outline -- exact arcs where the radius is constant, placed so the waist lands where you ask.",
      "document": "footprint", "icon": "icons/final/integrate_footprint_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A sidecut is designed by its radii, but drawn as an outline. Integrate footprint turns a radius profile into the outline: curvature integrated twice, with the waist location (or taper) and width you set. Constant-radius stretches come out as true arcs that report their radius.",
           "useWhen": ["Start a sidecut from radius targets", "Try a reverse-radius flank or a transition", "Need arcs that show 'R 14000' when clicked"],
           "image": IMG + "fig02_integrate.png", "caption": "IF3: profile lines at R14 m and R3 m (reverse) -> three exact, tangent arcs."},
          VOCAB,
          {"type": "concept", "title": "Exact where it matters",
           "image": IMG + "fig02_integrate.png",
           "points": [{"head": "Constant radius -> arc", "body": "Closed form sin(phi) = sin(phi0) + (x - x0) / R: an exact circle, emitted as a sketch arc."},
                      {"head": "Changing radius -> spline", "body": "A sloped profile line is integrated numerically and joins the arcs tangentially."},
                      {"head": "Placed by a solver", "body": "Tangent angle solved so the waist lands at the waist location (or the taper matches), then shifted to the waist width."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("integrate_footprint"),
           "params": [["Spline method", "APPROX / FIT: how transition splines are made."],
                      ["Build Mode", "One curve per same-sign region, or per profile edge."],
                      ["Unify curves", "One curve for all (ignored when there are arcs)."],
                      ["Strict", "Force arcs / lines -- rational curves that do NOT report a radius."],
                      ["Waist/Taper Angle Calculations", "WAIST: place the narrowest point; TAPER: set the overall taper angle."],
                      ["Waist location", "World X of the waist (65 mm)."],
                      ["Waist width", "Full width at the waist (95 mm)."],
                      ["Radius Profile(s)", "The profile sketch lines: 10 mm of y = 1 m of radius; below y = 0 = reverse."],
                      ["Contact points", "FCP sets which end is the forebody (taper sign). ACP is not used yet."],
                      ["Integration definition", "Y-axis scaling: 10 / 20 / 50 / 100 mm per metre."],
                      ["Spline approximation parameters", "Degree, maximum control points, tolerance."],
                      ["Debug & details", "Samples per profile edge; group output into one wire."],
                      ["Recalculate?", "No effect."]]},
          {"type": "example", "title": "Example: IF3",
           "image": IMG + "fig02_integrate.png",
           "head": "Profile: R14 m core line (y = 140), R3 m reverse flanks (y = -30); waist width 200 at x = 0",
           "body": "Three exact arcs R3000 / R14000 / R3000, tangent at the joints; widest points exactly at x = +-667.857. Also tested: IF1 one line -> one arc R14000; IF2 25 / 14 / 25 m -> three arcs; IF5 placed by taper 0.25 deg; IF6 sloped transitions -> exact core + 2 tangent splines."},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["(wire)", "One +Y side wire: sketch arcs for constant radius, splines for transitions."]],
           "messages": [["(none)", "No messages; a solver that does not converge stops on its last try silently.", ""]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Horizontal lines for arcs", "body": "Only a constant-radius (horizontal) profile line gives an exact arc."},
              {"kind": "limit", "head": "Profile edges", "body": "An edge may not cross y = 0: split concave and reverse regions."},
              {"kind": "limit", "head": "Strict", "body": "Its arcs are rational splines and report no radius; leave it off for radius callouts."}]},
          {"type": "reference", "rows": ref("integrateFootprint (+ fpt_geometry, arcFit)", "IF1-IF8", "1.2, 2.1")}]}

AF = {"slug": "analyze_footprint", "title": "Analyze footprint",
      "tagline": "Measure any sidecut: dimensions, waist, taper, tip and tail length, average and natural radius, widest and inflection points -- and publish them as variables.",
      "document": "footprint", "icon": "icons/final/analyze_footprint_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Comparing sidecuts needs the same numbers, measured the same way: tip-waist-tail, taper, radius. Analyze footprint reads them off any outline -- designed, scaled or imported -- and publishes them for later features.",
           "useWhen": ["Check a sidecut against its targets", "Compare skis in a family", "Drive later features from measured values"],
           "image": IMG + "fig01_vocabulary.png", "caption": "The points it finds: contacts, widest, inflections, waist."},
          {"type": "concept", "title": "Average vs natural radius",
           "image": IMG + "fig03_average_natural.png",
           "points": [{"head": "Average", "body": "Mean of R at 200 X stations between the inflections -- independent of how the edges are split."},
                      {"head": "Natural", "body": "One circle through inflection - waist - inflection (or widest - waist - widest)."},
                      {"head": "They differ", "body": "FX2: 16.97 m average vs 14.47 m natural. Both are right; they answer different questions."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("analyze_footprint"),
           "params": [["Footprint edges", "The sidecut (+Y side)."],
                      ["RSL line", "A straight edge FCP to ACP."],
                      ["FCP", "Plane, vertex or mate connector; the nearest point on the RSL line is used."],
                      ["ACP", "As FCP."],
                      ["Sketch key points", "Draw the waist, widest and inflection lines."],
                      ["Footprint Data", "Read-only: tip-waist-tail, waist width and X, taper, tip and tail length."],
                      ["Radius calculations", "Average, natural (widest), natural (inflection)."],
                      ["Widest & Inflection points", "Distances from each contact; inflection to widest."],
                      ["Recalculate", "Button: refresh the panel."]]},
          {"type": "examples", "title": "Examples (Footprint tests)",
           "cards": [{"image": IMG + "fig01_vocabulary.png", "head": "AF1: S14", "body": "Inflections +-550, average 14.00 m, natural (inflection) 14.00 m, natural (widest) 17.00 m, taper 0."},
                     {"image": IMG + "fig03_average_natural.png", "head": "AF2 / AF3: FX2 in 7 or 13 edges", "body": "Same result either way: average 16.97 m, natural 14.47 m."}]},
          {"type": "outputs",
           "keys": [["tipWidth, waistWidth, tailWidth", "Full widths at the widest points and the waist."],
                    ["waistX, tipLength, tailLength", "World X of the waist; contact to tip / tail end."],
                    ["taperAngle", "Widest to widest; positive when the tip is wider."],
                    ["sidecutRadius", "The average radius."],
                    ["output", "The key-point sketch (when on)."]],
           "messages": [["Info", "Tip-waist-tail ... mm | average radius ... | natural radius (inflection) ...", "Every regeneration, always current."]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Panel goes stale", "body": "The dialog panel updates only when the feature is edited or Recalculate is pressed; the info line and variables are always current."},
              {"kind": "tip", "head": "Any orientation along X", "body": "Tip at +X and off-centre skis measure correctly (AF4, AF5); raw sketch arcs too (AF7)."}]},
          {"type": "reference", "rows": ref("analyzeFootprint (+ fpt_analyze, predicates)", "AF1-AF7", "1.3, 2.2")}]}

SF = {"slug": "scale_footprint", "title": "Scale Footprint",
      "tagline": "Derive a footprint for a new running-surface length from a reference: accordion in X, or keep the taper -- arcs stay tangent arcs that report their radius.",
      "document": "footprint", "icon": "icons/final/scale_footprint_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A ski family shares one sidecut at several lengths. Scale Footprint moves the tip and tail to the new contacts and stretches the sidecut between them -- keeping arcs as arcs and, if asked, the reference taper.",
           "useWhen": ["Grow or shrink a ski family by length", "Keep a taper angle across lengths", "Keep radius callouts on the scaled outline"],
           "image": IMG + "fig05_scale.png", "caption": "SF1 (accordion) and SF2 (keep taper, pin ACP): reference grey, arcs blue, splines orange."},
          {"type": "concept", "title": "Arcs stay arcs",
           "image": IMG + "fig05_scale.png",
           "points": [{"head": "Stretch = ellipse", "body": "A circle stretched in X is no longer a circle."},
                      {"head": "Refit as a chain", "body": "Arcs through the moved joints, each starting tangent to the last -- tangent by construction (R14 -> R16.94 m at x 1.1)."},
                      {"head": "Splines exact", "body": "A spline sidecut is stretched by its control points; knots kept (within 0.001 mm)."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("scale_footprint"),
           "params": [["Reference footprint edges", "The whole reference, +Y and -Y."],
                      ["Reference RSL line", "FCP = its lower-X end."],
                      ["New RSL line", "The new contacts."],
                      ["Symmetry mode", "Symmetric (mirror +Y) / Asymmetric (each side on its own)."],
                      ["Scale mode", "Accordion (X only) / Keep taper angle."],
                      ["Pin location", "Keep taper: hold the ACP width or the MRS width."],
                      ["Spline options", "Degree, Strict arcs, tolerance, maximum control points."],
                      ["Specify target width", "Target waist width (full)."],
                      ["-Y Scale mode", "Asymmetric: the -Y side's own settings."],
                      ["Enforce tangency at tip (FCP)", "Move one tip control point so the tip joins tangentially."],
                      ["Enforce tangency at tail (ACP)", "Same at the tail."],
                      ["Keep reference curves", "Keep the input."],
                      ["Debug", "Colour curves by type; skip wire merge; print the arc chain."]]},
          {"type": "example", "title": "Example: accordion (SF1)",
           "image": IMG + "fig05a_accordion.png",
           "head": "S14 to RSL 1650 (x 1.1)",
           "body": "Contacts at +-825, y 61.999; tip end -924.567 -> -999.567; waist (0, 50) kept; arcs refit R14 -> R16.94 m, R3 -> R3.63 m; -Y is the mirror."},
          {"type": "example", "title": "Example: keep taper, pin ACP (SF2)",
           "image": IMG + "fig05b_keep_taper.png",
           "head": "Asymmetric sidecut (R2 / R4 m flanks) to RSL 1650",
           "body": "Taper -0.0662 deg kept (accordion alone would give -0.0602); ACP width 63.666 kept. Also tested: SF5, a spline sidecut stretched exactly (knots kept, within 0.001 mm)."},
          {"type": "outputs",
           "keys": [["positiveSide / negativeSide", "The scaled +Y and -Y wires (open; never closed into a loop)."],
                    ["lengthScale", "New RSL / reference RSL."]],
           "messages": [["Error", "Asymmetric with no -Y data", "Select both sides of the reference."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Pin choice", "body": "Pin ACP keeps the tail width; pin MRS keeps the mid-stance width."},
              {"kind": "limit", "head": "World axes", "body": "Works in world XY with the FCP at the lower-X end of each RSL line."},
              {"kind": "limit", "head": "No Scale radius", "body": "The retarget-radius mode was removed 2026-09-25 (it missed its target)."}]},
          {"type": "reference", "rows": ref("scaleFootprint (+ fpt_analyze, arcFit)", "SF1, SF2, SF5", "2.3")}]}

AR = {"slug": "arc_fit", "title": "Arc fit",
      "tagline": "Turn freeform coplanar edges into a chain of lines and circular arcs within a position tolerance -- for CAM and for radius callouts.",
      "document": "footprint", "icon": "icons/final/arc_fit_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Machines and drawings want lines and arcs; design curves are splines. Arc fit replaces each edge with the fewest lines and arcs that stay within tolerance, keeping edges that already are arcs or lines.",
           "useWhen": ["A spline sidecut needs radius callouts", "CAM needs line/arc geometry", "Check how 'arc-like' a curve is"],
           "image": IMG + "fig04_arc_fit.png", "caption": "AR3: a spline S-curve -> 10 arcs within 0.008 mm."},
          {"type": "concept", "title": "How it fits",
           "image": IMG + "fig04_arc_fit.png",
           "points": [{"head": "Keep what is exact", "body": "An edge that already is a circle or line passes through whole."},
                      {"head": "Biarcs", "body": "Each segment: a line, a biarc (two tangent arcs matching the end tangents) or a 3-point arc."},
                      {"head": "Split, then merge", "body": "Split at the worst point until in tolerance; merge neighbours while it stays in tolerance."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("arc_fit"),
           "params": [["Edges to fit", "Coplanar edges, selected in chain order."],
                      ["Position tolerance", "Largest deviation (0.01 mm); also the join tolerance."],
                      ["Plane tolerance", "Allowed out-of-plane error (0.01 mm)."],
                      ["Minimum segment length", "1 mm."],
                      ["Tangency tol", "No effect today."],
                      ["Validation samples per arc", "Where the deviation is checked (16)."],
                      ["Max subdivision depth", "8."],
                      ["Output type", "Curves / Sketch / Curves and sketch."],
                      ["Debug", "Console diagnostics."]]},
          {"type": "example", "title": "Example: AR3",
           "image": IMG + "fig04_arc_fit.png",
           "head": "A spline S-curve (R14 core into an R3 reverse flank), tolerance 0.01 mm",
           "body": "10 arcs, within 0.008 mm both ways, one wire. Also tested: AR1 five exact arcs pass through unchanged; AR2 a spline on an R14 arc -> one arc R14000; AR4 2 mm knot spans with a 10 mm minimum -> no gaps."},
          {"type": "outputs",
           "keys": [["output", "The arc-fit wire (empty when Output type is Sketch)."]],
           "messages": [["Error", "edges not coplanar (deviation stated)", "Raise Plane tolerance or fix the input."]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Order matters", "body": "Select the edges in chain order; they are not re-ordered."},
              {"kind": "limit", "head": "Across edges", "body": "Arcs are tangent within an input edge, not across input edges."},
              {"kind": "limit", "head": "Tight tolerances", "body": "Very small tolerances drive every segment to maximum depth: slow."}]},
          {"type": "reference", "rows": ref("arcFit", "AR1-AR4", "2.4")}]}

GP = {"slug": "generate_footprint_points", "title": "Generate Footprint Points",
      "tagline": "Export the sidecut as tip, RSL and tail point tables at equal arc-length spacing, each relative to its own origin -- for CNC, specs and spreadsheets.",
      "document": "footprint", "icon": "icons/final/footprint_points_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Shops and spec sheets want numbers, not curves. Generate Footprint Points splits the +Y outline at the contacts and samples each region evenly, in a table you can copy out.",
           "useWhen": ["Send a sidecut to CNC or a supplier", "Fill a spec sheet", "Compare outlines point by point"],
           "image": IMG + "fig06_points.png", "caption": "GP1: 23 tip, 80 RSL and 23 tail points on S14."},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("generate_footprint_points"),
           "params": [["Geometry Selection Type", "FACE (the footprint face) / EDGES (edges or wires)."],
                      ["Footprint Edges", "(EDGES) the outline."],
                      ["RSL Line", "FCP to ACP."],
                      ["Tip toward +X", "Off: the tip is the lower-X end; on: the higher-X end."],
                      ["Include Points", "Not used."],
                      ["Table Formatting", "Units, decimal places ('sig figs'), units in cells, point counts 23 / 80 / 23, flips."],
                      ["Additional options", "Keep the region curves / FCP-ACP planes; make sketches of the points."]]},
          {"type": "example", "title": "Example: GP1",
           "image": IMG + "fig06_points.png",
           "head": "S14, RSL -750 ... 750",
           "body": "Tip: 23 points x -924.567 ... -750 relative to the FCP; RSL: 80 points relative to the MRS; tail: 23 points relative to the ACP. All on the curve, spacing equal to 0.2 %."},
          {"type": "outputs",
           "keys": [["tipCurve, rslCurve, tailCurve", "The region curves (empty unless Retain curves is on)."],
                    ["Footprint Points table", "Reads tipEdgePoints / rslEdgePoints / tailEdgePoints / tableFormat stored on the origin (keep these stable)."]],
           "messages": [["(none)", "", ""]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Tip at +X", "body": "Turn on Tip toward +X; tip and tail swap with their origins (GP2)."},
              {"kind": "limit", "head": "Last one wins", "body": "The stored point lists are shared: the last Generate Footprint Points in the tree feeds the table."}]},
          {"type": "reference", "rows": ref("getFootprintPoints", "GP1-GP3", "2.5")}]}

for spec in (IF, AF, SF, AR, GP):
    json.dump(spec, open("docs/decks/%s/spec.json" % spec["slug"], "w"), indent=2)
    print("wrote", spec["slug"])
