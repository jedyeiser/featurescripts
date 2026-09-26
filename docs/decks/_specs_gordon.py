"""Writes the Gordon-tools deck specs (Gordon Surface, Interior curves, Scaled Curve, Modify curve end, Pull surface,
Simplify surface, makeCurvesCompitable). Run from the repo root; then build each with docs/tooling/build_deck.js.
Figures: docs/explainers/gordon_surface/img (img/src/figs.py). Dialog shots: docs/decks/_shots_raw/gs.json.
"""
import json

IMG = "docs/explainers/gordon_surface/img/"
STATUS = "Draft 2026-09-26"
EXPL = "docs/explainers/gordon_surface/gordon_surface_explained.md, section "
DOC = "gordonSurface (d0950ed72a894fbe35c27d43)"
RELATED = "Gordon Surface, Interior curves, Scaled Curve, Modify curve end, Pull surface, Simplify surface"


def ref(tab, tests, sec, examples):
    return [["Document", DOC], ["Tab", tab], ["Tests", tests], ["Examples", examples],
            ["Explainer", EXPL + sec], ["Related", RELATED]]


def shot(slug):
    return "docs/decks/%s/shots/dialog.png" % slug


COMPAT = {"type": "concept", "title": "Compatible curves",
          "image": IMG + "fig05_compatibility.png",
          "points": [{"head": "Same degree", "body": "Every curve raised to the highest degree -- exactly."},
                     {"head": "Same knots", "body": "The union of all interior knots inserted into every curve -- exactly."},
                     {"head": "Then blend", "body": "Corresponding control points can be combined; the shapes do not change."}]}

GS = {"slug": "gordon_surface", "title": "Gordon Surface",
      "tagline": "One B-spline face through a whole network of crossing curves -- every U curve and every V curve, interior ones included.",
      "document": "gordonSurface", "icon": "docs/decks/_icons/gordon_surface_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A loft passes through one family of sections; a boundary surface only through its edges. Gordon Surface passes through a full U x V network, so interior curves shape the face exactly where you drew them.",
           "useWhen": ["The shape is defined by curves in both directions", "Interior curves must lie on the surface", "One smooth face instead of patches"],
           "image": IMG + "fig02_real_network.png", "caption": "Gordon Surface 1: one face through 5 U and 3 V curves."},
          {"type": "concept", "title": "S = S_u + S_v - T",
           "image": IMG + "fig01_gordon_construction.png",
           "points": [{"head": "Two skins", "body": "S_u through the U curves (misses V); S_v through the V curves (misses U)."},
                      {"head": "Minus the grid", "body": "T passes through the crossings only; along each curve it cancels the other skin."},
                      {"head": "Exact on both", "body": "The sum reproduces every curve; done control point by control point once compatible."}]},
          COMPAT,
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("gordon_surface"),
           "params": [["U-Curves", "Up to 10 edges, in section order (the pick order is used)."],
                      ["V-Curves", "The crossing family, in order."],
                      ["Debug and details", "Create S_u / S_v / tensor sheets, show curves (on), print, degree in u and v (3)."]]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["Gordon surf", "One sheet body."]],
           "messages": [["Error", "Need at least 2 curves", "Per family."], ["Error", "(dimension mismatch)", "The network is inconsistent."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Seed with Interior curves", "body": "Four boundaries -> interior curves that are guaranteed to cross."},
              {"kind": "limit", "head": "Curves must cross", "body": "Every U must cross every V, in order; a near miss is used silently (midpoint of the closest points)."},
              {"kind": "limit", "head": "Averaged crossings", "body": "Uneven crossing parameters make the fit approximate; arcs' weights are ignored."}]},
          {"type": "reference", "rows": ref("gordonSurface (+ gordonCurveCompat)", "none yet", "1.1, 1.2, 2.1", "Test Part Studio 1: Gordon Surface 1, 2")}]}

