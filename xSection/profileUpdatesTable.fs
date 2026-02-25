FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");
import(path : "onshape/std/table.fs", version : "2892.0");

annotation { "Table Type Name" : "Profile Updates" }
export const profileUpdatesTable = defineTable(function(context is Context, definition is map) returns TableArray
precondition
{
}
{
    var bodiesWithData = evaluateQuery(context, qHasAttribute("profileUpdates"));
    if (size(bodiesWithData) == 0)
        return tableArray([table("Profile Updates (no data)", [], [])]);

    var allData = getAttribute(context, {
        "entity" : bodiesWithData[0],
        "name"   : "profileUpdates"
    });

    if (allData == undefined || size(keys(allData)) == 0)
        return tableArray([table("Profile Updates (no data)", [], [])]);

    var columns = [
        tableColumnDefinition("x",      "X (mm)",                   TableTextAlignment.RIGHT),
        tableColumnDefinition("tMeas",  "Meas. Thickness (mm)",     TableTextAlignment.RIGHT),
        tableColumnDefinition("EIMeas", "Meas. Stiffness (N·m²)",   TableTextAlignment.RIGHT),
        tableColumnDefinition("tUpd",   "Updated Thickness (mm)",   TableTextAlignment.RIGHT),
        tableColumnDefinition("EIUpd",  "Updated Stiffness (N·m²)", TableTextAlignment.RIGHT),
        tableColumnDefinition("dT",     "Δt (mm)",                  TableTextAlignment.RIGHT),
        tableColumnDefinition("dEI",    "ΔEI (N·m²)",               TableTextAlignment.RIGHT)
    ];

    var tables = [];
    for (var featureKey in keys(allData))
    {
        var entry = allData[featureKey];
        var title = (entry.updateName == "") ? "Profile Update" : entry.updateName;
        var rows  = [];

        for (var row in entry.tableData)
        {
            rows = append(rows, tableRow({
                "x"      : toString(row.xMm),
                "tMeas"  : toString(row.tMeasMm),
                "EIMeas" : toString(row.EIMeas),
                "tUpd"   : toString(row.tUpdMm),
                "EIUpd"  : toString(row.EIUpd),
                "dT"     : toString(row.tUpdMm - row.tMeasMm),
                "dEI"    : toString(row.EIUpd - row.EIMeas)
            }));
        }

        tables = append(tables, table(title, columns, rows));
    }

    return tableArray(tables);
});
