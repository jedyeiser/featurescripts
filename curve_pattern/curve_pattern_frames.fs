FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

/**
 * Curve pattern plus: frames at the stations of a path.
 *
 * Onshape's own Curve pattern gets roll-free frames by sweeping a small sketch line along the path (kernel ops that
 * can fail). Here the frames are computed analytically: tangents come from one evPathTangentLines call, and the
 * normal is carried along the path by double-reflection rotation-minimizing transport (Wang et al., 2008), or
 * taken from a reference direction or a reference face. One evPathTangentLines call serves the whole path.
 *
 * Each frame is a CoordSystem with its z axis along the path tangent and its x axis along the normal. The
 * instances are placed with the transform that carries the first frame onto each later frame, so the seed is
 * treated as sitting at the first station.
 */

export enum CurveOrientationType
{
    annotation { "Name" : "Follow path (no roll)" }
    FOLLOW,
    annotation { "Name" : "Keep seed orientation" }
    KEEP,
    annotation { "Name" : "Reference direction" }
    DIRECTION,
    annotation { "Name" : "Reference face" }
    FACE
}

const DEGENERATE_NORMAL = 1e-6;
const MAX_STEPS_PER_SEGMENT = 200;
const STEPS_PER_PATH = 400;

/** The unit vector closest to `v` that is perpendicular to the unit vector `t`, or undefined if `v` is along `t`. */
function normalPerpendicularTo(v is Vector, t is Vector)
{
    const w = v - dot(v, t) * t;
    const len = norm(w);
    if (len < DEGENERATE_NORMAL)
    {
        return undefined;
    }
    return w / len;
}

/** Path fraction (0..1) of the point `d` along the path; wraps on a closed path, clamps on an open one. */
function pathFraction(d is ValueWithUnits, pathLength is ValueWithUnits, closed is boolean, reversed is boolean) returns number
{
    var u = d / pathLength;
    if (closed)
    {
        u = u - floor(u);
    }
    else
    {
        u = min(max(u, 0), 1);
    }
    if (reversed)
    {
        u = 1 - u;
        if (closed)
        {
            u = u - floor(u);
        }
    }
    return u;
}

/**
 * Frames at the stations of a path.
 *
 * @param path {Path}
 * @param pathLength {ValueWithUnits}
 * @param distances {array} : Arc length of each station from the start of the path, in station order.
 * @param options {{
 *      @field reversed {boolean} : Measure from the other end of the path.
 *      @field orientation {CurveOrientationType}
 *      @field direction {Vector} : Reference direction (unitless), for `DIRECTION`.
 *      @field face {Query} : Reference face, for `FACE`.
 *      @field flipFace {boolean} : Use the opposite side of the face normal.
 *      @field referenceGeometry {Query} : On a closed path, the point nearest this geometry is the start.
 * }}
 * @returns {{
 *      @field frames {array} : A [CoordSystem] per station (x = normal, z = path tangent).
 *      @field fallbacks {number} : Stations whose reference normal ran along the path, so the previous normal was carried.
 * }}
 */
