FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// Onshape's bridging-curve maths (computeBridgingControlPoints): G1 / G2 bridges between the pieces
import(path : "onshape/std/bridgingCurve.fs", version : "3083.0");
// IMPORT: csv_curve_plus_icon.svg (feature icon)
IconNamespace::import(path : "9de909b39fd8e8bc5b3240df", version : "3932588472736c9f9a1243b3");

/**
 * csv curve +: a CSV of points (x, y, z) becomes ONE wire made of several curves.
 *
 * Routing curve fits a single spline through every CSV row, which rings (and inflates the control point count)
 * where the data has near-discontinuities. Here the data is split at BREAK points; each piece is fitted on its own
 * (interpolated, or approximated within a tolerance with its end points interpolated exactly) and the pieces are
 * joined into one wire. Breaks are G0 corners, or are replaced by a G1 / G2 bridging curve (Onshape's bridging
 * curve maths) that takes position, tangent and curvature from the two pieces themselves.
 *
 * Breaks are typed (Point # = the row number of the CSV), clicked in the graphics area, or suggested: the
 * second difference of the chord direction is scored with a robust z-score (median and MAD); points scoring above
 * the threshold are suggested. Suggested breaks are blue markers; nothing is applied until accepted
 * (Suggest breaks fills the list, or tick Apply detected breaks automatically).
 *
 * Process mode (default): the CSV is read when Process is pressed and cached in the feature, so editing the CSV
 * never changes geometry unless asked. Auto mode reads the CSV on every regeneration.
 * Design: csv_curve_plus/DESIGN.md.
 */

export enum CsvUnit
{
    annotation { "Name" : "Millimeter" }
    MILLIMETER,
    annotation { "Name" : "Centimeter" }
    CENTIMETER,
    annotation { "Name" : "Meter" }
    METER,
    annotation { "Name" : "Inch" }
    INCH,
    annotation { "Name" : "Foot" }
    FOOT
}

export enum CsvBuildMode
{
    annotation { "Name" : "Process button" }
    PROCESS,
    annotation { "Name" : "Auto" }
    AUTO
}

export enum CsvBridgeType
{
    annotation { "Name" : "None (G0)" }
    NONE,
    annotation { "Name" : "G1 (tangent)" }
    G1,
    annotation { "Name" : "G2 (curvature)" }
    G2
}

export enum CsvWidthBy
{
    annotation { "Name" : "Distance" }
    DISTANCE,
    annotation { "Name" : "Data points" }
    POINTS
}

const CSV_WIDTH_POINTS_BOUNDS = { (unitless) : [1, 1, 1000] } as IntegerBoundSpec;
const CSV_MERGE_POINTS_BOUNDS = { (unitless) : [0, 0, 1000] } as IntegerBoundSpec;
const CSV_COLUMN_BOUNDS = { (unitless) : [0, 0, 1e5] } as IntegerBoundSpec;
const CSV_POINT_NUMBER_BOUNDS = { (unitless) : [1, 1, 1e6] } as IntegerBoundSpec;
const CSV_COUNT_BOUNDS = { (unitless) : [0, 0, 1e7] } as IntegerBoundSpec;
const CSV_THRESHOLD_BOUNDS = { (unitless) : [1, 5, 100] } as RealBoundSpec;
const CSV_CHECKSUM_BOUNDS = { (unitless) : [-1e15, 0, 1e15] } as RealBoundSpec;
const CSV_TOLERANCE_BOUNDS = { (millimeter) : [0.001, 0.02, 10] } as LengthBoundSpec;
const CSV_BRIDGE_WIDTH_BOUNDS = { (millimeter) : [0, 0, 1000] } as LengthBoundSpec;

/** Distances below this are the same point / the same position along the data. */
const CSV_EPS = 1e-9 * meter;

/** A bridge may use at most this fraction of a piece at each of its two ends, so neighbouring bridges never overlap. */
const CSV_MAX_BRIDGE_FRACTION = 0.4;

/** At most this many replaced data points per bridge are measured against it. */
const CSV_MAX_DEPARTURE_SAMPLES = 40;

/** The data polyline is drawn with at most this many debug lines (each is a sketch solve). */
const CSV_MAX_POLYLINE_LINES = 100;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "csv curve +",
        "Feature Type Description" : "A curve from a CSV of points, split at break points, optionally approximated and bridged (G1 / G2), as one wire.",
        "Feature Name Template" : "csv curve + (#curveLength)",
        "Manipulator Change Function" : "csvCurveManipulatorChange",
        "Editing Logic Function" : "csvCurveEditLogic" }
