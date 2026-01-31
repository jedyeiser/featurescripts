FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");
import(path : "onshape/std/transform.fs", version : "2856.0");

import(path : "b4b27eddd41251b5f56f042b", version : "a477172449057a1235bdb0f8");

export enum FootprintCurveBuildMode
{
    annotation { "Name" : "PER_REGION" }
    ONE_PER_REGION,

    annotation { "Name" : "PER_EDGE" }
    ONE_PER_EDGE
}

export enum AngleDriver
{
    annotation { "Name" : "Waist location" }
    WAIST,

    annotation { "Name" : "Overall taper angle" }
    TAPER_ANGLE
}

export enum FootprintSplineExportType
{
    annotation { "Name" : "FIT" }
    FIT,

    annotation { "Name" : "APPROX" }
    APPROX
}

export enum RadiusSign
{
    POS,
    NEG
}


export function generateFootprintFromRadiusEdges(context is Context, id is Id, radiusEdgesQuery is Query, mode is FootprintCurveBuildMode, samplingDef is map, integrationDef is map, splineDef
is map) returns array
{
    if (isQueryEmpty(context, radiusEdgesQuery))
        throw regenError("No radius profile edges selected.");

    // 1) Sample edges
    var edgeData = sampleRadiusEdges(context, radiusEdgesQuery, samplingDef);

    var orderedEdgeMap = divideEdgeDataIntoRegions(context, edgeData);
    var makeRegions = (mode == FootprintCurveBuildMode.ONE_PER_EDGE) ? orderedEdgeMap['edgeBreaks']: orderedEdgeMap['regionBreaks'];

    // 4) Solve footprint globally
    var solved = solveFootprintConstraints(makeRegions, integrationDef);

    var resultMaps = [];
    for (var i = 0; i < size(solved.splineSections); i += 1)
    {
        var section = solved.splineSections[i];
        var pts = [];
        //println('keys(section) -> ' ~ keys(section));
        //println('size(section.x) -> ' ~ size(section.x));
       // println('size(section.y) -> ' ~ size(section.y));
        for (var p = 0; p < size(section.x); p += 1)
        {
            pts = append(pts, vector(section.x[p], section.y[p], 0 * millimeter));
        }
        resultMaps = append(resultMaps, createFootprintSplineFromPoints(context, id + ('footprintSpline'~i), pts, splineDef));
    }

    return resultMaps;
}

/* =========================
 * Sampling
 * ========================= */
 /**
  * Take an array of edges - with additional data after being processed by sampleRadiusEdges, and divide them into seperate arrays based on 'radius sign'.
  * @param edgeArray: Array of maps containing edge data. Keys: start, end, stdDir, startTangent, endTangent, radiusSign, edgeQuery, edgeOrder, startRange, endRange, startGap, endGap, xPoints,
  radiusPoints
  *
  */
 export function divideEdgeDataIntoRegions(context is Context, edgeArray is array) returns map
 {
     var regionBreaks = [];
     var edgeBreaks = [];
     var regionR = [];
     var regionX = [];

     var prevSign = undefined;

     for (var i = 0; i < size(edgeArray); i += 1)
     {
        if (prevSign == undefined) // first edge
        {
            regionX = concatenateArrays(regionX, edgeArray[i]['xPoints']);
            regionR = concatenateArrays(regionR, edgeArray[i]['radiusPoints']);
            edgeBreaks = append(edgeBreaks, {'xPoints': edgeArray[i]['xPoints'], 'radiusPoints': edgeArray[i]['radiusPoints'], 'edgeOrder': edgeArray[i]['edgeOrder'], 'edgeRadiusSign':
            edgeArray[i].radiusSign});
            prevSign = edgeArray[i].radiusSign;
        }
        else //We have a previous Sign.
        {
            var curSign = edgeArray[i].radiusSign;
            edgeBreaks = append(edgeBreaks, {'xPoints': edgeArray[i]['xPoints'], 'radiusPoints': edgeArray[i]['radiusPoints'], 'edgeOrder': edgeArray[i]['edgeOrder'], 'edgeRadiusSign':
            edgeArray[i].radiusSign}); // break edges per normal

            if (curSign == prevSign) //Sign is unchanged. Append edge data to running region totals.
            {
                // sign has flipped. Dump previous arrays into region arrays.
                regionX = concatenateArrays(regionX, edgeArray[i]['xPoints']);
                regionR = concatenateArrays(regionR, edgeArray[i]['radiusPoints']);
                prevSign = edgeArray[i].radiusSign;
            }

            else // curSign != prevSign
            {
                regionBreaks = append(regionBreaks, {'regionNum': size(regionBreaks), 'xPoints': regionX, 'radiusPoints': regionR, 'regionSign': prevSign});
                //add this edge's data to cleaned regionR and regionX arrays.
                regionX = edgeArray[i]['xPoints'];
                regionR = edgeArray[i]['radiusPoints'];
                // update sign
                prevSign = edgeArray[i].radiusSign;

            }
        }
        if (i == size(edgeArray)-1) // last edge
        {
            regionBreaks = append(regionBreaks, {'regionNum': size(regionBreaks), 'xPoints': regionX, 'radiusPoints': regionR, 'regionSign': prevSign});
        }
     }

     return {'regionBreaks': regionBreaks, 'edgeBreaks': edgeBreaks};
 }

