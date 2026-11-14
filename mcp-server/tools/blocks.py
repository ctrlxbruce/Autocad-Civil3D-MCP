from .common import ent

TOOLS = {
    "blocks.list": ("List block definitions with attribute tags.", {}),
    "blocks.create": ("Make a block definition from existing objects.",
                      {"name": ("string", "Block name", True), "handles": ("strings", "Objects", True),
                       "base_point": ("point", "Base point"), "erase_source": ("boolean", "Erase originals (default true)")}),
    "blocks.insert": ("Insert a block with optional attribute values {TAG: value}.",
                      ent({"name": ("string", "Block", True), "position": ("point", "Insertion point", True),
                           "scale": ("number", "Uniform scale"), "rotation": ("number", "Degrees"),
                           "attributes": ("object", "Attribute values by tag")})),
    "blocks.set_attributes": ("Change attribute values on an inserted block.",
                              {"handle": ("string", "Block reference", True), "attributes": ("object", "{TAG: value}", True)}),
    "blocks.set_dynamic_property": ("Set dynamic block properties (Distance1, Visibility1 ...).",
                                    {"handle": ("string", "Block reference", True), "props": ("object", "{name: value}", True)}),
    "blocks.import_dwg": ("Load a DWG file as a block definition.", {"path": ("string", "DWG path", True), "name": ("string", "Block name")}),
    "blocks.attach_xref": ("Attach an external reference DWG.", ent({"path": ("string", "DWG path", True), "name": ("string", "Name"),
                                                                    "position": ("point", "Insertion point")})),
}
