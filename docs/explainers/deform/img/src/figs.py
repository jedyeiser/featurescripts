"""Figures for docs/explainers/deform/deform_explained.md (round 2).

Every figure is about 7 in wide and every text is >= 9 pt, so it is >= 8.4 pt when the PDF prints it at 6.5 in.
img/shot_before_after.png is a screenshot taken by hand and is not touched here.
usage (repo root): python docs/explainers/deform/img/src/figs.py
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402
from matplotlib.patches import Polygon as Poly  # noqa: E402

OUT = os.path.join(HERE, "..")
DPI = 200
FS = 10.5         # body text in figures (pt)
FSB = 11.5       # titles
W = 7.0           # figure width (in)
LBLUE, LORANGE, LGREEN = "#cfe3f8", "#fbd9c9", "#c8ecdc"


def save2(fig, name):
    p = os.path.join(OUT, name)
    fig.savefig(p, dpi=DPI, bbox_inches="tight", pad_inches=0.06)
    plt.close(fig)
    print("wrote", p)


def txt(ax, x, y, s, **kw):
    kw.setdefault("fontsize", FS)
    return ax.text(x, y, s, **kw)


# ---------------------------------------------------------------- fig01
def fig01():
    R, L, H = 600.0, 100.0, 5.0
    fig, (a, b) = plt.subplots(1, 2, figsize=(W, 2.5))
    for ax in (a, b):
        bare(ax)
    # left: flat
    a.plot([-25, 125], [0, 0], color=INK, lw=2.4)
    a.add_patch(Poly([[0, 0], [L, 0], [L, H], [0, H]], closed=True, fc=LBLUE, ec=BLUE, lw=1.6))
    a.plot(0, 0, "s", color=RED, ms=7)
    txt(a, 0, -5, "reference", color=RED, ha="center", va="top")
    a.annotate("", xy=(L, -13), xytext=(0, -13), arrowprops=dict(arrowstyle="<|-|>", color=INK2, lw=1.2))
    txt(a, 50, -16, "100 mm on the chain", ha="center", va="top")
    txt(a, 50, 17, "flat 100 × 5 mm part", ha="center", va="bottom", color=BLUE)
    a.set_title("From: straight chain", fontsize=FSB, fontweight="bold", loc="left")
    a.set_xlim(-30, 130)
    a.set_ylim(-34, 44)

    # right: bent
    th = np.linspace(-0.04, L / R + 0.04, 80)
    b.plot(R * np.sin(th), R * (1 - np.cos(th)), color=INK, lw=2.4)
    t = np.linspace(0, L / R, 80)
    outer = np.stack([R * np.sin(t), R * (1 - np.cos(t))], 1)
    inner = np.stack([(R - H) * np.sin(t), R - (R - H) * np.cos(t)], 1)
    b.add_patch(Poly(np.vstack([outer, inner[::-1]]), closed=True, fc=LBLUE, ec=BLUE, lw=1.6))
    b.plot(0, 0, "s", color=RED, ms=7)
    txt(b, 0, -5, "reference", color=RED, ha="center", va="top")
    b.annotate("100 mm on the chain", xy=(outer[40, 0], outer[40, 1] - 1), xytext=(40, -22), fontsize=FS,
               ha="center", va="top", arrowprops=dict(arrowstyle="-", color=INK2, lw=1.0))
    b.annotate("99.2 mm, 5 mm toward the centre\n(100 × 595/600)", xy=(inner[45, 0], inner[45, 1] + 0.5), xytext=(62, 22),
               fontsize=FS, ha="center", va="bottom", color=ORANGE,
               arrowprops=dict(arrowstyle="-", color=ORANGE, lw=1.0))
    b.set_title("To: R = 600 mm arc", fontsize=FSB, fontweight="bold", loc="left")
    b.set_xlim(-30, 130)
    b.set_ylim(-34, 54)
    a.set_ylim(-34, 54)
    for ax in (a, b):
        ax.set_aspect("equal")
    fig.suptitle("Deform: a flat part is carried onto the profile", fontsize=12.5, fontweight="bold", y=1.03)
    fig.tight_layout()
    save2(fig, "fig01_bending_map.png")


# ---------------------------------------------------------------- fig02
def fig02():
    fig, ax = plt.subplots(figsize=(W, 3.0))
    bare(ax)
    x = np.linspace(-430, 430, 6000)
    y = 55 * np.sin(x / 150.0)
    ds = np.hypot(np.diff(x), np.diff(y))
    s = np.concatenate([[0], np.cumsum(ds)])
    xref = -200.0
    s -= np.interp(xref, x, s)
    ax.plot(x, y, color=INK, lw=3, zorder=2)
    tx, ty = np.gradient(x), np.gradient(y)
    nr = np.hypot(tx, ty)
    tx, ty = tx / nr, ty / nr
    nx, ny = -ty, tx
    L = 62
    xs_f = [-400, xref, 0, 200, 400]
    base = -112
    ax.annotate("", xy=(425, base), xytext=(-425, base), arrowprops=dict(arrowstyle="-|>", color=MUTED, lw=1.4))
    txt(ax, 445, base + 10, "arc length s (mm)", va="bottom", ha="right", color=INK2, fontsize=FS)
    for xf in xs_f:
        i = int(np.argmin(abs(x - xf)))
        px, py = x[i], y[i]
        arrow(ax, (px, py), (px + L * tx[i], py + L * ty[i]), col=BLUE, lw=2.4, ms=13, zorder=4)
        arrow(ax, (px, py), (px + L * nx[i], py + L * ny[i]), col=ORANGE, lw=2.4, ms=13, zorder=4)
        txt(ax, px + (L + 12) * tx[i], py + (L + 12) * ty[i] - 7, "T", color=BLUE, fontsize=13, fontweight="bold", ha="center")
        txt(ax, px + (L + 14) * nx[i], py + (L + 14) * ny[i] + 3, "N", color=ORANGE, fontsize=13, fontweight="bold", ha="center")
        ax.add_patch(Circle((px, py), 12, fc="white", ec=GREEN, lw=2, zorder=5))
        ax.plot(px, py, ".", color=GREEN, ms=7, zorder=6)
        txt(ax, px - 26, py - 33, "B", color=GREEN, fontsize=13, fontweight="bold", ha="center")
        ax.plot([px, px], [base - 4, base + 4], color=MUTED, lw=1.4)
        sv = float(s[i])
        lab = "s = 0\nreference" if xf == xref else "s = %+.0f" % sv
        lab = lab.replace("-", "−")
        txt(ax, px, base - 14, lab, ha="center", va="top", color=RED if xf == xref else INK2, fontsize=FS,
            fontweight="bold" if xf == xref else "normal")
    i0 = int(np.argmin(abs(x - xref)))
    ax.plot(x[i0], y[i0], "s", color=RED, ms=9, zorder=7)
    ax.plot(0, 0, "o", mfc="white", mec=INK, ms=7, zorder=7)
    txt(ax, 0, -62, "inflection", ha="center", fontsize=FS, color=INK2)
    txt(ax, 445, 176, "N points away from the centre of the bend after the inflection", ha="right", fontsize=FS, color="#b8470f", fontweight="bold")
    txt(ax, -435, 205, "T tangent (along)    N up    B sideways (dot in circle = out of the page)", ha="left", fontsize=FS, color=INK2)
    ax.set_xlim(-440, 450)
    ax.set_ylim(-175, 215)
    save2(fig, "fig02_frames.png")


# ---------------------------------------------------------------- fig03
def fig03():
    pr = lambda x, n, bb: (x + 0.5 * bb, n + 0.35 * bb)
    n_, b_, s_ = 55.0, 80.0, 170.0
    R = 260.0
    fig, (a, b) = plt.subplots(1, 2, figsize=(W, 2.7))
    for ax in (a, b):
        bare(ax)

    def drawP(ax, F, N, D, cb, sym):
        """F foot, N unit 'up' (screen), D screen offset of one unit of b."""
        A = (F[0] + n_ * N[0], F[1] + n_ * N[1])
        G = (F[0] + b_ * D[0], F[1] + b_ * D[1])
        P = (A[0] + b_ * D[0], A[1] + b_ * D[1])
        arrow(ax, F, A, col=ORANGE, lw=2.4, ms=11)
        arrow(ax, A, P, col=GREEN, lw=2.4, ms=11)
        ax.plot([F[0], G[0]], [F[1], G[1]], color=MUTED, lw=1.1, ls="--")
        ax.plot([G[0], P[0]], [G[1], P[1]], color=MUTED, lw=1.1, ls="--")
        ax.plot(*F, "o", color=INK, ms=6)
        ax.plot(*P, "o", color=RED, ms=10, zorder=6)
        txt(ax, P[0] + 9, P[1] + 5, sym, color=RED, fontsize=14, fontweight="bold")
        mx, my = (F[0] + A[0]) / 2, (F[1] + A[1]) / 2
        txt(ax, mx - 7 * N[1] * -1 - 22 if False else mx - 18, my - 2, "n", color=ORANGE, fontsize=13, fontweight="bold", ha="right")
        txt(ax, (A[0] + P[0]) / 2 + 2, (A[1] + P[1]) / 2 + 9, "b", color=GREEN, fontsize=13, fontweight="bold", ha="center")
        return F, A, P

    D = (0.5, 0.35)
    # left
    a.plot([-30, 330], [0, 0], color=INK, lw=3)
    a.plot(0, 0, "s", color=RED, ms=8)
    txt(a, 0, -14, "reference", color=RED, ha="center", va="top")
    drawP(a, (s_, 0), (0, 1), D, GREEN, "P")
    a.annotate("", xy=(s_, -42), xytext=(0, -42), arrowprops=dict(arrowstyle="<|-|>", color=INK, lw=1.6))
    txt(a, s_ / 2, -48, "s", ha="center", va="top", fontsize=13, fontweight="bold")
    txt(a, s_ + 8, -14, "foot", ha="left", va="top")
    a.set_title("From: straight chain", fontsize=FSB, fontweight="bold", loc="left")
    txt(a, -30, 150, "(t = 0: P is beside its foot)", fontsize=FS, color=INK2)
    a.set_xlim(-35, 330)
    a.set_ylim(-70, 160)
    # right
    th = np.linspace(-0.12, 1.05, 120)
    b.plot(R * np.sin(th), R * (1 - np.cos(th)), color=INK, lw=3)
    b.plot(0, 0, "s", color=RED, ms=8)
    txt(b, 0, -14, "reference", color=RED, ha="center", va="top")
    t = s_ / R
    F = (R * np.sin(t), R * (1 - np.cos(t)))
    N = (-np.sin(t), np.cos(t))
    drawP(b, F, N, D, GREEN, "P′")
    # equal s: arc dimension outside the chain
    tt = np.linspace(0, t, 40)
    r2 = R + 55
    ax_, ay_ = r2 * np.sin(tt), R - r2 * np.cos(tt)
    b.plot(ax_, ay_, color=INK, lw=1.6)
    b.annotate("", xy=(ax_[-1], ay_[-1]), xytext=(ax_[-3], ay_[-3]), arrowprops=dict(arrowstyle="-|>", color=INK, lw=1.6))
    b.annotate("", xy=(ax_[0], ay_[0]), xytext=(ax_[2], ay_[2]), arrowprops=dict(arrowstyle="-|>", color=INK, lw=1.6))
    txt(b, ax_[20] + 4, ay_[20] - 20, "s (the same)", ha="center", va="top", fontsize=13, fontweight="bold")
    txt(b, F[0] + 8, F[1] - 8, "foot", ha="left", va="top")
    b.set_title("To: curved chain", fontsize=FSB, fontweight="bold", loc="left")
    txt(b, -30, 150, "same s, same n, same b", fontsize=FS, color=INK2)
    b.set_xlim(-35, 330)
    b.set_ylim(-70, 160)
    for ax in (a, b):
        ax.set_aspect("equal")
    fig.text(0.5, 0.0,
             "Example: a 100 mm block, 5 mm high, on R = 600 mm. The layer 5 mm toward the centre is 100 × 595/600 = 99.2 mm long.",
             ha="center", va="bottom", fontsize=FS, color=INK2)
    fig.tight_layout(rect=(0, 0.06, 1, 1))
    save2(fig, "fig03_point_map.png")


# ---------------------------------------------------------------- fig04
def fig04():
    Wd, H, Rb = 100.0, 30.0, 85.0
    outline = np.array([[0, 0], [Wd, 0], [Wd, H], [60, H], [60, H - 14], [38, H - 14], [38, H], [0, H]])

    def bend(p):
        p = np.asarray(p, float)
        th = p[..., 0] / Rb
        r = Rb - p[..., 1]
        return np.stack([r * np.sin(th), Rb - r * np.cos(th)], axis=-1)

    def poly_bent(pts, n=14):
        out = []
        for i in range(len(pts)):
            p, q = pts[i], pts[(i + 1) % len(pts)]
            t = np.linspace(0, 1, n)[:, None]
            out.append(bend(p + (q - p) * t))
        return np.vstack(out)

    def lines(ax, us, vs, col, lw, tf, **kw):
        for u in us:
            vv = np.linspace(0, H, 40)
            q = tf(np.stack([u + 0 * vv, vv], -1))
            ax.plot(q[:, 0], q[:, 1], color=col, lw=lw, **kw)
        for v in vs:
            uu = np.linspace(0, Wd, 60)
            q = tf(np.stack([uu, v + 0 * uu], -1))
            ax.plot(q[:, 0], q[:, 1], color=col, lw=lw, **kw)

    ident = lambda p: p
    us, vs = np.linspace(0, Wd, 9), np.linspace(0, H, 4)
    G = np.array([[u, v] for u in us for v in vs])
    B = bend(G)
    fig, axs = plt.subplots(2, 3, figsize=(W, 4.4))
    axs = axs.ravel()
    for ax in axs:
        bare(ax)
    cap = dict(fontsize=FS, color=INK, ha="center", va="top")
    ttl = dict(fontsize=FSB, fontweight="bold")
    # 1
    ax = axs[0]
    o = np.vstack([outline, outline[:1]])
    ax.plot(o[:, 0], o[:, 1], color=INK, lw=2)
    ax.plot(G[:, 0], G[:, 1], "o", color=BLUE, ms=5)
    ax.set_title("1  Sample", **ttl)
    ax.text(50, -6, "a (u, v) grid on the\nflat face; notch = hole\nin the outline", **cap)
    ax.set_xlim(-6, 106)
    ax.set_ylim(-40, 36)
    # 2
    ax = axs[1]
    ax.plot(B[:, 0], B[:, 1], "o", color=ORANGE, ms=5)
    ax.set_title("2  Map", **ttl)
    # 3
    ax = axs[2]
    lines(ax, us, vs, BLUE, 1.3, bend)
    ax.plot(B[:, 0], B[:, 1], "o", color=ORANGE, ms=3.5)
    ax.set_title("3  Fit", **ttl)
    # 4
    ax = axs[3]
    lines(ax, us, vs, BLUE, 0.9, bend, alpha=0.55)
    us2 = np.array([(us[i] + us[i + 1]) / 2 for i in (2, 3, 4, 5)])
    vs2 = np.array([(vs[j] + vs[j + 1]) / 2 for j in range(3)])
    lines(ax, us2, vs2, ORANGE, 1.4, bend)
    ax.set_title("4  Refine", **ttl)
    # 5
    ax = axs[4]
    pb = poly_bent(outline)
    ax.add_patch(Poly(pb, closed=True, fc=LBLUE, ec=INK, lw=2))
    lines(ax, us, vs, BLUE, 0.6, bend, alpha=0.4)
    ax.set_title("5  Trim", **ttl)
    allp = np.vstack([B, pb])
    xl = (allp[:, 0].min() - 6, allp[:, 0].max() + 6)
    yl = (allp[:, 1].min() - 36, allp[:, 1].max() + 8)
    for ax, c in zip(axs[1:5], ["each grid point carried\nthrough the map", "a smooth surface passes\nexactly through the points",
                                "more samples where it\nmisses the exact map", "cut to the mapped outline\n(notch kept)"]):
        ax.set_xlim(*xl)
        ax.set_ylim(*yl)
        ax.text(sum(xl) / 2, allp[:, 1].min() - 6, c, **cap)
    for ax in axs[:5]:
        ax.set_aspect("equal")
    # key
    ax = axs[5]
    ax.set_xlim(0, 1)
    ax.set_ylim(0, 1)
    ax.plot(0.05, 0.82, "o", color=BLUE, ms=6)
    ax.text(0.12, 0.82, "sample on the flat face", va="center", fontsize=FS)
    ax.plot(0.05, 0.62, "o", color=ORANGE, ms=6)
    ax.text(0.12, 0.62, "the same sample, mapped", va="center", fontsize=FS)
    ax.plot([0.0, 0.1], [0.42, 0.42], color=BLUE, lw=2)
    ax.text(0.12, 0.42, "fitted surface (isolines)", va="center", fontsize=FS)
    ax.plot([0.0, 0.1], [0.22, 0.22], color=ORANGE, lw=2)
    ax.text(0.12, 0.22, "samples added by Refine", va="center", fontsize=FS)
    fig.tight_layout(h_pad=1.2)
    save2(fig, "fig04_face_pipeline.png")


# ---------------------------------------------------------------- fig05
def fig05():
    fig, axs = plt.subplots(1, 3, figsize=(W, 3.6))
    for ax in axs:
        bare(ax)
        ax.set_xlim(-6, 126)
        ax.set_ylim(-52, 78)
    ys = np.linspace(0, 60, 40)
    wob = lambda y, ph: 3 * np.sin(y / 9.0 + ph)
    xa, xb, gap = 48.0, 72.0, 3.6

    def quad(xl, xr, ph1, ph2):
        return np.vstack([np.stack([xl + wob(ys, ph1), ys], -1), np.stack([xr + wob(ys, ph2), ys], -1)[::-1]])

    A = quad(0, xa, 0, 1.0)
    Bf = quad(xa + gap, xb, 1.0, 2.0)
    C = quad(xb + gap, 120, 2.0, 0.5)
    lab = dict(fontsize=14, fontweight="bold", ha="center", va="center")
    cap = dict(fontsize=FS, color=INK, ha="center", va="top")
    # 1
    ax = axs[0]
    for P, col in ((A, LBLUE), (Bf, LORANGE), (C, LBLUE)):
        ax.add_patch(Poly(P, closed=True, fc=col, ec=INK, lw=1.4))
    for xx, ph in ((xa, 1.0), (xb, 2.0)):
        ax.add_patch(Poly(np.vstack([np.stack([xx + wob(ys, ph), ys], -1), np.stack([xx + gap + wob(ys, ph), ys], -1)[::-1]]),
                          closed=True, fc=RED, ec="none"))
    for x0, s in ((24, "A"), (62, "B"), (96, "C")):
        ax.text(x0, 30, s, **lab)
    ax.set_title("1  Fitted separately", fontsize=FSB, fontweight="bold")
    ax.annotate("gap shown ×1000\n(really a few µm)", xy=(xa + 1.6, 10), xytext=(60, -8), fontsize=FS + 0.5, color=RED,
                fontweight="bold", ha="center", va="top", arrowprops=dict(arrowstyle="-|>", color=RED, lw=1.4))
    # 2
    ax = axs[1]
    ax.add_patch(Poly(A, closed=True, fc=LBLUE, ec=INK, lw=1.4))
    ax.add_patch(Poly(C, closed=True, fc=LBLUE, ec=INK, lw=1.4))
    ax.add_patch(Poly(Bf, closed=True, fc="none", ec=ORANGE, lw=1.8, ls="--"))
    ax.text(24, 30, "A", **lab)
    ax.text(96, 30, "C", **lab)
    ax.text(62, 30, "hole", fontsize=FS, color=ORANGE, ha="center", va="center")
    ax.text(62, -6, "the smaller face B\nis taken out", **cap)
    ax.set_title("2  Lift out B", fontsize=FSB, fontweight="bold")
    # 3
    ax = axs[2]
    Bn = np.vstack([np.stack([xa + wob(ys, 1.0), ys], -1), np.stack([xb + gap + wob(ys, 2.0), ys], -1)[::-1]])
    ax.add_patch(Poly(A, closed=True, fc=LBLUE, ec=INK, lw=1.4))
    ax.add_patch(Poly(C, closed=True, fc=LBLUE, ec=INK, lw=1.4))
    ax.add_patch(Poly(Bn, closed=True, fc=LGREEN, ec=INK, lw=1.4))
    ax.add_patch(Poly(Bf, closed=True, fc="none", ec=ORANGE, lw=1.2, ls="--"))
    ax.text(24, 30, "A", **lab)
    ax.text(96, 30, "C", **lab)
    ax.text(62 + 1.6, 30, "B′", **lab)
    ax.text(62, -6, "B′ meets the exact\nedges of A and C;\nit moves by at most the\nSeam repair limit", **cap)
    ax.set_title("3  Refill as B′", fontsize=FSB, fontweight="bold")
    fig.tight_layout()
    save2(fig, "fig05_seam_repair.png")


# ---------------------------------------------------------------- fig07
def fig07():
    R = 150.0
    hs = [0, 10, 20, 30, 40]
    cols = [INK, BLUE, AQUA, ORANGE, RED]
    fig, (a, b) = plt.subplots(1, 2, figsize=(W, 3.4), gridspec_kw={"width_ratios": [1.05, 1]})
    bare(a)
    half = 0.36
    th = np.linspace(-half, half, 80)
    for h, c in zip(hs, cols):
        r = R - h
        a.plot(r * np.sin(th), R - r * np.cos(th), color=c, lw=2.6 if h == 0 else 2.2)
        x1, y1 = r * np.sin(half), R - r * np.cos(half)
        yl = y1 + (h - 20) * 0.9
        a.plot([x1, 62], [y1, yl], color=c, lw=0.9, ls=":")
        a.text(65, yl, "h = %d mm:  × %.2f" % (h, r / R), color=c, fontsize=FS, va="center", fontweight="bold")
    # fan to the centre
    for sg in (-1, 1):
        a.plot([sg * R * np.sin(half), 0], [R - R * np.cos(half), R], color=MUTED, lw=1.2, ls="--")
    a.plot(0, R, "o", color=RED, ms=9, zorder=6)
    a.text(8, R + 2, "h = R = 150 mm:\nlength zero, the fold", fontsize=FS, color=RED, va="center", fontweight="bold")
    a.text(0, R - 44, "the part lives here\n(0 to 40 mm)", fontsize=FS, color=INK2, ha="center", va="top")
    a.text(-80, 6, "chain\n(h = 0)", fontsize=FS, color=INK, ha="left", va="top")
    a.set_xlim(-85, 150)
    a.set_ylim(-18, R + 22)
    a.set_aspect("equal")
    a.set_title("Layers on the inside of an R = 150 bend", fontsize=FSB, fontweight="bold", loc="left")
    # right: factor vs height
    b.set_xlim(-8, 165)
    b.set_ylim(-0.12, 1.12)
    b.plot([0, R], [1, 0], color=INK, lw=2.4)
    b.axvspan(0, 40, color=LBLUE, alpha=0.8, lw=0)
    for h in hs:
        b.plot(h, (R - h) / R, "o", color=RED if h == 40 else BLUE, ms=7, zorder=5)
        b.text(h + 4, (R - h) / R + 0.015, "%.2f" % ((R - h) / R), fontsize=FS, ha="left", va="center")
    b.axvline(R, color=RED, ls="--", lw=1.6)
    b.text(R - 4, 0.55, "h = R: length zero\n(fold)", color=RED, fontsize=FS, ha="right", va="center", fontweight="bold")
    b.text(20, 0.06, "the 40 mm part", fontsize=FS, color=BLUE, ha="center")
    b.axhline(0, color=MUTED, lw=0.8)
    b.set_xlabel("height h toward the centre (mm)", fontsize=FS)
    b.set_ylabel("length factor (R − h)/R", fontsize=FS)
    b.tick_params(labelsize=FS)
    b.set_title("Length falls in a straight line", fontsize=FSB, fontweight="bold", loc="left")
    b.grid(True, alpha=0.5)
    fig.tight_layout()
    save2(fig, "fig07_layers.png")


# ---------------------------------------------------------------- fig08
def fig08():
    R, hw, T = 260.0, 130.0, 34.0
    fig, axs = plt.subplots(1, 3, figsize=(W, 3.5))
    spec = [  # sign (+1 smile, centre above), side (+1 part above chain), title, centre word, verdict, colour
        (+1, +1, "Profile bends up", "UP", "part is INSIDE the bend:\nlayers shrink", BLUE, LBLUE),
        (-1, +1, "Profile bends down", "DOWN", "part is OUTSIDE the bend:\nlayers stretch", ORANGE, LORANGE),
        (+1, -1, "Flip normal ticked", "UP", "N flipped: part is\nOUTSIDE, layers stretch", ORANGE, LORANGE),
    ]
    for ax, (sign, side, title, cw, verdict, col, fc) in zip(axs, spec):
        bare(ax)
        xs = np.linspace(-hw, hw, 80)
        ax.plot(xs, sign * (R - np.sqrt(R ** 2 - xs ** 2)), color=INK, lw=3)
        xp = np.linspace(-hw * 0.7, hw * 0.7, 60)
        th = np.arcsin(xp / R)
        cn = np.stack([-np.sin(th) * sign, np.cos(th) * sign], 1)      # unit vector toward the centre of the circle
        base = np.stack([xp, sign * (R - R * np.cos(th))], 1)
        top = base + T * (cn if side * sign > 0 else -cn)
        ax.add_patch(Poly(np.vstack([base, top[::-1]]), closed=True, fc=fc, ec=INK, lw=1.4))
        # N arrow at the left end, grey arrow (toward the centre) at the right end
        for xe, kind in ((-hw * 0.93, "N"), (hw * 0.93, "C")):
            z0 = sign * (R - np.sqrt(R ** 2 - xe ** 2))
            d = side if kind == "N" else sign
            arrow(ax, (xe, z0), (xe, z0 + 38 * d), col=ORANGE if kind == "N" else INK2, lw=2.2, ms=12)
            if kind == "N":
                ax.text(xe + 10, z0 + 20 * d, "N", fontsize=13, fontweight="bold", color=ORANGE, ha="left", va="center")
        ax.set_title(title, fontsize=FSB, fontweight="bold", pad=6)
        ax.text(0, -74, "grey arrow: toward\nthe centre (%s)" % cw, fontsize=FS, color=INK, ha="center", va="top")
        ax.text(0, -132, verdict, fontsize=FS, color=col, ha="center", va="top", fontweight="bold")
        ax.set_xlim(-hw - 8, hw + 8)
        ax.set_ylim(-190, 95)
        ax.set_aspect("equal")
    fig.tight_layout()
    save2(fig, "fig08_flip.png")

# ---------------------------------------------------------------- annotated screenshot
def shot():
    from PIL import Image, ImageDraw, ImageFont
    im = Image.open(os.path.join(OUT, "shot_before_after.png")).convert("RGB")
    d0 = ImageDraw.Draw(im)
    d0.polygon([(0, 0), (110, 0), (0, 94)], fill=(255, 255, 255))      # stray plane edge, top left
    im = im.resize((im.width * 2, im.height * 2), Image.LANCZOS)
    padt, padb = 70, 80
    can = Image.new("RGB", (im.width, im.height + padt + padb), (255, 255, 255))
    can.paste(im, (0, padt))
    d = ImageDraw.Draw(can)
    f = ImageFont.truetype(r"C:\Windows\Fonts\segoeuib.ttf", 30)

    def label(xy, text, col, tips):
        d.text(xy, text, font=f, fill=col)
        w = d.textlength(text, font=f)
        sx = xy[0] + w / 2
        sy = xy[1] + (40 if xy[1] < padt + 10 else -4)
        for tx, ty in tips:
            d.line([(sx, sy), (tx, ty + padt)], fill=col, width=3)
            vx, vy = tx - sx, ty + padt - sy
            n = (vx * vx + vy * vy) ** 0.5
            ux, uy = vx / n, vy / n
            px, py = -uy, ux
            d.polygon([(tx, ty + padt), (tx - 16 * ux + 7 * px, ty + padt - 16 * uy + 7 * py), (tx - 16 * ux - 7 * px, ty + padt - 16 * uy - 7 * py)], fill=col)

    label((250, 8), "deformed copies", (20, 50, 130), [(380, 120), (900, 100)])
    label((330, can.height - 70), "flat source boxes", (30, 30, 30), [(330, 270), (760, 420)])
    can.save(os.path.join(OUT, "shot_before_after_annotated.png"))
    print("wrote shot_before_after_annotated.png")


# ---------------------------------------------------------------- fig09
def fig09():
    R = 150.0
    hs = [0, 40, 80, 120, 150, 200]
    cols = [INK, BLUE, AQUA, YELLOW, "#b0306a", RED]
    fig, ax = plt.subplots(figsize=(W, 4.6))
    bare(ax)
    half = 0.36
    th = np.linspace(-half, half, 80)
    for h, c in zip(hs, cols):
        r = R - h
        x, y = r * np.sin(th), R - r * np.cos(th)
        if h == 150:
            ax.plot(0, R, "o", color=c, ms=11, zorder=6)
        else:
            ax.plot(x, y, color=c, lw=2.6, ls="--" if h == 200 else "-")
            xe, ye = x[-1], y[-1]
            ax.plot([xe, 78], [ye, ye], color=c, lw=0.9, ls=":")
        fac = (R - h) / R
        lab = {0: "h = 0 mm: the chain, \u00d7 1.00", 150: "h = R = 150 mm: length zero (a point)",
               200: "h = 200 mm: \u00d7 \u22120.33, past R: inside-out"}.get(h, "h = %d mm: \u00d7 %.2f" % (h, fac))
        yl = (R - r * np.cos(half)) if h != 150 else R
        ax.text(81, yl, lab, color=c if h not in (0, 120) else (INK if h == 0 else "#a06a00"), fontsize=FS, fontweight="bold", va="center")
    for sg in (-1, 1):
        ax.plot([sg * R * np.sin(half), 0], [R - R * np.cos(half), R], color=MUTED, lw=1.2, ls="--")
    ax.annotate("the 200 mm layer\nlands on the other side\nof the centre,\nupside down", xy=(-26, 195), xytext=(-155, 180), fontsize=FS, color=RED,
                fontweight="bold", ha="left", va="top", arrowprops=dict(arrowstyle="-|>", color=RED, lw=1.4))
    ax.text(-150, -12, "layers up to h = R are squeezed more and more", fontsize=FS, color=INK, ha="left", va="top")
    ax.set_xlim(-155, 235)
    ax.set_ylim(-40, 215)
    ax.set_aspect("equal")
    ax.set_title("A part 200 mm tall on the inside of an R = 150 mm bend (section)", fontsize=FSB, fontweight="bold", loc="left")
    save2(fig, "fig09_fold.png")

# ---------------------------------------------------------------- equation
def eq():
    fig = plt.figure(figsize=(7.4, 0.5))
    fig.text(0.5, 0.5,
             r"$s' = s_{\mathrm{To,ref}} + (s - s_{\mathrm{From,ref}}),\quad P' = O_{\mathrm{To}}(s') + n\,N_{\mathrm{To}}(s') + b\,B_{\mathrm{To}}(s') + t\,T_{\mathrm{To}}(s')$",
             ha="center", va="center", fontsize=13)
    save2(fig, "eq_map.png")


if __name__ == "__main__":
    for f in (shot, fig02, fig01, fig03, fig04, fig05, fig07, fig08, fig09, eq):
        f()
