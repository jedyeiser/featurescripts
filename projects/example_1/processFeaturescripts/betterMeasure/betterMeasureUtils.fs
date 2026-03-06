FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * betterMeasureUtils.fs
 * Helper enums and functions for the betterMeasure feature.
 * All symbols exported for use by betterMeasure.fs.
 */

// ---------------------------------------------------------------------------
// Enums
// ---------------------------------------------------------------------------

/** Entity category set by editing logic into hidden entity1Type/entity2Type fields. */
export enum BMEntityType { NONE, SOLID, SHEET, WIRE, EDGE, VERTEX, MATE_CONNECTOR }

/** Sub-selection for SOLID body reference point. */
export enum BMSolidRef { COM, NEAREST_FACE, NEAREST_EDGE, NEAREST_VERTEX }

/** Sub-selection for SHEET body reference point. */
export enum BMSheetRef { COA, NEAREST_EDGE, NEAREST_VERTEX }

/**
 * Mate connector axis for ANGLE/VECTOR direction extraction.
 * Replaces MateConnectorAxisType — that enum has no PLUS_Z member,
 * so using it with a Z default causes a silent FS compile failure.
 */
export enum BMMCAxis { X_AXIS, Y_AXIS, Z_AXIS }

// ---------------------------------------------------------------------------
// getEntityBodyType
// ---------------------------------------------------------------------------

/**
 * Detects what category an entity belongs to and returns the corresponding
 * BMEntityType. Called from the editing logic to populate the hidden
 * entity1Type / entity2Type fields.
 *
 * Detection order matters: check MATE_CONNECTOR before WIRE because a
 * mate connector is internally a wire body and would match WIRE first.
 */
export function getEntityBodyType(context is Context, entityQuery is Query) returns BMEntityType
{
    // Empty or nothing selected
    if (isQueryEmpty(context, entityQuery))
    {
        return BMEntityType.NONE;
    }

    // Mate connector (must come before WIRE — MC is a wire body internally)
    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.BODY)->qBodyType(BodyType.MATE_CONNECTOR)))
    {
        return BMEntityType.MATE_CONNECTOR;
    }

    // Solid body
    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.BODY)->qBodyType(BodyType.SOLID)))
    {
        return BMEntityType.SOLID;
    }

    // Sheet body
    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.BODY)->qBodyType(BodyType.SHEET)))
    {
        return BMEntityType.SHEET;
    }

    // Wire body (non-MC)
    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.BODY)->qBodyType(BodyType.WIRE)))
    {
        return BMEntityType.WIRE;
    }

    // Edge
    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.EDGE)))
    {
        return BMEntityType.EDGE;
    }

    // Vertex
    if (!isQueryEmpty(context, qEntityFilter(entityQuery, EntityType.VERTEX)))
    {
        return BMEntityType.VERTEX;
    }

    return BMEntityType.NONE;
}

// ---------------------------------------------------------------------------
// resolveEntityPoint
// ---------------------------------------------------------------------------

/**
 * Two-pass reference point resolution for an entity.
 *
 * For NEAREST_* modes the otherPoint must be supplied so evDistance can find
 * the closest sub-entity to that point. On the first rough pass otherPoint
 * may be undefined; callers should do a second pass once both rough points
 * are known.
 *
 * Returns a 3-D position Vector (with length units).
 */
