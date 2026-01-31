FeatureScript 2856;
import(path : "onshape/std/common.fs", version : "2856.0");

/**
 * Debug visualization utilities for geometry debugging.
 *
 * Provides functions to visualize points, polylines, curves, and other
 * geometric entities in the Onshape viewport using debug entities.
 * All functions use DebugColor enum from Onshape std library.
 *
 * @source gordonSurface/gordonKnotOps.fs
 */

/**
 * Add multiple debug points to the context.
 *
 * Visualizes an array of points as debug entities in the specified color.
 * Commonly used to show control points, sample points, or other point arrays.
 *
 * @param context {Context} : Onshape context
 * @param pointArray {array} : Array of Vector points to visualize
 * @param debugColor {DebugColor} : Color for the debug points
 *
 * @example `addDebugPoints(context, controlPoints, DebugColor.CYAN)`
 *
 * @source gordonSurface/gordonKnotOps.fs:511-517
 */
export function addDebugPoints(context is Context, pointArray is array, debugColor is DebugColor)
{
    for (var i = 0; i < size(pointArray); i += 1)
    {
        addDebugPoint(context, pointArray[i], debugColor);
    }
}

/**
 * Show a BSpline's control polygon (polyline connecting control points).
 *
 * Visualizes the control polygon by drawing debug lines between consecutive
 * control points. For periodic splines, also closes the polygon.
 * Skips drawing lines between coincident control points.
 *
 * @param context {Context} : Onshape context
 * @param bspline {BSplineCurve} : BSpline curve to visualize
 * @param debugColor {DebugColor} : Color for the control polygon
 *
 * @example `showPolyline(context, myCurve, DebugColor.MAGENTA)`
 *
 * @source gordonSurface/gordonKnotOps.fs:520-534
 */
export function showPolyline(context is Context, bspline is BSplineCurve, debugColor is DebugColor)
{
    for (var i = 0; i < size(bspline.controlPoints) - 1; i += 1)
    {
        if (tolerantEquals(bspline.controlPoints[i], bspline.controlPoints[i + 1]))
        {
            continue;
        }
        addDebugLine(context, bspline.controlPoints[i], bspline.controlPoints[i + 1], debugColor);
    }

    // Close periodic polygon if needed
    if (bspline.isPeriodic && !firstAndLastCPShouldOverlap(bspline))
    {
        addDebugLine(context, bspline.controlPoints[size(bspline.controlPoints) - 1],
                     bspline.controlPoints[0], debugColor);
    }
}

/**
 * Helper: Check if periodic spline's first and last control points should overlap.
 *
 * For periodic splines starting at parameter 0, the first and last control
 * points already overlap, so we shouldn't draw a closing line.
 *
 * @param bspline {BSplineCurve} : BSpline to check
 * @returns {boolean} : true if control points should overlap
 *
 * @source gordonSurface/gordonKnotOps.fs:536-539
 */
function firstAndLastCPShouldOverlap(bspline is BSplineCurve) returns boolean
{
    return bspline.isPeriodic && bspline.knots[0] == 0;
}

/**
 * Show a point on a curve at a specific parameter value.
 *
 * Evaluates the curve at the given parameter and displays the point
 * as a debug entity. Useful for showing knot locations or other
 * parameter-specific features.
 *
 * @param context {Context} : Onshape context
 * @param curveQuery {Query} : Query for the edge to evaluate
 * @param param {number} : Parameter value (typically in [0,1])
 * @param debugColor {DebugColor} : Color for the debug point
 *
 * @example `showParamOnCurve(context, myEdge, 0.5, DebugColor.RED)`
 *
 * @source gordonSurface/gordonKnotOps.fs:500-508
 */
export function showParamOnCurve(context is Context, curveQuery is Query, param is number,
                                  debugColor is DebugColor)
{
    var point = evEdgeTangentLine(context, {
            "edge" : curveQuery,
            "parameter" : param
    }).origin;

    addDebugPoint(context, point, debugColor);
}

/**
 * Show existing entities in a specified debug color.
 *
 * Highlights entities matched by a query in the given color.
 * Uses Onshape's built-in debug function to visualize geometry.
 *
 * @param context {Context} : Onshape context
 * @param query {Query} : Query selecting entities to visualize
 * @param debugColor {DebugColor} : Color for the debug visualization
 *
 * @example `addDebugEntities(context, qCreatedBy(id), DebugColor.GREEN)`
 *
 * @note This wraps Onshape's standard debug(context, query, debugColor) function
 */
export function addDebugEntities(context is Context, query is Query, debugColor is DebugColor)
{
    debug(context, query, debugColor);
}
