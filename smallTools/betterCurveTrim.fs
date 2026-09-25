FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// curveTrimCore (same document) - plane-cut trim/split engine
import(path : "d56d74c24234ab2b885e6fc1", version : "c78d3783298db6306d08f3a8");

// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/78504463aa9ea7fa3cce2789/3cac74f0bc2b98272db13cd3", version : "b8c80ac05dcfd9f3cc172ffc");

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
    AT_INFLECTION
}

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

// Manipulator key for the inflection-point picker - shared between the feature
// body (which adds it) and the change function (which reads the toggled indices).
const INFLECTION_MANIPULATOR = "inflectionManipulator";

// ============================================================================
// FEATURE
// ============================================================================

annotation { "Feature Type Name" : "Trim curve +", "Icon" : IconNamespace::BLOB_DATA, "Manipulator Change Function" : "onInflectionPick" }
export const betterCurveTrim = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Operation", "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : OPERATION.SPLIT }
        definition.operation is OPERATION;

        annotation { "Name" : "Curves to adjust", "Filter" : EntityType.BODY && BodyType.WIRE && SketchObject.NO }
        definition.curves is Query;

        annotation { "Name" : "Cut location", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : CUT_BY.AT_POINTS }
        definition.cutBy is CUT_BY;

        if (definition.cutBy == CUT_BY.UP_TO_ENTITY)
        {
            annotation { "Name" : "Up to entity", "Filter" : (EntityType.BODY && SketchObject.NO) || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.boundary is Query;
        }
        else if (definition.cutBy == CUT_BY.ARC_LENGTH_FROM_POINT)
        {
            annotation { "Name" : "From point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.fromPoint is Query;

            annotation { "Name" : "Distance", "Description" : "Arc length from the reference point. Positive runs the way the blue arrow points; negative runs the other way." }
            isLength(definition.distance, LENGTH_BOUNDS);

            annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Reverse the positive direction (blue arrow)." }
            definition.distanceFlip is boolean;

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
            annotation { "Name" : "Division style", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : DIVISION_MODE.BETWEEN_POINTS }
            definition.divisionMode is DIVISION_MODE;

            if (definition.divisionMode == DIVISION_MODE.BETWEEN_POINTS)
            {
                annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.startPoint is Query;

                annotation { "Name" : "End point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.endPoint is Query;

                annotation { "Name" : "Number of segments" }
                isInteger(definition.divisions, CTC_DIVISION_BOUNDS);
            }
            else
            {
                annotation { "Name" : "From point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                definition.spacingFromPoint is Query;

                annotation { "Name" : "Spacing" }
                isLength(definition.spacing, CTC_SPACING_BOUNDS);

                annotation { "Name" : "Number of cuts" }
                isInteger(definition.spacingCount, CTC_SPACING_COUNT_BOUNDS);

                annotation { "Name" : "Reverse direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false, "Description" : "Space the cuts away from the midpoint instead of toward it." }
                definition.spacingReverse is boolean;
            }
        }
        else if (definition.cutBy == CUT_BY.AT_POINTS)
        {
            annotation { "Name" : "Cut points", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.atPoints is Query;
        }
        else if (definition.cutBy == CUT_BY.AT_INFLECTION)
        {
            // Inflections are solved from the curve; this only chooses which to cut.
            annotation { "Name" : "Inflection selection", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE], "Default" : INFLECTION_MODE.PICK }
            definition.inflectionMode is INFLECTION_MODE;
        }

        if (definition.operation == OPERATION.SPLIT)
        {
            annotation { "Name" : "Return single wire", "Default" : false, "Description" : "Recombine the split pieces into one wire body with multiple edges." }
            definition.returnSingleWire is boolean;
        }
        else
        {
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
    },
    {
        "inflectionIndices" : [],
        "distanceFlip" : false,
        "extraDistances" : [],
        "splitAtReference" : false
    });

// ============================================================================
// PER-CURVE WORK
// ============================================================================

/**
 * Trim or split a single wire at the cut locations for the selected mode. Returns
 * the per-curve result (see [applyCut]).
 */
function adjustOneCurve(context is Context, id is Id, definition is map, wire is Query) returns map
{
    var wp = wirePath(context, wire);

    var fractions = cleanFractions(cutFractionsFor(context, definition, wp));
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

    return applyCut(context, id, definition, wire, wp, fractions);
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

    var pieces = splitWireIntoPieces(context, id, wire, wp, fractions);
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
        return { "output" : pieces[keepIndex], "cutPoints" : points, "pieces" : [pieces[keepIndex]] };
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
function cutFractionsFor(context is Context, definition is map, wp is map) returns array
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
        var dir = definition.distanceFlip ? -1 : 1;
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
            var f = fractionAtSignedDistance(wp, f0, distances[i], dir);
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
