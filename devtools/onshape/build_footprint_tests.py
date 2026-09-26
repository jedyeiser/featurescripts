"""Build the footprint-suite test cases as real features in the footprint document's "Footprint tests" Part Studio
(created when missing; features upserted by name). Fixtures are NATIVE features only: sketches (lines, arcs,
interpolated splines) in the Top plane and composite curves. Checked by check_footprint_tests.py, which imports
this module for the geometry and the expected values (so the Onshape work lives in main()).

Fixture half sidecuts (+Y only; mm; u = distance along the ski from the waist, FB side u < 0):
  S14   core arc R14 m for |u| <= 550, reverse flank arcs R3 m to the contact points |u| = 750, reverse tip / tail
        arcs R0.3 m down to y = 0 (tip length 174.567). Waist half-width 50. Inflections (curvature jumps) at
        |u| = 550, widest points at |u| = 667.857.
  CHAIN core R14 m for |u| <= 400, R25 m to |u| = 550, then as S14.
  ASYM  S14 with a R2 m forebody flank and a R4 m aftbody flank (tapered: the tail is wider).
Footprint wires are native composite curves of the sketch edges (a composite curve orients its edges head to
tail; raw sketch arcs are all counter-clockwise -- see AF7).

  FX1  S14, tip at -X, centred on X = 0            FX2  CHAIN at X = 4000        FX3  CHAIN at X = 8000, 13 edges
  FX4  S14, tip at +X, at X = 12000                FX5  S14 at X = 16000         FX6  ASYM at X = 20000
  FX9  S14 at X = 24000, contact-to-contact sidecut drawn as ONE interpolated spline, tip/tail arcs
  FX10 spline through 9 points of a R14 m arc (X = 28000)
  FX11 S-curve spline through S14 points u = 0..750 (X = 32000)
  FX12 FX11 with 2 mm spans at the inflection (548, 550, 552) (X = 36000)
  IFn  radius-profile sketches (10 mm of y = 1 m of radius) near X = 0 (waist-driven: waistLocation is bounded
       to +-100 mm about X = 0) or at X = 40000 / 44000 (taper-driven)

  AF1-AF7 Analyze footprint (Sketch key points on): waist / widest / inflection lines, natural and average radius
  GP1-GP3 Generate Footprint Points (points sketched): counts, on-curve, equal spacing, region bounds
  AR1-AR4 Arc fit: arcs/lines only, within tolerance of the input both ways (no gaps), one wire
  IF1-IF7 Integrate footprint: exact arcs, radii, centres, G1, waist / taper drivers
  SF1-SF5 Scale Footprint: accordion, keep taper, scale radius, spline sidecut (knot preservation)

The case table (ids, names with the expected results, expected values) is CASES below.

usage (repo root): PYTHONPATH=. python devtools/onshape/build_footprint_tests.py
"""
import copy
import json
import math

# =====================================================================================================
# Analytic geometry (pure Python, no Onshape; imported by check_footprint_tests.py)
# =====================================================================================================
W = 50.0  # waist half-width of every Analyze / Scale / Points fixture (mm)
S14 = [(550.0, 14000.0), (750.0, -3000.0), ("tip", -300.0)]
CHAIN = [(400.0, 14000.0), (550.0, 25000.0), (750.0, -3000.0), ("tip", -300.0)]
ASYM_FB = [(550.0, 14000.0), (750.0, -2000.0), ("tip", -300.0)]
ASYM_AB = [(550.0, 14000.0), (750.0, -4000.0), ("tip", -300.0)]


def side(w, segs):
    """Arcs of one side, walking out from the waist (u = 0, slope 0, y = w). segs: [(u_end, R)], R signed
    (+ = centre above, i.e. concave toward +Y), the last one may be ("tip", R) = run on until y = 0.
    A circle parametrised by u has sin(phi) linear in u with slope 1/R, and y = cy - R cos(phi)."""
    u, s, y = 0.0, 0.0, w
    arcs = []
    for end, R in segs:
        phi0 = math.asin(s)
        if end == "tip":
            phi1 = -math.acos(math.cos(phi0) + y / R)
            s1 = math.sin(phi1)
            u1 = u + (s1 - s) * R
        else:
            u1 = end
            s1 = s + (u1 - u) / R
            phi1 = math.asin(s1)
        arcs.append({"u0": u, "u1": u1, "R": R, "cu": u - R * s, "cy": y + R * math.cos(phi0), "tip": end == "tip"})
        u, s, y = u1, s1, y + R * (math.cos(phi0) - math.cos(phi1))
    return arcs


