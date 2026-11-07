"""Referenced part figures (cross-section schematics). Run from the repo root:
python docs/sheets/referenced_part/figs.py"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, np, save, bare, BLUE, ORANGE, AQUA, MAGENTA, RED, INK2, MUTED, GRID  # noqa: E402

OUT = "docs/sheets/referenced_part/img/"
x = np.linspace(0, 10, 80)
prof = 1.2 + 0.35 * np.sin(x * 0.7)


def setup(ax, title):
    bare(ax)
    ax.set_xlim(-1.5, 11.5)
    ax.set_ylim(-1.3, 5.0)
    ax.set_title(title, fontsize=10.5, loc="left")


def fig_modes():
    fig, axes = plt.subplots(1, 2, figsize=(11, 3.6))
    # THICKENED
    ax = axes[0]
    setup(ax, "Thickened: offset 1, thickness 2")
    ax.plot(x, prof, color=GRID, lw=2.5, ls="--")
    ax.text(10.1, prof[-1] - 0.2, "profile", fontsize=8, color=MUTED)
    off = prof + 1.0
    ax.plot(x, off, color=AQUA, lw=2.8)
    ax.text(10.1, off[-1] + 0.05, "offset profile\n(profileFaces)", fontsize=8, color="#0e6b4a", va="bottom")
    opp = off + 2.0
    ax.plot(x, opp, color=RED, lw=2.8)
    ax.text(10.1, opp[-1] + 0.2, "oppositeFaces", fontsize=8, color=RED)
    xs = (x > 0.8) & (x < 9.2)
    ax.fill_between(x[xs], off[xs], opp[xs], color=AQUA, alpha=0.30, lw=0)
    ax.plot([0.8, 0.8], [off[xs][0], opp[xs][0]], color=BLUE, lw=3)
    ax.plot([9.2, 9.2], [off[xs][-1], opp[xs][-1]], color=BLUE, lw=3)
    ax.text(0.7, 4.75, "periphery cut (sideFaces)", fontsize=8, color=BLUE)
    ax.plot([5], [4.0], "o", color=ORANGE, ms=9, zorder=5)
    ax.text(5.2, 4.1, "reference point", fontsize=8.5, color=ORANGE)
    ax.annotate("", xy=(5.0, off[40]), xytext=(5.0, prof[40]), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text(5.15, (off[40] + prof[40]) / 2 - 0.05, "offset", fontsize=8, color=INK2)
    ax.annotate("", xy=(2.5, opp[20]), xytext=(2.5, off[20]), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text(2.65, (off[20] + opp[20]) / 2, "thickness", fontsize=8, color=INK2)
    # CONSTRAINED
    ax = axes[1]
    setup(ax, "Constrained: top, bottom, periphery enclosed")
    bot = 0.2 + 0.2 * np.cos(x * 0.8)
    top = 3.2 + 0.4 * np.sin(x * 0.6)
    ax.plot(x, bot, color=GREEN if False else AQUA, lw=2.8)
    ax.plot(x, top, color=RED, lw=2.8)
    ax.text(10.1, top[-1], "topFaces", fontsize=8, color=RED)
    ax.text(10.1, bot[-1] - 0.3, "bottomFaces", fontsize=8, color="#0e6b4a")
    xs = (x > 1.0) & (x < 9.0)
    ax.fill_between(x[xs], bot[xs], top[xs], color=AQUA, alpha=0.30, lw=0)
    ax.plot([1.0, 1.0], [bot[xs][0], top[xs][0]], color=BLUE, lw=3)
    ax.plot([9.0, 9.0], [bot[xs][-1], top[xs][-1]], color=BLUE, lw=3)
    ax.text(1.0, 4.6, "periphery (sideFaces)", fontsize=8, color=BLUE)
    ax.plot([7.4, 7.4], [-0.6, 4.2], color=MAGENTA, lw=2, ls="--")
    ax.text(7.5, -0.95, "cap 1: part keeps the point's side", fontsize=8, color=MAGENTA)
    ax.plot([3.5], [1.8], "o", color=ORANGE, ms=9, zorder=5)
    ax.text(3.7, 1.5, "reference point\n(inside the part)", fontsize=8.5, color=ORANGE)
    fig.tight_layout()
    save(fig, OUT + "modes.png")


if __name__ == "__main__":
    fig_modes()
