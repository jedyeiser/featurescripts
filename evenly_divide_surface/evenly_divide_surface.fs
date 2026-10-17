FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/path.fs", version : "3083.0");
import(path : "onshape/std/projectiontype.gen.fs", version : "3083.0");
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
/** A projected run must be within this fraction of the length of the curve it was projected from. */
const PROJECT_LENGTH_TOLERANCE = 0.01;
/** A section point closer to a seam point than this fraction of the section spacing is dropped (it would make a kink). */
const SEAM_CLOSE = 0.2;
/** Points closer than this are one point (joining a run to its seam point). */
const SEAM_MERGE = 1e-6 * meter;
/** Both points of a wire segment within this of the faces' shared edge: the wire runs along the seam. */
const SEAM_ALONG = 1e-5 * meter;
/** The shared edge of two faces must pass this close to the wire's chord to count as the seam the wire crosses. */
const SEAM_REACH = 2e-3 * meter;
/** Samples along a wire used to find the faces a strip starts from, and the most flood steps across edges. */
const STRIP_SEED_SAMPLES = 9;
const STRIP_FLOOD_LIMIT = 40;
/** A non-cut edge shorter than STRIP_GAP_LENGTH whose first sample is within STRIP_GAP_REACH of a cut is a gap in the cut. */
const STRIP_GAP_LENGTH = 1.5e-3 * meter;
const STRIP_GAP_REACH = 1e-3 * meter;
/** A strip larger than this times (width x wire length) is rejected: the cut around it did not close. */
const STRIP_AREA_LIMIT = 1.5;
/** Faces of a strip smaller than this are slivers and are dropped. */
const STRIP_MIN_FACE_AREA = 1e-8 * meter ^ 2;
/** The strip edges are continued straight by this much at both ends so the cut reaches the surface boundary. */
const STRIP_EXTENSION = 6e-3 * meter;
/** An edge of the split copy within this of a strip-edge wire (at 3 points) is a cut. */
const STRIP_CUT_TOLERANCE = 5e-5 * meter;
/** Most pieces a weak interval is split into in one round. */
const MAX_SPLIT = 5;
/** Strip width: 0.1 mm min, 10 mm default, 1 m max. */
export const StripWidthBounds =
{
    (meter)      : [1e-4, 1e-2, 1],
    (centimeter) : 1,
    (millimeter) : 10,
    (inch)       : 0.4,
    (foot)       : 0.03,
    (yard)       : 0.01
} as LengthBoundSpec;

/** Strip thickness: 0.01 mm min, 1 mm default, 0.1 m max. */
export const StripThicknessBounds =
{
    (meter)      : [1e-5, 1e-3, 0.1],
    (centimeter) : 0.1,
    (millimeter) : 1,
    (inch)       : 0.04,
    (foot)       : 0.003,
    (yard)       : 0.001
} as LengthBoundSpec;

