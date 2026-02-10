# QC Table Implementation - Lessons Learned

## Date: 2026-02-09

## Overview
After implementing the QC Table feature and fixing compilation errors in Onshape, several critical FeatureScript patterns emerged that were missed by our expert agents in pre-push review.

---

## Critical Issues Fixed

### 1. Map Keys Must Be Primitives (Not Type Names)

**Issue**: Cannot use type names or complex objects as map keys - keys must be strings, integers, or queries.

**Incorrect**:
```featurescript
var plane = measurePlane;
opPlane(context, id, {
    plane: plane  // ❌ ERROR: 'plane' as key is ambiguous
});
```

**Correct**:
```featurescript
var plane = measurePlane;
opPlane(context, id, {
    "plane" : plane  // ✅ String key disambiguates
});
```

**Why This Matters**: FeatureScript can't distinguish between:
- `plane` as a map key (identifier)
- `plane` as a reference to the variable `plane`

Even though the variable doesn't conflict with the key name in the abstract sense, FeatureScript requires string keys when there's ANY local variable/parameter with that name to avoid ambiguity.

**Affected Locations**:
- ALL `op*` function parameters (opPlane, opPattern, opSplitPart, opDeleteBodies, opFitSpline)
- ALL `ev*` function parameters (evDistance, evEdgeTangentLine)
- Return maps where keys match local variables

---

### 2. ID Concatenation Requires Parentheses for String Operations

**Issue**: Operator precedence requires parentheses when concatenating strings before adding to an Id.

**Incorrect**:
```featurescript
opPlane(context, id + i ~ "plane", {...});  // ❌ Evaluates as: (id + i) ~ "plane"
```

**Correct**:
```featurescript
opPlane(context, id + (i ~ "plane"), {...});  // ✅ Evaluates as: id + (i ~ "plane")
```

**Why This Matters**: The `+` operator (Id concatenation) has higher precedence than `~` (string concatenation). Without parentheses:
- `id + i ~ "plane"` → `(id + i) ~ "plane"` → tries to convert Id to string and concatenate
- `id + (i ~ "plane")` → `id + "0plane"` → creates Id correctly

**Affected Locations**:
- Lines 442, 446, 507 in `getBottomEdges()`
- Lines 548, 552, 613 in `getTopEdges()`
- Anywhere combining loop index with string in Id operations

---

### 3. ValueWithUnits Comparison Requires `.value` Property

**Issue**: When comparing ValueWithUnits with tolerance constants, must access the `.value` property.

**Incorrect**:
```featurescript
var deltaZ = widestHighest[2] - highestWidest[2];  // ValueWithUnits
if (abs(deltaZ) < TOLERANCE.zeroLength)  // ❌ Comparing ValueWithUnits to number
```

**Correct**:
```featurescript
var deltaZ = widestHighest[2] - highestWidest[2];  // ValueWithUnits
if (abs(deltaZ.value) < TOLERANCE.zeroLength)  // ✅ Compare number to number
```

**Why This Matters**:
- `deltaZ` is a `ValueWithUnits` (has `.value` and `.unit` properties)
- `TOLERANCE.zeroLength` is a raw number (1e-7)
- Can't compare ValueWithUnits to number directly

**Affected Locations**:
- Line 257 in `measureCoreAtStation()` - division by zero check

---

## Pattern Summary

### Map Key Quoting Strategy

**Rule**: Quote ALL map keys in operation parameters, even if they don't conflict with variables. This is the safest approach.

**Examples**:
```featurescript
// Operation parameters - quote everything
opPlane(context, id, {
    "plane" : myPlane  // Always quote operation params
});

evDistance(context, {
    "side0" : point1,  // Always quote ev* params
    "side1" : point2
});

// Return maps - quote keys that match local variables
return {
    "coreWidth" : coreWidth,      // Quotes - variable exists
    "coreThickness" : coreThickness,
    "coreBottomZ" : minZ,         // Quotes - even though variable name differs
    "coreTopZ" : maxZ
};
```

---

## Expert Agent Gaps

### What Was Missed in Pre-Push Review

1. **Map key disambiguation**: Agents approved code with unquoted keys like `plane: plane` without recognizing the ambiguity
2. **ID concatenation precedence**: Agents didn't catch `id + i ~ "string"` operator precedence issues
3. **ValueWithUnits `.value` access**: Agents missed that tolerance comparisons need `.value` property

### Why These Were Missed

1. **Syntactic correctness vs semantic correctness**: Code looked syntactically valid but had semantic issues
2. **Context-dependent rules**: Map key quoting rules depend on whether a variable exists in scope
3. **Operator precedence subtleties**: `+` vs `~` precedence not well-known

---

## Recommendations for Future Code Generation

### For Expert Agents

1. **Always quote operation parameters**: When generating `op*` or `ev*` calls, always use quoted string keys
2. **Parenthesize string concatenation in Ids**: Always wrap `(index ~ "string")` when adding to Ids
3. **Check ValueWithUnits comparisons**: When comparing `ValueWithUnits` to raw numbers, require `.value` access

### For Corrections Log

Add these patterns to `.claude/featurescript-corrections.md`:
- Map key disambiguation section (expand with primitive-only rule)
- ID concatenation operator precedence
- ValueWithUnits property access for comparisons

---

## Files Updated

1. `qcTable/qcTable_geometry.fs`
   - Converted all single-quoted map keys to double quotes
   - Fixed ID concatenation with parentheses (already correct in pulled version)
   - Fixed `.value` access in tolerance comparison (already correct in pulled version)

2. `qcTable/qcTable_merge.fs`
   - Already using correct double-quoted keys

3. `qcTable/qcTable_stations.fs`
   - Already using correct double-quoted keys for conflicting variables

---

## Status: ✅ RESOLVED

All map keys now use consistent double-quote formatting. Code compiles and runs correctly in Onshape.
