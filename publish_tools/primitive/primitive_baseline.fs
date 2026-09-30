FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "0c44c88b4829c8f3e72eb25d");
// IMPORT: xSection V58 analyzeBaseline.fs (analyzeBaselineGeometry; fcp_pt / acp_pt exact at the FCP / ACP x)
import(path : "f8deedeb1fbd819a8fa20113/ad6a3958dd2e8873f6dd0b0d/f0717a1116fee7304957da5b", version : "d8b52bb8029bcc972f9af4f0");

/**
 * Export Primitive -- the baseline (Table 5), measured by xSection's analyzeBaselineGeometry (V58: FCP / ACP points
 * exactly at their x, correction 59) on a LOCAL copy of the baseline (it works in world X / Z, which the local frame
 * makes the datum's X / Z):
 *     FCPh / ACPh     height of FCP / ACP above the rocker tangent at FRCP / ARCP
 *     FRCP / ARCP     on each half, the baseline inflection closest to that half's lowest point, towards MRS
 *     FRCPl / ARCPl   |dx| FCP -> FRCP, ACP -> ARCP
 *     FB_Roll/AB_Roll |dx| lowest point -> FRCP / ARCP
 *     MCh             camber height (max distance from the line through the two lowest points)
 *     MCl             position of the max camber point: x, and s
 * baseline_from VOLUME uses the profile's bottom wire.
 * A baseline that is flat within the RSL (no point more than PRIMITIVE_FLAT_BASELINE off the FCP - ACP chord) has
 * no rocker contacts, minima or camber: only its FCP / ACP points are drawn and the FCPh .. ACPh rows are left out.
 */

/** Evenly spaced x samples over the RSL for the flatness test. */
const FLAT_SAMPLES = 201;

/**
 * Copies the baseline into the LOCAL frame and analyses it. Returns { body (the local copy, a wire), result
 * (analyzeBaselineGeometry's map, undefined when it found nothing), chain, lookup, flat (true when the baseline is
 * flat within the RSL), deviation (its largest |height| above the FCP - ACP chord there) }. From the volume the copy is
 * the bottom wire itself, so the bottom frame's chain and x lookup (`bottomFrame`, primitiveFrame) are reused instead
 * of rebuilt: same curves, same TAIL -> TIP order, until the profile band is moved.
 */
export function primitiveBaseline(context is Context, id is Id, fromVolume is boolean, bottom is Query, inputEdges is Query,
    toLocal is Transform, isIdentity is boolean, fcp is Vector, acp is Vector, tail is Vector, bottomFrame is map) returns map
{
    if (fromVolume)
    {
        opExtractWires(context, id + "copy", { "edges" : qOwnedByBody(bottom, EntityType.EDGE) });
    }
    else
    {
        if (isQueryEmpty(context, inputEdges))
        {
            throw regenError("Select the baseline wire(s).", ["baselineWires"]);
        }
        opExtractWires(context, id + "copy", { "edges" : inputEdges });
        primitiveMove(context, id + "copyToLocal", qCreatedBy(id + "copy", EntityType.BODY), toLocal, isIdentity);
    }
    const body = qCreatedBy(id + "copy", EntityType.BODY);
    const edges = qOwnedByBody(body, EntityType.EDGE);

    opPoint(context, id + "fcp", { "point" : fcp });
    opPoint(context, id + "acp", { "point" : acp });
    const result = analyzeBaselineGeometry(context, edges,
        qOwnedByBody(qCreatedBy(id + "fcp", EntityType.BODY), EntityType.VERTEX),
        qOwnedByBody(qCreatedBy(id + "acp", EntityType.BODY), EntityType.VERTEX), false);
    opDeleteBodies(context, id + "deletePoints", { "entities" : qUnion([qCreatedBy(id + "fcp", EntityType.BODY), qCreatedBy(id + "acp", EntityType.BODY)]) });

    const chain = fromVolume ? bottomFrame.chain : primitiveChain(context, edges, tail, "Baseline");
    const lookup = fromVolume ? bottomFrame.lookup : primitiveChainTable(context, chain);
    var out = { "body" : body, "result" : result, "chain" : chain, "lookup" : lookup };
    var xs = [];
    for (var i = 0; i < FLAT_SAMPLES; i += 1)
    {
        xs = append(xs, fcp[0] + (acp[0] - fcp[0]) * i / (FLAT_SAMPLES - 1));
    }
    var deviation = 0 * meter;
    for (var h in primitiveBaselineHeights(context, out, xs, fcp[0], acp[0]))
    {
        deviation = max(deviation, abs(h));
    }
    out.deviation = deviation;
    out.flat = deviation < PRIMITIVE_FLAT_BASELINE;
    return out;
}

