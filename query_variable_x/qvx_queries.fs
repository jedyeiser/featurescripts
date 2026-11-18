FeatureScript 3083;
/**
 * Query Variable X: shared query helpers.
 *
 * The enums the source and filter stages share, reference resolution (a face, edge, vertex or mate connector all
 * work as a plane, line, point or direction), and the positional, size, closest-to, direction and between logic.
 * Part of Query Variable X, which builds on Query Variable+ by Derek Van Allen (positional and size comparison
 * logic originate there).
 */
export import(path : "onshape/std/query.fs", version : "3083.0");
export import(path : "onshape/std/edgeconvexitytype.gen.fs", version : "3083.0");
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/debug.fs", version : "3083.0");
import(path : "onshape/std/feature.fs", version : "3083.0");
import(path : "onshape/std/featureList.fs", version : "3083.0");
import(path : "onshape/std/evaluate.fs", version : "3083.0");
import(path : "onshape/std/string.fs", version : "3083.0");
import(path : "onshape/std/containers.fs", version : "3083.0");
import(path : "onshape/std/error.fs", version : "3083.0");

/**
 * Specifies the topological type of a body, similar to BodyType, with annotations.
 * @seealso [BodyType]
 */
export enum BodyTypeOptions
{
    annotation { "Name" : "Part" }
    SOLID,
    annotation { "Name" : "Surface" }
    SHEET,
    annotation { "Name" : "Curve" }
    WIRE,
    annotation { "Name" : "Point" }
    POINT,
    annotation { "Name" : "Mate connector" }
    MATE_CONNECTOR,
    annotation { "Name" : "Composite part" }
    COMPOSITE
}

/**
 * Defines the type of size-based comparison to perform on a set of entities.
 */
export enum SizeComparisonType
{
    annotation { "Name" : "Largest" }
    LARGEST,
    annotation { "Name" : "Smallest" }
    SMALLEST,
    annotation { "Name" : "Larger than selection" }
    LARGER_THAN_SELECTION,
    annotation { "Name" : "Smaller than selection" }
    SMALLER_THAN_SELECTION,
    annotation { "Name" : "Equal to selection" }
    EQUAL_TO_SELECTION
}

/**
 * Defines positional or directional queries that relate entities to reference geometry.
 */
export enum PositionalDirectionalType
{
    annotation { "Name" : "Plane normal" }
    PLANE_NORMAL,
    annotation { "Name" : "Intersects line" }
    INTERSECTS_LINE,
    annotation { "Name" : "Intersects plane" }
    INTERSECTS_PLANE,
    annotation { "Name" : "Intersects ball" }
    INTERSECTS_BALL,
    annotation { "Name" : "Contains point" }
    CONTAINS_POINT,
    annotation { "Name" : "Closest to" }
    CLOSEST_TO,
    annotation { "Name" : "Farthest along direction" }
    FARTHEST_ALONG,
    annotation { "Name" : "Coincides with plane" }
    COINCIDES_WITH_PLANE,
    annotation { "Name" : "Plane parallel to direction" }
    PLANE_PARALLEL_DIRECTION,
    annotation { "Name" : "Face parallel to direction" }
    FACE_PARALLEL_DIRECTION,
    annotation { "Name" : "In front of plane" }
    IN_FRONT_OF_PLANE
}

/**
 * Direction filter: match entities parallel to the reference, or perpendicular to it.
 * Parallel means parallel or anti-parallel; the sign of a direction is never significant.
 */
export enum DirectionRelation
{
    annotation { "Name" : "Parallel" }
    PARALLEL,
    annotation { "Name" : "Perpendicular" }
    PERPENDICULAR
}

/**
 * Which direction a face contributes to the Direction filter.
 */
export enum FaceDirectionUse
{
    annotation { "Name" : "Normal (planar faces)" }
    NORMAL,
    annotation { "Name" : "Axis (cylinders and cones)" }
    AXIS
}

/**
 * Which direction a curved edge (B-spline, arc, ...) contributes to the Direction filter. Straight edges always
 * use their own direction.
 */
