FeatureScript 3070;

// copied from ref

/** Modified version of the standard query variable feature that has been extended to allow more query options
 *
 * The goal of this feature is to eventually allow all query types defined by the standard library or at least
 * the options available in the older Query Explorer feature to enable more advanced procedural workflows
 *
 * Maintained by Derek Van Allen, to request updates message me on the forums in this thread:
 * https://forum.onshape.com/discussion/29012/custom-feature-query-variable
 *
 * Parallelish selection and cap entities features contributed by Jelte
 */

export import(path : "onshape/std/query.fs", version : "3070.0");
export import(path : "onshape/std/edgeconvexitytype.gen.fs", version : "3070.0");
export import(path : "onshape/std/booleanoperationtype.gen.fs", version : "3070.0");

import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/debug.fs", version : "3070.0");
import(path : "onshape/std/feature.fs", version : "3070.0");
import(path : "onshape/std/featureList.fs", version : "3070.0");
import(path : "onshape/std/evaluate.fs", version : "3070.0");
import(path : "onshape/std/string.fs", version : "3070.0");
import(path : "onshape/std/containers.fs", version : "3070.0");
import(path : "onshape/std/error.fs", version : "3070.0");
import(path : "onshape/std/sketch.fs", version : "3070.0");
import(path : "onshape/std/variable.fs", version : "3070.0");
import(path : "onshape/std/attributes.fs", version : "3070.0");

//icon::import(path : "7bc16b71641d1c179b59eb92", version : "8da46da443ae592e706756d7");

/**
 * Attribute name used to store query variable names on entities for persistence through derived part studios.
 */
const QUERY_VARIABLE_ATTRIBUTE_NAME = "queryVariableName";


/**
 * Allowed selection types to create query variable.
 */
export enum SelectionType
{
    annotation { "Name" : "Selection" }
    SELECTION,
    annotation { "Name" : "Created by" }
    CREATED_BY,
    annotation { "Name" : "Cap entities" }
    CAP_ENTITY,
    annotation { "Name" : "Non-cap entities" }
    NON_CAP_ENTITY,
    annotation { "Name" : "Owned by" }
    OWNED_BY,
    annotation { "Name" : "Protrusion" }
    PROTRUSION,
    annotation { "Name" : "Pocket" }
    POCKET,
    annotation { "Name" : "Hole" }
    HOLE,
    annotation { "Name" : "Fillets" }
    FILLETS,
    annotation { "Name" : "Bounded faces" }
    BOUNDED_FACES,
    annotation { "Name" : "Loop/chain connected" }
    LOOP_CHAIN_CONNECTED,
    annotation { "Name" : "Parallel" }
    PARALLEL,
    annotation { "Name" : "Parallelish edges" }
    TOLERANT_PARALLEL,
    annotation { "Name" : "Tangent(ish) connected" }
    TANGENT_CONNECTED,
    annotation { "Name" : "Matching" }
    MATCHING,
    annotation { "Name" : "Matching bodies" }
    MATCHING_BODIES,
    annotation { "Name" : "Adjacent" }
    ADJACENT,
    annotation { "Name" : "Size comparison" }
    SIZE_COMPARISON,
    annotation { "Name" : "Positional/directional" }
    POSITIONAL_DIRECTIONAL,
    annotation { "Name" : "Geometry type" }
    GEOMETRY,
    annotation { "Name" : "All solid bodies" }
    ALL_SOLID_BODIES,
    annotation { "Name" : "Everything" }
    EVERYTHING,
    annotation { "Name" : "Nothing" }
    NOTHING,
    annotation { "Name" : "Edge convexity" }
    EDGE_CONVEXITY,
    annotation { "Name" : "Load from derive feature" }
    LOAD_FROM_DERIVE
}

/**
 * @internal
 */
const SelectionTypeToLowercaseName = {
        SelectionType.SELECTION : "selection",
        SelectionType.CREATED_BY : "created by",
        SelectionType.CAP_ENTITY : "cap entities",
        SelectionType.NON_CAP_ENTITY : "non-cap entities",
        SelectionType.OWNED_BY : "owned by",
        SelectionType.PROTRUSION : "protrusion",
        SelectionType.POCKET : "pocket",
        SelectionType.HOLE : "hole",
        SelectionType.FILLETS : "fillets",
        SelectionType.BOUNDED_FACES : "bounded faces",
        SelectionType.LOOP_CHAIN_CONNECTED : "loop/chain connected",
        SelectionType.PARALLEL : "parallel",
        SelectionType.TOLERANT_PARALLEL : "parallelish edges",
        SelectionType.TANGENT_CONNECTED : "tangent connected",
        SelectionType.MATCHING : "matching",
        SelectionType.MATCHING_BODIES : "matching bodies",
        SelectionType.ADJACENT : "adjacent",
        SelectionType.SIZE_COMPARISON : "size comparison",
        SelectionType.POSITIONAL_DIRECTIONAL : "positional/directional",
        SelectionType.GEOMETRY : "geometry type",
        SelectionType.ALL_SOLID_BODIES : "all solid bodies",
        SelectionType.EVERYTHING : "everything",
        SelectionType.NOTHING : "nothing",
        SelectionType.EDGE_CONVEXITY : "edge convexity",
        SelectionType.LOAD_FROM_DERIVE : "load from derive feature"
    };

const MATCHING_BODY_CLUSTER_RELATIVE_TOLERANCE = 1e-4;

/**
 * Determines the type of entities returned by the adjacency selection.
 */
export enum AdjacentResultType
{
    annotation { "Name" : "Same as seed" }
    SAME_AS_SEED,
    annotation { "Name" : "Faces" }
    FACES,
    annotation { "Name" : "Edges" }
    EDGES,
    annotation { "Name" : "Vertices" }
    VERTICES
}

const adjacentResultTypeToEntityType =
{
        AdjacentResultType.FACES : EntityType.FACE,
        AdjacentResultType.EDGES : EntityType.EDGE,
        AdjacentResultType.VERTICES : EntityType.VERTEX
    };

/**
 * Some selection types accept either faces or edges, not both.
 * This enum allows picking which one.
 */
export enum SeedType
{
    annotation { "Name" : "Face" }
    FACE,
    annotation { "Name" : "Edge" }
    EDGE
}

/**
 * Subset of CompareType with just types allowed in qFilletFaces
 */
export enum FilletCompare
{
    annotation { "Name" : "Equal" }
    EQUAL,
    annotation { "Name" : "Less or equal" }
    LESS_EQUAL,
    annotation { "Name" : "Greater or equal" }
    GREATER_EQUAL
}

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
 * Predicate showing the selection type and the relevant queries/enum allowed by this type.
 */
