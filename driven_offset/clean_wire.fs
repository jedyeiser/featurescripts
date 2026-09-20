FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

// edge_offset_utils: the fitter (approximateFamily), emitters, classifiers, the
// approximation predicate and bounds, formatting helpers. export import so the enums
// declared there are reachable as feature parameter types.
export import(path : "a2665e22c07b7a6929ce4e80", version : "");
// design_map_query_utils: embedVariableMap and the extractable wrappers.
import(path : "2b6b313ac740a0146d5bef7c", version : "");

/**
 * Clean wire: one wire in, one wire out, the same shape with fewer edges and fewer
 * control points.
 *
 * This is the manual Wire_Smoothing recipe as a feature: split a wire at the vertices
 * worth keeping, Edit-curve each piece to one spline under its own control-point budget
 * with the end tangents held, leave the exact pieces alone, composite the lot. Here the
 * pieces are found for you -- every joint of the chain is classified by the angle the
 * two edges meet at -- and a GROUP (contiguous edges, its own approximation) is how you
 * say "fit this stretch as one, with this budget", which is what the manual splits at
 * tangent joints were for.
 *
 * Joints
 *   <= G1_JUNCTION_ANGLE (0.57 deg)          tangent: merged into one run
 *   up to the corner angle                    nearly tangent: merged if "Make nearly
 *                                             tangent joints tangent", else a corner
 *   > corner angle                            a corner: the vertex is kept
 *   A sliver edge is judged by its neighbours across it: absorbed into the run when they
 *   are tangent, kept as its own exact piece (it is a chamfer or a fillet) when not.
 *
 * Runs
 *   A run of LINE / CIRCLE source edges is copied exactly, so it keeps its type and its
 *   radius. Any other run is sampled at a curvature-adaptive spacing and fitted as one
 *   cubic with chord-length parameters and the kernel's own end tangents, then measured.
 *
 * Output
 *   One wire body, its edges created under ids that count runs from the chain's start,
 *   so downstream references survive an upstream edit that does not change the corner
 *   set. Reference the BODY (or the published query variable), never its edges.
 *
 * Not yet: closed loops, arc recognition inside splines, exact-merge fallback, the
 * "Suggest groups" editing logic. See research_clean_wire.md.
 */

// ============================================================================
// Bounds and enums
// ============================================================================

/**
 * Joints sharper than this are corners. Below it, down to G1_JUNCTION_ANGLE, a joint is
 * "nearly tangent". 3 degrees keeps the 4.8 degree pitch creases at the ramp ends of the
 * ski's rout intersection as corners, which is what the manual splits did.
 */
export const CleanWireCornerBounds = { (degree) : [0.1, 3, 90], (radian) : 0.05 } as AngleBoundSpec;

/** Per-group approximation override. */
export enum CleanApproximation
{
    annotation { "Name" : "Global" }
    GLOBAL,
    annotation { "Name" : "Max control points" }
    MAX_CP,
    annotation { "Name" : "Tolerance" }
    TOLERANCE
}

/**
 * Edges shorter than this are slivers: fragments whose own end tangents mean nothing.
 * Judged by the neighbours across them. A multiple of the tolerance with a floor: at
 * 0.01 mm the floor rules, at coarser tolerances the multiple does.
 */
export const SLIVER_TOLERANCE_MULTIPLE = 50;
export const SLIVER_MIN_LENGTH = 0.5 * millimeter;

/** Curvature stations per edge for the sampling density. */
export const CURVATURE_STATIONS = 32;

/** Bounds on the sample spacing, see runSamples. */
export const SAMPLE_MIN_SPACING = 0.05 * millimeter;
export const SAMPLE_MAX_SPACING = 10 * millimeter;
export const SAMPLE_MIN_PER_EDGE = 3;
export const SAMPLE_MAX_PER_EDGE = 200;

/** The chain is ordered by endpoint coincidence at this tolerance (merge_curve's value). */
export const CLEAN_CHAIN_TOLERANCE = 1e-5 * meter;

// ============================================================================
// Feature
// ============================================================================

annotation { "Feature Type Name" : "Clean wire",
        "Feature Type Description" : "Rebuild a wire with fewer edges and control points: tangent stretches become one spline each, corners are kept, exact lines and arcs pass through." }
