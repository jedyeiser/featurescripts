FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * QC Table Types and Constants
 *
 * Defines all enums, constants, and bounds used across the QC table feature modules.
 * This is the foundation module with zero dependencies on other qcTable modules.
 */

// ============================================================================
// ENUMS
// ============================================================================

/**
 * Point generation method - how to space measurement stations
 */
export enum POINT_TYPES
{
    annotation { "Name" : "Static Distance Between Points" }
    STATIC_DISTANCE,
    annotation { "Name" : "Evenly Spaced on RSL" }
    EVENLY_DIVIDE_RSL,
    annotation { "Name" : "Evenly Spaced on Body Length" }
    EVENLY_DIVIDE_LENGTH
}

/**
 * Starting location for static distance point generation
 */
export enum START_STATIC_POINTS
{
    annotation { "Name" : "Start at Tail" }
    TAIL,
    annotation { "Name" : "Center at MRS" }
    MRS
}

/**
 * Table row ordering
 */
export enum TABLE_ORDER
{
    annotation { "Name" : "Ascending (Tip to Tail)" }
    ASCENDING,
    annotation { "Name" : "Descending (Tail to Tip)" }
    DESCENDING
}

/**
 * Behavior for measurement points outside FCP/ACP boundaries
 */
export enum BOUNDARY_BEHAVIOR
{
    annotation { "Name" : "Ignore data outside FCP/ACP" }
    IGNORE,
    annotation { "Name" : "Minimal (one point per body at tip/tail)" }
    MINIMAL,
    annotation { "Name" : "Normal (all points)" }
    NORMAL
}

/**
 * Units for table export
 */
export enum EXPORT_UNITS
{
    annotation { "Name" : "Millimeters" }
    MILLIMETER,
    annotation { "Name" : "Inches" }
    INCH,
    annotation { "Name" : "Centimeters" }
    CENTIMETER
}

/**
 * Detail level for table columns
 */
export enum DETAIL_LEVEL
{
    annotation { "Name" : "Standard (Essential columns only)" }
    STANDARD,
    annotation { "Name" : "Details (All measurements)" }
    DETAILS
}

// ============================================================================
// CONSTANTS AND BOUNDS
// ============================================================================

/**
 * Distance between points for static spacing method
 * Default: 3 inches (76.2mm), Range: 5mm to 150mm
 */
export const pointDistBounds =
{
    (millimeter) : [5, 3 * 25.4, 150]
} as LengthBoundSpec;

/**
 * Reference Stance Length (RSL) bounds
 * Default: 1500mm, Range: 100mm to 2050mm
 */
export const rslBounds =
{
    (millimeter) : [100, 1500, 2050]
} as LengthBoundSpec;

/**
 * Number of evenly spaced points
 * Default: 40, Range: 12 to 80
 */
export const pointNumBounds =
{
    (unitless) : [12, 40, 80]
} as IntegerBoundSpec;

/**
 * Significant figures for table formatting
 * Default: 3, Range: 1 to 5
 */
export const sigFigBounds =
{
    (unitless) : [1, 3, 5]
} as IntegerBoundSpec;

/**
 * Filter distance for removing close points (sidewall legacy)
 * Default: 0.5mm, Range: 0.01mm to 5mm
 */
export const filterPointDistBounds =
{
    (millimeter) : [0.01, 0.5, 5]
} as LengthBoundSpec;

// ============================================================================
// GEOMETRIC TOLERANCES
// ============================================================================

/**
 * Tolerance for geometric comparisons (1 micron)
 */
export const GEOM_TOL = 1e-6 * meter;

/**
 * Tolerance for considering points coincident (100 microns)
 * Used for merging stations at nearly the same X position
 */
export const STATION_MERGE_TOL = 0.1 * millimeter;

/**
 * Minimum distance from body extents for interior point detection
 * Used to filter out points too close to tip/tail
 */
export const EDGE_MARGIN = 0.1 * millimeter;

// ============================================================================
// HELPER TYPES
// ============================================================================

/**
 * Station information - represents one measurement location
 */
export type Station typecheck canBeStation;

export predicate canBeStation(value)
{
    value is map;
    value.x is ValueWithUnits;           // X position in world coordinates
    value.callout is string;              // Human-readable label (e.g., "FCP", "MRS")
    value.preferred is boolean;           // True for critical stations (FCP, MRS, etc.)
}

/**
 * Core measurement data at a station
 */
export type CoreMeasurement typecheck canBeCoreMeasurement;

export predicate canBeCoreMeasurement(value)
{
    value is map;
    value.coreWidth is ValueWithUnits;
    value.coreThickness is ValueWithUnits;
    value.groovedThickness is ValueWithUnits;
    value.coreTopWidth is ValueWithUnits;
    value.coreBottomZ is ValueWithUnits;
    value.coreTopZ is ValueWithUnits;
    // Optional fields (may be empty string if not present)
    // value.coreTopAngle
    // value.baseRoutDepth
    // value.baseRoutWidth
}

/**
 * Sidewall measurement data at a station
 */
export type SidewallMeasurement typecheck canBeSidewallMeasurement;

export predicate canBeSidewallMeasurement(value)
{
    value is map;
    value.swHeight is ValueWithUnits;
    value.swBottomZ is ValueWithUnits;
    value.swTopZ is ValueWithUnits;
}

/**
 * Formatting configuration for table output
 */
export type FormatConfig typecheck canBeFormatConfig;

export predicate canBeFormatConfig(value)
{
    value is map;
    value.tableUnits is EXPORT_UNITS;
    value.sigFigs is number;
    value.showUnits is boolean;
    value.detailLevel is DETAIL_LEVEL;
}

// ============================================================================
// STANDARD CALLOUTS
// ============================================================================

/**
 * Standard station callout strings
 */
export const CALLOUT_FCP = "FCP";
export const CALLOUT_XS1 = "XS-1";
export const CALLOUT_MRS = "MRS";
export const CALLOUT_XS2 = "XS-2";
export const CALLOUT_ACP = "ACP";
export const CALLOUT_CORE_TIP = "CORE_TIP";
export const CALLOUT_CORE_TAIL = "CORE_TAIL";
export const CALLOUT_SW_TIP = "SW_TIP";
export const CALLOUT_SW_TAIL = "SW_TAIL";

// ============================================================================
// UNIT CONVERSION
// ============================================================================

/**
 * Get scale factor for converting meters to specified export units
 */
export function getUnitScaleFactor(units is EXPORT_UNITS) returns number
{
    if (units == EXPORT_UNITS.MILLIMETER)
    {
        return 1000;
    }
    else if (units == EXPORT_UNITS.INCH)
    {
        return 1000 / 25.4;
    }
    else if (units == EXPORT_UNITS.CENTIMETER)
    {
        return 100;
    }
    else
    {
        return 1000;  // Default to mm
    }
}

/**
 * Get unit suffix string for table display
 */
export function getUnitSuffix(units is EXPORT_UNITS, showUnits is boolean) returns string
{
    if (!showUnits)
    {
        return '';
    }

    if (units == EXPORT_UNITS.MILLIMETER)
    {
        return ' mm';
    }
    else if (units == EXPORT_UNITS.INCH)
    {
        return ' in';
    }
    else if (units == EXPORT_UNITS.CENTIMETER)
    {
        return ' cm';
    }
    else
    {
        return ' mm';  // Default
    }
}
