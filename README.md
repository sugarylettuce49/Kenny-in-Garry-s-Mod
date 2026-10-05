# Kenny (High on Life) for Garry's Mod

Kenny, the talking glob pistol from *High on Life*, as a Garry's Mod weapon, with his real model, facial
expressions and voice.

- **Shoots like in the game:** rapid slime globs on left click. Right click is the **Glob Shot**, a lobbed
  blob that throws nearby enemies and props into the air (4 s recharge, shown under the crosshair).
- **He talks:** 514 of his voice lines play on kills, headshots, multikills, getting hurt, burning, falling,
  dying, sprinting, health pickups, spotting enemies, idle chatter, and when you press **R** with a full clip.
- **His face moves:** his jaw follows his voice, he blinks, and his mood changes with what he says (wicked on
  kills, angry when hurt, excited when he spots enemies), using his own High on Life expressions. At rest he
  wears his usual half-lidded smirk.
- **He won't shoot friendly NPCs.** He complains instead, like in the game.
- First person, third person and other players all see him in the hand.

## You need

- Windows
- **Garry's Mod** and **High on Life**, both installed through Steam

This repository and its releases contain **no High on Life files**. The installer builds Kenny on your PC from
*your own* copy of the game.

## Install

1. Download `KennyInstaller.exe` from the [latest release](../../releases/latest).
2. Double-click it. It finds both games through Steam, reads Kenny's model, textures, expressions and voice
   from your High on Life install, compiles the model with Garry's Mod's own `studiomdl`, and installs the
   addon into `garrysmod/addons/kenny_hol`. It takes about a minute.
3. Start Garry's Mod. Kenny is under **Weapons → High on Life → Kenny**.

On first run the installer downloads [vgmstream](https://github.com/vgmstream/vgmstream) (an open-source
audio decoder) to convert the game's Wwise audio. It's cached in `%LOCALAPPDATA%\KennyInstaller`.

If your games aren't found automatically, pass the folders yourself:

```
KennyInstaller.exe --hol "<Steam library>\steamapps\common\HighOnLife" --gmod "<Steam library>\steamapps\common\GarrysMod"
```

Windows SmartScreen may warn about an unsigned app. Choose **More info → Run anyway**, or build it from
source (below).

## Uninstall

Run `KennyInstaller.exe --uninstall`, or delete `garrysmod/addons/kenny_hol`.

## Multiplayer

Everyone who wants to see the real Kenny needs to own High on Life and run the installer. Players without it
see an error model in his place. The weapon code itself works on any server that has the addon.

## Settings (console)

| Command | What it does |
|---|---|
| `kenny_talk 0` / `1` | mute / unmute Kenny (server) |
| `kenny_chattiness 1` | how often he speaks up, as a multiplier (server) |
| `kenny_idle_face happy` | his resting expression: `happy` `sarcastic` `wicked` `angry` `excited` `neutral` |
| `kenny_vm_x/y/z`, `kenny_vm_p/yaw/r`, `kenny_vm_scale` | nudge where he sits in your hand |

## Troubleshooting

- **"Couldn't find …"**: run with `--hol` / `--gmod` as above.
- **Kenny shows as an ERROR model**: run the installer again, then restart Garry's Mod.
- **After a High on Life update**: run the installer again.
- Anything else: open an issue with the installer's output text.

## Build from source

- Installer: .NET 10 SDK, then `dotnet publish installer -c Release -o dist`. It uses
  [CUE4Parse](https://github.com/FabianFG/CUE4Parse) to read the game files and
  [BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) for textures.
  `installer/CUE4Parse-Natives.dll` is CUE4Parse's native helper built with ACL only (no Oodle), from
  `CUE4Parse-Natives` with CMake.
- Addon code: `addon/`. The installer embeds it.
- `tools/legacy/` holds the original Blender/Python pipeline the installer was ported from, kept for
  reference. `docs/DEVLOG.md` is the development journal: every coordinate convention and gotcha.

## Credits

- **Kenny, High on Life**, and all of the character's art, animation and voice belong to **Squanch Games**.
  This is an unofficial fan project, not affiliated with or endorsed by Squanch Games, Facepunch Studios or
  Valve. Please buy the game.
- [CUE4Parse](https://github.com/FabianFG/CUE4Parse) (Apache-2.0), with
  [ACL](https://github.com/nfrechette/acl) (MIT) for animation decompression
- [BCnEncoder.Net](https://github.com/Nominom/BCnEncoder.NET) (MIT/Unlicense) for DXT texture compression
- [vgmstream](https://github.com/vgmstream/vgmstream) (ISC-style license) for Wwise audio, downloaded at install time
- Garry's Mod by Facepunch Studios; the Source engine and `studiomdl` by Valve (not included; the installer uses
  your own copy)

**AI disclosure:** this mod (the Lua weapon, the asset pipeline, the installer and this README) was built by
Claude Code (Anthropic's Claude Opus 5.5) working with the repository owner, who directed, play-tested and
reviewed it.

## License

The code in this repository is released into the public domain under the Unlicense (see `LICENSE`): no
copyright is claimed on it. That covers only this code, not anything from High on Life.
