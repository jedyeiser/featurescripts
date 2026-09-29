FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");
// IMPORT: primitive_profiles.fs
export import(path : "5865b24d55ff270a56088adf", version : "a49d6c6f041f48486d3a38bd");
// IMPORT: primitive_footprint.fs
export import(path : "fbc957543e769a649f00c5cc", version : "0643b2c32fc6fa3f5a5aeb72");
// IMPORT: primitive_baseline.fs
export import(path : "b827b10bc0bdc678c2db28cd", version : "17196e5856e8bb263bcadb97");
// IMPORT: primitive_output.fs
export import(path : "6f122edb2547a6a46991d9fd", version : "f5e360a9b5a73ca5e71e9980");
// IMPORT: Variable_tools extract_outputs.fs (embedStandardOutputs) -- same pin as station_geometry
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: xSection V58 xSectBeamAnalysis.fs (getEIFromEdges, computeBeamStiffness; direction-safe)
import(path : "f8deedeb1fbd819a8fa20113/ad6a3958dd2e8873f6dd0b0d/ebac109589e3bf405d3f3ae7", version : "07cfdc7634781f7f69ffd5b3");
// IMPORT: export_primitive_icon.svg (feature icon)
IconNamespace::import(path : "b9dc4aaf067afeb58293caed", version : "efb139f3e58d8e1cb09ef9fa");

/** The data table's radius is read this far inside the RSL, so a row on an edge junction takes the RSL-side edge. */
const RADIUS_SIDE_STEP = 1e-3 * millimeter;
/**
 * Auto scale with nothing plotted: the axis is chosen as if the manual axis top (Max radius, Max curvature, EI axis
 * max) were plotted at this share of it, which gives back that top for the defaults (50 m, 0.1 1/m, 450 N*m^2).
 */
const AUTO_AIM = 0.9;
/** A key-location tick within this of the plot region's ends is drawn. */
const REGION_TOLERANCE = 1e-6 * meter;

/**
 * Export primitive: the ski's "primitive drawing" geometry and data from its volume.
 *
 * Builds ONE closed composite "<prefix> PRIMITIVE" (excluded from the BOM) in the datum XZ plane, below the part,
 * with bands stacked top to bottom, `Band gap` apart:
 *     0 EI          (with a Target EI) the EI profile, a zero reference and its scale frame
 *     1 BASELINE    the baseline (x, z), with points FCP / ACP / TIP / TAIL (the baseline's ends) and, unless it
 *                   is flat within the RSL, FRCP / ARCP / MCL / FB_MIN / AB_MIN
 *     2 PROFILE     the volume cut by the datum XZ plane: BOTTOM, TOP, TIP END, TAIL END wires, with points at
 *                   FCP / ACP / MRS / XS1 / XS2 / MP / the extra key points on the bottom wire and TIP / TAIL at the
 *                   section's extreme points along X (by construction also the bottom wire's ends)
 *     3 FOOTPRINT   the footprint unwrapped along s (u = x(MRS) + s towards the tip), y across drawn as height,
 *                   with points at the widest points, the waist and the inflections, and (Junction ticks) a short
 *                   tick at every edge junction of the +y side
 *     4 RADIUS      (Plot type Sidecut radius) the sidecut radius along u, 10 mm per 1 m (integrateFootprint's input
 *                   format), sidecut positive, taper / tip / tail negative; axis -"Radius axis min" .. +"Max radius"
 *    or CURVATURE   (Plot type Curvature) the signed curvature (1/m, same signs), continuous through inflections and
 *                   across edge joins where the curvature is continuous (arcs = flat lines), "Curvature plot scale"
 *                   mm per 0.01 1/m; axis -"Curvature axis min" .. +"Max curvature"
 *                   Both plotted over the "Plot x-range", breaking where the value leaves the axis, with TICK marks at
 *                   the key locations and junction ticks there; the REFERENCE (0) line and the scale frame (end axes,
 *                   ticks, numbers) span the full footprint length whatever the data
 *     The EI band's frame spans the volume's x extent, EI 0 .. its axis top.
 *     Auto scale (default, 2026-09-29): each of these bands has a FIXED height ("Radius band height", zero line 20 %
 *     up; "EI band height") and the scale inside follows the data: axis top = k * nice step with the largest plotted
 *     value at 85-95 % of it (primitiveAutoAxis), the top numbered. Off: the manual scales above (10 mm per 1 m,
 *     "Curvature plot scale", "EI band scale") and axes (-"Radius axis min" .. +"Max radius", ..., 0 .. "EI axis max").
 * Each band has a DATUM point at x = 0 on its reference line; "Key lines" adds a vertical line (ONE light-grey edge;
 * the drawing restyles it dashed) through every band at FCP, MP(s), MRS, ACP and the extra key points that ask for one. All bodies are points and wires, so the
 * composite can be moved or copied as a unit.
 *
 * SW rout (optional, Table 4, 2026-09-29): the rout surface cut by the datum YZ plane through MRS gives the angle to Z,
 * step-in and distance above base at the section end closest to the centreline (swRout), plus start / stop x and s;
 * a PROFILE SW ROUT point marks that start edge's height at MRS in the profile band.
 *
 * Tables: every row is stored in the composite's attribute (schema primitive/1, primitive_types.fs) and shown by
 * the "Primitive tables" custom table; the same map and the headline values are embedded for Extract variables.
 * Only rows with data are stored: deflection / stiffness with a Target EI, Tip / Tail block when named, the rocker /
 * camber rows unless the baseline is flat within the RSL, radii only where found. The average radius is always taken
 * between the inflection points.
 *
 * Frames: datum = the world origin (X along the ski, Z up) when empty; else, per "Datum uses", the picked point with
 * world axes (ORIGIN, default) or the picked mate connector's own axes (COORDINATE_SYSTEM). x from the datum; s =
 * distance along the profile's bottom wire from the datum (x = 0), same direction as x (straight on along the end
 * tangent where x = 0 lies past an end of the wire). w = across (y), h = along the
 * bottom wire's normal into the ski. XS1 / XS2 = halfway FCP..MRS / MRS..ACP in x. FCP may lie on either side.
 */
annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Export primitive",
            "Feature Type Description" : "The ski's primitive drawing: baseline, profile, unwrapped footprint and radius or curvature plot in one closed composite, plus the primitive tables' data.",
            "Editing Logic Function" : "exportPrimitiveEditLogic" }
