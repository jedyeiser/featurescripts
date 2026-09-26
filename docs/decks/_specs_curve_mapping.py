"""Writes the curve-mapping deck specs (Wrap Curve, Wrap and Loft, Deform -- public versions -- and Offset edges).
Run from the repo root; then build each with docs/tooling/build_deck.js. See DECK_NOTES.md.
Figures: docs/explainers/curve_mapping/img (img/src/figs.py).
"""
import json

IMG = "docs/explainers/curve_mapping/img/"
STATUS = "Draft 2026-09-26"
PUB = "curveMapping_public (d12583277c42b00c63a9b7b7)"
EXPL = "docs/explainers/curve_mapping/curve_mapping_explained.md, section "
RELATED = "Wrap Curve, Wrap and Loft, Deform, Measure curve distance, Offset edges"


def shot(slug):
    return "docs/decks/%s/shots/dialog.png" % slug


MAP = {"type": "concept", "title": "The bending map",
       "image": IMG + "fig01_bending_map.png",
       "points": [{"head": "Arc length kept", "body": "A point s along the From reference lands s along the To reference (a shift, no scaling)."},
                  {"head": "Height and width kept", "body": "The point's offsets in the frame carry over: ribs stay perpendicular."},
                  {"head": "Layers bend", "body": "At height h around radius R they stretch or shrink by (R - h) / R, as a real layer does."}]}

FRAMES = {"type": "concept", "title": "Frames and ends",
          "image": IMG + "fig02_overrun.png",
          "points": [{"head": "Frenet (default)", "body": "One normal carried along with minimal rotation: no flip at inflections."},
                     {"head": "Binormal", "body": "Normal from a plane you give (face or mate connector); every edge must lie in it."},
                     {"head": "Past the ends", "body": "The osculating circle continues the chain -- exact on arcs."}]}

FROM_TO = [["From data", "From edge(s) (G1 chain) and From reference (vertex, mate connector, plane)."],
           ["To data", "To edge(s), Flip (reverse To), To reference, Flip normal."],
           ["Frame orientation", "Frenet (default) / Binormal (face, mate connector or world axis)."]]

WC = {"slug": "wrap_curve", "title": "Wrap Curve",
      "tagline": "Wrap flat curves onto a curved profile by arc length -- length along the profile kept, height and width carried over.",
      "document": "curveMapping_public", "icon": "docs/decks/_icons/wrap_curve_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Planforms, graphics, inlays and insert lines are drawn flat; the ski is built on a rockered, cambered profile. Native Wrap only handles cylinders and cones, and projecting foreshortens. Wrap Curve keeps the arc length along the profile, so a station 1500 mm along the layout lands 1500 mm along the ski.",
           "useWhen": ["Put flat layout lines on the profile", "Keep stations at their true distance along the ski", "Carry width and height exactly"],
           "image": IMG + "fig01_bending_map.png", "caption": "Tip example: 1200 flat + R600 tip; x = 1500 lands 300 into the arc."},
          MAP, FRAMES,
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("wrap_curve"),
           "params": FROM_TO[:2] + [["Source curves", "The curves to wrap."]] + FROM_TO[2:] + [
               ["Details", "Sampling (control-point or length based), degree 3, max control points 15, tolerance 0.01 mm; Break spans at curvature jumps and corners."],
               ["Debug", "Print curves, show frames and sample points."]]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["(wire)", "One wire: spline spans, plus exact copies on straight stretches."]],
           "messages": [["Console", "span fitted at a lower degree", "The source's degree was too high for a short To edge."],
                        ["Console", "cannot be fitted; the output will have a gap", "Rebuild the source at degree 3 or sample by length."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Flip normal", "body": "If 'up' comes out on the wrong side -- older models may need it after the 2026-09-23 frame change."},
              {"kind": "limit", "head": "Only To flips", "body": "From's direction comes from its chain; an off-centre From reference pushes points past an end."},
              {"kind": "limit", "head": "Break spans", "body": "Changes topology (more edges) -- downstream references may move."}]},
          {"type": "reference", "rows": [["Document", PUB], ["Tab", "wrapCurve (+ engine curveMappingCore, pinned)"], ["Tests", "none"],
                                         ["Explainer", EXPL + "1, 2.1"], ["Related", RELATED]]}]}

