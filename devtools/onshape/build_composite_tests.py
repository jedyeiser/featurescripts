"""Build the Composite boolean test cases as real features (upsert by name; the studio is created when missing):

  composite_part_tools / "Composite tests"   Composite boolean (composite_part_tools/composite_boolean.fs,
                                             feature types compositeBoolean and the legacy compositeIntersect)

Needs composite_part_tools/.document.json, which exists only once the composite_part_tools document has been
created and the tab pushed (the user's call -- see composite_part_tools/README.md). Nothing here runs before that.

Fixtures are native features only (sketches, extrudes, composite parts). Cases come from
reviews/2026-09-25_tools_review/utility.md (P1 item 7). Checked by check_composite_tests.py.

Naming: a case feature is "<tag> <setup> -> <expected>"; fixtures start with the tag and never contain "->".
Cases sit 1000 mm apart along X; case k is centred on x0 = 1000 * k. Every solid ("the solid") is a
100 x 100 x 20 block: x0-50..x0+50, y -50..50, z -10..10. Strips are extruded symmetric about Top.

  C1  intersect: strip inside, strip crossing x0+50, strip outside       -> INFO, 2 bodies, volume 4000
  C2  subtract solid: same layout as C1                                  -> INFO, 2 bodies, volume 5000
  C3  intersect: the solid (a 20 cube here) inside a big strip           -> INFO, 1 body, volume 8000
  C4  intersect: strip inside sharing the solid's top and bottom faces,
      strip outside touching the solid's +X face                         -> INFO, 1 body, volume 4000
                                                                           (the ABUT classification: WARNING
                                                                           here means evCollision could not place it)
  C5  intersect: two strips crossing in ONE boolean                      -> INFO, 2 bodies of 2000 each
  C6  intersect, closed composite, result name "C6 clipped"             -> INFO, closed, named, volume 4000
  C7  subtract composite from bodies, keep composite                     -> INFO, block volume 180000, strips kept
  L1  legacy "Composite intersection" (compositeIntersect) on C1 layout  -> INFO, volume 4000, op id "result" present
  E1  intersect with every strip outside (temporary, deleted)            -> ERROR
  E2  empty solid selection (temporary, deleted)                         -> ERROR

usage (repo root): PYTHONPATH=. python devtools/onshape/build_composite_tests.py
"""
import copy
import json
import math
import os
import sys

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, feature_status  # noqa: E402  (API budget: one feature-tree GET per studio)

DOC_JSON = "composite_part_tools/.document.json"
TAB = "composite_boolean"
STUDIO = "Composite tests"

if not os.path.exists(DOC_JSON):
    sys.exit("%s not found: the composite_part_tools document has not been created/pushed yet." % DOC_JSON)

c = OnshapeClient()
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


# ---- parameter helpers (as build_utility_tests.py) ----
def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def en(pid, enum, value, ns=""):
    return {"btType": "BTMParameterEnum-145", "parameterId": pid, "enumName": enum, "value": value, "namespace": ns}


def num(pid, expr):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": False}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def default_param(p):
    """The spec's default for one parameter, without its nodeId, with an expression filled in (correction 38)."""
    d = copy.deepcopy(p.get("defaultValue"))
    if not isinstance(d, dict):
        return None
    d.pop("nodeId", None)
    d["parameterId"] = p["parameterId"]
    if d.get("btType", "").startswith("BTMParameterQuantity") and not d.get("expression"):
        kind = p.get("quantityType", "")
        v = d.get("value", 0.0)
        d["expression"] = ("%.10g mm" % (v * 1000) if kind == "LENGTH" else "%d" % round(v) if kind == "INTEGER"
                           else "%.10g rad" % v if kind == "ANGLE" else "%.10g" % v)
    return d


def from_spec(spec_params, given):
    """Every parameter of a spec from its default, with `given` on top (parameterId -> full parameter dict,
    or a plain value: enum value / expression / bool / string / list of query expressions)."""
    out = []
    for p in spec_params:
        pid = p["parameterId"]
        d = default_param(p)
        if pid not in given:
            if d is not None:
                out.append(d)
            continue
        v = given[pid]
        if isinstance(v, dict) and "btType" in v:
            out.append(dict(v, parameterId=pid))
            continue
        kind = d["btType"]
        if kind.startswith("BTMParameterQueryList"):
            d = q(pid, *([v] if isinstance(v, str) else v))
        elif kind.startswith("BTMParameterQuantity"):
            d["expression"] = v
            d.pop("value", None)
        else:
            d["value"] = v
        out.append(d)
    return out


def mmv(*xs):
    return [x / 1000.0 for x in xs]


def seg(eid, x0, y0, x1, y1):
    L = math.hypot(x1 - x0, y1 - y0)
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": 0.0, "endParam": L, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryLine-117", "pntX": x0, "pntY": y0, "dirX": (x1 - x0) / L, "dirY": (y1 - y0) / L}}


