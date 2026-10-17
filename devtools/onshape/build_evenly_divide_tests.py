"""Build the Evenly divide surface test cases in the evenly_divide_surface document's "Divide tests" Part Studio.

Fixtures come from "Part Studio 1" through a Derived feature (the user's studio is only read, never written):
  roving_seed_surf (full, 47 faces)  -> closed sections, 0-360
  roving_seed_surf (half, 27 faces)  -> open sections,   0-180 (y >= 0)
  ctr_wire                           -> cross section wire, extends past the surface
  Front plane (y = 0)                -> symmetry plane

  E1 full, 5 divisions, wires on the symmetry plane
  E2 full, 5 divisions, half step off the symmetry plane
  E3 half, 5 divisions, wires on the symmetry plane  (first and last wire lie on it)
  E4 half, 5 divisions, half step off the symmetry plane

usage (repo root): PYTHONPATH=. python devtools/onshape/build_evenly_divide_tests.py
"""
import json
import os
import re

from sync.core.client import OnshapeClient
from devtools.onshape.fsapi import write_feature, delete_feature, cached_tree

c = OnshapeClient()
D, W = "69ab3696c014c05409202108", "c176011bc60a3f918e673d98"
SOURCE = "Part Studio 1"
STUDIO = "Divide tests"
SECTIONS = int(os.environ.get("DIVIDE_SECTIONS", "60"))

ELEMENTS = c.list_elements(D, W)
studios = {e["name"]: e["id"] for e in ELEMENTS if e["elementType"] == "PARTSTUDIO"}
if STUDIO not in studios:
    studios[STUDIO] = c.post(f"/api/v10/partstudios/d/{D}/w/{W}", {"name": STUDIO})["id"]
E = studios[STUDIO]
BASE = f"/api/v10/partstudios/d/{D}/w/{W}/e/{E}"
SRC = [e for e in ELEMENTS if e["name"] == SOURCE][0]
TAB = [e for e in ELEMENTS if e["name"] == "evenly_divide_surface" and e["elementType"] == "FEATURESTUDIO"][0]
NS = "e%s::m%s" % (TAB["id"], TAB["microversionId"])
SPEC = [x for x in c.get(f"/api/v10/featurestudios/d/{D}/w/{W}/e/{TAB['id']}/featurespecs")["featureSpecs"]
        if x["featureType"] == "evenlyDivideSurface"][0]
_TREE = {}


def q(pid, *exprs):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "query=%s;" % e} for e in exprs]}


def qids(pid, ids):
    return {"btType": "BTMParameterQueryList-148", "parameterId": pid,
            "queries": [{"btType": "BTMIndividualQuery-138", "queryStatement": None, "queryString": "", "deterministicIds": list(ids)}]}


def num(pid, expr, integer=False):
    return {"btType": "BTMParameterQuantity-147", "parameterId": pid, "expression": expr, "isInteger": integer}


def b(pid, v):
    return {"btType": "BTMParameterBoolean-144", "parameterId": pid, "value": v}


def write(feature):
    r = write_feature(BASE, feature, _TREE, client=c)
    print("%-80s %s" % (feature["name"][:80], r.get("featureState", {}).get("featureStatus")))
    return r["feature"]["featureId"]


# Start from an empty studio.
for f in reversed(cached_tree(BASE, _TREE, c)["features"]):
    delete_feature(BASE, f["featureId"], _TREE, client=c)

