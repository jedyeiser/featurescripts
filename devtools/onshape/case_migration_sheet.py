"""Read-only: list what moving a document's Case patterns (v2) to Case features (v3) needs, per Part Studio,
top of the tree down. Reads a VERSION (immutable), so it is safe while the user works in the workspace.

For every Case pattern: its Close case and Define case, the Close case's outputs (-> query-variable names to type),
and per case row: name, each input's selection described at the Close case's position (entity, geometry, owner part,
creating feature, location), and each value. Placement: a case whose selections all exist just above its Close case
goes there; otherwise it needs a second Close case where the old Case pattern sat.

usage (repo root): PYTHONPATH=. python devtools/onshape/case_migration_sheet.py <did> <vid> "<Part Studio>" [out.md]
"""
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
D, V, STUDIO = sys.argv[1], sys.argv[2], sys.argv[3]
OUT = sys.argv[4] if len(sys.argv) > 4 else None
E = [e for e in c.get(f"/api/v10/documents/d/{D}/v/{V}/elements") if e["name"] == STUDIO][0]["id"]
BASE = f"/api/v10/partstudios/d/{D}/v/{V}/e/{E}"
feats = c.get(f"{BASE}/features")["features"]
index = {f["featureId"]: i for i, f in enumerate(feats)}
byid = {f["featureId"]: f for f in feats}


def params(f):
    return {p["parameterId"]: p for p in f["parameters"]}


def picks_of(p):
    """[(query variable name or None, [deterministic ids])] per pick of a query parameter."""
    return [(q.get("queryVariableName"), q.get("deterministicIds", []) or []) for q in (p or {}).get("queries", [])]


def text_of(p):
    """A string parameter's text (entered as an expression "\"surf\"" it arrives as a quantity)."""
    if p is None:
        return None
    if isinstance(p.get("value"), str):
        return p["value"]
    return (p.get("expression") or "").strip('"')


DESCRIBE = '''function(context is Context, queries) {
  var out = [];
  for (var tid in IDS) {
    const q = qTransient(tid);
    const ents = evaluateQuery(context, q);
    if (size(ents) == 0) { out = append(out, tid ~ "|MISSING"); continue; }
    var kind = "?";
    for (var t in [EntityType.BODY, EntityType.FACE, EntityType.EDGE, EntityType.VERTEX]) {
      if (!isQueryEmpty(context, qEntityFilter(q, t))) { kind = toString(t); }
    }
    var geo = "";
    if (kind == "FACE") { geo = toString(try silent(evSurfaceDefinition(context, { "face" : q }).surfaceType)); }
    if (kind == "EDGE") { geo = toString(try silent(evCurveDefinition(context, { "edge" : q }).curveType)); }
    const owners = evaluateQuery(context, qOwnerBody(q));
    var owner = "";
    if (size(owners) > 0) { owner = getProperty(context, { "entity" : owners[0], "propertyType" : PropertyType.NAME }); }
    if (kind == "BODY") { owner = getProperty(context, { "entity" : ents[0], "propertyType" : PropertyType.NAME }); }
    const bb = evBox3d(context, { "topology" : q, "tight" : true });
    const m = (bb.minCorner + bb.maxCorner) / 2 / millimeter;
    const op = lastModifyingOperationId(context, q);
    out = append(out, tid ~ "|" ~ kind ~ "|" ~ geo ~ "|" ~ owner ~ "|" ~ roundToPrecision(m[0], 1) ~ "," ~ roundToPrecision(m[1], 1)
        ~ "," ~ roundToPrecision(m[2], 1) ~ "|" ~ (size(op) > 0 ? op[0] : ""));
  }
  return out;
}'''


def describe(ids, rollback):
    if not ids:
        return {}
    script = DESCRIBE.replace("IDS", json.dumps(ids))
    r = c._request("POST", f"{BASE}/featurescript", {"rollbackBarIndex": rollback}, {"script": script})
    txt = json.dumps(r.get("result"))
    rows = [x for x in re.findall(r'"value":\s*"([^"]*\|[^"]*)"', txt)]
    out = {}
    for row in rows:
        parts = row.split("|")
        out[parts[0]] = parts[1:]
    return out