def rectangle(x0, x1, y0, y1):
    pts = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
    return [seg("r%d" % i, *mmv(pts[i][0], pts[i][1], pts[(i + 1) % 4][0], pts[(i + 1) % 4][1])) for i in range(4)]


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def composite_of(fid):
    return 'qBodyType(%s, BodyType.COMPOSITE)' % body(fid)


class Studio:
    """The test Part Studio (created when missing) and the feature studio tab of the feature under test."""

    def __init__(self, feature_type):
        doc = json.load(open(DOC_JSON))
        self.D, self.W = doc["document_id"], doc["workspace_id"]
        elements = c.list_elements(self.D, self.W)
        studios = {e["name"]: e["id"] for e in elements if e["elementType"] == "PARTSTUDIO"}
        if STUDIO not in studios:
            studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{self.D}/w/{self.W}", {"name": STUDIO})["id"]
        self.E = studios[STUDIO]
        self.base = f"/api/v10/partstudios/d/{self.D}/w/{self.W}/e/{self.E}"
        self.tree = {}   # the Part Studio feature tree (write_feature keeps it in step)
        self.specs = {}
        tab_el = [e for e in elements if e["name"] == TAB][0]
        self.ns = "e%s::m%s" % (tab_el["id"], tab_el["microversionId"])
        self.ftype = feature_type
        self.specs[feature_type] = [x for x in c.get(f"/api/v10/featurestudios/d/{self.D}/w/{self.W}/e/{tab_el['id']}/featurespecs")["featureSpecs"]
                                    if x["featureType"] == feature_type][0]["parameters"]
        print("studio %s (%s), %s namespace %s" % (STUDIO, self.E, feature_type, self.ns))

    def upsert(self, feature):
        # API budget: the feature tree is read once and kept in step with each write (write_feature).
        r = write_feature(self.base, feature, self.tree, client=c)
        print("%-110s %s" % (feature["name"][:110], r.get("featureState", {}).get("featureStatus")))
        return r["feature"]["featureId"]

    def feature(self, name, ftype, params, ns=""):
        f = {"btType": "BTMFeature-134", "featureType": ftype, "name": name, "parameters": params}
        if ns:
            f["namespace"] = ns
        return self.upsert(f)

    def std(self, name, ftype, given):
        """A std feature with every parameter from the Part Studio's own spec defaults."""
        if ftype not in self.specs:
            self.specs[ftype] = [x for x in c.get(f"{self.base}/featurespecs")["featureSpecs"] if x["featureType"] == ftype][0]["parameters"]
        return self.feature(name, ftype, from_spec(self.specs[ftype], given))

    def custom(self, name, given):
        """An instance of the feature under test; parameters not given take the spec defaults (correction 38)."""
        return self.feature(name, self.ftype, from_spec(self.specs[self.ftype], given), self.ns)

    def sketch(self, name, entities):
        return self.upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                            "parameters": [q("sketchPlane", TOP)], "entities": entities, "constraints": []})

    def slab(self, name, x0, x1, y0, y1, h):
        """A box x0..x1, y0..y1, z -h/2..h/2."""
        sk = self.sketch(name + " (sketch)", rectangle(x0, x1, y0, y1))
        return self.feature(name, "extrude", [
            en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
            q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"),
            num("depth", "%g mm" % h), b("symmetric", True)])

    def solid(self, tag, x0):
        """The standard solid: 100 x 100 x 20 centred on (x0, 0, 0)."""
        return self.slab("%s solid 100x100x20 at x %d" % (tag, x0), x0 - 50, x0 + 50, -50, 50, 20)

    def composite(self, tag, strips):
        """A native open composite part of the given strip features."""
        return self.std("%s composite of %d strips" % (tag, len(strips)), "compositePart", {"bodies": [body(s) for s in strips], "closed": False})

    def temporary(self, label, name, given, want="ERROR"):
        """Insert an instance, read its status, delete it (error cases stay out of the tree)."""
        tmp = self.custom(name, given)
        status = feature_status(self.base, self.tree, tmp, client=c)   # from the insert's own response
        delete_feature(self.base, tmp, self.tree, client=c)
        print("%s -> %s (%s)" % (label, status, "PASS" if status == want else "FAIL: expected " + want))
        return status == want


S = Studio("compositeBoolean")


def op(value):
    return en("operation", "CompositeBooleanOperation", value, S.ns)


def c1_layout(tag, x0):
    """Solid + composite of: strip inside (x0-40..x0-20), strip crossing x0+50 (x0+30..x0+80), strip outside
    (x0+100..x0+120); all 10 wide (y -5..5) and 10 high (z -5..5)."""
    sol = S.solid(tag, x0)
    a = S.slab("%s strip A inside" % tag, x0 - 40, x0 - 20, -5, 5, 10)
    bb = S.slab("%s strip B crossing" % tag, x0 + 30, x0 + 80, -5, 5, 10)
    cc = S.slab("%s strip C outside" % tag, x0 + 100, x0 + 120, -5, 5, 10)
    return sol, S.composite(tag, [a, bb, cc])


