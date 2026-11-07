"""Enclose+ figure (schematic). Run from the repo root: python docs/sheets/enclose_plus/figs.py"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "tooling"))
from figstyle import plt, np, save, bare, BLUE, ORANGE, AQUA, MAGENTA, RED, INK, INK2, MUTED  # noqa: E402

OUT = "docs/sheets/enclose_plus/img/"

x = np.linspace(0, 6, 60)
top = 3.0 + 0.5 * np.sin(x * 0.9)
bot = 0.4 + 0.25 * np.cos(x * 0.7)


def base(ax, title, step):
    bare(ax)
    ax.set_xlim(-1.0, 13.2)
    ax.set_ylim(-1.9, 4.7)
    ax.set_title(title, fontsize=10.5, loc="left")
    ax.text(-0.9, 4.35, step, fontsize=9, color=MUTED)


def half(ax, dx=0.0, flip=1, fill=False, color=BLUE):
    xs = flip * x + dx
    ax.plot(xs, top, color=color, lw=2.6)
    ax.plot(xs, bot, color=color, lw=2.6)
    if fill:
        ax.fill_between(xs, bot, top, color=AQUA, alpha=0.35, lw=0)


def fig_flow():
    fig, axes = plt.subplots(1, 3, figsize=(11, 3.0))
    ax = axes[0]
    base(ax, "1  Inputs", "Surfaces, Caps, Inside point, Mirror plane")
    half(ax)
    ax.plot([0, 0], [-0.8, 3.9], color=MAGENTA, lw=2.4)
    ax.text(0.1, -1.15, "cap (mate connector)", fontsize=7.5, color=MAGENTA)
    ax.plot([6, 6], [-0.8, 3.9], color=ORANGE, lw=2, ls="--")
    ax.text(6.1, 4.0, "mirror plane", fontsize=8, color=ORANGE)
    ax.plot([3], [1.8], "o", color=RED, ms=8)
    ax.text(3.15, 1.35, "inside point", fontsize=8, color=RED)
    ax.text(2.2, 3.9, "surfaces", fontsize=8, color=BLUE)

    ax = axes[1]
    base(ax, "2  Enclose against the plane", "closed volume kept, other pockets dropped")
    half(ax, fill=True)
    ax.plot([0, 0], [bot[0], top[0]], color=MAGENTA, lw=2.4)
    ax.plot([6, 6], [bot[-1], top[-1]], color=ORANGE, lw=2, ls="--")
    ax.plot([3], [1.8], "o", color=RED, ms=8)
    ax.text(2.0, 1.0, "solid", fontsize=9, color="#0e6b4a")
    ax.add_patch(plt.Circle((9.6, 2.0), 0.7, fc="none", ec=MUTED, ls=":", lw=1.4))
    ax.text(9.6, 1.0, "stray pocket\nwithout the point", ha="center", fontsize=7.5, color=MUTED)

    ax = axes[2]
    base(ax, "3  Mirror and unite", "output + surfaceFaces, capFaces, seamEdges")
    half(ax, fill=True)
    half(ax, dx=12.0, flip=-1, fill=True, color=BLUE)
    ax.plot([6, 6], [bot[-1], top[-1]], color=RED, lw=2.6)
    ax.text(6.0, -1.3, "seam edge\n(free-form only)", ha="center", fontsize=7.5, color=RED)
    ax.plot([0, 0], [bot[0], top[0]], color=MAGENTA, lw=2.4)
    ax.plot([12, 12], [bot[0], top[0]], color=MAGENTA, lw=2.4)
    fig.tight_layout()
    save(fig, OUT + "enclose_flow.png")


if __name__ == "__main__":
    fig_flow()
