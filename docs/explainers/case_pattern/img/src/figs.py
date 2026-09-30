"""Case pattern v3 figures (diagrams, no live data). Run from the repo root:
    python docs/explainers/case_pattern/img/src/figs.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "..", "..", "..", "tooling"))
from figstyle import plt, save, bare, FancyBboxPatch, FancyArrowPatch, BLUE, ORANGE, AQUA, RED, INK, INK2, MUTED  # noqa: E402

OUT = "docs/explainers/case_pattern/img/"
TINT, EDGE = "#f3f5f8", "#52514e"


def box(ax, x, y, w, h, text, fc=TINT, ec=EDGE, size=10, weight="normal", color=INK, ha="left"):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", fc=fc, ec=ec, lw=1.2))
    tx = x + 0.12 if ha == "left" else x + w / 2
    ax.text(tx, y + h / 2, text, ha=ha, va="center", fontsize=size, fontweight=weight, color=color)


def arrow(ax, p, q, color=INK2, style="-|>", lw=1.4, rad=0.0, ls="-"):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle=style, mutation_scale=12, color=color, lw=lw,
                                 connectionstyle="arc3,rad=%g" % rad, linestyle=ls))


def brace(ax, x, y0, y1, text, color):
    ax.plot([x, x + 0.15, x + 0.15, x], [y0, y0, y1, y1], color=color, lw=1.6)
    ax.text(x + 0.3, (y0 + y1) / 2, text, ha="left", va="center", fontsize=10, fontweight="bold", color=color)


def fig_tree():
    """The feature tree of one template (test T1), and what the Close case lists."""
    fig, ax = plt.subplots(figsize=(10, 4.2))
    bare(ax)
    rows = [
        ("Define case A: inputs #top, #rim; values #bossH, #edgeR", "define"),
        ("Boss: extrude #top by #bossH", "body"),
        ("Rim: fillet #rim 2 mm", "body"),
        ("#bossEdges = edges created by Boss  (query variable)", "body"),
        ("Boss edges: fillet #bossEdges, #edgeR", "body"),
        ("#bossFaces = faces created by Boss  (query variable)", "body"),
        ("Case  B:  top / rim of block B;  25 mm, 3", "case"),
        ("Case  C:  top / rim of block C;  8 mm, 2", "case"),
        ("Close case:  body + Cases B, C;  output bossFaces", "close"),
    ]
    fc = {"define": "#e6effb", "body": TINT, "case": "#fdeee6", "close": "#e3f5ee"}
    ec = {"define": BLUE, "body": EDGE, "case": ORANGE, "close": AQUA}
    h, gap, top = 0.36, 0.08, 4.1
    ys = []
    for k, (text, kind) in enumerate(rows):
        y = top - k * (h + gap)
        ys.append(y)
        box(ax, 0.2, y, 6.0, h, text, fc=fc[kind], ec=ec[kind], size=9.5,
            weight="bold" if kind in ("define", "close", "case") else "normal")
    brace(ax, 6.35, ys[5], ys[1] + h, "the body = Features to repeat\n(case A runs here, in the tree)", INK2)
    brace(ax, 6.35, ys[7], ys[6] + h, "one Case feature per further case", ORANGE)
    # Close case lists the body and the Cases
    ax.text(6.35, ys[8] + h / 2, "runs the body again for B and C;\npublishes #A_, #B_, #C_bossFaces",
            ha="left", va="center", fontsize=10, color=AQUA, fontweight="bold")
    # the Close case lists the body and the Cases: two brackets on the left
    ax.plot([0.2, -0.25, -0.25], [ys[8] + h / 2, ys[8] + h / 2, ys[1] + h / 2], color=AQUA, lw=1.6)
    arrow(ax, (-0.25, ys[1] + h / 2), (0.2, ys[1] + h / 2), color=AQUA)
    arrow(ax, (-0.25, ys[6] + h / 2), (0.2, ys[6] + h / 2), color=AQUA)
    ax.text(-0.35, (ys[1] + ys[8]) / 2 + h / 2, "lists", rotation=90, ha="right", va="center", fontsize=9.5, color=AQUA, fontweight="bold")
    ax.set_xlim(-0.8, 10.2)
    ax.set_ylim(ys[-1] - 0.15, top + h + 0.1)
    save(fig, OUT + "fig03_tree_v3.png")


def fig_references():
    """What a feature in the body may point at, and how each kind behaves per case."""
    fig, axs = plt.subplots(1, 4, figsize=(10, 3.3))
    panels = [
        ("An input\n(Define case)", "#top from Define case;\neach Case gives its own", "per case", BLUE, "OK"),
        ("From before the body,\nclicked", "a tower face, a corner, a plane\n(tests T9a, T13, T14)", "same in every case", INK2, "OK (v3)"),
        ("Made by the body,\nvia a query variable", "#bossEdges = edges\ncreated by Boss", "remapped to each case's copy", AQUA, "OK"),
        ("Made by the body,\nclicked", "Boss's edges picked\nin a later fillet", "stays on case A's copy", RED, "ERROR (T3)"),
    ]
    for ax, (head, example, behaviour, color, verdict) in zip(axs, panels):
        bare(ax)
        ax.set_xlim(0, 1)
        ax.set_ylim(0, 1)
        ax.add_patch(FancyBboxPatch((0.04, 0.05), 0.92, 0.9, boxstyle="round,pad=0.0,rounding_size=0.04",
                                    fc="#ffffff", ec=color, lw=2))
        ax.text(0.5, 0.82, head, ha="center", va="center", fontsize=10.5, fontweight="bold", color=color)
        ax.text(0.5, 0.56, example, ha="center", va="center", fontsize=9.5, color=INK)
        ax.text(0.5, 0.34, behaviour, ha="center", va="center", fontsize=9.5, color=INK2, style="italic")
        ax.text(0.5, 0.15, verdict, ha="center", va="center", fontsize=12, fontweight="bold", color=color)
    fig.subplots_adjust(wspace=0.08, left=0.01, right=0.99, top=0.98, bottom=0.02)
    save(fig, OUT + "fig04_references_v3.png")


if __name__ == "__main__":
    fig_tree()
    fig_references()
