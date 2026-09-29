"""Checks the Export primitive tests in "Primitive tests" (5c3ac8fb8ec70b1f256c0e97; Publish & Drawing tools, built by build_primitive_tests.py).

Reads every "<prefix> PRIMITIVE" composite's attribute (schema primitive/1) through the eval API, plus the feature
statuses, and compares:
  * RD 20TAC known values (avg radius ~17.05 m, natural radius widest ~17.48 m / inflection ~16.18 m,
    RSL 1480, FRCPl 130, ARCPl 50) -- P1 / P2 / P3;
  * P4 (mirrored, tip -X) against P3: every value equal, x negated;
  * P5 (datum at x 500, z 10) against P1: every value equal, x - 500, z - 10;
  * P7 (datum = MRS connector, Datum uses ORIGIN) against P1: x - 885; P9 (COORDINATE_SYSTEM) against P5;
  * P8 (target EI 150 N*m^2 constant): deflection = P L^3 / 48 EI, stiffness = 48 EI (1 in) / L^3, block rows;
  * P18 (real TARG_EI + block names FROM WIRE Aufbug_22 / FR2_Wire): block rows = wire names, deflection / stiffness,
    EI band, other rows = P1;
  * only rows with data (no "n/a" / phase-2 rows); P1's flat baseline has no rocker / camber rows or points;
  * structure: composite members, band bodies, key points;
  * 2026-09-29: key lines and grid lines are ONE light-grey edge each (were dash segments); P1 (35 rows, the default)
    has 37 RSL data rows (+ XS1 / XS2), rows at key locations carry the key's name, numbers stay continuous;
  * auto scale (2026-09-29, default on): P1 / P6 / P11 / P15 (radius, every region) and P12 / P16 (curvature) share
    the band height (150 mm, zero line 30 mm up), end axes, reference line and stacking; P1's largest plotted radius
    sits at 85-95 % of the positive height on a nice step (TAC: 21.32 m, step 5 m, top 25 m, 0.853); P12 curvature
    likewise (0.0635 1/m, top 0.07, 0.907); P8's EI band is 0..150 mm with 150 N*m^2 at 0.857 of top 175 (step 25);
    P17 (Auto scale off) = P1 BEFORE auto scale (bands data, frame geometry, plot extent; needs --before);
  * xSection V58 pinned in primitive_baseline / export_primitive, the exactContacts workaround gone;
  * Station definition: the spec default of "variableName" is "" (new features: no # variable; SD1 in this studio),
    while the saved "Stations (test)" in Part Studio 1 keeps "stations" and its Station geometry features resolve;
  * 2026-09-29 Table 3 = Location | x | s | Dist. from tail (REST fstable), distFromTail = |x - x(TAIL)| on every case;
  * R1-R3 SW rout (Table 4): the RD SW_ROUT_SURFACE sheet -> 7.0 deg / 0.80 / 4.00 at MRS, start / stop = sheet extent,
    Table 4 cells, embedded Extract-variables keys; R2 (-Y mirror, picked start / stop) = R1 + P1's ACP / FCP rows;
    R3 (one face missing MRS) start / stop only; R1 / R3 other tables = P1;
  * P14 (the user's tail bite on a copy of the volume): the unwrap is an isometry (periphery length, base area), the
    footprint reaches the bite's true depth on the centreline, only the bite adds corners, the radius band beyond the
    bite = P1's and has no spikes through it, and inside the RSL everything = P3.
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
        var edgeCounts = {};
        var colours = {};
        var extents = {};
        for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
        {
            const name = getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME });
            counts[name] = (counts[name] == undefined ? 0 : counts[name]) + 1;
            edgeCounts[name] = (edgeCounts[name] == undefined ? 0 : edgeCounts[name]) + size(evaluateQuery(context, qOwnedByBody(m, EntityType.EDGE)));
            const a = getProperty(context, { "entity" : m, "propertyType" : PropertyType.APPEARANCE });
            colours[name] = round(a.red * 100) / 100 ~ "/" ~ round(a.green * 100) / 100 ~ "/" ~ round(a.blue * 100) / 100;
            // x / z ranges (mm) of the plot band's plot, reference line, junction and key-location ticks, per body (world frame).
            if (match(name, ".* (RADIUS|CURVATURE|EI)( REFERENCE| JUNCTION| TICK .*| AXIS .*| LABEL .*)?").hasMatch || match(name, ".* FOOTPRINT JUNCTION").hasMatch || match(name, ".* PROFILE SW ROUT").hasMatch)
            {
                const bb = evBox3d(context, { "topology" : m, "tight" : true });
                const row = [round(bb.minCorner[0] / millimeter * 1000) / 1000, round(bb.maxCorner[0] / millimeter * 1000) / 1000,
                             round(bb.minCorner[2] / millimeter * 1000) / 1000, round(bb.maxCorner[2] / millimeter * 1000) / 1000];
                extents[name] = append(extents[name] == undefined ? [] : extents[name], row);
            }
        }
        // Sampled footprint / plot-band geometry (x, z mm; start / end tangents) for the unwrap checks (P1, P14 only).
        var shapes = {};
        if ((data.prefix == "P1 TAC") || (data.prefix == "P14 TAC bite"))
        {
            var ts = [];
            for (var i = 0; i <= 100; i += 1)
            {
                ts = append(ts, i / 100);
            }
            for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
            {
                const name = getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME });
                if ((name != (data.prefix ~ " PRIMITIVE FOOTPRINT")) && (name != (data.prefix ~ " PRIMITIVE RADIUS")))
                {
                    continue;
                }
                var edges = [];
                for (var e in evaluateQuery(context, qOwnedByBody(m, EntityType.EDGE)))
                {
                    const tls = evEdgeTangentLines(context, { "edge" : e, "parameters" : ts });
                    var pts = [];
                    for (var tl in tls)
                    {
                        pts = append(pts, [round(tl.origin[0] / millimeter * 1e5) / 1e5, round(tl.origin[2] / millimeter * 1e5) / 1e5]);
                    }
                    const d0 = tls[0].direction;
                    const d1 = tls[100].direction;
                    edges = append(edges, { "p" : pts, "t0" : [d0[0], d0[2]], "t1" : [d1[0], d1[2]] });
                }
                shapes[name] = append(shapes[name] == undefined ? [] : shapes[name], edges);
            }
        }
        var members = [];
        for (var entry in counts)
        {
            members = append(members, entry.key);
        }
        out = append(out, "JSON " ~ toString({ "data" : data, "members" : members, "counts" : counts, "edges" : edgeCounts, "colours" : colours, "extents" : extents, "shapes" : shapes,
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
        if not re.match(r"[PR]\d+$", case):
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


def lvl_name(level, digits=0):
    """A scale level (whole level units) as primitive_output's levelName: 0 -> "0", 25 -> "+25", -5 -> "-5",
    5 with 2 digits -> "+0.05"."""
    n = abs(int(round(level)))
    if n == 0:
        return "0"
    text = str(n) if digits == 0 else "%d.%0*d" % (n // 10 ** digits, digits, n % 10 ** digits)
    return ("+" if level > 0 else "-") + text


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
        if re.match(r"[PR]\d+$", name.split()[0]):
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
    check("P1", "RSL data rows (N 35 + XS1 + XS2; MRS is a grid row)", 37, len(p1["data"]), len(p1["data"]) == 37)
    at_fcp = [r for r in p1["data"] if near(r["x"], 1625.0, 1e-3)]
    check("P1", "ski_thck at FCP mm (X-Sect SPA)", 6.75, at_fcp and at_fcp[0]["skiThck"], at_fcp and near(at_fcp[0]["skiThck"], 6.75, 0.01))
    # 2026-09-28 station numbers: Key locations and Data sorted by x ascending, # from 0 at the lowest x
    for sec in ("keyLocations", "data"):
        xs = [r["x"] for r in p1[sec]]
        st = [r.get("station") for r in p1[sec]]
        check("P1", "%s by x ascending, station 0.." % sec, "sorted, 0..%d" % (len(xs) - 1), "%s .. %s, %s" % (xs[0], xs[-1], st[:3]),
              xs == sorted(xs) and st == [float(i) for i in range(len(xs))])
    # 2026-09-29: RSL data rows at a key location carry its name (the # cell shows it); numbers count every row.
    named = {r["name"]: r["station"] for r in p1["data"] if r.get("name")}
    want_named = {"ACP": 145.0, "XS2": 515.0, "MRS": 885.0, "XS1": 1255.0, "FCP": 1625.0}
    xs_named = {r["name"]: r["x"] for r in p1["data"] if r.get("name")}
    check("P1", "RSL data: rows named ACP XS2 MRS XS1 FCP at their x (MP 807.97 is no sample: unnamed), others ''",
          sorted(want_named), xs_named,
          set(xs_named) == set(want_named) and all(near(xs_named[k], v, 1e-3) for k, v in want_named.items())
          and all(r.get("name") == "" for r in p1["data"] if r["name"] not in want_named))
    check("P1", "RSL data: named rows keep their number (ACP 0, MRS 18, FCP 36)", "0 / 18 / 36",
          "%s / %s / %s" % (named.get("ACP"), named.get("MRS"), named.get("FCP")),
          named.get("ACP") == 0.0 and named.get("MRS") == 18.0 and named.get("FCP") == 36.0)
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

    # P18 (2026-09-29, ported from the user's retired studio): the real TARG_EI wire as Target EI, tip / tail block
    # names FROM WIRE (Aufbug_22 / FR2_Wire; the builder sets the hidden wire-name strings the dialog's editing logic
    # would fill).
    p18 = d["P18"]
    b18 = rows(p18, "baseline")
    check("P18", "Tip / Tail block rows FROM WIRE", "Aufbug_22 / FR2_Wire",
          "%s / %s" % (b18.get("tipBlock", {}).get("value"), b18.get("tailBlock", {}).get("value")),
          b18.get("tipBlock", {}).get("value") == "Aufbug_22" and b18.get("tailBlock", {}).get("value") == "FR2_Wire")
    m18 = rows(p18, "metadata")
    d18, s18 = m18.get("deflection", {}).get("value"), m18.get("stiffness", {}).get("value")
    check("P18", "TARG_EI: deflection / stiffness rows, EI band", "> 0 / > 0, EI plot",
          "%s / %s, %s" % (d18, s18, "P18 TAC wires PRIMITIVE EI" in full["P18"]["members"]),
          isinstance(d18, float) and d18 > 0 and isinstance(s18, float) and s18 > 0 and "P18 TAC wires PRIMITIVE EI" in full["P18"]["members"])
    check("P18", "other rows = P1", "equal", "",
          [r for r in p18["metadata"] if r["key"] not in REMOVED] == p1["metadata"] and
          [r for r in p18["baseline"] if r["key"] not in REMOVED] == p1["baseline"])

    if before:
        old = {k.split()[0]: v["data"] for k, v in before.items()}
        for case in ("P1", "P2", "P3", "P4", "P5", "P6"):
            flat = d[case]["settings"]["baselineFlat"]
            compare("%s vs before" % case, strip(old[case], flat), strip(d[case], False), 1e-6)

    # 2026-09-28 radius frame, dashed grid, colours, labels (P1 labels on by default, P10 grid on, labels off)
    m1, m10 = full["P1"]["members"], full["P10"]["members"]
    # Auto scale (2026-09-29): the tick step and levels come from the data (bands.radiusTickStep / plotLevels).
    b1 = p1["bands"]
    step1 = int(b1["radiusTickStep"])
    frame = ["RADIUS AXIS TIP", "RADIUS AXIS TAIL"] + ["RADIUS TICK %s %s" % (lvl_name(v), end) for v in b1["plotLevels"] for end in ("TIP", "TAIL")]
    missing = [n for n in frame if "P1 TAC PRIMITIVE " + n not in m1]
    check("P1", "radius frame: end axes + a tick at every plotLevel on both axes", "present", missing or "all", not missing)
    levels = sorted(int(m.split()[-2]) for m in m1 if re.search(r"RADIUS TICK [-+]?\d+ TIP$", m))
    check("P1", "tick levels every radiusTickStep, 0 incl., top = axisTo, bottom within axisFrom", "step %d, 0..%s" % (step1, b1["axisTo"]),
          levels, levels and all(b - a == step1 for a, b in zip(levels, levels[1:])) and 0 in levels
          and levels[-1] == b1["axisTo"] and levels[0] >= b1["axisFrom"] and levels == [int(v) for v in b1["plotLevels"]])
    grid1 = [m for m in m1 if " RADIUS GRID " in m]
    check("P1", "grid lines off by default", "none", grid1, not grid1)
    grid10 = {m: (full["P10"]["counts"][m], full["P10"]["edges"][m]) for m in m10 if " RADIUS GRID " in m}
    lv10 = sorted(int(m.split()[-1]) for m in grid10)
    check("P10", "grid lines: ONE body / edge at every level but 0 (2026-09-29, was dashes)", "levels = P1 ticks - 0, 1 edge each",
          "%s %s" % (lv10, sorted(set(grid10.values()))), lv10 == [v for v in levels if v != 0] and all(n == (1, 1) for n in grid10.values()))
    text10 = [m for m in m10 if m.endswith(" TITLE") or " RADIUS LABEL " in m]
    check("P10", "labels off: no text", "none", text10, not text10)
    compare("P10 vs P1", p1, d["P10"], 1e-6)
    titles = ["BASELINE TITLE", "PROFILE TITLE", "FOOTPRINT TITLE", "RADIUS TITLE", "RADIUS LABEL 0", "RADIUS LABEL +10",
              "RADIUS LABEL -%d" % step1, "RADIUS LABEL %s" % lvl_name(b1["axisTo"])]
    missing = [n for n in titles if "P1 TAC PRIMITIVE " + n not in m1]
    check("P1", "labels (default on): band titles + scale numbers", "present", missing or "all", not missing)
    col = full["P1"]["colours"]
    want = {"BASELINE": "0.09/0.32/0.69", "BASELINE FCP": "0.09/0.32/0.69", "PROFILE BOTTOM": "0.0/0.5/0.25", "PROFILE TOP": "0.0/0.5/0.25",
            "FOOTPRINT": "0.85/0.4/0.0", "RADIUS": "0.75/0.1/0.1", "RADIUS REFERENCE": "0.5/0.5/0.5", "RADIUS TICK FCP": "0.5/0.5/0.5",
            "RADIUS AXIS TIP": "0.5/0.5/0.5", "RADIUS LABEL +10": "0.5/0.5/0.5", "FOOTPRINT TITLE": "0.85/0.4/0.0"}
    norm = lambda v: "/".join("%g" % float(x) for x in str(v).split("/"))
    bad = ["%s %s" % (k, col.get("P1 TAC PRIMITIVE " + k)) for k, v in want.items() if norm(col.get("P1 TAC PRIMITIVE " + k, "0/0/0")) != norm(v)]
    check("P1", "appearance per band (read back from the composite members)", "band colours", bad or "all", not bad)
    grey = norm(full["P10"]["colours"].get("P10 TAC grid PRIMITIVE RADIUS GRID +%d" % step1, "0/0/0"))
    check("P10", "grid lines light grey", "0.8/0.8/0.8", grey, grey == "0.8/0.8/0.8")

    # EI band (P8 only: target EI 150 N*m^2; auto scale 2026-09-29: band height 150 mm, top = k * eiTickStep)
    m8 = full["P8"]["members"]
    b8a = d["P8"]["bands"]
    e_step, e_top = int(b8a["eiTickStep"]), int(b8a["eiAxisMax"])
    ei_names = ["EI", "EI REFERENCE", "EI AXIS TIP", "EI AXIS TAIL", "EI TITLE", "EI DATUM", "EI LABEL +150", "EI LABEL +%d" % e_top]
    ei_names += ["EI TICK +%d %s" % (v, end) if v else "EI TICK 0 %s" % end for v in range(0, e_top + 1, e_step) for end in ("TIP", "TAIL")]
    missing = [n for n in ei_names if "P8 TAC EI PRIMITIVE " + n not in m8]
    top = [m for m in m8 if re.search(r" EI TICK \+%d " % (e_top + e_step), m)]
    check("P8", "EI band: plot, reference, axes, ticks 0..top by eiTickStep, labels (top numbered), title, datum",
          "present, 0..%d by %d, nothing above" % (e_top, e_step), missing or ("all" if not top else top), not missing and not top)
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
    run_checks_axes(d, full, before)
    run_checks_repin()
    run_checks_stations()
    if "P14" in d:
        run_checks_unwrap(d, full)
    run_checks_rout(d, full)

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


def fstable(kind, name_filter):
    """The "Primitive tables" custom table output (REST fstable) for one table kind and primitive name filter."""
    t = [e for e in c.list_elements(D, W) if e["name"] == "primitive_table"][0]
    ns = "e%s::m%s" % (t["id"], t["microversionId"])
    r = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/fstable",
              {"tableType": "primitiveTables", "tableNamespace": ns,
               "tableParameters": 'tableKind=PrimitiveTableKind.%s;nameFilter="%s"' % (kind, name_filter)})
    return r.get("tables", [])


EMBED_SCRIPT = r'''
function(context is Context, queries)
{
    const v = getVariable(context, toString(makeId("%s")));
    var out = [];
    for (var k in ["swRoutAngle", "swRoutStepIn", "swRoutDistAboveBase", "swRoutStartX", "swRoutStartS", "swRoutStopX", "swRoutStopS"])
    {
        const value = v.variable[k].value;
        out = append(out, "EMBED " ~ k ~ " " ~ (value is number ? value : (k == "swRoutAngle" ? value / degree : value / millimeter)));
    }
    return out;
}
'''


def embedded(case):
    """The SW rout keys a case's feature embeds for Extract variables: {key: number (deg / mm)}."""
    f = c.get(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/features")
    fid = [x["featureId"] for x in f["features"] if x["name"].split()[0] == case][0]
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}/featurescript", json_data={"script": EMBED_SCRIPT % fid})
    vals = re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result")))
    return {v.split()[1]: float(v.split()[2]) for v in vals if v.startswith("EMBED ")}


