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
 * -> approximateSpline -> opCreateBSplineCurve -> then opEditCurve (in place) or opExtractWires
 * (rebuild) -> opDeleteBodies.
 * There is no kernel "join two curves" primitive; every native route is fit-then-replace.
 *
 * Wire-membership scenarios (a "wire" here is a non-sketch BodyType.WIRE body):
 *   1. seed on wire W = {seed}, merge not on a wire      -> W edited in place; W keeps id + name
 *   2. seed on wire W = {seed}, merge on separate wire V -> W edited in place; V deleted if
 *                                                           V = {merge}, otherwise left alone
 *   3. seed and merge on the same wire W                 -> W's other edges and the merged
 *                                                           curve are extracted TOGETHER (so a
 *                                                           chain the merge bridges comes out
 *                                                           as one wire); W deleted. Body
 *                                                           identity CHANGES (warned).
 *   4. seed not on a wire (solid / sheet / sketch edge)  -> the fitted spline body is the
 *                                                           output; owners untouched
 * Scenarios 1-2 where W has edges beyond the seed reduce to scenario 3: opEditCurve replaces
 * the WHOLE wire body's curve, it cannot splice one edge of a multi-edge wire, so it is used
 * only when the seed is its wire's sole edge.
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

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print diagnostics", "Description" : "Print what the feature sees at each step to the FeatureScript notices." }
            definition.debugPrint is boolean;
        }
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

        if (definition.debugPrint)
        {
            printInputs(context, definition, seedInfo, mergeInfo, plan, path);
        }

        // 4. Arc / ellipse inputs become a spline.
        if (definition.warnOnArcLoss && (isArcLike(context, definition.seedEdge) || isArcLike(context, definition.mergeEdge)))
        {
            reportFeatureWarning(context, id, "Merged result is a spline; the arc radius is not preserved.");
        }

        // 5. Fit one spline through the chained path.
        const target = makeApproximationTarget(context, path, definition.keepStartDerivative, definition.keepEndDerivative);
        var spline = approximateSpline(context, {
                        "degree" : definition.approximationDegree,
                        "tolerance" : definition.approximationTolerance,
                        "isPeriodic" : path.closed,
                        "targets" : [target],
                        "maxControlPoints" : definition.approximationMaxCPs
                    })[0];
        // approximateSpline only promises the fit within tolerance; the ends can miss the
        // chain's vertices by that much and then fail to chain with their neighbours.
        if (definition.debugPrint)
        {
            const last = size(spline.controlPoints) - 1;
            println("[merge] fit end error before snap: start " ~ roundToPrecision(norm(spline.controlPoints[0] - target.positions[0]) / millimeter, 6)
                ~ " mm, end " ~ roundToPrecision(norm(spline.controlPoints[last] - target.positions[size(target.positions) - 1]) / millimeter, 6) ~ " mm");
        }
        if (!spline.isPeriodic)
        {
            spline = snapSplineEnds(spline, target.positions);
        }
        opCreateBSplineCurve(context, id + "new", { "bSplineCurve" : spline });
        const newEdge = qCreatedBy(id + "new", EntityType.EDGE);
        const newBody = qCreatedBy(id + "new", EntityType.BODY);

        const newEdgeCount = size(evaluateQuery(context, newEdge));
        if (definition.debugPrint)
        {
            println("[merge] spline: degree " ~ spline.degree ~ ", " ~ size(spline.controlPoints) ~ " control points, periodic "
                ~ spline.isPeriodic);
            println("[merge] new curve: " ~ size(evaluateQuery(context, newBody)) ~ " body(ies), " ~ newEdgeCount ~ " edge(s)");
        }
        if (newEdgeCount != 1)
        {
            throw regenError("Fitted curve resolved to " ~ newEdgeCount ~ " edges; expected 1.", newEdge);
        }

        // 6. Put the merged curve where it belongs. The path has been consumed, so wires may
        //    now be extracted and deleted.
        const output = placeMergedCurve(context, id, plan, edges, newEdge, newBody, definition.debugPrint);

        if (plan.deleteMergeWire)
        {
            opDeleteBodies(context, id + "deleteMergeWire", { "entities" : mergeInfo.wire });
        }

        // 7. Name the surviving wire(s).
        nameOutput(context, output, definition.outputName);
    }, {
        "keepStartDerivative" : true,
        "keepEndDerivative" : true,
        "outputName" : "",
        "warnOnArcLoss" : true,
        "debugPrint" : false
    });

