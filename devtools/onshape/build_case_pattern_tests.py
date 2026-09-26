"""Build the Case Pattern v2 tests (Define case / Close case / Case pattern) as real features in the
"Case pattern tests" Part Studio of the case_pattern document. The studio is ours alone: every run
deletes its features and rebuilds them. Check with check_case_pattern_tests.py.

Every selection is stored the way a click stores it (deterministic ids read from the tree at the
moment the feature is added), so the UI shows "Face of Part 1", not "Unknown". Editing logic does not
run on REST inserts, so the case rows are laid out here exactly as casePatternEditLogic lays them out.

Fixture: per test a row of three blocks, 20 mm tall on Top, at y = 100 mm * row:
  A  rectangle 60 x 40 at x 0      B  rectangle 90 x 30 at x 200      C  pentagon r 35 at x 400

T1  Define case A: #top, #rim; values #bossH (length 15 mm), #edgeR (number 3)
    Boss extrude #top #bossH new; Rim fillet #rim 2 mm (outside the list); #bossEdges QV created by Boss;
    Boss edges fillet #bossEdges #edgeR mm; #bossFaces QV created by Boss. Close case, output bossFaces
    (not bossEdges: the fillet consumes those edges before the case closes).
    Case pattern B (25 mm, 3), Case pattern C (8 mm, 2)
    -> Boss_B 25 mm, Boss_C 8 mm tall, edges filleted; #A_/#B_/#C_bossFaces on each case's boss
T2  Move face as the FIRST repeated feature, ONE Case pattern with two cases B and C
    -> B's +X face and C's 54 deg face offset 5 mm
T3  In-list reference by click: expected ERROR (clicks are not remapped onto the case)
T6  Boolean #pin (typed expression): Post extrude #cap 5 mm; Pin extrude #cap 12 mm, suppressed unless #pin.
    A true, B false, C true -> pins under A and C only
T7  Chained cases: Stud extrude #face 10 mm new, output stud. Case pattern B on block B's top; then
    Case pattern D whose #face is the top of B's stud (geometry made after the Define case)
    -> D's stud on top of B's stud (z 30..40); #D_stud exists

usage (repo root): PYTHONPATH=. python devtools/onshape/build_case_pattern_tests.py
"""
import json
import math
import re

from sync.core.client import OnshapeClient

c = OnshapeClient()
DOC = json.load(open("case_pattern/.document.json"))
D, W = DOC["document_id"], DOC["workspace_id"]
STUDIO = "Case pattern tests"


def elements():
    return c.list_elements(D, W)


studios = {e["name"]: e["id"] for e in elements() if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    r = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})
    studios[STUDIO] = r["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
TAB = [e for e in elements() if e["elementType"] == "FEATURESTUDIO" and e["name"] == "case_pattern"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
MAX_INPUTS, MAX_VALUES = 8, 6


# ---- parameters ----
def q(pid, *exprs):
    """A selection given as FeatureScript query expressions (shows as "Unknown" in the UI)."""
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def eval_ids(expr):
    """Deterministic ids of what `expr` resolves to at the end of the current tree."""
    script = ("function(context is Context, queries) { var out = []; for (var e in evaluateQuery(context, %s)) "
              "{ out = append(out, e.transientId); } return out; }" % expr)
    r = c.post(f"{BASE}/featurescript", json_data={"script": script})
    found = re.findall(r'"value":\s*"([^"]+)"', json.dumps(r.get("result")))
    return [x for x in found if x not in ("BTFSValueString", "BTFSValueArray")]


def sel(pid, *exprs):
    """A selection stored the way a click stores it: the ids `exprs` resolve to right now."""
    ids = []
    for e in exprs:
        ids += eval_ids(e)
    if not ids:
        raise RuntimeError("selection for %s is empty: %s" % (pid, exprs))
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": ids}]}


def qv(pid, name):
    """A selection of a query variable, stored the way the UI stores it."""
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualParametricQuery-3477", "queryVariableName": name, "queryStatement": None,
                         "queryString": "query = getQueryVariable(context, \"%s\");" % name}]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def s(pid, v):
    return {"btType": "BTMParameterString-149", "parameterId": pid, "value": v}


