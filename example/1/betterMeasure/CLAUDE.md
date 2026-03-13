# betterMeasure — FeatureScript Reference Notes

Compiled from thorough reading of the Onshape std library. Focused on patterns directly
relevant to this feature. Always verify against source when uncertain.

---

## Annotation Groups

Two valid keys exist. Both work but use them consistently:

```featurescript
// Canonical form (from variable.fs)
annotation { "Group Name" : "Measurements", "Collapsed By Default" : true }
{
    annotation { "Name" : "Distance", "UIHint" : UIHint.READ_ONLY }
    isLength(definition.displayDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
}

// Shorthand form (also seen in std)
annotation { "Name" : "Measurements", "UIHint" : UIHint.COLLAPSED }
{
    ...
}
```

**Prefer `"Group Name"` + `"Collapsed By Default"`.** The `UIHint.COLLAPSED` form exists but
`"Collapsed By Default"` is the documented parameter for groups.

---

## Coordinate Frame Projection — Use fromWorld

For axis delta decomposition, **don't dot manually**. Use `fromWorld`:

```featurescript
// VERBOSE (what we currently do)
var dX = abs(dot(delta, cSys.xAxis));
var dY = abs(dot(delta, yAxis(cSys)));
var dZ = abs(dot(delta, cSys.zAxis));

// CLEAN — fromWorld converts world-space vector into cSys coordinates
var deltaInFrame = fromWorld(cSys, p2) - fromWorld(cSys, p1);
var dX = abs(deltaInFrame[0]);
var dY = abs(deltaInFrame[1]);
var dZ = abs(deltaInFrame[2]);
```

`fromWorld(cSys, worldPoint)` returns the point expressed in cSys coordinates.
`toWorld(cSys, localPoint)` is the inverse.

Note: CoordSystem does NOT store a Y-axis field. `yAxis(cSys)` computes
`cross(cSys.zAxis, cSys.xAxis)` on the fly.

---

## Batch ev* Functions — Prefer Over Loops

Always prefer the plural batch version when evaluating multiple parameters:

```featurescript
// SLOW — N kernel calls in a loop
for (var i = 0; i <= nSteps; i += 1)
{
    var t = t1 + (t2 - t1) * i / nSteps;
    var line = evEdgeTangentLine(context, { "edge" : edgeQ, "parameter" : t });
    ...
}

// FAST — one kernel call
var params = range(t1, t2, nSteps + 1);  // array of nSteps+1 evenly spaced values
var lines = evEdgeTangentLines(context, { "edge" : edgeQ, "parameters" : params });
// lines[i].origin, lines[i].direction
```

Same pattern for faces:
```featurescript
var planes = evFaceTangentPlanes(context, { "face" : face, "parameters" : uvArray });
// planes[i].origin, planes[i].normal
```

---

## range() for Uniform Sampling

```featurescript
// Integer range (inclusive both ends)
range(0, 3)           // [0, 1, 2, 3]

// Uniform spacing with count
range(t1, t2, 21)     // 21 values evenly spaced from t1 to t2 (inclusive)
range(0, 1, 21)       // [0.0, 0.05, 0.10, ..., 1.0]

// Works with ValueWithUnits
range(0 * meter, 1 * meter, 5)  // [0, 0.25, 0.5, 0.75, 1.0] * meter
```

---

## evDistance Return Structure

```featurescript
var result = evDistance(context, { "side0" : pointOrQuery, "side1" : query });

result.distance          // ValueWithUnits — minimum distance
result.sides[0].point    // Vector — closest point on side0
result.sides[1].point    // Vector — closest point on side1
result.sides[1].parameter  // position on entity:
                           //   edge  → number 0..1 (arc-length by default)
                           //   face  → Vector [u, v] (UV parameter, 0..1 in bounding box)
                           //   vertex → undefined
```

`arcLengthParameterization` defaults to `true` for edges — parameter 0.5 is the midpoint.
Pass `"arcLengthParameterization" : false` to get the raw curve parameter instead.

`side0` and `side1` each accept: Query, Vector (3D point), Line, or Plane.

---

## UIHint Reference (Key Values)

```featurescript
UIHint.ALWAYS_HIDDEN              // Hidden field; editing logic only
UIHint.READ_ONLY                  // Display only; editing logic can still write it
UIHint.HORIZONTAL_ENUM            // Enum as horizontal tab bar
UIHint.SHOW_LABEL                 // Show label above enum dropdown
UIHint.DISPLAY_SHORT              // Two params on same row
UIHint.OPPOSITE_DIRECTION         // Arrow toggle button beside previous param
UIHint.OPPOSITE_DIRECTION_CIRCULAR // Circular arrow toggle (angles)
UIHint.VARIABLE_NAME              // String field is a variable name (autocomplete, validation)
UIHint.NO_PREVIEW_PROVIDED        // Hide preview regeneration slider
UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS  // MC selector: existing only
UIHint.COLLAPSED                  // Group is collapsed by default (shorthand)
UIHint.REMEMBER_PREVIOUS_VALUE    // Default to last-used value on new instance
```

