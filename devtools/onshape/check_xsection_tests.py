"""Check the EI and Cross Section test cases in the xSection document's "xSection tests" Part Studio (built by
build_xsection_tests.py). One read-only eval call reads the "CrossSectionAnalysis" attribute on the origin
(xSectStorage.fs: details.crossSections[i] -> xCoord, EI_eff, GJ_eff, neutralAxisY, bodyData areas,
details.beamAnalysis); every station of every case is compared with its analytic value and printed PASS / FAIL.
Fixture features must be OK; EI features OK or INFO (INFO = outlines repaired, reported on the console).

Physics under test (2026-09-25): EI and the neutral axis on the beam basis, sum(E I) - sum(E S)^2 / sum(E A) with
each body's Young's modulus E (isotropic override: E; orthotropic override: E1); GJ = 4 * sum(G * Iz), Iz about
the G-weighted centroid in the thickness direction, G = Q66 (isotropic: E / 2.66, nu fixed at 0.33; orthotropic: G12).

Tolerances: sections bounded by straight edges are cut into LINE edges and integrated exactly (Green's theorem for
EI/area, exact triangle moments for GJ), so they get 0.1 % relative and 0.01 mm on the neutral axis. The circle is
sampled at its B-spline control-point count, so it gets 0.5 % on area (the review's target), 1 % on EI, 0.05 mm on NA.

usage (repo root): PYTHONPATH=. python devtools/onshape/check_xsection_tests.py
"""
import json
import math
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("xSection/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "xSection tests" and e["elementType"] == "PARTSTUDIO"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"

EXACT = 1e-3        # relative, straight-edged sections
NA_TOL = 0.01       # mm
CIRCLE_AREA = 5e-3
CIRCLE_EI = 1e-2
CIRCLE_NA = 0.05    # mm
X_TOL = 0.01        # mm, station positions

SCRIPT = r'''
function(context is Context, queries)
{
    var out = [];
    const data = getAttribute(context, { "entity" : qOrigin(EntityType.BODY), "name" : "CrossSectionAnalysis" });
    if (data == undefined)
    {
        return ["none"];
    }
    const nm2 = newton * meter * meter;
    for (var entry in data)
    {
        const beam = entry.value.details.beamAnalysis == undefined ? "no" : "yes";
        for (var s in entry.value.details.crossSections)
        {
            var areas = "";
            for (var bd in s.bodyData)
            {
                areas = areas ~ bd.bodyIdx ~ ":" ~ toString(bd.totalSectionProperties.area / (millimeter * millimeter)) ~ ";";
            }
            out = append(out, "ROW|" ~ entry.key ~ "|" ~ s.stationNumber ~ "|" ~ toString(s.xCoord / millimeter)
                ~ "|" ~ (s.EI_eff == undefined ? "nan" : toString(s.EI_eff / nm2))
                ~ "|" ~ (s.GJ_eff == undefined ? "nan" : toString(s.GJ_eff / nm2))
                ~ "|" ~ (s.neutralAxisY == undefined ? "nan" : toString(s.neutralAxisY / millimeter))
                ~ "|" ~ areas ~ "|" ~ beam);
        }
    }
    return out;
}
'''


# ---- analytic sections (MPa, mm; results in N m^2 and mm) ----
def g_iso(e):
    """Q66 of the isotropic override: E / (2 (1 + 0.33))."""
    return e / 2.66


def layers(parts):
    """Rectangles (E, G, width, z0, z1), all measured from the path (z = 0); a negative width subtracts a hole.
    Returns neutral axis (mm), EI (N m^2, beam basis), GJ (N m^2, 4 sum G Iz about the G-weighted centroid), area."""
    ea = sum(e * w * (z1 - z0) for e, g, w, z0, z1 in parts)
    es = sum(e * w * (z1 - z0) * (z0 + z1) / 2 for e, g, w, z0, z1 in parts)
    ei0 = sum(e * w * (z1 ** 3 - z0 ** 3) / 3 for e, g, w, z0, z1 in parts)
    ga = sum(g * w * (z1 - z0) for e, g, w, z0, z1 in parts)
    zg = sum(g * w * (z1 - z0) * (z0 + z1) / 2 for e, g, w, z0, z1 in parts) / ga
    gj = 4 * sum(g * (w * (z1 - z0) ** 3 / 12 + w * (z1 - z0) * ((z0 + z1) / 2 - zg) ** 2) for e, g, w, z0, z1 in parts)
    return {"NA": es / ea, "EI": (ei0 - es * es / ea) * 1e-6, "GJ": gj * 1e-6, "area": sum(w * (z1 - z0) for e, g, w, z0, z1 in parts)}


ISO = (10000, g_iso(10000))
RECT = layers([ISO + (100, 0, 10)])                                                   # 83.33, NA 5, GJ 125.31
ORTHO = layers([(20000, 1500, 100, 0, 10)])                                          # 166.67, GJ 50.00
ORTHO_Q11 = 20000 / (1 - 0.45 * 0.45 * 10000 / 20000) * 100 * 10 ** 3 / 12 * 1e-6    # 185.44: the plate basis
PLATE = layers([(20000, 12000, 100, 0, 2)])                                          # GJ 3.200, EI 1.333
q11 = 20000 / (1 - 0.45 ** 2)
PLATE_OLD_GJ = 4 * ((q11 - 0.45 * q11) / 2) * 100 * 2 ** 3 / 12 * 1e-6               # 1.839: (Q11 - Q12)/2
STEP = layers([ISO + (50, 0, 10), ISO + (50, 0, 4)])                                 # 57.19, NA 4.143, GJ 86.00
STEP_STRIPS = g_iso(10000) * (50 * 10 ** 3 + 50 * 4 ** 3) / 3 * 1e-6                  # 66.67: strip-wise thin plate
HOLLOW = layers([ISO + (100, 0, 20), ISO + (-80, 5, 15)])                            # 600.0, NA 10, area 1200
HOLLOW_GJ_OUTER = layers([ISO + (100, 0, 20)])["GJ"]                                 # 1002.5: the hole's triangles are not subtracted
CIRCLE = {"NA": 5.0, "EI": 10000 * math.pi * 5 ** 4 / 4 * 1e-6, "area": math.pi * 25}  # 4.909, 78.54
BIMETAL = layers([(70000, g_iso(70000), 100, 0, 4), ISO + (100, 4, 10)])             # NA 2.882, EI 178.86, GJ 268.97


def rel(got, want):
    return abs(got - want) / abs(want) if want else abs(got)


def fmt(v):
    return "%.4g" % v


def near(label, got, want, tol, relative=True):
    """(ok, text) for one quantity."""
    err = rel(got, want) if relative else abs(got - want)
    ok = err <= tol and not math.isnan(got)
    return ok, "%s %s (%s)" % (label, fmt(got), fmt(want)) + ("" if ok else " <-- off by %s" % ("%.3g %%" % (100 * err) if relative else "%.3g mm" % err))


def stations_match(rows, want, ei_tol=EXACT, na_tol=NA_TOL, gj=True, area=None, area_tol=EXACT, where=None):
    """Every station (or those where `where(row)`) against `want` = {NA, EI, GJ[, area]}."""
    picked = [r for r in rows if where is None or where(r)]
    if not picked:
        return False, "no stations"
    ok, texts = True, []
    for r in picked:
        checks = [near("EI", r["EI"], want["EI"], ei_tol), near("NA", r["NA"], want["NA"], na_tol, relative=False)]
        if gj:
            checks.append(near("GJ", r["GJ"], want["GJ"], EXACT * 5))
        if area is not None:
            checks.append(near("area", r["area"], area, area_tol))
        good = all(x[0] for x in checks)
        ok = ok and good
        if not good or r is picked[0]:
            texts.append("x %s: %s" % (fmt(r["x"]), ", ".join(x[1] for x in checks)))
    return ok, ("%d stations; " % len(picked)) + "; ".join(texts)


def xs_are(rows, want):
    got = sorted(r["x"] for r in rows)
    ok = len(got) == len(want) and all(abs(a - b) <= X_TOL for a, b in zip(got, sorted(want)))
    return ok, "x " + ", ".join(fmt(x) for x in got) + ("" if ok else " (expected %s)" % ", ".join(fmt(x) for x in want))


def both(*results):
    return all(r[0] for r in results), " | ".join(r[1] for r in results)


UNIFORM = [-100, -50, 0, 50, 100]

CASES = {
    "X1 ": lambda rows: both(xs_are(rows, UNIFORM), stations_match(rows, RECT, area=RECT["area"])),
    "X2 ": lambda rows: both(stations_match(rows, ORTHO),
                             (True, "Q11-basis EI would be %s" % fmt(ORTHO_Q11))),
    "X3 ": lambda rows: both(stations_match(rows, PLATE), (True, "old (Q11-Q12)/2 GJ would be %s" % fmt(PLATE_OLD_GJ))),
    "X4 ": lambda rows: both(stations_match(rows, STEP, area=STEP["area"]),
                             (True, "strip-wise GJ %s, stored/strip %s" % (fmt(STEP_STRIPS), fmt(rows[0]["GJ"] / STEP_STRIPS) if rows else "-"))),
    "X5 ": lambda rows: both(stations_match(rows, HOLLOW, gj=False, area=HOLLOW["area"]),
                             (True, "GJ (info) %s: hole-aware %s, outer only %s" % (fmt(rows[0]["GJ"]) if rows else "-", fmt(HOLLOW["GJ"]), fmt(HOLLOW_GJ_OUTER)))),
    "X6 ": lambda rows: stations_match(rows, CIRCLE, ei_tol=CIRCLE_EI, na_tol=CIRCLE_NA, gj=False, area=CIRCLE["area"], area_tol=CIRCLE_AREA),
    "X7 ": lambda rows: both(stations_match(rows, BIMETAL, area=400 + 600), bodies_per_station(rows, 2)),
    "X8 ": lambda rows: both(stations_match(rows, RECT, area=RECT["area"]), bodies_per_station(rows, 2)),
    "X9 ": lambda rows: both(xs_are(rows, UNIFORM), stations_match(rows, RECT)),
    # End stations are inset by max(0.5 mm, 2% of the 50 mm spacing) = 1 mm from the path ends (2026-09-25).
    "X10 ": lambda rows: both(xs_are(rows, [-149, -100, -50, 0, 50, 100, 149]), ascending(rows), stations_match(rows, RECT),
                              (all(r["beam"] == "yes" for r in rows), "beam analysis " + (rows[0]["beam"] if rows else "-"))),
    "X11 ": lambda rows: both(stations_match(rows, RECT, where=lambda r: abs(r["x"]) < 99),
                              (True, "end stations (info): " + ", ".join("x %s EI %s" % (fmt(r["x"]), fmt(r["EI"])) for r in rows if abs(r["x"]) >= 99))),
    "X12 ": lambda rows: stations_match(rows, RECT),
}
# X11: the case IS the status -- grazing end stations must not raise the "no area" warning.
ALLOWED_STATUS = {"eiXSect": ("OK", "INFO")}


def bodies_per_station(rows, n):
    counts = [len(r["areas"]) for r in rows]
    ok = all(k == n for k in counts)
    return ok, "bodies per station %s" % counts + ("" if ok else " (expected %d)" % n)


def ascending(rows):
    xs = [r["x"] for r in rows]
    ok = all(a < b for a, b in zip(xs, xs[1:]))
    return ok, "stored ascending" if ok else "stored order %s" % xs


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


def read_rows():
    r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
    errors = [x.get("message") for x in (r.get("notices") or []) if x.get("level") == "ERROR"]
    if errors:
        raise SystemExit("eval failed: " + "; ".join(str(e) for e in errors))
    rows = {}
    for line in strings(r.get("result")):
        if not line.startswith("ROW|"):
            continue
        _, key, station, x, ei, gj, na, areas, beam = line.split("|")
        area_map = {}
        for part in areas.split(";"):
            if part:
                idx, a = part.split(":")
                area_map[idx] = float(a)
        rows.setdefault(key, []).append({"station": int(float(station)), "x": float(x), "EI": float(ei), "GJ": float(gj),
                                         "NA": float(na), "areas": area_map, "area": sum(area_map.values()), "beam": beam})
    return rows


def main():
    feats = c.get(f"{BASE}/features")
    states = feats["featureStates"]
    failed = 0

    for f in feats["features"]:
        status = states.get(f["featureId"], {}).get("featureStatus")
        allowed = ALLOWED_STATUS.get(f.get("featureType"), ("OK",))
        if status not in allowed:
            failed += 1
            print("FAIL", f["name"], "-- status", status, "expected", " or ".join(allowed))

    rows = read_rows()
    xs_features = [f for f in feats["features"] if f.get("featureType") == "eiXSect"]
    for prefix, check in CASES.items():
        found = [f for f in xs_features if f["name"].startswith(prefix)]
        if len(found) != 1:
            print("FAIL", prefix, "-- %d EI features with this prefix" % len(found))
            failed += 1
            continue
        f = found[0]
        case_rows = rows.get(f["featureId"], [])
        if not case_rows:
            print("FAIL", f["name"], "-- no stored cross sections (attribute key %s)" % f["featureId"])
            failed += 1
            continue
        ok, text = check(case_rows)
        failed += not ok
        print("PASS" if ok else "FAIL", f["name"], "--", text)
    print("%d failed" % failed if failed else "all passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
