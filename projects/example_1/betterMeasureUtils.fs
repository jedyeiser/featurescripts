FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * betterMeasureUtils.fs
 *
 * Shared helpers for the betterMeasure feature. Exports enums and all
 * computation functions so betterMeasure.fs stays declaration-focused.
 *
 * Exported enums:   BMEntityType, BMSolidRef, BMSheetRef
 * Exported helpers: getEntityBodyType, resolveEntityPoint, resolveCoordFrame,
 *                   extractEntityDirection, measureAngleBetweenEntities,
 *                   computeRelativeEuler, measureAlongEdge, measureAlongFace,
 *                   warnIfOffEntity, publishIfEnabled
 */

// ══════════════════════════════════════════════════════════════════════════════
// ENUMS
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Category of a selected entity, determined by editing logic and stored in
 * hidden definition fields (entity1Type, entity2Type). Controls which
 * sub-selection enum is shown in the precondition.
 *
 * Values are set by getEntityBodyType() in the editing logic function.
 */
export enum BMEntityType
{
    annotation { "Name" : "None" }           NONE,
    annotation { "Name" : "Solid" }          SOLID,
    annotation { "Name" : "Sheet" }          SHEET,
    annotation { "Name" : "Wire" }           WIRE,
    annotation { "Name" : "Edge" }           EDGE,
    annotation { "Name" : "Vertex" }         VERTEX,
    annotation { "Name" : "Mate connector" } MATE_CONNECTOR
}

/**
 * Reference point sub-selection for solid bodies.
 * COM            → center of mass via evApproximateCentroid
 * NEAREST_FACE   → closest point on any owned face to the other entity
 * NEAREST_EDGE   → closest point on any owned edge
 * NEAREST_VERTEX → closest owned vertex
 */
export enum BMSolidRef
{
    annotation { "Name" : "Center of mass" }   COM,
    annotation { "Name" : "Nearest face" }     NEAREST_FACE,
    annotation { "Name" : "Nearest edge" }     NEAREST_EDGE,
    annotation { "Name" : "Nearest vertex" }   NEAREST_VERTEX
}

/**
 * Reference point sub-selection for sheet bodies.
 * COA            → center of area (centroid of all owned faces)
 * NEAREST_EDGE   → closest point on any owned edge to the other entity
 * NEAREST_VERTEX → closest owned vertex
 */
export enum BMSheetRef
{
    annotation { "Name" : "Center of area" } COA,
    annotation { "Name" : "Nearest edge" }   NEAREST_EDGE,
    annotation { "Name" : "Nearest vertex" } NEAREST_VERTEX
}

/**
 * Axis of a mate connector to use for direction extraction in ANGLE / VECTOR modes.
 * Replaces MateConnectorAxisType, which only defines in-plane (X/Y) axes and has
 * no PLUS_Z member — using it with Z_AXIS defaults causes a silent compile failure.
 */
export enum BMMCAxis
{
    annotation { "Name" : "X axis" } X_AXIS,
    annotation { "Name" : "Y axis" } Y_AXIS,
    annotation { "Name" : "Z axis" } Z_AXIS
}

// ══════════════════════════════════════════════════════════════════════════════
// ENTITY CATEGORY DETECTION
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Determines the broad category of the first entity resolved by entityQuery.
 * Called from betterMeasureEditingLogic whenever entity1 or entity2 changes.
 * Sets definition.entity1Type / entity2Type to control sub-selection visibility.
 *
 * Priority order (most specific first, since MC is also a BodyType.WIRE body):
 *   MATE_CONNECTOR → SOLID → SHEET → WIRE → EDGE → VERTEX → NONE
 *
 * @param context      {Context}
 * @param entityQuery  {Query}   — should resolve to ≤ 1 entity
 * @returns {BMEntityType}
 *
 * Ref: query.fs — qBodyType, qEntityFilter, isQueryEmpty
 */
