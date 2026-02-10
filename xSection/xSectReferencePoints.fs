FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

// xSectMaterials (for tryGetKey helper)
import(path : "xSection/xSectMaterials.fs", version : "");

/**
 * XSECTION REFERENCE POINTS MODULE
 * =================================
 *
 * Reference point resolution for FCP (Front Climbing Point) and ACP (Aft Climbing Point).
 *
 * Resolves user-selected reference entities (vertices, mate connectors, planar faces)
 * to world X coordinates for beam stiffness calculations.
 *
 * Extracted from xSect.fs (lines 978-1046) to separate geometric resolution from main feature.
 */

/**
 * Resolve a reference point query (FCP or ACP) to a world X coordinate.
 *
 * Accepts:
 *   - Vertex: projects vertex position onto world X
 *   - Mate connector: projects mate connector origin onto world X
 *   - Planar face: validates normal is parallel to world X (no Y component),
 *     then uses the plane origin X coordinate
 *
 * @param context {Context}
 * @param refQuery {Query} : The FCP or ACP query
 * @param edgeQuery {Query} : The cross-section edge (for future plane intersection)
 * @returns : World X coordinate (ValueWithUnits), or undefined if unresolvable
 */
export function resolveReferencePointX(context is Context, refQuery is Query, edgeQuery is Query)
{
    var entities = evaluateQuery(context, refQuery);
    if (size(entities) == 0)
        return undefined;

    // --- Try as vertex ---
    try
    {
        var vertexEntities = evaluateQuery(context, qEntityFilter(refQuery, EntityType.VERTEX));
        if (size(vertexEntities) > 0)
        {
            var pos = evVertexPoint(context, { "vertex" : vertexEntities[0] });
            return pos[0];
        }
    }
    catch (e)
    {
        println("WARNING: Vertex resolution failed - " ~ e);
    }

    // --- Try as mate connector ---
    try
    {
        var mateEntities = evaluateQuery(context, qBodyType(refQuery, BodyType.MATE_CONNECTOR));
        if (size(mateEntities) > 0)
        {
            var csys = evMateConnector(context, { "mateConnector" : mateEntities[0] });
            return csys.origin[0];
        }
    }
    catch (e)
    {
        println("WARNING: Mate connector resolution failed - " ~ e);
    }

    // --- Try as planar face ---
    try
    {
        var faceEntities = evaluateQuery(context, qGeometry(refQuery, GeometryType.PLANE));
        if (size(faceEntities) > 0)
        {
            var facePlane = evPlane(context, { "face" : faceEntities[0] });

            // Validate: plane normal must be parallel to world X (no Y component)
            if (abs(facePlane.normal[1]) > 0.01)
            {
                throw regenError("FCP/ACP plane must be normal to the ski axis (world X). This plane has a Y component in its normal.");
            }

            return facePlane.origin[0];
        }
    }
    catch (e)
    {
        if (e is map && tryGetKey(e, "message") != undefined)
            throw e;  // Re-throw our validation error
        println("WARNING: Planar face resolution failed - " ~ e);
    }

    return undefined;
}
