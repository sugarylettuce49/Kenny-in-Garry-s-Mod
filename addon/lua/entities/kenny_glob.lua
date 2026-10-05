-- Kenny's slime glob. Small ones fly straight and fast; the big Glob Shot arcs and launches things on impact.
AddCSLuaFile()

ENT.Type = "anim"
ENT.PrintName = "Kenny Glob"
ENT.Spawnable = false
ENT.RenderGroup = RENDERGROUP_TRANSLUCENT
KENNY_GLOB_GRAVITY = 0.3 -- big Glob Shot only; small globs fly straight
CreateConVar("kenny_debug_slowglob", "0", FCVAR_REPLICATED, "debug: globs fly at 250 u/s")

function ENT:SetupDataTables()
	self:NetworkVar("Bool", 0, "Big")
end

function ENT:Initialize()
	self:SetModel("models/dav0r/hoverball.mdl")
	-- moved by hand in Think (a swept trace per tick): exact aim, no engine movetype surprises
	self:SetMoveType(MOVETYPE_NONE)
	self:SetSolid(SOLID_NONE)
	if SERVER then self.DieAt = CurTime() + 5 end
end

function ENT:Launch(vel)
	self.Vel = vel
end

-- what the glob's velocity is right now (the test harness and effects read this)
function ENT:GetGlobVelocity() return self.Vel or vector_origin end