// ============================================================================
// Diagnostics
// ============================================================================

/**
 * Print everything the feature has decided before it modifies the context, so a failure
 * can be read back as "which fact was wrong": the inputs, their owners, how the two edges
 * meet, the path, and the plan.
 */
function printInputs(context is Context, definition is map, seedInfo is map, mergeInfo is map, plan is map, path is map)
{
    println("[merge] seed : " ~ describeEdge(context, definition.seedEdge, seedInfo));
    println("[merge] merge: " ~ describeEdge(context, definition.mergeEdge, mergeInfo));
    println("[merge] same wire: " ~ isSameWire(context, seedInfo, mergeInfo));

    // The four end-to-end pairings. constructPath accepted the closest one within
    // MERGE_CHAIN_TOLERANCE; opExtractWires uses the kernel's own (tighter) tolerance.
    const seedEnds = evEdgeTangentLines(context, { "edge" : definition.seedEdge, "parameters" : [0, 1] });
    const mergeEnds = evEdgeTangentLines(context, { "edge" : definition.mergeEdge, "parameters" : [0, 1] });
    const labels = ["start", "end"];
    for (var i = 0; i < 2; i += 1)
    {
        for (var j = 0; j < 2; j += 1)
        {
            const gap = norm(seedEnds[i].origin - mergeEnds[j].origin);
            println("[merge] gap seed." ~ labels[i] ~ " -> merge." ~ labels[j] ~ ": " ~ roundToPrecision(gap / millimeter, 6) ~ " mm"
                ~ ", angle " ~ roundToPrecision(angleBetween(seedEnds[i].direction, mergeEnds[j].direction) / degree, 3) ~ " deg");
        }
    }

    println("[merge] path: " ~ size(path.edges) ~ " edge(s), closed " ~ path.closed ~ ", flipped " ~ path.flipped
        ~ ", length " ~ roundToPrecision(evLength(context, { "entities" : qUnion(path.edges) }) / millimeter, 3) ~ " mm");
    println("[merge] plan: mode " ~ plan.mode ~ ", delete merge wire " ~ plan.deleteMergeWire);
}

/** One line: curve type, owner body count, wire/sketch membership, edges on the owner. */
function describeEdge(context is Context, edge is Query, info is map) returns string
{
    const owners = size(evaluateQuery(context, qOwnerBody(edge)));
    const curveDef = evCurveDefinition(context, { "edge" : edge, "simplify" : true });
    return curveTypeName(curveDef) ~ ", length " ~ roundToPrecision(evLength(context, { "entities" : edge }) / millimeter, 3) ~ " mm"
        ~ ", owner bodies " ~ owners ~ ", on wire " ~ info.onWire ~ ", edges on owner " ~ info.edgeCount;
}

function curveTypeName(curveDef) returns string
{
    if (curveDef is Line)
    {
        return "Line";
    }
    if (curveDef is Circle)
    {
        return "Circle r=" ~ roundToPrecision(curveDef.radius / millimeter, 3) ~ " mm";
    }
    if (curveDef is Ellipse)
    {
        return "Ellipse";
    }
    if (curveDef is BSplineCurve)
    {
        return "BSpline deg " ~ curveDef.degree ~ ", " ~ size(curveDef.controlPoints) ~ " CPs";
    }
    return "other";
}

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
    // The seed wire may be edited in place only when the seed is its only edge: opEditCurve
    // replaces one single-edge wire body. Anything else is rebuilt around a copy of the seed.
    const inPlace = seedInfo.edgeCount == 1;

    // Scenario 2: the merge edge's own wire is emptied by the merge only if it held nothing else.
    const deleteMergeWire = !sameWire && mergeInfo.onWire && mergeInfo.edgeCount == 1;

    return {
            "mode" : inPlace ? "IN_PLACE" : "REBUILD",
            "seedWire" : seedInfo.wire,
            "deleteMergeWire" : deleteMergeWire
        };
}

