FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import tools/bspline_knots.fs
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/dadb70c0a762573622fa609c", version : "744c7afe122e8ae2b3b17a12");
// import tools/frenet
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/a19a275a032ee47f4dbcc83c", version : "e11709063628c9ca70edfca2");
//import tools/transition_functions (export/import)
export import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/a656fa0d17723f0dafaf8638", version : "172b230734bb73cef189f4ed");
//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "7a407d1cf555ba0254c21433");
//import scaledCurve
import(path : "2dfee1d44e9bde0daba9d73e", version : "43e9d842d62de2642d74cefc");

//import continuityTools
import(path : "6db2a56b5418f71818d7a607", version : "572e9b00092aba3c046bebcc");
//import curveOps
import(path : "73de71e75b755f0042e0e6d8", version : "585ddf22041315f25495eeec");

// IMPORT: Variable_tools V1 extract_outputs.fs (embedStandardOutputs)
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

IconNamespace::import(path : "71590b120a852dc32d7ad90a", version : "41610d09c4cd9ac57a27e926");

/**
 * How the part of the curve that must not move is given.
 */
export enum HoldMode
{
    annotation { "Name" : "Point" }
    POINT,
    annotation { "Name" : "Distance" }
    DISTANCE
}

/**
 * Curvature at the modified end when "Match" is G2.
 */
export enum EndCurvatureMode
{
    annotation { "Name" : "Match reference" }
    MATCH,
    annotation { "Name" : "Zero" }
    ZERO,
    annotation { "Name" : "Radius" }
    RADIUS
}

export const HOLD_DISTANCE_BOUNDS = { (millimeter) : [1e-3, 50, 1e6] } as LengthBoundSpec;
export const END_RADIUS_BOUNDS = { (millimeter) : [1e-2, 100, 1e6] } as LengthBoundSpec;

// At least this many control points from the hold (or the fixed end) to the modified end, so the
// transition has enough control points to follow its shape. Knot insertion adds them without changing the curve.
const MCE_MIN_FREE_CONTROL_POINTS = 8;

// Parameter spans for the arc-length table (each integrated with 5-point Gauss-Legendre).
const MCE_ARC_LENGTH_SPANS = 200;

// An existing knot this close to the hold parameter is used as the hold knot.
const MCE_KNOT_SNAP = 1e-7;

// A hold-knot removal is kept only if the curve moves less than this (exact in theory; 1 nm absorbs the rounding of
// the divisions in P&T A5.6 on curves far from the origin).
const MCE_KNOT_REMOVAL_TOLERANCE = 1e-9 * meter;

// Transport: at most this many parameter steps from the end to the hold between tangent samples (plus every Greville abscissa).
const MCE_TRANSPORT_STEPS = 400;

const MCE_GAUSS_NODES = [-0.9061798459386640, -0.5384693101056831, 0, 0.5384693101056831, 0.9061798459386640];
const MCE_GAUSS_WEIGHTS = [0.2369268850561891, 0.4786286704993665, 0.5688888888888889, 0.4786286704993665, 0.2369268850561891];

annotation { "Feature Type Name" : "Modify curve end", "Icon" : IconNamespace::BLOB_DATA,
        "Feature Type Description" : "Moves one end of a curve, an edge chain or a wire to a new point and blends the change back toward the fixed end or a hold point. The end can match the tangent and curvature of a reference edge, face or mate connector. The held part of the curve does not move." }
