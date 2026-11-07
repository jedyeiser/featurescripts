"""Station geometry sheet figures (schematics; positions / widths are the 4101 test fixture's, y exaggerated).
Run from the repo root:  python docs/sheets/station_geometry/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import plt, np, save, bare, FancyBboxPatch, BLUE, ORANGE, AQUA, RED, INK, INK2, MUTED, VIOLET  # noqa: E402

OUT = "docs/sheets/station_geometry/img/"
TINT = "#f3f5f8"
KX = [145, 515, 885, 1255, 1625]
KW = [116.2, 97.95, 93.26, 105.94, 132.08]
COEF = np.polyfit(KX, KW, 4)


def half_width(x):
    w = np.polyval(COEF, np.clip(x, KX[0], KX[-1])) / 2
    out = np.where(x < KX[0], w * np.sqrt(np.clip(1 - ((KX[0] - x) / 145.0) ** 2, 0, 1)), w)
    return np.where(x > KX[-1], w * np.sqrt(np.clip(1 - ((x - KX[-1]) / 165.0) ** 2, 0, 1)), out)


def box(ax, x, y, w, h, text, ec, fc=TINT, size=8.5, weight="bold", ls="-"):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.05", fc=fc, ec=ec, lw=1.3, ls=ls))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=size, fontweight=weight, color=INK)


def fig_plan():
    fig, ax = plt.subplots(figsize=(10, 3.4))
    bare(ax, equal=False)
    x = np.linspace(0, 1790, 600)
    h = half_width(x)
    ax.fill_between(x, -h, h, color="#eef1f5", zorder=1)
    ax.plot(x, h, color=INK2, lw=1.0, ls=":", zorder=2)
    ax.plot(x, -h, color=INK2, lw=1.0, ls=":", zorder=2)
    # outline wires (what the feature builds)
    ax.plot(x, h, color=ORANGE, lw=2.2, zorder=3)
    ax.plot(x, -h, color=ORANGE, lw=2.2, zorder=3)
    for k, xs in enumerate(KX):
        hh = half_width(np.array([xs]))[0]
        ax.plot([xs, xs], [-hh, hh], color=BLUE, lw=2.2, zorder=4)
        ax.text(xs, hh + 14, "Q_%d" % (k + 1), ha="center", fontsize=8.5, color=BLUE, fontweight="bold")
        ax.text(xs, 0, "%.1f" % KW[k], ha="center", va="center", fontsize=8, color=INK, rotation=90,
                bbox=dict(fc="white", ec="none", pad=1.0), zorder=6)
    # extents + datum
    for xp, name in [(0, "TAIL"), (1790, "TIP")]:
        ax.plot([xp], [0], "s", color=RED, ms=7, zorder=5)
        ax.text(xp, -88, name + "  (extent)", ha="center", fontsize=8.5, color=RED, fontweight="bold")
    ax.plot([0], [0], "o", mfc="white", mec=VIOLET, ms=12, mew=2, zorder=5)
    ax.text(0, 88, "DATUM  x = 0", ha="center", fontsize=8.5, color=VIOLET, fontweight="bold")
    # x dimension
    ax.annotate("", xy=(885, -108), xytext=(0, -108), arrowprops=dict(arrowstyle="<->", color=INK2, lw=1))
    ax.text(442, -124, "x from datum (table column)", ha="center", fontsize=8, color=INK2)
    ax.annotate("", xy=(1700, -75), xytext=(1700, 75), arrowprops=dict(arrowstyle="<->", color=BLUE, lw=0))
    ax.set_xlim(-130, 1930)
    ax.set_ylim(-140, 150)
    # legend (text only, no fills)
    ax.text(1900, 138, "outline wires", color=ORANGE, fontsize=8.5, fontweight="bold", ha="right")
    ax.text(1900, 121, "station lines, labelled with the measured width (mm)", color=BLUE, fontsize=8.5, fontweight="bold", ha="right")
    ax.set_title("Plan view built by Station geometry (schematic; widths are the 4101 test part's)", fontsize=10)
    save(fig, OUT + "fig_plan.png")


def fig_struct():
    fig, ax = plt.subplots(figsize=(10, 2.9))
    bare(ax, equal=False)
    ax.set_xlim(0, 10)
    ax.set_ylim(-0.1, 2.9)
    box(ax, 0.05, 0.9, 1.7, 1.1, "Part\n(solid, sheet\nor composite)", INK2)
    box(ax, 0.05, 2.2, 1.7, 0.55, "Station definition", BLUE, "#e6effb", size=8)
    ax.annotate("", xy=(2.45, 1.45), xytext=(1.8, 1.45), arrowprops=dict(arrowstyle="-|>", color=INK2, lw=1.4))
    ax.annotate("", xy=(2.45, 1.9), xytext=(1.8, 2.4), arrowprops=dict(arrowstyle="-|>", color=INK2, lw=1.4))
    # open composite
    ax.add_patch(FancyBboxPatch((2.5, 0.2), 4.9, 2.55, boxstyle="round,pad=0.02,rounding_size=0.06", fc="white", ec=AQUA, lw=1.6, ls="--"))
    ax.text(2.65, 2.55, "<prefix> XY  (open composite; not in BOM; table attribute)", fontsize=7.5, color=AQUA, fontweight="bold", va="center")
    box(ax, 2.7, 1.0, 1.3, 0.9, "the part\n(unchanged)", INK2, size=8, weight="normal")
    ax.add_patch(FancyBboxPatch((4.2, 0.35), 3.0, 1.85, boxstyle="round,pad=0.02,rounding_size=0.05", fc="white", ec=BLUE, lw=1.3))
    ax.text(4.3, 2.0, "<prefix> XY WIRES  (closed)", fontsize=8, color=BLUE, fontweight="bold", va="center")
    for i, t in enumerate(["ST <id>  one line per station", "OUTLINE  wires", "DATUM  point", "TIP / TAIL  end points"]):
        ax.text(4.35, 1.68 - i * 0.33, t, fontsize=7.8, color=INK, va="center")
    # outputs right
    box(ax, 7.9, 1.9, 2.0, 0.8, "<prefix> XY FLAT\n(optional, for DXF)", ORANGE, size=7.8, weight="normal")
    box(ax, 7.9, 0.85, 2.05, 0.8, "REGION sheet (optional;\nstays in the XY composite)", ORANGE, size=7.5, weight="normal", ls="--")
    box(ax, 7.9, 0.0, 2.0, 0.65, "Station table\nreads the attribute", VIOLET, size=7.8, weight="normal")
    save(fig, OUT + "fig_struct.png")


if __name__ == "__main__":
    fig_plan()
    fig_struct()


