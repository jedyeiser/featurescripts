FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * This feature takes a:
 * seedEdge - The edge which will end up being edited to merge data from our mergeEdge
 * mergeEdge - The edge (which must be G0 with our seedEdge which contains data that will be added to 
 * 
 * Build and call a builtin onshape editCurve feature that modifies the seedEdge to include the mergeEdge. 
 * Note that the two edges may both belong to a wire body, and that wire body may or may not be the same wire body. 
 * 
 * Scenarios
 *      seedEdge on a wire body, mergeEdge not contained in a wire body
 *          merged curve stays part of the seed wire body
 *      seedEdge on wire body, mergeEdge on a seperate wire body
 *          merged curve part of the seed wire body
 *      seedEdge on wire body, mergeEdge on the same wire body
 *          merged curve part of the seed wire body
 *      seedEdge not on a wire body
 *          create new wire body, no matter what the status of mergeEdge is. 
 * 
 */ 