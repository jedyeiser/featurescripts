FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "14e722813a28828489a4152a");
//import tools/transition_functions (export import)
export import(path : "b1e8bfe71f67389ca210ed8b/82e98a4cc11d1d3bbe2adf53/a656fa0d17723f0dafaf8638", version : "f1dad98aa52458dfc0c1109e");

// IMPORT: scaled_curve_icon.svg (feature icon)
IconNamespace::import(path : "8b2fda35054b2e92a6334cd4", version : "501e50ca04aec46d534e616f");

/** Distance between samples along the longer group (the sample count is at least "Minimum samples"). */
export const SC_SAMPLE_SPACING_BOUNDS = { (millimeter) : [0.01, 5, 1e5] } as LengthBoundSpec;

/** Cap on the samples per group (an info notice says when it is reached). */
const SC_MAX_SAMPLES = 1000;

/** Edge end points this close join into one path (the same as std Edit curve's approximation). */
const SC_PATH_TOLERANCE = 1e-5 * meter;

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Scaled Curve",
        "Feature Type Description" : "Creates a new B-spline curve that blends between two edge chains (open or closed), with a scale factor that changes from start to end." }
export const createScaledCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Group 0", "Filter" : EntityType.EDGE }
        definition.group0 is Query;

        annotation { "Name" : "Reverse Group 0", "Default" : false, "UIHint" : [UIHint.OPPOSITE_DIRECTION],
                     "Description" : "Group 0 is paired with Group 1 automatically (open: nearest ends; closed: same direction of travel, seams aligned). This reverses that pairing." }
        definition.flipPairing is boolean;

        annotation { "Name" : "Group 1", "Filter" : EntityType.EDGE }
        definition.group1 is Query;

        annotation { "Name" : "Initial curve scale factor", "Description" : "Where the curve sits at the start of Group 1, in [-0.5, 0.5]: -0.5 = on Group 0, +0.5 = on Group 1." }
        isReal(definition.sf0, ScaledCurveParameterBounds);

        annotation { "Name" : "Final curve scale factor", "Description" : "Where the curve sits at the end of Group 1, in [-0.5, 0.5]: -0.5 = on Group 0, +0.5 = on Group 1." }
        isReal(definition.sf1, ScaledCurveParameterBounds);

        annotation { "Name" : "Transition type", "Default" : TransitionType.LINEAR, "UIHint" : [UIHint.SHOW_LABEL],
                     "Description" : "How the scale factor changes from the initial to the final value, by arc length along Group 1." }
        definition.transitionType is TransitionType;

        annotation { "Name" : "Curve name", "Description" : "When not blank, the output wire body will get this name." }
        definition.curveName is string;

        annotation { "Name" : "Project onto surface?" }
        definition.curveOnSurface is boolean;

        if (definition.curveOnSurface)
        {
            annotation { "Name" : "Projection face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
            definition.projectionFace is Query;
        }

        annotation { "Group Name" : "Debug & Details", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show endpoints", "Description" : "When true, shows the paired start points in GREEN and end points in RED", "Default" : false }
            definition.showEndpoints is boolean;

            annotation { "Name" : "Show curves", "Description" : "When true, shows Group 0 in CYAN and Group 1 in MAGENTA", "Default" : false }
            definition.showGroups is boolean;

            annotation { "Name" : "Print BSplineCurve data", "Description" : "Prints the sampling and the output B-spline", "Default" : false }
            definition.printBsplines is boolean;

            if (definition.printBsplines)
            {
                annotation { "Name" : "Data depth", "UIHint" : [UIHint.SHOW_LABEL], "Description" : "Print metadata only, or all details of the output curve" }
                definition.bsplineFormat is PrintFormat;
            }

            annotation { "Group Name" : "Details", "Collapsed By Default" : true }
            {
                annotation { "Group Name" : "Output parameters", "Collapsed By Default" : true }
                {
                    annotation { "Name" : "Minimum samples", "Description" : "At least this many samples along the curve; more when Sample spacing asks for them." }
                    isInteger(definition.numScaledSamples, SampleCountBounds);

                    annotation { "Name" : "Scaled tolerance", "Description" : "Fit tolerance for the output curve. Onshape minimum of 1e-8 meter or 1e-5 millimeter" }
                    isLength(definition.scaledTol, FitToleranceBounds);

                    annotation { "Name" : "Scaled curve degree", "Description" : "Degree of the output curve" }
                    isInteger(definition.scaledDegree, curveDegreeBounds);
                }

                annotation { "Name" : "Sample spacing", "Description" : "Distance between samples along the longer group. Both groups are sampled at the same arc-length fractions." }
                isLength(definition.sampleSpacing, SC_SAMPLE_SPACING_BOUNDS);
            }
        }
    }
    {
        var notes = [];

        // ---- 1. Each group as one native path (arc-length parameterized) ----
        const path0 = scPath(context, definition.group0, "group0");
        const path1 = scPath(context, definition.group1, "group1");
        if (path0.closed != path1.closed)
        {
            throw regenError("Group 0 and Group 1 must both be closed or both be open.", ["group0", "group1"]);
        }
        const closed = path1.closed;
        const length0 = evPathLength(context, path0);
        const length1 = evPathLength(context, path1);

        // ---- 2. Sample count from the spacing (at least Minimum samples, at most SC_MAX_SAMPLES) ----
        var n = max(definition.numScaledSamples, ceil(max(length0, length1) / definition.sampleSpacing) + 1);
        if (n > SC_MAX_SAMPLES)
        {
            notes = append(notes, "Sampling capped at " ~ SC_MAX_SAMPLES ~ " samples (Sample spacing asks for " ~ n ~ ").");
            n = SC_MAX_SAMPLES;
        }
        var fractions = [];
        for (var i = 0; i < n; i += 1)
        {
            fractions = append(fractions, i / (n - 1));
        }

        // ---- 3. Pairing: Group 1 sets the direction; Group 0 is oriented (and, closed, its seam aligned) to it ----
        const pairing = scPairing(context, path0, path1, definition.flipPairing);
        const sfStart = 0.5 + definition.sf0;
        const sfEnd = 0.5 + definition.sf1;
        const periodic = closed && abs(sfStart - sfEnd) < 1e-12;
        if (closed && !periodic)
        {
            notes = append(notes, "Closed groups with different initial and final scale factors give an open curve (its ends meet only when the factors are equal).");
        }

        var positions = scBlendAt(context, path0, path1, pairing, fractions, sfStart, sfEnd, definition.transitionType);

        // Exact blend halfway between samples, for the deviation check.
        var midFractions = [];
        for (var i = 0; i < n - 1; i += 1)
        {
            midFractions = append(midFractions, (fractions[i] + fractions[i + 1]) / 2);
        }
        var midPositions = scBlendAt(context, path0, path1, pairing, midFractions, sfStart, sfEnd, definition.transitionType);

        if (definition.curveOnSurface)
        {
            if (isQueryEmpty(context, definition.projectionFace))
            {
                throw regenError("Select a projection face, or turn off Project onto surface.", ["projectionFace"]);
            }
            positions = scProjectPoints(context, positions, definition.projectionFace);
            midPositions = scProjectPoints(context, midPositions, definition.projectionFace);
        }

        if (definition.showEndpoints)
        {
            const ends0 = evPathTangentLines(context, path0, [scFraction0(pairing, 0), scFraction0(pairing, 1)]).tangentLines;
            const ends1 = evPathTangentLines(context, path1, [0, 1]).tangentLines;
            addDebugPoint(context, ends0[0].origin, DebugColor.GREEN);
            addDebugPoint(context, ends1[0].origin, DebugColor.GREEN);
            addDebugPoint(context, ends0[1].origin, DebugColor.RED);
            addDebugPoint(context, ends1[1].origin, DebugColor.RED);
        }

        if (definition.showGroups)
        {
            addDebugEntities(context, qUnion(path0.edges), DebugColor.CYAN);
            addDebugEntities(context, qUnion(path1.edges), DebugColor.MAGENTA);
        }

        // ---- 4. One fit: the user's degree; chord-length parameters (open) or periodic (closed, equal factors) ----
        const retCurve = scFit(context, positions, definition.scaledDegree, definition.scaledTol, periodic);

        opCreateBSplineCurve(context, id + "createScaledBsplineCurve", {
                    "bSplineCurve" : retCurve
                });

        const splineQ = qCreatedBy(id + "createScaledBsplineCurve", EntityType.BODY);
        if (length(definition.curveName) > 0)
        {
            setProperty(context, {
                        "entities" : splineQ,
                        "propertyType" : PropertyType.NAME,
                        "value" : definition.curveName
                    });
        }

        // ---- 5. Deviation between samples: the created edge against the exact blend at the midpoints ----
        const edgeQ = qCreatedBy(id + "createScaledBsplineCurve", EntityType.EDGE);
        var deviation = 0 * meter;
        for (var p in midPositions)
        {
            deviation = max(deviation, evDistance(context, { "side0" : edgeQ, "side1" : p }).distance);
        }
        if (deviation > 2 * definition.scaledTol)
        {
            notes = append(notes, "Between samples the curve is up to " ~ scMm(deviation) ~ " from the exact blend (" ~ n ~ " samples, fit tolerance "
                    ~ scMm(definition.scaledTol) ~ "); reduce Sample spacing for a closer curve.");
        }

        if (definition.printBsplines)
        {
            println("---------------- SCALED CURVE ----------------");
            println("Group 0: " ~ size(path0.edges) ~ " edge(s), " ~ scMm(length0) ~ (path0.closed ? ", closed" : "") ~ "; Group 1: " ~ size(path1.edges)
                    ~ " edge(s), " ~ scMm(length1) ~ (path1.closed ? ", closed" : ""));
            println("samples " ~ n ~ ", Group 0 reversed " ~ pairing.reversed ~ ", seam shift " ~ pairing.shift ~ ", periodic " ~ periodic
                    ~ ", max deviation between samples " ~ scMm(deviation));
            printBSpline(retCurve, definition.bsplineFormat, [" - - - - - - Scaled BSplineCurve - - - - - - "]);
            println("---------------- / SCALED CURVE ----------------");
        }

        if (size(notes) > 0)
        {
            reportFeatureInfo(context, id, join(notes, " "));
        }
    }, { "flipPairing" : false, "sampleSpacing" : 5 * millimeter, "curveName" : "", "curveOnSurface" : false, "projectionFace" : qNothing(),
            "showEndpoints" : false, "showGroups" : false, "printBsplines" : false });