export function resolveEntityPoint(context is Context, entityQuery is Query,
    entityType is BMEntityType, solidRef is BMSolidRef, sheetRef is BMSheetRef,
    otherPoint) returns Vector
{
    if (entityType == BMEntityType.VERTEX)
    {
        return evVertexPoint(context, { "vertex" : entityQuery });
    }

    if (entityType == BMEntityType.MATE_CONNECTOR)
    {
        return evMateConnector(context, { "mateConnector" : entityQuery }).origin;
    }

    if (entityType == BMEntityType.EDGE)
    {
        // Use midpoint of edge when no other point available, closest point otherwise
        if (otherPoint == undefined)
        {
            var line = evEdgeTangentLine(context, { "edge" : entityQuery, "parameter" : 0.5 });
            return line.origin;
        }
        var dist = evDistance(context, { "side0" : otherPoint, "side1" : entityQuery });
        return dist.sides[1].point;
    }

    if (entityType == BMEntityType.SOLID)
    {
        if (solidRef == BMSolidRef.COM)
        {
            return evApproximateCentroid(context, { "entities" : entityQuery });
        }
        // NEAREST_FACE, NEAREST_EDGE, NEAREST_VERTEX — find closest sub-entity point
        var subFilter = EntityType.FACE;
        if (solidRef == BMSolidRef.NEAREST_EDGE)
        {
            subFilter = EntityType.EDGE;
        }
        else if (solidRef == BMSolidRef.NEAREST_VERTEX)
        {
            subFilter = EntityType.VERTEX;
        }
        var subQuery = qOwnedByBody(entityQuery, subFilter);
        var refPoint = (otherPoint != undefined) ? otherPoint : evApproximateCentroid(context, { "entities" : entityQuery });
        var dist = evDistance(context, { "side0" : refPoint, "side1" : subQuery });
        return dist.sides[1].point;
    }

    if (entityType == BMEntityType.SHEET)
    {
        if (sheetRef == BMSheetRef.COA)
        {
            return evApproximateCentroid(context, { "entities" : entityQuery });
        }
        var subFilter = EntityType.EDGE;
        if (sheetRef == BMSheetRef.NEAREST_VERTEX)
        {
            subFilter = EntityType.VERTEX;
        }
        var subQuery = qOwnedByBody(entityQuery, subFilter);
        var refPoint = (otherPoint != undefined) ? otherPoint : evApproximateCentroid(context, { "entities" : entityQuery });
        var dist = evDistance(context, { "side0" : refPoint, "side1" : subQuery });
        return dist.sides[1].point;
    }

    if (entityType == BMEntityType.WIRE)
    {
        // Use midpoint of wire's first edge
        var edges = qOwnedByBody(entityQuery, EntityType.EDGE);
        if (!isQueryEmpty(context, edges))
        {
            if (otherPoint == undefined)
            {
                var line = evEdgeTangentLine(context, { "edge" : qNthElement(edges, 0), "parameter" : 0.5 });
                return line.origin;
            }
            var dist = evDistance(context, { "side0" : otherPoint, "side1" : edges });
            return dist.sides[1].point;
        }
    }

    // Fallback: use evDistance against whatever the query is
    var fallbackRef = (otherPoint != undefined) ? otherPoint : vector(0, 0, 0) * meter;
    var dist = evDistance(context, { "side0" : fallbackRef, "side1" : entityQuery });
    return dist.sides[1].point;
}

// ---------------------------------------------------------------------------
// resolveCoordFrame
// ---------------------------------------------------------------------------

/**
 * Returns a CoordSystem for the requested coordinate frame.
 * WORLD returns the world coordinate system constant.
 * MATE_CONNECTOR evaluates the supplied MC query.
 */
export function resolveCoordFrame(context is Context, coordSystemType is BMCoordSystem, csQuery is Query) returns CoordSystem
{
    if (coordSystemType == BMCoordSystem.MATE_CONNECTOR)
    {
        if (!isQueryEmpty(context, csQuery))
        {
            return evMateConnector(context, { "mateConnector" : csQuery });
        }
    }
    return WORLD_COORD_SYSTEM;
}

// ---------------------------------------------------------------------------
// extractEntityDirection
// ---------------------------------------------------------------------------

/**
 * Extracts a unit direction vector from an entity for use in angle/vector
 * measurements.
 *
 * - MATE_CONNECTOR: returns X/Y/Z axis of the MC frame per mcAxisType
 * - EDGE:           returns tangent at the parameter closest to otherPoint
 * - SHEET:          returns face normal at the point closest to otherPoint
 * - Other types:    returns undefined
 */
export function extractEntityDirection(context is Context, entityQuery is Query,
    entityType is BMEntityType, otherPoint, mcAxisType is BMMCAxis)
{
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
            return cSys.zAxis;
        }
    }

    if (entityType == BMEntityType.EDGE)
    {
        var param = 0.5;
        if (otherPoint != undefined)
        {
            var dist = evDistance(context, { "side0" : otherPoint, "side1" : entityQuery });
            param = dist.sides[1].parameter[0];
        }
        var line = evEdgeTangentLine(context, { "edge" : entityQuery, "parameter" : param });
        return line.direction;
    }

    if (entityType == BMEntityType.SHEET)
    {
        var faceQ = qOwnedByBody(entityQuery, EntityType.FACE);
        if (isQueryEmpty(context, faceQ))
        {
            faceQ = entityQuery;
        }
        var uvParam = vector(0.5, 0.5);
        if (otherPoint != undefined)
        {
            var dist = evDistance(context, { "side0" : otherPoint, "side1" : faceQ });
            uvParam = dist.sides[1].parameter;
        }
        var plane = evFaceTangentPlane(context, { "face" : qNthElement(faceQ, 0), "parameter" : uvParam });
        return plane.normal;
    }

    return undefined;
}

