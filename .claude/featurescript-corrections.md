# FeatureScript Corrections Log

This document tracks corrections needed to LLM-generated FeatureScript code. It serves as a living knowledge base to improve future code generation by learning from past mistakes.

**Purpose**: Reference this log before generating FeatureScript code to avoid known issues and follow corrected patterns.

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

## Statistics

- **Total Corrections**: 4
- **Last Updated**: 2026-01-31
- **Most Common Category**: FeatureScript Syntax (2), Import Issues (1), Type System (1)
- **Critical Bugs Found**: 1 (Missing braces in control flow)

---

*This log helps the FeatureScript expert agent avoid repeating mistakes and generate more accurate code on the first attempt.*
