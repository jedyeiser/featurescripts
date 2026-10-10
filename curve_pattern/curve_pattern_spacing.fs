FeatureScript 3095;
import(path : "onshape/std/common.fs", version : "3095.0");

/**
 * Curve pattern plus: spacing resolver.
 *
 * Pure geometry-free maths: given the length of a path and the user's spacing options, work out how many
 * instances there are, the spacing between them, and the arc-length distance of each one from the start of
 * the path. No kernel calls, so it can be tested in a single FeatureScript eval.
 *
 * The count / spacing / total-length / target-spacing / rounding model is adapted from "Linear pattern plus"
 * by Evan Reese (The Onsherpa, www.TheOnsherpa.com), Copyright (c) 2026 Evan Reese, used with permission
 * under the MIT License (https://opensource.org/licenses/MIT).
 */

export enum CurveSpacingType
{
    annotation { "Name" : "Spacing" }
    SPACING,
    annotation { "Name" : "Total length" }
    LENGTH
}

export enum CurveCountType
{
    annotation { "Name" : "Count" }
    COUNT,
    annotation { "Name" : "Target spacing" }
    TARGET_SPACING
}

export enum CurveRoundingType
{
    annotation { "Name" : "Exact spacing" }
    EXACT,
    annotation { "Name" : "Nearest value spacing" }
    ROUND,
    annotation { "Name" : "Maximum spacing" }
    UP,
    annotation { "Name" : "Minimum spacing" }
    DOWN
}

const OVERRUN_TOLERANCE = 1e-6 * meter;

/**
 * Resolve the instances of a curve pattern.
 *
 * Instance 0 is the seed and sits at `first`, the arc length from the start of the path of the first station.
 * Stations are `first + i * spacing`, i = 0 .. count - 1.
 *
 * @param p {{
 *      @field pathLength {ValueWithUnits} : Length of the whole path.
 *      @field closed {boolean} : Whether the path is closed (no end offset; the last instance is one spacing
 *              short of the start, so no instance doubles up on the first).
 *      @field spacingType {CurveSpacingType} : `SPACING` (distance between instances) or `LENGTH` (total length).
 *      @field fillPath {boolean} : With `LENGTH`, use the whole usable path as the total length.
 *      @field distance {ValueWithUnits} : The spacing, or the total length, per `spacingType`.
 *      @field countType {CurveCountType} : With `LENGTH`, set the number of instances by `COUNT` or `TARGET_SPACING`.
 *      @field count {number} : Number of instances (including the seed) for `SPACING`, or `LENGTH` with `COUNT`.
 *      @field targetSpacing {ValueWithUnits} : With `LENGTH` and `TARGET_SPACING`.
 *      @field roundingType {CurveRoundingType} : How `TARGET_SPACING` is rounded to a whole count.
 *      @field startOffset {ValueWithUnits} : Distance from the start of the path to the seed.
 *      @field endOffset {ValueWithUnits} : Distance kept clear at the end of an open path.
 *      @field centered {boolean} : Centre the instances in the usable length instead of starting at the offset.
 * }}
 * @returns {{
 *      @field count {number} : Number of instances including the seed.
 *      @field spacing {ValueWithUnits} : Distance between consecutive instances.
 *      @field span {ValueWithUnits} : Distance from the first to the last instance.
 *      @field usable {ValueWithUnits} : Path length between the offsets.
 *      @field first {ValueWithUnits} : Arc length of the seed from the start of the path.
 *      @field distances {array} : Arc length of every instance from the start of the path, seed first.
 * }}
 * @throws regenError naming `distance`, `count`, `targetSpacing`, `startOffset` or `endOffset`.
 */
export function resolveCurveSpacing(p is map) returns map
{
    const L = p.pathLength;
    const closed = p.closed == true;
    const startOffset = p.startOffset == undefined ? 0 * meter : p.startOffset;
    const endOffset = (closed || p.endOffset == undefined) ? 0 * meter : p.endOffset;
    const usable = closed ? L : L - startOffset - endOffset;
    if (usable < -OVERRUN_TOLERANCE)
    {
        throw regenError("The start and end offsets are longer than the path.", ["startOffset", "endOffset"]);
    }

    var count = p.count;
    var spacing = 0 * meter;
    var span = 0 * meter;

    if (p.spacingType == CurveSpacingType.SPACING)
    {
        if (count == undefined || count < 1)
        {
            throw regenError("The instance count must be at least 1.", ["count"]);
        }
        spacing = p.distance;
        span = spacing * (count - 1);
    }
    else
    {
        const total = p.fillPath == true ? usable : p.distance;
        if (total == undefined || total < 0 * meter)
        {
            throw regenError("The total length must not be negative.", ["distance"]);
        }
        if (p.countType == CurveCountType.TARGET_SPACING)
        {
            const target = p.targetSpacing;
            if (target == undefined || target <= TOLERANCE.zeroLength * meter)
            {
                throw regenError("The target spacing must be greater than zero.", ["targetSpacing"]);
            }
            const ratio = total / target;
            var steps = floor(ratio + 1e-9);
            if (p.roundingType == CurveRoundingType.ROUND)
            {
                steps = round(ratio);
            }
            else if (p.roundingType == CurveRoundingType.UP)
            {
                steps = ceil(ratio - 1e-9);
            }
            // a closed path has as many gaps as instances; an open one has one gap fewer
            count = closed && p.fillPath == true ? steps : steps + 1;
            if (count < 1)
            {
                count = 1;
            }
        }
        if (count == undefined || count < 1)
        {
            throw regenError("The instance count must be at least 1.", ["count"]);
        }
        if (count == 1)
        {
            spacing = 0 * meter;
        }
        else if (p.countType == CurveCountType.TARGET_SPACING && p.roundingType == CurveRoundingType.EXACT)
        {
            spacing = p.targetSpacing;
        }
        else
        {
            const gaps = (closed && p.fillPath == true) ? count : count - 1;
            spacing = total / gaps;
        }
        span = spacing * (count - 1);
    }

    var first = startOffset;
    if (p.centered == true)
    {
        first = startOffset + (usable - span) / 2;
    }

    if (!closed && first + span > L - endOffset + OVERRUN_TOLERANCE)
    {
        throw regenError("The pattern runs past the end of the path. Reduce the spacing, count or length.",
                p.spacingType == CurveSpacingType.SPACING ? ["distance", "count"] : ["distance"]);
    }
    if (!closed && first < -OVERRUN_TOLERANCE)
    {
        throw regenError("The pattern starts before the beginning of the path.", ["startOffset"]);
    }

    var distances = [];
    for (var i = 0; i < count; i += 1)
    {
        distances = append(distances, first + spacing * i);
    }

    return { "count" : count, "spacing" : spacing, "span" : span, "usable" : usable, "first" : first, "distances" : distances };
}
