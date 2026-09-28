function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        const nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME });
        if (nm == "2D_PERIPHERY" || nm == "TOP_SURFACE")
        {
            for (var f in evaluateQuery(context, qOwnedByBody(b, EntityType.FACE)))
            {
                const s = evSurfaceDefinition(context, { "face" : f });
                out = append(out, nm ~ " | " ~ s.surfaceType ~ " | " ~ (s is Plane ? toString(s.normal) ~ " z " ~ toString(s.origin[2] / millimeter) : ""));
            }
            const bb = evBox3d(context, { "topology" : b, "tight" : true });
            out = append(out, nm ~ " bbox z " ~ toString(bb.minCorner[2] / millimeter) ~ " .. " ~ toString(bb.maxCorner[2] / millimeter));
        }
    }
    return out;
}
