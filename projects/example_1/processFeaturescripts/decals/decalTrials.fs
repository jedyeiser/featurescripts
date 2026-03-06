FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

import(path : "onshape/std/decalUtils.fs", version : "2892.0");
import(path : "onshape/std/error.fs", version : "2892.0");
import(path : "onshape/std/imagemappingtype.gen.fs", version : "2892.0");
import(path : "onshape/std/mateConnector.fs", version : "2892.0");
import(path : "onshape/std/topologyUtils.fs", version : "2892.0");

/**
 * Trials to try to align decals with top surfaces. Fist make flat/cylindrical copy, 
 * Apply and allign decal to simplified surface body
 * use opReplaceFace to replace the flattened faces with the old faces. 
 */
 
 //start by writing the most mimimal of functions. Faces are povided to the function. One is decaled, one is deformed. 
 //assume that surfaces map to one another. We just want to try playing around with face replacing and decals. 
 //play around/print imageData to see what we get. 
 
 annotation { "Feature Type Name" : "Deform decal", "Feature Type Description" : "" }
 export const deformDecal = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         annotation { "Name" : "Image" }
        definition.image is ImageData;
        
         annotation { "Name" : "Decal faces", "Filter" : EntityType.FACE}
         definition.decalFaces is Query;
         
         annotation { "Name" : "Deformed faces", "Filter" : EntityType.FACE}
         definition.deformedFaces is Query;
         
     }
     {
         // Define the function's action
     });
 