# MODLOG - Kenny (High on Life) -> Garry's Mod

## Goal
Kenny (the talking pistol from High on Life) as a Garry's Mod SWEP: his model, his voice lines, glob-shot + charged shot.
Done = spawnable from the Q menu (Weapons > High on Life), shoots, talks, and has been verified in game.

## Installs
- GMod: <Steam library>\steamapps\common\GarrysMod (appid 4000, Source 1, no anti-cheat). Has bin\studiomdl.exe.
- High on Life: <Steam library>\steamapps\common\HighOnLife (appid 1583230, UE4, project "oregon")
  - single pak oregon\Content\Paks\Oregon-WindowsNoEditor.pak (45.7 GB), pak version 11, index NOT encrypted (zero key guid)
- Tools: Blender 5.2 / 4.5, Python 3.14, dotnet, ffmpeg.

## Route
1. Extract Kenny assets from the user's own pak with CUE4Parse (personal use only; never redistribute).
2. Blender: glTF/psk -> SMD, compile with GMod studiomdl (.qc) -> models/weapons/v_/w_ kenny.
3. Textures -> VTF; Wwise .wem -> wav/ogg via vgmstream.
4. Lua SWEP addon in garrysmod/addons/kenny_hol/.
Reason: GMod Lua addons are the native route; no loader needed.

## Log
- kb: nothing on GMod or High on Life yet.
- 2026-10-05: DONE and verified in game (gm_construct, scripted take via data/kenny_test.txt + lua/kenny_hol/sv_test.lua).

## Pipeline (re-run order)
1. a small CUE4Parse 1.2.2 console tool (now the installer), EGame.GAME_UE4_27): `HolX export <out> <assets>` (glTF mesh / PNG),
   `HolX audio <regex> <out>` (Wwise event -> MediaList -> AkMediaAssetData chunks = .wem).
2. tools/legacy/glb_to_smd.py (Blender 5.2) -> a scratch folder; studiomdl with tools/kenny.qc (writes to garrysmod/models -> move into addon!).
3. tools/build_textures.py, tools/build_audio.py (vgmstream-cli), tools/render_icon.py -> straight into addons/kenny_hol.
4. tools/sync.ps1 (run with -ExecutionPolicy Bypass; -Test arms the scripted take).