export function getEntityBodyType(context is Context, entityQuery is Query) returns BMEntityType
{
    if (isQueryEmpty(context, entityQuery))
    {
        return BMEntityType.NONE;
    }

    // MATE_CONNECTOR must be checked before WIRE (MC is a wire body internally)
    if (!isQueryEmpty(context, qBodyType(entityQuery, BodyType.MATE_CONNECTOR)))
    {
        return BMEntityType.MATE_CONNECTOR;
    }

    if (!isQueryEmpty(context, qBodyType(entityQuery, BodyType.SOLID)))
    {
        return BMEntityType.SOLID;
    }

    if (!isQueryEmpty(context, qBodyType(entityQuery, BodyType.SHEET)))
    {
        return BMEntityType.SHEET;
    }

    if (!isQueryEmpty(context, qBodyType(entityQuery, BodyType.WIRE)))
    {
        return BMEntityType.WIRE;
    }

    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.EDGE)))
    {
        return BMEntityType.EDGE;
    }

    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.VERTEX)))
    {
        return BMEntityType.VERTEX;
    }

    return BMEntityType.NONE;
}

// ══════════════════════════════════════════════════════════════════════════════
// ENTITY POINT RESOLUTION
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Resolves a single 3D world-space reference point from entityQuery based on
 * its entity category and the user's sub-selection.
 *
 * NEAREST_* sub-selections use otherPoint as the "from" side of evDistance.
 * Pass otherPoint = undefined on the first pass (before the opposing point
 * is known). The caller re-resolves both points in a second pass.
 * When otherPoint is undefined and a NEAREST_* ref is active, the function
 * falls back to the entity centroid so a valid initial point is always returned.
 *
 * Resolution table:
 *   SOLID / COM            → evApproximateCentroid
 *   SOLID / NEAREST_FACE   → closest point on owned faces to otherPoint
 *   SOLID / NEAREST_EDGE   → closest point on owned edges to otherPoint
 *   SOLID / NEAREST_VERTEX → closest owned vertex to otherPoint
 *   SHEET / COA            → evApproximateCentroid
 *   SHEET / NEAREST_EDGE   → closest point on owned edges to otherPoint
 *   SHEET / NEAREST_VERTEX → closest owned vertex to otherPoint
 *   WIRE                   → evApproximateCentroid (wire centroid)
 *   EDGE                   → closest point on edge to otherPoint (midpoint fallback)
 *   VERTEX                 → evVertexPoint
 *   MATE_CONNECTOR         → evMateConnector .origin
 *   NONE / fallback        → evApproximateCentroid
 *
 * @param context     {Context}
 * @param entityQuery {Query}
 * @param entityType  {BMEntityType}
 * @param solidRef    {BMSolidRef}  — used when entityType == SOLID
 * @param sheetRef    {BMSheetRef}  — used when entityType == SHEET
 * @param otherPoint              — opposing measurement point (Vector) or undefined
 * @returns {Vector} — 3D world-space position with length units
 *
 * Ref: evaluate.fs — evApproximateCentroid, evVertexPoint, evMateConnector,
 *                    evDistance, evEdgeTangentLine
 */
