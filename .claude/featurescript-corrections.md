# FeatureScript Corrections Log

This document tracks corrections needed to LLM-generated FeatureScript code. It serves as a living knowledge base to improve future code generation by learning from past mistakes.

**Purpose**: Reference this log before generating FeatureScript code to avoid known issues and follow corrected patterns.

---

## Quick index

Added 2026-09-30. One row per entry; `line` = the heading's line in this file. Read this table and the Critical top 10
first, then open only the entries whose topic matches your task. Entry bodies below are unchanged.

| # | topic tag | RULE | line |
|---|---|---|---|
| Precond dup params (2026-09-11) | params-preconditions | Declare each parameter id once for the whole feature, including array items; combine branch conditions. | 143 |
| Boolean op enum | ops | Use BooleanOperationType.UNION/SUBTRACT/INTERSECT; BooleanType does not exist. | 162 |
| opReplaceFace | ops | Set oppositeSense = dot of face normals at UV (0.5,0.5) < 0. | 173 |
| BSpline splitCurve | splines-nurbs | After refinement, take CPs A with i < splitStart and CPs B from splitEnd - degree. | 190 |
| Function Call Syntax | syntax-strings | Pass std functions one named-parameter map; evaluateSpline "parameters" must be an array. | 214 |
| Import Issues | imports-pins | Never alias imports with name::; exports are global, so keep names unique (icons excepted, 35). | 249 |
| Type System | syntax-strings | Predicates cannot hold standalone var; use a boolean-returning function instead. | 296 |
| Geometry Operations (template) | geometry-frames | Vectors carry units; direction vectors are unitless and normalized. No real entry. | 367 |
| Query Patterns (template) | queries-ids | Start from qEverything, chain filters, cache evaluations. No real entry. | 392 |
| Units Handling (template, 1st) | units | Template only; the real content is the second Units Handling section (next Units Handling row). | 417 |
| API Usage | splines-nurbs | evaluateSpline returns result[derivOrder][paramIdx]; a single point is [0][0]. | 442 |
| Tolerance and Numerical Stability | numerics | Never test floats for equality; use TOLERANCE.* (unitless) against abs(value). | 485 |
| FeatureScript Syntax (8 sub-rules) | syntax-strings | Quote map keys that clash and all op*/ev* keys; id + (i ~ "x"); always braces; annotate every parameter; export. | 509 |
| Instructions for Use | testing | Process text only: read this log before generating code, add entries after errors. | 831 |
| Matrix and Array Indexing | numerics | CLT Q is 3x3; shear Q66 is Q[2][2], not Q[5][5]. | 841 |
| Units Handling (2nd; incl. no pow) | units | Derive unit factors by dimensional analysis, never guess; use ^ not pow(); triangle Jp needs six cross terms. | 882 |
| Reserved Variable Names | syntax-strings | Never name variables box, line, plane, transform, or type names; use bb, tl, pl. | 981 |
| addDebugLine cost | testing | Never call addDebugLine/addDebugPoint per sample; stride to about 60 markers or batch in one scope. | 1009 |
| Reserved words in map access | syntax-strings | Never read a keyword key with dot access (m.type); use another key name or brackets. | 1022 |
| Annotations ASCII only | syntax-strings | Use printable ASCII only in annotation strings; no en dash, minus, ellipsis, smart quotes. | 1050 |
| 16 | dialog-ui | Every Query parameter needs a Filter; sketch geometry is FACE/EDGE; unique ids; "Default" only serves new instances. | 1068 |
| 17 | imports-pins | common.fs omits ProjectionType and queryVariable; import those modules explicitly. | 1095 |
| 18 | queries-ids | Do not enumerate query variables; getAllVariables omits them and a selection yields a value, not a name. | 1119 |
| 19 | ops | Filter opCreateCompositePart members to SOLID and SHEET; mate connectors are rejected. | 1145 |
| 20 | params-preconditions | Precondition visibility ifs allow enum/bool/==/!/&&/|| only; never > < >= <=. | 1164 |
| 21 | splines-nurbs | Fit corresponding curves under identical constraints; check CPs >= degree+1 and knots = CPs+degree+1. | 1206 |
| 22 | splines-nurbs | If opLoft refuses compatible B-splines, build the ruled surface via opCreateBSplineSurface (degree 1 across). | 1244 |
| 23 | splines-nurbs | Pass chord-length "parameters" to approximateSpline or end-derivative magnitudes are ignored; keep spacing even. | 1284 |
| 26 | geometry-frames | Keep the measuring axis and the point's true velocity as separate vectors in a frame. | 1322 |
| 24 | splines-nurbs | Split a ruled B-surface at every full-multiplicity interior knot; the kernel refuses creased surfaces. | 1342 |
| 25 | params-preconditions | A new parameter's annotation "Default" must reproduce OLD behaviour; it migrates into every saved feature. | 1361 |
| 27 | dialog-ui | Use isButton plus the 8th editing-logic arg clickedButton; never test a button's value or put it in defaults. | 1386 |
| 28 | geometry-frames | Decide "which side" against the splitter's signed distance, not distance to the pieces. | 1413 |
| 29 | splines-nurbs | Pass bSplineSurface weights as matrix([[..]]); degenerate rows are fine; edge vertices via AdjacencyType.VERTEX. | 1434 |
| 30 | params-preconditions | Never use isVolume/isArea as dialog parameters; use isReal in a stated unit and multiply in the body. | 1452 |
| 31 | ops | Never call FeatureList functions inside startFeature; push a pattern frame on a prefix like applyPattern. | 1468 |
| 32 | syntax-strings | getVariable with undefined default still throws; use a sentinel default (undefined means absent). | 1491 |
| 33 | dialog-ui | Write selection "Filter" as a literal expression in every annotation, never a const. | 1505 |
| 34 | queries-ids | After opSplitFace use untouched faces plus both qSplitBy sides; qCreatedBy FACE is empty. | 1519 |
| 35 | imports-pins | Feature icons are the one allowed Name::import of an SVG blob tab; use Name::BLOB_DATA. | 1535 |
| 36 | ops | Never call getProperty in a feature body (even in error messages); read it in editing logic. | 1550 |
| 37 | ops | For a composite part pass members (SOLID/SHEET plus qFlattenedCompositeParts) to opCreateOutline. | 1562 |
| 38 | rest-sync | REST-inserted array items must carry every item parameter; custom enum namespace is the feature studio's. | 1573 |
| 39 | splines-nurbs | Build arcs that must stay arcs as sketch arcs; never evaluateSpline a rational B-spline. | 1585 |
| 41 | ops | Keep the frame pushed for the whole replay; retry outside it only on SELF_INTERSECTING_CURVE_SELECTED. | 1600 |
| 40 | ops | Custom feature with std boolean step needs export import of onshape/std/tool.fs; delete helper bodies first. | 1627 |
| Statistics (stale) | testing | Ignore this block (says 40 corrections, 2026-09-24); see Statistics below. | 1638 |
| 42 | geometry-frames | Never take "the" closest point of a body; evDistance ties are arbitrary; project a defined point. | 1652 |
| 43 | dialog-ui | Add UIHint.SHOW_LABEL to every enum parameter or the dropdown has no caption. | 1664 |
| 44 | queries-ids | Resolve mate-connector selections to the connector body via qOwnerBody; never name connectors. | 1672 |
| 45a | ops | Cut multi-edge wires with opSplitEdges at arc-length parameters, not planes; compare with TOLERANCE.zeroLength * meter. | 1684 |
| 46a | params-preconditions | Use a distinct parameter id per precondition branch, even when only one branch is active. | 1696 |
| 45b | rest-sync | After pin chains, write tab contents directly, GET and compare; never trust "pushed"/"skipped". | 1707 |
| 46b | splines-nurbs | Never assume clamped kernel curves; get ends from [knots[p], knots[size-1-p]], not first/last CP. | 1721 |
| 47 | rest-sync | Correctness beats stable op ids; after pushes, check and report downstream features that lost references. | 1732 |
| 48 | rest-sync | A saved feature's namespace cannot change via REST; replace the old tab with a re-export shim. | 1745 |
| 49 | imports-pins | Push tabs whose cross-document pin you bumped by direct write; repin.py adopt_remote_pins can revert them. | 1765 |
| 50 | ops | Never store FeatureList functions in variables; the replaying feature owns its frame; pass outside refs via case inputs. | 1782 |
| 51 | dialog-ui | Write parameter "Filter" inline, not as a const; fscheck misses it, so run pushproject --check. | 1833 |
| 52 | queries-ids | Producers publish untracked entities; the consumer chooses tracking (startTracking) after merges. | 1841 |
| 53 | syntax-strings | No < on strings, no (?i) or ${ in regex/string literals; order by number. | 1858 |
| 54 | drawings-tables | Views need includeWires; reference edges by deterministicId; add a filter parameter to custom tables. | 1869 |
| 55 | queries-ids | Split a history query or qUnion([targets, startTracking(targets)]); an evaluated target is lost. | 1880 |
| 56 | numerics | Use cancellation-free quadratic roots (q = -(B + sign(B)*sqrt(disc))/2; q/A, C/q). | 1897 |
| 57 | geometry-frames | Never take curvature from opCreateOutline output; use the body's own edges via opExtractWires. | 1915 |
| 58 | imports-pins | importDerived skips sketch-attached mate connectors; add a BodyType.MATE_CONNECTOR query, pick by position. | 1925 |
| 59 | numerics | Solve for a named point (x = x_FCP) exactly; never snap to the nearest sample. | 1933 |
| 60a | ops | Extract sketch-text wires from the SHEET body's edges, not from all sketch edges. | 1945 |
| 61a | geometry-frames | Unwrap by the point's own base foot; parenthesise ~ inside != comparisons. | 1958 |
| 60b | ops | Every feature-function call under a foreign id must be inside a pattern frame; the executor owns the list. | 1974 |
| 62 | params-preconditions | A new length parameter's default migrates in mm; use a mm bound spec or isReal under a fresh id. | 1998 |
| 61b | rest-sync | Migrate features by inserting at the old index and copying subFeatures; pass rollbackBarIndex explicitly. | 2013 |
| 63 | splines-nurbs | Add in-between curve points to approximateSpline samples; measure fits between samples, not at them. | 2034 |
| 64 | drawings-tables | Never add a parameter to a published custom table; placed tables are not migrated. | 2047 |
| 65 | imports-pins | Put every declaration after the LAST import line, including Namespace::import. | 2061 |
| 66 | numerics | Use VolumeAccuracy.HIGH for any reported or compared volume; compare to 6 digits at most. | 2077 |
| 67 | rest-sync | Pass rollbackBarIndex as a QUERY parameter to the eval API, never in the JSON body. | 2088 |
| 68 | splines-nurbs | Fit adaptive-row curves to 0.5 um with midpoint samples; 1e-7 m fits interpolate and ring. | 2097 |
| 69 | splines-nurbs | opCreateBSplineCurve needs G1 joints; triple interior knots are fine; arcs as rational Beziers. | 2112 |
| 70 | numerics | Sample face departure on a trim with a 13x13 grid, not 5x5. | 2125 |
| 71 | geometry-frames | Make an overshoot part of the chain's own curve, at least 4x the end's distance from the other chain. | 2136 |
| 72 | testing | Verify rebuilds in both directions (reverse and forward); a reverse check misses missing features. | 2148 |
| 73 | ops | Sheet union sews gaps up to 1 um and fails entirely from 2 um; fit supports to 0.1-0.5 um. | 2157 |
| 74 | numerics | Warm-start Newton feet along a row only; seed cold (undefined) after any large jump. | 2168 |
| 75 | splines-nurbs | Fit closed curves periodic (opFitSpline, last = first); clamped closed splines are refused; use wire-body edges. | 2177 |
| 76 | geometry-frames | Take a face's trim parameter range from its edges before gridding; the parameter box can be huge. | 2190 |
| 77 | geometry-frames | Project a LINE to-edge with opPolyline to keep an exact line; give seam checks a 1 um tolerance. | 2197 |
| 78 | ops | opCreateOutline refuses faces (INVALID_INPUT): opExtractSurface each face; lift a planar face parallel to the plane instead of outlining. | 2216 |
| 79 | ops | A trimmed opPlane section stays a construction plane ("Plane 1" child): opExtractSurface its face and delete the plane. | 2224 |
| 80 | ops | Wires: no boolean union (BOOLEAN_INPUTS_NOT_SOLID); opExtractWires joins only end-to-end chains; group many curves in a CLOSED composite. | 2232 |
| 81 | dialog-ui | Declare a group's Driving Parameter (hidden boolean) immediately BEFORE the group annotation, not inside it. | 2239 |
| 82 | rest-sync | The sync matches a tab by NAME: a different local file name creates a second Feature Studio; Playwright notices pane reads compile notices. | 2245 |
| 83 | testing | getFeatureError/Warning/Info in one eval read a feature's text; eval needs makeId, match().hasMatch, rollbackBarIndex as query param. | 2252 |
| 84 | ops | Calling a custom feature from another: export import its tab, pass every parameter, pin and re-pin; move absolute inputs (stations) with a frame. | 2259 |
| 86 | queries-ids | A "created by <feature>" query variable also returns that feature's sketch construction wires; filter with qSketchFilter (not qConstructionFilter) before opExtractWires. | 2283 |
| 87 | dialog-ui | A WARNING status is drawn as an error in the feature tree; report a handled fallback that produced the intended result as INFO. | 2294 |
| 88 | notices | A feature never println's unless the user turned on a Debug / print option; a fit or build problem the user must see goes through reportFeatureWarning, never a gated print. | see entry |
| 89 | ops | A union of sheets that do not touch raises NO error; qCreatedBy(id, BODY) is empty after a union (the result is the first tool's body). Ask the new faces' edges for naked edges to know whether it sewed. | see entry |
| 90 | eval-api | An eval at the end of the tree includes the user's later features (use rollbackBarIndex); heavy evals need FS_SYNC_TIMEOUT; qAdjacent(edge, FACE) counts faces of every body; range(a, b) includes b; box is reserved. | see entry |
| 91 | performance | "Too many steps" is the per-FEATURE step limit: a println per face, or refinement of big faces, breaks a 193-face deform; there is no profiler, so measure by experiment. A first-come point budget starves late faces and stops the faces that miss their accuracy. | see entry |
| 92 | geometry-frames | A fill meets its neighbours (G0) but creases against them; a creased sheet cannot be offset or thickened (measured: lid 44 edges over 1 deg, source 1). Use G1 fills (edgesG1) and measure the angle across every edge. | see entry |
| 93 | ops | A tagging feature (setAttribute only): one attribute NAME per entity means one tag, so write an ARRAY of tag maps and replace by (group, role); FS cannot read a mate connector's owner or name, so ownership is picked by the user and ids go to the API; omit 0-means-default options instead of writing 0. | see entry |
| 94 | ops | Never unite two SOLIDS that meet in a face on real surfaces (BOOLEAN_INVALID, plane faces with micron-scale outlines, slivers from edges that cross the plane by 0.1 um): mirror and join the SHEETS (opPattern + sheet opBoolean UNION), thicken once, check the seam with naked (ONE_SIDED) edges lying in the plane. A one-sided cap/periphery surface is mirrored and joined the same way. | see entry |

| 95 | thicken-draft | A new std enum can be missing from `common.fs` (ExtendEndType, ExtendSheetShapeType): the eval API sees all of std and passes, the feature fails with a generic REGEN_ERROR; read the notices pane. A drafted thicken = grown seed slab + ruled wall cutters, joined per chain, split one chain at a time (convex only); tracked caps pick up the faces walls cut, so read caps by signed distance. | see entry |
| 96 | patch-plus | A region push/pull (Patch+) = a cutting tool, not a knit of pieces: walls (lofted curve pairs well past base and offset surface, corners mitred and BLENDED along the edge, never only the last sample) joined into one tube, then mutual trim. Sheet: UNION [base outside piece first, wall, offset piece]; solid: UNION patch+wall+offset piece -> opEnclose -> plug, then UNION [base, plug] or SUBTRACTION (plug the only tool). Cut the offset surface and base with a COPY of the full tube. | see entry |
## Critical top 10

1. Always use braces on every if/else/for/while; unbraced follow-on lines run unconditionally (FeatureScript Syntax section).
2. A new parameter's annotation "Default" migrates into every saved feature, so make it reproduce the OLD behaviour; new length defaults arrive in mm (Corrections 25, 62).
3. Write selection "Filter" expressions inline in every annotation, never as a const, and give every Query parameter a Filter (Corrections 16, 33, 51).
4. Declare each parameter id exactly once across the whole precondition, branches and array items included (Corrections 9, 16, 46a).
5. Put every declaration after the LAST import line, including Namespace::import; otherwise the whole tab fails to compile (Correction 65).
6. Precondition visibility conditions allow enum/bool/==/&&/|| only; no ordering comparisons (Correction 20).
7. Never call getProperty in a feature body or its error paths; read it in editing logic (Corrections 36, 63).
8. Call FeatureList functions inside a pattern frame on a prefix id, never inside startFeature, and let one executor feature own the list (Corrections 31, 41, 60b).
9. After any push over pin chains, GET the tab and compare; never trust "pushed"/"skipped", and grep bumped cross-document pins (Corrections 45b, 49).
10. Never add a parameter to a published custom table; placed tables are not migrated and must be re-inserted (Correction 64).

## Known contradictions (which rule wins)

- 9 vs 46a: merge branches into one declaration when the parameter is identical; use a distinct id per branch when the label or meaning differs. Both fall under 16 (one id namespace per feature).
- 16.3 vs 25 vs 62: the defineFeature defaults map does not protect saved features; the annotation default is what migrates (25), and length defaults migrate in mm (62). 25 and 62 win.
- 25 vs 64: feature parameters migrate with the annotation default; custom-table parameters do not (defineTable has no defaults map). 64 wins for tables.
- 31 vs 41 vs 50 vs 60b: use a one-level executor that owns the list and pushes the frame on its own sub-id (60b); 41's "no frame" retry is only for SELF_INTERSECTING_CURVE_SELECTED.
- 48 vs 61b: both say REST cannot change a feature's namespace. Use the shim re-export tab when instance ids must survive (48); replace at the old index when the library version must change (61b, mind its downstream-click caveat).
- 45b vs 49: repin.py once reverted a deliberate cross-document bump; it now keeps the newer pin. Still write bumped tabs directly and grep afterwards.
- 63 vs 68: 68 supersedes 63's tolerance detail (fit to 0.5 um with midpoints; 1e-7 m fits interpolate and ring). 23 and 63 combine: even spacing, chord parameters, extra curve points.
- 39 vs 69: sketch arcs when an arc must stay an arc (39); rational cubic joins are fine for merged chains (69).
- 22 vs 24: split at full-multiplicity knots first (24), then build the ruled surface from control nets (22).
- 67 vs 61b: rollbackBarIndex goes as a QUERY parameter to the eval API, not in the body (67 wins).
- 35 vs Import Issues: no namespace imports, except icons and description images (35, BLOB_DATA only).
- The first Units Handling section is an empty template; use the second. The old Statistics block is stale; use the one below.
- 33 vs 51 do not conflict: 51 repeats 33 and adds that fscheck misses it and pushproject --check catches it.
- 77 (2026-09-30): a LINE to-edge must project to an exact line, or the wrapAndLoft fast path (arcs preserved) never fires.

## Statistics (corrected 2026-09-30)

- 87 headings: 66 numbered entries (62 distinct numbers, 16 through 77; 1-15 never existed), 20 un-numbered sections, 1 stale Statistics block.
- Duplicate numbers, disambiguated above as a/b: 45, 46, 60, 61. Out of order: 26 before 24/25, 41 before 40, 62 before 61b.
- 8 un-numbered sections are unfilled templates (Geometry Operations, Query Patterns, Units Handling 1st, Type System and similar).
- Date range 2026-01-30 to 2026-09-30.

---

## Precondition Parameter Declared in Two Branches -- "Duplicate feature parameter"
**Date**: 2026-09-11
**File**: `driven_offset/design_map_query_utils.fs` -- `designMapEntryPredicate`
**Issue**: Declaring the same parameter (`entry.e_key is string;`) in two different `if`/`else` branches of a precondition (or array-item predicate) fails at compile time with `Duplicate feature parameter e_key` and `Nonconforming feature function ... precondition failed`. The feature silently disappears from the custom-feature list. `fscheck.py` does not catch this (no type/precondition checking).
**Fix**: Declare each parameter exactly once, with a combined visibility condition.
**Also (2026-09-24, Move_Along_Edge)**: parameter ids are one namespace for the WHOLE feature,
array items included -- `copy.nameMode` inside an array clashes with a top-level
`definition.nameMode`. Prefix item parameters (`copy.copyNameMode`).
```featurescript
// WRONG
if (entry.e_kind == Kind.A) { if (entry.rename) { entry.e_key is string; } }
else { entry.e_key is string; }
// CORRECT
if (entry.e_kind != Kind.A || entry.rename) { entry.e_key is string; }
```
**Diagnosis tip**: the Onshape API `featurestudios/.../featurespecs` returns `[]` for a tab whose features fail to compile; the editor's notices pane shows the actual error.

---

## Boolean Operation Type Enum ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Wrong Name
**Date**: 2026-03-02
**Issue**: Used `BooleanType.UNION` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â this enum does not exist.
**Fix**: Use `BooleanOperationType.UNION` (and `.SUBTRACT`, `.INTERSECT`).
```featurescript
// WRONG
opBoolean(context, id, { "tools": q, "operationType": BooleanType.UNION });
// CORRECT
opBoolean(context, id, { "tools": q, "operationType": BooleanOperationType.UNION });
```

## opReplaceFace ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â oppositeSense Must Be Auto-Detected
**Date**: 2026-03-02
**Issue**: Calling `opReplaceFace` without `oppositeSense` causes `DIRECT_EDIT_REPLACE_FACE_FAILED` when the template surface normal is opposite to the original face normal.
**Fix**: Dot the normals at face center (UV=0.5,0.5) and set `oppositeSense = dot(origNormal, templateNormal) < 0`. Pattern from `std/replaceFace.fs`.
```featurescript
var origPlane = try(evFaceTangentPlane(context, { "face": definition.face, "parameter": vector(0.5, 0.5) }));
var newPlane  = try(evFaceTangentPlane(context, { "face": newFaceQ, "parameter": vector(0.5, 0.5) }));
var oppositeSense = (origPlane != undefined && newPlane != undefined)
    ? dot(origPlane.normal, newPlane.normal) < 0
    : false;
opReplaceFace(context, id + "replace", {
    "replaceFaces": definition.face, "templateFace": newFaceQ, "oppositeSense": oppositeSense
});
```

---

## BSpline Splitting ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Control Point Index Bug in `splitCurve`
**Date**: 2026-02-26
**File**: `tools/curve_operations.fs` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â `splitCurve()`
**Issue**: After `refineKnotVector` to multiplicity `degree+1` at `splitParam`, the CP extraction bounds for both halves were off-by-N, causing malformed BSplineCurves that pass knot count validation silently but fail `evaluateSpline` when used as input to a second `splitCurve` call.

**Error triggered**:
```
@evaluateSpline: 18 (# control points + degree + 1) knots required, but 21 found.
```
Only appears on the 2nd+ call to `splitCurveMultiple` (first split's `curveB` is invalid; the error surfaces when that invalid curve is passed to the next `splitCurve`).

**Root cause** (cubic, `splitStart=4`, `splitEnd=7`, 8 CPs after refinement):
- CPs A: `i <= splitStart` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ took 5 CPs instead of 4 (included duplicate junction CP)
- CPs B: started at `splitEnd` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ took 1 CP instead of 4 (skipped junction and intermediate CPs)
- Knots A and B: were already correct

**Fix**:
- CPs A / weights A: change `i <= splitStart` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ `i < splitStart`
- CPs B / weights B: change starting index `splitEnd` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ `splitEnd - degree`

Knots are unchanged. The formula `splitEnd - degree = splitStart` holds for a full `degree+1` split.

---

## Function Call Syntax

### evaluateSpline() and All Standard Library Functions
**Date**: 2026-02-09
**Issue**: FeatureScript functions require named parameter maps, not positional arguments

**WRONG** (positional arguments):
```featurescript
var point = evaluateSpline(curve, param);
var dist = evDistance(context, edge1, edge2);
```

**CORRECT** (named parameter map):
```featurescript
var point = evaluateSpline({
    "spline" : curve,
    "parameters" : [param]  // Must be array, even for single parameter
})[0];  // Returns array, extract first element

var dist = evDistance(context, {
    "side0" : edge1,
    "side1" : edge2
});
```

**Key Points**:
- ALL FeatureScript standard library functions take a single map argument with named parameters
- `evaluateSpline` requires `"parameters"` as an array (e.g., `[0.5]`), not a scalar
- `evaluateSpline` returns an array of points (even for single parameter)
- Only exception: some functions take `context` as first positional arg, then parameter map

**Lesson Learned**: Never assume positional arguments work in FeatureScript. Always use parameter maps with string keys.

---

## Import Issues

### Namespace Imports Not Supported
**Date**: 2026-01-30
**Issue**: FeatureScript does NOT support namespace aliasing with `import(path) as namespace`
**Incorrect Pattern**:
```featurescript
import(path : "fpt_analyze.fs", version : "") as analyze;
import(path : "fpt_geometry.fs", version : "") as geometry;
import(path : "fpt_constants.fs", version : "") as constants;

// Later using namespace prefix
var result = analyze::findWaistPoint(...);
var mode = constants::FootprintScaleMode.ACCORDION;
```
**Correct Pattern**:
```featurescript
import(path : "fpt_analyze.fs", version : "");
import(path : "fpt_geometry.fs", version : "");
import(path : "fpt_constants.fs", version : "");

// Functions and constants are directly available
var result = findWaistPoint(...);
var mode = FootprintScaleMode.ACCORDION;
```
**Lesson Learned**: FeatureScript imports make all exported symbols directly available in the global namespace. There is no support for namespace qualification or aliasing. This means:
1. All exported names must be unique across all imported files
2. Cannot use `::` to qualify function/constant names
3. Name collisions must be avoided through careful naming conventions
4. Consider using `export import` to re-export symbols from dependencies

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Description of import/export problem]
**Incorrect Pattern**:
```featurescript
// Wrong code here
```
**Correct Pattern**:
```featurescript
// Correct code here
// Reference: [file.fs:line-number]
```
**Lesson Learned**: [Root cause and key takeaway]

---

## Type System

### Predicates Cannot Have Standalone Var Declarations
**Date**: 2026-01-31
**Issue**: Predicates can only contain boolean expressions and for-loops (with var in loop declaration). Standalone `var` statements outside loops are not allowed in predicates.
**Incorrect Pattern**:
```featurescript
export predicate isClamped(curve is BSplineCurve, tolerance is number)
{
    tolerance > 0;

    var knots = curve.knots;      // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ILLEGAL - standalone var
    var p = curve.degree;          // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ILLEGAL - standalone var
    var n = size(knots);           // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ILLEGAL - standalone var

    for (var i = 1; i <= p; i += 1)  // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ OK - var in loop declaration
    {
        abs(knots[i] - knots[0]) < tolerance;
    }
}
```
**Correct Pattern**:
```featurescript
// Convert to function returning boolean
export function isClamped(curve is BSplineCurve, tolerance is number) returns boolean
{
    if (tolerance <= 0)
        return false;

    var knots = curve.knots;      // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ OK in function
    var p = curve.degree;          // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ OK in function
    var n = size(knots);           // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ OK in function

    for (var i = 1; i <= p; i += 1)
    {
        if (abs(knots[i] - knots[0]) >= tolerance)
            return false;
    }
    return true;
}
// Reference: tools/bspline_data.fs:68-98 (fixed 2026-01-31)
```
**Lesson Learned**:
- **Predicates are for type checking**, not complex validation logic
- Predicates can only contain: boolean expressions, for-loops (with var in declaration), calls to other predicates
- For validation with intermediate variables, **use functions returning boolean**
- Compare to native predicates in `std/math.fs` (lines 57-76) and `std/vector.fs` (lines 62-131) - no standalone vars
- When converted from predicate to function, change boolean expressions to explicit return statements

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Predicate or type error description]
**Incorrect Pattern**:
```featurescript
// Wrong code here
```
**Correct Pattern**:
```featurescript
// Correct code here
// Reference: See math.fs for predicate examples
```
**Lesson Learned**: [Understanding of type system rules]

**Common Type System Patterns** (Reference: `std/math.fs`):
- Predicates return boolean: `is3dLengthVector(v)`, `isUnitVector(v)`
- Type definitions: `type MyType typecheck canBeMyType;`
- Preconditions in function signatures validate inputs
- `ValueWithUnits` required for geometric quantities

---

## Geometry Operations

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Incorrect geometric calculation]
**Incorrect Pattern**:
```featurescript
// Wrong geometric operation
```
**Correct Pattern**:
```featurescript
// Correct geometric operation
// Reference: surfaceGeometry.fs or relevant std library file
```
**Lesson Learned**: [Geometric principle or API usage]

