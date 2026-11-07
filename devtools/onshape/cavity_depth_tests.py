"""Build + check the Export primitive's cavity depth profile and the scaled baseline / profile bands (2026-11-06) in the
'Primitive tests' studio. ONE active Export primitive is needed as the base (P8, the studio's `--keep` case).

  Fixture   a sketch polyline on Front, 5 mm BELOW the top profile of the base case, x 300..1500 mm
            (the top wire is probed in the base case's composite, converted back from the band's scale and height).
  P20       a copy of the base case + that wire as the cavity depth profile
  Checks    cavity depth ~5 mm (4.8 .. 5.2: the fixture is a 50 mm polyline and the depth is measured along the bottom
            wire's normal) in the data rows with 300 <= x <= 1500, empty outside; no cavityDepth in the base case's rows;
            the PROFILE CAVITY body exists; baseline / profile bands carry a z scale, a REFERENCE line and mm ticks; the
            base case is unchanged otherwise (same rows / thck); every feature OK or INFO.

Writes 4 features-worth of calls (probe, sketch, P20, read-back, statuses: about 6 REST calls).
usage (repo root): PYTHONPATH=. FS_SYNC_TIMEOUT=300 python devtools/onshape/cavity_depth_tests.py
"""
import copy
import json
import re
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, cached_tree, q, b, en, num, s

c = OnshapeClient()
D, W = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761"
E = [e["id"] for e in c.list_elements(D, W) if e["name"] == "Primitive tests"][0]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
CACHE = {}
tree = cached_tree(BASE, CACHE, c)
p1 = [x for x in tree["features"] if x["name"].startswith("P8 ")][0]
print("P1:", p1["name"][:70], tree["featureStates"].get(p1["featureId"], {}).get("featureStatus"))


def strings(result):
    return [s for s in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(result)) if not s.startswith("BTFSValue")]


# 1. Probe: the top wire of P1's profile (local = world here: datum on the world origin), x / z in mm.
probe = r'''function(context is Context, queries)
{
    var out = [];
    for (var body in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "publishPrimitive")))
    {
        const data = getAttribute(context, { "entity" : body, "name" : "publishPrimitive" });
        if (data.prefix != "P8 TAC EI")
            continue;
        out = append(out, "ZREF " ~ data.bands.profile ~ " " ~ data.bands.profileZScale);
        for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
        {
            const name = getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME });
            if (!match(name, "P8 TAC EI PRIMITIVE PROFILE (TOP|BOTTOM|TIP END|TAIL END)").hasMatch)
                continue;
            for (var e in evaluateQuery(context, qOwnedByBody(m, EntityType.EDGE)))
            {
                var ts = [];
                for (var i = 0; i <= 60; i += 1)
                    ts = append(ts, i / 60);
                out = append(out, "EDGE " ~ replace(name, " ", "_"));
                for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : ts }))
                    out = append(out, roundToPrecision(tl.origin[0] / millimeter, 4) ~ " " ~ roundToPrecision(tl.origin[2] / millimeter, 4));
            }
        }
    }
    return out;
}
'''
r = c.post(BASE + "/featurescript", json_data={"script": probe})
zref, zk = [float(v) for v in [s for s in strings(r.get("result")) if s.startswith("ZREF ")][0].split()[1:]]
print("band reference z %.3f, z scale %.4f" % (zref, zk))
# Every probed edge as a polyline of true (x, z) mm (the band's scale and height undone); the TOP edges' samples give top_z.
edges_xz = []
for s_ in strings(r.get("result")):
    if s_.startswith("EDGE "):
        edges_xz.append({"name": s_[5:], "p": []})
    elif edges_xz and re.match(r"^-?[0-9.]+ -?[0-9.]+$", s_):
        x_, z_ = (float(v) for v in s_.split())
        edges_xz[-1]["p"].append((x_, (z_ - zref) / zk))
pts = sorted(set(p for e_ in edges_xz if e_["name"].endswith("PROFILE_TOP") for p in e_["p"]))
if len(pts) < 20:
    print("probe failed", [n.get("message") for n in (r.get("notices") or [])][:5], len(pts))
    sys.exit(1)
print("probe: %d top samples, x %.1f .. %.1f" % (len(pts), pts[0][0], pts[-1][0]))


def top_z(x):
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if x0 <= x <= x1 and x1 > x0:
            return z0 + (z1 - z0) * (x - x0) / (x1 - x0)
    raise SystemExit("x %s outside the probe" % x)


