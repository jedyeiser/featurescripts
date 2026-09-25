function(context is Context, queries)
{
    var out = [];
    for (var b in evaluateQuery(context, qEverything(EntityType.BODY)))
    {
        var t = "";
        for (var bt in [BodyType.SOLID, BodyType.SHEET, BodyType.WIRE, BodyType.MATE_CONNECTOR])
        {
            if (!isQueryEmpty(context, qBodyType(b, bt))) { t = toString(bt); }
        }
        if (t == "") { continue; }
        const bb = evBox3d(context, { "topology" : b, "tight" : true });
        const nm = getProperty(context, { "entity" : b, "propertyType" : PropertyType.NAME });
        out = append(out, toString(b.transientId) ~ " | " ~ t ~ " | " ~ nm ~ " | faces " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.FACE))) ~ " edges " ~ size(evaluateQuery(context, qOwnedByBody(b, EntityType.EDGE))) ~ " | " ~ toString(roundToPrecision(bb.minCorner[0] / millimeter, 1)) ~ "," ~ toString(roundToPrecision(bb.minCorner[1] / millimeter, 1)) ~ "," ~ toString(roundToPrecision(bb.minCorner[2] / millimeter, 1)) ~ " .. " ~ toString(roundToPrecision(bb.maxCorner[0] / millimeter, 1)) ~ "," ~ toString(roundToPrecision(bb.maxCorner[1] / millimeter, 1)) ~ "," ~ toString(roundToPrecision(bb.maxCorner[2] / millimeter, 1)));
    }
    return out;
}