# The derived SW_ROUT_SURFACE sheet (RD 20TAC Design Master V1), probed independently with the eval API: the TAC
# volume at MRS (x 885) has its outside at |y| 48.72930 (base edge, vertical side up to z 4), a 0.8 mm shelf at z 4
# (inner end |y| 47.92935) and the rout face rising from there at 7.0 deg to Z (face normal z = sin 7 deg); the sheet
# spans x 0.46599 .. 1784.25132. RD model variables: SW_Rout_Step_In 0.8 mm, SW_Rout_Above_Bottom 4 mm.
ROUT_EXPECT = {"angle": 7.0, "stepIn": 0.8, "distAboveBase": 4.0}
ROUT_SHEET_X = (0.46599287400549716, 1784.2513154849216)
ROUT_OUTSIDE_Y = 48.72930096625616


def run_checks_rout(d, full):
    """2026-09-29: Table 3 = Location | x | s | Dist. from tail; Table 4 SW rout (R1-R3)."""
    p1 = d["P1"]
    k1 = rows(p1, "keyLocations")
    # Table 3: distFromTail = |x - x(TAIL)| on every primitive (tip +X, tip -X, datum shifts); old fields kept.
    bad = []
    for case, dd in sorted(d.items()):
        if not dd.get("keyLocations"):
            continue
        kk = rows(dd, "keyLocations")
        xt = kk["TAIL"]["x"]
        for key, r in kk.items():
            if not near(r.get("distFromTail"), abs(r["x"] - xt), 2e-4):
                bad.append("%s.%s %s vs %s" % (case, key, r.get("distFromTail"), abs(r["x"] - xt)))
            if not all(f in r for f in ("y", "z", "w", "h")):
                bad.append("%s.%s lost y/z/w/h" % (case, key))
    check("T3", "key rows: distFromTail = |x - x(TAIL)|, y z w h kept (all cases)", "all", "%d bad %s" % (len(bad), bad[:3]), not bad)
    t3 = fstable("KEY_LOCATIONS", "P1 TAC")
    heads = [col["header"] for col in t3[0]["columns"]] if t3 else []
    check("T3", "P1 Table 3 columns", "Location | x | s | Dist. from tail", " | ".join(heads),
          heads == ["Location", "x (mm)", "s (mm)", "Dist. from tail (mm)"])
    names = [row["columnIdToValue"]["name"] for row in t3[0]["rows"]] if t3 else []
    tail = [row["columnIdToValue"] for row in t3[0]["rows"] if row["columnIdToValue"]["name"] == "TAIL"] if t3 else []
    check("T3", "P1 Table 3 rows sorted by x, TAIL dist 0, FCP dist = 1625 - x(TAIL)", "%d rows" % len(p1["keyLocations"]),
          "%d rows, TAIL %s" % (len(names), tail[0]["distFromTail"] if tail else "-"),
          names == [r["name"] for r in p1["keyLocations"]] and tail and tail[0]["distFromTail"] == "0"
          and near(k1["FCP"]["distFromTail"], 1625.0 - k1["TAIL"]["x"], 2e-4))
    t4p1 = fstable("SW_ROUT", "P1 TAC")
    check("T4", "no SW rout surface (P1): no Table 4, settings.swRout false", "none / false",
          "%d / %s" % (len(t4p1), p1["settings"].get("swRout")), not t4p1 and p1["settings"].get("swRout") is False and p1["swRout"] == [])
    if "R1" not in d:
        return

    r1 = rows(d["R1"], "swRout")
    got = {k: r1[k]["value"] if k in r1 else None for k in ROUT_EXPECT}
    check("R1", "angle deg (0.1) / step-in mm (0.01) / dist. above base mm (0.01)", "7.0 / 0.80 / 4.00",
          "%s / %s / %s" % (got["angle"], got["stepIn"], got["distAboveBase"]),
          near(got["angle"], 7.0, 0.1) and near(got["stepIn"], 0.8, 0.01) and near(got["distAboveBase"], 4.0, 0.01))
    sec = d["R1"]["swRoutSection"]
    check("R1", "section: +Y side, start edge = lowest end inside the ski (y 47.93, z 4), outside y 48.73", "+Y / 47.93 / 4 / 48.73",
          "%s / %s / %s / %s (%s)" % (sec.get("side"), sec.get("startY"), sec.get("startZ"), sec.get("outsideY"), sec.get("startRule")),
          sec.get("side") == "+Y" and near(sec.get("startY"), 47.92935, 0.01) and near(sec.get("startZ"), 4.0, 0.01)
          and near(sec.get("outsideY"), ROUT_OUTSIDE_Y, 0.01) and sec.get("startRule") == "lowest inside the ski")
    xt = k1["TAIL"]["x"]
    st, sp = r1["start"], r1["stop"]
    check("R1", "start / stop x = the sheet's x extent (0.01 mm), dist. from tail = |x - x(TAIL)|", "%.2f / %.2f" % ROUT_SHEET_X,
          "%s / %s, %s / %s" % (st["x"], sp["x"], st["distFromTail"], sp["distFromTail"]),
          near(st["x"], ROUT_SHEET_X[0], 0.01) and near(sp["x"], ROUT_SHEET_X[1], 0.01)
          and near(st["distFromTail"], abs(st["x"] - xt), 2e-4) and near(sp["distFromTail"], abs(sp["x"] - xt), 2e-4))
    # Both ends lie just past the bottom wire's ends (TAIL x 0.63, TIP x 1783.75): s runs on along the end tangents.
    check("R1", "start / stop s past the wire ends: s(start) < s(TAIL), s(stop) > s(TIP), both increasing with x", "yes",
          "%s < %s, %s > %s" % (st["s"], k1["TAIL"]["s"], sp["s"], k1["TIP"]["s"]),
          st["s"] < k1["TAIL"]["s"] and sp["s"] > k1["TIP"]["s"])
    check("R1", "MRS rows at x / s of MRS (= P1 MRS row)", "%s / %s" % (k1["MRS"]["x"], k1["MRS"]["s"]),
          "%s / %s" % (r1["angle"]["x"], r1["angle"]["s"]),
          all(near(r1[k]["x"], k1["MRS"]["x"], 1e-4) and near(r1[k]["s"], k1["MRS"]["s"], 1e-4) and near(r1[k]["distFromTail"], k1["MRS"]["distFromTail"], 1e-4)
              for k in ROUT_EXPECT))
    pt = full["R1"]["extents"].get("R1 TAC rout PRIMITIVE PROFILE SW ROUT")
    zp = d["R1"]["bands"]["profile"]
    check("R1", "PROFILE SW ROUT point at x(MRS), profile band + 4 mm", "885 / %.3f" % (zp + 4.0),
          pt, bool(pt) and near(pt[0][0], 885.0, 1e-3) and near(pt[0][2], zp + 4.0, 1e-3))
    t4 = fstable("SW_ROUT", "R1 TAC rout")
    heads = [col["header"] for col in t4[0]["columns"]] if t4 else []
    cells = {row["columnIdToValue"]["name"]: row["columnIdToValue"] for row in t4[0]["rows"]} if t4 else {}
    check("R1", "Table 4 columns + rows (angle 1 decimal)", "Measure|Value|Unit|x|s|Dist. from tail; 7.0 deg; 5 rows",
          "%s; %s; %d rows" % ("|".join(heads), cells.get("SW rout angle", {}).get("value"), len(cells)),
          heads == ["Measure", "Value", "Unit", "x (mm)", "s (mm)", "Dist. from tail (mm)"]
          and cells.get("SW rout angle", {}).get("value") == "7.0" and cells.get("Step-in", {}).get("value") == "0.8"
          and cells.get("Dist. above base", {}).get("value") == "4" and set(cells) == {"SW rout angle", "Step-in", "Dist. above base", "Start", "Stop"})
    emb = embedded("R1")
    want = {"swRoutAngle": got["angle"], "swRoutStepIn": got["stepIn"], "swRoutDistAboveBase": got["distAboveBase"],
            "swRoutStartX": st["x"], "swRoutStartS": st["s"], "swRoutStopX": sp["x"], "swRoutStopS": sp["s"]}
    check("R1", "embedded Extract-variables keys = Table 4 (deg / mm)", "7 keys equal", emb,
          all(near(emb.get(k), v, 1e-6) for k, v in want.items()))
    compare("R1", p1, d["R1"], 1e-4)

    r2 = rows(d["R2"], "swRout")
    sec2 = d["R2"]["swRoutSection"]
    check("R2", "rout on -Y only: measured there, mirrored = R1 values", "-Y, R1 values",
          "%s %s / %s / %s" % (sec2.get("side"), r2.get("angle", {}).get("value"), r2.get("stepIn", {}).get("value"), r2.get("distAboveBase", {}).get("value")),
          sec2.get("side") == "-Y" and all(k in r2 and near(r2[k]["value"], r1[k]["value"], 1e-3) for k in ROUT_EXPECT)
          and near(sec2.get("startY"), -sec.get("startY"), 1e-3))
    ok = all(near(r2[e][f], k1[k][f], 1e-4) for e, k in (("start", "ACP"), ("stop", "FCP")) for f in ("x", "s", "distFromTail"))
    check("R2", "picked start / stop (ACP / FCP connectors): x, s, dist. from tail = P1's ACP / FCP rows", "145 / 1625",
          "%s / %s (s %s / %s)" % (r2["start"]["x"], r2["stop"]["x"], r2["start"]["s"], r2["stop"]["s"]),
          ok and r2["start"]["note"] == "picked" and r2["stop"]["note"] == "picked")

    r3 = d["R3"]["swRout"]
    keys3 = [r["key"] for r in r3]
    t4r3 = fstable("SW_ROUT", "R3 TAC rout short")
    xs = [r["x"] for r in r3]
    check("R3", "face misses MRS: start / stop only (face x extent around 1515), no MRS rows / point, INFO", "start, stop; INFO",
          "%s x %s; table rows %d" % (keys3, xs, len(t4r3[0]["rows"]) if t4r3 else 0),
          keys3 == ["start", "stop"] and 885.0 < xs[0] < 1515.41 < xs[1] < 1625.0 and d["R3"]["swRoutSection"] == {}
          and not any("SW ROUT" in m for m in full["R3"]["members"]) and t4r3 and len(t4r3[0]["rows"]) == 2)
    compare("R3", p1, d["R3"], 1e-4)


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
        frame_x = [b.get("frameFrom"), b.get("frameTo")]
        if ok:
            ok = near(ref[0], frame_x[0], 0.01) and near(ref[1], frame_x[1], 0.01) and plot[0] >= got[0] - 0.01 and plot[1] <= got[1] + 0.01
        if ok and region == "FULL":
            ok = plot[0] < lo_rsl - 1 or plot[1] > hi_rsl + 1  # the full plot runs on past the contacts
        if ok and region != "FULL":
            ok = plot[0] < got[0] + 30 and plot[1] > got[1] - 30
        ticks = [r[0] for name, rs in full[case]["extents"].items() if " %s TICK " % band in name and not re.search(r"TICK [-+]?[\d.]+ (TIP|TAIL)$", name) for r in rs]
        ok = ok and all(got[0] - 0.01 <= x <= got[1] + 0.01 for x in ticks)
        check(case, "plot region %s: reference = full frame, plot + key ticks inside the region" % region, "%s %s" % (region, want and [round(v, 2) for v in want]),
              "region %s, frame %s, ref %s, plot %s, %d key ticks" % (got, frame_x, ref and ref[:2], plot and plot[:2], len(ticks)), ok)
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
    # Auto scale: step 0.01 1/m (TAC max 0.0635 -> top 0.07), level height = 120 mm / 7.
    check("P12", "curvature auto scale: step 0.01 1/m, top 0.07 1/m, level height 120 / 7 mm", "1, 7, 17.1429 mm",
          "%s, %s, %s" % (b12.get("plotLevelStep"), b12.get("axisTo"), b12.get("plotLevelHeight")),
          b12.get("plotLevelStep") == 1.0 and b12.get("axisTo") == 7.0 and near(b12.get("plotLevelHeight"), 120.0 / 7, 1e-3))
    labels = sorted(m.split(" LABEL ")[1] for m in m12 if " CURVATURE LABEL " in m)
    want_labels = sorted(lvl_name(v, 2) for v in b12["plotLevels"])
    c05 = full["P12"]["counts"].get("P12 TAC curvature PRIMITIVE CURVATURE LABEL +0.05")
    check("P12", "curvature numbers at every 0.01 1/m level (-0.01..+0.07, top numbered); '0.05' drawn without '+' (6 glyph loops)",
          "%d labels; 6" % len(want_labels), "%s; %s" % (labels, c05), labels == want_labels and c05 == 6)
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
    check("P11", "extra points forced into the data table (x 807.97, 500): 39 rows", 39,
          "%d rows, 807.97 %s, 500 %s" % (len(dx), any(near(x, 807.9668, 1e-3) for x in dx), any(near(x, 500.0, 1e-3) for x in dx)),
          len(dx) == 39 and any(near(x, 807.9668, 1e-3) for x in dx) and any(near(x, 500.0, 1e-3) for x in dx))
    common = [(r, q) for r in p11["data"] for q in p1["data"] if near(r["x"], q["x"], 1e-6)]
    same = all(near(r[f], q[f], 1e-6) or r[f] == q[f] for r, q in common for f in q if f != "station")
    check("P11", "other data rows and metadata = P1", "equal, 37 common", "%d common rows" % len(common),
          same and len(common) == 37 and p11["metadata"] == p1["metadata"])
    n11 = {r["name"]: r["station"] for r in p11["data"] if r.get("name")}
    check("P11", "RSL data: forced extra rows named (MP coincides with FB_Mass_location -> joined), numbers continuous",
          "Mass AB, MP/FB_Mass_location; 0..38", sorted(n11),
          set(n11) == {"ACP", "XS2", "MRS", "XS1", "FCP", "Mass AB", "MP/FB_Mass_location"}
          and [r["station"] for r in p11["data"]] == [float(i) for i in range(len(p11["data"]))])
    m11 = full["P11"]["members"]
    want = ["PROFILE FB_Mass_location", "PROFILE Mass AB", "RADIUS TICK FB_Mass_location", "RADIUS TICK Mass AB"]
    missing = [n for n in want if "P11 TAC extra PRIMITIVE " + n not in m11]
    check("P11", "extra points: profile points + radius ticks", "present", missing or "all", not missing)
    lines = sorted(m.split(" KEY LINE ")[1] for m in m11 if " KEY LINE " in m)
    norm = lambda v: "/".join("%g" % float(x) for x in str(v).split("/"))
    kl = {n: (full["P11"]["counts"]["P11 TAC extra PRIMITIVE KEY LINE " + n], full["P11"]["edges"]["P11 TAC extra PRIMITIVE KEY LINE " + n],
              norm(full["P11"]["colours"]["P11 TAC extra PRIMITIVE KEY LINE " + n])) for n in lines}
    check("P11", "key lines FCP MP MRS ACP + FB_Mass_location (not Mass AB): ONE light-grey edge each (2026-09-29)",
          "5 lines, 1 body / 1 edge, 0.8/0.8/0.8", kl,
          lines == sorted(["FCP", "MP", "MRS", "ACP", "FB_Mass_location"]) and all(v == (1, 1, "0.8/0.8/0.8") for v in kl.values()))
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
    rm5 = full["P1"]["counts"].get("P1 TAC PRIMITIVE RADIUS LABEL -5")
    check("P8", "EI numbers without '+' (150 = 4 loops, 50 = 3); radius keeps signs (+10 = 4, -5 = 2)", "4 3 / 4 2",
          "%s %s / %s %s" % (e150, e50, r10, rm5), e150 == 4 and e50 == 3 and r10 == 4 and rm5 == 2)
    et = full["P8"]["counts"].get("P8 TAC EI PRIMITIVE EI TITLE")
    check("P8", "EI title 'EI (Nm^2)': E I ( N m ) 2 = 7 glyph loops (no '*')", 7, et, et == 7)


