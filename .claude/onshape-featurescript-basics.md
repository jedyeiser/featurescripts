# Onshape / FeatureScript basics (condensed research, 2026-10)

Tags: [doc] official docs or local std; [forum] forum or staff post; [repo] project's own notes/tests; [inf] inferred, untested.
API budget, workflow, fscheck, sync tools: see CLAUDE.md (not repeated here). No live testing was done for this file.

## 1. Regeneration and recovery
- Editing a Part Studio re-runs the changed feature and every feature after it; earlier ones do not [doc]. No finer caching documented [inf].
- Committing a Feature Studio, then viewing a Part Studio that uses it, triggers regen [doc]. Profile info reflects COMMITTED text only [doc].
- Same-document/workspace imports update at once and regen every dependent studio in that workspace [doc]. Other-document imports stay fixed until re-imported [doc].
- A bad library edit cannot break a consumer pinned to an old version; a bad same-document tab edit hits all studios of that workspace [inf].
- "Too many steps: possible infinite loop" is a thrown exception from the interpreter [forum]. Likely behaves like any feature error (red feature, dependents fail, list still loads) [inf].
- Wall-clock regen timeout ~10 min, whole studio; the studio fails to load and shows an error code [forum, Onshape staff]. Splitting work over several feature instances does not help; the time sums [forum].
- Studio is shared state, so all users see the same failed regen [inf].
- Recovery when a studio will not load, in order:
  1. Versions and history: pick an entry from before the problem, View it, restore to main [forum].
  2. Roll back to a change before the timeouts began (staff advice) [forum].
  3. If it opens: grey bar = rollback state; right-click > "Roll to end". Moving the bar before the bad feature, or suspending it, prevents it running [forum; suspend is standard UI, inf].
  4. Fix the code: edit the Feature Studio and commit; next open regenerates with the fix. Works only if the Feature Studio tab opens. Pinned (cross-doc) studios need a re-import at a new version [inf].
  5. Create workspace from a version BEFORE the bad change (copying a bad state keeps the bad feature) [inf].
  6. Large count parameter as trigger: only history restore works (cannot edit without opening) [inf].
  7. Support ticket with the document link [forum].
- No REST route avoiding regen found. featureStates is lazy (trust only after a regen); eval and featurespecs endpoints regenerate [repo]. History/version restore endpoints exist but were not verified [inf].

## 2. Limits and performance
- Step limit: NO numeric value anywhere (docs, forum, local std grep) [doc-absent]. Scope (per feature vs per regen) unknown. Counts interpreter work, probably not kernel op*/ev* time [inf].
- Conflict: regen note says 10 min is staff-stated; limits note calls it an anecdote. Treat as ~10 min, unofficial.
- Time data point: geodesic dome, 120 edges ~3 min OK, 240 edges ~5.7 min OK, fails near 10 min [forum].
- Recursion: "Maximum call stack depth exceeded" at ~1500 calls (anecdotal); keep under 1000, prefer loops [forum].
- Import warning in Performance panel above 600 imported modules; do not import several std versions [doc].
- Staff advice for step errors: take selected parts not all, batch, bisect, split the studio, use feature patterns [forum].
- Costly constructs:
  - append(arr,v) is @resize(arr,size+1,v); mapArray/filter add one lambda call per element [doc std/containers.fs]. Staff: a series of appends beats x[i]=...; keep `arr = append(arr, x)` [forum]. Alt: makeArray(max), write by counter, resize [forum].
  - concatenateArrays is native, fast [doc].
  - sort() is merge sort in FS, O(n log n) lambda calls; precompute keys, sort [key,item] pairs of plain numbers [doc/inf].
  - ValueWithUnits ops build and typecheck a map each time; far costlier than plain numbers [inf].
  - isIn(x,arr) is linear (containers.fs:59); use a map for sets/memo [doc/inf]. String `~` is O(len) per use, avoid in hot loops incl. println [inf].
  - removeIndex does not exist in the std mirror; use filter or loop+append [doc].
  - std precondition wrappers are FS code and cost steps; avoid calling tiny std helpers millions of times [inf].
  - addDebugLine/addDebugPoint each solve a sketch: 758 calls = +2.17 s [repo].
