"""Build the utility-feature test cases as real features (upsert by name; studios created when missing):

  example_1 / "Utility tests"   Join wires (example_1/joinWires.fs), Extrude edge (example_1/extrudeEdge.fs)
  bodyRename / "Rename tests"   Simple Body Rename (bodyRename/Simple_Rename.fs, feature type myFeature)

Fixtures are native features only (sketches, extrudes, offset planes, composite curves, composite parts, mate
connectors). Cases come from reviews/2026-09-25_tools_review/utility.md section 4. Checked by
check_utility_tests.py, which also lists the cases that document today's bugs (EXPECT_FAIL).

Naming: a case feature is "<tag> <setup> -> <expected>"; fixtures start with the tag and never contain "->".
Cases sit 1000 mm apart along X.

Join wires (utility.md J1-J5)
  J1  two touching wires                          -> 1 wire, 2 edges, length 170.7107, seeds deleted
  J2  a solid's edge + a touching wire            -> 1 wire, 2 edges, length 100 (BUG today: the filter / body
                                                     filter takes WIRE bodies only, so the edge is dropped)
  J3  two wires with a 50 mm gap                  -> WARNING, 2 wires (BUG today: no warning)
  J4  three wires forming a closed triangle       -> INFO, 1 closed wire (BUG today: no notice)
  J5  J1 with Keep seed bodies                    -> 1 wire, both seeds kept (3 bodies)
Extrude edge (utility.md E1-E6; E5 "up to surface" cannot be set: the feature has BLIND only)
  E1  wire (line 100 + R50 quarter arc) + mate connector, 30     -> 1 sheet, area 5356.194, z 0..30
  E2  two model edges of a block + the Top plane, 25             -> 1 sheet, area 1000, z 10..35
  E4  wire, direction vector (0, 0, 1), 20 + second direction 5  -> area 2500, z -5..20
  E7  wire + mate connector, flipped, 20                         -> z -20..0
  E8  wire + mate connector picked by its VERTEX (correction 44) -> z 0..20 (verified by eval: resolves today)
  E3  direction along the edges (temporary, deleted)             -> ERROR
  E6  empty selection (temporary, deleted)                       -> ERROR
  E9  direction from a circular edge (temporary, deleted)        -> ERROR (today: extractDir falls off its end
                                                                    with no return, so the error is a type error,
                                                                    not a message; the status cannot tell them apart)
Simple Body Rename (utility.md R1-R6)
  R1  solid                      -> "R1 solid"
  R2  composite part             -> "R2 composite"
  R3  prefix "P_" + suffix "_S"  -> "P_mid_S"
  R4  blank name + prefix "PRE_" -> INFO, name unchanged (BUG today: renamed to the bare prefix "PRE_")
  R5  lost reference + a solid   -> WARNING, the solid still renamed "R5 kept" (BUG today: setProperty throws
                                    CANNOT_RESOLVE_ENTITIES on the empty item, so the feature is an ERROR and
                                    nothing is renamed; verified by eval)
  R6  wire body                  -> "R6 wire"

usage (repo root): PYTHONPATH=. python devtools/onshape/build_utility_tests.py
"""
import copy
import json
import math

from sync.core.client import OnshapeClient

c = OnshapeClient()
TOP = 'qCreatedBy(makeId("Top"), EntityType.FACE)'


# ---- parameter helpers ----
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
    """Every parameter of a spec (array items included) from its default, with `given` on top.
    given: parameterId -> full parameter dict, or a plain value (enum value / expression / bool / string /
    list of query expressions / list of item dicts for an array)."""
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
        if kind.startswith("BTMParameterArray"):
            d["items"] = [{"btType": "BTMArrayParameterItem-1843", "parameters": from_spec(p["parameters"], item)} for item in v]
        elif kind.startswith("BTMParameterQueryList"):
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