# 2. The fixture: a polyline on Front (sketch x = world X, sketch y = world Z), 5 mm below the top, x 300..1500.
xs = list(range(300, 1501, 50))
cd = [(x, top_z(x) - 5.0) for x in xs]
entities = []
for i, ((xa, za), (xb, zb)) in enumerate(zip(cd, cd[1:])):
    xa, za, xb, zb = (v / 1000.0 for v in (xa, za, xb, zb))
    L = ((xb - xa) ** 2 + (zb - za) ** 2) ** 0.5
    entities.append({"btType": "BTMSketchCurveSegment-155", "entityId": "cd%d" % i, "startPointId": "cd%d.start" % i, "endPointId": "cd%d.end" % i,
                     "startParam": 0.0, "endParam": L, "isConstruction": False,
                     "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": xa, "pntY": za, "dirX": (xb - xa) / L, "dirY": (zb - za) / L}})
def cd_sketch(name, plane_query):
    r_ = write_feature(BASE, {"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                              "parameters": [q("sketchPlane", plane_query)], "entities": entities, "constraints": []}, CACHE, client=c)
    print("%-52s %s" % (name, r_.get("featureState", {}).get("featureStatus")))
    return r_["feature"]["featureId"]


sk_id = cd_sketch("CD fixture (5 mm below the top, x 300..1500)", 'qCreatedBy(makeId("Front"), EntityType.FACE)')
# The same wire on a plane 10 mm off Front: it is NOT in the datum XZ plane, so the feature must project it (P21).
pl = write_feature(BASE, {"btType": "BTMFeature-134", "featureType": "cPlane", "name": "CD plane (Front + 10 mm)", "parameters": [
    q("entities", 'qCreatedBy(makeId("Front"), EntityType.FACE)'), en("cplaneType", "CPlaneType", "OFFSET"), num("offset", "10 mm"),
    b("oppositeDirection", False), b("flipNormal", False)]}, CACHE, client=c)
sk2_id = cd_sketch("CD fixture off the plane (Front + 10 mm)", 'qCreatedBy(makeId("%s"), EntityType.FACE)' % pl["feature"]["featureId"])


def strip(o):
    if isinstance(o, dict):
        o.pop("nodeId", None)
        for v in o.values():
            strip(v)
    elif isinstance(o, list):
        for v in o:
            strip(v)


def wires_of(sketch_id):
    return 'qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE)' % sketch_id


def case_with_cavity(name, prefix, sketch_id, extra=()):
    """A copy of the base case (P8, the only active case) with the cavity profile = the wire of `sketch_id` (+ `extra` parameters)."""
    f_ = copy.deepcopy(p1)
    f_.pop("featureId", None)
    f_["name"] = name
    strip(f_)
    replaced = ("cavityProfile", "prefix", "queryVariable") + tuple(p["parameterId"] for p in extra)
    f_["parameters"] = [p for p in f_["parameters"] if p["parameterId"] not in replaced] + [
        q("cavityProfile", wires_of(sketch_id)), s("prefix", prefix), s("queryVariable", "")] + list(extra)
    r_ = write_feature(BASE, f_, CACHE, client=c)
    print("%-52s %s" % (prefix, r_.get("featureState", {}).get("featureStatus")))


case_with_cavity("P20 TAC as P8 + cavity depth profile 5 mm below the top, x 300..1500 -> cavity depth ~5 mm there, empty outside, scaled baseline / profile bands, INFO",
                 "P20 TAC", sk_id)
case_with_cavity("P21 TAC as P20 with the cavity wire on a plane 10 mm off Front -> projected: same depths as P20, INFO", "P21 TAC", sk2_id)

# P22: the profile from WIRES (Profile source) = the base case's own four profile wires rebuilt as a closed polyline loop
# (each probed edge, 60 chords); with the cavity wire it must reproduce P20's thck and depths (the polyline is within ~0.01 mm).
loop = []
for k_, e_ in enumerate(edges_xz):
    prev = None
    for j_, (xp, zp) in enumerate(e_["p"]):
        if prev is not None and abs(xp - prev[0]) + abs(zp - prev[1]) > 1e-6:
            xa, za, xb, zb = (v / 1000.0 for v in (prev[0], prev[1], xp, zp))
            L_ = ((xb - xa) ** 2 + (zb - za) ** 2) ** 0.5
            loop.append({"btType": "BTMSketchCurveSegment-155", "entityId": "pf%d_%d" % (k_, j_), "startPointId": "pf%d_%d.start" % (k_, j_),
                         "endPointId": "pf%d_%d.end" % (k_, j_), "startParam": 0.0, "endParam": L_, "isConstruction": False,
                         "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": xa, "pntY": za, "dirX": (xb - xa) / L_, "dirY": (zb - za) / L_}})
        prev = (xp, zp)
sk3 = write_feature(BASE, {"btType": "BTMSketch-151", "featureType": "newSketch", "name": "Profile wires fixture (closed polyline of the base profile)",
                           "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Front"), EntityType.FACE)')], "entities": loop, "constraints": []},
                    CACHE, client=c)
