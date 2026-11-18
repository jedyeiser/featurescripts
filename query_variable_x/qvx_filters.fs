FeatureScript 3083;
/**
 * Query Variable X: the filter stage.
 *
 * An ordered list of filters, each of which keeps or rejects the entities it matches. Used by the
 * Query variable X feature; the logic they call lives in qvx_queries.fs.
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
export import(path : "4215a2585ebe1072420e5d9c", version : "");

/**
 * Whether a filter keeps or removes the entities it matches.
 */
export enum FilterMode
{
    annotation { "Name" : "Keep" }
    KEEP,
    annotation { "Name" : "Reject" }
    REJECT
}

/**
 * The kinds of filter that can be applied to the source query.
 */
export enum FilterType
{
    annotation { "Name" : "Direction" }
    DIRECTION,
    annotation { "Name" : "Between" }
    BETWEEN,
    annotation { "Name" : "Position" }
    POSITION,
    annotation { "Name" : "Geometry type" }
    GEOMETRY,
    annotation { "Name" : "Entity type" }
    ENTITY_TYPE,
    annotation { "Name" : "Body type" }
    BODY_TYPE,
    annotation { "Name" : "Size" }
    SIZE,
    annotation { "Name" : "Edge convexity" }
    CONVEXITY,
    annotation { "Name" : "In selection" }
    SELECTION,
    annotation { "Name" : "Last modified by feature" }
    LAST_MODIFIED_BY
}

/**
 * One item of the Filters array. Parameter ids all start with `flt` because array item ids share the feature's
 * id space.
 */
