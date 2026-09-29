FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");
// IMPORT: primitive_profiles.fs
export import(path : "PRIMITIVE_PROFILES_EID", version : "PRIMITIVE_PROFILES_MV");
// IMPORT: primitive_footprint.fs
export import(path : "PRIMITIVE_FOOTPRINT_EID", version : "PRIMITIVE_FOOTPRINT_MV");
// IMPORT: primitive_baseline.fs
export import(path : "PRIMITIVE_BASELINE_EID", version : "PRIMITIVE_BASELINE_MV");
// IMPORT: primitive_output.fs
export import(path : "PRIMITIVE_OUTPUT_EID", version : "PRIMITIVE_OUTPUT_MV");
// IMPORT: Variable_tools extract_outputs.fs (embedStandardOutputs) -- same pin as station_geometry
import(path : "a47f90bfa6b17a59e20cebd0/eb9b32c556ff036c3dd19f73/3cac74f0bc2b98272db13cd3", version : "cffacd73d80aa6dc1a2c4273");

/**
 * Export primitive (phase 1): the ski's "primitive drawing" geometry and data from its volume.
 *
 * Builds ONE closed composite "<prefix> PRIMITIVE" (excluded from the BOM) in the datum XZ plane, below the part,
 * with four bands stacked top to bottom, `Band gap` apart:
 *     1 BASELINE    the baseline (x, z), with points FCP / ACP / FRCP / ARCP / MCL / FB_MIN / AB_MIN
 *     2 PROFILE     the volume cut by the datum XZ plane: BOTTOM, TOP, TIP END, TAIL END wires, with points at
 *                   FCP / ACP / MRS / XS1 / XS2 / MP / TIP / TAIL on the bottom wire
 *     3 FOOTPRINT   the footprint unwrapped along s (u = x(MRS) + s towards the tip), y across drawn as height,
 *                   with points at the widest points, the waist and the inflections
 *     4 RADIUS      the sidecut radius along u, 10 mm per 1 m (integrateFootprint's input format), sidecut
 *                   positive, taper / tip / tail negative, breaking where |R| > "Radius plot limit"; a REFERENCE
 *                   (0) line and TICK marks at the key locations
 * Each band has a DATUM point at x = 0 on its reference line. All bodies are points and wires, so the composite
 * can be moved or copied as a unit.
 *
 * Tables: every row is stored in the composite's attribute (schema primitive/1, primitive_types.fs) and shown by
 * the "Primitive tables" custom table; the same map and the headline values are embedded for Extract variables.
 *
 * Frames: datum = the picked mate connector, or the world origin (X along the ski, Z up) when empty. s = arc length
 * along the profile's bottom wire, zero at MRS, positive towards FCP (the tip). w = across (y), h = along the
 * bottom wire's normal into the ski. XS1 / XS2 = halfway FCP..MRS / MRS..ACP in x. FCP may lie on either side.
 */
annotation { "Feature Type Name" : "Export primitive",
            "Feature Type Description" : "The ski's primitive drawing: baseline, profile, unwrapped footprint and radius plot in one closed composite, plus the primitive tables' data.",
            "Editing Logic Function" : "exportPrimitiveEditLogic" }
