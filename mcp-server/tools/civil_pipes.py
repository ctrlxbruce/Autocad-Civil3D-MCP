TOOLS = {
    "pipes.parts_lists": ("Parts lists with families and sizes (needed for add_pipe/add_structure).", {}),
    "pipes.network_create": ("Create a gravity pipe network.",
                             {"name": ("string", ""), "parts_list": ("string", "Default first"), "surface": ("string", "Reference surface"),
                              "alignment": ("string", "Reference alignment"), "layer": ("string", "")}),
    "pipes.add_structure": ("Add a structure (manhole, inlet ...). If the network has a reference surface, the rim follows it. "
                            "Returns rim/sump elevations and any warnings.",
                            {"network": ("string", "", True), "family": ("string", "Family name from parts_lists", True),
                             "size": ("string", "Size name or index"), "location": ("point", "", True), "rotation": ("number", "Degrees"),
                             "rim": ("number", "Rim elevation (default: from the network's reference surface)"),
                             "sump": ("number", "Sump DEPTH below the lowest connected pipe invert (not an elevation)"),
                             "name": ("string", ""), "apply_rules": ("boolean", "")}),
    "pipes.add_pipe": ("Add a pipe, optionally connected to structures. For a real design give start_invert and slope: "
                       "slope is the grade from start to end and NEGATIVE means it falls (sewer: -0.01 = 1 % fall). "
                       "Without an invert the pipe stays at the points' z (usually 0) unless rules set it. "
                       "Returns start/end inverts, falls_from_start_to_end and any warnings.",
                       {"network": ("string", "", True), "family": ("string", "", True), "size": ("string", "Size name or index"),
                        "start": ("point", "", True), "end": ("point", "", True), "start_structure": ("string", ""),
                        "end_structure": ("string", ""),
                        "start_invert": ("number", "Invert (inside bottom of the pipe) elevation at the start"),
                        "end_invert": ("number", "Invert elevation at the end (ignored when slope is given)"),
                        "slope": ("number", "Grade from start to end; negative = falls, e.g. -0.01"),
                        "name": ("string", ""), "apply_rules": ("boolean", "")}),
    "pipes.connect": ("Connect a pipe end to a structure.", {"pipe": ("string", "", True), "structure": ("string", "", True),
                                                            "end": ("string", "Start | End")}),
    "pipes.network_info": ("All pipes and structures of a network.", {"network": ("string", "", True)}),
    "pipes.apply_rules": ("Apply design rules to pipes/structures.", {"parts": ("strings", "", True)}),
    "pipes.interference_check": ("Start Civil 3D's interference check (opens a dialog where the user picks the networks).", {}),
    "pressure.network_info": ("Inspect a pressure pipe network (pipes, fittings, appurtenances).", {"network": ("string", "", True)}),
}