/**
 * Take a collection of edges and a sampling definition. Return a 'corrected' map containing x, R, order, edgeQuery, region, edgeNum data at even/correct intervals.
 *
 * @param samplingDef : Map containing keys numSamplesPerEdge
 *
 */

/**
 * Take a collection of edges and a sampling definition. Return a 'corrected' map containing x, R, order, edgeQuery, region, edgeNum data at even/correct
 intervals.
 *
 * @param samplingDef : Map containing keys numSamplesPerEdge
 *
 */

export function sampleRadiusEdges(context is Context, q is Query, samplingDef is map) returns array
{
    // Step 0 - setup analysis.
    var edgeData = [];
    var edgeArray = evaluateQuery(context, qUnion([q]));

    for (var i = 0; i < size(edgeArray); i += 1)
    {
        var endLines = evEdgeTangentLines(context, {
                "edge" : edgeArray[i],
                "parameters" : [0, 1]
        });

        var stdDir = endLines[0].origin[0] < endLines[1].origin[0]; // is the x value of the param0 point less than the x value of the param1 point?

        var startX = -3 * meter;
        var endX = 3 * meter;
        var startTangent = vector(1, 1, 1);
        var endTangent = vector(1, 1, 1);
        var deltaVector = vector(1, 1, 1) * meter;

        if (stdDir)
        {
            startX = endLines[0].origin[0];
            endX = endLines[1].origin[0];
            startTangent = endLines[0].direction;
            endTangent = endLines[1].direction;
            deltaVector = endLines[0].origin - endLines[1].origin;
        }
        else
        {
            startX = endLines[1].origin[0];
            endX = endLines[0].origin[0];
            startTangent = endLines[1].direction;
            endTangent = endLines[0].direction;
            deltaVector = endLines[1].origin - endLines[0].origin;
        }

        if (dot(deltaVector, startTangent) < 0)
        {
            startTangent *= -1;
        }
        if (dot(deltaVector, endTangent) < 0)
        {
            endTangent *= -1;
        }

        var radiusRegion = RadiusSign.POS;
        if (endLines[0].origin[1] < 0 * millimeter && endLines[1].origin[1] < 0 * millimeter)
        {
            radiusRegion = RadiusSign.NEG;
        }
        if ((endLines[0].origin[1] < 0 * millimeter && endLines[1].origin[1] > 0 * millimeter) ||
            (endLines[0].origin[1] > 0 * millimeter && endLines[1].origin[1] < 0 * millimeter))
        {
            throw regenError("Edges defining radius progression cannot cross y = 0");
        }

        edgeData = append(edgeData, {
            'edgeNum' : i,
            'start' : startX,
            'end' : endX,
            'startTangent' : startTangent,
            'endTangent' : endTangent,
            'radiusSign' : radiusRegion,
            'edgeQuery' : edgeArray[i]
        });
    }

    edgeData = sort(edgeData, function(a, b) { return a.start - b.start; }); // now ordered smallest to largest.

    // Compute global X bounds (true data extent) - used to prevent extrapolation at endpoints
    var globalXMin = edgeData[0].start;
    var globalXMax = edgeData[size(edgeData) - 1].end;
    var xTol = 1e-9 * meter;

    // Step 1 - traverse edges and find any gaps.
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var thisEdge = edgeData[i];
        var otherEdges = removeElementAt(edgeData, i);

        var overlapEdges = filter(otherEdges, function(x) {
            return (abs(x.start - thisEdge.end) < 0.0001 * millimeter) && abs(x.end - thisEdge.end) < 0.0001 * millimeter;
        });

        if (size(overlapEdges) > 1)
        {
            println(' thisEdge: [' ~ toString(thisEdge.start) ~ ' ---> ' ~ toString(thisEdge.end) ~ ']');
            for (var e = 0; e < size(overlapEdges); e += 1)
            {
                println('   overlapEdge: [' ~ toString(overlapEdges[e].start) ~ ' ---> ' ~ toString(overlapEdges[e].end) ~ ']');
            }
            throw regenError("Radius profiles cannot overlap in X");
        }

        var prevEdge = i >= 1 ? edgeData[i - 1] : undefined;
        edgeData[i]['edgeOrder'] = i;

        if (prevEdge != undefined)
        {
            if (abs(thisEdge.start - prevEdge.end) >= 0.0001 * millimeter) // x endpoints don't match - gap exists
            {
                var gap = thisEdge.start - prevEdge.end;
                edgeData[i]['startRange'] = edgeData[i].start - gap / 2;
                edgeData[i - 1]['endRange'] = edgeData[i - 1].end + gap / 2;
                edgeData[i]['startGap'] = true;
                edgeData[i - 1]['endGap'] = true;

                if (i == size(edgeData) - 1)
                {
                    edgeData[i]['endRange'] = edgeData[i].end;
                    edgeData[i]['endGap'] = false;
                }
            }
            else // edges are continuous
            {
                edgeData[i]['startRange'] = edgeData[i].start;
                edgeData[i - 1]['endRange'] = edgeData[i - 1].end;
                edgeData[i]['startGap'] = false;
                edgeData[i - 1]['endGap'] = false;

                if (i == size(edgeData) - 1)
                {
                    edgeData[i]['endRange'] = edgeData[i].end;
                    edgeData[i]['endGap'] = false;
                }
            }
        }
        else // no previous edge (first edge)
        {
            edgeData[i]['startRange'] = edgeData[i].start;
            edgeData[i]['startGap'] = false;
            if (i == size(edgeData) - 1) // only one edge provided
            {
                edgeData[i]['endRange'] = edgeData[i].end;
                edgeData[i]['endGap'] = false;
            }
        }
    }

    // Step 2 - Iterate over edges to sample radius points.
    // Use tangent-based extrapolation for interior gaps, but never at global endpoints.
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var radiusPoints = [];
        var xPoints = [];

        var xRange = range(edgeData[i].startRange, edgeData[i].endRange, samplingDef.numSamplesPerEdge);

        for (var p = 0; p < size(xRange); p += 1)
        {
            var xVal = xRange[p];
            var xPlane = plane(vector(xVal, 0 * millimeter, 0 * millimeter), vector(1, 0, 0));
            var pointDist = evDistance(context, {
                    "side0" : edgeData[i]['edgeQuery'],
                    "side1" : xPlane
            });

            xPoints = append(xPoints, xVal);

            if (pointDist.distance > 0 * millimeter) // edge does not intersect the plane
            {
                // Check if this is a global endpoint - never extrapolate beyond true data bounds
                var isGlobalMin = (xVal <= globalXMin + xTol);
                var isGlobalMax = (xVal >= globalXMax - xTol);

                if (isGlobalMin || isGlobalMax)
                {
                    // Use the closest point on the edge directly - no extrapolation
                    radiusPoints = append(radiusPoints, pointDist.sides[0].point[1].value);
                }
                else if (xVal < edgeData[i].start) // interior gap, before this edge
                {
                    // Extrapolate using proper derivative: dR/dx = tangent[1] / tangent[0]
                    var dx = xVal - edgeData[i].start; // negative
                    var dRdx = edgeData[i].startTangent[1] / edgeData[i].startTangent[0];
                    radiusPoints = append(radiusPoints, (pointDist.sides[0].point[1] + dx * dRdx).value);
                }
                else // interior gap, after this edge
                {
                    var dx = xVal - edgeData[i].end; // positive
                    var dRdx = edgeData[i].endTangent[1] / edgeData[i].endTangent[0];
                    radiusPoints = append(radiusPoints, (pointDist.sides[0].point[1] + dx * dRdx).value);
                }
            }
            else // plane intersects edge - use intersection point directly
            {
                radiusPoints = append(radiusPoints, pointDist.sides[0].point[1].value);
            }
        }

        edgeData[i]['radiusPoints'] = radiusPoints;
        edgeData[i]['xPoints'] = xPoints;
    }

    return edgeData;
}



