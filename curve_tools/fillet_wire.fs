FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");

/**
 * Takes a wire or a chain (G0) of edges as input along with a fillet radius. 
 * G0 intersections are shown via a manipulator function
 * Selected intersections get modified to have a fillet of the specified radius between edges on either end of that intersection
 *      We may need to allow our fillets to be bridging curves in some situations - when no valid circular solution exists. If that is the case, we should try to have curvature as constant as possible over the 'fillet' curve
 * A new wire is returned with the fillet(s) intactA 
 * 
 * If a wire is selected, this feature supports the option to either return a new wire, or:
 *      1: Split the old wire at locations where our fillets will 'return' to the original wire
 *      2: use edit_curve to approximate/move control points in our 'G0 sections' (now isolated via splits) to create our fillet
 *      3: Recombine the curve
 * 
 * This is subtly different than simply returning a new wire. 
 */ 