"""Create offset profile figures from the LIVE test wires ("Offset profile tests" studio, BeamBuilder Testbed).
Data: data/offset_profile_tests.json, sampled read-only with
    PYTHONPATH=. MSYS_NO_PATHCONV=1 python docs/tooling/sample_feature_edges.py f61d2c000ab2d1240776342e \
        5b11f323ab31b04cba8b36ef e18678532ec07b057b372dbd docs/explainers/driven_offset/img/src/data/offset_profile_tests.json \
        "T1 *" "T2 *" "T3 *" "T4 *" "T5 *" "T6 Regions*" "T7 Points*" "T8 *" "T9 *" "T10 *" "T11 *" "T12 *"
Run from the repo root: python docs/explainers/driven_offset/img/src/offset_profile_figs.py
Profile coordinates: X = station, Y = width offset, Z = height offset (mm). Plots show width against station.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import plt, np, save, BLUE, ORANGE, AQUA, MAGENTA, VIOLET, RED, INK, INK2, MUTED  # noqa: E402

OUT = "docs/explainers/driven_offset/img/"
DATA = json.load(open(os.path.join(HERE, "data", "offset_profile_tests.json")))
PIECE = [BLUE, ORANGE, AQUA, VIOLET]


def case(prefix):
    return [v for k, v in DATA.items() if k.startswith(prefix + " ")][0]


def plot_case(ax, prefix, channel=1, by_piece=True, shade=None):
    """Width (channel 1) or height (channel 2) against station; one colour per piece (body), edge ends ticked."""
    edges = case(prefix)
    for e in edges:
        p = np.array(e["pts"])
        p = p[np.argsort(p[:, 0])]
        col = PIECE[e["body"] % len(PIECE)] if by_piece else BLUE
        ax.plot(p[:, 0], p[:, channel], color=col, lw=2.4, solid_capstyle="round")
        ax.plot([p[0, 0], p[-1, 0]], [p[0, channel], p[-1, channel]], "o", ms=3.5, color=INK, zorder=5)
    ax.set_xlabel("station X (mm)")
    ax.set_ylabel(("width" if channel == 1 else "height") + " (mm)")
    if shade:
        for x0, x1 in shade:
            ax.axvspan(x0, x1, color="#eeeae2", zorder=0)


def fig_regions():
    """T1: tip / RSL / tail regions with value jumps -> three pieces, breaks at 0 and 1000."""
    fig, ax = plt.subplots(figsize=(10, 3.0))
    plot_case(ax, "T1")
    for x, lab in ((0, "break (jump) at 0"), (1000, "break at 1000")):
        ax.axvline(x, ls="--", color=MAGENTA, lw=1.2)
        ax.text(x, 3.35, lab, color=MAGENTA, ha="center", fontsize=9.5, fontweight="bold")
    for x, lab in ((-50, "Tip  w 0"), (500, "RSL  w 3"), (1050, "Tail  w 0")):
        ax.text(x, 1.4, lab, ha="center", fontsize=10.5, fontweight="bold", color=INK2)
    ax.set_ylim(-0.4, 3.8)
    ax.set_title("T1: three regions, values jump at their joints -> three pieces (one wire each)")
    save(fig, OUT + "cop_fig01_regions_breaks.png")


def fig_shapes():
    """The four region shapes, each from a live test wire."""
    fig, axs = plt.subplots(1, 4, figsize=(10, 2.8), sharey=False)
    plot_case(axs[0], "T6", by_piece=False)
    axs[0].set_title("Constant (then Linear)\nT6: w 2, then 2 -> 4", fontsize=10)
    plot_case(axs[1], "T2", by_piece=False, shade=[(2000, 2010), (2080, 2100)])
    axs[1].set_title("Smooth, buffers 10 / 20\nT2: w 0 -> 4, C2 at the buffers", fontsize=10)
    plot_case(axs[2], "T8", by_piece=False)
    axs[2].set_title("Quadratic, flat at Start\nT8: w 0 -> 4", fontsize=10)
    plot_case(axs[3], "T9", by_piece=False)
    axs[3].set_title("Quadratic, flat at End\nT9: w 0 -> 4", fontsize=10)
    for a in axs[1:]:
        a.set_ylabel("")
    fig.tight_layout()
    save(fig, OUT + "cop_fig02_shapes.png")


def fig_blend():
    """T3: two regions with a jump at 3100, joined by a G2 / G2 blend 20 mm into each."""
    fig, ax = plt.subplots(figsize=(10, 3.0))
    plot_case(ax, "T3", by_piece=False)
    edges = case("T3")
    mid = [e for e in edges if e["type"] == "SPLINE"][0]
    p = np.array(mid["pts"])
    ax.plot(p[:, 0], p[:, 1], color=MAGENTA, lw=3.2, label="blend (G2 / G2, 20 mm into each region)")
    ax.plot([3100, 3100], [2, 5], ":", color=MUTED, lw=1.2)
    ax.text(3102, 3.4, "without the blend:\na jump -> a break", color=MUTED, fontsize=9)
    ax.text(3040, 0.2, "region A: w 0 -> 2", color=INK2, fontsize=10, fontweight="bold")
    ax.text(3150, 4.3, "region B: w 5", color=INK2, fontsize=10, fontweight="bold")
    ax.legend(loc="upper left")
    ax.set_title("T3: an intersection blend turns the jump into one continuous piece")
    save(fig, OUT + "cop_fig03_blend.png")


def fig_points():
    """T4 smooth points (no overshoot) and T5 a jump at one station (two points) -> two pieces."""
    fig, axs = plt.subplots(1, 2, figsize=(10, 2.9))
    plot_case(axs[0], "T4", by_piece=False)
    for x, w in ((4000, 0), (4050, 3), (4100, 1)):
        axs[0].plot(x, w, "s", ms=7, color=RED, zorder=6)
    axs[0].set_title("T4 Points, Smooth: flat at every point, never overshoots", fontsize=10)
    plot_case(axs[1], "T5")
    for x, w in ((5000, 0), (5050, 0), (5050, 3), (5100, 3)):
        axs[1].plot(x, w, "s", ms=7, color=RED, zorder=6)
    axs[1].axvline(5050, ls="--", color=MAGENTA, lw=1.2)
    axs[1].set_title("T5 Points, Linear: two points at 5050 = a jump -> 2 pieces", fontsize=10)
    axs[1].set_ylabel("")
    fig.tight_layout()
    save(fig, OUT + "cop_fig04_points.png")


def fig_reverse():
    """T10 / T12: regions entered toward -X give the same profile as entered toward +X."""
    fig, axs = plt.subplots(1, 2, figsize=(10, 2.9))
    plot_case(axs[0], "T10", by_piece=False, shade=[(10080, 10100)])
    axs[0].annotate("", xy=(10005, 3.6), xytext=(10095, 3.6), arrowprops=dict(arrowstyle="-|>", color=ORANGE, lw=1.6))
    axs[0].text(10050, 3.8, "entered start 10100 -> end 10000", ha="center", color=ORANGE, fontsize=9.5)
    axs[0].set_ylim(-0.3, 4.4)
    axs[0].set_title("T10 Linear w 0 -> 4, start buffer 20 (held at the START end)", fontsize=10)
    plot_case(axs[1], "T12", by_piece=False)
    axs[1].set_title("T12 two touching regions, both entered toward -X: one piece", fontsize=10)
    axs[1].set_ylabel("")
    fig.tight_layout()
    save(fig, OUT + "cop_fig05_reverse.png")


def fig_deo_step():
    """DEO regions test D3: Driven edge offset with Profile source = Regions, a step at 200 -> two offset bodies."""
    d = json.load(open(os.path.join(HERE, "data", "deo_regions_d3.json")))
    ref = [v for k, v in d.items() if k.startswith("Reference")][0]
    out = [v for k, v in d.items() if k.startswith("D3")][0]
    fig, ax = plt.subplots(figsize=(10, 3.4))
    for e in ref:
        p = np.array(e["pts"])
        ax.plot(p[:, 0], p[:, 1], color=MUTED, lw=1.5, ls="--", label="reference arc R400")
    for e in out:
        p = np.array(e["pts"])
        ax.plot(p[:, 0], p[:, 1], color=PIECE[e["body"]], lw=2.6,
                label="offset body %d: R%.0f (width %d)" % (e["body"] + 1, e["radius"], 3 if e["body"] == 0 else 6))
    ax.set_aspect("equal")
    ax.legend(loc="upper left")
    ax.text(235, 10, "step at station 200:\nthe output splits", color=MAGENTA, fontsize=10, fontweight="bold")
    ax.set_title("DEO regions test D3: regions Low 0..200 w 3 | High 200..400 w 6, typed in Driven edge offset")
    ax.set_xlabel("x (mm)")
    ax.set_ylabel("y (mm)")
    save(fig, OUT + "cop_fig06_deo_step.png")


if __name__ == "__main__":
    fig_deo_step()
    fig_regions()
    fig_shapes()
    fig_blend()
    fig_points()
    fig_reverse()