/* =========================
 * Spline creation
 * ========================= */

export function createFootprintSplineFromPoints(context is Context, id is Id, pts is array, splineDef is map) returns map
{

    var aprx = approximateSpline(context, {
        "degree" : splineDef.targetDegree,
        "tolerance" : splineDef.tolerance,
        "maxControlPoints" : splineDef.maxCPs,
        "targets" : [approximationTarget({ "positions" : pts })],
        "interpolateIndices" : [0, size(pts)-1]
    })[0];

    return {'bSpline': aprx, 'points': pts};
}

export function solveFootprintConstraints(samples is array, integrationDef is map) returns map
{
    if (size(samples) < 2)
        throw regenError("solveFootprintConstraints: need at least 2 samples.");

    //samples has keys xPoints, radiusPoints, edgeOrder, edgeRadiusSign, edgeQuery, region num, etc

    // --- inputs ---
    var waistHalf = integrationDef.waistWidth * 0.5;
    var cScale = (integrationDef.curvatureScaleFactor == undefined) ? 1 : integrationDef.curvatureScaleFactor;
    var maxIter = (integrationDef.maxIter == undefined) ? 20 : integrationDef.maxIter;

    // default tolerances by driver (can be overridden by integrationDef.solveTol)
    var tol = integrationDef.solveTol;
    if (tol == undefined)
    {
        tol = (integrationDef.angleDriver == AngleDriver.TAPER_ANGLE) ? (1e-5 * degree) : (0.001 * millimeter);
    }

    // --- base integrals: x[] ---

    var base = buildBaseIntegrals(samples, cScale);

    // Solve theta0 to hit target
    var theta0 = solveTheta0ForDriver(base.integral, integrationDef, tol, maxIter);

    // Choose y0 to enforce min(y)=waistHalf
    var yBase = evalY(base.integral, theta0, 0 * meter); // y0=0
    var minY = min(yBase);
    var y0 = waistHalf - minY;

    // Final assembled y and points
    var yFinal = evalY(base.integral, theta0, y0);
    var pts = [];
    for (var i = 0; i < size(base.integral.x); i += 1)
    {
        pts = append(pts, vector(base.integral.x[i], yFinal[i], 0 * meter));
    }

    var splineSections = [];

    for (var i = 0; i < size(base.sections); i += 1)
    {
        var section = base.sections[i].baseIntegral;
        var thetaSect = evalTheta(section, theta0);
        var ySect = evalY(section, theta0, y0);
        splineSections = append(splineSections, {'x': section.x, 'y': ySect, 'y0': y0, 'theta0': theta0});

        //y0 = last(ySect);
        //theta0 = last(thetaSect);

    }

    var stats = footprintStatsFromDiscrete(base.integral.x, yFinal, evalTheta(base.integral, theta0));

    return {
        "theta0" : theta0,
        "y0" : y0,
        "points" : pts,
        "x" : base.integral.x,
        "y" : yFinal,
        "stats" : stats,
        "splineSections": splineSections
    };
}

