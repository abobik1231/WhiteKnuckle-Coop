using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Gameplay;
using WKMPMod.NetWork;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(ENT_Player), "Movement")]
public static class Patch_ChainMovement {
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
		var move = AccessTools.Method(typeof(CharacterController), nameof(CharacterController.Move));
		var constrained = AccessTools.Method(typeof(PlayerChain), nameof(PlayerChain.MovePlayer));
		int replaced = 0;
		foreach (var instruction in instructions) {
			if (instruction.Calls(move)) { instruction.opcode = OpCodes.Call; instruction.operand = constrained; replaced++; }
			yield return instruction;
		}
		if (replaced != 1) throw new InvalidOperationException("Unexpected player movement method: " + replaced);
	}
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.IsHanging))]
public static class Patch_ChainHanging {
	private static bool Prefix(ENT_Player __instance, ref bool __result) {
		if (!MPSteamworks.Instance.IsLan || __instance.hands == null) return true;
		__result = false;
		foreach (var hand in __instance.hands) if (hand.interactState == ENT_Player.InteractType.hanging && (!PlayerChain.MobileHeld(hand) || CoopPlayerGrip.NativeHang(hand))) { __result = true; break; }
		return false;
	}
}

[HarmonyPatch(typeof(ENT_Player), "Hang")]
public static class Patch_ChainHangForces {
	private static void Prefix(ENT_Player __instance, out int __state) {
		__state = 0;
		if (!MPSteamworks.Instance.IsLan || __instance.hands == null) return;
		for (int i = 0; i < __instance.hands.Length; i++) {
			var hand = __instance.hands[i];
			if (hand.interactState != ENT_Player.InteractType.hanging || !PlayerChain.MobileHeld(hand) || CoopPlayerGrip.NativeHang(hand)) continue;
			__state |= 1 << i; hand.interactState = ENT_Player.InteractType.none;
		}
	}
	private static Exception Finalizer(ENT_Player __instance, int __state, Exception __exception) {
		if (__instance.hands != null) for (int i = 0; i < __instance.hands.Length; i++) {
			var hand = __instance.hands[i];
			if ((__state & 1 << i) != 0 && hand.interactState == ENT_Player.InteractType.none && PlayerChain.MobileHeld(hand)) hand.interactState = ENT_Player.InteractType.hanging;
		}
		return __exception;
	}
}

