_CAT = ("string", "Collection under Styles, e.g. SurfaceStyles, PointStyles, AlignmentStyles, ProfileStyles, ProfileViewStyles, "
        "SectionViewStyles, PipeStyles, StructureStyles, CorridorStyles, FeatureLineStyles, "
        "LabelSetStyles.AlignmentLabelSetStyles, LabelStyles.PointLabelStyles.LabelStyles, PartsListSet", True)

TOOLS = {
    "civil.list": ("List Civil objects. kind: surfaces | alignments | profiles | sites | pipe_networks | pressure_networks | "
                   "point_groups | corridors | assemblies | subassemblies | feature_lines | profile_views | section_views | "
                   "sample_line_groups | parcels.", {"kind": ("string", "Kind", True)}),
    "civil.drawing_settings": ("Read or set Civil drawing settings (units/zone ...).", {"props": ("object", "Settings to set")}),
    "styles.categories": ("Discover style collection names (optionally below a path like 'LabelStyles').", {"path": ("string", "")}),
    "styles.list": ("List style names in a collection.", {"category": _CAT}),
    "styles.create": ("Create a style (or get existing) and set properties.", {"category": _CAT, "name": ("string", "", True),
                                                                              "props": ("object", "Properties")}),
    "styles.get": ("All properties of a style (or one dotted path).", {"category": _CAT, "name": ("string", "", True), "path": ("string", "")}),
    "styles.set": ("Set style properties (dotted paths allowed).", {"category": _CAT, "name": ("string", "", True),
                                                                   "props": ("object", "", True)}),
    "styles.display": ("Show/hide/colour style display components in plan/model/profile/section view.",
                       {"category": _CAT, "name": ("string", "", True), "components": ("strings", "e.g. MajorContour, Triangles, Line, Curve", True),
                        "view": ("string", "plan | model | profile | section"), "visible": ("boolean", ""), "color": ("string", ""),
                        "layer": ("string", ""), "linetype": ("string", ""), "lineweight": ("number", "")}),
}