**Key Geometric Patterns** (Reference: `std/surfaceGeometry.fs`, `std/curveGeometry.fs`):
- Vectors must have units when representing positions
- Direction vectors should be unitless
- Always normalize direction vectors when required
- Use `TOLERANCE.zeroLength` for geometric comparisons
- Plane normal must be unit vector

---

## Query Patterns

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Query construction or usage error]
**Incorrect Pattern**:
```featurescript
// Wrong query usage
```
**Correct Pattern**:
```featurescript
// Correct query usage
// Reference: queryVariable.fs
```
**Lesson Learned**: [Query system understanding]

**Query Best Practices** (Reference: `std/queryVariable.fs`):
- Use `qEverything()` as starting point
- Chain filters appropriately
- `evaluateQuery()` returns array of entities
- `qCreatedBy()` for entities from specific operations
- Cache query results if used multiple times

---

## Units Handling

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [ValueWithUnits mistake]
**Incorrect Pattern**:
```featurescript
// Wrong units handling
```
**Correct Pattern**:
```featurescript
// Correct units handling
// Reference: math.fs examples
```
**Lesson Learned**: [Units system rules]

**Units System Patterns** (Reference: `std/math.fs`):
- Create values: `5 * meter`, `90 * degree`
- Unit constants: `meter`, `inch`, `millimeter`, `degree`, `radian`
- Automatic unit conversion in operations
- Mixed unit arithmetic handled by system
- Angular functions expect radians

---

## API Usage

### `evaluateSpline` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Return Type is Nested Array, Not Array of Vectors
**Date**: 2026-03-01
**Issue**: `evaluateSpline` returns a nested array indexed as `[derivativeOrder][parameterIndex]`, not a flat array of Vectors. Accessing `[0]` returns an `array` (all positions), not a `Vector`.

**Incorrect Pattern**:
```featurescript
var pt = evaluateSpline({ "spline" : curve, "parameters" : [t] })[0];
// Error: addDebugLine(Context, array, array, ...) does not match (..., Vector, Vector, ...)
```
**Correct Pattern**:
```featurescript
// [0] = positions (0th derivative order), [0] = first parameter's result
var pt = evaluateSpline({ "spline" : curve, "parameters" : [t] })[0][0];
// pt is a Vector (3D position with units)
```
**Lesson Learned**: `evaluateSpline` return structure is `result[derivOrder][paramIdx]`. For a single parameter, position is always at `[0][0]`. For multiple parameters, `[0][i]` gives the position at the i-th input parameter.

---

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Misuse of Onshape operation or function]
**Incorrect Pattern**:
```featurescript
// Wrong API call
```
**Correct Pattern**:
```featurescript
// Correct API call
// Reference: [feature implementation file]
```
**Lesson Learned**: [API contract understanding]

**Common API Patterns**:
- **Surface Offset** (Reference: `std/offsetSurface.fs:44-45`): Proper offset direction and distance handling
- **Ruled Surface** (Reference: `std/ruledSurface.fs`): Curve alignment for ruled surface creation
- **Fill Surface** (Reference: `std/fillSurface.fs`): Boundary curve loop requirements
- **Gordon Surface** (Reference: `gordonSurface/` project): Curve network intersection consistency

---

## Tolerance and Numerical Stability

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Numerical comparison or tolerance error]
**Incorrect Pattern**:
```featurescript
// Wrong comparison (e.g., exact equality)
```
**Correct Pattern**:
```featurescript
// Correct tolerance-based comparison
// Use TOLERANCE.zeroLength, TOLERANCE.zeroAngle
```
**Lesson Learned**: [Numerical stability principle]

**Tolerance Constants**:
- `TOLERANCE.zeroLength`: ~1e-7 meters
- `TOLERANCE.zeroAngle`: ~1e-7 radians
- Never use exact equality for floating point
- Example: `abs(value) < TOLERANCE.zeroLength` instead of `value == 0`

---

## FeatureScript Syntax

### Map Key Disambiguation Required
**Date**: 2026-02-09
**Issue**: "Cannot use X as map key because there is a variable or constant with that name" - FeatureScript requires disambiguation when a map key name matches a local variable or parameter name in scope.
**Incorrect Pattern**:
```featurescript
var plane = measurePlane;
var coreWidth = 10 * millimeter;
var x = 5 * millimeter;

// ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ERROR - Ambiguous unquoted keys
return {
    plane: plane,           // ERROR: ambiguous
    coreWidth: coreWidth    // ERROR: ambiguous
};

// In station generation
stations = append(stations, {
    x: x,                   // ERROR: ambiguous
    callout: '',
    preferred: false
});

// ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ WRONG - Parentheses are for units in bounds, not disambiguation
return {
    (plane): plane          // Syntax error - wrong use of parentheses
};
```
**Correct Pattern**:
```featurescript
var plane = measurePlane;
var coreWidth = 10 * millimeter;
var x = 5 * millimeter;

// ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ CORRECT - Use quoted strings for conflicting keys
return {
    "plane" : plane,        // Quoted string disambiguates
    "coreWidth" : coreWidth // Quoted string disambiguates
};

// In station generation
stations = append(stations, {
    "x" : x,                // Quoted string disambiguates
    callout: '',            // Non-conflicting keys can remain unquoted
    preferred: false
});
```
**Reference**: Standard library uses this pattern (std/query.fs:1920, std/coordSystem.fs:80)

**Lesson Learned**:
- When constructing a map where a key name matches a local variable/parameter, use **quoted strings** `"key"` to disambiguate
- Use **double quotes** `"key"` for consistency with standard library convention
- Only quote keys that conflict; non-conflicting keys can remain unquoted (minimal quoting approach)
- Parentheses `(unitType)` are for unit specifications in parameter bounds, NOT for map key disambiguation
- Map access works the same regardless: `result.myValue` or `result["myValue"]`
- Common conflicts in measurement code:
  - Return statements with measurement data: `"coreWidth"`, `"coreThickness"`, `"swHeight"`
  - Geometry references: `"plane"`, `"query"`, `"zVal"`, `"yVal"`
  - Station data: `"x"`, `"station"`, `"rsl"`

**Files Fixed**: qcTable_geometry.fs (lines 96, 294-300, 338-339, 423-439, 529-545), qcTable_merge.fs (line 47), qcTable_stations.fs (lines 37, 252, 270, 292)

### Operation Parameters Must Use Quoted String Keys
**Date**: 2026-02-09
**Issue**: Map parameters to Onshape operations (`op*`, `ev*` functions) must use quoted string keys, even when no variable conflict exists. This is more restrictive than general map key rules.
**Incorrect Pattern**:
```featurescript
var measurePlane = plane(origin, normal);

// ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ERROR - Unquoted key in operation parameter
opPlane(context, id + "measurePlane", {
    plane: measurePlane  // ERROR: Cannot use 'plane' as key
});

evDistance(context, {
    side0: point1,  // ERROR: Even though 'side0' doesn't conflict
    side1: point2   // ERROR: Must quote operation params
});
```
**Correct Pattern**:
```featurescript
var measurePlane = plane(origin, normal);

// ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ CORRECT - Always quote keys in operation parameters
opPlane(context, id + "measurePlane", {
    "plane" : measurePlane  // Quoted key required
});

evDistance(context, {
    "side0" : point1,  // Quote all op*/ev* params
    "side1" : point2
});

// All operation examples
opPattern(context, id, {
    "entities" : bodies,
    "transforms" : transforms,
    "instanceNames" : ["name"]
});

opSplitPart(context, id, {
    "targets" : target,
    "tool" : tool,
    "keepTools" : true,
    "keepType" : SplitOperationKeepType.KEEP_BACK
});
```

**Lesson Learned**:
- **Always quote ALL keys in operation parameters** (`op*`, `ev*` functions)
- This applies even if no local variable conflicts exist
- Onshape operations have stricter parsing requirements than general maps
- Use double quotes `"key"` for consistency

**Why This Matters**: Onshape's operation functions use a more restrictive parser that requires explicit string keys to avoid ambiguity with field names.

**Files Fixed**: qcTable_geometry.fs (all op*/ev* calls throughout)

### ID Concatenation Requires Parentheses for String Operations
**Date**: 2026-02-09
**Issue**: The `+` operator (Id concatenation) has higher precedence than `~` (string concatenation). Must use parentheses when building dynamic Id strings.
**Incorrect Pattern**:
```featurescript
for (var i = 0; i < 10; i += 1)
{
    // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ERROR - Evaluates as (id + i) ~ "plane"
    opPlane(context, id + i ~ "plane", {...});

    // This tries to:
    // 1. Add number to Id: id + i
    // 2. Concatenate result with string: (result) ~ "plane"
    // 3. Fails because can't concatenate Id with string
}
```
**Correct Pattern**:
```featurescript
for (var i = 0; i < 10; i += 1)
{
    // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ CORRECT - Parentheses force string concatenation first
    opPlane(context, id + (i ~ "plane"), {...});

    // This correctly:
    // 1. Concatenate number with string: i ~ "plane" ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ "0plane"
    // 2. Add string to Id: id + "0plane"
}

// More examples
opDeleteBodies(context, id + ("deletePlane" ~ i), {...});
var searchPlane = qCreatedBy(id + (i ~ "plane"), EntityType.BODY);
```

**Lesson Learned**:
- **Always wrap string concatenation in parentheses** when adding to Id
- Pattern: `id + (variable ~ "suffix")` not `id + variable ~ "suffix"`
- Applies to any dynamic Id generation in loops
- The `+` operator binds tighter than `~`

**Operator Precedence**:
1. `+` (Id concatenation) - higher precedence
2. `~` (string concatenation) - lower precedence

**Files Fixed**: qcTable_geometry.fs (lines 442, 446, 507, 548, 552, 613)

### ValueWithUnits Comparisons Require .value Property
**Date**: 2026-02-09
**Issue**: When comparing `ValueWithUnits` to raw numbers (like tolerance constants), must access the `.value` property.
**Incorrect Pattern**:
```featurescript
var deltaZ = widestHighest[2] - highestWidest[2];  // ValueWithUnits

// ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ERROR - Comparing ValueWithUnits to number
if (abs(deltaZ) < TOLERANCE.zeroLength)  // TOLERANCE.zeroLength is raw number
{
    // ...
}
```
**Correct Pattern**:
```featurescript
var deltaZ = widestHighest[2] - highestWidest[2];  // ValueWithUnits

// ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ CORRECT - Extract numeric value first
if (abs(deltaZ.value) < TOLERANCE.zeroLength)  // Compare number to number
{
    // ...
}

// Or compare ValueWithUnits to ValueWithUnits
if (abs(deltaZ) < (TOLERANCE.zeroLength * meter))  // Both ValueWithUnits
{
    // ...
}
```

**Lesson Learned**:
- `ValueWithUnits` has two properties: `.value` (number) and `.unit` (unit type)
- Tolerance constants like `TOLERANCE.zeroLength` are raw numbers, not `ValueWithUnits`
- Cannot compare `ValueWithUnits` directly to raw numbers
- Options:
  1. Extract `.value` property: `myLength.value`
  2. Convert number to `ValueWithUnits`: `number * meter`
- Prefer option 1 for tolerance comparisons

**Why This Matters**: Type safety - FeatureScript enforces that comparisons are type-compatible.

**Files Fixed**: qcTable_geometry.fs (line 257)

### Always Use Braces for Control Flow Statements
**Date**: 2026-01-31
**Issue**: **CRITICAL BUG** - Control flow statements (if, else, for, while) without braces execute only the FIRST statement conditionally. Additional indented statements that appear to be part of the block execute unconditionally, causing severe logic errors.
**Incorrect Pattern**:
```featurescript
// CRITICAL BUG: return executes ALWAYS, not just when converged!
if (abs(f1) <= tol)
    println("  CONVERGED at iteration " ~ it);
    return t1;  // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ALWAYS executes! Not part of if!

// Another example
for (var i = 0; i < n; i += 1)
    sum += values[i];
    count += 1;  // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ALWAYS executes! Loop only includes first line!
```
**Correct Pattern**:
```featurescript
// Always use braces, even for single statements
if (abs(f1) <= tol)
{
    println("  CONVERGED at iteration " ~ it);
    return t1;  // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ Both statements execute conditionally
}

// With braces, both statements are in the loop
for (var i = 0; i < n; i += 1)
{
    sum += values[i];
    count += 1;  // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ Both statements in loop
}

// Even for single statements (recommended style)
if (x < 0)
{
    return false;
}
```
**Lesson Learned**:
- **ALWAYS use braces `{ }` for all control flow statements**, even single-line statements
- Without braces, only the FIRST statement is controlled by if/else/for/while
- Additional indented statements execute unconditionally, causing silent bugs
- This bug is especially dangerous because:
  - Code LOOKS correct due to indentation
  - Compiler doesn't warn
  - Bug only appears at runtime with subtle logic errors