def frame_geometry(full_case, data, band):
    """{name suffix: [[xmin, xmax, zmin - z_ref, zmax - z_ref], ...]} of the band's fixed frame: end axes, reference line,
    level ticks and numbers (not the plot, key-location or junction ticks)."""
    zref = data["bands"][band.lower()]
    out = {}
    for name, rows_ in full_case["extents"].items():
        suffix = name.split(" PRIMITIVE ", 1)[1]
        if re.match(r"%s (AXIS (TIP|TAIL)|REFERENCE|TICK [-+]?[\d.]+ (TIP|TAIL)|LABEL .*)$" % band, suffix):
            out[suffix] = sorted([r[0], r[1], round(r[2] - zref, 3), round(r[3] - zref, 3)] for r in rows_)
    return out


def same_geometry(a, b, tol=1e-3):
    if set(a) != set(b):
        return "names differ: %s" % sorted(set(a) ^ set(b))[:4]
    for k in a:
        if len(a[k]) != len(b[k]):
            return "%s count %d vs %d" % (k, len(a[k]), len(b[k]))
        for ra, rb in zip(a[k], b[k]):
            if any(abs(x - y) > tol for x, y in zip(ra, rb)):
                return "%s %s vs %s" % (k, ra, rb)
    return ""


def nice(step, bases):
    """True when step is one of `bases` times 1, 10, 100, ..."""
    for decade in (1, 10, 100, 1000, 10000):
        if any(abs(step - b * decade) < 1e-9 for b in bases):
            return True
    return False


