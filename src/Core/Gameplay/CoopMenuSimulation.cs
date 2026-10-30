using System;
using HarmonyLib;
using UnityEngine;
using WKMPMod.Core;
using WKMPMod.NetWork;
using WKMPMod.UI;

namespace WKMPMod.Gameplay;

public static class CoopMenuSimulation {
	public static int PhysicsDepth;
	public static bool Enabled => MPCore.CanSync && MPSteamworks.Instance.IsLan && !ManualSaves.Loading && !WorldLayoutSync.WaitingForMap && CL_GameManager.gMan != null && !CL_GameManager.IsLoading() && !CL_GameManager.gMan.IsReviving() && ENT_Player.GetPlayer() != null && !ENT_Player.GetPlayer().IsDead();
	public static bool MenuOpen => Enabled && (CL_GameManager.gMan.isPaused || PhysicsDepth > 0);
}

[HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.SetTimescale))]
public static class Patch_CoopMenuTimescale {
	private static void Prefix(ref float m) {
		if (m >= 1) return;
		if (CoopMenuSimulation.Enabled && CL_GameManager.gMan.isPaused || MPCore.IsInLobby && MPSteamworks.Instance.IsLan && CoopSpectator.Active && !ManualSaves.Loading && !CoopEnding.Running) m = 1;
	}
}

[HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.Pause))]
public static class Patch_CoopPauseAudio {
	private static void Postfix() { if (CoopMenuSimulation.Enabled) AudioListener.pause = false; }
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.isFrozen))]
public static class Patch_CoopMenuFrozen {
	private static readonly AccessTools.FieldRef<ENT_Player, bool> paused = AccessTools.FieldRefAccess<ENT_Player, bool>("paused");
	private static void Postfix(ENT_Player __instance, ref bool __result) { if (CoopMenuSimulation.MenuOpen && !paused(__instance) && !CL_GameManager.gMan.freecam && !__instance.IsDead()) __result = false; }
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.FixedUpdate))]
public static class Patch_CoopMenuPhysics {
	private static bool Prefix(out bool __state) { __state = CoopMenuSimulation.MenuOpen; if (ManualSaves.GroupLoading && !ManualSaves.Loading) { __state = false; return false; } if (__state) { CoopMenuSimulation.PhysicsDepth++; CL_GameManager.gMan.isPaused = false; } return true; }
	private static Exception Finalizer(Exception __exception, bool __state) { if (__state) { CoopMenuSimulation.PhysicsDepth = Math.Max(0, CoopMenuSimulation.PhysicsDepth - 1); if (CL_GameManager.gMan != null) CL_GameManager.gMan.isPaused = true; } return __exception; }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetVector))]
public static class Patch_CoopMenuMovement {
	private static readonly InputManager.VectorInput empty = new InputManager.VectorInput();
	private static bool Prefix(string name, ref InputManager.VectorInput __result) {
		if (!CoopMenuSimulation.Enabled || name != "Move" || !CoopMenuSimulation.MenuOpen && !Inventory.IsShowingInventory() && !LanMenu.BlocksGameplayInput) return true;
		__result = empty; return false;
	}
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.GetSlowTimeBuffMult))]
public static class Patch_CoopInventoryTime {
	private static void Postfix(ENT_Player __instance, ref float __result) { if (CoopMenuSimulation.Enabled && Inventory.IsShowingInventory()) __result *= 1 + __instance.curBuffs.GetBuff("slowTimeInventory"); }
}
