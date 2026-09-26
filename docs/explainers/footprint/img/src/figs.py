"""Figures for docs/explainers/footprint/footprint_explained.md and the footprint decks.

Data in data/ was read from the footprint document's "Footprint tests" studio (read-only eval API, 2026-09-26,
docs/tooling/sample_feature_edges.py): fixture sketches FX1 (S14), FX2, FX6, FX11; results AR3, IF3, IF6, SF1, SF2;
AF1's key-point sketch. Widths are exaggerated in every plan view (Y x YS).
usage (repo root): python docs/explainers/footprint/img/src/figs.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

OUT = os.path.join(HERE, "..")
D = json.load(open(os.path.join(HERE, "data", "fpt.json")))
D.update(json.load(open(os.path.join(HERE, "data", "fpt2.json"))))
YS = 5.0


def edges(key, dx=0.0):
    out = []
    for e in D[key]:
        p = np.array(e["pts"])[:, :2].copy()
        p[:, 0] -= dx
        if p[0, 0] > p[-1, 0]:
            p = p[::-1]
        out.append({"type": e["type"], "radius": e["radius"], "p": p})
    return sorted(out, key=lambda e: e["p"][:, 0].min())


def plot_chain(ax, es, cols=(BLUE, ORANGE), lw=2.2, ys=YS, ticks=True, **kw):
    for i, e in enumerate(es):
        ax.plot(e["p"][:, 0], e["p"][:, 1] * ys, color=cols[i % len(cols)], lw=lw, solid_capstyle="butt", **kw)
        if ticks and i:
            x, y = e["p"][0]
            ax.plot([x, x], [y * ys - 6, y * ys + 6], color=INK, lw=1)


def yticks_true(ax, ys=YS, step=20):
    lo, hi = ax.get_ylim()
    t = np.arange(0, hi / ys + 1, step)
    ax.set_yticks(t * ys)
    ax.set_yticklabels(["%g" % v for v in t])


# ---------------------------------------------------------------- fig01 vocabulary
def fig01():
    es = edges("FX1*")
    fig, ax = plt.subplots(figsize=(11, 4.6))
    plot_chain(ax, es, cols=(INK2,), ticks=False)
    ax.axhline(0, color=MUTED, lw=1)
    wy = 63.124 * YS
    pts = {"FCP": (-750, 61.999), "ACP": (750, 61.999), "widest FB": (-667.857, 63.124), "widest AB": (667.857, 63.124),
           "inflection": (-550, 60.808), "inflection ": (550, 60.808), "waist": (0, 50)}
    for t, (x, y) in pts.items():
        col = RED if t.startswith("widest") else (VIOLET if t.startswith("infl") else (BLUE if t == "waist" else INK))
        ax.plot(x, y * YS, "o", color=col, ms=6, zorder=5)
        dy = -34 if t in ("waist", "FCP", "ACP", "inflection", "inflection ") else 16
        ax.text(x, y * YS + dy, t.strip(), ha="center", fontsize=9, color=col)
    ax.plot([-667.857, 667.857], [wy, wy], color=RED, lw=1, ls="--")
    ax.text(0, wy + 10, "taper = angle of this line (0 here: symmetric)", ha="center", fontsize=8.5, color=RED)
    for x in (-750, 750):
        ax.axvline(x, color=MUTED, lw=1, ls=":")
    ax.annotate("", xy=(750, -40), xytext=(-750, -40), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text(0, -70, "RSL: running surface length, FCP -> ACP = 1500 mm   (MRS = its middle)", ha="center", fontsize=9)
    ax.annotate("", xy=(-924.567, -40), xytext=(-750, -40), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text(-837, -70, "tip 174.6", ha="center", fontsize=9)
    ax.annotate("", xy=(924.567, -40), xytext=(750, -40), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text(837, -70, "tail 174.6", ha="center", fontsize=9)
    ax.text(-300, 140, "forebody (FB)", ha="center", color=INK2)
    ax.text(300, 140, "aftbody (AB)", ha="center", color=INK2)
    ax.set_ylim(-90, 400)
    yticks_true(ax)
    ax.set_xlabel("x (mm)")
    ax.set_ylabel("half-width y (mm), x %g" % YS)
    ax.set_title("One side of a footprint (fixture S14: R14 m core, R3 m reverse flanks, R0.3 m to the tips)")
    save(fig, os.path.join(OUT, "fig01_vocabulary.png"))


# ---------------------------------------------------------------- fig02 integrate
def fig02():
    prof = edges("IF3*")
    out = edges("IF3 R14*")
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(10.5, 6.4), sharex=True)
    for e in prof:
        a1.plot(e["p"][:, 0], e["p"][:, 1], color=BLUE if e["p"][0, 1] > 0 else ORANGE, lw=2.6)
    a1.axhline(0, color=INK2, lw=1)
    a1.text(0, 150, "y = 140 mm -> R 14 m (concave core)", ha="center", color=BLUE, fontsize=9)
    a1.text(-650, -42, "y = -30 -> R 3 m\n(reverse flank)", ha="center", color=ORANGE, fontsize=9, va="top")
    a1.text(650, -42, "y = -30 -> R 3 m", ha="center", color=ORANGE, fontsize=9, va="top")
    a1.set_ylim(-80, 190)
    a1.set_ylabel("sketch y (mm)")
    a1.set_title("Input: the radius profile, drawn as sketch lines (10 mm of y = 1 m of radius)")
    for e in out:
        col = BLUE if e["radius"] > 10000 else ORANGE
        a2.plot(e["p"][:, 0], e["p"][:, 1], color=col, lw=2.6)
        m = e["p"][len(e["p"]) // 2]
        a2.text(m[0], m[1] + 1.2, "arc R %.0f" % e["radius"], ha="center", fontsize=9, color=col)
    a2.plot(0, 100, "o", color=INK)
    a2.text(0, 98.6, "waist: width 200 at x = 0", ha="center", va="top", fontsize=9)
    a2.set_ylim(97, 116)
    a2.set_ylabel("half-width y (mm)")
    a2.set_xlabel("x (mm)")
    a2.set_title("Output (IF3): three exact arcs, tangent at the joints, placed so the waist lands where asked")
    save(fig, os.path.join(OUT, "fig02_integrate.png"))


# ---------------------------------------------------------------- fig03 average vs natural radius
def fig03():
    es = edges("FX2*", dx=4000)
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(10.5, 6.2), sharex=True, gridspec_kw={"height_ratios": [1.2, 1]})
    plot_chain(a1, es, cols=(INK2,), ticks=False, ys=1)
    xi, yi = 550, 60.454
    for x in (-xi, xi):
        a1.plot(x, yi, "o", color=VIOLET, ms=6)
    a1.plot(0, 50, "o", color=BLUE, ms=6)
    # circle through the inflections and the waist
    r = (xi ** 2 + (yi - 50) ** 2) / (2 * (yi - 50))
    xx = np.linspace(-700, 700, 300)
    a1.plot(xx, 50 + r - np.sqrt(r ** 2 - xx ** 2), color=RED, ls="--", lw=1.4)
    a1.text(0, 62, "natural radius (inflection): circle through inflection - waist - inflection = %.2f m" % (r / 1000),
            ha="center", color=RED, fontsize=9)
    a1.set_xlim(-940, 940)
    a1.set_ylim(40, 66)
    a1.set_ylabel("half-width (mm)")
    a1.set_title("Fixture FX2: R14 m core (x +-400), R25 m to the inflections (+-550), reverse flanks beyond the inflections")
    xs = np.linspace(-550, 550, 400)
    R = np.where(abs(xs) <= 400, 14.0, 25.0)
    a2.plot(xs, R, color=BLUE, lw=2)
    a2.fill_between(xs, 0, R, color=BLUE, alpha=0.08)
    a2.axhline(16.97, color=BLUE, ls="--", lw=1.2)
    a2.text(-540, 17.6, "average radius = mean of R at 200 stations between the inflections = 16.97 m", color=BLUE, fontsize=9)
    a2.set_ylim(0, 30)
    a2.set_ylabel("radius (m)")
    a2.set_xlabel("x (mm)")
    save(fig, os.path.join(OUT, "fig03_average_natural.png"))


# ---------------------------------------------------------------- fig04 arc fit
def fig04():
    src = edges("FX11*", dx=32000)
    out = edges("AR3*", dx=32000)
    fig, ax = plt.subplots(figsize=(10.5, 4.2))
    ys = 12
    for e in src:
        ax.plot(e["p"][:, 0], e["p"][:, 1] * ys, color=MUTED, lw=7, alpha=0.5, solid_capstyle="butt")
    for i, e in enumerate(out):
        col = (BLUE, ORANGE)[i % 2]
        ax.plot(e["p"][:, 0], e["p"][:, 1] * ys, color=col, lw=2.2)
        x, y = e["p"][0]
        ax.plot([x, x], [y * ys - 8, y * ys + 8], color=INK, lw=1)
        m = e["p"][len(e["p"]) // 2]
        ax.text(m[0], m[1] * ys + (16 if i % 2 else -26), "R%.1f" % (e["radius"] / 1000), ha="center", fontsize=8, color=col)
    ax.text(10, 63 * ys, "grey: the input spline (FX11, through 11 points of S14)\ncolours: Arc fit's 10 arcs, R in m; within 0.008 mm both ways",
            fontsize=9, va="top")
    ax.set_xlim(-10, 760)
    ax.set_ylim(46 * ys, 66 * ys)
    t = np.arange(48, 66, 4)
    ax.set_yticks(t * ys)
    ax.set_yticklabels(["%g" % v for v in t])
    ax.set_xlabel("x from the waist (mm)")
    ax.set_ylabel("half-width (mm), x %d" % ys)
    ax.set_title("Arc fit (AR3): a freeform S-curve becomes arcs, each reporting a radius")
    save(fig, os.path.join(OUT, "fig04_arc_fit.png"))


# ---------------------------------------------------------------- fig05 scale
def scale_panel(ax, r, o, title, legend=False):
    for e in r:
        ax.plot(e["p"][:, 0], e["p"][:, 1] * YS, color=MUTED, lw=4, alpha=0.6, solid_capstyle="butt")
    for e in o:
        col = BLUE if e["type"].startswith("CIRC") else ORANGE
        ax.plot(e["p"][:, 0], e["p"][:, 1] * YS, color=col, lw=2)
        if e["type"].startswith("CIRC") and e["radius"] > 3000:
            m = e["p"][len(e["p"]) // 2]
            ax.text(m[0], m[1] * YS - 36, "R%.2f m" % (e["radius"] / 1000), ha="center", fontsize=8.5, color=col)
    for x in (-750, 750):
        ax.axvline(x, color=MUTED, lw=1, ls=":")
    for x in (-825, 825):
        ax.axvline(x, color=BLUE, lw=1, ls=":")
    ax.set_ylim(-20, 380)
    yticks_true(ax)
    ax.set_ylabel("half-width (mm), x %g" % YS)
    ax.set_title(title)
    if legend:
        ax.text(-1000, 340, "grey: reference (RSL 1500)   blue: arcs (refit as a tangent chain)   orange: splines   new RSL 1650",
                fontsize=8.5)


def fig05():
    ref = edges("FX1*")
    out = [e for e in edges("SF1*") if e["p"][:, 1].mean() > 0]
    ref6 = edges("FX6 sketch*", dx=20000)
    out6 = [e for e in edges("SF2*", dx=20000) if e["p"][:, 1].mean() > 0]
    t1 = "Accordion (SF1): x stretched 1.1 x about the contacts, widths kept"
    t2 = "Keep taper, pin ACP (SF2, asymmetric sidecut): accordion, then rotate to restore the taper"
    note = "an arc stretched in x is an ellipse:\nrefit as arcs through the moved joints (R14 -> R16.94)"
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(10.5, 6.6), sharex=True)
    scale_panel(a1, ref, out, t1, legend=True)
    a1.text(0, 150, note, ha="center", fontsize=8.5, color=BLUE)
    scale_panel(a2, ref6, out6, t2)
    a2.set_xlabel("x (mm)")
    save(fig, os.path.join(OUT, "fig05_scale.png"))
    # each mode on its own, for the deck's one-example-per-slide pages
    for r, o, t, name, extra in ((ref, out, t1, "fig05a_accordion.png", True), (ref6, out6, t2, "fig05b_keep_taper.png", False)):
        fig, ax = plt.subplots(figsize=(10.5, 3.6))
        scale_panel(ax, r, o, t, legend=True)
        if extra:
            ax.text(0, 150, note, ha="center", fontsize=8.5, color=BLUE)
        ax.set_xlabel("x (mm)")
        save(fig, os.path.join(OUT, name))


# ---------------------------------------------------------------- fig06 points
def fig06():
    es = edges("FX1*")
    p = np.vstack([e["p"] for e in es])
    s = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(p, axis=0).T))])
    fig, ax = plt.subplots(figsize=(11, 3.8))
    ax.plot(p[:, 0], p[:, 1] * YS, color=INK2, lw=1.2)

    def region(x0, x1, n, col, label):
        m = (p[:, 0] >= x0 - 1e-6) & (p[:, 0] <= x1 + 1e-6)
        q, t = p[m], s[m]
        tt = np.linspace(t[0], t[-1], n)
        xs, ys = np.interp(tt, t, q[:, 0]), np.interp(tt, t, q[:, 1])
        ax.plot(xs, ys * YS, "o", color=col, ms=3.2)
        ax.text((x0 + x1) / 2, 360, label, ha="center", color=col, fontsize=9)

    region(-924.567, -750, 23, RED, "tip: 23 points\nrelative to FCP")
    region(-750, 750, 80, BLUE, "RSL: 80 points, relative to MRS (x = 0)")
    region(750, 924.567, 23, GREEN, "tail: 23 points\nrelative to ACP")
    for x, t in ((-750, "FCP"), (0, "MRS"), (750, "ACP")):
        ax.axvline(x, color=MUTED, lw=1, ls=":")
        ax.text(x, -40, t, ha="center", fontsize=9)
    ax.set_ylim(-60, 420)
    yticks_true(ax)
    ax.set_xlabel("x (mm)")
    ax.set_ylabel("half-width (mm), x %g" % YS)
    ax.set_title("Generate Footprint Points (GP1): equal arc-length spacing within each region")
    save(fig, os.path.join(OUT, "fig06_points.png"))


if __name__ == "__main__":
    for f in (fig01, fig02, fig03, fig04, fig05, fig06):
        f()
