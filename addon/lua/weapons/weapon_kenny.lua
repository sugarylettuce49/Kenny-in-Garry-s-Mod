-- Kenny, the talking glob pistol from High on Life.
-- Primary: rapid slime globs. Secondary: Glob Shot, a lobbed blob that launches everything near it into the air.
AddCSLuaFile()

SWEP.PrintName = "Kenny"
SWEP.Author = "Squanch Games (character) / ported for GMod"
SWEP.Category = "High on Life"
SWEP.Purpose = "He's a gun. He talks. A lot."
SWEP.Instructions = "Primary: glob shot. Secondary: Glob Shot (launches enemies). Reload: reload / make him talk."
SWEP.Spawnable = true
SWEP.AdminOnly = false
SWEP.Slot = 1
SWEP.SlotPos = 3
SWEP.DrawAmmo = true
SWEP.DrawCrosshair = true
SWEP.UseHands = true
SWEP.ViewModel = "models/weapons/c_pistol.mdl"
SWEP.WorldModel = "models/weapons/kenny/w_kenny.mdl"
SWEP.ViewModelFOV = 62
SWEP.HoldType = "pistol"

SWEP.Primary.ClipSize = 16
SWEP.Primary.DefaultClip = 16
SWEP.Primary.Automatic = true
SWEP.Primary.Ammo = "none"
SWEP.Primary.Damage = 14
SWEP.Primary.Delay = 0.16

SWEP.Secondary.ClipSize = -1
SWEP.Secondary.DefaultClip = -1
SWEP.Secondary.Automatic = false
SWEP.Secondary.Ammo = "none"
SWEP.Secondary.Cooldown = 4

SWEP.ReloadTime = 1.4

function SWEP:SetupDataTables()
	self:NetworkVar("Float", 0, "GlobReadyAt")
	self:NetworkVar("Float", 1, "ReloadDoneAt")
	self:NetworkVar("Float", 2, "LastShot")
end

function SWEP:Initialize()
	self:SetHoldType(self.HoldType)
end

