using HarmonyLib;
using UnityEngine;
using WKMPMod.Gameplay;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(ENT_Player), "HandStamina")]
public static class Patch_ChainStamina {
	[HarmonyPrefix]
	private static void Before(ENT_Player.Hand curhand, out float __state) => __state = curhand.gripStrength;
	[HarmonyPostfix]
	private static void After(ENT_Player.Hand curhand, float __state) {
		if (!PlayerChain.SupportingPlayer || !PlayerChain.SurfaceHeld(curhand)) return;
		float spent = __state - curhand.gripStrength;
		if (spent > 0) curhand.gripStrength = Mathf.Max(0, curhand.gripStrength - spent);
	}
}