- Patterns that stay under budget [inf unless noted]:
  1. Strip units once (`v / meter`), loop on numbers, rewrap at the end.
  2. Hoist evaluateQuery, evCurveDefinition, evaluateSpline out of loops; one evaluateSpline with many params.
  3. One opBoolean with many tools, not N booleans.
  4. Early return/break; bisect or knot-span lookup instead of scanning.
  5. Replace O(n^2) membership with maps; sort + sweep instead of nested loops.
  6. Memoize pure functions in a map passed explicitly (no globals).
  7. Profile first: staff say custom features can be profiled "right down to the line" in the Feature Studio profiler [forum].
- try / try silent [doc exceptions.html]:
  - `try (expr)` gives undefined on exception; `try {}` aborts the block at first throw; `catch (e)` handler is not protected; `throw e;` rethrows outward.
  - Catches errors from called functions, incl. op*, ev*, std preconditions.
  - Every exception, caught or not, is reported to notices; `try silent` suppresses that report (both forms). A silently swallowed error leaves NO trace: use catch (e) to record, or check for undefined.
  - A failed op* may leave partial context state and feature status stays OK unless you call reportFeatureWarning/regenError [inf]. reportFeatureWarning turns feature orange/red but it regenerates; endFeature thrown inside try then caught can double-abort [forum].

## 3. Debugging
- Compile notices (tab:line:col) show in the document-wide FeatureScript notices pane, one set per tab, streaming ~1 s late [repo]. "Function X not found" is rated WARNING but fatal at run [repo].
- Monitor (regen from notices pane): runtime notices with cross-module stack traces, println output, `Result:` lines [repo]. println/print go to the console stream [doc std/string.fs:61-79].
- reportFeatureInfo text does NOT appear in Monitor output; it is a feature status on the feature [repo; semantics doc std/error.fs:280-307].
- REST has no compile diagnostics; featurespecs returns [] on a broken studio [repo]. featureStates = status only, no text [repo].
- Output absent when: the code never ran (throw earlier, suppressed/rolled-back feature, unreached path) [inf]; selection resolved to nothing ("No edges found in the selection", no prints arrived) [repo]; studio will not compile (only compile notices) [inf]; non-default configuration (eval and --monitor use DEFAULT config; pass signed query param `configuration=List_xxx=Value;...`, body is ignored) [repo].
- Debug flags: boolean dialog parameters ("Print from chain", "Print surface") must be ON in the feature dialog; ask the user to leave them on, do not toggle via API [repo].
- regenError: msg ASCII only, >200 chars not fully shown; (msg, ["paramId"]) highlights params; (msg, query) highlights entities, only while the dialog is open [doc std/error.fs:15-159].
- reportFeatureError does not throw and rolls back a top-level feature at finish; use `throw` to stop. reportFeatureWarning/Info keep the result [doc error.fs:209-307]. featureHasError/getFeatureError read status back (useful in try) [doc].
- setErrorEntities(context,id,{"entities":q}) shows entities red even if they are rolled back; best for a failed intermediate body [doc error.fs:469-479].
- debug(context,value[,color]) / addDebugEntities draw red geometry ONLY while that feature is being edited [doc std/debug.fs:23-56].
- Safe bisecting:
  1. fscheck first (0 API calls) [repo].
  2. `stopAfter` integer/enum parameter: return after stage N, println counts before each op; first failing N names the culprit [inf].
  3. Wrap a suspect stage in try silent/try, degrade to reportFeatureWarning; setErrorEntities before a throw [inf].
  4. Reproduce kernel refusals via eval API: ONE anonymous `function(context is Context, queries){...}`, no helper functions, builds geometry in a discarded context, fresh newId() per variant, many variants = 1 call [repo]. Eval accepts only lambda expressions [doc].
  5. Test unversioned library code in a scratch document copy (ask first) [repo].
  6. Never isolate by editing the user's live feature parameters (lost-selection incident) [repo].

