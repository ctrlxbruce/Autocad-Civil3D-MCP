"""Gravity network hydraulics and QC, and small-catchment hydrology."""

TOOLS = {
    "pipes.hydraulics": ("Gravity network hydraulics with Manning's equation: full capacity and velocity of every pipe; with design "
                         "flows (L/s per pipe name) also the depth ratio d/D, velocity and a simplified HGL from the outlet. "
                         "Flags pipes outside the velocity and depth criteria.",
                         {"network": ("string", "", True),
                          "flows": ("object", "Design flow per pipe name in L/s, e.g. {\"P-1\": 12.5}"),
                          "n": ("number", "Manning's n (default 0.013)"),
                          "min_velocity": ("number", "m/s, default 0.6 (self-cleansing)"),
                          "max_velocity": ("number", "m/s, default 3.0"),
                          "max_depth_ratio": ("number", "d/D, default 0.8"),
                          "outlet_hgl": ("number", "Tailwater elevation at the outlet (m); default: normal depth")}),
    "pipes.qc": ("Quality check of a gravity network: slopes, cover at each pipe end, unconnected pipe ends, sumps above pipe "
                 "inverts, and rims that differ from the ground.",
                 {"network": ("string", "", True), "surface": ("string", "Default: network reference surface"),
                  "min_cover": ("number", "m, default 1.0"), "max_cover": ("number", "m"),
                  "min_slope": ("number", "m/m, e.g. 0.005"), "max_slope": ("number", "m/m"),
                  "rim_tolerance": ("number", "m, default 0.05")}),
    "hydrology.rational": ("Peak runoff by the Rational method, Q = C i A / 360 (Q m³/s, i mm/h, A ha). Area from area_ha or a "
                           "closed boundary polyline; several areas give a weighted C.",
                           {"intensity": ("number", "Rainfall intensity (mm/h) for a duration equal to tc", True),
                            "c": ("number", "Runoff coefficient 0-1"), "area_ha": ("number", "Area in hectares"),
                            "boundary": ("string", "Closed polyline handle (instead of area_ha)"),
                            "areas": ("array", "[{name, area_ha or boundary, c}] for a weighted C")}),
    "hydrology.time_of_concentration": ("Time of concentration in minutes by Kirpich, California Culvert Practice (used in "
                                        "IRC:SP:13) and FAA. Give length + fall (or slope), or a flow path handle.",
                                        {"length": ("number", "Flow length (m)"), "fall": ("number", "Fall along the path (m)"),
                                         "slope": ("number", "m/m, instead of fall"),
                                         "path": ("string", "3D polyline handle, e.g. from hydrology_flow_path"),
                                         "method": ("string", "kirpich | california | faa | all (default)"),
                                         "c": ("number", "Runoff coefficient, needed for FAA")}),
    "hydrology.catchment_area": ("Area (m², ha, km²) and perimeter of a closed boundary; with a surface also the min / max / mean "
                                 "elevation and the lowest point inside.",
                                 {"boundary": ("string", "Closed polyline handle", True), "surface": ("string", ""),
                                  "grid": ("integer", "Sampling grid, default 60")}),
    "hydrology.low_point": ("Lowest and highest points of a surface, optionally inside a closed boundary (grid sampling).",
                            {"surface": ("string", "", True), "boundary": ("string", "Closed polyline handle"),
                             "grid": ("integer", "Sampling grid, default 80")}),
    "hydrology.flow_path": ("Draw the downhill flow path from a point on a surface (Civil 3D water drop, 3D polyline) and return "
                            "its length and fall.",
                            {"surface": ("string", "", True), "start": ("point", "", True), "layer": ("string", "")}),
}
