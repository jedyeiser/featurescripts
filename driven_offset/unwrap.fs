FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Takes edges to unwrap, a wrapped reference curve, a wrapped alignment point (mate connector), unwrapped origin (mate connector)
 * Unwraps the edges to unwrap, preserving length along the wrapped reference curve. 
 * Tests final edges for circularity. 
 *      Painful extraction process in this case, but a value added one
 *          Test - does chaing to an arc affect the tangent vector? How 'arclike' is each edge? We'd like to test for this quickly - filter results. Can we do this by simply unwrapping control points? That would be much faster. 
 * 
 * This will be both a more general and more specific version of our deform feature. 
 * More general because it will accept wires, surfaces, composite parts, parts and mate connectors as input. 
 * More specific because we add the restriction that our to/from curves (or geometry/input type, really) be planar tangent chains, and that the planes of our respective curves are parallel (they will most often be coplanar)
 * More general because we support general deformation/unwrapping, similar to deform (with the restrictions above) as well as some specific cases
 * More specific because we support how we unwrap specific geometries {Thickened: Part is constant thickness. Find 'center surface', deform that surface and rethicken
 *                                                                      Preserve Neutral Axis/Length_curve:
 *                                                                              (preserve the length of the neutral axis of the part, but the neutral axis may itself not be flat in our new geometry. 
 *                                                                              Need to select faces which will then be wrapped flat - with their shapes being driven by preserving the neutral axis}
 *                                                                              Need to write a seperate feature that calculates neutral axis. Users should be able to use this feature. 
 *                                                                              Can rely on a mid-profile from our evaluate profiles feature rather than a neutral axis for speed. 
 * When unwrapping, we test to see if we can convert edges into lines or arcs while preserving continuity.
 * When unwrapping, we enforce that the edges/faces that unwrap onto the specified plane are planar
 * 
 * Users can supply mate connectors (allow implicit creation), planar faces or planes as an unwrap face. 
 * 
 * When a composite part is supplied, limit input to one composite part. Extract components of that composite part and populate an array variable with deformation types for each body, unless we can easily and robustly test for cases (I doubt it)
 * 
 * WIRE
 *      Allow projection onto a plane NORMAL to the plane onto which we are unwrapping to get the 'profile' of the wire to deform. Support cases where the wire turns back on itself - but if it does so, it must 'hold' the same profile as the other edges on the profile plane projection
 *      Support finding lines, arcs
 * 
 * MATE CONNECTOR
 *      Move/rotate the mate connector so that it retains the correct placement in the new coordinate system/flattend system
 * 
 * SURFACE
 *      Allow projection/intersection onto a plane NORMAL to the unwrap plane. Throw an error when there is no determinate result. 
 *      Can be type THICKEN
 *          Test for contsant thickness
 *          Solve for interior surface
 *          If the surface needs to be UNDDRAPED (deformed such that its edges all fall within an extruded surface (extrude direction is normal to unwrap plane)  (or a mathematical representation therof) of a supplied valid profile (to which the user can supply an offset - in which case show offset in magenta)
 *          Unwrap undraped 'extruded' (or the surface was that way to start) onto plane half thickness above (may need to flip/toggle) unwrap plane
 *          Test for lines/arcs. convert/sketch/extract where appropriate
 *          Thicken surface
 * PART
 *      Support 'planar deform' as default
 *      Support providing a 'preserve length profile' {MIDDLE, NEUTRAL_AXIS, QUERY (user provides a valid profile. Support offsetting. When offseting, show offset profile in CYAN)}
 *      Unwrap part, preserving length measured along the preserve_length_profile.
 *      Optionally, allow for selected faces to be the ones that get projected flat. We may be able to solve for these faces, as our parts will often be above our unwrap planes. If when conflicts exist, we can provide reference geometry on the 'top' side of our unwrap plane. I suppose this applies to all unwrap     types, not just Parts. 
 * 
 * Variables to export
 *      edges_on_plane (the edges that wrap to the offset plane. Note that in the case of a thickened part, these are not the same edges we originally unwrap. Or they could be - if we measured/required thickness)
 *      
 * 
 */ 