FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * Allows users to push and pull surface around using manipulator functions. 
 * Utilities are built in to simplify complex surfaces to a few manipulators. 
 * 
 * User specifies continuity type to preserve. This should mean
 * 
 * 1. If G0 is selected, no need to bang control points into place and alter the surface. 
 * 2. If G1 is selected, solve for whatever we solve for, but ensure G1 continuity. Use tangent vectors when approximating splines, setting interpolating indicies appropriates.y
 * 3. If G2 is selected, same as G1, but preserve curvature
 * 
 * User provides U and V dimension. Create isoparametric curves and solve for intersection points. Two of each dimension will be on extents. Allow editing of any of the points that do not impact the set continuity. Lock points that do affect G1 or G2 continuity.
 * Create points for all U, V intersections. Add manipulators to any point that can be moved. Manipulators should be alligned with surface normal at whatever point they're at. 
 * Store manipulator offsets in editing logic. Clear this data when needed and resolve. 
 * 
 * User provides standard spline approximation parameters. Approximate splines through control points. Show U, V curve contrrol point polygons using addDebugLine. 
 * 
 * When offsets are confirmed or function runs, use the approximated U/V curves from our manipulators to construct the simplest, cleanest surface possible. As manipulators are at control points, we should be able
 * to use these control points directly to create a BSplineSurface. Protect against the case when we have repeated control points. One manipulator per unique point, period. 
 * 
 */
 
 annotation { "Feature Type Name" : "Pull surface", "Feature Type Description" : "Takes a face and user input. Allows users to push/pull control points around and deform the surface. Replace face optional" }
 export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
     precondition
     {
         // Define the parameters of the feature type
     }
     {
         // Define the function's action
     });
 