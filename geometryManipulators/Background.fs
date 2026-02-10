FeatureScript 2522;
import(path : "onshape/std/common.fs", version : "2522.0");
import(path : "onshape/std/isoparametricCurve.fs", version : "2522.0");

export const edgeSpacingBounds = {(millimeter) : [0.5, 2, 20]} as LengthBoundSpec;

annotation { "Feature Type Name" : "Face Curvatures", "Feature Type Description" : "" }
export const exploreFaceCurvature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.pickedFace is Query;
        
        annotation { "Group Name" : "bSplineSurface", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Show control points", "Default" : false }
            definition.showBControlPoints is boolean;
            
            if (definition.showBControlPoints)
            {
                annotation { "Name" : "Show u, v parameters" }
                definition.printBUV is boolean;
                
            }
            
            annotation { "Name" : "Connect point arrays" }
            definition.connectBctrlPointArray is boolean;
            
            annotation { "Name" : "Connect point indicies" }
            definition.connectBctrlIndex is boolean;

            
        }
        
        annotation { "Group Name" : "boundaryBSplineCurves", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Print boundaryBSplineCurves" }
            definition.printBoundaryBCurves is boolean;
            
            annotation { "Name" : "Show Control Points" }
            definition.showBoundaryControlPoints is boolean;
            
            annotation { "Name" : "Convert control points to world" }
            definition.convertBoundaryControlPoints is boolean;
            
            annotation { "Name" : "Display boundary BSplines" }
            definition.displayBoundaryBsplines is boolean;
            
            
            annotation { "Name" : "Show knot array" }
            definition.showBoundaryKnots is boolean;
            
        }
        
   
        
    }
    {
        
        
        var bSplineSurf = evApproximateBSplineSurface(context, {
                "face" : definition.pickedFace
        });
        
        println('BSPLINESURF: (' ~ toString(keys(bSplineSurf)) ~ ')');
        println('BSPLINESURF.bSplineSurface: ' ~ keys(bSplineSurf.bSplineSurface));
        //println('size(boundaryBSplineCurves): ' ~ toString(size(bSplineSurf.boundaryBSplineCurves)));
        //println('size(innerLoopBSplineCurves): ' ~ toString(size(bSplineSurf.innerLoopBSplineCurves)));
        //println('keys(bSplineSurf.bSplineSurface): ' ~ keys(bSplineSurf.bSplineSurface));
        //println(toString(bSplineSurf));
        
        //println('There are: ' ~ size(bSplineSurf.bSplineSurface.controlPoints) ~ ' sets of points');
        
        println('Control Point Dimension: (' ~ size(bSplineSurf.bSplineSurface.controlPoints) ~ ', ' ~ size(bSplineSurf.bSplineSurface.controlPoints[0]) ~ ')');
        
        for (var i = 0; i < size(bSplineSurf.bSplineSurface.controlPoints); i += 1)
        {
            println('Point set: ' ~ i ~ ' ******');
            for (var pt in bSplineSurf.bSplineSurface.controlPoints[i])
            {
                if (definition.showBControlPoints)
                {
                    addDebugPoint(context, pt, DebugColor.GREEN);
                    println(printVector(pt, 1000, 'mm', 3));
                }
                
                
                if (definition.printBUV)
                {
                    var controlPointDist = evDistance(context, {
                            "side0" : definition.pickedFace,
                            "side1" : pt
                    });
                    
                    var uvparams = controlPointDist.sides[0].parameter;
                    println('[u, v] -> ' ~ printVector(uvparams, 1, '', 3));  
                }
            }
            
            var controlRows = bSplineSurf.bSplineSurface.controlPoints;
            
            
            if (definition.connectBctrlPointArray)
            {
                    for (var p = 0; p < size(controlRows[i]); p += 1)
                {
                    if (p > 0)
                    {
                        if (controlRows[i][p-1] != controlRows[i][p])
                        {
                            addDebugLine(context, controlRows[i][p-1], controlRows[i][p], DebugColor.MAGENTA);   
                        }
                        
                    }   
                }
                
            }
            
            // now connect point[i] of each group.
            
            
            
            var numGroups = size(bSplineSurf.bSplineSurface.controlPoints);
            var numGroupPoints = size(bSplineSurf.bSplineSurface.controlPoints[0]);
            
            if (definition.connectBctrlIndex)
            {
                for (var u = 0; u < numGroupPoints; u += 1)
                {
                    for (var v = 1; v < numGroups; v += 1)
                    {
                        if (v > 0)
                        {
                            addDebugLine(context, bSplineSurf.bSplineSurface.controlPoints[v-1][u], bSplineSurf.bSplineSurface.controlPoints[v][u], DebugColor.CYAN);
                        }   
                    }
                    
                }
                
            }

        }
        
        if (definition.printBoundaryBCurves)
        {
            println('* * * * boundaryBSplineCurves * * * *');
            println('There are ' ~ size(bSplineSurf.boundaryBSplineCurves) ~ ' boundary BSpline Curves');
            for (var i = 0; i < size(bSplineSurf.boundaryBSplineCurves); i += 1)
            {
                println('Boundary BSpline Curve ' ~ i);
                println(keys(bSplineSurf.boundaryBSplineCurves[i]));
                var curveDimension = bSplineSurf.boundaryBSplineCurves[i].dimension;
                println('dimension - > ' ~ curveDimension);
                
                if (definition.showBoundaryControlPoints)
                {
                    var controlPoints = bSplineSurf.boundaryBSplineCurves[i].controlPoints;
                    var worldPoints = [];
                    for (var p = 0; p < size(controlPoints); p += 1)
                    {
                        var ctrlPoint = controlPoints[p];
                        println(ctrlPoint);
                        if (definition.convertBoundaryControlPoints)
                        {
                            var surfPlane = evFaceTangentPlane(context, {
                                    "face" : definition.pickedFace,
                                    "parameter" : ctrlPoint
                            });
                            println(printVector(surfPlane.origin, 1000, 'mm', 3));
                            
                            worldPoints = append(worldPoints, surfPlane.origin);
                            
                            addDebugPoint(context, surfPlane.origin, DebugColor.GREEN);
                        }
                    }
                    
                    if (definition.displayBoundaryBsplines)
                    {
                        var boundaryBSpline = bSplineCurve({
                                "degree" : size(bSplineSurf.boundaryBSplineCurves[i].controlPoints)-1,
                                "isPeriodic" : bSplineSurf.boundaryBSplineCurves[i].isPeriodic,
                                "controlPoints" : worldPoints
                        });
                        
                        opCreateBSplineCurve(context, id + ("BOUNDARYbSplineCurve" ~ i), {
                                "bSplineCurve" : boundaryBSpline
                        });
                        
                        addDebugEntities(context, qCreatedBy(id + ("BOUNDARYbSplineCurve" ~ i), EntityType.EDGE), DebugColor.RED);
                        
                        opDeleteBodies(context, id + ("deleteBodies1" ~ i), {
                                "entities" : qCreatedBy(id + ("BOUNDARYbSplineCurve" ~ i), EntityType.BODY)
                        });
                        
                    }
                       
                }
                   
            }
        }
        
        println('bsplineSurf.bSplineSurface.uKnots -> ' ~ bSplineSurf.bSplineSurface.uKnots);
        println('bsplineSurf.bSplineSurface.vKnots -> ' ~ bSplineSurf.bSplineSurface.vKnots);
        
        
        
        
        
    });
    