export const cleanWire = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Wire", "Filter" : (EntityType.EDGE || BodyType.WIRE) && ConstructionObject.NO, "Description" : "The wire body, or edges forming one chain, to clean." }
        definition.sourceEdges is Query;

        annotation { "Name" : "Name", "Description" : "Names the output wire. Clear it to leave it unnamed." }
        definition.outputName is string;

        annotation { "Name" : "Groups", "Item name" : "group", "Item label template" : "#cw_name", "Description" : "A stretch of contiguous edges fitted as one curve under its own approximation. The ends of a group are always kept as vertices." }
        definition.groups is array;
        for (var entry in definition.groups)
        {
            annotation { "Name" : "Edges", "Filter" : EntityType.EDGE && ConstructionObject.NO, "Description" : "Contiguous edges of the wire, fitted as one curve." }
            entry.cw_edges is Query;

            annotation { "Name" : "Name" }
            entry.cw_name is string;

            annotation { "Name" : "Approximation", "Default" : CleanApproximation.GLOBAL, "UIHint" : [UIHint.HORIZONTAL_ENUM, UIHint.SHOW_LABEL], "Description" : "Global: the approximation parameters below. Otherwise this group's own control-point cap or tolerance." }
            entry.cw_mode is CleanApproximation;

            if (entry.cw_mode == CleanApproximation.MAX_CP)
            {
                annotation { "Name" : "Maximum control points" }
                isInteger(entry.cw_maxCPs, DrivenOffsetMaxCPBounds);
            }

            if (entry.cw_mode == CleanApproximation.TOLERANCE)
            {
                annotation { "Name" : "Tolerance" }
                isLength(entry.cw_tolerance, TOLERANCE_BOUND);
            }
        }

        annotation { "Name" : "Corner angle", "Description" : "Joints where the edges meet at more than this angle are corners and keep their vertex. Below it a joint is nearly tangent." }
        isAngle(definition.cornerAngle, CleanWireCornerBounds);

        annotation { "Name" : "Make nearly tangent joints tangent", "Default" : true, "Description" : "A joint between the tangent threshold (0.57 degrees) and the corner angle is fitted through, which makes it exactly tangent within the tolerance. Off, such joints are corners." }
        definition.forceTangency is boolean;

        annotation { "Group Name" : "Approximation parameters", "Collapsed By Default" : true }
        {
            drivenOffsetApproximationPredicate(definition);
        }

        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print joints", "Default" : false, "Description" : "Every edge of the chain with its length and type, and every joint with its angle and what was decided about it." }
            definition.debugPrintJoints is boolean;

            annotation { "Name" : "Print runs", "Default" : false, "Description" : "Every run: its edges, samples, the fit's control points and the measured deviation." }
            definition.debugPrintRuns is boolean;

            annotation { "Name" : "Show corners", "Default" : false, "Description" : "Mark every kept vertex." }
            definition.debugShowCorners is boolean;

            annotation { "Name" : "Keep pieces", "Default" : false, "Description" : "Leave the per-run curves in the result beside the wire." }
            definition.debugKeepPieces is boolean;
        }
    }
    {
        const edges = expandEdgeQuery(definition.sourceEdges);
        if (isQueryEmpty(context, edges))
        {
            throw regenError("Select a wire or the edges of one chain.", ["sourceEdges"]);
        }

        const chain = describeChain(context, edges);
        if (chain.closed)
        {
            throw regenError("Closed loops are not supported yet; split the wire once first.", ["sourceEdges"]);
        }

        const approximation = approximationSettings(definition);
        const joints = classifyJoints(context, definition, chain);
        const grouped = applyGroups(context, definition, chain, joints);
        const runs = buildRuns(chain, grouped.joints, grouped.groupOfEdge);

        if (definition.debugPrintJoints)
        {
            printChain(chain, grouped.joints, grouped.groupOfEdge);
        }

        var created = [];
        var reports = [];
        for (var k = 0; k < size(runs); k += 1)
        {
            const runId = id + ("run" ~ k);
            const report = emitRun(context, runId, definition, chain, runs[k], approximation);
            created = append(created, qCreatedBy(runId, EntityType.EDGE));
            reports = append(reports, report);
        }

        if (definition.debugPrintRuns)
        {
            printRuns(reports);
        }

        const wireId = id + "wire";
        opExtractWires(context, wireId, { "edges" : qUnion(created) });

        if (!definition.debugKeepPieces)
        {
            opDeleteBodies(context, id + "cleanup", { "entities" : qOwnerBody(qUnion(created)) });
        }

        const wire = qCreatedBy(wireId, EntityType.BODY);
        if (definition.outputName != "")
        {
            setProperty(context, { "entities" : wire, "propertyType" : PropertyType.NAME, "value" : definition.outputName });
        }

        if (definition.debugShowCorners)
        {
            showCorners(context, chain, grouped.joints);
        }

        reportOutcome(context, id, chain, grouped.joints, runs, reports);

        embedVariableMap(context, id, {
                    "variable" : {
                        "edgeCount" : extractableVariable(size(runs), "Edges in the cleaned wire."),
                        "sourceEdgeCount" : extractableVariable(size(chain.edges), "Edges in the wire that was cleaned.")
                    },
                    "query" : {
                        "cleanWire" : extractableQuery(wire, "The cleaned wire.", DebugColor.GREEN),
                        "cleanEdges" : extractableQuery(qOwnedByBody(wire, EntityType.EDGE), "Edges of the cleaned wire."),
                        "sourceEdges" : extractableQuery(definition.sourceEdges, "The wire that was cleaned.", DebugColor.BLUE)
                    }
                });
    }, {
        "outputName" : "",
        "groups" : [],
        "cornerAngle" : 3 * degree,
        "forceTangency" : true,
        "approximationDegree" : 3,
        "approximationTolerance" : 1e-5 * meter,
        "approximationMaxCPs" : 30,
        "debugPrintJoints" : false,
        "debugPrintRuns" : false,
        "debugShowCorners" : false,
        "debugKeepPieces" : false
    });