def arc(eid, cx, cy, r, a0, a1):
    return {"btType": "BTMSketchCurveSegment-155", "entityId": eid, "startPointId": eid + ".start", "endPointId": eid + ".end",
            "startParam": a0, "endParam": a1, "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def circle(eid, cx, cy, r):
    return {"btType": "BTMSketchCurve-4", "entityId": eid, "centerId": eid + ".center", "isConstruction": False,
            "geometry": {"btType": "BTCurveGeometryCircle-115", "radius": r, "xCenter": cx, "yCenter": cy, "xDir": 1.0, "yDir": 0.0, "clockwise": False}}


def point(eid, x, y):
    return {"btType": "BTMSketchPoint-158", "entityId": eid, "isConstruction": False, "x": x, "y": y}


def polyline(prefix, pts, closed=False):
    n = len(pts) if closed else len(pts) - 1
    return [seg("%s%d" % (prefix, i), *mmv(pts[i][0], pts[i][1], pts[(i + 1) % len(pts)][0], pts[(i + 1) % len(pts)][1])) for i in range(n)]


def edges(fid):
    return 'qConstructionFilter(qOwnedByBody(qBodyType(qCreatedBy(makeId("%s"), EntityType.BODY), BodyType.WIRE), EntityType.EDGE), ConstructionObject.NO)' % fid


def body(fid):
    return 'qCreatedBy(makeId("%s"), EntityType.BODY)' % fid


def edge_at(fid, x, y, z):
    return 'qContainsPoint(qOwnedByBody(qCreatedBy(makeId("%s"), EntityType.BODY), EntityType.EDGE), vector(%g, %g, %g) * millimeter)' % (fid, x, y, z)


class Studio:
    """One test Part Studio (created when missing) and the feature studio tab of the feature under test."""

    def __init__(self, doc_json, studio, tab, feature_type):
        doc = json.load(open(doc_json))
        self.D, self.W = doc["document_id"], doc["workspace_id"]
        elements = c.list_elements(self.D, self.W)
        studios = {e["name"]: e["id"] for e in elements if e["elementType"] == "PARTSTUDIO"}
        if studio not in studios:
            studios[studio] = c.post(f"/api/v10/partstudios/d/{self.D}/w/{self.W}", {"name": studio})["id"]
        self.E = studios[studio]
        self.base = f"/api/v10/partstudios/d/{self.D}/w/{self.W}/e/{self.E}"
        self.features = None
        self.planes = {}
        self.specs = {}
        tab_el = [e for e in elements if e["name"] == tab][0]
        self.ns = "e%s::m%s" % (tab_el["id"], tab_el["microversionId"])
        self.ftype = feature_type
        self.specs[feature_type] = [x for x in c.get(f"/api/v10/featurestudios/d/{self.D}/w/{self.W}/e/{tab_el['id']}/featurespecs")["featureSpecs"]
                                    if x["featureType"] == feature_type][0]["parameters"]
        print("studio %s (%s), %s namespace %s" % (studio, self.E, feature_type, self.ns))

    def upsert(self, feature):
        if self.features is None:
            self.features = c.get(f"{self.base}/features")
        f = self.features
        existing = [x for x in f["features"] if x["name"] == feature["name"]]
        body_ = {"btType": "BTFeatureDefinitionCall-1406", "feature": feature,
                 "serializationVersion": f["serializationVersion"], "sourceMicroversion": f["sourceMicroversion"]}
        if existing:
            feature["featureId"] = existing[0]["featureId"]
            r = c.post(f"{self.base}/features/featureid/{existing[0]['featureId']}", body_)
        else:
            r = c.post(f"{self.base}/features", body_)
        self.features = None
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

    def sketch(self, name, plane_expr, entities):
        return self.upsert({"btType": "BTMSketch-151", "featureType": "newSketch", "name": name,
                            "parameters": [q("sketchPlane", plane_expr)], "entities": entities, "constraints": []})

    def plane_at(self, name, offset):
        if offset not in self.planes:
            self.planes[offset] = self.feature(name, "cPlane", [
                q("entities", TOP), en("cplaneType", "CPlaneType", "OFFSET"),
                num("offset", "%g mm" % abs(offset)), b("oppositeDirection", offset < 0), b("flipNormal", False)])
        return 'qCreatedBy(makeId("%s"), EntityType.FACE)' % self.planes[offset]

    def block(self, name, x, y, half=10):
        """A cube of side 2*half centred at (x, y, 0)."""
        sk = self.sketch(name + " (sketch)", TOP, polyline("r", [(x - half, y - half), (x + half, y - half), (x + half, y + half), (x - half, y + half)], closed=True))
        return self.feature(name, "extrude", [
            en("bodyType", "ExtendedToolBodyType", "SOLID"), en("operationType", "NewBodyOperationType", "NEW"),
            q("entities", 'qSketchRegion(makeId("%s"))' % sk), en("endBound", "BoundingType", "BLIND"),
            num("depth", "%g mm" % (2 * half)), b("symmetric", True)])

    def wire(self, name, entities, plane_expr=TOP):
        """A sketch turned into a wire body by a native composite curve."""
        sk = self.sketch(name + " (sketch)", plane_expr, entities)
        return self.feature(name, "compositeCurve", [q("edges", edges(sk))])

    def connector(self, name, x, y):
        sk = self.sketch(name + " (sketch)", TOP, [point("p", *mmv(x, y))])
        return self.feature(name, "mateConnector", [
            en("originType", "OriginCreationType", "ON_ENTITY"), q("originQuery", 'qCreatedBy(makeId("%s"), EntityType.VERTEX)' % sk),
            en("entityInferenceType", "EntityInferenceType", "POINT"), b("allowOwnerEntity", True), b("requireOwnerPart", False)])

    def temporary(self, label, name, given, want="ERROR"):
        """Insert an instance, read its status, delete it (error cases stay out of the tree)."""
        tmp = self.custom(name, given)
        status = c.get(f"{self.base}/features")["featureStates"][tmp]["featureStatus"]
        c._request("DELETE", f"{self.base}/features/featureid/{tmp}")
        self.features = None
        print("%s -> %s (%s)" % (label, status, "PASS" if status == want else "FAIL: expected " + want))
        return status == want


# ============================================================================
# example_1 / Utility tests: Join wires
# ============================================================================
U = Studio("example_1/.document.json", "Utility tests", "joinWires", "joinWires")

wa = U.wire("J1 wire A line (0, 0) to (100, 0)", polyline("l", [(0, 0), (100, 0)]))
wb = U.wire("J1 wire B line (100, 0) to (150, 50)", polyline("l", [(100, 0), (150, 50)]))
U.custom("J1 two touching wires -> 1 wire, 2 edges, length 170.7107, seeds deleted", {"edgeWireSelection": [body(wa), body(wb)]})

# J2: the block spans x 1000..1020, y -10..10, z -10..10; its top edge at y -10 runs (1000..1020, -10, 10)
b2 = U.block("J2 block 20 at (1010, 0, 0)", 1010, 0)
w2 = U.wire("J2 wire line (1020, -10, 10) to (1100, -10, 10)", polyline("l", [(1020, -10), (1100, -10)]), U.plane_at("Plane z = 10", 10))
U.custom("J2 block edge + touching wire -> 1 wire, 2 edges, length 100, block kept, seed deleted",
         {"edgeWireSelection": [edge_at(b2, 1010, -10, 10), body(w2)]})

w3a = U.wire("J3 wire A line (2000, 0) to (2100, 0)", polyline("l", [(2000, 0), (2100, 0)]))
w3b = U.wire("J3 wire B line (2150, 0) to (2250, 0)", polyline("l", [(2150, 0), (2250, 0)]))
U.custom("J3 two wires with a 50 gap -> WARNING, 2 wires, length 200", {"edgeWireSelection": [body(w3a), body(w3b)]})

tri = [(3000, 0), (3100, 0), (3050, 80)]
w4 = [U.wire("J4 wire %s line (%d, %d) to (%d, %d)" % ("ABC"[i], tri[i][0], tri[i][1], tri[(i + 1) % 3][0], tri[(i + 1) % 3][1]),
             polyline("l", [tri[i], tri[(i + 1) % 3]])) for i in range(3)]
U.custom("J4 three wires closing a triangle -> INFO, 1 closed wire, 3 edges, 3 vertices", {"edgeWireSelection": [body(w) for w in w4]})

w5a = U.wire("J5 wire A line (4000, 0) to (4100, 0)", polyline("l", [(4000, 0), (4100, 0)]))
w5b = U.wire("J5 wire B line (4100, 0) to (4150, 50)", polyline("l", [(4100, 0), (4150, 50)]))
U.custom("J5 keep seed bodies -> 1 wire, both seeds kept (3 bodies)", {"edgeWireSelection": [body(w5a), body(w5b)], "keepSeeds": True})

# ============================================================================
# example_1 / Utility tests: Extrude edge (same studio, own feature studio tab)
# ============================================================================
X = Studio("example_1/.document.json", "Utility tests", "extrudeEdge", "extrudeEdge")
X.planes = U.planes


def ee(name, given):
    return X.custom(name, given)


def ens(pid, enum, value):
    return en(pid, enum, value, X.ns)


# E1: line 10000..10100 + R50 quarter arc to (10150, 50): length 100 + 25 pi = 178.5398
w1 = X.wire("E1 wire line (10000, 0) to (10100, 0) + R50 quarter arc to (10150, 50)",
            polyline("l", [(10000, 0), (10100, 0)]) + [arc("a", *mmv(10100, 50, 50), -math.pi / 2, 0)])
mc1 = X.connector("E1 mate connector at (10000, -40, 0)", 10000, -40)
ee("E1 wire + mate connector, 30 -> 1 sheet, area 5356.194, z 0..30", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": body(w1),
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "QUERY"), "directionQuery": body(mc1),
    "extrudeLength": "30 mm"})

