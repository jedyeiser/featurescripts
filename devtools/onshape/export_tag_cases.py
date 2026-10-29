"""Test cases for the Export tag (publish_tools/export/export_tag.fs), shared by build_export_tag_tests.py and
check_export_tag_tests.py.

The cases are real Export tag features in the Part Studio "Export Tests" of the Publish & Drawing tools document,
named "ZT<n> <title>" (the user's own tags, Base_Export / Core_Export / Export_UG, are never touched). Their parts are the
three skis derived there (Belag = base, HK = core composite, UG = underlay).

A case is { key, title, part, stem, params, expect }.

params: ("b", id, bool) ("s", id, text) ("num", id, "10 mm") ("int", id, n) ("enum", id, EnumType, VALUE)
        ("copy", id, "<feature name>")  copies that parameter (e.g. a face selection) from an existing feature.
expect: status        INFO | WARNING | ERROR          (feature status)
        message_has   text the error / warning / info must contain
        sheets        body names (SHEET) that must exist
        composites    body names (COMPOSITE) that must exist
        absent        body names that must NOT exist
        min           { sheet name: (x, y, z) mm }     lowest corner, 0.05 mm
        arcs_min      { sheet name: n }               at least n circular edges (arcs stay arcs)
        edges_max     { sheet name: n }               at most n edges (a section is a few edges, a silhouette hundreds)
        pitch_y       [name, name, name]              equal steps in y between the three sheets
        faces_equal_sheets  True                      the feature created exactly as many faces as sheets (no construction plane)
"""

STUDIO = ("73271cfcd708b3f5e3fc315c", "fea83d30106dba0a4e066761", "40ddd837fc2586ab9a75239f")
TAG_STUDIO_NAME = "export_tag"
PREFIX = "ZT"

PARTS = {"belag": "RD K035_SZ01 178 Belag", "hk": "RD K035_SZ01 178 HK", "ug": "RD K035_SZ01 178 UG"}

QUADRANT = [("enum", "originType", "ExportOriginType", "QUADRANT"), ("enum", "quadrant", "ExportQuadrant", "FIRST"),
            ("num", "bufferX", "10 mm"), ("num", "bufferY", "10 mm"), ("num", "bufferZ", "0 mm")]

CASES = [
    {"key": "ZT1", "title": "plan, 1st quadrant, pattern 1 x 3 -> 3 faces, arcs kept", "part": "belag", "stem": "ZT1 PLAN",
     "params": [("b", "exportDxf", True), ("enum", "planSource", "ExportDxfSource", "PROJECTION")] + QUADRANT
               + [("b", "dxfPattern", True), ("int", "patternCountX", 1), ("int", "patternCountY", 3),
                  ("enum", "patternSpacing", "ExportPatternSpacing", "CLEARANCE"), ("num", "clearanceY", "10 mm")],
     "expect": {"status": "INFO",
                "sheets": ["ZT1 PLAN XY EXPORT", "ZT1 PLAN XY EXPORT_PATTERN_1", "ZT1 PLAN XY EXPORT_PATTERN_2"],
                "composites": ["ZT1 PLAN XY DXF"],
                "min": {"ZT1 PLAN XY EXPORT": (10, 10, 0)},
                "arcs_min": {"ZT1 PLAN XY EXPORT": 1, "ZT1 PLAN XY EXPORT_PATTERN_2": 1},
                "pitch_y": ["ZT1 PLAN XY EXPORT", "ZT1 PLAN XY EXPORT_PATTERN_1", "ZT1 PLAN XY EXPORT_PATTERN_2"]}},
    {"key": "ZT2", "title": "composite, plan + profile SECTION -> exact profile, no construction plane", "part": "hk",
     "stem": "ZT2 PROFILE",
     "params": [("b", "exportDxf", True), ("enum", "planSource", "ExportDxfSource", "PROJECTION"),
                ("enum", "profileSource", "ExportDxfSource", "PROJECTION"),
                ("enum", "profileOutline", "ExportProfileOutline", "SECTION")],
     "expect": {"status": "INFO", "sheets": ["ZT2 PROFILE XY EXPORT", "ZT2 PROFILE XZ EXPORT"],
                "composites": ["ZT2 PROFILE XY DXF", "ZT2 PROFILE XZ DXF"],
                "edges_max": {"ZT2 PROFILE XZ EXPORT": 12}, "faces_equal_sheets": True}},
    {"key": "ZT4", "title": "pattern 20 x 20 -> ERROR (limit)", "part": "belag", "stem": "ZT4 CAP",
     "params": [("b", "exportDxf", True), ("enum", "planSource", "ExportDxfSource", "PROJECTION"),
                ("b", "dxfPattern", True), ("int", "patternCountX", 20), ("int", "patternCountY", 20)],
     "expect": {"status": "ERROR", "message_has": "limit"}},
    {"key": "ZT5", "title": "same export name as ZT1 -> WARNING", "part": "ug", "stem": "ZT1 PLAN",
     "params": [("b", "exportDxf", True), ("enum", "planSource", "ExportDxfSource", "PROJECTION")],
     "expect": {"status": "WARNING", "message_has": "already uses the export name"}},
    {"key": "ZT6", "title": "Station table on, Export drawing off -> no station views", "part": "ug", "stem": "ZT6 GATE",
     "params": [("b", "stationTable", True)],
     "expect": {"status": "INFO", "absent": ["ZT6 GATE XY WIRES", "ZT6 GATE XY"]}},
    {"key": "ZT7", "title": "composite + 1st quadrant + shifted copy -> faces shift, WARNING", "part": "hk", "stem": "ZT7 COMP",
     "params": [("b", "exportDxf", True), ("enum", "planSource", "ExportDxfSource", "PROJECTION"), ("b", "shiftCopy", True)] + QUADRANT,
     "expect": {"status": "WARNING", "message_has": "cannot be copied", "sheets": ["ZT7 COMP XY EXPORT"],
                "min": {"ZT7 COMP XY EXPORT": (10, 10, 0)}}},
    {"key": "ZT8", "title": "selected faces (copied from Export_UG) -> a face, arcs kept", "part": "ug", "stem": "ZT8 SEL",
     "params": [("b", "exportDxf", True), ("enum", "planSource", "ExportDxfSource", "SELECTION"), ("copy", "planFaces", "Export_UG")],
     "expect": {"status": "INFO", "sheets": ["ZT8 SEL XY EXPORT"], "arcs_min": {"ZT8 SEL XY EXPORT": 1}}},
]