export enum CurveDirectionMode
{
    annotation { "Name" : "Chord (start to end)" }
    CHORD,
    annotation { "Name" : "Start tangent" }
    START_TANGENT,
    annotation { "Name" : "End tangent" }
    END_TANGENT,
    annotation { "Name" : "Either end tangent" }
    EITHER_END_TANGENT
}

/**
 * How the Between filter decides that an entity lies between its two bounds.
 */
export enum BetweenMode
{
    annotation { "Name" : "Fully between" }
    FULLY_BETWEEN,
    annotation { "Name" : "Overlaps" }
    OVERLAPS,
    annotation { "Name" : "Center between" }
    CENTER_BETWEEN
}

/**
 * Ensures that all entities in a query share a single size-comparable entity type.
 * Returns the detected entity type and a filtered query containing only that type.
 */
export function enforceUniformSizeEntityType(context is Context, entities is Query, errorField is string)
{
    const supportedTypes = [EntityType.BODY, EntityType.FACE, EntityType.EDGE];
    var presentTypes = [];

    for (var supportedType in supportedTypes)
    {
        const concreteType = supportedType as EntityType;
        if (!isQueryEmpty(context, qEntityFilter(entities, concreteType)))
        {
            presentTypes = append(presentTypes, concreteType);
        }
    }

    if (size(presentTypes) != 1)
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    const selectedType = presentTypes[0] as EntityType;
    return { "entityType" : selectedType, "filteredQuery" : qEntityFilter(entities, selectedType) };
}

/**
 * Measures a single entity's size using volume, area, or length depending on its type.
 */
export function measureEntitySize(context is Context, entity is Query, entityType is EntityType)
{
    if (entityType == EntityType.BODY)
    {
        return evVolume(context, { "entities" : entity });
    }

    if (entityType == EntityType.FACE)
    {
        return evArea(context, { "entities" : entity });
    }

    if (entityType == EntityType.EDGE)
    {
        return evLength(context, { "entities" : entity });
    }

    return 0 * meter;
}

/**
 * Builds a size-comparison query using either extremum queries or explicit size filtering against a reference selection.
 */
export function sizeComparisonSelection(context is Context, definition is map) returns Query
{
    const comparisonType = definition.sizeComparisonType as SizeComparisonType;
    const comparisonEntities = definition.sizeComparisonEntities as Query;
    const allowEqualSize = definition.sizeComparisonAllowEqual == true;

    const candidateSelection = enforceUniformSizeEntityType(context, comparisonEntities, "sizeComparisonEntities");

    if (comparisonType == SizeComparisonType.LARGEST)
    {
        return qLargest(candidateSelection.filteredQuery);
    }

    if (comparisonType == SizeComparisonType.SMALLEST)
    {
        return qSmallest(candidateSelection.filteredQuery);
    }

    const referenceEntities = definition.sizeComparisonReference as Query;
    const referenceSelection = enforceUniformSizeEntityType(context, referenceEntities, "sizeComparisonReference");

    if (candidateSelection.entityType != referenceSelection.entityType)
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, ["sizeComparisonReference"]);
    }

    const filteredCandidates = candidateSelection.filteredQuery;
    const filteredReference = referenceSelection.filteredQuery;

    const referenceEntitiesArray = evaluateQuery(context, filteredReference);
    if (size(referenceEntitiesArray) != 1)
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, ["sizeComparisonReference"]);
    }

    const referenceSize = measureEntitySize(context, qUnion([referenceEntitiesArray[0]]), referenceSelection.entityType);
    var qualifyingEntities = [];
    for (var entity in evaluateQuery(context, filteredCandidates))
    {
        const entitySize = measureEntitySize(context, qUnion([entity]), candidateSelection.entityType);
        const sizeDifference = entitySize - referenceSize;
        const isEqual = tolerantEquals(entitySize, referenceSize);
        const isLarger = sizeDifference > 0 && !isEqual;
        const isSmaller = sizeDifference < 0 && !isEqual;

        if ((comparisonType == SizeComparisonType.LARGER_THAN_SELECTION && (isLarger || (allowEqualSize && isEqual)))
            || (comparisonType == SizeComparisonType.SMALLER_THAN_SELECTION && (isSmaller || (allowEqualSize && isEqual)))
            || (comparisonType == SizeComparisonType.EQUAL_TO_SELECTION && isEqual))
        {
            qualifyingEntities = append(qualifyingEntities, entity);
        }
    }

    return size(qualifyingEntities) == 0 ? qNothing() : qUnion(qualifyingEntities);
}

