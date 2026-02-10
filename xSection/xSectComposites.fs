FeatureScript 2878;
import(path : "onshape/std/common.fs", version : "2878.0");

/**
 * XSECTION COMPOSITES MODULE
 * ===========================
 *
 * Composite wire body generation from cross-section B-spline curves.
 *
 * Creates composite part bodies from deduplicated B-spline curves at each cross-section.
 * Optionally generates closed composite parts for visualization/export.
 *
 * Extracted from xSect.fs (lines 1479-1511) to separate geometry creation from analysis.
 */

/**
 * Create composite wire bodies from cross-section data.
 *
 * For each cross-section, creates individual B-spline curve bodies and combines them
 * into a closed composite part. Each composite is named "XSect N Composite" where
 * N is the section index.
 *
 * @param context {Context}
 * @param id {Id} : Base feature ID for operations
 * @param data {map} : Cross-section data with bSplineCurves array
 */
export function createCompositeWires(context is Context, id is Id, data is map)
{
    for (var i = 0; i < size(data.crossSections); i += 1)
    {
        var section = data.crossSections[i];
        var createdBodies = [];

        for (var s = 0; s < size(section.bSplineCurves); s += 1)
        {
            var curve = section.bSplineCurves[s].bSplineCurve;

            try
            {
                opCreateBSplineCurve(context, id + ("curve" ~ i ~ "_" ~ s), {
                        "bSplineCurve" : curve
                });

                createdBodies = append(createdBodies, qCreatedBy(id + ("curve" ~ i ~ "_" ~ s), EntityType.BODY));
            }
            catch (e)
            {
                println("WARNING: Failed to create B-spline curve at section " ~ i ~ ", curve " ~ s ~ " - " ~ e);
            }
        }

        if (size(createdBodies) > 0)
        {
            try
            {
                opCreateCompositePart(context, id + ("composite" ~ i), {
                        "bodies" : qUnion(createdBodies),
                        "closed" : true
                });

                setProperty(context, {
                        "entities" : qCompositePartsContaining(qUnion(createdBodies)),
                        "propertyType" : PropertyType.NAME,
                        "value" : "XSect " ~ i ~ " Composite"
                });
            }
            catch (e)
            {
                println("WARNING: Failed to create composite part at section " ~ i ~ " - " ~ e);
            }
        }
    }
}
