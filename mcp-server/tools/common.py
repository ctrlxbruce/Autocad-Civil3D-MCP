"""Shared parameter blocks."""

ENTITY = {
    "layer": ("string", "Layer name (created if missing)"),
    "color": ("string", "ACI number '1'-'255', a name like 'red', 'R,G,B', 'bylayer' or 'byblock'"),
    "linetype": ("string", "Linetype name, e.g. 'DASHED', 'CENTER', 'HIDDEN' (loaded automatically)"),
    "lineweight": ("number", "Lineweight in mm, e.g. 0.25, 0.35, 0.50"),
    "ltscale": ("number", "Linetype scale for this object"),
    "transparency": ("number", "Transparency percent 0-90"),
}


def ent(params: dict) -> dict:
    """Add the common entity properties to a parameter dict."""
    return {**params, **ENTITY}


HANDLES = {"handles": ("strings", "Object handles (from query_list or previous results)", True)}
