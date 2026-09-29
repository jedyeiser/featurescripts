FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/queryVariable.fs", version : "3083.0");

/**
 * THROWAWAY spike (2026-09-29): what an outside reference can and cannot do inside a Case pattern.
 * Delete this tab and the "Case pattern outside-ref spikes" studio when done.
 *
 * Outside probe      put in a repeated body; tries every way a feature reads its reference and
 *                    appends one line per run to the variable "-probeLog".
 * Top-frame replay   replays a FeatureList in a frame pushed by THIS (top-level) feature, the way std
 *                    Pattern does, after binding one query variable: isolates Case pattern's nesting.
 */

const PROBE_LOG = "-probeLog";

annotation { "Feature Type Name" : "Outside probe" }
export const outsideProbe = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Label" }
        definition.label is string;

        annotation { "Name" : "Reference", "Filter" : EntityType.FACE || EntityType.VERTEX || EntityType.EDGE || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1 }
        definition.ref is Query;
    }
    {
        const ref = definition.ref;
        var line = definition.label ~ " | case " ~ try silent(getVariable(context, "caseName")) ~ " | inPattern " ~ isInFeaturePattern(context);

        var e = "ok";
        try silent
        {
            e = "ok " ~ size(evaluateQuery(context, ref));
        }
        catch (err)
        {
            e = "ERR " ~ errText(err);
        }
        line = line ~ " | evaluateQuery " ~ e;

        e = "ok";
        try silent
        {
            e = "ok " ~ isQueryEmpty(context, ref);
        }
        catch (err)
        {
            e = "ERR " ~ errText(err);
        }
        line = line ~ " | isQueryEmpty " ~ e;

        e = "ok";
        try silent
        {
            const bb = evBox3d(context, { "topology" : ref, "tight" : true });
            e = "ok x " ~ roundToPrecision(bb.minCorner[0] / millimeter, 2);
        }
        catch (err)
        {
            e = "ERR " ~ errText(err);
        }
        line = line ~ " | evBox3d " ~ e;

        e = "ok";
        try silent
        {
            e = "ok " ~ size(evaluateQuery(context, qOwnerBody(ref)));
        }
        catch (err)
        {
            e = "ERR " ~ errText(err);
        }
        line = line ~ " | qOwnerBody " ~ e;

        // A transient copy made OUTSIDE (impossible here) is what Case pattern binds; the closest in-feature
        // equivalent: re-wrap whatever evaluateQuery returned.
        e = "ok";
        try silent
        {
            const bb = evBox3d(context, { "topology" : qUnion(evaluateQuery(context, ref)), "tight" : true });
            e = "ok x " ~ roundToPrecision(bb.minCorner[0] / millimeter, 2);
        }
        catch (err)
        {
            e = "ERR " ~ errText(err);
        }
        line = line ~ " | transient evBox3d " ~ e;

        // Kernel ops given the query directly.
        e = "ok";
        try silent
        {
            opPoint(context, id + "pt", { "point" : vector(0, 0, 0) * meter });
            opDeleteBodies(context, id + "ptDel", { "entities" : qCreatedBy(id + "pt", EntityType.BODY) });
            if (!isQueryEmpty(context, qEntityFilter(ref, EntityType.FACE)))
            {
                opExtractSurface(context, id + "extract", { "faces" : ref });
                e = "ok face";
            }
            else if (!isQueryEmpty(context, qEntityFilter(ref, EntityType.EDGE)))
            {
                opExtractWires(context, id + "extract", { "edges" : ref });
                e = "ok edge";
            }
            else
            {
                e = "n/a";
            }
            if (e != "n/a")
            {
                opDeleteBodies(context, id + "extractDel", { "entities" : qCreatedBy(id + "extract", EntityType.BODY) });
            }
        }
        catch (err)
        {
            e = "ERR " ~ errText(err);
        }
        line = line ~ " | opExtract " ~ e;

        println("[probe] " ~ line);
        setVariable(context, PROBE_LOG, append(try silent(getVariable(context, PROBE_LOG)) == undefined ? [] : getVariable(context, PROBE_LOG), line));
    });

annotation { "Feature Type Name" : "Top-frame replay" }
export const topFrameReplay = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features to replay" }
        definition.features is FeatureList;

        annotation { "Name" : "Query variable to rebind" }
        definition.bindName is string;

        annotation { "Name" : "Bind to" }
        definition.bindQuery is Query;

        annotation { "Name" : "Case name" }
        definition.caseName is string;
    }
    {
        if (isInFeaturePattern(context))
        {
            return;
        }
        const saved = getQueryVariable(context, definition.bindName);
        const savedName = try silent(getVariable(context, "caseName"));
        setQueryVariable(context, definition.bindName, qUnion(evaluateQuery(context, definition.bindQuery)));
        setVariable(context, "caseName", definition.caseName);
        const functions = valuesSortedById(context, definition.features);
        const instanceId = id + "inst";
        var errors = [];
        setFeaturePatternInstanceData(context, instanceId, { "transform" : identityTransform() });
        for (var i = 0; i < size(functions); i += 1)
        {
            try
            {
                functions[i](instanceId);
            }
            catch (err)
            {
                errors = append(errors, "feature " ~ (i + 1) ~ ": " ~ errText(err));
            }
        }
        unsetFeaturePatternInstanceData(context, instanceId);
        setQueryVariable(context, definition.bindName, saved);
        if (savedName != undefined)
        {
            setVariable(context, "caseName", savedName);
        }
        if (size(errors) > 0)
        {
            throw regenError("Top-frame replay: " ~ join(errors, "; "));
        }
    });