export const csvCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "Input", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Table", "UIHint" : UIHint.UNCONFIGURABLE }
            definition.inputData is TableData;

            annotation { "Name" : "First data column (0, 1, 2 ...)", "UIHint" : UIHint.UNCONFIGURABLE }
            isInteger(definition.startColumn, CSV_COLUMN_BOUNDS);

            annotation { "Name" : "Unit", "Default" : CsvUnit.MILLIMETER, "UIHint" : [UIHint.UNCONFIGURABLE, UIHint.SHOW_LABEL] }
            definition.csvUnit is CsvUnit;

            annotation { "Name" : "Mode", "Default" : CsvBuildMode.PROCESS, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
            definition.buildMode is CsvBuildMode;

            if (definition.buildMode == CsvBuildMode.PROCESS)
            {
                annotation { "Name" : "Process" }
                isButton(definition.processInputs);
            }
        }

        annotation { "Group Name" : "Breaks", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Break points", "Item name" : "break", "Item label template" : "Point #pointNumber",
                        "UIHint" : UIHint.PREVENT_ARRAY_REORDER,
                        "Description" : "Rows of the CSV (1 = first row) where the curve is split. Click points in the graphics area, type them, or use Suggest breaks." }
            definition.breaks is array;
            for (var brk in definition.breaks)
            {
                annotation { "Name" : "Point #" }
                isInteger(brk.pointNumber, CSV_POINT_NUMBER_BOUNDS);
            }

            annotation { "Name" : "Suggest breaks", "Description" : "Add the detected breaks to the list." }
            isButton(definition.suggestBreaks);

            annotation { "Name" : "Detection threshold", "Description" : "Robust z-score of the change of direction. Lower finds more breaks (default 5)." }
            isReal(definition.breakThreshold, CSV_THRESHOLD_BOUNDS);

            annotation { "Name" : "Apply detected breaks automatically", "Default" : false }
            definition.autoBreaks is boolean;

            annotation { "Name" : "Show suggested breaks", "Default" : true }
            definition.showSuggested is boolean;
        }

        annotation { "Group Name" : "Bridge curves", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Continuity at breaks", "Default" : CsvBridgeType.NONE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
            definition.bridgeType is CsvBridgeType;

            if (definition.bridgeType == CsvBridgeType.G1 || definition.bridgeType == CsvBridgeType.G2)
            {
                annotation { "Name" : "Width by", "Default" : CsvWidthBy.DISTANCE, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL] }
                definition.widthBy is CsvWidthBy;

                if (definition.widthBy == CsvWidthBy.DISTANCE)
                {
                    annotation { "Name" : "Bridge width",
                                "Description" : "Length cut away on each side of a break and replaced by the bridge. 0 = one data interval each side. Limited to 40% of the neighbouring piece." }
                    isLength(definition.bridgeWidth, CSV_BRIDGE_WIDTH_BOUNDS);

                    annotation { "Name" : "Bridge across pieces shorter than",
                                "Description" : "A piece between two breaks that is shorter than this is removed and its two neighbours are bridged directly (one bridge for three sections). 0 = off." }
                    isLength(definition.mergeLength, CSV_BRIDGE_WIDTH_BOUNDS);
                }
                else
                {
                    annotation { "Name" : "Bridge width (data points)",
                                "Description" : "Number of data intervals cut away on each side of a break and replaced by the bridge. Limited to 40% of the neighbouring piece." }
                    isInteger(definition.bridgeWidthPoints, CSV_WIDTH_POINTS_BOUNDS);

                    annotation { "Name" : "Bridge across pieces spanning fewer than (data intervals)",
                                "Description" : "A piece between two breaks that spans fewer data intervals than this is removed and its two neighbours are bridged directly. 0 = off." }
                    isInteger(definition.mergePoints, CSV_MERGE_POINTS_BOUNDS);
                }
            }
        }

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Approximate each piece", "Default" : false }
            definition.approximate is boolean;

            if (definition.approximate)
            {
                annotation { "Name" : "Tolerance" }
                isLength(definition.tolerance, CSV_TOLERANCE_BOUNDS);
            }
        }

        annotation { "Group Name" : "Display", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show data polyline", "Default" : true }
            definition.showPolyline is boolean;

            annotation { "Name" : "Print details", "Default" : false }
            definition.debugPrint is boolean;
        }

        // Process mode cache (written by the editing logic, never shown)
        annotation { "Name" : "Snapshot", "Item name" : "point", "UIHint" : [UIHint.ALWAYS_HIDDEN, UIHint.PREVENT_ARRAY_REORDER] }
        definition.snapshot is array;
        for (var snap in definition.snapshot)
        {
            annotation { "Name" : "x", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isLength(snap.px, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "y", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isLength(snap.py, ZERO_DEFAULT_LENGTH_BOUNDS);

            annotation { "Name" : "z", "UIHint" : UIHint.ALWAYS_HIDDEN }
            isLength(snap.pz, ZERO_DEFAULT_LENGTH_BOUNDS);
        }

        annotation { "Name" : "Snapshot count", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isInteger(definition.snapshotCount, CSV_COUNT_BOUNDS);

        annotation { "Name" : "Snapshot checksum", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isReal(definition.snapshotChecksum, CSV_CHECKSUM_BOUNDS);

        annotation { "Name" : "Curve length", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isLength(definition.curveLength, NONNEGATIVE_ZERO_DEFAULT_LENGTH_BOUNDS);
    }
    {
        var notes = [];

        // ---- the data ----
        const liveTable = csvTablePoints(definition);
        if (liveTable.error != undefined)
        {
            throw regenError(liveTable.error, ["inputData"]);
        }
        var points = [];
        if (definition.buildMode == CsvBuildMode.AUTO)
        {
            if (liveTable.points == undefined)
            {
                throw regenError("Select a CSV table.", ["inputData"]);
            }
            points = liveTable.points;
        }
        else
        {
            points = csvSnapshotPoints(definition);
            if (size(points) == 0)
            {
                throw regenError("Select a CSV table and press Process.", ["inputData"]);
            }
            if (liveTable.points != undefined)
            {
                const live = csvChecksum(liveTable.points);
                if (size(liveTable.points) != definition.snapshotCount || abs(live - definition.snapshotChecksum) > 1e-9 * (1 + abs(live)))
                {
                    notes = append(notes, "The CSV (or its unit) changed since Process: press Process to update the curve.");
                }
            }
        }
        const n = size(points);
        if (n < 2)
        {
            throw regenError("At least two distinct points are needed.", ["inputData"]);
        }

        // ---- breaks: typed / clicked, plus detected ones when asked ----
        const detected = csvDetectBreaks(points, definition.breakThreshold);
        var chosen = {};
        for (var brk in definition.breaks)
        {
            const index = brk.pointNumber - 1;
            if (index < 1 || index > n - 2)
            {
                throw regenError("Break point " ~ brk.pointNumber ~ " must be an interior point (2 to " ~ (n - 1) ~ ").", ["breaks"]);
            }
            chosen[index] = true;
        }
        if (definition.autoBreaks)
        {
            for (var index in detected.indices)
            {
                chosen[index] = true;
            }
        }
        var breaks = [];
        for (var index, flag in chosen)
        {
            breaks = append(breaks, index);
        }
        breaks = sort(breaks, function(a, b)
            {
                return a - b;
            });

        // ---- the curves: one piece between breaks, a bridge across each break ----
        const bridging = definition.bridgeType != CsvBridgeType.NONE;
        const plan = csvPlanPieces(points, breaks, bridging, {
                    "byPoints" : definition.widthBy == CsvWidthBy.POINTS,
                    "width" : definition.bridgeWidth,
                    "points" : definition.bridgeWidthPoints,
                    "mergeLength" : definition.mergeLength,
                    "mergePoints" : definition.mergePoints
                });
        const pieces = plan.pieces;
        var loose = [];
        var allEdges = [];
        var approximated = 0;
        var fallbacks = 0;
        var controlPoints = 0;
        var worstDeviation = 0 * meter;
        for (var j = 0; j < size(pieces); j += 1)
        {
            const pieceId = id + ("piece" ~ j);
            var made = undefined;
            if (definition.approximate && size(pieces[j]) >= 5)
            {
                made = csvApproximatePiece(context, pieceId, pieces[j], definition.tolerance);
                if (made == undefined)
                {
                    fallbacks += 1;
                }
            }
            if (made == undefined)
            {
                opFitSpline(context, pieceId, { "points" : pieces[j] });
            }
            else
            {
                approximated += 1;
                controlPoints += made.controlPoints;
                worstDeviation = max(worstDeviation, made.deviation);
            }
            loose = append(loose, qCreatedBy(pieceId, EntityType.BODY));
            allEdges = append(allEdges, qCreatedBy(pieceId, EntityType.EDGE));
        }
        var worstBridge = 0 * meter;
        if (bridging)
        {
            for (var j = 0; j < size(pieces) - 1; j += 1)
            {
                const bridgeId = id + ("bridge" ~ j);
                csvBuildBridge(context, bridgeId, qCreatedBy(id + ("piece" ~ j), EntityType.EDGE),
                    qCreatedBy(id + ("piece" ~ (j + 1)), EntityType.EDGE), definition.bridgeType);
                loose = append(loose, qCreatedBy(bridgeId, EntityType.BODY));
                allEdges = append(allEdges, qCreatedBy(bridgeId, EntityType.EDGE));

                // how far the bridge is from the data it replaced (at most CSV_MAX_DEPARTURE_SAMPLES of them per bridge)
                const replaced = plan.removed[j];
                const step = max(1, ceil(size(replaced) / CSV_MAX_DEPARTURE_SAMPLES));
                for (var r = 0; r < size(replaced); r += step)
                {
                    worstBridge = max(worstBridge, evDistance(context, { "side0" : qCreatedBy(bridgeId, EntityType.EDGE), "side1" : replaced[r] }).distance);
                }
            }
        }

        // ---- one wire ----
        var wire = qCreatedBy(id + "piece0", EntityType.BODY);
        if (size(allEdges) > 1)
        {
            opExtractWires(context, id + "join", { "edges" : qUnion(allEdges) });
            wire = qCreatedBy(id + "join", EntityType.BODY)->qBodyType(BodyType.WIRE);
            if (size(evaluateQuery(context, wire)) != 1)
            {
                throw regenError("The pieces did not join into one wire (they do not meet end to end).");
            }
            opDeleteBodies(context, id + "deleteLoose", { "entities" : qUnion(loose) });
        }
        if (size(evaluateQuery(context, wire)) == 1)
        {
            setFeatureComputedParameter(context, id, { "name" : "curveLength", "value" : evLength(context, { "entities" : wire }) });
        }

        // ---- what the user sees: markers, polyline, manipulator, one message ----
        addManipulators(context, id, { "breakPoints" : {
                        "points" : points,
                        "selectedIndices" : breaks,
                        "suppressedIndices" : [0, n - 1],
                        "manipulatorType" : ManipulatorType.TOGGLE_POINTS } as Manipulator });

        var suggested = [];
        for (var index in detected.indices)
        {
            if (chosen[index] == undefined)
            {
                suggested = append(suggested, index);
            }
        }
        if (definition.showSuggested && size(suggested) > 0)
        {
            csvShowMarkers(context, id + "suggested", points, suggested);
        }
        if (definition.showPolyline)
        {
            csvShowPolyline(context, points);
        }

        var summary = n ~ " points, " ~ size(pieces) ~ " piece(s)";
        if (bridging)
        {
            summary = summary ~ ", " ~ (size(pieces) - 1) ~ " " ~ (definition.bridgeType == CsvBridgeType.G1 ? "G1" : "G2") ~ " bridge(s)";
            if (plan.mergedPieces > 0)
            {
                summary = summary ~ " (" ~ plan.mergedPieces ~ " short piece(s) bridged across)";
            }
            summary = summary ~ "; bridges stay within " ~ roundToPrecision(worstBridge / millimeter, 3) ~ " mm of the data they replace";
        }
        if (definition.approximate)
        {
            summary = summary ~ "; " ~ approximated ~ " piece(s) approximated (" ~ controlPoints ~ " control points, worst deviation "
                ~ roundToPrecision(worstDeviation / millimeter, 4) ~ " mm)";
            if (fallbacks > 0)
            {
                summary = summary ~ "; " ~ fallbacks ~ " piece(s) kept exact (tolerance not reached)";
            }
        }
        notes = append(notes, summary ~ ".");
        if (definition.showSuggested && size(suggested) > 0)
        {
            const gain = csvRoughnessRatio(points, concatenateArrays([breaks, suggested]));
            notes = append(notes, "Suggested breaks (not applied) at point(s) " ~ csvNumberList(suggested, 1)
                ~ (gain != undefined ? ": applying them would make the data " ~ roundToPrecision(gain, 2) ~ "x smoother (roughness estimate)." : "."));
        }
        reportFeatureInfo(context, id, join(notes, " "));

        if (definition.debugPrint)
        {
            println("[csv curve +] points " ~ n ~ ", breaks at point(s) " ~ csvNumberList(breaks, 1) ~ ", detected at point(s) "
                ~ csvNumberList(detected.indices, 1));
        }
    }, {
            "startColumn" : 0,
            "breaks" : [],
            "breakThreshold" : 5,
            "autoBreaks" : false,
            "showSuggested" : true,
            "bridgeType" : CsvBridgeType.NONE,
            "widthBy" : CsvWidthBy.DISTANCE,
            "bridgeWidth" : 0 * millimeter,
            "mergeLength" : 0 * millimeter,
            "bridgeWidthPoints" : 1,
            "mergePoints" : 0,
            "approximate" : false,
            "tolerance" : 0.02 * millimeter,
            "showPolyline" : true,
            "debugPrint" : false,
            "snapshot" : [],
            "snapshotCount" : 0,
            "snapshotChecksum" : 0,
            "curveLength" : 0 * meter
        });

// ============================================================================
// Reading the CSV
// ============================================================================

function csvUnitValue(unit is CsvUnit) returns ValueWithUnits
{
    if (unit == CsvUnit.CENTIMETER)
    {
        return centimeter;
    }
    if (unit == CsvUnit.METER)
    {
        return meter;
    }
    if (unit == CsvUnit.INCH)
    {
        return inch;
    }
    if (unit == CsvUnit.FOOT)
    {
        return foot;
    }
    return millimeter;
}

function csvHasTable(definition is map) returns boolean
{
    return definition.inputData != undefined && definition.inputData != {} && definition.inputData.csvData != undefined;
}

/** A cell that is empty (a blank line at the end of the file). */
function csvCellBlank(cell) returns boolean
{
    return cell == undefined || cell == "";
}

/**
 * The points of the table in the dialog (or undefined when there is no table).
 * Leading rows that are not numbers (a header) are skipped; blank rows are skipped; a text cell inside the data is an
 * error. Consecutive identical points are dropped.
 * @returns {map} : { points (array of vectors, or undefined), error (string, or undefined) }
 */
function csvTablePoints(definition is map) returns map
{
    if (!csvHasTable(definition))
    {
        return { "points" : undefined, "error" : undefined };
    }
    const unit = csvUnitValue(definition.csvUnit);
    const start = definition.startColumn;
    var points = [];
    const rows = definition.inputData.csvData;
    for (var r = 0; r < size(rows); r += 1)
    {
        const row = rows[r];
        if (!(row is array) || size(row) < start + 3)
        {
            if (size(points) == 0 || (row is array && size(row) == 0))
            {
                continue;
            }
            return { "points" : undefined, "error" : "Row " ~ (r + 1) ~ " has fewer than three columns after the first data column." };
        }
        const x = row[start];
        const y = row[start + 1];
        const z = row[start + 2];
        if (csvCellBlank(x) && csvCellBlank(y) && csvCellBlank(z))
        {
            continue;
        }
        if (!(x is number) || !(y is number) || !(z is number))
        {
            if (size(points) == 0)
            {
                continue;
            }
            return { "points" : undefined, "error" : "Row " ~ (r + 1) ~ " is not numeric in the three data columns." };
        }
        const p = vector(x, y, z) * unit;
        if (size(points) > 0 && norm(p - points[size(points) - 1]) < CSV_EPS)
        {
            continue;
        }
        points = append(points, p);
    }
    return { "points" : points, "error" : undefined };
}

function csvSnapshotPoints(definition is map) returns array
{
    return mapArray(definition.snapshot, function(s)
        {
            return vector(s.px, s.py, s.pz);
        });
}

/** A number that changes whenever a point (or the unit) changes; compared between the cache and the live table. */
function csvChecksum(points is array) returns number
{
    var sum = 0;
    for (var i = 0; i < size(points); i += 1)
    {
        sum += (i + 1) * (points[i][0] + 2 * points[i][1] + 3 * points[i][2]) / meter;
    }
    return sum;
}

// ============================================================================
// Break detection and the roughness estimate
// ============================================================================

function csvMedian(values is array) returns number
{
    const sorted = sort(values, function(a, b)
        {
            return a - b;
        });
    const m = size(sorted);
    if (m == 0)
    {
        return 0;
    }
    if (m % 2 == 1)
    {
        return sorted[floor(m / 2)];
    }
    return 0.5 * (sorted[m / 2 - 1] + sorted[m / 2]);
}

/**
 * Change of the chord direction at every interior point, per unit length: |t_out - t_in| / mean(h_in, h_out), in 1/m.
 * (The second difference of a curve: a smooth curve gives small, even values; a corner gives a spike.)
 * Index i of the result belongs to point i; the end points get 0.
 */
function csvTurnRates(points is array) returns array
{
    const n = size(points);
    var h = [];
    var t = [];
    for (var i = 0; i < n - 1; i += 1)
    {
        const d = points[i + 1] - points[i];
        const len = norm(d);
        h = append(h, len);
        t = append(t, d / len);
    }
    var rates = [0];
    for (var i = 1; i < n - 1; i += 1)
    {
        rates = append(rates, norm(t[i] - t[i - 1]) / (0.5 * (h[i - 1] + h[i])) * meter);
    }
    return append(rates, 0);
}

/**
 * Break candidates: interior points whose turn rate is an outlier, scored with a robust z-score
 * (value - median) / (1.4826 * MAD) over all interior points. Measured on the ski profile test file: exactly the
 * three real breaks for thresholds 3 to 6. O(n log n), unit-free, never rescales the geometry.
 * @returns {map} : { indices (0-based point indices), scores (z-score for every point) }
 */
function csvDetectBreaks(points is array, threshold is number) returns map
{
    const n = size(points);
    if (n < 4)
    {
        return { "indices" : [], "scores" : makeArray(n, 0) };
    }
    const rates = csvTurnRates(points);
    const interior = subArray(rates, 1, n - 1);
    const med = csvMedian(interior);
    const mad = csvMedian(mapArray(interior, function(v)
            {
                return abs(v - med);
            }));
    const sigma = max(max(1.4826 * mad, 1e-3 * med), 1e-9);
    var scores = [0];
    var indices = [];
    for (var i = 1; i < n - 1; i += 1)
    {
        const z = (rates[i] - med) / sigma;
        scores = append(scores, z);
        if (z > threshold)
        {
            indices = append(indices, i);
        }
    }
    return { "indices" : indices, "scores" : append(scores, 0) };
}

/**
 * How much smoother the data is when the points in `breaks` are corners rather than part of the curve:
 * the sum of (turn angle)^2 / length over all interior points divided by the same sum without the break points.
 * A cheap estimate of the curvature energy of a spline through the data (no spline is built); undefined when the
 * pieces have no roughness left.
 */
function csvRoughnessRatio(points is array, breaks is array)
{
    const n = size(points);
    if (n < 3)
    {
        return undefined;
    }
    const rates = csvTurnRates(points);
    var isBreak = {};
    for (var b in breaks)
    {
        isBreak[b] = true;
    }
    var all = 0;
    var kept = 0;
    for (var i = 1; i < n - 1; i += 1)
    {
        const energy = rates[i] ^ 2 * (norm(points[i + 1] - points[i - 1]) / (2 * meter));
        all += energy;
        if (isBreak[i] == undefined)
        {
            kept += energy;
        }
    }
    if (kept < 1e-18)
    {
        return undefined;
    }
    return all / kept;
}

function csvNumberList(zeroBased is array, offset is number) returns string
{
    const sorted = sort(zeroBased, function(a, b)
        {
            return a - b;
        });
    return join(mapArray(sorted, function(i)
            {
                return toString(i + offset);
            }), ", ");
}

// ============================================================================
// Pieces
// ============================================================================

/** The point at a length along the data polyline (exactly a data point when the length is at one). */
function csvPointAtLength(points is array, lengths is array, at is ValueWithUnits) returns Vector
{
    const n = size(points);
    for (var k = 0; k < n; k += 1)
    {
        if (abs(at - lengths[k]) <= CSV_EPS)
        {
            return points[k];
        }
    }
    for (var k = 0; k < n - 1; k += 1)
    {
        if (at < lengths[k + 1])
        {
            return points[k] + (points[k + 1] - points[k]) * ((at - lengths[k]) / (lengths[k + 1] - lengths[k]));
        }
    }
    return points[n - 1];
}

/**
 * Is the piece between data indices a and b short enough to be removed and bridged across? Only used with bridging.
 * @param spec {map} : byPoints, mergeLength (length), mergePoints (data intervals); 0 = off.
 */
function csvPieceIsShort(lengths is array, a is number, b is number, spec is map) returns boolean
{
    if (spec.byPoints)
    {
        return spec.mergePoints > 0 && (b - a) < spec.mergePoints;
    }
    return spec.mergeLength > 0 * meter && lengths[b] - lengths[a] < spec.mergeLength;
}

/**
 * How much of a piece a bridge cuts away at one of its ends: `breakIndex` is the break at that end, `otherIndex` the data
 * index at the piece's other end. By distance: the width, or one data interval when it is 0. By points: that many data
 * intervals (not past the other end of the piece), so the cut lands on a data point.
 */
function csvCutWidth(lengths is array, breakIndex is number, otherIndex is number, atStart is boolean, spec is map) returns ValueWithUnits
{
    if (spec.byPoints)
    {
        if (atStart)
        {
            return lengths[min(breakIndex + spec.points, otherIndex)] - lengths[breakIndex];
        }
        return lengths[breakIndex] - lengths[max(breakIndex - spec.points, otherIndex)];
    }
    if (spec.width > 0 * meter)
    {
        return spec.width;
    }
    return atStart ? lengths[breakIndex + 1] - lengths[breakIndex] : lengths[breakIndex] - lengths[breakIndex - 1];
}

/**
 * The points of every piece. Pieces run between gaps; a gap is one break (the end of one piece and the start of the next,
 * the same point value) or, with bridging and a merge setting, a run of breaks whose in-between pieces are short: those
 * pieces are removed and the pieces on either side are bridged directly (one bridge for three or more sections).
 * With bridging, a piece stops short of a gap by the bridge width (cut on the data polyline, so it may fall between data
 * points) and the data inside the cut is replaced by the bridge. The width is limited to CSV_MAX_BRIDGE_FRACTION of the
 * piece, so neighbouring bridges never overlap.
 * @returns {map} : { pieces (array of point arrays), removed (per bridge: the data points it replaces),
 *                    mergedPieces (number of short pieces bridged across) }
 */
function csvPlanPieces(points is array, breaks is array, bridging is boolean, spec is map) returns map
{
    const n = size(points);
    var lengths = [0 * meter];
    for (var k = 1; k < n; k += 1)
    {
        lengths = append(lengths, lengths[k - 1] + norm(points[k] - points[k - 1]));
    }

    // gaps: runs of breaks (lo = first break of the run, hi = last)
    var gaps = [];
    for (var b in breaks)
    {
        if (bridging && size(gaps) > 0 && csvPieceIsShort(lengths, gaps[size(gaps) - 1].hi, b, spec))
        {
            gaps[size(gaps) - 1].hi = b;
        }
        else
        {
            gaps = append(gaps, { "lo" : b, "hi" : b });
        }
    }

    const g = size(gaps);
    var pieces = [];
    var cutStarts = [];
    var cutEnds = [];
    for (var j = 0; j <= g; j += 1)
    {
        const a = j == 0 ? 0 : gaps[j - 1].hi;
        const b = j == g ? n - 1 : gaps[j].lo;
        const pieceLength = lengths[b] - lengths[a];
        var startCut = 0 * meter;
        var endCut = 0 * meter;
        if (bridging && j > 0)
        {
            startCut = min(csvCutWidth(lengths, a, b, true, spec), CSV_MAX_BRIDGE_FRACTION * pieceLength);
        }
        if (bridging && j < g)
        {
            endCut = min(csvCutWidth(lengths, b, a, false, spec), CSV_MAX_BRIDGE_FRACTION * pieceLength);
        }
        const cutStart = lengths[a] + startCut;
        const cutEnd = lengths[b] - endCut;
        var piece = [csvPointAtLength(points, lengths, cutStart)];
        for (var k = a; k <= b; k += 1)
        {
            if (lengths[k] > cutStart + CSV_EPS && lengths[k] < cutEnd - CSV_EPS)
            {
                piece = append(piece, points[k]);
            }
        }
        piece = append(piece, csvPointAtLength(points, lengths, cutEnd));
        pieces = append(pieces, piece);
        cutStarts = append(cutStarts, cutStart);
        cutEnds = append(cutEnds, cutEnd);
    }

    // the data each bridge replaces: everything between the end of one piece and the start of the next
    var removed = [];
    for (var j = 0; j < g; j += 1)
    {
        var replaced = [];
        for (var k = 0; k < n; k += 1)
        {
            if (lengths[k] > cutEnds[j] + CSV_EPS && lengths[k] < cutStarts[j + 1] - CSV_EPS)
            {
                replaced = append(replaced, points[k]);
            }
        }
        removed = append(removed, replaced);
    }
    return { "pieces" : pieces, "removed" : removed, "mergedPieces" : size(breaks) - g };
}

/**
 * A piece as a least-squares B-spline within `tolerance`, its end points interpolated exactly. The deviation is checked at
 * every data point; if it is above the tolerance the fit is repeated once at half of it, and if it is still above, nothing
 * is created (undefined) and the caller interpolates the piece instead. Greedy knot insertion was measured to overshoot
 * between data points by up to 1.3 mm, so the native approximation is used and verified.
 */
function csvApproximatePiece(context is Context, id is Id, piece is array, tolerance is ValueWithUnits)
{
    const m = size(piece);
    var parameters = [0];
    for (var k = 1; k < m; k += 1)
    {
        parameters = append(parameters, parameters[k - 1] + norm(piece[k] - piece[k - 1]) / meter);
    }
    var attempt = tolerance;
    for (var round = 0; round < 2; round += 1)
    {
        const result = approximateSpline(context, {
                    "degree" : 3,
                    "tolerance" : attempt,
                    "isPeriodic" : false,
                    "targets" : [approximationTarget({ "positions" : piece })],
                    "parameters" : parameters,
                    "interpolateIndices" : [0, m - 1],
                    "suppressInterpolationNotice" : true
                });
        const spline = result[0];
        const at = evaluateSpline({ "spline" : spline, "parameters" : parameters })[0];
        var worst = 0 * meter;
        for (var k = 0; k < m; k += 1)
        {
            worst = max(worst, norm(at[k] - piece[k]));
        }
        if (worst <= tolerance)
        {
            opCreateBSplineCurve(context, id, { "bSplineCurve" : spline });
            return { "controlPoints" : size(spline.controlPoints), "deviation" : worst };
        }
        attempt = attempt / 2;
    }
    return undefined;
}

// ============================================================================
// Bridges
// ============================================================================

/**
 * A G1 (cubic, 4 control points) or G2 (quintic, 6 control points) Bezier from the end of one piece to the start of the next,
 * from Onshape's bridging curve maths. Position, tangent and curvature are read from the two pieces themselves.
 */
function csvBuildBridge(context is Context, id is Id, pieceA is Query, pieceB is Query, continuity is CsvBridgeType)
{
    const degree = continuity == CsvBridgeType.G2 ? 2 : 1;
    const endA = evEdgeCurvature(context, { "edge" : pieceA, "parameter" : 1 });
    const startB = evEdgeCurvature(context, { "edge" : pieceB, "parameter" : 0 });
    var sideA = { "degree" : degree, "position" : endA.frame.origin, "tangent" : curvatureFrameTangent(endA) };
    var sideB = { "degree" : degree, "position" : startB.frame.origin, "tangent" : -curvatureFrameTangent(startB) };
    if (degree == 2)
    {
        sideA.curvature = endA.curvature;
        sideA.curvatureDirection = curvatureFrameNormal(endA);
        sideB.curvature = startB.curvature;
        sideB.curvatureDirection = curvatureFrameNormal(startB);
    }
    const controls = computeBridgingControlPoints(context, sideA as BridgingSideData, sideB as BridgingSideData);
    opCreateBSplineCurve(context, id, { "bSplineCurve" : bSplineCurve({
                    "degree" : size(controls) - 1,
                    "isPeriodic" : false,
                    "controlPoints" : controls,
                    "knots" : knotArray([0, 1])
                }) });
}

// ============================================================================
// Display
// ============================================================================

/** Blue points at the given data indices, in one temporary scope (a debug point is a sketch solve each). */
function csvShowMarkers(context is Context, id is Id, points is array, indices is array)
{
    startFeature(context, id, {});
    for (var i = 0; i < size(indices); i += 1)
    {
        opPoint(context, id + ("marker" ~ i), { "point" : points[indices[i]] });
    }
    addDebugEntities(context, qCreatedBy(id, EntityType.VERTEX), DebugColor.BLUE);
    abortFeature(context, id);
}

/** The data as a polyline (debug lines, strided so there are at most CSV_MAX_POLYLINE_LINES). */
function csvShowPolyline(context is Context, points is array)
{
    const n = size(points);
    const stride = max(1, ceil((n - 1) / CSV_MAX_POLYLINE_LINES));
    var last = 0;
    for (var i = stride; i < n; i += stride)
    {
        addDebugLine(context, points[last], points[i], DebugColor.GREEN);
        last = i;
    }
    if (last < n - 1)
    {
        addDebugLine(context, points[last], points[n - 1], DebugColor.GREEN);
    }
}

// ============================================================================
// Editing logic and manipulator
// ============================================================================

/** Fills the Process cache from the table in the dialog. */
function csvProcess(definition is map) returns map
{
    const table = csvTablePoints(definition);
    if (table.points == undefined || size(table.points) == 0)
    {
        return definition;
    }
    definition.snapshot = mapArray(table.points, function(p)
        {
            return { "px" : p[0], "py" : p[1], "pz" : p[2] };
        });
    definition.snapshotCount = size(table.points);
    definition.snapshotChecksum = csvChecksum(table.points);
    return definition;
}

/** The break list as items, from 0-based indices. */
function csvBreakItems(indices is array) returns array
{
    const sorted = sort(indices, function(a, b)
        {
            return a - b;
        });
    return mapArray(sorted, function(i)
        {
            return { "pointNumber" : i + 1 };
        });
}

export function csvCurveEditLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean,
    specifiedParameters is map, hiddenQueries is Query, clickedButton is string) returns map
{
    if (clickedButton == "processInputs")
    {
        return csvProcess(definition);
    }
    if (clickedButton == "suggestBreaks")
    {
        var points = [];
        if (definition.buildMode == CsvBuildMode.AUTO)
        {
            const table = csvTablePoints(definition);
            points = table.points == undefined ? [] : table.points;
        }
        else
        {
            points = csvSnapshotPoints(definition);
        }
        var chosen = {};
        for (var brk in definition.breaks)
        {
            chosen[brk.pointNumber - 1] = true;
        }
        for (var index in csvDetectBreaks(points, definition.breakThreshold).indices)
        {
            chosen[index] = true;
        }
        var indices = [];
        for (var index, flag in chosen)
        {
            indices = append(indices, index);
        }
        definition.breaks = csvBreakItems(indices);
        return definition;
    }
    // first time a table is picked in Process mode: fill the cache so something shows (later changes need Process)
    if (definition.buildMode == CsvBuildMode.PROCESS && size(definition.snapshot) == 0 && csvHasTable(definition))
    {
        return csvProcess(definition);
    }
    return definition;
}

export function csvCurveManipulatorChange(context is Context, definition is map, newManipulators is map) returns map
{
    if (newManipulators["breakPoints"] == undefined)
    {
        return definition;
    }
    definition.breaks = csvBreakItems(newManipulators["breakPoints"].selectedIndices);
    return definition;
}
