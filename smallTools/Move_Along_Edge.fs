FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: move_along_edge_icon.svg (feature icon)
IconNamespace::import(path : "e00a212f91b5216e53c5e66f", version : "6c54073813e984b5ed59167d");

/**
 * How a moved body, copy or point is named. Prefix and suffix are added to the source body's name.
 */
export enum MoveNameMode
{
    annotation { "Name" : "N/A" }
    NONE,
    annotation { "Name" : "New name" }
    NEW_NAME,
    annotation { "Name" : "Prefix" }
    PREFIX,
    annotation { "Name" : "Suffix" }
    SUFFIX
}

/**
 * Which part of the motion along the path is applied.
 */
export enum MoveApplyMode
{
    annotation { "Name" : "Move and rotate" }
    MOVE_AND_ROTATE,
    annotation { "Name" : "Translate only" }
    TRANSLATE,
    annotation { "Name" : "Rotate only" }
    ROTATE
}

/**
 * How the rotation follows the path.
 */
export enum MoveOrientation
{
    annotation { "Name" : "Tangent only" }
    TANGENT,
    annotation { "Name" : "Transported (no twist)" }
    TRANSPORT,
    annotation { "Name" : "Curve normal (Frenet)" }
    FRENET
}

/**
 * Where a move ends: a distance along the path, or the path point nearest a selection.
 */
export enum MoveToMode
{
    annotation { "Name" : "Distance" }
    DISTANCE,
    annotation { "Name" : "Nearest point to" }
    NEAREST
}

// Id of the flip arrow manipulator.
const FLIP_MANIPULATOR = "flipArrow";

// Separates the source body names stored in definition.sourceNames (part names cannot hold a newline).
const NAME_SEPARATOR = "\n";