export const exportPrimitive = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "Inputs", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Volume", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1,
                        "Description" : "The ski / board volume (one solid part)." }
            definition.volume is Query;

            annotation { "Name" : "FCP - forebody contact point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "A vertex, point or mate connector. The tip is on the FCP side: MRS -> FCP points to the tip. MRS = Mid Running Surface, halfway between FCP and ACP." }
            definition.fcp is Query;

            annotation { "Name" : "ACP - aftbody contact point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "A vertex, point or mate connector." }
            definition.acp is Query;

            annotation { "Name" : "MP - mounting point(s) (optional)", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                        "Description" : "Mounting point(s), in order (several for a snowboard): MP, MP2, ..." }
            definition.mp is Query;

            annotation { "Name" : "Datum (measuring origin)", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "X runs along the ski, Z up. Empty = world origin. Profiles are drawn in its XZ plane." }
            definition.datum is Query;

            annotation { "Name" : "Datum axes", "Default" : PrimitiveDatumUse.ORIGIN, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "Ignored when Datum is empty. World axes: the datum only moves the origin (x is measured along world X from it). Mate connector's axes: its X must run along the ski and Z up." }
            definition.datumUses is PrimitiveDatumUse;

            annotation { "Name" : "Name prefix", "Default" : "", "MaxLength" : 128,
                        "Description" : "Starts every body name: <prefix> PRIMITIVE, <prefix> PRIMITIVE PROFILE TOP, ... Filled with the volume's name when it is picked." }
            definition.prefix is string;
        }

        annotation { "Group Name" : "Baseline and footprint source", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Baseline source", "Default" : PrimitiveSource.VOLUME, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "From the volume: the bottom edge of the profile (the volume cut by the datum XZ plane). From picked wires: e.g. FULL_BASELINE." }
            definition.baselineFrom is PrimitiveSource;

            if (definition.baselineFrom == PrimitiveSource.INPUT)
            {
                annotation { "Name" : "Baseline curve (picked wires)", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                            "Description" : "The ski's baseline as one connected chain in the datum XZ plane (e.g. FULL_BASELINE)." }
                definition.baselineWires is Query;
            }

            annotation { "Name" : "Footprint source", "Default" : PrimitiveSource.VOLUME, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "From the volume: the outline of the part's base, unwrapped along the bottom edge of the profile. From picked wires: the footprint's side curves; flat ones (constant z) are taken as already unwrapped and aligned at MRS, wrapped ones are unwrapped." }
            definition.footprintFrom is PrimitiveSource;

            if (definition.footprintFrom == PrimitiveSource.INPUT)
            {
                annotation { "Name" : "Footprint side curves (L and R)", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                            "Description" : "Both sides of the footprint (e.g. FPT_L and FPT_R)." }
                definition.footprintWires is Query;
            }
        }

        annotation { "Group Name" : "Key locations", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Extra key points", "Item name" : "key point", "Item label template" : "#keyName",
                        "Description" : "Named points of your own (e.g. FB_Mass_location): a Key locations row, a profile point, a plot tick and a data-table row." }
            definition.extraPoints is array;
            for (var item in definition.extraPoints)
            {
                annotation { "Name" : "Name", "Default" : "", "MaxLength" : 64,
                            "Description" : "The key location's name. Operation ids use it with every character other than letters, digits and _ turned into _, so names must differ in those; FCP, ACP, MRS, MP.., XS1, XS2, TIP, TAIL and the footprint points are taken." }
                item.keyName is string;

                annotation { "Name" : "Point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "A vertex, point or mate connector." }
                item.keyPoint is Query;

                annotation { "Name" : "Include in key lines (Plot > Key lines)", "Default" : false,
                            "Description" : "With Key lines (Plot) on: a vertical line through every band at this point." }
                item.showKeyLine is boolean;
            }
        }

        // New 2026-09-29 (Table 4): all picks, empty by default, so saved features gain no table and no change.
        annotation { "Group Name" : "SW rout", "Collapsed By Default" : true }
        {
            annotation { "Name" : "SW rout surface (optional)", "Filter" : EntityType.FACE || (EntityType.BODY && BodyType.SHEET),
                        "Description" : "The sidewall rout surface: faces and / or sheet bodies (several faces allowed; both sides or one). Adds Table 4. Measured at MRS in the datum YZ plane through MRS, on the +Y side (the -Y side mirrored when the surface is only there)." }
            definition.routSurface is Query;

            annotation { "Name" : "Rout start (optional)", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "A vertex, point or mate connector: the rout's start (its x). Empty = the surface's lowest x." }
            definition.routStart is Query;

            annotation { "Name" : "Rout stop (optional)", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "A vertex, point or mate connector: the rout's stop (its x). Empty = the surface's highest x." }
            definition.routStop is Query;
        }

        annotation { "Group Name" : "Plot", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Plot type", "Default" : PrimitivePlot.RADIUS, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "The last band. Sidecut radius (m) or curvature (1/m) of the footprint edges, continuous through inflections. Tables stay in radius." }
            definition.plotMode is PrimitivePlot;

            annotation { "Name" : "Plot x-range", "Default" : PrimitivePlotRegion.FULL, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "Where the radius / curvature is plotted (with its key-location and junction ticks). The band's axes and reference line always span the full ski. The average radius is always taken between the inflection points." }
            definition.plotRegion is PrimitivePlotRegion;

            // New 2026-09-29, default ON (correction 25: saved features migrate to true, intended by the user).
            annotation { "Name" : "Auto scale", "Default" : true,
                        "Description" : "Fixed band heights, scale fitted to the data: the largest plotted radius / curvature (within the Plot x-range and Max radius) and the largest EI sit at 85-95 % of their band's top; ticks on a nice step (1, 2, 5, 10, 20, 25, 50 m; 0.01 1/m steps; 10, 25, 50, 100 N*m^2), the top numbered. Off: the manual scales and axes." }
            definition.autoScale is boolean;

            if (definition.autoScale)
            {
                annotation { "Name" : "Radius band height", "Description" : "Model height of the radius (or curvature) band, axis bottom to top: 80 % above the zero line, 20 % below it (taper, tip, tail). 150 mm prints 30 mm at 1:5." }
                isLength(definition.radiusBandHeight, PRIMITIVE_PLOT_BAND_HEIGHT_BOUNDS);
            }
            else if (definition.plotMode == PrimitivePlot.CURVATURE)
            {
                annotation { "Name" : "Curvature plot scale", "Description" : "Plot height per 0.01 1/m of curvature, e.g. 50 mm: a 14 m sidecut (0.071 1/m) plots 357 mm high." }
                isLength(definition.curvatureScale, PRIMITIVE_CURVATURE_SCALE_BOUNDS);

                annotation { "Name" : "Max curvature (1/m)", "Description" : "Top of the curvature axis; the plot breaks where |curvature| is larger (tip, tail), so Plot x-range = Full ski keeps the axes." }
                isReal(definition.maxCurvature, PRIMITIVE_MAX_CURVATURE_BOUNDS);

                annotation { "Name" : "Curvature axis min (1/m)", "Description" : "The curvature axis runs from minus this to +Max curvature; the plot breaks below it." }
                isReal(definition.curvatureAxisMin, PRIMITIVE_CURVATURE_AXIS_MIN_BOUNDS);
            }
            else
            {
                // A plain number in m: a NEW length parameter's default is migrated into saved features as that number
                // in mm (10 m -> "10.0*mm", 2026-09-29), a real's is not.
                annotation { "Name" : "Radius axis min (m)", "Description" : "The radius axis runs from minus this (taper, tip, tail) to +Max radius; the plot breaks below it." }
                isReal(definition.radiusAxisLow, PRIMITIVE_RADIUS_AXIS_MIN_BOUNDS);
            }

            annotation { "Name" : "Max radius (treated as flat above)", "Description" : "The radius plot breaks where |R| is larger (flat parts, next to an inflection) and the data table's radius is empty there. Enter in m, e.g. 50 m. With Auto scale off it is also the top of the radius axis (10 mm per 1 m: 500 mm of plot)." }
            isLength(definition.radiusLimit, PRIMITIVE_RADIUS_LIMIT_BOUNDS);

            annotation { "Name" : "Key-location tick half-length", "Description" : "Half length of the radius / curvature band's key-location tick marks." }
            isLength(definition.tickLength, PRIMITIVE_TICK_BOUNDS);

            annotation { "Name" : "Band titles and scale numbers", "Default" : true,
                        "Description" : "Band titles (EI, BASELINE, PROFILE, FOOTPRINT, RADIUS (m) / CURVATURE (1/m)) left of the bands and the scales' numbers, as outline text geometry in the composite." }
            definition.labels is boolean;

            if (definition.labels)
            {
                annotation { "Name" : "Text height", "Description" : "Height of the band titles; the scales' numbers are 0.6 of it. Model size: at 1:5 on the sheet, 20 mm prints 4 mm." }
                isLength(definition.textHeight, PRIMITIVE_TEXT_HEIGHT_BOUNDS);
            }

            annotation { "Name" : "Grid lines", "Default" : false,
                        "Description" : "Horizontal grid lines at every tick level of the plot and EI bands: one light-grey edge per level; restyle them (dashed, colour) in the drawing." }
            definition.dashedGrid is boolean;

            annotation { "Name" : "Key lines", "Default" : false,
                        "Description" : "Vertical lines through every band, top to bottom, at FCP, MP(s), MRS, ACP and the extra key points marked for key lines: one light-grey edge per line; restyle them (dashed, colour) in the drawing." }
            definition.keyLines is boolean;

            annotation { "Name" : "Edge junction ticks", "Default" : true,
                        "Description" : "A short tick at every edge junction of the +y footprint and at the same x in the radius / curvature band, so each edge lines up with its plot segment in black and white." }
            definition.junctionTicks is boolean;

            annotation { "Name" : "Band gap", "Description" : "Clear space between the part and the first band, and between bands." }
            isLength(definition.bandGap, PRIMITIVE_BAND_GAP_BOUNDS);
        }

        annotation { "Group Name" : "Stiffness", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Target EI (optional)", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "An EI profile wire in the xSection convention (EI and Cross Section's EI curve): x = world X along the ski, height z = EI with 1 mm = 1 N*m^2. Adds the EI band and Table 2 theoretical deflection (mm / 30 kg) and stiffness (lb/in): 3-point bending, rollers at FCP and ACP, load at MRS." }
            definition.targetEI is Query;

            if (definition.autoScale)
            {
                annotation { "Name" : "EI band height", "Description" : "Used only with a Target EI (Auto scale, Plot group). Model height of the EI band, 0 to the axis top; the largest EI sits at 85-95 % of it." }
                isLength(definition.eiBandHeight, PRIMITIVE_EI_BAND_HEIGHT_BOUNDS);
            }
            else
            {
                annotation { "Name" : "EI band scale (Nm^2 per mm)", "Description" : "Used only with a Target EI. N*m^2 per 1 mm of plot height: 2 = a 150 N*m^2 ski plots 75 mm high. Ticks every 50 N*m^2." }
                isReal(definition.eiScale, PRIMITIVE_EI_SCALE_BOUNDS);

                annotation { "Name" : "EI axis max (Nm^2)", "Description" : "Used only with a Target EI. The EI axis runs from 0 to this; the plot breaks above it." }
                isReal(definition.eiAxisMax, PRIMITIVE_EI_AXIS_MAX_BOUNDS);
            }
        }

        annotation { "Group Name" : "Tooling blocks (Table 5)", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Tip block name source", "Default" : PrimitiveNameFrom.TYPED, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "Type a name: enter the tip tooling block's name. Use a picked wire's name: pick the block's wire." }
            definition.tipBlockFrom is PrimitiveNameFrom;

            if (definition.tipBlockFrom == PrimitiveNameFrom.TYPED)
            {
                annotation { "Name" : "Tip block name", "Default" : "", "MaxLength" : 128,
                            "Description" : "The tip tooling block's name (Table 5). Empty = no row." }
                definition.tipBlock is string;
            }
            else
            {
                annotation { "Name" : "Tip block wire", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1,
                            "Description" : "The tip block's wire; its name goes into Table 5. None picked = no row." }
                definition.tipBlockWire is Query;
            }

            annotation { "Name" : "Tip block wire name", "Default" : "", "MaxLength" : 256, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            definition.tipBlockWireName is string;

            annotation { "Name" : "Tail block name source", "Default" : PrimitiveNameFrom.TYPED, "UIHint" : [UIHint.SHOW_LABEL],
                        "Description" : "Type a name: enter the tail tooling block's name. Use a picked wire's name: pick the block's wire." }
            definition.tailBlockFrom is PrimitiveNameFrom;

            if (definition.tailBlockFrom == PrimitiveNameFrom.TYPED)
            {
                annotation { "Name" : "Tail block name", "Default" : "", "MaxLength" : 128,
                            "Description" : "The tail tooling block's name (Table 5). Empty = no row." }
                definition.tailBlock is string;
            }
            else
            {
                annotation { "Name" : "Tail block wire", "Filter" : EntityType.BODY && BodyType.WIRE, "MaxNumberOfPicks" : 1,
                            "Description" : "The tail block's wire; its name goes into Table 5. None picked = no row." }
                definition.tailBlockWire is Query;
            }

            annotation { "Name" : "Tail block wire name", "Default" : "", "MaxLength" : 256, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            definition.tailBlockWireName is string;
        }

        annotation { "Group Name" : "Data table", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Data table rows (FCP to ACP)", "Description" : "Evenly spaced in x from FCP to ACP, both included." }
            isInteger(definition.dataPoints, PRIMITIVE_DATA_POINTS_BOUNDS);

            annotation { "Name" : "Add rows at key locations", "Default" : true,
                        "Description" : "Add rows at XS1, MRS, XS2 and every extra key point between FCP and ACP when no row falls there (within 0.01 mm)." }
            definition.forceStations is boolean;

            annotation { "Name" : "Show # column in RSL data (table 6)", "Default" : true,
                        "Description" : "RSL data rows are sorted by x (ascending) and numbered from 0 at the lowest x; a row at a key location (FCP, ACP, XS1, MRS, XS2, MP, extra key points) shows that name instead of its number. Numbers and names are always stored; this shows the # column." }
            definition.stationNumbers is boolean;
        }

        annotation { "Group Name" : "Output", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Publish as query variable", "Default" : "primitive", "MaxLength" : 64,
                        "Description" : "Empty = don't publish. Apps expect 'primitive'." }
            definition.queryVariable is string;
        }
    }
    {
        if (isQueryEmpty(context, definition.volume))
        {
            throw regenError("Select the volume (the ski part).", ["volume"]);
        }
        if (definition.prefix == "")
        {
            throw regenError("Enter a name prefix, e.g. the model name.", ["prefix"]);
        }
        const title = definition.prefix ~ " PRIMITIVE";
        const fromVolumeBaseline = definition.baselineFrom == PrimitiveSource.VOLUME;
        const fromVolumeFootprint = definition.footprintFrom == PrimitiveSource.VOLUME;
        const curvatureMode = definition.plotMode == PrimitivePlot.CURVATURE;
        const autoScale = definition.autoScale;
        const plotBand = curvatureMode ? "curvature" : "radius";
        const plotLabel = PRIMITIVE_BAND_LABELS[plotBand];
        var notes = [];

        // ---- Frames and points (LOCAL frame: the datum on the world origin) ----
        const extras = extraKeyPoints(context, definition.extraPoints);
        const datum = primitiveDatum(context, definition.datum, definition.datumUses);
        const isIdentity = isQueryEmpty(context, definition.datum);
        const toLocal = fromWorld(datum);
        const fcpWorld = primitivePoint(context, definition.fcp, "FCP", "fcp");
        const acpWorld = primitivePoint(context, definition.acp, "ACP", "acp");
        const fcp = toLocal * fcpWorld;
        const acp = toLocal * acpWorld;
        var mps = [];
        for (var p in primitivePoints(context, definition.mp))
        {
            mps = append(mps, toLocal * p);
        }
        if (abs(fcp[0] - acp[0]) < 1 * millimeter)
        {
            throw regenError("FCP and ACP must be apart along the datum X.", ["fcp", "acp"]);
        }
        const dirSign = fcp[0] > acp[0] ? 1 : -1;
        const mrs = (fcp + acp) / 2;
        const volumeBox = evBox3d(context, { "topology" : definition.volume, "cSys" : datum, "tight" : true });

        // ---- Profile and the bottom-wire frame ----
        const profile = primitiveProfile(context, id + "profile", definition.volume, datum, toLocal, isIdentity, dirSign, mrs[0]);
        if (profile.loops > 1)
        {
            notes = append(notes, "the mid plane cuts the volume in " ~ profile.loops ~ " loops; the longest is used");
        }
        const frame = primitiveFrame(context, profile.bottomChain, mrs, dirSign);

        // ---- Key locations (Table 3) ----
        const xXs1 = (fcp[0] + mrs[0]) / 2;
        const xXs2 = (mrs[0] + acp[0]) / 2;
        const xsBottom = primitiveChainAtX(context, frame.chain, frame.lookup, [xXs1, xXs2]);
        var keyPoints = [
            { "key" : "FCP", "name" : "FCP", "point" : fcp },
            { "key" : "ACP", "name" : "ACP", "point" : acp },
            { "key" : "MRS", "name" : "MRS", "point" : mrs }
        ];
        for (var i = 0; i < size(mps); i += 1)
        {
            const mpKey = i == 0 ? "MP" : "MP" ~ (i + 1);
            keyPoints = append(keyPoints, { "key" : mpKey, "name" : mpKey, "point" : mps[i] });
        }
        keyPoints = concatenateArrays([keyPoints, [
                        { "key" : "XS1", "name" : "XS1", "point" : xsBottom[0].point },
                        { "key" : "XS2", "name" : "XS2", "point" : xsBottom[1].point },
                        { "key" : "TIP", "name" : "TIP", "point" : profile.tip },
                        { "key" : "TAIL", "name" : "TAIL", "point" : profile.tail }
                    ]]);
        for (var e in extras)
        {
            keyPoints = append(keyPoints, { "key" : e.key, "name" : e.name, "point" : toLocal * e.world, "extra" : true });
        }
        var keys = {};
        var keyRows = [];
        for (var kp in keyPoints)
        {
            const loc = primitiveLocate(context, frame, kp.point);
            keys[kp.key] = loc;
            keyRows = append(keyRows, { "key" : kp.key, "name" : kp.name,
                        "x" : primitiveMM(loc.x), "y" : primitiveMM(loc.y), "z" : primitiveMM(loc.z),
                        "s" : primitiveMM(loc.s), "w" : primitiveMM(loc.w), "h" : primitiveMM(loc.h), "extra" : kp.extra == true });
        }
        // Table 3 in x order (ascending), numbered from 0 at the lowest x. Dist. from tail (2026-09-29) = |x - x(TAIL)|,
        // along x from the profile's TAIL extreme point.
        const xTail = keys.TAIL.x;
        keyRows = sort(keyRows, function(a, b) { return a.x - b.x; });
        for (var i = 0; i < size(keyRows); i += 1)
        {
            keyRows[i].station = i;
            keyRows[i].distFromTail = primitiveMM(abs(keys[keyRows[i].key].x - xTail));
        }

        // ---- Table 4: SW rout (optional) ----
        const rout = swRout(context, id + "swRout", definition, datum, toLocal, frame, definition.volume, xTail);
        if (rout != undefined && rout.note != undefined)
        {
            notes = append(notes, rout.note);
        }

        // ---- Table 1: theoretical scale factors ----
        const scale = primitiveScaleFactors(context, frame, profile.topChain, keys.FCP.a, keys.ACP.a);

        // ---- Table 5: baseline ----
        const baseline = primitiveBaseline(context, id + "baseline", fromVolumeBaseline, profile.bottom,
            fromVolumeBaseline ? qNothing() : wireEdges(definition.baselineWires), toLocal, isIdentity, fcp, acp, profile.tail, frame);
        if (baseline.result == undefined)
        {
            notes = append(notes, "the baseline could not be analysed");
        }
        else if (baseline.flat)
        {
            notes = append(notes, "the baseline is flat within the RSL (within " ~ primitiveRound(baseline.deviation / millimeter, 4) ~
                " mm of the FCP - ACP chord): no rocker contacts, minima or camber");
        }
        const baselineRows = primitiveBaselineRows(context, frame, baseline, blockName(context, definition, "tip"), blockName(context, definition, "tail"));

        // ---- Table 2: theoretical deflection / stiffness from the target EI (xSection convention: world X, 1 mm = 1 N*m^2) ----
        var beam = undefined;
        if (!isQueryEmpty(context, definition.targetEI))
        {
            const eiData = getEIFromEdges(context, wireEdges(definition.targetEI), fcpWorld[0], acpWorld[0]);
            if (size(eiData) < 2)
            {
                throw regenError("The target EI gives no EI samples: select the EI wire (or its edges).", ["targetEI"]);
            }
            if (eiData[0].x > min(fcpWorld[0], acpWorld[0]) + 1 * millimeter || eiData[size(eiData) - 1].x < max(fcpWorld[0], acpWorld[0]) - 1 * millimeter)
            {
                notes = append(notes, "the target EI does not span FCP - ACP (world X); its end values are held flat");
            }
            beam = computeBeamStiffness(eiData, fcpWorld[0], acpWorld[0]);
        }
        // EI band geometry (built at height 0, moved into place after stacking). Frame x over the volume's full extent,
        // EI 0 .. the axis top; the plot is cut to that frame. Auto scale (2026-09-29): fixed "EI band height", the top a
        // nice multiple with the largest EI at 85-95 % of it; else EI scale / EI axis max (fixed axes of 2026-09-29).
        const eiFrameLo = volumeBox.minCorner[0];
        const eiFrameHi = volumeBox.maxCorner[0];
        var eiPerUnit = millimeter / definition.eiScale;
        var eiTop = definition.eiAxisMax;
        var eiStep = PRIMITIVE_EI_GRID_STEP;
        var eiMax = undefined;
        var eiPlot = undefined;
        if (beam != undefined)
        {
            if (autoScale)
            {
                eiMax = primitiveEIMax(context, wireEdges(definition.targetEI), toLocal, eiFrameLo, eiFrameHi);
                const eiAxis = primitiveAutoAxis(eiMax > 0 ? eiMax : definition.eiAxisMax * AUTO_AIM, PRIMITIVE_EI_STEPS);
                eiTop = eiAxis.top;
                eiStep = eiAxis.step;
                eiPerUnit = definition.eiBandHeight / eiTop;
            }
            eiPlot = primitiveEIPlot(context, id + "eiPlot", wireEdges(definition.targetEI), toLocal, 0 * meter, eiPerUnit,
                eiFrameLo, eiFrameHi, eiTop);
            if (eiPlot.hi > eiTop)
            {
                notes = append(notes, "the target EI exceeds EI axis max (" ~ eiTop ~ " N*m^2): its plot breaks above it");
            }
        }

        // ---- Footprint: unwrap, analyse (Table 2), radius ----
        const source = primitiveFootprintSource(context, id + "footprintSource", fromVolumeFootprint, definition.volume,
            fromVolumeFootprint ? qNothing() : wireEdges(definition.footprintWires), frame, toWorld(datum), toLocal, isIdentity);
        const unwrapped = primitiveUnwrap(context, id + "unwrap", frame, source.edges, !fromVolumeFootprint, definition.radiusLimit,
            source.sections);
        opDeleteBodies(context, id + "deleteFootprintSource", { "entities" : source.bodies });
        const uFcp = primitiveU(frame, keys.FCP.a);
        const uAcp = primitiveU(frame, keys.ACP.a);
        const fpt = primitiveFootprintAnalysis(context, unwrapped.bodies, uFcp, uAcp, frame.xMrs);
        const fr = fpt.result;
        const fptPoints = {
                "FB_WIDEST" : fr.fbWidestData.point,
                "AB_WIDEST" : fr.abWidestData.point,
                "WAIST" : fr.waist.point,
                "FB_INFLECTION" : fr.fbInflectionData.point,
                "AB_INFLECTION" : fr.abInflectionData.point
            };
        if (!fr.hasInflections)
        {
            notes = append(notes, "no sidecut inflection found on one side; the widest point stands in for it");
        }

        // ---- Data table (within RSL) ----
        var forced = [xXs1, mrs[0], xXs2];
        for (var e in extras)
        {
            const x = keys[e.key].x;
            if (x >= min(fcp[0], acp[0]) - 0.01 * millimeter && x <= max(fcp[0], acp[0]) + 0.01 * millimeter)
            {
                forced = append(forced, x);
            }
            else if (definition.forceStations)
            {
                notes = append(notes, "extra key point " ~ e.name ~ " lies outside the RSL: no data-table row");
            }
        }
        const dataX = dataStations(definition, fcp[0], acp[0], forced);
        const dataBottom = primitiveChainAtX(context, frame.chain, frame.lookup, dataX);
        const dataHeights = primitiveBaselineHeights(context, baseline, dataX, fcp[0], acp[0]);
        // Footprint crossings for every row in one batch: first the rows' u, then the u the radius is read at.
        var rowU = [];
        var radiusU = [];
        for (var j = 0; j < size(dataX); j += 1)
        {
            const u = primitiveU(frame, dataBottom[j].a);
            // At an edge junction (FCP / ACP often are) the radius is taken on the RSL side.
            const inward = abs(u - frame.xMrs) > RADIUS_SIDE_STEP ? (frame.xMrs > u ? 1 : -1) * RADIUS_SIDE_STEP : 0 * meter;
            rowU = append(rowU, u);
            radiusU = append(radiusU, u + inward);
        }
        const crossings = primitiveFootprintAtMany(unwrapped.samples, concatenateArrays([rowU, radiusU]));
        // A row at a key location (|dx| < 0.01 mm; not TIP / TAIL) is named by it ("MRS", several joined with "/");
        // the number still counts every row.
        var dataRows = [];
        for (var j = 0; j < size(dataX); j += 1)
        {
            var rowName = "";
            for (var kp in keyPoints)
            {
                if (kp.key != "TIP" && kp.key != "TAIL" && abs(keys[kp.key].x - dataX[j]) < 0.01 * millimeter)
                {
                    rowName = rowName == "" ? kp.name : rowName ~ "/" ~ kp.name;
                }
            }
            const b = dataBottom[j];
            const s = primitiveS(frame, b.a);
            const up = primitiveUp(frame, b.tangent);
            const topHit = primitiveChainCrossing(context, profile.topChain, b.point, up);
            const across = crossings[j];
            const radius = crossings[size(dataX) + j].radius;
            dataRows = append(dataRows, {
                        "station" : j,
                        "name" : rowName,
                        "x" : primitiveMM(dataX[j]),
                        "s" : primitiveMM(s),
                        "y" : across.hit ? primitiveMM(across.yMax) : PRIMITIVE_NOT_FOUND,
                        "skiWidth" : across.hit ? primitiveMM(across.yMax - across.yMin) : PRIMITIVE_NOT_FOUND,
                        "z" : primitiveMM(b.point[2]),
                        "skiThck" : topHit == undefined ? PRIMITIVE_NOT_FOUND : primitiveMM(dot(topHit.point - b.point, up)),
                        "baselineHeight" : primitiveMM(dataHeights[j]),
                        "radius" : radius == undefined ? "" : primitiveRound(radius, 4)
                    });
        }

        // ---- Table 2: metadata ----
        const metaRows = metadataRows(fpt, fcp, acp, volumeBox, beam);

        // ---- Plot band: fixed axes (2026-09-29), region, runs ----
        // The frame (axes, reference line, grid, numbers) always spans the full footprint in u and a fixed value range
        // (radius: -Radius axis min .. +Max radius; curvature: -Curvature axis min .. +Max curvature), so the band's
        // size and the stacking never depend on the data. Plot x-range only cuts the plotted data (and its ticks);
        // values outside the axis break the line.
        const fpBox = evBox3d(context, { "topology" : unwrapped.bodies, "tight" : true });
        const frameLo = fpBox.minCorner[0];
        const frameHi = fpBox.maxCorner[0];
        const region = plotRegion(definition.plotRegion, fpBox, uFcp, uAcp, fr);
        var perUnit = PRIMITIVE_RADIUS_PLOT_SCALE * meter;
        var perLevel = perUnit;
        var levelStep = PRIMITIVE_RADIUS_GRID_STEP;
        var axisLo = 0;
        var axisHi = 0;
        var valueLo = 0;
        var valueHi = 0;
        var runs = [];
        // One level unit in value units: 0.01 1/m of curvature, 1 m of radius.
        const levelUnit = curvatureMode ? PRIMITIVE_CURVATURE_UNIT : 1;
        if (curvatureMode)
        {
            // Levels in 0.01 1/m; values in 1/m. The runs join at the manual scale in both modes (a continuity test).
            perLevel = definition.curvatureScale;
            perUnit = perLevel / PRIMITIVE_CURVATURE_UNIT;
            axisLo = -definition.curvatureAxisMin / PRIMITIVE_CURVATURE_UNIT;
            axisHi = definition.maxCurvature / PRIMITIVE_CURVATURE_UNIT;
            valueLo = -min(definition.curvatureAxisMin, definition.maxCurvature);
            valueHi = definition.maxCurvature;
            levelStep = primitiveNiceStep(1, ceil((axisHi - axisLo) / PRIMITIVE_MAX_LEVELS - 1e-9));
            runs = primitivePlotRuns(unwrapped.samples, "K", false, perUnit);
        }
        else
        {
            // Levels and values in m of radius.
            axisLo = -definition.radiusAxisLow;
            axisHi = definition.radiusLimit / meter;
            valueLo = -min(definition.radiusAxisLow, definition.radiusLimit / meter);
            valueHi = definition.radiusLimit / meter;
            runs = primitiveRadiusRuns(unwrapped.samples);
        }
        const clipLo = region.clip ? region.lo : frameLo - 1 * meter;
        const clipHi = region.clip ? region.hi : frameHi + 1 * meter;
        // Auto scale (2026-09-29): fixed band height, zero line at PRIMITIVE_PLOT_NEGATIVE_FRACTION from the bottom (never
        // moves), the positive axis top = a nice multiple with the largest plotted value (inside the Plot x-range and
        // Max radius) at 85-95 % of it; the negative side takes the same scale down to the band's bottom.
        var plotMax = undefined;
        var plotFill = undefined;
        const bandNegative = definition.radiusBandHeight * PRIMITIVE_PLOT_NEGATIVE_FRACTION;
        const bandPositive = definition.radiusBandHeight - bandNegative;
        if (autoScale)
        {
            const limit = curvatureMode ? 1e12 : definition.radiusLimit / meter;
            runs = primitiveClipRuns(runs, clipLo, clipHi, -limit, limit);
            plotMax = 0;
            for (var run in runs)
            {
                for (var p in run)
                {
                    plotMax = max(plotMax, p[1]);
                }
            }
            const manualTop = curvatureMode ? definition.maxCurvature : definition.radiusLimit / meter;
            const axis = primitiveAutoAxis((plotMax > 0 ? plotMax : manualTop * AUTO_AIM) / levelUnit, PRIMITIVE_PLOT_STEPS);
            plotFill = plotMax > 0 ? axis.fill : 0;
            levelStep = axis.step;
            axisHi = axis.top;
            perLevel = bandPositive / axisHi;
            perUnit = perLevel / levelUnit;
            axisLo = -bandNegative / perLevel;
            valueLo = axisLo * levelUnit;
            valueHi = min(axisHi * levelUnit, limit);
        }
        runs = primitiveClipRuns(runs, clipLo, clipHi, valueLo, valueHi);
        const labelHeight = definition.labels ? definition.textHeight * 0.6 : undefined;
        const levels = primitiveLevelsWithin(axisLo, axisHi, levelStep);
        const plotFormat = { "digits" : curvatureMode ? 2 : 0, "signed" : !curvatureMode,
                "labelStep" : primitiveLabelStep(levelStep, perLevel, labelHeight), "axisLo" : axisLo, "axisHi" : axisHi,
                "labelTop" : autoScale ? axisHi : undefined };

        // ---- Bands: extents, stacking ----
        const labelHalf = definition.labels && size(levels) > 1 ? labelHeight / 2 : 0 * meter;
        const eiLevels = eiPlot == undefined ? [] : primitiveLevelsWithin(0, eiTop, eiStep);
        const eiLabelHalf = definition.labels && size(eiLevels) > 1 ? labelHeight / 2 : 0 * meter;
        const tick = definition.tickLength;
        const profileBodies = qUnion([profile.bottom, profile.top, profile.tipEnd, profile.tailEnd]);
        var bandKeys = [];
        var extents = [];
        if (eiPlot != undefined)
        {
            bandKeys = ["ei"];
            extents = [{ "lo" : -eiLabelHalf, "hi" : max(eiTop * eiPerUnit, eiLevels[size(eiLevels) - 1] * eiPerUnit + eiLabelHalf) }];
        }
        bandKeys = concatenateArrays([bandKeys, ["baseline", "profile", "footprint", plotBand]]);
        // Auto scale: the plot band's extent is the band height (+ half a number at each end), whatever the data.
        const plotExtent = autoScale ?
            { "lo" : min(-bandNegative - labelHalf, -tick), "hi" : max(bandPositive + labelHalf, tick) } :
            { "lo" : min([axisLo * perLevel, -tick, levels[0] * perLevel - labelHalf]),
              "hi" : max([axisHi * perLevel, tick, levels[size(levels) - 1] * perLevel + labelHalf]) };
        extents = concatenateArrays([extents, [
            primitiveZExtent(context, baseline.body),
            primitiveZExtent(context, profileBodies),
            { "lo" : fpBox.minCorner[1], "hi" : fpBox.maxCorner[1] },
            plotExtent
        ]]);
        const offsets = primitiveStack(extents, volumeBox.minCorner[2], definition.bandGap);
        var bandZ = {};
        for (var i = 0; i < size(bandKeys); i += 1)
        {
            bandZ[bandKeys[i]] = offsets[i];
        }
        const zBaseline = bandZ.baseline;
        const zProfile = bandZ.profile;
        const zFootprint = bandZ.footprint;
        const zPlot = bandZ[plotBand];
        const zero = 0 * meter;
        var members = [];
        var queries = {};

        // Band 0 (with a target EI): the EI profile (purple), zero reference, scale frame (grey)
        if (eiPlot != undefined)
        {
            const zEI = bandZ.ei;
            const eiBodies = qUnion(eiPlot.bodies);
            if (size(eiPlot.bodies) > 0)
            {
                opTransform(context, id + "moveEI", { "bodies" : eiBodies, "transform" : transform(vector(zero, zero, zEI)) });
                primitiveName(context, eiBodies, title ~ " EI");
                primitiveColour(context, eiBodies, PRIMITIVE_COLOURS.ei);
            }
            members = append(members, eiBodies);
            queries.ei = eiBodies;
            const eiReference = primitiveSegment(context, id + "eiReference", vector(eiFrameLo, zero, zEI), vector(eiFrameHi, zero, zEI),
                title ~ " EI REFERENCE");
            primitiveColour(context, eiReference, PRIMITIVE_COLOURS.frame);
            members = append(members, eiReference);
            members = concatenateArrays([members, primitiveScaleFrame(context, id + "eiFrame", "EI", eiLevels, eiPerUnit, eiFrameLo, eiFrameHi,
                            zEI, dirSign, definition.dashedGrid, labelHeight, title,
                            { "digits" : 0, "signed" : false, "labelStep" : primitiveLabelStep(eiStep, eiPerUnit, labelHeight),
                              "axisLo" : 0, "axisHi" : eiTop, "labelTop" : autoScale ? eiTop : undefined })]);
        }

        // Band 1: baseline
        var bandStart = size(members);
        opTransform(context, id + "moveBaseline", { "bodies" : baseline.body, "transform" : transform(vector(zero, zero, zBaseline)) });
        primitiveName(context, baseline.body, title ~ " BASELINE");
        members = append(members, baseline.body);
        queries.baseline = baseline.body;
        // The baseline's ends (its chain runs TAIL -> TIP), then the analysed points (only FCP / ACP when flat).
        var blPoints = { "TAIL" : baseline.chain.startPoint, "TIP" : baseline.chain.endPoint };
        if (baseline.result != undefined)
        {
            const r = baseline.result;
            blPoints.FCP = r.fcp_pt;
            blPoints.ACP = r.acp_pt;
            if (!baseline.flat)
            {
                blPoints.FRCP = r.frcp_pt;
                blPoints.ARCP = r.arcp_pt;
                blPoints.MCL = r.mcl_pt;
                blPoints.FB_MIN = r.fb_min_pt;
                blPoints.AB_MIN = r.ab_min_pt;
            }
        }
        for (var entry in blPoints)
        {
            if (entry.value is Vector)
            {
                members = append(members, primitivePointBody(context, id + ("baselinePt" ~ entry.key),
                            entry.value + vector(zero, zero, zBaseline), title ~ " BASELINE " ~ entry.key));
            }
        }
        primitiveColour(context, qUnion(subArray(members, bandStart)), PRIMITIVE_COLOURS.baseline);

        // Band 2: profile
        bandStart = size(members);
        const profileMove = transform(vector(zero, zero, zProfile));
        opTransform(context, id + "moveProfile", { "bodies" : profileBodies, "transform" : profileMove });
        primitiveName(context, profile.bottom, title ~ " PROFILE BOTTOM");
        primitiveName(context, profile.top, title ~ " PROFILE TOP");
        if (!isQueryEmpty(context, profile.tipEnd))
        {
            primitiveName(context, profile.tipEnd, title ~ " PROFILE TIP END");
        }
        if (!isQueryEmpty(context, profile.tailEnd))
        {
            primitiveName(context, profile.tailEnd, title ~ " PROFILE TAIL END");
        }
        members = append(members, profileBodies);
        queries.profileBottom = profile.bottom;
        queries.profileTop = profile.top;
        for (var kp in keyPoints)
        {
            // TIP / TAIL: the extreme points themselves; the others at their foot on the bottom wire.
            const at = (kp.key == "TIP" || kp.key == "TAIL") ? kp.point : keys[kp.key].foot;
            members = append(members, primitivePointBody(context, id + ("profilePt" ~ primitiveKey(kp.key)),
                        profileMove * at, title ~ " PROFILE " ~ kp.name));
        }
        if (scale.topFcp != undefined)
        {
            members = append(members, primitivePointBody(context, id + "profilePtTopFCP", profileMove * scale.topFcp, title ~ " PROFILE TOP FCP"));
        }
        if (rout != undefined && rout.start != undefined)
        {
            // The SW rout's start edge at MRS, seen from the side (x of MRS, its height).
            members = append(members, primitivePointBody(context, id + "profilePtSwRout",
                        profileMove * vector(rout.start[0], zero, rout.start[2]), title ~ " PROFILE SW ROUT"));
        }
        if (scale.topAcp != undefined)
        {
            members = append(members, primitivePointBody(context, id + "profilePtTopACP", profileMove * scale.topAcp, title ~ " PROFILE TOP ACP"));
        }
        primitiveColour(context, qUnion(subArray(members, bandStart)), PRIMITIVE_COLOURS.profile);

        // Band 3: footprint (plan y becomes height)
        bandStart = size(members);
        const flatToBand = transform(vector(zero, zero, zFootprint)) * rotationAround(line(vector(zero, zero, zero), vector(1, 0, 0)), 90 * degree);
        opTransform(context, id + "moveFootprint", { "bodies" : unwrapped.bodies, "transform" : flatToBand });
        primitiveName(context, unwrapped.bodies, title ~ " FOOTPRINT");
        members = append(members, unwrapped.bodies);
        queries.footprint = unwrapped.bodies;
        for (var entry in fptPoints)
        {
            members = append(members, primitivePointBody(context, id + ("footprintPt" ~ entry.key),
                        flatToBand * entry.value, title ~ " FOOTPRINT " ~ entry.key));
        }
        primitiveColour(context, qUnion(subArray(members, bandStart)), PRIMITIVE_COLOURS.footprint);
        // Junction ticks across the +y outline (grey), keyed by their u.
        var junctions = [];
        if (definition.junctionTicks)
        {
            var seenKeys = {};
            for (var jt in primitiveJunctions(unwrapped.samples))
            {
                var key = uKey(jt.u);
                if (seenKeys[key] != undefined)
                {
                    key = key ~ "Y" ~ round(jt.y / millimeter * 1000);
                }
                seenKeys[key] = true;
                junctions = append(junctions, { "u" : jt.u, "y" : jt.y, "key" : key });
            }
        }
        bandStart = size(members);
        for (var jt in junctions)
        {
            members = append(members, primitiveSegment(context, id + ("footprintJunction" ~ jt.key),
                        flatToBand * vector(jt.u, jt.y - PRIMITIVE_AXIS_TICK, zero), flatToBand * vector(jt.u, jt.y + PRIMITIVE_AXIS_TICK, zero),
                        title ~ " FOOTPRINT JUNCTION"));
        }
        primitiveColour(context, qUnion(subArray(members, bandStart)), PRIMITIVE_COLOURS.frame);

        // Band 4: radius or curvature plot (red); reference line, key-location and junction ticks, scale frame, grid and numbers (grey)
        const plot = qUnion(primitiveRadiusPlot(context, id + "radiusPlot", runs, zPlot, perUnit));
        if (!isQueryEmpty(context, plot))
        {
            primitiveName(context, plot, title ~ " " ~ plotLabel);
            primitiveColour(context, plot, PRIMITIVE_COLOURS[plotBand]);
        }
        members = append(members, plot);
        queries[plotBand] = plot;
        bandStart = size(members);
        const reference = primitiveSegment(context, id + "radiusReference", vector(frameLo, zero, zPlot),
            vector(frameHi, zero, zPlot), title ~ " " ~ plotLabel ~ " REFERENCE");
        members = append(members, reference);
        var ticks = [];
        for (var kp in keyPoints)
        {
            if (kp.key != "TIP" && kp.key != "TAIL")
            {
                ticks = append(ticks, { "key" : kp.key, "name" : kp.name, "u" : primitiveU(frame, keys[kp.key].a) });
            }
        }
        for (var name in ["FB_WIDEST", "AB_WIDEST", "FB_INFLECTION", "AB_INFLECTION"])
        {
            ticks = append(ticks, { "key" : name, "name" : name, "u" : fptPoints[name][0] });
        }
        for (var t in ticks)
        {
            if (inRegion(region, t.u))
            {
                members = append(members, primitiveSegment(context, id + ("tick" ~ primitiveKey(t.key)),
                            vector(t.u, zero, zPlot - tick), vector(t.u, zero, zPlot + tick), title ~ " " ~ plotLabel ~ " TICK " ~ t.name));
            }
        }
        var plotSeen = {};
        for (var jt in junctions)
        {
            if (inRegion(region, jt.u) && plotSeen[uKey(jt.u)] == undefined)
            {
                plotSeen[uKey(jt.u)] = true;
                members = append(members, primitiveSegment(context, id + ("plotJunction" ~ uKey(jt.u)),
                            vector(jt.u, zero, zPlot - PRIMITIVE_AXIS_TICK), vector(jt.u, zero, zPlot + PRIMITIVE_AXIS_TICK),
                            title ~ " " ~ plotLabel ~ " JUNCTION"));
            }
        }
        primitiveColour(context, qUnion(subArray(members, bandStart)), PRIMITIVE_COLOURS.frame);
        members = concatenateArrays([members, primitiveScaleFrame(context, id + "radiusFrame", plotLabel, levels, perLevel, frameLo,
                        frameHi, zPlot, dirSign, definition.dashedGrid, labelHeight, title, plotFormat)]);

        // Band datum points (x = 0 on each band's reference line), for ordinate dimensions.
        for (var band in bandKeys)
        {
            const label = PRIMITIVE_BAND_LABELS[band];
            const datumPoint = primitivePointBody(context, id + ("datum" ~ label), vector(zero, zero, bandZ[band]), title ~ " " ~ label ~ " DATUM");
            primitiveColour(context, datumPoint, PRIMITIVE_COLOURS.frame);
            members = append(members, datumPoint);
        }

        // Key lines: one light-grey vertical edge each through every band, from the top band's top to the bottom band's bottom.
        var keyLineNames = [];
        if (definition.keyLines)
        {
            const zTop = offsets[0] + extents[0].hi;
            const zBottom = offsets[size(offsets) - 1] + extents[size(extents) - 1].lo;
            for (var kp in keyPoints)
            {
                const wanted = kp.extra == true ? extraShowsLine(extras, kp.key) : (kp.key == "FCP" || kp.key == "ACP" || kp.key == "MRS" || match(kp.key, "MP[0-9]*").hasMatch);
                if (wanted)
                {
                    members = append(members, primitiveGridLine(context, id + ("keyLine" ~ primitiveKey(kp.key)),
                                vector(keys[kp.key].x, zero, zTop), vector(keys[kp.key].x, zero, zBottom), title ~ " KEY LINE " ~ kp.name));
                    keyLineNames = append(keyLineNames, kp.name);
                }
            }
        }

        // Band titles: one right-aligned column left of everything, centred on each band's reference line.
        if (definition.labels)
        {
            const left = evBox3d(context, { "topology" : qUnion(members), "tight" : true }).minCorner[0] - definition.textHeight;
            for (var band in bandKeys)
            {
                const titleText = primitiveLabel(context, id + ("title" ~ PRIMITIVE_BAND_LABELS[band]), PRIMITIVE_BAND_TITLES[band],
                    vector(left, zero, bandZ[band]), definition.textHeight, "RIGHT", title ~ " " ~ PRIMITIVE_BAND_LABELS[band] ~ " TITLE");
                primitiveColour(context, titleText, PRIMITIVE_COLOURS[band]);
                members = append(members, titleText);
            }
        }

        // ---- Into the datum frame, one closed composite ----
        const allMembers = qUnion(members);
        primitiveMove(context, id + "toDatum", allMembers, toWorld(datum), isIdentity);

        var extraNames = [];
        for (var e in extras)
        {
            extraNames = append(extraNames, e.name);
        }
        const data = {
                "schema" : PRIMITIVE_SCHEMA,
                "title" : title,
                "prefix" : definition.prefix,
                "units" : { "length" : "mm", "radius" : "m", "angle" : "deg", "curvature" : "1/m" },
                "settings" : {
                    "datumUses" : definition.datumUses == PrimitiveDatumUse.ORIGIN ? "ORIGIN" : "COORDINATE_SYSTEM",
                    "targetEI" : beam != undefined,
                    "baselineFlat" : baseline.flat,
                    "baselineFrom" : fromVolumeBaseline ? "VOLUME" : "INPUT",
                    "footprintFrom" : fromVolumeFootprint ? "VOLUME" : "INPUT",
                    "radiusBetween" : AVERAGE_BETWEEN,
                    "plot" : curvatureMode ? "CURVATURE" : "RADIUS",
                    "plotRegion" : region.name,
                    "dataPoints" : definition.dataPoints,
                    "forceStations" : definition.forceStations,
                    "stationNumbers" : definition.stationNumbers,
                    "extraKeyPoints" : extraNames,
                    "keyLines" : keyLineNames,
                    "junctionTicks" : definition.junctionTicks,
                    "autoScale" : autoScale,
                    "swRout" : rout != undefined,
                    "tipTowards" : dirSign > 0 ? "+X" : "-X"
                },
                "bands" : bandsData(bandZ, definition, region, [frameLo, frameHi], [axisLo, axisHi], levels, levelStep, perLevel, curvatureMode,
                    size(junctions), eiPlot == undefined ? undefined : [eiFrameLo, eiFrameHi],
                    { "max" : plotMax, "fill" : plotFill, "eiTop" : eiTop, "eiStep" : eiStep, "eiPerUnit" : eiPerUnit, "eiMax" : eiMax }),
                "scaleFactors" : scale.rows,
                "metadata" : metaRows,
                "keyLocations" : keyRows,
                "swRout" : rout == undefined ? [] : rout.rows,
                "swRoutSection" : rout == undefined ? {} : rout.section,
                "baseline" : baselineRows,
                "data" : dataRows,
                "footprint" : footprintSummary(frame, fptPoints, fpt, unwrapped, source)
            };
        const composite = primitiveComposite(context, id + "composite", allMembers, title, data);
        queries.primitive = composite;

        if (definition.queryVariable != "")
        {
            setQueryVariable(context, definition.queryVariable, "The " ~ title ~ " composite (Export primitive).", composite);
        }

        embedStandardOutputs(context, id, {
                    "output" : composite,
                    "outputDescription" : "The " ~ title ~ " composite",
                    "inputs" : definition.volume,
                    "variables" : {
                        "primitive" : extractableVariable(data, "All primitive tables (schema " ~ PRIMITIVE_SCHEMA ~ "): scaleFactors, metadata, keyLocations, baseline, data rows; mm / m / deg."),
                        "rsl" : extractableVariable(abs(acp[0] - fcp[0]), "RSL: FCP to ACP along the datum X."),
                        "averageRadius" : extractableVariable(fpt.average.valid ? fpt.average.avgRadius : 0 * meter, "Average sidecut radius (" ~ AVERAGE_BETWEEN ~ "); 0 = none."),
                        "naturalRadiusWidest" : extractableVariable(fr.naturalRadiusWidest.valid ? fr.naturalRadiusWidest.R : 0 * meter, "Natural radius through the widest points; 0 = none."),
                        "naturalRadiusInflection" : extractableVariable(fr.naturalRadiusInflection.valid ? fr.naturalRadiusInflection.R : 0 * meter, "Natural radius through the inflection points; 0 = none."),
                        "taperAngleWidest" : extractableVariable(fr.foundTaperAngle, "Taper angle between the widest points (+ = forebody wider)."),
                        "taperAngleInflection" : extractableVariable(fr.taperAngleInflection, "Taper angle between the inflection points."),
                        "deflection" : extractableVariable(beam == undefined ? 0 : primitiveRound(beam.estimatedStiffness_mm, 4), "Theoretical deflection (mm / 30 kg at MRS, rollers at FCP / ACP) from the target EI; 0 = no target EI."),
                        "stiffness" : extractableVariable(beam == undefined ? 0 : primitiveRound(beam.estimatedStiffness_lbin, 4), "Theoretical stiffness (lb/in, same load case) from the target EI; 0 = no target EI."),
                        "swRoutAngle" : extractableVariable(routValue(rout, "angle") * degree, "SW rout angle to Z at MRS (start edge tangent); 0 = no rout surface or it does not cross MRS."),
                        "swRoutStepIn" : extractableVariable(routValue(rout, "stepIn") * millimeter, "SW rout step-in at MRS: ski outside to the start edge along y (+ = inside); 0 = none."),
                        "swRoutDistAboveBase" : extractableVariable(routValue(rout, "distAboveBase") * millimeter, "SW rout start edge height above the base at MRS (assumed definition); 0 = none."),
                        "swRoutStartX" : extractableVariable(routPosition(rout, "start", "x") * millimeter, "SW rout start x (datum); 0 = no rout surface."),
                        "swRoutStartS" : extractableVariable(routPosition(rout, "start", "s") * millimeter, "SW rout start s (bottom wire); 0 = no rout surface."),
                        "swRoutStopX" : extractableVariable(routPosition(rout, "stop", "x") * millimeter, "SW rout stop x (datum); 0 = no rout surface."),
                        "swRoutStopS" : extractableVariable(routPosition(rout, "stop", "s") * millimeter, "SW rout stop s (bottom wire); 0 = no rout surface.")
                    },
                    "queries" : queries
                });

        const avgText = fpt.average.valid ? primitiveRound(fpt.average.avgRadius / meter, 3) ~ " m" : "n/a";
        var summary = title ~ ": RSL " ~ primitiveRound(abs(acp[0] - fcp[0]) / millimeter, 2) ~ " mm, average radius " ~ avgText ~
            " (" ~ AVERAGE_BETWEEN ~ "), " ~ size(dataRows) ~ " data rows; footprint " ~
            unwrapped.exact ~ " exact group(s), " ~ unwrapped.fitted ~ " fitted edge(s).";
        if (size(notes) > 0)
        {
            summary = summary ~ " Note: " ~ join(notes, "; ") ~ ".";
        }
        reportFeatureInfo(context, id, summary);
    }, {
        // Hidden manual scales (Auto scale on) and the auto band heights (off) may be absent from a definition.
        "autoScale" : true,
        "radiusBandHeight" : 150 * millimeter,
        "eiBandHeight" : 150 * millimeter,
        "curvatureScale" : 50 * millimeter,
        "maxCurvature" : 0.1,
        "curvatureAxisMin" : 0.02,
        "radiusAxisLow" : 10,
        "eiScale" : 2,
        "eiAxisMax" : 450,
        "routSurface" : qNothing(),
        "routStart" : qNothing(),
        "routStop" : qNothing()
    });

/** The section plane's size at MRS (SW rout). */
const ROUT_PLANE_SIZE = 20 * meter;
/** A rout surface whose x extent reaches MRS within this is cut there. */
const ROUT_REACH = 1e-6 * meter;
/** A section end within this of the ski's outside (|y|) counts as on it, not inside. */
const ROUT_OUTSIDE_TOLERANCE = 1e-3 * millimeter;
/** Section ends within this in height are level (the start edge's tie-break). */
const ROUT_TIE = 1e-6 * meter;

/**
 * Table 4, SW rout (2026-09-29, user definitions; undefined without a rout surface). In the LOCAL frame (datum X
 * along the ski, Z up):
 *     start / stop     x of the picked Rout start / Rout stop, else the surface's lowest / highest x; s = the bottom
 *                      wire's s at that x (primitiveS), Dist. from tail = |x - x(TAIL)|
 *     section          the rout faces cut by the datum YZ plane through MRS; the +Y side's edges (the -Y side's,
 *                      mirrored, when the surface is only there)
 *     start edge       the section's LOWEST end point inside the ski's outside (a height tie goes to the edge rising
 *                      from it); none inside -> the end closest to the centreline. (User rule 2026-09-29 was "the end
 *                      closest to the centreline"; see routAtMrs for why the lowest inner end is used.)
 *     angle            between the section's tangent at the start edge and Z (0..90 deg)
 *     step-in          outside - |y(start edge)|, outside = the volume's largest |y| on that side in the same plane
 *                      (the ski's outermost point at MRS, normally the base edge); + = start edge inside the ski
 *     dist above base  z(start edge) - z(base), base = the profile's bottom wire at MRS (the centreline base)
 *                      -- ASSUMED definition (user, 2026-09-29)
 * Returns { rows (stored), section (the measured coordinates, mm), start (local point of the start edge, or undefined),
 * values { angle deg, stepIn mm, distAboveBase mm } (undefined when not measured), positions { start, stop : { x, s,
 * distFromTail } mm }, note (a message for the feature's info, or undefined) }.
 */
function swRout(context is Context, id is Id, definition is map, datum is CoordSystem, toLocal is Transform, frame is map,
    volume is Query, xTail is ValueWithUnits)
{
    if (isQueryEmpty(context, definition.routSurface))
    {
        return undefined;
    }
    const faces = qUnion([qEntityFilter(definition.routSurface, EntityType.FACE),
                qOwnedByBody(qBodyType(qEntityFilter(definition.routSurface, EntityType.BODY), BodyType.SHEET), EntityType.FACE)]);
    if (isQueryEmpty(context, faces))
    {
        throw regenError("Select the SW rout surface as faces or sheet bodies.", ["routSurface"]);
    }
    const extent = evBox3d(context, { "topology" : faces, "cSys" : datum, "tight" : true });
    var xs = { "start" : extent.minCorner[0], "stop" : extent.maxCorner[0] };
    var origins = { "start" : "surface extent (lowest x)", "stop" : "surface extent (highest x)" };
    for (var which in ["start", "stop"])
    {
        const pick = which == "start" ? definition.routStart : definition.routStop;
        if (!isQueryEmpty(context, pick))
        {
            xs[which] = (toLocal * primitivePoint(context, pick, "Rout " ~ which, which == "start" ? "routStart" : "routStop"))[0];
            origins[which] = "picked";
        }
    }
    const ends = primitiveChainAtX(context, frame.chain, frame.lookup, [xs.start, xs.stop, frame.xMrs]);
    var positions = {};
    for (var k = 0; k < 2; k += 1)
    {
        const which = k == 0 ? "start" : "stop";
        positions[which] = { "x" : primitiveMM(xs[which]), "s" : primitiveMM(primitiveS(frame, ends[k].a)),
                "distFromTail" : primitiveMM(abs(xs[which] - xTail)) };
    }
    const mrsPos = { "x" : primitiveMM(frame.xMrs), "s" : primitiveMM(primitiveS(frame, ends[2].a)),
            "distFromTail" : primitiveMM(abs(frame.xMrs - xTail)) };

    // ---- The section at MRS ----
    var measured = undefined;
    var note = undefined;
    if (frame.xMrs < extent.minCorner[0] - ROUT_REACH || frame.xMrs > extent.maxCorner[0] + ROUT_REACH)
    {
        note = "the SW rout surface does not reach MRS (x " ~ primitiveRound(extent.minCorner[0] / millimeter, 2) ~ " .. " ~
            primitiveRound(extent.maxCorner[0] / millimeter, 2) ~ " mm): Table 4 has start / stop only";
    }
    else
    {
        measured = routAtMrs(context, id, faces, volume, datum, toLocal, frame, ends[2].point);
        if (measured == undefined)
        {
            note = "the SW rout surface does not cross the YZ plane through MRS: Table 4 has start / stop only";
        }
    }

    var rows = [];
    var section = {};
    var values = undefined;
    if (measured != undefined)
    {
        values = { "angle" : primitiveRound(measured.angle / degree, 4), "stepIn" : primitiveMM(measured.stepIn),
                "distAboveBase" : primitiveMM(measured.distAboveBase) };
        rows = [
            mergeMaps({ "key" : "angle", "name" : "SW rout angle", "value" : values.angle, "unit" : "deg",
                    "note" : "Rout section tangent at the start edge to Z, at MRS" }, mrsPos),
            mergeMaps({ "key" : "stepIn", "name" : "Step-in", "value" : values.stepIn, "unit" : "mm",
                    "note" : "Ski outside (largest |y| of the volume at MRS) to the start edge, along y; + = inside" }, mrsPos),
            mergeMaps({ "key" : "distAboveBase", "name" : "Dist. above base", "value" : values.distAboveBase, "unit" : "mm",
                    "note" : "Start edge height (z) above the base (bottom wire at MRS) -- assumed definition" }, mrsPos)
        ];
        section = { "x" : primitiveMM(frame.xMrs), "side" : measured.side > 0 ? "+Y" : "-Y",
                "startY" : primitiveMM(measured.start[1]), "startZ" : primitiveMM(measured.start[2]),
                "outsideY" : primitiveMM(measured.outsideY), "outsideZ" : primitiveMM(measured.outsideZ),
                "baseZ" : primitiveMM(measured.baseZ), "edges" : measured.edges, "startRule" : measured.rule };
    }
    rows = concatenateArrays([rows, [
                mergeMaps({ "key" : "start", "name" : "Start", "value" : "", "unit" : "", "note" : origins.start }, positions.start),
                mergeMaps({ "key" : "stop", "name" : "Stop", "value" : "", "unit" : "", "note" : origins.stop }, positions.stop)
            ]]);
    return { "rows" : rows, "section" : section, "start" : measured == undefined ? undefined : measured.start,
            "values" : values, "positions" : positions, "note" : note };
}

/**
 * The rout faces and the volume cut by the datum YZ plane through MRS (`base` = the bottom wire's point at MRS, local):
 * { side (+1 / -1), start (local point), angle, stepIn, distAboveBase, outsideY, outsideZ (signed y / z of the volume's
 * outermost point on that side), baseZ, edges (section edges used) }, or undefined when the plane misses the faces.
 */
function routAtMrs(context is Context, id is Id, faces is Query, volume is Query, datum is CoordSystem, toLocal is Transform,
    frame is map, base is Vector)
{
    const yAxis = cross(datum.zAxis, datum.xAxis);
    const origin = toWorld(datum) * vector(frame.xMrs, 0 * meter, 0 * meter);
    opPlane(context, id + "mrsPlane", { "plane" : plane(origin, datum.xAxis, yAxis), "width" : ROUT_PLANE_SIZE, "height" : ROUT_PLANE_SIZE });
    const planeFace = qCreatedBy(id + "mrsPlane", EntityType.FACE);
    opIntersectFaces(context, id + "routSection", { "tools" : planeFace, "targets" : faces });
    opIntersectFaces(context, id + "volumeSection", { "tools" : planeFace, "targets" : volume });
    const routEdges = evaluateQuery(context, qCreatedBy(id + "routSection", EntityType.EDGE));
    const volumeEdges = qCreatedBy(id + "volumeSection", EntityType.EDGE);
    var result = undefined;
    if (size(routEdges) > 0 && !isQueryEmpty(context, volumeEdges))
    {
        // End points and tangents of every section edge (local), each edge's side (its midpoint's y) and its rise at
        // that end (the other end's z minus this one's).
        var ends = [];
        var anyPlus = false;
        for (var e in routEdges)
        {
            const tls = evEdgeTangentLines(context, { "edge" : e, "parameters" : [0, 0.5, 1] });
            const edgeSide = (toLocal * tls[1].origin)[1] >= 0 * meter ? 1 : -1;
            anyPlus = anyPlus || edgeSide > 0;
            const p0 = toLocal * tls[0].origin;
            const p1 = toLocal * tls[2].origin;
            ends = append(ends, { "point" : p0, "direction" : toLocal.linear * tls[0].direction, "side" : edgeSide, "rise" : p1[2] - p0[2] });
            ends = append(ends, { "point" : p1, "direction" : toLocal.linear * tls[2].direction, "side" : edgeSide, "rise" : p0[2] - p1[2] });
        }
        const side = anyPlus ? 1 : -1;
        const volumeBox = evBox3d(context, { "topology" : volumeEdges, "cSys" : datum, "tight" : true });
        const outsideY = side > 0 ? volumeBox.maxCorner[1] : volumeBox.minCorner[1];
        // The start edge: the LOWEST section end inside the ski's outside (a tie in height goes to the edge that rises
        // from it, i.e. the rout face rather than a shelf). The user's rule was "the end closest to the centreline";
        // on a rout that leans in going up (RD 20TAC: 7 deg, 0.8 mm step-in, 4 mm up, the face running on past the
        // ski's top) that end is the overshoot above the top, while the lowest inner end is the designed start edge.
        // On a rout leaning out both rules pick the same end. No end inside: the end closest to the centreline.
        var best = undefined;
        var closest = undefined;
        var count = 0;
        for (var candidate in ends)
        {
            if (candidate.side != side)
            {
                continue;
            }
            count += 1;
            if (closest == undefined || side * candidate.point[1] < side * closest.point[1])
            {
                closest = candidate;
            }
            if (side * (outsideY - candidate.point[1]) <= ROUT_OUTSIDE_TOLERANCE)
            {
                continue;
            }
            if (best == undefined || candidate.point[2] < best.point[2] - ROUT_TIE ||
                (abs(candidate.point[2] - best.point[2]) <= ROUT_TIE && candidate.rise > best.rise))
            {
                best = candidate;
            }
        }
        if (best == undefined)
        {
            best = closest;
        }
        const d = normalize(best.direction);
        result = { "side" : side, "start" : best.point, "angle" : acos(min(1, abs(d[2]))),
                "stepIn" : side * (outsideY - best.point[1]), "distAboveBase" : best.point[2] - base[2],
                "outsideY" : outsideY, "outsideZ" : outermostZ(context, volumeEdges, toLocal, outsideY), "baseZ" : base[2],
                "edges" : count / 2, "rule" : best == closest ? "closest to the centreline" : "lowest inside the ski" };
    }
    opDeleteBodies(context, id + "deleteSections", { "entities" : qUnion([qCreatedBy(id + "mrsPlane", EntityType.BODY),
                    qCreatedBy(id + "routSection", EntityType.BODY), qCreatedBy(id + "volumeSection", EntityType.BODY)]) });
    return result;
}

/** z (local) of the volume section's sampled point nearest y = `y` (its outermost point on that side). */
function outermostZ(context is Context, edges is Query, toLocal is Transform, y is ValueWithUnits) returns ValueWithUnits
{
    var ts = [];
    for (var i = 0; i <= 32; i += 1)
    {
        ts = append(ts, i / 32);
    }
    var bestZ = 0 * meter;
    var bestDy = undefined;
    for (var e in evaluateQuery(context, edges))
    {
        for (var tl in evEdgeTangentLines(context, { "edge" : e, "parameters" : ts }))
        {
            const p = toLocal * tl.origin;
            if (bestDy == undefined || abs(p[1] - y) < bestDy)
            {
                bestDy = abs(p[1] - y);
                bestZ = p[2];
            }
        }
    }
    return bestZ;
}

/** A measured SW rout value (angle deg, stepIn / distAboveBase mm), 0 when there is none. */
function routValue(rout, key is string) returns number
{
    return (rout == undefined || rout.values == undefined) ? 0 : rout.values[key];
}

/** A SW rout start / stop position (x or s, mm), 0 without a rout surface. */
function routPosition(rout, end is string, field is string) returns number
{
    return rout == undefined ? 0 : rout.positions[end][field];
}

/** Where the Table 2 average radius is always taken (user, 2026-09-28). */
const AVERAGE_BETWEEN = "between the inflection points";

/**
 * Fills names from picks (getProperty throws in the feature body, correction 36): the name prefix from the volume
 * (filled when empty, and follows a pick change as long as it still is the previous pick's name), and the hidden
 * Tip / Tail block wire names from the block wires (always the picked wire's name; empty without a pick).
 */
export function exportPrimitiveEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    var out = followName(context, oldDefinition, definition, "volume", "prefix");
    out = wireName(context, out, "tipBlockWire", "tipBlockWireName");
    out = wireName(context, out, "tailBlockWire", "tailBlockWireName");
    return out;
}

