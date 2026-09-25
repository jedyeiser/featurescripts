FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
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
 * Which frame carries the body's roll about the tangent when the curve frame is kept.
 */
export enum MoveFrameMode
{
    annotation { "Name" : "Curve normal (Frenet)" }
    FRENET,
    annotation { "Name" : "Transported (no twist)" }
    TRANSPORT
}

// Separates the source body names stored in definition.sourceNames (part names cannot hold a newline).
const NAME_SEPARATOR = "\n";

// Samples per transported-frame walk (double reflection); the walk is exact at straight runs.
const TRANSPORT_SAMPLES = 64;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Move Along Edge",
        "Feature Type Description" : "Takes a body and moves it a specified distance along an edge, a connected path of edges or a wire, keeping orientation.",
        "Editing Logic Function" : "moveAlongEdgeEditLogic" }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Body to move", "Filter" : EntityType.BODY || BodyType.MATE_CONNECTOR }
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

        annotation { "Name" : "Distance to move" }
        isLength(definition.moveDist, LENGTH_BOUNDS);

        annotation { "Name" : "Flip direction" }
        definition.flipDirection is boolean;

        annotation { "Name" : "Maintain curve frame", "Description" : "Rotate the body about the tangent as well as aligning the tangent. When off, only the tangent direction is aligned." }
        definition.useFrenet is boolean;

        if (definition.useFrenet)
        {
            annotation { "Name" : "Frame", "Description" : "Curve normal: follows the curvature direction (arbitrary on straight edges, flips at inflections). Transported: carries the start frame along the path without twist.", "Default" : MoveFrameMode.FRENET }
            definition.frameMode is MoveFrameMode;
        }

        annotation { "Name" : "Copy Bodies?", "Default" : false }
        definition.copyBodies is boolean;

        annotation { "Name" : "Name", "Description" : "Name the moved body (or its copy). Prefix and suffix are added to the source body's name.", "Default" : MoveNameMode.NONE }
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
                annotation { "Name" : "Distance to move" }
                isLength(copy.copyDistance, LENGTH_BOUNDS);

                annotation { "Name" : "Name", "Default" : MoveNameMode.NONE }
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

        var moveBodies = evaluateQuery(context, qUnion([definition.moveBodies]));
        var names = definition.sourceNames == "" ? [] : splitByRegexp(definition.sourceNames, NAME_SEPARATOR);
        var namesMissing = false;
        // results[j] = what move j produced, over all source bodies (0 = the main move).
        var extraCount = definition.copyBodies ? size(definition.extraCopies) : 0;
        var results = makeArray(1 + extraCount, []);
        for (var i = 0; i < size(moveBodies); i += 1)
        {
            var baseName = i < size(names) ? names[i] : undefined;
            var moved = moveBodyOnCurve(context, id + ('moveBody' ~ i), moveBodies[i], pathData, baseName, definition);
            namesMissing = namesMissing || moved.namesMissing;
            for (var j = 0; j < size(moved.results); j += 1)
            {
                results[j] = append(results[j], moved.results[j]);
            }
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
    }, { "frameMode" : MoveFrameMode.FRENET, "nameMode" : MoveNameMode.NONE, "nameText" : "", "mcAsPoints" : false, "extraCopies" : [], "sourceNames" : "" });

/**
 * Reads the selected bodies' names into the hidden sourceNames parameter, in the order the
 * feature body evaluates them. A later rename upstream is picked up the next time the feature is edited.
 */
export function moveAlongEdgeEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    var names = [];
    for (var body in evaluateQuery(context, qUnion([definition.moveBodies])))
    {
        names = append(names, getProperty(context, { "entity" : body, "propertyType" : PropertyType.NAME }));
    }
    definition.sourceNames = join(names, NAME_SEPARATOR);
    return definition;
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
 * copy. A mate connector in "as points" mode gets a point per move and is itself left in place.
 * Returns { results : one Query per move, namesMissing : a prefix/suffix name lacked the source name }.
 */
export function moveBodyOnCurve(context is Context, id is Id, body is Query, pathData is map, baseName, definition is map) returns map
{
    var startQ = definition.provideRef ? definition.refVertex : body;
    var startArcLength = locateStartArcLength(context, pathData, startQ);

    // The main move keeps the original ids so existing references to its copies survive.
    var moves = [{ "id" : id, "distance" : definition.moveDist, "nameMode" : definition.nameMode, "nameText" : definition.nameText }];
    if (definition.copyBodies)
    {
        for (var j = 0; j < size(definition.extraCopies); j += 1)
        {
            var copy = definition.extraCopies[j];
            moves = append(moves, { "id" : id + ("extra" ~ j), "distance" : copy.copyDistance, "nameMode" : copy.copyNameMode, "nameText" : copy.copyName });
        }
    }

    var asPoint = definition.mcAsPoints && !isQueryEmpty(context, qBodyType(body, BodyType.MATE_CONNECTOR));
    var namesMissing = false;
    var results = [];
    for (var move in moves)
    {
        var motion = motionAlongPath(context, pathData, startArcLength, move.distance, definition);

        var result = body;
        if (asPoint)
        {
            var mcOrigin = evMateConnector(context, { "mateConnector" : body }).origin;
            opPoint(context, move.id + "point", { "point" : motion * mcOrigin });
            result = qCreatedBy(move.id + "point", EntityType.BODY);
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

        if (move.nameMode != MoveNameMode.NONE)
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
    return { "results" : results, "namesMissing" : namesMissing };
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
 * The rigid motion taking the path point at startArcLength to the point `distance` further on
 * (backwards when flipDirection is set).
 */
function motionAlongPath(context is Context, pathData is map, startArcLength is ValueWithUnits,
    distance is ValueWithUnits, definition is map) returns Transform
{
    var endArcLength = definition.flipDirection ? (startArcLength - distance) : (startArcLength + distance);

    var startEval = evalPathAtArcLength(context, pathData, startArcLength);
    var endEval = evalPathAtArcLength(context, pathData, endArcLength);

    if (definition.useFrenet)
    {
        if (definition.frameMode == MoveFrameMode.TRANSPORT)
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

// Project the start onto the path and return its position as an arc length measured
// from the path start (parameter 0), in the path's travel direction. A body projects its
// centroid (a mate connector its origin): the body's own closest point is ambiguous when it
// touches the path or has a face parallel to it (evDistance returns any point of the tie).
function locateStartArcLength(context is Context, pathData is map, startQ is Query) returns ValueWithUnits
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
    var nearest = nearestOnPath(context, pathData, start);

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

// The path edge closest to the point `target` and the arc-length fraction on it,
// in the edge's own direction: { distance, edgeIndex, edgeParam }.
function nearestOnPath(context is Context, pathData is map, target is Vector) returns map
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
