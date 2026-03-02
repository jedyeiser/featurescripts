FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/transform.fs", version : "2892.0");

// IMPORT: tools/math_utils.fs (for safeSign)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/280a24d76f52bdbf44cd941d", version : "d9e09196718b914b96e84924");

// IMPORT: tools/numerical_integration.fs (for cumTrapz)
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/ef834eed6e0d2df2b34c10eb", version : "542adae37c1360ee2171b5fd");
//Import solvers
import(path : "b1e8bfe71f67389ca210ed8b/71a714bb442c2a2dabd1278a/99e84dbe2a4e2350792fa693", version : "9e71a1ec81d7a22319fafe0e");

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


export function generateFootprintFromRadiusEdges(context is Context, id is Id, radiusEdgesQuery is Query, mode
is FootprintCurveBuildMode, samplingDef is map, integrationDef is map, splineDef is map) returns array
{
    if (isQueryEmpty(context, radiusEdgesQuery))
        throw regenError("No radius profile edges selected.");

    // 1) Sample edges
    var edgeData = sampleRadiusEdges(context, radiusEdgesQuery, samplingDef);

    var orderedEdgeMap = divideEdgeDataIntoRegions(context, edgeData);
    var makeRegions = (mode == FootprintCurveBuildMode.ONE_PER_EDGE) ? orderedEdgeMap['edgeBreaks']:
    orderedEdgeMap['regionBreaks'];

    // 4) Solve footprint globally
    var solved = solveFootprintConstraints(makeRegions, integrationDef);

    var resultMaps = [];
    for (var i = 0; i < size(solved.splineSections); i += 1)
    {
        var section = solved.splineSections[i];
        var pts = [];
        for (var p = 0; p < size(section.x); p += 1)
        {
            pts = append(pts, vector(section.x[p], section.y[p], 0 * millimeter));
        }
        resultMaps = append(resultMaps, createFootprintSplineFromPoints(context, id + ('footprintSpline'~i),
        pts, splineDef));
    }

    // Refine BSplines against continuous measurement to close the
    // discrete-vs-continuous gap (~1mm → <0.05mm)
    resultMaps = refineFootprintBSplines(resultMaps, integrationDef);

    return resultMaps;
}