if SERVER then
	function ENT:Think()
		if CurTime() > self.DieAt then self:Remove() return end
		if self.Hit or not self.Vel then self:NextThink(CurTime()) return true end
		local dt = engine.TickInterval()
		if self:GetBig() then self.Vel.z = self.Vel.z - GetConVar("sv_gravity"):GetFloat() * KENNY_GLOB_GRAVITY * dt end
		local from, to = self:GetPos(), self:GetPos() + self.Vel * dt
		local r = self:GetBig() and 4 or 1
		local owner = self:GetOwner()
		local tr = util.TraceHull({ start = from, endpos = to, mins = Vector(-r, -r, -r), maxs = Vector(r, r, r),
			filter = function(e) return e ~= self and e ~= owner and e:GetClass() ~= "kenny_glob" end, mask = MASK_SHOT })
		if tr.Hit then
			self:SetPos(tr.HitPos)
			self:Impact(tr)
		else
			self:SetPos(to)
		end
		self:NextThink(CurTime())
		return true
	end

	function ENT:Impact(tr)
		self.Hit = true
		local ent = tr.Entity
		local owner = IsValid(self:GetOwner()) and self:GetOwner() or self
		local dir = self.Vel:GetNormalized()
		local pos = tr.HitPos

		if IsValid(ent) then
			local dmg = DamageInfo()
			dmg:SetDamage(self.Damage or 14)
			dmg:SetAttacker(owner)
			dmg:SetInflictor(self)
			dmg:SetDamageType(DMG_BULLET)
			dmg:SetDamagePosition(pos)
			dmg:SetDamageForce(dir * (self:GetBig() and 8000 or 2500))
			-- the hull trace carries the hitgroup, so headshots count like bullets
			if ent:IsNPC() or ent:IsPlayer() then
				local line = util.TraceLine({ start = pos - dir * 16, endpos = pos + dir * 32, filter = self })
				ent:DispatchTraceAttack(dmg, line.Entity == ent and line or tr, dir)
			else
				ent:TakeDamageInfo(dmg)
			end
		end

		hook.Run("KennyGlobHit", self, pos, owner, ent)
		if self:GetBig() then self:GlobBurst(pos, owner) end

		local ed = EffectData()
		ed:SetOrigin(pos)
		ed:SetNormal(tr.HitNormal)
		ed:SetScale(self:GetBig() and 3 or 1)
		util.Effect("kenny_splat", ed, true, true)
		if tr.HitWorld or (IsValid(ent) and not ent:IsNPC() and not ent:IsPlayer()) then
			util.Decal(self:GetBig() and "Antlion.Splat" or "YellowBlood", pos + tr.HitNormal, pos - tr.HitNormal, self)
		end

		local t = KENNY_SFX and KENNY_SFX[self:GetBig() and "glob_impact" or "impact"]
		if t then sound.Play(t[math.random(#t)], pos, self:GetBig() and 85 or 70, math.random(95, 105), 1) end
		self:Remove()
	end

	-- Glob Shot: throw everything nearby up into the air, like in the game
	function ENT:GlobBurst(pos, owner)
		local hitAny = false
		for _, e in ipairs(ents.FindInSphere(pos, 170)) do
			if e == owner or e == self then continue end
			local up = Vector(0, 0, 1)
			local push = (e:WorldSpaceCenter() - pos):GetNormalized() * 120 + up * 520
			if e:IsNPC() or e:IsNextBot() then
				hitAny = true
				e:SetGroundEntity(NULL)
				if e.SetVelocity then e:SetVelocity(push) end
				if e:IsNPC() and e:GetMoveType() == MOVETYPE_STEP then e:SetPos(e:GetPos() + up * 4) e:SetVelocity(push) end
				local d = DamageInfo()
				d:SetDamage(10) d:SetAttacker(owner) d:SetInflictor(self) d:SetDamageType(DMG_CLUB)
				d:SetDamageForce(push * 60)
				e:TakeDamageInfo(d)
			elseif e:IsPlayer() then
				e:SetVelocity(push)
			else
				local phys = e:GetPhysicsObject()
				if IsValid(phys) and phys:IsMoveable() then
					phys:Wake()
					phys:ApplyForceCenter(push * phys:GetMass() * 1.2)
					phys:AddAngleVelocity(VectorRand() * 300)
				end
			end
		end
		if hitAny and IsValid(owner) and owner:IsPlayer() then KENNY.Say(owner, "stagger", 0.35, 12) end
	end
end

if CLIENT then
	local glow = Material("sprites/light_glow02_add")
	local blob = Material("sprites/glow04_noz")
	local trail = Material("trails/laser")
	local BLEND_DIST = 300 -- units of flight over which the drawn glob merges from Kenny's barrel onto its real path

	-- in first person the glob really starts beside your eye; draw it leaving Kenny's barrel instead
	function ENT:FirstPersonOffset()
		if self.MuzzleOffset == nil then
			self.MuzzleOffset = false
			local owner = self:GetOwner()
			local w = IsValid(owner) and owner == LocalPlayer() and not owner:ShouldDrawLocalPlayer() and owner:GetActiveWeapon()
			if IsValid(w) and w.KennyMuzzle and CurTime() - (w.KennyMuzzleTime or 0) < 0.25 then
				self.MuzzleOffset = w.KennyMuzzle - self:GetPos()
			end
			self.FirstPos = self:GetPos()
			self.FirstTime = CurTime()
		end
		if not self.MuzzleOffset then return vector_origin, 1 end
		-- fading the offset linearly with distance flown draws a straight line from the barrel tip to the
		-- real path BLEND_DIST further on (i.e. toward the crosshair), never across his face
		-- hold it at the barrel for its first couple of frames (at 3500 u/s it would otherwise be ~60 units
		-- away by the second frame), then fly the straight line from there
		if CurTime() - self.FirstTime < 0.025 then return self.MuzzleOffset + (self.FirstPos - self:GetPos()), 0.3 end
		local a = math.Clamp(self:GetPos():Distance(self.FirstPos) / BLEND_DIST, 0, 1)
		-- also start small: right at the barrel it is only a few units from the camera
		return self.MuzzleOffset * (1 - a), 0.3 + 0.7 * math.min(a * 3, 1)
	end

	function ENT:Draw() end
	function ENT:DrawTranslucent()
		local big = self:GetBig()
		local off, grow = self:FirstPersonOffset()
		local s = (big and 1 or 0.4) * grow
		local pos = self:GetPos() + off

		-- short trail through the last drawn positions
		self.Trail = self.Trail or {}
		table.insert(self.Trail, 1, { pos, CurTime() })
		local life = big and 0.25 or 0.12
		while #self.Trail > 1 and CurTime() - self.Trail[#self.Trail][2] > life do table.remove(self.Trail) end
		if #self.Trail > 1 then
			render.SetMaterial(trail)
			render.StartBeam(#self.Trail)
			for i, p in ipairs(self.Trail) do
				local f = 1 - (CurTime() - p[2]) / life
				render.AddBeam(p[1], (big and 14 or 5) * f * grow, i / #self.Trail, Color(120, 255, 70, 200 * f))
			end
			render.EndBeam()
		end

		render.SetMaterial(glow)
		render.DrawSprite(pos, 40 * s, 40 * s, Color(110, 255, 60, 200))
		render.SetMaterial(blob)
		render.DrawSprite(pos, 16 * s, 16 * s, Color(200, 255, 150, 255))
	end
end