// Samples per transported-frame walk (double reflection); the walk is exact at straight runs.
const TRANSPORT_SAMPLES = 64;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Move Along Edge",
        "Feature Type Description" : "Takes a body, mate connector or sketch entity and moves it a specified distance along an edge, a connected path of edges or a wire, keeping orientation. Sketch edges become wires and sketch vertices become points at the new location.",
        "Editing Logic Function" : "moveAlongEdgeEditLogic", "Manipulator Change Function" : "moveAlongEdgeManipulatorChange" }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Whole sketch bodies are excluded (they cannot be transformed); pick their edges or vertices instead.
        annotation { "Name" : "Entities to move", "Description" : "Bodies and mate connectors move (or copy). Sketch edges and vertices are left in place: each move creates a wire or point.",
                     "Filter" : (EntityType.BODY && SketchObject.NO) || BodyType.MATE_CONNECTOR || ((EntityType.EDGE || EntityType.VERTEX) && SketchObject.YES) }
        definition.moveBodies is Query;

        annotation { "Name" : "Edges or wire to move along", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE) }
        definition.moveEdge is Query;

        annotation { "Name" : "Provide Reference Point" }
        definition.provideRef is boolean;

        if (definition.provideRef)
        {
            annotation { "Name" : "Reference Vertex", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.refVertex is Query;
        }

        annotation { "Name" : "Move to", "UIHint" : [UIHint.SHOW_LABEL], "Description" : "Distance: move a distance along the path. Nearest point to: move to the path point nearest a selection (where a curve or face crosses the path, that crossing).", "Default" : MoveToMode.DISTANCE }
        definition.moveMode is MoveToMode;

        if (definition.moveMode == MoveToMode.NEAREST)
        {
            annotation { "Name" : "Target", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.moveTarget is Query;
        }
        else
        {
            annotation { "Name" : "Distance to move" }
            isLength(definition.moveDist, LENGTH_BOUNDS);
        }

        annotation { "Name" : "Flip direction", "Description" : "Move backwards along the path (Distance moves only)." }
        definition.flipDirection is boolean;

        annotation { "Name" : "Apply", "UIHint" : [UIHint.SHOW_LABEL], "Description" : "Translate only keeps the orientation. Rotate only keeps the position: the entity turns about its reference point (reference vertex, else its centroid or mate connector origin).", "Default" : MoveApplyMode.MOVE_AND_ROTATE }
        definition.applyMode is MoveApplyMode;

        if (definition.applyMode != MoveApplyMode.TRANSLATE)
        {
            annotation { "Name" : "Orientation", "UIHint" : [UIHint.SHOW_LABEL], "Description" : "Tangent only: turn with the tangent. Transported: also roll with the path, without twist. Curve normal: follow the curvature direction (arbitrary on straight edges, flips at inflections).", "Default" : MoveOrientation.TANGENT }
            definition.orientation is MoveOrientation;
        }

        annotation { "Name" : "Copy Bodies?", "Default" : false }
        definition.copyBodies is boolean;

        annotation { "Name" : "Naming", "UIHint" : [UIHint.SHOW_LABEL], "Description" : "Name the moved body (or its copy). Prefix and suffix are added to the source body's name.", "Default" : MoveNameMode.NONE }
        definition.nameMode is MoveNameMode;

        if (definition.nameMode != MoveNameMode.NONE)
        {
            annotation { "Name" : "Name text" }
            definition.nameText is string;
        }

        annotation { "Name" : "Mate connectors as points", "Description" : "For a selected mate connector, create a point at each moved location and leave the mate connector where it is.", "Default" : false }
        definition.mcAsPoints is boolean;

        if (definition.copyBodies)
        {
            annotation { "Name" : "Additional copies", "Item name" : "copy", "Item label template" : "#copyName", "Description" : "More copies of each body, each moved its own distance from the same start point." }
            definition.extraCopies is array;
            for (var copy in definition.extraCopies)
            {
                annotation { "Name" : "Move to", "UIHint" : [UIHint.SHOW_LABEL], "Default" : MoveToMode.DISTANCE }
                copy.copyMoveMode is MoveToMode;

                if (copy.copyMoveMode == MoveToMode.NEAREST)
                {
                    annotation { "Name" : "Target", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                    copy.copyTarget is Query;
                }
                else
                {
                    annotation { "Name" : "Distance to move" }
                    isLength(copy.copyDistance, LENGTH_BOUNDS);
                }

                annotation { "Name" : "Naming", "UIHint" : [UIHint.SHOW_LABEL], "Default" : MoveNameMode.NONE }
                copy.copyNameMode is MoveNameMode;

                if (copy.copyNameMode != MoveNameMode.NONE)
                {
                    annotation { "Name" : "Name text" }
                    copy.copyName is string;
                }
            }
        }

        // Filled by the editing logic: getProperty cannot read names during regeneration (correction 36).
        annotation { "Name" : "Source names", "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.sourceNames is string;
    }
    {
        var pathData = buildMovePath(context, definition.moveEdge);

        var moveBodies = selectedEntities(context, definition.moveBodies);
        // Names are stored for named bodies only, in selection order; sketch entities and mate connectors have none.
        var names = definition.sourceNames == "" ? [] : splitByRegexp(definition.sourceNames, NAME_SEPARATOR);
        var bodyIndex = 0;
        var namesMissing = false;
        var connectorNotNamed = false;
        // results[j] = what move j produced, over all source bodies (0 = the main move).
        var extraCount = definition.copyBodies ? size(definition.extraCopies) : 0;
        var results = makeArray(1 + extraCount, []);
        for (var i = 0; i < size(moveBodies); i += 1)
        {
            var baseName = "";
            if (hasPartName(context, moveBodies[i]))
            {
                baseName = bodyIndex < size(names) ? names[bodyIndex] : undefined;
                bodyIndex += 1;
            }
            var moved = moveBodyOnCurve(context, id + ('moveBody' ~ i), moveBodies[i], pathData, baseName, definition);
            namesMissing = namesMissing || moved.namesMissing;
            connectorNotNamed = connectorNotNamed || moved.connectorNotNamed;
            if (i == 0 && definition.moveMode != MoveToMode.NEAREST)
            {
                // The positive move direction at the first entity's reference point; clicking it flips.
                addManipulators(context, id, { (FLIP_MANIPULATOR) : flipManipulator({
                                    "base" : moved.startPoint, "direction" : moved.startTangent, "flipped" : definition.flipDirection }) });
            }
            for (var j = 0; j < size(moved.results); j += 1)
            {
                results[j] = append(results[j], moved.results[j]);
            }
        }

        // A point turned about itself stays where it is.
        var selection = qUnion(moveBodies);
        var points = qUnion([qEntityFilter(selection, EntityType.VERTEX),
                    definition.mcAsPoints ? qBodyType(selection, BodyType.MATE_CONNECTOR) : qNothing()]);
        if (definition.applyMode == MoveApplyMode.ROTATE && !definition.provideRef && !isQueryEmpty(context, points))
        {
            reportFeatureInfo(context, id, "Rotate only: points are created at their source locations (a point has no orientation).");
        }

        if (connectorNotNamed)
        {
            reportFeatureInfo(context, id, "Mate connectors cannot be named by a feature; they were moved unnamed. Use 'Mate connectors as points' for named points.");
        }

        if (namesMissing)
        {
            reportFeatureWarning(context, id, "Some source body names were not available for prefix/suffix; only the name text was used. Edit the feature to refresh the names.");
        }

        // One key per move: initial_copy (the main move, or the moved bodies when not copying), copy_1, copy_2, ...
        var queries = { "initial_copy" : extractableQuery(qUnion(results[0]), "The bodies (or points) of the main move.", DebugColor.GREEN) };
        for (var j = 1; j <= extraCount; j += 1)
        {
            queries["copy_" ~ j] = extractableQuery(qUnion(results[j]), "The bodies (or points) of additional copy " ~ j ~ ".", DebugColor.BLUE);
        }
        var allResults = [];
        for (var r in results)
        {
            allResults = concatenateArrays([allResults, r]);
        }
        embedStandardOutputs(context, id, {
                    "output" : qUnion(allResults),
                    "outputDescription" : "The moved bodies, copies and points",
                    "inputs" : qUnion([definition.moveBodies, definition.moveEdge]),
                    "queries" : queries
                });
    }, { "moveMode" : MoveToMode.DISTANCE, "moveTarget" : qNothing(), "applyMode" : MoveApplyMode.MOVE_AND_ROTATE, "orientation" : MoveOrientation.TANGENT, "nameMode" : MoveNameMode.NONE, "nameText" : "", "mcAsPoints" : false, "extraCopies" : [], "sourceNames" : "" });

/**
 * Reads the selected bodies' names into the hidden sourceNames parameter, in the order the
 * feature body evaluates them. A later rename upstream is picked up the next time the feature is edited.
 */
export function moveAlongEdgeEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    var names = [];
    for (var body in selectedEntities(context, definition.moveBodies))
    {
        if (!hasPartName(context, body))
        {
            continue;
        }
        names = append(names, getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME }));
    }
    definition.sourceNames = join(names, NAME_SEPARATOR);
    return definition;
}

