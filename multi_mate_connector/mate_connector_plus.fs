FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/path.fs", version : "3095.0");
// IMPORT: mc_core.fs (same document: references, paths, arc length)
import(path : "0a18eeece6adacc759621422", version : "2095707b9424d45d3bf94814");
// IMPORT: mate_connector_plus_icon.svg (feature icon)
IconNamespace::import(path : "c77b15e39da290cd4c47d86e", version : "8cd6167f6128c77d55b6e4d4");

/**
 * Mate connector +.
 *
 * Defines a mate connector frame precisely, then does one of three things with it:
 *   New        creates the mate connector (no tree clutter, but a mate connector made in FeatureScript cannot be named).
 *   Build      draws a marker: three len x len squares meeting at the origin, one normal to each axis, as one surface body.
 *              The face normal to Z is blue, to X red, to Y green.
 *   Transform  moves bodies (mate connectors included) from an existing mate connector's frame to the defined frame.
 *
 * ORIGIN: a point or mate connector; a place on an edge or wire (nearest to something, a distance from an end, a distance
 * from a point toward or away from something); or a place on a face or surface (nearest to something).
 * AXES: Z and X each come from a world axis, a vector, or the tangent or the normal of an edge or face (taken where that
 * geometry is nearest the origin). X is squared up to Z; Y = Z x X. An optional direction disambiguates the two cases that
 * have a free direction: the tangent of a FACE (a direction in its tangent plane) and the normal of an EDGE (a direction
 * square to it). Without one, the face's u direction and the edge's principal normal are used.
 * MODIFY: moves along and turns about the frame's own axes, in that order.
 */

export enum McpMode
{
    annotation { "Name" : "New" }
    NEW,
    annotation { "Name" : "Build" }
    BUILD,
    annotation { "Name" : "Transform" }
    TRANSFORM
}

export enum McpOriginType
{
    annotation { "Name" : "Point" }
    POINT,
    annotation { "Name" : "Edge or wire" }
    EDGE,
    annotation { "Name" : "Face or surface" }
    FACE
}

export enum McpEdgeMethod
{
    annotation { "Name" : "Nearest to" }
    NEAREST,
    annotation { "Name" : "Distance from an end" }
    DISTANCE_FROM_END,
    annotation { "Name" : "Distance from a point" }
    DISTANCE_FROM_POINT
}

export enum McpFaceMethod
{
    annotation { "Name" : "Nearest to" }
    NEAREST
}

export enum McpDirection
{
    annotation { "Name" : "Toward" }
    TOWARD,
    annotation { "Name" : "Away from" }
    AWAY_FROM
}

export enum McpAxisType
{
    annotation { "Name" : "World axis" }
    WORLD,
    annotation { "Name" : "Vector" }
    VECTOR,
    annotation { "Name" : "Tangent" }
    TANGENT,
    annotation { "Name" : "Normal" }
    NORMAL
}

export enum McpXAxisType
{
    annotation { "Name" : "Automatic" }
    AUTOMATIC,
    annotation { "Name" : "World axis" }
    WORLD,
    annotation { "Name" : "Vector" }
    VECTOR,
    annotation { "Name" : "Tangent" }
    TANGENT,
    annotation { "Name" : "Normal" }
    NORMAL
}

export enum McpWorldAxis
{
    annotation { "Name" : "X" }
    X_AXIS,
    annotation { "Name" : "Y" }
    Y_AXIS,
    annotation { "Name" : "Z" }
    Z_AXIS
}

const MCP_EDGE_DISTANCE_BOUNDS = { (meter) : [0, 0.01, 500], (centimeter) : 1.0, (millimeter) : 10.0, (inch) : 0.4 } as LengthBoundSpec;
const MCP_OFFSET_BOUNDS = { (meter) : [-500, 0, 500], (centimeter) : 0.0, (millimeter) : 0.0, (inch) : 0.0 } as LengthBoundSpec;
const MCP_BUILD_BOUNDS = { (meter) : [0.00001, 0.025, 500], (centimeter) : 2.5, (millimeter) : 25.0, (inch) : 1.0 } as LengthBoundSpec;
const MCP_ROTATION_BOUNDS = { (degree) : [-360, 0, 360], (radian) : 0.0 } as AngleBoundSpec;
const MCP_COMPONENT_BOUNDS = { (unitless) : [-100000, 0, 100000] } as RealBoundSpec;
const MCP_COMPONENT_Z_BOUNDS = { (unitless) : [-100000, 1, 100000] } as RealBoundSpec;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Mate connector +",
        "Feature Type Description" : "Defines a mate connector frame precisely and then creates it, builds a colored half-cube marker (blue Z, red X, green Y), or transforms bodies from another mate connector to it. The origin can be a point, a place on an edge or wire, or a place on a face. Z and X come from a world axis, a vector, or the tangent or normal of an edge or face; X is squared up to Z. Modify moves and turns the frame about its own axes." }
