FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
export import(path : "onshape/std/geometriccontinuity.gen.fs", version : "3083.0");

/**
 * Offset profile core: the profile machinery of Create offset profile, in memory. Shared by Create offset
 * profile (which turns it into wires) and Driven edge offset's "Regions" profile source (which reads it
 * directly, no wire). Design: research_create_offset_profile.md; plan: reviews/2026-09-25_tools_review/
 * round2_lead_offset.md (P1 / P4).
 *
 * Profile coordinates: X = station (world X, or a picked point's world X plus a signed distance along +X),
 * Y = width offset, Z = height offset.
 *
 * What this module gives
 *   - the dialog: offsetProfileRegionsPredicate / offsetProfilePointsPredicate (the Regions / Points
 *     arrays, parameter ids as Create offset profile has always had them), the editing-logic update
 *     (station copy, region names, intersection list) and the blend-distance manipulators;
 *   - the profile: offsetProfileRegionPieces / offsetProfilePointPieces -> { pieces, breaks }. A piece is
 *     one continuous stretch, an array of segments; a segment is a polynomial in x over [xa, xb] (metres,
 *     plain numbers) with value functions w(x), h(x) -- the sampler: width / height in metres at x in
 *     metres. breaks: the station of each break (metres);
 *   - exact curves: offsetProfileCurves -> per piece the exact Bezier (as a BSplineCurve map, knots [0, 1])
 *     of every segment, consecutive segments sharing their joint point exactly -- the curves Create offset
 *     profile creates, without creating anything.
 *
 * Nothing here creates geometry. Everything numeric is the code Create offset profile ran before the split,
 * moved verbatim, so the wires it makes are bit-for-bit what they were.
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
    SMOOTH,
    annotation { "Name" : "Quadratic" }
    QUADRATIC
}

/**
 * Which end of a QUADRATIC region's ramp is flat (zero slope): the retired Variable surface offset's "Zero slope
 * at start" and Offset edges' "Zero slope at" (AT_START / AT_END). START: S(u) = u^2; END: S(u) = 2u - u^2.
 */
export enum OffsetQuadraticFlat
{
    annotation { "Name" : "Start" }
    START,
    annotation { "Name" : "End" }
    END
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
    annotation { "Name" : "Enter X" }
    VALUE,
    annotation { "Name" : "At point" }
    POINT
}

/** Manipulator keys for the blend distances of intersection i: BLEND_START_KEY ~ i, BLEND_END_KEY ~ i. */
const BLEND_START_KEY = "blendIntoFirst";
const BLEND_END_KEY = "blendIntoSecond";

/** Stations closer than this touch; offsets closer than this are equal. */
export const OFFSET_PROFILE_TOLERANCE = 1e-6 * meter;

// ============================================================================
// Dialog
// ============================================================================

/**
 * The Regions and Intersections arrays (parameter ids regions / intersections and their item fields). Used
 * inside `if (... == REGIONS)` by Create offset profile and by Driven edge offset's Regions profile source.
 */
