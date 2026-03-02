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
 * Finite-difference tangent in the u-direction at a v-isoparameter on the face.
 * uEdge = 0 → start boundary (u=0), uEdge = 1 → end boundary (u=1).
 */
function faceTangentAtUBoundary(context is Context, face is Query, v is number, uEdge is number) returns Vector
{
    const eps = 1e-5;
    var u0 = (uEdge == 0) ? 0 : (1 - eps);
    var u1 = (uEdge == 0) ? eps : 1;
    return (evFaceTangentPlane(context, { "face" : face, "parameter" : vector(u1, v) }).origin
          - evFaceTangentPlane(context, { "face" : face, "parameter" : vector(u0, v) }).origin) / eps;
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

/**
 * Fit a BSplineCurve through colPts (constant V, varying U) using the
 * definition's degree/tolerance. Mirrors fitIsoCurve but for the U direction.
 */
function fitVIsoCurve(context is Context, definition is map, colPts is array, v is number) returns BSplineCurve
{
    var useG1 = (definition.continuityType == GeometricContinuity.G1 ||
                 definition.continuityType == GeometricContinuity.G2);
    var target;
    if (useG1)
    {
        target = approximationTarget({
            "positions"       : colPts,
            "startDerivative" : faceTangentAtUBoundary(context, definition.face, v, 0),
            "endDerivative"   : faceTangentAtUBoundary(context, definition.face, v, 1)
        });
    }
    else
    {
        target = approximationTarget({ "positions" : colPts });
    }
    return approximateSpline(context, {
        "degree"             : definition.curveDegree,
        "tolerance"          : definition.fitTolerance,
        "isPeriodic"         : false,
        "targets"            : [target],
        "interpolateIndices" : [0, size(colPts) - 1]
    })[0];
}

// ── Editing Logic ─────────────────────────────────────────────────────────────

/**
 * Reset stored manipulator offsets when grid dimensions or continuity type change,
 * since existing offsets correspond to a different point layout.
 *
 * NOTE: Face change is intentionally NOT compared here. Query comparison with !=
 * is unreliable in FeatureScript — it may evaluate true during manipulator drag
 * (when the manipulator change function updates definition[key]), causing Onshape
 * to zero all stored offsets before the feature body runs, producing snap-back.
 * If the user changes the face they should also change grid counts to force a reset,
 * or toggle the continuity type and back.
 */
export function pullSurfaceEditingLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    if (definition.uCurveCount != oldDefinition.uCurveCount ||
        definition.vCurveCount != oldDefinition.vCurveCount ||
        definition.continuityType != oldDefinition.continuityType)
    {
        // Reset the offset array to match the new grid dimensions.
        definition.mpOffsets = makeArray(definition.uCurveCount * definition.vCurveCount, 0 * meter);
    }
    return definition;
}

// ── Manipulator Change Function ───────────────────────────────────────────────

/**
 * Called by Onshape when the user drags a manipulator arrow.
 * Stores the new scalar offset into definition.mpOffsets (flat array) at the
 * index corresponding to grid position (i, j): index = i * vCurveCount + j.
 *
 * Dynamic definition keys (e.g. definition["mp_3_2"]) are rejected by Onshape's
 * validator with "Unknown parameter". Only declared precondition fields survive
 * the round-trip, so all offsets are packed into the single declared mpOffsets array.
 */