// ---------------------------------------------------------------------------
// measureAngleBetweenEntities
// ---------------------------------------------------------------------------

/**
 * Computes the unsigned angle in [0, π/2] between the direction vectors of
 * two entities. Returns undefined if either direction cannot be extracted.
 */
export function measureAngleBetweenEntities(context is Context,
    entity1 is Query, type1 is BMEntityType,
    entity2 is Query, type2 is BMEntityType,
    mc1Axis is BMMCAxis, mc2Axis is BMMCAxis)
{
    var p1 = try silent(resolveEntityPoint(context, entity1, type1, BMSolidRef.COM, BMSheetRef.COA, undefined));
    var p2 = try silent(resolveEntityPoint(context, entity2, type2, BMSolidRef.COM, BMSheetRef.COA, p1));

    var d1 = try silent(extractEntityDirection(context, entity1, type1, p2, mc1Axis));
    var d2 = try silent(extractEntityDirection(context, entity2, type2, p1, mc2Axis));

    if (d1 == undefined || d2 == undefined)
    {
        return undefined;
    }

    var cosAngle = clamp(abs(dot(d1, d2)), 0, 1);
    return acos(cosAngle) * radian;
}

// ---------------------------------------------------------------------------
// computeRelativeEuler
// ---------------------------------------------------------------------------

/**
 * Computes ZYX Euler angles (in radians) from mc1 frame to mc2 frame.
 * Returns an array [eulerX, eulerY, eulerZ] as ValueWithUnits (radians).
 */
export function computeRelativeEuler(context is Context, mc1Query is Query, mc2Query is Query) returns array
{
    var cs1 = evMateConnector(context, { "mateConnector" : mc1Query });
    var cs2 = evMateConnector(context, { "mateConnector" : mc2Query });

    // Build relative rotation matrix R = R1^T * R2
    var x1 = cs1.xAxis;
    var y1 = yAxis(cs1);
    var z1 = cs1.zAxis;
    var x2 = cs2.xAxis;
    var y2 = yAxis(cs2);
    var z2 = cs2.zAxis;

    // R_rel columns expressed in mc1 frame
    var rx = vector(dot(x1, x2), dot(y1, x2), dot(z1, x2));
    var ry = vector(dot(x1, y2), dot(y1, y2), dot(z1, y2));
    var rz = vector(dot(x1, z2), dot(y1, z2), dot(z1, z2));

    // ZYX Euler: R = Rz * Ry * Rx
    // ry[0] = -sin(eulerY), rz[0] = cos(eulerY)*sin(eulerZ), rx[0] = cos(eulerY)*cos(eulerX) ... etc.
    var eulerY = asin(clamp(-rx[2], -1, 1));
    var cosY = cos(eulerY);

    var eulerX = 0;
    var eulerZ = 0;
    if (abs(cosY) > 1e-6)
    {
        eulerX = atan2(ry[2], rz[2]);
        eulerZ = atan2(rx[1], rx[0]);
    }
    else
    {
        // Gimbal lock
        eulerX = atan2(-rz[1], ry[1]);
        eulerZ = 0;
    }

    return [eulerX * radian, eulerY * radian, eulerZ * radian];
}

// ---------------------------------------------------------------------------
// measureAlongEdge
// ---------------------------------------------------------------------------

/**
 * Measures the arc length along an edge between the two points p1 and p2
 * (which should be near the edge). Finds closest parameters to each point,
 * then integrates arc length over that parameter span.
 */