print("%-52s %s (%d chords)" % ("profile wires sketch", sk3.get("featureState", {}).get("featureStatus"), len(loop)))
ns_ = [p for p in p1["parameters"] if p["parameterId"] == "baselineFrom"][0].get("namespace", "")
case_with_cavity("P22 TAC as P20 with the profile from picked wires (Profile source) -> P20's thck and cavity depths, INFO", "P22 TAC", sk_id, extra=[
    en("profileFrom", "PrimitiveSource", "INPUT", ns_), q("profileWires", wires_of(sk3["feature"]["featureId"]))])

# 4. Read back P1 and P20.
read = r'''function(context is Context, queries)
{
    var out = [];
    for (var body in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "publishPrimitive")))
    {
        const data = getAttribute(context, { "entity" : body, "name" : "publishPrimitive" });
        if (data.prefix != "P8 TAC EI" && data.prefix != "P20 TAC" && data.prefix != "P21 TAC" && data.prefix != "P22 TAC")
            continue;
        out = append(out, "CASE " ~ data.prefix ~ " rows " ~ size(data.data) ~ " cavitySetting " ~ data.settings.cavityProfile);
        for (var key in ["baselineZScale", "baselineTickStep", "baselineAxisFrom", "baselineAxisTo", "baselineBandHeight",
                         "profileZScale", "profileTickStep", "profileAxisFrom", "profileAxisTo", "profileBandHeight", "baseline", "profile"])
            out = append(out, "BAND " ~ key ~ " " ~ toString(data.bands[key]));
        for (var r in data.data)
            out = append(out, "ROW x " ~ roundToPrecision(r.x, 1) ~ " thck " ~ toString(r.skiThck) ~ " cd " ~ (r.cavityDepth == undefined ? "none" : "[" ~ toString(r.cavityDepth) ~ "]"));
        for (var m in evaluateQuery(context, qContainedInCompositeParts(body)))
        {
            const name = getProperty(context, { "entity" : m, "propertyType" : PropertyType.NAME });
            if (match(name, ".* PRIMITIVE (PROFILE|BASELINE)( BOTTOM| TOP| CAVITY| REFERENCE| AXIS .*| TICK .*| LABEL .*)?").hasMatch)
            {
                const bb = evBox3d(context, { "topology" : m, "tight" : true });
                out = append(out, "BODY " ~ name ~ " x " ~ roundToPrecision(bb.minCorner[0] / millimeter, 2) ~ ".." ~ roundToPrecision(bb.maxCorner[0] / millimeter, 2)
                    ~ " z " ~ roundToPrecision(bb.minCorner[2] / millimeter, 3) ~ ".." ~ roundToPrecision(bb.maxCorner[2] / millimeter, 3));
            }
        }
    }
    return out;
}
'''
r = c.post(BASE + "/featurescript", json_data={"script": read})
for n in (r.get("notices") or []):
    if n.get("level") == "ERROR":
        print("NOTICE ERROR", n.get("message"))
lines = strings(r.get("result"))

# ---- checks ----
FAILED = []


def check(label, ok, detail=""):
    print("%-4s %s %s" % ("PASS" if ok else "FAIL", label, detail))
    if not ok:
        FAILED.append(label)


cases = {}
current = None
for s_ in lines:
    m = re.match(r"CASE (.+?) rows (\d+) cavitySetting (\w+)", s_)
    if m:
        current = cases.setdefault(m.group(1), {"rows": [], "bands": {}, "bodies": [], "setting": m.group(3) == "true", "n": int(m.group(2))})
    elif current is not None and s_.startswith("ROW "):
        mm = re.match(r"ROW x (\S+) thck (\S+) cd (none|\[(.*)\])", s_)
        current["rows"].append((float(mm.group(1)), mm.group(2), None if mm.group(3) == "none" else mm.group(4)))
    elif current is not None and s_.startswith("BAND "):
        _, key, val = s_.split(" ", 2)
        current["bands"][key] = val
    elif current is not None and s_.startswith("BODY "):
        current["bodies"].append(s_[5:])
