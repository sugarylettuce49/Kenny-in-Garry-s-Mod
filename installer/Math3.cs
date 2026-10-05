namespace KennyInstaller;

/// <summary>3-vector (double precision; column-vector maths throughout).</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public V3 Normalized() { var l = Length; return l > 1e-12 ? this * (1 / l) : this; }
}

/// <summary>Row-major 3x3 matrix.</summary>
public sealed class M3
{
    public readonly double[] M = new double[9];
    public double this[int r, int c] { get => M[r * 3 + c]; set => M[r * 3 + c] = value; }

    public static M3 Identity() { var m = new M3(); m[0, 0] = m[1, 1] = m[2, 2] = 1; return m; }
    public static M3 Diag(double a, double b, double c) { var m = new M3(); m[0, 0] = a; m[1, 1] = b; m[2, 2] = c; return m; }

    public static M3 FromQuat(double x, double y, double z, double w)
    {
        var n = Math.Sqrt(x * x + y * y + z * z + w * w);
        x /= n; y /= n; z /= n; w /= n;
        var m = new M3();
        m[0, 0] = 1 - 2 * (y * y + z * z); m[0, 1] = 2 * (x * y - z * w); m[0, 2] = 2 * (x * z + y * w);
        m[1, 0] = 2 * (x * y + z * w); m[1, 1] = 1 - 2 * (x * x + z * z); m[1, 2] = 2 * (y * z - x * w);
        m[2, 0] = 2 * (x * z - y * w); m[2, 1] = 2 * (y * z + x * w); m[2, 2] = 1 - 2 * (x * x + y * y);
        return m;
    }

    public static M3 operator *(M3 a, M3 b)
    {
        var m = new M3();
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r, c] = a[r, 0] * b[0, c] + a[r, 1] * b[1, c] + a[r, 2] * b[2, c];
        return m;
    }

    public static V3 operator *(M3 a, V3 v) =>
        new(a[0, 0] * v.X + a[0, 1] * v.Y + a[0, 2] * v.Z,
            a[1, 0] * v.X + a[1, 1] * v.Y + a[1, 2] * v.Z,
            a[2, 0] * v.X + a[2, 1] * v.Y + a[2, 2] * v.Z);

    public static M3 operator *(M3 a, double s) { var m = new M3(); for (int i = 0; i < 9; i++) m.M[i] = a.M[i] * s; return m; }
    public static M3 operator +(M3 a, M3 b) { var m = new M3(); for (int i = 0; i < 9; i++) m.M[i] = a.M[i] + b.M[i]; return m; }

    public M3 Inverse()
    {
        double a = this[0, 0], b = this[0, 1], c = this[0, 2], d = this[1, 0], e = this[1, 1], f = this[1, 2], g = this[2, 0], h = this[2, 1], i = this[2, 2];
        var det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        var m = new M3();
        m[0, 0] = (e * i - f * h) / det; m[0, 1] = (c * h - b * i) / det; m[0, 2] = (b * f - c * e) / det;
        m[1, 0] = (f * g - d * i) / det; m[1, 1] = (a * i - c * g) / det; m[1, 2] = (c * d - a * f) / det;
        m[2, 0] = (d * h - e * g) / det; m[2, 1] = (b * g - a * h) / det; m[2, 2] = (a * e - b * d) / det;
        return m;
    }

    /// <summary>Euler XYZ (R = Rz * Ry * Rx), same convention as Blender's to_euler('XYZ') and SMD.</summary>
    public V3 ToEulerXYZ()
    {
        var cy = Math.Sqrt(this[0, 0] * this[0, 0] + this[1, 0] * this[1, 0]);
        if (cy > 1e-9)
            return new V3(Math.Atan2(this[2, 1], this[2, 2]), Math.Atan2(-this[2, 0], cy), Math.Atan2(this[1, 0], this[0, 0]));
        return new V3(Math.Atan2(-this[1, 2], this[1, 1]), Math.Atan2(-this[2, 0], cy), 0);
    }
}

/// <summary>Affine transform p' = R p + T.</summary>
public readonly record struct Xf(M3 R, V3 T)
{
    public static Xf Identity => new(M3.Identity(), new V3(0, 0, 0));
    public static Xf operator *(Xf a, Xf b) => new(a.R * b.R, a.R * b.T + a.T);
    public V3 Apply(V3 p) => R * p + T;
    public Xf Inverse() { var ri = R.Inverse(); return new Xf(ri, (ri * T) * -1); }
}

/// <summary>
/// The coordinate conventions this mod's Lua is tuned for (they come from the original Blender/glTF pipeline,
/// see tools/glb_to_smd.py):
///   model position (metres) = S * unreal position (cm) * 0.01, S = diag(1, -1, 1)
///   bone frame             = S * unreal component rotation * D, D swaps the Y and Z columns
/// </summary>
public static class Conv
{
    public static readonly M3 S = M3.Diag(1, -1, 1);
    public static readonly M3 D = Swap();
    public const double M2IN = 1.0 / 0.0254;

    static M3 Swap() { var m = new M3(); m[0, 0] = 1; m[1, 2] = 1; m[2, 1] = 1; return m; }

    public static V3 PosM(V3 ueCm) => S * ueCm * 0.01;
    public static V3 PosIn(V3 ueCm) => PosM(ueCm) * M2IN;
    public static V3 Dir(V3 ue) => (S * ue).Normalized();

    /// <summary>Unreal component-space bone transform (cm) -> bone frame in model space (metres).</summary>
    public static Xf BoneFrame(Xf ueComp) => new(S * ueComp.R * D, PosM(ueComp.T));

    /// <summary>A change in Unreal model space (delta = posed * rest^-1, cm) expressed in model space (metres).</summary>
    public static Xf Delta(Xf ueDelta) => new(S * ueDelta.R * S, PosM(ueDelta.T));
}
