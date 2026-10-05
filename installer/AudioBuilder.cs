using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Wwise;

namespace KennyInstaller;

/// <summary>Kenny's Wwise voice lines + weapon sounds -> GMod .wav files and a Lua table of lines with loudness
/// envelopes for the jaw (port of tools/build_audio.py). Decoding uses vgmstream, downloaded on first run.</summary>
public sealed class AudioBuilder
{
    const string VoDir = "Oregon/Content/WwiseAudio/Localized/English_US_/Events/VO/VO/VO_Kenny/";
    const string SfxDir = "Oregon/Content/WwiseAudio/Events/SFX/Weapon/Kenny/";
    const int EnvHz = 20;

    // GMod-side situation -> High on Life VO events
    static readonly (string cat, string[] events)[] Vo =
    {
        ("equip", new[] { "G_Kenny_Equipped", "Global_Kenny_SwapGeneric" }),
        ("kill", new[] { "G_Kenny_Kill_EnemyGeneric", "Global_Kenny_KillsEnemy" }),
        ("multikill", new[] { "G_Kenny_Multikill" }),
        ("headshot", new[] { "G_Kenny_Headshot" }),
        ("hurt", new[] { "G_Kenny_Take_Damage", "Global_Kenny_TakesLightDamage" }),
        ("hurt_heavy", new[] { "G_Kenny_Take_HeavyDamage" }),
        ("hurt_fire", new[] { "G_Kenny_Hazard_DamageAcidFire" }),
        ("hurt_shock", new[] { "G_Kenny_Hazard_DamageElectric" }),
        ("hurt_other", new[] { "G_Kenny_Hazard_DamageGeneric" }),
        ("fall", new[] { "G_Kenny_Fall_OffCliff" }),
        ("death", new[] { "G_Kenny_Player_Dies" }),
        ("idle", new[] { "G_Kenny_Idles_Act1", "G_Kenny_Idles_Act2" }),
        ("combat_idle", new[] { "G_Kenny_Combat_Idle" }),
        ("first_shot", new[] { "Global_Kenny_FirstShotBattleCries" }),
        ("spot", new[] { "G_Kenny_Spot_AnyEnemies" }),
        ("taunt", new[] { "VS_Kenny_GenericTaunts", "Global_Kenny_GenericEnemyTaunt" }),
        ("glob_ready", new[] { "G_Kenny_Support_FireReady" }),
        ("glob_kill", new[] { "G_Kenny_Support_FireKill" }),
        ("env_kill", new[] { "G_Kenny_Environment_Kill" }),
        ("stagger", new[] { "G_Kenny_Enemy_Staggered" }),
        ("refuse", new[] { "G_Kenny_Shoot_NPC_Refusal" }),
        ("health", new[] { "G_Kenny_Pickup_Health" }),
        ("sprint", new[] { "G_Kenny_Sprint_ForLongTime" }),
        ("no_ammo", new[] { "Global_Kenny_OutOfAmmo" }),
    };
    static readonly (string key, string ev)[] Sfx =
    {
        ("fire", "Play_Weapon_Kenny_FirePrimary"),
        ("impact", "Play_SFX_Kenny_Fire_Basic_Impact"),
        ("glob_fire", "Play_Weapon_Kenny_Support_Fire"),
        ("glob_impact", "Play_Weapon_Kenny_Support_Impact"),
        ("draw", "Play_Weapon_Kenny_Draw"),
        ("holster", "Play_Weapon_Kenny_Holster"),
        ("reload", "Play_Weapon_Kenny_Reload"),
    };

    readonly IFileProvider _provider;
    readonly Action<string> _log;
    readonly string _work;
    string _vgm = "";

    public AudioBuilder(IFileProvider provider, Action<string> log, string workDir) { _provider = provider; _log = log; _work = workDir; }

