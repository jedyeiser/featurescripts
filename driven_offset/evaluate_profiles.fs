FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Creates wires (or returns bSpline data) obeying special rules from either a solid body, an edge, or a chain of edges
 * 
 * User/caller provides input type {WIRE, PART, EDGES} (query type)
 * User/caller provides output type {WIRE, Data} (query, map)
 * User/caller provides projection planar face
 * User/caller specifies approdimation parameters
 * User/caller specifies if output should be grouped as a single curve, a curve per input curve (exclusing curves that collapse to a point), or an "efficent representation"
 *      Efficent implies look at curvature progressions, degree, weights and control point spacing to evaluate if two curves are good candidates to be merged into a single entity. If so, output has these two curves merged. Note that we may end up wanting to merge multiple curves into a single one. 
 * 
 * Stop projection where an endpoint of the chain is normal to the reference plane. Note that there may be multiple edges in the chain beyond these tangent points, and we want to ignore all data beyond the first crossing (moving outward from the chain center)
 * If a PART is provided
 *      User/caller specifies what types of data should be returned
 *          Top/Bottom Profiles 
 *          Middle Profile
 *          Periphery
 *          All Profiles
 *      The explicit assumption here is that our 'profile direction' here is along, or along-ish the x direction. Users/callers should, however, provide a query or a vector specifying the 'prevailing direction'
 *      We use this prevailing direction to better understand our curves
 *          Top and Bottom profiles will likely be long chains with endpoints near (though not necessarily AT) the extents of our inputs, projected ionto the planar face onto which we're projecting
 *          Top and Bottom profiles will be of similar, though not necessarily equal length
 *          Top and Bottom profiles are joined either by:
 *              A single line with a length sigificantly shorter than either chain
 *              An arc with an arclength significantly shorter than either chain
 *              A b-spline with an arclength significantly shorter than either chain
 *              Intersections between Top/Bottom profiles and the short sections that join them should roughly be strongly perpendicular, or strongly tangent
 * 
 * 
 */  