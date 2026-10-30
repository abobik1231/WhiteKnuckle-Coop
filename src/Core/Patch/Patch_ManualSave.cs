using HarmonyLib;
using WKMPMod.UI;

namespace WKMPMod.Patch;

[HarmonyPatch(typeof(CL_SaveManager), nameof(CL_SaveManager.LoadSessionFromFile))]
public static class Patch_ManualSave {
	private static bool Prefix(ref CL_SaveManager.SessionSave __result) {
		if (ManualSaves.PendingNativeSave == null) return true;
		__result = ManualSaves.PendingNativeSave;
		return false;
	}
}

[HarmonyPatch(typeof(M_Level), nameof(M_Level.IsLastLevel))]
public static class Patch_ManualSaveLastLevel {
	private static void Postfix(M_Level __instance, ref bool __result) {
		if (__result && ManualSaves.Loading && !string.IsNullOrEmpty(ManualSaves.LoadTargetLevel) && __instance.levelName != ManualSaves.LoadTargetLevel) __result = false;
	}
}