export const modCurveEnd = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges or wire to modify", "Filter" : (EntityType.EDGE || (EntityType.BODY && BodyType.WIRE)) && ConstructionObject.NO,
                     "Description" : "One edge, a chain of connected edges (in any order) or a wire. The chain must be open and must not branch." }
        definition.selEdges is Query;

        annotation { "Name" : "Modified end", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                     "Description" : "The chain end nearest this point is moved. Empty: the end of the chain." }
        definition.fromPoint is Query;

        annotation { "Name" : "To point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                     "Description" : "New position of the modified end. Empty: the end stays (or snaps onto the end reference) and only its tangent / curvature change." }
        definition.toPoint is Query;

        annotation { "Name" : "Hold part of the curve", "Default" : false,
                     "Description" : "Keep the curve unchanged from the fixed end up to a hold point; only the part beyond it moves." }
        definition.useHold is boolean;

        if (definition.useHold)
        {
            annotation { "Name" : "Hold by", "UIHint" : [UIHint.SHOW_LABEL, UIHint.HORIZONTAL_ENUM], "Default" : HoldMode.POINT }
            definition.holdMode is HoldMode;

            if (definition.holdMode == HoldMode.POINT)
            {
                annotation { "Name" : "Hold point", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                             "Description" : "Projected onto the curve. The curve is unchanged between the fixed end and this point." }
                definition.holdPoint is Query;
            }
            else
            {
                annotation { "Name" : "Hold distance", "Description" : "Arc length from the modified end to the hold. Everything farther from the modified end is unchanged." }
                isLength(definition.holdDistance, HOLD_DISTANCE_BOUNDS);
            }

            annotation { "Name" : "Remove hold knot", "Default" : false,
                         "Description" : "After the edit, take the hold knot back down from multiplicity p to p - k (k = 1 for G1, 2 for G2): fewer control points, same curve (a removal is kept only where it moves the curve less than 1 nm)." }
            definition.removeHoldKnot is boolean;
        }

        annotation { "Group Name" : "Parameters", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Offset frame", "UIHint" : [UIHint.SHOW_LABEL], "Default" : OffsetFrame.FRENET,
                         "Description" : "World: the end moves by To - From. Frenet: currently the same constant offset as World (kept for saved features)." }
            definition.offsetFrame is OffsetFrame;

            annotation { "Name" : "Carry offset along the curve (transport)", "Default" : false,
                         "Description" : "Off: every point moves in the same direction (a constant offset, faded by the transition). On: the offset is fixed to the curve at the modified end and carried back along it by parallel transport (no twist), so it turns with the curve; the end still lands exactly on To." }
            definition.transportOffset is boolean;

            annotation { "Name" : "Transition type", "UIHint" : [UIHint.SHOW_LABEL], "Default" : TransitionType.LOGISTIC,
                         "Description" : "How the offset fades from the modified end to the hold (or the fixed end), by arc length." }
            definition.transitionType is TransitionType;

            annotation { "Name" : "Continuity at fixed end / hold point", "UIHint" : [UIHint.SHOW_LABEL], "Default" : GeometricContinuity.G0,
                         "Description" : "G0: coincident, G1: tangent, G2: equal curvature with the unchanged curve, at the hold point (or the fixed end without a hold)." }
            definition.fixedEndContinuity is GeometricContinuity;

            annotation { "Name" : "Endpoint continuity ref?", "Default" : false }
            definition.showModContinuity is boolean;

            if (definition.showModContinuity)
            {
                annotation { "Group Name" : "Endpoint continuity", "Collapsed By Default" : false, "Driving Parameter" : "showModContinuity" }
                {
                    annotation { "Name" : "Endpoint continuity ref.", "Filter" : EntityType.EDGE || EntityType.FACE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                                 "Description" : "Edge: its tangent and curvature at the point nearest the new end. Face: tangent in the face, normal curvature of the face. Mate connector: tangent along Z, zero curvature." }
                    definition.modContinuityRef is Query;

                    annotation { "Name" : "Opposite direction", "Default" : false, "UIHint" : [UIHint.OPPOSITE_DIRECTION] }
                    definition.flipRef is boolean;

                    annotation { "Name" : "Match", "UIHint" : [UIHint.SHOW_LABEL], "Default" : GeometricContinuity.G0,
                                 "Description" : "G0: position only (used when To point is empty). G1: end tangent. G2: end tangent and curvature." }
                    definition.modEndContinuity is GeometricContinuity;

                    if (definition.modEndContinuity == GeometricContinuity.G2)
                    {
                        annotation { "Name" : "End curvature", "UIHint" : [UIHint.SHOW_LABEL], "Default" : EndCurvatureMode.MATCH }
                        definition.modCurvatureMode is EndCurvatureMode;

                        if (definition.modCurvatureMode == EndCurvatureMode.RADIUS)
                        {
                            annotation { "Name" : "End radius", "Description" : "Bends toward the mate connector's X axis, else the reference edge's normal, else the curve's own normal." }
                            isLength(definition.modEndRadius, END_RADIUS_BOUNDS);
                        }
                    }
                }
            }
        }

        annotation { "Name" : "Project onto surface?" }
        definition.curveOnSurface is boolean;

        if (definition.curveOnSurface)
        {
            annotation { "Name" : "Projection face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
            definition.projectionFace is Query;
        }

        annotation { "Group Name" : "Debug, Details", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Spline degree", "Description" : "Degree of the fit when a chain of edges is merged into one curve (and of lines, which are raised to at least 3)." }
            isInteger(definition.splineDegree, DEGREE_BOUND);

            annotation { "Name" : "Sampled spline tol." }
            isLength(definition.splineTol, TOLERANCE_BOUND);

            annotation { "Name" : "Max control points", "Description" : "Cap on the control points of the merged fit of a chain of edges." }
            isInteger(definition.splineCP, { (unitless) : [4, 10, 100] } as IntegerBoundSpec);

            annotation { "Name" : "Sampling multiple", "Default" : 4, "Description" : "Projection samples = control points * N" }
            isInteger(definition.samplingMultiple, POSITIVE_COUNT_BOUNDS);

            annotation { "Name" : "Print input BSplineCurve" }
            definition.printInput is boolean;

            annotation { "Name" : "Print output BSplineCurve" }
            definition.printOutput is boolean;

            if (definition.printInput || definition.printOutput)
            {
                annotation { "Name" : "BSpline Print Format", "Default" : PrintFormat.METADATA }
                definition.printFormat is PrintFormat;
            }
        }
    }
    {
        var notes = [];
        const tol = definition.splineTol;

        // ---- 1. The chain, as one clamped non-rational curve on [0, 1] ----
        const chain = mceResolveChain(context, definition.selEdges, definition.splineDegree, tol, definition.splineCP);
        notes = concatenateArrays([notes, chain.notes]);
        var curve = chain.curve;
        const inputCurve = curve;

        // ---- 2. Orient: the modified end is always u = 1 ----
        const startPoint = curve.controlPoints[0];
        const endPoint = curve.controlPoints[size(curve.controlPoints) - 1];
        var reversed = false;
        if (isQueryEmpty(context, definition.fromPoint))
        {
            notes = append(notes, "No Modified end picked: the end of the chain was moved.");
        }
        else
        {
            const pick = mcePointOf(context, definition.fromPoint);
            const d0 = norm(pick - startPoint);
            const d1 = norm(pick - endPoint);
            if (abs(d0 - d1) < 0.01 * chain.length)
            {
                throw regenError("The Modified end pick is about as close to both ends of the chain; pick the end vertex.", ["fromPoint"]);
            }
            reversed = d0 < d1;
            if (min(d0, d1) > tol)
            {
                notes = append(notes, "The Modified end pick is " ~ mceMm(min(d0, d1)) ~ " from the chain end; that end was moved.");
            }
        }
        if (reversed)
        {
            curve = mceReverse(curve);
        }
        const oldEnd = curve.controlPoints[size(curve.controlPoints) - 1];

        // ---- The reference (only while "Endpoint continuity ref?" is on) ----
        const hasRef = definition.showModContinuity && !isQueryEmpty(context, definition.modContinuityRef);
        const ref = hasRef ? mceReferenceOf(context, definition.modContinuityRef) : undefined;
        const kEnd = hasRef ? mceContinuityOrder(definition.modEndContinuity) : 0;

        // ---- Target position: To point wins; without one the end snaps onto the reference ----
        var target = oldEnd;
        if (!isQueryEmpty(context, definition.toPoint))
        {
            target = mcePointOf(context, definition.toPoint);
        }
        else if (hasRef)
        {
            target = mceNearestOnReference(context, ref, oldEnd);
            if (norm(target - oldEnd) > tol)
            {
                notes = append(notes, "No To point: the end was snapped onto the reference (" ~ mceMm(norm(target - oldEnd)) ~ ").");
            }
        }
        // FRENET is a constant offset, the same as WORLD (curves.md sec 4.4: kept as it was; TRANSPORT is a later option).
        const displacement = target - oldEnd;

        // ---- 3. Hold ----
        const kHold = mceContinuityOrder(definition.fixedEndContinuity);
        const table = mceArcLengthTable(curve);
        const totalLength = table.lengths[size(table.lengths) - 1];
        var holdParam = 0;
        if (definition.useHold)
        {
            if (definition.holdMode == HoldMode.POINT)
            {
                if (isQueryEmpty(context, definition.holdPoint))
                {
                    throw regenError("Select a hold point, or turn off Hold part of the curve.", ["holdPoint"]);
                }
                const projected = mceProjectParam(curve, mcePointOf(context, definition.holdPoint));
                if (projected.distance > tol)
                {
                    notes = append(notes, "The hold point is " ~ mceMm(projected.distance) ~ " off the curve; the nearest curve point is held.");
                }
                const fromEnd = totalLength - mceLengthsAt(curve, table, [projected.parameter])[0];
                if (fromEnd < tol)
                {
                    throw regenError("The hold point is at the modified end, so nothing could move.", ["holdPoint"]);
                }
                if (totalLength - fromEnd < tol)
                {
                    notes = append(notes, "The hold point is at the fixed end: the hold has no effect.");
                }
                else
                {
                    holdParam = projected.parameter;
                }
            }
            else
            {
                const d = definition.holdDistance;
                if (d > totalLength + tol)
                {
                    throw regenError("Hold distance " ~ mceMm(d) ~ " is longer than the curve (" ~ mceMm(totalLength) ~ ").", ["holdDistance"]);
                }
                if (d < tol)
                {
                    throw regenError("The hold is at the modified end, so nothing could move.", ["holdDistance"]);
                }
                if (d >= totalLength - tol)
                {
                    notes = append(notes, "Hold distance reaches the fixed end: the hold has no effect.");
                }
                else
                {
                    holdParam = mceParamAtLength(curve, table, totalLength - d);
                }
            }
            if (holdParam > 0 && kHold == 0)
            {
                notes = append(notes, "A G0 hold leaves a corner at the hold point; use G1 or G2 for a smooth hold.");
            }
        }
        const holdActive = holdParam > 0;

        // ---- 3b. Hold knot and enough free control points (exact: the shape does not change) ----
        const prepared = mcePrepare(curve, holdParam, kHold, kEnd);
        curve = prepared.curve;
        holdParam = prepared.holdParam;
        const holdIndex = prepared.holdIndex;
        const lastLocked = holdIndex + kHold;

        // ---- 4. Blend weights and displacement (constant, or carried along the curve by parallel transport) ----
        if (definition.transportOffset)
        {
            curve = mceDisplaceTransported(curve, table, holdParam, lastLocked, displacement, definition.transitionType);
        }
        else
        {
            curve = mceDisplace(curve, table, holdParam, lastLocked, displacement, definition.transitionType);
        }

        // ---- 5. End reference overwrite (closed form) ----
        var endTangent = undefined;
        var endCurvature = undefined;
        if (kEnd >= 1)
        {
            const conditions = mceEndConditions(context, ref, curve, definition, kEnd, tol);
            notes = concatenateArrays([notes, conditions.notes]);
            endTangent = conditions.tangent;
            endCurvature = conditions.curvature;
            const overwritten = mceEndOverwrite(curve, kEnd, endTangent, endCurvature);
            notes = concatenateArrays([notes, overwritten.notes]);
            curve = overwritten.curve;
        }

        if (norm(displacement) < TOLERANCE.zeroLength * meter && kEnd == 0)
        {
            notes = append(notes, "Nothing to change: the curve is copied.");
        }

        // ---- 7. Optional projection (the only refit; never touches the held part) ----
        if (definition.curveOnSurface)
        {
            if (isQueryEmpty(context, definition.projectionFace))
            {
                throw regenError("Select a projection face, or turn off Project onto surface.", ["projectionFace"]);
            }
            const numSamples = max(10, definition.samplingMultiple * size(curve.controlPoints));
            const projectedCurve = mceProjectTail(context, curve, definition.projectionFace, holdActive ? holdParam : 0, holdIndex,
                kHold, kEnd, endTangent, endCurvature, numSamples, tol);
            notes = concatenateArrays([notes, projectedCurve.notes]);
            curve = projectedCurve.curve;
        }

        if (mceHasLoop(curve, holdActive ? holdParam : 0))
        {
            notes = append(notes, "Possible loop or cusp near the modified end; check the result.");
        }

        const holdPosition = holdActive ? curve.controlPoints[holdIndex] : curve.controlPoints[0];

        // ---- 6. Optional: take the hold knot back down (exact: the edited curve is C^kHold there) ----
        if (holdActive && definition.removeHoldKnot)
        {
            // Projection keeps only G1 at the hold (mceProjectTail), so at most one removal there.
            const wanted = definition.curveOnSurface ? min(kHold, 1) : kHold;
            if (wanted == 0)
            {
                notes = append(notes, "Remove hold knot: a G0 hold is a corner, so its knot stays.");
            }
            else
            {
                const removal = mceRemoveKnot(curve, holdParam, wanted, MCE_KNOT_REMOVAL_TOLERANCE);
                curve = removal.curve;
                if (removal.removed < wanted)
                {
                    notes = append(notes, "Remove hold knot: removed " ~ removal.removed ~ " of " ~ wanted ~ " (the rest would move the curve).");
                }
            }
        }
        const movedPosition = curve.controlPoints[size(curve.controlPoints) - 1];
        const modifiedCurve = reversed ? mceReverse(curve) : curve;

        if (definition.printInput || definition.printOutput)
        {
            println(" - - - - - - - - Modified Endpoint Spline data - - - - - - - - ");
            println("modified end " ~ toString(oldEnd) ~ " -> " ~ toString(target) ~ ", reversed " ~ reversed);
            println("hold parameter " ~ holdParam ~ " (control point " ~ holdIndex ~ "), fixed/hold continuity " ~ definition.fixedEndContinuity
                ~ ", end continuity order " ~ kEnd);
            println("transitionType " ~ definition.transitionType ~ ", offsetFrame " ~ definition.offsetFrame ~ ", transport " ~ definition.transportOffset
                ~ ", remove hold knot " ~ definition.removeHoldKnot ~ ", control points " ~ size(modifiedCurve.controlPoints));
        }
        if (definition.printInput)
        {
            printBSpline(inputCurve, definition.printFormat, ["* * * * * Modify Endpoint input BSplineCurve *  * * * * "]);
        }
        if (definition.printOutput)
        {
            printBSpline(modifiedCurve, definition.printFormat, ["* * * * * Modify Endpoint output BSplineCurve *  * * * * "]);
        }

        opCreateBSplineCurve(context, id + "createModifiedEndpointBSpline", {
                    "bSplineCurve" : modifiedCurve
                });
        const output = qCreatedBy(id + "createModifiedEndpointBSpline", EntityType.BODY);
        const vertices = qOwnedByBody(output, EntityType.VERTEX);

        // holdVertex: a point body at the hold (the curve is one edge, so it has no vertex there), else the fixed-end vertex.
        var holdVertex = qContainsPoint(vertices, holdPosition);
        if (holdActive)
        {
            opPoint(context, id + "holdPoint", { "point" : holdPosition });
            holdVertex = qCreatedBy(id + "holdPoint", EntityType.VERTEX);
        }

        embedStandardOutputs(context, id, {
                    "output" : output,
                    "outputDescription" : "The modified curve",
                    "inputs" : qUnion([definition.selEdges, definition.fromPoint, definition.toPoint, definition.modContinuityRef, definition.holdPoint]),
                    "queries" : {
                        "movedVertex" : extractableQuery(qContainsPoint(vertices, movedPosition), "The vertex at the moved end (at the To point).", DebugColor.BLUE),
                        "holdVertex" : extractableQuery(holdVertex, "The hold point (a point body), or the fixed-end vertex when there is no hold.", DebugColor.GREEN)
                    }
                });

        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
    }, { "useHold" : false, "holdMode" : HoldMode.POINT, "holdPoint" : qNothing(), "holdDistance" : 50 * millimeter,
            "showModContinuity" : false, "modContinuityRef" : qNothing(), "flipRef" : false, "modEndContinuity" : GeometricContinuity.G0,
            "modCurvatureMode" : EndCurvatureMode.MATCH, "modEndRadius" : 100 * millimeter, "curveOnSurface" : false, "projectionFace" : qNothing(),
            "fromPoint" : qNothing(), "toPoint" : qNothing(), "printInput" : false, "printOutput" : false, "printFormat" : PrintFormat.METADATA,
            "transportOffset" : false, "removeHoldKnot" : false });