/* =========================
 * Sampling
 * ========================= */
 /**
  * Take an array of edges - with additional data after being processed by sampleRadiusEdges, and divide them
  into seperate arrays based on 'radius sign'.
  * @param edgeArray: Array of maps containing edge data. Keys: start, end, stdDir, startTangent, endTangent,
  radiusSign, edgeQuery, edgeOrder, startRange, endRange, startGap, endGap, xPoints, radiusPoints
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
            edgeBreaks = append(edgeBreaks, {'xPoints': edgeArray[i]['xPoints'], 'radiusPoints': edgeArray[i]
            ['radiusPoints'], 'edgeOrder': edgeArray[i]['edgeOrder'], 'edgeRadiusSign':
            edgeArray[i].radiusSign});
            prevSign = edgeArray[i].radiusSign;
        }
        else //We have a previous Sign.
        {
            var curSign = edgeArray[i].radiusSign;
            edgeBreaks = append(edgeBreaks, {'xPoints': edgeArray[i]['xPoints'], 'radiusPoints': edgeArray[i]
            ['radiusPoints'], 'edgeOrder': edgeArray[i]['edgeOrder'], 'edgeRadiusSign':
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
                regionBreaks = append(regionBreaks, {'regionNum': size(regionBreaks), 'xPoints': regionX,
                'radiusPoints': regionR, 'regionSign': prevSign});
                //add this edge's data to cleaned regionR and regionX arrays.
                regionX = edgeArray[i]['xPoints'];
                regionR = edgeArray[i]['radiusPoints'];
                // update sign
                prevSign = edgeArray[i].radiusSign;

            }
        }
        if (i == size(edgeArray)-1) // last edge
        {
            regionBreaks = append(regionBreaks, {'regionNum': size(regionBreaks), 'xPoints': regionX,
            'radiusPoints': regionR, 'regionSign': prevSign});
        }
     }

     return {'regionBreaks': regionBreaks, 'edgeBreaks': edgeBreaks};
 }

/**
 * Take a collection of edges and a sampling definition. Return a 'corrected' map containing x, R, order,
 edgeQuery, region, edgeNum data at even/correct intervals.
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
                "parameters" : [0,1]
        });

        var stdDir = endLines[0].origin[0] < endLines[1].origin[0]; // is the x value of the param0 point less than the x value of hte param1 point?

        var startX = -3 * meter;
        var endX = 3 * meter;
        var startTangent = vector(1, 1, 1);
        var endTangent = vector(1, 1, 1);
        var deltaVector = vector(1, 1, 1)* meter;


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

        var startToEnd = endX - startX; //should be positive. We use this to make sure our tangents 'point' in the direction of increasing X.

        if (dot(deltaVector, startTangent) < 0)
        {
            // dot product of the tangent vector and the start to end vector is negative. The two vectors point in opposite directions. We want them to point in the same direction.
            startTangent *= -1;
        }
        if (dot(deltaVector, endTangent) < 0)
        {
            // dot product of the tangent vector and the start to end vector is negative. The two vectors point in opposite directions. We want them to point in the same direction.
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

        edgeData = append(edgeData, {'edgeNum': i, 'start': startX, 'end': endX, 'startTangent': startTangent,
        'endTangent': endTangent, 'radiusSign': radiusRegion, 'edgeQuery': edgeArray[i]});
    }

    edgeData = sort(edgeData, function(a, b) {return a.start - b.start;}); // npw ordered smallest to largest.

    // Step 1 - traverse edges and find any gaps.
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var thisEdge = edgeData[i];

        var otherEdges = removeElementAt(edgeData, i);

        var overlapEdges = filter(otherEdges, function(x) {return (abs(x.start - thisEdge.end) < 0.0001 *
        millimeter) && abs(x.end - thisEdge.end) < 0.0001*millimeter;});
        if (size(overlapEdges) > 1) // if any edge crosses this edge
        {
            throw regenError("Radius profiles cannot overlap in X");
        }

        //get prev, next edges (if available)
        var prevEdge = i >= 1 ? edgeData[i-1] : undefined;

        edgeData[i]['edgeOrder'] = i;

        if (prevEdge != undefined) // if there's a previous edge
        {
            if (abs(thisEdge.start - prevEdge.end) >= 0.0001 * millimeter) // if x endpoints don't match. We
            //need to do some futzing!
            {
                var gap = thisEdge.start - prevEdge.end;
                edgeData[i]['startRange'] = edgeData[i].start - gap/2;
                edgeData[i-1]['endRange'] = edgeData[i-1].end + gap/2; // fill in the gap, half the distance each way.
                edgeData[i]['startGap'] = true;
                edgeData[i-1]['endGap'] = true;

                if (i == size(edgeData)-1)
                {
                    edgeData[i]['endRange'] = edgeData[i].end;
                    edgeData[i]['endGap'] = false;
                }
            }
            else
            {
                edgeData[i]['startRange'] = edgeData[i].start;
                edgeData[i-1]['endRange'] = edgeData[i-1].end;
                edgeData[i]['startGap'] = false;
                edgeData[i-1]['endGap'] = false;

                if (i == size(edgeData)-1)
                {
                    edgeData[i]['endRange'] = edgeData[i].end;
                    edgeData[i]['endGap'] = false;
                }
            }
        }
        else // there's no previous edge
        {
            edgeData[i]['startRange'] = edgeData[i].start;
            edgeData[i]['startGap'] = false;
            if (i == size(edgeData)-1) // in the event that only one edge is provided.
            {
                edgeData[i]['endRange'] = edgeData[i].end;
                edgeData[i]['endGap'] = false;
            }

        }
    }

    // Step 2 - Iterate over edges to find 'radius' points. Fill in gaps based on edge tangents at endpoints
    //if plane does not intersect edge.
    for (var i = 0; i < size(edgeData); i += 1) // for each edge
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

            if (pointDist.distance > 0 * millimeter) // edge does not hit the plane
            {
                if (xVal < edgeData[i].start) // before the edge in question
                {
                    var distFromStart = edgeData[i].start - xVal;
                    radiusPoints = append(radiusPoints, (pointDist.sides[1].point[1] + distFromStart *
                    edgeData[i].startTangent[1]).value); // [0, 1, 0] component of tangent
                }
                else // after the edge in question
                {
                    var distFromEnd = xVal - edgeData[i].end;
                    radiusPoints = append(radiusPoints, (pointDist.sides[1].point[1] + distFromEnd *
                    edgeData[i].endTangent[1]).value);
                }
            }
            else // plane intersects edge
            {
                radiusPoints = append(radiusPoints, pointDist.sides[1].point[1].value);
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

export function createFootprintSplineFromPoints(context is Context, id is Id, pts is array, splineDef is map)
returns map
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
        tol = (integrationDef.angleDriver == AngleDriver.TAPER_ANGLE) ? (1e-5 * degree) : (0.001 *
        millimeter);
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

    var stats = footprintStatsFromDiscrete(base.integral.x, yFinal, evalTheta(base.integral, theta0), integrationDef.fcpX);

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
    for (var s = 0; s < size(samples); s += 1)
    {
        var x = samples[s].xPoints;

        var k = [];
        for (var i = 0; i < size(samples[s].xPoints); i += 1)
        {
            var R = samples[s].radiusPoints[i] * cScale;
            if (abs(R) < 1e-12)
                throw regenError("Radius too close to zero at x=" ~ toString(samples[i].x));
            k = append(k, 1 / R);
        }

        var yP = cumTrapz(x, mapArray(k, function(r) {return r/meter;}), size(runningTotal['yP']) > 0 ?
        last(runningTotal['yP']) : 0 ).cumulative;
        var y = cumTrapz(x, yP, size(runningTotal['y']) > 0 ? last(runningTotal['y']) : 0 * millimeter
        ).cumulative;

        runningTotal['x'] = concatenateArrays(runningTotal['x'], x);
        runningTotal['k'] = concatenateArrays(runningTotal['k'], k);
        runningTotal['yP'] = concatenateArrays(runningTotal['yP'], yP);
        runningTotal['y'] = concatenateArrays(runningTotal['y'], y);

        sections[s]['baseIntegral'] = { "x" : x, "k" : k, "yP" : yP, "y" : y };
    }

    return {'integral': runningTotal, 'sections': sections};
}


function residual (theta0 is number, base is map, angleDriver is AngleDriver, targetVal is ValueWithUnits, fcpX)
returns ValueWithUnits
{
    var y = evalY(base, theta0, 0 * meter);           // y0 irrelevant for waist/taper
    var theta = evalTheta(base, theta0);
    var stats = footprintStatsFromDiscrete(base.x, y, theta, fcpX);

    if (angleDriver == AngleDriver.WAIST)
    {
        return stats.waistLocation - targetVal;
    }

    return stats.taperAngle - targetVal;
}

/**
 * Dispatcher: routes to the appropriate solver based on angleDriver.
 *
 * TAPER_ANGLE mode: directly solves for theta0 using the secant method on the
 *   taper angle residual. This is well-conditioned because taper angle varies
 *   smoothly and monotonically with theta0.
 *
 * WAIST mode: uses a two-level approach. The inner level solves for theta0 at a
 *   given taper angle (reusing the reliable taper solver). The outer level uses
 *   secant iteration on taper angle to drive the waist location to its target.
 *   This avoids the ill-conditioned direct solve where theta0~0 produces a nearly
 *   symmetric footprint with no well-defined waist.
 */
