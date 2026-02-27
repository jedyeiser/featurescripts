# FeatureScript Corrections Log

This document tracks corrections needed to LLM-generated FeatureScript code. It serves as a living knowledge base to improve future code generation by learning from past mistakes.

**Purpose**: Reference this log before generating FeatureScript code to avoid known issues and follow corrected patterns.

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

## Statistics

- **Total Corrections**: 12
- **Last Updated**: 2026-02-18
- **Most Common Category**: FeatureScript Syntax (7), Units Handling (3), Import Issues (1), Type System (1), Matrix/Array Indexing (1)
- **Critical Bugs Found**: 2 (Missing braces in control flow, Q matrix indexing)
- **Latest Additions**: No pow() function (use ^ operator)

---

*This log helps the FeatureScript expert agent avoid repeating mistakes and generate more accurate code on the first attempt.*