/**
 * Reads a plane from a face query, failing with a clear regen error when no reference is provided.
 */
export function evaluatePlaneReference(context is Context, planeQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, planeQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return qvxPlane(context, planeQuery);
}

/**
 * Reads a line from an edge query, validating that the selection is present.
 */
export function evaluateLineReference(context is Context, lineQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, lineQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return qvxLine(context, lineQuery);
}

/**
 * Reads a point from a vertex query, validating that the selection is present.
 */
export function evaluatePointReference(context is Context, pointQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, pointQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return qvxPoint(context, pointQuery);
}

/**
 * Extracts a direction vector from the provided reference, failing on invalid inputs.
 */
export function evaluateDirectionReference(context is Context, directionQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, directionQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return qvxReferenceDirection(context, directionQuery, CurveDirectionMode.CHORD);
}

/**
 * Builds positional and directional queries that relate a seed selection to reference geometry.
 */
export function positionalDirectionalSelection(context is Context, definition is map) returns Query
{
    const positionalType = definition.positionalMetricType as PositionalDirectionalType;
    const candidateEntities = definition.positionalEntities as Query;

    if (positionalType == PositionalDirectionalType.PLANE_NORMAL
        || positionalType == PositionalDirectionalType.INTERSECTS_PLANE
        || positionalType == PositionalDirectionalType.COINCIDES_WITH_PLANE
        || positionalType == PositionalDirectionalType.IN_FRONT_OF_PLANE)
    {
        const referencePlane = evaluatePlaneReference(context, definition.positionalPlane as Query, "positionalPlane");
        if (positionalType == PositionalDirectionalType.PLANE_NORMAL)
        {
            return qParallelPlanes(candidateEntities, referencePlane);
        }

        if (positionalType == PositionalDirectionalType.INTERSECTS_PLANE)
        {
            return qIntersectsPlane(candidateEntities, referencePlane);
        }

        if (positionalType == PositionalDirectionalType.COINCIDES_WITH_PLANE)
        {
            return qCoincidesWithPlane(candidateEntities, referencePlane);
        }

        if (positionalType == PositionalDirectionalType.IN_FRONT_OF_PLANE)
        {
            return qInFrontOfPlane(candidateEntities, referencePlane);
        }

        throw regenError(ErrorStringEnum.INVALID_INPUT, ["positionalMetricType"]);
    }

    if (positionalType == PositionalDirectionalType.INTERSECTS_LINE)
    {
        const referenceLine = evaluateLineReference(context, definition.positionalLine as Query, "positionalLine");
        return qIntersectsLine(candidateEntities, referenceLine);
    }

    if (positionalType == PositionalDirectionalType.CLOSEST_TO)
    {
        return qvxClosestTo(context, candidateEntities, definition.positionalClosestRef as Query);
    }

    if (positionalType == PositionalDirectionalType.CONTAINS_POINT
        || positionalType == PositionalDirectionalType.INTERSECTS_BALL)
    {
        const referencePoint = evaluatePointReference(context, definition.positionalVertex as Query, "positionalVertex");
        if (positionalType == PositionalDirectionalType.CONTAINS_POINT)
        {
            return qContainsPoint(candidateEntities, referencePoint);
        }

        const radius = definition.positionalRadius as ValueWithUnits;
        return qWithinRadius(candidateEntities, referencePoint, radius);
    }

    const referenceDirection = evaluateDirectionReference(context, definition.positionalDirection as Query, "positionalDirection");
    if (positionalType == PositionalDirectionalType.FARTHEST_ALONG)
    {
        return qFarthestAlong(candidateEntities, referenceDirection);
    }

    if (positionalType == PositionalDirectionalType.PLANE_PARALLEL_DIRECTION)
    {
        return qPlanesParallelToDirection(candidateEntities, referenceDirection);
    }

    return qFacesParallelToDirection(candidateEntities, referenceDirection);
}

