FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// xSectPredicates (for DEBUG_COLOR_SEQUENCE and XSectionDebugType enum)
import(path : "17142132b20343b5f125e7e7", version : "0e25c0a56662fafd96d4d17b");
// tools/debug - provides debugControlPolygon
import(path : "b1e8bfe71f67389ca210ed8b/910a6d7a356c2832de31817a/8944e3e431de4929b0a28fbc", version : "889ff7e9c358da182dc0bf8a");

/**
 * XSECTION DEBUG MODULE
 * ======================
 *
 * Debug visualization and console output for cross-section analysis.
 *
 * Provides:
 * - Debug rendering of curves, points, and triangle meshes
 * - Console output of geometric data
 * - Per-body and per-section filtering
 *
 * Extracted from xSect.fs (lines 1520-1698) to separate debug code from production logic.
 */

/**
 * Render debug visualization for cross-sections.
 *
 * Supports three visualization modes:
 * - EDGES: Show B-spline curve control polygons
 * - POINTS: Show perimeter points
 * - MESH: Show triangulation mesh
 *
 * @param context {Context}
 * @param id {Id} : Feature ID (unused, but kept for API consistency)
 * @param data {map} : Full cross-section data
 * @param definition {map} : Feature definition with debug settings
 */
export function debugVisualization(context is Context, id is Id, data is map, definition is map)
{
    var sectionIndices = [];
    if (definition.debugAllXSections)
    {
        for (var i = 0; i < size(data.crossSections); i += 1)
            sectionIndices = append(sectionIndices, i);
    }
    else
    {
        sectionIndices = mapArray(definition.debugXSections, function(x) { return x.xSectionNum - 1; });
    }

    // Get selected debug body indices (only if not debugging all bodies)
    var debugBodyIndices = [];
    if (!definition.debugAllBodies)
    {
        var debugBodiesQuery = evaluateQuery(context, definition.debugBodies);
        for (var i = 0; i < size(data.bodies); i += 1)
        {
            for (var debugBody in debugBodiesQuery)
            {
                if (areQueriesEquivalent(context, data.bodies[i].bodyQuery, debugBody))
                {
                    debugBodyIndices = append(debugBodyIndices, i);
                    break;
                }
            }
        }
    }

    for (var idx in sectionIndices)
    {
        if (idx < 0 || idx >= size(data.crossSections))
            continue;

        var section = data.crossSections[idx];
        var sectionPoints = section.sectionPoints;

        if (definition.printBodyData)
        {
            println("=== Cross Section " ~ (idx + 1) ~ " ===");
            println("Total section points: " ~ size(sectionPoints));
        }

        for (var bodyInfo in section.bodyData)
        {
            var bodyIdx = bodyInfo.bodyIdx;

            // Skip if filtering bodies and this one isn't selected
            if (!definition.debugAllBodies && !isIn(bodyIdx, debugBodyIndices))
                continue;

            var color = DEBUG_COLOR_SEQUENCE[bodyIdx % size(DEBUG_COLOR_SEQUENCE)];

            if (definition.printBodyData)
            {
                println("--- Body " ~ bodyIdx ~ " ---");
                println("  Groups: " ~ size(bodyInfo.groups));
                printGroupData(bodyInfo.groups, sectionPoints, 1, definition.printTriangles);
            }

            if (definition.debugType == XSectionDebugType.EDGES)
            {
                var bodyCurves = filter(section.bSplineCurves, function(c) {
                    return isIn(bodyIdx, c.bodyIndices);
                });
                for (var curveData in bodyCurves)
                {
                    debugControlPolygon(context, curveData.bSplineCurve, color);
                }
            }
            else if (definition.debugType == XSectionDebugType.POINTS)
            {
                debugGroupPoints(context, bodyInfo.groups, sectionPoints, color);
            }
            else if (definition.debugType == XSectionDebugType.MESH)
            {
                debugGroupMesh(context, bodyInfo.groups, sectionPoints, color);
            }
        }
    }
}