export function resolveEntityPoint(context is Context, entityQuery is Query,
    entityType is BMEntityType, solidRef is BMSolidRef, sheetRef is BMSheetRef,
    otherPoint) returns Vector
{
    // ── Mate connector: frame origin
    if (entityType == BMEntityType.MATE_CONNECTOR)
    {
        return evMateConnector(context, { "mateConnector" : entityQuery }).origin;
    }

    // ── Vertex: exact point
    // Ref: evaluate.fs:1313 — evVertexPoint
    if (entityType == BMEntityType.VERTEX)
    {
        return evVertexPoint(context, { "vertex" : entityQuery });
    }

    // ── Solid body
    if (entityType == BMEntityType.SOLID)
    {
        if (solidRef == BMSolidRef.COM || otherPoint == undefined)
        {
            return evApproximateCentroid(context, { "entities" : entityQuery });
        }

        // Sub-entity queries for NEAREST_* modes
        var subQ;
        if (solidRef == BMSolidRef.NEAREST_FACE)
        {
            subQ = qOwnedByBody(entityQuery, EntityType.FACE);
        }
        else if (solidRef == BMSolidRef.NEAREST_EDGE)
        {
            subQ = qOwnedByBody(entityQuery, EntityType.EDGE);
        }
        else // NEAREST_VERTEX
        {
            subQ = qOwnedByBody(entityQuery, EntityType.VERTEX);
        }
        return evDistance(context, { "side0" : otherPoint, "side1" : subQ }).sides[1].point;
    }

    // ── Sheet body
    if (entityType == BMEntityType.SHEET)
    {
        if (sheetRef == BMSheetRef.COA || otherPoint == undefined)
        {
            return evApproximateCentroid(context, { "entities" : entityQuery });
        }

        var subQ;
        if (sheetRef == BMSheetRef.NEAREST_EDGE)
        {
            subQ = qOwnedByBody(entityQuery, EntityType.EDGE);
        }
        else // NEAREST_VERTEX
        {
            subQ = qOwnedByBody(entityQuery, EntityType.VERTEX);
        }
        return evDistance(context, { "side0" : otherPoint, "side1" : subQ }).sides[1].point;
    }

    // ── Wire body: centroid (wire may have multiple edges)
    if (entityType == BMEntityType.WIRE)
    {
        return evApproximateCentroid(context, { "entities" : entityQuery });
    }

    // ── Edge: closest point on edge, or midpoint if no otherPoint
    if (entityType == BMEntityType.EDGE)
    {
        if (otherPoint == undefined)
        {
            // Midpoint fallback via arc-length parameterization
            // Ref: evaluate.fs — evEdgeTangentLine
            return evEdgeTangentLine(context, { "edge" : entityQuery, "parameter" : 0.5 }).origin;
        }
        return evDistance(context, { "side0" : otherPoint, "side1" : entityQuery }).sides[1].point;
    }

    // ── NONE or unknown: centroid of whatever was selected
    return evApproximateCentroid(context, { "entities" : entityQuery });
}

// ══════════════════════════════════════════════════════════════════════════════
// COORDINATE FRAME RESOLUTION
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Returns the CoordSystem to use for axis-delta decomposition.
 *
 * BMCoordSystem.WORLD          → WORLD_COORD_SYSTEM (x=worldX, z=worldZ)
 * BMCoordSystem.MATE_CONNECTOR → evMateConnector result for csQuery
 *
 * Falls back to WORLD_COORD_SYSTEM if csQuery is empty (MC not yet selected).
 *
 * @param context         {Context}
 * @param coordSystemType {BMCoordSystem}
 * @param csQuery         {Query}  — mate connector; only used when MATE_CONNECTOR
 * @returns {CoordSystem}
 *
 * Ref: coordSystem.fs — WORLD_COORD_SYSTEM; evaluate.fs — evMateConnector
 */
export function resolveCoordFrame(context is Context, coordSystemType is BMCoordSystem,
    csQuery is Query) returns CoordSystem
{
    if (coordSystemType == BMCoordSystem.WORLD || isQueryEmpty(context, csQuery))
    {
        return WORLD_COORD_SYSTEM;
    }

    return evMateConnector(context, { "mateConnector" : csQuery });
}

// ══════════════════════════════════════════════════════════════════════════════
// DIRECTION EXTRACTION
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Extracts a representative unit direction vector from an entity for use in
 * angle measurement. Returns undefined when no direction is meaningful
 * (SOLID, VERTEX, NONE — these have position but no inherent axis).
 *
 * Resolution:
 *   MATE_CONNECTOR → axis selected by mcAxisType
 *                    PLUS_X → cSys.xAxis, PLUS_Y → yAxis(cSys), else → cSys.zAxis
 *   EDGE           → edge tangent at the closest point to otherPoint (midpoint fallback)
 *   WIRE           → tangent of first/closest edge in the wire body
 *   SHEET          → face normal of the closest face to otherPoint (center fallback)
 *   SOLID / VERTEX / NONE → undefined (no extractable direction)
 *
 * Note: the parameter type for otherPoint is intentionally unannotated because
 * it may be a Vector (with length units) or undefined when not available.
 *
 * @param context     {Context}
 * @param entityQuery {Query}
 * @param entityType  {BMEntityType}
 * @param otherPoint              — opposing reference point (Vector) or undefined
 * @param mcAxisType  {BMMCAxis}
 * @returns {Vector | undefined}  — unitless direction, or undefined if not applicable
 *
 * Ref: evaluate.fs — evEdgeTangentLine, evFaceTangentPlane, evMateConnector, evDistance
 *      coordSystem.fs — yAxis
 */
