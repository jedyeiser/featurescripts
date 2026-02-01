FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");
import(path : "onshape/std/transform.fs", version : "2856.0");

// IMPORT: tools/math_utils.fs (for safeSign)
import(path : "b1e8bfe71f67389ca210ed8b/96aed2c3625444f0bea650a0/280a24d76f52bdbf44cd941d", version : "8adbd2a2364f067a7e4b775f");

// IMPORT: tools/numerical_integration.fs (for cumTrapz)
import(path : "b1e8bfe71f67389ca210ed8b/96aed2c3625444f0bea650a0/ef834eed6e0d2df2b34c10eb", version : "542adae37c1360ee2171b5fd");
//Import solvers
import(path : "b1e8bfe71f67389ca210ed8b/fa0241a434caffbc394f0e00/99e84dbe2a4e2350792fa693", version : "21d503eab7434ae894507618");



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


export function generateFootprintFromRadiusEdges(context is Context, id is Id, radiusEdgesQuery is Query, mode is FootprintCurveBuildMode, samplingDef is map, integrationDef is map, splineDef is map) returns array
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
  * @param edgeArray: Array of maps containing edge data. Keys: start, end, stdDir, startTangent, endTangent, radiusSign, edgeQuery, edgeOrder, startRange, endRange, startGap, endGap, xPoints, radiusPoints
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
            edgeBreaks = append(edgeBreaks, {'xPoints': edgeArray[i]['xPoints'], 'radiusPoints': edgeArray[i]['radiusPoints'], 'edgeOrder': edgeArray[i]['edgeOrder'], 'edgeRadiusSign': edgeArray[i].radiusSign});
            prevSign = edgeArray[i].radiusSign;
        }
        else //We have a previous Sign. 
        {
            var curSign = edgeArray[i].radiusSign;
            edgeBreaks = append(edgeBreaks, {'xPoints': edgeArray[i]['xPoints'], 'radiusPoints': edgeArray[i]['radiusPoints'], 'edgeOrder': edgeArray[i]['edgeOrder'], 'edgeRadiusSign': edgeArray[i].radiusSign}); // break edges per normal
            
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
        if ((endLines[0].origin[1] < 0 * millimeter && endLines[1].origin[1] > 0 * millimeter) || (endLines[0].origin[1] > 0 * millimeter && endLines[1].origin[1] < 0 * millimeter))
        {
            throw regenError("Edges defining radius progression cannot cross y = 0");
        }
        
        edgeData = append(edgeData, {'edgeNum': i, 'start': startX, 'end': endX, 'startTangent': startTangent, 'endTangent': endTangent, 'radiusSign': radiusRegion, 'edgeQuery': edgeArray[i]});
    }
    
    edgeData = sort(edgeData, function(a, b) {return a.start - b.start;}); // npw ordered smallest to largest. 
    
    // Step 1 - traverse edges and find any gaps.
    for (var i = 0; i < size(edgeData); i += 1)
    {
        var thisEdge = edgeData[i];

        var otherEdges = removeElementAt(edgeData, i);

        var overlapEdges = filter(otherEdges, function(x) {return (abs(x.start - thisEdge.end) < 0.0001 * millimeter) && abs(x.end - thisEdge.end) < 0.0001*millimeter;});
        if (size(overlapEdges) > 1) // if any edge crosses this edge
        {
            println(' thisEdge: [' ~ toString(thisEdge.start) ~ ' ---> ' ~ toString(thisEdge.end) ~ ']');
            for (var e = 0; e < size(overlapEdges); e += 1)
            {
                println('   overlapEdge: [' ~ toString(overlapEdges[e].start) ~ ' ---> ' ~ toString(overlapEdges[e].end) ~ ']');
            }
            throw regenError("Radius profiles cannot overlap in X");
        }
        
        //get prev, next edges (if available)
        var prevEdge = i >= 1 ? edgeData[i-1] : undefined;
        
        edgeData[i]['edgeOrder'] = i;
        
        if (prevEdge != undefined) // if there's a previous edge
        {
            if (abs(thisEdge.start - prevEdge.end) >= 0.0001 * millimeter) // if x endpoints don't match. We need to do some futzing!
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
    
    // Step 2 - Iterate over edges to find 'radius' points. Fill in gaps based on edge tangents at endpoints if plane does not intersect edge. 
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
                    radiusPoints = append(radiusPoints, (pointDist.sides[1].point[1] + distFromStart * edgeData[i].startTangent[1]).value); // [0, 1, 0] component of tangent
                }
                else // after the edge in question
                {
                    var distFromEnd = xVal - edgeData[i].end;
                    radiusPoints = append(radiusPoints, (pointDist.sides[1].point[1] + distFromEnd * edgeData[i].endTangent[1]).value);
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
    
    // DEBUG: Print solver results                                                                                                                                                                                                                              println("=== SOLVER DEBUG ===");
    println("  angleDriver: " ~ integrationDef.angleDriver);                                                                                                                                                                                                  
    println("  theta0: " ~ theta0);
    println("  y0: " ~ toString(y0));
    println("  stats.waistLocation: " ~ toString(stats.waistLocation));
    println("  stats.taperAngle: " ~ toString(stats.taperAngle));
    if (integrationDef.angleDriver == AngleDriver.WAIST)
    {
      println("  target waistLocation: " ~ toString(integrationDef.waistLocation));
      println("  error: " ~ toString(stats.waistLocation - integrationDef.waistLocation));
    }
    else
    {
      println("  target taperAngle: " ~ toString(integrationDef.taperAngle));
      println("  error: " ~ toString(stats.taperAngle - integrationDef.taperAngle));
    }
    println("==================");

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
    
        sections[s]['baseIntegral'] = { "x" : x, "k" : k, "k" : yP, "y" : y };   
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

      println("=== SOLVER START ===");
      println("  targetVal: " ~ toString(targetVal));
      println("  tolerance: " ~ toString(tol));
      println("  maxIter: " ~ maxIter);

      // DEBUG: Print base curve structure around indices 30-40
      println("\n=== BASE CURVE DEBUG ===");
      println("Total points: " ~ size(base.x));
      var debugStart = max([0, 30]);
      var debugEnd = min([size(base.x) - 1, 40]);
      for (var i = debugStart; i <= debugEnd; i += 1)
      {
          println("  i=" ~ i ~ ": x=" ~ toString(base.x[i]) ~ ", base.y=" ~ toString(base.y[i]));
      }
      println("=== END BASE CURVE DEBUG ===\n");



    // NOTE: FeatureScript does NOT support passing residual as argument to another function.
    // So we keep the secant loop here.

    var t0 = 0;
    var f0 = residual(t0, base, integrationDef.angleDriver, targetVal);

    // A second seed. Using average K gives a reasonable scale.
    // OLD CODE: Just use -average(k) without multiplier
    var t1 = -average(base['k']);
    var f1 = residual(t1, base, integrationDef.angleDriver, targetVal);

    println("  t0=" ~ t0 ~ ", f0=" ~ toString(f0));
    println("  t1=" ~ t1 ~ ", f1=" ~ toString(f1));

    // OLD CODE: Simple secant method without stall detection
    for (var it = 0; it < maxIter; it += 1)
    {
        if (abs(f1) <= tol)
        {
            println("  CONVERGED at iteration " ~ it ~ ": f1=" ~ toString(f1));
            return t1;
        }

        var denom = (f1 - f0);
        if (denom == 0 * denom) // unit-safe zero check pattern
        {
            println("  STALLED at iteration " ~ it ~ ": denom=0");
            return t1;
        }

        // secant update (unit-safe because f has units, denom has same units)
        var t2 = t1 - f1 * (t1 - t0) / denom;

        if (it < 5 || it == maxIter - 1)  // Print first 5 and last iteration
              println("  iter " ~ it ~ ": t2=" ~ t2 ~ ", f1=" ~ toString(f1) ~ ", denom=" ~ toString(denom));

        t0 = t1;
        f0 = f1;
        t1 = t2;
        f1 = residual(t1, base, integrationDef.angleDriver, targetVal);
    }
    println("  MAX ITER REACHED: final t1=" ~ t1 ~ ", f1=" ~ toString(f1));
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
    println("  Waist (minimum Y): x=" ~ toString(waist.x) ~ ", y=" ~ toString(waist.y) ~ " (index=" ~ wIdx ~ ")");

    // DEBUG: Show Y values around waist to understand the landscape
    var wDebugStart = max([0, wIdx - 5]);
    var wDebugEnd = min([size(y) - 1, wIdx + 5]);
    println("  Y values around waist:");
    for (var i = wDebugStart; i <= wDebugEnd; i += 1)
    {
        var marker = (i == wIdx) ? " <-- MIN" : "";
        println("    i=" ~ i ~ ": x=" ~ toString(x[i]) ~ ", y=" ~ toString(y[i]) ~ marker);
    }

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
 * Refine a zero-crossing using parabolic interpolation.
 * Given discrete samples where f changes sign between idx and idx+1,
 * fits a parabola through 3 points to find where f = 0.
 *
 * @param x : X coordinates array
 * @param f : Function values array
 * @param idx : Index where sign change occurs (f[idx] and f[idx+1] have opposite signs)
 * @returns : X coordinate where parabola crosses zero
 */
function refineZeroCrossing(x is array, f is array, idx is number) returns ValueWithUnits
{
    // Need at least 3 points for parabolic fit
    if (size(x) < 3 || idx < 0 || idx >= size(x) - 1)
    {
        // Fallback to linear interpolation
        var fa = f[idx];
        var fb = f[idx + 1];
        if (abs(fb - fa) < 1e-30)
            return x[idx];
        var t = -fa / (fb - fa);
        return x[idx] + t * (x[idx + 1] - x[idx]);
    }

    // Choose 3 points around the zero-crossing
    var i0;
    var i1;
    var i2;

    if (idx == 0)
    {
        // Zero-crossing at start: use first 3 points
        i0 = 0; i1 = 1; i2 = 2;
    }
    else if (idx == size(x) - 2)
    {
        // Zero-crossing at end: use last 3 points
        i0 = size(x) - 3; i1 = size(x) - 2; i2 = size(x) - 1;
    }
    else
    {
        // Zero-crossing in middle: use point before, at, and after
        i0 = idx - 1; i1 = idx; i2 = idx + 1;
    }

    var x0 = x[i0]; var f0 = f[i0];
    var x1 = x[i1]; var f1 = f[i1];
    var x2 = x[i2]; var f2 = f[i2];

    // Check for duplicate X values (junction)
    if (abs((x1 - x0).value) < 1e-12 || abs((x2 - x1).value) < 1e-12)
    {
        // Junction detected - use linear interpolation on the bracket
        var fa = f[idx];
        var fb = f[idx + 1];
        if (abs(fb - fa) < 1e-30)
            return x[idx];
        var t = -fa / (fb - fa);
        return x[idx] + t * (x[idx + 1] - x[idx]);
    }

    // Fit parabola: f = A*(x-x1)^2 + B*(x-x1) + C where C = f1
    var h0 = x0 - x1;
    var h2 = x2 - x1;
    var denom = h0 * h2 * (h0 - h2);

    // Guard against degenerate cases
    if (abs(denom.value) < 1e-30)
    {
        // Fallback to linear
        var fa = f[idx];
        var fb = f[idx + 1];
        if (abs(fb - fa) < 1e-30)
            return x[idx];
        var t = -fa / (fb - fa);
        return x[idx] + t * (x[idx + 1] - x[idx]);
    }

    var df0 = f0 - f1;
    var df2 = f2 - f1;

    var A = (h2 * df0 - h0 * df2) / denom;
    var B = (h0 * h0 * df2 - h2 * h2 * df0) / denom;
    var C = f1;

    // Solve A*(x-x1)^2 + B*(x-x1) + C = 0
    // Let u = x - x1, solve Au^2 + Bu + C = 0
    var discriminant = B * B - 4 * A * C;

    if (discriminant.value < 0 || abs(A.value) < 1e-30)
    {
        // No real roots or nearly linear - use linear interpolation
        var fa = f[idx];
        var fb = f[idx + 1];
        if (abs(fb - fa) < 1e-30)
            return x[idx];
        var t = -fa / (fb - fa);
        return x[idx] + t * (x[idx + 1] - x[idx]);
    }

    // Two solutions: u = (-B ± sqrt(discriminant)) / (2A)
    var sqrtDisc = sqrt(discriminant);
    var u1 = (-B + sqrtDisc) / (2 * A);
    var u2 = (-B - sqrtDisc) / (2 * A);

    var xZero1 = x1 + u1;
    var xZero2 = x1 + u2;

    // Choose the root that's between x[idx] and x[idx+1]
    var xMin = min([x[idx], x[idx + 1]]);
    var xMax = max([x[idx], x[idx + 1]]);

    var in1 = (xZero1 >= xMin && xZero1 <= xMax);
    var in2 = (xZero2 >= xMin && xZero2 <= xMax);

    if (in1 && !in2)
        return xZero1;
    if (in2 && !in1)
        return xZero2;
    if (in1 && in2)
    {
        // Both roots in range - choose closer to bracket midpoint
        var mid = (x[idx] + x[idx + 1]) / 2;
        if (abs(xZero1 - mid) < abs(xZero2 - mid))
            return xZero1;
        else
            return xZero2;
    }

    // Neither root in range - fallback to linear
    var fa = f[idx];
    var fb = f[idx + 1];
    if (abs(fb - fa) < 1e-30)
        return x[idx];
    var t = -fa / (fb - fa);
    return x[idx] + t * (x[idx + 1] - x[idx]);
}

/**
 * Linear interpolation to find Y value at given X coordinate.
 * Finds the two points x[i], x[i+1] that bracket targetX and interpolates.
 */
function linearInterpolateY(x is array, y is array, targetX) returns ValueWithUnits
{
    // Find bracketing indices
    for (var i = 0; i < size(x) - 1; i += 1)
    {
        if (x[i] <= targetX && targetX <= x[i + 1])
        {
            // Linear interpolation
            var t = (targetX - x[i]) / (x[i + 1] - x[i]);
            return y[i] * (1 - t) + y[i + 1] * t;
        }
    }

    // If not found in range, return closest endpoint
    if (targetX < x[0])
        return y[0];
    else
        return y[size(y) - 1];
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
    
    // Guard against flat region (A ≈ 0)
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
