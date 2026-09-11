FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Merge curve
 *
 * Takes two edges that meet end to end (G0):
 *   seedEdge  - the edge whose curve is edited; it ends up carrying seed + merge as ONE curve
 *   mergeEdge - the edge absorbed into the seed
 *
 * This is the standard-library Edit-curve / Composite-curve pipeline, driven so that the
 * caller controls which body survives: constructPath(seed U merge) -> makeApproximationTarget
 * -> approximateSpline -> opCreateBSplineCurve -> opEditCurve(target wire) -> opDeleteBodies.
 * There is no kernel "join two curves" primitive; every native route is fit-then-replace.
 *
 * Wire-membership scenarios (a "wire" here is a non-sketch BodyType.WIRE body):
 *   1. seed on wire W = {seed}, merge not on a wire      -> W edited in place; W keeps id + name
 *   2. seed on wire W = {seed}, merge on separate wire V -> W edited in place; V deleted if
 *                                                           V = {merge}, otherwise left alone
 *   3. seed and merge on the same wire W                 -> W = {seed, merge}: edited in place.
 *                                                           W has other edges: those are
 *                                                           re-extracted to their own wire(s),
 *                                                           seed U merge extracted to a copy,
 *                                                           W deleted, the copy edited. Body
 *                                                           identity CHANGES (warned).
 *   4. seed not on a wire (solid / sheet / sketch edge)  -> opExtractWires(seed U merge), that
 *                                                           new wire edited; owners untouched
 * Scenarios 1-2 where W has edges beyond the seed reduce to the second half of scenario 3:
 * opEditCurve replaces the WHOLE wire body's curve, it cannot splice one edge of a multi-edge
 * wire.
 *
 * Arc caveat: the result is always a fitted NURBS. A circular arc (or ellipse) input loses its
 * analytic type -- the radius no longer reads in Onshape. Reported as a warning, not refused.
 *
 * opEditCurve is marked @internal in the standard library (std/geomOperations.fs). This
 * document already depends on internal opCreateOutline (evaluate_profiles.fs) under the same
 * caveat. Fallback if it ever breaks: opExtractWires the new curve, delete the seed wire,
 * copy the name -- identity lost, geometry preserved.
 */

// ============================================================================
// Bounds
// ============================================================================

/** Control point cap for the merged fit. DEGREE_BOUND and TOLERANCE_BOUND come from std. */
export const MERGE_MAX_CPS_BOUND =
{
            (unitless) : [4, 100, MAX_CONTROL_POINTS]
        } as IntegerBoundSpec;

/** Endpoint gap tolerance for chaining the two edges (same value std Edit curve uses). */
export const MERGE_CHAIN_TOLERANCE = 1e-5 * meter;

// ============================================================================
// Feature
// ============================================================================

annotation { "Feature Type Name" : "Merge curve",
        "Feature Type Description" : "Merge a G0-adjacent edge into a seed edge as one spline curve." }