local function sfx(key)
	local t = KENNY_SFX and KENNY_SFX[key]
	return t and t[math.random(#t)]
end

function SWEP:Deploy()
	self:SendWeaponAnim(ACT_VM_DRAW)
	self:SetNextPrimaryFire(CurTime() + 0.5)
	self:SetReloadDoneAt(0)
	if SERVER then
		local s = sfx("draw")
		if s then self:EmitSound(s, 70, 100, 0.8, CHAN_ITEM) end
		local owner = self:GetOwner()
		timer.Simple(0.3, function()
			if IsValid(self) and IsValid(owner) and owner:GetActiveWeapon() == self then KENNY.Say(owner, "equip", 0.7, 20) end
		end)
	end
	return true
end

function SWEP:Holster()
	self:SetReloadDoneAt(0)
	if SERVER then
		local s = sfx("holster")
		if s then self:EmitSound(s, 65, 100, 0.7, CHAN_ITEM) end
	end
	return true
end

-- Kenny won't shoot people who are nice to you (he refuses in the game too)
function SWEP:AimingAtFriend()
	local owner = self:GetOwner()
	local e = owner:GetEyeTrace().Entity
	return IsValid(e) and e:IsNPC() and e.Disposition and e:Disposition(owner) == D_LI
end

function SWEP:ShootGlob(big, bloom)
	local owner = self:GetOwner()
	if CLIENT then return end
	-- the glob leaves Kenny's barrel (right/below the eye) but flies at whatever is under the crosshair
	local aim = owner:GetAimVector()
	local right, up = owner:EyeAngles():Right(), owner:EyeAngles():Up()
	local tr = util.TraceLine({ start = owner:GetShootPos(), endpos = owner:GetShootPos() + aim * 16384, filter = owner, mask = MASK_SHOT })
	local start = owner:GetShootPos() + right * 3 - up * 2 + aim * 26
	-- too close to converge (hugging a wall): fire straight from the eye instead
	if tr.HitPos:DistToSqr(owner:GetShootPos()) < 48 * 48 then start = owner:GetShootPos() end
	local dir = (tr.HitPos - start):GetNormalized()
	-- a little bloom only while holding the trigger; the first shot is dead on
	if bloom then
		dir = (dir + VectorRand() * 0.006):GetNormalized()
	end

	local glob = ents.Create("kenny_glob")
	glob:SetPos(start)
	glob:SetOwner(owner)
	glob:SetBig(big)
	glob.Damage = big and 25 or self.Primary.Damage
	glob:Spawn()
	local speed = big and 1600 or 3500
	if GetConVar("kenny_debug_slowglob"):GetBool() then speed = 250 end
	local vel = dir * speed
	if big then
		-- the Glob Shot lobs under (reduced) gravity: aim it up just enough to land on the crosshair
		local t = math.min(tr.HitPos:Distance(start), 3000) / speed
		vel.z = vel.z + 0.5 * GetConVar("sv_gravity"):GetFloat() * KENNY_GLOB_GRAVITY * t
	end
	glob:Launch(vel)
end

function SWEP:PrimaryAttack()
	if self:GetReloadDoneAt() > 0 then return end
	local owner = self:GetOwner()
	if self:Clip1() <= 0 then
		self:SetNextPrimaryFire(CurTime() + 0.4)
		self:EmitSound("Weapon_Pistol.Empty")
		if SERVER then KENNY.Say(owner, "no_ammo", 0.6, 10) end
		self:Reload()
		return
	end
	if self:AimingAtFriend() then
		self:SetNextPrimaryFire(CurTime() + 0.8)
		if SERVER then KENNY.Say(owner, "refuse", 1, 3) end
		return
	end
	self:SetNextPrimaryFire(CurTime() + self.Primary.Delay)
	if SERVER and CurTime() - self:GetLastShot() > 20 then KENNY.Say(owner, "first_shot", 0.5, 30) end
	local bloom = CurTime() - self:GetLastShot() < self.Primary.Delay * 2
	self:SetLastShot(CurTime())
	self:TakePrimaryAmmo(1)
	self:SendWeaponAnim(ACT_VM_PRIMARYATTACK)
	owner:SetAnimation(PLAYER_ATTACK1)
	owner:ViewPunch(Angle(-0.8, math.Rand(-0.3, 0.3), 0))
	local s = sfx("fire")
	if s then self:EmitSound(s, 80, math.random(97, 103), 1, CHAN_WEAPON) end
	self:ShootGlob(false, bloom)
end

function SWEP:SecondaryAttack()
	if CurTime() < self:GetGlobReadyAt() or self:GetReloadDoneAt() > 0 then return end
	local owner = self:GetOwner()
	self:SetGlobReadyAt(CurTime() + self.Secondary.Cooldown)
	self:SetNextSecondaryFire(CurTime() + self.Secondary.Cooldown)
	self:SetNextPrimaryFire(CurTime() + 0.35)
	self:SendWeaponAnim(ACT_VM_PRIMARYATTACK) -- c_pistol has no secondary animation
	owner:SetAnimation(PLAYER_ATTACK1)
	owner:ViewPunch(Angle(-3, 0, 0))
	local s = sfx("glob_fire")
	if s then self:EmitSound(s, 85, 100, 1, CHAN_WEAPON) end
	self:ShootGlob(true)
	self.GlobAnnounced = false
end

function SWEP:Reload()
	if self:GetReloadDoneAt() > 0 then return end
	if self:Clip1() >= self.Primary.ClipSize then
		-- full mag: R pokes Kenny into saying something
		if SERVER and (self.NextTaunt or 0) < CurTime() then
			self.NextTaunt = CurTime() + 1.5
			KENNY.Say(self:GetOwner(), "taunt", 1, 2)
		end
		return
	end
	self:SendWeaponAnim(ACT_VM_RELOAD)
	self:GetOwner():SetAnimation(PLAYER_RELOAD)
	self:SetReloadDoneAt(CurTime() + self.ReloadTime)
	local s = sfx("reload")
	if SERVER and s then self:EmitSound(s, 70, 100, 0.8, CHAN_ITEM) end
end

function SWEP:Think()
	local done = self:GetReloadDoneAt()
	if done > 0 and CurTime() >= done then
		self:SetClip1(self.Primary.ClipSize)
		self:SetReloadDoneAt(0)
		self:SendWeaponAnim(ACT_VM_IDLE)
	end
	if SERVER and not self.GlobAnnounced and CurTime() >= self:GetGlobReadyAt() then
		self.GlobAnnounced = true
		if self:GetGlobReadyAt() > 0 then KENNY.Say(self:GetOwner(), "glob_ready", 0.25, 30) end
	end
end

if SERVER then return end

------------------------------------------------------------------------------------------------
-- Client: draw the real Kenny model in the hand (the c_pistol arms animate, the pistol is hidden)
------------------------------------------------------------------------------------------------
local cv = {
	-- fine-tuning on top of "Kenny's grip where the pistol's magazine is": offsets along the gun's
	-- forward / right / up in units, then extra pitch / yaw / roll in degrees
	x = CreateClientConVar("kenny_vm_x", "0", false), y = CreateClientConVar("kenny_vm_y", "0", false),
	z = CreateClientConVar("kenny_vm_z", "0", false), p = CreateClientConVar("kenny_vm_p", "0", false),
	yaw = CreateClientConVar("kenny_vm_yaw", "0", false), r = CreateClientConVar("kenny_vm_r", "0", false),
	scale = CreateClientConVar("kenny_vm_scale", "0.85", false),
	showpistol = CreateClientConVar("kenny_debug_showpistol", "0", false),
	closeup = CreateClientConVar("kenny_debug_closeup", "0", false),
	showmuzzle = CreateClientConVar("kenny_debug_muzzle", "0", false),
	closedist = CreateClientConVar("kenny_debug_closeup_dist", "9", false),
	head = CreateClientConVar("kenny_head_tilt", "0", false),
	headturn = CreateClientConVar("kenny_head_turn", "0", false),
	expr = CreateClientConVar("kenny_debug_expr", "", false),
	debugblink = CreateClientConVar("kenny_debug_blink", "-1", false),
	idleface = CreateClientConVar("kenny_idle_face", "happy", true, false, "Kenny's resting expression: happy sarcastic wicked angry excited neutral"),
}
-- centre of Kenny's grip in model space (studiomdl put his barrel on +Y, up is +Z)
local GRIP_CENTER = Vector(0, 0.1, 1.85)
-- where c_pistol's magazine sits relative to its body bone at rest (gun forward / right / up), measured in game
local MAG_OFFSET = Vector(-2.28, 0.13, -2.94)

function SWEP:GetKennyVM()
	if not IsValid(self.KennyVM) then
		self.KennyVM = ClientsideModel(self.WorldModel, RENDERGROUP_OPAQUE)
		self.KennyVM:SetNoDraw(true)
	end
	return self.KennyVM
end

-- third-person Kenny is a clientside copy too: the weapon entity's own bone/flex state is networked from the
-- server and would snap his face back to the bind pose (eyes shut) every update
function SWEP:GetKennyWM()
	if not IsValid(self.KennyWM) then
		self.KennyWM = ClientsideModel(self.WorldModel, RENDERGROUP_OPAQUE)
		self.KennyWM:SetNoDraw(true)
	end
	return self.KennyWM
end

function SWEP:OnRemove()
	if IsValid(self.KennyVM) then self.KennyVM:Remove() end
	if IsValid(self.KennyWM) then self.KennyWM:Remove() end
end

-- his mood follows what he's saying: High on Life's own expressions, blended as flexes
local MOOD = {
	kill = "wicked", headshot = "wicked", glob_kill = "wicked", env_kill = "wicked", multikill = "happy",
	health = "happy", equip = "happy",
	hurt = "angry", hurt_heavy = "angry", hurt_fire = "angry", hurt_shock = "angry", hurt_other = "angry",
	fall = "angry", death = "angry", no_ammo = "angry", refuse = "angry",
	spot = "excited", first_shot = "excited", glob_ready = "excited", stagger = "excited", sprint = "excited",
	taunt = "sarcastic", idle = "sarcastic", combat_idle = "sarcastic",
}
local EXPRESSIONS = { "neutral", "sarcastic", "happy", "wicked", "angry", "excited" }

function SWEP:SetFlex(ent, name, w)
	ent.KennyFlex = ent.KennyFlex or {}
	local id = ent.KennyFlex[name]
	if id == nil then
		id = ent:GetFlexIDByName(name) or false
		ent.KennyFlex[name] = id
	end
	if id then ent:SetFlexWeight(id, w) end
	return id
end

-- expressions whose mouth stays sealed when he isn't talking (wicked shows his teeth on purpose)
local LIPS_SEALED = { neutral = true, sarcastic = true, happy = true, angry = true, excited = true }

function SWEP:AnimateExpression(ent)
	ent.KennyFlex = ent.KennyFlex or {}
	-- resting face (kenny_idle_face): his relaxed half-lidded smirk is the "happy" expression
	local want = cv.idleface:GetString()
	if not KENNY_FACE or not KENNY_FACE[want] then want = "happy" end
	local s = KENNY.Speaking
	if s and (s.ent == self or s.ent == self:GetOwner()) and CurTime() - s.start < s.dur + 2.5 then
		want = MOOD[s.cat] or want
	end
	local forced = cv.expr:GetString()
	if forced ~= "" then want = forced end
	local step = math.min(FrameTime() * 6, 1)
	for _, name in ipairs(EXPRESSIONS) do
		local id = ent.KennyFlex[name]
		if id == nil then
			id = ent:GetFlexIDByName(name) or false
			ent.KennyFlex[name] = id
		end
		ent.KennyExprWeights = ent.KennyExprWeights or {}
		local wnow = Lerp(step, ent.KennyExprWeights[name] or (name == want and 1 or 0), name == want and 1 or 0)
		ent.KennyExprWeights[name] = wnow
		if id then ent:SetFlexWeight(id, wnow) end
	end
	local sealed = LIPS_SEALED[want] and math.Clamp(1 - self.Mouth * 4, 0, 1) or 0
	ent.KennyLips = Lerp(step, ent.KennyLips or 0, sealed)
	self:SetFlex(ent, "lipsclosed", ent.KennyLips)
end

-- face: jaw follows the voice loudness, eyelids blink, antennae wobble
local blinkAt, blinkPhase = 0, 0
function SWEP:AnimateFace(ent)
	ent.KennyBones = ent.KennyBones or {
		jaw = ent:LookupBone("gun_jaw_JNT"), tongue = ent:LookupBone("gun_tongue_01_JNT"),
		antL = ent:LookupBone("gun_antena_01_l_JNT"), antR = ent:LookupBone("gun_antena_01_r_JNT"),
		head = ent:LookupBone("gun_head_JNT"),
	}
	local b, t = ent.KennyBones, CurTime()
	local mouth = 0
	local s = KENNY.Speaking
	if s and (s.ent == self or s.ent == self:GetOwner()) then mouth = KENNY.MouthOpen() end
	self.Mouth = Lerp(math.min(FrameTime() * 25, 1), self.Mouth or 0, mouth)
	if b.jaw then ent:ManipulateBoneAngles(b.jaw, Angle(0, 22 * self.Mouth, 0)) end
	self:AnimateExpression(ent)
	if b.tongue then ent:ManipulateBoneAngles(b.tongue, Angle(0, 8 * self.Mouth, 0)) end

	-- eyes wander a little (pitch = look left/right, yaw = up/down in the eye bones' frame)
	local side, vert = math.sin(t * 0.7) * 4, math.sin(t * 0.43) * 3
	if cv.closeup:GetBool() then side, vert = 0, 0 end
	-- eyelids + eyeballs are bone rotations from the game's animation. A blink rotates the lids back toward the
	-- bind pose (where they're shut) - rotating, so they follow the eyeball
	if t > blinkAt then blinkAt, blinkPhase = t + math.Rand(2, 5), t end
	local blink = math.max(0, 1 - math.abs(t - blinkPhase - 0.09) / 0.09)
	if cv.debugblink:GetFloat() >= 0 then blink = cv.debugblink:GetFloat() end
	-- the eyes follow his mood (blended with the expression weights)
	local face = {}
	for expr, w in pairs(ent.KennyExprWeights or {}) do
		for bone, ang in pairs(KENNY_FACE and (KENNY_FACE[expr] or KENNY_FACE.excited) or {}) do
			face[bone] = (face[bone] or Angle(0, 0, 0)) + ang * w
		end
	end
	ent.KennyFaceBones = ent.KennyFaceBones or {}
	for bone, ang in pairs(face) do
		local id = ent.KennyFaceBones[bone]
		if id == nil then
			id = ent:LookupBone(bone) or false
			ent.KennyFaceBones[bone] = id
		end
		if id then
			if bone:find("eyelid", 1, true) then ang = ang * (1 - blink) end
			if bone:find("gun_eye_", 1, true) then ang = ang + Angle(side, vert, 0) end
			ent:ManipulateBoneAngles(id, ang)
		end
	end

	-- like in the game, he turns his head back toward you (first person only)
	if b.head then
		if ent == self.KennyWM then ent:ManipulateBoneAngles(b.head, angle_zero)
		else ent:ManipulateBoneAngles(b.head, Angle(cv.headturn:GetFloat(), cv.head:GetFloat(), 0)) end
	end

	local wob = math.sin(t * 3) * 4 + self.Mouth * 6
	if b.antL then ent:ManipulateBoneAngles(b.antL, Angle(wob, 0, 0)) end
	if b.antR then ent:ManipulateBoneAngles(b.antR, Angle(-wob, 0, 0)) end
end

-- hide the HL2 pistol: the c_ viewmodel holds only the gun mesh (the arms are a separate hands entity),
-- so drawing it with a fully transparent material leaves the hands alone
function SWEP:PreDrawViewModel(vm)
	if not cv.showpistol:GetBool() then vm:SetMaterial("models/effects/vol_light001") end
end

concommand.Add("kenny_debug_vm", function(ply)
	local vm = ply:GetViewModel()
	local ea = ply:EyeAngles()
	local f, r, u = ea:Forward(), ea:Right(), ea:Up()
	for _, name in ipairs({ "ValveBiped.square", "ValveBiped.clip", "ValveBiped.Bip01_R_Hand" }) do
		local m = vm:GetBoneMatrix(vm:LookupBone(name))
		if not m then continue end
		local a = m:GetAngles()
		local rel = m:GetTranslation() - ply:EyePos()
		print(string.format("[kenny_dbg] %s pos(eye f/r/u)=%.2f %.2f %.2f | boneF.(f,r,u)=%.2f %.2f %.2f | boneR=%.2f %.2f %.2f | boneU=%.2f %.2f %.2f",
			name, rel:Dot(f), rel:Dot(r), rel:Dot(u), a:Forward():Dot(f), a:Forward():Dot(r), a:Forward():Dot(u),
			a:Right():Dot(f), a:Right():Dot(r), a:Right():Dot(u), a:Up():Dot(f), a:Up():Dot(r), a:Up():Dot(u)))
	end
	-- Kenny's barrel is the model's +Y axis (= -Right of the entity angles)
	local w = ply:GetActiveWeapon()
	local k = IsValid(w) and w.KennyVM
	if IsValid(k) then
		local rel, barrel = k:GetPos() - ply:EyePos(), -k:GetAngles():Right()
		print(string.format("[kenny_dbg] kenny pos(eye f/r/u)=%.2f %.2f %.2f barrel.(f,r,u)=%.2f %.2f %.2f", rel:Dot(f), rel:Dot(r), rel:Dot(u),
			barrel:Dot(f), barrel:Dot(r), barrel:Dot(u)))
	end
	local att = ply:LookupAttachment("anim_attachment_RH")
	local a = att and att > 0 and ply:GetAttachment(att)
	local aim, up = ply:GetAimVector(), Vector(0, 0, 1)
	local function axes(label, ang, pos)
		print(string.format("[kenny_dbg] %s F.(aim,up)=%.2f %.2f R.(aim,up)=%.2f %.2f U.(aim,up)=%.2f %.2f pos-hand=%s", label,
			ang:Forward():Dot(aim), ang:Forward():Dot(up), ang:Right():Dot(aim), ang:Right():Dot(up), ang:Up():Dot(aim), ang:Up():Dot(up), tostring(pos)))
	end
	local hb = ply:LookupBone("ValveBiped.Bip01_R_Hand")
	local hm = hb and ply:GetBoneMatrix(hb)
	if a and hm then axes("RH attachment", a.Ang, a.Pos - hm:GetTranslation()) end
	if hm then axes("R_Hand bone", hm:GetAngles(), vector_origin) end
	if IsValid(w) and w.KennyWMAng then
		local a = w.KennyWMAng
		local barrel, up = -a:Right(), a:Up()
		local bf = ply:GetAngles() -- body yaw
		print(string.format("[kenny_dbg] world barrel.(bodyF,bodyR,up)=%.2f %.2f %.2f  kennyUp.(bodyF,bodyR,up)=%.2f %.2f %.2f",
			barrel:Dot(bf:Forward()), barrel:Dot(bf:Right()), barrel.z, up:Dot(bf:Forward()), up:Dot(bf:Right()), up.z))
	end
end)

local flashGlow = Material("sprites/light_glow02_add")

-- the hidden HL2 pistol's own animation events: orange muzzle flash, brass shell casings. Kenny has neither.
function SWEP:FireAnimationEvent(pos, ang, event, options)
	return true
end

function SWEP:PostDrawViewModel(vm)
	vm:SetMaterial("")
	-- sit Kenny where the HL2 pistol sat, following the pistol body ("square") so draw/recoil/reload move
	-- him with the hand. His grip centre goes where the magazine sits at rest (inside the pistol grip, in
	-- the fist). Not on the magazine bone itself: it drops out of the gun during the reload animation.
	local sq = vm:LookupBone("ValveBiped.square")
	local msq = sq and vm:GetBoneMatrix(sq)
	if not msq then return end
	-- on that bone the barrel is its Up axis and the top of the gun is its -Right axis (measured in game)
	local sqAng = msq:GetAngles()
	local gunF, gunU = sqAng:Up(), -sqAng:Right()
	local ang = gunF:AngleEx(gunU)
	local gunR = ang:Right()
	-- studiomdl turned the model 90 degrees (barrel along +Y): yaw it back, then user tweaks
	ang:RotateAroundAxis(ang:Up(), -90 + cv.yaw:GetFloat())
	ang:RotateAroundAxis(ang:Right(), cv.p:GetFloat())
	ang:RotateAroundAxis(ang:Forward(), cv.r:GetFloat())
	local scale = cv.scale:GetFloat()
	local grip = LocalToWorld(GRIP_CENTER * scale, angle_zero, vector_origin, ang)
	local magazine = msq:GetTranslation() + gunF * MAG_OFFSET.x + gunR * MAG_OFFSET.y + gunU * MAG_OFFSET.z
	local pos = magazine - grip + gunF * cv.x:GetFloat() + gunR * cv.y:GetFloat() + gunU * cv.z:GetFloat()

	if cv.closeup:GetBool() then -- debug: Kenny's face filling the screen, looking at the camera
		local ea = EyeAngles()
		ang = Angle(ea.p, ea.y, ea.r)
		ang:RotateAroundAxis(ang:Up(), -90)
		pos = EyePos() + ea:Forward() * cv.closedist:GetFloat() - ea:Up() * 5.6 * scale
	end

	local k = self:GetKennyVM()
	k:SetPos(pos)
	k:SetAngles(ang)
	k:SetModelScale(scale)
	self:AnimateFace(k)
	k:InvalidateBoneCache() -- otherwise the bones keep the first frame's transform and Kenny never turns
	k:SetupBones()
	k:DrawModel()

	-- remember where Kenny's barrel tip appears on screen, so our globs can be drawn leaving it. The viewmodel
	-- has its own FOV, so: take the tip's pixel from this (viewmodel) render, then cast that pixel back into
	-- the world view at the same depth.
	local tip = k:LookupBone("barrelTip_02_JNT")
	local mt = tip and k:GetBoneMatrix(tip)
	if mt then
		local eye, ea = EyePos(), EyeAngles()
		local tipPos = mt:GetTranslation()
		local scr = tipPos:ToScreen()
		local depth = (tipPos - eye):Dot(ea:Forward())
		local dir = util.AimVector(ea, LocalPlayer():GetFOV(), scr.x, scr.y, ScrW(), ScrH())
		self.KennyMuzzle = eye + dir * (depth / math.max(dir:Dot(ea:Forward()), 0.1))
		self.KennyMuzzleTime = CurTime()
		-- muzzle flash: a quick green slime burst at the barrel end, drawn over Kenny so it reads as leaving him
		-- (fire times come from networked vars: in single player the attack code only runs on the server)
		local globAt = self:GetGlobReadyAt() - self.Secondary.Cooldown
		local big = globAt > self:GetLastShot()
		local since = CurTime() - math.max(self:GetLastShot(), globAt)
		if since >= 0 and since < 0.07 then
			local f = 1 - since / 0.07
			local size = (big and 7 or 4.5) * (0.6 + 0.4 * f)
			cam.IgnoreZ(true)
			render.SetMaterial(flashGlow)
			render.DrawSprite(tipPos, size * 1.8, size * 1.8, Color(110, 255, 60, 220 * f))
			render.DrawSprite(tipPos, size * 0.7, size * 0.7, Color(220, 255, 180, 255 * f))
			cam.IgnoreZ(false)
		end
		if cv.showmuzzle:GetBool() then -- debug: mark the barrel tip, drawn through everything
			cam.IgnoreZ(true)
			render.SetMaterial(Material("sprites/light_glow02_add"))
			render.DrawSprite(mt:GetTranslation(), 3, 3, Color(255, 0, 255))
			cam.IgnoreZ(false)
		end
	end
end

-- third person / other players: Kenny's grip on the hand's weapon attachment (its forward = aim, up = up)
local wm_off = { pos = Vector(3.2, -1.2, -3.2), ang = Angle(-8, 90, 180) }
function SWEP:DrawWorldModel()
	local owner = self:GetOwner()
	local pos, ang = self:GetPos(), self:GetAngles() -- dropped on the ground: where the entity is
	if IsValid(owner) then
		local att = owner:LookupAttachment("anim_attachment_RH")
		local a = att and att > 0 and owner:GetAttachment(att)
		if a then
			ang = Angle(a.Ang.p, a.Ang.y, a.Ang.r)
			ang:RotateAroundAxis(ang:Up(), -90) -- studiomdl put his barrel on +Y
			pos = a.Pos - LocalToWorld(GRIP_CENTER, angle_zero, vector_origin, ang)
		else
			local bone = owner:LookupBone("ValveBiped.Bip01_R_Hand")
			local m = bone and owner:GetBoneMatrix(bone)
			if m then pos, ang = LocalToWorld(wm_off.pos, wm_off.ang, m:GetTranslation(), m:GetAngles()) end
		end
	end
	self.KennyWMAng = ang

	local k = self:GetKennyWM()
	k:SetPos(pos)
	k:SetAngles(ang)
	self:AnimateFace(k)
	k:InvalidateBoneCache()
	k:SetupBones()
	k:DrawModel()
end


-- debug: kenny_debug_tpcam 1 = third-person view looking straight at Kenny's face in your hand
local tpcam = CreateClientConVar("kenny_debug_tpcam", "0", false)
hook.Add("CalcView", "kenny_debug_tpcam", function(ply, origin, angles, fov)
	if not tpcam:GetBool() then return end
	local att = ply:LookupAttachment("anim_attachment_RH")
	local a = att and att > 0 and ply:GetAttachment(att)
	if not a then return end
	local f = ply:EyeAngles():Forward()
	f.z = 0
	local target = a.Pos + Vector(0, 0, 4)
	-- his face looks back at whoever holds him: look over the right shoulder
	f:Normalize()
	local eye = target - f * 14 + f:Cross(Vector(0, 0, 1)) * 9 + Vector(0, 0, 5)
	return { origin = eye, angles = (target - eye):Angle(), fov = 55, drawviewer = true }
end)

-- glob shot charge indicator
function SWEP:DrawHUD()
	local ready = self:GetGlobReadyAt()
	local frac = math.Clamp(1 - (ready - CurTime()) / self.Secondary.Cooldown, 0, 1)
	local w, h = 120, 6
	local x, y = ScrW() / 2 - w / 2, ScrH() / 2 + 40
	surface.SetDrawColor(0, 0, 0, 140)
	surface.DrawRect(x, y, w, h)
	surface.SetDrawColor(frac >= 1 and Color(140, 255, 80) or Color(90, 160, 60))
	surface.DrawRect(x, y, w * frac, h)
end

function SWEP:DrawWeaponSelection(x, y, w, h, a)
	local icon = self.KennyIcon or Material("entities/weapon_kenny.png")
	self.KennyIcon = icon
	surface.SetDrawColor(255, 255, 255, a)
	surface.SetMaterial(icon)
	local s = math.min(w, h)
	surface.DrawTexturedRect(x + (w - s) / 2, y + (h - s) / 2, s, s)
end

killicon.Add("weapon_kenny", "entities/weapon_kenny.png", Color(255, 255, 255))
killicon.Add("kenny_glob", "entities/weapon_kenny.png", Color(255, 255, 255))
