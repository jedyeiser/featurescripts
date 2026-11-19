FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");
import(path : "onshape/std/path.fs", version : "3095.0");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractableVariable, extractableQuery)
import(path : "a47f90bfa6b17a59e20cebd0/f4f872fe20d1498201fed64d/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

/**
 * Multi mate connector.
 *
 * Places N mate connectors evenly along a wire (or a chain of edges). Each connector's Z axis follows the normal of a
 * surface (the closest point on the faces you give), its X axis follows the curve, and the normals can be kept consistent
 * where the faces' own normals flip, so the connectors do not jump from one side of a surface to the other.
 *
 * Spacing: both ends included (N connectors, the first at the start and the last at the end, N - 1 equal spans), or
 * centered (N equal spans, a connector in the middle of each). A closed curve has no ends: N equal spans.
 *
 * Normals (when taken from a surface): the closest point of the faces to each station gives a face normal. Faces can
 * point opposite ways (a patched surface, a mirrored half, a seam), and the closest point can hop between faces, so the raw
 * normals can flip along the curve. "Consistent along the curve" flips a normal whenever it points against the previous
 * one; "Toward a reference point" and "Along a reference direction" choose the side directly; "Exactly as the faces" keeps
 * the raw normals. "Flip" reverses every connector.
 */

export enum MmcSpacing
{
    annotation { "Name" : "Both ends included" }
    INCLUDE_ENDS,
    annotation { "Name" : "Centered in equal spans" }
    CENTERED
}

export enum MmcNormalSource
{
    annotation { "Name" : "Surface or faces" }
    SURFACE,
    annotation { "Name" : "A fixed direction" }
    DIRECTION
}

export enum MmcNormalCorrection
{
    annotation { "Name" : "Consistent along the curve" }
    CONSISTENT,
    annotation { "Name" : "Toward a reference point" }
    TOWARD_POINT,
    annotation { "Name" : "Along a reference direction" }
    TOWARD_DIRECTION,
    annotation { "Name" : "Exactly as the faces" }
    AS_FACES
}

const MMC_COUNT_BOUNDS = { (unitless) : [1, 5, 200] } as IntegerBoundSpec;

annotation { "Feature Type Name" : "Multi mate connector",
        "Feature Type Description" : "Places N mate connectors evenly along a wire or edges. Each Z axis follows the normal of a surface or faces you pick (the closest point to each connector), and the X axis follows the curve. Normals can be kept consistent along the curve, or forced toward a reference point or direction, so faces whose normals flip do not turn connectors over. Flip reverses them all." }
export const multiMateConnector = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Curves", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                    "Description" : "A wire (each selected wire gets its own N connectors) or edges that form one continuous chain." }
        definition.curves is Query;

        annotation { "Name" : "Count", "Description" : "Number of mate connectors along each curve." }
        isInteger(definition.count, MMC_COUNT_BOUNDS);

        annotation { "Name" : "Spacing", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : MmcSpacing.INCLUDE_ENDS,
                    "Description" : "Both ends included: the first connector is at the start and the last at the end. Centered: the curve is cut into N equal spans with a connector in the middle of each. A closed curve is always N equal spans." }
        definition.spacingMode is MmcSpacing;

        annotation { "Name" : "Reverse curve direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false,
                    "Description" : "Start from the other end of the curve. Changes which end is first and which way the X axes point." }
        definition.reverseCurve is boolean;

        annotation { "Name" : "Z axis from", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : MmcNormalSource.SURFACE,
                    "Description" : "A surface or faces (the normal at the point closest to each connector) or one fixed direction." }
        definition.normalSource is MmcNormalSource;

        if (definition.normalSource == MmcNormalSource.SURFACE)
        {
            annotation { "Name" : "Surface or faces", "Filter" : EntityType.FACE || (EntityType.BODY && BodyType.SHEET),
                        "Description" : "Faces, or a surface body (all its faces). Each connector takes the normal of the closest point on these faces." }
            definition.normalFaces is Query;

            annotation { "Name" : "Normals", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : MmcNormalCorrection.CONSISTENT,
                        "Description" : "Consistent along the curve: a normal that points against the previous one is flipped. Toward a reference point or along a reference direction: every normal is flipped to that side. Exactly as the faces: no correction." }
            definition.normalCorrection is MmcNormalCorrection;

            if (definition.normalCorrection == MmcNormalCorrection.TOWARD_POINT)
            {
                annotation { "Name" : "Reference point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "A vertex or mate connector. Each normal is flipped, if needed, so that it points toward this point." }
                definition.referencePoint is Query;
            }
            else if (definition.normalCorrection == MmcNormalCorrection.TOWARD_DIRECTION)
            {
                annotation { "Name" : "Reference direction", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "An edge, planar face, cylinder axis or mate connector (Z axis). Each normal is flipped, if needed, so that it points along this direction." }
                definition.referenceDirection is Query;
            }
        }
        else
        {
            annotation { "Name" : "Direction", "Filter" : EntityType.EDGE || QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "An edge, planar face, cylinder axis or mate connector (Z axis). Every connector's Z axis points exactly this way; the X axes still follow the curve, squared to Z." }
            definition.zDirection is Query;
        }

        annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false,
                    "Description" : "Reverse the Z axis of every mate connector." }
        definition.flipNormals is boolean;
    }
    {
        if (isQueryEmpty(context, definition.curves))
        {
            throw regenError("Select a wire or edges", ["curves"]);
        }

        // ---- where the Z axes come from ----
        var faceQuery = qNothing();
        var faces = [];
        var referencePoint = undefined;
        var referenceDirection = undefined;
        var fixedDirection = undefined;
        if (definition.normalSource == MmcNormalSource.SURFACE)
        {
            faceQuery = qUnion([qEntityFilter(definition.normalFaces, EntityType.FACE),
                            qOwnedByBody(qEntityFilter(definition.normalFaces, EntityType.BODY), EntityType.FACE)]);
            faces = evaluateQuery(context, faceQuery);
            if (size(faces) == 0)
            {
                throw regenError("Select the surface or faces the Z axes should follow", ["normalFaces"]);
            }
            if (definition.normalCorrection == MmcNormalCorrection.TOWARD_POINT)
            {
                referencePoint = mmcPoint(context, definition.referencePoint, "referencePoint");
            }
            else if (definition.normalCorrection == MmcNormalCorrection.TOWARD_DIRECTION)
            {
                referenceDirection = mmcDirection(context, definition.referenceDirection, "referenceDirection");
            }
        }
        else
        {
            fixedDirection = mmcDirection(context, definition.zDirection, "zDirection");
        }

        // ---- one set of connectors per wire (edges picked loose form one chain) ----
        const groups = mmcGroups(context, definition.curves);
        var made = 0;
        var flipped = 0;
        var farthest = 0 * meter;
        var firstLength = undefined;
        var firstSpacing = undefined;
        var skippedAlong = 0;

        for (var g = 0; g < size(groups); g += 1)
        {
            var path = mmcPath(context, groups[g]);
            if (definition.reverseCurve)
            {
                path = reverse(path);
            }
            const length = evPathLength(context, path);
            const fractions = mmcFractions(definition.count, path.closed, definition.spacingMode);
            const lines = evPathTangentLines(context, path, fractions).tangentLines;

            var previousNormal = undefined;
            var previousX = undefined;
            for (var i = 0; i < size(lines); i += 1)
            {
                const origin = lines[i].origin;
                const tangent = normalize(lines[i].direction);

                var normal = fixedDirection;
                if (fixedDirection == undefined)
                {
                    const hit = evDistance(context, { "side0" : origin, "side1" : faceQuery });
                    normal = evFaceTangentPlane(context, { "face" : faces[hit.sides[1].index], "parameter" : hit.sides[1].parameter }).normal;
                    farthest = max(farthest, hit.distance);

                    var flip = false;
                    if (definition.normalCorrection == MmcNormalCorrection.CONSISTENT)
                    {
                        flip = previousNormal != undefined && dot(normal, previousNormal) < 0;
                    }
                    else if (definition.normalCorrection == MmcNormalCorrection.TOWARD_POINT)
                    {
                        flip = dot(normal, referencePoint - origin) < 0 * meter;
                    }
                    else if (definition.normalCorrection == MmcNormalCorrection.TOWARD_DIRECTION)
                    {
                        flip = dot(normal, referenceDirection) < 0;
                    }
                    if (flip)
                    {
                        normal = -normal;
                        flipped += 1;
                    }
                    previousNormal = normal;
                }
                if (definition.flipNormals)
                {
                    normal = -normal;
                }

                // X follows the curve, squared up to Z. Where the curve runs along the normal, keep the previous X.
                var xAxis = tangent - normal * dot(tangent, normal);
                if (norm(xAxis) < 1e-6)
                {
                    xAxis = previousX != undefined ? previousX : perpendicularVector(normal);
                    skippedAlong += 1;
                }
                xAxis = normalize(xAxis);
                previousX = xAxis;

                opMateConnector(context, id + ("g" ~ g) + ("s" ~ i), { "coordSystem" : coordSystem(origin, xAxis, normal) });
                made += 1;
            }
            if (firstLength == undefined)
            {
                firstLength = length;
                firstSpacing = size(lines) < 2 ? length : length * (fractions[1] - fractions[0]);
            }
        }

        var message = made ~ " mate connector" ~ (made == 1 ? "" : "s") ~ ", " ~ roundToPrecision(firstSpacing / millimeter, 3) ~ " mm apart.";
        if (flipped > 0)
        {
            message = message ~ " " ~ flipped ~ " normal" ~ (flipped == 1 ? " was" : "s were") ~ " flipped to keep the connectors on one side.";
        }
        if (fixedDirection == undefined)
        {
            message = message ~ " The curve is up to " ~ roundToPrecision(farthest / millimeter, 3) ~ " mm from the surface.";
        }
        if (skippedAlong > 0)
        {
            message = message ~ " " ~ skippedAlong ~ " connector" ~ (skippedAlong == 1 ? " has" : "s have") ~ " the curve running along its Z axis, so the previous X axis was kept there.";
        }
        reportFeatureInfo(context, id, message);

        // Values for a later Extract variables feature
        embedStandardOutputs(context, id, {
                    "output" : qCreatedBy(id, EntityType.BODY),
                    "outputDescription" : "The mate connectors",
                    "inputs" : definition.curves,
                    "variables" : {
                        "count" : extractableVariable(made, "Mate connectors made, over all curves."),
                        "spacing" : extractableVariable(firstSpacing, "Distance along the (first) curve between connectors."),
                        "pathLength" : extractableVariable(firstLength, "Length of the (first) curve."),
                        "flipped" : extractableVariable(flipped, "Normals that were flipped to keep the connectors consistent.")
                    },
                    "queries" : {}
                });
    }, { "count" : 5, "spacingMode" : MmcSpacing.INCLUDE_ENDS, "reverseCurve" : false, "normalSource" : MmcNormalSource.SURFACE,
         "normalCorrection" : MmcNormalCorrection.CONSISTENT, "flipNormals" : false });