/**
 * The mate connector body inside `q`, or an empty query when `q` holds no mate connector. A mate connector
 * selection may arrive as the connector body or as a sub-entity of it (correction 44), so look at both.
 */
export function qvxMateConnector(context is Context, q is Query) returns Query
{
    return qBodyType(qUnion([q, qOwnerBody(q)]), BodyType.MATE_CONNECTOR);
}

export function qvxMateConnectorFrame(context is Context, q is Query) returns CoordSystem
{
    return evMateConnector(context, { "mateConnector" : qNthElement(qvxMateConnector(context, q), 0) });
}

/**
 * Plane of a planar face (its normal) or of a mate connector (origin, Z axis).
 */
export function qvxPlane(context is Context, q is Query) returns Plane
{
    if (!isQueryEmpty(context, qvxMateConnector(context, q)))
    {
        const cs = qvxMateConnectorFrame(context, q);
        return plane(cs.origin, cs.zAxis, cs.xAxis);
    }
    if (isQueryEmpty(context, qGeometry(q, GeometryType.PLANE)))
    {
        throw regenError("The reference must be a planar face or a mate connector");
    }
    return evPlane(context, { "face" : q });
}

/**
 * Line of a straight edge or the Z axis of a mate connector.
 */
export function qvxLine(context is Context, q is Query) returns Line
{
    if (!isQueryEmpty(context, qvxMateConnector(context, q)))
    {
        const cs = qvxMateConnectorFrame(context, q);
        return line(cs.origin, cs.zAxis);
    }
    if (isQueryEmpty(context, qGeometry(q, GeometryType.LINE)))
    {
        throw regenError("The reference must be a straight edge or a mate connector");
    }
    return evLine(context, { "edge" : q });
}

/**
 * Point of a vertex or the origin of a mate connector.
 */
export function qvxPoint(context is Context, q is Query) returns Vector
{
    if (!isQueryEmpty(context, qvxMateConnector(context, q)))
    {
        return qvxMateConnectorFrame(context, q).origin;
    }
    if (isQueryEmpty(context, qEntityFilter(q, EntityType.VERTEX)))
    {
        throw regenError("The reference must be a vertex or a mate connector");
    }
    return evVertexPoint(context, { "vertex" : q });
}

/**
 * Directions an edge offers for the Direction filter: its own direction when straight, otherwise per `curveMode`.
 */
export function qvxEdgeDirections(context is Context, edge is Query, curveMode is CurveDirectionMode) returns array
{
    if (!isQueryEmpty(context, qGeometry(edge, GeometryType.LINE)))
    {
        return [evLine(context, { "edge" : edge }).direction];
    }

    const startLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0, "arcLengthParameterization" : false });
    const endLine = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1, "arcLengthParameterization" : false });
    if (curveMode == CurveDirectionMode.START_TANGENT)
    {
        return [startLine.direction];
    }
    if (curveMode == CurveDirectionMode.END_TANGENT)
    {
        return [endLine.direction];
    }
    if (curveMode == CurveDirectionMode.EITHER_END_TANGENT)
    {
        return [startLine.direction, endLine.direction];
    }

    const chord = endLine.origin - startLine.origin;
    const chordLength = norm(chord);
    if (chordLength < TOLERANCE.zeroLength * meter)
    {
        return [];
    }
    return [chord / chordLength];
}

/**
 * Directions one entity offers for the Direction filter. Entities with no direction (vertices, bodies,
 * non-planar faces under "Normal") offer none and so never match.
 */
