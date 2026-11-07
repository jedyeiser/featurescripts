"""Figures for the Create offset profile sheet (schematic, computed from the documented shape formulas;
not Onshape renders). Existing deck figures (docs/explainers/driven_offset/img/cop_fig*.png) are reused in the sheet.
Run from repo root: python docs/sheets/create_offset_profile/figs.py
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import *  # noqa

OUT = "docs/sheets/create_offset_profile/img/"


def smoother(u):
    return 6 * u ** 5 - 15 * u ** 4 + 10 * u ** 3


def fig_table_to_offset():
    fig = plt.figure(figsize=(8.2, 3.6))
    ax = fig.add_axes([0.07, 0.52, 0.90, 0.40])
    # regions: Tip const 0 [0,100]; Ramp smooth 0->3 [100,300]; RSL const 3 [300,800]; Tail linear 3->0 [800,1000] ; gap then region 5
    xa = np.linspace(100, 300, 80); xb = np.linspace(300, 800, 2); xc = np.linspace(800, 1000, 2)
    ax.plot([0, 100], [0, 0], color=BLUE, lw=3)
    ax.plot(xa, 3 * smoother((xa - 100) / 200), color=ORANGE, lw=3)
    ax.plot(xb, [3, 3], color=AQUA, lw=3)
    ax.plot(xc, [3, 0], color=VIOLET, lw=3)
    for x, y, lab, c in ((50, 0.6, "Tip\nConstant", BLUE), (135, 2.2, "Ramp\nSmooth", ORANGE),
                         (550, 3.6, "RSL  Constant", AQUA), (900, 2.1, "Tail\nLinear", VIOLET)):
        ax.text(x, y, lab, ha="center", fontsize=8.5, color=c, fontweight="bold")
    ax.set_ylim(-0.4, 4.6); ax.set_xlim(-20, 1020)
    ax.set_ylabel("width (mm)", fontsize=9); ax.set_xlabel("station X (mm)", fontsize=9)
    ax.set_title("Regions typed in the dialog -> exact profile wire (touching, equal values: one piece)", fontsize=10)
    ax2 = fig.add_axes([0.07, 0.06, 0.90, 0.30]); bare(ax2, equal=False)
    s = np.linspace(0, 1000, 300)
    y = 40 * np.sin(s / 1000 * np.pi * 0.9)
    w = np.where(s < 100, 0, np.where(s < 300, 3 * smoother((s - 100) / 200), np.where(s < 800, 3, 3 * (1000 - s) / 200)))
    dy = np.gradient(y, s); n = np.hypot(1, dy)
    ax2.plot(s, y, color=INK2, lw=2)
    ax2.plot(s - dy / n * w * 6, y + w * 6 / n, color=ORANGE, lw=2.2)
    ax2.set_xlim(-20, 1020); ax2.set_ylim(-12, 85)
    ax2.text(500, y.max() + 28, "orange: the offset Driven edge offset / Driven offset surface build from this profile", ha="center", fontsize=8.3, color=ORANGE, va="bottom")
    ax2.text(5, y[0] - 8, "(width exaggerated x6 in this plan view)", fontsize=7.5, color=MUTED)
    save(fig, OUT + "cop_table_to_offset.png")


if __name__ == "__main__":
    fig_table_to_offset()