export function measureAlongEdge(context is Context, edgeQuery is Query,
    p1 is Vector, p2 is Vector) returns ValueWithUnits
{
    var dist1 = evDistance(context, { "side0" : p1, "side1" : edgeQuery });
    var dist2 = evDistance(context, { "side0" : p2, "side1" : edgeQuery });

    var t1 = dist1.sides[1].parameter[0];
    var t2 = dist2.sides[1].parameter[0];

    if (t1 > t2)
    {
        var tmp = t1;
        t1 = t2;
        t2 = tmp;
    }

    // Sample chord lengths over parameter span
    var nSteps = 20;
    var totalLength = 0 * meter;
    var prevPt = evEdgeTangentLine(context, { "edge" : edgeQuery, "parameter" : t1 }).origin;

    for (var i = 1; i <= nSteps; i += 1)
    {
        var t = t1 + (t2 - t1) * i / nSteps;
        var pt = evEdgeTangentLine(context, { "edge" : edgeQuery, "parameter" : t }).origin;
        totalLength = totalLength + norm(pt - prevPt);
        prevPt = pt;
    }

    return totalLength;
}

// ---------------------------------------------------------------------------
// measureAlongFace
// ---------------------------------------------------------------------------

/**
 * Approximates the geodesic distance along a face between p1 and p2 using a
 * 20-step UV-space polyline. Optionally creates a spline wire on the face
 * (keepWire = true).
 *
 * Returns the approximate arc length as ValueWithUnits.
 */
export function measureAlongFace(context is Context, id is Id, faceQuery is Query,
    p1 is Vector, p2 is Vector, keepWire is boolean) returns ValueWithUnits
{
    var faceOnlyQ = qEntityFilter(faceQuery, EntityType.FACE);
    if (isQueryEmpty(context, faceOnlyQ))
    {
        faceOnlyQ = faceQuery;
    }
    var face = qNthElement(faceOnlyQ, 0);

    var dist1 = evDistance(context, { "side0" : p1, "side1" : face });
    var dist2 = evDistance(context, { "side0" : p2, "side1" : face });

    var uv1 = dist1.sides[1].parameter;
    var uv2 = dist2.sides[1].parameter;

    var nSteps = 20;
    var pts = [];
    var totalLength = 0 * meter;
    var prevPt = evFaceTangentPlane(context, { "face" : face, "parameter" : uv1 }).origin;
    pts = append(pts, prevPt);

    for (var i = 1; i <= nSteps; i += 1)
    {
        var uv = uv1 + (uv2 - uv1) * i / nSteps;
        var tp = evFaceTangentPlane(context, { "face" : face, "parameter" : uv });
        var pt = tp.origin;
        totalLength = totalLength + norm(pt - prevPt);
        prevPt = pt;
        pts = append(pts, pt);
    }

    if (keepWire)
    {
        try silent(opFitSpline(context, id + "measureWire", {
            "points" : pts
        }));
    }

    return totalLength;
}

// ---------------------------------------------------------------------------
// warnIfOffEntity
// ---------------------------------------------------------------------------

/**
 * Issues a reportFeatureWarning if either p1 or p2 is more than 0.1 mm away
 * from the given query entity (edge or face). Used to alert the user when
 * their entity selection does not pass through the measurement points.
 */
export function warnIfOffEntity(context is Context, id is Id, alongQuery is Query,
    p1 is Vector, p2 is Vector)
{
    var threshold = 0.0001 * meter; // 0.1 mm

    var dist1 = try silent(evDistance(context, { "side0" : p1, "side1" : alongQuery }));
    var dist2 = try silent(evDistance(context, { "side0" : p2, "side1" : alongQuery }));

    if (dist1 != undefined && dist1.distance > threshold)
    {
        reportFeatureWarning(context, id, "Entity 1 is not on the 'along' edge/face. Distance measured to nearest point.");
    }
    if (dist2 != undefined && dist2.distance > threshold)
    {
        reportFeatureWarning(context, id, "Entity 2 is not on the 'along' edge/face. Distance measured to nearest point.");
    }
}

// ---------------------------------------------------------------------------
// publishIfEnabled
// ---------------------------------------------------------------------------

/**
 * If shouldSave is true and varName is a non-empty string, validates the
 * variable name and publishes value as a context variable. Issues a
 * reportFeatureWarning if the name is invalid rather than throwing.
 */
export function publishIfEnabled(context is Context, id is Id, shouldSave is boolean,
    varName is string, value)
{
    if (!shouldSave)
    {
        return;
    }
    if (varName == "")
    {
        reportFeatureWarning(context, id, "Variable name is empty — value not saved.");
        return;
    }
    try
    {
        verifyVariableName(context, varName);
        setVariable(context, varName, value);
    }
    catch
    {
        reportFeatureWarning(context, id, "Invalid variable name '" ~ varName ~ "' — value not saved.");
    }
}