def arr(pid, items):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid,
            "items": [{"btType": "BTMArrayParameterItem-1843", "parameters": it} for it in items]}


def flist(pid, ids):
    return {"btType": "BTMParameterFeatureList-1749", "parameterId": pid, "featureIds": list(ids)}


# ---- tree ----
def post_feature(feature):
    f = c.get(f"{BASE}/features")
    body = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
            "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
    r = c.post(f"{BASE}/features", body)
    print("%-96s %s" % (feature["name"][:96], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns="", suppress_unless=None):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
    if suppress_unless:
        # Suppression by expression, the format the UI writes (active while the expression is true).
        f["suppressionState"] = {"btType": "BTMSuppressionStateExpression-1811",
                                 "value": {"btType": "BTMParameterQuantity-147", "isInteger": False, "value": 0.0, "units": "",
                                           "expression": suppress_unless, "parameterId": "feature-suppression-state"}}
    return post_feature(f)


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def polygon(prefix, pts):
    n = len(pts)
    return [seg("%s%d" % (prefix, i), pts[i][0] / 1000.0, pts[i][1] / 1000.0, pts[(i + 1) % n][0] / 1000.0, pts[(i + 1) % n][1] / 1000.0)
            for i in range(n)]


def sketch(name, entities):
    return post_feature({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                         "parameters": [q("sketchPlane", 'qCreatedBy(makeId("Top"), EntityType.FACE)')],
                         "entities": entities, "constraints": []})


def block(name, pts):
    sk = sketch(name + " (sketch)", polygon("p", pts))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"), num("depth", "20 mm")])


AP = 35 * math.cos(math.radians(36))  # pentagon apothem


def blocks(tag, y):
    pent = [(400 + 35 * math.cos(math.radians(90 + 72 * k)), y + 35 * math.sin(math.radians(90 + 72 * k))) for k in range(5)]
    return (block("%s block A (60 x 40)" % tag, [(-30, y - 20), (30, y - 20), (30, y + 20), (-30, y + 20)]),
            block("%s block B (90 x 30)" % tag, [(155, y - 15), (245, y - 15), (245, y + 15), (155, y + 15)]),
            block("%s block C (pentagon r 35)" % tag, pent))


def top(fid):
    return 'qCapEntity(makeId("%s"), CapType.END, EntityType.FACE)' % fid


def bottom(fid):
    return 'qCapEntity(makeId("%s"), CapType.START, EntityType.FACE)' % fid


def rim(fid):
    return 'qAdjacent(%s, AdjacencyType.EDGE, EntityType.EDGE)' % top(fid)


def side_at(fid, x, y):
    return 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.FACE), vector(%.9f, %.9f, 10) * millimeter)' % (fid, x, y)


def native_qv(name, created_by, entity):
    """A native Query Variable "created by", from a parameter set captured from a working one."""
    params = json.load(open("devtools/onshape/case_pattern_qv_template.json"))
    for p in params:
        if p["parameterId"] == "name":
            p["value"] = name
        elif p["parameterId"] == "createdByFeatures":
            p["featureIds"] = list(created_by)
        elif p["parameterId"] == "entityType":
            p["value"] = entity
    return params


WORDS = ["Length", "Angle", "Area", "Volume", "Number", "Integer", "Text", "Boolean"]
KIND_WORD = {"LENGTH": "Length", "ANGLE": "Angle", "AREA": "Area", "VOLUME": "Volume", "NUMBER": "Number",
             "INTEGER": "Integer", "TEXT": "Text", "BOOLEAN": "Boolean"}
KIND_TEXT = {"LENGTH": "length", "ANGLE": "angle", "AREA": "area, mm^2", "VOLUME": "volume, mm^3", "NUMBER": "number",
             "INTEGER": "integer", "TEXT": "text", "BOOLEAN": "true or false"}
ZERO = {"Length": "0 mm", "Angle": "0 deg", "Area": "0", "Volume": "0", "Number": "0", "Integer": "0", "Boolean": "true"}