export function buildBaseIntegrals(samples is array, cScale is number) returns map
{
    var sections = samples;
    var runningTotal = { "x" : [], "k" : [], "yP" : [], "y" : [] };
    var retArray = [];
    //println('we have ' ~ size(samples) ~ " sections");
    for (var s = 0; s < size(samples); s += 1)
    {
        var x = samples[s].xPoints;
        //println('Section # ' ~ s ~ " has " ~ size(samples[s].xPoints) ~ " points");

        var k = [];
        for (var i = 0; i < size(samples[s].xPoints); i += 1)
        {
            //println('point ' ~ i);
            //println(samples[s].radiusPoints[i]);
            var R = samples[s].radiusPoints[i] * cScale;
            //println("R -> " ~ R);
            if (abs(R) < 1e-12)
                throw regenError("Radius too close to zero at x=" ~ toString(samples[i].x));
            k = append(k, 1 / R);
        }

        var yP = cumTrapz(x, mapArray(k, function(r) {return r/meter;}), size(runningTotal['yP']) > 0 ? last(runningTotal['yP']) : 0 ).cumulative;
        var y = cumTrapz(x, yP, size(runningTotal['y']) > 0 ? last(runningTotal['y']) : 0 * millimeter ).cumulative;

        runningTotal['x'] = concatenateArrays(runningTotal['x'], x);
        runningTotal['k'] = concatenateArrays(runningTotal['k'], k);
        runningTotal['yP'] = concatenateArrays(runningTotal['yP'], yP);
        runningTotal['y'] = concatenateArrays(runningTotal['y'], y);

        sections[s]['baseIntegral'] = { "x" : x, "k" : k, "yP" : yP, "y" : y };
    }

    return {'integral': runningTotal, 'sections': sections};
}


