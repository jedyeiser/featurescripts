"""Move a Part Studio's v2 Case patterns to v3 Case features (Case_Pattern v3), from its old rows.

Per template (Define case -> body -> Close case -> Case pattern(s)):
  1. Define case + Close case switched to the v3 version (namespace NS).
  2. One Case feature per old case row, inserted just above the Close case (rollback bar there), its slots copied
     from the old row -- the same query objects (query-variable picks and clicks) and value expressions.
  3. Close case: Cases listed; outputs retyped as query-variable NAMES (from the old output's query-variable pick).
  4. Old Case pattern(s) deleted.
Modes: --dry-run (read-only: resolve every old selection just above its Close case) / --baseline out.json (read-only
snapshot of solid/sheet bodies + case outputs) / --run (writes; only with the user's go-ahead, never while they edit).

usage: PYTHONPATH=. python devtools/onshape/migrate_case_patterns.py <did> <wid> "<Part Studio>" <v3 namespace> MODE
"""
import copy
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, W, STUDIO, NS, MODE = sys.argv[1:6]
E = [e for e in c.list_elements(D, W) if e["name"] == STUDIO][0]["id"]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"


def tree():
    f = c.get(f"{BASE}/features")
    return f, f["features"]


def P(feature):
    return {p["parameterId"]: p for p in feature["parameters"]}


def text_of(p):
    if p is None:
        return None
    if isinstance(p.get("value"), str):
        return p["value"]
    return (p.get("expression") or "").strip('"')


def fs(script, rollback=None):
    params = {} if rollback is None else {"rollbackBarIndex": rollback}
    r = c._request("POST", f"{BASE}/featurescript", params, {"script": script})
    return json.dumps(r.get("result"))


def templates(features):
    """[(define, close, [case patterns])] in tree order."""
    byid = {x["featureId"]: x for x in features}
    out = {}
    for x in features:
        if x.get("featureType") == "casePattern":
            close_id = P(x)["closeCase"]["featureIds"][0]
            out.setdefault(close_id, []).append(x)
    result = []
    for close_id, cps in out.items():
        close = byid[close_id]
        define = byid[P(close)["defineCase"]["featureIds"][0]]
        result.append((define, close, cps))
    order = {x["featureId"]: i for i, x in enumerate(features)}
    return sorted(result, key=lambda t: order[t[1]["featureId"]])


def rows(cp):
    return [{p["parameterId"]: p for p in it["parameters"]} for it in P(cp)["cases"]["items"]]


def eval_expr(expr, feature_id):
    """A stored query string made evaluable outside its feature: its `id` is that feature's id."""
    return re.sub(r",\s*id\)$", ', makeId("%s"))' % feature_id, expr)


def row_queries(row):
    """FS expressions for every pick of a row: getQueryVariable for query-variable picks, the stored query for clicks."""
    out = []
    for k in range(1, 9):
        if row.get("use%d" % k, {}).get("value"):
            for q in row["input%d" % k].get("queries", []):
                if q.get("queryVariableName"):
                    out.append((text_of(row["in%dName" % k]), "#" + q["queryVariableName"], 'getQueryVariable(context, "%s")' % q["queryVariableName"]))
                else:
                    qs = (q.get("queryString") or "").strip()
                    expr = qs[qs.index("=") + 1:].rstrip(";").strip() if "=" in qs else "qNothing()"
                    out.append((text_of(row["in%dName" % k]), "click", expr))
    return out