def run_checks_axes(d, full, before=None):
    """2026-09-29 auto scale (default): the radius / curvature band and the EI band keep a FIXED height (Radius band
    height 150 mm, zero line 30 mm up; EI band height 150 mm) and position, whatever the data or the Plot x-range; the
    scale inside is fitted: axis top = k * a nice step with the largest plotted value at 85-95 % of it. P17 (Auto scale
    off) = P1 before auto scale (the fixed axes of the morning of 2026-09-29)."""
    stack_keys = ("ei", "baseline", "profile", "footprint", "radius", "curvature", "plotBandHeight", "plotBandNegative")
    # Band height / position: every region and radius vs curvature. The plot band's own z is keyed by its kind.
    ref = d["P1"]["bands"]
    for case in ("P6", "P11", "P15", "P12", "P16"):
        b = d[case]["bands"]
        band_z = b.get("radius", b.get("curvature"))
        sa = {k: ref.get(k) for k in stack_keys if k not in ("radius", "curvature")}
        sb = {k: b.get(k) for k in stack_keys if k not in ("radius", "curvature")}
        ok = sa == sb and band_z == ref["radius"]
        check(case, "band stacking + band height = P1's (region %s, %s)" % (d[case]["settings"]["plotRegion"], d[case]["settings"]["plot"]),
              "equal, plot band z %s, 150 / 30 mm" % ref["radius"], "equal" if ok else "%s z %s" % (sb, band_z), ok)
    # The end axes and the reference line: identical geometry (relative to the band's zero line) for all six.
    def axes_of(case):
        band = d[case]["settings"]["plot"]
        g = frame_geometry(full[case], d[case], band)
        return {k.split(" ", 1)[1]: v for k, v in g.items() if re.match(r"\w+ (AXIS (TIP|TAIL)|REFERENCE)$", k)}
    a1 = axes_of("P1")
    ax = a1.get("AXIS TIP", [[0, 0, 0, 0]])[0]
    check("P1", "radius band: end axes -30 .. +120 mm around the zero line (150 mm, 20 % below)", "-30 / 120", ax,
          near(ax[2], -30.0, 1e-3) and near(ax[3], 120.0, 1e-3) and len(a1) == 3)
    for case in ("P6", "P11", "P15", "P12", "P16"):
        diff = same_geometry(a1, axes_of(case))
        check(case, "end axes + reference line geometry = P1's", "identical", diff or "identical", not diff)
    # Same data (TAC: the largest radius lies inside every region) -> same ticks and numbers too.
    g0 = frame_geometry(full["P1"], d["P1"], "RADIUS")
    for case in ("P6", "P11", "P15"):
        diff = same_geometry(g0, frame_geometry(full[case], d[case], "RADIUS"))
        check(case, "radius frame incl. ticks / numbers = P1's (same largest radius in region %s)" % d[case]["settings"]["plotRegion"],
              "identical, %d bodies" % len(g0), diff or "identical", not diff and len(g0) > 10)
    g12 = frame_geometry(full["P12"], d["P12"], "CURVATURE")
    diff = same_geometry(g12, frame_geometry(full["P16"], d["P16"], "CURVATURE"))
    check("P16", "curvature frame incl. ticks / numbers = P12's (same largest curvature, FULL vs INFLECTION)",
          "identical, %d bodies" % len(g12), diff or "identical", not diff and len(g12) > 10)

    # P1: the largest plotted radius at 85-95 % of the positive axis; nice step; top = k * step.
    b1 = d["P1"]["bands"]
    plot = span(full["P1"], "RADIUS")
    top_z = plot[3] - b1["radius"]
    frac = top_z / 120.0
    check("P1", "largest plotted radius at 85-95 % of the band's positive height (120 mm)", "0.85..0.95",
          "z %.3f mm = %.4f (plotMax %s m, plotFill %s)" % (top_z, frac, b1.get("plotMax"), b1.get("plotFill")),
          0.85 <= frac <= 0.95 and near(frac, b1.get("plotFill"), 1e-3) and near(b1["plotMax"] * b1["radiusScaleMm"], top_z, 0.01))
    step, top = b1["radiusTickStep"], b1["axisTo"]
    check("P1", "tick step nice (1 2 5 10 20 25 50 m x 10^n), top = k * step, 2 <= k <= 10, scale = 120 mm / top",
          "nice, k * step", "step %s, top %s, %s" % (step, top, b1.get("radiusScale")),
          nice(step, (1, 2, 5, 10, 20, 25, 50)) and abs(top / step - round(top / step)) < 1e-9 and 2 <= round(top / step) <= 10
          and near(b1["radiusScaleMm"], round(120.0 / top, 4), 1e-9) and b1["radiusScale"] == "%g mm per 1 m" % b1["radiusScaleMm"]
          and b1.get("autoScale") is True and d["P1"]["settings"].get("autoScale") is True)
    b12 = d["P12"]["bands"]
    plot12 = span(full["P12"], "CURVATURE")
    frac12 = (plot12[3] - b12["curvature"]) / 120.0
    check("P12", "curvature: largest plotted at 85-95 %, nice step, published scale", "0.85..0.95",
          "%.4f, step %s 1/m, top %s 1/m, %s" % (frac12, b12.get("curvatureTickStep"), b12.get("maxCurvature"), b12.get("curvatureScale")),
          0.85 <= frac12 <= 0.95 and near(frac12, b12.get("plotFill"), 1e-3) and nice(round(b12["curvatureTickStep"] / 0.01, 6), (1, 2, 5, 10, 20, 25, 50)))
    # P16 (curvature FULL): plotted, inside the axis (the tip / tail arcs, -0.98 1/m, are cut at the band's bottom).
    b16 = d["P16"]["bands"]
    plot = span(full["P16"], "CURVATURE")
    lo = b16["curvature"] + b16["axisFrom"] * b16["plotLevelHeight"]
    hi = b16["curvature"] + b16["axisTo"] * b16["plotLevelHeight"]
    fp = {k: v for k, v in d["P1"]["footprint"].items() if isinstance(v, dict)}
    inf = sorted([fp["FB_INFLECTION"]["u"], fp["AB_INFLECTION"]["u"]])
    check("P16", "curvature FULL: plot inside the axis (z) and frame (x)",
          "z %.1f..%.1f, x %s..%s" % (lo, hi, b16["frameFrom"], b16["frameTo"]), plot,
          bool(plot) and plot[2] >= lo - 1e-3 and plot[3] <= hi + 1e-3 and plot[0] >= b16["frameFrom"] - 1e-3 and plot[1] <= b16["frameTo"] + 1e-3
          and plot[0] <= inf[0] + 1 and plot[1] >= inf[1] - 1)

    # EI (P8): band 0..150 mm, top = k * nice step, 150 N*m^2 at 85-95 % of it.
    b8 = d["P8"]["bands"]
    eg = frame_geometry(full["P8"], d["P8"], "EI")
    eax = eg.get("EI AXIS TIP", [[0, 0, 0, 0]])[0]
    eplot = span(full["P8"], "EI")
    efrac = (eplot[2] - b8["ei"]) / 150.0 if eplot else 0
    ok = (near(eax[2], 0.0, 1e-3) and near(eax[3], 150.0, 1e-3) and eplot and eplot[0] >= b8["eiFrameFrom"] - 1e-3
          and eplot[1] <= b8["eiFrameTo"] + 1e-3 and 0.85 <= efrac <= 0.95 and near(efrac, b8.get("eiFill"), 1e-3)
          and nice(b8["eiTickStep"], (10, 25, 50, 100)) and abs(b8["eiAxisMax"] / b8["eiTickStep"] - round(b8["eiAxisMax"] / b8["eiTickStep"])) < 1e-9
          and near(b8["eiPerMm"], round(b8["eiAxisMax"] / 150.0, 6), 1e-9) and b8.get("eiBandHeight") == 150.0)
    check("P8", "EI band 0..150 mm, 150 N*m^2 at 85-95 % of the top, nice step, published scale", "0 / 150, ~0.9",
          "axis %s, fill %.4f, top %s step %s, %s" % (eax, efrac, b8.get("eiAxisMax"), b8.get("eiTickStep"), b8.get("eiScale")), bool(ok))

    # P17 (Auto scale off) = P1 BEFORE auto scale: bands data, frame geometry, radius curve, tables.
    b17 = d["P17"]["bands"]
    ax17 = frame_geometry(full["P17"], d["P17"], "RADIUS").get("RADIUS AXIS TIP", [[0, 0, 0, 0]])[0]
    check("P17", "Auto scale off: radius axis -10 .. +50 m at 10 mm per m (-100 .. +500 mm), ticks every 10 m",
          "-100 / 500, step 10", "%s, step %s, %s" % (ax17, b17.get("plotLevelStep"), b17.get("radiusScale")),
          near(ax17[2], -100.0, 1e-3) and near(ax17[3], 500.0, 1e-3) and b17.get("plotLevelStep") == 10.0 and b17.get("radiusScale") == "10 mm per 1 m"
          and b17.get("autoScale") is False)
    compare("P17 vs P1", d["P1"], d["P17"], 1e-6)
    old = None if not before else {k.split()[0]: v for k, v in before.items()}.get("P1")
    if old:
        ob = old["data"]["bands"]
        diffs = ["%s %s vs %s" % (k, ob[k], b17.get(k)) for k in ob if b17.get(k) != ob[k]]
        check("P17", "bands data = P1 before auto scale (every old key; stacking incl.)", "equal", diffs or "equal", not diffs)
        diff = same_geometry(frame_geometry(old, old["data"], "RADIUS"), frame_geometry(full["P17"], d["P17"], "RADIUS"))
        check("P17", "radius frame geometry (axes, reference, ticks, numbers) = P1 before auto scale", "identical", diff or "identical", not diff)
        po, p17 = span(old, "RADIUS"), span(full["P17"], "RADIUS")
        check("P17", "radius plot extent = P1 before auto scale", po, p17, po and p17 and all(abs(a - b) < 1e-3 for a, b in zip(po, p17)))