/** `text` = the name of the wire picked in `pick` ("" without a pick). */
function wireName(context is Context, definition is map, pick is string, text is string) returns map
{
    if (!(definition[text] is string))
    {
        return definition;
    }
    var name = "";
    if (definition[pick] is Query && !isQueryEmpty(context, definition[pick]))
    {
        const got = getProperty(context, { "entity" : definition[pick], "propertyType" : PropertyType.NAME });
        name = got is string ? got : "";
    }
    var out = definition;
    out[text] = name;
    return out;
}

/** A tooling block's name for Table 5 (`end` "tip" / "tail"): typed, or the picked wire's name ("" = no row). */
function blockName(context is Context, definition is map, end is string) returns string
{
    if (definition[end ~ "BlockFrom"] == PrimitiveNameFrom.WIRE)
    {
        const pick = definition[end ~ "BlockWire"];
        return pick is Query && !isQueryEmpty(context, pick) ? definition[end ~ "BlockWireName"] : "";
    }
    return definition[end ~ "Block"];
}

/**
 * The extra key points, checked before any geometry: [{ key (the name as an id fragment, primitiveKey), name, world
 * (point), keyLine }]. An empty name, a key another item or a built-in location already has, or no point is a
 * regenError on that item.
 */
function extraKeyPoints(context is Context, items is array) returns array
{
    var out = [];
    var used = {};
    for (var i = 0; i < size(items); i += 1)
    {
        const item = items[i];
        const path = "extraPoints[" ~ i ~ "]";
        const name = item.keyName;
        if (match(name, "^\\s*$").hasMatch)
        {
            throw regenError("Extra key point " ~ (i + 1) ~ ": enter a name.", [path ~ ".keyName"]);
        }
        const key = primitiveKey(name);
        if (isIn(key, PRIMITIVE_RESERVED_KEYS) || match(key, "^MP[0-9]*$").hasMatch)
        {
            throw regenError("Extra key point \"" ~ name ~ "\": that name is a built-in key location.", [path ~ ".keyName"]);
        }
        if (used[key] != undefined)
        {
            throw regenError("Extra key point \"" ~ name ~ "\" has the same name as \"" ~ used[key] ~
                "\" (names are compared with every character other than letters, digits and _ read as _).", [path ~ ".keyName"]);
        }
        used[key] = name;
        out = append(out, { "key" : key, "name" : name, "keyLine" : item.showKeyLine,
                    "world" : primitivePoint(context, item.keyPoint, "extra key point \"" ~ name ~ "\"", path ~ ".keyPoint") });
    }
    return out;
}

