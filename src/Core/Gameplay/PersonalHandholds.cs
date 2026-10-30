using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WKMPMod.NetWork;

namespace WKMPMod.Gameplay;

public sealed class PersonalHandholds : IDisposable {
	private static int depth;
	public static bool Active => depth > 0;
	public PersonalHandholds() { depth++; }
	public void Dispose() { depth = Math.Max(0, depth - 1); }
	public static bool Contains(UnityEngine.Component item) => item != null && item.GetComponentInParent<CL_Handhold_Breakable>(true) != null;
}

[HarmonyPatch(typeof(CL_Handhold_Breakable), "Start")]
public static class Patch_PersonalHandholdState {
	private static void Prefix(CL_Handhold_Breakable __instance) {
		if (!MPSteamworks.Instance.IsLan) return;
		AccessTools.Field(typeof(CL_Handhold), "hands").SetValue(__instance, new List<ENT_Player.Hand>());
		AccessTools.Field(typeof(CL_Handhold_Breakable), "handHoldTime").SetValue(__instance, new Dictionary<ENT_Player.Hand, float>());
	}
}

[HarmonyPatch(typeof(CL_Handhold_Breakable), "Update")]
public static class Patch_PersonalHandholdEffects {
	private static void Prefix(out PersonalHandholds __state) { __state = MPSteamworks.Instance.IsLan ? new PersonalHandholds() : null; }
	private static Exception Finalizer(PersonalHandholds __state, Exception __exception) { __state?.Dispose(); return __exception; }
}
