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


def item(kind, ns, name, pt=(), second=(), edge=(), count=5, reverse=False):
    return [st(kind, ns), s("stationName", name), qids("point", pt), qids("secondPoint", second),
            qids("lineEdge", edge), integer("count", count), b("reverse", reverse)]


def point(name, pid, ns):
    return item("POINT", ns, name, pt=[pid])


ns_def = namespace_of(D, W, "station_definition")
ns_geo = namespace_of(D, W, "station_geometry")

upsert(D, W, E, "stationDefinition", "Stations (test)", [
    s("variableName", "stations"),
    arr("stations", [
        point("TAIL", MC0, ns_def),
        point("EDA", MC145, ns_def),
        point("SPA", MC1625, ns_def),
        item("BETWEEN", ns_def, "Q", pt=[MC145], second=[MC1625], count=5),
        item("LINE", ns_def, "CORE", edge=[EDGE], count=4),
    ]),
    b("printStations", True),
], ns_def)

upsert(D, W, E, "stationGeometry", "4101 stations (test)", [
    qids("part", [P4101]), qids("datum", [MC0]), s("prefix", "4101"),
    b("planView", True), b("profileView", False), arr("otherViews", []),
    s("stationSet", "stations"), arr("stations", []),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", False),
    b("datumPoint", True), b("flatCopy", True), b("printTable", True),
], ns_geo)

upsert(D, W, E, "stationGeometry", "4501 stations (test)", [
    qids("part", [P4501]), qids("datum", [MC0]), s("prefix", "4501"),
    b("planView", True), b("profileView", True), arr("otherViews", []),
    s("stationSet", "stations"),
    arr("stations", [point("MIDTEST", MC145, ns_geo)]),
    b("stationLines", True), b("outlineWires", True), b("outlineSurface", True),
    b("datumPoint", True), b("flatCopy", False), b("printTable", True),
], ns_geo)
