FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: deform_tag_icon.svg (feature icon)
IconNamespace::import(path : "416d971d75cdf57db8f459e6", version : "1d0a4cbca37efaa00cc6e0f3");

/**
 * Deform tag: prepares geometry for the eocDataTools API deform (Geometry > Deform). It changes no geometry. It writes
 * a schema-versioned attribute (deformRole, value schema "deformRole/1") on the picked entities, and the app reads the
 * tags with one evaluateFeatureScript (qHasAttribute / getAttribute, grouped on `group` and `role`).
 *
 * One tag feature per deform group. A group is named by the user and must be unique in the Part Studio.
 *
 * The attribute value is an ARRAY of tag maps (an entity may belong to several groups or roles). Each tag map:
 *   schema, group, role ("from" | "to" | "source" | "refFrom" | "refTo"), index (pick order within the role),
 *   name (the owner body's name when the dialog was last edited), feature (this feature's id), extra (hidden text),
 *   and on from / to / refFrom / refTo entities the options: prevailing, extension, flipFrom, flipTo, maxKinkDeg,
 *   smoothMaxDeviationMm, accuracy. `group` is the deform name: the record key AND the name of the result. Reference points also carry originMm. A source entity whose
 *   Source item has mate connectors also carries mateConnectors: [{ id (REST id), originMm, xAxis, zAxis }]; the app
 *   reads each connector's name through the API and re-creates it on the deformed body.
 *
 * The dialog's parameter list is frozen once published (correction 64): new options go into `extraOptions`, which is
 * copied into the attribute as text.
 */

/** Attribute name; the value carries the schema string. */
export const DEFORM_TAG_ATTRIBUTE = "deformRole";
export const DEFORM_TAG_SCHEMA = "deformRole/1";

/** Direction that stays fixed while the chains are matched (or none). */
export enum DeformTagPrevailing
{
    annotation { "Name" : "None" }
    NONE,
    annotation { "Name" : "+X" }
    PLUS_X,
    annotation { "Name" : "-X" }
    MINUS_X,
    annotation { "Name" : "+Y" }
    PLUS_Y,
    annotation { "Name" : "-Y" }
    MINUS_Y,
    annotation { "Name" : "+Z" }
    PLUS_Z,
    annotation { "Name" : "-Z" }
    MINUS_Z
}

/** How the mapping continues past the ends of a chain. */
export enum DeformTagExtension
{
    annotation { "Name" : "Tangent" }
    TANGENT,
    annotation { "Name" : "Osculating" }
    OSCULATING
}

/** Fit accuracy the backend should use. */
export enum DeformTagAccuracy
{
    annotation { "Name" : "Standard" }
    STANDARD,
    annotation { "Name" : "Fine" }
    FINE
}

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Deform tag",
            "Feature Type Description" : "Tags the From and To chains, reference points and source geometry of one deform group, and records the deform options, for the API deform. Changes no geometry. Place it at the bottom of the feature tree.",
            "Editing Logic Function" : "deformTagEditLogic" }
