# Curve Mapping Module - Design Document

**Date:** 2026-02-10
**Status:** Design Phase
**Target:** FeatureScript 2878

---

## Overview

This module provides core functionality for mapping curves and geometry between reference curves using Frenet frame transformations. It serves as the foundation for WrapCurve, Wrap and Loft, and Deform Body features.

---

## Architecture

### Module Hierarchy
```
curve_chain.fs           - G0/G1 continuous multi-edge chains with arc-length parameterization
curve_mapping_core.fs    - Frenet frame coordinate transformations
curve_mapping_utils.fs   - Endpoint-exact approximation, validation utilities
wrap_curve.fs           - User-facing curve wrapping feature
wrap_and_loft.fs        - Curve wrapping + surface lofting feature
deform_body.fs          - Full body deformation feature
```

### Dependencies
- **Onshape std**: `common.fs`, `splineUtils.fs`, `path.fs`, `evaluate.fs`
- **Tools library**: `frenet.fs`, `arc_length.fs`, `point_projection.fs`, `bspline_data.fs`, `curve_operations.fs`

---

## Module 1: curve_chain.fs

### Purpose
Represents a continuous chain of edges with G0 or G1 continuity. Provides unified parameterization (0→1) across all edges with arc-length support.

### Data Structure

```featurescript
export enum ChainContinuity
{
    annotation { "Name" : "G0 (Position)" }
    G0,
    annotation { "Name" : "G1 (Tangent)" }
    G1
}

// Internal representation (opaque to users)
type CurveChain typecheck canBeCurveChain;

predicate canBeCurveChain(value)
{
    value is map;
    value.edges is array;              // Array of edge queries
    value.path is Path;                // Constructed path from edges
    value.continuity is ChainContinuity;
    value.edgeBoundaries is array;     // Arc-length boundaries [0, s1, s2, ..., 1]
    value.totalLength is ValueWithUnits;
    value.arcLengthTable is map;       // From buildArcLengthTable()
    value.isValid is boolean;
}
```

### Core Functions

#### **buildCurveChain**
```featurescript
/**
 * Construct a curve chain from edge queries with continuity validation.
 *
 * @param context {Context} : Onshape context
 * @param edges {Query} : Query for edges to chain (order matters)
 * @param continuity {ChainContinuity} : Required continuity level
 * @param options {map} : Optional settings:
 *                        - arcLengthSamples: Table resolution (default 200)
 *                        - tangentTolerance: G1 angle tolerance (default 1e-3 rad)
 *                        - gapTolerance: G0 gap tolerance (default 1e-6 m)
 * @returns {CurveChain} : Chain data structure
 * @throws regenError if continuity validation fails
 *
 * @example Build G1 continuous chain:
 *   var chain = buildCurveChain(context, qEdgeTopologyFilter(myEdges, EdgeTopology.TWO_SIDED),
 *                               ChainContinuity.G1, {});
 */
export function buildCurveChain(context is Context, edges is Query,
                                continuity is ChainContinuity,
                                options is map) returns CurveChain
```

**Implementation notes:**
- Evaluate edges in order, store start/end points
- Check G0: `norm(endPt[i] - startPt[i+1]) < gapTolerance`
- Check G1: `abs(PI - angleBetween(tangent[i], tangent[i+1])) < tangentTolerance`
- Build path using `constructPath()`
- Compute arc-length for each edge, store normalized boundaries
- Build unified arc-length table for entire chain

#### **evaluateChain**
```featurescript
/**
 * Evaluate chain at normalized parameter(s).
 *
 * @param chain {CurveChain} : Chain to evaluate
 * @param parameters {array} : Parameter values in [0, 1]
 * @returns {map} : {
 *                    points: array,      // 3D positions
 *                    tangents: array,    // Tangent vectors (optional)
 *                    derivatives: number // Derivative order evaluated
 *                  }
 */
export function evaluateChain(chain is CurveChain, parameters is array,
                              nDerivatives is number) returns map
```

#### **getChainFrenetFrame**
```featurescript
/**
 * Compute Frenet frame at chain parameter.
 *
 * @param chain {CurveChain} : Chain to evaluate
 * @param parameter {number} : Chain parameter in [0, 1]
 * @returns {EdgeCurvatureResult} : Frenet frame + curvature
 *
 * @example Get frame at chain midpoint:
 *   var frame = getChainFrenetFrame(chain, 0.5);
 *   var tangent = frame.frame.zAxis;
 *   var normal = frame.frame.xAxis;
 */
export function getChainFrenetFrame(chain is CurveChain, parameter is number)
    returns EdgeCurvatureResult
```

