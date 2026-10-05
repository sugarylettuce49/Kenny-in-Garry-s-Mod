"""Blender (5.x) script: Kenny glTF (exported by CUE4Parse) -> Source SMD reference + physics hull.

blender -b --python glb_to_smd.py -- <in.glb> <out_dir> [name=emotion.psa ...] [--face-lua <file>]

Each name=psa pair becomes a flex (Source shape key) in kenny.vta: his real High on Life facial expression,
baked from the middle frame of that animation (tools/psa_pose.py).

- Source caps a model at 128 bones, Kenny has 229: lip / eye-orbit / eyelid sub-joints are folded
  into a kept ancestor (lower lip -> jaw, so opening the jaw opens the mouth).
- Units: glTF metres -> Source inches. Axes kept: +X = barrel (forward), +Z = up.
"""
import re
import sys

import bmesh
import bpy
from mathutils import Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
GLB, OUT = argv[0], argv[1]
FACE_LUA = argv[argv.index("--face-lua") + 1] if "--face-lua" in argv else None
FLEXES = [a.split("=", 1) for a in argv[2:] if "=" in a]
# eyelids and eyeballs rotate around the eye: a flex (straight-line vertex motion) would cut through the eyeball
# while blending, so they stay out of the flexes and are driven as bone rotations from Lua instead
FACE_BONES = ["sep_upper_eyelid_l_JNT", "sep_upper_eyelid_r_JNT", "sep_lower_eyelid_l_JNT", "sep_lower_eyelid_r_JNT",
              "gun_eye_l_JNT", "gun_eye_r_JNT"]
M2IN = 1.0 / 0.0254

MATERIALS = {
    "Kenny_MI": "kenny_body",
    "Kenny_Eyeball_MI": "kenny_eye",
    "Kenny_Eyelids_MI": "kenny_eyelids",
    "Kenny_InhibitorChip": "kenny_chip",
}
DROP = re.compile(r"(Lip_\d+|eyeOrbit_\d+_[lr]|eyelid_\d+_[lr])_JNT$")
# mouth corners stay separate so Lua can pull them into his idle smirk
KEEP = {"upperLip_01_JNT", "upperLip_03_JNT", "upperLip_43_JNT", "upperLip_45_JNT",
        "lowerLip_03_JNT", "lowerLip_05_JNT", "lowerLip_33_JNT", "lowerLip_35_JNT"}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
mesh_obj = next(o for o in bpy.data.objects if o.type == "MESH" and o.find_armature() == arm)

bones = list(arm.data.bones)  # parents come before children
# the first-person rig also carries the player's arms (clavicle_* chains); Kenny's skin never uses them


def is_arm(b):
    while b:
        if b.name.startswith("clavicle_"):
            return True
        b = b.parent
    return False


kept = [b for b in bones if (b.name in KEEP or not DROP.search(b.name)) and not is_arm(b)]
kept_names = {b.name for b in kept}


def target(bone):
    if bone.name in kept_names:
        return bone.name
    if bone.name.startswith("lowerLip_"):
        return "gun_jaw_JNT"
    while bone.name not in kept_names:
        bone = bone.parent
    return bone.name


remap = {b.name: target(b) for b in bones}
index = {b.name: i for i, b in enumerate(kept)}
print(f"bones: {len(bones)} -> {len(kept)}")
assert len(kept) <= 128, "still too many bones for Source"

arm_world = arm.matrix_world
to_src = Matrix.Scale(M2IN, 4)


def bone_matrix(b):
    # parent-relative transform in metres, then only the translation goes to inches.
    # (Scaling both matrices first cancels the scale out of the relative offset -> a 39x too small skeleton.)
    m = arm_world @ b.matrix_local
    if b.parent:
        m = (arm_world @ b.parent.matrix_local).inverted() @ m
    m = m.copy()
    m.translation = m.translation * M2IN
    return m


def fmt(v):
    return f"{v:.6f}"