IC = {"slug": "interior_curves", "title": "Interior curves",
      "tagline": "From four boundary chains, make interior U and V curves that are guaranteed to cross -- a ready network for Gordon Surface.",
      "document": "gordonSurface", "icon": "docs/decks/_icons/interior_curves_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A Gordon network needs curves that actually cross. Interior curves fills a four-sided boundary with the rows and columns of a Coons patch, which share their grid points by construction.",
           "useWhen": ["Start a Gordon network from its boundary", "Need crossing interior curves to edit later"],
           "image": IMG + "fig06_interior_curves.png", "caption": "4 boundaries -> Coons grid -> 3 + 3 interior curves."},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("interior_curves"),
           "params": [["U0", "Boundary chain (edges joined)."], ["U1", "Opposite boundary."],
                      ["V0", "Crossing boundary."], ["V1", "Opposite crossing boundary."],
                      ["Interior U cure count", "Interior U curves (3; 1-15)."], ["Interior V cure count", "Interior V curves (3)."],
                      ["Debug & Details", "Print curve data (its output tolerance / degree are not used)."]]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["interiorU_i, interiorV_i", "The interior wires."]],
           "messages": [["Error", "Boundary curves don't form a closed loop at v0", ""]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Coarse", "body": "Each curve is fitted through count + 2 points (3 x 3 -> 5 points each)."},
              {"kind": "limit", "head": "Uniform in parameter", "body": "Not in length; prototype, untested."}]},
          {"type": "reference", "rows": ref("interiorCurves (symbol myFeature)", "none yet", "2.2", "Test Part Studio 1: Interior curves 1")}]}

SC = {"slug": "scaled_curve", "title": "Scaled Curve",
      "tagline": "A curve between two rails whose position across them changes along its length -- 30 % at the start, 70 % at the end, along a chosen transition.",
      "document": "gordonSurface", "icon": "icons/final/scaled_curve_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A mid-curve between two rails at a varying percentage is tedious by hand. Scaled Curve blends the rails point by point with a factor that travels from the initial to the final value.",
           "useWhen": ["A character line drifting between two edges", "A midline between two rails", "Seed curves for a network"],
           "image": IMG + "fig04_scaled_curve.png", "caption": "SC3 and the three transition shapes."},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("scaled_curve"),
           "params": [["Group 0", "First rail (edges joined); Flip? reverses it."],
                      ["Group 1", "Second rail."],
                      ["Initial curve scalefactor", "-0.5 = on Group 0, 0 = halfway, +0.5 = on Group 1."],
                      ["Final curve scalefactor", "The value at the end."],
                      ["Transition Type", "Linear / Sinusoidal / Logistic."],
                      ["Curve name", "Names the wire."],
                      ["Project onto surface?", "+ face: drop the result onto it."],
                      ["Debug & Details", "Show ends / curves; output samples, tolerance, degree; input sampling."]]},
          {"type": "example", "title": "Example: SC3",
           "image": IMG + "fig04_scaled_curve.png",
           "head": "Arcs R100 and R200, scale factor 0",
           "body": "An exact R150 arc (within 0.00062 mm) -- but only its first radian: the known arc bug. SC0: lines y = 0 and y = 100 -> y = 50; SC2: output degree 5."},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["(wire)", "The scaled curve, named by Curve name."]],
           "messages": [["(none)", "", ""]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Arcs: first radian only", "body": "On arc rails the result stops after 1 rad of each arc (bug; SC3 checks only the radius)."},
              {"kind": "limit", "head": "Matched by parameter", "body": "Not by length: uneven rails skew the blend. Orientation is manual (Flip)."},
              {"kind": "limit", "head": "Projection", "body": "Can move the ends."}]},
          {"type": "reference", "rows": ref("scaledCurve", "Gordon tools tests: SC0-SC3", "2.3", "Test Part Studio 1: Scaled Curve 1")}]}