/** Wire numbers in the overrides list: 0 is the start wire. */
export const WireIndexBounds = { (unitless) : [0, 0, 999] } as IntegerBoundSpec;

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

        annotation { "Name" : "Project wires onto faces", "Default" : true,
                     "Description" : "Each wire is one curve per face it crosses, projected onto that face, joined into a single wire. Off keeps the fitted curves (not exactly on the faces)." }
        definition.projectWires is boolean;

        annotation { "Name" : "Wire name prefix", "Default" : "even_divide_curve_",
                     "Description" : "Wires are named prefix + index, 0 at the start point (the uppermost crossing of the symmetry plane). Blank leaves them unnamed." }
        definition.wirePrefix is string;

        annotation { "Name" : "Keep section curves", "Default" : false }
        definition.keepSections is boolean;

        annotation { "Name" : "Create strips", "Default" : false,
                     "Description" : "Make a strip of the surface, centred on each wire (width measured along the surface, perpendicular to the wire), optionally thickened. Overlapping strips stay separate bodies." }
        definition.createStrips is boolean;

        if (definition.createStrips)
        {
            annotation { "Group Name" : "Strips", "Collapsed By Default" : false, "Driving Parameter" : "createStrips" }
            {
                annotation { "Name" : "Strip width", "Description" : "Width of each strip, centred on its wire." }
                isLength(definition.stripWidth, StripWidthBounds);

                annotation { "Name" : "Thicken strips", "Default" : true, "Description" : "Thicken each strip into a solid. Off leaves surface strips." }
                definition.thickenStrips is boolean;

                if (definition.thickenStrips)
                {
                annotation { "Name" : "Strip thickness", "Description" : "Thicken each strip by this much." }
                isLength(definition.stripThickness, StripThicknessBounds);

                annotation { "Name" : "Mid-plane", "Default" : false, "Description" : "Thicken equally to both sides of the surface." }
                definition.stripMidplane is boolean;

                if (!definition.stripMidplane)
                {
                    annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false,
                                 "Description" : "Thicken against the surface normal." }
                    definition.stripFlip is boolean;

                    annotation { "Name" : "Second direction", "Default" : false,
                                 "Description" : "Also thicken to the other side by a second thickness." }
                    definition.stripSecondDirection is boolean;

                    if (definition.stripSecondDirection)
                    {
                        annotation { "Name" : "Second thickness" }
                        isLength(definition.stripThickness2, StripThicknessBounds);
                    }
                }

                }

                annotation { "Name" : "Consume wires", "Default" : true, "Description" : "Remove the wires once their strips are made." }
                definition.consumeWires is boolean;

                annotation { "Name" : "Consume surface", "Default" : false, "Description" : "Delete the input surface once the strips are made." }
                definition.consumeSurface is boolean;

                annotation { "Name" : "Strip name prefix", "Default" : "even_divide_strip_",
                             "Description" : "Strips are named prefix + wire number. Blank leaves them unnamed." }
                definition.stripPrefix is string;

                annotation { "Name" : "Strip overrides", "Item name" : "override", "Item label template" : "Wire #wireIndex",
                             "Description" : "Change the width or thickness of single wires, or skip them. Wires are numbered from 0 at the start point." }
                definition.overrides is array;
                for (var ov in definition.overrides)
                {
                    annotation { "Name" : "Wire number" }
                    isInteger(ov.wireIndex, WireIndexBounds);

                    annotation { "Name" : "Skip wire", "Default" : false }
                    ov.skip is boolean;

                    if (!ov.skip)
                    {
                        annotation { "Name" : "Override width", "Default" : false }
                        ov.overrideWidth is boolean;

                        if (ov.overrideWidth)
                        {
                            annotation { "Name" : "Width" }
                            isLength(ov.width, StripWidthBounds);
                        }

                        annotation { "Name" : "Override thickness", "Default" : false }
                        ov.overrideThickness is boolean;

                        if (ov.overrideThickness)
                        {
                            annotation { "Name" : "Thickness" }
                            isLength(ov.thickness, StripThicknessBounds);
                        }
                    }
                }
            }
        }
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

        // 2. Section planes at even arc length over that range, and the curves they cut in the surface. Each division point
        //    also records the face it lies on, so a wire can later be built one face at a time.
        // Strip settings, and the "boundary tracks": for every strip two more division points per section, half the strip width
        // either side of the wire along the section curve.
        const stripSettings = definition.createStrips ? stripPlan(definition, definition.divisions) : undefined;
        var tracks = [];
        if (stripSettings != undefined)
        {
            for (var i = 0; i < definition.divisions; i += 1)
            {
                if (stripSettings.plan[i].skip)
                    continue;
                const halfWidth = stripSettings.plan[i].width / 2;
                tracks = append(tracks, { "wire" : i, "side" : -1, "halfWidth" : halfWidth });
                tracks = append(tracks, { "wire" : i, "side" : 1, "halfWidth" : halfWidth });
            }
        }

        const faceList = evaluateQuery(context, surfaceFaces);
        const faceBoxes = surfaceFaceBoxes(context, surfaceFaces, max(2 * surfaceTolerance, WIRE_CHECK_BOX_INFLATE));
        var sections = [];
        var sectionCount = 0;      // ids used so far: section0, section1, ...
        for (var k = 0; k < definition.sections; k += 1)
        {
            const u = firstU + (lastU - firstU) * k / (definition.sections - 1);
            const section = makeSection(context, id + ("section" ~ sectionCount), crossPath, u, surfaceFaces, planeSize, symPlane,
                                         definition.divisions, definition.onSymmetryPlane, faceBoxes, tracks);
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

        // 3. One wire per division, made of one curve per FACE RUN. Neighbouring faces meet at creases, so a single smooth
        //    curve cannot follow the surface across them: a wire is cut into runs of consecutive sections whose division point
        //    lies on the same face; at each face change the wire is cut at the seam (the crossing of the wire's chord with
        //    the faces' shared edge), and each run gets its own curve through its points and its seam points. Where a run's
        //    curve strays from the surface between two sections, sections are added there and the curves are fitted again.
        //
        //    Cost control: a curve's deviation is sampled INSIDE each section interval (its own parameters delimit the
        //    intervals, so no search is needed), each sample is measured against only the faces whose bounding box contains
        //    it, and after the first round only intervals near new sections are re-measured.
        const wireCount = definition.divisions;     // the sections' points also hold the strip-edge tracks after these
        const trackCount = size(sections[0].points);
        var intervalDev = makeArray(size(sections) - 1, 0 * meter);   // worst deviation seen in each interval
        var dirty = makeArray(size(sections) - 1, true);              // intervals to (re)measure this round
        var wireRuns = [];          // per division: its runs, each with the id and parameters of its fitted curve
        var fitIds = [];
        var rounds = 0;
        while (true)
        {
            wireRuns = [];
            fitIds = [];
            for (var i = 0; i < trackCount; i += 1)
            {
                var runs = buildRuns(context, sections, i, faceList);
                for (var r = 0; r < size(runs); r += 1)
                {
                    const fitId = id + ("fit" ~ i ~ "_" ~ r ~ "r" ~ rounds);
                    runs[r].fitId = fitId;
                    runs[r].params = fitRun(context, fitId, runs[r].pts);
                    fitIds = append(fitIds, fitId);
                }
                wireRuns = append(wireRuns, runs);
            }

            // Measure the dirty intervals: WIRE_CHECK_SAMPLES samples strictly inside each, on every curve.
            for (var j = 0; j < size(intervalDev); j += 1)
                if (dirty[j])
                    intervalDev[j] = 0 * meter;
            for (var i = 0; i < trackCount; i += 1)
            {
                for (var run in wireRuns[i])
                {
                    var sampleParams = [];
                    var sampleInterval = [];
                    for (var q = 0; q < size(run.ivals); q += 1)
                    {
                        if (!dirty[run.ivals[q]])
                            continue;
                        for (var k = 1; k <= WIRE_CHECK_SAMPLES; k += 1)
                        {
                            sampleParams = append(sampleParams, run.params[q] + (run.params[q + 1] - run.params[q]) * k / (WIRE_CHECK_SAMPLES + 1));
                            sampleInterval = append(sampleInterval, run.ivals[q]);
                        }
                    }
                    if (size(sampleParams) == 0)
                        continue;
                    const lines = evEdgeTangentLines(context, { "edge" : qCreatedBy(run.fitId, EntityType.EDGE), "parameters" : sampleParams,
                                                                "arcLengthParameterization" : false });
                    for (var m = 0; m < size(lines); m += 1)
                        intervalDev[sampleInterval[m]] = max(intervalDev[sampleInterval[m]], distanceToSurface(context, faceBoxes, surfaceFaces, lines[m].origin));
                }
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

            // Split each weak interval into pieces sized from its error, then fit again; this round's curves are discarded.
            // Within one face the error falls like the 4th power of the spacing, but a round (fit + measure) costs far more
            // than a section, so the square law is assumed.
            opDeleteBodies(context, id + ("discard" ~ rounds), { "entities" : qUnion(mapArray(fitIds, function(w) { return qCreatedBy(w, EntityType.BODY); })) });
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
                                                    definition.divisions, definition.onSymmetryPlane, faceBoxes, tracks);
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
            // An interval next to a changed one is re-measured too: the refit moves the curves a little there.
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

        // 4. The wire bodies: each run's curve is projected onto its own face (the fitted curve stays when the projection fails)
        //    and the runs of a wire are joined into ONE wire body, which is named.
        var scratch = [];
        var edgeTotal = 0;
        var projectedRuns = 0;
        var totalRuns = 0;
        for (var i = 0; i < wireCount; i += 1)
        {
            const made = finishWire(context, id + ("wire" ~ i), id + ("project" ~ i ~ "_"), wireRuns[i], definition.projectWires, faceList);
            scratch = concatenateArrays([scratch, made.scratch]);
            edgeTotal += made.edges;
            projectedRuns += made.projectedRuns;
            totalRuns += made.runs;
            if (definition.wirePrefix != "")
            {
                try silent
                {
                    setProperty(context, { "entities" : qCreatedBy(id + ("wire" ~ i), EntityType.BODY), "propertyType" : PropertyType.NAME,
                                           "value" : definition.wirePrefix ~ i });
                }
            }
        }

        // 5. Strips. The strip edges are the "boundary tracks" computed with the sections; they are fitted from the final
        //    sections (not refined separately) and built like wires. A track that lies entirely on the surface boundary
        //    (the strip is clipped there) needs no cut.
        var boundaryWireIds = [];
        var boundaryExtensions = [];     // per track: ids of the two extension lines (or [])
        for (var j = 0; j < size(tracks); j += 1)
        {
            var allClamped = true;
            for (var s in sections)
                if (!s.clamped[j])
                    allClamped = false;
            if (allClamped)
            {
                boundaryWireIds = append(boundaryWireIds, undefined);
                boundaryExtensions = append(boundaryExtensions, []);
                for (var run in wireRuns[wireCount + j])
                    scratch = append(scratch, qCreatedBy(run.fitId, EntityType.BODY));
                continue;
            }
            const boundaryRuns = wireRuns[wireCount + j];     // fitted and refined with the wires
            const boundaryMade = finishWire(context, id + ("bwire" ~ j), id + ("bproject" ~ j ~ "_"), boundaryRuns, definition.projectWires, faceList);
            scratch = concatenateArrays([scratch, boundaryMade.scratch]);
            boundaryWireIds = append(boundaryWireIds, id + ("bwire" ~ j));
            // The strip edge stops short of the surface ends (the last complete section): continue it straight along its end
            // tangents so the cut reaches the boundary (the part beyond the surface projects onto nothing).
            var extensionIds = [];
            try
            {
                const startLine = evEdgeTangentLine(context, { "edge" : qCreatedBy(boundaryRuns[0].fitId, EntityType.EDGE), "parameter" : 0 });
                const endLine = evEdgeTangentLine(context, { "edge" : qCreatedBy(boundaryRuns[size(boundaryRuns) - 1].fitId, EntityType.EDGE), "parameter" : 1 });
                opPolyline(context, id + ("bext" ~ j ~ "a"), { "points" : [startLine.origin - startLine.direction * STRIP_EXTENSION, startLine.origin] });
                opPolyline(context, id + ("bext" ~ j ~ "b"), { "points" : [endLine.origin, endLine.origin + endLine.direction * STRIP_EXTENSION] });
                extensionIds = [id + ("bext" ~ j ~ "a"), id + ("bext" ~ j ~ "b")];
            }
            catch (e)
            {
                println("evenlyDivideSurface: strip edge " ~ j ~ " could not be extended: " ~ toString(e));
            }
            boundaryExtensions = append(boundaryExtensions, extensionIds);
        }

        var stripNote = "";
        var stripWarnings = [];
        var builtStrips = 0;
        if (definition.createStrips)
        {
            stripWarnings = stripSettings.warnings;
            var consumed = [];
            for (var i = 0; i < wireCount; i += 1)
            {
                if (stripSettings.plan[i].skip)
                    continue;
                var toolEdges = [];
                for (var j = 0; j < size(tracks); j += 1)
                    if (tracks[j].wire == i && boundaryWireIds[j] != undefined)
                        toolEdges = append(toolEdges, qOwnedByBody(qCreatedBy(boundaryWireIds[j], EntityType.BODY), EntityType.EDGE));
                for (var j = 0; j < size(tracks); j += 1)
                    if (tracks[j].wire == i && boundaryWireIds[j] != undefined)
                        for (var extensionId in boundaryExtensions[j])
                            toolEdges = append(toolEdges, qOwnedByBody(qCreatedBy(extensionId, EntityType.BODY), EntityType.EDGE));
                const stripId = id + ("strip" ~ i);
                const wireEdgesQuery = qOwnedByBody(qCreatedBy(id + ("wire" ~ i), EntityType.BODY), EntityType.EDGE);
                var wireLength = 0 * meter;
                for (var wireEdge in evaluateQuery(context, wireEdgesQuery))
                    wireLength += evLength(context, { "entities" : wireEdge });
                const expectedArea = stripSettings.plan[i].width * wireLength;

                // Two attempts: first the cut edges found by position block the flood; if that strip is too large (a flood that
                // escaped through an incomplete cut), the edges opSplitFace reports are blocked as well. A strip that is still
                // far larger than width x length is not kept.
                var sheet = undefined;
                for (var attempt = 0; attempt < 2 && sheet == undefined; attempt += 1)
                {
                    const candidate = buildStrip(context, stripId + ("try" ~ attempt), surfaceFaces, toolEdges, wireEdgesQuery, attempt == 1);
                    if (candidate == undefined)
                        continue;
                    const stripArea = evArea(context, { "entities" : candidate });
                    if (stripArea > STRIP_AREA_LIMIT * expectedArea)
                    {
                        println("evenlyDivideSurface: strip " ~ i ~ " attempt " ~ attempt ~ ": area " ~ roundToPrecision(stripArea / (millimeter ^ 2), 0) ~
                                " mm2 against about " ~ roundToPrecision(expectedArea / (millimeter ^ 2), 0) ~ " mm2");
                        opDeleteBodies(context, stripId + ("reject" ~ attempt), { "entities" : candidate });
                        continue;
                    }
                    sheet = candidate;
                }
                if (sheet == undefined)
                {
                    stripWarnings = append(stripWarnings, "Strip " ~ i ~ " was not built: the cut around it did not close. Try a narrower strip.");
                    continue;
                }
                var stripBody = sheet;
                if (definition.thickenStrips)
                {
                    const thickness = stripSettings.plan[i].thickness;
                    var thickness1 = thickness;      // along the surface normal
                    var thickness2 = 0 * meter;      // against it
                    if (definition.stripMidplane)
                    {
                        thickness1 = thickness / 2;
                        thickness2 = thickness / 2;
                    }
                    else
                    {
                        const second = definition.stripSecondDirection ? definition.stripThickness2 : 0 * meter;
                        if (definition.stripFlip)
                        {
                            thickness1 = second;
                            thickness2 = thickness;
                        }
                        else
                        {
                            thickness2 = second;
                        }
                    }
                    // Thickening across a sharp fold can fail when both sides are thickened at once (the offsets collide on the
                    // concave side). Fall back to the whole thickness on one side, front first.
                    const attempts = [{ "t1" : thickness1, "t2" : thickness2, "note" : "" },
                                      { "t1" : thickness, "t2" : 0 * meter, "note" : "was thickened to its front side only (the requested thickening failed across a sharp fold)" },
                                      { "t1" : 0 * meter, "t2" : thickness, "note" : "was thickened to its back side only (the requested thickening failed across a sharp fold)" }];
                    var thickened = false;
                    for (var attempt in attempts)
                    {
                        if (thickened)
                            break;
                        if (attempt.note != "" && thickness1 == thickness && thickness2 == 0 * meter && attempt.t1 == thickness)
                            continue;      // that is what was just tried
                        try
                        {
                            opThicken(context, stripId + ("thicken" ~ (attempt.note == "" ? "" : (attempt.t1 > 0 * meter ? "F" : "B"))),
                                      { "entities" : sheet, "thickness1" : attempt.t1, "thickness2" : attempt.t2, "keepTools" : false });
                            stripBody = qCreatedBy(stripId + ("thicken" ~ (attempt.note == "" ? "" : (attempt.t1 > 0 * meter ? "F" : "B"))), EntityType.BODY);
                            thickened = true;
                            if (attempt.note != "")
                                stripWarnings = append(stripWarnings, "Strip " ~ i ~ " " ~ attempt.note ~ ".");
                        }
                        catch (e)
                        {
                            println("evenlyDivideSurface: strip " ~ i ~ " thickening attempt failed: " ~ toString(e));
                        }
                    }
                    if (!thickened)
                        stripWarnings = append(stripWarnings, "Strip " ~ i ~ " could not be thickened and stays a surface.");
                }
                if (definition.stripPrefix != "")
                {
                    try silent
                    {
                        setProperty(context, { "entities" : stripBody, "propertyType" : PropertyType.NAME, "value" : definition.stripPrefix ~ i });
                    }
                }
                builtStrips += 1;
                consumed = append(consumed, i);
            }
            // The strip edges are construction geometry; the wires go too when asked (only those whose strip was built).
            for (var j = 0; j < size(boundaryWireIds); j += 1)
            {
                if (boundaryWireIds[j] == undefined)
                    continue;
                if (definition.keepSections)
                {
                    // Debug aid: the strip edges are kept and named too.
                    try silent
                    {
                        setProperty(context, { "entities" : qCreatedBy(boundaryWireIds[j], EntityType.BODY), "propertyType" : PropertyType.NAME,
                                               "value" : "strip_edge_" ~ tracks[j].wire ~ (tracks[j].side < 0 ? "_a" : "_b") });
                    }
                }
                else
                {
                    scratch = append(scratch, qCreatedBy(boundaryWireIds[j], EntityType.BODY));
                    for (var extensionId in boundaryExtensions[j])
                        scratch = append(scratch, qCreatedBy(extensionId, EntityType.BODY));
                }
            }
            if (definition.consumeWires)
                for (var i in consumed)
                    scratch = append(scratch, qCreatedBy(id + ("wire" ~ i), EntityType.BODY));
            if (definition.consumeSurface && builtStrips > 0)
                opDeleteBodies(context, id + "consumeSurface", { "entities" : definition.surface });
            stripNote = " " ~ stripSettings.summary ~ " " ~ builtStrips ~ " strips built" ~ (definition.thickenStrips ? " and thickened" : " as surfaces") ~ ".";
        }

        // 6. Clean up the construction geometry.
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

        const summary = wireCount ~ " wires (" ~ edgeTotal ~ " edges) through " ~ size(sections) ~ " cross sections" ~
                        (size(sections) > baseSections ? " (" ~ (size(sections) - baseSections) ~ " added to follow the surface)" : "") ~
                        (closedCount > 0 ? ", closed sections" : ", open sections") ~
                        (definition.projectWires ? "; " ~ projectedRuns ~ " of " ~ totalRuns ~ " face runs projected onto their face" : "") ~
                        "; widest gap before projection " ~ roundToPrecision(worstOff / millimeter, 4) ~ " mm.";
        println("evenlyDivideSurface: " ~ summary ~ stripNote);
        if (size(stripWarnings) > 0)
            reportFeatureWarning(context, id, summary ~ stripNote ~ " " ~ join(stripWarnings, " "));
        else if (worstOff > surfaceTolerance)
            reportFeatureWarning(context, id, summary ~ stripNote ~ (definition.refine ? " Increase the number of cross sections." : " Turn on Refine sections or increase the number of cross sections."));
        else
            reportFeatureInfo(context, id, summary ~ (definition.projectWires && projectedRuns < totalRuns ? " The others lie along a seam or the surface boundary, or kept their fitted curve (within the tolerance)." : "") ~ stripNote);
    });


