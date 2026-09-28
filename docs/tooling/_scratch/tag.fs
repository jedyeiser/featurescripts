function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qHasAttribute(qEverything(EntityType.BODY), "publishStationTable")))
    {
        const a = getAttribute(context, { "entity" : b, "name" : "publishStationTable" });
        out = append(out, a.title ~ " | " ~ a.schema ~ " | rows " ~ size(a.rows));
    }
    return out;
}