def circumradius(p, q, r):
    ax, ay = q[0] - p[0], q[1] - p[1]
    bx, by = r[0] - p[0], r[1] - p[1]
    cr = abs(ax * by - ay * bx)
    return math.dist(p, q) * math.dist(q, r) * math.dist(r, p) / (2 * cr)


class Profile:
    """A half sidecut (y >= 0) in world X / Y (mm): x = x0 + d * u (d = +1: tip at -X, d = -1: tip at +X)."""

    def __init__(self, key, x0, d, fb, ab, w=W, splits=()):
        self.key, self.x0, self.d, self.w = key, x0, d, w
        self.sides = {-1: side(w, fb), 1: side(w, ab)}
        self.splits = sorted(splits)

    def seg(self, u):
        arcs = self.sides[1 if u >= 0 else -1]
        a = abs(u)
        for g in arcs:
            if a <= g["u1"] + 1e-9:
                return g
        return arcs[-1]

    def y(self, u):
        g = self.seg(u)
        s = (abs(u) - g["cu"]) / g["R"]
        return g["cy"] - g["R"] * math.cos(math.asin(max(-1.0, min(1.0, s))))

    def slope(self, u):
        """dy/du (u increasing)."""
        g = self.seg(u)
        s = (abs(u) - g["cu"]) / g["R"]
        t = s / math.sqrt(1 - s * s)
        return t if u >= 0 else -t

    def x(self, u):
        return self.x0 + self.d * u

    def pt(self, u):
        return (self.x(u), self.y(u))

    def contact_u(self, sgn):
        """Contact point (FCP for sgn = -1, ACP for +1) = start of the tip / tail arc."""
        return sgn * [g for g in self.sides[sgn] if g["tip"]][0]["u0"]

    def end_u(self, sgn):
        return sgn * self.sides[sgn][-1]["u1"]

    def fcp(self):
        return self.pt(self.contact_u(-1))

    def acp(self):
        return self.pt(self.contact_u(1))

    def tip_end(self):
        return (self.x(self.end_u(-1)), 0.0)

    def tail_end(self):
        return (self.x(self.end_u(1)), 0.0)

    def waist(self):
        return self.pt(0.0)

    def widest(self, sgn):
        g = [g for g in self.sides[sgn] if g["R"] < 0 and g["u0"] <= g["cu"] <= g["u1"]][0]
        return (self.x(sgn * g["cu"]), g["cy"] - g["R"])

    def inflection(self, sgn):
        arcs = self.sides[sgn]
        i = 0
        while arcs[i + 1]["R"] > 0:
            i += 1
        return self.pt(sgn * arcs[i]["u1"])

    def natural_inflection(self):
        return circumradius(self.inflection(-1), self.waist(), self.inflection(1))

    def natural_widest(self):
        return circumradius(self.widest(-1), self.waist(), self.widest(1))

    def taper(self):
        """Degrees; + when the forebody (FCP side) widest point is wider (fpt_analyze computeTaperAngle)."""
        f, a = self.widest(-1), self.widest(1)
        return math.degrees(math.atan2(f[1] - a[1], abs(f[0] - a[0])))

    def average_radius(self):
        """The definition in fpt_analyze computeAverageRadius: mean of |R| at 200 stations at the midpoints of
        equal x intervals between the inflection points (metres)."""
        xa, xb = sorted([self.inflection(-1)[0], self.inflection(1)[0]])
        dx = (xb - xa) / 200
        total = 0.0
        for st in range(200):
            u = (xa + (st + 0.5) * dx - self.x0) / self.d
            total += abs(self.seg(u)["R"])
        return total / 200 / 1000

    def arcs(self):
        """World arcs from tip end to tail end: {cx, cy, r, u0, u1} (u0 < u1), split at self.splits."""
        pieces = []
        for sgn in (-1, 1):
            for g in self.sides[sgn]:
                lo, hi = sorted([sgn * g["u0"], sgn * g["u1"]])
                pieces.append([lo, hi, self.x(sgn * g["cu"]), g["cy"], abs(g["R"])])
        pieces.sort(key=lambda v: v[0])
        merged = [pieces[0]]
        for v in pieces[1:]:
            m = merged[-1]
            if abs(m[1] - v[0]) < 1e-9 and abs(m[2] - v[2]) < 1e-9 and abs(m[3] - v[3]) < 1e-9 and m[4] == v[4]:
                m[1] = v[1]  # the two halves of the core (same circle) are one arc
            else:
                merged.append(v)
        out = []
        for lo, hi, cx, cy, r in merged:
            cuts = [lo] + [v for v in self.splits if lo + 1e-6 < v < hi - 1e-6] + [hi]
            for a, b in zip(cuts, cuts[1:]):
                out.append({"cx": cx, "cy": cy, "r": r, "u0": a, "u1": b})
        return out

    def tangent(self, u):
        """Unit tangent in world X / Y, pointing toward increasing world x."""
        t = (self.d, self.slope(u))
        if self.d < 0:
            t = (1.0, -self.slope(u))
        n = math.hypot(*t)
        return (t[0] / n, t[1] / n)