/**
 * The three fitter settings, as the utils fitter reads them.
 */
function approximationSettings(definition is map) returns map
{
    return {
            "approximationDegree" : definition.approximationDegree,
            "approximationTolerance" : definition.approximationTolerance,
            "approximationMaxCPs" : definition.approximationMaxCPs,
            "debugFit" : definition.debugPrintRuns
        };
}

// ============================================================================
// Chain
// ============================================================================

/**
 * The edges in chain order, each described once with batched kernel calls.
 *
 * Every direction below runs ALONG THE CHAIN: an edge the path traverses backwards has
 * its kernel tangents negated and its ends swapped, so a joint is always "the end of
 * the previous edge against the start of the next".
 *
 * @returns {map} : { "edges" : [{ query, flipped, length, curveType, startPoint, endPoint,
 *      startTangent, endTangent, curvatures (CURVATURE_STATIONS values along the chain) }],
 *      "closed" : boolean }
 */
function describeChain(context is Context, edges is Query) returns map
{
    var path;
    try silent
    {
        path = constructPath(context, edges, { "tolerance" : CLEAN_CHAIN_TOLERANCE }).path;
    }
    catch
    {
        throw regenError("The selected edges do not form one chain; they must meet end to end without branching.", edges);
    }

    var stationParams = [];
    for (var s = 0; s < CURVATURE_STATIONS; s += 1)
    {
        stationParams = append(stationParams, s / (CURVATURE_STATIONS - 1));
    }

    var described = [];
    for (var i = 0; i < size(path.edges); i += 1)
    {
        const edge = path.edges[i];
        const flipped = path.flipped[i];
        const ends = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0, 1] });
        const definition = evCurveDefinition(context, { "edge" : edge, "returnBSplinesAsOther" : true });
        const length = evLength(context, { "entities" : edge });
        const curvatureResults = evEdgeCurvatures(context, { "edge" : edge, "parameters" : stationParams });

        var curvatures = [];
        for (var s = 0; s < CURVATURE_STATIONS; s += 1)
        {
            const at = flipped ? CURVATURE_STATIONS - 1 - s : s;
            curvatures = append(curvatures, curvatureResults[at].curvature);
        }

        described = append(described, {
                    "query" : edge,
                    "flipped" : flipped,
                    "length" : length,
                    "curveType" : definition.curveType,
                    "startPoint" : flipped ? ends[1].origin : ends[0].origin,
                    "endPoint" : flipped ? ends[0].origin : ends[1].origin,
                    "startTangent" : flipped ? -1 * ends[1].direction : ends[0].direction,
                    "endTangent" : flipped ? -1 * ends[0].direction : ends[1].direction,
                    "curvatures" : curvatures
                });
    }

    return { "edges" : described, "closed" : path.closed };
}