base, new = cases.get("P8 TAC EI"), cases.get("P20 TAC")
check("both cases regenerate", base is not None and new is not None)
if base and new:
    check("base case: no cavity depth column in its rows", all(cd is None for _, _, cd in base["rows"]))
    check("P20: the cavity setting is stored", new["setting"] and not base["setting"])
    check("same rows / thck as the base case", [(x, t) for x, t, _ in base["rows"]] == [(x, t) for x, t, _ in new["rows"]])
    inside = [(x, cd) for x, _, cd in new["rows"] if 300 <= x <= 1500]
    outside = [(x, cd) for x, _, cd in new["rows"] if x < 299 or x > 1501]
    values = [float(cd) for _, cd in inside if cd not in ("", None)]
    check("P20: %d rows inside x 300..1500 all have a depth" % len(inside), len(values) == len(inside) and len(inside) > 20)
    check("P20: depth 4.8 .. 5.2 mm inside (fixture is 5 mm below the top)", bool(values) and all(4.8 <= v <= 5.2 for v in values),
          "min %.3f max %.3f" % (min(values), max(values)) if values else "")
    check("P20: rows outside the wire are empty", len(outside) > 3 and all(cd == "" for _, cd in outside))
    check("P20: PROFILE CAVITY body between x 300 and 1500", any(" PROFILE CAVITY x 300.0" in b or " PROFILE CAVITY x 300..1500" in b for b in new["bodies"]), "")
    for case in (base, new):
        for band in ("baseline", "profile"):
            check("%s band: z scale > 1, reference line and ticks" % band,
                  float(case["bands"].get(band + "ZScale", "0")) > 1 and any(b.startswith("%s PRIMITIVE %s REFERENCE" % (("P20 TAC" if case is new else "P8 TAC EI"), band.upper())) for b in case["bodies"])
                  and float(case["bands"].get(band + "TickStep", "0")) > 0)
    check("profile band scale unchanged by a cavity inside the profile's range", new["bands"].get("profileZScale") == base["bands"].get("profileZScale"))
    off = cases.get("P21 TAC")
    check("P21 (wire 10 mm off the plane) regenerates", off is not None)
    if off:
        pairs = [(a, b_) for (_, _, a), (_, _, b_) in zip(new["rows"], off["rows"])]
        same = all((a == "" and b_ == "") or (a not in ("", None) and b_ not in ("", None) and abs(float(a) - float(b_)) <= 0.02) for a, b_ in pairs)
        check("P21: projected wire gives P20's depths (within 0.02 mm) and the same empty rows", same and len(pairs) == len(new["rows"]),
              "max diff %.4f" % max([abs(float(a) - float(b_)) for a, b_ in pairs if a not in ("", None) and b_ not in ("", None)] or [0]))
        check("P21: PROFILE CAVITY body is in the plane (y 0): drawn in the profile band", any(" PROFILE CAVITY" in b_ for b_ in off["bodies"]))
    wires = cases.get("P22 TAC")
    check("P22 (profile from picked wires) regenerates", wires is not None)
    if wires:
        dt = [abs(float(t1) - float(t2)) for (_, t1, _), (_, t2, _) in zip(new["rows"], wires["rows"]) if t1 != "n/a" and t2 != "n/a"]
        check("P22: same rows and thck as P20 (within 0.05 mm)", len(wires["rows"]) == len(new["rows"]) and bool(dt) and max(dt) <= 0.05, "max diff %.4f over %d rows" % (max(dt or [0]), len(dt)))
        dd = [abs(float(a) - float(b_)) for (_, _, a), (_, _, b_) in zip(new["rows"], wires["rows"]) if a not in ("", None) and b_ not in ("", None)]
        check("P22: same cavity depths as P20 (within 0.05 mm)", bool(dd) and max(dd) <= 0.05, "max diff %.4f over %d rows" % (max(dd or [0]), len(dd)))

f = c.get(BASE + "/features")
names = {x["featureId"]: x["name"] for x in f["features"]}
bad = {names[k]: v["featureStatus"] for k, v in f["featureStates"].items() if k in names and v["featureStatus"] not in ("OK", "INFO")}
check("every feature OK or INFO", not bad, str(bad) if bad else "")
print("%d check(s) failed" % len(FAILED) if FAILED else "all passed")
sys.exit(1 if FAILED else 0)


