using System.Diagnostics;
using System.Reflection;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using KennyInstaller;

// Kenny (High on Life) for Garry's Mod - installer.
// Builds Kenny's model, expressions, textures and voice lines from YOUR High on Life install and puts the
// finished addon in garrysmod/addons/kenny_hol. Nothing from the game ships with this program.

Console.Title = "Kenny for Garry's Mod - installer";
var argList = args.ToList();
string? Arg(string name) { int i = argList.IndexOf(name); return i >= 0 && i + 1 < argList.Count ? argList[i + 1] : null; }
bool pause = !argList.Contains("--no-pause");
void Log(string s) => Console.WriteLine(s);

int code = 0;
try
{
    Log("Kenny (High on Life) for Garry's Mod");
    Log("====================================\n");

    var gmod = Arg("--gmod") ?? Steam.FindGame(Steam.GarrysMod)
        ?? throw new UserError("Couldn't find Garry's Mod. Install it through Steam, or run with --gmod \"<GarrysMod folder>\".");
    var gmodGame = Path.Combine(gmod, "garrysmod");
    var addonDir = Path.Combine(gmodGame, "addons", "kenny_hol");
    Log($"Garry's Mod:  {gmod}");

    if (argList.Contains("--uninstall"))
    {
        if (Directory.Exists(addonDir)) { Directory.Delete(addonDir, true); Log("\nRemoved " + addonDir); }
        else Log("\nKenny isn't installed.");
        return Finish(0);
    }

    var hol = Arg("--hol") ?? Steam.FindGame(Steam.HighOnLife)
        ?? throw new UserError("Couldn't find High on Life. You need it installed through Steam (Kenny is built from your copy), or run with --hol \"<HighOnLife folder>\".");
    var paks = Path.Combine(hol, "Oregon", "Content", "Paks");
    if (!Directory.Exists(paks)) throw new UserError($"High on Life's game files weren't found in {paks}.");
    Log($"High on Life: {hol}\n");

    var studiomdl = Path.Combine(gmod, "bin", "studiomdl.exe");
    if (!File.Exists(studiomdl)) throw new UserError($"Garry's Mod's model compiler is missing ({studiomdl}). Verify GMod's files in Steam.");
    if (Process.GetProcessesByName("gmod").Length + Process.GetProcessesByName("hl2").Length > 0)
        Log("Note: Garry's Mod is running. Restart it after installing to load Kenny.\n");

    var sw = Stopwatch.StartNew();
    Log("[1/6] Reading High on Life's game files...");
    var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE4_27));
    provider.Initialize();
    provider.Mount();
    if (!provider.Files.ContainsKey(ModelBuilder.MeshPath + ".uasset"))
        throw new UserError("Kenny wasn't found in your High on Life files. Is the game fully installed and up to date?");

    // fresh addon folder (only ever ours: we check its addon.json before deleting)
    if (Directory.Exists(addonDir))
    {
        var aj = Path.Combine(addonDir, "addon.json");
        if (!File.Exists(aj) || !File.ReadAllText(aj).Contains("Kenny (High on Life)"))
            throw new UserError($"{addonDir} exists but isn't this mod's folder; not touching it.");
        Directory.Delete(addonDir, true);
    }
    Directory.CreateDirectory(addonDir);
    var work = Path.Combine(Path.GetTempPath(), "KennyInstaller_" + Environment.ProcessId);
    Directory.CreateDirectory(work);

    Log("[2/6] Installing the addon's code...");
    WriteEmbeddedAddon(addonDir);

    Log("[3/6] Building Kenny's model and facial expressions...");
    var mb = new ModelBuilder(provider, Log);
    mb.Load();
    Directory.CreateDirectory(Path.Combine(addonDir, "lua", "kenny_hol"));
    mb.Write(work, Path.Combine(addonDir, "lua", "kenny_hol", "sh_face_data.lua"));
    File.WriteAllText(Path.Combine(work, "kenny.qc"), ReadResource("kenny.qc"));
    CompileModel(studiomdl, gmodGame, work, Path.Combine(addonDir, "models", "weapons", "kenny"));

    Log("[4/6] Converting textures...");
    var tb = new TextureBuilder(provider, Log);
    tb.Build(Path.Combine(addonDir, "materials", "models", "weapons", "kenny"));

    Log("[5/6] Rendering the spawn icon...");
    var (ipos, inrm, iuv, itris) = mb.Posed("happy");
    var texes = new Dictionary<string, Img>
    {
        ["kenny_body"] = tb.Load("Oregon/Content/Art/Characters/Kenny/Textures/Kenny_Body_D").Resized(1024),
        ["kenny_eyelids"] = tb.Load("Oregon/Content/Art/Characters/Kenny/Textures/Kenny_Eyelids_D"),
        ["kenny_chip"] = tb.Load("Oregon/Content/Art/Characters/Kenny/InhibitorChip/Kenny_InhibitorChip_D").Resized(1024),
        ["kenny_eye"] = tb.EyeTexture(),
    };
    IconRenderer.Render(Path.Combine(addonDir, "materials", "entities", "weapon_kenny.png"), ipos, inrm, iuv, itris, texes);

    Log("[6/6] Converting Kenny's voice lines (this is the slow part)...");
    var ab = new AudioBuilder(provider, Log, Path.Combine(work, "audio"));
    await ab.EnsureVgmstream();
    ab.Build(addonDir);

    try { Directory.Delete(work, true); } catch { /* temp files only */ }
    Log($"\nDone in {sw.Elapsed.TotalSeconds:0}s. Kenny is installed in:\n  {addonDir}\n");
    Log("Start Garry's Mod and find him under  Weapons > High on Life > Kenny.");
    Log("To remove him later, run this installer with --uninstall (or delete that folder).");
}
catch (UserError e) { Log("\n" + e.Message); code = 1; }
catch (Exception e) { Log("\nSomething went wrong:\n" + e); code = 2; }
return Finish(code);