export function solveTheta0ForDriver(base is map, integrationDef is map, tol is ValueWithUnits, maxIter is
number) returns number
{
    var fcpX = integrationDef.fcpX;  // may be undefined if no FCP query provided
    if (integrationDef.angleDriver == AngleDriver.WAIST)
    {
        return solveWaistViaOuterSecant(base, integrationDef, tol, maxIter);
    }
    else
    {
        return solveTheta0ForTaperAngle(base, integrationDef.taperAngle, tol, maxIter, fcpX);
    }
}

/**
 * Inner solver: find theta0 that produces a target taper angle.
 *
 * Uses the secant method on the residual:
 *   f(theta0) = measuredTaperAngle(theta0) - targetTaperAngle
 *
 * This converges reliably because taper angle is a smooth, monotonic function
 * of theta0 (adding a constant slope tilts the footprint predictably).
 *
 * @param base : map from buildBaseIntegrals containing x, k, yP, y arrays
 * @param targetTaperAngle : the taper angle to solve for (ValueWithUnits, degrees)
 * @param tol : convergence tolerance on the taper angle residual
 * @param maxIter : maximum number of secant iterations
 * @returns theta0 (unitless number) that produces the target taper angle
 */
function solveTheta0ForTaperAngle(base is map, targetTaperAngle is ValueWithUnits, tol is ValueWithUnits,
maxIter is number, fcpX) returns number
{
    // Seed 1: theta0 = 0 (symmetric footprint)
    var t0 = 0;
    var f0 = residual(t0, base, AngleDriver.TAPER_ANGLE, targetTaperAngle, fcpX);

    // Seed 2: use average curvature as a scale for a reasonable perturbation.
    // -average(k) is a natural scale for theta0 because it roughly centers the
    // slope profile, giving a non-trivial taper angle to compare against.
    var t1 = -average(base['k']);
    var f1 = residual(t1, base, AngleDriver.TAPER_ANGLE, targetTaperAngle, fcpX);

    // If initial seeds give same residual, try a larger perturbation
    if (abs(f1 - f0) < tol && abs(f0) > tol)
    {
        t1 = (f0 > 0 * f0) ? -0.5 : 0.5;
        f1 = residual(t1, base, AngleDriver.TAPER_ANGLE, targetTaperAngle, fcpX);
    }

    for (var it = 0; it < maxIter; it += 1)
    {
        if (abs(f1) <= tol)
        {
            return t1;
        }

        var denom = (f1 - f0);
        if (denom == 0 * denom)
        {
            // Try to escape stall with a jump
            if (it < maxIter - 1)
            {
                var jumpDir = (f1 > 0 * f1) ? -1 : 1;
                t0 = t1;
                f0 = f1;
                t1 = t1 + jumpDir * 0.5;
                f1 = residual(t1, base, AngleDriver.TAPER_ANGLE, targetTaperAngle, fcpX);
                continue;
            }
            return t1;
        }

        // Secant update: unit-safe because f and denom both have angle units
        var t2 = t1 - f1 * (t1 - t0) / denom;

        t0 = t1;
        f0 = f1;
        t1 = t2;
        f1 = residual(t1, base, AngleDriver.TAPER_ANGLE, targetTaperAngle, fcpX);
    }

    return t1;
}

