FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

//import constEnums (export - needed for enums in preconditions)
export import(path : "050a4670bd42b2ca8da04540", version : "b463eaf5c39ae77152ed2484");
//import gordonCurveCompat
import(path : "b9e1608a507a242d87720d9b", version : "1f36dbe055928048afcc6fe0");
//import gordonSurface
import(path : "b3c74a9035256a2ff6bd0004", version : "d43b8d12f8c9732a31d21430");
//import continuityTools
import(path : "6db2a56b5418f71818d7a607", version : "f5e90edbacec0ec0ff42136f");
//import debugTools
import(path : "3f40c735a406f3df927e0b13", version : "ca97f371da515817e2e1c16b");

/**
 * Allows users to push and pull surface around using manipulator functions.
 * Utilities are built in to simplify complex surfaces to a few manipulators.
 *
 * User specifies continuity type to preserve:
 *   G0 - boundary points locked only
 *   G1 - boundary + first interior row locked (tangent continuity)
 *   G2 - boundary + first two interior rows locked (curvature continuity)
 *
 * User provides U and V dimension. Isoparametric intersection points are
 * computed on the face, manipulators placed at free (unlocked) points.
 * Offsets are stored in definition.manipulatorOffsets and applied at regen.
 * Adjusted points are fit to iso-U BSpline curves, then skinned to a surface.
 */

// ── Private Helpers ──────────────────────────────────────────────────────────

/**
 * Returns true if grid point (i, j) must remain fixed to preserve the
 * requested boundary continuity.
 */
function isPointLocked(i is number, j is number, uCount is number, vCount is number, continuity is GeometricContinuity) returns boolean
{
    var boundary = (i == 0 || i == uCount - 1 || j == 0 || j == vCount - 1);
    if (continuity == GeometricContinuity.G0) return boundary;
    var g1row = (i == 1 || i == uCount - 2 || j == 1 || j == vCount - 2);
    if (continuity == GeometricContinuity.G1) return boundary || g1row;
    var g2row = (i == 2 || i == uCount - 3 || j == 2 || j == vCount - 3);
    return boundary || g1row || g2row;
}

/**
 * Finite-difference tangent in the v-direction at a u-isoparameter on the face.
 * vEdge = 0 → start boundary (v=0), vEdge = 1 → end boundary (v=1).
 */
function faceTangentAtVBoundary(context is Context, face is Query, u is number, vEdge is number) returns Vector
{
    const eps = 1e-5;
    var v0 = (vEdge == 0) ? 0 : (1 - eps);
    var v1 = (vEdge == 0) ? eps : 1;
    return (evFaceTangentPlane(context, { "face" : face, "parameter" : vector(u, v1) }).origin
          - evFaceTangentPlane(context, { "face" : face, "parameter" : vector(u, v0) }).origin) / eps;
}

/**
 * Fit a BSplineCurve through rowPts using the definition's degree/tolerance.
 * If G1 or G2 continuity is requested, boundary tangents are passed to the
 * spline fitter so the iso-curve respects the face tangent at v=0 and v=1.
 */
function fitIsoCurve(context is Context, definition is map, rowPts is array, u is number) returns BSplineCurve
{
    var useG1 = (definition.continuityType == GeometricContinuity.G1 ||
                 definition.continuityType == GeometricContinuity.G2);
    var target;
    if (useG1)
    {
        target = approximationTarget({
            "positions"       : rowPts,
            "startDerivative" : faceTangentAtVBoundary(context, definition.face, u, 0),
            "endDerivative"   : faceTangentAtVBoundary(context, definition.face, u, 1)
        });
    }
    else
    {
        target = approximationTarget({ "positions" : rowPts });
    }
    return approximateSpline(context, {
        "degree"             : definition.curveDegree,
        "tolerance"          : definition.fitTolerance,
        "isPeriodic"         : false,
        "targets"            : [target],
        "interpolateIndices" : [0, size(rowPts) - 1]
    })[0];
}

// ── Editing Logic ─────────────────────────────────────────────────────────────