def typed(prefix, kind, value):
    """All typed fields of a value slot (the API wants every parameter), `value` in the one of `kind`."""
    out = []
    for word in WORDS:
        pid = prefix + word
        mine = kind is not None and KIND_WORD[kind] == word and value is not None
        if word == "Text":
            out.append(s(pid, value if mine else ""))
        else:
            out.append(num(pid, value if mine else ZERO[word], integer=(word == "Integer")))
    return out


def define_case(name, case1, inputs, values=()):
    """inputs: [(name, expr)]; values: [(name, KIND, case-1 expression)]."""
    return feature(name, "defineCase", [
        s("caseName", case1),
        arr("inputs", [[s("inputName", n), sel("query", e)] for n, e in inputs]),
        arr("values", [[s("valueName", n), en("valueKind", "CaseValueKind", k, NS)] + typed("value", k, v) for n, k, v in values])], NS)


def close_case(name, define, features, outputs=(), names=""):
    """outputs: [(name, query variable name, evaluate on use)]."""
    return feature(name, "closeCase", [
        flist("defineCase", [define]), flist("features", features),
        arr("outputs", [[s("outputName", n), qv("outputQuery", v), b("outputOnUse", on_use), b("outputTrack", False)]
                        for n, v, on_use in outputs]),
        s("templateNames", names)], NS)


def case_row(case_name, inputs, selections, values=(), row_values=()):
    """A case row laid out as casePatternEditLogic lays it out. inputs: declared names; selections: one
    expression per input; values: [(name, KIND)] declared; row_values: one expression per value."""
    p = [s("caseName", case_name)]
    for k in range(1, MAX_INPUTS + 1):
        used = k <= len(inputs)
        p += [b("use%d" % k, used), s("in%dKey" % k, inputs[k - 1] if used else ""),
              s("in%dName" % k, "#" + inputs[k - 1] if used else ""),
              sel("input%d" % k, selections[k - 1]) if used else q("input%d" % k)]
    for m in range(1, MAX_VALUES + 1):
        used = m <= len(values)
        kind = values[m - 1][1] if used else None
        p += [b("useValue%d" % m, used), en("v%dKind" % m, "CaseSlotKind", kind if used else "NONE", NS),
              s("v%dKey" % m, values[m - 1][0] if used else ""),
              s("v%dName" % m, "#%s (%s)" % (values[m - 1][0], KIND_TEXT[kind]) if used else "")]
        p += typed("v%d" % m, kind, row_values[m - 1] if used else None)
    return p


def case_pattern(name, close, rows):
    return feature(name, "casePattern", [flist("closeCase", [close]), arr("cases", rows), b("debug", False)], NS)


def extrude_new(name, entities, depth, opposite=False):
    params = [en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
              entities, en("endBound", "BoundingType", "BLIND"), num("depth", depth)]
    if opposite:
        params.append(b("oppositeDirection", True))
    return params