- **Mandatory coding standard**: All if/else/for/while must have braces
- When reviewing code, check every control flow statement for missing braces
- Real bug example: `fpt_geometry.fs:512-519` - solver always returned on first iteration due to missing braces around `println(); return;` blocks

### Function Parameters Must Always Have Type Annotations
**Date**: 2026-03-01
**Issue**: FeatureScript function parameters without type annotations silently compile but lose type checking. Missing types on `context`, `id`, `face`, etc. produce untyped parameters that bypass FS's precondition system and can cause cryptic runtime errors.
**Incorrect Pattern**:
```featurescript
function isPointLocked(i, j, uCount, vCount, continuity) returns boolean
function faceTangentAtVBoundary(context, face, u, vEdge) returns Vector
function fitIsoCurve(context, definition, rowPts, u) returns BSplineCurve
export function myEditingLogic(context, id, oldDefinition, definition, isCreating, specifiedParameters) returns map
```
**Correct Pattern**:
```featurescript
function isPointLocked(i is number, j is number, uCount is number, vCount is number, continuity is GeometricContinuity) returns boolean
function faceTangentAtVBoundary(context is Context, face is Query, u is number, vEdge is number) returns Vector
function fitIsoCurve(context is Context, definition is map, rowPts is array, u is number) returns BSplineCurve
export function myEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
```
**Lesson Learned**:
- **Always annotate every parameter** ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â `context is Context`, `id is Id`, `face is Query`, numbers as `is number`, arrays as `is array`, feature definitions as `is map`
- The editing logic function signature is: `(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map`
- This is a systematic error that LLMs produce when writing helper functions. Treat unannotated parameters as a bug, not a style issue.
- Review ALL function signatures (not just exported ones) before finalizing any file.

**Files Fixed**: gordonSurface/pullSurface.fs ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â isPointLocked, faceTangentAtVBoundary, fitIsoCurve, pullSurfaceEditingLogic (2026-03-01)

### Export Functions by Default
**Date**: 2026-01-30
**Issue**: Functions not marked with `export` cannot be used by other files that import them
**Incorrect Pattern**:
```featurescript
// In fpt_analyze.fs
function buildCurveDataArray(bsplines is array) returns array
{
    // ...
}
```
**Correct Pattern**:
```featurescript
// In fpt_analyze.fs
export function buildCurveDataArray(bsplines is array) returns array
{
    // ...
}
```
**Lesson Learned**:
- **Nearly always export functions** in utility/library files (anything ending in `_utils`, `_math`, `_analyze`, etc.)
- Only make functions non-exported if they are truly internal helpers
- The same applies to constants and enums - export them if other files might use them
- When refactoring, always check that shared functions have `export` keyword
- Default to exporting - it's easier to remove an export later than to track down missing ones

### Template Entry
**Date**: YYYY-MM-DD
**Issue**: [Syntax error or language feature misuse]
**Incorrect Pattern**:
```featurescript
// Wrong syntax
```
**Correct Pattern**:
```featurescript
// Correct syntax
```
**Lesson Learned**: [Language rule]

---

## Instructions for Use

1. **Before generating code**: Review relevant sections for known issues
2. **After finding errors**: Add new entry with complete information
3. **Include references**: Link to std library examples when possible
4. **Be specific**: Provide exact code snippets, not just descriptions
5. **Update regularly**: This is a living document that grows with experience

---

## Matrix and Array Indexing

### Q Matrix Storage is 3ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â3, Not 6ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â6
**Date**: 2026-02-10
**Issue**: Classical Laminate Theory Q matrix is stored as 3ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â3 array in xSection code, not the full 6ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â6 matrix
**Incorrect Pattern**:
```featurescript
// Attempting to access Q66 shear modulus
var G = body.Q[5][5];  // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ERROR - Index out of bounds!

// Assuming full 6ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â6 matrix layout:
// [[Q11, Q12, Q13, Q14, Q15, Q16],
//  [Q21, Q22, Q23, Q24, Q25, Q26],
//  ...
//  [Q61, Q62, Q63, Q64, Q65, Q66]]
```
**Correct Pattern**:
```featurescript
// Q is stored as 3ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â3 reduced stiffness matrix
// [[Q11, Q12, Q16],
//  [Q12, Q22, Q26],
//  [Q16, Q26, Q66]]

var G = body.Q[2][2];  // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ Q66 is at [2][2] in 3ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â3 matrix

// For isotropic materials: Q66 = E/(2*(1+ÃƒÆ’Ã…Â½Ãƒâ€šÃ‚Â½))
// For orthotropic materials: Q66 = G12
```
**Reference**: xSectCLT.fs lines 77-87 (Q matrix documentation)

**Lesson Learned**:
- xSection code uses **3ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â3 reduced stiffness matrix**, not full 6ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â6
- Q66 (shear modulus) is accessed at `Q[2][2]`, NOT `Q[5][5]`
- Always check data structure documentation before accessing matrix elements
- The 3ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â3 format is standard for plane stress/CLT formulations
- Indices map: Q11=[0][0], Q22=[1][1], Q66=[2][2]

**Files Fixed**: xSect_GJ.fs line 193

---

## Units Handling

### Dimensional Analysis Required for Unit Stripping
**Date**: 2026-02-10
**Issue**: When stripping units for numerical solvers, must perform dimensional analysis to get correct unit factors
**Incorrect Pattern**:
```featurescript
// FEM stiffness matrix K and load vector f
// Assembled with: K += G * A * (dNdy[a] * dNdy[b] + dNdz[a] * dNdz[b])
//                 f += G * A * (z_c * dNdy[a] - y_c * dNdz[a])

// ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ WRONG - Guessed units without dimensional analysis
var K_unit = newton;                 // Wrong!
var f_unit = newton * meter;         // Wrong!

K_plain[i][j] = K[i][j] / K_unit;
f_plain[i] = f[i] / f_unit;
```
**Correct Pattern**:
```featurescript
// Perform dimensional analysis:
// K: G * A * (dNdy)ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² = (N/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²) * mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² * (1/m)ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² = N/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²
// f: G * A * distance * dNdy = (N/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²) * mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² * m * (1/m) = N

var K_unit = newton / (meter * meter);  // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ N/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²
var f_unit = newton;                     // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ N

K_plain[i][j] = K[i][j] / K_unit;
f_plain[i] = f[i] / f_unit;

// Document the dimensional analysis in comments!
```
**Lesson Learned**:
- **Never guess units** - always perform dimensional analysis
- Write out the calculation chain: `quantity = A * B * C`
- Track units through each step: `[N/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²] * [mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²] * [1/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²] = [N/mÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â²]`
- Add comments showing the dimensional analysis
- Common mistake: simplifying unit expressions mentally without checking
- Shape function gradients have units `[1/length]`
- For FEM: Stiffness ~ G*A*gradÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â², Load ~ G*A*distance*grad

**Files Fixed**: xSect_GJ.fs lines 354-357

### Polar Moment of Inertia Formula for Triangles
**Date**: 2026-02-10
**Issue**: Polar moment calculation requires all six cross-product terms, not just three
**Incorrect Pattern**:
```featurescript
// ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ INCOMPLETE - Missing y1*y3 and z1*z3 cross terms
var y_cross = y1*y2 + y2*y3 + y3*y1;  // Only 3 terms
var z_cross = z1*z2 + z2*z3 + z3*z1;  // Only 3 terms
var Jp = (A / 6.0) * (y_sq + z_sq + y_cross + z_cross);
```
**Correct Pattern**:
```featurescript
// ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ CORRECT - All six cross terms for each axis
// Jp = Iy + Iz where:
//   Iy = ÃƒÆ’Ã‚Â¢Ãƒâ€¹Ã¢â‚¬Â Ãƒâ€šÃ‚Â«zÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² dA = (A/6) * (z1ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² + z2ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² + z3ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² + z1*z2 + z1*z3 + z2*z3)
//   Iz = ÃƒÆ’Ã‚Â¢Ãƒâ€¹Ã¢â‚¬Â Ãƒâ€šÃ‚Â«yÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² dA = (A/6) * (y1ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² + y2ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² + y3ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² + y1*y2 + y1*y3 + y2*y3)

var Iy = (A / 6.0) * (z1*z1 + z2*z2 + z3*z3 + z1*z2 + z1*z3 + z2*z3);
var Iz = (A / 6.0) * (y1*y1 + y2*y2 + y3*y3 + y1*y2 + y1*y3 + y2*y3);
var Jp_e = Iy + Iz;

// Alternative: Write all terms explicitly
// y1*y2 + y1*y3 + y2*y3 (not y1*y2 + y2*y3 + y3*y1)
```
**Lesson Learned**:
- Closed-form integrals for triangles need **all pairwise products**
- For three vertices (i,j,k), cross terms are: i*j + i*k + j*k
- The pattern i*j + j*k + k*i is the **same set** (just reordered), but less clear
- Better to write as two separate formulas (Iy and Iz) for clarity
- Verify closed-form formulas against textbook references
- Second moment formulas: nÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â² terms + n(n-1)/2 cross terms = n(n+1)/2 total

**Files Fixed**: xSect_GJ.fs lines 456-462

---

### No pow() Function ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Use ^ Operator
**Date**: 2026-02-18
**Issue**: `pow(base, exponent)` does not exist in FeatureScript. Calling it with 2 arguments throws "function pow with 2 arguments not found".
**Incorrect Pattern**:
```featurescript
var magnitude = pow(10, exponent);  // ÃƒÆ’Ã‚Â¢Ãƒâ€šÃ‚ÂÃƒâ€¦Ã¢â‚¬â„¢ ERROR - no pow() function
```
**Correct Pattern**:
```featurescript
var magnitude = 10 ^ exponent;  // ÃƒÆ’Ã‚Â¢Ãƒâ€¦Ã¢â‚¬Å“ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦ Use ^ operator for exponentiation
```
**Lesson Learned**:
- FeatureScript uses `^` for exponentiation (not `**` or `pow()`)
- The std/math.fs header explicitly states: "There is no `pow` function: exponentiation is done using the `^` operator."
- Available math functions: `log(x)` (natural log), `log10(x)`, `exp(x)`, `abs(x)`, `floor(x)`, `ceil(x)`, `round(x)`, `sqrt(x)`

**Files Fixed**: ExportCurveCore.fs (roundToPrecision)

---

## Reserved Variable Names ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Built-in Type Identifiers

**Date**: 2026-03-14
**Issue**: Certain lowercase identifiers are reserved in FeatureScript and cannot be used as variable names. `box` is the most surprising ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â it is a built-in mutable reference type (used as `new box(undefined)`). Assigning `var box = ...` causes a parse error: `mismatched input 'box' expecting ID`.

**Known reserved identifiers** (cannot be used as variable names):
- `box` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â built-in mutable reference type
- `line` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â std function that constructs a `Line` value (from `std/line.fs`)
- `plane` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â std function that constructs a `Plane` value (from `std/coordSystem.fs`)
- `transform` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â std function that constructs a `Transform` value
- `array`, `map`, `string`, `number`, `boolean`, `function`, `undefined`, `builtin` ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â primitive type keywords

**Type names** (PascalCase) are also reserved: `Box3d`, `Line`, `Plane`, `Path`, `Query`, `Context`, `Id`, `Vector`, `BSplineCurve`, etc. ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â do not use these as variable names.

**Fix**: Use a descriptive prefix or suffix. Examples:
```featurescript
// WRONG
var box  = evBox3d(context, { "topology" : body, "tight" : true });
var line = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 });

// CORRECT
var bb   = evBox3d(context, { "topology" : body, "tight" : true });
var tl   = evEdgeTangentLine(context, { "edge" : e, "parameter" : 0.5 });
```
**Lesson Learned**: Scan all variable names for conflicts with built-in identifiers before writing any FeatureScript. Prefer prefixed names (`bb`, `tl`, `pl`, `xf`) for geometry temporaries.

---

## addDebugLine / addDebugPoint Cost a Full Sketch Solve Each

**Date**: 2026-09-09
**Issue**: `addDebugLine` looks like a cheap annotation. It is not. `std/debug.fs:429` expands to `startFeature` -> `createSketchLine` -> `addDebugEntities` -> `abortFeature`, and `createSketchLine` (`std/debug.fs:405-417`) does `newSketchOnPlane` + `skLineSegment` + **`skSolve`**. So every debug line is a sketch creation and a constraint solve. `addDebugPoint` (`std/debug.fs:363`) is the same shape with `opPoint`.

**Measured (driven_offset, 2026-09-09)**: drawing two axes at each of 379 stations = 758 `addDebugLine` calls = 758 sketch solves, costing **+2.17 s** on a feature whose entire non-debug regen was 1.5 s. Capping debug markers at 60 via a stride took the whole feature to 1.41 s *with all debug on* -- i.e. below its previous debug-off time.

**Fix**: never draw one debug entity per sample. Either stride the samples (`stride = max(1, ceil(count / MAX_MARKERS))`) or accumulate geometry inside a single `startFeature`/`abortFeature` scope and make one `addDebugEntities` call. Note `opPolyline` does NOT exist in this std version -- verify before reaching for it.

**Lesson Learned**: treat `addDebugLine`/`addDebugPoint` as modelling operations, not print statements. `println` by comparison is cheap; ~870 println calls in the same feature were not the bottleneck.

---

## Reserved Words Also Break Map Field Access

**Date**: 2026-09-09
**Issue**: The reserved-identifier rule is not limited to variable names -- it also applies to **dot access on a map field**. A map built with a `"type"` key parses fine, but reading it back as `myMap.type` fails with `mismatched input 'type' expecting ID`, and the error points at the read site, not at the line where the key was created.

```featurescript
// Parses fine -- quoted keys are just strings
const shape = { "type" : "arc", "radius" : r };

// WRONG -- parse error at the '.type'
if (shape.type == "arc") { ... }

// WORKS -- bracket access sidesteps the keyword
if (shape["type"] == "arc") { ... }

// BETTER -- do not name the key a keyword in the first place
const shape = { "kind" : "arc", "radius" : r };
if (shape.kind == "arc") { ... }
```

**Why `footprint/arcFit.fs` looks the way it does**: it uses `seg["type"]` throughout. That bracket access is not a style choice, it is the workaround for this exact error.

**Fix**: Prefer a non-keyword key name (`kind`, `curveKind`, `shapeKind`). Use bracket access only when an existing key name cannot be changed.

**Lesson Learned**: Apply the reserved-word check to map *keys* that will be read with dot access, not just to variable names. Found while compiling `driven_offset/driven_edge_offset.fs`.

---

## Annotations Must Use Printable ASCII Only

**Date**: 2026-03-14
**Issue**: Unicode characters in `"Name"`, `"Description"`, or any annotation string cause a compile error: `Invalid character in '...' annotation: only printable ASCII allowed`. Common offenders: `ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ` (en dash U+2013), `ÃƒÆ’Ã‚Â¢Ãƒâ€¹Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢` (minus U+2212), `ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦` (ellipsis U+2026), `"` / `"` (smart quotes).
**Fix**: Use only plain ASCII: `-` not `ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ` or `ÃƒÆ’Ã‚Â¢Ãƒâ€¹Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢`, `...` not `ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€šÃ‚Â¦`, `"` not smart quotes.
```featurescript
// WRONG ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â Unicode en dash / minus sign
annotation { "Name" : "Step (1ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ6)" }           // U+2013 en dash
annotation { "Description" : "x = a ÃƒÆ’Ã‚Â¢Ãƒâ€¹Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ b" }    // U+2212 minus

// CORRECT ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â plain ASCII
annotation { "Name" : "Step (1-6)" }
annotation { "Description" : "x = a - b" }
```
**Lesson Learned**: Write all annotation strings in plain ASCII. Editors that auto-correct punctuation (smart quotes, en dashes) will silently break FeatureScript annotations.

---

## 16. Feature UI conformance: four rules fscheck cannot see

**Date**: 2026-09-14
**Category**: FeatureScript Syntax / Feature UI

Four separate regen failures on one new feature, all reported clean by `fscheck.py`,
because they are Onshape *precondition conformance* rules rather than symbol resolution.

1. **Every `Query` parameter needs a `Filter`** (or `UIHint.ALWAYS_HIDDEN`). Without one:
   `Nonconforming feature function: precondition analysis failed`. A `"Name"` alone is not
   enough.
2. **Parameter names are unique across the WHOLE precondition**, not per array. Two arrays
   each declaring `entry.variableName` gives `Duplicate feature parameter 'variableName'`.
3. **An annotation's `"Default"` only serves NEW instances.** A feature instance saved
   before a parameter existed fails `Precondition failed (definition.x is Query)` on regen.
   The fix is the defaults map -- `defineFeature`'s second argument -- which supplies values
   for keys a stored definition lacks.
4. **Sketch geometry is `EntityType.FACE` / `EntityType.EDGE`, never `EntityType.BODY`.**
   A filter of `EntityType.BODY && SketchObject.YES` selects nothing. std always writes
   `(EntityType.FACE || EntityType.EDGE) && SketchObject.YES`.

**Lesson Learned**: fscheck is blind to all four. Grep for them before pushing -- each is a
one-line check over the precondition block -- or read real notices with
`python -m sync.main notices <project> --json`.

---

## 17. common.fs does not re-export every std module

**Date**: 2026-09-14
**Category**: Import Issues

`Variable ProjectionType not found` and `Function getQueryVariable with 2 argument(s) not
found` both came from importing only `onshape/std/common.fs`. The symbols exist; the
modules holding them are not re-exported.

```featurescript
import(path : "onshape/std/projectiontype.gen.fs", version : "3070.0");  // ProjectionType
import(path : "onshape/std/queryVariable.fs", version : "3070.0");       // get/setQueryVariable
```

`context.fs` IS re-exported (so `getVariable` / `getAllVariables` are available).
`geomOperations.fs` documents `opDropCurve` in terms of `ProjectionType` without the type
being in scope, which is what makes this one confusing.

**Lesson Learned**: "documented in a module common re-exports" does not mean "in scope".
When a std name is not found, check whether its declaring module is in `common.fs` before
assuming the name is wrong.

---

## 18. Query variables cannot be enumerated from FeatureScript

**Date**: 2026-09-14
**Category**: Type System / Query Variables

Established by regen, after building an editing-logic discovery pass that found nothing:

- `getAllVariables(context)` (`@internal`) returns ordinary variables only. Query variables
  are written through a separate builtin (`@setQueryVariable`) and do not appear.
  `queryVariable.fs` confirms the split: it treats a name `getVariable` can find as proof
  the name belongs to a NON-query variable.
- There is no `getAllQueryVariables`.
- **Selecting a query variable in a `Query` parameter gives you its VALUE, not a reference.**
  `QueryType` has no `QUERY_VARIABLE` member, so the variable's name is unrecoverable from
  the selection.
- `UIHint.QUERY_VARIABLE_NAME` (`@internal`) exists and is used by std's own Query variable
  feature on a string parameter. Whether it presents a picker was not established.
- Producer slots from the embed pattern DO appear in `getAllVariables`, under keys shaped
  like `[ Fxxxx_0 ]`.

**Lesson Learned**: do not design a feature around discovering query variables in context.
Either take the name as a string, or read embedded producer maps -- which are enumerable,
named and described -- and accept that this only reaches features you instrumented.

---

## 19. opCreateCompositePart rejects mate connectors

**Date**: 2026-09-14
**Category**: Geometry Operations

`@opCreateCompositePart: INVALID_INPUT` with no further detail. The cause was passing mate
connectors in `bodies`. A mate connector is `BodyType.MATE_CONNECTOR` -- a body by type --
but not a valid composite member.

```featurescript
const members = qBodyType(candidates, [BodyType.SOLID, BodyType.SHEET]);
```

Nested composite parts are also suspect; `qFlattenedCompositeParts` avoids the question.

**Lesson Learned**: filter composite members to SOLID and SHEET explicitly. `EntityType.BODY`
is a wider set than the operation accepts, and the error names neither the offending entity
nor the reason.

## 20. Precondition visibility conditions admit no ordering comparison

**Error**: `Nonconforming feature function 'drivenOffsetSurface': precondition analysis failed`
followed by `Unexpected operator: GREATER` pointing at the `if` line.

**Wrong** -- inside a `precondition { ... }`:

```
if (definition.loftPerSegment && size(definition.offsets) > 2)
{
    annotation { "Name" : "Blend through profiles", "Default" : false }
    definition.blendThroughProfiles is boolean;
}
```

**Right**:

```
if (definition.loftPerSegment)
{
    annotation { "Name" : "Blend through profiles", "Default" : false }
    definition.blendThroughProfiles is boolean;
}
```

**Why**: a precondition is analysed statically to work out which parameters a given
configuration can show, so the conditions are not evaluated -- they are parsed against a
restricted grammar. Equality against an enum or a constant, boolean fields, `!`, `&&` and
`||` are in it. `>`, `<`, `>=` and `<=` are not, and the analysis rejects the whole feature
rather than the one line. Every ordering comparison in std's own files is in a feature body
or a helper, never in a precondition's visibility `if`.

**Note**: the body below the precondition is ordinary runtime code, so
`if (size(definition.offsets) < 2) { throw regenError(...); }` there is fine. This is purely
about the visibility conditions inside the precondition block.

**Lesson Learned**: gate parameter visibility on enums and booleans only. When the natural
condition is "more than N of something", either drop it and make the parameter inert in the
cases it does not apply to, or validate in the body with a regenError.

---

## 21. Fit corresponding curves under the same constraints, or the fitter drops control points

**Error**: `@opCreateBSplineCurve: BAD_GEOMETRY`, thrown from `emitSplineCurve` via
`curveThrough` <- `ruledSection`. The base section of the same ruled surface built fine;
only the displaced sections failed.

**Cause**: `ruledSection` passed exact end tangents to `approximateSpline` only at zero
reach, gated on `atOffset`, on the reasoning that the offset's slopes no longer describe a
displaced section. True of the slopes, false of the tangent -- a constant reach has zero
derivative, so the displaced tangent is the offset's own tangent re-evaluated at the larger
amount.

An unconstrained fit is free to use fewer control points than a constrained one. Measured on
one 28-point run: `CPs 12 / knots 16` with end tangents, `CPs 7 / knots 11` without. On a
short, nearly-straight run that reduction takes the count below `degree + 1`, and a
`BSplineCurve` with fewer than `degree + 1` control points is malformed. The kernel reports
that only as `BAD_GEOMETRY` -- it names neither the curve nor the reason.

**Fix**: give every section the tangent belonging to its own displacement. `runTangent`
gained a form taking a constant `displacement` added to the amounts; it is exact in both
maps, because `d/ds surfaceOffset(w + reach, h)` is `surfaceOffsetTangent(w + reach, h)`, and
`d/ds (P + reach*W)` is `P' + reach*W'`, which is what `offsetTangent` returns once reach is
folded into the width amount (W enters multiplied by the amount). At reach 0 the displacement
is zero and the result is bit-identical to before.

**Second symptom, same cause**: base and displaced sections were going into `opLoft` with
different control-point counts, so `LoftTopology.COLUMNS` was pairing sections carrying
different parameterizations. The fix closes that too.

**Lesson Learned**: when two curves have to correspond -- loft sections, paired profiles --
fit them under the SAME constraints. A difference in constraints is a difference in
parameterization even when the points are identical, and an unconstrained fit can degenerate
below the degree it declares. If `opCreateBSplineCurve` says `BAD_GEOMETRY`, print the
fitted `degree`, control-point count and knot count before handing it over; a valid clamped
B-spline needs `knots == CPs + degree + 1` and `CPs >= degree + 1`.

---

## 22. opLoft refuses some pairs of compatible B-splines; write the ruled surface down instead

**Error**: `@opLoft: LOFT_FAILED` on one patch of a multi-profile loft while the neighbouring
patches, measuring identically, built. Manual loft of the same two edges in the Part Studio
failed the same way.

**What it was not**: the curves. The pair was two 6-CP degree-3 B-splines from one coupled
`approximateSpline` fit -- same knots bit for bit, 5.8 mm apart the whole way, tangents in
agreement, no cusp, no reversal, chords co-directed, endpoints paired. Rebuilt from their own
`evCurveDefinition` and lofted again in the same context: refused. Reproduced outside the
feature from the printed definition (10 decimal places): refused. So not context, not the
edges' state, not the geometry in any sense we can measure.

**What it was**: the exact knot values. Same pair with knots rounded to 1e-5: lofts. Knots at
exact thirds: lofts. Knots shifted by 1e-9: still refused. Either curve's control points
rounded to 1 um: lofts (in one configuration also at 100 nm; in another 10 nm was not
enough). A kernel-internal path with no characterisation available from outside.

**Fix**: do not loft what can be written down. Two B-spline sections sharing degree and knots
define the ruled surface between them exactly -- `opCreateBSplineSurface` with `uDegree` and
`uKnots` from the curves, `vDegree` 1, `vKnots` `[0, 0, 1, 1]`, and the control net the two
control polygons side by side. Verified: 1 face, the source curve lies on it to 0 mm, area
equals length x separation, and `opBoolean` UNION joins it with the lofted line/arc patches
into one sheet body. `ruledPatch` / `sharedParameterization` in `driven_offset_surface.fs`;
`opLoft` remains for every pair that does not qualify (lines, arcs, separately fitted
sections), where it has never refused anything.

**How it was found**, since the route matters more than the answer: print the refused pair in
full (`[refused]` dump, gated on the refusal itself, not on a toggle); rebuild it outside the
feature via the FeatureScript eval API and confirm the refusal reproduces; then perturb ONE
quantity at a time -- knots, one profile, one axis, end vs interior CPs -- until the pass/fail
boundary is found. Rounding to the printed precision silently perturbs the knots too, which
is why the first out-of-context rebuild passed and misled for a while.

**Lesson Learned**: when an op refuses geometry that measures correct, stop measuring and
bisect. And when the surface is determined by its boundary curves' control nets, build it
from them -- a loft is a fit, and a fit can refuse.

---

## 23. approximateSpline ignores the magnitude of the end derivatives you pass

**Symptom**: a fitted section curve with curvature 60 /m (R = 16 mm) in its last 2 mm, on a
15 m radius source, and a +-60% curvature swing along the rest of the run. Tangent DIRECTION
handed to the fit agreed with the points to 0.001 degrees.

**Cause**: std doc for `approximateSpline`: "If `parameters` are not specified, the magnitude of
start and end derivatives in targets is ignored." The fitter sizes them itself from the end
segments of the point list. `insertCrossings` had put an exact station on a profile break
0.158 mm before a regular 9.3 mm sample, so the first segment was 60x shorter than the rest and
the start derivative came out at 4.5 mm on a 149 mm run (`|cp1 - cp0|` 0.445 mm against 17.7 mm
at the other end). The curve honoured the tangent for half a millimetre, then hooked.

**Fix**: `CROSSING_CLEARANCE` in `insertCrossings` -- a regular station within half the local
spacing of an inserted crossing is dropped (chain ends always kept). Every gap is then between
0.5 and 1.5 spacings. After: 0.058-0.061 /m flat along the run.

**Lesson Learned**: scaling a derivative by the chord before `approximateSpline` does nothing
unless `parameters` is also given. Keep sample spacing even near inserted stations, or the
fitter's own derivative sizing is garbage. Print `|cp1 - cp0|` next to `|cp_last - cp_prev|`
when a fit hooks: a 30x asymmetry is this.

Related resolution fact: a fit at tolerance `tol` can only resolve curvature to about
`8 * tol / L^2` over a run of length L. At std's 10 um default a 53 mm run is +-0.03 /m,
which is +-45% of a ski sidecut's 0.066 /m. Use 1 um for ski radii.