/**
 * Project each run's curve of one wire onto its own face and join the runs into ONE wire body `wireId`.
 *
 * @returns {{ edges : number of edges of the wire, projectedRuns, runs, scratch : array of queries to delete }}
 */
function finishWire(context is Context, wireId is Id, projectPrefix is Id, runs is array, projectWires is boolean, faceList is array) returns map
{
    var pieceEdges = [];
    var scratch = [];
    var projectedRuns = 0;
    for (var r = 0; r < size(runs); r += 1)
    {
        const run = runs[r];
        const fitEdge = qCreatedBy(run.fitId, EntityType.EDGE);
        var useEdges = fitEdge;
        // Only a run on ONE face is projected: a run over several faces lies along a seam (or its seam could not be
        // found), where it is already on both faces and a projection onto both is unreliable.
        if (projectWires && size(run.faces) == 1)
        {
            const projectId = projectPrefix + toString(r);
            try
            {
                opDropCurve(context, projectId, { "tools" : fitEdge, "targets" : faceList[run.faces[0]],
                                                  "projectionType" : ProjectionType.NORMAL_TO_TARGET });
                const projected = qCreatedBy(projectId, EntityType.EDGE);
                scratch = append(scratch, qCreatedBy(projectId, EntityType.BODY));
                if (!isQueryEmpty(context, projected))
                {
                    // A projection that wanders (much longer or shorter than the curve it came from) is rejected.
                    const projectedLength = evLength(context, { "entities" : projected });
                    const fittedLength = evLength(context, { "entities" : fitEdge });
                    if (abs(projectedLength - fittedLength) <= PROJECT_LENGTH_TOLERANCE * fittedLength)
                    {
                        useEdges = projected;
                        projectedRuns += 1;
                    }
                    else
                    {
                        println("evenlyDivideSurface: run " ~ r ~ " projection strayed (" ~ roundToPrecision(projectedLength / millimeter, 2) ~
                                " mm against " ~ roundToPrecision(fittedLength / millimeter, 2) ~ " mm); the fitted curve is kept");
                    }
                }
            }
            catch (e)
            {
                println("evenlyDivideSurface: run " ~ r ~ " not projected: " ~ toString(e));
            }
        }
        pieceEdges = append(pieceEdges, useEdges);
        scratch = append(scratch, qCreatedBy(run.fitId, EntityType.BODY));
    }
    try
    {
        opExtractWires(context, wireId, { "edges" : qUnion(pieceEdges) });
    }
    catch
    {
        // Projected pieces that overlap cannot be joined: join the fitted curves instead.
        println("evenlyDivideSurface: a wire could not be joined after projection; using the fitted curves");
        opExtractWires(context, wireId, { "edges" : qUnion(mapArray(runs, function(run) { return qCreatedBy(run.fitId, EntityType.EDGE); })) });
    }
    return { "edges" : size(evaluateQuery(context, qCreatedBy(wireId, EntityType.EDGE))), "projectedRuns" : projectedRuns, "runs" : size(runs), "scratch" : scratch };
}

