"""THROWAWAY spike (2026-09-29): outside references inside a Case pattern. Builds the
"Case pattern outside-ref spikes" studio in the case_pattern document (ours alone; rebuilt every run)
using the helpers of build_case_pattern_tests.py and the spike tab spike_outside.fs, then prints
feature statuses and the probe log.

usage (repo root): PYTHONPATH=. python devtools/onshape/spike_case_outside.py
"""
import json
import re

src = open("devtools/onshape/build_case_pattern_tests.py").read()
src = src[:src.index("# ---- clean slate ----")].replace('STUDIO = "Case pattern tests"', 'STUDIO = "Case pattern outside-ref spikes"')
exec(src)

SP = [e for e in elements() if e["elementType"] == "FEATURESTUDIO" and e["name"] == "spike_outside"][0]
SNS = "e%s::m%s" % (SP["id"], SP["microversionId"])

for f in reversed(c.get(f"{BASE}/features")["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")


def tower(name, x, y, h):
    sk = sketch(name + " (sketch)", polygon("p", [(x - 10, y - 10), (x + 10, y - 10), (x + 10, y + 10), (x - 10, y + 10)]))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"), num("depth", h)])


def probe(label, ref_expr):
    return feature(label, "outsideProbe", [s("label", label), sel("ref", ref_expr)], SNS)


def up_to(name, entities, face_expr):
    return feature(name, "extrude", [en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
                                     entities, en("endBound", "BoundingType", "UP_TO_SURFACE"), sel("endBoundEntityFace", face_expr)])


def row(tag, y):
    A, B, C = blocks(tag, y)
    T = tower(tag + " tower (outside, 60 mm)", 600, y, "60 mm")
    corner = 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(590, %d, 60) * millimeter)' % (T, y - 10)
    mc = feature(tag + " outside MC at tower corner", "spikeMateConnector",
                 [sel("at", corner), sel("owner", 'qCreatedBy(makeId("%s"), EntityType.BODY)' % T)], SNS)
    return A, B, C, T, corner, 'qCreatedBy(makeId("%s"), EntityType.BODY)' % mc


def body(tag, top_param, T, corner, mc):
    return [probe(tag + " probe face", top(T)), probe(tag + " probe vertex", corner), probe(tag + " probe MC", mc),
            up_to(tag + " boss up to tower top (clicked outside)", top_param, top(T))]


# O1 control: no pattern at all.
A, B, C, T, corner, mc = row("O1", 0)
body("O1", q("entities", top(A)), T, corner, mc)

# O2 Case pattern (as built: Case pattern -> Close case -> frame on its own id).
A, B, C, T, corner, mc = row("O2", 150)
d2 = define_case("O2 Define case A: #top2", "A", [("top2", top(A))])
f2 = body("O2", qv("entities", "top2"), T, corner, mc)
c2 = close_case("O2 Close case", d2, f2, name_parts=False)
case_pattern("O2 Case pattern B", c2, [case_row("B", ["top2"], [top(B)])])

# O3 native Linear pattern, Reapply features (+X 200 mm, 2 instances).
A, B, C, T, corner, mc = row("O3", 300)
f3 = body("O3", q("entities", top(A)), T, corner, mc)
feature("O3 native Linear pattern, reapply features, +X 200 mm", "linearPattern", [
    en("patternType", "PatternType", "FEATURE"), flist("instanceFunction", f3),
    q("directionOne", 'qCreatedBy(makeId("Right"), EntityType.FACE)'), num("distance", "200 mm"),
    num("instanceCount", "2", integer=True), b("fullFeaturePattern", True)])

# O4 frame pushed by the top-level feature (std Pattern's shape), one query variable rebound.
A, B, C, T, corner, mc = row("O4", 450)
d4 = define_case("O4 Define case A: #top4 (binds case 1 only)", "A", [("top4", top(A))])
f4 = body("O4", qv("entities", "top4"), T, corner, mc)
feature("O4 Top-frame replay, #top4 = B's top", "topFrameReplay",
        [flist("features", f4), s("bindName", "top4"), sel("bindQuery", top(B)), s("caseName", "B")], SNS)