/**
 * Outer solver: find theta0 that places the waist at a target X location.
 *
 * Strategy: instead of directly solving for waist position (which is poorly
 * conditioned when theta0 ~ 0 because the footprint is nearly symmetric and
 * the "waist" is ambiguous), we treat taper angle as an intermediary variable.
 *
 * The key insight is:
 *   - solveTheta0ForTaperAngle() already works reliably
 *   - Taper angle and waist position are monotonically related (more taper
 *     shifts the waist toward the narrower end of the ski)
 *   - So we can use secant iteration in taper-angle space to drive waist
 *     position to its target
 *
 * Each outer iteration:
 *   1. Guess a taper angle via secant update
 *   2. Solve the inner problem: find theta0 for that taper angle
 *   3. Measure the resulting waist position
 *   4. Compute residual = measuredWaist - targetWaist
 *   5. Repeat until waist is within tolerance
 *
 * @param base : map from buildBaseIntegrals
 * @param integrationDef : must contain waistLocation (target) and waistWidth
 * @param waistTol : convergence tolerance on waist position (ValueWithUnits, mm)
 * @param maxOuterIter : maximum outer iterations
 * @returns theta0 that produces the target waist position
 */
function solveWaistViaOuterSecant(base is map, integrationDef is map, waistTol is ValueWithUnits,
maxOuterIter is number) returns number
{
    var targetWaist = integrationDef.waistLocation;
    var fcpX = integrationDef.fcpX;  // may be undefined if no FCP query provided
    var innerTol = 1e-3 * degree;
    var innerMaxIter = 20;

    // --- Seed 1: taper angle = 0 degrees (symmetric footprint) ---
    var taper0 = 0 * degree;
    var theta0_a = solveTheta0ForTaperAngle(base, taper0, innerTol, innerMaxIter, fcpX);
    var y_a = evalY(base, theta0_a, 0 * meter);
    var slope_a = evalTheta(base, theta0_a);
    var stats_a = footprintStatsFromDiscrete(base.x, y_a, slope_a, fcpX);
    var w0 = stats_a.waistLocation;
    var f0 = w0 - targetWaist;

    // --- Seed 2: taper angle = 0.35 degrees (typical ski range, creates real asymmetry) ---
    var taper1 = 0.35 * degree;
    var theta0_b = solveTheta0ForTaperAngle(base, taper1, innerTol, innerMaxIter, fcpX);
    var y_b = evalY(base, theta0_b, 0 * meter);
    var slope_b = evalTheta(base, theta0_b);
    var stats_b = footprintStatsFromDiscrete(base.x, y_b, slope_b, fcpX);
    var w1 = stats_b.waistLocation;
    var f1 = w1 - targetWaist;

    // Track current best theta0
    var currentTheta0 = theta0_b;

    // Check if either seed already hit the target
    if (abs(f0) <= waistTol)
    {
        return theta0_a;
    }
    if (abs(f1) <= waistTol)
    {
        return theta0_b;
    }

    // Outer secant loop: find taper angle that produces target waist
    for (var it = 0; it < maxOuterIter; it += 1)
    {
        var denom = f1 - f0;

        // Stall check: if both residuals are essentially the same, the secant
        // slope is undefined. This shouldn't happen in practice because taper
        // angle has a real effect on waist position.
        if (abs(denom) < 1e-12 * millimeter)
        {
            return currentTheta0;
        }

        // Secant update for taper angle.
        // Units: f1 [mm] * (taper1 - taper0) [deg] / denom [mm] = [deg]. Correct.
        var taper2 = taper1 - f1 * (taper1 - taper0) / denom;

        // Solve inner problem at the new taper angle
        var theta0_c = solveTheta0ForTaperAngle(base, taper2, innerTol, innerMaxIter, fcpX);
        var y_c = evalY(base, theta0_c, 0 * meter);
        var slope_c = evalTheta(base, theta0_c);
        var stats_c = footprintStatsFromDiscrete(base.x, y_c, slope_c, fcpX);
        var w2 = stats_c.waistLocation;
        var f2 = w2 - targetWaist;

        currentTheta0 = theta0_c;

        if (abs(f2) <= waistTol)
        {
            return theta0_c;
        }

        // Shift for next secant step
        taper0 = taper1;
        f0 = f1;
        taper1 = taper2;
        f1 = f2;
    }

    return currentTheta0;
}


