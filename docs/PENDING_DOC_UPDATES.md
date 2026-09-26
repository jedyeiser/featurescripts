# Pending doc updates: code changes announced but not landed yet

When one of these is pushed, update the docs listed, then strike it here. The tracker (`docs/doc_status.py`) also
flags "code changed since written" once the source files change.

| Announced | By | Change | Docs to update |
|---|---|---|---|
| 2026-09-25 | featurescripts-d4 session | NEW Curve_tools feature **Recognize arcs** -- LANDED (tab 00e11280c6d08ef6fd29e547). Params: Edges or wires, Tolerance 0.01 mm, Max end tangent change 0.05 deg, Replace with arcs (on; off = report only), Name, Delete input, Highlight matches. Keys recognizedCount, candidateCount, arcEdges. Tests: 'Arc tangency tests' studio (0ee366a19b18c78097222cd0), build_/check_arc_tangency_tests.py 9/9 (R1-R5; also P12/P13 Evaluate profiles, M1/M2 Map curve) | DONE in docs 2026-09-26 (explainer 2.7, deck recognize_arcs; deck-only icon) |
| 2026-09-25 | featurescripts-d4 session | Driven edge offset / Driven offset surface: new parameter **Varying offset on arcs: Spline / Biarc fit** | driven_offset explainer + DEO / DOS decks (not written yet -- include from the start) |
| 2026-09-25 (Map curve LANDED) | featurescripts-d4 session | Map curve and DEO no longer emit an arc or line whose end tangents disagree with the true tangents (some edges change type) | DONE in docs 2026-09-26 (explainer 1.3, 2.2; Map curve deck tip). DEO part: with the driven_offset docs |
| 2026-09-25 (LANDED) | featurescripts-d4 session | Evaluate profiles **Efficient**: freeform edges merge only across tangent junctions (<= 0.57 deg), not chords within 45 deg | DONE in docs 2026-09-26 (explainer 2.4; deck dialog text + tip) |
| 2026-09-25 | tools-review agent | Clean wire keys trimmed (run1..N, curveCount, inputCount, maxDeviation) -- in the working copy, not pushed | DONE in docs (explainer 2.6, deck) |
| 2026-09-25 | tools-review agent | Offset+ corner-count keys removed -- in the working copy | DONE in docs (explainer 2.5, deck) |
