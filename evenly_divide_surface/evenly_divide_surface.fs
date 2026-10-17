FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/path.fs", version : "3083.0");
// IMPORT: evenly_divide_surface_icon.svg (feature icon)
IconNamespace::import(path : "3a855693361d30ac881a44d2", version : "7386e241f35293c7b09eba6f");

/**
 * Evenly divide surface (phase 1: the division wires).
 *
 * Cross section planes are placed at even arc length along a wire, over the part of the wire that meets the
 * surface. Each plane cuts the surface into a section curve; the section's perimeter is divided evenly, starting
 * at the uppermost (world Z) place where the section meets the symmetry plane. Matching divisions of all sections
 * are joined into one wire each, and the wires are dropped onto the surface.
 *
 *   closed section (full surface, 0-360): N wires at i/N, or (i+0.5)/N when not on the symmetry plane
 *   open section   (half surface, 0-180): N wires at i/(N-1), or (i+0.5)/N when not on the symmetry plane
 *
 * See evenly_divide_surface/DESIGN.md.
 */

export const DivisionCountBounds = { (unitless) : [1, 5, 500] } as IntegerBoundSpec;
export const SectionCountBounds  = { (unitless) : [2, 60, 1000] } as IntegerBoundSpec;

/** The first/last probe plane sits this far inside the end of the surface. */
const SECTION_END_INSET = 1e-4 * meter;
/** Planes keep this far inside the first/last COMPLETE section (a plane that only just clears a trimmed end cuts a degenerate section). */
const SECTION_END_MARGIN = 1e-3 * meter;
/** Samples inside each section interval when measuring how far a wire strays from the surface (error peaks mid-interval). */
const WIRE_CHECK_SAMPLES = 2;
/** A face is a candidate for a distance check when the point is within its bounding box inflated by at least this much. */
const WIRE_CHECK_BOX_INFLATE = 2e-3 * meter;
/** Samples per section interval used to find which faces a wire crosses. */
const PROJECT_FACE_SAMPLES_PER_SPAN = 3;
/** Most pieces a weak interval is split into in one round. */
const MAX_SPLIT = 5;
/** Bounds of the surface tolerance input: 1e-3 mm min, 0.01 mm default, 1 mm max (meter entry [min, default, max]; the other units give the default, as std TOLERANCE_BOUND does). */
export const SurfaceToleranceBounds =
{
    (meter)      : [1e-6, 1e-5, 1e-3],
    (centimeter) : 1e-3,
    (millimeter) : 1e-2,
    (inch)       : 4e-4,
    (foot)       : 3e-5,
    (yard)       : 1e-5
} as LengthBoundSpec;
/** Rounds of adding sections, and the most sections the refinement may reach. */
const MAX_REFINE_ROUNDS = 5;
const MAX_SECTIONS = 600;
/** Bisection steps when locating where complete sections start and stop. */
const RANGE_BISECTIONS = 12;
/** Edge endpoints closer than this are one point when joining section edges into a path. */
const PATH_TOLERANCE = 1e-5 * meter;
/** Samples along the wire when finding where the surface starts and ends. */
const EXTENT_SAMPLES = 200;
/** Samples along a section when finding where it meets the symmetry plane. */
const SYMMETRY_SAMPLES = 400;
/** Regula falsi stops when a crossing is this close to the symmetry plane (meters). */
const CROSSING_TOLERANCE = 1e-8;
/** A section point this close to the symmetry plane is on it. */
const SYMMETRY_TOLERANCE = 1e-5 * meter;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Evenly divide surface",
             "Feature Type Description" : "Divides a surface into evenly spaced wires around its cross sections" }