export const exportPrimitive = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Volume", "Filter" : EntityType.BODY && BodyType.SOLID, "MaxNumberOfPicks" : 1,
                    "Description" : "The ski / board volume (one solid part)." }
        definition.volume is Query;

        annotation { "Name" : "FCP", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Forebody contact point. The tip is on the FCP side: MRS -> FCP points to the tip." }
        definition.fcp is Query;

        annotation { "Name" : "ACP", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Aftbody contact point." }
        definition.acp is Query;

        annotation { "Name" : "MP (optional)", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR,
                    "Description" : "Mounting point(s), in order (several for a snowboard): MP, MP2, ..." }
        definition.mp is Query;

        annotation { "Name" : "Datum", "Filter" : BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                    "Description" : "Measuring and drawing frame: X along the ski, Z up, profiles in its XZ plane. Empty = the world origin with world X / Y / Z." }
        definition.datum is Query;

        annotation { "Name" : "Name prefix", "Default" : "", "MaxLength" : 128,
                    "Description" : "Starts every body name: <prefix> PRIMITIVE, <prefix> PRIMITIVE PROFILE TOP, ... Filled with the volume's name when it is picked." }
        definition.prefix is string;

        annotation { "Name" : "Baseline from", "Default" : PrimitiveSource.VOLUME, "UIHint" : [UIHint.SHOW_LABEL, UIHint.HORIZONTAL_ENUM],
                    "Description" : "Volume: the profile's bottom wire. Input wires: e.g. FULL_BASELINE." }
        definition.baselineFrom is PrimitiveSource;

        if (definition.baselineFrom == PrimitiveSource.INPUT)
        {
            annotation { "Name" : "Baseline wires", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "One connected chain in (or near) the datum XZ plane." }
            definition.baselineWires is Query;
        }

        annotation { "Name" : "Footprint from", "Default" : PrimitiveSource.VOLUME, "UIHint" : [UIHint.SHOW_LABEL, UIHint.HORIZONTAL_ENUM],
                    "Description" : "Volume: the part's plan outline, unwrapped along the bottom wire. Input wires: a flat footprint (constant z, taken as unwrapped, aligned at MRS) or a wrapped one." }
        definition.footprintFrom is PrimitiveSource;

        if (definition.footprintFrom == PrimitiveSource.INPUT)
        {
            annotation { "Name" : "Footprint wires", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "Both sides of the footprint (e.g. FPT_L and FPT_R)." }
            definition.footprintWires is Query;
        }

        annotation { "Name" : "Average radius between", "Default" : PrimitiveRadiusBetween.INFLECTION, "UIHint" : [UIHint.SHOW_LABEL],
                    "Description" : "Table 2 average radius = arc-length weighted mean of |R| (parts flatter than 100 m left out) over the unwrapped footprint between this station pair: FCP-ACP, the FB/AB widest points, or the FB/AB inflections (the ones nearest the widest points, on the MRS side). Natural radii and taper angles are named by their own pair." }
        definition.radiusBetween is PrimitiveRadiusBetween;

        annotation { "Name" : "Data points", "Description" : "Rows of the data table: evenly spaced in x from FCP to ACP, both included." }
        isInteger(definition.dataPoints, PRIMITIVE_DATA_POINTS_BOUNDS);

        annotation { "Name" : "Force XS1 / MRS / XS2", "Default" : true,
                    "Description" : "Add rows at XS1, MRS and XS2 when no data point falls there (within 0.01 mm)." }
        definition.forceStations is boolean;

        annotation { "Group Name" : "Layout", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Band gap", "Description" : "Clear space between the part and the first band, and between bands." }
            isLength(definition.bandGap, PRIMITIVE_BAND_GAP_BOUNDS);

            annotation { "Name" : "Radius plot limit", "Description" : "The radius plot breaks where |R| is larger (flat parts, next to an inflection). 50 m = 500 mm of plot." }
            isLength(definition.radiusLimit, PRIMITIVE_RADIUS_LIMIT_BOUNDS);

            annotation { "Name" : "Tick length", "Description" : "Half length of the radius plot's key-location tick marks." }
            isLength(definition.tickLength, PRIMITIVE_TICK_BOUNDS);
        }

        annotation { "Name" : "Query variable", "Default" : "primitive", "MaxLength" : 64,
                    "Description" : "Also publish the composite as this query variable (the app contract's required name). Empty = none." }
        definition.queryVariable is string;
    }
    {
        if (isQueryEmpty(context, definition.volume))
        {
            throw regenError("Select the volume (the ski part).", ["volume"]);
        }
        if (definition.prefix == "")
        {
            throw regenError("Enter a name prefix, e.g. the model name.", ["prefix"]);
        }
        const title = definition.prefix ~ " PRIMITIVE";
        const fromVolumeBaseline = definition.baselineFrom == PrimitiveSource.VOLUME;
        const fromVolumeFootprint = definition.footprintFrom == PrimitiveSource.VOLUME;
        var notes = [];

        // ---- Frames and points (LOCAL frame: the datum on the world origin) ----
        const datum = primitiveDatum(context, definition.datum);
        const isIdentity = isQueryEmpty(context, definition.datum);
        const toLocal = fromWorld(datum);
        const fcp = toLocal * primitivePoint(context, definition.fcp, "FCP", "fcp");
        const acp = toLocal * primitivePoint(context, definition.acp, "ACP", "acp");
        var mps = [];
        for (var p in primitivePoints(context, definition.mp))
        {
            mps = append(mps, toLocal * p);
        }
        if (abs(fcp[0] - acp[0]) < 1 * millimeter)
        {
            throw regenError("FCP and ACP must be apart along the datum X.", ["fcp", "acp"]);
        }
        const dirSign = fcp[0] > acp[0] ? 1 : -1;
        const mrs = (fcp + acp) / 2;
        const volumeBox = evBox3d(context, { "topology" : definition.volume, "cSys" : datum, "tight" : true });

        // ---- Profile and the bottom-wire frame ----
        const profile = primitiveProfile(context, id + "profile", definition.volume, datum, toLocal, isIdentity, dirSign, mrs[0]);
        if (profile.loops > 1)
        {
            notes = append(notes, "the mid plane cuts the volume in " ~ profile.loops ~ " loops; the longest is used");
        }
        const frame = primitiveFrame(context, profile.bottomChain, mrs, dirSign);

        // ---- Key locations (Table 3) ----
        const xXs1 = (fcp[0] + mrs[0]) / 2;
        const xXs2 = (mrs[0] + acp[0]) / 2;
        const xsBottom = primitiveChainAtX(context, frame.chain, frame.lookup, [xXs1, xXs2]);
        var keyPoints = [
            { "key" : "FCP", "point" : fcp },
            { "key" : "ACP", "point" : acp },
            { "key" : "MRS", "point" : mrs }
        ];
        for (var i = 0; i < size(mps); i += 1)
        {
            keyPoints = append(keyPoints, { "key" : i == 0 ? "MP" : "MP" ~ (i + 1), "point" : mps[i] });
        }
        keyPoints = concatenateArrays([keyPoints, [
                        { "key" : "XS1", "point" : xsBottom[0].point },
                        { "key" : "XS2", "point" : xsBottom[1].point },
                        { "key" : "TIP", "point" : profile.tip },
                        { "key" : "TAIL", "point" : profile.tail }
                    ]]);
        var keys = {};
        var keyRows = [];
        for (var kp in keyPoints)
        {
            const loc = primitiveLocate(context, frame, kp.point);
            keys[kp.key] = loc;
            keyRows = append(keyRows, { "key" : kp.key, "name" : kp.key,
                        "x" : primitiveMM(loc.x), "y" : primitiveMM(loc.y), "z" : primitiveMM(loc.z),
                        "s" : primitiveMM(loc.s), "w" : primitiveMM(loc.w), "h" : primitiveMM(loc.h) });
        }

        // ---- Table 1: theoretical scale factors ----
        const scale = primitiveScaleFactors(context, frame, profile.topChain, keys.FCP.a, keys.ACP.a);

        // ---- Table 5: baseline ----
        const baseline = primitiveBaseline(context, id + "baseline", fromVolumeBaseline, profile.bottom,
            fromVolumeBaseline ? qNothing() : wireEdges(definition.baselineWires), toLocal, isIdentity, fcp, acp, profile.tail);
        if (baseline.result == undefined)
        {
            notes = append(notes, "the baseline could not be analysed");
        }
        const baselineRows = primitiveBaselineRows(context, frame, baseline.result);

        // ---- Footprint: unwrap, analyse (Table 2), radius ----
        const source = primitiveFootprintSource(context, id + "footprintSource", fromVolumeFootprint, definition.volume,
            fromVolumeFootprint ? qNothing() : wireEdges(definition.footprintWires), datum, toLocal, isIdentity);
        const unwrapped = primitiveUnwrap(context, id + "unwrap", frame, source.edges, !fromVolumeFootprint, definition.radiusLimit);
        opDeleteBodies(context, id + "deleteFootprintSource", { "entities" : source.bodies });
        const uFcp = primitiveU(frame, keys.FCP.a);
        const uAcp = primitiveU(frame, keys.ACP.a);
        const fpt = primitiveFootprintAnalysis(context, unwrapped.bodies, uFcp, uAcp, frame.xMrs, definition.radiusBetween);
        const fr = fpt.result;
        const fptPoints = {
                "FB_WIDEST" : fr.fbWidestData.point,
                "AB_WIDEST" : fr.abWidestData.point,
                "WAIST" : fr.waist.point,
                "FB_INFLECTION" : fr.fbInflectionData.point,
                "AB_INFLECTION" : fr.abInflectionData.point
            };
        if (!fr.hasInflections)
        {
            notes = append(notes, "no sidecut inflection found on one side; the widest point stands in for it");
        }

        // ---- Data table (within RSL) ----
        const dataX = dataStations(definition, fcp[0], acp[0], [xXs1, mrs[0], xXs2]);
        const dataBottom = primitiveChainAtX(context, frame.chain, frame.lookup, dataX);
        const dataHeights = primitiveBaselineHeights(context, baseline, dataX, fcp[0], acp[0]);
        var dataRows = [];
        for (var j = 0; j < size(dataX); j += 1)
        {
            const b = dataBottom[j];
            const s = primitiveS(frame, b.a);
            const up = primitiveUp(frame, b.tangent);
            const topHit = primitiveChainCrossing(context, profile.topChain, b.point, up);
            const across = primitiveFootprintAt(unwrapped.samples, primitiveU(frame, b.a));
            dataRows = append(dataRows, {
                        "x" : primitiveMM(dataX[j]),
                        "s" : primitiveMM(s),
                        "y" : across.hit ? primitiveMM(across.yMax) : PRIMITIVE_NOT_FOUND,
                        "skiWidth" : across.hit ? primitiveMM(across.yMax - across.yMin) : PRIMITIVE_NOT_FOUND,
                        "z" : primitiveMM(b.point[2]),
                        "skiThck" : topHit == undefined ? PRIMITIVE_NOT_FOUND : primitiveMM(dot(topHit.point - b.point, up)),
                        "baselineHeight" : primitiveMM(dataHeights[j]),
                        "radius" : across.radius == undefined ? "" : primitiveRound(across.radius, 4)
                    });
        }

        // ---- Table 2: metadata ----
        const metaRows = metadataRows(fpt, fcp, acp, volumeBox, definition.radiusBetween, frame);

        // ---- Bands: extents, stacking ----
        const runs = primitiveRadiusRuns(unwrapped.samples);
        const radiusExtent = primitiveRadiusExtent(runs);
        const tick = definition.tickLength;
        const profileBodies = qUnion([profile.bottom, profile.top, profile.tipEnd, profile.tailEnd]);
        const fpBox = evBox3d(context, { "topology" : unwrapped.bodies, "tight" : true });
        const extents = [
            primitiveZExtent(context, baseline.body),
            primitiveZExtent(context, profileBodies),
            { "lo" : fpBox.minCorner[1], "hi" : fpBox.maxCorner[1] },
            { "lo" : min(radiusExtent.lo, -tick), "hi" : max(radiusExtent.hi, tick) }
        ];
        const offsets = primitiveStack(extents, volumeBox.minCorner[2], definition.bandGap);
        const zBaseline = offsets[0];
        const zProfile = offsets[1];
        const zFootprint = offsets[2];
        const zRadius = offsets[3];
        const zero = 0 * meter;
        var members = [];
        var queries = {};

        // Band 1: baseline
        opTransform(context, id + "moveBaseline", { "bodies" : baseline.body, "transform" : transform(vector(zero, zero, zBaseline)) });
        primitiveName(context, baseline.body, title ~ " BASELINE");
        members = append(members, baseline.body);
        queries.baseline = baseline.body;
        if (baseline.result != undefined)
        {
            const r = baseline.result;
            const blPoints = { "FCP" : r.fcp_pt, "ACP" : r.acp_pt, "FRCP" : r.frcp_pt, "ARCP" : r.arcp_pt, "MCL" : r.mcl_pt,
                    "FB_MIN" : r.fb_min_pt, "AB_MIN" : r.ab_min_pt };
            for (var entry in blPoints)
            {
                if (entry.value is Vector)
                {
                    members = append(members, primitivePointBody(context, id + ("baselinePt" ~ entry.key),
                                entry.value + vector(zero, zero, zBaseline), title ~ " BASELINE " ~ entry.key));
                }
            }
        }

        // Band 2: profile
        const profileMove = transform(vector(zero, zero, zProfile));
        opTransform(context, id + "moveProfile", { "bodies" : profileBodies, "transform" : profileMove });
        primitiveName(context, profile.bottom, title ~ " PROFILE BOTTOM");
        primitiveName(context, profile.top, title ~ " PROFILE TOP");
        if (!isQueryEmpty(context, profile.tipEnd))
        {
            primitiveName(context, profile.tipEnd, title ~ " PROFILE TIP END");
        }
        if (!isQueryEmpty(context, profile.tailEnd))
        {
            primitiveName(context, profile.tailEnd, title ~ " PROFILE TAIL END");
        }
        members = append(members, profileBodies);
        queries.profileBottom = profile.bottom;
        queries.profileTop = profile.top;
        for (var kp in keyPoints)
        {
            members = append(members, primitivePointBody(context, id + ("profilePt" ~ primitiveKey(kp.key)),
                        profileMove * keys[kp.key].foot, title ~ " PROFILE " ~ kp.key));
        }
        if (scale.topFcp != undefined)
        {
            members = append(members, primitivePointBody(context, id + "profilePtTopFCP", profileMove * scale.topFcp, title ~ " PROFILE TOP FCP"));
        }
        if (scale.topAcp != undefined)
        {
            members = append(members, primitivePointBody(context, id + "profilePtTopACP", profileMove * scale.topAcp, title ~ " PROFILE TOP ACP"));
        }

        // Band 3: footprint (plan y becomes height)
        const flatToBand = transform(vector(zero, zero, zFootprint)) * rotationAround(line(vector(zero, zero, zero), vector(1, 0, 0)), 90 * degree);
        opTransform(context, id + "moveFootprint", { "bodies" : unwrapped.bodies, "transform" : flatToBand });
        primitiveName(context, unwrapped.bodies, title ~ " FOOTPRINT");
        members = append(members, unwrapped.bodies);
        queries.footprint = unwrapped.bodies;
        for (var entry in fptPoints)
        {
            members = append(members, primitivePointBody(context, id + ("footprintPt" ~ entry.key),
                        flatToBand * entry.value, title ~ " FOOTPRINT " ~ entry.key));
        }

        // Band 4: radius plot, reference line, ticks
        const plot = qUnion(primitiveRadiusPlot(context, id + "radiusPlot", runs, zRadius));
        if (!isQueryEmpty(context, plot))
        {
            primitiveName(context, plot, title ~ " RADIUS");
        }
        members = append(members, plot);
        queries.radius = plot;
        const reference = primitiveSegment(context, id + "radiusReference", vector(fpBox.minCorner[0], zero, zRadius),
            vector(fpBox.maxCorner[0], zero, zRadius), title ~ " RADIUS REFERENCE");
        members = append(members, reference);
        var tickU = {};
        for (var kp in keyPoints)
        {
            if (kp.key != "TIP" && kp.key != "TAIL")
            {
                tickU[kp.key] = primitiveU(frame, keys[kp.key].a);
            }
        }
        for (var name in ["FB_WIDEST", "AB_WIDEST", "FB_INFLECTION", "AB_INFLECTION"])
        {
            tickU[name] = fptPoints[name][0];
        }
        for (var entry in tickU)
        {
            members = append(members, primitiveSegment(context, id + ("tick" ~ primitiveKey(entry.key)),
                        vector(entry.value, zero, zRadius - tick), vector(entry.value, zero, zRadius + tick), title ~ " RADIUS TICK " ~ entry.key));
        }

        // Band datum points (x = 0 on each band's reference line), for ordinate dimensions.
        const bandZ = [zBaseline, zProfile, zFootprint, zRadius];
        for (var i = 0; i < size(PRIMITIVE_BANDS); i += 1)
        {
            const label = PRIMITIVE_BAND_LABELS[PRIMITIVE_BANDS[i]];
            members = append(members, primitivePointBody(context, id + ("datum" ~ label), vector(zero, zero, bandZ[i]), title ~ " " ~ label ~ " DATUM"));
        }

        // ---- Into the datum frame, one closed composite ----
        const allMembers = qUnion(members);
        primitiveMove(context, id + "toDatum", allMembers, toWorld(datum), isIdentity);

        const data = {
                "schema" : PRIMITIVE_SCHEMA,
                "title" : title,
                "prefix" : definition.prefix,
                "units" : { "length" : "mm", "radius" : "m", "angle" : "deg" },
                "settings" : {
                    "baselineFrom" : fromVolumeBaseline ? "VOLUME" : "INPUT",
                    "footprintFrom" : fromVolumeFootprint ? "VOLUME" : "INPUT",
                    "radiusBetween" : betweenLabel(definition.radiusBetween),
                    "dataPoints" : definition.dataPoints,
                    "forceStations" : definition.forceStations,
                    "tipTowards" : dirSign > 0 ? "+X" : "-X"
                },
                "bands" : { "baseline" : primitiveMM(zBaseline), "profile" : primitiveMM(zProfile),
                    "footprint" : primitiveMM(zFootprint), "radius" : primitiveMM(zRadius), "radiusScale" : "10 mm per 1 m" },
                "scaleFactors" : scale.rows,
                "metadata" : metaRows,
                "keyLocations" : keyRows,
                "baseline" : baselineRows,
                "data" : dataRows,
                "footprint" : footprintSummary(frame, fptPoints, fpt)
            };
        const composite = primitiveComposite(context, id + "composite", allMembers, title, data);
        queries.primitive = composite;

        if (definition.queryVariable != "")
        {
            setQueryVariable(context, definition.queryVariable, "The " ~ title ~ " composite (Export primitive).", composite);
        }

        embedStandardOutputs(context, id, {
                    "output" : composite,
                    "outputDescription" : "The " ~ title ~ " composite",
                    "inputs" : definition.volume,
                    "variables" : {
                        "primitive" : extractableVariable(data, "All primitive tables (schema " ~ PRIMITIVE_SCHEMA ~ "): scaleFactors, metadata, keyLocations, baseline, data rows; mm / m / deg."),
                        "rsl" : extractableVariable(abs(acp[0] - fcp[0]), "RSL: FCP to ACP along the datum X."),
                        "averageRadius" : extractableVariable(fpt.average.valid ? fpt.average.avgRadius : 0 * meter, "Average sidecut radius (" ~ betweenLabel(definition.radiusBetween) ~ "); 0 = none."),
                        "naturalRadiusWidest" : extractableVariable(fr.naturalRadiusWidest.valid ? fr.naturalRadiusWidest.R : 0 * meter, "Natural radius through the widest points; 0 = none."),
                        "naturalRadiusInflection" : extractableVariable(fr.naturalRadiusInflection.valid ? fr.naturalRadiusInflection.R : 0 * meter, "Natural radius through the inflection points; 0 = none."),
                        "taperAngleWidest" : extractableVariable(fr.foundTaperAngle, "Taper angle between the widest points (+ = forebody wider)."),
                        "taperAngleInflection" : extractableVariable(fr.taperAngleInflection, "Taper angle between the inflection points.")
                    },
                    "queries" : queries
                });

        const avgText = fpt.average.valid ? primitiveRound(fpt.average.avgRadius / meter, 3) ~ " m" : "n/a";
        var summary = title ~ ": RSL " ~ primitiveRound(abs(acp[0] - fcp[0]) / millimeter, 2) ~ " mm, average radius " ~ avgText ~
            " (" ~ betweenLabel(definition.radiusBetween) ~ "), " ~ size(dataRows) ~ " data rows; footprint " ~
            unwrapped.exact ~ " exact group(s), " ~ unwrapped.fitted ~ " fitted edge(s).";
        if (size(notes) > 0)
        {
            summary = summary ~ " Note: " ~ join(notes, "; ") ~ ".";
        }
        reportFeatureInfo(context, id, summary);
    }, {});