# ---- fixtures: derive everything from Part Studio 1 ----
derived = {
    "btType": "BTMFeature-134", "featureType": "importDerived", "name": "Derived from " + SOURCE,
    "parameters": [
        b("newUI", True),
        {"btType": "BTMParameterReferencePartStudio-3302", "parameterId": "partStudio",
         "namespace": "e%s::m%s" % (SRC["id"], SRC["microversionId"]),
         "partQuery": q("partQuery", "qEverything(EntityType.BODY)")},
        b("preserveActiveSheetMetal", False),
        {"btType": "BTMParameterQueryList-148", "parameterId": "location", "queries": []},
        {"btType": "BTMParameterEnum-145", "parameterId": "placement", "enumName": "DerivedPlacementType", "value": "AT_ORIGIN", "namespace": ""},
        num("mateConnectorIndex", "-1", True), num("mateConnectorId", "0"), num("mateConnectorIndexInFeature", "-1", True),
        b("includeMateConnectors", True), b("includeProperties", True),
        {"btType": "BTMParameterQueryList-148", "parameterId": "parts", "queries": []},
        {"btType": "BTMParameterDerived-864", "parameterId": "buildFunction", "namespace": "", "configuration": [], "imports": []},
    ]}
derived_id = write(derived)

# Which derived body is which: ids of the two roving surfaces (by face count) and the wire.
SCRIPT = """function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var wire = !isQueryEmpty(context, qIntersection([b, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)]));
        var sheet = !isQueryEmpty(context, qIntersection([b, qBodyType(qEverything(EntityType.BODY), BodyType.SHEET)]));
        out = append(out, "B " ~ toString(b) ~ " " ~ (wire ? "WIRE" : (sheet ? "SHEET" : "OTHER")) ~ " " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))));
    }
    return out;
}"""
r = c.post(f"{BASE}/featurescript", json_data={"script": SCRIPT})
rows = []
for v in re.findall(r'"value":\s*"((?:[^"\\]|\\.)*)"', json.dumps(r.get("result"))):
    if v.startswith("B "):
        m = re.search(r"transientId : (\w+) \} (\w+) (\d+)", v)
        rows.append((m.group(1), m.group(2), int(m.group(3))))
print(rows)
full = [i for i, k, n in rows if k == "SHEET" and n == 47][0]
half = [i for i, k, n in rows if k == "SHEET" and n == 27][0]
wire = [i for i, k, n in rows if k == "WIRE"][0]
FRONT = 'qCreatedBy(makeId("Front"), EntityType.FACE)'


def divide(name, surface_id, divisions, on_plane, extra=()):
    if name.split()[0] not in os.environ.get("DIVIDE_CASES", "E1,E2,E3,E4,S1,S2,S3,S4,S5,S6,S7").split(","):
        return None
    given = {p["parameterId"]: p for p in [
        qids("surface", [surface_id]), qids("crossWire", [wire]), q("symmetryPlane", FRONT),
        num("divisions", str(divisions), True), num("sections", str(SECTIONS), True), b("onSymmetryPlane", on_plane),
        b("projectWires", os.environ.get("DIVIDE_PROJECT", "1") == "1"), num("surfaceTolerance", "0.01 mm")] + list(extra)}
    params = []
    for p in SPEC["parameters"]:
        pid = p["parameterId"]
        d = p.get("defaultValue")
        if pid in given:
            params.append(given[pid])
        elif pid == "overrides":      # array parameter: an empty list when no overrides are given (correction 38)
            params.append({"btType": "BTMParameterArray-2025", "parameterId": pid, "items": []})
        elif isinstance(d, dict):
            params.append(dict(d, parameterId=pid))
    return write({"btType": "BTMFeature-134", "featureType": "evenlyDivideSurface", "name": name, "namespace": NS, "parameters": params})


WANT = os.environ.get("DIVIDE_CASES", "E1,E2,E3,E4,S1,S2,S3,S4,S5,S6,S7").split(",")   # which cases to build (run strip cases one at a time when debugging)

if "E1" in WANT:
    divide("E1 full, 5 divisions, wires on symmetry plane", full, 5, True)
if "E2" in WANT:
    divide("E2 full, 5 divisions, half step off symmetry plane", full, 5, False)
if "E3" in WANT:
    divide("E3 half, 5 divisions, wires on symmetry plane", half, 5, True)
if "E4" in WANT:
    divide("E4 half, 5 divisions, half step off symmetry plane", half, 5, False)


