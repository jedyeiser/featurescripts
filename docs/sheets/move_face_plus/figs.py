"""Move face+ figure (schematic). Run from the repo root: python docs/sheets/move_face_plus/figs.py"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, save, bare, FancyArrowPatch, BLUE, ORANGE, AQUA, INK, INK2, MUTED, GRID  # noqa: E402

OUT = "docs/sheets/move_face_plus/img/"


def arrow(ax, p, q, color=INK2, lw=2.0):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle="-|>", mutation_scale=14, color=color, lw=lw))


def panel(ax, title, moved, note):
    bare(ax)
    ax.set_xlim(-6.5, 6.5)
    ax.set_ylim(-2.2, 4.0)
    ax.set_title(title, fontsize=11, loc="left")
    # reference
    ax.plot([0], [0], "o", color=ORANGE, ms=9, zorder=5)
    ax.text(0, -0.55, "reference", ha="center", fontsize=9, color=ORANGE)
    # two sheets, normals pointing AWAY from each other (opposite orientation)
    for x0, nx, dx, name in [(-3.0, -1, moved[0], "A"), (3.0, 1, moved[1], "B")]:
        ax.plot([x0, x0], [-1.5, 2.2], color=GRID, lw=3, ls="--")          # original
        ax.plot([x0 + dx, x0 + dx], [-1.5, 2.2], color=BLUE, lw=4)           # moved
        arrow(ax, (x0, 2.6), (x0 + nx * 0.9, 2.6), color=MUTED, lw=1.6)      # normal
        ax.text(x0 + nx * 1.0, 2.95, "normal", ha="center", fontsize=8, color=MUTED)
        if abs(dx) > 1e-9:
            arrow(ax, (x0, 0.9), (x0 + dx, 0.9), color=AQUA if dx * x0 < 0 else "#e34948", lw=2.4)
        ax.text(x0 - 0.2 * nx, -1.9, "sheet " + name, ha="center", fontsize=9, color=INK2)
    ax.text(0, 3.6, note, ha="center", fontsize=9, color=INK2)


def fig_toward():
    fig, axes = plt.subplots(1, 2, figsize=(10, 3.6))
    panel(axes[0], "Built-in Move face > Offset, +1.5", (-1.5, 1.5), "each face follows its own normal")
    panel(axes[1], "Move face+, +1.5, reference between", (1.5, -1.5), "both faces move toward the reference")
    save(fig, OUT + "toward_reference.png")


if __name__ == "__main__":
    fig_toward()