/**
 * One strip of the surface: a copy of the surface is split along the strip's edge wires (`toolEdges`, which lie on the surface;
 * the split is extended to complete the cut), the faces of the copy that lie between them are found by flooding from the
 * faces the wire runs on across every edge that is not a cut, and those faces are extracted as a new sheet body. A fresh copy
 * per strip keeps overlapping strips apart.
 *
 * @returns Query of the strip sheet, or `undefined` when it could not be built
 */
function buildStrip(context is Context, stripId is Id, surfaceFaces is Query, toolEdges is array, wireEdges is Query, alsoReportedEdges is boolean)
{
    try
    {
        opExtractSurface(context, stripId + "copy", { "faces" : surfaceFaces });
        const copyBody = qCreatedBy(stripId + "copy", EntityType.BODY);
        var blocked = qNothing();
        if (size(toolEdges) > 0)
        {
            const split = opSplitFace(context, stripId + "split", { "faceTargets" : qOwnedByBody(copyBody, EntityType.FACE),
                                                      "edgeTools" : qUnion(toolEdges),
                                                      "projectionType" : ProjectionType.NORMAL_TO_TARGET,
                                                      "extendToCompletion" : true });
            // The cut edges: edges of the copy that lie on the strip-edge wires or their extensions, found by position. (The
            // edges opSplitFace reports also include every existing edge the faces share, which would over-block the flood.)
            const tools = qUnion(toolEdges);
            var cuts = [];
            var gaps = [];
            for (var edge in evaluateQuery(context, qOwnedByBody(copyBody, EntityType.EDGE)))
            {
                var onTool = true;
                var nearest = undefined;
                for (var l in evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0.25, 0.5, 0.75] }))
                {
                    const distance = evDistance(context, { "side0" : l.origin, "side1" : tools }).distance;
                    if (nearest == undefined)
                        nearest = distance;
                    if (distance > STRIP_CUT_TOLERANCE)
                    {
                        onTool = false;
                        break;
                    }
                }
                if (onTool)
                    cuts = append(cuts, edge);
                else if (nearest < STRIP_GAP_REACH && evLength(context, { "entities" : edge }) < STRIP_GAP_LENGTH)
                    gaps = append(gaps, edge);     // a sliver edge right beside a cut: the cut did not quite cross it
            }
            // Cut edges and the gaps beside them stop the flood. (The edges opSplitFace reports also include every existing
            // shared edge, which over-blocks on a closed surface; they are only a second-attempt fallback.)
            blocked = qUnion(concatenateArrays([cuts, gaps, alsoReportedEdges ? split.splittingEdges : []]));
        }
        const copyFaces = qOwnedByBody(copyBody, EntityType.FACE);
        const faceArray = evaluateQuery(context, copyFaces);

        // Seeds: the faces the wire runs on.
        var faceIndex = {};
        for (var n = 0; n < size(faceArray); n += 1)
            faceIndex[toString(faceArray[n])] = n;
        var inStrip = makeArray(size(faceArray), false);
        var frontier = [];
        for (var edge in evaluateQuery(context, wireEdges))
        {
            for (var l in evEdgeTangentLines(context, { "edge" : edge, "parameters" : range(0, 1, STRIP_SEED_SAMPLES) }))
            {
                const index = evDistance(context, { "side0" : l.origin, "side1" : copyFaces }).sides[1].index;
                if (!inStrip[index])
                {
                    inStrip[index] = true;
                    frontier = append(frontier, index);
                }
            }
        }
        if (size(frontier) == 0)
            throw regenError("The strip's wire is not on the surface.");

        // Flood: a neighbouring face joins the strip when it shares at least one edge with the face that is NOT a cut.
        for (var iteration = 0; iteration < STRIP_FLOOD_LIMIT && size(frontier) > 0; iteration += 1)
        {
            var next = [];
            for (var fi in frontier)
            {
                const edgesOfFace = qAdjacent(faceArray[fi], AdjacencyType.EDGE, EntityType.EDGE);
                for (var neighbour in evaluateQuery(context, qAdjacent(faceArray[fi], AdjacencyType.EDGE, EntityType.FACE)))
                {
                    const ni = faceIndex[toString(neighbour)];
                    if (ni == undefined || inStrip[ni])
                        continue;
                    const shared = qIntersection([edgesOfFace, qAdjacent(neighbour, AdjacencyType.EDGE, EntityType.EDGE)]);
                    if (!isQueryEmpty(context, qSubtraction(shared, blocked)))
                    {
                        inStrip[ni] = true;
                        next = append(next, ni);
                    }
                }
            }
            frontier = next;
        }
        // Zero-area sliver faces (the cut passing through a vertex or seam) add no surface and stop the sheet from thickening.
        var chosen = [];
        for (var n = 0; n < size(faceArray); n += 1)
            if (inStrip[n] && evArea(context, { "entities" : faceArray[n] }) > STRIP_MIN_FACE_AREA)
                chosen = append(chosen, faceArray[n]);
        const stripFaces = qUnion(chosen);
        opExtractSurface(context, stripId + "sheet", { "faces" : stripFaces });
        opDeleteBodies(context, stripId + "dropCopy", { "entities" : copyBody });
        return qCreatedBy(stripId + "sheet", EntityType.BODY);
    }
    catch (e)
    {
        println("evenlyDivideSurface: strip could not be built: " ~ toString(e));
        try silent
        {
            opDeleteBodies(context, stripId + "dropCopyAfterError", { "entities" : qCreatedBy(stripId + "copy", EntityType.BODY) });
        }
        return undefined;
    }
}