# N1-N3: where the frame lives. Outer replay -> Inner replay (FeatureList) -> body, like Case pattern -> Close case.
# Body: probes, boss up to the clicked tower top, rim fillet on #rim (edits outside geometry), QV created by boss,
# boss-edge fillet via that QV (in-list remap).
for tag, y, outer, mode in [("N1 outer no frame, inner own frame (= Case pattern today)", 600, False, "own"),
                            ("N2 outer frame, inner none", 750, True, "none"),
                            ("N3 outer frame, inner own (nested)", 900, True, "own"),
                            ("N4 outer frame, inner own + retry outside own frame", 1050, True, "retry"),
                            ("N5 outer no frame, inner own + retry (= Case pattern today, full)", 1200, False, "retry"),
                            ("N6 outer frame on SIBLING id, inner own + retry", 1350, True, "retry6")]:
    t = tag.split()[0]
    A, B, C, T, corner, mc = row(t, y)
    dn = define_case("%s Define case A: #top%s #rim%s" % (t, t, t), "A", [("top" + t, top(A)), ("rim" + t, rim(A))])
    fb = [probe(t + " probe face", top(T)), probe(t + " probe MC", mc),
          up_to(t + " boss up to tower top (clicked outside)", qv("entities", "top" + t), top(T))]
    fb.append(feature(t + " rim fillet #rim 2 mm (outside geometry edit)", "fillet", [qv("entities", "rim" + t), num("radius", "2 mm")]))
    fb.append(feature(t + " #bossE = edges created by boss (native QV)", "queryVariable", native_qv("bossE" + t, [fb[2]], "EDGE")))
    fb.append(feature(t + " boss fillet #bossE 1 mm (in-list remap)", "fillet", [qv("entities", "bossE" + t), num("radius", "1 mm")]))
    inner = feature(t + " Inner replay", "innerReplay", [flist("features", fb)], SNS)
    feature("%s Outer replay -> case B" % tag, "outerReplay", [flist("inner", [inner]), s("bindName", "top" + t), sel("bindQuery", top(B)),
            s("bindName2", "rim" + t), sel("bindQuery2", rim(B)), b("outerFrame", outer), s("innerMode", mode.rstrip("6")), b("frameElsewhere", mode.endswith("6"))], SNS)

feats = c.get(f"{BASE}/features")
print("\nSTATUS")
for f in feats["features"]:
    st = feats["featureStates"][f["featureId"]]["featureStatus"]
    print("  %-8s %s" % (st, f["name"]))
script = ('function(context is Context, queries) { var out = try silent(getVariable(context, "-probeLog")); '
          'var bodies = []; for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.SOLID))) '
          '{ const bb = evBox3d(context, { "topology" : b }); const fc = qOwnedByBody(b, EntityType.FACE); bodies = append(bodies, size(evaluateQuery(context, qUnion([qGeometry(fc, GeometryType.CYLINDER), qGeometry(fc, GeometryType.TORUS)]))) ~ "R " ~ roundToPrecision(bb.minCorner[0] / millimeter, 1) ~ "," '
          '~ roundToPrecision(bb.minCorner[1] / millimeter, 1) ~ " z" ~ roundToPrecision(bb.minCorner[2] / millimeter, 1) ~ ".." '
          '~ roundToPrecision(bb.maxCorner[2] / millimeter, 1)); } return toString({ "log" : out, "bodies" : bodies }); }')
r = c.post(f"{BASE}/featurescript", json_data={"script": script})
txt = json.dumps(r.get("result"))
m = re.search(r'"value":\s*"(\{.*\})"', txt)
print("\nRESULT")
print((m.group(1) if m else txt).replace("\n", "\n"))