export predicate initialQueryPredicate(definition is map)
{
    annotation { "Name" : "Selection type" }
    definition.selectionType is SelectionType;

    if (definition.selectionType == SelectionType.TANGENT_CONNECTED
        || definition.selectionType == SelectionType.MATCHING)
    {
        annotation { "Name" : "Entity type" }
        definition.seedType is SeedType;
    }

    if (definition.selectionType == SelectionType.SELECTION)
    {
        annotation { "Name" : "Selections", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.selectionQuery is Query;
    }
    else if (definition.selectionType == SelectionType.CREATED_BY)
    {
        annotation { "Name" : "Created by features", "UIHint" : UIHint.ALLOW_FLAT_SKETCH_SELECTION }
        definition.createdByFeatures is FeatureList;
    }
    else if (definition.selectionType == SelectionType.CAP_ENTITY)
    {
        annotation { "Name" : "Created by features", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        definition.capEntityCreatedByFeatures is FeatureList;
    }
    else if (definition.selectionType == SelectionType.NON_CAP_ENTITY)
    {
        annotation { "Name" : "Created by features", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        definition.nonCapEntityCreatedByFeatures is FeatureList;
    }

    else if (definition.selectionType == SelectionType.OWNED_BY || definition.selectionType == SelectionType.EDGE_CONVEXITY || definition.selectionType == SelectionType.MATCHING_BODIES)
    {
        annotation { "Name" : "Entities", "Filter" : EntityType.BODY && AllowFlattenedGeometry.YES && AllowMeshGeometry.YES }
        definition.seedBodies is Query;
    }
    else if (definition.selectionType == SelectionType.PROTRUSION
        || definition.selectionType == SelectionType.POCKET
        || definition.selectionType == SelectionType.HOLE
        || definition.selectionType == SelectionType.FILLETS
        || definition.selectionType == SelectionType.BOUNDED_FACES
        || (definition.selectionType == SelectionType.TANGENT_CONNECTED && definition.seedType == SeedType.FACE)
        || (definition.selectionType == SelectionType.MATCHING && definition.seedType == SeedType.FACE))
    {
        annotation { "Name" : "Faces", "Filter" : EntityType.FACE }
        definition.seedFaces is Query;
    }
    else if (definition.selectionType == SelectionType.ADJACENT)
    {
        annotation { "Name" : "Seed entities", "Filter" : (EntityType.FACE || EntityType.EDGE || EntityType.VERTEX) && AllowFlattenedGeometry.YES && AllowMeshGeometry.YES }
        definition.adjacentSeedEntities is Query;

        annotation { "Name" : "Adjacency type" }
        definition.adjacencyType is AdjacencyType;

        annotation { "Name" : "Result entities", "Default" : AdjacentResultType.SAME_AS_SEED }
        definition.adjacentResultType is AdjacentResultType;
    }
    else if (definition.selectionType == SelectionType.SIZE_COMPARISON)
    {
        annotation { "Name" : "Entities", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES }
        definition.sizeComparisonEntities is Query;

        annotation { "Name" : "Comparison type", "Default" : SizeComparisonType.LARGEST }
        definition.sizeComparisonType is SizeComparisonType;

        if (definition.sizeComparisonType == SizeComparisonType.LARGER_THAN_SELECTION
            || definition.sizeComparisonType == SizeComparisonType.SMALLER_THAN_SELECTION
            || definition.sizeComparisonType == SizeComparisonType.EQUAL_TO_SELECTION)
        {
            annotation { "Name" : "Reference selection", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "MaxNumberOfPicks" : 1 }
            definition.sizeComparisonReference is Query;

            if (definition.sizeComparisonType == SizeComparisonType.LARGER_THAN_SELECTION
                || definition.sizeComparisonType == SizeComparisonType.SMALLER_THAN_SELECTION)
            {
                annotation { "Name" : "Or equal to selection", "Default" : false }
                definition.sizeComparisonAllowEqual is boolean;
            }
        }
    }
    else if (definition.selectionType == SelectionType.POSITIONAL_DIRECTIONAL)
    {
        annotation { "Name" : "Entities", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES }
        definition.positionalEntities is Query;

        annotation { "Name" : "Positional query", "Default" : PositionalDirectionalType.PLANE_NORMAL }
        definition.positionalMetricType is PositionalDirectionalType;

        if (definition.positionalMetricType == PositionalDirectionalType.PLANE_NORMAL
            || definition.positionalMetricType == PositionalDirectionalType.INTERSECTS_PLANE
            || definition.positionalMetricType == PositionalDirectionalType.COINCIDES_WITH_PLANE
            || definition.positionalMetricType == PositionalDirectionalType.IN_FRONT_OF_PLANE)
        {
            annotation { "Name" : "Reference plane", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
            definition.positionalPlane is Query;
        }
        else if (definition.positionalMetricType == PositionalDirectionalType.INTERSECTS_LINE)
        {
            annotation { "Name" : "Reference line", "Filter" : EntityType.EDGE && GeometryType.LINE, "MaxNumberOfPicks" : 1 }
            definition.positionalLine is Query;
        }
        else if (definition.positionalMetricType == PositionalDirectionalType.CONTAINS_POINT
            || definition.positionalMetricType == PositionalDirectionalType.CLOSEST_TO
            || definition.positionalMetricType == PositionalDirectionalType.INTERSECTS_BALL)
        {
            annotation { "Name" : "Reference point", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.positionalVertex is Query;

            if (definition.positionalMetricType == PositionalDirectionalType.INTERSECTS_BALL)
            {
                annotation { "Name" : "Radius", "Default" : 0 * meter }
                isLength(definition.positionalRadius, NONNEGATIVE_LENGTH_BOUNDS);
            }
        }
        else if (definition.positionalMetricType == PositionalDirectionalType.FARTHEST_ALONG
            || definition.positionalMetricType == PositionalDirectionalType.PLANE_PARALLEL_DIRECTION
            || definition.positionalMetricType == PositionalDirectionalType.FACE_PARALLEL_DIRECTION)
        {
            annotation { "Name" : "Direction", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
            definition.positionalDirection is Query;
        }
    }
    else if (definition.selectionType == SelectionType.GEOMETRY)
    {
        annotation { "Name" : "Seed entities", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES }
        definition.geometrySeedEntities is Query;

        annotation { "Name" : "Geometry type" }
        definition.geometryType is GeometryType;
    }
    else if (definition.selectionType == SelectionType.LOOP_CHAIN_CONNECTED)
    {
        annotation { "Name" : "Edges or faces", "Filter" : EntityType.EDGE || EntityType.FACE }
        definition.seedEdgesOrFaces is Query;
    }
    else if (definition.selectionType == SelectionType.PARALLEL
        || definition.selectionType == SelectionType.TOLERANT_PARALLEL
        || (definition.selectionType == SelectionType.TANGENT_CONNECTED && definition.seedType == SeedType.EDGE)
        || (definition.selectionType == SelectionType.MATCHING && definition.seedType == SeedType.EDGE))
    {
        annotation { "Name" : "Edges", "Filter" : EntityType.EDGE }
        definition.seedEdges is Query;
    }
    if (definition.selectionType == SelectionType.CREATED_BY
        || definition.selectionType == SelectionType.CAP_ENTITY
        || definition.selectionType == SelectionType.NON_CAP_ENTITY
        || definition.selectionType == SelectionType.OWNED_BY
        || definition.selectionType == SelectionType.EVERYTHING)
    {
        annotation { "Name" : "Entity type" }
        definition.entityType is EntityType;
    }
    if (definition.selectionType == SelectionType.FILLETS)
    {
        annotation { "Name" : "Fillet compare type" }
        definition.filletCompareType is FilletCompare;
    }
    if (definition.selectionType == SelectionType.BOUNDED_FACES)
    {
        annotation { "Name" : "Bounds", "Filter" : EntityType.EDGE || EntityType.FACE }
        definition.boundedFacesBounds is Query;
    }
    if (definition.selectionType == SelectionType.EDGE_CONVEXITY)
    {
        annotation { "Name" : "Edge convexity type" }
        definition.edgeConvexityType is EdgeConvexityType;
    }
    if (definition.selectionType == SelectionType.TOLERANT_PARALLEL)
    {
        annotation { "Name" : "Direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
        definition.direction is Query;
    }
    if (definition.selectionType == SelectionType.TANGENT_CONNECTED && definition.seedType == SeedType.FACE
        || definition.selectionType == SelectionType.TOLERANT_PARALLEL)
    {
        annotation { "Name" : "Angle tolerance", "Default" : 0 * degree }
        isAngle(definition.angleTolerance, ANGLE_STRICT_180_BOUNDS);
    }
    if (definition.selectionType == SelectionType.CREATED_BY
        || definition.selectionType == SelectionType.EVERYTHING)
    {
        annotation { "Name" : "Filter construction entities", "Default" : true }
        definition.filterConstruction is boolean;

        annotation { "Name" : "Filter by body type", "Default" : false }
        definition.filterByBodyType is boolean;

        if (definition.filterByBodyType)
        {
            annotation { "Name" : "Body type" }
            definition.createdByBodyType is BodyTypeOptions;
        }
    }

    if (definition.selectionType == SelectionType.CAP_ENTITY)
    {
        annotation { "Name" : "Cap type", "Default" : CapType.EITHER }
        definition.capType is CapType;
    }
    
    if (definition.selectionType == SelectionType.LOAD_FROM_DERIVE)
    {
        annotation { "Name" : "Derive feature", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        definition.deriveFeature is FeatureList;
    }
}

/**
 * Same as initialQueryPredicate, needs to be separate because of naming restriction in array parameters.
 */
export predicate additionalQueryPredicate(addQ is map)
{
    annotation { "Name" : "Selection type" }
    addQ.addQselectionType is SelectionType;

    annotation { "Name" : "Selection type", "UIHint" : UIHint.ALWAYS_HIDDEN }
    addQ.addQlowercaseSelectionType is string;

    if (addQ.addQselectionType == SelectionType.TANGENT_CONNECTED
        || addQ.addQselectionType == SelectionType.MATCHING)
    {
        annotation { "Name" : "Entity type" }
        addQ.addQseedType is SeedType;
    }

    if (addQ.addQselectionType == SelectionType.SELECTION)
    {
        annotation { "Name" : "Selections", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        addQ.addQselectionQuery is Query;
    }
    else if (addQ.addQselectionType == SelectionType.CREATED_BY)
    {
        annotation { "Name" : "Created by features", "UIHint" : UIHint.ALLOW_FLAT_SKETCH_SELECTION }
        addQ.addQcreatedByFeatures is FeatureList;
    }
    else if (addQ.addQselectionType == SelectionType.CAP_ENTITY)
    {
        annotation { "Name" : "Created by features", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        addQ.addQcapEntityCreatedByFeatures is FeatureList;
    }
    else if (addQ.addQselectionType == SelectionType.NON_CAP_ENTITY)
    {
        annotation { "Name" : "Created by features", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        addQ.addQnonCapEntityCreatedByFeatures is FeatureList;
    }
    else if (addQ.addQselectionType == SelectionType.OWNED_BY || addQ.addQselectionType == SelectionType.EDGE_CONVEXITY || addQ.addQselectionType == SelectionType.MATCHING_BODIES)
    {
        annotation { "Name" : "Entities", "Filter" : EntityType.BODY && AllowFlattenedGeometry.YES && AllowMeshGeometry.YES }
        addQ.addQseedBodies is Query;
    }
    else if (addQ.addQselectionType == SelectionType.PROTRUSION
        || addQ.addQselectionType == SelectionType.POCKET
        || addQ.addQselectionType == SelectionType.HOLE
        || addQ.addQselectionType == SelectionType.FILLETS
        || addQ.addQselectionType == SelectionType.BOUNDED_FACES
        || (addQ.addQselectionType == SelectionType.TANGENT_CONNECTED && addQ.addQseedType == SeedType.FACE)
        || (addQ.addQselectionType == SelectionType.MATCHING && addQ.addQseedType == SeedType.FACE))
    {
        annotation { "Name" : "Face", "Filter" : EntityType.FACE }
        addQ.addQseedFaces is Query;
    }
    else if (addQ.addQselectionType == SelectionType.ADJACENT)
    {
        annotation { "Name" : "Seed entities", "Filter" : (EntityType.FACE || EntityType.EDGE || EntityType.VERTEX) && AllowFlattenedGeometry.YES && AllowMeshGeometry.YES }
        addQ.addQadjacentSeedEntities is Query;

        annotation { "Name" : "Adjacency type" }
        addQ.addQadjacencyType is AdjacencyType;

        annotation { "Name" : "Result entities", "Default" : AdjacentResultType.SAME_AS_SEED }
        addQ.addQadjacentResultType is AdjacentResultType;
    }
    else if (addQ.addQselectionType == SelectionType.SIZE_COMPARISON)
    {
        annotation { "Name" : "Entities", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES }
        addQ.addQsizeComparisonEntities is Query;

        annotation { "Name" : "Comparison type", "Default" : SizeComparisonType.LARGEST }
        addQ.addQsizeComparisonType is SizeComparisonType;

        if (addQ.addQsizeComparisonType == SizeComparisonType.LARGER_THAN_SELECTION
            || addQ.addQsizeComparisonType == SizeComparisonType.SMALLER_THAN_SELECTION
            || addQ.addQsizeComparisonType == SizeComparisonType.EQUAL_TO_SELECTION)
        {
            annotation { "Name" : "Reference selection", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES, "MaxNumberOfPicks" : 1 }
            addQ.addQsizeComparisonReference is Query;

            if (addQ.addQsizeComparisonType == SizeComparisonType.LARGER_THAN_SELECTION
                || addQ.addQsizeComparisonType == SizeComparisonType.SMALLER_THAN_SELECTION)
            {
                annotation { "Name" : "Or equal to selection", "Default" : false }
                addQ.addQsizeComparisonAllowEqual is boolean;
            }
        }
    }
    else if (addQ.addQselectionType == SelectionType.POSITIONAL_DIRECTIONAL)
    {
        annotation { "Name" : "Entities", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES }
        addQ.addQpositionalEntities is Query;

        annotation { "Name" : "Positional query", "Default" : PositionalDirectionalType.PLANE_NORMAL }
        addQ.addQpositionalMetricType is PositionalDirectionalType;

        if (addQ.addQpositionalMetricType == PositionalDirectionalType.PLANE_NORMAL
            || addQ.addQpositionalMetricType == PositionalDirectionalType.INTERSECTS_PLANE
            || addQ.addQpositionalMetricType == PositionalDirectionalType.COINCIDES_WITH_PLANE
            || addQ.addQpositionalMetricType == PositionalDirectionalType.IN_FRONT_OF_PLANE)
        {
            annotation { "Name" : "Reference plane", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1 }
            addQ.addQpositionalPlane is Query;
        }
        else if (addQ.addQpositionalMetricType == PositionalDirectionalType.INTERSECTS_LINE)
        {
            annotation { "Name" : "Reference line", "Filter" : EntityType.EDGE && GeometryType.LINE, "MaxNumberOfPicks" : 1 }
            addQ.addQpositionalLine is Query;
        }
        else if (addQ.addQpositionalMetricType == PositionalDirectionalType.CONTAINS_POINT
            || addQ.addQpositionalMetricType == PositionalDirectionalType.CLOSEST_TO
            || addQ.addQpositionalMetricType == PositionalDirectionalType.INTERSECTS_BALL)
        {
            annotation { "Name" : "Reference point", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            addQ.addQpositionalVertex is Query;

            if (addQ.addQpositionalMetricType == PositionalDirectionalType.INTERSECTS_BALL)
            {
                annotation { "Name" : "Radius", "Default" : 0 * meter }
                isLength(addQ.addQpositionalRadius, NONNEGATIVE_LENGTH_BOUNDS);
            }
        }
        else if (addQ.addQpositionalMetricType == PositionalDirectionalType.FARTHEST_ALONG
            || addQ.addQpositionalMetricType == PositionalDirectionalType.PLANE_PARALLEL_DIRECTION
            || addQ.addQpositionalMetricType == PositionalDirectionalType.FACE_PARALLEL_DIRECTION)
        {
            annotation { "Name" : "Direction", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
            addQ.addQpositionalDirection is Query;
        }
    }
    else if (addQ.addQselectionType == SelectionType.GEOMETRY)
    {
        annotation { "Name" : "Seed entities", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES }
        addQ.addQgeometrySeedEntities is Query;

        annotation { "Name" : "Geometry type" }
        addQ.addQgeometryType is GeometryType;
    }
    else if (addQ.addQselectionType == SelectionType.LOOP_CHAIN_CONNECTED)
    {
        annotation { "Name" : "Edge or face", "Filter" : EntityType.EDGE || EntityType.FACE }
        addQ.addQseedEdgesOrFaces is Query;
    }
    else if (addQ.addQselectionType == SelectionType.PARALLEL
        || addQ.addQselectionType == SelectionType.TOLERANT_PARALLEL
        || (addQ.addQselectionType == SelectionType.TANGENT_CONNECTED && addQ.addQseedType == SeedType.EDGE)
        || (addQ.addQselectionType == SelectionType.MATCHING && addQ.addQseedType == SeedType.EDGE))
    {
        annotation { "Name" : "Edge", "Filter" : EntityType.EDGE }
        addQ.addQseedEdges is Query;
    }
    if (addQ.addQselectionType == SelectionType.CREATED_BY
        || addQ.addQselectionType == SelectionType.CAP_ENTITY
        || addQ.addQselectionType == SelectionType.NON_CAP_ENTITY
        || addQ.addQselectionType == SelectionType.OWNED_BY
        || addQ.addQselectionType == SelectionType.EVERYTHING)
    {
        annotation { "Name" : "Entity type" }
        addQ.addQentityType is EntityType;
    }
    if (addQ.addQselectionType == SelectionType.FILLETS)
    {
        annotation { "Name" : "Fillet compare type" }
        addQ.addQfilletCompareType is FilletCompare;
    }
    if (addQ.addQselectionType == SelectionType.BOUNDED_FACES)
    {
        annotation { "Name" : "Bounds", "Filter" : EntityType.EDGE || EntityType.FACE }
        addQ.addQboundedFacesBounds is Query;
    }
    if (addQ.addQselectionType == SelectionType.EDGE_CONVEXITY)
    {
        annotation { "Name" : "Edge convexity type" }
        addQ.addQedgeConvexityType is EdgeConvexityType;
    }
    if (addQ.addQselectionType == SelectionType.TOLERANT_PARALLEL)
    {
        annotation { "Name" : "Direction", "Filter" : QueryFilterCompound.ALLOWS_DIRECTION, "MaxNumberOfPicks" : 1 }
        addQ.addQdirection is Query;
    }
    if (addQ.addQselectionType == SelectionType.TANGENT_CONNECTED && addQ.addQseedType == SeedType.FACE
        || addQ.addQselectionType == SelectionType.TOLERANT_PARALLEL)
    {
        annotation { "Name" : "Angle tolerance", "Default" : 0 * degree }
        isAngle(addQ.addQangleTolerance, ANGLE_STRICT_180_BOUNDS);
    }
    if (addQ.addQselectionType == SelectionType.CREATED_BY
        || addQ.addQselectionType == SelectionType.EVERYTHING)
    {
        annotation { "Name" : "Filter construction entities", "Default" : true }
        addQ.addQfilterConstruction is boolean;

        annotation { "Name" : "Filter by body type", "Default" : false }
        addQ.addQfilterByBodyType is boolean;

        if (addQ.addQfilterByBodyType)
        {
            annotation { "Name" : "Body type" }
            addQ.addQcreatedByBodyType is BodyTypeOptions;
        }
    }

    if (addQ.addQselectionType == SelectionType.CAP_ENTITY)
    {
        annotation { "Name" : "Cap type", "Default" : CapType.EITHER }
        addQ.addQcapType is CapType;
    }
    
    if (addQ.addQselectionType == SelectionType.LOAD_FROM_DERIVE)
    {
        annotation { "Name" : "Derive feature", "UIHint" : UIHint.ALLOW_FEATURE_SELECTION }
        addQ.addQderiveFeature is FeatureList;
    }
}

/**
 * Feature to create a query variable via calling `setQueryVariable`.
 * This variable may be used in featurescript via call `getQueryVariable` or in the UI by clicking the "Variable selection" dropdown in the feature dialog
 * or by selecting the query variable feature in the feature list.
 *
 * @param definition {{
 *      @field name {string} : The name of the feature. Must not belong to a non-query variable feature.
 *          If a query variable feature with this name exists, it will be overwritten after this feature.
 *      @field description {string} : Description of the variable. Maximum length of 256 characters.
 *
 *      @field selectionType {SelectionType} : The type of selection the initial query will hold.
 *      @field seedType {SeedType} : If the selection type allows edges or faces, selects the seed type.
 *      @field selectionQuery {Query} : If selectionType is SELECTION, query that will be contained in the variable.
 *      @field createdByFeatures {FeatureList} : If selectionType is CREATED_BY, features whose created entities will be contained in the variable.
 *      @field capEntityCreatedByFeatures {FeatureList} : If selectionType is CAP_ENTITY, features whose cap entities will be contained in the variable.
 *      @field nonCapEntityCreatedByFeatures {FeatureList} : If selectionType is NON_CAP_ENTITY, features whose non-cap entities will be contained in the variable.
 *      @field filterConstruction {boolean} : If selectionType is CREATED_BY, whether to exclude construction geometry.
 *      @field filterByBodyType {boolean} : If selectionType is CREATED_BY, whether to filter results by body type.
 *      @field createdByBodyType {BodyTypeOptions} : If selectionType is CREATED_BY and filterByBodyType is true, body type to include in the variable.
 *      @field capType {CapType} : If selectionType is CAP_ENTITY, selects which cap entities (start, end, or either) are included.
 *      @field seedBodies {Query} : If selectionType is OWNED_BY or EDGE_CONVEXITY, bodies owning the entities that will be contained in the variable.
 *          If selectionType is MATCHING_BODIES, bodies from which the selection is created.
 *      @field seedFaces {Query} : If selectionType is PROTRUSION or POCKET or HOLE or FILLETS or BOUNDED_FACES, or TANGENT_CONNECTED or MATCHING and seedType is FACE,
 *          faces from which the selection is created.
 *      @field adjacentSeedEntities {Query} : If selectionType is ADJACENT, entities whose neighbors will be collected.
 *      @field adjacencyType {AdjacencyType} : If selectionType is ADJACENT, determines whether adjacency is computed by shared vertices or shared edges.
 *      @field adjacentResultType {AdjacentResultType} : If selectionType is ADJACENT, determines the type of adjacent entities to return.
 *      @field sizeComparisonEntities {Query} : If selectionType is SIZE_COMPARISON, entities to compare by size.
 *      @field sizeComparisonType {SizeComparisonType} : If selectionType is SIZE_COMPARISON, the size comparison to apply.
 *      @field sizeComparisonReference {Query} : If sizeComparisonType compares against a reference selection, the reference entity to compare against.
 *      @field sizeComparisonAllowEqual {boolean} : If sizeComparisonType compares against a reference selection, whether entities equal in size qualify.
 *      @field positionalEntities {Query} : If selectionType is POSITIONAL_DIRECTIONAL, entities filtered by positional or directional relationships.
 *      @field positionalMetricType {PositionalDirectionalType} : If selectionType is POSITIONAL_DIRECTIONAL, the positional or directional query to apply.
 *      @field positionalPlane {Query} : If positionalMetricType references a plane, the plane face used in the comparison.
 *      @field positionalLine {Query} : If positionalMetricType is INTERSECTS_LINE, the line entity used for intersection.
 *      @field positionalVertex {Query} : If positionalMetricType requires a point reference, the vertex used for evaluation.
 *      @field positionalRadius {ValueWithUnits} : If positionalMetricType is INTERSECTS_BALL, the sphere radius used for filtering.
 *      @field positionalDirection {Query} : If positionalMetricType requires a direction, the entity defining the direction.
 *      @field geometrySeedEntities {Query} : If selectionType is GEOMETRY, entities that will be filtered by geometry type.
 *      @field geometryType {GeometryType} : If selectionType is GEOMETRY, geometry category used to filter the seed entities.
 *      @field angleTolerance {ValueWithUnits} : If selectionType is TANGENT_CONNECTED and seedType is FACE,
 *          maximum angular deviation for considering faces tangent. Defaults to `0` degrees.
 *      @field seedEdgesOrFaces {Query} : If selectionType is LOOP_CHAIN_CONNECTED, faces or edges from which the loops are computed.
 *      @field seedEdgesOrFaces {Query} : If selectionType is PARALLEL, or TANGENT_CONNECTED or MATCHING and seedType is EDGE, edges from which the selection is created.
 *      @field entityType {EntityType} : If selectionType is CREATED_BY or CAP_ENTITY or NON_CAP_ENTITY or OWNED_BY, the entity type to include in the variable.
 *      @field filletCompareType {FilletCompare} : If selectionType is FILLETS, the type of fillets to include in the variable.
 *      @field boundedFacesBounds {Query} : If selectionType is BOUNDED_FACES, the faces or edges bounding the selection.
 *      @field edgeConvexityType {EdgeConvexityType} : If selectionType is EDGE_CONVEXITY, the convexity type of edges to include in the variable.
 *
 *      @field addAdditionalQueries {boolean} : Whether to include addition queries in the variable.
 *      @field additionalQueries {array} : An array of additional queries to include. Each item's content is analogous to what is contained in the original query.
 *          It also contains a `booleanOperation` field determining how to combine the additional query with the current query.
 *
 *      @field evaluateOnUse {boolean} : Whether to evaluate the variable when it is created or when it is used.
 *      @field showSelection {boolean} : Whether to highlight the entities contained in the created variable.
 * }}
 */
annotation { "Feature Type Name" : "Query variable+", "Feature Name Template" : "###name", "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
        "Tooltip Template" : "###name #description" }
export const queryVariable = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Name", "UIHint" : [UIHint.UNCONFIGURABLE, UIHint.QUERY_VARIABLE_NAME], "MaxLength" : 10000 }
        definition.name is string;

        annotation { "Name" : "Description", "MaxLength" : 256, "Default" : "" }
        definition.description is string;

        initialQueryPredicate(definition);

        // Hide additional queries option when loading from derive feature
        // because there's no way to guarantee which variables should get which entities
        if (definition.selectionType != SelectionType.LOAD_FROM_DERIVE)
        {
            annotation { "Name" : "Add additional queries" }
            definition.addAdditionalQueries is boolean;

            annotation { "Group Name" : "Additional queries", "Driving Parameter" : "addAdditionalQueries", "Collapsed By Default" : false }
            {
                if (definition.addAdditionalQueries)
                {
                    annotation { "Name" : "Additional queries", "Item name" : "additional query", "Item label template" : "#booleanOperation of #addQlowercaseSelectionType" }
                    definition.additionalQueries is array;
                    for (var addQ in definition.additionalQueries)
                    {
                        annotation { "Name" : "Boolean operation", "UIHint" : UIHint.HORIZONTAL_ENUM }
                        addQ.booleanOperation is BooleanOperationType;

                        additionalQueryPredicate(addQ);
                    }
                }
            }
        }

        annotation { "Name" : "Evaluate on use", "Default" : false }
        definition.evaluateOnUse is boolean;

        annotation { "Name" : "Show selection", "Default" : true }
        definition.showSelection is boolean;
        
        annotation { "Name" : "Save for derived studios", "Default" : false }
        definition.saveForDerivedStudios is boolean;
    }
    {
        if (definition.addAdditionalQueries)
        {
            for (var i = 0; i < size(definition.additionalQueries); i += 1)
            {
                setFeatureComputedParameter(context, id, {
                            "name" : faultyArrayParameterId("additionalQueries", i, "addQlowercaseSelectionType"),
                            "value" : SelectionTypeToLowercaseName[definition.additionalQueries[i].addQselectionType]
                        });
            }
        }

        // Special handling for LOAD_FROM_DERIVE: reconstruct all query variables from a derive feature
        if (definition.selectionType == SelectionType.LOAD_FROM_DERIVE)
        {
            loadAllQueryVariablesFromDerive(context, id, definition);
            return; // Early return - we handle everything in the helper function
        }

        // Validate name for all other selection types
        if (length(definition.name) == 0)
        {
            throw regenError(ErrorStringEnum.QUERY_VARIABLE_EMPTY_NAME);
        }
        checkQueryVariableName(context, definition.name);

        var query = mapSelectionTypeToQuery(context, definition);

        if (definition.addAdditionalQueries)
        {
            for (var addQ in definition.additionalQueries)
            {
                const innerQuery = mapSelectionTypeToQuery(context, remapAdditionalQuery(addQ));

                query = switch (addQ.booleanOperation)
                    {
                            BooleanOperationType.UNION : qUnion(query, innerQuery),
                            BooleanOperationType.SUBTRACTION : qSubtraction(query, innerQuery),
                            BooleanOperationType.INTERSECTION : qIntersection(query, innerQuery)
                        };
            }
        }

        if (!definition.evaluateOnUse)
        {
            // This follows the queries through modifications, substitutions and naming.
            query = qUnion(makeRobustQueriesBatched(context, query));
        }

        setQueryVariable(context, definition.name, definition.description, query);
        
        // If saveForDerivedStudios is enabled, attach the query variable name as an attribute to all entities
        if (definition.saveForDerivedStudios == true)
        {
            try silent
            {
                const evaluatedEntities = evaluateQuery(context, query);
                if (size(evaluatedEntities) > 0)
                {
                    // For each entity, append this QV name to the list of QV names
                    for (var entity in evaluatedEntities)
                    {
                        // Get existing QV names array, or create new one
                        var qvNames = getAttribute(context, {
                            "entity" : entity,
                            "name" : QUERY_VARIABLE_ATTRIBUTE_NAME
                        });
                        
                        if (qvNames == undefined)
                        {
                            qvNames = [];
                        }
                        
                        // Append this QV name if not already in the list
                        if (indexOf(qvNames, definition.name) == -1)
                        {
                            qvNames = append(qvNames, definition.name);
                        }
                        
                        // Set the updated array
                        setAttribute(context, {
                            "entities" : entity,
                            "name" : QUERY_VARIABLE_ATTRIBUTE_NAME,
                            "attribute" : qvNames
                        });
                    }
                }
            }
        }

        if (definition.showSelection)
        {
            try silent
            {
                addDebugEntities(context, query, DebugColor.YELLOW);
            }
        }
        setHighlightedEntities(context, { "entities" : query, "equivalentQueryPropagationOnly" : !definition.evaluateOnUse });
    }, { filterByBodyType : false });

function mapSelectionTypeToQuery(context is Context, definition is map) returns Query
{
    return switch (definition.selectionType)
        {
                SelectionType.SELECTION : definition.selectionQuery,
                SelectionType.CREATED_BY : createdBySelection(context, definition),
                SelectionType.CAP_ENTITY : capEntitiesCreatedBy(context, definition),
                SelectionType.NON_CAP_ENTITY : nonCapEntitiesCreatedBy(definition),
                SelectionType.OWNED_BY : qOwnedByBody(definition.seedBodies, definition.entityType),
                SelectionType.PROTRUSION : qConvexConnectedFaces(definition.seedFaces),
                SelectionType.POCKET : qConcaveConnectedFaces(definition.seedFaces),
                SelectionType.HOLE : qHoleFaces(definition.seedFaces),
                SelectionType.FILLETS : qFilletFaces(definition.seedFaces, definition.filletCompareType as CompareType),
                SelectionType.BOUNDED_FACES : qFaceOrEdgeBoundedFaces(qUnion([definition.seedFaces, definition.boundedFacesBounds])),
                SelectionType.LOOP_CHAIN_CONNECTED : qLoopEdges(definition.seedEdgesOrFaces),
                SelectionType.PARALLEL : qParallelEdges(definition.seedEdges),
                SelectionType.TOLERANT_PARALLEL : qTolerantParallelEdges(context, definition),
                SelectionType.TANGENT_CONNECTED : definition.seedType == SeedType.FACE ?
                // Include faces within the given angular deviation
                qTangentConnectedFaces(definition.seedFaces, definition.angleTolerance) :
                qTangentConnectedEdges(definition.seedEdges),
                SelectionType.MATCHING : definition.seedType == SeedType.FACE ? qMatching(definition.seedFaces) : qMatching(definition.seedEdges),
                SelectionType.MATCHING_BODIES : qMatchingBodies(context, definition.seedBodies),
                SelectionType.ADJACENT : adjacencySelection(definition),
                SelectionType.SIZE_COMPARISON : sizeComparisonSelection(context, definition),
                SelectionType.POSITIONAL_DIRECTIONAL : positionalDirectionalSelection(context, definition),
                SelectionType.GEOMETRY : qGeometry(definition.geometrySeedEntities, definition.geometryType),
                SelectionType.ALL_SOLID_BODIES : qAllSolidBodies(),
                SelectionType.EVERYTHING : everythingSelection(context, definition),
                SelectionType.NOTHING : qNothing(),
                SelectionType.EDGE_CONVEXITY : qEdgeConvexityTypeFilter(qOwnedByBody(definition.seedBodies, EntityType.EDGE), definition.edgeConvexityType)
            };
}

/**
 * Builds a qAdjacent query using the provided adjacency parameters.
 * @param definition {map} : Parameters that describe the adjacency query selection.
 *      Expected keys: `adjacentSeedEntities`, `adjacencyType`, `adjacentResultType`.
 */
function adjacencySelection(definition is map) returns Query
{
    const adjacencyType = definition.adjacencyType as AdjacencyType;
    const seedEntities = definition.adjacentSeedEntities as Query;
    const resultSelection = definition.adjacentResultType as AdjacentResultType;

    if (resultSelection == AdjacentResultType.SAME_AS_SEED)
    {
        return qAdjacent(seedEntities, adjacencyType);
    }

    const resultEntityType = adjacentResultTypeToEntityType[resultSelection];
    if (adjacencyType == AdjacencyType.EDGE && resultEntityType == EntityType.VERTEX)
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, ["adjacentResultType"]);
    }

    return qAdjacent(seedEntities, adjacencyType, resultEntityType);
}

/**
 * Ensures that all entities in a query share a single size-comparable entity type.
 * Returns the detected entity type and a filtered query containing only that type.
 */
function enforceUniformSizeEntityType(context is Context, entities is Query, errorField is string)
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
function measureEntitySize(context is Context, entity is Query, entityType is EntityType)
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
function sizeComparisonSelection(context is Context, definition is map) returns Query
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
function evaluatePlaneReference(context is Context, planeQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, planeQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return evPlane(context, { "face" : planeQuery });
}

/**
 * Reads a line from an edge query, validating that the selection is present.
 */
function evaluateLineReference(context is Context, lineQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, lineQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return evLine(context, { "edge" : lineQuery });
}

/**
 * Reads a point from a vertex query, validating that the selection is present.
 */
function evaluatePointReference(context is Context, pointQuery is Query, errorField is string)
{
    if (isQueryEmpty(context, pointQuery))
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return evVertexPoint(context, { "vertex" : pointQuery });
}

/**
 * Extracts a direction vector from the provided reference, failing on invalid inputs.
 */
function evaluateDirectionReference(context is Context, directionQuery is Query, errorField is string)
{
    const directionResult = extractDirection(context, directionQuery);
    if (directionResult == undefined)
    {
        throw regenError(ErrorStringEnum.INVALID_INPUT, [errorField]);
    }

    return directionResult;
}

/**
 * Builds positional and directional queries that relate a seed selection to reference geometry.
 */
function positionalDirectionalSelection(context is Context, definition is map) returns Query
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

    if (positionalType == PositionalDirectionalType.CONTAINS_POINT
        || positionalType == PositionalDirectionalType.CLOSEST_TO
        || positionalType == PositionalDirectionalType.INTERSECTS_BALL)
    {
        const referencePoint = evaluatePointReference(context, definition.positionalVertex as Query, "positionalVertex");
        if (positionalType == PositionalDirectionalType.CONTAINS_POINT)
        {
            return qContainsPoint(candidateEntities, referencePoint);
        }

        if (positionalType == PositionalDirectionalType.CLOSEST_TO)
        {
            return qClosestTo(candidateEntities, referencePoint);
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

function qMatchingBodies(context is Context, seedBodies is Query) returns Query
{
    const seedBodiesArray = evaluateQuery(context, seedBodies);
    if (size(seedBodiesArray) == 0)
    {
        return qNothing();
    }

    const candidateBodiesQuery = qEntityFilter(qEverything(), EntityType.BODY);
    const candidateBodies = evaluateQuery(context, candidateBodiesQuery);
    if (size(candidateBodies) == 0)
    {
        return qUnion(seedBodiesArray);
    }

    const candidateBodiesUnion = qUnion(candidateBodies);
    const seedBodiesUnion = qUnion(seedBodiesArray);

    var clusters = [];
    try
    {
        clusters = clusterBodies(context, {
                    "bodies" : candidateBodiesUnion,
                    "relativeTolerance" : MATCHING_BODY_CLUSTER_RELATIVE_TOLERANCE
                });
    }
    catch
    {
        return qUnion(seedBodiesArray);
    }

    var matchedBodies = [];
    for (var cluster in clusters)
    {
        var clusterContainsSeed = false;
        for (var bodyIndex in cluster)
        {
            const index = bodyIndex as number;
            const bodyQuery = candidateBodiesUnion->qNthElement(index);
            if (!isQueryEmpty(context, qIntersection([bodyQuery, seedBodiesUnion])))
            {
                clusterContainsSeed = true;
                break;
            }
        }

        if (!clusterContainsSeed)
        {
            continue;
        }

        for (var bodyIndex in cluster)
        {
            const index = bodyIndex as number;
            matchedBodies = append(matchedBodies, candidateBodiesUnion->qNthElement(index));
        }
    }

    return size(matchedBodies) == 0 ? qUnion(seedBodiesArray) : qUnion(matchedBodies);
}

function filterSketchEdgesAndVerticesFromSheetDeprecated(context is Context, definition is map) returns Query
{
    var featureIds = [];
    for (var feature in definition.createdByFeatures)
    {
        featureIds = append(featureIds, feature.key);
    }
    // For sketches, there are two operations that create edges and vertices.
    // When we select an edge or a vertex from a sketch, it's always from the wire operation.
    // So, to avoid grabbing duplicate edges and vertices, we change the selected id to be the wire operation instead of the whole sketch.
    if ((definition.entityType == EntityType.EDGE || definition.entityType == EntityType.VERTEX) && containsSketch(context, definition.createdByFeatures))
    {
        for (var i = 0; i < size(featureIds); i += 1)
        {
            if (isIdForSketch(context, featureIds[i]))
            {
                featureIds[i] = featureIds[i] + makeId("wireOp");
            }
        }
    }
    var createdByQuery = [];
    for (var featureId in featureIds)
    {
        createdByQuery = append(createdByQuery, qCreatedBy(featureId, definition.entityType));
    }
    return qUnion(createdByQuery);
}

function createdBySelection(context is Context, definition is map) returns Query
{
    var createdByQuery;
    if (isAtVersionOrLater(context, FeatureScriptVersionNumber.V2793_BETTER_QV_SKETCH_IMPRINT_FILTERING))
    {
        createdByQuery = qCreatedBy(definition.createdByFeatures, definition.entityType);
        // There is a much easier way to filter edges/vertices from imprints: we subtract sketch entities belonging to sheet bodies.
        if (definition.entityType == EntityType.EDGE || definition.entityType == EntityType.VERTEX)
        {
            const edgesOrVerticesInSheetInSketch = createdByQuery->qSketchFilter(SketchObject.YES)->qBodyType(BodyType.SHEET);
            createdByQuery = createdByQuery->qSubtraction(edgesOrVerticesInSheetInSketch);
        }

        if (definition.entityType == EntityType.BODY && isAtVersionOrLater(context, FeatureScriptVersionNumber.V2808_CREATED_BY_CLOSED_COMPOSITE_FILTER))
        {
            const closedPartsConstituents = createdByQuery->qCompositePartTypeFilter(CompositePartType.CLOSED)->qContainedInCompositeParts();
            createdByQuery = createdByQuery->qSubtraction(closedPartsConstituents);
        }
    }
    else
    {
        createdByQuery = filterSketchEdgesAndVerticesFromSheetDeprecated(context, definition);
    }
    if (definition.filterConstruction)
    {
        createdByQuery = createdByQuery->qConstructionFilter(ConstructionObject.NO);
    }
    if (definition.filterByBodyType)
    {
        createdByQuery = createdByQuery->qBodyType(definition.createdByBodyType as BodyType);
    }
    return createdByQuery;
}

/**
 * Builds a query for cap entities created by the specified features.
 */
function capEntitiesCreatedBy(context is Context, definition is map) returns Query
{
    var capEntitiesQuery = qNothing();
    for (var featureId, _ in definition.capEntityCreatedByFeatures)
    {
        capEntitiesQuery = qUnion(capEntitiesQuery, qCapEntity(featureId, definition.capType, definition.entityType));
    }

    if (isQueryEmpty(context, capEntitiesQuery))
    {
        throw regenError("No cap entities found for the selected features and entity type. Only extrude, revolve, sweep, loft and thicken features produce cap entities.");
    }

    return capEntitiesQuery;
}

/**
 * Builds a query for non-cap entities created by the specified features.
 */
function nonCapEntitiesCreatedBy(definition is map) returns Query
{
    if (definition.entityType == EntityType.BODY)
    {
        throw regenError("Non-cap queries cannot resolve bodies.", ["entityType"]);
    }

    var nonCapEntitiesQuery = qNothing();
    for (var featureId, _ in definition.nonCapEntityCreatedByFeatures)
    {
        nonCapEntitiesQuery = qUnion(nonCapEntitiesQuery, qNonCapEntity(featureId, definition.entityType));
    }

    return nonCapEntitiesQuery;
}

function remapAdditionalQuery(definition is map) returns map
{
    var remapped = {};
    const prefix = "addQ";
    const offset = 4;
    for (var key, value in definition)
    {
        if (!startsWith(key, prefix))
        {
            continue;
        }
        remapped[substring(key, offset)] = value;
    }
    return remapped;
}

/**
 * Filters input edges by angle tolerance to a reference direction.
 * This function enables "parallelish" edge selection with an angular tolerance.
 *
 * @param context {Context} : The context in which the query is evaluated.
 * @param definition {map} : Map containing:
 *      - seedEdges {Query} : Input edges to filter
 *      - direction {Query} : Reference direction for parallel comparison
 *      - angleTolerance {ValueWithUnits} : Maximum angular deviation from parallel
 * @returns {Query} : Query containing edges within the angular tolerance of the direction
 */
function qTolerantParallelEdges(context is Context, definition is map) returns Query
{
    var tolerantParallelQuery = new box(qNothing());

    const direction = extractDirection(context, definition.direction);

    // Filter input edges to only straight lines
    var straightEdges = definition.seedEdges->qGeometry(GeometryType.LINE);

    const angleTolerance = definition.angleTolerance;

    for (var edge in evaluateQuery(context, straightEdges))
    {
        const edgeDirection = evLine(context, { "edge" : edge }).direction;

        var angle = angleBetween(direction, edgeDirection);

        if (angle > 90 * degree)
        {
            angle = abs(angle - 180 * degree);
        }

        if (angle <= angleTolerance)
        {
            tolerantParallelQuery[] = qUnion(tolerantParallelQuery[], edge);
        }
    }

    return tolerantParallelQuery[];
}

/**
 * Builds a query for all entities of a specified type with optional filtering.
 * This enables selecting everything in the context with construction and body type filters.
 *
 * @param context {Context} : The context in which the query is evaluated.
 * @param definition {map} : Map containing:
 *      - entityType {EntityType} : Type of entities to query
 *      - filterConstruction {boolean} : Whether to exclude construction geometry
 *      - filterByBodyType {boolean} : Whether to filter by body type
 *      - createdByBodyType {BodyTypeOptions} : Body type to filter by if filterByBodyType is true
 * @returns {Query} : Query containing all entities matching the specified filters
 */
function everythingSelection(context is Context, definition is map) returns Query
{
    var everythingQuery = qEverything(definition.entityType);

    if (definition.filterConstruction)
    {
        everythingQuery = everythingQuery->qConstructionFilter(ConstructionObject.NO);
    }

    if (definition.filterByBodyType)
    {
        everythingQuery = everythingQuery->qBodyType(definition.createdByBodyType as BodyType);
    }

    return everythingQuery;
}

function checkQueryVariableName(context is Context, name is string)
{
    verifyVariableNameIsValid(name, "name");
    var exists = false;
    try silent
    {
        getVariable(context, name);
        exists = true;
    }
    if (exists)
    {
        throw regenError(ErrorStringEnum.QUERY_VARIABLE_NAME_ALREADY_USED_IN_NON_QUERY_VARIABLE, ["name"]);
    }
}

/**
 * Loads and reconstructs all query variables from one or more derive features by scanning for queryVariableName attributes.
 * This creates multiple query variables automatically based on the attribute values found.
 * When multiple derive features are selected, entities from all derives are merged into their respective query variables.
 *
 * @param context {Context} : The context in which the query variables are created.
 * @param id {Id} : The feature ID.
 * @param definition {map} : Map containing:
 *      - deriveFeature {FeatureList} : One or more derive features to scan for entities with QV attributes
 */
function loadAllQueryVariablesFromDerive(context is Context, id is Id, definition is map)
{
    if (definition.deriveFeature == undefined || size(definition.deriveFeature) == 0)
    {
        throw regenError("No derive feature selected. Please select a derive feature to load query variables from.", ["deriveFeature"]);
    }
    
    // Build a map of query variable names to their entities across all selected derive features
    var queryVariableMap = {};
    var processedAnyDerive = false;
    
    // Iterate through all selected derive features
    for (var deriveFeatureId, _ in definition.deriveFeature)
    {
        // Query all entities created by this derive feature
        const allDerivedEntities = qUnion([
            qCreatedBy(deriveFeatureId, EntityType.BODY),
            qCreatedBy(deriveFeatureId, EntityType.FACE),
            qCreatedBy(deriveFeatureId, EntityType.EDGE),
            qCreatedBy(deriveFeatureId, EntityType.VERTEX)
        ]);
        
        // Get all entities that have the query variable attribute
        const entitiesWithQVAttribute = qHasAttribute(allDerivedEntities, QUERY_VARIABLE_ATTRIBUTE_NAME);
        
        // Skip this derive if it has no QV attributes (but continue processing others)
        if (!isQueryEmpty(context, entitiesWithQVAttribute))
        {
            processedAnyDerive = true;
            
            const entityArray = evaluateQuery(context, entitiesWithQVAttribute);
            for (var entity in entityArray)
            {
                const qvNames = getAttribute(context, {
                    "entity" : entity,
                    "name" : QUERY_VARIABLE_ATTRIBUTE_NAME
                });
                
                // Expect an array of QV names
                if (qvNames != undefined && qvNames is array)
                {
                    // Add this entity to each QV name it belongs to
                    for (var qvName in qvNames)
                    {
                        if (qvName is string)
                        {
                            if (queryVariableMap[qvName] == undefined)
                            {
                                queryVariableMap[qvName] = [];
                            }
                            queryVariableMap[qvName] = append(queryVariableMap[qvName], entity);
                        }
                    }
                }
            }
        }
    }
    
    // Check if we found any entities with QV attributes across all derives
    if (!processedAnyDerive)
    {
        throw regenError("No entities with saved query variable attributes found from the selected derive feature(s). Ensure the original query variables were created with 'Save for derived studios' enabled.", ["deriveFeature"]);
    }
    
    // Create a query variable for each unique name found
    var createdCount = 0;
    var createdNames = [];
    var allLoadedEntities = [];
    for (var qvName, entities in queryVariableMap)
    {
        if (size(entities) > 0)
        {
            const entityQuery = qUnion(entities);
            setQueryVariable(context, qvName, "Loaded from derive feature", entityQuery);
            createdCount += 1;
            createdNames = append(createdNames, qvName);
            
            // Collect all entities for show selection
            for (var entity in entities)
            {
                allLoadedEntities = append(allLoadedEntities, entity);
            }
        }
    }
    
    if (createdCount == 0)
    {
        throw regenError("No valid query variables could be reconstructed from the derive feature(s).", ["deriveFeature"]);
    }
    
    // Count how many derive features were selected
    var deriveFeatureCount = 0;
    for (var _, __ in definition.deriveFeature)
    {
        deriveFeatureCount += 1;
    }
    
    // Report which query variables were loaded
    var statusMessage = "Loaded " ~ createdCount ~ " query variable" ~ (createdCount > 1 ? "s" : "") ~ " from " ~ deriveFeatureCount ~ " derive feature" ~ (deriveFeatureCount > 1 ? "s" : "") ~ ": ";
    statusMessage = statusMessage ~ createdNames[0];
    for (var i = 1; i < size(createdNames); i += 1)
    {
        statusMessage = statusMessage ~ ", " ~ createdNames[i];
    }
    reportFeatureInfo(context, id, statusMessage);
    
    // Show selection if enabled
    if (definition.showSelection == true)
    {
        const allEntitiesQuery = qUnion(allLoadedEntities);
        try silent
        {
            addDebugEntities(context, allEntitiesQuery, DebugColor.YELLOW);
        }
        setHighlightedEntities(context, { "entities" : allEntitiesQuery });
    }
}


/**
 * Returns a query that was previously stored in a variable of the given name.
 */
export function getQueryVariable(context is Context, name is string) returns Query
{
    return @getQueryVariable(context, { "name" : name, "defaultValue" : qNothing() });
}

/**
 * Saves a query in a variable with the given name.
 */
export function setQueryVariable(context is Context, name is string, value is Query)
{
    return setQueryVariable(context, name, "", value);
}

/**
 * Saves a query in a variable with the given name and description.
 */
export function setQueryVariable(context is Context, name is string, description is string, value is Query)
{
    return @setQueryVariable(context, { "name" : name, "description" : description, "value" : value });
}