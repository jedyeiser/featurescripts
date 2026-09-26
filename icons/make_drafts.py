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

# ---------------------------------------------------------------------------------------------------------------
# 2026-09-25 KEY-tools batch (reviews/2026-09-25_tools_review/crosscut.md section 6 glyph concepts).
# Shared motifs: ski planform (footprint suite), ski side profile + slices (xSection), curve with end dots.


def P(d, stroke=DARK, w=1.4, fill="none", extra=""):
    return ('<path d="%s" fill="%s" stroke="%s" stroke-width="%g" stroke-linecap="round" stroke-linejoin="round"%s/>'
            % (d, fill, stroke, w, extra))


def F(d, fill):
    return '<path d="%s" fill="%s"/>' % (d, fill)


def dot(x, y, r=1.4, fill=DARK, stroke=None, w=1.0):
    s = ' stroke="%s" stroke-width="%g"' % (stroke, w) if stroke else ""
    return '<circle cx="%g" cy="%g" r="%g" fill="%s"%s/>' % (x, y, r, fill, s)


def head(x, y, dx, dy, size=2.6, color=BLUE):
    """Filled triangular arrow head with its tip at (x, y) pointing along (dx, dy)."""
    import math
    n = math.hypot(dx, dy)
    tx, ty = dx / n, dy / n
    nx, ny = -ty, tx
    bx, by = x - tx * size, y - ty * size
    h = size * 0.55
    return F("M%.2f %.2f L%.2f %.2f L%.2f %.2f Z" % (x, y, bx + nx * h, by + ny * h, bx - nx * h, by - ny * h), color)


def ski(dx=0.0, dy=0.0, s=1.0, stroke=DARK, w=1.3, fill=WHITE):
    """Ski planform, tail left / tip right, centreline y = 10, x 1.5 .. 19 (before transform)."""
    d = ("M1.5 6.6 C5.5 7.4 8.5 8.1 10.5 8.1 C13 8.1 14.3 6.1 16.2 5.9 C18.2 5.7 19 8.2 19 10 "
         "C19 11.8 18.2 14.3 16.2 14.1 C14.3 13.9 13 11.9 10.5 11.9 C8.5 11.9 5.5 12.6 1.5 13.4 Z")
    return '<g transform="translate(%g %g) scale(%g)">%s</g>' % (dx, dy, s, P(d, stroke, w / s, fill))


def half_ski(stroke=DARK, w=1.4):
    """Top half of the planform on a grey centreline (Integrate footprint)."""
    return (P("M1.5 15.5 H19", GREY, 1.1) +
            P("M1.5 9.5 C5.5 10.4 8.5 11.3 10.5 11.3 C13 11.3 14.3 8.6 16.2 8.4 C18.2 8.2 19 12 19 15.5", stroke, w))


def slice_(cx, cy, fill=WHITE, stroke=DARK, w=1.0, sx=1.0, sy=1.0):
    """Trapezoid ski cross-section seen in perspective (a slanted card)."""
    pts = [(-1.3, -3.4), (1.3, -4.4), (1.3, 3.4), (-1.3, 4.4)]
    d = "M" + " L".join("%.2f %.2f" % (cx + x * sx, cy + y * sy) for x, y in pts) + " Z"
    return P(d, stroke, w, fill)


# Arc fit: grey dashed raw sidecut, three tangent dark arcs (left convex, centre concave, right convex) with
# white junction dots; blue centre + radius of the middle arc.
ARC_FIT = (P("M1.2 11.5 A4.4 4.4 0 0 1 10 10", DARK, 1.6)
           + P("M10 10 A4.4 4.4 0 0 0 18.8 8.5", DARK, 1.6)
           + P("M5.6 9.3 V13.3 M3.6 11.3 H7.6", GREY, 1.1)
           + P("M14.4 8.7 L17.3 12", BLUE, 1.2) + dot(14.4, 8.7, 1.5, BLUE)
           + dot(1.2, 11.5, 1.3) + dot(18.8, 8.5, 1.3) + dot(10, 10, 1.4, WHITE, DARK, 1.1))

