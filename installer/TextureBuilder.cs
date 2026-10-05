using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;

namespace KennyInstaller;

/// <summary>Float RGBA image, row-major, values 0..1.</summary>
public sealed class Img
{
    public readonly int W, H;
    public readonly float[] P; // r,g,b,a
    public Img(int w, int h) { W = w; H = h; P = new float[w * h * 4]; }
    public ref float At(int x, int y, int c) => ref P[(y * W + x) * 4 + c];

    /// <summary>Box filter down to size (power-of-two factor), or nearest/bilinear otherwise.</summary>
    public Img Resized(int size)
    {
        if (W == size && H == size) return this;
        var o = new Img(size, size);
        if (W % size == 0 && H % size == 0)
        {
            int fx = W / size, fy = H / size;
            float inv = 1f / (fx * fy);
            Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                    for (int c = 0; c < 4; c++)
                    {
                        float s = 0;
                        for (int j = 0; j < fy; j++)
                            for (int i = 0; i < fx; i++) s += At(x * fx + i, y * fy + j, c);
                        o.At(x, y, c) = s * inv;
                    }
            });
            return o;
        }
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float sx = (x + 0.5f) * W / size - 0.5f, sy = (y + 0.5f) * H / size - 0.5f;
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, W - 1), y0 = Math.Clamp((int)Math.Floor(sy), 0, H - 1);
                int x1 = Math.Min(x0 + 1, W - 1), y1 = Math.Min(y0 + 1, H - 1);
                float tx = Math.Clamp(sx - x0, 0, 1), ty = Math.Clamp(sy - y0, 0, 1);
                for (int c = 0; c < 4; c++)
                    o.At(x, y, c) = (At(x0, y0, c) * (1 - tx) + At(x1, y0, c) * tx) * (1 - ty) +
                                    (At(x0, y1, c) * (1 - tx) + At(x1, y1, c) * tx) * ty;
            }
        });
        return o;
    }

    public byte[] ToRgba8()
    {
        var b = new byte[P.Length];
        for (int i = 0; i < P.Length; i++) b[i] = (byte)Math.Clamp((int)Math.Round(P[i] * 255), 0, 255);
        return b;
    }
}

/// <summary>Kenny's Unreal textures -> Source VTF + VMT (port of tools/build_textures.py).</summary>
public sealed class TextureBuilder
{
    const string Kenny = "Oregon/Content/Art/Characters/Kenny/";
    const string Eyes = "Oregon/Content/Art/Characters/Eyes/";
    readonly IFileProvider _provider;
    readonly Action<string> _log;
    readonly Dictionary<string, Img> _cache = new();
    public TextureBuilder(IFileProvider provider, Action<string> log) { _provider = provider; _log = log; }

    public Img Load(string path)
    {
        if (_cache.TryGetValue(path, out var cached)) return cached;
        var tex = _provider.LoadPackageObject<UTexture2D>(path + "." + path[(path.LastIndexOf('/') + 1)..]);
        var c = tex.Decode(ETexturePlatform.DesktopMobile) ?? throw new Exception("could not decode " + path);
        var img = new Img(c.Width, c.Height);
        var d = c.Data;
        bool bgra = c.PixelFormat == EPixelFormat.PF_B8G8R8A8;
        if (c.PixelFormat != EPixelFormat.PF_R8G8B8A8 && !bgra) throw new Exception($"unexpected pixel format {c.PixelFormat} in {path}");
        for (int i = 0; i < c.Width * c.Height; i++)
        {
            img.P[i * 4 + 0] = d[i * 4 + (bgra ? 2 : 0)] / 255f;
            img.P[i * 4 + 1] = d[i * 4 + 1] / 255f;
            img.P[i * 4 + 2] = d[i * 4 + (bgra ? 0 : 2)] / 255f;
            img.P[i * 4 + 3] = d[i * 4 + 3] / 255f;
        }
        return _cache[path] = img;
    }