FIX = {
    "FX1": Profile("FX1", 0.0, 1, S14, S14),
    "FX2": Profile("FX2", 4000.0, 1, CHAIN, CHAIN),
    "FX3": Profile("FX3", 8000.0, 1, CHAIN, CHAIN, splits=[-650, -480, -150, 100, 480, 650]),
    "FX4": Profile("FX4", 12000.0, -1, S14, S14),
    "FX5": Profile("FX5", 16000.0, 1, S14, S14),
    "FX6": Profile("FX6", 20000.0, 1, ASYM_FB, ASYM_AB),
    "FX9": Profile("FX9", 24000.0, 1, S14, S14),
}
FX10_ARC = Profile("FX10", 28000.0, 1, S14, S14)
FX11_S = Profile("FX11", 32000.0, 1, S14, S14)
FX12_S = Profile("FX12", 36000.0, 1, S14, S14)
FX9_U = [-750.0 + 150.0 * i for i in range(11)]
FX10_U = [-550.0 + 137.5 * i for i in range(9)]
FX11_U = [0.0, 100.0, 200.0, 300.0, 400.0, 500.0, 550.0, 600.0, 650.0, 700.0, 750.0]
FX12_U = [0.0, 100.0, 200.0, 300.0, 400.0, 500.0, 548.0, 550.0, 552.0, 600.0, 650.0, 700.0, 750.0]


def r3(v):
    return round(v, 3)


def fx_names(key):
    """Names of the fixture features (the part before ':' is the lookup key used by the checker)."""
    p = FIX.get(key)
    return {
        "sketch": "%s sketch: half sidecut at X = %g%s" % (key, p.x0 if p else 0, "" if not p or p.d > 0 else ", tip at +X"),
        "wire": "%s wire: composite curve of the sketch edges" % key,
        "rsl": "%s RSL: FCP to ACP on y = 0" % key,
        "rsl2": "%s RSL2: x +-700 about the waist (contacts inside the flanks)" % key,
        "newrsl": "%s new RSL: contacts at +-825 about the waist (RSL x 1.1)" % key,
    }


# ---- integrate footprint: exact solutions of the radius profiles ----
def integrate_arcs(w, segs_ab, segs_fb=None, x0=0.0):
    """Exact footprint of constant-radius profile sections: [(cx, cy, R)] sorted by x (world mm)."""
    p = Profile("IF", x0, 1, segs_fb or segs_ab, segs_ab, w=w)
    return p, [(a["cx"], a["cy"], a["r"]) for a in p.arcs()]


def taper_centre(R, half, taper_deg):
    """Centre offset uc of a single arc over u in [-half, half] whose end heights differ by the taper
    (FB end, u = -half, higher when taper > 0): sqrt(R^2 - (half - uc)^2) - sqrt(R^2 - (half + uc)^2) = 2 half tan."""
    target = 2 * half * math.tan(math.radians(taper_deg))
    lo, hi = -half, half
    for _ in range(200):
        uc = (lo + hi) / 2
        f = math.sqrt(R * R - (half - uc) ** 2) - math.sqrt(R * R - (half + uc) ** 2) - target
        if f > 0:
            hi = uc
        else:
            lo = uc
    return (lo + hi) / 2


# =====================================================================================================
# The cases (single source of truth for the builder and the checker)
# =====================================================================================================
CASES = []
EXPECT_FAIL = {}  # case id -> bug reference (the checker prints XFAIL until it passes, then XPASS = fix the list)


def add(cid, name, kind, **kw):
    CASES.append(dict(id=cid, name="%s %s" % (cid, name), kind=kind, **kw))


def analyze_expect(p):
    return {
        "waist": p.waist(), "fbWidest": p.widest(-1), "abWidest": p.widest(1),
        "fbInflection": p.inflection(-1), "abInflection": p.inflection(1),
        "naturalInflection": p.natural_inflection() / 1000, "naturalWidest": p.natural_widest() / 1000,
        "averageRadius": p.average_radius(), "taper": p.taper(),
    }


