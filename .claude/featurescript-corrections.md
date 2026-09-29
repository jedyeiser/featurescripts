# FeatureScript Corrections Log

This document tracks corrections needed to LLM-generated FeatureScript code. It serves as a living knowledge base to improve future code generation by learning from past mistakes.

**Purpose**: Reference this log before generating FeatureScript code to avoid known issues and follow corrected patterns.

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

## Boolean Operation Type Enum — Wrong Name
**Date**: 2026-03-02
**Issue**: Used `BooleanType.UNION` — this enum does not exist.
**Fix**: Use `BooleanOperationType.UNION` (and `.SUBTRACT`, `.INTERSECT`).
```featurescript
// WRONG
opBoolean(context, id, { "tools": q, "operationType": BooleanType.UNION });
// CORRECT
opBoolean(context, id, { "tools": q, "operationType": BooleanOperationType.UNION });
```

## opReplaceFace — oppositeSense Must Be Auto-Detected
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

## BSpline Splitting — Control Point Index Bug in `splitCurve`
**Date**: 2026-02-26
**File**: `tools/curve_operations.fs` — `splitCurve()`
**Issue**: After `refineKnotVector` to multiplicity `degree+1` at `splitParam`, the CP extraction bounds for both halves were off-by-N, causing malformed BSplineCurves that pass knot count validation silently but fail `evaluateSpline` when used as input to a second `splitCurve` call.