**Implementation notes:**
- Map chain parameter to path parameter
- Use `computeFrenetFrame()` from `tools/frenet.fs`
- Handle edge boundaries carefully (avoid discontinuities)

#### **sampleChainUniform**
```featurescript
/**
 * Sample chain at uniformly-spaced arc lengths.
 *
 * @param chain {CurveChain} : Chain to sample
 * @param numPoints {number} : Number of sample points
 * @returns {map} : {
 *                    parameters: array,  // Chain parameters [0, ..., 1]
 *                    points: array,      // 3D positions
 *                    tangents: array,    // Tangent vectors
 *                    arcLengths: array   // Arc lengths from start
 *                  }
 */
export function sampleChainUniform(chain is CurveChain, numPoints is number)
    returns map
```

#### **chainParameterAtArcLength**
```featurescript
/**
 * Find chain parameter at given arc length from chain start.
 *
 * @param chain {CurveChain} : Chain
 * @param arcLength {ValueWithUnits} : Target arc length
 * @returns {number} : Chain parameter in [0, 1]
 */
export function chainParameterAtArcLength(chain is CurveChain,
                                          arcLength is ValueWithUnits)
    returns number
```

#### **projectPointOnChain**
```featurescript
/**
 * Project point onto chain, finding closest point.
 *
 * @param chain {CurveChain} : Chain
 * @param point {Vector} : Query point
 * @param options {map} : Options passed to projectPointOnCurve
 * @returns {map} : {
 *                    chainParameter: number,      // Parameter on chain [0,1]
 *                    edgeIndex: number,          // Which edge in chain
 *                    localParameter: number,     // Parameter on that edge
 *                    point: Vector,              // Closest point
 *                    distance: ValueWithUnits    // Distance to query point
 *                  }
 */
export function projectPointOnChain(chain is CurveChain, point is Vector,
                                    options is map) returns map
```

#### **validateChainContinuity**
```featurescript
/**
 * Validate continuity at chain joints (internal helper).
 * Called by buildCurveChain, can be used standalone for diagnostics.
 */
function validateChainContinuity(context is Context, edgeArray is array,
                                 continuity is ChainContinuity,
                                 options is map) returns map
```

#### **getChainLength**
```featurescript
/**
 * Get total arc length of chain.
 */
export function getChainLength(chain is CurveChain) returns ValueWithUnits
```

#### **debugChain**
```featurescript
/**
 * Visualize chain with debug graphics.
 * - Draws tangent arrows at start and end
 * - Shows joint locations with colored spheres (green=G1, yellow=G0)
 * - Displays edge boundaries
 */
export function debugChain(context is Context, chain is CurveChain,
                          options is map)
```

---

## Module 2: curve_mapping_core.fs

### Purpose
Core Frenet frame transformation logic for mapping points between curves.

### Enums

```featurescript
export enum MappingMode
{
    annotation { "Name" : "Preserve arc length" }
    LENGTH,
    annotation { "Name" : "Preserve parameter" }
    PARAM
}

export enum AlignmentMode
{
    annotation { "Name" : "Automatic (closest point)" }
    AUTO,
    annotation { "Name" : "Manual alignment" }
    MANUAL
}
```

### Core Functions

#### **buildCurveMapping**
```featurescript
/**
 * Set up curve mapping between fromChain and toChain.
 *
 * @param context {Context} : Onshape context
 * @param fromChain {CurveChain} : Source reference chain
 * @param toChain {CurveChain} : Target reference chain
 * @param sourcePoint {Vector} : Alignment point on source geometry
 * @param alignmentMode {AlignmentMode} : AUTO or MANUAL
 * @param options {map} : {
 *                          fromAlignPoint: Vector (required if MANUAL)
 *                          toAlignPoint: Vector (required if MANUAL)
 *                          mappingMode: MappingMode (default LENGTH)
 *                          checkCoplanar: boolean (default true)
 *                          warnNonCoplanar: boolean (default true)
 *                        }
 * @returns {map} : {
 *                    fromChain: CurveChain,
 *                    toChain: CurveChain,
 *                    fromRefParam: number,     // Reference point on fromChain
 *                    toRefParam: number,       // Corresponding point on toChain
 *                    mappingMode: MappingMode,
 *                    isCoplanar: boolean,
 *                    coplanarityAngle: ValueWithUnits (if not coplanar)
 *                  }
 *
 * Performs alignment:
 * - AUTO: Projects sourcePoint onto both chains, uses closest points
 * - MANUAL: Projects fromAlignPoint/toAlignPoint onto respective chains
 *
 * Checks coplanarity (if enabled):
 * - Fit plane to fromChain sample points
 * - Measure max distance of toChain points from plane
 * - If > threshold, set isCoplanar = false and warn user
 */
export function buildCurveMapping(context is Context, id is Id,
                                  fromChain is CurveChain,
                                  toChain is CurveChain,
                                  sourcePoint is Vector,
                                  alignmentMode is AlignmentMode,
                                  options is map) returns map
```

