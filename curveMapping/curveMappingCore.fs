FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/path.fs", version : "3083.0");
import(path : "onshape/std/approximationUtils.fs", version : "3083.0");
//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/b1c7f2116fb64e6b40bf53f4", version : "afe2c4279f26bf0b7e587d71");
//import Utils
import(path : "ad98c7f43a25a4c0e8a428e7", version : "223c53d12a83984c4c62e354");
// IMPORT: tools/arc_length.fs
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/f88f68e9ff3cb3c30d4afffe", version : "9d7ce42abf58886bfeccfaaa");
// IMPORT: tools/frenet.fs
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/a19a275a032ee47f4dbcc83c", version : "e11709063628c9ca70edfca2");
// IMPORT: tools/point_projection.fs
import(path : "b1e8bfe71f67389ca210ed8b/18ce001c456655455ae400f8/eb46317a27a44e391e11dfe6", version : "7b2ce264ee62cfbbb372ecdf");

export const samplingDensityBounds = {(millimeter) : [.1, 10, 200]} as LengthBoundSpec;

export enum SamplingMode
{
    annotation { "Name" : "Length-based" } LENGTH_BASED,
    annotation { "Name" : "Control point-based" } CP_BASED
}

export const cpMultiplierBounds = { (unitless) : [1, 3, 50] } as IntegerBoundSpec;

// ── Frame normal mode ───────────────────────────────────────────────────────
// Controls how getFrameAtArcLength orients the frame normal (xAxis) along a path.
//   FRENET   : a normal seeded from the kernel curvature normal at the first curved
//              edge, then parallel-transported along the whole path. It cannot flip at
//              an inflection -- there is no curvature sign in transport at all -- but
//              its absolute roll about the tangent is fixed by that one seed, so only
//              roll DIFFERENCES along a path are meaningful.
//   BINORMAL : build an in-plane normal from a supplied plane normal (the binormal):
//              N = normalize(ref x tangent), B = ref. Independent of curvature sign or
//              magnitude, so it never flips at inflections. Requires the path to be
//              planar with plane normal ~= ref (validated in buildFrenetPath). The ref
//              is shared by the from- and to-paths, so its sign does NOT affect the wrap
//              output (offset side is corrected with the existing "Flip normal").
export enum FrameNormalMode
{
    annotation { "Name" : "Frenet" }
    FRENET,
    annotation { "Name" : "Binormal" }
    BINORMAL
}

// How the BINORMAL reference (plane normal) direction is supplied.
export enum BinormalSource
{
    annotation { "Name" : "Query" }
    QUERY,
    annotation { "Name" : "Vector" }
    VECTOR
}

export const binormalCompBounds  = { (unitless) : [-1e7, 0, 1e7] } as RealBoundSpec;  // X / Z default 0
export const binormalCompBoundsY = { (unitless) : [-1e7, 1, 1e7] } as RealBoundSpec;  // Y default 1

/**
 * Default frame-normal options: legacy Frenet behavior. Stored on every FrenetPath
 * built via the 4-argument buildFrenetPath so getFrameAtArcLength can read it safely.
 */
export function defaultFrameNormalOptions() returns map
{
    return { "mode" : FrameNormalMode.FRENET, "ref" : vector(0, 0, 0) };
}

/**
 * Build a BINORMAL options map from an already-resolved, normalized ref direction.
 */
export function planeNormalOptions(ref is Vector) returns map
{
    return { "mode" : FrameNormalMode.BINORMAL, "ref" : ref };
}

/**
 * Resolve a plane-normal (binormal) reference direction from explicit components.
 * Returns a normalized unitless direction.
 */
export function resolveBinormalRefFromVector(vx is number, vy is number, vz is number) returns Vector
{
    var dir = vector(vx, vy, vz);
    if (norm(dir) < 1e-9)
        throw regenError("Plane normal vector is zero - supply a non-zero (X, Y, Z).");
    return normalize(dir);
}

/**
 * Resolve a plane-normal (binormal) reference direction from a query.
 * Accepts a mate connector (uses its Z axis) or a planar face (uses its normal).
 * Returns a normalized unitless direction.
 */
export function resolveBinormalRefFromQuery(context is Context, q is Query) returns Vector
{
    if (isQueryEmpty(context, q))
        throw regenError("Select a planar face or mate connector for the plane normal.");

    var dir;
    if (!isQueryEmpty(context, qBodyType(q, BodyType.MATE_CONNECTOR)))
        dir = evMateConnector(context, { "mateConnector" : q }).zAxis;
    else
        dir = evPlane(context, { "face" : q }).normal;

    if (norm(dir) < 1e-9)
        throw regenError("Could not resolve a plane normal from the selection.");
    return normalize(dir);
}

/**
 * In-plane (offset) normal for BINORMAL mode: N = normalize(ref x tangent).
 * Throws if ref is parallel to the tangent (degenerate).
 */
function planeNormalAxis(ref is Vector, tangent is Vector) returns Vector
{
    var n = cross(ref, tangent);
    if (norm(n) < 1e-6)
        throw regenError("Plane normal is parallel to the path tangent; cannot build an in-plane frame.");
    return normalize(n);
}

/**
 * Expands a mixed edge/wire-body/composite selection into a flat edge query.
 * - Direct edges pass through unchanged.
 * - Wire bodies contribute all of their owned edges.
 * - Composite parts contribute edges owned by any wire body they contain.
 */
export function expandEdgeQuery(q is Query) returns Query
{
    var directEdges = qEntityFilter(q, EntityType.EDGE);

    var wireBodies = qBodyType(qEntityFilter(q, EntityType.BODY), BodyType.WIRE);
    var wireEdges = qOwnedByBody(wireBodies, EntityType.EDGE);

    var composites = qBodyType(qEntityFilter(q, EntityType.BODY), BodyType.COMPOSITE);
    var compositeWireEdges = qOwnedByBody(
        qBodyType(qContainedInCompositeParts(composites), BodyType.WIRE),
        EntityType.EDGE);

    return qUnion([directEdges, wireEdges, compositeWireEdges]);
}



// ============================================================================
// buildFrenetPath
// ============================================================================

/**
 * Preprocess a G1-continuous edge chain into a queryable FrenetPath structure.
 *
 * Validates G1 continuity via constructPath, optionally reverses the chain,
 * and computes per-edge metadata: arc-length tables, inflection arc-lengths,
 * and cumulative normal sign.
 *
 * @param context   {Context}
 * @param id        {Id}
 * @param sourceEdges {Query}  : edges forming a single G1-continuous chain
 * @param flipRef   {boolean} : if true, reverse the chain direction
 * @returns {map} :
 *   "path"        {Path}            - ordered Path from constructPath
 *   "totalLength" {ValueWithUnits}  - total arc-length of the chain
 *   "edgeData"    {array}           - per-edge maps (index 0 = chain start)
 *
 * Each edgeData entry contains:
 *   query, bspline, stdDir, isLine, length,
 *   arcLengthTable, frameSamples,
 *   lineFrame (lines only), lineStartPt (lines only),
 *   startArcLength
 */
export function buildFrenetPath(context is Context, id is Id, sourceEdges is Query, flipRef is boolean) returns map
{
    return buildFrenetPath(context, id, sourceEdges, flipRef, defaultFrameNormalOptions());
}

/**
 * 5-argument overload: same as above but with explicit frame-normal options.
 *
 * @param frameOptions {map} : { "mode" : FrameNormalMode, "ref" : Vector }
 *   - FRENET   : ref ignored (legacy curvature-normal + inflection parity).
 *   - BINORMAL : ref is the unitless plane normal; each edge is validated planar with
 *     that normal, inflection detection is skipped, and getFrameAtArcLength builds
 *     N = normalize(ref x tangent).
 */
