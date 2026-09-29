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
        for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
        {
            const name = getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME });
            counts[name] = (counts[name] == undefined ? 0 : counts[name]) + 1;
            const a = getProperty(context, { "entity" : m, "propertyType" : PropertyType.APPEARANCE });
            colours[name] = round(a.red * 100) / 100 ~ "/" ~ round(a.green * 100) / 100 ~ "/" ~ round(a.blue * 100) / 100;
        }
        var members = [];
        for (var entry in counts)
        {
            members = append(members, entry.key);
        }
        out = append(out, "JSON " ~ toString({ "data" : data, "members" : members, "counts" : counts, "colours" : colours,
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
    """Every number of primitive b equals primitive a's (x -> sign * x + dx, z -> z + dz); texts equal."""
    bad = []
    for section in ("scaleFactors", "metadata", "keyLocations", "baseline"):
        ra, rb = rows(a, section), rows(b, section)
        for key, row in ra.items():
            for field, va in row.items():
                vb = rb.get(key, {}).get(field)
                if field == "x" and isinstance(va, float):
                    va = sign * va + dx
                if field == "z" and section == "keyLocations" and isinstance(va, float):
                    va = va + dz
                if isinstance(va, float):
                    if not near(va, vb, tol):
                        bad.append("%s.%s.%s %s vs %s" % (section, key, field, va, vb))
                elif field != "note" and va != vb:
                    bad.append("%s.%s.%s %r vs %r" % (section, key, field, va, vb))
    if len(a["data"]) != len(b["data"]):
        bad.append("data rows %d vs %d" % (len(a["data"]), len(b["data"])))
    for i, (ra, rb) in enumerate(zip(a["data"], b["data"])):
        for field, va in ra.items():
            vb = rb.get(field)
            if field == "x" and isinstance(va, float):
                va = sign * va + dx
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
        if name[:2] in ("P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8", "P9"):
            check(name.split()[0], "feature status", "INFO", status, status == "INFO")
    d = {k.split()[0]: v["data"] for k, v in prims.items()}
    full = {k.split()[0]: v for k, v in prims.items()}

    p1 = d["P1"]
    check("P1", "average radius (inflection) m", "~17.05", meta(p1, "averageRadius"), near(meta(p1, "averageRadius"), 17.05, 0.01))
    check("P1", "natural radius widest m", "~17.48", meta(p1, "naturalRadiusWidest"), near(meta(p1, "naturalRadiusWidest"), 17.48, 0.01))
    check("P1", "natural radius inflection m", "~16.18", meta(p1, "naturalRadiusInflection"), near(meta(p1, "naturalRadiusInflection"), 16.18, 0.01))
    check("P1", "RSL mm", 1480, meta(p1, "rsl"), near(meta(p1, "rsl"), 1480.0, 0.01))
    k1 = rows(p1, "keyLocations")
    for key, x, s in (("FCP", 1625, 740), ("ACP", 145, -740), ("MRS", 885, 0), ("XS1", 1255, 370), ("XS2", 515, -370)):
        check("P1", "%s x / s mm" % key, "%s / %s" % (x, s), "%s / %s" % (k1[key]["x"], k1[key]["s"]),
              near(k1[key]["x"], float(x), 1e-3) and near(k1[key]["s"], float(s), 1e-3))
    check("P1", "TIP / TAIL s mm", "905 / -885", "%s / %s" % (k1["TIP"]["s"], k1["TAIL"]["s"]),
          near(k1["TIP"]["s"], 905.0, 0.01) and near(k1["TAIL"]["s"], -885.0, 0.01))
    sf = rows(p1, "scaleFactors")
    check("P1", "running surface top/bottom %", "slightly > 100", sf["runningSurface"]["ratio"], 100.0 < sf["runningSurface"]["ratio"] < 100.1)
    check("P1", "tip top/bottom %", "< 100", sf["tip"]["ratio"], sf["tip"]["ratio"] < 100.0)
    check("P1", "data rows (N 21 incl. XS1/MRS/XS2)", 21, len(p1["data"]), len(p1["data"]) == 21)
    check("P1", "ski_thck at FCP mm (X-Sect SPA)", 6.75, p1["data"][0]["skiThck"], near(p1["data"][0]["skiThck"], 6.75, 0.01))
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

    p6 = d["P6"]
    check("P6", "average radius FCP-ACP m", "valid, differs from P1", meta(p6, "averageRadius"),
          isinstance(meta(p6, "averageRadius"), float) and abs(meta(p6, "averageRadius") - meta(p1, "averageRadius")) > 0.01)

    width = max(len(r[1]) for r in RESULTS)
    fails = 0
    for case, what, expected, actual, ok in RESULTS:
        fails += 0 if ok else 1
        print("%-4s %-9s %-*s expected %-22s actual %s" % ("ok" if ok else "FAIL", case, width, what, expected, actual))
    print("%d / %d pass" % (len(RESULTS) - fails, len(RESULTS)))
    return fails


if __name__ == "__main__":
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