DRAFTS.update({
    "arc_fit": ARC_FIT,

    # Generate footprint points: grey planform, blue sample dots on the top edge, small dark table card.
    "footprint_points": (ski(0, -1.5, 1.0, GREY, 1.2)
                         + "".join(dot(x, y, 1.25, BLUE) for x, y in [(2.5, 5.3), (6.5, 6.1), (10.5, 6.6), (14.5, 4.9), (18.1, 5.8)])
                         + '<rect x="11" y="12.3" width="8" height="7" rx="0.6" fill="%s" stroke="%s" stroke-width="1.1"/>' % (WHITE, DARK)
                         + P("M11 14.7 H19 M11 17 H19 M14.3 12.3 V19.3", DARK, 0.9)),

    # Analyze footprint: planform with a blue waist width dimension.
    "analyze_footprint": (ski(0, 0, 1.0, DARK, 1.4)
                          + P("M10.5 3.4 V16.6", BLUE, 1.2) + head(10.5, 8.2, 0, 1, 2.4) + head(10.5, 11.8, 0, -1, 2.4)
                          + P("M8.6 3.1 H12.4 M8.6 16.9 H12.4", BLUE, 1.1)),

    # Integrate footprint: half planform on its centreline + a blue integral sign.
    "integrate_footprint": (half_ski()
                            + P("M8.2 2.2 C6.6 1.2 5.4 2.4 5.2 4.6 L4.5 13.4 C4.3 15.6 3.1 16.8 1.5 15.8", BLUE, 1.6)),

    # Scale footprint: grey larger ski behind a dark smaller ski; blue length arrow underneath.
    "scale_footprint": (ski(0, -2.2, 1.0, GREY, 1.1, "none") + ski(2.4, 0.2, 0.62, DARK, 1.3)
                        + P("M2 17.8 H18", BLUE, 1.2) + head(1.2, 17.8, -1, 0, 2.4) + head(18.8, 17.8, 1, 0, 2.4)),

    # Generate baseline: ski side profile (tail/tip rise, camber) above a grey ground line; blue contact points.
    "generate_baseline": (P("M1 14.8 H19", GREY, 1.1)
                          + P("M1.2 7.5 C2.2 11 3.4 13.2 5.2 13.2 C8.5 11.4 11.5 11.4 14.8 13.2 C16.6 13.2 17.8 9 18.8 4.5", DARK, 1.6)
                          + dot(5.2, 13.2, 1.7, BLUE, WHITE, 0.8) + dot(14.8, 13.2, 1.7, BLUE, WHITE, 0.8)),

    # Scaled curve: two grey rails, a dark curve between them, a blue double tick at the scale position.
    "scaled_curve": (P("M1.5 4.5 C6 1.5 12 7.5 18.5 3.5", GREY, 1.3) + P("M1.5 17 C6 14 12 19.5 18.5 15.5", GREY, 1.3)
                     + P("M1.5 12 C6 9 12 14.8 18.5 10.8", BLUE, 1.7) + dot(1.5, 12, 1.4) + dot(18.5, 10.8, 1.4)),

    # Modify curve end: grey dashed original, dark modified curve from the fixed end through a hollow hold dot to a
    # blue moved end; blue arrow from the old end; grey reference stub at the new end.
    "modify_curve_end": (P("M2 17 C6 17 9 13 12 12.5 C14.5 12 16.5 13 18 14", GREY, 1.1, extra=' stroke-dasharray="1.6 1.4"')
                         + P("M2 17 C6 17 7.5 12.5 9 11 C11 9 13 5 15.5 3.5", DARK, 1.6)
                         + P("M15.5 3.5 L19 1.6", GREY, 1.1)
                         + P("M17.8 13.2 L16.4 7.4", BLUE, 1.1) + head(16.1, 6.1, -0.24, -1, 2.4)
                         + dot(2, 17, 1.4) + dot(8.2, 12.1, 1.35, WHITE, DARK, 1.1) + dot(15.5, 3.5, 1.6, BLUE)),

    # Pull surface: perspective patch (bulged top edge) with a 3x3 grey dot grid; blue centre dot + up arrow.
    "pull_surface": (P("M1.5 12.5 H13.5 L18.5 18.5 H6.5 Z", DARK, 1.2, WHITE)
                     + P("M4.6 16 C7 15.6 8 11.2 11 11.2 C13.8 11.2 14 15.6 16.4 16", GREY, 1.1)
                     + "".join(dot(x, y, 0.75, GREY) for x, y in [(5, 14), (16.3, 17.2), (8.6, 17.2), (13.4, 14)])
                     + P("M11 11.2 V5", BLUE, 1.3) + head(11, 1.6, 0, -1, 3.0) + dot(11, 11.2, 1.5, BLUE)),

    # Join wires: three dark segments; one joint already a dark dot, the other joined by a blue link.
    "join_wires": (P("M1.8 16.5 C3.5 12 4.5 10 7 9.2 C9.5 8.5 10.5 12.5 13.2 11.8 C15.8 10 16.8 6.5 18.2 3.5", DARK, 1.6)
                   + dot(1.8, 16.5, 1.4) + dot(18.2, 3.5, 1.4) + dot(7, 9.2, 1.4)
                   + dot(13.2, 11.8, 1.9, BLUE, WHITE, 0.9)),

    # Extrude edge: dark open curve at the bottom, thin white sheet with grey edges rising from it, blue up arrow.
    "extrude_edge": (P("M1.5 17.5 C5 15 8 19.5 11.5 17 V7.5 C8 10 5 5.5 1.5 8 Z", GREY, 1.1, WHITE)
                     + P("M1.5 17.5 C5 15 8 19.5 11.5 17", DARK, 1.7)
                     + dot(1.5, 17.5, 1.3) + dot(11.5, 17, 1.3)
                     + P("M16 17.5 V5.5", BLUE, 1.4) + head(16, 2.2, 0, -1, 3.2)),

    # Offset edges: dark source curve, grey offset copy, blue moving frame (tangent + normal arrows) on the source.
    "offset_edges": (P("M1.5 9.5 C5 2.5 11 2.5 18.5 6.5", GREY, 1.2)
                     + P("M1.5 18.5 C5 11.5 11 11.5 18.5 15.5", DARK, 1.6) + dot(1.5, 18.5, 1.4) + dot(18.5, 15.5, 1.4)
                     + P("M8.4 13.3 L13.4 13.5", BLUE, 1.3) + head(16, 13.7, 1, 0.05, 2.8)
                     + P("M8.4 13.3 V9.4", BLUE, 1.3) + head(8.4, 6.8, 0, -1, 2.8)
                     + dot(8.4, 13.3, 1.3, BLUE)),

    # EI & Cross Section: thin dark side profile with three slices (grey, blue, grey).
    "ei_cross_section": (P("M1 11.5 C2.5 13.5 4 14 6 14 H14 C16 14 17.5 12.5 19 9.5", DARK, 1.6)
                         + slice_(4.4, 11.6, WHITE, GREY, 1.2) + slice_(15.6, 11.6, WHITE, GREY, 1.2)
                         + slice_(10, 11.6, BLUE, BLUE, 1.0, 1.25, 1.2)),

    # Solve GJ: large trapezoid slice on a grey axis, blue twist arrow around the axis.
    "solve_gj": (P("M1 11 H19", GREY, 1.1)
                 + P("M7 4.5 L13 3 V17 L7 18.5 Z", DARK, 1.3, WHITE)
                 + P("M15.6 5.2 A6.2 3.6 0 0 1 15.9 16.4", BLUE, 1.4) + head(13.2, 17.6, -1, 0.35, 2.8)
                 + P("M4.4 16.8 A6.2 3.6 0 0 1 4.1 5.6", BLUE, 1.4) + head(6.8, 4.4, 1, -0.35, 2.8)),

    # Simple body rename: dark cube, white tag, blue I-beam text cursor on the tag.
    "simple_body_rename": (F("M7 1 L13 4 V11 L7 14 L1 11 V4 Z", DARK)
                           + F("M7 2.3 L11.6 4.6 L7 6.9 L2.4 4.6 Z", WHITE) + F("M2.1 5.6 L6.4 7.8 V12.6 L2.1 10.4 Z", WHITE)
                           + F("M11.9 5.6 V10.4 L7.6 12.6 V7.8 Z", GREY)
                           + P("M8.5 12 H18.8 V18.8 H8.5 L6.3 15.4 Z", DARK, 1.2, WHITE)
                           + P("M13.6 13.4 V17.4 M12.4 13.4 H14.8 M12.4 17.4 H14.8", BLUE, 1.2)),

    # Composite boolean (composite_part_tools, pending tab): two overlapping composite parts (each two stacked
    # boxes); the overlap -- the boolean result -- in blue.
    "composite_boolean": ('<rect x="1.5" y="1.5" width="11" height="11" rx="0.8" fill="%s" stroke="%s" stroke-width="1.3"/>' % (WHITE, DARK)
                          + P("M1.5 7 H12.5", DARK, 1.0)
                          + '<rect x="7.5" y="7.5" width="11" height="11" rx="0.8" fill="%s" stroke="%s" stroke-width="1.3"/>' % (WHITE, GREY)
                          + P("M7.5 13 H18.5", GREY, 1.0)
                          + F("M7.5 7.5 H12.5 V12.5 H7.5 Z", BLUE)
                          + P("M1.5 1.5 H12.5 V12.5 H1.5 Z", DARK, 1.3)),
})

os.makedirs(OUT, exist_ok=True)
for name, body in DRAFTS.items():
    open(os.path.join(OUT, name + ".svg"), "w", encoding="ascii").write(svg(body))
    print("wrote", name)
