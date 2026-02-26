FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * This feature generates a target baseline given varied user input. 
 * If a query is provided for EI, we solve for the camber pocket based on theoretical deflections based on this curve. 
 * Otherwise we assume that the camber pocket is roughly cubic in shape. 
 * 
 * User provides queries for FCP, ACP, mount/load point. All these points must be colinear. 
 * 
 * User provides FCP height in mm, can be 0mm (ACPH)
 * User provides Forebody rocker length (FRCPL) in mm. Can be 0mm
 * User provides camber height in mm (MCH). This is the max height of the camber pocket, once rocker sections have been factored in. Can be 0mm .
 * User provides Aftbody rocker length in mm (ARCPL). Can be 0mm. 
 * User provides ACP height in mm (ACPH). 
 * 
 * Because camber height depends on rocker configurations, this will need to be an iterative solver. 
 * 
 * Step 1. If an EI profile is provided, use beam bending equations to solve for the theoretical deflection of a beam with a provided
 * EI provile when supported at FRCP (Move towards MRS by FRCPL from FCP) and ARCP (move towards MRS by ARCPL from ACP) and loaded at mount/load point. 
 * For first iteration, use a load that fgives us a total deflection of max camber height when we apply the boundary condition that deflection be 
 * 0 at FRCP and ARCP. Save this load, as we'll need it later. 
 * 
 * If no edge query is provided for an EI profile, assume symmetric cubic shape for the camber profile with no inflection points (it's basically just a sharper quadratic). Solve for a cubic
 * that has our target camber heigh and is at 0 at FRCP and ARCP. Save this height, we'll need it later. 
 * 
 * IF FRCPL > 0 * millimeter, we have a forebody rocker section. The key detail here is that we imagine a line that is tangent 
 * to the camber pocket  at FRCP. We create a curve that is G1 continuous to that line. The other point of our line is at X = FCP. the Z value of that point is dictated
 * by the normal projection from our camber pocket tangent line by FCP height. Solve for a simple quadratic here. 
 * 
 * If ARCPL > 0 * millimeter, we have an aftbody rocker section. Apply the same logic as the forebody, but to the aftbody curve. 
 * 
 * Translate and rotate curves as necessary suc that forebody rocker is G1 continuous to Camber which is G1 continuous to Aftbody Rocker. 
 * Rotate these curves 'together' such that the minimum in the forebody and the minimum in the aftbody are both at the same z. 
 * 
 * Translate curves together such that the forebody and aftbody minima are at z = 0. 
 * 
 * Measure the camber height of the translated and shifted baseline. If this height is within a specified tolerance, we're done. 
 * If not, update either our estimated load or solved height and re-caululate/iterate until we achieve the correct tolerance. 
 * 
 * When we have converged on a solution, we should export our curves, but group them using opExtractWires. 
 * 
 * User supplies a string to name curves, or no name if no string is given. 
 * 
 * User supplies standard spline approximation paramaters. We use these to approximate our splines
 * (but ensure that endpoints get interpolated exactly). 
 */
 
annotation { "Feature Type Name" : "Generate baseline", "Feature Type Description" : "" }
export const myFeature = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // Define the parameters of the feature type
    }
    {
        // Define the function's action
    });