/**
 * The selected entities in selection order, each as its own query. Anything picked on a mate
 * connector (the dialog can hand over the connector's vertex) becomes the connector body, so it is
 * moved as a connector and never mistaken for a sketch vertex. Shared by the feature body and the
 * editing logic so the stored names line up with the bodies.
 */
function selectedEntities(context is Context, selection is Query) returns array
{
    var entities = [];
    for (var entity in evaluateQuery(context, qUnion([selection])))
    {
        // The connector body's own id (a query through its vertex stops resolving once the connector moves).
        var connector = evaluateQuery(context, qBodyType(qOwnerBody(entity), BodyType.MATE_CONNECTOR));
        entities = append(entities, connector == [] ? entity : connector[0]);
    }
    return entities;
}

/**
 * Clicking the direction arrow toggles Flip direction.
 */
export function moveAlongEdgeManipulatorChange(context is Context, definition is map, newManipulators is map) returns map
{
    if (newManipulators[FLIP_MANIPULATOR] != undefined)
    {
        definition.flipDirection = newManipulators[FLIP_MANIPULATOR].flipped;
    }
    return definition;
}

/**
 * Whether a selected entity has a part name to prefix or suffix: bodies do; sketch entities and
 * mate connectors (getProperty NAME gives undefined) do not.
 */