export predicate offsetProfileRegionsPredicate(definition is map)
{
    annotation { "Name" : "Regions", "Item name" : "Region", "Item label template" : "#regionName",
                "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
    definition.regions is array;
    for (var region in definition.regions)
    {
        annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64, "Description" : "Empty = Region n. Intersections are kept by region name." }
        region.regionName is string;

        annotation { "Name" : "Start station", "Default" : OffsetStationSource.VALUE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL],
                    "Description" : "Enter X: type the station (world X). At point: pick a vertex or mate connector (or create one here); the station is its world X plus a signed offset along +X." }
        region.startSource is OffsetStationSource;

        if (region.startSource == OffsetStationSource.POINT)
        {
            annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "Start station = this point's world X plus the distance below." }
            region.startPoint is Query;

            annotation { "Name" : "Start offset along X", "Description" : "Signed: positive moves the station toward +X." }
            isLength(region.startPointOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Start X", "Description" : "World X in profile coordinates." }
            isLength(region.startStation, ZERO_DEFAULT_LENGTH_BOUNDS);
        }

        annotation { "Name" : "End station", "Default" : OffsetStationSource.VALUE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL],
                    "Description" : "Enter X: type the station (world X). At point: pick a vertex or mate connector (or create one here); the station is its world X plus a signed offset along +X." }
        region.endSource is OffsetStationSource;

        if (region.endSource == OffsetStationSource.POINT)
        {
            annotation { "Name" : "End point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "End station = this point's world X plus the distance below." }
            region.endPoint is Query;

            annotation { "Name" : "End offset along X", "Description" : "Signed: positive moves the station toward +X." }
            isLength(region.endPointOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }
        else
        {
            annotation { "Name" : "End X", "Description" : "World X in profile coordinates." }
            isLength(region.endStation, ZERO_DEFAULT_LENGTH_BOUNDS);
        }

        annotation { "Name" : "Shape", "Default" : OffsetProfileShape.LINEAR, "UIHint" : UIHint.HORIZONTAL_ENUM,
                    "Description" : "Constant (one width and height), linear, smooth (smootherstep: flat, zero curvature at both ends of the change), or quadratic (a parabola, flat at one end of the change)." }
        region.shape is OffsetProfileShape;

        if (region.shape == OffsetProfileShape.QUADRATIC)
        {
            annotation { "Name" : "Flat at", "Default" : OffsetQuadraticFlat.START, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL],
                        "Description" : "The end of the change with zero slope (width and height alike); the other end meets its buffer, or the next region, at a kink." }
            region.quadraticFlat is OffsetQuadraticFlat;
        }

        if (region.shape == OffsetProfileShape.CONSTANT)
        {
            annotation { "Name" : "Width" }
            isLength(region.constantWidth, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "Height" }
            isLength(region.constantHeight, ZERO_DEFAULT_LENGTH_BOUNDS);
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
            annotation { "Name" : "Continuity with first region", "Default" : GeometricContinuity.G1, "UIHint" : UIHint.SHOW_LABEL }
            intersection.startContinuity is GeometricContinuity;

            annotation { "Name" : "Distance into first region", "Description" : "The blend starts this far back from the first region's end (toward its start). Both distances 0 only works where the regions leave a gap in X." }
            isLength(intersection.startDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "Continuity with second region", "Default" : GeometricContinuity.G1, "UIHint" : UIHint.SHOW_LABEL }
            intersection.endContinuity is GeometricContinuity;

            annotation { "Name" : "Distance into second region", "Description" : "The blend ends this far past the second region's start (toward its end)." }
            isLength(intersection.endDistance, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
        }
    }
}

