FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

// IMPORT: curve_core.fs (export import: OffsetPointSpacing is a parameter type here)
export import(path : "02d7784437f621c76397f0d6", version : "71f4e8e4d07b445940796389");
// IMPORT: merge_curve.fs
import(path : "584469527ba0d496604523ec", version : "89d3f3abf456c89ff38fbb24");
// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");
// IMPORT: map_curve_icon.svg (feature icon)
IconNamespace::import(path : "45065c466dba5711602641c3", version : "dc2902a68da2b41d350af736");

/**
 * Map curve
 *
 * Carries curve data from one chain of edges (the FROM chain) onto another (the TO chain),
 * preserving distance along the curve from a reference point. A point s along the from-chain
 * from its reference lands s along the to-chain from its reference: an isometry along the
 * curve, measured in arc length. Design: research_map_curve.md; vocabulary:
 * research_shared_vocabulary.md.
 *
 * Phase 1 scope: the from-edges lie ON their own chain, so every from-point has (w, h) = 0 and
 * the mapped output is the to-chain re-cut to the from-chain's span. Off-chain from-edges
 * (projectOntoChain, LengthPreservation) are phase 2.
 *
 * Modes
 *   FROM_EDGES  sample the from-chain, place each sample at the same arc on the to-chain,
 *               classify runs (line / arc / freeform) and emit them, one wire per from-link.
 *   TO_EDGES    copy the to-edges into a fresh wire and trim that copy to the from-span. Never
 *               re-fits, so arcs stay arcs.
 *   SINGLE      FROM_EDGES, then merge every run into one spline (merge_curve core).
 *
 * Reference points: SHARED uses one mate connector / vertex for both chains, SEPARATE one per
 * chain. A reference lands on its chain by world X (the ski convention, same as
 * driven_edge_offset) or by closest point. World X falls back to closest point when the chain
 * does not span the point's X, and says so.
 *
 * Both chains are built ascending in world X (buildChain). Where that leaves the two chains
 * pointing opposite ways at the reference, the feature errors and asks for "Flip to-chain".
 *
 * Kernel calls are batched: one evEdgeTangentLines per to-edge that hosts samples, never one
 * per sample (evaluateLocated). Chain description costs are buildChain's (utils).
 *
 * Notices: only regenError turns the feature red. Expected outcomes (projection fallback, arc
 * radius lost in a merge) are reportFeatureInfo.
 */

// ============================================================================
// Enums and bounds
// ============================================================================

// TODO: shared helper (research_shared_vocabulary.md s4) -- these enums belong in
// edge_offset_utils.fs once evaluate_offset / unwrap take them up.

/** What the feature produces. */
export enum MapMode
{
    annotation { "Name" : "Deform from-edges onto to-edges" }
    FROM_EDGES,
    annotation { "Name" : "Trim to-edges to the from span" }
    TO_EDGES,
    annotation { "Name" : "Deform and merge to one curve" }
    SINGLE
}

/** One reference point for both chains, or one per chain. */
export enum RefPointMode
{
    annotation { "Name" : "Shared" }
    SHARED,
    annotation { "Name" : "Separate" }
    SEPARATE
}

/** How a reference point lands on its chain. */
export enum ProjectionMode
{
    annotation { "Name" : "World X" }
    WORLD_X,
    annotation { "Name" : "Closest point" }
    CLOSEST_POINT
}

