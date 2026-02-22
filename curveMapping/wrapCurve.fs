FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");
import(path : "onshape/std/approximationUtils.fs", version : "2878.0");
import(path : "onshape/std/path.fs", version : "2878.0");


//import tools/bspline_data
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/b1c7f2116fb64e6b40bf53f4", version : "4fe0cca8e00a4cd812896a8c");
//import Utils
import(path : "ad98c7f43a25a4c0e8a428e7", version : "63d775b3f3586154f31324be");


export const samplingDensityBounds = {(millimeter) : [.1, 1, 10]} as LengthBoundSpec;

annotation { "Feature Type Name" : "Wrap Curve",
             "Feature Type Description" : "Map curves from one reference edge to another using Frenet frame transformations",
             "Filter Selector" : "allparts"
            }
export const wrapCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "From data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "From edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map from (source reference)" }
            definition.fromEdges is Query;
            
            annotation { "Name" : "From reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1 , "Description" : "reference point on from curve"}
            definition.fromRef is Query;
            
        }
        
        annotation { "Group Name" : "To data", "Collapsed By Default" : true }
        {
            annotation { "Name" : "To edge(s)",
                    "Filter" : EntityType.EDGE,
                    "MaxNumberOfPicks" : 10,
                    "Description" : "Reference edge(s) to map to (target reference)" }
            definition.toEdges is Query;
            
            annotation { "Name" : "Flip", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.flipTo is boolean;
            
            annotation { "Name" : "To reference", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR || GeometryType.PLANE, "MaxNumberOfPicks" : 1, "Description" : "reference point on to curve" }
            definition.toRef is Query;
            
            annotation { "Name" : "Flip normal", "Default" : false, "Description" : "When true, flips the frenet frame normal vector on the to chain" }
            definition.flipToNormal is boolean;
            
        }
        


        annotation { "Name" : "Source curves",
                    "Filter" : EntityType.EDGE,
                    "Description" : "Curves to map from fromEdge to toEdge" }
        definition.sourceCurves is Query;


        annotation { "Name" : "Advanced options",
                    "Default" : false }
        definition.showAdvanced is boolean;

        if (definition.showAdvanced)
        {
            annotation { "Name" : "Sampling density", "Description" : "Distance between sample points along source edges" }
            isLength(definition.samplingDensity, samplingDensityBounds);
            
            annotation { "Name" : "Target degree", "Column Name" : "Approximation target degree" }
            isInteger(definition.approximationDegree, DEGREE_BOUND);

            annotation { "Name" : "Maximum control points" }
            isInteger(definition.approximationMaxCPs, { (unitless) : [4, 15, MAX_CONTROL_POINTS] } as IntegerBoundSpec);

            annotation { "Name" : "Tolerance" }
            isLength(definition.approximationTolerance, TOLERANCE_BOUND);
            
        }

        annotation { "Group Name" : "Debug Options",
                    "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print from BSplines",
                        "Description" : "Print BSpline data from 'from' reference chain",
                        "Default" : false }
            definition.debugFromBSplines is boolean;

            annotation { "Name" : "Print to BSplines",
                        "Description" : "Print BSpline data from 'to' reference chain",
                        "Default" : false }
            definition.debugToBSplines is boolean;

            annotation { "Name" : "Print source BSplines",
                        "Description" : "Print BSpline data from source curves",
                        "Default" : false }
            definition.debugSourceBSplines is boolean;

            annotation { "Name" : "Print wrapped BSplines",
                        "Description" : "Print BSpline data from wrapped output curves",
                        "Default" : false }
            definition.debugWrappedCurves is boolean;
        }
    }
    
    {
        var processedRefs = processEdgeRefs(context, id, definition);
        definition.processedRefs = processedRefs;
    });
    
