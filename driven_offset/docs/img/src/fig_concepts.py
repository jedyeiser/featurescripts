"""Concept and codebase figures for unwrap_explained.md.
fig01 chart, fig02 offset length, fig03 neutral surface, fig04 handedness, fig05 the test ski (real data),
fig10 Part mode split + cell rebuild, fig12 rigid-piece error (question a), fig13 call graph, fig14 which curve
keeps its length (question b).  usage: python fig_concepts.py   (from this folder)"""
import os
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.patches import FancyBboxPatch, Polygon, FancyArrowPatch

from style import save, bare, DATA, BLUE, ORANGE, AQUA, YELLOW, VIOLET, RED, GREEN, INK, INK2, MUTED, GRID


def arrow(ax, p, q, col=INK2, lw=1.4, style="-|>", ms=12, **kw):
    ax.add_patch(FancyArrowPatch(p, q, arrowstyle=style, mutation_scale=ms, color=col, lw=lw, **kw))


# ------------------------------------------------------------------ fig01: the chart
def fig01():
    fig = plt.figure(figsize=(11, 4.6))
    ax = fig.add_subplot(1, 2, 1, projection="3d")
    s = np.linspace(0, 300, 200)
    # a planar reference in XZ: flat then rising (a tip), parametrised by arc length (approx.)
    th = 0.9 * np.clip((s - 120) / 180, 0, 1) ** 2
    x = np.concatenate([[0], np.cumsum(np.cos(th[:-1]) * np.diff(s))])
    z = np.concatenate([[0], np.cumsum(np.sin(th[:-1]) * np.diff(s))])
    for v in np.linspace(-60, 60, 7):
        ax.plot(x, np.full_like(x, v), z, color=GRID, lw=0.8)
    for k in range(0, 200, 20):
        ax.plot([x[k], x[k]], [-60, 60], [z[k], z[k]], color=GRID, lw=0.8)
    ax.plot(x, 0 * x, z, color=BLUE, lw=2.5)
    k = 160
    tvec = np.array([np.cos(th[k]), 0, np.sin(th[k])])
    nvec = np.array([-np.sin(th[k]), 0, np.cos(th[k])])
    A = np.array([x[k], 0, z[k]])
    v, h = 35.0, 22.0
    B = A + np.array([0, v, 0])
    P = B + h * nvec
    ax.plot(*zip(A, B), color=ORANGE, lw=2)
    ax.plot(*zip(B, P), color=VIOLET, lw=2)
    ax.plot(x[:k + 1], 0 * x[:k + 1], z[:k + 1], color=AQUA, lw=4, alpha=0.8)
    ax.scatter(*P, color=RED, s=30)
    ax.text(*(P + [0, 0, 6]), "P", color=RED)
    ax.text(*(A + [-30, 0, -22]), "A(s)", color=INK2, fontsize=8)
    ax.text(*(0.5 * (A + B) + [0, 0, -16]), "v", color=ORANGE)
    ax.text(*(0.5 * (B + P) + [4, 0, 0]), "h", color=VIOLET)
    
    ax.set_title("Wrapped: reference W swept along its plane normal")
    ax.set_xlabel("X"); ax.set_ylabel("Y"); ax.set_zlabel("Z")
    ax.set_box_aspect((300, 120, 110)); ax.view_init(24, -42)
    ax.set_xticks([]); ax.set_yticks([]); ax.set_zticks([])
    ax2 = fig.add_subplot(1, 2, 2, projection="3d")
    xf = s
    for vv in np.linspace(-60, 60, 7):
        ax2.plot(xf, np.full_like(xf, vv), 0 * xf, color=GRID, lw=0.8)
    for kk in range(0, 200, 20):
        ax2.plot([xf[kk], xf[kk]], [-60, 60], [0, 0], color=GRID, lw=0.8)
    ax2.plot(xf, 0 * xf, 0 * xf, color=BLUE, lw=2.5)
    ax2.plot(xf[:k + 1], 0 * xf[:k + 1], 0 * xf[:k + 1], color=AQUA, lw=4, alpha=0.8)
    Af = np.array([s[k], 0, 0]); Bf = Af + [0, v, 0]; Pf = Bf + [0, 0, h]
    ax2.plot(*zip(Af, Bf), color=ORANGE, lw=2); ax2.plot(*zip(Bf, Pf), color=VIOLET, lw=2)
    ax2.scatter(*Pf, color=RED, s=30); ax2.text(*(Pf + [0, 0, 6]), "P'", color=RED)
    ax2.text(*(0.5 * (Af + Bf) + [0, 0, -14]), "y", color=ORANGE)
    ax2.text(*(0.5 * (Bf + Pf) + [4, 0, 0]), "z", color=VIOLET)
    
    ax2.set_title("Flat: the same (s, v, h), written out as (x, y, z)")
    ax2.set_box_aspect((300, 120, 110)); ax2.view_init(24, -42)
    ax2.set_xticks([]); ax2.set_yticks([]); ax2.set_zticks([])
    ax2.set_zlim(-20, 90)
    fig.text(0.05, 0.08, "LEFT - Green: arc s from the alignment to the foot A(s).  Orange: v = (P - A) . pn across W's plane.\n"
             "Violet: height h along N = pn x t.  (s, v) is a FLAT chart of the swept surface (zero Gaussian curvature).",
             color=INK2, fontsize=8.5)
    fig.text(0.05, -0.01, "RIGHT - x = s - d*theta (= s for d = 0),  y = -v,  z = h, in the unwrapped origin's frame.\n"
             "Same green length; the point keeps its side (y = -v undoes pn = -Y, see fig04).",
             color=INK2, fontsize=8.5)
    save(fig, "fig01_chart.png")


