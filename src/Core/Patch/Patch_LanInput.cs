using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WKMPMod.UI;

namespace WKMPMod.Patch;

[HarmonyPatch]
public class Patch_LanPlayerLocks {
	private static IEnumerable<MethodBase> TargetMethods() {
		yield return AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.IsInputLocked));
		yield return AccessTools.Method(typeof(ENT_Player), nameof(ENT_Player.IsMoveLocked));
	}
	private static bool Prefix(MethodBase __originalMethod, ref bool __result) {
		if (!LanMenu.BlocksGameplayInput) return true;
		if (__originalMethod.Name == nameof(ENT_Player.IsMoveLocked) && Gameplay.CoopMenuSimulation.Enabled) return true;
		__result = true;
		return false;
	}
}

[HarmonyPatch(typeof(ENT_Player), "MouseLook")]
public class Patch_LanMouseLook {
	private static bool Prefix() => !LanMenu.BlocksGameplayInput && !Gameplay.CoopMenuSimulation.MenuOpen;
}

[HarmonyPatch]
public class Patch_LanInputVectors {
	private static IEnumerable<MethodBase> TargetMethods() {
		yield return AccessTools.PropertyGetter(typeof(InputManager), nameof(InputManager.MoveInput));
		yield return AccessTools.Method(typeof(InputManager), nameof(InputManager.GetLookVector));
	}
	private static bool Prefix(ref Vector2 __result) {
		if (!LanMenu.BlocksGameplayInput && Gameplay.CoopMenuSimulation.PhysicsDepth == 0) return true;
		__result = Vector2.zero;
		return false;
	}
}

[HarmonyPatch]
public class Patch_LanActionButtons {
	private static IEnumerable<MethodBase> TargetMethods() {
		foreach (var property in new[] { "Pressed", "Down", "Up" })
			yield return AccessTools.PropertyGetter(typeof(InputManager.ButtonInput), property);
	}
	private static bool Prefix(ref bool __result) {
		if (!LanMenu.BlocksGameplayInput && Gameplay.CoopMenuSimulation.PhysicsDepth == 0) return true;
		__result = false;
		return false;
	}
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetButton))]
public static class Patch_ChatButtonInput {
	private static readonly InputManager.ButtonInput empty = new InputManager.ButtonInput();
	private static bool Prefix(ref InputManager.ButtonInput __result) { if (!MultiplayerChat.BlocksGameplayInput) return true; __result = empty; return false; }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.UpdateInputs))]
public static class Patch_ChatInputBuffer {
	private static readonly AccessTools.FieldRef<InputManager, List<InputManager.ButtonInput>> buttons = AccessTools.FieldRefAccess<InputManager, List<InputManager.ButtonInput>>("buttonInputs");
	private static void Postfix(InputManager __instance) { if (MultiplayerChat.BlocksGameplayInput && buttons(__instance) != null) foreach (var button in buttons(__instance)) { button.Pressed = false; button.Down = false; button.Up = false; } }
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.Jump))]
public static class Patch_ChatJump {
	private static bool Prefix() => !MultiplayerChat.BlocksGameplayInput;
}