MCE = {"slug": "modify_curve_end", "title": "Modify curve end",
       "tagline": "Move one end of a curve, chain or wire to a new point and blend the change back -- hold part of the curve exactly, keep G1/G2 there, and match the end to a reference.",
       "document": "gordonSurface", "icon": "icons/final/modify_curve_end_icon.svg", "status": STATUS,
       "slides": [
           {"type": "why", "title": "What it does",
            "problem": "Moving an end of a network curve usually means redrawing it. Modify curve end edits the curve's own control points: the end goes where you ask, the change fades back, and whatever you hold stays exactly as it was.",
            "useWhen": ["Stretch a curve to a new corner of the network", "Keep most of a curve untouched", "Land an end tangent (or curvature-continuous) to an edge, face or mate connector"],
            "image": IMG + "fig03_modify_curve_end.png", "caption": "T01 move, T30 hold, T10 match an arc (G2)."},
           {"type": "concept", "title": "How it works",
            "image": IMG + "fig03_modify_curve_end.png",
            "points": [{"head": "Control points, not a refit", "body": "Free control points move by a share of the offset that fades by arc length; the last moves fully."},
                       {"head": "Exact hold", "body": "Knots at the hold make the held part depend only on locked control points; G1 / G2 lock one / two more."},
                       {"head": "Closed-form end", "body": "Tangent (and curvature) of an edge, face or mate connector set directly on the last control points."}]},
           {"type": "dialogshot", "title": "The dialog", "screenshot": shot("modify_curve_end"),
            "params": [["Edges or wire to modify", "An edge, a chain in any order, or a wire; open, unbranched."],
                       ["Modified end", "The end to move (empty = the chain's end)."],
                       ["To point", "Where it goes (empty = stays / snaps onto the reference)."],
                       ["Hold part of the curve", "Hold by Point or Distance (arc length from the modified end)."],
                       ["Parameters", "Transition type; continuity at the fixed end / hold (G0 / G1 / G2); end reference."],
                       ["Project onto surface?", "+ face: only the part after the hold is projected (G1 kept at the hold)."],
                       ["Debug, Details", "Degree, tolerance, max control points of a merged chain, sampling multiple, print."]]},
           {"type": "dialog", "title": "End reference (in Parameters)",
            "params": [["Endpoint continuity ref.", "An edge, a face or a mate connector."],
                       ["Opposite direction", "Reverse the end tangent."],
                       ["Match", "G0 / G1 / G2 at the moved end."],
                       ["End curvature", "(G2) Match the reference / Zero / Radius."],
                       ["End radius", "100 mm; bends toward the connector's X, else the edge's normal, else the curve's own."],
                       ["Offset frame", "No effect any more (kept for saved features)."]]},
           {"type": "example", "title": "Example: hold point (T30)",
            "image": IMG + "fig03_modify_curve_end.png",
            "head": "End (200, 0) -> (200, 30); hold at (150, -30), G1",
            "body": "Everything from the fixed end to the hold is unchanged (under 1 micron); the tangent at the hold is kept. T10: matching an R25 arc gives end curvature 40/m, as the arc."},
           {"type": "outputs",
            "keys": [["output", "The modified curve (one wire)."],
                     ["movedVertex", "The end at the To point."],
                     ["holdVertex", "The hold point, or the fixed end when there is no hold."]],
            "messages": [["Info", "Notes: no Modified end picked, hold has no effect, G0 hold leaves a corner, reference far away, handle shortened, projection keeps G1 only", "One line."],
                         ["Error", "branched / several chains / closed chain", "Select one open chain."],
                         ["Error", "hold point missing or at the modified end; hold distance longer than the curve", ""]]},
           {"type": "tips", "items": [
               {"kind": "tip", "head": "Hold instead of G2 everywhere", "body": "A hold keeps the part you care about exactly; blend only the rest."},
               {"kind": "tip", "head": "Mate connector reference", "body": "Z = the end tangent, X = the bend side; set a radius for exact end curvature (T13)."},
               {"kind": "limit", "head": "Projection", "body": "Keeps the end tangent only if it lies in the face; end curvature cannot be kept."}]},
           {"type": "reference", "rows": ref("modifyCurveEnd", "Gordon tools tests: T01-T40 (all pass)", "2.4", "Test Part Studio 1: Modify curve end 1")}]}