/** The Points array (parameter id points and its item fields). */
export predicate offsetProfilePointsPredicate(definition is map)
{
    annotation { "Name" : "Points", "Item name" : "Point", "Item label template" : "#station",
                "UIHint" : UIHint.COLLAPSE_ARRAY_ITEMS }
    definition.points is array;
    for (var point in definition.points)
    {
        annotation { "Name" : "Station", "Default" : OffsetStationSource.VALUE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL],
                    "Description" : "Enter X: type the station (world X). At point: pick a vertex or mate connector (or create one here); the station is its world X plus a signed offset along +X." }
        point.stationSource is OffsetStationSource;

        if (point.stationSource == OffsetStationSource.POINT)
        {
            annotation { "Name" : "Station point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "Station = this point's world X plus the distance below." }
            point.stationPoint is Query;

            annotation { "Name" : "Offset along X", "Description" : "Signed: positive moves the station toward +X." }
            isLength(point.stationPointOffset, ZERO_DEFAULT_LENGTH_BOUNDS);
        }
        else
        {
            annotation { "Name" : "X", "Description" : "World X in profile coordinates." }
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

// ============================================================================
// Editing logic: names and intersections
// ============================================================================

/**
 * The editing-logic update: copies stations picked from points into their value fields (so switching back to
 * Value keeps the number, and item labels show it), and with `isRegions` names unnamed regions "Region n" and
 * rebuilds the intersection list: one entry per consecutive pair (by start station), each keeping the settings
 * of an existing entry for the same region-name pair.
 *
 * @param isRegions {boolean} : the Regions arrays are in use (else the Points array).
 */
export function offsetProfileEditingUpdate(context is Context, definition is map, isRegions is boolean) returns map
{
    var result = resolveOffsetProfileStations(context, definition, isRegions, false);
    if (!isRegions)
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
// Blend distance arrows
// ============================================================================

/** A region's profile point at station x (metres): (x, width, height). */
function regionPoint(region is map, x is number) returns Vector
{
    return vector(x, regionValue(region, "w", x)[0], regionValue(region, "h", x)[0]) * meter;
}

/**
 * Two drag arrows per blended intersection, each with a magenta debug point at its base: on the first region's end, dragging back along -X (distance into the
 * first region), and on the second region's start, dragging along +X (distance into the second region). Each is
 * limited to its region's length. Stations must already be resolved (resolveOffsetProfileStations).
 */
export function addOffsetProfileBlendManipulators(context is Context, id is Id, definition is map)
{
    var byName = {};
    for (var i = 0; i < size(definition.regions); i += 1)
    {
        const r = regionData(definition.regions[i], i);
        byName[r.name] = r;
    }
    var manipulators = {};
    for (var i = 0; i < size(definition.intersections); i += 1)
    {
        const entry = definition.intersections[i];
        const a = byName[entry.region1];
        const b = byName[entry.region2];
        if (entry.blend != true || a == undefined || b == undefined)
        {
            continue;
        }
        // Debug points mark the arrow bases (shown while editing only; no geometry is made).
        addDebugPoint(context, regionPoint(a, a.xe), DebugColor.MAGENTA);
        addDebugPoint(context, regionPoint(b, b.xs), DebugColor.MAGENTA);
        manipulators[BLEND_START_KEY ~ i] = linearManipulator({
                    "base" : regionPoint(a, a.xe), "direction" : vector(-1, 0, 0), "offset" : entry.startDistance,
                    "minValue" : 0 * meter, "maxValue" : (a.xe - a.xs) * meter });
        manipulators[BLEND_END_KEY ~ i] = linearManipulator({
                    "base" : regionPoint(b, b.xs), "direction" : vector(1, 0, 0), "offset" : entry.endDistance,
                    "minValue" : 0 * meter, "maxValue" : (b.xe - b.xs) * meter });
    }
    if (size(manipulators) > 0)
    {
        addManipulators(context, id, manipulators);
    }
}

/** Writes a dragged blend arrow back into its intersection's distance (the body of a Manipulator Change Function). */
export function offsetProfileManipulatorChange(definition is map, newManipulators is map) returns map
{
    for (var i = 0; i < size(definition.intersections); i += 1)
    {
        if (newManipulators[BLEND_START_KEY ~ i] is map)
        {
            definition.intersections[i].startDistance = max(0 * meter, newManipulators[BLEND_START_KEY ~ i].offset);
        }
        if (newManipulators[BLEND_END_KEY ~ i] is map)
        {
            definition.intersections[i].endDistance = max(0 * meter, newManipulators[BLEND_END_KEY ~ i].offset);
        }
    }
    return definition;
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
 * the profile code reads plain stations. `strict` (a feature body) throws on a missing pick; the editing logic
 * passes false and leaves such a station as it was.
 *
 * @param isRegions {boolean} : resolve the Regions array (else the Points array).
 */
export function resolveOffsetProfileStations(context is Context, definition is map, isRegions is boolean, strict is boolean) returns map
{
    var result = definition;
    const arrays = isRegions ? [["regions", REGION_STATION_KEYS]] : [["points", POINT_STATION_KEYS]];
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

/**
 * The shape function S(u) and its first and second derivatives, u in [0, 1]. `flatEnd` (QUADRATIC only): the
 * zero slope is at u = 1 instead of u = 0.
 */
function shapeAt(shape is OffsetProfileShape, flatEnd is boolean, u is number) returns array
{
    if (shape == OffsetProfileShape.LINEAR)
    {
        return [u, 1, 0];
    }
    if (shape == OffsetProfileShape.QUADRATIC)
    {
        if (flatEnd)
        {
            return [2 * u - u * u, 2 - 2 * u, -2];
        }
        return [u * u, 2 * u, 2];
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
    const s = shapeAt(region.shape, region.quadraticFlatEnd == true, (x - rampStart) / len);
    return [v0 + (v1 - v0) * s[0], (v1 - v0) * s[1] / len, (v1 - v0) * s[2] / (len * len)];
}

/**
 * A region in metres, validated. `index` is its position in the dialog (for error highlighting). Stations
 * must already be resolved (resolveOffsetProfileStations). A CONSTANT region has one width and height and no buffers.
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
            "w0" : (constant ? region.constantWidth : region.startWidth) / meter,
            "w1" : (constant ? region.constantWidth : region.endWidth) / meter,
            "h0" : (constant ? region.constantHeight : region.startHeight) / meter,
            "h1" : (constant ? region.constantHeight : region.endHeight) / meter,
            "shape" : region.shape,
            "quadraticFlatEnd" : region.shape == OffsetProfileShape.QUADRATIC && region.quadraticFlat == OffsetQuadraticFlat.END
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
 * The region's own sub-segments inside [a, b]: the start buffer (constant), the ramp (linear, smootherstep or
 * quadratic), the end buffer (constant). Zero-length pieces are skipped. The ramp's degree is the shape's own
 * (1, 5, 2), so offsetProfileCurves writes it as an exact Bezier of that degree.
 */
function regionSegments(region is map, a is number, b is number) returns array
{
    const tol = OFFSET_PROFILE_TOLERANCE / meter;
    const w = function(x is number) returns number { return regionValue(region, "w", x)[0]; };
    const h = function(x is number) returns number { return regionValue(region, "h", x)[0]; };
    var rampDegree = region.shape == OffsetProfileShape.SMOOTH ? 5 : 1;
    if (region.shape == OffsetProfileShape.QUADRATIC)
    {
        rampDegree = 2;
    }
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
    var blend = segment(xa, xb, max(1, n0 + n1 + 1), w, h, "blend " ~ regionA.name ~ " / " ~ regionB.name);
    blend.isBlend = true;
    return blend;
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
 * Regions in station order, each trimmed where a blend reaches into it, joined into pieces. Stations must
 * already be resolved (resolveOffsetProfileStations).
 *
 * @returns {map} : { pieces : array of arrays of segments, breaks : array of stations (metres) }
 */
export function offsetProfileRegionPieces(definition is map) returns map
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
                continue;
            }
            // A blend with no length: nothing to bridge over. Fine where the values already meet; a jump is an error.
            if (!sameValues(a, b))
            {
                const entry = junctions[k].entry;
                throw regenError("The blend between '" ~ a.name ~ "' and '" ~ b.name ~ "' has no length but the offset jumps at "
                    ~ offsetProfileFmtStation(a.xe) ~ " mm; give it a distance into either region.",
                    [faultyArrayParameterId("intersections", entry, "startDistance"), faultyArrayParameterId("intersections", entry, "endDistance")]);
            }
            continue;
        }
        const touching = b.xs - a.xe <= tol;
        if (!(touching && sameValues(a, b)))
        {
            pieces = append(pieces, current);
            breaks = append(breaks, touching ? a.xe : (a.xe + b.xs) / 2);
            current = [];
        }
    }
    pieces = append(pieces, current);
    return { "pieces" : pieces, "breaks" : breaks };
}

/** Whether region a's end value equals region b's start value in both channels. */
function sameValues(a is map, b is map) returns boolean
{
    const tol = OFFSET_PROFILE_TOLERANCE / meter;
    return abs(regionValue(a, "w", a.xe)[0] - regionValue(b, "w", b.xs)[0]) <= tol
        && abs(regionValue(a, "h", a.xe)[0] - regionValue(b, "h", b.xs)[0]) <= tol;
}

// ============================================================================
// Points mode
// ============================================================================

/**
 * Points in station order, each joined to the next by its transition. Stations must already be resolved
 * (resolveOffsetProfileStations).
 *
 * @returns {map} : { pieces, breaks } as offsetProfileRegionPieces.
 */
export function offsetProfilePointPieces(definition is map) returns map
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
            current = append(current, segment(a.x, b.x, 1, w, h, "hold at " ~ offsetProfileFmtStation(a.x)));
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
                    offsetProfileFmtStation(a.x) ~ " -> " ~ offsetProfileFmtStation(b.x)));
    }
    if (size(current) > 0)
    {
        pieces = append(pieces, current);
    }
    return { "pieces" : pieces, "breaks" : breaks };
}

