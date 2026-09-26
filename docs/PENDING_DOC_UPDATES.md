# Pending doc updates: code changes announced but not landed yet

When one of these is pushed, update the docs listed, then strike it here. The tracker (`docs/doc_status.py`) also
flags "code changed since written" once the source files change.

| Announced | By | Change | Docs to update |
|---|---|---|---|
| 2026-09-25 | featurescripts-d4 session | NEW Curve_tools feature **Recognize arcs** (`curve_tools/recognize_arcs.fs`) | add to `docs/status.json` (deck + Curve tools explainer section), icon? |
| 2026-09-25 | featurescripts-d4 session | Driven edge offset / Driven offset surface: new parameter **Varying offset on arcs: Spline / Biarc fit** | driven_offset explainer + DEO / DOS decks (not written yet -- include from the start) |
| 2026-09-25 | featurescripts-d4 session | Map curve and DEO no longer emit an arc or line whose end tangents disagree with the true tangents (some edges change type) | Curve tools explainer 1.3 / 2.2, Map curve deck |
| 2026-09-25 | featurescripts-d4 session | Evaluate profiles **Efficient**: freeform edges merge only across tangent junctions (<= 0.57 deg), not chords within 45 deg | Curve tools explainer 2.4 (Output table), Evaluate profiles deck (dialog + example text) |
| 2026-09-25 | tools-review agent | Clean wire keys trimmed (run1..N, curveCount, inputCount, maxDeviation) -- in the working copy, not pushed | DONE in docs (explainer 2.6, deck) |
| 2026-09-25 | tools-review agent | Offset+ corner-count keys removed -- in the working copy | DONE in docs (explainer 2.5, deck) |
