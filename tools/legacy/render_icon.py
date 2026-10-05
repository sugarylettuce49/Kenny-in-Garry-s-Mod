"""Blender: render Kenny's spawn-menu icon (512x512, transparent) from the extracted glTF + textures.

blender -b --python render_icon.py -- <in.glb> <textures dir> <eye png or ''> <out.png>
"""
import math
import sys

import bpy
from mathutils import Vector

GLB, TEX, EYE, OUT = sys.argv[sys.argv.index("--") + 1:]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
for o in list(bpy.data.objects):
    if o.type == "MESH" and o.find_armature() is None:
        bpy.data.objects.remove(o)
mesh = next(o for o in bpy.data.objects if o.type == "MESH")


def textured(name, color_png, normal_png=None, emit_png=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(color_png)
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.45
    if normal_png:
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images.load(normal_png)
        n.image.colorspace_settings.name = "Non-Color"
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(n.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    if emit_png:
        e = nt.nodes.new("ShaderNodeTexImage")
        e.image = bpy.data.images.load(emit_png)
        nt.links.new(e.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 3
    return m


mats = {
    "Kenny_MI": textured("body", f"{TEX}/Kenny_Body_D.png", f"{TEX}/Kenny_Body_N.png", f"{TEX}/Kenny_Body_E.png"),
    "Kenny_Eyelids_MI": textured("lids", f"{TEX}/Kenny_Eyelids_D.png", f"{TEX}/Kenny_Eyelids_N.png"),
}
if EYE:
    mats["Kenny_Eyeball_MI"] = textured("eye", EYE)
for i, slot in enumerate(mesh.material_slots):
    if slot.material and slot.material.name in mats:
        mesh.material_slots[i].material = mats[slot.material.name]

# frame the gun: face-on three-quarter view from behind-left (Kenny's face is at the back of the gun)
bpy.context.view_layer.update()
pts = [mesh.matrix_world @ Vector(c) for c in mesh.bound_box]
center = sum(pts, Vector()) / 8
size = max((max(p[i] for p in pts) - min(p[i] for p in pts)) for i in range(3))

cam_data = bpy.data.cameras.new("cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = size * 1.25
cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
d = Vector((-1.0, -1.1, 0.45)).normalized()
cam.location = center + d * size * 4
cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
bpy.context.scene.camera = cam

for loc, energy in (((-1, -2, 2), 400), ((2, 1, 2), 250), ((0, 2, -1), 120)):
    ld = bpy.data.lights.new("l", "AREA")
    ld.energy = energy * size * size * 4
    ld.size = size * 2
    lo = bpy.data.objects.new("l", ld)
    lo.location = center + Vector(loc) * size * 2
    lo.rotation_euler = (center - lo.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(lo)

sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
sc.render.resolution_x = sc.render.resolution_y = 512
sc.render.film_transparent = True
sc.view_settings.view_transform = "Standard"
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
print("icon ->", OUT)