# E2: two top edges of a block (x 11000..11020, z 10), Top plane normal +Z
b_2 = X.block("E2 block 20 at (11010, 0, 0)", 11010, 0)
ee("E2 two block edges + Top plane, 25 -> 1 sheet, area 1000, z 10..35", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "EDGES"),
    "selEdges": [edge_at(b_2, 11010, -10, 10), edge_at(b_2, 11010, 10, 10)],
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "QUERY"), "directionQuery": TOP,
    "extrudeLength": "25 mm"})

w4e = X.wire("E4 wire line (12000, 0) to (12100, 0)", polyline("l", [(12000, 0), (12100, 0)]))
ee("E4 wire, vector (0, 0, 1), 20 + second direction 5 -> area 2500, z -5..20", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": body(w4e),
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "VECTOR"),
    "vectorX": "0", "vectorY": "0", "vectorZ": "1", "extrudeLength": "20 mm", "secondDirection": True, "dir2Len": "5 mm"})

w7 = X.wire("E7 wire line (13000, 0) to (13100, 0)", polyline("l", [(13000, 0), (13100, 0)]))
mc7 = X.connector("E7 mate connector at (13000, -40, 0)", 13000, -40)
ee("E7 wire + mate connector flipped, 20 -> area 2000, z -20..0", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": body(w7),
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "QUERY"), "directionQuery": body(mc7),
    "flipDir": True, "extrudeLength": "20 mm"})