#### **mapPointToCurve**
```featurescript
/**
 * Map a single point from fromChain reference frame to toChain.
 *
 * @param context {Context} : Onshape context
 * @param mapping {map} : Result from buildCurveMapping()
 * @param sourcePoint {Vector} : Point to map (in world coordinates)
 * @returns {map} : {
 *                    fromParam: number,          // Parameter on fromChain
 *                    fromFrame: EdgeCurvatureResult, // Frenet frame at fromParam
 *                    localCoords: Vector,        // [tangent, normal, binormal]
 *                    toParam: number,            // Mapped parameter on toChain
 *                    toFrame: EdgeCurvatureResult,   // Frenet frame at toParam
 *                    mappedPoint: Vector         // Result in world coordinates
 *                  }
 *
 * Algorithm:
 * 1. Project sourcePoint onto fromChain → get fromParam
 * 2. Compute Frenet frame at fromParam
 * 3. Transform sourcePoint to Frenet coordinates
 * 4. Compute toParam based on mappingMode:
 *    - LENGTH: arc-length from fromRef = arc-length from toRef
 *    - PARAM: parameter delta preserved
 * 5. Get Frenet frame at toParam
 * 6. Transform local coords to world using toFrame
 */
export function mapPointToCurve(context is Context, mapping is map,
                                sourcePoint is Vector) returns map
```

#### **mapPointsToCurve**
```featurescript
/**
 * Batch version of mapPointToCurve for performance.
 *
 * @param context {Context} : Onshape context
 * @param mapping {map} : Result from buildCurveMapping()
 * @param sourcePoints {array} : Array of points to map
 * @returns {array} : Array of mapping results (same structure as mapPointToCurve)
 *
 * Performance optimization:
 * - Batch evaluations on chains
 * - Reuse frame computations where possible
 */
export function mapPointsToCurve(context is Context, mapping is map,
                                 sourcePoints is array) returns array
```

#### **checkFrameConsistency**
```featurescript
/**
 * Check Frenet frame orientation consistency between curves.
 *
 * For coplanar curves, ensures normals point in consistent directions.
 * Detects potential binormal flipping issues.
 *
 * @param fromFrame {EdgeCurvatureResult} : Frame on fromChain
 * @param toFrame {EdgeCurvatureResult} : Frame on toChain
 * @returns {map} : {
 *                    consistent: boolean,
 *                    normalFlip: boolean,    // Normals point opposite
 *                    binormalFlip: boolean,  // Binormals point opposite
 *                    maxAngle: ValueWithUnits // Max angle between corresponding axes
 *                  }
 */
function checkFrameConsistency(fromFrame is EdgeCurvatureResult,
                               toFrame is EdgeCurvatureResult) returns map
```

#### **adjustFrameOrientation**
```featurescript
/**
 * Adjust toFrame orientation to match fromFrame (if needed).
 *
 * Used internally when frame consistency check fails.
 * Flips normal/binormal to maintain consistent orientation.
 */
function adjustFrameOrientation(fromFrame is EdgeCurvatureResult,
                                toFrame is EdgeCurvatureResult,
                                consistency is map) returns EdgeCurvatureResult
```

---

## Module 3: curve_mapping_utils.fs

### Purpose
Utility functions for curve approximation and validation.

### Core Functions

#### **approximateWithEndpoints**
```featurescript
/**
 * Approximate BSpline through points with guaranteed endpoint interpolation.
 *
 * Fixes the endpoint accuracy bug from old geometryManipulators code.
 *
 * @param context {Context} : Onshape context
 * @param points {array} : Points to fit (MUST include endpoints)
 * @param degree {number} : Spline degree
 * @param options {map} : {
 *                          tolerance: ValueWithUnits (default 1e-5 m)
 *                          maxControlPoints: number (default 50)
 *                          isPeriodic: boolean (default false)
 *                          endpointWeight: number (default 1000) // High weight for endpoints
 *                        }
 * @returns {BSplineCurve} : Fitted curve
 *
 * Algorithm:
 * 1. Verify points[0] and points[end] are included
 * 2. Call approximateSpline with weighted targets
 * 3. Assign very high weight to endpoints for interpolation
 * 4. Verify result hits endpoints within tight tolerance
 */
export function approximateWithEndpoints(context is Context, points is array,
                                         degree is number, options is map)
    returns BSplineCurve
```