function residual (theta0 is number, base is map, angleDriver is AngleDriver, targetVal is ValueWithUnits) returns ValueWithUnits
{
    var y = evalY(base, theta0, 0 * meter);           // y0 irrelevant for waist/taper
    var theta = evalTheta(base, theta0);
    var stats = footprintStatsFromDiscrete(base.x, y, theta);

    if (angleDriver == AngleDriver.WAIST)
    {
        return stats.waistLocation - targetVal;
    }

    return stats.taperAngle - targetVal;
}

export function solveTheta0ForDriver(base is map, integrationDef is map, tol is ValueWithUnits, maxIter is number) returns number
{

    // Choose target value based on driver
    var targetVal;
    if (integrationDef.angleDriver == AngleDriver.WAIST)
        targetVal = integrationDef.waistLocation;
    else
        targetVal = integrationDef.taperAngle;



    // NOTE: FeatureScript does NOT support passing residual as argument to another function.
    // So we keep the secant loop here.

    var t0 = 0;
    var f0 = residual(t0, base, integrationDef.angleDriver, targetVal);

    // A second seed. Using average K gives a reasonable scale.
    //println(base);

    var t1 = -average(base['k']);
    var f1 = residual(t1, base, integrationDef.angleDriver, targetVal);

    for (var it = 0; it < maxIter; it += 1)
    {
        if (abs(f1) <= tol)
            return t1;

        var denom = (f1 - f0);
        if (denom == 0 * denom) // unit-safe zero check pattern
            return t1;

        // secant update (unit-safe because f has units, denom has same units)
        var t2 = t1 - f1 * (t1 - t0) / denom;

        t0 = t1;
        f0 = f1;
        t1 = t2;
        f1 = residual(t1, base, integrationDef.angleDriver, targetVal);
    }

    return t1;
}

export function evalTheta(base is map, theta0 is number) returns array
{
    var th = [];
    //println('keys(base) -> ' ~keys(base));
    //println('size(base.x) -> ' ~ size(base.x));
    //println('size(base.k) -> ' ~ size(base.k));
    for (var i = 0; i < size(base.x); i += 1)
        {
            //println('base.k['~i~'] -> ' ~ base.k[i]);
            th = append(th, base.k[i] + theta0);
        }
    return th;
}

export function evalY(base is map, theta0 is number, y0 is ValueWithUnits) returns array
{
    var y = [];
    for (var i = 0; i < size(base.x); i += 1)
    {
        y = append(y, base.y[i] + theta0 * base.x[i] + y0);
    }
    return y;
}

