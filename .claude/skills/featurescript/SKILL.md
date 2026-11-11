---
name: featurescript
description: Rules and workflow for writing or editing Onshape FeatureScript (.fs) in this repo. Use before writing or changing any .fs file, FeatureScript feature dialog, query, op*/ev* call or custom table. Loads the critical corrections-log rules, style rules and the fscheck/push workflow.
---

# FeatureScript in this repo

Read this, then open only the corrections-log entries for your topic. Do NOT read the whole
`.claude/featurescript-corrections.md` (~1800 lines): read its "Quick index" table and "Critical top 10"
(lines ~11-140), then jump to the entry line numbers in the index that match your task.

## Critical top 10 (corrections log)
1. Braces on every if/else/for/while, even one-liners.
2. A new parameter's annotation "Default" migrates into every saved feature: make it reproduce OLD behaviour (lengths arrive in mm).
3. Selection "Filter" is a literal expression in every annotation (never a const); every Query parameter has a Filter.
4. Declare each parameter id once across the whole precondition (branches and array items included).
5. Every declaration goes after the LAST import line (a Namespace::import is only for icon blobs, `Name::BLOB_DATA`).
6. Precondition visibility ifs: enum/bool/==/&&/|| only; no < > <= >=.
7. Never call getProperty in a feature body or its error paths; read it in editing logic.
8. FeatureList functions go in a pattern frame on a prefix id, never inside startFeature.
9. After a push over pin chains, GET the tab and compare; never trust "pushed"/"skipped".
10. Never add a parameter to a published custom table; placed tables must be re-inserted.

## Style rules (hard requirements)
- ASCII only in code, comments, strings (no en dash, smart quotes, accents). No UTF-8 BOM.
- Never name a variable `box line plane transform type array map string number boolean function undefined`; use `bb tl pl xf`. Dot access on a reserved key (`m.type`) fails: use `m["type"]`.
- try/catch only in an emergency (hides root causes); validate upstream.
- UIHint lists are arrays; bound specs are `{ (unitless) : [min, default, max] } as IntegerBoundSpec`.
- Every vertex/point pick accepts mate connectors: `EntityType.VERTEX || BodyType.MATE_CONNECTOR`.
- Notices: did the user get what they asked for? yes -> reportFeatureInfo or nothing; partly -> reportFeatureWarning (feature shows as failed in the tree); no -> regenError.
- No `println` unless the user turned on a Debug/print option (fscheck lints this).
- Export by default in utility files; no `import ... as`; vectors carry units, directions are unitless; prefer native op*/ev* before custom code.

## Workflow
1. Write the .fs locally. Use the std version already at the top of the file (std 2878 mirror is local; active docs are on 3070+, so a newer std function shows UNDEFINED in fscheck: confirm it exists before changing code).
2. `python fscheck.py <all tabs of the document>`. A PostToolUse hook already runs it on the edited file's folder after each edit and reports findings.
3. Push: `python -m sync.main pushproject <project> --files <f.fs> --check`.
4. Live checks: use the `onshape-live-runner` agent (call-capped). Mind the API budget in CLAUDE.md.
5. New lesson learned: add an entry to the corrections log.