/** True when the extra key point with `key` asks for a key line. */
function extraShowsLine(extras is array, key is string) returns boolean
{
    for (var e in extras)
    {
        if (e.key == key)
        {
            return e.keyLine;
        }
    }
    return false;
}

/**
 * The plot band's x-range (u, local): { lo, hi, clip (false for the full ski: the whole footprint, nothing cut), name }.
 */
function plotRegion(choice is PrimitivePlotRegion, fpBox is Box3d, uFcp is ValueWithUnits, uAcp is ValueWithUnits, fr is map) returns map
{
    if (choice == PrimitivePlotRegion.CONTACTS)
    {
        return { "lo" : min(uFcp, uAcp), "hi" : max(uFcp, uAcp), "clip" : true, "name" : "RSL" };
    }
    if (choice == PrimitivePlotRegion.WIDEST)
    {
        return { "lo" : min(fr.widestXMin, fr.widestXMax), "hi" : max(fr.widestXMin, fr.widestXMax), "clip" : true, "name" : "WIDEST" };
    }
    if (choice == PrimitivePlotRegion.INFLECTION)
    {
        return { "lo" : min(fr.inflectionXMin, fr.inflectionXMax), "hi" : max(fr.inflectionXMin, fr.inflectionXMax), "clip" : true, "name" : "INFLECTION" };
    }
    return { "lo" : fpBox.minCorner[0], "hi" : fpBox.maxCorner[0], "clip" : false, "name" : "FULL" };
}