if MODE == "--baseline":
    script = '''function(context is Context, queries) {
      var out = [];
      for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), [BodyType.SOLID, BodyType.SHEET]))) {
        const bb = evBox3d(context, { "topology" : b, "tight" : true });
        const isSolid = !isQueryEmpty(context, qBodyType(b, BodyType.SOLID));
        const size = isSolid ? evVolume(context, { "entities" : b }) / millimeter ^ 3 : evArea(context, { "entities" : b }) / millimeter ^ 2;
        out = append(out, getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) ~ "|" ~ (isSolid ? "solid" : "sheet")
            ~ "|" ~ roundToPrecision(size, 3) ~ "|" ~ roundToPrecision(bb.minCorner[0] / millimeter, 3) ~ "," ~ roundToPrecision(bb.maxCorner[0] / millimeter, 3)
            ~ "," ~ roundToPrecision(bb.minCorner[2] / millimeter, 3) ~ "," ~ roundToPrecision(bb.maxCorner[2] / millimeter, 3));
      }
      return out;
    }'''
    bodies = sorted(re.findall(r'"value":\s*"([^"]*\|[^"]*)"', fs(script)))
    _, features = tree()
    names = []
    if len(sys.argv) > 7:
        names = [o.split("|")[0] for o in json.load(open(sys.argv[7]))["outputs"]]
    for define, close, cps in ([] if names else templates(features)):
        outs = [text_of({q["parameterId"]: q for q in it["parameters"]}["outputName"]) for it in P(close)["outputs"]["items"]]
        cases = [text_of(P(define)["caseName"])] + [text_of(r["caseName"]) for cp in cps for r in rows(cp)]
        names += ["%s_%s" % (cn, o) for cn in cases for o in outs]
    script = 'function(context is Context, queries) { var out = []; for (var n in %s) { const q = try silent(getQueryVariable(context, n)); ' \
             'out = append(out, n ~ "|" ~ (q is Query ? size(evaluateQuery(context, q)) : -1)); } return out; }' % json.dumps(names)
    outputs = sorted(re.findall(r'"value":\s*"([^"]*\|-?\d+(?:\.0)?)"', fs(script)))
    json.dump({"bodies": bodies, "outputs": outputs}, open(sys.argv[6], "w"), indent=1)
    print(len(bodies), "bodies,", len(outputs), "outputs ->", sys.argv[6])
    sys.exit(0)

if MODE == "--dry-run":
    f, features = tree()
    order = {x["featureId"]: i for i, x in enumerate(features)}
    for define, close, cps in templates(features):
        ci = order[close["featureId"]]
        print("%s -> %s (#%d): %s" % (define["name"], close["name"], ci + 1, ", ".join(x["name"] for x in cps)))
        for it in P(close)["outputs"]["items"]:
            op = {q["parameterId"]: q for q in it["parameters"]}
            qv = [q.get("queryVariableName") for q in op["outputQuery"].get("queries", []) if q.get("queryVariableName")]
            print("   output %s <- %s" % (text_of(op["outputName"]), qv[0] if qv else "?? NONE"))
        for cp in cps:
            for r in rows(cp):
                qs = row_queries(r)
                body = "".join('out = append(out, size(evaluateQuery(context, %s)));' % eval_expr(e, cp["featureId"]) for _, _, e in qs)
                found = re.findall(r'"value":\s*(-?[\d.]+)', fs("function(context is Context, queries) { var out = []; %s return out; }" % body, ci))
                found = [int(float(x)) for x in found]
                ok = len(found) == len(qs) and all(n > 0 for n in found)
                print("   case %-16s %s" % (text_of(r["caseName"]), "all %d selections resolve above the Close case" % len(qs) if ok
                      else "PROBLEM " + str(list(zip([a for a, _, _ in qs], [b for _, b, _ in qs], found)))))
    sys.exit(0)

print("unknown mode", MODE)


# ---------------------------------------------------------------- --run (writes) ----------------------------------------
def strip_nodes(x):
    """A parameter tree without nodeIds / filters, safe to post as new parameters."""
    if isinstance(x, dict):
        return {k: strip_nodes(v) for k, v in x.items() if k not in ("nodeId", "filter", "libraryRelationType")}
    if isinstance(x, list):
        return [strip_nodes(v) for v in x]
    return x


def renamespace(x):
    """Enum parameters of the case_pattern tab moved to the v3 version's namespace."""
    if isinstance(x, dict):
        y = {k: renamespace(v) for k, v in x.items()}
        if y.get("btType") == "BTMParameterEnum-145" and "d2099413dd91f34578b385892" in (y.get("namespace") or ""):
            y["namespace"] = NS
        return y
    if isinstance(x, list):
        return [renamespace(v) for v in x]
    return x


