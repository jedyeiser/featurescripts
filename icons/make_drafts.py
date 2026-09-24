"""Draft feature icons from Onshape's own glyphs (icons/onshape_reference) plus small additions.

Style (corrections log #35): 20 x 20 viewBox; #333333 outline, #999999 secondary, #FFFFFF fill, #1651B0 accent.
Writes icons/drafts/<name>.svg. Run from the repo root:  python icons/make_drafts.py
"""
import os
import re

REF = "icons/onshape_reference"
OUT = "icons/drafts"
BLUE, DARK, GREY, WHITE = "#1651B0", "#333333", "#999999", "#FFFFFF"


def inner(name):
    """The drawing inside an exported Onshape glyph (everything between <svg ...> and </svg>)."""
    s = open(os.path.join(REF, name + ".svg"), encoding="ascii").read()
    return re.sub(r"^<svg[^>]*>|</svg>\s*$", "", s.strip())


def glyph(name, scale=1.0, dx=0.0, dy=0.0):
    return '<g transform="translate(%g %g) scale(%g)">%s</g>' % (dx, dy, scale, inner(name))


# Onshape's own "+" badge (add-variable-button), with a white halo so it reads over any glyph.
PLUS = "M11 15h3v-3h2v3h3v2h-3v3h-2v-3h-3v-2z"
PLUS_BADGE = ('<path d="%s" fill="%s" stroke="%s" stroke-width="1.6" stroke-linejoin="miter"/>'
              '<path d="%s" fill="%s"/>' % (PLUS, WHITE, WHITE, PLUS, BLUE))


def profile_graph(x0, y0, w, h, color, weight):
    """A tiny offset-profile graph: axes, a flat run, a smooth rise, a flat run -- the create_offset_profile motif."""
    ax = weight * 0.8
    x1, y1 = x0 + w, y0 + h
    lo, hi = y1 - 0.28 * h, y0 + 0.25 * h
    a, b, c = x0 + 0.18 * w, x0 + 0.42 * w, x0 + 0.66 * w
    curve = "M%g %g H%g C%g %g %g %g %g %g H%g" % (x0 + 0.1 * w, lo, a, (a + b) / 2, lo, (a + b) / 2, hi, b, hi, x1 - 0.05 * w)
    step = "M%g %g V%g" % (c, hi, lo)
    return ('<path d="M%g %g V%g H%g" fill="none" stroke="%s" stroke-width="%g"/>' % (x0, y0, y1, x1, GREY, ax) +
            '<path d="%s" fill="none" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"/>' % (curve, color, weight))


def badge_box(x0, y0, w, h):
    """White backing card so a badge reads over the base glyph."""
    return '<rect x="%g" y="%g" width="%g" height="%g" rx="1" fill="%s" stroke="%s" stroke-width="0.8"/>' % (x0, y0, w, h, WHITE, WHITE)


def svg(body):
    return '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 20 20">%s</svg>\n' % body


DRAFTS = {
    # "+" features: Onshape base + Onshape's blue plus
    "split_plus": glyph("split-part-button") + PLUS_BADGE,
    "mutual_trim_plus": glyph("mutual-trim-button") + PLUS_BADGE,
    "offset_plus": glyph("offset-surface-button") + PLUS_BADGE,

    # driven offset family: the profile graph is the shared motif
    "create_offset_profile": profile_graph(2, 3, 16, 14, DARK, 1.6),
    "driven_edge_offset": glyph("offset-curve-on-face-button", 0.85, 0, 0) + badge_box(10.5, 11.5, 9, 8) + profile_graph(11.2, 12.2, 7.8, 6.6, BLUE, 1.2),
    "driven_offset_surface": glyph("offset-surface-button", 0.85, 0, 0) + badge_box(10.5, 11.5, 9, 8) + profile_graph(11.2, 12.2, 7.8, 6.6, BLUE, 1.2),

    # solvers
    "iterative_solve": glyph("refresh-button") + glyph("assign-variable-button", 0.42, 5.8, 5.8),
    "part_volume": glyph("mass-properties"),

    # small tools
    "move_along_edge": ('<path d="M2 16 C6 16 7 5 12 5 S17 9 18 9" fill="none" stroke="%s" stroke-width="1.4" stroke-linecap="round"/>' % GREY
                        + '<circle cx="2" cy="16" r="1.3" fill="%s"/><circle cx="18" cy="9" r="1.3" fill="%s"/>' % (DARK, DARK)
                        + '<g transform="translate(7.2 1.6)"><rect x="0" y="0" width="5.2" height="5.2" fill="%s" stroke="%s" stroke-width="1.2"/></g>' % (WHITE, DARK)
                        + '<path d="M13.5 3.2 l3 1.4 -3 1.4 z" fill="%s"/>' % BLUE),
}

os.makedirs(OUT, exist_ok=True)
for name, body in DRAFTS.items():
    open(os.path.join(OUT, name + ".svg"), "w", encoding="ascii").write(svg(body))
    print("wrote", name)
