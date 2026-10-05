using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse_Conversion.Animations;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Meshes;
using CUE4Parse_Conversion.Options;

namespace KennyInstaller;

/// <summary>
/// Kenny's skeletal mesh + facial expressions -> Source SMD / VTA / QC, plus the per-expression eyelid/eyeball bone
/// rotations for Lua. A port of tools/glb_to_smd.py + tools/psa_pose.py that reads the game files directly.
/// </summary>
public sealed class ModelBuilder
{
    const string KennyDir = "Oregon/Content/Art/Characters/Kenny/";
    public const string MeshPath = KennyDir + "KennyV2_skelmesh";
    public static readonly string[] Expressions = { "neutral", "sarcastic", "happy", "wicked", "angry", "excited" };

    static readonly Dictionary<string, string> Materials = new()
    {
        ["Kenny_MI"] = "kenny_body",
        ["Kenny_Eyeball_MI"] = "kenny_eye",
        ["Kenny_Eyelids_MI"] = "kenny_eyelids",
        ["Kenny_InhibitorChip"] = "kenny_chip",
    };

    // Source caps a model at 128 bones; Kenny has 229 (incl. the player's first-person arms).
    static readonly Regex Drop = new(@"(Lip_\d+|eyeOrbit_\d+_[lr]|eyelid_\d+_[lr])_JNT$");
    static readonly HashSet<string> Keep = new() {
        "upperLip_01_JNT", "upperLip_03_JNT", "upperLip_43_JNT", "upperLip_45_JNT",
        "lowerLip_03_JNT", "lowerLip_05_JNT", "lowerLip_33_JNT", "lowerLip_35_JNT" };
    // eyelids/eyeballs swing round the eye: driven as bone rotations from Lua, never baked into flexes
    public static readonly string[] FaceBones = {
        "sep_upper_eyelid_l_JNT", "sep_upper_eyelid_r_JNT", "sep_lower_eyelid_l_JNT", "sep_lower_eyelid_r_JNT",
        "gun_eye_l_JNT", "gun_eye_r_JNT" };

    readonly IFileProvider _provider;
    readonly Action<string> _log;

    // reference skeleton (Unreal)
    string[] _names = Array.Empty<string>();
    int[] _parent = Array.Empty<int>();
    Xf[] _restLocal = Array.Empty<Xf>();
    Xf[] _restComp = Array.Empty<Xf>();
    Xf[] _frame = Array.Empty<Xf>(); // bone frames in model space (metres)

    // mesh (Unreal, cm)
    V3[] _pos = Array.Empty<V3>();
    V3[] _nrm = Array.Empty<V3>();
    (double u, double v)[] _uv = Array.Empty<(double, double)>();
    (int bone, double w)[][] _infl = Array.Empty<(int, double)[]>();
    readonly List<(int a, int b, int c, string mat)> _tris = new();

    public ModelBuilder(IFileProvider provider, Action<string> log) { _provider = provider; _log = log; }

    public int VertexCount => _pos.Length;

    // ---------------------------------------------------------------- loading

