"""Station definition sheet figures (schematics; positions are the 4101 test fixture's, widths not to scale).
Run from the repo root:  python docs/sheets/station_definition/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import plt, np, save, bare, FancyBboxPatch, BLUE, ORANGE, AQUA, RED, INK, INK2, MUTED, VIOLET  # noqa: E402

OUT = "docs/sheets/station_definition/img/"
TINT = "#f3f5f8"
KX = [145, 515, 885, 1255, 1625]
KW = [116.2, 97.95, 93.26, 105.94, 132.08]
COEF = np.polyfit(KX, KW, 4)


def half_width(x):
    w = np.polyval(COEF, np.clip(x, KX[0], KX[-1])) / 2
    out = np.where(x < KX[0], w * np.sqrt(np.clip(1 - ((KX[0] - x) / 145.0) ** 2, 0, 1)), w)
    out = np.where(x > KX[-1], w * np.sqrt(np.clip(1 - ((x - KX[-1]) / 165.0) ** 2, 0, 1)), out)
    return out


def ski(ax):
    x = np.linspace(0, 1790, 600)
    h = half_width(x)
    ax.fill_between(x, -h, h, color="#eef1f5", zorder=1)
    ax.plot(x, h, color=INK2, lw=1.6, zorder=2)
    ax.plot(x, -h, color=INK2, lw=1.6, zorder=2)
    return x, h


def box(ax, x, y, w, h, text, ec, fc=TINT, size=9, weight="bold"):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.06", fc=fc, ec=ec, lw=1.3))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=size, fontweight=weight, color=INK)


def fig_stations():
    fig, ax = plt.subplots(figsize=(10, 3.3))
    bare(ax, equal=False)
    ski(ax)
    ax.set_title("What a Station definition holds: named places along the part (plan view, schematic)", fontsize=10)
    # single points
    for xp, name in [(0, "TAIL"), (1060, "MRS"), (1790, "TIP")]:
        ax.plot([xp], [0], "o", color=VIOLET, ms=7, zorder=5)
        ax.text(xp, -92 if name != "MRS" else 78, name, ha="center", fontsize=8.5, color=VIOLET, fontweight="bold")
    # N stations between two points
    for k, xs in enumerate(KX):
        h = half_width(np.array([xs]))[0]
        ax.plot([xs, xs], [-h - 6, h + 6], color=BLUE, lw=1.6, zorder=4)
        ax.text(xs, h + 22, "Q_%d" % (k + 1), ha="center", fontsize=8.5, color=BLUE, fontweight="bold")
    ax.plot([145, 1625], [-118, -118], color=BLUE, lw=0.8)
    ax.plot([145, 1625], [-118, -118], "|", color=BLUE, ms=8)
    ax.text(885, -134, "N stations between two points (EDA, SPA), N = 5: ends included, each measures perpendicular to the line",
            ha="center", fontsize=8, color=BLUE)
    ax.annotate("", xy=(1790, 128), xytext=(1500, 128), arrowprops=dict(arrowstyle="-|>", color=INK2))
    ax.text(1480, 128, "line / between entries number toward +X", fontsize=7.5, color=INK2, ha="right", va="center")
    ax.set_xlim(-60, 1850)
    ax.set_ylim(-150, 150)
    save(fig, OUT + "fig_stations.png")


def fig_flow():
    fig, ax = plt.subplots(figsize=(10, 2.1))
    bare(ax, equal=False)
    ax.set_xlim(0, 10)
    ax.set_ylim(0, 2.1)
    box(ax, 0.1, 0.55, 2.6, 1.0, "Station definition\n(resolves picks to\nplain points)", BLUE, "#e6effb", size=8.5)
    ax.annotate("", xy=(3.6, 1.05), xytext=(2.75, 1.05), arrowprops=dict(arrowstyle="-|>", color=INK2, lw=1.4))
    box(ax, 3.6, 0.55, 2.0, 1.0, "station set\n(stationSet/1)\nid, origin, direction", INK2, TINT, size=8, weight="normal")
    ax.annotate("", xy=(6.5, 1.05), xytext=(5.65, 1.05), arrowprops=dict(arrowstyle="-|>", color=INK2, lw=1.4))
    box(ax, 6.5, 1.15, 3.3, 0.7, "Station geometry (picks it)", AQUA, "#e3f5ee", size=8.5)
    box(ax, 6.5, 0.25, 3.3, 0.7, "Export tag (runs Station geometry)", AQUA, "#e3f5ee", size=8.5)
    ax.text(3.6, 0.22, "also: optional # variable", fontsize=7.5, color=INK2)
    save(fig, OUT + "fig_flow.png")


if __name__ == "__main__":
    fig_stations()
    fig_flow()

