FeatureScript 3083;
/**
 * Query Variable X: the filter stage.
 *
 * An ordered list of filters, each of which keeps or rejects the entities it matches. Used by the
 * Query variable X feature; the logic they call lives in qvx_queries.fs.
 */
export import(path : "onshape/std/query.fs", version : "3083.0");
export import(path : "onshape/std/edgeconvexitytype.gen.fs", version : "3083.0");
export import(path : "onshape/std/edgetopology.gen.fs", version : "3083.0");
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
    annotation { "Name" : "Edge topology (one or two sided)" }
    EDGE_TOPOLOGY,
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
    annotation { "Name" : "Keep or reject", "Description" : "Keep the entities the filter matches, or reject them.", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
    flt.fltMode is FilterMode;

    annotation { "Name" : "Filter type", "Description" : "What the filter tests. The options below depend on this choice.", "UIHint" : UIHint.SHOW_LABEL }
    flt.fltType is FilterType;

    if (flt.fltType == FilterType.DIRECTION)
    {
        annotation { "Name" : "Reference direction", "Description" : "An edge, planar face, cylinder axis or mate connector (Z axis) that gives the reference direction. A curved edge uses its chord.", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        flt.fltDirRef is Query;

        annotation { "Name" : "Relation", "Description" : "Parallel (either way along the line) or perpendicular to the reference direction.", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
        flt.fltDirRelation is DirectionRelation;

        annotation { "Name" : "Angle tolerance", "Description" : "Largest angle from exactly parallel or perpendicular that still matches.", "Default" : 0 * degree }
        isAngle(flt.fltAngleTol, ANGLE_STRICT_180_BOUNDS);

        annotation { "Name" : "Faces use", "Description" : "Use a face normal (planar faces) or the axis (cylinders and cones) as its direction.", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltFaceUse is FaceDirectionUse;

        annotation { "Name" : "Curve direction", "Description" : "How a curved edge gives a direction: chord from start to end, start tangent, end tangent, or either end tangent. Straight edges use their own direction.", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltCurveMode is CurveDirectionMode;

        annotation { "Name" : "Measure in a plane", "Description" : "Flatten the directions into a plane before comparing, so only the angle within that plane counts.", "Default" : false }
        flt.fltProject is boolean;

        if (flt.fltProject)
        {
            annotation { "Name" : "Projection plane", "Description" : "Planar face or mate connector (plane normal to Z) to measure in.", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltProjectPlane is Query;
        }
    }
    else if (flt.fltType == FilterType.BETWEEN)
    {
        annotation { "Name" : "First bound", "Description" : "First bound: a planar face, a vertex or a mate connector (a point unless the option below is on).", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        flt.fltBetweenA is Query;

        annotation { "Name" : "Second bound", "Description" : "Second bound: a planar face, a vertex or a mate connector (a point unless the option below is on).", "Filter" : (EntityType.FACE && GeometryType.PLANE) || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        flt.fltBetweenB is Query;

        annotation { "Name" : "Between", "Description" : "Fully between: the whole entity lies inside. Overlaps: any part lies inside. Center between: the entity center lies inside.", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltBetweenMode is BetweenMode;

        annotation { "Name" : "Include entities on the bounds", "Description" : "Count entities lying exactly on a bound as between.", "Default" : true }
        flt.fltIncludeBounds is boolean;

        annotation { "Name" : "Treat mate connectors as planes (normal to Z)", "Description" : "Treat each mate connector as the plane through its origin normal to its Z axis, instead of as a point.", "Default" : false }
        flt.fltMateAsPlane is boolean;
    }
    else if (flt.fltType == FilterType.POSITION)
    {
        annotation { "Name" : "Positional query", "Description" : "Which positional test to apply to the current entities.", "Default" : PositionalDirectionalType.INTERSECTS_PLANE, "UIHint" : UIHint.SHOW_LABEL }
        flt.fltPosType is PositionalDirectionalType;

        if (flt.fltPosType == PositionalDirectionalType.PLANE_NORMAL
            || flt.fltPosType == PositionalDirectionalType.INTERSECTS_PLANE
            || flt.fltPosType == PositionalDirectionalType.COINCIDES_WITH_PLANE
            || flt.fltPosType == PositionalDirectionalType.IN_FRONT_OF_PLANE)
        {
            annotation { "Name" : "Reference plane", "Description" : "Planar faces or mate connectors (plane normal to Z). An entity matches if it matches any of them.", "Filter" : (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR }
            flt.fltPosPlane is Query;
        }
        else if (flt.fltPosType == PositionalDirectionalType.INTERSECTS_LINE)
        {
            annotation { "Name" : "Reference line", "Description" : "Straight edges or mate connectors (Z axis). An entity matches if it matches any of them.", "Filter" : (EntityType.EDGE && GeometryType.LINE) || BodyType.MATE_CONNECTOR }
            flt.fltPosLine is Query;
        }
        else if (flt.fltPosType == PositionalDirectionalType.CLOSEST_TO)
        {
            annotation { "Name" : "Closest to", "Description" : "A vertex, edge, face, body or mate connector. Keeps the entity (or tied entities) closest to it.", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltPosClosestRef is Query;
        }
        else if (flt.fltPosType == PositionalDirectionalType.CONTAINS_POINT
            || flt.fltPosType == PositionalDirectionalType.INTERSECTS_BALL)
        {
            annotation { "Name" : "Reference point", "Description" : "Vertices or mate connectors (origin). An entity matches if it matches any of them.", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            flt.fltPosVertex is Query;

            if (flt.fltPosType == PositionalDirectionalType.INTERSECTS_BALL)
            {
                annotation { "Name" : "Radius", "Description" : "Distance around the reference point.", "Default" : 0 * meter }
                isLength(flt.fltPosRadius, NONNEGATIVE_LENGTH_BOUNDS);
            }
        }
        else
        {
            annotation { "Name" : "Direction", "Description" : "An edge, planar face, cylinder axis or mate connector (Z axis) that gives the direction.", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            flt.fltPosDirection is Query;
        }
    }
    else if (flt.fltType == FilterType.GEOMETRY)
    {
        annotation { "Name" : "Geometry type", "Description" : "Geometry type to test for: plane, cylinder, line, circle, ...", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltGeometryType is GeometryType;
    }
    else if (flt.fltType == FilterType.ENTITY_TYPE)
    {
        annotation { "Name" : "Entity type", "Description" : "Entity type to test for: face, edge, vertex or body.", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltEntityType is EntityType;
    }
    else if (flt.fltType == FilterType.BODY_TYPE)
    {
        annotation { "Name" : "Body type", "Description" : "Body type to test for: part, surface, curve, point, mate connector or composite.", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltBodyType is BodyTypeOptions;
    }
    else if (flt.fltType == FilterType.SIZE)
    {
        annotation { "Name" : "Comparison type", "Description" : "Largest, smallest, or larger, smaller or equal to a reference entity.", "Default" : SizeComparisonType.LARGEST, "UIHint" : UIHint.SHOW_LABEL }
        flt.fltSizeType is SizeComparisonType;

        if (flt.fltSizeType == SizeComparisonType.LARGER_THAN_SELECTION
            || flt.fltSizeType == SizeComparisonType.SMALLER_THAN_SELECTION
            || flt.fltSizeType == SizeComparisonType.EQUAL_TO_SELECTION)
        {
            annotation { "Name" : "Reference selection", "Description" : "The entity whose size is the reference.", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "MaxNumberOfPicks" : 1 }
            flt.fltSizeReference is Query;

            if (flt.fltSizeType == SizeComparisonType.LARGER_THAN_SELECTION
                || flt.fltSizeType == SizeComparisonType.SMALLER_THAN_SELECTION)
            {
                annotation { "Name" : "Or equal to selection", "Description" : "Also match entities equal in size to the reference.", "Default" : false }
                flt.fltSizeAllowEqual is boolean;
            }
        }
    }
    else if (flt.fltType == FilterType.CONVEXITY)
    {
        annotation { "Name" : "Edge convexity type", "Description" : "Convex, concave or smooth edges.", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltConvexity is EdgeConvexityType;
    }
    else if (flt.fltType == FilterType.EDGE_TOPOLOGY)
    {
        annotation { "Name" : "Edge topology", "Description" : "One-sided (naked edges of a surface), two-sided (joins two faces) or wire (sketch and curve edges).", "UIHint" : UIHint.SHOW_LABEL }
        flt.fltEdgeTopology is EdgeTopology;
    }
    else if (flt.fltType == FilterType.LAST_MODIFIED_BY)
    {
        annotation { "Name" : "Features", "Description" : "Features to test. An entity matches if the last operation that created or modified it belongs to one of them.", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        flt.fltModifiedBy is FeatureList;
    }
    else if (flt.fltType == FilterType.SELECTION)
    {
        annotation { "Name" : "Selections", "Description" : "Entities to test membership of.", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
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
    const mateAsPlane = flt.fltMateAsPlane == true;
    const boundA = qvxBound(context, flt.fltBetweenA, mateAsPlane);
    const boundB = qvxBound(context, flt.fltBetweenB, mateAsPlane);
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
    if (flt.fltType == FilterType.EDGE_TOPOLOGY)
    {
        return qEdgeTopologyFilter(current, flt.fltEdgeTopology);
    }
    if (flt.fltType == FilterType.LAST_MODIFIED_BY)
    {
        return qvxLastModifiedBy(context, current, flt.fltModifiedBy);
    }
    return qIntersection(current, flt.fltSelection);
}

export const QVX_STAGE_COLORS = [DebugColor.RED, DebugColor.GREEN, DebugColor.BLUE, DebugColor.CYAN, DebugColor.MAGENTA, DebugColor.YELLOW];

/**
 * How the filters combine. All: every filter narrows the result of the one before it (an intersection for Keep
 * filters). Any: each Keep filter runs on the whole source and the matches are united; Reject filters then run in order
 * on that union. Use Any for "on this plane OR on that cylinder" across different filter types.
 */
export enum FilterCombine
{
    annotation { "Name" : "All (each filter narrows the previous result)" }
    ALL,
    annotation { "Name" : "Any keep filter (union), then reject filters" }
    ANY
}

/**
 * Run one filter over a set of entities and return what remains (Keep: the matches; Reject: the rest).
 */
export function qvxRunFilter(context is Context, flt is map, entities is array) returns array
{
    const current = qUnion(entities);
    const matches = qvxFilterMatches(context, flt, current);
    return evaluateQuery(context, flt.fltMode == FilterMode.KEEP ? qIntersection(current, matches) : qSubtraction(current, matches));
}

/**
 * Run the filter stage over `source`. Each stage is evaluated and frozen, so counts are exact and later
 * filters see a plain set. The entity counts before and after each filter go to the item label; with
 * `debugStages` on, each stage's result is highlighted in its own color. See FilterCombine for All and Any.
 */
export function qvxApplyFilters(context is Context, id is Id, definition is map, source is Query) returns Query
{
    var entities = evaluateQuery(context, source);
    const sourceEntities = entities;
    setFeatureComputedParameter(context, id, { "name" : "qvxSummary", "value" : size(entities) ~ " source entities" });
    if (definition.filters == undefined)
    {
        return source;
    }

    const anyMode = definition.filterCombine == FilterCombine.ANY;
    if (anyMode)
    {
        // Every Keep filter sees the whole source; their matches are united.
        var united = [];
        var anyKeep = false;
        for (var i = 0; i < size(definition.filters); i += 1)
        {
            const flt = definition.filters[i];
            if (flt.fltMode != FilterMode.KEEP)
            {
                continue;
            }
            anyKeep = true;
            const kept = qvxRunFilter(context, flt, sourceEntities);
            united = concatenateArrays([united, kept]);
            setFeatureComputedParameter(context, id, {
                        "name" : faultyArrayParameterId("filters", i, "fltCount"),
                        "value" : size(sourceEntities) ~ " > " ~ size(kept)
                    });
            if (definition.debugStages == true && size(kept) > 0)
            {
                addDebugEntities(context, qUnion(kept), QVX_STAGE_COLORS[i % size(QVX_STAGE_COLORS)]);
            }
        }
        if (anyKeep)
        {
            entities = evaluateQuery(context, qUnion(united));
        }
    }

    for (var i = 0; i < size(definition.filters); i += 1)
    {
        const flt = definition.filters[i];
        if (anyMode && flt.fltMode == FilterMode.KEEP)
        {
            continue;
        }
        const before = size(entities);
        entities = qvxRunFilter(context, flt, entities);

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
