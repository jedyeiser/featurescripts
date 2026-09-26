function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)))
    {
        const bb = evBox3d(context, { "topology" : b });
        out = append(out, "W|" ~ getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME }) ~ "|" ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE))) ~ "|" ~ toString(bb.minCorner / millimeter) ~ toString(bb.maxCorner / millimeter));
    }
    return out;
}
