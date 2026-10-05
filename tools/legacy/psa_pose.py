"""Apply a High on Life animation (ActorX .psa exported by CUE4Parse) to the glTF-imported Kenny rig in Blender.

The .psa is in Unreal space (cm, its own bone orientations); the rig came through glTF (metres, Blender bone
orientations). Rather than convert bone axes, we take each bone's change from rest in *model* space,
  delta_UE = posed_component @ rest_component^-1,
map it into Blender space with an affine fit C (from the rest-pose joint positions of both rigs), and apply
  posed_blender = C @ delta_UE @ C^-1 @ rest_blender.
"""
import struct

import numpy as np
from mathutils import Matrix, Quaternion, Vector


def read_psa(path):
    data = open(path, "rb").read()
    off, chunks = 0, {}
    while off < len(data):
        cid = data[off:off + 20].split(b"\0")[0].decode()
        size, count = struct.unpack_from("<ii", data, off + 24)
        off += 32
        chunks[cid] = (off, size, count)
        off += size * count
    o, size, count = chunks["BONENAMES"]
    bones = []
    for i in range(count):
        b = o + i * size
        name = data[b:b + 64].split(b"\0")[0].decode()
        _flags, _nchild, parent = struct.unpack_from("<iii", data, b + 64)
        qx, qy, qz, qw, px, py, pz = struct.unpack_from("<7f", data, b + 76)
        bones.append((name, parent, (qx, qy, qz, qw), (px, py, pz)))
    o, size, count = chunks["ANIMINFO"]
    anims = []
    for i in range(count):
        b = o + i * size
        name = data[b:b + 64].split(b"\0")[0].decode()
        nb, = struct.unpack_from("<i", data, b + 128)
        rate, = struct.unpack_from("<f", data, b + 128 + 4 * 6)
        first, nframes = struct.unpack_from("<ii", data, b + 128 + 4 * 8)
        anims.append({"name": name, "bones": nb, "rate": rate, "first": first, "frames": nframes})
    o, size, count = chunks["ANIMKEYS"]
    keys = [struct.unpack_from("<3f4ff", data, o + i * size) for i in range(count)]
    return bones, anims, keys


def unmirror(key):
    """CUE4Parse writes the .psa keys mirrored along Y relative to the reference pose: flip them back"""
    px, py, pz, qx, qy, qz, qw = key[:7]
    return (qx, -qy, qz, qw), (px, -py, pz)


def local_matrix(q, p, conj):
    quat = Quaternion((q[3], q[0], q[1], q[2]))
    if conj:
        quat = quat.conjugated()
    return Matrix.Translation(Vector(p)) @ quat.to_matrix().to_4x4()


def component(bones, local_of, conj_mode):
    comp = []
    for i, (name, parent, q, p) in enumerate(bones):
        qq, pp = local_of(i)
        conj = conj_mode == "all" or (conj_mode == "nonroot" and i > 0)
        m = local_matrix(qq, pp, conj)
        comp.append(m if parent < 0 or i == 0 else comp[parent] @ m)
    return comp


def fit_affine(src, dst):
    """least-squares similarity (rotation or reflection + uniform scale + translation): dst ~ C @ src"""
    src, dst = np.asarray(src), np.asarray(dst)
    ms, md = src.mean(0), dst.mean(0)
    a, b = src - ms, dst - md
    u, s, vt = np.linalg.svd(b.T @ a)
    r = u @ vt  # may be a reflection; that is fine (UE is left-handed)
    scale = s.sum() / (a ** 2).sum()
    t = md - scale * r @ ms
    m = Matrix.Identity(4)
    for i in range(3):
        for j in range(3):
            m[i][j] = scale * r[i, j]
        m[i][3] = t[i]
    err = np.linalg.norm((scale * (r @ a.T)).T - b, axis=1)
    return m, float(err.mean()), float(err.max())


def pose_from_psa(arm, psa_path, frame=None, anim_index=0, anchor="gun_root_JNT"):
    """returns {bone name: posed armature-space 4x4} for the glTF rig, plus fit diagnostics"""
    bones, anims, keys = read_psa(psa_path)
    rest_world = {b.name: arm.matrix_world @ b.matrix_local for b in arm.data.bones}
    names = [b[0] for b in bones]
    shared = [i for i, n in enumerate(names) if n in rest_world]

    best = None
    for mode in ("nonroot", "none", "all"):
        comp = component(bones, lambda i: (bones[i][2], bones[i][3]), mode)
        c, mean, mx = fit_affine([comp[i].translation for i in shared], [rest_world[names[i]].translation for i in shared])
        if best is None or mean < best[2]:
            best = (mode, c, mean, mx, comp)
    mode, c, mean, mx, rest_comp = best

    an = anims[anim_index]
    if frame is None:
        frame = an["frames"] // 2
    base = (an["first"] + frame) * an["bones"]
    # the animation keys may use a different quaternion convention than the reference pose: pick the one whose
    # joints stay closest to rest (facial poses are small deviations)
    pose_comp, key_mode, drift = None, None, None
    for km in ("none", "nonroot", "all"):
        pc = component(bones, lambda i: unmirror(keys[base + i]), km)
        # first-person anims also move the whole gun in front of the camera: keep only the pose relative to
        # the anchor bone (his body), placed where the anchor sits at rest
        if anchor in names:
            g = names.index(anchor)
            fix = rest_comp[g] @ pc[g].inverted()
            pc = [fix @ m for m in pc]
        d = float(np.mean([(pc[i].translation - rest_comp[i].translation).length for i in shared]))
        if drift is None or d < drift:
            pose_comp, key_mode, drift = pc, km, d

    c_inv = c.inverted()
    posed = {}
    for i in shared:
        delta = pose_comp[i] @ rest_comp[i].inverted()
        posed[names[i]] = arm.matrix_world.inverted() @ (c @ delta @ c_inv) @ rest_world[names[i]]
    info = {"mode": mode, "key_mode": key_mode, "drift_cm": round(drift, 3), "fit_mean": mean, "fit_max": mx, "anim": an["name"], "frames": an["frames"], "frame": frame}
    return posed, info


def apply_pose(arm, posed):
    import bpy
    for b in arm.data.bones:  # parents before children
        if b.name in posed:
            arm.pose.bones[b.name].matrix = posed[b.name]
            bpy.context.view_layer.update()
