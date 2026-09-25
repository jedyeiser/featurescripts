// Dense samples (arc-length parameters) of FULL_BASELINE's edges and Wrapped_profile's edges, for the adaptive
// sampling figure. READ-ONLY eval ("Unwrap_Testing Copy 2").
function(context is Context, queries)
{
    var out = [];
    for (var w in [["FULL_BASELINE", "RjRL", 801], ["Wrapped_profile", "RjRP", 1601]])
    {
        for (var e in evaluateQuery(context, qOwnedByBody(qTransient(w[1]), EntityType.EDGE)))
        {
            const def = evCurveDefinition(context, { "edge" : e });
            var cps = (def is BSplineCurve) ? size(def.controlPoints) : 0;
            var s = "D " ~ w[0] ~ " " ~ cps ~ " " ~ (evLength(context, { "entities" : e }) / millimeter) ~ " |";
            for (var q in evEdgeTangentLines(context, { "edge" : e, "parameters" : range(0, 1, w[2]) }))
            {
                const p = q.origin / millimeter;
                s = s ~ " " ~ p[0] ~ " " ~ p[2];
            }
            out = append(out, s);
        }
    }
    return out;
}