// ============================================================================
// Joints
// ============================================================================

/**
 * One record per joint (between edge j - 1 and edge j), decided from the angle the two
 * edges meet at. A sliver's own tangents are not consulted: the joints on either side of
 * it are decided together from the edges beyond it.
 *
 * @returns {array} : size(edges) - 1 records { "angle" (radians), "kind" ("tangent" /
 *      "near" / "corner"), "isBreak" : boolean, "sliver" : boolean, "why" : string }
 */
function classifyJoints(context is Context, definition is map, chain is map) returns array
{
    const edges = chain.edges;
    const sliverMax = max(SLIVER_MIN_LENGTH, SLIVER_TOLERANCE_MULTIPLE * definition.approximationTolerance);

    var joints = [];
    for (var j = 1; j < size(edges); j += 1)
    {
        joints = append(joints, jointRecord(definition, edges[j - 1].endTangent, edges[j].startTangent, false, ""));
    }

    // Slivers: the joints either side of one are re-decided from the neighbours across it.
    for (var i = 1; i + 1 < size(edges); i += 1)
    {
        if (edges[i].length >= sliverMax)
        {
            continue;
        }

        const across = jointRecord(definition, edges[i - 1].endTangent, edges[i + 1].startTangent, true, "");
        if (across.isBreak)
        {
            // A chamfer or a fillet at a corner: kept as its own piece, both joints corners.
            joints[i - 1] = mergeMaps(across, { "why" : "sliver at a corner, kept" });
            joints[i] = mergeMaps(across, { "why" : "sliver at a corner, kept" });
        }
        else
        {
            joints[i - 1] = mergeMaps(across, { "why" : "sliver absorbed" });
            joints[i] = mergeMaps(across, { "why" : "sliver absorbed" });
        }
    }

    return joints;
}

/**
 * The decision for one pair of along-chain tangents.
 */
function jointRecord(definition is map, before is Vector, after is Vector, sliver is boolean, why is string) returns map
{
    const angle = angleBetween(before, after) / radian;
    var kind = "corner";
    var isBreak = true;

    if (angle <= G1_JUNCTION_ANGLE)
    {
        kind = "tangent";
        isBreak = false;
    }
    else if (angle <= definition.cornerAngle / radian)
    {
        kind = "near";
        isBreak = !definition.forceTangency;
    }

    return { "angle" : angle, "kind" : kind, "isBreak" : isBreak, "sliver" : sliver, "why" : why };
}

// ============================================================================
// Groups
// ============================================================================

/**
 * Lay the user's groups over the chain: which edge belongs to which group, and the
 * joints re-decided so that a group's ends are breaks and its interior is not.
 *
 * @returns {map} : { "joints", "groupOfEdge" : per edge the group index or undefined }
 */
function applyGroups(context is Context, definition is map, chain is map, joints is array) returns map
{
    const edges = chain.edges;
    var groupOfEdge = makeArray(size(edges), undefined);
    var decided = joints;

    for (var g = 0; g < size(definition.groups); g += 1)
    {
        const entry = definition.groups[g];
        const label = entry.cw_name == "" ? "group " ~ toString(g + 1) : "'" ~ entry.cw_name ~ "'";
        if (isQueryEmpty(context, entry.cw_edges))
        {
            continue;
        }

        var indices = [];
        for (var i = 0; i < size(edges); i += 1)
        {
            if (!isQueryEmpty(context, qIntersection([edges[i].query, entry.cw_edges])))
            {
                indices = append(indices, i);
            }
        }

        if (size(indices) == 0)
        {
            throw regenError("The edges of " ~ label ~ " are not part of the wire.",
                [faultyArrayParameterId("groups", g, "cw_edges")]);
        }

        indices = sort(indices, function(a, b) { return a - b; });
        const first = indices[0];
        const last = indices[size(indices) - 1];
        if (last - first + 1 != size(indices))
        {
            throw regenError("The edges of " ~ label ~ " are not contiguous along the wire.",
                [faultyArrayParameterId("groups", g, "cw_edges")]);
        }

        for (var i = first; i <= last; i += 1)
        {
            if (groupOfEdge[i] != undefined)
            {
                throw regenError("An edge of " ~ label ~ " is already in another group.",
                    [faultyArrayParameterId("groups", g, "cw_edges")]);
            }
            groupOfEdge[i] = g;
        }

        // Ends are breaks; the interior is fitted through, corners included -- the user
        // said so, and it is reported.
        if (first > 0)
        {
            decided[first - 1] = mergeMaps(decided[first - 1], { "isBreak" : true, "why" : "group start" });
        }
        if (last + 1 < size(edges))
        {
            decided[last] = mergeMaps(decided[last], { "isBreak" : true, "why" : "group end" });
        }
        for (var j = first; j < last; j += 1)
        {
            const inside = decided[j];
            decided[j] = mergeMaps(inside, {
                        "isBreak" : false,
                        "why" : inside.kind == "corner" ? "corner fitted through in " ~ label : "inside " ~ label
                    });
        }
    }

    return { "joints" : decided, "groupOfEdge" : groupOfEdge };
}