// ============================================================================
// Chain and selections
// ============================================================================

/**
 * The selected edges / wires as one open chain, read as one clamped, non-rational B-spline on [0, 1].
 * One edge is read exactly (forceNonRational, correction 39); a chain of edges is merged with one
 * approximateSpline fit through the path (the same fit as curve_tools mergedCurveThroughPath).
 *
 * @returns {{ @field curve {BSplineCurve}, @field length {ValueWithUnits}, @field notes {array} }}
 */
export function mceResolveChain(context is Context, selection is Query, degree is number, tol is ValueWithUnits, maxControlPoints is number) returns map
{
    var notes = [];
    const edgeQuery = qConstructionFilter(qUnion([qEntityFilter(selection, EntityType.EDGE),
                    qOwnedByBody(qBodyType(qEntityFilter(selection, EntityType.BODY), BodyType.WIRE), EntityType.EDGE)]),
            ConstructionObject.NO);
    var kept = [];
    var dropped = 0;
    for (var edge in evaluateQuery(context, edgeQuery))
    {
        if (evLength(context, { "entities" : edge }) < TOLERANCE.zeroLength * meter)
        {
            dropped += 1;
        }
        else
        {
            kept = append(kept, edge);
        }
    }
    if (size(kept) == 0)
    {
        throw regenError("Select an edge, a chain of edges or a wire to modify.", ["selEdges"]);
    }
    if (dropped > 0)
    {
        notes = append(notes, "Ignored " ~ dropped ~ " zero-length edge(s).");
    }

    // The one try in this feature: constructPaths refuses a branching selection with a kernel message.
    var paths;
    try silent
    {
        paths = constructPaths(context, qUnion(kept), {});
    }
    catch
    {
        throw regenError("Edges branch at a vertex; select one open chain.", ["selEdges"]);
    }
    if (size(paths) != 1)
    {
        throw regenError("The selection forms " ~ size(paths) ~ " separate chains. Select one chain.", ["selEdges"]);
    }
    const path = paths[0];
    if (path.closed)
    {
        throw regenError("The selected chain is closed. Select an open chain.", ["selEdges"]);
    }

    var curve;
    if (size(path.edges) == 1)
    {
        curve = evApproximateBSplineCurve(context, { "edge" : path.edges[0], "forceNonRational" : true });
        if (!mceIsClamped(curve))
        {
            curve = mcePathFit(context, path, degree, tol, maxControlPoints);
        }
    }
    else
    {
        curve = mcePathFit(context, path, degree, tol, maxControlPoints);
        const deviation = mceFitDeviation(context, curve, qUnion(path.edges));
        if (deviation > 2 * tol && size(curve.controlPoints) >= maxControlPoints)
        {
            notes = append(notes, "The merged chain stops at " ~ size(curve.controlPoints) ~ " control points and deviates up to "
                    ~ mceMm(deviation) ~ " from the edges; raise Max control points.");
        }
    }
    curve = mceCurve(curve.degree, curve.controlPoints, withUnitDomain(curve).knots);

    // A line comes back as degree 1 with 2 control points: raise it so tangent and curvature can be set.
    if (curve.degree < 3)
    {
        const elevated = elevateDegree(context, curve, max(degree, 3) - curve.degree);
        curve = mceCurve(elevated.degree, elevated.controlPoints, elevated.knots);
    }
    return { "curve" : curve, "length" : evLength(context, { "entities" : qUnion(path.edges) }), "notes" : notes };
}

/**
 * One fit through a path of edges: std's approximation target (APPROXIMATION_SAMPLES points with end
 * derivatives), then the ends snapped onto the path's end points.
 */
export function mcePathFit(context is Context, path is Path, degree is number, tol is ValueWithUnits, maxControlPoints is number) returns BSplineCurve
{
    const target = makeApproximationTarget(context, path, true, true);
    const spline = approximateSpline(context, {
                    "degree" : degree,
                    "tolerance" : tol,
                    "isPeriodic" : false,
                    "targets" : [target],
                    "maxControlPoints" : max(maxControlPoints, degree + 3),
                    "suppressInterpolationNotice" : true
                })[0];
    var controlPoints = spline.controlPoints;
    controlPoints[0] = target.positions[0];
    controlPoints[size(controlPoints) - 1] = target.positions[size(target.positions) - 1];
    return mceCurve(spline.degree, controlPoints, spline.knots);
}

/**
 * Largest distance from 25 points of the curve to the edges.
 */
export function mceFitDeviation(context is Context, curve is BSplineCurve, edges is Query) returns ValueWithUnits
{
    const lo = curve.knots[0];
    const hi = curve.knots[size(curve.knots) - 1];
    var params = [];
    for (var i = 0; i <= 24; i += 1)
    {
        params = append(params, lo + (hi - lo) * i / 24);
    }
    var worst = 0 * meter;
    for (var p in evaluateSpline({ "spline" : curve, "parameters" : params })[0])
    {
        worst = max(worst, evDistance(context, { "side0" : edges, "side1" : p }).distance);
    }
    return worst;
}

/**
 * Position of a vertex or mate connector pick. A mate connector can arrive as its vertex (correction 44); either way
 * the connector's origin is used.
 */
export function mcePointOf(context is Context, selection is Query) returns Vector
{
    const connectors = evaluateQuery(context, qBodyType(qOwnerBody(selection), BodyType.MATE_CONNECTOR));
    if (size(connectors) > 0)
    {
        return evMateConnector(context, { "mateConnector" : connectors[0] }).origin;
    }
    return evVertexPoint(context, { "vertex" : qEntityFilter(selection, EntityType.VERTEX) });
}

/**
 * The end reference, by what was picked: { kind : "CONNECTOR" | "EDGE" | "FACE", query, frame (connector only) }.
 */
export function mceReferenceOf(context is Context, selection is Query) returns map
{
    const connectors = evaluateQuery(context, qBodyType(qOwnerBody(selection), BodyType.MATE_CONNECTOR));
    if (size(connectors) > 0)
    {
        return { "kind" : "CONNECTOR", "query" : connectors[0], "frame" : evMateConnector(context, { "mateConnector" : connectors[0] }) };
    }
    const edges = evaluateQuery(context, qEntityFilter(selection, EntityType.EDGE));
    if (size(edges) > 0)
    {
        return { "kind" : "EDGE", "query" : edges[0] };
    }
    const faces = evaluateQuery(context, qEntityFilter(selection, EntityType.FACE));
    if (size(faces) > 0)
    {
        return { "kind" : "FACE", "query" : faces[0] };
    }
    throw regenError("The end reference must be an edge, a face or a mate connector.", ["modContinuityRef"]);
}

/**
 * Nearest point of the reference to a point (a mate connector's origin).
 */
export function mceNearestOnReference(context is Context, ref is map, point is Vector) returns Vector
{
    if (ref.kind == "CONNECTOR")
    {
        return ref.frame.origin;
    }
    return evDistance(context, { "side0" : ref.query, "side1" : point }).sides[0].point;
}

/** 0 / 1 / 2 for G0 / G1 / G2. */
export function mceContinuityOrder(continuity is GeometricContinuity) returns number
{
    if (continuity == GeometricContinuity.G2)
    {
        return 2;
    }
    if (continuity == GeometricContinuity.G1)
    {
        return 1;
    }
    return 0;
}

/** A length as "x.xxxx mm". */
export function mceMm(value is ValueWithUnits) returns string
{
    return toString(roundToPrecision(value / millimeter, 4)) ~ " mm";
}

// ============================================================================
// B-spline helpers (clamped, non-rational)
// ============================================================================

/** A clamped, non-rational, open B-spline. */
export function mceCurve(degree is number, controlPoints is array, knots is array) returns BSplineCurve
{
    return bSplineCurve({
                "degree" : degree,
                "isPeriodic" : false,
                "controlPoints" : controlPoints,
                "knots" : knots as KnotArray
            });
}

/** True for an open curve whose first and last p + 1 knots are equal. */
export function mceIsClamped(curve is BSplineCurve) returns boolean
{
    if (curve.isPeriodic)
    {
        return false;
    }
    const p = curve.degree;
    const knots = curve.knots;
    const last = size(knots) - 1;
    for (var i = 1; i <= p; i += 1)
    {
        if (knots[i] != knots[0] || knots[last - i] != knots[last])
        {
            return false;
        }
    }
    return true;
}

