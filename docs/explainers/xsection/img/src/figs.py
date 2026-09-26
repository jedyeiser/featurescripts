"""Figures for docs/explainers/xsection/xsection_explained.md and the xSection decks.

Data in data/ was read from the xSection document (read-only eval API, 2026-09-25):
  roy_stations.json  ROY_Test, "EI and Cross Section 1": per station x, EI, GJ, NA, thickness, width, lineal density
  test_curves.json   Test studio: NewTestBaseline (Generate baseline 1), Test_Profile_Update, Curve 9
  test2_curves.json  Test2 studio: eiTarget, altered_EI, deflection_curve (Estimate Deflection 1)
usage (repo root): python docs/explainers/xsection/img/src/figs.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "..", "tooling"))
from figstyle import *  # noqa: F401,F403,E402

OUT = os.path.join(HERE, "..")
ROY = json.load(open(os.path.join(HERE, "data", "roy_stations.json")))
TC = json.load(open(os.path.join(HERE, "data", "test_curves.json")))
T2 = json.load(open(os.path.join(HERE, "data", "test2_curves.json")))
FCP_X, ACP_X, MRS_X = 145.0, 1625.0, 885.0      # ROY_Test (stations 0 and 34; beamAnalysis xMRS)


def ref_lines(ax, labels=True, top=None):
    for x, t in ((FCP_X, "FCP"), (ACP_X, "ACP")):
        ax.axvline(x, color=MUTED, lw=1, ls="--", zorder=0)
        if labels:
            ax.text(x, top if top is not None else ax.get_ylim()[1], t, ha="center", va="bottom", color=INK2, fontsize=9)


# ---------------------------------------------------------------- fig01 data flow
def fig01():
    fig, ax = plt.subplots(figsize=(11, 4.6))
    bare(ax, equal=False)
    ax.set_xlim(0, 110)
    ax.set_ylim(0, 46)

    def box(x, y, w, h, text, col, sub=None, fill="#ffffff"):
        ax.add_patch(FancyBboxPatch((x, y), w, h, boxstyle="round,pad=0.4,rounding_size=1.2", fc=fill, ec=col, lw=1.8))
        ax.text(x + w / 2, y + h / 2 + (1.3 if sub else 0), text, ha="center", va="center", fontsize=10, weight="bold", color=INK)
        if sub:
            ax.text(x + w / 2, y + h / 2 - 1.9, sub, ha="center", va="center", fontsize=8, color=INK2)

    box(1, 30, 17, 10, "Layer bodies", MUTED, "one solid per ply,\nOnshape material")
    box(1, 14, 17, 10, "materialData.csv", MUTED, "E, Q66, density ...")
    box(26, 20, 21, 14, "EI and Cross Section", BLUE, "stations, EI, NA, GJ,\nflex, weight", fill="#eef4fc")
    box(55, 34, 20, 8, "EI curve", BLUE, "Z mm = EI N m^2")
    box(55, 22, 20, 8, "Tables", MUTED, "Summary, Details ...")
    box(55, 7, 20, 10, "Update profile", ORANGE, "target EI ->\nnew thickness")
    box(84, 37, 23, 7, "Estimate Stiffness", AQUA, "4 flex numbers")
    box(84, 27, 23, 7, "Estimate Deflection", AQUA, "bent shape, load case")
    box(84, 16, 23, 7, "Generate baseline", GREEN, "camber + rocker")
    box(84, 5, 23, 7, "Analyze baseline", GREEN, "measure a baseline")
    box(26, 5, 21, 8, "Solve GJ", MUTED, "legacy: recompute GJ")
    for p, q in [((18.8, 35), (25.6, 29)), ((18.8, 19), (25.6, 25)), ((47.6, 30), (54.6, 37)), ((47.6, 27), (54.6, 26)),
                 ((47.6, 23), (54.6, 13)), ((75.6, 38), (83.6, 40.5)), ((75.6, 37.5), (83.6, 30.5)),
                 ((75.6, 36.5), (83.6, 19.5)), ((95.5, 15.6), (95.5, 12.4)), ((36.5, 19.6), (36.5, 13.4))]:
        arrow(ax, p, q, col=INK2, lw=1.3)
    ax.text(66, 25.6 - 7.6, "", fontsize=8)
    save(fig, os.path.join(OUT, "fig01_data_flow.png"))


# ---------------------------------------------------------------- fig02 stations
def fig02():
    fig, ax = plt.subplots(figsize=(11, 3.4))
    xs = [r["x"] for r in ROY]
    ts = [r["t"] for r in ROY]
    ax.fill_between(xs, 0, ts, color="#e8eef8", lw=0)
    ax.plot(xs, ts, color=INK2, lw=1.4)
    for r in ROY:
        col = VIOLET if r["n"] < 0 else (ORANGE if r["n"] > 34 else BLUE)
        ax.plot([r["x"], r["x"]], [0, r["t"]], color=col, lw=1.6)
    for r in ROY:
        if r["n"] in (-4, -1, 0, 1, 17, 33, 34, 35, 38):
            ax.text(r["x"], -1.3, str(r["n"]), ha="center", va="top", fontsize=8, color=INK2)
    ax.set_ylim(-3.2, 19.5)
    ax.set_xlim(-30, 1810)
    ref_lines(ax, top=16.6)
    ax.text(8, 8.6, "tip: 4 extra\n(negative\nnumbers)", ha="left", color=VIOLET, fontsize=9)
    ax.text(885, 16.8, "35 stations FCP -> ACP, one exactly at each (0 ... 34), 43.5 mm apart", ha="center", color=BLUE, fontsize=9)
    ax.text(1720, 9.5, "tail: 4 extra\n(34 + 1 ...)", ha="center", color=ORANGE, fontsize=9)
    ax.set_xlabel("x along the ski (mm)")
    ax.set_ylabel("thickness (mm)")
    ax.set_title("Where the ski is cut: ROY_Test, 'Number of cross sections' = 35")
    save(fig, os.path.join(OUT, "fig02_stations.png"))


# ---------------------------------------------------------------- fig03 laminate section
def fig03():
    fig, ax = plt.subplots(figsize=(10, 4.2))
    bare(ax, equal=False)
    ax.set_xlim(-4, 150)
    ax.set_ylim(-3, 24)
    # layer stack: (name, z0, z1, E GPa, colour, inset)
    layers = [("base (P-Tex)", 0.0, 1.2, 0.7, "#bcbcbc", 0),
              ("bottom laminate", 1.2, 2.0, 25, "#8fb3e8", 0),
              ("core (wood)", 2.0, 12.0, 9.8, "#e9cf9b", 6),
              ("top laminate", 12.0, 12.8, 25, "#8fb3e8", 6),
              ("topsheet", 12.8, 13.6, 2.0, "#d7d2ea", 6)]
    edges = ("#8a8a8a", "#2a78d6")
    sx = 0.9
    for name, z0, z1, e, col, ins in layers:
        ax.add_patch(Rectangle((ins * sx, z0), (100 - 2 * ins) * sx, z1 - z0, fc=col, ec="#ffffff", lw=0.8))
        ly = {"base (P-Tex)": -0.8, "bottom laminate": 2.6, "core (wood)": 7.0, "top laminate": 11.2, "topsheet": 14.8}[name]
        ax.plot([(100 - ins) * sx, 96], [(z0 + z1) / 2, ly], color=MUTED, lw=0.7)
        ax.text(97, ly, "%s  E = %g GPa" % (name, e), va="center", fontsize=8.5, color=INK2)
    # neutral axis: E-weighted centroid
    ea = sum(e * (100 - 2 * ins) * (z1 - z0) for _, z0, z1, e, _, ins in layers)
    es = sum(e * (100 - 2 * ins) * (z1 - z0) * (z0 + z1) / 2 for _, z0, z1, e, _, ins in layers)
    na = es / ea
    ax.plot([-2, 91], [na, na], color=RED, lw=1.6, ls="--")
    ax.text(-3, na + 0.5, "neutral axis", color=RED, fontsize=9, va="bottom")
    # ybar of the core
    xk = 30
    arrow(ax, (xk, 0), (xk, 7.0), col=INK, lw=1.2, style="<|-|>", ms=9)
    ax.text(xk + 1.2, 3.6, "ybar_k", fontsize=9)
    ax.plot([-2, 91], [0, 0], color=INK, lw=1.2)
    ax.text(-3, -1.5, "base edge (y = 0)", fontsize=9, color=INK2)
    ax.text(0, 21.5, "Each body is one ply k:  area A_k,  centroid height ybar_k,  own I_k,  modulus E_k", fontsize=10)
    ax.text(0, 19.2, "NA = sum(E A ybar) / sum(E A)        EI = sum E (I_k + A_k ybar_k^2)  -  (sum E A ybar)^2 / sum(E A)",
            fontsize=10, color=BLUE)
    ax.text(0, 16.9, "(the stiffness about the neutral axis; schematic section, thickness x 4)", fontsize=8.5, color=INK2)
    save(fig, os.path.join(OUT, "fig03_laminate_ei.png"))


# ---------------------------------------------------------------- fig04 ROY results
def fig04():
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(11, 6.2), sharex=True, gridspec_kw={"height_ratios": [1.3, 1]})
    xs = [r["x"] for r in ROY]
    a1.plot(xs, [r["EI"] for r in ROY], color=BLUE, marker="o", ms=3, label="EI  (N m^2)")
    a1.plot(xs, [r["GJ"] for r in ROY], color=ORANGE, marker="o", ms=3, label="GJ  (N m^2)")
    a1.set_ylim(0, 520)
    ref_lines(a1, top=500)
    a1.legend(loc="upper left")
    a1.set_title("ROY_Test: what EI and Cross Section stores per station")
    a2.plot(xs, [r["t"] for r in ROY], color=INK2, label="thickness (mm)")
    a2.plot(xs, [r["NA"] for r in ROY], color=RED, ls="--", label="neutral axis height (mm)")
    a2.plot(xs, [r["rho"] * 10 for r in ROY], color=AQUA, label="lineal density (x 10, kg/m)")
    a2.set_ylim(0, 17)
    ref_lines(a2, labels=False)
    a2.legend(loc="upper left", ncol=3)
    a2.set_xlabel("x along the ski (mm)")
    save(fig, os.path.join(OUT, "fig04_roy_results.png"))


# ---------------------------------------------------------------- fig05 flex numbers
def fig05():
    fig, (a1, a2) = plt.subplots(2, 1, figsize=(10, 5.2), gridspec_kw={"height_ratios": [1, 1.1]})
    bare(a1, equal=False)
    a1.set_xlim(-60, 1560)
    a1.set_ylim(-60, 120)
    L = ACP_X - FCP_X
    xm = MRS_X - FCP_X
    beam = np.linspace(0, L, 200)
    a1.plot(beam, 0 * beam + 20, color=MUTED, lw=6, solid_capstyle="butt")
    a1.plot(beam, 20 - 30 * np.sin(np.pi * beam / L), color=BLUE, lw=2)
    for x, t in ((0, "FCP"), (L, "ACP")):
        a1.add_patch(Polygon([(x, 12), (x - 30, -22), (x + 30, -22)], closed=True, fc=INK2, ec=INK2))
        a1.text(x, -48, t, ha="center", fontsize=9)
    arrow(a1, (xm, 115), (xm, 26), col=RED, lw=2.2, ms=16)
    a1.text(xm + 20, 90, "P at MRS (mount)", color=RED, fontsize=9)
    a1.text(L / 2, -48, "span L = FCP -> ACP = %.0f mm" % L, ha="center", fontsize=9, color=INK2)
    # moment arm
    s = np.where(beam < xm, beam * (L - xm) / L, (L - beam) * xm / L)
    a2.fill_between(beam, 0, s, color="#fde3d8")
    a2.plot(beam, s, color=ORANGE)
    xs = [r["x"] - FCP_X for r in ROY if 0 <= r["n"] <= 34]
    ei = [r["EI"] for r in ROY if 0 <= r["n"] <= 34]
    a3 = a2.twinx()
    a3.plot(xs, ei, color=BLUE, lw=1.6)
    a3.set_ylabel("EI (N m^2)", color=BLUE)
    a3.set_ylim(0, 450)
    a3.spines["right"].set_visible(True)
    a2.set_ylabel("moment per unit load s(x) (mm)", color=ORANGE)
    a2.set_xlabel("x from FCP (mm)")
    a2.set_ylim(0, 580)
    a3.set_ylim(0, 580)
    a2.set_xlim(-60, 1560)
    a2.text(30, 535, "prismatic:  delta = P L^3 / (48 EI_avg)     ->  103.6 mm at 30 kg,  16.2 lb/in", fontsize=9)
    a2.text(30, 485, "variable EI (Mohr):  delta = P * integral s^2 / EI dx   ->  81.3 mm at 30 kg,  20.7 lb/in", fontsize=9, color=BLUE)
    save(fig, os.path.join(OUT, "fig05_flex_numbers.png"))


# ---------------------------------------------------------------- baseline helpers
def baseline_edges():
    edges = sorted(TC["NewTestBaseline"], key=lambda e: min(p[0] for p in e))
    return [np.array(sorted(e, key=lambda p: p[0])) for e in edges]


def fig06():
    fore, camber, aft = baseline_edges()
    fig, ax = plt.subplots(figsize=(11, 4.4))
    for e, col, t in ((fore, VIOLET, "forebody rocker"), (camber, BLUE, "camber pocket"), (aft, ORANGE, "aftbody rocker")):
        ax.plot(e[:, 0], e[:, 1], color=col, lw=2.4, label=t)
    fcp, acp = fore[0], aft[-1]
    frcp, arcp = camber[0], camber[-1]
    mount = camber[np.argmax(camber[:, 1])]
    lo_f = fore[np.argmin(fore[:, 1])]
    lo_a = aft[np.argmin(aft[:, 1])]
    for p, t, dy in ((fcp, "FCP", 0.5), (acp, "ACP", 0.5), (frcp, "FRCP", 0.9), (arcp, "ARCP", 0.9),
                     (lo_f, "min", -1.2), (lo_a, "min", -1.2)):
        ax.plot(*p, "o", color=INK, ms=5)
        ax.text(p[0], p[1] + dy, t, ha="center", fontsize=9)
    ax.plot(*mount, "o", color=RED, ms=6)
    ax.text(mount[0], mount[1] + 0.5, "mount (MRS)", ha="center", color=RED, fontsize=9)
    arrow(ax, (mount[0] + 30, 0), (mount[0] + 30, mount[1]), col=RED, lw=1.4, style="<|-|>", ms=9)
    ax.text(mount[0] + 45, 2.2, "camber height\nat the mount = 5 mm", color=RED, fontsize=9)
    ax.axhline(0, color=MUTED, lw=1, ls="--", zorder=0)
    ax.annotate("", xy=(frcp[0], -2.2), xytext=(fcp[0], -2.2), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text((fcp[0] + frcp[0]) / 2, -3.3, "forebody rocker\nlength 200", ha="center", fontsize=8.5, color=INK2, va="top")
    ax.annotate("", xy=(acp[0], -2.2), xytext=(arcp[0], -2.2), arrowprops=dict(arrowstyle="<->", color=INK2))
    ax.text((acp[0] + arcp[0]) / 2, -3.3, "aftbody rocker\nlength 200", ha="center", fontsize=8.5, color=INK2, va="top")
    ax.set_ylim(-7, 9.5)
    ax.set_xlabel("x (mm)")
    ax.set_ylabel("z (mm), 60 x exaggerated")
    ax.legend(loc="upper center", ncol=3)
    ax.set_title("Generate baseline: Test studio, NewTestBaseline (curve per region)")
    save(fig, os.path.join(OUT, "fig06_baseline.png"))


def fig07():
    """Rocker construction at the forebody, schematic with the real numbers of NewTestBaseline."""
    fore, camber, _ = baseline_edges()
    fcp, frcp = fore[0], camber[0]
    slope = (camber[3, 1] - camber[0, 1]) / (camber[3, 0] - camber[0, 0])
    fig, ax = plt.subplots(figsize=(9, 4.4))
    ax.plot(fore[:, 0], fore[:, 1], color=VIOLET, lw=2.4, label="rocker (quadratic)")
    ax.plot(camber[:40, 0], camber[:40, 1], color=BLUE, lw=2.4, label="camber")
    tx = np.array([fcp[0] - 10, frcp[0] + 60])
    ty = frcp[1] + slope * (tx - frcp[0])
    ax.plot(tx, ty, color=INK2, ls="--", lw=1.2, label="camber tangent at FRCP")
    # normal from the tangent line to FCP
    n = np.array([-slope, 1.0]) / np.hypot(slope, 1.0)
    foot_t = np.dot(np.array(fcp) - frcp, np.array([1, slope]) / np.hypot(1, slope))
    foot = frcp + foot_t * np.array([1, slope]) / np.hypot(1, slope)
    ax.annotate("", xy=fcp, xytext=foot, arrowprops=dict(arrowstyle="<->", color=RED, lw=1.4))
    d = np.hypot(*(np.array(fcp) - foot))
    ax.text(foot[0] + 6, 0.4, "FCP height\n%.1f mm, normal\nto the tangent" % d, color=RED, fontsize=9)
    # control polygon: FRCP, tangent-line point, FCP
    mid = frcp + 0.5 * (foot - frcp)
    ax.plot([frcp[0], mid[0], fcp[0]], [frcp[1], mid[1], fcp[1]], color=MUTED, lw=1, marker="s", ms=4)
    ax.text(mid[0] + 5, mid[1] - 0.5, "middle control point, on the tangent\n(tension t sets where the low point falls)", ha="left", fontsize=8.5, color=INK2, va="top")
    for p, t in ((fcp, "FCP"), (frcp, "FRCP")):
        ax.plot(*p, "o", color=INK, ms=5)
        ax.text(p[0] + 4, p[1] + 0.5, t, fontsize=9)
    ax.set_xlim(fcp[0] - 30, frcp[0] + 70)
    ax.set_ylim(-4, 13)
    ax.set_xlabel("x (mm)")
    ax.set_ylabel("z (mm), exaggerated")
    ax.legend(loc="upper right")
    save(fig, os.path.join(OUT, "fig07_rocker_construction.png"))


# ---------------------------------------------------------------- deflection
def fig08():
    ei = np.array(sorted(T2["eiTarget"][0], key=lambda p: p[0]))
    alt = np.array(sorted(T2["altered_EI"][0], key=lambda p: p[0]))
    de = np.array(sorted(T2["deflection_curve"][0], key=lambda p: p[0]))
    fig, (a1, a2, a3) = plt.subplots(3, 1, figsize=(10.5, 7.2), sharex=True, gridspec_kw={"height_ratios": [1, 0.9, 1]})
    a1.plot(ei[:, 0], ei[:, 1], color=BLUE, label="EI edge (input)")
    a1.plot(alt[:, 0], alt[:, 1], color=BLUE, ls=":", lw=1.4, label="altered_EI (curvature handles dragged)")
    a1.set_ylabel("EI (N m^2)")
    a1.legend(loc="upper left")
    a1.set_title("Estimate Deflection: Test2 studio, two supports, two loads")
    # load case
    bare(a2, equal=False)
    a2.set_ylim(-1.3, 2.0)
    a2.plot([-775, 775], [0, 0], color=MUTED, lw=5, solid_capstyle="butt")
    for x in (-700, 700):
        a2.add_patch(Polygon([(x, -0.12), (x - 35, -0.75), (x + 35, -0.75)], closed=True, fc=INK2))
        a2.text(x, -1.15, "support x = %d\n(logistic, 100 wide)" % x, ha="center", fontsize=8, va="center")
    for x, f in ((-125, 60), (250, 20)):
        h = 0.2 + 1.1 * f / 60
        arrow(a2, (x, h), (x, 0.1), col=RED, lw=2, ms=14)
        a2.text(x + 18, h - 0.05, "%d N at x = %d" % (f, x), color=RED, fontsize=8.5, va="top")
    a2.text(-770, 1.85, "applied load 80 N, balance 0.75: 60 N on load 1, 20 N on load 2 (each 100 mm wide)", fontsize=8.5, color=INK2)
    a3.plot(de[:, 0], de[:, 1], color=ORANGE, lw=2.2, label="deflection_curve (true scale)")
    a3.axhline(0, color=MUTED, lw=1)
    for x in (-700, 700):
        a3.plot(x, 0, "o", color=INK2, ms=5)
    i = np.argmin(de[:, 1])
    a3.text(de[i, 0], de[i, 1] - 1.6, "%.1f mm" % de[i, 1], ha="center", fontsize=9, color=ORANGE)
    a3.set_ylim(-19, 4)
    a3.set_ylabel("deflection (mm)")
    a3.set_xlabel("x (mm)")
    a3.legend(loc="upper center")
    save(fig, os.path.join(OUT, "fig08_deflection_case.png"))


def fig09():
    t = np.linspace(-0.75, 0.75, 400)
    w = 1.0
    shapes = {
        "Constant": np.where(abs(t) <= 0.5, 1.0, 0.0),
        "Linear": np.where(abs(t) <= 0.5, 2 * (1 - abs(t) / 0.5), 0.0),
        "Quadratic": np.where(abs(t) <= 0.5, 1.5 * (1 - (t / 0.5) ** 2), 0.0),
    }
    u = np.clip(abs(t) / 0.5, 0, 1)
    shapes["Quintic"] = np.where(abs(t) <= 0.5, 2 * (1 - 6 * u ** 5 + 15 * u ** 4 - 10 * u ** 3), 0.0)
    tt = t / 0.5
    lg = 2 * (1 / (1 + np.exp(-10 * (tt + 0.5))) - 1 / (1 + np.exp(-10 * (tt - 0.5))))
    shapes["Logistic"] = np.where(abs(t) <= 0.5, lg, 0.0)
    cols = [INK2, VIOLET, AQUA, BLUE, ORANGE]
    fig, axs = plt.subplots(1, 6, figsize=(12, 2.4), sharey=True)
    axs[0].axis("off")
    axs[0].annotate("", xy=(0.5, 0.15), xytext=(0.5, 0.9), xycoords="axes fraction", arrowprops=dict(arrowstyle="-|>", color=RED, lw=2.2))
    axs[0].text(0.5, 0.0, "Point", ha="center", transform=axs[0].transAxes, fontsize=10, weight="bold")
    for ax, (k, v), c in zip(axs[1:], shapes.items(), cols):
        ax.fill_between(t, 0, v, color=c, alpha=0.25, lw=0)
        ax.plot(t, v, color=c)
        ax.set_title(k)
        ax.set_xticks([-0.5, 0, 0.5])
        ax.set_xticklabels(["-w/2", "0", "w/2"])
        ax.set_ylim(0, 2.3)
    axs[1].set_yticks([])
    fig.suptitle("Load shapes as designed: each spreads the force over the width w (area = force)", y=1.06, fontsize=11, weight="bold")
    save(fig, os.path.join(OUT, "fig09_load_shapes.png"))


# ---------------------------------------------------------------- update profile
def fig10():
    fig, (a1, a2) = plt.subplots(1, 2, figsize=(11.5, 3.8), gridspec_kw={"width_ratios": [1, 1.5]})
    bare(a1, equal=False)
    a1.set_xlim(-8, 108)
    a1.set_ylim(-3, 22)
    na = 5.2
    a1.add_patch(Rectangle((0, 0), 100, 11, fc="#e9cf9b", ec="#ffffff"))
    a1.add_patch(Rectangle((0, na), 100, 11 - na, fc="none", ec=MUTED, ls=":"))
    a1.add_patch(Rectangle((0, 11), 100, 3.2, fc="#fde3d8", ec=ORANGE, ls="--"))
    a1.plot([-4, 104], [na, na], color=RED, ls="--", lw=1.4)
    a1.text(-6, na + 0.5, "NA", color=RED, fontsize=9)
    for x in (20, 50, 80):
        arrow(a1, (x, 8.5), (x, 11.8), col=ORANGE, lw=1.3, ms=9)
    a1.text(50, 15.4, "every section point above the NA moves up dt", ha="center", color=ORANGE, fontsize=9)
    a1.text(50, 2.3, "points below stay", ha="center", fontsize=9, color=INK2)
    a1.text(50, 19.2, "STD: find dt so EI(dt) = target (bisection)", ha="center", fontsize=9.5, weight="bold")
    e = np.array(sorted(TC["Test_Profile_Update"][0], key=lambda p: p[0]))
    a2.plot(e[:, 0], e[:, 1], color=ORANGE, lw=2.2, label="Test_Profile_Update (new thickness, Z true scale)")
    a2.set_ylim(0, 19)
    a2.set_xlabel("x (mm)")
    a2.set_ylabel("thickness (mm)")
    a2.legend(loc="upper left")
    a2.set_title("Output: a thickness curve (Test studio, STD)")
    save(fig, os.path.join(OUT, "fig10_update_profile.png"))


if __name__ == "__main__":
    for f in (fig01, fig02, fig03, fig04, fig05, fig06, fig07, fig08, fig09, fig10):
        f()