export function pullSurfaceManipulator(context is Context, definition is map, newManipulators is map) returns map
{
    var uCount = definition.uCurveCount;
    var vCount = definition.vCurveCount;
    var total  = uCount * vCount;

    // Initialise or resize the offset array (value-type copy from definition).
    var oldOffsets = definition.mpOffsets;
    var offsets    = makeArray(total, 0 * meter);
    if (oldOffsets != undefined)
    {
        var copyLen = size(oldOffsets);
        for (var k = 0; k < total && k < copyLen; k += 1)
        {
            offsets[k] = oldOffsets[k];
        }
    }

    // Update entries for each dragged manipulator.
    // Manipulator keys are "mp_i_j"; scan the grid to find the matching (i, j).
    for (var key, manip in newManipulators)
    {
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                if (("mp_" ~ i ~ "_" ~ j) == key)
                {
                    offsets[i * vCount + j] = manip.offset;
                }
            }
        }
    }

    definition.mpOffsets = offsets;
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
        isInteger(definition.uCurveCount, { (unitless) : [2, 4, 20] } as IntegerBoundSpec);

        annotation { "Name" : "V curve count" }
        isInteger(definition.vCurveCount, { (unitless) : [2, 4, 20] } as IntegerBoundSpec);

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

        // ── Debug group ───────────────────────────────────────────────────────
        annotation { "Group Name" : "Debug", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Show intersection points" }
            definition.showIntersections is boolean;

            annotation { "Name" : "Show iso-curves" }
            definition.showIsoCurves is boolean;

            annotation { "Name" : "Keep U curves" }
            definition.keepUCurves is boolean;

            annotation { "Name" : "Keep V curves" }
            definition.keepVCurves is boolean;

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

        // ── Hidden offset storage ─────────────────────────────────────────────
        // Flat array of scalar length offsets, indexed by i * vCurveCount + j.
        // Must be declared here so Onshape's definition validator accepts writes
        // from pullSurfaceManipulator. Dynamic definition keys are rejected.
        annotation { "Name" : "Manipulator offsets", "UIHint" : UIHint.ALWAYS_HIDDEN }
        definition.mpOffsets is array;
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

        // ── STAGE 2: Place manipulators at free grid points ────────────────────
        // Each free point gets a linearManipulator constrained to the face normal,
        // so the user can only push/pull perpendicular to the surface.
        // offset is a scalar length along that normal direction.
        var offsets  = definition.mpOffsets;
        var total    = uCount * vCount;
        var manipMap = {};
        for (var i = 0; i < uCount; i += 1)
        {
            for (var j = 0; j < vCount; j += 1)
            {
                if (!isPointLocked(i, j, uCount, vCount, definition.continuityType))
                {
                    var flatIdx = i * vCount + j;
                    var off = (offsets != undefined && size(offsets) == total)
                        ? offsets[flatIdx]
                        : (0 * meter);
                    manipMap["mp_" ~ i ~ "_" ~ j] = linearManipulator({
                        "base"      : basePts[i][j],
                        "direction" : baseNormals[i][j],
                        "offset"    : off
                    });
                }
            }
        }
        addManipulators(context, id, manipMap);

        // ── STAGE 3: Apply offsets to produce adjusted grid points ────────────
        // Build adjPts row-by-row using makeArray so each inner array is freshly
        // allocated. Avoid adjPts[i] = basePts[i] then adjPts[i][j] = ... because
        // FeatureScript arrays are value types — modifying a copied inner array
        // does not write back to adjPts[i].
        var adjPts = makeArray(uCount);
        for (var i = 0; i < uCount; i += 1)
        {
            var row = makeArray(vCount);
            for (var j = 0; j < vCount; j += 1)
            {
                var flatIdx = i * vCount + j;
                var off     = (offsets != undefined && size(offsets) == total) ? offsets[flatIdx] : (0 * meter);
                row[j] = (!isPointLocked(i, j, uCount, vCount, definition.continuityType) && off != 0 * meter)
                    ? basePts[i][j] + off * baseNormals[i][j]
                    : basePts[i][j];
            }
            adjPts[i] = row;
        }

        // Fit iso-U curves through the adjusted points.
        var adjCurves = [];
        for (var i = 0; i < uCount; i += 1)
        {
            adjCurves = append(adjCurves, fitIsoCurve(context, definition, adjPts[i], uParams[i]));
        }

        // Fit iso-V curves (constant V, varying U) for debug visualization.
        // Collected into vIsoCurves regardless of flags — cost is low and they
        // are needed for both showIsoCurves and showCPPolygons.
        var vIsoCurves = [];
        for (var j = 0; j < vCount; j += 1)
        {
            var colPts = makeArray(uCount);
            for (var i = 0; i < uCount; i += 1) colPts[i] = adjPts[i][j];
            vIsoCurves = append(vIsoCurves, fitVIsoCurve(context, definition, colPts, vParams[j]));
        }

        // Create iso-curve bodies (deleted below unless keep flags are set).
        if (definition.showIsoCurves)
        {
            for (var i = 0; i < uCount; i += 1)
            {
                opCreateBSplineCurve(context, id + ("isoU_" ~ i), { "bSplineCurve" : adjCurves[i] });
            }
            for (var j = 0; j < vCount; j += 1)
            {
                opCreateBSplineCurve(context, id + ("isoV_" ~ j), { "bSplineCurve" : vIsoCurves[j] });
            }
        }

        // CP polygon lines for both families (debug lines, no persistent geometry).
        if (definition.showCPPolygons)
        {
            for (var i = 0; i < uCount; i += 1)
            {
                var ucps = adjCurves[i].controlPoints;
                for (var k = 0; k < size(ucps) - 1; k += 1)
                {
                    addDebugLine(context, ucps[k], ucps[k + 1], DebugColor.CYAN);
                }
            }
            for (var j = 0; j < vCount; j += 1)
            {
                var vcps = vIsoCurves[j].controlPoints;
                for (var k = 0; k < size(vcps) - 1; k += 1)
                {
                    addDebugLine(context, vcps[k], vcps[k + 1], DebugColor.GREEN);
                }
            }
        }

        if (definition.printCurveData)
        {
            for (var i = 0; i < uCount; i += 1) printCurve(adjCurves[i], "Iso-U " ~ i, PrintFormat.METADATA);
            for (var j = 0; j < vCount; j += 1) printCurve(vIsoCurves[j], "Iso-V " ~ j, PrintFormat.METADATA);
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

        // ── Cleanup: delete iso-curve bodies unless keep flags are set ─────────
        if (definition.showIsoCurves)
        {
            if (!definition.keepUCurves)
            {
                var uCurveEntities = [];
                for (var i = 0; i < uCount; i += 1)
                {
                    uCurveEntities = append(uCurveEntities, qCreatedBy(id + ("isoU_" ~ i)));
                }
                opDeleteBodies(context, id + "deleteU", { "entities" : qUnion(uCurveEntities) });
            }
            if (!definition.keepVCurves)
            {
                var vCurveEntities = [];
                for (var j = 0; j < vCount; j += 1)
                {
                    vCurveEntities = append(vCurveEntities, qCreatedBy(id + ("isoV_" ~ j)));
                }
                opDeleteBodies(context, id + "deleteV", { "entities" : qUnion(vCurveEntities) });
            }
        }
    });