/** True when u is inside the plot region (always for the full ski). */
function inRegion(region is map, u is ValueWithUnits) returns boolean
{
    return !region.clip || (u >= region.lo - REGION_TOLERANCE && u <= region.hi + REGION_TOLERANCE);
}

/** An operation-id fragment from a u position (whole micrometres): "P1625000", "M145000". */
function uKey(u is ValueWithUnits) returns string
{
    return (u < 0 * meter ? "M" : "P") ~ round(abs(u) / millimeter * 1000);
}

/**
 * The bands' reference heights (mm, local frame), scales, the plot region (data x-range), the fixed frames (x-range in
 * mm and axis range in level units, 2026-09-29) and the levels for the attribute.
 */
function bandsData(bandZ is map, definition is map, region is map, frameX is array, axis is array, levels is array, levelStep is number,
    perLevel is ValueWithUnits, curvatureMode is boolean, junctionCount is number, eiFrameX, scale is map) returns map
{
    const auto = definition.autoScale;
    // The scales actually used (2026-09-29 auto scale): radiusScale / curvatureScale / eiScale as text (keys of
    // primitive/1), plus numbers: radiusScaleMm (mm per 1 m) + radiusTickStep (m), or curvatureScaleMm (mm per
    // 0.01 1/m) + curvatureTickStep (1/m); eiPerMm (N*m^2 per 1 mm) + eiTickStep; band heights (mm); with auto scale
    // the largest plotted value (plotMax: m or 1/m; eiMax) and its share of the positive axis (plotFill, eiFill).
    const levelMM = primitiveRound(perLevel / millimeter, 4);
    var out = { "autoScale" : auto, "radiusScale" : curvatureMode ? "10 mm per 1 m" : levelMM ~ " mm per 1 m" };
    for (var entry in bandZ)
    {
        out[entry.key] = primitiveMM(entry.value);
    }
    if (bandZ.ei != undefined)
    {
        const eiPerMm = primitiveRound(millimeter / scale.eiPerUnit, 6);
        out.eiScale = eiPerMm ~ " N*m^2 per 1 mm";
        out.eiPerMm = eiPerMm;
        out.eiAxisMax = scale.eiTop;
        out.eiTickStep = scale.eiStep;
        out.eiBandHeight = primitiveMM(scale.eiTop * scale.eiPerUnit);
        out.eiFrameFrom = primitiveMM(eiFrameX[0]);
        out.eiFrameTo = primitiveMM(eiFrameX[1]);
        if (auto && scale.eiMax != undefined)
        {
            out.eiMax = primitiveRound(scale.eiMax, 4);
            out.eiFill = primitiveRound(scale.eiMax / scale.eiTop, 4);
        }
    }
    if (curvatureMode)
    {
        out.curvatureScale = levelMM ~ " mm per 0.01 1/m";
        out.curvatureScaleMm = levelMM;
        out.curvatureTickStep = primitiveRound(levelStep * PRIMITIVE_CURVATURE_UNIT, 6);
        out.maxCurvature = primitiveRound(axis[1] * PRIMITIVE_CURVATURE_UNIT, 6);
        out.curvatureAxisMin = primitiveRound(-axis[0] * PRIMITIVE_CURVATURE_UNIT, 6);
    }
    else
    {
        out.radiusScaleMm = levelMM;
        out.radiusTickStep = levelStep;
        out.radiusAxisMin = primitiveRound(-axis[0], 6);
    }
    out.plotBandHeight = primitiveMM((axis[1] - axis[0]) * perLevel);
    out.plotBandNegative = primitiveMM(-axis[0] * perLevel);
    if (auto)
    {
        out.plotMax = primitiveRound(scale.max, 6);
        out.plotFill = primitiveRound(scale.fill, 4);
    }
    out.radiusLimit = primitiveRound(definition.radiusLimit / meter, 4);
    // The plot band's fixed frame: u from / to (mm) and the axis from / to (level units: m of radius, 0.01 1/m).
    out.frameFrom = primitiveMM(frameX[0]);
    out.frameTo = primitiveMM(frameX[1]);
    out.axisFrom = primitiveRound(axis[0], 6);
    out.axisTo = primitiveRound(axis[1], 6);
    // Plot region (u, local mm) and the scale's tick levels (m of radius, or 0.01 1/m of curvature).
    out.plotFrom = primitiveMM(region.lo);
    out.plotTo = primitiveMM(region.hi);
    out.plotLevels = levels;
    out.plotLevelStep = levelStep;
    out.plotLevelHeight = primitiveMM(perLevel);
    out.junctions = junctionCount;
    return out;
}