function hasPartName(context is Context, entity is Query) returns boolean
{
    return !isQueryEmpty(context, qEntityFilter(entity, EntityType.BODY)) && isQueryEmpty(context, qBodyType(entity, BodyType.MATE_CONNECTOR));
}

/**
 * The path through the selected edges and the edges of the selected wire bodies, with its
 * edge lengths: { path, lengths, totalLen }.
 */
function buildMovePath(context is Context, selection is Query) returns map
{
    var edges = qUnion([qEntityFilter(selection, EntityType.EDGE),
                qOwnedByBody(qEntityFilter(selection, EntityType.BODY), EntityType.EDGE)]);
    // constructPath throws a raw string when the selection is empty or disconnected;
    // convert that into an actionable feature error that highlights the edge picker.
    var path;
    try
    {
        path = constructPath(context, edges);
    }
    catch
    {
        throw regenError("Selected edges must form a single connected path (no gaps or branches).", ["moveEdge"], selection);
    }
    var lengths = [];
    var totalLen = 0 * meter;
    for (var edge in path.edges)
    {
        var edgeLength = evLength(context, { "entities" : edge });
        lengths = append(lengths, edgeLength);
        totalLen = totalLen + edgeLength;
    }
    return { "path" : path, "lengths" : lengths, "totalLen" : totalLen };
}

/**
 * Moves (or copies) one body along the path: the main move plus, when copying, each additional
 * copy. A mate connector in "as points" mode, and a sketch vertex, get a point per move; a sketch
 * edge gets a wire per move. Those sources stay in place.
 * Returns { results : one Query per move, namesMissing : a prefix/suffix name lacked the source name,
 * connectorNotNamed : a name was asked for a mate connector, startPoint, startTangent : the positive direction there }.
 */
