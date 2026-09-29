FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: primitive_frame.fs
export import(path : "5808546b3b3d863d82796d24", version : "11f0f0307ee2dcceaf62c9c1");
// IMPORT: xSection V57 analyzeBaseline.fs (analyzeBaselineGeometry)
import(path : "f8deedeb1fbd819a8fa20113/1113d16a32de3db613416436/f0717a1116fee7304957da5b", version : "8e18336b63be0ae8baa68042");

/**
 * Export Primitive -- the baseline (Table 5), measured by xSection's analyzeBaselineGeometry on a LOCAL copy of
 * the baseline (it works in world X / Z, which the local frame makes the datum's X / Z):
 *     FCPh / ACPh     height of FCP / ACP above the rocker tangent at FRCP / ARCP
 *     FRCP / ARCP     on each half, the baseline inflection closest to that half's lowest point, towards MRS
 *     FRCPl / ARCPl   |dx| FCP -> FRCP, ACP -> ARCP
 *     FB_Roll/AB_Roll |dx| lowest point -> FRCP / ARCP
 *     MCh             camber height (max distance from the line through the two lowest points)
 *     MCl             position of the max camber point: x, and s
 * baseline_from VOLUME uses the profile's bottom wire.
 */

/**
 * Copies the baseline into the LOCAL frame and analyses it. Returns { body (the local copy, a wire), result
 * (analyzeBaselineGeometry's map, undefined when it found nothing), chain, lookup }.
 */
export function primitiveBaseline(context is Context, id is Id, fromVolume is boolean, bottom is Query, inputEdges is Query,
    toLocal is Transform, isIdentity is boolean, fcp is Vector, acp is Vector, tail is Vector) returns map
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

    const chain = primitiveChain(context, edges, tail, "Baseline");
    const lookup = primitiveChainTable(context, chain);
    return { "body" : body, "result" : exactContacts(context, result, chain, lookup, fcp, acp), "chain" : chain, "lookup" : lookup };
}

/**
 * analyzeBaselineGeometry (xSection V57) takes fcp_pt / acp_pt from its 200 midpoint samples unless FCP / ACP is a
 * chain END, so on a baseline that runs past the contacts (FULL_BASELINE with tip / tail) FRCPl, ARCPl, FCPh and
 * ACPh were off by up to half a sample spacing (RD 20TAC: FRCPl 131.25 for 130, ARCPl 49.26 for 50). Re-measured
 * here from the baseline point exactly at the FCP / ACP x, with analyzeBaseline's own formulas (|dx|, and the
 * distance to the rocker tangent line).
 */
function exactContacts(context is Context, result, chain is map, lookup is map, fcp is Vector, acp is Vector)
{
    if (result == undefined)
    {
        return undefined;
    }
    const at = primitiveChainAtX(context, chain, lookup, [fcp[0], acp[0]]);
    var r = result;
    r.fcp_pt = at[0].point;
    r.acp_pt = at[1].point;
    if (r.frcp_pt is Vector && r.frcp_dir is Vector)
    {
        r.frcpl = abs(r.frcp_pt[0] - r.fcp_pt[0]);
        r.fcph = distanceToLine(r.fcp_pt, r.frcp_pt, r.frcp_dir);
    }
    if (r.arcp_pt is Vector && r.arcp_dir is Vector)
    {
        r.arcpl = abs(r.acp_pt[0] - r.arcp_pt[0]);
        r.acph = distanceToLine(r.acp_pt, r.arcp_pt, r.arcp_dir);
    }
    return r;
}

function distanceToLine(point is Vector, origin is Vector, direction is Vector) returns ValueWithUnits
{
    const d = point - origin;
    return norm(d - dot(d, direction) * direction);
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
 * Table 5 rows: { key, name, value (mm or text), x, s (mm or ""), note }. Tip / Tail block and Tip / Tail height
 * are present but not computed (phase 2).
 */
export function primitiveBaselineRows(context is Context, frame is map, result) returns array
{
    const missing = PRIMITIVE_NOT_FOUND;
    var rows = [
        textRow("tipBlock", "Tip block", PRIMITIVE_PHASE_2),
        textRow("tailBlock", "Tail block", PRIMITIVE_PHASE_2),
        textRow("tipHeight", "Tip_height", PRIMITIVE_PHASE_2)
    ];
    if (result == undefined)
    {
        for (var name in ["FCPh", "FRCP", "FRCPl", "FB_Roll", "MCh", "MCl", "AB_Roll", "ARCPl", "ARCP", "ACPh"])
        {
            rows = append(rows, textRow(name, name, missing));
        }
        rows = append(rows, textRow("tailHeight", "Tail_height", PRIMITIVE_PHASE_2));
        return rows;
    }
    rows = append(rows, valueRow("FCPh", "FCPh", result.fcph, "FCP height above the FRCP rocker tangent"));
    rows = append(rows, pointRow(context, frame, "FRCP", "FRCP", result.frcp_pt, undefined, "Forebody rocker contact: inflection nearest the forebody minimum, towards MRS"));
    rows = append(rows, valueRow("FRCPl", "FRCPl", result.frcpl, "|dx| FCP -> FRCP"));
    rows = append(rows, valueRow("FB_Roll", "FB_Roll", result.fb_roll, "|dx| forebody minimum -> FRCP"));
    rows = append(rows, valueRow("MCh", "MCh", result.camber_height, "Camber height"));
    rows = append(rows, pointRow(context, frame, "MCl", "MCl", result.mcl_pt, result.mcl_s, "Max camber position"));
    rows = append(rows, valueRow("AB_Roll", "AB_Roll", result.ab_roll, "|dx| aftbody minimum -> ARCP"));
    rows = append(rows, valueRow("ARCPl", "ARCPl", result.arcpl, "|dx| ACP -> ARCP"));
    rows = append(rows, pointRow(context, frame, "ARCP", "ARCP", result.arcp_pt, undefined, "Aftbody rocker contact: inflection nearest the aftbody minimum, towards MRS"));
    rows = append(rows, valueRow("ACPh", "ACPh", result.acph, "ACP height above the ARCP rocker tangent"));
    rows = append(rows, textRow("tailHeight", "Tail_height", PRIMITIVE_PHASE_2));
    return rows;
}

function textRow(key is string, name is string, text is string) returns map
{
    return { "key" : key, "name" : name, "value" : text, "x" : "", "s" : "", "note" : "" };
}

function valueRow(key is string, name is string, value, note is string) returns map
{
    if (!(value is ValueWithUnits))
    {
        return textRow(key, name, PRIMITIVE_NOT_FOUND);
    }
    return { "key" : key, "name" : name, "value" : primitiveMM(value), "x" : "", "s" : "", "note" : note };
}

/** A baseline point: x, and s along the bottom wire at that x (MCl: analyzeBaseline's own mcl_s when given). */
function pointRow(context is Context, frame is map, key is string, name is string, point, s, note is string) returns map
{
    if (!(point is Vector))
    {
        return textRow(key, name, PRIMITIVE_NOT_FOUND);
    }
    var sValue = s;
    if (!(sValue is ValueWithUnits))
    {
        const at = primitiveChainAtX(context, frame.chain, frame.lookup, [point[0]])[0];
        sValue = primitiveS(frame, at.a);
    }
    return { "key" : key, "name" : name, "value" : "", "x" : primitiveMM(point[0]), "s" : primitiveMM(sValue), "note" : note };
}
