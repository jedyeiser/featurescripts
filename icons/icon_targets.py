"""Which feature file gets which icon: (project dir, feature .fs basename, icon name in icons/final/<name>_icon.svg)."""

TARGETS = [
    ("reference_side", "split_plus", "split_plus"),
    ("reference_side", "mutual_trim_plus", "mutual_trim_plus"),
    ("reference_side", "offset_plus", "offset_plus"),
    ("reference_side", "thicken_plus", "thicken_plus"),
    ("reference_side", "orient_to_reference", "orient_to_reference"),
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
    ("xSection", "features/GJ_Feature", "solve_gj"),
    ("gordonSurface", "scaledCurve", "scaled_curve"),
    ("gordonSurface", "pullSurface", "pull_surface"),
    ("example_1", "joinWires", "join_wires"),
    ("example_1", "extrudeEdge", "extrude_edge"),
    ("example_1", "refSurfCreation/offsetEdges", "offset_edges"),
    ("bodyRename", "Simple_Rename", "simple_body_rename"),
    # RESTYLES: these files already carry an (off-style) icon, so install_icons.py leaves them alone. Swap by hand:
    # replace the IconNamespace::import tab id/version with a NEW tab (the script matches tabs by name).
    ("footprint", "analyzeFootprint", "analyze_footprint"),
    ("footprint", "integrateFootprint", "integrate_footprint"),
    ("footprint", "scaleFootprint", "scale_footprint"),
    ("gordonSurface", "modifyCurveEnd", "modify_curve_end"),
    # composite_part_tools document created 2026-09-26.
    ("composite_part_tools", "composite_boolean", "composite_boolean"),
]
