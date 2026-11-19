# Tool reference

**183 tools** in `full` mode. In Claude the dots become underscores (`drafting.line` → `drafting_line`).

Points are `[x, y]` or `[x, y, z]`. Angles are degrees, except in `props_set`, `props_call` and C# code, which use radians.

Mode column: **C** = loaded in `code` mode, **L** = loaded in `lean` mode, all tools load in `full` mode.
`search` mode lists a few of them plus `tools_search`, `tools_read` and `tools_call`, which reach every tool below by name.

## Code execution & skills

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `code_execute` | C | Run C# that CHANGES the drawing (transaction is committed). C# runs INSIDE Civil 3D with the full AutoCAD + Civil 3D .NET API (Roslyn). Globals: Document, CivilDoc, Database, Transaction (already open), Editor, Ctx (helpers: Ctx.Find("EG") → ObjectId by name/handle, Ctx.Add(entity) → adds to current space, Ctx.StyleId(category,name), Ctx.EnsureLayer(name), Ctx.ModelSpace()), Inputs (dict of your inputs), Print(x) for notes. Imported: System, System.Linq, Autodesk.AutoCAD.DatabaseServices/Geometry/ApplicationServices/EditorInput/Colors, Autodesk.Civil, Autodesk.Civil.ApplicationServices, Autodesk.Civil.DatabaseServices, Civil3DMcp (Conv.ToPoint3d, Conv.ToColor...). 'Entity' = AutoCAD Entity, 'Surface' = Civil Surface; use full names for styles (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle). Do NOT start/commit transactions yourself. End with `return <value>;` to get data back as JSON. Check skills_search first for a ready template. | **code**, inputs |
| `code_query` | C | Run C# READ-ONLY (transaction always rolled back, nothing changes). Use for questions/reports. C# runs INSIDE Civil 3D with the full AutoCAD + Civil 3D .NET API (Roslyn). Globals: Document, CivilDoc, Database, Transaction (already open), Editor, Ctx (helpers: Ctx.Find("EG") → ObjectId by name/handle, Ctx.Add(entity) → adds to current space, Ctx.StyleId(category,name), Ctx.EnsureLayer(name), Ctx.ModelSpace()), Inputs (dict of your inputs), Print(x) for notes. Imported: System, System.Linq, Autodesk.AutoCAD.DatabaseServices/Geometry/ApplicationServices/EditorInput/Colors, Autodesk.Civil, Autodesk.Civil.ApplicationServices, Autodesk.Civil.DatabaseServices, Civil3DMcp (Conv.ToPoint3d, Conv.ToColor...). 'Entity' = AutoCAD Entity, 'Surface' = Civil Surface; use full names for styles (Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle). Do NOT start/commit transactions yourself. End with `return <value>;` to get data back as JSON. Check skills_search first for a ready template. | **code**, inputs |
| `skills_list` | C | List the C# skill templates (by category: surfaces, points, alignments, drawing, geometry, pipes, workflows). | category |
| `skills_search` | C | Search skill templates by words, e.g. 'contour smoothing', 'import csv', 'volume'. | **query** |
| `skills_read` | C | Read a skill template (code + example inputs) to adapt and run with code_execute / code_query. | **name** |