// ---------------------------------------------------------------------------------------------------------------------
// Curves
// ---------------------------------------------------------------------------------------------------------------------

/**
 * The curves to work on, one edge query each: every selected wire body on its own, and the loose edges together as one chain.
 */
function mmcGroups(context is Context, curves is Query) returns array
{
    const bodies = qEntityFilter(curves, EntityType.BODY);
    var groups = [];
    for (var body in evaluateQuery(context, bodies))
    {
        groups = append(groups, qOwnedByBody(body, EntityType.EDGE));
    }
    const loose = qSubtraction(qEntityFilter(curves, EntityType.EDGE), qOwnedByBody(bodies, EntityType.EDGE));
    if (!isQueryEmpty(context, loose))
    {
        groups = append(groups, loose);
    }
    return groups;
}

/**
 * One ordered path through the edges. std constructPath refuses edges whose ends miss by more than 1e-8 m and throws a bare
 * string, so it is retried with 1, 10 and 100 um before giving up with a message.
 */
function mmcPath(context is Context, edges is Query) returns Path
{
    try silent
    {
        return constructPath(context, edges);
    }
    for (var tolerance in [0.000001, 0.00001, 0.0001])
    {
        try silent
        {
            return constructPath(context, edges, { "tolerance" : tolerance * meter }).path;
        }
    }
    throw regenError("The edges of this curve do not form one continuous chain: its " ~ size(evaluateQuery(context, edges))
        ~ " edges do not meet end to end within 0.1 mm, or the curve branches or touches itself.", ["curves"]);
}