**Error triggered**:
```
@evaluateSpline: 18 (# control points + degree + 1) knots required, but 21 found.
```
Only appears on the 2nd+ call to `splitCurveMultiple` (first split's `curveB` is invalid; the error surfaces when that invalid curve is passed to the next `splitCurve`).

**Root cause** (cubic, `splitStart=4`, `splitEnd=7`, 8 CPs after refinement):
- CPs A: `i <= splitStart` → took 5 CPs instead of 4 (included duplicate junction CP)
- CPs B: started at `splitEnd` → took 1 CP instead of 4 (skipped junction and intermediate CPs)
- Knots A and B: were already correct

**Fix**:
- CPs A / weights A: change `i <= splitStart` → `i < splitStart`
- CPs B / weights B: change starting index `splitEnd` → `splitEnd - degree`

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

    var knots = curve.knots;      // ❌ ILLEGAL - standalone var
    var p = curve.degree;          // ❌ ILLEGAL - standalone var
    var n = size(knots);           // ❌ ILLEGAL - standalone var

    for (var i = 1; i <= p; i += 1)  // ✓ OK - var in loop declaration
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

    var knots = curve.knots;      // ✓ OK in function
    var p = curve.degree;          // ✓ OK in function
    var n = size(knots);           // ✓ OK in function

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

### `evaluateSpline` — Return Type is Nested Array, Not Array of Vectors
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

// ❌ ERROR - Ambiguous unquoted keys
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

// ❌ WRONG - Parentheses are for units in bounds, not disambiguation
return {
    (plane): plane          // Syntax error - wrong use of parentheses
};
```
**Correct Pattern**:
```featurescript
var plane = measurePlane;
var coreWidth = 10 * millimeter;
var x = 5 * millimeter;

// ✅ CORRECT - Use quoted strings for conflicting keys
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

// ❌ ERROR - Unquoted key in operation parameter
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

// ✅ CORRECT - Always quote keys in operation parameters
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
    // ❌ ERROR - Evaluates as (id + i) ~ "plane"
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
    // ✅ CORRECT - Parentheses force string concatenation first
    opPlane(context, id + (i ~ "plane"), {...});

    // This correctly:
    // 1. Concatenate number with string: i ~ "plane" → "0plane"
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

// ❌ ERROR - Comparing ValueWithUnits to number
if (abs(deltaZ) < TOLERANCE.zeroLength)  // TOLERANCE.zeroLength is raw number
{
    // ...
}
```
**Correct Pattern**:
```featurescript
var deltaZ = widestHighest[2] - highestWidest[2];  // ValueWithUnits

// ✅ CORRECT - Extract numeric value first
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
    return t1;  // ❌ ALWAYS executes! Not part of if!

// Another example
for (var i = 0; i < n; i += 1)
    sum += values[i];
    count += 1;  // ❌ ALWAYS executes! Loop only includes first line!
```
**Correct Pattern**:
```featurescript
// Always use braces, even for single statements
if (abs(f1) <= tol)
{
    println("  CONVERGED at iteration " ~ it);
    return t1;  // ✓ Both statements execute conditionally
}

// With braces, both statements are in the loop
for (var i = 0; i < n; i += 1)
{
    sum += values[i];
    count += 1;  // ✓ Both statements in loop
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
- **Always annotate every parameter** — `context is Context`, `id is Id`, `face is Query`, numbers as `is number`, arrays as `is array`, feature definitions as `is map`
- The editing logic function signature is: `(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map`
- This is a systematic error that LLMs produce when writing helper functions. Treat unannotated parameters as a bug, not a style issue.
- Review ALL function signatures (not just exported ones) before finalizing any file.

**Files Fixed**: gordonSurface/pullSurface.fs — isPointLocked, faceTangentAtVBoundary, fitIsoCurve, pullSurfaceEditingLogic (2026-03-01)

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

### Q Matrix Storage is 3×3, Not 6×6
**Date**: 2026-02-10
**Issue**: Classical Laminate Theory Q matrix is stored as 3×3 array in xSection code, not the full 6×6 matrix
**Incorrect Pattern**:
```featurescript
// Attempting to access Q66 shear modulus
var G = body.Q[5][5];  // ❌ ERROR - Index out of bounds!

// Assuming full 6×6 matrix layout:
// [[Q11, Q12, Q13, Q14, Q15, Q16],
//  [Q21, Q22, Q23, Q24, Q25, Q26],
//  ...
//  [Q61, Q62, Q63, Q64, Q65, Q66]]
```
**Correct Pattern**:
```featurescript
// Q is stored as 3×3 reduced stiffness matrix
// [[Q11, Q12, Q16],
//  [Q12, Q22, Q26],
//  [Q16, Q26, Q66]]

var G = body.Q[2][2];  // ✓ Q66 is at [2][2] in 3×3 matrix

// For isotropic materials: Q66 = E/(2*(1+ν))
// For orthotropic materials: Q66 = G12
```
**Reference**: xSectCLT.fs lines 77-87 (Q matrix documentation)

**Lesson Learned**:
- xSection code uses **3×3 reduced stiffness matrix**, not full 6×6
- Q66 (shear modulus) is accessed at `Q[2][2]`, NOT `Q[5][5]`
- Always check data structure documentation before accessing matrix elements
- The 3×3 format is standard for plane stress/CLT formulations
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

// ❌ WRONG - Guessed units without dimensional analysis
var K_unit = newton;                 // Wrong!
var f_unit = newton * meter;         // Wrong!

K_plain[i][j] = K[i][j] / K_unit;
f_plain[i] = f[i] / f_unit;
```
**Correct Pattern**:
```featurescript
// Perform dimensional analysis:
// K: G * A * (dNdy)² = (N/m²) * m² * (1/m)² = N/m²
// f: G * A * distance * dNdy = (N/m²) * m² * m * (1/m) = N

var K_unit = newton / (meter * meter);  // ✓ N/m²
var f_unit = newton;                     // ✓ N

K_plain[i][j] = K[i][j] / K_unit;
f_plain[i] = f[i] / f_unit;

// Document the dimensional analysis in comments!
```
**Lesson Learned**:
- **Never guess units** - always perform dimensional analysis
- Write out the calculation chain: `quantity = A * B * C`
- Track units through each step: `[N/m²] * [m²] * [1/m²] = [N/m²]`
- Add comments showing the dimensional analysis
- Common mistake: simplifying unit expressions mentally without checking
- Shape function gradients have units `[1/length]`
- For FEM: Stiffness ~ G*A*grad², Load ~ G*A*distance*grad

**Files Fixed**: xSect_GJ.fs lines 354-357

### Polar Moment of Inertia Formula for Triangles
**Date**: 2026-02-10
**Issue**: Polar moment calculation requires all six cross-product terms, not just three
**Incorrect Pattern**:
```featurescript
// ❌ INCOMPLETE - Missing y1*y3 and z1*z3 cross terms
var y_cross = y1*y2 + y2*y3 + y3*y1;  // Only 3 terms
var z_cross = z1*z2 + z2*z3 + z3*z1;  // Only 3 terms
var Jp = (A / 6.0) * (y_sq + z_sq + y_cross + z_cross);
```
**Correct Pattern**:
```featurescript
// ✓ CORRECT - All six cross terms for each axis
// Jp = Iy + Iz where:
//   Iy = ∫z² dA = (A/6) * (z1² + z2² + z3² + z1*z2 + z1*z3 + z2*z3)
//   Iz = ∫y² dA = (A/6) * (y1² + y2² + y3² + y1*y2 + y1*y3 + y2*y3)

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
- Second moment formulas: n² terms + n(n-1)/2 cross terms = n(n+1)/2 total

**Files Fixed**: xSect_GJ.fs lines 456-462

---

### No pow() Function — Use ^ Operator
**Date**: 2026-02-18
**Issue**: `pow(base, exponent)` does not exist in FeatureScript. Calling it with 2 arguments throws "function pow with 2 arguments not found".
**Incorrect Pattern**:
```featurescript
var magnitude = pow(10, exponent);  // ❌ ERROR - no pow() function
```
**Correct Pattern**:
```featurescript
var magnitude = 10 ^ exponent;  // ✅ Use ^ operator for exponentiation
```
**Lesson Learned**:
- FeatureScript uses `^` for exponentiation (not `**` or `pow()`)
- The std/math.fs header explicitly states: "There is no `pow` function: exponentiation is done using the `^` operator."
- Available math functions: `log(x)` (natural log), `log10(x)`, `exp(x)`, `abs(x)`, `floor(x)`, `ceil(x)`, `round(x)`, `sqrt(x)`

**Files Fixed**: ExportCurveCore.fs (roundToPrecision)

---

## Reserved Variable Names — Built-in Type Identifiers

**Date**: 2026-03-14
**Issue**: Certain lowercase identifiers are reserved in FeatureScript and cannot be used as variable names. `box` is the most surprising — it is a built-in mutable reference type (used as `new box(undefined)`). Assigning `var box = ...` causes a parse error: `mismatched input 'box' expecting ID`.

**Known reserved identifiers** (cannot be used as variable names):
- `box` — built-in mutable reference type
- `line` — std function that constructs a `Line` value (from `std/line.fs`)
- `plane` — std function that constructs a `Plane` value (from `std/coordSystem.fs`)
- `transform` — std function that constructs a `Transform` value
- `array`, `map`, `string`, `number`, `boolean`, `function`, `undefined`, `builtin` — primitive type keywords

**Type names** (PascalCase) are also reserved: `Box3d`, `Line`, `Plane`, `Path`, `Query`, `Context`, `Id`, `Vector`, `BSplineCurve`, etc. — do not use these as variable names.

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
**Issue**: Unicode characters in `"Name"`, `"Description"`, or any annotation string cause a compile error: `Invalid character in '...' annotation: only printable ASCII allowed`. Common offenders: `–` (en dash U+2013), `−` (minus U+2212), `…` (ellipsis U+2026), `"` / `"` (smart quotes).
**Fix**: Use only plain ASCII: `-` not `–` or `−`, `...` not `…`, `"` not smart quotes.
```featurescript
// WRONG — Unicode en dash / minus sign
annotation { "Name" : "Step (1–6)" }           // U+2013 en dash
annotation { "Description" : "x = a − b" }    // U+2212 minus

// CORRECT — plain ASCII
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