/**
 * Processes input to and from curves such that they can be used
 * to deform geometry. Engorces G1 continuity and throws an error if curves
 * do not conform. Provides information about how frenet frames should change along the reference
 * @param context {Context} : context
 * @param definition {{
 *      @field fromEdges {Query} : Query for from edges
 *      @field toEdges {Query} : Query for to eges
 * }}
 * @returns {map} : a map with keys 'toRef' and 'fromRef'
 */
 export function processEdgeRefs(context is Context, id is Id, definition is map) returns map
 {
     var fromRef = processEdgeRef(context,id, definition, 'from', false);
     var toRef = processEdgeRef(context, id, definition, 'to', definition.flipTo);
     return {'toRef' : toRef, 'fromRef' : fromRef};
 }
 
/**
 * Processes a group of edges to ensure G1 continuity and 
 * provide metadata about the set of edges, including information
 * that will be used to get internally consistent frente frames
 * @param context {Context} : context
 * @param definition {{
 *      @field fromEdges {Query} : Query for from edges
 *      @field toEdges {Query} : Query for to eges
 * }}
 * @param region {string} : region name. Will be used to select fromEdges or toEges, as well as reporting correct 
 * @param flipRef {boolean} : if true, flips standard chain evaluation direction. 
 * @returns {{
 *      @field startPoint {Vector} : Chain startpoint. Corresponds to parameter 0
 *      @field endPoint {Vector} : Chain endpoint. Corresponds to parameter 1
 *      @field startFrenet {frame} : coordinate system at parameter 0
 *      @field endFrenet {frame} : coordinate system at parameter 1
 *      @field length {ValueWithUnits} : Length of the chain in mm
 *      @field edges {array} : array of edge maps. Edge map keys include length, startPathParam, endPathParam, BSplineCurve, curveType, frenetDef, etc
 * 
 */