function followName(context is Context, oldDefinition is map, definition is map, pick is string, text is string) returns map
{
    if (!(definition[pick] is Query) || !(definition[text] is string) || isQueryEmpty(context, definition[pick]))
    {
        return definition;
    }
    const name = getProperty(context, { "entity" : definition[pick], "propertyType" : PropertyType.NAME });
    if (!(name is string))
    {
        return definition;
    }
    var out = definition;
    if (definition[text] == "")
    {
        out[text] = name;
    }
    else if (oldDefinition[pick] is Query && !isQueryEmpty(context, oldDefinition[pick])
        && definition[text] == getProperty(context, { "entity" : oldDefinition[pick], "propertyType" : PropertyType.NAME }))
    {
        out[text] = name;
    }
    return out;
}

/** Edges of a pick that may hold edges or wire bodies. */
function wireEdges(pick is Query) returns Query
{
    return qUnion([qEntityFilter(pick, EntityType.EDGE), qOwnedByBody(qBodyType(qEntityFilter(pick, EntityType.BODY), BodyType.WIRE), EntityType.EDGE)]);
}

/** Data-table x positions: N evenly spaced FCP..ACP (both included), plus the `forced` x when forced; ascending x. */
function dataStations(definition is map, xFcp is ValueWithUnits, xAcp is ValueWithUnits, forced is array) returns array
{
    const n = definition.dataPoints;
    var xs = [];
    for (var j = 0; j < n; j += 1)
    {
        xs = append(xs, xFcp + (xAcp - xFcp) * j / (n - 1));
    }
    if (definition.forceStations)
    {
        for (var x in forced)
        {
            var present = false;
            for (var have in xs)
            {
                if (abs(have - x) < 0.01 * millimeter)
                {
                    present = true;
                    break;
                }
            }
            if (!present)
            {
                xs = append(xs, x);
            }
        }
    }
    return sort(xs, function(a, b) { return (a - b) / meter; });
}