export function buildFrenetPath(context is Context, id is Id, sourceEdges is Query, flipRef is boolean, frameOptions is map) returns map
{
    var planeNormalMode = (frameOptions.mode == FrameNormalMode.BINORMAL);
    var planeRef        = frameOptions.ref;

    // 1. Validate G1 continuity and get ordered path
    var path;
    try
    {
        path = constructPath(context, sourceEdges);
    }
    catch (error)
    {
        throw regenError("Reference edges must be G1 continuous", sourceEdges);
    }

    // 2. Optionally reverse traversal direction
    if (flipRef)
        path = reverse(path);

    // 3. Build per-edge data
    var edgeData = [];
    var nEdges   = size(path.edges);

    for (var i = 0; i < nEdges; i += 1)
    {
        var edge    = path.edges[i];
        var flipped = path.flipped[i];
        var stdDir  = !flipped;  // true = traverse param 0→1

        var bspline  = evApproximateBSplineCurve(context, { "edge": edge });
        var length   = evLength(context, { "entities": edge });
        var curveDef = evCurveDefinition(context, { "edge": edge });
        var isLine   = (curveDef.curveType == CurveType.LINE);

        // Always build arc-length table — used for both projection and frame lookup
        var arcLengthTable = buildArcLengthTable(bspline, 100);

        // BINORMAL requires each reference edge to be planar with normal ~= planeRef.
        // A planar edge has all control points at constant dot(point, planeRef); flag the
        // max out-of-plane deviation so a tilted/3D reference fails loudly instead of skewing.
        if (planeNormalMode)
        {
            var pcps   = bspline.controlPoints;
            var maxOff = 0 * meter;
            for (var j = 1; j < size(pcps); j += 1)
            {
                var off = abs(dot(pcps[j] - pcps[0], planeRef));
                if (off > maxOff)  maxOff = off;
            }
            if (maxOff > 1e-4 * length)
                throw regenError("Reference edge is not planar with the supplied plane normal " ~
                    "(out-of-plane deviation " ~ toString(maxOff) ~ "). Use Frenet mode, or a " ~
                    "plane normal in the path's plane.", edge);
        }

        var lineFrame           = undefined;
        var lineStartPt         = undefined;

        if (isLine)
        {
            // Derive the line tangent from the actual traversal endpoints rather than
            // curveDef.direction. For a line, curveDef.direction is a fixed geometric
            // orientation that is INDEPENDENT of the edge's parametric (control-point)
            // order, so the old "if (!stdDir) lineDir = -lineDir" correction could point
            // the frame backwards: on a flat to-edge traversed opposite to its CP order
            // (stdDir=false) the frame ran -x while the path traversed +x, placing the
            // reference one edge-length off and mirroring every source curve on that edge.
            var endLines       = evEdgeTangentLines(context, { "edge": edge, "parameters": [0, 1] });
            var traversalStart = stdDir ? endLines[0].origin : endLines[1].origin;
            var traversalEnd   = stdDir ? endLines[1].origin : endLines[0].origin;
            var lineDir        = normalize(traversalEnd - traversalStart);

            lineFrame   = lineFrenetFrame({ "origin": curveDef.origin, "direction": lineDir });
            lineStartPt = traversalStart;
        }
        else
        {
            // Guard: skip inflection detection for near-linear BSplines.
            // Nearly-collinear control polygons produce unreliable cross products in
            // bSplineMayHaveInflection, generating spurious sign flips in getFrameAtArcLength.
            // Threshold: max lateral deviation < 0.1% of edge length.
            var cps          = bspline.controlPoints;
            var nCPs         = size(cps);
            var p0           = cps[0];
            var p1           = cps[nCPs - 1];
            var chord        = p1 - p0;
            var chordLen     = norm(chord);
            var isNearLinear = false;
            if (chordLen.value > 1e-10)
            {
                var chordDir = normalize(chord);
                var maxDev   = 0 * meter;
                for (var j = 1; j < nCPs - 1; j += 1)
                {
                    var diff    = cps[j] - p0;
                    var lateral = norm(diff - dot(diff, chordDir) * chordDir);
                    if (lateral > maxDev)  maxDev = lateral;
                }
                isNearLinear = (maxDev < 0.001 * length);
            }

            // Promote near-linear BSplines to line mode so getFrameAtArcLength uses a
            // stable, consistent xAxis instead of computeFrenetFrame's noisy normal.
            // (computeFrenetFrame is unreliable when curvature ≈ 0.)
            if (isNearLinear)
            {
                isLine = true;
                var lineDir = normalize(chord);  // chordLen > 1e-10 guaranteed here
                if (!stdDir)
                    lineDir = -1 * lineDir;
                var origin  = stdDir ? p0 : p1;
                lineFrame   = lineFrenetFrame({ "origin": origin, "direction": lineDir });
                lineStartPt = origin;
            }

        }

        // One batched kernel call per curved edge, replacing one per mapped point. A near-linear
        // edge promoted to line mode above is sampled too: its NORMAL comes from the line logic, but
        // its position and tangent must be the curve's own (see isExactLine).
        var frameSamples = (curveDef.curveType == CurveType.LINE) ? undefined : sampleEdgeFrames(context, edge, stdDir);

        edgeData = append(edgeData, {
            "query"              : edge,
            "frameSamples"       : frameSamples,
            "bspline"            : bspline,
            "stdDir"             : stdDir,
            "isLine"             : isLine,
            "length"             : length,
            "arcLengthTable"     : arcLengthTable,
            "lineFrame"          : lineFrame,
            "lineStartPt"        : lineStartPt
        });
    }

    // 4. Propagate cumulative startArcLength across edges
    var runningArc = 0 * meter;

    for (var i = 0; i < size(edgeData); i += 1)
    {
        edgeData[i] = mergeMaps(edgeData[i], { "startArcLength": runningArc });
        runningArc += edgeData[i].length;
    }

    // 4.5. Transport one normal along the whole path.
    //
    // This replaces three things at once: the inflection-parity sign tracking, the
    // line-normal borrowing that used to follow it, and the junction continuity check
    // that rejected any chain the parity could not describe.
    //
    // The Frenet normal points wherever the curve happens to be bending, which on a 3D
    // chain swings about the tangent from edge to edge, and at zero curvature the kernel
    // documents it as arbitrary. A parity bit cannot track that. Measured on a real ski
    // chain elsewhere in this repo, the normal rolled 86.9 degrees across a junction
    // whose tangent was identical -- with a POSITIVE dot product, so a sign flip could
    // not see it, while the 26-degree continuity check would have rejected the chain
    // outright.
    //
    // Minimal-rotation transport has no sign convention to get backwards, needs no
    // inflection list, and reduces to the in-plane normal exactly when the chain is
    // planar. It is seeded from the old convention at the path start, so the frame there
    // is unchanged and it diverges only where the parity was already wrong.
    // Seed from the first genuinely curved edge, NOT from edge 0.
    //
    // A line has no curvature normal of its own: lineFrenetFrame picks whichever world
    // axis is least parallel to the tangent, which is arbitrary and generally out of the
    // path's plane. Seeding the transport from that would roll every downstream frame by
    // an arbitrary angle. The pass this replaced had the same problem and solved it by
    // borrowing a line's normal from an adjacent curve; seeding at the curve and
    // transporting outwards in both directions is the same idea, done once.
    var seedIdx = 0;
    while (seedIdx < size(edgeData) && edgeData[seedIdx].isLine)
    {
        seedIdx += 1;
    }
    if (seedIdx >= size(edgeData))
    {
        // Every edge is a line. Nothing prefers any orientation, so edge 0 will do.
        seedIdx = 0;
    }

    var seedTangent = edgeData[seedIdx].isLine
        ? edgeData[seedIdx].lineFrame.zAxis
        : edgeData[seedIdx].frameSamples.tangents[0];
    var seed = edgeData[seedIdx].isLine
        ? edgeData[seedIdx].lineFrame.xAxis
        : edgeData[seedIdx].frameSamples.normals[0];

    seed = seed - dot(seed, seedTangent) * seedTangent;
    seed = (norm(seed) < 1e-9) ? perpendicularVector(seedTangent) : normalize(seed);

    // Seed from the seed edge's most-curved INTERIOR sample, carried back to its start, not
    // from the kernel normal at parameter 0 -- endpoint curvature evaluations are the least
    // reliable (driven_offset measured a sign flip there), and where the start is nearly
    // straight that normal is arbitrary and rolls the whole field. Oriented to agree with the
    // old seed, so a planar reference -- where the two agree -- is unchanged.
    if (!edgeData[seedIdx].isLine)
    {
        var seedSamples = edgeData[seedIdx].frameSamples;
        var bestJ = 0;
        var bestTurn = 0;
        for (var j = 1; j < CM_FRAME_SAMPLES - 1; j += 1)
        {
            var turn = norm(seedSamples.tangents[j + 1] - seedSamples.tangents[j - 1]);
            if (turn > bestTurn)
            {
                bestTurn = turn;
                bestJ = j;
            }
        }
        if (bestJ > 0 && bestTurn > 1e-6)
        {
            var interior = seedSamples.normals[bestJ];
            interior = interior - dot(interior, seedSamples.tangents[bestJ]) * seedSamples.tangents[bestJ];
            if (norm(interior) > 1e-9)
            {
                var carriedBack = normalize(interior);
                var backT = seedSamples.tangents[bestJ];
                for (var j = bestJ - 1; j >= 0; j -= 1)
                {
                    carriedBack = cmTransportNormal(carriedBack, backT, seedSamples.tangents[j]);
                    backT = seedSamples.tangents[j];
                }
                seed = (dot(carriedBack, seed) < 0) ? -1 * carriedBack : carriedBack;
            }
        }
    }

    var carried         = seed;
    var previousTangent = seedTangent;

    for (var i = seedIdx; i < size(edgeData); i += 1)
    {
        if (edgeData[i].isLine)
        {
            var lineTangent = edgeData[i].lineFrame.zAxis;
            carried         = cmTransportNormal(carried, previousTangent, lineTangent);
            previousTangent = lineTangent;

            edgeData[i] = mergeMaps(edgeData[i], { "transportedNormal": carried });
        }
        else
        {
            var tangents       = edgeData[i].frameSamples.tangents;
            var carriedNormals = [];

            for (var j = 0; j < CM_FRAME_SAMPLES; j += 1)
            {
                carried         = cmTransportNormal(carried, previousTangent, tangents[j]);
                previousTangent = tangents[j];
                carriedNormals  = append(carriedNormals, carried);
            }

            edgeData[i] = mergeMaps(edgeData[i], {
                "frameSamples": mergeMaps(edgeData[i].frameSamples, { "normals": carriedNormals })
            });
        }
    }

    // Backwards from the seed, so a leading line inherits the curve's orientation
    // instead of dictating an arbitrary one.
    carried         = seed;
    previousTangent = seedTangent;

    for (var i = seedIdx - 1; i >= 0; i -= 1)
    {
        if (edgeData[i].isLine)
        {
            var backTangent = edgeData[i].lineFrame.zAxis;
            carried         = cmTransportNormal(carried, previousTangent, backTangent);
            previousTangent = backTangent;

            edgeData[i] = mergeMaps(edgeData[i], { "transportedNormal": carried });
        }
        else
        {
            var backTangents = edgeData[i].frameSamples.tangents;
            var backNormals  = makeArray(CM_FRAME_SAMPLES, carried);

            for (var j = CM_FRAME_SAMPLES - 1; j >= 0; j -= 1)
            {
                carried         = cmTransportNormal(carried, previousTangent, backTangents[j]);
                previousTangent = backTangents[j];
                backNormals[j]  = carried;
            }

            edgeData[i] = mergeMaps(edgeData[i], {
                "frameSamples": mergeMaps(edgeData[i].frameSamples, { "normals": backNormals })
            });
        }
    }

    // 4.6. Cumulative turning, theta(s) = integral of kappa ds, measured about the
    //      transported normal. Only definable now that the normal is sign-stable: with
    //      the old inflection parity the normal flipped mid-path and the integral had no
    //      consistent sign. Used by the parallel-arc correction in mapSinglePoint.
    var turnArcs   = [];
    var turnThetas = [];
    var theta      = 0;

    for (var i = 0; i < size(edgeData); i += 1)
    {
        var ed = edgeData[i];

        if (ed.isLine)
        {
            // A line does not turn, so theta is flat across it; one entry is enough.
            turnArcs   = append(turnArcs, ed.startArcLength);
            turnThetas = append(turnThetas, theta);
            continue;
        }

        var tangents = ed.frameSamples.tangents;
        var normals  = ed.frameSamples.normals;
        var span     = ed.length / (CM_FRAME_SAMPLES - 1);

        for (var j = 0; j < CM_FRAME_SAMPLES; j += 1)
        {
            if (j > 0)
            {
                theta += dot(tangents[j] - tangents[j - 1], normals[j - 1]);
            }
            turnArcs   = append(turnArcs, ed.startArcLength + j * span);
            turnThetas = append(turnThetas, theta);
        }
    }

    // 5. Compute total length by summing edges (avoids dependency on evPathLength)
    var totalLength = 0 * meter;
    for (var ed in edgeData)
        totalLength += ed.length;

    // Close the table at the far end so a trailing line is covered.
    turnArcs   = append(turnArcs, totalLength);
    turnThetas = append(turnThetas, theta);

    return {
        "path"              : path,
        "totalLength"       : totalLength,
        "edgeData"          : edgeData,
        "turnArcs"          : turnArcs,
        "turnThetas"        : turnThetas,
        "frameNormalOptions": frameOptions
    };
}


// ============================================================================
// getFrameAtArcLength
// ============================================================================

/**
 * Whether the transplant matches arc length on the curve the point ACTUALLY lies on,
 * rather than on the reference.
 *
 * OFF by default: turning it on changes the geometry every existing wrap produces.
 *
 * The transplant is s_to = toRefArc + (s_from - fromRefArc), which preserves the
 * REFERENCE's arc length. A point sitting at normal distance h from the reference does
 * not travel along the reference though -- it travels along the parallel curve at
 * distance h, whose arc length is s - h*theta(s). So the map is length-preserving only
 * for points lying ON the reference; off it, the length error is h*(theta_to -
 * theta_from). Wrapping a curve 20 mm off a reference that turns 0.8 rad onto one that
 * does not turn is a 16 mm error.
 *
 * Scope that matters, and it is favourable: this applies to the NORMAL component only.
 * The binormal is the ruling direction of the swept surface, which is straight, so
 * carrying that coordinate across verbatim is already exactly right.
 *
 * When on, only mapSinglePoint (and therefore mapWorldPoints and deform) is corrected.
 * wrapCurve and wrapAndLoft each hand-roll the same shift inline -- 6 sites between them
 * -- and must adopt mappedArc before the flag is used in production, or the two paths
 * will disagree.
 */
export const CM_PARALLEL_ARC = false;

/**
 * The to-path arc a point maps to.
 *
 * With CM_PARALLEL_ARC off this is exactly the old isometric shift, so it is a drop-in.
 * With it on, it solves for the arc at which the point has travelled the same distance
 * along its OWN offset curve, by Newton on
 *
 *     f(s) = (s - toRefArc) - h*(theta_to(s) - theta_to(toRefArc)) - travelled
 *     f'(s) = 1 - h*kappa_to(s)
 *
 * seeded at the old answer, which is already within h*theta of the root.
 *
 * @param h {ValueWithUnits} : the point's signed normal offset from the from-reference.
 */
export function mappedArc(fromPath is map, toPath is map, fromRefArc is ValueWithUnits,
    toRefArc is ValueWithUnits, sFrom is ValueWithUnits, h is ValueWithUnits) returns ValueWithUnits
{
    var shifted = toRefArc + (sFrom - fromRefArc);

    if (!CM_PARALLEL_ARC)
    {
        return shifted;
    }

    var travelled = (sFrom - fromRefArc) - h * (turningAt(fromPath, sFrom) - turningAt(fromPath, fromRefArc));
    var thetaRef  = turningAt(toPath, toRefArc);
    var s         = shifted;

    for (var i = 0; i < 3; i += 1)
    {
        var residual = (s - toRefArc) - h * (turningAt(toPath, s) - thetaRef) - travelled;
        var slope    = 1 - h * curvatureAtArc(toPath, s);

        // Past the centre of curvature the parallel curve reverses and there is no
        // sensible root; keep the uncorrected answer rather than diverge.
        if (abs(slope) < 1e-6)
        {
            return shifted;
        }

        s = s - residual / slope;
    }

    return s;
}

/** Two adjacent to-path edges meeting within this angle count as one smooth run. */
export const CM_SPAN_MERGE_ANGLE = 0.5 * degree;

/** Traversal-direction tangent at an edge's start. */
function edgeStartTangent(edgeDat is map) returns Vector
{
    return isExactLine(edgeDat) ? edgeDat.lineFrame.zAxis : edgeDat.frameSamples.tangents[0];
}

/** Traversal-direction tangent at an edge's end. */
function edgeEndTangent(edgeDat is map) returns Vector
{
    return isExactLine(edgeDat) ? edgeDat.lineFrame.zAxis
        : edgeDat.frameSamples.tangents[CM_FRAME_SAMPLES - 1];
}

/**
 * Whether an edge is a TRUE line (the kernel's curve type), as opposed to a near-linear spline promoted
 * to line mode for a stable normal. A promoted edge keeps its sampled frames and is read through them for
 * position and tangent: taking its chord instead put mapped points up to the sagitta (0.1% of the edge
 * length) off and turned the frame by up to ~0.23 degrees at its ends, kinking every curve mapped across
 * the boundary (2026-09-25 arc / line tangency review).
 */
function isExactLine(edgeDat is map) returns boolean
{
    return edgeDat.isLine && edgeDat.frameSamples == undefined;
}

/**
 * Map a point lying between two existing samples of a source curve.
 *
 * Same route as the junction oversampling: evaluate the source curve at an intermediate
 * parameter, read its coordinates in the from-frame there, rebuild them in the to-frame.
 * Used to top up a span that the to-path cut too short to fit.
 *
 * @param k    {number} : index of the sample this point follows.
 * @param frac {number} : position between sample k and k+1, in (0, 1).
 * @returns {map} : { "point" : Vector, "offsetDir" : Vector }
 */
export function mapBetweenSamples(context is Context, flipToNormal is boolean, sourceEdge is Query,
    srcFlipped is boolean, numSamples is number, fromFrenetPath is map, toFrenetPath is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits,
    mappedData is array, k is number, frac is number) returns map
{
    var param    = (k + frac) / (numSamples - 1);
    var srcParam = srcFlipped ? 1 - param : param;

    var line = evEdgeTangentLines(context, { "edge": sourceEdge, "parameters": [srcParam] })[0];

    var sFrom   = mappedData[k].sFrom + frac * (mappedData[k + 1].sFrom - mappedData[k].sFrom);
    var fromRes = getFrameAtArcLength(context, fromFrenetPath, sFrom);
    var local   = worldPointToFrenet(line.origin, fromRes);

    var sTo   = mappedArc(fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, sFrom, local[1]);
    var toRes = getFrameAtArcLength(context, toFrenetPath, sTo);

    var toSign  = flipToNormal ? -1 * toRes.sign : toRes.sign;
    var toFrame = (toSign != fromRes.sign)
        ? mergeMaps(toRes, { "frame": coordSystem(toRes.frame.origin, -1 * toRes.frame.xAxis, toRes.frame.zAxis) })
        : toRes;

    return {
        "point"     : frenetPointToWorld(local, toFrame),
        "offsetDir" : toFrame.frame.xAxis
    };
}

