FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

// xSectPredicates (UI definitions)
export import(path : "17142132b20343b5f125e7e7", version : "20da8dfc09e465299e6d578e");


/**
 *    CROSS SECTION AND EI FEATURE (NEW APPROACH)
 *  ----------------------------------------------------
 * In an effort to reduce kernel calls, improve efficency and allow caching through editing logic we are updating our approach. 
 * 
 * Feature takes a cross section cure and a group of bodies as core input. The user can then specify how many cross sections to evaluate, and important return and debug information
 * Debug takes a ton of time, but we need to be able to drill down to points, edges, meshes on either all or any provided bodies at any provided cross sections
 * Returning composite curves of each cross section provides some intuitive insight into what's going on, but requires many kernel calls. Returning these is optional. 
 * 
 * Editing logic maps one large User query that accepts composite parts as well as bodies to an ordered array of data. This ordered array is combined material data .csv (if available)
 * and Part material data to extract relevant material properties.
 * 
 * First evaluate cross section plane data. Sort smallest to largetst X
 * track analysis data in a map, analysisMap = {}
 * track cross section data in analysisMap.crossSections. 
 * first analysisMap.Bodies -> Body aray from definition. 
 * For each body in analysisMap.Bodies
 *      first find cross sections that intersect this body.
 * 
 *      DATASTRUCTURE/SKELETON
 * 
 *      var analysisMap = {}
 *      analysisMap.crossSections = array of maps {plane: plane(origin, normal)
 *      analysisMap.bodies = evaluateQuery(context, all bodies query)
 * 
 *      for (b = 0, b < size(analysisMap.bodies), b++) in analysisMap.bodies
 *      {
 *          var body = {}
 *          body.bodyQuery = analysisMap.bodies[b]
 *          body.intersetionPlanes = array of plane indicies that intersect this plane
 *          for cs in body.intersectionPlanes -> analysisMap.crossSections[cs].bodies = append(analysisMap.crossSections[cs].bodies, i). Need to test if map keys have already been created
 *          body.boundingBox = evBox3D(body)
 *          body.adjacentBodies = array of body indicies that touch this body
 *          body.volume = volume of body
 *          body.mass = mass of body - protect against no density data. 
 *          
 *          body.edgePlaneQArr = [];
 *          body.facePlaneQArr = [];
 * 
 *          for (var p = 0, p < size(body.intersectionPlanes), p+=)
 *          {
 *              var intersectionPlane = body.intersectionPlanes[p];
 *              body.edgePlaneQArr = append(body.edgePlaneQArr, qIntersectsPlane(context, qUnion([qOwnedByBody(context, body.bodyQuery, EntityType.EDGE)])));
 *              body.facePlaneQArr = append(body.facePlaneQArr, qIntersectsPlane(context, qUnion([qOwnedByBody(context, body.bodyQuery, EntityType.FACE)])));
 *          }
 * 
 *          body.faces = evaluateQuery(context, qUnion(edgePlaneQArr));
 *          body.edges = evaluateQuery(context, qUnion(facePlaneQArr));
 * 
 *          body.intersectionFaces = mapArray(body.faces, function(x) {return {'faceQuery': x, 'BSplineSurface': evApproximateBSplineSurface(context, x)};});
 *          body.intersectionEdges = mapArray(body.edges, function(x) {return {'edgeQuery': x, 'BSplineCurve': evApproximateBSplineCurve(context, x)};});
 *          body.intersectionEdges = mapArray(body.intersectionEdges, function(x) {return mergeMaps(x, {'dimension' : size(x.BSplineCurve.controlPoints), 'firstCP' : x.BSplineCurve.controlPoints[0], 'lastCP' : x.BSplineCurve.controlPoints[size(x.BSplineCurve.controlPoints) - 1], 'averageCP' : average(x.BSplineCurve.controlPoints)});});
 * 
 *          for (f = 0, f < size(body.intersectionFaces), f+=)
 *          {
 *              var face = body.intersectionFaces[f]
 *              face.faceidx = f;
 *              face.BSplineSurface = evApproximateBSplineSurface(face.faceQuery)
 *              face.uCurves = extracted u curves from BSplineSurface. Do not approximate spline, rather take control points directly and calculate knot vectors through arc length parameterization
 *              face.vCurves = vCurve equivelant of face.uCurves
 *              face.avgUDirection = average normal of each u curve. This is the average of each uCurve's unitless, normalized vector that connects the first and last control point of that uCurve. Protect against having the same point repeated. 
 *              face.avgVDirection = see above
 *              face.useParam = enum {USE_U, USE_V}. ? (|dot(crossSectionPlane.normal, face.avgUDirection)| > |dot(crossSectionPlane.normal, face.avgVDirection)|) USE_U : USE_V  - which parameter group intersects our plane at the most normal angle?. These are the curves that we expect to 'pierce' our plane. 
 *              face.intersectionDimension = integer. Dimension of either U or V curves (# of control points) based on face.useParam
 *              face.adjacentEdges = evaluateQuery(context, qUnion([qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)]));
 *              face.adjacentEdgeIndicies = [];
 *                  
 *              for (var fae = 0, fae < size(face.adjacentEdges), fae +=) // iterate over face intersection edges
 *              {
 *                  var faeBSpline = evApproximateBSplineCurve(context, face.adjacentEdges[fae]);
 *                  var numFAEctrl = size(faeSpline.controlPoints)
 *                  var firstFAE = faeSpline.controlPoints[0];
 *                  var lastFAE = faeSpline.controlPoints[numFAEctrl - 1];
 *                  var averageFAE = average(faeSpline.controlPoints);
 *                      
 *                  for (var bie = 0, bie < size(body.intersectionEdges), bie +=) // iterate over body intersection edges
 *                  {
 *                      var bieData = body.intersectionEdges[bie];
 *                      if(bieData.dimension == numFAEctrl) // if BSplineCurves have the same number of control points. 
 *                      {
 *                          if(norm(bieData.firstCP - firstFAE) < 1e-6 * meter || norm(bieData.lastCP - firstFAE) < 1e-6 * meter) // if the first FAE control point is within tolerance of either bodyIntersectionEdge endpoint
 *                          {
 *                              if(norm(bieData.firstCP - lastFAE) < 1e-6 * meter || norm(bieData.lastCP - lastFAE) < 1e-6 * meter // if the last FAE control point is within tolerance of either bodyIntersectionEdge endpoint
*                               {
*                                   if(norm(bieData.averageCP - averageFAE) < 1e-6 * meter) // if the control points have the same average point
*                                   {
*                                       face.adjacentEdgeIndicies = append(face.adjacentEdgeIndicies, bie);
*                                       if(!any(keys(bieData), function(x) {return x == 'adjacentFaces';})) // need to create array
*                                       {
*                                           body.intersectionEdges[bie]['adjacentFaces'] = [fae]; // create array
*                                       }
*                                       else
*                                       {
*                                           body.intersectionEdges[bie]['adjacentFaces'] = append(body.intersectionEdges[bie]['adjacentFaces'], fae); // append to array
*                                       }
*                                   }
*                               }
 *                          }
 *                      }
 *                  }
 *              }
 * 
 *              
 *              face.planeIntersectionControlPointBSplines = [];
 *              face.planeIntersectionControlPoints : array of control index that most closely bound the plane  
 *                      
 *              evaluation notes: first map  point distance dot(crossSectionPlane.normal, (cp[i][j] - crossSectionPlane.origin)) for each surface control point. 
 *              We need a curve of dimension face.intersectionDimension. We assume (safely?) that this will mean that either N points are on our plane, or somewhere between N and 2N points are either on the plane or bound the plane. 
 *                  
 *              for(var planeIndex in body.intersectionPlanes)
 *              {
 *                  var plane = analysisMap.crossSections[planeIndex].plane;
 *                  var planePointDist = [];
 *                  for (var i = 0, i < size(face.BSplineSurface.controlPoints[0]), i +=)
 *                  {
 *                      for (var j = 0, j < size(face.BSplineSurface.controlPoints[j]), j +=)
 *                      {
 *                          planePointDist = append(planePointDist, {'point': face.BsplineSurface.controlPoints[i][j], 'dist' : dot((face.BsplineSurface.controlPoints[i][j] - plane.origin), plane.normal), 'index' : [i][j]});
 *                      }
 *                  }
 *                  planePointDist = sort(planePointDist, function(a, b) {return abs(a.dist) - abs(b.dist)} // sorted from shortest distance to largest distance
 * 
 *                  var closestPoints = subArray(planePointDist, 0, 2 * face.intersectionDimension); // worst case scenario - plane is on a line between two surface control points everywhere, no points are on plane
 *                  var ctrlPoints = [];
 *                  var intersectionSplines = [];
 *                              
 *                  for (var ppd = 0, ppd < size(face.intersectionDimension), ppd +=) // iterate over planePointDistances
 *                  {
 *                      if(size(ctrlPoints) >= face.intersectionDimension) // if we've found all the control points we need to
 *                      {
 *                          break;
 *                      }
 *                      else
 *                      {
 *                          var otherPoints = subArray(closestPoints, ppd, size(closestPoints));
 *                          var thisPoint = closestPoints[ppd];
 *                          if(!any(otherPoints, function(x) {return norm(x.point - thisPoint.point) < 1e-6 * meter;})) // none of the other points are within 1e-6 * meter of this point
 *                          {
 *                                      
 *                              if(abs(dot((thisPoint.point - plane.origin), plane.normal)) < 1e-6 * meter) // this point is on the plane
 *                              {
 *                                  ctrlPoints = append(ctrlPoints, thisPoint);
 *                              }
 *                              else
 *                              {
 *                                  var closestPointIndex = getClosestIndexAlongParamCurve(thisPoint, otherPoints); 
 *                                                          // need to write this function. based on face.useParam and the thisPoint.index ([u][v]), provide the closest point. if face.useParam == USE_U, this means that
 *                                                          we should return the point with either index = controlPoints[i+1][j] or index = controlPoints[i-1][j], whichever point is in otherPoints. 
 *                                                          This function should return the index of the closest point to thisPoint in otherPoints
 *                                  var closestPoint = otherPoints[closestPointIndex];
 *                                  // the closest point is GUARANTEED to be further from the plane than this point (that's the whole point of sorting the array).
 *                                  closestPoints = removeIndexAt(closestPoints, closestPointIndex);
 *                                  var t = (dot(plane.origin - thisPoint.point, plane.normal)/dot(closestPoint.point - thisPoint.point, plane.normal);
 *                                  ctrlPoints = append(ctrlPoints, (1-t) * thisPoint.point + t * closestPoint.point;    //linearly interpolate between closest control points in the right direction.
 *                              
 *                              }
 *                          }
 *                      }
 *                  }
 * 
 *                  var knotVector = approximateKnotVector(ctrlPoints); // need to write or import function
 *                  var intersectionSpline = assembleBSpline(ctrlPoints, knotVector); // need to write or import function 
 * 
 *                  face.planeIntersectionControlPoints = append(face.planeIntersectionControlPoints, ctrlPoints);  // these control points are the control points of a 'pretty good fit. However, we DON'T know that the spline resulting from 
 *                                                                      //these control points intersect our face edges.
 *                  face.planeIntersectionBSplines = append(face.planeIntersectionBSplines, intersectionSpline);
 *                              
 *                  var faceIntersectionEdges = filter(body.intersectionEdges, function(x) {return !isQueryEmpty(context,qIntersectsPlane(context, x.edgeQuery, fip));});
 *                  faceIntersectionEdges = mapArray(faceIntersectionEdges, function(x) {return {'edge' : x, 'intersections': getPlaneBSplineIntersections(context, fip, x};}); 
 *                                          // getPlaneBSplineIntersections returns an array of points where the edge and the plane intersect. In nearly all cases, this 
 *                                          // will be one point. In limited cases, this will be two points. In extreme cases, this could be 10 or more points.                           
 *                                                              
 *                  //evaluate where each faceIntersectionEdge is on our plane
 *                  faceIntersectionEdges = mapArray(faceIntersectionEdges, function(x) {return mergeMaps(x, {'planeIntersections' : getPlaneBSplineIntersections(plane, x.BSplineCurve)});}); // need to write function. Should return an array of maps containing {'point' : intersection_point}. 
 *      
 *                  //estimate parameter of each intersection point along intersectionSpline
 *                  for(var r = 0, r < size(faceIntersectionEdges), r +=)
 *                  {
 *                      for(var x = 0, x < size(faceIntersectionEdges[r].planeIntersections), x +=)
 *                      {
 *                          var fitParam = estimateParam(face.intersectionEdges[r].planeIntersections[x].point, intersectionSpline); // need to clamp to 0 1. I think we've built this functionality in tools
 *                          face.intersectionEdges[r].planeIntersections[x]['fitParam'] = fitParam;
 *                      }
 *                  }
 * 
 *                  var paramSpans = getParamSpans(face.intersectionEdges); //total number of intersections needs to be evenly divisible by 2. need to write function. Given intersection points of ALL edges, sort by fitParam. 
 *                                  Connect smallest fit param with next smallest. param. skip span. connect thrid smallest param with fourth 
 *                                  smallet param, etc, etc. 
 *                              
 *                  for(var sp = 0, sp < size(paramSpans), sp +=)
 *                  {
 *                      // We want to connect our spans. However, we really don't know that the points at either end of our span actually hit face body edges, so we replace evaluated spline endpoints with intersection points
 *                      var numEvalPoints = face.intersectionDimension > 3 ? face.intersectionDimension - 2 : 1;
 *                      var paramStep = (endParam - startParam)/(numEvalPoints + 1)
 *                      var evalParams = numEvalPoints > 1 ? range(startParam + paramStep, endParam - paramStep, numEvalPoints) : [paramStep];
 *                      var fitPoints = evaluateSpline(context, {'spline' : intersectionSpline, 'parameters' : evalParams})[0];
 *                      var splinePoints = concatenateArrays([[startPoint], fitPoints, [endPoint]]);
 *                      var finalSpline = approximateSpline(splinePoints, enforce endpoints with interpolationIndicies)[0];
 *                      analysisMap.crossSections[planeIndex].intersectionCurves = append(analysisMap.crossSections[planeIndex].intersectionCurves, {'BSplineCurve' : finalSpline, 'body' : bodyIndex.bodyIndex });// intersection curves get mapped to the cross section with associated body,
 *                                              
 *                  }
 *                  
 *              } // end iterating over plane index in body
 *          } // end iterating over each face in body
 *      } // end iterating over each body
 * 
 *      // now extract cross section data similar to before
 *      for(var cs = 0, cs < definition.numCrossSections, cs +=)
 *      {
 *          var enforceUnique = definition.createComposites;
 *          var planeIntersectionCurves = analysisMap.crossSections[cs].intersectionCurves; // these are maps {BSplineCurve, bodyIndex}
 *          var processedIntersectionCurves = enforceUnique ? extractUniqueBSplines(planeIntersectionCurves) : planeIntersectionCurves; // similar logic to before. Use adjacent bodies to quickly break down which bSpline could possibly overlap
 *          var csBodyData = [];
 *          for(var b in analysisMap.crossSections[cs].bodies)
 *          {
 *              var bodyCrossSectionData = extractBodyCrossSectionData(processedIntersectionCurves, b); // extract groups, subgroups, etc. perimeter and ear clipped mesh. Area. Moments of inertia. 
 *              if(material data is available for the body)
 *              {
 *                  bodyCrossSectionData = mergeMaps(bodyCrossSectionData, addMaterialInfo(body.materialData, bodyCrossSectionData)); // lineal density, part ABD matricies. Note that we probably need to take widths into account somehow. If we need to calculate cross section width, this should be pretty easy 
 *                                          // to add above. Come to think of it, we're also going to want to track the thickness of the ski (max displacement in plane y) up above. 
 *              }
 *              csBodyData = append(csBodyData, bodyCrossSectionData);
 *          }
 *              
 *          analysisMap.crossSections[cs].metaData = getCrossSectionMetadata(csBodyData); //{'EI' : bending_stiffness, 'rho' : lineal_density, 'neutralAxis' : neutral_axis_heaight, 'beamWidth' : beam_width, 'beamThickness' : beam_thickness, 
 *                                                                                              'bodyDataMap' : map with keys(body_index) and values of releavant body data to add to analysisMap.bodies(body_index) (Area, moments of inertia, triangles, groups, etc)
 *              
 *      }
 * 
 *      cache data as necessary
 *      if not in editing logic set data on origin
 *      if createCoposites, extract and name composite pars from unique b-spline
 *      if definition.debug run debug module. 
 *                          
 *  ********* NOTES *********
 *  We can save quite a bit of time in many evaluations by using @functions, or basically calling @evaluateDistance(..) instead of evaluate distance. see: https://www.smartbenchsoftware.com/post/featurescript-optimization
 * I'm not overly familiar with this and will play around, but great candidates could be evaluateDistance, evaluateApproximateBSplineCurve, evaluateApproximateBSplineSurface, evaluateSpline etc. I believe that calling functions this way
 * returns unitless results, so we'd need to make adjustments. I'll have research on this complete before beginning this refactor. 
 *                                                           
 *      
 */

annotation { "Feature Type Name" : "Cross Section", "Feature Type Description" : "Cross sections ski/snowboards to get detailed geometric information. If material data is available, a bending stiffness and neutral axis curve are created and returned" }
export const crossSectionEI = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Define the parameters of the feature type
    }
    {
        
    });
