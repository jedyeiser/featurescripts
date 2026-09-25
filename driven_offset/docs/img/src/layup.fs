// Reference wires, Front-plane (y = 0) sections of the test ski's parts, and the flat topsheet's outline.
// READ-ONLY: runs in the FS eval API's throwaway context ("Unwrap_Testing Copy 2", 681a5825e353d376c88224eb).
function(context is Context, queries)
{
    const id = newId();
    var out = [];
    // 1. Wires: per edge its kind, length and 61 samples [x y z curvature].
    for (var w in [["REF_WIRE", "RtjD"], ["FULL_BASELINE", "RjRL"], ["Wrapped_profile", "RjRP"]])
    {
        for (var e in evaluateQuery(context, qOwnedByBody(qTransient(w[1]), EntityType.EDGE)))
        {
            const def = evCurveDefinition(context, { "edge" : e });
            var kind = "other";
            if (def is Line) { kind = "line"; }
            else if (def is Circle) { kind = "arc R" ~ toString(def.radius / millimeter); }
            else if (def is BSplineCurve) { kind = "bspline deg " ~ def.degree ~ " cps " ~ size(def.controlPoints); }
            var s = "W " ~ w[0] ~ " | " ~ kind ~ " | " ~ toString(evLength(context, { "entities" : e }) / millimeter) ~ " |";
            for (var c in evEdgeCurvatures(context, { "edge" : e, "parameters" : range(0, 1, 61) }))
            {
                const p = c.frame.origin / millimeter;
                s = s ~ " " ~ p[0] ~ " " ~ p[1] ~ " " ~ p[2] ~ " " ~ (c.curvature * millimeter);
            }
            out = append(out, s);
        }
    }
    // 2. Front-plane sections of the parts.
    opPlane(context, id + "front", { "plane" : plane(vector(900, 0, 0) * millimeter, vector(0, 1, 0), vector(1, 0, 0)), "width" : 4 * meter, "height" : 1 * meter });
    var k = 0;
    for (var b in [["Topsheet", "RnRD"], ["6005", "Rtjf"], ["4310", "Rtjj"], ["CORE", "Rtjn"], ["4802", "Rtj7"], ["4803", "Rtj3"],
                   ["4305", "RtjT"], ["Tip-Mat", "RtjX"], ["Tail-Mat", "Rtjb"], ["Tip-Shear", "Rtj/"], ["Tail-Shear", "StjDB"], ["base 4101", "RtjP"]])
    {
        k += 1;
        try silent
        {
            opIntersectFaces(context, id + ("ix" ~ k), { "tools" : qCreatedBy(id + "front", EntityType.FACE), "targets" : qOwnedByBody(qTransient(b[1]), EntityType.FACE) });
        }
        catch
        {
            out = append(out, "F " ~ b[0] ~ " FAIL");
            continue;
        }
        for (var e in evaluateQuery(context, qCreatedBy(id + ("ix" ~ k), EntityType.EDGE)))
        {
            var s = "F " ~ b[0] ~ " |";
            for (var q in evEdgeTangentLines(context, { "edge" : e, "parameters" : range(0, 1, 25) }))
            {
                const p = q.origin / millimeter;
                s = s ~ " " ~ p[0] ~ " " ~ p[2];
            }
            out = append(out, s);
        }
    }
    // 3. The flat topsheet (output of "Unwrap Topsheet (plate, own section)"): edges of its lowest face.
    const flat = qTransient("RIpD");
    var lowest = undefined;
    var lowZ = 1e9;
    for (var f in evaluateQuery(context, qOwnedByBody(flat, EntityType.FACE)))
    {
        const tp = evFaceTangentPlane(context, { "face" : f, "parameter" : vector(0.5, 0.5) });
        if (abs(tp.normal[2]) > 0.999 && tp.origin[2] / millimeter < lowZ)
        {
            lowZ = tp.origin[2] / millimeter;
            lowest = f;
        }
    }
    for (var e in evaluateQuery(context, qAdjacent(lowest, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        const def = evCurveDefinition(context, { "edge" : e });
        var kind = (def is Line) ? "line" : ((def is Circle) ? "arc" : "spline");
        var s = "P " ~ kind ~ " |";
        for (var q in evEdgeTangentLines(context, { "edge" : e, "parameters" : range(0, 1, 41) }))
        {
            const p = q.origin / millimeter;
            s = s ~ " " ~ p[0] ~ " " ~ p[1];
        }
        out = append(out, s);
    }
    return out;
}
