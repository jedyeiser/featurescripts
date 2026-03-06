FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * This function takes a group of faces (which must be adjacent) or a surface body as input
 * along with a 'spine curve. 
 * 
 * User provides relevant approximation parameters.
 * User provides a mainFaces Query. These are the 'main faces' that we want to distort as little as possible with our transformations. 
 * 
 * MODE = TO_DECAL
 *      Step 1: Find the "optimal solution" to approximate the spineCurve as a set of lines and arcs. Approximated/fit sline must remain G1 continuous. 
 *      Function should fail and report
 *      to user if the spineCurve is not planar.  create a surface normal to the spineEdges plane that extends beyond 
 *      our input faces. If spine curve does not extend beyond our faces/body, throw an error and alert user. 
 * 
 *      Solve for 'best wrap/drape' that minimises 
 *          1. strain on our mainFaces, though some shear/strain near boundaries is expected
 *          2. we're 'flattening' our surface onto a simpler version (even if it's multiplanar!).
 *          3. Try to preserve areas, but accept that deformation is bound to occur. 
 *          Should we just make a mesh and solve that way? The math get's pretty easy. 
 *      Create boundary edges for the wrapped surface. Create edges parallel to the spine plane normal that connect wrapped surface boundaries. 
 *      Split boundary curves with 'width' curves so that each 'loop' contains either a linear or circular section along our approximated spine. 
 *      Double confirm that when projected onto our spine normal plane, ALL of our boundary edges follow either a linear or a circular pattern (even though we'll be representing these
 *      as NURBS). 
 * 
 *      For all input faces, project control points onto face. Map those control points through our 'unwrapping transform'into the regions bounded by our wrapped boundary and the connecting curves. Use opFillSurface with no control points at first. If result is planar or
 *      cylindrical, we're done. Otherwise, either manually edit control points and knots to establish the correct planarity . Every wrapped surface point should store data on how to 'get back to where I came from' as an attribute on that face. 
 * 
 *      User can specify 'wrapEdges' in which case source face edges are deformed onto the draped surface. First wrap endpoints. Then Wrap edges. Come to think of it, for the time being, we should wrap the edges no matter what. 
 *      If edges are created, allow the option to split our wrapped face. If we split faces, make sure that we're updating face attibutes properly so that each face keeps track of it's interior point mapping, noting any point on an edge (not interior, not exterior, not node). 
 *      
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

//Step 1 - approximate spine correctly with arcs and lines
//Step 2 - create surface from approximated spine
//step 3 - 

annotation { "Feature Type Name" : "Decal face transform", "Feature Type Description" : "" }
export const decalFaceTransform = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Define the parameters of the feature type
    }
    {
        // Define the function's action
    });