export const evenlyDivideSurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,
                     "Description" : "Surface body to divide: a closed (full) or open (half) surface" }
        definition.surface is Query;

        annotation { "Name" : "Cross section wire",
                     "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                     "Description" : "Wire or edges the section planes are normal to. It may extend past the surface." }
        definition.crossWire is Query;

        annotation { "Name" : "Symmetry plane", "Filter" : EntityType.FACE && GeometryType.PLANE, "MaxNumberOfPicks" : 1,
                     "Description" : "Defines the 0 and 180 degree locations of the division. The start point is where each section meets it, uppermost in Z." }
        definition.symmetryPlane is Query;

        annotation { "Name" : "Divisions", "Description" : "Number of wires around the section perimeter" }
        isInteger(definition.divisions, DivisionCountBounds);

        annotation { "Name" : "Cross sections", "Description" : "Number of section planes along the part of the wire that cuts complete sections" }
        isInteger(definition.sections, SectionCountBounds);

        annotation { "Name" : "Wires on symmetry plane", "Default" : true,
                     "Description" : "On: the first and last wire lie on the symmetry plane. Off: they start a half step away from it." }
        definition.onSymmetryPlane is boolean;

        annotation { "Name" : "Surface tolerance", "Description" : "How far a wire may stray from the surface between two cross sections. Larger values regenerate faster." }
        isLength(definition.surfaceTolerance, SurfaceToleranceBounds);

        annotation { "Name" : "Refine sections", "Default" : true,
                     "Description" : "Add cross sections where a wire would otherwise stray from the surface (the number above is the starting count)." }
        definition.refine is boolean;

        annotation { "Name" : "Project wires onto faces (experimental)", "Default" : false,
                     "Description" : "Project each wire onto only the faces it crosses and join the pieces into one wire of several edges, so every edge lies exactly on its face." }
        definition.projectWires is boolean;

        annotation { "Name" : "Keep section curves", "Default" : false }
        definition.keepSections is boolean;
    }
    {
        const surfaceTolerance = definition.surfaceTolerance;
        const surfaceFaces = qOwnedByBody(definition.surface, EntityType.FACE);
        const wireEdges = qUnion([qEntityFilter(definition.crossWire, EntityType.EDGE),
                                  qOwnedByBody(qEntityFilter(definition.crossWire, EntityType.BODY), EntityType.EDGE)]);
        if (isQueryEmpty(context, wireEdges))
            throw regenError("Select the cross section wire.", ["crossWire"]);

        const crossPath = constructPath(context, wireEdges, { "tolerance" : PATH_TOLERANCE }).path;
        const wireLength = evPathLength(context, crossPath);
        const symPlane = evPlane(context, { "face" : definition.symmetryPlane });

        const bbox = evBox3d(context, { "topology" : definition.surface });
        const planeSize = 1.5 * norm(bbox.maxCorner - bbox.minCorner);

        // 1. Where along the wire does the surface exist, and where does a plane cut a COMPLETE section from it?
        //    (Near a trimmed end the plane only clips a sliver of the surface; those partial sections cannot be
        //    divided consistently with the rest.)
        const extent = surfaceExtentAlongPath(context, crossPath, definition.surface);
        const complete = completeSectionRange(context, id, crossPath, wireLength, extent, surfaceFaces, planeSize, symPlane);
        const firstU = complete.minU + SECTION_END_MARGIN / wireLength;
        const lastU = complete.maxU - SECTION_END_MARGIN / wireLength;
        if (!(lastU > firstU))
            throw regenError("The surface does not extend along the cross section wire.", ["surface", "crossWire"]);

        // 2. Section planes at even arc length over that range, and the curves they cut in the surface.
        var sections = [];
        var sectionCount = 0;      // ids used so far: section0, section1, ...
        for (var k = 0; k < definition.sections; k += 1)
        {
            const u = firstU + (lastU - firstU) * k / (definition.sections - 1);
            const section = makeSection(context, id + ("section" ~ sectionCount), crossPath, u, surfaceFaces, planeSize, symPlane,
                                         definition.divisions, definition.onSymmetryPlane);
            sectionCount += 1;
            if (section == undefined)
                println("evenlyDivideSurface: the section at " ~ roundToPrecision(u, 4) ~ " could not be divided and was skipped");
            else
                sections = append(sections, section);
        }
        if (size(sections) < 2)
            throw regenError("Fewer than two cross sections met the surface.", ["surface", "crossWire", "sections"]);
        const baseSections = size(sections);
        if (baseSections < definition.sections)
            reportFeatureWarning(context, id, (definition.sections - baseSections) ~ " of " ~ definition.sections ~ " cross sections could not be divided and were skipped.");

        // 3. One wire per division: a spline through that division's point on every section. (Dropping the spline onto the
        //    surface with opDropCurve was tried and rejected: it projects onto every face the curve can reach and returns
        //    many fragments per division, not one wire.) Instead, where a wire strays from the surface between two sections,
        //    sections are added in that interval and the wires are fitted again.
        //
        //    Cost control: a wire's deviation is sampled INSIDE each section interval (the spline's own parameters of its
        //    section points delimit the intervals, so no search is needed), each sample is measured against only the faces
        //    whose bounding box contains it, and after the first round only intervals near new sections are re-measured.
        const wireCount = size(sections[0].points);
        const faceBoxes = surfaceFaceBoxes(context, surfaceFaces, max(2 * surfaceTolerance, WIRE_CHECK_BOX_INFLATE));
        var wireIds = [];
        var intervalDev = makeArray(size(sections) - 1, 0 * meter);   // worst deviation seen in each interval
        var dirty = makeArray(size(sections) - 1, true);              // intervals to (re)measure this round
        var rounds = 0;
        while (true)
        {
            // Fit one spline per division.
            wireIds = [];
            var wireParams = [];
            for (var i = 0; i < wireCount; i += 1)
            {
                var wirePoints = [];
                for (var s in sections)
                    wirePoints = append(wirePoints, s.points[i]);
                const params = chordLengthParameters(wirePoints);
                const wireId = id + ("wire" ~ i ~ "r" ~ rounds);
                opFitSpline(context, wireId, { "points" : wirePoints, "parameters" : params });
                wireIds = append(wireIds, wireId);
                wireParams = append(wireParams, params);
            }

            // Measure the dirty intervals: WIRE_CHECK_SAMPLES samples strictly inside each, on every wire.
            for (var j = 0; j < size(intervalDev); j += 1)
                if (dirty[j])
                    intervalDev[j] = 0 * meter;
            for (var i = 0; i < wireCount; i += 1)
            {
                var sampleParams = [];
                var sampleInterval = [];
                for (var j = 0; j < size(intervalDev); j += 1)
                {
                    if (!dirty[j])
                        continue;
                    for (var k = 1; k <= WIRE_CHECK_SAMPLES; k += 1)
                    {
                        sampleParams = append(sampleParams, wireParams[i][j] + (wireParams[i][j + 1] - wireParams[i][j]) * k / (WIRE_CHECK_SAMPLES + 1));
                        sampleInterval = append(sampleInterval, j);
                    }
                }
                if (size(sampleParams) == 0)
                    continue;
                const lines = evEdgeTangentLines(context, { "edge" : qCreatedBy(wireIds[i], EntityType.EDGE), "parameters" : sampleParams,
                                                            "arcLengthParameterization" : false });
                for (var m = 0; m < size(lines); m += 1)
                    intervalDev[sampleInterval[m]] = max(intervalDev[sampleInterval[m]], distanceToSurface(context, faceBoxes, surfaceFaces, lines[m].origin));
            }

            var roundWorst = 0 * meter;
            var weakCount = 0;
            for (var j = 0; j < size(intervalDev); j += 1)
            {
                roundWorst = max(roundWorst, intervalDev[j]);
                if (intervalDev[j] > surfaceTolerance)
                    weakCount += 1;
            }
            println("evenlyDivideSurface: round " ~ rounds ~ ": " ~ size(sections) ~ " sections, " ~ weakCount ~ " weak intervals, worst " ~
                    roundToPrecision(roundWorst / millimeter, 4) ~ " mm");
            if (!definition.refine || weakCount == 0 || rounds >= MAX_REFINE_ROUNDS || size(sections) + weakCount > MAX_SECTIONS)
                break;

            // Split each weak interval into pieces sized from its error, then fit again; the wires of this round are discarded.
            // A smooth surface converges like the 4th power of the spacing, but near a face seam only like the square, and a
            // round (fit + measure) costs far more than a section, so the square law is assumed.
            opDeleteBodies(context, id + ("discard" ~ rounds), { "entities" : qUnion(mapArray(wireIds, function(w) { return qCreatedBy(w, EntityType.BODY); })) });
            var refined = [];
            var refinedDev = [];
            var changed = [];        // new interval i is next to a new section
            for (var j = 0; j < size(sections); j += 1)
            {
                refined = append(refined, sections[j]);
                if (j == size(sections) - 1)
                    break;
                var added = 0;
                if (intervalDev[j] > surfaceTolerance)
                {
                    const pieces = min(MAX_SPLIT, max(2, ceil(sqrt(intervalDev[j] / surfaceTolerance) * 1.1)));
                    for (var p = 1; p < pieces; p += 1)
                    {
                        const u = sections[j].u + (sections[j + 1].u - sections[j].u) * p / pieces;
                        const section = makeSection(context, id + ("section" ~ sectionCount), crossPath, u, surfaceFaces, planeSize, symPlane,
                                                    definition.divisions, definition.onSymmetryPlane);
                        sectionCount += 1;
                        if (section != undefined)
                        {
                            refined = append(refined, section);
                            added += 1;
                        }
                    }
                }
                // intervals j (split into added + 1 pieces)
                for (var p = 0; p <= added; p += 1)
                {
                    refinedDev = append(refinedDev, intervalDev[j]);
                    changed = append(changed, added > 0);
                }
            }
            if (size(refined) == size(sections))
                break;
            // An interval next to a changed one is re-measured too: the refit moves the spline a little there.
            dirty = makeArray(size(changed), false);
            for (var j = 0; j < size(changed); j += 1)
            {
                if (changed[j])
                {
                    for (var near = max(0, j - 1); near <= min(size(changed) - 1, j + 1); near += 1)
                        dirty[near] = true;
                }
            }
            intervalDev = refinedDev;
            sections = refined;
            rounds += 1;
        }
        var worstOff = 0 * meter;
        for (var dev in intervalDev)
            worstOff = max(worstOff, dev);

        // 3b. Optionally replace each fitted spline by a wire of one edge per face run, exactly on the faces.
        var projected = 0;
        if (definition.projectWires)
        {
            const surfaceFaceList = evaluateQuery(context, surfaceFaces);
            for (var i = 0; i < wireCount; i += 1)
            {
                const fitEdge = qCreatedBy(wireIds[i], EntityType.EDGE);
                const probe = evEdgeTangentLines(context, { "edge" : fitEdge, "parameters" : range(0, 1, PROJECT_FACE_SAMPLES_PER_SPAN * (size(sections) - 1) + 1) });
                var used = makeArray(size(surfaceFaceList), false);
                for (var l in probe)
                    used[nearestFaceIndex(context, faceBoxes, surfaceFaces, l.origin)] = true;
                var targets = [];
                for (var k = 0; k < size(used); k += 1)
                    if (used[k])
                        targets = append(targets, surfaceFaceList[k]);
                try
                {
                    opDropCurve(context, id + ("project" ~ i), { "tools" : fitEdge, "targets" : qUnion(targets),
                                                                 "projectionType" : ProjectionType.NORMAL_TO_TARGET });
                    const pieces = qCreatedBy(id + ("project" ~ i), EntityType.EDGE);
                    println("evenlyDivideSurface: wire " ~ i ~ " projected onto " ~ size(targets) ~ " faces into " ~ size(evaluateQuery(context, pieces)) ~ " edges");
                    opExtractWires(context, id + ("join" ~ i), { "edges" : pieces });
                    opDeleteBodies(context, id + ("dropPieces" ~ i), { "entities" : qUnion([qCreatedBy(id + ("project" ~ i), EntityType.BODY), qCreatedBy(wireIds[i], EntityType.BODY)]) });
                    projected += 1;
                }
                catch (e)
                {
                    println("evenlyDivideSurface: wire " ~ i ~ " could not be projected: " ~ toString(e));
                }
            }
        }
        // 4. Clean up the construction geometry.
        var scratch = [];
        for (var k = 0; k < sectionCount; k += 1)
        {
            if (definition.keepSections)
                scratch = append(scratch, qSubtraction(qCreatedBy(id + ("section" ~ k), EntityType.BODY),
                                                       qBodyType(qCreatedBy(id + ("section" ~ k), EntityType.BODY), BodyType.WIRE)));
            else
                scratch = append(scratch, qCreatedBy(id + ("section" ~ k), EntityType.BODY));
        }
        const scratchQuery = qUnion(scratch);
        if (!isQueryEmpty(context, scratchQuery))
            opDeleteBodies(context, id + "cleanup", { "entities" : scratchQuery });

        var closedCount = 0;
        for (var s in sections)
            if (s.closed)
                closedCount += 1;
        if (closedCount != 0 && closedCount != size(sections))
            reportFeatureWarning(context, id, "Some sections are closed loops and some are open; the divisions may not line up.");

        const summary = wireCount ~ " wires through " ~ size(sections) ~ " cross sections" ~
                        (size(sections) > baseSections ? " (" ~ (size(sections) - baseSections) ~ " added to follow the surface)" : "") ~
                        (closedCount > 0 ? ", closed sections" : ", open sections") ~
                        "; widest gap to the surface " ~ roundToPrecision(worstOff / millimeter, 4) ~ " mm.";
        println("evenlyDivideSurface: " ~ summary);
        if (worstOff > surfaceTolerance)
            reportFeatureWarning(context, id, summary ~ (definition.refine ? " Increase the number of cross sections." : " Turn on Refine sections or increase the number of cross sections."));
        else
            reportFeatureInfo(context, id, summary);
    });