export function processEdgeRef(context is Context, id is Id, definition is map, sourceString is string, flipRef is boolean) returns map
{
    var edgePath;
    var sourceEdges = (sourceString == "from") ? definition.fromEdges : definition.toEdges;
    var retMap = {'flip' : flipRef};
    
    try
    {
        @constructPaths(context, {"edges" : sourceEdges}); // can run dimensionless. We don't atually want the path - we just want to see if we can create one. 
    }
    catch (error)
    {
        reportFeatureInfo(context, id, (sourceString ~ " edges must be G1 continuous"));
        throw regenError((sourceString ~ " edges must be G1 continuous"), (sourceString ~ "Edges"), sourceEdges);
    }
    
    //we were able to create a path, we have G1 continuity. Moving on. 
    
    // startpoint defaults to the point closest to the minimum corner
    var refBox = evBox3d(context, {
            "topology" : sourceEdges,
            "tight" : true
    });

    var allEdges = evaluateQuery(context, qUnion([sourceEdges]));
    var edgeMapArray = mapArray(allEdges, function(x) {return {"query" : x};}); // add edge query
    edgeMapArray = mapArray(edgeMapArray, function(x) {return mergeMaps(x, {"length" : evLength(context, { "entities" : x.query})});}); // add length of each edge
    edgeMapArray = mapArray(edgeMapArray, function(x) {return mergeMaps(x, {"BSplineCurve" : evApproximateBSplineCurve(context, { "edge" : x.query})});}); // evaluate BSplineCurve for each edge
    edgeMapArray = mapArray(edgeMapArray, function(x) {return mergeMaps(x, {"curveDef" : evCurveDefinition(context, { "edge" : x.query})});}); // get curve definition for each edge. This will include BSpline info for BSPlines, but also call out arcs, and - importantly - lines. 
    
    for (var i = 0; i < size(edgeMapArray); i += 1) // add edge endpoints as well as edge index. 
    {
        var curveType = edgeMapArray[i]['curveDef']['curveType'];
        
        var endLines = evEdgeTangentLines(context, {
                "edge" : edgeMapArray[i].query,
                "parameters" : [0,1]
        });
        
        edgeMapArray[i]['startPoint'] = endLines[0].origin;
        edgeMapArray[i]['endPoint'] = endLines[1].origin;
        edgeMapArray[i]['index'] = i;
        
    }
    
    for (var i = 0; i < size(edgeMapArray); i += 1) // map edge adjacency for each edge. 
    {
        var adjacentEdges = [];
        var thisStart = edgeMapArray[i].startPoint;
        var thisEnd = edgeMapArray[i].endPoint;
        var startFree = true;
        var endFree = true;
        for (var j = 0; j < size(edgeMapArray); j += 1)
        {
            if (i == j) // we don't want to compare an edge to itself. 
            {
                continue;
            }
            else
            {
                var otherStart = edgeMapArray[j].startPoint;
                var otherEnd = edgeMapArray[j].endPoint;
                if ((norm(thisStart - otherStart) < 1e-6 * meter) || (norm(thisStart - otherEnd) < 1e-6 * meter)) // if this startpoint is within 1e-6 * meter of one of the endpoints of the other curve, we've found an adjacent curve
                {
                    adjacentEdges = append(adjacentEdges, j);
                    var startFree = false;
                }
                if ((norm(thisEnd - otherStart) < 1e-6 * meter) || (norm(thisEnd - otherEnd) < 1e-6 * meter)) // if this endpoint is within 1e-6 * meter of one of the endpoints of the other curve, we've found an adjacent curve
                {
                    adjacentEdges = append(adjacentEdges, j);
                    var endFree = false;   
                }
                
            }
            
        }
        edgeMapArray[i]['adjacentEdges'] = adjacentEdges; // add the adjacency array to the edgeMap
        edgeMapArray[i]['endFreedom'] = {'startFree' : startFree, 'endFree' : endFree}; // add endFreedom to the edgeMap
        
        if (edgeMapArray[i].curveDef.curveType != CurveType.LINE) // if this isn't a line, we want to look into some start and stop coordinate systems 
        {
            var endpointCurvatures = evEdgeCurvatures(context, {
                    "edge" : edgeMapArray[i].query,
                    "parameters" : [0, 1]
            });
            edgeMapArray[i]['frenetDef'] = {'startFrame' : endpointCurvatures[0].frame, 'endFrame' : endpointCurvatures[1].frame};
        }
    }
    
    //find startpoint (closest free point to min corner) start chaining. 
    var possibleStartEdges = (size(edgeMapArray) > 1) ? filter(edgeMapArray, function(x) {return size(x.adjacentEdges) > 1;}) : edgeMapArray;
    for (var i = 0; i < size(possibleStartEdges); i += 1)
    {
        var distToBoxMin = 100 * meter;
        if (possibleStartEdges[i].endFreedom.startFree)
        {
            distToBoxMin = norm(refBox.minCorner - possibleStartEdges[i].startPoint);
        }
        if (possibleStartEdges[i].endFreedom.endFree)
        {
            distToBoxMin = norm(refBox.minCorner - possibleStartEdges[i].endPoint);
        }
        possibleStartEdges[i]['distToBoxMin'] = distToBoxMin;
    }
    
    var minStartDist = min(mapArray(possibleStartEdges, function(x) {return x.distToBoxMin;}));
    var startEdge = filter(possibleStartEdges, function(x) {return abs(minStartDist - x.distToBoxMin) < 1e-6 * meter;})[0];
    
    var nextIndex = startEdge.index;
    var nextStart = (startEdge.endFreedom.startFree) ? startEdge.startPoint : startEdge.endPoint; //next start point
    
    var chainLength = 0 * meter;
    
    for (var i = 0; i < size(edgeMapArray); i += 1) //chaining loop. 
    {
        var thisEdge = edgeMapArray[(nextIndex)];
        edgeMapArray[i]['chainOrder'] = i;
        var stdDir = norm(thisEdge.startPoint - nextStart) < 1e-6 * meter; // if the start point (param 0) is at nextStart, we're evaluating in a standard direction. 
        edgeMapArray[i]['stdDir'] = stdDir;
        
        // evaluate next loop
        nextStart = stdDir ? thisEdge.endPoint : thisEdge.startPoint;
        var edgeContainsNextStart = filter(edgeMapArray, function(x) {return ( (norm(x.startPoint - nextStart) <= 1e-6 * meter) || (norm(x.endPoint - nextStart) <= 1e-6 * meter));});
        var nextEdge = (size(edgeMapArray) > 1) ? filter(edgeContainsNextStart, function(x) {return x.index != thisEdge.index;})[0] : edgeMapArray[0]; // the explicit assumption here is that only two edges will share an endpoint
        nextIndex = nextEdge.index;
        chainLength += thisEdge.length;
        
    }
    
    //order edges
    edgeMapArray = sort(edgeMapArray, function(a, b) {return a.chainOrder - b.chainOrder;});
    
    var needSearchCurvature = !any(keys(edgeMapArray[0]), function(x) {return x =='frenetDef';}); //if the first element already has a frenet def, than we're all set. 
    //Otherwise, we've got to figure out what the frenet frame at the start of the chain is. Note that we will have frenetFrames defined for all curve types except lines
    
    if (needSearchCurvature)
    {
        
        var hasFrameInfo = filter(edgeMapArray, function(x) {return any(keys(x) , function(y) {return y == 'frenetDef';});});
        if (size(hasFrameInfo) == 0)
        {
            retMap['hasFrenetDriver'] = false; // no curves have a frenet refrence. 
            var bestFrame = lineFrenetFrame(line(edgeMapArray[0].curveDef.origin, edgeMapArray[0].curveDef.direction));
            edgeMapArray[0]['frenetDef'] = {'startFrame' : coordSystem(edgeMapArray[0].startPoint, bestFrame.xAxis, bestFrame.zAxis), 'endFrame' : coordSystem(edgeMapArray[0].endPoint, bestFrame.xAxis, bestFrame.zAxis)};
        }
        else
        {
            retMap['hasFrenetDriver'] = true; //we have a frenet Frame def in at least one of the edges
            var firstDriver = hasFrameInfo[0];
            var drivingFrame = firstDriver.stdDir ? firstDriver.startFrame : firstDriver.endFrame;
            // We SHOULD be ok with these assumptions, but they seem a bit brittle. Worth looking into once functionality 
            edgeMapArray[0]['frenetDef'] = {'startFrame' : coordSystem(edgeMapArray[0].startPoint, drivingFrame.xAxis, drivingFrame.zAxis), 'endFrame' : coordSystem(edgeMapArray[0].endPoint, drivingFrame.xAxis, drivingFrame.zAxis)};
            
        }
    }
    
    //no additional searching necessary if we have a frenetDef in the first edge. We DO however need to work through the chain to:
    //1. Adjust the zAxis of each frame so that it points in the correct direction 0 -> 1
    //2. Compare the xAxis of frenet frames of two adjacent edges. Really, these need to be parallel/antiparallel. If they're not, transformations are ill defined
    // and we need to throw an error. the xAxis of a 'new' edge should flip (if necessary) to allign with the preceeding edge. 
    //3. Understand inflection points within an edge and specify parameters where we should 'flip' the x-Axis. 
    
    var refXAxis = (edgeMapArray[0].stdDir) ? edgeMapArray[0].frenetDef.startFrame.xAxis : edgeMapArray[0].frenetDef.endFrame.xAxis; //we update each loop. This is the 'last frame along the chain from the last edge'. 
    
    for (var i = 0; i < size(edgeMapArray); i += 1) //iterate over edges, assign/flip start/end frames. Check coherence. 
    {
        var thisEdge = edgeMapArray[i];
        if (!any(keys(edgeMapArray[i]), function(x) {return x == "frenetDef";})) //we're somewhere in the chain and hit an edge without a frenetDef, which must be a line. 
        {
            var lastEdge = edgeMapArray[i-1];
            var lastFrame = lastEdge.stdDir ? lastEdge.frenetDef.endFrame : lastEdge.frenetFDef.startFrame;
            edgeMapArray[i]['frenetDef'] = {'startFrame' : coordSystem(thisEdge.startPoint, lastFrame.xAxis, lastFrame.zAxis), 'endFrame' : coordSystem(thisEdge.endPoint, lastFrame.xAxis, lastFrame.zAxis)};
            
        }
        var thisStartFrame = flipFrameToMatchCurve(context, thisEdge.frenetDef.startFrame, thisEdge.BSplineCurve, 0, thisEdge.stdDir);
        var thisEndFrame = flipFrameToMatchCurve(context, thisEdge.frenetDef.endFrame, thisEdge.BSplineCurve, 1, thisEdge.stdDir);
        //Frame zAxes pointing in chain direction. 
        

        var dirDot = dot(thisStartFrame.xAxis, refXAxis);
        if (dirDot < 0.9) // normals don't point in the same/opposite directions
        {
            reportFeatureInfo(context, id, (sourceString ~ " edges must have curvature pointing in parallel/antiparallel directions at junctions"));
            throw regenError((sourceString ~ " edges must have curvature pointing in parallel/antiparallel directions at junctions"), (sourceString ~ "Edges"), sourceEdges);
        }
        
        else // parallelish/antiparallelish xAxes
        {
            //does this edge have inflection points? If so, find them. 
            edgeMapArray[i]['hasInflections'] = false;
            edgeMapArray[i]['inflectionParams'] = [];
            var searchInflections = bSplineMayHaveInflection(edgeMapArray[i].BSplineCurve);
            
            if (searchInflections)
            {
                var foundInflections = findBSplineInflections(edgeMapArray[i].BSplineCurve, 4 * size(edgeMapArray[i].BSplineCurve.controlPoints), 0.0001);    
                edgeMapArray[i]['hasInflections'] = true;
                edgeMapArray[i]['inflectionParams'] = foundInflections;
            }
            
            if (dirDot < 0) // xAxes point in the opposite direction. Flip 
            {
                if (edgeMapArray[i].stdDir)
                {
                    thisStartFrame.xAxis = -1 * thisStartFrame.xAxis;
                }
                else
                {
                    thisEndFrame.xAxis = -1 * thisEndFrame.xAxis;
                }
            }
            
            var inflectionFlip = 1;
            for (var p in edgeMapArray[i].inflectionParams)
            {
                inflectionFlip *= -1;
            }
            
            if (edgeMapArray[i].stdDir)
            {
                thisEndFrame.xAxis = thisEndFrame.xAxis * inflectionFlip; //if theres one inflection, flips xAxis. If two, flips twice, so reverts. 
                refXAxis  = thisEndFrame.xAxis;
            }
            else // not standard dir
            {
                thisStartFrame.xAxis = thisStartFrame.xAxis * inflectionFlip;
                refXAxis = thisStartFrame.xAxis;
            }
        }
        
        // Now insert the conditioned frames back in
        edgeMapArray[i].frenetDef = {'startFrame' : thisStartFrame, 'endFrame' : thisEndFrame};    
    }
    
    //now get 
    
    retMap['edgeMapArray'] = edgeMapArray;
    
    return retMap;
}

function flipFrameToMatchCurve(context is Context, frame is CoordSystem, curve is BSplineCurve, parameter is number, stdDir is boolean) returns CoordSystem
{
    var cps = curve.controlPoints;
    var n = size(cps);

    // Determine reference direction from the control polygon
    var referenceDir = (parameter < 0.5) ?
        normalize(cps[1] - cps[0]) :
        normalize(cps[n - 1] - cps[n - 2]);

    // If non-standard direction, we want frames pointing from param 1 -> 0
    if (!stdDir)
    {
        referenceDir = -referenceDir;
    }

    // Flip if the frame's tangent opposes the desired direction
    if (dot(frame.zAxis, referenceDir) < 0)
    {
        frame = coordSystem(frame.origin, frame.xAxis, -frame.zAxis);
    }

    return frame;
}