export function extractEntityDirection(context is Context, entityQuery is Query,
    entityType is BMEntityType, otherPoint, mcAxisType is BMMCAxis)
{
    // ── Mate connector: user-selected axis
    if (entityType == BMEntityType.MATE_CONNECTOR)
    {
        var cSys = evMateConnector(context, { "mateConnector" : entityQuery });
        if (mcAxisType == BMMCAxis.X_AXIS)
        {
            return cSys.xAxis;
        }
        else if (mcAxisType == BMMCAxis.Y_AXIS)
        {
            return yAxis(cSys);
        }
        else
        {
            // Z_AXIS default
            return cSys.zAxis;
        }
    }

    // ── Edge: tangent at closest point
    if (entityType == BMEntityType.EDGE)
    {
        var param = 0.5;
        if (otherPoint != undefined)
        {
            var dr = evDistance(context, {
                "side0" : otherPoint,
                "side1" : entityQuery,
                "arcLengthParameterization" : true
            });
            var p = dr.sides[1].parameter;
            if (p is number)
            {
                param = p;
            }
        }
        return evEdgeTangentLine(context, { "edge" : entityQuery, "parameter" : param }).direction;
    }

    // ── Wire: tangent of closest-to-otherPoint edge (first edge as fallback)
    if (entityType == BMEntityType.WIRE)
    {
        var edges = qOwnedByBody(entityQuery, EntityType.EDGE);
        var edgeQ = qNthElement(edges, 0); // default: first edge
        var param = 0.5;

        if (otherPoint != undefined)
        {
            var dr = evDistance(context, {
                "side0" : otherPoint,
                "side1" : edges,
                "arcLengthParameterization" : true
            });
            edgeQ = qNthElement(edges, dr.sides[1].index);
            var p = dr.sides[1].parameter;
            if (p is number)
            {
                param = p;
            }
        }
        return evEdgeTangentLine(context, { "edge" : edgeQ, "parameter" : param }).direction;
    }

    // ── Sheet: face normal at closest point to otherPoint
    if (entityType == BMEntityType.SHEET)
    {
        var faces = qOwnedByBody(entityQuery, EntityType.FACE);
        var faceQ = faces;
        var uv = vector(0.5, 0.5); // bbox-relative center

        if (otherPoint != undefined)
        {
            var dr = evDistance(context, { "side0" : otherPoint, "side1" : faces });
            faceQ = qNthElement(faces, dr.sides[1].index);
            var faceparam = dr.sides[1].parameter;
            // evDistance returns 2D UV vector for face parameters
            if (faceparam is Vector && size(faceparam) == 2)
            {
                uv = faceparam;
            }
        }
        return evFaceTangentPlane(context, { "face" : faceQ, "parameter" : uv }).normal;
    }

    // SOLID, VERTEX, NONE — no meaningful direction
    return undefined;
}

// ══════════════════════════════════════════════════════════════════════════════
// ANGLE MEASUREMENT
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Computes the unsigned angle between two entities using their extractable
 * direction vectors. Result is in [0, π/2] — smallest angle between the lines.
 *
 * Returns undefined if neither entity yields a direction (SOLID, VERTEX, NONE).
 * Uses try silent at call site for graceful fallback.
 *
 * Workflow:
 *   1. Get rough positions (centroid) for both entities as direction-extraction context
 *   2. Extract direction vectors via extractEntityDirection
 *   3. Compute acos(|dot(d1, d2)|), clamped for numerical safety
 *
 * @param context      {Context}
 * @param entity1      {Query}
 * @param entityType1  {BMEntityType}
 * @param entity2      {Query}
 * @param entityType2  {BMEntityType}
 * @param mc1AxisType  {BMMCAxis}  — MC axis for entity1
 * @param mc2AxisType  {BMMCAxis}  — MC axis for entity2
 * @returns {ValueWithUnits | undefined} — angle in radians
 *
 * Ref: evaluate.fs — evApproximateCentroid; mathUtils.fs — acos, dot, normalize
 */