// ============================================================================
// Paths and pairing
// ============================================================================

/**
 * The edges of a selection as one path (edges in any order; open or closed). A selection that is not one
 * continuous chain fails with std's "Edges do not form a continuous path".
 * Coincident duplicates are dropped first: a closed sketch profile has both a wire edge and the region's boundary
 * edge on the same geometry, and with both the path runs round the loop twice (a self-overlapping periodic fit,
 * refused by opCreateBSplineCurve as BAD_GEOMETRY -- SC5, 2026-09-26).
 */
export function scPath(context is Context, selection is Query, parameterId is string) returns Path
{
    const edges = qEntityFilter(selection, EntityType.EDGE);
    if (isQueryEmpty(context, edges))
    {
        throw regenError("Select the edges of " ~ (parameterId == "group0" ? "Group 0." : "Group 1."), [parameterId]);
    }
    var kept = [];
    var keptLengths = [];
    for (var edge in evaluateQuery(context, edges))
    {
        const edgeLength = evLength(context, { "entities" : edge });
        const probes = evEdgeTangentLines(context, { "edge" : edge, "parameters" : [0.25, 0.75] });
        var duplicate = false;
        for (var j = 0; j < size(kept); j += 1)
        {
            if (abs(edgeLength - keptLengths[j]) < SC_PATH_TOLERANCE
                && evDistance(context, { "side0" : kept[j], "side1" : probes[0].origin }).distance < SC_PATH_TOLERANCE
                && evDistance(context, { "side0" : kept[j], "side1" : probes[1].origin }).distance < SC_PATH_TOLERANCE)
            {
                duplicate = true;
                break;
            }
        }
        if (!duplicate)
        {
            kept = append(kept, edge);
            keptLengths = append(keptLengths, edgeLength);
        }
    }
    return constructPath(context, qUnion(kept), { "tolerance" : SC_PATH_TOLERANCE }).path;
}

