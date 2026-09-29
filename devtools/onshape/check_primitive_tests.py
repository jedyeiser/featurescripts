"""Checks the Export primitive tests in "Primitive tests" (Publish & Drawing tools, built by build_primitive_tests.py).

Reads every "<prefix> PRIMITIVE" composite's attribute (schema primitive/1) through the eval API, plus the feature
statuses, and compares:
  * RD 20TAC known values (avg radius ~17.05 m, natural radius widest ~17.48 m / inflection ~16.18 m,
    RSL 1480, FRCPl 130, ARCPl 50) -- P1 / P2 / P3;
  * P4 (mirrored, tip -X) against P3: every value equal, x negated;
  * P5 (datum at x 500, z 10) against P1: every value equal, x - 500, z - 10;
  * P7 (datum = MRS connector, Datum uses ORIGIN) against P1: x - 885; P9 (COORDINATE_SYSTEM) against P5;
  * P8 (target EI 150 N*m^2 constant): deflection = P L^3 / 48 EI, stiffness = 48 EI (1 in) / L^3, block rows;
  * only rows with data (no "n/a" / phase-2 rows); P1's flat baseline has no rocker / camber rows or points;
  * structure: composite members, band bodies, key points.
With --before <json> (a --json snapshot of an earlier run): P1 .. P6 values unchanged, except the rows the
2026-09-28 decisions removed (phase-2 placeholders; P1 / P5 / P6 rocker rows on a flat baseline).

usage (repo root): PYTHONPATH=. python devtools/onshape/check_primitive_tests.py [--dump | --json <out> | --before <json>]
                   [--unsuppress] [--keep P1,P8]
  --unsuppress   unsuppress every P case first (the checks need all of them regenerated)
  --keep P1,P8   afterwards suppress every P case except these, so the studio regenerates fast (2026-09-28: many
                 Export primitive instances make a studio slow)
"""
import json
import os
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
STUDIO = os.environ.get("PRIMITIVE_STUDIO", "Primitive tests")
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == STUDIO][0]

SCRIPT = r'''
function(context is Context, queries)
{
    var out = [];
    for (var body in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "publishPrimitive")))
    {
        const data = getAttribute(context, { "entity" : body, "name" : "publishPrimitive" });
        var counts = {};
        var colours = {};
        var extents = {};
        for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
        {
            const name = getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME });
            counts[name] = (counts[name] == undefined ? 0 : counts[name]) + 1;
            const a = getProperty(context, { "entity" : m, "propertyType" : PropertyType.APPEARANCE });
            colours[name] = round(a.red * 100) / 100 ~ "/" ~ round(a.green * 100) / 100 ~ "/" ~ round(a.blue * 100) / 100;
            // x / z ranges (mm) of the plot band's plot, reference line, junction and key-location ticks, per body (world frame).
            if (match(name, ".* (RADIUS|CURVATURE)( REFERENCE| JUNCTION| TICK .*)?").hasMatch || match(name, ".* FOOTPRINT JUNCTION").hasMatch)
            {
                const bb = evBox3d(context, { "topology" : m, "tight" : true });
                const row = [round(bb.minCorner[0] / millimeter * 1000) / 1000, round(bb.maxCorner[0] / millimeter * 1000) / 1000,
                             round(bb.minCorner[2] / millimeter * 1000) / 1000, round(bb.maxCorner[2] / millimeter * 1000) / 1000];
                extents[name] = append(extents[name] == undefined ? [] : extents[name], row);
            }
        }
        var members = [];
        for (var entry in counts)
        {
            members = append(members, entry.key);
        }
        out = append(out, "JSON " ~ toString({ "data" : data, "members" : members, "counts" : counts, "colours" : colours, "extents" : extents,
                    "name" : getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME }),
                    "bom" : getProperty(context, { "entity" : body, "propertyType" : PropertyType.EXCLUDE_FROM_BOM }) }));
    }
    return out;
}
'''


def parse_fs(text):
    """Parses FeatureScript's toString of maps / arrays ("{ key : value , ... }", "[ a , b ]") into Python.
    Relies on the primitive's strings never containing " , ", " : " or a bracket next to a space."""
    parts = re.split(r"(\{ | \}|\[ | \]| , | : )", text)
    # parts alternates text, separator, text, ...; texts may be "" (an empty string value).
    pos = [0]

    def text():
        t = parts[pos[0]]
        pos[0] += 1
        return t

    def sep():
        s = parts[pos[0]]
        pos[0] += 1
        return s

    def closed():
        # after a closing bracket comes an empty text before the next separator
        if pos[0] < len(parts):
            assert text() == ""

    def value():
        t = text()
        nxt = parts[pos[0]] if pos[0] < len(parts) else None
        if t == "" and nxt == "{ ":
            sep()
            out = {}
            while True:
                key = text()
                assert sep() == " : ", (key, pos[0])
                out[key] = value()
                s = sep()
                if s == " }":
                    closed()
                    return out
                assert s == " , ", s
        if t == "" and nxt == "[ ":
            sep()
            out = []
            while True:
                out.append(value())
                s = sep()
                if s == " ]":
                    closed()
                    return out
                assert s == " , ", s
        return scalar(t)

    def scalar(tok):
        if tok == "[]":
            return []
        if tok == "{}":
            return {}
        try:
            return float(tok)
        except ValueError:
            return {"true": True, "false": False}.get(tok, tok)

    return value()