## Core, query, properties, fallback

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `system_ping` | C | Check that Civil 3D and the plugin are running. |  |
| `system_list_tools` |  | List every tool name the plugin knows. |  |
| `doc_info` | C | Info about the open drawing: name, units, current layer/text/dim style, extents. |  |
| `doc_run_command` | C | FALLBACK for anything without a dedicated tool: send text to the Civil 3D command line exactly as typed. Use '-' command versions to avoid dialogs (e.g. '-LAYER', '-PLOT'). Separate inputs with spaces or \n. Civil 3D command names usually start with Aecc (e.g. '_AeccLandXMLIn', '_AeccSetNetworkCatalog'). Examples: 'FILLET R 2 ', '_TRIM', 'REVCLOUD', '_AeccLandXMLIn'. Blocked for safety: AutoLISP, and commands that start programs, load code (NETLOAD, SCRIPT, VBA) or quit. | **command** |
| `doc_get_sysvar` |  | Read a system variable (e.g. DIMSCALE, LTSCALE, PDMODE). | **name** |
| `doc_set_sysvar` |  | Set a system variable. Security variables (SECURELOAD, TRUSTEDPATHS...) can't be changed. | **name**, **value** |
| `doc_zoom_extents` |  | Zoom to extents. |  |
| `doc_summary` | L | Short, size-capped picture of a drawing (good first call on large drawings): object counts by type and layer, extents, a spatial grid of where objects are, Civil 3D object counts, xrefs and layouts. | top, grid |
| `doc_save` |  | Save the drawing (QSAVE) or Save As when path is given. | path |
| `doc_undo` | L | Undo Claude's own last changes in the active drawing (one step = one tool call that changed it; a zoom after it is undone with it). Refuses when the drawing was changed another way since (by the user, a command-line command, a layout or system-variable change), so it never undoes the user's work; then the user can press Ctrl+Z in Civil 3D. | steps |
| `query_list` | L | List objects in the current space with handle, type, layer and name. Filter by type (Line, Polyline, Circle, TinSurface, Alignment, CogoPoint, ...) and/or layer. | type, layer, limit |
| `query_count_by_type` | L | Count objects in the current space by type. |  |
| `props_get` | L | Read ALL properties of any object (entity, Civil object, style) by handle or Civil object name. Optional dotted path to read one value, e.g. 'ContourStyle.MajorContourInterval'. | **handle**, path |
| `props_set` | L | Set ANY properties on one or more objects. props example: {"Layer":"C-ROAD","Color":"1","Elevation":100,"StyleId":"<style handle>"}. Dotted paths allowed. ANGLES ARE IN RADIANS here. | handle, handles, **props** |
| `props_methods` | L | List the API methods available on an object (to use with props_call). | **handle** |
| `props_call` | L | Call ANY Civil 3D / AutoCAD API method on an object, e.g. method 'RaiseSurface' args [2.5]. Strings are converted to enums, handles to ObjectIds, [x,y,z] to points. | **handle**, **method**, args |
| `entity_delete` | L | Delete objects. | **handles** |
| `measure_distance` | L | Distance, 2D distance, angle and deltas between two points. | **p1**, **p2** |
| `measure_entity` | L | Length, area, start/end and extents of an object. | **handle** |

## Preview (dry run)

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `doc_preview` | L | Preview (dry run) a tool that changes the drawing, without changing anything: Civil 3D runs it, reports what it would add, change and delete, then rolls everything back. Use it before deleting or changing many objects, or when unsure what a tool or a C# script (code_execute) will do. Tools that save, plot, switch layouts or use the command line can't be previewed. | **name**, args |

## Drafting

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `drafting_line` | L | Draw a line. | **start**, **end**, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_polyline` | L | Draw a 2D polyline. Optional bulges per vertex (arc segments: bulge = tan(angle/4)), widths, closed. | **points**, closed, bulges, width, global_width, elevation, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_polyline3d` |  | Draw a 3D polyline. | **points**, closed, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_rectangle` |  | Draw a rectangle from two opposite corners. | **corner1**, **corner2**, rotation, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_polygon` |  | Draw a regular polygon. | **center**, **radius**, sides, mode, rotation, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_circle` | L | Draw a circle: center + radius/diameter, or through 3 points p1,p2,p3. | center, radius, diameter, p1, p2, p3, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_arc` |  | Draw an arc: center+radius+start/end angles (degrees, counter-clockwise), or 3 points p1,p2,p3. | center, radius, start_angle, end_angle, p1, p2, p3, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_ellipse` |  | Draw an ellipse or elliptical arc. | **center**, major_radius, minor_radius, rotation, major_axis, ratio, start_angle, end_angle, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_spline` |  | Draw a spline (SPLINE) through fit points, or from control vertices. | fit_points, control_points, degree, fit_tolerance, closed, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_point` |  | Place AutoCAD point(s). pdmode/pdsize set the point display style for the drawing. | location, points, pdmode, pdsize, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_hatch` |  | Hatch inside closed boundary objects. | **boundaries**, pattern, scale, angle, associative, background_color, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_xline` |  | Construction line through two points. | **base**, **through**, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_ray` |  | Ray from base towards a point. | **base**, **through**, layer, color, linetype, lineweight, ltscale, transparency |
| `drafting_donut` |  | Filled donut. | **center**, **inner_diameter**, **outer_diameter**, layer, color, linetype, lineweight, ltscale, transparency |

