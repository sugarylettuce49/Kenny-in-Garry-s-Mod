# Copy the addon's hand-written files (lua, addon.json) into GMod. Generated assets (models, materials,
# sounds, lua/kenny_hol/sh_vo_data.lua) are written straight there by the other tools.
# -Test also arms the scripted test take (lua/kenny_hol/sv_test.lua).
param([switch]$Test, [string]$GMod = $env:KENNY_GMOD)
# -GMod <...\GarrysMod\garrysmod> or set KENNY_GMOD
if (-not $GMod) { throw "pass -GMod <GarrysMod\garrysmod folder> or set KENNY_GMOD" }
$gmod = $GMod
$dst = "$gmod\addons\kenny_hol"
Copy-Item -Recurse -Force "$PSScriptRoot\..\addon\*" $dst
if ($Test) { Set-Content -Path "$gmod\data\kenny_test.txt" -Value "1" -Encoding ascii }
elseif (Test-Path "$gmod\data\kenny_test.txt") { Remove-Item "$gmod\data\kenny_test.txt" }
Get-ChildItem -Recurse "$dst\lua" | Select-Object -ExpandProperty FullName