PS = {"slug": "pull_surface", "title": "Pull surface",
      "tagline": "Push and pull a face with a grid of handles; the edges keep G0, G1 or G2 with their neighbours along their whole length.",
      "document": "gordonSurface", "icon": "icons/final/pull_surface_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Onshape has no control-net push/pull. Pull surface puts arrow handles on a face; drag them and a new sheet is built from the face's own control net, with the edge rows locked so it still fits its neighbours.",
           "useWhen": ["Add a bump or dip to a face", "Tweak a surface without rebuilding its curves", "Keep continuity with surrounding faces"],
           "image": IMG + "fig07_pull_surface.png", "caption": "P2: +10 mm at the centre of a cylinder patch, G1 edges."},
          {"type": "concept", "title": "Locks and handles",
           "image": IMG + "fig07_pull_surface.png",
           "points": [{"head": "Locked rows", "body": "1 / 2 / 3 control-point rows per edge for G0 / G1 / G2: continuity holds along the whole edge."},
                      {"head": "Handles", "body": "Free control points move along the normal; the offsets are solved so the surface passes through each handle."},
                      {"head": "Enough handles", "body": "G1 needs 5+ handles each way before any is free, G2 7+."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("pull_surface"),
           "params": [["Face", "The face to pull (kept; the result is a new sheet)."],
                      ["U curve count", "Handles across u, edges included (2-20, 4)."],
                      ["V curve count", "Handles across v."],
                      ["Continuity", "G0 / G1 / G2 along the edges."],
                      ["Active offsets", "U, V, Offset per handle; filled by dragging; 0 removes."],
                      ["Debug", "Iso-curves, keep curves / points, handle grid, offset vectors, print."],
                      ["Approximation", "Degree (minimum, up to 5); how exactly the face is read."]]},
          {"type": "outputs",
           "keys": [["output", "The pulled sheet (pullSurf)."]],
           "messages": [["Info", "no handle is free / holes not kept / solve fell back / trimmed edges held approximately", "One line."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Counts first", "body": "Changing a count or the continuity resets the offsets; set them before dragging."},
              {"kind": "limit", "head": "Trims", "body": "Holes are dropped; edges trimmed inside the face hold continuity only approximately."}]},
          {"type": "reference", "rows": ref("pullSurface", "Gordon tools tests: P1-P3", "2.5", "Test Part Studio 1: Pull surface 1")}]}

SS = {"slug": "simplify_surface", "title": "Simplify surface",
      "tagline": "Rebuild a heavy face as a lighter cubic B-spline within a tolerance -- or replace it in place.",
      "document": "gordonSurface", "icon": "docs/decks/_icons/simplify_surface_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Imported, derived and fitted faces carry far more control points than their shape needs, which slows and destabilises later features. Simplify surface finds, curve by curve, the fewest control points within your tolerance.",
           "useWhen": ["A heavy imported or fitted face", "Downstream features are slow or fail on a face", "Replace a face with a lighter one in place"],
           "image": IMG + "fig05_compatibility.png", "caption": "Station curves are reduced, made compatible and skinned."},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("simplify_surface"),
           "params": [["Face", "The face to simplify."],
                      ["Tolerance", "0.01-100 mm (1 mm)."],
                      ["Continuity", "G0 / G1 / G2 end derivatives along v (G2 mode has no effect)."],
                      ["Mode", "AUTO (Greville stations, binary search per curve) / MANUAL (U, V counts)."],
                      ["Replace face", "Replace the source face instead of a new sheet."],
                      ["Debug print", "Console."]]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["simplified", "The new sheet (or the replaced face)."]],
           "messages": [["(none)", "", ""]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Checked on stations", "body": "The error is measured along the station curves only."},
              {"kind": "limit", "head": "Compatibility re-inflates", "body": "The final count follows the most complex station; G1 needs about 6 control points."},
              {"kind": "limit", "head": "Untested", "body": "No test fixture yet."}]},
          {"type": "reference", "rows": ref("simplifySurface", "none yet", "2.6", "Test Part Studio 1: Simplify surface 1")}]}

MC = {"slug": "makecurvescompitable", "title": "makeCurvesCompitable",
      "tagline": "Developer utility: make selected curves share degree and knots, and print a before / after report.",
      "document": "gordonSurface", "icon": "docs/decks/_icons/makecurvescompitable_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Every surface-from-curves tool here first makes curves compatible. This feature exposes that step for checking: what degrees, knots and control-point counts the curves have before and after.",
           "useWhen": ["Diagnose a skinning or Gordon problem", "Inspect curve structure"],
           "image": IMG + "fig05_compatibility.png", "caption": "Degree elevation and knot union, exact."},
          {"type": "dialog", "title": "The dialog",
           "params": [["Edges to make compatible", "The curves (in selection order)."],
                      ["Print Report?", "Degrees, control-point counts, knots, rational flags, match checks -- before and after."],
                      ["Create wire?", "Output the compatible curves as wires compatibleBSpline_i (shapes unchanged)."]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Name and description", "body": "The feature name is misspelled and its description is empty."},
              {"kind": "limit", "head": "Rational input", "body": "Edges are read without forcing non-rational."}]},
          {"type": "reference", "rows": ref("gordonCurveCompat", "none", "1.1, 2.7", "none")}]}

for spec in (GS, IC, SC, MCE, PS, SS, MC):
    json.dump(spec, open("docs/decks/%s/spec.json" % spec["slug"], "w"), indent=2)
    print("wrote", spec["slug"])