for cid, fx, what in [
    ("AF1", "FX1", "S14 wire, tip at -X, centred on X = 0"),
    ("AF2", "FX2", "chain 14 m core + 25 m flanks, 7 edges"),
    ("AF3", "FX3", "same chain split into 13 edges (split must not change it)"),
    ("AF4", "FX4", "S14 wire, tip at +X"),
    ("AF5", "FX5", "S14 wire, not centred (waist at X = 16000)"),
    ("AF6", "FX6", "asymmetric flanks R2 m / R4 m (tail wider)"),
]:
    p = FIX[fx]
    e = analyze_expect(p)
    add(cid, "%s -> avg radius %.2f m, natural (inflection) %.2f m, inflections x %g / %g, taper %.4f deg" % (
        what, e["averageRadius"], e["naturalInflection"], r3(e["fbInflection"][0]), r3(e["abInflection"][0]), e["taper"]),
        "analyze", fixture=fx, source="wire", expect=e)

e = analyze_expect(FIX["FX1"])
add("AF7", "FX1 raw sketch edges (arcs all counter-clockwise) -> inflections x -550 / 550, avg radius 14.00 m",
    "analyze", fixture="FX1", source="sketch", expect=e)


def points_expect(p, contact=None, counts=(23, 80, 23)):
    """Region x bounds for Generate Footprint Points: tip = tip end .. FCP, RSL = FCP .. ACP, tail = ACP .. tail end."""
    fx_, ax_ = (p.x(-contact), p.x(contact)) if contact else (p.fcp()[0], p.acp()[0])
    te, ta = p.tip_end()[0], p.tail_end()[0]
    return {"regions": [
        {"name": "Tip_Sketch", "count": counts[0], "xlo": min(te, fx_), "xhi": max(te, fx_)},
        {"name": "RSL_Sketch", "count": counts[1], "xlo": min(fx_, ax_), "xhi": max(fx_, ax_)},
        {"name": "Tail_Sketch", "count": counts[2], "xlo": min(ta, ax_), "xhi": max(ta, ax_)},
    ]}


p = FIX["FX1"]
add("GP1", "S14 wire, RSL -750..750 -> 23 tip points x -924.567..-750, 80 RSL points -750..750, 23 tail points, equal spacing",
    "points", fixture="FX1", rsl="rsl", expect=points_expect(p))
add("GP2", "S14 wire, tip at +X (Tip toward +X on) -> 23 tip points x 12750..12924.567 (tip end), 80 RSL points, 23 tail points x 11075.433..11250",
    "points", fixture="FX4", rsl="rsl", tipAtPositiveX=True, expect=points_expect(FIX["FX4"]))
add("GP3", "S14 wire, RSL -700..700 inside the flanks (edges split at the planes) -> RSL points -700..700, tip -924.567..-700",
    "points", fixture="FX1", rsl="rsl2", expect=points_expect(p, contact=700.0))

add("AR1", "S14 wire (5 exact arcs), tol 0.01 mm -> 5 arcs R0.3 / 3 / 14 / 3 / 0.3 m, one wire, within 0.01 mm",
    "arcfit", source=("wire", "FX1"), tol=0.01, minLength=1.0,
    expect={"radii": sorted([a["r"] for a in FIX["FX1"].arcs()])})
add("AR2", "spline through 9 points of a R14 m arc, tol 0.01 mm -> arcs/lines only, within 0.01 mm both ways, one wire",
    "arcfit", source=("spline", "FX10"), tol=0.01, minLength=1.0, expect={})
add("AR3", "S-curve spline (R14 core -> R3 reverse flank), tol 0.01 mm -> arcs/lines only, within 0.01 mm both ways, one wire",
    "arcfit", source=("spline", "FX11"), tol=0.01, minLength=1.0, expect={})
add("AR4", "S-curve spline with 2 mm knot spans at the inflection, min length 10 mm -> no gap: one wire within 0.01 mm both ways",
    "arcfit", source=("spline", "FX12"), tol=0.01, minLength=10.0, expect={})
# AR4 (short knot-span block dropped -> gap) fixed 2026-09-25; the case now guards against regressions.


def if_case(cid, text, lines, w, fcp, driver="WAIST", waist_loc=0.0, taper=0.0, strict=False, expect=None):
    add(cid, text, "integrate", lines=lines, waistWidth=2 * w, fcp=fcp, driver=driver, waistLocation=waist_loc,
        taperAngle=taper, strict=strict, expect=expect or {})