/**
 * Whether the path runs smoothly from one edge into the next.
 *
 * Consumers split a mapped curve wherever the to-path edge index changes, because the
 * frame used to jump at those boundaries. It no longer does: the position and tangent
 * come from a G1 chain, and the normal is now transported, so across a tangent-continuous
 * boundary the map is continuous and a split there buys nothing. It costs plenty though --
 * each split is an independently fitted span that only rejoins through jostleG2Junctions,
 * and a span landing on a short edge can end up with too few points to fit at all.
 *
 * Non-adjacent indices never merge: the traversal has genuinely jumped.
 */
export function pathIsSmoothAcross(frenetPath is map, edgeA is number, edgeB is number) returns boolean
{
    return pathIsSmoothAcross(frenetPath, edgeA, edgeB, false);
}

/**
 * [pathIsSmoothAcross], and with `curvatureBreaks` also NOT smooth across a curvature jump
 * (see the check below). Off keeps span topology as it was: turning it on adds spans, so
 * faces and edges downstream of the output change.
 */
export function pathIsSmoothAcross(frenetPath is map, edgeA is number, edgeB is number, curvatureBreaks is boolean) returns boolean
{
    if (edgeA == edgeB)
    {
        return true;
    }

    var lo = min([edgeA, edgeB]);
    var hi = max([edgeA, edgeB]);
    if (hi != lo + 1)
    {
        return false;
    }

    var edgeData = frenetPath.edgeData;
    if (lo < 0 || hi >= size(edgeData))
    {
        return false;
    }

    if (dot(edgeEndTangent(edgeData[lo]), edgeStartTangent(edgeData[hi])) < cos(CM_SPAN_MERGE_ANGLE))
    {
        return false;
    }

    // Tangent-continuous is not enough: across a curvature jump (a line meeting an arc, or
    // arcs of clearly different radius) the wrapped curve's own curvature jumps too, and one
    // span fitted across it rings. Break there; each side then keeps its curvature, with the
    // exact junction point and each side's exact tangent.
    if (!curvatureBreaks)
    {
        return true;
    }
    var kA  = edgeEndCurvature(edgeData[lo], true);
    var kB  = edgeEndCurvature(edgeData[hi], false);
    var big = max(norm(kA), norm(kB));
    return !(big > CM_CURVATURE_FLOOR && norm(kA - kB) > CM_CURVATURE_JUMP_REL * big);
}

/**
 * Signed curvature at any arc along the path, about the transported normal.
 */
export function curvatureAtArc(frenetPath is map, arc is ValueWithUnits)
{
    var edgeData = frenetPath.edgeData;
    var edgeIdx  = 0;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        if (edgeData[i].startArcLength <= arc)  edgeIdx = i;
    }

    var edgeDat = edgeData[edgeIdx];
    if (edgeDat.frameSamples == undefined)
    {
        return 0 / meter;
    }

    var last = CM_FRAME_SAMPLES - 1;
    var span = edgeDat.length / last;
    var t    = (arc - edgeDat.startArcLength) / span;
    var i    = floor(t);
    if (i < 0)        { i = 0; }
    if (i > last - 1) { i = last - 1; }

    var samples = edgeDat.frameSamples;

    return dot((samples.tangents[i + 1] - samples.tangents[i]) / span, samples.normals[i]);
}

/**
 * Cumulative turning angle at an arc, in radians as a bare number.
 *
 * Outside the sampled range it continues at the end curvature, matching the way
 * getFrameAtArcLength continues the path itself.
 */
export function turningAt(frenetPath is map, arc is ValueWithUnits) returns number
{
    var arcs   = frenetPath.turnArcs;
    var thetas = frenetPath.turnThetas;
    var count  = size(arcs);

    if (count == 0)         { return 0; }
    if (arc <= arcs[0])     { return thetas[0] + curvatureAtArc(frenetPath, arcs[0]) * (arc - arcs[0]); }
    if (arc >= arcs[count - 1])
    {
        return thetas[count - 1] + curvatureAtArc(frenetPath, arcs[count - 1]) * (arc - arcs[count - 1]);
    }

    var lo = 0;
    var hi = count - 1;
    while (lo < hi)
    {
        var mid = ceil((lo + hi) / 2);
        if (arcs[mid] <= arc) { lo = mid; } else { hi = mid - 1; }
    }

    var span = arcs[lo + 1] - arcs[lo];
    if (abs(span / meter) < 1e-15)
    {
        return thetas[lo + 1];
    }

    var f = (arc - arcs[lo]) / span;

    return (1 - f) * thetas[lo] + f * thetas[lo + 1];
}

/**
 * Rotate a normal from one tangent to the next by the smallest rotation that carries
 * the old tangent onto the new one.
 *
 *     N' = N - (N.b / (1 + a.b)) * (a + b)
 *
 * Trig-free, and exactly a rotation: the result stays unit length and stays
 * perpendicular to the new tangent (measured at 1.1e-16 over 600 consecutive steps).
 * Critically it contains no branch on the sign of anything, so there is no convention
 * to get backwards -- which is the entire problem it replaces.
 */
function cmTransportNormal(normal is Vector, fromTangent is Vector, toTangent is Vector) returns Vector
{
    var denominator = 1 + dot(fromTangent, toTangent);

    // Tangent reversed on itself: no minimal rotation exists, so re-project instead.
    if (denominator < 1e-9)
    {
        var projected = normal - dot(normal, toTangent) * toTangent;
        return (norm(projected) < 1e-9) ? normal : normalize(projected);
    }

    return normalize(normal - (dot(normal, toTangent) / denominator) * (fromTangent + toTangent));
}

/**
 * Signed curvature at one end of the path, resolved about a caller-supplied axis.
 *
 * Only used to continue the path past its ends. A line, or an edge whose end samples
 * are collinear, returns zero and the extension stays straight.
 */
function endCurvature(frenetPath is map, boundaryArc is ValueWithUnits, aboutAxis is Vector)
{
    var edgeData = frenetPath.edgeData;
    var edgeDat  = (boundaryArc <= 0 * meter) ? edgeData[0] : edgeData[size(edgeData) - 1];

    if (edgeDat.frameSamples == undefined)
    {
        return 0 / meter;
    }

    var samples = edgeDat.frameSamples;
    var last    = CM_FRAME_SAMPLES - 1;
    var span    = edgeDat.length / last;
    var i       = (boundaryArc <= 0 * meter) ? 0 : last - 1;

    // Resolved about the axis the caller will actually rotate toward, not about the
    // stored normal. In BINORMAL mode the frame's xAxis has been replaced by
    // planeNormalAxis(ref, T), which is +/- the transported normal -- reading the stored
    // one there could return the wrong sign and bend the extension away from the curve,
    // which is worse than the straight tangent it replaced.
    return dot((samples.tangents[i + 1] - samples.tangents[i]) / span, aboutAxis);
}

/** Frames sampled per curved edge at build time. One batched kernel call each. */
export const CM_FRAME_SAMPLES = 100;

/**
 * Sample one edge's frames in traversal order, with a single batched kernel call.
 *
 * getFrameAtArcLength used to issue its own unbatched evEdgeCurvature every time it
 * was asked for a frame, and mapSinglePoint asks twice per point -- once on the
 * from-path and once on the to-path. Mapping N points therefore cost 2N kernel round
 * trips. evEdgeCurvatures takes a parameters ARRAY, so the whole edge can be
 * evaluated once here and every later lookup becomes arithmetic.
 *
 * Parameters are requested at the same arc-length fractions the old per-point call
 * would have used, so at a sample the result is bit-identical to before; only the
 * interpolation between samples is new.
 *
 * Tangents are stored already turned into traversal direction. Normals are stored raw
 * (+N from the kernel); the cumulative sign correction stays where it was, in
 * getFrameAtArcLength.
 */
function sampleEdgeFrames(context is Context, edge is Query, stdDir is boolean) returns map
{
    var parameters = [];
    for (var j = 0; j < CM_FRAME_SAMPLES; j += 1)
    {
        var frac = j / (CM_FRAME_SAMPLES - 1);
        parameters = append(parameters, stdDir ? frac : 1 - frac);
    }

    var raw = evEdgeCurvatures(context, {
        "edge"                      : edge,
        "parameters"                : parameters,
        "arcLengthParameterization" : true
    });

    var origins  = [];
    var tangents = [];
    var normals  = [];
    for (var j = 0; j < CM_FRAME_SAMPLES; j += 1)
    {
        var fr = raw[j].frame;
        origins  = append(origins,  fr.origin);
        tangents = append(tangents, stdDir ? fr.zAxis : -1 * fr.zAxis);
        normals  = append(normals,  fr.xAxis);
    }

    return { "origins": origins, "tangents": tangents, "normals": normals };
}

/**
 * The sampled frame at a local traversal arc length.
 *
 * Position is cubic Hermite with the stored unit tangents as slopes -- d(origin)/d(arc)
 * IS the tangent, so those slopes are exact rather than fitted, and the interpolant
 * osculates the real curve. Samples are uniform in arc length by construction, so the
 * span is constant and the index is a division rather than a search.
 */
function frameAtLocalArc(edgeDat is map, localArc is ValueWithUnits) returns map
{
    var samples = edgeDat.frameSamples;
    var last    = CM_FRAME_SAMPLES - 1;
    var span    = edgeDat.length / last;

    var t = localArc / span;
    var i = floor(t);
    if (i < 0)         { i = 0; }
    if (i > last - 1)  { i = last - 1; }

    var f = t - i;
    if (f < 0) { f = 0; }
    if (f > 1) { f = 1; }

    var t0 = samples.tangents[i];
    var t1 = samples.tangents[i + 1];

    var f2 = f * f;
    var f3 = f2 * f;
    var origin = (2 * f3 - 3 * f2 + 1) * samples.origins[i]
        + (f3 - 2 * f2 + f) * span * t0
        + (-2 * f3 + 3 * f2) * samples.origins[i + 1]
        + (f3 - f2) * span * t1;

    var tangent = (1 - f) * t0 + f * t1;
    tangent = (norm(tangent) < 1e-9) ? t0 : normalize(tangent);

    // Where curvature is ~0 the kernel normal is arbitrary, so two neighbours can
    // point opposite ways and cancel. Fall back to the nearer sample, then to any
    // perpendicular -- the same situation the old code met one point at a time.
    var normal = (1 - f) * samples.normals[i] + f * samples.normals[i + 1];
    normal = normal - dot(normal, tangent) * tangent;
    if (norm(normal) < 1e-9)
    {
        var nearer = (f < 0.5) ? samples.normals[i] : samples.normals[i + 1];
        normal = nearer - dot(nearer, tangent) * tangent;
    }
    if (norm(normal) < 1e-9)
    {
        normal = perpendicularVector(tangent);
    }

    return { "origin": origin, "tangent": tangent, "normal": normalize(normal) };
}

/**
 * A frame at any arc-length along the path, continuous by construction.
 *
 * Handles multi-edge chains and reversed traversal (stdDir=false). The normal is
 * transported along the whole path at build time rather than derived per point, so it
 * never flips: there is no inflection handling here and no sign to track.
 *
 * Past either end the path is continued along its own osculating circle, not along a
 * straight tangent -- consumers shift arc lengths without clamping, so running off the
 * end is ordinary rather than exceptional.
 *
 * @param arcLength {ValueWithUnits} : global arc-length position; outside [0, totalLength]
 *          the frame is extrapolated rather than clamped.
 * @returns {map} :
 *   "frame"     {CoordSystem} - zAxis = tangent, xAxis = transported normal
 *   "sign"      {number}      - always +1; kept so callers that compare a from-sign
 *                               against a to-sign collapse to "flip iff flipToNormal"
 *   "edgeIndex" {number}      - index of the edge containing this position
 */