w8 = X.wire("E8 wire line (14000, 0) to (14100, 0)", polyline("l", [(14000, 0), (14100, 0)]))
mc8 = X.connector("E8 mate connector at (14000, -40, 0)", 14000, -40)
ee("E8 wire + mate connector picked by its vertex, 20 -> area 2000, z 0..20", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": body(w8),
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "QUERY"),
    "directionQuery": 'qOwnedByBody(%s, EntityType.VERTEX)' % body(mc8), "extrudeLength": "20 mm"})

# Error cases: temporary instances
w3e = X.wire("E3 wire line (15000, 0) to (15100, 0)", polyline("l", [(15000, 0), (15100, 0)]))
ok = X.temporary("E3 direction along the edges", "E3 temporary", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": body(w3e),
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "VECTOR"),
    "vectorX": "1", "vectorY": "0", "vectorZ": "0", "extrudeLength": "20 mm"})
ok &= X.temporary("E6 empty selection", "E6 temporary", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": [],
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "VECTOR"),
    "vectorX": "0", "vectorY": "0", "vectorZ": "1", "extrudeLength": "20 mm"})
c9 = X.sketch("E9 direction sketch: circle R20 at (16000, 60)", TOP, [circle("c", *mmv(16000, 60, 20))])
w9 = X.wire("E9 wire line (16000, 0) to (16100, 0)", polyline("l", [(16000, 0), (16100, 0)]))
ok &= X.temporary("E9 direction from a circular edge (extractDir falls through)", "E9 temporary", {
    "inputType": ens("inputType", "ExtrudeEdgeInputType", "BODIES"), "wireBody": body(w9),
    "extrudeDirectionFrom": ens("extrudeDirectionFrom", "ExtrudeEdgeDirectionType", "QUERY"), "directionQuery": edges(c9),
    "extrudeLength": "20 mm"})