def arc_list(arcs, centres=True):
    return [{"R": r, "c": (cx, cy) if centres else None} for cx, cy, r in arcs]


_, a1 = integrate_arcs(100.0, [(550.0, 14000.0)])
if_case("IF1", "one line R14 m, waist 0, width 200 -> one exact arc R14000 centre (0, 14100), ends x -550 / 550",
        [((-550, 140), (550, 140))], 100.0, (-550, 140),
        expect={"edges": arc_list(a1), "waist": (0.0, 100.0), "ctol": (0.1, 0.02)})
p2, a2 = integrate_arcs(100.0, [(400.0, 14000.0), (550.0, 25000.0)])
if_case("IF2", "lines R25 / R14 / R25 m -> 3 exact arcs R25000 / 14000 / 25000, G1, waist (0, 100)",
        [((-550, 250), (-400, 250)), ((-400, 140), (400, 140)), ((400, 250), (550, 250))], 100.0, (-550, 250),
        expect={"edges": arc_list(a2), "waist": (0.0, 100.0), "ctol": (0.1, 0.05)})
p3, a3 = integrate_arcs(100.0, [(550.0, 14000.0), (750.0, -3000.0)])
if_case("IF3", "R14 m core + reverse R3 m flanks -> 3 exact arcs, widest x -667.857 / 667.857, waist (0, 100)",
        [((-750, -30), (-550, -30)), ((-550, 140), (550, 140)), ((550, -30), (750, -30))], 100.0, (-750, -30),
        expect={"edges": arc_list(a3), "waist": (0.0, 100.0), "ctol": (0.1, 0.05),
                "widest": [p3.widest(-1), p3.widest(1)]})
if_case("IF4", "one line R14 m, waist location 50, width 250 -> arc centre (50, 14125)",
        [((-550, 140), (550, 140))], 125.0, (-550, 140), waist_loc=50.0,
        expect={"edges": [{"R": 14000.0, "c": (50.0, 14125.0)}], "waist": (50.0, 125.0), "ctol": (0.15, 0.02)})
uc5 = taper_centre(14000.0, 550.0, 0.25)
if_case("IF5", "one line R14 m at X = 40000, taper 0.25 deg, width 250 -> arc centre x %.3f, end heights give 0.25 deg" % (40000 + uc5),
        [((39450, 140), (40550, 140))], 125.0, (39450, 140), driver="TAPER_ANGLE", taper=0.25,
        expect={"edges": [{"R": 14000.0, "c": (40000.0 + uc5, 14125.0)}], "waist": (40000.0 + uc5, 125.0),
                "ctol": (0.5, 0.05), "taper": 0.25})
if_case("IF6", "R14 m core line + sloped 14 -> 25 m transitions -> one wire, exact R14000 core, 2 spline transitions, G1",
        [((-550, 250), (-400, 140)), ((-400, 140), (400, 140)), ((400, 140), (550, 250))], 100.0, (-550, 250),
        expect={"edges": [{"R": None}, {"R": 14000.0, "c": (0.0, 14100.0)}, {"R": None}], "waist": (0.0, 100.0),
                "ctol": (0.1, 0.05)})
if_case("IF7", "sloped profile only (14 -> 25 m), Strict on -> every output edge an exact arc or line (reports a radius)",
        [((43450, 140), (44550, 250))], 100.0, (43450, 140), driver="TAPER_ANGLE", taper=0.0, strict=True,
        expect={"allAnalytic": True})


def scale_expect(p, k):
    """Accordion by k about the FCP (tip at -X fixtures): contacts at waist +- 750 k, tip / tail translated."""
    fcpx, acpx = p.x0 - 750 * k, p.x0 + 750 * k
    return {
        "newFcp": fcpx, "newAcp": acpx,
        "tipEnd": (fcpx - (p.fcp()[0] - p.tip_end()[0]), 0.0),
        "tailEnd": (acpx + (p.tail_end()[0] - p.acp()[0]), 0.0),
    }


p = FIX["FX1"]
e = scale_expect(p, 1.1)
e.update({"contacts": [(e["newFcp"], p.fcp()[1]), (e["newAcp"], p.acp()[1])], "waist": (0.0, W),
          "widest": [(p.widest(-1)[0] * 1.1, p.widest(-1)[1]), (p.widest(1)[0] * 1.1, p.widest(1)[1])], "mirror": True})
add("SF1", "accordion S14 to RSL 1650 -> contacts x -825 / 825 at y 61.999, tip end x -999.567, waist (0, 50), widest y 63.124, -Y mirror",
    "scale", fixture="FX1", newrsl="newrsl", mode="ACCORDION", expect=e)
