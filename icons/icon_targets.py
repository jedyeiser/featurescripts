"""Which feature file gets which icon: (project dir, feature .fs basename, icon name in icons/final/<name>_icon.svg)."""

TARGETS = [
    ("feature_tree_note", "feature_tree_note", "feature_tree_note"),  # 2026-11-23 Jelte's icon
    ("reference_side", "split_plus", "split_plus"),
    ("reference_side", "mutual_trim_plus", "mutual_trim_plus"),
    ("reference_side", "offset_plus", "offset_plus"),
    ("reference_side", "thicken_plus", "thicken_plus"),
    ("reference_side", "orient_to_reference", "orient_to_reference"),
    ("reference_side", "move_face_plus", "move_face_plus"),  # 2026-09-26, wired by hand
    ("reference_side", "enclose_plus", "enclose_plus"),  # 2026-09-27, wired by hand
    ("reference_side", "join_profile_surfaces", "join_profile_surfaces"),  # 2026-09-27, wired by hand
    ("reference_side", "referenced_part", "referenced_part"),  # 2026-09-28, wired by hand
    ("reference_side", "patch_plus", "patch_plus"),  # 2026-11-23 (candidate A: a block standing on a surface)
    ("variable_tools", "extract_variables", "extract_variables"),
    ("curve_tools", "clean_wire", "clean_wire"),
    ("curve_tools", "map_curve", "map_curve"),
    ("curve_tools", "merge_curve", "merge_curve"),
    ("curve_tools", "evaluate_profiles", "evaluate_profiles"),
    ("curve_tools", "fillet_wire", "fillet_wire"),
    ("driven_offset", "create_offset_profile", "create_offset_profile"),
    ("driven_offset", "driven_edge_offset", "driven_edge_offset"),
    ("driven_offset", "driven_offset_surface", "driven_offset_surface"),
    ("solvers", "iterative_solve", "iterative_solve"),
    ("solvers", "Part_Vol", "part_volume"),
    ("smallTools", "Move_Along_Edge", "move_along_edge"),
    # 2026-09-25 KEY-tools batch (drafts: make_drafts.py; crosscut.md section 6). Not installed yet.
    ("footprint", "arcFit", "arc_fit"),
    ("footprint", "getFootprintPoints", "footprint_points"),
    ("xSection", "features/generateBaseline", "generate_baseline"),
    ("xSection", "features/xSect", "ei_cross_section"),
    ("gordonSurface", "scaledCurve", "scaled_curve"),
    ("gordonSurface", "pullSurface", "pull_surface"),
    ("example_1", "joinWires", "join_wires"),
    ("example_1", "extrudeEdge", "extrude_edge"),
    ("example_1", "refSurfCreation/offsetEdges", "offset_edges"),
    ("bodyRename", "Simple_Rename", "simple_body_rename"),
    ("clean_surfaces", "clean_surfaces", "clean_surfaces"),  # 2026-11-06 (concept A)
    ("query_variable_x", "query_variable_x", "query_variable_x"),
    ("multi_mate_connector", "multi_mate_connector", "multi_mate_connector"),  # 2026-11-19 (candidate B, agent draft)
    ("multi_mate_connector", "mate_connector_plus", "mate_connector_plus"),
    ("multi_mate_connector", "bridging_curve_plus", "bridging_curve_plus"),  # 2026-11-19 (candidate A, agent draft)  # 2026-11-19 (candidate A, agent draft)  # 2026-11-18 (candidate A, bolt behind the lens)
    ("evenly_divide_surface", "evenly_divide_surface", "evenly_divide_surface"),  # 2026-10-17 (concept A2, agent draft)
    # RESTYLES: these files already carry an (off-style) icon, so install_icons.py leaves them alone. Swap by hand:
    # replace the IconNamespace::import tab id/version with a NEW tab (the script matches tabs by name).
    ("footprint", "analyzeFootprint", "analyze_footprint"),
    ("footprint", "integrateFootprint", "integrate_footprint"),
    ("footprint", "scaleFootprint", "scale_footprint"),
    ("gordonSurface", "modifyCurveEnd", "modify_curve_end"),
    # composite_part_tools document created 2026-09-26.
    ("composite_part_tools", "composite_boolean", "composite_boolean"),
    # publish_tools, 2026-09-28 (user picked definition B, geometry A; drafts icons/drafts/publish_tools/).
    ("publish_tools", "station_definition", "station_definition"),
    ("publish_tools", "station_geometry", "station_geometry"),
    ("publish_tools", "station_table", "station_table"),  # 2026-09-28, table annotation wired by hand (user picked B)
    ("publish_tools", "primitive/export_primitive", "export_primitive"),  # 2026-09-28, user picked draft B
    ("publish_tools", "primitive/primitive_table", "primitive_tables"),  # 2026-09-28, draft A; table annotation wired by hand
    ("publish_tools", "layout/auto_layout", "auto_layout"),  # 2026-10-28, packed boxes + blue origin dot
    ("publish_tools", "export/export_tag", "export_tag"),  # 2026-10-28, tag outline + blue hole and line
    ("curveMapping_public", "deformTag", "deform_tag"),  # 2026-11-11, grey From line, blue To curve, dashed links
    ("curveMapping_public", "bodyIdentity", "body_identity"),  # 2026-11-12, grey box with a blue anchor dot
]