/**
 * Cut the surface at parameter `u` of the wire and divide the section. Everything made is created under `id`.
 *
 * @returns {{ u, points, closed }} or `undefined` when the plane cuts nothing usable
 */
function makeSection(context is Context, id is Id, crossPath is Path, u is number, surfaceFaces is Query, planeSize is ValueWithUnits,
        symPlane is Plane, divisions is number, onSymmetryPlane is boolean)
{
    const cut = cutSection(context, id, crossPath, u, surfaceFaces, planeSize);
    if (cut.path == undefined)
        return undefined;
    try
    {
        const divided = divideSection(context, pathEvaluator(context, cut.path), symPlane, divisions, onSymmetryPlane);
        return { "u" : u, "points" : divided.points, "closed" : divided.closed };
    }
    catch (e)
    {
        println("evenlyDivideSurface: section at " ~ roundToPrecision(u, 4) ~ " skipped: " ~ toString(e));
        return undefined;
    }
}




/** Bounding box (unitless, meters) of every face of the surface, inflated by `inflate`. */
function surfaceFaceBoxes(context is Context, surfaceFaces is Query, inflate is ValueWithUnits) returns array
{
    const pad = inflate / meter;
    return mapArray(evaluateQuery(context, surfaceFaces), function(face)
        {
            const faceBox = evBox3d(context, { "topology" : face });
            return { "face" : face, "lo" : faceBox.minCorner / meter - vector(pad, pad, pad), "hi" : faceBox.maxCorner / meter + vector(pad, pad, pad) };
        });
}

