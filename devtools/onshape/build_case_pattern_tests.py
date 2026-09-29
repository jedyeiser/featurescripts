"""Build the Case Pattern v3 tests (Define case / Case / Close case) as real features in the
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
T9  Offset+ side reference = a vertex from before the Define case: T9a clicked inside Offset+ -> expect ERROR
    (Reference_Side V5 refuses a picked reference it cannot read; V4 silently flipped), T9b passed in as a Define
    case input -> correct
T10 The same reference as a SHARED reference (no per-case selection), two cases in one pattern
    -> B's +X face and C's 54 deg face both offset toward -X
T12 Move face+ (Reference_Side V5) in a Case pattern: input = the block's +X face, SHARED reference = block A's
    -X corner, two cases -> each face moves 2 mm toward the reference (the block shrinks); edits geometry from
    before the Define case, so it also runs the step-out-of-frame retry
T11 A value slot left empty in a case -> expect ERROR "#h11 has no value" (no silent case-1 fallback)
T8  Part naming: the same stud case twice, case 1's stud named "Stud_A" (cached names), Close case
    "Name parts with the case name" on -> case B's stud "Stud_B"; off -> Onshape's default name kept

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


def define_case(name, case1, inputs, values=(), shared=()):
    """inputs: [(name, expr)]; values: [(name, KIND, case-1 expression)]; shared: [(name, expr)]."""
    return feature(name, "defineCase", [
        s("caseName", case1),
        arr("inputs", [[s("inputName", n), sel("query", e)] for n, e in inputs]),
        arr("shared", [[s("sharedName", n), sel("sharedQuery", e)] for n, e in shared]),
        arr("values", [[s("valueName", n), en("valueKind", "CaseValueKind", k, NS)] + typed("value", k, v) for n, k, v in values])], NS)


def close_case(name, define, features, cases=(), outputs=(), name_parts=True):
    """outputs: [(name, query variable the body sets, evaluate on use)]."""
    return feature(name, "closeCase", [b("nameParts", name_parts),
        flist("defineCase", [define]), flist("features", features), flist("cases", cases),
        arr("outputs", [[s("outputName", n), s("outputVariable", v), b("outputOnUse", on_use), b("outputTrack", False)]
                        for n, v, on_use in outputs]), b("debug", False)], NS)


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


def case_feature(name, define, case_name, inputs, selections, values=(), row_values=()):
    """A Case feature laid out as caseEditLogic lays it out (see case_row)."""
    return feature(name, "caseFeature", [flist("defineCase", [define])] + case_row(case_name, inputs, selections, values, row_values), NS)


def extrude_new(name, entities, depth, opposite=False):
    params = [en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
              entities, en("endBound", "BoundingType", "BLIND"), num("depth", depth)]
    if opposite:
        params.append(b("oppositeDirection", True))
    return params


# ---- clean slate ----
for f in reversed(c.get(f"{BASE}/features")["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")


def tower(name, x, y, h):
    sk = sketch(name + " (sketch)", polygon("p", [(x - 10, y - 10), (x + 10, y - 10), (x + 10, y + 10), (x - 10, y + 10)]))
    return feature(name, "extrude", [
        en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
        q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"), num("depth", h)])


def up_to(name, entities, face_expr):
    return feature(name, "extrude", [en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
                                     entities, en("endBound", "BoundingType", "UP_TO_SURFACE"), sel("endBoundEntityFace", face_expr)])


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
k1b = case_feature("T1 Case B (25 mm, 3)", d1, "B", T1_IN, [top(B), rim(B)], T1_VAL, ["25 mm", "3"])
k1c = case_feature("T1 Case C (8 mm, 2)", d1, "C", T1_IN, [top(C), rim(C)], T1_VAL, ["8 mm", "2"])
close_case("T1 Close case B, C -> Boss_B 25 mm R3, Boss_C 8 mm R2, #X_bossFaces", d1, [boss, rimf, qv1, bossf, qv1f], [k1b, k1c],
           [("bossFaces", "bossFaces", False)])

# ---- T2 ----
A, B, C = blocks("T2", 100)
d2 = define_case("T2 Define case A: #side", "A", [("side", side_at(A, 30, 100))])
move = feature("T2 Move face #side offset 5 mm (first repeated feature)", "moveFace", [
    qv("moveFaces", "side"), en("moveFaceType", "MoveFaceType", "OFFSET"), num("offsetDistance", "5 mm")])
k2b = case_feature("T2 Case B", d2, "B", ["side"], [side_at(B, 245, 100)])
k2c = case_feature("T2 Case C", d2, "C", ["side"], [side_at(C, 400 + AP * math.cos(math.radians(54)), 100 + AP * math.sin(math.radians(54)))])
close_case("T2 Close case B, C -> B +X face and C 54 deg face offset 5 mm", d2, [move], [k2b, k2c])

# ---- T3 ----
A, B, C = blocks("T3", 200)
d3 = define_case("T3 Define case A: #cap", "A", [("cap", bottom(A))])
post3 = feature("T3 Post: extrude #cap 10 mm up, new", "extrude", extrude_new("", qv("entities", "cap"), "10 mm", opposite=True))
post3f = feature("T3 Post edges: fillet 2 mm, clicked", "fillet", [
    sel("entities", 'qAdjacent(qCapEntity(makeId("%s"), CapType.END, EntityType.FACE), AdjacencyType.EDGE, EntityType.EDGE)' % post3),
    num("radius", "2 mm")])
k3 = case_feature("T3 Case B", d3, "B", ["cap"], [bottom(B)])
close_case("T3 Close case B -> expect ERROR: a clicked in-list edge stays on case 1", d3, [post3, post3f], [k3])

# ---- T6 ----
A, B, C = blocks("T6", 300)
d6 = define_case("T6 Define case A: #cap6, #pin (boolean true)", "A", [("cap6", bottom(A))], [("pin", "BOOLEAN", "true")])
post6 = feature("T6 Post: extrude #cap6 5 mm down, new", "extrude", extrude_new("", qv("entities", "cap6"), "5 mm"))
pin6 = feature("T6 Pin: extrude #cap6 12 mm down, new (active while #pin)", "extrude",
               extrude_new("", qv("entities", "cap6"), "12 mm"), suppress_unless="#pin")
k6b = case_feature("T6 Case B, #pin false", d6, "B", ["cap6"], [bottom(B)], [("pin", "BOOLEAN")], ["false"])
k6c = case_feature("T6 Case C, #pin true", d6, "C", ["cap6"], [bottom(C)], [("pin", "BOOLEAN")], ["true"])
close_case("T6 Close case B, C -> post only under B, post and pin under C", d6, [post6, pin6], [k6b, k6c])

# ---- T7: re-close ----
A, B, C = blocks("T7", 400)
d7 = define_case("T7 Define case A: #face7", "A", [("face7", top(A))])
stud = feature("T7 Stud: extrude #face7 10 mm new", "extrude", extrude_new("", qv("entities", "face7"), "10 mm"))
qv7 = feature("T7 #studBody = bodies created by Stud (native QV)", "queryVariable", native_qv("studBody", [stud], "BODY"))
k7b = case_feature("T7 Case B", d7, "B", ["face7"], [top(B)])
close_case("T7 Close case B -> stud on block B, #A_stud, #B_stud", d7, [stud, qv7], [k7b], [("stud", "studBody", False)])
stud_b_top = 'qContainsPoint(qEverything(EntityType.FACE), vector(200, 400, 30) * millimeter)'
k7d = case_feature("T7 Case D on top of B's stud (made by the first Close case)", d7, "D", ["face7"], [stud_b_top])
close_case("T7 Close case again (same Define case and body), D -> z 30..40, #D_stud", d7, [stud, qv7], [k7d], [("stud", "studBody", False)])

# ---- T8 ----
A, B, C = blocks("T8", 500)
d8 = define_case("T8 Define case A: face8", "A", [("face8", top(A))])
stud8 = feature("T8 Stud: extrude face8 10 mm new", "extrude", extrude_new("", qv("entities", "face8"), "10 mm"))
qv8 = feature("T8 _stud = bodies created by Stud (native QV)", "queryVariable", native_qv("_stud", [stud8], "BODY"))
k8 = case_feature("T8 Case B", d8, "B", ["face8"], [top(B)])
close_case("T8 Close case, names ON -> case 1 part A_stud8, case B part B_stud8", d8, [stud8, qv8], [k8], [("stud8", "_stud", False)])
d8off = define_case("T8off Define case F: face8off (block C)", "F", [("face8off", top(C))])
stud8off = feature("T8off Stud: extrude face8off 10 mm new", "extrude", extrude_new("", qv("entities", "face8off"), "10 mm"))
qv8off = feature("T8off _stud8off = bodies created by Stud (native QV)", "queryVariable", native_qv("_stud8off", [stud8off], "BODY"))
close_case("T8off Close case, no further cases, names OFF -> default name", d8off, [stud8off, qv8off], [], [("studoff", "_stud8off", False)],
           name_parts=False)

# ---- T9 ----
# Offset+ (Reference_Side) inside a case, side reference = a vertex OUTSIDE the list (the RD 20FOU 28 setup).
# Wall = the block's +X face; reference = block A's (-X, -Y, top) corner, so "toward the reference" is -X for every case.
# T9a clicks the reference in Offset+ (v2: ERROR; v3: works, correction 60); T9b routes it through a Define case input.
OFFSET_PLUS_NS = "d22764764a00a7f607dbc1c4d::vfe155aeed546628ec5b3fba7::e742e5b3f04cc9115de8b6d8a::m94ab915a1bf0099353c19a6a"
MOVE_FACE_PLUS_NS = "d22764764a00a7f607dbc1c4d::vfe155aeed546628ec5b3fba7::efb7f7776686f955d15ce4262::mb340dd3d29eee73b047e7ea5"


def offset_plus(name, surfaces, side_reference):
    return feature(name, "offsetPlus", [
        en("offsetType", "OffsetPlusType", "SURFACE", OFFSET_PLUS_NS), surfaces, q("curves"),
        en("frameMode", "OffsetFrameMode", "TRANSPORT", OFFSET_PLUS_NS), q("normalSurface"),
        en("surfaceDirection", "SurfaceOffsetDirection", "NORMAL", OFFSET_PLUS_NS), q("planeNormal"),
        num("distance", "2 mm"), side_reference, b("towardReference", True),
        num("samplesPerEdge", "24", integer=True), num("maxSpacing", "5 mm"), num("fitTolerance", "0.001 mm"), b("debugPrint", False)],
        OFFSET_PLUS_NS)


A, B, C = blocks("T9", 600)
corner_a = 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(-30, 580, 20) * millimeter)' % A
d9a = define_case("T9a Define case A: wall9 (+X face)", "A", [("wall9", side_at(A, 30, 600))])
op9a = offset_plus("T9a Offset+ wall9 2 mm toward CLICKED corner", qv("surfaces", "wall9"), sel("sideReference", corner_a))
k9a = case_feature("T9a Case B", d9a, "B", ["wall9"], [side_at(B, 245, 600)])
close_case("T9a Close case B -> reference clicked inside the body works: offset toward -X (x 243)", d9a, [op9a], [k9a], name_parts=False)
d9b = define_case("T9b Define case A: wall9b, ref9b (corner)", "A", [("wall9b", side_at(A, 30, 600)), ("ref9b", corner_a)])
op9b = offset_plus("T9b Offset+ wall9b 2 mm toward ref9b", qv("surfaces", "wall9b"), qv("sideReference", "ref9b"))
k9b = case_feature("T9b Case C", d9b, "C", ["wall9b", "ref9b"],
                   [side_at(C, 400 + AP * math.cos(math.radians(54)), 600 + AP * math.sin(math.radians(54))), corner_a])
close_case("T9b Close case C -> offset toward -X (x 398.8..432.1)", d9b, [op9b], [k9b], name_parts=False)

# ---- T10 ----
A, B, C = blocks("T10", 700)
corner10 = 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(-30, 680, 20) * millimeter)' % A
d10 = define_case("T10 Define case A: wall10; shared ref10 (A's corner)", "A", [("wall10", side_at(A, 30, 700))], shared=[("ref10", corner10)])
op10 = offset_plus("T10 Offset+ wall10 2 mm toward shared ref10", qv("surfaces", "wall10"), qv("sideReference", "ref10"))
k10b = case_feature("T10 Case B", d10, "B", ["wall10"], [side_at(B, 245, 700)])
k10c = case_feature("T10 Case C", d10, "C", ["wall10"], [side_at(C, 400 + AP * math.cos(math.radians(54)), 700 + AP * math.sin(math.radians(54)))])
close_case("T10 Close case B, C, shared reference -> both offset toward -X", d10, [op10], [k10b, k10c], name_parts=False)

# ---- T11 ----
A, B, C = blocks("T11", 800)
d11 = define_case("T11 Define case A: face11; h11 (length 10 mm)", "A", [("face11", top(A))], values=[("h11", "LENGTH", "10 mm")])
stud11 = feature("T11 Stud: extrude face11 h11 new", "extrude", extrude_new("", qv("entities", "face11"), "#h11"))
k11 = case_feature("T11 Case B, value slot missing -> expect ERROR: #h11 has no value", d11, "B", ["face11"], [top(B)])
close_case("T11 Close case B -> expect ERROR (its Case failed)", d11, [stud11], [k11], name_parts=False)

# ---- T12 ----
A, B, C = blocks("T12", 900)
corner12 = 'qContainsPoint(qCreatedBy(makeId("%s"), EntityType.VERTEX), vector(-30, 880, 20) * millimeter)' % A
d12 = define_case("T12 Define case A: face12; shared ref12 (A's -X corner)", "A", [("face12", side_at(A, 30, 900))], shared=[("ref12", corner12)])
mf12 = feature("T12 Move face+ face12 2 mm toward shared ref12", "moveFacePlus", [
    qv("faces", "face12"), num("distance", "2 mm"), qv("sideReference", "ref12"), b("towardReference", True),
    b("reFillet", False), b("debugPrint", False)], MOVE_FACE_PLUS_NS)
k12b = case_feature("T12 Case B", d12, "B", ["face12"], [side_at(B, 245, 900)])
k12c = case_feature("T12 Case C", d12, "C", ["face12"], [side_at(C, 400 + AP * math.cos(math.radians(54)), 900 + AP * math.sin(math.radians(54)))])
close_case("T12 Close case B, C -> both faces move 2 mm toward the reference (blocks shrink)", d12, [mf12], [k12b, k12c], name_parts=False)

# ---- T13: clicked outside references (correction 60) ----
A, B, C = blocks("T13", 1000)
tw13 = tower("T13 tower (outside, 60 mm)", 600, 1000, "60 mm")
d13 = define_case("T13 Define case A: #top13", "A", [("top13", top(A))])
boss13 = up_to("T13 Boss: extrude #top13 up to the CLICKED tower top", qv("entities", "top13"), top(tw13))
qv13 = feature("T13 #boss13 = bodies created by Boss (native QV)", "queryVariable", native_qv("boss13", [boss13], "BODY"))
k13 = case_feature("T13 Case B", d13, "B", ["top13"], [top(B)])
close_case("T13 Close case B -> B_boss13 up to the tower top (z 20..60)", d13, [boss13, qv13], [k13], [("boss13", "boss13", False)])

# ---- T14: clicked outside reference AND an edit of outside geometry in one case ----
A, B, C = blocks("T14", 1100)
tw14 = tower("T14 tower (outside, 60 mm)", 600, 1100, "60 mm")
d14 = define_case("T14 Define case A: #top14, #rim14", "A", [("top14", top(A)), ("rim14", rim(A))])
boss14 = up_to("T14 Boss: extrude #top14 up to the CLICKED tower top", qv("entities", "top14"), top(tw14))
rim14 = feature("T14 Rim: fillet #rim14 2 mm (edits the block)", "fillet", [qv("entities", "rim14"), num("radius", "2 mm")])
qv14 = feature("T14 #bossEdges14 = edges created by Boss (native QV)", "queryVariable", native_qv("bossEdges14", [boss14], "EDGE"))
f14 = feature("T14 Boss edges: fillet #bossEdges14 1 mm", "fillet", [qv("entities", "bossEdges14"), num("radius", "1 mm")])
k14 = case_feature("T14 Case B", d14, "B", ["top14", "rim14"], [top(B), rim(B)])
close_case("T14 Close case B -> boss up to tower, B's rim filleted, boss edges filleted", d14, [boss14, rim14, qv14, f14], [k14], name_parts=False)