export predicate filterPredicate(flt is map)
{
    annotation { "Name" : "Keep or reject", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
    flt.fltMode is FilterMode;

    annotation { "Name" : "Filter type", "UIHint" : UIHint.SHOW_LABEL }
    flt.fltType is FilterType;

    if (flt.fltType == FilterType.DIRECTION)
    {
        annotation { "Name" : "Reference direction", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
        flt.fltDirRef is Query;

        annotation { "Name" : "Relation", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
        flt.fltDirRelation is DirectionRelation;

        annotation { "Name" : "Angle tolerance", "Default" : 0 * degree }
        isAngle(flt.fltAngleTol, ANGLE_STRICT_180_BOUNDS);

        annotation { "Name" : "Faces use", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltFaceUse is FaceDirectionUse;

        annotation { "Name" : "Curve direction", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltCurveMode is CurveDirectionMode;

        annotation { "Name" : "Measure in a plane", "Default" : false }
        flt.fltProject is boolean;

        if (flt.fltProject)
        {
            annotation { "Name" : "Projection plane", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltProjectPlane is Query;
        }
    }
    else if (flt.fltType == FilterType.BETWEEN)
    {
        annotation { "Name" : "First bound", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        flt.fltBetweenA is Query;

        annotation { "Name" : "Second bound", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        flt.fltBetweenB is Query;

        annotation { "Name" : "Between", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltBetweenMode is BetweenMode;

        annotation { "Name" : "Include entities on the bounds", "Default" : true }
        flt.fltIncludeBounds is boolean;
    }
    else if (flt.fltType == FilterType.POSITION)
    {
        annotation { "Name" : "Positional query", "Default" : PositionalDirectionalType.INTERSECTS_PLANE, "UIHint" : UIHint.SHOW_LABEL }
        flt.fltPosType is PositionalDirectionalType;

        if (flt.fltPosType == PositionalDirectionalType.PLANE_NORMAL
            || flt.fltPosType == PositionalDirectionalType.INTERSECTS_PLANE
            || flt.fltPosType == PositionalDirectionalType.COINCIDES_WITH_PLANE
            || flt.fltPosType == PositionalDirectionalType.IN_FRONT_OF_PLANE)
        {
            annotation { "Name" : "Reference plane", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltPosPlane is Query;
        }
        else if (flt.fltPosType == PositionalDirectionalType.INTERSECTS_LINE)
        {
            annotation { "Name" : "Reference line", "Filter" : (EntityType.EDGE && GeometryType.LINE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltPosLine is Query;
        }
        else if (flt.fltPosType == PositionalDirectionalType.CLOSEST_TO)
        {
            annotation { "Name" : "Closest to", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltPosClosestRef is Query;
        }
        else if (flt.fltPosType == PositionalDirectionalType.CONTAINS_POINT
            || flt.fltPosType == PositionalDirectionalType.INTERSECTS_BALL)
        {
            annotation { "Name" : "Reference point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltPosVertex is Query;

            if (flt.fltPosType == PositionalDirectionalType.INTERSECTS_BALL)
            {
                annotation { "Name" : "Radius", "Default" : 0 * meter }
                isLength(flt.fltPosRadius, NONNEGATIVE_LENGTH_BOUNDS);
            }
        }
        else
        {
            annotation { "Name" : "Direction", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
            flt.fltPosDirection is Query;
        }
    }
    else if (flt.fltType == FilterType.GEOMETRY)
    {
        annotation { "Name" : "Geometry type", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltGeometryType is GeometryType;
    }
    else if (flt.fltType == FilterType.ENTITY_TYPE)
    {
        annotation { "Name" : "Entity type", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltEntityType is EntityType;
    }
    else if (flt.fltType == FilterType.BODY_TYPE)
    {
        annotation { "Name" : "Body type", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltBodyType is BodyTypeOptions;
    }
    else if (flt.fltType == FilterType.SIZE)
    {
        annotation { "Name" : "Comparison type", "Default" : SizeComparisonType.LARGEST, "UIHint" : UIHint.SHOW_LABEL }
        flt.fltSizeType is SizeComparisonType;

        if (flt.fltSizeType == SizeComparisonType.LARGER_THAN_SELECTION
            || flt.fltSizeType == SizeComparisonType.SMALLER_THAN_SELECTION
            || flt.fltSizeType == SizeComparisonType.EQUAL_TO_SELECTION)
        {
            annotation { "Name" : "Reference selection", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "MaxNumberOfPicks" : 1 }
            flt.fltSizeReference is Query;

            if (flt.fltSizeType == SizeComparisonType.LARGER_THAN_SELECTION
                || flt.fltSizeType == SizeComparisonType.SMALLER_THAN_SELECTION)
            {
                annotation { "Name" : "Or equal to selection", "Default" : false }
                flt.fltSizeAllowEqual is boolean;
            }
        }
    }
    else if (flt.fltType == FilterType.CONVEXITY)
    {
        annotation { "Name" : "Edge convexity type", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltConvexity is EdgeConvexityType;
    }
    else if (flt.fltType == FilterType.LAST_MODIFIED_BY)
    {
        annotation { "Name" : "Features", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        flt.fltModifiedBy is FeatureList;
    }
    else if (flt.fltType == FilterType.SELECTION)
    {
        annotation { "Name" : "Selections", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        flt.fltSelection is Query;
    }
}

export function qvxDirectionMatches(context is Context, flt is map, current is Query) returns Query
{
    const reference = qvxReferenceDirection(context, flt.fltDirRef, flt.fltCurveMode);
    var projNormal = undefined;
    if (flt.fltProject == true)
    {
        projNormal = qvxPlane(context, flt.fltProjectPlane).normal;
    }
    const tolRad = max(flt.fltAngleTol / radian, 1e-6);

    var hits = [];
    for (var entity in evaluateQuery(context, current))
    {
        const dirs = qvxCandidateDirections(context, entity, flt.fltFaceUse, flt.fltCurveMode);
        for (var dir in dirs)
        {
            if (qvxDirectionsMatch(dir, reference, projNormal, flt.fltDirRelation, tolRad))
            {
                hits = append(hits, entity);
                break;
            }
        }
    }
    return qUnion(hits);
}

export function qvxBetweenMatches(context is Context, flt is map, current is Query) returns Query
{
    const boundA = qvxBound(context, flt.fltBetweenA);
    const boundB = qvxBound(context, flt.fltBetweenB);
    const axis = qvxBetweenAxis(boundA, boundB);
    const posA = dot(boundA.origin, axis);
    const posB = dot(boundB.origin, axis);
    const lo = min(posA, posB);
    const hi = max(posA, posB);
    const tol = TOLERANCE.zeroLength * meter;
    const include = flt.fltIncludeBounds == true;
    // Box measured along the Z axis of a frame at the world origin, so zMin and zMax are positions along `axis`.
    const frame = coordSystem(vector(0, 0, 0) * meter, perpendicularVector(axis), axis);

    var hits = [];
    for (var entity in evaluateQuery(context, current))
    {
        const bb = evBox3d(context, { "topology" : entity, "cSys" : frame, "tight" : true });
        const zMin = bb.minCorner[2];
        const zMax = bb.maxCorner[2];
        const zMid = (zMin + zMax) / 2;

        var inside = false;
        if (flt.fltBetweenMode == BetweenMode.FULLY_BETWEEN)
        {
            inside = include ? (zMin >= lo - tol && zMax <= hi + tol) : (zMin > lo + tol && zMax < hi - tol);
        }
        else if (flt.fltBetweenMode == BetweenMode.OVERLAPS)
        {
            inside = include ? (zMax >= lo - tol && zMin <= hi + tol) : (zMax > lo + tol && zMin < hi - tol);
        }
        else
        {
            inside = include ? (zMid >= lo - tol && zMid <= hi + tol) : (zMid > lo + tol && zMid < hi - tol);
        }
        if (inside)
        {
            hits = append(hits, entity);
        }
    }
    return qUnion(hits);
}

/**
 * The entities of `current` that one filter matches (before Keep / Reject is applied).
 */
export function qvxFilterMatches(context is Context, flt is map, current is Query) returns Query
{
    if (flt.fltType == FilterType.DIRECTION)
    {
        return qvxDirectionMatches(context, flt, current);
    }
    if (flt.fltType == FilterType.BETWEEN)
    {
        return qvxBetweenMatches(context, flt, current);
    }
    if (flt.fltType == FilterType.POSITION)
    {
        return positionalDirectionalSelection(context, {
                    "positionalEntities" : current,
                    "positionalMetricType" : flt.fltPosType,
                    "positionalPlane" : flt.fltPosPlane,
                    "positionalLine" : flt.fltPosLine,
                    "positionalVertex" : flt.fltPosVertex,
                    "positionalClosestRef" : flt.fltPosClosestRef,
                    "positionalRadius" : flt.fltPosRadius,
                    "positionalDirection" : flt.fltPosDirection
                });
    }
    if (flt.fltType == FilterType.GEOMETRY)
    {
        return qGeometry(current, flt.fltGeometryType);
    }
    if (flt.fltType == FilterType.ENTITY_TYPE)
    {
        return qEntityFilter(current, flt.fltEntityType);
    }
    if (flt.fltType == FilterType.BODY_TYPE)
    {
        return qvxBodyTypeQuery(current, flt.fltBodyType);
    }
    if (flt.fltType == FilterType.SIZE)
    {
        return sizeComparisonSelection(context, {
                    "sizeComparisonEntities" : current,
                    "sizeComparisonType" : flt.fltSizeType,
                    "sizeComparisonReference" : flt.fltSizeReference,
                    "sizeComparisonAllowEqual" : flt.fltSizeAllowEqual
                });
    }
    if (flt.fltType == FilterType.CONVEXITY)
    {
        return qEdgeConvexityTypeFilter(current, flt.fltConvexity);
    }
    if (flt.fltType == FilterType.LAST_MODIFIED_BY)
    {
        return qvxLastModifiedBy(context, current, flt.fltModifiedBy);
    }
    return qIntersection(current, flt.fltSelection);
}

export const QVX_STAGE_COLORS = [DebugColor.RED, DebugColor.GREEN, DebugColor.BLUE, DebugColor.CYAN, DebugColor.MAGENTA, DebugColor.YELLOW];

/**
 * Run the filter stage over `source`. Each stage is evaluated and frozen, so counts are exact and later
 * filters see a plain set. The entity counts before and after each filter go to the item label; with
 * `debugStages` on, each stage's result is highlighted in its own color.
 */
export function qvxApplyFilters(context is Context, id is Id, definition is map, source is Query) returns Query
{
    var entities = evaluateQuery(context, source);
    setFeatureComputedParameter(context, id, { "name" : "qvxSummary", "value" : size(entities) ~ " source entities" });
    if (definition.filters == undefined)
    {
        return source;
    }

    for (var i = 0; i < size(definition.filters); i += 1)
    {
        const flt = definition.filters[i];
        const before = size(entities);
        const current = qUnion(entities);
        const matches = qvxFilterMatches(context, flt, current);
        const next = flt.fltMode == FilterMode.KEEP ? qIntersection(current, matches) : qSubtraction(current, matches);
        entities = evaluateQuery(context, next);

        setFeatureComputedParameter(context, id, {
                    "name" : faultyArrayParameterId("filters", i, "fltCount"),
                    "value" : before ~ " > " ~ size(entities)
                });
        if (definition.debugStages == true && size(entities) > 0)
        {
            addDebugEntities(context, qUnion(entities), QVX_STAGE_COLORS[i % size(QVX_STAGE_COLORS)]);
        }
    }
    setFeatureComputedParameter(context, id, { "name" : "qvxSummary", "value" : size(entities) ~ " entities after " ~ size(definition.filters) ~ " filters" });
    return qUnion(entities);
}

/**
 * Optionally restrict the final result to one entity type. Reports a warning when that removes everything,
 * because the user asked for a type the query does not contain.
 */
export function qvxApplyOutputType(context is Context, id is Id, definition is map, query is Query) returns Query
{
    if (definition.specifyOutputType != true)
    {
        return query;
    }
    const typed = qEntityFilter(query, definition.outputEntityType);
    if (isQueryEmpty(context, typed) && !isQueryEmpty(context, query))
    {
        reportFeatureWarning(context, id, "The query holds no entities of the requested output type.");
    }
    return typed;
}