def write_skeleton(f):
    f.write("version 1\nnodes\n")
    for i, b in enumerate(kept):
        parent = index[b.parent.name] if b.parent else -1
        f.write(f'{i} "{b.name}" {parent}\n')
    f.write("end\nskeleton\ntime 0\n")
    for i, b in enumerate(kept):
        m = bone_matrix(b)
        p, r = m.to_translation(), m.to_euler("XYZ")
        f.write(f"{i} {fmt(p.x)} {fmt(p.y)} {fmt(p.z)} {fmt(r.x)} {fmt(r.y)} {fmt(r.z)}\n")
    f.write("end\n")


# evaluated, triangulated copy of the mesh in Source space
dg = bpy.context.evaluated_depsgraph_get()
mesh_obj.data.shape_keys and setattr(mesh_obj, "show_only_shape_key", False)
for mod in mesh_obj.modifiers:
    mod.show_viewport = False  # bind pose, not the armature-deformed pose
me = bpy.data.meshes.new_from_object(mesh_obj.evaluated_get(dg))  # own copy: later to_mesh() calls free theirs
bm = bmesh.new()
bm.from_mesh(me)
bmesh.ops.triangulate(bm, faces=bm.faces)
bm.to_mesh(me)
bm.free()
me.transform(to_src @ mesh_obj.matrix_world)
me.calc_loop_triangles() if hasattr(me, "calc_loop_triangles") else None

uv = me.uv_layers.active.data
groups = {g.index: g.name for g in mesh_obj.vertex_groups}


def links(v):
    acc = {}
    for g in v.groups:
        name = groups.get(g.group)
        if name in remap and g.weight > 0:
            k = index[remap[name]]
            acc[k] = acc.get(k, 0.0) + g.weight
    top = sorted(acc.items(), key=lambda kv: -kv[1])[:3]
    total = sum(w for _, w in top) or 1.0
    return [(k, w / total) for k, w in top] or [(0, 1.0)]


vlinks = [links(v) for v in me.vertices]
mats = [MATERIALS.get(m.name if m else "", "kenny_body") for m in me.materials]
normals = me.corner_normals

with open(f"{OUT}/kenny_ref.smd", "w") as f:
    write_skeleton(f)
    f.write("triangles\n")
    for poly in me.polygons:
        f.write(mats[poly.material_index] + "\n")
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            co, n, t = me.vertices[vi].co, normals[li].vector, uv[li].uv
            lk = vlinks[vi]
            f.write(f"0 {fmt(co.x)} {fmt(co.y)} {fmt(co.z)} {fmt(n.x)} {fmt(n.y)} {fmt(n.z)} "
                    f"{fmt(t.x)} {fmt(t.y)} {len(lk)} " + " ".join(f"{k} {fmt(w)}" for k, w in lk) + "\n")
    f.write("end\n")

