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

/** The station pair the Table 2 average radius is taken between. */
export enum PrimitiveRadiusBetween
{
    annotation { "Name" : "Contact points (FCP - ACP)" }
    CONTACTS,
    annotation { "Name" : "Widest points" }
    WIDEST,
    annotation { "Name" : "Inflection points" }
    INFLECTION
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
    annotation { "Name" : "Data (within RSL)" }
    DATA
}

export const PRIMITIVE_ATTRIBUTE = "publishPrimitive";
export const PRIMITIVE_SCHEMA = "primitive/1";

/** Cell text for rows that are present but not computed yet. */
export const PRIMITIVE_PHASE_2 = "not computed (phase 2)";
/** Cell text for a value that could not be found on this geometry. */
export const PRIMITIVE_NOT_FOUND = "n/a";

/** Radius plot scale: 10 mm of plot height per 1 m of radius (integrateFootprint "10mm [y] = 1m [radius]"). */
export const PRIMITIVE_RADIUS_PLOT_SCALE = 0.01;

export const PRIMITIVE_DATA_POINTS_BOUNDS = { (unitless) : [2, 21, 501] } as IntegerBoundSpec;
export const PRIMITIVE_BAND_GAP_BOUNDS = { (millimeter) : [0, 50, 5000] } as LengthBoundSpec;
export const PRIMITIVE_RADIUS_LIMIT_BOUNDS = { (meter) : [1, 50, 10000] } as LengthBoundSpec;
export const PRIMITIVE_TICK_BOUNDS = { (millimeter) : [0.1, 10, 500] } as LengthBoundSpec;

/** Band keys, in stacking order (top to bottom), and their body-name labels. */
export const PRIMITIVE_BANDS = ["baseline", "profile", "footprint", "radius"];
export const PRIMITIVE_BAND_LABELS = { "baseline" : "BASELINE", "profile" : "PROFILE", "footprint" : "FOOTPRINT", "radius" : "RADIUS" };

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