/**
 * Arc-length fraction along a path of the path point nearest `point`.
 */
export function scPathFraction(context is Context, path is Path, point is Vector) returns number
{
    const total = evPathLength(context, path);
    const nearest = evDistance(context, { "side0" : qUnion(path.edges), "side1" : point }).sides[0];
    var start = 0;
    for (var i = 0; i < nearest.index; i += 1)
    {
        start += evLength(context, { "entities" : path.edges[i] }) / total;
    }
    const edgeFraction = evLength(context, { "entities" : path.edges[nearest.index] }) / total;
    const t = path.flipped[nearest.index] ? 1 - nearest.parameter : nearest.parameter;
    return start + edgeFraction * t;
}

/**
 * How Group 0 is paired with Group 1 (which sets the direction): Group 0's fraction at Group 1 fraction f is
 * reversed ? 1 - f : f (open) or wrap(shift +- f) (closed; shift = Group 0's point nearest Group 1's start).
 * Open: reversed when the crossed end pairing is shorter. Closed: reversed when the two loops run in opposite
 * senses (vector areas; the start tangents when a loop has no area). `flip` reverses the automatic choice.
 *
 * @returns {{ @field reversed {boolean}, @field shift {number}, @field closed {boolean} }}
 */
export function scPairing(context is Context, path0 is Path, path1 is Path, flip is boolean) returns map
{
    if (!path1.closed)
    {
        const ends0 = evPathTangentLines(context, path0, [0, 1]).tangentLines;
        const ends1 = evPathTangentLines(context, path1, [0, 1]).tangentLines;
        const straight = norm(ends0[0].origin - ends1[0].origin) + norm(ends0[1].origin - ends1[1].origin);
        const crossed = norm(ends0[0].origin - ends1[1].origin) + norm(ends0[1].origin - ends1[0].origin);
        return { "reversed" : (crossed < straight) != flip, "shift" : 0, "closed" : false };
    }

    const start1 = evPathTangentLines(context, path1, [0]).tangentLines[0];
    const shift = scPathFraction(context, path0, start1.origin);
    var fractions = [];
    for (var i = 0; i < 64; i += 1)
    {
        fractions = append(fractions, i / 64);
    }
    const area0 = scVectorArea(evPathTangentLines(context, path0, fractions).tangentLines);
    const area1 = scVectorArea(evPathTangentLines(context, path1, fractions).tangentLines);
    const size0 = evPathLength(context, path0);
    const size1 = evPathLength(context, path1);
    var reversed;
    if (norm(area0) > 1e-6 * size0 * size0 && norm(area1) > 1e-6 * size1 * size1)
    {
        reversed = dot(area0, area1) < 0 * meter * meter * meter * meter;
    }
    else
    {
        const seam0 = evPathTangentLines(context, path0, [shift]).tangentLines[0];
        reversed = dot(seam0.direction, start1.direction) < 0;
    }
    return { "reversed" : reversed != flip, "shift" : shift, "closed" : true };
}