// ============================================================================
// Runs
// ============================================================================

/**
 * Contiguous edge ranges between breaks.
 *
 * @returns {array} : { "first", "last" : edge indices inclusive, "group" : index or undefined }
 */
function buildRuns(chain is map, joints is array, groupOfEdge is array) returns array
{
    var runs = [];
    var first = 0;

    for (var i = 0; i < size(chain.edges); i += 1)
    {
        const lastOfRun = (i == size(chain.edges) - 1) || joints[i].isBreak;
        if (lastOfRun)
        {
            runs = append(runs, { "first" : first, "last" : i, "group" : groupOfEdge[first] });
            first = i + 1;
        }
    }

    return runs;
}

/**
 * Whether every edge of a run is an exact primitive the kernel can copy as such.
 */
function runIsExact(chain is map, run is map) returns boolean
{
    for (var i = run.first; i <= run.last; i += 1)
    {
        const t = chain.edges[i].curveType;
        if (t != CurveType.LINE && t != CurveType.CIRCLE)
        {
            return false;
        }
    }
    return true;
}

/**
 * The fitter settings for one run: the group's override, else the global ones.
 */
function runApproximation(definition is map, run is map, approximation is map) returns map
{
    if (run.group == undefined)
    {
        return approximation;
    }

    const entry = definition.groups[run.group];
    if (entry.cw_mode == CleanApproximation.MAX_CP)
    {
        return mergeMaps(approximation, { "approximationMaxCPs" : entry.cw_maxCPs });
    }
    if (entry.cw_mode == CleanApproximation.TOLERANCE)
    {
        return mergeMaps(approximation, { "approximationTolerance" : entry.cw_tolerance });
    }
    return approximation;
}

// ============================================================================
// Sampling
// ============================================================================

/**
 * Sample points along one run at a curvature-adaptive spacing.
 *
 * The chord between samples at spacing s on curvature kappa misses the curve by
 * kappa s^2 / 8; spending half the tolerance on that gives s = sqrt(4 tol / kappa),
 * clamped so a straight stretch still carries shape samples and a tight fillet cannot
 * demand thousands. The density 1 / s is integrated along each edge from its curvature
 * stations, and ceil(integral) samples are placed at equal increments of the cumulative
 * density, both edge ends included -- so every gap is between half and one and a half
 * spacings and the fitter never sees a short end segment (correction 23).
 *
 * @returns {map} : { "points" : array, "perEdge" : sample counts }
 */