/**
 * Fills the name prefix with the volume's name when it is picked, and follows a volume change as long as the prefix
 * still is the previous volume's name (getProperty throws in the feature body, correction 36).
 */
export function exportPrimitiveEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map) returns map
{
    if (isQueryEmpty(context, definition.volume))
    {
        return definition;
    }
    const partName = getProperty(context, { "entity" : definition.volume, "propertyType" : PropertyType.NAME });
    if (definition.prefix == "")
    {
        definition.prefix = partName;
    }
    else if (oldDefinition.volume != undefined && !isQueryEmpty(context, oldDefinition.volume)
        && definition.prefix == getProperty(context, { "entity" : oldDefinition.volume, "propertyType" : PropertyType.NAME }))
    {
        definition.prefix = partName;
    }
    return definition;
}

/** Edges of a pick that may hold edges or wire bodies. */
function wireEdges(pick is Query) returns Query
{
    return qUnion([qEntityFilter(pick, EntityType.EDGE), qOwnedByBody(qBodyType(qEntityFilter(pick, EntityType.BODY), BodyType.WIRE), EntityType.EDGE)]);
}

/** Data-table x positions: N evenly spaced FCP..ACP (both included), plus XS1 / MRS / XS2 when forced; FCP first. */
function dataStations(definition is map, xFcp is ValueWithUnits, xAcp is ValueWithUnits, forced is array) returns array
{
    const n = definition.dataPoints;
    var xs = [];
    for (var j = 0; j < n; j += 1)
    {
        xs = append(xs, xFcp + (xAcp - xFcp) * j / (n - 1));
    }
    if (definition.forceStations)
    {
        for (var x in forced)
        {
            var present = false;
            for (var have in xs)
            {
                if (abs(have - x) < 0.01 * millimeter)
                {
                    present = true;
                    break;
                }
            }
            if (!present)
            {
                xs = append(xs, x);
            }
        }
    }
    return sort(xs, function(a, b) { return (abs(a - xFcp) - abs(b - xFcp)) / meter; });
}

