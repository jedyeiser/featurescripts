"""Re-point broken click selections of Part Studio features to the same geometry in the workspace.

A click stores entity ids tied to the feature that created the entity. After a migration re-creates the geometry
in a different feature (Case pattern -> Close case), clicks on it find nothing. For every click query of the named
features: read the SAME feature in a reference version (its deterministic ids), describe each entity there
(type, owning body's name, geometry type, box centre; a mate connector's origin), find the entity in the workspace
with the same description at the feature's position, and write the feature back with those ids. Read-only unless
--write. A click that matches none or several entities is reported and left alone.

usage: PYTHONPATH=. python devtools/onshape/repick_by_geometry.py <did> <wid> <vid> "<Part Studio>" "<feature>" [...] [--write]
"""
import copy
import json
import re
import sys

from sync.core.client import OnshapeClient

c = OnshapeClient()
args = [a for a in sys.argv[1:] if a != "--write"]
WRITE = "--write" in sys.argv
D, W, V, STUDIO = args[:4]
NAMES = args[4:]
EW = [e for e in c.list_elements(D, W) if e["name"] == STUDIO][0]["id"]
EV = [e for e in c.get(f"/api/v10/documents/d/{D}/v/{V}/elements") if e["name"] == STUDIO][0]["id"]
BW = f"/api/v10/partstudios/d/{D}/w/{W}/e/{EW}"
BV = f"/api/v10/partstudios/d/{D}/v/{V}/e/{EV}"

DESCRIBE = '''
function sig(context is Context, q is Query) returns string {
  var kind = "BODY";
  for (var t in [EntityType.FACE, EntityType.EDGE, EntityType.VERTEX]) { if (!isQueryEmpty(context, qEntityFilter(q, t))) { kind = toString(t); } }
  var owner = "";
  const ob = evaluateQuery(context, kind == "BODY" ? q : qOwnerBody(q));
  if (size(ob) > 0) { owner = toString(try silent(getProperty(context, { "entity" : ob[0], "propertyType" : PropertyType.NAME }))); }
  var at = [0, 0, 0];
  const mc = try silent(evMateConnector(context, { "mateConnector" : q }));
  if (mc != undefined) { at = mc.origin / millimeter; kind = "MC"; }
  else { const bb = evBox3d(context, { "topology" : q, "tight" : true }); at = (bb.minCorner + bb.maxCorner) / 2 / millimeter; }
  return kind ~ "|" ~ owner ~ "|" ~ roundToPrecision(at[0], 3) ~ "," ~ roundToPrecision(at[1], 3) ~ "," ~ roundToPrecision(at[2], 3);
}
'''


def fs(base, body, rollback):
    script = "function(context is Context, queries) { %s }" % body
    r = c._request("POST", base + "/featurescript", {"rollbackBarIndex": rollback}, {"script": DESCRIBE.replace("function sig", "const sig = function") .replace("returns string {", "{") + script if False else script})
    return r


def run(base, body, rollback):
    # sig as a lambda inside the evaluated function (the eval API takes one function only)
    lam = DESCRIBE.strip().replace("function sig(context is Context, q is Query) returns string {", "const sig = function(context is Context, q is Query) {", 1)
    script = "function(context is Context, queries) { %s; %s }" % (lam, body)
    r = c._request("POST", base + "/featurescript", {"rollbackBarIndex": rollback}, {"script": script})
    return re.findall(r'"value":\s*"([^"]*)"', json.dumps(r.get("result"))), [n["message"] for n in r.get("notices", []) if n.get("level") == "ERROR"][:2]


fw = c.get(BW + "/features")
fv = {x["featureId"]: x for x in c.get(BV + "/features")["features"]}
order = [x["featureId"] for x in fw["features"]]
vorder = list(fv)
for name in NAMES:
    feat = [x for x in fw["features"] if x["name"] == name][0]
    at = order.index(feat["featureId"])
    vfeat = fv.get(feat["featureId"])
    if vfeat is None:
        print(name, ": not in the reference version")
        continue
    vat = vorder.index(feat["featureId"])
    new = copy.deepcopy(feat)
    changed = 0
    vparams = {p["parameterId"]: p for p in vfeat["parameters"]}
    for p in new["parameters"]:
        vp = vparams.get(p["parameterId"])
        if p.get("btType") != "BTMParameterQueryList-148" or vp is None:
            continue
        vclicks = [q for q in vp.get("queries", []) if q.get("deterministicIds")]
        if not vclicks or any(q.get("queryVariableName") for q in p.get("queries", [])):
            continue
        ids = [i for q in vclicks for i in q["deterministicIds"]]
        sigs, err = run(BV, "var out = []; for (var t in %s) { out = append(out, sig(context, qTransient(t))); } return out;" % json.dumps(ids), vat)
        if err or len(sigs) != len(ids):
            # A version's ids can name the entity as the END of its tree has it (seen on 20FOU): describe it there.
            sigs, err = run(BV, "var out = []; for (var t in %s) { out = append(out, sig(context, qTransient(t))); } return out;" % json.dumps(ids), len(vorder))
        if err or len(sigs) != len(ids):
            print(name, p["parameterId"], ": could not describe in the version", err)
            continue
        new_ids = []
        for s in sigs:
            kind, owner, xyz = s.split("|")
            x, y, z = (float(v) for v in xyz.split(","))
            etype = {"FACE": "EntityType.FACE", "EDGE": "EntityType.EDGE", "VERTEX": "EntityType.VERTEX"}.get(kind, "EntityType.BODY")
            body = ('var out = []; for (var e in evaluateQuery(context, qEverything(%s))) { const s = sig(context, e); '
                    'const p = splitIntoCharacters(s); if (s == "%s") { out = append(out, e.transientId); } } return out;') % (etype, s)
            found, err = run(BW, body, at)
            if len(found) != 1:
                # tolerant match on the centre (1e-3 mm) when the exact string differs by rounding
                body = ('var out = []; for (var e in evaluateQuery(context, qEverything(%s))) { const s = sig(context, e); '
                        'if (s != undefined) { out = append(out, e.transientId ~ "#" ~ s); } } return out;') % etype
                allf, err = run(BW, body, at)
                cands = []
                for a in allf:
                    tid, sg = a.split("#", 1)
                    k2, o2, xyz2 = sg.split("|")
                    x2, y2, z2 = (float(v) for v in xyz2.split(","))
                    if k2 == kind and o2 == owner and abs(x - x2) < 1e-2 and abs(y - y2) < 1e-2 and abs(z - z2) < 1e-2:
                        cands.append(tid)
                found = cands
            if len(found) != 1:
                print(name, p["parameterId"], ": %s matched %d entities" % (s, len(found)), err)
                new_ids = None
                break
            new_ids.append(found[0])
        if new_ids is None:
            continue
        p["queries"] = [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": new_ids}]
        changed += 1
        print(name, p["parameterId"], ":", len(new_ids), "entities re-picked", sigs[:2])
    if changed and WRITE:
        cur = c.get(BW + "/features")
        r = c.post(BW + "/features/featureid/" + feat["featureId"], {"btType": "BTFeatureDefinitionCall-1406", "feature": new,
                   "serializationVersion": cur["serializationVersion"], "sourceMicroversion": cur["sourceMicroversion"]})
        print("  ->", name, r["featureState"]["featureStatus"])
