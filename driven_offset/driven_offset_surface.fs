FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * This feature extends driven_offset_Profile to create a variety of surfaces from driven offsets. We have three basic modes. 
 * For each mode, we can optionally keep offset wires or not
 * 
 * definition.surfaceMode - {RULED_OFFSET_PROFILE, CONNECTED_OFFSET, MULTIPROFILE_LOFT}
 * 
 * RULED_OFFSET_PROFILE: Options for WIDTH and HEIGHT. If WIDTH is selected, output surface is the width of the offset. If a reference profile is provided, the output width surface shoudl respect that profile (surface may not be linear in cross section)
 *  If HEIGHT is selected, surface should be along the HEIGHt direction of our frames. 
 *  Provide second direction boolean, which 'extends' the surface in the opposite direction
 * 
 * CONNECTED_OFFSET: Connect the sourceEdges to the offset edges with a loft. Note that not all source edges may be used, and the 'loft' portion of the source edges is just where there is a relevant offset. This may require a change in driven_edge_offset
 *  so it's easier for us to understand if a vertex should be lofted to an arc (G0), visa versa, or if we loose edges due to an offset. 
 * 
 * MULTIPROFILE_LOFT: 
 *  Provide multiple offset profiles, loft between them. Understand vertex connections based on seedEdges verticies - or whatever makes sense
 * 
 */