/**
 * Distance from `p` to the surface. Only faces whose (inflated) bounding box contains `p` are measured: exact whenever the
 * distance is within the inflation, since the nearest face's box then contains `p`; with no candidate every face is measured.
 */
function distanceToSurface(context is Context, boxes is array, surfaceFaces is Query, p is Vector) returns ValueWithUnits
{
    const q = p / meter;
    var near = [];
    for (var b in boxes)
    {
        if (q[0] >= b.lo[0] && q[0] <= b.hi[0] && q[1] >= b.lo[1] && q[1] <= b.hi[1] && q[2] >= b.lo[2] && q[2] <= b.hi[2])
            near = append(near, b.face);
    }
    return evDistance(context, { "side0" : p, "side1" : (size(near) == 0) ? surfaceFaces : qUnion(near) }).distance;
}
/**
 * A path prepared for evaluation: the arc-length position of every edge boundary, computed once (`evPathTangentLines` in
 * std/path.fs recomputes the path length and every edge length on EVERY call and evaluates one parameter per kernel call).
 */
function pathEvaluator(context is Context, path is Path) returns map
{
    var lengths = [];
    var total = 0 * meter;
    for (var edge in path.edges)
    {
        const len = evLength(context, { "entities" : edge });
        lengths = append(lengths, len);
        total += len;
    }
    var cum = [0];
    var run = 0 * meter;
    for (var len in lengths)
    {
        run += len;
        cum = append(cum, run / total);
    }
    return { "path" : path, "cum" : cum };
}