/** How the emitted runs are grouped into bodies. */
export enum WireOutput
{
    annotation { "Name" : "One wire per link" }
    PER_LINK,
    annotation { "Name" : "One body per run" }
    PER_RUN
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- same value as driven_edge_offset.fs.
/** Control-point budget for a fitted run. The floor of 4 is a cubic's minimum. */
export const OffsetMaxCPBounds = { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec;

/**
 * A split this close to an edge end is not made: the vertex already sits there, and a
 * zero-length sliver is not geometry.
 */
export const SPLIT_END_FRACTION = 1e-6;

/**
 * Arc-length slack allowed on the span check and on piece classification. Well above
 * arithmetic noise, well below any distance a user could mean.
 */
export const SPAN_TOLERANCE = 1e-9 * meter;

/**
 * How far inside the chain's vertex X range a reference must sit for world-X projection to be
 * attempted. At the exact end vertex closest point gives the same arc without a secant search.
 */
export const SPAN_X_MARGIN = 1e-9 * meter;

// ============================================================================
// Feature
// ============================================================================

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Map curve", "Feature Type Description" : "Map curve data from one chain of edges onto another, preserving distance along the curve from a reference point" }
export const mapCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Mode", "Default" : MapMode.FROM_EDGES, "UIHint" : UIHint.HORIZONTAL_ENUM }
        definition.mapMode is MapMode;

        annotation { "Name" : "From edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The edges whose span, measured from the reference point, is carried onto the to-edges. Phase 1: they must lie on their own chain." }
        definition.fromEdges is Query;

        annotation { "Name" : "To edges", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The chain the output lies along. Never modified: the output is new geometry." }
        definition.toEdges is Query;

        annotation { "Name" : "Reference point", "Default" : RefPointMode.SHARED, "UIHint" : UIHint.HORIZONTAL_ENUM, "Description" : "Shared: one point is the zero station on both chains. Separate: each chain has its own." }
        definition.refPointMode is RefPointMode;

        if (definition.refPointMode == RefPointMode.SHARED)
        {
            annotation { "Name" : "Zero point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.offsetRefPoint is Query;
        }
        else
        {
            annotation { "Name" : "From zero point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.fromRefPoint is Query;

            annotation { "Name" : "To zero point", "Filter" : BodyType.MATE_CONNECTOR || EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
            definition.toRefPoint is Query;
        }

        annotation { "Name" : "Projection", "Default" : ProjectionMode.WORLD_X, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Description" : "How a zero point lands on its chain. World X is the ski convention: a mate connector at the FCP is the same station on the core bottom and on the sidecut whatever its Y or Z. Falls back to closest point where the chain does not span the point's X." }
        definition.projectionMode is ProjectionMode;

        annotation { "Name" : "Flip to-chain", "Default" : false, "UIHint" : UIHint.OPPOSITE_DIRECTION, "Description" : "Reverse the direction the to-chain is traversed. Required when the two chains point opposite ways at the reference point." }
        definition.flipTo is boolean;

        if (definition.mapMode != MapMode.TO_EDGES)
        {
            annotation { "Group Name" : "Spacing & approximation", "Collapsed By Default" : true }
            {
                offsetSpacingPredicate(definition);

                annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
                {
                    offsetApproximationPredicate(definition);
                }
            }
        }

        if (definition.mapMode == MapMode.FROM_EDGES)
        {
            annotation { "Name" : "Output", "Default" : WireOutput.PER_LINK, "UIHint" : UIHint.SHOW_LABEL, "Description" : "One wire per connected from-chain, or every emitted run left as its own body." }
            definition.wireOutput is WireOutput;
        }

        annotation { "Name" : "Name", "Description" : "Name given to the resulting bodies. Clear it to leave them unnamed." }
        definition.outputName is string;

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show chain ends", "Default" : false, "Description" : "Arrows on both chains: green at the start, red at the end, blue at the zero point, each pointing the way the chain is traversed." }
            definition.debugShowChainEnds is boolean;

            annotation { "Name" : "Print summary", "Default" : false, "Description" : "One block: chain lengths, zero arcs and projection, span, station and run counts." }
            definition.debugPrint is boolean;
        }
    }
    {
        // ---- 1. Zero points and chains -------------------------------------------------
        const zeroPoints = resolveZeroPoints(context, definition);
        const fromResolved = resolveChain(context, definition.fromEdges, zeroPoints.from, definition.projectionMode, false);
        const fromChain = fromResolved.chain;

        // TO_EDGES works on an independent copy of the to-edges from the start, so every
        // arc located below refers to edges of the body that is actually split and trimmed.
        var toSelection = definition.toEdges;
        if (definition.mapMode == MapMode.TO_EDGES)
        {
            opExtractWires(context, id + "copy", { "edges" : expandEdgeQuery(definition.toEdges) });
            toSelection = qOwnedByBody(qCreatedBy(id + "copy", EntityType.BODY), EntityType.EDGE);
        }
        const toResolved = resolveChain(context, toSelection, zeroPoints.to, definition.projectionMode, definition.flipTo);
        const toChain = toResolved.chain;

        reportProjectionFallback(context, id, fromResolved, "from");
        reportProjectionFallback(context, id, toResolved, "to");

        // ---- 2. Direction check ---------------------------------------------------------
        const fromZeroFrame = frameAtArc(context, fromChain, [fromChain.zeroArc])[0];
        const toZeroFrame = frameAtArc(context, toChain, [toChain.zeroArc])[0];
        if (dot(fromZeroFrame.tangent, toZeroFrame.tangent) < 0)
        {
            throw regenError("The from and to chains point in opposite directions at the reference point. Tick Flip to-chain.",
                ["flipTo"], qUnion([expandEdgeQuery(definition.fromEdges), expandEdgeQuery(definition.toEdges)]));
        }

        // ---- 3. Span: from-chain ends relative to its zero, placed on the to-chain -------
        const s0 = -fromChain.zeroArc;
        const s1 = fromChain.totalLength - fromChain.zeroArc;
        const t0 = toChain.zeroArc + s0;
        const t1 = toChain.zeroArc + s1;
        checkSpan(toChain, t0, t1);

        // ---- 4. Mode -------------------------------------------------------------------
        var summary = { "stations" : 0, "runs" : [], "wires" : 0 };
        if (definition.mapMode == MapMode.TO_EDGES)
        {
            summary.wires = trimToEdges(context, id, definition, toResolved, t0, t1);
        }
        else
        {
            const stations = chainStations(context, fromChain, spacingSettings(definition));
            const samples = resolveSamples(context, toChain, mapSamples(toChain, stations));
            const runs = buildRuns(samples);
            if (size(runs) == 0)
            {
                throw regenError("The from-edges map to no span on the to-edges.", ["fromEdges"]);
            }
            const emitted = emitRuns(context, id, definition, samples, runs);
            summary.stations = size(stations);
            summary.runs = emitted;

            if (definition.mapMode == MapMode.SINGLE)
            {
                summary.wires = mergeEmitted(context, id, definition, emitted);
            }
            else
            {
                summary.wires = wireEmitted(context, id, definition, emitted);
            }
        }

        // ---- 5. Debug ------------------------------------------------------------------
        if (definition.debugShowChainEnds)
        {
            drawChainArrows(context, fromChain, fromZeroFrame);
            drawChainArrows(context, toChain, toZeroFrame);
        }
        if (definition.debugPrint)
        {
            printSummary(definition, fromResolved, toResolved, s0, s1, summary);
        }

        // Ends: start = the end of each mapped wire nearer the to-chain's start.
        var mappedChains = [];
        for (var body in evaluateQuery(context, qBodyType(qCreatedBy(id, EntityType.BODY), BodyType.WIRE)))
        {
            mappedChains = append(mappedChains, qOwnedByBody(body, EntityType.EDGE));
        }
        const ends = wireEnds(context, mappedChains, toChain.links[0].edges[0].startPoint);

        embedStandardOutputs(context, id, {
                    "output" : qCreatedBy(id, EntityType.BODY),
                    "outputDescription" : "The mapped wires",
                    "inputs" : qUnion([definition.fromEdges, definition.toEdges]),
                    "variables" : {
                        "spanStart" : extractableVariable(s0, "Start of the from-chain, measured from its reference point."),
                        "spanEnd" : extractableVariable(s1, "End of the from-chain, measured from its reference point."),
                        "toStart" : extractableVariable(t0, "Where the span starts on the to-chain (arc length from its start)."),
                        "toEnd" : extractableVariable(t1, "Where the span ends on the to-chain (arc length from its start)."),
                        "stationCount" : extractableVariable(summary.stations, "Stations sampled along the from-chain (0 for Trim to edges).")
                    },
                    "queries" : {
                        "startVertex" : extractableQuery(ends.startVertex, "The end of the wire where its source starts.", DebugColor.GREEN),
                        "endVertex" : extractableQuery(ends.endVertex, "The other end of the wire.", DebugColor.RED),
                        "startEdge" : extractableQuery(ends.startEdge, "The edge at startVertex.", DebugColor.GREEN),
                        "endEdge" : extractableQuery(ends.endEdge, "The edge at endVertex.", DebugColor.RED)
                    }
                });
    }, {
        // Only for callers that omit parameters (API, other features); the dialog sets all.
        "mapMode" : MapMode.FROM_EDGES,
        "refPointMode" : RefPointMode.SHARED,
        "offsetRefPoint" : qNothing(),
        "fromRefPoint" : qNothing(),
        "toRefPoint" : qNothing(),
        "projectionMode" : ProjectionMode.WORLD_X,
        "flipTo" : false,
        "edgeOffsetSpacingDef" : OffsetPointSpacing.CTRL_POINT,
        "ctrlPointMultiplier" : 3,
        "pointsPerEdge" : 25,
        "targetPointSpacing" : 10 * millimeter,
        "approximationDegree" : 3,
        "approximationTolerance" : 1e-5 * meter,
        "approximationMaxCPs" : 15,
        "wireOutput" : WireOutput.PER_LINK,
        "outputName" : "",
        "debugShowChainEnds" : false,
        "debugPrint" : false
    });

// ============================================================================
// Input settings
// ============================================================================

// TODO: shared helper (research_shared_vocabulary.md s3/s4) -- copied from driven_edge_offset.fs:113-130.
/** The spacing group: how many points to evaluate along each from-edge. */
predicate offsetSpacingPredicate(definition is map)
{
    annotation { "Name" : "Offset spacing", "Description" : "How many points to evaluate along each from-edge", "UIHint" : UIHint.HORIZONTAL_ENUM, "Default" : OffsetPointSpacing.CTRL_POINT }
    definition.edgeOffsetSpacingDef is OffsetPointSpacing;

    if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.CTRL_POINT)
    {
        annotation { "Name" : "Control point multiplier" }
        isInteger(definition.ctrlPointMultiplier, CtrlPointMultiplierBounds);
    }
    if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.NUM_POINTS)
    {
        annotation { "Name" : "Points per edge" }
        isInteger(definition.pointsPerEdge, PointsPerEdgeBounds);
    }
    if (definition.edgeOffsetSpacingDef == OffsetPointSpacing.DISTANCE_ALONG)
    {
        annotation { "Name" : "Point spacing" }
        isLength(definition.targetPointSpacing, PointSpacingBounds);
    }
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- copied from driven_edge_offset.fs:253.
/** The approximation controls: only freeform runs reach the solver; lines and arcs are exact. */
predicate offsetApproximationPredicate(definition is map)
{
    annotation { "Name" : "Target degree", "Description" : "Degree the fit aims for on freeform runs" }
    isInteger(definition.approximationDegree, DEGREE_BOUND);

    annotation { "Name" : "Tolerance", "Description" : "How far a fitted run may sit from the mapped points" }
    isLength(definition.approximationTolerance, TOLERANCE_BOUND);

    annotation { "Name" : "Maximum control points", "Description" : "Cap on a fitted run. The fit stops as soon as tolerance is met, so this only binds on a run that cannot reach it." }
    isInteger(definition.approximationMaxCPs, OffsetMaxCPBounds);
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- copied from driven_edge_offset.fs:269.
/** Spacing fields gathered into one map; only the field matching the chosen mode is read. */
function spacingSettings(definition is map) returns map
{
    return {
        "mode" : definition.edgeOffsetSpacingDef,
        "pointsPerEdge" : definition.pointsPerEdge,
        "targetSpacing" : definition.targetPointSpacing,
        "ctrlPointMultiplier" : definition.ctrlPointMultiplier
    };
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- copied from driven_edge_offset.fs:286.
/** Fitting settings for freeform output, in the shape emitSplineCurve reads. */
function approximationSettings(definition is map) returns map
{
    return {
        "approximationDegree" : definition.approximationDegree,
        "approximationTolerance" : definition.approximationTolerance,
        "approximationMaxCPs" : definition.approximationMaxCPs
    };
}

// ============================================================================
// Chains
// ============================================================================

/**
 * The zero point of each chain as a world position.
 *
 * @returns {map} : { "from" : Vector, "to" : Vector }
 */
function resolveZeroPoints(context is Context, definition is map) returns map
{
    if (definition.refPointMode == RefPointMode.SHARED)
    {
        const shared = evZeroPoint(context, definition.offsetRefPoint);
        return { "from" : shared, "to" : shared };
    }

    return {
        "from" : evZeroPoint(context, definition.fromRefPoint),
        "to" : evZeroPoint(context, definition.toRefPoint)
    };
}

// TODO: shared helper (research_shared_vocabulary.md s4 chainZeroArcAtPoint).
/**
 * Build a chain and anchor its zero station by the chosen projection.
 *
 * buildChain always resolves its zero by world X (arcLengthAtX), and throws when no edge
 * spans that X. When world X is wanted AND the chain's vertices span the point's X (one
 * evBox3d over the vertices), the real point goes in. Otherwise the chain is built at an
 * anchor it spans by construction -- the X midpoint of one edge's own endpoints (a second,
 * one-edge evBox3d) -- and the zero arc is re-anchored by closest point: one evDistance
 * against the chain's edges.
 *
 * On a single link "spanned" is exact: consecutive edge endpoints walk from the lowest vertex X
 * to the highest, so any X strictly between is straddled by some edge. On a multi-link chain
 * an X in the gap between links still reaches arcLengthAtX's own error.
 *
 * @returns {map} : { "chain", "projection" : "world X" | "closest point", "fallback" : boolean,
 *                    "anchor" : Vector (a point buildChain accepts for these edges, or any
 *                    split of them) }
 */
function resolveChain(context is Context, selection is Query, zeroPoint is Vector, mode is ProjectionMode, flip is boolean) returns map
{
    const edges = expandEdgeQuery(selection);
    if (isQueryEmpty(context, edges))
    {
        throw regenError("No edges found in the selection.", selection);
    }

    // spanned feeds useX and fallback, and both are already gated on WORLD_X -- so in
    // CLOSEST_POINT this box, and the adjacency query behind it, were evaluated and then
    // discarded. The !useX branch below evaluates its own box anyway, so that mode was
    // paying for two where it needs one.
    // UNVERIFIED in Onshape: evBox3d over a vertex-only query.
    var spanned = false;
    if (mode == ProjectionMode.WORLD_X)
    {
        const vertexBox = evBox3d(context, {
                    "topology" : qAdjacent(edges, AdjacencyType.VERTEX, EntityType.VERTEX),
                    "tight" : true
                });
        spanned = zeroPoint[0] > vertexBox.minCorner[0] + SPAN_X_MARGIN
            && zeroPoint[0] < vertexBox.maxCorner[0] - SPAN_X_MARGIN;
    }
    const useX = (mode == ProjectionMode.WORLD_X) && spanned;

    var anchor = zeroPoint;
    if (!useX)
    {
        // The midpoint in X of one edge's endpoints is straddled by that edge (or by a piece
        // of it after a split), so arcLengthAtX cannot miss. Floating noise cannot break it:
        // a vertical edge gives x0 == x1 == anchor exactly.
        const seedBox = evBox3d(context, {
                    "topology" : qAdjacent(qNthElement(edges, 0), AdjacencyType.VERTEX, EntityType.VERTEX),
                    "tight" : true
                });
        anchor = 0.5 * (seedBox.minCorner + seedBox.maxCorner);
    }

    var chain = buildChain(context, selection, anchor);
    if (flip)
    {
        chain = reverseChain(chain);
    }
    if (!useX)
    {
        chain.zeroArc = closestArc(context, chain, zeroPoint);
    }

    return {
        "chain" : chain,
        "projection" : useX ? "world X" : "closest point",
        "fallback" : (mode == ProjectionMode.WORLD_X) && !spanned,
        "anchor" : anchor
    };
}

/**
 * Tell the user when world X was asked for but could not be used.
 */
function reportProjectionFallback(context is Context, id is Id, resolved is map, label is string)
{
    if (resolved.fallback)
    {
        reportFeatureInfo(context, id, "The " ~ label ~ "-edges do not span the zero point's X; its closest point on the chain was used instead.");
    }
}

/**
 * Traverse a chain the other way: links in reverse order, every edge reversed with its
 * traversal sense toggled and endpoints swapped, arc lengths restated from the new start.
 * The zero station is the same place on the curve, so its arc mirrors: total - zeroArc.
 */
function reverseChain(chain is map) returns map
{
    var links = [];
    var runningArc = 0 * meter;

    for (var i = size(chain.links) - 1; i >= 0; i -= 1)
    {
        const link = chain.links[i];
        var edges = makeArray(size(link.edges));
        var edgeArc = runningArc;

        for (var j = 0; j < size(link.edges); j += 1)
        {
            const source = link.edges[size(link.edges) - 1 - j];
            edges[j] = mergeMaps(source, {
                        "flipped" : !source.flipped,
                        "startPoint" : source.endPoint,
                        "endPoint" : source.startPoint,
                        "startArc" : edgeArc
                    });
            edgeArc += source.length;
        }

        links = append(links, mergeMaps(link, {
                        "path" : reverse(link.path),
                        "edges" : edges,
                        "startArc" : runningArc
                    }));
        runningArc += link.length;
    }

    return {
        "links" : links,
        "totalLength" : chain.totalLength,
        "zeroArc" : chain.totalLength - chain.zeroArc
    };
}

/**
 * Arc length, from the chain start, of the point on the chain closest to a world position.
 * One evDistance over all the chain's edges; the side-1 index is into that edge list in
 * chain order (qUnion preserves order), and its parameter is arc-length fraction along the
 * edge's own direction, so the chain's traversal sense is applied as edgeParam's inverse.
 */
function closestArc(context is Context, chain is map, point is Vector) returns ValueWithUnits
{
    var flat = [];
    var queries = [];
    for (var link in chain.links)
    {
        for (var edgeData in link.edges)
        {
            flat = append(flat, edgeData);
            queries = append(queries, edgeData.query);
        }
    }

    const result = evDistance(context, { "side0" : point, "side1" : qUnion(queries) });
    const edgeData = flat[result.sides[1].index];
    const p = result.sides[1].parameter;

    return edgeData.startArc + (edgeData.flipped ? 1 - p : p) * edgeData.length;
}

/**
 * Error unless [t0, t1] lies within the to-chain, naming the overrun.
 */
function checkSpan(toChain is map, t0 is ValueWithUnits, t1 is ValueWithUnits)
{
    const startOverrun = -t0;
    const endOverrun = t1 - toChain.totalLength;

    if (startOverrun > SPAN_TOLERANCE)
    {
        throw regenError("The from-edges run " ~ roundToPrecision(startOverrun / millimeter, 3)
            ~ " mm past the START of the to-edges, measured from the reference point. Shorten the from-edges, extend the to-edges, or move the reference point.",
            ["fromEdges", "toEdges"]);
    }
    if (endOverrun > SPAN_TOLERANCE)
    {
        throw regenError("The from-edges run " ~ roundToPrecision(endOverrun / millimeter, 3)
            ~ " mm past the END of the to-edges, measured from the reference point. Shorten the from-edges, extend the to-edges, or move the reference point.",
            ["fromEdges", "toEdges"]);
    }
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- driven_edge_offset.fs:736 edgeAtArc
// takes an arc measured from the zero point; this one takes an absolute arc from the chain
// start and also reports the link and edge indices.
/**
 * The chain edge containing an absolute arc length, and how far along it that arc falls.
 *
 * @returns {map} : { "edgeData", "linkIndex", "edgeIndex", "fraction" }
 */
function edgeAtArc(chain is map, absoluteArc is ValueWithUnits) returns map
{
    const arc = clamp(absoluteArc, 0 * meter, chain.totalLength);
    var last = undefined;

    for (var linkIndex = 0; linkIndex < size(chain.links); linkIndex += 1)
    {
        const link = chain.links[linkIndex];
        for (var edgeIndex = 0; edgeIndex < size(link.edges); edgeIndex += 1)
        {
            const edgeData = link.edges[edgeIndex];
            last = { "edgeData" : edgeData, "linkIndex" : linkIndex, "edgeIndex" : edgeIndex, "fraction" : 1 };

            if (arc >= edgeData.startArc && arc <= edgeData.startArc + edgeData.length)
            {
                last.fraction = clamp((arc - edgeData.startArc) / edgeData.length, 0, 1);
                return last;
            }
        }
    }

    return last;
}

/**
 * Every vertex between two consecutive edges of one link, as an absolute arc, in chain order.
 *
 * @returns {array} : each { "arc", "linkIndex", "edgeIndex" (the edge that STARTS at the vertex) }
 */
function chainVertices(chain is map) returns array
{
    var vertices = [];
    for (var linkIndex = 0; linkIndex < size(chain.links); linkIndex += 1)
    {
        const link = chain.links[linkIndex];
        for (var edgeIndex = 1; edgeIndex < size(link.edges); edgeIndex += 1)
        {
            vertices = append(vertices, {
                        "arc" : link.edges[edgeIndex].startArc,
                        "linkIndex" : linkIndex,
                        "edgeIndex" : edgeIndex
                    });
        }
    }
    return vertices;
}

// ============================================================================
// Frames
// ============================================================================

// TODO: shared helper (research_shared_vocabulary.md s4 frameAtArc).
/**
 * Origin and tangent at any number of absolute arcs on a chain, batched: the arcs are
 * located on their host edges, then ONE evEdgeTangentLines per host edge.
 *
 * @returns {array} : aligned with arcs, each { "origin", "tangent" } with the tangent in the
 *          chain's traversal sense.
 */
function frameAtArc(context is Context, chain is map, arcs is array) returns array
{
    var located = [];
    for (var arc in arcs)
    {
        located = append(located, edgeAtArc(chain, arc));
    }

    return evaluateLocated(context, chain, located);
}

/**
 * Origin and tangent for located records { linkIndex, edgeIndex, fraction }, one kernel call
 * per distinct host edge.
 */
function evaluateLocated(context is Context, chain is map, located is array) returns array
{
    var groups = {};
    var order = [];

    for (var i = 0; i < size(located); i += 1)
    {
        const record = located[i];
        const key = "l" ~ record.linkIndex ~ "e" ~ record.edgeIndex;
        const edgeData = chain.links[record.linkIndex].edges[record.edgeIndex];

        var group = groups[key];
        if (group == undefined)
        {
            group = { "edgeData" : edgeData, "indices" : [], "parameters" : [] };
            order = append(order, key);
        }
        group.indices = append(group.indices, i);
        group.parameters = append(group.parameters, edgeParam(edgeData, record.fraction));
        groups[key] = group;
    }

    var frames = makeArray(size(located));
    for (var key in order)
    {
        const group = groups[key];
        const lines = evEdgeTangentLines(context, {
                    "edge" : group.edgeData.query,
                    "parameters" : group.parameters
                });

        for (var j = 0; j < size(lines); j += 1)
        {
            frames[group.indices[j]] = {
                    "origin" : lines[j].origin,
                    "tangent" : group.edgeData.flipped ? -1 * lines[j].direction : lines[j].direction,
                    // The host edge's type, for the shape gates in emitRuns.
                    "curveType" : group.edgeData.curveType
                };
        }
    }

    return frames;
}

// ============================================================================
// Samples and runs (FROM_EDGES / SINGLE)
// ============================================================================

/**
 * Place every from-station on the to-chain, as a located record, in chain order.
 *
 * A sample carries where it sits on the to-chain (linkIndex, edgeIndex, fraction, absolute
 * arc), which from-link it came from, and whether a run must break before it. Breaks come
 * from the from-chain here: a link change, or an edge change that chainStations did not weld
 * (a G0 corner). Across a welded (G1) junction the right-hand half duplicates the left and
 * is dropped.
 *
 * To-chain vertices inside the span are inserted as an explicit pair of samples (end of the
 * edge before, start of the edge after), so a G0 corner on the to-chain is hit exactly by
 * both neighbouring runs rather than approached from either side. Which of the two it is --
 * a corner that breaks the run, or a tangent junction that does not -- is decided once the
 * tangents are known, in resolveSamples.
 */
function mapSamples(toChain is map, stations is array) returns array
{
    const vertices = chainVertices(toChain);
    const last = size(stations) - 1;
    var samples = [];
    var nextVertex = 0;

    for (var i = 0; i <= last; i += 1)
    {
        const station = stations[i];
        const arc = toChain.zeroArc + station.arc;

        var breakBefore = false;
        if (i > 0)
        {
            const previous = stations[i - 1];
            const newEdge = station.linkIndex != previous.linkIndex || station.edgeIndex != previous.edgeIndex;
            if (newEdge && station.welded == true)
            {
                continue;
            }
            breakBefore = newEdge;
        }

        // To-vertices passed since the previous sample. One before the first sample lies
        // outside the span and is skipped.
        while (nextVertex < size(vertices) && vertices[nextVertex].arc < arc - OFFSET_GEOM_TOL)
        {
            if (size(samples) > 0)
            {
                samples = concatenateArrays([samples,
                        vertexSamples(vertices[nextVertex], station.linkIndex, false, true, true)]);
            }
            nextVertex += 1;
        }

        // A station landing on a to-vertex becomes the vertex pair itself, keeping its
        // break flag. At the span's own ends only the inside half exists.
        if (nextVertex < size(vertices) && abs(vertices[nextVertex].arc - arc) <= OFFSET_GEOM_TOL)
        {
            samples = concatenateArrays([samples,
                    vertexSamples(vertices[nextVertex], station.linkIndex, breakBefore, i > 0, i < last)]);
            nextVertex += 1;
            continue;
        }

        const located = edgeAtArc(toChain, arc);
        samples = append(samples, {
                    "arc" : arc,
                    "linkIndex" : located.linkIndex,
                    "edgeIndex" : located.edgeIndex,
                    "fraction" : located.fraction,
                    "fromLink" : station.linkIndex,
                    "breakBefore" : breakBefore,
                    "vertexSide" : undefined
                });
    }

    return samples;
}

/**
 * The two halves of a to-chain vertex as samples: the end of the edge before it and the
 * start of the edge after it.
 */
function vertexSamples(vertex is map, fromLink is number, breakBefore is boolean, includeLeft is boolean, includeRight is boolean) returns array
{
    var pair = [];
    if (includeLeft)
    {
        pair = append(pair, {
                    "arc" : vertex.arc,
                    "linkIndex" : vertex.linkIndex,
                    "edgeIndex" : vertex.edgeIndex - 1,
                    "fraction" : 1,
                    "fromLink" : fromLink,
                    "breakBefore" : breakBefore,
                    "vertexSide" : "left"
                });
    }
    if (includeRight)
    {
        pair = append(pair, {
                    "arc" : vertex.arc,
                    "linkIndex" : vertex.linkIndex,
                    "edgeIndex" : vertex.edgeIndex,
                    "fraction" : 0,
                    "fromLink" : fromLink,
                    "breakBefore" : includeLeft ? false : breakBefore,
                    "vertexSide" : "right"
                });
    }
    return pair;
}

/**
 * Evaluate the samples' frames on the to-chain (batched per host edge) and settle the
 * to-chain breaks: a vertex pair whose tangents agree within G1_JUNCTION_ANGLE collapses
 * to its left half; one that does not is a corner and breaks the run. A to-link change is
 * a gap and always breaks. Coincident samples on one edge collapse to the first.
 *
 * @returns {array} : samples with "origin" and "tangent" merged in.
 */
function resolveSamples(context is Context, toChain is map, samples is array) returns array
{
    const frames = evaluateLocated(context, toChain, samples);
    var resolved = [];

    for (var i = 0; i < size(samples); i += 1)
    {
        var sample = mergeMaps(samples[i], frames[i]);

        if (size(resolved) > 0)
        {
            const previous = resolved[size(resolved) - 1];
            const coincident = abs(sample.arc - previous.arc) <= OFFSET_GEOM_TOL;

            if (sample.vertexSide == "right" && previous.vertexSide == "left" && coincident)
            {
                const breakAngle = angleBetween(previous.tangent, sample.tangent) / radian;
                if (breakAngle <= G1_JUNCTION_ANGLE)
                {
                    continue;
                }
                sample.breakBefore = true;
            }
            else if (sample.linkIndex != previous.linkIndex)
            {
                sample.breakBefore = true;
            }
            else if (coincident && !sample.breakBefore && sample.edgeIndex == previous.edgeIndex)
            {
                continue;
            }
        }

        resolved = append(resolved, sample);
    }

    return resolved;
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- driven_edge_offset.fs:536 buildRuns,
// adapted: the break decisions are already on the samples.
/**
 * Group samples into runs at the settled breaks.
 *
 * @returns {array} : each { "start", "end", "fromLink" }, inclusive sample indices.
 */
function buildRuns(samples is array) returns array
{
    var runs = [];
    var start = 0;

    for (var i = 1; i < size(samples); i += 1)
    {
        if (samples[i].breakBefore)
        {
            runs = closeRun(runs, samples, start, i - 1);
            start = i;
        }
    }

    return closeRun(runs, samples, start, size(samples) - 1);
}

/**
 * Append a run if it holds at least two samples and spans a real distance. A run can
 * collapse to a point where two breaks coincide; the neighbouring runs cover the position.
 */
function closeRun(runs is array, samples is array, start is number, end is number) returns array
{
    if (end - start < 1)
    {
        return runs;
    }

    var span = 0 * meter;
    for (var i = start + 1; i <= end; i += 1)
    {
        span += norm(samples[i].origin - samples[i - 1].origin);
    }
    if (span < OFFSET_GEOM_TOL)
    {
        return runs;
    }

    return append(runs, { "start" : start, "end" : end, "fromLink" : samples[start].fromLink });
}

// ============================================================================
// Emit
// ============================================================================

/**
 * One curve per run: a degree-one curve for a straight run, a sketched arc for a circular
 * one (so it carries a real radius), a fitted spline otherwise with the to-chain's exact
 * tangents at both ends.
 *
 * Every sample lies ON the to-chain, so the output's shape is the to-chain's. A line or an
 * arc is only on the table where every to-edge under the run is one (until 2026-09-25 there
 * were no gates, and a mapped spline that happened to sit within tolerance of a circle came
 * out as an arc with kinked ends). A run on arcs only is the arcs themselves, so it is emitted
 * exact; anything else is shaped by shapeRuns, which pins every spline end to the tangent the
 * neighbouring piece actually has (reviews/2026-09-25_arc_line_fitting).
 *
 * @returns {array} : each { "start", "end", "fromLink", "kind", "radius", "edges" : Query }
 */
function emitRuns(context is Context, id is Id, definition is map, samples is array, runs is array) returns array
{
    const approximation = approximationSettings(definition);
    var emitted = [];
    var items = [];
    var runPoints = [];

    for (var r = 0; r < size(runs); r += 1)
    {
        const run = runs[r];

        var points = [];
        var allowLine = true;
        var allowArc = true;
        var allCircles = true;
        for (var i = run.start; i <= run.end; i += 1)
        {
            points = append(points, samples[i].origin);

            const hostType = samples[i].curveType;
            allowLine = allowLine && hostType == CurveType.LINE;
            allowArc = allowArc && (hostType == CurveType.LINE || hostType == CurveType.CIRCLE);
            allCircles = allCircles && hostType == CurveType.CIRCLE;
        }
        runPoints = append(runPoints, points);

        const previous = (r > 0) ? runs[r - 1] : undefined;
        items = append(items, {
                    "points" : points,
                    "startTangent" : samples[run.start].tangent,
                    "endTangent" : samples[run.end].tangent,
                    "allowArc" : allowArc,
                    "allowLine" : allowLine,
                    "exactArc" : allCircles,
                    "joinsPrevious" : previous != undefined && previous.fromLink == run.fromLink
                        && norm(samples[run.start].origin - samples[previous.end].origin) < OFFSET_GEOM_TOL
                });
    }

    const shapes = shapeRuns(items, { "tolerance" : approximation.approximationTolerance });

    for (var r = 0; r < size(runs); r += 1)
    {
        const run = runs[r];
        const runId = id + ("run" ~ r);
        const shape = shapes[r];

        emitRunShape(context, runId, shape, runPoints[r], approximation);
        if (shape.note != undefined)
        {
            println("Map curve run " ~ r ~ " -> " ~ shape.kind ~ ": " ~ shape.note);
        }

        emitted = append(emitted, mergeMaps(run, {
                        "kind" : shape.kind,
                        "radius" : (shape.kind == "arc") ? shape.radius : undefined,
                        "edges" : qCreatedBy(runId, EntityType.EDGE)
                    }));
    }

    return emitted;
}

/**
 * The edges of every emitted run, as one query.
 */
function emittedEdges(emitted is array) returns Query
{
    var edges = [];
    for (var run in emitted)
    {
        edges = append(edges, run.edges);
    }
    return qUnion(edges);
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- driven_edge_offset.fs:891-916.
/**
 * FROM_EDGES output: one wire per from-link (the extracted wires are independent copies, so
 * the run curves are deleted), or every run left as its own body.
 *
 * @returns {number} : bodies produced.
 */
function wireEmitted(context is Context, id is Id, definition is map, emitted is array) returns number
{
    if (definition.wireOutput == WireOutput.PER_RUN)
    {
        nameOutput(context, qOwnerBody(emittedEdges(emitted)), definition.outputName);
        return size(emitted);
    }

    var edgesByLink = {};
    var linkOrder = [];
    for (var run in emitted)
    {
        const key = "link" ~ run.fromLink;
        if (edgesByLink[key] == undefined)
        {
            edgesByLink[key] = [];
            linkOrder = append(linkOrder, run.fromLink);
        }
        edgesByLink[key] = append(edgesByLink[key], run.edges);
    }

    var wires = [];
    for (var linkIndex in linkOrder)
    {
        const wireId = id + ("wire" ~ linkIndex);
        opExtractWires(context, wireId, { "edges" : qUnion(edgesByLink["link" ~ linkIndex]) });
        wires = append(wires, qCreatedBy(wireId, EntityType.BODY));
    }

    opDeleteBodies(context, id + "cleanup", { "entities" : qOwnerBody(emittedEdges(emitted)) });
    nameOutput(context, qUnion(wires), definition.outputName);

    return size(wires);
}

/**
 * SINGLE output: merge every run into one spline through the merge_curve core, then drop the
 * run curves. A lone run is already one body and is kept as emitted, radius and all.
 *
 * @returns {number} : bodies produced (always 1).
 */
function mergeEmitted(context is Context, id is Id, definition is map, emitted is array) returns number
{
    if (size(emitted) == 1)
    {
        nameOutput(context, qOwnerBody(emitted[0].edges), definition.outputName);
        return 1;
    }

    var hadArc = false;
    for (var run in emitted)
    {
        if (run.kind == "arc")
        {
            hadArc = true;
        }
    }

    mergeCurves(context, id + "merged", emittedEdges(emitted), {
                "degree" : definition.approximationDegree,
                "tolerance" : definition.approximationTolerance,
                "maxControlPoints" : definition.approximationMaxCPs,
                "keepStartDerivative" : true,
                "keepEndDerivative" : true,
                "debugPrint" : definition.debugPrint
            });

    opDeleteBodies(context, id + "cleanup", { "entities" : qOwnerBody(emittedEdges(emitted)) });
    nameOutput(context, qCreatedBy(id + "merged", EntityType.BODY), definition.outputName);

    if (hadArc)
    {
        reportFeatureInfo(context, id, "Merged result is a spline; the arc radius of a circular run is not preserved.");
    }

    return 1;
}

/**
 * TO_EDGES output: split the copy of the to-edges at the span ends, keep the pieces inside
 * the span, extract them into a fresh wire and delete the copy. Always one body.
 *
 * The copy chain's edges are the copy's own, so the split parameters land on the body being
 * cut. After the split the copy is described again as a chain: the pieces are then edges
 * with their own arc ranges, and a piece is kept when its midpoint arc lies in [t0, t1]. The
 * arc parameterisation is unchanged by splitting (same geometry, same X orientation, same
 * link order), so t0 and t1 carry over as they are.
 *
 * UNVERIFIED in Onshape: opSplitEdges on the edges of a wire body (std's only caller is sheet
 * metal); the parameter sense on an edge traversed against its own direction (edgeParam
 * handles it if the parameter is arc-length from the edge's own start, as documented).
 *
 * @returns {number} : bodies produced (always 1).
 */
function trimToEdges(context is Context, id is Id, definition is map, toResolved is map, t0 is ValueWithUnits, t1 is ValueWithUnits) returns number
{
    const toChain = toResolved.chain;
    const copyBody = qCreatedBy(id + "copy", EntityType.BODY);

    const at0 = edgeAtArc(toChain, t0);
    const at1 = edgeAtArc(toChain, t1);
    const cut0 = at0.fraction > SPLIT_END_FRACTION && at0.fraction < 1 - SPLIT_END_FRACTION;
    const cut1 = at1.fraction > SPLIT_END_FRACTION && at1.fraction < 1 - SPLIT_END_FRACTION;
    const sameEdge = at0.linkIndex == at1.linkIndex && at0.edgeIndex == at1.edgeIndex;

    var splitEdges = [];
    var splitParameters = [];
    if (cut0 && cut1 && sameEdge)
    {
        const p0 = edgeParam(at0.edgeData, at0.fraction);
        const p1 = edgeParam(at1.edgeData, at1.fraction);
        splitEdges = [at0.edgeData.query];
        splitParameters = [[min(p0, p1), max(p0, p1)]];
    }
    else
    {
        if (cut0)
        {
            splitEdges = append(splitEdges, at0.edgeData.query);
            splitParameters = append(splitParameters, [edgeParam(at0.edgeData, at0.fraction)]);
        }
        if (cut1)
        {
            splitEdges = append(splitEdges, at1.edgeData.query);
            splitParameters = append(splitParameters, [edgeParam(at1.edgeData, at1.fraction)]);
        }
    }

    if (size(splitEdges) > 0)
    {
        opSplitEdges(context, id + "split", {
                    "edges" : qUnion(splitEdges),
                    "parameters" : splitParameters,
                    "arcLengthParameterization" : true
                });
    }

    // Re-describe the cut copy. Its zero station is irrelevant here (t0, t1 are absolute),
    // so it is built at the anchor the original was (still spanned after the split), then
    // flipped like the original.
    var pieces = buildChain(context, qOwnedByBody(copyBody, EntityType.EDGE), toResolved.anchor);
    if (definition.flipTo)
    {
        pieces = reverseChain(pieces);
    }

    var kept = [];
    for (var link in pieces.links)
    {
        for (var edgeData in link.edges)
        {
            const midArc = edgeData.startArc + 0.5 * edgeData.length;
            if (midArc >= t0 - SPAN_TOLERANCE && midArc <= t1 + SPAN_TOLERANCE)
            {
                kept = append(kept, edgeData.query);
            }
        }
    }

    if (size(kept) == 0)
    {
        throw regenError("No part of the to-edges lies within the from span.", ["fromEdges", "toEdges"]);
    }

    opExtractWires(context, id + "trim", { "edges" : qUnion(kept) });
    opDeleteBodies(context, id + "deleteCopy", { "entities" : copyBody });
    nameOutput(context, qCreatedBy(id + "trim", EntityType.BODY), definition.outputName);

    return 1;
}

// TODO: shared helper (research_shared_vocabulary.md s3) -- copied from driven_edge_offset.fs:919.
/**
 * Name the bodies this feature produced. An empty name is a deliberate choice: setProperty
 * would happily write "" and leave the bodies looking unnamed but shadowed, so skip it.
 */
function nameOutput(context is Context, bodies is Query, name is string)
{
    if (name == "")
    {
        return;
    }

    setProperty(context, {
                "entities" : bodies,
                "propertyType" : PropertyType.NAME,
                "value" : name
            });
}

// ============================================================================
// Debug
// ============================================================================

// TODO: shared helper (research_shared_vocabulary.md s3) -- offset_debug.fs:213 drawChainEnds, minimal.
/**
 * Three arrows per chain, each pointing the way the chain is traversed: green at the start,
 * red at the end, blue at the zero station. Two kernel calls for the end frames; the zero
 * frame is the one the direction check already evaluated.
 */
function drawChainArrows(context is Context, chain is map, zeroFrame is map)
{
    const ends = frameAtArc(context, chain, [0 * meter, chain.totalLength]);

    addDebugArrow(context, ends[0].origin, ends[0].origin + DEBUG_END_ARROW * ends[0].tangent,
        DEBUG_END_ARROW_RADIUS, DebugColor.GREEN);
    addDebugArrow(context, ends[1].origin, ends[1].origin + DEBUG_END_ARROW * ends[1].tangent,
        DEBUG_END_ARROW_RADIUS, DebugColor.RED);
    addDebugArrow(context, zeroFrame.origin, zeroFrame.origin + DEBUG_END_ARROW * zeroFrame.tangent,
        DEBUG_END_ARROW_RADIUS, DebugColor.BLUE);
}

/**
 * One summary block. No per-station output.
 */
function printSummary(definition is map, fromResolved is map, toResolved is map, s0 is ValueWithUnits, s1 is ValueWithUnits, summary is map)
{
    const fromChain = fromResolved.chain;
    const toChain = toResolved.chain;

    println("=== Map curve: " ~ toString(definition.mapMode) ~ " ===");
    println("from chain: " ~ size(fromChain.links) ~ " link(s), " ~ chainEdgeCount(fromChain) ~ " edge(s), length "
        ~ fmtMM(fromChain.totalLength, 3, 0) ~ " mm, zero arc " ~ fmtMM(fromChain.zeroArc, 3, 0)
        ~ " mm (" ~ fromResolved.projection ~ ")");
    println("to chain:   " ~ size(toChain.links) ~ " link(s), " ~ chainEdgeCount(toChain) ~ " edge(s), length "
        ~ fmtMM(toChain.totalLength, 3, 0) ~ " mm, zero arc " ~ fmtMM(toChain.zeroArc, 3, 0)
        ~ " mm (" ~ toResolved.projection ~ ")" ~ (definition.flipTo ? ", flipped" : ""));
    println("from span:  [" ~ fmtMM(s0, 3, 0) ~ ", " ~ fmtMM(s1, 3, 0) ~ "] mm from the zero point -> to-chain arcs ["
        ~ fmtMM(toChain.zeroArc + s0, 3, 0) ~ ", " ~ fmtMM(toChain.zeroArc + s1, 3, 0) ~ "] mm");

    if (definition.mapMode != MapMode.TO_EDGES)
    {
        var kinds = "";
        for (var run in summary.runs)
        {
            kinds = kinds ~ (kinds == "" ? "" : ", ") ~ run.kind
                ~ (run.radius == undefined ? "" : " r=" ~ fmtMM(run.radius, 3, 0));
        }
        println("stations:   " ~ summary.stations ~ ", runs: " ~ size(summary.runs) ~ " [" ~ kinds ~ "]");
    }
    println("bodies out: " ~ summary.wires);
}

/**
 * Total edges across a chain's links.
 */
function chainEdgeCount(chain is map) returns number
{
    var count = 0;
    for (var link in chain.links)
    {
        count += size(link.edges);
    }
    return count;
}