/** The same curve traversed the other way, on the same domain. */
export function mceReverse(curve is BSplineCurve) returns BSplineCurve
{
    const knots = curve.knots;
    const lo = knots[0];
    const hi = knots[size(knots) - 1];
    var newKnots = [];
    for (var i = size(knots) - 1; i >= 0; i -= 1)
    {
        newKnots = append(newKnots, lo + hi - knots[i]);
    }
    return mceCurve(curve.degree, reverse(curve.controlPoints), newKnots);
}

/**
 * Insert one knot (Boehm, P&T A5.1 with r = 1). The curve does not change.
 */
export function mceInsertKnot(curve is BSplineCurve, u is number) returns BSplineCurve
{
    const p = curve.degree;
    const knots = curve.knots;
    const points = curve.controlPoints;
    const n = size(points);
    var k = p;
    for (var i = p; i < n; i += 1)
    {
        if (knots[i] <= u)
        {
            k = i;
        }
    }
    var s = 0;
    for (var knot in knots)
    {
        if (knot == u)
        {
            s += 1;
        }
    }
    var newPoints = [];
    for (var i = 0; i <= n; i += 1)
    {
        if (i <= k - p)
        {
            newPoints = append(newPoints, points[i]);
        }
        else if (i >= k - s + 1)
        {
            newPoints = append(newPoints, points[i - 1]);
        }
        else
        {
            const alpha = (u - knots[i]) / (knots[i + p] - knots[i]);
            newPoints = append(newPoints, alpha * points[i] + (1 - alpha) * points[i - 1]);
        }
    }
    var newKnots = [];
    for (var i = 0; i < size(knots); i += 1)
    {
        newKnots = append(newKnots, knots[i]);
        if (i == k)
        {
            newKnots = append(newKnots, u);
        }
    }
    return mceCurve(p, newPoints, newKnots);
}

/**
 * Insert the hold knot to multiplicity p (then C(t_h) is control point `holdIndex` and the curve before t_h
 * depends only on control points 0..holdIndex), then add knots in the widest spans after the hold until
 * enough control points are free. Without a hold (holdParam 0) the fixed end is the hold, index 0.
 *
 * @returns {{ @field curve {BSplineCurve}, @field holdParam {number}, @field holdIndex {number} }}
 */
export function mcePrepare(curve is BSplineCurve, holdParam is number, kHold is number, kEnd is number) returns map
{
    const p = curve.degree;
    var c = curve;
    var h = holdParam;
    var holdIndex = 0;
    if (h > 0)
    {
        for (var knot in c.knots)
        {
            if (abs(knot - h) < MCE_KNOT_SNAP)
            {
                h = knot;
            }
        }
        var multiplicity = 0;
        for (var knot in c.knots)
        {
            if (knot == h)
            {
                multiplicity += 1;
            }
        }
        while (multiplicity < p)
        {
            c = mceInsertKnot(c, h);
            multiplicity += 1;
        }
        var lastAtHold = 0;
        for (var i = 0; i < size(c.knots); i += 1)
        {
            if (c.knots[i] == h)
            {
                lastAtHold = i;
            }
        }
        holdIndex = lastAtHold - p;
    }

    // Free control points: the end overwrite (kEnd + 1 points) must not reach the locked ones (holdIndex + kHold).
    const needed = max(MCE_MIN_FREE_CONTROL_POINTS, kHold + kEnd + 3);
    while (size(c.controlPoints) - holdIndex < needed)
    {
        var bestA = h;
        var bestB = h;
        for (var i = p; i < size(c.controlPoints); i += 1)
        {
            const a = c.knots[i];
            const b = c.knots[i + 1];
            // Longest span wins; near-equal spans (float noise, e.g. a translated copy) go to the first one,
            // so the same curve anywhere in space gets the same knot (2026-09-26, T09 vs T01).
            if (a >= h && b - a > bestB - bestA + MCE_KNOT_SNAP)
            {
                bestA = a;
                bestB = b;
            }
        }
        c = mceInsertKnot(c, (bestA + bestB) / 2);
    }
    return { "curve" : c, "holdParam" : h, "holdIndex" : holdIndex };
}

/** Greville abscissae of the control points, clamped to [0, 1]. */
export function mceGreville(curve is BSplineCurve) returns array
{
    const p = curve.degree;
    const knots = curve.knots;
    var greville = [];
    for (var i = 0; i < size(curve.controlPoints); i += 1)
    {
        var g = 0;
        for (var k = i + 1; k <= i + p; k += 1)
        {
            g += knots[k];
        }
        greville = append(greville, clamp(g / p, 0, 1));
    }
    return greville;
}

/**
 * Blend weight of every control point: 0 for 0..lastLocked, 1 for the last one, else transition(sigma_i) with sigma_i
 * the Greville abscissa of control point i mapped onto [hold, end] by arc length.
 */
export function mceBlendWeights(curve is BSplineCurve, table is map, holdParam is number, lastLocked is number,
    transitionType is TransitionType) returns array
{
    const n = size(curve.controlPoints);
    const greville = mceGreville(curve);
    const lengths = mceLengthsAt(curve, table, concatenateArrays([[holdParam], greville]));
    const holdLength = lengths[0];
    const span = table.lengths[size(table.lengths) - 1] - holdLength;
    var weights = makeArray(n, 0);
    for (var i = lastLocked + 1; i < n; i += 1)
    {
        var w = 1;
        if (i < n - 1)
        {
            w = evaluateTransition(clamp((lengths[i + 1] - holdLength) / span, 0, 1), transitionType);
        }
        weights[i] = w;
    }
    return weights;
}

/**
 * Move the free control points by w_i * D (weights from mceBlendWeights). Control points 0..lastLocked stay; the last
 * one moves by D. The basis functions sum to one, so the displacement along the curve is W(u) * D with W between 0 and 1.
 */
export function mceDisplace(curve is BSplineCurve, table is map, holdParam is number, lastLocked is number,
    displacement is Vector, transitionType is TransitionType) returns BSplineCurve
{
    const weights = mceBlendWeights(curve, table, holdParam, lastLocked, transitionType);
    var points = curve.controlPoints;
    for (var i = lastLocked + 1; i < size(points); i += 1)
    {
        points[i] = points[i] + weights[i] * displacement;
    }
    return mceCurve(curve.degree, points, curve.knots);
}

/**
 * TRANSPORT offset ("Carry offset along the curve").
 *
 * Definition: the displacement D = To - (old end) is attached to the curve at the modified end (u = 1) and carried
 * back along the UNEDITED curve by parallel transport. R(g) is the rotation-minimizing (Bishop, zero-twist) map from
 * u = 1 to parameter g: it sends the unit tangent T(1) to T(g) and turns vectors normal to the tangent with no spin
 * about it. Control point i moves by
 *     w_i * R(g_i) D
 * with w_i the same blend weight as the constant offset (mceBlendWeights) and g_i its Greville abscissa. So D's
 * tangential part turns with the tangent and its normal part keeps its angle to the transported normals.
 * Properties: R(1) = I and w = 1 at the last control point, so the end still lands exactly on To; the held control
 * points (w = 0) do not move; on a straight stretch R = I and the result is the constant (WORLD) offset.
 * Computed by composing minimal rotations between tangents sampled from u = 1 down to the lowest free Greville abscissa,
 * at most 1 / MCE_TRANSPORT_STEPS apart in u, plus every Greville abscissa; a tangent reversal between two samples
 * (a cusp) is skipped.
 */
export function mceDisplaceTransported(curve is BSplineCurve, table is map, holdParam is number, lastLocked is number,
    displacement is Vector, transitionType is TransitionType) returns BSplineCurve
{
    const n = size(curve.controlPoints);
    const weights = mceBlendWeights(curve, table, holdParam, lastLocked, transitionType);
    const greville = mceGreville(curve);
    var lowest = 1;
    for (var i = lastLocked + 1; i < n; i += 1)
    {
        lowest = min(lowest, greville[i]);
    }
    // Parameters from 1 down to the lowest free Greville abscissa: a uniform grid plus the abscissae themselves.
    var params = [];
    for (var k = 0; k <= MCE_TRANSPORT_STEPS; k += 1)
    {
        const u = 1 - k / MCE_TRANSPORT_STEPS;
        if (u > lowest)
        {
            params = append(params, u);
        }
    }
    for (var i = lastLocked + 1; i < n; i += 1)
    {
        params = append(params, greville[i]);
    }
    params = sort(params, function(a, b) { return b - a; });
    const d1 = evaluateSpline({ "spline" : curve, "parameters" : params, "nDerivatives" : 1 })[1];

    // Walk from u = 1 toward the hold carrying D; record it at every sample (looked up at the Greville abscissae).
    var carried = {};
    var v = displacement;
    var previous = normalize(d1[0]);
    for (var k = 0; k < size(params); k += 1)
    {
        if (norm(d1[k]) > TOLERANCE.zeroLength * meter)
        {
            const tangent = normalize(d1[k]);
            v = mceMinimalRotation(v, previous, tangent);
            previous = tangent;
        }
        carried[params[k]] = v;
    }

    var points = curve.controlPoints;
    for (var i = lastLocked + 1; i < n; i += 1)
    {
        const moved = i == n - 1 ? displacement : carried[greville[i]];
        points[i] = points[i] + weights[i] * moved;
    }
    return mceCurve(curve.degree, points, curve.knots);
}

/**
 * v turned by the smallest rotation that takes unit vector a to unit vector b (about the axis a x b), by Rodrigues'
 * formula with the unnormalized axis w = a x b and c = a . b:  v c + w x v + w (w . v) / (1 + c).
 * Parallel or opposite a and b: v is returned unchanged.
 */
export function mceMinimalRotation(v is Vector, a is Vector, b is Vector) returns Vector
{
    const w = cross(a, b);
    const c = dot(a, b);
    if (norm(w) < 1e-14 || c <= -1 + 1e-12)
    {
        return v;
    }
    return v * c + cross(w, v) + w * dot(w, v) / (1 + c);
}