# ------------------------------------------------------------------ fig02: offset curves and s - d*theta
def fig02():
    fig, ax = plt.subplots(figsize=(9, 5))
    bare(ax); ax.set_aspect("equal")
    R, lead, turn = 100.0, 80.0, np.radians(70)
    c = np.array([lead, R])   # centre of the bend (above: the curve turns up, like a tip)
    for d, col, lab in [(0, BLUE, "W itself (d = 0)"), (25, AQUA, "offset d = +25 (towards the centre)"), (-25, ORANGE, "offset d = -25")]:
        xs = [0, lead]
        zs = [d, d]
        ph = np.linspace(0, turn, 60)
        xa = c[0] + (R - d) * np.sin(ph)
        za = c[1] - (R - d) * np.cos(ph)
        ax.plot(np.r_[xs, xa], np.r_[zs, za], color=col, lw=2.4, label=lab)
        Lline, Larc = lead, (R - d) * turn
        ax.text(215, {25: 95, 0: 70, -25: 45}[d], "length %.1f = s - d*theta = %.1f - (%g)(%.3f)" % (Lline + Larc, lead + R * turn, d, turn),
                color=col, fontsize=8, va="center")
    ax.plot(*c, "+", color=INK2, ms=10)
    ax.text(c[0] + 3, c[1] + 3, "centre", color=INK2, fontsize=8)
    ax.annotate("", xy=(c[0] + R * np.sin(turn), c[1] - R * np.cos(turn)), xytext=c,
                arrowprops=dict(arrowstyle="-", color=MUTED, ls="--"))
    ax.annotate("", xy=(lead, 0), xytext=c, arrowprops=dict(arrowstyle="-", color=MUTED, ls="--"))
    ax.text(lead + 12, 60, "theta = %.0f deg\n= %.3f rad" % (np.degrees(turn), turn), color=INK2, fontsize=9)
    ax.text(5, -45, "On the straight lead-in all three have the same length: theta only grows where W curves.\n"
            "The chart stores ONE table theta(s), so any offset's length is known without building the offset curve.",
            color=INK2, fontsize=8.5)
    ax.legend(loc="upper left", fontsize=8.5)
    ax.set_xlim(-5, 400); ax.set_ylim(-55, 130)
    ax.set_title("An offset curve's length differs from W's by d x (turning angle)")
    save(fig, "fig02_offset_length.png")


