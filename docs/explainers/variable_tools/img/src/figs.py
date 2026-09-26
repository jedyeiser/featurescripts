"""Concept figures for variable_tools_explained.md and case_pattern_explained.md (diagrams, no Onshape data).
usage: python docs/explainers/variable_tools/img/src/figs.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: E402,F403

OUT_VT = os.path.dirname(HERE)
OUT_CP = os.path.join(HERE, "..", "..", "..", "case_pattern", "img")


def box(ax, x, y, w, h, text, fc, ec=INK2, fs=9, bold=False, tc=INK):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.02,rounding_size=0.08", fc=fc, ec=ec, lw=1.2))
    ax.text(x + w / 2, y + h / 2, text, ha="center", va="center", fontsize=fs, fontweight="bold" if bold else "normal", color=tc)


# ---------------------------------------------------------------- VT fig01: producers -> Extract variables -> names
def vt_fig01():
    fig, ax = plt.subplots(figsize=(12, 4.6))
    bare(ax, equal=False)
    ax.set_xlim(0, 12)
    ax.set_ylim(0, 5)
    prods = [("Split+", "output, start, middle, end,\nstartCut, splitEdges ..."),
             ("Mutual Trim+", "output, trimEdges,\nkeptFaces1, keptFaces2"),
             ("any feature (e.g. Extrude)", "output, outputFaces,\noutputEdges (implicit)")]
    for k, (name, keys) in enumerate(prods):
        y = 3.6 - k * 1.45
        box(ax, 0.2, y, 2.6, 1.1, name, "#e8f0fb", bold=True)
        box(ax, 3.1, y, 2.6, 1.1, keys, "#f3f5f8", fs=8)
        ax.text(5.65, y + 0.08, "[ F%d ]" % (k + 1), fontsize=7, color=MUTED, ha="right")
        arrow(ax, (2.8, y + 0.55), (3.1, y + 0.55))
        arrow(ax, (5.7, y + 0.55), (6.4, 2.45), col=MUTED)
    box(ax, 6.4, 1.3, 2.5, 2.3, "Extract variables\n\nSources\nEntries: key -> name\n(Source key, Filtered,\nRegion, Chain end ...)", "#fdf3e1", bold=False, fs=9)
    outs = ["#box_body", "box_middleFaces (query)", "box_startCut (query)", "#box_pieceCount"]
    for k, o in enumerate(outs):
        y = 3.9 - k * 0.9
        box(ax, 9.5, y, 2.3, 0.6, o, "#e6f5ee", fs=8)
        arrow(ax, (8.9, 2.45), (9.5, y + 0.3), col=MUTED)
    ax.text(0.2, 4.85, "Producers publish keys in a hidden map [ Fn ] (embedStandardOutputs)", fontsize=10, fontweight="bold")
    ax.text(9.5, 4.85, "Names later features pick", fontsize=10, fontweight="bold")
    save(fig, os.path.join(OUT_VT, "fig01_producers_consumer.png"))


# ---------------------------------------------------------------- VT fig02: Hold / Track / Evaluate on use
def vt_fig02():
    fig, ax = plt.subplots(figsize=(12, 3.8))
    bare(ax, equal=False)
    ax.set_xlim(0, 12)
    ax.set_ylim(0, 4.4)
    steps = [(1.5, "Extract variables publishes\nedge E (at x 100)"), (6.0, "Move boundary extends\nE by 10 mm (to x 110)")]
    for x, t in steps:
        box(ax, x - 1.2, 3.35, 2.8, 0.85, t, "#f3f5f8", fs=8)
    arrow(ax, (4.3, 3.78), (4.8, 3.78))
    ax.text(8.2, 3.78, "a later feature uses the name", fontsize=9, color=INK2, va="center")
    rows = [("Hold (default)", "what was there at extraction, followed through edits that keep it", KEEP, "the extended edge"),
            ("Track", "also whatever a later split or rebuild makes of it", BLUE, "the extended edge (+ its pieces)"),
            ("Evaluate on use", "the raw query, re-resolved wherever the name is used", ORANGE, "whatever it finds there")]
    for k, (name, desc, col, result) in enumerate(rows):
        y = 2.4 - k * 0.9
        ax.text(0.1, y, name, fontsize=10, fontweight="bold", color=col, va="center")
        ax.text(2.1, y, desc, fontsize=9, color=INK2, va="center")
        ax.text(8.8, y, "-> " + result, fontsize=9, color=col, va="center")
    ax.text(0.1, 0.1, "Verified by the Tracking tests studio (4/4): Hold already follows the extend; modifiedEdges of the Move boundary gives the moved edge + its 2 lengthened neighbours.",
            fontsize=8, color=MUTED)
    save(fig, os.path.join(OUT_VT, "fig02_hold_track_evaluate.png"))


# ---------------------------------------------------------------- CP fig01: the case table
def cp_fig01():
    fig, ax = plt.subplots(figsize=(12, 4.2))
    bare(ax, equal=False)
    ax.set_xlim(0, 12)
    ax.set_ylim(0, 4.6)
    cols = ["case", "#top", "#rim", "#bossH", "#edgeR"]
    rows = [["A (case 1)", "top face A", "rim edges A", "15 mm", "3"],
            ["B", "top face B", "rim edges B", "25 mm", "3"],
            ["C", "top face C", "rim edges C", "8 mm", "2"]]
    x0, w = 0.2, 1.25
    for j, c in enumerate(cols):
        box(ax, x0 + j * w, 3.7, w - 0.05, 0.5, c, "#22262b", tc="#ffffff", fs=9, bold=True)
    for i, r in enumerate(rows):
        for j, v in enumerate(r):
            box(ax, x0 + j * w, 3.05 - i * 0.6, w - 0.05, 0.5, v, "#e8f0fb" if i == 0 else "#f3f5f8", fs=8)
    chain = ["Boss:\nextrude #top by #bossH", "Rim:\nfillet #rim 2 mm", "#bossEdges:\nQV created by Boss", "fillet #bossEdges\nat #edgeR"]
    for k, t in enumerate(chain):
        box(ax, 6.8 + (k % 2) * 2.55, 3.3 - (k // 2) * 1.1, 2.4, 0.85, t, "#fdf3e1", fs=8)
    arrow(ax, (6.5, 2.7), (6.8, 2.7))
    ax.text(6.8, 4.35, "The chain, built once on case A", fontsize=10, fontweight="bold")
    ax.text(0.2, 4.35, "The Case template", fontsize=10, fontweight="bold")
    ax.text(6.8, 0.75, "Case pattern re-runs the chain for B and C: before each case every name is bound to that case's\n"
            "selection or value; afterwards case A's bindings are restored. New bodies get the case name: Boss_A -> Boss_B.",
            fontsize=8.5, color=INK2, va="center")
    save(fig, os.path.join(OUT_CP, "fig01_case_table.png"))


# ---------------------------------------------------------------- CP fig02: clicked vs QV created-by
def cp_fig02():
    fig, axes = plt.subplots(1, 2, figsize=(12, 3.6))
    specs = [("Clicked (or qCreatedBy(makeId(...)))", RED, ["case A: Post", "case B: Post copy", "case C: Post copy"],
              "the fillet stays on case A's edges\n-> test T3 / T4: ERROR"),
             ("Query variable 'created by Post'", KEEP, ["case A: Post", "case B: Post copy", "case C: Post copy"],
              "each case's copy is remapped\n-> test T5: every post filleted")]
    for ax, (title, col, lanes, verdict) in zip(axes, specs):
        bare(ax, equal=False)
        ax.set_xlim(0, 6)
        ax.set_ylim(0, 4)
        for k, lane in enumerate(lanes):
            box(ax, 0.2, 3.0 - k * 1.0, 2.2, 0.7, lane, "#f3f5f8", fs=8)
        if col == RED:
            for k in range(3):
                arrow(ax, (4.0, 1.9), (2.4, 3.35 - k * 1.0 if k == 0 else 3.35), col=col)
        else:
            for k in range(3):
                arrow(ax, (4.0, 3.35 - k * 1.0), (2.4, 3.35 - k * 1.0), col=col)
        box(ax, 4.0, 1.5, 1.9, 0.8, "fillet the\npost's edges", "#fdf3e1", fs=8)
        ax.set_title(title + "\n" + verdict, fontsize=9, color=col)
    save(fig, os.path.join(OUT_CP, "fig02_references_in_list.png"))


if __name__ == "__main__":
    os.makedirs(OUT_CP, exist_ok=True)
    vt_fig01()
    vt_fig02()
    cp_fig01()
    cp_fig02()