    public void Load()
    {
        var mesh = _provider.LoadPackageObject<USkeletalMesh>(MeshPath + ".KennyV2_skelmesh");
        var refSkel = mesh.ReferenceSkeleton;
        int n = refSkel.FinalRefBoneInfo.Length;
        _names = new string[n]; _parent = new int[n]; _restLocal = new Xf[n]; _restComp = new Xf[n]; _frame = new Xf[n];
        for (int i = 0; i < n; i++)
        {
            _names[i] = refSkel.FinalRefBoneInfo[i].Name.Text;
            _parent[i] = refSkel.FinalRefBoneInfo[i].ParentIndex;
            var t = refSkel.FinalRefBonePose[i];
            _restLocal[i] = new Xf(M3.FromQuat(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W),
                new V3(t.Translation.X, t.Translation.Y, t.Translation.Z));
            _restComp[i] = _parent[i] < 0 ? _restLocal[i] : _restComp[_parent[i]] * _restLocal[i];
            _frame[i] = Conv.BoneFrame(_restComp[i]);
        }

        if (!mesh.TryConvert(out SkeletalMeshDto dto, EMeshQuality.Highest) || dto.LODs.Count == 0)
            throw new Exception("could not read Kenny's mesh");
        var lod = dto.LODs[0];
        int nv = lod.Vertices.Length;
        _pos = new V3[nv]; _nrm = new V3[nv]; _uv = new (double, double)[nv]; _infl = new (int, double)[nv][];
        for (int i = 0; i < nv; i++)
        {
            var v = lod.Vertices[i];
            _pos[i] = new V3(v.Position.X, v.Position.Y, v.Position.Z);
            _nrm[i] = new V3(v.Normal.X, v.Normal.Y, v.Normal.Z).Normalized();
            _uv[i] = (v.Uv.U, v.Uv.V);
            _infl[i] = v.Influences.Where(f => f.Weight > 0).Select(f => ((int)f.Bone, (double)f.Weight)).ToArray();
        }
        var matNames = dto.Materials.Select(m => m.Material.ResolvedObject?.Name.Text ?? m.SlotName).ToArray();
        foreach (var sec in lod.Sections)
        {
            var mat = Materials.GetValueOrDefault(matNames[sec.MaterialIndex], "kenny_body");
            for (int f = 0; f < sec.NumFaces; f++)
            {
                int k = sec.FirstIndex + f * 3;
                _tris.Add(((int)lod.Indices[k], (int)lod.Indices[k + 1], (int)lod.Indices[k + 2], mat));
            }
        }
        _log($"  mesh: {nv} vertices, {_tris.Count} triangles, {n} bones");
    }

    // ---------------------------------------------------------------- skeleton folding

    int[] _kept = Array.Empty<int>();
    Dictionary<int, int> _keptIndex = new();
    int[] _remap = Array.Empty<int>();

    bool IsArm(int i) { for (; i >= 0; i = _parent[i]) if (_names[i].StartsWith("clavicle_")) return true; return false; }

    void FoldBones()
    {
        var kept = new List<int>();
        for (int i = 0; i < _names.Length; i++)
            if ((Keep.Contains(_names[i]) || !Drop.IsMatch(_names[i])) && !IsArm(i)) kept.Add(i);
        _kept = kept.ToArray();
        _keptIndex = _kept.Select((b, k) => (b, k)).ToDictionary(x => x.b, x => x.k);
        int jaw = Array.IndexOf(_names, "gun_jaw_JNT");
        _remap = new int[_names.Length];
        for (int i = 0; i < _names.Length; i++)
        {
            int b = i;
            if (!_keptIndex.ContainsKey(i) && _names[i].StartsWith("lowerLip_")) b = jaw; // lower lip rides the jaw
            while (!_keptIndex.ContainsKey(b)) b = _parent[b];
            _remap[i] = _keptIndex[b];
        }
        if (_kept.Length > 128) throw new Exception("too many bones for Source");
    }

    // ---------------------------------------------------------------- posing (facial expressions)

    bool Under(int i, string ancestor) { for (; i >= 0; i = _parent[i]) if (_names[i] == ancestor) return true; return false; }
    bool UnderAny(int i, IEnumerable<string> names) => names.Any(nm => Under(i, nm));

    /// <summary>Component-space pose of the middle frame of an animation, re-anchored on gun_root (first-person
    /// anims also move the whole gun in front of the camera). Returns null if it can't be read.</summary>
    Xf[]? AnimPose(string animPath)
    {
        var seqAsset = _provider.LoadPackageObject<UAnimSequence>(animPath + "." + animPath[(animPath.LastIndexOf('/') + 1)..]);
        var set = seqAsset.ConvertAnims();
        var seq = set.Sequences[0];
        var skelNames = set.Skeleton.ReferenceSkeleton.FinalRefBoneInfo.Select(b => b.Name.Text).ToArray();
        float frame = seq.NumFrames / 2;

        // raw Unreal keys, same convention as the reference pose (verified against the Blender pipeline)
        var local = new Xf[_names.Length];
        for (int i = 0; i < _names.Length; i++)
        {
            local[i] = _restLocal[i];
            int si = Array.IndexOf(skelNames, _names[i]);
            if (si < 0 || si >= seq.Tracks.Count || !seq.Tracks[si].HasKeys()) continue;
            var q = new CUE4Parse.UE4.Objects.Core.Math.FQuat(0, 0, 0, 1);
            var p = new CUE4Parse.UE4.Objects.Core.Math.FVector(_restLocal[i].T.X, _restLocal[i].T.Y, _restLocal[i].T.Z);
            var sc = new CUE4Parse.UE4.Objects.Core.Math.FVector(1, 1, 1);
            seq.Tracks[si].GetBoneTransform(frame, seq.NumFrames, ref q, ref p, ref sc);
            local[i] = new Xf(M3.FromQuat(q.X, q.Y, q.Z, q.W), new V3(p.X, p.Y, p.Z));
        }
        var best = Compose(local, _ => true);
        int head = Array.IndexOf(_names, "gun_head_JNT");
        int jawI = Array.IndexOf(_names, "gun_jaw_JNT");
        var bestDrift = ((best[head].Inverse() * best[jawI]).T - (_restComp[head].Inverse() * _restComp[jawI]).T).Length;
        if (best == null) return null;
        int g = Array.IndexOf(_names, "gun_root_JNT");
        var fix = _restComp[g] * best[g].Inverse();
        for (int i = 0; i < best.Length; i++) best[i] = fix * best[i];
        _log($"    {animPath[(animPath.LastIndexOf('/') + 1)..]}: frame {frame}, jaw moved {bestDrift:0.00} cm");
        return best;
    }