/**
 * Tangent lines of a prepared path at arc-length parameters (0..1, any order), the same values as `evPathTangentLines`
 * but with ONE kernel call per edge touched.
 *
 * @returns array of tangent Lines, in the order of `params`
 */
function evaluatePath(context is Context, pe is map, params is array) returns array
{
    const nEdges = size(pe.path.edges);
    var onEdge = makeArray(nEdges, []);       // parameters on each edge (in the edge's own direction)
    var slot = makeArray(nEdges, []);         // where each result goes
    for (var i = 0; i < size(params); i += 1)
    {
        var e = 0;
        while (e < nEdges - 1 && pe.cum[e + 1] < params[i])
            e += 1;
        const fraction = (params[i] - pe.cum[e]) / (pe.cum[e + 1] - pe.cum[e]);
        onEdge[e] = append(onEdge[e], pe.path.flipped[e] ? 1 - fraction : fraction);
        slot[e] = append(slot[e], i);
    }
    var result = makeArray(size(params));
    for (var e = 0; e < nEdges; e += 1)
    {
        if (size(onEdge[e]) == 0)
            continue;
        const lines = evEdgeTangentLines(context, { "edge" : pe.path.edges[e], "parameters" : onEdge[e] });
        for (var k = 0; k < size(lines); k += 1)
        {
            var line = lines[k];
            if (pe.path.flipped[e])
                line.direction = -line.direction;
            result[slot[e][k]] = line;
        }
    }
    return result;
}