## Modify

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `modify_move` |  | Move objects by from→to. | **handles**, from, **to** |
| `modify_copy` |  | Copy objects by from→to. Returns new handles. | **handles**, from, **to** |
| `modify_rotate` |  | Rotate objects. | **handles**, **base**, **angle** |
| `modify_scale` |  | Scale objects. | **handles**, **base**, **factor** |
| `modify_mirror` |  | Mirror objects about the line p1-p2. | **handles**, **p1**, **p2**, erase_source |
| `modify_offset` |  | Offset a curve (line, polyline, arc, circle, spline). Sign of distance picks the side. | **handle**, **distance**, layer |
| `modify_explode` |  | Explode objects (blocks, polylines, dims ...). | **handles** |
| `modify_join` |  | Join lines/arcs/polylines onto a target. | **target**, **handles** |
| `modify_array_rect` |  | Rectangular array (copies). | **handles**, rows, columns, row_spacing, column_spacing |
| `modify_array_polar` |  | Polar array (copies). | **handles**, **center**, **count**, fill_angle |
| `modify_match_properties` |  | Copy layer/color/linetype/lineweight/transparency from source to objects. | **source**, **handles** |
| `modify_polyline_vertex` |  | Edit a polyline vertex. action: add \| move \| remove \| bulge \| width. | **handle**, **action**, **index**, point, bulge, start_width, end_width |

## Layers & linetypes

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `layers_list` | L | List all layers with color, linetype, lineweight, on/frozen/locked/plot. |  |
| `layers_set` | L | Create a layer or change it. | **name**, new_name, color, linetype, lineweight, on, frozen, locked, plot, description, transparency, make_current |
| `layers_set_current` |  | Make a layer current (creates it if missing). | **name** |
| `layers_delete` |  | Delete an unused layer. | **name** |
| `linetypes_load` |  | Load linetypes from acad.lin/acadiso.lin, optionally set global LTSCALE. | **names**, ltscale |
| `linetypes_list` |  | List loaded linetypes. |  |

