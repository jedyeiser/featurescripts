# FeatureScript Development Project

## Overview

This project contains Onshape FeatureScript code for advanced CAD geometry operations. The code is developed locally and tested by copying to Onshape FeatureStudio.

**Target**: FeatureScript 2878 standard

## Project Structure

### Standard Library (`std/`)

The `std/` directory contains 12 FeatureScript library modules providing reusable geometry and utility functions:

**Geometry Operations**:
- `surfaceGeometry.fs`: Surface creation and manipulation primitives
- `curveGeometry.fs`: Curve operations and editing functions
- `offsetSurface.fs`: Surface offset implementation with normal distance
- `fillSurface.fs`: Fill surface creation bounded by curve loops
- `ruledSurface.fs`: Ruled surface (linear loft) between curves
- `nurbsUtils.fs`: NURBS curve and surface utilities

**Mathematics & Utilities**:
- `math.fs`: Mathematical functions, predicates, matrix operations
- `intersections.fs`: Geometric intersection calculations
- `queryVariable.fs`: Query system utilities for entity selection
- `manipulator.fs`: Manipulator creation for interactive controls
- `attributes.fs`: Entity attribute management
- `containers.fs`: Data structure utilities (maps, arrays, sets)

### Project Features

**footprint/**: Feature for creating footprint geometry
**gordonSurface/**: Gordon surface implementation (interpolates through curve networks)
**xSect_EI/**: Cross-section and engineering properties calculations

Each project directory typically contains:
- Main feature implementation (.fs file)
- Supporting utilities and functions
- Import statements referencing `std/` modules

## Development Workflow

### 1. Local Development
- Write and edit FeatureScript code in .fs files
- Use standard library modules from `std/` via imports
- Leverage IDE support (syntax highlighting, etc.)

### 2. Testing in Onshape
- Copy code to Onshape FeatureStudio for testing
- FeatureStudio provides live preview and debugging
- Test with actual CAD geometry and operations

### 3. Import Handling
- **Import comments**: In code, use comment placeholders like `// IMPORT: fpt_constants.fs`
- **Actual imports**: Document IDs and version hashes are managed separately (not in AI-generated code)
- **FeatureScript limitation**: No namespace aliasing - imports make all exported symbols globally available
- **Export policy**: Use `export` keyword by default for all functions in utility files
- **Local imports**: Reference `std/` modules relatively
  ```featurescript
  import(path : "onshape/std/math.fs");
  ```
- **Onshape**: May need to adjust import paths in FeatureStudio
- Standard library reference format: `onshape/std/MODULE_NAME.fs`

### 4. Iteration
- Fix issues discovered in Onshape
- Update local code
- Copy back to FeatureStudio for re-testing
- Repeat until feature works correctly

## FeatureScript Language Notes

### Strong Type System
- Predicates for type checking (e.g., `is3dLengthVector`, `isUnitVector`)
- Type definitions with typecheck functions
- Preconditions validate function inputs
- Function overloading based on parameter types

### Units System
- All geometric quantities use `ValueWithUnits`
- Create values: `5 * meter`, `90 * degree`
- Unit constants: `meter`, `inch`, `millimeter`, `degree`, `radian`
- Automatic unit conversion in expressions

### Key Concepts
- **Query**: System for selecting entities (bodies, faces, edges, etc.)
- **Context**: Modeling context passed to operations
- **Id**: Unique identifier for features and operations
- **Transform**: 4x4 transformation matrices
- **Coordinate Systems**: Origin + XYZ axes defining reference frames

## Claude Code Integration

### Automatic FeatureScript Expert

When working with .fs files, the **FeatureScript expert agent** is automatically invoked. This agent has specialized knowledge of:
- FeatureScript syntax and type system
- Onshape geometry primitives
- CAD operations and best practices
- Analytic geometry and NURBS mathematics

**Agent definition**: `.claude/agents/featurescript-expert.md`

### Corrections Log

The project maintains a living corrections log at `.claude/featurescript-corrections.md` that tracks:
- Common LLM-generated code mistakes
- Corrected patterns with std library references
- Lessons learned from debugging

**Usage**: The agent references this log before generating code to avoid known issues.

### Settings

Project-specific Claude Code settings are in `.claude/settings.json`:
- Auto-invocation configuration for .fs files
- Project metadata
- Workflow notes

## Best Practices

### Code Organization
- Follow patterns from `std/` library modules
- **Export functions by default** - Nearly always use `export` keyword for functions, constants, and enums in utility/library files
- Only make functions non-exported if they are truly internal helpers
- Import management: Use comment placeholders (`// IMPORT: filename.fs`) - actual import statements with document IDs are managed separately
- Group related functions together
- Document complex algorithms

### Geometry Operations
- Use `TOLERANCE.zeroLength` for distance comparisons (~1e-7 meters)
- Use `TOLERANCE.zeroAngle` for angle comparisons (~1e-7 radians)
- Normalize direction vectors when required
- Validate inputs with predicates

### Error Handling
- Check operation results
- Provide meaningful error messages
- Validate geometric validity (e.g., non-degenerate curves)

### Performance
- Cache query results if used multiple times
- Avoid redundant geometric evaluations
- Use efficient algorithms for complex operations

## Common Patterns

### Feature Definition
```featurescript
annotation { "Feature Type Name" : "myFeature" }
export function myFeature(context is Context, id is Id, definition is map)
{
    // 1. Extract parameters from definition
    // 2. Validate inputs
    // 3. Perform operations
    // 4. Return or throw on error
}
```

### Surface Creation
```featurescript
// Offset surface
const offsetDistance = 5 * millimeter;
const offsetFaces = qSomeFaceQuery;
// Use opOffsetSurface or custom implementation

// Ruled surface between curves
const curve1 = qEdge1;
const curve2 = qEdge2;
// Create ruled surface with parameter alignment
```

### Query Usage
```featurescript
// Select entities
const allBodies = qEverything()->qBodyType(BodyType.SOLID);
const createdFaces = qCreatedBy(id + "operation", EntityType.FACE);

// Evaluate to get array
const faceArray = evaluateQuery(context, createdFaces);
```

## Resources

- **Onshape FeatureScript Documentation**: [https://cad.onshape.com/FsDoc/](https://cad.onshape.com/FsDoc/)
- **FeatureScript Standard Library**: Available in Onshape FeatureStudio
- **This Project's std/ Library**: Local reference implementations and utilities

## Getting Started

1. Review the `std/` library to understand available utilities
2. Examine existing project features (`footprint/`, `gordonSurface/`, `xSect_EI/`)
3. Start writing FeatureScript - the expert agent will assist automatically with .fs files
4. Reference `.claude/featurescript-corrections.md` for known issues
5. Test code in Onshape FeatureStudio
6. Update corrections log with any issues discovered

## Notes

- The automatic agent activation ensures consistent, expert-level assistance
- The corrections log grows more valuable over time as it accumulates knowledge
- Always test in Onshape FeatureStudio before considering code complete
- Geometric operations require careful attention to units, tolerances, and numerical stability
