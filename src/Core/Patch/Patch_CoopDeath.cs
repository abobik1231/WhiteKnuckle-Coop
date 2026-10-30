using HarmonyLib;
using WKMPMod.Core;
using WKMPMod.Gameplay;
using WKMPMod.NetWork;
using WKMPMod.UI;
using System.Collections;
using DarkMachine.AI;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(AIC_Teeth_Chase), "KillPlayerAnimation")]
public static class Patch_TeethDeathAnimation {
	private static bool Prefix(ref IEnumerator __result) {
		if (!MPCore.IsInLobby || !MPSteamworks.Instance.IsLan) return true;
		__result = Empty();
		return false;
	}
	private static IEnumerator Empty() { yield break; }
}

[HarmonyPatch(typeof(CL_GameManager), nameof(CL_GameManager.Die))]
public static class Patch_CoopDeath {
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(CL_GameManager __instance) {
		if (ManualSaves.Loading) return false;
		if (!MPCore.IsInLobby || !MPSteamworks.Instance.IsLan) return true;
		AccessTools.Field(typeof(CL_GameManager), "dead").SetValue(__instance, true);
		CoopSpectator.Enter();
		return false;
	}
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.Kill))]
public static class Patch_LoadDeathGuard {
	[HarmonyPriority(Priority.First)]
	private static bool Prefix() => !ManualSaves.Loading && !ManualSaves.GroupLoading;
}

[HarmonyPatch(typeof(UI_ScoreScreen), nameof(UI_ScoreScreen.ShowScreen))]
public static class Patch_NoScore {
	private static bool Prefix(UI_ScoreScreen __instance) {
		__instance.StopAllCoroutines();
		__instance.gameObject.SetActive(false);
		if (CoopEnding.Running) { CoopEnding.ReachedMenu = true; return false; }
		if (CL_UIManager.instance != null) CL_UIManager.instance.SetFadeTarget(0);
		if (!ManualSaves.Loading && !CoopSpectator.Active && CL_GameManager.gMan != null) CL_GameManager.gMan.Pause();
		return false;
	}
}

[HarmonyPatch(typeof(ENT_Player), nameof(ENT_Player.Damage))]
public static class Patch_LoadDamageGuard {
	private static bool Prefix() => !ManualSaves.Loading && !ManualSaves.GroupLoading;
}