/**
 * Remove interior knot u up to `times` times (P&T A5.6, one removal per pass). A removal is kept only if the curve
 * moves less than `tol`; the first refused one stops the loop.
 *
 * @returns {{ @field curve {BSplineCurve}, @field removed {number} }}
 */
export function mceRemoveKnot(curve is BSplineCurve, u is number, times is number, tol is ValueWithUnits) returns map
{
    var c = curve;
    var removed = 0;
    for (var t = 0; t < times; t += 1)
    {
        const once = mceRemoveKnotOnce(c, u, tol);
        if (once == undefined)
        {
            break;
        }
        c = once;
        removed += 1;
    }
    return { "curve" : c, "removed" : removed };
}

/**
 * One removal of the interior knot u (Piegl & Tiller A5.6 with num = 1, non-rational), or undefined when u is not an
 * interior knot or the removal would move the curve by more than `tol`.
 * (tools/bspline_knots removeKnotOnce skips the deviation check when first == last, i.e. for a knot of multiplicity p,
 * and keeps only the left-hand new points; hence this local version.)
 */
export function mceRemoveKnotOnce(curve is BSplineCurve, u is number, tol is ValueWithUnits)
{
    const p = curve.degree;
    const knots = curve.knots;
    const points = curve.controlPoints;
    const n = size(points) - 1;
    var r = -1;
    var s = 0;
    for (var i = 0; i < size(knots); i += 1)
    {
        if (knots[i] == u)
        {
            r = i;
            s += 1;
        }
    }
    if (s == 0 || r < p + 1 || r > n)
    {
        return undefined;
    }
    const ord = p + 1;
    const first = r - p;
    const last = r - s;
    const off = first - 1;
    var temp = makeArray(last - off + 2);
    temp[0] = points[off];
    temp[last + 1 - off] = points[last + 1];
    var i = first;
    var j = last;
    var ii = 1;
    var jj = last - off;
    while (j - i > 0)
    {
        const alfi = (u - knots[i]) / (knots[i + ord] - knots[i]);
        const alfj = (u - knots[j]) / (knots[j + ord] - knots[j]);
        temp[ii] = (points[i] - (1 - alfi) * temp[ii - 1]) / alfi;
        temp[jj] = (points[j] - alfj * temp[jj + 1]) / (1 - alfj);
        i += 1;
        ii += 1;
        j -= 1;
        jj -= 1;
    }
    var gap;
    if (j - i < 0)
    {
        gap = norm(temp[ii - 1] - temp[jj + 1]);
    }
    else
    {
        const alfi = (u - knots[i]) / (knots[i + ord] - knots[i]);
        gap = norm(points[i] - (alfi * temp[ii + 1] + (1 - alfi) * temp[ii - 1]));
    }
    if (gap > tol)
    {
        return undefined;
    }
    var updated = points;
    i = first;
    j = last;
    while (j - i > 0)
    {
        updated[i] = temp[i - off];
        updated[j] = temp[j - off];
        i += 1;
        j -= 1;
    }
    const fout = floor((2 * r - s - p) / 2);
    var newPoints = [];
    for (var k = 0; k <= n; k += 1)
    {
        if (k != fout)
        {
            newPoints = append(newPoints, updated[k]);
        }
    }
    var newKnots = [];
    for (var k = 0; k < size(knots); k += 1)
    {
        if (k != r)
        {
            newKnots = append(newKnots, knots[k]);
        }
    }
    return mceCurve(p, newPoints, newKnots);
}

/**
 * Point, first derivative, unit tangent and curvature vector at u = 1.
 */
export function mceEndState(curve is BSplineCurve) returns map
{
    const r = evaluateSpline({ "spline" : curve, "parameters" : [1], "nDerivatives" : 2 });
    const d1 = r[1][0];
    const d2 = r[2][0];
    const tangent = normalize(d1);
    return { "point" : r[0][0], "d1" : d1, "tangent" : tangent, "curvature" : (d2 - dot(d2, tangent) * tangent) / dot(d1, d1) };
}

/**
 * Target tangent T (unit) and curvature vector K (1/length) at the modified end, from the reference.
 * T is oriented along the curve's own approach direction, then reversed by "Opposite direction".
 */
export function mceEndConditions(context is Context, ref is map, curve is BSplineCurve, definition is map, kEnd is number, tol is ValueWithUnits) returns map
{
    var notes = [];
    const state = mceEndState(curve);
    const approach = state.tangent;
    const zeroK = vector(0, 0, 0) / meter;
    var tangent;
    var curvature = zeroK;
    var sideHint = undefined;

    if (ref.kind == "CONNECTOR")
    {
        tangent = ref.frame.zAxis;
        sideHint = ref.frame.xAxis;
        if (kEnd == 2 && definition.modCurvatureMode == EndCurvatureMode.MATCH)
        {
            notes = append(notes, "A mate connector has no curvature: the end curvature is zero.");
        }
    }
    else if (ref.kind == "EDGE")
    {
        const distance = evDistance(context, { "side0" : ref.query, "side1" : state.point });
        if (distance.distance > tol)
        {
            notes = append(notes, "The reference edge passes " ~ mceMm(distance.distance) ~ " from the new end; its tangent at the nearest point is used.");
        }
        const t = distance.sides[0].parameter;
        const result = evEdgeCurvature(context, { "edge" : ref.query, "parameter" : t });
        tangent = result.frame.zAxis;
        if (result.curvature > 1e-9 / meter)
        {
            // Point the normal where the tangent actually turns (does not rely on the frame's x-axis convention).
            var normalDir = result.frame.xAxis;
            const lines = evEdgeTangentLines(context, { "edge" : ref.query, "parameters" : [max(t - 1e-4, 0), min(t + 1e-4, 1)] });
            if (dot(lines[1].direction - lines[0].direction, normalDir) < 0)
            {
                normalDir = -normalDir;
            }
            curvature = result.curvature * normalDir;
            sideHint = normalDir;
        }
    }
    else
    {
        const face = ref.query;
        const distance = evDistance(context, { "side0" : face, "side1" : state.point, "extendSide0" : true });
        if (distance.distance > tol)
        {
            notes = append(notes, "The reference face is " ~ mceMm(distance.distance) ~ " from the new end; its tangent plane at the nearest point is used.");
        }
        const uv = distance.sides[0].parameter;
        const onFace = distance.sides[0].point;
        const faceNormal = evFaceTangentPlane(context, { "face" : face, "parameter" : uv }).normal;
        const principal = evFaceCurvature(context, { "face" : face, "parameter" : uv });
        const inPlane = approach - dot(approach, faceNormal) * faceNormal;
        if (norm(inPlane) < 1e-10)
        {
            tangent = principal.minDirection;
        }
        else
        {
            tangent = normalize(inPlane);
        }
        if (dot(tangent, approach) < 1 - 5e-7)
        {
            notes = append(notes, "The end tangent is the curve's approach direction projected into the face.");
        }
        // Normal curvature from Euler's formula; its side from a probe of the face a short step along the tangent.
        const cosTheta = dot(tangent, principal.minDirection);
        const sinTheta = dot(tangent, principal.maxDirection);
        const kn = abs(principal.minCurvature * cosTheta * cosTheta + principal.maxCurvature * sinTheta * sinTheta);
        var normalPart = zeroK;
        if (kn > 1e-9 / meter)
        {
            const step = min(1 * millimeter, 0.05 / kn);
            const ahead = evDistance(context, { "side0" : face, "side1" : onFace + step * tangent, "extendSide0" : true }).sides[0].point;
            const behind = evDistance(context, { "side0" : face, "side1" : onFace - step * tangent, "extendSide0" : true }).sides[0].point;
            const rise = dot(ahead - onFace, faceNormal) + dot(behind - onFace, faceNormal);
            normalPart = (rise < 0 * meter ? -kn : kn) * faceNormal;
        }
        // The geodesic part (in the face, across the tangent) is kept from the current curve.
        const current = state.curvature;
        const geodesic = current - dot(current, faceNormal) * faceNormal - dot(current, tangent) * tangent;
        curvature = normalPart + geodesic;
    }

    if (dot(tangent, approach) < 0)
    {
        tangent = -tangent;
    }
    if (definition.flipRef)
    {
        tangent = -tangent;
    }
    if (dot(tangent, approach) < 0)
    {
        notes = append(notes, "The end tangent points back against the curve's approach; the curve may hook near the end.");
    }

    if (kEnd == 2 && definition.modCurvatureMode == EndCurvatureMode.ZERO)
    {
        curvature = zeroK;
    }
    else if (kEnd == 2 && definition.modCurvatureMode == EndCurvatureMode.RADIUS)
    {
        var side = sideHint;
        if (side == undefined || norm(side - dot(side, tangent) * tangent) < 1e-6)
        {
            side = undefined;
            const current = state.curvature - dot(state.curvature, tangent) * tangent;
            if (norm(current) > 1e-9 / meter)
            {
                side = normalize(current * meter);
            }
        }
        if (side == undefined)
        {
            throw regenError("The curvature side is undefined (straight end, no curved reference); use a mate connector reference.", ["modEndRadius"]);
        }
        curvature = normalize(side - dot(side, tangent) * tangent) / definition.modEndRadius;
    }
    return { "tangent" : tangent, "curvature" : curvature, "notes" : notes };
}

/**
 * Closed-form end overwrite (modified end at u = 1, clamped, degree p >= 2, knot gaps read after all insertions):
 *   P[n-2] = P[n-1] - a*T
 *   P[n-3] = P[n-2] - beta*T + (p/(p-1)) * (E2/el1) * a^2 * K     (K without its tangential part)
 * with el1 = U[n] - U[n-1], E2 = U[n] - U[n-2], a = the current |P[n-1] - P[n-2]|, beta the tangential part of the
 * old P[n-2] - P[n-3] clamped to [0.25, 4] * a * E2 / el1. Exact in one step, including K = 0 (collinear points).
 */