export const mergeCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Seed edge", "Filter" : EntityType.EDGE && ConstructionObject.NO, "MaxNumberOfPicks" : 1,
                    "Description" : "The edge that is edited. Its curve becomes seed + merge." }
        definition.seedEdge is Query;

        annotation { "Name" : "Merge edge", "Filter" : EntityType.EDGE && ConstructionObject.NO, "MaxNumberOfPicks" : 1,
                    "Description" : "The edge absorbed into the seed. Must meet the seed end to end (G0)." }
        definition.mergeEdge is Query;

        annotation { "Name" : "Keep start derivative" }
        definition.keepStartDerivative is boolean;

        annotation { "Name" : "Keep end derivative" }
        definition.keepEndDerivative is boolean;

        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, MERGE_MAX_CPS_BOUND);
        }

        annotation { "Name" : "Output name", "Description" : "Optional name for the resulting wire body." }
        definition.outputName is string;

        annotation { "Name" : "Warn on arc loss", "Description" : "Report a warning when an input arc or ellipse becomes a spline." }
        definition.warnOnArcLoss is boolean;
    }
    {
        // 1. Both inputs must be exactly one edge each, and not the same edge.
        const seedEdges = evaluateQuery(context, definition.seedEdge);
        if (size(seedEdges) != 1)
        {
            throw regenError("Select exactly one seed edge.", ["seedEdge"]);
        }
        const mergeEdges = evaluateQuery(context, definition.mergeEdge);
        if (size(mergeEdges) != 1)
        {
            throw regenError("Select exactly one merge edge.", ["mergeEdge"]);
        }
        const edges = qUnion([definition.seedEdge, definition.mergeEdge]);
        if (size(evaluateQuery(context, edges)) != 2)
        {
            throw regenError("Seed and merge edges must be different edges.", ["mergeEdge"]);
        }

        // 2. Chainability. constructPath throws when the endpoints do not meet; that IS the G0 test.
        var path;
        try silent
        {
            path = constructPath(context, edges, { "tolerance" : MERGE_CHAIN_TOLERANCE }).path;
        }
        catch (error)
        {
            throw regenError("Seed and merge edges are not G0 connected (they must meet end to end).", ["mergeEdge"], edges);
        }
        checkApproximationParameters(definition, path);

        // 3. Wire membership -> which body receives the merged curve. Pure decision, no ops yet:
        //    the path above still references the original edges and is sampled in step 5.
        const seedInfo = describeWireMembership(context, definition.seedEdge);
        const mergeInfo = describeWireMembership(context, definition.mergeEdge);
        const plan = planMergeTarget(context, seedInfo, mergeInfo);

        // 4. Arc / ellipse inputs become a spline.
        if (definition.warnOnArcLoss && (isArcLike(context, definition.seedEdge) || isArcLike(context, definition.mergeEdge)))
        {
            reportFeatureWarning(context, id, "Merged result is a spline; the arc radius is not preserved.");
        }

        // 5. Fit one spline through the chained path.
        const target = makeApproximationTarget(context, path, definition.keepStartDerivative, definition.keepEndDerivative);
        const spline = approximateSpline(context, {
                        "degree" : definition.approximationDegree,
                        "tolerance" : definition.approximationTolerance,
                        "isPeriodic" : path.closed,
                        "targets" : [target],
                        "maxControlPoints" : definition.approximationMaxCPs
                    })[0];
        opCreateBSplineCurve(context, id + "new", { "bSplineCurve" : spline });

        // Now that the path has been consumed it is safe to extract / delete wires.
        const targetWire = prepareTargetWire(context, id, plan, edges);

        opEditCurve(context, id + "edit", {
                    "wire" : targetWire,
                    "edge" : qCreatedBy(id + "new", EntityType.EDGE),
                    "showCurves" : true
                });
        opDeleteBodies(context, id + "cleanup", { "entities" : qCreatedBy(id + "new", EntityType.BODY) });

        if (plan.deleteMergeWire)
        {
            opDeleteBodies(context, id + "deleteMergeWire", { "entities" : mergeInfo.wire });
        }

        // 6. Name the surviving wire.
        nameOutput(context, targetWire, definition.outputName);
    }, {
        "keepStartDerivative" : true,
        "keepEndDerivative" : true,
        "outputName" : "",
        "warnOnArcLoss" : true
    });

// ============================================================================
// Wire membership
// ============================================================================

/**
 * Which non-sketch wire body, if any, owns `edge`.
 * @return {{
 *      @field onWire {boolean} : true when the owner is a BodyType.WIRE body that is not a sketch body
 *      @field wire {Query} : the owner body query (meaningful only when onWire)
 *      @field edgeCount {number} : number of edges on that wire (0 when not on a wire)
 * }}
 */
export function describeWireMembership(context is Context, edge is Query) returns map
{
    const owner = qOwnerBody(edge);
    const isWire = !isQueryEmpty(context, qBodyType(owner, BodyType.WIRE));
    const isSketch = !isQueryEmpty(context, qSketchFilter(owner, SketchObject.YES));
    const onWire = isWire && !isSketch;
    var edgeCount = 0;
    if (onWire)
    {
        edgeCount = size(evaluateQuery(context, qOwnedByBody(owner, EntityType.EDGE)));
    }
    return { "onWire" : onWire, "wire" : owner, "edgeCount" : edgeCount };
}