    public async Task EnsureVgmstream()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KennyInstaller", "vgmstream");
        _vgm = Path.Combine(dir, "vgmstream-cli.exe");
        if (File.Exists(_vgm)) return;
        _log("  downloading vgmstream (Wwise audio decoder) from github.com/vgmstream ...");
        Directory.CreateDirectory(dir);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KennyInstaller");
        var zip = await http.GetByteArrayAsync("https://github.com/vgmstream/vgmstream/releases/latest/download/vgmstream-win64.zip");
        using var za = new ZipArchive(new MemoryStream(zip));
        za.ExtractToDirectory(dir, true);
        if (!File.Exists(_vgm)) throw new Exception("vgmstream-cli.exe missing from the download");
    }

    /// <summary>All media of one Wwise event, as .wem bytes.</summary>
    List<byte[]> EventMedia(string assetPath)
    {
        var res = new List<byte[]>();
        var ev = _provider.LoadPackage(assetPath).GetExports().FirstOrDefault(e => e.ExportType == "AkAudioEventData");
        if (ev == null) return res;
        foreach (var m in ev.GetOrDefault<UObject[]>("MediaList", Array.Empty<UObject>()))
        {
            foreach (var exp in m.Owner!.GetExports())
                if (exp is UAkMediaAssetData md)
                {
                    var bytes = md.DataChunks.Select(c => c.Data.Data).Where(d => d != null).SelectMany(d => d!).ToArray();
                    if (bytes.Length > 0) res.Add(bytes);
                }
        }
        return res;
    }

    (short[] mono, int rate) Decode(byte[] wem, string name)
    {
        var wemPath = Path.Combine(_work, name + ".wem");
        var wavPath = Path.Combine(_work, name + ".wav");
        File.WriteAllBytes(wemPath, wem);
        var psi = new ProcessStartInfo(_vgm, $"-o \"{wavPath}\" \"{wemPath}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var p = Process.Start(psi)!) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new Exception("vgmstream failed on " + name); }
        var (pcm, rate, ch) = Wav.Read(wavPath);
        File.Delete(wemPath); File.Delete(wavPath);
        var mono = new short[pcm.Length / ch];
        for (int i = 0; i < mono.Length; i++) { int s = 0; for (int c = 0; c < ch; c++) s += pcm[i * ch + c]; mono[i] = (short)(s / ch); }
        return (mono, rate);
    }

    static string Envelope(short[] a, int rate)
    {
        int hop = rate / EnvHz;
        var rms = new List<double>();
        for (int i = 0; i < a.Length; i += hop)
        {
            double s = 0; int n = Math.Min(hop, a.Length - i);
            for (int k = 0; k < n; k++) { double v = a[i + k] / 32768.0; s += v * v; }
            rms.Add(Math.Sqrt(s / Math.Max(n, 1)));
        }
        var max = rms.DefaultIfEmpty(0).Max(); if (max <= 0) max = 1;
        return string.Concat(rms.Select(v => (char)('0' + Math.Min(9, (int)(v / max * 10)))));
    }

    public void Build(string addonDir)
    {
        Directory.CreateDirectory(_work);
        var all = _provider.Files.Keys.Where(k => k.EndsWith(".uasset")).ToList();
        var lua = new StringBuilder();
        lua.Append($"-- generated by KennyInstaller: Kenny's lines, {{path, seconds, loudness envelope @ {EnvHz} Hz}}\nKENNY_VO = {{\n");
        int total = 0;
        foreach (var (cat, events) in Vo)
        {
            lua.Append($"\t{cat} = {{\n");
            int n = 0;
            foreach (var ev in events)
            {
                var rx = new Regex($"/Play_(\\d+_)?{ev}(_Improv)?_\\d+(_v\\d+)?\\.uasset$");
                foreach (var path in all.Where(k => k.StartsWith(VoDir) && rx.IsMatch(k)).OrderBy(k => k, StringComparer.Ordinal))
                    foreach (var wem in EventMedia(path[..^".uasset".Length]))
                    {
                        n++;
                        var (mono, rate) = Decode(wem, $"{cat}_{n:00}");
                        var env = Envelope(mono, rate);
                        var rel = $"kenny/vo/{cat}_{n:00}.wav";
                        Wav.Write(Path.Combine(addonDir, "sound", rel), Wav.Resample(mono, rate, 22050), 22050);
                        lua.Append(string.Format(CultureInfo.InvariantCulture, "\t\t{{\"{0}\", {1:0.00}, \"{2}\"}},\n", rel, mono.Length / (double)rate, env));
                    }
            }
            lua.Append("\t},\n");
            total += n;
        }
        lua.Append("}\nKENNY_SFX = {\n");
        foreach (var (key, ev) in Sfx)
        {
            var paths = new List<string>();
            var rx = new Regex($"/{ev}(_\\d+)?\\.uasset$");
            int i = 0;
            foreach (var path in all.Where(k => k.StartsWith(SfxDir) && rx.IsMatch(k)).OrderBy(k => k, StringComparer.Ordinal))
                foreach (var wem in EventMedia(path[..^".uasset".Length]))
                {
                    i++;
                    var (mono, rate) = Decode(wem, $"{key}_{i:00}");
                    var rel = $"kenny/sfx/{key}_{i:00}.wav";
                    Wav.Write(Path.Combine(addonDir, "sound", rel), Wav.Resample(mono, rate, 44100), 44100);
                    paths.Add($"\"{rel}\"");
                }
            lua.Append($"\t{key} = {{{string.Join(", ", paths)}}},\n");
        }
        lua.Append("}\n");
        var luaPath = Path.Combine(addonDir, "lua", "kenny_hol", "sh_vo_data.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(luaPath)!);
        File.WriteAllText(luaPath, lua.ToString());
        _log($"  audio: {total} voice lines + weapon sounds");
    }
}

/// <summary>16-bit PCM WAV read/write + linear resampling.</summary>
public static class Wav
{
    public static (short[] pcm, int rate, int channels) Read(string path)
    {
        var b = File.ReadAllBytes(path);
        int pos = 12, rate = 0, ch = 1, bits = 16;
        short[]? data = null;
        while (pos + 8 <= b.Length)
        {
            var id = Encoding.ASCII.GetString(b, pos, 4); int size = BitConverter.ToInt32(b, pos + 4);
            if (id == "fmt ") { ch = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
            else if (id == "data")
            {
                if (bits != 16) throw new Exception("expected 16-bit wav from vgmstream");
                size = Math.Min(size, b.Length - pos - 8);
                data = new short[size / 2];
                Buffer.BlockCopy(b, pos + 8, data, 0, data.Length * 2);
            }
            pos += 8 + size + (size & 1);
        }
        return (data ?? throw new Exception("no audio data"), rate, ch);
    }

    public static short[] Resample(short[] a, int from, int to)
    {
        if (from == to) return a;
        var n = (int)((long)a.Length * to / from);
        var o = new short[n];
        // average when shrinking (simple low-pass), interpolate when growing
        double step = (double)from / to;
        for (int i = 0; i < n; i++)
        {
            double s0 = i * step, s1 = s0 + step;
            if (step > 1)
            {
                int k0 = (int)s0, k1 = Math.Min((int)s1, a.Length);
                double sum = 0; int cnt = 0;
                for (int k = k0; k < k1; k++) { sum += a[k]; cnt++; }
                o[i] = (short)(cnt > 0 ? sum / cnt : 0);
            }
            else
            {
                int k = Math.Min((int)s0, a.Length - 1), k2 = Math.Min(k + 1, a.Length - 1); double t = s0 - (int)s0;
                o[i] = (short)(a[k] * (1 - t) + a[k2] * t);
            }
        }
        return o;
    }

    public static void Write(string path, short[] mono, int rate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var w = new BinaryWriter(File.Create(path));
        int bytes = mono.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(bytes);
        var buf = new byte[bytes]; Buffer.BlockCopy(mono, 0, buf, 0, bytes); w.Write(buf);
    }
}