export const mateConnectorPlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : McpMode.NEW,
                    "Description" : "New creates the mate connector. Build makes a surface marker (three colored squares). Transform moves bodies from another mate connector to this frame." }
        definition.mode is McpMode;

        annotation { "Group Name" : "Origin", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Origin on", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : McpOriginType.POINT,
                        "Description" : "Where the origin comes from: a point or mate connector, a place on an edge or wire, or a place on a face or surface." }
            definition.originType is McpOriginType;

            if (definition.originType == McpOriginType.POINT)
            {
                annotation { "Name" : "Point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "A vertex or mate connector. The origin is exactly this point." }
                definition.originPoint is Query;
            }
            else if (definition.originType == McpOriginType.EDGE)
            {
                annotation { "Name" : "Edge or wire", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                            "Description" : "Edges that form one chain, or a wire body. The origin lies on this curve." }
                definition.originEdges is Query;

                annotation { "Name" : "Method", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : McpEdgeMethod.NEAREST,
                            "Description" : "Nearest to: the point of the curve nearest something. Distance from an end: measured along the whole wire from its end nearest something. Distance from a point: measured along the wire from the point nearest something, toward or away from something else." }
                definition.edgeMethod is McpEdgeMethod;

                if (definition.edgeMethod == McpEdgeMethod.NEAREST)
                {
                    annotation { "Name" : "Nearest to", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "A point, mate connector, edge, face or body. The origin is the point of the curve nearest it." }
                    definition.edgeNearTo is Query;
                }
                else if (definition.edgeMethod == McpEdgeMethod.DISTANCE_FROM_END)
                {
                    annotation { "Name" : "End nearest to", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "Picks the end of the wire: the end nearest this entity." }
                    definition.edgeEndNear is Query;

                    annotation { "Name" : "Distance from the end", "Description" : "Arc length measured along the whole wire from that end." }
                    isLength(definition.edgeEndDistance, MCP_EDGE_DISTANCE_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "From the point nearest", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "The start: the point of the curve nearest this entity." }
                    definition.edgeFromEntity is Query;

                    annotation { "Name" : "Distance", "Description" : "Arc length measured along the wire from the start." }
                    isLength(definition.edgeFromDistance, MCP_EDGE_DISTANCE_BOUNDS);

                    annotation { "Name" : "Direction", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Default" : McpDirection.TOWARD,
                                "Description" : "Move toward or away from the entity below. This fixes which way along the wire the distance runs." }
                    definition.edgeDirection is McpDirection;

                    annotation { "Name" : "Entity", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "A point, mate connector, edge, face or body that the origin moves toward or away from." }
                    definition.edgeDirectionEntity is Query;
                }
            }
            else
            {
                annotation { "Name" : "Face or surface", "Filter" : EntityType.FACE || (EntityType.BODY && BodyType.SHEET),
                            "Description" : "Faces, or a surface body. The origin lies on these faces." }
                definition.originFaces is Query;

                annotation { "Name" : "Method", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : McpFaceMethod.NEAREST,
                            "Description" : "Nearest to: the point of the faces nearest something." }
                definition.faceMethod is McpFaceMethod;

                annotation { "Name" : "Nearest to", "Filter" : EntityType.VERTEX || EntityType.EDGE || EntityType.FACE || EntityType.BODY || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "A point, mate connector, edge, face or body. The origin is the point of the faces nearest it." }
                definition.faceNearTo is Query;
            }
        }

        annotation { "Group Name" : "Z axis", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Z axis from", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : McpAxisType.WORLD,
                        "Description" : "A world axis, a vector, or the tangent or normal of an edge or face (taken where that geometry is nearest the origin)." }
            definition.zType is McpAxisType;

            if (definition.zType == McpAxisType.WORLD)
            {
                annotation { "Name" : "World axis", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Default" : McpWorldAxis.Z_AXIS,
                            "Description" : "The mate connector's Z axis is parallel to this world axis (use Flip for the negative direction)." }
                definition.zWorld is McpWorldAxis;
            }
            else if (definition.zType == McpAxisType.VECTOR)
            {
                annotation { "Name" : "Vector X", "Description" : "X component of the direction. Only the direction matters, not the length." }
                isReal(definition.zVecX, MCP_COMPONENT_BOUNDS);
                annotation { "Name" : "Vector Y", "Description" : "Y component of the direction." }
                isReal(definition.zVecY, MCP_COMPONENT_BOUNDS);
                annotation { "Name" : "Vector Z", "Description" : "Z component of the direction." }
                isReal(definition.zVecZ, MCP_COMPONENT_Z_BOUNDS);
            }
            else
            {
                annotation { "Name" : "Edge, wire, face or surface", "Filter" : EntityType.EDGE || EntityType.FACE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.SHEET), "MaxNumberOfPicks" : 1,
                            "Description" : "Tangent: along an edge, or in the tangent plane of a face. Normal: the normal of a face, or square to an edge. Taken where the geometry is nearest the origin." }
                definition.zGeometry is Query;

                annotation { "Name" : "Disambiguating direction (optional)", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "Only needed for the tangent of a face or the normal of an edge. The direction is this one projected into the face's tangent plane, or square to the edge." }
                definition.zHint is Query;
            }

            annotation { "Name" : "Flip Z", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Reverse the Z axis." }
            definition.zFlip is boolean;
        }

        annotation { "Group Name" : "X axis", "Collapsed By Default" : true }
        {
            annotation { "Name" : "X axis from", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : McpXAxisType.AUTOMATIC,
                        "Description" : "Automatic uses the world X axis (or Y if that is parallel to Z). Otherwise as for Z. The result is always squared up to Z." }
            definition.xType is McpXAxisType;

            if (definition.xType == McpXAxisType.WORLD)
            {
                annotation { "Name" : "World axis", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Default" : McpWorldAxis.X_AXIS,
                            "Description" : "The X axis is this world axis, squared up to Z." }
                definition.xWorld is McpWorldAxis;
            }
            else if (definition.xType == McpXAxisType.VECTOR)
            {
                annotation { "Name" : "Vector X", "Description" : "X component of the direction." }
                isReal(definition.xVecX, MCP_COMPONENT_Z_BOUNDS);
                annotation { "Name" : "Vector Y", "Description" : "Y component of the direction." }
                isReal(definition.xVecY, MCP_COMPONENT_BOUNDS);
                annotation { "Name" : "Vector Z", "Description" : "Z component of the direction." }
                isReal(definition.xVecZ, MCP_COMPONENT_BOUNDS);
            }
            else if (definition.xType == McpXAxisType.TANGENT || definition.xType == McpXAxisType.NORMAL)
            {
                annotation { "Name" : "Edge, wire, face or surface", "Filter" : EntityType.EDGE || EntityType.FACE || (EntityType.BODY && BodyType.WIRE) || (EntityType.BODY && BodyType.SHEET), "MaxNumberOfPicks" : 1,
                            "Description" : "Tangent: along an edge, or in the tangent plane of a face. Normal: the normal of a face, or square to an edge." }
                definition.xGeometry is Query;

                annotation { "Name" : "Disambiguating direction (optional)", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "Only needed for the tangent of a face or the normal of an edge." }
                definition.xHint is Query;
            }

            annotation { "Name" : "Flip X", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Reverse the X axis." }
            definition.xFlip is boolean;
        }

        annotation { "Group Name" : "Modify", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Move along X", "Description" : "Move the origin along the frame's X axis." }
            isLength(definition.modOffsetX, MCP_OFFSET_BOUNDS);
            annotation { "Name" : "Move along Y", "Description" : "Move the origin along the frame's Y axis." }
            isLength(definition.modOffsetY, MCP_OFFSET_BOUNDS);
            annotation { "Name" : "Move along Z", "Description" : "Move the origin along the frame's Z axis." }
            isLength(definition.modOffsetZ, MCP_OFFSET_BOUNDS);
            annotation { "Name" : "Rotate about X", "Description" : "Turn the frame about its own X axis (after the moves)." }
            isAngle(definition.modRotX, MCP_ROTATION_BOUNDS);
            annotation { "Name" : "Rotate about Y", "Description" : "Turn the frame about its own (new) Y axis." }
            isAngle(definition.modRotY, MCP_ROTATION_BOUNDS);
            annotation { "Name" : "Rotate about Z", "Description" : "Turn the frame about its own (new) Z axis." }
            isAngle(definition.modRotZ, MCP_ROTATION_BOUNDS);
        }

        if (definition.mode == McpMode.BUILD)
        {
            annotation { "Name" : "Side length", "Description" : "Side of each square. Three squares meet at the origin: blue normal to Z, red normal to X, green normal to Y." }
            isLength(definition.buildLength, MCP_BUILD_BOUNDS);
        }
        else if (definition.mode == McpMode.TRANSFORM)
        {
            annotation { "Name" : "From mate connector", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "The frame the bodies currently sit in. They are moved so this frame lands on the defined one." }
            definition.fromConnector is Query;

            annotation { "Name" : "Bodies to transform", "Filter" : EntityType.BODY && SketchObject.NO,
                        "Description" : "Bodies to move, including mate connectors. The 'from' connector may be one of them, or the one used to define this frame." }
            definition.transformBodies is Query;
        }
    }
    {
        // Everything is evaluated before anything moves, so a connector used to define the frame may also be transformed.
        const origin = mcpOrigin(context, definition);
        const frame = mcpFrame(context, definition, origin);
        if (frame.note != "")
        {
            reportFeatureInfo(context, id, frame.note);
        }
        const cs = frame.cSys;

        if (definition.mode == McpMode.NEW)
        {
            opMateConnector(context, id + "mateConnector", { "coordSystem" : cs });
        }
        else if (definition.mode == McpMode.BUILD)
        {
            mcpBuild(context, id, cs, definition.buildLength);
        }
        else
        {
            const fromMate = mcMateConnector(context, definition.fromConnector);
            if (isQueryEmpty(context, fromMate))
            {
                throw regenError("Select the mate connector the bodies currently sit in", ["fromConnector"]);
            }
            const bodies = qEntityFilter(definition.transformBodies, EntityType.BODY);
            if (isQueryEmpty(context, bodies))
            {
                throw regenError("Select the bodies to transform", ["transformBodies"]);
            }
            const fromCs = evMateConnector(context, { "mateConnector" : qNthElement(fromMate, 0) });
            opTransform(context, id + "transform", { "bodies" : bodies, "transform" : toWorld(cs) * fromWorld(fromCs) });
        }
    });