export function measureAngleBetweenEntities(context is Context,
    entity1 is Query, entityType1 is BMEntityType,
    entity2 is Query, entityType2 is BMEntityType,
    mc1AxisType is BMMCAxis, mc2AxisType is BMMCAxis)
{
    // Rough positions to provide context for direction extraction
    var rough1 = evApproximateCentroid(context, { "entities" : entity1 });
    var rough2 = evApproximateCentroid(context, { "entities" : entity2 });

    var dir1 = try silent(extractEntityDirection(context, entity1, entityType1,
        rough2, mc1AxisType));
    var dir2 = try silent(extractEntityDirection(context, entity2, entityType2,
        rough1, mc2AxisType));

    if (dir1 == undefined || dir2 == undefined)
    {
        return undefined;
    }

    // Unsigned smallest angle between lines: acos(|d1·d2|), clamped to [0, 1]
    var cosAngle = abs(dot(normalize(dir1), normalize(dir2)));
    cosAngle = min(1.0, max(0.0, cosAngle));
    return acos(cosAngle) * radian;
}

// ══════════════════════════════════════════════════════════════════════════════
// EULER ANGLE EXTRACTION
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Computes ZYX Euler angles of the rotation from mc1's frame to mc2's frame.
 * Useful for visualizing relative orientation between two mate connectors.
 *
 * Convention: ZYX intrinsic — rotate about Z first, then new Y, then new X.
 * Returned angles are in radians.
 *
 * Algorithm:
 *   Build each MC's basis as column vectors {xAxis, yAxis, zAxis}.
 *   Express mc2's basis in mc1's frame via dot products → relative rotation matrix R.
 *   Extract ZYX Euler: ry = asin(-R[2][0]), rz = atan2(R[1][0], R[0][0]),
 *                       rx = atan2(R[2][1], R[2][2])
 *
 * @param context  {Context}
 * @param mc1Query {Query}  — first mate connector
 * @param mc2Query {Query}  — second mate connector
 * @returns {array} — [rx, ry, rz], each a ValueWithUnits in radians
 *
 * Ref: evaluate.fs — evMateConnector; coordSystem.fs — yAxis;
 *      mathUtils.fs — atan2, asin
 */
export function computeRelativeEuler(context is Context,
    mc1Query is Query, mc2Query is Query) returns array
{
    var cSys1 = evMateConnector(context, { "mateConnector" : mc1Query });
    var cSys2 = evMateConnector(context, { "mateConnector" : mc2Query });

    var y1 = yAxis(cSys1);
    var y2 = yAxis(cSys2);

    // R[i][j] = dot(mc2 basis vector i, mc1 basis vector j)
    // We need the 3x3 matrix row by row for ZYX extraction.
    // R col 0 = mc2.xAxis expressed in mc1 frame
    var r00 = dot(cSys2.xAxis, cSys1.xAxis);
    var r10 = dot(cSys2.xAxis, y1);
    var r20 = dot(cSys2.xAxis, cSys1.zAxis);

    // R col 1 = mc2.yAxis expressed in mc1 frame
    var r21 = dot(y2, cSys1.zAxis);

    // R col 2 = mc2.zAxis expressed in mc1 frame
    var r22 = dot(cSys2.zAxis, cSys1.zAxis);

    // ZYX Euler extraction
    // ry = asin(-r20), clamped for numerical safety
    var sinRy = min(1.0, max(-1.0, -r20));
    var ry = asin(sinRy) * radian;
    var rz = atan2(r10, r00) * radian;
    var rx = atan2(r21, r22) * radian;

    return [rx, ry, rz];
}

