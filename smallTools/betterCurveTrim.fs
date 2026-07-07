FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

// curveTrimCore (same document) - plane-cut trim/split engine
import(path : "d56d74c24234ab2b885e6fc1", version : "");

/**
 * betterCurveTrim - a more powerful curve trim/split, in the look and feel of
 * the stock Onshape "Trim curve" (see OS_Trim.fs, reference only - not imported).
 *
 * Roadmap:
 *   1. Trim OR split (split keeps all geometry; optionally return one wire).   [Phase 1]
 *   2. Cut at multiple points, or evenly along arc length between two points.  [Phase 1]
 *   3. Cut a blind arc-length distance from a help point, toward the midpoint.  [Phase 1]
 *   4. EXTEND (pass-through to opMoveCurveBoundary) to fully supersede OS_Trim.  [Phase 1.5]
 *   5. Cut at solved inflection points, picked via interactive manipulators.    [Phase 2]
 *
 * All cutting is done by building a plane perpendicular to the curve at each cut
 * location and letting opSplitPart do the work (see curveTrimCore.fs). This
 * keeps everything in arc-length space - no BSpline knot handling.
 *
 * Phase 1 note: the arc-length cut modes operate on single-edge wires. Multi-
 * edge wires (via constructPath) are a follow-up.
 */

// ============================================================================
// ENUMS
// ============================================================================

/**
 * Top-level operation. TRIM keeps one side of a single cut; SPLIT keeps every
 * piece. (EXTEND is added in Phase 1.5.)
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
    AT_POINTS
}

// ============================================================================
// FEATURE
// ============================================================================

annotation { "Feature Type Name" : "Trim curve +" }
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
            annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.startPoint is Query;

            annotation { "Name" : "End point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.endPoint is Query;

            annotation { "Name" : "Number of segments" }
            isInteger(definition.divisions, CTC_DIVISION_BOUNDS);
        }
        else
        {
            annotation { "Name" : "Cut points", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR }
            definition.atPoints is Query;
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
    }
    {
        if (isQueryEmpty(context, definition.curves))
        {
            throw regenError("Select one or more curves to adjust", ["curves"]);
        }

        var wires = evaluateQuery(context, definition.curves);
        for (var w = 0; w < size(wires); w += 1)
        {
            adjustOneCurve(context, id + ("curve" ~ w), definition, wires[w]);
        }
    });

// ============================================================================
// PER-CURVE WORK
// ============================================================================

/**
 * Trim or split a single wire. Phase 1 requires a single-edge wire so arc-length
 * fractions map to one edge; the guard makes that explicit rather than silently
 * mis-measuring a multi-edge wire.
 */
function adjustOneCurve(context is Context, id is Id, definition is map, wire is Query)
{
    var edges = evaluateQuery(context, qOwnedByBody(wire, EntityType.EDGE));
    if (size(edges) != 1)
    {
        throw regenError("Each curve must currently be a single-edge wire", ["curves"]);
    }
    var edge = edges[0];

    var fractions = cleanFractions(cutFractionsFor(context, definition, edge));
    if (size(fractions) == 0)
    {
        throw regenError("No valid cut location found on the curve", ["curves"]);
    }

    // Trim is a single-cut operation - keep one side, discard the other - so
    // reduce to the first cut location. Which side is kept is chosen by the flip
    // boolean, identified by the curve endpoint it contains (captured here,
    // before opSplitPart consumes the edge). No help-point pick needed.
    var startPt = undefined;
    var endPt = undefined;
    if (definition.operation == OPERATION.TRIM)
    {
        if (size(fractions) > 1)
        {
            fractions = [fractions[0]];
        }
        startPt = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 0 }).origin;
        endPt = evEdgeTangentLine(context, { "edge" : edge, "parameter" : 1 }).origin;
    }

    var planes = [];
    for (var i = 0; i < size(fractions); i += 1)
    {
        planes = append(planes, cutPlaneAtFraction(context, edge, fractions[i]));
    }

    var segments = splitWireWithPlanes(context, id, wire, planes);

    if (definition.operation == OPERATION.TRIM)
    {
        var reference = definition.flipHeuristics ? endPt : startPt;
        keepSegmentByPoint(context, id, segments, reference, true);
    }
    else if (definition.returnSingleWire)
    {
        unionSegments(context, id, segments);
    }
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
        var from = resolvePoint(context, definition.fromPoint);
        return [fractionAtDistanceTowardMid(context, edge, from, definition.distance)];
    }
    else if (definition.cutBy == CUT_BY.EVEN_DIVISION)
    {
        if (isQueryEmpty(context, definition.startPoint) || isQueryEmpty(context, definition.endPoint))
        {
            throw regenError("Select start and end points for the even division", ["startPoint"]);
        }
        var a = resolvePoint(context, definition.startPoint);
        var b = resolvePoint(context, definition.endPoint);
        return evenDivisionFractions(context, edge, a, b, definition.divisions);
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
            fractions = append(fractions, fractionOfPointOnEdge(context, edge, resolvePoint(context, pts[i])));
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