/** Vector area of a closed polygon (half the sum of p_i x p_i+1, taken about the first point). */
export function scVectorArea(lines is array) returns Vector
{
    const origin = lines[0].origin;
    var area = vector(0, 0, 0) * meter * meter;
    for (var i = 0; i < size(lines); i += 1)
    {
        const a = lines[i].origin - origin;
        const b = lines[(i + 1) % size(lines)].origin - origin;
        area = area + cross(a, b) / 2;
    }
    return area;
}

/** Group 0's path fraction paired with Group 1's fraction f. */
export function scFraction0(pairing is map, f is number) returns number
{
    if (!pairing.closed)
    {
        return pairing.reversed ? 1 - f : f;
    }
    const x = pairing.reversed ? pairing.shift - f : pairing.shift + f;
    return x - floor(x);
}

/**
 * The blend (1 - sf) * Group0 + sf * Group1 at Group 1 fractions `fractions`, sf by the transition from sfStart to sfEnd.
 */
export function scBlendAt(context is Context, path0 is Path, path1 is Path, pairing is map, fractions is array,
    sfStart is number, sfEnd is number, transitionType is TransitionType) returns array
{
    var fractions0 = [];
    for (var f in fractions)
    {
        fractions0 = append(fractions0, scFraction0(pairing, f));
    }
    const lines0 = evPathTangentLines(context, path0, fractions0).tangentLines;
    const lines1 = evPathTangentLines(context, path1, fractions).tangentLines;
    var out = [];
    for (var i = 0; i < size(fractions); i += 1)
    {
        const sf = computeAppliedSF(fractions[i], sfStart, sfEnd, transitionType);
        out = append(out, (1 - sf) * lines0[i].origin + sf * lines1[i].origin);
    }
    return out;
}

/** Each point moved to the nearest point of the face. */
export function scProjectPoints(context is Context, points is array, face is Query) returns array
{
    var out = [];
    for (var p in points)
    {
        out = append(out, evDistance(context, { "side0" : face, "side1" : p }).sides[0].point);
    }
    return out;
}

/**
 * One approximateSpline fit. Open: chord-length parameters (so the fitter keeps the derivative sizes, correction 23),
 * both ends interpolated; points closer than 1e-6 of the chord to the previous one are dropped. Periodic: std's own
 * form (positions from fraction 0 to 1 inclusive, no parameters), as Edit curve does for a closed path.
 */