# ---- phase 2 settings (strip geometry is not built yet): the feature validates and reports them ----
def override(index, skip=False, width=None, thickness=None):
    """One strip-override item: every item parameter is sent, hidden ones with defaults (correction 38)."""
    return {"btType": "BTMArrayParameterItem-1843", "parameters": [
        num("wireIndex", str(index), True), b("skip", skip),
        b("overrideWidth", width is not None), num("width", width or "10 mm"),
        b("overrideThickness", thickness is not None), num("thickness", thickness or "1 mm")]}


def overrides(*items):
    return {"btType": "BTMParameterArray-2025", "parameterId": "overrides", "items": list(items)}


if os.environ.get("DIVIDE_PHASE2") == "1":
    # S1 half surface: width 12, thickness 2, mid-plane; wire 1 is 20 mm wide, wire 3 skipped, wire 9 does not exist
    #   expected: "Strips: 4 of 5 wires, width 12 mm (1 overridden), thickness 2 mm (0 overridden), 1 skipped, mid-plane, wires consumed."
    #             + warning "Override for wire 9 ignored: there are only 5 wires (0 to 4)."
    divide("S1 half strips: width 12, thickness 2, mid-plane; wire 1 width 20, wire 3 skipped, wire 9 invalid", half, 5, True, [
        b("createStrips", True), num("stripWidth", "12 mm"), num("stripThickness", "2 mm"), b("stripMidplane", True), b("consumeWires", True),
        overrides(override(1, width="20 mm"), override(3, skip=True), override(9, thickness="3 mm"))])
    # S2 full surface: opposite direction + second direction 0.5, wires kept; wire 0 overridden twice (thickness 3, then width 15)
    #   expected: "Strips: 5 of 5 wires, width 10 mm (1 overridden), thickness 1 mm (1 overridden), 0 skipped, second direction 0.5 mm, wires kept."
    #             + warning "Wire 0 has more than one override; they are applied in order."   (both apply: thickness 3 from the first, width 15 from the second)
    divide("S2 full strips: opposite + second direction 0.5, wires kept; wire 0 overridden twice", full, 5, True, [
        b("createStrips", True), num("stripWidth", "10 mm"), num("stripThickness", "1 mm"), b("stripMidplane", False), b("stripFlip", True),
        b("stripSecondDirection", True), num("stripThickness2", "0.5 mm"), b("consumeWires", False),
        overrides(override(0, thickness="3 mm"), override(0, width="15 mm"))])
    # ---- strip geometry ----
    # S3 half surface, 12 mm SURFACE strips (not thickened), wires kept: 5 sheet strips; the two edge strips (wires on the
    #    symmetry plane) are clipped to the half, so about half the area of the others
    divide("S3 half sheet strips, width 12, wires kept", half, 5, True, [
        b("createStrips", True), num("stripWidth", "12 mm"), b("thickenStrips", False), b("consumeWires", False), b("keepSections", True)])
    # S4 full surface, 10 mm strips thickened 2 mm mid-plane: 5 solids of about area x 2 mm
    divide("S4 full solid strips, width 10, thickness 2 mid-plane", full, 5, True, [
        b("createStrips", True), num("stripWidth", "10 mm"), b("thickenStrips", True), num("stripThickness", "2 mm"),
        b("stripMidplane", True), b("consumeWires", False)])
    # S5 full surface, 60 mm strips (wire spacing is about 44 mm): the strips overlap and stay separate bodies
    divide("S5 full overlapping sheet strips, width 60", full, 5, True, [
        b("createStrips", True), num("stripWidth", "60 mm"), b("thickenStrips", False), b("consumeWires", False)])    # S6 / S7: the same on the full surface at 30 and 45 mm (is a wide strip narrower than specified?)
    divide("S6 full sheet strips, width 30", full, 5, True, [
        b("createStrips", True), num("stripWidth", "30 mm"), b("thickenStrips", False), b("consumeWires", False)])
    divide("S7 full sheet strips, width 45", full, 5, True, [
        b("createStrips", True), num("stripWidth", "45 mm"), b("thickenStrips", False), b("consumeWires", False), b("keepSections", os.environ.get("DIVIDE_KEEP") == "1")])