export function moveBodyOnCurve(context is Context, id is Id, body is Query, pathData is map, baseName, definition is map) returns map
{
    var startQ = definition.provideRef ? definition.refVertex : body;
    var startPoint = referencePoint(context, startQ);
    var startArcLength = arcLengthOf(pathData, nearestOnPath(context, pathData, startPoint));

    // The main move keeps the original ids so existing references to its copies survive.
    var moves = [{ "id" : id, "nameMode" : definition.nameMode, "nameText" : definition.nameText,
                "end" : endArcLength(context, pathData, startArcLength, definition.moveMode, definition.moveDist, definition.moveTarget, "moveTarget", definition) }];
    if (definition.copyBodies)
    {
        for (var j = 0; j < size(definition.extraCopies); j += 1)
        {
            var copy = definition.extraCopies[j];
            var mode = copy.copyMoveMode == undefined ? MoveToMode.DISTANCE : copy.copyMoveMode;
            moves = append(moves, { "id" : id + ("extra" ~ j), "nameMode" : copy.copyNameMode, "nameText" : copy.copyName,
                        "end" : endArcLength(context, pathData, startArcLength, mode, copy.copyDistance, copy.copyTarget, "extraCopies", definition) });
        }
    }

    var isVertex = !isQueryEmpty(context, qEntityFilter(body, EntityType.VERTEX));
    var isConnector = !isQueryEmpty(context, qBodyType(body, BodyType.MATE_CONNECTOR));
    var isEdge = !isQueryEmpty(context, qEntityFilter(body, EntityType.EDGE));
    var asPoint = isVertex || (definition.mcAsPoints && isConnector);
    var namesMissing = false;
    var connectorNotNamed = false;
    var results = [];
    for (var move in moves)
    {
        var motion = motionAlongPath(context, pathData, startArcLength, move.end, startPoint, definition);

        var result = body;
        if (asPoint)
        {
            var source = isVertex ? evVertexPoint(context, { "vertex" : body }) : evMateConnector(context, { "mateConnector" : body }).origin;
            opPoint(context, move.id + "point", { "point" : motion * source });
            result = qCreatedBy(move.id + "point", EntityType.BODY);
        }
        else if (isEdge)
        {
            opExtractWires(context, move.id + "wire", { "edges" : body });
            result = qCreatedBy(move.id + "wire", EntityType.BODY);
            opTransform(context, move.id + "shiftBodyOnEdge", {
                    "bodies" : result,
                    "transform" : motion
            });
        }
        else
        {
            if (definition.copyBodies)
            {
                opPattern(context, move.id + "copyBody", {
                        "entities" : body,
                        "transforms" : [identityTransform()],
                        "instanceNames" : ['1']
                });
                result = qCreatedBy(move.id + "copyBody", EntityType.BODY);
            }
            opTransform(context, move.id + "shiftBodyOnEdge", {
                    "bodies" : result,
                    "transform" : motion
            });
        }

        if (move.nameMode != MoveNameMode.NONE && isConnector && !asPoint)
        {
            // setProperty refuses a mate connector (CANNOT_RESOLVE_ENTITIES).
            connectorNotNamed = true;
        }
        else if (move.nameMode != MoveNameMode.NONE)
        {
            if (move.nameMode != MoveNameMode.NEW_NAME && baseName == undefined)
            {
                namesMissing = true;
            }
            setProperty(context, { "entities" : result, "propertyType" : PropertyType.NAME,
                        "value" : composeName(move.nameMode, move.nameText, baseName) });
        }
        results = append(results, result);
    }
    return { "results" : results, "namesMissing" : namesMissing, "connectorNotNamed" : connectorNotNamed,
            "startPoint" : startPoint, "startTangent" : evalPathAtArcLength(context, pathData, startArcLength).tangent };
}

function composeName(mode is MoveNameMode, text is string, baseName) returns string
{
    if (mode == MoveNameMode.NEW_NAME || baseName == undefined)
    {
        return text;
    }
    if (mode == MoveNameMode.PREFIX)
    {
        return text ~ baseName;
    }
    return baseName ~ text;
}

/**
 * Where a move ends, as an arc length: `distance` on from the start (backwards when flipDirection
 * is set), or the path point nearest `target`.
 */
function endArcLength(context is Context, pathData is map, startArcLength is ValueWithUnits, mode is MoveToMode,
    distance, target, targetParameter is string, definition is map) returns ValueWithUnits
{
    if (mode == MoveToMode.NEAREST)
    {
        if (target == undefined || isQueryEmpty(context, target))
        {
            throw regenError("Select a target for the move.", [targetParameter]);
        }
        // The target's own nearest point: a curve or face crossing the path gives the crossing.
        return arcLengthOf(pathData, nearestOnPath(context, pathData, target));
    }
    return definition.flipDirection ? (startArcLength - distance) : (startArcLength + distance);
}

/**
 * The motion the Apply mode asks for: the full path motion from startArcLength to endArcLength,
 * its translation of the path point alone, or its rotation alone about `pivot`.
 */