# physics: one convex box around the whole gun, skinned to the root
xs = [v.co.x for v in me.vertices]; ys = [v.co.y for v in me.vertices]; zs = [v.co.z for v in me.vertices]
lo, hi = (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))
print("bounds (in):", lo, hi)
corners = [(x, y, z) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
quads = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
with open(f"{OUT}/kenny_phys.smd", "w") as f:
    f.write('version 1\nnodes\n0 "root_JNT" -1\nend\nskeleton\ntime 0\n0 0 0 0 0 0 0\nend\ntriangles\n')
    for q in quads:
        for tri in ((q[0], q[1], q[2]), (q[0], q[2], q[3])):
            f.write("phys\n")
            for c in tri:
                x, y, z = corners[c]
                f.write(f"0 {fmt(x)} {fmt(y)} {fmt(z)} 0 0 1 0 0 1 0 1\n")
    f.write("end\n")

# flexes: per loop (same order as the SMD triangles), the vertex position in each expression
if FLEXES:
    sys.path.insert(0, __import__("os").path.dirname(__file__))
    import psa_pose

    import math
    face_angles = {}
    loop_verts = [me.loops[li].vertex_index for poly in me.polygons for li in poly.loop_indices]
    src_m = to_src @ mesh_obj.matrix_world
    nrm_m = src_m.to_3x3().inverted().transposed()

    def under_any(b, names):
        while b:
            if b.name in names:
                return True
            b = b.parent
        return False

    def under_face(b):
        while b:
            if b.name == "gun_face_master_JNT":
                return True
            b = b.parent
        return False

    def posed_vertices():
        for mod in mesh_obj.modifiers:
            mod.show_viewport = True
        bpy.context.view_layer.update()
        ev = mesh_obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
        out = [(src_m @ v.co, (nrm_m @ v.normal).normalized()) for v in ev.vertices]
        return out

    with open(f"{OUT}/kenny.vta", "w") as f:
        f.write('version 1\nnodes\n0 "root_JNT" -1\nend\nskeleton\n')
        for t in range(len(FLEXES) + 2):
            f.write(f"time {t}\n0 0 0 0 0 0 0\n")
        f.write("end\nvertexanimation\ntime 0\n")
        base = []
        for i, vi in enumerate(loop_verts):
            co = me.vertices[vi].co
            n = me.vertices[vi].normal
            base.append(co.copy())
            f.write(f"{i} {fmt(co.x)} {fmt(co.y)} {fmt(co.z)} {fmt(n.x)} {fmt(n.y)} {fmt(n.z)}\n")
        for t, (name, psa) in enumerate(FLEXES, 1):
            for pb in arm.pose.bones:
                pb.matrix_basis.identity()
            posed, info = psa_pose.pose_from_psa(arm, psa)
            # face only: the emotion anims also sway his body/barrel, which the weapon code already animates
            posed = {n: m for n, m in posed.items() if under_face(arm.data.bones[n])}
            psa_pose.apply_pose(arm, posed)
            face_angles[name] = {}
            for bn in FACE_BONES:
                e = arm.pose.bones[bn].matrix_basis.to_euler("XYZ")
                # Source Angle(pitch, yaw, roll) = rotation about local Y, Z, X (R = Rz Ry Rx, same as Blender XYZ)
                face_angles[name][bn] = (math.degrees(e.y), math.degrees(e.z), math.degrees(e.x))
            for pb in arm.pose.bones:
                pb.matrix_basis.identity()
            psa_pose.apply_pose(arm, {n: m for n, m in posed.items() if not under_any(arm.data.bones[n], FACE_BONES)})
            pv = posed_vertices()
            f.write(f"time {t}\n")
            moved = 0
            for i, vi in enumerate(loop_verts):
                co, n = pv[vi]
                if (co - base[i]).length > 0.003:
                    moved += 1
                    f.write(f"{i} {fmt(co.x)} {fmt(co.y)} {fmt(co.z)} {fmt(n.x)} {fmt(n.y)} {fmt(n.z)}\n")
            print(f"flex {name}: {moved} loops moved ({info['anim']} frame {info['frame']})")
            if name == "neutral":
                neutral_pv = pv
        t = len(FLEXES) + 1
        # "lipsclosed": the rig's own shape key that seals his lips
        kb = mesh_obj.data.shape_keys and mesh_obj.data.shape_keys.key_blocks.get("lipsclosed_BS")
        f.write(f"time {t}\n")
        n_lips = 0
        if kb:
            for i, vi in enumerate(loop_verts):
                co = src_m @ kb.data[vi].co
                if (co - base[i]).length > 0.003:
                    n = me.vertices[vi].normal
                    n_lips += 1
                    f.write(f"{i} {fmt(co.x)} {fmt(co.y)} {fmt(co.z)} {fmt(n.x)} {fmt(n.y)} {fmt(n.z)}\n")
        print(f"flex lipsclosed: {n_lips} loops")
        f.write("end\n")
    if FACE_LUA:
        with open(FACE_LUA, "w") as lf:
            lf.write("-- generated by tools/glb_to_smd.py: per expression, eyelid / eyeball bone rotations\n")
            lf.write("-- (bone-local Angle(pitch, yaw, roll)) taken from Kenny's High on Life emotion animations\n")
            lf.write("KENNY_FACE = {\n")
            for name, bonesd in face_angles.items():
                lf.write(f"\t{name} = {{\n")
                for bn, (p_, y_, r_) in bonesd.items():
                    lf.write(f"\t\t{bn} = Angle({p_:.2f}, {y_:.2f}, {r_:.2f}),\n")
                lf.write("\t},\n")
            lf.write("}\n")
        print("wrote", FACE_LUA)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()

with open(f"{OUT}/bones.txt", "w") as f:
    f.write("\n".join(b.name for b in kept))
print("wrote", OUT)