// ============================================================================
// Exact curves (in memory)
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
 * The exact curves of every piece: one Bezier per segment (x linear in the parameter, knots [0, 1]), as the
 * BSplineCurve map opCreateBSplineCurve takes. Consecutive segments of a piece share their joint point exactly
 * (the next segment's first control point IS the previous one's last).
 *
 * @returns {array} : per piece { "curves" : array of BSplineCurve (one per segment), "start", "end" : Vector
 *          (the piece's end points) }.
 */
export function offsetProfileCurves(pieces is array) returns array
{
    var out = [];
    for (var segments in pieces)
    {
        var curves = [];
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
                // Consecutive segments of a piece meet by construction; snapping only removes rounding. A real gap
                // here is a bug (it used to drag the next segment onto the previous end), so refuse it.
                if (norm(controlPoints[0] - previousEnd) > OFFSET_PROFILE_TOLERANCE)
                {
                    throw regenError("Internal: segments '" ~ segments[j - 1].label ~ "' and '" ~ seg.label ~ "' do not meet at "
                        ~ offsetProfileFmtStation(seg.xa) ~ " mm.");
                }
                controlPoints[0] = previousEnd;
            }
            else
            {
                start = controlPoints[0];
            }
            previousEnd = controlPoints[p];
            curves = append(curves, bSplineCurve({ "degree" : p, "isPeriodic" : false, "controlPoints" : controlPoints, "knots" : knotArray([0, 1]) }));
        }
        out = append(out, { "curves" : curves, "start" : start, "end" : previousEnd });
    }
    return out;
}

// ============================================================================
// Debug
// ============================================================================

/** A station in metres as millimetres, 4 decimals. */
export function offsetProfileFmtStation(x is number) returns string
{
    return toString(roundToPrecision(x * 1000, 4));
}

/** Every piece and sub-segment: stations, degree, end values. */
export function printOffsetProfilePieces(pieces is array)
{
    for (var k = 0; k < size(pieces); k += 1)
    {
        println("[offset profile] piece " ~ (k + 1) ~ ": " ~ size(pieces[k]) ~ " segment(s)");
        for (var seg in pieces[k])
        {
            println("    " ~ seg.label ~ ": x " ~ offsetProfileFmtStation(seg.xa) ~ " .. " ~ offsetProfileFmtStation(seg.xb) ~ " mm, degree " ~ seg.degree
                ~ ", width " ~ offsetProfileFmtStation(seg.w(seg.xa)) ~ " -> " ~ offsetProfileFmtStation(seg.w(seg.xb))
                ~ ", height " ~ offsetProfileFmtStation(seg.h(seg.xa)) ~ " -> " ~ offsetProfileFmtStation(seg.h(seg.xb)) ~ " mm");
        }
    }
}