    Xf[] Compose(Xf[] local, Func<int, bool> _)
    {
        var comp = new Xf[local.Length];
        for (int i = 0; i < local.Length; i++) comp[i] = _parent[i] < 0 ? local[i] : comp[_parent[i]] * local[i];
        return comp;
    }

    /// <summary>Final component transforms when only bones in <paramref name="posedSet"/> take the animation; the
    /// rest keep their rest pose relative to their (possibly posed) parent - like Blender with identity basis.</summary>
    Xf[] PartialPose(Xf[] animComp, Func<int, bool> posedSet)
    {
        var final = new Xf[_names.Length];
        for (int i = 0; i < _names.Length; i++)
        {
            if (posedSet(i)) final[i] = animComp[i];
            else final[i] = _parent[i] < 0 ? _restLocal[i] : final[_parent[i]] * _restLocal[i];
        }
        return final;
    }

    (V3[] pos, V3[] nrm) Skin(Xf[] final)
    {
        var skin = new Xf[_names.Length];
        for (int i = 0; i < _names.Length; i++) skin[i] = final[i] * _restComp[i].Inverse();
        var pos = new V3[_pos.Length]; var nrm = new V3[_pos.Length];
        for (int v = 0; v < _pos.Length; v++)
        {
            V3 p = default, nn = default; double tw = 0;
            foreach (var (b, w) in _infl[v]) { p += skin[b].Apply(_pos[v]) * w; nn += (skin[b].R * _nrm[v]) * w; tw += w; }
            if (tw <= 0) { p = _pos[v]; nn = _nrm[v]; } else { p *= 1 / tw; }
            pos[v] = p; nrm[v] = nn.Normalized();
        }
        return (pos, nrm);
    }

    /// <summary>Blender-style local rotation (matrix_basis) of a bone in a posed skeleton, as Source Angle values.</summary>
    (double p, double y, double r) BasisAngles(Xf[] final, int i)
    {
        // posed bone frame in model space: the Unreal-space change, mapped, applied to the rest frame
        Xf Posed(int b) => Conv.Delta(final[b] * _restComp[b].Inverse()) * _frame[b];
        var restLocal = _frame[_parent[i]].Inverse() * _frame[i];
        var posedLocal = Posed(_parent[i]).Inverse() * Posed(i);
        var basis = restLocal.Inverse() * posedLocal;
        var e = basis.R.ToEulerXYZ();
        const double d = 180 / Math.PI;
        return (e.Y * d, e.Z * d, e.X * d);
    }

    // ---------------------------------------------------------------- writing