export function mceEndOverwrite(curve is BSplineCurve, kEnd is number, tangent is Vector, curvature is Vector) returns map
{
    var notes = [];
    const p = curve.degree;
    const knots = curve.knots;
    var points = curve.controlPoints;
    const n = size(points);
    const endPoint = points[n - 1];
    var a = norm(points[n - 1] - points[n - 2]);
    if (kEnd == 1)
    {
        points[n - 2] = endPoint - a * tangent;
        return { "curve" : mceCurve(p, points, knots), "notes" : notes };
    }

    const el1 = knots[n] - knots[n - 1];
    const e2 = knots[n] - knots[n - 2];
    const k = curvature - dot(curvature, tangent) * tangent;
    const oldP2 = points[n - 2];
    const oldP3 = points[n - 3];
    const oldSpacing = norm(oldP3 - oldP2);
    const betaRaw = dot(oldP2 - oldP3, tangent);
    const fullA = a;
    var newP2;
    var newP3;
    for (var attempt = 0; attempt <= 6; attempt += 1)
    {
        const beta = clamp(betaRaw, 0.25 * a * e2 / el1, 4 * a * e2 / el1);
        newP2 = endPoint - a * tangent;
        newP3 = newP2 - beta * tangent + (p / (p - 1)) * (e2 / el1) * a * a * k;
        if (norm(newP3 - oldP3) <= 2 * oldSpacing || attempt == 6)
        {
            break;
        }
        a = 0.7 * a;
    }
    if (a < fullA)
    {
        notes = append(notes, "The end handle was shortened to " ~ roundToPrecision(100 * a / fullA, 1)
                ~ "% to reach the end curvature without a large control-point jump.");
    }
    points[n - 2] = newP2;
    points[n - 3] = newP3;
    return { "curve" : mceCurve(p, points, knots), "notes" : notes };
}

/**
 * Project the part of the curve after `startParam` onto a face and refit it (the only refit in the feature).
 * Projects onto the face's underlying surface (extendSide0): a default plane is a small finite face to evDistance,
 * and a curve running past a trimmed face's edge should still land on its surface (2026-09-26, T38).
 * The held part [0, startParam] is kept exactly and joined back at the hold knot; the fit starts at the hold point
 * with the held curve's derivative there (G1/G2 hold) and, when the end tangent lies in the face, ends on it.
 */
export function mceProjectTail(context is Context, curve is BSplineCurve, face is Query, startParam is number, holdIndex is number,
    kHold is number, kEnd is number, endTangent, endCurvature, numSamples is number, tol is ValueWithUnits) returns map
{
    var notes = [];
    const p = curve.degree;
    const scale = 1 - startParam;
    var params = [];
    var fitParams = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, startParam + scale * i / (numSamples - 1));
        fitParams = append(fitParams, i / (numSamples - 1));
    }
    const samples = evaluateSpline({ "spline" : curve, "parameters" : params, "nDerivatives" : 1 });
    var positions = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        if (i == 0 && startParam > 0)
        {
            positions = append(positions, samples[0][0]);
        }
        else
        {
            positions = append(positions, evDistance(context, { "side0" : face, "side1" : samples[0][i], "extendSide0" : true }).sides[0].point);
        }
    }
    var targetMap = { "positions" : positions };
    if (kHold >= 1)
    {
        var startDerivative = samples[1][0] * scale;
        if (startParam == 0)
        {
            const n0 = mceFaceNormalAt(context, face, positions[0]);
            startDerivative = startDerivative - dot(startDerivative, n0) * n0;
        }
        targetMap.startDerivative = startDerivative;
    }
    if (kEnd >= 1)
    {
        const nEnd = mceFaceNormalAt(context, face, positions[numSamples - 1]);
        if (abs(dot(endTangent, nEnd)) < sin(1e-3 * radian))
        {
            targetMap.endDerivative = endTangent * norm(samples[1][numSamples - 1]) * scale;
        }
        else
        {
            notes = append(notes, "The end reference tangent is not in the projection face; end tangency is not kept after projecting.");
        }
    }
    // Chord-free: the samples keep the curve's own parameters, so the derivative magnitudes are used (correction 23).
    const fit = approximateSpline(context, {
                    "degree" : p,
                    "tolerance" : tol,
                    "isPeriodic" : false,
                    "targets" : [approximationTarget(targetMap)],
                    "parameters" : fitParams,
                    "interpolateIndices" : [0, numSamples - 1],
                    "suppressInterpolationNotice" : true
                })[0];
    var fitPoints = fit.controlPoints;
    fitPoints[0] = positions[0];
    fitPoints[size(fitPoints) - 1] = positions[numSamples - 1];
    var fitCurve = mceCurve(fit.degree, fitPoints, withUnitDomain(fit).knots);
    if (fitCurve.degree < p)
    {
        // The join below needs one degree (approximateSpline may return a lower one for few points).
        const elevated = elevateDegree(context, fitCurve, p - fitCurve.degree);
        fitCurve = mceCurve(elevated.degree, elevated.controlPoints, elevated.knots);
    }

    if (kEnd == 2)
    {
        const achieved = mceEndState(fitCurve).curvature;
        notes = append(notes, "End curvature cannot be held on a projected curve (end curvature off by "
                ~ roundToPrecision(norm(achieved - endCurvature) * meter, 4) ~ " /m).");
    }
    if (startParam == 0)
    {
        return { "curve" : fitCurve, "notes" : notes };
    }
    if (kHold == 2)
    {
        notes = append(notes, "Projection keeps G1 at the hold, not G2.");
    }

    // Join: held knots up to (and including) the p copies of the hold knot, then the fit's knots mapped onto [hold, 1].
    var knots = [];
    const lastAtHold = holdIndex + p;
    for (var i = 0; i <= lastAtHold; i += 1)
    {
        knots = append(knots, curve.knots[i]);
    }
    for (var i = fitCurve.degree + 1; i < size(fitCurve.knots); i += 1)
    {
        knots = append(knots, startParam + scale * fitCurve.knots[i]);
    }
    var points = [];
    for (var i = 0; i <= holdIndex; i += 1)
    {
        points = append(points, curve.controlPoints[i]);
    }
    for (var i = 1; i < size(fitCurve.controlPoints); i += 1)
    {
        points = append(points, fitCurve.controlPoints[i]);
    }
    return { "curve" : mceCurve(p, points, knots), "notes" : notes };
}

/** Normal of a face at the point of it nearest `point`. */
export function mceFaceNormalAt(context is Context, face is Query, point is Vector) returns Vector
{
    const d = evDistance(context, { "side0" : face, "side1" : point, "extendSide0" : true });
    return evFaceTangentPlane(context, { "face" : face, "parameter" : d.sides[0].parameter }).normal;
}

/**
 * True when the curve's derivative reverses between neighbouring samples after `fromParam` (a loop or cusp).
 */
export function mceHasLoop(curve is BSplineCurve, fromParam is number) returns boolean
{
    var params = [];
    for (var i = 0; i <= 100; i += 1)
    {
        params = append(params, fromParam + (1 - fromParam) * i / 100);
    }
    const d1 = evaluateSpline({ "spline" : curve, "parameters" : params, "nDerivatives" : 1 })[1];
    for (var i = 1; i < size(d1); i += 1)
    {
        if (dot(d1[i], d1[i - 1]) <= 0 * meter * meter)
        {
            return true;
        }
    }
    return false;
}

// ============================================================================
// Arc length and projection on a B-spline
// ============================================================================

/**
 * Cumulative arc length at breakpoints (uniform spans plus every distinct knot), each span integrated with
 * 5-point Gauss-Legendre. Knot insertion does not change the parameterization, so the table stays valid.
 *
 * @returns {{ @field params {array}, @field lengths {array} }}
 */
export function mceArcLengthTable(curve is BSplineCurve) returns map
{
    var breaks = [];
    for (var i = 0; i <= MCE_ARC_LENGTH_SPANS; i += 1)
    {
        breaks = append(breaks, i / MCE_ARC_LENGTH_SPANS);
    }
    for (var knot in curve.knots)
    {
        if (knot > 0 && knot < 1)
        {
            breaks = append(breaks, knot);
        }
    }
    breaks = sort(breaks, function(a, b) { return a - b; });
    var params = [breaks[0]];
    for (var i = 1; i < size(breaks); i += 1)
    {
        if (breaks[i] - params[size(params) - 1] > 1e-12)
        {
            params = append(params, breaks[i]);
        }
    }
    var gaussParams = [];
    for (var i = 0; i < size(params) - 1; i += 1)
    {
        gaussParams = concatenateArrays([gaussParams, mceGaussPoints(params[i], params[i + 1])]);
    }
    const speeds = evaluateSpline({ "spline" : curve, "parameters" : gaussParams, "nDerivatives" : 1 })[1];
    var lengths = [0 * meter];
    for (var i = 0; i < size(params) - 1; i += 1)
    {
        var sum = 0 * meter;
        for (var g = 0; g < 5; g += 1)
        {
            sum += MCE_GAUSS_WEIGHTS[g] * norm(speeds[5 * i + g]);
        }
        lengths = append(lengths, lengths[i] + sum * (params[i + 1] - params[i]) / 2);
    }
    return { "params" : params, "lengths" : lengths };
}

/** The 5 Gauss-Legendre points of [a, b]. */
export function mceGaussPoints(a is number, b is number) returns array
{
    var out = [];
    for (var node in MCE_GAUSS_NODES)
    {
        out = append(out, (a + b) / 2 + node * (b - a) / 2);
    }
    return out;
}

/** Index of the table span holding u. */
export function mceSpanOf(table is map, u is number) returns number
{
    const params = table.params;
    var i = 0;
    while (i < size(params) - 2 && params[i + 1] <= u)
    {
        i += 1;
    }
    return i;
}