function motionAlongPath(context is Context, pathData is map, startArcLength is ValueWithUnits,
    endArcLength is ValueWithUnits, pivot is Vector, definition is map) returns Transform
{
    if (definition.applyMode == MoveApplyMode.TRANSLATE)
    {
        // The start path point to the end path point, with no rotation.
        return transform(evalPathAtArcLength(context, pathData, endArcLength).origin - evalPathAtArcLength(context, pathData, startArcLength).origin);
    }
    var full = pathMotion(context, pathData, startArcLength, endArcLength, definition);
    if (definition.applyMode == MoveApplyMode.ROTATE)
    {
        // The same rotation, about the pivot: the pivot stays put.
        return transform(full.linear, pivot - full.linear * pivot);
    }
    return full;
}

/**
 * The full rigid motion (rotation and translation) from the path frame at startArcLength to the one at endArcLength.
 */
function pathMotion(context is Context, pathData is map, startArcLength is ValueWithUnits,
    endArcLength is ValueWithUnits, definition is map) returns Transform
{
    var startEval = evalPathAtArcLength(context, pathData, startArcLength);
    var endEval = evalPathAtArcLength(context, pathData, endArcLength);

    if (definition.orientation == MoveOrientation.TRANSPORT || definition.orientation == MoveOrientation.FRENET)
    {
        if (definition.orientation == MoveOrientation.TRANSPORT)
        {
            var endNormal = transportNormal(context, pathData, startArcLength, endArcLength, startEval);
            var endFrame = coordSystem(endEval.origin, endNormal, endEval.tangent);
            return toWorld(endFrame) * fromWorld(startEval.frame);
        }
        // Map the full start frame onto the full end frame (tangent + curve normal).
        return toWorld(endEval.frame) * fromWorld(startEval.frame);
    }
    // Align only the tangent direction and translate the start point to the end point.
    return transform(line(startEval.origin, startEval.tangent), line(endEval.origin, endEval.tangent));
}

/**
 * Carries the start frame's normal along the path from s0 to s1 without twist (rotation-minimizing
 * frame by double reflection, Wang et al. 2008) and returns the normal at s1.
 */
function transportNormal(context is Context, pathData is map, s0 is ValueWithUnits, s1 is ValueWithUnits, startEval is map) returns Vector
{
    var point = startEval.origin;
    var tangent = startEval.tangent;
    var normal = startEval.frame.xAxis;
    for (var k = 1; k <= TRANSPORT_SAMPLES; k += 1)
    {
        var next = evalPathAtArcLength(context, pathData, s0 + (s1 - s0) * k / TRANSPORT_SAMPLES);
        var v1 = next.origin - point;
        var c1 = dot(v1, v1);
        var normalL = normal;
        var tangentL = tangent;
        if (c1 > TOLERANCE.zeroLength * TOLERANCE.zeroLength * meter * meter)
        {
            normalL = normal - (2 / c1) * dot(v1, normal) * v1;
            tangentL = tangent - (2 / c1) * dot(v1, tangent) * v1;
        }
        var v2 = next.tangent - tangentL;
        var c2 = dot(v2, v2);
        normal = c2 > TOLERANCE.zeroAngle * TOLERANCE.zeroAngle ? normalL - (2 / c2) * dot(v2, normalL) * v2 : normalL;
        point = next.origin;
        tangent = next.tangent;
    }
    // Remove round-off so the frame's axes are exactly perpendicular.
    return normalize(normal - dot(normal, tangent) * tangent);
}

// The point that locates the start on the path (projected onto it) and that Rotate only turns
// about: a vertex's point, a mate connector's origin, else the centroid. A body's own closest
// point is not used: it is ambiguous when the body touches the path or has a face parallel to
// it (evDistance returns any point of the tie).
function referencePoint(context is Context, startQ is Query) returns Vector
{
    var start;
    if (!isQueryEmpty(context, qEntityFilter(startQ, EntityType.VERTEX)))
    {
        start = evVertexPoint(context, { "vertex" : startQ });
    }
    else if (!isQueryEmpty(context, qBodyType(startQ, BodyType.MATE_CONNECTOR)))
    {
        start = evMateConnector(context, { "mateConnector" : startQ }).origin;
    }
    else
    {
        start = evApproximateCentroid(context, { "entities" : startQ });
    }
    return start;
}

