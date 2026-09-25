FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "3070.0");
// IMPORT: Variable_tools V2 extract_outputs.fs (embedStandardOutputs, extractable wrappers)
import(path : "a47f90bfa6b17a59e20cebd0/f4f872fe20d1498201fed64d/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");
// IMPORT: create_offset_profile_icon.svg (feature icon)
IconNamespace::import(path : "5abec4cb3826cb3a41e6e340", version : "dff061882348551c0d057bb6");

/**
 * Create offset profile: builds the profile Driven edge offset and Driven offset surface read -- wires in
 * profile coordinates, X = station, Y = width offset, Z = height offset -- from a table of REGIONS or POINTS.
 * Design: research_create_offset_profile.md.
 *
 * The profile is data only. Where the offset jumps, or is not defined between two items, the profile BREAKS:
 * one wire per continuous piece. Whether and how a break is joined is the consumer's choice. The profile
 * starts at the first item and stops at the last.
 *
 * Every piece is exact: each sub-segment is a polynomial in x (a line, a smootherstep ramp, a Hermite blend),
 * written as an exact Bezier -- no sampling, no approximation tolerance. The sub-segments of a piece are
 * joined into one wire; continuity at each joint is what the inputs give (a buffer into a smooth ramp is
 * C2, a linear corner G0, a blend what was chosen).
 *
 * Any station (region start / end, point station) is a typed value or a picked vertex / mate connector: its
 * world X plus a signed distance along +X. The pick field allows creating a mate connector in place.
 *
 * Regions: start / end station, then either CONSTANT (one width, one height: a straight line) or start / end
 * width and height with a shape (linear or smooth) and optional buffers -- distances from each end toward the
 * centre over which the end value is held. Consecutive
 * regions join as they are when they touch with equal values; otherwise the profile breaks, unless their
 * intersection asks for a blend.
 *
 * Points: station, width and height, and how the profile runs to the next point: linear, smooth (flat at
 * both points, never overshoots), or hold (keep the value, then break). Two points at one station with
 * different values are a break.
 */

/** How the profile is specified. */
export enum OffsetProfileMode
{
    annotation { "Name" : "Regions" }
    REGIONS,
    annotation { "Name" : "Points" }
    POINTS
}

/** How a region's offset changes between its buffers. */
export enum OffsetProfileShape
{
    annotation { "Name" : "Constant" }
    CONSTANT,
    annotation { "Name" : "Linear" }
    LINEAR,
    annotation { "Name" : "Smooth" }
    SMOOTH
}

/** How the profile runs from one point to the next. */
export enum OffsetPointTransition
{
    annotation { "Name" : "Smooth" }
    SMOOTH,
    annotation { "Name" : "Linear" }
    LINEAR,
    annotation { "Name" : "Hold" }
    HOLD
}

/** Where a station comes from: a typed value, or a picked point's world X plus a signed distance along +X. */
export enum OffsetStationSource
{
    annotation { "Name" : "Value" }
    VALUE,
    annotation { "Name" : "Point" }
    POINT
}

/** Stations closer than this touch; offsets closer than this are equal. */
export const OFFSET_PROFILE_TOLERANCE = 1e-6 * meter;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Create offset profile",
        "Feature Type Description" : "Build an offset profile (X station, Y width, Z height) for Driven edge offset / Driven offset surface from regions or points.",
        "Editing Logic Function" : "createOffsetProfileEditingLogic" }