def post(path, body):
    f = c.get(f"{BASE}/features")
    body = dict(body, serializationVersion=f["serializationVersion"], sourceMicroversion=f["sourceMicroversion"])
    return c.post(path, body)


def set_rollback(i):
    post(f"{BASE}/features/rollback", {"rollbackIndex": i})


def update_feature(feature):
    r = post(f"{BASE}/features/featureid/{feature['featureId']}", {"btType": "BTFeatureDefinitionCall-1406", "feature": feature})
    return r["featureState"]["featureStatus"]


def status_of(fid):
    return c.get(f"{BASE}/features")["featureStates"][fid]["featureStatus"]


if MODE == "--run":
    V3 = sys.argv[6]   # version id holding the same rows with deterministic ids for clicks
    EV = [e for e in c.get(f"/api/v10/documents/d/{D}/v/{V3}/elements") if e["name"] == STUDIO][0]["id"]
    vfeats = {x["featureId"]: x for x in c.get(f"/api/v10/partstudios/d/{D}/v/{V3}/e/{EV}/features")["features"]}
    f, features = tree()
    work = [(d["featureId"], cl["featureId"], [x["featureId"] for x in cps]) for d, cl, cps in templates(features)]
    for define_id, close_id, cp_ids in work:
        f, features = tree()
        byid = {x["featureId"]: x for x in features}
        define = copy.deepcopy(byid[define_id])
        print("==", define["name"])
        # 1. Define case on v3.
        if define["namespace"] != NS:
            define["namespace"] = NS
            define["parameters"] = renamespace(define["parameters"])
            print("   Define case ->", update_feature(define))
        # 2. Cases above the Close case.
        order = [x["featureId"] for x in features]
        set_rollback(order.index(close_id))
        case_ids = []
        for cp_id in cp_ids:
            for it_v in P(vfeats[cp_id])["cases"]["items"]:
                row = {p["parameterId"]: p for p in it_v["parameters"]}
                params = [{"btType": "BTMParameterFeatureList-1749", "parameterId": "defineCase", "featureIds": [define_id]}]
                for pid, p in row.items():
                    p = strip_nodes(renamespace(p))
                    if p.get("btType") == "BTMParameterQueryList-148":
                        qs = []
                        for q in p.get("queries", []):
                            if q.get("queryVariableName"):
                                qs.append({"btType": "BTMIndividualParametricQuery-3477", "queryVariableName": q["queryVariableName"],
                                           "queryStatement": None, "queryString": q.get("queryString")})
                            else:
                                qs.append({"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "",
                                           "deterministicIds": q.get("deterministicIds", [])})
                        p = {"btType": "BTMParameterQueryList-148", "parameterId": pid, "queries": qs}
                    params.append(p)
                name = text_of(row["caseName"])
                feat = {"btType": "BTMFeature-134", "featureType": "caseFeature", "name": "Case " + name, "namespace": NS, "parameters": params}
                r = post(f"{BASE}/features", {"btType": "BTFeatureDefinitionCall-1406", "feature": feat})
                case_ids.append(r["feature"]["featureId"])
                print("   Case", name, "->", r["featureState"]["featureStatus"])
        set_rollback(len(c.get(f"{BASE}/features")["features"]))
        # 3. Close case on v3: Cases listed, outputs by query-variable name.
        close = copy.deepcopy({x["featureId"]: x for x in c.get(f"{BASE}/features")["features"]}[close_id])
        close["namespace"] = NS
        new_params = []
        for p in close["parameters"]:
            if p["parameterId"] == "outputs":
                items = []
                for it in p["items"]:
                    op = {q["parameterId"]: q for q in it["parameters"]}
                    qv = [q.get("queryVariableName") for q in op["outputQuery"].get("queries", []) if q.get("queryVariableName")][0]
                    items.append({"btType": "BTMArrayParameterItem-1843", "parameters": [
                        {"btType": "BTMParameterString-149", "parameterId": "outputName", "value": text_of(op["outputName"])},
                        {"btType": "BTMParameterString-149", "parameterId": "outputVariable", "value": qv},
                        strip_nodes(op["outputOnUse"]),
                        strip_nodes(op.get("outputTrack", {"btType": "BTMParameterBoolean-144", "parameterId": "outputTrack", "value": False}))]})
                p = {"btType": "BTMParameterArray-2025", "parameterId": "outputs", "items": items}
            new_params.append(p)
        new_params.append({"btType": "BTMParameterFeatureList-1749", "parameterId": "cases", "featureIds": case_ids})
        close["parameters"] = [q for q in new_params if q["parameterId"] != "cases"] + [new_params[-1]]
        print("   Close case ->", update_feature(close))
        # 4. Old Case patterns out.
        for cp_id in cp_ids:
            c._request("DELETE", f"{BASE}/features/featureid/{cp_id}")
        print("   deleted", len(cp_ids), "Case pattern(s); Close case now", status_of(close_id))


