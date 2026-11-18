FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
export import(path : "onshape/std/movecurveboundarytype.gen.fs", version : "3083.0");
export import(path : "onshape/std/curveextensionshape.gen.fs", version : "3083.0");
export import(path : "onshape/std/curveextensionendcondition.gen.fs", version : "3083.0");

// curveTrimCore (same document) - plane-cut trim/split engine
import(path : "d56d74c24234ab2b885e6fc1", version : "ea2966195a7ea412688a95fe");

// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

IconNamespace::import(path : "a4ceb2cfcdb959208b76a85c", version : "e1d6923ac9355dc407e71a4f");


/**
 * betterCurveTrim - a more powerful curve trim/split, in the look and feel of
 * the stock Onshape "Trim curve" (see OS_Trim.fs, reference only - not imported).
 *
 * Capabilities:
 *   - Trim OR split (split keeps all geometry; optionally return one wire).     [done]
 *   - Cut at points, evenly between two points, or every X from a point.        [done]
 *   - Cut at one or more signed arc-length distances from a reference point     [done]
 *     (blue arrow = positive direction), optionally also at the reference point.
 *   - Cut at solved inflection points: pick any (toggle), or nearest each end.  [done]
 *   - EXTEND (pass-through to opMoveCurveBoundary) to fully supersede OS_Trim.   [pending]
 *
 * Wires may have any number of edges: a wire is read as one path (constructPath) and
 * cut exactly with opSplitEdges at arc-length parameters (see curveTrimCore.fs).
 * Inflection mode still needs a single-edge wire.
 *
 * Variable_tools outputs (Extract variables): the standard keys plus cut_1..n (the
 * vertex at each cut, ordered along the curve), cutVertices (all of them), piece_1..m
 * (each span between cuts: a body, or its edges when split in place as one wire) and
 * cutCount.
 */

// ============================================================================
// ENUMS
// ============================================================================

/**
 * Top-level operation. TRIM keeps one side of a cut; SPLIT keeps every piece.
 * (An EXTEND mode is still pending - see the file header.)
 */
export enum OPERATION
{
    annotation { "Name" : "Trim" }
    TRIM,
    annotation { "Name" : "Split" }
    SPLIT
}

/**
 * How the cut location(s) along the curve are defined.
 */
export enum CUT_BY
{
    annotation { "Name" : "Up to entity" }
    UP_TO_ENTITY,
    annotation { "Name" : "Distance from point" }
    ARC_LENGTH_FROM_POINT,
    annotation { "Name" : "Even division" }
    EVEN_DIVISION,
    annotation { "Name" : "At points" }
    AT_POINTS,
    annotation { "Name" : "At inflections" }
    AT_INFLECTION,
    annotation { "Name" : "Distance from ends" }
    DISTANCE_FROM_ENDS
}

/**
 * Distance from ends: trim (or cut) the same distance from both ends of the wire, or a different distance from each end,
 * with the start end picked by a point. "Ends" are the ends of the WIRE (its path), not the ends of its edges.
 */
export enum END_TRIM_MODE
{
    annotation { "Name" : "Same at both ends" }
    SAME_AT_BOTH,
    annotation { "Name" : "Different at each end" }
    DIFFERENT_AT_EACH
}

// Default 5 mm, in the units the user works in, for the end distances (a new length parameter's default migrates in mm).
const END_TRIM_BOUNDS = { (meter) : [-500, 0.005, 500], (centimeter) : 0.5, (millimeter) : 5.0, (inch) : 0.2 } as LengthBoundSpec;

/**
 * Even-division style: divide the span between two points into equal parts, or
 * place cuts at a fixed spacing repeated a number of times from one point.
 */
export enum DIVISION_MODE
{
    annotation { "Name" : "Between two points" }
    BETWEEN_POINTS,
    annotation { "Name" : "Every distance from point" }
    SPACED_FROM_POINT
}

/**
 * How the inflection cut chooses which inflection(s) to cut at: PICK lets the user
 * click a manipulator point; NEAR_ENDS auto-selects the inflection(s) closest to
 * the curve's endpoints (which only differs from "all" when there are 3+).
 */
export enum INFLECTION_MODE
{
    annotation { "Name" : "Pick (click a point)" }
    PICK,
    annotation { "Name" : "Nearest each endpoint" }
    NEAR_ENDS
}

/**
 * Which part of the curve a distance trim keeps, relative to the reference point: the cut
 * sits the given distance from the point, so either the side of the cut that contains the
 * point (the default) or the side away from it survives.
 */
export enum TRIM_KEEP
{
    annotation { "Name" : "Toward point" }
    POINT_SIDE,
    annotation { "Name" : "Away from point" }
    FAR_SIDE
}

// Manipulator key for the inflection-point picker - shared between the feature
// body (which adds it) and the change function (which reads the toggled indices).
const INFLECTION_MANIPULATOR = "inflectionManipulator";

// ============================================================================
// FEATURE
// ============================================================================

annotation { "Feature Type Name" : "Trim curve +", "Icon" : IconNamespace::BLOB_DATA,
        "Feature Type Description" : "Trim or split wires at exact places: up to an entity, a distance from a point, an even division, at points, at inflections, or a distance from the ends of the wire (positive trims, negative extends). Wires may have any number of edges and are cut along their whole path. A trim is done in place when it can be, so references to the wire survive, and the feature says which route it took. Feeds Extract variables with the cut vertices and pieces.", "Manipulator Change Function" : "onInflectionPick" }
