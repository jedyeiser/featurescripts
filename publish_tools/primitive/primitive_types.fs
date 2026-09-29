FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Export Primitive -- shared names, enums and the data contract.
 *
 * The contract: the "<prefix> PRIMITIVE" composite carries the attribute PRIMITIVE_ATTRIBUTE, a map with
 * schema PRIMITIVE_SCHEMA ("primitive/1") holding every table row as plain numbers (mm, m, deg) or strings.
 * The "Primitive tables" custom table and any app read that attribute; nothing reads feature parameters.
 * A later schema adds keys; it never renames or removes one ("primitive/2" if it must).
 */

/** Where the baseline or the footprint comes from (shared by both: generic names, details in each parameter's description). */
export enum PrimitiveSource
{
    annotation { "Name" : "From the volume" }
    VOLUME,
    annotation { "Name" : "From picked wires" }
    INPUT
}

/** How a picked datum places the measuring frame. */
export enum PrimitiveDatumUse
{
    annotation { "Name" : "World axes (datum is a point only)" }
    ORIGIN,
    annotation { "Name" : "Mate connector's axes" }
    COORDINATE_SYSTEM
}

/** Where a tooling block's name comes from. */
export enum PrimitiveNameFrom
{
    annotation { "Name" : "Type a name" }
    TYPED,
    annotation { "Name" : "Use a picked wire's name" }
    WIRE
}

/**
 * The x-range the radius / curvature band shows (2026-09-28: was "Average radius between"; the average radius is now
 * always taken between the inflection points). Value ids kept; CONTACTS is the running surface, FCP to ACP. It clips
 * the plotted data only: the band's axes and reference line always span the full ski (2026-09-29).
 */
export enum PrimitivePlotRegion
{
    annotation { "Name" : "Full ski" }
    FULL,
    annotation { "Name" : "Running surface (FCP to ACP)" }
    CONTACTS,
    annotation { "Name" : "Between widest points" }
    WIDEST,
    annotation { "Name" : "Between inflection points" }
    INFLECTION
}

/** What the plot band draws along the footprint. */
export enum PrimitivePlot
{
    annotation { "Name" : "Sidecut radius (m)" }
    RADIUS,
    annotation { "Name" : "Curvature (1/m)" }
    CURVATURE
}

/**
 * Which of the primitive's tables a "Primitive tables" instance returns (a drawing inserts all it returns). Table 4
 * (SW rout, 2026-09-29) exists only when Export primitive has a SW rout surface.
 */
export enum PrimitiveTableKind
{
    annotation { "Name" : "All tables" }
    ALL,
    annotation { "Name" : "1 Theoretical scale factors" }
    SCALE_FACTORS,
    annotation { "Name" : "2 Metadata" }
    METADATA,
    annotation { "Name" : "3 Key locations" }
    KEY_LOCATIONS,
    annotation { "Name" : "4 SW rout" }
    SW_ROUT,
    annotation { "Name" : "5 Baseline" }
    BASELINE,
    annotation { "Name" : "6 RSL data" }
    DATA
}

export const PRIMITIVE_ATTRIBUTE = "publishPrimitive";
export const PRIMITIVE_SCHEMA = "primitive/1";

/** Cell text for a value that could not be found on this geometry. */
export const PRIMITIVE_NOT_FOUND = "n/a";

/** A baseline whose largest height above the FCP - ACP chord (within the RSL) is below this is flat: no rocker contacts, minima or camber. */
export const PRIMITIVE_FLAT_BASELINE = 0.01 * millimeter;

/** Radius plot scale: 10 mm of plot height per 1 m of radius (integrateFootprint "10mm [y] = 1m [radius]"). */
export const PRIMITIVE_RADIUS_PLOT_SCALE = 0.01;