function errText(err) returns string
{
    if (err is map)
    {
        if (err.message != undefined)
        {
            return toString(err.message);
        }
    }
    return toString(err);
}

annotation { "Feature Type Name" : "Spike mate connector" }
export const spikeMateConnector = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "At vertex", "Filter" : EntityType.VERTEX, "MaxNumberOfPicks" : 1 }
        definition.at is Query;

        annotation { "Name" : "Owner", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1 }
        definition.owner is Query;
    }
    {
        opMateConnector(context, id + "mc", {
                    "coordSystem" : coordSystem(evVertexPoint(context, { "vertex" : definition.at }), vector(1, 0, 0), vector(0, 0, 1)),
                    "owner" : definition.owner
                });
    });

/**
 * Nesting spike: Outer replay calls Inner replay (a FeatureList holding the Inner replay feature) the way
 * Case pattern calls Close case. "Outer frame": the outer pushes the pattern frame around the call.
 * Inner mode "own": the inner pushes a frame on its own id (today's Close case); "none": no inner frame.
 */
annotation { "Feature Type Name" : "Inner replay" }
export const innerReplay = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Features to replay" }
        definition.features is FeatureList;
    }
    {
        const mode = try silent(getVariable(context, "-spikeMode"));
        if (mode == undefined)
        {
            return;
        }
        var functions = [];
        if (isInFeaturePattern(context))
        {
            functions = valuesSortedById(context, definition.features);
        }
        else
        {
            var byFeature = {};
            for (var key, listed in definition.features)
            {
                byFeature[makeId(key[size(key) - 1])] = listed;
            }
            functions = valuesSortedById(context, byFeature);
        }
        var errors = [];
        if (mode == "own" || mode == "retry")
        {
            setFeaturePatternInstanceData(context, id, { "transform" : identityTransform() });
        }
        for (var i = 0; i < size(functions); i += 1)
        {
            var failed = undefined;
            try
            {
                functions[i](id);
            }
            catch (err)
            {
                failed = errText(err);
            }
            if (failed != undefined && mode == "retry" && indexOf(failed, "SELF_INTERSECTING_CURVE_SELECTED") >= 0)
            {
                // Pop only our own frame; the caller's frame (if any) stays.
                unsetFeaturePatternInstanceData(context, id);
                try
                {
                    functions[i](id + ("direct" ~ i));
                    failed = undefined;
                }
                catch (err)
                {
                    failed = "retry: " ~ errText(err);
                }
                setFeaturePatternInstanceData(context, id, { "transform" : identityTransform() });
            }
            if (failed != undefined)
            {
                errors = append(errors, "feature " ~ (i + 1) ~ ": " ~ failed);
            }
        }
        if (mode == "own" || mode == "retry")
        {
            unsetFeaturePatternInstanceData(context, id);
        }
        setVariable(context, "-spikeErr", errors);
    });

annotation { "Feature Type Name" : "Outer replay" }
export const outerReplay = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Inner replay" }
        definition.inner is FeatureList;

        annotation { "Name" : "Name 1" }
        definition.bindName is string;

        annotation { "Name" : "Bind 1", "Filter" : EntityType.FACE || EntityType.EDGE }
        definition.bindQuery is Query;

        annotation { "Name" : "Name 2" }
        definition.bindName2 is string;

        annotation { "Name" : "Bind 2", "Filter" : EntityType.FACE || EntityType.EDGE }
        definition.bindQuery2 is Query;

        annotation { "Name" : "Outer frame" }
        definition.outerFrame is boolean;

        annotation { "Name" : "Inner mode (own / none)" }
        definition.innerMode is string;
    }
    {
        const saved1 = getQueryVariable(context, definition.bindName);
        const saved2 = getQueryVariable(context, definition.bindName2);
        setQueryVariable(context, definition.bindName, qUnion(evaluateQuery(context, definition.bindQuery)));
        setQueryVariable(context, definition.bindName2, qUnion(evaluateQuery(context, definition.bindQuery2)));
        setVariable(context, "caseName", "B");
        setVariable(context, "-spikeMode", definition.innerMode);
        setVariable(context, "-spikeErr", ["inner did not run"]);
        const callId = id + "c";
        var outerError = undefined;
        if (definition.outerFrame)
        {
            setFeaturePatternInstanceData(context, callId, { "transform" : identityTransform() });
        }
        try
        {
            values(definition.inner)[0](callId);
        }
        catch (err)
        {
            outerError = errText(err);
        }
        if (definition.outerFrame)
        {
            unsetFeaturePatternInstanceData(context, callId);
        }
        setVariable(context, "-spikeMode", undefined);
        setQueryVariable(context, definition.bindName, saved1);
        setQueryVariable(context, definition.bindName2, saved2);
        setVariable(context, "caseName", "A");
        const errors = getVariable(context, "-spikeErr");
        if (outerError != undefined || size(errors) > 0)
        {
            throw regenError("Outer replay: " ~ (outerError == undefined ? "" : "call: " ~ outerError ~ "; ") ~ join(errors, "; "));
        }
    });