def read():
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": SCRIPT})
    blob = json.dumps(r.get("result"))
    strings = [bytes(s, "ascii").decode("unicode_escape") for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', blob)]
    return [s[5:] for s in strings if s.startswith("JSON ")], r


def statuses():
    f = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")
    names = {x["featureId"]: x["name"] for x in f["features"]}
    return {names[k]: v["featureStatus"] for k, v in f["featureStates"].items() if k in names}


def set_suppressed(keep=None):
    """Suppresses every P case not in `keep` (None: unsuppress all), in one feature update."""
    f = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")
    changed = []
    for x in f["features"]:
        case = x["name"].split()[0]
        if not re.match(r"P\d+$", case):
            continue
        want = keep is not None and case not in keep
        if bool(x.get("suppressed")) != want:
            x["suppressed"] = want
            changed.append(x)
    if not changed:
        return
    c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features/updates", {
        "btType": "BTUpdateFeaturesCall-1748", "features": changed, "updateSuppressionAttributes": True,
        "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]})
    print("%s %s" % ("suppressed" if keep is not None else "unsuppressed", ", ".join(x["name"].split()[0] for x in changed)))


def primitives():
    items, raw = read()
    out = {}
    for s in items:
        v = parse_fs(s)
        out[v["data"]["prefix"]] = v
    return out


RESULTS = []


def check(case, what, expected, actual, ok):
    RESULTS.append((case, what, expected, actual, bool(ok)))


def near(a, b, tol):
    return isinstance(a, float) and isinstance(b, float) and abs(a - b) <= tol


def rows(d, section):
    return {r["key"]: r for r in d[section]}


def meta(d, key):
    return rows(d, "metadata")[key]["value"]


def compare(case, a, b, tol, dx=0.0, dz=0.0, sign=1.0):
    """Every number of primitive b equals primitive a's (x -> sign * x + dx, z -> z + dz, s -> sign * s + ds); texts
    equal. s runs from the datum along the bottom wire with x (2026-09-28), so a datum shift moves every s by the same
    arc length ds, taken from MRS."""
    bad = []
    ds = rows(b, "keyLocations")["MRS"]["s"] - sign * rows(a, "keyLocations")["MRS"]["s"]
    for section in ("scaleFactors", "metadata", "keyLocations", "baseline"):
        ra, rb = rows(a, section), rows(b, section)
        for key, row in ra.items():
            for field, va in row.items():
                if field == "station" and sign < 0:
                    continue  # mirrored: the x order (and so the numbering) reverses
                vb = rb.get(key, {}).get(field)
                if field == "x" and isinstance(va, float):
                    va = sign * va + dx
                if field == "s" and isinstance(va, float):
                    va = sign * va + ds
                if field == "z" and section == "keyLocations" and isinstance(va, float):
                    va = va + dz
                if isinstance(va, float):
                    if not near(va, vb, tol):
                        bad.append("%s.%s.%s %s vs %s" % (section, key, field, va, vb))
                elif field != "note" and va != vb:
                    bad.append("%s.%s.%s %r vs %r" % (section, key, field, va, vb))
    if len(a["data"]) != len(b["data"]):
        bad.append("data rows %d vs %d" % (len(a["data"]), len(b["data"])))
    # Data rows run by ascending x: mirrored, they pair up in reverse.
    data_b = b["data"] if sign > 0 else list(reversed(b["data"]))
    for i, (ra, rb) in enumerate(zip(a["data"], data_b)):
        for field, va in ra.items():
            if field == "station" and sign < 0:
                continue
            vb = rb.get(field)
            if field == "x" and isinstance(va, float):
                va = sign * va + dx
            if field == "s" and isinstance(va, float):
                va = sign * va + ds
            if field == "z" and isinstance(va, float):
                va = va + dz
            if isinstance(va, float) and not near(va, vb, tol):
                bad.append("data[%d].%s %s vs %s" % (i, field, va, vb))
            elif not isinstance(va, float) and va != vb:
                bad.append("data[%d].%s %r vs %r" % (i, field, va, vb))
    check(case, "every table value (tol %g)" % tol, "equal",
          "%d differences" % len(bad) + ("" if not bad else ": " + "; ".join(bad[:4])), not bad)


REQUIRED_MEMBERS = ["PRIMITIVE BASELINE", "PRIMITIVE BASELINE TIP", "PRIMITIVE BASELINE TAIL", "PRIMITIVE BASELINE FCP", "PRIMITIVE PROFILE BOTTOM", "PRIMITIVE PROFILE TOP", "PRIMITIVE FOOTPRINT",
                    "PRIMITIVE RADIUS", "PRIMITIVE RADIUS REFERENCE", "PRIMITIVE RADIUS TICK FCP", "PRIMITIVE RADIUS TICK MRS",
                    "PRIMITIVE PROFILE FCP", "PRIMITIVE PROFILE TIP", "PRIMITIVE FOOTPRINT FB_WIDEST", "PRIMITIVE BASELINE DATUM"]


REMOVED = {"deflection", "stiffness", "tipBlock", "tailBlock", "tipHeight", "tailHeight"}
UNAVAILABLE = ("n/a", "not computed (phase 2)")
# 3-point bending, rollers at FCP / ACP, 30 kg at MRS, L = RSL (xSection xSectBeamAnalysis constants)
P_LOAD = 30 * 9.80665
EI_TEST = 150.0
L_RSL = 1.48


def strip(d, flat):
    """Rows of a primitive without the ones the 2026-09-28 decisions removed."""
    out = dict(d)
    out["metadata"] = [r for r in d["metadata"] if r["key"] not in REMOVED]
    out["baseline"] = [] if flat else [r for r in d["baseline"] if r["key"] not in REMOVED]
    return out


def run_checks(prims, before=None):
    st = statuses()
    for name, status in sorted(st.items()):
        if re.match(r"P\d+$", name.split()[0]):
            want = "ERROR" if "-> ERROR" in name else "INFO"
            check(name.split()[0], "feature status", want, status, status == want)
    d = {k.split()[0]: v["data"] for k, v in prims.items()}
    full = {k.split()[0]: v for k, v in prims.items()}

    p1 = d["P1"]
    check("P1", "average radius (inflection) m", "~17.05", meta(p1, "averageRadius"), near(meta(p1, "averageRadius"), 17.05, 0.01))
    check("P1", "natural radius widest m", "~17.48", meta(p1, "naturalRadiusWidest"), near(meta(p1, "naturalRadiusWidest"), 17.48, 0.01))
    check("P1", "natural radius inflection m", "~16.18", meta(p1, "naturalRadiusInflection"), near(meta(p1, "naturalRadiusInflection"), 16.18, 0.01))
    check("P1", "RSL mm", 1480, meta(p1, "rsl"), near(meta(p1, "rsl"), 1480.0, 0.01))
    k1 = rows(p1, "keyLocations")
    s_mrs = k1["MRS"]["s"]
    for key, x, s in (("FCP", 1625, 740), ("ACP", 145, -740), ("MRS", 885, 0), ("XS1", 1255, 370), ("XS2", 515, -370)):
        check("P1", "%s x / s - s(MRS) mm" % key, "%s / %s" % (x, s), "%s / %s" % (k1[key]["x"], k1[key]["s"] - s_mrs),
              near(k1[key]["x"], float(x), 1e-3) and near(k1[key]["s"] - s_mrs, float(s), 1e-3))
    check("P1", "TIP / TAIL s - s(MRS) mm", "905 / -885", "%s / %s" % (k1["TIP"]["s"] - s_mrs, k1["TAIL"]["s"] - s_mrs),
          near(k1["TIP"]["s"] - s_mrs, 905.0, 0.01) and near(k1["TAIL"]["s"] - s_mrs, -885.0, 0.01))
    # 2026-09-28 s rule: zero at x = 0 (the datum; P1 world origin, just past the TAIL end at x 0.63 -> straight
    # extension), increasing with x. On TAC's flat running length s - x is constant.
    m7 = rows(d["P7"], "keyLocations")["MRS"]
    check("P7", "s zero at the datum (datum = MRS): x / s of MRS", "0 / 0", "%s / %s" % (m7["x"], m7["s"]), near(m7["x"], 0.0, 1e-3) and near(m7["s"], 0.0, 1e-3))
    check("P1", "s(TAIL) small, >= 0 (x = 0 just past the tail end)", ">= 0, < 5", k1["TAIL"]["s"],
          0.0 <= k1["TAIL"]["s"] < 5.0)
    ks = sorted(p1["keyLocations"], key=lambda r: r["x"])
    check("P1", "s increases with x (key locations)", "ascending", [r["s"] for r in ks],
          all(b["s"] > a["s"] for a, b in zip(ks, ks[1:])))
    k4 = rows(d["P4"], "keyLocations")
    check("P4", "mirror: s(key) = -s(P3 key), tip at -X", "negated", "FCP %s vs %s" % (k4["FCP"]["s"], rows(d["P3"], "keyLocations")["FCP"]["s"]),
          all(near(k4[k]["s"], -rows(d["P3"], "keyLocations")[k]["s"], 2e-3) for k in ("FCP", "ACP", "MRS", "TIP", "TAIL")))
    sf = rows(p1, "scaleFactors")
    check("P1", "running surface top/bottom %", "slightly > 100", sf["runningSurface"]["ratio"], 100.0 < sf["runningSurface"]["ratio"] < 100.1)
    check("P1", "tip top/bottom %", "< 100", sf["tip"]["ratio"], sf["tip"]["ratio"] < 100.0)
    check("P1", "data rows (N 21 incl. XS1/MRS/XS2)", 21, len(p1["data"]), len(p1["data"]) == 21)
    at_fcp = [r for r in p1["data"] if near(r["x"], 1625.0, 1e-3)]
    check("P1", "ski_thck at FCP mm (X-Sect SPA)", 6.75, at_fcp and at_fcp[0]["skiThck"], at_fcp and near(at_fcp[0]["skiThck"], 6.75, 0.01))
    # 2026-09-28 station numbers: Key locations and Data sorted by x ascending, # from 0 at the lowest x
    for sec in ("keyLocations", "data"):
        xs = [r["x"] for r in p1[sec]]
        st = [r.get("station") for r in p1[sec]]
        check("P1", "%s by x ascending, station 0.." % sec, "sorted, 0..%d" % (len(xs) - 1), "%s .. %s, %s" % (xs[0], xs[-1], st[:3]),
              xs == sorted(xs) and st == [float(i) for i in range(len(xs))])
    kx = [(r["key"], r["station"]) for r in rows(p1, "keyLocations").values() if r["key"] in ("ACP", "FCP")]
    check("P1", "ACP (x 145) = station 1 after TAIL, FCP before TIP", "ACP 1, FCP n-2", kx,
          dict(kx).get("ACP") == 1.0 and dict(kx).get("FCP") == float(len(p1["keyLocations"]) - 2))
    check("P1", "settings.stationNumbers", True, p1["settings"].get("stationNumbers"), p1["settings"].get("stationNumbers") is True)
    shown = [(sec, r["key"]) for case in ("P1", "P2", "P3", "P4", "P5", "P6", "P7", "P9") for sec in ("metadata", "baseline")
             for r in d[case][sec] if r.get("value") in UNAVAILABLE or (r["key"] in REMOVED)]
    check("P1-P9", "no unavailable / phase-2 rows (no EI, no blocks)", "none", shown, not shown)
    check("P1", "flat baseline: flag, no Table 5 rows", "flat, 0 rows",
          "%s, %d rows" % (p1["settings"]["baselineFlat"], len(p1["baseline"])), p1["settings"]["baselineFlat"] is True and not p1["baseline"])
    gone = [m for m in full["P1"]["members"] if re.search(r"BASELINE (FB_MIN|AB_MIN|FRCP|ARCP|MCL)$", m)]
    check("P1", "flat baseline: no min / FRCP / ARCP / MCL points", "none", gone, not gone)
    members = full["P1"]["members"]
    missing = [m for m in REQUIRED_MEMBERS if "P1 TAC " + m not in members]
    check("P1", "composite members / BOM", "named bands + points, excluded",
          "%d members, missing %s, bom %s" % (len(members), missing, full["P1"]["bom"]), not missing and full["P1"]["bom"] is True)

    p2 = d["P2"]
    b2 = rows(p2, "baseline")
    for case in ("P2", "P3", "P4"):
        flat = d[case]["settings"]["baselineFlat"]
        pts = [m for m in full[case]["members"] if re.search(r"BASELINE (FB_MIN|AB_MIN|FRCP|ARCP|MCL|TIP|TAIL)$", m)]
        check(case, "cambered baseline: not flat, 10 rows, 7 points", "False, 10, 7",
              "%s, %d, %d" % (flat, len(d[case]["baseline"]), len(pts)), flat is False and len(d[case]["baseline"]) == 10 and len(pts) == 7)
    check("P2", "FRCPl mm", 130, b2["FRCPl"]["value"], near(b2["FRCPl"]["value"], 130.0, 0.1))
    check("P2", "ARCPl mm", 50, b2["ARCPl"]["value"], near(b2["ARCPl"]["value"], 50.0, 0.1))
    check("P2", "FRCP / ARCP x mm", "1495 / 195", "%s / %s" % (b2["FRCP"]["x"], b2["ARCP"]["x"]),
          near(b2["FRCP"]["x"], 1495.0, 0.1) and near(b2["ARCP"]["x"], 195.0, 0.1))
    for key in ("averageRadius", "naturalRadiusWidest", "naturalRadiusInflection"):
        check("P2", key + " vs P1 m", meta(p1, key), meta(p2, key), near(meta(p2, key), meta(p1, key), 0.001))

    p3 = d["P3"]
    b3 = rows(p3, "baseline")
    same = all(near(b3[k][f], b2[k][f], 1e-3) or b3[k][f] == b2[k][f] for k in b2 for f in ("value", "x", "s"))
    check("P3", "baseline rows vs P2", "equal", "equal" if same else "differ", same)
    for key in ("averageRadius", "naturalRadiusWidest", "naturalRadiusInflection", "taperAngleWidest"):
        check("P3", key + " vs P1", meta(p1, key), meta(p3, key), near(meta(p3, key), meta(p1, key), 1e-4))

    compare("P4 vs P3", p3, d["P4"], 2e-3, sign=-1.0)
    check("P4", "tip direction", "-X", d["P4"]["settings"]["tipTowards"], d["P4"]["settings"]["tipTowards"] == "-X")
    compare("P5 vs P1", p1, d["P5"], 2e-3, dx=-500.0, dz=-10.0)
    compare("P7 vs P1", p1, d["P7"], 2e-3, dx=-885.0)
    check("P7", "datum uses", "ORIGIN", d["P7"]["settings"]["datumUses"], d["P7"]["settings"]["datumUses"] == "ORIGIN")
    compare("P9 vs P5", d["P5"], d["P9"], 2e-3)
    check("P9", "datum uses", "COORDINATE_SYSTEM", d["P9"]["settings"]["datumUses"], d["P9"]["settings"]["datumUses"] == "COORDINATE_SYSTEM")

    p8 = d["P8"]
    m8 = rows(p8, "metadata")
    delta = P_LOAD * L_RSL ** 3 / (48 * EI_TEST) * 1000.0
    lbin = 48 * EI_TEST * 0.0254 / L_RSL ** 3 / 4.44822
    got = m8.get("deflection", {}).get("value")
    check("P8", "deflection mm/30kg = P L^3 / 48 EI", "%.3f" % delta, got, isinstance(got, float) and abs(got - delta) <= 1e-3 * delta)
    got = m8.get("stiffness", {}).get("value")
    check("P8", "stiffness lb/in = 48 EI d / L^3", "%.3f" % lbin, got, isinstance(got, float) and abs(got - lbin) <= 1e-3 * lbin)
    b8 = rows(p8, "baseline")
    check("P8", "Tip / Tail block rows", "TIP_BLOCK_T1 / TAIL_BLOCK_T1",
          "%s / %s" % (b8.get("tipBlock", {}).get("value"), b8.get("tailBlock", {}).get("value")),
          b8.get("tipBlock", {}).get("value") == "TIP_BLOCK_T1" and b8.get("tailBlock", {}).get("value") == "TAIL_BLOCK_T1")
    check("P8", "other rows = P1", "equal", "",
          [r for r in p8["metadata"] if r["key"] not in REMOVED] == p1["metadata"] and
          [r for r in p8["baseline"] if r["key"] not in REMOVED] == p1["baseline"])

    if before:
        old = {k.split()[0]: v["data"] for k, v in before.items()}
        for case in ("P1", "P2", "P3", "P4", "P5", "P6"):
            flat = d[case]["settings"]["baselineFlat"]
            compare("%s vs before" % case, strip(old[case], flat), strip(d[case], False), 1e-6)

    # 2026-09-28 radius frame, dashed grid, colours, labels (P1 labels on by default, P10 grid on, labels off)
    m1, m10 = full["P1"]["members"], full["P10"]["members"]
    frame = ["RADIUS AXIS TIP", "RADIUS AXIS TAIL", "RADIUS TICK 0 TIP", "RADIUS TICK 0 TAIL", "RADIUS TICK +10 TIP",
             "RADIUS TICK +10 TAIL", "RADIUS TICK -10 TIP"]
    missing = [n for n in frame if "P1 TAC PRIMITIVE " + n not in m1]
    check("P1", "radius frame: end axes + 10 m ticks", "present", missing or "all", not missing)
    levels = sorted(int(m.split()[-2]) for m in m1 if re.search(r"RADIUS TICK [-+]?\d+ TIP$", m))
    plot = [abs(r["radius"]) for r in p1["data"] if isinstance(r["radius"], float)]
    check("P1", "tick levels every 10 m, 0 incl., within the limit", "step 10, |max| <= 50",
          levels, levels and all(b - a == 10 for a, b in zip(levels, levels[1:])) and 0 in levels and max(abs(v) for v in levels) <= 50)
    grid1 = [m for m in m1 if " RADIUS GRID " in m]
    check("P1", "dashed grid off by default", "none", grid1, not grid1)
    grid10 = {m: full["P10"]["counts"][m] for m in m10 if " RADIUS GRID " in m}
    lv10 = sorted(int(m.split()[-1]) for m in grid10)
    check("P10", "dashed grid: a line of dashes at every level but 0", "levels = P1 ticks - 0, > 100 dashes each",
          "%s %s" % (lv10, sorted(set(grid10.values()))), lv10 == [v for v in levels if v != 0] and all(n > 100 for n in grid10.values()))
    text10 = [m for m in m10 if m.endswith(" TITLE") or " RADIUS LABEL " in m]
    check("P10", "labels off: no text", "none", text10, not text10)
    compare("P10 vs P1", p1, d["P10"], 1e-6)
    titles = ["BASELINE TITLE", "PROFILE TITLE", "FOOTPRINT TITLE", "RADIUS TITLE", "RADIUS LABEL 0", "RADIUS LABEL +10", "RADIUS LABEL -10"]
    missing = [n for n in titles if "P1 TAC PRIMITIVE " + n not in m1]
    check("P1", "labels (default on): band titles + scale numbers", "present", missing or "all", not missing)
    col = full["P1"]["colours"]
    want = {"BASELINE": "0.09/0.32/0.69", "BASELINE FCP": "0.09/0.32/0.69", "PROFILE BOTTOM": "0.0/0.5/0.25", "PROFILE TOP": "0.0/0.5/0.25",
            "FOOTPRINT": "0.85/0.4/0.0", "RADIUS": "0.75/0.1/0.1", "RADIUS REFERENCE": "0.5/0.5/0.5", "RADIUS TICK FCP": "0.5/0.5/0.5",
            "RADIUS AXIS TIP": "0.5/0.5/0.5", "RADIUS LABEL +10": "0.5/0.5/0.5", "FOOTPRINT TITLE": "0.85/0.4/0.0"}
    norm = lambda v: "/".join("%g" % float(x) for x in str(v).split("/"))
    bad = ["%s %s" % (k, col.get("P1 TAC PRIMITIVE " + k)) for k, v in want.items() if norm(col.get("P1 TAC PRIMITIVE " + k, "0/0/0")) != norm(v)]
    check("P1", "appearance per band (read back from the composite members)", "band colours", bad or "all", not bad)
    grey = norm(full["P10"]["colours"].get("P10 TAC grid PRIMITIVE RADIUS GRID +10", "0/0/0"))
    check("P10", "grid dashes grey (pattern copies keep it)", "0.5/0.5/0.5", grey, grey == "0.5/0.5/0.5")

    # 2026-09-28 EI band (P8 only: target EI 150 N*m^2, scale 2 N*m^2 per mm -> 75 mm high, ticks 0..150 by 50)
    m8 = full["P8"]["members"]
    ei_names = ["EI", "EI REFERENCE", "EI AXIS TIP", "EI AXIS TAIL", "EI TICK 0 TIP", "EI TICK +50 TIP", "EI TICK +150 TAIL",
                "EI LABEL +150", "EI TITLE", "EI DATUM"]
    missing = [n for n in ei_names if "P8 TAC EI PRIMITIVE " + n not in m8]
    top = [m for m in m8 if re.search(r" EI TICK \+200 ", m)]
    check("P8", "EI band: plot, reference, axes, ticks 0..150 by 50, label, title, datum", "present, no +200",
          missing or ("all" if not top else top), not missing and not top)
    eic = norm(full["P8"]["colours"].get("P8 TAC EI PRIMITIVE EI", "0/0/0"))
    check("P8", "EI plot colour", "0.45/0.2/0.6", eic, eic == "0.45/0.2/0.6")
    b8 = d["P8"]["bands"]
    check("P8", "EI band above the baseline band", "ei > baseline", "%s / %s" % (b8.get("ei"), b8.get("baseline")),
          isinstance(b8.get("ei"), float) and b8["ei"] > b8["baseline"])
    no_ei = [m for m in m1 if " PRIMITIVE EI" in m]
    check("P1", "no EI band without a target EI", "none", no_ei, not no_ei and "ei" not in p1["bands"])

    p6 = d["P6"]
    check("P6", "average radius always between the inflections: = P1 (region RSL)", meta(p1, "averageRadius"), meta(p6, "averageRadius"),
          near(meta(p6, "averageRadius"), meta(p1, "averageRadius"), 1e-4))
    run_checks_b(d, full)

    width = max(len(r[1]) for r in RESULTS)
    fails = 0
    for case, what, expected, actual, ok in RESULTS:
        fails += 0 if ok else 1
        print("%-4s %-9s %-*s expected %-22s actual %s" % ("ok" if ok else "FAIL", case, width, what, expected, actual))
    print("%d / %d pass" % (len(RESULTS) - fails, len(RESULTS)))
    return fails


def span(full_case, suffix):
    """[xmin, xmax, zmin, zmax] (mm) over every body named '<prefix> PRIMITIVE <suffix>'; None when there is none."""
    rs = [r for name, rows_ in full_case["extents"].items() if name.endswith(" PRIMITIVE " + suffix) for r in rows_]
    if not rs:
        return None
    return [min(r[0] for r in rs), max(r[1] for r in rs), min(r[2] for r in rs), max(r[3] for r in rs)]


def count(full_case, suffix):
    return sum(n for name, n in full_case["counts"].items() if name.endswith(" PRIMITIVE " + suffix))


def run_checks_b(d, full):
    """2026-09-28 (b): plot region, curvature plot, extra key points, key lines, junction ticks, unsigned EI labels."""
    p1 = d["P1"]
    fp = {k: v for k, v in p1["footprint"].items() if isinstance(v, dict)}
    # Plot region: the reference line spans the region, the plot stays inside it (and reaches near both ends).
    lo_rsl, hi_rsl = 145.0, 1625.0
    wid = sorted([fp["FB_WIDEST"]["u"], fp["AB_WIDEST"]["u"]])
    inf = sorted([fp["FB_INFLECTION"]["u"], fp["AB_INFLECTION"]["u"]])
    for case, band, region, want in (("P1", "RADIUS", "FULL", None), ("P6", "RADIUS", "RSL", [lo_rsl, hi_rsl]),
                                     ("P11", "RADIUS", "WIDEST", wid), ("P12", "CURVATURE", "INFLECTION", inf)):
        b = d[case]["bands"]
        ref = span(full[case], band + " REFERENCE")
        plot = span(full[case], band)
        got = [b.get("plotFrom"), b.get("plotTo")]
        ok = bool(d[case]["settings"].get("plotRegion") == region and ref and plot)
        if ok and want:
            ok = near(got[0], want[0], 0.01) and near(got[1], want[1], 0.01)
        if ok:
            ok = near(ref[0], got[0], 0.01) and near(ref[1], got[1], 0.01) and plot[0] >= got[0] - 0.01 and plot[1] <= got[1] + 0.01
        if ok and region == "FULL":
            ok = plot[0] < lo_rsl - 1 or plot[1] > hi_rsl + 1  # the full plot runs on past the contacts
        if ok and region != "FULL":
            ok = plot[0] < got[0] + 30 and plot[1] > got[1] - 30
        ticks = [r[0] for name, rs in full[case]["extents"].items() if " %s TICK " % band in name and not re.search(r"TICK [-+]?[\d.]+ (TIP|TAIL)$", name) for r in rs]
        ok = ok and all(got[0] - 0.01 <= x <= got[1] + 0.01 for x in ticks)
        check(case, "plot region %s: reference = region, plot + key ticks inside" % region, "%s %s" % (region, want and [round(v, 2) for v in want]),
              "region %s, ref %s, plot %s, %d key ticks" % (got, ref and ref[:2], plot and plot[:2], len(ticks)), ok)
    # Curvature (P12): band, 1/m scale, values ~ 1/R of the sidecut, continuous (few runs), tables = P1
    m12 = full["P12"]["members"]
    names = ["CURVATURE", "CURVATURE REFERENCE", "CURVATURE TITLE", "CURVATURE DATUM", "CURVATURE AXIS TIP", "CURVATURE TICK 0 TIP",
             "CURVATURE TICK +0.01 TIP", "CURVATURE LABEL 0", "CURVATURE TICK MRS"]
    missing = [n for n in names if "P12 TAC curvature PRIMITIVE " + n not in m12]
    radius_left = [m for m in m12 if " PRIMITIVE RADIUS" in m]
    check("P12", "curvature band: plot, reference, title, 0.01 1/m ticks, labels; no radius band", "present",
          missing or radius_left or "all", not missing and not radius_left)
    b12 = d["P12"]["bands"]
    plot = span(full["P12"], "CURVATURE")
    kmax = (plot[3] - b12["curvature"]) / b12["plotLevelHeight"] * 0.01
    kmin = (plot[2] - b12["curvature"]) / b12["plotLevelHeight"] * 0.01
    check("P12", "curvature between inflections (1/m): max ~ 1/R sidecut, min >= ~0 (TAC: arcs, the tip arc starts at the inflection)",
          "0.05..0.08 / -0.01..max", "%.4f / %.4f" % (kmax, kmin), 0.05 <= kmax <= 0.08 and -0.01 <= kmin <= kmax)
    check("P12", "curvature level step 0.01 1/m, 5 mm", "1, 5 mm", "%s, %s" % (b12.get("plotLevelStep"), b12.get("plotLevelHeight")),
          b12.get("plotLevelStep") == 1.0 and near(b12.get("plotLevelHeight"), 5.0, 1e-6))
    labels = sorted(m.split(" LABEL ")[1] for m in m12 if " CURVATURE LABEL " in m)
    c05 = full["P12"]["counts"].get("P12 TAC curvature PRIMITIVE CURVATURE LABEL +0.05")
    check("P12", "curvature numbers every 0.05 1/m (label spacing); '0.05' drawn without '+' (6 glyph loops)", "0, +0.05; 6",
          "%s; %s" % (labels, c05), labels == ["+0.05", "0"] and c05 == 6)
    runs = full["P12"]["counts"].get("P12 TAC curvature PRIMITIVE CURVATURE", 0)
    cj12 = full["P12"]["extents"].get("P12 TAC curvature PRIMITIVE CURVATURE JUNCTION", [])
    check("P12", "curvature breaks only at edge junctions (no sign breaks): runs <= junctions + 1", "<= %d" % (len(cj12) + 1), runs,
          1 <= runs <= len(cj12) + 1)
    compare("P12 vs P1", p1, d["P12"], 1e-6)
    # Extra key points (P11)
    p11 = d["P11"]
    k11 = rows(p11, "keyLocations")
    fb, ab = k11.get("FB_Mass_location", {}), k11.get("Mass_AB", {})
    check("P11", "extra key rows: FB_Mass_location x 807.97 / 'Mass AB' x 500 z 10, flagged extra", "rows",
          "%s %s / %s %s %s" % (fb.get("x"), fb.get("extra"), ab.get("name"), ab.get("x"), ab.get("z")),
          near(fb.get("x"), 807.9668, 1e-3) and fb.get("extra") is True and ab.get("name") == "Mass AB"
          and near(ab.get("x"), 500.0, 1e-3) and near(ab.get("z"), 10.0, 1e-3))
    xs = [r["x"] for r in p11["keyLocations"]]
    st = [r["station"] for r in p11["keyLocations"]]
    check("P11", "key locations sorted by x incl. extras, stations 0..", "sorted, P1 + 2", "%d rows" % len(xs),
          xs == sorted(xs) and st == [float(i) for i in range(len(xs))] and len(xs) == len(p1["keyLocations"]) + 2)
    dx = [r["x"] for r in p11["data"]]
    check("P11", "extra points forced into the data table (x 807.97, 500): 23 rows", 23,
          "%d rows, 807.97 %s, 500 %s" % (len(dx), any(near(x, 807.9668, 1e-3) for x in dx), any(near(x, 500.0, 1e-3) for x in dx)),
          len(dx) == 23 and any(near(x, 807.9668, 1e-3) for x in dx) and any(near(x, 500.0, 1e-3) for x in dx))
    common = [(r, q) for r in p11["data"] for q in p1["data"] if near(r["x"], q["x"], 1e-6)]
    same = all(near(r[f], q[f], 1e-6) or r[f] == q[f] for r, q in common for f in q if f != "station")
    check("P11", "other data rows and metadata = P1", "equal, 21 common", "%d common rows" % len(common),
          same and len(common) == 21 and p11["metadata"] == p1["metadata"])
    m11 = full["P11"]["members"]
    want = ["PROFILE FB_Mass_location", "PROFILE Mass AB", "RADIUS TICK FB_Mass_location", "RADIUS TICK Mass AB"]
    missing = [n for n in want if "P11 TAC extra PRIMITIVE " + n not in m11]
    check("P11", "extra points: profile points + radius ticks", "present", missing or "all", not missing)
    lines = sorted(m.split(" KEY LINE ")[1] for m in m11 if " KEY LINE " in m)
    dashes = [count(full["P11"], "KEY LINE " + n) for n in lines]
    check("P11", "key lines FCP MP MRS ACP + FB_Mass_location (not Mass AB), dashed", "5 lines, > 50 dashes",
          "%s %s" % (lines, dashes), lines == sorted(["FCP", "MP", "MRS", "ACP", "FB_Mass_location"]) and min(dashes or [0]) > 50)
    no_lines = [m for m in full["P1"]["members"] if " KEY LINE " in m]
    check("P1", "key lines off by default", "none", no_lines, not no_lines)
    check("P13", "duplicate extra key point name: no primitive", "no composite", "composite" if "P13" in d else "none", "P13" not in d)
    # Junction ticks (default on): footprint and plot band, same x
    fj = sorted(r[0] for r in full["P1"]["extents"].get("P1 TAC PRIMITIVE FOOTPRINT JUNCTION", []))
    rj = sorted(r[0] for r in full["P1"]["extents"].get("P1 TAC PRIMITIVE RADIUS JUNCTION", []))
    check("P1", "junction ticks: footprint + radius band at the same x", ">= 2, equal x",
          "%d / %d, bands.junctions %s" % (len(fj), len(rj), p1["bands"].get("junctions")),
          len(fj) >= 2 and len(fj) == len(rj) == p1["bands"].get("junctions") and all(abs(a - b) < 1e-3 for a, b in zip(fj, rj)))
    cj = [r[0] for r in full["P12"]["extents"].get("P12 TAC curvature PRIMITIVE CURVATURE JUNCTION", [])]
    check("P12", "curvature junction ticks inside the region only", "inside, fewer than P1", "%d ticks" % len(cj),
          all(inf[0] - 0.01 <= x <= inf[1] + 0.01 for x in cj) and len(cj) < len(fj))
    # EI numbers without "+" (glyph loops: "150" = 4, "50" = 3); radius numbers keep their sign ("+10" = 4, "-10" = 4)
    e150 = full["P8"]["counts"].get("P8 TAC EI PRIMITIVE EI LABEL +150")
    e50 = full["P8"]["counts"].get("P8 TAC EI PRIMITIVE EI LABEL +50")
    r10 = full["P1"]["counts"].get("P1 TAC PRIMITIVE RADIUS LABEL +10")
    rm10 = full["P1"]["counts"].get("P1 TAC PRIMITIVE RADIUS LABEL -10")
    check("P8", "EI numbers without '+' (150 = 4 loops, 50 = 3); radius keeps signs (+10 = 4, -10 = 4)", "4 3 / 4 4",
          "%s %s / %s %s" % (e150, e50, r10, rm10), e150 == 4 and e50 == 3 and r10 == 4 and rm10 == 4)
    et = full["P8"]["counts"].get("P8 TAC EI PRIMITIVE EI TITLE")
    check("P8", "EI title 'EI (Nm^2)': E I ( N m ) 2 = 7 glyph loops (no '*')", 7, et, et == 7)


if __name__ == "__main__":
    if "--unsuppress" in sys.argv:
        set_suppressed(None)
    if "--keep" in sys.argv:
        import atexit
        atexit.register(set_suppressed, set(sys.argv[sys.argv.index("--keep") + 1].split(",")))
    prims = primitives()
    if "--json" in sys.argv:
        with open(sys.argv[sys.argv.index("--json") + 1], "w") as fh:
            json.dump(prims, fh, indent=1)
        sys.exit(0)
    before = None
    if "--before" in sys.argv:
        with open(sys.argv[sys.argv.index("--before") + 1]) as fh:
            before = json.load(fh)
    if "--dump" not in sys.argv:
        sys.exit(1 if run_checks(prims, before) else 0)
    if "--dump" in sys.argv:
        for k, v in prims.items():
            d = v["data"]
            print("=====", k, "| bom", v["bom"], "| members", len(v["members"]))
            for sec in ("settings", "bands", "footprint"):
                print(sec, d[sec])
            for sec in ("scaleFactors", "metadata", "keyLocations", "baseline"):
                print("--", sec)
                for r in d[sec]:
                    print("   ", {kk: vv for kk, vv in r.items() if kk not in ("note",)})
            print("-- data")
            for r in d["data"]:
                print("   ", r)
            print("-- members", sorted(v["members"]))
        sys.exit(0)