export const PRIMITIVE_DATA_POINTS_BOUNDS = { (unitless) : [2, 35, 501] } as IntegerBoundSpec;
export const PRIMITIVE_BAND_GAP_BOUNDS = { (millimeter) : [0, 50, 5000] } as LengthBoundSpec;
export const PRIMITIVE_RADIUS_LIMIT_BOUNDS = { (meter) : [1, 50, 10000] } as LengthBoundSpec;
export const PRIMITIVE_TICK_BOUNDS = { (millimeter) : [0.1, 10, 500] } as LengthBoundSpec;
export const PRIMITIVE_TEXT_HEIGHT_BOUNDS = { (millimeter) : [1, 20, 500] } as LengthBoundSpec;
/** EI band scale: N*m^2 per 1 mm of plot height (2 -> a 150 N*m^2 ski plots 75 mm high). */
export const PRIMITIVE_EI_SCALE_BOUNDS = { (unitless) : [0.01, 2, 100000] } as RealBoundSpec;
/** EI band ticks every 50 N*m^2. */
export const PRIMITIVE_EI_GRID_STEP = 50;
/** Curvature band: plot height per 0.01 1/m (50 mm, user 2026-09-29 -> a 14 m sidecut (0.071 1/m) plots 357 mm high, a 30 m one 167 mm). */
export const PRIMITIVE_CURVATURE_SCALE_BOUNDS = { (millimeter) : [0.1, 50, 1000] } as LengthBoundSpec;
/**
 * Manual axes (Auto scale off; the fixed chart axes of 2026-09-29): the frames do not move with the data. Radius band: from
 * -"Radius axis min" to +radius limit (10 mm per 1 m: -100 .. +500 mm at the defaults). Curvature band: from
 * -"Curvature axis min" to +"Max curvature" (1/m; at 50 mm per 0.01 1/m also -100 .. +500 mm). EI band: 0 .. "EI axis
 * max" (N*m^2; 225 mm at 2 N*m^2 per mm). Plotted values outside a band's axis break the line.
 */
/** Radius axis min in m (a plain number, see export_primitive's radiusAxisLow). */
export const PRIMITIVE_RADIUS_AXIS_MIN_BOUNDS = { (unitless) : [0, 10, 10000] } as RealBoundSpec;
export const PRIMITIVE_MAX_CURVATURE_BOUNDS = { (unitless) : [0.001, 0.1, 1000] } as RealBoundSpec;
export const PRIMITIVE_CURVATURE_AXIS_MIN_BOUNDS = { (unitless) : [0, 0.02, 1000] } as RealBoundSpec;
export const PRIMITIVE_EI_AXIS_MAX_BOUNDS = { (unitless) : [1, 450, 1000000] } as RealBoundSpec;
/**
 * Auto scale (2026-09-29, default on): the bands keep a FIXED height (so they stack and align the same on every ski)
 * and the scale inside follows the data. Radius / curvature band: "Radius band height", the zero line at
 * PRIMITIVE_PLOT_NEGATIVE_FRACTION of it from the bottom; the positive side's top = k * a nice step with the largest
 * plotted value at PRIMITIVE_AUTO_FILL_LO .. HI of it (primitiveAutoAxis). EI band: "EI band height", 0 at the bottom.
 * Band heights are length parameters with MILLIMETRE bounds (correction 62: the migrated default is written in mm).
 */
export const PRIMITIVE_PLOT_BAND_HEIGHT_BOUNDS = { (millimeter) : [10, 150, 5000] } as LengthBoundSpec;
export const PRIMITIVE_EI_BAND_HEIGHT_BOUNDS = { (millimeter) : [10, 150, 5000] } as LengthBoundSpec;
/** Share of the radius / curvature band below its zero line (taper, tip, tail); fixed so the zero line never moves. */
export const PRIMITIVE_PLOT_NEGATIVE_FRACTION = 0.2;
/** Auto scale: the largest plotted value sits within this share of the positive axis (aim 90 %). */
export const PRIMITIVE_AUTO_FILL_LO = 0.85;
export const PRIMITIVE_AUTO_FILL_HI = 0.95;
/** Auto scale tick steps (level units: m of radius, 0.01 1/m of curvature; N*m^2 of EI), also times 10, 100, ... */
export const PRIMITIVE_PLOT_STEPS = [1, 2, 5, 10, 20, 25, 50];
export const PRIMITIVE_EI_STEPS = [10, 25, 50, 100];

/** The curvature scale's level unit: 0.01 1/m (levels are whole multiples, labels with 2 decimals). */
export const PRIMITIVE_CURVATURE_UNIT = 0.01;
/** At most about this many tick levels on a scale whose step adapts (curvature). */
export const PRIMITIVE_MAX_LEVELS = 20;

/** Radius plot frame: ticks (and the optional grid lines) every 10 m of radius, short ticks on the two end axes. */
export const PRIMITIVE_RADIUS_GRID_STEP = 10;
export const PRIMITIVE_AXIS_TICK = 3 * millimeter;

/**
 * Appearance per band (Part Studio colours; frame, ticks and tick labels grey; grid lines and key lines light grey --
 * single solid edges since 2026-09-29, a drawing restyles them dashed / coloured).
 */
export const PRIMITIVE_COLOURS = {
        "baseline" : color(0.09, 0.32, 0.69),
        "profile" : color(0.0, 0.5, 0.25),
        "footprint" : color(0.85, 0.4, 0.0),
        "radius" : color(0.75, 0.1, 0.1),
        "curvature" : color(0.75, 0.1, 0.1),
        "ei" : color(0.45, 0.2, 0.6),
        "frame" : color(0.5, 0.5, 0.5),
        "grid" : color(0.8, 0.8, 0.8)
    };