export function getFrameAtArcLength(context is Context, frenetPath is map, arcLength) returns map
{
    var edgeData    = frenetPath.edgeData;
    var totalLength = frenetPath.totalLength;

    // 1. Out-of-bounds: continue the path along its own osculating circle.
    //
    // This is not a rare guard. s_to = toRefArc + (s_from - fromRefArc) in mapSinglePoint
    // is an UNCLAMPED shift, so any difference in length between the two references, or an
    // off-centre reference point, pushes ordinary points past the end.
    //
    // A straight tangent extension leaves the true curve by d^2/2R: on a 200 mm tip radius
    // that is 0.25 mm only 10 mm past the end. Carrying the boundary curvature through
    // costs nothing -- the sampled table already knows it -- and collapses back to exactly
    // the old straight line as curvature goes to zero.
    if (arcLength < 0 * meter || arcLength > totalLength)
    {
        var boundaryArc = (arcLength < 0 * meter) ? 0 * meter : totalLength;
        var overflow    = arcLength - boundaryArc;   // negative at start, positive at end
        var boundary    = getFrameAtArcLength(context, frenetPath, boundaryArc);

        var tangent  = boundary.frame.zAxis;
        var toCentre = boundary.frame.xAxis;
        var kappa    = endCurvature(frenetPath, boundaryArc, toCentre);
        var theta    = kappa * overflow;
        var straight = (abs(theta) < 1e-7);

        var alongTangent = straight ? overflow : sin(theta * radian) / kappa;
        var towardCentre = straight ? 0 * meter : (1 - cos(theta * radian)) / kappa;
        var extTan       = straight
            ? tangent
            : normalize(cos(theta * radian) * tangent + sin(theta * radian) * toCentre);
        // The normal turns with the tangent: on the circle it is -sin(theta) T + cos(theta) N.
        // Keeping the boundary normal (as this did) leaves it theta off perpendicular to the
        // rotated tangent, and coordSystem's perpendicularVectors precondition fails once the
        // overflow on a curved end is large enough.
        var extNormal    = straight
            ? toCentre
            : normalize(-sin(theta * radian) * tangent + cos(theta * radian) * toCentre);

        var extPos   = boundary.frame.origin + alongTangent * tangent + towardCentre * toCentre;
        var extFrame = coordSystem(extPos, extNormal, extTan);

        return mergeMaps(boundary, { "frame": extFrame });
    }

    // 2. Find the edge whose span contains arcLength, by bisection on startArcLength.
    var lo = 0;
    var hi = size(edgeData) - 1;
    while (lo < hi)
    {
        var mid = ceil((lo + hi) / 2);
        if (edgeData[mid].startArcLength <= arcLength)
        {
            lo = mid;
        }
        else
        {
            hi = mid - 1;
        }
    }
    var edgeIdx = lo;

    var edgeDat = edgeData[edgeIdx];

    // 3. Local arc-length within this edge (from its traversal start)
    var localArc = arcLength - edgeDat.startArcLength;

    // 4. The normal is transported along the path, so it is continuous by construction
    //    and there is no sign to correct. The field is still reported, as +1, because
    //    callers compare a from-frame's sign against a to-frame's to decide whether to
    //    invert -- with both always +1 that comparison correctly collapses to "invert
    //    only when the user asked for flipToNormal".
    var sign = 1;

    var fopts        = frenetPath.frameNormalOptions;
    var binormalMode = (fopts.mode == FrameNormalMode.BINORMAL);

    var frame;

    if (edgeDat.isLine && edgeDat.frameSamples != undefined)
    {
        // 6a'. Near-linear spline promoted to line mode: the curve's own position and tangent, the line
        // logic's normal made perpendicular to that tangent (the tangent turns by well under a degree).
        var sampledLine = frameAtLocalArc(edgeDat, localArc);
        var lineNormal  = binormalMode ? planeNormalAxis(fopts.ref, sampledLine.tangent) : edgeDat.transportedNormal;
        lineNormal = lineNormal - dot(lineNormal, sampledLine.tangent) * sampledLine.tangent;
        lineNormal = (norm(lineNormal) < 1e-9) ? sampledLine.normal : normalize(lineNormal);
        frame = coordSystem(sampledLine.origin, lineNormal, sampledLine.tangent);
    }
    else if (edgeDat.isLine)
    {
        // 6a. Line: interpolate position along traversal direction.
        var position = edgeDat.lineStartPt + localArc * edgeDat.lineFrame.zAxis;
        if (binormalMode)
        {
            // In-plane normal from the supplied plane normal; tangent = line direction.
            frame = coordSystem(position, planeNormalAxis(fopts.ref, edgeDat.lineFrame.zAxis), edgeDat.lineFrame.zAxis);
        }
        else
        {
            frame = coordSystem(position, edgeDat.transportedNormal, edgeDat.lineFrame.zAxis);
        }
    }
    else
    {
        // 6b. Curved: read the frame sampled at build time. Tangents in the table are
        // already in traversal direction, so no stdDir flip is needed here. frameAtLocalArc
        // clamps its own index, which absorbs the float overshoot at the boundary
        // (arcLength == totalLength giving localArc = length + eps).
        var sampled = frameAtLocalArc(edgeDat, localArc);

        frame = coordSystem(sampled.origin, sampled.normal, sampled.tangent);
    }

    // BINORMAL: replace the curvature normal with the in-plane normal N = ref x tangent.
    // The evEdgeCurvature call above already provides the tangent (zAxis), so we only rebuild
    // the normal. (We tried evaluateSpline for a tangent-only frame: it is also a kernel call,
    // but it takes a raw BSpline parameter, so it needs a per-sample arc-length -> parameter
    // conversion [parameterAtArcLength] that evEdgeCurvature does internally for free — net
    // slower. So we keep evEdgeCurvature and override only the normal.)
    if (binormalMode)
    {
        frame = coordSystem(frame.origin, planeNormalAxis(fopts.ref, frame.zAxis), frame.zAxis);
    }

    return {
        "frame"    : frame,
        "sign"     : sign,
        "edgeIndex": edgeIdx
    };
}


// ============================================================================
// projectOntoFrenetPath
// ============================================================================

/**
 * Project a point onto a FrenetPath and return the global arc-length position.
 *
 * Foot-point search on the frames the path already sampled at build time -- no spline
 * evaluation, no kernel call. A line edge is solved in closed form; a curved edge by
 * Newton on dot(P - A(s), T(s)) = 0 over the Hermite position table, the same table
 * getFrameAtArcLength reads, so the arc found here and the frame read there share one
 * parameterisation. (The old projection fitted the approximated BSpline and converted its
 * parameter through a separate trapezoid table: 20-80 native spline evaluations per point,
 * and two arc-length measures that differed mid-edge.)
 *
 * With a hint, only the hinted edge (seeded at the hinted arc) and its two neighbours are
 * searched -- consecutive samples of a curve move little. Without one, a coarse scan of
 * every edge's table picks the seed, so an unordered point (a Deform vertex) still finds
 * the global nearest foot.
 *
 * @param frenetPath {map}    - result from buildFrenetPath
 * @param point      {Vector} - query point with units
 * @param hint               - optional { "edgeIndex", "param" } from a previous call
 *                             ("param" is the local arc on that edge); undefined = full scan.
 * @returns {map} :
 *   "arcLength" {ValueWithUnits} - arc-length along the path
 *   "hint"      {map}            - { "edgeIndex", "param" } for next call
 */
export function projectOntoFrenetPath(frenetPath is map, point is Vector, hint) returns map
{
    var edgeData = frenetPath.edgeData;
    var nEdges   = size(edgeData);

    // Candidate (edge, seed arc) pairs.
    var centre;
    var centreSeed;
    if (hint != undefined && hint.edgeIndex >= 0 && hint.edgeIndex < nEdges)
    {
        centre     = hint.edgeIndex;
        centreSeed = (hint.param is ValueWithUnits) ? hint.param : 0.5 * edgeData[centre].length;
    }
    else
    {
        var coarse = coarseNearest(edgeData, point);
        centre     = coarse.edge;
        centreSeed = coarse.localArc;
    }
    var candidates = [{ "edge": centre, "seed": centreSeed }];
    if (centre > 0)
    {
        candidates = append(candidates, { "edge": centre - 1, "seed": edgeData[centre - 1].length });
    }
    if (centre < nEdges - 1)
    {
        candidates = append(candidates, { "edge": centre + 1, "seed": 0 * meter });
    }

    var bestDist     = inf * meter;
    var bestEdgeIdx  = centre;
    var bestLocalArc = 0 * meter;
    for (var c in candidates)
    {
        var foot = footOnEdge(edgeData[c.edge], point, c.seed);
        if (foot.distance < bestDist)
        {
            bestDist     = foot.distance;
            bestEdgeIdx  = c.edge;
            bestLocalArc = foot.localArc;
        }
    }

    return {
        "arcLength" : edgeData[bestEdgeIdx].startArcLength + bestLocalArc,
        "hint"      : { "edgeIndex": bestEdgeIdx, "param": bestLocalArc }
    };
}

/** Table stride of the coarse scan; the best stride sample is then refined by Newton. */
const CM_PROJECTION_STRIDE = 5;

/**
 * The edge and local arc of the table sample nearest `point`, over the whole path: every
 * CM_PROJECTION_STRIDE-th sample of each curved edge (and its last), closed form on lines.
 */
function coarseNearest(edgeData is array, point is Vector) returns map
{
    var bestEdge = 0;
    var bestArc  = 0 * meter;
    var bestDist = inf * meter;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var edgeDat = edgeData[i];
        if (isExactLine(edgeDat))
        {
            var t = clamp(dot(point - edgeDat.lineStartPt, edgeDat.lineFrame.zAxis), 0 * meter, edgeDat.length);
            var dLine = norm(point - (edgeDat.lineStartPt + t * edgeDat.lineFrame.zAxis));
            if (dLine < bestDist)
            {
                bestDist = dLine;
                bestEdge = i;
                bestArc  = t;
            }
            continue;
        }
        var origins = edgeDat.frameSamples.origins;
        var last    = size(origins) - 1;
        var span    = edgeDat.length / last;
        var j = 0;
        while (true)
        {
            var d = norm(point - origins[j]);
            if (d < bestDist)
            {
                bestDist = d;
                bestEdge = i;
                bestArc  = j * span;
            }
            if (j == last)
            {
                break;
            }
            j = min(j + CM_PROJECTION_STRIDE, last);
        }
    }
    return { "edge": bestEdge, "localArc": bestArc };
}

/**
 * The foot of `point` on one edge: closed form on a line, Newton from `seed` on a curved
 * edge's Hermite table, clamped to the edge. Returns { localArc, distance }.
 *
 * Newton step ds = dot(P - A, T) / (1 - dot(P - A, dT/ds)), the curvature term from the
 * table cell. The denominator is floored so a point near the centre of curvature (where
 * several feet exist) moves conservatively instead of jumping across the edge.
 */
function footOnEdge(edgeDat is map, point is Vector, seed is ValueWithUnits) returns map
{
    var length = edgeDat.length;
    if (isExactLine(edgeDat))
    {
        var t = clamp(dot(point - edgeDat.lineStartPt, edgeDat.lineFrame.zAxis), 0 * meter, length);
        return { "localArc": t, "distance": norm(point - (edgeDat.lineStartPt + t * edgeDat.lineFrame.zAxis)) };
    }

    var samples = edgeDat.frameSamples;
    var last    = size(samples.origins) - 1;
    var span    = length / last;
    var s       = clamp(seed, 0 * meter, length);
    for (var k = 0; k < 12; k += 1)
    {
        var cell  = hermiteCell(samples, span, last, s);
        var d     = point - cell.origin;
        var g     = dot(d, cell.tangent);
        var denom = 1 - dot(d, cell.tangentRate);
        if (denom < 0.2)
        {
            denom = 0.2;
        }
        var next  = clamp(s + g / denom, 0 * meter, length);
        var moved = abs(next - s);
        s = next;
        if (moved < 1e-10 * meter)
        {
            break;
        }
    }
    var finalCell = hermiteCell(samples, span, last, s);
    return { "localArc": s, "distance": norm(point - finalCell.origin) };
}

/**
 * Hermite position, blended unit tangent and the cell's tangent rate (dT/ds) at a local
 * arc -- the position half of frameAtLocalArc, without the normal.
 */
function hermiteCell(samples is map, span is ValueWithUnits, last is number, localArc is ValueWithUnits) returns map
{
    var t = localArc / span;
    var i = floor(t);
    if (i < 0)        { i = 0; }
    if (i > last - 1) { i = last - 1; }
    var f = t - i;
    if (f < 0) { f = 0; }
    if (f > 1) { f = 1; }

    var t0 = samples.tangents[i];
    var t1 = samples.tangents[i + 1];
    var f2 = f * f;
    var f3 = f2 * f;
    var origin = (2 * f3 - 3 * f2 + 1) * samples.origins[i]
        + (f3 - 2 * f2 + f) * span * t0
        + (-2 * f3 + 3 * f2) * samples.origins[i + 1]
        + (f3 - f2) * span * t1;
    var tangent = (1 - f) * t0 + f * t1;
    tangent = (norm(tangent) < 1e-9) ? t0 : normalize(tangent);
    return { "origin": origin, "tangent": tangent, "tangentRate": (t1 - t0) / span };
}


// ============================================================================
// getRefPoint
// ============================================================================

/**
 * Extract a world point from a vertex, mate connector, or planar face query.
 *
 * @param context  {Context}
 * @param refQuery {Query} - vertex, mate connector, or planar face
 * @returns {Vector} - 3D position with units
 */
export function getRefPoint(context is Context, refQuery is Query) returns Vector
{
    var pt = undefined;

    try silent { pt = evVertexPoint(context, { "vertex": refQuery }); }
    if (pt != undefined) return pt;

    try silent { pt = evMateConnector(context, { "mateConnector": refQuery }).origin; }
    if (pt != undefined) return pt;

    try silent { pt = evPlane(context, { "face": refQuery }).origin; }
    if (pt != undefined) return pt;

    throw regenError("Cannot evaluate reference point from selection");
}


// ============================================================================
// mapSinglePoint
// ============================================================================

/**
 * Map one world point from fromFrenetPath to toFrenetPath.
 *
 * Projects onto from-path, converts to Frenet coordinates, maps arc-length
 * linearly, and reconstructs in the to-frame with sign reconciliation.
 *
 * @param context        {Context}
 * @param fromFrenetPath {map}            - result from buildFrenetPath
 * @param toFrenetPath   {map}            - result from buildFrenetPath
 * @param fromRefArc     {ValueWithUnits}
 * @param toRefArc       {ValueWithUnits}
 * @param flipToNormal   {boolean}
 * @param pt             {Vector}         - source point with units
 * @param projHint               - previous hint from projectOntoFrenetPath, or undefined
 * @returns {map} : {
 *   "point"     : Vector          - mapped world position
 *   "edgeIndex" : number          - to-edge the mapped point landed on
 *   "sFrom"     : ValueWithUnits  - arc-length on from-path
 *   "offsetDir" : Vector          - to-frame xAxis (loft offset direction)
 *   "hint"      : map             - updated projection hint for next call
 * }
 */
export function mapSinglePoint(context is Context,
    fromFrenetPath is map, toFrenetPath is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits,
    flipToNormal is boolean, pt is Vector, projHint) returns map
{
    var projResult = projectOntoFrenetPath(fromFrenetPath, pt, projHint);
    var s_from     = projResult.arcLength;
    var fromResult = getFrameAtArcLength(context, fromFrenetPath, s_from);

    var localCoords = worldPointToFrenet(pt, fromResult);

    var s_to     = mappedArc(fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, s_from, localCoords[1]);
    var toResult = getFrameAtArcLength(context, toFrenetPath, s_to);

    var toSign = toResult.sign;
    if (flipToNormal)
        toSign = -1 * toSign;

    var toFrameResult = toResult;
    if (toSign != fromResult.sign)
    {
        var flippedFrame = coordSystem(toResult.frame.origin,
                                       -1 * toResult.frame.xAxis,
                                       toResult.frame.zAxis);
        toFrameResult = mergeMaps(toResult, { "frame": flippedFrame });
    }

    var toPoint = toFrameResult.frame.origin
        + toFrameResult.frame.xAxis * localCoords[1]
        + yAxis(toFrameResult.frame) * localCoords[2]
        + toFrameResult.frame.zAxis * localCoords[0];

    return {
        "point"     : toPoint,
        "edgeIndex" : toResult.edgeIndex,
        "sFrom"     : s_from,
        "offsetDir" : toFrameResult.frame.xAxis,
        "hint"      : projResult.hint
    };
}