**Structural fix (2026-09-19)**: pass chord-length `parameters` (cumulative chord / total,
in [0, 1]). Then the derivatives are NOT rescaled, the fit is still an approximation (not
forced interpolation), and it is far cheaper: 40 uneven points on a 200 mm arc fitted with
8 CPs at 10 um and end curvature 4.94 /m (true 5) against 15 CPs (the cap) and 0.6 /m
without. Constraints: parameters must increase by >= 1e-6 (cull repeats relative to the
run's chord first), and one array serves every target of a family. Bonus: with the points
pinned to parameters, `evaluateSpline` at those parameters measures the fit's real error --
`approximateSpline` says nothing when it stops AT `maxControlPoints` short of tolerance
(ten runs in one document were 16-70 um off at a 10 um tolerance, silently).

---

## 26. The frame's length axis is not the point's velocity

**Symptom**: every run-end tangent on a source edge climbing a ramp was 4.7-4.8 degrees off
the run's own chord, on both profiles alike (so not a width term), while the source edge's
tangent agreed with its own chord to 0.01 degrees.

**Cause**: `stationFrame` projects the length axis into the reference surface when "Hold
length in the reference surface" is on (and `worldFrame` sets it to world X), and both
`offsetTangent` and `surfaceOffsetTangent` used that axis as the base point's velocity:
`heightRate = dot(frame.tangent, surf.normal)` was identically zero, so the source's climb
out of the surface vanished from the derivative.

**Fix**: the frame carries `velocity` (the true source tangent) beside `tangent` (the axis);
`frameVelocity(frame)` feeds the derivative. Run 5 of the wall: 4.76 -> 0.07 degrees.

**Lesson Learned**: an axis you define for MEASURING offsets and the direction the base point
actually MOVES are different vectors the moment the axes are constrained. Keep both.

---

## 24. opCreateBSplineSurface refuses a creased surface (BSPLINESURFACE_NOT_G1)

**Error**: `@opCreateBSplineSurface: BSPLINESURFACE_NOT_G1` building a ruled B-surface whose
u-curves were two pieces joined C0 (knot of multiplicity `degree` at the seam).

**Cause**: the kernel will not hold a G1 discontinuity inside one surface; a crease has to be
a boundary between faces.

**Fix**: `splitAtCreases` in `bspline_compat.fs` -- cut both u-curves at every interior knot of
multiplicity >= degree (they share a knot vector after `unifyKnots`, so they split at the same
places) and build one surface per stretch. The union joins them along the crease as faces.

**Lesson Learned**: when making two curves compatible for a ruled surface (elevate, join,
unify knots -- all exact), treat every full-multiplicity interior knot as a face boundary,
not a knot. The result is a patch of two or three 6x2 nets where `opLoft` produced one 80x2
fit with the crease smeared into a ripple.

---

## 25. A new parameter's annotation "Default" is written into every SAVED feature

**Symptom**: pushing a Feature Studio that added `runBreakMode` with `"Default" :
RunBreakMode.DISCONTINUITIES` in the surface feature silently re-ran the saved
`SW_Rout_Surface` with the new behaviour (3 runs instead of 7, 8 faces instead of 16), and
the `intersectionCurve` selected off its faces failed with `CANNOT_RESOLVE_ENTITIES`, which
took the feature seeded on that wire down with it. The `defineFeature` defaults map said
`SOURCE_EDGES`.

**Cause**: when a feature type gains a parameter, Onshape adds it to every existing feature
instance with the ANNOTATION default; the defaults map is only consulted while the parameter
is absent from the definition, which after that migration it never is. Verified by reading
the features back through the REST API: every saved instance carried
`runBreakMode: DISCONTINUITIES` explicitly.

**Fix**: a new parameter's annotation default must be the value that reproduces the OLD
behaviour, whatever a new instance would ideally want. Repaired the two features through
`POST .../features/featureid/{id}` with the value set back.

**Lesson Learned**: "the defaults map protects saved features" is only half true -- it keeps
the precondition from failing, it does not decide the value. Treat the annotation default as
a migration for every feature already in every document.

---

## 27. Dialog buttons: `isButton` + the 8th editing-logic argument `clickedButton`

**Symptom**: a "run this once" action declared as a boolean renders as a checkbox and
needs edge detection plus a self-clear in editing logic.

**Fix**: std has a real button. Declare `annotation { "Name" : "Fit control points to
tolerance" } isButton(definition.fitControlPoints);` -- no Default, no bounds, and the
key must NOT be in the defineFeature defaults map (`isButton` is `value is undefined`).
Editing logic takes an 8th argument, `clickedButton is string`, equal to the parameter
KEY for the invocation in which it was pressed and "" otherwise:
`if (clickedButton == "fitControlPoints") { return withFittedControlPoints(context, definition); }`.
Nothing to reset; the click is never stored and never re-fires on regen. Onshape migrates
a saved boolean of that name to `BTMParameterButton` on the next push without complaint.
`UIHint.DISPLAY_SHORT` puts two buttons on one row. Leftover boolean edge detection on a
button (`!oldDefinition.recalculate && definition.recalculate`) throws "Operand for '!' was not a
boolean" / "Unexpected error in editing logic execution" on every dialog edit, because the button's
value is undefined (xSection estimateStiffness, fixed 2026-09-26) -- test only `clickedButton`. Only std user: routingCurve.fs
(`resetTriad`, `processInputs`, `orthoPrevious/Next`); repo users: footprint/analyzeFootprint.fs,
xSection/features/*.fs. The note in example_1/betterMeasure/CLAUDE.md that "clickedButton
does NOT exist" was wrong.

**Lesson Learned**: editing logic runs only while the dialog is open, and any context
changes it makes are rolled back -- express the result purely as definition edits. It can
call ev* functions and approximateSpline (evaluations), which is enough to size a fit.

---

## 28. "Which side to keep" by distance to the pieces picks wrong; decide against the splitter

**Symptom**: Mutual Trim+ kept the wrong half of a leaning cap wall although the reference
point (MRS) was plainly below the cut: the wall's UPPER half reached closer to the point
than the lower half did, and "keep the piece nearest the reference" measured reach, not
side.

**Fix**: the side of a piece of surface A is decided against the surface that split it,
B, extended: signed distance to B at the closest point (`evDistance` -> face index +
(u, v) -> `evFaceTangentPlane(...).normal`) for the reference and for each piece's
`evApproximateCentroid`; the piece whose sign matches the reference is the near one.
Distance to the pieces is only the fallback when the two pieces do not straddle B.

**Lesson Learned**: minimum distance to a face SET answers "how far does it reach", never
"which side is it on". Any side question near a split has a natural splitter; ask it there.
Same family as driven_edge_offset's straight-reference plane (correction 26's neighbour):
state orientation in geometry (reference entity, world Z) rather than as a flip, and the
result holds when the inputs change.

---

## Correction 29: Rational / degenerate B-spline surfaces and vertex adjacency (2026-09-20)

**Symptom**: `opCreateBSplineSurface` "Execution error" for an exact corner sector (arc row
+ collapsed point row); `qAdjacent(edge, AdjacencyType.EDGE, EntityType.VERTEX)` precondition
failure.

**Fix**: `bSplineSurface` requires `weights` to be a `Matrix` -- `"weights" : matrix([[wA, wB], ...])`,
never a plain array of arrays (the precondition `definition.weights is Matrix` is what the
"Execution error" was). Rows may be degenerate: a control row of three copies of one point
builds fine (a cone sector from an arc to its vertex; area verified exact in the eval
sandbox). Vertices of an edge are `qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX)`.

**Lesson Learned**: the std geometry constructors are typed; when an op says "Execution
error" with no kernel message, check the constructor's precondition first (the eval API
prints it as a WARNING). Reproduce constructions in the eval sandbox before wiring them in.

---

## Correction 30: isVolume / isArea are not dialog parameters (2026-09-22)

**Symptom**: `Nonconforming feature function 'x': precondition analysis failed` plus
`No definition passed to predicate isVolume` at the `isVolume(definition.v, VOLUME_BOUNDS)` line.

**Fix**: the precondition analyser accepts `isLength`, `isAngle`, `isInteger`, `isReal`
(and the enum/boolean/string/Query/FeatureList types) -- std never declares a dialog parameter
with `isVolume` or `isArea`. Take a plain `isReal` in a stated unit ("Target (mm^3)") and
multiply by `cubicMillimeter` / `squareMillimeter` in the body. No mass or density bound spec
exists either -- same approach (g, g/cm^3).

**Lesson Learned**: before using a value predicate as a parameter, grep std for
`is<Kind>(definition.` -- if std never does it, the analyser will not either.

---

## Correction 31: FeatureList functions -- call them like Pattern does, never inside startFeature (2026-09-22)

**Symptom**: re-running a Part Studio feature list from a custom feature (`f(prefixId)` for each
value of a `FeatureList`), the re-run sketch reports success but builds NOTHING (no bodies,
no entities anywhere); the next extrude fails `EXTRUDE_NO_SELECTED_REGION`.

**Cause**: the calls were wrapped in `startFeature(context, id + "trialK")` ... `abortFeature`
for cheap rollback. Inside a started subfeature a re-run sketch is a no-op. Bisected live:
no startFeature + prefix `id + "trialK"` builds all 27 features; the same with startFeature
fails. The pattern frame (`setFeaturePatternInstanceData`) is irrelevant to this.

**Fix**: do exactly what `applyPattern` (patternUtils.fs:336) does -- prefix `id + name`,
push `setFeaturePatternInstanceData(context, prefix, {transform: identityTransform()})`, call
every function with the prefix, unset. Discard a trial by `opDeleteBodies` on
`qCreatedBy(prefix, EntityType.BODY)`. That cannot undo a listed feature that modified a body
from outside the list.

**Lesson Learned**: `startFeature`/`abortFeature` rollback is fine for ops you call yourself;
it is not for generated Part Studio feature functions. Also: a variable-feature in the list
that reassigns the solver's variable must be overridden after it runs, not rejected.

---

## Correction 32: getVariable with an `undefined` default still throws (2026-09-23)

**Symptom**: `getVariable(context, name, undefined)` raised `@getVariable: VARIABLE_NOT_FOUND`
for a missing name, exactly like the two-argument form.

**Cause**: the overload passes `{ "name" : name, "defaultValue" : defaultValue }`; a map entry
set to `undefined` is an ABSENT entry, so there is no default at all.

**Fix**: a sentinel default, e.g. `const v = getVariable(context, name, "__missing__");` and
compare -- wrapped once as `optionalVariable` (design_map_query_utils.fs, iterative_solve.fs).
The same applies to any std call taking an optional map field: `undefined` means "not given".

---

## Correction 33: a selection "Filter" must be a literal expression, not a constant (2026-09-23)

**Symptom**: `annotation { "Filter" : REFERENCE_FILTER, ... }` with
`export const REFERENCE_FILTER = EntityType.BODY || ... || BodyType.MATE_CONNECTOR;` compiled
with warnings "Invalid filter expression variable reference" and "Nonconforming feature
function: precondition analysis failed" -- the feature would not be usable.

**Cause**: the precondition analyser reads filters statically; it resolves enum literals, not
variables or constants.

**Fix**: write the filter out in every annotation. Share it by copy, not by name.

---

## Correction 34: opSplitFace -- the split face's id dies, qCreatedBy has no faces (2026-09-24)

**Symptom**: after `opSplitFace`, both an evaluated (transient) query of the target face and
`qCreatedBy(splitId, EntityType.FACE)` resolve to NOTHING; code tracking "the faces so far"
ends up empty (Split+ face mode threw "Nothing is left").

**Verified** (eval API): top face of a cube split by a plane -> transient query 0,
qCreatedBy FACE 0, qCreatedBy EDGE 1 (the new cut), qSplitBy(id, FACE, false) 1 and
qSplitBy(id, FACE, true) 1 (the two halves). A lazy query (qContainsPoint) still resolves.

**Fix**: after each split, faces = untouched faces (their ids survive) + both qSplitBy sides:
`qUnion([faces, qSplitBy(splitId, EntityType.FACE, false), qSplitBy(splitId, EntityType.FACE, true)])`,
evaluated to dedupe.

---

## Correction 35: feature icons -- the one allowed `name::` import (2026-09-24)

**Context**: CLAUDE.md says FeatureScript has no namespace imports. Icons are the exception.

**How**: upload the SVG into the document as its own tab (a blob element), then at the top of the feature file
`IconNamespace::import(path : "<svg element id>", version : "<microversion>");` (cross-document:
`"<doc>/<version>/<element>"`) and in the feature annotation `"Icon" : IconNamespace::BLOB_DATA`. A description
image works the same way (`ImageNamespace::import(...)`, `"Description Image" : ImageNamespace::BLOB_DATA`).

**Style** (from Onshape's own icon symbols, exported to icons/onshape_reference/): 20 x 20 viewBox; outline
#333333, fill #FFFFFF, secondary #999999, accent blue #1651B0. "Plus" variants = base glyph + blue "+" in the
bottom-right corner (Onshape's add-variable-button).

---

## Correction 36: getProperty (e.g. NAME) throws inside a feature's regeneration (2026-09-24)

**Symptom**: `@getProperty: Cannot get properties during feature regeneration in current context`
from `getProperty(context, { "entity" : part, "propertyType" : PropertyType.NAME })` in a feature
body. The same call works in the eval API.

**Fix**: read names in the EDITING LOGIC function (getProperty works there) and write them into a
string parameter -- Station geometry fills "Name prefix" from the picked part. `setProperty` is fine
in the body.

---

## Correction 37: opCreateOutline refuses a composite part (2026-09-24)

**Symptom**: `@opCreateOutline: INVALID_INPUT` when the SELECTED part is itself a composite
(4501 in the ski docs is an open composite of core strips). A plain part goes in as is.

**Fix**: for a selected composite, pass its members -- `qUnion([qBodyType(q, [BodyType.SOLID, BodyType.SHEET]), qFlattenedCompositeParts(q)])`.
Verified in the same spike: an OPEN composite accepts a point body (opPoint), wires, a nested
composite, and a part that already belongs to another open composite.

---

## Correction 38: inserting a custom feature with an array parameter through the REST API (2026-09-24)

**Symptom**: `400 Parameter stations ... does not match its feature spec`.

**Fix**: (1) every array item must carry EVERY parameter of the item spec, hidden ones included
(send empty query lists / defaults); (2) a custom enum's `namespace` is the namespace of the feature
studio that defines the FEATURE (`e<eid>::m<mv>`, same as the feature's), not blank. Read the spec
with `GET /api/v10/featurestudios/d/{d}/w/{w}/e/{e}/featurespecs`. Queries by deterministic id
(`fsapi.qids`, transient ids from an eval run) are stored as persistent queries.

---

## Correction 39: rational B-splines -- Onshape calls them splines, and evaluateSpline drops the weights (2026-09-24)

**Symptom 1**: an exact circular arc built as a rational quadratic (`opCreateBSplineCurve`, weights
[1, cos(sweep/2), 1]) is geometrically a circle, but Onshape shows and measures it as a SPLINE (no radius).
**Fix**: build arcs that must be known as arcs as sketch arcs -- `newSketchOnPlane` in the arc's plane,
`skArc` start / mid / end, `skSolve` -- and use that edge (fillet_wire tangent mode feeds it to
`opEditCurve`, which keeps it a circle; `evCurveDefinition` returns a `Circle`).

**Symptom 2**: `evaluateSpline` on that rational B-spline returned the NON-rational point: the "middle" of a
90 deg r10 arc was off the circle, and a circle through start / that point / end has r = 8.839.
**Fix**: never evaluate a rational BSplineCurve with evaluateSpline; compute arc points from the centre
(midpoint = centre + r * normalize(toA + toB)), or evaluate the created edge with ev* functions.

---

## Correction 41: re-running a FeatureList inside a pattern frame -- what remaps, what refuses (2026-09-24)

Verified live in case_pattern (tests T1-T5, "Case pattern tests" Part Studio), replaying listed
feature functions under `id + "caseK"` with `setFeaturePatternInstanceData(identityTransform())`:

- **Remapped onto the replay's copies**: only FeatureList parameters of the listed features (e.g. a
  native Query Variable "created by <listed feature>"; it builds `qCreatedBy(feature.key)`).
- **NOT remapped**: plain queries -- click selections (qCompressed persistent queries) and
  `qCreatedBy(makeId("<listed feature>"))` strings. They keep resolving to the ORIGINAL (template)
  geometry, in or out of the frame.
- **Refused in the frame**: a kernel op that edits geometry from OUTSIDE the list (opFillet on an
  existing block edge, opOffsetFace for Move face) throws SELF_INTERSECTING_CURVE_SELECTED. This is
  Query Pattern's "Move face as first feature" bug; it is not about being first. The same call with
  the frame popped succeeds -- but under a FRESH sub-id: re-calling with the aborted attempt's id
  fails again.
- Without any frame, listed FeatureList references do not remap either (resolve to the template).

**Fix pattern**: keep the frame pushed for the whole replay; retry a feature outside the frame only
on SELF_INTERSECTING_CURVE_SELECTED (any other failure may be an unremapped in-list reference,
and outside the frame it would act on the template's geometry). Tell users to reference in-list
geometry through Query Variables "created by", not clicks.

**Also**: fscheck flags a call through a function-valued PARAMETER (`listed(id)`) as UNDEFINED;
index an array of functions instead (`functions[i](id)`), as iterative_solve does.

---

## Correction 40: a custom feature with the std boolean step (2026-09-24)

**Symptom**: `booleanDefinition.operationType: Enum used as parameter type must be exported` and
"precondition analysis failed" after calling `booleanStepTypePredicate` / `booleanStepScopePredicate`.
**Fix**: `export import(path : "onshape/std/tool.fs", ...)` in the feature's file (std Thicken does the same).
Over REST the enum is then in the FEATURE tab's namespace (`NewBodyOperationType` with `e<eid>::m<mv>`).
Also: `processNewBodyIfNeeded` takes every body created under `id` as a tool -- delete helper bodies made under
`id` first. And opThicken (keep tools off) consumes its input faces: measure against a copy taken beforehand.

---

## Statistics

- **Total Corrections**: 40
- **Last Updated**: 2026-09-24
- **Most Common Category**: FeatureScript Syntax (8), Units Handling (3), Import Issues (1), Type System (1), Matrix/Array Indexing (1)
- **Critical Bugs Found**: 2 (Missing braces in control flow, Q matrix indexing)
- **Latest Additions**: annotation Default migrates into saved features; chord-length parameters for approximateSpline (23); frame length axis is not the velocity (26); isButton + clickedButton (27); side-of-splitter for trims (28); Matrix weights + degenerate rows for exact sectors (29)

---

*This log helps the FeatureScript expert agent avoid repeating mistakes and generate more accurate code on the first attempt.*

---

## Correction 42: evDistance to a BODY is ambiguous when the closest distance ties (2026-09-24)

**Symptom**: Move Along Edge started a cube from the wrong place: a 20 mm cube centred on a line's start
got `distance 0, parameter 0.1` (the point where the line LEAVES the cube, 11010 not 11000); a cube hovering
20 mm over an arc (bottom face parallel to it) started ~10 mm along. Results 0.5 mm off, silently.

**Fix**: never take "the" closest point of a body to a curve when the body can touch the curve or has a
face parallel to it -- evDistance returns ANY point of the tie. Project a defined point instead
(evApproximateCentroid, a mate connector's origin, a vertex).

---

## Correction 43: enum parameters show no label without UIHint.SHOW_LABEL (2026-09-24)

**Symptom**: a dropdown with no caption in the feature dialog (Move Along Edge "Name" / "Frame").
**Fix**: `annotation { "Name" : "Naming", "UIHint" : [UIHint.SHOW_LABEL] }` on every enum parameter
(std does the same). Booleans, lengths and queries show their names without it.

---

## Correction 44: a mate connector selection can arrive as its VERTEX; connectors cannot be named (2026-09-25)

**Symptom 1**: Move Along Edge turned every mate connector into a point. A filter term
`BodyType.MATE_CONNECTOR` admits ANY entity of the connector, and the dialog hands over its (non-sketch)
vertex, which then matched "vertex -> point".
**Fix**: resolve anything owned by a connector to the connector body up front:
`evaluateQuery(context, qBodyType(qOwnerBody(entity), BodyType.MATE_CONNECTOR))` -- take the EVALUATED
body (a query through the vertex stops resolving after opTransform moves the connector).

**Symptom 2**: `@setProperty: CANNOT_RESOLVE_ENTITIES` naming a mate connector (and getProperty NAME on
one returns undefined). **Fix**: never name connectors; report info.

## Correction 45: cut a wire with opSplitEdges at arc-length parameters, not with planes (2026-09-25)

**Symptom**: Trim curve + refused every multi-edge wire ("Each curve must currently be a single-edge
wire"), and its plane cutter (opSplitPart with a plane normal to the tangent) would also cut the curve
anywhere else it crossed that plane (tip/tail rises of a ski profile).
**Fix**: read the wire as one path (constructPath; per-edge lengths -> path fraction), map each cut to
(edge, arc-length parameter) and call `opSplitEdges(context, id, { "edges" : edge, "parameters" : [[t1, t2]] })`.
The wire stays ONE body (verified by eval API on a real 3-edge profile); separate pieces = opExtractWires per
span + delete the source. After the split, re-read the path and re-orient it to the old start point
(`reverse(path)`) before grouping edges, or piece order can flip. `TOLERANCE.zeroLength` is unitless -- compare
distances against `TOLERANCE.zeroLength * meter`.

## Correction 46: one parameter id may not appear in two branches of a precondition (2026-09-25)

**Symptom**: `Duplicate feature parameter 'startWidth'` + "precondition analysis failed" -- the whole tab stops
compiling. Declaring `region.startWidth` under `if (shape == CONSTANT)` (named "Width") AND under the `else`
branch is refused, even though only one branch can be active.
**Fix**: a distinct id per branch (`constantWidth`), and pick the right one in the body.
**Process**: `python fscheck.py ... | tail -4 && push` pushes even when fscheck fails (the pipe's status is
tail's). fscheck does not catch this one anyway -- push with `--check` and read the notices before moving on.

---

## Correction 45: pushproject can DUPLICATE edited blocks after Onshape re-pins a tab remotely (2026-09-25)

**Symptom**: after pushing a callee (xSect_GJ), Onshape re-pinned the caller's import itself (xSectCLT
now pinned ae5d... remotely). The next `pushproject` of the edited caller reported "1 pushed", then a
re-push "skipped" -- but the Onshape tab contained the new code block TWICE (duplicate `var EA`), which
does not compile. Push timeouts (30 s client read timeout; the server often still applies the write)
make it worse.
**Fix**: for pin chains, write the tab with `OnshapeClient().update_featurestudio_contents(...)` and
VERIFY by GETting the contents and comparing to the local file; re-pin callers with
`devtools/onshape/repin.py <project> <callee...>` (reads element microversions). Never trust
"pushed"/"skipped" alone on a chain.

---

## Correction 46: first/last control points are NOT the curve's ends on a periodic B-spline (2026-09-25)

**Symptom**: xSection's triangulation took section outline end points from `controlPoints[0]` / `[n-1]`. A cut
circle comes back from `evApproximateBSplineCurve` as ONE periodic curve, so its "ends" were off the curve,
the loop never closed ("outline left open and closed by a chord") and the r5 circle's area came out +1.4%
(+26% before forceNonRational).
**Fix**: evaluate the ends over the real domain `[knots[p], knots[size - 1 - p]]` (same as first/last knot for
a clamped curve). Never assume clamped for curves that come from the kernel.

---

## Correction 47: changing a feature's op-id structure breaks downstream references in existing documents (2026-09-25)

**Symptom**: Integrate footprint's output was changed from one `opExtractWires(id + "fpCompositeWire")` to a
two-stage extract (arcs to `id + "fpArcWires"` first). Same geometry, same final op id -- but the output edges'
identities derive from their source edges, so Bridging curve 1/2, Mirror 1 and Join wires 2 in the footprint
"Test" studio lost their references and errored.
**Rule (user policy 2026-09-25)**: correctness wins. Make the right change even when it re-identifies output
edges; a document that breaks either stays on the older version of the feature (its import pin) or gets its
references re-picked / replaced by the corrected feature. What IS required: check a studio that USES the feature
(`notices --monitor` statuses or a fingerprint) after every push and REPORT which downstream features lost
references, so the user knows what to re-pick when they update. (The two-stage extract was reverted once,
then re-applied on the user's call.)

## Correction 48: a saved feature's namespace (source document) cannot be changed through the REST API (2026-09-26)

**Symptom:** moving a custom feature's instances to a new document by editing `feature.namespace`:
- `POST .../features/featureid/{fid}` returns 400 "Feature does not match".
- `POST .../features/updates` (BTUpdateFeaturesCall-1748) answers OK but silently keeps the old namespace.
Updating only the `m<microversion>` part of a SAME-document namespace works.

**Fix: shim tab.** Replace the old feature tab's contents with a re-export of the new library, then update each instance
to the tab's new microversion:
```
FeatureScript 3083;
export import(path : "<new did>/<version id>/<element id>", version : "<element microversion>");
```
Instances keep their feature ids, so `<fid>result`-style downstream references and query variables survive.
The exported feature names and parameter ids must match the old ones.
Used for Composite intersection (Mindbender Cores) and Subtract Composite (20BSS Tooling_Prep): fingerprints
identical. Backups of the old tabs: composite_part_tools/legacy_backups/ (*.fs.txt so the sync does not push them), and reviews/2026-09-25_tools_review/composite_intersection_remote_snapshot.fs.

---

## Correction 49: `repin.py push` silently reverted an intentional cross-document pin bump (2026-09-26, FIXED)

**Symptom**: edge_offset_utils was edited to import curve_core from Curve_tools V9; `repin.py push` printed
"cross-document pins taken from Onshape ... MATCH", and the tab went up pinned to V7 (the pin live in Onshape) --
without ArcSourceFit / shapeRuns, so every caller would have failed to compile.
**Cause**: `adopt_remote_pins` (added so a stale local file cannot undo a pin the user moved while versioning)
replaces EVERY cross-document pin in the local file with the live one, matched by element id -- including the
one you meant to change.
**Fix**: push the tab whose cross-document pin you are bumping with a direct write (OnshapeClient
update_featurestudio_contents, then GET and compare), and grep the local file afterwards; use `repin.py push`
only for tabs whose cross-document pins you did not change.
**Fixed the same day** (tools-review session): `adopt_remote_pins` now keeps, per import, whichever of the local and
the live pin is the NEWER library version (by the version's createdAt), so a deliberate bump survives and a stale
local file still cannot push an old version back. Renumbered from a second "48" (two sessions wrote one).

---

## Correction 50: FeatureList functions cannot be stored in a variable; getAllVariables omits query variables (2026-09-26)

Measured live in the "Case pattern v2 spikes" studio (case_pattern/spike_v2.fs):

- `setVariable(context, name, <FeatureList map>)` and `setVariable(..., values(<FeatureList>))` both throw
  "Execution error". Plain lambdas store fine (`setVariable(context, "x", {"f": function(x){...}})` works),
  so it is the generated feature functions that are refused. A feature cannot hand its FeatureList to a
  LATER feature through a variable.
- **Working route**: the later feature takes a FeatureList of the EARLIER feature and calls that feature's
  own function inside its pattern frame; the earlier feature detects `isInFeaturePattern(context)` and
  replays its own listed functions with the id it was given (`functions[i](id)`). Frame remapping still
  works through that extra level (Query Variable "created by" followed the replayed copy; case 1 untouched),
  and a sketch in the list rebuilt (not the correction-31 no-op -- the extra level is the feature wrapper's
  own startFeature, which is fine).
- `getAllVariables(context)` does NOT list query variables (neither one set by setQueryVariable in a custom
  feature nor a native Query Variable feature). A before/after diff cannot find query variables a replay set.
- Editing logic DOES see variables set by earlier features (getVariable / getAllVariables in the editing
  logic function) -- but it only runs on a parameter change in the open dialog, never on open or REST insert.
- Suppression by expression (feature.suppressionState = BTMSuppressionStateExpression-1811, a
  BTMParameterQuantity-147 with expression "#flag") IS re-evaluated when the feature function is replayed
  from a FeatureList with a different #flag; a suppressed feature is still in the FeatureList.
- **Where the frame must live (v2 build, 2026-09-26, "Case pattern tests" T1-T7):**
  - Everything a called feature creates must be under THAT feature's id: ops run under an id outside
    it (e.g. caseId while the Close case runs as caseId.s0.<close>) are discarded silently.
  - A frame pushed by an OUTER feature cannot be popped inside a feature it calls ("Execution error").
    So the feature that replays the list pushes the frame on its OWN id and calls the list with that id;
    then it can pop it to retry an outside-geometry edit (SELF_INTERSECTING_CURVE_SELECTED), as v1 did.
  - Called under a prefix OUTSIDE any frame, the calling feature's FeatureList keys arrive as
    prefix + original id ([pattern, case_B, <feature id>]); valuesSortedById finds none of them (returns
    []), so sort by makeId(key[size(key) - 1]). Inside a frame the reverse: sorting by original ids is
    refused "out of pattern scope" -- use the keys as given.
  - A Query parameter (incl. a query variable pick) is resolved when the feature is CALLED. Outputs that
    depend on variables the replayed features set must be read by a SECOND call after the replay (the
    first call sees the previous case's values -- outputs lagged one case).
  - References inside the list resolve relative to the id a feature is called with, so every feature of
    one case must run under the same prefix (splitting a case over several calls broke "created by").
- **Queries on outside geometry inside the frame (2026-09-26, RD 20FOU 28 Offset+ sides flipped; test T9):**
  inside a pattern frame, a query that resolves through the history of an op from OUTSIDE the instance
  (a click, or a query variable bound to such a selection) is refused "@evaluateQuery / @isQueryEmpty:
  Operation id <op> is out of pattern scope <instance>". Native features cope; custom ones either throw
  ("Execution error") or -- worse -- read it as EMPTY: Offset+ then had no side reference and offset along
  the surface normals (right where the normal pointed inward, flipped elsewhere).
  Fixes: (1) Case pattern binds each input as the entities it resolved OUTSIDE the frame
  (`qUnion(evaluateQuery(context, selection))`) -- T9b passes; (2) Reference_Side referenceProbe throws
  when a reference is picked (`isPicked`) but resolves to nothing, instead of returning "no reference".
  User rule: pass outside references (side references, mate connectors) in through a Define case input,
  never click them inside a repeated feature (T9a still flips on an old Reference_Side version).


---

## Correction 51: a parameter "Filter" must be written inline, not as a const (2026-09-27)

**Symptom**: `join_profile_surfaces:70:60 definition.insideProfile: Invalid filter` (warning, one per use) when the
annotation said `"Filter" : PROFILE_FILTER` with `const PROFILE_FILTER = EntityType.BODY && BodyType.SHEET && ...;`.
**Fix**: write the filter expression inline in every annotation. fscheck does not catch it; `pushproject --check` does.

---

## Correction 52: a merge kills exact AND identity-robust edge references; only tracking follows (2026-09-27)

**Symptom** (RD 20FOU 28): a fillet on Split+ cut edges extracted by Extract variables failed after the lofts were
merged onto those edges (loft "Add"): all four variables resolved to 0 edges.
**Measured** (sheet split, then a strip united onto the cut edge): after the union the transient query finds 0 edges,
`makeRobustQueriesBatched` (identity-preserving freeze) finds 0, `qUnion([now, startTracking(now)])` finds the one
new two-sided edge.
**Fix (user decision)**: following an entity through later edits is the CONSUMER's explicit choice, not the
producer's: producers publish the entities as they leave them (untracked), and Extract variables' Track option
(Evaluate on use off, Track on) stores freeze + startTracking, which finds the merged edge. A producer that tracks
silently makes every consumer's key drift (a later fillet's edges join it). Tried and reverted the same day: Split+
publishing tracked cut keys. Applied to ALL Reference_Side features: `settledOutputs` (reference_side_utils)
resolves every published query to the entities present when the feature finishes; features still track INTERNALLY
where their own later steps need it (Mutual Trim+ through its merge, Join through its merge / fillets). Caveat: the
standard keys embedStandardOutputs derives itself (outputEdges / outputFaces / outputVertices) are rules on the
settled bodies and see later changes to those bodies -- that lives in Variable_tools.

## Correction 53: strings -- no `<` ordering, no `(?i)` in regex, `${` breaks a string literal (2026-09-28)

**Symptoms** (publish_tools/station_tools/station_table.fs):
- `sort(..., function(a, b) { return a.title < b.title ? ... })` -> table error "Can not compare string and string".
- `match(s, "(?i).*x.*")` -> "@match: Invalid regular expression: Invalid special open parenthesis".
- A literal containing `${` (a regex escape class) -> "String ... is not a valid token" (eval API; the tab push
  showed no compile notice, the table then failed at run time).

**Fix**: order by a number (or keep model order); match case-sensitively (or build [aA] classes); avoid regex
escaping by mapping every non-safe character to `.`: `replace(filter, "[^A-Za-z0-9 _-]", ".")`.

## Correction 54: drawings -- wires, references, and custom tables (2026-09-28)

- A drawing view shows wire bodies only if created with `"includeWires": true` (onshapeCreateViews);
  onshapeEditViews sets the flag but does not re-render.
- Dimension references must name edges by the view's `deterministicId` (views/{vid}/jsongeometry); `uniqueId`
  resolved to other lines. Text positions are sheet mm from the lower-left corner.
- Geometry lying on a part's far face is hidden in a view (hidden lines off): build drawing aids in FRONT of the
  part along the view normal.
- A custom table inserted into a drawing brings EVERY table its function returns; give the table a precondition
  parameter (e.g. a name filter): it appears in the drawing's "Select a custom table" dialog, per insertion.

## Correction 55: opSplitPart pieces belong to the ORIGINAL body's feature; an evaluated target is lost (2026-09-28)

**Symptom** (reference_side/referenced_part.fs, first build): a Split+ subfeature given an evaluated query
(`qUnion(evaluateQuery(...))`) as its target failed with SPLIT_SELECT_TARGETS on its second tool, or found no
pieces; the same split with a `qCreatedBy(...)` target worked.

**Measured (eval API)**: after `opSplitPart` (plane or sheet tool, KEEP_ALL):
- `qCreatedBy(<split id>, BODY)` is EMPTY -- the pieces are attributed to the feature that made the original body
  (`qCreatedBy(<that feature>, BODY)` returns both pieces);
- the evaluated (transient) query of the original body resolves to NOTHING;
- `startTracking(context, target)` taken before the split returns every piece, also through a second split.

**Fix**: split a HISTORY query (`qBodyType(qCreatedBy(id, BODY), SOLID)` for bodies your own feature made), or
`qUnion([targets, startTracking(context, targets)])`. Split+ does the latter since 2026-09-28, so an evaluated
target -- a Case pattern input (pre-resolved, correction 50) or a composite feature's query -- keeps its pieces.
The `qUnion([pieces, qCreatedBy(splitId)])` idiom adds nothing.

## Correction 56: the tangent-arc (natural radius) quadratic cancels for near-symmetric stations away from x = 0 (2026-09-28)

**Symptom** (footprint/fpt_analyze.fs, natural radius ported from beamBuilder `_natural_radius`): the S14 test
fixture gave natural radius (inflection) 14.000 m centred on X = 0 but 14.052 m at X = 16000 (AF5) and 13.987 m
for AF6 -- the same geometry translated.

**Cause**: the centre-x quadratic `A cx^2 + B cx + C = 0` has `A = a2 - a1` (difference of the two station
heights above the waist). A symmetric ski gives A ~ 1e-12 instead of 0, and the textbook `(-B + sqrt(disc)) / 2A`
subtracts two numbers of size |B| ~ 3e4: 1e-11 mm of width difference at x = 16 m moved R by 4 mm, 1e-12 mm by
42 mm. beamBuilder has the same formula (its own values are exposed on near-zero-taper skis modelled away from
x = 0).

**Fix**: cancellation-free roots, same values and order: `q = -(B + sign(B) sqrt(disc)) / 2`, roots `q / A` and
`C / q`. Agrees with the textbook form to 6e-13 relative wherever that one is well conditioned.

**Lesson**: any quadratic whose leading coefficient is a DIFFERENCE of nearly equal measurements needs the stable
root form; check a translated copy of a symmetric fixture.

## Correction 57: opCreateOutline re-fits silhouette edges -- never take curvature from an outline (2026-09-28)

**Symptom** (publish_tools Export primitive, RD 20TAC): the footprint radius at FCP read R 3.93 m on the ski and
R 8.82 m on its mirror image; the volume's own base edge there is an exact R 1.020 m arc on both.
**Cause**: `opCreateOutline` returns the silhouette as re-fitted splines: positions are fine, but end curvature is
garbage (the same edge read R 1.047 / 1.02 / 1.287 / 3.93 m along it; its tip neighbour started at R 2.7e8 m).
**Fix**: take curvature-sensitive geometry from the body's own edges. Export primitive finds the base periphery as
the edges bounding exactly one "base" face (faces the section's bottom wire lies on, probed with qContainsPoint at
interior points) and copies those with opExtractWires. Exact arcs stay exact.

## Correction 58: importDerived "include mate connectors" skips connectors that hang off sketches (2026-09-28)

**Symptom**: a REST-inserted derive of Design Master @ V1 (parts by deterministic id, includeMateConnectors true)
brought no FCP / ACP / MRS / MP connectors: they are attached to sketch entities, not to a derived part.
**Fix**: add a second query to `partQuery`: `qBodyType(qEverything(EntityType.BODY), BodyType.MATE_CONNECTOR)`
(devtools/onshape/build_primitive_tests.py). Connectors cannot be named (correction 44), so pick them by position:
`qContainsPoint(qOwnedByBody(<connectors>, EntityType.VERTEX), point)`.

## Correction 59: a reference point taken from a sample grid is only as good as the grid (2026-09-28)

**Symptom**: xSection analyzeBaselineGeometry (V57) gave FRCPl 131.25 / ARCPl 49.26 on RD 20TAC FULL_BASELINE where
FRCP sits exactly 130 / 50 from the contacts. It snaps fcp_pt / acp_pt to the nearest of 200 MIDPOINT samples (exact
only when FCP / ACP is a chain end, as on Generate baseline output); FULL_BASELINE runs on into the tip and tail.
**Fix** (call site, Export primitive): re-measure FRCPl / ARCPl / FCPh / ACPh from the baseline point exactly at the
FCP / ACP x (Newton on the chain). Upstream FIXED 2026-09-28 in the xSection workspace (not yet versioned):
fcp_pt / acp_pt / mrs_pt = chain point at the exact X (chainPointAtX, bisection); RD 20TAC now 130.000 / 50.000,
FCPh 5.000 / ACPh 0.500 (were 5.098 / 0.485). Remove the Export primitive workaround once it pins that version.
**Lesson**: solve for a named point (x = x_FCP) instead of picking the nearest sample; a test fixture whose contacts
are chain ends hides the error.

## Correction 60: sketch text as geometry -- extract the REGION edges, not the text curves (2026-09-28)

**Symptom**: `@opExtractWires: EXTRACT_WIRES_OVERLAPPING_EDGES` on `qCreatedBy(sketchId, EntityType.EDGE)` of an FS
sketch with `skText` (also with only the sketch's wire bodies' edges).
**Cause**: a solved text sketch makes one wire body per glyph curve (185 for "RADIUS 10") AND a sheet body of the
text regions; the glyph curves overlap each other / the region edges.
**Fix**: `opExtractWires` on `qOwnedByBody(qBodyType(qCreatedBy(sketchId, EntityType.BODY), BodyType.SHEET),
EntityType.EDGE)` -> one closed wire per glyph loop; then `opDeleteBodies` the sketch bodies. The wires go into a
closed composite fine. Text box: firstCorner / secondCorner height = text height, width is ignored (text runs on).
Sketch plane `plane(origin, vector(0, -1, 0), vector(1, 0, 0))` reads correctly in a front view (sketch y = +Z).
Also: wire and point bodies keep `PropertyType.APPEARANCE` (read back), and `opPattern` copies name + appearance
(copyPropertiesAndAttributes defaults true) -- set them on the seed first.

## Correction 61: unwrap by the point's own base, not the centreline at the same x; `!=` binds tighter than `~` (2026-09-28)

**Symptom** (Export primitive, the user's tail bite): the unwrapped footprint showed a bite cut into the tail as a
vertical line. **Cause**: each periphery point took u from the mid-plane bottom wire at the SAME x, clamped past the
wire's end; the bite cut the centreline back, so every point beyond it got the same u. Also the vertical bite wall made
the profile's TAIL extreme point a tie: the bottom wire climbed the wall and the wall was taken as a base face.
**Fix**: map each point by its foot (nearest point in XZ) on sections of the BASE FACES at several y, interpolated in y,
extra sections at the own y of points no section reaches; break extreme-point ties towards the lower point. Check an
unwrap by isometry: on a base extruded along y the unwrapped lengths and area equal the base's.
**Also**: in FS `name != prefix ~ " X"` parses as `(name != prefix) ~ " X"` ("First operand of logical or conditional
operator must be boolean, value is string") -- parenthesise concatenations inside comparisons.
**Perf**: per-point Newton with ValueWithUnits vectors cost ~1.2 s for 1600 feet; the same in plain numbers on a
cubic-Hermite table plus ONE batched exact kernel correction cost ~0.6 s.

---

## Correction 60: calling a feature function under a new id WITHOUT a pattern frame drops its outside references (2026-09-29)

**Symptom**: inside a Case pattern (v2), every reference a repeated feature CLICKED on geometry from before the body
(face, vertex, mate connector) resolved to nothing: evaluateQuery 0, isQueryEmpty true, ev* CANNOT_RESOLVE_ENTITIES;
native Extrude up-to-face failed EXTRUDE_SELECT_TERMINATING_SURFACE; Offset+ lost its side reference (RD 20FOU 28).
**Measured** ("Case pattern outside-ref spikes" studio, case_pattern/spike_outside.fs, devtools/onshape/spike_case_outside.py):
- native Linear pattern (Reapply features) and a top-level feature that pushes a frame and calls a FeatureList:
  every outside click resolves (native and custom features alike).
- Outer feature calls an inner feature (a FeatureList of the outer) which replays ITS list (Case pattern -> Close case):
  outer no frame + inner frame (= v2): outside clicks EMPTY. Outer frame + inner none: EMPTY. Outer frame + inner frame
  (nested): outside clicks RESOLVE. Rule: every call of a feature function under a foreign id must be inside a frame.
- The SELF_INTERSECTING_CURVE_SELECTED retry (edit of outside geometry run with the frame popped) works only when NO
  frame is left: with the outer frame still pushed, popping the inner one is not enough. Pushing the outer frame on a
  sibling id instead of the call id: "Execution error".
**Consequence**: outside clicks and outside edits need different call shapes. Case inputs / Shared references work in
both because they are bound as resolved entities before the call.
- **One-level executor works for everything** (row O5): a feature that holds the body FeatureList ITSELF, pushes the
  frame on its own sub-id (identity transform) and replays: outside clicks resolve, in-list QV "created by" remaps,
  and an outside-geometry edit (fillet of a case input's edges) succeeds through the pop-and-retry. So the fix is the
  call shape (the executor must own the list), not the transform.


---

## Correction 62: a NEW length parameter's default is migrated into saved features in MILLIMETRES (2026-09-29)

**Symptom** (publish_tools Export primitive): a new `isLength(definition.radiusAxisMin, { (meter) : [0, 10, 10000] })`
showed up in every SAVED feature as the expression `10.0*mm` (REST features list), so the radius axis ran from -10 mm and
the plot below it was cut away. New isReal parameters added in the same pass migrated correctly (`0.02`, `450.0`), and an
existing parameter whose default changed (curvatureScale 5 -> 50 mm, Station definition variableName "stations" -> "")
kept its saved value.
**Cause**: correction 25's migration writes the bound spec's default NUMBER with the document's length unit (mm), not
the bound spec's unit.
**Fix**: give a new length parameter a millimetre bound spec, or (done here) make it a plain number in the unit it names
("Radius axis min (m)", isReal) under a fresh id (`radiusAxisLow`) so the stale `10.0*mm` is ignored. Check a new
parameter's migrated value in the REST feature list of an existing feature before trusting the default.

---

## Correction 61: migrating features by REST -- versions, sub-features, and downstream clicks (2026-09-29, RD 20FOU 28)

- **A feature cannot be moved to another library version by REST**: updating it with a new `namespace` is refused
  ("Feature does not match"); `features/updates` (BTUpdateFeaturesCall-1748) returns OK but keeps the old namespace.
  Replace instead: insert a new feature at the old one's index (POST /features inserts AT the rollback bar and moves
  the bar down one; move the bar with POST /features/rollback {rollbackIndex}), then delete the old one.
- **Copy `subFeatures` too**: a mate connector created inside a feature's dialog is a sub-feature, referenced as
  `qCreatedBy(id + "<subId>")`. Copying only the parameters loses it (Define core case's #inside_pt).
- **Enums of different library versions never compare equal**: a v2 Define case's CaseValueKind values fail v3's
  checks, so every feature of a library family must be on the same version.
- **Workspace GETs return clicks as qCompressed strings**, not deterministic ids, and they cannot be evaluated outside
  their feature (`id` is unbound). A VERSION's GET returns deterministic ids; check them in the workspace with
  qTransient at `rollbackBarIndex`. The eval API evaluates at the CURRENT rollback bar unless `rollbackBarIndex` is
  given -- pass it explicitly (a "baseline" taken at the user's bar missed half the tree).
- **Downstream clicks die when the creating feature changes** (geometry once made by Case pattern, now by Close case).
  devtools/onshape/repick_by_geometry.py re-points them: describe each clicked entity in the version (type, owner name,
  box centre / MC origin), find the unique match in the workspace at the feature's position, write deterministic ids.
  Sketch-internal references (projected edges) are not parameters and are not covered.

---

## Correction 63: approximateSpline through sparse samples interpolates and rings; getProperty in an error message (2026-09-29)

**Symptom** (Unwrap, "wobbly edges"): flat outline splines within 0.005 mm of every sample but 325 um off the
sampled curve BETWEEN samples (topsheet tip U-turn), 104 um at its tail, 19 um at 4305's wing roots; curvature combs
with 13-20 inflections. **Cause**: approximateSpline only measures the points it is given; with sparse or uneven
samples it adds knots until it passes all of them (CP count = point count, i.e. interpolation) and the interpolant
rings between them. **Fix**: fit through the samples PLUS points of the curve the sampling was validated against
(unwrap.fs `fitPoints`: 3 cubic-Hermite points per span, chord length, quadratic slopes, true end tangents): worst
between-sample miss 325 -> 5.5 um and FEWER control points. Measure a fit between its samples, not at them.
**Also**: a helper that called getProperty(NAME) inside a regenError message (unwrap_part `partName`) made every
refusal surface as "@getProperty: Cannot get properties during feature regeneration" and lost the real message --
never call getProperty in a feature body, not even in an error path (correction 36).

## Correction 64: a NEW parameter on a custom TABLE breaks every table already placed (2026-09-29)

**Symptom**: after adding "Show definitions" (`definition.showDefinitions is boolean`) to the Primitive tables
custom table, existing tables in drawings failed to update ("table could not regenerate"); the REST fstable call
without the parameter returned `Precondition failed (definition.showDefinitions is boolean)`.

**Cause**: `defineTable` has no defaults map (std/table.fs: it just calls the function), and a placed table's
parameters are not migrated with the annotation default the way feature parameters are (correction 25). A placed
custom table's parameters also can't be edited in a drawing afterwards.

**Fix / rule**: avoid adding parameters to a published custom table; put new options on the producing FEATURE (its
published data) or add a new table choice to an existing enum. If a parameter must be added, every placed table must
be deleted and re-inserted. REST fstable callers must pass every parameter.

## Correction 65: a declaration placed between import lines breaks the whole tab (2026-09-29)

**Symptom**: after adding `export enum StationDatumUse` to station_geometry.fs, the "Station tests" Part Studio
regenerated with ZERO bodies (even the upstream Derive produced nothing) and feature states read OK instead of INFO.
fscheck was clean; `--check` could not run (browser signed out).

**Cause**: the enum was inserted after the last plain `import(...)` but BEFORE `IconNamespace::import(...)`. All
imports (including namespace imports) must precede every declaration; the tab failed to compile and every Part Studio
using it came up empty.

**Fix / rule**: put new declarations after the LAST import line (look for `Namespace::import` too). When `--check`
can't run, verify by counting bodies in a studio that uses the tab (eval `size(evaluateQuery(context,
qEverything(EntityType.BODY)))`) -- a sudden 0 means a compile failure.

---

## Correction 66: evVolume's default accuracy moves between regenerations of identical geometry (2026-09-29)

**Symptom** (Unwrap regression): 4103 over FULL_BASELINE reported volume x0.998559 in one run and x0.998538 in the
next with no code change ("non-deterministic"); later x0.998942.
**Measured**: all 80 vertices and 82 face points of the body identical to 0.1 nm across regenerations, face areas
identical to 1e-6 mm2. Only `evVolume` moved: default (= MEDIUM) 10186.62 / 10186.83 / 10190.73 mm3, HIGH 10187.8097 /
10187.8106 / 10188.8298 (the last after a real code change). The default estimate is ~1e-4 off and depends on the
server's state (a freshly built body vs a cached one); LOW is 0.4 % off.
**Fix / rule**: any volume that is reported or compared uses `"accuracy" : VolumeAccuracy.HIGH` (~20 ms per call).
HIGH still moves by ~1e-7 relative between cached and fresh states: compare to 6 digits at most.

## Correction 67: the eval API ignores rollbackBarIndex in the JSON body -- pass it as a QUERY parameter (2026-09-29)

**Symptom**: `POST .../featurescript` with `{"script": ..., "rollbackBarIndex": 7}` evaluated at the studio's own
rollback bar (6), so the last feature had "no embedded output". As a query parameter (`?rollbackBarIndex=7`) it
evaluates the whole tree. Same as `configuration` (fseval.py docstring). devtools scripts using
`c._request("POST", path, {"rollbackBarIndex": n}, body)` were right; check_unwrap_regression.py now does the same.
Also check `rollbackIndex` in `GET .../features`: a UI session had left "Unwrap_Testing Copy 1" rolled back before U3
(its feature state then reads OK, not INFO).

## Correction 68: Part-mode spline tools rang between adaptive rows; 1e-7 m fits are slow and buy nothing (2026-09-29)

**Symptom** (Unwrap Part mode, 4103 over FULL_BASELINE): flat part 45 um off its source at the ledge taper (both
inside and outside), 0.01 % of its volume missing; the 16-point-per-face reverse check passed (5.5 um).
**Cause**: correction 63 in unwrap_part.fs: every PRISM tool curve was fitted to 1e-7 m through the adaptive row
samples, so approximateSpline interpolated them and rang between them.
**Fix**: fit through the row plus the midpoint of each span on the cubic the row was refined against (fitPoints,
densify 1) to 0.5 um (`UNWRAP_PART_ROW_FIT_TOL`; the rows are only known to shape tolerance / 4). Three points per span
at 1e-7 cost the CORE ~3 s; the samples alone at 0.5 um were as accurate on every test case and 0.85 s faster.
**Measure a curve between its samples** (forward check: source face points mapped and measured to the result), not at
them, and not with 16 points per face.
**Also**: an auto-backup commit made by ANOTHER agent's push (`[AUTO-BACKUP] Before pushing publish_tools`) commits
your uncommitted working files too: `git show HEAD:` is then not your "before" state. Pin the before-state commit
hash when you start (here 6440d32).

## Correction 69: opCreateBSplineCurve wants G1; triple interior knots are fine (2026-09-29)

**Symptom**: joining line, arc and spline pieces into one cubic B-spline (triple knot at each joint) failed with
`BSPLINECURVE_NOT_G1` wherever two pieces met with a kink of even 1e-5 deg.
**Measured**: a rational cubic with a triple (C0) interior knot and geometrically tangent pieces is accepted as ONE
edge; its extrusion is ONE face (EXTRUDED); curvature jumps cleanly (0 -> 1/r) at the knot. An arc goes in exactly as
rational quadratic Beziers of <= 90 deg (middle weight cos(half sweep)) raised to cubic in homogeneous coordinates
(G1 = (H0 + 2 H1) / 3, G2 = (2 H1 + H2) / 3). A piece's weights may be scaled freely; shared control points must
carry one weight.
**Fix** (unwrap_part `g1Runs`): make every joint G1 by turning the SHORTER control arm of a Bezier side onto the other
direction, after cutting that Bezier close to the joint (de Casteljau) so the arm is short enough that the move stays
under a set tolerance (4/9 x arm x angle). Turning a whole long arm instead moved a 1.5 m wall by 0.76 um (-5.8 mm3).

## Correction 70: face departure sampled on a face's trim needs more than a 5 x 5 grid (2026-09-29)

**Symptom** (PRISM): 4803's tail walls over FULL_BASELINE measured 6.3 um from a plan-view extrusion, were accepted
at a 0.01 mm shape tolerance, and the result missed the source by 12.6 um (the reverse check, 16 points per face,
passed at 9.75 um). 4103's tip walls: 4.4 um measured, 13.5 um real miss at 0.005 mm tolerance.
**Cause**: a face whose parameter box reaches past its trim is re-read with `returnUndefinedOutsideFace`; on a 5 x 5
grid only a handful of points survive, and the departure (half the spread across the face) came out at half the truth.
**Fix**: the trim re-read uses 13 x 13 (`UNWRAP_PART_PRISM_TRIM_GRID`): 10.5 / 10.4 um. 4803 then goes to the exact cell
rebuild (0.24 um); 4103 at 0.005 mm is refused. Grid code takes G from the sample count; `capEnvelope` interpolates
between grids of different sizes.

## Correction 71: an overshoot line at a shallow junction becomes a sliver face; overshoot < junction distance leaves a gap (2026-09-29)

**Symptom** (PRISM arrangement): (a) 8 PLANE faces 8 um wide on the CORE's top where a profile chain met another at
0.53 deg; (b) 4401 over FULL_BASELINE at 0.005 mm: "no plan cell holds material".
**Cause**: a chain end on another chain got a separate straight overshoot edge. (a) The other curve, fitted to 0.5 um,
crossed that overshoot 8 um past the end (a 0.07 um miss at 0.53 deg), so the overshoot bounded a cell. (b) The end sat
29 um from the other chain (the junction test allows 3 x tolerance, min 50 um) but the overshoot was 20 um: no crossing,
the outline stayed open.
**Fix**: the overshoot is part of the chain's own curve (a line longer, an arc further round, a spline by a tangent
line inside the same B-spline), and at least 4 x the end's distance from the chain it lies on.


## Correction 72: a reverse check cannot see a MISSING feature -- check forward too (2026-09-30)

**Symptom** (Unwrap Part mode, a round hole in a curved span): the cell rebuild built the flat block with the hole
FILLED, and its reverse check (16 points per result face mapped back, distance to the source) passed at 0.02 um.
**Cause**: a filled hole leaves no result face to sample; the top face spans it, and 16 points on a 300 mm face never
land over an 8 mm hole. **Fix**: `pieceCheck` = reverse + forward (5 points per SOURCE face mapped forward, distance to
the result): the hole wall's points are 4-5 mm from any result face. Cost on the CORE over FULL_BASELINE: 3 x 3 points
+0.75 s (9.05 -> 9.8 s), so 5 points. Any rebuild-and-check scheme needs both directions.

## Correction 73: the sheet union sews edges up to 1 um apart, and fails the WHOLE union from 2 um (2026-09-30)

**Measured** (two sheets, shared edge moved by a gap, in-plane and normal): gaps 0 .. 1 um -> one sheet; 3-7 um in plane
-> two sheets, no error; 2 um, any normal gap >= 2 um, overlaps >= 3 um -> BOOLEAN_INVALID thrown, nothing united.
**Consequence** (Unwrap Faces mode): faces rebuilt on separate supports and split by a shared curve (opSplitFace
projects it onto each) sew only if (a) both supports follow the true surface to well under a micron (a best-fit plane
4.2 um off gave 7.8 um gaps) and (b) the shared curve itself is that close (neighbours at an angle project it apart by
its own error: 5 um curves, 2-3 um gaps). Fix: exact supports only within 0.5 um, fitted ones to 0.1 um, shared edges
fitted to 0.25 um; if the union still throws, unite one face at a time. Reusing a neighbour's IMPRINTED edges as the tool
made it worse (imprints split at different vertices: 26 sheets).

## Correction 74: a chart foot warm-started from far away converges on the WRONG span (2026-09-30)

**Symptom**: a B-spline fitted through a CORE ledge wall's flat grid (17 x 5 points, 156 x 0.6 mm) came out 250 mm tall
and nowhere near the face (curves 4 mm off, SPLIT_FAILED); on the topsheet's step face a 0.56 mm "notch" appeared.
**Cause**: the grid was mapped row by row with `chartFootAt(chart, p, previous)` and `previous` carried from the end of
one row (x 682) to the start of the next (x 527): the Newton from that seed converged -- residual below 1e-7 -- on a foot
of another span. `chartFootConverged` checks the tangential residual only. **Fix**: warm-start along a row only (reset
at each row). Any loop that jumps far between consecutive points must seed cold (`undefined`).

## Correction 75: closed curves -- clamped closed splines are refused; opFitSpline closes periodic; closed sketch curves (2026-09-30)

- A clamped B-spline whose first and last control points coincide (a closed edge fitted like an open one, or open pieces
  joined with triple knots into a loop) is refused: `opCreateBSplineCurve` BAD_GEOMETRY. Unwrap's Edges pipeline had
  never met a closed edge (a hole's circle) until Faces mode. `opFitSpline` through points whose last equals the first
  makes a PERIODIC curve and is accepted; two such fits with the same `parameters` get identical knots (a ruled surface
  between them is exact: control net side by side, degree 1 across, `isUPeriodic`).
- A ring of faces with curvature jumps (a slot: arc -> line) interpolated as ONE closed spline rings at the jumps
  (8.6 um); per-face patches sewn by a union do not.
- A closed sketch curve also bounds a region face: `opExtractWires` on `qCreatedBy(sketch, EDGE)` fails with
  EXTRACT_WIRES_OVERLAPPING_EDGES; take the edges of the sketch's WIRE body.
- `opDeleteFace` (heal, leaveOpen false) on a hole's faces fills it: the healed block matched the unholed one to 0 mm3.

## Correction 76: a face's parameter box can be far larger than its trim (2026-09-30)

A CORE wall 0.6 mm tall sat in a parameter box whose v range spanned the whole extruded surface; a grid over [0, 1]^2
sampled mostly the surface's extension. Take the trim's parameter range from its edges (edge points ->
`evDistance(point, face).sides[1].parameter`) before gridding a face (Unwrap `faceTrimBox`). Also: `box` is a reserved
word -- again (the variable in that function first).

## Correction 77: Wrap and Loft lost its arcs in planar mode -- the projected from-path had no exact line (2026-09-30)

With `sourceEdgesArePlanar` the from-path is the to-edges projected onto the source plane. Every projection was refit with
`approximateSpline` + `opCreateBSplineCurve`, so a true LINE to-edge became a SPLINE; `buildFrenetPath` then only
"promoted" it (`isLine` true, `frameSamples` set) and `isExactLine` (correction of 2026-09-25) rejected it, so
`linearRegionMove` never fired and every source arc between FCP and ACP was sampled and refit as a spline.
Fix (wrapAndLoft_i + public): a LINE to-edge projects to `opPolyline` of its two projected ends (a true line).
Also in `linearRegionMove`: anchor on the MIDDLE probe, judge probes by foot arc length against the from-edge's span, and
give the seam checks `CM_SEAM_TOL` (1 um), so an arc ending exactly on a reference vertex (FCP / ACP) is not rejected by rounding.
Verified in a scratch copy of the K2 ski doc: 39 splines -> 33 circles (FCP..ACP wrapped + both offsets); the tip/tail
splines stay (they bend onto the curved profile). Needs a curveMapping version, then repin public wrapAndLoft + the Design doc.

## Correction 78: opCreateOutline refuses faces (INVALID_INPUT); copy them as sheets first (2026-10-29)

`opCreateOutline` takes bodies, not faces: a face selection as `tools` gives INVALID_INPUT (Export tag, selected-faces
DXF). Copy each face with `opExtractSurface({ "faces" : face })` (a normal sheet body). A PLANAR face parallel to the
target plane is its own outline: translate the copy along the plane normal onto the plane (`opTransform`) and skip the
outline; any other face goes to `opCreateOutline` as a sheet, then delete the copies. Planar test:
`evSurfaceDefinition(face) is Plane && abs(abs(dot(normal, targetNormal)) - 1) < 1e-9`.

## Correction 79: a plane sheet trimmed into a section is still a construction plane -- extract it (2026-10-29)

`opPlane` + `opBoolean` SUBTRACT_COMPLEMENT leaves the section region as a plane-typed sheet: the tree shows a child "Plane 1"
under the feature, the region is not a normal surface to export, and the plane cannot be deleted without losing the region.
Copy its face with `opExtractSurface({ "faces" : qOwnedByBody(region, EntityType.FACE) })` and `opDeleteBodies` the plane;
the extract is an ordinary sheet and the child disappears. Check: faces created by the feature == sheets expected
(check_export_tag_tests.py "faces_equal_sheets"). Station geometry's own `sectionRegion` still returns the plane sheet.

## Correction 80: wires cannot be unioned, and opExtractWires merges only connected chains (2026-10-29)

`opBoolean` UNION on wire bodies: BOOLEAN_INPUTS_NOT_SOLID. `opExtractWires` joins edges that meet END TO END into one wire
body; edges that overlap or touch mid-edge (station lines ending on an outline) give EXTRACT_WIRES_OVERLAPPING_EDGES, and
disjoint edges come back as one body each (probed: 3 disjoint lines -> 3 bodies). One parts-list entry for many curves is a
CLOSED composite (`opCreateCompositePart`, `closed : true`); an open composite lists its members.

## Correction 81: a group's driving parameter is declared immediately BEFORE the group (2026-10-29)

`annotation { "Group Name" : "X", "Driving Parameter" : "flag" }` fails with "Parameter group must be immediately preceded
by its driving parameter" when the boolean is declared inside the group. Declare it right before the group annotation (hidden,
`UIHint.ALWAYS_HIDDEN`); the group header then shows the checkbox and the contents apply only when it is on.

## Correction 82: the sync matches a tab by NAME; a different file name creates a second element (2026-10-28)

`pushproject <project> --files export_tag.fs` looked for a tab named `export_tag`, found none next to the user's empty
`Export_Tag`, and created a new Feature Studio. Name the local file exactly like the tab, or move the new tab by hand
(the user moved it; the old empty one was deleted). `--check` needs the sync tool's own browser login; the Playwright
notices pane (click the notices icon in the studio toolbar) reads the same compile notices without it.

## Correction 83: read a feature's message and body facts with one eval (2026-10-29)

The features GET carries only the status. In an eval, `getFeatureError(context, makeId(featureId))`, `getFeatureWarning`
and `getFeatureInfo` return the text (undefined when none). Eval-script traps: there is no `id` (use `makeId("name")`),
`match(s, regex)` returns a map (test `.hasMatch`), `indexOf(s, sub)` is the string search, strings have no `.length`, and
`rollbackBarIndex` is a QUERY parameter (correction 67). Scratch scripts: devtools/onshape/check_export_tag_tests.py.

## Correction 84: calling a custom feature from another feature (2026-10-28)

`stationGeometry(context, id + "stations", { ... })` works from a feature body when (a) the caller `export import`s the
callee's tab (an enum used as a parameter type must be exported: "Enum used as parameter type must be exported"),
(b) the definition map carries every parameter the callee reads, (c) FeatureList values are passed through as given.
Pin the import by microversion and re-pin after every change to the callee. A callee that reads ABSOLUTE positions (a Station
definition's stations) must be told to move them when it is run on a shifted copy: Station geometry has a hidden
"Station frame" mate connector (the image of the world frame under the shift) for that.

## Correction 85: a micro-edge (microns) must never be its own run; read the real wires, not the story (2026-11-02, RD 25ONE 28, Cap_Wall_Surface)

**Symptom**: Driven offset surface refused patches (`The loft refused 1 of 6 patches ... run:profiles 5:[ 0 , 1 ]`, earlier `Every patch was refused`). Notices: fit warnings `reached the cap of 20 control points 0.43 mm`, `run 6 spans 0.000000 mm and was not emitted`, `CANNOT_MAKE_BSPLINESURFACE`, then `LOFT_INVALID`.

**Cause chain (measured by read-only evals at rollbackBarIndex 149/150/159)**: Clean wire 1's output already held a 0.0034 mm edge at x=1624.72 between a 6.33 mm edge and a 201 mm tail, tangents agreeing to 0.35 deg (not a geometric corner). One offset (ltc) kept it as a 3.4 um run; the other (lbc) collapsed it to 0 span, `emitRuns` / `sectionPlan` dropped the run, and the offset wire opened a 7.3 mm gap (two wire bodies). `loftColumns` then carried ltc's overhang (6.31 mm + 3.4 um + 199.5 mm tail, 205.8 mm) into the end patch against lbc's 5.8 mm piece (35:1, rulings nearly parallel) and the kernel refused it.

**Why the Clean wire kept it (CONFIRMED 2026-11-02 by a [micro] print in a derived test studio)**: `describeChain` calls `constructPath(..., { tolerance : CLEAN_CHAIN_TOLERANCE })`. A 3.4 um edge has both ends within that tolerance of its joint, so the path came back with it traversed AGAINST the chain: its endTangent pointed backwards (-0.9995, 0.0012, -0.0325 while the chain ran +x). Both joints around it then read as ~180 degree corners, the sliver rule said "sliver between corners, kept", and a run of its own was emitted. A first fix that merged the sliver into its neighbour (buildRuns) still failed: the run's END tangent was that reversed one -> approximateSpline / opCreateBSplineCurve `BAD_GEOMETRY`.

**Fix**: clean_wire.fs `describeChain` -- after the path is described, an edge shorter than CLEAN_MICRO_EDGE (0.01 mm) is oriented by which of its ends meets the neighbouring edge and takes its tangents from the neighbours (a direction measured over microns carries nothing). The existing sliver rule then absorbs it ("a 0.0034 mm fragment ... was absorbed: the edges either side of it line up"). Belt and braces: `buildRuns` joins a micro run to the previous run when the edges either side line up, and `runSamples` takes no samples from a micro edge inside a longer run (a micro end moves the run's end point instead). Result in the test studio: the Clean wire output went from 8 edges to 6, no edge under 0.01 mm anywhere. Diagnostics only in driven_edge_offset.fs (`emitRuns` warns `The offset wire has N open joint(s)` with the location) and driven_offset_surface.fs (the refusal warning now names each member's edge count and length).

**Not done (decide after the Clean wire fix is seen)**: bridging a real wire gap, guarding overhang absorption by length ratio, using every stretch instead of `longestSpan`. Agent reports: scratchpad agent_A..D (diagnosis, fit, fallback ladder, preflight).

**Rule**: a wire with a micro-edge is an input defect Clean wire must remove; downstream features should report open joints, not fail silently. Verify with the actual wires (eval at a rollback index) before naming a corner or a turn: two claims in this session ("90 degree turn", "caps are a limit of this feature") came from reading notes, not geometry.

## Correction 86: a "created by <feature>" query variable returns the feature's sketch lines too; opExtractWires then refuses it (2026-11-06, RD 25ONE 28, Export primitive)

**Symptom**: Export primitive 1 ERROR `EXTRACT_WIRES_OVERLAPPING_EDGES`, stack at primitiveBaseline `opExtractWires(... inputEdges)`. The Baseline wires pick was the query variable `target_baseline` (selection "created by" Generate_Target_Baseline, wire bodies): 8 bodies, not 1.

**Cause**: generateBaseline with "Add baseline sketch" on draws a measurement sketch of 7 CONSTRUCTION lines (minChord, inflChord, fbTangentLeg, fbNormalLeg, abTangentLeg, abNormalLeg, camberNormal); a sketch's curves are wire bodies created by the feature. inflChord joins FRCP and ARCP and lies on the baseline's camber edge, so the extract refuses the set. `qConstructionFilter(.., ConstructionObject.NO)` keeps ALL 8 (sketch construction lines are not ConstructionObject.YES); `qSketchFilter(.., SketchObject.NO)` keeps only the baseline (measured: sketch YES 7 of 8).

**Fix**: export_primitive.fs `modelWireEdges` (baseline and footprint picks): when a pick mixes model wires with sketch edges, keep the model wires (a pick of sketch edges alone is unchanged) and note "N sketch edge(s) ... were ignored". primitive_baseline.fs wraps the extract in `try silent` and throws a regenError that says the baseline wires overlap or are not one wire, naming the edge count. `try silent { ... }` takes NO catch clause: set a flag after the call and test it.

**Rule**: a query that selects "everything a feature created" is not a wire; filter sketch objects (qSketchFilter) before building a chain, and replace a kernel error from user input with one that names the input. Also seen in this session: the Edit/Write tools turn a `ö` escape into a real umlaut (fscheck then reports 2 non-ASCII bytes): fix at byte level, never with PowerShell Replace on a console-decoded string.


## Correction 87: a WARNING status shows as an ERROR in the feature tree; report a successful fallback as INFO (2026-11-07, Offset+ AUTO)

**Symptom**: Offset+ built the right geometry through its Thicken fallback and reported it with `reportFeatureWarning`; the Reference Side geo-test studio showed the feature red (tree item class `ns-list-item-error`, dialog `has-regen-error`, red icon on the Features header) and the user read it as a failure. The stored REST status was WARNING (not ERROR) and the notification bubble was `alert-warning`: Onshape styles WARNING-status custom features like errors in the tree.

**Test**: the same message through `reportFeatureInfo` -> no error class, normal icon, same geometry.

**Fix**: offset_plus.fs reports "The direct offset failed on surface N; it was built by thickening ..." with `reportFeatureInfo`. A failed op inside `try silent` does NOT leak an error status (checked: INFO leaves the feature clean).

**Rule**: use WARNING only for a result the user must fix or distrust; a handled fallback that produced the intended result is INFO. Do not trust an eval's `getFeatureWarning` for how the tree will look: open the studio (class names via Playwright) or compare the stored status.

## Correction 88: no unconditional println; fit problems reach the user as a feature warning, not a print (2026-11-08, repo-wide println audit)

**Symptom**: about 80 files called println; roughly 100 calls ran with every option at its default (progress lines, fallback notes, per-run diagnostics), burying real warnings and errors in the notices pane. The user's rule: a feature must NEVER write to the FeatureScript notices unless a Debug / print option in its dialog is on.

**Fix**: unconditional prints were deleted, or gated behind the feature's existing debug flag. Messages the user should see were NOT left as prints:
- curve_core.fs pproximateFamily collects one message per fit run (interpolating spline instead of the requested tolerance; cap reached off tolerance) and reports ONE 
eportFeatureWarning through an optional pproximation.warnId (feature id). emitSplineCurve (so also emitRunShape) supplies its own id; direct callers add "warnId" : id (clean_wire.fs emitFittedRun, driven_offset_surface.fs coupledFits). Without warnId the fit stays silent, so older pinned callers are unaffected. Editing-logic dry fits must NOT set it.
- evaluate_profiles.fs: "top and bottom profiles do not overlap, no middle profile" is a 
eportFeatureWarning (the middle profile was asked for and not built).

**Lint**: python fscheck.py now flags any println not lexically guarded by a condition naming debug / print / verbose / trace / dbg / diag / log (finding label [println]; --println-only, --no-println; skips tests, spikes, std, archive, example_1). A reasoned exception is // fscheck: allow-println <why> on the line before. A helper whose NAME contains a guard word is only reported when a caller is ungated. Name a debug gate function after what it does (debugPrintsRun, not reportsRun) so the lint and the reader both see it.

**Rule**: println only behind a user-visible Debug / print checkbox (default off), or an explicit "Print ..." option the user asked for. Everything else is reportFeatureInfo / reportFeatureWarning / regenError, or nothing (see correction 87 for which). Deleting prints from a library imported by VERSION needs a new version and a re-pin.


## Correction 89: a union of sheets that do not touch raises no error; ask the geometry whether it sewed (2026-11-09, Deform on a 193-face ski sheet)

**Symptom**: Deform reported "0 still separate" while a patch sat unsewn beside the main sheet, and a later "not sewn" check reported a patch that WAS sewn. Before that, a join loop joined 2 of 192 faces: every later join failed with BOOLEAN_BAD_INPUT.

**Facts**: opBoolean UNION of sheets whose edges do not meet returns without an error and leaves both bodies. The union's result is the FIRST tool's body, modified: qCreatedBy(joinId, BODY) is EMPTY, and the consumed tool's own query resolves to nothing, so a loop that points "the main body" at qCreatedBy(joinId) hands the next join one tool. Keep the main body as qUnion([createdBy, mainQuery, toolQuery]). Body queries after a union cannot say whether it sewed; the FACES survive (qCreatedBy(id, FACE)), so count its edges that have fewer than two adjacent faces (naked): none = sewn. A patch that is only merged with a neighbour is not sewn.

**Also**: after a failed patch, deleting the patch's BODY removes whatever the failed union merged it with, and deleting its FACES (opDeleteFace) removed the old strip and a sliver too: the result lost geometry. Delete by body only when the union never ran, and put a lifted face back from a copy (opExtractSurface) before deleting it; when it cannot be put back there is a hole and a stray face. Explicit TopologyMatches (COINCIDENT, nearest naked edge of each side, as the standard Fill ADD does) are needed when an edge is refit, but edges more than about 10 microns apart are not sewn by any match: the geometry has to move (rebuild the face from its neighbour's edges).

**Rule**: verify the RESULT (naked edges, body count, face count at the rollback bar after the feature), never the feature's own bookkeeping.

## Correction 90: eval-API traps found while verifying Deform (2026-11-09)

- An eval without `rollbackBarIndex` runs the END of the tree, so it includes features the user added after yours (Fill, Boolean, Thicken) and any face-sharing edge count; pass query_params {"rollbackBarIndex": n} with n = the index just after the feature (the lid was 11).
- A heavy eval (regen plus opThicken) exceeds the client's 30 s read timeout: set FS_SYNC_TIMEOUT=600 and run it again.
- `qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE)` returns the faces of EVERY body at that edge, so an edge looks interior when another body shares it.
- `range(a, b)` includes b (`range(0, n)` has n + 1 entries): it crashed every deform; use range(0, n - 1).
- `box`, `type` are reserved words (fscheck does not catch `box`).
- A local lambda cannot be called by fscheck (reports "undefined"): write a top-level function. `sort(array, function(a, b) {...})` works with a number comparator.
- A feature's real message is the tooltip on its tree row (hover `.os-list-item-name`); REST says WARNING for what the UI draws red (correction 87).

## Correction 91: the per-feature step limit and the first-come point budget (2026-11-09, Deform lid)

**Symptom**: "Too many steps: possible infinite loop" (the limit is steps per FEATURE, cumulative; it is not catchable, later code fails too). It hit when (a) a debug line was printed for each of 193 faces, (b) a 30% refinement reserve was added before the core was cheaper.

**Measured**: the 458 edges of the lid map only about 8,600 points; `inverse` and `approximateSpline` are native; the per-point cost is the projection onto the from-path (Newton on the hinted edge AND both neighbours = 3 solves). Skipping the neighbours when the foot is strictly inside the hinted edge, a 10 nm Newton tolerance, and an incrementalJoin that is linear (links indexed by face, one main-body query) cut the lid from 192 s to 172 s and made 5k more mapped points affordable. There is no step counter: find costs by experiment, keep any per-face debug output to a handful of faces.

**Deform budget**: faces were built in index order from one pool, each with an allowance of twice its share. A face that misses its accuracy target stopped after ONE refinement pass whenever the next pass exceeded its allowance (the lid: a 38.8 mm2 face 22 um off, the strip 93.5 um, two 644 mm faces 11-17 um). Those boundary errors ARE the gaps between neighbouring faces (a 25 um wedge between two faces that share one exact edge in the source). A reserve that only faces missing by more than 8 um may use goes to the worst offenders; building the long faces first stops late faces being starved. Both are switches in deform.fs (MAPPED_REFINE_RESERVE, MAPPED_LONG_FACES_FIRST), OFF by default because with them the strip maps directly and its 137 mm edges miss their neighbours (4 seams left instead of 1).

**Rule**: when something refuses a fix for accuracy, find out which face carries the error and why before adding a repair pass; and test every budget change on the case that already works.

## Correction 92: a fill creases against its neighbours; a creased sheet will not offset or thicken (2026-11-09, Deform lid, Thicken 1 2 mm)

**Symptom**: Thicken 1 (2 mm, "Opposite direction" = thickness2) failed with "Failed to thicken all selections" on the deformed lid while the source sheet thickens at every thickness up to 2 mm on that side.

**Measured**: neither the selection nor the faces individually (the same 10 faces refuse 2 mm alone in the source, whose whole sheet still passes). The angle between neighbouring faces across every edge: source 404 edges, 1 over 1 deg, none over 5; lid 400 edges, 44 over 1 deg, 11 over 5, worst 17 deg, at the faces rebuilt by opFillSurface (31 faces fall back to a fill; the patches). A fill matches the boundary position only (G0), so it meets its neighbours at a kink; offsetting a creased sheet by 2 mm opens gaps. Whole lid at 0.2 and 0.5 mm passes, 0.05 and 2 mm fail.

**Fix direction**: patches and re-filled faces with `edgesG1` = the hole's naked edges (adjacent to the neighbours' faces), plain G0 only as the fallback; then re-measure the crease angle across every two-face edge (an eval using evFaceTangentPlane on both faces at the edge midpoint) and re-run the thickness sweep. Not done yet; patchLeftSheets already takes a `tangent` flag.

## Correction 93: a tagging feature (Deform tag): arrays of tags, explicit connector ownership, 0 = default (2026-11-11, curveMapping_public/deformTag.fs)

**Rules**: (1) `setAttribute` replaces an attribute of the same NAME on an entity, so two tag features picking one entity erase each other. Write ONE attribute name whose value is an ARRAY of tag maps, read it with `getAttribute`, keep the other entries and replace only the entry with the same (group, role). (2) FeatureScript cannot read which body owns a mate connector (`qOwnerBody` returns the connector) and cannot name one: make the user list connectors under their body (an array parameter, item = one body + a connector query; an array item cannot hold another array) and pass the connector's id (`transientQueriesToStrings`, equals the REST id: inferred) so the API reads the name. (3) A numeric option whose 0 means "the app's default" must be LEFT OUT of the tag, or the reader takes 0 as a zero tolerance. (4) Match the reader's defaults (the engine's prevailing default is +x, a dialog default of "none" turned orientation off). (5) Put the schema string in the value, not in the attribute name (name rules are unverified). (6) No live read-back was possible: tell users to place tag features at the BOTTOM of the feature tree (docs/briefs/deform_tag_contract.md).

**Also**: std has no trim function (`replace(s, "^\\s+|\\s+$", "")`); a mate connector attribute is wrapped in `try silent` because the refusal is unverified (reported, not fatal).

## Correction 94: mirror the sheet, not the solid (2026-11-12, Referenced part top sheet, reference_side/referenced_part.fs)

**Symptom**: Referenced part, Thickened, mirror plane = Front: "Boolean operation failed to return a valid part" (BOOLEAN_INVALID at the final `opBoolean` union of the half and its mirror, line of `mirrorAndUnite`). Not mirrored it worked; the user's manual Thicken + Mirror(solid) + Boolean failed the same way, while Mirror (Add) of the SURFACE worked.

**Measured** (Print diagnostics, notices pane stack trace): the half 51414 mm^3; the cut at the plane made 2 extra slivers of 3e-5 and 3e-6 mm^3 (13 of 462 profile edges cross the plane by a fraction of a micron), and the side test by CENTROID keeps one sliver (a sliver has no meaningful centroid): filter by volume first. That alone did not help: with the slivers gone, with no cut at all (box 0.14 um past the plane = kernel padding), and with the half as target / mirror as tool, the union of the two solids still failed. The plane faces carry 22 / 28 / 10 / 16 edge outlines on a 0.4 mm strip.

**Fix**: build the full-width SHEET (Offset+ distance-0 copy of the ORIGINAL profile, `opPattern` mirror, sheet `opBoolean` UNION), thicken once (`thickenLayer` on it), cut once. One-sided cap / periphery SURFACES are mirrored and joined the same way (a plane cap must not tilt across the mirror plane, else the old route). Sheet union raises no error when nothing touched (correction 89): check `qEdgeTopologyFilter(.., EdgeTopology.ONE_SIDED)` edges lying in the plane (3 samples within 1 um) is 0. Result 102828 mm^3 = 2 x the half, box -72.5..72.5 mm.

**Diagnosing**: a feature-level `Print diagnostics` boolean (every println gated; helpers carry `fscheck: allow-println`) prints step markers, edges against the plane, piece volumes; the failing op and line come from the Part Studio notices pane stack (`@opBoolean: BOOLEAN_INVALID` + function frames), which the feature-tree tooltip hides. Playwright: `.notice-pane-toggle-button` opens the pane; `browser_find` with a regex and `filename` pulls the console text.

## Correction 95: Thicken+ draft, side offset and delete caps (2026-11-23, reference_side/thicken_plus.fs)

**Symptom**: every new drafted feature showed only `REGEN_ERROR` (getFeatureError returns the category, never text, for a kernel/runtime failure) while an eval-API reproduction of the same code passed. The notices pane (`python -m sync.main notices <project> --monitor "<studio>"`, needs `python -m sync.main login` run in the FOREGROUND and finished) showed `Variable ExtendEndType not found`.

**Rules**: (1) `import("onshape/std/common.fs")` does not include `extendendtype.gen.fs` / `extendsheetshapetype.gen.fs`; import them like driven_offset/unwrap.fs does. The eval API has all of std in scope, so it cannot find a missing import: after an eval pass, run `pushproject --check` before a live run. (2) opDraft cannot draft a face along its varying normal (one pull vector); the draft is built: copy the faces (opExtractSurface), grow the copy (opExtendSheetBody on the LAMINAR edges, `ExtendEndType.EXTEND_BLIND`, LINEAR) by a margin above the walls' shift, opThicken it into a slab, build one ruled wall per outline edge (opFitSpline at overextended heights through points `p + h*n + (offset + (h-hN)*tan(angle))*c`, opLoft SURFACE; n toward the reference, c the outward conormal), split the slab by each chain's cutter and keep the piece holding a point inside the slab (qContainsPoint). (3) A wall must reach PAST the slab outline to split it (extension reach > slab margin, about 8x), with extension points projected onto a second, bigger grown copy so they follow the surface; else SPLIT_FAILED. (4) Several sheets used as one cutter are NOT one tool: join them first (opBoolean UNION; the union keeps one sheet's identity, so follow them with startTracking), also the two halves of a kinked wall. (5) Edges meeting within 15 degrees (a fold, a mild bend) are one chain: shared mitre end curve (`n = nm / dot(nm, n)`), no extension there. (6) Concave corners are NOT supported (the sequential keep-the-seed-piece split would cut into the face): the extension point lands on the face, reported by a clear error; closed-loop mitred cutters would be the way to lift this. (7) startTracking of a cap face also returns the faces the walls cut (overlapping toward/away/side queries): read the caps from geometry (signed distance from the grown seed along the toward normal equals +toward or -away at three sample points). (8) A lazy `qBodyType(qEverything(...), SHEET)` seed query picks up every later sheet: freeze it with qUnion(evaluateQuery(...)). (9) Op fields differ from feature fields (opExtrude: entities, endDepth, ToolBodyType). (10) `try silent(op())` of a void op returns undefined even on success. (11) Side face offset is the same walls with a constant added to the shift (also without an angle, margin grows with it); selected edges only decide which walls tilt; one-sided edges are counted from the model topology (faces of the set on the edge == 1).

**Tests**: reference_side build/check pairs TD1-3 (delete caps), DR1-13 (draft neutrals, kink, edge selection both ways, offset, closed circle, concave error, caps deleted, folded face set, Add boolean); volumes checked against an integral of the wall shift. Reproduce a case without the browser: scratchpad repro_main.py approach (the real function bodies turned into eval lambdas). Spikes: docs/research/thicken_plus_draft_spike*.fs.txt.

## Correction 96: Patch+ (push or pull the region inside a footprint), reference_side/patch_plus.fs (2026-11-23)

**Built**: footprint curves imprinted on a COPY of the faces with opSplitFace edgeTools (native projection, `ProjectionType.NORMAL_TO_TARGET` or `DIRECTION`; import `onshape/std/projectiontype.gen.fs`); the region is the copy face whose edges are all imprint edges; one ruled wall per outline edge (opFitSpline pairs at -reach / +reach along `u cos(theta) + c sin(theta)`, opLoft SURFACE), all joined by opBoolean UNION into one tube; the replacement is `opExtractSurface(faces, offset)` with `useFacesAroundToTrimOffset false`. Tube cut by the base and the offset surface (opSplitPart, keepTools true), keep the piece nearest a probe halfway to the offset surface; offset surface and base cut by a COPY of the full tube (the trimmed wall's edge lies on the offset surface: degenerate as a cutter). Sheet: UNION [outside base piece FIRST (keeps its identity and name), wall, offset piece] -> 1 sheet. Solid: UNION [base patch, wall, offset piece] -> closed shell (check naked edges 0) -> opEnclose -> plug; offset > 0: UNION [base, plug]; offset < 0: SUBTRACTION with the plug as the ONLY tool (base in the tools too = BOOLEAN_SAME_INPUT).

**Rules**: (1) A corner between two outline edges is mitred: `cm = (ci + cj) / (1 + ci.cj)`; both walls share the mitre ruling, so they meet exactly at convex AND concave corners (a closed tube has no inside/outside ambiguity, unlike the one-wall-at-a-time split of Thicken+). Store only the CORRECTION `cm - ci` (it runs along the edge's own wall plane) and blend it linearly along the edge: moving just the last sample doubles the sampled curve back at full reach (35 mm times a 10 degree lean is 2.5 mm against 1.9 mm spacing) and the tube-join union fails with BOOLEAN_INVALID. (2) Without a draft, adjacent normal walls share their vertex ruling exactly: no mitre needed. (3) Wall reach: 3 x |offset| + 5 mm past the base on both sides. (4) Errors: wrap the build in try/catch with a `stage` variable; clear `stage` before your own `throw regenError` and rethrow unchanged, so only kernel failures get "Patch+ failed while <stage>". (5) Leftover helper bodies: delete `qSubtraction(qCreatedBy(id, BODY), result)` at the end and check the count in the test. (6) Native alternatives measured: opOffsetFace / opMoveFace build their own wall on imprinted solids and sheets but cannot draft; opReplaceFace only swaps geometry (fails with nothing to extend to).

**Tests**: PP1-PP7 in the reference_side build/check pair (circle boss, pocket, sheet, +10 draft, concave L straight and drafted, walls along a flipped direction): volumes against closed forms (offset-polygon area `A0 + P d + (convex - concave) d^2`). Not covered: arbitrary replacement surface, several footprint loops, footprint crossing the faces' boundary, non-flat bases (spikes: sphere with normal walls and draft, docs/research/patch_plus_spike*.fs.txt), very short edges with a large draft.

**Patch+ additions (2026-11-23, from the user's real ski test studio)**: (7) The region inside a footprint is NOT one face on a real surface (a 196-face sheet: the projected curve crossed 15 faces): find it by FLOODING across face edges from the two sides of an imprint edge, stopping at the imprint edges (`qCreatedBy(id + "imprint", EDGE)`), both sides in lockstep, visited set keyed by `transientQueriesToStrings(face)`; the side that closes without reaching a free edge (an edge with no face across it) is the inside. The outline is then `qAdjacent(region, EDGE) ∩ imprint edges`, normals come from the nearest region face (`evDistance(...).sides[1].index` into `evaluateQuery(region)`). (8) The kernel offset of a patchwork of small freeform faces (`opExtractSurface ... offset`) fails with DIRECT_EDIT_OFFSET_FACE_FAILED, and a normal offset of a CREASED surface overlaps/gaps at the creases (the following cut of the offset surface fails with SPLIT_FAILED): offset only the faces within the walls' reach of the region, and give walls along the projection direction a MOVE (`opTransform`, offset measured along the direction) instead of an offset: exact and crease-proof. (9) Replacement surface (user-picked face/sheet, either side of the faces, must not cross them in the footprint): rays from the region along the normal (or the projection direction) both ways find the side and the distance (`evRaycast`: entity, parameter, intersection, distance); wall `us` oriented toward it; rays from every 4th outline sample must all hit (else a marked error); wall reach `1.5 * (farthest hit + |offset|) + 5 mm`; offset on a replacement is signed "further from the faces" (flip by the replacement's own normal vs the hit direction); boss or pocket for a solid = which side (`side > 0` -> UNION, else SUBTRACTION); wall probe at `(first hit distance + offset) / 2`. (10) For a solid, cut the tube with a copy of the PICKED faces only, never all the solid's faces (SPLIT_FAILED on a 169-face core). (11) Reproduce a failing feature of the user's own studio without their browser: read its stored parameters (`GET .../features`), swap `id + "` for a root id and `,id)` (qCompressed's third argument) for it, inline the real feature body in an eval (scratchpad repro_patch.py pattern); `getFeatureError` gives only the category, the message comes from the notices pane or a try/catch with a `stage` variable.

**Patch+ crossing (2026-11-23, user's Patch+ 2: replacement offset -2 mm dipped 0.14 mm below the faces at one outline sample)**: (12) When the new surface crosses the faces along the outline, the wall between them pinches to zero and the tube is cut into SEVERAL middle pieces (4 pieces instead of 3): keeping only the piece nearest a probe leaves most of the wall out (kept area 11.8 mm2 of about 185) and the seam is naked (naked edges 54 -> 80). Keep EVERY tube piece that does not touch the tube's two free rims (freeze the rim edges before the cuts, `startTracking` them, test `qOwnedByBody(piece, EDGE) ∩ rim`): naked edges 54 -> 54. The rays that measure the wall reach must go BOTH ways along the wall (the surface is behind the faces for part of the outline); do not reject a replacement for crossing. (13) For a solid, the base patch and the replacement then intersect each other inside the outline: a union of crossing sheets is BOOLEAN_INVALID. Cut each by a COPY of the other first (`evCollision` non-empty decides; `try silent` around the two opSplitPart), then close ONE shell PER LENS: a wall part belongs with the pieces its free edges' midpoints lie on (distance < 1e-5 m), walls sharing a piece are one lens, each lens is unioned, checked for 0 naked edges, enclosed on its own (lenses touch along the crossing curve: four sheets at one edge cannot be joined as one body). Each enclosed lens is added (outside the region faces' normal) or subtracted (inside): classify against the imprinted COPY's region faces, never `baseSurface` (it has been split by then: CANNOT_RESOLVE_ENTITIES). RP7 (sheet) and RP8 (solid) are the tests: a plate tilted 10 degrees through the face over a circle: sheet area 10000 - pi r^2 + pi r^2 / cos 10 + 4 r^2 tan 10, solid volume unchanged (lens added = lens subtracted). (14) Replaying a feature that already ran needs the eval rolled back to before it (`rollbackBarIndex` = the feature's index, as a signed query parameter): at the end of the tree the footprint already coincides with the new edges and the region search fails.

**Result-face stability (2026-11-23, user's Thicken+ 1 selected faces and edges on Patch+ 1's result and went to "Select the edges to draft" after a Patch+ edit)**: (15) A user's pick on a face a feature CREATES is a `qCompressed` query keyed to the operation that created the face and the order of the operations: renaming an op id (`shell` -> `shell0`, `enclose` -> `enclose0`, `apply` -> `applySub`) or changing the tool order of a union (a union keeps the identity of its FIRST tool: wall-first instead of patch-first) silently breaks every downstream pick (they resolve to nothing, so the next feature fails with an unrelated message such as "select the edges"). When a feature grows (extra cases, more pieces), keep the ORIGINAL op ids and tool order for the ordinary case and give only the NEW cases new ids (lens 0 keeps `shell` / `enclose` / `apply`, later lenses `shell1`...). Test it: evaluate the dependent selections with `rollbackBarIndex` (scratchpad where_thicken.py): they must resolve to the same counts before and after the edit. The regression tests of the feature itself (volumes, areas) do NOT see this; a test that picks a face on a Patch+ result and thickens it would.

**Patch+ offset direction (2026-11-23)**: (16) The direction of a POSITIVE offset must be a stated rule, not an accident of face orientation (a sheet's normal is whichever way it was built): with a Side reference the offset goes TOWARD it, the side taken from the reference's nearest point to the region (mate connector: its origin; vertex / face / edge / body: `evDistance(pIn, reference).sides[1].point`; an error with a marked point when `|dot(refPoint - pIn, normal)| < 1e-5 m`: in the plane it says nothing); without a reference it is along the face normal (outward on a solid) and a Flip reverses it, with a viewport arrow (`addManipulators(context, id, { "offsetFlip" : flipManipulator({ "base", "direction", "flipped" }) })` + `"Manipulator Change Function" : "<name>"` in the feature annotation + an exported change function writing `newManipulators["offsetFlip"].flipped` into the definition); with a replacement surface the side comes from rays and the offset means further / closer, both ignored. Signed offsets in the no-replacement branches become `abs(offset) * side`. Tests RF1-RF4 (reference above / below a sheet, Flip on a solid, reference in the plane = error). (17) Pick-stability fingerprint (PP2 check): `qCreatedBy(makeId(featureId) + opName, FACE)` counts per internal op of a pocket (`makeId` takes ONE string); the only op whose faces survive is `apply` (=2); a renamed op id shows as 0. It does not see a changed union tool order.


## 97. Patch+ footprint FACES, islands/holes, footprint off the picked faces (2026-11-23)
- **Input**: `footprintFaces` (faces / sheet body) next to `footprint` (curves); pick one or the other. The outline of faces = edges with exactly ONE picked face (`outlineOfFaces`), all loops (islands, hole loops).
- **Region by rays, not flood**: for each face of the imprinted copy, a point well inside it (`interiorPoint`: centroid's nearest point, else best of a 5x5 grid by margin to the face edges: a ring's centroid lies in its hole) + `evRaycast` along the projection direction / face normal, both senses, against the footprint faces. The flood from one imprint edge cannot tell holes/islands apart; keep it for the curves input (exact old ops, fingerprint unchanged).
- **Several loops = several tube bodies**: one split per tube (`offsetByTube`, `baseByTube`, `surfByTube`; the first keeps the legacy id, later ones get the index). Pieces are kept by classification (distance to region for base pieces, rays back to region for replacement pieces), not by `nearest`.
- **Trap**: a feature created through the REST API has no parameter for a Query the dialog would always supply: add every query parameter to the defaults map (`"footprint" : qNothing()`), or the precondition `definition.x is Query` fails with only an info notice.
- **Footprint running off the picked faces**: the tube engine cannot cut it (a strip ending partway through a face does not separate it). Straight path instead (`offEdge`): region moved rigidly by `uIn * |offset|`. Solid: `opExtrude(region faces)` then union / subtract. Sheet: imprint the footprint on the SHEET ITSELF (stays the result body), `opDeleteFace` the region faces, extrude the outline edges into walls (`opExtrude` surface), knit [sheet pieces, walls, moved copy]. Deleting the region can split the sheet into several bodies: follow all with `tracked(owner)`. Limits (clear errors): no draft, no replacement, planar region or Walls along projection, all faces of the sheet picked, curves input unsupported.
- **Test trap**: a second Patch+ on the same cube picks a face the first one already changed (point query finds nothing: "Pick faces of ONE ... body"). One feature per fixture.