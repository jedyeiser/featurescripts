"""Join profile surfaces figure (side-view schematic). Run from the repo root:
python docs/sheets/join_profile_surfaces/figs.py"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, save, bare, BLUE, ORANGE, AQUA, MAGENTA, RED, INK2, MUTED, GRID  # noqa: E402

OUT = "docs/sheets/join_profile_surfaces/img/"
S, E = 3.0, 9.0          # start and end split positions


def common(ax, title):
    bare(ax)
    ax.set_xlim(-0.5, 12.5)
    ax.set_ylim(-1.3, 4.6)
    ax.set_title(title, fontsize=10.5, loc="left")
    for xs, name in [(S, "start split"), (E, "end split")]:
        ax.plot([xs, xs], [-0.6, 4.1], color=MUTED, lw=1.6, ls="--")
        ax.text(xs, 4.25, name, ha="center", fontsize=8.5, color=INK2)
    ax.plot([6], [1.2], "o", color=ORANGE, ms=9, zorder=5)
    ax.text(6, 0.55, "inside point", ha="center", fontsize=8.5, color=ORANGE)


def fig_join():
    fig, axes = plt.subplots(1, 2, figsize=(11, 3.3))
    ax = axes[0]
    common(ax, "Before: two profile surfaces, two splits")
    ax.plot([0, 12], [3.0, 3.0], color=BLUE, lw=3)
    ax.text(0.1, 3.25, "outside profile", fontsize=8.5, color=BLUE)
    ax.plot([0, 12], [1.9, 1.9], color=AQUA, lw=3)
    ax.text(0.1, 1.45, "inside profile", fontsize=8.5, color="#0e6b4a")

    ax = axes[1]
    common(ax, "After: cut, lofted, merged, filleted")
    ax.plot([0, S], [3.0, 3.0], color=BLUE, lw=3)
    ax.plot([E, 12], [3.0, 3.0], color=BLUE, lw=3)
    ax.plot([S, E], [1.9, 1.9], color=AQUA, lw=3)
    ax.plot([S, S], [1.9, 3.0], color=MAGENTA, lw=3)
    ax.plot([E, E], [1.9, 3.0], color=MAGENTA, lw=3)
    ax.text(S - 0.2, 2.45, "start loft", fontsize=8.5, color=MAGENTA, ha="right")
    ax.text(E + 0.2, 2.45, "end loft", fontsize=8.5, color=MAGENTA, ha="left")
    ax.text(6, 2.15, "inside kept between", ha="center", fontsize=8.5, color="#0e6b4a")
    ax.text(1.5, 3.25, "outside kept beyond", ha="center", fontsize=8.5, color=BLUE)
    ax.text(10.5, 3.25, "outside kept beyond", ha="center", fontsize=8.5, color=BLUE)
    for xs, ys in [(S, 1.9), (S, 3.0), (E, 1.9), (E, 3.0)]:
        ax.plot([xs], [ys], "s", color=RED, ms=5)
    ax.text(6, -0.9, "red squares = the four joint edges (fillet goes on one of each pair)", ha="center", fontsize=8, color=RED)
    fig.tight_layout()
    save(fig, OUT + "join_flow.png")


if __name__ == "__main__":
    fig_join()