// ============================================================================
// mapWorldPoints  (public API)
// ============================================================================

/**
 * Map an array of 3D world points from one FrenetPath to another using the
 * same per-point logic as the main wrapCurve mapping loop.
 *
 * @param context        {Context}
 * @param fromFrenetPath {map}     - result from buildFrenetPath for the source path
 * @param toFrenetPath   {map}     - result from buildFrenetPath for the target path
 * @param fromRefArc     {ValueWithUnits} - arc-length of the reference point on the from-path
 * @param toRefArc       {ValueWithUnits} - arc-length of the reference point on the to-path
 * @param flipToNormal   {boolean} - when true, invert the to-path normal sign
 * @param points         {array}   - array of Vector (3D world points with units)
 * @returns {array}                - array of Vector (mapped world points, same length)
 */
export function mapWorldPoints(context is Context,
                               fromFrenetPath is map,
                               toFrenetPath   is map,
                               fromRefArc     is ValueWithUnits,
                               toRefArc       is ValueWithUnits,
                               flipToNormal   is boolean,
                               points         is array) returns array
{
    var result = [];
    for (var i = 0; i < size(points); i += 1)
    {
        var r = mapSinglePoint(context, fromFrenetPath, toFrenetPath,
            fromRefArc, toRefArc, flipToNormal, points[i], undefined);
        result = append(result, r.point);
    }
    return result;
}


/**
 * mapWorldPoints for ORDERED points (samples along one curve): each projection is seeded
 * from the previous point's foot, so it searches three edges near there instead of the
 * whole path. For unordered points (a body's vertices) use mapWorldPoints, whose every
 * projection does the global coarse scan.
 */
export function mapWorldPointChain(context is Context,
                                   fromFrenetPath is map,
                                   toFrenetPath   is map,
                                   fromRefArc     is ValueWithUnits,
                                   toRefArc       is ValueWithUnits,
                                   flipToNormal   is boolean,
                                   points         is array) returns array
{
    var result = makeArray(size(points));
    var hint = undefined;
    for (var i = 0; i < size(points); i += 1)
    {
        var r = mapSinglePoint(context, fromFrenetPath, toFrenetPath,
            fromRefArc, toRefArc, flipToNormal, points[i], hint);
        result[i] = r.point;
        hint = r.hint;
    }
    return result;
}


// ============================================================================
// LINEAR-REGION FAST PATH
// ============================================================================
//
// Where the from- and to-reference are BOTH straight over a source edge's span,
// the Frenet wrap collapses to a single constant rigid transform: the isometric
// arc-length shift (s_to = toRefArc + (s_from - fromRefArc)) plus constant line
// frames make the arc-length dependence cancel. Such an edge can be copied and
// moved with opExtractWires + opTransform instead of sample-and-refit - which also
// preserves EXACT geometry (an arc stays a true arc, rational weights stay intact).
// Ineligible edges fall back to the normal mapping path, so this is a pure
// optimization with no behavior change.

// Master switch (flip to false to A/B against the sample-and-refit path).
export const CM_LINEAR_FASTPATH = true;

/**
 * Index of the edge whose span contains `arcLength` (last edge with
 * startArcLength <= arcLength) - the same rule getFrameAtArcLength uses.
 */
function edgeIndexAtArcLength(frenetPath is map, arcLength) returns number
{
    var edgeData = frenetPath.edgeData;
    var idx = 0;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        if (edgeData[i].startArcLength <= arcLength)
        {
            idx = i;
        }
    }
    return idx;
}

/**
 * The constant rigid transform mapping a point from the from-frame to the to-frame
 * at arc-length `sFrom`. In a doubly-linear region the frames are constant, so this
 * one transform is the exact deformation for the whole region; it reproduces
 * mapSinglePoint's reconstruction (with the same normal-sign reconciliation).
 * toWorld(toFrame) . fromWorld(fromFrame) is world -> from-local -> to-world, i.e.
 * origin + xAxis*normal + yAxis*binormal + zAxis*tangent.
 */
function linearRegionTransformAt(context is Context, fromMap is map, toMap is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits, flipToNormal is boolean, sFrom is ValueWithUnits) returns map
{
    var fromRes = getFrameAtArcLength(context, fromMap, sFrom);
    var toRes   = getFrameAtArcLength(context, toMap, toRefArc + (sFrom - fromRefArc));

    var toSign  = flipToNormal ? (-1 * toRes.sign) : toRes.sign;
    var toFrame = toRes.frame;
    if (toSign != fromRes.sign)
    {
        toFrame = coordSystem(toRes.frame.origin, -1 * toRes.frame.xAxis, toRes.frame.zAxis);
    }

    // offsetDir is the constant to-frame normal (xAxis) in the linear region -
    // the loft-offset direction, so offset curves are this transform + a translation.
    return {
        "transform" : toWorld(toFrame) * fromWorld(fromRes.frame),
        "offsetDir" : toFrame.xAxis
    };
}

/**
 * Decide whether the source edge sampled by `samplePts` (world points) lies wholly
 * within a region where both references are straight, and if so return the single
 * rigid transform to move it. Returns { "eligible" : boolean, "transform" : Transform }.
 *
 * Eligible when every sample projects onto the SAME from-edge and that edge is a
 * line, and the shifted arc-length span stays within a SINGLE line to-edge that is
 * in-bounds. The first-sample line check is a cheap reject for curved regions.
 */
export function linearRegionMove(context is Context, fromMap is map, toMap is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits, flipToNormal is boolean, samplePts is array) returns map
{
    // Both references need a line somewhere, or no span can be doubly straight.
    if (size(samplePts) == 0 || !pathHasLine(fromMap) || !pathHasLine(toMap))
    {
        return { "eligible" : false };
    }

    var proj0       = projectOntoFrenetPath(fromMap, samplePts[0], undefined);
    var fromEdgeIdx = proj0.hint.edgeIndex;
    if (!isExactLine(fromMap.edgeData[fromEdgeIdx]))
    {
        return { "eligible" : false };   // cheap reject: source is not over a line
    }
    // Cheap reject on the to side too, before projecting the other probes.
    var toArc0 = proj0.arcLength + (toRefArc - fromRefArc);
    if (toArc0 < 0 * meter || toArc0 > toMap.totalLength || !isExactLine(toMap.edgeData[edgeIndexAtArcLength(toMap, toArc0)]))
    {
        return { "eligible" : false };
    }

    var sMin = proj0.arcLength;
    var sMax = proj0.arcLength;
    var probeHint = proj0.hint;
    for (var k = 1; k < size(samplePts); k += 1)
    {
        var proj = projectOntoFrenetPath(fromMap, samplePts[k], probeHint);
        probeHint = proj.hint;
        if (proj.hint.edgeIndex != fromEdgeIdx)
        {
            return { "eligible" : false };   // straddles two from-edges
        }
        if (proj.arcLength < sMin) { sMin = proj.arcLength; }
        if (proj.arcLength > sMax) { sMax = proj.arcLength; }
    }

    // The shifted span must land inside a single line to-edge, in-bounds.
    var delta  = toRefArc - fromRefArc;
    var sToMin = sMin + delta;
    var sToMax = sMax + delta;
    if (sToMin < 0 * meter || sToMax > toMap.totalLength)
    {
        return { "eligible" : false };
    }
    var toIdx = edgeIndexAtArcLength(toMap, sToMin);
    if (toIdx != edgeIndexAtArcLength(toMap, sToMax) || !isExactLine(toMap.edgeData[toIdx]))
    {
        return { "eligible" : false };
    }

    var res = linearRegionTransformAt(context, fromMap, toMap,
                  fromRefArc, toRefArc, flipToNormal, (sMin + sMax) / 2);
    return {
        "eligible"  : true,
        "transform" : res.transform,
        "offsetDir" : res.offsetDir
    };
}


// ============================================================================
// alignIsolatedLineFrames
// ============================================================================

/**
 * Align xAxis of isolated line edges in a FrenetPath.
 *
 * Pass 1: promote near-linear BSpline edges to line mode when max lateral
 *         deviation < linearThreshold * edgeLength.
 * Pass 2: for isolated lines (no adjacent curve neighbor), borrow the to-path
 *         normal at the mapped arc-length as the xAxis.
 *
 * Lines adjacent to a curve already received a curve-context xAxis in
 * buildFrenetPath step 4.5 and are skipped by Pass 2.
 *
 * @param context          {Context}
 * @param fromFrenetPath   {map}            - result from buildFrenetPath
 * @param toFrenetPath     {map}            - result from buildFrenetPath
 * @param fromRefArc       {ValueWithUnits}
 * @param toRefArc         {ValueWithUnits}
 * @param linearThreshold  {number}         - max lateral deviation fraction (e.g. 0.001)
 * @returns {map} - updated fromFrenetPath with corrected lineFrames
 */
export function alignIsolatedLineFrames(context is Context,
    fromFrenetPath is map, toFrenetPath is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits,
    linearThreshold is number) returns map
{
    var fromEdgeData = fromFrenetPath.edgeData;

    // Pass 1: promote effectively-linear BSplines to line mode so Pass 2 can fix their xAxis.
    // Handles projected BSplines in the planar case that were not classified as CurveType.LINE.
    for (var i = 0; i < size(fromEdgeData); i += 1)
    {
        var ed = fromEdgeData[i];
        if (ed.isLine)  continue;

        var cps      = ed.bspline.controlPoints;
        var n        = size(cps);
        var p0       = cps[0];
        var p1       = cps[n - 1];
        var chord    = p1 - p0;
        var chordLen = norm(chord);
        if (chordLen.value < 1e-10)  continue;

        var chordDir = normalize(chord);
        var maxDev   = 0 * meter;
        for (var j = 1; j < n - 1; j += 1)
        {
            var diff    = cps[j] - p0;
            var lateral = norm(diff - dot(diff, chordDir) * chordDir);
            if (lateral > maxDev)  maxDev = lateral;
        }
        if (maxDev >= linearThreshold * ed.length)  continue;

        // Promote to line mode with a placeholder xAxis (overwritten in Pass 2)
        var traversalStartPt = ed.stdDir ? cps[0]     : cps[n - 1];
        var traversalEndPt   = ed.stdDir ? cps[n - 1] : cps[0];
        var tangent          = normalize(traversalEndPt - traversalStartPt);
        var refVec           = (abs(dot(tangent, vector(1, 0, 0))) < 0.9) ? vector(1, 0, 0) : vector(0, 1, 0);
        var tempXAxis        = normalize(refVec - dot(refVec, tangent) * tangent);
        // The edge already carries a transported normal per sample. It is near-linear,
        // so that normal barely turns across it; take the midpoint one and keep it, or
        // the line branch of getFrameAtArcLength would read an undefined field. Pass 2
        // may still overwrite it for an isolated line.
        var carried = ed.frameSamples.normals[floor(CM_FRAME_SAMPLES / 2)];
        carried = carried - dot(carried, tangent) * tangent;
        carried = (norm(carried) < 1e-9) ? tempXAxis : normalize(carried);

        fromEdgeData[i] = mergeMaps(ed, {
            "isLine"            : true,
            "lineStartPt"       : traversalStartPt,
            "lineFrame"         : coordSystem(traversalStartPt, carried, tangent),
            "transportedNormal" : carried
        });
    }

    // Pass 2: borrow to-path xAxis for isolated lines (no adjacent curve neighbor).
    for (var i = 0; i < size(fromEdgeData); i += 1)
    {
        var ed = fromEdgeData[i];
        if (!ed.isLine)  continue;

        var hasCurveCtx = (i > 0 && !fromEdgeData[i - 1].isLine) ||
                          (i + 1 < size(fromEdgeData) && !fromEdgeData[i + 1].isLine);
        if (hasCurveCtx)  continue;

        var midFromArc = ed.startArcLength + ed.length / 2;
        var midToArc   = toRefArc + (midFromArc - fromRefArc);
        var toXAxis    = getFrameAtArcLength(context, toFrenetPath, midToArc).frame.xAxis;

        var tangent   = ed.lineFrame.zAxis;
        var perpXAxis = toXAxis - dot(toXAxis, tangent) * tangent;
        if (norm(perpXAxis) > 1e-6)
        {
            fromEdgeData[i] = mergeMaps(ed, {
                "lineFrame"         : coordSystem(ed.lineFrame.origin, normalize(perpXAxis), tangent),
                "transportedNormal" : normalize(perpXAxis)
            });
        }
    }

    return mergeMaps(fromFrenetPath, { "edgeData": fromEdgeData });
}


// ============================================================================
// alignLineFramesBilateral
// ============================================================================

