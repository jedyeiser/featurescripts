function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "publishStationTable")))
    {
        const a = getAttribute(context, { "entity" : b, "name" : "publishStationTable" });
        out = append(out, "VIEW " ~ a.title ~ " stations " ~ size(a.rows));
    }
    for (var b in evaluateQuery(context, qBodyType(qEverything(EntityType.BODY), BodyType.WIRE)))
    {
        const nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME });
        if (nm != undefined && match(nm, ".*OUTLINE.*").hasMatch)
        {
            const es = evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE));
            var nSpline = 0;
            var shortest = 1e9;
            for (var e in es)
            {
                if (evCurveDefinition(context, { "edge" : e }).curveType == CurveType.SPLINE) { nSpline += 1; }
                const L = evLength(context, { "entities" : e }) / millimeter;
                if (L < shortest) { shortest = L; }
            }
            out = append(out, "OUTLINE " ~ nm ~ " edges " ~ size(es) ~ " splines " ~ nSpline ~ " shortest " ~ roundToPrecision(shortest, 4) ~ " mm");
        }
    }
    return out;
}
