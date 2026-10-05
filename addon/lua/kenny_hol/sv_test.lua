-- Scripted test take. Runs only when garrysmod/data/kenny_test.txt exists (written by tools/test.ps1).
-- Each step logs to data/kenny_test_log.txt and screenshots land in garrysmod/screenshots/.
local log = {}
local function L(...)
	local s = string.format("[%.2f] ", CurTime()) .. string.format(...)
	print("[kenny_test] " .. s)
	log[#log + 1] = s
	file.Write("kenny_test_log.txt", table.concat(log, "\n"))
end

local steps
local function shot(ply, name)
	L("SHOT %s", name)
	ply:ConCommand("jpeg_quality 90; jpeg")
end

hook.Add("PlayerInitialSpawn", "kenny_test", function(ply)
	timer.Simple(4, function()
		if not IsValid(ply) then return end
		ply:StripWeapons()
		ply:Give("weapon_kenny")
		ply:SelectWeapon("weapon_kenny")
		ply:SetHealth(500)
		L("vo categories: %d, sfx: %d", table.Count(KENNY_VO or {}), table.Count(KENNY_SFX or {}))
		local w = ply:GetActiveWeapon()
		L("active weapon: %s model %s", IsValid(w) and w:GetClass() or "none", IsValid(w) and w:GetModel() or "")
		local vm = ply:GetViewModel()
		local bones = {}
		for i = 0, vm:GetBoneCount() - 1 do bones[#bones + 1] = vm:GetBoneName(i) .. "<" .. vm:GetBoneParent(i) end
		L("vm bones: %s", table.concat(bones, ", "))

		local t = 0
		for _, step in ipairs(steps) do
			t = t + step[1]
			timer.Simple(t, function() if IsValid(ply) then step[2](ply) end end)
		end
	end)
end)

-- accuracy: how far each glob landed from where the crosshair pointed when it was fired
hook.Add("KennyGlobHit", "kenny_test", function(glob, pos, owner, ent)
	if not glob.AimPos then return end
	local want = (glob.AimPos - glob.Start):GetNormalized()
	L("glob %s hit %.1f units from crosshair point (range %.0f) hit=%s flew=%.0f dirErr=%.1fdeg start=%s pos=%s aim=%s",
		glob:GetBig() and "BIG" or "small", pos:Distance(glob.AimPos), glob.AimPos:Distance(glob.FiredFrom),
		IsValid(ent) and ent:GetClass() or (ent:IsWorld() and "world" or "?"), pos:Distance(glob.Start),
		math.deg(math.acos(math.Clamp(want:Dot(glob.Vel0:GetNormalized()), -1, 1))), tostring(glob.Start), tostring(pos), tostring(glob.AimPos))
end)
hook.Add("OnEntityCreated", "kenny_test", function(e)
	if e:GetClass() ~= "kenny_glob" then return end
	timer.Simple(0, function()
		local o = IsValid(e) and e:GetOwner()
		if IsValid(o) and o:IsPlayer() then e.AimPos, e.FiredFrom, e.Start, e.Vel0 = o:GetEyeTrace().HitPos, o:GetShootPos(), e:GetPos(), e:GetGlobVelocity() end
	end)
end)

local function spawnNPC(ply, class, dist, side, weapon)
	local fwd = ply:GetAimVector() fwd.z = 0 fwd:Normalize()
	local pos = ply:GetPos() + fwd * dist + fwd:Angle():Right() * side
	local tr = util.TraceLine({ start = pos + Vector(0, 0, 64), endpos = pos - Vector(0, 0, 256) })
	local npc = ents.Create(class)
	npc:SetPos(tr.HitPos + Vector(0, 0, 4))
	npc:SetAngles(Angle(0, (ply:GetPos() - pos):Angle().y, 0))
	if weapon then npc:SetKeyValue("additionalequipment", weapon) end
	npc:Spawn()
	npc:Activate()
	L("spawned %s at %s", class, tostring(npc:GetPos()))
	return npc
end

steps = {
	{ 6, function(ply) L("AUDIO_START") KENNY.Say(ply, "taunt", 1) end },
	{ 3, function(ply) KENNY.Say(ply, "kill", 1) end },
	{ 3, function(ply) ply:ConCommand("+attack") end },
	{ 1, function(ply) ply:ConCommand("-attack") L("AUDIO_END") end },
	-- empty the clip so R actually reloads, then catch frames across the reload animation
	{ 2.5, function(ply) local w = ply:GetActiveWeapon() if IsValid(w) then w:SetClip1(0) end ply:ConCommand("+reload") end },
	{ 0.1, function(ply) ply:ConCommand("-reload") end },
	{ 0.15, function(ply) shot(ply, "reload_a") end },
	{ 0.3, function(ply) shot(ply, "reload_b") end },
	{ 0.3, function(ply) shot(ply, "reload_c") end },
	{ 0.3, function(ply) shot(ply, "reload_d") end },
	{ 1.5, function(ply) ply:SetEyeAngles(Angle(12, ply:EyeAngles().y, 0)) ply:ConCommand("+attack") end },
	{ 0.6, function(ply) ply:ConCommand("-attack") end },
	{ 0.6, function(ply) ply:ConCommand("+attack2") end },
	{ 0.1, function(ply) ply:ConCommand("-attack2") end },
	{ 3, function(ply)
		ply:SetEyeAngles(Angle(5, ply:EyeAngles().y, 0))
		spawnNPC(ply, "npc_combine_s", 350, -60, "weapon_smg1")
		spawnNPC(ply, "npc_combine_s", 380, 60, "weapon_smg1")
		spawnNPC(ply, "npc_metropolice", 300, 0, "weapon_stunstick")
	end },
	{ 1.5, function(ply) ply:ConCommand("+attack") end },
	{ 0.25, function(ply) shot(ply, "firing") end },
	{ 1.2, function(ply) ply:ConCommand("-attack") end },
	{ 0.5, function(ply) ply:ConCommand("+attack2") end },
	{ 0.15, function(ply) ply:ConCommand("-attack2") end },
	{ 0.35, function(ply) shot(ply, "glob_shot") end },
	{ 0.5, function(ply) shot(ply, "glob_impact") end },
	{ 1.0, function(ply) ply:ConCommand("+attack") end },
	{ 2.5, function(ply) ply:ConCommand("-attack") end },
	{ 0.2, function(ply) shot(ply, "after_fight") end },
	{ 0.5, function(ply) ply:ConCommand("+reload") end },
	{ 0.1, function(ply) ply:ConCommand("-reload") end },
	{ 0.6, function(ply) shot(ply, "reloading") end },
	{ 4.2, function(ply)
		ply:ConCommand("thirdperson; cam_idealyaw 160; cam_idealdist 70")
		L("thirdperson")
	end },
	{ 1, function(ply) shot(ply, "third_person") ply:ConCommand("kenny_debug_vm; cam_idealyaw 215; cam_idealdist 60") end },
	{ 0.8, function(ply) shot(ply, "third_person_b") ply:ConCommand("cam_idealyaw 250; cam_idealdist 50; cam_idealpitch 10") end },
	{ 0.8, function(ply) shot(ply, "third_person_c") ply:ConCommand("cam_idealpitch 0") end },
	{ 0.5, function(ply)
		local n = 0
		for _, e in ipairs(ents.FindByClass("npc_*")) do if e:Health() > 0 then n = n + 1 end end
		L("npcs alive: %d", n)
		ply:ConCommand("firstperson; cam_idealyaw 0; cam_idealdist 150")
		L("DONE")
	end },
}
