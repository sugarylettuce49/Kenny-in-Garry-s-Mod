// Compiled with GMod's bin/studiomdl.exe next to kenny_ref.smd / kenny_phys.smd / kenny.vta (tools/glb_to_smd.py)
$modelname "weapons/kenny/w_kenny.mdl"
$cdmaterials "models/weapons/kenny/"
$surfaceprop "flesh"
$model "kenny" "kenny_ref.smd" {
	// his real facial expressions from High on Life, baked as shape keys
	flexfile "kenny.vta" {
		defaultflex frame 0
		flex "neutral" frame 1
		flex "sarcastic" frame 2
		flex "happy" frame 3
		flex "wicked" frame 4
		flex "angry" frame 5
		flex "excited" frame 6
		flex "lipsclosed" frame 7
	}
	flexcontroller expression range 0 1 neutral
	flexcontroller expression range 0 1 sarcastic
	flexcontroller expression range 0 1 happy
	flexcontroller expression range 0 1 wicked
	flexcontroller expression range 0 1 angry
	flexcontroller expression range 0 1 excited
	flexcontroller mouth range 0 1 lipsclosed
	%neutral = neutral
	%sarcastic = sarcastic
	%happy = happy
	%wicked = wicked
	%angry = angry
	%excited = excited
	%lipsclosed = lipsclosed
}
$sequence "idle" "kenny_ref.smd" fps 1
$attachment "muzzle" "barrelTip_02_JNT" 0 0 0 rigid
$illumposition 4 0 4
$collisionmodel "kenny_phys.smd"
{
	$mass 3
	$concave
}
