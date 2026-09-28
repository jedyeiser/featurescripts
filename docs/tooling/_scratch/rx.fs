function(context is Context, queries)
{
    const lit = replace("4101 PLAN", "[^A-Za-z0-9 _-]", ".");
    return ["a=" ~ toString(match("4101 PLAN", ".*" ~ lit ~ ".*").hasMatch), "b=" ~ toString(match("4501 PLAN", ".*" ~ lit ~ ".*").hasMatch)];
}
