"""(Re)insert the publish_tools test features into "Publish & Drawing tools" Part Studio 1.

Upserts by feature name. Entity ids are deterministic ids from an eval run (valid for the
Derive_Parts_V1 geometry); re-derive them with an eval probe if the derive changes.
usage: PYTHONPATH=. python devtools/onshape/publish_tools_fixture.py
"""
import sys
sys.path.insert(0, r"C:/Users/jed.yeiser/Documents/featurescripts")
from devtools.onshape.fsapi import upsert, namespace_of, qids, s, b, en, integer

D, W, E = "73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761", "bb2cddb24faf53d9d043c38e"
MC0, MC145, MC1625, EDGE = "SFXXG", "RFXb", "RFXP", "TFXpVC"
P4101, P4501 = "SFXnC", "SFXnG"


def arr(pid, items):
    return {"btType": "BTMParameterArray-2025", "parameterId": pid,
            "items": [{"btType": "BTMArrayParameterItem-1843", "parameters": it} for it in items]}


def st(kind, ns):
    return en("stationType", "StationEntryType", kind, ns)


def item(kind, ns, name, pt=(), second=(), edge=(), count=5, reverse=False, first=1):
    return [st(kind, ns), s("stationName", name), qids("point", pt), qids("secondPoint", second),
            qids("lineEdge", edge), integer("count", count), integer("firstNumber", first), b("reverse", reverse)]


def flist(pid, ids):
    return {"btType": "BTMParameterFeatureList-1749", "parameterId": pid, "featureIds": list(ids)}


def point(name, pid, ns):
    return item("POINT", ns, name, pt=[pid])


ns_def = namespace_of(D, W, "station_definition")
ns_geo = namespace_of(D, W, "station_geometry")

defn = upsert(D, W, E, "stationDefinition", "Stations (test)", [
    s("variableName", "stations"), en("language", "StationLanguage", "ENGLISH", ns_def),
    arr("stations", [
        point("TAIL", MC0, ns_def),
        point("EDA", MC145, ns_def),
        point("SPA", MC1625, ns_def),
        item("BETWEEN", ns_def, "Q", pt=[MC145], second=[MC1625], count=5),
        item("LINE", ns_def, "CORE", edge=[EDGE], count=4),
    ]),
    b("printStations", True),
], ns_def)

# Station geometry picks the Station definition feature (2026-09-28; the old "stationSet" variable name is kept hidden).
DEF_ID = defn["feature"]["featureId"]

upsert(D, W, E, "stationGeometry", "4101 stations (test)", [
    qids("part", [P4101]), qids("datum", [MC0]), s("prefix", "4101"),
    b("planView", True), b("profileView", False), arr("otherViews", []),
    flist("stationDefinitions", [DEF_ID]), s("stationSet", ""), arr("stations", []),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", False),
    b("datumPoint", True), b("flatCopy", True), b("printTable", True),
], ns_geo)

upsert(D, W, E, "stationGeometry", "4501 stations (test)", [
    qids("part", [P4501]), qids("datum", [MC0]), s("prefix", "4501"),
    b("planView", True), b("profileView", True), arr("otherViews", []),
    flist("stationDefinitions", [DEF_ID]), s("stationSet", ""),
    arr("stations", [point("MIDTEST", MC145, ns_geo)]),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", True),
    b("datumPoint", True), b("flatCopy", False), b("printTable", True),
], ns_geo)

# Surfaces as the part (2026-09-28): a flat sheet seen face-on (copied onto the view plane, since opCreateOutline
# refuses it) and a curved sheet (outline as usual).
P2D_PERIPHERY, PTOP_SURFACE = "SFXHB", "RFXv"
upsert(D, W, E, "stationGeometry", "2D_PERIPHERY stations (test)", [
    qids("part", [P2D_PERIPHERY]), qids("datum", [MC0]), s("prefix", "2D_PERIPHERY"),
    b("planView", True), b("profileView", False), arr("otherViews", []),
    flist("stationDefinitions", [DEF_ID]), s("stationSet", ""), arr("stations", []),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", False),
    b("datumPoint", True), b("flatCopy", False), b("printTable", True),
], ns_geo)
upsert(D, W, E, "stationGeometry", "TOP_SURFACE stations (test)", [
    qids("part", [PTOP_SURFACE]), qids("datum", [MC0]), s("prefix", "TOP_SURFACE"),
    b("planView", True), b("profileView", True), arr("otherViews", []),
    flist("stationDefinitions", [DEF_ID]), s("stationSet", ""), arr("stations", []),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", False),
    b("datumPoint", True), b("flatCopy", False), b("printTable", True),
], ns_geo)

# Unnamed stations numbered from 0, German headings (2026-09-28): expect stations 0..4 and a "4101N PLAN Stationen"
# table with Station | x (mm) | Breite (mm).
defn_de = upsert(D, W, E, "stationDefinition", "Numbered 0-4 DE (test)", [
    s("variableName", ""), en("language", "StationLanguage", "GERMAN", ns_def),
    arr("stations", [item("BETWEEN", ns_def, "", pt=[MC145], second=[MC1625], count=5, first=0)]),
    b("printStations", False),
], ns_def)
upsert(D, W, E, "stationGeometry", "4101N numbered DE (test)", [
    qids("part", [P4101]), qids("datum", [MC0]), s("prefix", "4101N"),
    b("planView", True), b("profileView", False), arr("otherViews", []),
    flist("stationDefinitions", [defn_de["feature"]["featureId"]]), s("stationSet", ""), arr("stations", []),
    b("stationLines", True), b("outlineWires", False), b("outlineSurface", False),
    b("datumPoint", True), b("flatCopy", False), b("printTable", True),
], ns_geo)

# Extents (2026-09-30, user: "always have extents, even if no other stations are provided"). Every view gets two
# position-only rows at the part's ends. No Station definition and no stations at all -> INFO, a table with only
# MIN X / MAX X (4101 plan: 0 / 1790).
upsert(D, W, E, "stationGeometry", "4101E extents only (test)", [
    qids("part", [P4101]), qids("datum", [MC0]), s("prefix", "4101E"),
    b("planView", True), b("profileView", False), arr("otherViews", []),
    flist("stationDefinitions", []), s("stationSet", ""), arr("stations", []),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", False),
    b("datumPoint", True), b("flatCopy", False), b("printTable", True),
], ns_geo)
# FCP + ACP among the stations -> the ends are named TIP (FCP's side, here x = 1790) and TAIL.
upsert(D, W, E, "stationGeometry", "4101T tip tail (test)", [
    qids("part", [P4101]), qids("datum", [MC0]), s("prefix", "4101T"),
    b("planView", True), b("profileView", True), arr("otherViews", []),
    flist("stationDefinitions", []), s("stationSet", ""),
    arr("stations", [point("FCP", MC1625, ns_geo), point("ACP", MC145, ns_geo)]),
    b("stationLines", True), b("outlineWires", False), b("outlineSurface", False),
    b("datumPoint", False), b("flatCopy", False), b("printTable", True),
], ns_geo)
