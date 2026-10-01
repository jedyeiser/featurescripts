# Archived docs (2026-09-30)

Moved out of the working tree so agents stop reading them by default. Nothing was deleted or edited; `git log --follow`
finds the history. Read these only to understand WHY something was built a certain way, never as current instructions:
most predate the FeatureScript 3070/3083 work and state 2878-era facts (std has 264 files, not 12 or 33).

| was | now | why archived |
|---|---|---|
| PROJECT.md, TOOLS_READY.md, IMPLEMENTATION_SUMMARY.md, WAIST_SOLVER_FIX.md | root/ | January 2026 overview and status; superseded by CLAUDE.md (index), driven_offset/DOCUMENT_MAP.md and .claude/featurescript-corrections.md |
| .claude/QC-Table-*.md, qc-table-*.md, xsection-polish-implementation-summary.md | claude_notes/ | February qcTable / xSection write-ups; the code and correction log carry the rules |
| .claude/bspline-knots-fix.md, coordinate-space-fix.md, parameter-visibility-fix.md, strict-arcs-fix.md | claude_notes/ | February one-off fix notes; each fix is in the code, its rule in the corrections log |
| curveMapping/DESIGN.md, FRENET_FRAME_USAGE.md, G1_IMPLEMENTATION_NOTES.md, approach.txt | curveMapping/ | Feb-Mar design docs; current behaviour: docs/explainers/curve_mapping and memory topic `curvemapping` (docs/briefs/curve_mapping.md still cites two of these by name) |
| driven_offset/extract_variables_schema.md (older duplicate) | driven_offset/extract_variables_schema_older_copy.md | duplicate of variable_tools/extract_variables_schema.md, missing the Region type and Show option; the original path is now a 3-line pointer |

Stale facts in these files that were corrected elsewhere (do not reintroduce): FS target 2878 only; "std has 12/33
modules" (264); "tools/ has 14 files" (13, no tools/README.md); PROJECT.md "footprint, gordon, xSect_EI only".
