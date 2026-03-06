FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * Trials to try to align decals with top surfaces. Fist make flat/cylindrical copy, 
 * Apply and allign decal to simplified surface body
 * use opReplaceFace to replace the flattened faces with the old faces. 
 */
 
 //start by writing the most mimimal of functions. Faces are povided to the function. One is decaled, one is deformed. WE're going to replace the decaled faces
 //with the deformed faces.
 
 annotation { "Feature Type Name" : "Deform decal", "Feature Type Description" : "" }
 export const deformDecal = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "Decal faces", "Filter" : EntityType.FACE}
         definition.decalFaces is Query;
         
         annotation { "Name" : "Deformed faces", "Filter" : EntityType.FACE}
         definition.deformedFaces is Query;
         
     }
     {
         // Define the function's action
     });
 