def fname(fid):
    return byid[fid]["name"] if fid in byid else fid


def quantity(p):
    return p.get("expression") if p else None


lines = ["# Case pattern -> Case migration: %s (version %s)" % (STUDIO, V), ""]
cps = [f for f in feats if f.get("featureType") == "casePattern"]
lines.append("%d Case pattern feature(s), top of the tree down." % len(cps))
for n, cp in enumerate(cps, 1):
    P = params(cp)
    close_id = (P.get("closeCase", {}).get("featureIds") or [None])[0]
    close = byid.get(close_id)
    CP = params(close) if close else {}
    define_id = (CP.get("defineCase", {}).get("featureIds") or [None])[0] if close else None
    define = byid.get(define_id)
    lines += ["", "## %d. %s  (tree #%d)" % (n, cp["name"], index[cp["featureId"]] + 1)]
    if not close:
        lines.append("  ! no Close case found")
        continue
    ci = index[close_id]
    lines.append("- Define case: **%s** (#%d); Close case: **%s** (#%d)" % (fname(define_id), index.get(define_id, -1) + 1, close["name"], ci + 1))
    outs = []
    for it in CP.get("outputs", {}).get("items", []):
        op = {p["parameterId"]: p for p in it["parameters"]}
        qvs = [q.get("queryVariableName") for q in op.get("outputQuery", {}).get("queries", []) if q.get("queryVariableName")]
        outs.append("`%s` <- query variable `%s`" % (text_of(op["outputName"]), qvs[0] if qvs else "?? (not a query variable: pick one)"))
    lines.append("- Close case outputs (type the query-variable name): " + ("; ".join(outs) if outs else "none"))
    for r_i, it in enumerate(P.get("cases", {}).get("items", []), 1):
        RP = {p["parameterId"]: p for p in it["parameters"]}
        name = text_of(RP["caseName"])
        sel = []
        for k in range(1, 9):
            if RP.get("use%d" % k, {}).get("value"):
                sel.append((text_of(RP["in%dName" % k]), picks_of(RP.get("input%d" % k))))
        all_ids = [i for _, picks in sel for _, ids in picks for i in ids]
        at_close = describe(all_ids, ci)
        at_end = describe([i for i in all_ids if at_close.get(i, ["MISSING"])[0] == "MISSING"], len(feats))
        movable = all(at_close.get(i, ["MISSING"])[0] != "MISSING" for i in all_ids)
        lines += ["", "### Case `%s`  -> %s" % (name, "insert just above the Close case" if movable
                    else "needs a SECOND Close case at the old Case pattern's place (selects geometry made after the Close case)")]
        for label, picks in sel:
            for qv, ids in picks:
                where = []
                for i in ids:
                    d = at_close.get(i) or at_end.get(i) or ["?"]
                    if d[0] in ("MISSING", "?"):
                        where.append("id %s not found" % i)
                        continue
                    kind, geo, owner, loc, op = (d + [""] * 5)[:5]
                    what = ((geo or "").lower() + " " + kind.lower()).strip()
                    where.append("%s **%s** at (%s) mm, from \"%s\"" % (what, owner, loc, fname(op)))
                if qv:
                    lines.append("- %s: pick query variable **#%s**  (= %s)" % (label, qv, "; ".join(where)))
                else:
                    lines.append("- %s: CLICK %s" % (label, "; ".join(where)))
        for m in range(1, 7):
            if RP.get("useValue%d" % m, {}).get("value"):
                kind = RP["v%dKind" % m]["value"]
                word = {"LENGTH": "Length", "ANGLE": "Angle", "AREA": "Area", "VOLUME": "Volume", "NUMBER": "Number",
                        "INTEGER": "Integer", "TEXT": "Text", "BOOLEAN": "Boolean"}[kind]
                fld = RP.get("v%d%s" % (m, word), {})
                val = text_of(fld) if word == "Text" else quantity(fld)
                lines.append("- %s = `%s`" % (text_of(RP["v%dName" % m]), val))
text = "\n".join(lines) + "\n"
if OUT:
    open(OUT, "w", encoding="ascii", errors="replace").write(text)
print(text)
