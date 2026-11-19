"""Quantities, cost estimate and road geometry checks."""

TOOLS = {
    "qty.pipe_lengths": ("Pipe lengths by size plus structure, fitting and valve counts, for gravity and pressure networks.",
                         {"networks": ("strings", "Network names (default: all)")}),
    "qty.earthwork": ("Cut, fill and net volumes of every volume surface (create one with surface_volume).", {}),
    "qty.cost_estimate": ("Bill of quantities with amounts: manual items and/or rates per part name applied to the pipe networks. "
                          "Optional CSV export.",
                          {"items": ("array", "[{item, quantity, unit, rate}]"),
                           "rates": ("object", "Rate per part name, e.g. {\"200 mm PVC Pipe\": 850}; priced from the networks"),
                           "networks": ("strings", "Networks to price (default: all)"),
                           "from_networks": ("boolean", "Price the networks with rates (default true)"),
                           "currency": ("string", "Label only, e.g. INR"),
                           "csv": ("string", "Path of a .csv file to write")}),
    "qc.stopping_distance": ("Stopping sight distance and the minimum crest / sag K for a design speed.",
                             {"design_speed": ("number", "km/h", True), "grade": ("number", "%, + uphill"),
                              "standard": ("string", "aashto (default) | irc"), "reaction_time": ("number", "s, default 2.5")}),
    "qc.profile_k_values": ("Check every vertical curve of a profile: K = L / A against the minimum for the design speed.",
                            {"profile": ("string", "", True), "design_speed": ("number", "km/h", True),
                             "standard": ("string", "aashto (default) | irc"), "reaction_time": ("number", "s, default 2.5")}),
    "qc.alignment": ("Check every horizontal curve radius of an alignment against R min = V² / (127 (e + f)).",
                     {"alignment": ("string", "", True), "design_speed": ("number", "km/h", True),
                      "standard": ("string", "aashto (default) | irc"),
                      "max_superelevation": ("number", "Default 0.07"),
                      "side_friction": ("number", "Default from the standard")}),
}
