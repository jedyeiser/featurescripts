"""Figures for docs/explainers/curve_mapping/curve_mapping_explained.md and the Wrap Curve / Wrap and Loft / Deform /
Offset edges decks.

fig01-fig04 are computed here with the bending map itself on an analytic tip: From = a straight reference, To = a
1200 mm flat followed by an R600 tip arc, references at x = 0 (the worked example of the brief). fig05 / fig06 use
Offset edges test geometry read from example_1's "Offset edges tests" studio (data/oe.json, read-only eval API,
2026-09-26, docs/tooling/sample_feature_edges.py).
usage (repo root): python docs/explainers/curve_mapping/img/src/figs.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

OUT = os.path.join(HERE, "..")
FLAT, R = 1200.0, 600.0
LTO = FLAT + R * np.pi / 2


def to_frame(s):
    """Point and normal of the To path (side view x, z) at arc length s; past the end: osculating circle (same arc)."""
    s = np.asarray(s, dtype=float)
    th = np.clip((s - FLAT) / R, 0, None)
    on_arc = s > FLAT
    x = np.where(on_arc, FLAT + R * np.sin(th), s)
    z = np.where(on_arc, R * (1 - np.cos(th)), 0.0)
    nx = np.where(on_arc, -np.sin(th), 0.0)
    nz = np.where(on_arc, np.cos(th), 1.0)
    return x, z, nx, nz


def wrap(xs, hs):
    """From: straight line z = 0 with reference at x = 0 (s = x, height = z). To reference at s = 0."""
    x, z, nx, nz = to_frame(xs)
    return x + hs * nx, z + hs * nz


def fig01():
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(10.5, 8), gridspec_kw={"height_ratios": [0.7, 1.6]})
    s = np.linspace(0, 1900, 400)
    a1.plot(s, 0 * s, color=INK, lw=2.2, label="From: straight reference (flat layout)")
    for h, col in ((0, BLUE), (20, ORANGE)):
        a1.plot(s[s > 900], h + 0 * s[s > 900], color=col, lw=2, label="source curve at height %d" % h)
    a1.plot(1500, 0, "o", color=RED, ms=6)
    a1.text(1500, -18, "x = 1500", ha="center", color=RED, fontsize=9, va="top")
    a1.plot(0, 0, "s", color=INK, ms=6)
    a1.text(0, 8, "reference", fontsize=9)
    a1.set_ylim(-45, 75)
    a1.set_xlim(-30, 1950)
    a1.legend(loc="upper left", fontsize=8.5, ncol=3)
    a1.set_title("Before: flat")
    tx, tz, _, _ = to_frame(np.linspace(0, LTO, 400))
    a2.plot(tx, tz, color=INK, lw=2.2, label="To: 1200 flat + R600 tip")
    for h, col in ((0, BLUE), (20, ORANGE)):
        ss = np.linspace(900, 1900, 300)
        wx, wz = wrap(ss, h)
        a2.plot(wx, wz, color=col, lw=2)
    px, pz = wrap(1500.0, 0.0)
    a2.plot(px, pz, "o", color=RED, ms=6)
    a2.text(px + 15, pz - 5, "x = 1500 lands 1500 along the profile:\n300 into the arc -> (%.1f, %.1f)" % (px, pz), color=RED, fontsize=9, va="top")
    a2.plot(0, 0, "s", color=INK, ms=6)
    a2.text(1300, 380, "height 20 is on the concave side:\nits length shrinks by (600 - 20) / 600", color=ORANGE, fontsize=9)
    a2.set_aspect("equal")
    a2.set_xlim(-30, 1950)
    a2.set_ylim(-40, 640)
    a2.legend(loc="upper left", fontsize=8.5)
    a2.set_title("After Wrap Curve: arc length along the reference kept, height carried unchanged")
    a2.set_xlabel("x (mm)")
    save(fig, os.path.join(OUT, "fig01_bending_map.png"))


def fig02():
    """Past the end of To: the osculating circle, not a straight line."""
    fig, ax = plt.subplots(figsize=(8.5, 4.6))
    Rt = 200.0
    th = np.linspace(0, 1.0, 100)
    ax.plot(Rt * np.sin(th), Rt * (1 - np.cos(th)), color=INK, lw=2.4, label="To chain: ends on an R200 tip")
    th2 = np.linspace(1.0, 1.0 + 60 / Rt, 40)
    ax.plot(Rt * np.sin(th2), Rt * (1 - np.cos(th2)), color=BLUE, lw=2.2, ls="--", label="past the end: the osculating circle (exact)")
    ex, ez = Rt * np.sin(1.0), Rt * (1 - np.cos(1.0))
    tx, tz = np.cos(1.0), np.sin(1.0)
    d = np.linspace(0, 60, 20)
    ax.plot(ex + d * tx, ez + d * tz, color=MUTED, lw=1.6, ls=":", label="straight extension (off by d^2 / 2R)")
    ax.plot(ex, ez, "o", color=INK, ms=6)
    ax.text(ex + 8, ez - 12, "end of To", fontsize=9)
    ax.set_aspect("equal")
    ax.legend(loc="upper left", fontsize=8.5)
    ax.set_title("A point mapped past the end of the To chain")
    ax.set_xlabel("x (mm)")
    save(fig, os.path.join(OUT, "fig02_overrun.png"))


def fig03():
    """Wrap and Loft: a strip between the wrapped curve and its offsets along the To normal."""
    fig, ax = plt.subplots(figsize=(10, 4.8))
    ss = np.linspace(700, LTO, 300)
    tx, tz, nx, nz = to_frame(ss)
    ax.plot(tx, tz, color=INK, lw=2.2, label="wrapped curve (on the profile)")
    ax.plot(tx + 20 * nx, tz + 20 * nz, color=BLUE, lw=1.8, label="primary offset 20 (along the To normal)")
    ax.plot(tx - 5 * nx, tz - 5 * nz, color=ORANGE, lw=1.8, label="second offset 5 (other side)")
    ax.fill(np.concatenate([tx + 20 * nx, (tx - 5 * nx)[::-1]]), np.concatenate([tz + 20 * nz, (tz - 5 * nz)[::-1]]),
            color=BLUE, alpha=0.12, lw=0)
    for s0 in (900, 1350, 1700):
        x, z, a, b = to_frame(s0)
        arrow(ax, (x, z), (x + 60 * a, z + 60 * b), col=RED, lw=1.2, ms=9)
    ax.text(760, 60, "the lofted strip spans primary to second: 25 wide", color=BLUE, fontsize=9)
    ax.set_aspect("equal")
    ax.set_ylim(-60, 640)
    ax.legend(loc="upper left", fontsize=8.5)
    ax.set_title("Wrap and Loft: offset the wrapped curve along the To normal, loft between the offsets")
    ax.set_xlabel("x (mm)")
    save(fig, os.path.join(OUT, "fig03_wrap_and_loft.png"))


def fig04():
    """Deform: a 100 x 5 block at x 1400..1500 bent onto the tip."""
    fig, ax = plt.subplots(figsize=(10, 4.6))
    tx, tz, _, _ = to_frame(np.linspace(900, LTO, 300))
    ax.plot(tx, tz, color=INK, lw=2, label="To profile")
    xs = np.concatenate([np.linspace(1400, 1500, 50), [1500], np.linspace(1500, 1400, 50), [1400]])
    hs = np.concatenate([np.zeros(50), [5], 5 * np.ones(50), [0]])
    ax.fill(xs, hs - 60, color=MUTED, alpha=0.5, lw=0)
    ax.text(1450, -75, "flat block 100 x 5\n(drawn 60 below)", ha="center", fontsize=9, va="top", color=INK2)
    bx, bz = wrap(xs, hs)
    ax.fill(bx, bz, color=BLUE, alpha=0.6, lw=0)
    top = wrap(np.linspace(1400, 1500, 200), 5 * np.ones(200))
    L_top = np.sum(np.hypot(np.diff(top[0]), np.diff(top[1])))
    ax.text(1335, 95, "bent: bottom 100 (on the profile)\ntop at height 5 = %.1f (concave side)" % L_top, fontsize=9, color=BLUE)
    ax.set_aspect("equal")
    ax.set_xlim(1330, 1560)
    ax.set_ylim(-95, 120)
    ax.set_title("Deform: every vertex, edge and face goes through the same map")
    ax.set_xlabel("x (mm)")
    ax.legend(loc="lower right", fontsize=8.5)
    save(fig, os.path.join(OUT, "fig04_deform.png"))


# ---------------------------------------------------------------- Offset edges
OE = json.load(open(os.path.join(HERE, "data", "oe.json")))


def oe(key, dx=0.0):
    out = []
    for e in OE[key]:
        p = np.array(e["pts"])
        p[:, 0] -= dx
        out.append((e, p))
    return out


def fig05():
    fig, axs = plt.subplots(1, 3, figsize=(13, 3.9), gridspec_kw={"width_ratios": [1.4, 1.1, 1]})
    a1, a2, a3 = axs
    for e, p in oe("OE6 source*", 5000):
        a1.plot(p[:, 0], p[:, 2], color=MUTED, lw=4)
    cols = [BLUE, ORANGE, BLUE]
    for (e, p), c in zip(sorted(oe("OE6 line*", 5000), key=lambda t: t[1][:, 0].min()), cols):
        a1.plot(p[:, 0], p[:, 2], color=c, lw=2.2)
    a1.axvspan(0, 150, color=BLUE, alpha=0.06)
    a1.axvspan(250, 400, color=BLUE, alpha=0.06)
    a1.text(75, 23, "region A: 0", ha="center", fontsize=9, color=BLUE)
    a1.text(325, 23, "region B: 20", ha="center", fontsize=9, color=BLUE)
    a1.text(200, 3, "G1 blend\n10 into each", ha="center", fontsize=8.5, color=ORANGE)
    a1.set_ylim(-4, 28)
    a1.set_xlabel("distance along the line (mm)")
    a1.set_ylabel("offset (mm)")
    a1.set_title("OE6: two regions + a G1 blend", fontsize=10)
    for e, p in oe("OE10 line*", 10000):
        a2.plot(p[:, 0], p[:, 2], color=BLUE, lw=2.2)
    for s, v in ((50, 0), (150, 20), (250, 5)):
        a2.plot(s, v, "o", color=RED, ms=6)
    a2.set_xlabel("distance (mm)")
    a2.set_title("OE10: single region, Smooth through pins\n(50: 0, 150: 20, 250: 5), flat outside", fontsize=10)
    for e, p in oe("OE24 source*", 24000):
        a3.plot(p[:, 0], p[:, 1], color=MUTED, lw=4)
    for e, p in oe("OE24 G0*", 24000):
        a3.plot(p[:, 0], p[:, 1], color=ORANGE if e["type"].startswith("CIRC") else BLUE, lw=2.2)
    a3.text(112, -20, "R10 arc fills\nthe corner", color=ORANGE, fontsize=8.5)
    a3.set_aspect("equal")
    a3.set_xlim(-10, 150)
    a3.set_ylim(-30, 110)
    a3.set_title("OE24: sharp corner, in-plane 10\n(gap filled with an arc)", fontsize=10)
    save(fig, os.path.join(OUT, "fig05_offset_edges.png"))


def fig06():
    t = np.linspace(0, 1, 200)
    fig, ax = plt.subplots(figsize=(8.5, 3.8))
    ax.plot(t, t, color=INK2, lw=2, label="Linear")
    ax.plot(t, t ** 2, color=VIOLET, lw=2, label="Quadratic, zero slope at start")
    ax.plot(t, 6 * t ** 5 - 15 * t ** 4 + 10 * t ** 3, color=BLUE, lw=2, label="Smooth  6t^5 - 15t^4 + 10t^3")
    d0 = 50 / 300
    u = np.clip((t - d0) / (1 - d0), 0, 1)
    ax.plot(t, u, color=ORANGE, lw=1.8, ls="--", label="Linear with a start dwell of 50 of 300 mm")
    ax.set_xlabel("position in the region t")
    ax.set_ylabel("share of the start -> end offset")
    ax.legend(loc="upper left", fontsize=8.5)
    ax.set_title("Offset edges: how an offset changes across a region")
    save(fig, os.path.join(OUT, "fig06_region_types.png"))


if __name__ == "__main__":
    for fn in (fig01, fig02, fig03, fig04, fig05, fig06):
        fn()
