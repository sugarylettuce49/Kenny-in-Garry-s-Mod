-- Green slime burst where a Kenny glob lands.
function EFFECT:Init(data)
	local pos, normal, scale = data:GetOrigin(), data:GetNormal(), math.max(data:GetScale(), 1)
	local em = ParticleEmitter(pos)
	if not em then return end
	for i = 1, 10 * scale do
		local p = em:Add("sprites/light_glow02_add", pos)
		if p then
			p:SetVelocity((normal + VectorRand() * 0.8):GetNormalized() * math.Rand(60, 180) * scale)
			p:SetGravity(Vector(0, 0, -500))
			p:SetDieTime(math.Rand(0.4, 0.8))
			p:SetStartAlpha(230)
			p:SetEndAlpha(0)
			p:SetStartSize(math.Rand(2, 4) * scale)
			p:SetEndSize(math.Rand(6, 10) * scale)
			p:SetRoll(math.Rand(0, 360))
			p:SetColor(110, 230, 50)
			p:SetCollide(true)
		end
	end
	local p = em:Add("sprites/light_glow02_add", pos)
	if p then
		p:SetDieTime(0.15)
		p:SetStartAlpha(255)
		p:SetEndAlpha(0)
		p:SetStartSize(20 * scale)
		p:SetEndSize(40 * scale)
		p:SetColor(140, 255, 80)
	end
	em:Finish()
	local dl = DynamicLight(0)
	if dl then
		dl.pos, dl.r, dl.g, dl.b, dl.brightness = pos, 120, 255, 60, 2
		dl.decay, dl.size, dl.dietime = 1000, 120 * scale, CurTime() + 0.2
	end
end

function EFFECT:Think() return false end
function EFFECT:Render() end
