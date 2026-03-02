FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import tools/arc_length
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/f88f68e9ff3cb3c30d4afffe", version : "561709ffbf7a138328bbffc4");


/**
 * Project a BSplineCurve onto a face by finding the closest point on the face
 * for each sampled position. For smooth faces this is equivalent to projecting
 * along the face normal (orthogonal projection).
 *
 * @param context {Context}
 * @param curve {BSplineCurve} : Curve to project
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
 * Join an array of curve segments into a single BSplineCurve.
 * Uses arc-length proportional sampling.
 *
 * @param context {Context}
 * @param segments {array} : Unordered array of BSplineCurve
 * @param totalSamples {number} : Total sample points across all segments
 * @param tolerance {ValueWithUnits} : Fitting tolerance
 */
export function joinCurveSegments(
    context is Context,
    segments is array,
    totalSamples is number,
    tolerance is ValueWithUnits
) returns BSplineCurve
{
    if (size(segments) == 0)
    {
        throw regenError("No segments provided");
    }

    if (size(segments) == 1)
    {
        return segments[0];
    }

    // Order the segments first
    var ordering = orderCurveSegments(context, segments, tolerance);
    var orderedSegs = ordering.ordered;
    var flips = ordering.flips;

    // Compute arc length of each segment
    const arcLengthSamples = 20;  // For approximation
    var lengths = [];
    var totalLength = 0 * meter;

    for (var seg in orderedSegs)
    {
        var len = computeArcLength(seg, {numIntervals: arcLengthSamples});
        lengths = append(lengths, len);
        totalLength += len;
    }

    // Allocate samples per segment proportionally
    var samplesPerSeg = [];
    var allocatedSamples = 0;

    for (var i = 0; i < size(orderedSegs); i += 1)
    {
        var fraction = lengths[i] / totalLength;
        var samples = round(fraction * totalSamples);

        // Ensure at least 2 samples per segment
        samples = max(samples, 2);

        samplesPerSeg = append(samplesPerSeg, samples);
        allocatedSamples += samples;
    }

    // Adjust last segment to hit exact total (account for rounding)
    var adjustment = totalSamples - allocatedSamples;
    samplesPerSeg[size(samplesPerSeg) - 1] = samplesPerSeg[size(samplesPerSeg) - 1] + adjustment;

    // Sample all segments, collecting points
    var allPoints = [];

    for (var segIdx = 0; segIdx < size(orderedSegs); segIdx += 1)
    {
        var segment = orderedSegs[segIdx];
        var flip = flips[segIdx];
        var numSamples = samplesPerSeg[segIdx];

        // Skip start point for subsequent segments (avoids duplicate)
        var startI = (segIdx == 0) ? 0 : 1;

        for (var i = startI; i < numSamples; i += 1)
        {
            var S = i / (numSamples - 1);
            var evalS = flip ? (1 - S) : S;

            var pt = evaluateSpline({
                "spline" : segment,
                "parameters" : [evalS]
            })[0][0];

            allPoints = append(allPoints, pt);
        }
    }

    // Create parameters proportional to cumulative arc length
    var params = [];
    var cumulativeLength = 0 * meter;
    var prevPt = allPoints[0];
    params = append(params, 0);

    for (var i = 1; i < size(allPoints); i += 1)
    {
        cumulativeLength += norm(allPoints[i] - prevPt);
        params = append(params, cumulativeLength / totalLength);  // Normalized to [0, 1]
        prevPt = allPoints[i];
    }

    // Force last param to exactly 1 (avoid floating point drift)
    params[size(params) - 1] = 1;

    // Fit single curve through all points
    var result = approximateSpline(context, {
        "degree" : 3,
        "tolerance" : tolerance,
        "isPeriodic" : false,
        "targets" : [approximationTarget({ "positions" : allPoints })],
        "parameters" : params,
        "interpolateIndices" : [0, size(allPoints) - 1]
    });

    return result[0];
}