// ---------------------------------------------------------------------------------------------------------------------
// Origin
// ---------------------------------------------------------------------------------------------------------------------

/** Distance from `a` (a point or a query) to the point `b`. */
function mcpDistance(context is Context, a, b is Vector) returns ValueWithUnits
{
    if (a is Vector)
    {
        return norm(a - b);
    }
    return evDistance(context, { "side0" : a, "side1" : b }).distance;
}

function mcpOrigin(context is Context, definition is map) returns Vector
{
    if (definition.originType == McpOriginType.POINT)
    {
        return mcPointOf(context, definition.originPoint, "originPoint");
    }

    if (definition.originType == McpOriginType.FACE)
    {
        const faces = qUnion([qEntityFilter(definition.originFaces, EntityType.FACE),
                        qOwnedByBody(qEntityFilter(definition.originFaces, EntityType.BODY), EntityType.FACE)]);
        if (isQueryEmpty(context, faces))
        {
            throw regenError("Select the face or surface the origin lies on", ["originFaces"]);
        }
        const near = mcAsGeometry(context, definition.faceNearTo, "faceNearTo");
        return evDistance(context, { "side0" : near, "side1" : faces }).sides[1].point;
    }

    // ---- edge or wire ----
    const edges = mcEdgesOf(context, definition.originEdges);
    if (isQueryEmpty(context, edges))
    {
        throw regenError("Select the edge or wire the origin lies on", ["originEdges"]);
    }
    if (definition.edgeMethod == McpEdgeMethod.NEAREST)
    {
        const near = mcAsGeometry(context, definition.edgeNearTo, "edgeNearTo");
        return evDistance(context, { "side0" : near, "side1" : edges }).sides[1].point;
    }

    const path = mcPath(context, edges, "originEdges");
    const length = evPathLength(context, path);
    if (definition.edgeMethod == McpEdgeMethod.DISTANCE_FROM_END)
    {
        const first = mcPathPoint(context, path, 0);
        const last = mcPathPoint(context, path, 1);
        if (norm(first - last) < TOLERANCE.zeroLength * meter)
        {
            throw regenError("A closed curve has no ends. Use Distance from a point instead.", ["originEdges"]);
        }
        if (definition.edgeEndDistance > length)
        {
            throw regenError("The distance (" ~ roundToPrecision(definition.edgeEndDistance / millimeter, 3) ~ " mm) is longer than the curve ("
                ~ roundToPrecision(length / millimeter, 3) ~ " mm)", ["edgeEndDistance"]);
        }
        const near = mcAsGeometry(context, definition.edgeEndNear, "edgeEndNear");
        const fromFirst = mcpDistance(context, near, first) <= mcpDistance(context, near, last);
        return mcPathPoint(context, path, fromFirst ? definition.edgeEndDistance / length : 1 - definition.edgeEndDistance / length);
    }

    // distance from a point, toward or away from something
    const startEntity = mcAsGeometry(context, definition.edgeFromEntity, "edgeFromEntity");
    const start = evDistance(context, { "side0" : startEntity, "side1" : edges }).sides[1].point;
    const startFraction = mcFractionOfPoint(context, path, start);
    const dir = mcAsGeometry(context, definition.edgeDirectionEntity, "edgeDirectionEntity");
    const probe = min(definition.edgeFromDistance, length * 0.02) / length;
    const plusFraction = min(1, startFraction + probe);
    const minusFraction = max(0, startFraction - probe);
    const plusDistance = mcpDistance(context, dir, mcPathPoint(context, path, plusFraction));
    const minusDistance = mcpDistance(context, dir, mcPathPoint(context, path, minusFraction));
    const towardPlus = plusDistance < minusDistance;
    const sign = (definition.edgeDirection == McpDirection.TOWARD) == towardPlus ? 1 : -1;
    var f = startFraction + sign * definition.edgeFromDistance / length;
    if (path.closed)
    {
        f = f - floor(f);
    }
    else if (f < 0 || f > 1)
    {
        throw regenError("The distance (" ~ roundToPrecision(definition.edgeFromDistance / millimeter, 3) ~ " mm) runs past the end of the curve: the start is "
            ~ roundToPrecision(startFraction * length / millimeter, 3) ~ " mm along a " ~ roundToPrecision(length / millimeter, 3) ~ " mm curve, going "
            ~ (sign > 0 ? "toward its end" : "toward its start"), ["edgeFromDistance"]);
    }
    return mcPathPoint(context, path, f);
}