export const createOffsetProfile = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "Default" : OffsetProfileMode.REGIONS, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.mode is OffsetProfileMode;

        if (definition.mode == OffsetProfileMode.REGIONS)
        {
            annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#regionName",
                        "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
            definition.regions is array;
            for (var region in definition.regions)
            {
                annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64, "Description" : "Empty = Region n. Intersections are kept by region name." }
                region.regionName is string;

                annotation { "Name" : "Start from", "Default" : OffsetStationSource.VALUE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
                region.startSource is OffsetStationSource;

                if (region.startSource == OffsetStationSource.POINT)
                {
                    annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "Start station = this point's world X plus the distance below." }
                    region.startPoint is Query;

                    annotation { "Name" : "Start distance from point", "Description" : "Signed, along +X." }
                    isLength(region.startPointOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Start station", "Description" : "World X in profile coordinates." }
                    isLength(region.startStation, ZERO_DEFAULT_LENGTH_BOUNDS);
                }

                annotation { "Name" : "End from", "Default" : OffsetStationSource.VALUE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
                region.endSource is OffsetStationSource;

                if (region.endSource == OffsetStationSource.POINT)
                {
                    annotation { "Name" : "End point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "End station = this point's world X plus the distance below." }
                    region.endPoint is Query;

                    annotation { "Name" : "End distance from point", "Description" : "Signed, along +X." }
                    isLength(region.endPointOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "End station" }
                    isLength(region.endStation, ZERO_DEFAULT_LENGTH_BOUNDS);
                }

                annotation { "Name" : "Shape", "Default" : OffsetProfileShape.LINEAR, "UIHint" : UIHint.HORIZONTAL_ENUM,
                            "Description" : "Constant (one width and height), linear, or smooth (smootherstep: flat, zero curvature at both ends of the change)." }
                region.shape is OffsetProfileShape;

                if (region.shape == OffsetProfileShape.CONSTANT)
                {
                    annotation { "Name" : "Width" }
                    isLength(region.startWidth, ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "Height" }
                    isLength(region.startHeight, ZERO_DEFAULT_LENGTH_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Start width" }
                    isLength(region.startWidth, ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "End width" }
                    isLength(region.endWidth, ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "Start height" }
                    isLength(region.startHeight, ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "End height" }
                    isLength(region.endHeight, ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "Start buffer", "Description" : "Hold the start value for this distance from the start toward the centre." }
                    isLength(region.startBuffer, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "End buffer", "Description" : "Hold the end value for this distance from the end toward the centre." }
                    isLength(region.endBuffer, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
                }
            }

            annotation { "Name" : "Intersections", "Item name" : "Intersection", "Item label template" : "#region1 / #region2",
                        "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS,
                        "Description" : "One per pair of consecutive regions, kept up to date automatically." }
            definition.intersections is array;
            for (var intersection in definition.intersections)
            {
                annotation { "Name" : "First region", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                intersection.region1 is string;

                annotation { "Name" : "Second region", "Default" : "", "UIHint" : UIHint.READ_ONLY }
                intersection.region2 is string;

                annotation { "Name" : "Blend", "Default" : false,
                            "Description" : "Off: the regions join as they are when they touch with equal values; otherwise the profile breaks here." }
                intersection.blend is boolean;

                if (intersection.blend)
                {
                    annotation { "Name" : "Start continuity", "Default" : GeometricContinuity.G1, "UIHint" : UIHint.SHOW_LABEL }
                    intersection.startContinuity is GeometricContinuity;

                    annotation { "Name" : "Start distance", "Description" : "How far the blend reaches back into the first region from its end." }
                    isLength(intersection.startDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

                    annotation { "Name" : "End continuity", "Default" : GeometricContinuity.G1, "UIHint" : UIHint.SHOW_LABEL }
                    intersection.endContinuity is GeometricContinuity;

                    annotation { "Name" : "End distance", "Description" : "How far the blend reaches into the second region from its start." }
                    isLength(intersection.endDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
                }
            }
        }
        else
        {
            annotation { "Name" : "Points", "Item name" : "Point", "Item label template" : "#station",
                        "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
            definition.points is array;
            for (var point in definition.points)
            {
                annotation { "Name" : "Station from", "Default" : OffsetStationSource.VALUE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
                point.stationSource is OffsetStationSource;

                if (point.stationSource == OffsetStationSource.POINT)
                {
                    annotation { "Name" : "Station point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                "Description" : "Station = this point's world X plus the distance below." }
                    point.stationPoint is Query;

                    annotation { "Name" : "Distance from point", "Description" : "Signed, along +X." }
                    isLength(point.stationPointOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Station", "Description" : "World X in profile coordinates." }
                    isLength(point.station, ZERO_DEFAULT_LENGTH_BOUNDS);
                }

                annotation { "Name" : "Width" }
                isLength(point.width, ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "Height" }
                isLength(point.height, ZERO_DEFAULT_LENGTH_BOUNDS);

                annotation { "Name" : "To next point", "Default" : OffsetPointTransition.SMOOTH, "UIHint" : UIHint.SHOW_LABEL,
                            "Description" : "Smooth: flat at both points, no overshoot. Hold: keep this value up to the next point, then break. Ignored on the last point." }
                point.transition is OffsetPointTransition;
            }
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print segments", "Default" : false, "Description" : "Every piece and sub-segment: stations, degree, end values." }
            definition.debugPrint is boolean;
        }
    }
    {
        definition = resolveStations(context, definition, true);
        var built;
        if (definition.mode == OffsetProfileMode.REGIONS)
        {
            built = regionPieces(definition);
        }
        else
        {
            built = pointPieces(definition);
        }
        if (size(built.pieces) == 0)
        {
            throw regenError(definition.mode == OffsetProfileMode.REGIONS ? "Add at least one region." : "Add at least two points at different stations.",
                [definition.mode == OffsetProfileMode.REGIONS ? "regions" : "points"]);
        }

        var wires = [];
        var pieceEnds = [];
        for (var k = 0; k < size(built.pieces); k += 1)
        {
            const pieceResult = buildPiece(context, id + ("piece" ~ k), built.pieces[k]);
            wires = append(wires, pieceResult.wire);
            pieceEnds = append(pieceEnds, pieceResult);
        }

        if (definition.debugPrint)
        {
            printPieces(built.pieces);
        }

        publishProfile(context, id, definition, wires, pieceEnds, built.breaks);
    }, {
        "mode" : OffsetProfileMode.REGIONS,
        "regions" : [],
        "intersections" : [],
        "points" : [],
        "debugPrint" : false
    });

// ============================================================================
// Editing logic: names and intersections
// ============================================================================

/**
 * Copies stations picked from points into their value fields (so switching back to Value keeps the number, and
 * item labels show it), names unnamed regions "Region n" and rebuilds the intersection list: one entry per
 * consecutive pair (by start station), each keeping the settings of an existing entry for the same region-name pair.
 */
export function createOffsetProfileEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    var result = resolveStations(context, definition, false);
    if (definition.mode != OffsetProfileMode.REGIONS)
    {
        return result;
    }
    for (var i = 0; i < size(result.regions); i += 1)
    {
        if (result.regions[i].regionName == "")
        {
            result.regions[i].regionName = "Region " ~ (i + 1);
        }
    }
    const order = sortedRegionOrder(result.regions);
    var intersections = [];
    for (var k = 0; k + 1 < size(order); k += 1)
    {
        const first = result.regions[order[k]].regionName;
        const second = result.regions[order[k + 1]].regionName;
        var entry = {
            "region1" : first,
            "region2" : second,
            "blend" : false,
            "startContinuity" : GeometricContinuity.G1,
            "startDistance" : 0 * meter,
            "endContinuity" : GeometricContinuity.G1,
            "endDistance" : 0 * meter
        };
        for (var existing in definition.intersections)
        {
            if (existing.region1 == first && existing.region2 == second)
            {
                entry = mergeMaps(entry, existing);
                break;
            }
        }
        intersections = append(intersections, entry);
    }
    result.intersections = intersections;
    return result;
}

// ============================================================================
// Stations from points
// ============================================================================

/** Parameter ids of the station entries: [source, point, distance, value] per station of an item. */
const REGION_STATION_KEYS = [["startSource", "startPoint", "startPointOffset", "startStation"],
        ["endSource", "endPoint", "endPointOffset", "endStation"]];
const POINT_STATION_KEYS = [["stationSource", "stationPoint", "stationPointOffset", "station"]];

/**
 * World X of a vertex or mate connector pick, or undefined when nothing is picked. A connector pick can arrive
 * as the connector's vertex (correction 44), so it is resolved through the owner body.
 */
function pickedX(context is Context, q)
{
    if (!(q is Query) || isQueryEmpty(context, q))
    {
        return undefined;
    }
    const connector = evaluateQuery(context, qBodyType(qOwnerBody(q), BodyType.MATE_CONNECTOR));
    if (size(connector) > 0)
    {
        return evMateConnector(context, { "mateConnector" : connector[0] }).origin[0];
    }
    return evVertexPoint(context, { "vertex" : q })[0];
}

/**
 * Writes every Point-sourced station into its value field (picked point's world X + distance), so the rest of
 * the feature reads plain stations. `strict` (the feature body) throws on a missing pick; the editing logic
 * passes false and leaves such a station as it was.
 */
function resolveStations(context is Context, definition is map, strict is boolean) returns map
{
    var result = definition;
    const arrays = definition.mode == OffsetProfileMode.REGIONS ? [["regions", REGION_STATION_KEYS]] : [["points", POINT_STATION_KEYS]];
    for (var entry in arrays)
    {
        const arrayId = entry[0];
        for (var i = 0; i < size(result[arrayId]); i += 1)
        {
            for (var keys in entry[1])
            {
                const item = result[arrayId][i];
                if (item[keys[0]] != OffsetStationSource.POINT)
                {
                    continue;
                }
                const x = pickedX(context, item[keys[1]]);
                if (x == undefined)
                {
                    if (strict)
                    {
                        const label = arrayId == "regions" ? "Region '" ~ item.regionName ~ "'" : "Point " ~ (i + 1);
                        throw regenError(label ~ ": pick a point (vertex or mate connector) for the station.",
                            [faultyArrayParameterId(arrayId, i, keys[1])]);
                    }
                    continue;
                }
                const offset = item[keys[2]] is ValueWithUnits ? item[keys[2]] : 0 * meter;
                result[arrayId][i][keys[3]] = x + offset;
            }
        }
    }
    return result;
}

/** Indices of the regions in order of start station (then end station). */
function sortedRegionOrder(regions is array) returns array
{
    var order = [];
    for (var i = 0; i < size(regions); i += 1)
    {
        order = append(order, i);
    }
    return sort(order, function(a, b)
        {
            const d = regions[a].startStation - regions[b].startStation;
            if (abs(d) > OFFSET_PROFILE_TOLERANCE)
            {
                return d / meter;
            }
            return (regions[a].endStation - regions[b].endStation) / meter;
        });
}

// ============================================================================
// Shapes and region values (all internal math in metres, as plain numbers)
// ============================================================================

/** The shape function S(u) and its first and second derivatives, u in [0, 1]. */
function shapeAt(shape is OffsetProfileShape, u is number) returns array
{
    if (shape == OffsetProfileShape.LINEAR)
    {
        return [u, 1, 0];
    }
    const u2 = u * u;
    const u3 = u2 * u;
    return [u3 * (10 + u * (6 * u - 15)), 30 * u2 * (u - 1) * (u - 1), 60 * u * (2 * u2 - 3 * u + 1)];
}

/**
 * A region's offset in one channel ("w" or "h") at station x (metres): [value, d/dx, d2/dx2].
 * Constant over the buffers; the shape runs between them.
 */
function regionValue(region is map, channel is string, x is number) returns array
{
    const v0 = channel == "w" ? region.w0 : region.h0;
    const v1 = channel == "w" ? region.w1 : region.h1;
    const rampStart = region.xs + region.b0;
    const rampEnd = region.xe - region.b1;
    if (x <= rampStart || abs(v1 - v0) < 1e-15)
    {
        return [v0, 0, 0];
    }
    if (x >= rampEnd)
    {
        return [v1, 0, 0];
    }
    const len = rampEnd - rampStart;
    const s = shapeAt(region.shape, (x - rampStart) / len);
    return [v0 + (v1 - v0) * s[0], (v1 - v0) * s[1] / len, (v1 - v0) * s[2] / (len * len)];
}

/**
 * A region in metres, validated. `index` is its position in the dialog (for error highlighting). Stations
 * must already be resolved (resolveStations). A CONSTANT region has one width and height and no buffers.
 */
function regionData(region is map, index is number) returns map
{
    const constant = region.shape == OffsetProfileShape.CONSTANT;
    const xs = region.startStation / meter;
    const xe = region.endStation / meter;
    const b0 = constant ? 0 : region.startBuffer / meter;
    const b1 = constant ? 0 : region.endBuffer / meter;
    const tol = OFFSET_PROFILE_TOLERANCE / meter;
    if (xe - xs <= tol)
    {
        throw regenError("Region '" ~ region.regionName ~ "': the end station must be past the start station.",
            [faultyArrayParameterId("regions", index, "endStation")]);
    }
    if (b0 + b1 > xe - xs - tol)
    {
        throw regenError("Region '" ~ region.regionName ~ "': the buffers are as long as the region or longer.",
            [faultyArrayParameterId("regions", index, "startBuffer"), faultyArrayParameterId("regions", index, "endBuffer")]);
    }
    return {
            "name" : region.regionName,
            "index" : index,
            "xs" : xs, "xe" : xe, "b0" : b0, "b1" : b1,
            "w0" : region.startWidth / meter, "w1" : (constant ? region.startWidth : region.endWidth) / meter,
            "h0" : region.startHeight / meter, "h1" : (constant ? region.startHeight : region.endHeight) / meter,
            "shape" : region.shape
        };
}

// ============================================================================
// Segments: a polynomial in x over [xa, xb] for width and height
// ============================================================================

/** A segment: stations xa < xb (metres), polynomial degree, and value functions of x for each channel. */
function segment(xa is number, xb is number, degree is number, w is function, h is function, label is string) returns map
{
    return { "xa" : xa, "xb" : xb, "degree" : degree, "w" : w, "h" : h, "label" : label };
}

/**
 * The region's own sub-segments inside [a, b]: the start buffer (constant), the ramp (linear or smootherstep),
 * the end buffer (constant). Zero-length pieces are skipped.
 */
function regionSegments(region is map, a is number, b is number) returns array
{
    const tol = OFFSET_PROFILE_TOLERANCE / meter;
    const w = function(x is number) returns number { return regionValue(region, "w", x)[0]; };
    const h = function(x is number) returns number { return regionValue(region, "h", x)[0]; };
    const rampDegree = region.shape == OffsetProfileShape.SMOOTH ? 5 : 1;
    const parts = [[region.xs, region.xs + region.b0, 1, "start buffer"],
            [region.xs + region.b0, region.xe - region.b1, rampDegree, "ramp"],
            [region.xe - region.b1, region.xe, 1, "end buffer"]];
    var out = [];
    for (var p in parts)
    {
        const lo = max(p[0], a);
        const hi = min(p[1], b);
        if (hi - lo > tol)
        {
            out = append(out, segment(lo, hi, p[2], w, h, region.name ~ " " ~ p[3]));
        }
    }
    return out;
}

/** Continuity -> how many derivatives (beyond the value) a blend end matches. */
function derivativesFor(continuity is GeometricContinuity) returns number
{
    if (continuity == GeometricContinuity.G2)
    {
        return 2;
    }
    if (continuity == GeometricContinuity.G1)
    {
        return 1;
    }
    return 0;
}

/**
 * Coefficients c0..cn (in s = (x - xa) / L) of the lowest-degree polynomial matching the given value and
 * derivatives at s = 0 (end0: [v, dv/ds, d2v/ds2]) and s = 1 (end1), using n0 / n1 derivatives at each end.
 */
function hermiteCoefficients(end0 is array, n0 is number, end1 is array, n1 is number) returns array
{
    const count = n0 + n1 + 2;
    var rows = [];
    var rhs = [];
    for (var d = 0; d <= n0; d += 1)
    {
        var row = makeArray(count, 0);
        row[d] = d == 2 ? 2 : 1;
        rows = append(rows, row);
        rhs = append(rhs, end0[d]);
    }
    for (var d = 0; d <= n1; d += 1)
    {
        var row = makeArray(count, 0);
        for (var k = 0; k < count; k += 1)
        {
            // d-th derivative of s^k at s = 1: k (k - 1) ... (k - d + 1)
            var f = 1;
            for (var j = 0; j < d; j += 1)
            {
                f *= (k - j);
            }
            row[k] = k >= d ? f : 0;
        }
        rows = append(rows, row);
        rhs = append(rhs, end1[d]);
    }
    return solveLinear(rows, rhs);
}

/**
 * The blend between regions A and B from xa (inside A) to xb (inside B): per channel the Hermite polynomial
 * matching A at xa and B at xb to the chosen continuities; derivatives are the regions' analytic ones.
 */
function blendSegment(regionA is map, regionB is map, xa is number, xb is number, n0 is number, n1 is number) returns map
{
    const len = xb - xa;
    var coefficients = {};
    for (var channel in ["w", "h"])
    {
        const a = regionValue(regionA, channel, xa);
        const b = regionValue(regionB, channel, xb);
        coefficients[channel] = hermiteCoefficients([a[0], a[1] * len, a[2] * len * len], n0,
                                                   [b[0], b[1] * len, b[2] * len * len], n1);
    }
    const w = function(x is number) returns number { return polynomialAt(coefficients.w, (x - xa) / len); };
    const h = function(x is number) returns number { return polynomialAt(coefficients.h, (x - xa) / len); };
    return segment(xa, xb, max(1, n0 + n1 + 1), w, h, "blend " ~ regionA.name ~ " / " ~ regionB.name);
}

function polynomialAt(c is array, s is number) returns number
{
    var v = 0;
    for (var k = size(c) - 1; k >= 0; k -= 1)
    {
        v = v * s + c[k];
    }
    return v;
}

/** Gaussian elimination with partial pivoting (small dense systems). */
function solveLinear(rows is array, rhs is array) returns array
{
    const n = size(rhs);
    var a = rows;
    var b = rhs;
    for (var col = 0; col < n; col += 1)
    {
        var pivot = col;
        for (var r = col + 1; r < n; r += 1)
        {
            if (abs(a[r][col]) > abs(a[pivot][col]))
            {
                pivot = r;
            }
        }
        const rowTmp = a[col];
        a[col] = a[pivot];
        a[pivot] = rowTmp;
        const bTmp = b[col];
        b[col] = b[pivot];
        b[pivot] = bTmp;
        for (var r = col + 1; r < n; r += 1)
        {
            const f = a[r][col] / a[col][col];
            for (var c = col; c < n; c += 1)
            {
                a[r][c] -= f * a[col][c];
            }
            b[r] -= f * b[col];
        }
    }
    var x = makeArray(n, 0);
    for (var r = n - 1; r >= 0; r -= 1)
    {
        var s = b[r];
        for (var c = r + 1; c < n; c += 1)
        {
            s -= a[r][c] * x[c];
        }
        x[r] = s / a[r][r];
    }
    return x;
}

// ============================================================================
// Regions mode
// ============================================================================

/**
 * Regions in station order, each trimmed where a blend reaches into it, joined into pieces.
 *
 * @returns {map} : { pieces : array of arrays of segments, breaks : array of stations (metres) }
 */
function regionPieces(definition is map) returns map
{
    const order = sortedRegionOrder(definition.regions);
    var regions = [];
    var names = {};
    for (var i in order)
    {
        const r = regionData(definition.regions[i], i);
        if (names[r.name] == true)
        {
            throw regenError("Two regions are named '" ~ r.name ~ "'; intersections are kept by name, so names must differ.",
                [faultyArrayParameterId("regions", i, "regionName")]);
        }
        names[r.name] = true;
        regions = append(regions, r);
    }
    const tol = OFFSET_PROFILE_TOLERANCE / meter;
    const n = size(regions);
    if (n == 0)
    {
        return { "pieces" : [], "breaks" : [] };
    }

    // Per junction between regions k and k+1: blend settings, or undefined.
    var junctions = [];
    for (var k = 0; k + 1 < n; k += 1)
    {
        const a = regions[k];
        const b = regions[k + 1];
        if (b.xs < a.xe - tol)
        {
            throw regenError("Regions '" ~ a.name ~ "' and '" ~ b.name ~ "' overlap.",
                [faultyArrayParameterId("regions", b.index, "startStation")]);
        }
        var junction = undefined;
        for (var i = 0; i < size(definition.intersections); i += 1)
        {
            const entry = definition.intersections[i];
            if (entry.region1 == a.name && entry.region2 == b.name && entry.blend == true)
            {
                junction = { "xa" : a.xe - entry.startDistance / meter, "xb" : b.xs + entry.endDistance / meter,
                        "n0" : derivativesFor(entry.startContinuity), "n1" : derivativesFor(entry.endContinuity), "entry" : i };
            }
        }
        junctions = append(junctions, junction);
    }

    // Each region's kept span, trimmed by the blends on either side.
    var spans = [];
    for (var k = 0; k < n; k += 1)
    {
        var lo = regions[k].xs;
        var hi = regions[k].xe;
        if (k > 0 && junctions[k - 1] != undefined)
        {
            lo = junctions[k - 1].xb;
        }
        if (k + 1 < n && junctions[k] != undefined)
        {
            hi = junctions[k].xa;
        }
        if (hi < lo - tol)
        {
            const entry = k + 1 < n && junctions[k] != undefined ? junctions[k].entry : junctions[k - 1].entry;
            throw regenError("The blends into region '" ~ regions[k].name ~ "' reach past each other; shorten their distances.",
                [faultyArrayParameterId("intersections", entry, "startDistance"), faultyArrayParameterId("intersections", entry, "endDistance")]);
        }
        spans = append(spans, [lo, hi]);
    }

    var pieces = [];
    var breaks = [];
    var current = [];
    for (var k = 0; k < n; k += 1)
    {
        current = concatenateArrays([current, regionSegments(regions[k], spans[k][0], spans[k][1])]);
        if (k + 1 == n)
        {
            break;
        }
        const a = regions[k];
        const b = regions[k + 1];
        if (junctions[k] != undefined)
        {
            if (junctions[k].xb - junctions[k].xa > tol)
            {
                current = append(current, blendSegment(a, b, junctions[k].xa, junctions[k].xb, junctions[k].n0, junctions[k].n1));
            }
            continue;
        }
        const touching = b.xs - a.xe <= tol;
        const same = abs(regionValue(a, "w", a.xe)[0] - regionValue(b, "w", b.xs)[0]) <= tol
            && abs(regionValue(a, "h", a.xe)[0] - regionValue(b, "h", b.xs)[0]) <= tol;
        if (!(touching && same))
        {
            pieces = append(pieces, current);
            breaks = append(breaks, touching ? a.xe : (a.xe + b.xs) / 2);
            current = [];
        }
    }
    pieces = append(pieces, current);
    return { "pieces" : pieces, "breaks" : breaks };
}

// ============================================================================
// Points mode
// ============================================================================

/**
 * Points in station order, each joined to the next by its transition.
 *
 * @returns {map} : { pieces, breaks } as regionPieces.
 */
function pointPieces(definition is map) returns map
{
    const tol = OFFSET_PROFILE_TOLERANCE / meter;
    var points = [];
    for (var i = 0; i < size(definition.points); i += 1)
    {
        const p = definition.points[i];
        points = append(points, { "x" : p.station / meter, "w" : p.width / meter, "h" : p.height / meter,
                    "transition" : p.transition, "index" : i });
    }
    // stable sort by station: equal stations keep dialog order (the order of a jump)
    points = sort(points, function(a, b)
        {
            if (abs(a.x - b.x) > tol)
            {
                return a.x - b.x;
            }
            return a.index - b.index;
        });

    var pieces = [];
    var breaks = [];
    var current = [];
    for (var k = 0; k + 1 < size(points); k += 1)
    {
        const a = points[k];
        const b = points[k + 1];
        const same = abs(a.w - b.w) <= tol && abs(a.h - b.h) <= tol;
        if (b.x - a.x <= tol)
        {
            if (!same)
            {
                // a jump: two points at one station
                if (size(current) > 0)
                {
                    pieces = append(pieces, current);
                    breaks = append(breaks, a.x);
                }
                current = [];
            }
            continue;
        }
        if (a.transition == OffsetPointTransition.HOLD)
        {
            const w = function(x is number) returns number { return a.w; };
            const h = function(x is number) returns number { return a.h; };
            current = append(current, segment(a.x, b.x, 1, w, h, "hold at " ~ fmtStation(a.x)));
            if (!same)
            {
                pieces = append(pieces, current);
                breaks = append(breaks, b.x);
                current = [];
            }
            continue;
        }
        const shape = a.transition == OffsetPointTransition.SMOOTH ? OffsetProfileShape.SMOOTH : OffsetProfileShape.LINEAR;
        const pseudo = { "name" : "points", "xs" : a.x, "xe" : b.x, "b0" : 0, "b1" : 0, "w0" : a.w, "w1" : b.w, "h0" : a.h, "h1" : b.h, "shape" : shape };
        const w = function(x is number) returns number { return regionValue(pseudo, "w", x)[0]; };
        const h = function(x is number) returns number { return regionValue(pseudo, "h", x)[0]; };
        current = append(current, segment(a.x, b.x, shape == OffsetProfileShape.SMOOTH ? 5 : 1, w, h,
                    fmtStation(a.x) ~ " -> " ~ fmtStation(b.x)));
    }
    if (size(current) > 0)
    {
        pieces = append(pieces, current);
    }
    return { "pieces" : pieces, "breaks" : breaks };
}

// ============================================================================
// Geometry
// ============================================================================

/** t^n for a whole n >= 0, with t^0 = 1 (also for t = 0). */
function powInt(t is number, n is number) returns number
{
    var r = 1;
    for (var j = 0; j < n; j += 1)
    {
        r *= t;
    }
    return r;
}

function binomial(n is number, k is number) returns number
{
    var r = 1;
    for (var j = 1; j <= k; j += 1)
    {
        r = r * (n - k + j) / j;
    }
    return r;
}

/**
 * Bernstein coefficients of the degree-p polynomial f over [xa, xb]: interpolation at p + 1 equally spaced
 * stations (exact for a polynomial of degree <= p), end coefficients set to the end values exactly.
 */
function bernsteinOf(f is function, xa is number, xb is number, p is number) returns array
{
    var rows = [];
    var values = [];
    for (var i = 0; i <= p; i += 1)
    {
        const t = i / p;
        var row = [];
        for (var j = 0; j <= p; j += 1)
        {
            row = append(row, binomial(p, j) * powInt(t, j) * powInt(1 - t, p - j));
        }
        rows = append(rows, row);
        values = append(values, f(xa + (xb - xa) * t));
    }
    var c = solveLinear(rows, values);
    c[0] = values[0];
    c[p] = values[p];
    return c;
}

/**
 * One piece: one exact Bezier per segment (x linear in the parameter), joined into a single wire.
 * Consecutive segments share their joint point exactly.
 *
 * @returns {map} : { wire (Query), start, end (Vectors: the piece's end points) }
 */
function buildPiece(context is Context, id is Id, segments is array) returns map
{
    var bodies = [];
    var previousEnd = undefined;
    var start = undefined;
    for (var j = 0; j < size(segments); j += 1)
    {
        const seg = segments[j];
        const p = seg.degree;
        const cw = bernsteinOf(seg.w, seg.xa, seg.xb, p);
        const ch = bernsteinOf(seg.h, seg.xa, seg.xb, p);
        var controlPoints = [];
        for (var i = 0; i <= p; i += 1)
        {
            controlPoints = append(controlPoints, vector(seg.xa + (seg.xb - seg.xa) * i / p, cw[i], ch[i]) * meter);
        }
        if (previousEnd != undefined)
        {
            controlPoints[0] = previousEnd;
        }
        else
        {
            start = controlPoints[0];
        }
        previousEnd = controlPoints[p];
        const segId = id + ("segment" ~ j);
        opCreateBSplineCurve(context, segId, {
                    "bSplineCurve" : bSplineCurve({ "degree" : p, "isPeriodic" : false, "controlPoints" : controlPoints, "knots" : knotArray([0, 1]) })
                });
        bodies = append(bodies, qCreatedBy(segId, EntityType.BODY));
    }
    const pieceBodies = qUnion(bodies);
    opExtractWires(context, id + "wire", { "edges" : qOwnedByBody(pieceBodies, EntityType.EDGE) });
    opDeleteBodies(context, id + "deleteSegments", { "entities" : pieceBodies });
    return { "wire" : qCreatedBy(id + "wire", EntityType.BODY), "start" : start, "end" : previousEnd };
}

// ============================================================================
// Published outputs
// ============================================================================

function publishProfile(context is Context, id is Id, definition is map, wires is array, pieceEnds is array, breaks is array)
{
    var breakStations = [];
    for (var x in breaks)
    {
        breakStations = append(breakStations, x * meter);
    }
    const firstPiece = pieceEnds[0];
    const lastPiece = pieceEnds[size(pieceEnds) - 1];
    const vertexAt = function(wire is Query, point is Vector) returns Query
        {
            return qClosestTo(qOwnedByBody(wire, EntityType.VERTEX), point);
        };

    var breakVertices = [];
    for (var k = 0; k + 1 < size(pieceEnds); k += 1)
    {
        breakVertices = append(breakVertices, vertexAt(pieceEnds[k].wire, pieceEnds[k].end));
        breakVertices = append(breakVertices, vertexAt(pieceEnds[k + 1].wire, pieceEnds[k + 1].start));
    }
    var queries = {
        "startVertex" : extractableQuery(vertexAt(firstPiece.wire, firstPiece.start), "Where the profile starts (lowest station).", DebugColor.GREEN),
        "endVertex" : extractableQuery(vertexAt(lastPiece.wire, lastPiece.end), "Where the profile ends (highest station).", DebugColor.RED),
        "breakVertices" : extractableQuery(qUnion(breakVertices), "The piece ends on both sides of every break.", DebugColor.MAGENTA)
    };
    for (var k = 0; k < size(wires); k += 1)
    {
        queries["piece" ~ (k + 1)] = extractableQuery(wires[k], "Piece " ~ (k + 1) ~ " of the profile, in station order.", DebugColor.CYAN);
    }

    embedStandardOutputs(context, id, {
                "output" : qUnion(wires),
                "outputDescription" : "The offset profile pieces (X station, Y width, Z height)",
                "variables" : {
                    "pieceCount" : extractableVariable(size(wires), "Continuous pieces of the profile."),
                    "breakCount" : extractableVariable(size(breaks), "Breaks between pieces."),
                    "breakStations" : extractableVariable(breakStations, "Station of each break (a jump, or the middle of a gap)."),
                    "startStation" : extractableVariable(firstPiece.start[0], "First station of the profile."),
                    "endStation" : extractableVariable(lastPiece.end[0], "Last station of the profile.")
                },
                "queries" : queries
            });
}

// ============================================================================
// Debug
// ============================================================================

function fmtStation(x is number) returns string
{
    return toString(roundToPrecision(x * 1000, 4));
}

function printPieces(pieces is array)
{
    for (var k = 0; k < size(pieces); k += 1)
    {
        println("[offset profile] piece " ~ (k + 1) ~ ": " ~ size(pieces[k]) ~ " segment(s)");
        for (var seg in pieces[k])
        {
            println("    " ~ seg.label ~ ": x " ~ fmtStation(seg.xa) ~ " .. " ~ fmtStation(seg.xb) ~ " mm, degree " ~ seg.degree
                ~ ", width " ~ fmtStation(seg.w(seg.xa)) ~ " -> " ~ fmtStation(seg.w(seg.xb))
                ~ ", height " ~ fmtStation(seg.h(seg.xa)) ~ " -> " ~ fmtStation(seg.h(seg.xb)) ~ " mm");
        }
    }
}
