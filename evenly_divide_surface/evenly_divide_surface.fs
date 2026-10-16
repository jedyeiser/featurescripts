FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/path.fs", version : "3083.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");

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
export const SectionCountBounds  = { (unitless) : [2, 20, 1000] } as IntegerBoundSpec;

/** Section planes sit this far inside the ends of the surface (a plane exactly on a trimmed end edge does not intersect cleanly). */
const SECTION_END_INSET = 1e-4 * meter;
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

        annotation { "Name" : "Cross sections", "Description" : "Number of section planes along the part of the wire that meets the surface" }
        isInteger(definition.sections, SectionCountBounds);

        annotation { "Name" : "Wires on symmetry plane", "Default" : true,
                     "Description" : "On: the first and last wire lie on the symmetry plane. Off: they start a half step away from it." }
        definition.onSymmetryPlane is boolean;

        annotation { "Name" : "Keep section curves", "Default" : false }
        definition.keepSections is boolean;
    }
    {
        println("DBG evenlyDivideSurface start");
        const surfaceFaces = qOwnedByBody(definition.surface, EntityType.FACE);
        const wireEdges = qUnion([qEntityFilter(definition.crossWire, EntityType.EDGE),
                                  qOwnedByBody(qEntityFilter(definition.crossWire, EntityType.BODY), EntityType.EDGE)]);
        if (isQueryEmpty(context, wireEdges))
            throw regenError("Select the cross section wire.", ["crossWire"]);

        const crossPath = constructPath(context, wireEdges, { "tolerance" : PATH_TOLERANCE }).path;
        const wireLength = evPathLength(context, crossPath);
        const symPlane = evPlane(context, { "face" : definition.symmetryPlane });

        // 1. Where along the wire does the surface exist?
        const extent = surfaceExtentAlongPath(context, crossPath, definition.surface);
        const inset = SECTION_END_INSET / wireLength;
        const firstU = extent.minU + inset;
        const lastU = extent.maxU - inset;
        println("DBG extent u " ~ extent.minU ~ " .. " ~ extent.maxU ~ " of wire length " ~ (wireLength / millimeter) ~ " mm");
        if (!(lastU > firstU))
            throw regenError("The surface does not extend along the cross section wire.", ["surface", "crossWire"]);

        var sectionParams = [];
        for (var k = 0; k < definition.sections; k += 1)
            sectionParams = append(sectionParams, firstU + (lastU - firstU) * k / (definition.sections - 1));
        const sectionLines = evPathTangentLines(context, crossPath, sectionParams).tangentLines;

        // 2. Section planes and the curves they cut in the surface.
        const bbox = evBox3d(context, { "topology" : definition.surface });
        const planeSize = 1.5 * norm(bbox.maxCorner - bbox.minCorner);

        var sections = [];
        var closedCount = 0;
        for (var k = 0; k < definition.sections; k += 1)
        {
            const planeId = id + ("plane" ~ k);
            const sectionId = id + ("section" ~ k);
            opPlane(context, planeId, { "plane" : plane(sectionLines[k].origin, sectionLines[k].direction),
                                        "width" : planeSize, "height" : planeSize });
            try
            {
                opIntersectFaces(context, sectionId, { "tools" : qCreatedBy(planeId, EntityType.FACE), "targets" : surfaceFaces });
            }
            catch
            {
                continue;
            }
            const sectionEdges = qUnion(evaluateQuery(context, qCreatedBy(sectionId, EntityType.EDGE)));
            if (isQueryEmpty(context, sectionEdges))
                continue;
            try
            {
                const sectionPath = constructPath(context, sectionEdges, { "tolerance" : PATH_TOLERANCE }).path;
                const divided = divideSection(context, sectionPath, symPlane, definition.divisions, definition.onSymmetryPlane);
                println("DBG section " ~ k ~ ": edges " ~ size(sectionPath.edges) ~ " closed " ~ divided.closed ~
                        " length " ~ (evPathLength(context, sectionPath) / millimeter) ~ " mm, crossings " ~ divided.crossings ~
                        ", start u " ~ divided.startU ~ ", first point " ~ toString(divided.points[0] / millimeter));
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
        if (closedCount != 0 && closedCount != size(sections))
            reportFeatureWarning(context, id, "Some sections are closed loops and some are open; the divisions may not line up.");

        // 3. One wire per division: fit through that division's point on every section, then drop onto the surface.
        const wireCount = size(sections[0].points);
        var wires = [];
        for (var i = 0; i < wireCount; i += 1)
        {
            var wirePoints = [];
            for (var s in sections)
                wirePoints = append(wirePoints, s.points[i]);

            const fitId = id + ("fit" ~ i);
            const dropId = id + ("wire" ~ i);
            opFitSpline(context, fitId, { "points" : wirePoints });
            opDropCurve(context, dropId, { "tools" : qCreatedBy(fitId, EntityType.EDGE),
                                           "targets" : surfaceFaces,
                                           "projectionType" : ProjectionType.NORMAL_TO_TARGET });
            wires = append(wires, dropId);
        }

        // 4. Clean up the construction geometry.
        var scratch = [];
        for (var i = 0; i < wireCount; i += 1)
            scratch = append(scratch, qCreatedBy(id + ("fit" ~ i), EntityType.BODY));
        for (var k = 0; k < definition.sections; k += 1)
        {
            scratch = append(scratch, qCreatedBy(id + ("plane" ~ k), EntityType.BODY));
            if (!definition.keepSections)
                scratch = append(scratch, qCreatedBy(id + ("section" ~ k), EntityType.BODY));
        }
        opDeleteBodies(context, id + "cleanup", { "entities" : qUnion(scratch) });

        reportFeatureInfo(context, id, wireCount ~ " wires through " ~ size(sections) ~ " cross sections" ~
                          (closedCount > 0 ? " (closed sections)." : " (open sections)."));
    });


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