## 4. Platform
- Versions and per-edit microversions are immutable; writes (POST) go to a workspace [doc api-intro].
- import(path, version): same-document `path` = tab element id; other-document `path` = `docId/versionId/elementId`, fixed to that VERSION; blue link icon = newer version exists [doc imports.html].
- Same-document `version` string is a microversion hash; empty and commit lets Onshape fill the latest and keep it updated [forum 2499]. Onshape re-pins a written tab's same-document imports to the callees' current microversions; an old pin stays valid, so a pin freezes callee code until re-pinned [repo DOCUMENT_MAP.md, repin.py].
- Consequence: a callee change reaches a caller after its pin moves (same doc) or after a new VERSION exists and the pin moves (cross doc). Only the user creates versions [repo].
- Derived feature: carries parts, part studios, surfaces, curves, sketches, planes, active sheet metal, optionally mate connectors and metadata; one-way link [doc derived.htm]. Same doc: reference WORKSPACE (instant) or VERSION (manual update, recommended for performance); other doc: VERSION only, blue link when newer [doc].
- Feature Studio changes: same-workspace feature instances follow the latest tab; regen happens when the Part Studio is next rebuilt/opened [repo]. Cross-document: consumer is tied to a version and gets an upgrade prompt; custom feature stays linked to the std version it was created with [doc snippet].
- New parameter annotation default is migrated into saved instances [repo corr. 25]. A feature cannot be moved between elements/documents by API ("Feature does not match"): insert new, delete old, re-point references [repo].
- REST quota (company pool, pro-rated per seat) [doc auth/limits]: Enterprise 10,000 per Full User; Professional 5,000 per user; EDU student/free/standard 2,500 per user; EDU Enterprise 10,000 per enterprise; EDU educator/discovery 2,500 per company.
  - Over limit: HTTP 402. Per-endpoint 429 limits exist, values unpublished; header X-Rate-Limit-Remaining shows remaining [doc].
  - Counts 2xx/3xx for API-key calls, private OAuth apps (charged to owner), Explorer with key/OAuth. Free: public App Store apps, browser/mobile traffic, Explorer with session login, webhooks, any 4xx/5xx [doc].
  - So browser notice scraping is not quota (check_project REST calls are) [inf]. Do not blindly retry 504s that may have landed [inf]. Forum quotes older/different numbers (20,000/yr/company); trust the table [forum].
- Conflict: CLAUDE.md says "per user per year"; docs say pooled at company level.
- Variable Studio: GET /api/v10/variables/d/{did}/w/{wid}/e/{eid}/variables [repo]. Feature Studio create ignores `contents` [repo].
- "Open in another session": no doc found. Likely same account in another tab/device; closing it clears it [forum 59640]. Never click "Open" on that prompt; never write features while the user is in the document; closing a browser mid feature dialog SAVES the edit (stray features) [repo].

## Unverified / needs a live test
- Numeric step limit; per-feature vs per-regen scope; whether kernel ops cost steps.
- Behaviour at step/time limit: red feature vs failed load; whether earlier prints survive.
- Whether the 10 min timeout is official; stale-session effects on other users.
- Whether a studio re-regenerates on open, and microversion pin semantics beyond forum/repo evidence.
- Any REST route to restore a non-loading studio without regen (and its quota cost).
- Derive and variables: whether Variable Studio variables carry through derive.
- Exact Feature Studio upgrade-prompt behaviour; Variable Studio help page.
- Official Monitor pane docs (debugging.html 404); setHighlightedEntities use; 429/402 backoff guidance; meaning of "open in another session".