// ══════════════════════════════════════════════════════════════════════════════
// ALONG-EDGE ARC LENGTH
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Measures arc length along an edge between the two closest points to p1 and p2.
 *
 * Uses evDistance with arcLengthParameterization: true so that the returned
 * parameter t ∈ [0, 1] is proportional to arc length. The distance along the
 * edge between t1 and t2 is then |t2 - t1| * totalEdgeLength.
 *
 * This formula is exact: because arc-length parameterization makes t linear in
 * arc length, the fractional difference maps directly to a length difference.
 *
 * @param context   {Context}
 * @param edgeQuery {Query}   — a single edge
 * @param p1        {Vector}  — first measurement point (world space)
 * @param p2        {Vector}  — second measurement point (world space)
 * @returns {ValueWithUnits}  — arc length with length units
 *
 * Ref: evaluate.fs — evDistance (arcLengthParameterization), evLength
 */
export function measureAlongEdge(context is Context, edgeQuery is Query,
    p1 is Vector, p2 is Vector) returns ValueWithUnits
{
    var dr1 = evDistance(context, {
        "side0" : p1,
        "side1" : edgeQuery,
        "arcLengthParameterization" : true
    });
    var dr2 = evDistance(context, {
        "side0" : p2,
        "side1" : edgeQuery,
        "arcLengthParameterization" : true
    });

    var t1 = dr1.sides[1].parameter;
    var t2 = dr2.sides[1].parameter;

    // Edge returns a scalar parameter; guard against unexpected types
    if (!(t1 is number)) { t1 = 0.0; }
    if (!(t2 is number)) { t2 = 1.0; }

    var totalLength = evLength(context, { "entities" : edgeQuery });
    return abs(t2 - t1) * totalLength;
}

// ══════════════════════════════════════════════════════════════════════════════
// ALONG-FACE GEODESIC APPROXIMATION
// ══════════════════════════════════════════════════════════════════════════════

/** Number of polyline segments used to approximate the on-face path. */
const ALONG_FACE_SAMPLES = 20;

/**
 * Approximates the geodesic arc length along a face between the closest points
 * on the face to p1 and p2. Optionally creates a wire body along the path.
 *
 * Algorithm:
 *   1. Project p1 → face and p2 → face via evDistance to obtain UV parameters.
 *   2. Linearly interpolate in UV space over ALONG_FACE_SAMPLES steps.
 *   3. Evaluate the world-space position at each UV sample via evFaceTangentPlane.
 *   4. Sum chord distances between consecutive world-space positions.
 *   5. If keepWire: create a spline wire body through the sample points.
 *
 * Accuracy: UV-linear interpolation is exact on planar faces and a first-order
 * approximation on curved faces. Accuracy improves with ALONG_FACE_SAMPLES.
 * This is NOT a true geodesic — do not use for precision on highly curved faces.
 *
 * @param context   {Context}
 * @param id        {Id}
 * @param faceQuery {Query}   — a single non-mesh face
 * @param p1        {Vector}  — first measurement point (need not be on face)
 * @param p2        {Vector}  — second measurement point (need not be on face)
 * @param keepWire  {boolean} — if true, persists the path as a wire body
 * @returns {ValueWithUnits}  — approximated path length with length units
 *
 * Ref: evaluate.fs — evDistance, evFaceTangentPlane
 *      geomOperations.fs — opFitSpline (for keepWire)
 */