export function footprintStatsFromDiscrete(x is array, y is array, slope is array) returns map
{
    var fbIdx = -1;
    var fbMax = -inf;
    var abIdx = -1;
    var abMax = -inf;

    for (var i = 0; i < size(x); i += 1)
    {
        if (x[i] < 0 * meter)
        {
            if (y[i].value > fbMax) { fbMax = y[i].value; fbIdx = i; }
        }
        else
        {
            if (y[i].value > abMax) { abMax = y[i].value; abIdx = i; }
        }
    }

    // Fallback to global max if one side is missing
    var globalMaxIdx = 0; var globalMax = y[0].value;
    for (var i = 1; i < size(y); i += 1)
    {
        if (y[i].value > globalMax) { globalMax = y[i].value; globalMaxIdx = i; }
    }

    if (fbIdx < 0) fbIdx = globalMaxIdx;
    if (abIdx < 0) abIdx = globalMaxIdx;

    // Refine FB widest with parabolic interpolation
    var maxFB = refineExtremum(x, y, fbIdx);

    // Refine AB widest with parabolic interpolation
    var maxAB = refineExtremum(x, y, abIdx);

    // Waist: min y between widest indices
    var i0 = min([fbIdx, abIdx]);
    var i1 = max([fbIdx, abIdx]);
    if (i0 < 0 || i1 < 0) { i0 = 0; i1 = size(x) - 1; }

    var wIdx = i0;
    var wMin = y[i0].value;
    for (var i = i0; i <= i1; i += 1)
    {
        if (y[i].value < wMin) { wMin = y[i].value; wIdx = i; }
    }

    // Refine waist with parabolic interpolation
    var waist = refineExtremum(x, y, wIdx);

    // Taper angle from refined points
    var taperAngle = 0 * degree;
    if (fbIdx != abIdx)
    {
        var deltaY = maxFB.y - maxAB.y;
        var deltaX = maxAB.x - maxFB.x;
        taperAngle = atan2(deltaY, deltaX);
    }

    return {
        "maxFB" : maxFB,
        "maxAB" : maxAB,
        "waist" : waist,
        "waistLocation" : waist.x,
        "taperAngle" : taperAngle
    };
}

/**
 * Refine an extremum (max or min) using parabolic interpolation.
 * Given discrete samples and the index of the approximate extremum,
 * fits a parabola through 3 points to estimate the true peak/valley.
 */
function refineExtremum(x is array, y is array, idx is number) returns map
{
    // Boundary check - can't interpolate at endpoints
    if (idx <= 0 || idx >= size(x) - 1)
    {
        return { "x" : x[idx], "y" : y[idx] };
    }

    var x0 = x[idx - 1]; var y0 = y[idx - 1];
    var x1 = x[idx];     var y1 = y[idx];
    var x2 = x[idx + 1]; var y2 = y[idx + 1];

    // Parabola through 3 points: y = A*(x-x1)^2 + B*(x-x1) + C
    // where C = y1, and we solve for A, B
    var h0 = x0 - x1;
    var h2 = x2 - x1;

    var denom = h0 * h2 * (h0 - h2);

    // Guard against degenerate cases (e.g., duplicate x values)
    if (abs(denom.value) < 1e-30)
    {
        return { "x" : x1, "y" : y1 };
    }

    // Solve the 2x2 system for A and B
    var dy0 = y0 - y1;
    var dy2 = y2 - y1;

    var A = (h2 * dy0 - h0 * dy2) / denom;
    var B = (h0 * h0 * dy2 - h2 * h2 * dy0) / denom;

    // Vertex at x where derivative = 0: 2*A*(x-x1) + B = 0
    // So: xPeak = x1 - B/(2*A)

    // Guard against flat region (A ˜ 0)
    if (abs(A.value) < 1e-30)
    {
        return { "x" : x1, "y" : y1 };
    }

    var xPeak = x1 - B / (2 * A);

    // Sanity check: peak should be between x0 and x2
    if (xPeak < x0 || xPeak > x2)
    {
        return { "x" : x1, "y" : y1 };
    }

    var yPeak = y1 + A * (xPeak - x1) * (xPeak - x1) + B * (xPeak - x1);

    return { "x" : xPeak, "y" : yPeak };
}

export function signR(R is ValueWithUnits, epsR is ValueWithUnits) returns number
{
    if (R.value > epsR.value) return 1;
    if (R.value < -epsR.value) return -1;
    return 0;
}