/** Table 2 rows: { key, name, value (number or text), unit, note }; only rows with data. */
function metadataRows(fpt is map, fcp is Vector, acp is Vector, volumeBox is Box3d, beam) returns array
{
    const r = fpt.result;
    const dims = volumeBox.maxCorner - volumeBox.minCorner;
    var rows = [
        { "key" : "rsl", "name" : "RSL", "value" : primitiveMM(abs(acp[0] - fcp[0])), "unit" : "mm", "note" : "FCP to ACP along the datum X" },
        { "key" : "dimensions", "name" : "Dimensions (L x W x H)",
            "value" : primitiveRound(dims[0] / millimeter, 2) ~ " x " ~ primitiveRound(dims[1] / millimeter, 2) ~ " x " ~ primitiveRound(dims[2] / millimeter, 2),
            "unit" : "mm", "note" : "Volume bounding box in the datum frame" },
        { "key" : "sidecutWidths", "name" : "Widths (FB widest - waist - AB widest)",
            "value" : primitiveRound(r.fbWidestData.width * 2 / millimeter, 2) ~ " - " ~ primitiveRound(r.waist.width * 2 / millimeter, 2) ~ " - " ~ primitiveRound(r.abWidestData.width * 2 / millimeter, 2),
            "unit" : "mm", "note" : "Unwrapped footprint, full widths" }
    ];
    if (fpt.average.valid)
    {
        rows = append(rows, { "key" : "averageRadius", "name" : "Average radius", "value" : primitiveRound(fpt.average.avgRadius / meter, 4),
                    "unit" : "m", "note" : "Arc-length weighted mean |R| " ~ AVERAGE_BETWEEN ~ " (flatter than 100 m left out)" });
    }
    if (r.naturalRadiusWidest.valid)
    {
        rows = append(rows, { "key" : "naturalRadiusWidest", "name" : "Natural radius widest", "value" : primitiveRound(r.naturalRadiusWidest.R / meter, 4),
                    "unit" : "m", "note" : "Arc through the widest points, tangent to the waist line" });
    }
    if (r.naturalRadiusInflection.valid)
    {
        rows = append(rows, { "key" : "naturalRadiusInflection", "name" : "Natural radius inflection", "value" : primitiveRound(r.naturalRadiusInflection.R / meter, 4),
                    "unit" : "m", "note" : "Arc through the inflection points, tangent to the waist line" });
    }
    rows = concatenateArrays([rows, [
                { "key" : "taperAngleWidest", "name" : "Taper angle widest", "value" : primitiveRound(r.foundTaperAngle / degree, 4),
                    "unit" : "deg", "note" : "Centreline to the line through the widest points (+ = forebody wider)" },
                { "key" : "taperAngleInflection", "name" : "Taper angle inflection", "value" : primitiveRound(r.taperAngleInflection / degree, 4),
                    "unit" : "deg", "note" : "Centreline to the line through the inflection points" }
            ]]);
    if (beam != undefined)
    {
        rows = concatenateArrays([rows, [
                    { "key" : "deflection", "name" : "Theoretical deflection", "value" : primitiveRound(beam.estimatedStiffness_mm, 4), "unit" : "mm/30kg",
                        "note" : "Target EI; 3-point bending, rollers at FCP / ACP, 30 kg at MRS" },
                    { "key" : "stiffness", "name" : "Theoretical stiffness", "value" : primitiveRound(beam.estimatedStiffness_lbin, 4), "unit" : "lb/in",
                        "note" : "Target EI; load for 1 in deflection at MRS, same supports" }
                ]]);
    }
    return rows;
}