/**
 * Path fractions (0 to 1, by arc length) of the stations.
 */
function mmcFractions(count is number, closed is boolean, mode is MmcSpacing) returns array
{
    var fractions = [];
    for (var i = 0; i < count; i += 1)
    {
        if (mode == MmcSpacing.CENTERED)
        {
            fractions = append(fractions, (i + 0.5) / count);
        }
        else if (closed)
        {
            fractions = append(fractions, i / count);
        }
        else if (count == 1)
        {
            fractions = append(fractions, 0.5);
        }
        else
        {
            fractions = append(fractions, i / (count - 1));
        }
    }
    return fractions;
}

// ---------------------------------------------------------------------------------------------------------------------
// References
// ---------------------------------------------------------------------------------------------------------------------

function mmcMateConnector(context is Context, q is Query) returns Query
{
    return qBodyType(qUnion([q, qOwnerBody(q)]), BodyType.MATE_CONNECTOR);
}

/** The point of a vertex, or the origin of a mate connector. */
function mmcPoint(context is Context, q is Query, field is string) returns Vector
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("Select the reference point", [field]);
    }
    const mc = mmcMateConnector(context, q);
    if (!isQueryEmpty(context, mc))
    {
        return evMateConnector(context, { "mateConnector" : qNthElement(mc, 0) }).origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/** The direction of an edge, planar face, cylinder axis, or the Z axis of a mate connector. */
function mmcDirection(context is Context, q is Query, field is string) returns Vector
{
    if (isQueryEmpty(context, q))
    {
        throw regenError("Select the reference direction", [field]);
    }
    const mc = mmcMateConnector(context, q);
    if (!isQueryEmpty(context, mc))
    {
        return evMateConnector(context, { "mateConnector" : qNthElement(mc, 0) }).zAxis;
    }
    const d = extractDirection(context, q);
    if (d == undefined)
    {
        throw regenError("A direction cannot be taken from this selection", [field]);
    }
    return normalize(d);
}
