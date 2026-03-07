FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

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
 * Failure handling: opCreateBSplineCurve and opCreateCompositePart failures are
 * caught and silently skipped. This is intentional — a partially successful composite
 * (some sections created, others failed due to degenerate geometry) is still useful
 * for visualization. Callers should not rely on all sections being present.
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
            catch
            {
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
            catch
            {
            }
        }
    }
}
