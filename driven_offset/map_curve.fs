FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * This feature is like a special flatten/deform. It maps curve data from one curve to another, preserving length/distance from reference points. 
 * User/caller provides fromEdges, toEdges
 * User/caller provides a ref point enum {Shared, Seperate}
 *      Ref point(s) (mate connector)
 * Feature should visualize directions and flag if our fromChain and toChain point in different directions. 
 * User specifies if the wrap/map type should be:
 *      FROM_EDGES - the output edges will be 'deformations' of the fromEdges such that they are along the toEdges, as measured from the reference point (output curves have the same length/distance along the toEdges as fromEdges has from the reference point. 
 *      TO_EDGES - basically trim a wire created from toEdges at distances from the reference point equal to the distances of fromEdges endpoints along the chain from the reference point. 
 *      SINGLE - Single curve
 * 
 */