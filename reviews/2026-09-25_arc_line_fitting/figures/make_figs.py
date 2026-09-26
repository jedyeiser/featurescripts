"""Generate SVG figures for reviews/2026-09-25_arc_line_fitting/SUMMARY.md."""
import math
import os
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

BLUE = "#1651B0"
RED = "#C0392B"
GREEN = "#1E8449"
ORANGE = "#D68910"
GREY = "#888888"
DARK = "#333333"
LIGHT = "#F4F6F8"


def svg(name, w, h, body):
    head = (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
        f'viewBox="0 0 {w} {h}" font-family="Segoe UI, Helvetica, Arial, sans-serif" '
        f'font-size="13" fill="{DARK}">\n'
        f'<rect x="0" y="0" width="{w}" height="{h}" fill="#FFFFFF"/>\n'
        '<defs>\n'
    )
    for key, col in (("b", BLUE), ("r", RED), ("g", GREEN), ("k", DARK), ("o", ORANGE), ("y", GREY)):
        head += (
            f'<marker id="a{key}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" '
            f'markerHeight="7" orient="auto-start-reverse"><path d="M0,0 L10,5 L0,10 z" '
            f'fill="{col}"/></marker>\n'
        )
    head += '</defs>\n'
    with open(os.path.join(OUT, name), "w", encoding="ascii") as f:
        f.write(head + body + "</svg>\n")


def text(x, y, s, size=13, anchor="start", col=DARK, weight="normal", italic=False):
    st = ' font-style="italic"' if italic else ""
    return (f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" text-anchor="{anchor}" '
            f'fill="{col}" font-weight="{weight}"{st}>{s}</text>\n')


def line(x1, y1, x2, y2, col=DARK, w=1.5, dash=None, arrow=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    m = f' marker-end="url(#a{arrow})"' if arrow else ""
    return (f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{col}" '
            f'stroke-width="{w}"{d}{m}/>\n')


def poly(pts, col=DARK, w=2, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    p = " ".join(f"{x:.1f},{y:.1f}" for x, y in pts)
    return f'<polyline points="{p}" fill="none" stroke="{col}" stroke-width="{w}"{d}/>\n'


def dot(x, y, col=DARK, r=4):
    return f'<circle cx="{x:.1f}" cy="{y:.1f}" r="{r}" fill="{col}"/>\n'


def box(x, y, w, h, fill, stroke, rx=6):
    return (f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{rx}" '
            f'fill="{fill}" stroke="{stroke}" stroke-width="1.5"/>\n')


def norm(v):
    l = math.hypot(v[0], v[1])
    return (v[0] / l, v[1] / l)


def circle3(a, b, c):
    ax, ay = a
    bx, by = b
    cx, cy = c
    d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by))
    ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d
    uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d
    return (ux, uy), math.hypot(ax - ux, ay - uy)


