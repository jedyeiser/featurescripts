FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Takes edges to unwrap, a wrapped reference curve, a wrapped alignment point (mate connector), unwrapped origin (mate connector)
 * Unwraps the edges to unwrap, preserving length along the wrapped reference curve. 
 * Tests final edges for circularity. 
 *      Painful extraction process in this case, but a value added one
 *          Test - does chaing to an arc affect the tangent vector? How 'arclike' is each edge? We'd like to test for this quickly - filter results. Can we do this by simply unwrapping control points? That would be much faster. 
 * 
 */ 