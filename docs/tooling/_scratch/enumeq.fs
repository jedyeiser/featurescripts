function(context is Context, queries)
{
    return ["eq=" ~ toString(BodyType.WIRE == "WIRE"), "str=" ~ toString(BodyType.WIRE)];
}
