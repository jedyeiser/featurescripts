function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)))
    {
        const nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME });
        if (nm != undefined && match(nm, ".* ST .*").hasMatch)
        {
            for (var e in evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE)))
            {
                out = append(out, nm ~ " | " ~ evCurveDefinition(context, { "edge" : e }).curveType);
            }
        }
    }
    return out;
}