/** Index (into `boxes`, i.e. the surface's face list) of the face nearest `p`; only faces whose box contains `p` are measured. */
function nearestFaceIndex(context is Context, boxes is array, surfaceFaces is Query, p is Vector) returns number
{
    const q = p / meter;
    var near = [];
    for (var j = 0; j < size(boxes); j += 1)
    {
        const b = boxes[j];
        if (q[0] >= b.lo[0] && q[0] <= b.hi[0] && q[1] >= b.lo[1] && q[1] <= b.hi[1] && q[2] >= b.lo[2] && q[2] <= b.hi[2])
            near = append(near, j);
    }
    if (size(near) == 0)
        return evDistance(context, { "side0" : p, "side1" : surfaceFaces }).sides[1].index;
    return near[evDistance(context, { "side0" : p, "side1" : qUnion(mapArray(near, function(j) { return boxes[j].face; })) }).sides[1].index];
}
/** Chord-length parameters (0..1) for a spline through `points`: far fewer wiggles than evenly spaced parameters on unevenly spaced points. */
function chordLengthParameters(points is array) returns array
{
    var cumulative = [0];
    for (var j = 1; j < size(points); j += 1)
        cumulative = append(cumulative, cumulative[j - 1] + norm(points[j] - points[j - 1]) / meter);
    const total = cumulative[size(cumulative) - 1];
    return mapArray(cumulative, function(c) { return c / total; });
}

// ============================================================================
// Cutting sections
// ============================================================================

/**
 * Cut the surface with the plane normal to `crossPath` at parameter `u`. Everything made (plane and cut curves) is
 * created under `id`.
 *
 * @returns {{ path : Path or undefined }} the cut curves joined into a path, `undefined` if there is no (single) cut
 */
function cutSection(context is Context, id is Id, crossPath is Path, u is number, surfaceFaces is Query, planeSize is ValueWithUnits) returns map
{
    const line = evPathTangentLines(context, crossPath, [u]).tangentLines[0];
    opPlane(context, id + "plane", { "plane" : plane(line.origin, line.direction), "width" : planeSize, "height" : planeSize });
    try
    {
        opIntersectFaces(context, id + "cut", { "tools" : qCreatedBy(id + "plane", EntityType.FACE), "targets" : surfaceFaces });
    }
    catch
    {
        return { "path" : undefined };
    }
    const edges = qUnion(evaluateQuery(context, qCreatedBy(id + "cut", EntityType.EDGE)));
    if (isQueryEmpty(context, edges))
        return { "path" : undefined };
    try
    {
        return { "path" : constructPath(context, edges, { "tolerance" : PATH_TOLERANCE }).path };
    }
    catch
    {
        return { "path" : undefined };
    }
}

/** A complete section runs all the way around: a closed loop, or an open arc with both ends on the symmetry plane. */
function isCompleteSection(context is Context, pe is map, symPlane is Plane) returns boolean
{
    const crossings = symmetryCrossings(context, pe, symPlane);
    if (pe.path.closed)
        return size(crossings) == 2;
    var atStart = false;
    var atEnd = false;
    for (var c in crossings)
    {
        if (c.u < 1e-6)
            atStart = true;
        if (c.u > 1 - 1e-6)
            atEnd = true;
    }
    return atStart && atEnd;
}

/** Whether the plane at `u` cuts a complete section (the probe geometry is deleted again). */
function completeSectionAt(context is Context, id is Id, crossPath is Path, u is number, surfaceFaces is Query,
        planeSize is ValueWithUnits, symPlane is Plane) returns boolean
{
    const cut = cutSection(context, id, crossPath, u, surfaceFaces, planeSize);
    var complete = false;
    if (cut.path != undefined)
    {
        try silent
        {
            complete = isCompleteSection(context, pathEvaluator(context, cut.path), symPlane);
        }
    }
    const made = qCreatedBy(id, EntityType.BODY);
    if (!isQueryEmpty(context, made))
        opDeleteBodies(context, id + "delete", { "entities" : made });
    return complete;
}