/**
 * Reset stored manipulator offsets whenever the grid layout or face changes,
 * since previous offsets correspond to a different point distribution.
 */
export function pullSurfaceEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    if (definition.uCurveCount != oldDefinition.uCurveCount ||
        definition.vCurveCount != oldDefinition.vCurveCount ||
        definition.continuityType != oldDefinition.continuityType ||
        definition.face != oldDefinition.face)
    {
        definition.manipulatorOffsets = {};
    }
    return definition;
}

// ── Manipulator Change Function ───────────────────────────────────────────────

/**
 * Called by Onshape when the user drags a manipulator arrow.
 * Stores the new offset into definition.manipulatorOffsets keyed by
 * the manipulator name ("mp_i_j").
 */
export function pullSurfaceManipulator(context is Context, definition is map, newManipulators is map) returns map
{
    for (var key, manip in newManipulators)
    {
        definition.manipulatorOffsets = insert(definition.manipulatorOffsets, key, manip.offset);
    }
    return definition;
}

// ── Feature Definition ────────────────────────────────────────────────────────

annotation { "Feature Type Name" : "Pull surface",
             "Editing Logic Function" : "pullSurfaceEditingLogic",
             "Manipulator Change Function" : "pullSurfaceManipulator",
             "Feature Type Description" : "Push/pull surface control points with configurable boundary continuity." }
