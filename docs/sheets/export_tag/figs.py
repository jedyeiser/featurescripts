"""Export tag sheet figures (schematics, no live data). Run from the repo root:
    python docs/sheets/export_tag/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import (plt, np, save, bare, arrow, FancyBboxPatch, Polygon, Rectangle,  # noqa: E402
                      BLUE, ORANGE, AQUA, RED, INK, INK2, MUTED, GRID)

OUT = "docs/sheets/export_tag/img/"
TINT, EDGE = "#f3f5f8", "#52514e"


def outline(cx, cy, L=60, W=14, rot=0.0):
    """A ski-like plan outline (waisted rounded strip), centre cx, cy."""
    t = np.linspace(-1, 1, 60)
    half = W / 2 * (1 - 0.18 * np.cos(np.pi * t * 0.9) * 0 - 0.22 * (1 - t ** 2) ** 0.0) * (0.78 + 0.22 * t ** 2)
    xs = np.concatenate([t * L / 2, (t * L / 2)[::-1]])
    ys = np.concatenate([half, -half[::-1]])
    c, s = np.cos(rot), np.sin(rot)
    return np.column_stack([cx + c * xs - s * ys, cy + s * xs + c * ys])


def fig_hero():
    """Before / after: a part somewhere in the model -> DXF faces patterned from the export origin."""
    fig, (a, b) = plt.subplots(1, 2, figsize=(10, 3.6), gridspec_kw={"width_ratios": [1, 1.5]})
    # left: in the model
    bare(a)
    a.set_xlim(-60, 90)
    a.set_ylim(-40, 70)
    a.plot([-60, 90], [0, 0], color=GRID, lw=1)
    a.plot([0, 0], [-40, 70], color=GRID, lw=1)
    a.add_patch(Polygon(outline(30, 38, rot=0.35), closed=True, fc="#dbe6f5", ec=BLUE, lw=1.6))
    a.plot([30], [38], "o", color=ORANGE, ms=6)
    a.text(30, 56, "tagged part\n(anywhere in the model)", ha="center", fontsize=8.5, color=INK2)
    a.text(2, -6, "world origin", fontsize=8, color=MUTED)
    a.set_title("Part Studio", loc="left")
    # right: export frame
    bare(b)
    b.set_xlim(-10, 215)
    b.set_ylim(-18, 40)
    arrow(b, (-5, 0), (210, 0), col=INK2, lw=1)
    arrow(b, (0, -5), (0, 36), col=INK2, lw=1)
    b.text(207, 2, "X", fontsize=8, color=INK2)
    b.text(3, 34, "Y", fontsize=8, color=INK2)
    for k in range(3):
        x0 = 10 + k * 70
        b.add_patch(Polygon(outline(x0 + 30, 18), closed=True, fc="#d7f0e6", ec=AQUA, lw=1.6))
    b.annotate("", xy=(10, 3), xytext=(0, 3), arrowprops=dict(arrowstyle="<->", color=RED, lw=1.1))
    b.text(5, 5.5, "X\nbuffer", fontsize=6.5, color=RED, ha="center", va="bottom")
    b.annotate("", xy=(80, 27), xytext=(70, 27), arrowprops=dict(arrowstyle="<->", color=RED, lw=1.1))
    b.text(75, 29.5, "clearance", fontsize=7, color=RED, ha="center")
    b.text(105, -14, "surface bodies <name> XY EXPORT_PATTERN_n, in composite <name> XY DXF", ha="center", fontsize=8, color=INK2)
    b.set_title("Export origin: 1st quadrant, pattern 3 x 1", loc="left")
    fig.subplots_adjust(wspace=0.08)
    save(fig, OUT + "export_tag_hero.png")


def box(ax, x, y, w, h, text, ec=EDGE, fc=TINT, size=8.5, weight="normal", color=INK):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.1", fc=fc, ec=ec, lw=1.2))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=size, fontweight=weight, color=color)


def fig_flow():
    """What goes in, what the tag builds, and what the export app / API tool reads."""
    fig, ax = plt.subplots(figsize=(10, 3.7))
    bare(ax)
    ax.set_xlim(0, 10)
    ax.set_ylim(0, 4)
    box(ax, 0.05, 2.6, 1.9, 1.0, "ONE body or\nONE composite", ec=BLUE, weight="bold")
    ax.text(1.0, 2.5, "+ name, export origin,\nDXF / solid / station options", ha="center", va="top", fontsize=7.5, color=INK2)
    box(ax, 3.0, 2.45, 2.0, 1.1, "Export tag\n(the feature)", ec=ORANGE, weight="bold", size=10)
    arrow(ax, (1.97, 3.1), (2.98, 3.0), col=INK2)
    # builds
    items = [(3.4, "DXF faces: surface bodies on XY / XZ + composite"),
             (2.55, "Shifted copy  <name> EXPORT  (single body)"),
             (1.75, "Station views  <name> XY / XZ  (Station geometry)"),
             (0.95, "Attribute publishExportTag + variable exportTag")]
    for y, t in items:
        box(ax, 5.8, y - 0.3, 4.15, 0.6, t, ec=AQUA, size=7.5)
        arrow(ax, (5.02, 3.0), (5.78, y), col=INK2, lw=1.1, ms=9)
    ax.text(7.9, 0.3, "Built by FeatureScript", ha="center", fontsize=8, color=AQUA, fontweight="bold")
    ax.text(2.6, 1.0, "Only RECORDED here (the app / API tool does the exporting):\nSTEP / IGES / STL / Parasolid, file names, release prefix",
            ha="center", va="center", fontsize=8, color=RED)
    ax.add_patch(Rectangle((0.05, 0.5), 5.0, 1.0, fc="none", ec=RED, lw=1, ls="--"))
    save(fig, OUT + "export_tag_flow.png")


if __name__ == "__main__":
    fig_hero()
    fig_flow()

