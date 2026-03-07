FeatureScript 2892;
import(path : "onshape/std/common.fs", version : "2892.0");

/**
 * XSECTION LANGUAGE / TRANSLATION MODULE
 * =========================================
 *
 * Provides localized label lookup tables for xSection analysis output.
 *
 * Supported languages: ENG (English), GER (Deutsch)
 *
 * Usage pattern:
 *   var label = overallAnalysisTranslationLookup[LANGUAGE.ENG]["Prismatic stiffness (lb/in)"];
 *   var header = mainTableHeaderTranslationLookup[LANGUAGE.GER]["Station"];
 *
 * Lookup tables:
 *   - `overallAnalysisTranslationLookup` : summary-row labels (stiffness metrics, weight)
 *   - `mainTableHeaderTranslationLookup` : cross-section table column headers
 *
 * Key conventions:
 *   - Keys are the canonical English strings (same as LANGUAGE.ENG values)
 *   - Missing keys throw at runtime; callers must use `tryGetKey` for safe access
 *     when a key may not exist in all language maps.
 */
export enum LANGUAGE
{
    annotation { "Name" : "Deutsch"}
    GER,
    annotation {"Name" : "English"}
    ENG
}

export const overallAnalysisTranslationLookup = {
    LANGUAGE.ENG : {
        "Prismatic stiffness (lb/in)" : "Prismatic stiffness (lb/in)",
        "Prismatic stiffness (mm/30kg)" : "Prismatic stiffness (mm/30kg)",
        "Estimated stiffness (lb/in)" : "Estimated stiffness (lb/in)",
        "Estimated stiffness (mm/30kg)" : "Estimated stiffness (mm/30kg)",
        "Weight (kg)" : "Weight (kg)"
    },
    LANGUAGE.GER : {
        "Prismatic stiffness (lb/in)" : "Prismatische Steifigkeit (lb/in)",
        "Prismatic stiffness (mm/30kg)" : "Prismatische Steifigkeit (mm/30Kg)",
        "Estimated stiffness (lb/in)" : "Geschätzte Steifigkeit (lb/in)",
        "Estimated stiffness (mm/30kg)" : "Geschätzte Steifigkeit (mm/30kg)",
        "Weight (kg)" : "Gewicht (kg)"
    },
};

export const mainTableHeaderTranslationLookup = {
    LANGUAGE.ENG : {
        "Station" : "Station",
        "X" : "X",
        "EI" : "EI",
        "GJ" : "GJ",
        "NA Height" : "NA Height",
        "Beam Width" : "Beam Width",
        "Beam Height" : "Beam Height",
        "NA Percentage": "NA Percentage",
        "Lineal Density" : "Lineal Density"
    },
    LANGUAGE.GER : {
        "Station" : "Punkt",
        "X" : "X Achse",
        "EI" : "Biegesteifigkeit",
        "GJ" : "Torsionssteifigkeit",
        "NA Height" : "Höhe der neutralen Achse",
        "Beam Width" : "Strahlbreite",
        "Beam Height" : "Strahlenhöhe",
        "NA Percentage": "Neutrale Achshöhe/Balkenhöhe",
        "Lineal Density" : "Lineare Dichte"
    }
};