export function curveStationFrames(context is Context, path is Path, pathLength is ValueWithUnits, distances is array,
        options is map) returns map
{
    const closed = path.closed;
    const reversed = options.reversed == true;
    const orientation = options.orientation;
    const stations = size(distances);

    // Sample the path: every station, plus extra samples between stations when the normal is carried along it.
    var sampleDistances = [];
    var stationIndex = [];
    for (var k = 0; k < stations; k += 1)
    {
        stationIndex = append(stationIndex, size(sampleDistances));
        sampleDistances = append(sampleDistances, distances[k]);
        if (orientation == CurveOrientationType.FOLLOW && k < stations - 1)
        {
            const gap = distances[k + 1] - distances[k];
            const steps = min(max(ceil(abs(gap) / (pathLength / STEPS_PER_PATH)), 1), MAX_STEPS_PER_SEGMENT);
            for (var j = 1; j < steps; j += 1)
            {
                sampleDistances = append(sampleDistances, distances[k] + gap * (j / steps));
            }
        }
    }

    var fractions = [];
    for (var d in sampleDistances)
    {
        fractions = append(fractions, pathFraction(d, pathLength, closed, reversed));
    }
    const tangentResult = evPathTangentLines(context, path, fractions,
        options.referenceGeometry == undefined ? qNothing() : options.referenceGeometry);

    var origins = [];
    var tangents = [];
    for (var line in tangentResult.tangentLines)
    {
        origins = append(origins, line.origin);
        tangents = append(tangents, reversed ? -line.direction : line.direction);
    }

    // Normal at each sample
    var normals = [];
    var fallbacks = 0;
    if (orientation == CurveOrientationType.FOLLOW || orientation == CurveOrientationType.KEEP)
    {
        var current = normalPerpendicularTo(perpendicularVector(tangents[0]), tangents[0]);
        normals = append(normals, current);
        for (var i = 1; i < size(origins); i += 1)
        {
            if (orientation == CurveOrientationType.KEEP)
            {
                normals = append(normals, current);
                continue;
            }
            // double-reflection step from sample i - 1 to sample i
            const v1 = (origins[i] - origins[i - 1]) / meter;
            const c1 = dot(v1, v1);
            var reflected = current;
            var reflectedTangent = tangents[i - 1];
            if (c1 > 1e-18)
            {
                reflected = current - (2 / c1) * dot(v1, current) * v1;
                reflectedTangent = tangents[i - 1] - (2 / c1) * dot(v1, tangents[i - 1]) * v1;
            }
            const v2 = tangents[i] - reflectedTangent;
            const c2 = dot(v2, v2);
            var next = reflected;
            if (c2 > 1e-18)
            {
                next = reflected - (2 / c2) * dot(v2, reflected) * v2;
            }
            // re-square against the tangent so rounding does not accumulate
            const squared = normalPerpendicularTo(next, tangents[i]);
            current = squared == undefined ? current : squared;
            normals = append(normals, current);
        }
    }
    else
    {
        var previous = undefined;
        for (var i = 0; i < size(origins); i += 1)
        {
            var reference;
            if (orientation == CurveOrientationType.DIRECTION)
            {
                reference = options.direction;
            }
            else
            {
                const near = evDistance(context, { "side0" : options.face, "side1" : origins[i], "arcLengthParameterization" : false });
                const plane = evFaceTangentPlane(context, { "face" : options.face, "parameter" : near.sides[0].parameter });
                reference = options.flipFace == true ? -plane.normal : plane.normal;
            }
            var n = normalPerpendicularTo(reference, tangents[i]);
            if (n == undefined)
            {
                fallbacks += 1;
                n = previous == undefined ? undefined : normalPerpendicularTo(previous, tangents[i]);
                if (n == undefined)
                {
                    n = normalPerpendicularTo(perpendicularVector(tangents[i]), tangents[i]);
                }
            }
            previous = n;
            normals = append(normals, n);
        }
    }

    var frames = [];
    for (var k = 0; k < stations; k += 1)
    {
        const s = stationIndex[k];
        // KEEP: every frame has the first frame's axes, so the transforms are pure translations
        const axes = orientation == CurveOrientationType.KEEP ? 0 : s;
        frames = append(frames, coordSystem(origins[s], normals[axes], tangents[axes]));
    }
    return { "frames" : frames, "fallbacks" : fallbacks };
}

/**
 * Transforms that carry the seed (at the first station) to every later station: one per station after the first.
 * With `keepOrientation` the transforms are pure translations.
 */
export function curveStationTransforms(frames is array, keepOrientation is boolean) returns array
{
    var transforms = [];
    const fromFirst = fromWorld(frames[0]);
    for (var i = 1; i < size(frames); i += 1)
    {
        transforms = append(transforms, keepOrientation
            ? transform(frames[i].origin - frames[0].origin)
            : toWorld(frames[i]) * fromFirst);
    }
    return transforms;
}

/** A mate connector frame at a station: z along the path tangent, or z along the normal (x then along the tangent). */
export function mateConnectorFrame(frame is CoordSystem, zAlongPath is boolean) returns CoordSystem
{
    return zAlongPath ? frame : coordSystem(frame.origin, frame.zAxis, frame.xAxis);
}