/* =========================
 * BSpline Refinement
 * ========================= */

/**
 * Refine BSpline footprint curves by adjusting control points to precisely
 * match target constraints (waist position or taper angle, plus waist width).
 *
 * The discrete solver gets us close (~1mm), but measuring the actual BSpline
 * geometry reveals small discrepancies because discrete samples don't perfectly
 * represent the continuous curve — especially for waist position, which is
 * extremely sensitive to taper angle.
 *
 * This exploits a key property of BSplines: partition of unity means that
 * adding deltaTheta * x to each control point's y coordinate produces
 * EXACTLY y(x) + deltaTheta * x on the curve. Similarly, adding deltaY0 to
 * each cp.y shifts the entire curve vertically by exactly deltaY0. So we can
 * make precise corrections without refitting.
 *
 * For TAPER_ANGLE mode: the correction is semi-analytical. Because
 *   new_taper ≈ old_taper - deltaTheta,  we can compute deltaTheta directly.
 *
 * For WAIST mode: the waist-to-theta relationship isn't linear, so we use
 *   a Newton step with a finite-difference derivative estimate.
 *
 * @param bSplineResults : array of maps with 'bSpline' and 'points' keys
 * @param integrationDef : map with waistWidth, angleDriver, waistLocation/taperAngle
 * @returns array of maps with refined 'bSpline' and updated 'points'
 */
