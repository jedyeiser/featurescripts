FeatureScript 2279;
import(path : "onshape/std/common.fs", version : "2279.0");
import(path : "onshape/std/geometry.fs", version : "2279.0");
import(path : "86da00b8da67e4d203926ccd/8d457ae91af1ab93c763999b/90eb1785f1a4a9a913b14c2d", version : "5eda8d613ec40af50a1b3cad");



export enum EdgeLocationType
{
    DIRECTION,
    NEAREST,
    DEFINED
}

export enum DirectionSpecificationType
{
    QUERY, 
    VECTOR
}


export enum LoftType
{
    MID_PLANE,
    BLIND
}

export const dirInputBounds = {(unitless) : [0, 0, 20]} as RealBoundSpec;

export const surfLoftDistBounds = {(millimeter) : [0.5, 10, 50]} as LengthBoundSpec;

export const pointSpacingBounds = {(millimeter) : [1, 5, 50]} as LengthBoundSpec;
export const aprxDegreeBounds = {(unitless) : [3, 3, 10]} as IntegerBoundSpec;
export const aprxTolBOunds = {(millimeter) : [0.00001, 0.001, 1]} as LengthBoundSpec;
export const aprxMaxCtrlPointBounds = {(unitless) : [5, 20, 100]} as IntegerBoundSpec;


