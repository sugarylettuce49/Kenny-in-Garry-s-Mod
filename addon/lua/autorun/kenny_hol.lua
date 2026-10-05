-- Kenny (High on Life) for Garry's Mod: shared setup + the server-side "Kenny talks" brain.
AddCSLuaFile()
AddCSLuaFile("kenny_hol/sh_vo_data.lua")
include("kenny_hol/sh_vo_data.lua")
AddCSLuaFile("kenny_hol/sh_face_data.lua")
include("kenny_hol/sh_face_data.lua")

KENNY = KENNY or {}
KENNY.Model = "models/weapons/kenny/w_kenny.mdl"

-- how important a line is: a higher one may cut off a lower one that's still playing
KENNY.Priority = {
	death = 9, multikill = 8, headshot = 7, glob_kill = 7, env_kill = 7, kill = 6, refuse = 6,
	hurt_heavy = 5, hurt_fire = 5, hurt_shock = 5, fall = 5, stagger = 4, hurt = 4, hurt_other = 4,
	first_shot = 3, spot = 3, equip = 3, no_ammo = 3, glob_ready = 2, health = 2, taunt = 2,
	sprint = 1, combat_idle = 1, idle = 1,
}

if CLIENT then
	-- what Kenny is saying right now, per entity: drives the jaw (see weapon_kenny.lua)
	net.Receive("kenny_say", function()
		local ent, cat, idx = net.ReadEntity(), net.ReadString(), net.ReadUInt(8)
		local line = KENNY_VO[cat] and KENNY_VO[cat][idx]
		if not line then return end
		KENNY.Speaking = { ent = ent, start = CurTime(), dur = line[2], env = line[3], cat = cat }
	end)

	function KENNY.MouthOpen()
		local s = KENNY.Speaking
		if not s then return 0 end
		local t = CurTime() - s.start
		if t > s.dur then return 0 end
		local i = math.floor(t * 20) + 1
		local a, b = tonumber(s.env[i] or "0") or 0, tonumber(s.env[i + 1] or "0") or 0
		return Lerp(t * 20 % 1, a, b) / 9
	end
	return
end

util.AddNetworkString("kenny_say")
local talk = CreateConVar("kenny_talk", "1", FCVAR_ARCHIVE, "Let Kenny talk (0 = silent gun)")
local chatty = CreateConVar("kenny_chattiness", "1", FCVAR_ARCHIVE, "Multiplier on how often Kenny speaks up")

local state = {} -- per player: talking-until, last line per category, recent kills...
local function S(ply)
	state[ply] = state[ply] or { busyUntil = 0, busyPrio = 0, last = {}, kills = {}, lastIdx = {} }
	return state[ply]
end

local function holdingKenny(ply)
	if not IsValid(ply) or not ply:IsPlayer() then return end
	local w = ply:GetActiveWeapon()
	if IsValid(w) and w:GetClass() == "weapon_kenny" then return w end
end
KENNY.HoldingKenny = holdingKenny