// ---------------------------------------------------------------------------------------------------------------------
// Axes and the frame
// ---------------------------------------------------------------------------------------------------------------------

function mcpWorld(axis is McpWorldAxis) returns Vector
{
    if (axis == McpWorldAxis.X_AXIS)
    {
        return vector(1, 0, 0);
    }
    if (axis == McpWorldAxis.Y_AXIS)
    {
        return vector(0, 1, 0);
    }
    return vector(0, 0, 1);
}

/** `v` with its component along `n` removed, normalized; undefined when nothing is left. */
function mcpSquareTo(v is Vector, n is Vector)
{
    const rest = v - n * dot(v, n);
    if (norm(rest) < 1e-6)
    {
        return undefined;
    }
    return normalize(rest);
}

/**
 * The direction one axis definition gives at `origin` (before flip). `role` is "Z" or "X" for messages.
 */
function mcpAxisDirection(context is Context, kind, world, vec is Vector, geometry, hint, origin is Vector, role is string) returns Vector
{
    if (kind == "WORLD")
    {
        return mcpWorld(world);
    }
    if (kind == "VECTOR")
    {
        if (norm(vec) < 1e-9)
        {
            throw regenError("The " ~ role ~ " vector is zero", []);
        }
        return normalize(vec);
    }

    if (isQueryEmpty(context, geometry))
    {
        throw regenError("Select the edge, wire, face or surface for the " ~ role ~ " axis", []);
    }
    var hintDirection = undefined;
    if (!isQueryEmpty(context, hint))
    {
        hintDirection = mcDirectionOf(context, hint, "");
    }

    const faces = qUnion([qEntityFilter(geometry, EntityType.FACE), qOwnedByBody(qEntityFilter(geometry, EntityType.BODY), EntityType.FACE)]);
    if (!isQueryEmpty(context, faces))
    {
        const hit = evDistance(context, { "side0" : origin, "side1" : faces });
        const plane = evFaceTangentPlane(context, { "face" : evaluateQuery(context, faces)[hit.sides[1].index], "parameter" : hit.sides[1].parameter });
        if (kind == "NORMAL")
        {
            return plane.normal;
        }
        if (hintDirection != undefined)
        {
            const projected = mcpSquareTo(hintDirection, plane.normal);
            if (projected == undefined)
            {
                throw regenError("The disambiguating direction for the " ~ role ~ " axis is along the face normal, so it gives no direction in the face", []);
            }
            return projected;
        }
        return normalize(plane.x);
    }

    const edges = mcEdgesOf(context, geometry);
    const hit = evDistance(context, { "side0" : origin, "side1" : edges });
    const edge = evaluateQuery(context, edges)[hit.sides[1].index];
    const p = hit.sides[1].parameter;
    const tangent = normalize(evEdgeTangentLine(context, { "edge" : edge, "parameter" : p }).direction);
    if (kind == "TANGENT")
    {
        return tangent;
    }
    // normal of an edge: the disambiguating direction squared to it, else the principal normal (toward the center of curvature)
    if (hintDirection != undefined)
    {
        const projected = mcpSquareTo(hintDirection, tangent);
        if (projected == undefined)
        {
            throw regenError("The disambiguating direction for the " ~ role ~ " axis is along the edge, so it gives no normal", []);
        }
        return projected;
    }
    const lines = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [max(0, p - 0.01), min(1, p + 0.01)] });
    const principal = mcpSquareTo(lines[1].direction - lines[0].direction, tangent);
    if (principal == undefined)
    {
        throw regenError("The edge is straight there, so its normal is not defined for the " ~ role ~ " axis. Add a disambiguating direction.", []);
    }
    return principal;
}