/**
 * Align isolated line frames in BOTH FrenetPaths so the from/to frames stay
 * aligned regardless of which path contains the straight line(s).
 *
 * Why: alignIsolatedLineFrames is one-directional — it only fixes isolated
 * lines on the first path by borrowing the second path's xAxis. When the
 * line lives on the to-path instead (e.g. flattening a bent body with Deform),
 * the to-path's line keeps its arbitrary world-axis heuristic from
 * lineFrenetFrame, producing a fixed rotational offset (often 90 deg) between
 * the from and to frames.
 *
 * Calls alignIsolatedLineFrames twice with swapped arguments:
 *   Pass A: fix from-path isolated lines using the original to-path frames.
 *   Pass B: fix to-path   isolated lines using the updated  from-path frames.
 *
 * Pass B reads the updated from-path so that any line that was just corrected
 * in Pass A is used as a real reference when the to-path borrows back.
 *
 * Use this in place of the one-directional alignIsolatedLineFrames whenever
 * either path may contain straight-line edges.
 *
 * @param context         {Context}
 * @param fromFrenetPath  {map}            - result from buildFrenetPath
 * @param toFrenetPath    {map}            - result from buildFrenetPath
 * @param fromRefArc      {ValueWithUnits} - reference arc-length on from-path
 * @param toRefArc        {ValueWithUnits} - reference arc-length on to-path
 * @param linearThreshold {number}         - max lateral deviation fraction (e.g. 0.001)
 * @returns {map} : { "fromFrenetPath": map, "toFrenetPath": map }
 */
export function alignLineFramesBilateral(context is Context,
    fromFrenetPath is map, toFrenetPath is map,
    fromRefArc is ValueWithUnits, toRefArc is ValueWithUnits,
    linearThreshold is number) returns map
{
    fromFrenetPath = alignIsolatedLineFrames(context, fromFrenetPath, toFrenetPath, fromRefArc, toRefArc, linearThreshold);
    toFrenetPath   = alignIsolatedLineFrames(context, toFrenetPath,   fromFrenetPath, toRefArc,   fromRefArc, linearThreshold);
    return { "fromFrenetPath": fromFrenetPath, "toFrenetPath": toFrenetPath };
}


// ============================================================================
// sampleSourceEdge
// ============================================================================

/**
 * Sample a source edge into world points and a chord arc-length table.
 *
 * Uses evEdgeTangentLines (correct for rational arcs; avoids the
 * evApproximateBSplineCurve + evaluateSpline pipeline which is unreliable
 * on rational BSplines).
 *
 * @param context      {Context}
 * @param edge         {Query}        - single source edge
 * @param samplingMode {SamplingMode} - LENGTH_BASED or CP_BASED
 * @param params       {map} : {
 *   "samplingDensity"    : ValueWithUnits  (used when LENGTH_BASED),
 *   "sourceCPMultiplier" : number          (used when CP_BASED),
 *   "srcBSpline"         : BSplineCurve   (required when CP_BASED)
 * }
 * @returns {map} : {
 *   "points"     : array of Vector          - sampled world positions
 *   "arcLengths" : array of ValueWithUnits  - cumulative chord arc-lengths
 *   "numSamples" : number
 * }
 */
export function sampleSourceEdge(context is Context, edge is Query,
    samplingMode is SamplingMode, params is map) returns map
{
    var numSamples;
    if (samplingMode == SamplingMode.CP_BASED)
    {
        numSamples = max([10, params.sourceCPMultiplier * size(params.srcBSpline.controlPoints)]);
    }
    else
    {
        var edgeLen = evLength(context, { "entities": edge });
        numSamples = max([10, ceil(edgeLen / params.samplingDensity) + 1]);
    }

    var lines = evEdgeTangentLines(context, {
        "edge"       : edge,
        "parameters" : range(0, 1, numSamples)
    });
    var points = mapArray(lines, function(x) { return x.origin; });

    var arcLengths = makeArray(size(points));
    arcLengths[0] = 0 * meter;
    for (var k = 1; k < size(points); k += 1)
    {
        arcLengths[k] = arcLengths[k - 1] + norm(points[k] - points[k - 1]);
    }

    return {
        "points"       : points,
        "arcLengths"   : arcLengths,
        "numSamples"   : numSamples,
        // The source tangents at the first and last sample (parameters 0 and 1), already
        // evaluated here -- callers need not ask the kernel for them again.
        "startTangent" : lines[0].direction,
        "endTangent"   : lines[size(lines) - 1].direction
    };
}


// ============================================================================
// mapEdgeJunctionTangent
// ============================================================================

/**
 * Transform a source tangent direction from one Frenet frame to another.
 *
 * Projects srcTangent onto the from-frame axes, then reconstructs in the
 * to-frame. Returns a normalized unitless direction vector.
 *
 * @param srcTangent {Vector} - unit direction in world space
 * @param fromResult {map}    - result from getFrameAtArcLength (from-path)
 * @param toResult   {map}    - result from getFrameAtArcLength (to-path, sign-reconciled)
 * @returns {Vector}
 */
export function mapEdgeJunctionTangent(srcTangent is Vector,
    fromResult is map, toResult is map) returns Vector
{
    var dir = dot(srcTangent, fromResult.frame.zAxis) * toResult.frame.zAxis
            + dot(srcTangent, fromResult.frame.xAxis) * toResult.frame.xAxis
            + dot(srcTangent, yAxis(fromResult.frame)) * yAxis(toResult.frame);
    return normalize(dir);
}


// ============================================================================
// mapEdgeJunctionCurvature
// ============================================================================

/**
 * Transform a source curvature vector from one Frenet frame to another.
 *
 * Projects srcCurvature onto the normal (xAxis) and binormal (yAxis) components
 * of the from-frame, then reconstructs in the to-frame. The tangent (zAxis)
 * component is discarded — curvature is always perpendicular to the tangent
 * for smooth curves; any residual tangential component is numerical noise.
 *
 * @param srcCurvature {Vector} - curvature vector in world space (units: 1/length)
 * @param fromResult   {map}    - result from getFrameAtArcLength (from-path)
 * @param toResult     {map}    - result from getFrameAtArcLength (to-path, sign-reconciled)
 * @returns {Vector} - mapped curvature vector in world space (units: 1/length)
 */
export function mapEdgeJunctionCurvature(srcCurvature is Vector,
    fromResult is map, toResult is map) returns Vector
{
    return dot(srcCurvature, fromResult.frame.xAxis) * toResult.frame.xAxis
         + dot(srcCurvature, yAxis(fromResult.frame)) * yAxis(toResult.frame);
}


// ============================================================================
// jostleG2Junctions
// ============================================================================

/**
 * Post-process wrapped-span BSplines to achieve G2 continuity at junctions.
 *
 * Builds an adjacency map (last CP of span i -> first CP of span k) and for
 * each matched junction: averages curvature from both sides, then jostles
 * P2 of the after-span and P_{m-2} of the before-span to match.
 *
 * Guard: shift < 20% of local segment scale (norm(P1-P0) * degree).
 * Deletes old bodies and creates new ones under fresh Ids derived from id.
 *
 * Endpoint second-derivative formulas (clamped B-spline, knot gaps dl1/dl2):
 *   P2      = P1 + dl2 * (d2t*(dl1+dl2)/(d*(d-1)) + (P1-P0)/dl1)
 *   P_{m-2} = (ae*PL + be*PT - Ke)/(ae+be)
 *             where ae=1/de1, be=1/de2, Ke=d2t*(de1+de2)/(d*(d-1))
 *
 * @param context         {Context}
 * @param id              {Id}     - feature Id; sub-Ids derived from this
 * @param wrappedBSplines {array}  - BSplineCurve maps, one per span
 * @param wrappedIds      {array}  - Id for each span body
 * @param matchTol        {ValueWithUnits} - endpoint-match tolerance
 * @param junctionCurvatures {array} - per-span mapped source curvature at span END (same indexing
 *   as wrappedBSplines). junctionCurvatures[k] is the mapped source curvature Vector at the end
 *   of span k (junction to span k+1), or undefined if not available. When defined, used as the
 *   G2 target instead of averaging both sides. Pass [] if no source curvature data available.
 * @returns {map} : {
 *   "bsplines"    : array  - updated BSplineCurve maps
 *   "ids"         : array  - updated Ids (replaced spans get new Ids)
 *   "edgeQueries" : array  - qCreatedBy edge queries for all final spans
 *   "bodyQueries" : array  - qCreatedBy body queries for all final spans
 * }
 */
export function jostleG2Junctions(context is Context, id is Id,
    wrappedBSplines is array, wrappedIds is array,
    matchTol is ValueWithUnits, junctionCurvatures is array,
    frontPlaneOnly is boolean) returns map
{
    // All the adjustment is arithmetic on the BSplines; each span it changed is then
    // replaced ONCE, however many of its ends moved (it used to be deleted and recreated
    // per adjusted end).
    const jostled  = jostleG2Splines(wrappedBSplines, matchTol, junctionCurvatures, frontPlaneOnly);
    const numSpans = size(wrappedBSplines);
    var wrappedCurrIds = wrappedIds;
    for (var k = 0; k < numSpans; k += 1)
    {
        if (!jostled.changed[k])
        {
            continue;
        }
        const newId = id + ("g2_cr_" ~ toString(k));
        opDeleteBodies(context, id + ("g2_del_" ~ toString(k)), { "entities": qCreatedBy(wrappedIds[k], EntityType.BODY) });
        opCreateBSplineCurve(context, newId, { "bSplineCurve": jostled.bsplines[k] });
        wrappedCurrIds[k] = newId;
    }

    var finalEdgeQueries = makeArray(numSpans);
    var finalBodyQueries = makeArray(numSpans);
    for (var k = 0; k < numSpans; k += 1)
    {
        finalEdgeQueries[k] = qCreatedBy(wrappedCurrIds[k], EntityType.EDGE);
        finalBodyQueries[k] = qCreatedBy(wrappedCurrIds[k], EntityType.BODY);
    }
    return {
        "bsplines"    : jostled.bsplines,
        "ids"         : wrappedCurrIds,
        "edgeQueries" : finalEdgeQueries,
        "bodyQueries" : finalBodyQueries
    };
}

/**
 * The G2 adjustment of [jostleG2Junctions] on the BSplines alone: no bodies are touched.
 *
 * @returns {map} : { "bsplines" : adjusted curves (same order), "changed" : [boolean] per span }
 */
export function jostleG2Splines(wrappedBSplines is array, matchTol is ValueWithUnits,
    junctionCurvatures is array, frontPlaneOnly is boolean) returns map
{
    var numSpans = size(wrappedBSplines);
    var changed  = makeArray(numSpans, false);

    // Build adjacency: spanNext[si] = index whose first CP matches si's last CP (-1 if none)
    var spanNext = [];
    for (var si = 0; si < numSpans; si += 1)
    {
        var cpsI   = wrappedBSplines[si].controlPoints;
        var endPtI = cpsI[size(cpsI) - 1];
        var found  = -1;
        for (var sk = 0; sk < numSpans; sk += 1)
        {
            if (sk != si && norm(wrappedBSplines[sk].controlPoints[0] - endPtI) < matchTol)
            {
                found = sk;
                break;
            }
        }
        spanNext = append(spanNext, found);
    }

    for (var sj = 0; sj < numSpans; sj += 1)
    {
        var sjNext = spanNext[sj];
        if (sjNext < 0)  continue;

        var splineBefore = wrappedBSplines[sj];
        var splineAfter  = wrappedBSplines[sjNext];

        // Only jostle at junctions on the front plane (Y = 0) when requested.
        if (frontPlaneOnly)
        {
            var junctionPt = splineBefore.controlPoints[size(splineBefore.controlPoints) - 1];
            if (abs(junctionPt[1]) > 1e-4 * meter)  continue;
        }
        var kB           = splineBefore.knots;
        var kA           = splineAfter.knots;

        var evalB  = evaluateSpline({ "spline": splineBefore, "parameters": [kB[size(kB) - 1]], "nDerivatives": 2 });
        var evalA  = evaluateSpline({ "spline": splineAfter,  "parameters": [kA[0]],             "nDerivatives": 2 });
        var d1B    = evalB[1][0];  var d2B = evalB[2][0];
        var d1A    = evalA[1][0];  var d2A = evalA[2][0];
        var d1B_sq = dot(d1B, d1B);
        var d1A_sq = dot(d1A, d1A);

        if (!(d1B_sq > 0 && d1A_sq > 0))  continue;

        var T_B          = d1B / sqrt(d1B_sq);
        var T_A          = d1A / sqrt(d1A_sq);
        var kappa_B      = (d2B - dot(d2B, T_B) * T_B) / d1B_sq;
        var kappa_A      = (d2A - dot(d2A, T_A) * T_A) / d1A_sq;
        // Option 2 — relative near-linear guard: skip jostle when either span's curvature is
        // less than 5% of the dominant side. This naturally suppresses jostling near inflection
        // points without needing an absolute threshold, and handles asymmetric span curvatures.
        var kappaScale  = max([norm(kappa_B), norm(kappa_A)]);
        var kappaLinTol = kappaScale * 0.05;
        if (norm(kappa_B) < kappaLinTol || norm(kappa_A) < kappaLinTol)
            continue;

        // Use mapped source curvature as G2 target when available (ground truth from source
        // geometry). Falls back to averaging both output sides when not available.
        var kappa_target;
        var kappa_source = (sj < size(junctionCurvatures)) ? junctionCurvatures[sj] : undefined;
        if (kappa_source != undefined)
        {
            kappa_target = kappa_source;
        }
        else
        {
            kappa_target = 0.5 * (kappa_B + kappa_A);
        }

        // Option 1 — inflection detector: if kappa_B and kappa_A point in opposite directions,
        // the junction is (or contains) a curvature sign-reversal. Force kappa_target to the
        // zero vector so the jostle targets κ=0 rather than a noisy near-zero direction from
        // the finite-difference estimate. The shift guard still prevents over-correction.
        var nKB = norm(kappa_B);
        var nKA = norm(kappa_A);
        if (nKB > 0 * (1 / meter) && nKA > 0 * (1 / meter))
        {
            if (dot(kappa_B / nKB, kappa_A / nKA) < 0)
                kappa_target = vector(0, 0, 0) / meter;
        }

        // Adjust P2 of splineAfter (start of sjNext span)
        {
            var d2tA = kappa_target * d1A_sq + dot(d2A, T_A) * T_A;
            var dA   = splineAfter.degree;
            var knA  = splineAfter.knots;
            var CPA  = splineAfter.controlPoints;
            if (size(CPA) >= 3 && dA >= 2)
            {
                var dl1 = knA[dA + 1] - knA[dA];
                var dl2 = knA[dA + 2] - knA[dA + 1];
                if (dl1 > 0 && dl2 > 0)
                {
                    var P0A  = CPA[0];
                    var P1A  = CPA[1];
                    var P2An = P1A + dl2 * (d2tA * (dl1 + dl2) / (dA * (dA - 1)) + (P1A - P0A) / dl1);
                    var shA  = norm(P2An - CPA[2]);
                    var scA  = norm(P1A - P0A) * dA;
                    if (scA > 0 * meter && shA > 0 * meter && shA < 0.2 * scA)
                    {
                        var ncA = CPA;
                        ncA[2] = P2An;
                        wrappedBSplines[sjNext] = mergeMaps(splineAfter, { "controlPoints": ncA });
                        changed[sjNext] = true;
                    }
                }
            }
        }

        // Adjust P_{m-2} of splineBefore (end of sj span).
        // Keep P_{m-1} (G1 CP) fixed; solve for P_{m-2} (G2 CP only).
        // Symmetric to the after-span: P_{m-2} = P_{m-1} + de2*(Ke - (P_m-P_{m-1})/de1)
        {
            var d2tB = kappa_target * d1B_sq + dot(d2B, T_B) * T_B;
            var dB   = splineBefore.degree;
            var knB  = splineBefore.knots;
            var CPB  = splineBefore.controlPoints;
            var kLB  = size(knB);
            var mB   = size(CPB);
            if (mB >= 4 && dB >= 2)
            {
                var de1 = knB[kLB - dB - 1] - knB[kLB - dB - 2];
                var de2 = knB[kLB - dB - 2] - knB[kLB - dB - 3];
                if (de1 > 0 && de2 > 0)
                {
                    var Ke   = d2tB * (de1 + de2) / (dB * (dB - 1));
                    var Pm   = CPB[mB - 1];
                    var Pm1  = CPB[mB - 2];
                    var Pm2n = Pm1 + de2 * (Ke - (Pm - Pm1) / de1);
                    var shB  = norm(Pm2n - CPB[mB - 3]);
                    var scB  = norm(Pm - Pm1) * dB;
                    if (scB > 0 * meter && shB > 0 * meter && shB < 0.2 * scB)
                    {
                        var ncB = CPB;
                        ncB[mB - 3] = Pm2n;
                        wrappedBSplines[sj] = mergeMaps(splineBefore, { "controlPoints": ncB });
                        changed[sj] = true;
                    }
                }
            }
        }
    }

    return { "bsplines" : wrappedBSplines, "changed" : changed };
}