# ------------------------------------------------ --run2: replace (the API cannot move a feature to a new version) -----
def ids_resolve(ids, rollback):
    if not ids:
        return False
    body = "return size(evaluateQuery(context, qUnion([%s])));" % ",".join('qTransient("%s")' % i for i in ids)
    r = c._request("POST", f"{BASE}/featurescript", {"rollbackBarIndex": rollback}, {"script": "function(context is Context, queries) { %s }" % body})
    v = (r.get("result") or {}).get("value")
    return v is not None and float(v) > 0


def convert(v3_param, main_param, rollback):
    """A v3-version parameter made postable: nodes stripped, enums on NS, query lists as picks (by name) or clicks
    (V3 deterministic ids when they resolve at `rollback`, else Main's stored query)."""
    p = strip_nodes(renamespace(v3_param))
    t = p.get("btType")
    if t == "BTMParameterQueryList-148":
        qs = []
        main_qs = (main_param or {}).get("queries", [])
        for n, q in enumerate(p.get("queries", [])):
            if q.get("queryVariableName"):
                qs.append({"btType": "BTMIndividualParametricQuery-3477", "queryVariableName": q["queryVariableName"],
                           "queryStatement": None, "queryString": q.get("queryString")})
            elif ids_resolve(q.get("deterministicIds", []), rollback):
                qs.append({"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": q["deterministicIds"]})
            else:
                mq = main_qs[n] if n < len(main_qs) else {}
                print("      click %s -> Main's stored query" % q.get("deterministicIds"))
                qs.append({"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": mq.get("queryString", "")})
        return {"btType": t, "parameterId": p["parameterId"], "queries": qs}
    if t == "BTMParameterArray-2025":
        items = []
        main_items = (main_param or {}).get("items", [])
        for n, it in enumerate(p.get("items", [])):
            mp = {q["parameterId"]: q for q in (main_items[n]["parameters"] if n < len(main_items) else [])}
            v3p = {q["parameterId"]: q for q in v3_param["items"][n]["parameters"]}
            items.append({"btType": "BTMArrayParameterItem-1843",
                          "parameters": [convert(v3p[k], mp.get(k), rollback) for k in v3p]})
        return {"btType": t, "parameterId": p["parameterId"], "items": items}
    return p


def insert_at(index, feature):
    set_rollback(index)
    r = post(f"{BASE}/features", {"btType": "BTFeatureDefinitionCall-1406", "feature": feature})
    return r["feature"]["featureId"], r["featureState"]["featureStatus"]


def index_of(fid):
    return [x["featureId"] for x in c.get(f"{BASE}/features")["features"]].index(fid)


def delete(fid):
    c._request("DELETE", f"{BASE}/features/featureid/{fid}")


