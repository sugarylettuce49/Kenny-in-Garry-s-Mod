using System.IO.Compression;

namespace KennyInstaller;

/// <summary>Software-renders Kenny's spawn-menu icon (512x512, transparent) from the player's own game data.</summary>
public static class IconRenderer
{
    public static void Render(string pngPath, V3[] pos, V3[] nrm, (double u, double v)[] uv,
        List<(int a, int b, int c, string mat)> tris, Dictionary<string, Img> textures)
    {
        const int R = 1024; // render at 2x, downsample for anti-aliasing
        double x0 = pos.Min(p => p.X), x1 = pos.Max(p => p.X), y0 = pos.Min(p => p.Y), y1 = pos.Max(p => p.Y), z0 = pos.Min(p => p.Z), z1 = pos.Max(p => p.Z);
        var center = new V3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
        double size = Math.Max(x1 - x0, Math.Max(y1 - y0, z1 - z0));
        double ortho = size * 1.25;
        // three-quarter view from behind-left: Kenny's face is at the back of the gun
        var fwd = (new V3(1.0, 1.1, -0.45)).Normalized();
        var right = fwd.Cross(new V3(0, 0, 1)).Normalized();
        var up = right.Cross(fwd);
        var light1 = new V3(-1, -2, 2).Normalized(); var light2 = new V3(2, 1, 2).Normalized();

        var color = new float[R * R * 4];
        var depth = new double[R * R];
        Array.Fill(depth, double.MaxValue);
        (double x, double y, double z) Proj(V3 p)
        {
            var d = p - center;
            return ((d.Dot(right) / ortho + 0.5) * R, (0.5 - d.Dot(up) / ortho) * R, d.Dot(fwd));
        }
        var sp = pos.Select(Proj).ToArray();

        foreach (var (a, b, c, mat) in tris)
        {
            if (!textures.TryGetValue(mat, out var tex)) continue;
            var (ax, ay, az) = sp[a]; var (bx, by, bz) = sp[b]; var (cx, cy, cz) = sp[c];
            double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(area) < 1e-9) continue;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), maxX = Math.Min(R - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), maxY = Math.Min(R - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
                    double w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
                    double w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    double z = w0 * az + w1 * bz + w2 * cz;
                    int idx = y * R + x;
                    if (z >= depth[idx]) continue;
                    depth[idx] = z;
                    double u = w0 * uv[a].u + w1 * uv[b].u + w2 * uv[c].u, v = w0 * uv[a].v + w1 * uv[b].v + w2 * uv[c].v;
                    int tx = Math.Clamp((int)((u - Math.Floor(u)) * tex.W), 0, tex.W - 1);
                    int ty = Math.Clamp((int)((1 - (v - Math.Floor(v))) * tex.H), 0, tex.H - 1);
                    var n = (nrm[a] * w0 + nrm[b] * w1 + nrm[c] * w2).Normalized();
                    if (n.Dot(fwd) > 0) n = n * -1; // two-sided
                    double lit = 0.42 + 0.55 * Math.Max(0, n.Dot(light1)) + 0.3 * Math.Max(0, n.Dot(light2));
                    for (int ch = 0; ch < 3; ch++) color[idx * 4 + ch] = (float)Math.Min(1, tex.At(tx, ty, ch) * lit);
                    color[idx * 4 + 3] = 1;
                }
        }

        // 2x2 downsample (alpha-weighted)
        const int O = R / 2;
        var outPx = new byte[O * O * 4];
        for (int y = 0; y < O; y++)
            for (int x = 0; x < O; x++)
            {
                float r = 0, g = 0, b = 0, al = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int k = ((y * 2 + j) * R + x * 2 + i) * 4;
                        float aa = color[k + 3]; r += color[k] * aa; g += color[k + 1] * aa; b += color[k + 2] * aa; al += aa;
                    }
                int o = (y * O + x) * 4;
                if (al > 0) { outPx[o] = (byte)(r / al * 255); outPx[o + 1] = (byte)(g / al * 255); outPx[o + 2] = (byte)(b / al * 255); }
                outPx[o + 3] = (byte)(al / 4 * 255);
            }
        Png.Write(pngPath, outPx, O, O);
    }
}

/// <summary>Minimal RGBA8 PNG encoder.</summary>
public static class Png
{
    static readonly uint[] Crc = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static void Write(string path, byte[] rgba, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(fs, "IHDR", ihdr);
        var raw = new MemoryStream();
        for (int y = 0; y < h; y++) { raw.WriteByte(0); raw.Write(rgba, y * w * 4, w * 4); }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);
        Chunk(fs, "IDAT", z.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(td);
        uint c = 0xFFFFFFFFu;
        foreach (var bt in td) c = Crc[(c ^ bt) & 0xFF] ^ (c >> 8);
        var crc = new byte[4]; BE(crc, 0, (int)(c ^ 0xFFFFFFFFu)); s.Write(crc);
    }
}