export function qvxCandidateDirections(context is Context, entity is Query, faceUse is FaceDirectionUse, curveMode is CurveDirectionMode) returns array
{
    if (!isQueryEmpty(context, qvxMateConnector(context, entity)))
    {
        return [qvxMateConnectorFrame(context, entity).zAxis];
    }
    if (!isQueryEmpty(context, qEntityFilter(entity, EntityType.FACE)))
    {
        if (faceUse == FaceDirectionUse.NORMAL)
        {
            if (!isQueryEmpty(context, qGeometry(entity, GeometryType.PLANE)))
            {
                return [evPlane(context, { "face" : entity }).normal];
            }
            return [];
        }
        if (!isQueryEmpty(context, qUnion([qGeometry(entity, GeometryType.CYLINDER), qGeometry(entity, GeometryType.CONE)])))
        {
            return [evAxis(context, { "axis" : entity }).direction];
        }
        return [];
    }
    if (!isQueryEmpty(context, qEntityFilter(entity, EntityType.EDGE)))
    {
        return qvxEdgeDirections(context, entity, curveMode);
    }
    return [];
}

/**
 * The single direction of a reference selection. A cylindrical face gives its axis, a planar face its normal,
 * a mate connector its Z axis, a straight edge its direction, a curved edge its chord or tangent.
 */
export function qvxReferenceDirection(context is Context, q is Query, curveMode is CurveDirectionMode) returns Vector
{
    const entities = evaluateQuery(context, q);
    if (size(entities) == 0)
    {
        throw regenError("Select a reference direction");
    }
    var dirs = qvxCandidateDirections(context, entities[0], FaceDirectionUse.NORMAL, curveMode);
    if (size(dirs) == 0)
    {
        dirs = qvxCandidateDirections(context, entities[0], FaceDirectionUse.AXIS, curveMode);
    }
    if (size(dirs) == 0)
    {
        throw regenError("A direction cannot be taken from the reference selection");
    }
    return normalize(dirs[0]);
}

/**
 * Whether direction `dir` is parallel (or perpendicular) to `reference` within `tolRad` radians. The sign is
 * ignored. With `projNormal`, both vectors are first flattened into the plane with that normal.
 */
export function qvxDirectionsMatch(dir is Vector, reference is Vector, projNormal, relation is DirectionRelation, tolRad is number) returns boolean
{
    var a = dir;
    var b = reference;
    if (projNormal != undefined)
    {
        a = a - projNormal * dot(a, projNormal);
        b = b - projNormal * dot(b, projNormal);
    }
    const normA = norm(a);
    const normB = norm(b);
    if (normA < 1e-9 || normB < 1e-9)
    {
        return false;
    }
    const cosine = abs(dot(a, b)) / (normA * normB);
    if (relation == DirectionRelation.PARALLEL)
    {
        return cosine >= cos(tolRad * radian);
    }
    return cosine <= sin(tolRad * radian);
}

/**
 * The entities of `candidates` closest to `reference`. The reference may be a vertex or mate connector (a point),
 * or an edge, face or body (measured with evDistance, so the nearest points on both sides are used). Ties within
 * TOLERANCE.zeroLength are all returned. The reference itself is never a result, and for a body reference neither
 * are the entities it owns (they would all be at distance zero).
 */
export function qvxClosestTo(context is Context, candidates is Query, reference is Query) returns Query
{
    if (isQueryEmpty(context, reference))
    {
        throw regenError("Select what the result should be closest to");
    }

    var excluded = reference;
    if (!isQueryEmpty(context, qEntityFilter(reference, EntityType.BODY)))
    {
        excluded = qUnion([reference, qOwnedByBody(reference)]);
    }
    const pool = evaluateQuery(context, qSubtraction(candidates, excluded));
    if (size(pool) == 0)
    {
        return qNothing();
    }

    const referenceIsPoint = !isQueryEmpty(context, qvxMateConnector(context, reference))
        || !isQueryEmpty(context, qEntityFilter(reference, EntityType.VERTEX));
    var side0 = reference;
    if (referenceIsPoint)
    {
        side0 = qvxPoint(context, reference);
    }

    var distances = [];
    for (var entity in pool)
    {
        distances = append(distances, evDistance(context, { "side0" : side0, "side1" : entity }).distance);
    }
    var nearest = distances[0];
    for (var d in distances)
    {
        nearest = min(nearest, d);
    }

    var hits = [];
    for (var i = 0; i < size(pool); i += 1)
    {
        if (distances[i] <= nearest + TOLERANCE.zeroLength * meter)
        {
            hits = append(hits, pool[i]);
        }
    }
    return qUnion(hits);
}

