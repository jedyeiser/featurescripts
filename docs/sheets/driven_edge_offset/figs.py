"""Figures for the Driven edge offset sheet. Schematic (computed, not from Onshape).
Run from repo root: python docs/sheets/driven_edge_offset/figs.py
"""
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tooling"))
from figstyle import *  # noqa

OUT = "docs/sheets/driven_edge_offset/img/"


def fig_concept():
    """Source chain + profile -> offset wire (plan view), profile below."""
    s = np.linspace(0, 1000, 400)
    # source: gently curved ski-like centreline (x along, y up)
    x = s
    y = 0.00006 * (s - 500) ** 2 * 0.0 + 40 * np.sin(s / 1000 * np.pi * 0.9)
    dy = np.gradient(y, x); n = np.hypot(1, dy); nx, ny = -dy / n, 1 / n
    def prof(s):
        w = np.where(s < 150, s / 150 * 12, np.where(s < 850, 12, (1000 - s) / 150 * 12))
        return w
    w = prof(s)
    fig = plt.figure(figsize=(8.2, 3.6))
    ax = fig.add_axes([0.06, 0.40, 0.92, 0.55]); bare(ax, equal=False)
    ax.plot(x, y, color=INK2, lw=2.2, label="Offset edges (source)")
    ax.plot(x + w * nx, y + w * ny, color=ORANGE, lw=2.4, label="Driven edge offset result")
    for i in range(0, 400, 20):
        ax.plot([x[i], x[i] + w[i] * nx[i]], [y[i], y[i] + w[i] * ny[i]], color=MUTED, lw=0.8)
    ax.legend(loc="upper left", fontsize=8.5, ncol=2)
    ax.set_ylim(-8, 80); ax.set_xlim(-10, 1010)
    ax.set_title("Plan view: the profile says how far to step sideways at each station", fontsize=10)
    ax2 = fig.add_axes([0.10, 0.10, 0.84, 0.22])
    ax2.plot(s, w, color=BLUE, lw=2.4)
    ax2.set_xlabel("profile X = station (Measure along)", fontsize=9)
    ax2.set_ylabel("Y = width", fontsize=9); ax2.set_ylim(-1, 15)
    ax2.set_xlim(-10, 1010)
    ax2.text(500, 3.5, "offset profile (a wire: X station, Y width, Z height)", ha="center", color=BLUE, fontsize=8.5)
    save(fig, OUT + "deo_concept.png")


def biarc(P0, T0, P1, T1):
    v = P1 - P0; t = T0 + T1
    k = 2 * (1 - T0 @ T1)
    d = (-(v @ t) + np.sqrt((v @ t) ** 2 + k * (v @ v))) / k
    J = (P0 + P1 + d * (T0 - T1)) / 2
    return J


def arc_pts(P, T, Q, n=60):
    """Circular arc from P (tangent T) to Q."""
    nrm = np.array([-T[1], T[0]])
    c = (Q - P)
    r = (c @ c) / (2 * (nrm @ c))
    C = P + nrm * r
    a0 = np.arctan2(*(P - C)[::-1]); a1 = np.arctan2(*(Q - C)[::-1])
    da = (a1 - a0 + np.pi) % (2 * np.pi) - np.pi
    a = a0 + np.linspace(0, da, n)
    return np.c_[C[0] + abs(r) * np.cos(a), C[1] + abs(r) * np.sin(a)]


def fig_arcs():
    """Source arc, offset growing along it: Spline vs Biarc fit."""
    R = 100.0
    th = np.linspace(0, np.radians(70), 200)
    wv = 12 * th / th[-1]
    r = R - wv     # offset towards the centre, growing
    fig, axs = plt.subplots(1, 3, figsize=(8.2, 2.7))
    for ax in axs:
        bare(ax)
        ax.plot(R * np.cos(th), R * np.sin(th), color=INK2, lw=1.8)
    off = np.c_[r * np.cos(th), r * np.sin(th)]
    axs[0].plot(*off.T, color=ORANGE, lw=2.4)
    axs[0].set_title("Source: an arc,\noffset grows along it", fontsize=9)
    axs[1].plot(*off.T, color=BLUE, lw=2.4)
    axs[1].set_title("Spline (default)\none spline per run", fontsize=9)
    P0, P1 = off[0], off[-1]
    T0 = np.array([-np.sin(th[0]), np.cos(th[0])])
    # true curve tangent from the data
    d = np.gradient(off, axis=0)
    T0 = d[0] / np.hypot(*d[0]); T1 = d[-1] / np.hypot(*d[-1])
    J = biarc(P0, T0, P1, T1)
    TJ = None
    a1 = arc_pts(P0, T0, J); a2 = arc_pts(P1, -T1, J)
    axs[2].plot(*a1.T, color=GREEN, lw=2.4); axs[2].plot(*a2.T, color=VIOLET, lw=2.4)
    axs[2].plot(*off.T, color=MUTED, lw=0.9, ls="--")
    axs[2].plot(*J, "o", color=INK, ms=4)
    axs[2].set_title("Biarc fit\ntangent arcs (2 or more per run)", fontsize=9)
    for ax in axs:
        ax.set_xlim(0, 105); ax.set_ylim(-3, 100)
    save(fig, OUT + "deo_arc_fit.png")


if __name__ == "__main__":
    fig_concept()
    fig_arcs()

