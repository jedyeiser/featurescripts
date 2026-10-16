FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/path.fs", version : "3083.0");

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
/** Samples per section interval when measuring how far a wire strays from the surface. */
const WIRE_CHECK_SAMPLES_PER_SPAN = 4;
/** A wire farther than this from the surface (between its section points) is reported. */
const WIRE_SURFACE_TOLERANCE = 1e-4 * meter;
/** Bisection steps when locating where complete sections start and stop. */
const RANGE_BISECTIONS = 12;
/** Edge endpoints closer than this are one point when joining section edges into a path. */
const PATH_TOLERANCE = 1e-5 * meter;
/** Samples along the wire when finding where the surface starts and ends. */
const EXTENT_SAMPLES = 200;
/** Samples along a section when finding where it meets the symmetry plane. */
const SYMMETRY_SAMPLES = 400;
/** A section point this close to the symmetry plane is on it. */
const SYMMETRY_TOLERANCE = 1e-5 * meter;

annotation { "Feature Type Name" : "Evenly divide surface",
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

        annotation { "Name" : "Keep section curves", "Default" : false }
        definition.keepSections is boolean;
    }
    {
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
        var sectionParams = [];
        for (var k = 0; k < definition.sections; k += 1)
            sectionParams = append(sectionParams, firstU + (lastU - firstU) * k / (definition.sections - 1));
        var sections = [];
        var closedCount = 0;
        for (var k = 0; k < definition.sections; k += 1)
        {
            const cut = cutSection(context, id + ("section" ~ k), crossPath, sectionParams[k], surfaceFaces, planeSize);
            if (cut.path == undefined)
            {
                println("evenlyDivideSurface: section " ~ k ~ " has no cut and was skipped");
                continue;
            }
            try
            {
                const divided = divideSection(context, cut.path, symPlane, definition.divisions, definition.onSymmetryPlane);
                sections = append(sections, { "index" : k, "points" : divided.points, "closed" : divided.closed });
                if (divided.closed)
                    closedCount += 1;
            }
            catch (e)
            {
                println("evenlyDivideSurface: section " ~ k ~ " skipped: " ~ toString(e));
            }
        }

        if (size(sections) < 2)
            throw regenError("Fewer than two cross sections met the surface.", ["surface", "crossWire", "sections"]);
        if (size(sections) < definition.sections)
            println("evenlyDivideSurface: only " ~ size(sections) ~ " of " ~ definition.sections ~ " sections divided");
        if (size(sections) < definition.sections)
            reportFeatureWarning(context, id, (definition.sections - size(sections)) ~ " of " ~ definition.sections ~ " cross sections could not be divided and were skipped.");
        if (closedCount != 0 && closedCount != size(sections))
            reportFeatureWarning(context, id, "Some sections are closed loops and some are open; the divisions may not line up.");

        // 3. One wire per division: a spline through that division's point on every section. (Dropping the spline onto the
        //    surface with opDropCurve was tried and rejected: it projects onto every face the curve can reach and returns
        //    many fragments per division, not one wire.) The distance of each wire from the surface is measured instead.
        const wireCount = size(sections[0].points);
        var worstOff = 0 * meter;
        for (var i = 0; i < wireCount; i += 1)
        {
            var wirePoints = [];
            for (var s in sections)
                wirePoints = append(wirePoints, s.points[i]);

            const wireId = id + ("wire" ~ i);
            opFitSpline(context, wireId, { "points" : wirePoints, "parameters" : chordLengthParameters(wirePoints) });
            worstOff = max(worstOff, deviationFromSurface(context, qCreatedBy(wireId, EntityType.EDGE), surfaceFaces, size(sections) * WIRE_CHECK_SAMPLES_PER_SPAN));
        }

        // 4. Clean up the construction geometry.
        var scratch = [];
        for (var k = 0; k < definition.sections; k += 1)
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

        const summary = wireCount ~ " wires through " ~ size(sections) ~ " cross sections" ~
                        (closedCount > 0 ? " (closed sections)" : " (open sections)") ~
                        "; widest gap to the surface " ~ roundToPrecision(worstOff / millimeter, 4) ~ " mm.";
        println("evenlyDivideSurface: " ~ summary);
        if (worstOff > WIRE_SURFACE_TOLERANCE)
            reportFeatureWarning(context, id, summary ~ " Increase the number of cross sections.");
        else
            reportFeatureInfo(context, id, summary);
    });