export function refineFootprintBSplines(bSplineResults is array, integrationDef is map) returns array
{
    var waistHalf = integrationDef.waistWidth * 0.5;
    var maxRefineIter = 5;
    var numEvalPoints = 100; // dense sampling for precise measurement

    // Refinement tolerances (tighter than the discrete solver)
    var primaryTol;
    if (integrationDef.angleDriver == AngleDriver.WAIST)
        primaryTol = 0.05 * millimeter;
    else
        primaryTol = 5e-5 * degree;
    var widthTol = 0.05 * millimeter;

    // Extract BSplineCurve objects — we'll modify control points iteratively
    var workingSplines = mapArray(bSplineResults, function(r) { return r.bSpline; });

    for (var iter = 0; iter < maxRefineIter; iter += 1)
    {
        // 1. Densely sample all BSplines and measure actual geometry
        var measured = sampleBSplinesForStats(workingSplines, numEvalPoints);
        var stats = footprintStatsFromDiscrete(measured.x, measured.y, measured.slope, integrationDef.fcpX);

        // 2. Compute errors against targets
        var primaryError;
        if (integrationDef.angleDriver == AngleDriver.WAIST)
            primaryError = stats.waistLocation - integrationDef.waistLocation;
        else
            primaryError = stats.taperAngle - integrationDef.taperAngle;

        var widthError = stats.waist.y - waistHalf;

        // 3. Check convergence on both constraints
        if (abs(primaryError) <= primaryTol && abs(widthError) <= widthTol)
        {
            break;
        }

        // 4. Compute deltaTheta to correct the primary constraint
        var deltaTheta = 0;

        if (abs(primaryError) > primaryTol)
        {
            if (integrationDef.angleDriver == AngleDriver.TAPER_ANGLE)
            {
                // Semi-analytical: for small taper angles, the relationship
                // new_taper ≈ old_taper - deltaTheta is nearly exact.
                // So deltaTheta = measured - target = primaryError in radians.
                // .value converts the angle ValueWithUnits to a unitless number
                // (FeatureScript stores angles in radians internally).
                deltaTheta = primaryError.value;
            }
            else
            {
                // For waist position, use a finite-difference Newton step:
                // probe with a small deltaTheta to estimate d(waist)/d(theta),
                // then compute the correction that zeros the residual.
                var probeTheta = 1e-5;
                var probedSplines = adjustBSplineControlPoints(workingSplines, probeTheta, 0 * meter);
                var probeMeasured = sampleBSplinesForStats(probedSplines, numEvalPoints);
                var probeStats = footprintStatsFromDiscrete(probeMeasured.x, probeMeasured.y,
                    probeMeasured.slope, integrationDef.fcpX);

                var dWaist_dTheta = (probeStats.waistLocation - stats.waistLocation) / probeTheta;

                if (abs(dWaist_dTheta) > 1e-6 * meter)
                {
                    deltaTheta = -(primaryError / dWaist_dTheta); // Newton: -f/f'
                }
            }

            // Apply the rotation to all control points
            workingSplines = adjustBSplineControlPoints(workingSplines, deltaTheta, 0 * meter);
        }

        // 5. Re-measure after rotation to get fresh waist width error
        var afterRotation = sampleBSplinesForStats(workingSplines, numEvalPoints);
        var afterStats = footprintStatsFromDiscrete(afterRotation.x, afterRotation.y, afterRotation.slope, integrationDef.fcpX);
        var newWidthError = afterStats.waist.y - waistHalf;

        // 6. Apply vertical shift to correct waist width
        if (abs(newWidthError) > widthTol)
        {
            workingSplines = adjustBSplineControlPoints(workingSplines, 0, -newWidthError);
        }
    }

    // Rebuild result maps with refined BSplines and freshly-sampled points
    var refinedResults = [];
    for (var i = 0; i < size(workingSplines); i += 1)
    {
        var spline = workingSplines[i];
        var params = range(spline.knots[0], last(spline.knots), numEvalPoints);
        var positions = evaluateSpline({ "spline" : spline, "parameters" : params })[0];
        var pts = mapArray(positions, function(p) { return vector(p[0], p[1], p[2]); });

        refinedResults = append(refinedResults, { "bSpline" : spline, "points" : pts });
    }

    return refinedResults;
}

/**
 * Densely sample an array of BSplineCurves and return combined, x-sorted
 * arrays ready for footprintStatsFromDiscrete.
 */
function sampleBSplinesForStats(splines is array, numPointsPerSpline is number) returns map
{
    var pairs = [];

    for (var i = 0; i < size(splines); i += 1)
    {
        var spline = splines[i];
        var params = range(spline.knots[0], last(spline.knots), numPointsPerSpline);
        var positions = evaluateSpline({ "spline" : spline, "parameters" : params })[0];

        for (var j = 0; j < size(positions); j += 1)
        {
            pairs = append(pairs, { "x" : positions[j][0], "y" : positions[j][1] });
        }
    }

    // Sort by x ascending so footprintStatsFromDiscrete can find widest/waist correctly
    pairs = sort(pairs, function(a, b) { return a.x - b.x; });

    var allX = mapArray(pairs, function(p) { return p.x; });
    var allY = mapArray(pairs, function(p) { return p.y; });

    // Central finite differences for slope (meters/meters = unitless).
    // footprintStatsFromDiscrete doesn't currently use slope, but we compute it
    // for correctness and forward-compatibility.
    var slope = [];
    for (var i = 0; i < size(allX); i += 1)
    {
        var dx;
        if (i == 0)
            dx = allX[1] - allX[0];
        else if (i == size(allX) - 1)
            dx = allX[i] - allX[i - 1];
        else
            dx = allX[i + 1] - allX[i - 1];

        if (abs(dx) < 1e-12 * meter)
        {
            // Guard against zero-width intervals at section boundaries
            slope = append(slope, size(slope) > 0 ? last(slope) : 0);
        }
        else
        {
            var dy;
            if (i == 0)
                dy = allY[1] - allY[0];
            else if (i == size(allX) - 1)
                dy = allY[i] - allY[i - 1];
            else
                dy = allY[i + 1] - allY[i - 1];

            slope = append(slope, dy / dx);
        }
    }

    return { "x" : allX, "y" : allY, "slope" : slope };
}