/**
 * baseline_height at each x: the baseline's height above the straight line through its points at FCP and ACP
 * (camber positive), in the local frame.
 */
export function primitiveBaselineHeights(context is Context, baseline is map, xs is array, xFcp is ValueWithUnits, xAcp is ValueWithUnits) returns array
{
    const ends = primitiveChainAtX(context, baseline.chain, baseline.lookup, [xFcp, xAcp]);
    const at = primitiveChainAtX(context, baseline.chain, baseline.lookup, xs);
    var out = [];
    for (var j = 0; j < size(xs); j += 1)
    {
        const f = (xs[j] - xFcp) / (xAcp - xFcp);
        const chordZ = ends[0].point[2] + (ends[1].point[2] - ends[0].point[2]) * f;
        out = append(out, at[j].point[2] - chordZ);
    }
    return out;
}

/**
 * Table 5 rows: { key, name, value (mm or text), x, s (mm or ""), note }, name and note from PRIMITIVE_TERMS in
 * `language` ("en" / "de"). Only rows with data: Tip / Tail block when named; the rocker / camber rows when the
 * baseline was analysed and is not flat; a value that could not be found on this geometry is left out.
 */
export function primitiveBaselineRows(context is Context, frame is map, baseline is map, tipBlock is string, tailBlock is string,
    language is string) returns array
{
    var rows = [];
    if (tipBlock != "")
    {
        rows = append(rows, textRow("tipBlock", tipBlock, language));
    }
    if (tailBlock != "")
    {
        rows = append(rows, textRow("tailBlock", tailBlock, language));
    }
    const result = baseline.result;
    if (result == undefined || baseline.flat)
    {
        return rows;
    }
    const candidates = [
        valueRow("FCPh", result.fcph, language),
        pointRow(context, frame, "FRCP", result.frcp_pt, undefined, language),
        valueRow("FRCPl", result.frcpl, language),
        valueRow("FB_Roll", result.fb_roll, language),
        valueRow("MCh", result.camber_height, language),
        pointRow(context, frame, "MCl", result.mcl_pt, undefined, language),
        valueRow("AB_Roll", result.ab_roll, language),
        valueRow("ARCPl", result.arcpl, language),
        pointRow(context, frame, "ARCP", result.arcp_pt, undefined, language),
        valueRow("ACPh", result.acph, language)
    ];
    for (var row in candidates)
    {
        if (row != undefined)
        {
            rows = append(rows, row);
        }
    }
    return rows;
}

function textRow(key is string, text is string, language is string) returns map
{
    return primitiveRow("baseline", key, language, { "value" : text, "x" : "", "s" : "" });
}

/** A length row; undefined when the value was not found. */
function valueRow(key is string, value, language is string)
{
    if (!(value is ValueWithUnits))
    {
        return undefined;
    }
    return primitiveRow("baseline", key, language, { "value" : primitiveMM(value), "x" : "", "s" : "" });
}

/** A baseline point: x, and s along the bottom wire at that x (primitiveS; `s` overrides when given); undefined when not found. */
function pointRow(context is Context, frame is map, key is string, point, s, language is string)
{
    if (!(point is Vector))
    {
        return undefined;
    }
    var sValue = s;
    if (!(sValue is ValueWithUnits))
    {
        const at = primitiveChainAtX(context, frame.chain, frame.lookup, [point[0]])[0];
        sValue = primitiveS(frame, at.a);
    }
    return primitiveRow("baseline", key, language, { "value" : "", "x" : primitiveMM(point[0]), "s" : primitiveMM(sValue) });
}