## Gotchas
1. HoL Wwise audio lives inside the pak (WwiseAudio/Localized/English_US_/Media/*.ubulk), no loose .wem/.bnk.
2. Kenny skeleton = 229 bones incl. the player's arms; Source max 128. Fold lip/eyeOrbit/eyelid sub-joints + drop clavicle chains -> 59.
3. Jaw/eyelids hinge on bone-local Z = yaw in ManipulateBoneAngles. +22 yaw opens the jaw. Bind pose = eyes SHUT:
   upper lids rest at yaw -62 (lower +31), blink returns toward 0. Same sign as Blender pose-bone Z.
4. studiomdl turns the SMD 90 degrees: barrel ends up on model +Y. VM yaw -72 / world yaw +90 compensate.
5. ClientsideModel moved each frame MUST call InvalidateBoneCache() before SetupBones, or it stays frozen at frame-1 transform.
6. ManipulateBoneScale / BuildBonePositions on c_pistol do not hide the gun reliably; vm:SetMaterial("models/effects/vol_light001")
   in Pre/PostDrawViewModel does (hands are a separate entity). render.MaterialOverride leaks onto hands - don't.
7. MOVETYPE_FLY + SetVelocity globs drifted ~140u off the crosshair; per-tick TraceHull movement in Think is exact (2-10u at 335u).
8. Eyeball material is procedural: IrisScale 0.19 / PupilScale 0.32 around UV centre; compositing the mask at full scale = black eyes.
9. Kenny VM follows c_pistol's "ValveBiped.square" bone: barrel = bone Up, gun top = bone -Right (measure at idle,
   not mid-draw). Grip centre (model 0,0.1,1.85) goes to the magazine's REST spot = square + (-2.28 fwd, 0.13 right,
   -2.94 up). Don't follow the "clip" bone itself: it drops out of the gun during reload.
10. RunConsoleCommand("sv_cheats") is blocked from Lua; pass +sv_cheats 1 on the command line.
11. Black pixel chunks near NPC blood hits come from another installed addon's blood effects, not this mod (no missing materials logged).

12. glb_to_smd bug (fixed): scaling both bone matrices before taking parent-relative transforms cancels the scale ->
    skeleton 39x too small. Mesh still looks right (verts are model space) but every bone rotation pivots near the
    grip. Compute relative in metres, then scale only the translation.
13. GMod `jpeg` runs at the end of the client frame: a convar change queued in the same step lands in the shot.
    Give every screenshot its own step.

## Not shippable
Model, textures and voice are ripped from the user's own High on Life install: personal use only. Do NOT upload to the Workshop.

## Look pass vs. in-game screenshots (2026-10-05)
- First-person pose kept as before (yaw 0, scale 0.85, no head turn): user prefers GMod framing; only the face matches HoL.
- Idle face: jaw rest -8 (closed lips), smirk = ManipulateBonePosition on 8 kept mouth-corner bones
  (upperLip_01/03/43/45, lowerLip_03/05/33/35; 67 bones total), fades out while talking.
- Eyes: lids rest at -40 (half-lidded), blink +40; pupils turned outward 12 (eye bone pitch = left/right);
  eye texture = pale sclera, small pupil, faint iris.
- Glob visuals leave Kenny's barrel tip in first person: VM bone pos rescaled by tan(fov/2)/tan(vmfov/2), blended onto
  the real (crosshair-traced) path over 0.12 s; trail drawn client-side.

## Real expressions (2026-10-05)
- Animations need CUE4Parse-Natives.dll (ACL): not in the NuGet package. Built from github.com/FabianFG/CUE4Parse
  (CUE4Parse-Natives, CMake + VS 18 2026, x64 Release) and copied next to HolX.exe.
- .psa from CUE4Parse: ANIMKEYS are mirrored along Y vs the BONENAMES ref pose -> key pos (x,-y,z), quat (x,-y,z,w).
  First-person anims also move the whole gun: re-anchor every frame on gun_root_JNT. (tools/psa_pose.py)
- Retarget onto the glTF rig in model space: posed = C @ (pose_comp @ rest_comp^-1) @ C^-1 @ rest_blender, C = affine fit
  of the two rigs' rest joint positions (error ~1e-7).
- Expressions baked as flexes (kenny.vta, face bones only): neutral, sarcastic, happy, wicked, angry, excited
  (KennyV2_emotions_*, middle frame) + blink (lid verts back to bind = shut) + lipsclosed (rig's lipsclosed_BS key).
  QC: `$model ... {` with flexfile/flexcontroller/%name lines; `{` must be on the same line.
- Lua: mood per voice category (kill -> wicked, hurt -> angry, spot -> excited, taunt/idle -> sarcastic), blends back
  to neutral 2.5 s after the line. Hand-made smirk/lid/jaw-rest hacks removed.
- Blender gotcha: evaluated_get().to_mesh() frees the previous to_mesh result of that object -> use
  bpy.data.meshes.new_from_object() for a mesh you keep.
- Third person: grip centre on the player's anim_attachment_RH (forward = aim, up = up), model yawed -90.
- Eyelids/eyeballs must NOT be flexes: flex = straight-line vertex motion, a lid swinging round the eyeball cuts
  through it at any partial weight (blink, expression blends). They're excluded from the flexes; per-expression bone
  rotations (Blender matrix_basis -> Euler XYZ -> Angle(deg y, deg z, deg x)) go to lua/kenny_hol/sh_face_data.lua
  (`--face-lua`), blended by expression weight in Lua; blink scales the lid angles toward 0 (bind = shut).
- Eyes: user picked the wide 'excited' eye pose for all moods (kenny_eyes convar); moods only drive mouth/brow flexes.
- Correction: eyes follow the mood again (user liked the transitions). Only the idle "neutral" eye pose put the lids in
  the eyeballs; idle now borrows the "happy" eyes (the "Here I am" equip line's look) via kenny_idle_eyes_v2.
- Third person "always sleeping": the SWEP entity's bone/flex state is networked from the server and overwrote the
  client-side face every update. World model is now a ClientsideModel copy (KennyWM) like the viewmodel.
- kenny_debug_tpcam 1: over-the-shoulder debug camera on the hand attachment (thirdperson cam was unreliable).
- Resting face = full "happy" expression (flex + eyes), per user's reference screenshot; kenny_idle_face convar. Moods still switch on voice lines.
- Glob start: VM barrel tip -> ToScreen() in the viewmodel render -> util.AimVector with the world FOV at the same depth (the tan-ratio FOV rescale was off and spawned globs at his cheek).
- Glob visual: server spawns at eye + aim*26 + right*3 - up*2; client offset from the barrel tip fades linearly over 300 u of flight (straight line tip->crosshair; the old 0.12 s time blend swept across his face). kenny_debug_slowglob 1 = 250 u/s globs for checking.
- Muzzle: green flash sprite at the VM barrel tip (IgnoreZ, 0.07 s) timed from networked LastShot/GlobReadyAt
  (single player: attack code runs server-side only); glob held at the tip for 0.025 s before flying.
- SWEP:FireAnimationEvent returns true: kills c_pistol's orange muzzle flash + brass shell events.