    static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);

    public void Write(string outDir, string faceLuaPath)
    {
        FoldBones();
        _log($"  skeleton: {_names.Length} -> {_kept.Length} bones");
        var inv = CultureInfo.InvariantCulture;

        // ---- reference SMD
        var sb = new StringBuilder("version 1\nnodes\n");
        for (int k = 0; k < _kept.Length; k++)
        {
            int b = _kept[k];
            int p = _parent[b] < 0 ? -1 : _keptIndex[_parent[b]];
            sb.Append($"{k} \"{_names[b]}\" {p}\n");
        }
        sb.Append("end\nskeleton\ntime 0\n");
        for (int k = 0; k < _kept.Length; k++)
        {
            int b = _kept[k];
            var m = _parent[b] < 0 ? _frame[b] : _frame[_parent[b]].Inverse() * _frame[b];
            var t = m.T * Conv.M2IN; var e = m.R.ToEulerXYZ();
            sb.Append($"{k} {F(t.X)} {F(t.Y)} {F(t.Z)} {F(e.X)} {F(e.Y)} {F(e.Z)}\n");
        }
        sb.Append("end\ntriangles\n");
        var basePos = _pos.Select(Conv.PosIn).ToArray();
        var baseNrm = _nrm.Select(Conv.Dir).ToArray();
        foreach (var (a, b, c, mat) in _tris)
        {
            sb.Append(mat).Append('\n');
            foreach (var v in new[] { a, b, c })
            {
                var acc = new Dictionary<int, double>();
                foreach (var (bone, w) in _infl[v]) acc[_remap[bone]] = acc.GetValueOrDefault(_remap[bone]) + w;
                var top = acc.OrderByDescending(kv => kv.Value).Take(3).ToList();
                var tot = top.Sum(kv => kv.Value); if (tot <= 0) { top = new() { new(0, 1) }; tot = 1; }
                var P = basePos[v]; var N = baseNrm[v];
                sb.Append($"0 {F(P.X)} {F(P.Y)} {F(P.Z)} {F(N.X)} {F(N.Y)} {F(N.Z)} {F(_uv[v].u)} {F(1 - _uv[v].v)} {top.Count}");
                foreach (var kv in top) sb.Append($" {kv.Key} {F(kv.Value / tot)}");
                sb.Append('\n');
            }
        }
        sb.Append("end\n");
        File.WriteAllText(Path.Combine(outDir, "kenny_ref.smd"), sb.ToString());

        // ---- physics: one box around the gun
        double x0 = basePos.Min(p => p.X), x1 = basePos.Max(p => p.X), y0 = basePos.Min(p => p.Y), y1 = basePos.Max(p => p.Y), z0 = basePos.Min(p => p.Z), z1 = basePos.Max(p => p.Z);
        var corners = new List<V3>();
        foreach (var x in new[] { x0, x1 }) foreach (var y in new[] { y0, y1 }) foreach (var z in new[] { z0, z1 }) corners.Add(new V3(x, y, z));
        int[][] quads = { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } };
        var ph = new StringBuilder("version 1\nnodes\n0 \"root_JNT\" -1\nend\nskeleton\ntime 0\n0 0 0 0 0 0 0\nend\ntriangles\n");
        foreach (var q in quads)
            foreach (var tri in new[] { new[] { q[0], q[1], q[2] }, new[] { q[0], q[2], q[3] } })
            {
                ph.Append("phys\n");
                foreach (var ci in tri) ph.Append($"0 {F(corners[ci].X)} {F(corners[ci].Y)} {F(corners[ci].Z)} 0 0 1 0 0 1 0 1\n");
            }
        ph.Append("end\n");
        File.WriteAllText(Path.Combine(outDir, "kenny_phys.smd"), ph.ToString());

        // ---- flexes: one per expression (face only, lids/eyes excluded) + lipsclosed
        var loopVerts = _tris.SelectMany(t => new[] { t.a, t.b, t.c }).ToArray();
        var vta = new StringBuilder("version 1\nnodes\n0 \"root_JNT\" -1\nend\nskeleton\n");
        for (int t = 0; t < Expressions.Length + 2; t++) vta.Append($"time {t}\n0 0 0 0 0 0 0\n");
        vta.Append("end\nvertexanimation\ntime 0\n");
        for (int i = 0; i < loopVerts.Length; i++)
        {
            var P = basePos[loopVerts[i]]; var N = baseNrm[loopVerts[i]];
            vta.Append($"{i} {F(P.X)} {F(P.Y)} {F(P.Z)} {F(N.X)} {F(N.Y)} {F(N.Z)}\n");
        }
        var face = new StringBuilder();
        face.Append("-- generated by KennyInstaller: per expression, eyelid / eyeball bone rotations\n");
        face.Append("-- (bone-local Angle(pitch, yaw, roll)) taken from Kenny's High on Life emotion animations\n");
        face.Append("KENNY_FACE = {\n");
        int t0 = 1;
        foreach (var expr in Expressions)
        {
            var comp = AnimPose(KennyDir + "Animations/emotions/KennyV2_emotions_" + expr)
                       ?? throw new Exception($"could not read the {expr} expression");
            // eyelid/eyeball rotations (full face posed, like the Blender pipeline)
            var fullFace = PartialPose(comp, i => Under(i, "gun_face_master_JNT"));
            face.Append($"\t{expr} = {{\n");
            foreach (var fb in FaceBones)
            {
                var (p, y, r) = BasisAngles(fullFace, Array.IndexOf(_names, fb));
                face.Append(string.Format(inv, "\t\t{0} = Angle({1:0.00}, {2:0.00}, {3:0.00}),\n", fb, p, y, r));
            }
            face.Append("\t},\n");
            // the flex: face without the lids/eyes
            var flexPose = PartialPose(comp, i => Under(i, "gun_face_master_JNT") && !UnderAny(i, FaceBones));
            var (pp, pn) = Skin(flexPose);
            vta.Append($"time {t0++}\n");
            int moved = 0;
            for (int i = 0; i < loopVerts.Length; i++)
            {
                var P = Conv.PosIn(pp[loopVerts[i]]);
                if ((P - basePos[loopVerts[i]]).Length <= 0.003) continue;
                var N = Conv.Dir(pn[loopVerts[i]]);
                vta.Append($"{i} {F(P.X)} {F(P.Y)} {F(P.Z)} {F(N.X)} {F(N.Y)} {F(N.Z)}\n");
                moved++;
            }
            _log($"    flex {expr}: {moved} corners");
        }
        face.Append("}\n");

        // lipsclosed: the rig's own morph target
        vta.Append($"time {t0}\n");
        var lips = LipsClosedDeltas();
        int nl = 0;
        for (int i = 0; i < loopVerts.Length; i++)
        {
            if (!lips.TryGetValue(loopVerts[i], out var d)) continue;
            var P = Conv.PosIn(_pos[loopVerts[i]] + d);
            if ((P - basePos[loopVerts[i]]).Length <= 0.003) continue;
            var N = baseNrm[loopVerts[i]];
            vta.Append($"{i} {F(P.X)} {F(P.Y)} {F(P.Z)} {F(N.X)} {F(N.Y)} {F(N.Z)}\n");
            nl++;
        }
        _log($"    flex lipsclosed: {nl} corners");
        vta.Append("end\n");
        File.WriteAllText(Path.Combine(outDir, "kenny.vta"), vta.ToString());
        File.WriteAllText(faceLuaPath, face.ToString());
    }

    Dictionary<int, V3> LipsClosedDeltas()
    {
        var res = new Dictionary<int, V3>();
        var mesh = _provider.LoadPackageObject<USkeletalMesh>(MeshPath + ".KennyV2_skelmesh");
        foreach (var mtRef in mesh.MorphTargets)
        {
            var mt = mtRef?.Load<UMorphTarget>();
            if (mt == null || !mt.Name.Contains("lipsclosed", StringComparison.OrdinalIgnoreCase)) continue;
            if (mt.MorphLODModels.Length == 0) continue;
            foreach (var d in mt.MorphLODModels[0].Vertices)
                res[(int)d.SourceIdx] = new V3(d.PositionDelta.X, d.PositionDelta.Y, d.PositionDelta.Z);
        }
        return res;
    }

    // ---------------------------------------------------------------- for the icon renderer

    /// <summary>The mesh posed in one expression (all face bones, lids included), in model inches.</summary>
    public (V3[] pos, V3[] nrm, (double u, double v)[] uv, List<(int a, int b, int c, string mat)> tris) Posed(string expr)
    {
        var comp = AnimPose(KennyDir + "Animations/emotions/KennyV2_emotions_" + expr);
        var (p, n) = comp == null ? (_pos, _nrm) : Skin(PartialPose(comp, i => Under(i, "gun_face_master_JNT")));
        return (p.Select(Conv.PosIn).ToArray(), n.Select(Conv.Dir).ToArray(), _uv.Select(t => (t.u, 1 - t.v)).ToArray(), _tris);
    }
}