WL = {"slug": "wrap_and_loft", "title": "Wrap and Loft",
      "tagline": "Wrap curves onto the profile, offset them along its normal, and loft a strip -- sidewall and core-edge reference surfaces that follow rocker and camber.",
      "document": "curveMapping_public", "icon": "docs/decks/_icons/wrap_and_loft_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "A reference surface that follows the profile through the thickness needs the wrapped curve and a matching offset curve, lofted -- with no gaps where source curves meet. Wrap and Loft does all of it in one feature.",
           "useWhen": ["Sidewall or core-edge reference surfaces", "A strip of set height along the profile", "Planar sources (the From path is made for you)"],
           "image": IMG + "fig03_wrap_and_loft.png", "caption": "Primary 20 + second 5: a 25 wide strip along the To normal."},
          MAP,
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("wrap_and_loft"),
           "params": [["Wrap edges", "The source curves."]] + FROM_TO + [
               ["Offset", "Primary offset (20 mm), Flip offset direction, Second direction + Second offset."],
               ["Keep output curves", "Keep all / Keep wrapped only."],
               ["Details", "As Wrap Curve, + Fix control-point clustering (on), Break spans, Exact ruled surface."],
               ["Debug", "Print curves, show frames."]]},
          {"type": "concept", "title": "What makes it robust",
           "image": IMG + "fig03_wrap_and_loft.png",
           "points": [{"head": "Planar shortcut", "body": "Planar sources: From edge(s) disappears; the From path is the To chain projected onto the source plane (wraps, not drapes)."},
                      {"head": "Welds", "body": "Where curves meet, the later adopts the first's offset direction: no 0.35 mm gaps at seams."},
                      {"head": "Exact ruled (option)", "body": "An exact ruled B-spline instead of a loft where possible; changes face ids."}]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["(sheet)", "One face per source curve, unioned."], ["(wires)", "Optional: wrapped, primary, second curves."]],
           "messages": [["Info", "clustering fix recommended", "Only when it is off and slivers were found."]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Negative offset", "body": "Use Flip offset direction; primary cannot be negative."},
              {"kind": "limit", "head": "Planar check", "body": "A single straight source edge cannot define a plane: the flag keeps its previous value."},
              {"kind": "limit", "head": "Failures", "body": "Loft / union failures are reported on the console only."}]},
          {"type": "reference", "rows": [["Document", PUB], ["Tab", "wrapAndLoft (+ engine curveMappingCore)"], ["Tests", "none"],
                                         ["Explainer", EXPL + "2.2"], ["Related", RELATED]]}]}

DF = {"slug": "deform", "title": "Deform",
      "tagline": "Bend a whole body, or selected faces, onto the profile with the same map -- a flat-modelled insert, core feature or tip protector.",
      "document": "curveMapping_public", "icon": "docs/decks/_icons/deform_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Parts modelled flat must sit on the curved ski. Native Bend only flexes around an axis. Deform maps every vertex, edge and face through the bending map, so the part follows the profile exactly as a layer would.",
           "useWhen": ["An insert or protector modelled flat", "Features that must follow rocker / camber", "Faces rather than a whole body"],
           "image": IMG + "fig04_deform.png", "caption": "A 100 x 5 block bent onto the tip: bottom 100, top 99.2 (concave side)."},
          MAP,
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("deform"),
           "params": [["Mode", "Bodies (one solid or sheet) / Faces."]] + FROM_TO + [
               ["Setup", "Sampling, degree, max control points, tolerance; Use face guide points; Keep wires."],
               ["Debug", "Create wires / faces / bodies stages; show frames."]]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["(body)", "A new body (the source stays); solids closed into solids."]],
           "messages": [["Console", "per-face WARNING / ERROR, OPEN END reports", "Check for a missing face or a sheet instead of a solid."]]},
          {"type": "tips", "items": [
              {"kind": "limit", "head": "Every face is a fill", "body": "Planes and cylinders are not kept as such; inner loops may fail."},
              {"kind": "tip", "head": "Guide points", "body": "Turn on Use face guide points when fills do not follow the face."},
              {"kind": "limit", "head": "Heavy", "body": "The slowest of the family: many edges, many projections."}]},
          {"type": "reference", "rows": [["Document", PUB], ["Tab", "deform (+ engine curveMappingCore)"], ["Tests", "none"],
                                         ["Explainer", EXPL + "2.3"], ["Related", RELATED]]}]}