/**
 * Put the merged curve into the context according to the plan and return the output
 * wire(s).
 *
 * IN_PLACE : opEditCurve replaces the seed wire's curve; W keeps its id and name.
 * EXTRACT  : the fitted spline body IS the output; nothing else is touched.
 * REBUILD  : the seed wire's other edges and the fitted curve are extracted TOGETHER, so
 *            chains the merge bridges come out as one wire; W and the spline body are
 *            deleted. Body identity changes (warned). Extracting seed + merge instead is not
 *            an option: opExtractWires chains with the kernel tolerance, tighter than the
 *            1e-5 m constructPath tolerance, and a micron gap yields two wires.
 */
export function placeMergedCurve(context is Context, id is Id, plan is map, edges is Query, newEdge is Query,
    newBody is Query, debugPrint is boolean) returns Query
{
    if (plan.mode == "IN_PLACE")
    {
        opEditCurve(context, id + "edit", {
                    "wire" : plan.seedWire,
                    "edge" : newEdge,
                    "showCurves" : true
                });
        opDeleteBodies(context, id + "cleanup", { "entities" : newBody });
        return plan.seedWire;
    }

    if (plan.mode == "REBUILD")
    {
        const otherEdges = qSubtraction(qOwnedByBody(plan.seedWire, EntityType.EDGE), edges);
        opExtractWires(context, id + "extract", { "edges" : qUnion([otherEdges, newEdge]) });
        const output = qCreatedBy(id + "extract", EntityType.BODY);
        if (debugPrint)
        {
            const wires = evaluateQuery(context, output);
            println("[merge] REBUILD: " ~ size(evaluateQuery(context, otherEdges)) ~ " other edge(s) + merged curve -> " ~ size(wires) ~ " wire(s)");
            for (var w = 0; w < size(wires); w += 1)
            {
                const wireEdges = qOwnedByBody(wires[w], EntityType.EDGE);
                println("[merge]   wire " ~ w ~ ": " ~ size(evaluateQuery(context, wireEdges)) ~ " edge(s), length "
                    ~ roundToPrecision(evLength(context, { "entities" : wireEdges }) / millimeter, 3) ~ " mm"
                    ~ (isQueryEmpty(context, qIntersection([wireEdges, newEdge])) ? "" : " (contains the merged curve)"));
            }
        }
        opDeleteBodies(context, id + "deleteSeedWire", { "entities" : qUnion([plan.seedWire, newBody]) });
        reportFeatureWarning(context, id, "The seed wire had other edges; it was rebuilt as "
            ~ size(evaluateQuery(context, output)) ~ " new wire body(ies). Body identity changed.");
        return output;
    }

    // EXTRACT: seed is not on a wire. The fitted spline body is the result.
    return newBody;
}

// ============================================================================
// Small helpers
// TODO: replace with shared helpers from edge_offset_utils.fs once that tab settles.
// ============================================================================

/** Pin a clamped spline's end control points to the exact target end points. */
function snapSplineEnds(curve is BSplineCurve, positions is array) returns BSplineCurve
{
    var controlPoints = curve.controlPoints;
    controlPoints[0] = positions[0];
    controlPoints[size(controlPoints) - 1] = positions[size(positions) - 1];
    return mergeMaps(curve, { "controlPoints" : controlPoints }) as BSplineCurve;
}

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