export function scFit(context is Context, positions is array, degree is number, tolerance is ValueWithUnits, periodic is boolean) returns BSplineCurve
{
    if (periodic)
    {
        return approximateSpline(context, {
                        "degree" : degree,
                        "tolerance" : tolerance,
                        "isPeriodic" : true,
                        "targets" : [approximationTarget({ "positions" : positions })],
                        "suppressInterpolationNotice" : true
                    })[0];
    }
    var total = 0 * meter;
    for (var i = 1; i < size(positions); i += 1)
    {
        total += norm(positions[i] - positions[i - 1]);
    }
    if (total < TOLERANCE.zeroLength * meter)
    {
        throw regenError("The scaled curve has zero length.", ["group0", "group1"]);
    }
    var kept = [positions[0]];
    var chord = [0 * meter];
    for (var i = 1; i < size(positions); i += 1)
    {
        const step = norm(positions[i] - kept[size(kept) - 1]);
        if (step > 1e-6 * total)
        {
            kept = append(kept, positions[i]);
            chord = append(chord, chord[size(chord) - 1] + step);
        }
        else if (i == size(positions) - 1)
        {
            // Keep the true end: it replaces the last kept point.
            kept[size(kept) - 1] = positions[i];
        }
    }
    if (size(kept) < 2)
    {
        throw regenError("The scaled curve has zero length.", ["group0", "group1"]);
    }
    var params = [];
    for (var s in chord)
    {
        params = append(params, s / chord[size(chord) - 1]);
    }
    return approximateSpline(context, {
                    "degree" : degree,
                    "tolerance" : tolerance,
                    "isPeriodic" : false,
                    "targets" : [approximationTarget({ "positions" : kept })],
                    "parameters" : params,
                    "interpolateIndices" : [0, size(kept) - 1],
                    "suppressInterpolationNotice" : true
                })[0];
}

/** A length as "x.xxxx mm". */
export function scMm(value is ValueWithUnits) returns string
{
    return toString(roundToPrecision(value / millimeter, 4)) ~ " mm";
}

// ============================================================================
// Legacy API (B-spline in, B-spline out). The feature above no longer uses these.
// ============================================================================

/**
 * Project a BSplineCurve onto a face by finding the closest point on the face
 * for each sampled position. For smooth faces this is equivalent to projecting
 * along the face normal (orthogonal projection).
 *
 * @param context {Context}
 * @param curve {BSplineCurve} : Curve to project (clamped, domain [0, 1])
 * @param face {Query} : Target face
 * @param numSamples {number} : Sample count along the curve for re-fitting
 * @param tolerance {ValueWithUnits} : Fitting tolerance for approximateSpline
 * @returns {BSplineCurve} : Projected curve lying on the face
 */
export function projectCurveOnSurface(context is Context, curve is BSplineCurve,
                                       face is Query, numSamples is number,
                                       tolerance is ValueWithUnits) returns BSplineCurve
{
    var projectedPoints = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var t = i / (numSamples - 1);
        var pt = evaluateSpline({ "spline" : curve, "parameters" : [t] })[0][0];
        var distResult = evDistance(context, { "side0" : face, "side1" : pt });
        projectedPoints = append(projectedPoints, distResult.sides[0].point);
    }

    return approximateSpline(context, {
        "degree" : curve.degree,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : projectedPoints })],
        "interpolateIndices" : [0, numSamples - 1]
    })[0];
}

/**
 * Create a blended curve between two B-splines with a variable scale factor, pairing them by B-SPLINE PARAMETER
 * (the feature pairs by arc length instead).
 *
 * @param curve0 {BSplineCurve} : First boundary curve
 * @param curve1 {BSplineCurve} : Second boundary curve
 * @param flip {boolean} : If true, reverse parameterization of curve0
 * @param sf_0 {number} : Scale factor at S=0 (0 = all curve0, 1 = all curve1)
 * @param sf_1 {number} : Scale factor at S=1
 * @param transition {TransitionType} : How scale factor changes
 * @param numSamples {number} : Number of sample points for fitting
 * @param degree {number} : Output degree
 * @param tolerance {ValueWithUnits} : Fitting tolerance for approximateSpline
 */
export function scaledCurve(context is Context, curve0 is BSplineCurve, curve1 is BSplineCurve, flip is boolean, sf_0 is number, sf_1 is number, transition is TransitionType, numSamples is number, degree is number, tolerance is ValueWithUnits) returns BSplineCurve
{
    var blendedPoints = [];
    var params = [];
    for (var i = 0; i < numSamples; i += 1)
    {
        var S = i / (numSamples - 1);
        var S0 = flip ? (1 - S) : S;
        var pt0 = evaluateSpline({ "spline" : curve0, "parameters" : [S0] })[0][0];
        var pt1 = evaluateSpline({ "spline" : curve1, "parameters" : [S] })[0][0];
        var sf = computeAppliedSF(S, sf_0, sf_1, transition);
        blendedPoints = append(blendedPoints, (1 - sf) * pt0 + sf * pt1);
        params = append(params, S);
    }

    return approximateSpline(context, {
        "degree" : degree,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : blendedPoints })],
        "parameters" : params,
        "interpolateIndices" : [0, numSamples - 1]
    })[0];
}
