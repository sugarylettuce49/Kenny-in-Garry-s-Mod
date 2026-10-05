using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KennyInstaller;

/// <summary>Finds Steam game install folders (registry -> libraryfolders.vdf -> appmanifest_*.acf).</summary>
public static class Steam
{
    public const int HighOnLife = 1583230;
    public const int GarrysMod = 4000;

    public static string? FindGame(int appId)
    {
        foreach (var lib in Libraries())
        {
            var manifest = Path.Combine(lib, "steamapps", $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest)) continue;
            var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!m.Success) continue;
            var dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    static IEnumerable<string> Libraries()
    {
        var steam = SteamRoot();
        if (steam == null) yield break;
        yield return steam;
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    static string? SteamRoot()
    {
        if (!OperatingSystem.IsWindows()) return null;
        foreach (var (hive, key, value) in new[] {
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath") })
        {
            using var k = hive.OpenSubKey(key);
            if (k?.GetValue(value) is string p && Directory.Exists(p)) return p.Replace('/', '\\');
        }
        return null;
    }
}