/**
 * Adjust BSpline control points to apply a rotation and/or vertical shift.
 *
 * Thanks to the BSpline partition of unity property, these control point
 * modifications produce EXACT changes on the curve:
 *   cp.y += deltaTheta * cp.x  →  curve y(x) += deltaTheta * x  (rotation)
 *   cp.y += deltaY0             →  curve y(x) += deltaY0         (shift)
 *
 * @param splines : array of BSplineCurve maps
 * @param deltaTheta : unitless slope correction (rotation)
 * @param deltaY0 : vertical shift (ValueWithUnits, length)
 * @returns array of new BSplineCurve maps with adjusted control points
 */
function adjustBSplineControlPoints(splines is array, deltaTheta is number, deltaY0 is ValueWithUnits)
returns array
{
    var result = [];
    for (var i = 0; i < size(splines); i += 1)
    {
        var spline = splines[i];
        var newCPs = [];

        for (var j = 0; j < size(spline.controlPoints); j += 1)
        {
            var cp = spline.controlPoints[j];
            // cp[0] = x (meters), cp[1] = y (meters), cp[2] = z (meters)
            // deltaTheta * cp[0] has units: unitless * meters = meters ✓
            var newY = cp[1] + deltaTheta * cp[0] + deltaY0;
            newCPs = append(newCPs, vector(cp[0], newY, cp[2]));
        }

        // Reconstruct BSplineCurve with new control points, preserving all other fields.
        // Using plain map construction (same pattern as arcFit.fs primitivesToBSplines).
        result = append(result, {
            "degree" : spline.degree,
            "dimension" : 3,
            "isPeriodic" : spline.isPeriodic,
            "isRational" : false,
            "controlPoints" : newCPs,
            "knots" : spline.knots
        });
    }
    return result;
}