# C1 intersect: A whole (2000) + B clipped to x0+30..x0+50 (2000)
sol, comp = c1_layout("C1", 0)
S.custom("C1 intersect: inside + crossing + outside strips -> INFO, 2 bodies, volume 4000",
         {"operation": op("INTERSECT"), "compositePart": composite_of(comp), "solidBody": body(sol)})

# C2 subtract solid: C whole (2000) + B outside part x0+50..x0+80 (3000)
sol, comp = c1_layout("C2", 1000)
S.custom("C2 subtract solid: inside + crossing + outside strips -> INFO, 2 bodies, volume 5000",
         {"operation": op("SUBTRACT"), "compositePart": composite_of(comp), "solidBody": body(sol)})

# C3 the solid (a 20 cube) inside a big strip: TOOL_IN_TARGET -> the cube's volume
x0 = 2000
cube = S.slab("C3 solid cube 20 at x %d" % x0, x0 - 10, x0 + 10, -10, 10, 20)
big = S.slab("C3 strip big 200x40x40", x0 - 100, x0 + 100, -20, 20, 40)
far = S.slab("C3 strip outside", x0 + 150, x0 + 170, -5, 5, 10)
comp = S.composite("C3", [big, far])
S.custom("C3 intersect: solid inside a big strip -> INFO, 1 body, volume 8000",
         {"operation": op("INTERSECT"), "compositePart": composite_of(comp), "solidBody": body(cube)})

# C4 abutting: strip inside with the solid's full height (shares top and bottom faces), 20x10x20 = 4000 kept;
# strip outside touching the +X face (x0+50..x0+70) dropped.
x0 = 3000
sol = S.solid("C4", x0)
a = S.slab("C4 strip A inside, full height", x0 - 40, x0 - 20, -5, 5, 20)
t = S.slab("C4 strip T touching +X face", x0 + 50, x0 + 70, -5, 5, 10)
comp = S.composite("C4", [a, t])
S.custom("C4 intersect: strip sharing top/bottom faces + strip touching outside -> INFO, 1 body, volume 4000",
         {"operation": op("INTERSECT"), "compositePart": composite_of(comp), "solidBody": body(sol)})

# C5 two crossing strips in one boolean: each clipped to x0+30..x0+50 -> 2000 each
x0 = 4000
sol = S.solid("C5", x0)
p = S.slab("C5 strip P crossing at y -25", x0 + 30, x0 + 80, -30, -20, 10)
r = S.slab("C5 strip R crossing at y 25", x0 + 30, x0 + 80, 20, 30, 10)
comp = S.composite("C5", [p, r])
S.custom("C5 intersect: two crossing strips, one boolean -> INFO, 2 bodies of 2000",
         {"operation": op("INTERSECT"), "compositePart": composite_of(comp), "solidBody": body(sol)})

# C6 closed + named
sol, comp = c1_layout("C6", 5000)
S.custom("C6 intersect, closed, named 'C6 clipped' -> INFO, closed composite 'C6 clipped', volume 4000",
         {"operation": op("INTERSECT"), "compositePart": composite_of(comp), "solidBody": body(sol),
          "closedComposite": True, "resultName": "C6 clipped"})

# C7 subtract composite from bodies: two strips 160 long through the block; 100 of each inside -> 2 x 10000 removed
x0 = 6000
blk = S.solid("C7", x0)
p = S.slab("C7 strip P through at y -25", x0 - 80, x0 + 80, -30, -20, 10)
r = S.slab("C7 strip R through at y 25", x0 - 80, x0 + 80, 20, 30, 10)
comp = S.composite("C7", [p, r])
S.custom("C7 subtract composite from block, keep composite -> INFO, block volume 180000, strips kept",
         {"operation": op("SUBTRACT_FROM_BODIES"), "compositePart": composite_of(comp), "targetBodies": body(blk),
          "keepComposite": True})

# Error cases: temporary instances
x0 = 8000
sol = S.solid("E1", x0)
o1 = S.slab("E1 strip outside 1", x0 + 100, x0 + 120, -5, 5, 10)
o2 = S.slab("E1 strip outside 2", x0 + 150, x0 + 170, -5, 5, 10)
comp_e1 = S.composite("E1", [o1, o2])
ok = S.temporary("E1 every strip outside", "E1 temporary",
                 {"operation": op("INTERSECT"), "compositePart": composite_of(comp_e1), "solidBody": body(sol)})
ok &= S.temporary("E2 empty solid selection", "E2 temporary",
                  {"operation": op("INTERSECT"), "compositePart": composite_of(comp_e1), "solidBody": []})

# L1 legacy compositeIntersect entry point (parameter ids solidBody / compositePart, as the Ski Cores instances)
L = Studio("compositeIntersect")
sol, comp = c1_layout("L1", 7000)
L.custom("L1 legacy Composite intersection on the C1 layout -> INFO, 2 bodies, volume 4000, op id result",
         {"compositePart": composite_of(comp), "solidBody": body(sol)})

print("Composite tests studio", S.E)
print("temporary error cases:", "all passed" if ok else "FAILED")