/**
 * The range of path parameters whose planes cut complete sections. The middle of the surface must be complete; each end is
 * found by bisection between that and the end of the surface.
 */
function completeSectionRange(context is Context, id is Id, crossPath is Path, wireLength is ValueWithUnits, extent is map,
        surfaceFaces is Query, planeSize is ValueWithUnits, symPlane is Plane) returns map
{
    const inset = SECTION_END_INSET / wireLength;
    const middle = (extent.minU + extent.maxU) / 2;
    if (!completeSectionAt(context, id + "probeMiddle", crossPath, middle, surfaceFaces, planeSize, symPlane))
        throw regenError("A plane through the middle of the surface does not cut a complete section. Check the wire and the symmetry plane.",
                         ["surface", "crossWire", "symmetryPlane"]);

    var ends = [];
    for (var side in [0, 1])
    {
        var good = middle;
        var bad = (side == 0) ? extent.minU + inset : extent.maxU - inset;
        if (completeSectionAt(context, id + ("probeEnd" ~ side), crossPath, bad, surfaceFaces, planeSize, symPlane))
        {
            ends = append(ends, bad);
            continue;
        }
        for (var n = 0; n < RANGE_BISECTIONS; n += 1)
        {
            const mid = (good + bad) / 2;
            if (completeSectionAt(context, id + ("probe" ~ side ~ "_" ~ n), crossPath, mid, surfaceFaces, planeSize, symPlane))
                good = mid;
            else
                bad = mid;
        }
        ends = append(ends, good);
    }
    return { "minU" : ends[0], "maxU" : ends[1] };
}


// ============================================================================
// Extent of the surface along the wire
// ============================================================================

/**
 * The range [minU, maxU] of path parameters (0..1, arc length) that the surface reaches: its vertices and a few points
 * on each edge are projected to the nearest point on the path.
 */
function surfaceExtentAlongPath(context is Context, path is Path, surface is Query) returns map
{
    var params = [];
    for (var j = 0; j <= EXTENT_SAMPLES; j += 1)
        params = append(params, j / EXTENT_SAMPLES);
    const samples = mapArray(evPathTangentLines(context, path, params).tangentLines, function(l) { return l.origin / meter; });

    var points = mapArray(evaluateQuery(context, qOwnedByBody(surface, EntityType.VERTEX)),
                          function(v) { return evVertexPoint(context, { "vertex" : v }) / meter; });
    for (var edge in evaluateQuery(context, qOwnedByBody(surface, EntityType.EDGE)))
    {
        for (var l in evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0.25, 0.5, 0.75] }))
            points = append(points, l.origin / meter);
    }

    var minU = inf;
    var maxU = -inf;
    for (var p in points)
    {
        const u = nearestPathParameter(samples, p);
        minU = min(minU, u);
        maxU = max(maxU, u);
    }
    return { "minU" : minU, "maxU" : maxU };
}

/** Path parameter of the point on the sampled polyline (unitless, meters) nearest `p`. */
function nearestPathParameter(samples is array, p is Vector) returns number
{
    const n = size(samples) - 1;
    var best = 0;
    var bestD = inf;
    for (var j = 0; j <= n; j += 1)
    {
        const d = squaredNorm(p - samples[j]);
        if (d < bestD)
        {
            bestD = d;
            best = j;
        }
    }

    var u = best / n;
    bestD = inf;
    for (var seg in [best - 1, best])
    {
        if (seg < 0 || seg >= n)
            continue;
        const a = samples[seg];
        const ab = samples[seg + 1] - a;
        const t = clamp(dot(p - a, ab) / dot(ab, ab), 0, 1);
        const d = squaredNorm(p - (a + ab * t));
        if (d < bestD)
        {
            bestD = d;
            u = (seg + t) / n;
        }
    }
    return u;
}


// ============================================================================
// Dividing a section
// ============================================================================

/**
 * Division points of one section.
 *
 * @returns {{ points : array of Vector (division i of this section, i = 0..N-1, from the start point), closed : boolean }}
 */