export function evalTheta(base is map, theta0 is number) returns array
{
    var th = [];
    for (var i = 0; i < size(base.x); i += 1)
        {
            th = append(th, base.yP[i] + theta0);
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

// fcpX is an optional untyped parameter. When provided (ValueWithUnits), FB/AB are assigned
// by proximity to FCP relative to the waist, making the convention correct for any X orientation.
// When undefined (legacy / no FCP query), falls back to x < 0 = FB, x >= 0 = AB.
export function footprintStatsFromDiscrete(x is array, y is array, slope is array, fcpX) returns map
{
    var fbIdx = -1;
    var fbMax = -inf;
    var abIdx = -1;
    var abMax = -inf;

    if (fcpX != undefined)
    {
        // Find approximate waist (global minimum Y) to use as the split point.
        var approxWaistIdx = 0;
        var approxWaistMin = y[0].value;
        for (var i = 1; i < size(y); i += 1)
        {
            if (y[i].value < approxWaistMin) { approxWaistMin = y[i].value; approxWaistIdx = i; }
        }
        var approxWaistX = x[approxWaistIdx];

        // FB = the side of the waist that FCP is on; AB = the other side.
        var fcpIsBelowWaist = fcpX < approxWaistX;
        for (var i = 0; i < size(x); i += 1)
        {
            var isOnFcpSide = fcpIsBelowWaist ? (x[i] <= approxWaistX) : (x[i] >= approxWaistX);
            if (isOnFcpSide)
            {
                if (y[i].value > fbMax) { fbMax = y[i].value; fbIdx = i; }
            }
            else
            {
                if (y[i].value > abMax) { abMax = y[i].value; abIdx = i; }
            }
        }
    }
    else
    {
        // Legacy fallback: negative X = forebody, positive X = aftbody.
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

    // Waist: find where slope (theta) crosses zero between widest indices
    // This is the correct definition - waist is where dY/dX = 0
    var i0 = min([fbIdx, abIdx]);
    var i1 = max([fbIdx, abIdx]);
    if (i0 < 0 || i1 < 0) { i0 = 0; i1 = size(x) - 1; }

    // Waist: find MINIMUM Y between the two widest points
    // This is the narrowest point of the footprint, NOT where slope=0
    var wIdx = i0;
    var wMin = y[i0].value;
    for (var i = i0; i <= i1; i += 1)
    {
        if (y[i].value < wMin)
        {
            wMin = y[i].value;
            wIdx = i;
        }
    }

    // Use parabolic interpolation to refine the minimum location
    var waist = refineExtremum(x, y, wIdx);

    // Taper angle: positive = FB (FCP side) wider than AB (ACP side).
    // deltaX is always the absolute X distance so the sign comes only from deltaY.
    var taperAngle = 0 * degree;
    if (fbIdx != abIdx)
    {
        var deltaY = maxFB.y - maxAB.y;
        var deltaX = abs(maxAB.x - maxFB.x);
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

    // Guard against flat region (A � 0)
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

/**
 * Combines results from evPathTangentLines and evEdgeCurvature. Provides curvature data for paths.
 *
 * - Uses evPathTangentLines for path-aligned tangent directions (already accounts for path.flipped)
 * - Uses evDistance(edge, point) to recover the closest edge parameter
 * - Computes SIGNED curvature for a planar XY footprint using binormal vs world +Z
 * - Ensures sign is consistent with PATH traversal direction (via dot(tEdge, tPath))
 *
 * Moved from fpt_math.fs during Phase 1 refactoring (2026-01-31)
 */
export function evPathCurvatures(context is Context, path is Path, params is array) returns map
{
    var retMap = {
        "edges" : path.edges,
        "flipped" : path.flipped,
        "closed" : path.closed,
        "evalParams" : params
    };

    var pathTangentLines = evPathTangentLines(context, path, params);
    var tangentLines = pathTangentLines.tangentLines;
    var edgeIndices = pathTangentLines.edgeIndices;

    var points = [];
    var tangents = [];
    var edgeParams = [];

    // Raw curvature results (magnitude + frame)
    var curvatures = [];

    // Signed curvature (ValueWithUnits) and sign scalar (unitless)
    var curvatureSigned = [];
    var curvatureSign = [];
    var curvatureMag = [];

    // World plane normal (XY footprint)
    var worldZ = vector(0, 0, 1);

    for (var i = 0; i < size(params); i += 1)
    {
        var tangentLine = tangentLines[i];
        var edge = path.edges[edgeIndices[i]];

        var p = tangentLine.origin;
        var tPath = normalize(tangentLine.direction); // path-aligned by evPathTangentLines

        points = append(points, p);
        tangents = append(tangents, tPath);

        // Closest parameter on the underlying edge
        var pointDist = evDistance(context, {
            "side0" : edge,
            "side1" : p
        });
        var uEdge = pointDist.sides[0].parameter;
        edgeParams = append(edgeParams, uEdge);

        // Curvature magnitude + Frenet frame at that edge parameter
        var cr = evEdgeCurvature(context, {
            "edge" : edge,
            "parameter" : uEdge
        });
        curvatures = append(curvatures, cr);

        // --- Signed curvature logic (planar) ---
        // Onshape EdgeCurvatureResult frame:
        //   zAxis = tangent, xAxis = normal, yAxis = binormal
        var b = normalize(cr.frame.yAxis); // binormal

        // Base sign from binormal relative to world +Z
        var sgn = safeSign(dot(b, worldZ), 1e-9);

        // Ensure sign is consistent with PATH traversal direction:
        // If edge's parameterization tangent opposes the path tangent, reverse sign.
        var tEdge = normalize(evEdgeTangentLine(context, { "edge" : edge, "parameter" : uEdge }).direction);
        if (dot(tEdge, tPath) < 0)
        {
            sgn = -sgn;
        }

        // Optional guard near inflection (avoid noisy sign when curvature ~ 0)
        var kMag = cr.curvature;
        var kSigned;
        if (abs(kMag.value) < 1e-12)
        {
            sgn = 0;
            kSigned = 0 * kMag;
        }
        else
        {
            kSigned = (sgn == 0) ? (0 * kMag) : (sgn * kMag);
        }

        curvatureMag = append(curvatureMag, kMag);
        curvatureSign = append(curvatureSign, sgn);
        curvatureSigned = append(curvatureSigned, kSigned);
    }

    retMap["points"] = points;
    retMap["tangents"] = tangents;
    retMap["edgeParams"] = edgeParams;
    retMap["curvatures"] = curvatures;

    retMap["edgeIndices"] = edgeIndices;
    retMap["curvatureSign"] = curvatureSign;
    retMap["curvatureSigned"] = curvatureSigned;
    retMap["curvatureMag"] = curvatureMag;

    return retMap;
}