p = FIX["FX6"]
e = scale_expect(p, 1.1)
e.update({"taper": p.taper(), "acpWidth": (e["newAcp"], p.acp()[1]), "mirror": True})
add("SF2", "keep taper (pin ACP) ASYM to RSL 1650 -> taper %.4f deg kept (accordion alone gives %.4f), ACP width %.3f kept" % (
    p.taper(), math.degrees(math.atan2(p.widest(-1)[1] - p.widest(1)[1], 1.1 * (p.widest(1)[0] - p.widest(-1)[0]))), p.acp()[1]),
    "scale", fixture="FX6", newrsl="newrsl", mode="KEEP_TAPER", expect=e)
# SF3 / SF4 (Scale radius) removed with the mode, 2026-09-25.
p = FIX["FX9"]
e = scale_expect(p, 1.1)
e.update({"contacts": [(e["newFcp"], p.fcp()[1]), (e["newAcp"], p.acp()[1])], "affine": {"x0": p.x0, "k": 1.1}})
add("SF5", "accordion of a SPLINE sidecut to RSL 1650 -> output = exact x-scaled image of the reference spline (knots kept), within 0.001 mm",
    "scale", fixture="FX9", newrsl="newrsl", mode="ACCORDION", expect=e)


# =====================================================================================================
# Onshape (main only)
# =====================================================================================================
STUDIO = "Footprint tests"
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'
FEATURE_TABS = {"analyzeFootprint": "analyzeFootprint", "arcFit": "arcFit", "getFootprintPoints": "getFootprintPoints",
                "integrateFootprint": "integrateFootprint", "scaleFootprint": "scaleFootprint"}
STATE = {"features": None}


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def upsert(feature):
    c, base = STATE["client"], STATE["base"]
    if STATE["features"] is None:
        STATE["features"] = c.get(f"{base}/features")
    f = STATE["features"]
    existing = [x for x in f["features"] if x["name"] == feature["name"]]
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    if existing:
        feature["featureId"] = existing[0]["featureId"]
        r = c.post(f"{base}/features/featureid/{existing[0]['featureId']}", body)
    else:
        r = c.post(f"{base}/features", body)
    STATE["features"] = None
    print("%-110s %s" % (feature["name"][:110], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    return upsert(f)


UNIT = {"meter": "m", "millimeter": "mm", "centimeter": "cm", "inch": "in", "degree": "deg", "radian": "rad", "": ""}


def default_param(p):
    """The spec's default value for a parameter, with an expression filled in for quantities (correction 38)."""
    d = copy.deepcopy(p["defaultValue"])
    d["parameterId"] = p["parameterId"]
    if d["btType"].startswith("BTMParameterQuantity") and not d.get("expression"):
        rng = (p.get("ranges") or [{}])[0]
        value = rng.get("defaultValue", d.get("value", 0))
        if p.get("quantityType") == "INTEGER":
            d["expression"] = "%d" % round(value)
            d["isInteger"] = True
        else:
            d["expression"] = ("%r %s" % (value, UNIT.get(rng.get("units", ""), rng.get("units", "")))).strip()
    return d


def custom(name, ftype, given=None, enums=None):
    """An instance of a footprint feature; every parameter the spec defines is sent (correction 38), custom enums
    carry the feature studio's namespace (copied from the spec default)."""
    spec = STATE["specs"][ftype]
    given = {p["parameterId"]: p for p in (given or [])}
    enums = enums or {}
    params = []
    for p in spec["parameters"]:
        pid = p["parameterId"]
        if pid in given:
            params.append(given[pid])
        elif pid in enums:
            d = default_param(p)
            d["value"] = enums[pid]
            params.append(d)
        elif isinstance(p.get("defaultValue"), dict):
            params.append(default_param(p))
    return feature(name, ftype, params, spec["namespace"])


# ---- native sketch geometry (the REST sketch format is metres) ----
def seg(eid, x0, y0, x1, y1):
    x0, y0, x1, y1 = x0 / 1000, y0 / 1000, x1 / 1000, y1 / 1000
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def arc(eid, cx, cy, r, p0, p1):
    """Sketch arc (mm in) on centre (cx, cy), radius r, between the world points p0 and p1 (the minor arc)."""
    t0 = math.atan2(p0[1] - cy, p0[0] - cx)
    t1 = math.atan2(p1[1] - cy, p1[0] - cx)
    dt = (t1 - t0) % (2 * math.pi)
    a0, a1 = (t0, t0 + dt) if dt <= math.pi else (t1, t1 + 2 * math.pi - dt)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r / 1000, "xCenter": cx / 1000, "yCenter": cy / 1000,
                         "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def spline(eid, pts, t0, t1):
    """Interpolated sketch spline through pts (mm) with end tangents t0 / t1 (unit, along the point order), in the
    format Onshape stores (centripetal knots; handle = end point +- derivative * span / 3)."""
    P = [(x / 1000, y / 1000) for x, y in pts]
    ch = [math.dist(P[i], P[i + 1]) for i in range(len(P) - 1)]
    qs = [math.sqrt(v) for v in ch]
    du = [v / sum(qs) for v in qs]
    s0, s1 = ch[0] / du[0], ch[-1] / du[-1]
    d0, d1 = (t0[0] * s0, t0[1] * s0), (t1[0] * s1, t1[1] * s1)
    n = len(P)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": 1.0, "isConstruction": False, "centerId": "",
            "internalIds": ["%s.%d.internal" % (eid, i) for i in range(n)] + [eid + ".startHandle", eid + ".endHandle"],
            "geometry": {"btType": "BTCurveGeometryInterpolatedSpline-116", "isPeriodic": False, "derivatives": {},
                         "interpolationPoints": [v for p in P for v in p],
                         "startDerivativeX": d0[0], "startDerivativeY": d0[1], "endDerivativeX": d1[0], "endDerivativeY": d1[1],
                         "startHandleX": P[0][0] + d0[0] * du[0] / 3, "startHandleY": P[0][1] + d0[1] * du[0] / 3,
                         "endHandleX": P[-1][0] - d1[0] * du[-1] / 3, "endHandleY": P[-1][1] - d1[1] * du[-1] / 3},
            "parameters": [b("geometryIsPeriodic", False), b(".hasHandlesInSketch", True)]
            + [b(".%d.hasInternalHandle" % i, i in (0, n - 1)) for i in range(n)]
            + [num("splinePointParamCount", "0.0"), num("splinePointCount", "%d.0" % n)]}


def sketch(name, entities):
    return upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                   "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})


def edges(fid):
    """Non-construction edges of a sketch."""
    return ('qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), '
            'EntityType.EDGE), ConstructionObject.NO)' % fid)


def wire_edges(fid):
    return 'qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE)' % fid


def vertex_at(fid, x, y):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(%.6f, %.6f, 0) * millimeter)' % (fid, x, y)