/** A vector from three components that may be undefined (the parameters of an unused branch). */
function mcpVec(x, y, z) returns Vector
{
    if (x == undefined || y == undefined || z == undefined)
    {
        return vector(0, 0, 1);
    }
    return vector(x, y, z);
}

/** Turn `v` about the unit axis `a` by `angle` (Rodrigues). */
function mcpRotate(v is Vector, a is Vector, angle is ValueWithUnits) returns Vector
{
    return v * cos(angle) + cross(a, v) * sin(angle) + a * dot(a, v) * (1 - cos(angle));
}

/**
 * The frame: Z, then X squared to Z, Y = Z x X; then the Modify moves and turns.
 * @returns {{ @field cSys {CoordSystem} @field note {string} : what was approximated }}
 */
function mcpFrame(context is Context, definition is map, origin is Vector) returns map
{
    var notes = [];
    var zDirection = mcpAxisDirection(context, definition.zType == McpAxisType.WORLD ? "WORLD" : (definition.zType == McpAxisType.VECTOR ? "VECTOR" : (definition.zType == McpAxisType.TANGENT ? "TANGENT" : "NORMAL")),
            definition.zWorld, mcpVec(definition.zVecX, definition.zVecY, definition.zVecZ), definition.zGeometry, definition.zHint, origin, "Z");
    if (definition.zFlip)
    {
        zDirection = -zDirection;
    }

    var xRaw = vector(1, 0, 0);
    if (definition.xType == McpXAxisType.AUTOMATIC)
    {
        if (abs(dot(xRaw, zDirection)) > 0.999999)
        {
            xRaw = vector(0, 1, 0);
        }
    }
    else
    {
        const kind = definition.xType == McpXAxisType.WORLD ? "WORLD" : (definition.xType == McpXAxisType.VECTOR ? "VECTOR" : (definition.xType == McpXAxisType.TANGENT ? "TANGENT" : "NORMAL"));
        xRaw = mcpAxisDirection(context, kind, definition.xWorld, mcpVec(definition.xVecX, definition.xVecY, definition.xVecZ), definition.xGeometry, definition.xHint, origin, "X");
        if (definition.xFlip)
        {
            xRaw = -xRaw;
        }
    }
    const xAxis = mcpSquareTo(xRaw, zDirection);
    if (xAxis == undefined)
    {
        throw regenError("The X axis is parallel to the Z axis, so the frame is not defined. Choose a different X axis.", []);
    }
    const off = abs(angleBetween(xRaw, zDirection) / degree - 90);
    if (definition.xType != McpXAxisType.AUTOMATIC && off > 0.01)
    {
        notes = append(notes, "The X axis was " ~ roundToPrecision(off, 3) ~ " degrees off square to Z and was squared up.");
    }

    // Modify: move along the frame's own axes, then turn about them in turn (X, then the new Y, then the new Z).
    var x = xAxis;
    var z = zDirection;
    var y = cross(z, x);
    var o = origin + x * definition.modOffsetX + y * definition.modOffsetY + z * definition.modOffsetZ;
    if (definition.modRotX != 0 * degree)
    {
        y = mcpRotate(y, x, definition.modRotX);
        z = mcpRotate(z, x, definition.modRotX);
    }
    if (definition.modRotY != 0 * degree)
    {
        z = mcpRotate(z, y, definition.modRotY);
        x = mcpRotate(x, y, definition.modRotY);
    }
    if (definition.modRotZ != 0 * degree)
    {
        x = mcpRotate(x, z, definition.modRotZ);
        y = mcpRotate(y, z, definition.modRotZ);
    }
    return { "cSys" : coordSystem(o, normalize(x), normalize(z)), "note" : join(notes, " ") };
}