// The arc length from the path start of a nearestOnPath result.
function arcLengthOf(pathData is map, nearest is map) returns ValueWithUnits
{
    var path = pathData.path;
    var cumulative = 0 * meter;
    for (var i = 0; i < nearest.edgeIndex; i += 1)
    {
        cumulative = cumulative + pathData.lengths[i];
    }

    // path.flipped[i] is true when the path traverses the edge opposite its own direction.
    var fractionInPathDir = path.flipped[nearest.edgeIndex] ? (1 - nearest.edgeParam) : nearest.edgeParam;
    return cumulative + fractionInPathDir * pathData.lengths[nearest.edgeIndex];
}

// The path edge closest to `target` (a point or a Query) and the arc-length fraction on it,
// in the edge's own direction: { distance, edgeIndex, edgeParam }.
function nearestOnPath(context is Context, pathData is map, target) returns map
{
    var best = { "distance" : inf * meter, "edgeIndex" : 0, "edgeParam" : 0 };
    for (var i = 0; i < size(pathData.path.edges); i += 1)
    {
        var d = evDistance(context, {
                "side0" : pathData.path.edges[i],
                "side1" : target
        });
        if (d.distance < best.distance)
        {
            best = { "distance" : d.distance, "edgeIndex" : i, "edgeParam" : d.sides[0].parameter };
        }
    }
    return best;
}

// Evaluate position, path-direction tangent, and a curve frame at arc length s
// along the path. A closed path wraps around; on an open path, s outside
// [0, totalLen] is extrapolated linearly along the nearest end tangent.
function evalPathAtArcLength(context is Context, pathData is map, sAlong is ValueWithUnits) returns map
{
    var path = pathData.path;
    var totalLen = pathData.totalLen;
    var s = sAlong;
    if (path.closed && totalLen > 0 * meter)
    {
        s = s - floor(s / totalLen) * totalLen;
    }
    var sClamped = max(0 * meter, min(s, totalLen));

    var cumulative = 0 * meter;
    var edgeIndex = size(path.edges) - 1;
    var fractionInPathDir = 1;
    for (var i = 0; i < size(path.edges); i += 1)
    {
        var edgeLength = pathData.lengths[i];
        if (sClamped <= cumulative + edgeLength || i == size(path.edges) - 1)
        {
            edgeIndex = i;
            fractionInPathDir = (edgeLength == 0 * meter) ? 0 : (sClamped - cumulative) / edgeLength;
            fractionInPathDir = max(0, min(1, fractionInPathDir));
            break;
        }
        cumulative = cumulative + edgeLength;
    }

    var flipped = path.flipped[edgeIndex];
    var edgeParam = flipped ? (1 - fractionInPathDir) : fractionInPathDir;

    var curveFrame = evEdgeCurvatures(context, {
            "edge" : path.edges[edgeIndex],
            "parameters" : [edgeParam]
    })[0].frame; // CoordSystem: zAxis = tangent, xAxis = normal

    var tangent = flipped ? -curveFrame.zAxis : curveFrame.zAxis;
    var origin = curveFrame.origin;

    // Extrapolate linearly past the path ends along the end tangent.
    if (s < 0 * meter)
    {
        origin = origin + s * tangent;              // s < 0 steps behind the start
    }
    else if (s > totalLen)
    {
        origin = origin + (s - totalLen) * tangent; // steps past the end
    }

    // Rebuild a frame with the tangent oriented along the path direction so the
    // start and end frames share a consistent handedness for the Frenet transform.
    var frame = coordSystem(origin, curveFrame.xAxis, tangent);

    return { "origin" : origin, "tangent" : tangent, "frame" : frame };
}