-- Say a line from a category. chance (0-1) and cooldown (seconds per category) keep him from never shutting up.
function KENNY.Say(ply, cat, chance, cooldown, source)
	if not talk:GetBool() then return end
	local lines = KENNY_VO[cat]
	if not lines or #lines == 0 then return end
	local st, now = S(ply), CurTime()
	if chance and math.random() > math.min(1, chance * chatty:GetFloat()) then return end
	if cooldown and (st.last[cat] or -999) + cooldown / math.max(chatty:GetFloat(), 0.1) > now then return end
	local prio = KENNY.Priority[cat] or 1
	if now < st.busyUntil and prio <= st.busyPrio then return end
	source = source or holdingKenny(ply) or ply
	if now < st.busyUntil and IsValid(st.source) then st.source:StopSound(st.sound) end

	-- never the same line twice in a row
	local idx = math.random(#lines)
	if #lines > 1 and idx == st.lastIdx[cat] then idx = idx % #lines + 1 end
	st.lastIdx[cat] = idx

	local line = lines[idx]
	source:EmitSound(line[1], 80, 100, 1, CHAN_VOICE2)
	st.busyUntil, st.busyPrio, st.last[cat], st.source, st.sound = now + line[2] + 0.4, prio, now, source, line[1]

	net.Start("kenny_say")
	net.WriteEntity(source)
	net.WriteString(cat)
	net.WriteUInt(idx, 8)
	net.Broadcast()
	return true
end

-- hitgroup of the last bullet/glob that hit each NPC/player, for headshot lines
local lastHitgroup = {}
hook.Add("ScaleNPCDamage", "kenny_hitgroup", function(npc, hg) lastHitgroup[npc] = hg end)
hook.Add("ScalePlayerDamage", "kenny_hitgroup", function(ply, hg) lastHitgroup[ply] = hg end)

local function onKill(victim, attacker, inflictor)
	local ply = IsValid(attacker) and attacker:IsPlayer() and attacker
	if not ply or victim == ply or not holdingKenny(ply) then return end
	local st, now = S(ply), CurTime()
	st.combatUntil = now + 15
	table.insert(st.kills, now)
	while st.kills[1] and st.kills[1] < now - 4 do table.remove(st.kills, 1) end

	if #st.kills >= 3 and KENNY.Say(ply, "multikill", 1, 6) then return end
	if lastHitgroup[victim] == HITGROUP_HEAD and KENNY.Say(ply, "headshot", 0.8, 4) then return end
	if IsValid(inflictor) and inflictor:GetClass() == "kenny_glob" and inflictor:GetBig() and KENNY.Say(ply, "glob_kill", 0.9, 4) then return end
	if IsValid(inflictor) and inflictor:GetClass() == "prop_physics" and KENNY.Say(ply, "env_kill", 0.9, 4) then return end
	KENNY.Say(ply, "kill", 0.6, 2)
end
hook.Add("OnNPCKilled", "kenny_kill", function(npc, attacker, inflictor) onKill(npc, attacker, inflictor) end)
hook.Add("PlayerDeath", "kenny_kill", function(victim, inflictor, attacker)
	onKill(victim, attacker, inflictor)
	if holdingKenny(victim) or (IsValid(victim.KennyLastWeapon) and victim.KennyLastWeapon:GetClass() == "weapon_kenny") then
		S(victim).busyUntil = 0
		KENNY.Say(victim, "death", 0.9, 0, victim)
	end
end)
hook.Add("PlayerSwitchWeapon", "kenny_track", function(ply, old, new) ply.KennyLastWeapon = new end)

hook.Add("EntityTakeDamage", "kenny_hurt", function(ent, dmg)
	if not holdingKenny(ent) or dmg:GetDamage() <= 0 then return end
	local st = S(ent)
	st.combatUntil = CurTime() + 15
	if dmg:IsDamageType(DMG_FALL) then return KENNY.Say(ent, "fall", 0.9, 5) end
	if dmg:IsDamageType(DMG_BURN) or dmg:IsDamageType(DMG_ACID) then return KENNY.Say(ent, "hurt_fire", 0.6, 8) end
	if dmg:IsDamageType(DMG_SHOCK) then return KENNY.Say(ent, "hurt_shock", 0.8, 8) end
	if dmg:IsDamageType(DMG_POISON) or dmg:IsDamageType(DMG_RADIATION) or dmg:IsDamageType(DMG_DROWN) then
		return KENNY.Say(ent, "hurt_other", 0.6, 8)
	end
	if dmg:GetDamage() >= 30 then return KENNY.Say(ent, "hurt_heavy", 0.8, 5) end
	KENNY.Say(ent, "hurt", 0.35, 6)
end)

-- once a second: idle chatter, long sprints, health pickups, spotting enemies
timer.Create("kenny_brain", 1, 0, function()
	local now = CurTime()
	for _, ply in ipairs(player.GetAll()) do
		local w = holdingKenny(ply)
		if not w or not ply:Alive() then continue end
		local st = S(ply)
		st.idleAt = st.idleAt or now + math.random(40, 80)

		local hp = ply:Health()
		if st.hp and hp >= st.hp + 10 then KENNY.Say(ply, "health", 0.7, 20) end
		st.hp = hp

		if ply:IsSprinting() and ply:GetVelocity():Length2D() > 150 then
			st.sprint = (st.sprint or 0) + 1
			if st.sprint >= 8 then KENNY.Say(ply, "sprint", 0.5, 90) st.sprint = 0 end
		else
			st.sprint = 0
		end

		local tr = ply:GetEyeTrace()
		local e = tr.Entity
		if IsValid(e) and (e:IsNPC() or e:IsNextBot()) and e:Health() > 0 and tr.HitPos:Distance(ply:GetShootPos()) < 2500 then
			local hostile = not e.Disposition or e:Disposition(ply) == D_HT
			if hostile and (st.combatUntil or 0) < now then
				st.combatUntil = now + 15
				KENNY.Say(ply, "spot", 0.6, 40)
			end
		end

		if now >= st.idleAt then
			st.idleAt = now + math.random(40, 80)
			KENNY.Say(ply, (st.combatUntil or 0) > now and "combat_idle" or "idle", 0.8, 30)
		end
	end
end)

hook.Add("PlayerDisconnected", "kenny_cleanup", function(ply) state[ply] = nil end)

-- automated in-game test, only when data/kenny_test.txt exists (see tools/ for the harness)
if file.Exists("kenny_test.txt", "DATA") then include("kenny_hol/sv_test.lua") end