def arc_entities(p, prefix="a"):
    out = []
    for i, a in enumerate(p.arcs()):
        out.append(arc("%s%d" % (prefix, i), a["cx"], a["cy"], a["r"], p.pt(a["u0"]), p.pt(a["u1"])))
    return out


def spline_through(p, us, eid="s"):
    pts = [p.pt(u) for u in sorted(us, key=p.x)]
    ends = sorted(us, key=p.x)
    return spline(eid, pts, p.tangent(ends[0]), p.tangent(ends[-1]))


def build_fixtures():
    F = {}
    for key, p in FIX.items():
        n = fx_names(key)
        if key == "FX9":
            # tip / tail arcs, the contact-to-contact sidecut as one spline
            tips =[a for a in p.arcs() if a["u1"] <= p.contact_u(-1) + 1e-9 or a["u0"] >= p.contact_u(1) - 1e-9]
            ents = [arc("t%d" % i, a["cx"], a["cy"], a["r"], p.pt(a["u0"]), p.pt(a["u1"])) for i, a in enumerate(tips)]
            ents.append(spline_through(p, FX9_U))
        else:
            ents = arc_entities(p)
        F[key + ".sketch"] = sketch(n["sketch"], ents)
        F[key + ".wire"] = feature(n["wire"], "compositeCurve", [q("edges", edges(F[key + ".sketch"]))])
        f, a = p.fcp(), p.acp()
        F[key + ".rsl"] = sketch(n["rsl"], [seg("l", f[0], 0, a[0], 0)])
        if key in ("FX1", "FX6", "FX9"):
            F[key + ".newrsl"] = sketch(n["newrsl"], [seg("l", p.x0 - 825, 0, p.x0 + 825, 0)])
        if key == "FX1":
            F[key + ".rsl2"] = sketch(n["rsl2"], [seg("l", p.x0 - 700, 0, p.x0 + 700, 0)])
    for key, p, us in [("FX10", FX10_ARC, FX10_U), ("FX11", FX11_S, FX11_U), ("FX12", FX12_S, FX12_U)]:
        F[key + ".sketch"] = sketch("%s sketch: interpolated spline through %d points of S14 at X = %g" % (key, len(us), p.x0),
                                    [spline_through(p, us)])
    return F