/**
 * Cut the surface at parameter `u` of the wire and divide the section. Everything made is created under `id`.
 *
 * @returns {{ u, points, closed, faces }} (faces: index of the face each division point lies on) or `undefined` when the plane cuts nothing usable
 */
function makeSection(context is Context, id is Id, crossPath is Path, u is number, surfaceFaces is Query, planeSize is ValueWithUnits,
        symPlane is Plane, divisions is number, onSymmetryPlane is boolean, faceBoxes is array, tracks is array)
{
    const cut = cutSection(context, id, crossPath, u, surfaceFaces, planeSize);
    if (cut.path == undefined)
        return undefined;
    try
    {
        const pe = pathEvaluator(context, cut.path);
        const divided = divideSection(context, pe, symPlane, divisions, onSymmetryPlane);
        var points = divided.points;
        var clamped = [];
        if (size(tracks) > 0)
        {
            // Strip edges: half a strip width either side of the wire's division point, along this section curve. On an
            // open section the curve ends at the surface boundary: the point is clamped there (and marked).
            var us = [];
            for (var track in tracks)
            {
                const raw = divided.startU + divided.direction * (divided.fractions[track.wire] + track.side * track.halfWidth / divided.perimeter);
                if (divided.closed)
                {
                    us = append(us, wrapUnit(raw));
                    clamped = append(clamped, false);
                }
                else
                {
                    us = append(us, clamp(raw, 0, 1));
                    clamped = append(clamped, raw < 0 || raw > 1);
                }
            }
            points = concatenateArrays([points, mapArray(evaluatePath(context, pe, us), function(l) { return l.origin; })]);
        }
        const faces = mapArray(points, function(point) { return nearestFaceIndex(context, faceBoxes, surfaceFaces, point); });
        return { "u" : u, "points" : points, "closed" : divided.closed, "faces" : faces, "clamped" : clamped };
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
    return { "path" : path, "cum" : cum, "total" : total };
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
/**
 * Cut one division's wire into runs of consecutive sections whose division point lies on the same face.
 *
 * Each run is { faces, pts, ivals }: `pts` are the points its curve passes through (section points, and the seam points at
 * its ends where it meets the next/previous face), `ivals[q]` is the section interval between pts[q] and pts[q+1].
 * Where two consecutive faces share no edge near the wire, or the seam cannot be found, the runs are not split there: the
 * run continues across both faces (and is later projected onto both).
 */
function buildRuns(context is Context, sections is array, i is number, faceList is array) returns array
{
    var runs = [];
    var current = { "faces" : [sections[0].faces[i]], "pts" : [sections[0].points[i]], "ivals" : [], "last" : sections[0].faces[i] };
    var skipPoint = false;
    for (var k = 1; k < size(sections); k += 1)
    {
        const p = sections[k].points[i];
        const f = sections[k].faces[i];
        if (f != current.last)
        {
            const seam = seamPoint(context, faceList[current.last], faceList[f], sections[k - 1].points[i], p);
            if (seam == undefined)
            {
                // No usable seam: one run over both faces.
                current.faces = addFace(current.faces, f);
            }
            else
            {
                // A section point very close to the seam would force the curve through two nearly coincident points (a kink):
                // the seam point is on the surface right beside it, so the section point is left out.
                const spacing = norm(p - sections[k - 1].points[i]);
                if (size(current.pts) >= 2 && norm(seam - current.pts[size(current.pts) - 1]) < SEAM_CLOSE * spacing)
                {
                    current.pts = subArray(current.pts, 0, size(current.pts) - 1);
                    current.ivals = subArray(current.ivals, 0, size(current.ivals) - 1);
                }
                current = addPoint(current, seam, k - 1);
                if (size(current.pts) >= 2)
                    runs = append(runs, current);
                current = { "faces" : [f], "pts" : [seam], "ivals" : [], "last" : f };
                skipPoint = norm(seam - p) < SEAM_CLOSE * spacing;
            }
            current.last = f;
        }
        if (skipPoint)
            skipPoint = false;
        else
            current = addPoint(current, p, k - 1);
    }
    if (size(current.pts) >= 2)
        runs = append(runs, current);
    return runs;
}

/** Append `p` to a run's points (and the section interval it ends), unless it coincides with the last point. */
function addPoint(run is map, p is Vector, interval is number) returns map
{
    if (norm(p - run.pts[size(run.pts) - 1]) < SEAM_MERGE)
        return run;
    run.pts = append(run.pts, p);
    run.ivals = append(run.ivals, interval);
    return run;
}

function addFace(faces is array, f is number) returns array
{
    for (var g in faces)
        if (g == f)
            return faces;
    return append(faces, f);
}

/**
 * Where the wire crosses from face A to face B: the point on their shared edge closest to the line through the wire's
 * points on either side (`pA` on A, `pB` on B). `undefined` if the faces share no edge, the edge is not within SEAM_REACH of
 * that line, or the crossing is not between the two points.
 */
function seamPoint(context is Context, faceA is Query, faceB is Query, pA is Vector, pB is Vector)
{
    const shared = qIntersection([qAdjacent(faceA, AdjacencyType.EDGE, EntityType.EDGE), qAdjacent(faceB, AdjacencyType.EDGE, EntityType.EDGE)]);
    if (isQueryEmpty(context, shared))
        return undefined;
    // A wire running ALONG the seam (both points on the shared edge) has no crossing to find: it stays one run.
    if (evDistance(context, { "side0" : pA, "side1" : shared }).distance < SEAM_ALONG && evDistance(context, { "side0" : pB, "side1" : shared }).distance < SEAM_ALONG)
        return undefined;
    const chord = pB - pA;
    const length = norm(chord);
    if (length < SEAM_MERGE)
        return undefined;
    const result = evDistance(context, { "side0" : shared, "side1" : line(pA, chord / length) });
    if (result.distance > SEAM_REACH)
        return undefined;
    const along = dot(result.sides[1].point - pA, chord) / (length * length);
    if (along < -0.05 || along > 1.05)
        return undefined;
    return result.sides[0].point;
}

/** Fit a curve through a run's points (a line for two points); returns the chord-length parameters of the points on it. */
function fitRun(context is Context, fitId is Id, pts is array) returns array
{
    const params = chordLengthParameters(pts);
    if (size(pts) == 2)
        opPolyline(context, fitId, { "points" : pts });
    else
        opFitSpline(context, fitId, { "points" : pts, "parameters" : params });
    return params;
}

/**
 * The strip settings per wire (what phase 2 will build from): the default width and thickness, changed by the overrides.
 *
 * @returns {{ plan : array of { skip, width, thickness, overridden } (one per wire, index 0 = start wire),
 *             warnings : array of string, summary : string }}
 */
function stripPlan(definition is map, wireCount is number) returns map
{
    var plan = [];
    for (var w = 0; w < wireCount; w += 1)
        plan = append(plan, { "skip" : false, "width" : definition.stripWidth, "thickness" : definition.thickenStrips ? definition.stripThickness : 0 * meter, "overridden" : false });

    var warnings = [];
    for (var o in definition.overrides)
    {
        if (o.wireIndex >= wireCount)
        {
            warnings = append(warnings, "Override for wire " ~ o.wireIndex ~ " ignored: there are only " ~ wireCount ~ " wires (0 to " ~ (wireCount - 1) ~ ").");
            continue;
        }
        var entry = plan[o.wireIndex];
        if (entry.overridden)
            warnings = append(warnings, "Wire " ~ o.wireIndex ~ " has more than one override; they are applied in order.");
        entry.overridden = true;
        entry.skip = o.skip;
        if (!o.skip)
        {
            if (o.overrideWidth)
                entry.width = o.width;
            if (o.overrideThickness && definition.thickenStrips)
                entry.thickness = o.thickness;
        }
        plan[o.wireIndex] = entry;
    }

    var skipped = 0;
    var widthChanged = 0;
    var thicknessChanged = 0;
    for (var entry in plan)
    {
        if (entry.skip)
            skipped += 1;
        else
        {
            if (abs(entry.width - definition.stripWidth) > 1e-12 * meter)
                widthChanged += 1;
            if (definition.thickenStrips && abs(entry.thickness - definition.stripThickness) > 1e-12 * meter)
                thicknessChanged += 1;
        }
    }
    const summary = "Strips: " ~ (wireCount - skipped) ~ " of " ~ wireCount ~ " wires, width " ~ roundToPrecision(definition.stripWidth / millimeter, 3) ~
                    " mm (" ~ widthChanged ~ " overridden), " ~
                    (definition.thickenStrips ? "thickness " ~ roundToPrecision(definition.stripThickness / millimeter, 3) ~ " mm (" ~ thicknessChanged ~ " overridden), " : "not thickened, ") ~
                    skipped ~ " skipped" ~
                    (definition.thickenStrips && definition.stripMidplane ? ", mid-plane" : (definition.thickenStrips && definition.stripFlip ? ", opposite direction" : "")) ~
                    (definition.thickenStrips && !definition.stripMidplane && definition.stripSecondDirection ? ", second direction " ~ roundToPrecision(definition.stripThickness2 / millimeter, 3) ~ " mm" : "") ~
                    (definition.consumeWires ? ", wires consumed" : ", wires kept") ~ (definition.consumeSurface ? ", surface consumed" : "") ~ ".";
    return { "plan" : plan, "warnings" : warnings, "summary" : summary };
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
             "crossings" : size(crossings), "startU" : start.u, "direction" : direction, "fractions" : fractions, "perimeter" : pe.total };
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