export const betterCurveTrim = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Operation", "Description" : "Trim keeps one side of each cut and removes the rest. Split keeps every piece.", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : OPERATION.SPLIT }
        definition.operation is OPERATION;

        annotation { "Name" : "Curves to adjust", "Description" : "The wire bodies to cut. Each wire is read as one path, however many edges it has.", "Filter" : EntityType.BODY && BodyType.WIRE && SketchObject.NO }
        definition.curves is Query;

        annotation { "Name" : "Cut location", "Description" : "How the cut locations are defined: up to an entity, a distance from a point, even division, at points, at inflections, or a distance from the ends of the wire.", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : CUT_BY.AT_POINTS }
        definition.cutBy is CUT_BY;

        if (definition.cutBy == CUT_BY.UP_TO_ENTITY)
        {
            annotation { "Name" : "Up to entity", "Description" : "A face, edge, vertex, body or mate connector. The curve is cut where it meets this entity.", "Filter" : (EntityType.BODY && SketchObject.NO) || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.boundary is Query;
        }
        else if (definition.cutBy == CUT_BY.ARC_LENGTH_FROM_POINT)
        {
            annotation { "Name" : "From point", "Description" : "A vertex or mate connector on the curve. The distance is measured along the curve from here.", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.fromPoint is Query;

            annotation { "Name" : "Distance", "Description" : "Arc length from the reference point. Positive runs the way the blue arrow points; negative runs the other way. From an END of the curve it always runs into the curve." }
            isLength(definition.distance, LENGTH_BOUNDS);

            annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Reverse the positive direction (blue arrow). No effect when the reference point is an end of the curve: the distance then always runs into the curve." }
            definition.distanceFlip is boolean;

            if (definition.operation == OPERATION.TRIM)
            {
                annotation { "Name" : "Side to keep", "UIHint" : [UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : TRIM_KEEP.POINT_SIDE,
                            "Description" : "Toward point keeps the side of the cut that contains the reference point and removes everything past the cut. Away from point keeps the far side and removes the stretch between the point and the cut." }
                definition.trimKeep is TRIM_KEEP;
            }

            if (definition.operation == OPERATION.SPLIT)
            {
                annotation { "Name" : "Additional distances", "Item name" : "distance", "Item label template" : "#extraDistance", "Description" : "More cuts from the same reference point, each a signed distance like the first." }
                definition.extraDistances is array;
                for (var item in definition.extraDistances)
                {
                    annotation { "Name" : "Distance" }
                    isLength(item.extraDistance, LENGTH_BOUNDS);
                }

                annotation { "Name" : "Also split at reference point", "Default" : false }
                definition.splitAtReference is boolean;
            }
        }
        else if (definition.cutBy == CUT_BY.EVEN_DIVISION)
        {
            annotation { "Name" : "Division style", "Description" : "Between two points: divide the span into equal parts. Every distance from point: cuts at a fixed spacing repeated from one point.", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : DIVISION_MODE.BETWEEN_POINTS }
            definition.divisionMode is DIVISION_MODE;

            if (definition.divisionMode == DIVISION_MODE.BETWEEN_POINTS)
            {
                annotation { "Name" : "Start point", "Description" : "Start of the span to divide. A vertex or mate connector on the curve.", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.startPoint is Query;

                annotation { "Name" : "End point", "Description" : "End of the span to divide. A vertex or mate connector on the curve.", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.endPoint is Query;

                annotation { "Name" : "Number of segments", "Description" : "Number of equal parts the span is divided into." }
                isInteger(definition.divisions, CTC_DIVISION_BOUNDS);
            }
            else
            {
                annotation { "Name" : "From point", "Description" : "A vertex or mate connector on the curve. The cuts are spaced from here.", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.spacingFromPoint is Query;

                annotation { "Name" : "Spacing", "Description" : "Arc length between consecutive cuts." }
                isLength(definition.spacing, CTC_SPACING_BOUNDS);

                annotation { "Name" : "Number of cuts", "Description" : "How many cuts to place, each one spacing further along the curve." }
                isInteger(definition.spacingCount, CTC_SPACING_COUNT_BOUNDS);

                annotation { "Name" : "Reverse direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Space the cuts away from the midpoint instead of toward it." }
                definition.spacingReverse is boolean;
            }
        }
        else if (definition.cutBy == CUT_BY.AT_POINTS)
        {
            annotation { "Name" : "Cut points", "Description" : "Vertices or mate connectors on the curve. The curve is cut at each one (a point off the curve is projected onto it).", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.atPoints is Query;
        }
        else if (definition.cutBy == CUT_BY.AT_INFLECTION)
        {
            // Inflections are solved from the curve; this only chooses which to cut.
            annotation { "Name" : "Inflection selection", "Description" : "Pick: click the inflection points to cut at. Nearest each endpoint: cut at the inflection closest to each end of the curve.", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : INFLECTION_MODE.PICK }
            definition.inflectionMode is INFLECTION_MODE;
        }
        else if (definition.cutBy == CUT_BY.DISTANCE_FROM_ENDS)
        {
            annotation { "Name" : "Ends", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : END_TRIM_MODE.SAME_AT_BOTH,
                        "Description" : "Same at both ends cuts one distance in from each end of the wire. Different at each end lets you pick the start end and give a separate start and end distance. The ends are the ends of the whole wire, not of its edges." }
            definition.endsMode is END_TRIM_MODE;

            if (definition.endsMode == END_TRIM_MODE.SAME_AT_BOTH)
            {
                annotation { "Name" : "Distance from each end", "Description" : "Arc length measured along the wire from each end. Positive trims that much off each end; negative extends each end by that much." }
                isLength(definition.endsDistance, END_TRIM_BOUNDS);
            }
            else
            {
                annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                            "Description" : "A vertex or mate connector at one end of the wire. That end is the start; the other is the end." }
                definition.endsStartPoint is Query;

                annotation { "Name" : "Start distance", "Description" : "Arc length measured along the wire from the start end. Positive trims; negative extends the start end by that much." }
                isLength(definition.endsStartDistance, END_TRIM_BOUNDS);

                annotation { "Name" : "End distance", "Description" : "Arc length measured along the wire from the other end. Positive trims; negative extends that end by that much." }
                isLength(definition.endsEndDistance, END_TRIM_BOUNDS);
            }

            annotation { "Name" : "Extension shape", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : CurveExtensionShape.TANGENT,
                        "Description" : "Used only for a negative distance (an extension). Tangent continues straight along the end tangent; Curvature continues the end curvature." }
            definition.endsExtensionShape is CurveExtensionShape;
        }

        if (definition.operation == OPERATION.SPLIT)
        {
            annotation { "Name" : "Return single wire", "Default" : false, "Description" : "Recombine the split pieces into one wire body with multiple edges." }
            definition.returnSingleWire is boolean;
        }
        else if (definition.cutBy != CUT_BY.ARC_LENGTH_FROM_POINT && definition.cutBy != CUT_BY.DISTANCE_FROM_ENDS)
        {
            // Distance mode has its own "Keep" choice (relative to the reference point); distance from ends keeps the middle.
            annotation { "Name" : "Keep opposite side", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Which side of the cut to keep. Trim discards the other side." }
            definition.flipHeuristics is boolean;
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print inflection solve", "Default" : false, "Description" : "Print the solved inflection fractions with their distance from each curve end." }
            definition.debugInflections is boolean;

            annotation { "Name" : "Print cuts", "Default" : false, "Description" : "Print the cut fractions used and the resulting piece count." }
            definition.debugCuts is boolean;
        }

        // Hidden: which inflections the user toggled in AT_INFLECTION/PICK mode.
        // An array parameter needs an "Item name" and a for loop over its items
        // (even when hidden), so each item is a { "index" : n } map. The body and
        // change function convert to/from the manipulator's plain index array.
        annotation { "Name" : "Chosen inflections", "Item name" : "inflection", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.inflectionIndices is array;

        for (var inflection in definition.inflectionIndices)
        {
            annotation { "Name" : "Index" }
            isInteger(inflection.index, CTC_INFLECTION_INDEX_BOUNDS);
        }
    }
    {
        if (isQueryEmpty(context, definition.curves))
        {
            throw regenError("Select one or more curves to adjust", ["curves"]);
        }

        // Any error that is not already a message for the user (a failing operation, a bad query) would show as the bare
        // "Error regenerating." Say what it was instead.
        try
        {
        var wires = evaluateQuery(context, definition.curves);
        var results = [];

        if (definition.cutBy == CUT_BY.AT_INFLECTION)
        {
            // One picker per feature; keep to a single curve so the hidden selection
            // maps unambiguously to one manipulator.
            if (size(wires) != 1)
            {
                throw regenError("Inflection mode supports one curve at a time", ["curves"]);
            }
            results = [adjustAtInflection(context, id, definition, wires[0])];
        }
        else
        {
            for (var w = 0; w < size(wires); w += 1)
            {
                results = append(results, adjustOneCurve(context, id + ("curve" ~ w), definition, wires[w]));
            }
        }

        embedTrimOutputs(context, id, definition, results);
        reportRoute(context, id, results);
        }
        catch (unexpected)
        {
            if (unexpected is map && unexpected.message != undefined)
            {
                throw unexpected;
            }
            throw regenError("Trim curve + stopped on an unexpected error: " ~ toString(unexpected), ["curves"]);
        }
    },
    {
        "inflectionIndices" : [],
        "distanceFlip" : false,
        "flipHeuristics" : false,
        "trimKeep" : TRIM_KEEP.POINT_SIDE,
        "extraDistances" : [],
        "splitAtReference" : false,
        "endsMode" : END_TRIM_MODE.SAME_AT_BOTH,
        "endsExtensionShape" : CurveExtensionShape.TANGENT
    });

/**
 * Tell the user which route a trim took, since it decides whether references to the wire survive. In place
 * (opMoveCurveBoundary): the wire body is kept. Replaced (extract and delete): new wire bodies, the original is gone, so
 * a query variable made BEFORE this feature no longer resolves -- a WARNING. Also states the endpoint distance rule.
 * Other operations (split, inflection) report nothing new.
 */
function reportRoute(context is Context, id is Id, results is array)
{
    var inPlace = 0;
    var replaced = 0;
    var notes = [];
    var vias = [];
    var reasons = [];
    for (var r in results)
    {
        if (r.route == "inPlace")
        {
            inPlace += 1;
            if (r.via != undefined && indexOf(vias, r.via) < 0)
            {
                vias = append(vias, r.via);
            }
        }
        else if (r.route == "replaced")
        {
            replaced += 1;
            if (r.reason != undefined && r.reason != "" && indexOf(reasons, r.reason) < 0)
            {
                reasons = append(reasons, r.reason);
            }
        }
        if (r.endNote != undefined && r.endNote != "" && indexOf(notes, r.endNote) < 0)
        {
            notes = append(notes, r.endNote);
        }
    }
    var messages = notes;
    if (inPlace > 0)
    {
        messages = append(messages, "Trimmed in place (opMoveCurveBoundary, " ~ join(vias, "; ") ~ "): the wire body is kept, so references to it survive.");
    }
    if (replaced > 0)
    {
        messages = append(messages, "The wire was REPLACED: an in-place trim was not possible, so the cut made new wire bodies and deleted the "
            ~ "original. References to the old wire, such as a query variable made before this feature, no longer resolve. "
            ~ join(reasons, " "));
        reportFeatureWarning(context, id, join(messages, " "));
    }
    else if (size(messages) > 0)
    {
        reportFeatureInfo(context, id, join(messages, " "));
    }
}

// ============================================================================
// PER-CURVE WORK
// ============================================================================

/**
 * Trim or split a single wire at the cut locations for the selected mode. Returns
 * the per-curve result (see [applyCut]).
 */
function adjustOneCurve(context is Context, id is Id, definition is map, wire is Query) returns map
{
    if (definition.cutBy == CUT_BY.DISTANCE_FROM_ENDS)
    {
        return adjustEnds(context, id, definition, wire);
    }

    var wp = wirePath(context, wire);

    // Distance mode: a reference point that is an END of the curve fixes the direction (into the curve), whichever way
    // the path happens to run (see pathEndOfPoint).
    var endOfPath = undefined;
    if (definition.cutBy == CUT_BY.ARC_LENGTH_FROM_POINT && !isQueryEmpty(context, definition.fromPoint))
    {
        endOfPath = pathEndOfPoint(context, wp, fractionOfPoint(context, wp, resolvePoint(context, definition.fromPoint)));
    }

    var fractions = cleanFractions(cutFractionsFor(context, definition, wp, endOfPath));
    if (size(fractions) == 0)
    {
        throw regenError("No valid cut location found on the curve", ["curves"]);
    }

    // For the point-based modes, a trim is a single cut - reduce to the first
    // location. (Inflection mode trims/splits at every chosen point, so it does
    // its own thing and calls applyCut directly.)
    if (definition.operation == OPERATION.TRIM && size(fractions) > 1)
    {
        fractions = [fractions[0]];
    }

    if (definition.operation == OPERATION.TRIM && definition.cutBy == CUT_BY.ARC_LENGTH_FROM_POINT)
    {
        // Distance mode keeps a side relative to the reference point, not the path start.
        // applyCut keeps the path end unless flipHeuristics, so translate.
        var f0 = fractionOfPoint(context, wp, resolvePoint(context, definition.fromPoint));
        var cutIsAfterPoint = fractions[0] > f0;
        var keepEnd = (definition.trimKeep == TRIM_KEEP.FAR_SIDE) == cutIsAfterPoint;
        definition.flipHeuristics = !keepEnd;
    }

    var result = applyCut(context, id, definition, wire, wp, fractions);
    result.endNote = endOfPath == undefined ? "" : "The reference point is at the " ~ (endOfPath == 0 ? "start" : "end")
        ~ " of the curve, so the distance runs into the curve (Opposite direction and the sign of the distance are ignored).";
    return result;
}

/**
 * Distance from ends. The ENDS are the ends of the whole wire (its path), not of its edges. Two distances are applied
 * (the same at both ends, or a start and an end distance with the start end chosen by a picked point). A positive
 * distance TRIMS that much off the end; a negative distance EXTENDS the end by that much (opMoveCurveBoundary EXTEND).
 *   TRIM  keeps the middle: each end is handled in place in turn, so the wire body (and references to it) survive.
 *         When the second end cannot be done after the first (the in-place trim needs a clear side), the other order is
 *         tried on a scratch copy first, so the order that works is the one used.
 *   SPLIT cuts at both distances and keeps all three pieces (it cannot extend).
 * The trimmed distances together must be shorter than the wire.
 */
function adjustEnds(context is Context, id is Id, definition is map, wire is Query) returns map
{
    const wp = wirePath(context, wire);
    const total = wp.total;
    const first = pathPointAtFraction(context, wp, 0);
    const last = pathPointAtFraction(context, wp, 1);
    if (norm(first - last) < CTC_ON_CURVE_TOL)
    {
        throw regenError("A closed curve has no ends to trim from", ["curves"]);
    }

    var startEnd = first;
    var otherEnd = last;
    var startDistance = definition.endsDistance;
    var endDistance = definition.endsDistance;
    if (definition.endsMode == END_TRIM_MODE.DIFFERENT_AT_EACH)
    {
        if (isQueryEmpty(context, definition.endsStartPoint))
        {
            throw regenError("Pick the start point at one end of the curve", ["endsStartPoint"]);
        }
        const which = endOfWireAt(context, wp, resolvePoint(context, definition.endsStartPoint));
        startEnd = which == 0 ? first : last;
        otherEnd = which == 0 ? last : first;
        startDistance = definition.endsStartDistance;
        endDistance = definition.endsEndDistance;
    }

    const trimmed = max(startDistance, 0 * meter) + max(endDistance, 0 * meter);
    if (trimmed >= total - CTC_ON_CURVE_TOL)
    {
        throw regenError("The trimmed distances (" ~ roundToPrecision(max(startDistance, 0 * meter) / millimeter, 3) ~ " mm and "
            ~ roundToPrecision(max(endDistance, 0 * meter) / millimeter, 3) ~ " mm) are not shorter than the curve ("
            ~ roundToPrecision(total / millimeter, 3) ~ " mm)", ["curves"]);
    }

    if (definition.operation == OPERATION.SPLIT)
    {
        if (startDistance < 0 * meter || endDistance < 0 * meter)
        {
            throw regenError("A negative distance extends the curve, which Split cannot do. Use Trim, or enter positive distances.", ["curves"]);
        }
        var fractions = [];
        if (startDistance > CTC_ON_CURVE_TOL)
        {
            fractions = append(fractions, endDistanceFraction(context, wp, startEnd, startDistance));
        }
        if (endDistance > CTC_ON_CURVE_TOL)
        {
            fractions = append(fractions, endDistanceFraction(context, wp, otherEnd, endDistance));
        }
        fractions = cleanFractions(fractions);
        if (size(fractions) == 0)
        {
            throw regenError("Both distances are zero, so there is nothing to cut", ["curves"]);
        }
        return applyCut(context, id, definition, wire, wp, fractions);
    }

    var active = [];
    if (abs(startDistance) > CTC_ON_CURVE_TOL)
    {
        active = append(active, [startEnd, startDistance, "a"]);
    }
    if (abs(endDistance) > CTC_ON_CURVE_TOL)
    {
        active = append(active, [otherEnd, endDistance, "b"]);
    }
    if (size(active) == 0)
    {
        throw regenError("Both distances are zero, so there is nothing to trim", ["curves"]);
    }

    // Both ends: use the order that works. The first order is checked on a scratch copy; if it fails, the other is.
    var steps = active;
    if (size(active) == 2)
    {
        const reasonFirst = dryRunEndSteps(context, id, definition, wire, active);
        if (reasonFirst != "")
        {
            const reversed = [active[1], active[0]];
            const reasonSecond = dryRunEndSteps(context, id, definition, wire, reversed);
            if (reasonSecond != "")
            {
                throw regenError("Neither order of the two end changes works. Start end first: " ~ reasonFirst
                    ~ " Other end first: " ~ reasonSecond, ["curves"]);
            }
            steps = reversed;
        }
    }
    return runEndSteps(context, id, definition, wire, steps);
}

/**
 * Apply the end steps in order, each on the wire the previous one left. A step is [end point, signed distance, id suffix]:
 * positive trims that far in from the end (in place), negative extends the end by that much.
 */
function runEndSteps(context is Context, id is Id, definition is map, wire is Query, steps is array) returns map
{
    var current = wire;
    var cutPoints = [];
    var route = "";
    var via = undefined;
    var reason = "";
    for (var step in steps)
    {
        const stepWp = wirePath(context, current);
        const which = endOfWireAt(context, stepWp, step[0]);
        var result = {};
        if (step[1] < 0 * meter)
        {
            result = extendEnd(context, id + ("end" ~ step[2]), definition, current, step[0], -step[1]);
        }
        else
        {
            var stepDef = definition;
            stepDef.operation = OPERATION.TRIM;
            // Cutting near the path start keeps the path END side; cutting near the path end keeps the path START side.
            stepDef.flipHeuristics = which != 0;
            const fraction = which == 0 ? step[1] / stepWp.total : 1 - step[1] / stepWp.total;
            result = applyCut(context, id + ("end" ~ step[2]), stepDef, current, stepWp, [fraction]);
        }
        current = result.output;
        cutPoints = concatenateArrays([cutPoints, result.cutPoints]);
        if (result.route == "replaced" || route == "")
        {
            route = result.route == undefined ? route : result.route;
        }
        if (result.via != undefined)
        {
            via = result.via;
        }
        if (result.reason != undefined && result.reason != "")
        {
            reason = result.reason;
        }
    }
    var out = { "output" : current, "cutPoints" : cutPoints, "pieces" : [current] };
    if (route != "")
    {
        out.route = route;
        out.via = via;
        out.reason = reason;
    }
    return out;
}

/**
 * Does this order of end steps work? Runs it on a scratch copy of the wire and deletes everything it made. Returns ""
 * when it worked, otherwise why not.
 */
function dryRunEndSteps(context is Context, id is Id, definition is map, wire is Query, steps is array) returns string
{
    const dryId = id + ("dry" ~ steps[0][2]);
    var reason = "";
    try
    {
        opExtractWires(context, dryId + "copy", { "edges" : qOwnedByBody(wire, EntityType.EDGE) });
        runEndSteps(context, dryId + "run", definition, qCreatedBy(dryId + "copy", EntityType.BODY), steps);
    }
    catch (dryError)
    {
        reason = toString(dryError);
    }
    const leftovers = qCreatedBy(dryId, EntityType.BODY);
    if (!isQueryEmpty(context, leftovers))
    {
        opDeleteBodies(context, dryId + "delete", { "entities" : leftovers });
    }
    return reason;
}

/**
 * Extend the wire end at `endPoint` by `distance` (positive) along its end tangent or curvature, in place.
 */
function extendEnd(context is Context, id is Id, definition is map, wire is Query, endPoint is Vector, distance is ValueWithUnits) returns map
{
    const endVertex = qContainsPoint(qOwnedByBody(wire, EntityType.VERTEX), endPoint);
    if (isQueryEmpty(context, endVertex))
    {
        throw regenError("Could not find the end of the curve to extend", ["curves"]);
    }
    opMoveCurveBoundary(context, id + "extend", {
                "wires" : wire,
                "moveBoundaryType" : MoveCurveBoundaryType.EXTEND,
                "endCondition" : CurveExtensionEndCondition.BLIND,
                "extensionDistance" : distance,
                "extensionShape" : definition.endsExtensionShape,
                "helpPoint" : endVertex,
                "flipHeuristics" : false
            });
    // The wire keeps its body; find it again through its other end, which has not moved.
    return { "output" : wire, "cutPoints" : [endPoint], "pieces" : [wire], "route" : "inPlace",
             "via" : "extended " ~ roundToPrecision(distance / millimeter, 3) ~ " mm" };
}
/**
 * Which end of the wire path a point is at: 0 (path start) or 1 (path end). Throws when the point is not at an end.
 */
function endOfWireAt(context is Context, wp is map, point is Vector) returns number
{
    const f = fractionOfPoint(context, wp, point);
    const tol = max(CTC_FRACTION_EPS, CTC_ON_CURVE_TOL / wp.total);
    if (f <= tol)
    {
        return 0;
    }
    if (f >= 1 - tol)
    {
        return 1;
    }
    throw regenError("The start point is not at an end of the curve (it is " ~ roundToPrecision(f * wp.total / millimeter, 3)
        ~ " mm along a " ~ roundToPrecision(wp.total / millimeter, 3) ~ " mm curve)", ["endsStartPoint"]);
}

/**
 * Path fraction of the cut `distance` in from the wire end at `endPoint`.
 */
function endDistanceFraction(context is Context, wp is map, endPoint is Vector, distance is ValueWithUnits) returns number
{
    const which = endOfWireAt(context, wp, endPoint);
    return which == 0 ? distance / wp.total : 1 - distance / wp.total;
}

/**
 * Which end of the wire's path a reference point sits on: 0 (the path start), 1 (the path end), or undefined when it is
 * an interior point -- or when "end" is ambiguous: a closed wire (both ends coincide), or a wire that passes through the
 * same spot elsewhere (sampled along the path like the in-place trim's crossing test, so a touch is found to about
 * 2 sample spacings). The path direction comes from wirePath and is not tied to the point, so the same geometry can run
 * either way: this is what the distance rule keys on instead.
 */
function pathEndOfPoint(context is Context, wp is map, f0 is number)
{
    const tol = max(CTC_FRACTION_EPS, CTC_ON_CURVE_TOL / wp.total);
    var which = undefined;
    if (f0 <= tol)
    {
        which = 0;
    }
    else if (f0 >= 1 - tol)
    {
        which = 1;
    }
    if (which == undefined)
    {
        return undefined;
    }
    const here = pathPointAtFraction(context, wp, which);
    if (norm(pathPointAtFraction(context, wp, 0) - pathPointAtFraction(context, wp, 1)) < CTC_ON_CURVE_TOL)
    {
        return undefined;
    }
    const n = CTC_TRIM_CROSSING_SAMPLES;
    var samples = [];
    for (var i = 0; i <= n; i += 1)
    {
        samples = append(samples, i / n);
    }
    const touchTol = 2 * wp.total / n;
    const endZone = 6 / n;
    var index = 0;
    for (var tl in evPathTangentLines(context, wp.path, samples).tangentLines)
    {
        if (abs(index / n - which) > endZone && norm(tl.origin - here) < touchTol)
        {
            return undefined;
        }
        index += 1;
    }
    return which;
}

/**
 * Cut `wire` at every given path `fraction` (ascending) and apply the operation: SPLIT
 * keeps all pieces (as separate wires, or as one wire split in place), TRIM keeps one
 * side (the flip chooses which). Red dots preview each cut.
 *
 * @returns {{
 *      @field output {Query} : The resulting wire bodies of this curve.
 *      @field cutPoints {array} : World point of each cut, start to end.
 *      @field pieces {array} : One query per kept span between cuts (bodies, or edges when split in place).
 * }}
 */
function applyCut(context is Context, id is Id, definition is map, wire is Query, wp is map, fractions is array) returns map
{
    if (size(fractions) == 0)
    {
        return { "output" : wire, "cutPoints" : [], "pieces" : [wire] };
    }

    var points = [];
    for (var f in fractions)
    {
        points = append(points, pathPointAtFraction(context, wp, f));
    }
    markCutPoints(context, points);

    if (definition.operation == OPERATION.SPLIT && definition.returnSingleWire)
    {
        // Split in place: the wire stays one body (downstream references to it survive).
        var start = pathPointAtFraction(context, wp, 0);
        splitEdgesAtFractions(context, id, wp, fractions);
        var spans = [];
        for (var g in edgesBetweenCuts(context, wire, start, fractions))
        {
            spans = append(spans, qUnion(g));
        }
        return { "output" : wire, "cutPoints" : points, "pieces" : spans };
    }

    var inPlaceReason = "";
    if (definition.operation == OPERATION.TRIM && size(fractions) == 1)
    {
        // Preferred: trim in place so the wire body (and references to it) survive.
        var inPlace = trimWireInPlace(context, id, wire, wp, fractions[0], !definition.flipHeuristics);
        if (inPlace.done)
        {
            return { "output" : inPlace.wire, "cutPoints" : points, "pieces" : [inPlace.wire], "route" : "inPlace", "via" : inPlace.route };
        }
        inPlaceReason = inPlace.reason;
    }

    var pieces = [];
    try
    {
        pieces = splitWireIntoPieces(context, id, wire, wp, fractions);
    }
    catch (extractError)
    {
        // Say why the in-place trim was refused: this fallback is the last resort and its own error does not mention it.
        throw regenError("The wire could not be trimmed in place" ~ (inPlaceReason == "" ? "" : " (" ~ inPlaceReason ~ ")")
            ~ ", and extracting the pieces failed too (" ~ toString(extractError) ~ ")", ["curves"]);
    }
    if (definition.debugCuts)
    {
        println("applyCut: " ~ size(fractions) ~ " cut(s) -> " ~ size(pieces) ~ " piece(s)");
    }

    if (definition.operation == OPERATION.TRIM)
    {
        // pieces are ordered start->end; default keeps the end (front) side, and
        // the flip keeps the start (back) side.
        var keepIndex = definition.flipHeuristics ? 0 : (size(pieces) - 1);
        keepOnePiece(context, id, pieces, keepIndex);
        // "replaced": the single-cut trim could not run in place, so the wire was extracted and the original deleted.
        return { "output" : pieces[keepIndex], "cutPoints" : points, "pieces" : [pieces[keepIndex]],
                 "route" : size(fractions) == 1 ? "replaced" : "", "reason" : inPlaceReason };
    }
    return { "output" : qUnion(pieces), "cutPoints" : points, "pieces" : pieces };
}

/**
 * The single edge of a wire, or a clear error if it is not a single-edge wire
 * (inflection mode only; the other modes take any wire).
 */
function singleEdgeOf(context is Context, wire is Query) returns Query
{
    var edges = evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE));
    if (size(edges) != 1)
    {
        throw regenError("Inflection mode needs a single-edge wire", ["curves"]);
    }
    return edges[0];
}

/**
 * AT_INFLECTION: solve the curve's inflection points, then either auto-cut at the
 * inflection nearest each endpoint (NEAR_ENDS), or drop a multi-select toggle dot
 * at each and cut at every toggled one (PICK). With nothing toggled, only the dots
 * show. The curve is a single edge, so its edge fractions are its path fractions.
 */
function adjustAtInflection(context is Context, id is Id, definition is map, wire is Query) returns map
{
    var edge = singleEdgeOf(context, wire);
    var wp = wirePath(context, wire);

    var fractions = solveInflectionFractions(context, edge);
    if (definition.debugInflections)
    {
        var lenMm = wp.total / millimeter;
        println("== inflections: " ~ size(fractions) ~ " found, edge length " ~ lenMm ~ " mm ==");
        for (var i = 0; i < size(fractions); i += 1)
        {
            println("  [" ~ i ~ "] f=" ~ fractions[i] ~ "  fromStart=" ~ (fractions[i] * lenMm) ~ " mm  fromEnd=" ~ ((1 - fractions[i]) * lenMm) ~ " mm");
        }
    }
    if (size(fractions) == 0)
    {
        throw regenError("No inflection points found on this curve", ["curves"]);
    }

    if (definition.inflectionMode == INFLECTION_MODE.NEAR_ENDS)
    {
        // Auto: the inflection nearest each endpoint (first and last, since they
        // arrive sorted). cleanFractions de-dups a collapsed pair and drops any
        // root solved right on an endpoint. NEAR_ENDS needs its own keep logic: a
        // TRIM keeps the MIDDLE (drops both ends), unlike applyCut's single-end trim.
        var nearFractions = cleanFractions(endpointClosestFractions(fractions));
        if (size(nearFractions) == 0)
        {
            throw regenError("No valid inflection cut on this curve", ["curves"]);
        }
        if (definition.operation == OPERATION.SPLIT)
        {
            return applyCut(context, id + "inflCut", definition, wire, wp, nearFractions);
        }

        var points = [];
        for (var f in nearFractions)
        {
            points = append(points, pathPointAtFraction(context, wp, f));
        }
        markCutPoints(context, points);

        var pieces = splitWireIntoPieces(context, id + "inflCut", wire, wp, nearFractions);
        if (definition.debugCuts)
        {
            println("NEAR_ENDS: nearFractions=" ~ nearFractions ~ " -> " ~ size(pieces) ~ " piece(s)");
        }

        // 2 cuts -> 3 pieces (start | middle | end): keep the middle span. A
        // single inflection -> 2 pieces: ordinary single-end trim.
        var lo = definition.flipHeuristics ? 0 : (size(pieces) - 1);
        var hi = lo;
        if (size(pieces) >= 3)
        {
            lo = 1;
            hi = size(pieces) - 2;
        }
        keepPieceRange(context, id + "inflCut", pieces, lo, hi);
        var kept = subArray(pieces, lo, hi + 1);
        return { "output" : qUnion(kept), "cutPoints" : points, "pieces" : kept };
    }

    // PICK: a toggle dot at each inflection; cut at every toggled one.
    var pts = [];
    for (var i = 0; i < size(fractions); i += 1)
    {
        pts = append(pts, pathPointAtFraction(context, wp, fractions[i]));
    }
    var selectedIdx = mapArray(definition.inflectionIndices, function(sel)
        {
            return sel.index;
        });
    addManipulators(context, id, {
                (INFLECTION_MANIPULATOR) : togglePointsManipulator({
                            "points" : pts,
                            "selectedIndices" : selectedIdx,
                            "suppressedIndices" : []
                        })
            });

    // Cut at each toggled inflection, sorted along the curve for the splitter.
    var chosen = [];
    for (var sel in definition.inflectionIndices)
    {
        if (sel.index < size(fractions))
        {
            chosen = append(chosen, fractions[sel.index]);
        }
    }
    chosen = sort(chosen, function(a, b) { return a - b; });
    return applyCut(context, id + "inflCut", definition, wire, wp, chosen);
}

/**
 * The inflection fractions nearest the two curve endpoints. Inflections arrive
 * sorted along the curve, so these are just the first and last. Only filters when
 * there are 3 or more; with 1-2 they are already the endpoint-nearest ones.
 */
function endpointClosestFractions(fractions is array) returns array
{
    if (size(fractions) < 3)
    {
        return fractions;
    }
    return [fractions[0], fractions[size(fractions) - 1]];
}

/**
 * Path cut fractions (0..1 along the wire) for the selected cut mode. Each mode
 * validates its own required inputs.
 */
function cutFractionsFor(context is Context, definition is map, wp is map, endOfPath) returns array
{
    if (definition.cutBy == CUT_BY.UP_TO_ENTITY)
    {
        if (isQueryEmpty(context, definition.boundary))
        {
            throw regenError("Select an entity to cut up to", ["boundary"]);
        }
        return [fractionNearestEntity(context, wp, definition.boundary)];
    }
    else if (definition.cutBy == CUT_BY.ARC_LENGTH_FROM_POINT)
    {
        if (isQueryEmpty(context, definition.fromPoint))
        {
            throw regenError("Select a point to measure from", ["fromPoint"]);
        }
        var f0 = fractionOfPoint(context, wp, resolveAndMark(context, wp, definition.fromPoint));
        // From an END of the curve the distance runs into the curve whatever the path direction (the flip and the sign are
        // ignored); from an interior point the flip chooses the direction along the path as before.
        var atEnd = endOfPath != undefined;
        var dir = atEnd ? (endOfPath == 0 ? 1 : -1) : (definition.distanceFlip ? -1 : 1);
        markPositiveDirection(context, wp, f0, dir);

        var distances = [definition.distance];
        if (definition.operation == OPERATION.SPLIT)
        {
            for (var item in definition.extraDistances)
            {
                distances = append(distances, item.extraDistance);
            }
        }

        var fractions = [];
        for (var i = 0; i < size(distances); i += 1)
        {
            var f = fractionAtSignedDistance(wp, f0, atEnd ? abs(distances[i]) : distances[i], dir);
            if (f <= 0 || f >= 1)
            {
                var which = (i == 0) ? "The distance" : ("Additional distance " ~ i);
                throw regenError(which ~ " (" ~ roundToPrecision(distances[i] / millimeter, 3) ~ " mm) runs past the end of the curve (reference " ~ roundToPrecision(f0 * wp.total / millimeter, 3) ~ " mm from its start, curve " ~ roundToPrecision(wp.total / millimeter, 3) ~ " mm long)", [(i == 0) ? "distance" : "extraDistances"]);
            }
            fractions = append(fractions, f);
        }
        if (definition.operation == OPERATION.SPLIT && definition.splitAtReference)
        {
            fractions = append(fractions, f0);
        }
        return fractions;
    }
    else if (definition.cutBy == CUT_BY.EVEN_DIVISION)
    {
        if (definition.divisionMode == DIVISION_MODE.BETWEEN_POINTS)
        {
            if (isQueryEmpty(context, definition.startPoint) || isQueryEmpty(context, definition.endPoint))
            {
                throw regenError("Select start and end points for the even division", ["startPoint"]);
            }
            var a = resolveAndMark(context, wp, definition.startPoint);
            var b = resolveAndMark(context, wp, definition.endPoint);
            return evenDivisionFractions(context, wp, a, b, definition.divisions);
        }
        if (isQueryEmpty(context, definition.spacingFromPoint))
        {
            throw regenError("Select a point to space cuts from", ["spacingFromPoint"]);
        }
        var f0 = fractionOfPoint(context, wp, resolveAndMark(context, wp, definition.spacingFromPoint));
        var dir = spacingDirection(f0, definition.spacingReverse);
        markPositiveDirection(context, wp, f0, dir);
        return spacedFromPointFractions(wp, f0, definition.spacing, definition.spacingCount, dir);
    }
    else
    {
        if (isQueryEmpty(context, definition.atPoints))
        {
            throw regenError("Select one or more cut points", ["atPoints"]);
        }
        var fractions = [];
        for (var q in evaluateQuery(context, definition.atPoints))
        {
            fractions = append(fractions, fractionOfPoint(context, wp, resolveAndMark(context, wp, q)));
        }
        return fractions;
    }
}

/**
 * Drop fractions on or past either endpoint, then sort and de-duplicate so
 * coincident picks do not produce zero-length slivers.
 */
function cleanFractions(fractions is array) returns array
{
    var kept = [];
    for (var f in fractions)
    {
        if (f > CTC_FRACTION_EPS && f < 1 - CTC_FRACTION_EPS)
        {
            kept = append(kept, f);
        }
    }

    kept = sort(kept, function(a, b) { return a - b; });

    var out = [];
    for (var f in kept)
    {
        if (size(out) == 0 || abs(f - out[size(out) - 1]) > CTC_FRACTION_EPS)
        {
            out = append(out, f);
        }
    }
    return out;
}

/**
 * World point of a vertex or mate-connector pick. A connector pick can arrive as the
 * connector's vertex (correction 44), so resolve through the owner body.
 */
function resolvePoint(context is Context, q is Query) returns Vector
{
    var connector = evaluateQuery(context, qBodyType(qOwnerBody(q), BodyType.MATE_CONNECTOR));
    if (size(connector) > 0)
    {
        return evMateConnector(context, { "mateConnector" : connector[0] }).origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/**
 * Resolve a pick to a world point and, if it lies off the curve, draw the dashed
 * reference line to its projection - the shared "measure from this point" step for
 * the point-based cut modes.
 */
function resolveAndMark(context is Context, wp is map, q is Query) returns Vector
{
    var pt = resolvePoint(context, q);
    markProjectionToCurve(context, wp, pt);
    return pt;
}

// ============================================================================
// VARIABLE_TOOLS OUTPUTS
// ============================================================================

/**
 * Publish the standard keys plus cut_1..n (the vertex at each cut, start to end; with
 * several curves, cut k of every curve), cutVertices, piece_1..m and cutCount.
 */
function embedTrimOutputs(context is Context, id is Id, definition is map, results is array)
{
    var outputs = [];
    var maxCuts = 0;
    var maxPieces = 0;
    var cutCount = 0;
    for (var r in results)
    {
        outputs = append(outputs, r.output);
        maxCuts = max(maxCuts, size(r.cutPoints));
        maxPieces = max(maxPieces, size(r.pieces));
        cutCount += size(r.cutPoints);
    }
    var output = qUnion(outputs);
    var vertices = qOwnedByBody(output, EntityType.VERTEX);

    var queries = {};
    var allCuts = [];
    for (var k = 0; k < maxCuts; k += 1)
    {
        var atK = [];
        for (var r in results)
        {
            if (k < size(r.cutPoints))
            {
                atK = append(atK, qContainsPoint(vertices, r.cutPoints[k]));
            }
        }
        queries["cut_" ~ (k + 1)] = extractableQuery(qUnion(atK), "The vertex at cut " ~ (k + 1) ~ ", counted from the curve start.", DebugColor.RED);
        allCuts = concatenateArrays([allCuts, atK]);
    }
    queries["cutVertices"] = extractableQuery(qUnion(allCuts), "Every cut vertex.", DebugColor.RED);

    for (var k = 0; k < maxPieces; k += 1)
    {
        var atK = [];
        for (var r in results)
        {
            if (k < size(r.pieces))
            {
                atK = append(atK, r.pieces[k]);
            }
        }
        queries["piece_" ~ (k + 1)] = extractableQuery(qUnion(atK), "Span " ~ (k + 1) ~ " between cuts, counted from the curve start (a body, or edges when split in place).", DebugColor.BLUE);
    }

    embedStandardOutputs(context, id, {
                "output" : output,
                "outputDescription" : "The trimmed or split curves",
                "inputs" : definition.curves,
                "variables" : { "cutCount" : extractableVariable(cutCount, "Number of cuts made (all curves).") },
                "queries" : queries
            });
}

// ============================================================================
// MANIPULATOR CHANGE FUNCTION
// ============================================================================

/**
 * Records which inflection dots the user toggled into the hidden inflectionIndices,
 * so the body cuts at each on the next regen (and keeps them highlighted). Only
 * fires when the inflection picker is present; other modes add no manipulators.
 */
export function onInflectionPick(context is Context, definition is map, newManipulators is map) returns map
{
    if (newManipulators[INFLECTION_MANIPULATOR] is map)
    {
        var items = [];
        for (var i in newManipulators[INFLECTION_MANIPULATOR].selectedIndices)
        {
            items = append(items, { "index" : i });
        }
        definition.inflectionIndices = items;
    }
    return definition;
}
