"""One-time: seed docs/status.json with every planned explainer and deck. Refuses to overwrite."""
import glob, json, re, sys
from pathlib import Path
out = Path("docs/status.json")
if out.exists():
    sys.exit("status.json exists; edit it instead")
FAM = {
 "reference_side": ("Reference-side features", ["reference_side/*.fs"], {"Mutual Trim+":"reference_side/mutual_trim_plus.fs","Split+":"reference_side/split_plus.fs","Offset+":"reference_side/offset_plus.fs","Thicken+":"reference_side/thicken_plus.fs"}),
 "curve_tools": ("Curve tools", ["curve_tools/*.fs"], {"Clean wire":"curve_tools/clean_wire.fs","Map curve":"curve_tools/map_curve.fs","Merge curve":"curve_tools/merge_curve.fs","Evaluate profiles":"curve_tools/evaluate_profiles.fs","Fillet wire":"curve_tools/fillet_wire.fs"}),
 "variable_tools": ("Variable tools", ["variable_tools/*.fs"], {"Extract variables":"variable_tools/extract_variables.fs"}),
 "case_pattern": ("Case pattern", ["case_pattern/*.fs"], {"Case template":"case_pattern/case_pattern.fs","Case pattern":"case_pattern/case_pattern.fs"}),
 "driven_offset": ("Driven offset", ["driven_offset/driven_edge_offset.fs","driven_offset/driven_offset_surface.fs","driven_offset/evaluate_offset.fs","driven_offset/create_offset_profile.fs","driven_offset/edge_offset_utils.fs","driven_offset/offset_run_treatment.fs","driven_offset/bspline_compat.fs"], {"Driven edge offset":"driven_offset/driven_edge_offset.fs","Driven offset surface":"driven_offset/driven_offset_surface.fs","Evaluate offset":"driven_offset/evaluate_offset.fs","Create offset profile":"driven_offset/create_offset_profile.fs","Unwrap":"driven_offset/unwrap.fs"}),
 "publish_tools": ("Publish & drawing tools", ["publish_tools/*.fs"], {"Station definition":"publish_tools/station_tools/station_definition.fs","Station geometry":"publish_tools/station_tools/station_geometry.fs"}),
 "solvers": ("Solvers", ["solvers/*.fs"], {"Iterative Solve":"solvers/iterative_solve.fs","Part Volume":"solvers/Part_Vol.fs"}),
 "small_tools": ("Small tools", ["smallTools/*.fs"], {"Move Along Edge":"smallTools/Move_Along_Edge.fs","Trim curve":"smallTools/OS_Trim.fs","Trim curve +":"smallTools/betterCurveTrim.fs"}),
 "curve_mapping": ("Curve mapping (public)", ["curveMapping_public/*.fs","example_1/refSurfCreation/offsetEdges.fs"], {"Wrap Curve":"curveMapping_public/wrapCurve.fs","Wrap and Loft":"curveMapping_public/wrapAndLoft.fs","Deform":"curveMapping_public/deform.fs","Offset edges":"example_1/refSurfCreation/offsetEdges.fs"}),
 "footprint": ("Footprint", ["footprint/*.fs"], {"Generate Footprint Points":"footprint/getFootprintPoints.fs","Analyze footprint":"footprint/analyzeFootprint.fs","Integrate footprint":"footprint/integrateFootprint.fs","Scale Footprint":"footprint/scaleFootprint.fs","Arc fit":"footprint/arcFit.fs"}),
 "gordon_surface": ("Gordon surface", ["gordonSurface/*.fs"], {"Gordon Surface":"gordonSurface/gordonSurface.fs","Interior curves":"gordonSurface/interiorCurves.fs","Modify curve end":"gordonSurface/modifyCurveEnd.fs","Pull surface":"gordonSurface/pullSurface.fs","Scaled Curve":"gordonSurface/scaledCurve.fs","Simplify surface":"gordonSurface/simplifySurface.fs","makeCurvesCompitable":"gordonSurface/gordonCurveCompat.fs"}),
 "xsection": ("Cross-section (xSection)", ["xSection/**/*.fs"], {"EI and Cross Section":"xSection/features/xSect.fs","Solve GJ":"xSection/features/GJ_Feature.fs","Generate baseline":"xSection/features/generateBaseline.fs","Analyze baseline":"xSection/beam/analyzeBaseline.fs","Estimate Deflection":"xSection/features/estimateDeflection.fs","Estimate Stiffness":"xSection/features/estimateStiffness.fs","Update profile":"xSection/features/updateProfile.fs"}),
}
def slug(s): return re.sub(r"[^a-z0-9]+", "_", s.lower().replace("+", " plus")).strip("_")
docs = [{"id": "x_unwrap", "kind": "explainer", "title": "Unwrap (pre-existing)", "path": "driven_offset/docs/unwrap_explained.md",
         "sources": ["driven_offset/unwrap.fs", "driven_offset/undrape_utils.fs", "driven_offset/unwrap_part.fs", "driven_offset/edge_offset_utils.fs"],
         "state": "planned", "history": []}]
for k, (t, globs, feats) in FAM.items():
    src = sorted({Path(p).as_posix() for g in globs for p in glob.glob(g, recursive=True) if not p.endswith("_tests.fs")})
    docs.append({"id": "x_" + k, "kind": "explainer", "title": t, "path": f"docs/explainers/{k}/{k}_explained.md", "sources": src, "state": "planned", "history": []})
    for f, p in feats.items():
        s = slug(f)
        docs.append({"id": "d_" + s, "kind": "deck", "family": k, "title": f, "path": f"docs/decks/{s}/{s}.pptx", "sources": [p], "state": "planned", "history": []})
out.write_text(json.dumps({"docs": docs}, indent=2) + "\n")
print(len(docs), "docs")
