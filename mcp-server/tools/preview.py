# doc.preview is handled by server.py: it checks the target tool and asks the plugin for a dry run.
TOOLS: dict = {}
LOCAL_TOOLS = {
    "doc.preview": (
        "Preview (dry run) a tool that changes the drawing, without changing anything: Civil 3D runs it, reports "
        "what it would add, change and delete, then rolls everything back. Use it before deleting or changing many "
        "objects, or when unsure what a tool or a C# script (code_execute) will do. Tools that save, plot, switch "
        "layouts or use the command line can't be previewed.",
        {"name": ("string", "Tool to preview, e.g. 'entity_delete'", True),
         "args": ("object_or_json", "That tool's parameters, exactly as you would pass them")}),
}
