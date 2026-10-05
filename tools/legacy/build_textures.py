"""Kenny's UE textures (PNG from CUE4Parse) -> Source VTF + VMT.

python build_textures.py <folder of extracted PNGs> <addon dir>

UE packs ORM (R=AO, G=roughness, B=metal). Source wants a phong mask in the normal map's alpha,
so it gets (1 - roughness) * AO. UE and Source both use DirectX (green-down) normals, so no flip.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from srctools.vtf import VTF, ImageFormats, VTFFlags

SRC = Path(sys.argv[1])
OUT = Path(sys.argv[2]) / "materials" / "models" / "weapons" / "kenny"
OUT.mkdir(parents=True, exist_ok=True)
KENNY = SRC / "Oregon/Content/Art/Characters/Kenny"
EYES = SRC / "Oregon/Content/Art/Characters/Eyes"
SIZE = 2048


def load(path, size=SIZE):
    return np.asarray(Image.open(path).convert("RGBA").resize((size, size), Image.LANCZOS)).astype(np.float32) / 255


def save_vtf(name, rgba, fmt, flags=VTFFlags.EMPTY):
    img = Image.fromarray((np.clip(rgba, 0, 1) * 255).astype(np.uint8), "RGBA")
    vtf = VTF(img.width, img.height, version=(7, 2), fmt=fmt, flags=flags)
    vtf.get().copy_from(img.tobytes(), ImageFormats.RGBA8888)
    vtf.compute_mipmaps()
    with open(OUT / f"{name}.vtf", "wb") as f:
        vtf.save(f)
    print("vtf", name, img.size, fmt.name)


def pbr_set(name, d_path, n_path, orm_path, e_path=None, size=SIZE):
    d = load(d_path, size)
    n = load(n_path, size)
    orm = load(orm_path, size)
    d[..., 3] = 1
    n[..., 3] = (1 - orm[..., 1]) * orm[..., 0]  # phong mask
    save_vtf(f"{name}", d, ImageFormats.DXT1)
    save_vtf(f"{name}_n", n, ImageFormats.DXT5, VTFFlags.NORMAL)
    vmt = [
        '"VertexLitGeneric"', "{",
        f'\t"$basetexture" "models/weapons/kenny/{name}"',
        f'\t"$bumpmap" "models/weapons/kenny/{name}_n"',
        '\t"$phong" "1"', '\t"$phongexponent" "24"', '\t"$phongboost" "2"',
        '\t"$phongfresnelranges" "[0.3 0.8 1]"',
        '\t"$rimlight" "1"', '\t"$rimlightexponent" "6"', '\t"$rimlightboost" "0.6"',
    ]
    if e_path:
        e = load(e_path, size)
        lum = e[..., :3].max(axis=2)
        mask = np.dstack([lum, lum, lum, np.ones_like(lum)])
        save_vtf(f"{name}_e", mask, ImageFormats.DXT1)
        vmt += ['\t"$selfillum" "1"', f'\t"$selfillummask" "models/weapons/kenny/{name}_e"',
                '\t"$selfillumtint" "[2 2 2]"']
    vmt.append("}")
    (OUT / f"{name}.vmt").write_text("\n".join(vmt) + "\n")


pbr_set("kenny_body", KENNY / "Textures/Kenny_Body_D.png", KENNY / "Textures/Kenny_Body_N.png",
        KENNY / "Textures/Kenny_Body_ORM.png", KENNY / "Textures/Kenny_Body_E.png")
pbr_set("kenny_eyelids", KENNY / "Textures/Kenny_Eyelids_D.png", KENNY / "Textures/Kenny_Eyelids_N.png",
        KENNY / "Textures/Kenny_Eyelids_ORM.png", size=1024)
pbr_set("kenny_chip", KENNY / "InhibitorChip/Kenny_InhibitorChip_D.png",
        KENNY / "InhibitorChip/Kenny_InhibitorChip_N.png", KENNY / "Textures/Kenny_Body_ORM.png",
        KENNY / "InhibitorChip/Kenny_InhibitorChip_E.png", size=1024)

# Eyeball: UE builds it in a shader. The iris/pupil mask (Eye_Mask_A: R=veins/iris ring, G=sclera, B=pupil)
# and Iris_D are sampled around UV (0.5, 0.5) and shrunk by the material's IrisScale (0.19) / PupilScale (0.32).
IRIS_SCALE, PUPIL_SCALE = 0.19, 0.32
N = 1024
mask = load(EYES / "Eye_Mask_A.png", N)
iris = load(EYES / "Iris_D.png", N)
yy, xx = np.mgrid[0:N, 0:N] / N - 0.5
r = np.sqrt(xx ** 2 + yy ** 2)


def sample(img, scale):  # img stretched so its full width covers `scale` of the eyeball UVs
    u = np.clip(((xx / scale) + 0.5) * N, 0, N - 1).astype(int)
    v = np.clip(((yy / scale) + 0.5) * N, 0, N - 1).astype(int)
    return img[v, u]


iris_r = IRIS_SCALE * 0.5
pupil_r = iris_r * PUPIL_SCALE * 1.6
sclera = np.array([0.95, 0.93, 0.86])
veins = sample(mask, 1.0)[..., 0:1] * (r[..., None] > iris_r * 1.6)
col = np.broadcast_to(sclera, (N, N, 3)) * (1 - 0.25 * veins) + np.array([0.8, 0.3, 0.3]) * 0.25 * veins
# a faint grey-green ring that fades into the white, not a big gold iris
iris_col = sample(iris, IRIS_SCALE * 2.2)[..., :3] * np.array([0.9, 0.75, 0.45])
in_iris = np.clip((iris_r - r) / 0.006, 0, 1)[..., None]
col = col * (1 - in_iris) + iris_col * in_iris
in_pupil = np.clip((pupil_r - r) / 0.004, 0, 1)[..., None]
col = col * (1 - in_pupil) + np.array([0.02, 0.02, 0.03]) * in_pupil
eye = np.dstack([col, np.ones((N, N))])
save_vtf("kenny_eye", eye, ImageFormats.DXT1)
(OUT / "kenny_eye.vmt").write_text(
    '"VertexLitGeneric"\n{\n\t"$basetexture" "models/weapons/kenny/kenny_eye"\n'
    '\t"$phong" "1"\n\t"$phongexponent" "120"\n\t"$phongboost" "5"\n\t"$phongfresnelranges" "[1 1 1]"\n'
    '\t"$bumpmap" "dev/bump_normal"\n'
    '}\n')