annotation { "Feature Type Name" : "Wrap and Loft", "Feature Type Description" : "Wraps X to S along an edge, lofts a trim surface" }
export const wrapAndLoft = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Seed edges:", "Description" : "Edges which will be wrapped and then lofted", "Filter" : EntityType.EDGE && ConstructionObject.NO}
        definition.selEdges is Query;
        
        annotation { "Name" : "Seed location type:", "Description" : "Method to locate seed edge reference point on wrap profile curve", "Default" : EdgeLocationType.NEAREST, "UIHint" : UIHint.SHOW_LABEL }
        definition.seedLocationType is EdgeLocationType;
        
        annotation { "Name" : "Seed edge reference point:", "Description" : "Reference point to locate seed edges on wrap profile to begin wrapping", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.seedRefPoint is Query;
        
        annotation { "Name" : "Wrap profile:", "Description" : "XZ Profile to wrap onto", "Filter" : EntityType.EDGE && ConstructionObject.NO}
        definition.wrapProfile is Query;
        
        if (definition.seedLocationType == EdgeLocationType.DEFINED)
        {
            annotation { "Name" : "Wrap edge reference point:", "Description" : "Point on wrap profile to align seed reference point to", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
            definition.wrapRefPoint is Query;
            
        }
        
        annotation { "Name" : "Flip wrap direction", "UIHint" : UIHint.OPPOSITE_DIRECTION }
        definition.flipWrapDirection is boolean;
        
        annotation { "Name" : "Keep wrapped curve?" }
        definition.keepWrappedCurve is boolean;
        
        
        if (definition.seedLocationType == EdgeLocationType.DIRECTION)
        {
            annotation { "Group Name" : "Direction definition", "Collapsed By Default" : false }
            {
                annotation { "Name" : "Get direction from:", "Default" : DirectionSpecificationType.QUERY, "UIHint" : UIHint.HORIZONTAL_ENUM }
                definition.dirSpecType is DirectionSpecificationType;
                
                if (definition.dirSpecType == DirectionSpecificationType.QUERY)
                {
                    annotation { "Name" : "Direction query:", "Filter" : (EntityType.FACE && GeometryType.PLANE) || (EntityType.EDGE && GeometryType.LINE) || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
                    definition.dirQuery is Query;
                }
                else if (definition.dirSpecType == DirectionSpecificationType.VECTOR)
                {
                    annotation { "Name" : "X component:", "Icon" : Icon.ALONG_X }
                    isReal(definition.dirX, dirInputBounds);
                    
                    annotation { "Name" : "Y component:", "Icon" : Icon.ALONG_Y }
                    isReal(definition.dirY, dirInputBounds);
                    
                    annotation { "Name" : "Z component:", "Icon" : Icon.ALONG_Z }
                    isReal(definition.dirZ, dirInputBounds);
                }
                
            }
        }
        
        
        annotation { "Group Name" : "Surface Definition", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Surface location on wrapped wire:", "Default" : LoftType.BLIND, "UIHint" : UIHint.HORIZONTAL_ENUM }
            definition.surfLoc is LoftType;
            
            annotation { "Name" : "Surface Width:" }
            isLength(definition.surfWidth, surfLoftDistBounds);
            
            if (definition.surfLoc == LoftType.BLIND)
            {
                annotation { "Name" : "Flip side to loft:", "UIHint" : UIHint.OPPOSITE_DIRECTION}
                definition.flipSide is boolean;
                
                annotation { "Name" : "Extend opposite direction:", "Default" : false }
                definition.extendBottom is boolean;
                
                if (definition.extendBottom)
                {
                    annotation { "Name" : "Extend Length:" }
                    isLength(definition.extendLength, surfLoftDistBounds);
                }
                
            }
            
        }
        
        annotation { "Group Name" : "Wrap parameters", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Point spacing:", "Description" : "Target spacing between points along seed edges" }
            isLength(definition.pointSpacing, pointSpacingBounds);
            
            annotation { "Name" : "Spline approximation tol:" }
            isLength(definition.splineTol, aprxTolBOunds);
            
            annotation { "Name" : "Spline approximation degree" }
            isInteger(definition.splineDegree, aprxDegreeBounds);
            
            annotation { "Name" : "Max control points:" }
            isInteger(definition.splineMaxPoints, aprxMaxCtrlPointBounds);
            
        }  
        
    }
    {
        definition.wrapPath = constructPath(context, qUnion([definition.wrapProfile]));
        definition.seedRefPointVector = evVertexPoint(context, {
                "vertex" : definition.seedRefPoint
        });
        definition = locateRefPoint(context, id + 'locateRefPoint', definition);
        
        wrapOntoAndLoft(context, id, definition, 'WRAPPED SURFACE');
        
    });
    
export function locateRefPoint(context is Context, id is Id, definition is map) returns map
{
    var updatedDef = definition;
    if (updatedDef.seedLocationType == EdgeLocationType.NEAREST)
    {
        var pointDist = evDistancePath(context, {'side0' : definition.wrapPath, 'side1' : definition.seedRefPointVector});
        var pathParameter = pointDist.pathParameter;
        
        updatedDef.profileRefPoint = pointDist.sides[0].point;
        updatedDef.profileRefParameter = pathParameter;
    }
    else if (updatedDef.seedLocationType == EdgeLocationType.DIRECTION)
    {
        if (updatedDef.dirSpecType == DirectionSpecificationType.VECTOR)
        {   
            var dirVector = normalize(vector(updatedDef.dirX, updatedDef.dirY, updatedDef.dirZ));
            
            var dirLine = line(updatedDef.seedRefPointVector, dirVector);
            
            var lineDist = evDistancePath(context, {'side0' : definition.wrapPath, 'side1' : dirLine});
            
            updatedDef.profileRefPoint = lineDist.sides[0].point;
            updatedDef.profileRefParameter = lineDist.pathParameter;
            
        }
        else if (updatedDef.dirSpecType == DirectionSpecificationType.QUERY)
        {
           if (!isQueryEmpty(context, qGeometry(updatedDef.dirQuery, GeometryType.PLANE)))
           {
                var facePlane = evPlane(context, {
                        "face" : updatedDef.dirQuery
                });
                
                var planeNormalLIne = line(updatedDef.seedRefPointVector, facePlane.normal);
                var planeDist = evDistancePath(context, {'side0' : definition.wrapPath, 'side1' : planeNormalLIne});
            
                updatedDef.profileRefPoint = planeDist.sides[0].point;
                updatedDef.profileRefParameter = planeDist.pathParameter;
           }
           else if (!isQueryEmpty(context, qGeometry(updatedDef.dirQuery, GeometryType.LINE)))
           {
                var evaluatedLine = evLine(context, {
                        "edge" : updatedDef.dirQuery
                });
                
                var dirLine = line(updatedDef.seedRefPointVector, evaluatedLine.direction);
            
                var lineDist = evDistancePath(context, {'side0' : definition.wrapPath, 'side1' : dirLine});
                
                updatedDef.profileRefPoint = lineDist.sides[0].point;
                updatedDef.profileRefParameter = lineDist.pathParameter;
           }
           else if (!isQueryEmpty(context, qBodyType(updatedDef.dirQuery, BodyType.MATE_CONNECTOR)))
           {
                var mcCS = evMateConnector(context, {
                        "mateConnector" : updatedDef.dirQuery
                });
                   
                var dirLine = line(updatedDef.seedRefPointVector, mcCS.zAxis);
                
                var lineDist = evDistancePath(context, {'side0' : definition.wrapPath, 'side1' : dirLine});
                    
                updatedDef.profileRefPoint = lineDist.sides[0].point;
                updatedDef.profileRefParameter = lineDist.pathParameter;
           }
        }
        
    }
    else if (updatedDef.seedLocationType == EdgeLocationType.DEFINED)
    {
        var pointDist = evDistancePath(context, {'side0' : definition.wrapPath, 'side1' : definition.wrapRefPoint});
        var pathParameter = pointDist.pathParameter;
        
        updatedDef.profileRefPoint = pointDist.sides[0].point;
        updatedDef.profileRefParameter = pathParameter;
    }
    
    addDebugLine(context, updatedDef.seedRefPointVector, updatedDef.profileRefPoint, DebugColor.MAGENTA);
    
    return updatedDef;
}


/**
 * @param definition {map}: definition map. references fields below:
 *  @field profileRefParam {number} : the parameter of the wrap reference point
 */
 
function wrapOntoAndLoft(context is Context, id is Id, definition is map, name is string) returns Query
{
    var idName = replace(name, ' ', '');
    idName = replace(idName, ':', '');
    
    var curvePath = definition.wrapPath;

    var sIntersectParam = definition.profileRefParameter;
    
    var targCurveLen = evPathLength(context, curvePath); // length of the wrap profile
    var topLoftEdgeArray is array = []; // array for top edges
    var bottomLoftEdgeArray is array = []; // array for bottom edges
    var allEdges = evaluateQuery(context, definition.selEdges); // all seed edges
    
    for (var edge in allEdges)
    {
        var edgeLen = evLength(context, {
                "entities" : edge
        }); // edge length
        
        var numPoints = floor(edgeLen/definition.pointSpacing); // number of points to search over
        
        var edgeParams = range(0, 1, numPoints);
        
        var edgePointLines = evEdgeTangentLines(context, {
                "edge" : edge,
                "parameters" : edgeParams
            }); // lines for each evaluated parameter
            
        
        var topEdgePTArray = []; // top points
        var bottomEdgePTArray = []; // bottom points
        var centerPointArray = []; // center points
        
        for (var ln in edgePointLines) // for each pointLIne
        {
            addDebugPoint(context, ln.origin, DebugColor.GREEN);
            var yVal = ln.origin[1];
            var zVal = ln.origin[2];
            var xShift = ln.origin[0] - definition.seedRefPointVector[0]; // how far is this point from the seed refeence point?
            var sParam = sIntersectParam + (xShift / targCurveLen); // path parameter for wrapped X point
            if (definition.flipWrapDirection)
            {
                sParam = sIntersectParam - (xShift / targCurveLen);   
            }
            var wrapLine = evPathTangentLines(context, curvePath, [sParam]).tangentLines[0]; // find the wrapped 
            
            //var moveDirection = orthogonalize(vector([1, 0, 0]), wrapLine.direction, true);
            var moveDirection = vector([-1*wrapLine.direction[2], 0, wrapLine.direction[0]]);
            var xVal = wrapLine.origin[0];
            if (definition.surfLoc == LoftType.MID_PLANE)
            {
                var topVector = vector([xVal + moveDirection[0] * definition.surfWidth / 2, yVal, zVal + wrapLine.origin[2] + moveDirection[2] * definition.surfWidth / 2]);
                var bottomVector = vector([xVal - moveDirection[0] * definition.surfWidth / 2, yVal, zVal + wrapLine.origin[2] - moveDirection[2] * definition.surfWidth / 2]);
                topEdgePTArray = append(topEdgePTArray, topVector);
                bottomEdgePTArray = append(bottomEdgePTArray, bottomVector); 
                centerPointArray = append(centerPointArray, vector(xVal, yVal, zVal + wrapLine.origin[2]));
            }
            else if (definition.surfLoc == LoftType.BLIND)
            {
                if (definition.flipSide)
                {
                    moveDirection *= -1;   
                }
                
                var additionalBottom = 0 * millimeter;
                if (definition.extendBottom)
                {
                    additionalBottom = definition.extendLength;   
                }
                
                var topVector = vector([xVal + moveDirection[0] * definition.surfWidth, yVal, zVal + wrapLine.origin[2] + moveDirection[2] * definition.surfWidth]);
                var bottomVector = vector([xVal - moveDirection[0] * additionalBottom, yVal, zVal + wrapLine.origin[2] - moveDirection[2] * additionalBottom]);
                topEdgePTArray = append(topEdgePTArray, topVector);
                bottomEdgePTArray = append(bottomEdgePTArray, bottomVector);
                centerPointArray = append(centerPointArray, vector(xVal, yVal, zVal + wrapLine.origin[2]));
            }
        }
        
        var topAprxSpline = approximateSpline(context, {
                "degree" : definition.splineDegree,
                "tolerance" : definition.splineTol,
                "maxControlPoints" : definition.splineMaxPoints,
                "isPeriodic" : false,
                "targets" : [approximationTarget({ 'positions' : topEdgePTArray})]
        })[0];
        
        opCreateBSplineCurve(context, id + ("aprxTopSpline" ~ idName ~ size(topLoftEdgeArray)), {
                "bSplineCurve" : topAprxSpline
        });
                
        topLoftEdgeArray = append(topLoftEdgeArray, qCreatedBy(id + ("aprxTopSpline" ~ idName ~ size(topLoftEdgeArray)), EntityType.EDGE));
        
        var botAprxSpline = approximateSpline(context, {
                "degree" : definition.splineDegree,
                "tolerance" : definition.splineTol,
                "maxControlPoints" : definition.splineMaxPoints,
                "isPeriodic" : false,
                "targets" : [approximationTarget({ 'positions' : bottomEdgePTArray})]
        })[0];
        
        opCreateBSplineCurve(context, id + ("aprxBotSpline" ~ idName ~ size(bottomLoftEdgeArray)), {
                "bSplineCurve" : botAprxSpline
        });
                
        bottomLoftEdgeArray = append(bottomLoftEdgeArray, qCreatedBy(id + ("aprxBotSpline" ~ idName ~ size(bottomLoftEdgeArray)), EntityType.EDGE));
        
        if (definition.keepWrappedCurve)
        {
            var ctrAprxSpline = approximateSpline(context, {
                    "degree" : definition.splineDegree,
                    "tolerance" : definition.splineTol,
                    "maxControlPoints" : definition.splineMaxPoints,
                    "isPeriodic" : false,
                    "targets" : [approximationTarget({ 'positions' : centerPointArray})]
            })[0];
            
            opCreateBSplineCurve(context, id + ("aprxCTRtSpline" ~ idName ~ size(bottomLoftEdgeArray)), {
                    "bSplineCurve" : ctrAprxSpline
            });
        }
    }
    addDebugEntities(context, qUnion(topLoftEdgeArray), DebugColor.RED);
    addDebugEntities(context, qUnion(bottomLoftEdgeArray), DebugColor.BLUE);
    
    loft(context, id + ("loft" ~ idName),
        {
                "bodyType" : ToolBodyType.SURFACE,
                "operationType" : NewBodyOperationType.NEW,
                "surfaceOperationType" : NewSurfaceOperationType.NEW,
                "wireProfilesArray" : [{ "wireProfileEntities" : qUnion(topLoftEdgeArray) }, { "wireProfileEntities" : qUnion(bottomLoftEdgeArray) }]

            });
            
    setProperty(context, {
                "entities" : qCreatedBy(id + ("loft" ~ idName), EntityType.BODY),
                "propertyType" : PropertyType.NAME,
                "value" : name
            });

    opDeleteBodies(context, id + ("deleteSplines1" ~ idName), {
                "entities" : qUnion([qUnion(topLoftEdgeArray), qUnion(bottomLoftEdgeArray)])
            });
            
    
    return qCreatedBy(id + ("loft" ~ idName), EntityType.BODY);
}