# ---------------------------------------------------------------- fig 1: why a varying offset of an arc is not an arc
def fig_offset_not_circle():
    W, H = 760, 400
    R = 1.0
    th0, th1 = math.radians(-38), math.radians(38)
    n = 200

    def w_of(t):  # exaggerated varying offset, like the live profile (bigger at the ends)
        u = 2 * (t - th0) / (th1 - th0) - 1
        return 0.06 + 0.03 * u + 0.08 * (u ** 3 - u)   # S-shaped, like the live residual

    src, off = [], []
    for i in range(n + 1):
        t = th0 + (th1 - th0) * i / n
        src.append((R * math.sin(t), R * math.cos(t)))
        r = R - w_of(t)
        off.append((r * math.sin(t), r * math.cos(t)))
    c, rc = circle3(off[0], off[n // 2], off[-1])
    a0 = math.atan2(off[0][1] - c[1], off[0][0] - c[0])
    a1 = math.atan2(off[-1][1] - c[1], off[-1][0] - c[0])
    fit = [(c[0] + rc * math.cos(a0 + (a1 - a0) * i / n), c[1] + rc * math.sin(a0 + (a1 - a0) * i / n))
           for i in range(n + 1)]

    sc, ox, oy = 460, W / 2 - 40, 545

    def P(p):
        return (ox + sc * p[0], oy - sc * p[1])

    b = ""
    b += text(20, 28, "1. The offset of an arc by a VARYING distance is not an arc", 16, weight="bold")
    b += text(20, 48, "Exaggerated about 1000x. Live case: 0.5-0.8 mm offset on a 14 m radius.", 12, col=GREY, italic=True)
    b += poly([P(p) for p in src], GREY, 2.5)
    b += poly([P(p) for p in off], BLUE, 3)
    b += poly([P(p) for p in fit], RED, 2, "7,5")
    for k in (0, n // 2, n):
        x, y = P(off[k])
        b += dot(x, y, RED, 5)
    # offset arrows
    for k in (0, n // 4, n // 2, 3 * n // 4, n):
        x1, y1 = P(src[k])
        x2, y2 = P(off[k])
        b += line(x1, y1, x2, y2, GREY, 1, None, "y")
    # tangents at the right end: true (blue) vs circle (red)
    k = n
    p = off[k]
    tt = norm((off[k][0] - off[k - 1][0], off[k][1] - off[k - 1][1]))
    ct = norm((-(p[1] - c[1]), p[0] - c[0]))
    if ct[0] * tt[0] + ct[1] * tt[1] < 0:
        ct = (-ct[0], -ct[1])
    x, y = P(p)
    L = 75
    b += line(x, y, x + L * tt[0], y - L * tt[1], BLUE, 2.5, None, "b")
    b += line(x, y, x + L * ct[0], y - L * ct[1], RED, 2.5, "6,4", "r")
    ang = math.degrees(math.acos(max(-1, min(1, tt[0] * ct[0] + tt[1] * ct[1]))))
    b += text(x + L * tt[0] - 8, y - L * tt[1] + 16, "true tangent", 12, "end", BLUE)
    b += text(x + L * ct[0] + 10, y - L * ct[1] + 4, "circle's tangent", 12, col=RED)
    b += text(x - 20, y + 30, f"mismatch = the kink ({ang:.0f} deg here;", 12, "end", RED)
    b += text(x - 20, y + 46, "0.001-0.023 deg in the live part)", 12, "end", RED)
    # legend
    lx, ly = 24, 360
    b += line(lx, ly, lx + 30, ly, GREY, 2.5) + text(lx + 38, ly + 4, "source arc", 12)
    b += line(lx + 130, ly, lx + 160, ly, BLUE, 3) + text(lx + 168, ly + 4, "true offset curve", 12)
    b += line(lx + 300, ly, lx + 330, ly, RED, 2, "7,5") + text(lx + 338, ly + 4, "3-point circle classifyPoints fits", 12)
    b += dot(lx + 560, ly, RED, 5) + text(lx + 570, ly + 4, "the 3 points it uses", 12)
    b += text(24, 386, "The circle can sit within tolerance in POSITION and still leave the ends pointing the wrong way.", 12, col=GREY)
    svg("01_offset_not_circle.svg", W, H, b)


# ---------------------------------------------------------------- fig 2: what the kink looks like at a joint
def fig_kink():
    W, H = 760, 300
    b = text(20, 28, "2. Where the kink appears: arc next to a spline", 16, weight="bold")
    J = (380, 185)
    # spline coming in, pinned to the true tangent (direction (1,-0.18))
    b += f'<path d="M 60 245 C 170 225, 290 200, {J[0]} {J[1]}" fill="none" stroke="{BLUE}" stroke-width="3"/>\n'
    # emitted arc leaving with its own tangent (direction (1,-0.55))
    b += f'<path d="M {J[0]} {J[1]} Q 470 135, 700 110" fill="none" stroke="{RED}" stroke-width="3"/>\n'
    b += dot(*J, DARK, 5)
    tt = norm((1, -0.18))
    at = norm((90, -50))
    L = 170
    b += line(J[0], J[1], J[0] + L * tt[0], J[1] + L * tt[1], BLUE, 2, "6,4", "b")
    b += line(J[0], J[1], J[0] + L * at[0], J[1] + L * at[1], RED, 2, "6,4", "r")
    b += text(J[0] + L * tt[0] + 8, J[1] + L * tt[1] + 5, "spline end: pinned to TRUE tangent", 12, col=BLUE)
    b += text(J[0] + L * at[0] + 8, J[1] + L * at[1] - 2, "arc start: whatever the circle gives", 12, col=RED)
    b += f'<path d="M {J[0]+70*tt[0]:.1f} {J[1]+70*tt[1]:.1f} A 70 70 0 0 0 {J[0]+70*at[0]:.1f} {J[1]+70*at[1]:.1f}" fill="none" stroke="{DARK}" stroke-width="1.5"/>\n'
    b += text(J[0] + 78, J[1] - 12, "kink", 13, weight="bold")
    b += text(120, 270, "spline run", 13, col=BLUE)
    b += text(600, 140, "emitted arc", 13, col=RED)
    b += text(J[0], J[1] + 30, "joint (G0 only)", 12, "middle")
    b += text(20, 292, "Measured on SW_Shelf_offset: 0.023 deg at x 1615, 0.0012 deg at x 626.5, 0.001 deg at x 424 (arc to arc).", 12, col=GREY)
    svg("02_kink_at_joint.svg", W, H, b)


# ---------------------------------------------------------------- fig 3: MRS residual (real data)
def fig_residual():
    W, H = 760, 360
    xs = [626.5 + (1248.2 - 626.5) * i / 10 for i in range(11)]
    rs = [0, -0.013, -0.020, -0.020, -0.012, 0, 0.012, 0.020, 0.020, 0.013, 0]
    X0, X1, Y0 = 90, 720, 175
    ys = 110 / 0.025

    def PX(x):
        return X0 + (x - 626.5) / (1248.2 - 626.5) * (X1 - X0)

    def PY(r):
        return Y0 - r * ys

    b = text(20, 28, "3. Live data: the 622 mm MRS run vs its best 3-point circle", 16, weight="bold")
    b += f'<rect x="{X0}" y="{PY(0.01):.1f}" width="{X1-X0}" height="{PY(-0.01)-PY(0.01):.1f}" fill="#E8F4EA"/>\n'
    b += line(X0, PY(0.01), X1, PY(0.01), GREEN, 1.2, "5,4")
    b += line(X0, PY(-0.01), X1, PY(-0.01), GREEN, 1.2, "5,4")
    b += text(X1 + 4, PY(0.01) + 4, "+tol", 11, col=GREEN)
    b += text(X1 + 4, PY(-0.01) + 4, "-tol", 11, col=GREEN)
    b += line(X0, Y0, X1, Y0, GREY, 1)
    b += line(X0, PY(0.025), X0, PY(-0.025), GREY, 1)
    for v in (-0.02, -0.01, 0, 0.01, 0.02):
        b += text(X0 - 8, PY(v) + 4, f"{v:+.2f}", 11, "end", GREY)
    b += text(22, Y0 - 60, "radial", 11, col=GREY) + text(22, Y0 - 46, "error", 11, col=GREY) + text(22, Y0 - 32, "(mm)", 11, col=GREY)
    # smooth-ish curve through samples (Catmull-Rom sampled)
    pts = list(zip(xs, rs))
    dense = []
    for i in range(len(pts) - 1):
        p0 = pts[max(i - 1, 0)]
        p1, p2 = pts[i], pts[i + 1]
        p3 = pts[min(i + 2, len(pts) - 1)]
        for k in range(12):
            t = k / 12
            t2, t3 = t * t, t * t * t
            f = lambda a, b_, c_, d_: 0.5 * (2 * b_ + (-a + c_) * t + (2 * a - 5 * b_ + 4 * c_ - d_) * t2 + (-a + 3 * b_ - 3 * c_ + d_) * t3)
            dense.append((f(p0[0], p1[0], p2[0], p3[0]), f(p0[1], p1[1], p2[1], p3[1])))
    dense.append(pts[-1])
    b += poly([(PX(x), PY(r)) for x, r in dense], RED, 2.5)
    for x, r in pts:
        col = RED if abs(r) > 0.01 else DARK
        b += dot(PX(x), PY(r), col, 4)
    for x in (626.5, 880, 1248.2):
        b += text(PX(x), Y0 + 128, f"x {x:g}" + (" (MRS)" if x == 880 else ""), 11, "middle", GREY)
    b += text(X0, 330, "Max 0.021 mm > 0.01 mm tol, so the run was REJECTED as an arc and emitted as a spline.", 12)
    b += text(X0, 347, "Smooth S-shape = local radius wanders 14138..14965 mm. One biarc may not be enough; expect ~4 arcs.", 12, col=GREY)
    svg("03_mrs_residual.svg", W, H, b)


# ---------------------------------------------------------------- fig 4: snap threshold scale
def fig_snap():
    W, H = 760, 300
    X0, X1 = 70, 710

    def PX(deg):
        return X0 + (math.log10(deg) + 3) / 4 * (X1 - X0)   # 0.001 .. 10 deg

    Y = 150
    b = text(20, 28, "4. The decided rule: 0.57 deg is a SNAP threshold, not an acceptance tolerance", 16, weight="bold")
    b += f'<rect x="{X0}" y="{Y-22}" width="{PX(0.573)-X0:.1f}" height="44" fill="#E8F4EA" stroke="{GREEN}"/>\n'
    b += f'<rect x="{PX(0.573):.1f}" y="{Y-22}" width="{X1-PX(0.573):.1f}" height="44" fill="#FBEAEA" stroke="{RED}"/>\n'
    b += text((X0 + PX(0.573)) / 2, Y + 5, "SNAP: one shared tangent, both sides BUILT to it = 0 kink", 12, "middle", GREEN, "bold")
    b += text((PX(0.573) + X1) / 2, Y - 2, "REAL CORNER", 12, "middle", RED, "bold")
    b += text((PX(0.573) + X1) / 2, Y + 13, "kept G0", 12, "middle", RED)
    for d in (0.001, 0.01, 0.1, 1, 10):
        b += line(PX(d), Y + 22, PX(d), Y + 30, GREY, 1)
        b += text(PX(d), Y + 44, f"{d:g} deg", 11, "middle", GREY)
    b += line(PX(0.573), Y - 40, PX(0.573), Y + 30, DARK, 2)
    b += text(PX(0.573), Y - 46, "0.57 deg (G1_JUNCTION_ANGLE)", 12, "middle", weight="bold")
    # markers
    def mark(lo, hi, yy, label, col):
        s = line(PX(lo), yy, PX(hi), yy, col, 5)
        s += text(PX(lo), yy - 8, label, 11, "start", col)
        return s
    b += mark(0.001, 0.023, Y - 72, "kinks measured today (0.001-0.023)", BLUE)
    b += mark(0.001, 0.23, Y + 70, "upstream defects found (arcFit chords, curveMapping frames) up to 0.23", ORANGE)
    b += line(PX(3), Y + 92, PX(3), Y + 92, DARK)
    b += text(20, Y + 118, "Guard: snap only if reshaping keeps every piece within the approximation tolerance;", 12)
    b += text(20, Y + 134, "otherwise keep the joint as a corner and say so with reportFeatureInfo. (Axis is log scale.)", 12)
    svg("04_snap_threshold.svg", W, H, b)


# ---------------------------------------------------------------- fig 5: single arc vs biarc (real construction)
def perp(t):
    return (-t[1], t[0])


def arc_pts(c, p, q, t_at_p, n=80):
    a0 = math.atan2(p[1] - c[1], p[0] - c[0])
    a1 = math.atan2(q[1] - c[1], q[0] - c[0])
    ccw = ((p[0] - c[0]) * t_at_p[1] - (p[1] - c[1]) * t_at_p[0]) > 0
    if ccw:
        while a1 < a0:
            a1 += 2 * math.pi
    else:
        while a1 > a0:
            a1 -= 2 * math.pi
    r = math.hypot(p[0] - c[0], p[1] - c[1])
    return [(c[0] + r * math.cos(a0 + (a1 - a0) * i / n), c[1] + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(n + 1)]


def arc_center(p, t, q):
    n = perp(t)
    d = (q[0] - p[0], q[1] - p[1])
    r = (d[0] ** 2 + d[1] ** 2) / (2 * (d[0] * n[0] + d[1] * n[1]))
    return (p[0] + r * n[0], p[1] + r * n[1])


def fig_biarc():
    W, H = 760, 360
    p0, p1 = (0.0, 0.0), (4.0, 0.0)
    t0 = (math.cos(math.radians(40)), math.sin(math.radians(40)))
    t1 = (math.cos(math.radians(-28)), math.sin(math.radians(-28)))

    # single arc from p0 with t0 through p1
    cs = arc_center(p0, t0, p1)
    single = arc_pts(cs, p0, p1, t0)
    ts_end = norm((single[-1][0] - single[-2][0], single[-1][1] - single[-2][1]))

    # equal-ratio biarc
    v = (p1[0] - p0[0], p1[1] - p0[1])
    tt = (t0[0] + t1[0], t0[1] + t1[1])
    vt = v[0] * tt[0] + v[1] * tt[1]
    vv = v[0] ** 2 + v[1] ** 2
    a = 2 * (t0[0] * t1[0] + t0[1] * t1[1] - 1)
    d = (vt - math.sqrt(vt * vt - a * vv)) / a
    q = ((p0[0] + d * t0[0] + p1[0] - d * t1[0]) / 2, (p0[1] + d * t0[1] + p1[1] - d * t1[1]) / 2)
    c1 = arc_center(p0, t0, q)
    arc1 = arc_pts(c1, p0, q, t0)
    tq = norm((arc1[-1][0] - arc1[-2][0], arc1[-1][1] - arc1[-2][1]))
    c2 = arc_center(q, tq, p1)
    arc2 = arc_pts(c2, q, p1, tq)

    # target curve: cubic Hermite with the true tangents
    def herm(s):
        h00, h10, h01, h11 = 2*s**3-3*s**2+1, s**3-2*s**2+s, -2*s**3+3*s**2, s**3-s**2
        m = 4.2
        return (h00*p0[0]+h10*m*t0[0]+h01*p1[0]+h11*m*t1[0], h00*p0[1]+h10*m*t0[1]+h01*p1[1]+h11*m*t1[1])
    target = [herm(i / 80) for i in range(81)]

    b = text(20, 28, "5. The fix for arcs: a tangent-constrained BIARC", 16, weight="bold")
    b += text(20, 48, "Given two end points and their TRUE tangents, one arc can match only one tangent. Two arcs can match both.", 12, col=GREY)

    def panel(ox, oy, sc, title):
        return (lambda pt: (ox + sc * pt[0], oy - sc * pt[1])), text(ox + 2 * sc, 80, title, 14, "middle", weight="bold")

    P, t = panel(35, 255, 72, "Today: single arc")
    b += t
    b += poly([P(p) for p in target], GREY, 2, "3,4")
    b += poly([P(p) for p in single], RED, 3)
    for pt, tv, col in ((p0, t0, BLUE), (p1, t1, BLUE)):
        x, y = P(pt)
        b += dot(x, y, DARK, 4) + line(x, y, x + 45 * tv[0], y - 45 * tv[1], col, 2, None, "b")
    x, y = P(p1)
    b += line(x, y, x + 45 * ts_end[0], y - 45 * ts_end[1], RED, 2, "5,4", "r")
    b += text(x - 10, y + 48, "end tangent wrong = kink", 12, "end", RED)

    P, t = panel(415, 255, 72, "Fix: biarc (G1 at the middle joint)")
    b += t
    b += poly([P(p) for p in target], GREY, 2, "3,4")
    b += poly([P(p) for p in arc1], GREEN, 3)
    b += poly([P(p) for p in arc2], ORANGE, 3)
    for pt, tv in ((p0, t0), (p1, t1)):
        x, y = P(pt)
        b += dot(x, y, DARK, 4) + line(x, y, x + 45 * tv[0], y - 45 * tv[1], BLUE, 2, None, "b")
    x, y = P(q)
    b += dot(x, y, DARK, 5)
    b += text(x, y - 14, "joint: shared tangent (G1 is OK)", 11, "middle")
    x, y = P(p1)
    b += text(x - 10, y + 48, "both end tangents exact = 0 kink", 12, "end", GREEN)

    ly = 318
    b += line(24, ly, 54, ly, GREY, 2, "3,4") + text(60, ly + 4, "true offset curve", 12)
    b += line(200, ly, 240, ly, BLUE, 2, None, "b") + text(246, ly + 4, "true tangents (fixed)", 12)
    b += text(24, ly + 26, "If the biarc misses the curve by more than tol: split at the worst point and biarc each half (up to a cap), then fall back to a spline.", 12, col=GREY)
    b += text(24, ly + 42, "Reuse: biarcWithRatio in example_1/refSurfCreation/offsetEdges.fs (3D, exact tangents, joint ratio optimised for deviation).", 12, col=GREY)
    svg("05_biarc.svg", W, H + 30, b)


# ---------------------------------------------------------------- fig 6: evaluate_profiles smoothAcross bug
def fig_smooth_across():
    W, H = 760, 330
    b = text(20, 28, "6. Confirmed bug: Evaluate profiles 'Efficient' merges across real corners", 16, weight="bold")
    b += text(20, 48, "smoothAcross compares edge CHORDS against cos 45 deg; the docstring promises 'a G0 corner never merges'.", 12, col=GREY)
    ox = 0
    for panel_i, title in enumerate(("Today: chords agree (5 deg) -> MERGED", "Fix: compare END TANGENTS at the joint")):
        ox = panel_i * 380
        s = 0.62
        def P(x, y):
            return (ox + 20 + x * s, 70 + y * s)
        b += text(ox + 190, 80, title, 13, "middle", weight="bold")
        A = [(60, 300), (160, 305), (250, 300), (300, 240)]
        B = [(300, 240), (360, 250), (460, 240), (560, 200)]
        def bez(c):
            pts = []
            for i in range(41):
                t = i / 40
                x = (1-t)**3*c[0][0]+3*(1-t)**2*t*c[1][0]+3*(1-t)*t**2*c[2][0]+t**3*c[3][0]
                y = (1-t)**3*c[0][1]+3*(1-t)**2*t*c[1][1]+3*(1-t)*t**2*c[2][1]+t**3*c[3][1]
                pts.append(P(x, y))
            return pts
        b += poly(bez(A), DARK, 3) + poly(bez(B), DARK, 3)
        J = P(300, 240)
        b += dot(*J, DARK, 5)
        if panel_i == 0:
            a0, a1 = P(60, 300), P(560, 200)
            b += line(*a0, *J, BLUE, 2, "6,4", "b")
            b += line(*J, *a1, BLUE, 2, "6,4", "b")
            b += text(ox + 90, 225, "chord A", 12, col=BLUE) + text(ox + 270, 190, "chord B", 12, col=BLUE)
            b += poly(bez([(60, 300), (250, 300), (330, 230), (560, 200)]), RED, 2.5, "8,5")
            b += text(ox + 40, 305, "result: one refitted spline cuts the ~60 deg corner", 12, col=RED)
        else:
            ta = norm((50, -60))
            tb = norm((60, 10))
            L = 80
            b += line(*J, J[0] + L * ta[0], J[1] + L * ta[1], ORANGE, 2.5, None, "o")
            b += line(*J, J[0] + L * tb[0], J[1] + L * tb[1], GREEN, 2.5, None, "g")
            b += text(J[0] + L * ta[0] + 4, J[1] + L * ta[1], "end of A", 12, col=ORANGE)
            b += text(J[0] + L * tb[0] + 4, J[1] + L * tb[1] + 12, "start of B", 12, col=GREEN)
            b += text(ox + 40, 305, "~60 deg > 0.57 deg -> corner KEPT (G0)", 12, col=GREEN)
    b += line(380, 65, 380, 315, "#DDDDDD", 1)
    b += text(20, 325, "Cheap fix: peripheryEdges already evaluates both end tangents (evaluate_profiles.fs ~964) and discards them.", 12, col=GREY)
    svg("06_smooth_across_bug.svg", W, H + 10, b)


# ---------------------------------------------------------------- fig 7: the proposed decision flow
def fig_flow():
    W, H = 760, 560
    b = text(20, 28, "7. Proposed emission logic (new classifyPoints overload, per run)", 16, weight="bold")

    def node(x, y, w, h, label_lines, fill, stroke):
        s = box(x - w / 2, y - h / 2, w, h, fill, stroke)
        n = len(label_lines)
        for i, l in enumerate(label_lines):
            s += text(x, y + (i - (n - 1) / 2) * 15 + 4, l, 12, "middle")
        return s

    g, gs = "#E8F4EA", GREEN
    r, rs_ = "#FBEAEA", RED
    q_, qs = "#EEF3FB", BLUE
    y_, ys = "#FEF5E7", ORANGE
    b += node(380, 70, 360, 40, ["Run of offset points + TRUE start/end tangents", "(joints under 0.57 deg share ONE tangent)"], LIGHT, DARK)
    b += line(380, 90, 380, 120, DARK, 1.5, None, "k")
    b += node(380, 145, 300, 46, ["Source edge type?"], q_, qs)
    # line branch
    b += line(230, 145, 80, 145, DARK, 1.5) + text(160, 138, "LINE", 11, "middle")
    b += node(80, 210, 150, 60, ["Offset linear AND", "tangents = chord?"], q_, qs)
    b += line(80, 125 + 20, 80, 180, DARK, 1.5, None, "k")
    b += line(80, 240, 80, 290, DARK, 1.5, None, "k") + text(88, 268, "yes", 11)
    b += node(80, 315, 130, 40, ["exact LINE"], g, gs)
    b += line(155, 210, 230, 380, DARK, 1.2, "4,3", "k") + text(170, 300, "no", 11)
    # arc branch
    b += line(380, 168, 380, 200, DARK, 1.5, None, "k") + text(388, 190, "ARC", 11)
    b += node(380, 225, 220, 46, ["Offset constant over run?"], q_, qs)
    b += line(490, 225, 570, 225, DARK, 1.5, None, "k") + text(530, 218, "yes", 11)
    b += node(650, 225, 170, 46, ["exact concentric ARC", "(radius R - w)"], g, gs)
    b += line(380, 248, 380, 285, DARK, 1.5, None, "k") + text(388, 272, "no (varying)", 11)
    b += node(380, 310, 260, 46, ["User option: 'Varying offset on arcs'"], y_, ys)
    b += line(250, 310, 250, 380, DARK, 1.5, None, "k") + text(215, 350, "Spline", 11, "middle")
    b += line(510, 310, 560, 310, DARK, 1.5) + line(560, 310, 560, 370, DARK, 1.5, None, "k") + text(590, 350, "Biarc fit", 11, "middle")
    # spline node
    b += node(250, 410, 230, 56, ["SPLINE, end derivatives", "pinned to the shared tangents"], g, gs)
    # other/freeform
    b += line(530, 145, 700, 145, DARK, 1.5) + text(640, 138, "SPLINE / other", 11, "middle")
    b += line(700, 145, 700, 440, DARK, 1.2, "4,3") + line(700, 440, 365, 410, DARK, 1.2, "4,3", "k")
    # biarc
    b += node(560, 395, 170, 46, ["Tangent-constrained", "BIARC"], q_, qs)
    b += line(560, 418, 560, 450, DARK, 1.5, None, "k")
    b += node(560, 475, 170, 46, ["within approx tol?"], q_, qs)
    b += line(560, 498, 560, 520, DARK, 1.5, None, "k") + text(568, 514, "yes", 11)
    b += node(560, 537, 130, 28, ["emit arcs (G1)"], g, gs)
    b += line(475, 475, 420, 475, DARK, 1.5, None, "k") + text(448, 468, "no", 11, "middle")
    b += node(340, 490, 160, 56, ["split at worst point,", "biarc each half", "(until cap -> spline)"], y_, ys)
    b += f'<path d="M 340 462 Q 340 395 470 395" fill="none" stroke="{DARK}" stroke-width="1.2" stroke-dasharray="4,3" marker-end="url(#ak)"/>\n'
    b += text(20, 548, "Default = Spline (see open questions).", 12, col=GREY)
    svg("07_decision_flow.svg", W, H, b)


# ---------------------------------------------------------------- fig 8: affected features map
def fig_map():
    W, H = 760, 560
    b = text(20, 28, "8. What is affected, and how risky it is today", 16, weight="bold")
    col = {"KINK": ("#FBEAEA", RED), "COND": ("#FEF5E7", ORANGE), "SAFE": ("#E8F4EA", GREEN), "BUG": ("#FBEAEA", RED)}

    def item(x, y, w, label, sub, kind):
        f, s = col[kind]
        o = box(x, y, w, 40, f, s)
        o += text(x + 8, y + 17, label, 12, weight="bold")
        o += text(x + 8, y + 32, sub, 11, col=GREY)
        return o

    # hub
    b += box(290, 55, 180, 60, "#EEF3FB", BLUE)
    b += text(380, 78, "curve_core.fs", 13, "middle", BLUE, "bold")
    b += text(380, 95, "classifyPoints / emitArcCurve", 11, "middle")
    b += text(380, 108, "(Curve_tools doc)", 11, "middle", GREY)
    callers = [
        ("Driven edge offset", "arcs + lines, source-type gates only", "KINK"),
        ("Driven offset surface", "per section + alignedRunMerges", "KINK"),
        ("Map curve", "NO gates: spline can become arc/line", "KINK"),
        ("Unwrap", "tangent check, but 0.57 deg too loose", "COND"),
        ("Evaluate offset", "lines by sagitta, arcs off", "COND"),
        ("Offset run treatment", "cornerArc with height offset", "COND"),
    ]
    for i, (a, s_, k) in enumerate(callers):
        x = 20 + (i % 3) * 245
        y = 150 + (i // 3) * 55
        b += line(380, 115, x + 115, y, "#BBBBBB", 1)
        b += item(x, y, 230, a, s_, k)
    b += text(20, 285, "Own copies of the same idea (fixed separately):", 13, weight="bold")
    others = [
        ("Evaluate profiles", "private 3-pt circle + smoothAcross BUG", "BUG"),
        ("arcFit (footprint)", "fast path, line-before-biarc, fallback", "KINK"),
        ("scaleFootprint", "arc-chain ends, startPhi/endPhi unused", "KINK"),
        ("integrateFootprint", "FIT transitions, no end derivatives", "KINK"),
        ("curveMapping (+public)", "near-line promotion: 0.23 deg frame jump", "KINK"),
        ("Clean wire", "MANUAL groups fit through any corner", "COND"),
    ]
    for i, (a, s_, k) in enumerate(others):
        x = 20 + (i % 3) * 245
        y = 300 + (i // 3) * 55
        b += item(x, y, 230, a, s_, k)
    b += text(20, 435, "Already tangent by construction (no change):", 13, weight="bold")
    safe = [("Fillet wire", "solved tangent arcs"), ("offsetEdges biarc", "the code to reuse"),
            ("Create offset profile", "exact Bernstein pieces"), ("integrateFootprint arcs", "exact ODE arcs")]
    for i, (a, s_) in enumerate(safe):
        b += item(20 + i * 184, 450, 172, a, s_, "SAFE")
    ly = 525
    for i, (k, lbl) in enumerate((("KINK", "kinks today"), ("COND", "conditional / partial"), ("SAFE", "safe"))):
        f, s = col[k]
        b += box(20 + i * 190, ly, 18, 14, f, s, 3) + text(44 + i * 190, ly + 12, lbl, 12)
    svg("08_affected_features.svg", W, H, b)


fig_offset_not_circle()
fig_kink()
fig_residual()
fig_snap()
fig_biarc()
fig_smooth_across()
fig_flow()
fig_map()
print("ok")
