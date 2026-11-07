"""Figures for the Driven offset surface sheet (schematic cross-sections, not Onshape renders).
Run from repo root: python docs/sheets/driven_offset_surface/figs.py
Cross-section: horizontal = width axis W, vertical = height axis H, at one station.
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import *  # noqa

OUT = "docs/sheets/driven_offset_surface/img/"


def dot(ax, p, col, lab, dx=0.25, dy=-0.9, ha="left"):
    ax.plot(*p, "o", color=col, ms=6, zorder=5)
    ax.text(p[0] + dx, p[1] + dy, lab, color=col, fontsize=8, ha=ha)


def fig_modes():
    fig, axs = plt.subplots(1, 3, figsize=(8.2, 2.4))
    S = np.array([0.0, 0.0])
    for ax in axs:
        bare(ax)
        ax.set_xlim(-3, 17); ax.set_ylim(-2.2, 8.5)
        ax.axhline(-3.4, color=GRID, lw=0)
        dot(ax, S, INK2, "seed edge", dx=-0.4, ha="right")
    # 1 ruled
    ax = axs[0]
    O = np.array([6.0, 1.5])
    dot(ax, O, ORANGE, "driven offset", dx=-0.3, dy=-1.6)
    ax.plot([O[0], O[0] + 7], [O[1], O[1]], color=BLUE, lw=5, alpha=0.35, solid_capstyle="butt")
    arrow(ax, (O[0], O[1] + 0.1), (O[0] + 7, O[1] + 0.1), col=BLUE)
    ax.text(O[0] + 3.5, O[1] + 1.0, "Distance", ha="center", color=BLUE, fontsize=8)
    ax.set_title("Ruled from offset\n(Rule along Width)", fontsize=9)
    # 2 connected
    ax = axs[1]
    O = np.array([10.0, 5.0])
    dot(ax, O, ORANGE, "driven offset", dx=0.3, dy=0.3)
    ax.plot([S[0], O[0]], [S[1], O[1]], color=BLUE, lw=5, alpha=0.35, solid_capstyle="butt")
    ax.set_title("Connect source to offset\n(sheet from seed to offset)", fontsize=9)
    # 3 loft
    ax = axs[2]
    O1 = np.array([5.0, 2.0]); O2 = np.array([10.0, 6.0]); O3 = np.array([15.0, 3.0])
    for k, (o, c) in enumerate(zip((O1, O2, O3), (ORANGE, ORANGE, ORANGE))):
        dot(ax, o, c, "offset %d" % (k + 1), dx=0.0, dy=(0.6 if k == 1 else -1.5), ha="center")
    ax.plot([O1[0], O2[0], O3[0]], [O1[1], O2[1], O3[1]], color=BLUE, lw=5, alpha=0.35)
    ax.plot([O1[0], O2[0], O3[0]], [O1[1], O2[1], O3[1]], color=BLUE, lw=1)
    ax.set_title("Loft between profiles\n(ruled patches, or Blend through)", fontsize=9)
    for ax in axs:
        ax.text(17, -2.0, "W ->  (H up)", fontsize=7.5, color=MUTED, ha="right")
    fig.text(0.5, -0.02, "Section at one station. Blue = the sheet the feature builds; the same happens at every station along the edges.",
             ha="center", fontsize=7.5, color=INK2)
    save(fig, OUT + "dos_modes.png")


def fig_runs():
    """Why 'Break output at' matters: faces per run."""
    fig, axs = plt.subplots(2, 1, figsize=(8.2, 2.6), sharex=True)
    fig.subplots_adjust(hspace=0.55)
    s = np.linspace(0, 100, 200)
    edges = [0, 18, 40, 63, 85, 100]
    for ax, mode in zip(axs, ("Every source edge (default): one face per source edge",
                              "Corners and offset breaks: one face per smooth stretch")):
        ax.set_yticks([]); ax.grid(False); ax.set_ylim(0, 1)
        for sp in ax.spines.values():
            sp.set_visible(False)
        ax.set_title(mode, fontsize=9, loc="left")
    cols = [BLUE, ORANGE, AQUA, VIOLET, MAGENTA]
    for i in range(5):
        axs[0].fill_between([edges[i], edges[i + 1]], 0, 1, color=cols[i], alpha=0.55, lw=0)
        axs[0].plot([edges[i]] * 2, [0, 1], color="white", lw=2)
    axs[1].fill_between([0, 63], 0, 1, color=BLUE, alpha=0.55, lw=0)
    axs[1].fill_between([63, 100], 0, 1, color=ORANGE, alpha=0.55, lw=0)
    axs[1].text(31, 0.5, "tangent-continuous edges run together", ha="center", va="center", fontsize=8, color="white")
    axs[1].text(81, 0.5, "after a corner / profile break", ha="center", va="center", fontsize=8, color="white")
    axs[1].set_xlabel("station along the seed edges", fontsize=8.5)
    save(fig, OUT + "dos_break_modes.png")


if __name__ == "__main__":
    fig_modes()
    fig_runs()