print("Utility tests studio", U.E)

# ============================================================================
# bodyRename / Rename tests: Simple Body Rename
# ============================================================================
R = Studio("bodyRename/.document.json", "Rename tests", "Simple_Rename", "myFeature")


def rename(name, items, prefix=None, suffix=None):
    given = {"renameArray": [{"query": qexpr, "renameString": text} for qexpr, text in items]}
    if prefix is not None:
        given.update({"usePrefix": True, "prefix": prefix})
    if suffix is not None:
        given.update({"useSuffix": True, "suffix": suffix})
    return R.custom(name, given)


r1 = R.block("R1 block at (0, 0, 0)", 0, 0)
rename("R1 solid renamed -> 'R1 solid'", [(body(r1), "R1 solid")])

r2a = R.block("R2 block A at (1000, 0, 0)", 1000, 0)
r2b = R.block("R2 block B at (1040, 0, 0)", 1040, 0)
r2 = R.std("R2 composite part of blocks A and B", "compositePart", {"bodies": [body(r2a), body(r2b)], "closed": False})
rename("R2 composite part renamed -> 'R2 composite'", [('qBodyType(%s, BodyType.COMPOSITE)' % body(r2), "R2 composite")])

r3 = R.block("R3 block at (2000, 0, 0)", 2000, 0)
rename("R3 prefix 'P_' + 'mid' + suffix '_S' -> 'P_mid_S'", [(body(r3), "mid")], prefix="P_", suffix="_S")

r4 = R.block("R4 block at (3000, 0, 0)", 3000, 0)
rename("R4 blank name with prefix 'PRE_' -> INFO, skipped, name unchanged", [(body(r4), "")], prefix="PRE_")

r5 = R.block("R5 block at (4000, 0, 0)", 4000, 0)
rename("R5 lost reference + block -> WARNING, block still renamed 'R5 kept'",
       [('qCreatedBy(makeId("R5missingFeature"), EntityType.BODY)', "R5 lost"), (body(r5), "R5 kept")])

r6 = R.wire("R6 wire line (5000, 0) to (5100, 0)", polyline("l", [(5000, 0), (5100, 0)]))
rename("R6 wire body renamed -> 'R6 wire'", [(body(r6), "R6 wire")])
print("Rename tests studio", R.E)
print("temporary error cases:", "all passed" if ok else "FAILED")