export function measureAlongFace(context is Context, id is Id, faceQuery is Query,
    p1 is Vector, p2 is Vector, keepWire is boolean) returns ValueWithUnits
{
    // ── 1. Project p1 and p2 onto face to get UV parameters
    // evDistance returns sides[1].parameter as a 2D Vector for faces
    var dr1 = evDistance(context, { "side0" : p1, "side1" : faceQuery });
    var dr2 = evDistance(context, { "side0" : p2, "side1" : faceQuery });

    var uv1 = dr1.sides[1].parameter;
    var uv2 = dr2.sides[1].parameter;

    // Guard: ensure 2D UV vectors (planes may return degenerate params)
    if (!(uv1 is Vector) || size(uv1) != 2) { uv1 = vector(0.25, 0.25); }
    if (!(uv2 is Vector) || size(uv2) != 2) { uv2 = vector(0.75, 0.75); }

    // ── 2. Sample world positions along the UV path
    var n = ALONG_FACE_SAMPLES;
    var samplePoints = [];

    for (var i = 0; i <= n; i += 1)
    {
        var t = i / n;
        var uvSample = uv1 * (1.0 - t) + uv2 * t;

        // Clamp to [0, 1] (face parameter bbox bounds)
        uvSample = vector(min(1.0, max(0.0, uvSample[0])),
                          min(1.0, max(0.0, uvSample[1])));

        var tangentPlane = try silent(evFaceTangentPlane(context,
            { "face" : faceQuery, "parameter" : uvSample }));

        if (tangentPlane != undefined)
        {
            samplePoints = append(samplePoints, tangentPlane.origin);
        }
    }

    // ── 3. Sum chord distances
    var totalLength = 0 * meter;
    for (var i = 1; i < size(samplePoints); i += 1)
    {
        totalLength += norm(samplePoints[i] - samplePoints[i - 1]);
    }

    // ── 4. Optionally persist the path as a wire body via opFitSpline
    // Ref: geomOperations.fs — opFitSpline takes {points: array of Vectors}
    if (keepWire && size(samplePoints) >= 2)
    {
        try
        {
            opFitSpline(context, id + "wire", { "points" : samplePoints });
        }
    }

    return totalLength;
}

// ══════════════════════════════════════════════════════════════════════════════
// OFF-ENTITY PROXIMITY WARNING
// ══════════════════════════════════════════════════════════════════════════════

/** Distance threshold above which a measurement point is considered "off" the along entity. */
const OFF_ENTITY_THRESHOLD = 0.0001 * meter; // 0.1 mm

/**
 * Reports a feature warning if either p1 or p2 is farther than
 * OFF_ENTITY_THRESHOLD from the "along" entity. Computation still proceeds —
 * the closest point on the entity is used regardless.
 *
 * @param context    {Context}
 * @param id         {Id}
 * @param alongQuery {Query}  — the selected edge or face
 * @param p1         {Vector}
 * @param p2         {Vector}
 *
 * Ref: evaluate.fs — evDistance; error.fs — reportFeatureWarning
 */
export function warnIfOffEntity(context is Context, id is Id, alongQuery is Query,
    p1 is Vector, p2 is Vector)
{
    var dist1 = evDistance(context, { "side0" : p1, "side1" : alongQuery }).distance;
    var dist2 = evDistance(context, { "side0" : p2, "side1" : alongQuery }).distance;

    if (dist1 > OFF_ENTITY_THRESHOLD || dist2 > OFF_ENTITY_THRESHOLD)
    {
        reportFeatureWarning(context, id,
            "One or more measurement points are not on the 'along' entity. "
            ~ "Closest points on the entity are used instead.");
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// VARIABLE PUBLISHING
// ══════════════════════════════════════════════════════════════════════════════

/**
 * Creates a context variable for one measurement result if the user enabled
 * saving and provided a non-empty name. Skips silently when either condition
 * is false. Validates the name first; reports a warning (not a hard error) on
 * invalid names so other pending variables can still be published.
 *
 * Pattern mirrors publishVariableValue() in variable.fs:825.
 *
 * @param context    {Context}
 * @param id         {Id}
 * @param shouldSave {boolean} — definition.saveXxxVar flag
 * @param varName    {string}  — definition.xxxVarName field
 * @param value                — measurement value to publish (any FS type)
 *
 * Ref: variable.fs — verifyVariableName, setVariable
 */
export function publishIfEnabled(context is Context, id is Id,
    shouldSave is boolean, varName is string, value)
{
    if (!shouldSave || varName == "")
    {
        return;
    }

    try
    {
        verifyVariableName(context, varName, "name");
        setVariable(context, varName, value);
    }
    catch (e)
    {
        // Downgrade to warning so remaining variables are still published
        reportFeatureWarning(context, id,
            "Could not create variable '" ~ varName ~ "': check that the name is a valid identifier.");
    }
}