def run_checks_repin():
    """2026-09-29: xSection V58 pinned; the exactContacts / distanceToLine workaround removed (live tabs)."""
    tabs = {e["name"]: e["id"] for e in c.list_elements(D, W)}
    base = c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (D, W, tabs["primitive_baseline"]))["contents"]
    ep = c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s" % (D, W, tabs["export_primitive"]))["contents"]
    pins = re.findall(r'import\(path : "f8deedeb1fbd819a8fa20113/([0-9a-f]{24})/', base + ep)
    gone = "exactContacts" not in base and "distanceToLine" not in base
    check("repin", "xSection imports pinned to V58 (live tabs), workaround gone", "2 x V58, no exactContacts",
          "%s, workaround gone %s" % (pins, gone), pins == ["ad6a3958dd2e8873f6dd0b0d"] * 2 and gone)


def run_checks_stations():
    """Station definition's variableName default "" (2026-09-29) applies to NEW features only."""
    tabs = {e["name"]: e["id"] for e in c.list_elements(D, W)}
    specs = c.get("/api/v10/featurestudios/d/%s/w/%s/e/%s/featurespecs" % (D, W, tabs["station_definition"]))["featureSpecs"]
    default = [p["defaultValue"]["value"] for sp in specs for p in sp.get("parameters", []) if p.get("parameterId") == "variableName"]
    check("SD", "Station definition spec: variableName default (new features)", "''", default, default == [""])
    script = 'function(context is Context, queries) { var v; try silent { v = getVariable(context, "stations"); } return v == undefined ? "NONE" : "SET"; }'
    got = json.dumps(c.post("/api/v10/partstudios/d/%s/w/%s/e/%s/featurescript" % (D, W, E), json_data={"script": script}).get("result"))
    st = statuses()
    sd1 = [v for k, v in st.items() if k.startswith("SD1 ")]
    check("SD1", "new Station definition (spec default): INFO, no #stations variable", "INFO, NONE", "%s, %s" % (sd1, got),
          sd1 == ["INFO"] and '"NONE"' in got)
    # Saved features (Part Studio 1): "Stations (test)" keeps variableName "stations", the variable exists and every
    # Station definition / geometry feature still regenerates INFO.
    ps1 = "bb2cddb24faf53d9d043c38e"
    f = c.get("/api/v10/partstudios/d/%s/w/%s/e/%s/features" % (D, W, ps1))
    saved = [p.get("value") for x in f["features"] if x["name"] == "Stations (test)" for p in x["parameters"] if p.get("parameterId") == "variableName"]
    geo = {x["name"]: f["featureStates"][x["featureId"]]["featureStatus"] for x in f["features"]
           if x["featureType"] in ("stationGeometry", "stationDefinition") and x["featureId"] in f["featureStates"]}
    got1 = json.dumps(c.post("/api/v10/partstudios/d/%s/w/%s/e/%s/featurescript" % (D, W, ps1), json_data={"script": script}).get("result"))
    check("SD", "saved 'Stations (test)' keeps variableName 'stations', #stations set; station features INFO", "stations, SET, all INFO",
          "%s, %s, %s" % (saved, "SET" if '"SET"' in got1 else got1, geo),
          saved == ["stations"] and '"SET"' in got1 and geo and all(v == "INFO" for v in geo.values()))