#### **ensureSamplingIncludesEndpoints**
```featurescript
/**
 * Ensure sampling array includes t=0.0 and t=1.0 exactly.
 *
 * @param parameters {array} : Parameter array (may or may not include endpoints)
 * @returns {array} : Modified array with 0.0 and 1.0 included
 *
 * Used when sampling curves to guarantee endpoint coverage.
 */
export function ensureSamplingIncludesEndpoints(parameters is array) returns array
```

#### **validateMappedCurve**
```featurescript
/**
 * Validate mapped curve quality.
 *
 * Checks:
 * - Endpoint accuracy
 * - Smoothness (no kinks)
 * - Arc length preservation (if LENGTH mode)
 * - Parameter correspondence (if PARAM mode)
 *
 * @returns {map} : {
 *                    valid: boolean,
 *                    endpointError: ValueWithUnits,
 *                    maxCurvatureJump: ValueWithUnits,
 *                    lengthRatio: number (if LENGTH mode)
 *                    warnings: array of strings
 *                  }
 */
export function validateMappedCurve(context is Context,
                                    sourceCurve is BSplineCurve,
                                    mappedCurve is BSplineCurve,
                                    mapping is map,
                                    options is map) returns map
```

#### **checkCoplanarity**
```featurescript
/**
 * Check if two chains are approximately coplanar.
 *
 * @param context {Context} : Onshape context
 * @param chain1 {CurveChain} : First chain
 * @param chain2 {CurveChain} : Second chain
 * @param options {map} : {
 *                          numSamples: number (default 20)
 *                          tolerance: ValueWithUnits (default 1e-3 m)
 *                        }
 * @returns {map} : {
 *                    coplanar: boolean,
 *                    maxDeviation: ValueWithUnits,
 *                    fittedPlane: Plane,
 *                    angle: ValueWithUnits // Angle between normals if not coplanar
 *                  }
 *
 * Algorithm:
 * 1. Sample both chains uniformly
 * 2. Fit plane to combined point cloud (SVD or evFitPlane)
 * 3. Measure max distance of any point from plane
 * 4. If > tolerance, compute angle between chain tangent planes
 */
export function checkCoplanarity(context is Context,
                                 chain1 is CurveChain,
                                 chain2 is CurveChain,
                                 options is map) returns map
```

---

## Design Decisions

### 1. Why Chain Type vs. Direct Path?
- **Validation**: Enforce continuity requirements at construction
- **Arc-length**: Unified parameterization across all edges
- **Metadata**: Store edge boundaries, continuity info
- **API clarity**: Chain operations vs. generic path operations

### 2. Length Preservation Details
**LENGTH mode**: Arc-length based mapping
```
Given:
  - fromChain: total length L_from
  - toChain: total length L_to
  - fromRef at arc-length s_from_ref from start
  - Point at arc-length s_from from start

Compute:
  - delta_from = s_from - s_from_ref (can be negative)
  - delta_to = delta_from (preserve actual distance)
  - s_to = s_to_ref + delta_to
  - toParam = chainParameterAtArcLength(toChain, s_to)
```

**PARAM mode**: Parameter delta preserved
```
Given:
  - fromParam_ref = 0.3 (reference point)
  - fromParam = 0.5 (point to map)

Compute:
  - delta = fromParam - fromParam_ref = 0.2
  - toParam = toParam_ref + delta
  - Clamp to [0, 1]
```

### 3. Endpoint Handling Strategy
- Always include t=0.0 and t=1.0 in sampling
- Use high-weight endpoint constraints in `approximateSpline`
- Verify endpoint accuracy post-approximation
- If endpoints off by > 1e-7m, adjust control points directly

### 4. Frenet Frame Consistency
For coplanar curves with normals pointing "broadly in same direction":
- Check: `dot(normal_from, normal_to) > 0`
- Check: `dot(binormal_from, binormal_to) > 0`
- If either fails, flip the problematic axis on toFrame
- This prevents twisted mappings

### 5. Performance Considerations
- **Batch evaluations**: Use `evaluateSpline` with arrays, not loops
- **Table caching**: Build arc-length tables once, reuse
- **Coplanarity check**: Only if enabled (default on, can disable for speed)
- **Frame consistency**: Check at reference points only, not every point

