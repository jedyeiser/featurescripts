"""Build the Case Pattern test cases as real features in the "Case pattern tests" Part Studio of the
case_pattern document. The studio is ours alone: every run deletes its features and rebuilds them.

Every selection is stored the way a click stores it (deterministic ids read from the tree at the
moment the feature is added), so the UI shows "Face of Part 1", not "Unknown".

Fixture: three blocks of different footprints, 20 mm tall on Top, far apart in X.
  A  rectangle 60 x 40 at x 0      B  rectangle 90 x 30 at x 200      C  pentagon r 35 at x 400

T1  Case template: #top = top face, #rim = top face edges; value #bossH (A 15, B 25, C 8 mm)
    Boss       extrude #top #bossH, new body
    Rim        fillet #rim 2 mm (edits a block from outside the list: runs outside the frame)
    #bossEdges native Query Variable "created by" Boss (the in-list reference)
    Boss edges fillet #bossEdges 3 mm
    Case pattern -> Boss_B 25 mm, Boss_C 8 mm tall, all edges filleted; rims filleted
T2  Move face as the FIRST repeated feature (Query Pattern's open bug): #side offset 5 mm
    -> B 5 mm longer in +X; C's 54 deg side face moved out 5 mm
T3  In-list reference by click: expected ERROR (clicks are not remapped onto the case)
T4  In-list reference by qCreatedBy(makeId(...)) query: expected ERROR (not remapped either)
T5  In-list reference through a native Query Variable "created by": posts under B, C, all edges
    filleted

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


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


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
    print("%-90s %s" % (feature["name"][:90], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


def feature(name, ftype, params, ns=""):
    f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
    if ns:
        f["namespace"] = ns
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


def length_variable(name, expression):
    """A native Variable feature (length), from a parameter set captured from a working one."""
    params = json.load(open("devtools/onshape/case_pattern_variable_template.json"))
    for p in params:
        if p["parameterId"] == "name":
            p["value"] = name
        elif p["parameterId"] == "lengthValue":
            p["expression"] = expression
    return params


def template(name, case1, inputs, cases, values=(), debug=False):
    """A Case template. inputs: [(name, expr)]; cases: [(case name, [expr per input])]."""
    rows = []
    for case_name, exprs in cases:
        params = [s("rowCaseName", case_name)]
        for k in range(1, 9):
            params.append(sel("input%d" % k, exprs[k - 1]) if k <= len(exprs) else q("input%d" % k))
        for k in range(2, 9):
            params.append(b("use%d" % k, k <= len(exprs)))
        rows.append(params)
    params = [s("caseName", case1),
              arr("inputs", [[s("inputName", n), sel("query", e)] for n, e in inputs]),
              arr("values", [[s("valueName", v)] for v in values]),
              arr("cases", rows), b("debug", debug)]
    for k in range(1, 9):
        params.append(s("slot%d" % k, "Input %d: #%s" % (k, inputs[k - 1][0]) if k <= len(inputs) else ""))
    for k in range(2, 9):
        params.append(b("showSlot%d" % k, k <= len(inputs)))
    return feature(name, "caseTemplate", params, NS)


def pattern(name, tpl, features, names=""):
    return feature(name, "casePattern", [flist("template", [tpl]), flist("features", features), s("templateNames", names)], NS)


# ---- clean slate ----
for f in reversed(c.get(f"{BASE}/features")["features"]):
    c._request("DELETE", f"{BASE}/features/featureid/{f['featureId']}")

AP = 35 * math.cos(math.radians(36))  # pentagon apothem
pent = [(400 + 35 * math.cos(math.radians(90 + 72 * k)), 35 * math.sin(math.radians(90 + 72 * k))) for k in range(5)]
A = block("Block A (60 x 40)", [(-30, -20), (30, -20), (30, 20), (-30, 20)])
B = block("Block B (90 x 30)", [(155, -15), (245, -15), (245, 15), (155, 15)])
C = block("Block C (pentagon r 35)", pent)

# ---- T1 ----
feature("T1 #bossH = 15 mm (case A)", "assignVariable", length_variable("bossH", "15 mm"))
feature("T1 #bossH_B = 25 mm", "assignVariable", length_variable("bossH_B", "25 mm"))
feature("T1 #bossH_C = 8 mm", "assignVariable", length_variable("bossH_C", "8 mm"))
tpl = template("T1 Case template A: #top, #rim; value #bossH; cases B, C", "A",
               [("top", top(A)), ("rim", rim(A))], [("B", [top(B), rim(B)]), ("C", [top(C), rim(C)])], values=["bossH"], debug=True)
boss = feature("T1 Boss: extrude #top #bossH new", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
    qv("entities", "top"), en("endBound", "BoundingType", "BLIND"), num("depth", "#bossH")])
rimf = feature("T1 Rim: fillet #rim 2 mm", "fillet", [qv("entities", "rim"), num("radius", "2 mm")])
qv1 = feature("T1 #bossEdges = edges created by Boss (native QV)", "queryVariable", native_qv("bossEdges", [boss], "EDGE"))
bossf = feature("T1 Boss edges: fillet #bossEdges 3 mm", "fillet", [qv("entities", "bossEdges"), num("radius", "3 mm")])
pattern("T1 Case pattern -> Boss_B 25 mm, Boss_C 8 mm tall, all edges filleted; rims filleted", tpl, [boss, rimf, qv1, bossf],
        "0\t0\tBoss_A")

# ---- T2 ----
tpl2 = template("T2 Case template A: #side; cases B, C", "A", [("side", side_at(A, 30, 0))],
                [("B", [side_at(B, 245, 0)]), ("C", [side_at(C, 400 + AP * math.cos(math.radians(54)), AP * math.sin(math.radians(54)))])])
move = feature("T2 Move face #side offset 5 mm (first repeated feature)", "moveFace", [
    qv("moveFaces", "side"), en("moveFaceType", "MoveFaceType", "OFFSET"), num("offsetDistance", "5 mm")])
pattern("T2 Case pattern -> B +X face and C 54 deg face offset 5 mm", tpl2, [move])

# ---- T3 ----
tpl3 = template("T3 Case template A: #cap; cases B, C", "A", [("cap", bottom(A))], [("B", [bottom(B)]), ("C", [bottom(C)])])
post3 = feature("T3 Post: extrude #cap 10 mm up, new", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
    qv("entities", "cap"), en("endBound", "BoundingType", "BLIND"), num("depth", "10 mm"), b("oppositeDirection", True)])
post3f = feature("T3 Post edges: fillet 2 mm, clicked", "fillet", [
    sel("entities", 'qAdjacent(qCapEntity(makeId("%s"), CapType.END, EntityType.FACE), AdjacencyType.EDGE, EntityType.EDGE)' % post3),
    num("radius", "2 mm")])
pattern("T3 Case pattern -> expect ERROR: a clicked in-list edge stays on case 1", tpl3, [post3, post3f])

# ---- T4 ----
tpl4 = template("T4 Case template A: #cap4; cases B, C", "A", [("cap4", bottom(A))], [("B", [bottom(B)]), ("C", [bottom(C)])])
post4 = feature("T4 Post: extrude #cap4 10 mm up, new", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
    qv("entities", "cap4"), en("endBound", "BoundingType", "BLIND"), num("depth", "10 mm"), b("oppositeDirection", True)])
post4f = feature("T4 Post edges: fillet 1 mm, by qCreatedBy query", "fillet", [
    q("entities", 'qAdjacent(qCapEntity(makeId("%s"), CapType.END, EntityType.FACE), AdjacencyType.EDGE, EntityType.EDGE)' % post4),
    num("radius", "1 mm")])
pattern("T4 Case pattern -> expect ERROR: a qCreatedBy(makeId) in-list query stays on case 1", tpl4, [post4, post4f])

# ---- T5 ----
tpl5 = template("T5 Case template A: #cap5; cases B, C", "A", [("cap5", bottom(A))], [("B", [bottom(B)]), ("C", [bottom(C)])])
post5 = feature("T5 Post: extrude #cap5 10 mm up, new", "extrude", [
    en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
    qv("entities", "cap5"), en("endBound", "BoundingType", "BLIND"), num("depth", "10 mm"), b("oppositeDirection", True)])
qv5 = feature("T5 #postEdges = edges created by Post (native QV)", "queryVariable", native_qv("postEdges", [post5], "EDGE"))
post5f = feature("T5 Post edges: fillet #postEdges 1 mm", "fillet", [qv("entities", "postEdges"), num("radius", "1 mm")])
pattern("T5 Case pattern -> posts under B, C, all edges filleted", tpl5, [post5, qv5, post5f])