function runSamples(context is Context, chain is map, run is map, tolerance is ValueWithUnits) returns map
{
    var points = [];
    var perEdge = [];

    for (var i = run.first; i <= run.last; i += 1)
    {
        const edge = chain.edges[i];
        const step = edge.length / (CURVATURE_STATIONS - 1);

        // Cumulative density over the stations, along the chain.
        var cumulative = [0];
        for (var s = 1; s < CURVATURE_STATIONS; s += 1)
        {
            const density = 0.5 * (1 / spacingFor(edge.curvatures[s - 1], tolerance, edge.length)
                    + 1 / spacingFor(edge.curvatures[s], tolerance, edge.length));
            cumulative = append(cumulative, cumulative[s - 1] + density * step);
        }

        const total = cumulative[CURVATURE_STATIONS - 1];
        const count = clamp(ceil(total), SAMPLE_MIN_PER_EDGE, SAMPLE_MAX_PER_EDGE);

        // Along-chain fractions at equal density increments, inverted piecewise-linearly.
        var fractions = [];
        var station = 1;
        for (var k = 0; k < count; k += 1)
        {
            const target = total * k / (count - 1);
            while (station < CURVATURE_STATIONS - 1 && cumulative[station] < target)
            {
                station += 1;
            }
            const span = cumulative[station] - cumulative[station - 1];
            const within = (span <= 0) ? 0 : clamp((target - cumulative[station - 1]) / span, 0, 1);
            fractions = append(fractions, (station - 1 + within) / (CURVATURE_STATIONS - 1));
        }

        // Kernel parameters: an edge traversed backwards reads its fractions from the far end.
        var parameters = [];
        for (var fraction in fractions)
        {
            parameters = append(parameters, edge.flipped ? 1 - fraction : fraction);
        }

        const lines = evEdgeTangentLines(context, { "edge" : edge.query, "parameters" : parameters });
        for (var tangentLine in lines)
        {
            points = append(points, tangentLine.origin);
        }
        perEdge = append(perEdge, count);
    }

    return { "points" : points, "perEdge" : perEdge };
}

/**
 * The sample spacing a curvature earns, see runSamples.
 */
function spacingFor(curvature, tolerance is ValueWithUnits, edgeLength is ValueWithUnits) returns ValueWithUnits
{
    const kappa = abs(curvature);
    var spacing = SAMPLE_MAX_SPACING;
    if (kappa * meter > 1e-9)
    {
        spacing = sqrt(4 * tolerance / kappa);
    }
    return clamp(spacing, SAMPLE_MIN_SPACING, min(SAMPLE_MAX_SPACING, edgeLength / 2));
}

// ============================================================================
// Emission
// ============================================================================

/**
 * One run into geometry under `runId`: an exact copy of primitive edges, or one fitted
 * spline through the run's samples with the kernel's end tangents.
 *
 * @returns {map} : the report line's data.
 */
function emitRun(context is Context, runId is Id, definition is map, chain is map, run is map,
    approximation is map) returns map
{
    const first = chain.edges[run.first];
    const last = chain.edges[run.last];
    const edgeCount = run.last - run.first + 1;

    if (runIsExact(chain, run))
    {
        var members = [];
        for (var i = run.first; i <= run.last; i += 1)
        {
            members = append(members, chain.edges[i].query);
        }
        opExtractWires(context, runId, { "edges" : qUnion(members) });

        return { "kind" : "exact", "edges" : edgeCount, "first" : run.first, "last" : run.last,
                "samples" : 0, "controlPoints" : 0, "deviation" : 0 * meter, "tolerance" : undefined,
                "group" : run.group };
    }

    const settings = runApproximation(definition, run, approximation);
    const sampled = runSamples(context, chain, run, settings.approximationTolerance);
    const points = withoutRepeats(sampled.points, fitRepeatTolerance(sampled.points));

    const curves = approximateFamily(context, [{
                        "points" : points,
                        "startDerivative" : first.startTangent,
                        "endDerivative" : last.endTangent
                    }], settings);
    const curve = curves[0];

    if (curve.isRational == true)
    {
        throw regenError("The fitter returned a rational curve, which this feature does not expect.");
    }

    emitFittedCurve(context, runId, curve, points, settings);

    return { "kind" : "fit", "edges" : edgeCount, "first" : run.first, "last" : run.last,
            "samples" : size(points), "controlPoints" : size(curve.controlPoints),
            "deviation" : fitDeviation(curve, points), "tolerance" : settings.approximationTolerance,
            "group" : run.group };
}

/**
 * The fit measured at its own samples. approximateFamily pinned every sample to a
 * chord-length parameter, so the curve at those parameters IS the fit's claim about
 * each point.
 */
function fitDeviation(curve is BSplineCurve, points is array) returns ValueWithUnits
{
    var chords = [0 * meter];
    var chord = 0 * meter;
    for (var i = 1; i < size(points); i += 1)
    {
        chord += norm(points[i] - points[i - 1]);
        chords = append(chords, chord);
    }
    if (chord <= 0 * meter)
    {
        return 0 * meter;
    }

    var parameters = [];
    for (var i = 0; i < size(points); i += 1)
    {
        parameters = append(parameters, chords[i] / chord);
    }

    const evaluated = evaluateSpline({ "spline" : curve, "parameters" : parameters, "nDerivatives" : 0 })[0];
    var worst = 0 * meter;
    for (var i = 0; i < size(points); i += 1)
    {
        worst = max(worst, norm(evaluated[i] - points[i]));
    }
    return worst;
}