/**
 * Reduce a Between bound to {isPlane, origin, normal}. Planar faces and mate connectors are planes (a mate
 * connector uses its origin and Z axis); a vertex is a point.
 */
export function qvxBound(context is Context, q is Query) returns map
{
    if (!isQueryEmpty(context, qvxMateConnector(context, q)))
    {
        const cs = qvxMateConnectorFrame(context, q);
        return { "isPlane" : true, "origin" : cs.origin, "normal" : cs.zAxis };
    }
    if (!isQueryEmpty(context, qEntityFilter(q, EntityType.VERTEX)))
    {
        return { "isPlane" : false, "origin" : evVertexPoint(context, { "vertex" : q }) };
    }
    if (!isQueryEmpty(context, qGeometry(q, GeometryType.PLANE)))
    {
        const pl = evPlane(context, { "face" : q });
        return { "isPlane" : true, "origin" : pl.origin, "normal" : pl.normal };
    }
    throw regenError("Between bounds must be planar faces, vertices or mate connectors");
}

export function qvxBetweenAxis(boundA is map, boundB is map) returns Vector
{
    if (boundA.isPlane && boundB.isPlane)
    {
        if (!parallelVectors(boundA.normal, boundB.normal))
        {
            throw regenError("Between bounds that are planes must be parallel");
        }
        return boundA.normal;
    }
    if (boundA.isPlane)
    {
        return boundA.normal;
    }
    if (boundB.isPlane)
    {
        return boundB.normal;
    }
    const delta = boundB.origin - boundA.origin;
    const gap = norm(delta);
    if (gap < TOLERANCE.zeroLength * meter)
    {
        throw regenError("The two Between bounds are at the same point");
    }
    return delta / gap;
}

export function qvxBodyTypeQuery(current is Query, bodyType is BodyTypeOptions) returns Query
{
    if (bodyType == BodyTypeOptions.SOLID)
    {
        return qBodyType(current, BodyType.SOLID);
    }
    if (bodyType == BodyTypeOptions.SHEET)
    {
        return qBodyType(current, BodyType.SHEET);
    }
    if (bodyType == BodyTypeOptions.WIRE)
    {
        return qBodyType(current, BodyType.WIRE);
    }
    if (bodyType == BodyTypeOptions.POINT)
    {
        return qBodyType(current, BodyType.POINT);
    }
    if (bodyType == BodyTypeOptions.MATE_CONNECTOR)
    {
        return qBodyType(current, BodyType.MATE_CONNECTOR);
    }
    return qBodyType(current, BodyType.COMPOSITE);
}

/**
 * Whether `featureId` is a prefix of `operationId`, that is, whether the operation belongs to that feature
 * (or to one of its sub-features).
 */
export function qvxIdHasPrefix(operationId is Id, featureId is Id) returns boolean
{
    if (size(operationId) < size(featureId))
    {
        return false;
    }
    for (var i = 0; i < size(featureId); i += 1)
    {
        if (operationId[i] != featureId[i])
        {
            return false;
        }
    }
    return true;
}

/**
 * The entities of `candidates` whose creating or last modifying operation belongs to one of `features`.
 * Onshape records only the LAST operation that touched an entity, so an entity modified by feature A and later
 * by feature B is reported for B only. This is the closest thing to a "modified by" query that std offers.
 */
export function qvxLastModifiedBy(context is Context, candidates is Query, features is FeatureList) returns Query
{
    var hits = [];
    for (var entity in evaluateQuery(context, candidates))
    {
        const operationId = lastModifyingOperationId(context, entity);
        for (var feature in features)
        {
            if (qvxIdHasPrefix(operationId, feature.key))
            {
                hits = append(hits, entity);
                break;
            }
        }
    }
    return qUnion(hits);
}