OE = {"slug": "offset_edges", "title": "Offset edges",
      "tagline": "Offset one chain by amounts that vary along its length -- regions with ramps and dwells, blended; in-plane or out-of-plane; sharp corners handled.",
      "document": "example_1", "icon": "icons/final/offset_edges_icon.svg", "status": STATUS,
      "slides": [
          {"type": "why", "title": "What it does",
           "problem": "Native Offset curve is constant and in a sketch plane. Ski edges need offsets that change along the length -- a bevel that grows toward the tip, a step between two zones -- following a 3D chain.",
           "useWhen": ["Offsets that vary along a sidecut or edge line", "Zones with blends between them", "Out-of-plane offsets along a 3D chain"],
           "image": IMG + "fig05_offset_edges.png", "caption": "OE6 regions + blend, OE10 pins, OE24 corner."},
          {"type": "concept", "title": "Regions",
           "image": IMG + "fig06_region_types.png",
           "points": [{"head": "Per region", "body": "Extent (distances from the reference or two points), start / end offset, Linear / Quadratic / Smooth, dwells."},
                      {"head": "Blends", "body": "Neighbouring regions blended over a distance into each, matched to G0 / G1 / G2."},
                      {"head": "Or pins", "body": "Single region: offsets at chosen points, flat beyond the first and last."}]},
          {"type": "concept", "title": "Directions (physically)",
           "image": IMG + "fig05_offset_edges.png",
           "points": [{"head": "'Normal offset'", "body": "Moves OUT of the chain's plane (a line in XY offsets along Z)."},
                      {"head": "'Binormal offset'", "body": "Moves WITHIN the plane, toward / away from the centre of curvature."},
                      {"head": "Names", "body": "The reverse of Frenet naming: describe results physically."}]},
          {"type": "dialogshot", "title": "The dialog", "screenshot": shot("offset_edges"),
           "params": [["Reference edges", "One chain (G1, or with sharp corners)."],
                      ["Reference point", "Where distance 0 is."],
                      ["Flip direction", "Reverse the distance direction (+ Show direction indicator)."],
                      ["Flip normal", "Reverse the out-of-plane direction (Flip binormal likewise)."],
                      ["Source arcs", "Convert to splines / Keep as arcs (concentric or arc pairs)."],
                      ["Sampling density", "Points per region / blend (20)."],
                      ["Offset layout", "Multiple regions / Single region."],
                      ["Regions", "Type, name, extent, offset type, start / end offsets, dwells."],
                      ["Intersections", "Per neighbouring pair: blend, continuities, distances."],
                      ["Approximation", "Degree 3, tolerance 0.01 mm, max control points 100."],
                      ["Debug", "Show frames / regions / blends; print data."]]},
          {"type": "outputs", "keysTitle": "Outputs",
           "keys": [["(wire)", "One wire, split at source-edge boundaries and region / blend joints."]],
           "messages": [["Info", "N arc pairs, worst deviation ...", "Keep as arcs with varying offsets."],
                        ["Warning", "no regions / overlapping regions / clamped dwell or blend / region consumed by blends", ""]]},
          {"type": "tips", "items": [
              {"kind": "tip", "head": "Keep as arcs", "body": "Constant offsets of arcs stay exact arcs (report a radius)."},
              {"kind": "limit", "head": "Gaps", "body": "Gaps between regions are gaps in the output; blending off gives a step."},
              {"kind": "limit", "head": "Slivers", "body": "A region boundary within ~1 mm of an edge boundary can leave a sliver edge."}]},
          {"type": "reference", "rows": [["Document", "example_1 (7183868d32cdc7a6ad14410d)"], ["Tab", "refSurfCreation/offsetEdges (+ engine curveMappingCore)"],
                                         ["Tests", "Offset edges tests: OE1-OE24 (all pass)"], ["Explainer", EXPL + "2.5"], ["Related", RELATED]]}]}

for spec in (WC, WL, DF, OE):
    json.dump(spec, open("docs/decks/%s/spec.json" % spec["slug"], "w"), indent=2)
    print("wrote", spec["slug"])