annotation { "Feature Type Name" : "Explore Edges", "Feature Type Description" : "" }
export const exploreEdges = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Edges to investigate", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1 }
        definition.exploreEdge is Query;
        
    }
    {
        var aprxBspline = evApproximateBSplineCurve(context, {
                "edge" : definition.exploreEdge
        });
        
        println('curveType: ' ~ aprxBspline.curveType);
        println('degree: ' ~ aprxBspline.degree);
        println('dimension: ' ~ aprxBspline.dimension);
        println('isPeriodic: ' ~ aprxBspline.isPeriodic);
        println('isRational: ' ~ aprxBspline.isRational);
        println('knots: ' ~ aprxBspline.knots);
        println('# Control Points: ' ~ size(aprxBspline.controlPoints));
        
        for (var pt in aprxBspline.controlPoints)
        {
            addDebugPoint(context, pt, DebugColor.MAGENTA);
        }
        
        var newBspline = bSplineCurve({
                "degree" : aprxBspline.degree,
                "isPeriodic" : aprxBspline.isPeriodic,
                "controlPoints" : aprxBspline.controlPoints,
                "knots": aprxBspline.knots
        });
        
        opCreateBSplineCurve(context, id + "bSplineCurve1", {
                "bSplineCurve" : newBspline
        });
        
    });

export function printVector(vec is Vector, scaleFactor is number, suffix is string, precision is number) returns string
{
    var retString = '[ ';
    for (var i = 0; i < size(vec); i += 1)
    {
        if (i > 0)
        {
            retString = retString ~ ', ';
        }
        
        if (scaleFactor == 1)
        {
            retString =  retString ~ toString(roundToPrecision(vec[i], precision)) ~ suffix;   
        }
        else
        {
            retString =  retString ~ toString(roundToPrecision(vec[i].value * scaleFactor, precision)) ~ suffix;   
        }
    }
    
    retString = retString ~ ' ]';
    return retString;
}