/** Band titles (text geometry, "Labels"). */
export const PRIMITIVE_BAND_TITLES = { "ei" : "EI (Nm^2)", "baseline" : "BASELINE", "profile" : "PROFILE", "footprint" : "FOOTPRINT", "radius" : "RADIUS (m)",
        "curvature" : "CURVATURE (1/m)" };

/** Band keys, in stacking order (top to bottom; "ei" only with a target EI; the last band is "radius" or "curvature" per Plot), and their body-name labels. */
export const PRIMITIVE_BANDS = ["ei", "baseline", "profile", "footprint", "radius", "curvature"];
export const PRIMITIVE_BAND_LABELS = { "ei" : "EI", "baseline" : "BASELINE", "profile" : "PROFILE", "footprint" : "FOOTPRINT", "radius" : "RADIUS",
        "curvature" : "CURVATURE" };

/** Names an extra key point may not take (after primitiveKey): the built-in key locations and footprint points. */
export const PRIMITIVE_RESERVED_KEYS = ["FCP", "ACP", "MRS", "XS1", "XS2", "TIP", "TAIL", "FB_WIDEST", "AB_WIDEST", "WAIST", "FB_INFLECTION", "AB_INFLECTION"];

/** An operation-id / key fragment from a display name: letters, digits and _ only. */
export function primitiveKey(name is string) returns string
{
    return replace(name, "[^A-Za-z0-9_]", "_");
}

/** Rounds to `digits` decimals (for stored table values). */
export function primitiveRound(value is number, digits is number) returns number
{
    const f = 10 ^ digits;
    return round(value * f) / f;
}

/** A length in mm rounded to 1e-4 mm, for the stored rows. */
export function primitiveMM(value is ValueWithUnits) returns number
{
    return primitiveRound(value / millimeter, 4);
}

/**
 * Cuts a polyline of [x (length), value (number)] points to the window xLo <= x <= xHi, vLo <= value <= vHi (plain
 * numbers in the points' own units): the pieces inside, each at least 2 points, a piece crossing a window edge ending
 * ON it (interpolated). Used for the plot bands' fixed axes: data outside a band's frame breaks the line.
 */
export function primitiveClipPolyline(points is array, xLo is ValueWithUnits, xHi is ValueWithUnits, vLo is number, vHi is number) returns array
{
    var runs = [];
    var run = [];
    for (var k = 0; k < size(points) - 1; k += 1)
    {
        const piece = clipSegment(points[k], points[k + 1], xLo, xHi, vLo, vHi);
        if (piece == undefined)
        {
            if (size(run) >= 2)
            {
                runs = append(runs, run);
            }
            run = [];
            continue;
        }
        if (size(run) > 0)
        {
            const last = run[size(run) - 1];
            if (abs(last[0] - piece[0][0]) > 1e-12 * meter || abs(last[1] - piece[0][1]) > 1e-12 * (1 + abs(last[1])))
            {
                // The previous segment left the window: a new piece starts.
                if (size(run) >= 2)
                {
                    runs = append(runs, run);
                }
                run = [];
            }
        }
        if (size(run) == 0)
        {
            run = [piece[0]];
        }
        run = append(run, piece[1]);
    }
    if (size(run) >= 2)
    {
        runs = append(runs, run);
    }
    return runs;
}

/** Liang-Barsky: the part of segment p -> q inside the window ([start, end]), undefined when none (or only a point). */
function clipSegment(p is array, q is array, xLo is ValueWithUnits, xHi is ValueWithUnits, vLo is number, vHi is number)
{
    const x0 = p[0] / meter;
    const dx = q[0] / meter - x0;
    const v0 = p[1];
    const dv = q[1] - v0;
    var t0 = 0;
    var t1 = 1;
    for (var edge in [[-dx, x0 - xLo / meter], [dx, xHi / meter - x0], [-dv, v0 - vLo], [dv, vHi - v0]])
    {
        const pp = edge[0];
        const qq = edge[1];
        if (abs(pp) < 1e-300)
        {
            if (qq < 0)
            {
                return undefined;
            }
            continue;
        }
        const r = qq / pp;
        if (pp < 0)
        {
            if (r > t1)
            {
                return undefined;
            }
            t0 = max(t0, r);
        }
        else
        {
            if (r < t0)
            {
                return undefined;
            }
            t1 = min(t1, r);
        }
    }
    if (t1 - t0 < 1e-12)
    {
        return undefined;
    }
    const a = t0 == 0 ? p : [p[0] + (q[0] - p[0]) * t0, v0 + dv * t0];
    const b = t1 == 1 ? q : [p[0] + (q[0] - p[0]) * t1, v0 + dv * t1];
    return [a, b];
}