def band_edges(full_case, band):
    """The sampled edges ({p: [[x, z], ...], t0, t1}) of '<prefix> PRIMITIVE <band>' (P1 / P14 only)."""
    return [e for name, groups in full_case.get("shapes", {}).items() if name.endswith(" PRIMITIVE " + band) for g in groups for e in g]


def chain_loop(edges):
    """The edges' sample points chained end to end into one loop (nearest free end next)."""
    import math
    left = [list(e["p"]) for e in edges]
    loop = left.pop(0)
    while left:
        best = None
        for i, e in enumerate(left):
            for rev in (False, True):
                dd = math.dist(e[-1] if rev else e[0], loop[-1])
                if best is None or dd < best[0]:
                    best = (dd, i, rev)
        e = left.pop(best[1])
        loop += (e[::-1] if best[2] else e)[1:]
    return loop


def loop_area(points):
    return abs(sum(x0 * z1 - x1 * z0 for (x0, z0), (x1, z1) in zip(points, points[1:] + points[:1]))) / 2


def crossings(edges, z0):
    """x where the sampled edges cross z = z0 (linear between samples)."""
    out = []
    for e in edges:
        for (x0, a), (x1, b) in zip(e["p"], e["p"][1:]):
            if (a - z0) * (b - z0) <= 0 and a != b:
                out.append(x0 + (x1 - x0) * (z0 - a) / (b - a))
    return out