// ============================================================================
// Reporting
// ============================================================================

/**
 * The feature's own notices: what was decided for the user, and anything short of the
 * tolerance -- that is missing shape, so a warning.
 */
function reportOutcome(context is Context, id is Id, chain is map, joints is array, runs is array, reports is array)
{
    var short = [];
    for (var k = 0; k < size(reports); k += 1)
    {
        const report = reports[k];
        if (report.kind == "fit" && report.deviation > 1.0001 * report.tolerance)
        {
            short = append(short, toString(k));
        }
    }

    var cornersFitted = 0;
    for (var joint in joints)
    {
        if (joint.kind == "corner" && !joint.isBreak)
        {
            cornersFitted += 1;
        }
    }

    if (size(short) > 0)
    {
        reportFeatureWarning(context, id, "Run(s) " ~ join(short, ", ") ~ " could not be fitted within tolerance; see the console.");
        return;
    }

    if (cornersFitted > 0)
    {
        reportFeatureInfo(context, id, toString(size(chain.edges)) ~ " edge(s) -> " ~ toString(size(runs))
            ~ "; " ~ toString(cornersFitted) ~ " corner(s) inside a group were fitted through.");
    }
}

/**
 * Print every edge and every joint decision.
 */
function printChain(chain is map, joints is array, groupOfEdge is array)
{
    println("");
    println("========== clean wire: chain ==========");
    println("   edge        length  type     start (mm)                          group");
    for (var i = 0; i < size(chain.edges); i += 1)
    {
        const edge = chain.edges[i];
        println(padLeft(toString(i), 7) ~ fmtMM(edge.length, 3, 14) ~ "  " ~ padLeft(curveTypeName(edge.curveType), 7)
            ~ "  " ~ fmtVec(edge.startPoint / millimeter, 2, 10) ~ (edge.flipped ? "  (reversed)" : "            ")
            ~ (groupOfEdge[i] == undefined ? "" : "  group " ~ toString(groupOfEdge[i])));
    }
    println("");
    println("  joint   angle(deg)  kind      break  note");
    for (var j = 0; j < size(joints); j += 1)
    {
        const joint = joints[j];
        println(padLeft(toString(j) ~ "|" ~ toString(j + 1), 8) ~ fmtNum(joint.angle * 180 / PI, 3, 12)
            ~ "  " ~ padLeft(joint.kind, 8) ~ "  " ~ padLeft(joint.isBreak ? "yes" : "no", 5)
            ~ "  " ~ joint.why);
    }
    println("========== end chain ==========");
}

/**
 * Print every run as emitted.
 */
function printRuns(reports is array)
{
    println("");
    println("========== clean wire: runs ==========");
    println("    run  edges      kind   samples  CPs   deviation (mm)  group");
    for (var k = 0; k < size(reports); k += 1)
    {
        const r = reports[k];
        println(padLeft(toString(k), 7) ~ padLeft(toString(r.first) ~ ".." ~ toString(r.last), 7) ~ "  " ~ padLeft(r.kind, 8)
            ~ padLeft(toString(r.samples), 9) ~ padLeft(toString(r.controlPoints), 5)
            ~ fmtMM(r.deviation, 4, 16) ~ (r.group == undefined ? "" : padLeft(toString(r.group), 7)));
    }
    println("========== end runs ==========");
}

function curveTypeName(curveType) returns string
{
    if (curveType == CurveType.LINE) { return "line"; }
    if (curveType == CurveType.CIRCLE) { return "circle"; }
    if (curveType == CurveType.ELLIPSE) { return "ellipse"; }
    return "other";
}

/**
 * A debug point at every kept vertex.
 */
function showCorners(context is Context, chain is map, joints is array)
{
    for (var j = 0; j < size(joints); j += 1)
    {
        if (joints[j].isBreak)
        {
            addDebugPoint(context, chain.edges[j].endPoint, DebugColor.RED);
        }
    }
}