    public void Build(string materialDir)
    {
        Directory.CreateDirectory(materialDir);
        PbrSet(materialDir, "kenny_body", Kenny + "Textures/Kenny_Body_D", Kenny + "Textures/Kenny_Body_N",
            Kenny + "Textures/Kenny_Body_ORM", Kenny + "Textures/Kenny_Body_E", 2048);
        PbrSet(materialDir, "kenny_eyelids", Kenny + "Textures/Kenny_Eyelids_D", Kenny + "Textures/Kenny_Eyelids_N",
            Kenny + "Textures/Kenny_Eyelids_ORM", null, 1024);
        PbrSet(materialDir, "kenny_chip", Kenny + "InhibitorChip/Kenny_InhibitorChip_D", Kenny + "InhibitorChip/Kenny_InhibitorChip_N",
            Kenny + "Textures/Kenny_Body_ORM", Kenny + "InhibitorChip/Kenny_InhibitorChip_E", 1024);
        var eye = EyeTexture();
        Vtf.Write(Path.Combine(materialDir, "kenny_eye.vtf"), eye, CompressionFormat.Bc1, 0);
        File.WriteAllText(Path.Combine(materialDir, "kenny_eye.vmt"),
            "\"VertexLitGeneric\"\n{\n\t\"$basetexture\" \"models/weapons/kenny/kenny_eye\"\n" +
            "\t\"$phong\" \"1\"\n\t\"$phongexponent\" \"120\"\n\t\"$phongboost\" \"5\"\n\t\"$phongfresnelranges\" \"[1 1 1]\"\n" +
            "\t\"$bumpmap\" \"dev/bump_normal\"\n}\n");
        _log("  textures: kenny_body, kenny_eyelids, kenny_chip, kenny_eye");
    }

    void PbrSet(string dir, string name, string dPath, string nPath, string ormPath, string? ePath, int size)
    {
        var d = Load(dPath).Resized(size);
        var n = Load(nPath).Resized(size);
        var orm = Load(ormPath).Resized(size);
        var dd = new Img(size, size); Array.Copy(d.P, dd.P, d.P.Length);
        var nn = new Img(size, size); Array.Copy(n.P, nn.P, n.P.Length);
        for (int i = 0; i < size * size; i++)
        {
            dd.P[i * 4 + 3] = 1;
            nn.P[i * 4 + 3] = (1 - orm.P[i * 4 + 1]) * orm.P[i * 4 + 0]; // phong mask = (1 - roughness) * AO
        }
        Vtf.Write(Path.Combine(dir, name + ".vtf"), dd, CompressionFormat.Bc1, 0);
        Vtf.Write(Path.Combine(dir, name + "_n.vtf"), nn, CompressionFormat.Bc3, Vtf.FlagNormal);
        var vmt = new List<string> {
            "\"VertexLitGeneric\"", "{",
            $"\t\"$basetexture\" \"models/weapons/kenny/{name}\"",
            $"\t\"$bumpmap\" \"models/weapons/kenny/{name}_n\"",
            "\t\"$phong\" \"1\"", "\t\"$phongexponent\" \"24\"", "\t\"$phongboost\" \"2\"",
            "\t\"$phongfresnelranges\" \"[0.3 0.8 1]\"",
            "\t\"$rimlight\" \"1\"", "\t\"$rimlightexponent\" \"6\"", "\t\"$rimlightboost\" \"0.6\"" };
        if (ePath != null)
        {
            var e = Load(ePath).Resized(size);
            var mask = new Img(size, size);
            for (int i = 0; i < size * size; i++)
            {
                var lum = Math.Max(e.P[i * 4], Math.Max(e.P[i * 4 + 1], e.P[i * 4 + 2]));
                mask.P[i * 4] = mask.P[i * 4 + 1] = mask.P[i * 4 + 2] = lum; mask.P[i * 4 + 3] = 1;
            }
            Vtf.Write(Path.Combine(dir, name + "_e.vtf"), mask, CompressionFormat.Bc1, 0);
            vmt.AddRange(new[] { "\t\"$selfillum\" \"1\"", $"\t\"$selfillummask\" \"models/weapons/kenny/{name}_e\"", "\t\"$selfillumtint\" \"[2 2 2]\"" });
        }
        vmt.Add("}");
        File.WriteAllText(Path.Combine(dir, name + ".vmt"), string.Join("\n", vmt) + "\n");
    }