/** Arc length from u = 0 to each parameter (one evaluateSpline call for all). */
export function mceLengthsAt(curve is BSplineCurve, table is map, params is array) returns array
{
    var spans = [];
    var gaussParams = [];
    for (var u in params)
    {
        const i = mceSpanOf(table, u);
        spans = append(spans, i);
        gaussParams = concatenateArrays([gaussParams, mceGaussPoints(table.params[i], u)]);
    }
    const speeds = evaluateSpline({ "spline" : curve, "parameters" : gaussParams, "nDerivatives" : 1 })[1];
    var out = [];
    for (var j = 0; j < size(params); j += 1)
    {
        const i = spans[j];
        var sum = 0 * meter;
        for (var g = 0; g < 5; g += 1)
        {
            sum += MCE_GAUSS_WEIGHTS[g] * norm(speeds[5 * j + g]);
        }
        out = append(out, table.lengths[i] + sum * (params[j] - table.params[i]) / 2);
    }
    return out;
}

/** Parameter at arc length s from u = 0 (table span, then Newton on the Gauss-integrated length). */
export function mceParamAtLength(curve is BSplineCurve, table is map, s is ValueWithUnits) returns number
{
    const params = table.params;
    const lengths = table.lengths;
    var i = 0;
    while (i < size(params) - 2 && lengths[i + 1] <= s)
    {
        i += 1;
    }
    const u0 = params[i];
    const u1 = params[i + 1];
    var u = u0 + (u1 - u0) * clamp((s - lengths[i]) / (lengths[i + 1] - lengths[i]), 0, 1);
    for (var iter = 0; iter < 6; iter += 1)
    {
        const evalParams = append(mceGaussPoints(u0, u), u);
        const d1 = evaluateSpline({ "spline" : curve, "parameters" : evalParams, "nDerivatives" : 1 })[1];
        var sum = 0 * meter;
        for (var g = 0; g < 5; g += 1)
        {
            sum += MCE_GAUSS_WEIGHTS[g] * norm(d1[g]);
        }
        const error = lengths[i] + sum * (u - u0) / 2 - s;
        const step = error / norm(d1[5]);
        u = clamp(u - step, u0, u1);
        if (abs(step) < 1e-13)
        {
            break;
        }
    }
    return u;
}

/**
 * Parameter of the curve point nearest `point` (200 samples, then Newton on (C - P) . C').
 *
 * @returns {{ @field parameter {number}, @field distance {ValueWithUnits} }}
 */
export function mceProjectParam(curve is BSplineCurve, point is Vector) returns map
{
    var params = [];
    for (var i = 0; i <= 200; i += 1)
    {
        params = append(params, i / 200);
    }
    const positions = evaluateSpline({ "spline" : curve, "parameters" : params })[0];
    var best = 0;
    for (var i = 1; i < size(positions); i += 1)
    {
        if (norm(positions[i] - point) < norm(positions[best] - point))
        {
            best = i;
        }
    }
    var u = params[best];
    for (var iter = 0; iter < 20; iter += 1)
    {
        const r = evaluateSpline({ "spline" : curve, "parameters" : [u], "nDerivatives" : 2 });
        const diff = r[0][0] - point;
        const f = dot(diff, r[1][0]);
        const fp = dot(r[1][0], r[1][0]) + dot(diff, r[2][0]);
        if (fp <= 0 * meter * meter)
        {
            break;
        }
        const step = f / fp;
        u = clamp(u - step, 0, 1);
        if (abs(step) < 1e-13)
        {
            break;
        }
    }
    const at = evaluateSpline({ "spline" : curve, "parameters" : [u] })[0][0];
    return { "parameter" : u, "distance" : norm(at - point) };
}

// ============================================================================
// Legacy API (sample + refit). Kept because other tabs pin this module; the feature above no longer uses it.
// ============================================================================

/**
 * Modify a curve by displacing one endpoint with smooth transition to the fixed end.
 *
 * @param context {Context}
 * @param inputCurve {BSplineCurve} : Curve to modify
 * @param modPointParam {number} : 0 or 1 - which endpoint to displace
 * @param offsetVector {Vector} : Displacement at modified endpoint (with length units)
 * @param offsetFrame {OffsetFrame} : WORLD or FRENET [tangent, normal, binormal]
 * @param transitionType {TransitionType} : LINEAR, SINUSOIDAL, or LOGISTIC
 * @param fixedEndContinuity {GeometricContinuity} : G0, G1, or G2 constraint at fixed end
 * @param g2Mode {G2Mode} : EXACT or BEST_EFFORT (only used for G2)
 * @param modPointRef {Query} : Reference edge or face for continuity at modified end (can be empty)
 * @param modPointContinuity {GeometricContinuity} : G0, G1, or G2 constraint at modified end
 * @param numSamples {number} : Sample count for fitting
 * @param degree {number} : Output curve degree
 * @param tolerance {ValueWithUnits} : Fitting tolerance
 * @returns {BSplineCurve}
 */
export function modifyCurveEnd(
    context is Context,
    inputCurve is BSplineCurve,
    modPointParam is number,
    offsetVector is Vector,
    offsetFrame is OffsetFrame,
    transitionType is TransitionType,
    fixedEndContinuity is GeometricContinuity,
    g2Mode is G2Mode,
    modPointRef is Query,
    modPointContinuity is GeometricContinuity,
    numSamples is number,
    degree is number,
    tolerance is ValueWithUnits
) returns BSplineCurve
{
    return modifyCurveEnd(context, inputCurve, modPointParam, offsetVector, offsetFrame, transitionType,
        fixedEndContinuity, g2Mode, modPointRef, modPointContinuity, false, numSamples, degree, tolerance);
}

/**
 * modifyCurveEnd with the reference tangent optionally reversed ("Flip ref").
 */
export function modifyCurveEnd(
    context is Context,
    inputCurve is BSplineCurve,
    modPointParam is number,
    offsetVector is Vector,
    offsetFrame is OffsetFrame,
    transitionType is TransitionType,
    fixedEndContinuity is GeometricContinuity,
    g2Mode is G2Mode,
    modPointRef is Query,
    modPointContinuity is GeometricContinuity,
    flipRef is boolean,
    numSamples is number,
    degree is number,
    tolerance is ValueWithUnits
) returns BSplineCurve
{
    var fixedPointParam = (modPointParam == 0) ? 1 : 0;

    // Store original endpoint data for continuity enforcement later
    var fixedEndFrameOriginal = computeFrenetFrame(inputCurve, fixedPointParam);
    var fixedEndTangent = fixedEndFrameOriginal.frame.zAxis;
    var fixedEndCurvature = fixedEndFrameOriginal.curvature;

    // Compute world offset direction once from the Frenet offset at modParam
    var worldOffsetAtModPoint;
    if (offsetFrame == OffsetFrame.FRENET)
    {
        var frenetFrameAtMod = computeFrenetFrame(inputCurve, modPointParam);
        worldOffsetAtModPoint = frenetVectorToWorld(offsetVector, frenetFrameAtMod);
    }
    else
    {
        worldOffsetAtModPoint = offsetVector;
    }

    // Sample and offset points
    var modifiedPoints = [];

    for (var i = 0; i < numSamples; i += 1)
    {
        var s = i / (numSamples - 1);

        // Compute scale factor: 0 at fixed end, 1 at modPoint
        var sf;
        if (modPointParam == 1)
        {
            sf = computeAppliedSF(s, 0, 1, transitionType);
        }
        else
        {
            sf = computeAppliedSF(s, 1, 0, transitionType);
        }

        var originalPt = evaluateSpline({
                    "spline" : inputCurve,
                    "parameters" : [s]
                })[0][0];

        var worldOffset = sf * worldOffsetAtModPoint;

        modifiedPoints = append(modifiedPoints, originalPt + worldOffset);
    }

    // Build parameter array
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        params = append(params, i / (numSamples - 1));
    }

    // Fit initial curve
    var fittedCurve = approximateSpline(context, {
                "degree" : degree,
                "tolerance" : tolerance,
                "isPeriodic" : false,
                "targets" : [approximationTarget({ "positions" : modifiedPoints })],
                "parameters" : params,
                "interpolateIndices" : [0, numSamples - 1]
            })[0];

    // Apply continuity constraints at fixed end
    if (fixedEndContinuity == GeometricContinuity.G1 || fixedEndContinuity == GeometricContinuity.G2)
    {
        fittedCurve = enforceG1AtEnd(fittedCurve, fixedPointParam, fixedEndTangent);
    }

    if (fixedEndContinuity == GeometricContinuity.G2)
    {
        fittedCurve = enforceG2AtEnd(fittedCurve, fixedPointParam, fixedEndCurvature, g2Mode);
    }

    // Apply constraints at modified end (if reference supplied)
    if (!isQueryEmpty(context, modPointRef) && modPointContinuity != GeometricContinuity.G0)
    {
        var constraints = computeRefContinuityConstraints(context, modPointRef, fittedCurve, modPointParam);
        if (flipRef)
        {
            constraints.tangent = -constraints.tangent;
        }

        if (modPointContinuity == GeometricContinuity.G1 || modPointContinuity == GeometricContinuity.G2)
        {
            fittedCurve = enforceG1AtEnd(fittedCurve, modPointParam, constraints.tangent);
        }

        if (modPointContinuity == GeometricContinuity.G2)
        {
            fittedCurve = enforceG2AtEnd(fittedCurve, modPointParam, constraints.curvature, g2Mode);
        }
    }

    return fittedCurve;
}

/**
 * Compute tangent and curvature constraints from a reference edge or face.
 *
 * @param context {Context}
 * @param ref {Query} : Reference edge or face
 * @param curve {BSplineCurve} : The curve being constrained (used for approach direction on faces)
 * @param endParam {number} : 0 or 1 - which endpoint
 * @returns {map} : { "tangent": Vector, "curvature": ValueWithUnits }
 */
