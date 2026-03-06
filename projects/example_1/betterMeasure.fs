FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * This tool extends the standard variable/measure tool to add more useful measurement types. 
 * The primary functionality is to measure things, but we will be measuring different things. Measruements 
 * can be viewed in the 'Measruements' parameter group. All measruement parameters are read only, and which measurements are visible
 * is dictated by measruement types and selections. User should be able to create a variable (needs to provide a variable name)
 * for any visible measurement. Do research here - there may be a 'button' or 'icon' that we can use for std docs. see variable.fs
 * 
 * 
 * Measurement types: {DISTANCE, VECTOR, ANGLE, LENGTH}. Horizontal Enum
 * 
 * Select coordinate system in Enum. Show label. {WORLD, MATE_CONNECTOR}. Coordinate system does not impact some measurements. Default is false. 
 * 
 * User selects element 1 (SOLID, SHEET, WIRE, MATE_CONNECTOR, EDGE, VERTEX)
 * User may select element 2, butn not necessary for some measurements {SOLID, SHEET, WIRE, MATE CONNECTOR, EDGE, VERTEX}
 * 
 * For each element selection
 * If SOLID, User must select COM, NEAREST_FACE, NEAREST_EDGE or NEAREST_VERTEX
 * IF SHEET, User must select COA, NEAREST EDGE, NEAREST VERTEX
 * IF MATE_CONNECTOR and we're looking at an angle, user must specify MC axis to use as a reference. Use builtin Enums. 
 * 
 * if measurement is DISTANCE, user can toggle a boolean which will toggle the visiblity of a third query, which must either be an edge or a face. 
 * Check to see if both elements are ON the query face or edge. If they are not, warn the user, but still return a valid result. First find the closest point to each element on the ALONG query. There represent d1 and dN, respectively. Measure the distance along either the face or the edge
 * between the two nearest points to our input elemets. If the third query is a face, user can select to "keepMeasurementWire", in which case this function returns the curve on the selected face between measurement points, as well as providing the ability to create standard and query variables. 
 * 
 * If measurement is DISTANCE, show measurements:
 *      1. Distance between measurement points. Alway positive. 
 *      2. If distance along (third query provided). This will involve creating a curve along a face when face is selected. Possibly deleting later. 
 *      2. Parameter group AxisDeltas
 *          a. delta Z (difference along worldZ or mateConnector.zAxis)
 *          b. delta X (difference along worldX or mateconnector.xAxis)
 *          c. deltaY (difference along worldY or yAxis(mateConnector)
 * 
 * If measurment is VECTOR, show: 
 *      1. Distance bettwen measurement points
 *      2. Delta vector. Boolean to flip direction. Save variable as a vector or as an array. User should be able to normalize and or strip units from the vector. 
 *      3. If an angle measurement exists between the entities, show it. 
 *          Angles between curves at intersections or closest point
 *          Angles between curve and face at intersections or closest point
 *          Angles between planar faces (normals)
 *      4. If entities are mate connectors, user can toggle a boolean that show euler angles and a rotation matrix. 
 * 
 * If measurement is LENGTH
 *      1. Normal case here is an edge. Provide measurement. 
 *      2. If multiple edges are selected, provide their total length
 *      4. Allow the selection of a third query like with DISTANCE. 
 *          Query must be a face
 *          Measure length of curve on face connecting points.
 * 
 * Debug
 *      Snow measurement frames/vectors/points
 *      Show measurements
 *      Print measurement
 *
 *      
 * 
 */
 
 
 export function betterMeasureEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map,
  isCreating is boolean, specifiedParameters is map, hiddenBodies is Query, clickedButton is string) returns map
{
    return definition;
}
 
 annotation { "Feature Type Name" : "Better measure", "Feature Type Description" : "Improved measurement/equation tool", "Edtiting Logic Function" : "betterMeasureEditingLogic" }
 export const betterMeasure = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         // Define the parameters of the feature type
     }
     {
         // Define the function's action
     });
 