if MODE == "--run2":
    import atexit
    atexit.register(lambda: set_rollback(len(c.get(f"{BASE}/features")["features"])))   # never leave the bar mid-tree
    V3 = sys.argv[6]
    EV = [e for e in c.get(f"/api/v10/documents/d/{D}/v/{V3}/elements") if e["name"] == STUDIO][0]["id"]
    vfeats = {x["featureId"]: x for x in c.get(f"/api/v10/partstudios/d/{D}/v/{V3}/e/{EV}/features")["features"]}
    f, features = tree()
    work = [(d["featureId"], cl["featureId"], [x["featureId"] for x in cps]) for d, cl, cps in templates(features)]
    for define_id, close_id, cp_ids in work:
        byid = {x["featureId"]: x for x in c.get(f"{BASE}/features")["features"]}
        old_define = byid[define_id]
        print("==", old_define["name"])
        new_define = define_id
        if old_define["namespace"] != NS:
            at = index_of(define_id)
            mp = P(old_define)
            params = [convert(p, mp.get(p["parameterId"]), at) for p in vfeats[define_id]["parameters"]]
            new_define, st = insert_at(at, {"btType": "BTMFeature-134", "featureType": "defineCase", "name": old_define["name"],
                                            "namespace": NS, "parameters": params})
            print("   new Define case ->", st)
        # Cases just above the old Close case.
        case_ids = []
        for cp_id in cp_ids:
            for it_v in P(vfeats[cp_id])["cases"]["items"]:
                at = index_of(close_id)
                params = [{"btType": "BTMParameterFeatureList-1749", "parameterId": "defineCase", "featureIds": [new_define]}]
                params += [convert(p, None, at) for p in it_v["parameters"]]
                name = text_of({p["parameterId"]: p for p in it_v["parameters"]}["caseName"])
                fid, st = insert_at(at, {"btType": "BTMFeature-134", "featureType": "caseFeature", "name": "Case " + name,
                                         "namespace": NS, "parameters": params})
                case_ids.append(fid)
                print("   Case", name, "->", st)
        # New Close case in the old one's place.
        vclose = P(vfeats[close_id])
        items = []
        for it in vclose["outputs"]["items"]:
            op = {q["parameterId"]: q for q in it["parameters"]}
            qv = [q.get("queryVariableName") for q in op["outputQuery"].get("queries", []) if q.get("queryVariableName")][0]
            items.append({"btType": "BTMArrayParameterItem-1843", "parameters": [
                {"btType": "BTMParameterString-149", "parameterId": "outputName", "value": text_of(op["outputName"])},
                {"btType": "BTMParameterString-149", "parameterId": "outputVariable", "value": qv},
                strip_nodes(op["outputOnUse"]),
                strip_nodes(op.get("outputTrack", {"btType": "BTMParameterBoolean-144", "parameterId": "outputTrack", "value": False}))]})
        params = [{"btType": "BTMParameterFeatureList-1749", "parameterId": "defineCase", "featureIds": [new_define]},
                  strip_nodes(vclose["features"]),
                  {"btType": "BTMParameterFeatureList-1749", "parameterId": "cases", "featureIds": case_ids},
                  {"btType": "BTMParameterArray-2025", "parameterId": "outputs", "items": items}]
        params += [strip_nodes(vclose[k]) for k in ("keepParts", "keepSurfaces", "keepCurves", "keepMateConnectors", "keepPlanes",
                                                    "keepSketches", "nameParts") if k in vclose]
        new_close, st = insert_at(index_of(close_id), {"btType": "BTMFeature-134", "featureType": "closeCase",
                                                       "name": byid[close_id]["name"], "namespace": NS, "parameters": params})
        print("   new Close case ->", st)
        for fid in cp_ids + [close_id] + ([define_id] if new_define != define_id else []):
            delete(fid)
        set_rollback(len(c.get(f"{BASE}/features")["features"]))
        fs_ = c.get(f"{BASE}/features")["featureStates"]
        print("   after deletes: Define", fs_[new_define]["featureStatus"], "| Close", fs_[new_close]["featureStatus"],
              "| Cases", [fs_[i]["featureStatus"] for i in case_ids])