// ---------------------------------------------------------------------------------------------------------------------
// Build: three squares as one surface body
// ---------------------------------------------------------------------------------------------------------------------

function mcpSquare(context is Context, id is Id, corner is Vector, a is Vector, b is Vector, length is ValueWithUnits)
{
    const surface = bSplineSurface({ "uDegree" : 1, "vDegree" : 1, "isUPeriodic" : false, "isVPeriodic" : false,
                "controlPoints" : controlPointMatrix([[corner, corner + b * length], [corner + a * length, corner + a * length + b * length]]) });
    opCreateBSplineSurface(context, id, { "bSplineSurface" : surface });
}

function mcpBuild(context is Context, id is Id, cs is CoordSystem, length is ValueWithUnits)
{
    const o = cs.origin;
    const ex = cs.xAxis;
    const ey = yAxis(cs);
    const ez = cs.zAxis;
    mcpSquare(context, id + "zFace", o, ex, ey, length);
    mcpSquare(context, id + "xFace", o, ey, ez, length);
    mcpSquare(context, id + "yFace", o, ez, ex, length);

    opBoolean(context, id + "sew", {
                "tools" : qUnion([qCreatedBy(id + "zFace", EntityType.BODY), qCreatedBy(id + "xFace", EntityType.BODY), qCreatedBy(id + "yFace", EntityType.BODY)]),
                "operationType" : BooleanOperationType.UNION
            });

    // Color each face by the axis it is normal to: Z blue, X red, Y green.
    const faces = qOwnedByBody(qCreatedBy(id + "zFace", EntityType.BODY), EntityType.FACE);
    for (var face in evaluateQuery(context, faces))
    {
        const n = evFaceTangentPlane(context, { "face" : face, "parameter" : vector(0.5, 0.5) }).normal;
        var paint = undefined;
        if (abs(dot(n, ez)) > 0.99)
        {
            paint = color(0.09, 0.32, 0.69);
        }
        else if (abs(dot(n, ex)) > 0.99)
        {
            paint = color(0.85, 0.12, 0.12);
        }
        else if (abs(dot(n, ey)) > 0.99)
        {
            paint = color(0.12, 0.65, 0.22);
        }
        if (paint != undefined)
        {
            setProperty(context, { "entities" : face, "propertyType" : PropertyType.APPEARANCE, "value" : paint });
        }
    }
}