function divideSection(context is Context, pe is map, symPlane is Plane, divisions is number, onSymmetryPlane is boolean) returns map
{
    const closed = pe.path.closed;

    // Where does the section meet the symmetry plane?
    const crossings = symmetryCrossings(context, pe, symPlane);
    if (size(crossings) == 0)
        throw regenError("A section does not meet the symmetry plane.");

    // Start: the uppermost (world Z) crossing.
    var start = crossings[0];
    for (var c in crossings)
        if (c.z > start.z)
            start = c;

    // Fractions of the perimeter, 0 at the start.
    var fractions = [];
    for (var i = 0; i < divisions; i += 1)
    {
        if (!onSymmetryPlane)
            fractions = append(fractions, (i + 0.5) / divisions);
        else if (closed)
            fractions = append(fractions, i / divisions);
        else
            fractions = append(fractions, divisions == 1 ? 0.5 : i / (divisions - 1));
    }

    // Travel direction: toward the positive side of the symmetry plane.
    var direction = 1;
    if (closed)
    {
        const ahead = evaluatePath(context, pe, [wrapUnit(start.u + 0.002)])[0].origin;
        if (dot(ahead - symPlane.origin, symPlane.normal) < 0 * meter)
            direction = -1;
    }
    else if (start.u > 0.5)
    {
        direction = -1;
    }

    var params = [];
    for (var f in fractions)
        params = append(params, closed ? wrapUnit(start.u + direction * f) : (start.u > 0.5 ? 1 - f : f));

    const lines = evaluatePath(context, pe, params);
    return { "points" : mapArray(lines, function(l) { return l.origin; }), "closed" : closed,
             "crossings" : size(crossings), "startU" : start.u };
}

/** Wrap a path parameter into [0, 1). */
function wrapUnit(u is number) returns number
{
    return u - floor(u);
}

/**
 * Parameters (and world Z) where a path meets the symmetry plane: sign changes of the signed distance along an even
 * sampling, refined by regula falsi, plus samples already on the plane (the ends of an open section).
 *
 * @returns array of { u : number, z : ValueWithUnits }
 */
function symmetryCrossings(context is Context, pe is map, symPlane is Plane) returns array
{
    var params = [];
    for (var j = 0; j <= SYMMETRY_SAMPLES; j += 1)
        params = append(params, j / SYMMETRY_SAMPLES);
    const tol = SYMMETRY_TOLERANCE / meter;
    const origins = mapArray(evaluatePath(context, pe, params), function(l) { return l.origin; });
    const d = mapArray(origins, function(o) { return dot(o - symPlane.origin, symPlane.normal) / meter; });

    var found = [];      // exact: { u, z }
    var brackets = [];   // to refine: { ua, da, ub, db }
    for (var j = 0; j <= SYMMETRY_SAMPLES; j += 1)
    {
        if (abs(d[j]) <= tol)
        {
            // Skip the closing sample of a closed path (same point as sample 0) and runs of samples on the plane.
            if (j > 0 && abs(d[j - 1]) <= tol)
                continue;
            if (pe.path.closed && j == SYMMETRY_SAMPLES)
                continue;
            found = append(found, { "u" : params[j], "z" : origins[j][2] });
        }
        else if (j < SYMMETRY_SAMPLES && abs(d[j + 1]) > tol && (d[j] < 0) != (d[j + 1] < 0))
        {
            brackets = append(brackets, { "ua" : params[j], "da" : d[j], "ub" : params[j + 1], "db" : d[j + 1] });
        }
    }

    // Refine each bracket by regula falsi; stop as soon as every crossing is within CROSSING_TOLERANCE of the plane.
    var us = [];
    var pts = [];
    for (var iter = 0; iter < 6 && size(brackets) > 0; iter += 1)
    {
        us = mapArray(brackets, function(b) { return b.ua - b.da * (b.ub - b.ua) / (b.db - b.da); });
        pts = mapArray(evaluatePath(context, pe, us), function(l) { return l.origin; });
        var converged = true;
        for (var m = 0; m < size(brackets); m += 1)
        {
            const dm = dot(pts[m] - symPlane.origin, symPlane.normal) / meter;
            if (abs(dm) > CROSSING_TOLERANCE)
                converged = false;
            if ((dm < 0) == (brackets[m].da < 0))
                brackets[m] = { "ua" : us[m], "da" : dm, "ub" : brackets[m].ub, "db" : brackets[m].db };
            else
                brackets[m] = { "ua" : brackets[m].ua, "da" : brackets[m].da, "ub" : us[m], "db" : dm };
        }
        if (converged)
            break;
    }
    for (var m = 0; m < size(brackets); m += 1)
        found = append(found, { "u" : us[m], "z" : pts[m][2] });    return found;
}