/** Chord-length parameters (0..1) for a spline through `points`: far fewer wiggles than evenly spaced parameters on unevenly spaced points. */
function chordLengthParameters(points is array) returns array
{
    var cumulative = [0];
    for (var j = 1; j < size(points); j += 1)
        cumulative = append(cumulative, cumulative[j - 1] + norm(points[j] - points[j - 1]) / meter);
    const total = cumulative[size(cumulative) - 1];
    return mapArray(cumulative, function(c) { return c / total; });
}

/** The largest distance from `samples` evenly spaced points of the wire edge(s) to the surface faces. */
function deviationFromSurface(context is Context, wireEdge is Query, surfaceFaces is Query, samples is number) returns ValueWithUnits
{
    var worst = 0 * meter;
    for (var edge in evaluateQuery(context, wireEdge))
    {
        for (var l in evEdgeTangentLines(context, { "edge" : edge, "parameters" : range(0, 1, samples + 1) }))
            worst = max(worst, evDistance(context, { "side0" : l.origin, "side1" : surfaceFaces }).distance);
    }
    return worst;
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
function isCompleteSection(context is Context, path is Path, symPlane is Plane) returns boolean
{
    const crossings = symmetryCrossings(context, path, symPlane);
    if (path.closed)
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
            complete = isCompleteSection(context, cut.path, symPlane);
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
function divideSection(context is Context, sectionPath is Path, symPlane is Plane, divisions is number, onSymmetryPlane is boolean) returns map
{
    const closed = sectionPath.closed;

    // Where does the section meet the symmetry plane?
    const crossings = symmetryCrossings(context, sectionPath, symPlane);
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
        const ahead = evPathTangentLines(context, sectionPath, [wrapUnit(start.u + 0.002)]).tangentLines[0].origin;
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

    const lines = evPathTangentLines(context, sectionPath, params).tangentLines;
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
function symmetryCrossings(context is Context, path is Path, symPlane is Plane) returns array
{
    var params = [];
    for (var j = 0; j <= SYMMETRY_SAMPLES; j += 1)
        params = append(params, j / SYMMETRY_SAMPLES);
    const tol = SYMMETRY_TOLERANCE / meter;
    const origins = mapArray(evPathTangentLines(context, path, params).tangentLines, function(l) { return l.origin; });
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
            if (path.closed && j == SYMMETRY_SAMPLES)
                continue;
            found = append(found, { "u" : params[j], "z" : origins[j][2] });
        }
        else if (j < SYMMETRY_SAMPLES && abs(d[j + 1]) > tol && (d[j] < 0) != (d[j + 1] < 0))
        {
            brackets = append(brackets, { "ua" : params[j], "da" : d[j], "ub" : params[j + 1], "db" : d[j + 1] });
        }
    }

    for (var iter = 0; iter < 5 && size(brackets) > 0; iter += 1)
    {
        const us = mapArray(brackets, function(b) { return b.ua - b.da * (b.ub - b.ua) / (b.db - b.da); });
        const pts = mapArray(evPathTangentLines(context, path, us).tangentLines, function(l) { return l.origin; });
        for (var m = 0; m < size(brackets); m += 1)
        {
            const dm = dot(pts[m] - symPlane.origin, symPlane.normal) / meter;
            if ((dm < 0) == (brackets[m].da < 0))
                brackets[m] = { "ua" : us[m], "da" : dm, "ub" : brackets[m].ub, "db" : brackets[m].db };
            else
                brackets[m] = { "ua" : brackets[m].ua, "da" : brackets[m].da, "ub" : us[m], "db" : dm };
        }
    }
    if (size(brackets) > 0)
    {
        const us = mapArray(brackets, function(b) { return b.ua - b.da * (b.ub - b.ua) / (b.db - b.da); });
        const pts = mapArray(evPathTangentLines(context, path, us).tangentLines, function(l) { return l.origin; });
        for (var m = 0; m < size(brackets); m += 1)
            found = append(found, { "u" : us[m], "z" : pts[m][2] });
    }
    return found;
}