---

## Testing Strategy

### Unit Tests (when tools available)
1. **Chain construction**:
   - G0 continuous edges (should pass)
   - G1 continuous edges (should pass)
   - Discontinuous edges (should fail with clear error)

2. **Arc-length accuracy**:
   - Sample chain uniformly → verify spacing
   - Compare chain length to sum of edge lengths

3. **Mapping accuracy**:
   - Map grid of points → verify Frenet transformation
   - LENGTH mode: verify arc-length preservation
   - PARAM mode: verify parameter preservation

4. **Endpoint exactness**:
   - Map curve endpoints → verify hit exactly
   - Map intermediate points → verify accuracy

### Integration Tests (in Onshape)
1. **Simple cases**:
   - Line to line (different lengths)
   - Arc to arc (different radii)
   - Line to arc

2. **Complex cases**:
   - Multi-edge chains
   - Non-coplanar curves
   - Self-intersecting source curves

---

## Error Handling

### Expected Errors
```featurescript
// Continuity validation failure
throw regenError("Chain edges not G1 continuous at edge 2-3. " ~
                 "Angle between tangents: 15.2°",
                 ["fromEdge"]);

// Alignment point not on curves
throw regenError("Source point not within projection tolerance of fromEdge. " ~
                 "Closest distance: 5.2mm",
                 ["sourcePoint", "fromEdge"]);

// Invalid mapping (self-intersection, etc.)
throw regenError("Mapped curve self-intersects. " ~
                 "Reduce source curve complexity or adjust reference points.",
                 ["sourceCurves"]);
```

### Warnings
```featurescript
// Non-coplanar curves
reportFeatureInfo(context, id,
    "From and To curves not coplanar (max deviation: 12.3mm). " ~
    "Performance may be reduced.");

// Large length mismatch in LENGTH mode
reportFeatureWarning(context, id,
    "From curve length (100mm) much smaller than To curve length (500mm). " ~
    "Mapped geometry may be stretched significantly.");
```

---

## API Usage Examples

### Example 1: Simple Curve Mapping
```featurescript
// Build chains
var fromChain = buildCurveChain(context, definition.fromEdge,
                                ChainContinuity.G1, {});
var toChain = buildCurveChain(context, definition.toEdge,
                              ChainContinuity.G1, {});

// Set up mapping
var mapping = buildCurveMapping(context, id, fromChain, toChain,
                                definition.sourcePoint,
                                AlignmentMode.AUTO,
                                { "mappingMode" : MappingMode.LENGTH });

// Sample source curve
var sourceChain = buildCurveChain(context, definition.sourceCurves,
                                  ChainContinuity.G0, {});
var samples = sampleChainUniform(sourceChain, 50);

// Map all points
var mappedResults = mapPointsToCurve(context, mapping, samples.points);
var mappedPoints = mapArray(mappedResults, function(r) { return r.mappedPoint; });

// Create mapped curve
var mappedCurve = approximateWithEndpoints(context, mappedPoints, 3, {
    "tolerance" : definition.tolerance
});
```

### Example 2: Manual Alignment
```featurescript
var mapping = buildCurveMapping(context, id, fromChain, toChain,
                                definition.sourcePoint,
                                AlignmentMode.MANUAL,
                                {
                                    "fromAlignPoint" : definition.fromRefPoint,
                                    "toAlignPoint" : definition.toRefPoint,
                                    "mappingMode" : MappingMode.PARAM
                                });
```

### Example 3: Debug Visualization
```featurescript
// Visualize chains
debugChain(context, fromChain, { "color" : DebugColor.RED });
debugChain(context, toChain, { "color" : DebugColor.BLUE });

// Check coplanarity
var coplanar = checkCoplanarity(context, fromChain, toChain, {});
if (!coplanar.coplanar)
{
    println("Chains deviate by: " ~ toString(coplanar.maxDeviation));
    println("Angle between planes: " ~ toString(coplanar.angle));
}
```

---

## Next Steps

1. **Get approval** on this design
2. **Create files in Onshape** (see file list below)
3. **Implement modules** in order: chain → mapping → utils
4. **Test incrementally** with simple Onshape parts
5. **Build user features** once core is solid

---

## Files to Create in Onshape

1. `curve_chain.fs` - Chain data structure and operations
2. `curve_mapping_core.fs` - Frenet frame mapping logic
3. `curve_mapping_utils.fs` - Approximation and validation utilities

**Dependencies to add to existing tools:**
- None required - all dependencies already exist in std/ and tools/
