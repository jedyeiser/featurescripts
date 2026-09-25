FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// curveTrimCore (same document) - plane-cut trim/split engine
import(path : "d56d74c24234ab2b885e6fc1", version : "7ca8028126823f3fd7c28f56");

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
 *   - Cut a blind arc-length distance from a help point, toward the midpoint.   [done]
 *   - Cut at solved inflection points: pick any (toggle), or nearest each end.  [done]
 *   - EXTEND (pass-through to opMoveCurveBoundary) to fully supersede OS_Trim.   [pending]
 *
 * All cutting is done by building a plane perpendicular to the curve at each cut
 * location and letting opSplitPart do the work (see curveTrimCore.fs). This keeps
 * everything in arc-length space - no BSpline knot handling.
 *
 * Note: the cut modes operate on single-edge wires. Multi-edge wires (via
 * constructPath) are a follow-up.
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

            annotation { "Name" : "Distance" }
            isLength(definition.distance, NONNEGATIVE_LENGTH_BOUNDS);
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

        if (definition.cutBy == CUT_BY.AT_INFLECTION)
        {
            // One picker per feature; keep to a single curve so the hidden selection
            // maps unambiguously to one manipulator.
            if (size(wires) != 1)
            {
                throw regenError("Inflection mode supports one curve at a time", ["curves"]);
            }
            adjustAtInflection(context, id, definition, wires[0]);
        }
        else
        {
            for (var w = 0; w < size(wires); w += 1)
            {
                adjustOneCurve(context, id + ("curve" ~ w), definition, wires[w]);
            }
        }
    },
    {
        "inflectionIndices" : []
    });

// ============================================================================
// PER-CURVE WORK
// ============================================================================

/**
 * Trim or split a single wire at the cut locations for the selected mode.
 */
function adjustOneCurve(context is Context, id is Id, definition is map, wire is Query)
{
    var edge = singleEdgeOf(context, wire);

    var fractions = cleanFractions(cutFractionsFor(context, definition, edge));
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

    applyCut(context, id, definition, wire, edge, fractions);
}

/**
 * Cut `wire` at every given arc-length `fraction` and apply the operation: SPLIT
 * keeps all pieces (optionally recombined into one wire), TRIM keeps one side
 * (the flip chooses which). Red dots preview each cut.
 */
function applyCut(context is Context, id is Id, definition is map, wire is Query, edge is Query, fractions is array)
{
    if (size(fractions) == 0)
    {
        return;
    }

    var planes = [];
    for (var i = 0; i < size(fractions); i += 1)
    {
        planes = append(planes, cutPlaneAtFraction(context, edge, fractions[i]));
    }
    markCutPoints(context, planes);

    var pieces = splitWireIntoPieces(context, id, wire, planes);
    if (definition.debugCuts)
    {
        println("applyCut: " ~ size(fractions) ~ " fraction(s), " ~ size(planes) ~ " plane(s) -> " ~ size(pieces) ~ " piece(s)");
    }

    if (definition.operation == OPERATION.TRIM)
    {
        // pieces are ordered start->end; default keeps the end (front) side, and
        // the flip keeps the start (back) side.
        var keepIndex = definition.flipHeuristics ? 0 : (size(pieces) - 1);
        keepOnePiece(context, id, pieces, keepIndex);
    }
    else if (definition.returnSingleWire)
    {
        combineSplitToSingleWire(context, id, pieces);
    }
}

/**
 * The single edge of a wire, or a clear error if it is not a single-edge wire.
 * (Multi-edge wires via constructPath are a follow-up.)
 */
function singleEdgeOf(context is Context, wire is Query) returns Query
{
    var edges = evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE));
    if (size(edges) != 1)
    {
        throw regenError("Each curve must currently be a single-edge wire", ["curves"]);
    }
    return edges[0];
}

/**
 * AT_INFLECTION: solve the curve's inflection points, then either auto-cut at the
 * inflection nearest each endpoint (NEAR_ENDS), or drop a multi-select toggle dot
 * at each and cut at every toggled one (PICK). With nothing toggled, only the dots
 * show.
 */
