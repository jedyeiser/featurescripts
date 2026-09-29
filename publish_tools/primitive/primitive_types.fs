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

/** Where the baseline or the footprint comes from. */
export enum PrimitiveSource
{
    annotation { "Name" : "Volume" }
    VOLUME,
    annotation { "Name" : "Input wires" }
    INPUT
}

/** How a picked datum places the measuring frame. */
export enum PrimitiveDatumUse
{
    annotation { "Name" : "Origin only (world axes)" }
    ORIGIN,
    annotation { "Name" : "Coordinate system (connector axes)" }
    COORDINATE_SYSTEM
}

/** Where a tooling block's name comes from. */
export enum PrimitiveNameFrom
{
    annotation { "Name" : "Typed" }
    TYPED,
    annotation { "Name" : "Picked wire's name" }
    WIRE
}

/**
 * The x-range the radius / curvature band shows (2026-09-28: was "Average radius between"; the average radius is now
 * always taken between the inflection points). Value ids kept; CONTACTS reads "RSL".
 */
export enum PrimitivePlotRegion
{
    annotation { "Name" : "Full ski" }
    FULL,
    annotation { "Name" : "RSL" }
    CONTACTS,
    annotation { "Name" : "Widest points" }
    WIDEST,
    annotation { "Name" : "Inflection points" }
    INFLECTION
}

/** What the plot band draws along the footprint. */
export enum PrimitivePlot
{
    annotation { "Name" : "Radius" }
    RADIUS,
    annotation { "Name" : "Curvature" }
    CURVATURE
}

/** Which of the primitive's tables a "Primitive tables" instance returns (a drawing inserts all it returns). */
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
    annotation { "Name" : "5 Baseline" }
    BASELINE,
    annotation { "Name" : "6 Data (RSL)" }
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

export const PRIMITIVE_DATA_POINTS_BOUNDS = { (unitless) : [2, 21, 501] } as IntegerBoundSpec;
export const PRIMITIVE_BAND_GAP_BOUNDS = { (millimeter) : [0, 50, 5000] } as LengthBoundSpec;
export const PRIMITIVE_RADIUS_LIMIT_BOUNDS = { (meter) : [1, 50, 10000] } as LengthBoundSpec;
export const PRIMITIVE_TICK_BOUNDS = { (millimeter) : [0.1, 10, 500] } as LengthBoundSpec;
export const PRIMITIVE_TEXT_HEIGHT_BOUNDS = { (millimeter) : [1, 20, 500] } as LengthBoundSpec;
/** EI band scale: N*m^2 per 1 mm of plot height (2 -> a 150 N*m^2 ski plots 75 mm high). */
export const PRIMITIVE_EI_SCALE_BOUNDS = { (unitless) : [0.01, 2, 100000] } as RealBoundSpec;
/** EI band ticks every 50 N*m^2. */
export const PRIMITIVE_EI_GRID_STEP = 50;
/** Curvature band: plot height per 0.01 1/m (5 mm -> a 14 m sidecut plots 36 mm high, a 30 m one 17 mm). */
export const PRIMITIVE_CURVATURE_SCALE_BOUNDS = { (millimeter) : [0.1, 5, 1000] } as LengthBoundSpec;
/** The curvature scale's level unit: 0.01 1/m (levels are whole multiples, labels with 2 decimals). */
export const PRIMITIVE_CURVATURE_UNIT = 0.01;
/** At most about this many tick levels on a scale whose step adapts (curvature). */
export const PRIMITIVE_MAX_LEVELS = 20;

/** Radius plot frame: ticks (and the optional dashed grid) every 10 m of radius, short ticks on the two end axes. */
export const PRIMITIVE_RADIUS_GRID_STEP = 10;
export const PRIMITIVE_AXIS_TICK = 3 * millimeter;
export const PRIMITIVE_GRID_DASH = 4 * millimeter;
export const PRIMITIVE_GRID_GAP = 4 * millimeter;

/** Appearance per band (Part Studio colours; frame, ticks, grid and tick labels grey). */
export const PRIMITIVE_COLOURS = {
        "baseline" : color(0.09, 0.32, 0.69),
        "profile" : color(0.0, 0.5, 0.25),
        "footprint" : color(0.85, 0.4, 0.0),
        "radius" : color(0.75, 0.1, 0.1),
        "curvature" : color(0.75, 0.1, 0.1),
        "ei" : color(0.45, 0.2, 0.6),
        "frame" : color(0.5, 0.5, 0.5)
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
