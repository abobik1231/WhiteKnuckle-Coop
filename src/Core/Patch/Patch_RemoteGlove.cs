using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WKMPMod.Component;

namespace WKMPMod.Patch;

[HarmonyPatch]
public static class Patch_RemoteGlove {
	private static IEnumerable<MethodBase> TargetMethods() { foreach (string name in new[] { "Start", "OnEnable", "OnDisable", "SetActivationState" }) yield return AccessTools.Method(typeof(HandItem_Glove), name); }
	private static bool Prefix(HandItem_Glove __instance) => __instance.GetComponentInParent<RemoteHand>(true) == null && __instance.GetComponentInParent<RPContainerRef>(true) == null;
}