/** Footprint points as { u, s, w } in mm (u = unwrapped length coordinate, s as primitiveS), the average range and the unwrap diagnostics. */
function footprintSummary(frame is map, points is map, fpt is map, unwrapped is map, source is map) returns map
{
    var out = {};
    for (var entry in points)
    {
        const u = entry.value[0];
        out[entry.key] = { "u" : primitiveMM(u), "s" : primitiveMM(primitiveSFromU(frame, u)), "w" : primitiveMM(entry.value[1]) };
    }
    out.averageFrom = primitiveMM(primitiveSFromU(frame, fpt.lo));
    out.averageTo = primitiveMM(primitiveSFromU(frame, fpt.hi));
    // The unwrap (2026-09-28): base sections mapped through, exact / fitted edges, and the isometry check (lengths,
    // mm; base area, mm^2).
    out.unwrap = { "sections" : unwrapped.sections, "exact" : unwrapped.exact, "fitted" : unwrapped.fitted,
            "sourceLength" : primitiveMM(unwrapped.sourceLength), "unwrappedLength" : primitiveMM(unwrapped.unwrappedLength) };
    if (source.baseArea != undefined)
    {
        out.unwrap.baseArea = primitiveRound(source.baseArea / (millimeter * millimeter), 3);
    }
    return out;
}
