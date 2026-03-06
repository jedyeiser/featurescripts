FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * This function takes a group of faces (which must be adjacent) or a surface body as input
 * along with a 'spine curve. 
 * 
 * MODE = TO_DECAL
 *      Step 1: Find the "optimal solution" to approximate the spineCurve as a set of lines and arcs. 
 *      Must remain G1 continuous. User provides relevant approximation parameters. Function should fail and report
 *      to user if the spineCurve is not planar.  create a surface normal to the spineEdges plane that extends beyond 
 *      our input faces. If spine curve does not extend beyond our faces/body, throw an error and alert user. 
 * 
 *      'Wrap' or 'drape' input face edges onto the simplified surface. Create bespline surfaces inside those draped curves. Elevate
 *      flattened surface to achieve 1:1 control point mapping with the input surface. Set mapping data as an attribute on each face so it can be recovered
 *      by a RESTORE mode.
 * 
 * Mode = RESTORE
 *      Test faces to ensure they all have TO_DECAL attributes and those are valid. 
 *      Replace the new, flattened faces with their original faces (which you can get a query for from the data)
 *      We may need to do this one face at a time and then re-boolean, face by face. 
 * 
 * Detailed but concise docstrings with arguments and retursn. 
 * 
 * 
 */
 
 
export enum DeformDirection
{
    TO_DECAL,
    RESTORE
}



annotation { "Feature Type Name" : "Decal face transform", "Feature Type Description" : "" }
export const decalFaceTransform = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Define the parameters of the feature type
    }
    {
        // Define the function's action
    });