int Finish(int c)
{
    if (pause) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
    return c;
}

static void CompileModel(string studiomdl, string gmodGame, string work, string modelOut)
{
    // studiomdl writes into garrysmod/models/...; move the result into the addon and tidy up after it
    var target = Path.Combine(gmodGame, "models", "weapons", "kenny");
    var existed = new[] { Path.Combine(gmodGame, "models"), Path.Combine(gmodGame, "models", "weapons"), target }
        .ToDictionary(d => d, Directory.Exists);
    var psi = new ProcessStartInfo(studiomdl, $"-nop4 -game \"{gmodGame}\" kenny.qc")
    { WorkingDirectory = work, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    using var p = Process.Start(psi)!;
    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (!output.Contains("Completed \"kenny.qc\""))
        throw new Exception("studiomdl failed:\n" + string.Join("\n", output.Split('\n').Where(l => l.Contains("ERROR") || l.Contains("WARNING")).Take(20)));
    Directory.CreateDirectory(modelOut);
    foreach (var f in Directory.GetFiles(target, "w_kenny.*"))
        File.Move(f, Path.Combine(modelOut, Path.GetFileName(f)), true);
    foreach (var d in existed.Keys.Reverse())
        if (!existed[d] && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
    Console.WriteLine("  model compiled with Garry's Mod's studiomdl");
}

static string ReadResource(string name)
{
    using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new Exception("missing resource " + name);
    return new StreamReader(s).ReadToEnd();
}

static void WriteEmbeddedAddon(string addonDir)
{
    var asm = Assembly.GetExecutingAssembly();
    foreach (var res in asm.GetManifestResourceNames().Where(r => r.StartsWith("addon/")))
    {
        var rel = res["addon/".Length..].Replace('\\', '/');
        var dest = Path.Combine(addonDir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var s = asm.GetManifestResourceStream(res)!;
        using var f = File.Create(dest);
        s.CopyTo(f);
    }
}

sealed class UserError(string message) : Exception(message);
