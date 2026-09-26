function(context is Context, queries)
{
    var out = [];
    const data = getAttribute(context, { "entity" : qOrigin(EntityType.BODY), "name" : "CrossSectionAnalysis" });
    if (data == undefined)
    {
        return ["none"];
    }
    const nm2 = newton * meter * meter;
    for (var entry in data)
    {
        out = append(out, "FEAT|" ~ entry.key ~ "|" ~ toString(entry.value.details.beamAnalysis));
        for (var s in entry.value.details.crossSections)
        {
            out = append(out, "ROW|" ~ entry.key ~ "|" ~ s.stationNumber ~ "|" ~ toString(s.xCoord / millimeter)
                ~ "|" ~ (s.EI_eff == undefined ? "nan" : toString(s.EI_eff / nm2))
                ~ "|" ~ (s.GJ_eff == undefined ? "nan" : toString(s.GJ_eff / nm2))
                ~ "|" ~ (s.neutralAxisY == undefined ? "nan" : toString(s.neutralAxisY / millimeter))
                ~ "|" ~ toString(s.linealDensity) ~ "|" ~ toString(s.boundingBox));
        }
    }
    return out;
}