## Text, dimensions, leaders, styles

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `annotation_text` | L | Single-line TEXT. | **text**, **position**, height, rotation, style, justify, width_factor, oblique, layer, color, linetype, lineweight, ltscale, transparency |
| `annotation_mtext` | L | Multiline MTEXT. Use \P for a new paragraph; MText codes like {\fArial\|b1;Bold} work. | **text**, **position**, height, width, rotation, style, justify, line_spacing, background_fill, background_scale, layer, color, linetype, lineweight, ltscale, transparency |
| `annotation_edit_text` |  | Change text/height/width of TEXT or MTEXT. | **handle**, text, height, width |
| `annotation_table` |  | Insert a table filled with data rows. | **position**, **data**, row_height, column_width, text_height, layer, color, linetype, lineweight, ltscale, transparency |
| `annotation_mleader` |  | Multileader (MLEADER) with text. One or more arrow points. | **text**, **text_point**, arrow_point, arrow_points, style, text_height, layer, color, linetype, lineweight, ltscale, transparency |
| `styles_text_style` |  | Create/update a text style. | **name**, font, big_font, height, width_factor, oblique, bold, italic, annotative, make_current |
| `styles_list_text_styles` |  | List text styles. |  |
| `dim_linear` |  | Linear dimension. rotation 0 = horizontal, 90 = vertical. | style, text_override, props, **p1**, **p2**, **dim_line_point**, rotation, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_aligned` | L | Aligned dimension (parallel to p1-p2). | style, text_override, props, **p1**, **p2**, **dim_line_point**, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_angular` |  | Angular dimension from a vertex and two points. | style, text_override, props, **vertex**, **p1**, **p2**, **arc_point**, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_angular_2lines` |  | Angular dimension between two lines. | style, text_override, props, **line1_start**, **line1_end**, **line2_start**, **line2_end**, **arc_point**, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_radius` |  | Radius dimension on a circle/arc (handle) or center+radius. | style, text_override, props, handle, center, radius, angle, leader_length, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_diameter` |  | Diameter dimension. | style, text_override, props, handle, center, radius, angle, leader_length, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_arc_length` |  | Arc length dimension. | style, text_override, props, **handle**, arc_point, offset, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_ordinate` |  | Ordinate (X or Y datum) dimension. | style, text_override, props, **axis**, **feature_point**, **leader_end**, layer, color, linetype, lineweight, ltscale, transparency |
| `dim_polyline_segments` |  | Aligned dimension on every straight segment of a polyline. | style, text_override, props, **handle**, offset, layer, color, linetype, lineweight, ltscale, transparency |
| `styles_dim_style` |  | Create/update a DIMENSION STYLE and make it current. settings keys (friendly or raw DIMxxx): arrow, arrow1, arrow2, leader_arrow (ClosedFilled, _ARCHTICK, _OBLIQUE, _DOT, _DOTSMALL, _OPEN, _OPEN30, _NONE, _INTEGRAL, _BOXFILLED), arrow_size, text_height, text_style, text_gap, text_vertical (0 centered,1 above), text_horizontal, ext_offset, ext_extend, dim_line_extend, baseline_spacing, decimals, angle_decimals, units (2 decimal,4 arch...), angle_units, overall_scale, linear_scale, suffix, round, center_mark, tolerance, tol_plus, tol_minus, suppress_zeros, line_color, ext_color, text_color, line_weight, ext_weight, linetype, fit, text_movement, suppress_ext1/2, fixed_ext_length, ext_length, decimal_separator, text_fill, text_fill_color — or ANY raw name like Dimtih, Dimtoh, Dimtix, Dimsoxd, Dimzin. | **name**, settings, make_current |
| `styles_list_dim_styles` |  | List dimension styles. |  |
| `styles_dim_style_get` |  | All settings of a dimension style. | **name** |
| `styles_mleader_style` |  | Create/update a multileader style. | **name**, arrow, arrow_size, text_height, text_style, landing_gap, dogleg_length, leader_color, text_color, leader_type, max_points, scale, props, make_current |

## Blocks & xrefs

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `blocks_list` |  | List block definitions with attribute tags. |  |
| `blocks_create` |  | Make a block definition from existing objects. | **name**, **handles**, base_point, erase_source |
| `blocks_insert` |  | Insert a block with optional attribute values {TAG: value}. | **name**, **position**, scale, rotation, attributes, layer, color, linetype, lineweight, ltscale, transparency |
| `blocks_set_attributes` |  | Change attribute values on an inserted block. | **handle**, **attributes** |
| `blocks_set_dynamic_property` |  | Set dynamic block properties (Distance1, Visibility1 ...). | **handle**, **props** |
| `blocks_import_dwg` |  | Load a DWG file as a block definition. | **path**, name |
| `blocks_attach_xref` |  | Attach an external reference DWG. | **path**, name, position, layer, color, linetype, lineweight, ltscale, transparency |

## Layouts & plotting

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `layouts_list` |  | List layouts (sheets). |  |
| `layouts_create` |  | Create a layout tab. | **name**, make_current |
| `layouts_set_current` |  | Switch to a layout or 'Model'. | **name** |
| `layouts_delete` |  | Delete a layout. | **name** |
| `layouts_viewport` |  | Add a viewport on a layout. | layout, **center**, **width**, **height**, view_center, scale, twist, lock, layer |
| `layouts_list_papers` |  | List paper sizes for a plot device. | device |
| `layouts_plot_pdf` |  | Plot the current layout (or model extents) to a PDF file. | **output_path**, device, paper, area, style_table |

## COGO points & point groups

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `points_create` |  | Create COGO points. points: [{x,y,z,desc,number}] or [[x,y,z],...]. | **points**, style, label_style, layer |
| `points_list` |  | List COGO points (number, name, E, N, Z, raw/full description). | group, limit |
| `points_get` |  | All properties of one COGO point by number. | **number** |
| `points_edit` |  | Change COGO point properties (RawDescription, Elevation, Easting, Northing, PointName, StyleId, LabelStyleId, Layer, ...). | handles, numbers, **props** |
| `points_delete` |  | Delete COGO points by number. | **numbers** |
| `points_list_file_formats` |  | List point file formats available for import (PNEZD, PENZD, ...). |  |
| `points_import` | L | Import COGO points from a CSV/TXT file, optionally into a new point group. Files that write numbers with a decimal comma (1234,56) and separate fields with ; tab or space are converted automatically. | **path**, format, add_to_group, decimal_comma |
| `points_elevations_from_surface` |  | Set COGO point elevations from a surface. | **surface**, numbers |
| `points_description_keys` |  | List description key sets and keys. |  |
| `point_groups_create` |  | Create/update a point group with a query. | **name**, include_numbers, include_raw_desc, include_full_desc, include_elevations, include_names, include_groups, exclude_numbers, exclude_raw_desc, exclude_full_desc, exclude_elevations, exclude_names, exclude_groups, point_style, label_style |
| `styles_point_style` |  | Create/update a POINT STYLE (marker, size, rotation, display). | **name**, marker, symbol, custom_marker, size_mode, size, rotation, props |

## Surfaces

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `surface_create` |  | Create an empty TIN or grid surface. | **name**, type, style, layer, description |
| `surface_import_tin_file` |  | Create a surface from a .tin file. | **path**, name, style |
| `surface_add_point_groups` |  | Add point groups to a surface definition. | **surface**, **groups** |
| `surface_add_points` |  | Add XYZ points directly to a TIN surface. | **surface**, **points** |
| `surface_add_breaklines` |  | Add breaklines from polylines/3D polylines/feature lines. | **surface**, **handles**, type, mid_ordinate, max_distance, weeding_distance, weeding_angle |
| `surface_add_contours` |  | Build/add surface data from existing contour polylines (with elevation). | **surface**, **handles**, mid_ordinate, max_distance, weeding_distance, weeding_angle |
| `surface_add_boundary` |  | Add a boundary from a closed polyline. | **surface**, **handles**, type, non_destructive, mid_ordinate |
| `surface_add_dem` |  | Add a DEM file (.tif/.dem/.asc) to a surface. | **surface**, **path** |
| `surface_edit` |  | Edit a surface. op: raise(amount) \| paste(source) \| add_point(point) \| delete_point(point) \| add_line(start,end) \| delete_line(point) \| swap_edge(point) \| smooth(method nni\|kriging, grid_spacing) \| simplify(method PointRemoval\|EdgeContraction, percent 0-1). | **surface**, **op**, amount, source, point, start, end, method, grid_spacing, percent, locations |
| `surface_info` | L | Surface statistics: points, triangles, min/max/mean elevation, area, slopes, style. | **surface** |
| `surface_elevation_at` |  | Surface elevation at point(s). | **surface**, point, points |
| `surface_extract_contours` |  | Extract contours/border to AutoCAD polylines. what: contours (interval) \| major \| minor \| border. | **surface**, what, interval, smoothing, smoothness, layer |
| `surface_style_contours` |  | Set contour settings on a SURFACE STYLE: intervals, base elevation, CONTOUR SMOOTHING, and show/hide components (MajorContour, MinorContour, Triangles, Points, Boundary, Elevations, Slopes, SlopeArrows, Watersheds, UserContours, Gridded). | **style**, major_interval, minor_interval, base_elevation, smooth, smoothing_type, smoothing_amount, depression_ticks, show |
| `surface_analysis` |  | Elevation or slope analysis: split the surface's range into equal bands, save them on the surface (visible when its style shows Elevations / Slopes) and return them. Slopes are fractions (0.05 = 5 %). watershed returns the existing watershed data. | **surface**, kind, ranges |
| `surface_volume` |  | Cut/fill between two surfaces (creates a TIN volume surface) or read an existing one. | base, comparison, name, volume_surface, cut_factor, fill_factor |

## Alignments & profiles

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `alignment_from_polyline` |  | Create an alignment from a polyline. | **polyline**, name, add_curves, erase_polyline, start_station, description, style, label_set, layer, site |
| `alignment_from_pis` |  | Create an alignment by PI points with curve radii and optional spirals (spiral-curve-spiral). | **pis**, name, radii, default_radius, spiral_in, spiral_out, start_station, description, style, label_set, layer, site |
| `alignment_offset` |  | Create an offset alignment. | **alignment**, **offset**, name, style |
| `alignment_info` |  | Alignment stations, length, every entity (lines/curves/spirals), profiles, equations, design speeds. | **alignment** |
| `alignment_station_offset` |  | Station and offset of a point. | **alignment**, **point** |
| `alignment_point_at` |  | XY at a station/offset. | **alignment**, **station**, offset |
| `alignment_add_station_equation` |  | Add a station equation. | **alignment**, **raw_station_back**, **station_ahead**, decreasing |
| `alignment_add_design_speed` |  | Add a design speed. | **alignment**, **station**, **speed**, comment |
| `alignment_set_labels` |  | Apply an alignment label set. | **alignment**, **label_set** |
| `profile_from_surface` |  | Existing-ground profile from a surface along an alignment. | **alignment**, **surface**, name, offset, style, label_set, layer |
| `profile_create_layout` |  | Design (layout) profile by PVIs: [[station, elevation, vertical_curve_length], ...]. | **alignment**, **pvis**, name, curve_type, style, label_set, layer |
| `profile_add_pvi` |  | Add a PVI (and optional vertical curve) to a layout profile. | **profile**, **station**, **elevation**, curve_length |
| `profile_info` |  | Profile summary and PVIs with grades. | **profile** |
| `profile_elevation_at` |  | Profile elevation at station(s). | **profile**, station, stations |
| `profile_view_create` |  | Create a profile view (draws all profiles of the alignment). | **alignment**, **position**, name, style, band_set, elevation_min, elevation_max |

## Assemblies, corridors, sections, grading, parcels

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `assembly_create` |  | Create an (empty) assembly baseline marker. Add subassemblies with assembly_add_subassembly. | **name**, **position**, type |
| `corridor_create` |  | Create a corridor from alignment + profile + assembly and build it. | name, **alignment**, **profile**, **assembly**, start_station, end_station, frequency, style, baseline_name, region_name |
| `corridor_rebuild` |  | Rebuild a corridor. | **corridor** |
| `corridor_add_surface` |  | Corridor surface from link codes (Top, Datum, Pave ...). | **corridor**, name, **link_codes**, feature_line_codes, style |
| `sections_sample_lines` |  | Create sample lines (stations list or interval) with swath widths and choose surfaces/corridors to sample. | **alignment**, group_name, stations, interval, start_station, end_station, left, right, sample |
| `sections_create_views` |  | Create cross-section views for all sample lines in a grid. | **group**, **position**, spacing_x, spacing_y, columns |
| `sections_sample_line_info` |  | Section data (offset/elevation points) at a sample line. | **sample_line** |
| `featureline_from_entity` |  | Feature line from a polyline/line/arc, optional elevations from surface. | **entity**, name, site, surface, insert_intermediate, style |
| `featureline_set_elevations` |  | Set feature line elevations from surface, raise, or per point. | **featureline**, surface, raise, point_elevations, insert_intermediate |
| `assembly_list_stock` |  | Common Civil 3D stock subassembly types by category (lanes, curbs, shoulders, daylight, ditches ...). | category |
| `assembly_add_subassembly` |  | Add a stock subassembly to an assembly, on the left, right or both sides, optionally hooked to a point of another subassembly (e.g. a curb on the lane edge). Returns the created names. | **assembly**, **type**, side, params, hook_to, hook_point_code, name |
| `assembly_subassembly_params` |  | Parameter names, types and values of a subassembly, plus its point codes (for hooking). | **subassembly** |
| `assembly_info` |  | An assembly's subassemblies with side and what each is hooked to. | **assembly** |
| `site_create` |  | Create a site. | **name** |
| `parcels_list` |  | List parcels in a site with area and perimeter. To CREATE parcels from polylines use doc_run_command "_CreateParcelFromObjects" (Civil 3D only offers this in the UI). | **site** |

## Pipe networks

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `pipes_parts_lists` |  | Parts lists with families and sizes (needed for add_pipe/add_structure). |  |
| `pipes_network_create` |  | Create a gravity pipe network. | name, parts_list, surface, alignment, layer |
| `pipes_add_structure` |  | Add a structure (manhole, inlet ...). If the network has a reference surface, the rim follows it. Returns rim/sump elevations and any warnings. | **network**, **family**, size, **location**, rotation, rim, sump, name, apply_rules |
| `pipes_add_pipe` |  | Add a pipe, optionally connected to structures. For a real design give start_invert and slope: slope is the grade from start to end and NEGATIVE means it falls (sewer: -0.01 = 1 % fall). Without an invert the pipe stays at the points' z (usually 0) unless rules set it. Returns start/end inverts, falls_from_start_to_end and any warnings. | **network**, **family**, size, **start**, **end**, start_structure, end_structure, start_invert, end_invert, slope, name, apply_rules |
| `pipes_connect` |  | Connect a pipe end to a structure. | **pipe**, **structure**, end |
| `pipes_network_info` |  | All pipes and structures of a network. | **network** |
| `pipes_apply_rules` |  | Apply design rules to pipes/structures. | **parts** |
| `pipes_interference_check` |  | Start Civil 3D's interference check (opens a dialog where the user picks the networks). |  |
| `pressure_network_info` |  | Inspect a pressure pipe network (pipes, fittings, appurtenances). | **network** |

## Pressure (water supply) networks

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `pressure_parts_lists` |  | Pressure parts lists with their parts by type (pipes, elbows, tees, reducers, valves, hydrants ...). The part descriptions are what pressure_add_pipe / add_fitting / add_appurtenance expect. |  |
| `pressure_network_create` |  | Create a pressure (water supply) network. | name, parts_list, surface, alignment |
| `pressure_add_pipe` |  | Add a pressure pipe. With cover, the top of the pipe is placed that far below the surface at both ends. | **network**, **part**, **start**, **end**, cover, surface, name |
| `pressure_add_fitting` |  | Add a fitting (elbow, tee, wye, cross, reducer, cap ...) at a location. | **network**, **part**, **location**, name |
| `pressure_add_appurtenance` |  | Add an appurtenance (valve, hydrant, pump) at a location. | **network**, **part**, **location**, name |
| `pressure_check_cover` |  | Check the cover over every pressure pipe (start, middle, end) against a surface. | **network**, surface, min_cover, max_cover |

## Pipe hydraulics, network QC and hydrology

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `pipes_hydraulics` |  | Gravity network hydraulics with Manning's equation: full capacity and velocity of every pipe; with design flows (L/s per pipe name) also the depth ratio d/D, velocity and a simplified HGL from the outlet. Flags pipes outside the velocity and depth criteria. | **network**, flows, n, min_velocity, max_velocity, max_depth_ratio, outlet_hgl |
| `pipes_qc` |  | Quality check of a gravity network: slopes, cover at each pipe end, unconnected pipe ends, sumps above pipe inverts, and rims that differ from the ground. | **network**, surface, min_cover, max_cover, min_slope, max_slope, rim_tolerance |
| `hydrology_rational` |  | Peak runoff by the Rational method, Q = C i A / 360 (Q m³/s, i mm/h, A ha). Area from area_ha or a closed boundary polyline; several areas give a weighted C. | **intensity**, c, area_ha, boundary, areas |
| `hydrology_time_of_concentration` |  | Time of concentration in minutes by Kirpich, California Culvert Practice (used in IRC:SP:13) and FAA. Give length + fall (or slope), or a flow path handle. | length, fall, slope, path, method, c |
| `hydrology_catchment_area` |  | Area (m², ha, km²) and perimeter of a closed boundary; with a surface also the min / max / mean elevation and the lowest point inside. | **boundary**, surface, grid |
| `hydrology_low_point` |  | Lowest and highest points of a surface, optionally inside a closed boundary (grid sampling). | **surface**, boundary, grid |
| `hydrology_flow_path` |  | Draw the downhill flow path from a point on a surface (Civil 3D water drop, 3D polyline) and return its length and fall. | **surface**, **start**, layer |

## Quantities, cost estimate and road QC

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `qty_pipe_lengths` |  | Pipe lengths by size plus structure, fitting and valve counts, for gravity and pressure networks. | networks |
| `qty_earthwork` |  | Cut, fill and net volumes of every volume surface (create one with surface_volume). |  |
| `qty_cost_estimate` |  | Bill of quantities with amounts: manual items and/or rates per part name applied to the pipe networks. Optional CSV export. | items, rates, networks, from_networks, currency, csv |
| `qc_stopping_distance` |  | Stopping sight distance and the minimum crest / sag K for a design speed. | **design_speed**, grade, standard, reaction_time |
| `qc_profile_k_values` |  | Check every vertical curve of a profile: K = L / A against the minimum for the design speed. | **profile**, **design_speed**, standard, reaction_time |
| `qc_alignment` |  | Check every horizontal curve radius of an alignment against R min = V² / (127 (e + f)). | **alignment**, **design_speed**, standard, max_superelevation, side_friction |

## Civil objects & styles

| Tool | Mode | What it does | Inputs (**bold** = required) |
|---|---|---|---|
| `civil_list` | L | List Civil objects. kind: surfaces \| alignments \| profiles \| sites \| pipe_networks \| pressure_networks \| point_groups \| corridors \| assemblies \| subassemblies \| feature_lines \| profile_views \| section_views \| sample_line_groups \| parcels. | **kind** |
| `civil_drawing_settings` |  | Read or set Civil drawing settings (units/zone ...). | props |
| `styles_categories` | L | Discover style collection names (optionally below a path like 'LabelStyles'). | path |
| `styles_list` | L | List style names in a collection. | **category** |
| `styles_create` |  | Create a style (or get existing) and set properties. | **category**, **name**, props |
| `styles_get` | L | All properties of a style (or one dotted path). | **category**, **name**, path |
| `styles_set` | L | Set style properties (dotted paths allowed). | **category**, **name**, **props** |
| `styles_display` |  | Show/hide/colour style display components in plan/model/profile/section view. | **category**, **name**, **components**, view, visible, color, layer, linetype, lineweight |