Multiple UIHints use an array:
```featurescript
"UIHint" : [UIHint.OPPOSITE_DIRECTION, UIHint.DISPLAY_SHORT]
"UIHint" : [UIHint.VARIABLE_NAME, UIHint.SHOW_LABEL]
```

---

## READ_ONLY + setFeatureComputedParameter

`READ_ONLY` locks the field against user edits. The feature body (and editing logic) can
still write to it via `setFeatureComputedParameter`. This is the correct pattern for
computed/measured display values.

```featurescript
// In precondition:
annotation { "Name" : "Distance", "UIHint" : UIHint.READ_ONLY }
isLength(definition.displayDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

// In feature body — this updates what the user sees:
setFeatureComputedParameter(context, id, { "name" : "displayDistance", "value" : dist });
```

Signature: `setFeatureComputedParameter(context is Context, id is Id, definition is map)`
The `definition` map needs only `"name"` (string) and `"value"` (any).

---

## Feature Name Templates

Templates reference computed parameters by name:
```featurescript
"Feature Name Template" : "Better Measure (#displayDistance)"
// After setFeatureComputedParameter sets "displayDistance", the feature tree
// shows e.g. "Better Measure (12.5 mm)"

// ### prefix omits units label, # includes it
"Feature Name Template" : "###name = #value"
```

---

## verifyVariableName — 3 Arguments Required

```featurescript
// WRONG — only 2 args
verifyVariableName(context, varName);

// CORRECT — faultyParameter is required (highlights the field red on error)
verifyVariableName(context, varName, "mainVarName");
```

Signature: `verifyVariableName(context is Context, name is string, faultyParameter is string)`

Validates syntax AND checks for query variable name conflicts. Throws `regenError` on failure.

---

## Editing Logic Signature

Max 7 parameters. `clickedButton` does NOT exist:

```featurescript
export function myEditingLogic(context is Context, id is Id,
    oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map,
    hiddenBodies is Query) returns map   // hiddenBodies is optional
```

`specifiedParameters` is a map of parameter names → true/false indicating which params
the user just touched. Use this to avoid overwriting parameters the user didn't change:

```featurescript
if (specifiedParameters.entity1 == true)
{
    definition.entity1Type = getEntityBodyType(context, definition.entity1);
}
```

---

## opFitSpline

Creates a spline wire through an array of 3D points:

```featurescript
opFitSpline(context, id + "wire", {
    "points" : pts  // array of Vector (3D length vectors)
    // Optional:
    // "parameters"       : array of numbers (parameterization)
    // "startDerivative"  : Vector (tangent at start)
    // "endDerivative"    : Vector (tangent at end)
});
```

If `points[0] == points[last]`, the spline is closed.

---

## BMEntityType — Why It Exists

Neither `EntityType` (VERTEX, EDGE, FACE, BODY) nor `BodyType` (SOLID, SHEET, WIRE,
MATE_CONNECTOR, POINT, COMPOSITE) alone covers the set of things a user can select.

`BMEntityType` is the union:
- Body sub-types from `BodyType`: SOLID, SHEET, WIRE, MATE_CONNECTOR
- Sub-body entities from `EntityType`: EDGE, VERTEX
- Plus: NONE

It is a hidden field (`ALWAYS_HIDDEN`) set by editing logic. The precondition reads it
to conditionally show sub-selection UI (solidRef, sheetRef, MCAxis). It is not a display
field — users never see it.

---

## Common Pitfalls Found in This Codebase

1. **acos/asin/atan2 return ValueWithUnits** — do NOT multiply by `radian` again.
   `acos(x)` already returns radians as ValueWithUnits.

2. **BMCoordSystem must be in betterMeasureUtils.fs** — it's referenced as a type
   annotation in `resolveCoordFrame`. Utils doesn't import betterMeasure, so the type
   must live in utils alongside all other BM enums.

3. **`// IMPORT: file.fs` is a comment** — must be resolved to a real import statement
   in Onshape before pushing. Every push requires this to be a real import.

4. **`evDistance` sides[1].parameter for edges** — returns a single number (arc-length
   0..1), not an array. Access as `dist.sides[1].parameter` directly, not `[0]`.
   Wait — verify this. The agent's report says it may be context-dependent. CHECK before using.

5. **`dot(vectorWithUnits, unitlessVector)`** — returns ValueWithUnits, not a number.
   Comparing the result to `TOLERANCE.zeroLength` (a raw number) requires `.value`.