# ------------------------------------------------------------------ fig03: neutral surface
def fig03():
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(9, 4.6), gridspec_kw={"height_ratios": [1.3, 0.6]})
    for a in (a1, a2):
        bare(a); a.set_aspect("equal")
    R, t, th = 60.0, 14.0, np.radians(80)
    ph = np.linspace(-th / 2, th / 2, 80)
    def arc(r):
        return r * np.sin(ph), -r * np.cos(ph) + R
    xo, zo = arc(R + t / 2); xi, zi = arc(R - t / 2); xm, zm = arc(R)
    a1.fill(np.r_[xo, xi[::-1]], np.r_[zo, zi[::-1]], color="#dfe9f7")
    a1.plot(xo, zo, color=ORANGE, lw=2); a1.plot(xi, zi, color=RED, lw=2); a1.plot(xm, zm, color=BLUE, lw=2.4, ls="--")
    a1.text(xo[-1] + 3, zo[-1], "outer fibre: (R + t/2) theta, STRETCHED", color=ORANGE, fontsize=8.5)
    a1.text(xi[-1] + 3, zi[-1] - 2, "inner fibre: (R - t/2) theta, COMPRESSED", color=RED, fontsize=8.5)
    a1.text(xm[-1] + 3, zm[-1] - 1, "mid-thickness: R theta, unchanged", color=BLUE, fontsize=8.5)
    a1.set_xlim(-60, 130)
    a1.set_title("Bent plate (pure bending, symmetric section): only the neutral surface keeps its length")
    L = R * th
    a2.fill([-L / 2, L / 2, L / 2, -L / 2], [-t / 2, -t / 2, t / 2, t / 2], color="#dfe9f7")
    a2.plot([-L / 2, L / 2], [t / 2, t / 2], color=RED, lw=2); a2.plot([-L / 2, L / 2], [-t / 2, -t / 2], color=ORANGE, lw=2)
    a2.plot([-L / 2, L / 2], [0, 0], color=BLUE, lw=2.4, ls="--")
    a2.text(L / 2 + 3, -2, "flat blank: every fibre R theta long.\nUnwrap preserves length along ONE chosen curve\n(the reference offset by d): pick d = the neutral line.",
            color=INK2, fontsize=8.5, va="center")
    a2.set_xlim(-60, 130)
    save(fig, "fig03_neutral_surface.png")


# ------------------------------------------------------------------ fig04: handedness
def fig04():
    fig = plt.figure(figsize=(9, 3.8))
    for i, (title, axes_def) in enumerate([
            ("Wrapped frame at the foot", [((1, 0, 0), "t (along W)", AQUA), ((0, -1, 0), "pn (W's plane normal)", ORANGE), ((0, 0, 1), "N = pn x t (height)", VIOLET)]),
            ("Flat frame (the origin's X, Y, Z)", [((1, 0, 0), "X = t", AQUA), ((0, 1, 0), "Y = -pn", ORANGE), ((0, 0, 1), "Z = N", VIOLET)])]):
        ax = fig.add_subplot(1, 2, i + 1, projection="3d")
        for d, lab, col in axes_def:
            ax.quiver(0, 0, 0, *d, color=col, lw=2.5, arrow_length_ratio=0.15)
            ax.text(*(1.15 * np.array(d)), lab, color=col, fontsize=9)
        ax.set_xlim(-1, 1); ax.set_ylim(-1, 1); ax.set_zlim(-0.2, 1.2)
        ax.set_xticks([]); ax.set_yticks([]); ax.set_zticks([])
        ax.view_init(20, -55)
        ax.set_title(title, fontsize=10)
    fig.text(0.5, 0.02, "(t, pn, N) with N = pn x t is LEFT-handed. Writing y = +v would mirror every solid, so the chart uses y = -v\n"
             "(edge_offset_utils.fs unwrapCoords). Plane normal pn is fixed ONCE per reference (height up; horizontal references: largest component +).",
             ha="center", color=INK2, fontsize=8.5)
    save(fig, "fig04_handedness.png")