def build_case(case, F):
    kind = case["kind"]
    if kind == "analyze":
        fx = case["fixture"]
        p = FIX[fx]
        src = wire_edges(F[fx + ".wire"]) if case["source"] == "wire" else edges(F[fx + ".sketch"])
        rsl = F[fx + ".rsl"]
        return custom(case["name"], "analyzeFootprint", [
            q("fptEdges", src), q("rslEdge", edges(rsl)),
            q("fcpQuery", vertex_at(rsl, p.fcp()[0], 0)), q("acpQuery", vertex_at(rsl, p.acp()[0], 0)),
            b("outputSketch", True)])
    if kind == "points":
        fx = case["fixture"]
        return custom(case["name"], "getFootprintPoints", [
            q("fptEdges", wire_edges(F[fx + ".wire"])), q("rslQuery", edges(F[fx + "." + case["rsl"]])),
            b("sketchPoints", True), b("retainCurves", False), b("retainPlanes", False),
            b("tipAtPositiveX", case.get("tipAtPositiveX", False))],
            {"sourceType": "EDGES"})
    if kind == "arcfit":
        how, fx = case["source"]
        src = wire_edges(F[fx + ".wire"]) if how == "wire" else edges(F[fx + ".sketch"])
        return custom(case["name"], "arcFit", [
            q("selEdges", src), num("posTol", "%g mm" % case["tol"]), num("planeTol", "0.01 mm"),
            num("minLength", "%g mm" % case["minLength"])], {"outputType": "CURVES"})
    if kind == "integrate":
        cid = case["id"]
        prof = sketch("%s profile: radius lines (10 mm of y = 1 m of radius)" % cid,
                      [seg("l%d" % i, a[0], a[1], z[0], z[1]) for i, (a, z) in enumerate(case["lines"])])
        return custom(case["name"], "integrateFootprint", [
            q("radiusProfiles", edges(prof)), q("fcpQuery", vertex_at(prof, *case["fcp"])),
            num("waistWidth", "%g mm" % case["waistWidth"]), num("waistLocation", "%g mm" % case["waistLocation"]),
            num("taperAngle", "%g deg" % case["taperAngle"]), num("numSamplesPerEdge", "51", integer=True),
            b("strict", case["strict"]), b("unifyCurves", False), b("extractWires", True)],
            {"angleDriver": case["driver"], "curvatureScalefactor": "TEN", "splineExportType": "APPROX",
             "footprintCurveBuildMode": "ONE_PER_REGION"})
    if kind == "scale":
        fx = case["fixture"]
        given = [q("refEdges", wire_edges(F[fx + ".wire"])), q("refRslEdge", edges(F[fx + ".rsl"])),
                 q("newRslEdge", edges(F[fx + "." + case["newrsl"]])), b("keepReference", True)]
        if "targetRadius" in case:
            given.append(num("targetRadius", "%g m" % case["targetRadius"]))
        return custom(case["name"], "scaleFootprint", given,
                      {"symmetryMode": "SYMMETRIC", "scaleMode": case["mode"], "pinLocation": "PIN_ACP"})
    raise ValueError(kind)


def main():
    from sync.core.client import OnshapeClient

    c = OnshapeClient()
    doc = json.load(open("footprint/.document.json"))
    D, Wid = doc["document_id"], doc["workspace_id"]
    elements = c.list_elements(D, Wid)
    studios = {e["name"]: e["id"] for e in elements if e["elementType"] == "PARTSTUDIO"}
    if STUDIO not in studios:
        studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{Wid}", {"name": STUDIO})["id"]
    STATE["client"] = c
    STATE["base"] = f"/api/v10/partstudios/d/{D}/w/{Wid}/e/{studios[STUDIO]}"
    specs = {}
    for ftype, tab in FEATURE_TABS.items():
        eid = [e for e in elements if e["name"] == tab and e["elementType"] == "FEATURESTUDIO"][0]["id"]
        found = c.get(f"/api/v10/featurestudios/d/{D}/w/{Wid}/e/{eid}/featurespecs")["featureSpecs"]
        specs[ftype] = [x for x in found if x["featureType"] == ftype][0]
    STATE["specs"] = specs

    F = build_fixtures()
    for case in CASES:
        build_case(case, F)
    print("studio", studios[STUDIO])


if __name__ == "__main__":
    main()

# AF7, GP2, IF1/IF4/IF5, IF7 fixed 2026-09-25 -- they now guard against regressions.