// ============================================================================
// enforceEndpointDerivatives
// ============================================================================

/**
 * Correct endpoint control points of a clamped B-spline so the first derivative
 * at each end matches the supplied target velocity vector.
 *
 * Clamped endpoint derivative formula:
 *   C'(t0) = degree * (P1 - P0) / (knots[degree+1] - knots[degree])
 *     => P1 = P0 + startDeriv * (knots[degree+1] - knots[degree]) / degree
 *
 *   C'(t_end) = degree * (P_m - P_{m-1}) / (knots[m+d+1] - knots[m+d])
 *     => P_{m-1} = P_m - endDeriv * (knots[m+d+1] - knots[m+d]) / degree
 *
 * Guard: adjustment skipped when shift > 50 % of local span scale
 * (norm(P1-P0) * degree), to avoid over-correction on degenerate fits.
 *
 * @param bspline    {map}    - BSplineCurve map (controlPoints, knots, degree)
 * @param startDeriv {Vector} - target velocity at t0 (length units), or undefined
 * @param endDeriv   {Vector} - target velocity at t_end (length units), or undefined
 * @returns {map} - updated BSplineCurve map
 */
export function enforceEndpointDerivatives(bspline is map, startDeriv, endDeriv) returns map
{
    var cps = bspline.controlPoints;
    var kns = bspline.knots;
    var d   = bspline.degree;
    var m   = size(cps) - 1;
    if (m < 2 || d < 1)
        return bspline;

    var newCps = cps;

    if (startDeriv != undefined && norm(startDeriv) > 1e-10 * meter)
    {
        var dk0 = kns[d + 1] - kns[d];
        if (dk0 > 0)
        {
            var P1target = cps[0] + startDeriv * (dk0 / d);
            var scaleS   = norm(cps[1] - cps[0]) * d;
            var shiftS   = norm(P1target - cps[1]);
            if (scaleS > 0 * meter && shiftS < 0.2 * scaleS)
            {
                var nc = [];
                for (var ci = 0; ci <= m; ci += 1)
                    nc = append(nc, ci == 1 ? P1target : newCps[ci]);
                newCps = nc;
            }
        }
    }

    if (endDeriv != undefined && norm(endDeriv) > 1e-10 * meter)
    {
        var dkm = kns[m + d + 1] - kns[m + d];
        if (dkm > 0)
        {
            var Pm1target = cps[m] - endDeriv * (dkm / d);
            var scaleE    = norm(cps[m] - cps[m - 1]) * d;
            var shiftE    = norm(Pm1target - newCps[m - 1]);
            if (scaleE > 0 * meter && shiftE < 0.2 * scaleE)
            {
                var nc = [];
                for (var ci = 0; ci <= m; ci += 1)
                    nc = append(nc, ci == m - 1 ? Pm1target : newCps[ci]);
                newCps = nc;
            }
        }
    }

    return mergeMaps(bspline, { "controlPoints": newCps });
}


// ============================================================================
// debugDrawFrames
// ============================================================================

/**
 * Draw Frenet frames at evenly-spaced arc-length positions along a FrenetPath.
 *
 * Uses addDebugArrow directly so arrow length scales with path geometry
 * (1/3 of inter-sample spacing) rather than using a hardcoded 5cm length.
 * Also avoids the console println that debug(context, CoordSystem) emits.
 *
 * Colors: xAxis (normal) = RED, yAxis (binormal) = GREEN, zAxis (tangent) = BLUE
 */
export function debugDrawFrames(context is Context, frenetPath is map, numSamples is number)
{
    var totalLength = frenetPath.totalLength;
    var arrowLen    = totalLength / max([1, numSamples - 1]) / 3;
    var arrowRadius = arrowLen * 0.05;

    for (var i = 0; i < numSamples; i += 1)
    {
        var s      = totalLength * i / max([1, numSamples - 1]);
        var result = getFrameAtArcLength(context, frenetPath, s);
        var origin = result.frame.origin;

        addDebugArrow(context, origin, origin + arrowLen * result.frame.xAxis,  arrowRadius,           DebugColor.RED);
        addDebugArrow(context, origin, origin + arrowLen * yAxis(result.frame),  arrowRadius * (2 / 3), DebugColor.GREEN);
        addDebugArrow(context, origin, origin + arrowLen * result.frame.zAxis,   arrowRadius * 0.5,     DebugColor.BLUE);
    }
}


// ============================================================================
// transformEdges
// ============================================================================

/**
 * Transforms an array of edges from one Frenet path to another.
 * Returns an array of maps { "sourceEdge": Query, "wrappedEdge": Query, "wrappedBody": Query }.
 *
 * @param context   {Context}
 * @param id        {Id}
 * @param edgeArray {array}  : array of edge Queries to transform
 * @param fromMap   {map}    : result from buildFrenetPath (source reference)
 * @param toMap     {map}    : result from buildFrenetPath (target reference)
 * @param settings  {map}    : {
 *   fromRefArc, toRefArc, flipToNormal,
 *   samplingMode (SamplingMode), samplingDensity (LENGTH_BASED), sourceCPMultiplier (CP_BASED),
 *   approximationDegree, approximationMaxCPs, approximationTolerance
 * }
 * @returns {array} : [{ "sourceEdge": Query, "wrappedEdge": Query, "wrappedBody": Query }, ...]
 */
export function transformEdges(context is Context, id is Id, edgeArray is array, fromMap is map, toMap is map, settings is map) returns array
{
    // The rigid fast path needs BOTH references straight over the edge's span, so it can
    // only ever apply when both contain a line; otherwise skip its probe for every edge.
    const fastPathPossible = CM_LINEAR_FASTPATH && pathHasLine(fromMap) && pathHasLine(toMap);

    var result = [];
    for (var i = 0; i < size(edgeArray); i += 1)
    {
        var edge    = edgeArray[i];

        // Fast path: if the whole edge sits in a doubly-linear region, the wrap is
        // one rigid transform - copy and move the edge, preserving exact geometry
        // (arcs stay arcs, weights intact). Ineligible edges fall through below.
        if (fastPathPossible)
        {
            var probePts = mapArray(evEdgeTangentLines(context, {
                "edge"       : edge,
                "parameters" : [0, 0.25, 0.5, 0.75, 1]
            }), function(x) { return x.origin; });
            var lin = linearRegionMove(context, fromMap, toMap,
                settings.fromRefArc, settings.toRefArc, settings.flipToNormal, probePts);
            if (lin.eligible)
            {
                var linId = id + (toString(i) ~ "linmove");
                try
                {
                    opExtractWires(context, linId, { "edges": edge });
                    opTransform(context, linId + "xf", {
                        "bodies"    : qCreatedBy(linId, EntityType.BODY),
                        "transform" : lin.transform
                    });
                    result = append(result, {
                        "sourceEdge" : edge,
                        "wrappedEdge": qCreatedBy(linId, EntityType.EDGE),
                        "wrappedBody": qCreatedBy(linId, EntityType.BODY)
                    });
                }
                catch (e) { println("ERROR linearFastPath " ~ i ~ ": " ~ toString(e)); }
                continue;
            }
        }

        var edgeBSpline;
        if (settings.samplingMode == SamplingMode.CP_BASED || settings.keepDegree)
        {
            edgeBSpline = evApproximateBSplineCurve(context, { "edge": edge });
        }

        var numSamples;
        if (settings.samplingMode == SamplingMode.CP_BASED)
        {
            numSamples = max([10, settings.sourceCPMultiplier * size(edgeBSpline.controlPoints)]);
        }
        else
        {
            var edgeLen = evLength(context, { "entities": edge });
            numSamples = max([5, ceil(edgeLen / settings.samplingDensity) + 1]);
        }

        var srcPoints = mapArray(evEdgeTangentLines(context, {
            "edge"       : edge,
            "parameters" : range(0, 1, numSamples)
        }), function(x) { return x.origin; });

        // Ordered samples: each projection is seeded from the previous one.
        var mappedPoints = mapWorldPointChain(context, fromMap, toMap,
            settings.fromRefArc, settings.toRefArc, settings.flipToNormal, srcPoints);

        // Snap endpoints to pre-computed vertex-mapped positions so that all edges
        // sharing a source vertex produce BSplines with bit-identical endpoints.
        if (settings.vertexMap != undefined)
        {
            var edgeVerts = evaluateQuery(context, qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX));
            var nv = size(edgeVerts);
            if (nv == 2)
            {
                var vm0 = settings.vertexMap[toString(edgeVerts[0])];
                var vm1 = settings.vertexMap[toString(edgeVerts[1])];
                if (vm0 != undefined && vm1 != undefined)
                {
                    // Determine which pre-mapped vertex aligns with mappedPoints[0]
                    if (norm(mappedPoints[0] - vm0) <= norm(mappedPoints[0] - vm1))
                    {
                        mappedPoints[0]                    = vm0;
                        mappedPoints[size(mappedPoints) - 1] = vm1;
                    }
                    else
                    {
                        mappedPoints[0]                    = vm1;
                        mappedPoints[size(mappedPoints) - 1] = vm0;
                    }
                }
            }
            else if (nv == 1)
            {
                // Closed edge (full circle etc.) — same vertex at both ends
                var vm = settings.vertexMap[toString(edgeVerts[0])];
                if (vm != undefined)
                {
                    mappedPoints[0]                    = vm;
                    mappedPoints[size(mappedPoints) - 1] = vm;
                }
            }
            // nv == 0: degenerate edge, leave endpoints as-is
        }

        var approxDegree = (settings.keepDegree && edgeBSpline != undefined)
            ? max([settings.approximationDegree, edgeBSpline.degree])
            : settings.approximationDegree;

        // Fit freely, then put the clamped spline's end control points -- which ARE its end
        // points -- on the mapped ends. Same bit-identical shared endpoints as before, without
        // interpolateIndices, which makes the fitter far slower (lessons-learned: ~36 s).
        var approxDef = {
            "targets"            : [approximationTarget({ "positions": mappedPoints })],
            "tolerance"          : settings.approximationTolerance,
            "maxControlPoints"   : settings.approximationMaxCPs,
            "degree"             : approxDegree,
            "isPeriodic"         : false
        };
        var wrappedCurve = snapEndControlPoints(approximateSpline(context, approxDef)[0],
            mappedPoints[0], mappedPoints[size(mappedPoints) - 1]);

        var wrappedId = id + (toString(i) ~ "edge");
        try
        {
            opCreateBSplineCurve(context, wrappedId, { "bSplineCurve": wrappedCurve });
            result = append(result, {
                "sourceEdge" : edge,
                "wrappedEdge": qCreatedBy(wrappedId, EntityType.EDGE),
                "wrappedBody": qCreatedBy(wrappedId, EntityType.BODY)
            });
        }
        catch (e) { println("ERROR transformEdge " ~ i ~ ": " ~ toString(e)); }
    }
    return result;
}

/**
 * The same curve with its first and last control points moved to `first` and `last`. On
 * a clamped, non-periodic spline those control points are the curve's end points, so this
 * pins the ends exactly without constraining the fit.
 */
