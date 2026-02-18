FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

//import ExportCurveCore
import(path : "666228ba3514cc062764888b", version : "349d87725d452bf2c88bab4b");


/**
 * ________________ EXPORT CURVE ____________________
 * Takes an edge query (can be multiple, but must be G0 Continuious),
 * a sectionAlong enum {CHAIN, QUERY, WORLD} which dictates how we will generate points
 * in our export table, and user input about table export format (units, sig figs, 
 * clean (no units), number of points). Samples along the input edges based on user 
 * inputs and exports a custom table with evenly spaced point data along the edge(s),
 * though what 'evenly spaced' means depends on user input. 
 * 
 * If sectionAlong == AlongType.CHAIN, section N evenly spaced points (in distance) 
 * along the chain. In the event that our edges have G1 continuity, this would be equivelant to 
 * a parameter range of range(0, 1, N) over the path that composes the edges. 
 * 
 * If sectionAlong == AlongType.QUERY, the user will need to provide a query to define our spacing. 
 * If the query is an edge, it must be a line. Our evaluation planes will be normal to this line. We then create N evenly spaced planes 
 * that bound our edges correctly, and evaluate the intersection of our plane and one of the edges. 
 * If the query is a face, it must be a planar face. The normal of this face is analogous to the tangent of the line above. 
 * If sectionAlong == AlongType.WORLD, the user must select an alongAxis enum that specifies if the cross section planes
 * should be normal to worldX, worldY or worldZ. In this case, alongAxis.worldX would mean evenly spaced points in X. 
 * 
 * 
 */