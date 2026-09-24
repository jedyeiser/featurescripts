"""Which feature file gets which icon: (project dir, feature .fs basename, icon name in icons/final/<name>_icon.svg)."""

TARGETS = [
    ("reference_side", "split_plus", "split_plus"),
    ("reference_side", "mutual_trim_plus", "mutual_trim_plus"),
    ("reference_side", "offset_plus", "offset_plus"),
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
]