export function snapEndControlPoints(curve is BSplineCurve, first is Vector, last is Vector) returns BSplineCurve
{
    var cps = curve.controlPoints;
    cps[0] = first;
    cps[size(cps) - 1] = last;
    return mergeMaps(curve, { "controlPoints": cps }) as BSplineCurve;
}

/** True when any edge of the path is a line (the rigid fast path is only possible then). */
export function pathHasLine(frenetPath is map) returns boolean
{
    for (var edgeDat in frenetPath.edgeData)
    {
        if (edgeDat.isLine)
        {
            return true;
        }
    }
    return false;
}


// ============================================================================
// transformFacepoints
// ============================================================================

/**
 * Most interior guide points one face contributes. Each costs an opPoint and a constraint in
 * opFillSurface; past a few dozen the fill slows sharply and fails more often, while the
 * shape it can express does not improve.
 */
export const CM_MAX_FACE_GUIDE_POINTS = 25;

/** Interior samples per iso curve in CP-based sampling (the density field is hidden then). */
const CM_GUIDE_SAMPLES_PER_ISO = 5;

/**
 * Samples interior points from a face along isoparametric curves (which lie inside the
 * face, trimmed or not), maps them through the Frenet transform, and returns the mapped
 * positions for use as guide vertices in opFillSurface.
 *
 * Interior-only sampling (parameters 0 and 1 excluded) avoids duplicating points that are
 * already captured by the transformed boundary edges. At most CM_MAX_FACE_GUIDE_POINTS are
 * returned, thinned evenly. The iso-curve spacing follows the sampling mode: the density
 * in length-based sampling, a fixed count per curve in CP-based sampling.
 *
 * @param uMultiplier  {number} : u iso curve count = uMultiplier * u control-point dimension
 * @param vMultiplier  {number} : v iso curve count = vMultiplier * v control-point dimension
 * @param settings     {map}    : { fromRefArc, toRefArc, flipToNormal, samplingMode, samplingDensity, ... }
 * @returns {array} : mapped Vector positions (interior guide points)
 */
export function transformFacepoints(context is Context, id is Id, face is Query, uMultiplier is number, vMultiplier is number, fromMap is map, toMap is map, settings is map) returns array
{
    // 1. Get BSpline surface dimensions from the face approximation
    var surfData = evApproximateBSplineSurface(context, { "face": face });
    var bspl     = surfData.bSplineSurface;
    var nU       = uMultiplier * size(bspl.controlPoints);
    var nV       = vMultiplier * size(bspl.controlPoints[0]);

    // 2. Create isoparametric curves on the face
    var isoId  = id + "isoCurves";
    var uNames = [];
    var vNames = [];
    for (var k = 0; k < nU; k += 1) { uNames = append(uNames, "u" ~ k); }
    for (var k = 0; k < nV; k += 1) { vNames = append(vNames, "v" ~ k); }

    opCreateCurvesOnFace(context, isoId, {
        "curveDefinition" : [
            { "face": face, "creationType": FaceCurveCreationType.DIR1_AUTO_SPACED_ISO, "nCurves": nU, "names": uNames },
            { "face": face, "creationType": FaceCurveCreationType.DIR2_AUTO_SPACED_ISO, "nCurves": nV, "names": vNames }
        ]
    });
    var isoBodies = qCreatedBy(isoId, EntityType.BODY);

    // 3. Sample each iso curve at interior parameters; keep each curve's points in order so
    //    its projections can be chained.
    var lengthBased = settings.samplingMode == SamplingMode.LENGTH_BASED;
    var chains = [];
    var total  = 0;
    for (var e in evaluateQuery(context, qCreatedBy(isoId, EntityType.EDGE)))
    {
        var nSamp = CM_GUIDE_SAMPLES_PER_ISO + 2;
        if (lengthBased)
        {
            nSamp = max([3, ceil(evLength(context, { "entities": e }) / settings.samplingDensity) + 1]);
        }
        var params = [];
        for (var k = 1; k < nSamp - 1; k += 1)
        {
            params = append(params, k / (nSamp - 1));
        }
        if (size(params) == 0) { continue; }
        var pts = mapArray(evEdgeTangentLines(context, { "edge": e, "parameters": params }), function(x) { return x.origin; });
        chains = append(chains, pts);
        total += size(pts);
    }

    // 4. Delete iso curve bodies — they were only needed for sampling
    opDeleteBodies(context, id + "deleteIso", { "entities": isoBodies });

    // 5. Map each curve's points as a chain, then thin evenly to the cap.
    var mapped = [];
    for (var pts in chains)
    {
        mapped = concatenateArrays([mapped, mapWorldPointChain(context, fromMap, toMap,
                        settings.fromRefArc, settings.toRefArc, settings.flipToNormal, pts)]);
    }
    if (size(mapped) <= CM_MAX_FACE_GUIDE_POINTS)
    {
        return mapped;
    }
    var thinned = [];
    for (var k = 0; k < CM_MAX_FACE_GUIDE_POINTS; k += 1)
    {
        thinned = append(thinned, mapped[floor(k * size(mapped) / CM_MAX_FACE_GUIDE_POINTS)]);
    }
    return thinned;
}

// ============================================================================
// EXACT SPAN ENDS (2026-09-23)
// ============================================================================

/**
 * Relative curvature change across a to-edge boundary above which the path is NOT smooth
 * there: a line meeting an arc, or two arcs of clearly different radius. A span fitted
 * across such a boundary rings, because the wrapped curve's own curvature jumps there;
 * breaking it lets each side keep its curvature.
 */
export const CM_CURVATURE_JUMP_REL = 0.1;

/** Curvature below this (radius above 10 m) counts as straight for the jump test. */
export const CM_CURVATURE_FLOOR = 0.1 / meter;

/** Arc offset used to read "just before" / "just after" a junction. */
const CM_SIDE_EPSILON = 1e-7 * meter;

/** The zero curvature vector. */
function zeroCurvature() returns Vector
{
    return vector(0, 0, 0) / meter;
}

/**
 * The curvature vector (dT/ds, pointing to the centre) at the start or end of one edge, in
 * traversal order, from its frame table. Zero on a line.
 */
export function edgeEndCurvature(edgeDat is map, atEnd is boolean) returns Vector
{
    if (edgeDat.isLine || edgeDat.frameSamples == undefined)
    {
        return zeroCurvature();
    }
    var tangents = edgeDat.frameSamples.tangents;
    var last     = size(tangents) - 1;
    var span     = edgeDat.length / last;
    return atEnd ? (tangents[last] - tangents[last - 1]) / span : (tangents[1] - tangents[0]) / span;
}

/**
 * The curvature vector at a global arc, read from the frame table (zero on lines). Clamped
 * to the path. `side` < 0 reads just before `arc`, > 0 just after, 0 at it -- at an edge
 * boundary that picks the edge.
 */
export function curvatureVectorAtArc(frenetPath is map, arc is ValueWithUnits, side is number) returns Vector
{
    var s = arc + side * CM_SIDE_EPSILON;
    if (s < 0 * meter)                 { s = 0 * meter; }
    if (s > frenetPath.totalLength)    { s = frenetPath.totalLength; }
    var edgeData = frenetPath.edgeData;
    var idx = 0;
    for (var i = 0; i < size(edgeData); i += 1)
    {
        if (edgeData[i].startArcLength <= s)
        {
            idx = i;
        }
    }
    var edgeDat = edgeData[idx];
    if (edgeDat.isLine || edgeDat.frameSamples == undefined)
    {
        return zeroCurvature();
    }
    var samples = edgeDat.frameSamples;
    var last    = size(samples.origins) - 1;
    return hermiteCell(samples, edgeDat.length / last, last, s - edgeDat.startArcLength).tangentRate;
}

/**
 * The to-frame with the user's normal flip applied -- the same reconciliation every mapping
 * site repeats (signs are always +1 with the transported normal, so it reduces to
 * "invert iff flipToNormal").
 */
export function reconciledToFrame(toResult is map, fromResult is map, flipToNormal is boolean) returns map
{
    var toSign = flipToNormal ? -1 * toResult.sign : toResult.sign;
    if (toSign == fromResult.sign)
    {
        return toResult;
    }
    return mergeMaps(toResult, { "frame": coordSystem(toResult.frame.origin, -1 * toResult.frame.xAxis, toResult.frame.zAxis) });
}

/**
 * The exact image of a source tangent under the bending map, at one point.
 *
 * The map keeps the along-reference position and the (normal, binormal) coordinates, so a
 * source point moving by d(sigma) along srcTangent moves the reference foot by
 * a_t / (1 - (p - A_from).kappa_from) and its image by
 *     a_t * (1 - (q - A_to).kappa_to) / (1 - (p - A_from).kappa_from) * T_to
 *   + a_n * N_to + a_b * B_to
 * with (a_t, a_n, a_b) the source tangent in the from-frame, p the source point, q its
 * image. mapEdgeJunctionTangent is this with both stretch factors taken as 1, i.e. exact
 * only on the references themselves; 20 mm off a 200 mm tip radius it was ~3 deg out.
 *
 * @returns {map} : "tangent" (unit), "velocity" (unnormalized, per unit source arc),
 *      "alongRate" (reference arc per unit source arc), "kappa" (to-side curvature vector),
 *      "axis" (to-side tangent) -- the last three for [offsetCurveTangent].
 */
export function mapTangentExact(srcTangent is Vector, srcPoint is Vector, fromResult is map, toFrameResult is map,
    kappaFrom is Vector, kappaTo is Vector, mappedPoint is Vector) returns map
{
    var ff = fromResult.frame;
    var tf = toFrameResult.frame;
    var aT = dot(srcTangent, ff.zAxis);
    var aN = dot(srcTangent, ff.xAxis);
    var aB = dot(srcTangent, yAxis(ff));
    var scaleFrom = 1 - dot(srcPoint - ff.origin, kappaFrom);
    var scaleTo   = 1 - dot(mappedPoint - tf.origin, kappaTo);
    // Past a centre of curvature the parallel curve folds; keep the plain rotation there.
    if (scaleFrom < 0.05 || scaleTo < 0.05)
    {
        scaleFrom = 1;
        scaleTo   = 1;
    }
    var alongRate = aT / scaleFrom;
    var velocity  = (aT * scaleTo / scaleFrom) * tf.zAxis + aN * tf.xAxis + aB * yAxis(tf);
    var n = norm(velocity);
    return {
        "tangent"   : (n < 1e-12) ? tf.zAxis : velocity / n,
        "velocity"  : velocity,
        "alongRate" : alongRate,
        "kappa"     : kappaTo,
        "axis"      : tf.zAxis
    };
}

/**
 * Unit tangent of an offset curve Q = image + d * offsetDir at the same point, from the
 * wrapped curve's [mapTangentExact] record: the offset direction turns with the reference,
 * d(offsetDir)/ds = -(kappa . offsetDir) T, which the wrapped tangent does not carry.
 */
export function offsetCurveTangent(info is map, signedOffset is ValueWithUnits, offsetDir is Vector) returns Vector
{
    var v = info.velocity - signedOffset * dot(info.kappa, offsetDir) * info.alongRate * info.axis;
    var n = norm(v);
    return (n < 1e-12) ? info.tangent : v / n;
}

/**
 * The exact tangent record at a source point, read on one side (`side` -1 before, +1 after,
 * 0 at) of its arc on both paths. Frames and curvatures come from that side, so a junction
 * at a to-path corner gives each span its own frame instead of both the after-side one.
 */
export function exactTangentAt(context is Context, fromPath is map, toPath is map,
    sFrom is ValueWithUnits, sTo is ValueWithUnits, side is number, flipToNormal is boolean,
    srcTangent is Vector, srcPoint is Vector, mappedPoint is Vector) returns map
{
    var fromResult = getFrameAtArcLength(context, fromPath, clampArc(fromPath, sFrom + side * CM_SIDE_EPSILON));
    var toResult   = getFrameAtArcLength(context, toPath, sTo + side * CM_SIDE_EPSILON);
    var toFrame    = reconciledToFrame(toResult, fromResult, flipToNormal);
    return mapTangentExact(srcTangent, srcPoint, fromResult, toFrame,
        curvatureVectorAtArc(fromPath, sFrom, side), curvatureVectorAtArc(toPath, sTo, side), mappedPoint);
}

function clampArc(frenetPath is map, s is ValueWithUnits) returns ValueWithUnits
{
    if (s < 0 * meter)              { return 0 * meter; }
    if (s > frenetPath.totalLength) { return frenetPath.totalLength; }
    return s;
}

/**
 * The exact ruled surface between two curves that share a parameterization (same degree,
 * knots and control-point count, non-rational): u along the curves, v straight across at
 * degree 1, the control net the two polygons side by side. Returns false, having built
 * nothing, when the pair does not qualify or the kernel refuses; the caller lofts instead.
 */
export function ruledSurfaceBetween(context is Context, id is Id, a is BSplineCurve, b is BSplineCurve) returns boolean
{
    if (a.degree != b.degree || a.isPeriodic || b.isPeriodic || a.weights != undefined || b.weights != undefined
        || size(a.controlPoints) != size(b.controlPoints) || a.knots != b.knots)
    {
        return false;
    }
    var grid = [];
    for (var i = 0; i < size(a.controlPoints); i += 1)
    {
        grid = append(grid, [a.controlPoints[i], b.controlPoints[i]]);
    }
    var refusal = undefined;
    try silent
    {
        opCreateBSplineSurface(context, id, { "bSplineSurface" : bSplineSurface({
                            "uDegree" : a.degree,
                            "vDegree" : 1,
                            "isUPeriodic" : false,
                            "isVPeriodic" : false,
                            "controlPoints" : controlPointMatrix(grid),
                            "uKnots" : a.knots,
                            "vKnots" : knotArray([0, 0, 1, 1])
                        }) });
    }
    catch (error)
    {
        refusal = error;
    }
    return refusal == undefined;
}