    /// <summary>The eyeball: Unreal builds it in a shader from a mask (R = veins) and an iris texture, sampled around
    /// UV (0.5, 0.5) and shrunk by the material's IrisScale (0.19) / PupilScale (0.32).</summary>
    public Img EyeTexture()
    {
        const double irisScale = 0.19, pupilScale = 0.32;
        const int N = 1024;
        var mask = Load(Eyes + "Eye_Mask_A").Resized(N);
        var iris = Load(Eyes + "Iris_D").Resized(N);
        double irisR = irisScale * 0.5, pupilR = irisR * pupilScale * 1.6;
        double[] sclera = { 0.95, 0.93, 0.86 };
        var o = new Img(N, N);
        Parallel.For(0, N, yi =>
        {
            double yy = (double)yi / N - 0.5;
            for (int xi = 0; xi < N; xi++)
            {
                double xx = (double)xi / N - 0.5, r = Math.Sqrt(xx * xx + yy * yy);
                int S(double v, double scale) => Math.Clamp((int)((v / scale + 0.5) * N), 0, N - 1);
                double veins = r > irisR * 1.6 ? mask.At(S(xx, 1.0), S(yy, 1.0), 0) : 0;
                int iu = S(xx, irisScale * 2.2), iv = S(yy, irisScale * 2.2);
                double[] irisCol = { iris.At(iu, iv, 0) * 0.9, iris.At(iu, iv, 1) * 0.75, iris.At(iu, iv, 2) * 0.45 };
                double inIris = Math.Clamp((irisR - r) / 0.006, 0, 1), inPupil = Math.Clamp((pupilR - r) / 0.004, 0, 1);
                double[] veinCol = { 0.8, 0.3, 0.3 }, pupilCol = { 0.02, 0.02, 0.03 };
                for (int c = 0; c < 3; c++)
                {
                    double col = sclera[c] * (1 - 0.25 * veins) + veinCol[c] * 0.25 * veins;
                    col = col * (1 - inIris) + irisCol[c] * inIris;
                    col = col * (1 - inPupil) + pupilCol[c] * inPupil;
                    o.At(xi, yi, c) = (float)col;
                }
                o.At(xi, yi, 3) = 1;
            }
        });
        return o;
    }
}

/// <summary>Minimal VTF 7.2 writer (DXT1 / DXT5, full mip chain, no thumbnail).</summary>
public static class Vtf
{
    public const uint FlagNormal = 0x80, FlagEightBitAlpha = 0x2000;

    public static void Write(string path, Img img, CompressionFormat fmt, uint flags)
    {
        var enc = new BcEncoder();
        enc.OutputOptions.Format = fmt;
        enc.OutputOptions.GenerateMipMaps = true;
        enc.OutputOptions.Quality = CompressionQuality.Balanced;
        enc.Options.IsParallel = true;
        var mips = enc.EncodeToRawBytes(img.ToRgba8(), img.W, img.H, PixelFormat.Rgba32);
        if (fmt == CompressionFormat.Bc3) flags |= FlagEightBitAlpha;

        // reflectivity: average colour
        double r = 0, g = 0, b = 0; int n = img.W * img.H;
        for (int i = 0; i < n; i++) { r += img.P[i * 4]; g += img.P[i * 4 + 1]; b += img.P[i * 4 + 2]; }

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write("VTF\0"u8.ToArray());
        w.Write(7u); w.Write(2u); w.Write(80u);           // version 7.2, header size
        w.Write((ushort)img.W); w.Write((ushort)img.H);
        w.Write(flags);
        w.Write((ushort)1); w.Write((ushort)0);           // frames, first frame
        w.Write(new byte[4]);
        w.Write((float)(r / n)); w.Write((float)(g / n)); w.Write((float)(b / n));
        w.Write(new byte[4]);
        w.Write(1f);                                       // bumpmap scale
        w.Write(fmt == CompressionFormat.Bc1 ? 13u : 15u); // IMAGE_FORMAT_DXT1 / DXT5
        w.Write((byte)mips.Length);
        w.Write(0xFFFFFFFFu);                              // no low-res thumbnail
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1);                                // depth
        w.Write(new byte[80 - 65]);
        for (int m = mips.Length - 1; m >= 0; m--) w.Write(mips[m]); // smallest mip first
    }
}
