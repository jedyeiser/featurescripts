FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/extend.fs", version : "2892.0");

/**
 * This function generates sidewall rout surfaces given
 * 1. Geometry
 *      a. Ski bottom surface
 *      b. ski side surface
 *      c. Ski reference wire
 * 
 * 2. Setup
 *      a. Distance above the bottom face that the sidewall rout starts
 *      b. SW rout angle
 *      c. (optional) Sw rout stepin
 * OPTIONAL: up to two endpoint queries for SW rout start/stop points. If an endpoint query is provided, cutter radius must be provided.
 *      
 */
 
 export const SWRoutAngleBounds = {(degree) : [0, 20, 45]} as AngleBoundSpec;
 export const DistAboveBottomBounds = {(millimeter) : [1, 4, 10]} as LengthBoundSpec;
 export const SWStepInBounds = {(millimeter) : [0, 0, 3]} as LengthBoundSpec;
 export const cutterRadiusBounds = {(millimeter) :[2, 10, 20]} as LengthBoundSpec;
 
 annotation { "Feature Type Name" : "Sidewall rout surface", "Feature Type Description" : "Creates a SW rout surface based on inputs" }
 export const SWRout = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "Bottom surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1,  "Description" : "Bottom of ski. Surface must extend beyond side surface"}
         definition.bottomSheet is Query;
         
         annotation { "Name" : "Side surface", "Filter" : EntityType.BODY && BodyType.SHEET, "MaxNumberOfPicks" : 1, "Description" : "Side surface of ski. Footprint must be contained by the bottom surface" }
         definition.sideSheet is Query;
         
         annotation { "Name" : "Sidewall rout angle" }
         isAngle(definition.swRoutAngle, SWRoutAngleBounds);
         
         annotation { "Name" : "Bottom surface extension" }
         isLength(definition.bottomExtension, LENGTH_BOUNDS);
         
         annotation { "Name" : "Distance from bottom rout begins" }
         isLength(definition.distAboveBottom, DistAboveBottomBounds);
         
         annotation { "Name" : "SW rout step-in" }
         isLength(definition.swRoutStepin, SWStepInBounds);
         
         annotation { "Name" : "Spec rout endpoints?", "Default" : false }
         definition.specSWRoutEndpoints is boolean;
         
         annotation { "Group Name" : "SW rout endpoint data", "Driving Parameter" : "specSWRoutEndpoints", "Collapsed By Default" : false }
         {
             annotation { "Name" : "Reference curve", "Filter" : BodyType.WIRE, "MaxNumberOfPicks" : 1 }
            definition.refWire is Query;
         
            annotation { "Name" : "Start point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.startPointQ is Query;
         
            annotation { "Name" : "Stop point", "Filter" : EntityType.VERTEX || (EntityType.FACE && GeometryType.PLANE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.stopPointQ is Query;
            
            annotation { "Name" : "Cutter bottom radius" }
            isLength(definition.cutterBottomRadius, cutterRadiusBounds);
            
         }

     }
     {
         
         //assume that 'up' is in positive z and 'in is narrower y. Offsetting bottom is incorrect if box is lower. Offseting side is wrong if box y is smaller. 
         
         //copy bodies - we'll delete later
         
         opPattern(context, id + "copyBottom", {
                 "entities" : definition.bottomSheet,
                 "transforms" : [transform(vector(0, 0, 0) * millimeter)],
                 "instanceNames" : ['1']
         });
         
         opPattern(context, id + "copySide", {
                 "entities" : definition.bottomSheet,
                 "transforms" : [transform(vector(0, 0, 0) * millimeter)],
                 "instanceNames" : ['1']
         });
         
         var toDeleteBottomBody = qCreatedBy(id + "copyBottom", EntityType.BODY);
         var toDeleteSideBody = qCreatedBy(id + "copySide", EntityType.BODY);

        var firstMoveMap = processFirstMoves(context, id + "firstMoves", definition, toDeleteBottomBody, toDeleteSideBody);
         
         var bottomDirSign = firstMoveMap.bottomDirSign;
         var sideDirSign = firstMoveMap.sideDirSign;
         
         opIntersectFaces(context, id + "createStartIntersections", {
                 "tools" : qUnion([qOwnedByBody(toDeleteBottomBody, EntityType.FACE)]),
                 "targets" : qUnion([qOwnedByBody(toDeleteSideBody, EntityType.FACE)])
         });
         
         opExtractWires(context, id + "extractStartWire", {
                 "edges" : qUnion([qCreatedBy(id + "createStartIntersections", EntityType.EDGE)])
         });
         
         var startWire = qCreatedBy(id + "extractStartWire", EntityType.BODY);
         
         opDeleteBodies(context, id + "deleteStartSeeds", {
                 "entities" : qUnion([qCreatedBy(id + "createStartIntersections", EntityType.BODY)])
         });
         
         var dummyTopSurf = generateDummyTopSurf(context, id + "generateDummyTopSurf", definition.sideSheet, definition.bottomSheet, bottomDirSign);
         
         var gapDist = evDistance(context, {
                 "side0" : dummyTopSurf,
                 "side1" : definition.bottomSheet
         }).distance;
         
         var stepInWire = qNothing();
         if (definition.swRoutStepin > 0 * millimeter)
         {
            opOffsetFace(context, id + "swStepInOffset", {
                    "moveFaces" : toDeleteSideBody,
                    "offsetDistance" : sideDirSign * definition.swRoutStepin
            });
            
            opIntersectFaces(context, id + "intersectStepinSurfaces", {
                    "tools" : toDeleteSideBody,
                    "targets" : toDeleteBottomBody
            });
            
            opExtractWires(context, id + "extractStepInWire", {
                    "edges" : qUnion([qCreatedBy(id + "intersectStepinSurfaces", EntityType.EDGE)])
            });
            
            opDeleteBodies(context, id + "deleteStepeinIntersections", {
                    "entities" : qUnion([qCreatedBy(id + "intersectStepinSurfaces", EntityType.BODY)])
            });
            
            stepInWire = qCreatedBy(id + "extractStepInWire", EntityType.BODY);
         }
         
         var routHeight = gapDist - definition.distAboveBottom + 2 * millimeter; // go 2mm above, then split to trim. 
         var routOffset = routHeight * tan(definition.swRoutAngle);
         
         //profile offset
         opOffsetFace(context, id + "finalProfileOffset", {
                 "moveFaces" : qUnion([qOwnedByBody(toDeleteBottomBody, EntityType.FACE)]),
                 "offsetDistance" : bottomDirSign * routHeight
         });
         
         //periphery offset
         opOffsetFace(context, id + "finalPeripheryOffset", {
                 "moveFaces" : qUnion([qOwnedByBody(toDeleteSideBody, EntityType.FACE)]),
                 "offsetDistance" : sideDirSign * routOffset
         });
         
         //intersect
         opIntersectFaces(context, id + "finalIntersection", {
                 "tools" : qUnion([qOwnedByBody(toDeleteBottomBody, EntityType.FACE)]),
                 "targets" : qUnion([qOwnedByBody(toDeleteSideBody, EntityType.FACE)])
         });
         
         //extract
         opExtractWires(context, id + "finalWireExtract", {
                 "edges" : qCreatedBy(id + "finalIntersection", EntityType.EDGE)
         });
         
         var stopWire = qCreatedBy(id + "finalWireExtract", EntityType.BODY);
         
         opDeleteBodies(context, id + "finalIntersectionDelete", {
                 "entities" : qCreatedBy(id + "finalIntersection", EntityType.BODY)
         });
         
         //deleteSurfs
         opDeleteBodies(context, id + "deleteWorkerFaces", {
                 "entities" : qUnion([toDeleteBottomBody, toDeleteSideBody])
         });
         
         opSplitPart(context, id + "centerWireSplit", {
                 "targets" : qUnion([startWire, stepInWire, stopWire]),
                 "tool" : qFrontPlane(EntityType.FACE),
                 "keepType" : SplitOperationKeepType.KEEP_FRONT
         });
         
         opLoft(context, id + "loft1", {
                 "profileSubqueries" : [ stopWire, (definition.swRoutStepin > 0 * millimeter) ? stepInWire : startWire  ],
                 "connections" : [],
                 "bodyType" : ToolBodyType.SURFACE
                 }
            );
            
        if (definition.swRoutStepin > 0 * millimeter)
        {
            opLoft(context, id + "loft2", {
                 "profileSubqueries" : [ stepInWire, startWire  ],
                 "connections" : [],
                 "bodyType" : ToolBodyType.SURFACE
                 }
            );  
            
            opBoolean(context, id + "combineSurfs", {
                    "tools" : qUnion([qCreatedBy(id + "loft1", EntityType.BODY), qCreatedBy(id + "loft2", EntityType.BODY)]),
                    "operationType" : BooleanOperationType.UNION
            });
        }
        
        var loftBody = qCreatedBy(id + "loft1", EntityType.BODY);
        var loftEdgeOptions = evaluateQuery(context, qEdgeTopologyFilter(qUnion([qOwnedByBody(loftBody, EntityType.EDGE)]), EdgeTopology.ONE_SIDED));
        
        var outsideEdges = [];
        for (var i = 0; i < size(loftEdgeOptions); i += 1)
        {
            var midPoint = evEdgeTangentLine(context, {
                    "edge" : loftEdgeOptions[i],
                    "parameter" : 0.5
            }).origin;
            
            var midPointDist = evDistance(context, {
                    "side0" : midPoint,
                    "side1" : definition.sideSheet
            }).distance;
            
            if (midPointDist < 1e-5 * 1 * meter)
            {
                outsideEdges = append(outsideEdges, loftEdgeOptions[i]);  
            }
        }
        
        var extendDistance = (definition.specSWRoutEndpoints) ? definition.cutterBottomRadius : definition.bottomExtension;
        
        if (extendDistance > 0 * millimeter)
        {
            extendSurface(context, id + "extendBottom", {
                "entities" : qUnion(outsideEdges),
                "tangentPropagation" : true,
                "endCondition" : ExtendBoundingType.BLIND,
                "oppositeDirection" : false,
                "extendDistance" : 0 * millimeter,
                "maintainCurvature" : true
                });
        }
        
        //processed. Now we can trim if needed.
        if (definition.specSWRoutEndpoints)
        {
            var routBox = evBox3d(context, {
                    "topology" : loftBody,
                    "tight" : true
            });
            var keepRefPoint = (routBox.minCorner + routBox.maxCorner)/2;
            
            var refWirePath = constructPath(context, qUnion([qOwnedByBody(definition.refWire, EntityType.EDGE)]));
        }
        
        //trim the top. delete top dummy surface and wires
        
        //mirror. If bodies touch, boolean
        
        
         
     });
     
 export function trimSWRout(context is Context, id is Id, swRoutSurface is Query, refPath is Path, trimPoint is Query, keepRefPoint is Query)
 {
     var refPathDist = evDistance(context, {
             "side0" : refPath.edges,
             "side1" : vector(0, 0, 0) * meter
     });
     
     var refEdge = refPath.edges[refPathDist.sides[0].index];
     var refParam = refPathDist.sides[0].parameter;
     var edgeLine = evEdgeTangentLine(context, {
             "edge" : refEdge,
             "parameter" : refParam
     });
     
     var splitPlane = plane(edgeLine.origin, edgeLine.direction);
     
     opSplitPart(context, id + "splitSWRout", {
             "targets" : swRoutSurface,
             "tool" : splitPlane
     });
     
     var splitBodyTrue = qSplitBy(id + "splitSWRout", EntityType.BODY, true);
     var splitBodyFalse = qSplitBy(id + "splitSWRout", EntityType.BODY, false);
     
     var splitBodyTrueDist = evDistance(context, {
             "side0" : splitBodyTrue,
             "side1" : keepRefPoint
     }).distance;
     
     var splitBodyFalseDist = evDistance(context, {
             "side0" : splitBodyFalse,
             "side1" : keepRefPoint
     }).distance;
     
     if (splitBodyTrueDist > splitBodyFalseDist)
     {
         opDeleteBodies(context, id + "deleteSWSplitTrue", {
                 "entities" : splitBodyTrue
         });
     }
     else
     {
        opDeleteBodies(context, id + "deleteSWSplitFalse", {
                "entities" : splitBodyFalse
        });
     }
     
     var sweepEdges = qUnion([qCreatedBy(id + "splitSWRout", EntityType.EDGE)]);
     var sweepEdgesBox = evBox3d(context, {
             "topology" : sweepEdges,
             "tight" : true
     });
     
     var sweepAxisSearchPoint = vector(sweepEdgesBox.maxCorner[0], sweepEdgesBox.maxCorner[1], sweepEdgesBox.minCorner[2]);
 }
     
 export function generateDummyTopSurf(context is Context, id is Id, sideSheet is Query, bottomSheet is Query, bottomDirSign is number) returns Query
 {
     var sheetEdges = qUnion([qOwnedByBody(sideSheet, EntityType.EDGE)]);
     var targetEdges = qEdgeTopologyFilter(sheetEdges, EdgeTopology.ONE_SIDED);
     
     opExtractWires(context, id + "extractTopBottomWires", {
             "edges" : targetEdges
     });
     
     var wireBodies = evaluateQuery(context, qCreatedBy(id + "extractTopBottomWires", EntityType.BODY));
     
     var wireDist = evDistance(context, {
             "side0" : qUnion([qOwnedByBody(wireBodies[0], EntityType.EDGE)]),
             "side1" : qUnion([qOwnedByBody(wireBodies[1], EntityType.EDGE)])
     }).distance;
     
     opPattern(context, id + "copyBottomForDummyTop", {
             "entities" : bottomSheet,
             "transforms" : [transform(vector([0, 0, 0]) * millimeter)],
             "instanceNames" : ['1']
     });
     
     var dummyTop = qCreatedBy(id + "copyBottomForDummyTop", EntityType.BODY);
     
     opOffsetFace(context, id + "offsetBottomToTop", {
             "moveFaces" : qUnion([qOwnedByBody(dummyTop, EntityType.FACE)]),
             "offsetDistance" : wireDist * bottomDirSign
     });
     
     return dummyTop;
 }
     
 export function processFirstMoves(context is Context, id is Id, definition is map, toDeleteBottomBody is Query, toDeleteSideBody is Query) returns map
 {
     var bottomDirSign = 1;
     var sideDirSign = 1;
     
     var initialBottomBox = evBox3d(context, {
             "topology" : definition.bottomSheet,
             "tight" : true
     });
     
     var initialSideBox = evBox3d(context, {
             "topology" : definition.sideSheet,
             "tight" : true
     });
     
     var initialBottomBoxZ = initialBottomBox.minCorner[2];
     var initialSideWidth = initialSideBox.maxCorner[1] - initialSideBox.minCorner[1];
     
     // we now know where our initial faces were. We choose positive offsets initially and check to make sure we're offsetting
     // the right direction. If we aren't we reverse the offset to correct and save that direction. 
     
     opOffsetFace(context, id + "initialBottomOffset", {
             "moveFaces" : qOwnedByBody(toDeleteBottomBody, EntityType.BODY),
             "offsetDistance" : bottomDirSign * definition.distAboveBottom
     });
     
     opOffsetFace(context, id + "initialSideOffset", {
             "moveFaces" : qOwnedByBody(toDeleteSideBody, EntityType.BODY),
             "offsetDistance" : sideDirSign * definition.distAboveBottom
     });
     
     var newBottomBox = evBox3d(context, {
             "topology" : toDeleteBottomBody,
             "tight" : true
     });
     
     var newSideBox = evBox3d(context, {
             "topology" : toDeleteSideBody,
             "tight" : true
     });
         
     var newBottomBoxZ = newBottomBox.minCorner[2];
     var newSideWidth = newSideBox.maxCorner[1] - newSideBox.minCorner[1];
     
     if (newBottomBoxZ < initialBottomBoxZ) // we offset in the wrong direction
     {
        bottomDirSign = bottomDirSign * -1;
        opOffsetFace(context, id + "initialBottomOffsetFix", {
             "moveFaces" : qOwnedByBody(toDeleteBottomBody, EntityType.BODY),
             "offsetDistance" : bottomDirSign * 2 * definition.distAboveBottom
        });
        
     }
     
     if (newSideWidth < initialSideWidth) // we offset in the wrong direction. Note sign change, put face back where it should go
     {
         sideDirSign = -1 * sideDirSign;
         opOffsetFace(context, id + "initialSideOffsetFix", {
             "moveFaces" : qOwnedByBody(toDeleteSideBody, EntityType.BODY),
             "offsetDistance" : sideDirSign * definition.distAboveBottom
        });
     }
     else // just put face back
     {
         opOffsetFace(context, id + "initialSideOffsetFixRevert", {
             "moveFaces" : qOwnedByBody(toDeleteSideBody, EntityType.BODY),
             "offsetDistance" : -1 * sideDirSign * definition.distAboveBottom
        });
     }
     
     return {'bottomDir' : bottomDirSign, 'sideDir' : sideDirSign};
 }
 
     
     
     
     
     
 