"""Pressure (water supply) networks."""

TOOLS = {
    "pressure.parts_lists": ("Pressure parts lists with their parts by type (pipes, elbows, tees, reducers, valves, hydrants ...). "
                             "The part descriptions are what pressure_add_pipe / add_fitting / add_appurtenance expect.", {}),
    "pressure.network_create": ("Create a pressure (water supply) network.",
                                {"name": ("string", ""), "parts_list": ("string", "Default: first pressure parts list"),
                                 "surface": ("string", "Reference surface (used for cover)"),
                                 "alignment": ("string", "Reference alignment")}),
    "pressure.add_pipe": ("Add a pressure pipe. With cover, the top of the pipe is placed that far below the surface at both ends.",
                          {"network": ("string", "", True),
                           "part": ("string", "Pipe part description from pressure_parts_lists", True),
                           "start": ("point", "", True), "end": ("point", "", True),
                           "cover": ("number", "Cover to the pipe top (m); needs surface or a network reference surface"),
                           "surface": ("string", "Surface for cover (default: network reference surface)"),
                           "name": ("string", "")}),
    "pressure.add_fitting": ("Add a fitting (elbow, tee, wye, cross, reducer, cap ...) at a location.",
                             {"network": ("string", "", True), "part": ("string", "Fitting part description", True),
                              "location": ("point", "", True), "name": ("string", "")}),
    "pressure.add_appurtenance": ("Add an appurtenance (valve, hydrant, pump) at a location.",
                                  {"network": ("string", "", True), "part": ("string", "Appurtenance part description", True),
                                   "location": ("point", "", True), "name": ("string", "")}),
    "pressure.check_cover": ("Check the cover over every pressure pipe (start, middle, end) against a surface.",
                             {"network": ("string", "", True), "surface": ("string", "Default: network reference surface"),
                              "min_cover": ("number", "Default 1.0 m"), "max_cover": ("number", "m")}),
}
