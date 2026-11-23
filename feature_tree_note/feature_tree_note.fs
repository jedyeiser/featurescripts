FeatureScript 3083;
import(path : "onshape/std/common.fs", version : "3083.0");
import(path : "onshape/std/debug.fs", version : "3083.0");
import(path : "onshape/std/feature.fs", version : "3083.0");
import(path : "onshape/std/featureList.fs", version : "3083.0");
// IMPORT: feature_tree_note_icon.svg (feature icon)
IconNamespace::import(path : "2e4531f3581a5f0093a06c4f", version : "548ea1e490b2f90e833d1f3b");

/**
 * Feature tree note +
 *
 * Based on "Feature tree note" by Jelte (original feature, idea and icon):
 * https://k2-sports.onshape.com/documents/afc74bf7462dbbdaf38eec8d/w/ef9a0f17f6e2e3b88b6a4ec5/e/856bb8c77f61f7ec3699d5fa
 * Subject / description / free text / reference geometry / reference features are Jelte's design.
 *
 * Added here: a "Query variables" list. Each item names a query variable (pick it like any query),
 * can carry a short label for the note, and chooses how the entities are shown: not shown, highlighted,
 * drawn in a DebugColor, or both. The feature does nothing to the context or the model.
 */

/** How a referenced query variable is shown while the note is viewed. */
export enum NoteShowMode
{
    annotation { "Name" : "Do not show (note only)" }
    NONE,
    annotation { "Name" : "Highlight" }
    HIGHLIGHT,
    annotation { "Name" : "Color (debug color)" }
    COLOR,
    annotation { "Name" : "Highlight and color" }
    BOTH
}

/** Same colors as DebugColor (std's enum cannot be used as a parameter type from here). */
export enum NoteColor
{
    annotation { "Name" : "Red" }
    RED,
    annotation { "Name" : "Green" }
    GREEN,
    annotation { "Name" : "Blue" }
    BLUE,
    annotation { "Name" : "Cyan" }
    CYAN,
    annotation { "Name" : "Magenta" }
    MAGENTA,
    annotation { "Name" : "Yellow" }
    YELLOW,
    annotation { "Name" : "Black" }
    BLACK,
    annotation { "Name" : "Orange" }
    ORANGE
}

function toDebugColor(c is NoteColor) returns DebugColor
{
    return {
            NoteColor.RED : DebugColor.RED,
            NoteColor.GREEN : DebugColor.GREEN,
            NoteColor.BLUE : DebugColor.BLUE,
            NoteColor.CYAN : DebugColor.CYAN,
            NoteColor.MAGENTA : DebugColor.MAGENTA,
            NoteColor.YELLOW : DebugColor.YELLOW,
            NoteColor.BLACK : DebugColor.BLACK,
            NoteColor.ORANGE : DebugColor.ORANGE
        }[c];
}

annotation { "Icon": IconNamespace::BLOB_DATA, "Feature Type Name" : "Feature tree note +",
            "Feature Name Template" : "#subject",
            "UIHint" : UIHint.NO_PREVIEW_PROVIDED,
            "Tooltip Template" : "#subject: #description",
            "Feature Type Description" : "This feature does nothing to the context or model. It stores notes in the feature tree, like a READ ME, and can highlight or color referenced geometry and query variables. Based on Feature tree note by Jelte." }
export const featureTreeNotePlus = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Subject", "UIHint" : UIHint.VARIABLE_NAME, "MaxLength" : 256 }
        definition.subject is string;

        annotation { "Name" : "Description (shows on hover)", "MaxLength" : 512 }
        definition.description is string;

        // A very large MaxLength gives a multi-line text box (as in Jelte's original).
        annotation { "Name" : "More text here (multi-line)", "MaxLength" : 640000 }
        definition.text is string;

        annotation { "Name" : "Reference geometry", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES,
                    "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
        definition.geometry is Query;

        annotation { "Name" : "Reference features", "UIHint" : UIHint.ALLOW_FLAT_SKETCH_SELECTION }
        definition.features is FeatureList;

        annotation { "Name" : "Query variables", "Item name" : "query variable",
                    "Item label template" : "#label" }
        definition.queryVariables is array;
        for (var item in definition.queryVariables)
        {
            annotation { "Name" : "Query variable", "Filter" : AllowMeshGeometry.YES && AllowFlattenedGeometry.YES,
                        "UIHint" : UIHint.PREVENT_CREATING_NEW_MATE_CONNECTORS }
            item.query is Query;

            annotation { "Name" : "Label", "MaxLength" : 256 }
            item.label is string;

            annotation { "Name" : "Show", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE] }
            item.show is NoteShowMode;

            if (item.show == NoteShowMode.COLOR || item.show == NoteShowMode.BOTH)
            {
                annotation { "Name" : "Debug color", "UIHint" : [UIHint.SHOW_LABEL, UIHint.REMEMBER_PREVIOUS_VALUE] }
                item.color is NoteColor;
            }
        }
    }
    {
        // Reference geometry keeps Jelte's behavior: yellow debug color plus highlight.
        var highlight = [];
        if (size(evaluateQuery(context, definition.geometry)) > 0)
        {
            addDebugEntities(context, definition.geometry, DebugColor.YELLOW);
            highlight = append(highlight, definition.geometry);
        }

        for (var item in definition.queryVariables)
        {
            if (item.show == NoteShowMode.NONE || size(evaluateQuery(context, item.query)) == 0)
            {
                continue;
            }
            if (item.show == NoteShowMode.COLOR || item.show == NoteShowMode.BOTH)
            {
                addDebugEntities(context, item.query, toDebugColor(item.color));
            }
            if (item.show == NoteShowMode.HIGHLIGHT || item.show == NoteShowMode.BOTH)
            {
                highlight = append(highlight, item.query);
            }
        }

        if (size(highlight) > 0)
        {
            setHighlightedEntities(context, { "entities" : qUnion(highlight) });
        }
    });