# ------------------------------------------------------------------ real data helpers
def read_layup():
    wires, parts = {}, {}
    for line in open(os.path.join(DATA, "layup.txt")):
        if line.startswith("W "):
            name, kind, length, rest = [t.strip() for t in line[2:].split("|")]
            v = np.array([float(t) for t in rest.split()]).reshape(-1, 4)
            wires.setdefault(name, []).append((kind, float(length), v))
        elif line.startswith("F "):
            name, rest = [t.strip() for t in line[2:].split("|")]
            if rest.startswith("FAIL"):
                continue
            parts.setdefault(name, []).append(np.array([float(t) for t in rest.split()]).reshape(-1, 2))
    return wires, parts


KIND_COL = {"line": BLUE, "arc": ORANGE, "bspline": VIOLET}
PART_COL = {"Topsheet": INK2, "6005": MUTED, "4310": MUTED, "CORE": AQUA, "4802": YELLOW, "4803": YELLOW,
            "4305": MUTED, "Tip-Mat": MUTED, "Tail-Mat": MUTED, "Tip-Shear": MUTED, "Tail-Shear": MUTED, "base 4101": RED}


def fig05():
    wires, parts = read_layup()
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(11, 6.8), gridspec_kw={"height_ratios": [1, 1.25]})
    for ax, (x0, x1), ex in [(a1, (-100, 1830), 6.0), (a2, (1560, 1820), 1.0)]:
        for name, segs in parts.items():
            for v in segs:
                ax.plot(v[:, 0], v[:, 1], color=PART_COL.get(name, MUTED), lw=1.0)
        for kind, length, v in wires["REF_WIRE"]:
            k = kind.split()[0]
            ax.plot(v[:, 0], v[:, 2], color=KIND_COL[k], lw=2.6)
        ax.set_xlim(x0, x1)
        ax.set_aspect(ex)
        ax.set_xlabel("world X (mm)"); ax.set_ylabel("Z (mm)")
    a1.set_title("The test ski at y = 0 (Unwrap_Testing Copy 2), heights x6")
    a2.set_title("Tip, true scale")
    for kind, length, v in wires["REF_WIRE"]:
        k = kind.split()[0]
        lab = kind.replace("bspline deg 3 cps 10", "spline (10 CPs)").replace("arc R900.00000000001", "arc R900").replace("arc R406", "arc R406")
        a1.text(v[len(v) // 2, 0], v[len(v) // 2, 2] - 9, lab, color=KIND_COL[k], fontsize=8, ha="center")
    for name, xy in [("CORE", (900, 9)), ("base 4101", (900, -4.5)), ("Topsheet", (900, 20.5))]:
        a1.text(*xy, name, color=PART_COL[name], fontsize=8)
    for name, xy in [("4802 (core tip extension)", (1690, 24)), ("CORE", (1580, 9)), ("base", (1765, 20)), ("topsheet / 6005 / 4310 / mats / shear", (1600, 48))]:
        a2.text(*xy, name, color=PART_COL.get(name.split()[0], INK2) if name.split()[0] in PART_COL else INK2, fontsize=8)
    save(fig, "fig05_test_ski.png")


# ------------------------------------------------------------------ fig10: Part mode
def fig10():
    wires, parts = read_layup()
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(11, 7.6), gridspec_kw={"height_ratios": [1, 1.2], "hspace": 0.45})
    for kind, length, v in wires["REF_WIRE"]:
        a1.plot(v[:, 0], v[:, 2], color=KIND_COL[kind.split()[0]], lw=2.4)
    for name in ["CORE", "4802", "4803"]:
        for v in parts[name]:
            for lo, hi, col in [(-1e9, 145, ORANGE), (145, 1625, BLUE), (1625, 1e9, ORANGE)]:
                m = (v[:, 0] >= lo - 1e-6) & (v[:, 0] <= hi + 1e-6)
                if name != "CORE":
                    col = ORANGE
                if m.sum() > 1:
                    a1.plot(v[m, 0], v[m, 1], color=col, lw=1.6)
    for xj in (145, 1625):
        a1.axvline(xj, color=INK2, ls="--", lw=1)
        a1.text(xj + 8, 30, "junction plane\n(normal to W)", fontsize=8, color=INK2)
    a1.set_aspect(8); a1.set_xlim(-100, 1830); a1.set_ylim(-5, 60)
    a1.set_xlabel("world X (mm)"); a1.set_ylabel("Z (mm)")
    a1.set_title("Part mode: split where W changes line <-> curve (heights x8)")
    a1.text(560, 20, "blue: CORE middle piece over the line, 84 native faces moved by ONE rigid opTransform", color=BLUE, fontsize=8.5)
    a1.text(-90, 45, "4803 + CORE tail 10 mm: rebuilt", color=ORANGE, fontsize=8.5)
    a1.text(1330, 45, "CORE tip 10 mm + 4802: rebuilt", color=ORANGE, fontsize=8.5)
    # panel b: the cell rebuild in the chart frame (schematic of a 4802-like piece)
    ax = a2
    bare(ax)
    X0, X1, Z0, Z1 = 0, 180, 0, 8
    ax.add_patch(plt.Rectangle((X0, Z0), X1 - X0, Z1 - Z0, fill=False, ec=INK2, lw=1.2))
    xs = np.linspace(X0, X1, 200)
    top = 5.8 + 0.25 * np.sin(xs / 40)                 # a curved PROFILE chain (spline tool)
    bottom = np.full_like(xs, 1.8)                      # a straight PROFILE chain (plane tool)
    nose = lambda z: 160 + 3.0 * (z - 1.8)              # a leaning nose (RULED / WALL tool)
    endx = 12                                          # the end plane (x = const plane tool)
    # kept cell
    zz = np.linspace(1.8, 5.8, 20)
    m = (xs >= endx) & (xs <= nose(5.8))
    kx = np.r_[endx, xs[m], nose(5.8 + 0.25 * np.sin(nose(5.8) / 40)), nose(1.8), endx]
    kz = np.r_[1.8, top[m], 5.8 + 0.25 * np.sin(nose(5.8) / 40), 1.8, 1.8]
    ax.fill(kx, kz, color="#cfeee2")
    ax.plot(xs, top, color=VIOLET, lw=1.6); ax.plot(xs, bottom, color=VIOLET, lw=1.6)
    ax.plot([endx, endx], [Z0, Z1], color=VIOLET, lw=1.6)
    zl = np.array([Z0, Z1]); ax.plot(nose(zl), zl, color=VIOLET, lw=1.6)
    for (cx, cz) in [(6, 4), (90, 7.2), (90, 0.8), (172, 4), (175, 7.4), (6, 7.3), (6, 0.8), (172, 0.8)]:
        ax.text(cx, cz, "x", color=MUTED, ha="center", va="center", fontsize=10)
    ax.text(90, 3.8, "kept cell: its interior point maps back INSIDE the piece\n(unwrapInverse + qContainsPoint)", ha="center", fontsize=8.5, color=INK)
    ax.text(X1 + 3, 6.0, "spline tool (profile face chain)", color=VIOLET, fontsize=8)
    ax.text(X1 + 3, 1.8, "plane tool (straight chain)", color=VIOLET, fontsize=8)
    ax.text(nose(8.3), 8.35, "ruled tool (leaning wall)", color=VIOLET, fontsize=8, ha="center")
    ax.text(endx, 8.35, "plane tool (end plane)", color=VIOLET, fontsize=8, ha="center")
    ax.text(X0, -0.9, "box = the piece's flat extent + 2 mm, split by every tool one at a time;  x = dropped cells", color=INK2, fontsize=8)
    ax.set_xlim(-5, 240); ax.set_ylim(-1.5, 9.2)
    ax.set_title("Cell rebuild of a curved piece, in the chart frame (schematic side view)")
    save(fig, "fig10_part_mode.png")


# ------------------------------------------------------------------ fig12: rigid pieces over a curved reference
def rigid_error(L, R, H, d):
    s = np.linspace(-L / 2, L / 2, 161)
    h = np.linspace(0, H, 15)
    S, Hh = np.meshgrid(s, h)
    phi = S / R
    P = np.stack([(R - Hh) * np.sin(phi), R - (R - Hh) * np.cos(phi)], -1).reshape(-1, 2)
    F = np.stack([(R - d) * phi, Hh], -1).reshape(-1, 2)
    Pc, Fc = P - P.mean(0), F - F.mean(0)
    U, _, Vt = np.linalg.svd(Pc.T @ Fc)
    Q = ((U @ Vt).T @ Pc.T).T + F.mean(0)      # best rigid placement (least squares)
    return np.abs(Q - F).max()


def fig12():
    H = 14.0
    Ls = np.logspace(0.5, 3, 60)
    fig, ax = plt.subplots(figsize=(9, 5))
    for R, col in [(10000, BLUE), (30000, AQUA)]:
        e_mid = [rigid_error(L, R, H, H / 2) for L in Ls]
        ax.plot(Ls, e_mid, color=col, lw=2.2, label="measured: best rigid placement, R = %g m, core 0..14 mm, d = mid" % (R / 1000))
        hyp = 0.5 * H * (Ls / R) ** 2
        ax.plot(Ls, hyp, color=col, lw=1.4, ls="--", label="hypothesis 0.5 h dtheta^2, R = %g m" % (R / 1000))
        # sagitta alone
        ax.plot(Ls, Ls ** 2 / (8 * R), color=col, lw=1, ls=":", label="W's own sagitta L^2/(8R), R = %g m" % (R / 1000))
    ax.axhline(0.01, color=RED, lw=1.2)
    ax.text(3.3, 0.0115, "0.01 mm budget", color=RED, fontsize=9)
    ax.set_xscale("log"); ax.set_yscale("log")
    ax.set_xlabel("rigid piece length L along the reference (mm)")
    ax.set_ylabel("worst position error vs the exact chart map (mm)")
    ax.set_ylim(1e-6, 30)
    ax.set_title("Question (a): moving pieces of a CURVED span rigidly - the error is first order, not dtheta^2")
    ax.legend(fontsize=7.5, loc="upper left")
    save(fig, "fig12_rigid_piece_error.png")
    for R in (10000, 30000):
        for d in (0.0, H / 2):
            lo, hi = 1.0, 2000.0
            for _ in range(50):
                mid = (lo * hi) ** 0.5
                if rigid_error(mid, R, H, d) > 0.01:
                    hi = mid
                else:
                    lo = mid
            print("R %g d %g: max piece for 0.01 mm = %.1f mm" % (R, d, lo))


# ------------------------------------------------------------------ fig13: call graph
def box(ax, x, y, w, h, title, lines, col):
    ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.4,rounding_size=1.2", fc="#ffffff", ec=col, lw=1.6))
    ax.text(x + 1, y + h - 1.5, title, fontsize=9.5, weight="bold", color=col, va="top")
    ax.text(x + 1, y + h - 5, "\n".join(lines), fontsize=7.4, color=INK, va="top", family="monospace")


def fig13():
    fig, ax = plt.subplots(figsize=(13, 10))
    bare(ax); ax.set_xlim(0, 130); ax.set_ylim(0, 102)
    box(ax, 2, 62, 58, 38, "unwrap.fs  (the feature; tab a84cdaa8...)", [
        "unwrap (defineFeature)              body, 3 modes",
        "  sourceBodies / frameOf / pointOf",
        "  checkedChart -> unwrapChart   [X rises, align in span]",
        "  EDGES: unwrapEdgesToWires -> unwrapEdges",
        "         adaptiveEdgeSamples, checkedFoot, spanMidMiss",
        "         emitFlatCurve (classifyPoints + tangent gate)",
        "  PART : unwrapPart -> unwrapSolid, layOnPlane",
        "  PLATE: unwrapPlate: plateSidesGeneral, profileFromFace,",
        "         undrapeOutline, emitFlatCurve, plateFromOutline",
        "  lengthAndVolume      (length / volume check)",
        "  applyNamesAndProperties, reportSummary",
        "  embedStandardOutputs (lengthWrapped/Flat, volumeRatio)",
        "unwrapEditLogic   (button 'Read names and properties')"], BLUE)
    box(ax, 68, 76, 58, 24, "undrape_utils.fs  (tab 283b8f75...; imported version \"\")", [
        "undrapeOutline(context,id,chart,side0,side1,t,options)",
        "  undrapeSampleSide -> undrapeEdgeTable (adaptive)",
        "  undrapeRimLoops, undrapeSeeds, undrapeShare",
        "  undrapeResolveBatch: undrapeCandidateEdges,",
        "     undrapeEdgeCrossings, undrapeSection / ...ByFaces,",
        "     undrapeRefusedSection (U-TURN RULE, kernel)",
        "  undrapeRefine, undrapeAssemble, undrapeDeformation"], VIOLET)
    box(ax, 68, 46, 58, 24, "unwrap_part.fs  (tab fc976128...; imported version \"\")", [
        "unwrapSolid(context,id,chart,cs,part,options)",
        "  referenceSpans (edge curveType == LINE -> straight)",
        "  opSplitPart at junctions, rigidTransform",
        "  rebuildPiece: faceKind (PROFILE/WALL/RULED/FAIL),",
        "     faceRow, chainRows, distinctChains, chainTool,",
        "     box split, unwrapInverse membership, union"], ORANGE)
    box(ax, 2, 14, 58, 40, "edge_offset_utils.fs  (tab a2665e22; mv 70dcbbcd)", [
        "buildAlongReference  arcs, thetas, curvatures, points,",
        "                     tangents, planeNormal (25 / edge)",
        "referencePlaneNormal (handedness rule)",
        "unwrapChart -> unwrapReferenceProblem,",
        "               chartFromReference -> packChart",
        "chartEval / chartSpan / chartSpanOf / chartSeedArc",
        "chartFoot            Newton foot + residual",
        "unwrapFast           [x,y,z, arc,span, t, kappa,",
        "                      scale, h, residual]",
        "chartFootConverged, unwrapDirection, unwrapInverse",
        "(older unit-based: referenceSurfaceCoords / Point)"], AQUA)
    box(ax, 68, 20, 58, 18, "curve_core.fs  (Curve_tools doc; version 9d6f0887)", [
        "buildChain, expandEdgeQuery (chains)",
        "classifyPoints  line / arc / freeform",
        "emitLineCurve, emitArcCurve, emitSplineCurve",
        "approximateFamily, G1_JUNCTION_ANGLE"], GREEN)
    box(ax, 68, 3, 58, 11, "extract_outputs.fs  (Variable_tools; version b8c80ac0)", [
        "embedStandardOutputs, extractableVariable / Query"], MUTED)
    arrow(ax, (60.8, 92), (67.3, 92), BLUE); ax.text(61, 93, "undrapeOutline", fontsize=7, color=INK2)
    arrow(ax, (60.8, 66), (67.3, 60), BLUE); ax.text(61, 66.5, "unwrapSolid", fontsize=7, color=INK2)
    arrow(ax, (30, 61.3), (30, 54.8), BLUE); ax.text(31, 57.5, "unwrapChart, unwrapFast, unwrapDirection, chartEval ...", fontsize=7, color=INK2)
    arrow(ax, (67.3, 78), (60.8, 50), VIOLET); ax.text(62.5, 72, "chart*", fontsize=7, color=VIOLET)
    arrow(ax, (67.3, 48), (60.8, 40), ORANGE); ax.text(61.2, 45.5, "unwrapFast,\nunwrapInverse", fontsize=7, color=ORANGE)
    arrow(ax, (60.8, 29), (67.3, 29), AQUA); ax.text(61, 30, "export\nimport", fontsize=7, color=INK2)
    arrow(ax, (60.8, 63), (67.3, 9), BLUE, lw=0.9); ax.text(63.5, 15, "outputs", fontsize=7, color=BLUE)
    ax.text(2, 6, "Arrows: calls into / imports. edge_offset_utils EXPORT-imports curve_core, so\n"
            "unwrap.fs and unwrap_part.fs see classifyPoints and the emitters through it.", fontsize=8, color=INK2)
    save(fig, "fig13_call_graph.png")


# ------------------------------------------------------------------ fig14: which curve keeps its length
def fig14():
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(11.5, 4.4), gridspec_kw={"width_ratios": [1.3, 1]})
    bare(a1); a1.set_aspect("equal")
    R, th = 110.0, np.radians(45)
    ph = np.linspace(0, th, 60)
    layers = [(0, 1.2, RED, "base"), (1.8, 5.8, YELLOW, "core ext. (4802)"), (5.8, 6.4, VIOLET, "top laminate")]
    lead = 40
    for lo, hi, col, lab in layers:
        s0 = 5 * lo  # exaggerate thickness for the picture
        s1 = 5 * hi
        xo = np.r_[0, lead, lead + (R - s1) * np.sin(ph)]; zo = np.r_[s1, s1, R - (R - s1) * np.cos(ph)]
        xi = np.r_[0, lead, lead + (R - s0) * np.sin(ph)]; zi = np.r_[s0, s0, R - (R - s0) * np.cos(ph)]
        a1.fill(np.r_[xo, xi[::-1]], np.r_[zo, zi[::-1]], color=col, alpha=0.35, lw=0)
        sm = 2.5 * (lo + hi)
        a1.plot(np.r_[0, lead, lead + (R - sm) * np.sin(ph)], np.r_[sm, sm, R - (R - sm) * np.cos(ph)], color=col, lw=1.6, ls="--")
        a1.text(-3, sm, lab + "  (mid at d = %.1f)" % (0.5 * (lo + hi)), ha="right", va="center", fontsize=8, color=INK)
    a1.plot(np.r_[0, lead, lead + R * np.sin(ph)], np.r_[0, 0, R - R * np.cos(ph)], color=BLUE, lw=2.2)
    a1.text(lead + R * np.sin(th) + 6, R - R * np.cos(th) - 10, "REF_WIRE (d = 0)", color=BLUE, fontsize=8)
    a1.set_xlim(-80, 130)
    a1.set_title("Each layer's own neutral line = W offset by its own d\n(thicknesses x5)")
    d = np.array([0.0, 1.8, 3.8, 5.8])
    L = np.array([180.62, 179.57, 178.40, 177.22])
    a2.plot(d, L, "o-", color=YELLOW, lw=2, ms=8)
    for di, li, lab in zip(d, L, ["REF_WIRE", "its bottom", "its middle", "its top"]):
        a2.text(di + 0.15, li + 0.1, "%.2f  %s" % (li, lab), fontsize=8, color=INK)
    a2.set_xlabel("preserve-length offset d (mm)")
    a2.set_ylabel("flat length of 4802 (mm)")
    a2.set_title("4802: flat length vs d (-0.59 mm per mm)\nvolume x1.0126 at d = 0, x1.0001 at d = 3.8")
    a2.set_xlim(-0.3, 7.5)
    save(fig, "fig14_which_curve.png")


if __name__ == "__main__":
    fig01(); fig02(); fig03(); fig04(); fig05(); fig10(); fig12(); fig13(); fig14()