export function computeRefContinuityConstraints(context is Context, ref is Query, curve is BSplineCurve, endParam is number) returns map
{
    // Get endpoint position
    var endPoint = evaluateSpline({
                "spline" : curve,
                "parameters" : [endParam]
            })[0][0];

    // Check if reference is edge or face
    var edgeQuery = qEntityFilter(ref, EntityType.EDGE);
    var faceQuery = qEntityFilter(ref, EntityType.FACE);

    if (!isQueryEmpty(context, edgeQuery))
    {
        return computeEdgeContinuityConstraints(context, edgeQuery, endPoint);
    }
    else if (!isQueryEmpty(context, faceQuery))
    {
        return computeFaceContinuityConstraints(context, faceQuery, curve, endParam);
    }
    else
    {
        // Fallback - return curve's own tangent/curvature (no constraint)
        var frame = computeFrenetFrame(curve, endParam);
        return {
                "tangent" : frame.frame.zAxis,
                "curvature" : frame.curvature
            };
    }
}

/**
 * Compute tangent and curvature from a reference edge at a point.
 */
export function computeEdgeContinuityConstraints(context is Context, edge is Query, point is Vector) returns map
{
    // Find parameter on edge closest to point
    var distResult = evDistance(context, {
            "side0" : edge,
            "side1" : point
        });

    var edgeParam = distResult.sides[0].parameter;

    // Get edge as BSpline and compute Frenet frame
    var edgeCurve = evApproximateBSplineCurve(context, { "edge" : edge, "forceNonRational" : true });
    var frame = computeFrenetFrame(edgeCurve, edgeParam);

    return {
            "tangent" : frame.frame.zAxis,
            "curvature" : frame.curvature
        };
}

/**
 * Compute tangent and curvature for a curve meeting a face.
 * Projects curve's approach direction onto tangent plane,
 * then computes surface curvature in that direction.
 */
export function computeFaceContinuityConstraints(context is Context, face is Query, curve is BSplineCurve, endParam is number) returns map
{
    // Get endpoint position
    var endPoint = evaluateSpline({
                "spline" : curve,
                "parameters" : [endParam]
            })[0][0];

    // Get curve's approach direction (tangent at endpoint)
    var curveFrame = computeFrenetFrame(curve, endParam);
    var approachDirection = curveFrame.frame.zAxis;

    // Find UV parameter on face
    var distResult = evDistance(context, {
            "side0" : face,
            "side1" : endPoint
        });
    var uvParam = distResult.sides[0].parameter;

    // Get face normal at that point
    var tangentPlane = evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : uvParam
        });
    var faceNormal = tangentPlane.normal;

    // Project approach direction onto tangent plane
    var projected = approachDirection - dot(approachDirection, faceNormal) * faceNormal;
    var projNorm = norm(projected);

    var tangent;
    if (projNorm < 1e-10)
    {
        // Approach is perpendicular to face - pick arbitrary direction in tangent plane
        // Use face's principal direction as fallback
        var faceCurvature = evFaceCurvature(context, {
                "face" : face,
                "parameter" : uvParam
            });
        tangent = faceCurvature.minDirection;
    }
    else
    {
        tangent = projected / projNorm;
    }

    // Get face curvature and compute curvature in tangent direction (Euler's formula)
    var faceCurvature = evFaceCurvature(context, {
            "face" : face,
            "parameter" : uvParam
        });

    var cosTheta = dot(tangent, faceCurvature.minDirection);
    var sinTheta = dot(tangent, faceCurvature.maxDirection);
    var curvature = faceCurvature.minCurvature * cosTheta * cosTheta
        + faceCurvature.maxCurvature * sinTheta * sinTheta;

    return {
            "tangent" : tangent,
            "curvature" : curvature
        };
}

/**
 * Adjust control points to enforce tangent direction at an endpoint.
 *
 * @param curve {BSplineCurve}
 * @param endParam {number} : 0 or 1
 * @param targetTangent {Vector} : Desired tangent direction (will be normalized)
 * @returns {BSplineCurve} : Modified curve
 */
export function enforceG1AtEnd(curve is BSplineCurve, endParam is number, targetTangent is Vector) returns BSplineCurve
{
    var cps = curve.controlPoints;
    var n = size(cps);

    // Get current tangent at endpoint
    var currentFrame = computeFrenetFrame(curve, endParam);
    var currentTangent = currentFrame.frame.zAxis;

    // Check sign - flip target if needed
    var normalizedTarget = normalize(targetTangent);
    if (dot(currentTangent, normalizedTarget) < 0)
    {
        normalizedTarget = -normalizedTarget;
    }

    // For a clamped B-spline, tangent at endpoint is proportional to
    // the vector from first to second control point (for param 0)
    // or second-to-last to last control point (for param 1)

    var newCPs = cps;  // Copy

    if (endParam == 0)
    {
        // Tangent at s=0 is along (cp[1] - cp[0]); keep cp[0] (the end position), move cp[1].
        var currentVec = cps[1] - cps[0];
        var dist = norm(currentVec);
        newCPs[1] = cps[0] + dist * normalizedTarget;
    }
    else  // endParam == 1
    {
        // Tangent at s=1 points FROM cp[n-2] TO cp[n-1]; keep cp[n-1], move cp[n-2].
        var currentVec = cps[n - 1] - cps[n - 2];
        var dist = norm(currentVec);
        newCPs[n - 2] = cps[n - 1] - dist * normalizedTarget;
    }

    return bSplineCurve({
                "degree" : curve.degree,
                "isPeriodic" : curve.isPeriodic,
                "isRational" : curve.isRational,
                "controlPoints" : newCPs,
                "knots" : curve.knots,
                "weights" : curve.weights
            });
}

/**
 * Adjust control points to approximate a curvature at an endpoint (scales the normal offset of the third control point).
 * Used by simplifySurface. EXACT has no implementation of its own and behaves as BEST_EFFORT (tools review 2026-09-25);
 * Modify curve end itself now sets the end exactly with mceEndOverwrite.
 *
 * @param curve {BSplineCurve}
 * @param endParam {number} : 0 or 1
 * @param targetCurvature {ValueWithUnits} : Desired curvature (1/length units)
 * @param g2Mode {G2Mode} : EXACT or BEST_EFFORT (same behaviour)
 * @returns {BSplineCurve} : Modified curve
 */
export function enforceG2AtEnd(curve is BSplineCurve, endParam is number, targetCurvature is ValueWithUnits, g2Mode is G2Mode) returns BSplineCurve
{
    var cps = curve.controlPoints;
    var n = size(cps);
    var degree = curve.degree;

    if (degree < 3)
    {
        // Can't enforce G2 on degree < 3 curve
        return curve;
    }

    var newCPs = cps;

    // Current curvature
    var currentFrame = computeFrenetFrame(curve, endParam);
    var currentCurvature = currentFrame.curvature;

    if (endParam == 0)
    {
        // Move cp[2] toward/away from the tangent line to adjust curvature
        var tangentDir = normalize(cps[1] - cps[0]);
        var toCP2 = cps[2] - cps[1];

        // Component perpendicular to tangent affects curvature
        var perpComponent = toCP2 - dot(toCP2, tangentDir) * tangentDir;
        var perpDist = norm(perpComponent);

        if (perpDist > 1e-10 * meter)
        {
            var perpDir = perpComponent / perpDist;

            // Scale the perpendicular distance by the curvature ratio (clamped against wild adjustments)
            var curvatureRatio = (targetCurvature / currentCurvature);
            curvatureRatio = min(max(curvatureRatio, 0.1), 10);

            var newPerpDist = perpDist * curvatureRatio;
            var parallelComponent = dot(toCP2, tangentDir) * tangentDir;

            newCPs[2] = cps[1] + parallelComponent + newPerpDist * perpDir;
        }
    }
    else  // endParam == 1
    {
        var tangentDir = normalize(cps[n - 1] - cps[n - 2]);
        var toCP = cps[n - 3] - cps[n - 2];

        var perpComponent = toCP - dot(toCP, tangentDir) * tangentDir;
        var perpDist = norm(perpComponent);

        if (perpDist > 1e-10 * meter)
        {
            var perpDir = perpComponent / perpDist;
            var curvatureRatio = (targetCurvature / currentCurvature);
            curvatureRatio = min(max(curvatureRatio, 0.1), 10);

            var newPerpDist = perpDist * curvatureRatio;
            var parallelComponent = dot(toCP, tangentDir) * tangentDir;

            newCPs[n - 3] = cps[n - 2] + parallelComponent + newPerpDist * perpDir;
        }
    }

    return bSplineCurve({
                "degree" : curve.degree,
                "isPeriodic" : curve.isPeriodic,
                "isRational" : curve.isRational,
                "controlPoints" : newCPs,
                "knots" : curve.knots,
                "weights" : curve.weights
            });
}

/**
 * The same curve with its knot vector rescaled to [0, 1] (the shape is unchanged).
 */
export function withUnitDomain(curve is BSplineCurve) returns BSplineCurve
{
    var knots = curve.knots;
    var k0 = knots[0];
    var k1 = knots[size(knots) - 1];
    if (k0 == 0 && k1 == 1)
    {
        return curve;
    }
    var scaled = [];
    for (var k in knots)
    {
        scaled = append(scaled, (k - k0) / (k1 - k0));
    }
    var params = {
        "degree" : curve.degree,
        "controlPoints" : curve.controlPoints,
        "knots" : scaled as KnotArray,
        "isPeriodic" : curve.isPeriodic
    };
    if (curve.isRational == true)
    {
        params.isRational = true;
        params.weights = curve.weights;
    }
    return bSplineCurve(params);
}
