FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
// IMPORT: body_identity_icon.svg (feature icon)
IconNamespace::import(path : "eca6701c4f4c36eb73d9da99", version : "014c5808edfc70e2e2632275");

/**
 * Body identity: gives bodies a stable identity (opNameEntity), so features, mates, sketch planes, derives and released
 * part numbers that refer to a body by qNamed(identity) keep resolving when the body is replaced -- the import update of a
 * body the deform backend swaps in, whose id changes. It changes no geometry. Brief: docs/briefs/body_identity_feature.md.
 *
 * HOW THE BODIES ARE FOUND (differs from the brief): a feature cannot read a body's NAME while it regenerates (getProperty
 * throws "Cannot get properties during feature regeneration", correction 36), so the feature cannot search by name. Each item
 * therefore holds a SELECTION (the body, set through REST as a deterministic id) and the IDENTITY string. The backend, which
 * can read names through REST, re-points the selection after an import update; the identity string never changes, so
 * everything downstream keeps resolving. In the dialog the identity defaults to the picked body's name (the editing logic can
 * read it).
 *
 * The per-item operation id comes from the IDENTITY (sanitised plus a checksum), never from list order or the feature id, so
 * a list that gains or loses items, or a feature re-created by REST, names the same bodies the same way.
 *
 * A body that is missing or listed twice, or an identity that is empty or already used, is a feature WARNING that names it;
 * the other items are still named. The parameter list is frozen once published (correction 64): anything new goes into
 * extraOptions (hidden text).
 */

/** Characters used to checksum an identity (anything else counts as 0). */
const CHECKSUM_ALPHABET = " !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~";

annotation { "Icon" : IconNamespace::BLOB_DATA, "Feature Type Name" : "Body identity",
            "Feature Type Description" : "Names bodies so downstream features, mates and part numbers keep resolving when the bodies are replaced. Changes no geometry.",
            "Editing Logic Function" : "bodyIdentityEditLogic" }
export const bodyIdentity = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Bodies", "Item name" : "body", "Item label template" : "#identity",
                    "Description" : "Each item: a body and the identity it carries. Add items through the API or by picking." }
        definition.bodies is array;
        for (var item in definition.bodies)
        {
            annotation { "Name" : "Body", "Filter" : EntityType.BODY && (BodyType.SOLID || BodyType.SHEET || BodyType.WIRE), "MaxNumberOfPicks" : 1,
                        "Description" : "The body to name. Re-point it after the body is replaced; the identity stays." }
            item.body is Query;

            annotation { "Name" : "Identity", "Default" : "", "MaxLength" : 256,
                        "Description" : "The stable name (qNamed). Empty: the picked body's name. Unique in the Part Studio. Do not change it once downstream features use it." }
            item.identity is string;
        }

        // Room for options added after the first version: the parameter list cannot be extended safely once published
        // (correction 64).
        annotation { "Name" : "Extra options", "Default" : "", "MaxLength" : 4096, "UIHint" : [UIHint.ALWAYS_HIDDEN] }
        definition.extraOptions is string;
    }
    {
        var named = [];
        var problems = [];
        var usedIdentities = {};
        var usedBodies = {};
        var position = 0;
        for (var item in definition.bodies)
        {
            position += 1;
            const identity = replace(item.identity, "^\\s+|\\s+$", "");
            if (identity == "")
            {
                problems = append(problems, "Item " ~ position ~ " has no identity.");
                continue;
            }
            if (usedIdentities[identity] != undefined)
            {
                problems = append(problems, "The identity '" ~ identity ~ "' is listed twice; the second item was skipped.");
                continue;
            }
            usedIdentities[identity] = true;

            const bodies = evaluateQuery(context, item.body);
            if (size(bodies) == 0)
            {
                problems = append(problems, "The body for '" ~ identity ~ "' was not found: re-select it.");
                continue;
            }
            if (size(bodies) > 1)
            {
                problems = append(problems, "'" ~ identity ~ "' selects " ~ size(bodies) ~ " bodies; pick one.");
                continue;
            }
            const bodyKey = transientQueriesToStrings(bodies)[0];
            if (usedBodies[bodyKey] != undefined)
            {
                problems = append(problems, "'" ~ identity ~ "' is the same body as '" ~ usedBodies[bodyKey] ~ "'; it keeps the first identity.");
                continue;
            }
            // An earlier feature (another Body identity) may already carry this identity: two entities under one name would
            // make qNamed ambiguous. The query of an unknown name is empty; the try only guards that assumption (unverified).
            const alreadyNamed = try silent(evaluateQuery(context, qNamed(identity)));
            if (alreadyNamed != undefined && size(alreadyNamed) > 0)
            {
                problems = append(problems, "The identity '" ~ identity ~ "' is already used earlier in the feature list; skipped.");
                continue;
            }
            usedBodies[bodyKey] = identity;
            opNameEntity(context, id + idComponent(identity), { "entity" : bodies[0], "entityName" : identity });
            named = append(named, identity);
        }

        const summary = size(named) == 0 ? "No bodies named." : "Named " ~ size(named) ~ ": " ~ join(named, ", ") ~ ".";
        if (size(problems) > 0)
        {
            reportFeatureWarning(context, id, join(problems, " ") ~ " " ~ summary);
        }
        else
        {
            reportFeatureInfo(context, id, summary);
        }
    }, { "bodies" : [], "extraOptions" : "" });

/**
 * An operation-id component for an identity: letters and digits only, plus a checksum of the whole text so two identities
 * that sanitise to the same string still differ. Deterministic, independent of list order and of the feature id.
 */
function idComponent(identity is string) returns string
{
    const characters = splitIntoCharacters(identity);
    var sum = 0;
    for (var i = 0; i < size(characters); i += 1)
    {
        const position = indexOf(CHECKSUM_ALPHABET, characters[i]);
        sum = (sum + (position + 2) * (i + 1) * (i + 7)) % 1000003;
    }
    const readable = replace(identity, "[^A-Za-z0-9]", "_");
    const shortened = size(characters) > 40 ? substring(readable, 0, 40) : readable;
    return "n_" ~ shortened ~ "_" ~ size(characters) ~ "_" ~ sum;
}

/** An empty identity becomes the picked body's name (getProperty works here, not while the feature regenerates). */
export function bodyIdentityEditLogic(context is Context, id is Id, oldDefinition is map, definition is map,
    isCreating is boolean, specifiedParameters is map, hiddenBodies is Query) returns map
{
    var items = definition.bodies;
    for (var i = 0; i < size(items); i += 1)
    {
        if ((items[i].identity == undefined || items[i].identity == "") && !isQueryEmpty(context, items[i].body))
        {
            const name = getProperty(context, { "entity" : items[i].body, "propertyType" : PropertyType.NAME });
            if (name is string)
            {
                items[i].identity = name;
            }
        }
    }
    definition.bodies = items;
    return definition;
}