/**
 * Recursively draw perimeter points from groups and subgroups.
 */
function debugGroupPoints(context is Context, groups is array, sectionPoints is array, color is DebugColor)
{
    for (var group in groups)
    {
        for (var idx in group.perimeterPointIndices)
        {
            addDebugPoint(context, sectionPoints[idx].point3D, color);
        }
        debugGroupPoints(context, group.subgroups, sectionPoints, color);
    }
}

/**
 * Recursively draw triangle mesh from groups and subgroups.
 */
function debugGroupMesh(context is Context, groups is array, sectionPoints is array, color is DebugColor)
{
    for (var group in groups)
    {
        for (var tri in group.triangles)
        {
            var p0 = sectionPoints[tri[0]].point3D;
            var p1 = sectionPoints[tri[1]].point3D;
            var p2 = sectionPoints[tri[2]].point3D;

            if(norm(p1 - p0) > 1e-6 * meter)
            {
                addDebugLine(context, p0, p1, color);
            }

            if(norm(p2 - p1) > 1e-6 * meter)
            {
                addDebugLine(context, p1, p2, color);
            }

            if(norm(p0 - p2) > 1e-6 * meter)
            {
                addDebugLine(context, p2, p0, color);
            }
        }
        debugGroupMesh(context, group.subgroups, sectionPoints, color);
    }
}

/**
 * Recursively print group data for debugging.
 */
function printGroupData(groups is array, sectionPoints is array, depth is number, printTriangles is boolean)
{
    var indent = "";
    for (var d = 0; d < depth; d += 1)
        indent = indent ~ "  ";

    for (var g = 0; g < size(groups); g += 1)
    {
        var group = groups[g];
        println(indent ~ "Group " ~ g ~ ":");
        println(indent ~ "  Perimeter points: " ~ size(group.perimeterPointIndices));
        println(indent ~ "  Triangles: " ~ size(group.triangles));
        println(indent ~ "  Area: " ~ group.sectionProperties.area);
        println(indent ~ "  Subgroups: " ~ size(group.subgroups));

        println(indent ~ "  Perimeter coordinates (2D):");
        for (var i = 0; i < size(group.perimeterPointIndices); i += 1)
        {
            var ptIdx = group.perimeterPointIndices[i];
            var pt2D = sectionPoints[ptIdx].point2D;
            println(indent ~ "    [" ~ i ~ "] idx=" ~ ptIdx ~ " X=" ~ pt2D[0] ~ " Y=" ~ pt2D[1]);
        }

        if (printTriangles)
        {
            println(indent ~ "  Triangle indices:");
            for (var t = 0; t < size(group.triangles); t += 1)
            {
                var tri = group.triangles[t];
                var p0 = sectionPoints[tri[0]].point2D;
                var p1 = sectionPoints[tri[1]].point2D;
                var p2 = sectionPoints[tri[2]].point2D;
                println(indent ~ "    [" ~ t ~ "] " ~ tri[0] ~ " -> " ~ tri[1] ~ " -> " ~ tri[2]);
                println(indent ~ "      (" ~ p0[0] ~ ", " ~ p0[1] ~ ")");
                println(indent ~ "      (" ~ p1[0] ~ ", " ~ p1[1] ~ ")");
                println(indent ~ "      (" ~ p2[0] ~ ", " ~ p2[1] ~ ")");
            }
        }

        if (size(group.subgroups) > 0)
        {
            printGroupData(group.subgroups, sectionPoints, depth + 1, printTriangles);
        }
    }
}

export function debugControlPolygon(context is Context, bSplineCurve is BSplineCurve, color is DebugColor)
{
    for (var i = 1; i < size(bSplineCurve.controlPoints); i += 1)
    {
        addDebugLine(context, bSplineCurve.controlPoints[i-1], bSplineCurve.controlPoints[i], color);
    }
}