/**
 * Order an array of curve segments into a connected path.
 * Returns ordered segments and flip flags.
 *
 * @param context {Context}
 * @param segments {array} : Unordered array of BSplineCurve
 * @param tolerance {ValueWithUnits} : Tolerance for endpoint matching
 * @returns {map} : { "ordered" : array, "flips" : array } or throws error
 */
export function orderCurveSegments(context is Context, segments is array, tolerance is ValueWithUnits) returns map
{
    if (size(segments) == 0)
    {
        throw regenError("No segments provided");
    }

    if (size(segments) == 1)
    {
        return { "ordered" : segments, "flips" : [false] };
    }

    // Get start and end points for each segment
    var endpoints = [];
    for (var i = 0; i < size(segments); i += 1)
    {
        var startPt = evaluateSpline({ "spline" : segments[i], "parameters" : [0] })[0][0];
        var endPt = evaluateSpline({ "spline" : segments[i], "parameters" : [1] })[0][0];
        endpoints = append(endpoints, { "start" : startPt, "end" : endPt });
    }

    // Track which segments are used
    var used = makeArray(size(segments), false);
    var ordered = [];
    var flips = [];

    // Start with segment 0, determine if it needs flipping later
    var currentIdx = 0;
    var currentFlip = false;

    // First, find a segment that's an endpoint of the chain (only one connection)
    // This ensures we start at a true endpoint, not the middle
    for (var i = 0; i < size(segments); i += 1)
    {
        var connections = countConnections(endpoints, i, tolerance);
        if (connections == 1)
        {
            currentIdx = i;
            break;
        }
    }

    // Determine if first segment needs flipping
    // (its "start" should be the unconnected end)
    var firstStart = endpoints[currentIdx].start;
    var hasConnectionAtStart = false;
    for (var i = 0; i < size(segments); i += 1)
    {
        if (i == currentIdx) continue;
        if (tolerantEquals(firstStart, endpoints[i].start) ||
            tolerantEquals(firstStart, endpoints[i].end))
        {
            hasConnectionAtStart = true;
            break;
        }
    }
    currentFlip = hasConnectionAtStart;  // Flip if start is connected (we want start to be free end)

    // Build the chain
    while (size(ordered) < size(segments))
    {
        ordered = append(ordered, segments[currentIdx]);
        flips = append(flips, currentFlip);
        used[currentIdx] = true;

        // Current endpoint we're continuing from
        var currentEnd = currentFlip ? endpoints[currentIdx].start : endpoints[currentIdx].end;

        // Find next segment
        var foundNext = false;
        for (var i = 0; i < size(segments); i += 1)
        {
            if (used[i]) continue;

            if (tolerantEquals(currentEnd, endpoints[i].start))
            {
                currentIdx = i;
                currentFlip = false;
                foundNext = true;
                break;
            }
            else if (tolerantEquals(currentEnd, endpoints[i].end))
            {
                currentIdx = i;
                currentFlip = true;
                foundNext = true;
                break;
            }
        }

        if (!foundNext && size(ordered) < size(segments))
        {
            throw regenError("Segments do not form a continuous path. " ~
                size(ordered) ~ " of " ~ size(segments) ~ " segments connected.");
        }
    }

    return { "ordered" : ordered, "flips" : flips };
}


/**
 * Count how many other segments connect to segment at index.
 */
export function countConnections(endpoints is array, index is number, tolerance is ValueWithUnits) returns number
{
    var count = 0;
    var myStart = endpoints[index].start;
    var myEnd = endpoints[index].end;

    for (var i = 0; i < size(endpoints); i += 1)
    {
        if (i == index) continue;

        if (tolerantEquals(myStart, endpoints[i].start) ||
            tolerantEquals(myStart, endpoints[i].end))
        {
            count += 1;
        }
        if (tolerantEquals(myEnd, endpoints[i].start) ||
            tolerantEquals(myEnd, endpoints[i].end))
        {
            count += 1;
        }
    }

    return count;
}