export const deformTag = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Group Name" : "Name", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Deform name", "Default" : "", "MaxLength" : 128,
                        "Description" : "Names this deform and its result (the step file, etc). Unique in the Part Studio: the app keys its records on it, so renaming starts a new record." }
            definition.groupName is string;
        }

        annotation { "Group Name" : "Geometry", "Collapsed By Default" : false }
        {
            annotation { "Name" : "From chain", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "Edges or wire bodies the source currently follows." }
            definition.fromChain is Query;

            annotation { "Name" : "To chain", "Filter" : EntityType.EDGE || (EntityType.BODY && BodyType.WIRE),
                        "Description" : "Edges or wire bodies the source is deformed onto." }
            definition.toChain is Query;

            annotation { "Name" : "Reference point on From", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "Optional vertex or mate connector on the From chain." }
            definition.refFrom is Query;

            annotation { "Name" : "Reference point on To", "Filter" : EntityType.VERTEX || BodyType.MATE_CONNECTOR, "MaxNumberOfPicks" : 1,
                        "Description" : "Optional vertex or mate connector on the To chain." }
            definition.refTo is Query;

            annotation { "Name" : "Source", "Item name" : "source", "Item label template" : "Source",
                        "Description" : "What to deform. Each item: the geometry, and the mate connectors that stay with it." }
            definition.sourceItems is array;
            for (var item in definition.sourceItems)
            {
                annotation { "Name" : "Body", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE), "MaxNumberOfPicks" : 1,
                            "Description" : "The solid, surface or wire body to deform. One body per item." }
                item.body is Query;

                annotation { "Name" : "Mate connectors", "Filter" : BodyType.MATE_CONNECTOR,
                            "Description" : "Connectors the deformed geometry owns; the app re-creates and names them. None = it owns none." }
                item.connectors is Query;
            }
        }

        annotation { "Group Name" : "Options", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Prevailing direction", "Default" : DeformTagPrevailing.PLUS_X, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "A wire has no direction in Onshape. The From chain is turned so its start-to-end line points this way; the To chain is then matched to From. None keeps Onshape's order." }
            definition.prevailing is DeformTagPrevailing;

            annotation { "Name" : "End extension", "Default" : DeformTagExtension.TANGENT, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "How the map continues past a chain end. Tangent: straight on. Osculating: along the circle of the end curvature." }
            definition.extension is DeformTagExtension;

            annotation { "Name" : "Flip From direction", "Default" : false,
                        "Description" : "Reverses the From chain AFTER the prevailing direction is applied (with None: reverses Onshape's order)." }
            definition.flipFrom is boolean;

            annotation { "Name" : "Flip To direction", "Default" : false,
                        "Description" : "Reverses the To chain AFTER it is matched to From. To has no prevailing direction of its own." }
            definition.flipTo is boolean;

            annotation { "Name" : "Max kink angle", "Description" : "Largest tangent break between joined edges a chain accepts (0 to 30 deg). 0 = the app's default." }
            isAngle(definition.maxKink, { (degree) : [0, 0, 30] } as AngleBoundSpec);

            annotation { "Name" : "Smooth max deviation", "Description" : "Smooths tangent breaks within this distance. 0 = off." }
            isLength(definition.smoothMaxDeviation, { (millimeter) : [0, 0, 1000] } as LengthBoundSpec);

            annotation { "Name" : "Accuracy", "Default" : DeformTagAccuracy.STANDARD, "UIHint" : UIHint.SHOW_LABEL,
                        "Description" : "Fit accuracy of the deformed edges and faces. Fine is slower." }
            definition.accuracy is DeformTagAccuracy;

            // Room for options added after the first version (correction 64); copied into the attribute as text.
            annotation { "Name" : "Extra options", "Default" : "", "MaxLength" : 4096, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
            definition.extraOptions is string;
        }

        // Owner-body names, one line per picked entity in pick order, captured by the editing logic (the feature body
        // cannot read names, correction 36).
        annotation { "Name" : "From names", "Default" : "", "MaxLength" : 8192, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.fromNames is string;

        annotation { "Name" : "To names", "Default" : "", "MaxLength" : 8192, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.toNames is string;

        annotation { "Name" : "Source names", "Default" : "", "MaxLength" : 8192, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.sourceNames is string;
    }
    {
        definition.groupName = replace(definition.groupName, "^\\s+|\\s+$", "");
        if (definition.groupName == "")
        {
            throw regenError("Enter a deform name.", ["groupName"]);
        }
        if (isQueryEmpty(context, definition.fromChain))
        {
            throw regenError("Select the From chain.", ["fromChain"]);
        }
        if (isQueryEmpty(context, definition.toChain))
        {
            throw regenError("Select the To chain.", ["toChain"]);
        }
        if (size(definition.sourceItems) == 0)
        {
            throw regenError("Add a Source item with the geometry to deform.", ["sourceItems"]);
        }

        var warnings = [];

        // Another tag with this group name: the app would merge the two.
        if (groupInUse(context, definition.groupName))
        {
            warnings = append(warnings, "Another Deform tag already uses the deform name '" ~ definition.groupName ~ "'.");
        }

        var options = {
                "prevailing" : prevailingText(definition.prevailing),
                "extension" : definition.extension == DeformTagExtension.OSCULATING ? "osculating" : "tangent",
                "flipFrom" : definition.flipFrom,
                "flipTo" : definition.flipTo,
                "accuracy" : definition.accuracy == DeformTagAccuracy.FINE ? "fine" : "standard"
            };
        // 0 means "the app's default": leave the key out, so the engine does not read 0 as a zero tolerance.
        if (definition.maxKink > 0 * degree)
        {
            options.maxKinkDeg = definition.maxKink / degree;
        }
        if (definition.smoothMaxDeviation > 0 * millimeter)
        {
            options.smoothMaxDeviationMm = definition.smoothMaxDeviation / millimeter;
        }

        // Reference points first: their origins (mm) ride on every From / To tag as refFromOriginMm / refToOriginMm,
        // so the app has them even when a mate connector cannot carry a tag (unverified).
        var refTargets = {};
        var chainOptions = options;
        for (var spec in [{ "role" : "refFrom", "query" : definition.refFrom }, { "role" : "refTo", "query" : definition.refTo }])
        {
            if (isQueryEmpty(context, spec.query))
            {
                continue;
            }
            const connectors = evaluateQuery(context, qBodyType(qOwnerBody(spec.query), BodyType.MATE_CONNECTOR));
            var origin;
            if (size(connectors) > 0)
            {
                refTargets[spec.role] = { "entity" : connectors[0], "isConnector" : true };
                origin = evMateConnector(context, { "mateConnector" : connectors[0] }).origin;
            }
            else
            {
                refTargets[spec.role] = { "entity" : evaluateQuery(context, spec.query)[0], "isConnector" : false };
                origin = evVertexPoint(context, { "vertex" : spec.query });
            }
            chainOptions[spec.role ~ "OriginMm"] = [origin[0] / millimeter, origin[1] / millimeter, origin[2] / millimeter];
        }

        // Source items. Each item is entities to deform plus the mate connectors that belong to its (single) body. Connector
        // frames are in mm; ids are the REST ids, so the app reads each connector's name through the API and re-creates it on
        // the deformed body. An entity picked in two items is tagged once (its connectors merge), so it is deformed once.
        var entries = {};
        var entryOrder = [];
        for (var item in definition.sourceItems)
        {
            const itemEntities = evaluateQuery(context, item.body);
            if (size(itemEntities) != 1)
            {
                throw regenError("Pick one body in each Source item.", ["sourceItems"]);
            }
            var list = [];
            for (var mc in evaluateQuery(context, qBodyType(qOwnerBody(item.connectors), BodyType.MATE_CONNECTOR)))
            {
                const cs = evMateConnector(context, { "mateConnector" : mc });
                list = append(list, { "id" : transientQueriesToStrings([mc])[0],
                        "originMm" : [cs.origin[0] / millimeter, cs.origin[1] / millimeter, cs.origin[2] / millimeter],
                        "xAxis" : [cs.xAxis[0], cs.xAxis[1], cs.xAxis[2]], "zAxis" : [cs.zAxis[0], cs.zAxis[1], cs.zAxis[2]] });
            }
            for (var entity in itemEntities)
            {
                const entityKey = transientQueriesToStrings([entity])[0];
                if (entries[entityKey] == undefined)
                {
                    entries[entityKey] = { "entity" : entity, "connectors" : list };
                    entryOrder = append(entryOrder, entityKey);
                }
                else
                {
                    entries[entityKey].connectors = concatenateArrays([entries[entityKey].connectors, list]);
                }
            }
        }
        const sourceNameList = splitNames(definition.sourceNames, size(entryOrder));

        var replaced = 0;
        var counts = { "refFrom" : 0, "refTo" : 0 };
        var chainNames = {};
        const roles = [
                { "role" : "from", "query" : definition.fromChain, "names" : definition.fromNames, "options" : chainOptions },
                { "role" : "to", "query" : definition.toChain, "names" : definition.toNames, "options" : chainOptions }
            ];
        for (var spec in roles)
        {
            const entities = evaluateQuery(context, spec.query);
            const names = splitNames(spec.names, size(entities));
            for (var i = 0; i < size(entities); i += 1)
            {
                replaced += tagEntity(context, id, definition, entities[i], spec.role, i, names[i], spec.options, {});
            }
            counts[spec.role] = size(entities);
            chainNames[spec.role] = uniqueNames(names);
        }
        var sourceLabels = [];
        for (var s = 0; s < size(entryOrder); s += 1)
        {
            const entry = entries[entryOrder[s]];
            replaced += tagEntity(context, id, definition, entry.entity, "source", s, sourceNameList[s], {},
                size(entry.connectors) > 0 ? { "mateConnectors" : entry.connectors } : {});
            sourceLabels = append(sourceLabels, (sourceNameList[s] == "" ? "body " ~ (s + 1) : sourceNameList[s])
                ~ (size(entry.connectors) > 0 ? " (" ~ size(entry.connectors) ~ " mate connector" ~ (size(entry.connectors) == 1 ? "" : "s") ~ ")" : ""));
        }
        counts.source = size(entryOrder);

        for (var role in keys(refTargets))
        {
            const refInfo = refTargets[role];
            const refOptions = { "originMm" : chainOptions[role ~ "OriginMm"] };
            if (refInfo.isConnector)
            {
                // Setting an attribute on a mate connector body is unverified: a refusal is reported, not fatal.
                var tagged = false;
                try silent
                {
                    replaced += tagEntity(context, id, definition, refInfo.entity, role, 0, "", options, refOptions);
                    tagged = true;
                }
                if (!tagged)
                {
                    warnings = append(warnings, "The mate connector could not take a tag; its origin is carried on the " ~ (role == "refFrom" ? "From" : "To") ~ " tags instead.");
                }
            }
            else
            {
                replaced += tagEntity(context, id, definition, refInfo.entity, role, 0, "", options, refOptions);
            }
            counts[role] = 1;
        }

        if (replaced > 0)
        {
            warnings = append(warnings, replaced ~ " tag" ~ (replaced == 1 ? "" : "s")
                ~ " of an earlier Deform tag with this group and role were replaced.");
        }

        // What the user needs to see in the feature tree: the deform name, what is in each role (with body names), and the
        // reference points. Names are the ones captured by the editing logic (empty when the feature was inserted by API).
        const summary = "Deform '" ~ definition.groupName ~ "'. From: " ~ chainLabel(counts.from, chainNames.from)
            ~ ". To: " ~ chainLabel(counts.to, chainNames.to)
            ~ ". Source: " ~ join(sourceLabels, ", ")
            ~ (counts.refFrom == 1 || counts.refTo == 1 ? ". Reference points:" ~ (counts.refFrom == 1 ? " From" : "") ~ (counts.refTo == 1 ? " To" : "") : "")
            ~ ".";
        if (size(warnings) > 0)
        {
            reportFeatureWarning(context, id, join(warnings, " ") ~ " " ~ summary);
        }
        else
        {
            reportFeatureInfo(context, id, summary);
        }
    }, { "groupName" : "", "prevailing" : DeformTagPrevailing.PLUS_X, "extension" : DeformTagExtension.TANGENT,
         "flipFrom" : false, "flipTo" : false, "maxKink" : 0 * degree, "smoothMaxDeviation" : 0 * millimeter,
         "accuracy" : DeformTagAccuracy.STANDARD, "extraOptions" : "", "fromNames" : "", "toNames" : "", "sourceNames" : "",
         "refFrom" : qNothing(), "refTo" : qNothing() });

/** The non-empty names of `names`, each once, in order. */
function uniqueNames(names is array) returns array
{
    var seen = {};
    var result = [];
    for (var name in names)
    {
        if (name != "" && seen[name] == undefined)
        {
            seen[name] = true;
            result = append(result, name);
        }
    }
    return result;
}

/** "3 picks (Wire A, Wire B)" -- the number of picked entities and the bodies they belong to. */
function chainLabel(count is number, names is array) returns string
{
    return count ~ (count == 1 ? " pick" : " picks") ~ (size(names) > 0 ? " (" ~ join(names, ", ") ~ ")" : "");
}

/** Writes one tag; returns 1 when it replaced an existing Deform tag on the entity, else 0. */
function tagEntity(context is Context, id is Id, definition is map, entity is Query, role is string, index is number,
    entityName is string, options is map, more is map) returns number
{
    var data = {
            "schema" : DEFORM_TAG_SCHEMA,
            "group" : definition.groupName,
            "role" : role,
            "index" : index,
            "name" : entityName,
            "feature" : toString(id),
            "extra" : definition.extraOptions
        };
    for (var entry in options)
    {
        data[entry.key] = entry.value;
    }
    for (var entry in more)
    {
        data[entry.key] = entry.value;
    }
    // The attribute is an ARRAY of tag maps, so several groups (or roles) can share one entity: keep the others,
    // replace the entry of the same group and role.
    var tags = [];
    var replaced = 0;
    const existing = getAttribute(context, { "entity" : entity, "name" : DEFORM_TAG_ATTRIBUTE });
    if (existing is array)
    {
        for (var tag in existing)
        {
            if (tag is map && tag.group == data.group && tag.role == data.role)
            {
                replaced = 1;
            }
            else
            {
                tags = append(tags, tag);
            }
        }
    }
    tags = append(tags, data);
    setAttribute(context, { "entities" : entity, "name" : DEFORM_TAG_ATTRIBUTE, "attribute" : tags });
    return replaced;
}

/** True when an earlier Deform tag (any entity type) already uses the group name. */
function groupInUse(context is Context, groupName is string) returns boolean
{
    const tagged = qUnion([
                qHasAttribute(qEverything(EntityType.BODY), DEFORM_TAG_ATTRIBUTE),
                qHasAttribute(qEverything(EntityType.EDGE), DEFORM_TAG_ATTRIBUTE),
                qHasAttribute(qEverything(EntityType.FACE), DEFORM_TAG_ATTRIBUTE),
                qHasAttribute(qEverything(EntityType.VERTEX), DEFORM_TAG_ATTRIBUTE)
            ]);
    for (var entity in evaluateQuery(context, tagged))
    {
        const tags = getAttribute(context, { "entity" : entity, "name" : DEFORM_TAG_ATTRIBUTE });
        if (tags is array)
        {
            for (var tag in tags)
            {
                if (tag is map && tag.group == groupName)
                {
                    return true;
                }
            }
        }
    }
    return false;
}

/** The stored names as an array of exactly `count` strings; empty strings when the count no longer matches. */
function splitNames(stored is string, count is number) returns array
{
    var names = [];
    if (stored != "")
    {
        names = splitByRegexp(stored, "\n");
    }
    if (size(names) != count)
    {
        names = [];
    }
    var result = [];
    for (var i = 0; i < count; i += 1)
    {
        result = append(result, size(names) == 0 ? "" : names[i]);
    }
    return result;
}

function prevailingText(p is DeformTagPrevailing) returns string
{
    if (p == DeformTagPrevailing.PLUS_X)
    {
        return "+x";
    }
    if (p == DeformTagPrevailing.MINUS_X)
    {
        return "-x";
    }
    if (p == DeformTagPrevailing.PLUS_Y)
    {
        return "+y";
    }
    if (p == DeformTagPrevailing.MINUS_Y)
    {
        return "-y";
    }
    if (p == DeformTagPrevailing.PLUS_Z)
    {
        return "+z";
    }
    if (p == DeformTagPrevailing.MINUS_Z)
    {
        return "-z";
    }
    return "none";
}

/** One line per entity: the name of its owner body (the entity itself when it is a body), in pick order. */
function ownerNames(context is Context, selection is Query) returns string
{
    var lines = [];
    for (var entity in evaluateQuery(context, selection))
    {
        const owner = evaluateQuery(context, qOwnerBody(entity));
        const target = size(owner) > 0 ? owner[0] : entity;
        const name = getProperty(context, { "entity" : target, "propertyType" : PropertyType.NAME });
        lines = append(lines, name is string ? name : "");
    }
    return join(lines, "\n");
}

/** Captures the owner-body names of the picks, so a later rename of a source does not lose the pairing. */
export function deformTagEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    definition.fromNames = ownerNames(context, definition.fromChain);
    definition.toNames = ownerNames(context, definition.toChain);
    // One line per distinct source entity, in item order (the same order the feature body tags them in).
    var seen = {};
    var lines = [];
    for (var item in definition.sourceItems)
    {
        for (var entity in evaluateQuery(context, item.body))
        {
            const key = transientQueriesToStrings([entity])[0];
            if (seen[key] == undefined)
            {
                seen[key] = true;
                lines = append(lines, ownerNames(context, entity));
            }
        }
    }
    definition.sourceNames = join(lines, "\n");
    return definition;
}
