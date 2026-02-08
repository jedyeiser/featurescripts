FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/*
 *    CROSS SECTION AND EI FEATURE (NEW APPROACH v2)
 *  ----------------------------------------------------
 * In an effort to reduce kernel calls, improve efficiency and allow caching through editing logic we are updating our approach.
 *
 * Feature takes a cross section curve and a group of bodies as core input. The user can then specify how many cross sections to evaluate, and important return and debug information.
 * Debug takes a ton of time, but we need to be able to drill down to points, edges, meshes on either all or any provided bodies at any provided cross sections.
 * Returning composite curves of each cross section provides some intuitive insight into what's going on, but requires many kernel calls. Returning these is optional.
 *
 * Editing logic maps one large User query that accepts composite parts as well as bodies to an ordered array of data. This ordered array is combined material data .csv (if available)
 * and Part material data to extract relevant material properties.
 *
 * First evaluate cross section plane data. Sort smallest to largest X.
 * Track analysis data in a map, analysisMap = {}
 * Track cross section data in analysisMap.crossSections.
 * First analysisMap.bodies -> Body array from definition.
 * For each body in analysisMap.bodies, find cross sections that intersect this body.
 *
 *      DATASTRUCTURE / SKELETON
 *      ========================
 *
 *      var analysisMap = {}
 *      analysisMap.crossSections = array of maps { plane : plane(origin, normal), bodies : [], intersectionCurves : [] }
 *      analysisMap.bodies = evaluateQuery(context, all bodies query)
 *
 *      for (b = 0; b < size(analysisMap.bodies); b += 1)
 *      {
 *          var body = {}
 *          body.bodyQuery = analysisMap.bodies[b]
 *          body.bodyIndex = b
 *          body.intersectionPlanes = array of plane indices that intersect this body
 *
 *          // --- Cross-reference: mark each intersected cross-section with this body index ---
 *          for cs in body.intersectionPlanes:
 *              if analysisMap.crossSections[cs] does not have 'bodies' key yet, create empty array
 *              analysisMap.crossSections[cs].bodies = append(analysisMap.crossSections[cs].bodies, b)
 *
 *          body.boundingBox = evBox3d(body)
 *          body.adjacentBodies = array of body indices that touch this body
 *          body.volume = volume of body
 *          body.mass = mass of body - protect against no density data
 *
 *          // --- Collect faces and edges that intersect ANY of our cross-section planes ---
 *          // FIXED: facePlaneQArr feeds body.faces, edgePlaneQArr feeds body.edges
 *          body.facePlaneQArr = [];
 *          body.edgePlaneQArr = [];
 *
 *          for (var p = 0; p < size(body.intersectionPlanes); p += 1)
 *          {
 *              var intersectionPlane = analysisMap.crossSections[body.intersectionPlanes[p]].plane;
 *              body.facePlaneQArr = append(body.facePlaneQArr, qIntersectsPlane(qOwnedByBody(body.bodyQuery, EntityType.FACE), intersectionPlane));
 *              body.edgePlaneQArr = append(body.edgePlaneQArr, qIntersectsPlane(qOwnedByBody(body.bodyQuery, EntityType.EDGE), intersectionPlane));
 *          }
 *
 *          body.faces = evaluateQuery(context, qUnion(body.facePlaneQArr));
 *          body.edges = evaluateQuery(context, qUnion(body.edgePlaneQArr));
 *
 *          // --- Preprocess edges: evaluate B-splines once, cache summary data ---
 *          body.intersectionEdges = mapArray(body.edges, function(x) {
 *              return { 'edgeQuery' : x, 'BSplineCurve' : evApproximateBSplineCurve(context, x) };
 *          });
 *          body.intersectionEdges = mapArray(body.intersectionEdges, function(x) {
 *              var cps = x.BSplineCurve.controlPoints;
 *              var n = size(cps);
 *              return mergeMaps(x, {
 *                  'dimension' : n,
 *                  'firstCP' : cps[0],
 *                  'lastCP' : cps[n - 1],
 *                  'averageCP' : average(cps)
 *              });
 *          });
 *
 *          // --- Preprocess faces ---
 *          body.intersectionFaces = [];
 *          for (f = 0; f < size(body.faces); f += 1)
 *          {
 *              var face = {}
 *              face.faceIdx = f;
 *              face.faceQuery = body.faces[f];
 *              face.BSplineSurface = evApproximateBSplineSurface(context, face.faceQuery);
 *
 *              // --- Compute face bounding box from surface control points (for cheap per-plane filtering) ---
 *              var allCPs = flatten(face.BSplineSurface.controlPoints);  // flatten 2D grid to 1D
 *              face.bbox = {
 *                  'min' : vector(min x, min y, min z across allCPs),
 *                  'max' : vector(max x, max y, max z across allCPs)
 *              };
 *
 *              // --- Extract iso-parameter curves from surface ---
 *              // Do not call evApproximateBSplineCurve; extract control points directly
 *              // and calculate knot vectors through arc-length parameterization.
 *              face.uCurves = extracted u-curves from BSplineSurface (rows of CP grid)
 *              face.vCurves = extracted v-curves from BSplineSurface (columns of CP grid)
 *
 *              face.avgUDirection = average normalized direction (firstCP → lastCP) across all u-curves.
 *                                  Protect against degenerate curves where first == last.
 *              face.avgVDirection = same for v-curves
 *
 *              // Which parameter family's curves "pierce" the cross-section plane most directly?
 *              // NOTE: This uses the first intersection plane as reference. For faces that span many
 *              // planes with varying normals, this could theoretically give the wrong answer, but
 *              // for cross sections along a single spine curve the normals are similar enough.
 *              face.useParam = (|dot(plane.normal, face.avgUDirection)| > |dot(plane.normal, face.avgVDirection)|) ? USE_U : USE_V
 *
 *              // Dimension of curves in the piercing direction (# of CPs per iso-curve)
 *              face.intersectionDimension = USE_U ? size(face.BSplineSurface.controlPoints)
 *                                                 : size(face.BSplineSurface.controlPoints[0])
 *
 *              // --- Match face edges to body.intersectionEdges ---
 *              face.adjacentEdges = evaluateQuery(context, qAdjacent(face.faceQuery, AdjacencyType.EDGE, EntityType.EDGE));
 *              face.adjacentEdgeIndices = [];
 *
 *              for (var fae = 0; fae < size(face.adjacentEdges); fae += 1)
 *              {
 *                  var faeBSpline = evApproximateBSplineCurve(context, face.adjacentEdges[fae]);
 *                  var numFAEctrl = size(faeBSpline.controlPoints);
 *                  var firstFAE = faeBSpline.controlPoints[0];
 *                  var lastFAE = faeBSpline.controlPoints[numFAEctrl - 1];
 *                  var averageFAE = average(faeBSpline.controlPoints);
 *
 *                  for (var bie = 0; bie < size(body.intersectionEdges); bie += 1)
 *                  {
 *                      var bieData = body.intersectionEdges[bie];
 *
 *                      if (bieData.dimension != numFAEctrl)
 *                          continue;
 *
 *                      // --- ROBUSTNESS: Check both forward and reversed endpoint matching ---
 *                      var forwardMatch = (norm(bieData.firstCP - firstFAE) < 1e-6 * meter
 *                                       && norm(bieData.lastCP - lastFAE) < 1e-6 * meter);
 *                      var reverseMatch = (norm(bieData.firstCP - lastFAE) < 1e-6 * meter
 *                                       && norm(bieData.lastCP - firstFAE) < 1e-6 * meter);
 *
 *                      if (forwardMatch || reverseMatch)
 *                      {
 *                          if (norm(bieData.averageCP - averageFAE) < 1e-6 * meter)
 *                          {
 *                              face.adjacentEdgeIndices = append(face.adjacentEdgeIndices, bie);
 *
 *                              if body.intersectionEdges[bie] does not have 'adjacentFaces' key:
 *                                  body.intersectionEdges[bie]['adjacentFaces'] = [f];
 *                              else:
 *                                  body.intersectionEdges[bie]['adjacentFaces'] = append(..., f);
 *                          }
 *                      }
 *                  }
 *              }
 *
 *              body.intersectionFaces = append(body.intersectionFaces, face);
 *          }
 *
 *          // =================================================================
 *          // PER-PLANE INTERSECTION: iterate planes for each face of this body
 *          // =================================================================
 *
 *          for (f = 0; f < size(body.intersectionFaces); f += 1)
 *          {
 *              var face = body.intersectionFaces[f];
 *
 *              for (var planeIndex in body.intersectionPlanes)
 *              {
 *                  var plane = analysisMap.crossSections[planeIndex].plane;
 *
 *                  // --- EARLY EXIT: skip face if its bounding box doesn't intersect this plane ---
 *                  // Signed distance from bbox corners to plane: if all corners are on the
 *                  // same side (all positive or all negative), the face can't intersect.
 *                  var dMin = dot(face.bbox.min - plane.origin, plane.normal);
 *                  var dMax = dot(face.bbox.max - plane.origin, plane.normal);
 *                  // For a full check, test all 8 corners. Quick conservative check:
 *                  // if the projection of the bbox extent onto the plane normal doesn't
 *                  // span zero, skip this face for this plane.
 *                  var halfExtent = face.bbox.max - face.bbox.min;
 *                  var radius = 0.5 * (abs(halfExtent[0] * plane.normal[0])
 *                                    + abs(halfExtent[1] * plane.normal[1])
 *                                    + abs(halfExtent[2] * plane.normal[2]));
 *                  var centerDist = dot(0.5 * (face.bbox.min + face.bbox.max) - plane.origin, plane.normal);
 *                  if (abs(centerDist) > radius + 1e-6 * meter)
 *                      continue;  // face bbox doesn't intersect this plane
 *
 *                  // =================================================================
 *                  // STEP 1: Approximate intersection curve from surface control points
 *                  // =================================================================
 *                  //
 *                  // Compute signed distance from every surface CP to the plane.
 *                  // We preserve the [i][j] grid index so we can walk iso-curves.
 *
 *                  var cpGrid = face.BSplineSurface.controlPoints;  // cpGrid[i][j]
 *                  var numU = size(cpGrid);
 *                  var numV = size(cpGrid[0]);
 *
 *                  // Build distance grid (same shape as cpGrid)
 *                  var distGrid = [];  // distGrid[i][j] = signed distance
 *                  for (var i = 0; i < numU; i += 1)
 *                  {
 *                      var row = [];
 *                      for (var j = 0; j < numV; j += 1)
 *                      {
 *                          row = append(row, dot(cpGrid[i][j] - plane.origin, plane.normal));
 *                      }
 *                      distGrid = append(distGrid, row);
 *                  }
 *
 *                  // --- Walk iso-curves in the piercing direction to find approximate intersection points ---
 *                  //
 *                  // If useParam == USE_U:
 *                  //   "Piercing curves" are u-curves (rows: cpGrid[*][j] for fixed j).
 *                  //   There are numV such curves, each with numU control points.
 *                  //   For each u-curve, walk along i looking for sign changes in distGrid[i][j].
 *                  //
 *                  // If useParam == USE_V:
 *                  //   "Piercing curves" are v-curves (columns: cpGrid[i][*] for fixed i).
 *                  //   There are numU such curves, each with numV control points.
 *                  //   For each v-curve, walk along j looking for sign changes in distGrid[i][j].
 *                  //
 *                  // For each iso-curve, find the first sign change (or on-plane point),
 *                  // linearly interpolate between the bracketing CPs to get one intersection point.
 *                  // This yields one point per iso-curve, naturally ordered, no deduplication needed.
 *
 *                  var ctrlPoints = [];
 *
 *                  if (face.useParam == USE_U)
 *                  {
 *                      // Walk each v-index (j), scanning across u-indices (i)
 *                      for (var j = 0; j < numV; j += 1)
 *                      {
 *                          var found = false;
 *                          for (var i = 0; i < numU - 1; i += 1)
 *                          {
 *                              var d0 = distGrid[i][j];
 *                              var d1 = distGrid[i + 1][j];
 *
 *                              if (abs(d0) < 1e-6 * meter)
 *                              {
 *                                  ctrlPoints = append(ctrlPoints, cpGrid[i][j]);
 *                                  found = true;
 *                                  break;
 *                              }
 *
 *                              if (d0 * d1 < 0)  // sign change → crossing
 *                              {
 *                                  var t = d0 / (d0 - d1);  // parameter at zero crossing
 *                                  var interpPoint = (1 - t) * cpGrid[i][j] + t * cpGrid[i + 1][j];
 *                                  ctrlPoints = append(ctrlPoints, interpPoint);
 *                                  found = true;
 *                                  break;
 *                              }
 *                          }
 *                          // Check last CP
 *                          if (!found && abs(distGrid[numU - 1][j]) < 1e-6 * meter)
 *                          {
 *                              ctrlPoints = append(ctrlPoints, cpGrid[numU - 1][j]);
 *                          }
 *                          // If !found and last CP not on plane, this iso-curve doesn't cross the plane.
 *                          // This can happen for faces where the plane only clips a corner.
 *                          // We skip this iso-curve (ctrlPoints will have fewer than numV entries).
 *                      }
 *                  }
 *                  else  // USE_V
 *                  {
 *                      // Walk each u-index (i), scanning across v-indices (j)
 *                      for (var i = 0; i < numU; i += 1)
 *                      {
 *                          var found = false;
 *                          for (var j = 0; j < numV - 1; j += 1)
 *                          {
 *                              var d0 = distGrid[i][j];
 *                              var d1 = distGrid[i][j + 1];
 *
 *                              if (abs(d0) < 1e-6 * meter)
 *                              {
 *                                  ctrlPoints = append(ctrlPoints, cpGrid[i][j]);
 *                                  found = true;
 *                                  break;
 *                              }
 *
 *                              if (d0 * d1 < 0)
 *                              {
 *                                  var t = d0 / (d0 - d1);
 *                                  var interpPoint = (1 - t) * cpGrid[i][j] + t * cpGrid[i][j + 1];
 *                                  ctrlPoints = append(ctrlPoints, interpPoint);
 *                                  found = true;
 *                                  break;
 *                              }
 *                          }
 *                          if (!found && abs(distGrid[i][numV - 1]) < 1e-6 * meter)
 *                          {
 *                              ctrlPoints = append(ctrlPoints, cpGrid[i][numV - 1]);
 *                          }
 *                      }
 *                  }
 *
 *                  // If we got fewer than 2 points, this face doesn't meaningfully intersect this plane. Skip.
 *                  if (size(ctrlPoints) < 2)
 *                      continue;
 *
 *                  var knotVector = approximateKnotVector(ctrlPoints);  // arc-length parameterized
 *                  var intersectionSpline = assembleBSpline(ctrlPoints, knotVector);
 *
 *
 *                  // =================================================================
 *                  // STEP 2: Find exact edge-plane intersection points
 *                  // =================================================================
 *                  //
 *                  // Filter to face edges that actually cross this plane.
 *                  // Use adjacentEdgeIndices to look up precomputed edge B-spline data.
 *
 *                  var faceIntersectionEdges = [];
 *                  for (var aei = 0; aei < size(face.adjacentEdgeIndices); aei += 1)
 *                  {
 *                      var edgeIdx = face.adjacentEdgeIndices[aei];
 *                      var edgeData = body.intersectionEdges[edgeIdx];
 *
 *                      // Quick check: does this edge cross the plane?
 *                      // Use precomputed firstCP/lastCP signed distances as a fast filter,
 *                      // but an edge can curve back across the plane so we need to check all CPs.
 *                      var edgeCPs = edgeData.BSplineCurve.controlPoints;
 *                      var hasPositive = false;
 *                      var hasNegative = false;
 *                      var hasOnPlane = false;
 *                      for (var cp in edgeCPs)
 *                      {
 *                          var d = dot(cp - plane.origin, plane.normal);
 *                          if (abs(d) < 1e-6 * meter)
 *                              hasOnPlane = true;
 *                          else if (d > 0)
 *                              hasPositive = true;
 *                          else
 *                              hasNegative = true;
 *                      }
 *
 *                      if (!hasPositive && !hasNegative && !hasOnPlane)
 *                          continue;  // shouldn't happen, but guard
 *                      if (!hasPositive && !hasOnPlane)
 *                          continue;  // all CPs on negative side
 *                      if (!hasNegative && !hasOnPlane)
 *                          continue;  // all CPs on positive side
 *
 *                      // Edge crosses or touches the plane → find exact intersection(s)
 *                      var intersections = getPlaneBSplineIntersections(plane, edgeData.BSplineCurve);
 *                      // See APPENDIX A below for getPlaneBSplineIntersections implementation
 *
 *                      if (size(intersections) > 0)
 *                      {
 *                          faceIntersectionEdges = append(faceIntersectionEdges, {
 *                              'edgeIdx' : edgeIdx,
 *                              'edgeData' : edgeData,
 *                              'planeIntersections' : intersections  // array of { 'point', 'param' }
 *                          });
 *                      }
 *                  }
 *
 *
 *                  // =================================================================
 *                  // STEP 3: Protect against tangent and corner intersections
 *                  // =================================================================
 *                  //
 *                  // Collect ALL intersection points from all face edges into a single list.
 *
 *                  var allEdgeIntersections = [];
 *                  for (var r = 0; r < size(faceIntersectionEdges); r += 1)
 *                  {
 *                      for (var x = 0; x < size(faceIntersectionEdges[r].planeIntersections); x += 1)
 *                      {
 *                          var ipt = faceIntersectionEdges[r].planeIntersections[x];
 *                          allEdgeIntersections = append(allEdgeIntersections, {
 *                              'point' : ipt.point,
 *                              'param' : ipt.param,       // parameter on the EDGE
 *                              'edgeIdx' : faceIntersectionEdges[r].edgeIdx,
 *                              'sourceIdx' : r             // index into faceIntersectionEdges
 *                          });
 *                      }
 *                  }
 *
 *                  // --- Merge near-duplicate points (corner intersections) ---
 *                  // Two edges meeting at a vertex will both report an intersection at that vertex.
 *                  // Merge points within tolerance, keeping one representative.
 *                  var mergedIntersections = [];
 *                  for (var m = 0; m < size(allEdgeIntersections); m += 1)
 *                  {
 *                      var isDuplicate = false;
 *                      for (var existing in mergedIntersections)
 *                      {
 *                          if (norm(existing.point - allEdgeIntersections[m].point) < 1e-5 * meter)
 *                          {
 *                              isDuplicate = true;
 *                              break;
 *                          }
 *                      }
 *                      if (!isDuplicate)
 *                          mergedIntersections = append(mergedIntersections, allEdgeIntersections[m]);
 *                  }
 *
 *                  // --- Handle tangent intersections (odd count after merge) ---
 *                  // If odd: likely a tangent touch. Check if any point has near-zero
 *                  // crossing angle (edge direction nearly parallel to plane).
 *                  // Remove tangent-touch points (they don't define a real entry/exit pair).
 *                  if (size(mergedIntersections) % 2 != 0)
 *                  {
 *                      // Compute crossing angle for each intersection
 *                      // A tangent touch will have dot(edgeTangent, planeNormal) ≈ 0
 *                      var tangentThreshold = 0.05;  // ~3 degrees from parallel
 *                      var filtered = [];
 *                      for (var mi in mergedIntersections)
 *                      {
 *                          // Evaluate edge tangent at intersection parameter
 *                          // (use de Casteljau or derivative of B-spline at mi.param)
 *                          var tangent = evaluateBSplineDerivative(
 *                              body.intersectionEdges[mi.edgeIdx].BSplineCurve, mi.param);
 *                          var crossingAngle = abs(dot(normalize(tangent), plane.normal));
 *                          if (crossingAngle > tangentThreshold)
 *                              filtered = append(filtered, mi);
 *                          // else: tangent touch, discard
 *                      }
 *                      mergedIntersections = filtered;
 *                  }
 *
 *                  // If still odd after filtering, or < 2 intersections, skip this face for this plane.
 *                  // Log a warning — this face has degenerate geometry at this plane.
 *                  if (size(mergedIntersections) < 2 || size(mergedIntersections) % 2 != 0)
 *                  {
 *                      println("WARNING: face " ~ f ~ " has " ~ size(mergedIntersections)
 *                              ~ " intersections at plane " ~ planeIndex ~ ". Skipping.");
 *                      continue;
 *                  }
 *
 *
 *                  // =================================================================
 *                  // STEP 4: Estimate fit-parameters and form spans
 *                  // =================================================================
 *                  //
 *                  // Project each merged intersection point onto the approximate
 *                  // intersectionSpline to get a fit parameter (0..1).
 *
 *                  for (var mi = 0; mi < size(mergedIntersections); mi += 1)
 *                  {
 *                      var fitParam = estimateParam(mergedIntersections[mi].point, intersectionSpline);
 *                      // Clamp to [0, 1]. We have this utility in tools.
 *                      fitParam = clamp(fitParam, 0, 1);
 *                      mergedIntersections[mi]['fitParam'] = fitParam;
 *                  }
 *
 *                  // Sort by fitParam and pair into spans: (0,1), (2,3), (4,5), ...
 *                  mergedIntersections = sort(mergedIntersections, function(a, b) { return a.fitParam - b.fitParam; });
 *
 *                  var paramSpans = [];
 *                  for (var sp = 0; sp < size(mergedIntersections) - 1; sp += 2)
 *                  {
 *                      paramSpans = append(paramSpans, {
 *                          'startPoint' : mergedIntersections[sp].point,
 *                          'startParam' : mergedIntersections[sp].fitParam,
 *                          'endPoint' : mergedIntersections[sp + 1].point,
 *                          'endParam' : mergedIntersections[sp + 1].fitParam
 *                      });
 *                  }
 *
 *
 *                  // =================================================================
 *                  // STEP 5: Build final splines from exact endpoints + sampled interior
 *                  // =================================================================
 *
 *                  for (var sp = 0; sp < size(paramSpans); sp += 1)
 *                  {
 *                      var startParam = paramSpans[sp].startParam;
 *                      var endParam = paramSpans[sp].endParam;
 *                      var startPoint = paramSpans[sp].startPoint;
 *                      var endPoint = paramSpans[sp].endPoint;
 *
 *                      // Sample interior points from the approximate intersection spline.
 *                      // We don't trust the spline endpoints (they may not sit on face edges),
 *                      // so we replace them with the exact edge-plane intersection points.
 *                      var numEvalPoints = face.intersectionDimension > 3
 *                                        ? face.intersectionDimension - 2
 *                                        : 1;
 *                      var paramStep = (endParam - startParam) / (numEvalPoints + 1);
 *                      var evalParams = [];
 *                      if (numEvalPoints > 1)
 *                          evalParams = range(startParam + paramStep, endParam - paramStep, numEvalPoints);
 *                      else
 *                          evalParams = [(startParam + endParam) / 2];
 *
 *                      var fitPoints = evaluateSpline(context, { 'spline' : intersectionSpline, 'parameters' : evalParams })[0];
 *
 *                      var splinePoints = concatenateArrays([[startPoint], fitPoints, [endPoint]]);
 *
 *                      // Re-fit through exact endpoints + interior samples.
 *                      // interpolationIndices: [0, size(splinePoints) - 1] to pin endpoints.
 *                      var finalSpline = approximateSpline(splinePoints,
 *                          { interpolationIndices : [0, size(splinePoints) - 1] })[0];
 *
 *                      // --- UPDATED: Store with bodies array (plural) for shared-edge merging later ---
 *                      analysisMap.crossSections[planeIndex].intersectionCurves = append(
 *                          analysisMap.crossSections[planeIndex].intersectionCurves, {
 *                              'BSplineCurve' : finalSpline,
 *                              'bodies' : [b],          // array of body indices — starts as one, may grow during dedup
 *                              'bodyIndices' : [b],     // kept for backward compat with existing dedup logic
 *                              'faceIdx' : f,
 *                              'sourceFaceBbox' : face.bbox  // carry forward for debug
 *                          }
 *                      );
 *                  }
 *
 *              } // end iterating over planeIndex for this face
 *          } // end iterating over faces for this body
 *
 *          // Store preprocessed body data back
 *          analysisMap.bodies[b] = body;
 *
 *      } // end iterating over each body
 *
 *
 *      // =====================================================================
 *      // POST-PROCESSING: per cross-section analysis
 *      // =====================================================================
 *      //
 *      // Now all cross-sections have their intersectionCurves populated.
 *      // Process each cross-section for unique curves, meshing, and properties.
 *
 *      for (var cs = 0; cs < size(analysisMap.crossSections); cs += 1)
 *      {
 *          var enforceUnique = definition.createComposites;
 *          var planeIntersectionCurves = analysisMap.crossSections[cs].intersectionCurves;
 *
 *          // --- UPDATED: Use adjacentBodies for smarter dedup ---
 *          // Only check curves from adjacent bodies for overlap. Each curve
 *          // carries bodies[]. When two curves from adjacent bodies match,
 *          // merge into one curve with bodies = union of both body lists.
 *          var processedIntersectionCurves = enforceUnique
 *              ? extractUniqueBSplines(planeIntersectionCurves, analysisMap.bodies)
 *                // extractUniqueBSplines uses body.adjacentBodies to limit pairwise
 *                // comparisons. When a match is found, the surviving curve gets:
 *                //   curve.bodies = union(curveA.bodies, curveB.bodies)
 *                //   curve.bodyIndices = union(curveA.bodyIndices, curveB.bodyIndices)
 *              : planeIntersectionCurves;
 *
 *          var csBodyData = [];
 *          for (var bi = 0; bi < size(analysisMap.crossSections[cs].bodies); bi += 1)
 *          {
 *              var bodyIdx = analysisMap.crossSections[cs].bodies[bi];
 *              var bodyCrossSectionData = extractBodyCrossSectionData(processedIntersectionCurves, bodyIdx);
 *              // extractBodyCrossSectionData: filter to curves where bodyIdx is in curve.bodies,
 *              // extract perimeter points, run ear clipping / CDT, compute area, moments of inertia.
 *
 *              if (material data is available for analysisMap.bodies[bodyIdx])
 *              {
 *                  bodyCrossSectionData = mergeMaps(bodyCrossSectionData,
 *                      addMaterialInfo(analysisMap.bodies[bodyIdx].materialData, bodyCrossSectionData));
 *                  // lineal density, part ABD matrices.
 *                  // May need cross-section width (max Y extent) and thickness (max Z extent in-plane).
 *                  // Both are straightforward to extract from perimeter points at this stage.
 *              }
 *              csBodyData = append(csBodyData, bodyCrossSectionData);
 *          }
 *
 *          analysisMap.crossSections[cs].metaData = getCrossSectionMetadata(csBodyData);
 *          // Returns:
 *          // {
 *          //     'EI' : bending stiffness,
 *          //     'rho' : lineal density,
 *          //     'neutralAxis' : neutral axis height,
 *          //     'beamWidth' : beam width,
 *          //     'beamThickness' : beam thickness,
 *          //     'bodyDataMap' : map keyed by body_index → {
 *          //         area, moments, triangles, groups, width, thickness, etc.
 *          //     }
 *          // }
 *          //
 *          // Assign body-level data back:
 *          for (var bdKey in keys(analysisMap.crossSections[cs].metaData.bodyDataMap))
 *          {
 *              // Append or merge this cross-section's body data into analysisMap.bodies[bdKey]
 *              // e.g. analysisMap.bodies[bdKey].crossSectionResults[cs] = bodyDataMap[bdKey]
 *          }
 *      }
 *
 *      // Cache data as necessary (setVariable for editing logic round-trip)
 *      // If not in editing logic: set data on origin
 *      // If createComposites: extract and name composite parts from unique B-splines
 *      // If definition.debug: run debug module
 *
 *
 *  =====================================================================
 *  APPENDIX A: getPlaneBSplineIntersections(plane, bSplineCurve)
 *  =====================================================================
 *
 *  Finds all points where a B-spline curve crosses a plane.
 *  Returns array of { 'point' : Vector, 'param' : number }
 *
 *  Approach: Walk control points, detect sign changes in signed distance,
 *  then refine with Newton iterations.
 *
 *  function getPlaneBSplineIntersections(plane is Plane, curve is BSplineCurve) returns array
 *  {
 *      var cps = curve.controlPoints;
 *      var n = size(cps);
 *      var knots = curve.knots;        // or knotVector depending on FS representation
 *      var degree = curve.degree;
 *
 *      // Step 1: Compute signed distance for each CP
 *      var cpDists = [];
 *      for (var i = 0; i < n; i += 1)
 *          cpDists = append(cpDists, dot(cps[i] - plane.origin, plane.normal));
 *
 *      // Step 2: Find parameter intervals containing sign changes
 *      // Use Greville abscissae as approximate parameter locations for each CP.
 *      // Greville abscissa for CP[i] = average of knots[i+1] through knots[i+degree]
 *      var greville = [];
 *      for (var i = 0; i < n; i += 1)
 *      {
 *          var sum = 0;
 *          for (var k = 1; k <= degree; k += 1)
 *              sum += knots[i + k];       // NOTE: adjust indexing for FS knot representation
 *          greville = append(greville, sum / degree);
 *      }
 *
 *      // Step 3: Walk CPs, find sign changes
 *      var candidates = [];  // array of { paramGuess, dist0, dist1 }
 *      for (var i = 0; i < n - 1; i += 1)
 *      {
 *          if (abs(cpDists[i]) < 1e-8 * meter)
 *          {
 *              // CP is on the plane — use its Greville parameter directly
 *              candidates = append(candidates, { 'paramGuess' : greville[i], 'isExact' : true });
 *          }
 *          else if (cpDists[i] * cpDists[i + 1] < 0)
 *          {
 *              // Sign change between CP[i] and CP[i+1]
 *              // Linear interpolation for initial guess
 *              var t = cpDists[i] / (cpDists[i] - cpDists[i + 1]);
 *              var paramGuess = (1 - t) * greville[i] + t * greville[i + 1];
 *              candidates = append(candidates, { 'paramGuess' : paramGuess, 'isExact' : false });
 *          }
 *      }
 *      // Check last CP
 *      if (abs(cpDists[n - 1]) < 1e-8 * meter)
 *          candidates = append(candidates, { 'paramGuess' : greville[n - 1], 'isExact' : true });
 *
 *      // Step 4: Newton-Raphson refinement for each candidate
 *      var results = [];
 *      var maxIter = 10;
 *      var tolerance = 1e-8 * meter;
 *
 *      for (var c in candidates)
 *      {
 *          var param = c.paramGuess;
 *
 *          if (!c.isExact)
 *          {
 *              for (var iter = 0; iter < maxIter; iter += 1)
 *              {
 *                  // Evaluate curve position and first derivative at param
 *                  // C(t) and C'(t) — use de Boor's algorithm or evaluateSpline
 *                  var pos = evaluateBSplineAtParam(curve, param);       // 3D point
 *                  var deriv = evaluateBSplineDerivAtParam(curve, param); // 3D tangent
 *
 *                  var f = dot(pos - plane.origin, plane.normal);   // scalar: signed distance
 *                  var df = dot(deriv, plane.normal);               // scalar: rate of change
 *
 *                  if (abs(f) < tolerance)
 *                      break;
 *
 *                  if (abs(df) < 1e-12)
 *                      break;  // tangent to plane — can't refine further
 *
 *                  param = param - f / df;
 *
 *                  // Clamp to curve parameter range
 *                  param = clamp(param, knots[degree], knots[n]);
 *              }
 *          }
 *
 *          var finalPoint = evaluateBSplineAtParam(curve, param);
 *
 *          // Verify the point is actually on the plane (Newton may not have converged)
 *          if (abs(dot(finalPoint - plane.origin, plane.normal)) < 1e-6 * meter)
 *          {
 *              results = append(results, { 'point' : finalPoint, 'param' : param });
 *          }
 *      }
 *
 *      // Step 5: Deduplicate results (same edge may yield near-identical roots
 *      //         from adjacent CP sign changes converging to the same crossing)
 *      var deduped = [];
 *      for (var r in results)
 *      {
 *          var isDup = false;
 *          for (var d in deduped)
 *          {
 *              if (abs(r.param - d.param) < 1e-8)
 *              {
 *                  isDup = true;
 *                  break;
 *              }
 *          }
 *          if (!isDup)
 *              deduped = append(deduped, r);
 *      }
 *
 *      return deduped;
 *  }
 *
 *
 *  =====================================================================
 *  APPENDIX B: evaluateBSplineDerivative (needed for tangent checks)
 *  =====================================================================
 *
 *  For the tangent intersection filtering in Step 3, we need C'(t).
 *  Two options:
 *    a) Use evaluateSpline with derivatives flag if available in FS
 *    b) Implement derivative B-spline: derivative of degree-p B-spline
 *       is a degree-(p-1) B-spline with CPs:
 *       Q[i] = p * (P[i+1] - P[i]) / (knots[i+p+1] - knots[i+1])
 *       Then evaluate Q at param using standard de Boor.
 *
 *  This is the same derivative we need in Newton refinement above,
 *  so implement once and reuse.
 *
 *
 *  =====================================================================
 *  NOTES
 *  =====================================================================
 *
 *  @function optimization:
 *  We can save quite a bit of time in many evaluations by using @functions,
 *  i.e. calling @evaluateDistance(..) instead of evaluateDistance(..).
 *  See: https://www.smartbenchsoftware.com/post/featurescript-optimization
 *
 *  Great candidates:
 *    - evDistance → @evDistance (returns unitless) - 5% savings
 *    - evApproximateBSplineCurve → @evApproximateBSplineCurve - negligable savings
 *    - evApproximateBSplineSurface → @evApproximateBSplineSurface - negligible savings
 *    - evaluateSpline → can likely remain as-is since it's not a context call - SIGNIFICANT savings (3.2x)
 *    - Unitless math is 6.4x faster (norms, etc)
 *
 *  Note: @function calls return unitless results, so we need to re-apply
 *  units (multiply by meter) after the call. Research on exact behavior
 *  should be completed before beginning this refactor.
 *
 */