def corners(edges, min_angle=2.0):
    """[(x, z, angle deg)] at edge junctions (ends within 1 um) whose tangents differ by more than min_angle."""
    import math
    ends = []
    for e in edges:
        ends.append((e["p"][0], e["t0"]))
        ends.append((e["p"][-1], e["t1"]))
    out = []
    for i in range(len(ends)):
        for j in range(i + 1, len(ends)):
            (p, t), (q, u) = ends[i], ends[j]
            if math.dist(p, q) < 1e-3 and j // 2 != i // 2:
                c = abs(t[0] * u[0] + t[1] * u[1]) / (math.hypot(*t) * math.hypot(*u))
                ang = math.degrees(math.acos(min(1.0, c)))
                if ang > min_angle:
                    out.append((round(p[0], 3), round(p[1], 3), round(ang, 2)))
    return out


def run_checks_unwrap(d, full):
    """2026-09-28 unwrap through base sections (the user's tail bite): P14 = P1's volume with the bite cut into its tail."""
    for case in ("P1", "P14"):
        un = d[case]["footprint"].get("unwrap", {})
        check(case, "unwrap is an isometry: unwrapped length = base periphery length (mm)", "|diff| <= 0.01",
              "%s vs %s" % (un.get("unwrappedLength"), un.get("sourceLength")),
              near(un.get("unwrappedLength"), un.get("sourceLength"), 0.01))
        fp = band_edges(full[case], "FOOTPRINT")
        area = loop_area(chain_loop(fp)) if fp else 0.0
        base = un.get("baseArea")
        check(case, "unwrapped footprint area = base faces' area (mm^2)", "rel <= 1e-4", "%.1f vs %s" % (area, base),
              isinstance(base, float) and abs(area - base) <= 1e-4 * base)
        k = rows(d[case], "keyLocations")
        z0 = d[case]["bands"]["footprint"]
        u_of = lambda key: 885.0 + k[key]["s"] - k["MRS"]["s"]
        xs = crossings(fp, z0)
        check(case, "footprint on the centreline ends at u(TAIL) / u(TIP) (the bite's true depth on P14)", "%.3f / %.3f" % (u_of("TAIL"), u_of("TIP")),
              "%.3f / %.3f" % (min(xs), max(xs)) if xs else "no crossing",
              bool(xs) and near(min(xs), u_of("TAIL"), 0.01) and near(max(xs), u_of("TIP"), 0.01))
    c1 = corners(band_edges(full["P1"], "FOOTPRINT"))
    c14 = corners(band_edges(full["P14"], "FOOTPRINT"))
    check("P14", "footprint corners: P1's + the bite's two, at the tail (u < 60); no kinks elsewhere", "%d + 2" % len(c1),
          "P1 %s / P14 %s" % (c1, c14), len(c14) == len(c1) + 2 and all(c[0] < 60 for c in c14 if c not in c1))
    # Radius band: beyond the bite = P1 (band offset), and continuous through the bite region.
    r1 = band_edges(full["P1"], "RADIUS")
    r14 = band_edges(full["P14"], "RADIUS")
    dz = d["P14"]["bands"]["radius"] - d["P1"]["bands"]["radius"]
    worst = 0.0
    for e in r14:
        for x, z in e["p"]:
            if x < 50.0:
                continue
            zs = [a[1] + (b[1] - a[1]) * (x - a[0]) / (b[0] - a[0]) for f in r1 for a, b in zip(f["p"], f["p"][1:])
                  if min(a[0], b[0]) <= x <= max(a[0], b[0]) and a[0] != b[0]]
            if zs:
                worst = max(worst, min(abs(z - dz - v) for v in zs))
    # 0.03 mm of plot height (3 mm of radius): the plots are fitted through 5 mm samples that start at different points
    # (P14's tail edge starts at the bite corner), ~0.016 mm apart on TAC's tail curve.
    check("P14", "radius band beyond the bite (u >= 50) = P1's (mm of plot height; fit resolution)", "<= 0.03", round(worst, 5), worst <= 0.03)
    tail = [e for e in r14 if min(p[0] for p in e["p"]) < 50.0]
    steps = [abs(b[1] - a[1]) for e in tail for a, b in zip(e["p"], e["p"][1:])]
    check("P14", "radius runs through the bite region: no spikes (largest step between 101 samples, mm)", "< 1",
          round(max(steps), 4) if steps else "no run", bool(steps) and max(steps) < 1.0)
    # Inside the RSL the bite changes nothing: data table and radius metadata = P3 (same baseline and footprint source).
    # s is compared from MRS: s is zero at x = 0, which lies past the bitten bottom wire's end (straight extension).
    bad = []
    mrs3 = [r["s"] for r in d["P3"]["data"] if near(r["x"], 885.0, 1e-3)][0]
    mrs14 = [r["s"] for r in d["P14"]["data"] if near(r["x"], 885.0, 1e-3)][0]
    for a, b in zip(d["P3"]["data"], d["P14"]["data"]):
        for field, va in a.items():
            vb = b.get(field)
            if field == "s":
                va, vb = va - mrs3, vb - mrs14
            if isinstance(va, float) and not near(va, vb, 0.01):
                bad.append("%s %s vs %s" % (field, va, vb))
    for key in ("averageRadius", "naturalRadiusWidest", "naturalRadiusInflection", "taperAngleWidest", "rsl"):
        if not near(meta(d["P3"], key), meta(d["P14"], key), 1e-4):
            bad.append(key)
    check("P14", "inside the RSL = P3: data rows (0.01 mm, s from MRS) and radius metadata", "equal, %d rows" % len(d["P3"]["data"]),
          "%d rows, %d differences %s" % (len(d["P14"]["data"]), len(bad), bad[:3]), not bad and len(d["P14"]["data"]) == len(d["P3"]["data"]))


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