function betweenLabel(between is PrimitiveRadiusBetween) returns string
{
    if (between == PrimitiveRadiusBetween.CONTACTS)
    {
        return "between FCP and ACP";
    }
    if (between == PrimitiveRadiusBetween.WIDEST)
    {
        return "between the widest points";
    }
    return "between the inflection points";
}

/** Table 2 rows: { key, name, value (number or text), unit, note }. */
function metadataRows(fpt is map, fcp is Vector, acp is Vector, box is Box3d, between is PrimitiveRadiusBetween, frame is map) returns array
{
    const r = fpt.result;
    const dims = box.maxCorner - box.minCorner;
    return [
        { "key" : "rsl", "name" : "RSL", "value" : primitiveMM(abs(acp[0] - fcp[0])), "unit" : "mm", "note" : "FCP to ACP along the datum X" },
        { "key" : "dimensions", "name" : "Dimensions (L x W x H)",
            "value" : primitiveRound(dims[0] / millimeter, 2) ~ " x " ~ primitiveRound(dims[1] / millimeter, 2) ~ " x " ~ primitiveRound(dims[2] / millimeter, 2),
            "unit" : "mm", "note" : "Volume bounding box in the datum frame" },
        { "key" : "sidecutWidths", "name" : "Widths (FB widest - waist - AB widest)",
            "value" : primitiveRound(r.fbWidestData.width * 2 / millimeter, 2) ~ " - " ~ primitiveRound(r.waist.width * 2 / millimeter, 2) ~ " - " ~ primitiveRound(r.abWidestData.width * 2 / millimeter, 2),
            "unit" : "mm", "note" : "Unwrapped footprint, full widths" },
        { "key" : "averageRadius", "name" : "Average radius", "value" : fpt.average.valid ? primitiveRound(fpt.average.avgRadius / meter, 4) : PRIMITIVE_NOT_FOUND,
            "unit" : "m", "note" : "Arc-length weighted mean |R| " ~ betweenLabel(between) ~ " (flatter than 100 m left out)" },
        { "key" : "naturalRadiusWidest", "name" : "Natural radius widest", "value" : r.naturalRadiusWidest.valid ? primitiveRound(r.naturalRadiusWidest.R / meter, 4) : PRIMITIVE_NOT_FOUND,
            "unit" : "m", "note" : "Arc through the widest points, tangent to the waist line" },
        { "key" : "naturalRadiusInflection", "name" : "Natural radius inflection", "value" : r.naturalRadiusInflection.valid ? primitiveRound(r.naturalRadiusInflection.R / meter, 4) : PRIMITIVE_NOT_FOUND,
            "unit" : "m", "note" : "Arc through the inflection points, tangent to the waist line" },
        { "key" : "taperAngleWidest", "name" : "Taper angle widest", "value" : primitiveRound(r.foundTaperAngle / degree, 4),
            "unit" : "deg", "note" : "Centreline to the line through the widest points (+ = forebody wider)" },
        { "key" : "taperAngleInflection", "name" : "Taper angle inflection", "value" : primitiveRound(r.taperAngleInflection / degree, 4),
            "unit" : "deg", "note" : "Centreline to the line through the inflection points" },
        { "key" : "deflection", "name" : "Theoretical deflection", "value" : PRIMITIVE_PHASE_2, "unit" : "mm/30kg", "note" : "3-point bending, rollers at FCP / ACP, 30 kg at MRS" },
        { "key" : "stiffness", "name" : "Theoretical stiffness", "value" : PRIMITIVE_PHASE_2, "unit" : "lb/in", "note" : "Same load case" }
    ];
}

/** Footprint points as { u, s, w } in mm (u = unwrapped length coordinate, s from MRS towards FCP). */
function footprintSummary(frame is map, points is map, fpt is map) returns map
{
    var out = {};
    for (var entry in points)
    {
        const u = entry.value[0];
        out[entry.key] = { "u" : primitiveMM(u), "s" : primitiveMM((u - frame.xMrs) * frame.dirSign), "w" : primitiveMM(entry.value[1]) };
    }
    out.averageFrom = primitiveMM((fpt.lo - frame.xMrs) * frame.dirSign);
    out.averageTo = primitiveMM((fpt.hi - frame.xMrs) * frame.dirSign);
    return out;
}