export const pullSurface = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        // ── Face ──────────────────────────────────────────────────────────────
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1 }
        definition.face is Query;

        // ── Grid dimensions ───────────────────────────────────────────────────
        annotation { "Name" : "U curve count" }
        isInteger(definition.uCurveCount, { (unitless) : [2, 4, 20] });

        annotation { "Name" : "V curve count" }
        isInteger(definition.vCurveCount, { (unitless) : [2, 4, 20] });

        // ── Continuity ────────────────────────────────────────────────────────
        annotation { "Name" : "Continuity" }
        definition.continuityType is GeometricContinuity;

        if (definition.continuityType == GeometricContinuity.G2)
        {
            annotation { "Name" : "G2 mode" }
            definition.g2Mode is G2Mode;
        }

        // ── Output options ────────────────────────────────────────────────────
        annotation { "Name" : "Create surface", "Default" : true }
        definition.createSurface is boolean;

        annotation { "Name" : "Replace face", "Default" : false }
        definition.replaceFace is boolean;

        // ── Hidden offset storage ─────────────────────────────────────────────
        annotation { "Name" : "mp_offsets", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.manipulatorOffsets is map;

        // ── Debug group ───────────────────────────────────────────────────────
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show intersection points" }
            definition.showIntersections is boolean;

            annotation { "Name" : "Show iso-curves" }
            definition.showIsoCurves is boolean;

            annotation { "Name" : "Show control point polygons" }
            definition.showCPPolygons is boolean;

            annotation { "Name" : "Print curve data" }
            definition.printCurveData is boolean;
        }

        // ── Approximation group ───────────────────────────────────────────────
        annotation { "Group Name" : "Approximation", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Degree" }
            isInteger(definition.curveDegree, SurfDegreeBounds);

            annotation { "Name" : "Tolerance" }
            isLength(definition.fitTolerance, FitToleranceBounds);
        }
    }
    {
        var uCount = definition.uCurveCount;
        var vCount = definition.vCurveCount;

        // Build uniform parameter grids spanning [0, 1]
        var uParams = makeArray(uCount);
        var vParams = makeArray(vCount);
        for (var i = 0; i < uCount; i += 1) uParams[i] = i / (uCount - 1);
        for (var j = 0; j < vCount; j += 1) vParams[j] = j / (vCount - 1);

        // ── STAGE 1: Base intersection points ──────────────────────────────────
        // Sample the face at each (u, v) grid node to get 3D base positions and
        // surface normals. Normals are needed to orient the linear manipulators.
        var basePts     = makeArray(uCount);
        var baseNormals = makeArray(uCount);
        for (var i = 0; i < uCount; i += 1)
        {
            basePts[i]     = makeArray(vCount);
            baseNormals[i] = makeArray(vCount);
            for (var j = 0; j < vCount; j += 1)
            {
                var plane = evFaceTangentPlane(context,
                    { "face" : definition.face, "parameter" : vector(uParams[i], vParams[j]) });
                basePts[i][j]     = plane.origin;
                baseNormals[i][j] = plane.normal;
                if (definition.showIntersections)
                {
                    addDebugPoint(context, basePts[i][j], DebugColor.BLUE);
                }
            }
        }

        // Fit initial iso-U curves through base points (no offsets yet).
        // Used for the showIsoCurves diagnostic only; adjCurves are used for
        // the surface build.
        if (definition.showIsoCurves)
        {
            for (var i = 0; i < uCount; i += 1)
            {
                var baseCurve = fitIsoCurve(context, definition, basePts[i], uParams[i]);
                opCreateBSplineCurve(context, id + ("isoU_base_" ~ i), { "bSplineCurve" : baseCurve });
            }
        }

        // ── STAGE 2: Place manipulators at free grid points ────────────────────
        // Each free point gets a linearManipulator constrained to the face normal,
        // so the user can only push/pull perpendicular to the surface.
        // offset is a scalar length along that normal direction.
        var manipMap = {};
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                if (!isPointLocked(i, j, uCount, vCount, definition.continuityType))
                {
                    var key = "mp_" ~ i ~ "_" ~ j;
                    var storedOff = definition.manipulatorOffsets[key];
                    var off = (storedOff != undefined) ? storedOff : (0 * meter);
                    manipMap = insert(manipMap, key,
                        linearManipulator({
                            "base"      : basePts[i][j],
                            "direction" : baseNormals[i][j],
                            "offset"    : off
                        }));
                }
            }
        }
        addManipulators(context, id, manipMap);

        // ── STAGE 3: Apply offsets and re-fit adjusted iso-U curves ───────────
        // Build adjPts by applying any stored manipulator offsets to basePts.
        var adjPts = makeArray(uCount);
        for (var i = 0; i < uCount; i += 1)
        {
            adjPts[i] = basePts[i];
            for (var j = 0; j < vCount; j += 1)
            {
                if (!isPointLocked(i, j, uCount, vCount, definition.continuityType))
                {
                    var key = "mp_" ~ i ~ "_" ~ j;
                    var off = definition.manipulatorOffsets[key];
                    if (off != undefined)
                    {
                        adjPts[i][j] = basePts[i][j] + off * baseNormals[i][j];
                    }
                }
            }
        }

        // Fit iso-U curves through the adjusted points.
        var adjCurves = [];
        for (var i = 0; i < uCount; i += 1)
        {
            var curve = fitIsoCurve(context, definition, adjPts[i], uParams[i]);
            adjCurves = append(adjCurves, curve);

            if (definition.showCPPolygons)
            {
                var cps = curve.controlPoints;
                for (var k = 0; k < size(cps) - 1; k += 1)
                {
                    addDebugLine(context, cps[k], cps[k + 1], DebugColor.CYAN);
                }
            }

            if (definition.printCurveData)
            {
                printCurve(curve, "Iso-U " ~ i, PrintFormat.METADATA);
            }
        }

        // ── STAGE 4: Build skinning surface ────────────────────────────────────
        if (definition.createSurface)
        {
            var compatCurves = makeCurvesCompatible(context, id + "compat", adjCurves);
            var surf = createSkinningSurface(context, id + "skin", compatCurves,
                                             definition.curveDegree, uParams);
            opCreateBSplineSurface(context, id + "pullSurf", { "bSplineSurface" : surf });

            // ── STAGE 5: Replace original face with the new surface ────────────
            if (definition.replaceFace)
            {
                var newFace = qCreatedBy(id + "pullSurf", EntityType.FACE);
                opReplaceFace(context, id + "replace", {
                    "replaceFaces" : definition.face,
                    "templateFace" : newFace
                });
            }
        }
    });
