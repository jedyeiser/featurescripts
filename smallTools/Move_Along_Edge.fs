FeatureScript 3008;
import(path : "onshape/std/common.fs", version : "3008.0");

annotation { "Feature Type Name" : "Move Along Edge", "Feature Type Description" : "Takes a body and moves it a specified distance along an edge or a connected path of edges, keeping orientation." }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Body to move", "Filter" : EntityType.BODY || BodyType.MATE_CONNECTOR}
        definition.moveBodies is Query;

        annotation { "Name" : "Edge(s) to move along", "Filter" : EntityType.EDGE}
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

        annotation { "Name" : "Maintain curve frame (Frenet)", "Description" : "Rotate the body with the curve normal as well as its tangent. When off, only the tangent direction is aligned." }
        definition.useFrenet is boolean;

        annotation { "Name" : "Copy Bodies?", "Default" : false }
        definition.copyBodies is boolean;
    }
    {
        var moveBodies = evaluateQuery(context, qUnion([definition.moveBodies]));
        for (var i = 0; i < size(moveBodies); i += 1)
        {
            moveBodyOnCurve(context, id + ('moveBody' ~ i), moveBodies[i], definition.moveEdge, definition);
        }
    });

export function moveBodyOnCurve(context is Context, id is Id, body is Query, edge is Query, definition is map) returns Query
{
    // constructPath throws a raw string when the selection is empty or disconnected;
    // convert that into an actionable feature error that highlights the edge picker.
    var path;
    try
    {
        path = constructPath(context, qUnion([definition.moveEdge]));
    }
    catch
    {
        throw regenError("Selected edges must form a single connected path (no gaps or branches).", ["moveEdge"], definition.moveEdge);
    }
    var totalLen = evPathLength(context, path);

    var startQ = definition.provideRef ? definition.refVertex : body;

    var startArcLength = locateStartArcLength(context, path, startQ);
    var endArcLength = definition.flipDirection ? (startArcLength - definition.moveDist) : (startArcLength + definition.moveDist);

    var startEval = evalPathAtArcLength(context, path, totalLen, startArcLength);
    var endEval = evalPathAtArcLength(context, path, totalLen, endArcLength);
    
    var motion;
    if (definition.useFrenet)
    {
        // Map the full start frame onto the full end frame (tangent + normal).
        // Where an edge is straight the curve normal is arbitrary, so roll about
        // the tangent is not meaningful there; tangent-only mode avoids that.
        motion = toWorld(endEval.frame) * fromWorld(startEval.frame);
    }
    else
    {
        // Align only the tangent direction and translate the start point to the end point.
        motion = transform(line(startEval.origin, startEval.tangent), line(endEval.origin, endEval.tangent));
    }

    var transformQ = body;
    if (definition.copyBodies)
    {
        opPattern(context, id + "copyBody", {
                "entities" : body,
                "transforms" : [transform(vector(0, 0, 0) * millimeter)],
                "instanceNames" : ['1']
        });

        transformQ = qCreatedBy(id + "copyBody", EntityType.BODY);
    }

    opTransform(context, id + "shiftBodyOnEdge", {
            "bodies" : transformQ,
            "transform" : motion
    });

    return transformQ;
}

// Project startQ onto the path and return its position as an arc length measured
// from the path start (parameter 0), in the path's travel direction.
function locateStartArcLength(context is Context, path is Path, startQ is Query) returns ValueWithUnits
{
    var bestDistance = inf * meter;
    var bestEdgeIndex = 0;
    var bestEdgeParam = 0;
    for (var i = 0; i < size(path.edges); i += 1)
    {
        var d = evDistance(context, {
                "side0" : path.edges[i],
                "side1" : startQ
        });
        if (d.distance < bestDistance)
        {
            bestDistance = d.distance;
            bestEdgeIndex = i;
            bestEdgeParam = d.sides[0].parameter; // arc-length fraction in this edge's own direction
        }
    }

    var cumulative = 0 * meter;
    for (var i = 0; i < bestEdgeIndex; i += 1)
    {
        cumulative = cumulative + evLength(context, { "entities" : path.edges[i] });
    }

    var bestEdgeLength = evLength(context, { "entities" : path.edges[bestEdgeIndex] });
    // path.flipped[i] is true when the path traverses the edge opposite its own direction.
    var fractionInPathDir = path.flipped[bestEdgeIndex] ? (1 - bestEdgeParam) : bestEdgeParam;
    return cumulative + fractionInPathDir * bestEdgeLength;
}

// Evaluate position, path-direction tangent, and a curve frame at arc length s
// along the path. When s falls outside [0, totalLen] the result is extrapolated
// linearly along the nearest end tangent.
function evalPathAtArcLength(context is Context, path is Path, totalLen is ValueWithUnits, s is ValueWithUnits) returns map
{
    var sClamped = max(0 * meter, min(s, totalLen));

    var cumulative = 0 * meter;
    var edgeIndex = size(path.edges) - 1;
    var fractionInPathDir = 1;
    for (var i = 0; i < size(path.edges); i += 1)
    {
        var edgeLength = evLength(context, { "entities" : path.edges[i] });
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