# ---- clean slate ----
for f in reversed(c.get(f"{BASE}/features")["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")

# ---- T1 ----
A, B, C = blocks("T1", 0)
T1_IN = ["top", "rim"]
T1_VAL = [("bossH", "LENGTH"), ("edgeR", "NUMBER")]
d1 = define_case("T1 Define case A: #top, #rim; #bossH (length), #edgeR (number)", "A",
                 [("top", top(A)), ("rim", rim(A))], [("bossH", "LENGTH", "15 mm"), ("edgeR", "NUMBER", "3")])
boss = feature("T1 Boss: extrude #top #bossH new", "extrude", extrude_new("", qv("entities", "top"), "#bossH"))
rimf = feature("T1 Rim: fillet #rim 2 mm (outside the list)", "fillet", [qv("entities", "rim"), num("radius", "2 mm")])
qv1 = feature("T1 #bossEdges = edges created by Boss (native QV)", "queryVariable", native_qv("bossEdges", [boss], "EDGE"))
bossf = feature("T1 Boss edges: fillet #bossEdges #edgeR mm", "fillet", [qv("entities", "bossEdges"), num("radius", "#edgeR * 1 mm")])
qv1f = feature("T1 #bossFaces = faces created by Boss (native QV)", "queryVariable", native_qv("bossFaces", [boss], "FACE"))
c1 = close_case("T1 Close case: output bossFaces", d1, [boss, rimf, qv1, bossf, qv1f], [("bossFaces", "bossFaces", False)], "0\t0\tBoss_A")
case_pattern("T1 Case pattern B -> Boss_B 25 mm tall R3, #B_bossFaces", c1,
             [case_row("B", T1_IN, [top(B), rim(B)], T1_VAL, ["25 mm", "3"])])
case_pattern("T1 Case pattern C -> Boss_C 8 mm tall R2, #C_bossFaces", c1,
             [case_row("C", T1_IN, [top(C), rim(C)], T1_VAL, ["8 mm", "2"])])

# ---- T2 ----
A, B, C = blocks("T2", 100)
d2 = define_case("T2 Define case A: #side", "A", [("side", side_at(A, 30, 100))])
move = feature("T2 Move face #side offset 5 mm (first repeated feature)", "moveFace", [
    qv("moveFaces", "side"), en("moveFaceType", "MoveFaceType", "OFFSET"), num("offsetDistance", "5 mm")])
c2 = close_case("T2 Close case", d2, [move])
case_pattern("T2 Case pattern, two cases B and C -> B +X face and C 54 deg face offset 5 mm", c2, [
    case_row("B", ["side"], [side_at(B, 245, 100)]),
    case_row("C", ["side"], [side_at(C, 400 + AP * math.cos(math.radians(54)), 100 + AP * math.sin(math.radians(54)))])])

# ---- T3 ----
A, B, C = blocks("T3", 200)
d3 = define_case("T3 Define case A: #cap", "A", [("cap", bottom(A))])
post3 = feature("T3 Post: extrude #cap 10 mm up, new", "extrude", extrude_new("", qv("entities", "cap"), "10 mm", opposite=True))
post3f = feature("T3 Post edges: fillet 2 mm, clicked", "fillet", [
    sel("entities", 'qAdjacent(qCapEntity(makeId("%s"), CapType.END, EntityType.FACE), AdjacencyType.EDGE, EntityType.EDGE)' % post3),
    num("radius", "2 mm")])
c3 = close_case("T3 Close case", d3, [post3, post3f])
case_pattern("T3 Case pattern B -> expect ERROR: a clicked in-list edge stays on case 1", c3,
             [case_row("B", ["cap"], [bottom(B)])])

# ---- T6 ----
A, B, C = blocks("T6", 300)
d6 = define_case("T6 Define case A: #cap6, #pin (boolean true)", "A", [("cap6", bottom(A))], [("pin", "BOOLEAN", "true")])
post6 = feature("T6 Post: extrude #cap6 5 mm down, new", "extrude", extrude_new("", qv("entities", "cap6"), "5 mm"))
pin6 = feature("T6 Pin: extrude #cap6 12 mm down, new (active while #pin)", "extrude",
               extrude_new("", qv("entities", "cap6"), "12 mm"), suppress_unless="#pin")
c6 = close_case("T6 Close case", d6, [post6, pin6])
case_pattern("T6 Case pattern B, #pin false -> post only", c6, [case_row("B", ["cap6"], [bottom(B)], [("pin", "BOOLEAN")], ["false"])])
case_pattern("T6 Case pattern C, #pin true -> post and pin", c6, [case_row("C", ["cap6"], [bottom(C)], [("pin", "BOOLEAN")], ["true"])])

# ---- T7 ----
A, B, C = blocks("T7", 400)
d7 = define_case("T7 Define case A: #face7", "A", [("face7", top(A))])
stud = feature("T7 Stud: extrude #face7 10 mm new", "extrude", extrude_new("", qv("entities", "face7"), "10 mm"))
qv7 = feature("T7 #studBody = bodies created by Stud (native QV)", "queryVariable", native_qv("studBody", [stud], "BODY"))
c7 = close_case("T7 Close case: output stud", d7, [stud, qv7], [("stud", "studBody", False)])
case_pattern("T7 Case pattern B -> stud on block B, #B_stud", c7, [case_row("B", ["face7"], [top(B)])])
stud_b_top = 'qContainsPoint(qEverything(EntityType.FACE), vector(200, 400, 30) * millimeter)'
case_pattern("T7 Case pattern D on top of B's stud (chained) -> z 30..40, #D_stud", c7, [case_row("D", ["face7"], [stud_b_top])])