/** True when the two membership records point at the same body. */
function isSameWire(context is Context, seedInfo is map, mergeInfo is map) returns boolean
{
    if (!seedInfo.onWire || !mergeInfo.onWire)
    {
        return false;
    }
    return size(evaluateQuery(context, qUnion([seedInfo.wire, mergeInfo.wire]))) == 1;
}

/**
 * Decide, without modifying the context, what body receives the merged curve.
 * @return {{
 *      @field mode {string} : "IN_PLACE" (edit the seed wire), "REBUILD" (seed wire has extra
 *          edges: split it and edit a fresh copy), "EXTRACT" (seed is not on a wire: new wire)
 *      @field seedWire {Query} : the seed's wire (IN_PLACE / REBUILD)
 *      @field deleteMergeWire {boolean} : delete the merge edge's wire after the edit (scenario 2, V = {merge})
 * }}
 */
export function planMergeTarget(context is Context, seedInfo is map, mergeInfo is map) returns map
{
    if (!seedInfo.onWire)
    {
        // Scenario 4: a new wire regardless of where the merge edge lives.
        return { "mode" : "EXTRACT", "seedWire" : qNothing(), "deleteMergeWire" : false };
    }

    const sameWire = isSameWire(context, seedInfo, mergeInfo);
    // The seed wire may be edited in place only when its edges are exactly the ones being merged.
    const expectedCount = sameWire ? 2 : 1;
    const inPlace = seedInfo.edgeCount == expectedCount;

    // Scenario 2: the merge edge's own wire is emptied by the merge only if it held nothing else.
    const deleteMergeWire = !sameWire && mergeInfo.onWire && mergeInfo.edgeCount == 1;

    return {
            "mode" : inPlace ? "IN_PLACE" : "REBUILD",
            "seedWire" : seedInfo.wire,
            "deleteMergeWire" : deleteMergeWire
        };
}

/**
 * Execute the plan: returns the wire body query that opEditCurve should replace.
 * Runs opExtractWires / opDeleteBodies as needed; call only after the path has been sampled.
 */
export function prepareTargetWire(context is Context, id is Id, plan is map, edges is Query) returns Query
{
    if (plan.mode == "IN_PLACE")
    {
        return plan.seedWire;
    }

    if (plan.mode == "REBUILD")
    {
        // Keep the seed wire's other edges alive as their own wire(s), then retire the original.
        const otherEdges = qSubtraction(qOwnedByBody(plan.seedWire, EntityType.EDGE), edges);
        opExtractWires(context, id + "extractRemainder", { "edges" : otherEdges });
        opExtractWires(context, id + "extract", { "edges" : edges });
        opDeleteBodies(context, id + "deleteSeedWire", { "entities" : plan.seedWire });
        reportFeatureWarning(context, id, "The seed wire had other edges; the merged curve is a new wire body and the remaining edges were re-extracted. Body identity changed.");
        return qCreatedBy(id + "extract", EntityType.BODY);
    }

    // EXTRACT: seed is not on a wire.
    opExtractWires(context, id + "extract", { "edges" : edges });
    return qCreatedBy(id + "extract", EntityType.BODY);
}

// ============================================================================
// Small helpers
// TODO: replace with shared helpers from edge_offset_utils.fs once that tab settles.
// ============================================================================

/** True when the edge's simplified curve definition is a circular arc or an ellipse. */
function isArcLike(context is Context, edge is Query) returns boolean
{
    const curveDef = evCurveDefinition(context, { "edge" : edge, "simplify" : true });
    return curveDef is Circle || curveDef is Ellipse;
}

/**
 * Name the bodies this feature produced.
 *
 * An empty name is a deliberate choice, not a missing value: setProperty would happily
 * write "" and leave the bodies looking unnamed but shadowed, so skip it instead.
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