function adjustAtInflection(context is Context, id is Id, definition is map, wire is Query)
{
    var edge = singleEdgeOf(context, wire);

    var fractions = solveInflectionFractions(context, edge);
    if (definition.debugInflections)
    {
        var lenMm = evLength(context, { "entities" : edge }) / millimeter;
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

        var planes = [];
        for (var i = 0; i < size(nearFractions); i += 1)
        {
            planes = append(planes, cutPlaneAtFraction(context, edge, nearFractions[i]));
        }
        markCutPoints(context, planes);

        var pieces = splitWireIntoPieces(context, id + "inflCut", wire, planes);
        if (definition.debugCuts)
        {
            println("NEAR_ENDS: nearFractions=" ~ nearFractions ~ " -> " ~ size(planes) ~ " plane(s), " ~ size(pieces) ~ " piece(s)");
        }

        if (definition.operation == OPERATION.TRIM)
        {
            // 2 cuts -> 3 pieces (start | middle | end): keep the middle span. A
            // single inflection -> 2 pieces: ordinary single-end trim.
            if (size(pieces) >= 3)
            {
                keepPieceRange(context, id + "inflCut", pieces, 1, size(pieces) - 2);
            }
            else
            {
                keepOnePiece(context, id + "inflCut", pieces, definition.flipHeuristics ? 0 : (size(pieces) - 1));
            }
        }
        else if (definition.returnSingleWire)
        {
            combineSplitToSingleWire(context, id + "inflCut", pieces);
        }
    }
    else
    {
        // PICK: a toggle dot at each inflection; cut at every toggled one.
        var pts = [];
        for (var i = 0; i < size(fractions); i += 1)
        {
            pts = append(pts, evEdgeTangentLine(context, { "edge" : edge, "parameter" : fractions[i] }).origin);
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
        applyCut(context, id + "inflCut", definition, wire, edge, chosen);
    }
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
 * Arc-length cut fractions (0..1 on the edge) for the selected cut mode. Each
 * mode validates its own required inputs.
 */
function cutFractionsFor(context is Context, definition is map, edge is Query) returns array
{
    if (definition.cutBy == CUT_BY.UP_TO_ENTITY)
    {
        if (isQueryEmpty(context, definition.boundary))
        {
            throw regenError("Select an entity to cut up to", ["boundary"]);
        }
        return [fractionNearestEntity(context, edge, definition.boundary)];
    }
    else if (definition.cutBy == CUT_BY.ARC_LENGTH_FROM_POINT)
    {
        if (isQueryEmpty(context, definition.fromPoint))
        {
            throw regenError("Select a point to measure from", ["fromPoint"]);
        }
        var from = resolveAndMark(context, edge, definition.fromPoint);
        return [fractionAtDistanceTowardMid(context, edge, from, definition.distance)];
    }
    else if (definition.cutBy == CUT_BY.EVEN_DIVISION)
    {
        if (definition.divisionMode == DIVISION_MODE.BETWEEN_POINTS)
        {
            if (isQueryEmpty(context, definition.startPoint) || isQueryEmpty(context, definition.endPoint))
            {
                throw regenError("Select start and end points for the even division", ["startPoint"]);
            }
            var a = resolveAndMark(context, edge, definition.startPoint);
            var b = resolveAndMark(context, edge, definition.endPoint);
            return evenDivisionFractions(context, edge, a, b, definition.divisions);
        }
        if (isQueryEmpty(context, definition.spacingFromPoint))
        {
            throw regenError("Select a point to space cuts from", ["spacingFromPoint"]);
        }
        var from = resolveAndMark(context, edge, definition.spacingFromPoint);
        return spacedFromPointFractions(context, edge, from, definition.spacing, definition.spacingCount, definition.spacingReverse);
    }
    else
    {
        if (isQueryEmpty(context, definition.atPoints))
        {
            throw regenError("Select one or more cut points", ["atPoints"]);
        }
        var pts = evaluateQuery(context, definition.atPoints);
        var fractions = [];
        for (var i = 0; i < size(pts); i += 1)
        {
            var pt = resolveAndMark(context, edge, pts[i]);
            fractions = append(fractions, fractionOfPointOnEdge(context, edge, pt));
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
 * World point of a vertex or mate-connector pick.
 */
function resolvePoint(context is Context, q is Query) returns Vector
{
    if (size(evaluateQuery(context, qBodyType(q, BodyType.MATE_CONNECTOR))) > 0)
    {
        return evMateConnector(context, { "mateConnector" : q }).origin;
    }
    return evVertexPoint(context, { "vertex" : q });
}

/**
 * Resolve a pick to a world point and, if it lies off the curve, draw the dashed
 * reference line to its projection - the shared "measure from this point" step for
 * the point-based cut modes.
 */
function resolveAndMark(context is Context, edge is Query, q is Query) returns Vector
{
    var pt = resolvePoint(context, q);
    markProjectionToCurve(context, edge, pt);
    return pt;
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
